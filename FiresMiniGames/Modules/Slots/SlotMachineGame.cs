using System;
using System.Collections.Generic;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Slots
{
    /// <summary>
    /// The placed one-armed bandit. Interact opens a wager popup; the spin claims the machine's ZDO, rolls one of
    /// the drum's eight faces per reel and writes (t0, result) — every client, spectators and late joiners alike,
    /// replays the identical deterministic animation from that pair: lever pull, spin-up, and the three reels
    /// stagger-stopping on the exact faces the payout is judged on (the Window/sym markers baked by the builder
    /// carry each face's angle). Payouts land in the spinner's purse; the last win lives on the machine.
    /// </summary>
    public sealed class SlotMachineGame : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyState = "sm_state";
        private const string KeyT0 = "sm_t0";
        private const string KeyResult = "sm_r";
        private const string KeyBet = "sm_bet";
        private const string KeyPlayer = "sm_player";
        private const string KeyLast = "sm_last";

        private const float SpinUp = 0.45f;
        private const float SettleWindow = 0.55f;
        private const float SpinSpeed = -560f;             // deg/s, negative scrolls faces downward past the window
        private const float TotalTime = 4.7f;
        private static readonly float[] StopTimes = { 4.05f, 3.2f, 2.35f };   // reel indices run right-to-left on screen; screen-left stops first

        private static readonly Dictionary<string, int> Paytable = new Dictionary<string, int>
        {
            { "Diamond", 100 }, { "Star", 50 }, { "Eth", 30 }, { "Oscar", 25 },
            { "Cake", 20 }, { "Doge", 15 }, { "Cherry", 10 },
        };
        private const string MysterySymbol = "Question_Mark";

        private ZNetView _nview;
        private Transform[] _reels;
        private Quaternion[] _reelBase;
        private float[] _windowAngle;
        private string[][] _plateNames;                    // per reel, per plate
        private float[][] _plateAngles;
        private Transform _lever;
        private Quaternion _leverBase;

        private bool _hostSpin;                            // we started the live spin and owe the payout
        private int _localBet;
        private bool _posedIdle;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Machines.MachinePieces.RebindBundleShaders(gameObject);
            _reels = new Transform[3];
            _reelBase = new Quaternion[3];
            _windowAngle = new float[3];
            _plateNames = new string[3][];
            _plateAngles = new float[3][];
            for (int r = 0; r < 3; r++)
            {
                var t = transform.Find("model/Reel" + r);
                _reels[r] = t;
                if (t == null) { enabled = false; return; }
                _reelBase[r] = t.localRotation;
                var names = new List<string>();
                var angles = new List<float>();
                foreach (Transform c in t)
                {
                    if (c.name == "Window") _windowAngle[r] = AngleOf(c);
                    else if (c.name.StartsWith("sym_")) { names.Add(c.name.Substring(4)); angles.Add(AngleOf(c)); }
                }
                _plateNames[r] = names.ToArray();
                _plateAngles[r] = angles.ToArray();
                if (names.Count == 0) { enabled = false; return; }
            }
            _lever = transform.Find("model/Cylinder.001");
            if (_lever != null) _leverBase = _lever.localRotation;
            if (_nview == null || !_nview.IsValid()) enabled = false;
        }

        private static float AngleOf(Transform t) => Mathf.Atan2(t.localPosition.y, t.localPosition.x) * Mathf.Rad2Deg;

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        private bool SpinLive(ZDO zdo, out float t)
        {
            t = (float)(Now - zdo.GetFloat(KeyT0));
            return zdo.GetInt(KeyState) == 1 && t >= 0f && t < TotalTime + 6f;
        }

        // ── interact / hover ────────────────────────────────────────────────────────────────────────────────────
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            var zdo = _nview.GetZDO();
            if (SpinLive(zdo, out float t) && t < TotalTime)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "The reels are spinning...");
                return true;
            }

            var bets = MiniGamesConfig.ParseSizes(MiniGamesConfig.SlotBets != null ? MiniGamesConfig.SlotBets.Value : null, new[] { 2, 10, 50 });
            var options = new List<(string, string, Action)>();
            foreach (var b in bets)
            {
                int bet = b;
                options.Add(($"Bet {bet} coins", "Three of a kind pays up to 100x. Two alike returns the stake.", () => StartSpin(bet)));
            }
            MiniGamePopup.Show("Slot Machine", "Pull the arm — the drums decide.",
                $"Purse: {CoinWallet.Count(player)} coins", options.ToArray());
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Slot Machine";

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "Slot Machine";
            var zdo = _nview.GetZDO();
            if (SpinLive(zdo, out float t) && t < TotalTime)
                return Localization.instance.Localize("Slot Machine — spinning...");
            string last = zdo.GetString(KeyLast, "");
            string text = "Slot Machine\n[<color=yellow><b>$KEY_Use</b></color>] Play";
            if (!string.IsNullOrEmpty(last)) text += "\n<color=#c9a227>" + last + "</color>";
            return Localization.instance.Localize(text);
        }

        // ── spin ───────────────────────────────────────────────────────────────────────────────────────────────
        private void StartSpin(int bet)
        {
            var player = Player.m_localPlayer;
            if (player == null || _nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (SpinLive(zdo, out float live) && live < TotalTime) return;
            if (!CoinWallet.TrySpend(player, bet))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough coins.");
                return;
            }
            _nview.ClaimOwnership();
            int r0 = UnityEngine.Random.Range(0, _plateNames[0].Length);
            int r1 = UnityEngine.Random.Range(0, _plateNames[1].Length);
            int r2 = UnityEngine.Random.Range(0, _plateNames[2].Length);
            zdo.Set(KeyResult, r0 | (r1 << 8) | (r2 << 16));
            zdo.Set(KeyBet, bet);
            zdo.Set(KeyPlayer, player.GetPlayerName());
            zdo.Set(KeyT0, (float)Now);
            zdo.Set(KeyState, 1);
            _hostSpin = true;
            _localBet = bet;
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (zdo.GetInt(KeyState) == 1)
            {
                float t = (float)(Now - zdo.GetFloat(KeyT0));
                if (t < 0f) t = 0f;
                int packed = zdo.GetInt(KeyResult);
                Drive(Mathf.Min(t, TotalTime), packed);
                _posedIdle = true;
                if (t >= TotalTime)
                {
                    if (_hostSpin && _nview.IsOwner()) Settle(zdo, packed);
                    else if (t > TotalTime + 6f && _nview.IsOwner()) zdo.Set(KeyState, 0);   // abandoned mid-spin
                }
            }
            else
            {
                _hostSpin = false;
                if (_posedIdle) return;
                _posedIdle = true;
                int packed = zdo.GetInt(KeyResult);
                if (!string.IsNullOrEmpty(zdo.GetString(KeyLast, ""))) Drive(TotalTime, packed);
            }
        }

        // Deterministic pose at time t — spectators and late joiners replay the same spin from (t0, result).
        private void Drive(float t, int packed)
        {
            if (_lever != null)
            {
                float a = t < 0.25f ? Mathf.SmoothStep(0f, 55f, t / 0.25f)
                        : t < 0.7f ? Mathf.SmoothStep(55f, 0f, (t - 0.25f) / 0.45f) : 0f;
                _lever.localRotation = _leverBase * Quaternion.AngleAxis(a, Vector3.forward);
            }
            for (int r = 0; r < 3; r++)
            {
                int plate = (packed >> (r * 8)) & 0xFF;
                if (plate >= _plateAngles[r].Length) plate = 0;
                _reels[r].localRotation = _reelBase[r] * Quaternion.AngleAxis(ReelTheta(r, t, plate), Vector3.forward);
            }
        }

        private float ReelTheta(int reel, float t, int plate)
        {
            float stop = StopTimes[reel];
            float settleStart = stop - SettleWindow;
            float ThetaFree(float time) => time < SpinUp
                ? 0.5f * (SpinSpeed / SpinUp) * time * time
                : 0.5f * SpinSpeed * SpinUp + SpinSpeed * (time - SpinUp);

            float target = Mathf.DeltaAngle(0f, _windowAngle[reel] - _plateAngles[reel][plate]);
            float thetaAtSettle = ThetaFree(settleStart);
            // remaining travel continues in the spin direction and lands exactly on the target face, one bonus turn
            float remainder = Mathf.Repeat(thetaAtSettle - target, 360f) + 360f;

            if (t >= stop) return thetaAtSettle - remainder;
            if (t <= settleStart) return ThetaFree(t);
            float u = (t - settleStart) / SettleWindow;
            float ease = 1f - (1f - u) * (1f - u) * (1f - u);
            return thetaAtSettle - remainder * ease;
        }

        // ── payout (spin owner only) ───────────────────────────────────────────────────────────────────────────
        private void Settle(ZDO zdo, int packed)
        {
            _hostSpin = false;
            zdo.Set(KeyState, 0);

            var player = Player.m_localPlayer;
            string name = zdo.GetString(KeyPlayer, "?");
            int bet = zdo.GetInt(KeyBet, _localBet);
            var syms = new string[3];
            for (int r = 0; r < 3; r++)
            {
                int plate = (packed >> (r * 8)) & 0xFF;
                syms[r] = plate < _plateNames[r].Length ? _plateNames[r][plate] : "?";
            }

            int mult = 0;
            string story;
            if (syms[0] == syms[1] && syms[1] == syms[2])
            {
                if (syms[0] == MysterySymbol) mult = UnityEngine.Random.Range(2, 61);
                else if (!Paytable.TryGetValue(syms[0], out mult)) mult = 10;
                story = $"Three {Pretty(syms[0])}!";
            }
            else if (syms[0] == syms[1] || syms[1] == syms[2] || syms[0] == syms[2])
            {
                mult = 1;
                story = "Two alike — stake returned.";
            }
            else story = "The reels keep your coins.";

            int win = bet * mult;
            if (win > 0 && player != null) CoinWallet.Pay(player, win);
            string msg = mult > 1 ? $"{story} You win {win} coins!" : mult == 1 ? story : story;
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
            zdo.Set(KeyLast, mult > 0 ? $"{name}: {Pretty(syms[0])} | {Pretty(syms[1])} | {Pretty(syms[2])} — {(mult == 1 ? "stake back" : $"won {win}")}"
                                      : $"{name}: {Pretty(syms[0])} | {Pretty(syms[1])} | {Pretty(syms[2])} — house wins");
        }

        private static string Pretty(string sym) => sym.Replace('_', ' ');
    }
}
