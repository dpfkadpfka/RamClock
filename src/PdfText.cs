using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassroomHud
{
    // PDF에서 글자와 위치를 꺼내는 작은 리더 (표 모양 분석용)
    public class PdfRun { public float X, Y, W, Size; public string Text; public int Page; public List<PdfRun> Parts; }
    public class PdfLine { public float A1, A2, P; public int Page; }   // 가로선: x1~x2, y / 세로선: y1~y2, x

    public class PdfText
    {
        // ---------- 객체 ----------
        class Ref { public int N; }
        class Name { public string V; public override string ToString() { return V; } }
        class Str { public byte[] B; }
        class Op { public string V; }
        class Stream { public Dictionary<string, object> D; public byte[] Raw; public int N = -1, Gen; public bool Plain; }

        readonly byte[] data;
        readonly Dictionary<int, int> offs = new Dictionary<int, int>();
        readonly Dictionary<int, object> cache = new Dictionary<int, object>();
        readonly Dictionary<int, KeyValuePair<int, int>> inStm = new Dictionary<int, KeyValuePair<int, int>>();
        readonly Dictionary<int, List<object>> stmObjs = new Dictionary<int, List<object>>();
        public string Error;
        public List<PdfLine> HLines = new List<PdfLine>(), VLines = new List<PdfLine>();

        public PdfText(byte[] bytes)
        {
            data = bytes;
            string latin = Encoding.GetEncoding(28591).GetString(bytes);
            foreach (Match m in Regex.Matches(latin, @"(?<![0-9])(\d+)\s+(\d+)\s+obj\b"))
                offs[int.Parse(m.Groups[1].Value)] = m.Index;
            if (latin.Contains("/Encrypt")) SetupCrypt(latin);
            foreach (var n in new List<int>(offs.Keys))
            {
                if (!LooksObjStm(n)) continue;
                var s = Get(n) as Stream;
                if (s == null) continue;
                try
                {
                    var dec = Decode(s);
                    int count = (int)Num(s.D, "N"), first = (int)Num(s.D, "First");
                    var p = new Parser(dec, 0, this);
                    var nums = new List<int>(); var pos = new List<int>();
                    for (int i = 0; i < count; i++) { nums.Add((int)Convert.ToDouble(p.Next())); pos.Add((int)Convert.ToDouble(p.Next())); }
                    var list = new List<object>();
                    for (int i = 0; i < count; i++)
                    {
                        var q = new Parser(dec, first + pos[i], this);
                        list.Add(q.Value());
                        if (!offs.ContainsKey(nums[i])) inStm[nums[i]] = new KeyValuePair<int, int>(n, i);
                    }
                    stmObjs[n] = list;
                }
                catch { }
            }
        }

        bool LooksObjStm(int n)
        {
            int o = offs[n], end = Math.Min(data.Length, o + 400);
            string head = Encoding.GetEncoding(28591).GetString(data, o, end - o);
            return head.Contains("/ObjStm");
        }

        object Get(int n)
        {
            object v;
            if (cache.TryGetValue(n, out v)) return v;
            v = null;
            int o;
            if (offs.TryGetValue(n, out o))
            {
                try
                {
                    var p = new Parser(data, o, this);
                    p.Next(); var gn = p.Next(); p.Next();   // n g obj
                    v = p.Value();
                    var d = v as Dictionary<string, object>;
                    if (d != null && p.PeekKeyword("stream")) { var st = p.ReadStream(d); st.N = n; st.Gen = gn is double ? (int)(double)gn : 0; v = st; }
                }
                catch { v = null; }
            }
            else
            {
                KeyValuePair<int, int> loc;
                if (inStm.TryGetValue(n, out loc)) { List<object> l; if (stmObjs.TryGetValue(loc.Key, out l) && loc.Value < l.Count) v = l[loc.Value]; }
            }
            cache[n] = v;
            return v;
        }

        object R(object o) { int guard = 0; while (o is Ref && guard++ < 20) o = Get(((Ref)o).N); return o; }
        Dictionary<string, object> Dict(object o) { o = R(o); var s = o as Stream; return s != null ? s.D : o as Dictionary<string, object>; }
        object V(Dictionary<string, object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? R(v) : null; }
        double Num(Dictionary<string, object> d, string key) { var v = V(d, key); return v is double ? (double)v : 0; }

        // ---------- 파서 ----------
        class Parser
        {
            readonly byte[] b; public int P; readonly PdfText doc;
            public Parser(byte[] bytes, int pos, PdfText d) { b = bytes; P = pos; doc = d; }
            static bool Ws(byte c) { return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32; }
            static bool Delim(byte c) { return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%'; }
            void Skip()
            {
                while (P < b.Length)
                {
                    if (Ws(b[P])) P++;
                    else if (b[P] == '%') { while (P < b.Length && b[P] != 10 && b[P] != 13) P++; }
                    else break;
                }
            }
            public bool AtEnd { get { Skip(); return P >= b.Length; } }
            public bool PeekKeyword(string k)
            {
                Skip();
                if (P + k.Length > b.Length) return false;
                for (int i = 0; i < k.Length; i++) if (b[P + i] != k[i]) return false;
                return true;
            }
            public Stream ReadStream(Dictionary<string, object> d)
            {
                Skip(); P += 6;
                if (P < b.Length && b[P] == 13) P++;
                if (P < b.Length && b[P] == 10) P++;
                int len = -1;
                object lv;
                if (d.TryGetValue("Length", out lv)) { lv = doc.R(lv); if (lv is double) len = (int)(double)lv; }
                int start = P, end;
                if (len >= 0 && start + len <= b.Length && Near(start + len, "endstream")) end = start + len;
                else end = Find(start, "endstream");
                if (end < 0) end = b.Length;
                var raw = new byte[Math.Max(0, end - start)];
                Array.Copy(b, start, raw, 0, raw.Length);
                return new Stream { D = d, Raw = raw };
            }
            bool Near(int pos, string k) { for (int i = pos; i < Math.Min(b.Length, pos + 4); i++) if (Match(i, k)) return true; return false; }
            bool Match(int pos, string k) { if (pos + k.Length > b.Length) return false; for (int i = 0; i < k.Length; i++) if (b[pos + i] != k[i]) return false; return true; }
            int Find(int pos, string k) { for (int i = pos; i <= b.Length - k.Length; i++) if (Match(i, k)) return i; return -1; }

            // 토큰 하나(값 또는 연산자)
            public object Next()
            {
                Skip();
                if (P >= b.Length) return null;
                byte c = b[P];
                if (c == '/')
                {
                    P++; var sb = new StringBuilder();
                    while (P < b.Length && !Ws(b[P]) && !Delim(b[P]))
                    {
                        if (b[P] == '#' && P + 2 < b.Length) { sb.Append((char)Convert.ToInt32(Encoding.ASCII.GetString(b, P + 1, 2), 16)); P += 3; }
                        else sb.Append((char)b[P++]);
                    }
                    return new Name { V = sb.ToString() };
                }
                if (c == '(') return LitStr();
                if (c == '<')
                {
                    if (P + 1 < b.Length && b[P + 1] == '<') { P += 2; return DictBody(); }
                    return HexStr();
                }
                if (c == '[') { P++; var l = new List<object>(); while (true) { Skip(); if (P >= b.Length) break; if (b[P] == ']') { P++; break; } l.Add(Value()); } return l; }
                if (c == ']' || c == '>' || c == ')' || c == '{' || c == '}') { P++; return new Op { V = ((char)c).ToString() }; }
                int s = P;
                while (P < b.Length && !Ws(b[P]) && !Delim(b[P])) P++;
                if (P == s) { P++; return new Op { V = ((char)c).ToString() }; }
                string t = Encoding.GetEncoding(28591).GetString(b, s, P - s);
                double d;
                if ((char.IsDigit(t[0]) || t[0] == '-' || t[0] == '+' || t[0] == '.') && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                if (t == "true") return true;
                if (t == "false") return false;
                if (t == "null") return null;
                return new Op { V = t };
            }

            // 값(참조 n g R 처리 포함)
            public object Value()
            {
                var v = Next();
                if (v is double)
                {
                    int save = P;
                    var a = Next(); var r = Next() as Op;
                    if (a is double && r != null && r.V == "R") return new Ref { N = (int)(double)v };
                    P = save;
                }
                return v;
            }

            Dictionary<string, object> DictBody()
            {
                var d = new Dictionary<string, object>();
                while (true)
                {
                    Skip();
                    if (P >= b.Length) break;
                    if (b[P] == '>' && P + 1 < b.Length && b[P + 1] == '>') { P += 2; break; }
                    var k = Next() as Name;
                    if (k == null) continue;
                    d[k.V] = Value();
                }
                return d;
            }

            Str LitStr()
            {
                P++; int depth = 1; var ms = new MemoryStream();
                while (P < b.Length)
                {
                    byte c = b[P++];
                    if (c == '\\')
                    {
                        if (P >= b.Length) break;
                        byte e = b[P++];
                        switch (e)
                        {
                            case (byte)'n': ms.WriteByte(10); break;
                            case (byte)'r': ms.WriteByte(13); break;
                            case (byte)'t': ms.WriteByte(9); break;
                            case (byte)'b': ms.WriteByte(8); break;
                            case (byte)'f': ms.WriteByte(12); break;
                            case 13: if (P < b.Length && b[P] == 10) P++; break;
                            case 10: break;
                            default:
                                if (e >= '0' && e <= '7')
                                {
                                    int v = e - '0';
                                    for (int i = 0; i < 2 && P < b.Length && b[P] >= '0' && b[P] <= '7'; i++) v = v * 8 + (b[P++] - '0');
                                    ms.WriteByte((byte)v);
                                }
                                else ms.WriteByte(e);
                                break;
                        }
                    }
                    else if (c == '(') { depth++; ms.WriteByte(c); }
                    else if (c == ')') { if (--depth == 0) break; ms.WriteByte(c); }
                    else ms.WriteByte(c);
                }
                return new Str { B = ms.ToArray() };
            }

            Str HexStr()
            {
                P++; var sb = new StringBuilder();
                while (P < b.Length && b[P] != '>') { char c = (char)b[P++]; if (Uri.IsHexDigit(c)) sb.Append(c); }
                P++;
                if (sb.Length % 2 == 1) sb.Append('0');
                var r = new byte[sb.Length / 2];
                for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
                return new Str { B = r };
            }

            public void SkipInlineImage()
            {
                int i = Find(P, "ID");
                if (i < 0) { P = b.Length; return; }
                P = i + 3;
                while (P < b.Length - 2)
                {
                    if (b[P] == 'E' && b[P + 1] == 'I' && (P + 2 >= b.Length || Ws(b[P + 2])) && P > 0 && Ws(b[P - 1])) { P += 2; return; }
                    P++;
                }
                P = b.Length;
            }
        }

        // ---------- 스트림 풀기 ----------
        byte[] Decode(Stream s)
        {
            var f = V(s.D, "Filter");
            var filters = new List<string>();
            if (f is Name) filters.Add(((Name)f).V);
            else if (f is List<object>) foreach (var x in (List<object>)f) { var nx = R(x) as Name; if (nx != null) filters.Add(nx.V); }
            if (crypt != null && !s.Plain && s.N >= 0)
            {
                var ty = V(s.D, "Type") as Name;
                if (ty == null || ty.V != "XRef") { try { s.Raw = DecryptObj(s.Raw, s.N, s.Gen); } catch { s.Raw = new byte[0]; } }
                s.Plain = true;
            }
            byte[] d = s.Raw;
            foreach (var name in filters)
            {
                if (name == "FlateDecode" || name == "Fl") d = Inflate(d);
                else if (name == "ASCIIHexDecode" || name == "AHx") d = AHx(d);
                else if (name == "ASCII85Decode" || name == "A85") d = A85(d);
                else return new byte[0];
            }
            var parms = Dict(V(s.D, "DecodeParms"));
            if (parms != null && Num(parms, "Predictor") >= 10) d = UnPng(d, Math.Max(1, (int)Num(parms, "Columns")));
            return d;
        }

        static byte[] AHx(byte[] src)
        {
            var o = new MemoryStream(); int hi = -1;
            foreach (byte c in src)
            {
                if (c == '>') break;
                int v = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (v < 0) continue;
                if (hi < 0) hi = v; else { o.WriteByte((byte)(hi * 16 + v)); hi = -1; }
            }
            if (hi >= 0) o.WriteByte((byte)(hi * 16));
            return o.ToArray();
        }

        static byte[] A85(byte[] src)
        {
            var o = new MemoryStream(); var grp = new int[5]; int n = 0;
            int i = 0;
            if (src.Length > 1 && src[0] == '<' && src[1] == '~') i = 2;
            for (; i < src.Length; i++)
            {
                byte c = src[i];
                if (c == '~') break;
                if (c == 'z' && n == 0) { o.Write(new byte[4], 0, 4); continue; }
                if (c < '!' || c > 'u') continue;
                grp[n++] = c - 33;
                if (n == 5)
                {
                    uint v = 0; for (int k = 0; k < 5; k++) v = v * 85 + (uint)grp[k];
                    o.WriteByte((byte)(v >> 24)); o.WriteByte((byte)(v >> 16)); o.WriteByte((byte)(v >> 8)); o.WriteByte((byte)v);
                    n = 0;
                }
            }
            if (n > 1)
            {
                for (int k = n; k < 5; k++) grp[k] = 84;
                uint v = 0; for (int k = 0; k < 5; k++) v = v * 85 + (uint)grp[k];
                for (int k = 0; k < n - 1; k++) o.WriteByte((byte)(v >> (24 - 8 * k)));
            }
            return o.ToArray();
        }

        // ---------- 보안(빈 암호로 잠긴 PDF 풀기: 한글/학교 문서에 흔함) ----------
        class Crypt { public byte[] Key; public int Len; public bool Aes, V5; }
        Crypt crypt;
        static readonly byte[] Pad = { 0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A };

        void SetupCrypt(string latin)
        {
            try
            {
                var em = Regex.Matches(latin, @"/Encrypt\s+(\d+)\s+\d+\s+R");
                Dictionary<string, object> ed = null;
                if (em.Count > 0) ed = Dict(Get(int.Parse(em[em.Count - 1].Groups[1].Value)));
                else
                {
                    int ix = latin.LastIndexOf("/Encrypt");
                    var p0 = new Parser(data, ix + 8, this); ed = p0.Value() as Dictionary<string, object>;
                }
                if (ed == null) { Error = "보안이 걸린 PDF라 읽지 못했어요."; return; }
                var filt = V(ed, "Filter") as Name;
                if (filt != null && filt.V != "Standard") { Error = "인증서로 잠긴 PDF는 읽을 수 없어요."; return; }
                int v = (int)Num(ed, "V"), r = (int)Num(ed, "R");
                byte[] O = StrB(V(ed, "O")), U = StrB(V(ed, "U"));
                int P = (int)(long)Num(ed, "P");
                var c = new Crypt();
                if (v >= 5)
                {
                    byte[] UE = StrB(V(ed, "UE"));
                    if (U == null || U.Length < 48 || UE == null || UE.Length < 32) { Error = "보안이 걸린 PDF라 읽지 못했어요."; return; }
                    var vs = new byte[8]; Array.Copy(U, 32, vs, 0, 8);
                    var ks = new byte[8]; Array.Copy(U, 40, ks, 0, 8);
                    var chk = r >= 6 ? Hash6(new byte[0], vs, new byte[0]) : Sha(256, Cat(new byte[0], vs));
                    bool ok = true; for (int i = 0; i < 32; i++) if (chk[i] != U[i]) ok = false;
                    if (!ok) { Error = "열 때 암호를 넣어야 하는 PDF예요. 암호를 푼 PDF로 다시 저장해 주세요."; return; }
                    var ik = r >= 6 ? Hash6(new byte[0], ks, new byte[0]) : Sha(256, Cat(new byte[0], ks));
                    var ue = new byte[32]; Array.Copy(UE, ue, 32);
                    c.Key = AesRaw(ik, new byte[16], ue, false); c.Len = 32; c.Aes = true; c.V5 = true;
                }
                else
                {
                    byte[] id0 = new byte[0];
                    int ii = latin.LastIndexOf("/ID");
                    if (ii >= 0) { var lst = new Parser(data, ii + 3, this).Value() as List<object>; if (lst != null && lst.Count > 0) id0 = StrB(lst[0]) ?? new byte[0]; }
                    int n = r <= 2 ? 5 : Math.Max(5, Math.Min(16, (int)(Num(ed, "Length") > 0 ? Num(ed, "Length") : 40) / 8));
                    bool encMeta = !(V(ed, "EncryptMetadata") is bool) || (bool)V(ed, "EncryptMetadata");
                    var buf = new MemoryStream();
                    buf.Write(Pad, 0, 32);
                    buf.Write(O, 0, Math.Min(32, O.Length));
                    buf.Write(BitConverter.GetBytes(P), 0, 4);
                    buf.Write(id0, 0, id0.Length);
                    if (r >= 4 && !encMeta) buf.Write(new byte[] { 255, 255, 255, 255 }, 0, 4);
                    byte[] key;
                    using (var md = System.Security.Cryptography.MD5.Create())
                    {
                        key = md.ComputeHash(buf.ToArray());
                        if (r >= 3) for (int i = 0; i < 50; i++) key = md.ComputeHash(key, 0, n);
                    }
                    Array.Resize(ref key, n);
                    c.Key = key; c.Len = n;
                    if (v == 4)
                    {
                        var cf = Dict(V(ed, "CF")); var std = cf != null ? Dict(V(cf, "StdCF")) : null;
                        var cfm = std != null ? V(std, "CFM") as Name : null;
                        c.Aes = cfm != null && cfm.V == "AESV2";
                    }
                }
                crypt = c;
            }
            catch { Error = "보안이 걸린 PDF라 읽지 못했어요."; }
        }

        static byte[] StrB(object o) { var s = o as Str; return s != null ? s.B : null; }
        static byte[] Cat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; Array.Copy(a, r, a.Length); Array.Copy(b, 0, r, a.Length, b.Length); return r; }
        static byte[] Sha(int bits, byte[] d)
        {
            System.Security.Cryptography.HashAlgorithm h = bits == 256 ? (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create()
                : bits == 384 ? (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA384.Create() : System.Security.Cryptography.SHA512.Create();
            using (h) return h.ComputeHash(d);
        }
        static byte[] AesRaw(byte[] key, byte[] iv, byte[] data, bool pkcs)
        {
            using (var a = new System.Security.Cryptography.RijndaelManaged { KeySize = key.Length * 8, BlockSize = 128, Mode = System.Security.Cryptography.CipherMode.CBC, Padding = pkcs ? System.Security.Cryptography.PaddingMode.PKCS7 : System.Security.Cryptography.PaddingMode.None, Key = key, IV = iv })
            using (var t = a.CreateDecryptor()) return t.TransformFinalBlock(data, 0, data.Length);
        }
        static byte[] AesEnc(byte[] key, byte[] iv, byte[] data)
        {
            using (var a = new System.Security.Cryptography.RijndaelManaged { KeySize = 128, BlockSize = 128, Mode = System.Security.Cryptography.CipherMode.CBC, Padding = System.Security.Cryptography.PaddingMode.None, Key = key, IV = iv })
            using (var t = a.CreateEncryptor()) return t.TransformFinalBlock(data, 0, data.Length);
        }
        // PDF 2.0 (R6) 암호 해시
        static byte[] Hash6(byte[] pwd, byte[] salt, byte[] udata)
        {
            byte[] K = Sha(256, Cat(Cat(pwd, salt), udata));
            for (int i = 0; ; i++)
            {
                var one = Cat(Cat(pwd, K), udata);
                var K1 = new byte[one.Length * 64];
                for (int j = 0; j < 64; j++) Array.Copy(one, 0, K1, j * one.Length, one.Length);
                var k16 = new byte[16]; var iv = new byte[16]; Array.Copy(K, 0, k16, 0, 16); Array.Copy(K, 16, iv, 0, 16);
                var E = AesEnc(k16, iv, K1);
                int sum = 0; for (int j = 0; j < 16; j++) sum += E[j];
                K = Sha(sum % 3 == 0 ? 256 : sum % 3 == 1 ? 384 : 512, E);
                if (i >= 63 && E[E.Length - 1] <= i - 31) break;
            }
            Array.Resize(ref K, 32);
            return K;
        }

        byte[] DecryptObj(byte[] raw, int num, int gen)
        {
            byte[] key;
            if (crypt.V5) key = crypt.Key;
            else
            {
                var b = new byte[crypt.Len + 5 + (crypt.Aes ? 4 : 0)];
                Array.Copy(crypt.Key, b, crypt.Len);
                b[crypt.Len] = (byte)num; b[crypt.Len + 1] = (byte)(num >> 8); b[crypt.Len + 2] = (byte)(num >> 16);
                b[crypt.Len + 3] = (byte)gen; b[crypt.Len + 4] = (byte)(gen >> 8);
                if (crypt.Aes) { b[crypt.Len + 5] = 0x73; b[crypt.Len + 6] = 0x41; b[crypt.Len + 7] = 0x6C; b[crypt.Len + 8] = 0x54; }
                using (var md = System.Security.Cryptography.MD5.Create()) key = md.ComputeHash(b);
                Array.Resize(ref key, Math.Min(16, crypt.Len + 5));
            }
            if (crypt.Aes)
            {
                if (raw.Length < 32) return new byte[0];
                var iv = new byte[16]; Array.Copy(raw, iv, 16);
                int len = (raw.Length - 16) / 16 * 16;
                var body = new byte[len]; Array.Copy(raw, 16, body, 0, len);
                try { return AesRaw(key, iv, body, true); } catch { return AesRaw(key, iv, body, false); }
            }
            // RC4
            var S = new byte[256]; for (int i = 0; i < 256; i++) S[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++) { j = (j + S[i] + key[i % key.Length]) & 255; var t = S[i]; S[i] = S[j]; S[j] = t; }
            var o = new byte[raw.Length];
            for (int k = 0, i = 0, j = 0; k < raw.Length; k++)
            {
                i = (i + 1) & 255; j = (j + S[i]) & 255; var t = S[i]; S[i] = S[j]; S[j] = t;
                o[k] = (byte)(raw[k] ^ S[(S[i] + S[j]) & 255]);
            }
            return o;
        }

        static byte[] Inflate(byte[] src)
        {
            var outp = new MemoryStream();
            try
            {
                int start = src.Length > 2 && (src[0] & 0x0F) == 8 ? 2 : 0;
                using (var ds = new DeflateStream(new MemoryStream(src, start, src.Length - start), CompressionMode.Decompress))
                {
                    var buf = new byte[8192]; int n;
                    while ((n = ds.Read(buf, 0, buf.Length)) > 0) outp.Write(buf, 0, n);
                }
            }
            catch { }
            return outp.ToArray();
        }

        static byte[] UnPng(byte[] d, int cols)
        {
            int row = cols + 1; var o = new MemoryStream(); var prev = new byte[cols];
            for (int i = 0; i + row <= d.Length; i += row)
            {
                int t = d[i]; var cur = new byte[cols];
                for (int j = 0; j < cols; j++)
                {
                    int a = j > 0 ? cur[j - 1] : 0, up = prev[j], c = j > 0 ? prev[j - 1] : 0, x = d[i + 1 + j];
                    switch (t)
                    {
                        case 1: x += a; break;
                        case 2: x += up; break;
                        case 3: x += (a + up) / 2; break;
                        case 4: { int p = a + up - c, pa = Math.Abs(p - a), pb = Math.Abs(p - up), pc = Math.Abs(p - c); x += (pa <= pb && pa <= pc) ? a : (pb <= pc ? up : c); break; }
                    }
                    cur[j] = (byte)x;
                }
                o.Write(cur, 0, cols); prev = cur;
            }
            return o.ToArray();
        }

        // ---------- 글꼴 ----------
        class FontInfo
        {
            public bool TwoByte;
            public Dictionary<int, string> Map = new Dictionary<int, string>();
            public Dictionary<int, double> Widths = new Dictionary<int, double>();
            public double DW = 1000;
            public bool HasMap;
            public int Mode;
            public double WScale = 0.001;   // Type3 글꼴은 FontMatrix 단위   // 0 보통, 1 코드=유니코드(UCS2), 2 완성형/UHC(1~2바이트)
        }
        readonly Dictionary<int, FontInfo> fontCache = new Dictionary<int, FontInfo>();

        FontInfo LoadFont(object fo)
        {
            int key = fo is Ref ? ((Ref)fo).N : -1;
            FontInfo fi;
            if (key >= 0 && fontCache.TryGetValue(key, out fi)) return fi;
            fi = new FontInfo();
            var d = Dict(fo);
            if (d != null)
            {
                var sub = V(d, "Subtype") as Name;
                fi.TwoByte = sub != null && sub.V == "Type0";
                if (sub != null && sub.V == "Type3")
                {
                    var fm = V(d, "FontMatrix") as List<object>;
                    if (fm != null && fm.Count >= 4 && R(fm[0]) is double) { double a = (double)R(fm[0]); if (a > 0) fi.WScale = a; }
                }
                var tu = R(V(d, "ToUnicode")) as Stream;
                if (tu == null && d.ContainsKey("ToUnicode")) tu = R(d["ToUnicode"]) as Stream;
                if (tu != null) { ParseCMap(Decode(tu), fi); fi.HasMap = fi.Map.Count > 0; }
                if (fi.TwoByte)
                {
                    var desc = V(d, "DescendantFonts") as List<object>;
                    var dd = desc != null && desc.Count > 0 ? Dict(desc[0]) : null;
                    if (!fi.HasMap)
                    {
                        // ToUnicode가 없을 때: 한국어 표준 인코딩이나 내장 글꼴 표로 글자 알아내기
                        var en = V(d, "Encoding") as Name;
                        string ev = en != null ? en.V : "";
                        if (ev.Contains("UCS2") || ev.Contains("UTF16")) fi.Mode = 1;
                        else if (ev.StartsWith("KSC") || ev.Contains("UHC") || ev.Contains("KSCms")) fi.Mode = 2;
                        else if (dd != null) { try { EmbeddedCmap(dd, fi); } catch { } fi.HasMap = fi.Map.Count > 0; }
                    }
                    if (dd != null)
                    {
                        if (dd.ContainsKey("DW")) fi.DW = Num(dd, "DW");
                        var w = V(dd, "W") as List<object>;
                        if (w != null)
                        {
                            for (int i = 0; i < w.Count;)
                            {
                                var a = R(w[i]); if (!(a is double)) { i++; continue; }
                                int c1 = (int)(double)a;
                                var nx = i + 1 < w.Count ? R(w[i + 1]) : null;
                                if (nx is List<object>)
                                {
                                    var arr = (List<object>)nx;
                                    for (int j = 0; j < arr.Count; j++) { var x = R(arr[j]); if (x is double) fi.Widths[c1 + j] = (double)x; }
                                    i += 2;
                                }
                                else if (nx is double && i + 2 < w.Count && R(w[i + 2]) is double)
                                {
                                    int c2 = (int)(double)nx; double ww = (double)R(w[i + 2]);
                                    for (int c = c1; c <= c2 && c - c1 < 70000; c++) fi.Widths[c] = ww;
                                    i += 3;
                                }
                                else i++;
                            }
                        }
                    }
                }
                else
                {
                    fi.DW = fi.WScale != 0.001 ? 0.5 / fi.WScale : 500;
                    int fc = (int)Num(d, "FirstChar");
                    var w = V(d, "Widths") as List<object>;
                    if (w != null) for (int j = 0; j < w.Count; j++) { var x = R(w[j]); if (x is double) fi.Widths[fc + j] = (double)x; }
                }
            }
            if (key >= 0) fontCache[key] = fi;
            return fi;
        }

        static Encoding uhc;
        static Encoding Uhc { get { if (uhc == null) { try { uhc = Encoding.GetEncoding(949); } catch { uhc = Encoding.ASCII; } } return uhc; } }

        // 내장 TrueType 글꼴의 cmap 표를 거꾸로 써서 글리프 번호 → 글자
        void EmbeddedCmap(Dictionary<string, object> dd, FontInfo fi)
        {
            var fd = Dict(V(dd, "FontDescriptor"));
            var ff = fd != null ? R(V(fd, "FontFile2")) as Stream : null;
            if (ff == null) return;
            var t = Decode(ff);
            if (t.Length < 12) return;
            Func<int, int> U16 = o => (t[o] << 8) | t[o + 1];
            Func<int, long> U32 = o => ((long)t[o] << 24) | ((long)t[o + 1] << 16) | ((long)t[o + 2] << 8) | t[o + 3];
            int nt = U16(4), cmap = -1;
            for (int i = 0; i < nt; i++) { int o = 12 + i * 16; if (o + 16 > t.Length) break; if (t[o] == 'c' && t[o + 1] == 'm' && t[o + 2] == 'a' && t[o + 3] == 'p') cmap = (int)U32(o + 8); }
            if (cmap < 0 || cmap + 4 > t.Length) return;
            var g2u = new Dictionary<int, int>();
            int ns = U16(cmap + 2);
            for (int i = 0; i < ns; i++)
            {
                int pid = U16(cmap + 4 + i * 8), eid = U16(cmap + 6 + i * 8), so = cmap + (int)U32(cmap + 8 + i * 8);
                if (so + 4 > t.Length || !(pid == 3 && (eid == 1 || eid == 10) || pid == 0)) continue;
                int fmt = U16(so);
                if (fmt == 4)
                {
                    int segX2 = U16(so + 6), ends = so + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                    for (int sg = 0; sg < segX2 / 2; sg++)
                    {
                        int end = U16(ends + sg * 2), start = U16(starts + sg * 2), delta = U16(deltas + sg * 2), ro = U16(ranges + sg * 2);
                        if (start == 0xFFFF) continue;
                        for (int c = start; c <= end; c++)
                        {
                            int gid;
                            if (ro == 0) gid = (c + delta) & 0xFFFF;
                            else { int ga = ranges + sg * 2 + ro + (c - start) * 2; if (ga + 2 > t.Length) continue; gid = U16(ga); if (gid != 0) gid = (gid + delta) & 0xFFFF; }
                            if (gid != 0 && !g2u.ContainsKey(gid)) g2u[gid] = c;
                        }
                    }
                }
                else if (fmt == 12)
                {
                    long ng = U32(so + 12);
                    for (long k = 0; k < ng && so + 16 + k * 12 + 12 <= t.Length; k++)
                    {
                        int o = so + 16 + (int)k * 12; long a = U32(o), z = U32(o + 4), g0 = U32(o + 8);
                        for (long c = a; c <= z && c - a < 70000; c++) if (!g2u.ContainsKey((int)(g0 + c - a))) g2u[(int)(g0 + c - a)] = (int)c;
                    }
                }
            }
            if (g2u.Count == 0) return;
            var c2g = R(V(dd, "CIDToGIDMap")) as Stream;
            if (c2g != null)
            {
                var m = Decode(c2g);
                for (int cid = 0; cid * 2 + 1 < m.Length; cid++) { int gid = (m[cid * 2] << 8) | m[cid * 2 + 1]; int u; if (gid != 0 && g2u.TryGetValue(gid, out u)) fi.Map[cid] = char.ConvertFromUtf32(u); }
            }
            else foreach (var kv in g2u) { try { fi.Map[kv.Key] = char.ConvertFromUtf32(kv.Value); } catch { } }
        }

        static void ParseCMap(byte[] cm, FontInfo fi)
        {
            string s = Encoding.GetEncoding(28591).GetString(cm);
            foreach (Match m in Regex.Matches(s, @"begincodespacerange\s*<([0-9A-Fa-f]+)>"))
                fi.TwoByte = m.Groups[1].Value.Length >= 4;
            foreach (Match blk in Regex.Matches(s, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    fi.Map[Convert.ToInt32(m.Groups[1].Value, 16)] = Utf16(m.Groups[2].Value);
            foreach (Match blk in Regex.Matches(s, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<[0-9A-Fa-f]+>|\[[^\]]*\])"))
                {
                    int a = Convert.ToInt32(m.Groups[1].Value, 16), z = Convert.ToInt32(m.Groups[2].Value, 16);
                    string dst = m.Groups[3].Value;
                    if (z - a > 70000) continue;
                    if (dst.StartsWith("<"))
                    {
                        string hex = dst.Trim('<', '>');
                        string baseStr = Utf16(hex);
                        if (baseStr.Length == 0) continue;
                        for (int c = a; c <= z; c++)
                        {
                            char last = (char)(baseStr[baseStr.Length - 1] + (c - a));
                            fi.Map[c] = baseStr.Substring(0, baseStr.Length - 1) + last;
                        }
                    }
                    else
                    {
                        int c = a;
                        foreach (Match h in Regex.Matches(dst, @"<([0-9A-Fa-f]+)>")) fi.Map[c++] = Utf16(h.Groups[1].Value);
                    }
                }
        }

        static string Utf16(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
            if (hex.Length == 2) sb.Append((char)Convert.ToInt32(hex, 16));
            return sb.ToString();
        }

        // ---------- 페이지 ----------
        List<Dictionary<string, object>> Pages()
        {
            var list = new List<Dictionary<string, object>>();
            string latin = Encoding.GetEncoding(28591).GetString(data);
            var ms = Regex.Matches(latin, @"/Root\s+(\d+)\s+\d+\s+R");
            if (ms.Count == 0) return list;
            var root = Dict(Get(int.Parse(ms[ms.Count - 1].Groups[1].Value)));
            Walk(Dict(V(root, "Pages") ?? (root != null && root.ContainsKey("Pages") ? R(root["Pages"]) : null)), null, list, 0);
            return list;
        }

        void Walk(Dictionary<string, object> node, object res, List<Dictionary<string, object>> list, int depth)
        {
            if (node == null || depth > 30) return;
            object r; if (node.TryGetValue("Resources", out r)) res = r;
            var kids = V(node, "Kids") as List<object>;
            if (kids != null) { foreach (var k in kids) Walk(Dict(k), res, list, depth + 1); return; }
            if (!node.ContainsKey("Resources") && res != null) node["Resources"] = res;
            list.Add(node);
        }

        // ---------- 내용 읽기 ----------
        class GS { public double[] Ctm = { 1, 0, 0, 1, 0, 0 }; public FontInfo Font; public double Size = 10, Tc, Tw, Th = 1, TL, Rise; public GS Copy() { var g = (GS)MemberwiseClone(); g.Ctm = (double[])Ctm.Clone(); return g; } }

        static double[] Mul(double[] a, double[] b)
        {
            return new[] { a[0]*b[0] + a[1]*b[2], a[0]*b[1] + a[1]*b[3], a[2]*b[0] + a[3]*b[2], a[2]*b[1] + a[3]*b[3], a[4]*b[0] + a[5]*b[2] + b[4], a[4]*b[1] + a[5]*b[3] + b[5] };
        }

        public List<PdfRun> Extract(int maxPages = 4)
        {
            var glyphs = new List<PdfRun>();
            if (Error != null) return glyphs;
            var pages = Pages();
            for (int i = 0; i < pages.Count && i < maxPages; i++)
            {
                var pg = pages[i];
                var content = new MemoryStream();
                var c = V(pg, "Contents");
                var lst = c as List<object>;
                if (lst != null) foreach (var x in lst) { var s = R(x) as Stream; if (s != null) { var dd = Decode(s); content.Write(dd, 0, dd.Length); content.WriteByte(10); } }
                else { var s = c as Stream; if (s != null) { var dd = Decode(s); content.Write(dd, 0, dd.Length); } }
                Run(content.ToArray(), Dict(V(pg, "Resources")), new GS(), glyphs, i, 0);
            }
            return Merge(Dedupe(glyphs));
        }

        // 같은 글자를 같은 자리에 두 번 그린 것(굵게·테두리 효과, Canva 등) 하나로
        static List<PdfRun> Dedupe(List<PdfRun> g)
        {
            var res = new List<PdfRun>();
            var seen = new Dictionary<string, List<PdfRun>>();
            foreach (var r in g)
            {
                string key = r.Page + "|" + r.Text;
                List<PdfRun> l;
                if (!seen.TryGetValue(key, out l)) { l = new List<PdfRun>(); seen[key] = l; }
                bool dup = false;
                float tol = Math.Max(0.8f, r.Size * 0.2f);
                foreach (var o in l) if (Math.Abs(o.X - r.X) < tol && Math.Abs(o.Y - r.Y) < tol) { dup = true; break; }
                if (dup) continue;
                l.Add(r); res.Add(r);
            }
            return res;
        }

        void Run(byte[] cs, Dictionary<string, object> res, GS gs, List<PdfRun> outp, int page, int depth)
        {
            if (depth > 6) return;
            var p = new Parser(cs, 0, this);
            var ops = new List<object>();
            var stack = new Stack<GS>();
            var segs = new List<double[]>();
            double cx0 = 0, cy0 = 0, sx0 = 0, sy0 = 0;
            double[] tm = { 1, 0, 0, 1, 0, 0 }, tlm = { 1, 0, 0, 1, 0, 0 };
            var fonts = Dict(V(res, "Font"));
            var xobjs = Dict(V(res, "XObject"));
            int guard = 0;
            while (!p.AtEnd && guard++ < 2000000)
            {
                object t;
                try { t = p.Next(); } catch { break; }
                var op = t as Op;
                if (op == null) { ops.Add(t); continue; }
                string o = op.V;
                try
                {
                    switch (o)
                    {
                        case "q": stack.Push(gs.Copy()); break;
                        case "Q": if (stack.Count > 0) gs = stack.Pop(); break;
                        case "cm": if (ops.Count >= 6) gs.Ctm = Mul(Nums(ops, 6), gs.Ctm); break;
                        case "BT": tm = new double[] { 1, 0, 0, 1, 0, 0 }; tlm = (double[])tm.Clone(); break;
                        case "Tf":
                            if (ops.Count >= 2)
                            {
                                var fn = ops[ops.Count - 2] as Name; gs.Size = D(ops[ops.Count - 1]);
                                object fo; gs.Font = fn != null && fonts != null && fonts.TryGetValue(fn.V, out fo) ? LoadFont(fo) : null;
                            }
                            break;
                        case "Tc": gs.Tc = D(Last(ops)); break;
                        case "Tw": gs.Tw = D(Last(ops)); break;
                        case "Tz": gs.Th = D(Last(ops)) / 100.0; break;
                        case "TL": gs.TL = D(Last(ops)); break;
                        case "Ts": gs.Rise = D(Last(ops)); break;
                        case "Td": case "TD":
                            if (ops.Count >= 2)
                            {
                                double tx = D(ops[ops.Count - 2]), ty = D(ops[ops.Count - 1]);
                                if (o == "TD") gs.TL = -ty;
                                tlm = Mul(new[] { 1, 0, 0, 1, tx, ty }, tlm); tm = (double[])tlm.Clone();
                            }
                            break;
                        case "Tm": if (ops.Count >= 6) { tlm = Nums(ops, 6); tm = (double[])tlm.Clone(); } break;
                        case "T*": tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm); tm = (double[])tlm.Clone(); break;
                        case "Tj": { var s = Last(ops) as Str; if (s != null) tm = Show(s.B, gs, tm, outp, page); break; }
                        case "'": { tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm); tm = (double[])tlm.Clone(); var s = Last(ops) as Str; if (s != null) tm = Show(s.B, gs, tm, outp, page); break; }
                        case "\"": { if (ops.Count >= 3) { gs.Tw = D(ops[ops.Count - 3]); gs.Tc = D(ops[ops.Count - 2]); } tlm = Mul(new[] { 1, 0, 0, 1, 0, -gs.TL }, tlm); tm = (double[])tlm.Clone(); var s = Last(ops) as Str; if (s != null) tm = Show(s.B, gs, tm, outp, page); break; }
                        case "TJ":
                            {
                                var arr = Last(ops) as List<object>;
                                if (arr != null) foreach (var e in arr)
                                    {
                                        if (e is Str) tm = Show(((Str)e).B, gs, tm, outp, page);
                                        else if (e is double) tm = Mul(new[] { 1, 0, 0, 1, -(double)e / 1000.0 * gs.Size * gs.Th, 0 }, tm);
                                    }
                                break;
                            }
                        case "Do":
                            {
                                var nm = Last(ops) as Name; object xo;
                                if (nm != null && xobjs != null && xobjs.TryGetValue(nm.V, out xo))
                                {
                                    var xs = R(xo) as Stream;
                                    var st = xs != null ? V(xs.D, "Subtype") as Name : null;
                                    if (st != null && st.V == "Form")
                                    {
                                        var g2 = gs.Copy();
                                        var mx = V(xs.D, "Matrix") as List<object>;
                                        if (mx != null && mx.Count == 6) g2.Ctm = Mul(new[] { D(R(mx[0])), D(R(mx[1])), D(R(mx[2])), D(R(mx[3])), D(R(mx[4])), D(R(mx[5])) }, g2.Ctm);
                                        var r2 = Dict(V(xs.D, "Resources")) ?? res;
                                        Run(Decode(xs), r2, g2, outp, page, depth + 1);
                                    }
                                }
                                break;
                            }
                        case "BI": p.SkipInlineImage(); break;
                        case "m": if (ops.Count >= 2) { cx0 = sx0 = D(ops[ops.Count - 2]); cy0 = sy0 = D(ops[ops.Count - 1]); } break;
                        case "l": if (ops.Count >= 2) { double x = D(ops[ops.Count - 2]), y = D(ops[ops.Count - 1]); segs.Add(Seg(gs.Ctm, cx0, cy0, x, y)); cx0 = x; cy0 = y; } break;
                        case "c": if (ops.Count >= 6) { cx0 = D(ops[ops.Count - 2]); cy0 = D(ops[ops.Count - 1]); } break;
                        case "v": case "y": if (ops.Count >= 4) { cx0 = D(ops[ops.Count - 2]); cy0 = D(ops[ops.Count - 1]); } break;
                        case "h": segs.Add(Seg(gs.Ctm, cx0, cy0, sx0, sy0)); cx0 = sx0; cy0 = sy0; break;
                        case "re":
                            if (ops.Count >= 4)
                            {
                                double x = D(ops[ops.Count - 4]), y = D(ops[ops.Count - 3]), w = D(ops[ops.Count - 2]), hh = D(ops[ops.Count - 1]);
                                segs.Add(Seg(gs.Ctm, x, y, x + w, y)); segs.Add(Seg(gs.Ctm, x + w, y, x + w, y + hh));
                                segs.Add(Seg(gs.Ctm, x + w, y + hh, x, y + hh)); segs.Add(Seg(gs.Ctm, x, y + hh, x, y));
                                cx0 = sx0 = x; cy0 = sy0 = y;
                            }
                            break;
                        case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                            foreach (var sg in segs) AddLine(sg, page);
                            segs.Clear(); break;
                        case "n": segs.Clear(); break;
                    }
                }
                catch { }
                ops.Clear();
            }
        }

        static double[] Seg(double[] m, double x1, double y1, double x2, double y2)
        {
            return new[] { m[0] * x1 + m[2] * y1 + m[4], m[1] * x1 + m[3] * y1 + m[5], m[0] * x2 + m[2] * y2 + m[4], m[1] * x2 + m[3] * y2 + m[5] };
        }

        void AddLine(double[] g, int page)
        {
            double dx = Math.Abs(g[2] - g[0]), dy = Math.Abs(g[3] - g[1]);
            if (dy < 0.8 && dx > 3) HLines.Add(new PdfLine { A1 = (float)Math.Min(g[0], g[2]), A2 = (float)Math.Max(g[0], g[2]), P = (float)((g[1] + g[3]) / 2), Page = page });
            else if (dx < 0.8 && dy > 3) VLines.Add(new PdfLine { A1 = (float)Math.Min(g[1], g[3]), A2 = (float)Math.Max(g[1], g[3]), P = (float)((g[0] + g[2]) / 2), Page = page });
        }

        static object Last(List<object> ops) { return ops.Count > 0 ? ops[ops.Count - 1] : null; }
        static double D(object o) { return o is double ? (double)o : 0; }
        static double[] Nums(List<object> ops, int n) { var r = new double[n]; for (int i = 0; i < n; i++) r[i] = D(ops[ops.Count - n + i]); return r; }

        double[] Show(byte[] bytes, GS gs, double[] tm, List<PdfRun> outp, int page)
        {
            var f = gs.Font ?? new FontInfo();
            int step = f.TwoByte ? 2 : 1;
            for (int i = 0; i + 1 <= bytes.Length; i += step)
            {
                step = f.TwoByte ? 2 : 1;
                if (f.Mode == 2) step = bytes[i] >= 0x81 ? 2 : 1;
                if (i + step > bytes.Length) break;
                int code = step == 2 ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
                string u;
                if (f.Mode == 1) u = ((char)code).ToString();
                else if (f.Mode == 2) { try { u = Uhc.GetString(bytes, i, step); } catch { u = ""; } }
                else if (!f.Map.TryGetValue(code, out u)) u = !f.TwoByte ? ((char)code).ToString() : "";
                double w0; if (!f.Widths.TryGetValue(code, out w0)) w0 = f.DW;
                double adv = (w0 * f.WScale * gs.Size + gs.Tc + (step == 1 && code == 32 ? gs.Tw : 0)) * gs.Th;
                var trm = Mul(new[] { gs.Size * gs.Th, 0, 0, gs.Size, 0, gs.Rise }, Mul(tm, gs.Ctm));
                var m = Mul(tm, gs.Ctm);
                double scale = Math.Sqrt(m[2] * m[2] + m[3] * m[3]);
                double xs = Math.Sqrt(m[0] * m[0] + m[1] * m[1]);
                if (!string.IsNullOrEmpty(u)) { var cb = new StringBuilder(); foreach (char ch in u) if (ch >= 0x20 && ch != 0xFFFD && ch != 0xAD && ch != 0x200B && ch != 0xFEFF) cb.Append(ch); u = cb.ToString(); }
                if (!string.IsNullOrEmpty(u))
                    outp.Add(new PdfRun { X = (float)trm[4], Y = (float)trm[5], W = (float)(adv * xs), Size = (float)(gs.Size * scale), Text = u, Page = page });
                tm = Mul(new[] { 1, 0, 0, 1, adv, 0 }, tm);
            }
            return tm;
        }

        // 글자 하나씩을 같은 줄·가까운 거리끼리 묶기
        static List<PdfRun> Merge(List<PdfRun> g)
        {
            var res = new List<PdfRun>();
            g.Sort((a, b) =>
            {
                if (a.Page != b.Page) return a.Page.CompareTo(b.Page);
                float tol = Math.Max(1f, Math.Min(a.Size, b.Size) * 0.35f);
                if (Math.Abs(a.Y - b.Y) > tol) return b.Y.CompareTo(a.Y);
                return a.X.CompareTo(b.X);
            });
            PdfRun cur = null;
            foreach (var r in g)
            {
                if (cur != null && cur.Page == r.Page && Math.Abs(cur.Y - r.Y) <= Math.Max(1f, cur.Size * 0.35f))
                {
                    float gap = r.X - (cur.X + cur.W);
                    if (gap > -cur.Size * 0.3f && gap < cur.Size * 0.6f)
                    {
                        if (gap > cur.Size * 0.22f && !cur.Text.EndsWith(" ") && r.Text != " ") cur.Text += " ";
                        cur.Text += r.Text;
                        cur.W = r.X + r.W - cur.X;
                        cur.Parts.Add(r);
                        continue;
                    }
                }
                if (cur != null) res.Add(cur);
                cur = new PdfRun { X = r.X, Y = r.Y, W = r.W, Size = r.Size, Text = r.Text, Page = r.Page, Parts = new List<PdfRun> { r } };
            }
            if (cur != null) res.Add(cur);
            foreach (var r in res) r.Text = Regex.Replace(r.Text, @"\s+", " ").Trim();
            res.RemoveAll(r => r.Text.Length == 0);
            return res;
        }
    }
}
