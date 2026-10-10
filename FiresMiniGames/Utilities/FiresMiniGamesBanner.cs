using System;
using FiresCore.Logging;
using UnityEngine;

namespace FiresMiniGames
{
    // FiresMiniGames' console banner, rendered through the shared Banner engine — attract-mode arcade marquee:
    // pixel chase line, FIRES / MINI / GAMES block letters in cabinet colours, INSERT COIN. Fires at the top of Awake.
    internal static class FiresMiniGamesBanner
    {
        public static void PrintBig()
        {
            try
            {
                if (FiresMiniGames.Log != null) FiresMiniGames.Log.LogInfo("Fires Mini Games Loading...");
                else Debug.Log($"[{FiresMiniGames.PluginName}] Fires Mini Games Loading...");
                Banner.Print(s_bigLines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] Big banner failed: {ex.Message}");
            }
        }

        private static Banner.Segment[] L(params Banner.Segment[] segs) => segs;
        private static Banner.Segment S(string text, ConsoleColor color) => new Banner.Segment(text, color);

        private static readonly Banner.Segment[][] s_bigLines =
        {
            new Banner.Segment[0],
            L(S("▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄", ConsoleColor.DarkMagenta)),
            new Banner.Segment[0],
            L(S("   ᗧ", ConsoleColor.Yellow), S(" · · · · · · · · ·   ", ConsoleColor.White),
              S("ᗣ ", ConsoleColor.Red), S("ᗣ ", ConsoleColor.Cyan), S("ᗣ", ConsoleColor.Magenta)),
            new Banner.Segment[0],
            L(S(" ███████╗██╗██████╗ ███████╗ ███████╗", ConsoleColor.Red)),
            L(S(" ██╔════╝██║██╔══██╗██╔════╝ ██╔════╝", ConsoleColor.Red)),
            L(S(" █████╗  ██║██████╔╝█████╗   ███████╗", ConsoleColor.Red)),
            L(S(" ██╔══╝  ██║██╔══██╗██╔══╝   ╚════██║", ConsoleColor.DarkRed)),
            L(S(" ██║     ██║██║  ██║███████╗ ███████║", ConsoleColor.DarkRed)),
            L(S(" ╚═╝     ╚═╝╚═╝  ╚═╝╚══════╝ ╚══════╝", ConsoleColor.DarkRed)),
            new Banner.Segment[0],
            L(S("        ███╗   ███╗██╗███╗   ██╗██╗", ConsoleColor.Cyan)),
            L(S("        ████╗ ████║██║████╗  ██║██║", ConsoleColor.Cyan)),
            L(S("        ██╔████╔██║██║██╔██╗ ██║██║", ConsoleColor.Cyan)),
            L(S("        ██║╚██╔╝██║██║██║╚██╗██║██║", ConsoleColor.DarkCyan)),
            L(S("        ██║ ╚═╝ ██║██║██║ ╚████║██║", ConsoleColor.DarkCyan)),
            L(S("        ╚═╝     ╚═╝╚═╝╚═╝  ╚═══╝╚═╝", ConsoleColor.DarkCyan)),
            new Banner.Segment[0],
            L(S("              ██████╗  █████╗ ███╗   ███╗███████╗███████╗", ConsoleColor.Yellow)),
            L(S("             ██╔════╝ ██╔══██╗████╗ ████║██╔════╝██╔════╝", ConsoleColor.Yellow)),
            L(S("             ██║  ███╗███████║██╔████╔██║█████╗  ███████╗", ConsoleColor.Yellow)),
            L(S("             ██║   ██║██╔══██║██║╚██╔╝██║██╔══╝  ╚════██║", ConsoleColor.DarkYellow)),
            L(S("             ╚██████╔╝██║  ██║██║ ╚═╝ ██║███████╗███████║", ConsoleColor.DarkYellow)),
            L(S("              ╚═════╝ ╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝╚══════╝", ConsoleColor.DarkYellow)),
            new Banner.Segment[0],
            L(S("══════════════════════════════════════════", ConsoleColor.DarkGray)),
            L(S("   ROULETTE • SLOTS • ARCADE • BOARDGAMES", ConsoleColor.Yellow)),
            L(S("══════════════════════════════════════════", ConsoleColor.DarkGray)),
            new Banner.Segment[0],
            L(S("        1UP   ", ConsoleColor.White), S("HI-SCORE 5450", ConsoleColor.Cyan), S("   2UP", ConsoleColor.White)),
            L(S("            ▶ INSERT COIN ◀", ConsoleColor.White)),
            L(S("               CREDIT 01", ConsoleColor.Gray)),
            new Banner.Segment[0],
            L(S("   STEP RIGHT UP, VIKINGS — THE HOUSE ALWAYS WINS.", ConsoleColor.White)),
            L(S("   WHEELS, REELS, GHOSTS AND CHECKMATES AWAIT.", ConsoleColor.Gray)),
            new Banner.Segment[0],
            L(S("▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄▀▄", ConsoleColor.DarkMagenta)),
            new Banner.Segment[0],
        };
    }
}
