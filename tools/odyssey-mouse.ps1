#requires -Version 5.1
<#
  odyssey-mouse.ps1 - Samsung Odyssey (WMR) モーションコントローラをマウス代わりに使う PoC

  入力: HID デバイス 045E:065D を ReadFile で直読み -> HidP_GetData でデコード
  出力: SendInput でマウスイベントを注入

  既定のマッピング:
    スティック (idx8=X / idx7=Y)                 : カーソル移動（相対・加速カーブ）
    タッチパッド (idx11=X / idx10=Y, idx6=接触)   : カーソル移動（相対・速度で加速）
    タッチパッドのタップ（短く触れて離す）          : 左クリック
    タッチパッドクリック (idx4)                    : 左ボタン（押下中はドラッグ）
    アナログトリガー (idx9)                        : 左ボタン（しきい値超え=押下）※ドラッグ用
    グリップ (idx0)                               : 右ボタン
    スティック押込 (idx3)                          : 中ボタン
    メニュー (idx1)                               : スクロールモード切替（トグル）
        スクロールモード中: スティックY=ホイール / スティックX=横ホイール
    Windows (idx2)                                : 押している間だけ速度ブースト
    クリック時の振動                               : 出力ReportID4（**既定OFF / -HapticsStrength**）

  ※ 重要: このコントローラ(Bluetooth Classic HID)は、セッション中に
     **Windows から勝手に切断される**ことがある（省電力/スリープ/圏外）。
     そのとき読み取りは err=258(タイムアウト)→err=1167(未接続) で失敗する。
     対策として、切断を検出すると 1 秒ごとにデバイスを再探索して
     復帰したら自動で開き直す（**再接続**）ようにしてある。
     Windows のイベントログに `HidBth` ID 2「Bluetooth HID デバイスは圏外に
     なったか利用できなくなりました」が出ていれば、これが原因。
     頻発する場合はコントローラの電池(単4×2)を新品に交換すると改善する。

  ※ 振動: 出力レポートを送ると入力が止まる事例が確認されているため既定OFF。
     `-HapticsStrength 60` で有効化、`-TestHaptics` で振動のみ単体テスト可能。

  使い方:
    powershell -ExecutionPolicy Bypass -File tools\odyssey-mouse.ps1                 # Ctrl+C で終了
    powershell -ExecutionPolicy Bypass -File tools\odyssey-mouse.ps1 -Seconds 60
    powershell -ExecutionPolicy Bypass -File tools\odyssey-mouse.ps1 -DryRun         # 動かさず値だけ表示
    powershell -ExecutionPolicy Bypass -File tools\odyssey-mouse.ps1 -StickSensPx 1600 -StickDeadzone 300
    powershell -ExecutionPolicy Bypass -File tools\odyssey-mouse.ps1 -InvertY:$false # 上下を逆にしたい場合

  メモ: Y 軸は実機の向きに合わせて既定で反転（スティック・タッチパッド共通）。
        起動時にスティック中心を自動校准する（実機の静止値は 2048 からずれる）。
