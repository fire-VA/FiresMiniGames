using System;
using UnityEngine;

namespace FiresMiniGames.Modules.Machines
{
    /// <summary>
    /// Loads the arcade machine buildables (roulette wheel / slot machine / pacman cabinet / air hockey table)
    /// from the embedded firesminigames bundle and appends them to <see cref="FiresMiniGames.piecePrefabs"/> so
    /// they ride the ZNetScene + hammer registration passes on both client and server, attaching each machine's
    /// game driver (all four are playable — the drivers own machine motion; only the pacman cabinet keeps its
    /// baked idle Animator for the attract loop).
    /// </summary>
    public static class MachinePieces
    {
        private static bool _loaded;

        private struct Def
        {
            public string Name, Label, Desc;
            public Def(string n, string l, string d) { Name = n; Label = l; Desc = d; }
        }

        private static readonly Def[] Defs =
        {
            new Def("piece_fires_roulette", "Roulette Wheel",
                "A casino wheel of red and black. Use it to place your bets — the ball lands where it lands."),
            new Def("piece_fires_slotmachine", "Slot Machine",
                "A one-armed bandit, reels heavy with strange treasures. Use it to pull the arm."),
            new Def("piece_fires_arcade_pacman", "Arcade Cabinet",
                "A glowing arcade cabinet from another world. Insert a coin and outrun the little ghosts."),
            new Def("piece_fires_airhockey", "Air Hockey Table",
                "A neon rink on legs — pushers, puck and a live scoreboard. Use it to play. First to 7."),
            new Def("piece_fires_tv_soviet", "Soviet Television",
                "A KVN-49 with its water-magnifier lens. Somehow, it plays DOOM. Turn it on."),
            new Def("piece_fires_tv_retro", "Retro Television",
                "A wood-cabinet set from another age. Somehow, it plays DOOM. Turn it on."),
            new Def("piece_fires_nes", "Nintendo Entertainment System",
                "A little grey console with no screen of its own. Set it beside a television, insert a cartridge and power on — it plays on the set."),
            new Def("piece_fires_nes_cartridge", "Super Mario Bros — Cartridge",
                "The classic grey cartridge with its red-and-white label, stood for display. Purely for show."),
            new Def("piece_fires_tv_pvm", "Broadcast Monitor",
                "A Sony PVM studio monitor — the sharpest tube in the house. Set a console beside it, or turn it on."),
            new Def("piece_fires_tv_wall", "Flatscreen Television",
                "A wide flat-panel screen — the centrepiece of any proper mancave. Set a console beside it, or turn it on."),
            new Def("piece_fires_tv_monitor", "Gaming Monitor",
                "A widescreen flat-panel on a stand, ready for any desk. Set a console beside it, or turn it on."),
            new Def("piece_fires_bench", "Carved Bench",
                "A long settle carved with knotwork, worn smooth by years of use. Sit and rest a while."),
            new Def("piece_fires_sofa", "Floral Sofa",
                "A deep sofa in birds-and-blossoms brocade. Sink in and stay a while."),
            new Def("piece_fires_couch", "Lounge Couch",
                "A soft couch built for slouching. Sit down and put your feet up."),
            new Def("piece_fires_nes_console", "Nintendo Console",
                "The little grey console on its own. Set it beside a television, insert a cartridge and power on — it plays on the set."),
            new Def("piece_fires_nes_controller", "Game Controller",
                "A wired control pad, all its own. A bit of mancave dressing beside the console."),
            new Def("piece_fires_zelda_cartridge", "The Legend of Fire — Cartridge",
                "A gold cartridge from a legendary quest, stood on end for display. Pure mancave dressing."),
            new Def("piece_fires_zelda_box", "The Legend of Fire — Box",
                "The gold box for a legendary adventure, cover out on the shelf. Pure mancave dressing."),
            new Def("piece_fires_duckhunt_cartridge", "Duck Hunt — Cartridge",
                "The grey Duck Hunt cartridge stood on end for the shelf. Pure mancave dressing."),

            // ── NES collector's pack: a detailed console + controller, boxed games, and loose cartridges ──
            new Def("piece_fires_nes_deluxe", "Nintendo Console (Deluxe)",
                "A finely detailed grey console — lid flap, vents, ports and all. Pure mancave dressing."),
            new Def("piece_fires_nes_controller_deluxe", "Nintendo Controller (Deluxe)",
                "The classic control pad on its cord. Pure mancave dressing."),
            new Def("piece_fires_nes_dustcover", "NES Dust Cover",
                "A vinyl dust cover for the console. Pure mancave dressing."),

            new Def("piece_fires_box_bayoubilly", "Game Box — The Adventures of Bayou Billy", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_castlevania", "Game Box — Castlevania", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_contra", "Game Box — Contra", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_doubledragon", "Game Box — Double Dragon II", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_dragonwarior", "Game Box — Dragon Warrior", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_friday13", "Game Box — Friday the 13th", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_gauntlet", "Game Box — Gauntlet", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_mario3", "Game Box — Super Mario Bros. 3", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_megaman2_30th", "Game Box — Mega Man 2", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_metalgear", "Game Box — Metal Gear", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_ninjag", "Game Box — Ninja Gaiden", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_punchout", "Game Box — Punch-Out!!", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_robocop", "Game Box — RoboCop", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_tetris", "Game Box — Tetris", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_tmnt", "Game Box — Teenage Mutant Ninja Turtles", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_zelda", "Game Box — The Legend of Zelda", "A boxed NES classic for the shelf. Pure mancave dressing."),
            new Def("piece_fires_box_zeldaii", "Game Box — Zelda II: The Adventure of Link", "A boxed NES classic for the shelf. Pure mancave dressing."),

            new Def("piece_fires_cart_abadox", "Cartridge — Abadox", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_battletoads", "Cartridge — Battletoads", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_bionicc", "Cartridge — Bionic Commando", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_dracscurse", "Cartridge — Castlevania III: Dracula's Curse", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_dragon_spirit", "Cartridge — Dragon Spirit", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_ducktails", "Cartridge — DuckTales", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_excitebike", "Cartridge — Excitebike", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_faxanadu", "Cartridge — Faxanadu", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_ffi", "Cartridge — Final Fantasy", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_godzilla", "Cartridge — Godzilla", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_kid_icarus", "Cartridge — Kid Icarus", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_marblemadness", "Cartridge — Marble Madness", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_mario", "Cartridge — Super Mario Bros.", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_mario_2", "Cartridge — Super Mario Bros. 2", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_mario_3", "Cartridge — Super Mario Bros. 3", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_megaman2", "Cartridge — Mega Man 2", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_megaman_3", "Cartridge — Mega Man 3", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_ninjag", "Cartridge — Ninja Gaiden", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_ninjag2", "Cartridge — Ninja Gaiden II", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_sectionz", "Cartridge — Section Z", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_spyhunter", "Cartridge — Spy Hunter", "A loose NES cartridge for the collection. Pure mancave dressing."),
            new Def("piece_fires_cart_tetris", "Cartridge — Tetris", "A loose NES cartridge for the collection. Pure mancave dressing."),
        };

        // Bundle materials reference the shader copy baked into the bundle, whose exotic variants (Standard
        // transparent for the arcade glass, emissive for the screens/marquee) can be stripped at bundle-build —
        // Unity then renders them magenta whenever that variant isn't otherwise resident. Rebinding each material
        // to the game's fully-compiled resident shader by name cures it. Runs on a placed instance (shaders are
        // loaded by then); no-ops on a headless server where Shader.Find has nothing to return. Idempotent.
        internal static void RebindBundleShaders(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    var resident = Shader.Find(m.shader.name);
                    if (resident == null || resident == m.shader) continue;
                    int q = m.renderQueue;   // shader reassignment resets the queue; the glass depends on 3000
                    m.shader = resident;
                    m.renderQueue = q;
                }
        }

        public static void Load(AssetBundle bundle)
        {
            if (_loaded) return;
            if (bundle == null)
            {
                Debug.LogWarning($"{FiresMiniGames.PluginName}: bundle missing — machine pieces unavailable.");
                return;
            }
            _loaded = true;

            int ok = 0;
            foreach (var def in Defs)
                if (Configure(bundle, def)) ok++;
            Debug.Log($"{FiresMiniGames.PluginName}: machine pieces loaded ({ok}/{Defs.Length}) + queued for hammer registration.");
        }

        private static bool Configure(AssetBundle bundle, Def def)
        {
            try
            {
                var prefab = bundle.LoadAsset<GameObject>($"assets/custom/minigamemodels/pieces/{def.Name}.prefab")
                             ?? bundle.LoadAsset<GameObject>(def.Name);
                if (prefab == null)
                {
                    Debug.LogWarning($"{FiresMiniGames.PluginName}: {def.Name} not in the bundle.");
                    return false;
                }

                UnityEngine.Object.DontDestroyOnLoad(prefab);   // fake-null protection — the static list outlives worlds

                var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;
                nview.m_syncInitialScale = true;   // Structure Tweaks / Infinity Hammer scaling persists

                var piece = prefab.GetComponent<Piece>() ?? prefab.AddComponent<Piece>();
                piece.m_name = def.Label;
                piece.m_description = def.Desc;
                piece.m_category = Piece.PieceCategory.Furniture;
                piece.m_enabled = true;
                piece.m_canBeRemoved = true;
                piece.m_allowedInDungeons = true;
                piece.m_placeEffect = new EffectList();          // bundle-baked EffectList refs are the PlacePiece trap
                piece.m_resources = new Piece.Requirement[0];

                if (def.Name == "piece_fires_airhockey")
                {
                    if (prefab.GetComponent<AirHockey.AirHockeyTable>() == null) prefab.AddComponent<AirHockey.AirHockeyTable>();
                    if (prefab.GetComponent<AirHockey.AirHockeyScoreboard>() == null) prefab.AddComponent<AirHockey.AirHockeyScoreboard>();
                }
                if (def.Name == "piece_fires_slotmachine" && prefab.GetComponent<Slots.SlotMachineGame>() == null)
                    prefab.AddComponent<Slots.SlotMachineGame>();
                if (def.Name == "piece_fires_roulette" && prefab.GetComponent<Roulette.RouletteGame>() == null)
                    prefab.AddComponent<Roulette.RouletteGame>();
                if (def.Name == "piece_fires_arcade_pacman" && prefab.GetComponent<Pacman.PacmanArcadeGame>() == null)
                    prefab.AddComponent<Pacman.PacmanArcadeGame>();
                if ((def.Name == "piece_fires_tv_soviet" || def.Name == "piece_fires_tv_retro" || def.Name == "piece_fires_tv_pvm" || def.Name == "piece_fires_tv_wall" || def.Name == "piece_fires_tv_monitor") && prefab.GetComponent<DoomTv.DoomTvGame>() == null)
                    prefab.AddComponent<DoomTv.DoomTvGame>();
                if ((def.Name == "piece_fires_nes" || def.Name == "piece_fires_nes_console") && prefab.GetComponent<Nes.NesConsoleGame>() == null)
                    prefab.AddComponent<Nes.NesConsoleGame>();
                if ((def.Name.StartsWith("piece_fires_box_") || def.Name == "piece_fires_zelda_box") && prefab.GetComponent<Nes.CartridgeBox>() == null)
                    prefab.AddComponent<Nes.CartridgeBox>();   // boxes dispense/return their game cartridge
                if (def.Name == "piece_fires_bench" || def.Name == "piece_fires_sofa" || def.Name == "piece_fires_couch")
                    WireSeats(prefab, def.Label);

                if (!FiresMiniGames.piecePrefabs.Contains(prefab))
                    FiresMiniGames.piecePrefabs.Add(prefab);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{FiresMiniGames.PluginName}: machine piece '{def.Name}' load failed: {ex}");
                return false;
            }
        }

        // Adds a vanilla Chair to each baked "seat*" child so the piece is sittable per-seat. Each seat child has
        // its own BoxCollider, so Chair resolves against the seat the crosshair is on (GetComponentInParent<Chair>);
        // the builder baked the "attach" transform each Chair sits the player at.
        private static void WireSeats(GameObject prefab, string label)
        {
            foreach (Transform seat in prefab.transform)
            {
                if (!seat.name.StartsWith("seat")) continue;
                var attach = seat.Find("attach");
                if (attach == null) continue;
                var chair = seat.GetComponent<Chair>() ?? seat.gameObject.AddComponent<Chair>();
                chair.m_name = label;
                chair.m_attachPoint = attach;
                chair.m_useDistance = 1.8f;
                chair.m_attachAnimation = "attach_chair";
                chair.m_detachOffset = new Vector3(0f, 0.5f, 0f);
            }
        }
    }
}
