using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysMonitor
{
    internal sealed class DriveSample
    {
        public string Name;      // "C:" / "D:"
        public ulong Total;
        public ulong Used;

        public double ReadBps;
        public double WriteBps;
        public bool HasIo;       // 第一次采样没有基线，先显示 "--"

        public double Percent => Total == 0 ? 0 : Used * 100.0 / Total;
    }

    /// <summary>
    /// 全部指标都用 Win32 / 托管 API 直接取数，不走 WMI、不建性能计数器实例。
    /// 单次采集成本在几十微秒量级。
    /// </summary>
    internal static class Metrics
    {
        // ---------------- 物理内存 ----------------

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static void ReadMemory(out ulong used, out ulong total)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0)
            {
                total = m.ullTotalPhys;
                used = total - m.ullAvailPhys;
            }
            else
            {
                used = 0;
                total = 0;
            }
        }

        // ---------------- 磁盘容量 + 每卷读写速率 ----------------

        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_READ = 0x0001;
        private const uint FILE_SHARE_WRITE = 0x0002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;   // CTL_CODE(IOCTL_DISK_BASE, 0x0008, METHOD_BUFFERED, FILE_ANY_ACCESS)
        private const int DiskPerformanceSize = 88;               // sizeof(DISK_PERFORMANCE)（含 8 字节对齐）

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
            byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        private sealed class VolumeCounter
        {
            public SafeFileHandle Handle;
            public long LastRead = -1;
            public long LastWrite;
            public long Tick;
        }

        private static readonly Dictionary<string, VolumeCounter> _volumes =
            new Dictionary<string, VolumeCounter>(StringComparer.OrdinalIgnoreCase);

        // 复用同一块缓冲区，避免每次采样都分配
        private static readonly byte[] IoBuffer = new byte[DiskPerformanceSize];

        private static DriveInfo[] _drives = Array.Empty<DriveInfo>();
        private static DateTime _drivesAt = DateTime.MinValue;

        public static List<DriveSample> ReadDrives()
        {
            // 磁盘列表变化很慢，20 秒才枚举一次，避免每秒都走一遍驱动器枚举。
            if (_drives.Length == 0 || (DateTime.UtcNow - _drivesAt).TotalSeconds > 20)
            {
                _drives = CollectDrives();
                _drivesAt = DateTime.UtcNow;
            }

            var list = new List<DriveSample>(_drives.Length);
            foreach (var d in _drives)
            {
                try
                {
                    if (!d.IsReady) continue;
                    ulong total = (ulong)d.TotalSize;
                    ulong free = (ulong)d.TotalFreeSpace;
                    var sample = new DriveSample
                    {
                        Name = d.Name.TrimEnd('\\'),
                        Total = total,
                        Used = total - free
                    };
                    FillIo(sample, d.DriveType == DriveType.Fixed);
                    list.Add(sample);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            PruneVolumes(list);
            return list;
        }

        private static DriveInfo[] CollectDrives()
        {
            var list = new List<DriveInfo>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    list.Add(d);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 打开卷句柄 → IOCTL_DISK_PERFORMANCE 拿累计读/写字节 → 差分算速率。
        /// 固定盘长期持有句柄；可移动盘每次用完就关，避免锁住 U 盘让人没法安全弹出。
        /// </summary>
        private static void FillIo(DriveSample drive, bool keepHandle)
        {
            if (!_volumes.TryGetValue(drive.Name, out VolumeCounter vol))
            {
                vol = new VolumeCounter();
                _volumes[drive.Name] = vol;
            }

            SafeFileHandle handle = null;
            try
            {
                if (keepHandle)
                {
                    if (vol.Handle == null || vol.Handle.IsInvalid)
                    {
                        vol.Handle?.Dispose();
                        vol.Handle = OpenVolume(drive.Name);
                        vol.LastRead = -1;   // 新句柄，计数器重新打基线
                    }
                    handle = vol.Handle;
                }
                else
                {
                    handle = OpenVolume(drive.Name);
                }

                if (handle == null || handle.IsInvalid)
                {
                    drive.HasIo = false;
                    return;
                }

                if (!QueryIo(handle, out long read, out long write))
                {
                    // 卷被拔掉 / 盘符复用：丢掉句柄，下一轮重开
                    if (keepHandle)
                    {
                        vol.Handle?.Dispose();
                        vol.Handle = null;
                    }
                    vol.LastRead = -1;
                    drive.HasIo = false;
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                if (vol.LastRead < 0)
                {
                    vol.LastRead = read;
                    vol.LastWrite = write;
                    vol.Tick = now;
                    drive.HasIo = false;
                    return;
                }

                double seconds = (now - vol.Tick) / (double)Stopwatch.Frequency;
                if (seconds > 0.05)
                {
                    // 计数器被重置时会回退，钳到 0 防止出现负速度
                    drive.ReadBps = Math.Max(0, (read - vol.LastRead) / seconds);
                    drive.WriteBps = Math.Max(0, (write - vol.LastWrite) / seconds);
                    drive.HasIo = true;
                }

                vol.LastRead = read;
                vol.LastWrite = write;
                vol.Tick = now;
            }
            finally
            {
                if (!keepHandle) handle?.Dispose();
            }
        }

        private static void PruneVolumes(List<DriveSample> live)
        {
            if (_volumes.Count == 0) return;

            List<string> stale = null;
            foreach (var key in _volumes.Keys)
            {
                bool found = false;
                foreach (var d in live)
                {
                    if (string.Equals(d.Name, key, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found)
                {
                    (stale ?? (stale = new List<string>())).Add(key);
                }
            }

            if (stale == null) return;
            foreach (var key in stale)
            {
                _volumes[key].Handle?.Dispose();
                _volumes.Remove(key);
            }
        }

        private static SafeFileHandle OpenVolume(string driveName)
        {
            try
            {
                // 只请求 READ_ATTRIBUTES，普通用户即可打开卷句柄，不需要管理员
                var handle = CreateFileW(
                    @"\\.\" + driveName,
                    FILE_READ_ATTRIBUTES,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    return null;
                }
                return handle;
            }
            catch
            {
                return null;
            }
        }

        private static bool QueryIo(SafeFileHandle handle, out long read, out long write)
        {
            read = 0;
            write = 0;
            try
            {
                if (!DeviceIoControl(handle, IOCTL_DISK_PERFORMANCE, IntPtr.Zero, 0,
                        IoBuffer, (uint)DiskPerformanceSize, out uint returned, IntPtr.Zero)
                    || returned < 16)
                {
                    return false;
                }

                read = BitConverter.ToInt64(IoBuffer, 0);    // DISK_PERFORMANCE.BytesRead
                write = BitConverter.ToInt64(IoBuffer, 8);   // DISK_PERFORMANCE.BytesWritten
                return read >= 0 && write >= 0;
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 网络吞吐 ----------------

        private static NetworkInterface[] _nics = Array.Empty<NetworkInterface>();
        private static DateTime _nicsAt = DateTime.MinValue;
        private static long _rxBase = -1;
        private static long _txBase;
        private static long _tickBase;

        public static void ReadNetwork(out double downBps, out double upBps)
        {
            // 网卡热插拔（插网线 / 开 Wi-Fi）不需要秒级感知，10 秒刷新一次列表足够。
            if (_nics.Length == 0 || (DateTime.UtcNow - _nicsAt).TotalSeconds > 10)
            {
                _nics = CollectNics();
                _nicsAt = DateTime.UtcNow;
                _rxBase = -1;   // 列表变了，差分基线必须重置
            }

            long rx = 0, tx = 0;
            foreach (var nic in _nics)
            {
                try
                {
                    var s = nic.GetIPStatistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
                catch (NetworkInformationException) { }
                catch (InvalidOperationException) { }
            }

            long now = Stopwatch.GetTimestamp();
            downBps = 0;
            upBps = 0;

            if (_rxBase >= 0)
            {
                double sec = (now - _tickBase) / (double)Stopwatch.Frequency;
                if (sec > 0.05)
                {
                    // 网卡被移除时累计值会回退，钳到 0 防止出现负速度。
                    downBps = Math.Max(0, (rx - _rxBase) / sec);
                    upBps = Math.Max(0, (tx - _txBase) / sec);
                }
            }

            _rxBase = rx;
            _txBase = tx;
            _tickBase = now;
        }

        private static bool IsActive(NetworkInterface n)
        {
            return n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
        }

        private static NetworkInterface[] CollectNics()
        {
            var list = new List<NetworkInterface>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (IsActive(n)) list.Add(n);
            }
            return list.ToArray();
        }
    }
}
