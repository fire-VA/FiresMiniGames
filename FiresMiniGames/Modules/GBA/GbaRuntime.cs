using System;
using System.IO;
using UnityEngine;
using sGBA;
using FiresMiniGames.Modules.Common;
using FiresMiniGames.Modules.Nes;   // reuse the NES audio sink (a generic mono SPSC ring + OnAudioFilterRead)

namespace FiresMiniGames.Modules.GBA
{
    /// <summary>
    /// Owns one running Game Boy Advance: builds the vendored sGBA core (GPL-3.0, Asphaltian/sGBA) from a ROM's
    /// bytes, runs it at the handheld's native ~59.73 Hz field rate (one 280896-cycle frame per Tick), and paints
    /// each finished 240x160 frame into a point-filtered Texture2D so a TV can display it (IScreenConsole, exactly
    /// like NesRuntime / GbRuntime / DoomRuntime). The HLE BIOS boots the cart with no BIOS file. The ten buttons
    /// come from the shared GamepadKeys reader (the eight NES buttons) plus Q/E for the L/R shoulders, mapped into
    /// the core's GbaKey flags via Gba.SetKeyState.
    ///
    /// This is the CORE-PORT host: the vendored GbaVideo.Rendering.cs is a STUB (test-pattern framebuffer), so real
    /// games run their CPU/BIOS/DMA/audio but display only a moving test card until the real software PPU lands.
    /// Audio, however, is fully wired: the core clocks GbaAudio into Gba.Audio.OutputBuffer (interleaved stereo at a
    /// fixed 32768 Hz) every frame; each frame we downmix that to mono, resample 32768 Hz -> the Unity device output
    /// rate, and stream it to a looping AudioSource through the shared NesAudioSink, exactly as NesRuntime/GbRuntime do.
    /// </summary>
    public sealed class GbaRuntime : IScreenConsole
    {
        private const int W = GbaConstants.ScreenWidth;    // 240
        private const int H = GbaConstants.ScreenHeight;   // 160
        private const double FrameSeconds = 1.0 / 59.7275; // GBA field rate

        private Gba _gba;
        private Texture2D _tex;
        private Color32[] _pixels;
        private double _acc;

        private GameObject _audioGo;
        private NesAudioSink _sink;
        private object _audioSource;
        private int _outRate;         // Unity device output rate (Hz) — what OnAudioFilterRead pulls at
        private float[] _mono;        // per-frame stereo->mono downmix scratch, still at the APU's 32768 Hz
        private float[] _resampled;   // that block resampled up to _outRate, ready for the ring
        private double _resamplePos;  // fractional read cursor into the 32768 Hz block; carries the phase across frames

        private readonly string _savePath;   // <rom>.sav for battery carts (null = no persistence)
        private float _flushT;

        public bool Available { get; private set; }
        public string Error { get; private set; }
        public Texture2D Texture => _tex;

        public GbaRuntime(byte[] rom, string savePath = null)
        {
            _savePath = savePath;
            try
            {
                sGBA.Log.Sink = s => Debug.Log($"[{FiresMiniGames.PluginName}] GBA: {s}");
                GbaLog.DefaultLevels = LogLevel.Fatal | LogLevel.Error | LogLevel.Warn;

                _gba = new Gba();
                _gba.LoadRom(rom);   // load cart, detect savedata type, init RTC
                _gba.Reset();        // power-on state + HLE BIOS install + skip-BIOS to the cart entry
                LoadSave();          // restore battery SRAM/Flash/EEPROM before the game reads it

                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "gba_screen"
                };
                _pixels = new Color32[W * H];

                RunFrame();   // frame 0 so the screen isn't black before the first Tick
                Blit();
                StartAudio();   // frame 0's samples are never pumped, so the boot transient is discarded for free

                Available = true;
                Debug.Log($"[{FiresMiniGames.PluginName}] Game Boy Advance cartridge running " +
                          $"({GbaConstants.VideoTotalLength} cyc/frame, {W}x{H} stub renderer, " +
                          $"{GbaAudio.SampleRate} Hz audio).");
            }
            catch (Exception e)
            {
                Error = e.Message;
                Available = false;
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy Advance init failed: {e.Message}");
            }
        }

