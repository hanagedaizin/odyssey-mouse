// Odyssey Mouse - HID 入力 (Samsung Odyssey / Windows Mixed Reality モーションコントローラ) と
// マウス出力 (SendInput) のネイティブ層。
//
// PoC (tools/odyssey-mouse.ps1) で実機検証済みのコードをそのまま移植したもの。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OdysseyMouse
{
    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int Flags; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA x);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA x, IntPtr b, int bs, out int need, IntPtr d);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string n, uint a, uint sh, IntPtr sec, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int toWrite, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, IntPtr read, ref OVERLAPPED ov);

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal; public IntPtr InternalHigh;
            public uint Offset; public uint OffsetHigh; public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initialState, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, ref OVERLAPPED ov, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIo(SafeFileHandle h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_DATA { public ushort DataIndex; public ushort Reserved; public uint RawValue; }

        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetData(int t, [In, Out] HIDP_DATA[] d, ref uint n, IntPtr pp, byte[] report, uint len);

        // ---- SendInput ----
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public MOUSEINPUT mi; }
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint n, INPUT[] inputs, int size);

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_HWHEEL = 0x1000;

        // ---- 仮想デスクトップのサイズ (画面端でのクランプ用) ----
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
        public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    }

    /// <summary>
    /// 対象 HID デバイス (VID/PID 一致) を探して開き、レポートをデコードして読む。
    /// タイムアウト付きオーバーラップ I/O なので、読み取りで固まらない。
    /// </summary>
    internal sealed class HidDevice : IDisposable
    {
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ_WRITE = 3;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;
        public const int ERROR_TIMEOUT = 258;

        static readonly Guid HID_GUID = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

        SafeFileHandle _read, _write;
        IntPtr _pp = IntPtr.Zero;
        IntPtr _readEv = IntPtr.Zero;
        byte[] _buf;
        int _outLen = 64;
        string _path;
        readonly int _vid, _pid;

        public readonly Dictionary<int, uint> Vals = new Dictionary<int, uint>(24);
        public int LastReadErr { get; private set; }
        public bool CanWrite { get; private set; }
        public string Path { get { return _path; } }
        public int OutputReportLength { get { return _outLen; } }

        public HidDevice(int vid, int pid) { _vid = vid; _pid = pid; }

        public static bool MatchesPath(string path, int vid, int pid)
        {
            // Bluetooth HID のパスは "vid&0002045e_pid&065d#..." のように先頭に "0002" が付くため、
            // 全体を数値化せず「末尾4桁」で判定する。
            if (path == null) return false;
            string lp = path.ToLowerInvariant();
            int a = lp.IndexOf("vid&"); if (a < 0) return false;
            int c = lp.IndexOf("pid&", a); if (c < 0) return false;
            string vs = lp.Substring(a + 4, c - a - 4);
            int us = vs.IndexOf('_'); if (us >= 0) vs = vs.Substring(0, us);
            string ps = lp.Substring(c + 4);
            int hs = ps.IndexOf('#'); if (hs >= 0) ps = ps.Substring(0, hs);
            return vs.EndsWith(vid.ToString("x4")) && ps.EndsWith(pid.ToString("x4"));
        }

        public static string FindPath(int vid, int pid)
        {
            Guid guid = HID_GUID;
            IntPtr s = Win32.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (s == new IntPtr(-1)) return null;
            try
            {
                var x = new Win32.SP_DEVICE_INTERFACE_DATA();
                x.cbSize = Marshal.SizeOf(typeof(Win32.SP_DEVICE_INTERFACE_DATA));
                for (int i = 0; ; i++)
                {
                    if (!Win32.SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref guid, i, ref x)) break;
                    int need;
                    Win32.SetupDiGetDeviceInterfaceDetail(s, ref x, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need <= 0) continue;
                    IntPtr b = Marshal.AllocHGlobal(need);
                    string p = null;
                    try
                    {
                        Marshal.WriteInt32(b, 0, IntPtr.Size == 8 ? 8 : 6);
                        if (Win32.SetupDiGetDeviceInterfaceDetail(s, ref x, b, need, out need, IntPtr.Zero))
                            p = Marshal.PtrToStringAuto(new IntPtr(b.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(b); }
                    if (p != null && MatchesPath(p, vid, pid)) return p;
                }
            }
            finally { Win32.SetupDiDestroyDeviceInfoList(s); }
            return null;
        }

        public static bool IsPresent(int vid, int pid) { return FindPath(vid, pid) != null; }

        public bool Open()
        {
            var path = FindPath(_vid, _pid);
            if (path == null) return false;
            return Open(path);
        }

        public bool Open(string path)
        {
            _path = path;
            return OpenInternal();
        }

        /// <summary>切断(スリープ/圏外)からの復旧。ハンドルを捨てて再探索し、見つかれば開き直す。</summary>
        public bool Reconnect()
        {
            ReleaseHandles();
            var p = FindPath(_vid, _pid);
            if (p == null) return false;
            _path = p;
            return OpenInternal();
        }

        bool OpenInternal()
        {
            // 読み取り用と書き込み用(振動)でハンドルを分ける。同一ハンドルだと Bluetooth への
            // 出力書き込みが読み取りをブロックすることがある。
            _read = Win32.CreateFile(_path, GENERIC_READ, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (_read.IsInvalid) { _read = null; return false; }
            _readEv = Win32.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            _write = Win32.CreateFile(_path, GENERIC_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            CanWrite = !_write.IsInvalid;
            if (!Win32.HidD_GetPreparsedData(_read, out _pp)) { ReleaseHandles(); return false; }
            var caps = new Win32.HIDP_CAPS();
            caps.Reserved = new ushort[17];
            Win32.HidP_GetCaps(_pp, ref caps);
            _buf = new byte[Math.Max((int)caps.InputReportByteLength, 64) + 8];
            _outLen = (int)caps.OutputReportByteLength;
            if (_outLen <= 0) _outLen = 64;
            return true;
        }

        void ReleaseHandles()
        {
            if (_pp != IntPtr.Zero) { Win32.HidD_FreePreparsedData(_pp); _pp = IntPtr.Zero; }
            if (_read != null) { try { _read.Dispose(); } catch { } _read = null; }
            if (_write != null) { try { _write.Dispose(); } catch { } _write = null; }
            if (_readEv != IntPtr.Zero) { Win32.CloseHandle(_readEv); _readEv = IntPtr.Zero; }
            CanWrite = false;
        }

        public void Dispose() { ReleaseHandles(); }

        public bool IsOpen { get { return _read != null && !_read.IsInvalid; } }

        /// <summary>タイムアウト付き読み取り。データが無ければ 0 を返す（ループが固まらない）。</summary>
        public int Read(int timeoutMs)
        {
            Vals.Clear();
            if (_read == null || _read.IsInvalid || _readEv == IntPtr.Zero) return 0;
            Win32.ResetEvent(_readEv);
            var ov = new Win32.OVERLAPPED();
            ov.hEvent = _readEv;
            int r = 0;
            if (!Win32.ReadFile(_read, _buf, _buf.Length, IntPtr.Zero, ref ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING) { LastReadErr = err; return 0; }
                uint w = Win32.WaitForSingleObject(_readEv, (uint)timeoutMs);
                if (w != 0)
                {
                    Win32.CancelIo(_read);
                    Win32.WaitForSingleObject(_readEv, 200);
                    Win32.ResetEvent(_readEv);
                    LastReadErr = ERROR_TIMEOUT;
                    return 0;
                }
            }
            if (!Win32.GetOverlappedResult(_read, ref ov, out r, false)) { LastReadErr = Marshal.GetLastWin32Error(); r = 0; }
            else LastReadErr = 0;
            if (r <= 0) return 0;
            uint n = 512;
            var d = new Win32.HIDP_DATA[512];
            if (Win32.HidP_GetData(0, d, ref n, _pp, _buf, (uint)r) >= 0)
                for (uint i = 0; i < n; i++) Vals[d[i].DataIndex] = d[i].RawValue;
            return r;
        }

        public int ReportID { get { return (_buf == null) ? 0 : (int)_buf[0]; } }
        public bool Has(int di) { return Vals.ContainsKey(di); }
        public uint Get(int di) { uint v; return Vals.TryGetValue(di, out v) ? v : 0u; }

        static int _hapBusy;

        /// <summary>出力 ReportID 4: Haptics/0x23 + Haptics/0x21 (各 0..100)。
        /// Bluetooth HID では HidD_SetOutputReport が使えない(ERROR_NOT_SUPPORTED)ため WriteFile を使う。</summary>
        public int SendHaptics(int ch1, int ch2)
        {
            if (_write == null || _write.IsInvalid || !CanWrite) return -1;
            int len = _outLen > 0 ? _outLen : 64;
            var buf = new byte[len];
            buf[0] = 4;
            byte a = (byte)Math.Max(0, Math.Min(100, ch1));
            byte b = (byte)Math.Max(0, Math.Min(100, ch2));
            if (len > 1) buf[1] = a;
            if (len > 2) buf[2] = b;
            int written;
            bool ok = Win32.WriteFile(_write, buf, buf.Length, out written, IntPtr.Zero);
            if (!ok)
            {
                var small = new byte[] { 4, a, b };
                ok = Win32.WriteFile(_write, small, small.Length, out written, IntPtr.Zero);
            }
            if (!ok) ok = Win32.HidD_SetOutputReport(_write, buf, buf.Length);
            return ok ? 0 : Marshal.GetLastWin32Error();
        }

        /// <summary>ブロッキングしない振動パルス。Bluetooth の WriteFile は数百 ms 止まることがあるため別スレッドで送る。</summary>
        public void HapticPulse(int ch1, int ch2, int ms)
        {
            if (_write == null || _write.IsInvalid || !CanWrite) return;
            if (System.Threading.Interlocked.CompareExchange(ref _hapBusy, 1, 0) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SendHaptics(ch1, ch2);
                    if (ms > 0) System.Threading.Thread.Sleep(ms);
                    SendHaptics(0, 0);
                }
                finally { System.Threading.Interlocked.Exchange(ref _hapBusy, 0); }
            });
        }

        public void HapticsOff() { if (CanWrite) SendHaptics(0, 0); }
    }

    /// <summary>SendInput によるマウス出力。</summary>
    internal static class MouseOut
    {
        public static int LastSendCount;
        public static int LastSendErr;
        public static long LastClickMs;

        public static void Send(uint flags, int dx, int dy, int data)
        {
            var inp = new Win32.INPUT[1];
            inp[0].type = Win32.INPUT_MOUSE;
            inp[0].mi.dx = dx; inp[0].mi.dy = dy;
            inp[0].mi.mouseData = (uint)data;
            inp[0].mi.dwFlags = flags;
            LastSendCount = (int)Win32.SendInput(1, inp, Marshal.SizeOf(typeof(Win32.INPUT)));
            LastSendErr = (LastSendCount == 0) ? Marshal.GetLastWin32Error() : 0;
        }

        /// <summary>DOWN と UP を 1 回の SendInput で送る（ボタンが刺さるのを防ぐ）。</summary>
        public static void ClickLeft()
        {
            var inp = new Win32.INPUT[2];
            inp[0].type = Win32.INPUT_MOUSE; inp[0].mi.dwFlags = Win32.MOUSEEVENTF_LEFTDOWN;
            inp[1].type = Win32.INPUT_MOUSE; inp[1].mi.dwFlags = Win32.MOUSEEVENTF_LEFTUP;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LastSendCount = (int)Win32.SendInput(2, inp, Marshal.SizeOf(typeof(Win32.INPUT)));
            LastSendErr = (LastSendCount == 0) ? Marshal.GetLastWin32Error() : 0;
            sw.Stop();
            LastClickMs = sw.ElapsedMilliseconds;
        }

        public static void Wheel(int notches, bool horizontal)
        {
            if (notches == 0) return;
            Send(horizontal ? Win32.MOUSEEVENTF_HWHEEL : Win32.MOUSEEVENTF_WHEEL, 0, 0, notches * 120);
        }

        /// <summary>カーソルが画面外へ出ないようにクランプする（仮想デスクトップ全体）。</summary>
        public static void ClampCursor()
        {
            Win32.POINT p;
            if (!Win32.GetCursorPos(out p)) return;
            int x0 = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int y0 = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int w = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
            int h = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) { w = Win32.GetSystemMetrics(Win32.SM_CXSCREEN); h = Win32.GetSystemMetrics(Win32.SM_CYSCREEN); x0 = 0; y0 = 0; }
            int nx = p.X, ny = p.Y;
            if (nx < x0) nx = x0; if (nx > x0 + w - 1) nx = x0 + w - 1;
            if (ny < y0) ny = y0; if (ny > y0 + h - 1) ny = y0 + h - 1;
            if (nx != p.X || ny != p.Y) Win32.SetCursorPos(nx, ny);
        }
    }
}
