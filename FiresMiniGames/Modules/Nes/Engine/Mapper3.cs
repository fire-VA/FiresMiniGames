namespace FiresMiniGames.Modules.Nes.Engine
{
    // CNROM (iNES mapper 3) — fixed PRG like NROM, a single switchable 8KB CHR bank selected by any write to
    // $8000-$FFFF. Clean-room from the NESdev mapper spec. Covers many early titles (Arkanoid, Gradius, Bump'n'Jump…).
    public class Mapper3 : IMapper
    {
        private readonly Cartridge cartridge;
        private int chrBank;

        public Mapper3(Cartridge cart) { cartridge = cart; }
        public void Reset() { chrBank = 0; }

        public byte CPURead(ushort address)
        {
            if (address >= 0x6000 && address <= 0x7FFF) return cartridge.prgRAM[address - 0x6000];
            if (address >= 0x8000)
            {
                if (cartridge.prgBanks == 1) return cartridge.prgROM[address & 0x3FFF];
                return cartridge.prgROM[(address - 0x8000) % cartridge.prgROM.Length];
            }
            return 0;
        }

        public void CPUWrite(ushort address, byte value)
        {
            if (address >= 0x6000 && address <= 0x7FFF) { cartridge.prgRAM[address - 0x6000] = value; return; }
            if (address >= 0x8000) chrBank = value & 0x0F;   // % CHR size on read handles the real bank count
        }

        public byte PPURead(ushort address)
        {
            if (address < 0x2000)
            {
                if (cartridge.chrBanks == 0) return cartridge.chrRAM[address];
                return cartridge.chrROM[(chrBank * 0x2000 + address) % cartridge.chrROM.Length];
            }
            return 0;
        }

        public void PPUWrite(ushort address, byte value)
        {
            if (address < 0x2000 && cartridge.chrBanks == 0) cartridge.chrRAM[address] = value;
        }
    }
}
