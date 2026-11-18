# SysMonitorFloat — Windows 悬浮监控窗

一个常驻桌面的小悬浮窗：实时显示**内存占用**、**每块硬盘的容量占用**、**每块硬盘的读写速率**、**网络上下行速率**；顶部时间正下方还有一条**传感器条——CPU / GPU 温度、当前/最高频率**（右键菜单「温度 / 频率」可整体关掉）。另有**桌面便签**（自由文字 + 待办清单，跟随当前主题），全局热键 `Ctrl+Alt+N` 新建、`Ctrl+Alt+H` 隐藏/恢复全部。
窄条竖排（卡片 128 DIP 宽，含发光边距共 138 DIP），无边框、半透明、置顶、不进 Alt+Tab、点击不抢焦点、可拖动、**拖到屏幕边缘自动磁吸/自动收起**、记住位置、可开机自启。
提供 **8 套外观**（右键菜单 → 外观）：
- **深海 HUD**（默认主题）：仪表风格——切角八边形、细角标、冷银蓝分段电平条和极轻的数据脉冲；**去掉了网格、扫描线和刻度尺**，只保留克制的仪器感。
- **暮光山脊 / 霞光山脉 / 虹彩都市 / 黑白缎影 / 玄鳞 / 绯绒客厅 / 赤焰峡谷**：7 套氛围主题，按参考图的天空、山体、人物和纹理配色生成——18 DIP 圆角、三段渐变玻璃底、顶部受光、连续药丸电平条、无辉光柔光曲线，**没有网格/扫描线/分段/脉冲/角标**。

八套都使用同一套「玻璃卡片」材质：细渐变描边、外侧极淡投影、内圈高光、顶部一道受光；配色把亮度留给时间和读数。都跟随 Windows 浅色/深色模式自动换色。

实测（本机 Ryzen 7 6800H / 16 逻辑核 / 150% DPI）：
同一份代码在不同时刻测得 **0.63%** 与 **3.4%** 单核——差 5 倍，因为测试时机器本身有 50% 的背景负载。
**采样层是稳定的 2 ms/秒；剩下全部是 WPF 每帧的固定成本，随系统繁忙程度波动。**
改版后的 HUD 用自绘替代了大量 WPF 控件，并且不挂常驻动画：**30 秒稳态实测约 1.6% 单核（整机约 0.1%）**。
启动后前 10 秒会看到 ~5%，那是 WPF 预热和首帧渲染，别把启动成本当成常驻成本。
动效（数据脉冲、LIVE 灯）只在采样那一帧改一次状态，不挂 `DoubleAnimation`，所以不会按 60 fps 持续重绘。
想再降 CPU，最直接的手段还是把刷新间隔调到 2 秒 / 5 秒（右键菜单里能切）；收起后完全不采样。

| 项目 | 实测 |
| --- | --- |
| 1 Hz 刷新时 CPU（可见，30 s 稳态） | **约 1.5%–2% 单核（整机 ~0.1%）** |
| 其中纯采样 | ~2 ms/秒 = 0.2% 单核 |
| 工作集 / 私有内存 | ~120 MB / ~72 MB（WPF 基线） |
| 磁盘容量查询 | 5 个卷合计 5.7 ms，且每 20 秒才做一次 |
| 5 个卷的读写查询 | 5 次 `DeviceIoControl`，合计 < 1 ms |

### 外观一览

**深海 HUD（默认主题，仪表风格）**

| 深色 | 浅色 |
| --- | --- |
| ![deepsea dark](docs/preview.png) | ![deepsea light](docs/preview-light.png) |

**5 套氛围主题（按参考图生成）**

| 深色模式 | 浅色模式 |
| --- | --- |
| ![dark themes](docs/themes-dark.png) | ![light themes](docs/themes-light.png) |

右键菜单里的外观选择（当前主题会打点）：

![theme menu](docs/menu-themes.png)

---

## 一、该从哪里开始：技术选型

悬浮窗这个需求，80% 的坑在**窗口行为**（透明、置顶、不抢焦点、高 DPI），20% 在**取数**。所以先选窗口技术栈：

| 方案 | 开发速度 | 常驻开销 | 适用判断 |
| --- | --- | --- | --- |
| **C# / WPF (.NET 8)** ← 本项目 | 快 | ~120 MB 内存，CPU 与刷新频率相关 | 想一两天做出可用版本，Windows 独占，选它 |
| WinUI 3 / UWP | 中 | 略低于 WPF | 想要 Mica/原生观感；透明置顶悬浮窗反而更麻烦，部署链路重 |
| Python + PySide6 | 最快 | 打包 60 MB+，常驻更高 | 只给自己用、能接受体积，`psutil` + `Qt.FramelessWindowHint` 一天能搞定 |
| Avalonia | 中 | 与 WPF 相当 | 想顺便跨平台 |
| Tauri / Electron | 慢 | 高（多进程 + WebView） | 不推荐做这种常驻小工具 |
| **纯 Win32 + Direct2D/GDI** | 慢 | **最低**（几 MB、<0.1% CPU） | 追求极致；用 C++ 自己画文字和进度条 |

