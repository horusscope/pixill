using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixill
{
    /// <summary>
    /// The single desktop window: a grip strip, the stock rows, then the weather block.
    ///
    /// Stocks: name, trend arrow, price, day change. Drag a row to reorder.
    /// Weather: a header with the current temperature, then one row per day (today first) with low, a
    /// low-to-high bar, high, chance of rain, and a 24-bar chart running midnight to midnight. In each chart
    /// the chance of rain grows up from the bottom and the amount grows down from the top; each is limited to
    /// half the row. The amount that fills the top half is the same for every row and is printed once.
    ///
    /// The stock rows set the window width; the weather charts stretch or shrink to fit it.
    /// </summary>
    sealed class DeskWidget : WidgetForm
    {
        StocksConfig S { get { return Cfg.stocks; } }
        WeatherConfig W { get { return Cfg.weather; } }

        readonly Feed stockFeed, weatherFeed;
        readonly Dictionary<string, Quote> quotes = new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);
        WeatherData weather;

        // shared
        Font fSmall;
        int px, pad, gap, gripH, width;

        // stocks section
        Font fStock;
        int sRowH, sTop, sRows, wName, wPrice, wDelta, wIcon;
        int hover = -1, dragIdx = -1, dropIdx = -1;
        bool dragging;

        // weather section
        Font fWeather;
        int wRowH, wTop, wTemp, wRain, wRange, wScale, barW, plotW;
        double scale = 0.25;

        // rain animation
        readonly Timer rainTimer = new Timer();
        Rectangle chartsRect;       // the block of charts, the only part repainted per animation step
        long frame;
        int animTicks;
        bool rainAhead, animVisible;

        Point downPt;
        bool moveArmed, moved;

        public DeskWidget(App owner) : base(owner)
        {
            Text = "Pixill";
            if (W.enabled && W.animateRain)
            {
                rainTimer.Interval = 1000 / Math.Max(1, Math.Min(30, W.rainFps));
                rainTimer.Tick += delegate { RainStep(); };
                rainTimer.Start();
            }
            if (S.enabled)
            {
                stockFeed = new Feed { RefreshMs = () => S.refreshMs, Fetch = FetchStocks };
                Feeds.Add(stockFeed);
            }
            if (W.enabled)
            {
                weatherFeed = new Feed { RefreshMs = () => W.refreshMs, Fetch = FetchWeather };
                Feeds.Add(weatherFeed);
            }
        }

        // ---- data ----------------------------------------------------------

        async Task FetchStocks()
        {
            var syms = S.symbols.ToArray();
            if (syms.Length == 0) return;
            var tasks = syms.Select(s => Yahoo.GetQuote(s)).ToArray();
            Exception first = null;
            int ok = 0;
            foreach (var t in tasks)
            {
                try { var q = await t; quotes[q.Symbol] = q; ok++; }
                catch (Exception ex) { if (first == null) first = ex; }
            }
            if (ok == 0 && first != null) throw first;
        }

        bool HasLocation { get { return W.latitude.HasValue && W.longitude.HasValue; } }
        int DayCount { get { return Math.Max(1, Math.Min(16, W.days)); } }

        async Task FetchWeather()
        {
            if (!HasLocation) return;
            weather = await OpenMeteo.Get(W.latitude.Value, W.longitude.Value, W.temperatureUnit, W.precipitationUnit, DayCount);

            // is there any rain left to animate (this hour or later)?
            rainAhead = false;
            for (int d = 0; d < weather.Days.Count && !rainAhead; d++)
                for (int h = d == 0 ? Math.Max(0, weather.CurrentHour) : 0; h < 24; h++)
                    if (weather.Days[d].Amount[h] > 0) { rainAhead = true; break; }
        }

        /// <summary>
        /// One animation step. Does nothing unless there is rain to show and the widget is visible
        /// (visibility is re-checked about once a second, not every step).
        /// </summary>
        void RainStep()
        {
            if (!rainAhead || weather == null || !IsHandleCreated) return;
            if (animTicks++ % Math.Max(1, W.rainFps) == 0) animVisible = IsDesktopVisible();
            if (!animVisible) return;
            frame++;
            Invalidate(chartsRect);
        }

        public override void StepAnimation() { frame++; }

        static uint Hash(int a, int b, int c)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) rainTimer.Dispose();
            base.Dispose(disposing);
        }

        // ---- text ----------------------------------------------------------

        string NameOf(string sym)
        {
            string label;
            return S.labels != null && S.labels.TryGetValue(sym, out label) && !string.IsNullOrEmpty(label) ? label : sym;
        }

        Quote Get(string sym) { Quote q; return quotes.TryGetValue(sym, out q) ? q : null; }

        static string PriceText(Quote q)
        {
            if (q == null) return "—";
            return q.Price >= 1 ? q.Price.ToString("N2") : q.Price.ToString("0.####");
        }

        static string DeltaText(Quote q)
        {
            if (q == null) return "—";
            string fmt = Math.Abs(q.Price) >= 1 ? "+0.00;−0.00;0.00" : "+0.####;−0.####;0";
            return q.Change.ToString(fmt) + "  " + q.ChangePct.ToString("+0.00;−0.00;0.00") + "%";
        }

        static string Deg(double v) { return double.IsNaN(v) ? "—" : Math.Round(v).ToString("0") + "°"; }
        double ToF(double t) { return W.temperatureUnit == "celsius" ? t * 9 / 5 + 32 : t; }
        string ScaleLabel() { return scale.ToString("0.##") + (W.precipitationUnit == "inch" ? "\"" : "mm"); }

        /// <summary>
        /// Amount that fills the top half of a chart row. Normally the configured base; if any hour in the week
        /// exceeds it, the next whole step above the week's wettest hour (base 0.25": 0.6" -> 0.75", 1.3" -> 1.5").
        /// </summary>
        double ComputeScale()
        {
            double unit = W.amountScale > 0 ? W.amountScale : 0.25;
            double max = 0;
            if (weather != null)
                foreach (var d in weather.Days)
                    foreach (double a in d.Amount) max = Math.Max(max, a);
            return max <= unit ? unit : (Math.Floor(max / unit) + 1) * unit;
        }

        // ---- layout --------------------------------------------------------

        static int RowHeight(double rowVh, double maxVh, float vh)
        {
            double v = Math.Max(1.0, Math.Min(rowVh, Math.Max(1.0, maxVh)));
            return Math.Max(14, (int)Math.Round(v * vh));
        }

        protected override Size ComputeSize()
        {
            if (fSmall != null) { fSmall.Dispose(); fStock.Dispose(); fWeather.Dispose(); }
            sRowH = RowHeight(S.rowVh, S.maxRowVh, Vh);
            wRowH = RowHeight(W.rowVh, W.maxRowVh, Vh);
            int baseH = S.enabled ? sRowH : wRowH;
            fStock = Theme.Font(sRowH * 0.50f, true);
            fWeather = Theme.Font(wRowH * 0.50f, true);
            fSmall = Theme.Font(Theme.PixelFont ? baseH * 0.36f : Math.Max(10f, baseH * 0.36f));
            px = Theme.PixelSize(baseH);
            pad = (int)Math.Round(baseH * 0.35);
            gap = (int)Math.Round(baseH * 0.45);
            gripH = 5 * px;

            int y = gripH;
            width = 0;

            sTop = y; sRows = 0;
            if (S.enabled)
            {
                wName = Theme.Measure("WWW", fStock); wPrice = Theme.Measure("000.00", fStock); wDelta = Theme.Measure("+0.00  +0.00%", fStock);
                foreach (var s in S.symbols)
                {
                    var q = Get(s);
                    wName = Math.Max(wName, Theme.Measure(NameOf(s), fStock));
                    wPrice = Math.Max(wPrice, Theme.Measure(PriceText(q), fStock));
                    wDelta = Math.Max(wDelta, Theme.Measure(DeltaText(q), fStock));
                }
                wIcon = 9 * px;
                width = pad + wName + gap / 2 + wIcon + gap + wPrice + gap + wDelta + pad;
                sRows = Math.Max(1, S.symbols.Count);
                y += sRows * sRowH;
            }

            wTop = y;
            if (W.enabled)
            {
                scale = ComputeScale();
                // size the number columns to what is actually shown, so the chart gets the rest
                wTemp = Theme.Measure("00°", fWeather);
                wRain = Theme.Measure("00%", fWeather);
                if (weather != null)
                    foreach (var d in weather.Days)
                    {
                        wTemp = Math.Max(wTemp, Math.Max(Theme.Measure(Deg(d.Low), fWeather), Theme.Measure(Deg(d.High), fWeather)));
                        wRain = Math.Max(wRain, Theme.Measure(Math.Round(d.RainChance).ToString("0") + "%", fWeather));
                    }
                wScale = Theme.Measure(ScaleLabel(), fSmall);
                int cell = 2 * px + 1;                          // one block of the temperature bar plus its gap
                wRange = (int)Math.Round(wRowH * 1.8) / cell * cell - 1;
                int fixedW = pad + wTemp + gap / 2 + wRange + gap / 2 + wTemp + gap + wRain + gap + pad;
                // The stock rows decide the width; the 24 chart bars take whatever is left (at least 2px each).
                if (!S.enabled) width = fixedW + 24 * Math.Max(2, (int)(7 * Vw / 24));
                barW = Math.Max(2, (width - fixedW) / 24);
                plotW = barW * 24;
                width = Math.Max(width, fixedW + plotW);
                y += wRowH + DayCount * wRowH;                  // header row + one row per day
            }

            width = Math.Max(width, 60);
            chartsRect = W.enabled ? new Rectangle(width - pad - plotW, wTop + wRowH, plotW, DayCount * wRowH) : Rectangle.Empty;
            return new Size(width, y + pad / 3);
        }

        int StockRowAt(Point p)
        {
            if (!S.enabled || p.Y < sTop || p.Y >= sTop + sRows * sRowH) return -1;
            int i = (p.Y - sTop) / sRowH;
            return i >= 0 && i < S.symbols.Count ? i : -1;
        }

        // ---- paint ---------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            int w = ClientSize.Width;

            // an animation step only invalidates the charts, so skip all the text in that case
            if (W.enabled && !chartsRect.IsEmpty && chartsRect.Contains(e.ClipRectangle))
            {
                PaintWeather(g, w, true);
                return;
            }

            // grip strip: a row of dots; red while any data is failing to load
            bool failing = Feeds.Any(f => f.Error != null);
            using (var b = new SolidBrush(failing ? Theme.Down : Theme.Shine))
                for (int i = -6; i < 6; i++)
                    g.FillRectangle(b, w / 2 + i * 2 * px, 2 * px, px, px);

            if (S.enabled) PaintStocks(g, w);
            if (W.enabled) PaintWeather(g, w, false);
        }

        void PaintStocks(Graphics g, int w)
        {
            if (S.symbols.Count == 0)
            {
                Theme.DrawDivider(g, 0, w, sTop);
                TextRenderer.DrawText(g, "Right-click to add a symbol", fSmall, new Rectangle(pad, sTop, w - 2 * pad, sRowH), Theme.Dim, Theme.L);
                return;
            }

            for (int i = 0; i < S.symbols.Count; i++)
            {
                string sym = S.symbols[i];
                var q = Get(sym);
                var row = new Rectangle(0, sTop + i * sRowH, w, sRowH);
                if (i == hover && !dragging || dragging && i == dragIdx)
                    using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, row);
                Theme.DrawDivider(g, 0, w, row.Top);

                bool flat = q == null || Math.Abs(q.Change) < 1e-9;
                Color c = flat ? Theme.Dim : q.Change > 0 ? Theme.Up : Theme.Down;
                int x = pad;
                TextRenderer.DrawText(g, NameOf(sym), fStock, new Rectangle(x, row.Top, wName, sRowH), Theme.Text, Theme.L | TextFormatFlags.EndEllipsis);
                x += wName + gap / 2;
                if (q != null)
                    Theme.DrawPixels(g, flat ? Theme.ArrowFlat : q.Change > 0 ? Theme.ArrowUp : Theme.ArrowDown, x, row.Top + (sRowH - 6 * px) / 2 + 1, px, c);
                x += wIcon + gap;
                TextRenderer.DrawText(g, PriceText(q), fStock, new Rectangle(x, row.Top, wPrice, sRowH), Theme.Text, Theme.R);
                TextRenderer.DrawText(g, DeltaText(q), fStock, new Rectangle(w - pad - wDelta, row.Top, wDelta, sRowH), c, Theme.R);
            }

            if (dragging && dropIdx >= 0)
            {
                int y = sTop + dropIdx * sRowH;
                using (var p = new Pen(Theme.Accent, 2f)) g.DrawLine(p, pad / 2, y, w - pad / 2, y);
            }
        }

        void PaintWeather(Graphics g, int w, bool chartsOnly)
        {
            if (!chartsOnly) PaintWeatherHeader(g, w);
            PaintWeatherRows(g, w, chartsOnly);
        }

        void PaintWeatherHeader(Graphics g, int w)
        {
            // header: current temperature and place (or what is wrong)
            Theme.DrawDivider(g, 0, w, wTop);
            string temp = weather == null ? "--°" : Deg(weather.Temp);
            int wNow = Theme.Measure(temp, fWeather);
            string note; Color noteColor = Theme.Dim;
            if (!HasLocation) note = "Click to set location";
            else if (weatherFeed != null && weatherFeed.Error != null) { note = weatherFeed.EverSucceeded ? "stale" : "offline"; noteColor = Theme.Down; }
            else note = W.name ?? "";
            TextRenderer.DrawText(g, temp, fWeather, new Rectangle(pad, wTop, wNow, wRowH), Theme.Text, Theme.L);
            TextRenderer.DrawText(g, note, fSmall, new Rectangle(pad + wNow + gap, wTop, w - 2 * pad - wNow - gap - wScale - gap, wRowH), noteColor, Theme.L | TextFormatFlags.EndEllipsis);
            // the rain-amount scale for the whole week (the amount at each chart's middle line) sits above the charts
            TextRenderer.DrawText(g, ScaleLabel(), fSmall, new Rectangle(w - pad - wScale, wTop, wScale, wRowH), Color.FromArgb(255, Theme.RainAmount), Theme.R);
        }

        void PaintWeatherRows(Graphics g, int w, bool chartsOnly)
        {
            // columns: low | range bar | high | chance of rain | chart (right-aligned)
            int xLo = pad, xRange = xLo + wTemp + gap / 2, xHi = xRange + wRange + gap / 2, xRain = xHi + wTemp + gap;
            int xPlot = w - pad - plotW;
            int chartH = Math.Max(8, (wRowH - 4 * px) / 2 * 2);     // even, so both halves are the same height

            // the temperature bars share one scale: the coldest low to the warmest high of the week
            double weekMin = double.MaxValue, weekMax = double.MinValue;
            if (weather != null)
            {
                foreach (var d in weather.Days)
                {
                    if (!double.IsNaN(d.Low)) weekMin = Math.Min(weekMin, d.Low);
                    if (!double.IsNaN(d.High)) weekMax = Math.Max(weekMax, d.High);
                }
                if (!double.IsNaN(weather.Temp)) { weekMin = Math.Min(weekMin, weather.Temp); weekMax = Math.Max(weekMax, weather.Temp); }
            }

            for (int i = 0; i < DayCount; i++)
            {
                var day = weather != null && i < weather.Days.Count ? weather.Days[i] : null;
                int top = wTop + wRowH + i * wRowH;
                bool today = i == 0;
                Theme.DrawDivider(g, 0, w, top);

                if (!chartsOnly)
                {
                    TextRenderer.DrawText(g, day != null ? Deg(day.Low) : "—", fWeather, new Rectangle(xLo, top, wTemp, wRowH), Theme.Dim, Theme.L);   // lines up with the stock names above
                    DrawRange(g, new Rectangle(xRange, top, wRange, wRowH), day, weekMin, weekMax, today && weather != null ? weather.Temp : double.NaN);
                    TextRenderer.DrawText(g, day != null ? Deg(day.High) : "—", fWeather, new Rectangle(xHi, top, wTemp, wRowH), Theme.Text, Theme.L);
                    string rain = day != null ? Math.Round(day.RainChance).ToString("0") + "%" : "—";
                    TextRenderer.DrawText(g, rain, fWeather, new Rectangle(xRain, top, wRain, wRowH), day != null && day.RainChance >= 1 ? Theme.RainProb : Theme.Dim, Theme.R);
                }

                var plot = new Rectangle(xPlot, top + (wRowH - chartH) / 2 + 1, plotW, chartH);
                DrawChart(g, plot, day, today && weather != null ? weather.CurrentHour : -1, i);
            }
        }

        /// <summary>
        /// Blocky low-to-high bar. The track spans the week's coldest low to warmest high; the lit blocks are this
        /// day's range, each coloured by the temperature at its position. Today also gets a notch at the current temperature.
        /// </summary>
        void DrawRange(Graphics g, Rectangle area, DayWeather day, double weekMin, double weekMax, double now)
        {
            int block = 2 * px, cell = block + 1, h = 3 * px;
            int y = area.Top + (area.Height - h) / 2 + 1;
            int count = (area.Width + 1) / cell;
            bool ok = day != null && !double.IsNaN(day.Low) && !double.IsNaN(day.High) && weekMax > weekMin;
            double span = weekMax - weekMin;
            int first = ok ? (int)Math.Floor((day.Low - weekMin) / span * count) : 0;
            int last = ok ? (int)Math.Ceiling((day.High - weekMin) / span * count) - 1 : -1;
            first = Math.Max(0, Math.Min(count - 1, first));
            last = Math.Max(first, Math.Min(count - 1, last));
            for (int b = 0; b < count; b++)
            {
                bool lit = ok && b >= first && b <= last;
                Color c = lit ? Theme.TempColor(ToF(weekMin + (b + 0.5) / count * span)) : Theme.ChartBack;
                using (var br = new SolidBrush(c)) g.FillRectangle(br, area.Left + b * cell, y, block, h);
            }
            if (ok && !double.IsNaN(now))
            {
                int nb = Math.Max(0, Math.Min(count - 1, (int)Math.Floor((now - weekMin) / span * count)));
                using (var br = new SolidBrush(Theme.Text)) g.FillRectangle(br, area.Left + nb * cell, y - px, block, h + 2 * px);
            }
        }

        void DrawChart(Graphics g, Rectangle plot, DayWeather day, int nowHour, int dayIndex)
        {
            int half = plot.Height / 2, mid = plot.Top + half;
            using (var b = new SolidBrush(Theme.ChartBack)) g.FillRectangle(b, plot);
            if (nowHour >= 0)
                using (var b = new SolidBrush(Theme.Grid)) g.FillRectangle(b, plot.Left + nowHour * barW, plot.Top, barW, plot.Height);

            if (day != null)
            {
                int inner = barW >= 4 ? barW - 1 : barW;
                for (int h = 0; h < 24; h++)
                {
                    int alpha = h < nowHour ? 90 : 255;                  // hours already past are dimmed
                    int x = plot.Left + h * barW;
                    int up = (int)Math.Round(Math.Max(0, Math.Min(100, day.Prob[h])) / 100.0 * half);
                    int down = (int)Math.Round(Math.Max(0, Math.Min(scale, day.Amount[h])) / scale * half);
                    if (day.Prob[h] > 0 && up == 0) up = 1;              // anything above zero shows at least a sliver
                    if (day.Amount[h] > 0 && down == 0) down = 1;
                    if (up > 0)
                        using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.RainProb)))
                            g.FillRectangle(b, x, plot.Bottom - up, inner, up);
                    if (down > 0)
                    {
                        Color amt = Theme.RainAmount;
                        bool animate = W.animateRain && h >= nowHour;    // hours already past stay still
                        if (!animate)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(amt.A * alpha / 255, amt)))
                                g.FillRectangle(b, x, plot.Top, inner, down);
                            continue;
                        }

                        // Falling rain: a faint backdrop shows how far down the amount reaches, and one pixel
                        // "drop" per sub-column falls through it. More rain = taller, faster, longer drops.
                        down = Math.Max(down, px);
                        double level = Math.Min(1.0, day.Amount[h] / scale);
                        using (var b = new SolidBrush(Color.FromArgb(amt.A * 30 / 100, amt)))
                            g.FillRectangle(b, x, plot.Top, inner, down);
                        int cols = Math.Max(1, inner / px), rows = (down + px - 1) / px;
                        double speed = 0.2 + 0.8 * level;               // cells per animation step
                        int len = level > 0.5 ? 2 : 1;
                        using (var b = new SolidBrush(amt))
                            for (int c = 0; c < cols; c++)
                            {
                                uint hash = Hash(dayIndex, h, c);
                                int cycle = rows + 2 + (int)(hash % 3);   // the pause before the next drop keeps it sparse
                                int head = (int)(((hash >> 8) % (uint)cycle + (long)(frame * speed)) % cycle);
                                for (int k = 0; k < len; k++)
                                {
                                    int r = head - k;
                                    if (r < 0 || r >= rows) continue;
                                    int cy = r * px;
                                    g.FillRectangle(b, x + c * inner / cols, plot.Top + cy, px, Math.Min(px, down - cy));
                                }
                            }
                    }
                }
            }
            using (var p = new Pen(Theme.Back)) g.DrawLine(p, plot.Left, mid, plot.Right - 1, mid);
        }

        // ---- interaction ---------------------------------------------------
        // Stock rows: drag to reorder. Anywhere else (grip strip, weather block): drag to move the window.

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            downPt = e.Location; moved = false; dragging = false;
            int row = StockRowAt(e.Location);
            if (row >= 0) { dragIdx = row; moveArmed = false; }
            else { dragIdx = -1; moveArmed = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool left = (e.Button & MouseButtons.Left) != 0;
            if (left && moveArmed && !moved && (Math.Abs(e.X - downPt.X) > 3 || Math.Abs(e.Y - downPt.Y) > 3))
            {
                moved = true;
                BeginWindowDrag();
                return;
            }
            if (left && dragIdx >= 0)
            {
                if (!dragging && Math.Abs(e.Y - downPt.Y) > 4) dragging = true;
                if (dragging)
                {
                    int d = (e.Y - sTop + sRowH / 2) / sRowH;
                    dropIdx = Math.Max(0, Math.Min(S.symbols.Count, d));
                    Invalidate();
                }
                return;
            }
            int h = StockRowAt(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (dragging && dragIdx >= 0 && dropIdx >= 0)
            {
                string sym = S.symbols[dragIdx];
                int to = dropIdx > dragIdx ? dropIdx - 1 : dropIdx;
                if (to != dragIdx)
                {
                    S.symbols.RemoveAt(dragIdx);
                    S.symbols.Insert(to, sym);
                    Cfg.Save();
                }
            }
            else if (moveArmed && !moved && W.enabled && !HasLocation && e.Y >= wTop) SetLocation();
            dragIdx = dropIdx = -1; dragging = false; moveArmed = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        void ShowMenu(Point at)
        {
            var menu = new ContextMenuStrip();
            if (S.enabled)
            {
                menu.Items.Add("Add symbol…", null, delegate { AddSymbol(); });
                int row = StockRowAt(at);
                if (row >= 0)
                {
                    string sym = S.symbols[row];
                    menu.Items.Add("Remove " + NameOf(sym), null, delegate
                    {
                        S.symbols.Remove(sym);
                        quotes.Remove(sym);
                        Cfg.Save();
                        Relayout();
                    });
                }
            }
            if (W.enabled) menu.Items.Add("Set weather location…", null, delegate { SetLocation(); });
            menu.Items.Add(new ToolStripSeparator());
            AddCommonMenuItems(menu);
            menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
            menu.Show(this, at);
        }

        void AddSymbol()
        {
            SearchPopup.Open(this, "Type a ticker or company name", Yahoo.Search, delegate (SearchItem item)
            {
                string sym = (string)item.Tag;
                if (S.symbols.Any(s => string.Equals(s, sym, StringComparison.OrdinalIgnoreCase))) return;
                S.symbols.Add(sym);
                Cfg.Save();
                Relayout();
                RefreshNow();
            });
        }

        void SetLocation()
        {
            SearchPopup.Open(this, "Type a city name", OpenMeteo.Search, delegate (SearchItem item)
            {
                var p = (Place)item.Tag;
                W.name = p.Name; W.latitude = p.Lat; W.longitude = p.Lon;
                weather = null;
                Cfg.Save();
                Relayout();
                RefreshNow();
            });
        }
    }
}
