using System;
using System.IO;
using UnityEngine;
using LeBoyLib;
using FiresMiniGames.Modules.Common;
using FiresMiniGames.Modules.Nes;   // reuse the NES audio sink (a generic mono SPSC ring + OnAudioFilterRead)

namespace FiresMiniGames.Modules.GameBoy
{
    /// <summary>
    /// Owns one running Game Boy: builds the vendored LeBoy core (MIT, mrhelmut/LeBoy) from a ROM's bytes, runs it at
    /// the handheld's native ~59.73 Hz, and paints each finished 160x144 frame into a point-filtered Texture2D so a TV
    /// can display it (IScreenConsole, exactly like NesRuntime/DoomRuntime). The eight buttons come from the shared
    /// GamepadKeys reader (same A/B/Select/Start/D-pad as the NES) and are mapped into LeBoy's JoypadState order.
    /// The from-scratch GbApu (which LeBoy shipped without) is clocked by the CPU core; its samples are streamed to a
    /// looping AudioSource via the shared NesAudioSink, exactly like NesRuntime does for the 2A03.
    /// </summary>
    public sealed class GbRuntime : IScreenConsole
    {
        private const int W = 160, H = 144;
        private const double FrameSeconds = 1.0 / 59.7275;                 // GB field rate
        private const int CyclesPerFrame = (int)(GBZ80.ClockSpeed / 59.7275 + 0.5);   // ~70224 t-states

        private GBZ80 _gb;
        private Texture2D _tex;
        private Color32[] _pixels;
        private double _acc;

        private GameObject _audioGo;
        private NesAudioSink _sink;
        private object _audioSource;

        private readonly string _savePath;   // <rom>.sav for battery carts (null = no persistence)
        private float _flushT;               // periodic-save timer

        public bool Available { get; private set; }
        public string Error { get; private set; }
        public Texture2D Texture => _tex;

        public GbRuntime(byte[] rom, string savePath = null)
        {
            _savePath = savePath;
            try
            {
                _gb = new GBZ80();
                _gb.Load(rom);
                LoadSave();                                    // before boot: the save-select screen reads SRAM
                _gb.Apu.SampleRate = FiresAudio.OutputSampleRate;
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "gb_screen"
                };
                _pixels = new Color32[W * H];
                RunFrame();      // frame 0 so the screen isn't black before the first Tick
                _gb.Apu.ClearSamples();   // discard the boot transient
                Blit();
                StartAudio();
                Available = true;
                Debug.Log($"[{FiresMiniGames.PluginName}] Game Boy cartridge running ({CyclesPerFrame} cyc/frame, " +
                          $"{_gb.Apu.SampleRate} Hz audio).");
            }
            catch (Exception e)
            {
                Error = e.Message;
                Available = false;
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy init failed: {e.Message}");
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
            _flushT += dt;                                     // insurance flush in case a hard exit skips Dispose
            if (_flushT >= 15f) { SaveSave(); _flushT = 0f; }
        }

        private void LoadSave()
        {
            if (_gb == null || !_gb.HasBattery || string.IsNullOrEmpty(_savePath)) return;
            try
            {
                if (File.Exists(_savePath))
                {
                    _gb.LoadSaveRam(File.ReadAllBytes(_savePath));
                    Debug.Log($"[{FiresMiniGames.PluginName}] Loaded Game Boy save ({_savePath}).");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy save load failed: {e.Message}"); }
        }

        private void SaveSave()
        {
            if (_gb == null || !_gb.HasBattery || string.IsNullOrEmpty(_savePath)) return;
            try
            {
                var ram = _gb.GetSaveRam();
                if (ram != null && ram.Length > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_savePath));
                    File.WriteAllBytes(_savePath, ram);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy save write failed: {e.Message}"); }
        }

        private void PumpAudio()
        {
            var apu = _gb.Apu;
            if (_sink != null && apu.SampleCount > 0) _sink.Push(apu.Samples, apu.SampleCount);
            apu.ClearSamples();
        }

        private void StartAudio()
        {
            if (!FiresAudio.Available) return;
            try
            {
                int rate = _gb.Apu.SampleRate;
                _audioGo = new GameObject("gb_audio");
                _sink = _audioGo.AddComponent<NesAudioSink>();
                _sink.Init(rate / 4);                                    // ~0.25 s ring
                _audioSource = FiresAudio.AddSource(_audioGo, 1f, true);
                var silence = FiresAudio.Clip("gb_silence", new float[Mathf.Max(1024, rate / 10)], rate);
                FiresAudio.PlayLoop(_audioSource, silence);              // keeps OnAudioFilterRead firing
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{FiresMiniGames.PluginName}] Game Boy audio unavailable: {e.Message}");
                _sink = null;
            }
        }

        private void RunFrame()
        {
            long c = 0; int guard = 0;
            while (c < CyclesPerFrame && guard++ < 400000)
            {
                uint used = _gb.DecodeAndDispatch();
                c += used == 0 ? 4 : used;   // a stuck 0-cycle read still advances so the loop can't hang
            }
        }

        private void ApplyInput()
        {
            byte pad = GamepadKeys.ReadPad();   // NES bit order: 0 A,1 B,2 Select,3 Start,4 Up,5 Down,6 Left,7 Right
            var j = _gb.JoypadState;            // LeBoy order:   0 Right,1 Left,2 Up,3 Down,4 A,5 B,6 Select,7 Start
            if (j == null || j.Length < 8) return;
            j[0] = (pad & (1 << 7)) != 0;   // Right
            j[1] = (pad & (1 << 6)) != 0;   // Left
            j[2] = (pad & (1 << 4)) != 0;   // Up
            j[3] = (pad & (1 << 5)) != 0;   // Down
            j[4] = (pad & (1 << 0)) != 0;   // A
            j[5] = (pad & (1 << 1)) != 0;   // B
            j[6] = (pad & (1 << 2)) != 0;   // Select
            j[7] = (pad & (1 << 3)) != 0;   // Start
        }

        private void Blit()
        {
            var buf = _gb.GetScreenBuffer();   // 160x144, RGBA, row 0 = top
            var px = _pixels;
            for (int y = 0; y < H; y++)
            {
                int src = y * W * 4;
                int dst = (H - 1 - y) * W;     // Texture2D row 0 is the bottom, so flip vertically
                for (int x = 0; x < W; x++)
                {
                    int s = src + x * 4;
                    px[dst + x] = new Color32(buf[s], buf[s + 1], buf[s + 2], 255);
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }

        public void Dispose()
        {
            SaveSave();
            try { FiresAudio.Stop(_audioSource); } catch { }
            if (_audioGo != null) UnityEngine.Object.Destroy(_audioGo);
            _audioGo = null; _sink = null; _audioSource = null;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null;
            _gb = null;
            _pixels = null;
        }
    }
}
