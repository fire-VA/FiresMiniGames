namespace FiresMiniGames.Modules.Nes.Engine
{
    using System;
    using System.IO;
    using System.Collections.Generic;
public interface IBus {
    void Write(ushort address, byte value);
    byte Read(ushort address);
}
}
