using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Pixill
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int HTCAPTION = 2;

        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000;

        public const uint EVENT_SYSTEM_FOREGROUND = 3;

        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll")] public static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, ref uint count);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsDesktopClass(IntPtr h)
        {
            string c = ClassName(h);
            return c == "WorkerW" || c == "Progman";
        }

        /// <summary>Screen rectangle of a window if it can hide what is underneath it.</summary>
        static bool CoveringRect(IntPtr h, uint myPid, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (!IsWindowVisible(h) || IsIconic(h)) return false;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == myPid) return false;
            int cloaked;
            if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TRANSPARENT) != 0) return false;           // click-through overlays
            if ((ex & WS_EX_LAYERED) != 0)
            {
                uint key, flags; byte alpha;
                if (GetLayeredWindowAttributes(h, out key, out alpha, out flags) && (flags & 2) != 0 && alpha == 0) return false;
            }
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, 16) != 0 && !GetWindowRect(h, out r)) return false;
            rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return rect.Width > 0 && rect.Height > 0;
        }

        /// <summary>Percent (0-100) of <paramref name="bounds"/> not covered by windows above <paramref name="self"/>.</summary>
        public static int VisiblePercent(IntPtr self, Rectangle bounds)
        {
            long total = (long)bounds.Width * bounds.Height;
            if (total <= 0) return 0;
            var rem = new List<Rectangle> { bounds };
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr h = GetTopWindow(IntPtr.Zero);
            for (int guard = 0; h != IntPtr.Zero && h != self && guard < 5000; guard++)
            {
                Rectangle cover;
                if (CoveringRect(h, myPid, out cover))
                {
                    var next = new List<Rectangle>();
                    foreach (var a in rem)
                    {
                        var i = Rectangle.Intersect(a, cover);
                        if (i.Width <= 0 || i.Height <= 0) { next.Add(a); continue; }
                        if (i.Top > a.Top) next.Add(Rectangle.FromLTRB(a.Left, a.Top, a.Right, i.Top));
                        if (i.Bottom < a.Bottom) next.Add(Rectangle.FromLTRB(a.Left, i.Bottom, a.Right, a.Bottom));
                        if (i.Left > a.Left) next.Add(Rectangle.FromLTRB(a.Left, i.Top, i.Left, i.Bottom));
                        if (i.Right < a.Right) next.Add(Rectangle.FromLTRB(i.Right, i.Top, a.Right, i.Bottom));
                    }
                    rem = next;
                    if (rem.Count == 0) return 0;
                }
                h = GetWindow(h, 2 /* GW_HWNDNEXT */);
            }
            long area = 0;
            foreach (var a in rem) area += (long)a.Width * a.Height;
            return (int)(area * 100 / total);
        }

        /// <summary>True if <paramref name="first"/> is above <paramref name="second"/> in z-order.</summary>
        public static bool IsAbove(IntPtr first, IntPtr second)
        {
            IntPtr h = GetTopWindow(IntPtr.Zero);
            for (int guard = 0; h != IntPtr.Zero && guard < 5000; guard++)
            {
                if (h == first) return true;
                if (h == second) return false;
                h = GetWindow(h, 2);
            }
            return false;
        }
    }
}