#>
[CmdletBinding()]
param(
    [int]$VendorId = 0x045E,
    [int]$ProductId = 0x065D,
    [int]$Seconds = 0,
    [double]$StickSensPx = 1200,
    [int]$StickDeadzone = 350,
    [double]$StickExponent = 2.0,
    [double]$PadGain = 2.4,
    [double]$WheelRate = 15.0,
    [int]$TriggerThreshold = 100,
    [double]$BoostFactor = 2.5,
    [double]$PadAccelMax = 3.5,
    [double]$PadAccelRef = 1100,
    [double]$PadFollowTau = 0.0,
    [double]$PadSmoothTau = 0.004,
    [double]$PadAccelTau = 0.040,
    [double]$PadJumpMax = 6.0,
    [double]$PadSnapEnd = 30.0,
    [double]$PadRestBand = 4.0,
    [double]$TapMinSec = 0.04,
    [int]$PadNeutralX = -1,
    [int]$PadNeutralY = -1,
    [double]$PadStepMax = 80,
    [switch]$NoTapClick,
    [switch]$TestHaptics,
    [switch]$Trace,
    [switch]$TracePad,
    [string]$PadCsv = '',
    [double]$TapMaxSec = 0.25,
    [double]$TapMaxMove = 12,
    [int]$HapticsStrength = 0,
    [int]$HapticsMs = 50,
    [int]$CenterSamples = 40,
    [int]$StickCenterX = 0,
    [int]$StickCenterY = 0,
    [switch]$NoAutoCenter,
    [bool]$InvertY = $true,
    [switch]$NoStick,
    [switch]$NoTouchpad,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidMouse
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int Flags; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet=CharSet.Auto, SetLastError=true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
    [DllImport("setupapi.dll", CharSet=CharSet.Auto, SetLastError=true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA x);
    [DllImport("setupapi.dll", CharSet=CharSet.Auto, SetLastError=true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA x, IntPtr b, int bs, out int need, IntPtr d);
    [DllImport("setupapi.dll", SetLastError=true)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    [DllImport("kernel32.dll", CharSet=CharSet.Auto, SetLastError=true)]
    public static extern SafeFileHandle CreateFile(string n, uint a, uint sh, IntPtr sec, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, out int read, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int toWrite, out int written, IntPtr ov);

    [StructLayout(LayoutKind.Sequential)] public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_DATA { public ushort DataIndex; public ushort Reserved; public uint RawValue; }
    [StructLayout(LayoutKind.Sequential)] public struct OVERLAPPED
    {
        public IntPtr Internal; public IntPtr InternalHigh;
        public uint Offset; public uint OffsetHigh; public IntPtr hEvent;
    }

    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initialState, IntPtr name);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetOverlappedResult(SafeFileHandle h, ref OVERLAPPED ov, out int transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool CancelIo(SafeFileHandle h);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ResetEvent(IntPtr h);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, IntPtr read, ref OVERLAPPED ov);

    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetData(int t, [In,Out] HIDP_DATA[] d, ref uint n, IntPtr pp, byte[] report, uint len);

    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    public const uint INPUT_MOUSE   = 0;
    public const uint MOUSEEVENTF_MOVE       = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    public const uint MOUSEEVENTF_WHEEL      = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL     = 0x1000;

    static SafeFileHandle H; static SafeFileHandle HW; static IntPtr PP; static byte[] Buf; static int OutLen;
    static IntPtr _readEv = IntPtr.Zero;
    public static int LastReadErr = 0;
    public static bool CanWrite = false;
    public static int OutLenPublic { get { return OutLen; } }
    public static Dictionary<int,uint> Vals = new Dictionary<int,uint>();

    static bool MatchPath(string path, int vid, int pid)
    {
        // Bluetooth HID のパスは "vid&0002045e_pid&065d#..." のように
        // 先頭に "0002" が付くため、全体を数値化せず「末尾4桁」で判定する。
        string lp = path.ToLower();
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
        Guid g = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (s == new IntPtr(-1)) return null;
        try {
            var x = new SP_DEVICE_INTERFACE_DATA(); x.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (int i = 0; ; i++) {
                if (!SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref g, i, ref x)) break;
                int need; SetupDiGetDeviceInterfaceDetail(s, ref x, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr b = Marshal.AllocHGlobal(need);
                string p = null;
                try {
                    Marshal.WriteInt32(b, 0, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(s, ref x, b, need, out need, IntPtr.Zero))
                        p = Marshal.PtrToStringAuto(new IntPtr(b.ToInt64() + 4));
                } finally { Marshal.FreeHGlobal(b); }
                if (p != null && MatchPath(p, vid, pid)) return p;
            }
        } finally { SetupDiDestroyDeviceInfoList(s); }
        return null;
    }

    static string _path = null;

    public static bool Open(string path)
    {
        _path = path;
        return OpenInternal();
    }

    // デバイスが切断(スリープ/圏外)したときの復旧用。
    // ハンドルを捨ててデバイスを再探索し、見つかれば開き直す。
    public static bool Reconnect(int vid, int pid)
    {
        if (H != null && !H.IsInvalid) H.Dispose();
        if (HW != null && !HW.IsInvalid) HW.Dispose();
        if (_readEv != IntPtr.Zero) { CloseHandle(_readEv); _readEv = IntPtr.Zero; }
        if (PP != IntPtr.Zero) { HidD_FreePreparsedData(PP); PP = IntPtr.Zero; }
        H = null; HW = null;
        string p = FindPath(vid, pid);
        if (p == null) return false;
        _path = p;
        return OpenInternal();
    }

    static bool OpenInternal()
    {
        // 読み取り用と書き込み用(振動)でハンドルを分ける。
        // 同一ハンドルだと Bluetooth への出力書き込みが読み取りをブロックすることがある。
        // 読み取りはタイムアウト付き(オーバーラップ I/O)にして、固まらないようにする。
        H = CreateFile(_path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);   // GENERIC_READ | FILE_FLAG_OVERLAPPED
        if (H.IsInvalid) return false;
        _readEv = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        HW = CreateFile(_path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);  // GENERIC_WRITE
        CanWrite = !HW.IsInvalid;
        if (!HidD_GetPreparsedData(H, out PP)) return false;
        var caps = new HIDP_CAPS(); caps.Reserved = new ushort[17];
        HidP_GetCaps(PP, ref caps);
        Buf = new byte[Math.Max((int)caps.InputReportByteLength, 64) + 8];
        OutLen = (int)caps.OutputReportByteLength;
        if (OutLen <= 0) OutLen = 64;
        return true;
    }

    public static void Close()
    {
        if (PP != IntPtr.Zero) { HidD_FreePreparsedData(PP); PP = IntPtr.Zero; }
        if (H != null && !H.IsInvalid) H.Dispose();
        if (HW != null && !HW.IsInvalid) HW.Dispose();
        if (_readEv != IntPtr.Zero) { CloseHandle(_readEv); _readEv = IntPtr.Zero; }
        H = null; HW = null;
    }

    // タイムアウト付き読み取り。データが無ければ 0 を返す（ループが固まらない）。
    public static int Read(int timeoutMs)
    {
        Vals.Clear();
        if (H == null || H.IsInvalid || _readEv == IntPtr.Zero) return 0;
        ResetEvent(_readEv);
        var ov = new OVERLAPPED();
        ov.hEvent = _readEv;
        int r = 0;
        if (!ReadFile(H, Buf, Buf.Length, IntPtr.Zero, ref ov))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 997) { LastReadErr = err; return 0; }   // 997 = ERROR_IO_PENDING
            uint w = WaitForSingleObject(_readEv, (uint)timeoutMs);
            if (w != 0)
            {
                CancelIo(H);
                WaitForSingleObject(_readEv, 500);
                ResetEvent(_readEv);
                LastReadErr = 258;                              // 258 = WAIT_TIMEOUT
                return 0;
            }
        }
        if (!GetOverlappedResult(H, ref ov, out r, false)) { LastReadErr = Marshal.GetLastWin32Error(); r = 0; }
        else LastReadErr = 0;
        if (r <= 0) return 0;
        uint n = 512;
        var d = new HIDP_DATA[512];
        if (HidP_GetData(0, d, ref n, PP, Buf, (uint)r) >= 0)
            for (uint i = 0; i < n; i++) Vals[d[i].DataIndex] = d[i].RawValue;
        return r;
    }

    public static int ReportID { get { return (Buf == null) ? 0 : (int)Buf[0]; } }
    public static bool Has(int di) { return Vals.ContainsKey(di); }
    public static uint Get(int di) { uint v; return Vals.TryGetValue(di, out v) ? v : 0u; }

    public static uint LastSendCount = 0;
    public static int LastSendErr = 0;
    public static long LastClickMs = 0;

    public static void Mouse(uint flags, int dx, int dy, int wheel)
    {
        var inp = new INPUT[1];
        inp[0].type = INPUT_MOUSE;
        inp[0].mi.dx = dx; inp[0].mi.dy = dy;
        inp[0].mi.mouseData = (uint)wheel;
        inp[0].mi.dwFlags = flags;
        LastSendCount = SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        LastSendErr = (LastSendCount == 0) ? Marshal.GetLastWin32Error() : 0;
    }

    // DOWN と UP を 1 回の SendInput で送る（ボタンが刺さるのを防ぐ）
    public static void ClickLeft()
    {
        var inp = new INPUT[2];
        inp[0].type = INPUT_MOUSE; inp[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        inp[1].type = INPUT_MOUSE; inp[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LastSendCount = SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        LastSendErr = (LastSendCount == 0) ? Marshal.GetLastWin32Error() : 0;
        sw.Stop();
        LastClickMs = sw.ElapsedMilliseconds;
    }

    public static int LastErr = 0;
    static int _hapBusy = 0;

    // 出力 ReportID 4: Haptics/0x23 + Haptics/0x21 (各 0..100) を送る
    // Bluetooth HID では HidD_SetOutputReport が使えない(ERROR_NOT_SUPPORTED)ため WriteFile を使う。
    static bool WriteReport(int ch1, int ch2)
    {
        if (HW == null || HW.IsInvalid || !CanWrite) { LastErr = -1; return false; }
        int len = (OutLen > 0) ? OutLen : 64;
        var buf = new byte[len];
        buf[0] = 4;
        byte a = (byte)Math.Max(0, Math.Min(100, ch1));
        byte b = (byte)Math.Max(0, Math.Min(100, ch2));
        if (len > 1) buf[1] = a;
        if (len > 2) buf[2] = b;
        int written;
        bool ok = WriteFile(HW, buf, buf.Length, out written, IntPtr.Zero);
        if (!ok) {
            var small = new byte[] { 4, a, b };
            ok = WriteFile(HW, small, small.Length, out written, IntPtr.Zero);
        }
        if (!ok) ok = HidD_SetOutputReport(HW, buf, buf.Length);
        LastErr = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    public static bool SetHaptics(int ch1, int ch2) { return WriteReport(ch1, ch2); }

    // ブロッキングしない振動パルス。Bluetooth の WriteFile は数百 ms 止まることがあるため、
    // 専用スレッドで送信し、読み取りループを止めない。
    public static void HapticPulse(int ch1, int ch2, int ms)
    {
        if (H == null || H.IsInvalid || !CanWrite) return;
        if (System.Threading.Interlocked.CompareExchange(ref _hapBusy, 1, 0) != 0) return;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object st) {
            try {
                WriteReport(ch1, ch2);
                if (ms > 0) System.Threading.Thread.Sleep(ms);
                WriteReport(0, 0);
            } finally { _hapBusy = 0; }
        });
    }
}
'@
if (-not ('HidMouse' -as [type])) { Add-Type -TypeDefinition $cs -Language CSharp }

$path = [HidMouse]::FindPath($VendorId, $ProductId)
if (-not $path) { Write-Host "対象 HID デバイス (VID_$('{0:X4}' -f $VendorId) PID_$('{0:X4}' -f $ProductId)) が見つかりません"; exit 1 }
if (-not [HidMouse]::Open($path)) { Write-Host "デバイスを開けません: $path"; exit 1 }

Write-Host "=== Odyssey Mouse (PoC) ==="
Write-Host "  device : $path"
Write-Host "  stick  : sens=${StickSensPx}px/s deadzone=${StickDeadzone}/2048 exp=${StickExponent} invertY=$([bool]$InvertY)"
Write-Host ("  pad    : gain={0}px/unit smoothTau={1}s followTau={2}s accelTau={10}s accelMax={3}/{4} jumpMax={5} snapEnd={6} restBand={11} tap={7}s/{8}unit ok={9}" -f $PadGain, $PadSmoothTau, $PadFollowTau, $PadAccelMax, $PadAccelRef, $PadJumpMax, $PadSnapEnd, $TapMaxSec, $TapMaxMove, (-not $NoTapClick), $PadAccelTau, $PadRestBand)
Write-Host ("  wheel  : {0}notch/s  trigger>={1} => 左ボタン  haptics={2}" -f $WheelRate, $TriggerThreshold, $(if ($HapticsStrength -gt 0) { "$HapticsStrength/$($HapticsMs)ms" } else { 'OFF' }))
Write-Host "  操作   : スティック=移動 / タッチパッド=移動 / パッド押込(idx4)=左 / トリガー=左 / グリップ(idx0)=右 / スティック押込(idx3)=中"
Write-Host "           メニュー(idx1)=スクロールモード切替 / Windows(idx2)=速度ブースト"
if ($DryRun) { Write-Host "  *** DryRun: カーソルは動かしません ***" }
Write-Host "  終了   : Ctrl+C"
Write-Host ""

# ---- 振動テストモード ----
if ($TestHaptics) {
    Write-Host ("  出力レポート長={0}B 書き込み可={1}" -f [HidMouse]::OutLenPublic, [HidMouse]::CanWrite)
    Write-Host "  振動テスト: 3 回パルスします (${HapticsStrength}/100, ${HapticsMs}ms)"
    $pulses = @(
        @{ n = 'ch A (Haptics/0x23)'; a = $HapticsStrength; b = 0 },
        @{ n = 'ch B (Haptics/0x21)'; a = 0; b = $HapticsStrength },
        @{ n = 'A + B'; a = $HapticsStrength; b = $HapticsStrength }
    )
    $i = 0
    foreach ($pl in $pulses) {
        $i++
        $ok = [HidMouse]::SetHaptics($pl.a, $pl.b)
        Write-Host ("   pulse {0}: {1} -> {2} (err={3})" -f $i, $pl.n, $ok, [HidMouse]::LastErr)
        Start-Sleep -Milliseconds ([Math]::Max(50, $HapticsMs))
        [void][HidMouse]::SetHaptics(0, 0)
        Start-Sleep -Milliseconds 400
    }
    [HidMouse]::Close()
    exit 0
}

# ---- スティック中心の確定（実機は静止時の値が 2048 からずれるため自動校准する） ----
$cx = 2048.0; $cy = 2048.0
if ($StickCenterX -ne 0 -or $StickCenterY -ne 0) {
    $cx = [double]$StickCenterX; $cy = [double]$StickCenterY
    Write-Host ("  中心(手動): X={0} Y={1}" -f $cx, $cy)
} elseif (-not $NoAutoCenter) {
    Write-Host "  スティック中心とパッドのニュートラル値を自動計測中... (スティック/タッチパッドに触れないでください)"
    $sxsum = 0.0; $sysum = 0.0; $cn = 0
    $padXs = New-Object System.Collections.ArrayList
    $padYs = New-Object System.Collections.ArrayList
    $ctlim = (Get-Date).AddSeconds(3)
    # パッドは「指を離している間は 255 を返す」ことがあり、実値のサンプルが
    # なかなか出ない。スティック中心(40サンプル)だけでは 0.2 秒で終わってしまうため、
    # パッドのサンプルが1つ取れるまで続ける。ただし 255 が 0.5 秒続いたら
    # 「未接触時に 255 を返す実機」と判断して打ち切る（休止値は不要）。
    $pad255Run = 0
    while ((Get-Date) -lt $ctlim) {
        if ($cn -ge $CenterSamples -and $padXs.Count -gt 0) { break }
        if ($pad255Run -ge 100) { break }
        $l = [HidMouse]::Read(250)
        if ($l -le 0) { Start-Sleep -Milliseconds 1; continue }
        if ([HidMouse]::ReportID -ne 1) { continue }
        if ($cn -lt $CenterSamples) {
            $sxsum += [int][HidMouse]::Get(8); $sysum += [int][HidMouse]::Get(7); $cn++
        }
        $px = [int][HidMouse]::Get(11); $py = [int][HidMouse]::Get(10)
        if ($px -ne 255 -and $py -ne 255) { [void]$padXs.Add($px); [void]$padYs.Add($py); $pad255Run = 0 }
        else { $pad255Run++ }
    }
    if ($cn -gt 0) { $cx = $sxsum / $cn; $cy = $sysum / $cn }
    Write-Host ("  中心(自動): X={0:F1} Y={1:F1} (n={2})" -f $cx, $cy, $cn)
}

$deadline = if ($Seconds -gt 0) { (Get-Date).AddSeconds($Seconds) } else { [datetime]::MaxValue }
$started = Get-Date

# ---- タッチパッドの休止値 ----
# 実測(2026-10): この実機は
#   ・指を動かしている間は指の絶対位置(0..100)を連続して報告する
#   ・指を離すと 255,255 を返す。または指を置いたまま静止すると「休止値」
#     (実測 (49,50)(50,51)(50,53)(56,65)(57,64)(58,63)) を一定に報告し続ける
#   ・静止中は「実値2フレーム + 255 が約200ms」を繰り返すことがある(実測 pad-v5)
# 255 を返す実機では休止値そのものが不要なので、取得できなければ無効扱いにして
# toRest 条件を外し、255 と瞬間ジャンプだけで判定する。
$nX = 0.0; $nY = 0.0
$padRestValid = $false
if ($PadNeutralX -ge 0 -and $PadNeutralY -ge 0) {
    $nX = [double]$PadNeutralX; $nY = [double]$PadNeutralY
    $padRestValid = $true
    Write-Host ("  パッド静止値(手動指定): ({0},{1})" -f $nX, $nY)
} elseif ($padXs -and $padXs.Count -gt 0) {
    $freq = @{}
    for ($i = 0; $i -lt $padXs.Count; $i++) {
        $k = "{0},{1}" -f $padXs[$i], $padYs[$i]
        if ($freq.ContainsKey($k)) { $freq[$k]++ } else { $freq[$k] = 1 }
    }
    $best = $freq.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
    $parts = $best.Key.Split(',')
    $nX = [double]$parts[0]; $nY = [double]$parts[1]
    $padRestValid = $true
    Write-Host ("  パッド静止値(参考): ({0},{1}) 一致={2}/{3} サンプル" -f $nX, $nY, $best.Value, $padXs.Count)
} else {
    Write-Host "  パッド静止値: 取得できず (未接触時に 255 を返す実機のため)。255 と瞬間ジャンプで判定します。"
}
$padStillSince = Get-Date
$prevOnNeutral = $true
$restX = $nX; $restY = $nY
$padPrevX = -1; $padPrevY = -1; $padMovedPrev = $false
$padSession = $false
$padPending = $false; $pendBigJump = $false
$jump = 0; $toRest = 999.0; $padDown = $false; $padUp = $false; $padNoTouch = $false; $padDiscard = $false

$accX = 0.0; $accY = 0.0; $wheelAcc = 0.0; $hwheelAcc = 0.0
$lastT = Get-Date
$prevLeft = $false; $prevRight = $false; $prevMid = $false; $prevMenu = $false
$scrollMode = $false
$padPX = $null; $padPY = $null
$padFX = [double]$restX; $padFY = [double]$restY
$padPXf = $padFX; $padPYf = $padFY
$padRemX = 0.0; $padRemY = 0.0; $padSpeedEma = 0.0
$tapT0 = [datetime]::MinValue; $tapX0 = 0; $tapY0 = 0; $tapMoved = $false; $tapClicked = $false
$hbLast = $started; $hbReports = 0; $gapMax = 0.0
$lastPadPrint = [datetime]::MinValue
$dzN = $StickDeadzone / 2048.0
$nReports = 0
$lastPrint = [datetime]::MinValue
$sentX = 0; $sentY = 0; $cumX = 0; $cumY = 0

$padX = 0; $padY = 0; $bit6 = 0; $onNeutral = $true; $padActive = $false

# パッド生値＋実送信移動量のログ（毎フレーム）: 指の動きとカーソルの動きを直接比較するため
$padCsvW = $null
if ($PadCsv) {
    $padCsvW = New-Object IO.StreamWriter($PadCsv, $false, (New-Object Text.UTF8Encoding($false)))
    $padCsvW.WriteLine('el,rawX,rawY,bit6,noTouch,active,down,up,jump,toRest,restX,restY,filtX,filtY,sentX,sentY,cumX,cumY,remX,remY,pend')
}

$MOVE = [HidMouse]::MOUSEEVENTF_MOVE
$LDOWN = [HidMouse]::MOUSEEVENTF_LEFTDOWN;  $LUP = [HidMouse]::MOUSEEVENTF_LEFTUP
$RDOWN = [HidMouse]::MOUSEEVENTF_RIGHTDOWN; $RUP = [HidMouse]::MOUSEEVENTF_RIGHTUP
$MDOWN = [HidMouse]::MOUSEEVENTF_MIDDLEDOWN; $MUP = [HidMouse]::MOUSEEVENTF_MIDDLEUP
$WHEEL = [HidMouse]::MOUSEEVENTF_WHEEL; $HWHEEL = [HidMouse]::MOUSEEVENTF_HWHEEL

try {
    $readFail = 0
    $hbFail = 0
    $failStreak = 0
    $linkLost = $false
    $lastReconnect = [datetime]::MinValue
    $reopens = 0
    $padTouched = $false
    while ((Get-Date) -lt $deadline) {
        # ---- 診断ハートビート（読み取りの前に出すので、読み取りが失敗しても必ず毎秒残る） ----
        if ($Trace) {
            $nowH = Get-Date
            if (($nowH - $hbLast).TotalSeconds -ge 1.0) {
                Write-Host ("  [{0,7:F2}s] ALIVE rep/s={1,-4} gapMax={2:F3}s err={3} rdFail={4} reop={5} snd={6}/{7} padTouch={8,-5} L={9} R={10} M={11} scroll={12}" -f `
                    ($nowH - $started).TotalSeconds, ($nReports - $hbReports), $gapMax, [HidMouse]::LastReadErr, ($readFail - $hbFail), $reopens, [HidMouse]::LastSendCount, [HidMouse]::LastSendErr, $padTouched, $prevLeft, $prevRight, $prevMid, $scrollMode)
                [Console]::Out.Flush()
                $hbLast = $nowH; $hbReports = $nReports; $hbFail = $readFail; $gapMax = 0.0
            }
        }

        $len = [HidMouse]::Read(250)
        if ($len -le 0) {
            $readFail++
            $failStreak++
            if ($Trace -and $readFail -le 6) {
                Write-Host ("  [{0,7:F2}s] READ FAIL #{1} err={2}" -f ((Get-Date) - $started).TotalSeconds, $readFail, [HidMouse]::LastReadErr)
                [Console]::Out.Flush()
            }
            # 4 回以上連続で失敗 = デバイス切断(スリープ/圏外)とみなす。
            # この実機はセッション中に自律的に切断することがあるため、
            # 1 秒ごとにデバイスを再探索して、復帰したら開き直す。
            if ($failStreak -ge 4) {
                if (-not $linkLost) {
                    $linkLost = $true
                    Write-Host ("  [{0,7:F2}s] ** 入力停止（デバイス切断？）err={1} → 再接続を試みます **" -f ((Get-Date) - $started).TotalSeconds, [HidMouse]::LastReadErr)
                    [Console]::Out.Flush()
                }
                if (((Get-Date) - $lastReconnect).TotalSeconds -ge 1.0) {
                    $lastReconnect = Get-Date
                    if ([HidMouse]::Reconnect($VendorId, $ProductId)) {
                        $reopens++
                        Write-Host ("  [{0,7:F2}s] ** デバイスを開き直しました（復帰待ち）**" -f ((Get-Date) - $started).TotalSeconds)
                        [Console]::Out.Flush()
                    }
                }
            }
            Start-Sleep -Milliseconds 5
            continue
        }
        $failStreak = 0
        if ($linkLost) {
            $linkLost = $false
            Write-Host ("  [{0,7:F2}s] ** 入力を再開しました **" -f ((Get-Date) - $started).TotalSeconds)
            [Console]::Out.Flush()
        }
        if ([HidMouse]::ReportID -ne 1) { continue }
        $nReports++

        $now = Get-Date
        $dt = ($now - $lastT).TotalSeconds
        $lastT = $now
        if ($dt -le 0) { $dt = 0.0065 }
        if ($dt -gt 0.1) { $dt = 0.1 }
        $el = ($now - $started).TotalSeconds
        if ($dt -gt $gapMax) { $gapMax = $dt }

        # ---- 入力読み取り ----
        $sx = [int][HidMouse]::Get(8); $sy = [int][HidMouse]::Get(7)
        $trig = [int][HidMouse]::Get(9)
        $padX = [int][HidMouse]::Get(11); $padY = [int][HidMouse]::Get(10)
        $bit6 = [int][HidMouse]::Has(6)
        $padTouched = ($bit6 -ne 0) -or ($padX -ne 255 -and $padY -ne 255)

        # ---- タッチパッドの休止値とタッチダウン/リフトの判定 ----
        # 実測(2026-10)で確定したこの実機の挙動:
        #   ・指が触れている間は指の絶対位置(0..100)を返す
        #   ・指を離すと、その時点の「休止値」(実測 (49,50)(50,51)(50,53) など) へ
        #     瞬間移動し、離している間はずっと同じ値を返し続ける(255 のこともある)
        #   ・休止値はセッション中に緩やかにドリフトする
        #   ・触れる/離す瞬間に 6〜31 単位の瞬間ジャンプが出る。これを捨てないと
        #     毎回 14〜74px が逆方向へ注入され「ゴムのように引き戻される」
        # 判定: 0.05 秒まったく変化しなければその値は休止値。そこから値を動かせば
        #       タッチダウン、休止値付近へ戻ればリフト。そのフレームの移動だけを捨てる。
        # 重要: 休止値の追従は「接触セッションが開いていないとき」に限る。指を止めた
        #       位置を休止値と誤認すると、離したときの戻りを検出できなくなる。
        $onNeutral = ($padX -eq 255 -or $padY -eq 255)
        $padSnap = $false
        # 0.05 秒まったく変化しない値 = 休止値（指が離れている）として追従する
        $padMoved = ($padX -ne $padPrevX) -or ($padY -ne $padPrevY)
        $jump = [Math]::Max([Math]::Abs($padX - $padPrevX), [Math]::Abs($padY - $padPrevY))
        $prevFrozen = -not $padMovedPrev
        $padStillSec = ($now - $padStillSince).TotalSeconds
        if ($padMoved) { $padStillSince = $now }
        elseif ((($now - $padStillSince).TotalSeconds -ge 0.05) -and $padX -ne 255 -and (-not $padSession)) {
            # 休止値の追従は「接触セッションが開いていないとき」だけ行う。
            # セッション中(=指が触れている)に追従すると、指を止めた位置を休止値と
            # 誤認してしまい、離したときの戻りを検出できなくなる。
            $restX = [double]$padX; $restY = [double]$padY; $padRestValid = $true
        }
        $toRest = 999.0
        if ($padRestValid) { $toRest = [Math]::Max([Math]::Abs($padX - $restX), [Math]::Abs($padY - $restY)) }

        # タッチダウン = 前フレームが休止なのに値が動いた
        # リフト候補   = 接触中に休止値付近へ戻った(または巨大ジャンプ)
        #   → 候補の段階では確定せず、次フレームで「休止値に留まっているか」を確認する。
        #     休止値を通過しただけの本物の移動を捨ててしまうのを防ぐ。
        $padNoTouch = ($padX -eq 255 -or $padY -eq 255)
        $padDown = (-not $padNoTouch) -and (-not $padSession) -and $prevFrozen -and ($jump -ge 3) -and ($padStillSec -ge 0.05)
        $padUpCand = (-not $padNoTouch) -and $padSession -and (-not $prevFrozen) -and ($jump -ge $PadJumpMax) -and `
                     (($padRestValid -and $toRest -le $PadRestBand) -or ($jump -ge $PadSnapEnd))
        $padUp = $false
        $padDiscard = $padNoTouch -or $padDown -or $padUpCand
        $padSnap = $padDiscard

        # ---- スティック -> 速度(px/s) ----
        $ax = 0.0; $ay = 0.0
        if (-not $NoStick) {
            $sxn = ($sx - $cx) / 2047.0
            $syn = ($sy - $cy) / 2047.0
            if ($InvertY) { $syn = -$syn }
            if ([Math]::Abs($sxn) -gt $dzN) { $t = ([Math]::Abs($sxn) - $dzN) / (1.0 - $dzN); $ax = [Math]::Sign($sxn) * [Math]::Pow($t, $StickExponent) }
            if ([Math]::Abs($syn) -gt $dzN) { $t = ([Math]::Abs($syn) - $dzN) / (1.0 - $dzN); $ay = [Math]::Sign($syn) * [Math]::Pow($t, $StickExponent) }
        }
        $vx = $ax * $StickSensPx
        $vy = $ay * $StickSensPx

        # ---- タッチパッド（相対） + タップ判定 ----
        if (-not $NoTouchpad) {
            $padAct = 'move'
            if ($padNoTouch) { $padAct = 'lift' }
            elseif ($padDown) { $padAct = 'down' }
            elseif ($padPending) {
                # 前フレームのリフト候補を確定する。休止値に留まっていれば本物のリフト、
                # 動き出していれば休止値を通過しただけの本物の移動。
                # 休止値が既知ならそこへ戻ったかで判定する。未知(未接触時に 255 を
                # 返す実機)なら、巨大ジャンプのあと値が止まったかで判定する。
                if (($padRestValid -and $toRest -le $PadRestBand) -or ($pendBigJump -and (-not $padMoved))) { $padAct = 'lift' }
                else { $padPending = $false; $padAct = 'move' }
            }
            elseif ($padUpCand) { $padPending = $true; $pendBigJump = ($jump -ge $PadSnapEnd); $padAct = 'pending' }

            if ($padAct -eq 'down') {
                # 指が触れた: 新しい接触セッションを開始し、位置をアンカーする（移動は出さない）
                $padSession = $true
                $tapT0 = $now; $tapX0 = $padX; $tapY0 = $padY; $tapMoved = $false; $tapClicked = [HidMouse]::Has(4)
                $padFX = [double]$padX; $padFY = [double]$padY
                $padPXf = $padFX; $padPYf = $padFY
                $padSpeedEma = 0.0
                $padActive = $true
                if ($TracePad) { Write-Host ("  [{0,7:F2}s] PAD DOWN ({1},{2}) jump={3}" -f $el, $padX, $padY, $jump) }
            } elseif ($padAct -eq 'lift') {
                # 指が離れた: タップ判定してセッションを閉じる
                if ($padSession -and (-not $NoTapClick) -and (-not $tapMoved) -and (-not $tapClicked) -and (-not $prevLeft)) {
                    $tapDur = ($now - $tapT0).TotalSeconds
                    if ($tapDur -ge $TapMinSec -and $tapDur -lt $TapMaxSec) {
                        if (-not $DryRun) {
                            [HidMouse]::ClickLeft()   # DOWN+UP を 1 回の SendInput で送る
                            if ($HapticsStrength -gt 0) { [HidMouse]::HapticPulse($HapticsStrength, $HapticsStrength, $HapticsMs) }
                        }
                        if ($Trace) {
                            Write-Host ("  [{0,7:F2}s] TAP -> click sent={1} err={2} {3}ms (hold {4:F3}s)" -f $el, [HidMouse]::LastSendCount, [HidMouse]::LastSendErr, [HidMouse]::LastClickMs, $tapDur)
                            [Console]::Out.Flush()
                        }
                    }
                }
                $padSession = $false
                $padPending = $false
                $padUp = $true
                if (-not $padNoTouch) {
                    $padFX = [double]$padX; $padFY = [double]$padY
                    $padPXf = $padFX; $padPYf = $padFY
                }
                $padSpeedEma = 0.0
                $padActive = $false
                if ($TracePad) { Write-Host ("  [{0,7:F2}s] PAD UP   raw=({1},{2}) jump={3} toRest={4:F0}" -f $el, $padX, $padY, $jump, $toRest) }
            } elseif ($padAct -eq 'move') {
                # 通常の移動
                $padActive = $true
                # 位置フィルタは毎フレーム更新する。生値が止まったフレームで更新を止めると
                # フィルタが古い位置を抱えたままになり、次に動かした瞬間に「古い方向の
                # 移動」がまとめて出て、ゴムのように引き戻される。
                $alphaS = if ($PadSmoothTau -gt 0) { 1.0 - [Math]::Exp(-$dt / $PadSmoothTau) } else { 1.0 }
                $padFX = $padFX + ($padX - $padFX) * $alphaS
                $padFY = $padFY + ($padY - $padFY) * $alphaS
                if ($padPrevX -lt 0) { $padFX = [double]$padX; $padFY = [double]$padY }  # 最初のレポートは位置合わせのみ
                $dxp = $padFX - $padPXf
                $dyp = $padFY - $padPYf
                $padPXf = $padFX; $padPYf = $padFY

                # タップ判定は「開始位置からの距離」で見る（移動量の総和だとジッタで誤判定する）
                if ($padMoved -and $padSession) {
                    $dist = [Math]::Abs($padX - $tapX0) + [Math]::Abs($padY - $tapY0)
                    if ($dist -gt $TapMaxMove) { $tapMoved = $true }
                    if ([HidMouse]::Has(4)) { $tapClicked = $true }
                }

                if ($dxp -ne 0.0 -or $dyp -ne 0.0) {
                    # 指の速さは毎フレーム更新（止まれば 0 に減衰し、加速も 1 倍に戻る）
                    $spd = [Math]::Sqrt($dxp * $dxp + $dyp * $dyp) / $dt
                    $a = [Math]::Min(1.0, $dt / $PadAccelTau)
                    $padSpeedEma = (1.0 - $a) * $padSpeedEma + $a * $spd
                    $scale = 1.0 + ($padSpeedEma / $PadAccelRef)
                    if ($scale -gt $PadAccelMax) { $scale = $PadAccelMax }
                    $g = $PadGain * $scale
                    $padRemX += $dxp * $g
                    if ($InvertY) { $padRemY -= $dyp * $g } else { $padRemY += $dyp * $g }
                    if ($TracePad -and ($now - $lastPadPrint).TotalMilliseconds -ge 10) {
                        $lastPadPrint = $now
                        Write-Host ("  [{0,7:F2}s] PAD raw=({1,3},{2,3}) f=({3,5:F1},{4,5:F1}) d=({5,5:F2},{6,5:F2}) spd={7,5:F0} x{8:F2} rem=({9,6:F1},{10,6:F1})" -f `
                            $el, $padX, $padY, $padFX, $padFY, $dxp, $dyp, $padSpeedEma, $scale, $padRemX, $padRemY)
                    }
                }
            }
        }

        # ---- 次フレーム用に生値の前回値を保存 ----
        $padPrevX = $padX; $padPrevY = $padY; $padMovedPrev = $padMoved

        # ---- パッドの残量を毎フレーム一部だけ消費（量子化によるガタつきを平滑化） ----
        if ([Math]::Abs($padRemX) -gt 0.05 -or [Math]::Abs($padRemY) -gt 0.05) {
            $alphaF = if ($PadFollowTau -gt 0) { 1.0 - [Math]::Exp(-$dt / $PadFollowTau) } else { 1.0 }
            $stx = $padRemX * $alphaF; $sty = $padRemY * $alphaF
            if ($stx -gt $PadStepMax) { $stx = $PadStepMax } elseif ($stx -lt -$PadStepMax) { $stx = -$PadStepMax }
            if ($sty -gt $PadStepMax) { $sty = $PadStepMax } elseif ($sty -lt -$PadStepMax) { $sty = -$PadStepMax }
            $padRemX -= $stx; $padRemY -= $sty
            $accX += $stx; $accY += $sty
        } else { $padRemX = 0.0; $padRemY = 0.0 }

        # ---- タップの後始末（旧: 遅延解放。現在は ClickLeft で即座に離す） ----

        # ---- ボタン状態 ----
        $leftNow  = ([HidMouse]::Has(4)) -or ($trig -ge $TriggerThreshold)
        $rightNow = [HidMouse]::Has(0)
        $midNow   = [HidMouse]::Has(3)
        $menuNow  = [HidMouse]::Has(1)
        $winNow   = [HidMouse]::Has(2)

        if ($menuNow -and -not $prevMenu) {
            $scrollMode = -not $scrollMode
            Write-Host ("  [スクロールモード] {0}" -f $(if ($scrollMode) { 'ON' } else { 'OFF' }))
        }
        $prevMenu = $menuNow

        # ---- スクロールモード: スティックでホイール ----
        # 上に倒すと上にスクロール（$ay は InvertY 適用後で「上＝負」、ホイールは「正＝上」）
        if ($scrollMode) {
            $wheelAcc  -= $ay * $WheelRate * $dt
            $hwheelAcc += $ax * $WheelRate * $dt
            $vx = 0.0; $vy = 0.0   # スクロール中はスティックでカーソルを動かさない
        }

        if ($winNow) { $vx *= $BoostFactor; $vy *= $BoostFactor }

        # ---- 移動量の累積 ----
        $accX += $vx * $dt
        $accY += $vy * $dt
        if ($accX -gt 200) { $accX = 200 }; if ($accX -lt -200) { $accX = -200 }
        if ($accY -gt 200) { $accY = 200 }; if ($accY -lt -200) { $accY = -200 }
        $dx = [int][Math]::Round($accX)
        $dy = [int][Math]::Round($accY)
        $sentX = 0; $sentY = 0
        if ($dx -ne 0 -or $dy -ne 0) {
            $accX -= $dx; $accY -= $dy
            $sentX = $dx; $sentY = $dy
            if (-not $DryRun) { [HidMouse]::Mouse($MOVE, $dx, $dy, 0) }
        }
        $cumX += $sentX; $cumY += $sentY

        # ---- ホイール ----
        if ([Math]::Abs($wheelAcc) -ge 1.0) {
            $w = [int][Math]::Truncate($wheelAcc)
            if ($w -ne 0) {
                $wheelAcc -= $w
                if (-not $DryRun) { [HidMouse]::Mouse($WHEEL, 0, 0, $w * 120) }
            }
        }
        if ([Math]::Abs($hwheelAcc) -ge 1.0) {
            $hw = [int][Math]::Truncate($hwheelAcc)
            if ($hw -ne 0) {
                $hwheelAcc -= $hw
                if (-not $DryRun) { [HidMouse]::Mouse($HWHEEL, 0, 0, $hw * 120) }
            }
        }

        # ---- クリック（エッジ検出） + 振動フィードバック ----
        if ($leftNow -ne $prevLeft) {
            if (-not $DryRun) {
                [HidMouse]::Mouse($(if ($leftNow) { $LDOWN } else { $LUP }), 0, 0, 0)
                if ($leftNow -and $HapticsStrength -gt 0) { [HidMouse]::HapticPulse($HapticsStrength, $HapticsStrength, $HapticsMs) }
            }
            if ($Trace) { Write-Host ("  [{0,7:F2}s] LEFT  {1}" -f $el, $(if ($leftNow) { 'DOWN' } else { 'UP' })) }
            $prevLeft = $leftNow
        }
        if ($rightNow -ne $prevRight) {
            if (-not $DryRun) {
                [HidMouse]::Mouse($(if ($rightNow) { $RDOWN } else { $RUP }), 0, 0, 0)
                if ($rightNow -and $HapticsStrength -gt 0) { [HidMouse]::HapticPulse($HapticsStrength, $HapticsStrength, $HapticsMs) }
            }
            if ($Trace) { Write-Host ("  [{0,7:F2}s] RIGHT {1}" -f $el, $(if ($rightNow) { 'DOWN' } else { 'UP' })) }
            $prevRight = $rightNow
        }
        if ($midNow -ne $prevMid) {
            if (-not $DryRun) {
                [HidMouse]::Mouse($(if ($midNow) { $MDOWN } else { $MUP }), 0, 0, 0)
                if ($midNow -and $HapticsStrength -gt 0) { [HidMouse]::HapticPulse($HapticsStrength, $HapticsStrength, $HapticsMs) }
            }
            if ($Trace) { Write-Host ("  [{0,7:F2}s] MID   {1}" -f $el, $(if ($midNow) { 'DOWN' } else { 'UP' })) }
            $prevMid = $midNow
        }

        # ---- 診断ハートビートはループ先頭で出力（ここでは何もしない） ----

        # ---- パッド生値＋実送信移動量の記録（毎フレーム） ----
        if ($padCsvW) {
            $padCsvW.WriteLine(("{0:F4},{1},{2},{3},{4},{5},{6},{7},{8},{9:F0},{10:F0},{11:F0},{12:F2},{13:F2},{14},{15},{16},{17},{18:F2},{19:F2},{20}" -f `
                $el, $padX, $padY, $bit6, [int]$padNoTouch, [int]$padActive, [int]$padDown, [int]$padUp, $jump, $toRest, $restX, $restY, $padFX, $padFY, $sentX, $sentY, $cumX, $cumY, $padRemX, $padRemY, [int]$padPending))
        }

        # ---- DryRun 表示 ----
        if ($DryRun -and ($now - $lastPrint).TotalMilliseconds -ge 250) {
            $lastPrint = $now
            Write-Host ("  stick=({0,4},{1,4}) trig={2,3} pad=({3,3},{4,3}) touch={5,-5} ntrl={6,-5} L={7} R={8} M={9} scroll={10} dx={11} dy={12}" -f `
                $sx, $sy, $trig, $padX, $padY, $padTouched, $onNeutral, $leftNow, $rightNow, $midNow, $scrollMode, $dx, $dy)
        }
    }
} finally {
    if (-not $DryRun) {
        if ($prevLeft)  { [HidMouse]::Mouse($LUP, 0, 0, 0) }
        if ($prevRight) { [HidMouse]::Mouse($RUP, 0, 0, 0) }
        if ($prevMid)   { [HidMouse]::Mouse($MUP, 0, 0, 0) }
        if ($HapticsStrength -gt 0) { [void][HidMouse]::SetHaptics(0, 0) }
    }
    [HidMouse]::Close()
    if ($padCsvW) { $padCsvW.Flush(); $padCsvW.Close() }
    $el = ((Get-Date) - $started).TotalSeconds
    Write-Host ("`n終了: {0:F1} 秒 / {1} レポート ({2:F1} Hz)" -f $el, $nReports, $(if ($el -gt 0) { $nReports / $el } else { 0 }))
}
