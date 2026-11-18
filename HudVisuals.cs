using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SysMonitor
{
    internal enum HudCornerStyle
    {
        Chamfer,
        Round
    }

    /// <summary>吸附反馈画在哪条边上。Bottom 只做贴边、不做收起。</summary>
    internal enum HudDockSide
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>主题的颜色与形状描述。深色/浅色各一份，派生画笔统一在 HudPalette 里完成。</summary>
    internal sealed class HudThemeSpec
    {
        public bool IsLight;
        public bool Ambient;              // 氛围主题：无网格/扫描线/电平段，连续圆角条 + 柔光曲线
        public HudCornerStyle CornerStyle;
        public double CornerRadius;
        public bool ShowRails;
        public bool ShowNotch;

        public Color Accent;
        public Color AccentSoft;
        public Color Alt;
        public Color AltSoft;
        public Color Warn;
        public Color Crit;
        public Color TextBright;
        public Color TextMid;
        public Color TextDim;

        public Color BgA;
        public Color BgB;
        public Color BgC;                 // Ambient 的中间色
        public double BgMidStop = 0.5;
        public Color GlowA;               // 左上氛围光；透明则退回 Accent
        public Color GlowB;               // 右下氛围光；透明则退回 Alt
        public Color BorderA;
        public Color BorderB;
        public Color Track;
        public byte BgAlpha = 0xF5;
    }

    /// <summary>
    /// 一套已经 Freeze 的 HUD 画笔。8 个主题 × 深浅两套 = 16 个实例，
    /// 启动时一次建好，切换主题只换引用。
    /// </summary>
    internal sealed class HudPalette
    {
        public readonly bool IsLight;
        public readonly bool Ambient;
        public readonly string MemoryLabel;
        public readonly string NetworkLabel;
        public readonly string HistoryLabel;
        public readonly string LiveLabel;
        public readonly HudCornerStyle CornerStyle;
        public readonly double CornerRadius;
        public readonly bool ShowRails;
        public readonly bool ShowNotch;
        public readonly int MenuCornerRadius;

        public readonly Color Accent;
        public readonly Color AccentSoft;
        public readonly Color Alt;
        public readonly Color AltSoft;
        public readonly Color Warn;
        public readonly Color Crit;
        public readonly Color TextBright;
        public readonly Color TextMid;
        public readonly Color TextDim;

        public readonly SolidColorBrush AccentBrush;
        public readonly SolidColorBrush AccentSoftBrush;
        public readonly SolidColorBrush AltBrush;
        public readonly SolidColorBrush AltSoftBrush;
        public readonly SolidColorBrush WarnBrush;
        public readonly SolidColorBrush CritBrush;
        public readonly SolidColorBrush TextBrightBrush;
        public readonly SolidColorBrush TextMidBrush;
        public readonly SolidColorBrush TextDimBrush;

        public readonly SolidColorBrush TrackBrush;
        public readonly SolidColorBrush WhiteHotBrush;
        public readonly SolidColorBrush DotAuraBrush;
        public readonly SolidColorBrush ReadTagBrush;
        public readonly SolidColorBrush WriteTagBrush;
        public readonly SolidColorBrush NotchBrush;

        public readonly SolidColorBrush MenuBg;
        public readonly SolidColorBrush MenuHover;
        public readonly SolidColorBrush MenuActive;
        public readonly SolidColorBrush MenuBorder;
        public readonly SolidColorBrush MenuDivider;

        public readonly LinearGradientBrush Background;
        public readonly LinearGradientBrush GlassSheen;
        public readonly LinearGradientBrush BottomShade;
        public readonly LinearGradientBrush DividerBrush;
        public readonly LinearGradientBrush PulseBrush;
        public readonly LinearGradientBrush BorderBrush;
        public readonly LinearGradientBrush SparkArea;
        public readonly LinearGradientBrush DockBeamLeft;
        public readonly LinearGradientBrush DockBeamRight;
        public readonly LinearGradientBrush DockBeamTop;
        public readonly LinearGradientBrush DockBeamBottom;
        public readonly LinearGradientBrush AccentSegFill;
        public readonly LinearGradientBrush WarnSegFill;
        public readonly LinearGradientBrush CritSegFill;
        public readonly SolidColorBrush AccentSegGlow;
        public readonly SolidColorBrush WarnSegGlow;
        public readonly SolidColorBrush CritSegGlow;

        public readonly DrawingBrush GridTile;
        public readonly DrawingBrush ScanTile;
        public readonly RadialGradientBrush AccentCorner;
        public readonly RadialGradientBrush AltCorner;

        public readonly Pen BorderPen;
        public readonly Pen BorderHoverPen;
        public readonly Pen BorderGlowAccent;
        public readonly Pen BorderGlowAlt;
        public readonly Pen CardShadowWide;
        public readonly Pen CardShadow;
        public readonly Pen InnerRimPen;
        public readonly Pen TopLightPen;
        public readonly Pen TrackShadePen;
        public readonly Pen DockGlow;
        public readonly Pen DockPen;
        public readonly Pen DockRailPen;
        public readonly Pen RailMajor;
        public readonly Pen RailMinor;
        public readonly Pen BracketAccent;
        public readonly Pen BracketAlt;
        public readonly Pen InnerEdgePen;
        public readonly Pen SparkGridPen;
        public readonly Pen SparkDownGlow;
        public readonly Pen SparkDownPen;
        public readonly Pen SparkUpGlow;
        public readonly Pen SparkUpPen;
        public readonly Pen SparkDownSoftPen;
        public readonly Pen SparkUpSoftPen;
        public readonly Pen SparkCursorPen;
        public readonly Pen SegTipPen;

        public readonly SolidColorBrush DownHeadAura;
        public readonly SolidColorBrush UpHeadAura;

        public HudPalette(HudThemeSpec spec)
        {
            IsLight = spec.IsLight;
            Ambient = spec.Ambient;
            MemoryLabel = "MEMORY";
            NetworkLabel = "NETWORK";
            HistoryLabel = "HISTORY";
            LiveLabel = "LIVE";
            CornerStyle = spec.CornerStyle;
            CornerRadius = spec.CornerRadius;
            ShowRails = spec.ShowRails;
            ShowNotch = spec.ShowNotch;
            MenuCornerRadius = spec.CornerStyle == HudCornerStyle.Round ? 14 : 8;

            Accent = spec.Accent;
            AccentSoft = spec.AccentSoft;
            Alt = spec.Alt;
            AltSoft = spec.AltSoft;
            Warn = spec.Warn;
            Crit = spec.Crit;
            TextBright = spec.TextBright;
            TextMid = spec.TextMid;
            TextDim = spec.TextDim;

            AccentBrush = S(255, Accent);
            AccentSoftBrush = S(255, AccentSoft);
            AltBrush = S(255, Alt);
            AltSoftBrush = S(255, AltSoft);
            WarnBrush = S(255, Warn);
            CritBrush = S(255, Crit);
            TextBrightBrush = S(255, TextBright);
            TextMidBrush = S(255, TextMid);
            TextDimBrush = S(255, TextDim);

            TrackBrush = S(255, spec.Track);
            WhiteHotBrush = S(0xE6, Colors.White);
            DotAuraBrush = S(0x32, Accent);
            ReadTagBrush = S(0xAA, Accent);
            WriteTagBrush = S(0xAA, Alt);
            NotchBrush = S(0x66, Accent);

            Color menuTint = IsLight ? Lighten(spec.BgA, 0.55) : Darken(spec.BgA, 0.30);
            MenuBg = S(IsLight ? (byte)0xFA : (byte)0xF6, menuTint);
            MenuHover = S(IsLight ? (byte)0x12 : (byte)0x20, IsLight ? Darken(Accent, 0.05) : Accent);
            MenuActive = S(IsLight ? (byte)0x16 : (byte)0x24, Accent);
            MenuBorder = S(IsLight ? (byte)0x58 : (byte)0x62, Accent);
            MenuDivider = S(IsLight ? (byte)0x1E : (byte)0x24, Accent);

            if (spec.Ambient)
            {
                // 氛围主题用「天空 → 地面」的三段竖向渐变，更贴近参考图
                var bg = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
                bg.GradientStops.Add(new GradientStop(
                    Color.FromArgb(spec.BgAlpha, spec.BgA.R, spec.BgA.G, spec.BgA.B), 0));
                bg.GradientStops.Add(new GradientStop(
                    Color.FromArgb(spec.BgAlpha, spec.BgC.R, spec.BgC.G, spec.BgC.B), spec.BgMidStop));
                bg.GradientStops.Add(new GradientStop(
                    Color.FromArgb(spec.BgAlpha, spec.BgB.R, spec.BgB.G, spec.BgB.B), 1));
                bg.Freeze();
                Background = bg;
            }
            else
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(spec.BgAlpha, spec.BgA.R, spec.BgA.G, spec.BgA.B),
                    Color.FromArgb(spec.BgAlpha, spec.BgB.R, spec.BgB.G, spec.BgB.B),
                    new Point(0.05, 0), new Point(0.95, 1));
                Background.Freeze();
            }

            // 玻璃质感：顶部一层很淡的受光，底部压一点暗，避免大面积渐变显得平。
            var sheen = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            sheen.GradientStops.Add(new GradientStop(
                Color.FromArgb(IsLight ? (byte)0x70 : (byte)0x24, 255, 255, 255), 0));
            sheen.GradientStops.Add(new GradientStop(
                Color.FromArgb(IsLight ? (byte)0x18 : (byte)0x08, 255, 255, 255), 0.10));
            sheen.GradientStops.Add(new GradientStop(Colors.Transparent, 0.34));
            sheen.Freeze();
            GlassSheen = sheen;

            var shade = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            shade.GradientStops.Add(new GradientStop(Colors.Transparent, 0.52));
            shade.GradientStops.Add(new GradientStop(
                Color.FromArgb(IsLight ? (byte)0x10 : (byte)0x2C, 0, 0, 0), 1));
            shade.Freeze();
            BottomShade = shade;

            byte dividerAlpha = IsLight ? (byte)0x38 : (byte)0x4C;
            DividerBrush = H(
                new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 0),
                new GradientStop(Color.FromArgb(dividerAlpha, Accent.R, Accent.G, Accent.B), 0.22),
                new GradientStop(Color.FromArgb((byte)(dividerAlpha / 3), Accent.R, Accent.G, Accent.B), 0.72),
                new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1));

            PulseBrush = H(
                new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 0),
                new GradientStop(Color.FromArgb(0xC0, AccentSoft.R, AccentSoft.G, AccentSoft.B), 0.5),
                new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1));

            BorderBrush = MakeBorder(spec.BorderA, spec.BorderB, IsLight ? (byte)0xC0 : (byte)0xD0);
            BorderPen = P(BorderBrush, 1.0);
            BorderHoverPen = P(S(IsLight ? (byte)0x78 : (byte)0x88, Accent), 1.1);
            BorderGlowAccent = P(S(IsLight ? (byte)0x07 : (byte)0x0C, Accent), 3.2);
            BorderGlowAlt = P(S(IsLight ? (byte)0x04 : (byte)0x06, Alt), 5.0);
            CardShadowWide = P(S(IsLight ? (byte)0x12 : (byte)0x20, Colors.Black), 9.0);
            CardShadow = P(S(IsLight ? (byte)0x18 : (byte)0x2A, Colors.Black), 4.0);
            InnerRimPen = P(S(IsLight ? (byte)0x24 : (byte)0x1E, Colors.White), 0.7);
            TopLightPen = P(H(
                new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(IsLight ? (byte)0x50 : (byte)0x2A, 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 1)), 0.9);
            TrackShadePen = P(S(IsLight ? (byte)0x12 : (byte)0x2C, Colors.Black), 0.8);
            DockGlow = P(S(0x50, Accent), 4.2);
            DockPen = P(S(0xF0, Accent), 1.5);
            DockRailPen = PRound(S(0xFF, IsLight ? Accent : AccentSoft), 2.0);
            RailMajor = P(S(IsLight ? (byte)0x45 : (byte)0x55, Accent), 0.9);
            RailMinor = P(S(IsLight ? (byte)0x1A : (byte)0x20, Accent), 0.8);
            BracketAccent = P(S(IsLight ? (byte)0x70 : (byte)0x86, Accent), 0.9);
            BracketAlt = P(S(IsLight ? (byte)0x60 : (byte)0x72, Alt), 0.9);
            InnerEdgePen = P(S(IsLight ? (byte)0x16 : (byte)0x12, Accent), 0.8);

            AccentSegFill = V(Lighten(Accent, IsLight ? 0.34 : 0.46),
                              Darken(Accent, IsLight ? 0.28 : 0.34));
            WarnSegFill = V(Lighten(Warn, IsLight ? 0.32 : 0.46),
                            Darken(Warn, IsLight ? 0.26 : 0.32));
            CritSegFill = V(Lighten(Crit, IsLight ? 0.32 : 0.46),
                            Darken(Crit, IsLight ? 0.26 : 0.32));
            AccentSegGlow = S(IsLight ? (byte)0x14 : (byte)0x22, Accent);
            WarnSegGlow = S(IsLight ? (byte)0x12 : (byte)0x20, Warn);
            CritSegGlow = S(IsLight ? (byte)0x12 : (byte)0x20, Crit);
            SparkArea = V(Color.FromArgb(IsLight ? (byte)0x1A : (byte)0x26, Accent.R, Accent.G, Accent.B), Colors.Transparent);

            // 吸附光晕：贴着屏幕边的那一侧最亮，向卡片内部快速衰减
            Color beamCore = IsLight ? Lighten(Accent, 0.28) : AccentSoft;
            DockBeamLeft = MakeDockBeam(beamCore, Accent, new Point(0, 0.5), new Point(1, 0.5));
            DockBeamRight = MakeDockBeam(beamCore, Accent, new Point(1, 0.5), new Point(0, 0.5));
            DockBeamTop = MakeDockBeam(beamCore, Accent, new Point(0.5, 0), new Point(0.5, 1));
            DockBeamBottom = MakeDockBeam(beamCore, Accent, new Point(0.5, 1), new Point(0.5, 0));

            GridTile = MakeGrid(IsLight ? (byte)0x0D : (byte)0x0F, Accent);
            ScanTile = MakeScan(IsLight ? (byte)0x05 : (byte)0x08, IsLight ? TextBright : Colors.White);
            Color glowA = spec.GlowA.A == 0 ? Accent : spec.GlowA;
            Color glowB = spec.GlowB.A == 0 ? Alt : spec.GlowB;
            AccentCorner = MakeCorner(glowA, 0.02, 0.01, IsLight ? (byte)0x0C : (byte)0x16);
            AltCorner = MakeCorner(glowB, 0.98, 0.99, IsLight ? (byte)0x08 : (byte)0x0D);

            SparkGridPen = Dash(S(IsLight ? (byte)0x16 : (byte)0x1A, Accent), 0.65);
            SparkDownGlow = P(S(IsLight ? (byte)0x20 : (byte)0x26, Accent), 2.8);
            SparkDownPen = PRound(S(0xFF, AccentSoft), 1.15);
            SparkUpGlow = P(S(IsLight ? (byte)0x1A : (byte)0x20, Alt), 2.6);
            SparkUpPen = PRound(S(0xF0, AltSoft), 1.0);
            SparkDownSoftPen = PRound(S(0xFF, Accent), 1.3);
            SparkUpSoftPen = PRound(S(0xF0, Alt), 1.15);
            SparkCursorPen = P(S(IsLight ? (byte)0x1A : (byte)0x20, Accent), 0.9);
            SegTipPen = P(S(IsLight ? (byte)0x48 : (byte)0x32, Colors.White), 0.8);
            DownHeadAura = S(IsLight ? (byte)0x24 : (byte)0x28, AccentSoft);
            UpHeadAura = S(IsLight ? (byte)0x24 : (byte)0x28, AltSoft);
        }

        private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

        private static SolidColorBrush S(byte a, Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static Pen P(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness);
            p.Freeze();
            return p;
        }

        private static Pen PRound(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            p.Freeze();
            return p;
        }

        private static Pen Dash(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 1.4, 3.2 }, 0) };
            p.Freeze();
            return p;
        }

        private static LinearGradientBrush V(Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush H(params GradientStop[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var stop in stops) b.GradientStops.Add(stop);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush MakeDockBeam(Color core, Color accent, Point start, Point end)
        {
            var b = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xE8, core.R, core.G, core.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xA8, accent.R, accent.G, accent.B), 0.12));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 0.50));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush MakeBorder(Color a, Color b, byte alpha)
        {
            var brush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(alpha, a.R, a.G, a.B), 0),
                    new GradientStop(Color.FromArgb(0x70, b.R, b.G, b.B), 0.45),
                    new GradientStop(Color.FromArgb(alpha, b.R, b.G, b.B), 1)
                },
                new Point(0, 0), new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        private static DrawingBrush MakeGrid(byte alpha, Color color)
        {
            var pen = P(S(alpha, color), 0.5);
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawLine(pen, new Point(0, 0.25), new Point(10, 0.25));
                dc.DrawLine(pen, new Point(0.25, 0), new Point(0.25, 10));
            }

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 10, 10),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 10, 10),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            brush.Freeze();
            return brush;
        }

        private static DrawingBrush MakeScan(byte alpha, Color color)
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(S(alpha, color), null, new Rect(0, 0, 1, 1));
            }

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 1, 3),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 3),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            brush.Freeze();
            return brush;
        }

        private static RadialGradientBrush MakeCorner(Color c, double cx, double cy, byte alpha)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = 0.95,
                RadiusY = 0.8
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha / 4), c.R, c.G, c.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            brush.Freeze();
            return brush;
        }

        private static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static Color Lighten(Color c, double t) => Mix(c, Colors.White, t);
        private static Color Darken(Color c, double t) => Mix(c, Colors.Black, t);
    }

    internal sealed class HudTheme
    {
        public readonly string Id;
        public readonly string Name;
        public readonly HudPalette Dark;
        public readonly HudPalette Light;

        public HudTheme(string id, string name, HudThemeSpec dark, HudThemeSpec light)
        {
            Id = id;
            Name = name;
            Dark = new HudPalette(dark);
            Light = new HudPalette(light);
        }
    }

    /// <summary>主题注册表 + 当前主题。切换主题只改 Current，不碰每个控件。</summary>
    internal static class Hud
    {
        private static readonly HudTheme[] ThemeList = BuildThemes();

        public static IReadOnlyList<HudTheme> Themes => ThemeList;

        public static HudTheme CurrentTheme { get; private set; } = ThemeList[0];
        public static HudPalette Current { get; private set; } = ThemeList[0].Dark;
        public static bool IsLight { get; private set; }
        public static bool Ambient => Current.Ambient;
        public static string MemoryLabel => Current.MemoryLabel;
        public static string NetworkLabel => Current.NetworkLabel;
        public static string HistoryLabel => Current.HistoryLabel;
        public static string LiveLabel => Current.LiveLabel;
        public static string CurrentThemeId => CurrentTheme.Id;

        public static void UseTheme(string id, bool light)
        {
            HudTheme theme = ThemeList[0];
            for (int i = 0; i < ThemeList.Length; i++)
            {
                if (string.Equals(ThemeList[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    theme = ThemeList[i];
                    break;
                }
            }

            CurrentTheme = theme;
            IsLight = light;
            Current = light ? theme.Light : theme.Dark;
        }

        public static SolidColorBrush AccentBrush => Current.AccentBrush;
        public static SolidColorBrush AccentSoftBrush => Current.AccentSoftBrush;
        public static SolidColorBrush AltBrush => Current.AltBrush;
        public static SolidColorBrush AltSoftBrush => Current.AltSoftBrush;
        public static SolidColorBrush WarnBrush => Current.WarnBrush;
        public static SolidColorBrush CritBrush => Current.CritBrush;
        public static SolidColorBrush TextBrightBrush => Current.TextBrightBrush;
        public static SolidColorBrush TextMidBrush => Current.TextMidBrush;
        public static SolidColorBrush TextDimBrush => Current.TextDimBrush;
        public static SolidColorBrush TrackBrush => Current.TrackBrush;
        public static SolidColorBrush WhiteHotBrush => Current.WhiteHotBrush;
        public static SolidColorBrush DotAuraBrush => Current.DotAuraBrush;
        public static SolidColorBrush ReadTagBrush => Current.ReadTagBrush;
        public static SolidColorBrush WriteTagBrush => Current.WriteTagBrush;
        public static SolidColorBrush NotchBrush => Current.NotchBrush;
        public static SolidColorBrush MenuBg => Current.MenuBg;
        public static SolidColorBrush MenuHover => Current.MenuHover;
        public static SolidColorBrush MenuActive => Current.MenuActive;
        public static SolidColorBrush MenuBorder => Current.MenuBorder;
        public static SolidColorBrush MenuDivider => Current.MenuDivider;

        public static LinearGradientBrush Background => Current.Background;
        public static LinearGradientBrush GlassSheen => Current.GlassSheen;
        public static LinearGradientBrush BottomShade => Current.BottomShade;
        public static LinearGradientBrush DividerBrush => Current.DividerBrush;
        public static LinearGradientBrush PulseBrush => Current.PulseBrush;
        public static LinearGradientBrush BorderBrush => Current.BorderBrush;
        public static LinearGradientBrush SparkArea => Current.SparkArea;

        public static LinearGradientBrush DockBeamLeft => Current.DockBeamLeft;
        public static LinearGradientBrush DockBeamRight => Current.DockBeamRight;
        public static LinearGradientBrush DockBeamTop => Current.DockBeamTop;
        public static LinearGradientBrush DockBeamBottom => Current.DockBeamBottom;

        public static DrawingBrush GridTile => Current.GridTile;
        public static DrawingBrush ScanTile => Current.ScanTile;
        public static RadialGradientBrush AccentCorner => Current.AccentCorner;
        public static RadialGradientBrush AltCorner => Current.AltCorner;

        public static Pen BorderPen => Current.BorderPen;
        public static Pen BorderHoverPen => Current.BorderHoverPen;
        public static Pen BorderGlowAccent => Current.BorderGlowAccent;
        public static Pen BorderGlowAlt => Current.BorderGlowAlt;
        public static Pen CardShadowWide => Current.CardShadowWide;
        public static Pen CardShadow => Current.CardShadow;
        public static Pen InnerRimPen => Current.InnerRimPen;
        public static Pen TopLightPen => Current.TopLightPen;
        public static Pen TrackShadePen => Current.TrackShadePen;
        public static Pen DockGlow => Current.DockGlow;
        public static Pen DockPen => Current.DockPen;
        public static Pen DockRailPen => Current.DockRailPen;
        public static Pen RailMajor => Current.RailMajor;
        public static Pen RailMinor => Current.RailMinor;
        public static Pen BracketAccent => Current.BracketAccent;
        public static Pen BracketAlt => Current.BracketAlt;
        public static Pen InnerEdgePen => Current.InnerEdgePen;
        public static Pen SparkGridPen => Current.SparkGridPen;
        public static Pen SparkDownGlow => Current.SparkDownGlow;
        public static Pen SparkDownPen => Current.SparkDownPen;
        public static Pen SparkUpGlow => Current.SparkUpGlow;
        public static Pen SparkUpPen => Current.SparkUpPen;
        public static Pen SparkDownSoftPen => Current.SparkDownSoftPen;
        public static Pen SparkUpSoftPen => Current.SparkUpSoftPen;
        public static Pen SparkCursorPen => Current.SparkCursorPen;
        public static Pen SegTipPen => Current.SegTipPen;
        public static SolidColorBrush DownHeadAura => Current.DownHeadAura;
        public static SolidColorBrush UpHeadAura => Current.UpHeadAura;

        public static Color LevelColor(double percent)
        {
            return percent >= 96 ? Current.Crit : percent >= 90 ? Current.Warn : Current.Accent;
        }

        public static SolidColorBrush LevelBrush(double percent)
        {
            return percent >= 96 ? Current.CritBrush : percent >= 90 ? Current.WarnBrush : Current.AccentBrush;
        }

        public static Brush LevelFill(double percent)
        {
            return percent >= 96 ? Current.CritSegFill : percent >= 90 ? Current.WarnSegFill : Current.AccentSegFill;
        }

        public static Brush LevelGlow(double percent)
        {
            return percent >= 96 ? Current.CritSegGlow : percent >= 90 ? Current.WarnSegGlow : Current.AccentSegGlow;
        }

        // 预览底图用的小工具，与主题无关
        public static SolidColorBrush Solid(byte a, Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public static Pen Pen(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness);
            p.Freeze();
            return p;
        }

        private static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

        private static HudTheme[] BuildThemes()
        {
            return new[]
            {
                // 1. 深海 HUD：冷银蓝仪器，切角外框 + 极简刻度感，不再铺网格和扫描线
                new HudTheme("deepsea", "深海 HUD",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        CornerStyle = HudCornerStyle.Chamfer, CornerRadius = 10,
                        ShowRails = false, ShowNotch = true,
                        Accent = C(0xC6, 0xDE, 0xE9), AccentSoft = C(0xE8, 0xF4, 0xF9),
                        Alt = C(0xAF, 0xC8, 0xB8), AltSoft = C(0xD8, 0xE7, 0xDC),
                        Warn = C(0xD5, 0xB8, 0x8C), Crit = C(0xDD, 0x97, 0x9C),
                        TextBright = C(0xEC, 0xF3, 0xF7), TextMid = C(0xB5, 0xC6, 0xD0), TextDim = C(0x85, 0x99, 0xA5),
                        BgA = C(0x0C, 0x17, 0x20), BgB = C(0x06, 0x0A, 0x10),
                        GlowA = C(0x7E, 0xAF, 0xC4), GlowB = C(0x6C, 0x8F, 0x7A),
                        BorderA = C(0xDC, 0xEB, 0xF3), BorderB = C(0x45, 0x65, 0x75),
                        Track = C(0x12, 0x23, 0x2C), BgAlpha = 0xF2
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        CornerStyle = HudCornerStyle.Chamfer, CornerRadius = 10,
                        ShowRails = false, ShowNotch = true,
                        Accent = C(0x2F, 0x63, 0x77), AccentSoft = C(0x47, 0x7C, 0x90),
                        Alt = C(0x49, 0x6C, 0x58), AltSoft = C(0x6D, 0x90, 0x7A),
                        Warn = C(0x87, 0x5F, 0x27), Crit = C(0xA2, 0x4C, 0x51),
                        TextBright = C(0x10, 0x26, 0x2F), TextMid = C(0x31, 0x51, 0x5F), TextDim = C(0x4E, 0x68, 0x74),
                        BgA = C(0xF4, 0xF8, 0xFA), BgB = C(0xE5, 0xEE, 0xF2),
                        GlowA = C(0x7F, 0xB0, 0xC2), GlowB = C(0x8F, 0xAE, 0x99),
                        BorderA = C(0x3E, 0x66, 0x78), BorderB = C(0x7A, 0x96, 0xA3),
                        Track = C(0xD2, 0xE0, 0xE6), BgAlpha = 0xF7
                    }),

                // 2. 暮光山脊：雾蓝 + 灰玫瑰，低饱和的暮色玻璃
                new HudTheme("alpine", "暮光山脊",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xBD, 0xD2, 0xE7), AccentSoft = C(0xE2, 0xED, 0xF8),
                        Alt = C(0xD9, 0x93, 0x97), AltSoft = C(0xEE, 0xC2, 0xC4),
                        Warn = C(0xD2, 0xA7, 0x8F), Crit = C(0xDE, 0x88, 0x8D),
                        TextBright = C(0xEF, 0xF3, 0xF9), TextMid = C(0xBD, 0xC7, 0xD5), TextDim = C(0x8C, 0x9A, 0xAA),
                        BgA = C(0x1B, 0x25, 0x3A), BgB = C(0x0C, 0x10, 0x1C),
                        BgC = C(0x33, 0x22, 0x2E), BgMidStop = 0.46,
                        GlowA = C(0xE0, 0x8C, 0x92), GlowB = C(0x7C, 0xA6, 0xD8),
                        BorderA = C(0xD6, 0xE2, 0xEE), BorderB = C(0x8A, 0x6A, 0x7C),
                        Track = C(0x1B, 0x27, 0x42), BgAlpha = 0xF2
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x3A, 0x62, 0x87), AccentSoft = C(0x5A, 0x82, 0xA5),
                        Alt = C(0x99, 0x4C, 0x57), AltSoft = C(0xBE, 0x76, 0x7E),
                        Warn = C(0x96, 0x5C, 0x4D), Crit = C(0xA9, 0x4C, 0x57),
                        TextBright = C(0x17, 0x26, 0x38), TextMid = C(0x44, 0x5A, 0x6E), TextDim = C(0x57, 0x6A, 0x7C),
                        BgA = C(0xE7, 0xEE, 0xFB), BgB = C(0xD8, 0xE4, 0xF6),
                        BgC = C(0xF6, 0xDF, 0xE7), BgMidStop = 0.46,
                        GlowA = C(0xBE, 0x6C, 0x74), GlowB = C(0x6C, 0x96, 0xCE),
                        BorderA = C(0x52, 0x6F, 0x89), BorderB = C(0xA5, 0x6E, 0x7A),
                        Track = C(0xD0, 0xDC, 0xEE), BgAlpha = 0xF8
                    }),

                // 3. 霞光山脉：暖砂 + 深蓝，收敛成一杯落日下的深色玻璃
                new HudTheme("sunset", "霞光山脉",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xA6, 0xC0, 0xDF), AccentSoft = C(0xD5, 0xE4, 0xF5),
                        Alt = C(0xE8, 0x92, 0x68), AltSoft = C(0xF3, 0xC0, 0xA4),
                        Warn = C(0xD2, 0x9C, 0x66), Crit = C(0xD7, 0x7B, 0x7C),
                        TextBright = C(0xF3, 0xF0, 0xEC), TextMid = C(0xC7, 0xBC, 0xB7), TextDim = C(0x9D, 0x8D, 0x88),
                        BgA = C(0x32, 0x1E, 0x18), BgB = C(0x0E, 0x16, 0x24),
                        BgC = C(0x34, 0x1F, 0x1E), BgMidStop = 0.52,
                        GlowA = C(0xE8, 0x90, 0x60), GlowB = C(0x6E, 0x92, 0xCE),
                        BorderA = C(0xD9, 0xC5, 0xB6), BorderB = C(0xB2, 0x74, 0x5A),
                        Track = C(0x26, 0x1C, 0x20), BgAlpha = 0xF2
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x3E, 0x62, 0x89), AccentSoft = C(0x5D, 0x7E, 0xA3),
                        Alt = C(0x9C, 0x4E, 0x2C), AltSoft = C(0xCC, 0x7C, 0x58),
                        Warn = C(0x8D, 0x5C, 0x2A), Crit = C(0xA9, 0x4D, 0x50),
                        TextBright = C(0x2B, 0x23, 0x20), TextMid = C(0x60, 0x4F, 0x49), TextDim = C(0x75, 0x64, 0x5D),
                        BgA = C(0xFB, 0xEC, 0xD6), BgB = C(0xE2, 0xEA, 0xF7),
                        BgC = C(0xF9, 0xDC, 0xD2), BgMidStop = 0.52,
                        GlowA = C(0xC4, 0x6E, 0x44), GlowB = C(0x74, 0x98, 0xC6),
                        BorderA = C(0xA5, 0x7A, 0x61), BorderB = C(0xB0, 0x6E, 0x50),
                        Track = C(0xE4, 0xD0, 0xBA), BgAlpha = 0xF8
                    }),

                // 4. 虹彩都市：雾青 + 藕粉，全息感只留在细边和双色数据里
                new HudTheme("holo", "虹彩都市",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x89, 0xD4, 0xCF), AccentSoft = C(0xCF, 0xF2, 0xEE),
                        Alt = C(0xDB, 0x8E, 0xC0), AltSoft = C(0xF0, 0xC2, 0xE3),
                        Warn = C(0xD4, 0xBC, 0x72), Crit = C(0xE2, 0x87, 0x8E),
                        TextBright = C(0xF5, 0xF0, 0xFA), TextMid = C(0xC7, 0xBE, 0xD8), TextDim = C(0x99, 0x8F, 0xAA),
                        BgA = C(0x26, 0x18, 0x38), BgB = C(0x0E, 0x0E, 0x1C),
                        BgC = C(0x18, 0x2C, 0x3C), BgMidStop = 0.40,
                        GlowA = C(0xDE, 0x8A, 0xC0), GlowB = C(0x54, 0xD2, 0xC8),
                        BorderA = C(0xC3, 0xE7, 0xE2), BorderB = C(0xB4, 0x74, 0x9E),
                        Track = C(0x1D, 0x1A, 0x2E), BgAlpha = 0xF2
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x26, 0x71, 0x6E), AccentSoft = C(0x43, 0x8E, 0x8A),
                        Alt = C(0x96, 0x44, 0x74), AltSoft = C(0xB6, 0x6A, 0x90),
                        Warn = C(0x88, 0x65, 0x1F), Crit = C(0xA8, 0x49, 0x53),
                        TextBright = C(0x24, 0x1D, 0x33), TextMid = C(0x4D, 0x42, 0x63), TextDim = C(0x6D, 0x62, 0x7F),
                        BgA = C(0xF2, 0xEB, 0xFC), BgB = C(0xEB, 0xE2, 0xF9),
                        BgC = C(0xDE, 0xF4, 0xEE), BgMidStop = 0.40,
                        GlowA = C(0xB2, 0x5C, 0x8C), GlowB = C(0x44, 0xA2, 0x9C),
                        BorderA = C(0x4C, 0x8A, 0x85), BorderB = C(0xA5, 0x66, 0x8E),
                        Track = C(0xD8, 0xC8, 0xEE), BgAlpha = 0xF8
                    }),

                // 5. 黑白缎影：暖灰、骨白和一点酒红，像一张压光的黑白照片
                new HudTheme("noir", "黑白缎影",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xE2, 0xDF, 0xD8), AccentSoft = C(0xF6, 0xF3, 0xEE),
                        Alt = C(0xBE, 0x66, 0x70), AltSoft = C(0xDA, 0x9C, 0xA2),
                        Warn = C(0xC5, 0xB3, 0x87), Crit = C(0xD7, 0x6A, 0x71),
                        TextBright = C(0xF3, 0xF1, 0xEC), TextMid = C(0xC1, 0xBD, 0xB7), TextDim = C(0x92, 0x8E, 0x89),
                        BgA = C(0x19, 0x14, 0x15), BgB = C(0x0A, 0x0A, 0x0C),
                        BgC = C(0x1D, 0x16, 0x18), BgMidStop = 0.46,
                        GlowA = C(0xC4, 0x5E, 0x68), GlowB = C(0xD8, 0xD0, 0xC4),
                        BorderA = C(0xED, 0xEA, 0xE4), BorderB = C(0x64, 0x54, 0x58),
                        Track = C(0x1D, 0x1C, 0x20), BgAlpha = 0xF4
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x2B, 0x2A, 0x2F), AccentSoft = C(0x4B, 0x49, 0x4F),
                        Alt = C(0x94, 0x3A, 0x44), AltSoft = C(0xB4, 0x60, 0x68),
                        Warn = C(0x7E, 0x61, 0x2B), Crit = C(0x9F, 0x30, 0x3A),
                        TextBright = C(0x15, 0x14, 0x16), TextMid = C(0x44, 0x42, 0x48), TextDim = C(0x65, 0x61, 0x65),
                        BgA = C(0xFA, 0xF7, 0xF6), BgB = C(0xE7, 0xE2, 0xE1),
                        BgC = C(0xF3, 0xEB, 0xEA), BgMidStop = 0.46,
                        GlowA = C(0xAA, 0x4E, 0x58), GlowB = C(0x7A, 0x77, 0x70),
                        BorderA = C(0x4A, 0x46, 0x49), BorderB = C(0xA0, 0x9A, 0x95),
                        Track = C(0xDD, 0xDB, 0xD6), BgAlpha = 0xF8
                    }),

                // 6. 玄鳞：墨绿、矿石灰和一点旧金，像一块磨砂的深色石面
                new HudTheme("scale", "玄鳞",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xA6, 0xC1, 0xAE), AccentSoft = C(0xD6, 0xE5, 0xDA),
                        Alt = C(0xC2, 0xAD, 0x6E), AltSoft = C(0xE2, 0xD0, 0x9C),
                        Warn = C(0xC9, 0xAC, 0x69), Crit = C(0xD1, 0x5A, 0x5B),
                        TextBright = C(0xED, 0xF2, 0xEC), TextMid = C(0xB6, 0xC1, 0xB8), TextDim = C(0x8A, 0x99, 0x8E),
                        BgA = C(0x16, 0x22, 0x1A), BgB = C(0x08, 0x0C, 0x09),
                        BgC = C(0x16, 0x24, 0x18), BgMidStop = 0.46,
                        GlowA = C(0xC6, 0xAE, 0x62), GlowB = C(0x52, 0xA0, 0x88),
                        BorderA = C(0xC2, 0xD0, 0xC5), BorderB = C(0x4E, 0x62, 0x52),
                        Track = C(0x14, 0x1E, 0x17), BgAlpha = 0xF3
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0x3B, 0x54, 0x46), AccentSoft = C(0x59, 0x74, 0x60),
                        Alt = C(0x76, 0x66, 0x36), AltSoft = C(0x92, 0x82, 0x50),
                        Warn = C(0x80, 0x60, 0x29), Crit = C(0x9D, 0x31, 0x38),
                        TextBright = C(0x13, 0x1A, 0x15), TextMid = C(0x3C, 0x48, 0x40), TextDim = C(0x5F, 0x6B, 0x63),
                        BgA = C(0xEC, 0xF3, 0xEA), BgB = C(0xE0, 0xEC, 0xE2),
                        BgC = C(0xE0, 0xF2, 0xE2), BgMidStop = 0.46,
                        GlowA = C(0x9C, 0x86, 0x38), GlowB = C(0x54, 0x92, 0x7C),
                        BorderA = C(0x4B, 0x5C, 0x51), BorderB = C(0x80, 0x8E, 0x80),
                        Track = C(0xC8, 0xDA, 0xCC), BgAlpha = 0xF8
                    }),

                // 7. 绯绒客厅：lofi 暖调——奶油黄墙面、绯红沙发和一点墨青发色
                //    白天是米黄奶油玻璃，夜里是暖褐暗房的柔光；主色绯红、次色墨青
                new HudTheme("lofi", "绯绒客厅",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xEE, 0xB2, 0xA4), AccentSoft = C(0xFA, 0xD6, 0xC9),
                        Alt = C(0xB9, 0xC4, 0xD6), AltSoft = C(0xD9, 0xDF, 0xE9),
                        Warn = C(0xD6, 0xB0, 0x76), Crit = C(0xE0, 0x8A, 0x8C),
                        TextBright = C(0xF8, 0xF1, 0xE9), TextMid = C(0xD3, 0xC4, 0xBC), TextDim = C(0x9E, 0x8D, 0x89),
                        BgA = C(0x2C, 0x1E, 0x16), BgB = C(0x12, 0x0C, 0x10),
                        BgC = C(0x34, 0x24, 0x18), BgMidStop = 0.46,
                        GlowA = C(0xE8, 0xB2, 0x6C), GlowB = C(0x8E, 0x9A, 0xB2),
                        BorderA = C(0xF2, 0xDE, 0xC2), BorderB = C(0x8E, 0x66, 0x66),
                        Track = C(0x2A, 0x1D, 0x1F), BgAlpha = 0xF0
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xB0, 0x4A, 0x44), AccentSoft = C(0xC8, 0x6C, 0x60),
                        Alt = C(0x3B, 0x44, 0x5C), AltSoft = C(0x5C, 0x66, 0x7C),
                        Warn = C(0x8D, 0x5F, 0x27), Crit = C(0xA9, 0x47, 0x4D),
                        TextBright = C(0x2B, 0x1F, 0x21), TextMid = C(0x5E, 0x50, 0x50), TextDim = C(0x78, 0x68, 0x66),
                        BgA = C(0xFC, 0xEE, 0xC6), BgB = C(0xEE, 0xDE, 0xA4),
                        BgC = C(0xF8, 0xE8, 0xB2), BgMidStop = 0.46,
                        GlowA = C(0xC8, 0x6C, 0x60), GlowB = C(0x5C, 0x66, 0x7C),
                        BorderA = C(0xAB, 0x5F, 0x56), BorderB = C(0x9C, 0x8C, 0x7C),
                        Track = C(0xE7, 0xD6, 0xA8), BgAlpha = 0xF8
                    }),

                // 8. 赤焰峡谷：橙红岩壁 + 藏蓝天空的高饱和互补色，白车点睛
                //    白天是灼热的橙砂玻璃，夜里是暮色峡谷的余烬光；主色焰橙、次色暮蓝
                new HudTheme("canyon", "赤焰峡谷",
                    new HudThemeSpec
                    {
                        IsLight = false,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xF5, 0xA5, 0x5E), AccentSoft = C(0xFB, 0xD0, 0xA4),
                        Alt = C(0x8F, 0xA8, 0xD8), AltSoft = C(0xC2, 0xD2, 0xEE),
                        Warn = C(0xE8, 0xB4, 0x60), Crit = C(0xE8, 0x64, 0x55),
                        TextBright = C(0xFA, 0xF3, 0xEA), TextMid = C(0xD8, 0xC2, 0xB2), TextDim = C(0xA6, 0x8C, 0x7C),
                        BgA = C(0x2E, 0x14, 0x0A), BgB = C(0x12, 0x07, 0x08),
                        BgC = C(0x3C, 0x1A, 0x0C), BgMidStop = 0.46,
                        GlowA = C(0xE8, 0x72, 0x2E), GlowB = C(0x3E, 0x5C, 0x9A),
                        BorderA = C(0xE8, 0x7A, 0x38), BorderB = C(0x8A, 0x3E, 0x22),
                        Track = C(0x30, 0x16, 0x0C), BgAlpha = 0xF3
                    },
                    new HudThemeSpec
                    {
                        IsLight = true,
                        Ambient = true,
                        CornerStyle = HudCornerStyle.Round, CornerRadius = 18,
                        ShowRails = false, ShowNotch = false,
                        Accent = C(0xB8, 0x4A, 0x14), AccentSoft = C(0xD0, 0x64, 0x28),
                        Alt = C(0x2E, 0x44, 0x70), AltSoft = C(0x50, 0x68, 0x98),
                        Warn = C(0x9C, 0x5E, 0x14), Crit = C(0xB0, 0x3A, 0x2E),
                        TextBright = C(0x2B, 0x16, 0x08), TextMid = C(0x6E, 0x4A, 0x2E), TextDim = C(0x86, 0x60, 0x4A),
                        BgA = C(0xFA, 0xDC, 0xC0), BgB = C(0xF2, 0xC4, 0x94),
                        BgC = C(0xF8, 0xCE, 0x9E), BgMidStop = 0.46,
                        GlowA = C(0xE8, 0x72, 0x2E), GlowB = C(0x2E, 0x44, 0x70),
                        BorderA = C(0xC4, 0x5A, 0x1E), BorderB = C(0x8A, 0x5A, 0x34),
                        Track = C(0xF2, 0xD4, 0xAC), BgAlpha = 0xF8
                    })
            };
        }
    }

    /// <summary>
    /// 卡片的背景与边框：切角八边形（深海 HUD）或平滑圆角（其余主题）。
    /// 只画静态内容，刷新数据时不会触发重绘。
    /// </summary>
    internal sealed class HudSurface : FrameworkElement
    {
        private bool _dockHighlight;
        private bool _hovered;

        public HudSurface()
        {
            IsHitTestVisible = false;
        }

        public bool DockHighlight
        {
            get => _dockHighlight;
            set
            {
                if (_dockHighlight == value) return;
                _dockHighlight = value;
                InvalidateVisual();
            }
        }

        public bool Hovered
        {
            get => _hovered;
            set
            {
                if (_hovered == value) return;
                _hovered = value;
                InvalidateVisual();
            }
        }

        /// <summary>外框几何：切角八边形或圆角矩形，吸附层复用同一份，保证两层完全重合。</summary>
        internal static Geometry BuildFrameGeometry(HudPalette p, double w, double h)
        {
            double cut = Math.Min(p.CornerRadius, Math.Min(w, h) / 3);
            double radius = Math.Min(p.CornerRadius, Math.Min(w, h) / 2 - 1);
            Geometry geo = p.CornerStyle == HudCornerStyle.Round
                ? (Geometry)new RectangleGeometry(new Rect(0.5, 0.5, w - 1, h - 1), radius, radius)
                : BuildOctagon(w, h, cut, 0.5);
            if (geo.CanFreeze) geo.Freeze();
            return geo;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 12 || h < 12) return;

            HudPalette p = Hud.Current;
            bool round = p.CornerStyle == HudCornerStyle.Round;
            double cut = Math.Min(p.CornerRadius, Math.Min(w, h) / 3);
            double radius = Math.Min(p.CornerRadius, Math.Min(w, h) / 2 - 1);
            Geometry frame = BuildFrameGeometry(p, w, h);

            // 极淡的外阴影，只占卡片外 5 DIP 的透明边距；玻璃卡片和壁纸之间会有一层空气感。
            dc.DrawGeometry(null, Hud.CardShadowWide, frame);
            dc.DrawGeometry(null, Hud.CardShadow, frame);

            dc.PushClip(frame);
            dc.DrawRectangle(Hud.Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Hud.GlassSheen, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Hud.BottomShade, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Hud.AccentCorner, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Hud.AltCorner, null, new Rect(0, 0, w, h));

            // 顶部一道很细的受光，代替上一版的网格和扫描线。
            double topInset = round ? radius * 0.55 : cut + 4;
            if (w - topInset * 2 > 12)
            {
                dc.DrawLine(Hud.TopLightPen,
                    new Point(topInset, 1.15), new Point(w - topInset, 1.15));
            }
            dc.Pop();

            if (_dockHighlight)
            {
                dc.DrawGeometry(null, Hud.DockGlow, frame);
                dc.DrawGeometry(null, Hud.DockPen, frame);
            }
            else
            {
                dc.DrawGeometry(null, Hud.BorderGlowAlt, frame);
                dc.DrawGeometry(null, Hud.BorderGlowAccent, frame);
                dc.DrawGeometry(null, _hovered ? Hud.BorderHoverPen : Hud.BorderPen, frame);
            }

            // 内圈高光让边界更像一块有厚度的玻璃，而不是描边贴纸。
            if (round)
            {
                double r = Math.Max(2, radius - 1.1);
                dc.DrawRoundedRectangle(null, Hud.InnerRimPen,
                    new Rect(1.1, 1.1, w - 2.2, h - 2.2), r, r);
            }
            else
            {
                Geometry inner = BuildOctagon(w, h, Math.Max(2, cut - 0.8), 1.35);
                if (inner.CanFreeze) inner.Freeze();
                dc.DrawGeometry(null, Hud.InnerRimPen, inner);
                DrawBrackets(dc, w, h, cut);
                if (p.ShowNotch) DrawNotch(dc, w);
            }
        }

        private static void DrawBrackets(DrawingContext dc, double w, double h, double cut)
        {
            const double arm = 11;
            const double inset = 5.4;

            dc.DrawLine(Hud.BracketAccent,
                new Point(cut + 3.2, inset), new Point(cut + 3.2 + arm, inset));
            dc.DrawLine(Hud.BracketAccent,
                new Point(inset, cut + 3.2), new Point(inset, cut + 3.2 + arm));

            dc.DrawLine(Hud.BracketAlt,
                new Point(w - cut - 3.2 - arm, h - inset), new Point(w - cut - 3.2, h - inset));
            dc.DrawLine(Hud.BracketAlt,
                new Point(w - inset, h - cut - 3.2 - arm), new Point(w - inset, h - cut - 3.2));
        }

        private static void DrawNotch(DrawingContext dc, double w)
        {
            var notch = new StreamGeometry();
            using (var ctx = notch.Open())
            {
                ctx.BeginFigure(new Point(w / 2 - 3.6, 0.5), true, true);
                ctx.LineTo(new Point(w / 2 + 3.6, 0.5), true, false);
                ctx.LineTo(new Point(w / 2, 3.2), true, false);
            }
            notch.Freeze();
            dc.DrawGeometry(Hud.NotchBrush, null, notch);
        }

        private static StreamGeometry BuildOctagon(double w, double h, double cut, double inset)
        {
            double l = inset;
            double t = inset;
            double r = w - inset;
            double b = h - inset;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(l + cut, t), true, true);
                ctx.LineTo(new Point(r - cut, t), true, false);
                ctx.LineTo(new Point(r, t + cut), true, false);
                ctx.LineTo(new Point(r, b - cut), true, false);
                ctx.LineTo(new Point(r - cut, b), true, false);
                ctx.LineTo(new Point(l + cut, b), true, false);
                ctx.LineTo(new Point(l, b - cut), true, false);
                ctx.LineTo(new Point(l, t + cut), true, false);
            }
            return geo;
        }
    }

    /// <summary>
    /// 吸附反馈层：拖动靠近屏幕边时，从那条边向卡片内部铺一层主题色光晕并把外框点亮；
    /// 松手吸附后做一次快速衰减的闪光，最后留一条很淡的“已贴边”亮线。
    /// 它只重绘自己这一层（十几条线段/一个渐变矩形），不会触发卡片背景和数据区重绘。
    /// </summary>
    internal sealed class DockGuide : FrameworkElement
    {
        private const double BeamDepth = 17;

        private HudDockSide _side;
        private double _level;

        private Geometry _frame;
        private HudPalette _framePalette;
        private double _frameW = -1;
        private double _frameH = -1;

        public DockGuide()
        {
            IsHitTestVisible = false;
        }

        public HudDockSide Side
        {
            get => _side;
            set
            {
                if (_side == value) return;
                _side = value;
                InvalidateVisual();
            }
        }

        public double Level
        {
            get => _level;
            set
            {
                double v = value;
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) v = 0;
                else if (v > 1) v = 1;

                // 量化到 1/64：拖动时每像素都 Invalidate 没有意义，肉眼也看不出台阶
                v = Math.Round(v * 64) / 64;
                if (Math.Abs(v - _level) < 0.001) return;

                _level = v;
                InvalidateVisual();
            }
        }

        public void Reset()
        {
            Side = HudDockSide.None;
            Level = 0;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_side == HudDockSide.None || _level <= 0.001) return;

            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 24 || h < 24) return;

            Geometry frame = GetFrame(w, h);

            // 越靠近边缘，光晕越宽：远看是一条细线，快贴上时变成一整片边缘辉光
            double beamDepth = BeamDepth * (0.45 + 0.55 * _level);

            // 光晕裁进外框内部，圆角/切角处不会露出一块方形的底；整体透明度跟着接近程度走
            dc.PushOpacity(_level);
            dc.PushClip(frame);
            switch (_side)
            {
                case HudDockSide.Left:
                    dc.DrawRectangle(Hud.DockBeamLeft, null, new Rect(0, 4, beamDepth, h - 8));
                    break;
                case HudDockSide.Right:
                    dc.DrawRectangle(Hud.DockBeamRight, null, new Rect(w - beamDepth, 4, beamDepth, h - 8));
                    break;
                case HudDockSide.Top:
                    dc.DrawRectangle(Hud.DockBeamTop, null, new Rect(4, 0, w - 8, beamDepth));
                    break;
                case HudDockSide.Bottom:
                    dc.DrawRectangle(Hud.DockBeamBottom, null, new Rect(4, h - beamDepth, w - 8, beamDepth));
                    break;
            }
            dc.Pop();
            dc.Pop();

            // 贴着吸附边的一条亮线：近/远、拖出/松手时都靠它给出“正在吸/已经吸住”的反馈
            dc.PushOpacity(_level);
            DrawRail(dc, w, h);
            dc.Pop();

            double frameLevel = _level * _level * 0.72;
            if (frameLevel > 0.01)
            {
                dc.PushOpacity(frameLevel);
                dc.DrawGeometry(null, Hud.DockGlow, frame);
                dc.DrawGeometry(null, Hud.DockPen, frame);
                dc.Pop();
            }
        }

        private void DrawRail(DrawingContext dc, double w, double h)
        {
            const double inset = 3.6;
            const double end = 15;
            double grow = 0.35 + 0.65 * _level;   // 亮线随接近程度从中间向两端长出来

            switch (_side)
            {
                case HudDockSide.Left:
                case HudDockSide.Right:
                {
                    double x = _side == HudDockSide.Left ? inset : w - inset;
                    double half = Math.Max(6, (h / 2 - end) * grow);
                    dc.DrawLine(Hud.DockGlow, new Point(x, h / 2 - half), new Point(x, h / 2 + half));
                    dc.DrawLine(Hud.DockRailPen, new Point(x, h / 2 - half), new Point(x, h / 2 + half));
                    break;
                }
                default:
                {
                    double y = _side == HudDockSide.Top ? inset : h - inset;
                    double half = Math.Max(6, (w / 2 - end) * grow);
                    dc.DrawLine(Hud.DockGlow, new Point(w / 2 - half, y), new Point(w / 2 + half, y));
                    dc.DrawLine(Hud.DockRailPen, new Point(w / 2 - half, y), new Point(w / 2 + half, y));
                    break;
                }
            }
        }

        /// <summary>尺寸/主题不变时复用同一个冻结几何，拖动过程中零分配。</summary>
        private Geometry GetFrame(double w, double h)
        {
            HudPalette p = Hud.Current;
            if (_frame == null
                || !ReferenceEquals(p, _framePalette)
                || Math.Abs(w - _frameW) > 0.01
                || Math.Abs(h - _frameH) > 0.01)
            {
                _frame = HudSurface.BuildFrameGeometry(p, w, h);
                _framePalette = p;
                _frameW = w;
                _frameH = h;
            }
            return _frame;
        }
    }

    /// <summary>分段式电平条。</summary>
    internal sealed class SegBar : FrameworkElement
    {
        private double _value;

        public int SegmentCount { get; set; } = 24;
        public double Gap { get; set; } = 1.8;
        public double Corner { get; set; } = 1.2;

        public SegBar()
        {
            IsHitTestVisible = false;
        }

        public double Value
        {
            get => _value;
            set
            {
                double v = value;
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (Math.Abs(v - _value) < 0.01) return;
                _value = v;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 || h <= 1) return;

            // 氛围主题：一条连续的圆角药丸，用顶部细高光和内阴影做出玻璃厚度。
            if (Hud.Ambient)
            {
                double pill = h / 2;
                dc.DrawRoundedRectangle(Hud.TrackBrush, null, new Rect(0, 0, w, h), pill, pill);
                if (w > 4)
                {
                    dc.DrawLine(Hud.TrackShadePen,
                        new Point(pill, 0.45), new Point(w - pill, 0.45));
                }

                if (_value <= 0) return;

                double fillW = Math.Min(w, Math.Max(h, w * _value / 100.0));
                var fillGeo = new RectangleGeometry(new Rect(0, 0, fillW, h), pill, pill);
                fillGeo.Freeze();
                dc.DrawRoundedRectangle(Hud.LevelFill(_value), null,
                    new Rect(0, 0, fillW, h), pill, pill);

                if (fillW > 4)
                {
                    dc.PushClip(fillGeo);
                    dc.DrawLine(Hud.SegTipPen,
                        new Point(1.2, 0.72), new Point(fillW - 1.2, 0.72));
                    dc.Pop();
                }
                return;
            }

            int n = Math.Max(4, SegmentCount);
            double segW = (w - Gap * (n - 1)) / n;
            if (segW <= 0.2) return;

            int lit = (int)Math.Round(_value / 100.0 * n);
            if (lit < 0) lit = 0;
            if (lit > n) lit = n;

            double radius = Math.Min(Corner, segW / 2);

            for (int i = 0; i < n; i++)
            {
                var rect = new Rect(i * (segW + Gap), 0, segW, h);
                dc.DrawRoundedRectangle(Hud.TrackBrush, null, rect, radius, radius);
            }

            if (lit == 0) return;

            // 整段点亮的区域共用一层很弱的辉光，避免每格都出现光斑。
            double litWidth = lit * (segW + Gap) - Gap;
            dc.DrawRoundedRectangle(Hud.LevelGlow(_value), null,
                new Rect(-0.5, -0.5, litWidth + 1, h + 1), radius + 0.5, radius + 0.5);

            var fill = Hud.LevelFill(_value);
            for (int i = 0; i < lit; i++)
            {
                double x = i * (segW + Gap);
                dc.DrawRoundedRectangle(fill, null, new Rect(x, 0, segW, h), radius, radius);

                if (h >= 3 && segW > 2)
                {
                    dc.DrawLine(Hud.SegTipPen,
                        new Point(x + 0.5, 0.65), new Point(x + segW - 0.5, 0.65));
                }
            }
        }
    }

    /// <summary>64 点网络吞吐示波器。</summary>
    internal sealed class Sparkline : FrameworkElement
    {
        private const int Capacity = 64;

        private readonly double[] _down = new double[Capacity];
        private readonly double[] _up = new double[Capacity];
        private int _head;
        private int _count;
        private double _scale = -1;

        public Sparkline()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        public void Push(double downBps, double upBps)
        {
            if (double.IsNaN(downBps) || downBps < 0) downBps = 0;
            if (double.IsNaN(upBps) || upBps < 0) upBps = 0;

            if (_count < Capacity)
            {
                _down[_count] = downBps;
                _up[_count] = upBps;
                _count++;
            }
            else
            {
                _down[_head] = downBps;
                _up[_head] = upBps;
                _head = (_head + 1) % Capacity;
            }

            double max = 64 * 1024;
            for (int i = 0; i < _count; i++)
            {
                if (_down[i] > max) max = _down[i];
                if (_up[i] > max) max = _up[i];
            }

            _scale = _scale < 0 ? max : Math.Max(max, _scale * 0.94);
            InvalidateVisual();
        }

        public void CopyHistoryFrom(Sparkline source)
        {
            if (source == null || ReferenceEquals(source, this)) return;
            Array.Copy(source._down, _down, Capacity);
            Array.Copy(source._up, _up, Capacity);
            _head = source._head;
            _count = source._count;
            _scale = source._scale;
            InvalidateVisual();
        }

        /// <summary>
        /// 截图/文档预览用：填满 64 点，避免为了等历史曲线把程序跑满一分钟。
        /// 只在设置了 SYSFLOAT_DUMP_SEED 时调用，正常运行不经过这里。
        /// </summary>
        public void SeedPreview()
        {
            for (int i = 0; i < Capacity; i++)
            {
                double phase = i * 0.47;
                double down = 3.4e6
                    + 1.25e6 * Math.Sin(phase)
                    + 0.8e6 * Math.Cos(phase * 2.3 + 0.7);
                double up = 0.42e6
                    + 0.19e6 * Math.Sin(phase * 1.4 + 1.1)
                    + 0.11e6 * Math.Cos(phase * 3.1);

                // 稀疏的突发，让曲线读起来像真实网络流量，而不是正弦波。
                if ((i * 19 + 7) % 31 < 4)
                {
                    down += 3.2e6;
                    up += 0.32e6;
                }
                if (i == 29 || i == 53)
                {
                    down += 5.6e6;
                    up += 0.7e6;
                }

                _down[i] = Math.Max(0, down);
                _up[i] = Math.Max(0, up);
            }

            _head = 0;
            _count = Capacity;

            double max = 64 * 1024;
            for (int i = 0; i < Capacity; i++)
            {
                if (_down[i] > max) max = _down[i];
                if (_up[i] > max) max = _up[i];
            }
            _scale = max;
            InvalidateVisual();
        }

        private double ValueAt(double[] source, int slot)
        {
            int pad = Capacity - _count;
            if (slot < pad) return 0;
            int index = (slot - pad + _head) % Capacity;
            return source[index];
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 8 || h < 8) return;

            double top = 3;
            double bottom = h - 2.5;
            double height = bottom - top;
            bool ambient = Hud.Ambient;

            double scale = _scale <= 0 ? 1 : _scale;
            double usable = Math.Max(1, w - 2);
            double step = usable / (Capacity - 1);

            double Y(double value)
            {
                double t = Math.Min(1, Math.Max(0, value / scale));
                return bottom - Math.Pow(t, 0.62) * height;
            }

            // 一条基线足够定位，上版的网格/扫描光标全部去掉。
            dc.DrawLine(Hud.SparkGridPen,
                new Point(0, bottom), new Point(w, bottom));

            var downArea = new StreamGeometry();
            using (var ctx = downArea.Open())
            {
                ctx.BeginFigure(new Point(0, bottom), true, true);
                for (int i = 0; i < Capacity; i++)
                {
                    ctx.LineTo(new Point(i * step, Y(ValueAt(_down, i))), true, false);
                }
                ctx.LineTo(new Point(usable, bottom), true, false);
            }
            downArea.Freeze();
            dc.DrawGeometry(Hud.SparkArea, null, downArea);

            var downLine = BuildLine(step, ValueAt, _down, Y);
            var upLine = BuildLine(step, ValueAt, _up, Y);

            if (ambient)
            {
                dc.DrawGeometry(null, Hud.SparkDownSoftPen, downLine);
                dc.DrawGeometry(null, Hud.SparkUpSoftPen, upLine);
            }
            else
            {
                dc.DrawGeometry(null, Hud.SparkDownGlow, downLine);
                dc.DrawGeometry(null, Hud.SparkDownPen, downLine);
                dc.DrawGeometry(null, Hud.SparkUpGlow, upLine);
                dc.DrawGeometry(null, Hud.SparkUpPen, upLine);
                DrawHead(dc, usable, Y(ValueAt(_down, Capacity - 1)), Hud.DownHeadAura, Hud.AccentBrush);
                DrawHead(dc, usable, Y(ValueAt(_up, Capacity - 1)), Hud.UpHeadAura, Hud.AltBrush);
            }
        }

        private static StreamGeometry BuildLine(
            double step, Func<double[], int, double> valueAt, double[] source, Func<double, double> y)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, y(valueAt(source, 0))), false, false);
                for (int i = 1; i < Capacity; i++)
                {
                    ctx.LineTo(new Point(i * step, y(valueAt(source, i))), true, false);
                }
            }
            geo.Freeze();
            return geo;
        }

        private static void DrawHead(DrawingContext dc, double x, double y, Brush aura, Brush core)
        {
            dc.DrawEllipse(aura, null, new Point(x, y), 3.0, 3.0);
            dc.DrawEllipse(core, null, new Point(x, y), 1.3, 1.3);
        }
    }
}
