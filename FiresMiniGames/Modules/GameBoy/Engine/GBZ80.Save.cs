namespace LeBoyLib
{
    public partial class GBZ80
    {
        /// <summary>True when the loaded cartridge is battery-backed (its save RAM should persist to disk).</summary>
        public bool HasBattery => Memory.HasBattery;

        /// <summary>Snapshot the cartridge's save RAM for writing to a .sav file.</summary>
        public byte[] GetSaveRam() => Memory.GetSaveRam();

        /// <summary>Restore save RAM read from a .sav file (call before the game boots).</summary>
        public void LoadSaveRam(byte[] data) => Memory.SetSaveRam(data);
    }
}
