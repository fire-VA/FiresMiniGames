using System.Collections.Generic;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// The overworld — an 8x6 grid of 16x11-tile screens generated from a compact terrain map. Each screen gets a
    /// rock border with door gaps toward its existing neighbours (N/S at cols 7-8, E/W at rows 5-6), terrain fill
    /// (field/forest/lake/mountain/desert), a guaranteed walkable cross so every door is reachable, scattered
    /// enemies, and any placed feature (the sword cave, the Level 1 dungeon, item caves). Grows toward the guide's
    /// full 16x8 Hyrule in later passes.
    /// </summary>
    internal static class ZeldaWorld
    {
        public const int Cols = 8, Rows = 6;

        // F field, T forest, W lake, M mountain, D desert. Start is (3,5).
        private static readonly string[] Terr =
        {
            "MMFTTFFM",
            "MFFTTFFF",
            "FFWWFFFF",
            "FFFWFDDF",
            "TFFFFDDF",
            "FFFFFFFM",
        };

        // Placed cave/dungeon features (rendered as a cave mouth, entered by walking up into them).
        private static char Feature(int c, int r)
        {
            if (c == 3 && r == 5) return 'C';   // start — the sword cave
            if (c == 2 && r == 4) return 'L';   // Level 1 dungeon
            if (c == 5 && r == 4) return 'C';   // boomerang cave
            if (c == 6 && r == 1) return 'C';   // candle cave
            return '.';
        }

        public static readonly string[] Empty =
        {
            "RRRRRRRRRRRRRRRR","R..............R","R..............R","R..............R","R..............R",
            "R..............R","R..............R","R..............R","R..............R","R..............R","RRRRRRRRRRRRRRRR"
        };

        public static bool Walkable(char c)
        {
            switch (c) { case 'R': case 'T': case 'W': return false; default: return true; }
        }

        public static ZeldaArt.Sprite TileSprite(char c)
        {
            switch (c)
            {
                case 'R': return ZeldaArt.Rock;
                case 'T': return ZeldaArt.Tree;
                case 'W': return ZeldaArt.Water;
                case 'S': return ZeldaArt.Sand;
                case 'C': case 'L': return ZeldaArt.CaveMouth;
                default: return ZeldaArt.Grass;   // '.', 'o', 'k'
            }
        }

        public static Dictionary<int, string[]> Build()
        {
            var d = new Dictionary<int, string[]>();
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    d[c << 8 | r] = Gen(Terr[r][c], r > 0, c < Cols - 1, r < Rows - 1, c > 0, Feature(c, r), c, r);
            return d;
        }

        private static bool Block(char c) => c == 'T' || c == 'R' || c == 'W';

        private static string[] Gen(char t, bool n, bool e, bool s, bool w, char feature, int col, int row)
        {
            uint rng = (uint)(col * 374761393 + row * 668265263 + t * 40503) | 1u;
            int R(int m) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (int)(rng % (uint)m); }

            var g = new char[11][];
            for (int r = 0; r < 11; r++)
            {
                g[r] = new char[16];
                for (int c = 0; c < 16; c++)
                {
                    if (r == 0 || r == 10 || c == 0 || c == 15)
                    {
                        bool gap = (r == 0 && n && (c == 7 || c == 8)) || (r == 10 && s && (c == 7 || c == 8))
                                 || (c == 0 && w && (r == 5 || r == 6)) || (c == 15 && e && (r == 5 || r == 6));
                        g[r][c] = gap ? '.' : 'R';
                    }
                    else
                    {
                        switch (t)
                        {
                            case 'T': g[r][c] = R(3) == 0 ? '.' : 'T'; break;
                            case 'W': g[r][c] = (c >= 5 && c <= 10 && r >= 3 && r <= 6) ? 'W' : '.'; break;
                            case 'M': g[r][c] = R(3) == 0 ? 'R' : '.'; break;
                            case 'D': g[r][c] = 'S'; break;
                            default: g[r][c] = R(16) == 0 ? 'T' : '.'; break;   // field
                        }
                    }
                }
            }
            // guarantee a walkable cross so every door is reachable
            for (int c = 1; c <= 14; c++) { if (Block(g[5][c])) g[5][c] = '.'; if (Block(g[6][c])) g[6][c] = '.'; }
            for (int r = 1; r <= 9; r++) { if (Block(g[r][7])) g[r][7] = '.'; if (Block(g[r][8])) g[r][8] = '.'; }
            // scatter a couple of enemies on open ground (not the start screen)
            if ((t == 'F' || t == 'D') && !(col == 3 && row == 5))
                for (int i = 0; i < 2; i++) { int ec = 2 + R(12), er = 2 + R(7); if (g[er][ec] == '.' || g[er][ec] == 'S') g[er][ec] = R(2) == 0 ? 'o' : 'k'; }
            // feature cave (2x2 at cols 4-5, rows 3-4) with a cleared pocket, off the north column
            if (feature != '.')
            {
                for (int r = 2; r <= 5; r++) for (int c = 3; c <= 6; c++) g[r][c] = '.';
                g[3][4] = feature; g[3][5] = feature; g[4][4] = feature; g[4][5] = feature;
            }

            var rows = new string[11];
            for (int r = 0; r < 11; r++) rows[r] = new string(g[r]);
            return rows;
        }
    }
}
