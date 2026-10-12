using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;

namespace ClassroomHud
{
    // 바로가기 버튼 로고: 사이트는 파비콘(크롬 탭 아이콘), 파일·프로그램은 윈도우 아이콘
    public static class LinkIcons
    {
        public static Action Updated;
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        static readonly HashSet<string> loading = new HashSet<string>();
        static readonly object lk = new object();

        static string Dir { get { return Path.Combine(Settings.Dir, "icons"); } }

        public static string Host(string url)
        {
            try
            {
                string u = url.Trim();
                if (!u.Contains("://")) u = "https://" + u;
                return new Uri(u).Host.ToLowerInvariant();
            }
            catch { return null; }
        }

        static bool IsLocal(string url)
        {
            try { return !url.Contains("://") && (File.Exists(url) || Directory.Exists(url)); } catch { return false; }
        }

        public static Image Get(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            lock (lk) { Image im; if (cache.TryGetValue(url, out im)) return im; }

            if (IsLocal(url))
            {
                Image im = null;
                try
                {
                    if (File.Exists(url)) using (var ic = Icon.ExtractAssociatedIcon(url)) im = ic.ToBitmap();
                    else im = SystemIcons.WinLogo.ToBitmap();
                }
                catch { }
                lock (lk) cache[url] = im;
                return im;
            }

            string host = Host(url);
            if (host == null) return null;
            string file = Path.Combine(Dir, host + ".png");
            if (File.Exists(file))
            {
                Image im = null;
                try { using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read)) using (var b = new Bitmap(fs)) im = new Bitmap(b); } catch { }
                lock (lk) cache[url] = im;
                return im;
            }
            lock (lk)
            {
                if (loading.Contains(host)) return null;
                loading.Add(host);
            }
            var t = new Thread(() => Fetch(url, host, file)) { IsBackground = true };
            t.Start();
            return null;
        }

        static void Fetch(string url, string host, string file)
        {
            Image im = null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                byte[] data = null;
                foreach (var src in new[] { "https://" + host + "/apple-touch-icon.png", "https://www.google.com/s2/favicons?sz=128&domain=" + host, "https://icons.duckduckgo.com/ip3/" + host + ".ico", "https://" + host + "/favicon.ico" })
                {
                    try
                    {
                        using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 RamClock"; data = wc.DownloadData(src); }
                        using (var ms = new MemoryStream(data)) using (var b = Image.FromStream(ms)) { if (b.Width >= 32 || (b.Width >= 16 && src.Contains("favicon"))) { im = new Bitmap(b); break; } }
                    }
                    catch { }
                }
                if (im != null)
                {
                    Directory.CreateDirectory(Dir);
                    im.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
            lock (lk) { cache[url] = im; loading.Remove(host); }
            if (im != null) { var u = Updated; if (u != null) try { u(); } catch { } }
        }
    }
}
