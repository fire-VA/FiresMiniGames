namespace FiresMiniGames.Modules.Nes.Engine
{
    /// <summary>
    /// The console: wires a cartridge to the bus and runs the CPU + PPU. <see cref="Run"/> advances exactly one
    /// NTSC video frame (~29,828 CPU cycles, three PPU dots per CPU cycle) and leaves the finished image in
    /// <see cref="FrameBuffer"/>. The host sets <see cref="Input"/>.ControllerState before each Run and copies
    /// FrameBuffer to a texture after. Construction throws (rather than exiting the process) on a bad ROM.
    /// </summary>
    public class NES
    {
        Cartridge cartridge;
        Bus bus;

        public NES(byte[] romData)
        {
            cartridge = new Cartridge(romData);
            bus = new Bus(cartridge);
            bus.cpu.Reset();
        }

        public Input Input => bus.input;
        public Apu Apu => bus.apu;
        public Color[] FrameBuffer => bus.ppu.FrameBuffer;
        public int MapperID => cartridge.mapperID;
        public bool HasBattery => cartridge.hasBattery;
        /// <summary>Battery-backed save RAM ($6000-$7FFF). Persisted to a .sav file by the host when HasBattery.</summary>
        public byte[] Sram => cartridge.prgRAM;
        public int ScreenWidth => 256;
        public int ScreenHeight => 240;

        public void Run()
        {
            int cycles = 0;
            bus.input.UpdateController();

            while (cycles < 29828)
            {
                int used = bus.cpu.ExecuteInstruction();
                cycles += used;
                bus.ppu.Step(used * 3);
                bus.apu.Clock(used);
            }

            bus.ppu.DrawFrame(Helper.scale);
        }
    }
}
