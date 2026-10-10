using System.Collections.Generic;
using System.Reflection;
using FiresCore.Net;
using HarmonyLib;
using UnityEngine;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// The physical game cartridge — a real <see cref="ItemDrop"/> item a player can hold, drop, trade and set into a
    /// Nintendo, or slot into a game box (which behaves like a vanilla item stand). What game a cartridge is lives
    /// entirely in its <c>m_customData</c> (durable across save/load and network transfer): which ROM the console
    /// boots, a display name, and which box it came out of (drives its sticker art + a home to return to). Item
    /// prefabs are minted at runtime from display-cart PIECES (reusing their baked mesh + hammer-icon sprite) with
    /// shared data copied off a vanilla material item so no field is ever null. There are two skins: the generic grey
    /// cart (gets a per-game sticker) and the gold Legend-of-Fire cart.
    /// </summary>
    internal static class Cartridge
    {
        public const string ItemGeneric = "fires_cartridge";
        public const string ItemZelda = "fires_cartridge_zelda";
        public const string ItemName = ItemGeneric;   // back-compat alias

        // customData keys — a cartridge is recognised by the presence of KeyBox or a known item-prefab name.
        public const string KeyRom = "fmg_rom";     // ROM file name the console boots (or the built-in title)
        public const string KeyGame = "fmg_game";   // human display name, e.g. "The Legend of Zelda"
        public const string KeyBox = "fmg_box";     // prefab name of the box it came from (sticker + return home)

        private static readonly HashSet<string> _itemNames = new HashSet<string>();
        private static bool _built;

        // Boxes whose cartridge is the gold one (the real NES Legend of Zelda cart was gold, and the custom gold
        // model is the Legend of Fire cart) — both the pack's Zelda box and the Fire box hand out gold.
        private static readonly System.Collections.Generic.HashSet<string> _goldBoxes =
            new System.Collections.Generic.HashSet<string> { "piece_fires_box_zelda", "piece_fires_zelda_box" };

        /// <summary>Which cartridge item a given box hands out (gold cart for the Zelda / Legend-of-Fire boxes, grey otherwise).</summary>
        public static string ItemForBox(string boxPrefab) =>
            _goldBoxes.Contains(boxPrefab) ? ItemZelda : ItemGeneric;

        /// <summary>Build + queue the cartridge item prefabs. Safe to call repeatedly; needs pieces loaded and the
        /// in-game ObjectDB (for the shared-data base). Registers into <see cref="FiresMiniGames.itemPrefabs"/>.</summary>
        public static void EnsureTemplate(ObjectDB odb)
        {
            if (_built || odb == null) return;
            _built = true;

            // Generic grey cart — takes a runtime per-game sticker (see CartridgeSkin).
            BuildItem(odb, ItemGeneric, "Game Cartridge",
                new[] { "piece_fires_cart_mario", "piece_fires_nes_cartridge", "piece_fires_zelda_cartridge" }, sticker: true);
            // Gold Legend-of-Fire cart — its gold art is baked, no sticker.
            BuildItem(odb, ItemZelda, "The Legend of Fire — Cartridge",
                new[] { "piece_fires_zelda_cartridge", "piece_fires_cart_mario" }, sticker: false);
        }

        private static void BuildItem(ObjectDB odb, string itemName, string displayName, string[] srcPieces, bool sticker)
        {
            GameObject src = null;
            foreach (var n in srcPieces) { src = FindPiece(n); if (src != null) break; }
            if (src == null)
            {
                Debug.LogWarning($"{FiresMiniGames.PluginName}: no display-cart piece for '{itemName}' — item unavailable.");
                return;
            }
            Sprite icon = src.GetComponent<Piece>()?.m_icon;

            // Base shared data on a known-good vanilla material item so every field the game touches is populated.
            var baseItem = odb.GetItemPrefab("Wood") ?? odb.GetItemPrefab("Coins") ?? odb.GetItemPrefab("Stone");
            var baseShared = baseItem != null ? baseItem.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            var shared = CloneShared(baseShared);
            shared.m_name = displayName;
            shared.m_description = "A game cartridge. Set it into a Nintendo Entertainment System to play it on the television, or slot it into a game box on the shelf.";
            shared.m_icons = icon != null ? new[] { icon }
                           : (shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons : new Sprite[] { null });
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_maxStackSize = 1;
            shared.m_weight = 0.4f;
            shared.m_maxQuality = 1;
            shared.m_variants = 1;
            shared.m_value = 0;
            shared.m_questItem = false;
            shared.m_teleportable = true;

            var go = NetworkObjectHelper.InstantiateInactiveClone(src, itemName);
            if (go == null) return;
            Object.DontDestroyOnLoad(go);

            var piece = go.GetComponent<Piece>();
            if (piece != null) Object.DestroyImmediate(piece);

            var nview = go.GetComponent<ZNetView>() ?? go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_syncInitialScale = false;

            int itemLayer = LayerMask.NameToLayer("item");
            if (itemLayer >= 0) go.layer = itemLayer;
            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
            if (go.GetComponent<Rigidbody>() == null)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.useGravity = true;
            }

            var drop = go.GetComponent<ItemDrop>() ?? go.AddComponent<ItemDrop>();
            drop.m_autoPickup = true;
            drop.m_itemData = new ItemDrop.ItemData
            {
                m_shared = shared,
                m_stack = 1,
                m_quality = 1,
                m_durability = 100f,
                m_variant = 0,
                m_dropPrefab = go
            };

            if (sticker && go.GetComponent<CartridgeSkin>() == null) go.AddComponent<CartridgeSkin>();

            _itemNames.Add(itemName);
            if (!FiresMiniGames.itemPrefabs.Contains(go)) FiresMiniGames.itemPrefabs.Add(go);
        }

        /// <summary>Stamp a freshly-created cartridge with the game it represents (call right after AddItem).</summary>
        public static void Stamp(ItemDrop.ItemData item, string rom, string game, string boxPrefab)
        {
            if (item == null) return;
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            item.m_customData[KeyRom] = rom ?? "";
            item.m_customData[KeyGame] = game ?? "";
            item.m_customData[KeyBox] = boxPrefab ?? "";
        }

        public static bool IsCartridge(ItemDrop.ItemData item)
        {
            if (item == null) return false;
            if (item.m_dropPrefab != null && _itemNames.Contains(global::Utils.GetPrefabName(item.m_dropPrefab))) return true;
            return item.m_customData != null && item.m_customData.ContainsKey(KeyBox);
        }

        /// <summary>The item prefab name backing this cartridge (grey vs gold), for re-creating it on the way out of a box.</summary>
        public static string ItemNameOf(ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab != null)
            {
                string n = global::Utils.GetPrefabName(item.m_dropPrefab);
                if (_itemNames.Contains(n)) return n;
            }
            return ItemGeneric;
        }

        public static string Get(ItemDrop.ItemData item, string key) =>
            item?.m_customData != null && item.m_customData.TryGetValue(key, out var v) ? v : "";

        private static GameObject FindPiece(string name)
        {
            foreach (var p in FiresMiniGames.piecePrefabs)
                if (p != null && p.name == name) return p;
            return null;
        }

        // Shallow field-copy of a vanilla item's SharedData into a fresh instance, so we inherit every populated
        // field (m_attack, m_dlc, m_subtitle, damage-mod lists, …) and then override only the display fields.
        private static ItemDrop.ItemData.SharedData CloneShared(ItemDrop.ItemData.SharedData src)
        {
            var dst = new ItemDrop.ItemData.SharedData();
            if (src == null) return dst;
            foreach (var f in typeof(ItemDrop.ItemData.SharedData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { f.SetValue(dst, f.GetValue(src)); } catch { }
            }
            return dst;
        }

        // Show the stamped game on the cartridge's tooltip (the shared name stays generic, so per-cart identity rides
        // in customData and survives save/load without cloning shared data per instance).
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class Tooltip_Patch
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (!IsCartridge(item)) return;
                string game = Get(item, KeyGame);
                string rom = Get(item, KeyRom);
                var sb = new System.Text.StringBuilder(__result);
                if (!string.IsNullOrEmpty(game))
                    sb.Append($"\n\nGame: <color=orange>{game}</color>");
                else
                    sb.Append("\n\n<color=orange>Unlinked cartridge</color> — link a ROM to this game's box in F8 → Game Cartridge Links.");
                if (!string.IsNullOrEmpty(rom) && rom != RomLinks.BuiltInRom)
                    sb.Append($"\n<color=silver>ROM: {rom}</color>");
                __result = sb.ToString();
            }
        }
    }
}
