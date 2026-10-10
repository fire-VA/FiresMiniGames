namespace LeBoyLib
{
    using System;

    /// <summary>
    /// The Game Boy (DMG) audio processing unit — the sound chip the vendored LeBoy core shipped without, so every
    /// cartridge ran silent. This is a from-scratch clean-room implementation of the four hardware channels
    /// (square+sweep, square, wave, LFSR noise) with their real length-counter / volume-envelope / frequency-sweep
    /// behaviour, driven by the 512 Hz frame sequencer. It is clocked in CPU t-cycles by the CPU core, mixes the four
    /// channel DACs with NR50/NR51 volume+panning, runs a DC-blocking high-pass (the output capacitor) plus a gentle
    /// low-pass, and appends output samples at a host-chosen rate into <see cref="Samples"/> for the Unity side to hand
    /// to an AudioSource. UnityEngine-free so the gb_proof harness can render a WAV of it headlessly.
    /// Register map: NR10-NR14 $FF10-$FF14, NR21-NR24 $FF16-$FF19, NR30-NR34 $FF1A-$FF1E, NR41-NR44 $FF20-$FF23,
    /// NR50-NR52 $FF24-$FF26, wave RAM $FF30-$FF3F.
    /// </summary>
    public sealed class GbApu
    {
        private const double ClockRate = 4194304.0;   // DMG CPU / APU t-cycle rate

        // Duty waveforms indexed by the 2-bit NRx1 duty field: one 8-step cycle each (1 = high).
        private static readonly byte[][] DutyTable =
        {
            new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },   // 12.5%
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 1 },   // 25%
            new byte[] { 1, 0, 0, 0, 0, 1, 1, 1 },   // 50%
            new byte[] { 0, 1, 1, 1, 1, 1, 1, 0 }    // 75%
        };
        // Noise frequency-timer base divisors, indexed by the 3-bit NR43 divisor code.
        private static readonly int[] NoiseDivisor = { 8, 16, 32, 48, 64, 80, 96, 112 };

        // Read-or masks for $FF10-$FF3F: bits that always read back as 1 on hardware. NR52 ($FF26) and wave RAM are
        // handled specially in ReadRegister.
        private static readonly byte[] ReadMask =
        {
            0x80, 0x3F, 0x00, 0xFF, 0xBF,   // FF10 NR10 .. FF14 NR14
            0xFF, 0x3F, 0x00, 0xFF, 0xBF,   // FF15 (n/a) .. FF19 NR24
            0x7F, 0xFF, 0x9F, 0xFF, 0xBF,   // FF1A NR30 .. FF1E NR34
            0xFF, 0xFF, 0x00, 0x00, 0xBF,   // FF1F (n/a) .. FF23 NR44
            0x00, 0x00, 0x70, 0xFF, 0xFF,   // FF24 NR50, FF25 NR51, FF26 NR52, FF27-FF28 (n/a)
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF,   // FF29-FF2D (n/a)
            0xFF, 0xFF,                     // FF2E-FF2F (n/a)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // FF30-FF37 wave RAM
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00    // FF38-FF3F wave RAM
        };

        private readonly Square _ch1 = new Square(true);    // has sweep
        private readonly Square _ch2 = new Square(false);
        private readonly Wave _ch3 = new Wave();
        private readonly Noise _ch4 = new Noise();

        private bool _powered = true;
        private int _fsStep;        // 0..7 frame-sequencer position
        private int _fsCounter;     // t-cycles accumulated toward the next 512 Hz step

        private int _leftVol = 7, _rightVol = 7;   // NR50 master volume, 0..7 per side
        private int _panning = 0xF3;               // NR51 routing bits

        private readonly byte[] _regs = new byte[0x30];   // FF10..FF3F last-written, for read-back masking

        private int _sampleRate = 44100;
        private double _cpuPerSample = ClockRate / 44100.0;
        private double _sampleAcc;
        public float[] Samples = new float[8192];
        public int SampleCount;

        private float _hpPrevIn, _hpPrevOut, _lpPrev;

        public int SampleRate
        {
            get => _sampleRate;
            set
            {
                _sampleRate = value <= 8000 ? 44100 : value;
                _cpuPerSample = ClockRate / _sampleRate;
                int want = Math.Max(8192, _sampleRate / 8);
                if (Samples.Length < want) Samples = new float[want];
            }
        }

        // ── register interface ($FF10-$FF3F), routed here by GBMemory ────────────────────────────────────────────────
        public void WriteRegister(ushort addr, byte v)
        {
            int idx = addr - 0xFF10;
            if (idx < 0 || idx >= 0x30) return;

            // Wave RAM is writable regardless of power state.
            if (addr >= 0xFF30 && addr <= 0xFF3F)
            {
                _regs[idx] = v;
                _ch3.SetWaveByte(addr - 0xFF30, v);
                return;
            }

            // With the APU powered off, all sound registers are read-only (writes ignored) except NR52 itself.
            if (!_powered && addr != 0xFF26) return;

            _regs[idx] = v;
            switch (addr)
            {
                case 0xFF10: _ch1.WriteSweep(v); break;
                case 0xFF11: _ch1.WriteDutyLength(v); break;
                case 0xFF12: _ch1.WriteEnvelope(v); break;
                case 0xFF13: _ch1.WriteFreqLo(v); break;
                case 0xFF14: _ch1.WriteFreqHiTrigger(v); break;

                case 0xFF16: _ch2.WriteDutyLength(v); break;
                case 0xFF17: _ch2.WriteEnvelope(v); break;
                case 0xFF18: _ch2.WriteFreqLo(v); break;
                case 0xFF19: _ch2.WriteFreqHiTrigger(v); break;

                case 0xFF1A: _ch3.WriteDacPower(v); break;
                case 0xFF1B: _ch3.WriteLength(v); break;
                case 0xFF1C: _ch3.WriteVolume(v); break;
                case 0xFF1D: _ch3.WriteFreqLo(v); break;
                case 0xFF1E: _ch3.WriteFreqHiTrigger(v); break;

                case 0xFF20: _ch4.WriteLength(v); break;
                case 0xFF21: _ch4.WriteEnvelope(v); break;
                case 0xFF22: _ch4.WritePoly(v); break;
                case 0xFF23: _ch4.WriteTrigger(v); break;

                case 0xFF24: _leftVol = (v >> 4) & 7; _rightVol = v & 7; break;
                case 0xFF25: _panning = v; break;
                case 0xFF26: WritePower(v); break;
            }
        }

        public byte ReadRegister(ushort addr)
        {
            int idx = addr - 0xFF10;
            if (idx < 0 || idx >= 0x30) return 0xFF;

            if (addr == 0xFF26)
            {
                byte s = 0x70;   // bits 4-6 read as 1
                if (_powered) s |= 0x80;
                if (_ch1.Enabled) s |= 0x01;
                if (_ch2.Enabled) s |= 0x02;
                if (_ch3.Enabled) s |= 0x04;
                if (_ch4.Enabled) s |= 0x08;
                return s;
            }
            if (addr >= 0xFF30) return _ch3.GetWaveByte(addr - 0xFF30);
            return (byte)(_regs[idx] | ReadMask[idx]);
        }

        private void WritePower(byte v)
        {
            bool on = (v & 0x80) != 0;
            if (on && !_powered)
            {
                _powered = true;
                _fsStep = 0;
                _fsCounter = 0;
            }
            else if (!on && _powered)
            {
                _powered = false;
                _ch1.Reset(); _ch2.Reset(); _ch3.Reset(); _ch4.Reset();
                _leftVol = _rightVol = 0;
                _panning = 0;
                for (int i = 0; i <= 0x15; i++) _regs[i] = 0;   // FF10..FF25 read back 0 while off
            }
        }

        // ── clock: advance by a batch of CPU t-cycles, emitting output samples along the way ─────────────────────────
        public void Clock(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                _ch1.ClockTimer(); _ch2.ClockTimer(); _ch3.ClockTimer(); _ch4.ClockTimer();

                if (++_fsCounter >= 8192)   // 4194304 / 512 = 8192 t-cycles per frame-sequencer step
                {
                    _fsCounter = 0;
                    StepFrameSequencer();
                }

                _sampleAcc += 1.0;
                if (_sampleAcc >= _cpuPerSample) { _sampleAcc -= _cpuPerSample; EmitSample(); }
            }
        }

        // 8-step sequencer: length @256 Hz (steps 0,2,4,6), sweep @128 Hz (steps 2,6), envelope @64 Hz (step 7).
        private void StepFrameSequencer()
        {
            switch (_fsStep)
            {
                case 0: ClockLength(); break;
                case 2: ClockLength(); _ch1.ClockSweep(); break;
                case 4: ClockLength(); break;
                case 6: ClockLength(); _ch1.ClockSweep(); break;
                case 7: _ch1.ClockEnvelope(); _ch2.ClockEnvelope(); _ch4.ClockEnvelope(); break;
            }
            _fsStep = (_fsStep + 1) & 7;
        }

        private void ClockLength()
        {
            _ch1.ClockLength(); _ch2.ClockLength(); _ch3.ClockLength(); _ch4.ClockLength();
        }

        private void EmitSample()
        {
            float outv = 0f;
            if (_powered)
            {
                // Each channel DAC maps its 0..15 digital level to analog -1..+1; an inactive channel contributes 0.
                float a1 = _ch1.Active ? _ch1.DigitalOutput() / 7.5f - 1f : 0f;
                float a2 = _ch2.Active ? _ch2.DigitalOutput() / 7.5f - 1f : 0f;
                float a3 = _ch3.Active ? _ch3.DigitalOutput() / 7.5f - 1f : 0f;
                float a4 = _ch4.Active ? _ch4.DigitalOutput() / 7.5f - 1f : 0f;

                float left = 0f, right = 0f;
                if ((_panning & 0x10) != 0) left += a1;
                if ((_panning & 0x20) != 0) left += a2;
                if ((_panning & 0x40) != 0) left += a3;
                if ((_panning & 0x80) != 0) left += a4;
                if ((_panning & 0x01) != 0) right += a1;
                if ((_panning & 0x02) != 0) right += a2;
                if ((_panning & 0x04) != 0) right += a3;
                if ((_panning & 0x08) != 0) right += a4;

                left *= (_leftVol + 1) / 8f;
                right *= (_rightVol + 1) / 8f;
                outv = (left + right) * 0.25f;   // mono; single centred channel ~0.5, all four ~1.0
            }

            float hp = outv - _hpPrevIn + 0.996f * _hpPrevOut;   // DC blocker (the DMG output capacitor, ~28 Hz)
            _hpPrevIn = outv; _hpPrevOut = hp;
            _lpPrev += 0.5f * (hp - _lpPrev);                     // gentle low-pass
            float s = _lpPrev;
            if (s > 1f) s = 1f; else if (s < -1f) s = -1f;

            if (SampleCount < Samples.Length) Samples[SampleCount++] = s;
        }

        public void ClearSamples() => SampleCount = 0;

        public void Reset()
        {
            _powered = true;
            _fsStep = 0; _fsCounter = 0;
            _leftVol = _rightVol = 7;
            _panning = 0xF3;
            Array.Clear(_regs, 0, _regs.Length);
            _ch1.Reset(); _ch2.Reset(); _ch3.Reset(); _ch4.Reset();
            _sampleAcc = 0; SampleCount = 0;
            _hpPrevIn = _hpPrevOut = _lpPrev = 0f;
        }

        // ── channels ─────────────────────────────────────────────────────────────────────────────────────────────────

        // Square wave with duty, length, volume envelope and (channel 1 only) frequency sweep.
        private sealed class Square
        {
            private readonly bool _hasSweep;
            public Square(bool hasSweep) { _hasSweep = hasSweep; }

            private int _duty;
            public int Length;
            private bool _lengthEnable;
            private int _initVol, _envDir, _envPeriod;
            private int _freq;                 // 11-bit
            private bool _dacEnabled;
            public bool Enabled;

            private int _sweepPeriod, _sweepShift;
            private bool _sweepNegate, _sweepEnabled;
            private int _sweepTimer, _shadowFreq;

            private int _volume, _envTimer;
            private int _timer, _seq;

            public bool Active => Enabled && _dacEnabled;

            public void WriteSweep(byte v)
            {
                _sweepPeriod = (v >> 4) & 7;
                _sweepNegate = (v & 0x08) != 0;
                _sweepShift = v & 7;
            }
            public void WriteDutyLength(byte v)
            {
                _duty = (v >> 6) & 3;
                Length = 64 - (v & 0x3F);
            }
            public void WriteEnvelope(byte v)
            {
                _initVol = (v >> 4) & 0x0F;
                _envDir = (v & 0x08) != 0 ? 1 : -1;
                _envPeriod = v & 7;
                _dacEnabled = (v & 0xF8) != 0;
                if (!_dacEnabled) Enabled = false;
            }
            public void WriteFreqLo(byte v) => _freq = (_freq & 0x700) | v;
            public void WriteFreqHiTrigger(byte v)
            {
                _freq = (_freq & 0xFF) | ((v & 7) << 8);
                _lengthEnable = (v & 0x40) != 0;
                if ((v & 0x80) != 0) Trigger();
            }

            private void Trigger()
            {
                Enabled = _dacEnabled;
                if (Length == 0) Length = 64;
                _timer = (2048 - _freq) * 4;
                _volume = _initVol;
                _envTimer = _envPeriod;
                if (_hasSweep)
                {
                    _shadowFreq = _freq;
                    _sweepTimer = _sweepPeriod != 0 ? _sweepPeriod : 8;
                    _sweepEnabled = _sweepPeriod != 0 || _sweepShift != 0;
                    if (_sweepShift != 0) CalcSweep();   // immediate overflow check can disable the channel
                }
            }

            public void ClockTimer()
            {
                if (--_timer <= 0)
                {
                    _timer = (2048 - _freq) * 4;
                    _seq = (_seq + 1) & 7;
                }
            }
            public void ClockLength()
            {
                if (_lengthEnable && Length > 0 && --Length == 0) Enabled = false;
            }
            public void ClockEnvelope()
            {
                if (_envPeriod == 0) return;
                if (--_envTimer <= 0)
                {
                    _envTimer = _envPeriod;
                    int nv = _volume + _envDir;
                    if (nv >= 0 && nv <= 15) _volume = nv;
                }
            }
            public void ClockSweep()
            {
                if (!_hasSweep || --_sweepTimer > 0) return;
                _sweepTimer = _sweepPeriod != 0 ? _sweepPeriod : 8;
                if (_sweepEnabled && _sweepPeriod != 0)
                {
                    int nf = CalcSweep();
                    if (nf <= 2047 && _sweepShift != 0)
                    {
                        _shadowFreq = nf;
                        _freq = nf;
                        CalcSweep();   // second overflow check with the new shadow
                    }
                }
            }
            private int CalcSweep()
            {
                int delta = _shadowFreq >> _sweepShift;
                int nf = _sweepNegate ? _shadowFreq - delta : _shadowFreq + delta;
                if (nf > 2047) Enabled = false;
                return nf;
            }

            public int DigitalOutput() => DutyTable[_duty][_seq] == 1 ? _volume : 0;

            public void Reset()
            {
                _duty = 0; Length = 0; _lengthEnable = false;
                _initVol = 0; _envDir = -1; _envPeriod = 0; _freq = 0;
                _dacEnabled = false; Enabled = false;
                _sweepPeriod = _sweepShift = 0; _sweepNegate = _sweepEnabled = false;
                _sweepTimer = _shadowFreq = 0;
                _volume = _envTimer = 0; _timer = 0; _seq = 0;
            }
        }

        // Wave channel: plays 32 4-bit samples from wave RAM with a coarse volume shift.
        private sealed class Wave
        {
            public int Length;
            private bool _lengthEnable;
            private bool _dacEnabled;
            private int _volumeCode;   // 0 = mute, 1 = 100%, 2 = 50%, 3 = 25%
            private int _freq;
            public bool Enabled;
            private int _timer, _pos, _sample;
            private readonly byte[] _wave = new byte[32];   // 32 nibbles (high nibble first)

            public bool Active => Enabled && _dacEnabled;

            public void SetWaveByte(int index, byte v)
            {
                _wave[index * 2] = (byte)(v >> 4);
                _wave[index * 2 + 1] = (byte)(v & 0x0F);
            }
            public byte GetWaveByte(int index) => (byte)((_wave[index * 2] << 4) | _wave[index * 2 + 1]);

            public void WriteDacPower(byte v) { _dacEnabled = (v & 0x80) != 0; if (!_dacEnabled) Enabled = false; }
            public void WriteLength(byte v) => Length = 256 - v;
            public void WriteVolume(byte v) => _volumeCode = (v >> 5) & 3;
            public void WriteFreqLo(byte v) => _freq = (_freq & 0x700) | v;
            public void WriteFreqHiTrigger(byte v)
            {
                _freq = (_freq & 0xFF) | ((v & 7) << 8);
                _lengthEnable = (v & 0x40) != 0;
                if ((v & 0x80) != 0) Trigger();
            }

            private void Trigger()
            {
                Enabled = _dacEnabled;
                if (Length == 0) Length = 256;
                _timer = (2048 - _freq) * 2;
                _pos = 0;
            }

            public void ClockTimer()
            {
                if (--_timer <= 0)
                {
                    _timer = (2048 - _freq) * 2;
                    _pos = (_pos + 1) & 31;
                    _sample = _wave[_pos];
                }
            }
            public void ClockLength()
            {
                if (_lengthEnable && Length > 0 && --Length == 0) Enabled = false;
            }

            public int DigitalOutput()
            {
                int shift = _volumeCode == 0 ? 4 : _volumeCode - 1;   // 0->mute, 1->>>0, 2->>>1, 3->>>2
                return _sample >> shift;
            }

            public void Reset()
            {
                Length = 0; _lengthEnable = false; _dacEnabled = false; _volumeCode = 0;
                _freq = 0; Enabled = false; _timer = 0; _pos = 0; _sample = 0;
                Array.Clear(_wave, 0, _wave.Length);
            }
        }

        // Noise channel: pseudo-random LFSR (15- or 7-bit) with a volume envelope.
        private sealed class Noise
        {
            public int Length;
            private bool _lengthEnable;
            private int _initVol, _envDir, _envPeriod;
            private bool _dacEnabled;
            public bool Enabled;
            private int _volume, _envTimer;
            private int _divisorCode, _clockShift;
            private bool _widthMode;
            private int _timer;
            private int _lfsr = 0x7FFF;

            public bool Active => Enabled && _dacEnabled;

            public void WriteLength(byte v) => Length = 64 - (v & 0x3F);
            public void WriteEnvelope(byte v)
            {
                _initVol = (v >> 4) & 0x0F;
                _envDir = (v & 0x08) != 0 ? 1 : -1;
                _envPeriod = v & 7;
                _dacEnabled = (v & 0xF8) != 0;
                if (!_dacEnabled) Enabled = false;
            }
            public void WritePoly(byte v)
            {
                _clockShift = (v >> 4) & 0x0F;
                _widthMode = (v & 0x08) != 0;
                _divisorCode = v & 7;
            }
            public void WriteTrigger(byte v)
            {
                _lengthEnable = (v & 0x40) != 0;
                if ((v & 0x80) != 0) Trigger();
            }

            private void Trigger()
            {
                Enabled = _dacEnabled;
                if (Length == 0) Length = 64;
                _timer = NoiseDivisor[_divisorCode] << _clockShift;
                _volume = _initVol;
                _envTimer = _envPeriod;
                _lfsr = 0x7FFF;
            }

            public void ClockTimer()
            {
                if (--_timer <= 0)
                {
                    _timer = NoiseDivisor[_divisorCode] << _clockShift;
                    int fb = (_lfsr ^ (_lfsr >> 1)) & 1;
                    _lfsr = (_lfsr >> 1) | (fb << 14);
                    if (_widthMode) { _lfsr &= ~(1 << 6); _lfsr |= fb << 6; }
                }
            }
            public void ClockLength()
            {
                if (_lengthEnable && Length > 0 && --Length == 0) Enabled = false;
            }
            public void ClockEnvelope()
            {
                if (_envPeriod == 0) return;
                if (--_envTimer <= 0)
                {
                    _envTimer = _envPeriod;
                    int nv = _volume + _envDir;
                    if (nv >= 0 && nv <= 15) _volume = nv;
                }
            }

            public int DigitalOutput() => (_lfsr & 1) == 0 ? _volume : 0;

            public void Reset()
            {
                Length = 0; _lengthEnable = false;
                _initVol = 0; _envDir = -1; _envPeriod = 0;
                _dacEnabled = false; Enabled = false;
                _volume = _envTimer = 0;
                _divisorCode = _clockShift = 0; _widthMode = false;
                _timer = 0; _lfsr = 0x7FFF;
            }
        }
    }
}
