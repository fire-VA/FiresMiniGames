namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        // The GB has 4 channels, memory-mapped at $FF10-$FF26 (+ wave RAM $FF30-$FF3F):
        //   Ch1: square wave with sweep + volume envelope.
        //   Ch2: square wave with volume envelope.
        //   Ch3: 4-bit wave patterns from wave RAM.
        //   Ch4: white noise (LFSR) with a volume envelope.
        // GBMemory routes register writes to the APU; the CPU clocks it here, once per instruction.

        /// <summary>
        /// The sound chip. Exposed so the Unity host (GbRuntime) can set its sample rate and drain its output buffer.
        /// </summary>
        public GbApu Apu => Memory.Apu;

        // Advance the APU by the number of t-cycles the just-executed instruction took. At this point in
        // DecodeAndDispatch, totalT holds that count (it is reset to 0 at the end of each dispatch).
        private void Sound_Step()
        {
            Memory.Apu.Clock((int)totalT);
        }
    }
}
