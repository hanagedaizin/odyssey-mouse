# HID の各種 IOCTL を実際に発行し、どれが成功するかを調べる調査用スクリプト
param([string]$Path = '')
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidIoctlProbe {
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
  public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inb, int inlen, byte[] outb, int outlen, out int br, IntPtr ov);
  [StructLayout(LayoutKind.Sequential)] public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);

  public static List<string> Enumerate() {
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

  public static string Probe(string path) {
    var sb = new StringBuilder();
    var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (h.IsInvalid) { h.Dispose(); return "  OPEN_FAIL err=" + Marshal.GetLastWin32Error(); }
    try {
      var att = new HIDD_ATTRIBUTES(); att.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
      HidD_GetAttributes(h, ref att);
      sb.AppendLine(string.Format("  VID={0:X4} PID={1:X4}", att.VendorID, att.ProductID));
      uint[] codes = new uint[] {
        0x000B0003, // HID_CTL_CODE(0)  GET_DEVICE_DESCRIPTOR
        0x000B0007, // HID_CTL_CODE(1)  GET_REPORT_DESCRIPTOR
        0x000B000B, // HID_CTL_CODE(2)  READ_REPORT
        0x000B000F, // HID_CTL_CODE(3)  WRITE_REPORT
        0x000B0013, // HID_CTL_CODE(4)  GET_STRING
        0x000B0017, // HID_CTL_CODE(5)  GET_INDEXED_STRING
        0x000B0193, // HID_CTL_CODE(100) GET_FEATURE (NEITHER)
        0x000B0197, // HID_CTL_CODE(101) SET_FEATURE/COLLECTION_INFORMATION
        0x000B019B, // HID_CTL_CODE(102)
        0x000B019F, // HID_CTL_CODE(103)
        0x000B0323, // HID_CTL_CODE(200) GET_HARDWARE_ID
        0x000B0327, // HID_CTL_CODE(201)
        0x000B0191, // よく使われるとされる値
      };
      foreach (uint code in codes) {
        byte[] inb = new byte[4096];
        byte[] outb = new byte[4096];
        int br = 0;
        bool ok = DeviceIoControl(h, code, inb, inb.Length, outb, outb.Length, out br, IntPtr.Zero);
        int err = Marshal.GetLastWin32Error();
        string extra = "";
        if (ok && br > 0) extra = "  bytes=" + br + " hex[" + Math.Min(br, 64) + "]=" + BitConverter.ToString(outb, 0, Math.Min(br, 64)).Replace("-", " ");
        sb.AppendLine(string.Format("  ioctl=0x{0:X8} ok={1} len={2} err={3}{4}", code, ok, br, err, extra));
      }
    } finally { h.Dispose(); }
    return sb.ToString();
  }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp

$targets = if ($Path) { @($Path) } else {
  $list = @()
  foreach ($p in [HidIoctlProbe]::Enumerate()) { if ($p -match 'vid&0002045e_pid&065d|vid_00ff&pid_bacc') { $list += $p } }
  if ($list.Count -eq 0) { $list = [HidIoctlProbe]::Enumerate() | Select-Object -First 3 }
  $list
}
foreach ($t in $targets) {
  Write-Output "PATH: $t"
  Write-Output ([HidIoctlProbe]::Probe($t))
}