结论：**先用 WPF 把功能跑通**，它把"透明切角置顶窗口 + Win32 互操作"的成本压到了最低。真到了要抠那 2% CPU 的时候，再考虑 Win32 重写——那时业务逻辑（`Metrics.cs`）可以原样搬过去。

## 二、架构

```
Program.cs        入口，只负责 new Application + new FloatWindow
Metrics.cs        纯取数：内存 / 各盘容量 / 网络吞吐（Win32 P/Invoke + 托管 API）
Sensors.cs        纯取数：温度 / 频率（PDH 性能计数器 + NVML，同样免管理员）
StickyNote.cs     桌面便签窗口：自由文字 + 待办清单，跟随主题，位置/内容/勾选状态持久化
FloatWindow.cs    无边框窗口、拖动、右键菜单、全局热键（穿透 / 便签）、位置持久化、数据绑定、系统主题监听
HudVisuals.cs     6 套外观 × 深浅两套的冻结画笔；玻璃材质、切角/圆角外框、分段/药丸电平条、网络曲线
```

**三层分离的好处**：窗口行为、取数、视觉各管一块。想换一套皮肤只改 `HudVisuals.cs` 和 `FloatWindow` 里的视觉树，
`Metrics.cs` 完全不用动；将来用 Win32 重写窗口，取数逻辑也能原样搬过去。

### 数据源怎么选（这是"性能影响小"的关键）

| 指标 | 本项目用法 | 为什么不用别的 |
| --- | --- | --- |
| 内存 | `GlobalMemoryStatusEx`（P/Invoke） | 零分配、微秒级。不要用 WMI |
| 各盘容量 | `DriveInfo.TotalSize / TotalFreeSpace`，**每 20 s 枚举一次** | 比 `GetDiskFreeSpaceEx` 省事；卷列表变化很慢，不必每秒查 |
| 各盘读写速率 | 对每个卷 `CreateFile(@"\\.\X:")` + **`IOCTL_DISK_PERFORMANCE`**，取 `BytesRead / BytesWritten` 累计值做差分 | 不用 WMI、不用性能计数器、**不需要管理员权限**；直接按盘符拿到，不用把"物理磁盘 0"映射回 C:/D: |
| 网络速率 | `NetworkInterface.GetIPStatistics()` 取累计字节，**相邻两次差分 / 实际间隔** | 比 `PerformanceCounter("Network Interface")` 轻，不用建计数器实例、不用两次采样预热 |
| 网卡列表 | `GetAllNetworkInterfaces()` 过滤 Up / 非 Loopback / 非 Tunnel，**每 10 s 刷新** | 每帧枚举网卡是主要浪费点之一 |

三条硬规则：

1. **绝对不要用 WMI**（`Win32_PerfFormattedData_*`）。单次查询 10–100 ms，还会把 `WmiPrvSE.exe` 拉起来常驻。
2. **差分算速率**：`(本次累计字节 − 上次累计字节) / 实际间隔秒数`。间隔用 `Stopwatch.GetTimestamp()` 算真实间隔，不要假设"1 秒"——定时器会漂移。
3. **变化慢的东西慢刷**：容量 20 s、网卡列表 10 s、内存和读写速率 1 s。

### 磁盘读写速率的具体做法（这次新加的）

```csharp
// 1. 打开卷句柄：只要 FILE_READ_ATTRIBUTES，普通用户就行
var h = CreateFileW(@"\\.\C:", 0x80, FILE_SHARE_READ | FILE_SHARE_WRITE,
                     IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

// 2. 拿累计计数：DISK_PERFORMANCE 结构体，88 字节（注意 8 字节对齐）
byte[] buf = new byte[88];
DeviceIoControl(h, 0x00070020 /* IOCTL_DISK_PERFORMANCE */,
                IntPtr.Zero, 0, buf, 88, out uint ret, IntPtr.Zero);

long bytesRead    = BitConverter.ToInt64(buf, 0);
long bytesWritten = BitConverter.ToInt64(buf, 8);

// 3. 和上一次采样差分 → bytes / 真实间隔
```

几个坑：

- **结构体大小必须是 88 字节**，传 80 会得到 `ERROR_INVALID_PARAMETER (87)`。`DISK_PERFORMANCE` 里有 5 个 `LARGE_INTEGER` + 4 个 `DWORD` + `QueryTime` + `StorageDeviceNumber` + `WCHAR[8]`，对齐后是 88。
- **句柄要缓存**。固定盘长期持有句柄，每秒只发一次 IOCTL；**可移动盘每次用完就关**，否则可能锁住 U 盘让人没法"安全删除硬件"。
- 计数器被重置时会**回退**，速率要钳到 0，不然会看到负速度。
- 第一次采样没有基线，显示 `R -- / W --`，第二次开始才有数。

