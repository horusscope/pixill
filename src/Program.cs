using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pixill
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));   // per-monitor v2: sizes are real pixels

            // --snapshot <dir>: fetch once, save each widget as a PNG, exit (used to check rendering).
            string snapshot = null;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--snapshot") snapshot = args[i + 1];
            // --frames <n>: with --snapshot, also save n consecutive animation frames (frame_000.png ...).
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--frames") int.TryParse(args[i + 1], out App.SnapshotFrames);

            // --write-icon <file.ico>: write the logo as an .ico (the build uses it as the exe icon), then exit.
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--write-icon") { AppIcon.WriteIco(args[i + 1]); return; }

            bool created;
            using (new Mutex(true, "Pixill.SingleInstance", out created))
            {
                if (!created && snapshot == null) return;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App(snapshot));
            }
        }
    }

    sealed class App : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Pixill";

        public AppConfig Cfg { get; private set; }
        public bool SessionLocked { get; private set; }

        readonly List<WidgetForm> widgets = new List<WidgetForm>();
        readonly string snapshotDir;
        readonly System.Windows.Forms.Timer reloadTimer = new System.Windows.Forms.Timer { Interval = 300 };
        NotifyIcon tray;
        FileSystemWatcher watcher;
        Control marshal;                        // gives background threads a way onto the UI thread
        IntPtr hook;
        Native.WinEventProc hookProc;           // kept in a field so the GC does not collect the callback
        int snapshotPending;

        public static int SnapshotFrames;

        public App(string snapshotDir)
        {
            this.snapshotDir = snapshotDir;
            AppConfig.ReadOnly = snapshotDir != null;

            string error;
            Cfg = AppConfig.Load(out error);
            if (Cfg == null)
            {
                Cfg = new AppConfig();
                AppConfig.ReadOnly = true;      // never overwrite a config file we could not read
                MessageBox.Show("config.json could not be read, so defaults are in use until it is fixed:\n\n" + error, "Pixill");
            }

            marshal = new Control();
            marshal.CreateControl();
            var force = marshal.Handle;

            BuildWidgets();

            if (snapshotDir != null)
            {
                var giveUp = new System.Windows.Forms.Timer { Interval = 25000 };
                giveUp.Tick += delegate { giveUp.Stop(); SaveSnapshots(); };
                giveUp.Start();
                return;
            }

            BuildTray();
            reloadTimer.Tick += delegate { reloadTimer.Stop(); ReloadConfig(); };
            watcher = new FileSystemWatcher(Path.GetDirectoryName(AppConfig.FilePath), Path.GetFileName(AppConfig.FilePath));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName;
            FileSystemEventHandler changed = delegate
            {
                if ((DateTime.UtcNow - AppConfig.LastSelfWriteUtc).TotalMilliseconds < 800) return;
                try { marshal.BeginInvoke((Action)delegate { reloadTimer.Stop(); reloadTimer.Start(); }); } catch { }
            };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Renamed += delegate (object s, RenamedEventArgs e) { changed(s, e); };
            watcher.EnableRaisingEvents = true;

            hookProc = OnForegroundChanged;
            hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0, 0);
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        }

        // Fires when the user changes dark/light mode or the accent colour in Settings.
        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            try
            {
                marshal.BeginInvoke((Action)delegate
                {
                    if (Theme.ApplySystem(Cfg)) foreach (var w in widgets) w.ThemeChanged();
                });
            }
            catch { }
        }

        void BuildWidgets()
        {
            rebuilding = true;
            foreach (var w in widgets) { w.Close(); w.Dispose(); }
            widgets.Clear();
            rebuilding = false;
            Theme.Configure(Cfg);
            Theme.ApplySystem(Cfg);
            if (Cfg.stocks.enabled || Cfg.weather.enabled) widgets.Add(new DeskWidget(this));
            snapshotPending = widgets.Count;
            foreach (var w in widgets)
            {
                if (snapshotDir != null)
                {
                    w.ForceVisible = true;
                    w.Fetched += delegate { if (--snapshotPending == 0) SaveSnapshots(); };
                }
                // If something else closes the window (an installer or Windows asking the program to shut
                // down, or Alt+F4), exit completely instead of lingering as a tray icon with no widget.
                w.FormClosed += delegate { if (!rebuilding && !quitting) Quit(); };
                // An installer posts its close request to every window of the process, including the hidden
                // owner window Windows Forms gives a taskbar-less form. Closing that owner destroys the widget
                // without a normal close, so watch for the window disappearing as well.
                var widget = w;
                w.HandleDestroyed += delegate { if (!rebuilding && !quitting && !widget.RecreatingHandle) Quit(); };
                w.Show();
            }
        }

        void SaveSnapshots()
        {
            try
            {
                Directory.CreateDirectory(snapshotDir);
                foreach (var w in widgets)
                {
                    using (var bmp = new Bitmap(w.ClientSize.Width, w.ClientSize.Height))
                    {
                        w.DrawToBitmap(bmp, new Rectangle(Point.Empty, w.ClientSize));
                        bmp.Save(Path.Combine(snapshotDir, w.GetType().Name + ".png"), ImageFormat.Png);
                        for (int i = 0; i < SnapshotFrames; i++)
                        {
                            w.StepAnimation();
                            w.DrawToBitmap(bmp, new Rectangle(Point.Empty, w.ClientSize));
                            bmp.Save(Path.Combine(snapshotDir, "frame_" + i.ToString("000") + ".png"), ImageFormat.Png);
                        }
                    }
                }
            }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(snapshotDir, "error.txt"), ex.ToString()); } catch { } }
            Quit();
        }

        // ---- config --------------------------------------------------------

        public void OpenConfig()
        {
            try { Process.Start("notepad.exe", "\"" + AppConfig.FilePath + "\""); } catch { }
        }

        void ReloadConfig()
        {
            string error;
            var fresh = AppConfig.Load(out error);
            if (fresh == null)
            {
                tray.ShowBalloonTip(4000, "Pixill", "config.json has an error, keeping the previous settings: " + error, ToolTipIcon.Warning);
                return;
            }
            AppConfig.ReadOnly = false;
            Cfg = fresh;
            BuildWidgets();
        }

        // ---- visibility triggers ------------------------------------------

        void OnForegroundChanged(IntPtr h, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (widgets.Count == 0) return;
            uint fgPid; Native.GetWindowThreadProcessId(hwnd, out fgPid);
            if (fgPid == (uint)Process.GetCurrentProcess().Id) return;   // clicking a widget or its menu changes nothing
            // "Show desktop" raises the desktop window above everything; a plain click on the wallpaper does not.
            bool showDesktop = hwnd != IntPtr.Zero && Native.IsDesktopClass(hwnd) && DesktopIsRaised(hwnd);
            foreach (var w in widgets)
            {
                w.SetShowDesktopMode(showDesktop);
                w.Kick();
            }
        }

        bool DesktopIsRaised(IntPtr desktop)
        {
            IntPtr h = Native.GetTopWindow(IntPtr.Zero);
            for (int guard = 0; h != IntPtr.Zero && guard < 5000; guard++)
            {
                if (h == desktop) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                bool mine = false;
                foreach (var w in widgets) if (w.IsHandleCreated && w.Handle == h) mine = true;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                bool topmost = (ex & 0x8) != 0;
                // Topmost windows (taskbar, overlays) always sit above the desktop; reaching one of ours or any
                // ordinary visible window first means the desktop is still underneath the normal windows.
                if (mine && !topmost) return false;
                int cloaked;
                bool hidden = Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0;
                if (!mine && !topmost && !hidden && Native.IsWindowVisible(h) && !Native.IsIconic(h)) return false;
                h = Native.GetWindow(h, 2);
            }
            return false;
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) SessionLocked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                SessionLocked = false;
                try { marshal.BeginInvoke((Action)delegate { foreach (var w in widgets) w.Kick(); }); } catch { }
            }
        }

        // ---- tray ----------------------------------------------------------

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Refresh now", null, delegate { foreach (var w in widgets) w.RefreshNow(); });
            menu.Items.Add("Edit config…", null, delegate { OpenConfig(); });
            var auto = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutoStartEnabled() };
            auto.CheckedChanged += delegate { SetAutoStart(auto.Checked); };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = MakeIcon(), Text = "Pixill", ContextMenuStrip = menu, Visible = true };
        }

        static Icon MakeIcon()
        {
            // drawn at the tray's own icon size (16px at 100% scaling), so every logo pixel is a whole pixel
            return AppIcon.ToIcon(SystemInformation.SmallIconSize.Width);
        }

        static bool AutoStartEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null;
        }

        static void SetAutoStart(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunName, false);
            }
        }

        bool rebuilding, quitting;

        public void Quit()
        {
            if (quitting) return;
            quitting = true;
            // Each step is independent: when an installer closes the program it may already have destroyed
            // some of our hidden helper windows, and one failed clean-up step must not stop the exit.
            try { if (hook != IntPtr.Zero) { Native.UnhookWinEvent(hook); hook = IntPtr.Zero; } } catch { }
            try { SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.UserPreferenceChanged -= OnPreferenceChanged; } catch { }
            try { if (watcher != null) watcher.Dispose(); } catch { }
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            try { ExitThread(); } catch { }
            // Belt and braces: if the message loop is in no state to wind down by itself, leave anyway.
            var bail = new System.Threading.Timer(delegate { Environment.Exit(0); }, null, 1500, Timeout.Infinite);
            GC.KeepAlive(bail);
        }
    }
}
