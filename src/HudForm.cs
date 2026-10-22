using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClassroomHud
{
    public class HudForm : Form
    {
        Settings S, previewS;
        readonly HudState st = new HudState();
        float dpiK = 1f, sysK = 1f;
        readonly Timer tick;
        NotifyIcon tray;
        ContextMenuStrip menu;
        ToolStripMenuItem miShow;
        Composition comp;
        string lastSig = "";
        int ver;
        DateTime lastInside = DateTime.MinValue, lastTop = DateTime.MinValue, saveDue = DateTime.MaxValue;
        Btn pressed;
        bool dragging, settingsOpen, exiting, toolOpen;
        int resizeCorner = -1;
        float rsScale;
        Point rsMouse;
        Rectangle rsContent;
        // 안내 이미지(GIF 움직임)
        System.Drawing.Imaging.FrameDimension gifDim;
        int gifFrames, gifIdx;
        int[] gifDelays;
        DateTime gifNext;
        readonly HashSet<string> fired = new HashSet<string>();
        int lastSec = -1;
        string lastDayKey = "";

        public HudForm()
        {
            S = Settings.Load();
            if (S.PickResetOnStart && S.Picked.Count > 0) { S.Picked.Clear(); S.Save(); }
            // 새 버전 자동 업데이트
            Updater.CleanUp();
            Updater.Ready = v => { try { if (IsHandleCreated) BeginInvoke(new Action(() => UpdateReady(v))); } catch { } };
            Updater.Start();
            // 바로가기 로고를 미리 받아 두기
            LinkIcons.Updated = () => { try { if (IsHandleCreated) BeginInvoke(new Action(() => { ver++; Render(true); })); } catch { } };
            foreach (var l in S.Links) { try { LinkIcons.Get(l.Url); } catch { } }
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Text = "RamClock";
            Size = new Size(1, 1);
            TopMost = S.TopMostOn;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            using (var g = CreateGraphics()) sysK = Math.Max(1f, g.DpiX / 96f);
            dpiK = sysK;
            Audio.Init();
            Audio.ApplyVoice(S.VoiceName, S.VoiceRate, S.VoiceVolume);
            BuildTray();
            EnsurePosition();
            ApplyAutoStart();
            tick = new Timer { Interval = 50 };
            tick.Tick += OnTick;
        }

        // ---------- 창 스타일 ----------
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 /*LAYERED*/ | 0x00040000 /*APPWINDOW: 작업 표시줄에 표시*/ | 0x08000000 /*NOACTIVATE*/;
                cp.ExStyle &= ~0x00000080;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Render(true);
            tick.Start();
            if (!Audio.HasKoreanVoice && S.Voice)
                tray.ShowBalloonTip(5000, "RamClock", "한국어 음성이 없어 음성 안내가 어색할 수 있어요. 윈도우 설정 > 시간 및 언어 > 음성에서 한국어 음성을 추가해 주세요.", ToolTipIcon.Info);
        }

        void EnsurePosition()
        {
            bool ok = false;
            if (S.X != int.MinValue && S.Y != int.MinValue)
                foreach (var sc in Screen.AllScreens)
                    if (sc.WorkingArea.Contains(new Point(S.X, S.Y + 5))) { ok = true; break; }
            if (!ok)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                S.X = wa.Right - (int)(230 * dpiK * S.Scale);
                S.Y = wa.Top + (int)(24 * dpiK);
            }
        }

        // ---------- 그리기 ----------
        bool rendering, renderAgain;
        void Render(bool force)
        {
            // 끌고 있는 동안이나 그리는 도중에 다시 그리면 창이 제자리로 튕기며 떨리고 멈출 수 있어서 막는다
            if (dragging) return;
            if (rendering) { renderAgain = true; return; }
            rendering = true;
            try { RenderCore(force); }
            catch (Exception ex) { Program.Log(ex); }
            finally { rendering = false; }
            if (renderAgain)
            {
                renderAgain = false;
                try { BeginInvoke(new Action(() => Render(true))); } catch { }
            }
        }

        void RenderCore(bool force)
        {
            if ((!Visible || WindowState == FormWindowState.Minimized) && !force) return;
            if (WindowState == FormWindowState.Minimized) return;
            var now = DateTime.Now;
            if (previewS != null) KeepLive(previewS);
            var SS = previewS ?? S;
            string sig = ver + "|" + Painter.Signature(SS, st, now);
            if (!force && sig == lastSig) return;
            lastSig = sig;
            var work = Screen.FromPoint(new Point(S.X, S.Y)).WorkingArea;
            dpiK = DpiAt(S.X, S.Y + 5);
            var c = Painter.Compose(SS, st, now, dpiK, S.X, S.Y, work);
            if (comp != null && comp.Bmp != null) comp.Bmp.Dispose();
            comp = c;
            Push(c.Bmp, c.Win.Location);
            if (memoEd != null && !memoEd.IsDisposed) memoEd.Place(c.Content, c.MemoY);
            FollowMusic(c.Content);
        }

        void Push(Bitmap bmp, Point pos)
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBmp);
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT { x = 0, y = 0 };
                var top = new POINT { x = pos.X, y = pos.Y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memDc, ref src, 0, ref blend, 2);
            }
            catch { }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); }
                DeleteDC(memDc);
            }
        }

        // 위젯이 있는 모니터의 배율
        float DpiAt(int x, int y)
        {
            try
            {
                var m = MonitorFromPoint(new POINT { x = x, y = y }, 2);
                uint dx, dy;
                if (GetDpiForMonitor(m, 0, out dx, out dy) == 0 && dx > 0) return Math.Max(1f, dx / 96f);
            }
            catch { }
            return sysK;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { ver++; m.Result = IntPtr.Zero; return; }   // 모니터 배율 바뀜: 다음 틱에 다시 그림
            base.WndProc(ref m);
        }

        // ---------- 주기 처리 ----------
        void OnTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            if (st.TRun)
            {
                st.TRemain = (long)(st.TEnd - now).TotalMilliseconds;
                if (st.TRemain <= 0)
                {
                    st.TRemain = 0; st.TRun = false; st.TDone = true; ver++;
                    ShowBanner("시간이 끝났어요!", "", 12);
                    Audio.Announce("시간이 끝났습니다.", null, 1, true, S.Voice, 3);
                }
            }
            if (st.BannerLabel != null && now >= st.BannerUntil) { CloseBanner(); }
            if (st.BannerLabel != null && gifFrames > 1 && now >= gifNext)
            {
                gifIdx = (gifIdx + 1) % gifFrames;
                try { st.BannerImage.SelectActiveFrame(gifDim, gifIdx); } catch { }
                st.Frame = gifIdx;
                gifNext = now.AddMilliseconds(gifDelays[gifIdx]);
            }
            CheckEvents(now);
            if (now >= saveDue) { saveDue = DateTime.MaxValue; S.Save(); }
            if (dragging) return;
            if (WindowState == FormWindowState.Minimized) { if (tick.Interval != 500) tick.Interval = 500; return; }
            int want = ((S.Mode == 2 && st.WRun) || gifFrames > 1 || resizeCorner >= 0) ? 50 : (st.Hover ? 100 : 200);
            if (tick.Interval != want) tick.Interval = want;

            bool inside = Visible && comp != null && comp.Win.Contains(Control.MousePosition);
            if (inside && memoEd != null) inside = false;
            if (inside) { lastInside = now; if (!st.Hover) { st.Hover = true; ver++; } }
            else if (st.Hover && pressed == null && !settingsOpen && resizeCorner < 0 &&
                     (now - lastInside).TotalMilliseconds > (st.Popup != 0 ? 2500 : 900))
            { st.Hover = false; st.Popup = 0; ver++; }

            if (S.TopMostOn && Visible && !settingsOpen && !toolOpen && settingsForm == null && (now - lastTop).TotalSeconds > 10)
            {
                lastTop = now;
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            Render(false);
        }

        void CheckEvents(DateTime now)
        {
            int sec = now.Hour * 3600 + now.Minute * 60 + now.Second;
            string dk = now.ToString("yyyyMMdd");
            if (dk != lastDayKey) { lastDayKey = dk; fired.Clear(); lastSec = sec; return; }
            if (sec <= lastSec) { lastSec = Math.Min(lastSec, sec); return; }
            int dow = (int)now.DayOfWeek;
            if (dow >= 1 && dow <= 5)
            {
                foreach (var ev in Sched.DayEvents(S, now))
                {
                    if (ev.T > lastSec && ev.T <= sec && sec - ev.T <= 5 && !fired.Contains(ev.Id))
                    {
                        fired.Add(ev.Id);
                        ShowBanner("알림", ev.Text, 8);
                        Audio.Announce(ev.Text, ev.Sound, ev.Times, S.Sfx, S.Voice);
                        ver++;
                    }
                }
            }
            lastSec = sec;
        }

        void ShowBanner(string label, string text, int seconds, string image = null)
        {
            DisposeBannerImage();
            st.BannerLabel = label; st.BannerText = text ?? "";
            st.BannerUntil = seconds <= 0 ? DateTime.MaxValue : DateTime.Now.AddSeconds(seconds);
            var img = Media.LoadImage(image);
            if (img != null)
            {
                st.BannerImage = img;
                try
                {
                    gifDim = new System.Drawing.Imaging.FrameDimension(img.FrameDimensionsList[0]);
                    gifFrames = img.GetFrameCount(gifDim);
                    gifDelays = new int[gifFrames];
                    byte[] raw = null;
                    try { raw = img.GetPropertyItem(0x5100).Value; } catch { }
                    for (int i = 0; i < gifFrames; i++)
                    {
                        int d = raw != null && raw.Length >= (i + 1) * 4 ? BitConverter.ToInt32(raw, i * 4) * 10 : 100;
                        gifDelays[i] = d < 20 ? 100 : d;
                    }
                    gifIdx = 0; img.SelectActiveFrame(gifDim, 0);
                    gifNext = DateTime.Now.AddMilliseconds(gifDelays[0]);
                }
                catch { gifFrames = 1; }
            }
            ver++;
        }

        void CloseBanner()
        {
            st.BannerLabel = null;
            DisposeBannerImage();
            ver++;
        }

        void DisposeBannerImage()
        {
            if (st.BannerImage != null) { try { st.BannerImage.Dispose(); } catch { } }
            st.BannerImage = null; gifFrames = 0; st.Frame = 0;
        }

        // ---------- 마우스 ----------
        Btn HitBtn(Point p)
        {
            if (comp == null) return null;
            foreach (var b in comp.Btns) if (b.R.Contains(p)) return b;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (comp == null) return;
            if (e.Button == MouseButtons.Right) { menu.Show(Control.MousePosition); return; }
            if (e.Button != MouseButtons.Left) return;
            if (!S.Locked)
                for (int i = 0; i < comp.Handles.Length; i++)
                    if (comp.Handles[i].Contains(e.Location))
                    {
                        resizeCorner = i; rsScale = S.Scale; rsMouse = Control.MousePosition; rsContent = comp.Content;
                        Capture = true;
                        return;
                    }
            var b = HitBtn(e.Location);
            if (b != null) { pressed = b; return; }
            if (st.BannerLabel != null && comp.Banner.Contains(e.Location))
            {
                CloseBanner(); Audio.Stop(); Render(true); return;
            }
            if (S.Locked) return;
            var before = comp.Win.Location;
            dragging = true;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);   // 제목 표시줄처럼 끌기
            dragging = false;
            RECT r;
            if (GetWindowRect(Handle, out r))
            {
                int dx = r.Left - before.X, dy = r.Top - before.Y;
                if (dx != 0 || dy != 0) { S.X += dx; S.Y += dy; S.Save(); }
            }
            lastInside = DateTime.Now;
            ver++; Render(true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (resizeCorner >= 0) { resizeCorner = -1; Capture = false; S.Save(); lastInside = DateTime.Now; return; }
            if (pressed == null) return;
            var b = HitBtn(e.Location);
            var p = pressed; pressed = null;
            if (b != null && b.Id == p.Id) DoAction(p.Id);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (resizeCorner >= 0)
            {
                var p = Control.MousePosition;
                int sx = (resizeCorner == 1 || resizeCorner == 3) ? 1 : -1, sy = resizeCorner >= 2 ? 1 : -1;
                float W = Math.Max(1, rsContent.Width), H = Math.Max(1, rsContent.Height);
                float ratio = 1 + (sx * (p.X - rsMouse.X) / W + sy * (p.Y - rsMouse.Y) / H) / 2f;
                float ns = Math.Max(0.35f, Math.Min(5f, rsScale * ratio)), real = ns / rsScale;
                S.Scale = ns;
                S.X = sx > 0 ? (int)(rsContent.Left + W * real / 2) : (int)(rsContent.Right - W * real / 2);
                S.Y = sy > 0 ? rsContent.Top : (int)(rsContent.Bottom - H * real);
                lastInside = DateTime.Now;
                ver++; Render(true);
                return;
            }
            Cursor hc = null;
            if (comp != null && !S.Locked)
                for (int i = 0; i < comp.Handles.Length; i++)
                    if (comp.Handles[i].Contains(e.Location)) hc = (i == 0 || i == 3) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            var want = hc ?? (HitBtn(e.Location) != null ? Cursors.Hand : (S.Locked ? Cursors.Default : Cursors.SizeAll));
            if (Cursor != want) Cursor = want;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (S.Locked) return;
            Zoom(e.Delta > 0 ? 1.08f : 1 / 1.08f);
        }

        void Zoom(float f)
        {
            S.Scale = Math.Max(0.35f, Math.Min(5f, S.Scale * f));
            saveDue = DateTime.Now.AddSeconds(1);
            ver++; Render(true);
        }

        // ---------- 버튼 동작 ----------
        void DoAction(string id)
        {
            var now = DateTime.Now;
            if (id.StartsWith("tp:"))
            {
                int m; if (int.TryParse(id.Substring(3), out m)) { st.TDur = m * 60000L; st.TRemain = st.TDur; st.TRun = false; st.TDone = false; }
            }
            else if (id.StartsWith("c:")) { S.Color = id.Substring(2); st.Popup = 0; S.Save(); }
            else if (id == "memodel")
            {
                S.Memo = ""; S.MemoRuns = null; S.Save(); ver++; Render(true);
                return;
            }
            else if (id == "linkadd") { AddLinkFromWidget(); return; }
            else if (id.StartsWith("link:"))
            {
                int i; if (int.TryParse(id.Substring(5), out i) && i < S.Links.Count)
                {
                    string u = (S.Links[i].Url ?? "").Trim();
                    if (u.Length > 0 && !u.Contains("://") && !System.IO.File.Exists(u) && !System.IO.Directory.Exists(u)) u = "https://" + u;
                    // 위쪽 바로가기는 늘 쓰는 웹 브라우저로 (유튜브 뮤직 플레이어는 [음악] 버튼으로만)
                    { try { System.Diagnostics.Process.Start(u); } catch (Exception ex) { Program.Log(ex); } }
                }
                st.Hover = false; st.Popup = 0; ver++; Render(true);
                return;
            }
            else if (id.StartsWith("msg:"))
            {
                int i; if (int.TryParse(id.Substring(4), out i) && i < S.Messages.Count)
                {
                    var m = S.Messages[i];
                    ShowBanner(m.Label, m.Text, S.BannerSeconds, m.Image);
                    Audio.Announce(m.Text, m.Sound, 1, S.Sfx, S.Voice);
                }
                st.Popup = 0;
            }
            else switch (id)
            {
                case "m0": S.Mode = 0; S.Save(); break;
                case "m1": S.Mode = 1; S.Save(); break;
                case "m2": S.Mode = 2; S.Save(); break;
                case "pick": OpenPicker(); return;
                case "week": OpenWeek(); return;
                case "memo": OpenMemo(); return;
                case "music": OpenMusic(null); return;
                case "msgs": st.Popup = st.Popup == 2 ? 0 : 2; break;
                case "readable": S.Readable = !S.Readable; S.Save(); break;
                case "lock": S.Locked = !S.Locked; S.Save(); break;
                case "settings": OpenSettings(); return;
                case "hide": HideToTray(); return;
                case "quit": ExitApp(); return;
                case "max":
                    if (!st.Maximized)
                    {
                        // 화면을 꽉 채우도록 크게 (다시 누르면 원래 크기·자리로)
                        var wa = Screen.FromPoint(new Point(S.X, S.Y)).WorkingArea;
                        st.MaxPrevX = S.X; st.MaxPrevY = S.Y; st.MaxPrevScale = S.Scale;
                        float ratio = Math.Min(wa.Width * 0.86f / Math.Max(1, comp.Content.Width), wa.Height * 0.62f / Math.Max(1, comp.Content.Height));
                        S.Scale = Math.Max(0.35f, Math.Min(8f, S.Scale * ratio));
                        S.X = wa.Left + wa.Width / 2;
                        S.Y = wa.Top + (int)(wa.Height * 0.16f);
                        st.Maximized = true;
                    }
                    else
                    {
                        S.X = st.MaxPrevX; S.Y = st.MaxPrevY; S.Scale = st.MaxPrevScale; st.Maximized = false;
                    }
                    break;
                case "pin":
                    S.TopMostOn = !S.TopMostOn; S.Save();
                    TopMost = S.TopMostOn;
                    try { SetWindowPos(Handle, new IntPtr(S.TopMostOn ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { }
                    MusicTop();
                    break;
                case "cpick": PickColor(); return;
                case "tm-":
                case "tm+":
                    {
                        long d = id == "tm+" ? 60000 : -60000;
                        long nd = Math.Max(60000, Math.Min(180 * 60000L, st.TDur + d));
                        d = nd - st.TDur; st.TDur = nd;
                        if (st.TRun) st.TEnd = st.TEnd.AddMilliseconds(d);
                        else st.TRemain = Math.Max(0, (st.TDone ? 0 : st.TRemain) + d);
                        if (!st.TRun && (st.TDone || st.TRemain <= 0)) st.TRemain = st.TDur;
                        st.TDone = false;
                        break;
                    }
                case "tstart":
                    if (st.TRun) { st.TRemain = (long)(st.TEnd - now).TotalMilliseconds; st.TRun = false; }
                    else
                    {
                        if (st.TRemain <= 0) st.TRemain = st.TDur;
                        st.TEnd = now.AddMilliseconds(st.TRemain); st.TRun = true; st.TDone = false;
                        if (st.BannerLabel == "시간이 끝났어요!") st.BannerLabel = null;
                    }
                    break;
                case "treset": st.TRun = false; st.TRemain = st.TDur; st.TDone = false; break;
                case "wstart":
                    if (st.WRun) { st.WBase = st.WElapsed(now); st.WRun = false; }
                    else { st.WStart = now; st.WRun = true; }
                    break;
                case "wlap": { long el = st.WElapsed(now); if (el > 0) st.Laps.Add(el); break; }
                case "wreset": st.WRun = false; st.WBase = 0; st.Laps.Clear(); break;
            }
            ver++; Render(true);
        }

        // 창이 열려 있는 동안 시계를 '항상 위'에서 잠시 내림 (창을 가리지 않게)
        void Lower() { try { TopMost = false; SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { } }
        void Raise() { if (settingsForm != null && !settingsForm.IsDisposed) return; try { TopMost = S.TopMostOn; } catch { } }

        void OpenPicker()
        {
            if (toolOpen) return;
            toolOpen = true;
            st.Popup = 0; st.Hover = false; ver++; Render(true);
            var scr = Screen.FromPoint(new Point(S.X, S.Y)).WorkingArea;
            Lower();
            var f = new PickerForm(S, () => S.Save(), dpiK, scr);
            f.FormClosed += (o, e) => { toolOpen = false; Raise(); BeginInvoke(new Action(() => f.Dispose())); };
            f.ShowWithBackdrop();
        }

        // 위젯 바로 아래에서 메모 쓰기 (창 없이, 저장 버튼 없이)
        MemoEditor memoEd;
        void OpenMemo()
        {
            if (memoEd != null && !memoEd.IsDisposed) { memoEd.Close(); return; }
            st.MemoEdit = true; st.Hover = false; st.Popup = 0; ver++; Render(true);
            memoEd = new MemoEditor(S, Math.Max(0.2f, S.Scale) * dpiK);
            memoEd.Changed = t => { S.Memo = t; };
            memoEd.StyleRequested = () => BeginInvoke(new Action(OpenMemoStyle));
            memoEd.FormClosed += (o, e) =>
            {
                S.Memo = memoEd.MemoText; if (string.IsNullOrWhiteSpace(S.Memo)) S.MemoRuns = null; S.Save();
                memoEd = null; st.MemoEdit = false; ver++; Render(true);
            };
            memoEd.Place(comp.Content, comp.MemoY);
            memoEd.Show();
            memoEd.Activate();
        }

        void OpenMemoStyle()
        {
            if (settingsOpen) return;
            settingsOpen = true; st.Popup = 0; Lower();
            try
            {
                using (var f = new MemoForm(S))
                {
                    f.LivePreview = r => { previewS = r; previewFromMemo = true; ver++; Render(true); };
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        var m = f.M;
                        S.Memo = m.Memo; S.MemoColor = m.MemoColor; S.MemoBgOn = m.MemoBgOn; S.MemoBgColor = m.MemoBgColor; S.MemoBgOpacity = m.MemoBgOpacity;
                        S.MemoFont = m.MemoFont; S.MemoSize = m.MemoSize; S.MemoBold = m.MemoBold; S.MemoOutline = m.MemoOutline; S.MemoOutlineColor = m.MemoOutlineColor;
                        S.Save();
                    }
                }
            }
            finally { settingsOpen = false; previewS = null; Raise(); }
            ver++; Render(true);
        }

        void OpenWeek()
        {
            if (settingsOpen) return;
            settingsOpen = true; Lower();
            st.Popup = 0;
            try
            {
                var d = DateTime.Today;
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(2);
                using (var f = new WeekForm(S, () => S.Save(), d)) f.ShowDialog();
            }
            finally { settingsOpen = false; Raise(); }
            ver++; Render(true);
        }

        void PickColor()
        {
            settingsOpen = true; Lower();
            Color picked;
            if (ColorPicker.Pick(null, Painter.ParseColor(S.Color), out picked))
            {
                S.Color = "#" + picked.R.ToString("X2") + picked.G.ToString("X2") + picked.B.ToString("X2");
                S.Save();
            }
            settingsOpen = false; Raise(); st.Popup = 0; ver++; Render(true);
        }

        SettingsForm settingsForm;

        // 설정 창은 따로 떠 있는 창: 열어 둔 채로도 시계를 옮기고 크기를 바꾸고 버튼을 누를 수 있다
        void OpenSettings() { OpenSettings(null); }
        void OpenSettings(string tab)
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { if (tab != null) settingsForm.SelectTab(tab); Ui.BringUp(settingsForm); return; }
            Lower();
            SettingsForm.DpiK = dpiK;
            var f = new SettingsForm(S.Clone());
            f.StartPosition = FormStartPosition.Manual;
            f.Location = PlaceAway(f.Size);
            f.Saved = r => { var n = r.Clone(); n.Memo = S.Memo; ApplySettings(n); };   // 위젯에서 쓴 글은 그대로   // 위젯에서 쓴 글은 그대로
            f.FormClosed += (o, e) =>
            {
                if (f.DialogResult == DialogResult.OK) { f.R.Memo = S.Memo; ApplySettings(f.R); }
                settingsForm = null; previewS = null; Raise();
                Audio.ApplyVoice(S.VoiceName, S.VoiceRate, S.VoiceVolume);   // 들어보기로 바꾼 목소리 되돌리기
                st.Popup = 0; ver++; Render(true);
                BeginInvoke(new Action(() => f.Dispose()));
            };
            settingsForm = f;
            st.Popup = 0;
            f.Show();
            if (tab != null) f.SelectTab(tab);
        }

        // 시계와 겹치지 않는 곳에 창 놓기
        Point PlaceAway(Size sz)
        {
            var wa = Screen.FromPoint(new Point(S.X, S.Y)).WorkingArea;
            var hud = comp != null ? comp.Content : Rectangle.Empty;
            var cands = new[] {
                new Point(wa.X + (wa.Width - sz.Width) / 2, wa.Y + (wa.Height - sz.Height) / 2),
                new Point(wa.X + 20, wa.Y + (wa.Height - sz.Height) / 2),
                new Point(wa.Right - sz.Width - 20, wa.Y + (wa.Height - sz.Height) / 2),
                new Point(wa.X + (wa.Width - sz.Width) / 2, wa.Bottom - sz.Height - 20),
                new Point(wa.X + 20, wa.Bottom - sz.Height - 20) };
            foreach (var c in cands)
                if (!new Rectangle(c, sz).IntersectsWith(hud)) return new Point(Math.Max(wa.X, c.X), Math.Max(wa.Y, c.Y));
            return new Point(Math.Max(wa.X, cands[0].X), Math.Max(wa.Y, cands[0].Y));
        }

        // 설정 창이 열려 있는 동안 시계에서 바꾼 것(크기·위치·모드·메모)은 그대로 살리기
        bool previewFromMemo;
        void KeepLive(Settings t)
        {
            if (t == null || t == S) return;
            if (t.X != int.MinValue) { t.X = S.X; t.Y = S.Y; t.Scale = S.Scale; }
            t.Mode = S.Mode;
            t.Playlists = S.Playlists; t.MusicLast = S.MusicLast; t.MusicW = S.MusicW; t.MusicH = S.MusicH; t.MusicDX = S.MusicDX; t.MusicDY = S.MusicDY; t.MusicToolAdded = S.MusicToolAdded;
            if (t == previewS && previewFromMemo) return;
            t.Memo = S.Memo; t.MemoRuns = S.MemoRuns; t.MemoColor = S.MemoColor; t.MemoBgOn = S.MemoBgOn; t.MemoBgColor = S.MemoBgColor; t.MemoBgOpacity = S.MemoBgOpacity;
            t.MemoFont = S.MemoFont; t.MemoSize = S.MemoSize; t.MemoBold = S.MemoBold; t.MemoOutline = S.MemoOutline; t.MemoOutlineColor = S.MemoOutlineColor;
            t.Picked = S.Picked; t.PickEffect = S.PickEffect; t.PickSound = S.PickSound;
        }

        void ApplySettings(Settings n)
        {
            KeepLive(n);
            S = n;
            S.FillMissing();
            Audio.ApplyVoice(S.VoiceName, S.VoiceRate, S.VoiceVolume);
            EnsurePosition();
            S.Save();
            Raise();   // 설정 창이 열려 있으면 시계를 앞으로 올리지 않음
            ApplyAutoStart();
            lastSec = -1; lastDayKey = "";
            ver++; Render(true);
        }

        void ApplyAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    key.DeleteValue("ClassroomHUD-Yeram", false);
                    if (S.AutoStart) key.SetValue("RamClock", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("RamClock", false);
                }
            }
            catch { }
        }

        // ---------- 트레이 ----------
        void BuildTray()
        {
            menu = new ContextMenuStrip();
            miShow = new ToolStripMenuItem("숨기기", null, (s, e) => { if (Visible && WindowState != FormWindowState.Minimized) HideToTray(); else ShowFromTray(); });
            menu.Items.Add(miShow);
            menu.Items.Add(new ToolStripMenuItem("설정", null, (s, e) => { if (!Visible) ShowFromTray(); OpenSettings(); }));
            menu.Items.Add(new ToolStripMenuItem("뽑기", null, (s, e) => OpenPicker()));
            menu.Items.Add(new ToolStripMenuItem("메모", null, (s, e) => OpenMemo()));
            menu.Items.Add(new ToolStripMenuItem("이번 주 시간표", null, (s, e) => OpenWeek()));
            menu.Items.Add(new ToolStripMenuItem("위치·크기 처음으로", null, (s, e) =>
            {
                S.X = int.MinValue; S.Scale = 1f; EnsurePosition(); S.Save(); if (!Visible) ShowFromTray(); ver++; Render(true);
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("업데이트 확인 (지금 v" + Updater.Version + ")", null, (s, e) =>
            {
                tray.ShowBalloonTip(3000, "RamClock", "새 버전이 있는지 확인하고 있어요…", ToolTipIcon.None);
                Updater.CheckNow(m => { try { BeginInvoke(new Action(() => tray.ShowBalloonTip(6000, "RamClock 업데이트", m, ToolTipIcon.Info))); } catch { } });
            }));
            menu.Items.Add(new ToolStripMenuItem("종료", null, (s, e) => ExitApp()));
            tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = "RamClock",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (Visible && WindowState != FormWindowState.Minimized) HideToTray(); else ShowFromTray(); } };
        }

        void HideToTray()
        {
            st.Hover = false; st.Popup = 0;
            WindowState = FormWindowState.Minimized;
            miShow.Text = "보이기";
        }

        void ShowFromTray()
        {
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            miShow.Text = "숨기기";
            ver++; Render(true);
        }

        FormWindowState lastState = FormWindowState.Normal;
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 최소화에서 돌아올 때만 다시 그림 (창 크기가 바뀔 때마다 다시 그리면 서로 꼬리를 물며 떨림)
            if (WindowState == FormWindowState.Normal && lastState == FormWindowState.Minimized && IsHandleCreated)
            {
                if (miShow != null) miShow.Text = "숨기기";
                ver++; BeginInvoke(new Action(() => Render(true)));
            }
            lastState = WindowState;
        }

        // 유튜브는 주소창·탭 없는 작은 창(앱 모드)으로: 오른쪽 아래에 띄워 노래만 틀어 두기 좋게
        static string BrowserPath(string exe)
        {
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        var v = k != null ? k.GetValue("") as string : null;
                        if (!string.IsNullOrEmpty(v) && System.IO.File.Exists(v.Trim('"'))) return v.Trim('"');
                    }
                }
                catch { }
            }
            foreach (var p in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Microsoft\Edge\Application\msedge.exe",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft\Edge\Application\msedge.exe" })
                if (exe == "msedge.exe" && System.IO.File.Exists(p)) return p;
            return null;
        }

        bool OpenSmall(string url)
        {
            string host = LinkIcons.Host(url) ?? "";
            if (!(host.EndsWith("youtube.com") || host.EndsWith("youtu.be") || host.EndsWith("music.youtube.com"))) return false;
            string br = BrowserPath("chrome.exe") ?? BrowserPath("msedge.exe");
            if (br == null) return false;
            try
            {
                var wa = Screen.FromPoint(new Point(S.X, S.Y)).WorkingArea;
                int w = 560, h = 360;
                int x = wa.Right - w - 16, y = wa.Bottom - h - 16;
                System.Diagnostics.Process.Start(br, "--app=\"" + url + "\" --window-size=" + w + "," + h + " --window-position=" + x + "," + y);
                return true;
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        // ---------- 유튜브 미니 플레이어 (시계 위에 붙어 다님) ----------
        Form music;
        void OpenMusic(string url)
        {
            st.Hover = false; st.Popup = 0;
            if (music != null && !music.IsDisposed)
            {
                if (url != null) MusicPlay(url);
                else { music.Show(); music.BringToFront(); }
                ver++; Render(true);
                return;
            }
            string why;
            if (!Wv2.Available(out why))
            {
                // 엣지 엔진이 없으면 예전처럼 작은 브라우저 창으로
                if (!OpenSmall(url ?? MusicFallbackUrl())) try { System.Diagnostics.Process.Start(url ?? "https://music.youtube.com"); } catch { }
                tray.ShowBalloonTip(6000, "RamClock", "이 컴퓨터에는 미니 플레이어용 WebView2가 없어 브라우저 창으로 열었어요.\n" + why, ToolTipIcon.Info);
                return;
            }
            try { music = MakeMusic(url); }
            catch (Exception ex) { Program.Log(ex); if (!OpenSmall(url ?? MusicFallbackUrl())) try { System.Diagnostics.Process.Start(url ?? "https://music.youtube.com"); } catch { } return; }
            st.MusicOpen = true;
            ver++; Render(true);
        }
        static string MusicFallbackUrl() { return "https://music.youtube.com"; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        Form MakeMusic(string url)
        {
            var f = new MusicForm(() => S, () => comp != null ? comp.Content : Rectangle.Empty, dpiK);
            if (url != null) f.Play(url);
            f.FormClosed += (o, e) => { music = null; st.MusicOpen = false; ver++; Render(true); };
            f.SetTop(S.TopMostOn);
            music = f;
            st.MusicOpen = true;
            f.Show(this);
            ver++; Render(true);
            return f;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void MusicPlay(string url) { ((MusicForm)music).Play(url); music.Show(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void MusicFollow(Rectangle c) { ((MusicForm)music).Follow(c); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void MusicTopCore() { ((MusicForm)music).SetTop(S.TopMostOn); }
        void MusicTop() { if (music != null && !music.IsDisposed) try { MusicTopCore(); } catch { } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void MusicSizeCore() { }
        void FollowMusic(Rectangle c) { if (music != null && !music.IsDisposed && music.Visible) try { MusicFollow(c); } catch { } }

        // 플레이어 머리줄을 끌면 시계째로 움직이기
        void DragFromMusic()
        {
            if (S.Locked || comp == null) return;
            var before = comp.Win.Location;
            dragging = true;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            dragging = false;
            RECT r;
            if (GetWindowRect(Handle, out r))
            {
                int dx = r.Left - before.X, dy = r.Top - before.Y;
                if (dx != 0 || dy != 0) { S.X += dx; S.Y += dy; S.Save(); }
            }
            ver++; Render(true);
        }

        // 시계를 끄는 동안에도 플레이어가 같이 따라오게
        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (dragging && comp != null && music != null)
            {
                var c = comp.Content;
                c.Offset(Left - comp.Win.Left, Top - comp.Win.Top);
                FollowMusic(c);
            }
        }

        // 바로가기 줄의 [+]: 주소 넣는 작은 창
        void AddLinkFromWidget()
        {
            st.Hover = false; ver++; Render(true);
            using (var d = new LinkDialog(null))
            {
                if (d.ShowDialog() != DialogResult.OK || d.Result == null) return;
                S.Links.Add(d.Result); S.Save();
                try { LinkIcons.Get(d.Result.Url); } catch { }
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.AddLinkRow(d.Result);
            }
            ver++; Render(true);
        }

        void UpdateReady(string v)
        {
            try
            {
                tray.ShowBalloonTip(8000, "RamClock 새 버전 " + v, "새 버전을 받아 두었어요. 다음에 켤 때부터 적용돼요.\n트레이 아이콘 메뉴의 [지금 업데이트]를 누르면 바로 바뀌어요.", ToolTipIcon.Info);
                foreach (ToolStripItem it in tray.ContextMenuStrip.Items) if (it.Text != null && it.Text.StartsWith("지금 업데이트")) return;
                var mi = new ToolStripMenuItem("지금 업데이트 (v" + v + ")", null, (s, e) => { Updater.Restart(); ExitApp(); }) { Font = new Font(menu.Font, FontStyle.Bold) };
                tray.ContextMenuStrip.Items.Insert(0, mi);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void ExitApp()
        {
            exiting = true;
            S.Save();
            tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (st.Maximized) { S.X = st.MaxPrevX; S.Y = st.MaxPrevY; S.Scale = st.MaxPrevScale; st.Maximized = false; }
            // 작업 표시줄의 [창 닫기]·Alt+F4도 진짜 종료 (숨기기는 위젯의 [숨기기] 버튼)
            exiting = true;
            if (memoEd != null && !memoEd.IsDisposed) { try { S.Memo = memoEd.MemoText; S.MemoRuns = memoEd.Runs; } catch { } }
            S.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosing(e);
        }

        // ---------- Win32 ----------
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