**窄窗的显示格式约定**（卡片 128 DIP 宽，每像素都要省）：容量写成 `161/200G`、`11.9/15.2G`；速率用一位短单位 `845K` / `12.3M` / `1.2G`；每块盘的读写放在该盘最下面一行，左 `R` 右 `W`，右端和百分比列对齐；单位里的 `/s` 只在网络行出现。内存一行保留 `PHYS` 标注，提醒这是物理内存口径。

> 注意：Windows 的"已用内存"和任务管理器可能差几百 MB。`ullTotalPhys − ullAvailPhys` 是"物理已用"，任务管理器的"使用中"不含缓存/待机页。要的话再加 `PerformanceInformation` 的 `Memory\Cache Bytes` 校正。

### 温度 / 频率（顶栏传感器条）

顶栏时间正下方的一行两格：**CPU 温度 · GPU 温度**，读数下面一行小字是「当前/最高」频率。全部走**免管理员、免安装服务**的官方接口，实测单次采样 **约 0.3 ms（1 Hz 下 ≈ 0.03% 单核）**：

| 指标 | 数据源 | 说明 |
| --- | --- | --- |
| CPU 温度 | PDH `\Thermal Zone Information(*)\Temperature` | ACPI 热区（开尔文），多实例取最大；比 WMI 快两个量级且不拉起 WmiPrvSE |
| CPU 当前频率 | PDH `\Processor Information(_Total)\Processor Frequency × % Processor Performance` | 标称频率 × 实时性能百分比（ACPI/CPPC 上报），任务管理器的“速度”同源；>100% 就是睿频 |
| CPU 最高频率 | 同一 query 里的 `% of Maximum Frequency` 反推：`标称 × perf / maxPct` | 两个计数器同一时刻取数，比值稳定；再做慢速滑动平均压抖动，且实测跑超过会自动抬上限 |
| GPU 温度 / 频率 | NVML（`nvml.dll`，NVIDIA 驱动自带） | 进程内调用，温度读取约 0.05 ms；按 3 秒节流，避免把休眠中的独显反复唤醒 |

几个实现要点：

- 频率三个计数器放在**同一个 PDH query** 里，一次 `PdhCollectQueryData` 同一时刻取齐，`perf/maxPct` 的比值才不会因为采样时刻错开而抖。
- PDH 缓冲区复用一块 4 KB 内存，`PDH_MORE_DATA` 才扩容；rate 计数器首帧没有基线，无效值（`CStatus != 0`）直接丢弃。
- NVML 初始化约 17 ms，只在第一次采样时做；驱动不存在（`DllNotFoundException`）就永久跳过，不会反复尝试。
- 温度告警色：≥78° 用主题 Warn，≥88° 用 Crit。
- 右键菜单「温度 / 频率」可以整体关掉（写入配置 `"ShowSensors": false`）；关掉后 `UpdateSensors` 直接返回，一点开销都没有。
- 想要更精确的每核温度，只能上内核驱动（LibreHardwareMonitor 一类）并要求管理员——与本项目“免提权、零安装”的定位冲突，刻意不做。

### 便签（桌面）

全局热键 `Ctrl+Alt+N` 在鼠标附近新建一张便签，`Ctrl+Alt+H` 隐藏/恢复全部；右键菜单里也有对应两行（有便签时菜单会显示数量）。

- 便签上半是**自由文字**，下半是**待办清单**：输入行回车添加、点方块勾选（完成项变暗 + 删除线）、行尾 `×` 删除，右上 `×` 删除整张便签；标题栏拖动；
- **待办自动销毁 + 待办记录**：勾掉的待办在便签上停留 850 ms（让你看清勾选动效）后自动消失，同时写进**待办记录**——右键菜单「待办记录 · N 条」随时翻看，按天分组显示勾掉时间和内容，可单条删除或一键清空（防误触，要点两次）；记录上限 500 条，超出只留最近的；
- **边缘吸附 / 自动收起**（主屏副屏都支持）：松手时距任一显示器边缘 18 px 内自动贴齐；把便签往屏幕外推超过约 45% 就收起，只在屏幕边留一条 14 px 的窄条——鼠标碰一下窄条就探出来，移开 0.4 秒后自动缩回。左 / 右 / 上三边可收起，下边只贴齐；
- 配色完全跟随当前 HUD 主题（背景渐变、描边、强调色），切主题/切深浅色时自动重建；
- 内容、位置、勾选状态存在 `config.json` 的 `"Notes"` 数组里（位置用物理像素，跨屏不同缩放不漂移），打字时做 700 ms 防抖落盘，退出即存；`"NotesHidden"` 记住隐藏状态，吸附/收起状态也会原样恢复；
- 便签窗口同样不进 Alt+Tab（`WS_EX_TOOLWINDOW`），但可以正常接收键盘输入；置顶跟随主窗口独立设置；
- 调试：`SYSFLOAT_NOTE_DUMP=<png>` 渲染一张带示例待办的便签卡片，`SYSFLOAT_NOTE_DUMP_FLIP=1` 导出后再翻一次深浅色导出第二张（验证主题跟随），`SYSFLOAT_HISTORY_DUMP=<png>` 用示例数据渲一张「待办记录」窗口，`SYSFLOAT_DUMP_EXIT=1` 渲完自动退出。

