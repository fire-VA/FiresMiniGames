using System;
using System.Collections.Generic;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Roulette
{
    /// <summary>
    /// The placed roulette wheel. Interact opens the betting board (RouletteBetUI); Spin claims the ZDO, draws a
    /// pocket and writes (t0, number) — every client replays the same deterministic show: the wheel winds down
    /// while the ball orbits against it, spirals off the track, hops, and settles into the pocket ring riding the
    /// wheel. The wheel's final rotation is chosen so the drawn number is physically under the ball (the wheel
    /// carries the standard European ring; the builder bakes a Pocket0 marker at the zero cell's angle). The
    /// baked Animator idle is disabled at Awake — this component is the drum's only writer.
    /// </summary>
    public sealed class RouletteGame : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyState = "rl_state";
        private const string KeyT0 = "rl_t0";
        private const string KeyNum = "rl_num";
        private const string KeyPlayer = "rl_player";
        private const string KeyLast = "rl_last";

        private const float TotalTime = 8.2f;
        private const float BallTime = 6.1f;               // orbit + settle complete, ball rides the wheel after
        private const float SpiralStart = 3.7f;
        private const float PocketF = 0.56f;               // pocket-ring pose, calibrated by render sweep
        private const float PocketDz = -0.14f;
        private const float IdleDrift = 7f;                // deg/s, the parked wheel never quite sleeps

        private static readonly HashSet<int> RedSet = new HashSet<int>
            { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        private ZNetView _nview;
        private Transform _wheel, _orbit, _ball;
        private Quaternion _wheelBase, _orbitBase;
        private Vector3 _ballBase;
        private readonly float[] _pocketAngle = new float[37];   // per-number cell angle, baked PK_ markers
        private float _g0;                                  // ball's wheel-local angle at orbit angle 0 (wheel at base)
        private float _sign;                                // orbit-angle -> wheel-local-angle transfer sign

        private Dictionary<string, int> _pendingBets;       // the spinner's board, local only
        private bool _hostSpin;
        private float _idleW, _idleO;                       // parked drift state (local cosmetic)
        private bool _spinSeen;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Machines.MachinePieces.RebindBundleShaders(gameObject);
            _wheel = transform.Find("model/Plane.001");
            _orbit = transform.Find("model/Empty.002");
            _ball = _orbit != null ? _orbit.Find("Sphere") : null;
            if (_wheel == null || _orbit == null || _ball == null) { enabled = false; return; }
            for (int n = 0; n <= 36; n++)
            {
                var pk = _wheel.Find("PK_" + n);
                if (pk == null) { enabled = false; return; }
                _pocketAngle[n] = Mathf.Atan2(pk.localPosition.y, pk.localPosition.x) * Mathf.Rad2Deg;
            }

            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;

            _wheelBase = _wheel.localRotation;
            _orbitBase = _orbit.localRotation;
            _ballBase = _ball.localPosition;

            // orbit->wheel angle transfer, measured on the real transforms (restored within the same frame)
            _ball.localPosition = PocketPose(1f);
            _g0 = BallWheelAngle();
            _orbit.localRotation = _orbitBase * Quaternion.AngleAxis(10f, Vector3.forward);
            _sign = Mathf.Sign(Mathf.DeltaAngle(_g0, BallWheelAngle()));
            if (_sign == 0f) _sign = 1f;
            _orbit.localRotation = _orbitBase;
            _ball.localPosition = PocketPose(1f);           // parked in a pocket from the first frame

            if (_nview == null || !_nview.IsValid()) enabled = false;
        }

        private Vector3 PocketPose(float u)
        {
            float f = Mathf.Lerp(1f, PocketF, u);
            float dz = Mathf.Lerp(0f, PocketDz, u);
            return new Vector3(_ballBase.x * f, _ballBase.y * f, _ballBase.z + dz);
        }

        private float BallWheelAngle()
        {
            var p = _wheel.InverseTransformPoint(_ball.position);
            return Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        // ── interact / hover ────────────────────────────────────────────────────────────────────────────────────
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            var zdo = _nview.GetZDO();
            float t = (float)(Now - zdo.GetFloat(KeyT0));
            if (zdo.GetInt(KeyState) == 1 && t < TotalTime)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "The wheel is spinning...");
                return true;
            }
            RouletteBetUI.Show(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Roulette";

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "Roulette";
            var zdo = _nview.GetZDO();
            float t = (float)(Now - zdo.GetFloat(KeyT0));
            if (zdo.GetInt(KeyState) == 1 && t < TotalTime)
                return Localization.instance.Localize("Roulette — no more bets");
            string last = zdo.GetString(KeyLast, "");
            string text = "Roulette\n[<color=yellow><b>$KEY_Use</b></color>] Place your bets";
            if (!string.IsNullOrEmpty(last)) text += "\n<color=#c9a227>" + last + "</color>";
            return Localization.instance.Localize(text);
        }

        // ── spin ───────────────────────────────────────────────────────────────────────────────────────────────
        internal void StartSpin(Dictionary<string, int> bets)
        {
            var player = Player.m_localPlayer;
            if (player == null || _nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            float live = (float)(Now - zdo.GetFloat(KeyT0));
            if (zdo.GetInt(KeyState) == 1 && live < TotalTime) return;

            _nview.ClaimOwnership();
            zdo.Set(KeyNum, UnityEngine.Random.Range(0, 37));
            zdo.Set(KeyPlayer, player.GetPlayerName());
            zdo.Set(KeyT0, (float)Now);
            zdo.Set(KeyState, 1);
            _pendingBets = bets;
            _hostSpin = true;
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (zdo.GetInt(KeyState) == 1)
            {
                float t = (float)(Now - zdo.GetFloat(KeyT0));
                if (t < 0f) t = 0f;
                if (t <= TotalTime + 0.5f)
                {
                    Drive(Mathf.Min(t, TotalTime), zdo.GetInt(KeyNum));
                    _spinSeen = true;
                    _idleW = _idleO = 0f;
                }
                if (t >= TotalTime)
                {
                    if (_hostSpin && _nview.IsOwner()) Settle(zdo);
                    else if (t > TotalTime + 8f && _nview.IsOwner()) zdo.Set(KeyState, 0);
                }
            }
            else
            {
                _hostSpin = false;
                // parked drift — the ball rides its pocket
                float dt = Time.deltaTime * IdleDrift;
                _idleW += dt; _idleO += dt * _sign;
                if (_spinSeen)
                {
                    int num = zdo.GetInt(KeyNum);
                    _wheel.localRotation = _wheelBase * Quaternion.AngleAxis(WheelEnd(num) + _idleW, Vector3.forward);
                    _orbit.localRotation = _orbitBase * Quaternion.AngleAxis(OrbitEnd(num) + _idleO, Vector3.forward);
                }
                else
                {
                    _wheel.localRotation = _wheelBase * Quaternion.AngleAxis(_idleW, Vector3.forward);
                    _orbit.localRotation = _orbitBase * Quaternion.AngleAxis(_idleO, Vector3.forward);
                }
            }
        }

        // ── deterministic show, identical on every client from (t0, num) ───────────────────────────────────────
        private static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1f - (1f - u) * (1f - u) * (1f - u); }

        private float OrbitEnd(int num) => -_sign * (1440f + Mathf.Repeat(num * 87.3f, 360f));

        private float WheelEnd(int num)
        {
            float need = Mathf.Repeat(_g0 + _sign * OrbitEnd(num) - _pocketAngle[num], 360f);
            return need + 1080f;                            // three full turns of drama, landing on the pocket
        }

        private void Drive(float t, int num)
        {
            float wheelTheta = WheelEnd(num) * EaseOut(t / TotalTime);
            float orbitTheta;
            if (t < BallTime)
                orbitTheta = OrbitEnd(num) * EaseOut(t / BallTime);
            else
                orbitTheta = OrbitEnd(num) + _sign * (wheelTheta - WheelEnd(num) * EaseOut(BallTime / TotalTime));

            _wheel.localRotation = _wheelBase * Quaternion.AngleAxis(wheelTheta, Vector3.forward);
            _orbit.localRotation = _orbitBase * Quaternion.AngleAxis(orbitTheta, Vector3.forward);

            float u = Mathf.InverseLerp(SpiralStart, BallTime, t);
            float settle = Mathf.SmoothStep(0f, 1f, u);
            var pose = PocketPose(settle);
            if (u > 0f && u < 1f) pose.z += -0.012f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f)) * (1f - u);
            _ball.localPosition = pose;
        }

        // ── payout (spin owner only) ───────────────────────────────────────────────────────────────────────────
        private void Settle(ZDO zdo)
        {
            _hostSpin = false;
            zdo.Set(KeyState, 0);
            int num = zdo.GetInt(KeyNum);
            string name = zdo.GetString(KeyPlayer, "?");
            string color = num == 0 ? "GREEN" : RedSet.Contains(num) ? "RED" : "BLACK";

            int winnings = 0, staked = 0;
            if (_pendingBets != null)
            {
                foreach (var kv in _pendingBets)
                {
                    staked += kv.Value;
                    winnings += Returns(kv.Key, kv.Value, num);
                }
            }
            _pendingBets = null;

            var player = Player.m_localPlayer;
            if (winnings > 0 && player != null) CoinWallet.Pay(player, winnings);
            string msg = winnings > 0
                ? $"The ball lands on {num} {color} — you collect {winnings} coins!"
                : staked > 0 ? $"The ball lands on {num} {color} — the house takes it." : $"The ball lands on {num} {color}.";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
            zdo.Set(KeyLast, $"{name}: {num} {color}" + (winnings > 0 ? $" — won {winnings}" : staked > 0 ? " — house" : ""));
        }

        internal static int Returns(string key, int amount, int num)
        {
            switch (key)
            {
                case "red": return num != 0 && RedSet.Contains(num) ? amount * 2 : 0;
                case "black": return num != 0 && !RedSet.Contains(num) ? amount * 2 : 0;
                case "odd": return num != 0 && num % 2 == 1 ? amount * 2 : 0;
                case "even": return num != 0 && num % 2 == 0 ? amount * 2 : 0;
                case "low": return num >= 1 && num <= 18 ? amount * 2 : 0;
                case "high": return num >= 19 ? amount * 2 : 0;
                case "dz1": return num >= 1 && num <= 12 ? amount * 3 : 0;
                case "dz2": return num >= 13 && num <= 24 ? amount * 3 : 0;
                case "dz3": return num >= 25 ? amount * 3 : 0;
                default:
                    return key.Length > 1 && key[0] == 's' && int.TryParse(key.Substring(1), out int n) && n == num
                        ? amount * 36 : 0;
            }
        }
    }
}
