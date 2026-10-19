// Odyssey センサビジュアライザ
//
// Samsung Odyssey (Windows Mixed Reality) モーションコントローラの入力レポート 1 に
// 同梱されている IMU / コンパス生値を、そのままグラフ表示する開発用ツール。
//
//   加速度  idx13/14/15 (0x453/454/455, 24bit 符号)   ... docs/spec §3-2
//   ジャイロ idx17/18/19 (0x457/458/459, 24bit 符号)
//   コンパス idx21/22/23 (0x485/486/487, 16bit 符号)
//   温度    idx16 (0x434, 0.01C) / タイムスタンプ idx20 (0x529, 100ns) / 状態 idx24 (0x18)
//
// 読み取りは app/src/Native.cs と同じ経路 (CreateFile + ReadFile + HidP_GetData)。
// OdysseyMouse.exe と同時に開いても問題ない (共有モード / 複数リーダー可)。
//
// 使い方:
//   SensorViewer.exe                     ウィンドウ表示
//   SensorViewer.exe -Window 10          表示窓を 10 秒に
//   SensorViewer.exe -SelfTest 8         UI なしで 8 秒読み、統計を %TEMP% に出して終了
//
// キー: Space=一時停止 / B=ジャイロ基準化 / W=表示窓切替 / S=CSV 保存 / Esc=終了
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace SensorViewer
{
    // ---------------------------------------------------------------- 埋め込み API
    internal static class Nt
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
        public struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage; public byte ReportID; public byte IsAlias; public ushort BitField;
            public ushort LinkCollection, LinkUsage, LinkUsagePage;
            public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
            public ushort BitSize, ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
            public uint UnitsExp, Units;
            public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
            public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_DATA { public ushort DataIndex; public ushort Reserved; public uint RawValue; }

        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern int HidP_GetValueCaps(int t, [In, Out] HIDP_VALUE_CAPS[] c, ref ushort n, IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern int HidP_GetData(int t, [In, Out] HIDP_DATA[] d, ref uint n, IntPtr pp, byte[] report, uint len);

        public const int HIDP_INPUT = 0;
        public const int ERROR_TIMEOUT = 258;
    }

    internal struct FieldDef
    {
        public int Index;   // DataIndex
        public int Bits;    // ビット幅（符号拡張に使う）
        public FieldDef(int index, int bits) { Index = index; Bits = bits; }
    }

    // ---------------------------------------------------------------- 1 サンプル
    internal struct Sample
    {
        public double T;        // 秒（Stopwatch 基準）
        public long Ts;         // デバイスのタイムスタンプ (100ns)
        public bool HasAcc, HasGyro, HasMag;   // このレポートに含まれていたか（スリープ時は false）
        public int Ax, Ay, Az, Gx, Gy, Gz, Mx, My, Mz;
        public int Temp;        // 0.01 C
        public int Status;      // 0..3（0 = IMU 停止）
    }

    // ---------------------------------------------------------------- サンプルリング
    internal sealed class SampleRing
    {
        readonly Sample[] _a;
        long _count;
        public SampleRing(int capacity) { _a = new Sample[capacity]; }

        public void Push(Sample s)
        {
            _a[_count % _a.Length] = s;
            _count++;
        }
        public int Count { get { return (int)Math.Min(_count, _a.Length); } }
        public long Total { get { return _count; } }
        public Sample At(int i)
        {
            int start = (int)((_count - Count + _a.Length) % _a.Length);
            return _a[(start + i) % _a.Length];
        }
        public Sample Last { get { return Count > 0 ? At(Count - 1) : new Sample(); } }

        /// <summary>tMin より新しいサンプルを dst へ詰める（古い順）。</summary>
        public void CopySince(double tMin, List<Sample> dst, int maxCount)
        {
            dst.Clear();
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                Sample s = At(i);
                if (s.T >= tMin) dst.Add(s);
            }
            if (dst.Count > maxCount) dst.RemoveRange(0, dst.Count - maxCount);
        }
    }

    // ---------------------------------------------------------------- HID 読み取り
    internal sealed class Controller : IDisposable
    {
        const uint GENERIC_READ = 0x80000000;
        const uint SHARE_RW = 3;
        const uint OPEN_EXISTING = 3;
        const uint FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;

        static readonly Guid HID_GUID = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

        readonly int _vid, _pid;
        SafeFileHandle _h;
        IntPtr _pp = IntPtr.Zero;
        IntPtr _ev = IntPtr.Zero;
        byte[] _buf;
        string _path;
        readonly Dictionary<int, uint> _vals = new Dictionary<int, uint>(32);
        readonly Dictionary<ushort, FieldDef> _fields = new Dictionary<ushort, FieldDef>(32);

        public Controller(int vid, int pid) { _vid = vid; _pid = pid; }

        public bool IsOpen { get { return _h != null && !_h.IsInvalid; } }
        public int LastErr { get; private set; }
        public string Path { get { return _path; } }
        public IDictionary<ushort, FieldDef> Fields { get { return _fields; } }

        public static string FindPath(int vid, int pid)
        {
            Guid guid = HID_GUID;
            IntPtr s = Nt.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (s == new IntPtr(-1)) return null;
            try
            {
                var x = new Nt.SP_DEVICE_INTERFACE_DATA();
                x.cbSize = Marshal.SizeOf(typeof(Nt.SP_DEVICE_INTERFACE_DATA));
                for (int i = 0; ; i++)
                {
                    if (!Nt.SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref guid, i, ref x)) break;
                    int need;
                    Nt.SetupDiGetDeviceInterfaceDetail(s, ref x, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need <= 0) continue;
                    IntPtr b = Marshal.AllocHGlobal(need);
                    string p = null;
                    try
                    {
                        Marshal.WriteInt32(b, 0, IntPtr.Size == 8 ? 8 : 6);
                        if (Nt.SetupDiGetDeviceInterfaceDetail(s, ref x, b, need, out need, IntPtr.Zero))
                            p = Marshal.PtrToStringAuto(new IntPtr(b.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(b); }
                    if (p != null && MatchesPath(p, vid, pid)) return p;
                }
            }
            finally { Nt.SetupDiDestroyDeviceInfoList(s); }
            return null;
        }

        // Bluetooth HID のパスは先頭に "0002" が付くので末尾 4 桁で判定する（app と同じ）。
        public static bool MatchesPath(string path, int vid, int pid)
        {
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

        public bool Open()
        {
            string p = FindPath(_vid, _pid);
            if (p == null) return false;
            return OpenPath(p);
        }

        public bool OpenPath(string path)
        {
            Close();
            _path = path;
            _h = Nt.CreateFile(path, GENERIC_READ, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FLAG_OVERLAPPED, IntPtr.Zero);
            if (_h.IsInvalid) { _h = null; return false; }
            _ev = Nt.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (!Nt.HidD_GetPreparsedData(_h, out _pp)) { Close(); return false; }
            var caps = new Nt.HIDP_CAPS();
            caps.Reserved = new ushort[17];
            Nt.HidP_GetCaps(_pp, ref caps);
            _buf = new byte[Math.Max((int)caps.InputReportByteLength, 64) + 8];
            BuildFields();
            return true;
        }

        /// <summary>vendor ページ (0xFE) の値フィールドを Usage から DataIndex へ写像する。</summary>
        void BuildFields()
        {
            _fields.Clear();
            var caps = new Nt.HIDP_VALUE_CAPS[64];
            for (int i = 0; i < caps.Length; i++) caps[i].Reserved2 = new ushort[5];
            ushort n = (ushort)caps.Length;
            if (Nt.HidP_GetValueCaps(Nt.HIDP_INPUT, caps, ref n, _pp) < 0) return;
            for (int i = 0; i < n; i++)
            {
                if (caps[i].UsagePage != 0x00FE) continue;
                // 非レンジのときは UsageMin が Usage と同じ位置に載る（NotRange.Usage）
                _fields[caps[i].UsageMin] = new FieldDef((int)caps[i].DataIndexMin, caps[i].BitSize);
            }
        }

        public void Close()
        {
            if (_pp != IntPtr.Zero) { Nt.HidD_FreePreparsedData(_pp); _pp = IntPtr.Zero; }
            if (_h != null) { try { _h.Dispose(); } catch { } _h = null; }
            if (_ev != IntPtr.Zero) { Nt.CloseHandle(_ev); _ev = IntPtr.Zero; }
        }

        public void Dispose() { Close(); }

        public int Read(int timeoutMs)
        {
            _vals.Clear();
            if (!IsOpen || _ev == IntPtr.Zero) return 0;
            Nt.ResetEvent(_ev);
            var ov = new Nt.OVERLAPPED();
            ov.hEvent = _ev;
            int r = 0;
            if (!Nt.ReadFile(_h, _buf, _buf.Length, IntPtr.Zero, ref ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING) { LastErr = err; return 0; }
                if (Nt.WaitForSingleObject(_ev, (uint)timeoutMs) != 0)
                {
                    Nt.CancelIo(_h);
                    Nt.WaitForSingleObject(_ev, 200);
                    Nt.ResetEvent(_ev);
                    LastErr = Nt.ERROR_TIMEOUT;
                    return 0;
                }
            }
            if (!Nt.GetOverlappedResult(_h, ref ov, out r, false)) { LastErr = Marshal.GetLastWin32Error(); return 0; }
            LastErr = 0;
            if (r <= 0) return 0;
            uint cnt = 512;
            var d = new Nt.HIDP_DATA[512];
            if (Nt.HidP_GetData(0, d, ref cnt, _pp, _buf, (uint)r) >= 0)
                for (uint i = 0; i < cnt; i++) _vals[d[i].DataIndex] = d[i].RawValue;
            return r;
        }

        static long SignExtend(uint v, int bits)
        {
            if (bits >= 32) return (long)v;
            uint mask = (1u << bits) - 1u;
            v &= mask;
            uint sign = 1u << (bits - 1);
            return ((v & sign) != 0) ? (long)v - (1L << bits) : (long)v;
        }

        long Get(ushort usage, out bool has)
        {
            FieldDef f;
            has = false;
            if (!_fields.TryGetValue(usage, out f)) return 0;
            uint raw;
            if (!_vals.TryGetValue(f.Index, out raw)) return 0;   // レポートに無い＝値なし
            has = true;
            return SignExtend(raw, f.Bits);
        }

        /// <summary>状態フラグ (2bit) のように符号拡張してはいけないフィールド用。</summary>
        long GetUnsigned(ushort usage, out bool has)
        {
            FieldDef f;
            has = false;
            if (!_fields.TryGetValue(usage, out f)) return 0;
            uint raw;
            if (!_vals.TryGetValue(f.Index, out raw)) return 0;
            has = true;
            return (long)raw;
        }

        public Sample Decode(double t)
        {
            var s = new Sample();
            s.T = t;
            bool h;
            s.Ax = (int)Get(0x453, out h); s.HasAcc = h;
            s.Ay = (int)Get(0x454, out h); s.HasAcc |= h;
            s.Az = (int)Get(0x455, out h); s.HasAcc |= h;
            s.Gx = (int)Get(0x457, out h); s.HasGyro = h;
            s.Gy = (int)Get(0x458, out h); s.HasGyro |= h;
            s.Gz = (int)Get(0x459, out h); s.HasGyro |= h;
            s.Mx = (int)Get(0x485, out h); s.HasMag = h;
            s.My = (int)Get(0x486, out h); s.HasMag |= h;
            s.Mz = (int)Get(0x487, out h); s.HasMag |= h;
            s.Temp = (int)Get(0x434, out h);
            s.Status = (int)GetUnsigned(0x18, out h);   // 2bit フラグなので符号拡張しない
            s.Ts = Get(0x529, out h);
            return s;
        }
    }

    // ---------------------------------------------------------------- 定数
    internal static class Consts
    {
        public const double AccPerG = 490000.0;    // 1g ≒ 49 万カウント（推定、docs/spec §3-2）
        public const double GyroPerDps = 4194.0;   // ±2000 deg/s 想定 → 4194 counts/(deg/s)
    }

    // ---------------------------------------------------------------- 姿勢推定
    /// <summary>
    /// ジャイロ積分＋加速度（重力）補正の相補フィルタ＋コンパスによる yaw 補正。
    /// q は「体座標 → 世界座標」のクォータニオン。世界座標は Z=上（重力補正の基準）で水平面は XY。
    /// さらに表示合わせ用の固定回転 B（体座標系）を持ち、D = q ⊗ B が「見た目の姿勢」。
    /// C キーのキャリブレーションは B を決める＝その瞬間の向きを identity にする。
    /// </summary>
    internal sealed class Orient
    {
        public double W = 1.0, Xq, Yq, Zq;   // クォータニオン (w,x,y,z)
        public double GyroFactor = 1.0;      // スケール見直し用（[ / ] キー）
        public double AccGain = 2.0;         // 重力補正の強さ (1/秒, τ ≒ 0.5 秒)

        // --- 表示合わせ（キャリブレーション） ---
        public double BW = 1.0, BX, BY, BZ;  // 体座標まわりの固定回転（B = q⁻¹ で identity 化）
        public double DW = 1.0, DX, DY, DZ;  // 表示姿勢 D = q ⊗ B（Update 毎に合成）

        // --- コンパス（地磁気）による yaw 補正 ---
        public bool MagYaw = true;           // 既定 ON（M キーで切替）
        public double MagGain = 0.5;         // yaw 補正の強さ (1/秒, τ ≒ 2 秒)
        public double MagWorldAz;            // 世界座標での磁気方位 [rad]（ノックに使う）
        public bool MagRefInit { get { return _psiRefInit; } }
        double _psiRef;                      // 基準となる磁気方位 [rad]
        bool _psiRefInit;

        double _bx, _by, _bz;                // ジャイロのバイアス (counts)
        double _lastT = -1.0;

        public void SetBias(double x, double y, double z) { _bx = x; _by = y; _bz = z; }
        public void Reset()
        {
            W = 1; Xq = Yq = Zq = 0;
            BW = 1; BX = BY = BZ = 0;
            DW = 1; DX = DY = DZ = 0;
            _lastT = -1; _psiRefInit = false;
        }
        public void ResetMagRef() { _psiRefInit = false; }

        /// <summary>いま向いている方角を identity にする（この向きに構えて C キー）。</summary>
        public void SetCalibration()
        {
            // D = q ⊗ B = identity となる B = q⁻¹ = 共役（単位クォータニオン）
            BW = W; BX = -Xq; BY = -Yq; BZ = -Zq;
            ComposeDisp();
        }

        void ComposeDisp()
        {
            // D = q ⊗ B（まず B で体座標を見立て直してから世界へ写す）
            DW = W * BW - Xq * BX - Yq * BY - Zq * BZ;
            DX = W * BX + Xq * BW + Yq * BZ - Zq * BY;
            DY = W * BY - Xq * BZ + Yq * BW + Zq * BX;
            DZ = W * BZ + Xq * BY - Yq * BX + Zq * BW;
        }

        /// <summary>体座標 → 世界（キャリブレーション後の「見た目の向き」はこちら）。</summary>
        public void RotateDisp(out double ox, out double oy, out double oz, double vx, double vy, double vz)
        { RotateQ(DW, DX, DY, DZ, vx, vy, vz, out ox, out oy, out oz); }

        /// <summary>体の前向き（+X 軸）の世界方位 [deg]。Z=上 のため XY 面の角。</summary>
        public double YawDeg
        {
            get
            {
                double fx, fy, fz;
                RotateDisp(out fx, out fy, out fz, 1, 0, 0);
                double d = Math.Atan2(fy, fx) * 180.0 / Math.PI;
                while (d < 0) d += 360; while (d >= 360) d -= 360;
                return d;
            }
        }

        static double WrapPi(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        void Normalize()
        {
            double n = Math.Sqrt(W * W + Xq * Xq + Yq * Yq + Zq * Zq);
            if (n > 1e-9) { W /= n; Xq /= n; Yq /= n; Zq /= n; }
        }

        /// <summary>1 サンプルぶん回す。countsPerDps は「1 deg/s が何 counts か」。</summary>
        public void Update(Sample s, double countsPerDps)
        {
            UpdateCore(s, countsPerDps);
            ComposeDisp();     // 表示姿勢 D = q ⊗ B を毎サンプル更新
        }

        void UpdateCore(Sample s, double countsPerDps)
        {
            double dt = (_lastT < 0) ? 0 : (s.T - _lastT);
            _lastT = s.T;
            // IMU スリープ明けなどの巨大な dt は積分しない（姿勢が一瞬で飛ぶのを防ぐ）
            if (!s.HasGyro || dt <= 0 || dt > 0.2) return;

            double gx = (s.Gx - _bx) / countsPerDps * GyroFactor * (Math.PI / 180.0);
            double gy = (s.Gy - _by) / countsPerDps * GyroFactor * (Math.PI / 180.0);
            double gz = (s.Gz - _bz) / countsPerDps * GyroFactor * (Math.PI / 180.0);

            if (s.HasAcc)
            {
                double an = Math.Sqrt((double)s.Ax * s.Ax + (double)s.Ay * s.Ay + (double)s.Az * s.Az);
                // 重力だけのとき（1g ±30%）だけ補正に使う。振り中は補正しない。
                if (an > Consts.AccPerG * 0.7 && an < Consts.AccPerG * 1.3)
                {
                    double ax = s.Ax / an, ay = s.Ay / an, az = s.Az / an;  // 測定「上」方向
                    double px, py, pz;
                    RotateInv(out px, out py, out pz, 0, 0, 1);             // 推定「上」方向（体座標）
                    double ex = py * az - pz * ay;                          // cross(推定, 測定) = 誤差角
                    double ey = pz * ax - px * az;
                    double ez = px * ay - py * ax;
                    double k = AccGain;      // 誤差 → 角速度 (1/秒)。積分側で dt が掛かるのでここでは掛けない
                    gx -= ex * k; gy -= ey * k; gz -= ez * k;
                }
            }

            double qw = W, qx = Xq, qy = Yq, qz = Zq;
            W  = qw + 0.5 * (-qx * gx - qy * gy - qz * gz) * dt;
            Xq = qx + 0.5 * ( qw * gx + qy * gz - qz * gy) * dt;
            Yq = qy + 0.5 * ( qw * gy - qx * gz + qz * gx) * dt;
            Zq = qz + 0.5 * ( qw * gz + qx * gy - qy * gx) * dt;
            Normalize();

            // --- コンパスによる yaw 補正（世界 Z=上 なので水平回りだけ直す。傾きには影響しない） ---
            // 世界座標に写した磁気方位が基準方位のままなら yaw はドリフトしていない。
            // 地磁気はハードアイアン未補正なので誤差はあるが、無限ドリフトは防げる。
            if (s.HasMag)
            {
                double mx, my, mz;
                Rotate(out mx, out my, out mz, s.Mx, s.My, s.Mz);   // 体 → 世界
                double magLen = Math.Sqrt(mx * mx + my * my + mz * mz);
                if (magLen > 1e-6)
                {
                    MagWorldAz = Math.Atan2(my, mx);
                    if (!_psiRefInit) { _psiRef = MagWorldAz; _psiRefInit = true; }   // 初回を基準にする
                    if (MagYaw)
                    {
                        double err = WrapPi(MagWorldAz - _psiRef);
                        double d = -err * MagGain * dt;             // 世界 Z 軸まわりの微小回転
                        double hw = Math.Cos(d / 2), hz = Math.Sin(d / 2);
                        double nw = hw * W - hz * Zq;
                        double nx = hw * Xq - hz * Yq;
                        double ny = hw * Yq + hz * Xq;
                        double nz = hw * Zq + hz * W;
                        W = nw; Xq = nx; Yq = ny; Zq = nz;
                        Normalize();
                    }
                }
            }
        }

        static void RotateQ(double qw, double qx, double qy, double qz,
                            double vx, double vy, double vz,
                            out double ox, out double oy, out double oz)
        {
            double iw = -qx * vx - qy * vy - qz * vz;
            double ix =  qw * vx + qy * vz - qz * vy;
            double iy =  qw * vy - qx * vz + qz * vx;
            double iz =  qw * vz + qx * vy - qy * vx;
            ox = -iw * qx + ix * qw - iy * qz + iz * qy;
            oy = -iw * qy + ix * qz + iy * qw - iz * qx;
            oz = -iw * qz - ix * qy + iy * qx + iz * qw;
        }

        /// <summary>v' = q v q*（体座標 → 世界座標）。</summary>
        public void Rotate(out double ox, out double oy, out double oz, double vx, double vy, double vz)
        { RotateQ(W, Xq, Yq, Zq, vx, vy, vz, out ox, out oy, out oz); }

        /// <summary>v' = q* v q（世界座標 → 体座標）。</summary>
        public void RotateInv(out double ox, out double oy, out double oz, double vx, double vy, double vz)
        { RotateQ(W, -Xq, -Yq, -Zq, vx, vy, vz, out ox, out oy, out oz); }

        /// <summary>基準姿勢（identity）からの表示上の回転角 [deg]。</summary>
        public double AngleDegFromIdentity()
        {
            double w = Math.Min(1.0, Math.Abs(DW));
            return 2.0 * Math.Acos(w) * 180.0 / Math.PI;
        }
    }

    // ---------------------------------------------------------------- 3D モデル（.glb）
    /// <summary>
    /// Windows 公式のコントローラモデルを読み込んで三角形メッシュに焼き込む。
    /// 公式モデル = Microsoft が「コントローラの可視化」目的で Windows Update で配布している
    /// DriverStore の motioncontroller*model.inf_*\RightHandModel.glb（VID_045E&amp;PID_065D 対応）。
    /// リポジトリには同梱しない（ライセンス回避のため実行時に探す。exe 隣の assets/ でも可）。
    ///
    /// JSON は System.Web.Extensions の JavaScriptSerializer で解析。頂点は「1 三角形 = 9 float」に
    /// 展開して持ち、ベースカラーは baseColorTexture の重心サンプリングで 1 三角形 1 色に潰す
    /// （GDI+ のペインタ法＋平面光源で塗るため）。長辺は ModelLenM に正規化する。
    /// </summary>
    internal sealed class GlbModel
    {
        public const double ModelLenM = 0.18;      // モデル長辺の実寸見立て [m]

        public int TriCount;
        public float[] P;                          // 頂点座標 [m]（9 float / 三角形）
        public byte[] Col;                         // ベースカラー（3 byte / 三角形）
        public string Source;                      // 読み込み元パス
        public double ExtentX, ExtentY, ExtentZ;   // 正規化後の外接寸法 [m]

        // モデルの「正置き」見立て（Y-up の glTF を Z-up の世界に合わせる向き）。
        // 公式モデルは「パッドが上・コントローラが +Z 向き」で作者されているので、
        // Rx(-90) で Z 向きを水平に倒し、Ry(180) でパッドを上に戻す（= ポイントは世界 +Y＝画面の奥向き）。
        public static double RotXDeg = -90, RotYDeg = 180, RotZDeg = 0;

        GlbModel() { }

        /// <summary>exe 隣の assets\RightHandModel.glb → DriverStore の公式モデルの順に探す。</summary>
        public static GlbModel LoadDefault(out string why)
        {
            why = null;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string cand in new string[] {
                Path.Combine(baseDir, "assets", "RightHandModel.glb"),
                Path.Combine(baseDir, "RightHandModel.glb") })
            {
                if (!File.Exists(cand)) continue;
                try { return Load(cand); } catch (Exception ex) { why = ex.Message; }
            }
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                           "DriverStore", "FileRepository");
                foreach (string dir in Directory.GetDirectories(root, "motioncontroller*model.inf_*"))
                {
                    string p = Path.Combine(dir, "RightHandModel.glb");
                    if (!File.Exists(p)) continue;
                    try { return Load(p); } catch (Exception ex) { why = ex.Message; }
                }
            }
            catch (Exception ex) { why = ex.Message; }
            if (why == null) why = "公式モデル (motioncontroller*model.inf_*) が見つかりません";
            return null;
        }

        public static GlbModel Load(string path)
        {
            byte[] all = File.ReadAllBytes(path);
            if (all.Length < 20 || BitConverter.ToUInt32(all, 0) != 0x46546C67u)
                throw new InvalidDataException("glb ではありません");
            int c0 = (int)BitConverter.ToUInt32(all, 12);
            if (c0 <= 0 || 20 + c0 > all.Length) throw new InvalidDataException("JSON チャンクが異常");
            string json = Encoding.UTF8.GetString(all, 20, c0).TrimEnd('\0', ' ', '\t', '\r', '\n');
            byte[] bin = null;
            int c1 = 20 + ((c0 + 3) & ~3);
            if (c1 + 8 <= all.Length)
            {
                int len = (int)BitConverter.ToUInt32(all, c1);
                uint type = BitConverter.ToUInt32(all, c1 + 4);
                if (type == 0x004E4942u && c1 + 8 + len <= all.Length)
                {
                    bin = new byte[len];
                    Buffer.BlockCopy(all, c1 + 8, bin, 0, len);
                }
            }

            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("JSON 解析に失敗");

            IList bvs = Arr(root, "bufferViews");
            IList accs = Arr(root, "accessors");
            IList meshes = Arr(root, "meshes");
            IList nodes = Arr(root, "nodes");
            if (bin == null || bvs == null || accs == null || meshes == null || nodes == null)
                throw new InvalidDataException("必要な要素が足りません");

            // --- ノード階層 → 各ノードの世界行列（glTF は列優先 4x4） ---
            int n = nodes.Count;
            var world = new float[n][];
            var kids = new List<int>[n];
            var hasParent = new bool[n];
            for (int i = 0; i < n; i++)
            {
                kids[i] = new List<int>();
                IList ch = Arr(Obj(nodes[i]), "children");
                if (ch != null)
                    foreach (object c in ch) { int ci = NI(c); kids[i].Add(ci); hasParent[ci] = true; }
            }
            Action<int, float[]> walk = null;
            walk = delegate(int i, float[] parent)
            {
                float[] mine = MMul(parent, MNode(Obj(nodes[i])));
                world[i] = mine;
                foreach (int c in kids[i]) walk(c, mine);
            };
            var roots = new List<int>();
            IList scenes = Arr(root, "scenes");
            if (scenes != null && scenes.Count > 0)
            {
                IList sn = Arr(Obj(scenes[0]), "nodes");
                if (sn != null) foreach (object i in sn) roots.Add(NI(i));
            }
            if (roots.Count == 0)
                for (int i = 0; i < n; i++) if (!hasParent[i]) roots.Add(i);
            foreach (int r in roots) walk(r, MIdentity());

            // --- baseColorTexture（あれば）を読み込む ---
            Bitmap tex = null;
            try { tex = LoadBaseColor(root, bvs, bin); } catch { }

            // --- 三角形に展開 ---
            var tp = new List<float>(65536);
            var tc = new List<byte>(49152);
            for (int ni = 0; ni < n; ni++)
            {
                if (world[ni] == null) continue;
                var nd = Obj(nodes[ni]);
                object meshIdx;
                if (nd == null || !nd.TryGetValue("mesh", out meshIdx)) continue;
                var mesh = Obj(meshes[NI(meshIdx)]);
                IList prims = Arr(mesh, "primitives");
                if (prims == null) continue;
                foreach (object po in prims)
                {
                    var p = Obj(po);
                    if (p == null) continue;
                    int mode = 4;
                    object modeObj;
                    if (p.TryGetValue("mode", out modeObj)) mode = NI(modeObj);
                    if (mode != 4) continue;                       // 三角形リスト以外は無視
                    var attrs = Obj(p, "attributes");
                    object posObj, idxObj = null, uvObj = null;
                    if (attrs == null || !attrs.TryGetValue("POSITION", out posObj)) continue;
                    bool hasIdx = p.TryGetValue("indices", out idxObj);
                    attrs.TryGetValue("TEXCOORD_0", out uvObj);
                    float[] pos = ReadF32(accs, bvs, bin, NI(posObj), 3);
                    float[] uv = uvObj != null ? ReadF32(accs, bvs, bin, NI(uvObj), 2) : null;
                    int[] idx = hasIdx ? ReadIdx(accs, bvs, bin, NI(idxObj)) : null;
                    if (pos == null) continue;
                    int nTri = idx != null ? idx.Length / 3 : pos.Length / 9;
                    for (int t = 0; t < nTri; t++)
                    {
                        int i0, i1, i2;
                        if (idx != null) { i0 = idx[t * 3]; i1 = idx[t * 3 + 1]; i2 = idx[t * 3 + 2]; }
                        else { i0 = t * 3; i1 = t * 3 + 1; i2 = t * 3 + 2; }
                        if (i0 * 3 + 2 >= pos.Length || i1 * 3 + 2 >= pos.Length || i2 * 3 + 2 >= pos.Length) continue;
                        Emit(tp, world[ni], pos, i0);
                        Emit(tp, world[ni], pos, i1);
                        Emit(tp, world[ni], pos, i2);
                        byte r, gg, bb;
                        SampleColor(tex, uv, i0, i1, i2, out r, out gg, out bb);
                        tc.Add(r); tc.Add(gg); tc.Add(bb);
                    }
                }
            }
            if (tex != null) tex.Dispose();
            if (tp.Count < 9) throw new InvalidDataException("三角形がありません");

            // --- 中心を原点へ、長辺を ModelLenM に、正置き見立てを回す ---
            double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
            for (int i = 0; i < tp.Count; i += 3)
            {
                if (tp[i] < minx) minx = tp[i]; if (tp[i] > maxx) maxx = tp[i];
                if (tp[i + 1] < miny) miny = tp[i + 1]; if (tp[i + 1] > maxy) maxy = tp[i + 1];
                if (tp[i + 2] < minz) minz = tp[i + 2]; if (tp[i + 2] > maxz) maxz = tp[i + 2];
            }
            double ex = maxx - minx, ey = maxy - miny, ez = maxz - minz;
            double maxe = Math.Max(ex, Math.Max(ey, ez));
            double scale = (maxe > 1e-9) ? ModelLenM / maxe : 1.0;
            double ccx = (minx + maxx) / 2, ccy = (miny + maxy) / 2, ccz = (minz + maxz) / 2;
            const double D2R = Math.PI / 180.0;
            double ax = RotXDeg * D2R, ay = RotYDeg * D2R, az = RotZDeg * D2R;
            double cx = Math.Cos(ax), sx = Math.Sin(ax);
            double cy2 = Math.Cos(ay), sy = Math.Sin(ay);
            double cz = Math.Cos(az), sz = Math.Sin(az);
            var arr = tp.ToArray();
            for (int i = 0; i < arr.Length; i += 3)
            {
                double x = arr[i] - ccx, y = arr[i + 1] - ccy, z = arr[i + 2] - ccz;
                double t = y * cx - z * sx; z = y * sx + z * cx; y = t;      // X
                t = x * cy2 + z * sy; z = -x * sy + z * cy2; x = t;           // Y
                t = x * cz - y * sz; y = x * sz + y * cz; x = t;              // Z
                arr[i] = (float)(x * scale); arr[i + 1] = (float)(y * scale); arr[i + 2] = (float)(z * scale);
            }
            var mdl = new GlbModel();
            mdl.TriCount = arr.Length / 9;
            mdl.P = arr;
            mdl.Col = tc.ToArray();
            mdl.Source = path;
            mdl.ExtentX = ex * scale; mdl.ExtentY = ey * scale; mdl.ExtentZ = ez * scale;
            return mdl;
        }

        static void Emit(List<float> tp, float[] m, float[] pos, int vi)
        {
            double x = pos[vi * 3], y = pos[vi * 3 + 1], z = pos[vi * 3 + 2];
            tp.Add((float)(m[0] * x + m[4] * y + m[8] * z + m[12]));
            tp.Add((float)(m[1] * x + m[5] * y + m[9] * z + m[13]));
            tp.Add((float)(m[2] * x + m[6] * y + m[10] * z + m[14]));
        }

        /// <summary>三角形 3 頂点の UV 重心をテクスチャでサンプリング（UV 原点は左上で bitmap と同じ）。</summary>
        static void SampleColor(Bitmap tex, float[] uv, int i0, int i1, int i2, out byte r, out byte g, out byte b)
        {
            if (tex == null || uv == null ||
                i0 * 2 + 1 >= uv.Length || i1 * 2 + 1 >= uv.Length || i2 * 2 + 1 >= uv.Length)
            { r = g = b = 214; return; }
            double u = (uv[i0 * 2] + uv[i1 * 2] + uv[i2 * 2]) / 3.0;
            double v = (uv[i0 * 2 + 1] + uv[i1 * 2 + 1] + uv[i2 * 2 + 1]) / 3.0;
            int px = (int)(u * (tex.Width - 1)); if (px < 0) px = 0; if (px >= tex.Width) px = tex.Width - 1;
            int py = (int)(v * (tex.Height - 1)); if (py < 0) py = 0; if (py >= tex.Height) py = tex.Height - 1;
            Color c = tex.GetPixel(px, py);
            r = c.R; g = c.G; b = c.B;
        }

        static Bitmap LoadBaseColor(Dictionary<string, object> root, IList bvs, byte[] bin)
        {
            IList mats = Arr(root, "materials");
            if (mats == null || mats.Count == 0) return null;
            var bct = Obj(Obj(Obj(mats[0]), "pbrMetallicRoughness"), "baseColorTexture");
            object idxObj;
            if (bct == null || !bct.TryGetValue("index", out idxObj)) return null;
            IList texs = Arr(root, "textures");
            var tex = Obj(texs[NI(idxObj)]);
            object srcObj;
            if (tex == null || !tex.TryGetValue("source", out srcObj)) return null;
            IList imgs = Arr(root, "images");
            var img = Obj(imgs[NI(srcObj)]);
            object bvObj;
            if (img == null || !img.TryGetValue("bufferView", out bvObj)) return null;
            byte[] png = Slice(bvs, bin, NI(bvObj));
            if (png == null) return null;
            using (var ms = new MemoryStream(png))
            using (var im = Image.FromStream(ms))
                return new Bitmap(im);      // ストリーム破棄後も使えるよう複製
        }

        static byte[] Slice(IList bvs, byte[] bin, int i)
        {
            var bv = Obj(bvs[i]);
            if (bv == null) return null;
            int off = Has(bv, "byteOffset") ? NI(bv["byteOffset"]) : 0;
            int len = Has(bv, "byteLength") ? NI(bv["byteLength"]) : 0;
            if (off < 0 || len <= 0 || off + len > bin.Length) return null;
            var r = new byte[len];
            Buffer.BlockCopy(bin, off, r, 0, len);
            return r;
        }

        static float[] ReadF32(IList accs, IList bvs, byte[] bin, int ai, int comps)
        {
            var a = Obj(accs[ai]);
            if (a == null || NI(a["componentType"]) != 5126) return null;      // float 以外は来ない想定
            int count = NI(a["count"]);
            if (count <= 0) return null;
            var bv = Obj(bvs[NI(a["bufferView"])]);
            if (bv == null) return null;
            int baseOff = (Has(a, "byteOffset") ? NI(a["byteOffset"]) : 0) +
                          (Has(bv, "byteOffset") ? NI(bv["byteOffset"]) : 0);
            int stride = Has(bv, "byteStride") ? NI(bv["byteStride"]) : 0;
            if (stride <= 0) stride = comps * 4;
            if (baseOff < 0 || baseOff + (count - 1) * stride + comps * 4 > bin.Length) return null;
            var r = new float[count * comps];
            for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                    r[i * comps + c] = BitConverter.ToSingle(bin, baseOff + i * stride + c * 4);
            return r;
        }

        static int[] ReadIdx(IList accs, IList bvs, byte[] bin, int ai)
        {
            var a = Obj(accs[ai]);
            if (a == null) return null;
            int ct = NI(a["componentType"]);
            int size = (ct == 5125) ? 4 : (ct == 5123 || ct == 5122) ? 2 : (ct == 5121) ? 1 : 0;
            if (size == 0) return null;
            int count = NI(a["count"]);
            if (count <= 0) return null;
            var bv = Obj(bvs[NI(a["bufferView"])]);
            if (bv == null) return null;
            int baseOff = (Has(a, "byteOffset") ? NI(a["byteOffset"]) : 0) +
                          (Has(bv, "byteOffset") ? NI(bv["byteOffset"]) : 0);
            int stride = Has(bv, "byteStride") ? NI(bv["byteStride"]) : 0;
            if (stride <= 0) stride = size;
            if (baseOff < 0 || baseOff + (count - 1) * stride + size > bin.Length) return null;
            var r = new int[count];
            for (int i = 0; i < count; i++)
            {
                int o = baseOff + i * stride;
                r[i] = (ct == 5125) ? (int)BitConverter.ToUInt32(bin, o)
                      : (ct == 5123) ? (int)BitConverter.ToUInt16(bin, o)
                      : (ct == 5122) ? BitConverter.ToInt16(bin, o)
                      : (int)bin[o];
            }
            return r;
        }

        static bool Has(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k); }
        static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        { object v; if (d != null && d.TryGetValue(k, out v)) return v as Dictionary<string, object>; return null; }
        static IList Arr(Dictionary<string, object> d, string k)
        { object v; if (d != null && d.TryGetValue(k, out v)) return v as IList; return null; }
        static double N(object o) { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
        static int NI(object o) { return Convert.ToInt32(o, CultureInfo.InvariantCulture); }

        static float[] MIdentity() { var m = new float[16]; m[0] = m[5] = m[10] = m[15] = 1; return m; }

        static float[] MMul(float[] a, float[] b)     // a * b（列ベクトル: 先に b、後に a）
        {
            var r = new float[16];
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                {
                    double s = 0;
                    for (int k = 0; k < 4; k++) s += a[k * 4 + row] * b[c * 4 + k];
                    r[c * 4 + row] = (float)s;
                }
            return r;
        }

        static float[] MNode(Dictionary<string, object> nd)
        {
            if (nd == null) return MIdentity();
            IList mm = Arr(nd, "matrix");
            if (mm != null && mm.Count == 16)
            { var m = new float[16]; for (int i = 0; i < 16; i++) m[i] = (float)N(mm[i]); return m; }
            float[] T = MIdentity(), R = MIdentity(), S = MIdentity();
            IList t = Arr(nd, "translation");
            if (t != null && t.Count >= 3) { T[12] = (float)N(t[0]); T[13] = (float)N(t[1]); T[14] = (float)N(t[2]); }
            IList q = Arr(nd, "rotation");           // glTF のクォータニオンは (x,y,z,w)
            if (q != null && q.Count >= 4)
            {
                double x = N(q[0]), y = N(q[1]), z = N(q[2]), w = N(q[3]);
                R[0] = (float)(1 - 2 * (y * y + z * z)); R[1] = (float)(2 * (x * y + z * w)); R[2] = (float)(2 * (x * z - y * w));
                R[4] = (float)(2 * (x * y - z * w)); R[5] = (float)(1 - 2 * (x * x + z * z)); R[6] = (float)(2 * (y * z + x * w));
                R[8] = (float)(2 * (x * z + y * w)); R[9] = (float)(2 * (y * z - x * w)); R[10] = (float)(1 - 2 * (x * x + y * y));
            }
            IList sc = Arr(nd, "scale");
            if (sc != null && sc.Count >= 3) { S[0] = (float)N(sc[0]); S[5] = (float)N(sc[1]); S[10] = (float)N(sc[2]); }
            return MMul(MMul(T, R), S);
        }
    }

    // ---------------------------------------------------------------- 位置推定（加速度の 2 重積分）
    /// <summary>
    /// 姿勢推定で重力を差し引いた線加速度を 2 回積分して 3D 空間の位置を追う（自己航法）。
    /// 実時間どおりの速度・位置は保証しない試験用の観察値なので、ドリフト対策として
    /// 「1g ±2% が 0.35 秒続いた＝静止（ZUPT）」の間は積分を止めて速度をゼロにする。
    /// スリープ中（IMU 停止）も移動なしとして扱う。位置は PosLimit で暴走を抑える。
    /// </summary>
    internal sealed class Motion
    {
        public const int TrailMax = 800;           // 軌跡の最大点数（50ms 間隔で約 40 秒分）

        public readonly object Sync = new object();
        public double Px, Py, Pz;                  // 位置 [m]（世界座標・Z=上）
        public double Vx, Vy, Vz;                  // 速度 [m/s]
        public bool Zupt;                          // 静止検出中（積分停止）

        const double G0 = 9.80665;                 // [m/s^2]
        const double StillLo = 0.98, StillHi = 1.02;   // 静止判定（_gRef に対する比）
        const double AdaptLo = 0.94, AdaptHi = 1.06;   // 1g 取り込みの窓
        const double StillSec = 0.35;              // 静止と判定する時間
        const double PosLimit = 1.5;               // 位置の暴走防止 [m]

        readonly double[] _tx = new double[TrailMax];
        readonly double[] _ty = new double[TrailMax];
        readonly double[] _tz = new double[TrailMax];
        int _head, _count;
        double _lastT = -1.0, _stillT, _trailT = -1e9;
        double _gRef;                             // 実測の静止時 |a|（スケール未較正の吸収用）

        public void Reset()
        {
            lock (Sync)
            {
                Px = Py = Pz = Vx = Vy = Vz = 0;
                Zupt = false; _lastT = -1.0; _stillT = 0; _trailT = -1e9;
                _head = 0; _count = 0;
            }
        }

        /// <summary>1 サンプルぶん積分する（Orient.Update の後に呼ぶ。内部で Sync をロックする）。</summary>
        public void Update(Sample s, Orient o, double accPerG)
        {
            lock (Sync)
            {
                double dt = _lastT < 0 ? 0 : (s.T - _lastT);
                _lastT = s.T;
                if (!s.HasAcc || !s.HasGyro)
                {
                    // IMU スリープ＝コントローラは置かれた状態。速度だけ落として位置は保つ
                    Vx = Vy = Vz = 0; Zupt = true; _stillT = 1e9;
                    return;
                }
                if (dt <= 0 || dt > 0.2) return;
                if (_gRef <= 0) _gRef = accPerG;   // 1 回目だけ呼び出し元の推定値で開始

                double anRaw = Math.Sqrt((double)s.Ax * s.Ax + (double)s.Ay * s.Ay + (double)s.Az * s.Az);
                double an = anRaw / _gRef;
                if (an >= AdaptLo && an <= AdaptHi)
                {
                    // 静止に近い間は実測の「1g」をゆっくり取り込む（AccPerG は未較正の推定値のため）。
                    // 取り込まれると重力差し引きの縮みが消え、Z が勝手に落ちなくなる。
                    _gRef += (anRaw - _gRef) * 0.02;
                }
                if (an >= StillLo && an <= StillHi)
                {
                    _stillT += dt;
                    if (_stillT >= StillSec) { Zupt = true; Vx = Vy = Vz = 0; }
                }
                else { _stillT = 0; Zupt = false; }
                if (Zupt) return;                    // 静止中は位置も動かさない（ZUPT）

                double wx, wy, wz;
                o.Rotate(out wx, out wy, out wz, s.Ax / _gRef, s.Ay / _gRef, s.Az / _gRef);
                double ax = wx * G0, ay = wy * G0, az = wz * G0 - G0;   // 重力（世界 -Z）を差し引く
                Vx += ax * dt; Vy += ay * dt; Vz += az * dt;
                Px += Vx * dt; Py += Vy * dt; Pz += Vz * dt;

                double rr = Px * Px + Py * Py + Pz * Pz;
                if (rr > PosLimit * PosLimit)
                {
                    double k = PosLimit / Math.Sqrt(rr);
                    Px *= k; Py *= k; Pz *= k;
                    Vx = Vy = Vz = 0;
                }

                if (s.T - _trailT >= 0.05)           // 50ms ごとに軌跡を 1 点記録
                {
                    _tx[_head] = Px; _ty[_head] = Py; _tz[_head] = Pz;
                    _head = (_head + 1) % TrailMax;
                    if (_count < TrailMax) _count++;
                    _trailT = s.T;
                }
            }
        }

        /// <summary>軌跡を古い順に x/y/z へ写す。戻り値は点数。</summary>
        public int CopyTrail(double[] x, double[] y, double[] z)
        {
            lock (Sync)
            {
                int n = Math.Min(_count, Math.Min(x.Length, y.Length));
                if (n <= 0) return 0;
                int start = (_head - n + TrailMax) % TrailMax;
                for (int i = 0; i < n; i++)
                {
                    int k = (start + i) % TrailMax;
                    x[i] = _tx[k]; y[i] = _ty[k]; z[i] = _tz[k];
                }
                return n;
            }
        }
    }

    // ---------------------------------------------------------------- 折れ線グラフ
    internal sealed class Chart : Control
    {
        public delegate void SeriesGetter(ref Sample s, double[] dst);

        public string Title = "";
        public string Unit = "";
        public string Note = "";
        public SeriesGetter Getter;
        public double WindowSec = 5.0;
        public double UnitScale = 1.0;   // 生値 -> 表示単位
        public readonly Color[] Colors = new Color[4] {
            Color.FromArgb(230, 80, 80), Color.FromArgb(110, 220, 110),
            Color.FromArgb(110, 160, 255), Color.FromArgb(240, 200, 90) };

        public SampleRing Ring;
        public object Sync;
        public bool Paused;
        public bool Sleep;

        double _lo, _hi;
        bool _scaleInit;
        readonly List<Sample> _tmp = new List<Sample>(4096);
        readonly double[] _d = new double[4];
        readonly static string[] Names = new string[4] { "X", "Y", "Z", "|v|" };

        public Chart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(22, 24, 28);
            Font = new Font("Segoe UI", 8.5f);
        }

        public double LatestT { get { return (Ring != null && Ring.Count > 0) ? Ring.Last.T : 0.0; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width, h = ClientSize.Height;
            int left = 10, top = 22, right = w - 70, bottom = h - 18;
            if (right - left < 60 || bottom - top < 30) return;

            // --- 見出し ---
            using (var tb = new SolidBrush(Color.FromArgb(225, 228, 232)))
                g.DrawString(Title + "  [" + Unit + "]  " + Note, Font, tb, 10, 5);
            // --- 凡例（系列色つき）。タイトル行の右端に置き、目盛ラベルと重ねない ---
            for (int i = 0; i < 4; i++)
                using (var cb = new SolidBrush(Colors[i]))
                    g.DrawString(Names[i], Font, cb, right + 6 + i * 15, 5);

            if (Paused || Ring == null || Sync == null) { DrawSleepOverlay(g, "一時停止"); return; }

            double tNow = LatestT;
            double tMin = tNow - WindowSec;
            lock (Sync) { Ring.CopySince(tMin, _tmp, 8192); }
            if (_tmp.Count == 0) { DrawSleepOverlay(g, "待機中…"); return; }

            // --- 目盛りの決定（可変スケール、滑らかに追従） ---
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < _tmp.Count; i++)
            {
                Sample s = _tmp[i];
                Getter(ref s, _d);
                for (int k = 0; k < 4; k++)
                {
                    double v = _d[k];
                    if (double.IsNaN(v)) continue;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            }
            if (lo > hi) { lo = -1; hi = 1; }
            double pad = (hi - lo) * 0.15; if (pad <= 0) pad = 1e-6;
            double tLo = lo - pad, tHi = hi + pad;
            if (!_scaleInit) { _lo = tLo; _hi = tHi; _scaleInit = true; }
            else { _lo += (tLo - _lo) * 0.30; _hi += (tHi - _hi) * 0.30; }

            // --- 罫線 ---
            using (var gp = new Pen(Color.FromArgb(44, 48, 55)))
            {
                for (int i = 1; i < 4; i++)
                {
                    int y = top + (bottom - top) * i / 4;
                    g.DrawLine(gp, left, y, right, y);
                }
                for (double t = Math.Ceiling(tMin); t <= tNow + 0.001; t += 1.0)
                {
                    int x = left + (int)((right - left) * (t - tMin) / WindowSec);
                    if (x > left && x < right) g.DrawLine(gp, x, top, x, bottom);
                }
            }
            // --- ゼロ線 ---
            if (_lo < 0 && _hi > 0)
            {
                int y0 = YOf(0, top, bottom);
                using (var zp = new Pen(Color.FromArgb(90, 95, 105))) { zp.DashStyle = DashStyle.Dash; g.DrawLine(zp, left, y0, right, y0); }
            }

            // --- 系列ごとに折れ線 ---
            for (int si = 0; si < 4; si++)
            {
                using (var path = new GraphicsPath())
                {
                    float px = 0, py = 0;
                    bool open = false;
                    for (int i = 0; i < _tmp.Count; i++)
                    {
                        Sample s = _tmp[i];
                        Getter(ref s, _d);
                        double v = _d[si];
                        if (double.IsNaN(v)) { open = false; continue; }
                        float x = left + (float)((right - left) * (s.T - tMin) / WindowSec);
                        float y = YOf(v, top, bottom);
                        if (!open) { path.AddLine(x, y, x, y); open = true; }
                        else path.AddLine(px, py, x, y);
                        px = x; py = y;
                    }
                    if (path.PointCount > 1)
                    {
                        using (var pen = new Pen(Colors[si], si == 3 ? 1.6f : 1.0f))
                            g.DrawPath(pen, path);
                    }
                }
            }

            // --- スケール値（凡例の下から上→下に並べる） ---
            using (var fb = new SolidBrush(Color.FromArgb(150, 155, 162)))
            {
                g.DrawString(Fmt(_hi), Font, fb, right + 6, top + 2);
                g.DrawString(Fmt((_hi + _lo) / 2), Font, fb, right + 6, (top + bottom) / 2 - 7);
                g.DrawString(Fmt(_lo), Font, fb, right + 6, bottom - 8);
            }
            if (Sleep) DrawSleepOverlay(g, "IMU 停止（スリープ）");
        }

        void DrawSleepOverlay(Graphics g, string msg)
        {
            if (!Sleep && !Paused) return;
            using (var b = new SolidBrush(Color.FromArgb(90, 20, 20, 24)))
                g.FillRectangle(b, 0, 0, ClientSize.Width, ClientSize.Height);
            using (var tb = new SolidBrush(Color.FromArgb(230, 150, 150)))
                g.DrawString(msg, Font, tb, ClientSize.Width / 2f - 40, ClientSize.Height / 2f - 8);
        }

        int YOf(double v, int top, int bottom)
        {
            double k = (v - _lo) / (_hi - _lo);
            if (k < 0) k = 0; if (k > 1) k = 1;
            return bottom - (int)((bottom - top) * k);
        }

        static string Fmt(double v)
        {
            double a = Math.Abs(v);
            if (a >= 100000) return v.ToString("0,0", CultureInfo.InvariantCulture);
            if (a >= 100) return v.ToString("0", CultureInfo.InvariantCulture);
            if (a >= 1) return v.ToString("0.0", CultureInfo.InvariantCulture);
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    // ---------------------------------------------------------------- 右パネル（数値＋方位）
    internal sealed class InfoPanel : Control
    {
        public string Status = "起動中…";
        public Color StatusColor = Color.FromArgb(230, 200, 120);
        public double ReportHz, TsRate, TempC, HeadingDeg, TiltX, TiltZ, AccG;
        public double OrientYawDeg;             // 姿勢推定から出した yaw
        public double PosX, PosY, PosZ, Speed;  // 位置 [m] / 速度 [m/s]（2 重積分の観察値）
        public bool Zupt;                       // 静止検出中
        public int StatusFlag;
        public int Ax, Ay, Az, Gx, Gy, Gz, Mx, My, Mz;
        public bool HasData, BiasInit;
        public double BiasX, BiasY, BiasZ;
        public long SampleCount;

        public InfoPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(28, 30, 35);
            Font = new Font("Segoe UI", 9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int w = ClientSize.Width, x = 12, y = 12;
            using (var sb = new SolidBrush(StatusColor))
                g.DrawString(Status, new Font("Segoe UI", 11f, FontStyle.Bold), sb, x, y);
            y += 26;
            Line(g, ref y, x, "レポート", HasData ? ReportHz.ToString("0.0") + " Hz" : "-");
            Line(g, ref y, x, "ts 進み比", HasData ? TsRate.ToString("0.00") + " 倍" : "-");   // 1.00 = 実時間と一致
            Line(g, ref y, x, "温度", HasData ? TempC.ToString("0.00") + " C" : "-");
            Line(g, ref y, x, "状態 (idx24)", HasData ? StatusFlag.ToString() : "-");
            Line(g, ref y, x, "受信サンプル", SampleCount.ToString("N0"));
            y += 6;

            Line(g, ref y, x, "加速度 X/Y/Z", HasData ? Ax.ToString() + " / " + Ay.ToString() + " / " + Az.ToString() : "-");
            Line(g, ref y, x, "  |a|", HasData ? "|a| = " + Mag(Ax, Ay, Az).ToString("N0") + "  = " + AccG.ToString("0.00") + " g(推定)" : "-");
            Line(g, ref y, x, "ジャイロ X/Y/Z", HasData ? Gx.ToString() + " / " + Gy.ToString() + " / " + Gz.ToString() : "-");
            Line(g, ref y, x, "  バイアス", BiasInit ? BiasX.ToString("0") + " / " + BiasY.ToString("0") + " / " + BiasZ.ToString("0") : "（自動推定待ち）");
            Line(g, ref y, x, "コンパス X/Y/Z", HasData ? Mx.ToString() + " / " + My.ToString() + " / " + Mz.ToString() : "-");
            y += 6;

            // --- 方位は左の大型ビュー（方位コマ）に移設。ここは数値だけ ---
            Line(g, ref y, x, "マグ方位(生)", HasData ? HeadingDeg.ToString("0") + " °" : "-");
            Line(g, ref y, x, "姿勢 yaw", HasData ? OrientYawDeg.ToString("0") + " °" : "-");
            Line(g, ref y, x, "位置 X/Y/Z", HasData
                ? (PosX * 100).ToString("0.0") + " / " + (PosY * 100).ToString("0.0") + " / " + (PosZ * 100).ToString("0.0") + " cm"
                : "-");
            Line(g, ref y, x, "速度", HasData
                ? (Speed * 100).ToString("0") + " cm/s" + (Zupt ? "（静止検出）" : "")
                : "-");
        }

        void Line(Graphics g, ref int y, int x, string name, string val)
        {
            using (var nb = new SolidBrush(Color.FromArgb(150, 155, 162))) g.DrawString(name, Font, nb, x, y);
            using (var vb = new SolidBrush(Color.FromArgb(225, 228, 232))) g.DrawString(val, Font, vb, x + 118, y);
            y += 19;
        }

        static double Mag(int x, int y, int z) { return Math.Sqrt((double)x * x + (double)y * y + (double)z * z); }
    }

    // ---------------------------------------------------------------- 3D ビュー（モデル＋姿勢＋位置）
    /// <summary>
    /// 姿勢と位置を GDI+ で投影する 3D ビュー。公式モデル (.glb) があればペインタ法で三角形を塗り、
    /// 無ければ従来のワイヤーフレーム立方体。世界は Z=上・単位はメートルで、±0.5m の観察箱と
    /// 床グリッド、50ms ごとの移動軌跡を描く。矢印キーで見立て（視点）オフセットを回せる。
    /// </summary>
    internal sealed class CubeView : Control
    {
        public Orient Orient;
        public GlbModel Model;                    // コントローラ 3D モデル（無ければ立方体）
        public Motion Motion;                     // 位置・軌跡の元
        public bool HasData;
        public string Status = "待機中";
        public string ModelNote;                  // モデル読み込みの失敗理由など（HUD に出す）
        public double GyroFactor = 1.0;
        public double OffYaw, OffPitch, OffRoll;  // 見立てオフセット [deg]
        public bool MagYaw;                       // コンパス yaw 補正の状態
        public bool MagHas;                       // 磁気データが届いているか
        public double WorldMagAzDeg, YawDeg;      // 方位コマの 2 本の針 [deg]
        public double PosX, PosY, PosZ;           // 表示位置 [m]
        public double Speed;                      // |v| [m/s]
        public bool Zupt;                         // 静止検出中（積分停止）
        public bool Calibrated;                   // C キーでキャリブ済み
        public bool FlipX, FlipY, FlipZ;          // 軸反転の状態
        public double LastPaintMs;                // 前回の描画時間（HUD 用）
        public string PaintErr;                   // 描画例外（あれば HUD に出す）
        public double DiagMaxXY;                  // 描画座標の最大絶対値（Overflow 調査用）

        const double Zc = 4.2;          // カメラ距離
        const double K = 2.0;           // 1 m = 2（箱の半径 0.5m → 1.0 で旧キューブと同じ収まり）
        const double BoxHalf = 0.5;     // 観察箱の半サイズ [m]

        // フォールバック用の立方体（±1 → ±0.5m として描く）
        static readonly float[,] Vtx = {
            { -1,-1,-1 }, { 1,-1,-1 }, { 1,1,-1 }, { -1,1,-1 },
            { -1,-1, 1 }, { 1,-1, 1 }, { 1,1, 1 }, { -1,1, 1 } };
        // 面: 0=-Z(奥) 1=+Z(前) 2=-Y(下) 3=+Y(上) 4=-X(左) 5=+X(右)
        static readonly int[,] Faces = {
            { 0,3,2,1 }, { 4,5,6,7 }, { 0,1,5,4 }, { 1,2,6,5 }, { 2,3,7,6 }, { 3,0,4,7 } };
        static readonly Color[] FaceCol = {
            Color.FromArgb(70, 90, 150), Color.FromArgb(235, 190, 80), Color.FromArgb(105, 140, 205),
            Color.FromArgb(120, 160, 220), Color.FromArgb(85, 115, 180), Color.FromArgb(95, 130, 195) };

        // 観察箱（±0.5m の直方体）と床グリッド
        static readonly double[] BoxVtx = {
            -0.5,-0.5,-0.5,  0.5,-0.5,-0.5,  0.5,0.5,-0.5,  -0.5,0.5,-0.5,
            -0.5,-0.5, 0.5,  0.5,-0.5, 0.5,  0.5,0.5, 0.5,  -0.5,0.5, 0.5 };
        static readonly int[,] BoxEdge = {
            {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };

        readonly double[] _rr = new double[9];     // 見立て回転行列（paint 毎に組み立て）
        readonly double[] _rx = new double[8], _ry = new double[8], _rz = new double[8];
        readonly float[] _sx = new float[8], _sy = new float[8];
        readonly double[] _faceZ = new double[6];
        readonly int[] _order = new int[6] { 0, 1, 2, 3, 4, 5 };

        // モデル描画の作業配列（EnsureArrays で確保）
        double[] _mvx, _mvy, _mvz;                 // カメラ空間の頂点 [view] 3n
        float[] _msx, _msy;                        // 画面座標 3n
        double[] _mdep;                            // 三角形の深度 n
        int[] _mord;                               // 奥から手前の並び順 n
        Comparison<int> _mcmp;
        readonly PointF[] _triPt = new PointF[3];
        SolidBrush _triBr;
        Pen _edgePen;
        Color _lastCol;
        double[] _trx, _try, _trz;                 // 軌跡の写し取り用

        public CubeView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 26, 30);
            Font = new Font("Segoe UI", 8.5f);
            OffPitch = -60;                         // 初期視点は真上ではなく手前斜め上
            _triBr = new SolidBrush(Color.White);
            _edgePen = new Pen(Color.FromArgb(110, 116, 128), 1f);
            _lastCol = Color.FromArgb(0, 0, 0, 0);  // 初回は必ず色を設定させる
            _mcmp = delegate(int a, int b) { return _mdep[a].CompareTo(_mdep[b]); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_triBr != null) _triBr.Dispose();
                if (_edgePen != null) _edgePen.Dispose();
            }
            base.Dispose(disposing);
        }

        void BuildRot()
        {
            // 従来の OffsetRotate と同じ順序: Z(ロール) → X(ピッチ) → Y(ヨー)
            double rr = OffRoll * Math.PI / 180.0, rp = OffPitch * Math.PI / 180.0, ry = OffYaw * Math.PI / 180.0;
            double cr = Math.Cos(rr), sr = Math.Sin(rr);
            double cp = Math.Cos(rp), sp = Math.Sin(rp);
            double cy = Math.Cos(ry), sy = Math.Sin(ry);
            double[] mz = { cr, -sr, 0,  sr, cr, 0,  0, 0, 1 };
            double[] mx = { 1, 0, 0,  0, cp, -sp,  0, sp, cp };
            double[] my = { cy, 0, sy,  0, 1, 0,  -sy, 0, cy };
            double[] m = Mul3(my, Mul3(mx, mz));
            for (int i = 0; i < 9; i++) _rr[i] = m[i];
        }

        static double[] Mul3(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double s = 0;
                    for (int k = 0; k < 3; k++) s += a[i * 3 + k] * b[k * 3 + j];
                    r[i * 3 + j] = s;
                }
            return r;
        }

        void Rot(ref double x, ref double y, ref double z)
        {
            double a = _rr[0] * x + _rr[1] * y + _rr[2] * z;
            double b = _rr[3] * x + _rr[4] * y + _rr[5] * z;
            double c = _rr[6] * x + _rr[7] * y + _rr[8] * z;
            x = a; y = b; z = c;
        }

        /// <summary>世界座標 [m] → 画面。az はカメラ空間の z（大きいほど手前）。</summary>
        void Project(double wx, double wy, double wz, int cx, int cy, double R, out float sx, out float sy, out double az)
        {
            double x = wx * K, y = wy * K, z = wz * K;
            Rot(ref x, ref y, ref z);
            az = z;
            double k = Zc / (Zc - z);
            if (k > 40) k = 40;
            sx = (float)(cx + x * k * R);
            sy = (float)(cy - y * k * R);
        }

        void EnsureArrays(int n)
        {
            _mvx = new double[3 * n]; _mvy = new double[3 * n]; _mvz = new double[3 * n];
            _msx = new float[3 * n]; _msy = new float[3 * n];
            _mdep = new double[n];
            _mord = new int[n];
            for (int i = 0; i < n; i++) _mord[i] = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintAll(e.Graphics); }
            catch (Exception ex)
            {
                // 描画例外でウィンドウが白紙にならないように（原因は %TEMP% に残す）
                PaintErr = ex.GetType().Name + ": " + ex.Message + "  max|xy|=" + DiagMaxXY.ToString("0");
                ErrLog.Log("paint", PaintErr + "\r\n" + ex);
            }
        }

        void PaintAll(Graphics g)
        {
            long t0 = Stopwatch.GetTimestamp();
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 80 || h < 80) return;
            int cx = (int)(w * 0.45), cy = (int)(h * 0.46);
            double R = Math.Min(Math.Min(cx, Math.Min(cy, h - cy)) / 2.05, 300.0);   // 投影最大 ≒ 1.9R で見切れない値
            BuildRot();

            DrawWorld(g, cx, cy, R);                       // 観察箱＋床グリッド
            if (Model != null) DrawModel(g, cx, cy, R);    // 公式コントローラモデル
            else DrawCube(g, cx, cy, R);                   // フォールバックの立方体
            DrawTrail(g, cx, cy, R);                       // 移動軌跡
            DrawAxes(g, cx, cy, R);                        // センサ軸 X/Y/Z（位置・表示姿勢つき）
            DrawRose(g, w, h);                             // 方位コマ（右下）
            DrawHud(g, w, h);                              // 見出し・状態
            LastPaintMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>観察箱（±0.5m）と床グリッド（z = -0.5m、10cm 刻み）。</summary>
        void DrawWorld(Graphics g, int cx, int cy, double R)
        {
            float ax, ay, bx, by; double az;
            using (var gp = new Pen(Color.FromArgb(38, 42, 50), 1f))
            {
                for (int i = -5; i <= 5; i++)
                {
                    double v = i * 0.1;
                    Project(v, -BoxHalf, -BoxHalf, cx, cy, R, out ax, out ay, out az);
                    Project(v, BoxHalf, -BoxHalf, cx, cy, R, out bx, out by, out az);
                    g.DrawLine(gp, ax, ay, bx, by);
                    Project(-BoxHalf, v, -BoxHalf, cx, cy, R, out ax, out ay, out az);
                    Project(BoxHalf, v, -BoxHalf, cx, cy, R, out bx, out by, out az);
                    g.DrawLine(gp, ax, ay, bx, by);
                }
            }
            using (var cp = new Pen(Color.FromArgb(96, 104, 118), 1.4f))
            {
                Project(-0.06, 0, -BoxHalf, cx, cy, R, out ax, out ay, out az);
                Project(0.06, 0, -BoxHalf, cx, cy, R, out bx, out by, out az);
                g.DrawLine(cp, ax, ay, bx, by);
                Project(0, -0.06, -BoxHalf, cx, cy, R, out ax, out ay, out az);
                Project(0, 0.06, -BoxHalf, cx, cy, R, out bx, out by, out az);
                g.DrawLine(cp, ax, ay, bx, by);
            }
            using (var bp = new Pen(Color.FromArgb(56, 62, 74), 1f))
            {
                for (int e = 0; e < 12; e++)
                {
                    int i0 = BoxEdge[e, 0] * 3, i1 = BoxEdge[e, 1] * 3;
                    Project(BoxVtx[i0], BoxVtx[i0 + 1], BoxVtx[i0 + 2], cx, cy, R, out ax, out ay, out az);
                    Project(BoxVtx[i1], BoxVtx[i1 + 1], BoxVtx[i1 + 2], cx, cy, R, out bx, out by, out az);
                    g.DrawLine(bp, ax, ay, bx, by);
                }
            }
        }

        /// <summary>移動軌跡（Motion が 50ms ごとに記録した点をつないだ折れ線）。</summary>
        void DrawTrail(Graphics g, int cx, int cy, double R)
        {
            if (Motion == null) return;
            if (_trx == null)
            {
                _trx = new double[Motion.TrailMax];
                _try = new double[Motion.TrailMax];
                _trz = new double[Motion.TrailMax];
            }
            int n = Motion.CopyTrail(_trx, _try, _trz);
            if (n < 2) return;
            using (var path = new GraphicsPath())
            {
                float px, py; double az;
                Project(_trx[0], _try[0], _trz[0], cx, cy, R, out px, out py, out az);
                path.StartFigure();
                path.AddLine(px, py, px, py);
                for (int i = 1; i < n; i++)
                {
                    float x, y;
                    Project(_trx[i], _try[i], _trz[i], cx, cy, R, out x, out y, out az);
                    if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) continue;
                    path.AddLine(px, py, x, y);
                    px = x; py = y;
                }
                using (var pen = new Pen(Color.FromArgb(70, 220, 205), 2f))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// 公式モデルをペインタ法で描画。頂点は表示姿勢 D で回し、位置を足して投影、
        /// カメラ空間の法線で平面光源の陰影をかけて奥から順に塗る（裏面は奥の面が先に来て
        /// 手前の面で塗り潰される）。暗い本体色は下限を盛り、輪郭線で面の切れ目を見せる。
        /// AA は付けない（6 千枚の polygon では AA が重く、継ぎ目も出るため）。
        /// </summary>
        void DrawModel(Graphics g, int cx, int cy, double R)
        {
            GlbModel m = Model;
            int n = m.TriCount;
            if (_mdep == null || _mdep.Length != n) EnsureArrays(n);
            float[] P = m.P;
            byte[] C = m.Col;
            double px = PosX, py = PosY, pz = PosZ;
            bool hasO = Orient != null;

            for (int t = 0; t < n; t++)
            {
                int b = t * 9;
                for (int k = 0; k < 3; k++)
                {
                    int v = b + k * 3, c3 = t * 3 + k;
                    double rx, ry, rz;
                    if (hasO) Orient.RotateDisp(out rx, out ry, out rz, P[v], P[v + 1], P[v + 2]);
                    else { rx = P[v]; ry = P[v + 1]; rz = P[v + 2]; }
                    rx += px; ry += py; rz += pz;          // 表示位置 [m]
                    rx *= K; ry *= K; rz *= K;             // view 単位へ
                    Rot(ref rx, ref ry, ref rz);           // 見立て（視点）回転
                    _mvx[c3] = rx; _mvy[c3] = ry; _mvz[c3] = rz;
                    double kk = Zc / (Zc - rz);
                    if (kk > 40) kk = 40;
                    _msx[c3] = (float)(cx + rx * kk * R);
                    _msy[c3] = (float)(cy - ry * kk * R);
                    double adx = Math.Abs(_msx[c3]); if (adx > DiagMaxXY) DiagMaxXY = adx;
                    double ady = Math.Abs(_msy[c3]); if (ady > DiagMaxXY) DiagMaxXY = ady;
                }
                _mdep[t] = (_mvz[t * 3] + _mvz[t * 3 + 1] + _mvz[t * 3 + 2]) / 3.0;
            }
            for (int i = 0; i < n; i++) _mord[i] = i;      // 並び順を初期化して奥から
            Array.Sort(_mord, _mcmp);

            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;          // 塗りは非 AA（速度と継ぎ目の両対策）
            for (int i = 0; i < n; i++)
            {
                int t = _mord[i], i3 = t * 3, cb = t * 3;
                // 有限でない頂点は GDI+ が Overflow で倒すため捨てる（NaN 混入時の保険）
                if (float.IsNaN(_msx[i3]) || float.IsInfinity(_msx[i3]) || float.IsNaN(_msy[i3]) || float.IsInfinity(_msy[i3]) ||
                    float.IsNaN(_msx[i3 + 1]) || float.IsInfinity(_msx[i3 + 1]) || float.IsNaN(_msy[i3 + 1]) || float.IsInfinity(_msy[i3 + 1]) ||
                    float.IsNaN(_msx[i3 + 2]) || float.IsInfinity(_msx[i3 + 2]) || float.IsNaN(_msy[i3 + 2]) || float.IsInfinity(_msy[i3 + 2]))
                    continue;
                // カメラ空間の面法線（+Z が手前 → 正の面だけ描く）
                double e1x = _mvx[i3 + 1] - _mvx[i3], e1y = _mvy[i3 + 1] - _mvy[i3], e1z = _mvz[i3 + 1] - _mvz[i3];
                double e2x = _mvx[i3 + 2] - _mvx[i3 + 1], e2y = _mvy[i3 + 2] - _mvy[i3 + 1], e2z = _mvz[i3 + 2] - _mvz[i3 + 1];
                double nx = e1y * e2z - e1z * e2y;
                double ny = e1z * e2x - e1x * e2z;
                double nz = e1x * e2y - e1y * e2x;
                double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double lam = 0.40;
                if (nn > 1e-12)
                {
                    // 光は左上奥から（単位化済み (0.40,0.56,0.73)）。環境光 0.40 + 拡散 0.60
                    double d = (nx * 0.40 + ny * 0.56 + nz * 0.73) / nn;
                    if (d > 0) lam += 0.60 * d;
                }
                // 本体は黒いので暗部が背景(24,26,30)に埋もれないよう下限を盛る: 50 + 0.85×
                int qr = 50 + (int)(C[cb] * lam * 0.85); if (qr > 255) qr = 255;
                int qg = 50 + (int)(C[cb + 1] * lam * 0.85); if (qg > 255) qg = 255;
                int qb = 50 + (int)(C[cb + 2] * lam * 0.85); if (qb > 255) qb = 255;
                // 5bit 量子化でブラシの色変更回数を減らす（ペインタ法の並び順は近い面が連なる）
                var col = Color.FromArgb(255, qr & 0xF8, qg & 0xF8, qb & 0xF8);
                if (col != _lastCol) { _triBr.Color = col; _lastCol = col; }
                _triPt[0].X = _msx[i3]; _triPt[0].Y = _msy[i3];
                _triPt[1].X = _msx[i3 + 1]; _triPt[1].Y = _msy[i3 + 1];
                _triPt[2].X = _msx[i3 + 2]; _triPt[2].Y = _msy[i3 + 2];
                g.FillPolygon(_triBr, _triPt);
                g.DrawPolygon(_edgePen, _triPt);      // 輪郭で面の切れ目を見せる（固定色＝高速）
            }
            g.SmoothingMode = sm;
        }

        /// <summary>モデル無し時の従来どおりの陰影付き立方体（±0.5m・表示姿勢・位置つき）。</summary>
        void DrawCube(Graphics g, int cx, int cy, double R)
        {
            for (int i = 0; i < 8; i++)
            {
                double ax, ay, az;
                if (Orient != null)
                    Orient.RotateDisp(out ax, out ay, out az, Vtx[i, 0] * 0.5, Vtx[i, 1] * 0.5, Vtx[i, 2] * 0.5);
                else { ax = Vtx[i, 0] * 0.5; ay = Vtx[i, 1] * 0.5; az = Vtx[i, 2] * 0.5; }
                double vx = (ax + PosX) * K, vy = (ay + PosY) * K, vz = (az + PosZ) * K;
                Rot(ref vx, ref vy, ref vz);
                _rx[i] = vx; _ry[i] = vy; _rz[i] = vz;
                double k = Zc / (Zc - vz);
                if (k > 40) k = 40;
                _sx[i] = (float)(cx + vx * k * R);
                _sy[i] = (float)(cy - vy * k * R);
            }

            for (int f = 0; f < 6; f++)
                _faceZ[f] = (_rz[Faces[f, 0]] + _rz[Faces[f, 1]] + _rz[Faces[f, 2]] + _rz[Faces[f, 3]]) / 4.0;
            Array.Sort(_order, delegate(int a, int b) { return _faceZ[a].CompareTo(_faceZ[b]); });

            foreach (int f in _order)
            {
                int i0 = Faces[f, 0], i1 = Faces[f, 1], i2 = Faces[f, 2], i3 = Faces[f, 3];
                double e1x = _rx[i1] - _rx[i0], e1y = _ry[i1] - _ry[i0], e1z = _rz[i1] - _rz[i0];
                double e2x = _rx[i2] - _rx[i1], e2y = _ry[i2] - _ry[i1], e2z = _rz[i2] - _rz[i1];
                double nx = e1y * e2z - e1z * e2y;
                double ny = e1z * e2x - e1x * e2z;
                double nz = e1x * e2y - e1y * e2x;
                double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double lam = 0.35;
                if (nn > 1e-9)
                {
                    // 光は左上奥から（単位化済み: (0.40,0.56,0.73)）
                    double d = (nx / nn) * 0.40 + (ny / nn) * 0.56 + (nz / nn) * 0.73;
                    if (d > 0) lam += 0.65 * d;
                }
                var col = Color.FromArgb(255,
                    Math.Min(255, (int)(FaceCol[f].R * lam)),
                    Math.Min(255, (int)(FaceCol[f].G * lam)),
                    Math.Min(255, (int)(FaceCol[f].B * lam)));
                var pts = new PointF[] {
                    new PointF(_sx[i0], _sy[i0]), new PointF(_sx[i1], _sy[i1]),
                    new PointF(_sx[i2], _sy[i2]), new PointF(_sx[i3], _sy[i3]) };
                using (var br = new SolidBrush(col)) g.FillPolygon(br, pts);
                using (var pen = new Pen(Color.FromArgb(28, 30, 35), 1f)) g.DrawPolygon(pen, pts);
            }
        }

        /// <summary>
        /// 方位コマ。黄針=世界座標の磁気方位（マグ補正 ON なら固定＝北の目印）、
        /// 水色針=コントローラの前向き（体 +X）。両者の開きが「起動後に回った角度」。
        /// </summary>
        void DrawRose(Graphics g, int w, int h)
        {
            if (!MagHas || Orient == null) return;
            int rc = Math.Max(40, Math.Min(64, Math.Min(w, h) / 9));
            int rx = w - rc - 20, ry = h - rc - 34;
            if (rx - rc < w * 0.62 || ry - rc < h * 0.55) return;   // キューブに重なりすぎたら省略
            using (var ring = new Pen(Color.FromArgb(70, 76, 86), 1.5f))
                g.DrawEllipse(ring, rx - rc, ry - rc, rc * 2, rc * 2);
            using (var tick = new Pen(Color.FromArgb(95, 100, 110), 1f))
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4.0, c = Math.Cos(a), s = Math.Sin(a);
                    g.DrawLine(tick, (float)(rx + c * rc * 0.84), (float)(ry - s * rc * 0.84),
                                     (float)(rx + c * rc), (float)(ry - s * rc));
                }
            DrawNeedle(g, rx, ry, rc * 0.88, WorldMagAzDeg, Color.FromArgb(245, 205, 90), 2.2f, "N");
            DrawNeedle(g, rx, ry, rc * 0.74, YawDeg, Color.FromArgb(110, 190, 250), 3.0f, "前");
            using (var sb = new SolidBrush(Color.FromArgb(150, 155, 162)))
                g.DrawString("yaw " + YawDeg.ToString("0") + "°", Font, sb, rx - 26, ry + rc + 6);
        }

        void DrawNeedle(Graphics g, int cx, int cy, double len, double deg, Color col, float width, string tip)
        {
            double a = deg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            float x = (float)(cx + c * len), y = (float)(cy - s * len);
            float tx = (float)(cx - c * len * 0.22), ty = (float)(cy + s * len * 0.22);
            using (var pen = new Pen(col, width)) { pen.EndCap = LineCap.RoundAnchor; g.DrawLine(pen, tx, ty, x, y); }
            using (var sb = new SolidBrush(col)) g.DrawString(tip, Font, sb, x - 4, y - 13);
        }

        static double NormDeg(double d) { while (d < 0) d += 360; while (d >= 360) d -= 360; return d; }

        /// <summary>センサ軸 X/Y/Z。原点は表示位置、向きは表示姿勢 D、長さ 25cm。</summary>
        void DrawAxes(Graphics g, int cx, int cy, double R)
        {
            DrawAxisAt(g, cx, cy, R, 1, 0, 0, "X", Color.FromArgb(230, 90, 90));
            DrawAxisAt(g, cx, cy, R, 0, 1, 0, "Y", Color.FromArgb(110, 220, 110));
            DrawAxisAt(g, cx, cy, R, 0, 0, 1, "Z", Color.FromArgb(110, 160, 255));
        }

        void DrawAxisAt(Graphics g, int cx, int cy, double R, double dx, double dy, double dz, string name, Color col)
        {
            double ax, ay, az;
            if (Orient != null) Orient.RotateDisp(out ax, out ay, out az, dx, dy, dz);
            else { ax = dx; ay = dy; az = dz; }
            float x0, y0, x1, y1; double zz;
            Project(PosX, PosY, PosZ, cx, cy, R, out x0, out y0, out zz);
            Project(PosX + ax * 0.25, PosY + ay * 0.25, PosZ + az * 0.25, cx, cy, R, out x1, out y1, out zz);
            using (var pen = new Pen(col, 1.6f))
            {
                pen.EndCap = LineCap.RoundAnchor;
                g.DrawLine(pen, x0, y0, x1, y1);
            }
            using (var sb = new SolidBrush(col)) g.DrawString(name, Font, sb, x1 + 3, y1 - 9);
        }

        /// <summary>見出し・キャリブ案内・位置/速度・状態。</summary>
        void DrawHud(Graphics g, int w, int h)
        {
            using (var tb = new SolidBrush(Color.FromArgb(225, 228, 232)))
            {
                g.DrawString(Model != null ? "3D ビュー（コントローラモデル＋姿勢・位置）"
                                           : "3D ビュー（モデル未検出・立方体表示）", Font, tb, 10, 6);
                g.DrawString("ジャイロ感度 ×" + GyroFactor.ToString("0.00") + "   [ / ] で調整", Font, tb, 10, 24);
            }
            string magLine;
            Color magCol;
            if (!MagHas) { magLine = "マグ未受信（方位コマなし）"; magCol = Color.FromArgb(230, 200, 120); }
            else if (MagYaw) { magLine = "マグ yaw 補正 ON    （M=切替 / N=方位リセット）"; magCol = Color.FromArgb(130, 220, 140); }
            else { magLine = "マグ yaw 補正 OFF    （M=ON・現在は yaw がドリフトします）"; magCol = Color.FromArgb(230, 200, 120); }
            using (var sb = new SolidBrush(magCol)) g.DrawString(magLine, Font, sb, 10, 42);

            if (Calibrated)
            {
                using (var cb = new SolidBrush(Color.FromArgb(120, 215, 135)))
                    g.DrawString("キャリブ済み（C で今の向きを基準にし直し・位置も 0 に）", Font, cb, 10, 60);
            }
            else
            {
                using (var pb = new SolidBrush(Color.FromArgb(255, 210, 90)))
                    g.DrawString("▶ コントローラを水平に奥へ向けたまま構えたら C でキャリブレーション", Font, pb, 10, 60);
            }
            int yNext = 78;
            if (FlipX || FlipY || FlipZ)
            {
                string f = (FlipX ? "X " : "") + (FlipY ? "Y " : "") + (FlipZ ? "Z" : "");
                using (var rb = new SolidBrush(Color.FromArgb(240, 120, 110)))
                    g.DrawString("軸反転中: " + f + "（1 / 2 / 3 で解除）", Font, rb, 10, yNext);
                yNext += 18;
            }
            if (Model == null && !string.IsNullOrEmpty(ModelNote))
            {
                using (var ob = new SolidBrush(Color.FromArgb(205, 165, 145)))
                    g.DrawString("モデル読み込み: " + ModelNote, Font, ob, 10, yNext);
                yNext += 18;
            }
            if (!string.IsNullOrEmpty(PaintErr))
            {
                using (var eb = new SolidBrush(Color.FromArgb(245, 110, 100)))
                    g.DrawString("描画エラー: " + PaintErr, Font, eb, 10, yNext);
            }
            using (var nb = new SolidBrush(Color.FromArgb(150, 155, 162)))
                g.DrawString("描画 " + LastPaintMs.ToString("0.0") + " ms" +
                             (Model != null ? "   " + Model.TriCount.ToString("N0") + " 三角形" : ""),
                             Font, nb, w - 160, 6);

            using (var fb = new SolidBrush(Color.FromArgb(150, 155, 162)))
            {
                string pos = "位置 " + (PosX * 100).ToString("0.0") + " / " + (PosY * 100).ToString("0.0") +
                             " / " + (PosZ * 100).ToString("0.0") + " cm   速度 " + (Speed * 100).ToString("0") +
                             " cm/s" + (Zupt ? "   静止検出（移動は止まる）" : "");
                g.DrawString(pos, Font, fb, 10, h - 46);
                g.DrawString("オフセット yaw " + NormDeg(OffYaw).ToString("0") +
                             "  pitch " + OffPitch.ToString("0") + "  roll " + OffRoll.ToString("0") +
                             "  （矢印 / , .）", Font, fb, 10, h - 32);
                using (var st = new SolidBrush(HasData ? Color.FromArgb(130, 220, 140) : Color.FromArgb(230, 200, 120)))
                    g.DrawString(Status, Font, st, 10, h - 18);
            }
        }
    }

    // ---------------------------------------------------------------- メインウィンドウ
    internal sealed class MainForm : Form
    {
        // 推定スケール（docs/spec §3-2 参照。未較正。実体は Consts）
        const double ACC_PER_G = Consts.AccPerG;        // 1g ≒ 49 万カウント
        const double GYRO_PER_DPS = Consts.GyroPerDps;  // ±2000 deg/s 想定

        readonly int _vid, _pid;
        Controller _dev;
        SampleRing _ring;
        readonly object _sync = new object();
        Thread _th;
        volatile bool _stop;
        volatile bool _paused;

        readonly Chart _acc, _gyro, _mag;
        readonly InfoPanel _info;
        readonly CubeView _cube;
        readonly Orient _orient = new Orient();
        readonly Motion _motion = new Motion();
        readonly Label _hint;
        readonly Button _btnPause, _btnBias, _btnCal, _btnWindow, _btnCsv;
        readonly System.Windows.Forms.Timer _ui;
        readonly Stopwatch _sw = Stopwatch.StartNew();

        double _biasX, _biasY, _biasZ;
        bool _biasInit;
        bool _calibrated;      // C キーで姿勢・位置の基準をとったか
        bool _flipX, _flipY, _flipZ;   // センサ軸の反転（鏡像是正用）
        double[] _windowChoices = new double[3] { 2.0, 5.0, 10.0 };
        int _windowIdx = 1;
        double _reportHz, _tsRate;
        readonly List<double> _recent = new List<double>(4096);

        public MainForm(int vid, int pid, double windowSec)
        {
            _vid = vid; _pid = pid;
            for (int i = 0; i < _windowChoices.Length; i++) if (Math.Abs(_windowChoices[i] - windowSec) < 0.01) _windowIdx = i;

            Text = "Odyssey センサビジュアライザ（IMU / コンパス）";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 820);
            MinimumSize = new Size(980, 620);
            BackColor = Color.FromArgb(28, 30, 35);

            _ring = new SampleRing(16384);

            // --- 上部ツールバー ---
            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 42;
            top.BackColor = Color.FromArgb(34, 37, 43);
            _btnPause = Btn("一時停止 (_Space)", delegate { TogglePause(); });
            _btnBias = Btn("ジャイロ基準化 (_B)", delegate { ReZero(); });
            _btnCal = Btn("キャリブ (_C)", delegate { Calibrate(); });
            _btnWindow = Btn("表示窓 " + _windowChoices[_windowIdx].ToString("0") + "s (_W)", delegate { CycleWindow(); });
            _btnCsv = Btn("CSV 保存 (_S)", delegate { SaveCsv(); });
            int bx = 10;
            foreach (Button b in new Button[] { _btnPause, _btnBias, _btnCal, _btnWindow, _btnCsv })
            { b.Left = bx; b.Top = 7; top.Controls.Add(b); bx += b.Width + 8; }
            var lbl = new Label();
            lbl.Text = "Space=停止  B=基準化  C=キャリブ  W=窓  S=CSV  R=姿勢リセット  1/2/3=軸反転  矢印/,, .=見立て  [ ]=感度  M/N=マグ  Esc=終了";
            lbl.AutoSize = true; lbl.ForeColor = Color.FromArgb(140, 145, 155);
            lbl.Location = new Point(bx + 8, 13);
            top.Controls.Add(lbl);

            // --- グラフ 3 枚 ---
            _acc = MakeChart("加速度", "g (推定)", "1g ≒ 49万 cts");
            _acc.UnitScale = 1.0 / ACC_PER_G;
            _gyro = MakeChart("ジャイロ", "deg/s (推定)", "±2000deg/s 想定・基準化あり");
            _gyro.UnitScale = 1.0 / GYRO_PER_DPS;
            _mag = MakeChart("コンパス（地磁気）", "count (未較正)", "姿勢でしか動きません");
            BindSeries();

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1; grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            grid.Padding = new Padding(0);
            grid.Controls.Add(_acc, 0, 0);
            grid.Controls.Add(_gyro, 0, 1);
            grid.Controls.Add(_mag, 0, 2);

            // --- 右パネル（上に数値、下にグラフ 3 枚） ---
            var rightBox = new Panel();
            rightBox.Dock = DockStyle.Right; rightBox.Width = 420;
            rightBox.BackColor = Color.FromArgb(28, 30, 35);
            _info = new InfoPanel(); _info.Dock = DockStyle.Top; _info.Height = 350;
            rightBox.Controls.Add(grid);    // 先に Fill
            rightBox.Controls.Add(_info);   // 後に Top

            // --- 左：大きな 3D ビュー（モデル＋姿勢＋位置、主役） ---
            _cube = new CubeView(); _cube.Dock = DockStyle.Fill;
            _cube.Orient = _orient;
            _cube.Motion = _motion;
            string modelWhy;
            _cube.Model = GlbModel.LoadDefault(out modelWhy);
            _cube.ModelNote = modelWhy;

            // dock の順序: 先に Fill、そのあと Right / Top
            Controls.Add(_cube);
            Controls.Add(rightBox);
            Controls.Add(top);

            // --- キー ---
            KeyPreview = true;
            KeyDown += OnKey;

            // --- 読み取りスレッド ---
            _dev = new Controller(_vid, _pid);
            _th = new Thread(ReaderLoop); _th.IsBackground = true; _th.Name = "hid-reader";
            _th.Start();

            _ui = new System.Windows.Forms.Timer();
            _ui.Interval = 33;
            _ui.Tick += delegate { UpdateUi(); };
            _ui.Start();

            FormClosing += delegate { _stop = true; };
            FormClosed += delegate { if (_dev != null) _dev.Dispose(); };
        }

        Button Btn(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text; b.AutoSize = true;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 86);
            b.BackColor = Color.FromArgb(45, 49, 57); b.ForeColor = Color.FromArgb(220, 224, 230);
            b.Click += onClick;
            return b;
        }

        Chart MakeChart(string title, string unit, string note)
        {
            var c = new Chart();
            c.Title = title; c.Unit = unit; c.Note = note;
            c.Ring = _ring; c.Sync = _sync;
            c.Margin = new Padding(2, 2, 2, 2);
            // TableLayoutPanel はデフォルト Anchor(左上)だとサイズを伸ばさない（0x0 のまま）ので全方向固定
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            return c;
        }

        void BindSeries()
        {
            _acc.Getter = AccSeries;
            _gyro.Getter = GyroSeries;
            _mag.Getter = MagSeries;
            _acc.WindowSec = _gyro.WindowSec = _mag.WindowSec = _windowChoices[_windowIdx];
        }

        // ---------------------------------------------------------------- 読み取りスレッド
        void ReaderLoop()
        {
            var sw = Stopwatch.StartNew();
            while (!_stop)
            {
                if (!_dev.IsOpen)
                {
                    if (!_dev.Open()) { Thread.Sleep(800); continue; }
                    lock (_sync) { /* 開き直し後の最初のレポート待ち */ }
                }
                int r = _dev.Read(60);
                if (r > 0)
                {
                    Sample s = _dev.Decode(sw.Elapsed.TotalSeconds);
                    // 軸反転（鏡像が正しくないときの是正。加速度・ジャイロ・コンパスをまとめて反転）
                    if (_flipX) { s.Ax = -s.Ax; s.Gx = -s.Gx; s.Mx = -s.Mx; }
                    if (_flipY) { s.Ay = -s.Ay; s.Gy = -s.Gy; s.My = -s.My; }
                    if (_flipZ) { s.Az = -s.Az; s.Gz = -s.Gz; s.Mz = -s.Mz; }
                    lock (_sync) { _ring.Push(s); }
                    _orient.Update(s, Consts.GyroPerDps);        // サンプル毎に姿勢を積分（約165Hz）
                    _motion.Update(s, _orient, ACC_PER_G);       // 続けて位置を積分（姿勢の重力除去に使う）
                    // 静止検出（ZUPT）中はジャイロのバイアスをゆっくり自動追従する。
                    // 使い回した後の「勝手に回り続ける」を自己回復させる（τ ≒ 1.5 秒）。
                    if (_motion.Zupt)
                    {
                        _biasX += (s.Gx - _biasX) * 0.004;
                        _biasY += (s.Gy - _biasY) * 0.004;
                        _biasZ += (s.Gz - _biasZ) * 0.004;
                        _biasInit = true;
                        _orient.SetBias(_biasX, _biasY, _biasZ);
                    }
                }
                else if (_dev.LastErr != Nt.ERROR_TIMEOUT)
                {
                    _dev.Close();     // 切断されたら探し直す
                    Thread.Sleep(300);
                }
            }
        }

        // ---------------------------------------------------------------- UI 更新
        void UpdateUi()
        {
            Sample s;
            int count;
            lock (_sync) { count = _ring.Count; s = count > 0 ? _ring.Last : new Sample(); }

            bool has = count > 0 && (s.HasAcc || s.HasGyro || s.HasMag);
            if (has)
            {
                // バイアスの自動推定（起動直後の最初の 64 サンプル）
                if (!_biasInit && count >= 64)
                {
                    Sample b;
                    lock (_sync) { b = _ring.At(count - 1); }
                    if (b.HasGyro && Math.Abs(b.Gx) + Math.Abs(b.Gy) + Math.Abs(b.Gz) < 60000)
                    {
                        double sx = 0, sy = 0, sz = 0;
                        int n = 0;
                        for (int i = count - 64; i < count; i++)
                        {
                            Sample q;
                            lock (_sync) { q = _ring.At(i); }
                            if (!q.HasGyro) continue;
                            sx += q.Gx; sy += q.Gy; sz += q.Gz; n++;
                        }
                        if (n > 0)
                        {
                            _biasX = sx / n; _biasY = sy / n; _biasZ = sz / n; _biasInit = true;
                            _orient.SetBias(_biasX, _biasY, _biasZ);
                        }
                    }
                }

                // レート（直近 1 秒）
                double tNow = s.T;
                _recent.Clear();
                lock (_sync)
                {
                    for (int i = count - 1; i >= 0; i--)
                    {
                        Sample q = _ring.At(i);
                        if (q.T < tNow - 1.0) break;
                        _recent.Add(q.T);
                    }
                }
                _reportHz = _recent.Count;
                Sample first;
                lock (_sync)
                {
                    int i0 = count - _recent.Count;
                    first = i0 >= 0 ? _ring.At(Math.Max(0, i0)) : s;
                }
                double dTs = s.Ts - first.Ts;
                // デバイス時刻(100ns)の進み ÷ 実時間 = 1.00 なら時計は実時間と一致
                _tsRate = (dTs > 0 && s.T >= first.T) ? dTs / 1e7 / Math.Max(1e-3, s.T - first.T) : 0;
            }

            // 系列の値は Chart がループで呼う（BindSeries で紐付け済み）
            _acc.Sleep = !s.HasAcc;
            _gyro.Sleep = !s.HasGyro;
            _mag.Sleep = !s.HasMag;
            _acc.Paused = _gyro.Paused = _mag.Paused = _paused;

            // 系列の値は BindSeries で紐付け済み（毎フレームの再束縛はしない）

            // 位置・速度（2 重積分の観察値）を両方のパネルへ転写
            double mpx, mpy, mpz, msp;
            bool mz;
            lock (_motion.Sync)
            {
                mpx = _motion.Px; mpy = _motion.Py; mpz = _motion.Pz;
                msp = Math.Sqrt(_motion.Vx * _motion.Vx + _motion.Vy * _motion.Vy + _motion.Vz * _motion.Vz);
                mz = _motion.Zupt;
            }
            _info.PosX = mpx; _info.PosY = mpy; _info.PosZ = mpz; _info.Speed = msp; _info.Zupt = mz;
            _info.HasData = has;
            _info.SampleCount = _ring.Total;
            _info.ReportHz = _reportHz;
            _info.TsRate = _tsRate;
            _info.TempC = s.Temp / 100.0;
            _info.StatusFlag = s.Status;
            _info.Ax = s.Ax; _info.Ay = s.Ay; _info.Az = s.Az;
            _info.Gx = s.Gx; _info.Gy = s.Gy; _info.Gz = s.Gz;
            _info.Mx = s.Mx; _info.My = s.My; _info.Mz = s.Mz;
            _info.AccG = MagD(s.Ax, s.Ay, s.Az) / ACC_PER_G;
            _info.TiltX = s.Ax; _info.TiltZ = s.Az;
            _info.HeadingDeg = has && s.HasMag ? Norm(Math.Atan2(s.My, s.Mx) * 180.0 / Math.PI) : 0;
            _info.BiasInit = _biasInit;
            _info.BiasX = _biasX; _info.BiasY = _biasY; _info.BiasZ = _biasZ;

            if (!_dev.IsOpen) { _info.Status = "コントローラ未検出（再試行中）"; _info.StatusColor = Color.FromArgb(235, 120, 110); }
            else if (_paused) { _info.Status = "一時停止中"; _info.StatusColor = Color.FromArgb(150, 200, 235); }
            else if (!has) { _info.Status = "受信待ち…"; _info.StatusColor = Color.FromArgb(230, 200, 120); }
            else if (!s.HasAcc && !s.HasGyro) { _info.Status = "IMU 停止（スリープ中）"; _info.StatusColor = Color.FromArgb(235, 120, 110); }
            else { _info.Status = "センサ稼働中"; _info.StatusColor = Color.FromArgb(130, 220, 140); }

            _btnWindow.Text = "表示窓 " + _windowChoices[_windowIdx].ToString("0") + "s (_W)";
            _btnPause.Text = _paused ? "再開 (_Space)" : "一時停止 (_Space)";

            _cube.HasData = has;
            _cube.GyroFactor = _orient.GyroFactor;
            _cube.Status = _info.Status;
            _cube.MagYaw = _orient.MagYaw;
            _cube.MagHas = has && s.HasMag && _orient.MagRefInit;
            _cube.WorldMagAzDeg = Norm(_orient.MagWorldAz * 180.0 / Math.PI);
            _cube.YawDeg = _orient.YawDeg;
            // 表示位置は観察箱（±0.5m）にクランプ（数値は右パネルに実値を出す）
            _cube.PosX = ClampM(mpx); _cube.PosY = ClampM(mpy); _cube.PosZ = ClampM(mpz);
            _cube.Speed = msp; _cube.Zupt = mz;
            _cube.Calibrated = _calibrated;
            _cube.FlipX = _flipX; _cube.FlipY = _flipY; _cube.FlipZ = _flipZ;
            _cube.Invalidate();
            _info.OrientYawDeg = _orient.YawDeg;
            _acc.Invalidate(); _gyro.Invalidate(); _mag.Invalidate(); _info.Invalidate();
        }

        static double MagD(double x, double y, double z) { return Math.Sqrt(x * x + y * y + z * z); }
        static double Norm(double deg) { while (deg < 0) deg += 360; while (deg >= 360) deg -= 360; return deg; }
        static double ClampM(double v) { return v > 0.5 ? 0.5 : (v < -0.5 ? -0.5 : v); }

        void AccSeries(ref Sample s, double[] d)
        {
            if (!s.HasAcc) { d[0] = d[1] = d[2] = d[3] = double.NaN; return; }
            d[0] = s.Ax * _acc.UnitScale; d[1] = s.Ay * _acc.UnitScale; d[2] = s.Az * _acc.UnitScale;
            d[3] = MagD(s.Ax, s.Ay, s.Az) * _acc.UnitScale;
        }
        void GyroSeries(ref Sample s, double[] d)
        {
            if (!s.HasGyro) { d[0] = d[1] = d[2] = d[3] = double.NaN; return; }
            d[0] = (s.Gx - _biasX) * _gyro.UnitScale;
            d[1] = (s.Gy - _biasY) * _gyro.UnitScale;
            d[2] = (s.Gz - _biasZ) * _gyro.UnitScale;
            d[3] = MagD(s.Gx - _biasX, s.Gy - _biasY, s.Gz - _biasZ) * _gyro.UnitScale;
        }
        void MagSeries(ref Sample s, double[] d)
        {
            if (!s.HasMag) { d[0] = d[1] = d[2] = d[3] = double.NaN; return; }
            d[0] = s.Mx * _mag.UnitScale; d[1] = s.My * _mag.UnitScale; d[2] = s.Mz * _mag.UnitScale;
            d[3] = MagD(s.Mx, s.My, s.Mz) * _mag.UnitScale;
        }

        // ---------------------------------------------------------------- 操作
        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { TogglePause(); e.Handled = true; }
            else if (e.KeyCode == Keys.B) { ReZero(); e.Handled = true; }
            else if (e.KeyCode == Keys.C) { Calibrate(); e.Handled = true; }
            else if (e.KeyCode == Keys.W) { CycleWindow(); e.Handled = true; }
            else if (e.KeyCode == Keys.S) { SaveCsv(); e.Handled = true; }
            else if (e.KeyCode == Keys.R) { _orient.Reset(); _cube.OffYaw = _cube.OffPitch = _cube.OffRoll = 0; }
            else if (e.KeyCode == Keys.M) { _orient.MagYaw = !_orient.MagYaw; e.Handled = true; }
            else if (e.KeyCode == Keys.N) { _orient.ResetMagRef(); e.Handled = true; }
            else if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { _flipX = !_flipX; e.Handled = true; }
            else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { _flipY = !_flipY; e.Handled = true; }
            else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { _flipZ = !_flipZ; e.Handled = true; }
            else if (e.KeyCode == Keys.OemOpenBrackets) { _orient.GyroFactor = Math.Max(0.10, _orient.GyroFactor * 0.9); }
            else if (e.KeyCode == Keys.OemCloseBrackets) { _orient.GyroFactor = Math.Min(10.0, _orient.GyroFactor * 1.1); }
            else if (e.KeyCode == Keys.Left) { _cube.OffYaw -= 15; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { _cube.OffYaw += 15; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { _cube.OffPitch -= 15; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { _cube.OffPitch += 15; e.Handled = true; }
            else if (e.KeyCode == Keys.Oemcomma) { _cube.OffRoll -= 15; e.Handled = true; }
            else if (e.KeyCode == Keys.OemPeriod) { _cube.OffRoll += 15; e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }
        void TogglePause() { _paused = !_paused; }
        void CycleWindow()
        {
            _windowIdx = (_windowIdx + 1) % _windowChoices.Length;
            _acc.WindowSec = _gyro.WindowSec = _mag.WindowSec = _windowChoices[_windowIdx];
            _btnWindow.Text = "表示窓 " + _windowChoices[_windowIdx].ToString("0") + "s (_W)";
        }
        void ReZero()
        {
            // いまの最新値を基準にする（その場で止まっているときが前提）
            int count;
            lock (_sync) { count = _ring.Count; }
            if (count == 0) return;
            Sample q;
            lock (_sync) { q = _ring.At(count - 1); }
            if (!q.HasGyro) return;
            _biasX = q.Gx; _biasY = q.Gy; _biasZ = q.Gz; _biasInit = true;
            _orient.SetBias(_biasX, _biasY, _biasZ);
        }

        /// <summary>「この向きに構えて C」— 姿勢・位置・速度の基準をまとめてとる。</summary>
        void Calibrate()
        {
            ReZero();                      // ジャイロの静止バイアスを見直す（静止時が前提）
            _orient.SetCalibration();      // いま向いている方角を identity にする
            _orient.ResetMagRef();         // マグの基準方位も今の世界に合わせ直す
            _motion.Reset();               // 位置・速度・軌跡をゼロに
            _calibrated = true;
        }
        void SaveCsv()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV|*.csv";
                dlg.FileName = "odyssey-sensor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("t_sec,ts,ax,ay,az,gx,gy,gz,mx,my,mz,temp_c,status");
                List<Sample> tmp = new List<Sample>(4096);
                lock (_sync) { _ring.CopySince(_ring.Count > 0 ? _ring.Last.T - _windowChoices[_windowIdx] : 0, tmp, 65536); }
                foreach (Sample q in tmp)
                {
                    sb.Append(q.T.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                      .Append(q.Ts).Append(',')
                      .Append(q.Ax).Append(',').Append(q.Ay).Append(',').Append(q.Az).Append(',')
                      .Append(q.Gx).Append(',').Append(q.Gy).Append(',').Append(q.Gz).Append(',')
                      .Append(q.Mx).Append(',').Append(q.My).Append(',').Append(q.Mz).Append(',')
                      .Append((q.Temp / 100.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(q.Status).AppendLine();
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                MessageBox.Show(this, tmp.Count.ToString("N0") + " サンプルを保存しました。\n" + dlg.FileName,
                                "CSV 保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    // ---------------------------------------------------------------- エラー記録
    internal static class ErrLog
    {
        /// <summary>%TEMP%\sensor-viewer-error.txt に直近の例外を書き出す（致命的例外の調査用）。</summary>
        public static void Log(string tag, Exception ex)
        {
            Log(tag, (ex != null) ? ex.ToString() : "(null exception)");
        }

        public static void Log(string tag, string body)
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "sensor-viewer-error.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + tag + "]\r\n" + body,
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- エントリ
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            int vid = 0x045E, pid = 0x065D;
            double window = 5.0, selfTest = 0;
            string outPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("-Vid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) vid = Convert.ToInt32(args[++i], 0);
                else if (a.Equals("-Pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) pid = Convert.ToInt32(args[++i], 0);
                else if (a.Equals("-Window", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) window = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a.Equals("-SelfTest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) selfTest = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a.Equals("-Out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) outPath = args[++i];
            }
            if (selfTest > 0) return RunSelfTest(vid, pid, selfTest, outPath);

            // UI スレッド例外は握ってログに残し、ウィンドウは生かし続ける（監視ツールなので止めたない）
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender2, System.Threading.ThreadExceptionEventArgs te)
            { ErrLog.Log("ui", te.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender3, UnhandledExceptionEventArgs ue)
            { ErrLog.Log("fatal", ue.ExceptionObject as Exception ?? new Exception(Convert.ToString(ue.ExceptionObject))); };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(vid, pid, window));
            return 0;
        }

        /// <summary>UI なしで N 秒読み、フィールドごとの統計をテキストに出す（回帰確認用）。</summary>
        static int RunSelfTest(int vid, int pid, double seconds, string outPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Odyssey センサビジュアライザ self-test");
            sb.AppendLine("time   = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("target = VID=0x" + vid.ToString("X4") + " PID=0x" + pid.ToString("X4") + "  " + seconds.ToString("0.0") + " 秒");
            string modelWhy;
            GlbModel mdl = GlbModel.LoadDefault(out modelWhy);
            if (mdl != null)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "model   = {0}  ({1} tris, extent {2:0.0} x {3:0.0} x {4:0.0} cm)",
                    mdl.Source, mdl.TriCount, mdl.ExtentX * 100, mdl.ExtentY * 100, mdl.ExtentZ * 100));
            else
                sb.AppendLine("model   = none (" + modelWhy + ")");
            var dev = new Controller(vid, pid);
            if (!dev.Open())
            {
                sb.AppendLine("result = FAIL (デバイス未検出)");
                RunOrientTests(sb);
                return Finish(sb, outPath, 1);
            }
            sb.AppendLine("path   = " + dev.Path);

            var keys = new List<ushort>(dev.Fields.Keys);
            keys.Sort();
            sb.AppendLine("fields = " + keys.Count + " (vendor ページ 0xFE)");
            foreach (ushort u in keys)
            {
                FieldDef f = dev.Fields[u];
                sb.AppendLine(string.Format("  usage 0x{0:X3} -> idx {1,2} ({2}bit)", u, f.Index, f.Bits));
            }

            var sw = Stopwatch.StartNew();
            long n = 0, accN = 0, gyroN = 0, magN = 0;
            var acc = new AxisStat(); var gyr = new AxisStat(); var mag = new AxisStat();
            double tempMin = double.MaxValue, tempMax = double.MinValue;
            long ts0 = 0, ts1 = 0;
            var statusSeen = new Dictionary<int, int>();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                int r = dev.Read(60);
                if (r <= 0) continue;
                Sample s = dev.Decode(sw.Elapsed.TotalSeconds);
                n++;
                if (s.HasAcc) { accN++; acc.Add(s.Ax, s.Ay, s.Az); }
                if (s.HasGyro) { gyroN++; gyr.Add(s.Gx, s.Gy, s.Gz); }
                if (s.HasMag) { magN++; mag.Add(s.Mx, s.My, s.Mz); }
                if (s.Temp != 0) { double t = s.Temp / 100.0; if (t < tempMin) tempMin = t; if (t > tempMax) tempMax = t; }
                if (ts0 == 0) ts0 = s.Ts; ts1 = s.Ts;
                statusSeen[s.Status] = statusSeen.ContainsKey(s.Status) ? statusSeen[s.Status] + 1 : 1;
            }
            dev.Dispose();
            double el = sw.Elapsed.TotalSeconds;
            sb.AppendLine();
            sb.AppendLine(string.Format("result = {0}", n > 0 ? "OK" : "FAIL (レポートなし)"));
            sb.AppendLine(string.Format("  samples      : {0}  ({1:0.0} Hz)", n, n / el));
            sb.AppendLine(string.Format("  accel present: {0}", accN));
            sb.AppendLine(string.Format("    X {0}", acc));
            sb.AppendLine(string.Format("  gyro  present: {0}", gyroN));
            sb.AppendLine(string.Format("    X {0}", gyr));
            sb.AppendLine(string.Format("  mag   present: {0}", magN));
            sb.AppendLine(string.Format("    X {0}", mag));
            if (tempMax > double.MinValue) sb.AppendLine(string.Format("  temperature  : {0:0.00} .. {1:0.00} C", tempMin, tempMax));
            if (ts1 > ts0) sb.AppendLine(string.Format("  ts 進み比     : {0:0.00} 倍（1.00 = 実時間と一致）", (ts1 - ts0) / 1e7 / el));
            var st = new List<int>(statusSeen.Keys); st.Sort();
            foreach (int k in st) sb.AppendLine(string.Format("  status flag {0}: {1} 回", k, statusSeen[k]));
            RunOrientTests(sb);
            return Finish(sb, outPath, n > 0 ? 0 : 1);
        }

        /// <summary>姿勢推定（相補フィルタ）の数値検証。実機が無くても実行できる。</summary>
        static void RunOrientTests(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- 姿勢推定フィルタの検証（合成データ） ---");

            // A) 静止: 重力 +Z・ジャイロ 0 → 姿勢は基準のまま
            var oA = new Orient();
            var sA = new Sample(); sA.HasGyro = true; sA.HasAcc = true; sA.Az = (int)Consts.AccPerG;
            for (int i = 1; i <= 328; i++) { sA.T = i / 165.0; oA.Update(sA, Consts.GyroPerDps); }
            double aA = oA.AngleDegFromIdentity();

            // B) Z 軸まわりに 90 deg/s で 1 秒 → 90 deg（重力が +Z のままなので補正は効かない）
            var oB = new Orient();
            var sB = new Sample(); sB.HasGyro = true; sB.HasAcc = true; sB.Az = (int)Consts.AccPerG;
            sB.Gz = (int)(90.0 * Consts.GyroPerDps);
            for (int i = 1; i <= 165; i++) { sB.T = i / 165.0; oB.Update(sB, Consts.GyroPerDps); }
            double aB = oB.AngleDegFromIdentity();

            // C) 重力補正: 重力が +X なのに基準姿勢から始める → 数秒で +X へ倒れる
            var oC = new Orient();
            var sC = new Sample(); sC.HasGyro = true; sC.HasAcc = true; sC.Ax = (int)Consts.AccPerG;
            for (int i = 1; i <= 825; i++) { sC.T = i / 165.0; oC.Update(sC, Consts.GyroPerDps); }
            double px, py, pz;
            oC.RotateInv(out px, out py, out pz, 0, 0, 1);

            // D) 振り中 (3g) は補正しない → 傾いていても姿勢は飛ばない
            var oD = new Orient();
            var sD = new Sample(); sD.HasGyro = true; sD.HasAcc = true; sD.Ax = (int)(Consts.AccPerG * 3.0);
            for (int i = 1; i <= 328; i++) { sD.T = i / 165.0; oD.Update(sD, Consts.GyroPerDps); }
            double aD = oD.AngleDegFromIdentity();

            // E) コンパス yaw 補正: ジャイロに 1deg/s のバイアスが流れ続けると yaw は 30 秒で
            //    30 度ドリフトするが、マグ補正 ON なら磁気方位が基準に戻るので数度に収まる
            double aEoff = RunMagTest(false), aEon = RunMagTest(true);

            sb.AppendLine(string.Format("  A 静止         : 姿勢変化 {0,8:0.00} deg（期待 ≒ 0）", aA));
            sb.AppendLine(string.Format("  B 90deg/s×1秒  : 姿勢変化 {0,8:0.0} deg（期待 90）", aB));
            sb.AppendLine(string.Format("  C 重力補正     : 推定「上」と +X の内積 {0,8:0.00}（期待 ≒ 1）", px));
            sb.AppendLine(string.Format("  D 振り中(3g)   : 姿勢変化 {0,8:0.00} deg（期待 ≒ 0）", aD));
            sb.AppendLine(string.Format("  E マグ yaw補正 : OFF {0,8:0.0} deg / ON {1,8:0.0} deg（バイアス 1deg/s を 30 秒、期待 OFF ≒ 30 / ON ≒ 2）", aEoff, aEon));
            bool ok = (aA < 1.0) && (Math.Abs(aB - 90.0) < 3.0) && (px > 0.95) && (aD < 1.0) &&
                      (aEoff > 25.0) && (aEon < 6.0);
            sb.AppendLine(string.Format("  orient filter = {0}", ok ? "OK" : "NG"));
            RunMotionTests(sb);
        }

        /// <summary>位置推定（加速度の 2 重積分＋静止検出 ZUPT）の数値検証。実機が無くても実行できる。</summary>
        static void RunMotionTests(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- 位置推定（加速度 2 重積分）の検証（合成データ） ---");

            // F) 静止: 重力 +Z だけ → 位置は 0 のまま、静止検出（ZUPT）も入る
            var oF = new Orient();
            var mF = new Motion();
            var sF = new Sample(); sF.HasGyro = true; sF.HasAcc = true; sF.Az = (int)Consts.AccPerG;
            for (int i = 1; i <= 495; i++) { sF.T = i / 165.0; oF.Update(sF, Consts.GyroPerDps); mF.Update(sF, oF, Consts.AccPerG); }
            double pF = Math.Sqrt(mF.Px * mF.Px + mF.Py * mF.Py + mF.Pz * mF.Pz);

            // G) 水平 0.3g を 0.4 秒 → x = ½·0.3g·t² ≒ 0.235 m。
            //    重力補正は「持続加速度を傾きに吸い込む」性質があるので、積分そのものの検証として
            //    AccGain=0 にして姿勢の影響を分離する（実運用では加速度が短い動作なら影響は小さい）。
            var oG = new Orient(); oG.AccGain = 0;
            var mG = new Motion();
            var sG = new Sample(); sG.HasGyro = true; sG.HasAcc = true;
            sG.Ax = (int)(Consts.AccPerG * 0.3); sG.Az = (int)Consts.AccPerG;
            const int nG = 66;                    // 0.4 秒 @165Hz
            for (int i = 1; i <= nG; i++) { sG.T = i / 165.0; oG.Update(sG, Consts.GyroPerDps); mG.Update(sG, oG, Consts.AccPerG); }
            double xG = mG.Px;
            double xExp = 0.5 * 0.3 * 9.80665 * (nG / 165.0) * (nG / 165.0);

            // H) 1g（静止）に戻す → 0.35 秒の静止検出で速度ゼロ、その後は位置が止まる
            sG.Ax = 0;
            for (int i = nG + 1; i <= 198; i++) { sG.T = i / 165.0; oG.Update(sG, Consts.GyroPerDps); mG.Update(sG, oG, Consts.AccPerG); }
            double xMid = mG.Px;
            for (int i = 199; i <= 264; i++) { sG.T = i / 165.0; oG.Update(sG, Consts.GyroPerDps); mG.Update(sG, oG, Consts.AccPerG); }
            double xEnd = mG.Px;
            double frozeGap = Math.Abs(xEnd - xMid);
            bool ok = (pF < 1e-4) && mF.Zupt &&
                      (Math.Abs(xG - xExp) < xExp * 0.05) &&
                      mG.Zupt && (frozeGap < 0.01);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  F 静止          : 位置 {0,9:0.00000} m（期待 0 / 静止検出 {1}）", pF, mF.Zupt ? "ON" : "OFF"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  G 水平 0.3g×0.4秒: x {0,9:0.000} m（期待 {1:0.000}）", xG, xExp));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  H ZUPT 停止     : 残移動 {0,9:0.00000} m（期待 ≒ 0 / 静止検出 {1}）", frozeGap, mG.Zupt ? "ON" : "OFF"));
            sb.AppendLine(string.Format("  motion = {0}", ok ? "OK" : "NG"));
        }

        /// <summary>ジャイロに 1deg/s のバイアス＋体固定の磁気ベクトルを 30 秒与え、yaw のドリフト量を返す。</summary>
        static double RunMagTest(bool magYaw)
        {
            var o = new Orient(); o.MagYaw = magYaw;
            var s = new Sample();
            s.HasGyro = true; s.HasAcc = true; s.HasMag = true;
            s.Az = (int)Consts.AccPerG;                 // 静止（重力は +Z）
            s.Mx = 400; s.My = -300; s.Mz = -450;        // 体に固定された磁気ベクトル
            s.Gz = (int)Consts.GyroPerDps;               // 1 deg/s のバイアス
            for (int i = 1; i <= 4950; i++)              // 30 秒 @165Hz
            { s.T = i / 165.0; o.Update(s, Consts.GyroPerDps); }
            return o.AngleDegFromIdentity();
        }

        static int Finish(StringBuilder sb, string outPath, int code)
        {
            if (string.IsNullOrEmpty(outPath))
                outPath = Path.Combine(Path.GetTempPath(), "sensor-viewer-selftest.txt");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine(outPath);
            return code;
        }

        sealed class AxisStat
        {
            public long N;
            public double MinX = double.MaxValue, MaxX = double.MinValue;
            public double MinY = double.MaxValue, MaxY = double.MinValue;
            public double MinZ = double.MaxValue, MaxZ = double.MinValue;
            public void Add(double x, double y, double z)
            {
                N++;
                if (x < MinX) MinX = x; if (x > MaxX) MaxX = x;
                if (y < MinY) MinY = y; if (y > MaxY) MaxY = y;
                if (z < MinZ) MinZ = z; if (z > MaxZ) MaxZ = z;
            }
            public override string ToString()
            {
                return string.Format("X[{0:0}..{1:0}]  Y[{2:0}..{3:0}]  Z[{4:0}..{5:0}]", MinX, MaxX, MinY, MaxY, MinZ, MaxZ);
            }
        }
    }
}
