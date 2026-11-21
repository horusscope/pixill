using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Pixill
{
    class SearchItem
    {
        public string Title, Subtitle;
        public object Tag;
        public override string ToString()
        {
            return string.IsNullOrEmpty(Subtitle) ? Title : Title + "  –  " + Subtitle;
        }
    }

    static class Http
    {
        static readonly HttpClient client = Create();

        static HttpClient Create()
        {
            // GZip only: Open-Meteo answers "deflate" with a zlib wrapper that .NET Framework cannot decode.
            var h = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip };
            var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Pixill/1.0");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return c;
        }

        public static async Task<object> GetJson(string url)
        {
            string s = await client.GetStringAsync(url).ConfigureAwait(false);
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(s);
        }
    }

    static class J
    {
        public static object At(object o, params object[] path)
        {
            foreach (var k in path)
            {
                if (o == null) return null;
                var s = k as string;
                if (s != null)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || !d.TryGetValue(s, out o)) return null;
                }
                else
                {
                    var a = o as object[];
                    int i = (int)k;
                    if (a == null || i < 0 || i >= a.Length) return null;
                    o = a[i];
                }
            }
            return o;
        }

        public static double Num(object o, double def = double.NaN)
        {
            if (o == null) return def;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return def; }
        }

        public static string Str(object o) { return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture); }
    }

    class Quote
    {
        public string Symbol, Name, Currency;
        public double Price, PrevClose;
        public double Change { get { return Price - PrevClose; } }
        public double ChangePct { get { return PrevClose == 0 ? 0 : (Price - PrevClose) / PrevClose * 100.0; } }
    }

    /// <summary>Yahoo Finance's public (unofficial, key-less) JSON endpoints.</summary>
    static class Yahoo
    {
        public static async Task<Quote> GetQuote(string symbol)
        {
            string url = "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) + "?interval=1d&range=1d";
            object json = await Http.GetJson(url).ConfigureAwait(false);
            object meta = J.At(json, "chart", "result", 0, "meta");
            if (meta == null) throw new Exception("No data for " + symbol);
            var q = new Quote
            {
                Symbol = symbol,
                Name = J.Str(J.At(meta, "shortName")) ?? J.Str(J.At(meta, "longName")) ?? symbol,
                Currency = J.Str(J.At(meta, "currency")),
                Price = J.Num(J.At(meta, "regularMarketPrice")),
                PrevClose = J.Num(J.At(meta, "chartPreviousClose"), J.Num(J.At(meta, "previousClose")))
            };
            if (double.IsNaN(q.Price)) throw new Exception("No price for " + symbol);
            if (double.IsNaN(q.PrevClose)) q.PrevClose = q.Price;
            return q;
        }

        public static async Task<List<SearchItem>> Search(string text)
        {
            string url = "https://query2.finance.yahoo.com/v1/finance/search?quotesCount=8&newsCount=0&q=" + Uri.EscapeDataString(text);
            object json = await Http.GetJson(url).ConfigureAwait(false);
            var list = new List<SearchItem>();
            var quotes = J.At(json, "quotes") as object[];
            if (quotes == null) return list;
            foreach (var q in quotes)
            {
                string sym = J.Str(J.At(q, "symbol"));
                if (string.IsNullOrEmpty(sym)) continue;
                string name = J.Str(J.At(q, "shortname")) ?? J.Str(J.At(q, "longname")) ?? "";
                string exch = J.Str(J.At(q, "exchDisp")) ?? "";
                list.Add(new SearchItem { Title = sym, Subtitle = (name + (exch.Length > 0 ? "  (" + exch + ")" : "")).Trim(), Tag = sym });
            }
            return list;
        }
    }

    class DayWeather
    {
        public DateTime Date;
        public double High, Low, RainChance;
        public double[] Prob = new double[24];      // chance of rain per hour, midnight to midnight
        public double[] Amount = new double[24];    // rain amount per hour
    }

    class WeatherData
    {
        public double Temp;
        public int CurrentHour;
        public List<DayWeather> Days = new List<DayWeather>();
    }

    class Place
    {
        public string Name;
        public double Lat, Lon;
    }

    /// <summary>Open-Meteo: free, key-less forecast and geocoding APIs.</summary>
    static class OpenMeteo
    {
        static string F(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        public static async Task<WeatherData> Get(double lat, double lon, string tempUnit, string precipUnit, int days)
        {
            days = Math.Max(1, Math.Min(16, days));
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + F(lat) + "&longitude=" + F(lon)
                + "&current=temperature_2m&hourly=precipitation_probability,precipitation"
                + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max"
                + "&temperature_unit=" + Uri.EscapeDataString(tempUnit) + "&precipitation_unit=" + Uri.EscapeDataString(precipUnit)
                + "&timezone=auto&forecast_days=" + days;
            object json = await Http.GetJson(url).ConfigureAwait(false);
            if (J.At(json, "current") == null) throw new Exception(J.Str(J.At(json, "reason")) ?? "No weather data");
            var d = new WeatherData { Temp = J.Num(J.At(json, "current", "temperature_2m")) };
            DateTime t;
            for (int i = 0; i < days; i++)
            {
                string date = J.Str(J.At(json, "daily", "time", i));
                if (date == null) break;
                var day = new DayWeather
                {
                    Date = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.Today.AddDays(i),
                    High = J.Num(J.At(json, "daily", "temperature_2m_max", i)),
                    Low = J.Num(J.At(json, "daily", "temperature_2m_min", i)),
                    RainChance = J.Num(J.At(json, "daily", "precipitation_probability_max", i), 0)
                };
                // Hourly values are local time and always start at midnight of the first day, 24 per day.
                for (int h = 0; h < 24; h++)
                {
                    day.Prob[h] = J.Num(J.At(json, "hourly", "precipitation_probability", i * 24 + h), 0);
                    day.Amount[h] = J.Num(J.At(json, "hourly", "precipitation", i * 24 + h), 0);
                }
                d.Days.Add(day);
            }
            string ts = J.Str(J.At(json, "current", "time"));
            d.CurrentHour = DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t.Hour : DateTime.Now.Hour;
            return d;
        }

        public static async Task<List<SearchItem>> Search(string text)
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&name=" + Uri.EscapeDataString(text);
            object json = await Http.GetJson(url).ConfigureAwait(false);
            var list = new List<SearchItem>();
            var results = J.At(json, "results") as object[];
            if (results == null) return list;
            foreach (var r in results)
            {
                string name = J.Str(J.At(r, "name"));
                if (string.IsNullOrEmpty(name)) continue;
                string admin = J.Str(J.At(r, "admin1")) ?? "";
                string cc = J.Str(J.At(r, "country_code")) ?? "";
                string sub = (admin + (admin.Length > 0 && cc.Length > 0 ? ", " : "") + cc).Trim();
                list.Add(new SearchItem
                {
                    Title = name,
                    Subtitle = sub,
                    Tag = new Place { Name = name, Lat = J.Num(J.At(r, "latitude")), Lon = J.Num(J.At(r, "longitude")) }
                });
            }
            return list;
        }
    }
}