        public void Tick(float dt)
        {
            if (!Available) return;
            ApplyInput();
            _acc += Mathf.Min(dt, 0.1f);
            int ran = 0;
            while (_acc >= FrameSeconds && ran < 4) { RunFrame(); PumpAudio(); _acc -= FrameSeconds; ran++; }
            if (ran > 0) Blit();
            _flushT += dt;                       // insurance flush in case a hard exit skips Dispose
            if (_flushT >= 15f) { SaveSave(); _flushT = 0f; }
        }

        private void RunFrame()
        {
            // Gba.RunFrame runs the CPU/DMA/timers until the video timing advances FrameCounter by one — one full
            // 280896-cycle field. The stub renderer commits its test-pattern framebuffer during that frame's VBlank.
            // It also calls Audio.BeginFrame() at the top (resetting SamplesWritten to 0) and clocks the audio sample
            // events throughout, so on return Gba.Audio.OutputBuffer holds SamplesWritten fresh stereo pairs.
            _gba.RunFrame();
        }

        private void StartAudio()
        {
            if (!FiresAudio.Available) return;
            try
            {
                // The sGBA APU is hard-wired to 32768 Hz (GbaAudio.SampleRate is a const the frame-buffer length is
                // derived from — unlike the Game Boy APU, whose SampleRate we could just retarget to the device rate).
                // Unity's OnAudioFilterRead always pulls at the device output rate and the shared NesAudioSink drains
                // its ring one sample per output frame, so the ring must hold device-rate samples. GbRuntime satisfies
                // that by making its emulator produce at the output rate; we can't, so we resample 32768 -> _outRate in
                // PumpAudio. Everything downstream — the mono ring, the looping silent AudioSource that keeps the filter
                // callback firing, the disposal — is identical to GbRuntime.
                _outRate = FiresAudio.OutputSampleRate;
                _audioGo = new GameObject("gba_audio");
                _sink = _audioGo.AddComponent<NesAudioSink>();
                _sink.Init(_outRate / 4);                                // ~0.25 s ring at the device rate, like GbRuntime
                _audioSource = FiresAudio.AddSource(_audioGo, 1f, true);
                var silence = FiresAudio.Clip("gba_silence", new float[Mathf.Max(1024, _outRate / 10)], _outRate);
                FiresAudio.PlayLoop(_audioSource, silence);              // keeps OnAudioFilterRead firing

                _mono = new float[GbaAudio.SamplesPerFrame];
                // One 32768 Hz frame resampled up to the device rate produces at most SamplesPerFrame*_outRate/32768
                // samples; +8 covers the carried fractional sample and rounding.
                int maxOut = (int)((long)GbaAudio.SamplesPerFrame * _outRate / GbaAudio.SampleRate) + 8;
                _resampled = new float[maxOut];
                _resamplePos = 0.0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy Advance audio unavailable: {e.Message}");
                _sink = null;
            }
        }

