namespace FiresMiniGames.Modules.Nes.Engine
{
    using System;
    using System.IO;
    using System.Collections.Generic;
public interface IMapper {
    void Reset();
    
    byte CPURead(ushort address);
    void CPUWrite(ushort address, byte value);

    byte PPURead(ushort address);
    void PPUWrite(ushort address, byte value);
}
}
