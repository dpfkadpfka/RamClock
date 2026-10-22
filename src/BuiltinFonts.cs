using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ClassroomHud
{
    // 프로그램 안에 넣어 둔 무료 글꼴(SIL 오픈 폰트 라이선스) — 설치하지 않아도 바로 쓸 수 있다
    public static class BuiltinFonts
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string name, uint fl, IntPtr res);

        class Def { public string Name; public string[] Files; public Def(string n, params string[] f) { Name = n; Files = f; } }
        static readonly Def[] Defs = {
            new Def("★ 나눔고딕", "NanumGothic-Regular.ttf", "NanumGothic-Bold.ttf", "NanumGothic-ExtraBold.ttf"),
            new Def("★ 주아", "Jua-Regular.ttf"),
            new Def("★ 도현", "DoHyeon-Regular.ttf"),
            new Def("★ 검은고딕", "BlackHanSans-Regular.ttf"),
            new Def("★ 해바라기", "Sunflower-Medium.ttf", "Sunflower-Bold.ttf"),
            new Def("★ 개구 손글씨", "Gaegu-Bold.ttf"),
            new Def("★ 나눔손글씨 펜", "NanumPenScript-Regular.ttf"),
            new Def("★ 페이퍼로지", "Paperlogy-7Bold.ttf"),
            new Def("★ 페이퍼로지 블랙", "Paperlogy-9Black.ttf"),
            new Def("★ 카페24 써라운드", "Cafe24Ssurround.ttf"),
            new Def("★ 여기어때 잘난체", "Jalnan.ttf"),
            new Def("★ 학교안심 둥근미소", "HakgyoansimDunggeunmiso-B.ttf"),
            new Def("★ 학교안심 알림장", "HakgyoansimAllimjang-B.ttf"),
            new Def("★ 학교안심 별자리", "HakgyoansimByeoljari-B.ttf"),
        };

        public static IEnumerable<string> Names { get { foreach (var d in Defs) yield return d.Name; } }
        public static bool Is(string name) { foreach (var d in Defs) if (d.Name == name) return true; return false; }

        static readonly Dictionary<string, FontFamily> fams = new Dictionary<string, FontFamily>();
        static readonly List<PrivateFontCollection> keep = new List<PrivateFontCollection>();
        static readonly HashSet<string> gdiDone = new HashSet<string>();

        static string Dir { get { return Path.Combine(Settings.Dir, "fonts"); } }

        // 처음 쓸 때 설정 폴더에 풀어 둔다 (다음부터는 바로)
        static string FileFor(string file)
        {
            string path = Path.Combine(Dir, file);
            if (File.Exists(path)) return path;
            try
            {
                Directory.CreateDirectory(Dir);
                using (var rs = typeof(BuiltinFonts).Assembly.GetManifestResourceStream(file + ".gz"))
                {
                    if (rs == null) return null;
                    string tmp = path + ".tmp";
                    using (var gz = new GZipStream(rs, CompressionMode.Decompress))
                    using (var fs = File.Create(tmp)) gz.CopyTo(fs);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                return path;
            }
            catch { return null; }
        }

        public static FontFamily Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            FontFamily f;
            if (fams.TryGetValue(name, out f)) return f;
            Def def = null;
            foreach (var d in Defs) if (d.Name == name) def = d;
            if (def == null) return null;
            try
            {
                var pfc = new PrivateFontCollection();
                foreach (var file in def.Files)
                {
                    var p = FileFor(file);
                    if (p == null) continue;
                    pfc.AddFontFile(p);
                    // 입력칸 같은 일반 창에서도 보이도록 이 프로그램 안에서만 등록
                    if (gdiDone.Add(p)) { try { AddFontResourceEx(p, 0x10, IntPtr.Zero); } catch { } }
                }
                if (pfc.Families.Length == 0) return null;
                keep.Add(pfc);
                f = pfc.Families[0];
                if (Environment.GetEnvironmentVariable("RAM_FONT_TEST") == "1") f = new FontFamily(f.Name);   // 개발용(리눅스 확인)
                fams[name] = f;
                return f;
            }
            catch { return null; }
        }
    }
}
