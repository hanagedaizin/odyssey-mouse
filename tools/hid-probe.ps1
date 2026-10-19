#requires -Version 5.1
<#
  hid-probe.ps1 - HID デバイスの技術仕様を読み出すプローブ

  指定した VID/PID (省略時は WMR モーションコントローラ) の HID トップレベル
  コレクションを列挙し、以下を出力する:
    - デバイスインターフェイスパス
    - HIDD_ATTRIBUTES (VID/PID/Version)
    - 製造元/製品/シリアル文字列
    - HIDP_CAPS (UsagePage/Usage, 各レポート長, ValueCaps/ButtonCaps 数)
    - 入力 ValueCaps / ButtonCaps の全項目 (Usage, ReportID, BitSize, LogicalRange 等)
    - 生のレポート記述子 (IOCTL_HID_GET_REPORT_DESCRIPTOR) の16進ダンプ

  使い方:
    powershell -ExecutionPolicy Bypass -File tools\hid-probe.ps1
    powershell -ExecutionPolicy Bypass -File tools\hid-probe.ps1 -Vid 0x054C -Pid 0x09CC
    powershell -ExecutionPolicy Bypass -File tools\hid-probe.ps1 -All
#>
[CmdletBinding()]
param(
    [int]$VendorId = 0x045E,
    [int]$ProductId = 0x065D,
    [switch]$All
)

$ErrorActionPreference = 'Stop'

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidProbe
{
    // ---------- SetupAPI ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);

    // ---------- Kernel32 ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0191;
    public const uint IOCTL_HID_GET_STRING = 0x000B01B1;
    public const uint HID_STRING_MANUFACTURER = 0x0000;
    public const uint HID_STRING_PRODUCT = 0x0001;
    public const uint HID_STRING_SERIAL = 0x0002;

    // ---------- HID ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct HIDP_BUTTON_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr ppData);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr pData);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetSerialNumberString(SafeFileHandle h, byte[] buf, int len);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr ppData, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetValueCaps(int ReportType, [In, Out] HIDP_VALUE_CAPS[] caps, ref ushort count, IntPtr ppData);
    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetButtonCaps(int ReportType, [In, Out] HIDP_BUTTON_CAPS[] caps, ref ushort count, IntPtr ppData);

    public const int HIDP_INPUT = 0;
    public const int HIDP_OUTPUT = 1;
    public const int HIDP_FEATURE = 2;

    public class DevInfo
    {
        public string Path;
        public HIDD_ATTRIBUTES Attr;
        public string Manufacturer, Product, Serial;
        public HIDP_CAPS Caps;
        public List<HIDP_VALUE_CAPS> InputValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> InputButtons = new List<HIDP_BUTTON_CAPS>();
        public List<HIDP_VALUE_CAPS> OutputValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> OutputButtons = new List<HIDP_BUTTON_CAPS>();
        public List<HIDP_VALUE_CAPS> FeatureValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> FeatureButtons = new List<HIDP_BUTTON_CAPS>();
        public byte[] ReportDescriptor;
    }

    static string Utf16ToString(byte[] b)
    {
        if (b == null) return null;
        int n = 0;
        while (n + 1 < b.Length && !(b[n] == 0 && b[n + 1] == 0)) n += 2;
        return Encoding.Unicode.GetString(b, 0, n);
    }

    public static List<string> EnumerateHidPaths()
    {
        var result = new List<string>();
        Guid hidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT|DEVICEINTERFACE
        if (set == new IntPtr(-1)) return result;
        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (int i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did)) break;
                int need;
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6); // cbSize
                    if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, out need, IntPtr.Zero))
                        result.Add(Marshal.PtrToStringAuto(new IntPtr(buf.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    public static DevInfo Open(string path, bool deep)
    {
        var fi = new DevInfo();
        fi.Path = path;
        var h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        try
        {
            fi.Attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
            HidD_GetAttributes(h, ref fi.Attr);
            byte[] sb = new byte[512];
            if (HidD_GetManufacturerString(h, sb, sb.Length)) fi.Manufacturer = Utf16ToString(sb);
            sb = new byte[512];
            if (HidD_GetProductString(h, sb, sb.Length)) fi.Product = Utf16ToString(sb);
            sb = new byte[512];
            if (HidD_GetSerialNumberString(h, sb, sb.Length)) fi.Serial = Utf16ToString(sb);

            if (deep)
            {
                IntPtr pp;
                if (HidD_GetPreparsedData(h, out pp))
                {
                    try
                    {
                        var caps = new HIDP_CAPS();
                        caps.Reserved = new ushort[17];
                        HidP_GetCaps(pp, ref caps);
                        fi.Caps = caps;

                        ushort n = caps.NumberInputValueCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_VALUE_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetValueCaps(HIDP_INPUT, arr, ref n, pp) >= 0)
                                fi.InputValues.AddRange(arr);
                        }
                        n = caps.NumberInputButtonCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_BUTTON_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetButtonCaps(HIDP_INPUT, arr, ref n, pp) >= 0)
                                fi.InputButtons.AddRange(arr);
                        }
                        n = caps.NumberOutputValueCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_VALUE_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetValueCaps(HIDP_OUTPUT, arr, ref n, pp) >= 0)
                                fi.OutputValues.AddRange(arr);
                        }
                        n = caps.NumberOutputButtonCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_BUTTON_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetButtonCaps(HIDP_OUTPUT, arr, ref n, pp) >= 0)
                                fi.OutputButtons.AddRange(arr);
                        }
                        n = caps.NumberFeatureValueCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_VALUE_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetValueCaps(HIDP_FEATURE, arr, ref n, pp) >= 0)
                                fi.FeatureValues.AddRange(arr);
                        }
                        n = caps.NumberFeatureButtonCaps;
                        if (n > 0)
                        {
                            var arr = new HIDP_BUTTON_CAPS[n];
                            for (int k = 0; k < n; k++) arr[k].Reserved2 = new ushort[5];
                            if (HidP_GetButtonCaps(HIDP_FEATURE, arr, ref n, pp) >= 0)
                                fi.FeatureButtons.AddRange(arr);
                        }
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }

                // 生のレポート記述子
                byte[] rd = new byte[4096];
                int ret;
                if (DeviceIoControl(h, IOCTL_HID_GET_REPORT_DESCRIPTOR, IntPtr.Zero, 0, rd, rd.Length, out ret, IntPtr.Zero) && ret > 0)
                {
                    fi.ReportDescriptor = new byte[ret];
                    Array.Copy(rd, fi.ReportDescriptor, ret);
                }
            }
            return fi;
        }
        finally { h.Dispose(); }
    }
}
'@

