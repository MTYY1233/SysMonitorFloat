using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SysMonitor
{
    /// <summary>一条待办：文字 + 是否勾掉。存进 config.json。</summary>
    internal sealed class NoteTodo
    {
        public string Text { get; set; } = string.Empty;
        public bool Done { get; set; }
    }

    /// <summary>一张便签的内容与位置。位置统一用物理像素，跨屏不同缩放也不会飘。</summary>
    internal sealed class NoteData
    {
        public bool HasPosition { get; set; }
        public bool PxPosition { get; set; }      // 新格式：像素坐标
        public int LeftPx { get; set; }
        public int TopPx { get; set; }
        public double Left { get; set; }          // 旧格式：DIP，只在读旧配置时用
        public double Top { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<NoteTodo> Todos { get; set; } = new List<NoteTodo>();
        public int Dock { get; set; }             // 0=未吸附 1=左 2=右 3=上
        public bool Tucked { get; set; }          // 已收进屏幕边
    }

    /// <summary>
    /// 桌面便签：拖动标题栏移动（松手贴边自动吸附，拖出屏幕边收成一条），
    /// 上半部分自由文字，下半部分待办清单。配色取当前 HUD 主题，切主题/切深浅色时主窗口调 RefreshTheme 重建。
    /// </summary>
    internal sealed class StickyNoteWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const double NoteWidth = 302;      // 卡片宽度（DIP）
        private const double SnapDistance = 18;    // 离屏幕边这么近，松手就贴过去
        private const double TuckSliver = 14;      // 收起后露在外面的厚度
        private const double TuckRatio = 0.45;     // 推出屏幕超过这个比例就收起
        private const int DestroyDelayMs = 850;    // 勾掉到从便签上消失之间的停顿

        private StackPanel _todoHost;
        private TextBlock _todoCount;
        private TextBlock _todoEmpty;
        private TextBox _body;
        private TextBox _todoInput;
        private TextBlock _todoPlaceholder;

        // 拖动 / 吸附 / 收起
        private bool _dragging;
        private bool _dragMoved;
        private int _dragCursorX, _dragCursorY;
        private int _dragWinX, _dragWinY;
        private HudDockSide _dockSide = HudDockSide.None;
        private bool _tucked;
        private bool _peeking;
        private DateTime _leaveAt = DateTime.MaxValue;
        private DispatcherTimer _peekTimer;
        private DispatcherTimer _moveTimer;
        private readonly List<DispatcherTimer> _destroyTimers = new List<DispatcherTimer>();

        public NoteData Data { get; }

        /// <summary>内容或位置有变化（打字、勾选、拖动），由主窗口做防抖落盘。</summary>
        public event Action Changed;

        /// <summary>一条待办被勾掉、随后从便签上销毁；主窗口负责把它归档进「待办记录」。</summary>
        public event Action<NoteTodo> TodoCompleted;

        public StickyNoteWindow(NoteData data)
        {
            Data = data;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = NoteWidth;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            SnapsToDevicePixels = true;

            // 先给个合理位置，避免窗口在 OnSourceInitialized 之前闪在左上角
            Left = SystemParameters.WorkArea.Right - NoteWidth - 40;
            Top = SystemParameters.WorkArea.Top + 80;

            Build();

            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += (s, e) =>
            {
                if (!(bool)e.NewValue) StopPeekTimer();
                else if (_tucked) StartPeekTimer();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);   // 不进 Alt+Tab
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateLayout();

            // 离屏截图时把便签挪到看不见的地方，别挡住主卡片
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSFLOAT_NOTE_DUMP")))
            {
                MoveWindowPx(-20000, -20000);
                return;
            }

            if (Data.HasPosition) RestorePosition();
            else PlaceNearCursor();

            Data.HasPosition = true;
        }

        /// <summary>切主题/切深浅色后按新调色板重建视觉树，文字、待办和未提交的输入都保留。</summary>
        public void RefreshTheme()
        {
            string body = _body != null ? _body.Text : Data.Text;
            string input = _todoInput != null ? _todoInput.Text : string.Empty;

            Build();

            if (_body != null) _body.Text = body;
            if (_todoInput != null && !string.IsNullOrEmpty(input))
            {
                _todoInput.Text = input;
                _todoInput.CaretIndex = input.Length;
            }

            if (_tucked) RepositionTucked();
        }

        /// <summary>调试/文档用：把卡片离屏渲染成 2 倍 PNG（Transparent 背景需要 AllowsTransparency 窗口）。</summary>
        public void SavePreview(string path)
        {
            UpdateLayout();
            if (Content is not Visual visual) return;

            int w = (int)Math.Ceiling(ActualWidth);
            int h = (int)Math.Ceiling(ActualHeight);
            if (w <= 0 || h <= 0) return;

            const double scale = 2;
            var bmp = new RenderTargetBitmap(
                (int)Math.Round(w * scale), (int)Math.Round(h * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var file = System.IO.File.Create(path);
            encoder.Save(file);
        }

        // ---------------- 视觉树 ----------------

        private void Build()
        {
            HudPalette pal = Hud.Current;

            // 这几个控件每次重建都新建：老实例还挂在旧的可视树上，
            // 直接复用到新容器会抛「already the logical child of another element」。
            _todoHost = new StackPanel();
            _todoCount = new TextBlock();
            _todoEmpty = new TextBlock();

            var content = new StackPanel { Margin = new Thickness(15, 11, 15, 12) };

            // ---- 标题栏：拖动区 + 删除 ----
            var head = new Grid { Margin = new Thickness(0, 0, 0, 7), Background = Brushes.Transparent };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = pal.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 8, 0)
            };
            head.Children.Add(dot);

            var title = new TextBlock
            {
                Text = "便签",
                Foreground = pal.AccentBrush,
                FontSize = 11.4,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            // × 做成 22×22 的独立热区：标题栏的拖动手势不许抢它
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
                e.Handled = true;   // 先于标题栏的拖动处理，吃掉了就不会开始拖
                CloseNote();
            };
            closeHit.PreviewMouseLeftButtonUp += (s, e) => e.Handled = true;
            Grid.SetColumn(closeHit, 2);
            head.Children.Add(closeHit);

            head.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                BeginDrag();
            };
            content.Children.Add(head);
            content.Children.Add(new Rectangle { Height = 1, Fill = pal.MenuDivider, Margin = new Thickness(0, 0, 0, 8) });

            // ---- 自由文字 ----
            _body = new TextBox
            {
                Text = Data.Text ?? string.Empty,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = pal.TextBrightBrush,
                CaretBrush = pal.AccentBrush,
                SelectionBrush = pal.AccentBrush,
                SelectionOpacity = 0.32,
                FontSize = 13.6,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 96,
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _body.TextChanged += (s, e) =>
            {
                Data.Text = _body.Text;
                Changed?.Invoke();
            };
            content.Children.Add(_body);

            // ---- 待办标题 + 计数 ----
            var todoHead = new Grid { Margin = new Thickness(0, 10, 0, 5) };
            todoHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            todoHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            todoHead.Children.Add(new TextBlock
            {
                Text = "待办",
                Foreground = pal.TextDimBrush,
                FontSize = 10.8,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            _todoCount.Foreground = pal.TextDimBrush;
            _todoCount.FontSize = 9.8;
            _todoCount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_todoCount, 1);
            todoHead.Children.Add(_todoCount);
            content.Children.Add(todoHead);

            var todoScroll = new ScrollViewer
            {
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _todoHost
            };
            content.Children.Add(todoScroll);

            _todoEmpty.Text = "还没有待办";
            _todoEmpty.Foreground = pal.TextDimBrush;
            _todoEmpty.FontSize = 11.2;
            _todoEmpty.Opacity = 0.75;
            _todoEmpty.Margin = new Thickness(1, 2, 0, 3);

            // ---- 添加行 ----
            var addGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            addGrid.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = pal.AccentBrush,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var inputHost = new Grid();
            _todoPlaceholder = new TextBlock
            {
                Text = "添加待办，回车确认",
                Foreground = pal.TextDimBrush,
                FontSize = 11.6,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 0, 0),
                IsHitTestVisible = false
            };
            inputHost.Children.Add(_todoPlaceholder);

            _todoInput = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = pal.TextMidBrush,
                CaretBrush = pal.AccentBrush,
                SelectionBrush = pal.AccentBrush,
                SelectionOpacity = 0.32,
                FontSize = 12.4,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _todoInput.TextChanged += (s, e) =>
                _todoPlaceholder.Visibility = string.IsNullOrEmpty(_todoInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            _todoInput.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTodo();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _todoInput.Text = string.Empty;
                    e.Handled = true;
                }
            };
            inputHost.Children.Add(_todoInput);

            Grid.SetColumn(inputHost, 1);
            addGrid.Children.Add(inputHost);
            content.Children.Add(addGrid);

            var shell = new Border
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
            Content = shell;

            RebuildTodos();
        }

        private void CloseNote()
        {
            FlushCompleted();   // 还有勾掉没来得及销毁的，先归档，别丢
            StopPeekTimer();
            StopAnimation();
            Close();   // 主窗口在 Closed 里把它从列表移除并落盘
        }

        /// <summary>把已经勾掉但还没销毁的待办立刻归档并移除（关窗/退出前收尾用）。</summary>
        public void FlushCompleted()
        {
            foreach (DispatcherTimer timer in _destroyTimers) timer.Stop();
            _destroyTimers.Clear();

            bool removed = false;
            for (int i = Data.Todos.Count - 1; i >= 0; i--)
            {
                NoteTodo todo = Data.Todos[i];
                if (!todo.Done) continue;

                Data.Todos.RemoveAt(i);
                TodoCompleted?.Invoke(todo);
                removed = true;
            }

            if (removed)
            {
                RebuildTodos();
                Changed?.Invoke();
            }
        }

        private void RebuildTodos()
        {
            HudPalette pal = Hud.Current;
            _todoHost.Children.Clear();

            int remain = Data.Todos.Count;   // 勾掉的会自动销毁，这里就是“还没做的条数”
            _todoCount.Text = remain == 0 ? string.Empty : remain + " 项";

            if (Data.Todos.Count == 0)
            {
                _todoHost.Children.Add(_todoEmpty);
                return;
            }

            foreach (NoteTodo todo in Data.Todos) _todoHost.Children.Add(BuildTodoRow(todo, pal));
        }

        private UIElement BuildTodoRow(NoteTodo todo, HudPalette pal)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(21) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = pal.AccentBrush,
                Background = todo.Done ? pal.AccentBrush : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 0),
                Opacity = todo.Done ? 1.0 : 0.55,
                Cursor = Cursors.Hand
            };
            var tick = new TextBlock
            {
                Text = "✓",
                Foreground = pal.IsLight ? Brushes.White : Brushes.Black,
                FontSize = 9.6,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = todo.Done ? Visibility.Visible : Visibility.Collapsed,
                IsHitTestVisible = false,
                Margin = new Thickness(0, -1, 0, 0)
            };
            box.Child = tick;
            box.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                todo.Done = !todo.Done;
                RebuildTodos();
                Changed?.Invoke();

                // 勾上留 850ms 让用户看到那一下绿色/高亮，然后从便签上销毁并归档
                if (todo.Done) ScheduleDestroy(todo);
            };
            row.Children.Add(box);

            var text = new TextBlock
            {
                Text = todo.Text,
                Foreground = todo.Done ? pal.TextDimBrush : pal.TextBrightBrush,
                FontSize = 12.6,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 2, 0),
                TextDecorations = todo.Done ? TextDecorations.Strikethrough : null
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var del = new TextBlock
            {
                Text = "×",
                Foreground = pal.TextDimBrush,
                FontSize = 13,
                Opacity = 0.5,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            del.MouseEnter += (s, e) => del.Opacity = 1.0;
            del.MouseLeave += (s, e) => del.Opacity = 0.5;
            del.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Data.Todos.Remove(todo);
                RebuildTodos();
                Changed?.Invoke();
            };
            Grid.SetColumn(del, 2);
            row.Children.Add(del);

            return row;
        }

        private void CommitTodo()
        {
            string text = _todoInput.Text != null ? _todoInput.Text.Trim() : string.Empty;
            if (text.Length == 0) return;

            Data.Todos.Add(new NoteTodo { Text = text, Done = false });
            _todoInput.Text = string.Empty;
            RebuildTodos();
            Changed?.Invoke();
        }

        /// <summary>勾掉的待办延时销毁：先让勾选动效露个脸，再移除并通知主窗口归档。</summary>
        private void ScheduleDestroy(NoteTodo todo)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(DestroyDelayMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _destroyTimers.Remove(timer);

                if (!todo.Done) return;                       // 这段时间里又被取消勾选
                if (!Data.Todos.Remove(todo)) return;

                TodoCompleted?.Invoke(todo);
                RebuildTodos();
                Changed?.Invoke();
            };
            _destroyTimers.Add(timer);
            timer.Start();
        }

        // ---------------- 拖动 / 吸附 / 收起 ----------------

        private void BeginDrag()
        {
            LeaveDock();
            StopAnimation();

            GetCursorPos(out POINT p);
            _dragCursorX = p.X;
            _dragCursorY = p.Y;
            GetWindowRectPx(out _dragWinX, out _dragWinY, out _, out _);

            _dragging = true;
            _dragMoved = false;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            GetCursorPos(out POINT p);
            if (!_dragMoved
                && (Math.Abs(p.X - _dragCursorX) > 2 || Math.Abs(p.Y - _dragCursorY) > 2))
            {
                _dragMoved = true;
            }
            if (!_dragMoved) return;

            int rawX = _dragWinX + (p.X - _dragCursorX);
            int rawY = _dragWinY + (p.Y - _dragCursorY);

            GetWindowRectPx(out _, out _, out int w, out int h);
            GetVirtualScreenPx(out int vl, out int vt, out int vr, out int vb);

            // 允许越过最外侧屏幕边一点，这样才推得出去收起；再多就拦住，别把便签弄丢
            int over = PadX(160);
            int x = Math.Max(vl - over, Math.Min(rawX, vr - w + over));
            int y = Math.Max(vt - over, Math.Min(rawY, vb - h + over));

            MoveWindowPx(x, y);
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
            if (_dragging) _dragging = false;
        }

        private void ApplyDrop()
        {
            if (!_dragMoved) return;

            GetWindowRectPx(out int x, out int y, out int w, out int h);
            int cx = x + w / 2;
            int cy = y + h / 2;
            GetMonitorRectPx(cx, cy, out int ml, out int mt, out int mr, out int mb);

            // 只有这条边外面没有别的显示器时，才允许推出去收起
            bool outerLeft = !HasMonitorAt(ml - 5, cy);
            bool outerRight = !HasMonitorAt(mr + 5, cy);
            bool outerTop = !HasMonitorAt(cx, mt - 5);

            int sliver = PadX(TuckSliver);
            int overLeft = outerLeft ? ml - x : int.MinValue;
            int overRight = outerRight ? (x + w) - mr : int.MinValue;
            int overTop = outerTop ? mt - y : int.MinValue;

            int needX = (int)Math.Max(sliver + 10, (w - sliver) * TuckRatio);
            int needY = (int)Math.Max(sliver + 10, (h - sliver) * TuckRatio);

            if (overLeft >= needX) { Tuck(HudDockSide.Left); return; }
            if (overRight >= needX) { Tuck(HudDockSide.Right); return; }
            if (overTop >= needY) { Tuck(HudDockSide.Top); return; }

            // 贴边吸附：四个方向各自判断，角落可以同时贴两条边
            int snap = PadX(SnapDistance);
            int tx = x;
            int ty = y;

            if (outerLeft && x - ml <= snap) tx = ml;
            else if (outerRight && mr - (x + w) <= snap) tx = mr - w;
            if (outerTop && y - mt <= snap) ty = mt;
            else if (mb - (y + h) <= snap) ty = mb - h;

            // 落在别的屏中间时，别让它一半探到屏幕外的空白里
            tx = Math.Max(ml, Math.Min(tx, Math.Max(ml, mr - w)));
            ty = Math.Max(mt, Math.Min(ty, Math.Max(mt, mb - h)));

            LeaveDock();
            if (tx != x || ty != y) AnimateTo(tx, ty, 150);
            else MoveWindowPx(tx, ty);

            SavePosition(tx, ty);
        }

        /// <summary>贴边位置与收起位置（物理像素）。收起时只在屏幕边留一条 sliver。</summary>
        private void GetDockPositions(out int flushX, out int flushY, out int tuckX, out int tuckY)
        {
            GetWindowRectPx(out int x, out int y, out int w, out int h);
            GetMonitorRectPx(x + w / 2, y + h / 2, out int ml, out int mt, out int mr, out int mb);

            int sliver = PadX(TuckSliver);
            flushX = x;
            flushY = y;
            tuckX = x;
            tuckY = y;

            switch (_dockSide)
            {
                case HudDockSide.Left:
                    flushX = ml;
                    flushY = Math.Max(mt, Math.Min(y, Math.Max(mt, mb - h)));
                    tuckX = ml - (w - sliver);
                    tuckY = flushY;
                    break;

                case HudDockSide.Right:
                    flushX = mr - w;
                    flushY = Math.Max(mt, Math.Min(y, Math.Max(mt, mb - h)));
                    tuckX = mr - sliver;
                    tuckY = flushY;
                    break;

                case HudDockSide.Top:
                    flushX = Math.Max(ml, Math.Min(x, Math.Max(ml, mr - w)));
                    flushY = mt;
                    tuckX = flushX;
                    tuckY = mt - (h - sliver);
                    break;
            }
        }

        private void Tuck(HudDockSide side)
        {
            _dockSide = side;
            _tucked = true;
            _peeking = false;
            _leaveAt = DateTime.MaxValue;

            GetDockPositions(out int flushX, out int flushY, out int tuckX, out int tuckY);

            Data.Dock = (int)side;
            Data.Tucked = true;
            SavePosition(flushX, flushY);

            AnimateTo(tuckX, tuckY, 170);
            StartPeekTimer();
        }

        private void Peek()
        {
            if (!_tucked) return;

            GetDockPositions(out int flushX, out int flushY, out _, out _);
            _peeking = true;
            _leaveAt = DateTime.MaxValue;
            AnimateTo(flushX, flushY, 130);
        }

        private void LeaveDock()
        {
            StopPeekTimer();
            _dockSide = HudDockSide.None;
            _tucked = false;
            _peeking = false;
            _leaveAt = DateTime.MaxValue;

            if (Data.Dock != 0 || Data.Tucked)
            {
                Data.Dock = 0;
                Data.Tucked = false;
                Changed?.Invoke();
            }
        }

        private void RepositionTucked()
        {
            GetDockPositions(out int flushX, out int flushY, out int tuckX, out int tuckY);
            if (_peeking) MoveWindowPx(flushX, flushY);
            else MoveWindowPx(tuckX, tuckY);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_tucked) RepositionTucked();
        }

        // ---- 收起状态下轮询光标：探出 / 缩回 ----

        private void StartPeekTimer()
        {
            if (_peekTimer == null)
            {
                _peekTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(120)
                };
                _peekTimer.Tick += OnPeekTick;
            }
            _peekTimer.Start();
        }

        private void StopPeekTimer()
        {
            _peekTimer?.Stop();
        }

        private void OnPeekTick(object sender, EventArgs e)
        {
            if (!_tucked || !IsVisible)
            {
                StopPeekTimer();
                return;
            }

            GetCursorPos(out POINT cursor);
            GetWindowRectPx(out int wx, out int wy, out int w, out int h);

            if (!_peeking)
            {
                if (cursor.X >= wx - 6 && cursor.X <= wx + w + 6
                    && cursor.Y >= wy - 6 && cursor.Y <= wy + h + 6)
                {
                    Peek();
                }
                return;
            }

            bool inside = cursor.X >= wx - 14 && cursor.X <= wx + w + 14
                       && cursor.Y >= wy - 14 && cursor.Y <= wy + h + 14;

            if (inside)
            {
                _leaveAt = DateTime.MaxValue;
                return;
            }

            if (_leaveAt == DateTime.MaxValue)
            {
                _leaveAt = DateTime.UtcNow.AddMilliseconds(420);
            }
            else if (DateTime.UtcNow >= _leaveAt)
            {
                _leaveAt = DateTime.MaxValue;
                _peeking = false;
                GetDockPositions(out _, out _, out int tuckX, out int tuckY);
                AnimateTo(tuckX, tuckY, 150);
            }
        }

        // ---------------- 动画 ----------------

        private void AnimateTo(int targetX, int targetY, int ms)
        {
            StopAnimation();

            GetWindowRectPx(out int startX, out int startY, out _, out _);
            if (startX == targetX && startY == targetY)
            {
                MoveWindowPx(targetX, targetY);
                return;
            }

            long start = Stopwatch.GetTimestamp();
            _moveTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _moveTimer.Tick += (s, e) =>
            {
                double t = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / Math.Max(1, ms);
                if (t >= 1)
                {
                    StopAnimation();
                    MoveWindowPx(targetX, targetY);
                    return;
                }

                double k = 1 - Math.Pow(1 - t, 3);   // ease-out cubic
                MoveWindowPx(
                    startX + (int)Math.Round((targetX - startX) * k),
                    startY + (int)Math.Round((targetY - startY) * k));
            };
            _moveTimer.Start();
        }

        private void StopAnimation()
        {
            _moveTimer?.Stop();
            _moveTimer = null;
        }

        // ---------------- 定位 ----------------

        private void SavePosition(int x, int y)
        {
            Data.HasPosition = true;
            Data.PxPosition = true;
            Data.LeftPx = x;
            Data.TopPx = y;
            Changed?.Invoke();
        }

        /// <summary>恢复上次的位置；旧配置是 DIP，按系统缩放换算成像素。</summary>
        private void RestorePosition()
        {
            int x, y;
            if (Data.PxPosition)
            {
                x = Data.LeftPx;
                y = Data.TopPx;
            }
            else
            {
                double scale = SystemDpiScale();
                x = (int)Math.Round(Data.Left * scale);
                y = (int)Math.Round(Data.Top * scale);
                Data.PxPosition = true;
            }

            // 分辨率或缩放变了以后旧坐标可能落在屏幕外，先拉回最近的显示器
            GetMonitorRectPx(x + (int)(Width / 2), y + 40, out int ml, out int mt, out int mr, out int mb);
            int w = PadX(ActualWidth > 0 ? ActualWidth : NoteWidth);
            int h = PadY(ActualHeight > 0 ? ActualHeight : 160);
            x = Math.Max(ml, Math.Min(x, Math.Max(ml, mr - w)));
            y = Math.Max(mt, Math.Min(y, Math.Max(mt, mb - h)));

            MoveWindowPx(x, y);
            Data.LeftPx = x;
            Data.TopPx = y;

            if (Data.Tucked && Data.Dock != 0)
            {
                _dockSide = (HudDockSide)Data.Dock;
                _tucked = true;
                _peeking = false;
                RepositionTucked();
                StartPeekTimer();
            }
        }

        /// <summary>新便签出现在鼠标所在屏幕（主屏或副屏）的附近。</summary>
        private void PlaceNearCursor()
        {
            try
            {
                if (!GetCursorPos(out POINT pt)) return;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                IntPtr mon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref info)) return;

                int w = PadX(ActualWidth > 0 ? ActualWidth : NoteWidth);
                int h = PadY(ActualHeight > 0 ? ActualHeight : 160);

                int x = pt.X - w / 2;
                int y = pt.Y - 40;

                x = Math.Max(info.rcWork.Left + 8, Math.Min(x, Math.Max(info.rcWork.Left + 8, info.rcWork.Right - w - 8)));
                y = Math.Max(info.rcWork.Top + 8, Math.Min(y, Math.Max(info.rcWork.Top + 8, info.rcWork.Bottom - h - 8)));

                MoveWindowPx(x, y);
                SavePosition(x, y);
            }
            catch { }
        }

        private int PadX(double dip) => (int)Math.Round(dip * VisualTreeHelper.GetDpi(this).DpiScaleX);
        private int PadY(double dip) => (int)Math.Round(dip * VisualTreeHelper.GetDpi(this).DpiScaleY);

        /// <summary>旧配置（DIP）换算用：系统 DPI 缩放。</summary>
        private static double SystemDpiScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 48) return dpi / 96.0;
            }
            catch { }
            return 1.0;
        }

        // ---------------- 窗口位置（物理像素） ----------------

        private void MoveWindowPx(int x, int y)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private void GetWindowRectPx(out int left, out int top, out int width, out int height)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
            {
                left = r.Left;
                top = r.Top;
                width = r.Right - r.Left;
                height = r.Bottom - r.Top;
                return;
            }
            left = 0;
            top = 0;
            width = PadX(NoteWidth);
            height = PadY(160);
        }

        private static void GetVirtualScreenPx(out int left, out int top, out int right, out int bottom)
        {
            left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        }

        private static void GetMonitorRectPx(int x, int y, out int left, out int top, out int right, out int bottom)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            IntPtr mon = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            {
                left = mi.rcMonitor.Left;
                top = mi.rcMonitor.Top;
                right = mi.rcMonitor.Right;
                bottom = mi.rcMonitor.Bottom;
                return;
            }
            GetVirtualScreenPx(out left, out top, out right, out bottom);
        }

        /// <summary>这个点是否落在某块显示器上（判断边的外面还有没有屏）。</summary>
        private static bool HasMonitorAt(int x, int y)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            IntPtr mon = MonitorFromPoint(new POINT { X = x, Y = y }, 2);
            if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return false;
            return x >= mi.rcMonitor.Left && x < mi.rcMonitor.Right
                && y >= mi.rcMonitor.Top && y < mi.rcMonitor.Bottom;
        }

        // ---------------- P/Invoke ----------------

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

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
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();
    }
}
