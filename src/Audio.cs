using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 효과음: 메모리 WAV / 글자 읽기: 윈도우 SAPI / 소리 파일 재생·녹음: 윈도우 MCI
    public static class Audio
    {
        static object voice;
        static Type vt;
        static bool inited;
        static SoundPlayer sp1, sp3, spDrum, spFan;
        public static bool HasKoreanVoice;
        static Timer pending, repeat;
        static int repeatLeft;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetShortPathName(string longPath, StringBuilder shortPath, int len);

        static int Mci(string cmd) { try { return mciSendString(cmd, null, 0, IntPtr.Zero); } catch { return -1; } }
        static string MciQ(string cmd) { var sb = new StringBuilder(256); try { mciSendString(cmd, sb, 256, IntPtr.Zero); } catch { } return sb.ToString(); }
        static string Short(string path)
        {
            try { var sb = new StringBuilder(520); if (GetShortPathName(path, sb, 520) > 0) return sb.ToString(); } catch { }
            return path;
        }

        public static void Init()
        {
            if (inited) return;
            inited = true;
            try { sp1 = new SoundPlayer(new MemoryStream(MakeWav(1))); sp1.Load(); } catch { sp1 = null; }
            try { sp3 = new SoundPlayer(new MemoryStream(MakeWav(3))); sp3.Load(); } catch { sp3 = null; }
            // 뽑기 소리: 8비트 게임 (뿅뿅 올라가다 띠링) — exe 안에 넣어 둔 소리, 없으면 예전 드럼
            try { spDrum = Res("pick_roll.wav") ?? new SoundPlayer(new MemoryStream(Wav(DrumRoll(), 22050))); spDrum.Load(); } catch { spDrum = null; }
            try { spFan = Res("pick_hit.wav") ?? new SoundPlayer(new MemoryStream(Wav(Fanfare(), 22050))); spFan.Load(); } catch { spFan = null; }
            try
            {
                vt = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (vt != null) { voice = Activator.CreateInstance(vt); SelectKorean(); }
            }
            catch { voice = null; }
        }

        // ---------- 목소리 고르기 ----------
        static System.Collections.Generic.Dictionary<string, object> tokens;
        static void LoadTokens()
        {
            tokens = new System.Collections.Generic.Dictionary<string, object>();
            if (voice == null) return;
            Action<object> add = coll =>
            {
                try
                {
                    int n = (int)coll.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, coll, null);
                    for (int i = 0; i < n; i++)
                    {
                        var t = coll.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, coll, new object[] { i });
                        string d = (string)t.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, t, new object[] { 0 });
                        if (!string.IsNullOrEmpty(d) && !tokens.ContainsKey(d)) tokens[d] = t;
                    }
                }
                catch { }
            };
            try { add(vt.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, new object[] { "", "" })); } catch { }
            // 윈도우 10·11의 추가 목소리(OneCore)도 함께
            try
            {
                var ct = Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory");
                var cat = Activator.CreateInstance(ct);
                ct.InvokeMember("SetId", BindingFlags.InvokeMethod, null, cat, new object[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices", false });
                add(ct.InvokeMember("EnumerateTokens", BindingFlags.InvokeMethod, null, cat, new object[] { "", "" }));
            }
            catch { }
        }

        public static System.Collections.Generic.List<string> VoiceNames()
        {
            Init();
            LoadTokens();   // 설정창을 열 때마다 새로 읽기 → 새로 설치한 목소리도 바로 보임
            // 실제로 소리가 나는 한국어 목소리만 (내레이터·온라인 자연 음성은 학교 컴퓨터에서 소리가 안 나서 뺌)
            var l = new System.Collections.Generic.List<string>();
            foreach (var k in tokens.Keys)
            {
                if (k.Contains("(Natural)") || k.Contains("Online") || k.Contains("Neural")) continue;
                if (k.Contains("Korean") || k.Contains("한국")) l.Add(k);
            }
            // 한국어 목소리를 앞으로
            l.Sort((a, b) => { bool ka = a.Contains("Korean") || a.Contains("한국"), kb = b.Contains("Korean") || b.Contains("한국"); return ka == kb ? string.Compare(a, b, StringComparison.Ordinal) : (ka ? -1 : 1); });
            return l;
        }

        // 들어보기: 따로 만든 목소리로 끝까지 읽어 보고, 안 되면 이유를 돌려준다 (null이면 성공)
        public static void TestVoice(string name, int rate, string text, Action<string> done)
        {
            var th = new System.Threading.Thread(() =>
            {
                string err = null;
                try
                {
                    var t2 = Type.GetTypeFromProgID("SAPI.SpVoice");
                    var v2 = Activator.CreateInstance(t2);
                    if (!string.IsNullOrEmpty(name))
                    {
                        object tok = null;
                        var coll = t2.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, v2, new object[] { "", "" });
                        int n = (int)coll.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, coll, null);
                        for (int i = 0; i < n && tok == null; i++)
                        {
                            var x = coll.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, coll, new object[] { i });
                            if ((string)x.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, x, new object[] { 0 }) == name) tok = x;
                        }
                        if (tok == null && tokens != null) tokens.TryGetValue(name, out tok);
                        if (tok == null) throw new Exception("목록에서 이 목소리를 찾지 못했어요.");
                        try { t2.InvokeMember("Voice", BindingFlags.PutRefDispProperty, null, v2, new object[] { tok }); }
                        catch { t2.InvokeMember("Voice", BindingFlags.SetProperty, null, v2, new object[] { tok }); }
                    }
                    t2.InvokeMember("Rate", BindingFlags.SetProperty, null, v2, new object[] { Math.Max(-10, Math.Min(10, rate)) });
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    t2.InvokeMember("Speak", BindingFlags.InvokeMethod, null, v2, new object[] { text, 0 });   // 끝까지 기다림
                    if (sw.ElapsedMilliseconds < 300) err = "소리를 만들지 못하고 바로 끝났어요. (온라인 목소리라면 인터넷·학교망 차단, 내레이터 목소리라면 윈도우 11 최신 음성과 호환이 안 되는 경우예요)";
                }
                catch (Exception ex)
                {
                    var e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    err = e.Message + (e is COMException ? " (코드 0x" + ((COMException)e).ErrorCode.ToString("X8") + ")" : "");
                    Program.Log(new Exception("목소리 시험 실패: " + name, e));
                }
                if (done != null) done(err);
            });
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        // 목소리·빠르기·크기 적용 (name이 비면 한국어 기본 목소리)
        public static string CurrentName = "";
        public static void ApplyVoice(string name, int rate, int volume)
        {
            CurrentName = name ?? "";
            Init();
            if (voice == null) return;
            try
            {
                if (tokens == null) LoadTokens();
                object t;
                if (!string.IsNullOrEmpty(name) && tokens.TryGetValue(name, out t))
                {
                    try { vt.InvokeMember("Voice", BindingFlags.PutRefDispProperty, null, voice, new object[] { t }); }
                    catch { vt.InvokeMember("Voice", BindingFlags.SetProperty, null, voice, new object[] { t }); }
                }
                else SelectKorean();
                vt.InvokeMember("Rate", BindingFlags.SetProperty, null, voice, new object[] { Math.Max(-10, Math.Min(10, rate)) });
                vt.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, new object[] { Math.Max(0, Math.Min(100, volume)) });
            }
            catch (Exception ex) { Program.Log(new Exception("목소리 바꾸기 실패: " + name, ex)); }
        }

        static void SelectKorean()
        {
            try
            {
                object voices = vt.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, new object[] { "Language=412", "" });
                int count = (int)voices.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, voices, null);
                if (count > 0)
                {
                    object v = voices.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, voices, new object[] { 0 });
                    try { vt.InvokeMember("Voice", BindingFlags.PutRefDispProperty, null, voice, new object[] { v }); }
                    catch { vt.InvokeMember("Voice", BindingFlags.SetProperty, null, voice, new object[] { v }); }
                    HasKoreanVoice = true;
                }
            }
            catch { }
        }

        public static void Speak(string text, int times)
        {
            if (voice == null || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                int n = Math.Max(1, Math.Min(3, times));
                for (int i = 0; i < n; i++)
                    vt.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, i == 0 ? 3 : 1 });
            }
            catch (Exception ex)
            {
                // 고른 목소리가 소리를 못 내면 기록을 남기고 기본 한국어 목소리로라도 읽기
                Program.Log(new Exception("목소리 재생 실패: " + CurrentName, ex));
                try { SelectKorean(); vt.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, 3 }); } catch { }
            }
        }

        // ---------- 소리 파일 ----------
        public static bool PlayFile(string path, int times)
        {
            StopFile();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            string p = Short(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string type = ext == ".wav" ? "waveaudio" : "mpegvideo";
            if (Mci("open \"" + p + "\" type " + type + " alias hudsnd") != 0 && Mci("open \"" + p + "\" alias hudsnd") != 0) return false;
            Mci("set hudsnd time format milliseconds");
            int len; int.TryParse(MciQ("status hudsnd length").Trim(), out len);
            if (Mci("play hudsnd from 0") != 0) { Mci("close hudsnd"); return false; }
            repeatLeft = Math.Max(1, Math.Min(3, times)) - 1;
            if (repeatLeft > 0 && len > 0)
            {
                repeat = new Timer { Interval = len + 600 };
                repeat.Tick += (s, e) =>
                {
                    if (repeatLeft-- <= 0) { repeat.Stop(); return; }
                    Mci("play hudsnd from 0");
                    if (repeatLeft <= 0) repeat.Stop();
                };
                repeat.Start();
            }
            return true;
        }

        public static void StopFile()
        {
            if (repeat != null) { repeat.Stop(); repeat.Dispose(); repeat = null; }
            Mci("close hudsnd");
        }

        public static void Stop()
        {
            if (pending != null) { pending.Stop(); pending.Dispose(); pending = null; }
            StopFile();
            try { if (voice != null) vt.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { "", 3 }); } catch { }
        }

        public static void Chime(int n)
        {
            try { var p = n > 1 ? sp3 : sp1; if (p != null) p.Play(); } catch { }
        }

        // 효과음 → (소리 파일이 있으면 파일, 없으면 글자 읽기)
        static DateTime lastAnn = DateTime.MinValue;
        public static bool Busy { get { return (DateTime.Now - lastAnn).TotalSeconds < 40; } }   // 방금 안내가 나갔으면 업데이트로 다시 켜지 않음

        public static void Announce(string text, string soundName, int times, bool sfx, bool voiceOn, int chimes = 1)
        {
            lastAnn = DateTime.Now;
            Stop();
            if (sfx) Chime(chimes);
            if (!voiceOn) return;
            string file = Settings.MediaPath(soundName);
            int delay = sfx ? (chimes > 1 ? 1900 : 900) : 20;
            pending = new Timer { Interval = delay };
            pending.Tick += (s, e) =>
            {
                pending.Stop(); pending.Dispose(); pending = null;
                if (file == null || !PlayFile(file, times)) Speak(text, times);
            };
            pending.Start();
        }

        // ---------- 녹음 ----------
        public static bool RecStart()
        {
            RecCancel();
            if (Mci("open new type waveaudio alias hudrec") != 0) return false;
            Mci("set hudrec bitspersample 16 channels 1 samplespersec 22050 bytespersec 44100 alignment 2");
            if (Mci("record hudrec") != 0) { RecCancel(); return false; }
            return true;
        }

        public static bool RecStopSave(string path)
        {
            Mci("stop hudrec");
            string dir = Path.GetDirectoryName(path);
            string target = Path.Combine(Short(dir), Path.GetFileName(path));
            int r = Mci("save hudrec \"" + target + "\"");
            Mci("close hudrec");
            return r == 0 && File.Exists(path);
        }

        public static void RecCancel() { Mci("close hudrec"); }

        static SoundPlayer Res(string name)
        {
            var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (st == null) return null;
            var ms = new MemoryStream(); st.CopyTo(ms); st.Dispose(); ms.Position = 0;
            return new SoundPlayer(ms);
        }

        public static void PlayDrum() { try { if (spDrum != null) spDrum.Play(); } catch { } }
        public static void PlayFanfare() { try { if (spFan != null) spFan.Play(); } catch { } }
        public const int DrumMs = 2600;

        // 두두두두: 점점 빨라지는 작은북 + 마지막 큰 북
        static double[] DrumRoll()
        {
            int rate = 22050; double dur = DrumMs / 1000.0 + 0.5;
            var smp = new double[(int)(rate * dur)];
            var rnd = new Random(7);
            double t = 0, interval = 0.11;
            while (t < DrumMs / 1000.0 - 0.15)
            {
                Hit(smp, rate, t, 0.09, 0.32, 180, rnd);
                t += interval; interval = Math.Max(0.045, interval * 0.965);
            }
            Hit(smp, rate, DrumMs / 1000.0 - 0.05, 0.45, 0.65, 90, rnd);
            return smp;
        }
        static void Hit(double[] smp, int rate, double start, double len, double amp, double tone, Random rnd)
        {
            int a = (int)(start * rate), b = Math.Min(smp.Length, a + (int)(len * rate));
            for (int i = a; i < b; i++)
            {
                double t = (i - a) / (double)rate;
                double env = Math.Exp(-t * (len > 0.2 ? 9 : 38));
                smp[i] += amp * env * (0.55 * (rnd.NextDouble() * 2 - 1) + 0.45 * Math.Sin(2 * Math.PI * tone * t));
            }
        }

        // 빰빠바밤
        static double[] Fanfare()
        {
            int rate = 22050;
            var smp = new double[(int)(rate * 2.2)];
            double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
            double[] starts = { 0, 0.12, 0.24, 0.36 };
            for (int i = 0; i < 4; i++) Brass(smp, rate, notes[i], starts[i], i == 3 ? 1.6 : 0.16, 0.16);
            Brass(smp, rate, 659.25, 0.36, 1.6, 0.10);
            Brass(smp, rate, 783.99, 0.36, 1.6, 0.10);
            return smp;
        }
        static void Brass(double[] smp, int rate, double f, double start, double len, double amp)
        {
            int a = (int)(start * rate), b = Math.Min(smp.Length, a + (int)(len * rate));
            for (int i = a; i < b; i++)
            {
                double t = (i - a) / (double)rate;
                double env = Math.Min(1, t / 0.02) * (t > len - 0.25 ? Math.Max(0, (len - t) / 0.25) : 1);
                double v = 0;
                for (int hm = 1; hm <= 5; hm++) v += Math.Sin(2 * Math.PI * f * hm * t) / hm;
                smp[i] += amp * env * v * (1 + 0.04 * Math.Sin(2 * Math.PI * 5.5 * t));
            }
        }

        static byte[] Wav(double[] smp, int rate)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int dataLen = smp.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            foreach (var x in smp) w.Write((short)(Math.Max(-1, Math.Min(1, x)) * 32000));
            w.Flush();
            return ms.ToArray();
        }

        // ---------- 효과음 만들기 ----------
        static byte[] MakeWav(int dings)
        {
            int rate = 22050;
            double dur = 0.6 * dings + 0.6;
            int n = (int)(rate * dur);
            var smp = new double[n];
            for (int d = 0; d < dings; d++)
            {
                double off = d * 0.6;
                Tone(smp, rate, 880, off, 0.9);
                Tone(smp, rate, 660, off + 0.22, 0.9);
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int dataLen = n * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(-1, Math.Min(1, smp[i]));
                w.Write((short)(v * 32000));
            }
            w.Flush();
            return ms.ToArray();
        }

        // ---------- 쉬는 시간 알림 소리 (여러 가지 중 고르기) ----------
        // 학교 종(미 도 레 솔 · 솔 레 미 도)을 여러 가지 소리로
        public static readonly string[][] BreakSounds = {
            new[] { "bell", "학교 종 (기본)" }, new[] { "bell_short", "학교 종 짧게 (딩동댕동)" }, new[] { "bell_big", "학교 종 큰 종 (낮고 웅장하게)" },
            new[] { "bell_xylo", "학교 종 실로폰" }, new[] { "bell_box", "학교 종 오르골" }, new[] { "bell_piano", "학교 종 피아노" },
            new[] { "bell_chime", "학교 종 맑은 차임" }, new[] { "bell_8bit", "학교 종 8비트 게임" }, new[] { "bell_hand", "옛날 손종 (땡땡땡)" } };
        static readonly System.Collections.Generic.Dictionary<string, SoundPlayer> breakCache = new System.Collections.Generic.Dictionary<string, SoundPlayer>();

        // id: 위 목록의 이름, 또는 "file:미디어 파일 이름"
        public static void PlayBreak(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) id = "bell";
                if (id.StartsWith("file:"))
                {
                    string f = Settings.MediaPath(id.Substring(5));
                    if (f != null && PlayFile(f, 1)) return;
                    id = "bell";
                }
                SoundPlayer p;
                if (!breakCache.TryGetValue(id, out p))
                {
                    p = new SoundPlayer(new MemoryStream(Wav(BreakWave(id), 22050))); p.Load();
                    breakCache[id] = p;
                }
                StopFile(); p.Play();
            }
            catch { }
        }

        public static byte[] BreakWav(string id) { return Wav(BreakWave(id), 22050); }

        static double Hz(int midi) { return 440.0 * Math.Pow(2, (midi - 69) / 12.0); }

        // timbre: bell 종 · mar 실로폰 · box 오르골 · sq 8비트 · sine 차임
        static void Note(double[] smp, int rate, double f, double start, double len, string timbre, double vol)
        {
            int a = (int)(start * rate), b = Math.Min(smp.Length, (int)((start + len) * rate));
            for (int i = Math.Max(0, a); i < b; i++)
            {
                double t = (i - a) / (double)rate, x;
                double att = t < 0.005 ? t / 0.005 : 1.0;
                switch (timbre)
                {
                    case "bell":
                        x = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 1.6) + 0.45 * Math.Sin(2 * Math.PI * f * 2.76 * t) * Math.Exp(-t * 3.5)
                          + 0.25 * Math.Sin(2 * Math.PI * f * 5.4 * t) * Math.Exp(-t * 6) + 0.2 * Math.Sin(2 * Math.PI * f * 0.5 * t) * Math.Exp(-t * 1.2);
                        break;
                    case "mar":
                        x = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 5) + 0.35 * Math.Sin(2 * Math.PI * f * 4 * t) * Math.Exp(-t * 14);
                        break;
                    case "box":
                        x = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 2.8) + 0.3 * Math.Sin(2 * Math.PI * f * 3 * t) * Math.Exp(-t * 7);
                        break;
                    case "sq":
                        x = (Math.Sin(2 * Math.PI * f * t) >= 0 ? 0.55 : -0.55) * (t < len - 0.02 ? 1 : Math.Max(0, (len - t) / 0.02));
                        break;
                    default:
                        x = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 1.3) + 0.15 * Math.Sin(2 * Math.PI * f * 2 * t) * Math.Exp(-t * 3);
                        break;
                }
                smp[i] += vol * att * x;
            }
        }

        // 학교 종 멜로디를 정해진 소리로 (count: 4 또는 8음, gap: 음 사이 초, shift: 반음 올리기/내리기)
        static double[] SchoolBell(int rate, int[] tune, int count, double gap, int shift, string kind, double lastLen)
        {
            double half = count > 4 ? gap * 0.6 : 0;   // 4음 다음 잠깐 쉬기
            var smp = new double[(int)(rate * (gap * count + half + lastLen + 0.6))];
            for (int i = 0; i < count; i++)
            {
                double st = i * gap + (i >= 4 ? half : 0), len = i == count - 1 ? lastLen : (kind == "sq" ? gap * 0.85 : Math.Max(gap * 2.5, 0.9));
                int m = tune[i] + shift;
                if (kind == "rhodes" || kind == "pluck" || kind == "bass" || kind == "saw") Syn(smp, rate, m, st, len, kind, 0.3);
                else Note(smp, rate, Hz(m), st, len, kind, kind == "sq" ? 0.18 : 0.22);
            }
            return smp;
        }

        static double[] BreakWave(string id)
        {
            int rate = 22050;
            double[] smp;
            int[] tune = { 76, 72, 74, 67, 67, 74, 76, 72 };   // 미 도 레 솔 · 솔 레 미 도
            switch (id)
            {
                case "bell_short": smp = SchoolBell(rate, tune, 4, 0.5, 0, "bell", 1.8); break;
                case "bell_big": smp = SchoolBell(rate, tune, 8, 0.62, -12, "bell", 3.0); break;
                case "bell_xylo": smp = SchoolBell(rate, tune, 8, 0.34, 0, "mar", 1.0); break;
                case "bell_box": smp = SchoolBell(rate, tune, 8, 0.36, 12, "box", 1.4); break;
                case "bell_piano": smp = SchoolBell(rate, tune, 8, 0.42, 0, "rhodes", 1.6); break;
                case "bell_chime": smp = SchoolBell(rate, tune, 8, 0.48, 12, "sine", 2.2); break;
                case "bell_8bit": smp = SchoolBell(rate, tune, 8, 0.24, 0, "sq", 0.22); break;
                case "bell_hand":   // 손으로 흔드는 옛날 종: 같은 높이로 땡땡땡
                    smp = new double[(int)(rate * 3.2)];
                    for (int i = 0; i < 8; i++) { double st = i * 0.28 + (i >= 4 ? 0.3 : 0); Note(smp, rate, Hz(84) * (1 + 0.004 * (i % 2)), st, 1.4, "bell", 0.25); }
                    break;
                default: smp = SchoolBell(rate, tune, 8, 0.42, 0, "bell", 1.6); break;   // 학교 종 (기본)
            }
            // 소리마다 크기를 비슷하게 맞추기 (8비트는 원래 귀에 크게 들려서 조금 작게)
            double peak = 0; foreach (var v in smp) peak = Math.Max(peak, Math.Abs(v));
            double target = id.Contains("8bit") ? 0.45 : 0.8;
            if (peak > 0) for (int i = 0; i < smp.Length; i++) smp[i] *= target / peak;
            return smp;
        }

        // ---------- 요즘 느낌 비트 (직접 지은 짧은 멜로디) ----------
        static readonly Random rnd = new Random(7);
        static void Kick(double[] s, int r, double st, double v)
        {
            int a = (int)(st * r); double ph = 0;
            for (int i = 0; i < (int)(0.32 * r) && a + i < s.Length; i++) { double t = i / (double)r; double f = 45 + 110 * Math.Exp(-t * 28); ph += 2 * Math.PI * f / r; s[a + i] += v * Math.Sin(ph) * Math.Exp(-t * 9); }
        }
        static void Clap(double[] s, int r, double st, double v)
        {
            int a = (int)(st * r); double lp = 0;
            for (int i = 0; i < (int)(0.22 * r) && a + i < s.Length; i++) { double t = i / (double)r; double n = rnd.NextDouble() * 2 - 1; lp += 0.45 * (n - lp); double hp = lp; double env = (t < 0.012 ? 0.7 : 1) * Math.Exp(-t * 18); s[a + i] += v * hp * env; }
        }
        static void Hat(double[] s, int r, double st, double v)
        {
            int a = (int)(st * r); double prev = 0;
            for (int i = 0; i < (int)(0.05 * r) && a + i < s.Length; i++) { double t = i / (double)r; double n = rnd.NextDouble() * 2 - 1; s[a + i] += v * (n - prev) * Math.Exp(-t * 70); prev = n; }
        }
        // 신스 소리: pluck(통통 튀는 신스) · saw(톱니 신스) · rhodes(전자 피아노) · bass · mar(마림바)
        static void Syn(double[] s, int r, int midi, double st, double len, string kind, double v)
        {
            double f = Hz(midi); int a = (int)(st * r), n = (int)((len + 0.25) * r);
            for (int i = 0; i < n && a + i < s.Length; i++)
            {
                double t = i / (double)r, x = 0, env;
                double rel = t < len ? 1 : Math.Exp(-(t - len) * 25);
                switch (kind)
                {
                    case "pluck":
                        env = Math.Exp(-t * 7) * rel;
                        for (int h = 1; h <= 6; h++) x += Math.Sin(2 * Math.PI * f * h * t) / h * Math.Exp(-t * h * 2.5);
                        x *= 0.55; break;
                    case "saw":
                        env = Math.Min(1, t / 0.01) * (0.6 + 0.4 * Math.Exp(-t * 6)) * rel;
                        for (int h = 1; h <= 8; h++) x += Math.Sin(2 * Math.PI * f * h * t * (1 + 0.003 * (h % 2))) / h;
                        x *= 0.4; break;
                    case "rhodes":
                        env = Math.Min(1, t / 0.006) * Math.Exp(-t * 1.6) * rel;
                        x = Math.Sin(2 * Math.PI * f * t + 0.8 * Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 4)) * (1 + 0.15 * Math.Sin(2 * Math.PI * 5 * t));
                        break;
                    case "bass":
                        env = Math.Min(1, t / 0.005) * (0.5 + 0.5 * Math.Exp(-t * 5)) * rel;
                        x = Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(2 * Math.PI * f * 2 * t);
                        break;
                    default:
                        env = Math.Min(1, t / 0.003) * rel;
                        x = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 6) + 0.35 * Math.Sin(2 * Math.PI * f * 4 * t) * Math.Exp(-t * 16);
                        break;
                }
                s[a + i] += v * env * x;
            }
        }
        // 멜로디 적기: "음높이:길이(8분음표 개수)", 쉼표는 r
        static void Line(double[] s, int r, string notes, double st, double eighth, string kind, double v)
        {
            double t = st;
            foreach (var tok in notes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = tok.Split(':'); double d = p.Length > 1 ? double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                if (p[0] != "r") Syn(s, r, int.Parse(p[0]), t, eighth * d * 0.9, kind, v);
                t += eighth * d;
            }
        }

        static double[] Kpop(int r)   // 120bpm 2마디: 킥·클랩·하이햇 + 통통 신스 훅
        {
            double e = 0.25; var s = new double[(int)(r * (16 * e + 0.8))];
            for (int b = 0; b < 8; b++) { if (b % 2 == 0) Kick(s, r, b * 2 * e, 0.9); else Clap(s, r, b * 2 * e, 0.22); }
            Kick(s, r, 7 * e, 0.6); Kick(s, r, 11 * e, 0.6);
            for (int i = 0; i < 16; i++) Hat(s, r, i * e + e / 2, 0.08);
            Line(s, r, "60:2 r:1 60:1 63:2 58:2 56:2 r:1 56:1 58:2 63:2", 0, e, "bass", 0.35);
            Line(s, r, "72 75 r 77 79:2 77 75 72 r 70 72 75:2 82:2", 0, e, "pluck", 0.35);
            return s;
        }
        static double[] Lofi(int r)   // 82bpm: 전자 피아노 화음 + 부드러운 비트 + 레코드 잡음
        {
            double e = 60.0 / 82 / 2; var s = new double[(int)(r * (16 * e + 1.2))];
            int[][] ch = { new[] { 53, 57, 60, 64 }, new[] { 52, 55, 59, 62 }, new[] { 50, 53, 57, 60 }, new[] { 48, 52, 55, 59 } };
            for (int c = 0; c < 4; c++) foreach (var m in ch[c]) Syn(s, r, m, c * 4 * e + 0.012 * (m % 3), 4 * e, "rhodes", 0.11);
            Line(s, r, "r:2 76 74:1 72:2 r:2 71 72 74:2 r 79:3", 0, e, "rhodes", 0.16);
            for (int b = 0; b < 4; b++) { Kick(s, r, b * 4 * e, 0.55); Kick(s, r, b * 4 * e + 2.5 * e, 0.35); Clap(s, r, b * 4 * e + 2 * e, 0.1); }
            for (int i = 0; i < 16; i++) Hat(s, r, i * e, 0.04);
            for (int i = 0; i < s.Length; i += r / 9 + rnd.Next(r / 12)) if (i < s.Length) s[i] += 0.12 * (rnd.NextDouble() - 0.5);
            return s;
        }
        static double[] Bounce(int r)   // 116bpm: 엇박으로 통통 튀는 마림바 + 가벼운 비트
        {
            double e = 60.0 / 116 / 2; var s = new double[(int)(r * (16 * e + 0.9))];
            Line(s, r, "79:1.5 74:0.5 r:1 79 83:1.5 81:0.5 79:1 r:1 76:1.5 74:0.5 r:1 71 74:1.5 79:2.5", 0, e, "mar", 0.4);
            Line(s, r, "43:2 50:2 43:2 50:2 40:2 47:2 43:4", 0, e, "mar", 0.25);
            for (int b = 0; b < 4; b++) { Kick(s, r, b * 4 * e, 0.5); Clap(s, r, b * 4 * e + 2 * e, 0.12); }
            for (int i = 0; i < 16; i += 2) Hat(s, r, i * e + e, 0.06);
            return s;
        }
        static double[] Synth(int r)   // 118bpm: 16분음표 아르페지오 + 베이스 (라-파-도-솔)
        {
            double sx = 60.0 / 118 / 4; var s = new double[(int)(r * (32 * sx + 1.0))];
            int[][] ar = { new[] { 69, 72, 76, 81 }, new[] { 65, 69, 72, 77 }, new[] { 67, 72, 76, 79 }, new[] { 67, 71, 74, 79 } };
            int[] bass = { 45, 41, 48, 43 };
            for (int c = 0; c < 4; c++)
            {
                for (int k = 0; k < 8; k++) Syn(s, r, ar[c][k % 4] + (k >= 4 ? 12 : 0), (c * 8 + k) * sx, sx * 0.8, "saw", 0.12);
                Syn(s, r, bass[c], c * 8 * sx, 8 * sx * 0.95, "bass", 0.35);
                Kick(s, r, c * 8 * sx, 0.6); Clap(s, r, (c * 8 + 4) * sx, 0.14);
            }
            Syn(s, r, 81, 32 * sx, 0.6, "pluck", 0.35);
            return s;
        }

        static void Tone(double[] smp, int rate, double f, double start, double len)
        {
            int a = (int)(start * rate), b = Math.Min(smp.Length, (int)((start + len) * rate));
            for (int i = Math.Max(0, a); i < b; i++)
            {
                double t = (i - a) / (double)rate;
                double env = (t < 0.01 ? t / 0.01 : 1.0) * Math.Exp(-t * 4.5);
                smp[i] += 0.30 * env * Math.Sin(2 * Math.PI * f * t);
            }
        }
    }
}