if (-not ('HidProbe' -as [type])) { Add-Type -TypeDefinition $cs -Language CSharp }

# HID Usage 名の逆引き (主要なものだけ)
$script:GenericDesktop = @{
    0x00='Undefined';0x01='Pointer';0x02='Mouse';0x04='Joystick';0x05='Game Pad';0x06='Keyboard'
    0x07='Keypad';0x08='Multi-axis Controller';0x09='Tablet PC System Controls';0x0A='Water Cooling Device'
    0x0B='Computer Chassis Device';0x0C='Wireless Radio Controls';0x0D='Portable Device Control'
    0x0E='System Multi-Axis Controller';0x0F='Spatial Controller';0x10='Assistive Control'
    0x30='X';0x31='Y';0x32='Z';0x33='Rx';0x34='Ry';0x35='Rz';0x36='Slider';0x37='Dial';0x38='Wheel'
    0x39='Hat switch';0x3A='Counted Buffer';0x3B='Byte Count';0x3C='Motion Wakeup';0x3D='Start';0x3E='Select'
    0x40='Vx';0x41='Vy';0x42='Vz';0x43='Vbrx';0x44='Vbry';0x45='Vbrz';0x46='Vno';0x80='System Control'
    0x81='System Power Down';0x82='System Sleep';0x83='System Wake Up';0x84='System Context Menu';0x85='System Main Menu'
    0x86='System App Menu';0x87='System Help Menu';0x88='System Menu Exit';0x89='System Menu Select';0x8A='System Menu Right'
    0x8B='System Menu Left';0x8C='System Menu Up';0x8D='System Menu Down';0x90='D-pad Up';0x91='D-pad Down'
    0x92='D-pad Right';0x93='D-pad Left';0xC0='System Function Shift';0x100='System Multi-Axis Controller'
}
$script:UsagePages = @{
    0x0001='Generic Desktop';0x0002='Simulation Controls';0x0003='VR Controls';0x0004='Sport Controls'
    0x0005='Game Controls';0x0006='Generic Device Controls';0x0007='Keyboard/Keypad';0x0008='LED'
    0x0009='Button';0x000A='Ordinal';0x000B='Telephony';0x000C='Consumer';0x000D='Digitizer'
    0x000E='Haptics';0x000F='Physical Input Device';0x0010='Unicode';0x0012='Eye and Head Trackers'
    0x0014='Auxiliary Display';0x0020='Sensors';0x0040='Medical Instrument';0x0080='Monitor'
    0x0081='Monitor Enumerated';0x0082='VESA Virtual Controls';0x0084='Power';0x0085='Battery System'
    0x008C='Bar Code Scanner';0x0090='Camera Control';0x0091='Arcade';0x0092='Gaming Device'
    0xFF00='Vendor-Defined 0xFF00';0xFF01='Vendor-Defined 0xFF01';0xFFFF='Vendor-Defined 0xFFFF'
    0xFFA0='WMR Vendor (0xFFA0)'
}
function Get-UsageName([int]$page, [int]$usage) {
    $pn = if ($script:UsagePages.ContainsKey($page)) { $script:UsagePages[$page] } else { '0x{0:X4}' -f $page }
    if ($page -eq 0x01) {
        if ($script:GenericDesktop.ContainsKey($usage)) { return "$pn / $($script:GenericDesktop[$usage])" }
    }
    if ($page -eq 0x09) { return "$pn / Button $usage" }
    if ($page -eq 0x0C) {
        $consumer = @{
            0x01='Consumer Control';0x30='Power';0xCD='Play/Pause';0xE9='Volume Up';0xEA='Volume Down'
            0xB5='Scan Next Track';0xB6='Scan Previous Track';0xE2='Mute';0x221='AC Search'
            0x223='AC Home';0x224='AC Back';0x225='AC Forward';0x226='AC Stop';0x227='AC Refresh'
            0x22A='AC Bookmarks';0x183='AL Programmable Buttons';0x185='AL Consumer Control Configuration'
        }
        if ($consumer.ContainsKey($usage)) { return "$pn / $($consumer[$usage])" }
    }
    return ('{0} / 0x{1:X2}' -f $pn, $usage)
}

