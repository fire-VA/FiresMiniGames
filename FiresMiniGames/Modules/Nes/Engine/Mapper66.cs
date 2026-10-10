namespace FiresMiniGames.Modules.Nes.Engine
{
    // GxROM (iNES mapper 66) — a switchable 32KB PRG bank (register bits 4-5) and a switchable 8KB CHR bank (bits
    // 0-1), both set by a write to $8000-$FFFF. Clean-room from the NESdev spec. Covers Dragon Power, Doraemon,
    // Gumshoe, Thunder & Lightning…
    public class Mapper66 : IMapper
    {
        private readonly Cartridge cartridge;
        private int prgBank, chrBank;

        public Mapper66(Cartridge cart) { cartridge = cart; }
        public void Reset() { prgBank = 0; chrBank = 0; }

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
                prgBank = (value >> 4) & 0x03;
                chrBank = value & 0x03;
            }
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
