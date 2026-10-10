namespace FiresMiniGames.Modules.Nes.Engine
{
    /// <summary>
    /// 32-bit RGBA pixel — the vendored PPU's color type, standing in for Raylib_cs.Color so the emulator
    /// core carries no native dependency. The Unity glue reads R/G/B/A into a Color32 when it blits the frame.
    /// </summary>
    public struct Color
    {
        public byte R, G, B, A;
        public Color(int r, int g, int b, int a) { R = (byte)r; G = (byte)g; B = (byte)b; A = (byte)a; }
    }
}