### 主题与视觉设计

两种视觉语言并存，围绕"128 DIP 宽的常驻小窗"定。这一版的目标是**像一块安静、精密、有厚度的玻璃仪表**：降低背景饱和度，删掉装饰性网格和扫描线，把对比度留给时间、百分比和速率；所有主题共享外阴影、内圈高光、顶部受光和更克制的告警色，只保留各自的色相与性格。

| 元素 | 深海 HUD（仪表风格） | 5 套氛围主题（图片风格） |
| --- | --- | --- |
| 外框 | 10 DIP 切角八边形 + 细角标/顶部小缺口 + 内圈高光 | 18 DIP 圆角 + 1 px 主题色渐变边框 + 内圈高光，无角标/刻度尺 |
| 底色 | 近黑冷色斜向渐变 + 顶部受光 + 两角微光，**无网格/扫描线** | 天空→地面的三段竖向渐变 + 顶部受光 + 左上/右下柔光，**无网格/扫描线** |
| 材质 | 细渐变描边、外侧极淡投影、底部压暗 | 同左，圆角更大、更接近一块磨砂玻璃 |
| 电平条 | 26/22 段细 LED，整段共用一层弱辉光 + 顶部 micro 高光，无白色游标 | 连续圆角药丸 + 主题色渐变 + 内阴影 + 顶部高光 |
| 网络 | 64 点曲线 + 一条基线 + 端点光点 + 弱辉光，无网格/光标 | 64 点柔光曲线 + 面积填充 + 一条基线 |
| 字体 | `Cascadia Mono`，标签全大写；时间 20 DIP + 日期 7.4 DIP | `Segoe UI Variable / 微软雅黑`；时间 22 DIP + 日期 7.6 DIP，标签全大写 |
| 顶栏传感器条 | 时间正下方一行两格（CPU/GPU），发丝分隔线，读数 TextBright、≥78° Warn、≥88° Crit | 同一套结构，字号随主题字体微调 |
| 动效 | 数据脉冲每采样前进一格，透明度很低 | 无脉冲，只有 LIVE 灯按流量亮暗 |
| 配色 | 冷银蓝 + 苔绿，亮色只留给读数 | 每套取自对应图片的天空/山体/人物色；≥90% 用该主题的暖色告警、≥96% 玫瑰红 |

### 8 套外观

| 主题 | 取自 | 风格 |
| --- | --- | --- |
| 深海 HUD（当前默认） | 上一版冷色 HUD | 仪表：切角、细角标、分段条、极轻脉冲；网格/扫描线全部去掉 |
| 暮光山脊 | 蓝紫雪山 + 玫瑰色云 | 氛围：蓝紫→灰玫瑰三段玻璃，雾蓝读数 + 灰玫瑰告警，圆角 18 |
| 霞光山脉 | 蜜桃天空 + 蓝色层叠山脊 | 氛围：浅色是暖砂→藕粉→雾蓝，深色是暖褐→暮紫→深蓝；圆角 18 |
| 虹彩都市 | 全息外套 + 橙色烟雾 | 氛围：雾青 + 藕粉，旧金告警、青绿曲线；圆角 18 |
| 黑白缎影 | 黑白人像 + 红唇 | 氛围：暖灰/骨白/银 + 酒红，细节最克制；圆角 18 |
| 玄鳞 | 黑鳞纹理 + 绿/红点 | 氛围：墨绿/矿石灰 + 旧金 + 一点警示红；圆角 18 |
| 绯绒客厅 | lofi 女孩客厅壁纸：奶油黄墙面 + 绯红沙发 | 氛围：浅色是奶油米黄底 + 绯红主色 + 墨青次色，深色是暖褐暗房 + 珊瑚柔光；圆角 18 |
| 赤焰峡谷 | 橙红峡谷岩壁 + 藏蓝天空 + 白色跑车 | 氛围：全套高饱和互补色——浅色是灼热橙砂底 + 焰橙主色 + 藏蓝次色，深色是余烬褐底 + 焰橙辉光 + 暮蓝曲线；圆角 18 |

> 深色版和浅色版共享同一套色相与渐变顺序：浅色是白天版，深色是同一组颜色的"夜色化"版本，不会出现浅色是蜜桃蓝、深色却变成紫色这种风格割裂。
> 2026-11 起五套氛围主题整体加浓了一轮：背景色相更饱和、辉光更亮、浅色版底色带明确的主题色调，不再追求灰白玻璃的克制感（深海 HUD 保持原样）。
> 卡片顶部不再显示主题名，只保留大号系统时间和一行小日期；主题名只在右键菜单里出现。
>
> 每一套都有深色和浅色两版，共 14 个 `HudPalette`。启动时一次性构造并 `Freeze()`；切换外观只换引用和重建视觉树，不做逐控件换色。
>
> 为什么不用 WPF 动画：`AllowsTransparency` 的透明窗口对每个动画帧都要重新合成整窗。同一段 10 秒窗口的实测里，光跑 620 ms/秒的 `DoubleAnimation` 就能把单核占用从 ~5% 推到 ~28%。同样的"有生命感"，用采样帧内的一次性状态变化做，代价接近 0。

