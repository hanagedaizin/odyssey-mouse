#requires -Version 5.1
<#
  hid-collections.ps1 - HID のリンクコレクション構造をダンプする

  HIDP_LINK_COLLECTION_NODE と、各 ValueCap / ButtonCap の LinkCollection を突き合わせて、
  「どのコレクション(用途グループ)に、どの DataIndex が属するか」を表示する。
  これにより スティック / タッチパッド / センサー / ボタン のまとまりが分かる。
#>
[CmdletBinding()]
param(
    [int]$VendorId = 0x045E,
    [int]$ProductId = 0x065D
)
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidColl
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

    [StructLayout(LayoutKind.Sequential)] public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
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
    [StructLayout(LayoutKind.Sequential)] public struct HIDP_LINK_COLLECTION_NODE
    {
        public ushort LinkUsage, LinkUsagePage;
        public ushort Parent, NumberOfChildren, NextSibling, FirstChild;
        public uint Bits; public IntPtr UserContext;
    }

    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetValueCaps(int t, [In,Out] HIDP_VALUE_CAPS[] c, ref ushort n, IntPtr pp);
    [DllImport("hid.dll", SetLastError=true)] public static extern int HidP_GetButtonCaps(int t, [In,Out] HIDP_BUTTON_CAPS[] c, ref ushort n, IntPtr pp);
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
                try { Marshal.WriteInt32(b, 0, IntPtr.Size == 8 ? 8 : 6);
                  if (SetupDiGetDeviceInterfaceDetail(s, ref x, b, need, out need, IntPtr.Zero))
                    r.Add(Marshal.PtrToStringAuto(new IntPtr(b.ToInt64() + 4)));
                } finally { Marshal.FreeHGlobal(b); }
            }
        } finally { SetupDiDestroyDeviceInfoList(s); }
        return r;
    }

    public class Out {
        public HIDP_CAPS Caps;
        public HIDP_LINK_COLLECTION_NODE[] Nodes;
        public List<HIDP_VALUE_CAPS> InValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> InButtons = new List<HIDP_BUTTON_CAPS>();
        public List<HIDP_VALUE_CAPS> OutValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> OutButtons = new List<HIDP_BUTTON_CAPS>();
        public List<HIDP_VALUE_CAPS> FeatureValues = new List<HIDP_VALUE_CAPS>();
        public List<HIDP_BUTTON_CAPS> FeatureButtons = new List<HIDP_BUTTON_CAPS>();
    }

    public static Out Get(string path)
    {
        var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        try {
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return null;
            try {
                var o = new Out();
                o.Caps = new HIDP_CAPS(); o.Caps.Reserved = new ushort[17];
                HidP_GetCaps(pp, ref o.Caps);
                uint nl = o.Caps.NumberLinkCollectionNodes;
                if (nl > 0) {
                    var nodes = new HIDP_LINK_COLLECTION_NODE[nl];
                    if (HidP_GetLinkCollectionNodes(nodes, ref nl, pp) >= 0) o.Nodes = nodes;
                }
                ushort n = o.Caps.NumberInputValueCaps;
                if (n > 0) { var a = new HIDP_VALUE_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetValueCaps(HIDP_INPUT, a, ref n, pp) >= 0) o.InValues.AddRange(a); }
                n = o.Caps.NumberInputButtonCaps;
                if (n > 0) { var a = new HIDP_BUTTON_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetButtonCaps(HIDP_INPUT, a, ref n, pp) >= 0) o.InButtons.AddRange(a); }
                n = o.Caps.NumberOutputValueCaps;
                if (n > 0) { var a = new HIDP_VALUE_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetValueCaps(HIDP_OUTPUT, a, ref n, pp) >= 0) o.OutValues.AddRange(a); }
                n = o.Caps.NumberOutputButtonCaps;
                if (n > 0) { var a = new HIDP_BUTTON_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetButtonCaps(HIDP_OUTPUT, a, ref n, pp) >= 0) o.OutButtons.AddRange(a); }
                n = o.Caps.NumberFeatureValueCaps;
                if (n > 0) { var a = new HIDP_VALUE_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetValueCaps(HIDP_FEATURE, a, ref n, pp) >= 0) o.FeatureValues.AddRange(a); }
                n = o.Caps.NumberFeatureButtonCaps;
                if (n > 0) { var a = new HIDP_BUTTON_CAPS[n]; for (int i=0;i<n;i++) a[i].Reserved2=new ushort[5]; if (HidP_GetButtonCaps(HIDP_FEATURE, a, ref n, pp) >= 0) o.FeatureButtons.AddRange(a); }
                return o;
            } finally { HidD_FreePreparsedData(pp); }
        } finally { h.Dispose(); }
    }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp

