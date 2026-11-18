using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysMonitor
{
    /// <summary>一次传感器采样。读不到的项一律是 NaN，UI 直接显示 "--"。</summary>
    internal sealed class SensorSample
    {
        public double CpuTemp = double.NaN;      // °C
        public double CpuMHz = double.NaN;       // 当前全核平均频率
        public double CpuMaxMHz = double.NaN;    // 当前可达到的最高性能档
        public double GpuTemp = double.NaN;
        public double GpuMHz = double.NaN;
        public double GpuMaxMHz = double.NaN;

        public bool HasCpu => !double.IsNaN(CpuTemp) || !double.IsNaN(CpuMHz);
        public bool HasGpu => !double.IsNaN(GpuTemp) || !double.IsNaN(GpuMHz);
    }

    /// <summary>
    /// 温度 / 频率。全部走「不需要管理员、不需要额外服务」的官方接口：
    ///
    ///   CPU 温度   PDH  \Thermal Zone Information(*)\Temperature（ACPI 热区，开尔文）
    ///   CPU 频率   PDH  \Processor Information(_Total)\Processor Frequency × % Processor Performance
    ///                  （ACPI/CPPC 上报的实时性能百分比，任务管理器的“速度”也是这个来源）
    ///   显卡       NVML（nvml.dll，随 NVIDIA 驱动安装，进程内调用约几十微秒）
    ///
    /// 刻意不用 WMI / LibreHardwareMonitor：前者每次查询毫秒级还要拉起 WmiPrvSE，
    /// 后者要装内核驱动并要求管理员（本工具的目标就是免安装、免提权）。
    /// </summary>
    internal static class Sensors
    {
        private const double Nan = double.NaN;
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const double KelvinToCelsius = 273.15;

        // 显卡查询并不便宜（要过驱动），温度变化又很慢，单独限流。
        private const double GpuIntervalSeconds = 3.0;

        private static bool _initTried;
        private static bool _pdhOk;
        private static bool _nvmlOk;

        private static IntPtr _query = IntPtr.Zero;
        private static IntPtr _cTemp, _cTempHi, _cFreq, _cPerf, _cMaxPct;
        private static IntPtr _pdhBuf = IntPtr.Zero;
        private static int _pdhBufSize;

        private static IntPtr _nvmlDev = IntPtr.Zero;
        private static uint _nvmlCount;

        private static long _gpuTick;
        private static bool _gpuSampled;
        private static double _gpuTemp = Nan, _gpuMHz = Nan, _gpuMaxMHz = Nan;

        private static double _cpuPeakMHz;

        // ---------------- P/Invoke ----------------

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterArrayW(
            IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        private static extern int NvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
        private static extern int NvmlDeviceGetCount(out uint count);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int NvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint celsius);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
        private static extern int NvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint mhz);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxClockInfo")]
        private static extern int NvmlDeviceGetMaxClockInfo(IntPtr device, int clockType, out uint mhz);

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtValue
        {
            public uint Status;
            public double Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtItem
        {
            public IntPtr Name;
            public PdhFmtValue Value;
        }

        // ---------------- 初始化 ----------------

        /// <summary>第一次用到时才加载 pdh / nvml，两样都失败也不会影响主界面。</summary>
        private static void EnsureInit()
        {
            if (_initTried) return;
            _initTried = true;

            try
            {
                _pdhOk = PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0;
                if (_pdhOk)
                {
                    // 三个频率计数器放一个 query 里，一次 PdhCollectQueryData 同一时刻取齐。
                    // 首帧的 rate 计数器没有基线，值会是无效的，后面几帧自然就有数了。
                    AddCounter(_query, @"\Processor Information(_Total)\Processor Frequency", out _cFreq);
                    AddCounter(_query, @"\Processor Information(_Total)\% Processor Performance", out _cPerf);
                    AddCounter(_query, @"\Processor Information(_Total)\% of Maximum Frequency", out _cMaxPct);

                    PdhAddEnglishCounterW(_query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _cTemp);
                    PdhAddEnglishCounterW(_query, @"\Thermal Zone Information(*)\High Precision Temperature", IntPtr.Zero, out _cTempHi);

                    if (_cFreq == IntPtr.Zero && _cTemp == IntPtr.Zero && _cTempHi == IntPtr.Zero)
                    {
                        _pdhOk = false;   // 这台机器上没有这些计数器，别白测
                    }
                    else
                    {
                        _pdhBufSize = 4096;
                        _pdhBuf = Marshal.AllocHGlobal(_pdhBufSize);
                        PdhCollectQueryData(_query);   // 打一发基线
                    }
                }
            }
            catch
            {
                _pdhOk = false;
            }

            try
            {
                if (NvmlInit() == 0 && NvmlDeviceGetCount(out _nvmlCount) == 0 && _nvmlCount > 0)
                {
                    _nvmlOk = NvmlDeviceGetHandleByIndex(0, out _nvmlDev) == 0;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch { }
        }

        private static void AddCounter(IntPtr query, string path, out IntPtr counter)
        {
            counter = IntPtr.Zero;
            try
            {
                if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out counter) != 0) counter = IntPtr.Zero;
            }
            catch { counter = IntPtr.Zero; }
        }

        // ---------------- 采样 ----------------

        public static void Sample(SensorSample s)
        {
            EnsureInit();

            var sw = Stopwatch.StartNew();
            if (_pdhOk) SampleCpu(s);
            SampleGpu(s);
            sw.Stop();
            LastCostMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>最近一次采样的耗时（毫秒），只用于自检和文档里的实测数字。</summary>
        public static double LastCostMs { get; private set; }

        private static void SampleCpu(SensorSample s)
        {
            try
            {
                PdhCollectQueryData(_query);

                double freq = ReadSingle(_cFreq);          // 标称频率（MHz），基本是常数
                double perf = ReadSingle(_cPerf);          // 相对标称的性能百分比，>100 表示睿频
                double maxPct = ReadSingle(_cMaxPct);      // 相对最高性能档的百分比

                if (freq > 0 && perf > 0)
                {
                    double now = freq * perf / 100.0;

                    // 最高性能档 = 标称频率 × (perf / maxPct)。两个值同一时刻取，除出来的比值是稳定的；
                    // 但百分比本身有抖动，用一个很慢的滑动平均把它压平，再保证不低于当前频率。
                    if (maxPct > 1)
                    {
                        double cand = freq * perf / maxPct;
                        if (cand >= freq && cand < 12000)
                        {
                            _cpuPeakMHz = _cpuPeakMHz <= 0 ? cand : _cpuPeakMHz * 0.85 + cand * 0.15;
                        }
                    }
                    if (_cpuPeakMHz <= 0) _cpuPeakMHz = freq;
                    if (now > _cpuPeakMHz) _cpuPeakMHz = now;   // 实测跑过了就抬高上限

                    s.CpuMHz = now;
                    s.CpuMaxMHz = _cpuPeakMHz;
                }

                double hi = ReadArrayMax(_cTempHi);        // 0.1 K 精度
                double t = hi > 0 ? hi / 10.0 : ReadArrayMax(_cTemp);
                if (t > 200 && t < 400)                    // 开尔文，过滤掉 0/无效实例
                {
                    s.CpuTemp = t - KelvinToCelsius;
                }
            }
            catch
            {
                // PDH 偶发失败就这一帧没有数，别把整个 tick 拖下去
            }
        }

        /// <summary>单实例计数器（_Total）。没有有效值时返回 NaN。</summary>
        private static double ReadSingle(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return Nan;
            try
            {
                uint size = (uint)_pdhBufSize;
                uint count;
                uint rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, _pdhBuf);
                if (rc != 0 && size > _pdhBufSize)
                {
                    // 缓冲区不够就换一块再问一次
                    Marshal.FreeHGlobal(_pdhBuf);
                    _pdhBufSize = (int)size + 256;
                    _pdhBuf = Marshal.AllocHGlobal(_pdhBufSize);
                    size = (uint)_pdhBufSize;
                    rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, _pdhBuf);
                }
                if (count == 0) return Nan;

                var item = Marshal.PtrToStructure<PdhFmtItem>(_pdhBuf);
                if (item.Value.Status != 0) return Nan;
                return item.Value.Value;
            }
            catch
            {
                return Nan;
            }
        }

        /// <summary>多实例计数器（每个 ACPI 热区一个实例），取最大值当 CPU 温度。</summary>
        private static double ReadArrayMax(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return Nan;
            try
            {
                uint size = (uint)_pdhBufSize;
                uint count;
                uint rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, _pdhBuf);
                if (rc != 0 && size > _pdhBufSize)
                {
                    Marshal.FreeHGlobal(_pdhBuf);
                    _pdhBufSize = (int)size + 256;
                    _pdhBuf = Marshal.AllocHGlobal(_pdhBufSize);
                    size = (uint)_pdhBufSize;
                    rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, _pdhBuf);
                }
                if (count == 0) return Nan;

                int stride = Marshal.SizeOf<PdhFmtItem>();
                double best = Nan;
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PdhFmtItem>(_pdhBuf + i * stride);
                    if (item.Value.Status != 0) continue;
                    double v = item.Value.Value;
                    if (double.IsNaN(best) || v > best) best = v;
                }
                return best;
            }
            catch
            {
                return Nan;
            }
        }

        private static void SampleGpu(SensorSample s)
        {
            if (!_nvmlOk)
            {
                s.GpuTemp = _gpuTemp;
                s.GpuMHz = _gpuMHz;
                s.GpuMaxMHz = _gpuMaxMHz;
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double age = _gpuSampled ? (now - _gpuTick) / (double)Stopwatch.Frequency : double.MaxValue;
            if (age >= GpuIntervalSeconds)
            {
                _gpuSampled = true;
                _gpuTick = now;

                try
                {
                    _gpuTemp = NvmlDeviceGetTemperature(_nvmlDev, 0, out uint t) == 0 ? t : Nan;

                    // 0 = NVML_CLOCK_GRAPHICS
                    _gpuMHz = NvmlDeviceGetClockInfo(_nvmlDev, 0, out uint clk) == 0 ? clk : Nan;
                    _gpuMaxMHz = NvmlDeviceGetMaxClockInfo(_nvmlDev, 0, out uint max) == 0 ? max : Nan;
                }
                catch
                {
                    _gpuTemp = _gpuMHz = _gpuMaxMHz = Nan;
                }
            }

            s.GpuTemp = _gpuTemp;
            s.GpuMHz = _gpuMHz;
            s.GpuMaxMHz = _gpuMaxMHz;
        }

        public static void Shutdown()
        {
            try
            {
                if (_nvmlOk) NvmlShutdown();
            }
            catch { }

            try
            {
                if (_query != IntPtr.Zero) PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            catch { }

            if (_pdhBuf != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(_pdhBuf); } catch { }
                _pdhBuf = IntPtr.Zero;
            }
        }
    }
}
