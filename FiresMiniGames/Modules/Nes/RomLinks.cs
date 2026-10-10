using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Links each installed ROM (and the built-in adventure) to a game-BOX prefab id. Interacting with that box hands
    /// the player a cartridge stamped to boot this ROM; set the cartridge into a Nintendo to play it on a television.
    /// Rendered in F8 as one text field per ROM under section "Game Cartridge Links" — the value is the box's prefab
    /// id (e.g. <c>piece_fires_box_zelda</c>). A placed box asks <see cref="RomForBox"/> "which ROM am I?"; the cartridge
    /// carries the answer in its own customData thereafter, so the console never needs this map to boot. ROMs present
    /// at startup get a field here; one dropped in later needs a game restart to appear (same as the per-ROM controls).
    /// </summary>
    internal static class RomLinks
    {
        internal const string Section = "Game Cartridge Links";
        /// <summary>The native title shown as a linkable "cartridge" so a box can dispense The Legend of Fire.</summary>
        internal const string BuiltInRom = NesRomControls.BuiltIn;

        private static ConfigFile _cfg;
        // real ROM file name (or the built-in title) -> its config entry, whose value is a box prefab id
        private static readonly Dictionary<string, ConfigEntry<string>> _links =
            new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);

        public static void Bind(ConfigFile cfg)
        {
            _cfg = cfg;
            BindRom(BuiltInRom, "piece_fires_zelda_box");   // sensible default: the gold box plays the built-in
            try
            {
                string dir = NesRomControls.RomDir;
                if (Directory.Exists(dir))
                    foreach (var f in NesRomControls.InstalledRoms(dir))
                        BindRom(Path.GetFileName(f), "");
            }
            catch { }
        }

        private static void BindRom(string rom, string defaultPrefab)
        {
            if (_cfg == null || string.IsNullOrEmpty(rom) || _links.ContainsKey(rom)) return;
            var e = _cfg.Bind(Section, Sanitize(rom), defaultPrefab,
                $"Box prefab id that dispenses the cartridge for '{rom}'. Interacting with that box hands you a cartridge; " +
                "set it into a Nintendo to play. e.g. piece_fires_box_zelda. Leave blank to leave this game unlinked.");
            _links[rom] = e;
        }

        /// <summary>Every linked ROM name (built-in first as it's bound first), for a config summary / listing.</summary>
        public static IEnumerable<string> Roms => _links.Keys;

        /// <summary>The box prefab id linked to a ROM (empty if none / unlinked).</summary>
        public static string PrefabForRom(string rom) =>
            rom != null && _links.TryGetValue(rom, out var e) ? (e.Value ?? "").Trim() : "";

        /// <summary>Reverse: the ROM whose linked box == this prefab name (null if none). First match wins.</summary>
        public static string RomForBox(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            foreach (var kv in _links)
                if (string.Equals((kv.Value.Value ?? "").Trim(), prefabName, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            return null;
        }

        // Config-key safe: keep letters, digits, spaces, - _ ( ); fold everything else (incl. the '.' before an
        // extension, which TOML would read as key nesting) to '_'. The real name is preserved in the dictionary and
        // spelled out in the field's description — this only shapes the visible key.
        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '(' || c == ')' ? c : '_');
            return sb.ToString();
        }
    }
}
