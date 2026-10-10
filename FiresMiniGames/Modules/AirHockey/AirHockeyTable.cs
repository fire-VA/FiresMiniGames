using System;
using HarmonyLib;
using UnityEngine;

namespace FiresMiniGames.Modules.AirHockey
{
    /// <summary>
    /// The placed air hockey table: a real-time in-world match played on the actual prefab objects, driven by the
    /// lockpicking-minigame interaction pattern — the player is stood at their end, the camera locks to that end's
    /// anchor, the cursor is freed and their pusher glides to the cursor's point on the table plane.
    ///
    /// Authority: the player who starts a match claims the table's ZDO and simulates the puck (walls, goals,
    /// pusher impacts). Positions stream through the ZDO in rink-normalized coords (~12 Hz) for the guest and any
    /// spectators; the PvP guest sends their pusher over a per-object RPC (~15 Hz) and predicts it locally. The
    /// scoreboard digits read the same ZDO (see AirHockeyScoreboard), so the score is visible to everyone and
    /// survives reload. Vs Computer plays the house AI on the far end. First to 7 takes the match.
    /// </summary>
    public sealed class AirHockeyTable : MonoBehaviour, Interactable, Hoverable
    {
        public const string KeyState = "ah_state";        // 0 idle, 1 awaiting challenger, 2 pvp live, 3 vs-ai live
        public const string KeyHost = "ah_host";
        public const string KeyWin = "ah_win";
        public const string KeyBeat = "ah_beat";          // authority heartbeat (world seconds) — stale = abandoned match
        private const string KeyPuckX = "ah_px"; private const string KeyPuckZ = "ah_pz";
        private const string KeyAX = "ah_ax"; private const string KeyAZ = "ah_az";
        private const string KeyBX = "ah_bx"; private const string KeyBZ = "ah_bz";

        private const int WinScore = 7;
        private const float PuckMaxSpeed = 4.5f;
        private const float ServePause = 0.9f;
        private const float SyncRate = 0.08f;
        private const float SendRate = 0.066f;

        internal static AirHockeyTable Active;            // the local player's live session table
        private static readonly object Token = new object();

        private ZNetView _nview;
        private Transform _rink, _puck, _pusherA, _pusherB, _seatA, _seatB, _camA, _camB;
        private float _puckR = 0.055f, _pushR = 0.086f;

        // local session
        private bool _host, _sideA;
        private float _lastSeen;                          // watchdog: last authority update a non-host saw

        // authority sim state (rink-local metres; x across, z along; A owns +z)
        private Vector2 _pk, _pkVel, _pa, _pb, _bTarget, _paPrev;
        private float _serveT, _syncT, _sendT, _joinT, _stallT;
        private Vector2 _paVel, _pbVel, _pbPrev;

        // spectator/guest visual targets (world)
        private Vector3 _vPuck, _vA, _vB;
        private bool _vInit;

        // first-person: the local player's head bone is shrunk for the session (hair/helmet ride it)
        private Transform _headBone;
        private Vector3 _headScale = Vector3.one;
        private Vector3 _savedPlayerPos;      // pre-seat player pose, restored on exit so the camera returns to normal
        private Quaternion _savedPlayerRot;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _rink = transform.Find("rig/Markers/Rink");
            _puck = transform.Find("rig/Puck");
            _pusherA = transform.Find("rig/PusherA");
            _pusherB = transform.Find("rig/PusherB");
            _seatA = transform.Find("rig/Markers/SeatA");
            _seatB = transform.Find("rig/Markers/SeatB");
            _camA = transform.Find("rig/Markers/CamA");
            _camB = transform.Find("rig/Markers/CamB");
            if (_nview == null || !_nview.IsValid() || _rink == null) { enabled = _rink != null; return; }

            var pr = _puck != null ? _puck.GetComponent<Renderer>() : null;
            if (pr != null) _puckR = Mathf.Max(pr.bounds.extents.x, pr.bounds.extents.z);
            var ar = _pusherA != null ? _pusherA.GetComponent<Renderer>() : null;
            if (ar != null) _pushR = Mathf.Max(ar.bounds.extents.x, ar.bounds.extents.z);

            _nview.Register<string>("AH_Join", RPC_Join);
            _nview.Register<float, float>("AH_PusherB", RPC_PusherB);
            _nview.Register("AH_Leave", RPC_Leave);
        }

