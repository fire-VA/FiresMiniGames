using System.Collections.Generic;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// A dungeon room: a 16x11 interior tile grid (blue-brick border auto-drawn around it) plus a door type on each
    /// side. Door types: Wall (none), Open, Locked (key), Shutter (closed until the room is cleared), Bomb (hidden
    /// until blown open). Interior tile chars: '.' floor, 'B' block, 'W' water, 'k' keese, 'l' gel, 'z' stalfos.
    /// </summary>
    internal sealed class DungeonRoom
    {
        public string[] Tiles;
        public byte N, E, S, W;
        public bool GivesBow;
        public bool Boss;
        public bool Entrance;
    }

    internal static class ZeldaDungeon
    {
        public const byte Wall = 0, Open = 1, Locked = 2, Shutter = 3, Bomb = 4;

        private static string[] Empty() => new[]
        {
            "................", "................", "................", "................",
            "................", "................", "................", "................",
            "................", "................", "................"
        };

        // Level 1 — a small vertical run (entrance -> enemies -> bow -> Aquamentus). Grows toward the guide's map.
        public static Dictionary<int, DungeonRoom> Level1()
        {
            var d = new Dictionary<int, DungeonRoom>();
            void R(int col, int row, DungeonRoom rm) => d[col << 8 | row] = rm;

            R(2, 3, new DungeonRoom { Tiles = Empty(), N = Open, Entrance = true });

            R(2, 2, new DungeonRoom
            {
                Tiles = new[]
                {
                    "................", "................", "....k......k....", "................",
                    "......l....l....", "................", "....z........z..", "................",
                    "................", "................", "................"
                },
                N = Shutter, S = Open
            });

            R(2, 1, new DungeonRoom { Tiles = Empty(), N = Open, S = Open, GivesBow = true });

            R(2, 0, new DungeonRoom { Tiles = Empty(), S = Open, Boss = true });

            return d;
        }
    }
}
