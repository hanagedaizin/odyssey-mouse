#requires -Version 5.1
<#
  rawinput-list.ps1 - Raw Input デバイス一覧と HID 情報を表示する

  Raw Input API で、この PC に認識されているデバイスを列挙する。
  HID デバイスについては UsagePage / Usage / バージョン、
  および PreparsedData が取得できるか（= HidP でデコード可能か）を表示する。

  使い方:
    powershell -ExecutionPolicy Bypass -File tools\rawinput-list.ps1
    powershell -ExecutionPolicy Bypass -File tools\rawinput-list.ps1 -Filter Odyssey
#>
[CmdletBinding()]
param([string]$Filter = '')

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class RawIn
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_MOUSE { public uint dwId, dwNumberOfButtons, dwSampleRate; public int fHasHorizontalWheel; }
    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_KEYBOARD { public uint dwType, dwSubType, dwKeyboardMode, dwNumberOfFunctionKeys, dwNumberOfIndicators, dwNumberOfKeysTotal; }
    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID { public uint dwVendorId, dwProductId, dwVersionNumber; public ushort usUsagePage, usUsage; }
    [StructLayout(LayoutKind.Explicit)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public RID_DEVICE_INFO_MOUSE mouse;
        [FieldOffset(8)] public RID_DEVICE_INFO_KEYBOARD keyboard;
        [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
    }

    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, byte[] pData, ref uint pcbSize);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, ref RID_DEVICE_INFO pData, ref uint pcbSize);

    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;

    public class Dev { public IntPtr hDevice; public uint type; public string name; public RID_DEVICE_INFO info; public int preparsedSize; public int preparsedStatus; }

    public static List<Dev> List()
    {
        var res = new List<Dev>();
        uint n = 0;
        uint sz = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
        if (GetRawInputDeviceList(IntPtr.Zero, ref n, sz) != 0) return res;
        IntPtr buf = Marshal.AllocHGlobal((int)(n * sz));
        try {
            if (GetRawInputDeviceList(buf, ref n, sz) == 0xFFFFFFFF) return res;
            for (uint i = 0; i < n; i++) {
                IntPtr p = new IntPtr(buf.ToInt64() + i * sz);
                var l = (RAWINPUTDEVICELIST)Marshal.PtrToStructure(p, typeof(RAWINPUTDEVICELIST));
                var d = new Dev(); d.hDevice = l.hDevice; d.type = l.dwType;
                // 名前
                uint need = 0;
                GetRawInputDeviceInfoW(d.hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref need);
                if (need > 0) {
                    IntPtr nb = Marshal.AllocHGlobal((int)(need * 2 + 4));
                    try {
                        uint n2 = need + 1;
                        if (GetRawInputDeviceInfoW(d.hDevice, RIDI_DEVICENAME, nb, ref n2) > 0)
                            d.name = Marshal.PtrToStringUni(nb);
                    } finally { Marshal.FreeHGlobal(nb); }
                }
                // デバイス情報
                var di = new RID_DEVICE_INFO();
                di.cbSize = (uint)Marshal.SizeOf(typeof(RID_DEVICE_INFO));
                uint dsz = di.cbSize;
                if (GetRawInputDeviceInfoW(d.hDevice, RIDI_DEVICEINFO, ref di, ref dsz) > 0) d.info = di;
                // PreparsedData が取れるか
                uint psz = 0;
                GetRawInputDeviceInfoW(d.hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref psz);
                d.preparsedSize = (int)psz;
                d.preparsedStatus = Marshal.GetLastWin32Error();
                res.Add(d);
            }
        } finally { Marshal.FreeHGlobal(buf); }
        return res;
    }
}
'@
if (-not ('RawIn' -as [type])) { Add-Type -TypeDefinition $cs -Language CSharp }

$devs = [RawIn]::List()
Write-Output ("Raw Input デバイス数: {0}" -f $devs.Count)
Write-Output ''
foreach ($d in $devs) {
    $type = switch ($d.type) { 0 { 'MOUSE' } 1 { 'KEYBOARD' } 2 { 'HID' } default { "?$($d.type)" } }
    $name = $d.name
    if ($Filter -and ($name -notmatch $Filter)) { continue }
    if ($type -eq 'HID') {
        Write-Output ("[{0}] {1}" -f $type, $name)
        Write-Output ("      VID:PID=0x{0:X4}:0x{1:X4} ver=0x{2:X4} UsagePage=0x{3:X2} Usage=0x{4:X2} (PreparsedData={5} bytes)" -f `
            $d.info.hid.dwVendorId, $d.info.hid.dwProductId, $d.info.hid.dwVersionNumber, $d.info.hid.usUsagePage, $d.info.hid.usUsage, $d.preparsedSize)
    } else {
        Write-Output ("[{0}] {1}" -f $type, $name)
    }
}
