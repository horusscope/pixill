using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Pixill
{
    // Field names are the JSON keys in config.json.
    class StocksConfig
    {
        public bool enabled = true;
        public int refreshMs = 60000;
        public double rowVh = 2.4;              // row height in vh (1vh = display height / 100); capped at maxRowVh
        public double maxRowVh = 5.0;
        public List<string> symbols = new List<string> { "SPY", "QQQ", "MSFT", "NVDA", "AAPL" };
        public Dictionary<string, string> labels = new Dictionary<string, string>();   // optional display names, e.g. "^GSPC": "S&P 500"
    }

    class WeatherConfig
    {
        public bool enabled = true;
        public int refreshMs = 600000;
        public string name = "";
        public double? latitude, longitude;
        public string temperatureUnit = "fahrenheit";   // or "celsius"
        public string precipitationUnit = "inch";       // or "mm"
        public bool animateRain = true;                 // rain amount is drawn as falling pixels (only while the widget is visible)
        public int rainFps = 10;                        // animation steps per second
        public int days = 7;                           // one row per day, today first (1-16)
        public double rowVh = 2.4;                      // row height in vh; capped at maxRowVh
        public double maxRowVh = 5.0;
        public double amountScale = 0.25;               // 0.25"/hour is roughly where "heavy rain" starts. Rain amount that fills the top half of a row; grows in whole steps if the week exceeds it
    }

    class AppConfig
    {
        public double opacity = 0.92;
        public int visibilityPollMs = 1000;     // how often a due-but-hidden widget re-checks whether it is visible
        public int minVisiblePercent = 10;      // widget counts as visible when at least this much of it is uncovered
        public string pixelFont = "fonts\\Jersey15-Regular.ttf";   // path relative to the exe; "" = Segoe UI
        public int pixelFontGrid = 15;          // the font's design height in pixels; text is drawn at whole multiples of it
        public string theme = "system";         // "system" follows Windows' dark/light mode; or force "dark" / "light"
        public bool useAccent = true;           // use the Windows accent colour (background tint, row-drag marker)
        public double accentTint = 0.08;        // how far the background and dividers lean toward the accent (0 = neutral grey)
        public int? x, y;                       // top-left corner of the window; null = top-right of the primary display
        public StocksConfig stocks = new StocksConfig();
        public WeatherConfig weather = new WeatherConfig();

        static string filePath;

        /// <summary>
        /// Where config.json lives. Normally beside the exe, so a copied folder carries its settings with it.
        /// When the exe is installed under Program Files (which ordinary users cannot write to), it is
        /// %APPDATA%\Pixill\config.json instead.
        /// </summary>
        public static string FilePath
        {
            get { return filePath ?? (filePath = ResolvePath()); }
        }

        static string ResolvePath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!IsUnder(exeDir, Environment.SpecialFolder.ProgramFiles) && !IsUnder(exeDir, Environment.SpecialFolder.ProgramFilesX86) && CanWrite(exeDir))
                return Path.Combine(exeDir, "config.json");
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixill");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "config.json");
        }

        static bool IsUnder(string path, Environment.SpecialFolder folder)
        {
            string root = Environment.GetFolderPath(folder);
            return !string.IsNullOrEmpty(root) && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".pixill-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static DateTime LastSelfWriteUtc = DateTime.MinValue;
        public static bool ReadOnly;            // snapshot mode never writes

        public static AppConfig Load(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(FilePath))
                {
                    var fresh = new AppConfig();
                    fresh.Save();
                    return fresh;
                }
                var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath));
                if (cfg == null) cfg = new AppConfig();
                if (cfg.stocks == null) cfg.stocks = new StocksConfig();
                if (cfg.weather == null) cfg.weather = new WeatherConfig();
                if (cfg.stocks.symbols == null) cfg.stocks.symbols = new List<string>();
                if (cfg.stocks.labels == null) cfg.stocks.labels = new Dictionary<string, string>();
                return cfg;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                string json = Pretty(new JavaScriptSerializer().Serialize(this));
                LastSelfWriteUtc = DateTime.UtcNow;
                File.WriteAllText(FilePath, json, new UTF8Encoding(false));
                LastSelfWriteUtc = DateTime.UtcNow;
            }
            catch { }
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    sb.Append(c);
                    for (i++; i < json.Length; i++)
                    {
                        sb.Append(json[i]);
                        if (json[i] == '\\') { i++; sb.Append(json[i]); }
                        else if (json[i] == '"') break;
                    }
                    continue;
                }
                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        ind++; NewLine(sb, ind);
                        break;
                    case '}':
                    case ']':
                        ind--; NewLine(sb, ind); sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c); NewLine(sb, ind);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, int ind)
        {
            sb.Append("\r\n").Append(' ', ind * 2);
        }
    }
}
