using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// The in-game host for the built-in adventure: an IScreenConsole a television can display, exactly like DOOM
    /// or the NES emulator. It owns a <see cref="ZeldaGame"/> (the Unity-free core), runs it at 60 Hz, reads the
    /// keyboard as an NES pad, and blits its RGBA buffer into a point-filtered Texture2D (vertically flipped so the
    /// picture is upright on the screen quad). Sound events are drained each tick for the chiptune sink (wired next).
    /// </summary>
    public sealed class ZeldaConsole : IScreenConsole
    {
        private const double Frame = 1.0 / 60.0;

        private ZeldaGame _game;
        private ZeldaAudio _audio;
        private Texture2D _tex;
        private Color32[] _px;
        private double _acc;

        public bool Available => _game != null;
        public Texture2D Texture => _tex;

        public ZeldaConsole()
        {
            _game = new ZeldaGame();
            _tex = new Texture2D(ZeldaGame.Width, ZeldaGame.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "zelda_screen"
            };
            _px = new Color32[ZeldaGame.Width * ZeldaGame.Height];
            try { _audio = new ZeldaAudio(); } catch { _audio = null; }
            Blit();
        }

        public void Tick(float dt)
        {
            if (_game == null) return;
            _audio?.StartTheme();
            byte pad = GamepadKeys.ReadPad();
            _acc += Mathf.Min(dt, 0.1f);
            int ran = 0;
            while (_acc >= Frame && ran < 4) { _game.Step(pad); _acc -= Frame; ran++; }
            while (_game.Sounds.Count > 0) { var s = _game.Sounds.Dequeue(); _audio?.Play(s); }
            if (ran > 0) Blit();
        }

        private void Blit()
        {
            var buf = _game.Rgba; var px = _px;
            int w = ZeldaGame.Width, h = ZeldaGame.Height;
            for (int y = 0; y < h; y++)
            {
                int src = y * w * 4;
                int dst = (h - 1 - y) * w;     // Texture2D row 0 is the bottom → flip
                for (int x = 0; x < w; x++)
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
            _audio?.Dispose(); _audio = null;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null; _game = null; _px = null;
        }
    }
}
