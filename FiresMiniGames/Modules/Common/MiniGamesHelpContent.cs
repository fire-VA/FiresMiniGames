using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    // FiresMiniGames' sections for the shared FiresCore help panel (registration only).
    internal static class MiniGamesHelpContent
    {
        private const string ModId = "FiresMiniGames";
        private static bool _registered;

        public static void Register()
        {
            if (_registered || Application.isBatchMode) return;
            _registered = true;

            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "The Game Room", 0, BuildOverview);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Cartridges & Consoles", 1, BuildCartridges);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Controls", 2, BuildControls);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "ROMs, Saves & Config", 3, BuildConfig, adminOnly: true);
        }

        private static void BuildOverview(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("The Game Room");
            w.Paragraph("FiresMiniGames adds playable machines you build with the hammer (Furniture " +
                "category, free to place). Walk up, press E, and play.");
            w.Divider();

            w.SubHeader("The Machines");
            w.Bullet("Roulette Wheel — place chip bets and spin. Chip sizes and the table cap are " +
                "server-configurable; pays in the configured currency.");
            w.Bullet("Slot Machine — pick a bet and pull the arm.");
            w.Bullet("Arcade Cabinet (Pac-Man) — coin-op: insert coins for credits, 3 lives, and the " +
                "machine remembers its own high score with the scorer's name on the hover text.");
            w.Bullet("Air Hockey Table — first to 7, with a live scoreboard.");
            w.Bullet("Televisions (Soviet, Retro, PVM, Wall, Monitor) — every TV runs DOOM. Bring a " +
                "freedoom1.wad/doom1.wad (none ships).");
            w.Bullet("NES Console — hooks up to a nearby free TV and plays NES, Game Boy (.gb/.gbc), " +
                "and Game Boy Advance (.gba) ROMs, plus the built-in 'The Legend of Fire' adventure.");
            w.Divider();

            w.SubHeader("Etiquette");
            w.Bullet("One player per machine — an occupied machine shows who's playing.");
            w.Bullet("Sessions end on Esc, walking away, or death.");
            w.Bullet("Also included: game-room furniture (benches, sofas — sittable) and shelf-decor " +
                "boxed games and display cartridges.");
        }

        private static void BuildCartridges(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Cartridges & Consoles");
            w.Paragraph("Games are PHYSICAL cartridges you can hold, trade, shelve, and lose.");
            w.Divider();

            w.SubHeader("Getting a Cartridge");
            w.Bullet("Build (or find) a boxed game — piece_fires_box_* pieces (Zelda, Contra, " +
                "Castlevania, Tetris, Mario 3, TMNT...).");
            w.Bullet("Interact with the box to take the cartridge out — the box visibly empties.");
            w.Bullet("Use a cartridge on an empty box to put it back.");
            w.Divider();

            w.SubHeader("Playing It");
            w.Bullet("Hold the cartridge and use it on an NES console, or pick 'Set in <game>' from " +
                "the console's menu.");
            w.Bullet("The console menu's 'Cartridge…' option can also load any ROM file directly from " +
                "the config folder.");
            w.Bullet("A cartridge remembers which game it is in its item data — it survives save/load, " +
                "chests, and trading, and its tooltip names the game.");
        }

        private static void BuildControls(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Controls");
            w.SubHeader("Console Games (NES / GB / GBA / Legend of Fire)");
            w.CodeBlock("Move    W A S D\n" +
                "A       Left mouse button\n" +
                "B       Right mouse button\n" +
                "Select  Left Shift\n" +
                "Start   Enter");
            w.Paragraph("Every installed game gets its OWN rebindable keymap — F8 → FiresMiniGames → " +
                "'Controls - <game>', click a key to record a new one (mouse buttons bind too).");
            w.Divider();

            w.SubHeader("DOOM (TVs)");
            w.Bullet("WASD or Arrows — move; Ctrl or LMB — fire; Alt — strafe; Space — use/open doors");
            w.Bullet("Mouse turns; sensitivity under [Television] DOOM Mouse Sensitivity (0 disables)");
            w.Divider();

            w.SubHeader("Everything Else");
            w.Bullet("Arcade / Air Hockey — WASD or Arrows; Esc walks away");
            w.Bullet("Esc — end any session");
            w.Divider();

            w.SubHeader("Screen Camera");
            w.Paragraph("Hold Ctrl+Alt while playing to reframe the screen camera — drag pans, mouse " +
                "wheel zooms; the game freezes while you tune. Saved per screen type. Fine-grained " +
                "offsets also live under [Television] Screen Camera * in F8.");
        }

        private static void BuildConfig(FiresCore.Help.HelpContentWriter w)
        {
            w.AdminHeader("ROMs, Saves & Config");
            w.SubHeader("ROM Files");
            w.Bullet("Drop ROMs into BepInEx/config/FiresMiniGames — .nes, .gb, .gbc, .gba.");
            w.Bullet("No ROMs or WADs ship with the mod; provide your own legally.");
            w.Bullet("[Game Cartridge Links] maps each ROM to a game-box piece so its cartridge " +
                "dispenses from that box.");
            w.Bullet("Saves: NES writes a .sav next to the ROM (real battery-backed SRAM); GB/GBA " +
                "keep their own save files. DOOM saves too.");
            w.AdminDivider();

            w.SubHeader("Economy");
            w.Bullet("[General] Currency Item — the wager/payout item for every machine (default Coins)");
            w.Bullet("[Slot Machine] Bet Sizes — comma list (default 2,10,50)");
            w.Bullet("[Roulette] Chip Sizes / Max Total Bet");
            w.Bullet("[Arcade] Play Cost — coins per credit (0 = free play)");
            w.AdminDivider();

            w.SubHeader("Screens");
            w.Bullet("[Screens] Screen Brightness / Warmth / Hue — how every TV and arcade screen " +
                "renders; live in F8");
            w.Bullet("[Television] DOOM Skill — 0 (ITYTD) to 4 (Nightmare)");
        }
    }
}
