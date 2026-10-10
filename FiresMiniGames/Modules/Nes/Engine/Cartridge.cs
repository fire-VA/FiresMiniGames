namespace FiresMiniGames.Modules.Nes.Engine
{
    using System;
    using System.IO;
    using System.Collections.Generic;
public class Cartridge {
    public byte[] rom;

    public byte[] prgROM;
    public byte[] chrROM;

    public int prgBanks;
    public int chrBanks;
    public int mapperID;
    public bool mirrorHorizontal;
    public bool mirrorVertical;
    public Mirroring mirroringMode;
    public bool hasBattery;

    public byte[] prgRAM;
    public byte[] chrRAM;

    public IMapper mapper;

    public Cartridge(string romPath) : this(File.ReadAllBytes(romPath)) { }

    public Cartridge(byte[] romData) {
        rom = romData ?? throw new Exception("No ROM data.");

        if (rom.Length < 16 || rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1A)
            throw new Exception("Not a valid iNES ROM (missing NES header).");

        prgBanks = rom[4];
        chrBanks = rom[5];

        byte flag6 = rom[6];
        byte flag7 = rom[7];

        mirrorVertical = (flag6 & 0x01) != 0;
        mirrorHorizontal = !mirrorVertical;
        hasBattery = (flag6 & 0x02) != 0;

        if ((flag6 & 0x08) != 0) {
            
        } else if ((flag6 & 0x01) != 0) {
            mirroringMode = Mirroring.Vertical;
        } else {
            mirroringMode = Mirroring.Horizontal;
        }

        mapperID = flag6 >> 4 | ((flag7 >> 4) << 4);

        int prgSize = prgBanks * 16 * 1024;
        int chrSize = chrBanks * 8 * 1024;

        int offset = 16; //iNES rom is 16 bytes
        prgROM = new byte[prgSize];
        Array.Copy(rom, offset, prgROM, 0, prgSize);

        offset += prgSize;
        chrROM = new byte[chrSize];
        Array.Copy(rom, offset, chrROM, 0, chrSize);

        prgRAM = new byte[8 * 1024];
        chrRAM = new byte[8 * 1024];

        switch (mapperID) {
            case 0:
                mapper = new Mapper0(this);
                break;
            case 1:
                mapper = new Mapper1(this);
                break;
            case 2:
                mapper = new Mapper2(this);
                break;
            case 3:
                mapper = new Mapper3(this);
                break;
            case 4:
                mapper = new Mapper4(this);
                break;
            case 7:
                mapper = new Mapper7(this);
                break;
            case 11:
                mapper = new Mapper11(this);
                break;
            case 66:
                mapper = new Mapper66(this);
                break;
            default:
                throw new Exception("Mapper " + mapperID + " is not supported (this build has 0/NROM, 1/MMC1, 2/UxROM, 3/CNROM, 4/MMC3, 7/AxROM, 11/ColorDreams, 66/GxROM).");
        }
        mapper.Reset();

        //Console.WriteLine($"Cartridge loaded: Mapper {mapperID}, PRG {prgBanks * 16}KB, CHR {(chrSize > 0 ? chrBanks * 8 : 8)}KB");
        Console.WriteLine($"Cartridge loaded: Mapper {mapperID}, PRG-ROM {prgBanks * 16}KB, {(chrSize > 0 ? $"{chrBanks * 8}KB CHR-ROM" : "CHR-RAM")}");
    }

    public byte CPURead(ushort address) {
        return mapper.CPURead(address);
    }

    public void CPUWrite(ushort address, byte value) {
        mapper.CPUWrite(address, value);
    }

    public byte PPURead(ushort address) {
        return mapper.PPURead(address);
    } 
    public void PPUWrite(ushort address, byte value) {
        mapper.PPUWrite(address, value);
    }

    public void SetMirroring(Mirroring mode) {
        mirroringMode = mode;
        mirrorVertical = mode == Mirroring.Vertical;
        mirrorHorizontal = mode == Mirroring.Horizontal;
    }
}

public enum Mirroring {
    Horizontal,
    Vertical,
    SingleScreenA,
    SingleScreenB
}
}
