# Third-Party Notices — FiresMiniGames

FiresMiniGames vendors (embeds the source of) the following third-party emulator cores.
This file records their copyright and license terms and ships with the mod.

**FiresMiniGames as a whole is licensed under the GNU General Public License, version 3 (GPL-3.0)**
(full text in `LICENSE`), because it contains the GPL components below (ManagedDoom, GPL-2.0-or-later,
used under GPL-3.0; sGBA, GPL-3.0). The MIT-licensed cores keep their own notices below. The
corresponding source is published at https://github.com/fire-VA/FiresMiniGames.

---

## NET-NES (NES emulator core)

`Modules/Nes/Engine/*` is derived from **NET-NES** by Bot Randomness
(https://github.com/BotRandomness/NET-NES), used under the MIT License. The raylib
frontend was removed and the core adapted to run headless inside Unity; an APU, four
additional mappers (CNROM/AxROM/ColorDreams/GxROM) and SRAM persistence were added by
the FiresMiniGames authors.

```
MIT License

Copyright (c) 2025 Bot Randomness

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## LeBoy (Game Boy emulator core)

`Modules/GameBoy/Engine/*` is derived from **LeBoy** by Thomas Altenburger
(https://github.com/mrhelmut/LeBoy), used under the MIT License. The MonoGame frontend was
removed and the `LeBoyLib` core adapted to run headless inside Unity.

```
MIT License

Copyright (c) 2017 Thomas Altenburger

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## ManagedDoom (DOOM engine)

`Modules/Doom/Engine/*` is derived from **Managed Doom** by Nobuaki Tanaka
(https://github.com/sinshu/managed-doom), itself a C# port of id Software's DOOM engine.
It is licensed under the **GNU General Public License, version 2 (GPL-2.0)**:

> Copyright (C) 1993-1996 id Software, Inc.
> Copyright (C) 2019-2020 Nobuaki Tanaka
>
> This program is free software; you can redistribute it and/or modify it under the
> terms of the GNU General Public License as published by the Free Software Foundation;
> either version 2 of the License, or (at your option) any later version.

Because GPL-2.0 is a copyleft license, distributing this component obliges the combined
work to be offered under GPL-2.0 with corresponding source. See the full text at
https://www.gnu.org/licenses/old-licenses/gpl-2.0.html

---

## sGBA (Game Boy Advance emulator core)

`Modules/GBA/Engine/*` is derived from **sGBA** by Asphaltian
(https://github.com/Asphaltian/sgba), a Game Boy Advance emulator. Only the emulator core
(`Code/Emulator/*`) is vendored — the ARM7TDMI CPU, memory map, HLE BIOS, DMA, timers,
audio, savedata and SIO. The s&box (`Sandbox.Rendering`) GPU/compute-shader PPU was NOT
vendored and is replaced by a stub test-pattern renderer (`GbaVideo.Rendering.cs`); the
s&box frontend, `Code/Networking/*`, and the Networking-coupled link-cable session were
likewise dropped. net48/Unity-Mono compatibility shims live in `Modules/GBA/Engine/GbaCompat.cs`.
The real software PPU and audio wiring are follow-ups. It is licensed under the
**GNU General Public License, version 3 (GPL-3.0)**:

> Copyright (C) Asphaltian
>
> This program is free software: you can redistribute it and/or modify it under the terms
> of the GNU General Public License as published by the Free Software Foundation, either
> version 3 of the License, or (at your option) any later version.

Like ManagedDoom above, this is a copyleft (GPL) component: distributing it obliges the
combined work to be offered under the GPL with corresponding source, which is why FiresMiniGames
is released under GPL-3.0 (see the top of this file). See the full text at https://www.gnu.org/licenses/gpl-3.0.html


---

## Freedoom (DOOM game data)

`freedoom1.wad` is **Freedoom: Phase 1**, the free-content IWAD from the Freedoom project
(https://freedoom.github.io/). **No WAD is included with this mod.** The DOOM machine looks for a
WAD the player places in its folder: `freedoom1.wad` (free, below) or the player's own `doom1.wad`.
id Software's commercial `doom.wad` is NOT distributed and never should be.

Licensed under the 3-clause BSD licence:

```
Copyright (c) 2001-2024
Contributors to the Freedoom project.  All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.
  * Neither the name of the Freedoom project nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

The BSD notice above must travel with any distribution that includes the WAD - including a modpack
zip, not just a Thunderstore upload. Contributor list: https://github.com/freedoom/freedoom/blob/master/CREDITS