        // ── geometry helpers (world <-> rink-local metres, live so piece scaling keeps working) ────────────────
        private float HalfW => _rink.lossyScale.x * 0.5f;
        private float HalfL => _rink.lossyScale.z * 0.5f;
        private float GoalHalf => _rink.lossyScale.y * 0.5f;
        private Vector3 RinkRight => transform.right;
        private Vector3 RinkFwd => transform.forward;

        private Vector3 World(Vector2 local) => _rink.position + RinkRight * local.x + RinkFwd * local.y;
        private Vector2 Local(Vector3 world)
        {
            var d = world - _rink.position;
            return new Vector2(Vector3.Dot(d, RinkRight), Vector3.Dot(d, RinkFwd));
        }

        // ── interact / hover ────────────────────────────────────────────────────────────────────────────────────
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (Active != null) return false;
            int state = MatchLive() ? _nview.GetZDO().GetInt(KeyState) : 0;
            if (state == 2 || state == 3)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Match in progress");
                return true;
            }
            if (state == 1)
            {
                string host = _nview.GetZDO().GetString(KeyHost, "?");
                Common.MiniGamePopup.Show("Air Hockey", $"{host} awaits a challenger.",
                    ("Join Match", $"Take the far end against {host}.", JoinPvp));
                return true;
            }
            Common.MiniGamePopup.Show("Air Hockey", "The neon rink hums. First to 7.",
                ("Vs Computer", "Play the house — it never blinks.", StartVsAi),
                ("Vs Player", "Open the table and wait for a challenger.", StartHostPvp));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // A live match must have a fresh authority heartbeat — a host who crashed or logged out mid-match must
        // not leave the table bricked on "match in progress".
        private bool MatchLive()
        {
            if (_nview == null || !_nview.IsValid()) return false;
            var zdo = _nview.GetZDO();
            if (zdo.GetInt(KeyState) == 0) return false;
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
            return now - zdo.GetFloat(KeyBeat) < 8.0;
        }

        public string GetHoverText()
        {
            if (Active == this) return "";
            int state = MatchLive() ? _nview.GetZDO().GetInt(KeyState) : 0;
            if (state == 2 || state == 3) return Localization.instance.Localize("Air Hockey — match in progress");
            if (state == 1) return Localization.instance.Localize("Air Hockey\n[<color=yellow><b>$KEY_Use</b></color>] Join match");
            return Localization.instance.Localize("Air Hockey\n[<color=yellow><b>$KEY_Use</b></color>] Play");
        }

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Air Hockey";

        // ── session start / end ────────────────────────────────────────────────────────────────────────────────
        private void StartVsAi()
        {
            _nview.ClaimOwnership();
            ResetMatch();
            _nview.GetZDO().Set(KeyState, 3);
            _nview.GetZDO().Set(KeyHost, Player.m_localPlayer.GetPlayerName());
            Enter(host: true, sideA: true);
        }

        private void StartHostPvp()
        {
            _nview.ClaimOwnership();
            ResetMatch();
            _nview.GetZDO().Set(KeyState, 1);
            _nview.GetZDO().Set(KeyHost, Player.m_localPlayer.GetPlayerName());
            Enter(host: true, sideA: true);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Waiting for a challenger...");
        }

        private void JoinPvp()
        {
            _nview.InvokeRPC("AH_Join", Player.m_localPlayer.GetPlayerName());
            Enter(host: false, sideA: false);
            _joinT = 4f;
        }

        private void ResetMatch()
        {
            var zdo = _nview.GetZDO();
            zdo.Set(AirHockeyScoreboard.KeyScoreA, 0);
            zdo.Set(AirHockeyScoreboard.KeyScoreB, 0);
            zdo.Set(KeyWin, "");
            zdo.Set(KeyBeat, (float)(ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0));
            _pk = Vector2.zero; _pkVel = Vector2.zero;
            _pa = new Vector2(0f, HalfL * 0.6f);
            _pb = new Vector2(0f, -HalfL * 0.6f);
            _bTarget = _pb;
            _serveT = ServePause * 0.5f;
            _stallT = 0f;
        }

        private void Enter(bool host, bool sideA)
        {
            Active = this;
            _host = host; _sideA = sideA;
            _lastSeen = Time.time;

            var player = Player.m_localPlayer;
            var seat = sideA ? _seatA : _seatB;
            if (player != null && seat != null)
            {
                _savedPlayerPos = player.transform.position;
                _savedPlayerRot = player.transform.rotation;
                player.transform.position = seat.position;
                var look = transform.position - seat.position; look.y = 0f;
                if (look.sqrMagnitude > 0.001f) player.transform.rotation = Quaternion.LookRotation(look.normalized);
                player.SetMoveDir(Vector3.zero);

                var anim = player.GetComponentInChildren<Animator>();
                _headBone = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                if (_headBone != null)
                {
                    _headScale = _headBone.localScale;
                    _headBone.localScale = Vector3.one * 0.001f;
                }
            }

            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = true;
            if (!FiresCore.UI.InputBlock.IsBlocked) FiresCore.UI.InputBlock.Block(true);
            FiresCore.Input.FiresInputBlock.Acquire(Token);
            FiresCore.UI.MiniGameCamera.Begin(EndCameraPose, ease: 9f, tuneKey: "airhockey");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // The shared MiniGameCamera drives the shot to my end's baked anchor (top-down over the rink). Vanilla FOV
        // is kept (fov 0) so the anchor frames the table exactly as authored; the InputBlock pin yields to this.
        private FiresCore.UI.MiniGameCamera.Pose? EndCameraPose()
        {
            var anchor = _sideA ? _camA : _camB;
            if (anchor == null) return null;
            return new FiresCore.UI.MiniGameCamera.Pose(anchor.position, anchor.rotation, 0f);
        }

        internal void EndSession(string centerMsg = null)
        {
            if (Active != this) return;
            Active = null;
            if (_headBone != null)
            {
                _headBone.localScale = _headScale.sqrMagnitude > 0.0001f ? _headScale : Vector3.one;
                _headBone = null;
            }
            FiresCore.UI.MiniGameCamera.End();
            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = false;
            FiresCore.Input.FiresInputBlock.Release(Token);
            if (FiresCore.UI.InputBlock.IsBlocked) FiresCore.UI.InputBlock.Block(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            var lp = Player.m_localPlayer;
            if (lp != null && _savedPlayerPos != Vector3.zero)
            {
                lp.transform.position = _savedPlayerPos;
                lp.transform.rotation = _savedPlayerRot;
                lp.SetMoveDir(Vector3.zero);
            }
            if (_nview != null && _nview.IsValid())
            {
                if (_host && _nview.IsOwner()) _nview.GetZDO().Set(KeyState, 0);
                else if (!_host) _nview.InvokeRPC("AH_Leave");
            }
            if (!string.IsNullOrEmpty(centerMsg)) MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, centerMsg);
        }

        // ── RPCs (arrive on the ZDO owner) ─────────────────────────────────────────────────────────────────────
        private void RPC_Join(long sender, string name)
        {
            if (!_nview.IsOwner()) return;
            if (_nview.GetZDO().GetInt(KeyState) != 1) return;
            _nview.GetZDO().Set(KeyState, 2);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{name} takes the far end!");
        }

        private void RPC_PusherB(long sender, float nx, float nz)
        {
            if (!_nview.IsOwner()) return;
            _bTarget = new Vector2(nx * HalfW, nz * HalfL);
        }

        private void RPC_Leave(long sender)
        {
            if (!_nview.IsOwner()) return;
            if (_nview.GetZDO().GetInt(KeyState) == 2)
            {
                _nview.GetZDO().Set(KeyState, 0);
                if (Active == this) EndSession("Your challenger left the table.");
            }
        }

        // ── per-frame ──────────────────────────────────────────────────────────────────────────────────────────
        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || _rink == null) return;
            int state = _nview.GetZDO().GetInt(KeyState);

            if (Active == this) RunSession(state);
            else if (state == 2 || state == 3 || state == 1) RenderFromZdo();
        }

        private void RunSession(int state)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead() ||
                Vector3.Distance(player.transform.position, transform.position) > 12f)
            { EndSession(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { EndSession(_host ? "Match closed." : "You left the match."); return; }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // guest watchdog: joining that never went live, or a host that vanished
            if (!_host)
            {
                if (state == 2 && !MatchLive()) { EndSession("The table went quiet."); return; }
                if (state == 2) { _joinT = 0f; _lastSeen = Time.time; }
                else if (_joinT > 0f)
                {
                    _joinT -= Time.deltaTime;
                    if (_joinT <= 0f) { EndSession("No answer from the table."); return; }
                }
                else if (state == 0)
                {
                    string win = _nview.GetZDO().GetString(KeyWin, "");
                    EndSession(string.IsNullOrEmpty(win) ? "Match ended." : win);
                    return;
                }
            }
            if (_host && state == 0) { EndSession(); return; }

            // my pusher follows the cursor on the table plane, clamped to my half
            Vector2 mine;
            if (CursorOnTable(out mine))
            {
                mine.x = Mathf.Clamp(mine.x, -HalfW + _pushR, HalfW - _pushR);
                mine.y = _sideA
                    ? Mathf.Clamp(mine.y, _puckR, HalfL - _pushR)
                    : Mathf.Clamp(mine.y, -HalfL + _pushR, -_puckR);
                if (_host) { _paPrev = _pa; _pa = mine; }
                else
                {
                    PositionNode(_pusherB, mine);
                    _sendT -= Time.deltaTime;
                    if (_sendT <= 0f)
                    {
                        _sendT = SendRate;
                        _nview.InvokeRPC("AH_PusherB", mine.x / HalfW, mine.y / HalfL);
                    }
                }
            }

            if (_host)
            {
                _paVel = (_pa - _paPrev) / Mathf.Max(Time.deltaTime, 0.001f);
                if (state == 3) DriveAi(Time.deltaTime);
                if (state != 1) SimPuck(Time.deltaTime);
                _pbPrev = _pb;

                PositionNode(_pusherA, _pa);
                PositionNode(_pusherB, _pb);
                PositionNode(_puck, _pk);

                _syncT -= Time.deltaTime;
                if (_syncT <= 0f)
                {
                    _syncT = SyncRate;
                    var zdo = _nview.GetZDO();
                    zdo.Set(KeyPuckX, _pk.x / HalfW); zdo.Set(KeyPuckZ, _pk.y / HalfL);
                    zdo.Set(KeyAX, _pa.x / HalfW); zdo.Set(KeyAZ, _pa.y / HalfL);
                    zdo.Set(KeyBX, _pb.x / HalfW); zdo.Set(KeyBZ, _pb.y / HalfL);
                    zdo.Set(KeyBeat, (float)(ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0));
                }
            }
            else
            {
                // guest: puck + host pusher from the stream
                var zdo = _nview.GetZDO();
                LerpNode(_puck, new Vector2(zdo.GetFloat(KeyPuckX) * HalfW, zdo.GetFloat(KeyPuckZ) * HalfL));
                LerpNode(_pusherA, new Vector2(zdo.GetFloat(KeyAX) * HalfW, zdo.GetFloat(KeyAZ) * HalfL));
            }
        }

        private void RenderFromZdo()
        {
            var zdo = _nview.GetZDO();
            LerpNode(_puck, new Vector2(zdo.GetFloat(KeyPuckX) * HalfW, zdo.GetFloat(KeyPuckZ) * HalfL));
            LerpNode(_pusherA, new Vector2(zdo.GetFloat(KeyAX) * HalfW, zdo.GetFloat(KeyAZ) * HalfL));
            LerpNode(_pusherB, new Vector2(zdo.GetFloat(KeyBX) * HalfW, zdo.GetFloat(KeyBZ) * HalfL));
        }

        private void PositionNode(Transform t, Vector2 local)
        {
            if (t == null) return;
            var w = World(local);
            t.position = new Vector3(w.x, t.position.y, w.z);
        }

        private void LerpNode(Transform t, Vector2 local)
        {
            if (t == null) return;
            var w = World(local);
            var target = new Vector3(w.x, t.position.y, w.z);
            if (!_vInit) { _vInit = true; t.position = target; return; }
            t.position = Vector3.Lerp(t.position, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        private bool CursorOnTable(out Vector2 local)
        {
            local = Vector2.zero;
            if (FiresCore.UI.MiniGameCamera.IsTuning) return false;   // park the paddle while tuning the camera
            var cam = GameCamera.instance != null ? GameCamera.instance.m_camera : Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, _rink.position);
            if (!plane.Raycast(ray, out float d)) return false;
            local = Local(ray.GetPoint(d));
            return true;
        }

        // ── authority sim ──────────────────────────────────────────────────────────────────────────────────────
        private void SimPuck(float dt)
        {
            if (_serveT > 0f)
            {
                _serveT -= dt;
                return;
            }
            const int steps = 3;
            const float minBounce = 0.14f;   // walls always return SOME energy — a cornered puck must never die
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                _pk += _pkVel * h;

                float xMax = HalfW - _puckR;
                if (_pk.x > xMax) { _pk.x = xMax; _pkVel.x = -Mathf.Max(Mathf.Abs(_pkVel.x) * 0.92f, minBounce); }
                else if (_pk.x < -xMax) { _pk.x = -xMax; _pkVel.x = Mathf.Max(Mathf.Abs(_pkVel.x) * 0.92f, minBounce); }

                float zMax = HalfL - _puckR;
                if (Mathf.Abs(_pk.y) > zMax)
                {
                    if (Mathf.Abs(_pk.x) < GoalHalf - _puckR * 0.5f) { Goal(scoredOnA: _pk.y > 0f); return; }
                    _pk.y = Mathf.Sign(_pk.y) * zMax;
                    _pkVel.y = -Mathf.Sign(_pk.y) * Mathf.Max(Mathf.Abs(_pkVel.y) * 0.92f, minBounce);
                }

                Collide(ref _pk, ref _pkVel, _pa, _paVel);
                Collide(ref _pk, ref _pkVel, _pb, (_pb - _pbPrev) / Mathf.Max(dt, 0.001f));
            }
            _pkVel *= Mathf.Exp(-0.16f * dt);
            float sp = _pkVel.magnitude;
            if (sp > PuckMaxSpeed) _pkVel *= PuckMaxSpeed / sp;

            // The air table never lets a puck die: parked (pinned in a corner, wedged by a pusher) for over a
            // second, it drifts back toward centre ice on the jets.
            if (sp < 0.15f)
            {
                _stallT += dt;
                if (_stallT > 1.1f)
                {
                    _stallT = 0f;
                    var toCenter = -_pk;
                    if (toCenter.sqrMagnitude < 0.001f) toCenter = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f));
                    _pkVel += toCenter.normalized * 0.65f;
                }
            }
            else _stallT = 0f;
        }

        private void Collide(ref Vector2 pk, ref Vector2 vel, Vector2 pusher, Vector2 pusherVel)
        {
            var d = pk - pusher;
            float rr = _puckR + _pushR;
            float dist = d.magnitude;
            if (dist >= rr || dist < 0.0001f) return;
            var n = d / dist;
            pk = pusher + n * rr;
            var rel = vel - pusherVel;
            float vn = Vector2.Dot(rel, n);
            if (vn < 0f) vel = rel - 1.85f * vn * n + pusherVel;
        }

        private void Goal(bool scoredOnA)
        {
            var zdo = _nview.GetZDO();
            string key = scoredOnA ? AirHockeyScoreboard.KeyScoreB : AirHockeyScoreboard.KeyScoreA;
            int score = zdo.GetInt(key) + 1;
            zdo.Set(key, score);

            _pk = Vector2.zero;
            _serveT = ServePause;
            float dir = scoredOnA ? 1f : -1f;   // possession to the side that conceded
            _pkVel = new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f), dir * 1.25f);

            if (score >= WinScore)
            {
                bool aWon = key == AirHockeyScoreboard.KeyScoreA;
                string host = zdo.GetString(KeyHost, "The host");
                string msg = zdo.GetInt(KeyState) == 3
                    ? (aWon ? $"{host} beats the house {WinScore}:{zdo.GetInt(AirHockeyScoreboard.KeyScoreB)}!" : "The house wins. It always does.")
                    : (aWon ? $"{host} takes the match!" : "The challenger takes the match!");
                zdo.Set(KeyWin, msg);
                zdo.Set(KeyState, 0);
                EndSession(msg);
            }
        }

        private void DriveAi(float dt)
        {
            Vector2 target;
            float speed;
            // A puck jammed against the walls of the AI's corners cannot be attacked head-on — pressing it just
            // pins it. Back off to the defend line and let the table's air jets float it out.
            bool cornered = _pk.y < -HalfL + _pushR * 2.6f && Mathf.Abs(_pk.x) > HalfW - _pushR * 2.6f;
            if (_pk.y < -0.03f && !cornered)
            {
                var fromGoal = _pk - new Vector2(0f, -HalfL);
                target = _pk + fromGoal.normalized * 0.015f;
                speed = 3.1f;
            }
            else
            {
                target = new Vector2(cornered ? 0f : _pk.x * 0.55f, -HalfL * 0.74f);
                speed = 2.1f;
            }
            target.x = Mathf.Clamp(target.x, -HalfW + _pushR, HalfW - _pushR);
            target.y = Mathf.Clamp(target.y, -HalfL + _pushR, -_puckR);
            _pb = Vector2.MoveTowards(_pb, target, speed * dt);
        }

        // Camera is driven by the shared FiresCore.UI.MiniGameCamera (see EndCameraPose) — no per-table postfix.

        // A session open at logout must never leak the input block.
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class ZNet_Shutdown_Guard
        {
            private static void Postfix() { try { Active?.EndSession(); } catch { } }
        }
    }
}
