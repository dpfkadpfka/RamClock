using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClassroomHud
{
    // 학생 카드 꾸미기: 칠판(TV)에 큰 QR을 띄우면 학생이 태블릿으로 찍고 들어가 자기 카드를 만든다
    public class QrForm : Form
    {
        readonly WebView2 web;
        bool ready;
        public Action OnRefresh;
        readonly System.Collections.Generic.List<string> pending = new System.Collections.Generic.List<string>();

        public QrForm(string title, string link, float k)
        {
            Text = "학생 카드 꾸미기";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size((int)(560 * k), (int)(720 * k));
            MinimumSize = new Size((int)(360 * k), (int)(460 * k));
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
            Controls.Add(web);
            Init(Page(title, link));
        }

        static string Res(string name)
        {
            using (var s = typeof(QrForm).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(s)) return r.ReadToEnd();
        }

        static string Esc(string s) { return System.Net.WebUtility.HtmlEncode(s ?? ""); }

        static string Page(string title, string link)
        {
            string js = Res("qrcode.js");
            return @"<!doctype html><html><head><meta charset='utf-8'><style>
:root{--ink:#1c223a;--mut:#68708c;--acc:#4c63ff}
html,body{margin:0;height:100%;font-family:'Malgun Gothic',sans-serif;color:var(--ink);background:#fff}
.w{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:flex-start;padding:22px 20px;box-sizing:border-box;gap:10px}
h1{margin:0;font-size:30px;text-align:center}
p{margin:0;color:var(--mut);font-size:16px;text-align:center;line-height:1.5}
#q{width:min(78vw,62vh);aspect-ratio:1}#q svg{width:100%;height:100%}
.u{font-size:12px;color:var(--mut);word-break:break-all;text-align:center;max-width:94%;user-select:all}
#log{width:100%;max-height:22vh;overflow:auto;font-size:15px;border-top:1px solid #e3e6f2;padding-top:8px}
#log div{padding:3px 0}#log .no{color:#c0392b}
#rf{font:inherit;font-size:17px;font-weight:700;border:0;border-radius:22px;padding:10px 22px;color:#fff;background:linear-gradient(90deg,#4c63ff,#a05cff);cursor:pointer}
</style></head><body><div class='w'>
<h1>" + Esc(title) + @" 내 카드 만들기</h1>
<p>태블릿 카메라로 QR을 찍고, 내 번호와 이름을 넣은 뒤<br>사진과 닉네임을 정해 [보내기]를 눌러요.</p>
<div id='q'></div>
<div class='u'>" + Esc(link) + @"</div>
<button id='rf' onclick=""chrome.webview.postMessage('refresh')"">↻ 새로 받기</button>
<div id='log'><p>학생이 보내면 여기에 바로 떠요.</p></div>
</div><script>" + js + @"
qrcode.stringToBytes = qrcode.stringToBytesFuncs['UTF-8'];
var q = qrcode(0, 'M'); q.addData(" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(link) + @", 'Byte'); q.make();
document.getElementById('q').innerHTML = q.createSvgTag({cellSize: 8, margin: 2, scalable: true});
function addLog(t, bad){ var l=document.getElementById('log'); if(l.firstChild && l.firstChild.tagName==='P') l.innerHTML=''; var d=document.createElement('div'); d.textContent=t; if(bad) d.className='no'; l.insertBefore(d, l.firstChild); }
</script></body></html>";
        }

        async void Init(string html)
        {
            try
            {
                Directory.CreateDirectory(Wv2.DataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, Wv2.DataDir, null);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.WebMessageReceived += (o, e) => { try { if (e.TryGetWebMessageAsString() == "refresh" && OnRefresh != null) OnRefresh(); } catch { } };
                web.CoreWebView2.NavigationCompleted += (o, e) => { ready = true; foreach (var p in pending) Run(p); pending.Clear(); };
                web.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void Run(string js) { try { web.CoreWebView2.ExecuteScriptAsync(js); } catch { } }

        public void AddLog(string text)
        {
            bool bad = text.Contains("받지 않았");
            string js = "addLog(" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(text) + "," + (bad ? "true" : "false") + ")";
            if (ready) Run(js); else pending.Add(js);
        }
    }
}
