namespace FiresMiniGames.Modules.Nes.Engine
{
    // AxROM (iNES mapper 7) — a single switchable 32KB PRG bank plus single-screen mirroring selected together by a
    // write to $8000-$FFFF (bits 0-2 = PRG bank, bit 4 = nametable A/B). CHR is 8KB RAM. Clean-room from the NESdev
    // spec. Covers Battletoads, Marble Madness, Wizards & Warriors 3, R.C. Pro-Am…
    public class Mapper7 : IMapper
    {
        private readonly Cartridge cartridge;
        private int prgBank;

        public Mapper7(Cartridge cart) { cartridge = cart; }
        public void Reset() { prgBank = 0; cartridge.SetMirroring(Mirroring.SingleScreenA); }

        public byte CPURead(ushort address)
        {
            if (address >= 0x8000)
                return cartridge.prgROM[(prgBank * 0x8000 + (address - 0x8000)) % cartridge.prgROM.Length];
            return 0;
        }

        public void CPUWrite(ushort address, byte value)
        {
            if (address >= 0x8000)
            {
                prgBank = value & 0x07;
                cartridge.SetMirroring((value & 0x10) != 0 ? Mirroring.SingleScreenB : Mirroring.SingleScreenA);
            }
        }

        public byte PPURead(ushort address)
        {
            if (address < 0x2000)
            {
                if (cartridge.chrBanks == 0) return cartridge.chrRAM[address];
                return cartridge.chrROM[address % cartridge.chrROM.Length];
            }
            return 0;
        }

        public void PPUWrite(ushort address, byte value)
        {
            if (address < 0x2000 && cartridge.chrBanks == 0) cartridge.chrRAM[address] = value;
        }
    }
}
