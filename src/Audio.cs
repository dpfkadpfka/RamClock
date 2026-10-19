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
            try { spDrum = new SoundPlayer(new MemoryStream(Wav(DrumRoll(), 22050))); spDrum.Load(); } catch { spDrum = null; }
            try { spFan = new SoundPlayer(new MemoryStream(Wav(Fanfare(), 22050))); spFan.Load(); } catch { spFan = null; }
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
            var l = new System.Collections.Generic.List<string>(tokens.Keys);
            // 한국어 목소리를 앞으로
            l.Sort((a, b) => { bool ka = a.Contains("Korean") || a.Contains("한국"), kb = b.Contains("Korean") || b.Contains("한국"); return ka == kb ? string.Compare(a, b, StringComparison.Ordinal) : (ka ? -1 : 1); });
            return l;
        }

        // 목소리·빠르기·크기 적용 (name이 비면 한국어 기본 목소리)
        public static void ApplyVoice(string name, int rate, int volume)
        {
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
            catch { }
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
            catch { }
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
        public static void Announce(string text, string soundName, int times, bool sfx, bool voiceOn, int chimes = 1)
        {
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
