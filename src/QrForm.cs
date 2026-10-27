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
            ClientSize = new Size((int)(1200 * k), (int)(720 * k));
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
:root{--ink:#1c223a;--mut:#68708c;--acc:#4c63ff;--acc2:#a05cff;--line:#e3e6f2;--bg:#f6f7ff}
html,body{margin:0;height:100%;font-family:'Malgun Gothic',sans-serif;color:var(--ink);background:var(--bg)}
.w{height:100%;display:flex;gap:22px;padding:18px 22px;box-sizing:border-box}
.l{flex:0 0 auto;width:min(30vw,56vh);display:flex;flex-direction:column;align-items:center;gap:8px;background:#fff;border-radius:22px;padding:16px;box-sizing:border-box}
.r{flex:1;min-width:0;display:flex;flex-direction:column;gap:10px}
h1{margin:0;font-size:26px;text-align:center}
p{margin:0;color:var(--mut);font-size:16px;text-align:center;line-height:1.45}
#q{width:100%;aspect-ratio:1}#q svg{width:100%;height:100%}
.u{font-size:11px;color:var(--mut);word-break:break-all;text-align:center;user-select:all}
#rf{font:inherit;font-size:16px;font-weight:700;border:0;border-radius:20px;padding:9px 20px;color:#fff;background:linear-gradient(90deg,var(--acc),var(--acc2));cursor:pointer}
.top{display:flex;align-items:baseline;gap:14px}
h2{margin:0;font-size:26px}
#cnt{color:var(--mut);font-size:18px}
#news{font-size:28px;font-weight:700;color:var(--acc);min-height:38px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
#news.no{color:#c0392b}
#g{flex:1;overflow:auto;display:grid;grid-template-columns:repeat(auto-fill,minmax(118px,1fr));gap:12px;align-content:start;padding:4px}
.c{background:#fff;border-radius:16px;padding:8px;box-shadow:0 2px 8px rgba(40,50,120,.08);display:flex;flex-direction:column;gap:6px;transition:transform .3s}
.c .ph{aspect-ratio:1;border-radius:12px;overflow:hidden;display:grid;place-items:center;font-size:40px;font-weight:700;color:rgba(0,0,0,.35)}
.c .ph img{width:100%;height:100%;object-fit:cover}
.c .nm{font-size:16px;font-weight:700;text-align:center;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.c .nm b{color:var(--acc);margin-right:4px}
.c.empty{opacity:.45}
.c.hl{outline:4px solid var(--acc);animation:pop .6s ease}
@keyframes pop{0%{transform:scale(.8)}60%{transform:scale(1.08)}100%{transform:scale(1)}}
@media (max-aspect-ratio:1/1){.w{flex-direction:column}.l{width:100%;flex-direction:row;flex-wrap:wrap;justify-content:center}#q{width:34vh}}
</style></head><body><div class='w'>
<div class='l'>
<h1>" + Esc(title) + @" 내 카드 만들기</h1>
<p>QR을 찍고 내 번호를 누른 뒤<br>사진과 닉네임을 정해 [보내기]!</p>
<div id='q'></div>
<div class='u'>" + Esc(link) + @"</div>
<button id='rf' onclick=""chrome.webview.postMessage('refresh')"">↻ 새로 받기</button>
</div>
<div class='r'>
<div class='top'><h2>우리 반 카드</h2><span id='cnt'></span></div>
<div id='news'></div>
<div id='g'></div>
</div>
</div><script>" + js + @"
qrcode.stringToBytes = qrcode.stringToBytesFuncs['UTF-8'];
var q = qrcode(0, 'M'); q.addData(" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(link) + @", 'Byte'); q.make();
document.getElementById('q').innerHTML = q.createSvgTag({cellSize: 8, margin: 2, scalable: true});
var colors=['#ffe3a3','#ffd0e0','#cdf3dc','#d3e8ff','#e6dcff','#ffe0c4','#d9f3f1','#f3e3d3'];
function setCards(list, hl){
  var g=document.getElementById('g'); g.innerHTML=''; var done=0;
  list.forEach(function(c){
    var d=document.createElement('div'); d.className='c'+(c.photo||c.nick?'':' empty')+(hl&&hl.indexOf(c.no)>=0?' hl':'');
    var ph=document.createElement('div'); ph.className='ph'; ph.style.background=colors[(c.no-1)%colors.length];
    if(c.photo){ var im=document.createElement('img'); im.src='https://ramclock.media/'+encodeURIComponent(c.photo); ph.appendChild(im); }
    else ph.textContent=(c.label||'').charAt(0)||c.no;
    var nm=document.createElement('div'); nm.className='nm'; var b=document.createElement('b'); b.textContent=c.no; nm.appendChild(b); nm.appendChild(document.createTextNode(c.label||''));
    d.appendChild(ph); d.appendChild(nm); g.appendChild(d);
    if(c.photo||c.nick) done++;
    if(hl&&hl.indexOf(c.no)>=0) setTimeout(function(){ d.scrollIntoView({block:'nearest',behavior:'smooth'}); },50);
  });
  document.getElementById('cnt').textContent=done+' / '+list.length+'명 완성';
}
function addLog(t, bad){ var n=document.getElementById('news'); n.textContent=t; n.className=bad?'no':''; }
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
                // 학생 사진 폴더를 페이지에서 볼 수 있게
                Directory.CreateDirectory(Settings.MediaDir);
                web.CoreWebView2.SetVirtualHostNameToFolderMapping("ramclock.media", Settings.MediaDir, CoreWebView2HostResourceAccessKind.Allow);
                string dir = Path.Combine(Wv2.DataDir, "page"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "qr.html"), html, System.Text.Encoding.UTF8);
                web.CoreWebView2.SetVirtualHostNameToFolderMapping("ramclock.app", dir, CoreWebView2HostResourceAccessKind.Allow);
                web.CoreWebView2.Navigate("https://ramclock.app/qr.html");
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void Run(string js) { try { web.CoreWebView2.ExecuteScriptAsync(js); } catch { } }

        public void SetCards(string json, string hlJson)
        {
            string js = "setCards(" + json + "," + (hlJson ?? "[]") + ")";
            if (ready) Run(js); else pending.Add(js);
        }

        public void AddLog(string text)
        {
            bool bad = text.Contains("받지 않았");
            string js = "addLog(" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(text) + "," + (bad ? "true" : "false") + ")";
            if (ready) Run(js); else pending.Add(js);
        }
    }
}
