using System;
using System.IO;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Owns one running NES: builds the vendored core from a ROM's bytes, runs it at the console's native
    /// ~60.1 Hz, and paints each finished 256x240 frame into a point-filtered Texture2D. The eight controller
    /// buttons are read from the keyboard here (D-pad = arrows/WASD, A = X/K, B = Z/J, Start = Enter,
    /// Select = Shift) and pushed into the core before each frame. The 2A03 APU's samples are streamed to a
    /// looping AudioSource via <see cref="NesAudioSink"/>, and battery-backed cartridges load/save their SRAM to a
    /// .sav next to the ROM. Implements IScreenConsole so a TV displays it.
    /// </summary>
    public sealed class NesRuntime : IScreenConsole
    {
        private const double FrameSeconds = 1.0 / 60.0988;   // NTSC NES field rate

        private Engine.NES _nes;
        private Texture2D _tex;
        private Color32[] _pixels;
        private double _acc;

        private GameObject _audioGo;
        private NesAudioSink _sink;
        private object _audioSource;
        private readonly string _savePath;
        private bool _dirtySave;
        private float _flushT;

        public bool Available { get; private set; }
        public string Error { get; private set; }
        public Texture2D Texture => _tex;

        public NesRuntime(byte[] rom) : this(rom, null) { }

        public NesRuntime(byte[] rom, string savePath)
        {
            _savePath = savePath;
            try
            {
                _nes = new Engine.NES(rom);
                LoadSram();                                     // must precede boot so the file-select sees the save
                _nes.Apu.SampleRate = FiresAudio.OutputSampleRate;

                _tex = new Texture2D(256, 240, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "nes_screen"
                };
                _pixels = new Color32[256 * 240];

                _nes.Run();      // render frame 0 so the screen isn't black before the first Tick
                _nes.Apu.ClearSamples();   // discard the boot transient
                Blit();
                StartAudio();

                Available = true;
                Debug.Log($"[{FiresMiniGames.PluginName}] NES cartridge running (mapper {_nes.MapperID}, " +
                          $"battery {_nes.HasBattery}, {_nes.Apu.SampleRate} Hz audio).");
            }
            catch (Exception e)
            {
                Error = e.Message;
                Available = false;
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] NES init failed: {e.Message}");
            }
        }

        // Advances the sim in fixed ~60 Hz frames (capped so a hitch can't spiral), streaming each frame's audio and
        // then repainting the texture.
        public void Tick(float dt)
        {
            if (!Available) return;
            byte pad = GamepadKeys.ReadPad();
            _acc += Mathf.Min(dt, 0.1f);
            int ran = 0;
            while (_acc >= FrameSeconds && ran < 4)
            {
                _nes.Input.ControllerState = pad;
                _nes.Run();
                PumpAudio();
                _acc -= FrameSeconds;
                ran++;
            }
            if (ran > 0) Blit();

            // Insurance flush for battery saves in case the session ends by a hard exit that skips Dispose.
            if (_dirtySave)
            {
                _flushT += dt;
                if (_flushT >= 15f) { SaveSram(); _flushT = 0f; }
            }
        }

        private void PumpAudio()
        {
            var apu = _nes.Apu;
            if (_sink != null && apu.SampleCount > 0) _sink.Push(apu.Samples, apu.SampleCount);
            apu.ClearSamples();
            if (_nes.HasBattery) _dirtySave = true;
        }

        private void StartAudio()
        {
            if (!FiresAudio.Available) return;
            try
            {
                int rate = _nes.Apu.SampleRate;
                _audioGo = new GameObject("nes_audio");
                _sink = _audioGo.AddComponent<NesAudioSink>();
                _sink.Init(rate / 4);                                   // ~0.25 s ring
                _audioSource = FiresAudio.AddSource(_audioGo, 1f, true);
                var silence = FiresAudio.Clip("nes_silence", new float[Mathf.Max(1024, rate / 10)], rate);
                FiresAudio.PlayLoop(_audioSource, silence);             // keeps OnAudioFilterRead firing
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] NES audio unavailable: {e.Message}");
                _sink = null;
            }
        }

        private void LoadSram()
        {
            if (!_nes.HasBattery || string.IsNullOrEmpty(_savePath)) return;
            try
            {
                if (File.Exists(_savePath))
                {
                    byte[] sav = File.ReadAllBytes(_savePath);
                    var sram = _nes.Sram;
                    Array.Copy(sav, 0, sram, 0, Math.Min(sav.Length, sram.Length));
                    Debug.Log($"[{FiresMiniGames.PluginName}] Loaded NES save ({sav.Length} bytes).");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] NES save load failed: {e.Message}"); }
        }

        private void SaveSram()
        {
            if (!_nes.HasBattery || string.IsNullOrEmpty(_savePath)) return;
            try
            {
                File.WriteAllBytes(_savePath, _nes.Sram);
                _dirtySave = false;
                Debug.Log($"[{FiresMiniGames.PluginName}] Saved NES cartridge RAM ({_nes.Sram.Length} bytes).");
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] NES save write failed: {e.Message}"); }
        }

        private void Blit()
        {
            var fb = _nes.FrameBuffer;          // row 0 = top scanline
            var px = _pixels;
            for (int y = 0; y < 240; y++)
            {
                int src = y * 256;
                int dst = (239 - y) * 256;      // Texture2D row 0 is the bottom, so flip vertically
                for (int x = 0; x < 256; x++)
                {
                    var c = fb[src + x];
                    px[dst + x] = new Color32(c.R, c.G, c.B, 255);
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }

        public void Dispose()
        {
            if (_nes != null && _nes.HasBattery) SaveSram();
            try { FiresAudio.Stop(_audioSource); } catch { }
            if (_audioGo != null) UnityEngine.Object.Destroy(_audioGo);
            _audioGo = null; _sink = null; _audioSource = null;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null;
            _nes = null;
            _pixels = null;
        }
    }
}
