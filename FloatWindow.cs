using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SysMonitor
{
    /// <summary>
    /// 无边框 / 半透明 / 置顶 / 不抢焦点 / 不进 Alt+Tab 的悬浮窗。
    /// 采样跑在 UI 线程的 DispatcherTimer 上，默认 1 Hz。
    /// </summary>
    public sealed class FloatWindow : Window
    {
        private const string AppKey = "SysMonitorFloat";
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_THEMECHANGED = 0x031A;
        private const int WM_HOTKEY = 0x0312;
        private const int MA_NOACTIVATE = 3;
        private const int ToggleHotkeyId = 0xB001;
        private const int NewNoteHotkeyId = 0xB002;
        private const int ToggleNotesHotkeyId = 0xB003;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_M = 0x4D;
        private const uint VK_N = 0x4E;
        private const uint VK_H = 0x48;

        // 待办记录的留存上限：再多就只留最近的，别让 config.json 无限长
        private const int MaxTodoHistory = 500;

        // 卡片 128 DIP + 左右各 5 DIP 的发光边距；收起时仍留出 6 DIP 的卡片边。
        private const double WindowWidth = 138;
        private const double CardMargin = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 定位一律走物理像素：混合 DPI 下 WPF 的 Left/Top(DIP) 没有统一的坐标空间
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        // ---------------- 视觉层用到的静态画刷都在 HudVisuals.cs 里 ----------------

        private enum DockEdge
        {
            None,
            Left,
            Right,
            Top,
            Bottom      // 只贴边、不收起：下边缘通常是任务栏
        }

        private sealed class BarRow
        {
            public string Key;
            public TextBlock Value;
            public TextBlock Pct;
            public Run PctNumber;
            public Run PctSuffix;
            public TextBlock IoRead;
            public TextBlock IoWrite;
            public SegBar Bar;
        }

        /// <summary>顶栏传感器两格之一：CPU / GPU。</summary>
        private sealed class SensorCell
        {
            public TextBlock Value;
            public Run Number;
            public Run Unit;
            public TextBlock Sub;
        }

        private readonly DispatcherTimer _timer;
        private StackPanel _drivePanel;
        private readonly List<BarRow> _driveRows = new List<BarRow>();
        private BarRow _memRow;
        private TextBlock _downText;
        private TextBlock _upText;
        private TextBlock _clockText;
        private TextBlock _dateText;
        private SensorCell _cpuCell;
        private SensorCell _gpuCell;
        private readonly SensorSample _sensors = new SensorSample();
        private readonly List<StickyNoteWindow> _notes = new List<StickyNoteWindow>();
        private bool _notesHidden;
        private List<NoteData> _pendingNotes;
        private DispatcherTimer _noteSaveTimer;
        private DateTime _lastNoteCreatedAt = DateTime.MinValue;
        private List<TodoRecord> _todoHistory = new List<TodoRecord>();
        private TodoHistoryWindow _historyWindow;
        private TextBlock _intervalText;
        private Sparkline _spark;
        private HudSurface _surface;
        private DockGuide _dockGuide;
        private Ellipse _liveDot;
        private Rectangle _pulse;
        private TranslateTransform _pulseX;
        private double _intervalSeconds = 1;
        private bool _showIo = true;
        private bool _showSensors = true;
        private bool _rowsShowIo = true;
        private bool _clickThrough;
        private bool _isLight;
        private bool _themeRefreshQueued;
        private string _themeId = "deepsea";

        private DispatcherTimer _animTimer;
        private double _animFromX;
        private double _animToX;
        private double _animFromY;
        private double _animToY;
        private long _animStart;
        private double _animMs;

        // 吸附闪光：一次 420 ms 的短动画，只在松手命中吸附时存在
        private DispatcherTimer _dockFlashTimer;
        private long _dockFlashStart;
        private long _dockGuideTick;

        private bool _dragging;
        private bool _dragMoved;
        private int _dragCursorX;
        private int _dragCursorY;
        private int _dragWinX;
        private int _dragWinY;
        private bool _hasPendingPos;
        private int _pendingX;
        private int _pendingY;
        private bool _hasLastRect;
        private int _lastRectX;
        private int _lastRectY;
        private int _lastRectW;
        private int _lastRectH;

        public FloatWindow()
        {
            var cfg = LoadConfig();
            _intervalSeconds = cfg.Interval > 0 ? cfg.Interval : 1;
            _showIo = cfg.ShowIo;
            _showSensors = cfg.ShowSensors;
            _themeId = string.IsNullOrEmpty(cfg.Theme) ? "deepsea" : cfg.Theme;
            _notesHidden = cfg.NotesHidden;
            _pendingNotes = cfg.Notes ?? new List<NoteData>();
            _todoHistory = cfg.TodoHistory ?? new List<TodoRecord>();

            // 截图/调试时可以临时指定主题，不写入配置（见 OnClosing）。
            string forcedTheme = Environment.GetEnvironmentVariable("SYSFLOAT_THEME_ID");
            if (!string.IsNullOrEmpty(forcedTheme)) _themeId = forcedTheme;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = WindowWidth;
            Topmost = cfg.Topmost;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Cursor = Cursors.SizeAll;
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");
            FontSize = 9.5;

            if (cfg.HasPosition && cfg.PxPosition)
            {
                // 像素坐标，等窗口有 hwnd、尺寸算出来后直接 SetWindowPos 摆上去
                _hasPendingPos = true;
                _pendingX = cfg.LeftPx;
                _pendingY = cfg.TopPx;
            }

            // 兜底：先给个大致位置（DIP），避免首帧闪在屏幕左上角
            Left = SystemParameters.WorkArea.Right - WindowWidth - 24;
            Top = SystemParameters.WorkArea.Top + 24;

            _isLight = SystemUsesLightApps();
            Hud.UseTheme(_themeId, _isLight);
            _themeId = Hud.CurrentThemeId;

            Content = BuildContent();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(_intervalSeconds)
            };
            _timer.Tick += OnTick;

            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
        }


        /// <summary>按当前主题重建视觉树；主题切换时直接换掉 Content，位置和收起状态保持不变。</summary>
        private UIElement BuildContent()
        {
            // ---------------- 视觉树 ----------------

            bool ambient = Hud.Ambient;
            FontFamily = new FontFamily(ambient
                ? "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"
                : "Cascadia Mono, Consolas, Microsoft YaHei UI");
            FontSize = ambient ? 10 : 9.5;

            var previousSpark = _spark;
            _driveRows.Clear();
            _rowsShowIo = _showIo;

            var shell = new Grid { Margin = new Thickness(CardMargin) };

            _surface = new HudSurface();
            shell.Children.Add(_surface);

            // 吸附反馈层压在卡片背景之上、正文之下：拖动时只重绘它自己
            _dockGuide = new DockGuide();
            shell.Children.Add(_dockGuide);

            var root = new StackPanel { Margin = new Thickness(11, 10, 11, 9) };
            shell.Children.Add(root);

            // ---- 顶栏：大号时间 + 一行小日期，不显示主题名 ----
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            _clockText = new TextBlock
            {
                Text = "--:--",
                Foreground = Hud.TextBrightBrush,
                FontFamily = new FontFamily(ambient
                    ? "Segoe UI Variable Display, Segoe UI"
                    : "Cascadia Mono, Consolas, Segoe UI"),
                FontSize = ambient ? 22 : 20,
                FontWeight = ambient ? FontWeights.Medium : FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            SetTabular(_clockText);
            header.Children.Add(_clockText);

            _dateText = new TextBlock
            {
                Text = DateTime.Now.ToString("ddd  ·  dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 7.6 : 7.4,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            SetTabular(_dateText);
            header.Children.Add(_dateText);

            // ---- 时间正下方的传感器条：CPU / GPU，两格等宽，标签在上、读数居中、频率在下 ----
            _cpuCell = _gpuCell = null;
            if (_showSensors)
            {
                header.Children.Add(BuildSensorRail());
            }

            root.Children.Add(header);

            // 顶栏下面那条带“数据脉冲”的发光分隔线
            var headerLine = new Grid
            {
                Height = 2,
                Margin = new Thickness(0, 0, 0, 9),
                ClipToBounds = true
            };
            headerLine.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = Hud.DividerBrush,
                VerticalAlignment = VerticalAlignment.Bottom
            });
            _pulseX = new TranslateTransform(-24, 0);
            _pulse = new Rectangle
            {
                Width = 22,
                Height = 1.2,
                Fill = Hud.PulseBrush,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransform = _pulseX,
                Visibility = ambient ? Visibility.Collapsed : Visibility.Visible
            };
            headerLine.Children.Add(_pulse);
            root.Children.Add(headerLine);

            // ---- 内存 ----
            _memRow = AddBarRow(root, Hud.MemoryLabel, "mem", false, true);

            // ---- 各卷容量 + 读写 ----
            _drivePanel = new StackPanel();
            root.Children.Add(_drivePanel);

            AddDivider(root, 1, 6);

            // ---- 网络示波器 ----
            var netHead = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            netHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            netHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            netHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            netHead.Children.Add(new TextBlock
            {
                Text = Hud.NetworkLabel,
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 8.8 : 8.2,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            var netWindow = new TextBlock
            {
                Text = Hud.HistoryLabel,
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 7.6 : 7.4,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(netWindow, 2);
            netHead.Children.Add(netWindow);
            root.Children.Add(netHead);

            _spark = new Sparkline { Height = 32, Margin = new Thickness(0, 0, 0, 4) };
            if (previousSpark != null) _spark.CopyHistoryFrom(previousSpark);
            root.Children.Add(_spark);

            var rates = new Grid();
            rates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rates.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _downText = new TextBlock
            {
                Foreground = Hud.AccentSoftBrush,
                FontSize = ambient ? 11 : 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            SetTabular(_downText);
            _upText = new TextBlock
            {
                Foreground = Hud.AltSoftBrush,
                FontSize = ambient ? 11 : 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            SetTabular(_upText);
            rates.Children.Add(_downText);
            Grid.SetColumn(_upText, 1);
            rates.Children.Add(_upText);
            root.Children.Add(rates);

            // ---- 底栏：LIVE + 刷新间隔 ----
            AddDivider(root, 7, 5);

            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var live = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dotBox = new Grid { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0) };
            dotBox.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Hud.DotAuraBrush });
            _liveDot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = Hud.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dotBox.Children.Add(_liveDot);
            live.Children.Add(dotBox);
            live.Children.Add(new TextBlock
            {
                Text = Hud.LiveLabel,
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 8.2 : 7.8,
                VerticalAlignment = VerticalAlignment.Center
            });
            footer.Children.Add(live);

            _intervalText = new TextBlock
            {
                Text = FormatInterval(_intervalSeconds),
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 8.2 : 7.8,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            SetTabular(_intervalText);
            Grid.SetColumn(_intervalText, 1);
            footer.Children.Add(_intervalText);
            root.Children.Add(footer);

            _surface.DockHighlight = _tucked && !_peeking;
            RestDockGuide();
            return shell;
        }

        // ---------------- 顶栏传感器条 ----------------

        /// <summary>
        /// 一行三格：CPU 温度 / GPU 温度 / 风扇，读数下面一行是「当前/最高」频率。
        /// 只用 TextBlock + 一条发丝分隔线，没有动画、没有阴影，1 Hz 刷新时几乎零成本。
        /// </summary>
        private Grid BuildSensorRail()
        {
            var rail = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            rail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _cpuCell = AddSensorCell(rail, 0, "CPU");
            _gpuCell = AddSensorCell(rail, 2, "GPU");

            AddRailSeparator(rail, 1);
            return rail;
        }

        private static SensorCell AddSensorCell(Grid rail, int column, string label)
        {
            bool ambient = Hud.Ambient;
            var cell = new SensorCell();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Hud.TextDimBrush,
                FontSize = ambient ? 6.6 : 6.2,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1)
            });

            cell.Value = new TextBlock
            {
                Foreground = Hud.TextBrightBrush,
                FontSize = ambient ? 12.4 : 11.6,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            SetTabular(cell.Value);
            cell.Number = new Run("--");
            cell.Unit = new Run(string.Empty) { FontSize = ambient ? 7.4 : 6.8 };
            cell.Value.Inlines.Add(cell.Number);
            cell.Value.Inlines.Add(cell.Unit);
            panel.Children.Add(cell.Value);

            cell.Sub = new TextBlock
            {
                Text = string.Empty,
                Foreground = Hud.TextMidBrush,
                FontSize = ambient ? 7.2 : 6.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                // 占住一行的最小高度：频率读不到时两格依然等高，不会显得缺了一截
                MinHeight = ambient ? 10 : 9.4,
                Margin = new Thickness(0, 1, 0, 0)
            };
            SetTabular(cell.Sub);
            panel.Children.Add(cell.Sub);

            Grid.SetColumn(panel, column);
            rail.Children.Add(panel);
            return cell;
        }

        private static void AddRailSeparator(Grid rail, int column)
        {
            var line = new Rectangle
            {
                Width = 1,
                Fill = Hud.AccentBrush,
                Opacity = 0.14,
                Margin = new Thickness(2, 3, 2, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(line, column);
            rail.Children.Add(line);
        }

        /// <summary>把一格的读数和「当前/最高频率」写进去；变化才赋值，避免无谓的重绘。</summary>
        private static void UpdateSensorCell(SensorCell cell, double celsius, double mhz, double maxMhz)
        {
            if (cell == null) return;

            bool hasTemp = !double.IsNaN(celsius);
            SetRun(cell.Number, hasTemp ? celsius.ToString("0", CultureInfo.InvariantCulture) : "--");
            SetRun(cell.Unit, hasTemp ? "\u00B0" : string.Empty);

            Brush tone = !hasTemp ? Hud.TextDimBrush
                       : celsius >= 88 ? Hud.CritBrush
                       : celsius >= 78 ? Hud.WarnBrush
                       : Hud.TextBrightBrush;
            if (!ReferenceEquals(cell.Value.Foreground, tone)) cell.Value.Foreground = tone;

            SetText(cell.Sub, FmtClock(mhz, maxMhz));
        }

        private static string FmtClock(double mhz, double maxMhz)
        {
            if (double.IsNaN(mhz) || mhz <= 0) return string.Empty;

            string now = (mhz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
            if (double.IsNaN(maxMhz) || maxMhz <= 0 || maxMhz < mhz)
            {
                return now + "G";
            }
            return now + "/" + (maxMhz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "G";
        }

        private void UpdateSensors()
        {
            if (_cpuCell == null) return;

            Sensors.Sample(_sensors);
            UpdateSensorCell(_cpuCell, _sensors.CpuTemp, _sensors.CpuMHz, _sensors.CpuMaxMHz);
            UpdateSensorCell(_gpuCell, _sensors.GpuTemp, _sensors.GpuMHz, _sensors.GpuMaxMHz);
        }

        // ---------------- 窗口生命周期 ----------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_hasPendingPos)
            {
                MoveWindowPx(_pendingX, _pendingY);
                _hasPendingPos = false;
            }

            ClampToScreen();
            RestoreNotes();

            Metrics.ReadNetwork(out _, out _);   // 打一发基线，让第一秒就有读数
            TickOnce();

            _timer.Start();

            // 调试用：SYSFLOAT_NOTE_DUMP=<png> 渲染一张带待办的示例便签，用来调版式。
            string noteDump = Environment.GetEnvironmentVariable("SYSFLOAT_NOTE_DUMP");
            if (!string.IsNullOrEmpty(noteDump))
            {
                var sample = new NoteData
                {
                    HasPosition = true,
                    PxPosition = true,
                    LeftPx = -20000,
                    TopPx = -20000,
                    Text = "下午 3 点前把主题交付，顺手检查发布签名。",
                    Todos =
                    {
                        new NoteTodo { Text = "给赤焰峡谷补浅色版预览图", Done = true },
                        new NoteTodo { Text = "重签 publish 的 exe", Done = false },
                        new NoteTodo { Text = "更新 README 主题表", Done = false }
                    }
                };
                AddNoteWindow(sample, false);

                var noteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                noteTimer.Tick += (s, e) =>
                {
                    noteTimer.Stop();
                    if (_notes.Count > 0) _notes[_notes.Count - 1].SavePreview(noteDump);

                    // SYSFLOAT_NOTE_DUMP_FLIP=1：导出后再翻一次深浅色并导出第二张，
                    // 用来验证便签的 RefreshTheme 跟随切换。
                    if (Environment.GetEnvironmentVariable("SYSFLOAT_NOTE_DUMP_FLIP") == "1")
                    {
                        Hud.UseTheme(Hud.CurrentThemeId, !Hud.Current.IsLight);
                        RefreshNotesTheme();

                        var flipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        string flipped = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(noteDump)) ?? ".",
                            System.IO.Path.GetFileNameWithoutExtension(noteDump) + "-flip" +
                            System.IO.Path.GetExtension(noteDump));
                        flipTimer.Tick += (s2, e2) =>
                        {
                            flipTimer.Stop();
                            if (_notes.Count > 0) _notes[_notes.Count - 1].SavePreview(flipped);
                            if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_EXIT") == "1") Close();
                        };
                        flipTimer.Start();
                        return;
                    }

                    if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_EXIT") == "1") Close();
                };
                noteTimer.Start();
            }

            // 调试用：SYSFLOAT_HISTORY_DUMP=<png> 用示例数据渲一张「待办记录」窗口。
            string historyDump = Environment.GetEnvironmentVariable("SYSFLOAT_HISTORY_DUMP");
            if (!string.IsNullOrEmpty(historyDump))
            {
                DateTime now = DateTime.Now;
                _todoHistory.Clear();
                _todoHistory.Add(new TodoRecord { Text = "更新 README 待办记录说明", DoneAt = now.AddMinutes(-12) });
                _todoHistory.Add(new TodoRecord { Text = "给赤焰峡谷补浅色版预览图", DoneAt = now.AddHours(-2) });
                _todoHistory.Add(new TodoRecord { Text = "重签 publish 的 exe", DoneAt = now.AddDays(-1).AddHours(-3) });
                _todoHistory.Add(new TodoRecord { Text = "把风扇转速格从所有外观里去掉", DoneAt = now.AddDays(-1).AddHours(-6) });
                _todoHistory.Add(new TodoRecord { Text = "五套氛围主题统一加浓配色", DoneAt = now.AddDays(-2).AddHours(-1) });

                ShowTodoHistory();

                var historyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                historyTimer.Tick += (s, e) =>
                {
                    historyTimer.Stop();
                    _historyWindow?.SavePreview(historyDump);
                    if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_EXIT") == "1") Close();
                };
                historyTimer.Start();
            }

            // 调试用：设了 SYSFLOAT_DUMP=<png 路径> 就在稳定运行 3 秒后离屏渲染成图片，方便调排版。
            string dump = Environment.GetEnvironmentVariable("SYSFLOAT_DUMP");
            if (!string.IsNullOrEmpty(dump))
            {
                int delaySeconds = 3;
                if (int.TryParse(Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_DELAY"), out int custom) && custom > 0)
                {
                    delaySeconds = custom;   // 想截出 64 点历史曲线时把它设大一点
                }

                var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delaySeconds) };
                once.Tick += (s, e) =>
                {
                    once.Stop();
                    if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_SEED") == "1")
                    {
                        _spark.SeedPreview();
                    }
                    TickOnce();
                    UpdateLayout();
                    SaveSnapshot(dump);

                    // 文档预览用：把右键菜单单独离屏渲染成一张透明 PNG。
                    string dumpMenu = Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_MENU");
                    if (!string.IsNullOrEmpty(dumpMenu))
                    {
                        SaveMenuSnapshot(dumpMenu);
                    }

                    // 截图模式跑完就退，方便脚本/CI 里一张命令出图。
                    if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_EXIT") == "1")
                    {
                        Close();
                    }
                };
                once.Start();
            }
        }

        private void SaveSnapshot(string path)
        {
            int w = (int)Math.Ceiling(ActualWidth);
            int h = (int)Math.Ceiling(ActualHeight);
            if (w <= 0 || h <= 0) return;

            // 2 倍像素输出，README 里的预览图不会糊。
            const double scale = 2;
            int pw = (int)Math.Round(w * scale);
            int ph = (int)Math.Round(h * scale);

            var content = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            content.Render(this);

            // 透明底：文档合成时可以把同一张卡片放到任意背景上。
            if (Environment.GetEnvironmentVariable("SYSFLOAT_DUMP_TRANSPARENT") == "1")
            {
                var transparentEncoder = new PngBitmapEncoder();
                transparentEncoder.Frames.Add(BitmapFrame.Create(content));
                using var transparentFile = File.Create(path);
                transparentEncoder.Save(transparentFile);
                return;
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPreviewBackdrop(dc, w, h);
                dc.DrawImage(content, new Rect(0, 0, w, h));
            }
            var final = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            final.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(final));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        /// <summary>
        /// 文档预览用：把右键菜单单独渲染成透明 PNG，方便和卡片预览合成。
        /// 只有设置了 SYSFLOAT_DUMP_MENU 才会走到这里。
        /// </summary>
        private void SaveMenuSnapshot(string path)
        {
            ShowMenu();
            if (!(_menuPopup?.Child is FrameworkElement menu)) return;

            menu.UpdateLayout();
            int w = Math.Max(1, (int)Math.Ceiling(menu.ActualWidth));
            int h = Math.Max(1, (int)Math.Ceiling(menu.ActualHeight));

            const double scale = 2;
            const int pad = 36;   // 给投影留出透明边距
            var oldTransform = menu.RenderTransform;
            menu.RenderTransform = new TranslateTransform(pad, pad);

            var bitmap = new RenderTargetBitmap(
                (int)Math.Round(w * scale) + pad * 2,
                (int)Math.Round(h * scale) + pad * 2,
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(menu);
            menu.RenderTransform = oldTransform;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = File.Create(path);
            encoder.Save(fs);
            CloseMenu();
        }

        /// <summary>截图用的底：一块柔光棚拍背景，让半透明卡片和细描边显出层次。</summary>
        private static void DrawPreviewBackdrop(DrawingContext dc, int w, int h)
        {
            bool light = Hud.IsLight;

            var bg = new LinearGradientBrush(
                light ? Color.FromRgb(0xE7, 0xEE, 0xF3) : Color.FromRgb(0x0A, 0x10, 0x17),
                light ? Color.FromRgb(0xFA, 0xFC, 0xFD) : Color.FromRgb(0x02, 0x04, 0x07),
                new Point(0, 0), new Point(0.82, 1));
            bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            var accentGlow = new RadialGradientBrush
            {
                Center = new Point(0.12, 0.02),
                GradientOrigin = new Point(0.12, 0.02),
                RadiusX = 1.05,
                RadiusY = 0.65
            };
            accentGlow.GradientStops.Add(new GradientStop(
                Color.FromArgb(light ? (byte)0x20 : (byte)0x22, Hud.Current.Accent.R, Hud.Current.Accent.G, Hud.Current.Accent.B), 0));
            accentGlow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            accentGlow.Freeze();
            dc.DrawRectangle(accentGlow, null, new Rect(0, 0, w, h));

            var altGlow = new RadialGradientBrush
            {
                Center = new Point(0.92, 0.94),
                GradientOrigin = new Point(0.92, 0.94),
                RadiusX = 0.9,
                RadiusY = 0.7
            };
            altGlow.GradientStops.Add(new GradientStop(
                Color.FromArgb(light ? (byte)0x16 : (byte)0x1C, Hud.Current.Alt.R, Hud.Current.Alt.G, Hud.Current.Alt.B), 0));
            altGlow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            altGlow.Freeze();
            dc.DrawRectangle(altGlow, null, new Rect(0, 0, w, h));

            // 底部一条非常淡的横向渐隐，像摄影棚里的台面边缘。
            var floor = new LinearGradientBrush { StartPoint = new Point(0, 0.72), EndPoint = new Point(0, 1) };
            floor.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            floor.GradientStops.Add(new GradientStop(
                Color.FromArgb(light ? (byte)0x0C : (byte)0x22, 0, 0, 0), 1));
            floor.Freeze();
            dc.DrawRectangle(floor, null, new Rect(0, 0, w, h));

            var vignette = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.44),
                GradientOrigin = new Point(0.5, 0.44),
                RadiusX = 0.78,
                RadiusY = 0.78
            };
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.42));
            vignette.GradientStops.Add(new GradientStop(
                Color.FromArgb(light ? (byte)0x14 : (byte)0x5A, 0, 0, 0), 1));
            vignette.Freeze();
            dc.DrawRectangle(vignette, null, new Rect(0, 0, w, h));
        }

        /// <summary>
        /// 必须在 Closing 阶段保存：Closed 时 HWND 已经销毁，GetWindowRect 会失败（曾经因此把位置存成 0,0）。
        /// </summary>
        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 还有勾掉待销毁的待办，先归档再存盘
            foreach (StickyNoteWindow note in _notes) note.FlushCompleted();

            // 离屏截图时会临时覆盖主题/位置，别把 demo 状态写回用户配置。
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSFLOAT_DUMP"))
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSFLOAT_HISTORY_DUMP")))
            {
                SaveConfig();
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            _noteSaveTimer?.Stop();
            StopEdgeTimer();
            StopAnimation();
            StopDockFlash();
            Sensors.Shutdown();

            _historyWindow?.Close();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(hwnd, ToggleHotkeyId);
                UnregisterHotKey(hwnd, NewNoteHotkeyId);
                UnregisterHotKey(hwnd, ToggleNotesHotkeyId);
            }
        }

        // ---------------- 跟随系统浅色 / 深色 ----------------

        /// <summary>
        /// 读 Windows 的“应用模式”设置：AppsUseLightTheme = 1 表示浅色。
        /// 调试时可以用 SYSFLOAT_THEME=light|dark 强制，方便一次截两套主题的图。
        /// </summary>
        private static bool SystemUsesLightApps()
        {
            string forced = Environment.GetEnvironmentVariable("SYSFLOAT_THEME");
            if (string.Equals(forced, "light", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object value = key?.GetValue("AppsUseLightTheme")
                             ?? key?.GetValue("SystemUsesLightTheme");
                if (value is int i) return i != 0;
            }
            catch { }
            return false;
        }

        /// <summary>WM_SETTINGCHANGE 会成串地来，合并到一次 Dispatcher 回调里再处理。</summary>
        private void QueueThemeRefresh()
        {
            if (_themeRefreshQueued) return;
            _themeRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _themeRefreshQueued = false;
                ApplySystemTheme();
            }));
        }

        private void ApplySystemTheme()
        {
            bool light = SystemUsesLightApps();
            if (light == _isLight) return;

            _isLight = light;
            Hud.UseTheme(_themeId, _isLight);
            RebuildForTheme();
        }

        /// <summary>用户在右键菜单里换主题。</summary>
        private void SelectTheme(string id)
        {
            if (string.Equals(id, _themeId, StringComparison.OrdinalIgnoreCase))
            {
                CloseMenu();
                return;
            }

            _themeId = id;
            Hud.UseTheme(_themeId, _isLight);
            _themeId = Hud.CurrentThemeId;
            RebuildForTheme();
            SaveConfig();
        }

        /// <summary>调色板是 Freeze 的，换主题直接重建 Content；曲线和收起状态要保住。</summary>
        private void RebuildForTheme()
        {
            StopDockFlash();
            CloseMenu();
            Content = BuildContent();
            UpdateLayout();
            TickOnce();
            RefreshNotesTheme();

            // 收起状态位置本来就在屏幕外，别把它拽回来。
            if (!_tucked) ClampToScreen();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);   // 不进 Alt+Tab
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

            // 全局热键：穿透后没有别的办法点回来
            RegisterHotKey(hwnd, ToggleHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_M);
            // 便签：新建 / 隐藏全部
            RegisterHotKey(hwnd, NewNoteHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);
            RegisterHotKey(hwnd, ToggleNotesHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_H);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 点窗口不抢焦点：打字 / 游戏时误点到它，输入不会被打断。
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return (IntPtr)MA_NOACTIVATE;
            }

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == ToggleHotkeyId)
                {
                    SetClickThrough(!_clickThrough);
                    handled = true;
                    return IntPtr.Zero;
                }
                if (id == NewNoteHotkeyId)
                {
                    CreateNote();
                    handled = true;
                    return IntPtr.Zero;
                }
                if (id == ToggleNotesHotkeyId)
                {
                    ToggleNotesHidden();
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            if (msg == WM_SETTINGCHANGE || msg == WM_THEMECHANGED)
            {
                QueueThemeRefresh();
            }

            return IntPtr.Zero;
        }

        // ---------------- 采样与刷新 ----------------

        private void OnTick(object sender, EventArgs e)
        {
            // 收起后没人看得见，干脆不采样，把这段时间的开销也省掉
            if (_tucked && !_peeking) return;
            TickOnce();
        }

        private void TickOnce()
        {
            Metrics.ReadMemory(out ulong memUsed, out ulong memTotal);
            double memPct = memTotal == 0 ? 0 : memUsed * 100.0 / memTotal;

            UpdateRow(_memRow, memPct,
                FmtPair(memUsed, memTotal),
                memTotal == 0 ? "--" : memPct.ToString("0", CultureInfo.InvariantCulture) + "%");

            var drives = Metrics.ReadDrives();
            SyncDriveRows(drives);
            for (int i = 0; i < drives.Count && i < _driveRows.Count; i++)
            {
                var d = drives[i];
                var row = _driveRows[i];
                UpdateRow(row, d.Percent,
                    FmtPair(d.Used, d.Total),
                    d.Percent.ToString("0", CultureInfo.InvariantCulture) + "%");

                if (row.IoRead != null)
                {
                    bool reading = d.HasIo && d.ReadBps >= 1;
                    bool writing = d.HasIo && d.WriteBps >= 1;
                    SetText(row.IoRead, d.HasIo ? FmtRate(d.ReadBps) : "--");
                    SetText(row.IoWrite, d.HasIo ? FmtRate(d.WriteBps) : "--");
                    row.IoRead.Foreground = reading ? Hud.AccentSoftBrush : Hud.TextDimBrush;
                    row.IoWrite.Foreground = writing ? Hud.AltSoftBrush : Hud.TextDimBrush;
                }
            }

            Metrics.ReadNetwork(out double down, out double up);
            _spark.Push(down, up);
            SetText(_downText, "\u2193 " + FmtRate(down, "/s"));
            SetText(_upText, "\u2191 " + FmtRate(up, "/s"));
            SetText(_clockText, DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            SetText(_dateText, DateTime.Now.ToString("ddd  ·  dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant());

            UpdateSensors();

            FirePulse(down + up);
        }

        private int _pulseStep;

        /// <summary>
        /// 每次采样把“数据脉冲”往前挪一格、让 LIVE 灯按流量亮一下。
        /// 故意不用 WPF 的 DoubleAnimation：透明窗口下每帧重绘太贵，
        /// 这里的状态变化和数据刷新同一帧完成，几乎不增加开销。
        /// </summary>
        private void FirePulse(double totalBps)
        {
            if (!Hud.Ambient)
            {
                _pulseStep = (_pulseStep + 1) & 3;
                _pulseX.X = -24 + _pulseStep * 32;
                _pulse.Opacity = 0.55;
            }

            _liveDot.Opacity = totalBps >= 4096 ? 1.0 : 0.45;
        }

        private static string FormatInterval(double seconds)
        {
            return seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";
        }

        private void SyncDriveRows(List<DriveSample> drives)
        {
            // 盘符集合变化、或“显示读写”开关被切换时才重建视觉树，平时零分配。
            bool same = drives.Count == _driveRows.Count && _showIo == _rowsShowIo;
            for (int i = 0; same && i < drives.Count; i++)
            {
                if (_driveRows[i].Key != drives[i].Name) same = false;
            }
            if (same) return;

            _drivePanel.Children.Clear();
            _driveRows.Clear();
            _rowsShowIo = _showIo;
            foreach (var d in drives)
            {
                _driveRows.Add(AddBarRow(_drivePanel, d.Name, d.Name, _showIo));
            }
        }

        private static void AddDivider(Panel host, double top, double bottom)
        {
            host.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = Hud.DividerBrush,
                Margin = new Thickness(0, top, 0, bottom)
            });
        }

        private BarRow AddBarRow(Panel host, string label, string key, bool showIo, bool memory = false)
        {
            var row = new BarRow { Key = key };
            bool ambient = Hud.Ambient;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 标记
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 标签
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 数值
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 百分比

            FrameworkElement marker = ambient
                ? new Rectangle
                {
                    Width = 4,
                    Height = 4,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = memory ? Hud.AltBrush : Hud.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 3)
                }
                : new Rectangle
                {
                    Width = 2,
                    Height = 8,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = memory ? Hud.AltBrush : Hud.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 2)
                };
            header.Children.Add(marker);

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = memory ? Hud.TextDimBrush : Hud.TextBrightBrush,
                FontSize = ambient ? (memory ? 8.6 : 10.2) : (memory ? 8.2 : 9.6),
                FontWeight = memory ? FontWeights.Medium : FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(labelText, 1);
            header.Children.Add(labelText);

            row.Value = new TextBlock
            {
                Foreground = Hud.TextMidBrush,
                FontSize = ambient ? 9.3 : 9.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 5, 0)
            };
            SetTabular(row.Value);

            row.Pct = new TextBlock
            {
                Foreground = Hud.AccentBrush,
                FontSize = memory ? (ambient ? 18 : 17) : (ambient ? 9.8 : 9.4),
                FontWeight = FontWeights.SemiBold,
                Width = memory ? (ambient ? 39 : 37) : 28,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            SetTabular(row.Pct);
            row.PctNumber = new Run("--");
            row.PctSuffix = new Run("%")
            {
                FontSize = memory ? (ambient ? 11 : 10.5) : (ambient ? 7.0 : 6.8)
            };
            row.Pct.Inlines.Add(row.PctNumber);
            row.Pct.Inlines.Add(row.PctSuffix);
            Grid.SetColumn(row.Pct, 4);
            header.Children.Add(row.Pct);

            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, memory ? (ambient ? 11 : 10) : (ambient ? 9 : 8))
            };
            panel.Children.Add(header);

            if (memory)
            {
                // 内存多一行：左侧已用/总量，右侧标一下这是物理内存
                row.Value.HorizontalAlignment = HorizontalAlignment.Left;
                row.Value.Margin = new Thickness(0);
                row.Value.FontSize = ambient ? 10.5 : 10;

                var valueLine = new Grid { Margin = new Thickness(7, 1, 0, 0) };
                valueLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                valueLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                valueLine.Children.Add(row.Value);

                var phys = new TextBlock
                {
                    Text = "PHYS",
                    Foreground = Hud.TextDimBrush,
                    FontSize = ambient ? 7.6 : 7.4,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                Grid.SetColumn(phys, 1);
                valueLine.Children.Add(phys);
                panel.Children.Add(valueLine);
            }
            else
            {
                Grid.SetColumn(row.Value, 3);
                header.Children.Add(row.Value);
            }

            row.Bar = new SegBar
            {
                Height = memory ? 6 : 4.2,
                SegmentCount = memory ? 26 : 22,
                Gap = memory ? 1.5 : 1.7,
                Corner = memory ? 1.0 : 0.8,
                Margin = new Thickness(0, memory ? 5 : 4.5, 0, 0)
            };
            panel.Children.Add(row.Bar);

            if (showIo)
            {
                // 每块盘的读写：左 R、右 W，数值在活跃时点亮成主题主/次色
                var ioGrid = new Grid { Margin = new Thickness(7, 3, 0, 0) };
                ioGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ioGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ioGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var read = new StackPanel { Orientation = Orientation.Horizontal };
                read.Children.Add(new TextBlock
                {
                    Text = "R",
                    Foreground = Hud.ReadTagBrush,
                    FontSize = ambient ? 8.0 : 7.8,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.IoRead = new TextBlock
                {
                    Text = "--",
                    Foreground = Hud.TextDimBrush,
                    FontSize = ambient ? 8.8 : 8.4,
                    Margin = new Thickness(3, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                SetTabular(row.IoRead);
                read.Children.Add(row.IoRead);
                ioGrid.Children.Add(read);

                var write = new StackPanel { Orientation = Orientation.Horizontal };
                write.Children.Add(new TextBlock
                {
                    Text = "W",
                    Foreground = Hud.WriteTagBrush,
                    FontSize = ambient ? 8.0 : 7.8,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.IoWrite = new TextBlock
                {
                    Text = "--",
                    Foreground = Hud.TextDimBrush,
                    FontSize = ambient ? 8.8 : 8.4,
                    Margin = new Thickness(3, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                SetTabular(row.IoWrite);
                write.Children.Add(row.IoWrite);
                Grid.SetColumn(write, 2);
                ioGrid.Children.Add(write);

                panel.Children.Add(ioGrid);
            }

            host.Children.Add(panel);
            return row;
        }

        private static void UpdateRow(BarRow row, double percent, string value, string pct)
        {
            SetText(row.Value, value);

            // % 号独立成更小的字符：读数更醒目，整行也更像排版而不是数值拼接。
            string number = pct.EndsWith("%", StringComparison.Ordinal)
                ? pct.Substring(0, pct.Length - 1)
                : pct;
            if (!string.Equals(row.PctNumber.Text, number, StringComparison.Ordinal))
            {
                row.PctNumber.Text = number;
            }

            if (percent < 0 || double.IsNaN(percent)) percent = 0;
            if (percent > 100) percent = 100;

            row.Bar.Value = percent;
            row.Pct.Foreground = Hud.LevelBrush(percent);
        }

        private static void SetText(TextBlock block, string text)
        {
            // 只有内容真正变化才赋值，这是 1 Hz 刷新下最省事也最有效的优化。
            if (!string.Equals(block.Text, text, StringComparison.Ordinal))
            {
                block.Text = text;
            }
        }

        private static void SetRun(Run run, string text)
        {
            if (!string.Equals(run.Text, text, StringComparison.Ordinal))
            {
                run.Text = text;
            }
        }

        /// <summary>等宽数字：时间/百分比每秒跳动时不会左右抖。</summary>
        private static void SetTabular(TextBlock block)
        {
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        }

        // ---------------- 拖动 / 边缘磁吸 / 自动收起 ----------------

        private const double SnapDistance = 22;   // 离屏幕边这么近，松手就滑过去吸附
        private const double HideSliver = 11;     // 收起后露在外面的厚度：5 DIP 透明发光边距 + 6 DIP 卡片边
        private const double HideDistance = 90;   // 拖出边界这么远算完全收起
        private const double DockRestLevel = 0.14;// 吸附后保留的一点“已贴边”亮线
        private const double DockFlashMs = 420;   // 松手吸附时的确认闪光时长

        private DockEdge _dragEdge;               // 本次拖动命中的边
        private double _dragTuck;                 // 本次拖动的收起比例 0..1

        private DockEdge _dockEdge = DockEdge.None;
        private bool _tucked;                     // 已收进屏幕边里
        private bool _peeking;                    // 收起状态下临时探出来
        private DispatcherTimer _edgeTimer;
        private DateTime _leaveAt = DateTime.MaxValue;

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (_surface != null) _surface.Hovered = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_surface != null) _surface.Hovered = false;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (_tucked && !_peeking) Peek();
            ShowMenu();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // 收起状态下点一下只是“探出来”，不直接拖动：从 6px 的边上起拖手感很怪
            if (_tucked && !_peeking)
            {
                Peek();
                e.Handled = true;
                return;
            }

            LeaveDock();
            StopAnimation();

            GetCursorPos(out POINT p);
            _dragCursorX = p.X;
            _dragCursorY = p.Y;
            GetWindowRectPx(out _dragWinX, out _dragWinY, out _, out _);

            _dragging = true;
            _dragMoved = false;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;

            GetCursorPos(out POINT p);
            if (!_dragMoved
                && (Math.Abs(p.X - _dragCursorX) > 2 || Math.Abs(p.Y - _dragCursorY) > 2))
            {
                _dragMoved = true;   // 区分“点一下”和真的拖动：点一下不该触发吸附
            }

            int rawX = _dragWinX + (p.X - _dragCursorX);
            int rawY = _dragWinY + (p.Y - _dragCursorY);
            ResolveDrag(rawX, rawY);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            ApplyDrop();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            // 正常松手时 OnMouseLeftButtonUp 已经先清了 _dragging；
            // 这里只处理“捕获被系统抢走”的情况，顺手把吸附预览熄掉，别让它一直亮着。
            if (_dragging)
            {
                _dragging = false;
                _dragEdge = DockEdge.None;
                _dragTuck = 0;
                _dockGuide?.Reset();
            }
        }

        /// <summary>
        /// 拖动中实时算位置：自由移动时 1:1 跟手，只在快贴边时亮起吸附预览；
        /// 拖出边界超过 HideDistance 就按比例滑出去，让用户"看着它被推出去"，松手时再决定是吸附还是收起。
        /// 三个方向同时算越界量，取最大的那个 —— 斜着拖进角落时按"拖的方向"决定收哪条边。
        /// 注意：这里不再把窗口硬跳到边上，吸附位移统一交给松手后的 AnimateTo，
        /// 否则拖动过程中会出现一帧跳 16 px 的突兀感（旧版"不明显"的主因）。
        /// </summary>
        private void ResolveDrag(int rawX, int rawY)
        {
            GetWindowRectPx(out _, out _, out int w, out int h);
            GetVirtualScreenPx(out int vl, out int vt, out int vr, out int vb);

            // 以「窗口所在显示器」为准来判断边缘：副屏可能比主屏低/窄，
            // 用整个虚拟桌面的边会算错（曾经把窗口收进副屏可视区之外的缝里）。
            GetMonitorRectPx(rawX + w / 2, rawY + h / 2, out int ml, out int mt, out int mr, out int mb);
            int cx = rawX + w / 2;
            int cy = rawY + h / 2;

            int sliver = SliverPx;
            int hideDist = Math.Max(1, PadPx(HideDistance));
            int snap = PadPx(SnapDistance);

            // 只有"这条边外面没有别的显示器"时才允许推出去收起；
            // 否则一收就只是滑到隔壁屏上，看起来像没反应。
            bool outerLeft = !HasMonitorAt(ml - 5, cy);
            bool outerRight = !HasMonitorAt(mr + 5, cy);
            bool outerTop = !HasMonitorAt(cx, mt - 5);
            bool outerBottom = !HasMonitorAt(cx, mb + 5);

            int overLeft = outerLeft ? ml - rawX : int.MinValue;
            int overRight = outerRight ? (rawX + w) - mr : int.MinValue;
            int overTop = outerTop ? mt - rawY : int.MinValue;

            // 自由移动时也不许跑到"没有屏幕的那片空白"里
            int minX = outerLeft ? ml : vl;
            int maxX = outerRight ? mr - w : vr - w;
            int minY = outerTop ? mt : vt;
            int maxY = outerBottom ? mb - h : vb - h;

            int x = Math.Max(minX, Math.Min(rawX, maxX));
            int y = Math.Max(minY, Math.Min(rawY, maxY));
            double tuck = 0;
            DockEdge edge = DockEdge.None;

            if (overTop > 0 && overTop >= overLeft && overTop >= overRight)
            {
                // 拖出上边缘 → 往上收
                edge = DockEdge.Top;
                tuck = Math.Min((double)overTop / hideDist, 1);
                y = mt - (int)Math.Round((h - sliver) * tuck);
            }
            else if (overLeft > 0 && overLeft >= overRight)
            {
                edge = DockEdge.Left;
                tuck = Math.Min((double)overLeft / hideDist, 1);
                x = ml - (int)Math.Round((w - sliver) * tuck);
            }
            else if (overRight > 0)
            {
                edge = DockEdge.Right;
                tuck = Math.Min((double)overRight / hideDist, 1);
                x = mr - w + (int)Math.Round((w - sliver) * tuck);
            }

            _dragEdge = edge;
            _dragTuck = tuck;
            MoveWindowPx(x, y);

            if (edge != DockEdge.None)
            {
                // 已经拖出边界：吸附边保持全亮，和收起后整框点亮接上，不会出现亮度跳变
                ShowDockGuide(edge, 1);
            }
            else
            {
                UpdateDockPreview(x, y, w, h, ml, mt, mr, mb, snap,
                    outerLeft, outerRight, outerTop, outerBottom);
            }
        }

        /// <summary>
        /// 拖动时按"离边还有多少像素"点亮吸附预览：进入 SnapDistance 才开始亮，
        /// 到边缘时最亮。它只画反馈、不改窗口位置，所以拖动始终跟手。
        /// </summary>
        private void UpdateDockPreview(int x, int y, int w, int h,
            int ml, int mt, int mr, int mb, int snap,
            bool outerLeft, bool outerRight, bool outerTop, bool outerBottom)
        {
            int dLeft = outerLeft ? x - ml : int.MaxValue;
            int dRight = outerRight ? mr - (x + w) : int.MaxValue;
            int dTop = outerTop ? y - mt : int.MaxValue;
            int dBottom = outerBottom ? mb - (y + h) : int.MaxValue;

            int best = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
            if (best < 0) best = 0;

            if (best > snap)
            {
                _dockGuide.Reset();
                return;
            }

            double t = 1.0 - (double)best / Math.Max(1, snap);
            double level = t * t * (3 - 2 * t);   // smoothstep：进入吸附范围时从 0 渐亮

            DockEdge side;
            if (best == dTop) side = DockEdge.Top;
            else if (best == dLeft) side = DockEdge.Left;
            else if (best == dRight) side = DockEdge.Right;
            else side = DockEdge.Bottom;

            ShowDockGuide(side, level);
        }

        private void ApplyDrop()
        {
            // 只点了一下、没真的拖：不要因为窗口本来就在边上而触发吸附/闪光
            if (!_dragMoved)
            {
                LeaveDock();
                return;
            }

            GetWindowRectPx(out int x, out int y, out int w, out int h);

            if (_dragEdge != DockEdge.None && _dragTuck >= 0.5)
            {
                // 一半以上已经推出屏幕 → 直接收起
                BeginTuck(_dragEdge);
                return;
            }

            GetMonitorRectPx(x + w / 2, y + h / 2, out int ml, out int mt, out int mr, out int mb);
            int snap = PadPx(SnapDistance);

            bool outerLeft = !HasMonitorAt(ml - 5, y + h / 2);
            bool outerRight = !HasMonitorAt(mr + 5, y + h / 2);
            bool outerTop = !HasMonitorAt(x + w / 2, mt - 5);
            bool outerBottom = !HasMonitorAt(x + w / 2, mb + 5);

            int dLeft = outerLeft ? x - ml : int.MaxValue;
            int dRight = outerRight ? mr - (x + w) : int.MaxValue;
            int dTop = outerTop ? y - mt : int.MaxValue;
            int dBottom = outerBottom ? mb - (y + h) : int.MaxValue;

            int best = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
            if (best > snap)
            {
                LeaveDock();
                ClampToScreen();
                return;
            }

            // 角落可以两个方向一起吸：x / y 各自判断，互不影响
            int targetX = x;
            int targetY = y;
            DockEdge side = DockEdge.None;
            int sideDistance = int.MaxValue;

            if (dLeft <= snap)
            {
                targetX = ml;
                if (dLeft < sideDistance) { sideDistance = dLeft; side = DockEdge.Left; }
            }
            if (dRight <= snap)
            {
                targetX = mr - w;
                if (dRight < sideDistance) { sideDistance = dRight; side = DockEdge.Right; }
            }
            if (dTop <= snap)
            {
                targetY = mt;
                if (dTop < sideDistance) { sideDistance = dTop; side = DockEdge.Top; }
            }
            if (dBottom <= snap)
            {
                targetY = mb - h;
                if (dBottom < sideDistance) { sideDistance = dBottom; side = DockEdge.Bottom; }
            }

            _dockEdge = side;
            _tucked = false;
            _peeking = false;
            _leaveAt = DateTime.MaxValue;
            SetTuckHint(false);
            StopEdgeTimer();

            // 松手时用 180 ms 滑完最后这段距离；已经贴边就只做闪光
            if (targetX != x || targetY != y)
            {
                AnimateTo(targetX, targetY, 180);
            }
            else
            {
                MoveWindowPx(targetX, targetY);
            }

            FlashDock(side);
        }

        private void LeaveDock()
        {
            StopDockFlash();
            _dockGuide?.Reset();
            _dockEdge = DockEdge.None;
            _tucked = false;
            _peeking = false;
            _dragEdge = DockEdge.None;
            _dragTuck = 0;
            _leaveAt = DateTime.MaxValue;
            SetTuckHint(false);
            StopEdgeTimer();
        }

        private void BeginTuck(DockEdge edge)
        {
            StopDockFlash();
            _dockGuide?.Reset();
            _dockEdge = edge;
            _tucked = true;
            _peeking = false;
            _leaveAt = DateTime.MaxValue;
            MoveToTucked();
            StartEdgeTimer();
        }

        private void MoveToTucked()
        {
            SetTuckHint(true);
            GetDockPositions(out int tuckX, out int tuckY, out _, out _);
            AnimateTo(tuckX, tuckY, 150);
        }

        /// <summary>算出当前这条边对应的"收起位置"和"完全展开位置"（物理像素）。</summary>
        private void GetDockPositions(out int tuckX, out int tuckY, out int peekX, out int peekY)
        {
            GetWindowRectPx(out int x, out int y, out int w, out int h);
            GetMonitorRectPx(x + w / 2, y + h / 2, out int ml, out int mt, out int mr, out _);
            int sliver = SliverPx;

            tuckX = x;
            tuckY = y;
            peekX = x;
            peekY = y;

            switch (_dockEdge)
            {
                case DockEdge.Left:
                    peekX = ml;
                    tuckX = ml - (w - sliver);
                    break;
                case DockEdge.Right:
                    peekX = mr - w;
                    tuckX = mr - sliver;
                    break;
                case DockEdge.Top:
                    peekY = mt;
                    tuckY = mt - (h - sliver);
                    break;
            }
        }

        /// <summary>收起时把整圈边框点亮，露出的一小条才看得见、找得到。</summary>
        private void SetTuckHint(bool tucked)
        {
            if (_surface != null)
            {
                _surface.DockHighlight = tucked;
            }
        }

        private static HudDockSide ToHudSide(DockEdge edge)
        {
            switch (edge)
            {
                case DockEdge.Left: return HudDockSide.Left;
                case DockEdge.Right: return HudDockSide.Right;
                case DockEdge.Top: return HudDockSide.Top;
                case DockEdge.Bottom: return HudDockSide.Bottom;
                default: return HudDockSide.None;
            }
        }

        private void ShowDockGuide(DockEdge edge, double level)
        {
            if (_dockGuide == null) return;

            HudDockSide side = ToHudSide(edge);
            if (side == HudDockSide.None || level <= 0.001)
            {
                _dockGuide.Reset();
                return;
            }

            // 拖动时鼠标事件可能上百 Hz，而光晕只需要跟着"离边远近"缓慢变化。
            // 量化 + 20~25 Hz 节流：拖动仍然 1:1 跟手，但不会每来个 WM_MOUSEMOVE 就让整窗重合成一次。
            long now = Stopwatch.GetTimestamp();
            double sinceMs = (now - _dockGuideTick) * 1000.0 / Stopwatch.Frequency;
            if (side == _dockGuide.Side && sinceMs < 40) return;

            _dockGuide.Side = side;
            _dockGuide.Level = Math.Round(level * 16) / 16.0;
            _dockGuideTick = now;
        }

        /// <summary>吸附完成后留一条很淡的亮线，说明"现在贴在这条边上"；收起状态由整框高亮接管。</summary>
        private void RestDockGuide()
        {
            if (_dockGuide == null) return;

            if (_dockEdge != DockEdge.None && !_tucked)
            {
                _dockGuide.Side = ToHudSide(_dockEdge);
                _dockGuide.Level = DockRestLevel;
            }
            else
            {
                _dockGuide.Reset();
            }
        }

        /// <summary>
        /// 吸附确认：整条吸附边瞬间亮到 100%，再用 420 ms 衰减到常驻的淡亮线。
        /// 用一次性 DispatcherTimer 手动跳帧，不挂 WPF 的 DoubleAnimation——
        /// 透明窗口下常驻动画每帧都要重合成整窗，短促的一次性动画则只在松手后跑十几帧。
        /// </summary>
        private void FlashDock(DockEdge edge)
        {
            if (_dockGuide == null || edge == DockEdge.None) return;

            StopDockFlash();
            _dockGuide.Side = ToHudSide(edge);
            _dockGuide.Level = 1;

            _dockFlashStart = Stopwatch.GetTimestamp();
            _dockFlashTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _dockFlashTimer.Tick += OnDockFlashTick;
            _dockFlashTimer.Start();
        }

        private void OnDockFlashTick(object sender, EventArgs e)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - _dockFlashStart) * 1000.0 / Stopwatch.Frequency;
            double t = elapsedMs / DockFlashMs;

            if (t >= 1)
            {
                StopDockFlash();
                RestDockGuide();
                return;
            }

            double level = 1.0 - Math.Pow(t, 1.3);
            _dockGuide.Level = Math.Max(DockRestLevel, level);
        }

        private void StopDockFlash()
        {
            if (_dockFlashTimer == null) return;
            _dockFlashTimer.Stop();
            _dockFlashTimer.Tick -= OnDockFlashTick;
            _dockFlashTimer = null;
        }

        private void Peek()
        {
            _peeking = true;
            _leaveAt = DateTime.MaxValue;
            SetTuckHint(false);

            GetDockPositions(out _, out _, out int peekX, out int peekY);
            AnimateTo(peekX, peekY, 130);

            TickOnce();   // 收起期间不采样，探出来的瞬间补一次，避免看到过期数字
        }

        private void StartEdgeTimer()
        {
            if (_edgeTimer == null)
            {
                _edgeTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _edgeTimer.Tick += OnEdgeTick;
            }
            _edgeTimer.Start();
        }

        private void StopEdgeTimer()
        {
            _edgeTimer?.Stop();
        }

        /// <summary>收起状态下用 100ms 轮询光标（只有这一种状态才会跑，平时不耗）。</summary>
        private void OnEdgeTick(object sender, EventArgs e)
        {
            // 下边缘只贴边、不收起，不应该跑到这里；防御一下，别让收起状态卡死
            if (!_tucked || _dockEdge == DockEdge.None || _dockEdge == DockEdge.Bottom)
            {
                StopEdgeTimer();
                return;
            }

            GetCursorPos(out POINT cursor);
            int cx = cursor.X;
            int cy = cursor.Y;

            GetWindowRectPx(out int wx, out int wy, out int w, out int h);
            GetMonitorRectPx(wx + w / 2, wy + h / 2, out int ml, out int mt, out int mr, out _);
            int sliver = SliverPx;

            if (!_peeking)
            {
                bool hot;
                if (_dockEdge == DockEdge.Top)
                {
                    // 顶边是一条横向的窄带
                    hot = cx >= wx - 6
                       && cx <= wx + w + 6
                       && cy >= mt - 6
                       && cy <= mt + sliver + 15;
                }
                else
                {
                    int hotLeft = _dockEdge == DockEdge.Left ? ml - 6 : mr - sliver - 15;
                    int hotRight = _dockEdge == DockEdge.Left ? ml + sliver + 15 : mr + 6;
                    hot = cx >= hotLeft && cx <= hotRight && cy >= wy - 6 && cy <= wy + h + 6;
                }

                if (hot) Peek();
                return;
            }

            int pad = PadPx(14);
            bool inside = cx >= wx - pad && cx <= wx + w + pad
                       && cy >= wy - pad && cy <= wy + h + pad;
            if (inside)
            {
                _leaveAt = DateTime.MaxValue;
                return;
            }

            // 鼠标离开后给 320ms 宽限，避免擦边而过就缩回去
            if (_leaveAt == DateTime.MaxValue)
            {
                _leaveAt = DateTime.UtcNow.AddMilliseconds(320);
            }
            else if (DateTime.UtcNow >= _leaveAt)
            {
                _leaveAt = DateTime.MaxValue;
                _peeking = false;
                MoveToTucked();
            }
        }

        // ---------------- 定位（一律物理像素） ----------------

        /// <summary>当前显示器缩放（只用来把 DIP 常量换算成像素）。</summary>
        private double Scale
        {
            get
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                return dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
            }
        }

        private int PadPx(double dip) => (int)Math.Round(dip * Scale);

        private int SliverPx => Math.Max(2, PadPx(HideSliver));

        private IntPtr Handle => new WindowInteropHelper(this).Handle;

        private void GetWindowRectPx(out int x, out int y, out int w, out int h)
        {
            var hwnd = Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
            {
                x = r.Left;
                y = r.Top;
                w = r.Right - r.Left;
                h = r.Bottom - r.Top;

                // 缓存一份：关闭时窗口已经销毁，但还得知道最后停在哪
                _lastRectX = x;
                _lastRectY = y;
                _lastRectW = w;
                _lastRectH = h;
                _hasLastRect = true;
                return;
            }

            x = _hasLastRect ? _lastRectX : 0;
            y = _hasLastRect ? _lastRectY : 0;
            w = _hasLastRect ? _lastRectW : PadPx(WindowWidth);
            h = _hasLastRect ? _lastRectH : 120;
        }

        private void MoveWindowPx(int x, int y)
        {
            var hwnd = Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private static void GetVirtualScreenPx(out int left, out int top, out int right, out int bottom)
        {
            left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        }

        /// <summary>某个点所在那块显示器的物理矩形（找不到就退回虚拟桌面）。</summary>
        private static void GetMonitorRectPx(int x, int y, out int left, out int top, out int right, out int bottom)
        {
            var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (mon != IntPtr.Zero && GetMonitorInfoW(mon, ref mi))
            {
                left = mi.rcMonitor.Left;
                top = mi.rcMonitor.Top;
                right = mi.rcMonitor.Right;
                bottom = mi.rcMonitor.Bottom;
                return;
            }

            GetVirtualScreenPx(out left, out top, out right, out bottom);
        }

        /// <summary>窗口中心点所在的显示器矩形。</summary>
        private void GetCurrentMonitorPx(out int left, out int top, out int right, out int bottom)
        {
            GetWindowRectPx(out int x, out int y, out int w, out int h);
            GetMonitorRectPx(x + w / 2, y + h / 2, out left, out top, out right, out bottom);
        }

        /// <summary>这个坐标点是否落在某块显示器上（用来判断"边的外面还有没有屏"）。</summary>
        private static bool HasMonitorAt(int x, int y)
        {
            var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(mon, ref mi)) return false;

            return x >= mi.rcMonitor.Left && x < mi.rcMonitor.Right
                && y >= mi.rcMonitor.Top && y < mi.rcMonitor.Bottom;
        }

        /// <summary>
        /// 保证整窗落在当前显示器内。除了分辨率变化，换宽/换高后的旧配置位置也可能让窗口探出屏幕：
        /// 旧版 126 DIP 宽的窗口贴右边缘时，新版 138 DIP 宽会多出 18 px——这里按显示器右/下边界把它推回来。
        /// </summary>
        private void ClampToScreen()
        {
            GetWindowRectPx(out int x, out int y, out int w, out int h);
            GetMonitorRectPx(x + w / 2, y + h / 2, out int left, out int top, out int right, out int bottom);

            if (x + w > right) x = Math.Max(left, right - w);
            if (y + h > bottom) y = Math.Max(top, bottom - h);
            if (x < left) x = left;
            if (y < top) y = top;

            MoveWindowPx(x, y);
        }

        // ---------------- 右键菜单 ----------------

        private Popup _menuPopup;

        private void ShowMenu()
        {
            CloseMenu();

            var panel = new StackPanel { Width = 194 };

            var head = new Grid { Margin = new Thickness(10, 4, 10, 5) };
            head.Children.Add(new TextBlock
            {
                Text = "SYS · MONITOR",
                Foreground = Hud.AccentBrush,
                FontSize = 8.4,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var version = new TextBlock
            {
                Text = "SYSTEM",
                Foreground = Hud.TextDimBrush,
                FontSize = 7.2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            head.Children.Add(version);
            panel.Children.Add(head);
            panel.Children.Add(new Rectangle { Height = 1, Fill = Hud.MenuDivider, Margin = new Thickness(8, 0, 8, 6) });

            // ---- 外观 / 主题 ----
            var lookHead = new Grid { Margin = new Thickness(10, 2, 10, 4) };
            lookHead.Children.Add(new TextBlock
            {
                Text = "外观",
                Foreground = Hud.TextDimBrush,
                FontSize = 8.8,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            var autoTheme = new TextBlock
            {
                Text = "自动明暗",
                Foreground = Hud.TextDimBrush,
                FontSize = 7.4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            lookHead.Children.Add(autoTheme);
            panel.Children.Add(lookHead);

            foreach (HudTheme theme in Hud.Themes)
            {
                string id = theme.Id;
                AddThemeRow(panel, theme,
                    string.Equals(theme.Id, _themeId, StringComparison.OrdinalIgnoreCase),
                    () => SelectTheme(id));
            }

            panel.Children.Add(new Rectangle { Height = 1, Fill = Hud.MenuDivider, Margin = new Thickness(8, 5, 8, 5) });

            AddMenuRow(panel, "总在最前", Topmost, () => Topmost = !Topmost);
            AddMenuRow(panel, "显示读写", _showIo, () =>
            {
                _showIo = !_showIo;
                TickOnce();   // 下一次 SyncDriveRows 会因开关变化重建行，高度随之自动收缩
                // 高度变了，贴底/贴边的窗口可能探出屏幕，等布局跑完再推回来
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampToScreen));
            });
            AddMenuRow(panel, "温度 / 频率", _showSensors, () =>
            {
                _showSensors = !_showSensors;
                RebuildForTheme();   // 顶栏结构变了，直接按当前主题重建一次
            });
            AddMenuRow(panel, "鼠标穿透", _clickThrough, () => SetClickThrough(!_clickThrough), hint: "Ctrl+Alt+M");

            panel.Children.Add(new Rectangle { Height = 1, Fill = Hud.MenuDivider, Margin = new Thickness(8, 5, 8, 5) });
            AddMenuRow(panel, _notes.Count > 0 ? "新建便签  ·  " + _notes.Count + " 张" : "新建便签", false,
                CreateNote, hint: "Ctrl+Alt+N");
            if (_notes.Count > 0)
            {
                AddMenuRow(panel, _notesHidden ? "显示所有便签" : "隐藏所有便签", _notesHidden,
                    ToggleNotesHidden, hint: "Ctrl+Alt+H");
            }
            AddMenuRow(panel,
                _todoHistory.Count > 0 ? "待办记录  ·  " + _todoHistory.Count + " 条" : "待办记录",
                false, ShowTodoHistory);

            panel.Children.Add(new Rectangle { Height = 1, Fill = Hud.MenuDivider, Margin = new Thickness(8, 5, 8, 5) });
            panel.Children.Add(new TextBlock
            {
                Text = "刷新间隔",
                Foreground = Hud.TextDimBrush,
                FontSize = 8.8,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(10, 1, 10, 4)
            });

            foreach (double seconds in new[] { 1.0, 2.0, 5.0 })
            {
                double value = seconds;
                AddMenuRow(panel,
                    value.ToString("0", CultureInfo.InvariantCulture) + " 秒",
                    Math.Abs(_intervalSeconds - value) < 0.01,
                    () =>
                    {
                        _intervalSeconds = value;
                        _timer.Interval = TimeSpan.FromSeconds(value);
                        SetText(_intervalText, FormatInterval(value));
                    },
                    indent: true);
            }

            panel.Children.Add(new Rectangle { Height = 1, Fill = Hud.MenuDivider, Margin = new Thickness(8, 5, 8, 5) });
            AddMenuRow(panel, "开机自动启动", IsStartupEnabled(), () =>
            {
                try
                {
                    SetStartup(!IsStartupEnabled());
                }
                catch { }
            });
            AddMenuRow(panel, "退出", false, Close);

            _menuPopup = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.MousePoint,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = Hud.MenuBg,
                    BorderBrush = Hud.MenuBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(Hud.Current.MenuCornerRadius),
                    Padding = new Thickness(6),
                    Margin = new Thickness(14),
                    Child = panel,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 28,
                        ShadowDepth = 5,
                        Direction = 270,
                        Opacity = 0.45,
                        Color = Colors.Black
                    }
                }
            };
            _menuPopup.IsOpen = true;
        }

        private void AddMenuRow(Panel host, string text, bool marked, Action action, bool indent = false, string hint = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var marker = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Hud.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                Visibility = marked ? Visibility.Visible : Visibility.Hidden
            };
            grid.Children.Add(marker);

            var label = new TextBlock
            {
                Text = text,
                Foreground = marked ? Hud.TextBrightBrush : Hud.TextMidBrush,
                FontSize = 10.8,
                FontWeight = marked ? FontWeights.Medium : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            if (!string.IsNullOrEmpty(hint))
            {
                var hotkey = new TextBlock
                {
                    Text = hint,
                    Foreground = Hud.TextDimBrush,
                    FontSize = 7.6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(hotkey, 2);
                grid.Children.Add(hotkey);
            }

            Brush normal = Brushes.Transparent;
            var row = new Border
            {
                Background = normal,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(indent ? 13 : 8, 4.5, 9, 4.5),
                Cursor = Cursors.Hand,
                Child = grid
            };
            row.MouseEnter += (s, e) => row.Background = Hud.MenuHover;
            row.MouseLeave += (s, e) => row.Background = normal;
            row.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                CloseMenu();
                action();
            };
            host.Children.Add(row);
        }

        /// <summary>主题行：双色色板 + 名称 + 当前标记。色板取当前深浅模式下的那套颜色。</summary>
        private void AddThemeRow(Panel host, HudTheme theme, bool active, Action action)
        {
            HudPalette pal = Hud.IsLight ? theme.Light : theme.Dark;

            var swatchBrush = new LinearGradientBrush(pal.Accent, pal.Alt, 45.0);
            swatchBrush.Freeze();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(4),
                Background = swatchBrush,
                BorderBrush = Hud.Solid(Hud.IsLight ? (byte)0x30 : (byte)0x58,
                    Hud.IsLight ? Colors.Black : Colors.White),
                BorderThickness = new Thickness(0.7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            });

            var name = new TextBlock
            {
                Text = theme.Name,
                Foreground = active ? Hud.TextBrightBrush : Hud.TextMidBrush,
                FontSize = 10.8,
                FontWeight = active ? FontWeights.Medium : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var mark = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Hud.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = active ? Visibility.Visible : Visibility.Hidden
            };
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);

            Brush normal = active ? Hud.MenuActive : Brushes.Transparent;
            var row = new Border
            {
                Background = normal,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4, 9, 4),
                Cursor = Cursors.Hand,
                Child = grid
            };
            row.MouseEnter += (s, e) => row.Background = Hud.MenuHover;
            row.MouseLeave += (s, e) => row.Background = normal;
            row.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                CloseMenu();
                action();
            };
            host.Children.Add(row);
        }

        private void CloseMenu()
        {
            if (_menuPopup == null) return;
            _menuPopup.IsOpen = false;
            _menuPopup = null;
        }

        // ---------------- 鼠标穿透 ----------------

        /// <summary>穿透后鼠标事件全部落到下层窗口，只能靠全局热键 Ctrl+Alt+M 关回来。</summary>
        private void SetClickThrough(bool on)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex = on ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            _clickThrough = on;
        }

        // ---------------- 收起 / 展开动画 ----------------

        private void AnimateTo(int targetX, int targetY, double milliseconds)
        {
            StopAnimation();

            GetWindowRectPx(out int curX, out int curY, out _, out _);
            if (Math.Abs(curX - targetX) < 1 && Math.Abs(curY - targetY) < 1)
            {
                MoveWindowPx(targetX, targetY);
                return;
            }

            _animFromX = curX;
            _animToX = targetX;
            _animFromY = curY;
            _animToY = targetY;
            _animMs = milliseconds;
            _animStart = Stopwatch.GetTimestamp();

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(12)
            };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - _animStart) * 1000.0 / Stopwatch.Frequency;
            double t = _animMs <= 0 ? 1 : elapsedMs / _animMs;

            if (t >= 1)
            {
                MoveWindowPx((int)Math.Round(_animToX), (int)Math.Round(_animToY));
                StopAnimation();
                return;
            }

            // easeOutCubic：起步快、收尾稳
            double eased = 1 - Math.Pow(1 - t, 3);
            MoveWindowPx(
                (int)Math.Round(_animFromX + (_animToX - _animFromX) * eased),
                (int)Math.Round(_animFromY + (_animToY - _animFromY) * eased));
        }

        private void StopAnimation()
        {
            if (_animTimer == null) return;
            _animTimer.Stop();
            _animTimer.Tick -= OnAnimTick;
            _animTimer = null;
        }

        private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppKey) != null;
        }

        private static void SetStartup(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key == null) throw new InvalidOperationException("无法写入 Run 键");

            if (enabled)
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("取不到程序路径");
                key.SetValue(AppKey, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(AppKey, false);
            }
        }

        // ---------------- 便签 ----------------

        /// <summary>Ctrl+Alt+N / 菜单：新建一张便签，放在鼠标附近。</summary>
        private void CreateNote()
        {
            // 长按热键时 Windows 仍可能补发 WM_HOTKEY，压掉 450ms 内的重复触发，
            // 否则一按就是一串便签。
            DateTime now = DateTime.UtcNow;
            if ((now - _lastNoteCreatedAt).TotalMilliseconds < 450) return;
            _lastNoteCreatedAt = now;

            // 有便签被藏起来时，新建视为“开始用便签了”，先把旧的放出来
            if (_notesHidden)
            {
                _notesHidden = false;
                foreach (StickyNoteWindow note in _notes) note.Show();
            }

            AddNoteWindow(new NoteData(), true);
            SaveConfig();
        }

        private void AddNoteWindow(NoteData data, bool activate)
        {
            var note = new StickyNoteWindow(data);
            note.Changed += ScheduleNoteSave;
            note.TodoCompleted += ArchiveTodo;
            note.Closed += (s, e) =>
            {
                _notes.Remove(note);
                SaveConfig();
            };

            _notes.Add(note);
            if (_notesHidden) note.Hide();
            else note.Show();

            if (activate && !_notesHidden)
            {
                note.Activate();
                note.Focus();
            }
        }

        /// <summary>Ctrl+Alt+H / 菜单：隐藏或恢复全部便签。</summary>
        private void ToggleNotesHidden()
        {
            if (_notes.Count == 0) return;

            _notesHidden = !_notesHidden;
            foreach (StickyNoteWindow note in _notes)
            {
                if (_notesHidden) note.Hide();
                else note.Show();
            }
            SaveConfig();
        }

        private void RestoreNotes()
        {
            // 离屏截图时不要被用户上次留下的便签糊一脸
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSFLOAT_DUMP"))) return;
            if (_pendingNotes == null) return;

            foreach (NoteData data in _pendingNotes)
            {
                // 老配置里勾掉但还留着的待办：按新规则搬进记录，便签上只留没做的
                if (data.Todos != null)
                {
                    for (int i = data.Todos.Count - 1; i >= 0; i--)
                    {
                        if (!data.Todos[i].Done) continue;
                        ArchiveTodo(data.Todos[i], false);
                        data.Todos.RemoveAt(i);
                    }
                }

                AddNoteWindow(data, false);
            }
            _pendingNotes = null;

            if (_todoHistory.Count > 0) SaveConfig();
        }

        /// <summary>一条待办被勾掉：写进「待办记录」，最多留 500 条。</summary>
        private void ArchiveTodo(NoteTodo todo)
        {
            ArchiveTodo(todo, true);
        }

        private void ArchiveTodo(NoteTodo todo, bool save)
        {
            if (todo == null || string.IsNullOrWhiteSpace(todo.Text)) return;

            _todoHistory.Insert(0, new TodoRecord { Text = todo.Text.Trim(), DoneAt = DateTime.Now });
            if (_todoHistory.Count > MaxTodoHistory) _todoHistory.RemoveRange(MaxTodoHistory, _todoHistory.Count - MaxTodoHistory);

            _historyWindow?.Reload();
            if (save) SaveConfig();
        }

        /// <summary>右键菜单：打开待办记录窗口（已开就拉到前面）。</summary>
        private void ShowTodoHistory()
        {
            if (_historyWindow != null)
            {
                _historyWindow.Activate();
                return;
            }

            _historyWindow = new TodoHistoryWindow(_todoHistory, SaveConfig);
            _historyWindow.Closed += (s, e) => _historyWindow = null;
            _historyWindow.Show();
            _historyWindow.Activate();
        }

        /// <summary>打字时别每个字符都写盘：攒 0.7 秒再存一次。</summary>
        private void ScheduleNoteSave()
        {
            if (_noteSaveTimer == null)
            {
                _noteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _noteSaveTimer.Tick += (s, e) =>
                {
                    _noteSaveTimer.Stop();
                    SaveConfig();
                };
            }
            _noteSaveTimer.Stop();
            _noteSaveTimer.Start();
        }

        private void RefreshNotesTheme()
        {
            foreach (StickyNoteWindow note in _notes) note.RefreshTheme();
            _historyWindow?.RefreshTheme();
        }

        // ---------------- 位置与设置持久化 ----------------

        private sealed class Config
        {
            public bool HasPosition { get; set; }
            public bool PxPosition { get; set; }     // 新格式：像素坐标；旧配置是 DIP，读到了也忽略
            public int LeftPx { get; set; }
            public int TopPx { get; set; }
            public bool Topmost { get; set; } = true;
            public double Interval { get; set; } = 1;
            public bool ShowIo { get; set; } = true;
            public bool ShowSensors { get; set; } = true;
            public string Theme { get; set; } = "deepsea";
            public List<NoteData> Notes { get; set; } = new List<NoteData>();
            public bool NotesHidden { get; set; }
            public List<TodoRecord> TodoHistory { get; set; } = new List<TodoRecord>();
        }

        private static string ConfigPath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppKey,
                "config.json");

        private static Config LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) ?? new Config();
                }
            }
            catch { }
            return new Config();
        }

        private void SaveConfig()
        {
            try
            {
                // 收起状态存"展开后"的位置，下次启动就贴在边上，而不是一上来只剩一条缝
                GetWindowRectPx(out int x, out int y, out int w, out _);
                if (_tucked)
                {
                    GetMonitorRectPx(x + w / 2, y + 10, out int ml, out int mt, out int mr, out _);
                    switch (_dockEdge)
                    {
                        case DockEdge.Left: x = ml; break;
                        case DockEdge.Right: x = mr - w; break;
                        case DockEdge.Top: y = mt; break;
                    }
                }

                var cfg = new Config
                {
                    HasPosition = true,
                    PxPosition = true,
                    LeftPx = x,
                    TopPx = y,
                    Topmost = Topmost,
                    Interval = _intervalSeconds,
                    ShowIo = _showIo,
                    ShowSensors = _showSensors,
                    Theme = _themeId,
                    Notes = new List<NoteData>(),
                    NotesHidden = _notesHidden,
                    TodoHistory = _todoHistory
                };
                foreach (StickyNoteWindow note in _notes) cfg.Notes.Add(note.Data);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg));
            }
            catch { }
        }

        // ---------------- 格式化 ----------------

        /// <summary>窄版排版：容量写成 "161/200G"、"11.9/15.2G"、"0.5/1.0T"。</summary>
        private static string FmtPair(ulong used, ulong total)
        {
            const double GB = 1073741824.0;
            double totalGb = total / GB;

            if (totalGb >= 1024)
            {
                return (used / GB / 1024.0).ToString("0.00", CultureInfo.InvariantCulture)
                     + "/" + (totalGb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + "T";
            }
            if (totalGb >= 100)
            {
                return (used / GB).ToString("0", CultureInfo.InvariantCulture)
                     + "/" + totalGb.ToString("0", CultureInfo.InvariantCulture) + "G";
            }
            return (used / GB).ToString("0.0", CultureInfo.InvariantCulture)
                 + "/" + totalGb.ToString("0.0", CultureInfo.InvariantCulture) + "G";
        }

        /// <summary>速率用一两个字符的短单位，窄窗里才放得下：845K / 12.3M / 1.2G。</summary>
        private static string FmtRate(double bytesPerSec, string suffix = "")
        {
            if (bytesPerSec < 1024)
            {
                return bytesPerSec.ToString("0", CultureInfo.InvariantCulture) + "B" + suffix;
            }
            if (bytesPerSec < 1048576)
            {
                return (bytesPerSec / 1024.0).ToString("0", CultureInfo.InvariantCulture) + "K" + suffix;
            }
            if (bytesPerSec < 1073741824)
            {
                return (bytesPerSec / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "M" + suffix;
            }
            return (bytesPerSec / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + "G" + suffix;
        }
    }
}
