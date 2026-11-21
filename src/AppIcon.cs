using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Pixill
{
    /// <summary>
    /// The logo: a four-pane window, night outside, a few small raindrops.
    /// Drawn from a 16x16 pixel grid so it stays crisp at every whole multiple.
    /// </summary>
    static class AppIcon
    {
        static readonly string[] Grid =
        {
            // Only an impression of a window: a one-pixel wooden frame with the top corners cut in (an arch),
            // a cross with a few pixels missing, black night, five drops of slanting rain, and fog on the
            // bottom of the lower panes. There is no sill; the widget's rows are the sill.
            "...WWWWWWWWWW...",
            ".WWNNNNWNNNNNWW.",
            "WNNNNNNWNNNNNrNW",
            "wNNNNNNWNNNNNNNW",
            "WNNNNNNNNNNqNNNW",
            "WNNNNNNWNNNqNNNW",
            "WNNNNNNWNNNNNNNW",
            "WWWwWWNWWWWWwWWW",
            "WNNNNNNWNNNNNNNW",
            "WNNNNNNWNrNNNNNW",
            "WNNNNrNNNNNNNNNw",
            "WNNNNNNWNNNNNNNW",
            "waaqaaaWaaaaaaaW",
            "WbbqbbbWbbbbbbbW",
            "WccccccWcccccccW",
            ".WWWWWWWwWWWWWW.",
        };

        static readonly Dictionary<char, Color> Palette = new Dictionary<char, Color>
        {
            { 'W', Color.FromArgb(176, 137, 96) },      // unpainted wood
            { 'w', Color.FromArgb(146, 109, 74) },      // darker grain
            { 'N', Color.FromArgb(0, 0, 0) },           // night
            { 'r', Color.FromArgb(70, 120, 210) },      // one-pixel raindrop
            { 'q', Color.FromArgb(50, 90, 170) },       // two-pixel raindrop, slightly different shade
            { 'a', Color.FromArgb(13, 14, 17) },        // fogged glass, faintest (top of the fog)
            { 'b', Color.FromArgb(21, 23, 27) },
            { 'c', Color.FromArgb(30, 32, 38) },        // fogged glass, densest (bottom row)
        };
        public static Bitmap Render(int size)
        {
            int scale = Math.Max(1, size / 16);
            var bmp = new Bitmap(16 * scale, 16 * scale, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        Color c;
                        if (!Palette.TryGetValue(Grid[y][x], out c)) continue;
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, x * scale, y * scale, scale, scale);
                    }
            }
            return bmp;
        }

        public static Icon ToIcon(int size)
        {
            using (var bmp = Render(size))
            {
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        /// <summary>Writes a multi-size .ico file (used as the exe's icon at build time).</summary>
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 32, 48, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));    // width (0 means 256)
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));    // height
                    w.Write((byte)0); w.Write((byte)0);                 // palette size, reserved
                    w.Write((short)1); w.Write((short)32);              // planes, bits per pixel
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }
}