        private void PumpAudio()
        {
            if (_sink == null) return;
            var apu = _gba.Audio;
            int pairs = apu.SamplesWritten;         // stereo pairs written this frame; BeginFrame reset it to 0 at frame start
            if (pairs <= 0) return;
            if (pairs > _mono.Length) pairs = _mono.Length;   // WriteSample already clamps at SamplesPerFrame; stay defensive

            var buf = apu.OutputBuffer;             // interleaved L,R shorts
            var mono = _mono;
            for (int i = 0; i < pairs; i++)
            {
                // Downmix (average L+R) and short->float in one step: (L+R)/2/32768 == (L+R)/65536.
                mono[i] = (buf[i * 2] + buf[i * 2 + 1]) * (1f / 65536f);
            }

            // Linear resample the 32768 Hz block up to _outRate. step < 1 when upsampling (the usual case). _resamplePos
            // carries the sub-sample phase between frames so consecutive blocks join without a click at the seam.
            double step = (double)GbaAudio.SampleRate / _outRate;
            var outb = _resampled;
            int outCount = 0;
            double pos = _resamplePos;
            while (pos < pairs && outCount < outb.Length)
            {
                int i0 = (int)pos;
                int i1 = i0 + 1 < pairs ? i0 + 1 : i0;   // clamp at the block end; the seam is fixed up by the carry below
                float frac = (float)(pos - i0);
                outb[outCount++] = mono[i0] + (mono[i1] - mono[i0]) * frac;
                pos += step;
            }
            _resamplePos = pos - pairs;              // rebase the cursor onto next frame's block, keeping the fraction
            if (_resamplePos < 0.0) _resamplePos = 0.0;

            _sink.Push(outb, outCount);
        }

        private void ApplyInput()
        {
            byte pad = GamepadKeys.ReadPad();   // 0 A,1 B,2 Select,3 Start,4 Up,5 Down,6 Left,7 Right
            _gba.SetKeyState(GbaKey.A,      (pad & (1 << 0)) != 0);
            _gba.SetKeyState(GbaKey.B,      (pad & (1 << 1)) != 0);
            _gba.SetKeyState(GbaKey.Select, (pad & (1 << 2)) != 0);
            _gba.SetKeyState(GbaKey.Start,  (pad & (1 << 3)) != 0);
            _gba.SetKeyState(GbaKey.Up,     (pad & (1 << 4)) != 0);
            _gba.SetKeyState(GbaKey.Down,   (pad & (1 << 5)) != 0);
            _gba.SetKeyState(GbaKey.Left,   (pad & (1 << 6)) != 0);
            _gba.SetKeyState(GbaKey.Right,  (pad & (1 << 7)) != 0);
            _gba.SetKeyState(GbaKey.L,      Input.GetKey(KeyCode.Q));   // shoulders aren't on the NES pad
            _gba.SetKeyState(GbaKey.R,      Input.GetKey(KeyCode.E));
        }

        private void Blit()
        {
            var buf = _gba.Video.GetFramebuffer();   // 240x160 RGBA, row 0 = top
            var px = _pixels;
            for (int y = 0; y < H; y++)
            {
                int src = y * W * 4;
                int dst = (H - 1 - y) * W;   // Texture2D row 0 is the bottom, so flip vertically
                for (int x = 0; x < W; x++)
                {
                    int s = src + x * 4;
                    px[dst + x] = new Color32(buf[s], buf[s + 1], buf[s + 2], 255);
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }

        private void LoadSave()
        {
            if (_gba == null || string.IsNullOrEmpty(_savePath)) return;
            var data = _gba.Savedata.Data;
            if (data == null || data.Length == 0) return;   // ROM-only cart: nothing to persist
            try
            {
                if (File.Exists(_savePath))
                {
                    var bytes = File.ReadAllBytes(_savePath);
                    if (bytes.Length > 0)
                    {
                        _gba.Savedata.Load(bytes);
                        Debug.Log($"[{FiresMiniGames.PluginName}] Loaded Game Boy Advance save ({_savePath}).");
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] GBA save load failed: {e.Message}"); }
        }

        private void SaveSave()
        {
            if (_gba == null || string.IsNullOrEmpty(_savePath)) return;
            var data = _gba.Savedata.Data;
            if (data == null || data.Length == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_savePath));
                File.WriteAllBytes(_savePath, data);
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] GBA save write failed: {e.Message}"); }
        }

        public void Dispose()
        {
            SaveSave();
            try { FiresAudio.Stop(_audioSource); } catch { }
            if (_audioGo != null) UnityEngine.Object.Destroy(_audioGo);
            _audioGo = null; _sink = null; _audioSource = null;
            _mono = null; _resampled = null;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null;
            _gba = null;
            _pixels = null;
        }
    }
}