### 外观选择与跟随系统

- 右键菜单顶部是 **外观** 区域，列出 6 套主题；每行左侧是圆角双色板，当前主题用淡主题色底 + 小圆点标记，其余行只在悬停时提亮。
- 菜单本身也换成同一套玻璃语言：更大的 8/14 DIP 圆角、方向性柔和投影、细分隔线、无边框高亮；当前项不再用左侧色条。
- 选到 5 套氛围主题时，字体、电平条、网络曲线、背景渐变会一起切换；深海 HUD 保留仪表元素。悬浮窗内不显示主题名，顶部是时间 + 日期。选中后立即重建界面并写入 `%APPDATA%\SysMonitorFloat\config.json` 的 `"Theme"` 字段，下次启动恢复。
- 深浅色模式仍然自动跟随 Windows：读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`，`1` 是浅色；没有这个值时退回 `SystemUsesLightTheme`。
- 监听 `WM_SETTINGCHANGE` 和 `WM_THEMECHANGED`。系统切换主题时会连续发多条消息，统一用 `Dispatcher.BeginInvoke` 合并，再读一次注册表——只有值真的变了才重建。
- 重建时会把 64 点网络曲线历史搬到新 `Sparkline`，收起状态和窗口位置保持原样（收起时不做 `ClampToScreen`，否则会把窗从屏幕边上拽回来）。
- 截图或调试时可以用 `SYSFLOAT_THEME=light|dark` 强制明暗，用 `SYSFLOAT_THEME_ID=deepsea|alpine|sunset|holo|noir|scale|lofi|canyon` 强制主题；设置 `SYSFLOAT_DUMP` 时不会写回配置。再加 `SYSFLOAT_DUMP_SEED=1` 可直接填满 64 点历史曲线，`SYSFLOAT_DUMP_TRANSPARENT=1` 输出透明底卡片，`SYSFLOAT_DUMP_MENU=<png>` 额外导出菜单透明图。
- 可执行文件已用自签名证书 `SysMonitorFloat Local Signing`（指纹 `E094BCD48634803EFFD12CA3944D6368D6162766`，已装入当前用户的 Root + TrustedPublisher 存储）做 Authenticode 签名，避免 SmartScreen 每次"已保护你的电脑"拦截。**每次重新编译/publish 后签名会丢失**，需要重新签一次：先结束正在运行的浮窗进程，再执行 `Set-AuthenticodeSignature -FilePath <exe> -Certificate (Get-ChildItem Cert:\CurrentUser\My\E094BCD48634803EFFD12CA3944D6368D6162766)`。

## 三、窗口行为的六个坑（每个都踩过）

### 1. 高 DPI 必须声明 PerMonitorV2

`app.manifest` 里写 `dpiAwareness = PerMonitorV2`。否则在 150% 缩放屏上字是糊的，跨屏拖动还会错位。

### 2. 置顶 + 不进 Alt+Tab + 不出现在任务栏

```csharp
ShowInTaskbar = false;
Topmost = true;
// OnSourceInitialized 里追加扩展样式
SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
```

### 3. 点击不抢焦点（最容易被忽略，也最影响体验）

不加处理时，你正在打字/打游戏，鼠标划过或误点一下悬浮窗，焦点就被抢走。解决：

```csharp
if (msg == WM_MOUSEACTIVATE) { handled = true; return MA_NOACTIVATE; }
```

### 4. 有了 `MA_NOACTIVATE` 就别用 `DragMove()`

`DragMove()` 依赖系统移动循环，配合 `MA_NOACTIVATE` 行为不可靠。本项目改成**鼠标捕获 + `GetCursorPos()` 手动挪窗口**：

```csharp
GetWindowRect(hwnd, out RECT r);                       // 起点：物理像素
int x = r.Left + (p.X - _dragCursorX);                 // 光标位移直接加到物理坐标上
int y = r.Top  + (p.Y - _dragCursorY);
SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
```

（一开始这里用的是 `Left/Top` + DIP 换算，混合 DPI 下会错位，见下一节。）

### 5. 多显示器有负坐标，位置要按物理像素存

`SystemParameters.VirtualScreenLeft` 可能是负数（副屏在左侧）。

**更坑的是混合 DPI**：WPF 的 `Window.Left` 是「以当前显示器缩放为基准的 DIP」，而 `SystemParameters.VirtualScreen*` 是「以主屏缩放为基准的 DIP」。两者只在**所有屏缩放一致**时才等价。

真实踩到的例子：笔记本主屏 150%、副屏 100%。

```
SystemParameters.VirtualScreenLeft = -2560 / 1.5 = -1707   (主屏 DIP)
窗口在副屏上时 Left 是 100% DIP，-1707 DIP = -1707 px
副屏真实左边界           = -2560 px
差 853 px ≈ 2560/3  →  "拖到副屏左边三分之一就停住了"
```

**结论：跨屏定位不要用 `Left/Top`。** 这个项目改成全程物理像素：

```csharp
GetWindowRect(hwnd, out RECT r);                 // 物理像素
SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
             SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
