namespace FiresMiniGames.Modules.Nes.Engine
{
    /// <summary>
    /// NES standard controller. The host pushes the 8-bit button state into <see cref="ControllerState"/> each
    /// frame (bit0 A, 1 B, 2 Select, 3 Start, 4 Up, 5 Down, 6 Left, 7 Right); the strobe/shift protocol on $4016
    /// is unchanged from the reference. No keyboard is read here, so the core stays free of Unity and Raylib.
    /// </summary>
    public class Input
    {
        public byte ControllerState;
        private byte controllerShift;

        public void UpdateController() { }   // state is pushed in by the host before each frame

        public void Write4016(byte value) { if ((value & 1) != 0) controllerShift = ControllerState; }

        public byte Read4016()
        {
            byte result = (byte)(controllerShift & 1);
            controllerShift >>= 1;
            return result;
        }
    }
}
