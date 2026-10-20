# ============================================================================
#  Odyssey コントローラの HID アクセス権を修正する（管理者として実行）
#
#  症状:
#    Bluetooth では接続されているのに、SensorViewer / OdysseyMouse が
#    「コントローラ未検出」になり、HID を開くとアクセス拒否 (error 5) になる。
#
#  原因:
#    Windows の拡張 INF (oem127.inf / Motion Controller) が
#      HKR,,Security,,"D:P(A;;GA;;;SY)(A;;GRGW;;;BA)(A;;GRGW;;;UD)(A;;GRGW;;;S-1-5-90-0)"
#    を書き込み、管理者と SYSTEM 以外を締め出す。ペアリングし直すと再適用される。
#
#  対処:
#    デバイスのセキュリティ記述子に INTERACTIVE (S-1-5-4) の読み書きを追加し、
#    デバイスを再起動する。元に戻すときは同じスクリプトで SDDL から IU を外す。
#
#  使い方:
#    1. スタートメニューで PowerShell を右クリック → 「管理者として実行」
#    2. このファイルを実行:
#         powershell -ExecutionPolicy Bypass -File .\fix-hid-acl.ps1
# ============================================================================

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Write-Host "管理者権限が必要です。PowerShell を「管理者として実行」してから、もう一度お試しください。" -ForegroundColor Yellow
    exit 1
}

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class HidAclFix
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
        StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiSetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property, byte[] PropertyBuffer, uint PropertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiRestartDevices(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string StringSecurityDescriptor, uint StringSDRevision, out IntPtr SecurityDescriptor, out uint SecurityDescriptorSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    public static string Fix(string sddl)
    {
        var sb = new StringBuilder();
        IntPtr sd;
        uint sdLen;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out sd, out sdLen))
            return "SDDL の変換に失敗しました: " + Marshal.GetLastWin32Error();

        var bytes = new byte[sdLen];
        Marshal.Copy(sd, bytes, 0, (int)sdLen);
        LocalFree(sd);

        var hidClass = new Guid("745A17A0-74D3-11D0-B6FE-00A0C90F57DA");
        IntPtr set = SetupDiGetClassDevs(ref hidClass, IntPtr.Zero, IntPtr.Zero, 0x2); // DIGCF_PRESENT
        if (set == new IntPtr(-1))
            return "デバイス一覧の取得に失敗しました: " + Marshal.GetLastWin32Error();

        var info = new SP_DEVINFO_DATA();
        info.cbSize = (uint)Marshal.SizeOf(info);
        int found = 0;

        for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
        {
            var id = new StringBuilder(512);
            uint need;
            if (!SetupDiGetDeviceInstanceId(set, ref info, id, 512, out need)) continue;

            string s = id.ToString();
            if (s.IndexOf("VID&0002045E_PID&065D", StringComparison.OrdinalIgnoreCase) < 0) continue;

            found++;
            bool setOk = SetupDiSetDeviceRegistryProperty(set, ref info, 0x17 /* SPDRP_SECURITY */, bytes, sdLen);
            int setErr = Marshal.GetLastWin32Error();
            bool restartOk = setOk && SetupDiRestartDevices(set, ref info);
            int restartErr = Marshal.GetLastWin32Error();

            sb.AppendLine(s);
            sb.AppendLine("  セキュリティ設定: " + (setOk ? "OK" : "NG (" + setErr + ")") +
                          "  再起動: " + (restartOk ? "OK" : "NG (" + restartErr + ")"));
        }

        SetupDiDestroyDeviceInfoList(set);
        if (found == 0) sb.AppendLine("対象デバイス (VID_045E&PID_065D) が見つかりません。");
        return sb.ToString();
    }
}
"@

# 元の SD に INTERACTIVE (IU) の読み書きを追加したもの
$sddl = 'D:P(A;;GA;;;SY)(A;;GWGR;;;BA)(A;;GWGR;;;IU)(A;;GWGR;;;UD)(A;;GWGR;;;S-1-5-90-0)'

Write-Host "HID のアクセス権を修正しています..." -ForegroundColor Cyan
$result = [HidAclFix]::Fix($sddl)
Write-Host $result
Write-Host ""
Write-Host "完了しました。コントローラを一度スリープさせてから、もう一度つないでください。" -ForegroundColor Green
