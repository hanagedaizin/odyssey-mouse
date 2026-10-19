# IOCTL_HID_GET_REPORT_DESCRIPTOR がどの HID デバイスで成功するかを確認する調査用スクリプト
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidIoctlTest {
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
  public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inb, int inlen, byte[] outb, int outlen, out int br, IntPtr ov);
  [DllImport("kernel32.dll", EntryPoint="DeviceIoControl", SetLastError=true)]
  public static extern bool DeviceIoControlPtr(SafeFileHandle h, uint code, IntPtr inb, int inlen, IntPtr outb, int outlen, out int br, IntPtr ov);
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
  [StructLayout(LayoutKind.Sequential)] public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

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

  public static string Test(string path) {
    var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (h.IsInvalid) { h.Dispose(); return "OPEN_FAIL err=" + Marshal.GetLastWin32Error(); }
    var att = new HIDD_ATTRIBUTES(); att.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
    HidD_GetAttributes(h, ref att);
    var sb = new StringBuilder();
    sb.Append(string.Format("VID={0:X4} PID={1:X4} : ", att.VendorID, att.ProductID));
    // 配列渡し
    int br = 0;
    byte[] buf = new byte[4096];
    bool ok = DeviceIoControl(h, 0x000B0007, IntPtr.Zero, 0, buf, buf.Length, out br, IntPtr.Zero);
    sb.Append("arr:ok=" + ok + ",len=" + br + ",err=" + Marshal.GetLastWin32Error() + "  ");
    // IntPtr 渡し
    IntPtr pb = Marshal.AllocHGlobal(4096);
    try {
      br = 0;
      bool ok2 = DeviceIoControlPtr(h, 0x000B0007, IntPtr.Zero, 0, pb, 4096, out br, IntPtr.Zero);
      sb.Append("ptr:ok=" + ok2 + ",len=" + br + ",err=" + Marshal.GetLastWin32Error());
      if (ok2 && br > 0) {
        byte[] dd = new byte[br]; Marshal.Copy(pb, dd, 0, br);
        sb.Append("  hex=" + BitConverter.ToString(dd).Replace("-", " "));
      }
    } finally { Marshal.FreeHGlobal(pb); }
    h.Dispose();
    return sb.ToString();
  }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp
foreach ($p in [HidIoctlTest]::Enumerate()) { Write-Output ([HidIoctlTest]::Test($p)) }