GetSystemMetrics(SM_XVIRTUALSCREEN);             // 虚拟屏边界也是物理像素
```

只把"吸附距离""收起厚度"这些 DIP 常量按当前屏缩放换算成像素（`PadPx()`），视觉手感才在各屏上一致。

顺带一个观察：窗口在主屏宽 189 px、到副屏变成 126 px——这是 WPF 按每屏 DPI 正确缩放的结果，不是 bug。

### 6. 边缘判定必须相对"窗口当前所在的那块屏"

修好 DPI 之后又踩了第二个坑：**拖到副屏左边缘，窗口凭空消失**。

原因是两块屏上下错位：

```
主屏:  0,0    – 2560,1600   (150%)
副屏: -2560,67 – 0,1507     (100%)   ← 副屏顶边比主屏低 67px
```

原来判断"往上拖出屏幕"用的是**整个虚拟桌面的顶边**（y=0）。但副屏的顶边在 y=67，y=0~67 那一条在副屏上是黑的。于是沿副屏左边往上拖时，窗口被判定成"往上收"，收进 y=0 那条缝里——**那条缝在副屏可视区域之外，看起来就是消失了**。

正确做法两步：

```csharp
// 1. 取窗口中心所在的那块屏
MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST) + GetMonitorInfoW(...)
// 2. 只有"这条边外面没有别的显示器"才允许收起
bool outerLeft = !HasMonitorAt(monitor.Left - 5, centerY);
```

这样：副屏的顶边在它自己的 y=67 收起（露出 6px，看得见）；而主屏的左边界因为左边有副屏，**不会**触发收起（否则只是滑到隔壁屏上，看着像没反应）。

## 四、边缘吸附与自动收起

拖动时会做两件事，靠「拖出去多远」区分：

| 动作 | 判定 | 结果 |
| --- | --- | --- |
| **吸附** | 松手时窗口离屏幕边缘 ≤ 22 DIP | 贴在边上对齐，保持完整可见 |
| **收起** | 拖出边界，**超过一半**（约 45 DIP 位移） | 只留 6 DIP 露在屏幕内 |

**吸附是"看得见"的**：拖动进入边缘 22 DIP 范围后，吸附边会先浮出一层主题色光晕，外框同步提亮，贴边亮线从中间向两端长出来；离边越近越亮。松手时窗口不会瞬间跳位，而是用 **180 ms easeOutCubic** 滑完最后这段距离，同时整条吸附边亮到 100%，再在 **420 ms** 内衰减成一条常驻的淡亮线（`DockRestLevel = 0.14`），表示"现在贴在这条边上"。

角落可以两轴一起吸；下边缘只贴边、不收起。收起/探出仍然走原来的 130–150 ms 动画。

**收哪条边由主导方向决定**：左、右、上三个方向各自算「拖出边界的距离」，取最大的那个。所以斜着拖进右上角时，往上拖得多就收在顶边，往右拖得多就收在右边。拖动过程中就能看到反馈：拉出边界的距离 → 收起比例 0~1 线性映射，超过 `HideDistance = 90 DIP` 就完全推出屏幕（只留一条边）。松手时按当前比例决定是"吸住"还是"收起"。

> 下边缘只吸附、不收起：那里通常是任务栏，收进去就被盖住、鼠标再也够不着了。

**收起后**：边框会变成亮蓝色（`DockPen` / `DockGlow`），这样深色壁纸上那 6 DIP 的一条边也看得见。

- 鼠标移到那条边上 → 展开（`Peek`）；
- 鼠标离开 320ms → 自动缩回去；
- 收起状态下点一下 → 只是探出来，不开始拖动（从 6px 的边上起拖手感很怪）；
- 探出来之后就能正常拖动，拖走即脱离吸附。

实现要点：

- **收起时完全不采样**（`OnTick` 直接 return）。实测收起 10 秒 CPU = **0.000s**。
- 收起/展开走 **130–150ms 的 easeOutCubic 动画**（12ms 一帧，定时器只在动画期间存在，跑完立刻停）。
- **吸附预览是独立的一层 `DockGuide`**：只画一个渐变矩形、一条亮线和一圈外框高亮，不碰卡片背景、文字和数据区。拖动时只有它 `InvalidateVisual()`；亮度量化到 1/16 并按约 25 Hz 节流（窗口本身仍然 1:1 跟手），不会每来一个鼠标消息就重绘一次。
- **松手吸附**走 180 ms 的一次性动画；确认闪光用 16 ms 的 `DispatcherTimer` 手动跳帧，跑完 420 ms 立即停掉，不挂常驻 `DoubleAnimation`。
- 光标检测用 100ms 的 `DispatcherTimer`，**只在收起状态才跑**，平时零开销。
- 拖动、吸附、收起、悬停判定**全部在物理像素里算**，并且**相对窗口当前所在的那块显示器**（不是整个虚拟桌面）：副屏可能更矮、上下错位，用桌面的边会算错。只有"这条边外面没有别的显示器"时才允许收起。
- 吸附是**四个方向**（左/右/上/下）都有的；收起只有左/右/上三个方向，**下边缘不收起**——那里通常是任务栏，收进去就被盖住、鼠标够不着了。
- 收起状态**不写入配置**：`SaveConfig` 存的是展开后的位置，所以重启后是贴在边上，不会一上来只剩一条缝。

## 五、刷新开销怎么压

WPF 的固定成本主要在**每次重绘**，不在数据。项目里做了这些：

- `SetText()` 先比较再赋值——**内容没变就不触发布局**，这是 1 Hz 场景下最有效的一条；
- 电平条改成 `SegBar` 自绘：`Value` 变了才 `InvalidateVisual()`，一次 `OnRender` 画完 24/30 个段，不建 30 个子元素；
- 网络曲线是 `Sparkline` 的 64 点环形缓冲，每条采样只 push + 一次重绘；
- 所有画刷 / 画笔 `Freeze()` 成静态只读，不在 tick 里 new；
- 盘符集合变化才重建行（插拔 U 盘），平时零分配；
- 定时器用 `DispatcherPriority.Background`，不和输入抢调度；
- **不放常驻动画**：数据脉冲和 LIVE 灯的状态在采样那一帧顺手改掉，不挂 `DoubleAnimation`/`Forever`。同一段 10 秒窗口实测，光跑 620 ms/秒的交互动画就会把单核占用从 ~5% 推到 ~28%；
- 右键菜单直接自绘一个 `Popup` 面板（`Border` + 若干 `TextBlock` 行），不用 `ContextMenu`/`MenuItem`：WPF 默认菜单模板是浅色的，勾选列的底也不是 `Background` 能改的，要换主题得替换整个 `MenuItem` 模板。自绘反而几十行搞定，还能和 HUD 风格统一；
- 吸附预览和确认闪光都只碰 `DockGuide` 这一层：拖动时一次 `InvalidateVisual()` 只重画一个渐变矩形和两条线，并且按 1/16 亮度档 + 约 25 Hz 节流；闪光是一次 420 ms 的短定时器，跑完立刻停，不留常驻动画。待机时没有任何额外开销，只有贴边拖动/松手那一小段时间会多几次小面积重绘；
- 右键菜单里可以直接把刷新间隔切到 2 秒 / 5 秒。

## 六、跑起来

```powershell
cd SysMonitorFloat
dotnet run -c Release          # 直接跑
```

发布成单文件（160 KB，需要装 .NET 8 Desktop Runtime）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o publish
```

