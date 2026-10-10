using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Shared keyboard/mouse → NES 8-button pad reader for both TV consoles (the emulator and The Legend of Fire).
    /// Every button comes from the RUNNING game's own keymap — <see cref="Nes.NesRomControls.Active"/>, set per session
    /// so each ROM (and the built-in title) uses its own "Controls - &lt;rom&gt;" section from F8. The keymap is the
    /// single source of truth (so a recorded rebind fully takes effect, including binding a button to a mouse click);
    /// its defaults are A = left-click, B = right-click, Select = Shift, Start = Enter, d-pad = WASD. Bit order matches
    /// the hardware: 0 A, 1 B, 2 Select, 3 Start, 4 Up, 5 Down, 6 Left, 7 Right.
    /// </summary>
    internal static class GamepadKeys
    {
        public static byte ReadPad()
        {
            var m = Nes.NesRomControls.Active;   // the current game's per-ROM keymap (null → shared defaults)
            byte s = 0;
            for (int i = 0; i < 8; i++)
                if (Held(m, i)) s |= (byte)(1 << i);
            return s;
        }

        // Input.GetKey accepts KeyCode.Mouse0/Mouse1 directly, so a click bound to a button reads the same as a key.
        private static bool Held(Nes.RomKeymap m, int i)
        {
            KeyCode c = m != null ? m.Get(i) : Nes.NesRomControls.Defaults[i];
            return c != KeyCode.None && Input.GetKey(c);
        }
    }
}
