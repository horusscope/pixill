using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixill
{
    static class Theme
    {
        // ---- colours -------------------------------------------------------
        // Set by ApplySystem(). What Windows offers is: a light/dark switch, and one accent colour with three
        // lighter and three darker shades. Everything neutral (background, text, dividers) follows the switch;
        // the rain bars and highlights use the accent shades. Windows has no "gain/loss" or temperature colours,
        // so green/red and the temperature ramp keep their meaning and only change tone between light and dark.

        public static bool Light;
        public static Color Back, Hover, ChartBack, Text, Dim, Grid, Shade, Shine;
        public static Color Up, Down;
        public static Color Accent;
        public static Color RainProb;       // chance of rain, grows from the bottom
        public static Color RainAmount;     // rain amount, grows from the top

        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

        static int RegInt(string key, string name, int def)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v is int ? (int)v : def;
                }
            }
            catch { return def; }
        }

        /// <summary>Windows' accent palette, lightest to darkest: [0..2] light shades, [3] the accent, [4..6] dark shades.</summary>
        static Color[] AccentShades()
        {
            var fallback = Color.FromArgb(0, 120, 212);
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AccentKey))
                {
                    var bytes = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (bytes != null && bytes.Length >= 28)
                    {
                        var shades = new Color[7];
                        for (int i = 0; i < 7; i++) shades[i] = Color.FromArgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2]);
                        return shades;
                    }
                }
                int abgr = RegInt(@"Software\Microsoft\Windows\DWM", "AccentColor", 0);
                if (abgr != 0) fallback = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
            }
            catch { }
            return new[]
            {
                Mix(fallback, Color.White, 0.6), Mix(fallback, Color.White, 0.4), Mix(fallback, Color.White, 0.2), fallback,
                Mix(fallback, Color.Black, 0.2), Mix(fallback, Color.Black, 0.4), Mix(fallback, Color.Black, 0.6),
            };
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static double Luminance(Color c)
        {
            Func<int, double> lin = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }

        static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Nudges a colour away from the background until it is readable against it.</summary>
        static Color Readable(Color c, Color back, double minContrast)
        {
            Color toward = Luminance(back) < 0.5 ? Color.White : Color.Black;
            for (int i = 0; i < 12 && Contrast(c, back) < minContrast; i++) c = Mix(c, toward, 0.15);
            return c;
        }

        /// <summary>Reads the Windows theme and sets every colour. Returns true if anything changed.</summary>
        public static bool ApplySystem(AppConfig cfg)
        {
            string before = Signature();
            string mode = (cfg.theme ?? "system").ToLowerInvariant();
            // A desktop widget is shell furniture like the taskbar, so it follows the "Windows mode" switch.
            Light = mode == "light" || mode != "dark" && RegInt(PersonalizeKey, "SystemUsesLightTheme", 0) == 1;

            if (Light)
            {
                Back = Color.FromArgb(243, 243, 243); Hover = Color.FromArgb(228, 228, 228); ChartBack = Color.FromArgb(230, 230, 230);
                Text = Color.FromArgb(27, 27, 27); Dim = Color.FromArgb(112, 112, 112); Grid = Color.FromArgb(204, 204, 204);
                Shade = Color.FromArgb(206, 206, 206); Shine = Color.FromArgb(255, 255, 255);
                Up = Color.FromArgb(15, 123, 15); Down = Color.FromArgb(196, 43, 28);       // Windows' own success / critical tones
            }
            else
            {
                Back = Color.FromArgb(32, 32, 32); Hover = Color.FromArgb(48, 48, 48); ChartBack = Color.FromArgb(43, 43, 43);
                Text = Color.FromArgb(242, 242, 242); Dim = Color.FromArgb(150, 150, 150); Grid = Color.FromArgb(66, 66, 66);
                Shade = Color.FromArgb(17, 17, 17); Shine = Color.FromArgb(58, 58, 58);
                Up = Color.FromArgb(62, 207, 127); Down = Color.FromArgb(255, 99, 97);
            }

            // Tint every neutral surface slightly toward the accent colour, so the widget belongs to the theme
            // without the red/green numbers losing contrast. Text colours are left alone.
            double tint = cfg.useAccent ? Math.Max(0, Math.Min(0.5, cfg.accentTint)) : 0;
            if (tint > 0)
            {
                Color accent = AccentShades()[3];
                Back = Mix(Back, accent, tint); Hover = Mix(Hover, accent, tint); ChartBack = Mix(ChartBack, accent, tint);
                Grid = Mix(Grid, accent, tint); Shade = Mix(Shade, accent, tint); Shine = Mix(Shine, accent, tint);
            }

            // Rain uses Windows' "highlight" system colour (the classic selection blue unless a contrast theme
            // changes it); the falling-rain amount is the same colour 20% lighter.
            RainProb = Readable(SystemColors.Highlight, ChartBack, 2.0);
            RainAmount = Lighter(RainProb, 0.20);

            // The accent colour is only used for the row-drag marker.
            if (cfg.useAccent)
            {
                var a = AccentShades();
                Accent = Readable(Light ? a[4] : a[1], Back, 3.0);
            }
            else Accent = Light ? Color.FromArgb(0, 95, 184) : Color.FromArgb(120, 170, 255);
            return before != Signature();
        }

        /// <summary>
        /// Adds <paramref name="amount"/> (0-1) of full brightness to every channel. A channel cannot go past
        /// white, so whatever overshoots is taken out of the opacity instead.
        /// </summary>
        static Color Lighter(Color c, double amount)
        {
            double add = 255 * amount, r = c.R + add, g = c.G + add, b = c.B + add;
            double over = Math.Max(0, Math.Max(r, Math.Max(g, b)) - 255) / 255.0;
            int alpha = (int)Math.Round(c.A * (1 - over));
            return Color.FromArgb(Math.Max(32, alpha), (int)Math.Min(255, r), (int)Math.Min(255, g), (int)Math.Min(255, b));
        }

        static string Signature()
        {
            return Back.ToArgb() + "/" + RainProb.ToArgb() + "/" + RainAmount.ToArgb() + "/" + Accent.ToArgb();
        }

        // ---- fonts ---------------------------------------------------------

        static System.Drawing.Text.PrivateFontCollection pixelFonts;
        static FontFamily pixelFamily;
        static string loadedPath;
        static int pixelGrid = 15;

        public static bool PixelFont { get { return pixelFamily != null; } }

        /// <summary>Loads the pixel font named in the config (a path relative to the exe). Empty or missing = Segoe UI.</summary>
        public static void Configure(AppConfig cfg)
        {
            pixelGrid = Math.Max(4, cfg.pixelFontGrid);
            string path = string.IsNullOrWhiteSpace(cfg.pixelFont) ? null
                : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfg.pixelFont);
            if (path == loadedPath) return;
            loadedPath = path;
            pixelFamily = null;
            if (path == null || !System.IO.File.Exists(path)) return;
            try
            {
                Native.AddFontResourceEx(path, 0x10 /* FR_PRIVATE */, IntPtr.Zero);    // lets GDI text drawing find the face
                pixelFonts = new System.Drawing.Text.PrivateFontCollection();
                pixelFonts.AddFontFile(path);
                pixelFamily = pixelFonts.Families[0];
            }
            catch { pixelFamily = null; }
        }

        /// <summary>Call at the start of painting: pixel fonts are drawn without smoothing so every pixel is solid.</summary>
        public static void Prepare(Graphics g)
        {
            if (PixelFont) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        }

        public static Font Font(float px, bool bold = false)
        {
            if (pixelFamily != null)
            {
                // A pixel font is only crisp at whole multiples of its design grid, so snap to the nearest one.
                int mult = Math.Max(1, (int)Math.Round(px * 1.7 / pixelGrid));
                return new Font(pixelFamily, pixelGrid * mult, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            return new Font(bold ? "Segoe UI Semibold" : "Segoe UI", Math.Max(8f, px), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ---- retro pixel helpers -------------------------------------------

        // Zig-zag trend arrows, 9x6 cells. '#' is a filled cell.
        public static readonly string[] ArrowUp =
        {
            ".....####",
            ".......##",
            "......#.#",
            ".#...#..#",
            "#.#.#....",
            "...#.....",
        };
        public static readonly string[] ArrowDown =
        {
            "...#.....",
            "#.#.#....",
            ".#...#..#",
            "......#.#",
            ".......##",
            ".....####",
        };
        public static readonly string[] ArrowFlat =
        {
            ".........",
            ".........",
            ".#######.",
            ".#######.",
            ".........",
            ".........",
        };

        /// <summary>Size of one "retro pixel" for a given row height (2 real pixels at the default row size).</summary>
        public static int PixelSize(int rowH) { return Math.Max(1, (int)Math.Round(rowH / 16.0)); }

        public static void DrawPixels(Graphics g, string[] grid, int x, int y, int px, Color color)
        {
            using (var b = new SolidBrush(color))
                for (int r = 0; r < grid.Length; r++)
                    for (int c = 0; c < grid[r].Length; c++)
                        if (grid[r][c] == '#') g.FillRectangle(b, x + c * px, y + r * px, px, px);
        }

        /// <summary>Two-line engraved divider: a dark line with a lighter "shine" line under it.</summary>
        public static void DrawDivider(Graphics g, int x1, int x2, int y)
        {
            using (var b = new SolidBrush(Shade)) g.FillRectangle(b, x1, y - 1, x2 - x1, 1);
            using (var b = new SolidBrush(Shine)) g.FillRectangle(b, x1, y, x2 - x1, 1);
        }

        static readonly double[] TempStops = { 10, 32, 50, 65, 78, 90, 102 };   // degrees Fahrenheit
        static readonly Color[] TempColors =
        {
            Color.FromArgb(96, 110, 255), Color.FromArgb(80, 170, 255), Color.FromArgb(70, 210, 200), Color.FromArgb(120, 215, 110),
            Color.FromArgb(245, 215, 80), Color.FromArgb(255, 150, 60), Color.FromArgb(255, 80, 70),
        };

        /// <summary>Cold-to-hot colour for a temperature in degrees Fahrenheit.</summary>
        public static Color TempColor(double f)
        {
            Color c = TempColors[TempColors.Length - 1];
            if (f <= TempStops[0]) c = TempColors[0];
            else
                for (int i = 1; i < TempStops.Length; i++)
                {
                    if (f > TempStops[i]) continue;
                    c = Mix(TempColors[i - 1], TempColors[i], (f - TempStops[i - 1]) / (TempStops[i] - TempStops[i - 1]));
                    break;
                }
            return Light ? Mix(c, Color.Black, 0.22) : c;       // same hues, deeper on a light background
        }

        public const TextFormatFlags L =TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        public const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        public static int Measure(string s, Font f)
        {
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }
    }

    /// <summary>
    /// Borderless window pinned to the bottom of the z-order (just above the desktop).
    /// Fetches only when it is due AND at least partly visible; otherwise it idles.
    /// </summary>
    abstract class WidgetForm : Form
    {
        protected readonly App Owner_;
        protected AppConfig Cfg { get { return Owner_.Cfg; } }

        readonly Timer timer = new Timer();
        bool pinBottom = true, loaded;

        /// <summary>One independently refreshed data source (stocks, weather) shown in the window.</summary>
        protected sealed class Feed
        {
            public Func<int> RefreshMs;
            public Func<Task> Fetch;
            public DateTime LastAttemptUtc = DateTime.MinValue;
            public bool Fetching, EverSucceeded;
            public string Error;
        }
        protected readonly System.Collections.Generic.List<Feed> Feeds = new System.Collections.Generic.List<Feed>();

        public bool ForceVisible;               // snapshot mode
        public event Action Fetched;            // raised once every feed has been tried and none is still running

        protected float Vh = 14.4f, Vw = 25.6f;

        protected abstract Size ComputeSize();  // also rebuilds fonts/metrics

        protected WidgetForm(App owner)
        {
            Owner_ = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            Opacity = Math.Max(0.2, Math.Min(1.0, Cfg.opacity));
            timer.Tick += delegate { Tick(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)Native.WS_EX_TOOLWINDOW;     // no taskbar button, hidden from Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int square = 1;                                     // DWMWCP_DONOTROUND: square corners
            Native.DwmSetWindowAttribute(Handle, 33, ref square, 4);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateUnits(Screen.PrimaryScreen);
            int? x = Cfg.x, y = Cfg.y;
            Size size = ComputeSize();
            Point p;
            if (x.HasValue && y.HasValue && OnAnyScreen(new Rectangle(x.Value, y.Value, size.Width, size.Height)))
            {
                p = new Point(x.Value, y.Value);
                UpdateUnits(Screen.FromPoint(p));
                size = ComputeSize();
            }
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;      // default: top-right corner of the primary display
                p = new Point(wa.Right - size.Width - (int)(2 * Vh), wa.Top + (int)(2 * Vh));
            }
            Location = p;
            ClientSize = size;
            loaded = true;
            SendToBottom();
            Schedule(50);
        }

        static bool OnAnyScreen(Rectangle r)
        {
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) return true;
            return false;
        }

        void UpdateUnits(Screen s)
        {
            Vh = s.Bounds.Height / 100f;
            Vw = s.Bounds.Width / 100f;
        }

        protected void Relayout()
        {
            if (!loaded) return;
            UpdateUnits(Screen.FromControl(this));
            ClientSize = ComputeSize();
            Invalidate();
        }

        // ---- z-order -------------------------------------------------------

        void SendToBottom()
        {
            Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>While "Show desktop" (Win+D) is active the desktop is raised above every window, so float above it.</summary>
        public void SetShowDesktopMode(bool on)
        {
            if (!IsHandleCreated || on == !pinBottom) return;
            if (on)
            {
                pinBottom = false;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else
            {
                Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                pinBottom = true;
                SendToBottom();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_WINDOWPOSCHANGING && pinBottom)
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                wp.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            base.WndProc(ref m);
            if (m.Msg == Native.WM_EXITSIZEMOVE)
            {
                Cfg.x = Left; Cfg.y = Top;
                Cfg.Save();
                Relayout();                                     // the widget may now be on a display with a different size
            }
            else if (m.Msg == Native.WM_DISPLAYCHANGE) Relayout();
        }

        protected void BeginWindowDrag()
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        // ---- refresh scheduling -------------------------------------------

        public bool IsDesktopVisible()
        {
            if (ForceVisible) return true;
            if (!IsHandleCreated || !Visible || Owner_.SessionLocked) return false;
            return Native.VisiblePercent(Handle, Bounds) >= Math.Max(1, Cfg.minVisiblePercent);
        }

        void Schedule(double ms)
        {
            timer.Stop();
            timer.Interval = (int)Math.Max(20, Math.Min(ms, int.MaxValue - 1));
            timer.Start();
        }

        /// <summary>
        /// Starts every feed that is due, provided the widget is visible. While nothing is due this sleeps until
        /// the next due time; while something is due but the widget is hidden it only re-checks visibility.
        /// </summary>
        void Tick()
        {
            timer.Stop();
            if (IsDisposed) return;
            double next = double.MaxValue;
            var due = new System.Collections.Generic.List<Feed>();
            foreach (var f in Feeds)
            {
                if (f.Fetching) continue;
                double remaining = f.LastAttemptUtc == DateTime.MinValue ? 0
                    : Math.Max(1000, f.RefreshMs()) - (DateTime.UtcNow - f.LastAttemptUtc).TotalMilliseconds;
                if (remaining <= 0) due.Add(f); else next = Math.Min(next, remaining);
            }
            if (due.Count > 0)
            {
                if (!IsDesktopVisible()) { Schedule(Math.Max(250, Cfg.visibilityPollMs)); return; }
                foreach (var f in due) Run(f);
            }
            if (next < double.MaxValue) Schedule(next);
        }

        async void Run(Feed f)
        {
            f.Fetching = true;
            try
            {
                await f.Fetch();
                f.Error = null;
                f.EverSucceeded = true;
            }
            catch (Exception ex)
            {
                f.Error = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Debug.WriteLine(ex);
            }
            f.Fetching = false;
            f.LastAttemptUtc = DateTime.UtcNow;
            if (IsDisposed) return;
            Relayout();
            Tick();
            foreach (var other in Feeds) if (other.Fetching || other.LastAttemptUtc == DateTime.MinValue) return;
            var done = Fetched; if (done != null) done();
        }

        /// <summary>Advance any animation by one step (used when capturing frames for documentation).</summary>
        public virtual void StepAnimation() { }

        /// <summary>The Windows theme or accent colour changed: repaint with the new colours.</summary>
        public void ThemeChanged()
        {
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Invalidate();
        }

        /// <summary>Re-evaluate now (something changed that may have made the widget visible).</summary>
        public void Kick() { if (loaded && !IsDisposed) Tick(); }

        public void RefreshNow()
        {
            foreach (var f in Feeds) f.LastAttemptUtc = DateTime.MinValue;
            Kick();
        }

        protected void AddCommonMenuItems(ContextMenuStrip menu)
        {
            menu.Items.Add("Refresh now", null, delegate { RefreshNow(); });
            menu.Items.Add("Edit config…", null, delegate { Owner_.OpenConfig(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Pixill", null, delegate { Owner_.Quit(); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