想要完全免运行时（约 60 MB）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

**操作**：
- 左键拖动；靠近屏幕**左/右/上/下**边缘 22 DIP 内时，贴边一侧会亮起主题色光晕和一条增长的亮线；松手后窗口会**平滑滑过去吸住对齐**，并闪一下确认。再往外拖过一半就**自动收起**，只留 6 DIP 的卡片边（窗口实际留 11 DIP：5 DIP 透明发光边距 + 6 DIP 卡片边），整圈边框会点亮成青色。收在哪个方向取决于你往哪边拖得多。鼠标碰一下那条边就滑出来，移开自动缩回。
- 右键菜单：

| 菜单项 | 作用 |
| --- | --- |
| 外观 | 8 套主题：深海 HUD / 暮光山脊 / 霞光山脉 / 虹彩都市 / 黑白缎影 / 玄鳞 / 绯绒客厅 / 赤焰峡谷；当前选择写入配置，深浅色仍自动跟随系统 |
| 新建便签 | 桌面便签（自由文字 + 待办清单），放在鼠标附近；全局热键 `Ctrl+Alt+N`，标题栏拖动、右上 `×` 删除 |
| 隐藏所有便签 | 有便签时出现；`Ctrl+Alt+H` 隐藏/恢复全部，状态写入配置 |
| 总在最前 | 切换 Topmost |
| 显示读写 | 关掉后每块盘只剩容量 + 电平条：深海约 426→361 DIP，氛围主题约 477→402 DIP（主题不同略有差异） |
| 温度 / 频率 | 顶栏传感器条（CPU/GPU 温度、当前/最高频率）的整体开关，写入 `"ShowSensors"` |
| 鼠标穿透 | 鼠标事件全部落到下层窗口（看视频/全屏时不被挡）；开启后**只能用 `Ctrl+Alt+M` 切回来** |
| 刷新间隔 | 1 / 2 / 5 秒 |
| 开机自动启动 | 写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限 |
| 退出 | 关闭（会保存位置、开关状态、当前主题） |

> 鼠标穿透故意**不持久化**：万一热键被别的软件占用了，重启就能恢复成可点击，不会把自己锁死。
开机自启写的是 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。
配置存在 `%APPDATA%\SysMonitorFloat\config.json`，其中 `"Theme"` 是 `deepsea|alpine|sunset|holo|noir|scale` 之一。

