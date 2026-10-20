using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ClassroomHud
{
    // 유튜브 미니 플레이어용 WebView2(엣지 엔진): 필요한 DLL은 exe 안에 넣어 두었다가 처음 쓸 때 꺼낸다
    public static class Wv2
    {
        static bool ready;
        public static string Dir { get { return Path.Combine(Settings.Dir, "wv2", "1.0.3856.49"); } }
        public static string DataDir { get { return Path.Combine(Settings.Dir, "player"); } }

        static string Arch()
        {
            string a = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            if (a == "ARM64") return "arm64";
            return Environment.Is64BitProcess ? "x64" : "x86";
        }

        static void Extract(string res, string file)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(res))
            {
                if (s == null) throw new FileNotFoundException(res);
                string path = Path.Combine(Dir, file);
                if (File.Exists(path) && new FileInfo(path).Length == s.Length) return;
                using (var o = File.Create(path)) s.CopyTo(o);
            }
        }

        public static void Prepare()
        {
            if (ready) return;
            Directory.CreateDirectory(Dir);
            Extract("Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.Core.dll");
            Extract("Microsoft.Web.WebView2.WinForms.dll", "Microsoft.Web.WebView2.WinForms.dll");
            Extract("WebView2Loader." + Arch() + ".dll", "WebView2Loader.dll");
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                string n = new AssemblyName(e.Name).Name;
                if (n.StartsWith("Microsoft.Web.WebView2"))
                {
                    string p = Path.Combine(Dir, n + ".dll");
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };
            SetLoader();
            ready = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SetLoader() { Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(Dir); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string RuntimeVer() { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }

        // 이 컴퓨터에 WebView2 런타임(윈도우 10·11 기본 포함)이 있는지
        public static bool Available(out string why)
        {
            why = null;
            try
            {
                Prepare();
                string v = RuntimeVer();
                if (string.IsNullOrEmpty(v)) { why = "WebView2 런타임이 없어요."; return false; }
                return true;
            }
            catch (Exception ex) { why = ex.Message; Program.Log(ex); return false; }
        }
    }
}
