using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 앱 아이콘 고르기: 1번은 exe에 들어 있는 기본 아이콘, 2~6번은 프로그램 안에 넣어 둔 그림.
    // 고르면 트레이·창 아이콘과 바탕화면·시작 메뉴 바로가기 아이콘이 함께 바뀐다.
    public static class AppIcons
    {
        public const int Count = 6;
        public static readonly string[] Names = { "알람 시계 (기본)", "겹친 시계", "동그란 시계", "핑크 네모", "그라데이션", "검정 네모" };
        static readonly Dictionary<int, Icon> cache = new Dictionary<int, Icon>();
        static int current = 1;

        public static int Current { get { return current; } }

        public static Bitmap Picture(int n)
        {
            n = Clamp(n);
            using (var st = typeof(AppIcons).Assembly.GetManifestResourceStream("icon" + n + ".png"))
                return st == null ? null : new Bitmap(st);
        }

        static int Clamp(int n) { return n < 1 || n > Count ? 1 : n; }

        // 여러 크기(16~256)를 담은 .ico 바이트
        public static byte[] IcoBytes(int n)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            using (var src = Picture(n))
                foreach (var s in sizes)
                    using (var b = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(b))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawImage(src, 0, 0, s, s);
                        }
                        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
                    }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off); off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
                w.Flush(); return ms.ToArray();
            }
        }

        public static Icon Get(int n)
        {
            n = Clamp(n);
            Icon ic;
            if (cache.TryGetValue(n, out ic)) return ic;
            try
            {
                if (n == 1) ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                else ic = new Icon(new MemoryStream(IcoBytes(n)));
            }
            catch { try { ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { ic = SystemIcons.Application; } }
            cache[n] = ic;
            return ic;
        }

        // 창마다 붙이는 아이콘
        public static Icon Now { get { return Get(current); } }

        // 바로가기에 쓸 .ico 파일 (새 경로라 윈도우 아이콘 캐시에 남은 옛 그림 대신 새 그림을 읽음)
        public static string IconLocation(int n)
        {
            n = Clamp(n);
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Program.InstallPath), "icons");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "icon" + n + "_v2.ico");
                File.WriteAllBytes(p, IcoBytes(n));
                return p + ",0";
            }
            catch (Exception ex) { Program.Log(ex); return Program.InstallPath + ",0"; }
        }

        // 고른 아이콘을 열린 창·트레이·바로가기에 적용
        public static void Apply(int n, NotifyIcon tray)
        {
            n = Clamp(n);
            bool changed = n != current;
            current = n;
            var ic = Get(n);
            try { if (tray != null) tray.Icon = ic; } catch { }
            foreach (Form f in Application.OpenForms) try { if (f.ShowIcon) f.Icon = ic; } catch { }
            if (changed || n != 1) Program.UpdateShortcutIcons(IconLocation(n));
        }

        // 새 버전으로 아이콘 그림이 바뀌었을 때 한 번: 바로가기·작업 표시줄 고정 아이콘을 다시 읽게 함
        public static void RefreshAfterUpdate(Settings S)
        {
            const int Ver = 2;
            if (S.IconVer >= Ver) return;
            S.IconVer = Ver;
            Program.UpdateShortcutIcons(IconLocation(current), true);
        }
    }
}
