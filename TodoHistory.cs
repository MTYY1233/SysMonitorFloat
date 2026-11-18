using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SysMonitor
{
    /// <summary>一条已完成的待办记录。勾掉待办时自动写入，永久留档。</summary>
    internal sealed class TodoRecord
    {
        public string Text { get; set; } = string.Empty;
        public DateTime DoneAt { get; set; }
    }

    /// <summary>
    /// 待办记录窗口：按天分组列出所有勾掉过的待办，可单条删除或清空。
    /// 和便签共用同一套 HUD 主题，切深浅色时主窗口会调 RefreshTheme 重建。
    /// </summary>
    internal sealed class TodoHistoryWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const double WindowWidth = 358;

        private readonly List<TodoRecord> _records;
        private readonly Action _onChanged;

        private StackPanel _listHost;
        private TextBlock _countText;
        private bool _confirmClear;

        public TodoHistoryWindow(List<TodoRecord> records, Action onChanged)
        {
            _records = records;
            _onChanged = onChanged;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = WindowWidth;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            SnapsToDevicePixels = true;

            Left = SystemParameters.WorkArea.Right - WindowWidth - 60;
            Top = SystemParameters.WorkArea.Top + 100;

            Build();

            Loaded += (s, e) =>
            {
                UpdateLayout();
                CenterOnCursorMonitor();
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        public void RefreshTheme()
        {
            Build();
        }

        /// <summary>外部新增记录后刷新列表（窗口开着时即时更新）。</summary>
        public void Reload()
        {
            if (_listHost != null) BuildList();
        }

        /// <summary>调试/文档用：把窗口内容离屏渲染成 2 倍 PNG。</summary>
        public void SavePreview(string path)
        {
            UpdateLayout();
            if (Content is not Visual visual) return;

            int w = (int)Math.Ceiling(ActualWidth);
            int h = (int)Math.Ceiling(ActualHeight);
            if (w <= 0 || h <= 0) return;

            const double scale = 2;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Round(w * scale), (int)Math.Round(h * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(visual);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var file = System.IO.File.Create(path);
            encoder.Save(file);
        }

        // ---------------- 视觉树 ----------------

        private void Build()
        {
            HudPalette pal = Hud.Current;

            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 13) };

            // ---- 标题栏 ----
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = pal.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 8, 0)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "待办记录",
                Foreground = pal.AccentBrush,
                FontSize = 11.4,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            head.Children.Add(titleRow);

            _countText = new TextBlock
            {
                Text = _records.Count == 0 ? string.Empty : _records.Count + " 条",
                Foreground = pal.TextDimBrush,
                FontSize = 9.8,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(_countText, 1);
            head.Children.Add(_countText);

            var closeHit = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, -3, -4, 0)
            };
            var closeGlyph = new TextBlock
            {
                Text = "×",
                Foreground = pal.TextDimBrush,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            };
            closeHit.Child = closeGlyph;
            closeHit.MouseEnter += (s, e) =>
            {
                closeGlyph.Foreground = pal.CritBrush;
                closeHit.Background = pal.MenuHover;
            };
            closeHit.MouseLeave += (s, e) =>
            {
                closeGlyph.Foreground = pal.TextDimBrush;
                closeHit.Background = Brushes.Transparent;
            };
            closeHit.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                Close();
            };
            closeHit.PreviewMouseLeftButtonUp += (s, e) => e.Handled = true;
            Grid.SetColumn(closeHit, 2);
            head.Children.Add(closeHit);

            head.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                try { DragMove(); } catch { }
            };
            content.Children.Add(head);
            content.Children.Add(new Rectangle { Height = 1, Fill = pal.MenuDivider, Margin = new Thickness(0, 0, 0, 8) });

            // ---- 记录列表 ----
            _listHost = new StackPanel();
            var scroll = new ScrollViewer
            {
                MaxHeight = 430,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _listHost
            };
            content.Children.Add(scroll);

            // ---- 底栏 ----
            var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            footer.Children.Add(new TextBlock
            {
                Text = "勾掉的待办会自动归档到这里",
                Foreground = pal.TextDimBrush,
                FontSize = 9.8,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center
            });

            var clear = new TextBlock
            {
                Text = _records.Count == 0 ? string.Empty : (_confirmClear ? "确认清空？（再点一次）" : "清空记录"),
                Foreground = _confirmClear ? pal.CritBrush : pal.TextDimBrush,
                FontSize = 10.4,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            clear.MouseEnter += (s, e) => clear.Foreground = pal.CritBrush;
            clear.MouseLeave += (s, e) =>
                clear.Foreground = _confirmClear ? pal.CritBrush : pal.TextDimBrush;
            clear.MouseLeftButtonUp += (s, e) =>
            {
                if (_records.Count == 0) return;
                if (!_confirmClear)
                {
                    _confirmClear = true;
                    clear.Text = "确认清空？（再点一次）";
                    clear.Foreground = pal.CritBrush;
                    return;
                }

                _records.Clear();
                _confirmClear = false;
                _onChanged?.Invoke();
                Build();
            };
            Grid.SetColumn(clear, 1);
            footer.Children.Add(clear);

            content.Children.Add(footer);

            Content = new Border
            {
                Background = pal.Background,
                BorderBrush = pal.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Min(pal.CornerRadius, 16)),
                Child = content,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = 0.36,
                    Color = Colors.Black
                }
            };

            BuildList();
        }

        private void BuildList()
        {
            HudPalette pal = Hud.Current;
            _listHost.Children.Clear();

            if (_records.Count == 0)
            {
                _listHost.Children.Add(new TextBlock
                {
                    Text = "还没有完成过待办",
                    Foreground = pal.TextDimBrush,
                    FontSize = 11.4,
                    Opacity = 0.75,
                    Margin = new Thickness(1, 6, 0, 8)
                });
                return;
            }

            var sorted = new List<TodoRecord>(_records);
            sorted.Sort((a, b) => b.DoneAt.CompareTo(a.DoneAt));

            DateTime currentDay = DateTime.MinValue;
            foreach (TodoRecord record in sorted)
            {
                if (record.DoneAt.Date != currentDay)
                {
                    currentDay = record.DoneAt.Date;
                    _listHost.Children.Add(new TextBlock
                    {
                        Text = DayLabel(record.DoneAt),
                        Foreground = pal.TextDimBrush,
                        FontSize = 9.8,
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(1, _listHost.Children.Count == 0 ? 0 : 9, 0, 4)
                    });
                }

                _listHost.Children.Add(BuildRow(record, pal));
            }
        }

        private UIElement BuildRow(TodoRecord record, HudPalette pal)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var time = new TextBlock
            {
                Text = record.DoneAt.ToString("HH:mm"),
                Foreground = pal.TextDimBrush,
                FontSize = 10.4,
                Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(1, 1, 0, 0)
            };
            row.Children.Add(time);

            var text = new TextBlock
            {
                Text = record.Text,
                Foreground = pal.TextBrightBrush,
                FontSize = 12.2,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 2, 0)
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var del = new TextBlock
            {
                Text = "×",
                Foreground = pal.TextDimBrush,
                FontSize = 12.5,
                Opacity = 0.45,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 0, 0, 0)
            };
            del.MouseEnter += (s, e) => del.Opacity = 1.0;
            del.MouseLeave += (s, e) => del.Opacity = 0.45;
            del.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                _records.Remove(record);
                _onChanged?.Invoke();
                Build();
            };
            Grid.SetColumn(del, 2);
            row.Children.Add(del);

            return row;
        }

        private static string DayLabel(DateTime time)
        {
            DateTime today = DateTime.Today;
            if (time.Date == today) return "今天";
            if (time.Date == today.AddDays(-1)) return "昨天";
            return time.Year == today.Year ? time.ToString("M月d日") : time.ToString("yyyy年M月d日");
        }

        // ---------------- 定位 ----------------

        /// <summary>摆在鼠标所在显示器的中间偏上，主屏/副屏都对。</summary>
        private void CenterOnCursorMonitor()
        {
            try
            {
                if (!GetCursorPos(out POINT pt)) return;

                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                IntPtr mon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return;

                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
                int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);

                int workW = mi.rcWork.Right - mi.rcWork.Left;
                int workH = mi.rcWork.Bottom - mi.rcWork.Top;
                int x = mi.rcWork.Left + (workW - w) / 2;
                int y = mi.rcWork.Top + Math.Max(40, (workH - h) / 3);

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }
            }
            catch { }
        }

        // ---------------- P/Invoke ----------------

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    }
}
