using System.IO;
using System.Reflection;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Finds the NES ROM in BepInEx/config/FiresMiniGames. Prefers the filename set in config (default "game.nes"); if that
    /// isn't there it falls back to the first *.nes present, so dropping any cartridge dump in the folder just
    /// works. No copyrighted ROM ships with the mod — the folder is the player's to fill.
    /// </summary>
    internal static class NesRom
    {
        // ROMs live in the config folder (BepInEx/config/FiresMiniGames), not the plugin folder — created on demand
        // so there's always a place to drop a .nes.
        private static string Dir
        {
            get
            {
                string d = Path.Combine(BepInEx.Paths.ConfigPath, "FiresMiniGames");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static byte[] Load(out string error) => Load(out error, out _);

        public static byte[] Load(out string error, out string romPath)
        {
            error = null;
            romPath = null;
            try
            {
                string dir = Dir;
                string configured = MiniGamesConfig.NesRomFile != null ? MiniGamesConfig.NesRomFile.Value : "game.nes";

                if (!string.IsNullOrEmpty(configured))
                {
                    string p = Path.Combine(dir, configured);
                    if (File.Exists(p)) { romPath = p; return File.ReadAllBytes(p); }
                }

                var found = Directory.GetFiles(dir, "*.nes");           // prefer NES, then Game Boy / Color / Advance
                if (found.Length == 0) found = Directory.GetFiles(dir, "*.gb");
                if (found.Length == 0) found = Directory.GetFiles(dir, "*.gbc");
                if (found.Length == 0) found = Directory.GetFiles(dir, "*.gba");
                if (found.Length > 0) { romPath = found[0]; return File.ReadAllBytes(found[0]); }

                error = $"No ROM found. Put a .nes / .gb / .gbc / .gba file (e.g. {configured}) in BepInEx/config/FiresMiniGames.";
                return null;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Every installed cartridge in the config folder (all supported extensions), sorted by name, so the
        /// console can offer a pick-a-game list rather than auto-loading just the first one.</summary>
        public static string[] ListRoms()
        {
            try { return NesRomControls.InstalledRoms(Dir); }
            catch { return System.Array.Empty<string>(); }
        }

        /// <summary>Read one specific cartridge's bytes (null + error on failure).</summary>
        public static byte[] ReadRom(string path, out string error)
        {
            error = null;
            try { return File.ReadAllBytes(path); }
            catch (System.Exception e) { error = e.Message; return null; }
        }

        /// <summary>Resolve a bare ROM file name (as stamped on a physical cartridge) to its full path in the config
        /// folder — null if it isn't installed on this machine (each player supplies their own dumps).</summary>
        public static string FindByName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            try
            {
                string p = Path.Combine(Dir, fileName);
                if (File.Exists(p)) return p;
                foreach (var f in ListRoms())
                    if (string.Equals(Path.GetFileName(f), fileName, System.StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { }
            return null;
        }

        /// <summary>Where a cartridge's battery save lives — same folder and base name as the ROM, .sav extension.</summary>
        public static string SavePathFor(string romPath) =>
            string.IsNullOrEmpty(romPath) ? null : Path.ChangeExtension(romPath, ".sav");
    }
}
