using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 작업 표시줄의 RamClock 아이콘을 오른쪽 클릭했을 때 나오는 목록(점프 목록)에
    // [업데이트 확인] · [설정] · [생일 달력] · [뽑기]를 넣는다.
    // 누르면 RamClock.exe --cmd <이름> 으로 실행되고, 이미 켜져 있는 RamClock에 신호만 보내고 끝난다.
    static class JumpList
    {
        public static readonly string[][] Tasks =
        {
            new[] { "update", "업데이트 확인", "새 버전이 있는지 지금 확인해요" },
            new[] { "settings", "설정", "RamClock 설정 열기" },
            new[] { "bday", "생일 달력", "우리 반 생일 달력 열기" },
            new[] { "pick", "뽑기", "학생 번호 뽑기" },
        };

        public static int Code(string name)
        {
            for (int i = 0; i < Tasks.Length; i++) if (Tasks[i][0] == name) return i + 1;
            return 0;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        static int msg;
        public static int Msg { get { if (msg == 0) { try { msg = RegisterWindowMessage("RamClock-Command"); } catch { msg = -1; } } return msg; } }

        // 이미 켜져 있는 RamClock에 명령 보내기
        public static void Send(int code)
        {
            try { PostMessage(new IntPtr(0xffff), Msg, new IntPtr(code), IntPtr.Zero); } catch { }
        }

        public static string CmdOf(string[] args)
        {
            int i = Array.IndexOf(args, "--cmd");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ---------- 점프 목록 만들기 (COM) ----------
        [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
            void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, [MarshalAs(UnmanagedType.Interface)] IObjectArray array);
            void AppendKnownCategory(int category);
            [PreserveSig] int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray array);
            [PreserveSig] int CommitList();
            void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string id);
            void AbortList();
        }

        [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IObjectArray
        {
            void GetCount(out uint n);
            void GetAt(uint i, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
        }

        [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IObjectCollection
        {
            void GetCount(out uint n);
            void GetAt(uint i, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
            void AddObject([MarshalAs(UnmanagedType.Interface)] object obj);
            void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray src);
            void RemoveObjectAt(uint i);
            void Clear();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder f, int cch, IntPtr fd, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder s, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder s, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder s, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
            void GetHotkey(out short k);
            void SetHotkey(short k);
            void GetShowCmd(out int c);
            void SetShowCmd(int c);
            void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder s, int cch, out int i);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, uint r);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Explicit)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out uint n);
            void GetAt(uint i, out PROPERTYKEY key);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT v);
            void SetValue(ref PROPERTYKEY key, ref PROPVARIANT v);
            void Commit();
        }

        static object Create(string clsid) { return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid))); }

        public static void Build()
        {
            if (Type.GetType("Mono.Runtime") != null) return;
            try
            {
                string exe = Application.ExecutablePath;
                var list = (ICustomDestinationList)Create("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
                uint slots; object removed; var iidArr = typeof(IObjectArray).GUID;
                if (list.BeginList(out slots, ref iidArr, out removed) < 0) return;
                var col = (IObjectCollection)Create("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
                foreach (var t in Tasks)
                {
                    var link = (IShellLinkW)Create("00021401-0000-0000-C000-000000000046");
                    link.SetPath(exe);
                    link.SetArguments("--cmd " + t[0]);
                    link.SetDescription(t[2]);
                    link.SetIconLocation(exe, 0);
                    var ps = (IPropertyStore)link;
                    var key = new PROPERTYKEY { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 };   // PKEY_Title
                    var pv = new PROPVARIANT { vt = 31, p = Marshal.StringToCoTaskMemUni(t[1]) };   // VT_LPWSTR
                    try { ps.SetValue(ref key, ref pv); ps.Commit(); }
                    finally { Marshal.FreeCoTaskMem(pv.p); }
                    col.AddObject(link);
                }
                list.AddUserTasks((IObjectArray)col);
                list.CommitList();
            }
            catch (Exception ex) { try { Program.Log(ex); } catch { } }
        }
    }
}
