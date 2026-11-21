# Assembles PNG frames into a compact looping GIF: one shared palette, and after the first frame only the
# rectangle that changed is stored. Usage: make-gif.ps1 -Frames <dir with frame_*.png> -Out <file.gif> -DelayCs 10
param([string]$Frames, [string]$Out, [int]$DelayCs = 10)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class TinyGif
{
    static int[] Pixels(string file, out int w, out int h)
    {
        using (var bmp = new Bitmap(file))
        {
            w = bmp.Width; h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            return px;
        }
    }

    static void W16(Stream s, int v) { s.WriteByte((byte)(v & 255)); s.WriteByte((byte)(v >> 8)); }

    static void Lzw(Stream s, byte[] data, int minCode)
    {
        int clear = 1 << minCode, eoi = clear + 1, codeSize = minCode + 1, next = eoi + 1;
        var dict = new Dictionary<int, int>();
        var buf = new List<byte>();
        int cur = 0, curBits = 0;
        Action<int> emit = code =>
        {
            cur |= code << curBits; curBits += codeSize;
            while (curBits >= 8) { buf.Add((byte)(cur & 255)); cur >>= 8; curBits -= 8; }
        };
        emit(clear);
        int prefix = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            int k = data[i], key = (prefix << 8) | k, v;
            if (dict.TryGetValue(key, out v)) { prefix = v; continue; }
            emit(prefix);
            if (next < 4096)
            {
                if (next >= (1 << codeSize) && codeSize < 12) codeSize++;
                dict[key] = next++;
            }
            else { emit(clear); dict.Clear(); next = eoi + 1; codeSize = minCode + 1; }
            prefix = k;
        }
        emit(prefix); emit(eoi);
        if (curBits > 0) buf.Add((byte)(cur & 255));
        s.WriteByte((byte)minCode);
        for (int i = 0; i < buf.Count; i += 255)
        {
            int n = Math.Min(255, buf.Count - i);
            s.WriteByte((byte)n);
            for (int j = 0; j < n; j++) s.WriteByte(buf[i + j]);
        }
        s.WriteByte(0);
    }

    public static string Write(string outFile, string[] files, int delayCs)
    {
        int w = 0, h = 0;
        var frames = new List<int[]>();
        foreach (var f in files) frames.Add(Pixels(f, out w, out h));

        // Count colours; keep the 256 most used and map the rare remainder to their nearest kept colour.
        var counts = new Dictionary<int, long>();
        foreach (var f in frames)
            foreach (int p in f)
            {
                int c = p & 0xFFFFFF; long n;
                counts.TryGetValue(c, out n); counts[c] = n + 1;
            }
        var ordered = new List<KeyValuePair<int, long>>(counts);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        var palette = new List<int>();
        var index = new Dictionary<int, int>();
        int total = ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            int c = ordered[i].Key;
            if (i < 256) { index[c] = palette.Count; palette.Add(c); continue; }
            int best = 0; long bestD = long.MaxValue;
            for (int j = 0; j < palette.Count; j++)
            {
                long dr = ((c >> 16) & 255) - ((palette[j] >> 16) & 255), dg = ((c >> 8) & 255) - ((palette[j] >> 8) & 255), db = (c & 255) - (palette[j] & 255);
                long d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = j; }
            }
            index[c] = best;
        }
        int bits = 1; while ((1 << bits) < palette.Count) bits++;
        int minCode = Math.Max(2, bits);

        using (var s = File.Create(outFile))
        {
            var head = System.Text.Encoding.ASCII.GetBytes("GIF89a");
            s.Write(head, 0, 6);
            W16(s, w); W16(s, h);
            s.WriteByte((byte)(0x80 | ((bits - 1) << 4) | (bits - 1))); s.WriteByte(0); s.WriteByte(0);
            for (int i = 0; i < (1 << bits); i++)
            {
                int c = i < palette.Count ? palette[i] : 0;
                s.WriteByte((byte)(c >> 16)); s.WriteByte((byte)(c >> 8)); s.WriteByte((byte)c);
            }
            var loop = new byte[] { 0x21, 0xFF, 0x0B, 78, 69, 84, 83, 67, 65, 80, 69, 50, 46, 48, 3, 1, 0, 0, 0 };   // NETSCAPE2.0, loop forever
            s.Write(loop, 0, loop.Length);

            int[] prev = null;
            foreach (var f in frames)
            {
                int x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1;
                if (prev != null)
                {
                    x0 = w; y0 = h; x1 = -1; y1 = -1;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (f[y * w + x] != prev[y * w + x])
                            {
                                if (x < x0) x0 = x; if (x > x1) x1 = x;
                                if (y < y0) y0 = y; if (y > y1) y1 = y;
                            }
                    if (x1 < 0) { x0 = y0 = x1 = y1 = 0; }
                }
                int fw = x1 - x0 + 1, fh = y1 - y0 + 1;
                var data = new byte[fw * fh];
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        data[y * fw + x] = (byte)index[f[(y0 + y) * w + x0 + x] & 0xFFFFFF];

                s.WriteByte(0x21); s.WriteByte(0xF9); s.WriteByte(4); s.WriteByte(0x04); W16(s, delayCs); s.WriteByte(0); s.WriteByte(0);
                s.WriteByte(0x2C); W16(s, x0); W16(s, y0); W16(s, fw); W16(s, fh); s.WriteByte(0);
                Lzw(s, data, minCode);
                prev = f;
            }
            s.WriteByte(0x3B);
        }
        return frames.Count + " frames, " + palette.Count + " colours kept of " + total + ", " + w + "x" + h;
    }
}
'@
$files = Get-ChildItem (Join-Path $Frames 'frame_*.png') | Sort-Object Name | ForEach-Object FullName
[TinyGif]::Write($Out, [string[]]$files, $DelayCs)
