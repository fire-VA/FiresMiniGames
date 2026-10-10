namespace FiresMiniGames.Modules.Nes.Engine
{
    /// <summary>
    /// Trimmed from the reference CLI helper: only the static flags the emulation core actually reads survive.
    /// <c>debugs0h</c> forces the sprite-0 hit (a compatibility shortcut for games that spin waiting on it);
    /// <c>scale</c> is unused by the headless core but kept so PPU.DrawFrame's signature is untouched. The
    /// argument parser, Raylib logging and ASCII art from the original are dropped.
    /// </summary>
    public static class Helper
    {
        public static int scale = 1;
        public static bool debug = false;
        public static bool debugs0h = false;
    }
}
