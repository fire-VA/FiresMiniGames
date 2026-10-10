using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Per-ROM controller mapping for the TVs. Every cartridge in BepInEx/config/FiresMiniGames (plus the built-in
    /// The Legend of Fire) gets its OWN "Controls - &lt;rom&gt;" section in the F8 config, so each game binds its eight
    /// buttons however it likes. Entries are typed <see cref="KeyCode"/>, so the F8 panel renders a click-to-record
    /// button (press any key OR mouse button — <c>Mouse0</c>/<c>Mouse1</c> are captured too) instead of a text field.
    /// ROMs present at startup are bound in <see cref="Bind"/>; one dropped in later gets its section the first time
    /// it's booted (<see cref="GetOrBind"/>) and shows in F8 on the next open. The running session points
    /// <see cref="Active"/> at its ROM's map and <see cref="Common.GamepadKeys"/> reads that as the pad.
    /// </summary>
    internal static class NesRomControls
    {
        internal static readonly string[] Buttons = { "A", "B", "Select", "Start", "Up", "Down", "Left", "Right" };

        /// <summary>Default layout: A = left-click, B = right-click, Select = Shift, Start = Enter, d-pad = WASD.
        /// Also the fallback pad when no session map is set. Bump <see cref="SchemaVersion"/> when this changes.</summary>
        internal static readonly KeyCode[] Defaults =
            { KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.LeftShift, KeyCode.Return, KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D };

        internal const string BuiltIn = "The Legend of Fire";

        // Every ROM cartridge extension the console can boot — each installed file gets its own Controls section.
        internal static readonly string[] Extensions = { "*.nes", "*.gb", "*.gbc", "*.gba" };

        // Bumped when Defaults change so existing installs refresh their saved bindings ONCE. Older configs stored the
        // previous X/Z/arrows layout (as strings that parse straight back into KeyCodes), which would otherwise carry
        // over unchanged. Tracked in a hidden marker file so no throwaway entry clutters the config window.
        private const int SchemaVersion = 2;

        private static ConfigFile _cfg;
        private static readonly Dictionary<string, RomKeymap> _maps =
            new Dictionary<string, RomKeymap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The keymap the current TV session reads (null → <see cref="Defaults"/>).</summary>
        public static RomKeymap Active;

        public static void Bind(ConfigFile cfg)
        {
            _cfg = cfg;
            bool migrate = ReadSchema() < SchemaVersion;

            GetOrBind(BuiltIn);   // the native adventure always has a section
            try
            {
                string dir = RomDir;
                if (Directory.Exists(dir))
                    foreach (var f in InstalledRoms(dir))
                        GetOrBind(Path.GetFileName(f));
            }
            catch { }

            if (migrate)
            {
                // One-time: force the new default layout over whatever the old string-based config held.
                foreach (var m in _maps.Values) m.ResetToDefaults();
                WriteSchema(SchemaVersion);
            }
        }

        /// <summary>Every installed cartridge path (all supported extensions), sorted by name.</summary>
        internal static string[] InstalledRoms(string dir)
        {
            var list = new List<string>();
            foreach (var ext in Extensions) list.AddRange(Directory.GetFiles(dir, ext));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }

        internal static string RomDir => Path.Combine(BepInEx.Paths.ConfigPath, "FiresMiniGames");

        /// <summary>Fetch (binding on first use) the keymap for a ROM file name / the built-in title.</summary>
        public static RomKeymap GetOrBind(string romName)
        {
            if (_cfg == null || string.IsNullOrEmpty(romName)) return null;
            if (_maps.TryGetValue(romName, out var m)) return m;

            string section = "Controls - " + Sanitize(romName);
            var entries = new ConfigEntry<KeyCode>[8];
            for (int i = 0; i < 8; i++)
                entries[i] = _cfg.Bind(section, Buttons[i], Defaults[i],
                    $"NES {Buttons[i]} button when playing {romName}. Click the button to record a key OR mouse button (default A = left-click, B = right-click).");
            m = new RomKeymap(entries);
            _maps[romName] = m;
            return m;
        }

        private static string SchemaFile => Path.Combine(RomDir, ".controls_schema");

        private static int ReadSchema()
        {
            try { return File.Exists(SchemaFile) && int.TryParse(File.ReadAllText(SchemaFile).Trim(), out int v) ? v : 0; }
            catch { return 0; }
        }

        private static void WriteSchema(int v)
        {
            try { Directory.CreateDirectory(RomDir); File.WriteAllText(SchemaFile, v.ToString()); }
            catch { }
        }

        // Config section / TOML-table safe: keep letters, digits, spaces, - _ ( ); fold everything else to '_'.
        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '(' || c == ')' ? c : '_');
            return sb.ToString();
        }
    }

    /// <summary>One ROM's eight buttons → keys, read live from its typed config entries (a cfg edit or a recorded
    /// rebind applies on the next frame — no parsing, the entry is already a <see cref="KeyCode"/>).</summary>
    internal sealed class RomKeymap
    {
        private readonly ConfigEntry<KeyCode>[] _e;

        public RomKeymap(ConfigEntry<KeyCode>[] e) { _e = e; }

        public KeyCode Get(int i) =>
            _e != null && i >= 0 && i < _e.Length && _e[i] != null ? _e[i].Value : NesRomControls.Defaults[i];

        public void ResetToDefaults()
        {
            if (_e == null) return;
            for (int i = 0; i < _e.Length; i++)
                if (_e[i] != null) _e[i].Value = NesRomControls.Defaults[i];
        }
    }
}
