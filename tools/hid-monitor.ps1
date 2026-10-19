#requires -Version 5.1
<#
  hid-monitor.ps1 - HID 入力レポートをリアルタイムにデコードして表示する

  生のレポート記述子を読まずに、HidP_GetData / HidP_GetUsageValue を使って
  「どのコレクションの・どの Usage が・どの値になったか」をそのまま出力する。
  実行中にコントローラのスティック / タッチパッド / 各ボタンを操作すると、
  どの DataIndex / Usage が反応するかを確認できる。

  使い方:
    powershell -ExecutionPolicy Bypass -File tools\hid-monitor.ps1 -Seconds 30
    powershell -ExecutionPolicy Bypass -File tools\hid-monitor.ps1 -Seconds 60 -LogFile out.txt
    powershell -ExecutionPolicy Bypass -File tools\hid-monitor.ps1 -Raw   # 生バイトも表示
#>
[CmdletBinding()]
param(
    [int]$VendorId = 0x045E,
    [int]$ProductId = 0x065D,
    [int]$Seconds = 20,
    [string]$LogFile = '',
    [switch]$Raw,
    [switch]$PerReport,
    [switch]$Events,
    [string]$InstanceMatch = ''
)

$ErrorActionPreference = 'Stop'

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class HidMon
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
    public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, IntPtr read, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int toRead, out int read, IntPtr ov);

    [StructLayout(LayoutKind.Sequential)] public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_BUTTON_CAPS
    {
        public ushort UsagePage; public byte ReportID; public byte IsAlias; public ushort BitField;
        public ushort LinkCollection, LinkUsage, LinkUsagePage;
        public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
        public ushort BitSize, ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=5)] public ushort[] Reserved2;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
    }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage; public byte ReportID; public byte IsAlias; public ushort BitField;
        public ushort LinkCollection, LinkUsage, LinkUsagePage;
        public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
        public ushort BitSize, ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=5)] public ushort[] Reserved2;
        public uint UnitsExp, Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
    }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_DATA { public ushort DataIndex; public ushort Reserved; public uint RawValue; }
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_LINK_COLLECTION_NODE { public ushort LinkUsage, LinkUsagePage; public ushort Parent, NumberOfChildren, NextSibling, FirstChild; public uint Bits; public IntPtr UserContext; }

    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetSerialNumberString(SafeFileHandle h, byte[] b, int l);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetValueCaps(int t, [In,Out] HIDP_VALUE_CAPS[] c, ref ushort n, IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetButtonCaps(int t, [In,Out] HIDP_BUTTON_CAPS[] c, ref ushort n, IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetData(int t, [In,Out] HIDP_DATA[] d, ref uint n, IntPtr pp, byte[] report, uint len);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetLinkCollectionNodes([In,Out] HIDP_LINK_COLLECTION_NODE[] n, ref uint len, IntPtr pp);

    public const int HIDP_INPUT = 0, HIDP_OUTPUT = 1, HIDP_FEATURE = 2;

    public static List<string> Enumerate()
    {
        var r = new List<string>();
        Guid g = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (s == new IntPtr(-1)) return r;
        try {
            var x = new SP_DEVICE_INTERFACE_DATA(); x.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (int i = 0; ; i++) {
                if (!SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref g, i, ref x)) break;
                int need; SetupDiGetDeviceInterfaceDetail(s, ref x, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr b = Marshal.AllocHGlobal(need);
                try {
                    Marshal.WriteInt32(b, 0, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(s, ref x, b, need, out need, IntPtr.Zero))
                        r.Add(Marshal.PtrToStringAuto(new IntPtr(b.ToInt64() + 4)));
                } finally { Marshal.FreeHGlobal(b); }
            }
        } finally { SetupDiDestroyDeviceInfoList(s); }
        return r;
    }

    static string Utf16(byte[] b)
    {
        if (b == null) return null;
        int n = 0; while (n + 1 < b.Length && !(b[n] == 0 && b[n+1] == 0)) n += 2;
        return Encoding.Unicode.GetString(b, 0, n);
    }

    public class UsageMap
    {
        public Dictionary<int, ushort> ValueUsagePage = new Dictionary<int, ushort>();
        public Dictionary<int, ushort> ValueUsage = new Dictionary<int, ushort>();
        public Dictionary<int, ushort> ButtonUsagePage = new Dictionary<int, ushort>();
        public Dictionary<int, ushort> ButtonUsage = new Dictionary<int, ushort>();
    }

    public static UsageMap BuildUsageMap(IntPtr pp, HIDP_CAPS caps)
    {
        var m = new UsageMap();
        ushort n = caps.NumberInputValueCaps;
        if (n > 0) {
            var arr = new HIDP_VALUE_CAPS[n];
            for (int i = 0; i < n; i++) arr[i].Reserved2 = new ushort[5];
            if (HidP_GetValueCaps(HIDP_INPUT, arr, ref n, pp) >= 0)
                for (int i = 0; i < n; i++) {
                    var c = arr[i];
                    if (c.DataIndexMax >= c.DataIndexMin && c.DataIndexMax < 4096)
                        for (int d = c.DataIndexMin; d <= c.DataIndexMax; d++) {
                            m.ValueUsagePage[d] = c.UsagePage;
                            m.ValueUsage[d] = (ushort)(c.UsageMin + (d - c.DataIndexMin));
                        }
                }
        }
        n = caps.NumberInputButtonCaps;
        if (n > 0) {
            var arr = new HIDP_BUTTON_CAPS[n];
            for (int i = 0; i < n; i++) arr[i].Reserved2 = new ushort[5];
            if (HidP_GetButtonCaps(HIDP_INPUT, arr, ref n, pp) >= 0)
                for (int i = 0; i < n; i++) {
                    var c = arr[i];
                    if (c.DataIndexMax >= c.DataIndexMin && c.DataIndexMax < 4096)
                        for (int d = c.DataIndexMin; d <= c.DataIndexMax; d++) {
                            m.ButtonUsagePage[d] = c.UsagePage;
                            m.ButtonUsage[d] = (ushort)(c.UsageMin + (d - c.DataIndexMin));
                        }
                }
        }
        return m;
    }

    public class Report
    {
        public int ReportID;
        public string Hex;
        public List<string> Lines = new List<string>();
        public Dictionary<int, uint> Raw = new Dictionary<int, uint>();
    }

    public static Report Decode(IntPtr pp, UsageMap map, byte[] report, int len, bool isRaw, int inputReportLen)
    {
        var rep = new Report();
        rep.ReportID = report[0];
        var hx = new StringBuilder();
        for (int i = 0; i < len; i++) hx.Append(report[i].ToString("X2")).Append(' ');
        rep.Hex = hx.ToString().TrimEnd();

        uint count = 512;
        var dl = new HIDP_DATA[512];
        int st = HidP_GetData(HIDP_INPUT, dl, ref count, pp, report, (uint)len);
        if (st < 0) { rep.Lines.Add("  HidP_GetData failed: 0x" + st.ToString("X8")); return rep; }
        for (uint i = 0; i < count; i++) {
            int di = dl[i].DataIndex;
            rep.Raw[di] = dl[i].RawValue;
            ushort page; ushort usage; string kind;
            if (map.ButtonUsage.TryGetValue(di, out usage) && di < 0x1000 && !map.ValueUsage.ContainsKey(di)) {
                page = map.ButtonUsagePage[di]; kind = "btn";
            } else if (map.ValueUsage.TryGetValue(di, out usage)) {
                page = map.ValueUsagePage[di]; kind = "val";
            } else if (map.ButtonUsage.TryGetValue(di, out usage)) {
                page = map.ButtonUsagePage[di]; kind = "btn";
            } else { page = 0xFFFF; kind = "?"; }
            rep.Lines.Add(string.Format("  idx={0,-4} {1} page=0x{2:X4} usage=0x{3:X2} value={4}", di, kind, page, usage, dl[i].RawValue));
        }
        return rep;
    }
}
'@
if (-not ('HidMon' -as [type])) { Add-Type -TypeDefinition $cs -Language CSharp }

function Get-UsageLabel([int]$page, [int]$usage) {
    switch ($page) {
        0x0001 {
            $t = @{0x30='X';0x31='Y';0x32='Z';0x33='Rx';0x34='Ry';0x35='Rz';0x36='Slider';0x37='Dial';0x38='Wheel';0x39='Hat switch';0x90='D-pad Up';0x91='D-pad Down';0x92='D-pad Right';0x93='D-pad Left'}
            if ($t.ContainsKey($usage)) { return "GenericDesktop/$($t[$usage])" } else { return ('GenericDesktop/0x{0:X2}' -f $usage) }
        }
        0x0006 { return ('GenericDeviceControls/0x{0:X2}' -f $usage) }
        0x0009 { return "Button/$usage" }
        0x000C { return ('Consumer/0x{0:X2}' -f $usage) }
        0x000D { return ('Digitizer/0x{0:X2}' -f $usage) }
        default { return ('Page0x{0:X4}/0x{1:X2}' -f $page, $usage) }
    }
}

# ---- 対象デバイスを探す ----
$candidates = @()
foreach ($p in [HidMon]::Enumerate()) {
    if ($p -notmatch ('vid&[0-9a-f]*{0:x4}_pid&[0-9a-f]*{1:x4}' -f $VendorId, $ProductId)) { continue }
    if ($InstanceMatch -and $p -notmatch $InstanceMatch) { continue }
    $candidates += $p
}
if ($candidates.Count -eq 0) { Write-Host "対象 HID デバイスが見つかりません"; exit 1 }

$log = if ($LogFile) { [IO.StreamWriter]::new($LogFile, $true, [Text.UTF8Encoding]::new($false)) } else { $null }
function Emit([string]$s) { Write-Host $s; if ($log) { $log.WriteLine($s); $log.Flush() } }

foreach ($path in $candidates) {
    Emit "TARGET PATH : $path"
    $h = [HidMon]::CreateFile($path, [uint32]2147483648, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)  # GENERIC_READ
    if ($h.IsInvalid) { $h.Dispose(); Emit "  OPEN FAILED err=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())"; continue }
    $pp = [IntPtr]::Zero
    if (-not [HidMon]::HidD_GetPreparsedData($h, [ref]$pp)) { $h.Dispose(); Emit "  HidD_GetPreparsedData FAILED"; continue }
    $atts = New-Object HidMon+HIDD_ATTRIBUTES; $atts.Size = [Runtime.InteropServices.Marshal]::SizeOf($atts)
    [void][HidMon]::HidD_GetAttributes($h, [ref]$atts)
    $sn = New-Object byte[] 512
    [void][HidMon]::HidD_GetSerialNumberString($h, $sn, $sn.Length)
    $caps = New-Object HidMon+HIDP_CAPS; $caps.Reserved = [uint16[]]::new(17)
    [void][HidMon]::HidP_GetCaps($pp, [ref]$caps)
    Emit ("  VID:PID={0:X4}:{1:X4} SN={2} InputReport={3}B" -f $atts.VendorID, $atts.ProductID, ([Text.Encoding]::Unicode.GetString($sn).Trim([char]0)), $caps.InputReportByteLength)

    $map = [HidMon]::BuildUsageMap($pp, $caps)
    Emit '  --- DataIndex -> Usage 対応表 (入力) ---'
    foreach ($di in ($map.ValueUsage.Keys + $map.ButtonUsage.Keys | Sort-Object -Unique)) {
        if ($map.ValueUsage.ContainsKey($di)) {
            Emit ("    idx={0,-4} val page=0x{1:X4} usage=0x{2:X2} {3}" -f $di, $map.ValueUsagePage[$di], $map.ValueUsage[$di], (Get-UsageLabel $map.ValueUsagePage[$di] $map.ValueUsage[$di]))
        } else {
            Emit ("    idx={0,-4} btn page=0x{1:X4} usage=0x{2:X2} {3}" -f $di, $map.ButtonUsagePage[$di], $map.ButtonUsage[$di], (Get-UsageLabel $map.ButtonUsagePage[$di] $map.ButtonUsage[$di]))
        }
    }

    # 入力レポート長ぶんのバッファ
    $bufLen = [Math]::Max([int]$caps.InputReportByteLength, 64) + 8
    $deadline = (Get-Date).AddSeconds($Seconds)
    $counts = @{}
    $stat = @{}
    # 対応表にある DataIndex は「未観測」として事前登録
    foreach ($di in ($map.ValueUsage.Keys + $map.ButtonUsage.Keys | Sort-Object -Unique)) {
        $stat[$di] = [pscustomobject]@{ First = $null; Min = $null; Max = $null; Last = $null; Changes = 0; N = 0 }
    }
    Emit "  --- レポート受信開始 ($Seconds 秒間) ---"
    $prev = @{}
    $started = Get-Date
    # ボタン候補の DataIndex (0..6 と、ボタン cap に現れた index)
    $buttonIdx = @(0..6) + @($map.ButtonUsage.Keys | Where-Object { -not $map.ValueUsage.ContainsKey($_) })
    $buttonIdx = $buttonIdx | Sort-Object -Unique
    $btnState = @{}
    $pressCount = @{}
    foreach ($b in $buttonIdx) { $btnState[$b] = $false; $pressCount[$b] = 0 }
    # アナログ/IMU 系（常時変動するのでイベント出力から除外）
    $noisy = @(7, 8, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23)
    while ((Get-Date) -lt $deadline) {
        $read = 0
        $buf2 = New-Object byte[] $bufLen
        $ok = [HidMon]::ReadFile($h, $buf2, $bufLen, [ref]$read, [IntPtr]::Zero)
        if (-not $ok -or $read -le 0) { Start-Sleep -Milliseconds 5; continue }
        $len = $read
        $rid = $buf2[0]
        $counts[$rid] = 1 + [int]$counts[$rid]
        $rep = [HidMon]::Decode($pp, $map, $buf2, $len, $Raw, [int]$caps.InputReportByteLength)
        if ($PerReport) {
            Emit ("  [{0:HH:mm:ss.fff}] ReportID={1} len={2}" -f (Get-Date), $rep.ReportID, $len)
            Emit ("    raw: {0}" -f $rep.Hex)
            foreach ($l in $rep.Lines) { Emit $l }
        }
        if ($Events) {
            $el = ((Get-Date) - $started).TotalSeconds
            # HidP_GetData は「押されているボタン」しか返さないため、
            # レポートに存在しないボタンは解放とみなす
            foreach ($k in $buttonIdx) {
                $present = $rep.Raw.ContainsKey($k)
                $v = if ($present) { [uint32]$rep.Raw[$k] } else { 0 }
                $nowPressed = ($present -and $v -ne 0)
                if ($nowPressed -ne $btnState[$k]) {
                    $btnState[$k] = $nowPressed
                    if ($nowPressed) { $pressCount[$k] = 1 + [int]$pressCount[$k] }
                    Emit ("    [{0,8:F3}s] idx={1,-3} {2}  (count={3} raw={4})" -f $el, $k, $(if ($nowPressed) { 'PRESSED ' } else { 'released' }), $pressCount[$k], $v)
                }
            }
            foreach ($k in @($rep.Raw.Keys)) {
                $v = [uint32]$rep.Raw[$k]
                if ($buttonIdx -contains $k) { continue }
                if ($noisy -notcontains $k) {
                    if (-not $prev.ContainsKey($k) -or $prev[$k] -ne $v) {
                        Emit ("    [{0,8:F3}s] idx={1,-3} value={2} (was {3})" -f $el, $k, $v, $(if ($prev.ContainsKey($k)) { $prev[$k] } else { '-' }))
                    }
                }
                $prev[$k] = $v
            }
        }
        foreach ($k in @($rep.Raw.Keys)) {
            $v = [uint32]$rep.Raw[$k]
            if (-not $stat.ContainsKey($k)) {
                $stat[$k] = [pscustomobject]@{ First = $null; Min = $null; Max = $null; Last = $null; Changes = 0; N = 0 }
            }
            $s = $stat[$k]
            if ($s.N -eq 0) { $s.First = $v; $s.Min = $v; $s.Max = $v }
            else {
                if ($v -ne $s.Last) { $s.Changes = $s.Changes + 1 }
                if ($v -lt $s.Min) { $s.Min = $v }
                if ($v -gt $s.Max) { $s.Max = $v }
            }
            $s.Last = $v
            $s.N = $s.N + 1
        }
    }
    if ($Events) {
        Emit ''
        Emit '  --- ボタン押下回数 ---'
        foreach ($k in ($buttonIdx)) {
            Emit ("    idx={0,-3} 押下回数={1}" -f $k, $pressCount[$k])
        }
    }
    Emit ''
    Emit '  --- DataIndex 統計 ---'
    Emit ("    {0,-5} {1,-4} {2,-34} {3,12} {4,12} {5,12} {6,8} {7,8}" -f 'idx', 'kind', 'Usage', 'min', 'max', 'last', 'changes', 'seen')
    foreach ($k in ($stat.Keys | Sort-Object)) {
        $s = $stat[$k]
        if ($map.ValueUsage.ContainsKey($k)) {
            $kind = 'val'; $page = $map.ValueUsagePage[$k]; $usage = $map.ValueUsage[$k]
        } elseif ($map.ButtonUsage.ContainsKey($k)) {
            $kind = 'btn'; $page = $map.ButtonUsagePage[$k]; $usage = $map.ButtonUsage[$k]
        } else { $kind = '?'; $page = 0xFFFF; $usage = 0 }
        $label = Get-UsageLabel $page $usage
        if ($s.N -eq 0) {
            Emit ("    {0,-5} {1,-4} {2,-34} {3,12} {4,12} {5,12} {6,8} {7,8}" -f $k, $kind, $label, '-', '-', '-', '-', 0)
        } else {
            Emit ("    {0,-5} {1,-4} {2,-34} {3,12} {4,12} {5,12} {6,8} {7,8}" -f $k, $kind, $label, $s.Min, $s.Max, $s.Last, $s.Changes, $s.N)
        }
    }
    Emit '  --- ReportID 別 受信回数 ---'
    foreach ($k in ($counts.Keys | Sort-Object)) { Emit ("    ReportID={0} : {1} 回" -f $k, $counts[$k]) }

    [void][HidMon]::HidD_FreePreparsedData($pp)
    $h.Dispose()
}
if ($log) { $log.Dispose() }
