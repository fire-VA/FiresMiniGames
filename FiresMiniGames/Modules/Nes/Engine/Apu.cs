namespace FiresMiniGames.Modules.Nes.Engine
{
    using System;

    /// <summary>
    /// The NES audio processing unit — the sound chip the vendored NET-NES core shipped without, so every ROM ran
    /// silent. This is a from-scratch 2A03 APU: two pulse channels, triangle, and noise (DMC/sample playback is the
    /// one omission), each with the real envelope / length-counter / sweep / linear-counter behaviour, driven by the
    /// 240 Hz frame sequencer. It is clocked in CPU cycles by the bus, mixes the four channels with the hardware's
    /// non-linear formula, runs a DC-blocking high-pass plus a gentle low-pass, and appends output samples at a host
    /// chosen rate into <see cref="Samples"/> for the Unity side to hand to an AudioSource. UnityEngine-free so the
    /// nes_proof harness can render a WAV of it headlessly.
    /// </summary>
    public sealed class Apu
    {
        private const double CpuRate = 1789773.0;   // NTSC 2A03

        // Length-counter load values, indexed by the 5-bit field written to $4003/$4007/$400B/$400F.
        private static readonly byte[] LengthTable =
        {
            10, 254, 20,  2, 40,  4, 80,  6, 160,  8, 60, 10, 14, 12, 26, 14,
            12,  16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
        };
        private static readonly byte[][] DutyTable =
        {
            new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 },   // 12.5%
            new byte[] { 0, 1, 1, 0, 0, 0, 0, 0 },   // 25%
            new byte[] { 0, 1, 1, 1, 1, 0, 0, 0 },   // 50%
            new byte[] { 1, 0, 0, 1, 1, 1, 1, 1 }    // 25% negated
        };
        private static readonly byte[] TriSeq =
        {
            15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
        };
        private static readonly int[] NoiseTable =   // NTSC period reload values
        { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };

        private readonly Pulse _p1 = new Pulse(false);
        private readonly Pulse _p2 = new Pulse(true);
        private readonly Triangle _tri = new Triangle();
        private readonly Noise _noise = new Noise();
        private readonly Dmc _dmc = new Dmc();

        private CPU _cpu;                 // for the frame IRQ line
        private bool _apuCycle;           // pulse/noise timers tick every other CPU cycle
        private int _frameCycle;
        private bool _fiveStep;
        private bool _irqInhibit;
        public bool FrameInterrupt;

        private int _sampleRate = 44100;
        private double _cpuPerSample = CpuRate / 44100.0;
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
                _cpuPerSample = CpuRate / _sampleRate;
                int want = Math.Max(8192, _sampleRate / 8);
                if (Samples.Length < want) Samples = new float[want];
            }
        }

        public void AttachCpu(CPU cpu) { _cpu = cpu; _dmc.AttachCpu(cpu); }
        public void AttachBus(Bus bus) => _dmc.AttachBus(bus);   // DMC reads sample bytes from CPU memory

        // ── register interface ($4000-$4017), routed here by the bus ────────────────────────────────────────────
        public void WriteRegister(ushort addr, byte v)
        {
            switch (addr)
            {
                case 0x4000: _p1.WriteCtrl(v); break;
                case 0x4001: _p1.WriteSweep(v); break;
                case 0x4002: _p1.WriteTimerLo(v); break;
                case 0x4003: _p1.WriteTimerHi(v, LengthTable); break;
                case 0x4004: _p2.WriteCtrl(v); break;
                case 0x4005: _p2.WriteSweep(v); break;
                case 0x4006: _p2.WriteTimerLo(v); break;
                case 0x4007: _p2.WriteTimerHi(v, LengthTable); break;
                case 0x4008: _tri.WriteCtrl(v); break;
                case 0x400A: _tri.WriteTimerLo(v); break;
                case 0x400B: _tri.WriteTimerHi(v, LengthTable); break;
                case 0x400C: _noise.WriteCtrl(v); break;
                case 0x400E: _noise.WriteMode(v, NoiseTable); break;
                case 0x400F: _noise.WriteLength(v, LengthTable); break;
                case 0x4010: _dmc.Write4010(v); break;
                case 0x4011: _dmc.Write4011(v); break;
                case 0x4012: _dmc.Write4012(v); break;
                case 0x4013: _dmc.Write4013(v); break;
                case 0x4015: WriteStatus(v); break;
                case 0x4017: WriteFrameCounter(v); break;
            }
        }

        private void WriteStatus(byte v)
        {
            _p1.SetEnabled((v & 0x01) != 0);
            _p2.SetEnabled((v & 0x02) != 0);
            _tri.SetEnabled((v & 0x04) != 0);
            _noise.SetEnabled((v & 0x08) != 0);
            _dmc.SetEnabled((v & 0x10) != 0);
            _dmc.Interrupt = false;   // writing $4015 clears the DMC interrupt flag
        }

        public byte ReadStatus()
        {
            byte s = 0;
            if (_p1.Length > 0) s |= 0x01;
            if (_p2.Length > 0) s |= 0x02;
            if (_tri.Length > 0) s |= 0x04;
            if (_noise.Length > 0) s |= 0x08;
            if (_dmc.Active) s |= 0x10;
            if (FrameInterrupt) s |= 0x40;
            if (_dmc.Interrupt) s |= 0x80;
            FrameInterrupt = false;   // reading $4015 clears the frame flag (but NOT the DMC interrupt flag)
            return s;
        }

        private void WriteFrameCounter(byte v)
        {
            _fiveStep = (v & 0x80) != 0;
            _irqInhibit = (v & 0x40) != 0;
            if (_irqInhibit) FrameInterrupt = false;
            _frameCycle = 0;
            if (_fiveStep) { ClockQuarter(); ClockHalf(); }   // 5-step mode clocks immediately on write
        }

        // ── clock: advance by a batch of CPU cycles, emitting output samples along the way ───────────────────────
        public void Clock(int cpuCycles)
        {
            for (int i = 0; i < cpuCycles; i++)
            {
                _tri.ClockTimer();
                _dmc.Clock();
                _apuCycle = !_apuCycle;
                if (_apuCycle) { _p1.ClockTimer(); _p2.ClockTimer(); _noise.ClockTimer(); }

                StepFrameSequencer();

                _sampleAcc += 1.0;
                if (_sampleAcc >= _cpuPerSample) { _sampleAcc -= _cpuPerSample; EmitSample(); }
            }
        }

        private void StepFrameSequencer()
        {
            _frameCycle++;
            if (!_fiveStep)
            {
                switch (_frameCycle)
                {
                    case 7457: ClockQuarter(); break;
                    case 14913: ClockQuarter(); ClockHalf(); break;
                    case 22371: ClockQuarter(); break;
                    case 29829:
                        ClockQuarter(); ClockHalf();
                        if (!_irqInhibit) { FrameInterrupt = true; _cpu?.RequestIRQ(true); }
                        _frameCycle = 0;
                        break;
                }
            }
            else
            {
                switch (_frameCycle)
                {
                    case 7457: ClockQuarter(); break;
                    case 14913: ClockQuarter(); ClockHalf(); break;
                    case 22371: ClockQuarter(); break;
                    case 37281: ClockQuarter(); ClockHalf(); _frameCycle = 0; break;
                }
            }
        }

        private void ClockQuarter()   // envelopes + triangle linear counter (240 Hz)
        {
            _p1.ClockEnvelope(); _p2.ClockEnvelope(); _noise.ClockEnvelope(); _tri.ClockLinear();
        }

        private void ClockHalf()      // length counters + sweeps (120 Hz)
        {
            _p1.ClockLength(); _p1.ClockSweep(); _p2.ClockLength(); _p2.ClockSweep();
            _tri.ClockLength(); _noise.ClockLength();
        }

        private void EmitSample()
        {
            int p1 = _p1.Output(), p2 = _p2.Output();
            int t = _tri.Output(), n = _noise.Output(), d = _dmc.Output;

            double pulse = (p1 + p2) == 0 ? 0.0 : 95.88 / (8128.0 / (p1 + p2) + 100.0);
            double tnd = (t == 0 && n == 0 && d == 0) ? 0.0
                : 159.79 / (1.0 / (t / 8227.0 + n / 12241.0 + d / 22638.0) + 100.0);
            float raw = (float)(pulse + tnd);       // ~0..1

            float hp = raw - _hpPrevIn + 0.9963f * _hpPrevOut;   // DC blocker (~37 Hz)
            _hpPrevIn = raw; _hpPrevOut = hp;
            _lpPrev += 0.55f * (hp - _lpPrev);                    // gentle low-pass
            float outv = _lpPrev * 1.3f;
            if (outv > 1f) outv = 1f; else if (outv < -1f) outv = -1f;

            if (SampleCount < Samples.Length) Samples[SampleCount++] = outv;
        }

        public void ClearSamples() => SampleCount = 0;

        // ── channels ─────────────────────────────────────────────────────────────────────────────────────────────
        private sealed class Pulse
        {
            private readonly bool _isPulse2;
            public Pulse(bool isPulse2) { _isPulse2 = isPulse2; }

            private int _duty;
            private bool _halt, _constVol;
            private int _vol;
            private bool _envStart;
            private int _envDivider, _envDecay;

            private bool _sweepEnabled, _sweepNegate, _sweepReload;
            private int _sweepPeriod, _sweepShift, _sweepDivider;

            private int _timerPeriod, _timerCounter, _seqStep;
            public int Length;
            private bool _enabled;

            public void WriteCtrl(byte v)
            {
                _duty = (v >> 6) & 3;
                _halt = (v & 0x20) != 0;
                _constVol = (v & 0x10) != 0;
                _vol = v & 0x0F;
            }
            public void WriteSweep(byte v)
            {
                _sweepEnabled = (v & 0x80) != 0;
                _sweepPeriod = (v >> 4) & 7;
                _sweepNegate = (v & 0x08) != 0;
                _sweepShift = v & 7;
                _sweepReload = true;
            }
            public void WriteTimerLo(byte v) => _timerPeriod = (_timerPeriod & 0x700) | v;
            public void WriteTimerHi(byte v, byte[] lengthTable)
            {
                _timerPeriod = (_timerPeriod & 0xFF) | ((v & 7) << 8);
                if (_enabled) Length = lengthTable[(v >> 3) & 0x1F];
                _seqStep = 0;
                _envStart = true;
            }
            public void SetEnabled(bool e) { _enabled = e; if (!e) Length = 0; }

            public void ClockTimer()
            {
                if (_timerCounter <= 0) { _timerCounter = _timerPeriod; _seqStep = (_seqStep + 1) & 7; }
                else _timerCounter--;
            }
            public void ClockEnvelope()
            {
                if (_envStart) { _envStart = false; _envDecay = 15; _envDivider = _vol; }
                else if (_envDivider <= 0)
                {
                    _envDivider = _vol;
                    if (_envDecay > 0) _envDecay--;
                    else if (_halt) _envDecay = 15;
                }
                else _envDivider--;
            }
            public void ClockLength() { if (!_halt && Length > 0) Length--; }

            private int SweepTarget()
            {
                int change = _timerPeriod >> _sweepShift;
                if (_sweepNegate) { change = -change; if (!_isPulse2) change -= 1; }   // pulse 1 uses ones' complement
                return _timerPeriod + change;
            }
            public void ClockSweep()
            {
                int target = SweepTarget();
                if (_sweepDivider <= 0 && _sweepEnabled && _sweepShift > 0 && _timerPeriod >= 8 && target <= 0x7FF)
                    _timerPeriod = target;
                if (_sweepDivider <= 0 || _sweepReload) { _sweepDivider = _sweepPeriod; _sweepReload = false; }
                else _sweepDivider--;
            }

            public int Output()
            {
                if (!_enabled || Length == 0 || DutyTable[_duty][_seqStep] == 0) return 0;
                if (_timerPeriod < 8 || SweepTarget() > 0x7FF) return 0;   // sweep mute
                return _constVol ? _vol : _envDecay;
            }
        }

        private sealed class Triangle
        {
            private bool _control;              // length halt + linear control
            private int _linearReload, _linearCounter;
            private bool _linearReloadFlag;
            private int _timerPeriod, _timerCounter, _seqStep;
            public int Length;
            private bool _enabled;

            public void WriteCtrl(byte v) { _control = (v & 0x80) != 0; _linearReload = v & 0x7F; }
            public void WriteTimerLo(byte v) => _timerPeriod = (_timerPeriod & 0x700) | v;
            public void WriteTimerHi(byte v, byte[] lengthTable)
            {
                _timerPeriod = (_timerPeriod & 0xFF) | ((v & 7) << 8);
                if (_enabled) Length = lengthTable[(v >> 3) & 0x1F];
                _linearReloadFlag = true;
            }
            public void SetEnabled(bool e) { _enabled = e; if (!e) Length = 0; }

            public void ClockTimer()
            {
                if (_timerCounter <= 0)
                {
                    _timerCounter = _timerPeriod;
                    if (_linearCounter > 0 && Length > 0) _seqStep = (_seqStep + 1) & 31;
                }
                else _timerCounter--;
            }
            public void ClockLinear()
            {
                if (_linearReloadFlag) _linearCounter = _linearReload;
                else if (_linearCounter > 0) _linearCounter--;
                if (!_control) _linearReloadFlag = false;
            }
            public void ClockLength() { if (!_control && Length > 0) Length--; }

            public int Output()
            {
                if (_timerPeriod < 2) return 7;   // hold mid on ultrasonic periods to avoid pops
                return TriSeq[_seqStep];
            }
        }

        private sealed class Noise
        {
            private bool _halt, _constVol;
            private int _vol;
            private bool _envStart;
            private int _envDivider, _envDecay;
            private bool _mode;
            private int _timerPeriod, _timerCounter;
            private int _shift = 1;
            public int Length;
            private bool _enabled;

            public void WriteCtrl(byte v) { _halt = (v & 0x20) != 0; _constVol = (v & 0x10) != 0; _vol = v & 0x0F; }
            public void WriteMode(byte v, int[] noiseTable) { _mode = (v & 0x80) != 0; _timerPeriod = noiseTable[v & 0x0F]; }
            public void WriteLength(byte v, byte[] lengthTable) { if (_enabled) Length = lengthTable[(v >> 3) & 0x1F]; _envStart = true; }
            public void SetEnabled(bool e) { _enabled = e; if (!e) Length = 0; }

            public void ClockTimer()
            {
                if (_timerCounter <= 0)
                {
                    _timerCounter = _timerPeriod;
                    int fb = (_shift & 1) ^ (_mode ? ((_shift >> 6) & 1) : ((_shift >> 1) & 1));
                    _shift = (_shift >> 1) | (fb << 14);
                }
                else _timerCounter--;
            }
            public void ClockEnvelope()
            {
                if (_envStart) { _envStart = false; _envDecay = 15; _envDivider = _vol; }
                else if (_envDivider <= 0)
                {
                    _envDivider = _vol;
                    if (_envDecay > 0) _envDecay--;
                    else if (_halt) _envDecay = 15;
                }
                else _envDivider--;
            }
            public void ClockLength() { if (!_halt && Length > 0) Length--; }

            public int Output()
            {
                if (!_enabled || Length == 0 || (_shift & 1) != 0) return 0;
                return _constVol ? _vol : _envDecay;
            }
        }

        // Delta-modulation channel — plays 1-bit-encoded PCM samples read from CPU memory ($C000+). We do NOT steal
        // CPU cycles for the DMA fetch (a cycle-accuracy nicety, not audible), reading the byte straight through the
        // bus instead; everything else follows the hardware output unit.
        private sealed class Dmc
        {
            private static readonly int[] RateTable =   // NTSC: CPU cycles per output-unit clock
            { 428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54 };

            private Bus _bus;
            private CPU _cpu;
            public void AttachBus(Bus bus) => _bus = bus;
            public void AttachCpu(CPU cpu) => _cpu = cpu;

            private bool _irqEnabled, _loop;
            private int _rate = 428;
            private int _timer;
            public int Output;                    // 7-bit DAC, 0..127 — feeds the tnd mix

            private int _sampleAddr = 0xC000, _sampleLen = 1;
            private int _curAddr = 0xC000, _bytesRemaining;
            private int _sampleBuffer = -1;       // -1 = empty
            private int _shift, _bitsRemaining;
            private bool _silence = true;

            public bool Interrupt;
            public bool Active => _bytesRemaining > 0;

            public void Write4010(byte v)
            {
                _irqEnabled = (v & 0x80) != 0;
                _loop = (v & 0x40) != 0;
                _rate = RateTable[v & 0x0F];
                if (!_irqEnabled) Interrupt = false;
            }
            public void Write4011(byte v) => Output = v & 0x7F;
            public void Write4012(byte v) => _sampleAddr = 0xC000 + (v << 6);
            public void Write4013(byte v) => _sampleLen = (v << 4) + 1;

            public void SetEnabled(bool e)
            {
                if (!e) _bytesRemaining = 0;
                else if (_bytesRemaining == 0) { _curAddr = _sampleAddr; _bytesRemaining = _sampleLen; }
            }

            private void FillBuffer()
            {
                if (_sampleBuffer >= 0 || _bytesRemaining == 0 || _bus == null) return;
                _sampleBuffer = _bus.Read((ushort)_curAddr);
                _curAddr = _curAddr == 0xFFFF ? 0x8000 : _curAddr + 1;
                _bytesRemaining--;
                if (_bytesRemaining == 0)
                {
                    if (_loop) { _curAddr = _sampleAddr; _bytesRemaining = _sampleLen; }
                    else if (_irqEnabled) { Interrupt = true; _cpu?.RequestIRQ(true); }
                }
            }

            public void Clock()
            {
                FillBuffer();
                if (_timer > 0) { _timer--; return; }
                _timer = _rate;

                if (!_silence)
                {
                    if ((_shift & 1) == 1) { if (Output <= 125) Output += 2; }
                    else { if (Output >= 2) Output -= 2; }
                }
                _shift >>= 1;
                if (_bitsRemaining > 0) _bitsRemaining--;
                if (_bitsRemaining == 0)
                {
                    _bitsRemaining = 8;
                    if (_sampleBuffer < 0) _silence = true;
                    else { _silence = false; _shift = _sampleBuffer; _sampleBuffer = -1; }
                }
            }
        }
    }
}
