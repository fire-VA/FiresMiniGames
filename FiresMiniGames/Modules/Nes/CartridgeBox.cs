using UnityEngine;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// A boxed game on the shelf, worked like a vanilla item stand — it holds at most one cartridge. Interact to take
    /// the cartridge inside; use a cartridge on it (hotbar-use while looking, or the take-then-place flow) to slot one
    /// in. It accepts ANY cartridge, so a cart put in the wrong box stays there until someone takes it back out — find
    /// the box holding the one you want. A freshly-placed box starts holding its own game (the ROM linked to its
    /// prefab id in F8, gold cart for the Legend-of-Fire box). State lives in the box's ZDO so every client agrees and
    /// it survives reload; the held cartridge is shown sitting on the box.
    /// </summary>
    public sealed class CartridgeBox : MonoBehaviour, Interactable, Hoverable
    {
        // ZDO: state 0 = untouched (implicitly holds its linked game), 1 = empty, 2 = holds the stored cart.
        private const string ZState = "fmg_state";
        private const string ZRom = "fmg_c_rom", ZGame = "fmg_c_game", ZBox = "fmg_c_box", ZItem = "fmg_c_item";

        private ZNetView _nview;
        private Piece _piece;
        private GameObject _display;
        private int _shownState = int.MinValue;
        private string _shownKey = "";
        private float _topLocalY = float.NaN;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            Machines.MachinePieces.RebindBundleShaders(gameObject);
            if (_nview == null || !_nview.IsValid()) { enabled = false; return; }
            RefreshDisplay();
        }

        private void Update()
        {
            // Cheap poll so a take/place by another player updates this box's shown cartridge within a second.
            if (_nview != null && _nview.IsValid() && (State != _shownState || HeldKey() != _shownKey)) RefreshDisplay();
        }

        private string BoxPrefab => global::Utils.GetPrefabName(gameObject);
        private int State => _nview.GetZDO().GetInt(ZState, 0);
        private bool HasCart => State != 1;   // 0 (linked default) or 2 (stored) both hold a cartridge

        // Resolve the currently-held cartridge's data (defaults derived from the box when it's untouched).
        private void Held(out string rom, out string game, out string origin, out string item)
        {
            if (State == 2)
            {
                var z = _nview.GetZDO();
                rom = z.GetString(ZRom); game = z.GetString(ZGame); origin = z.GetString(ZBox); item = z.GetString(ZItem);
                if (string.IsNullOrEmpty(item)) item = Cartridge.ItemGeneric;
                return;
            }
            // untouched → its own linked game
            origin = BoxPrefab;
            rom = RomLinks.RomForBox(origin) ?? "";
            game = GameName();
            item = Cartridge.ItemForBox(origin);
        }

        // Cheap identity of what's shown (polled every frame) — avoids the full ROM-link resolution except on refresh.
        private string HeldKey()
        {
            int s = State;
            if (s == 1) return "empty";
            if (s == 2) { var z = _nview.GetZDO(); return "2|" + z.GetString(ZItem) + "|" + z.GetString(ZGame); }
            return "0";   // untouched — constant per box
        }

        // Box labels come two ways: "Game Box — The Legend of Zelda" and "The Legend of Fire — Box".
        private string GameName()
        {
            string n = _piece != null ? _piece.m_name : "";
            if (string.IsNullOrEmpty(n)) return "this game";
            char[] trim = { '—', '-', ' ' };
            int dash = n.IndexOf('—');
            if (dash < 0) dash = n.IndexOf('-');
            if (dash < 0) return n;
            string before = n.Substring(0, dash).Trim(trim);
            string after = n.Substring(dash + 1).Trim(trim);
            if (before.StartsWith("Game Box", System.StringComparison.OrdinalIgnoreCase)) return after;
            if (after.Equals("Box", System.StringComparison.OrdinalIgnoreCase)) return before;
            return after.Length > 0 ? after : before;
        }

        private void ClaimZdo()
        {
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;

            if (!HasCart) { Center("The box is empty — its cartridge is out there somewhere."); return true; }

            Held(out string rom, out string game, out string origin, out string item);
            var inv = player.GetInventory();
            var it = inv.AddItem(item, 1, 1, 0, player.GetPlayerID(), player.GetPlayerName(), false);
            if (it == null) { Center("Your inventory is full."); return true; }
            Cartridge.Stamp(it, rom, game, origin);

            ClaimZdo();
            _nview.GetZDO().Set(ZState, 1);   // now empty
            RefreshDisplay();
            Center($"Took the {game} cartridge.");
            return true;
        }

        // Slot a cartridge in by using it on the box (accepts ANY cartridge — that's the whole point).
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            if (_nview == null || !_nview.IsValid()) return false;
            if (!Cartridge.IsCartridge(item)) return false;   // not a cartridge → let vanilla report it

            if (HasCart) { Center("Take the cartridge inside first."); return true; }

            string game = Cartridge.Get(item, Cartridge.KeyGame);
            ClaimZdo();
            var z = _nview.GetZDO();
            z.Set(ZRom, Cartridge.Get(item, Cartridge.KeyRom));
            z.Set(ZGame, game);
            z.Set(ZBox, Cartridge.Get(item, Cartridge.KeyBox));
            z.Set(ZItem, Cartridge.ItemNameOf(item));
            z.Set(ZState, 2);
            player.GetInventory().RemoveItem(item, 1);
            RefreshDisplay();
            Center($"Put the {NameOf(game)} cartridge in the box.");
            return true;
        }

        // ── show the held cartridge sitting on the box ─────────────────────────────────────────────────────────
        private void RefreshDisplay()
        {
            if (float.IsNaN(_topLocalY)) _topLocalY = BoxTopLocalY();   // measure the box once, before any display child exists
            if (_display != null) { Destroy(_display); _display = null; }
            _shownState = State;
            _shownKey = HeldKey();
            if (!HasCart) return;

            Held(out _, out _, out string origin, out string item);
            GameObject model = SourceModelFor(item);
            if (model == null) return;

            float topLocalY = _topLocalY;

            // Inactive clone → no ZNetView/Piece Awake fires; strip to pure scenery, then reparent + activate.
            var clone = FiresCore.Net.NetworkObjectHelper.InstantiateInactiveClone(model);
            if (clone == null) return;
            StripToVisual(clone);
            clone.transform.SetParent(transform, false);
            clone.transform.localPosition = new Vector3(0f, topLocalY, 0f);
            clone.transform.localRotation = Quaternion.identity;
            var skin = clone.GetComponent<CartridgeSkin>() ?? clone.AddComponent<CartridgeSkin>();
            clone.SetActive(true);
            skin.Apply(origin);
            _display = clone;
        }

        private static GameObject SourceModelFor(string itemName)
        {
            // Match the item's shell to the display piece it was minted from.
            string piece = itemName == Cartridge.ItemZelda ? "piece_fires_zelda_cartridge" : "piece_fires_cart_mario";
            foreach (var p in FiresMiniGames.piecePrefabs)
                if (p != null && p.name == piece) return p;
            foreach (var p in FiresMiniGames.piecePrefabs)
                if (p != null && p.name == "piece_fires_nes_cartridge") return p;
            return null;
        }

        // Turn a cloned display piece into inert scenery. Destroy Piece BEFORE ZNetView (Piece requires it).
        private static void StripToVisual(GameObject go)
        {
            var piece = go.GetComponent<Piece>(); if (piece != null) DestroyImmediate(piece);
            var nv = go.GetComponent<ZNetView>(); if (nv != null) DestroyImmediate(nv);
            var rb = go.GetComponent<Rigidbody>(); if (rb != null) DestroyImmediate(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
        }

        // Height (in the box's local space) of the box's top surface, so the cart sits on the cover.
        private float BoxTopLocalY()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return 0.15f;
            var wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
            float scaleY = Mathf.Max(0.0001f, transform.lossyScale.y);
            return (wb.max.y - transform.position.y) / scaleY + 0.003f;
        }

        public float GetHoverOffset() => 0f;

        public string GetHoverName() => _piece != null ? _piece.m_name : "Game Box";

        public string GetHoverText()
        {
            string status;
            if (HasCart)
            {
                Held(out _, out string game, out _, out _);
                status = $"<color=silver>Holds: {NameOf(game)}</color>\n[<color=yellow><b>$KEY_Use</b></color>] Take the cartridge";
            }
            else
            {
                status = "<color=silver>Empty</color>\n[<color=yellow><b>$KEY_Use</b></color>] with a cartridge in hand to slot it in";
            }
            return Localization.instance.Localize((_piece != null ? _piece.m_name : "Game Box") + "\n" + status);
        }

        private static string NameOf(string game) => string.IsNullOrEmpty(game) ? "a cartridge" : game;
        private static void Center(string msg) =>
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, msg);
    }
}