调排版时可以用离屏渲染出图，不用截图（2 倍像素输出，方便当 README 预览）：

```powershell
$env:SYSFLOAT_DUMP="C:\temp\ui.png"; .\SysMonitorFloat.exe
# 立刻填满 64 点历史曲线，不用真的等一分钟：
$env:SYSFLOAT_DUMP_SEED="1"
$env:SYSFLOAT_DUMP_DELAY="2"
# 截完自动退出，适合脚本：
$env:SYSFLOAT_DUMP_EXIT="1"
# 指定主题 / 强制明暗（截图用；SYSFLOAT_DUMP 模式下不会写回配置）：
$env:SYSFLOAT_THEME_ID="holo"; $env:SYSFLOAT_THEME="dark"
# 出透明底卡片，方便在文档里合到任意背景：
$env:SYSFLOAT_DUMP_TRANSPARENT="1"
# 额外导出一张透明底右键菜单：
$env:SYSFLOAT_DUMP_MENU="C:\temp\menu.png"
```

## 七、顺手记下的环境坑

- 用 `Set-Content` 回写 `.cs` 文件会把 UTF-8 中文注释写成 GBK，编译虽然过但注释全乱码。改源码用编辑器/`edit` 工具，别用 shell 重定向。
- **别加 `<InvariantGlobalization>true</InvariantGlobalization>`**。我一开始为了省点启动开销加了这个，结果**右键菜单一弹出就崩**：`XamlParseException: Cannot find non-neutral culture related to 'en-us'`，栈里是 `XmlLanguage.GetSpecificCulture()` ← `MultiBindingExpression`。WPF 的内置控件模板依赖具体 culture，invariant 模式必炸。
- **`AllowsTransparency` 窗口 + `DoubleAnimation` 很贵**。透明分层窗口每个动画帧都要重新合成整窗；实测 620 ms/秒的动画就让单核从 ~5% 涨到 ~28%。HUD 的动效要么跟采样帧一起改状态，要么用 `DispatcherTimer` 手动跳帧，别挂 60 fps 的持续动画。
- **`Freeze()` 过的画刷不能改色**。主题切换别去原地替换几十个 `Foreground`/`Fill`，直接把 `Content` 换成按新调色板重建的视觉树；重建时记得把 `Sparkline` 的环形缓冲和收起状态搬过去，否则曲线会清空、贴边的窗口会被 `ClampToScreen` 拽回来。
- `WM_SETTINGCHANGE` 在切换系统主题时会连发多次，别在钩子里直接重建 UI；用 `Dispatcher.BeginInvoke` 合并，并且只在 `AppsUseLightTheme` 的值真的变了才动手。
- `pwsh` 进程默认不是 DPI-aware，`GetWindowRect` / `SystemInformation.VirtualScreen` 返回的是**虚拟化坐标**（150% 缩放下只有真实值的 2/3）。截图定位前先 `SetProcessDpiAwarenessContext(-4)`。
- WPF 的 `Popup` 也是 `HwndWrapper[...]` 类名，枚举窗口时不能只按类名找主窗口，要按**面积最大**（或按句柄）取，否则会把弹出菜单当成主窗口。

## 八、下一步可以加什么

| 功能 | 数据源 | 难度 |
| --- | --- | --- |
| ~~磁盘读写速率（MB/s）~~ | ~~`IOCTL_DISK_PERFORMANCE`~~ | ✅ 已实现 |
| ~~网络曲线图（64 点）~~ | ~~环形缓冲 + 自绘 `Sparkline`~~ | ✅ 已实现 |
| ~~跟随系统浅色/深色~~ | ~~`AppsUseLightTheme` + `WM_SETTINGCHANGE`~~ | ✅ 已实现 |
| ~~6 套外观 / 圆角主题~~ | ~~`HudTheme` 注册表 + 冻结调色板~~ | ✅ 已实现 |
| ~~CPU/GPU 温度、当前/最高频率~~ | ~~PDH 性能计数器 + NVML（`Sensors.cs`，免管理员）~~ | ✅ 已实现（风扇格已按需求移除） |
| CPU 占用 | `GetSystemTimes()` 差分（全机）/ `NtQuerySystemInformation`（每核） | 低 |
| GPU 占用 | `\GPU Engine(*)\Utilization Percentage` 或 PDH | 中 |
| 每进程 Top 3 占用 | `NtQuerySystemInformation` + 进程遍历 | 中 |
| 托盘图标 + 点击显示/隐藏 | `Shell_NotifyIcon` 或 `NotifyIcon` | 中 |
| 鼠标穿透模式（看视频时） | `WS_EX_TRANSPARENT` 切换 | ✅ 已实现（Ctrl+Alt+M） |
| ~~温度~~ | ~~LibreHardwareMonitor 库~~ | ✅ 已用免管理员的 PDH 热区 + NVML 实现（见「顶栏传感器条」）；每核精度仍需内核驱动 |
| 自定义主题编辑器 | 把 `HudThemeSpec` 暴露成配置文件 | 低 |
