using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// A placed television. Interact powers it on and locks you in front of the screen the same way the arcade
    /// cabinet does (stand marker, camera eased onto the screen, movement suppressed via FiresInputBlock only — no
    /// camera pin — cursor hidden). On direct interact it runs its own DOOM; a nearby NES console can instead hand
    /// it a running emulator to display (see <see cref="TryStartExternal"/>). Either way the set owns the session:
    /// it drives the screen texture from the console each frame, holds the camera, and tears down on Esc / walking
    /// away / death. DOOM's IWAD and the NES ROM both load from the plugin folder.
    /// </summary>
    public sealed class DoomTvGame : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyState = "doom_state";
        private const string KeyPlayer = "doom_player";
        private const string KeyBeat = "doom_beat";

        internal static DoomTvGame Active;

        private ZNetView _nview;
        private Transform _cam, _stand, _screenRoot, _gameScreen;
        private IScreenConsole _runtime;
        private float _beatT;
        private readonly List<Material> _screenMats = new List<Material>();   // inner opaque tube + round glass overlay
        private readonly object _token = new object();
        private Vector3 _savedPlayerPos;      // where the player was before we seated them at the set, so we can
        private Quaternion _savedPlayerRot;   // put them back on exit (the seat move is needed for a distant TV)

        private static string WadDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _cam = transform.Find("Markers/CamTv");
            _stand = transform.Find("Markers/StandTv");
            _screenRoot = transform.Find("Screen");
            _gameScreen = transform.Find("Screen/GameScreen");
            Machines.MachinePieces.RebindBundleShaders(gameObject);
            if (_cam == null || _stand == null || _screenRoot == null || _gameScreen == null || _nview == null || !_nview.IsValid())
                enabled = false;
        }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        // Uniform scale the set was placed / structure-tweaked at, so seat-relative distances track its real size.
        private float TvScale { get { var s = transform.lossyScale; return Mathf.Max(s.x, Mathf.Max(s.y, s.z)); } }

        private bool OccupiedByOther(ZDO zdo)
        {
            if (zdo.GetInt(KeyState) != 1 || Active == this) return false;
            return Now - zdo.GetFloat(KeyBeat) < 8.0;
        }

        /// <summary>True when this set is a valid, unoccupied display an external console (the NES) may claim.</summary>
        internal bool IsFreeForExternal()
        {
            if (!enabled || _nview == null || !_nview.IsValid() || Active != null) return false;
            return !OccupiedByOther(_nview.GetZDO());
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
            MiniGamePopup.Show("Television", "A strange glow from another world. Arrows/WASD move, Ctrl fires, Space opens doors. Esc steps back.",
                ("Turn it on", "Boot the set and play DOOM.", StartGame));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Television";

        public string GetHoverText()
        {
            if (Active == this) return "";   // running a game — no "Turn on" clutter over the screen
            if (_nview == null || !_nview.IsValid()) return "Television";
            if (OccupiedByOther(_nview.GetZDO()))
                return Localization.instance.Localize($"Television — {_nview.GetZDO().GetString(KeyPlayer, "someone")} is playing");
            return Localization.instance.Localize("Television\n[<color=yellow><b>$KEY_Use</b></color>] Turn on");
        }

        // ── session ────────────────────────────────────────────────────────────────────────────────────────────
        private void StartGame()
        {
            if (Player.m_localPlayer == null || _nview == null || !_nview.IsValid() || Active != null) return;
            if (OccupiedByOther(_nview.GetZDO())) return;

            int skill = MiniGamesConfig.DoomSkill != null ? Mathf.Clamp(MiniGamesConfig.DoomSkill.Value, 0, 4) : 2;
            var runtime = new DoomRuntime(WadDir, skill);
            if (!runtime.Available)
            {
                runtime.Dispose();
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    "No DOOM WAD found. Place freedoom1.wad (or doom1.wad) in the FiresMiniGames plugin folder.");
                return;
            }
            EnterSession(runtime, Player.m_localPlayer.GetPlayerName());
        }

        /// <summary>Display an already-built console (the NES) on this set. Returns false if it isn't free; the
        /// caller then disposes the console.</summary>
        internal bool TryStartExternal(IScreenConsole console, string label)
        {
            if (console == null || !IsFreeForExternal()) return false;
            EnterSession(console, label);
            return Active == this;
        }

        // Shared entry for both DOOM and an external console: claim the set, seat the player, lock camera + input,
        // and bind every screen surface to the console's live texture (each keeping its baked opacity).
        private void EnterSession(IScreenConsole console, string label)
        {
            var player = Player.m_localPlayer;
            if (player == null || _nview == null || !_nview.IsValid()) { console.Dispose(); return; }
            _runtime = console;

            _nview.ClaimOwnership();
            var zdo = _nview.GetZDO();
            zdo.Set(KeyState, 1);
            zdo.Set(KeyPlayer, label);
            zdo.Set(KeyBeat, (float)Now);

            _savedPlayerPos = player.transform.position;   // remember before seating, restore on exit
            _savedPlayerRot = player.transform.rotation;
            player.transform.position = _stand.position;
            player.transform.rotation = Quaternion.LookRotation(_stand.forward);
            player.SetMoveDir(Vector3.zero);

            Active = this;
            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = true;
            FiresCore.Input.FiresInputBlock.Acquire(_token);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _screenRoot.gameObject.SetActive(true);
            // Every screen surface under Screen gets the live texture, each keeping its baked opacity: the Soviet
            // has an opaque inner tube + a semi-transparent round overlay on the magnifier; the retro has a single
            // opaque CRT. Materials are created once and reused across sessions.
            if (_screenMats.Count == 0)
            {
                foreach (var r in _screenRoot.GetComponentsInChildren<Renderer>(true))
                {
                    // The BackPlate is the dark tube backing behind the picture. Binding the live game to it too made
                    // a second, parallax-offset copy of the image show through at any camera angle — the "doubled
                    // screen". Leave it on its baked dark material; only the game-picture surfaces get the texture.
                    if (r.name == "BackPlate") continue;
                    var m = new Material(r.sharedMaterial);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", MiniGamesConfig.ScreenEmission());
                    m.color = new Color(1f, 1f, 1f, m.color.a);
                    r.sharedMaterial = m;
                    _screenMats.Add(m);
                }
            }
            foreach (var m in _screenMats)
            {
                m.SetTexture("_MainTex", _runtime.Texture);
                m.SetTexture("_EmissionMap", _runtime.Texture);
            }
            FiresCore.UI.MiniGameCamera.Begin(ScreenPose, ease: 12f, tuneKey: "television");
        }

        internal void EndSession()
        {
            if (Active != this) return;
            Active = null;
            if (_screenRoot != null) _screenRoot.gameObject.SetActive(false);
            // Wipe the live picture off the screen materials too, so nothing can linger on the set if the overlay is
            // ever shown again outside a session.
            foreach (var m in _screenMats)
                if (m != null) { m.SetTexture("_MainTex", null); m.SetTexture("_EmissionMap", null); m.SetColor("_EmissionColor", Color.black); }
            _runtime?.Dispose();
            _runtime = null;

            FiresCore.UI.MiniGameCamera.End();
            FiresCore.UI.ContextMenu.FiresContextMenu.SuppressDriver = false;
            FiresCore.Input.FiresInputBlock.Release(_token);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Put the player back exactly where they were before we seated them, so the world camera returns to its
            // pre-interaction pose instead of leaving you facing the set at a stand-marker angle.
            var player = Player.m_localPlayer;
            if (player != null && _savedPlayerPos != Vector3.zero)
            {
                player.transform.position = _savedPlayerPos;
                player.transform.rotation = _savedPlayerRot;
                player.SetMoveDir(Vector3.zero);
            }

            if (_nview != null && _nview.IsValid() && _nview.IsOwner()) _nview.GetZDO().Set(KeyState, 0);
        }

        private bool _cfgWasOpen;

        private void Update()
        {
            if (Active != this || _runtime == null) return;
            var player = Player.m_localPlayer;
            // Leash from the SEAT, not the set's pivot, and scaled with the set. A structure-tweaked (enlarged) TV
            // seats you well over 5 m from its pivot, so the old pivot-relative 5 m ended the session the instant it
            // began — the camera never settled. Seat-relative it starts at ~0 at any scale; walking off still stops it.
            Vector3 seat = _stand != null ? _stand.position : transform.position;
            if (player == null || player.IsDead() ||
                Vector3.Distance(player.transform.position, seat) > 5f * Mathf.Max(1f, TvScale))
            { EndSession(); return; }

            // Safety: the camera lock is what "being in the TV" means. If it's gone (some other minigame took the
            // camera, or a desync) while we still think we're active, tear down cleanly instead of stranding the
            // player in the world with a live screen + hover text.
            if (!FiresCore.UI.MiniGameCamera.Active) { EndSession(); return; }

            // Keep the picture self-lit at the configured brightness so a game stays readable in the dark. Read live
            // here, before the tuning / config-panel early-outs, so dragging the Screen Brightness slider in the F8
            // panel shows on the screen at once.
            var glow = MiniGamesConfig.ScreenEmission();
            for (int i = 0; i < _screenMats.Count; i++)
                if (_screenMats[i] != null) _screenMats[i].SetColor("_EmissionColor", glow);

            // Hold Ctrl+Alt to reposition this screen's camera (MiniGameCameraTuner) — freeze the game so the drag
            // and scroll don't leak into play.
            if (FiresCore.UI.MiniGameCamera.IsTuning) { Heartbeat(); return; }

            // Open the Fires config while you keep watching the TV, so the camera can be tuned live (the shared
            // MiniGameCamera keeps the shot on the screen even though the panel pins the vanilla camera). We toggle
            // it via raw Input because the panel's own KeyboardShortcut path is suppressed by this session's
            // FiresInputBlock capture.
            if (Input.GetKeyDown(FiresCore.UI.FiresConfigUI.ToggleKey)) FiresCore.UI.ConfigPanel.Toggle();

            if (FiresCore.UI.ConfigPanel.IsOpen)
            {
                _cfgWasOpen = true;    // freeze the game; the panel owns the cursor + Escape while it's up
                Heartbeat();
                return;
            }
            if (_cfgWasOpen)
            {
                // Panel just closed this frame — swallow the Escape/F8 that closed it so it can't also end the
                // session, and take the cursor back (the panel had freed it).
                _cfgWasOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Heartbeat();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) { EndSession(); return; }
            Heartbeat();
            _runtime.Tick(Time.deltaTime);
        }

        private void Heartbeat()
        {
            _beatT -= Time.deltaTime;
            if (_beatT <= 0f && _nview != null && _nview.IsValid()) { _beatT = 2f; _nview.GetZDO().Set(KeyBeat, (float)Now); }
        }

        private void OnDestroy()
        {
            if (Active == this) EndSession();
            foreach (var m in _screenMats) if (m != null) Destroy(m);
            _screenMats.Clear();
        }

        // ── camera: the shared FiresCore MiniGameCamera drives the shot; we hand it a head-on screen-fill pose. ─
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

            // Config-tunable framing, read live so the Core F8 UI moves the camera in real time.
            float fov = MiniGamesConfig.TvCamFov != null ? MiniGamesConfig.TvCamFov.Value : ScreenFov;
            float back = MiniGamesConfig.TvCamBack != null ? MiniGamesConfig.TvCamBack.Value : 0f;
            float side = MiniGamesConfig.TvCamSide != null ? MiniGamesConfig.TvCamSide.Value : 0f;
            float height = MiniGamesConfig.TvCamHeight != null ? MiniGamesConfig.TvCamHeight.Value : 0f;
            float yaw = MiniGamesConfig.TvCamYaw != null ? MiniGamesConfig.TvCamYaw.Value : 0f;
            float pitch = MiniGamesConfig.TvCamPitch != null ? MiniGamesConfig.TvCamPitch.Value : 0f;
            float roll = MiniGamesConfig.TvCamRoll != null ? MiniGamesConfig.TvCamRoll.Value : 0f;

            Vector3 center = _gameScreen.position;
            // Put the camera on the screen face that points toward where the player stands (the StandTv marker),
            // so a screen quad whose normal is flipped per-model (the Soviet ellipse) can't leave us behind the
            // glass looking through the set. World up keeps the shot level regardless of the quad's roll.
            Vector3 n = _gameScreen.forward;
            Vector3 toStand = _stand != null ? (_stand.position - center) : -n;
            Vector3 outward = (Vector3.Dot(n, toStand) >= 0f) ? n : -n;
            Vector3 up = Vector3.up;
            // Yaw/pitch orbit the camera around the screen centre (the picture stays framed, the viewing angle
            // changes); roll below tilts the image. All read live so they can be dialled in from the F8 UI.
            Vector3 orbitRight = Vector3.Cross(up, -outward).normalized;
            outward = (Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, orbitRight) * outward).normalized;
            float vHalf = Mathf.Max(5f, fov) * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(cam.aspect, 0.1f));
            const float fill = 0.92f;
            float dist = Mathf.Max(halfH / Mathf.Tan(vHalf), halfW / Mathf.Tan(hHalf)) / fill + back;
            Vector3 camRight = Vector3.Cross(up, -outward).normalized;   // camera's right; +side = right, -side = left
            var pos = center + outward * dist + camRight * side + up * height;
            Vector3 fwd = (center - pos).normalized;
            Vector3 lookUp = Quaternion.AngleAxis(roll, fwd) * up;       // roll rotates the picture in-plane
            return new FiresCore.UI.MiniGameCamera.Pose(pos, Quaternion.LookRotation(fwd, lookUp), fov);
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class ZNet_Shutdown_Guard
        {
            private static void Postfix() { try { Active?.EndSession(); } catch { } }
        }
    }
}
