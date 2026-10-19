// Odyssey Mouse - 入力処理エンジン
// tools/odyssey-mouse.ps1 (実機検証済み PoC) のループをそのまま移植したもの。
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace OdysseyMouse
{
    internal enum EngineState { Searching, Calibrating, Running, Reconnecting }

    /// <summary>UI へ渡す状態のスナップショット。</summary>
    internal sealed class Status
    {
        public EngineState State = EngineState.Searching;
        public bool LinkLost;
        public string DevicePath = "";
        public int ReportsPerSec;
        public double OutPxPerSec;
        public long TotalReports;
        public int CenterX, CenterY;
        public bool CenterValid;

        public int StickX, StickY, PadX, PadY, Trig;
        public bool BtnL, BtnR, BtnM, BtnGrip, BtnMenu, BtnWin;
        public bool PadTouch, PadSession, ScrollMode, Boost;
        public bool SendBlocked;
        public int LastReadErr;

        public const int HistLen = 160;
        public readonly double[] Hist = new double[HistLen];
        public int HistCount;
        public int HistHead;
        public readonly object Lock = new object();
    }

    internal sealed class Engine
    {
        public const int VendorId = 0x045E;
        public const int ProductId = 0x065D;

        readonly Status _st = new Status();
        public Status St { get { return _st; } }

        Thread _th;
        volatile bool _run;
        HidDevice _dev;
        volatile Settings _s = new Settings();
        volatile bool _recalib;

        // ---- スティック ----
        double _cx = 2048.0, _cy = 2048.0;

        // ---- タッチパッドの状態 ----
        int _padX, _padY, _padPrevX = -1, _padPrevY = -1;
        bool _padMovedPrev;
        bool _padSession, _padPending, _pendBigJump;
        double _restX, _restY;
        bool _padRestValid;
        DateTime _padStillSince = DateTime.Now;
        double _padFX, _padFY, _padPXf, _padPYf;
        double _padRemX, _padRemY, _padSpeedEma;
        DateTime _tapT0 = DateTime.MinValue;
        int _tapX0, _tapY0;
        bool _tapMoved, _tapClicked;
        bool _padTouching;                 // 確認済みの接触中（255 のフレッタでリセットしない）
        double _padOutX, _padOutY;         // パッドが出力した累計 px（タップ時の巻き戻し用）
        double _padSnapX, _padSnapY;       // 接触開始時点のスナップショット
        bool _pendingTapClick;             // 位置を戻してからクリックするための保留

        // ---- 出力 ----
        double _accX, _accY, _wheelAcc, _hwheelAcc;
        double _cumX, _cumY;
        int _sentX, _sentY;

        // ---- ボタン ----
        bool _prevLeft, _prevRight, _prevMid, _prevMenu;
        bool _scrollMode;
        bool _wasEnabled = true;

        // ---- ログ ----
        StreamWriter _csv;
        string _csvPath;
        double _el;

        // ---- 診断 ----
        readonly Stopwatch _sw = new Stopwatch();
        int _reports, _reportsInSec, _reportsPerSec;
        double _outPxInSec, _outPxPerSec;
        DateTime _tick = DateTime.Now;
        volatile string _lastEvent = "";
        public string LastEvent { get { return _lastEvent; } }
        public string CsvPath { get { return _csvPath; } }

        public void Start()
        {
            if (_run) return;
            _run = true;
            _th = new Thread(Run);
            _th.IsBackground = true;
            _th.Priority = ThreadPriority.AboveNormal;
            _th.Name = "OdysseyEngine";
            _th.Start();
        }

        public void Stop()
        {
            _run = false;
            var t = _th;
            if (t != null) { try { t.Join(1500); } catch { } }
            ReleaseAll();
            CloseCsv();
            var d = _dev;
            if (d != null) { try { d.HapticsOff(); } catch { } try { d.Dispose(); } catch { } }
        }

        public void SetSettings(Settings s) { _s = s; }
        public void RequestCalibrate() { _recalib = true; }

        public void TestHaptics(int strength, int ms)
        {
            var d = _dev;
            if (d != null) d.HapticPulse(strength, strength, ms);
        }

        // ------------------------------------------------------------------
        void Run()
        {
            _sw.Restart();
            while (_run)
            {
                var s0 = _s;
                if (_dev == null || !_dev.IsOpen)
                {
                    // デバイスを探す / 開き直す
                    _st.State = EngineState.Searching;
                    string p = HidDevice.FindPath(VendorId, ProductId);
                    if (p == null)
                    {
                        _st.DevicePath = "";
                        _st.CenterValid = false;
                        for (int i = 0; i < 10 && _run; i++) Thread.Sleep(100);
                        continue;
                    }
                    var d = new HidDevice(VendorId, ProductId);
                    if (!d.Open(p))
                    {
                        d.Dispose();
                        Thread.Sleep(500);
                        continue;
                    }
                    _dev = d;
                    _st.DevicePath = p;
                    _st.LinkLost = false;
                    _LastEvent("接続しました");
                    Calibrate();
                    _st.State = EngineState.Running;
                }

                var s = _s;
                int len;
                try
                {
                    len = _dev.Read(250);
                }
                catch (Exception ex)
                {
                    _LastEvent("読み取り例外: " + ex.Message);
                    len = 0;
                }

                if (len <= 0)
                {
                    HandleReadFailure();
                    continue;
                }
                _failStreak = 0;
                if (_linkLost)
                {
                    _linkLost = false;
                    _st.LinkLost = false;
                }
                if (_dev.ReportID != 1) continue;

                _reports++;
                _reportsInSec++;
                double now = _sw.Elapsed.TotalSeconds;
                double dt = now - _lastT;
                _lastT = now;
                if (dt <= 0) dt = 0.0065;
                if (dt > 0.1) dt = 0.1;
                _el = now;

                Step(s, dt);
                TickSeconds();
            }
        }

        double _lastT;
        int _failStreak;
        bool _linkLost;
        DateTime _lastReconnect = DateTime.MinValue;

        void HandleReadFailure()
        {
            _failStreak++;
            if (_failStreak >= 4)
            {
                if (!_linkLost)
                {
                    _linkLost = true;
                    _st.LinkLost = true;
                    _st.State = EngineState.Reconnecting;
                    _LastEvent("入力が止まりました（デバイス切断）");
                }
                if ((DateTime.Now - _lastReconnect).TotalSeconds >= 1.0)
                {
                    _lastReconnect = DateTime.Now;
                    if (_dev != null && _dev.Reconnect())
                    {
                        _LastEvent("再接続しました");
                        _st.LinkLost = false;
                        _st.State = EngineState.Running;
                        ResetPadState();
                    }
                }
            }
            Thread.Sleep(5);
        }

        void TickSeconds()
        {
            var now = DateTime.Now;
            double sec = (now - _tick).TotalSeconds;
            if (sec < 0.5) return;
            _reportsPerSec = (int)Math.Round(_reportsInSec / sec);
            _outPxPerSec = _outPxInSec / sec;
            _reportsInSec = 0; _outPxInSec = 0; _tick = now;
            _st.ReportsPerSec = _reportsPerSec;
            _st.OutPxPerSec = _outPxPerSec;
            _st.TotalReports = _reports;
        }

        void _LastEvent(string s) { _lastEvent = s; }

        // ------------------------------------------------------------------
        // 中心位置の自動計測
        // ------------------------------------------------------------------
        void Calibrate()
        {
            var s = _s;
            if (s.StickCenterX != 0 || s.StickCenterY != 0)
            {
                _cx = s.StickCenterX; _cy = s.StickCenterY;
                _st.CenterX = (int)_cx; _st.CenterY = (int)_cy; _st.CenterValid = true;
                return;
            }
            _st.State = EngineState.Calibrating;
            double sx = 0, sy = 0; int n = 0;
            int pad255 = 0;
            bool gotPad = false;
            double padSumX = 0, padSumY = 0; int padN = 0;
            var lim = DateTime.Now.AddSeconds(3.0);
            while (_run && DateTime.Now < lim)
            {
                if (n >= 40 && gotPad) break;
                if (pad255 >= 100) break;
                if (_dev.Read(250) <= 0) { Thread.Sleep(1); continue; }
                if (_dev.ReportID != 1) continue;
                if (n < 40)
                {
                    sx += (int)_dev.Get(8); sy += (int)_dev.Get(7); n++;
                }
                int px = (int)_dev.Get(11), py = (int)_dev.Get(10);
                if (px != 255 && py != 255)
                {
                    gotPad = true; pad255 = 0;
                    if (padN < 200) { padSumX += px; padSumY += py; padN++; }
                }
                else pad255++;
            }
            if (n > 0) { _cx = sx / n; _cy = sy / n; }
            _st.CenterX = (int)_cx; _st.CenterY = (int)_cy; _st.CenterValid = true;

            _padRestValid = false;
            if (gotPad && padN > 0)
            {
                _restX = Math.Round(padSumX / padN); _restY = Math.Round(padSumY / padN);
                _padRestValid = true;
            }
            else { _restX = 0; _restY = 0; }

            _padFX = _restX; _padFY = _restY;
            _padPXf = _padFX; _padPYf = _padFY;
            _padPrevX = -1; _padPrevY = -1; _padMovedPrev = false;
            _padStillSince = DateTime.Now;
            _lastT = _sw.Elapsed.TotalSeconds;
            _LastEvent(string.Format(CultureInfo.InvariantCulture, "中心 X={0:F1} Y={1:F1}", _cx, _cy));
        }

        void ResetPadState()
        {
            _padSession = false; _padPending = false; _pendBigJump = false;
            _padSpeedEma = 0; _padRemX = 0; _padRemY = 0;
            _padTouching = false; _pendingTapClick = false;
            _padOutX = 0; _padOutY = 0; _padSnapX = 0; _padSnapY = 0;
            _padFX = _padX; _padFY = _padY;
            _padPXf = _padFX; _padPYf = _padFY;
        }

        /// <summary>
        /// 接触開始からいままでの間にパッドが出力した分を打ち消す。
        /// 指を着地させた瞬間の数 px がそのままカーソルへ出てしまい「タップすると
        /// カーソルがずれる」になるため、タップ確定時に相対移動で戻す。
        /// </summary>
        void UndoPadOutputSinceTap()
        {
            double ux = _padOutX - _padSnapX + _padRemX;
            double uy = _padOutY - _padSnapY + _padRemY;
            _padRemX = 0; _padRemY = 0;
            int ix = (int)Math.Round(ux), iy = (int)Math.Round(uy);
            if (ix != 0 || iy != 0) MouseOut.Send(Win32.MOUSEEVENTF_MOVE, -ix, -iy, 0);
        }

        void ReleaseAll()
        {
            if (_prevLeft) { MouseOut.Send(Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0); _prevLeft = false; }
            if (_prevRight) { MouseOut.Send(Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0); _prevRight = false; }
            if (_prevMid) { MouseOut.Send(Win32.MOUSEEVENTF_MIDDLEUP, 0, 0, 0); _prevMid = false; }
            var d = _dev;
            if (d != null) d.HapticsOff();
        }

        // ------------------------------------------------------------------
        // 1 レポートぶんの処理
        // ------------------------------------------------------------------
        void Step(Settings s, double dt)
        {
            if (_recalib) { _recalib = false; Calibrate(); }

            int sx = (int)_dev.Get(8), sy = (int)_dev.Get(7);
            int trig = (int)_dev.Get(9);
            _padX = (int)_dev.Get(11); _padY = (int)_dev.Get(10);
            bool bit6 = _dev.Has(6);
            bool padTouched = bit6 || (_padX != 255 && _padY != 255);

            // ---- 状態表示用 ----
            _st.StickX = sx; _st.StickY = sy; _st.PadX = _padX; _st.PadY = _padY; _st.Trig = trig;
            _st.PadTouch = padTouched;
            _st.BtnGrip = _dev.Has(0); _st.BtnMenu = _dev.Has(1); _st.BtnWin = _dev.Has(2);

            // ---- 無効時は読み取りと状態表示だけを行い、入力は一切送らない ----
            if (!s.Enabled)
            {
                if (_wasEnabled) { ReleaseAll(); _wasEnabled = false; }
                _st.BtnL = _dev.Has(4) || (trig >= s.TriggerThreshold);
                _st.BtnR = _dev.Has(0);
                _st.BtnM = _dev.Has(3);
                _st.PadSession = false;
                _padMovedPrev = (_padX != _padPrevX) || (_padY != _padPrevY);
                _padPrevX = _padX; _padPrevY = _padY;
                _accX = 0; _accY = 0; _wheelAcc = 0; _hwheelAcc = 0;
                ResetPadState();
                UpdateHistory();
                return;
            }
            if (!_wasEnabled) { _wasEnabled = true; ResetPadState(); }

            // ---- タッチパッド: 休止値とタッチダウン/リフトの判定 ----
            var now = DateTime.Now;
            bool padNoTouch = (_padX == 255 || _padY == 255);
            bool padMoved = (_padX != _padPrevX) || (_padY != _padPrevY);
            double jump = Math.Max(Math.Abs(_padX - _padPrevX), Math.Abs(_padY - _padPrevY));
            bool prevFrozen = !_padMovedPrev;
            double padStillSec = (now - _padStillSince).TotalSeconds;
            if (padMoved) _padStillSince = now;
            else if (padStillSec >= 0.05 && _padX != 255 && !_padSession)
            {
                // 休止値の追従は接触セッション外だけ（セッション中に追従すると指を止めた位置を
                // 休止値と誤認して、離したときの戻りを検出できなくなる）
                _restX = _padX; _restY = _padY; _padRestValid = true;
            }
            double toRest = 999.0;
            if (_padRestValid) toRest = Math.Max(Math.Abs(_padX - _restX), Math.Abs(_padY - _restY));

            bool padDown = !padNoTouch && !_padSession && prevFrozen && jump >= 3 && padStillSec >= 0.05;
            bool padUpCand = !padNoTouch && _padSession && !prevFrozen && jump >= s.PadJumpMax &&
                             ((_padRestValid && toRest <= s.PadRestBand) || jump >= s.PadSnapEnd);

            // ---- スティック -> 速度(px/s) ----
            double dzN = s.StickDeadzone / 2048.0;
            double ax = 0, ay = 0;
            if (s.StickEnabled)
            {
                double sxn = (sx - _cx) / 2047.0;
                double syn = (sy - _cy) / 2047.0;
                if (s.InvertY) syn = -syn;   // 上下反転（既定 ON）
                if (Math.Abs(sxn) > dzN) { double t = (Math.Abs(sxn) - dzN) / (1.0 - dzN); ax = Math.Sign(sxn) * Math.Pow(t, s.StickExponent); }
                if (Math.Abs(syn) > dzN) { double t = (Math.Abs(syn) - dzN) / (1.0 - dzN); ay = Math.Sign(syn) * Math.Pow(t, s.StickExponent); }
            }
            double vx = ax * s.StickSensPx;
            double vy = ay * s.StickSensPx;

            // ---- タッチパッド（相対移動 + タップ判定）----
            bool padActive = false;
            int padUpFlag = 0, padDownFlag = 0;
            if (s.PadEnabled && _wasEnabled)
            {
                // 接触開始のアンカー。
                // 位置フィルタ（_padFX）が「前回タッチした場所」のまま残っていると、
                // 次に指を置いた瞬間にその差分が丸ごとカーソル移動として出て
                // 「タップした瞬間にカーソルが飛ぶ」原因になる。実接触の初フレームで
                // 今の値に合わせ、接触中は 255 のフレッタがあっても再アンカーしない。
                if (!padNoTouch && !_padTouching)
                {
                    _padFX = _padX; _padFY = _padY;
                    _padPXf = _padFX; _padPYf = _padFY;
                    _padSpeedEma = 0;
                    _padSnapX = _padOutX; _padSnapY = _padOutY;   // タップ時の巻き戻し基準
                    _padTouching = true;
                }
                string padAct = "move";
                if (padNoTouch) padAct = "lift";
                else if (padDown) padAct = "down";
                else if (_padPending)
                {
                    if ((_padRestValid && toRest <= s.PadRestBand) || (_pendBigJump && !padMoved)) padAct = "lift";
                    else { _padPending = false; padAct = "move"; }
                }
                else if (padUpCand) { _padPending = true; _pendBigJump = (jump >= s.PadSnapEnd); padAct = "pending"; }

                if (padAct == "down")
                {
                    _padSession = true;
                    _tapT0 = now; _tapX0 = _padX; _tapY0 = _padY; _tapMoved = false; _tapClicked = _dev.Has(4);
                    _padFX = _padX; _padFY = _padY;
                    _padPXf = _padFX; _padPYf = _padFY;
                    _padSpeedEma = 0;
                    padActive = true; padDownFlag = 1;
                }
                else if (padAct == "lift")
                {
                    if (_padSession && s.TapClick && !_tapMoved && !_tapClicked && !_prevLeft)
                    {
                        double tapDur = (now - _tapT0).TotalSeconds;
                        if (tapDur >= s.TapMinSec && tapDur < s.TapMaxSec)
                        {
                            // 着地直後のずれを打ち消してからクリックする
                            UndoPadOutputSinceTap();
                            _pendingTapClick = true;   // 位置の戻し（Move 送信）が先に処理されるよう保留
                            if (s.HapticsOnClick && s.HapticsStrength > 0)
                                _dev.HapticPulse(s.HapticsStrength, s.HapticsStrength, s.HapticsMs);
                        }
                    }
                    _padSession = false; _padPending = false; _padTouching = false; padUpFlag = 1;
                    if (!padNoTouch) { _padFX = _padX; _padFY = _padY; _padPXf = _padFX; _padPYf = _padFY; }
                    _padSpeedEma = 0;
                }
                else if (padAct == "move")
                {
                    padActive = true;
                    // 位置フィルタは毎フレーム更新する（止まったフレームで止めると、
                    // 次に動かした瞬間に古い方向の移動がまとめて噴き出して「ゴム」になる）
                    // 低速では「1 単位≒2.4px」の刻みが階段として見えるので平滑時間を長くして
                    // 引き伸ばし、高速では既定の短い値へ戻して遅延（ゴム感）を出さない。
                    double tauCur = s.PadSmoothTau;
                    if (s.PadSmoothSlowTau > tauCur)
                    {
                        double k = s.PadSmoothRef > 0 ? _padSpeedEma / s.PadSmoothRef : 1.0;
                        if (k > 1.0) k = 1.0; if (k < 0.0) k = 0.0;
                        tauCur = s.PadSmoothSlowTau + (s.PadSmoothTau - s.PadSmoothSlowTau) * k;
                    }
                    double aS = tauCur > 0 ? 1.0 - Math.Exp(-dt / tauCur) : 1.0;
                    _padFX = _padFX + (_padX - _padFX) * aS;
                    _padFY = _padFY + (_padY - _padFY) * aS;
                    if (_padPrevX < 0) { _padFX = _padX; _padFY = _padY; }
                    double dxp = _padFX - _padPXf;
                    double dyp = _padFY - _padPYf;
                    _padPXf = _padFX; _padPYf = _padFY;

                    if (padMoved && _padSession)
                    {
                        double dist = Math.Abs(_padX - _tapX0) + Math.Abs(_padY - _tapY0);
                        if (dist > s.TapMaxMove) _tapMoved = true;
                        if (_dev.Has(4)) _tapClicked = true;
                    }
                    if (dxp != 0.0 || dyp != 0.0)
                    {
                        double spd = Math.Sqrt(dxp * dxp + dyp * dyp) / dt;
                        double a = Math.Min(1.0, dt / s.PadAccelTau);
                        _padSpeedEma = (1.0 - a) * _padSpeedEma + a * spd;
                        double scale = 1.0 + (_padSpeedEma / s.PadAccelRef);
                        if (scale > s.PadAccelMax) scale = s.PadAccelMax;
                        double g = s.PadGain * scale;
                        _padRemX += dxp * g;
                        if (s.InvertY) _padRemY -= dyp * g; else _padRemY += dyp * g;
                    }
                }
            }
            _padPrevX = _padX; _padPrevY = _padY; _padMovedPrev = padMoved;
            _st.PadSession = _padSession;

            // ---- パッドの残量を消費（量子化のガタつきを平滑化）----
            if (Math.Abs(_padRemX) > 0.05 || Math.Abs(_padRemY) > 0.05)
            {
                double stx = _padRemX, sty = _padRemY;   // PadFollowTau = 0（既定）: 全量を消費
                if (stx > s.PadStepMax) stx = s.PadStepMax; else if (stx < -s.PadStepMax) stx = -s.PadStepMax;
                if (sty > s.PadStepMax) sty = s.PadStepMax; else if (sty < -s.PadStepMax) sty = -s.PadStepMax;
                _padRemX -= stx; _padRemY -= sty;
                _accX += stx; _accY += sty;
                _padOutX += stx; _padOutY += sty;
            }
            else { _padRemX = 0; _padRemY = 0; }

            // ---- ボタン ----
            bool leftNow = _dev.Has(4) || (trig >= s.TriggerThreshold);
            bool rightNow = _dev.Has(0);
            bool midNow = _dev.Has(3);
            bool menuNow = _dev.Has(1);
            bool winNow = _dev.Has(2);
            _st.BtnL = leftNow; _st.BtnR = rightNow; _st.BtnM = midNow;

            if (menuNow && !_prevMenu)
            {
                _scrollMode = !_scrollMode;
                _st.ScrollMode = _scrollMode;
            }
            _prevMenu = menuNow;

            // ---- スクロールモード: スティックでホイール ----
            if (_scrollMode)
            {
                // スティックを上へ倒すと上へスクロール。
                // `ay` は InvertY 適用後の値で「上＝負」、Windows のホイールは「正＝上」
                // なので符号を反転して、カーソルの上下と同じ向きに揃える。
                _wheelAcc -= ay * s.WheelRate * dt;
                _hwheelAcc += ax * s.WheelRate * dt;
                vx = 0; vy = 0;
            }
            _st.Boost = winNow;
            if (winNow) { vx *= s.BoostFactor; vy *= s.BoostFactor; }

            _accX += vx * dt;
            _accY += vy * dt;
            if (_accX > 200) _accX = 200; if (_accX < -200) _accX = -200;
            if (_accY > 200) _accY = 200; if (_accY < -200) _accY = -200;
            int dx = (int)Math.Round(_accX);
            int dy = (int)Math.Round(_accY);
            _sentX = 0; _sentY = 0;
            if (dx != 0 || dy != 0)
            {
                _accX -= dx; _accY -= dy;
                _sentX = dx; _sentY = dy;
                MouseOut.Send(Win32.MOUSEEVENTF_MOVE, dx, dy, 0);
                _outPxInSec += Math.Abs(dx) + Math.Abs(dy);
            }
            _cumX += _sentX; _cumY += _sentY;

            // タップのクリックは「位置を戻す Move」が先に処理されるようここに置く
            if (_pendingTapClick) { _pendingTapClick = false; MouseOut.ClickLeft(); }

            // ---- ホイール ----
            if (Math.Abs(_wheelAcc) >= 1.0)
            {
                int w = (int)Math.Truncate(_wheelAcc);
                if (w != 0) { _wheelAcc -= w; MouseOut.Wheel(w, false); }
            }
            if (Math.Abs(_hwheelAcc) >= 1.0)
            {
                int hw = (int)Math.Truncate(_hwheelAcc);
                if (hw != 0) { _hwheelAcc -= hw; MouseOut.Wheel(hw, true); }
            }

            // ---- クリック（エッジ検出）----
            if (leftNow != _prevLeft)
            {
                MouseOut.Send(leftNow ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0);
                if (leftNow && s.HapticsOnClick && s.HapticsStrength > 0) _dev.HapticPulse(s.HapticsStrength, s.HapticsStrength, s.HapticsMs);
                _prevLeft = leftNow;
            }
            if (rightNow != _prevRight)
            {
                MouseOut.Send(rightNow ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0);
                if (rightNow && s.HapticsOnClick && s.HapticsStrength > 0) _dev.HapticPulse(s.HapticsStrength, s.HapticsStrength, s.HapticsMs);
                _prevRight = rightNow;
            }
            if (midNow != _prevMid)
            {
                MouseOut.Send(midNow ? Win32.MOUSEEVENTF_MIDDLEDOWN : Win32.MOUSEEVENTF_MIDDLEUP, 0, 0, 0);
                if (midNow && s.HapticsOnClick && s.HapticsStrength > 0) _dev.HapticPulse(s.HapticsStrength, s.HapticsStrength, s.HapticsMs);
                _prevMid = midNow;
            }

            if (s.CursorClamp) MouseOut.ClampCursor();

            // ---- 診断: 送信がブロックされていないか (管理者権限の画面など) ----
            _st.SendBlocked = MouseOut.LastSendErr == 5;   // ERROR_ACCESS_DENIED

            // ---- グラフ用ヒストリ ----
            UpdateHistory();

            // ---- CSV ログ ----
            if (s.LogPadCsv) EnsureCsv();
            else CloseCsv();
            if (_csv != null)
            {
                _csv.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F4},{1},{2},{3},{4},{5},{6},{7},{8:F0},{9:F0},{10:F0},{11:F0},{12:F2},{13:F2},{14},{15},{16:F0},{17:F0},{18:F2},{19:F2},{20}",
                    _el, _padX, _padY, bit6 ? 1 : 0, padNoTouch ? 1 : 0, padActive ? 1 : 0,
                    padDownFlag, padUpFlag, jump, toRest, _restX, _restY, _padFX, _padFY,
                    _sentX, _sentY, _cumX, _cumY, _padRemX, _padRemY, _padPending ? 1 : 0));
            }
        }

        double _histAcc;
        DateTime _histTick = DateTime.Now;

        void UpdateHistory()
        {
            _histAcc += Math.Abs(_sentX) + Math.Abs(_sentY);
            var now = DateTime.Now;
            if ((now - _histTick).TotalMilliseconds < 50) return;
            _histTick = now;
            lock (_st.Lock)
            {
                _st.Hist[_st.HistHead] = _histAcc;
                _st.HistHead = (_st.HistHead + 1) % Status.HistLen;
                if (_st.HistCount < Status.HistLen) _st.HistCount++;
            }
            _histAcc = 0;
        }

        void EnsureCsv()
        {
            if (_csv != null) return;
            try
            {
                _csvPath = Path.Combine(AppEnv.LogDir, "pad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                _csv = new StreamWriter(_csvPath, false, new UTF8Encoding(false));
                _csv.WriteLine("el,rawX,rawY,bit6,noTouch,active,down,up,jump,toRest,restX,restY,filtX,filtY,sentX,sentY,cumX,cumY,remX,remY,pend");
            }
            catch { _csv = null; }
        }

        void CloseCsv()
        {
            if (_csv == null) return;
            try { _csv.Flush(); _csv.Close(); } catch { }
            _csv = null;
        }
    }
}