function U([int]$page, [int]$usage) {
    $pages = @{0='Undefined';1='GenericDesktop';2='Simulation';3='VR';4='Sport';5='Game';6='GenericDeviceControls';7='Keyboard';8='LED';9='Button';0xA='Ordinal';0xB='Telephony';0xC='Consumer';0xD='Digitizer';0xE='Haptics';0x10='Unicode';0x12='EyeHeadTracker';0x20='Sensors';0xFE='Vendor(0xFE)?';0xFF00='Vendor(0xFF00)'}
    $pn = if ($pages.ContainsKey($page)) { $pages[$page] } else { '0x{0:X4}' -f $page }
    if ($page -eq 1) {
        $gd = @{0x00='Undefined';0x01='Pointer';0x02='Mouse';0x04='Joystick';0x05='GamePad';0x06='Keyboard';0x07='Keypad';0x08='MultiAxis';0x09='TabletPCSystem';0x0E='SystemMultiAxis';0x0F='SpatialController';0x30='X';0x31='Y';0x32='Z';0x33='Rx';0x34='Ry';0x35='Rz';0x36='Slider';0x37='Dial';0x38='Wheel';0x39='HatSwitch';0x90='DpadUp';0x91='DpadDown';0x92='DpadRight';0x93='DpadLeft';0x94='?0x94'}
        if ($gd.ContainsKey($usage)) { return "$pn/$($gd[$usage])" }
    }
    if ($page -eq 9) { return "Button/$usage" }
    if ($page -eq 0xD) {
        $dg = @{0x00='Undefined';0x20='Stylus';0x22='Finger';0x30='TipPressure';0x32='InRange';0x33='Touch';0x42='TipSwitch';0x47='Confidence';0x48='Width';0x49='Height';0x51='ContactIdentifier';0x54='ContactCount';0x56='ScanTime';0x5B='Azimuth';0x5D='Twist';0x61='ContactCountMaximum'}
        if ($dg.ContainsKey($usage)) { return "$pn/$($dg[$usage])" }
    }
    return ('{0}/0x{1:X2}' -f $pn, $usage)
}

$target = [HidColl]::Enumerate() | Where-Object { $_ -match ('vid&[0-9a-f]*{0:x4}_pid&[0-9a-f]*{1:x4}' -f $VendorId, $ProductId) } | Select-Object -First 1
if (-not $target) { Write-Output '対象デバイスなし'; exit 1 }
$o = [HidColl]::Get($target)
if (-not $o) { Write-Output 'オープン失敗'; exit 1 }

Write-Output ("リンクコレクションノード数: {0}" -f $o.Nodes.Count)
Write-Output ''
Write-Output '--- リンクコレクション一覧 ---'
Write-Output ('  {0,-4} {1,-12} {2,-26} {3,6} {4,6} {5,6} {6,6}' -f 'idx','usagePage','usage','parent','child','sib','firstKid')
for ($i = 0; $i -lt $o.Nodes.Count; $i++) {
    $n = $o.Nodes[$i]
    Write-Output ('  {0,-4} 0x{1:X4}  {2,-26} {3,6} {4,6} {5,6} {6,6}' -f $i, $n.LinkUsagePage, (U $n.LinkUsagePage $n.LinkUsage), $n.Parent, $n.NumberOfChildren, $n.NextSibling, $n.FirstChild)
}

function ShowCaps($title, $values, $buttons) {
    Write-Output ''
    Write-Output "--- $title ---"
    Write-Output ('  {0,-5} {1,-4} {2,-6} {3,7} {4,-30} {5,6} {6,7} {7,12} {8,10}' -f 'idx','rep','type','linkcol','usage','bits','count','logical','flags')
    foreach ($c in $values) {
        $flag = @()
        if ($c.IsAbsolute -eq 0) { $flag += 'rel' }
        if ($c.HasNull -ne 0) { $flag += 'null' }
        $usage = if ($c.IsRange -ne 0) { '0x{0:X2}..0x{1:X2}' -f $c.UsageMin,$c.UsageMax } else { '0x{0:X2}' -f $c.UsageMin }
        $lab = if ($c.IsRange -ne 0) { (U $c.UsagePage $c.UsageMin) } else { (U $c.UsagePage $c.UsageMin) }
        Write-Output ('  {0,-5} {1,-4} val  {2,4}  {3,-30} {4,6} {5,7} {6,12} {7,10}' -f $c.DataIndexMin, $c.ReportID, $c.LinkCollection, $lab, $c.BitSize, $c.ReportCount, ('{0}..{1}' -f $c.LogicalMin,$c.LogicalMax), ($flag -join ','))
    }
    foreach ($c in $buttons) {
        $lab = if ($c.IsRange -ne 0) { (U $c.UsagePage $c.UsageMin) } else { (U $c.UsagePage $c.UsageMin) }
        Write-Output ('  {0,-5} {1,-4} btn  {2,4}  {3,-30} {4,6} {5,7} {6,12} {7,10}' -f $c.DataIndexMin, $c.ReportID, $c.LinkCollection, $lab, $c.BitSize, $c.ReportCount, ('{0}..{1}' -f $c.UsageMin,$c.UsageMax), '')
    }
}

ShowCaps 'INPUT ValueCaps' $o.InValues $null
ShowCaps 'INPUT ButtonCaps' @() $o.InButtons
ShowCaps 'OUTPUT ValueCaps' $o.OutValues $null
ShowCaps 'OUTPUT ButtonCaps' @() $o.OutButtons
ShowCaps 'FEATURE ValueCaps' $o.FeatureValues $null
ShowCaps 'FEATURE ButtonCaps' @() $o.FeatureButtons