$paths = [HidProbe]::EnumerateHidPaths()
$targets = @()
foreach ($p in $paths) {
    $d = [HidProbe]::Open($p, $false)
    if ($null -eq $d) { continue }
    if ($All -or ($d.Attr.VendorID -eq $VendorId -and $d.Attr.ProductID -eq $ProductId)) { $targets += $d }
}

if ($targets.Count -eq 0) {
    Write-Host "該当する HID デバイスが見つかりません (VID=0x{0:X4} PID=0x{1:X4})" -f $VendorId, $ProductId
    exit 1
}

function Format-ValueCaps($caps) {
    $i = 0
    foreach ($c in $caps) {
        $usage = if ($c.IsRange -ne 0) { '0x{0:X2}..0x{1:X2}' -f $c.UsageMin, $c.UsageMax } else { '0x{0:X2}' -f $c.UsageMin }
        [pscustomobject]@{
            '# '        = $i
            UsagePage   = '0x{0:X4}' -f $c.UsagePage
            Usage       = $usage
            Name        = (Get-UsageName $c.UsagePage $c.UsageMin)
            ReportID    = $c.ReportID
            BitSize     = $c.BitSize
            ReportCount = $c.ReportCount
            Logical     = '{0}..{1}' -f $c.LogicalMin, $c.LogicalMax
            Physical    = '{0}..{1}' -f $c.PhysicalMin, $c.PhysicalMax
            Flags       = "rel=$($c.IsAbsolute -eq 0) null=$($c.HasNull) bits=$($c.BitField)"
            LinkColl    = "{0}:0x{1:X2} ({2})" -f $c.LinkUsagePage, $c.LinkUsage, (Get-UsageName $c.LinkUsagePage $c.LinkUsage)
            DataIndex   = '{0}..{1}' -f $c.DataIndexMin, $c.DataIndexMax
        }
        $i++
    }
}

