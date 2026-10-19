# HID の IOCTL コード空間を走査し、hidclass が認識しているコードを特定する調査用スクリプト
param([int]$From = 0x000B0000, [int]$To = 0x000B0200)
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidScan {
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

  public delegate string Sink(uint code, bool ok, int len, int err, byte[] outb, int mode);

  public static void Scan(string path, uint from, uint to, Sink sink) {
    var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (h.IsInvalid) { Console.WriteLine("OPEN_FAIL err=" + Marshal.GetLastWin32Error()); h.Dispose(); return; }
    try {
      byte[] big = new byte[4096];
      for (uint code = from; code <= to; code++) {
        // mode 0: 入力バッファなし
        int br = 0;
        bool ok = DeviceIoControl(h, code, null, 0, big, big.Length, out br, IntPtr.Zero);
        int err = Marshal.GetLastWin32Error();
        if (err != 1) Console.WriteLine(sink(code, ok, br, err, big, 0));
        // mode 1: 入力バッファあり
        br = 0;
        ok = DeviceIoControl(h, code, big, big.Length, big, big.Length, out br, IntPtr.Zero);
        err = Marshal.GetLastWin32Error();
        if (err != 1) Console.WriteLine(sink(code, ok, br, err, big, 1));
      }
    } finally { h.Dispose(); }
  }
}
'@
Add-Type -TypeDefinition $cs -Language CSharp

$path = [HidScan]::Enumerate() | Where-Object { $_ -match 'vid&0002045e_pid&065d' } | Select-Object -First 1
Write-Output "TARGET: $path"
$sink = [HidScan+Sink]{
    param($code, $ok, $len, $err, $outb, $mode)
    $hex = ''
    if ($ok -and $len -gt 0) { $hex = ' hex=' + (($outb[0..([Math]::Min($len, 48) - 1)] | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
    return ('  0x{0:X8} mode={1} ok={2} len={3} err={4}{5}' -f $code, $mode, $ok, $len, $err, $hex)
}
[HidScan]::Scan($path, [uint32]$From, [uint32]$To, $sink)
