using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClassroomHud
{
    // 태블릿 투표·의견 모으기: 질문을 만들면 QR이 뜨고, 학생이 보내는 대로 결과가 바로 바뀐다
    public class VoteForm : Form
    {
        public const string PageUrl = "https://dpfkadpfka.github.io/RamClock/v/";
        readonly WebView2 web;
        readonly Settings S;
        readonly JavaScriptSerializer js = new JavaScriptSerializer();
        bool ready;
        readonly List<string> pending = new List<string>();

        // 지금 진행 중인 투표
        string voteId, mode; List<string> opts = new List<string>();
        long startUnix; bool closed;
        readonly Dictionary<string, object> answers = new Dictionary<string, object>();   // 기기마다 마지막 답
        volatile int gen;   // 새 투표마다 바뀜 → 예전 연결은 스스로 멈춤

        public VoteForm(Settings s, float k)
        {
            S = s;
            Text = "투표 · 의견 모으기";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size((int)(1200 * k), (int)(720 * k));
            MinimumSize = new Size((int)(500 * k), (int)(420 * k));
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
            Controls.Add(web);
            Init();
        }

        static string Res(string name)
        {
            using (var st = typeof(VoteForm).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(st)) return r.ReadToEnd();
        }

        async void Init()
        {
            try
            {
                Directory.CreateDirectory(Wv2.DataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, Wv2.DataDir, null);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.WebMessageReceived += (o, e) => { try { OnMsg(e.TryGetWebMessageAsString()); } catch (Exception ex) { Program.Log(ex); } };
                web.CoreWebView2.NavigationCompleted += (o, e) => { ready = true; foreach (var p in pending) Run(p); pending.Clear(); };
                string dir = Path.Combine(Wv2.DataDir, "page"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "vote.html"), Page(), Encoding.UTF8);
                web.CoreWebView2.SetVirtualHostNameToFolderMapping("ramclock.app", dir, CoreWebView2HostResourceAccessKind.Allow);
                web.CoreWebView2.Navigate("https://ramclock.app/vote.html");
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void Run(string script) { if (!ready) { pending.Add(script); return; } try { web.CoreWebView2.ExecuteScriptAsync(script); } catch { } }

        void OnMsg(string m)
        {
            var d = js.Deserialize<Dictionary<string, object>>(m);
            string act = d["act"] as string;
            if (act == "start")
            {
                mode = (d["mode"] as string) == "t" ? "t" : "c";
                opts = new List<string>();
                if (d.ContainsKey("opts")) foreach (var o in (System.Collections.ArrayList)d["opts"]) { var t = (o as string ?? "").Trim().Replace("|", "/"); if (t.Length > 0) opts.Add(t); }
                string q = (d["q"] as string ?? "").Trim();
                const string cs = "abcdefghjkmnpqrstuvwxyz23456789"; var rnd = new Random(); var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(cs[rnd.Next(cs.Length)]);
                voteId = sb.ToString();
                startUnix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - 5;
                answers.Clear(); closed = false;
                string link = PageUrl + "?c=" + S.ClassCode + "&id=" + voteId + "&m=" + mode + "&q=" + Uri.EscapeDataString(q) + (mode == "c" ? "&o=" + Uri.EscapeDataString(string.Join("|", opts)) : "");
                Run("showVote(" + js.Serialize(link) + "," + js.Serialize(q) + "," + js.Serialize(mode) + "," + js.Serialize(opts) + ")");
                Listen(++gen);
                Push();
            }
            else if (act == "close") { closed = true; Run("setClosed(true)"); }
            else if (act == "reopen") { closed = false; Run("setClosed(false)"); }
            else if (act == "new") { gen++; voteId = null; }
            else if (act == "sfx") { if (S.PickSound) { if ((d["k"] as string) == "hit") Audio.PlayFanfare(); else Audio.PlayDrum(); } }
        }

        // 학생 답을 실시간으로 받기 (끊기면 다시 연결, 이번 투표가 시작된 뒤 것만)
        void Listen(int myGen)
        {
            string topic = "ramclock-" + S.ClassCode + "-vote";
            new Thread(() =>
            {
                while (gen == myGen && !IsDisposed)
                {
                    try
                    {
                        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                        var req = (HttpWebRequest)WebRequest.Create(ProfileSync.Server + "/" + topic + "/json?since=" + startUnix);
                        req.UserAgent = "RamClock"; req.Timeout = 30000; req.ReadWriteTimeout = 120000;
                        using (var resp = req.GetResponse())
                        using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            string line;
                            while (gen == myGen && (line = rd.ReadLine()) != null)
                            {
                                try
                                {
                                    var m = js.Deserialize<Dictionary<string, object>>(line);
                                    if (m == null || (m["event"] as string) != "message") continue;
                                    var a = js.Deserialize<Dictionary<string, object>>(m["message"] as string ?? "{}");
                                    if ((a["id"] as string) != voteId) continue;
                                    string dev = a.ContainsKey("dev") ? a["dev"] as string : null; if (string.IsNullOrEmpty(dev)) continue;
                                    object val = mode == "t" ? (object)((a.ContainsKey("text") ? a["text"] as string : "") ?? "").Trim() : a.ContainsKey("opt") ? Convert.ToInt32(a["opt"]) : -1;
                                    BeginInvoke(new Action(() => { if (gen != myGen || closed) return; answers[dev] = val; Push(); }));
                                }
                                catch { }
                            }
                        }
                    }
                    catch { try { BeginInvoke(new Action(() => Run("setLive(false)"))); } catch { } }
                    Thread.Sleep(3000);
                }
            }) { IsBackground = true }.Start();
        }

        // 결과를 화면으로
        void Push()
        {
            if (mode == "c")
            {
                var counts = new int[opts.Count];
                foreach (var v in answers.Values) { int i = (int)v; if (i >= 0 && i < counts.Length) counts[i]++; }
                Run("setResults(" + js.Serialize(counts) + "," + answers.Count + ")");
            }
            else
            {
                var texts = new List<string>(); foreach (var v in answers.Values) { var t = v as string; if (!string.IsNullOrEmpty(t)) texts.Add(t); }
                Run("setTexts(" + js.Serialize(texts) + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { gen++; base.OnFormClosed(e); }

        string Page()
        {
            return @"<!doctype html><html><head><meta charset='utf-8'><style>
:root{--ink:#1c223a;--mut:#68708c;--acc:#4c63ff;--acc2:#a05cff;--line:#e3e6f2;--bg:#f6f7ff}
*{box-sizing:border-box}
html,body{margin:0;height:100%;font-family:'Malgun Gothic',sans-serif;color:var(--ink);background:var(--bg)}
button{font:inherit;cursor:pointer}
.pri{border:0;border-radius:24px;padding:12px 26px;color:#fff;font-weight:700;font-size:18px;background:linear-gradient(90deg,var(--acc),var(--acc2))}
.sec{border:1px solid var(--line);border-radius:24px;padding:11px 22px;background:#fff;font-weight:700;font-size:16px;color:var(--ink)}
#setup{max-width:720px;margin:0 auto;padding:30px 24px;display:flex;flex-direction:column;gap:14px}
h1{margin:0;font-size:30px}
input[type=text]{width:100%;height:52px;border-radius:14px;border:1px solid var(--line);padding:0 16px;font:inherit;font-size:20px}
.tabs{display:flex;gap:8px}.tabs button{flex:1}.tabs .on{background:linear-gradient(90deg,var(--acc),var(--acc2));color:#fff;border-color:transparent}
#opts{display:flex;flex-direction:column;gap:8px}
.ex{display:flex;gap:8px;flex-wrap:wrap}.ex button{font-size:14px;padding:6px 14px}
#live{height:100%;display:flex;gap:22px;padding:18px 22px}
.l{flex:0 0 auto;width:min(30vw,56vh);display:flex;flex-direction:column;align-items:center;gap:8px;background:#fff;border-radius:22px;padding:16px}
#qr{width:100%;aspect-ratio:1}#qr svg{width:100%;height:100%}
.r{flex:1;min-width:0;display:flex;flex-direction:column;gap:12px}
#qt{font-size:34px;font-weight:700;margin:0}
#cnt{color:var(--mut);font-size:20px}
#res{flex:1;overflow:auto;display:flex;flex-direction:column;gap:14px;padding:4px}
.bar{display:flex;align-items:center;gap:12px;font-size:26px;font-weight:700}
.bar .nm{flex:0 0 32%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.bar .tr{flex:1;height:46px;background:#e8ebf7;border-radius:23px;overflow:hidden}
.bar .fl{height:100%;border-radius:23px;background:linear-gradient(90deg,var(--acc),var(--acc2));transition:width .5s;min-width:0}
.bar .n{flex:0 0 60px;text-align:right;color:var(--acc)}
.bar.top .fl{background:linear-gradient(90deg,#ff9f1c,#ff5e7e)}
#wall{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px;align-content:start}
.note{background:#fff7c2;border-radius:14px;padding:14px 16px;font-size:22px;font-weight:700;box-shadow:0 2px 8px rgba(0,0,0,.06);animation:pop .5s ease;word-break:break-all}
.note:nth-child(3n+2){background:#d9f1ff}.note:nth-child(3n){background:#ffe0ec}
@keyframes pop{0%{transform:scale(.8);opacity:0}100%{transform:scale(1);opacity:1}}
.btns{display:flex;gap:8px;flex-wrap:wrap}
.hide .fl,.hide .n,.hide #wall{filter:blur(14px)}
#closedTag{display:none;color:#c0392b;font-weight:700;font-size:20px}
.closed #closedTag{display:inline}
.note.roll{outline:5px solid var(--acc);transform:scale(1.06)}
.note.done{opacity:.35}
#pick{position:fixed;inset:0;background:rgba(20,24,48,.55);display:flex;align-items:center;justify-content:center;z-index:9}
#pick .card{background:#fff;border-radius:28px;padding:34px 40px;width:min(80vw,900px);text-align:center;box-shadow:0 20px 60px rgba(0,0,0,.25);animation:pop .45s ease}
#pick .tag{display:inline-block;padding:6px 18px;border-radius:20px;color:#fff;font-weight:700;font-size:18px;background:linear-gradient(90deg,#ff9f1c,#ff5e7e)}
#pick .txt{font-size:clamp(30px,5vw,56px);font-weight:700;margin:22px 0 26px;word-break:keep-all;line-height:1.35}
#pick .left{color:var(--mut);font-size:16px;margin-top:12px}
[hidden]{display:none!important}
</style></head><body>
<div id='setup'>
<h1>투표 · 의견 모으기</h1>
<div class='tabs'><button class='sec on' id='tc'>골라서 투표</button><button class='sec' id='tt'>의견 쓰기</button></div>
<input type='text' id='q' maxlength='60' placeholder='질문 (예: 오늘 체육 시간에 뭐 할까?)'>
<div id='optBox'><div id='opts'></div><div class='ex' style='margin-top:8px'><button class='sec' id='add'>+ 보기 추가</button><button class='sec' data-ex='좋아요|그저 그래요|별로예요'>좋아요/그저 그래요/별로예요</button><button class='sec' data-ex='O|X'>O / X</button><button class='sec' data-ex='1번|2번|3번|4번'>1~4번</button></div></div>
<button class='pri' id='go'>QR 띄우고 시작</button>
</div>
<div id='live' hidden>
<div class='l'><div id='qr'></div><p style='margin:0;color:#68708c;text-align:center'>태블릿으로 QR을 찍어요</p>
<div class='btns'><button class='sec' id='hideBtn'>결과 가리기</button><button class='sec' id='closeBtn'>마감</button><button class='sec' id='newBtn'>새 질문</button></div><button class='pri' id='pickBtn' hidden>의견 뽑기</button></div>
<div class='r'><h2 id='qt'></h2><div><span id='cnt'>0명 참여</span> <span id='closedTag'>· 마감했어요</span></div><div id='res'></div><div id='wall' hidden></div></div>
</div>
<div id='pick' hidden><div class='card'><span class='tag'>★ 뽑힌 의견 ★</span><div class='txt' id='pickTxt'></div><div class='btns' style='justify-content:center'><button class='pri' id='againBtn'>한 번 더 뽑기</button><button class='sec' id='pickClose'>닫기</button></div><div class='left' id='pickLeft'></div></div></div>
<script>" + Res("qrcode.js") + @"
qrcode.stringToBytes = qrcode.stringToBytesFuncs['UTF-8'];
var mode='c', $=function(i){return document.getElementById(i);}, opts=[];
function optRow(v){ var i=document.createElement('input'); i.type='text'; i.maxLength=20; i.placeholder='보기'; i.value=v||''; $('opts').appendChild(i); }
function setOpts(list){ $('opts').innerHTML=''; list.forEach(optRow); }
setOpts(['','']);
$('add').onclick=function(){ if($('opts').children.length<8) optRow(''); };
document.querySelectorAll('[data-ex]').forEach(function(b){ b.onclick=function(){ setOpts(b.dataset.ex.split('|')); }; });
$('tc').onclick=function(){ mode='c'; $('tc').classList.add('on'); $('tt').classList.remove('on'); $('optBox').hidden=false; };
$('tt').onclick=function(){ mode='t'; $('tt').classList.add('on'); $('tc').classList.remove('on'); $('optBox').hidden=true; };
$('go').onclick=function(){
  var o=[].map.call($('opts').children,function(i){return i.value.trim();}).filter(function(x){return x;});
  if(!$('q').value.trim()){ $('q').focus(); return; }
  if(mode==='c' && o.length<2){ $('add').click(); $('opts').lastChild.focus(); return; }
  chrome.webview.postMessage(JSON.stringify({act:'start',mode:mode,q:$('q').value,opts:o}));
};
function showVote(link,q,m,o){
  mode=m; opts=o; $('setup').hidden=true; $('live').hidden=false; $('qt').textContent=q;
  var c=qrcode(0,'M'); c.addData(link,'Byte'); c.make(); $('qr').innerHTML=c.createSvgTag({cellSize:8,margin:2,scalable:true});
  $('res').hidden = m!=='c'; $('wall').hidden = m!=='t'; $('pickBtn').hidden = m!=='t'; picked={}; document.body.classList.remove('closed');
  if(m==='c') setResults(o.map(function(){return 0;}),0); else setTexts([]);
}
function setResults(cnt,total){
  $('cnt').textContent=total+'명 참여'; var mx=Math.max.apply(null,cnt.concat([0])); var r=$('res');
  if(r.children.length!==cnt.length){ r.innerHTML=''; opts.forEach(function(o,i){ r.insertAdjacentHTML('beforeend',""<div class='bar'><div class='nm'></div><div class='tr'><div class='fl'></div></div><div class='n'></div></div>""); r.lastChild.querySelector('.nm').textContent=(i+1)+'. '+o; }); }
  cnt.forEach(function(n,i){ var b=r.children[i]; b.querySelector('.fl').style.width=(total? n*100/Math.max(1,total):0)+'%'; b.querySelector('.n').textContent=n; b.classList.toggle('top', n>0&&n===mx); });
}
function setTexts(list){
  $('cnt').textContent=list.length+'명 참여'; var w=$('wall');
  while(w.children.length>list.length) w.removeChild(w.lastChild);
  list.forEach(function(t,i){ if(i<w.children.length){ if(w.children[i].textContent!==t) w.children[i].textContent=t; } else { var d=document.createElement('div'); d.className='note'; d.textContent=t; w.appendChild(d); } });
  [].forEach.call(w.children,function(n){ n.classList.toggle('done', !!picked[n.textContent]); });
}
// 의견 뽑기: 쪽지들 사이로 테두리가 돌다가 멈추고, 뽑힌 의견을 크게 보여 준다 (한 번 뽑힌 의견은 다음에 빼고)
var picked={}, rolling=false;
function doPick(){
  if(rolling) return; var notes=[].slice.call($('wall').children); if(!notes.length) return;
  var pool=notes.filter(function(n){ return !picked[n.textContent]; });
  if(!pool.length){ picked={}; notes.forEach(function(n){ n.classList.remove('done'); }); pool=notes; }
  rolling=true; $('pick').hidden=true; document.body.classList.remove('hide'); $('hideBtn').textContent='결과 가리기';
  chrome.webview.postMessage(JSON.stringify({act:'sfx',k:'roll'}));
  var win=pool[Math.floor(Math.random()*pool.length)], steps=14+Math.floor(Math.random()*6), i=0, cur=null;
  (function tick(){
    if(cur) cur.classList.remove('roll');
    cur = i<steps ? pool[Math.floor(Math.random()*pool.length)] : win; cur.classList.add('roll');
    if(i++<steps){ setTimeout(tick, 70+i*i*2); return; }
    setTimeout(function(){
      cur.classList.remove('roll'); picked[win.textContent]=1; win.classList.add('done'); rolling=false;
      chrome.webview.postMessage(JSON.stringify({act:'sfx',k:'hit'}));
      var left=notes.filter(function(n){ return !picked[n.textContent]; }).length;
      $('pickTxt').textContent=win.textContent; $('pickLeft').textContent= left? '아직 안 뽑힌 의견 '+left+'개' : '모든 의견을 다 뽑았어요. 다시 누르면 처음부터 뽑아요.';
      $('pick').hidden=false;
    },450);
  })();
}
$('pickBtn').onclick=doPick; $('againBtn').onclick=doPick; $('pickClose').onclick=function(){ $('pick').hidden=true; };
function setClosed(c){ document.body.classList.toggle('closed',c); $('closeBtn').textContent=c?'다시 열기':'마감'; }
function setLive(ok){}
$('hideBtn').onclick=function(){ var h=document.body.classList.toggle('hide'); $('hideBtn').textContent=h?'결과 보이기':'결과 가리기'; };
$('closeBtn').onclick=function(){ chrome.webview.postMessage(JSON.stringify({act: document.body.classList.contains('closed')?'reopen':'close'})); };
$('newBtn').onclick=function(){ chrome.webview.postMessage(JSON.stringify({act:'new'})); $('pick').hidden=true; $('live').hidden=true; $('setup').hidden=false; document.body.classList.remove('hide','closed'); };
</script></body></html>";
        }
    }
}
