using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Machine wager settings. Values are read at use time (popup open / spin start), so a cfg edit or a Core F8
    /// config-UI write applies on the next interaction without a restart.
    /// </summary>
    public static class MiniGamesConfig
    {
        public static ConfigEntry<string> CurrencyItem;
        public static ConfigEntry<string> SlotBets;
        public static ConfigEntry<string> RouletteChips;
        public static ConfigEntry<int> RouletteMaxBet;
        public static ConfigEntry<int> ArcadeCost;
        public static ConfigEntry<float> ScreenBrightness;
        public static ConfigEntry<float> ScreenWarmth;
        public static ConfigEntry<float> ScreenHue;
        public static ConfigEntry<int> DoomSkill;
        public static ConfigEntry<float> DoomMouseSensitivity;
        public static ConfigEntry<string> NesRomFile;
        public static ConfigEntry<float> TvCamFov;
        public static ConfigEntry<float> TvCamBack;
        public static ConfigEntry<float> TvCamSide;
        public static ConfigEntry<float> TvCamHeight;
        public static ConfigEntry<float> TvCamYaw;
        public static ConfigEntry<float> TvCamPitch;
        public static ConfigEntry<float> TvCamRoll;

        /// <summary>Composed emission colour for the game screens (TV + arcade), read live: brightness scales
        /// intensity, warmth tilts the colour temperature (amber↔blue), hue rotates that tint around the wheel.
        /// Neutral at warmth 0 / hue 0, where it is simply white × brightness (matching the plain glow).</summary>
        public static Color ScreenEmission()
        {
            float b = ScreenBrightness != null ? ScreenBrightness.Value : 1f;
            float w = (ScreenWarmth != null ? ScreenWarmth.Value : 0f) / 100f;   // -1 cool .. +1 warm
            float hue = ScreenHue != null ? ScreenHue.Value : 0f;                // degrees to rotate the tint

            // Colour-temperature tint kept at max-channel 1 so warmth only rebalances colour — brightness owns
            // intensity. Warm trims green a little and blue a lot (→ amber); cool trims red a lot and green a little
            // (→ blue). A multiply tint can only subtract, which is exactly a gel over the tube.
            float r = 1f, g = 1f, bl = 1f;
            if (w >= 0f) { g = 1f - 0.2f * w; bl = 1f - 0.6f * w; }
            else { r = 1f + 0.6f * w; g = 1f + 0.2f * w; }

            var tint = new Color(r, g, bl, 1f);
            if (Mathf.Abs(hue) > 0.01f)
            {
                Color.RGBToHSV(tint, out float th, out float ts, out float tv);
                th = Mathf.Repeat(th + hue / 360f, 1f);
                tint = Color.HSVToRGB(th, ts, tv);
            }

            var e = tint * b;
            e.a = 1f;
            return e;
        }

        public static void Bind(ConfigFile cfg)
        {
            CurrencyItem = cfg.Bind("General", "Currency Item", "Coins",
                "Item prefab used for every machine wager and payout.");
            SlotBets = cfg.Bind("Slot Machine", "Bet Sizes", "2,10,50",
                "Comma-separated coin bets offered by the slot machine.");
            RouletteChips = cfg.Bind("Roulette", "Chip Sizes", "1,5,10,25,100",
                "Comma-separated chip values offered at the roulette table.");
            RouletteMaxBet = cfg.Bind("Roulette", "Max Total Bet", 500,
                "Maximum total stake allowed on a single roulette spin.");
            ArcadeCost = cfg.Bind("Arcade", "Play Cost", 2,
                "Coins per arcade credit. 0 makes the cabinet free to play.");
            ScreenBrightness = cfg.Bind("Screens", "Screen Brightness", 2f,
                new ConfigDescription("How brightly the game picture self-illuminates on every TV and arcade screen, so it stays readable in the dark. 1 = flat/dim, 2 = default glow, 4+ = bright. Read live — drag it in the F8 panel while a game is on the screen and it responds at once.",
                    new AcceptableValueRange<float>(0.25f, 8f)));
            ScreenWarmth = cfg.Bind("Screens", "Screen Warmth", 0f,
                new ConfigDescription("Colour temperature of the game picture. 0 = neutral; positive warms it toward amber (cosy CRT), negative cools it toward blue. Read live.",
                    new AcceptableValueRange<float>(-100f, 100f)));
            ScreenHue = cfg.Bind("Screens", "Screen Hue", 0f,
                new ConfigDescription("Rotates the warmth tint around the colour wheel, in degrees, to push the cast to any colour (green, pink, cyan…). Only bites once Warmth is off 0 (a neutral tint has no hue to spin). Read live.",
                    new AcceptableValueRange<float>(0f, 360f)));
            DoomSkill = cfg.Bind("Television", "DOOM Skill", 2,
                new ConfigDescription("DOOM difficulty on the TVs: 0 I'm Too Young to Die, 1 Hey Not Too Rough, 2 Hurt Me Plenty, 3 Ultra-Violence, 4 Nightmare.",
                    new AcceptableValueRange<int>(0, 4)));
            DoomMouseSensitivity = cfg.Bind("Television", "DOOM Mouse Sensitivity", 5f,
                new ConfigDescription("Mouse turn speed for DOOM on the TVs. 0 disables mouse turning (keyboard A/D only).",
                    new AcceptableValueRange<float>(0f, 20f)));
            NesRomFile = cfg.Bind("Television", "NES ROM File", "game.nes",
                "NES ROM filename (in BepInEx/config/FiresMiniGames) the console loads onto a nearby TV. Falls back to the first .nes present. No ROM ships with the mod.");
            // TV screen camera — read live each frame, so you can tune it with the Core F8 config UI while a game is
            // on the screen, then tell me the values to bake in. Applies to DOOM, NES and The Legend of Fire alike.
            TvCamFov = cfg.Bind("Television", "Screen Camera FOV", 52f,
                new ConfigDescription("Field of view when watching a TV screen. Lower = tighter/more zoomed in.",
                    new AcceptableValueRange<float>(20f, 80f)));
            TvCamBack = cfg.Bind("Television", "Screen Camera Back", 0.1f,
                new ConfigDescription("Step the camera back from the screen, in metres. Higher = further away / more zoomed out.",
                    new AcceptableValueRange<float>(0f, 5f)));
            TvCamSide = cfg.Bind("Television", "Screen Camera Side", -0.1f,
                new ConfigDescription("Slide the camera sideways, in metres. Negative = left, positive = right.",
                    new AcceptableValueRange<float>(-2f, 2f)));
            TvCamHeight = cfg.Bind("Television", "Screen Camera Height", 0f,
                new ConfigDescription("Raise or lower the camera, in metres. Positive = up.",
                    new AcceptableValueRange<float>(-2f, 2f)));
            TvCamYaw = cfg.Bind("Television", "Screen Camera Yaw", 0f,
                new ConfigDescription("Orbit the camera around the screen, in degrees, keeping it framed. Negative = view from the left.",
                    new AcceptableValueRange<float>(-30f, 30f)));
            TvCamPitch = cfg.Bind("Television", "Screen Camera Pitch", 0f,
                new ConfigDescription("Orbit the camera up or down around the screen, in degrees. Positive = view from above.",
                    new AcceptableValueRange<float>(-30f, 30f)));
            TvCamRoll = cfg.Bind("Television", "Screen Camera Roll", 0f,
                new ConfigDescription("Tilt (roll) the picture, in degrees. Negative = rotate the image anticlockwise (to the left).",
                    new AcceptableValueRange<float>(-30f, 30f)));

            // Per-ROM button → key mapping: each cartridge in BepInEx/config/FiresMiniGames (and the built-in title)
            // gets its own "Controls - <rom>" section, so every game binds its buttons differently. Entries are typed
            // KeyCode, so the F8 panel renders a click-to-record button (a mouse click binds too). Read live.
            Nes.NesRomControls.Bind(cfg);
        }

        public static int[] ParseSizes(string csv, int[] fallback)
        {
            try
            {
                var vals = (csv ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                    .Where(n => n > 0).Distinct().OrderBy(n => n).ToArray();
                return vals.Length > 0 ? vals : fallback;
            }
            catch { return fallback; }
        }
    }
}