function Format-ButtonCaps($caps) {
    $i = 0
    foreach ($c in $caps) {
        $usage = if ($c.IsRange -ne 0) { '0x{0:X2}..0x{1:X2}' -f $c.UsageMin, $c.UsageMax } else { '0x{0:X2}' -f $c.UsageMin }
        [pscustomobject]@{
            '# '        = $i
            UsagePage   = '0x{0:X4}' -f $c.UsagePage
            Usage       = $usage
            Name        = (Get-UsageName $c.UsagePage $c.UsageMin)
            ReportID    = $c.ReportID
            BitSize     = $c.BitSize
            ReportCount = $c.ReportCount
            DataIndex   = '{0}..{1}' -f $c.DataIndexMin, $c.DataIndexMax
        }
        $i++
    }
}

foreach ($d in $targets) {
    $full = [HidProbe]::Open($d.Path, $true)
    Write-Host ''
    Write-Host ('=' * 100)
    Write-Host "PATH          : $($full.Path)"
    Write-Host ('VID:PID       : {0:X4}:{1:X4}  Version=0x{2:X4}' -f $full.Attr.VendorID, $full.Attr.ProductID, $full.Attr.VersionNumber)
    Write-Host "MANUFACTURER  : $($full.Manufacturer)"
    Write-Host "PRODUCT       : $($full.Product)"
    Write-Host "SERIAL        : $($full.Serial)"
    $c = $full.Caps
    Write-Host ('CAPS          : UsagePage=0x{0:X4} Usage=0x{1:X4} ({2})' -f $c.UsagePage, $c.Usage, (Get-UsageName $c.UsagePage $c.Usage))
    Write-Host ('  InReport={0}B OutReport={1}B Feature={2}B  LinkColl={3}' -f $c.InputReportByteLength, $c.OutputReportByteLength, $c.FeatureReportByteLength, $c.NumberLinkCollectionNodes)
    Write-Host ('  Input : Buttons={0} Values={1} DataIdx={2}' -f $c.NumberInputButtonCaps, $c.NumberInputValueCaps, $c.NumberInputDataIndices)
    Write-Host ('  Output: Buttons={0} Values={1} DataIdx={2}' -f $c.NumberOutputButtonCaps, $c.NumberOutputValueCaps, $c.NumberOutputDataIndices)
    Write-Host ('  Feat  : Buttons={0} Values={1} DataIdx={2}' -f $c.NumberFeatureButtonCaps, $c.NumberFeatureValueCaps, $c.NumberFeatureDataIndices)

    Write-Host ''
    Write-Host '--- INPUT ValueCaps ---'
    Format-ValueCaps $full.InputValues | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
    Write-Host '--- INPUT ButtonCaps ---'
    Format-ButtonCaps $full.InputButtons | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
    if ($full.FeatureValues.Count -or $full.FeatureButtons.Count) {
        Write-Host '--- FEATURE ValueCaps ---'
        Format-ValueCaps $full.FeatureValues | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
        Write-Host '--- FEATURE ButtonCaps ---'
        Format-ButtonCaps $full.FeatureButtons | Format-Table -AutoSize | Out-String -Width 300 | Write-Host
    }

    if ($full.ReportDescriptor) {
        Write-Host "--- RAW REPORT DESCRIPTOR ($($full.ReportDescriptor.Length) bytes) ---"
        $sb = New-Object Text.StringBuilder
        for ($i = 0; $i -lt $full.ReportDescriptor.Length; $i++) {
            [void]$sb.Append(('{0:X2} ' -f $full.ReportDescriptor[$i]))
            if (($i % 16) -eq 15) { [void]$sb.AppendLine() }
        }
        Write-Host $sb.ToString()
    }
}
