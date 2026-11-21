using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixill
{
    /// <summary>Small type-to-search box with live results; closes on pick, Esc, or losing focus.</summary>
    sealed class SearchPopup : Form
    {
        readonly TextBox box = new TextBox();
        readonly ListBox list = new ListBox();
        readonly Timer debounce = new Timer { Interval = 250 };
        readonly Func<string, Task<List<SearchItem>>> search;
        readonly Action<SearchItem> picked;
        int seq;
        bool done;

        public static void Open(Form anchor, string hint, Func<string, Task<List<SearchItem>>> search, Action<SearchItem> picked)
        {
            var p = new SearchPopup(anchor, hint, search, picked);
            p.Show();
            p.Activate();
            p.box.Focus();
        }

        SearchPopup(Form anchor, string hint, Func<string, Task<List<SearchItem>>> search, Action<SearchItem> picked)
        {
            this.search = search; this.picked = picked;
            var screen = Screen.FromControl(anchor);
            float vh = screen.Bounds.Height / 100f;
            int fontPx = (int)Math.Round(vh * 1.25f);
            Font = Theme.Font(fontPx);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Hover;
            Padding = new Padding(1);

            int w = Math.Max(anchor.Width, (int)(vh * 30));
            int rowH = fontPx + 10;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Back; box.ForeColor = Theme.Text;
            box.Dock = DockStyle.Top;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Back; list.ForeColor = Theme.Text;
            list.IntegralHeight = false;
            list.ItemHeight = rowH;
            list.Dock = DockStyle.Fill;
            list.Items.Add(hint);
            Controls.Add(list);
            Controls.Add(box);
            ClientSize = new Size(w, box.PreferredHeight + rowH * 8 + 2);

            int x = anchor.Left, y = anchor.Bottom + 4;
            var wa = screen.WorkingArea;
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, anchor.Top - Height - 4);
            if (x + Width > wa.Right) x = wa.Right - Width;
            Location = new Point(Math.Max(wa.Left, x), y);

            box.TextChanged += delegate { debounce.Stop(); debounce.Start(); };
            debounce.Tick += delegate { debounce.Stop(); RunSearch(); };
            box.KeyDown += OnBoxKey;
            list.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { Pick(list.SelectedItem as SearchItem); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) Close();
            };
            list.MouseClick += delegate (object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0) Pick(list.Items[i] as SearchItem);
            };
            Deactivate += delegate { if (!done) Close(); };
        }

        void OnBoxKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter)
            {
                var item = (list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null)) as SearchItem;
                Pick(item);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && list.SelectedIndex > 0)
            {
                list.SelectedIndex--;
                e.Handled = true;
            }
        }

        async void RunSearch()
        {
            string text = box.Text.Trim();
            int mine = ++seq;
            if (text.Length == 0) { list.Items.Clear(); return; }
            List<SearchItem> items;
            string failure = null;
            try { items = await search(text); }
            catch (Exception ex) { items = new List<SearchItem>(); failure = "Search failed: " + ex.Message; }
            if (mine != seq || IsDisposed) return;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in items) list.Items.Add(it);
            if (items.Count == 0) list.Items.Add(failure ?? "No matches");
            list.EndUpdate();
        }

        void Pick(SearchItem item)
        {
            if (item == null || done) return;
            done = true;
            Close();
            picked(item);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) debounce.Dispose();
            base.Dispose(disposing);
        }
    }
}
