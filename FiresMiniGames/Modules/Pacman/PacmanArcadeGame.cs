using System;
using HarmonyLib;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Pacman
{
    /// <summary>
    /// The placed arcade cabinet, coin-op. Interact drops a credit and locks the player in front of the glass the
    /// same way the air hockey table does (stand marker, camera eased to the baked CamPac anchor, the full modal
    /// input block with raw WASD/arrow reads for steering) — then PacSim runs on a 224x288 texture pushed onto the
    /// cabinet's hidden GameScreen quad while the idle screen sprites hide. The machine's high score lives in its
    /// ZDO with the scorer's name, shown on hover and beaten in person.
    /// </summary>
    public sealed class PacmanArcadeGame : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyState = "pm_state";
        private const string KeyPlayer = "pm_player";
        private const string KeyBeat = "pm_beat";
        private const string KeyHi = "pm_hi";
        private const string KeyHiName = "pm_hiname";

        internal static PacmanArcadeGame Active;
        private static readonly object Token = new object();

        private ZNetView _nview;
        private Transform _cam, _stand, _screenRoot, _gameScreen;
        private Renderer[] _idleSprites;

        private PacSim _sim;
        private Texture2D _tex;
        private Color32[] _buf;
        private Material _screenMat;
        private float _beatT;
        private float _overT;
        private Vector3 _savedPlayerPos;      // pre-seat player pose, restored on exit so the camera returns to normal
        private Quaternion _savedPlayerRot;

        // Uniform scale the cabinet was placed / structure-tweaked at, so the seat leash tracks its real size.
        private float CabinetScale { get { var s = transform.lossyScale; return Mathf.Max(s.x, Mathf.Max(s.y, s.z)); } }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _cam = transform.Find("Markers/CamPac");
            _stand = transform.Find("Markers/StandPac");
            _screenRoot = transform.Find("Screen");
            _gameScreen = transform.Find("Screen/GameScreen");
            if (_cam == null || _stand == null || _screenRoot == null || _gameScreen == null) { enabled = false; return; }

            var sprites = new System.Collections.Generic.List<Renderer>();
            foreach (var name in new[] { "model/ghost1", "model/ghost2", "model/ghost3", "model/ghost4", "model/pacman" })
            {
                var t = transform.Find(name);
                if (t != null) sprites.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
            _idleSprites = sprites.ToArray();
            Machines.MachinePieces.RebindBundleShaders(gameObject);   // cure the stripped-variant magenta screen
            if (_nview == null || !_nview.IsValid()) enabled = false;
        }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        private bool OccupiedByOther(ZDO zdo)
        {
            if (zdo.GetInt(KeyState) != 1) return false;
            if (Active == this) return false;
            return Now - zdo.GetFloat(KeyBeat) < 8.0;
        }

        // ── interact / hover ────────────────────────────────────────────────────────────────────────────────────
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid() || Active != null) return false;
            var zdo = _nview.GetZDO();
            if (OccupiedByOther(zdo))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"{zdo.GetString(KeyPlayer, "Someone")} is playing.");
                return true;
            }
            int cost = MiniGamesConfig.ArcadeCost != null ? Mathf.Max(0, MiniGamesConfig.ArcadeCost.Value) : 2;
            int hi = zdo.GetInt(KeyHi);
            string footer = $"Purse: {CoinWallet.Count(player)} coins" + (hi > 0 ? $"   High score: {hi:n0} ({zdo.GetString(KeyHiName, "?")})" : "");
            string label = cost > 0 ? $"Insert coin — {cost}" : "Play";
            MiniGamePopup.Show("Arcade", "The little ghosts never rest. Arrows or WASD steer — Esc walks away.",
                footer, (label, "Three lives. Clear the maze, chase the fruit, beat the house record.", () => StartGame(cost)));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Arcade Cabinet";

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "Arcade Cabinet";
            var zdo = _nview.GetZDO();
            int hi = zdo.GetInt(KeyHi);
            string hiLine = hi > 0 ? $"\n<color=#c9a227>High score: {hi:n0} ({zdo.GetString(KeyHiName, "?")})</color>" : "";
            if (OccupiedByOther(zdo))
                return Localization.instance.Localize($"Arcade Cabinet — {zdo.GetString(KeyPlayer, "someone")} is playing{hiLine}");
            int cost = MiniGamesConfig.ArcadeCost != null ? Mathf.Max(0, MiniGamesConfig.ArcadeCost.Value) : 2;
            string use = cost > 0 ? $"Insert coin ({cost})" : "Play";
            return Localization.instance.Localize($"Arcade Cabinet\n[<color=yellow><b>$KEY_Use</b></color>] {use}{hiLine}");
        }

        // ── session ────────────────────────────────────────────────────────────────────────────────────────────
        private void StartGame(int cost)
        {
            var player = Player.m_localPlayer;
            if (player == null || _nview == null || !_nview.IsValid() || Active != null) return;
            var zdo = _nview.GetZDO();
            if (OccupiedByOther(zdo)) return;
            if (cost > 0 && !CoinWallet.TrySpend(player, cost))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Not enough coins.");
                return;
            }

            _nview.ClaimOwnership();
            zdo.Set(KeyState, 1);
            zdo.Set(KeyPlayer, player.GetPlayerName());
            zdo.Set(KeyBeat, (float)Now);

            _savedPlayerPos = player.transform.position;
            _savedPlayerRot = player.transform.rotation;
            player.transform.position = _stand.position;
            player.transform.rotation = Quaternion.LookRotation(_stand.forward);
            player.SetMoveDir(Vector3.zero);

            Active = this;
            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = true;
            // Suppress movement/build/hotbar via the text-capture gate ONLY — NOT FiresCore.UI.InputBlock, whose
            // camera pin would fight our screen-fit lock and freeze the view at the player's zoom. The cabinet is
            // keyboard-only, so we hide the cursor ourselves and own the camera outright.
            FiresCore.Input.FiresInputBlock.Acquire(Token);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            foreach (var r in _idleSprites) if (r != null) r.enabled = false;
            _screenRoot.gameObject.SetActive(true);

            EnsureScreenSurface();
            _sim = new PacSim(zdo.GetInt(KeyHi), OnGameOver);
            _overT = 0f;
            FiresCore.UI.MiniGameCamera.Begin(ScreenPose, ease: 12f, tuneKey: "arcade");
        }

        // A single owned texture + material instance reused for every play. Instancing renderer.material and
        // destroying it per session left the renderer pointing at a dead material — magenta on the second play.
        private void EnsureScreenSurface()
        {
            if (_tex == null)
            {
                _tex = new Texture2D(PacSim.W, PacSim.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _buf = new Color32[PacSim.W * PacSim.H];
            }
            var renderer = _gameScreen.GetComponent<Renderer>();
            if (_screenMat == null)
            {
                _screenMat = new Material(renderer.sharedMaterial);   // copy the shader-rebound bundle mat, never destroyed mid-life
                _screenMat.color = Color.white;
                _screenMat.SetTexture("_MainTex", _tex);
                _screenMat.SetTexture("_EmissionMap", _tex);
                _screenMat.SetColor("_EmissionColor", MiniGamesConfig.ScreenEmission());
                _screenMat.EnableKeyword("_EMISSION");
            }
            renderer.sharedMaterial = _screenMat;
        }

        private void OnGameOver(int score)
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo != null && score > zdo.GetInt(KeyHi) && _nview.IsOwner())
            {
                zdo.Set(KeyHi, score);
                zdo.Set(KeyHiName, Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"NEW HIGH SCORE — {score:n0}!");
            }
            else MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Game over — {score:n0}.");
        }

        internal void EndSession()
        {
            if (Active != this) return;
            Active = null;
            _sim = null;
            foreach (var r in _idleSprites) if (r != null) r.enabled = true;
            if (_screenRoot != null) _screenRoot.gameObject.SetActive(false);
            // The texture + material instance are kept alive and reused next play — destroying renderer.material
            // mid-life is exactly what caused the second-play magenta. They release in OnDestroy.

            FiresCore.UI.MiniGameCamera.End();
            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = false;
            FiresCore.Input.FiresInputBlock.Release(Token);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            var player = Player.m_localPlayer;
            if (player != null && _savedPlayerPos != Vector3.zero)
            {
                player.transform.position = _savedPlayerPos;
                player.transform.rotation = _savedPlayerRot;
                player.SetMoveDir(Vector3.zero);
            }

            if (_nview != null && _nview.IsValid() && _nview.IsOwner()) _nview.GetZDO().Set(KeyState, 0);
        }

        private void OnDestroy()
        {
            if (Active == this) EndSession();
            if (_screenMat != null) { Destroy(_screenMat); _screenMat = null; }
            if (_tex != null) { Destroy(_tex); _tex = null; }
            _buf = null;
        }

        private void Update()
        {
            if (Active != this || _sim == null) return;
            var player = Player.m_localPlayer;
            // Leash from the seat, scaled with the cabinet — a structure-tweaked (enlarged) cabinet seats you past a
            // fixed 5 m from its pivot and would end the game the instant it began. Seat-relative starts at ~0.
            Vector3 seat = _stand != null ? _stand.position : transform.position;
            if (player == null || player.IsDead() ||
                Vector3.Distance(player.transform.position, seat) > 5f * Mathf.Max(1f, CabinetScale))
            { EndSession(); return; }

            // Self-illuminate the picture at the configured brightness so the cabinet stays readable at night; read
            // live so the F8 Screen Brightness slider responds while you play.
            if (_screenMat != null)
                _screenMat.SetColor("_EmissionColor", MiniGamesConfig.ScreenEmission());

            _beatT -= Time.deltaTime;
            if (_beatT <= 0f && _nview.IsValid())
            {
                _beatT = 2f;
                _nview.GetZDO().Set(KeyBeat, (float)Now);
            }

            // Hold Ctrl+Alt to reposition the cabinet's camera — freeze the game while tuning.
            if (FiresCore.UI.MiniGameCamera.IsTuning) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { EndSession(); return; }

            int dx = 0, dy = 0;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dy = -1;
            else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dy = 1;
            else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx = -1;
            else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx = 1;

            _sim.Tick(Time.deltaTime, dx, dy);
            _sim.Render(_buf);
            _tex.SetPixels32(_buf);
            _tex.Apply(false);

            if (_sim.Over)
            {
                _overT += Time.deltaTime;
                if (_overT > 1.2f) EndSession();
            }
        }

        // ── camera: the shared FiresCore MiniGameCamera drives the shot (the Valcast technique); we just hand it a
        // head-on screen-fill pose each frame. Fixed narrow FOV so the flat screen reads with little perspective;
        // InputBlock's pin yields to MiniGameCamera, so nothing fights the transform. ────────────────────────────
        private const float ScreenFov = 52f;

        private FiresCore.UI.MiniGameCamera.Pose? ScreenPose()
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.m_camera : null;
            if (cam == null || _gameScreen == null) return null;
            var mf = _gameScreen.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            var ext = mf.sharedMesh.bounds.extents;
            var ls = _gameScreen.lossyScale;
            float halfW = Mathf.Abs(ext.x * ls.x), halfH = Mathf.Abs(ext.y * ls.y);
            if (halfW < 0.001f || halfH < 0.001f) return null;

            Vector3 center = _gameScreen.position;
            Vector3 outward = -_gameScreen.forward;
            Vector3 up = _gameScreen.up;
            float vHalf = ScreenFov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(cam.aspect, 0.1f));
            const float fill = 0.92f;
            float dist = Mathf.Max(halfH / Mathf.Tan(vHalf), halfW / Mathf.Tan(hHalf)) / fill;
            var pos = center + outward * dist;
            return new FiresCore.UI.MiniGameCamera.Pose(pos, Quaternion.LookRotation((center - pos).normalized, up), ScreenFov);
        }

        // A session open at logout must never leak the input block.
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class ZNet_Shutdown_Guard
        {
            private static void Postfix() { try { Active?.EndSession(); } catch { } }
        }
    }
}
