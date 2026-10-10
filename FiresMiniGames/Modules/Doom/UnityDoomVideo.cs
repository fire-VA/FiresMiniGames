using ManagedDoom;
using UnityEngine;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// IVideo backed by the vendored software Renderer. Each frame the renderer fills a column-major RGBA buffer
    /// (palette packs R|G8|B16|A24 = Unity RGBA32); we transpose + vertical-flip it into an upright Texture2D the
    /// TV screen displays. Everything else delegates to the renderer.
    /// </summary>
    public sealed class UnityDoomVideo : ManagedDoom.Video.IVideo
    {
        private readonly ManagedDoom.Video.Renderer _renderer;
        private readonly byte[] _frame;      // column-major from the renderer
        private readonly byte[] _rows;       // row-major, flipped, for Unity
        private readonly int _w, _h;

        public Texture2D Texture { get; }

        public UnityDoomVideo(Config config, GameContent content)
        {
            _renderer = new ManagedDoom.Video.Renderer(config, content);
            _w = _renderer.Width;
            _h = _renderer.Height;
            _frame = new byte[4 * _w * _h];
            _rows = new byte[4 * _w * _h];
            Texture = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            _renderer.Render(doom, _frame, frameFrac);
            for (int x = 0; x < _w; x++)
            {
                int col = x * _h;
                for (int y = 0; y < _h; y++)
                {
                    int src = (col + y) * 4;
                    int dst = ((_h - 1 - y) * _w + x) * 4;
                    _rows[dst] = _frame[src];
                    _rows[dst + 1] = _frame[src + 1];
                    _rows[dst + 2] = _frame[src + 2];
                    _rows[dst + 3] = _frame[src + 3];
                }
            }
            Texture.LoadRawTextureData(_rows);
            Texture.Apply(false);
        }

        public void InitializeWipe() => _renderer.InitializeWipe();
        public bool HasFocus() => true;

        public int MaxWindowSize => _renderer.MaxWindowSize;
        public int WindowSize { get => _renderer.WindowSize; set => _renderer.WindowSize = value; }
        public bool DisplayMessage { get => _renderer.DisplayMessage; set => _renderer.DisplayMessage = value; }
        public int MaxGammaCorrectionLevel => _renderer.MaxGammaCorrectionLevel;
        public int GammaCorrectionLevel { get => _renderer.GammaCorrectionLevel; set => _renderer.GammaCorrectionLevel = value; }
        public int WipeBandCount => _renderer.WipeBandCount;
        public int WipeHeight => _renderer.WipeHeight;
    }
}
