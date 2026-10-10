using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FiresMiniGames.Modules.Common;
using FiresMiniGames.Modules.DoomTv;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// A placed NES console. It has no screen of its own — like the real thing it plays on a television. A game
    /// cartridge (see <see cref="Cartridge"/>) can be set into it: hold the cartridge on your hotbar and use it on the
    /// console, and it slots in (stored in the console's ZDO, so it stays put across reloads and every client sees it)
    /// and boots on the nearest free TV. Interact with a loaded console to Play again or Eject the cartridge; interact
    /// empty-handed for the built-in adventure or to pick a ROM straight from the config folder. The TV owns the camera
    /// lock, the screen and teardown exactly as it does for its own DOOM.
    /// </summary>
    public sealed class NesConsoleGame : MonoBehaviour, Interactable, Hoverable
    {
        private const float TvRange = 14f;
        private const string ZRom = "fmg_c_rom", ZGame = "fmg_c_game", ZBox = "fmg_c_box";
        private const string Controls =
            "Move = WASD, A = Left-click, B = Right-click, Select = Shift, Start = Enter — rebind any of these in F8 → Controls. Esc steps back.";

        private ZNetView _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Machines.MachinePieces.RebindBundleShaders(gameObject);
            if (_nview == null || !_nview.IsValid()) enabled = false;
        }

        // ── loaded-cartridge state (lives in the console's ZDO) ────────────────────────────────────────────────
        private bool HasCart => _nview != null && _nview.IsValid() && !string.IsNullOrEmpty(_nview.GetZDO().GetString(ZGame));
        private string CartRom => _nview.GetZDO().GetString(ZRom);
        private string CartGame => _nview.GetZDO().GetString(ZGame);
        private string CartBox => _nview.GetZDO().GetString(ZBox);

        private void SetCart(string rom, string game, string box)
        {
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            var zdo = _nview.GetZDO();
            zdo.Set(ZRom, rom ?? ""); zdo.Set(ZGame, game ?? ""); zdo.Set(ZBox, box ?? "");
        }

        private void ClearCart()
        {
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            var zdo = _nview.GetZDO();
            zdo.Set(ZRom, ""); zdo.Set(ZGame, ""); zdo.Set(ZBox, "");
        }

        // ── insert: use a held cartridge on the console ────────────────────────────────────────────────────────
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            if (!Cartridge.IsCartridge(item)) return false;   // not a cartridge → let vanilla report it

            if (DoomTvGame.Active != null) { Center("A game is already running."); return true; }
            if (HasCart) { Center($"Eject the {CartGame} cartridge first."); return true; }

            InsertCart(player, item, FindNearestFreeTv());
            return true;
        }

        // Slot a cartridge in from the player's inventory (from a hotbar-use or the interact menu) and boot it if a
        // free television is in reach; otherwise it sits loaded until powered on beside one.
        private void InsertCart(Player player, ItemDrop.ItemData item, DoomTvGame tv)
        {
            if (player == null || item == null || HasCart) return;
            string rom = Cartridge.Get(item, Cartridge.KeyRom);
            string game = Cartridge.Get(item, Cartridge.KeyGame);
            string box = Cartridge.Get(item, Cartridge.KeyBox);
            player.GetInventory().RemoveItem(item, 1);
            SetCart(rom, game, box);
            if (tv != null) BootInserted(tv);
            else Center($"Set {NameOf(game)} into the Nintendo — power on beside a television.");
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            if (DoomTvGame.Active != null) return false;

            var tv = FindNearestFreeTv();

            if (HasCart)
            {
                string game = NameOf(CartGame);
                MiniGamePopup.Show("Nintendo", $"{game} is loaded. " + Controls,
                    ("Play " + game, tv != null ? "Boot it on the television." : "No free television in reach — it will play once one is near.",
                        () => { var t = FindNearestFreeTv(); if (t != null) BootInserted(t); else Center("Plug into a television — place one within reach."); }),
                    ("Eject " + game, "Take the cartridge back out.", () => EjectTo(player)));
                return true;
            }

            var opts = new List<(string, string, Action)>();

            // Cartridges the player is carrying — set one in (works even with no TV; it will play once one is near).
            foreach (var held in player.GetInventory().GetAllItems())
            {
                if (!Cartridge.IsCartridge(held)) continue;
                var cap = held;
                string g = NameOf(Cartridge.Get(held, Cartridge.KeyGame));
                opts.Add(("Set in " + g, "Slot this cartridge from your pack into the console.",
                    () => InsertCart(player, cap, FindNearestFreeTv())));
            }

            opts.Add(("The Legend of Fire", tv != null ? "The built-in adventure." : "Needs a television nearby.",
                () => { var t = FindNearestFreeTv(); if (t != null) BootZelda(t); else Center("Plug into a television — place one within reach."); }));

            var roms = NesRom.ListRoms();   // every cartridge dropped in the config folder (power-user path)
            if (roms.Length > 0)
                opts.Add(("Cartridge…", $"Pick from {roms.Length} game{(roms.Length == 1 ? "" : "s")} in your config folder.",
                    () => { var t = FindNearestFreeTv(); if (t != null) ShowRomChooser(t, roms); else Center("Plug into a television — place one within reach."); }));

            MiniGamePopup.Show("Nintendo", "Set a cartridge in, or choose a game. " + Controls, opts.ToArray());
            return true;
        }

        private void EjectTo(Player player)
        {
            if (!HasCart || player == null) return;
            var inv = player.GetInventory();
            var it = inv.AddItem(Cartridge.ItemName, 1, 1, 0, player.GetPlayerID(), player.GetPlayerName(), false);
            if (it == null) { Center("Your inventory is full — free a slot to eject."); return; }
            Cartridge.Stamp(it, CartRom, CartGame, CartBox);
            string game = NameOf(CartGame);
            ClearCart();
            Center($"Ejected {game}.");
        }

        // Boot whatever cartridge is currently loaded in the console.
        private void BootInserted(DoomTvGame tv)
        {
            if (tv == null) return;
            string rom = CartRom;
            if (string.IsNullOrEmpty(rom))
            {
                Center("This cartridge has no game linked — set a ROM for its box in F8 → Game Cartridge Links.");
                return;
            }
            if (rom == RomLinks.BuiltInRom) { BootZelda(tv); return; }

            string path = NesRom.FindByName(rom);
            if (path == null)
            {
                Center($"ROM not found: {rom}. Drop it in BepInEx/config/FiresMiniGames to play this cartridge.");
                return;
            }
            byte[] data = NesRom.ReadRom(path, out string err);
            if (data == null) { Center("Bad cartridge: " + err); return; }
            BootRom(tv, data, NesRom.SavePathFor(path), Path.GetFileName(path));
        }

        private void BootZelda(DoomTvGame tv)
        {
            var player = Player.m_localPlayer;
            if (tv == null || player == null || DoomTvGame.Active != null) return;
            NesRomControls.Active = NesRomControls.GetOrBind(NesRomControls.BuiltIn);   // the title's own keymap
            var rt = new Zelda.ZeldaConsole();
            if (!tv.TryStartExternal(rt, player.GetPlayerName()))
            {
                rt.Dispose();
                Center("That television is busy.");
            }
        }

        private void BootRom(DoomTvGame tv, byte[] rom, string savePath, string romName)
        {
            var player = Player.m_localPlayer;
            if (tv == null || player == null || rom == null || DoomTvGame.Active != null) return;
            NesRomControls.Active = NesRomControls.GetOrBind(romName);   // this cartridge's own keymap

            bool gba = romName.EndsWith(".gba", StringComparison.OrdinalIgnoreCase);
            bool gb = romName.EndsWith(".gb", StringComparison.OrdinalIgnoreCase) ||
                      romName.EndsWith(".gbc", StringComparison.OrdinalIgnoreCase);

            IScreenConsole rt;
            try
            {
                rt = gba ? (IScreenConsole)new GBA.GbaRuntime(rom, savePath)
                   : gb  ? new GameBoy.GbRuntime(rom, savePath)
                         : new NesRuntime(rom, savePath);
            }
            catch (Exception e)
            {
                Center("Bad cartridge: " + e.Message);
                return;
            }
            if (!rt.Available)
            {
                string e = (rt as NesRuntime)?.Error ?? (rt as GameBoy.GbRuntime)?.Error ?? (rt as GBA.GbaRuntime)?.Error ?? "unsupported";
                rt.Dispose();
                Center("Bad cartridge: " + e);
                return;
            }
            if (!tv.TryStartExternal(rt, player.GetPlayerName()))
            {
                rt.Dispose();
                Center("That television is busy.");
            }
        }

        // Second popup: the installed cartridges as a pick-list, so multiple ROMs can coexist (each with its own
        // "Controls - <rom>" keybinds). The chosen ROM is read on demand and handed to the TV with its per-ROM map.
        private void ShowRomChooser(DoomTvGame tv, string[] roms)
        {
            if (tv == null || roms == null || roms.Length == 0) return;
            var opts = new List<(string, string, Action)>();
            foreach (var path in roms)
            {
                string p = path;   // capture per-iteration for the closure
                string file = Path.GetFileName(p);
                string ext = Path.GetExtension(p).TrimStart('.').ToUpperInvariant();
                opts.Add((Path.GetFileNameWithoutExtension(p), ext + " cartridge", () =>
                {
                    byte[] data = NesRom.ReadRom(p, out string err);
                    if (data == null) { Center("Bad cartridge: " + err); return; }
                    BootRom(tv, data, NesRom.SavePathFor(p), file);
                }));
            }
            MiniGamePopup.Show("Choose a Cartridge", "Games in BepInEx/config/FiresMiniGames — each keeps its own controls.", opts.ToArray());
        }

        private DoomTvGame FindNearestFreeTv()
        {
            DoomTvGame best = null;
            float bestSq = TvRange * TvRange;
            Vector3 me = transform.position;
            foreach (var tv in UnityEngine.Object.FindObjectsOfType<DoomTvGame>())
            {
                if (tv == null || !tv.IsFreeForExternal()) continue;
                float d = (tv.transform.position - me).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = tv; }
            }
            return best;
        }

        private static string NameOf(string game) => string.IsNullOrEmpty(game) ? "the cartridge" : game;
        private static void Center(string msg) =>
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => "Nintendo Entertainment System";

        public string GetHoverText()
        {
            string line = HasCart
                ? $"Nintendo Entertainment System\n<color=silver>{NameOf(CartGame)} loaded</color>\n[<color=yellow><b>$KEY_Use</b></color>] Play / Eject"
                : "Nintendo Entertainment System\n[<color=yellow><b>$KEY_Use</b></color>] Power on (needs a TV nearby), or set a cartridge in";
            return Localization.instance.Localize(line);
        }
    }
}
