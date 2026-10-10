using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.Audio;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.DoomTv
{
    /// <summary>
    /// DOOM sound effects on a television: an ISound that decodes the WAD's DMX sound lumps (DS*, 8-bit unsigned
    /// PCM) into clips on first use and plays them via <see cref="FiresAudio"/> (reflection — net48 can't reference
    /// AudioModule types), attenuating positional sounds by the listener's distance. Music (MUS/MIDI) is deferred —
    /// this is effects only, which is what makes the game feel alive. Clips/source are object-typed.
    /// </summary>
    public sealed class UnityDoomSound : ISound, IDisposable
    {
        private readonly GameContent _content;
        private GameObject _go;
        private object _src;
        private readonly Dictionary<int, object> _cache = new Dictionary<int, object>();
        private Mobj _listener;

        public int MaxVolume => 15;
        public int Volume { get; set; } = 12;

        public UnityDoomSound(GameContent content)
        {
            _content = content;
            _go = new GameObject("FiresDoomAudio");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _src = FiresAudio.AddSource(_go, 1f, false);
        }

        public void SetListener(Mobj listener) => _listener = listener;
        public void Update() { }
        public void StartSound(Sfx sfx) => Play(sfx, 1f);
        public void StartSound(Mobj mobj, Sfx sfx, SfxType type) => Play(sfx, Atten(mobj));
        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume) => Play(sfx, Atten(mobj) * Mathf.Clamp01(volume / 127f));
        public void StopSound(Mobj mobj) { }
        public void Reset() { }
        public void Pause() { }
        public void Resume() { }

        private float Atten(Mobj mobj)
        {
            if (_listener == null || mobj == null || mobj == _listener) return 1f;
            double dx = (mobj.X - _listener.X).ToDouble();
            double dy = (mobj.Y - _listener.Y).ToDouble();
            double dist = Math.Sqrt(dx * dx + dy * dy);
            const double near = 200, far = 1800;
            if (dist <= near) return 1f;
            if (dist >= far) return 0f;
            return (float)(1.0 - (dist - near) / (far - near));
        }

        private void Play(Sfx sfx, float vol)
        {
            if (sfx == Sfx.NONE || _src == null) return;
            var clip = Get(sfx);
            if (clip == null) return;
            float v = Mathf.Clamp01(vol) * (Volume / (float)MaxVolume);
            if (v <= 0.01f) return;
            FiresAudio.PlayOneShot(_src, clip, v);
        }

        private object Get(Sfx sfx)
        {
            int key = (int)sfx;
            if (_cache.TryGetValue(key, out var c)) return c;
            c = Build(sfx);
            _cache[key] = c;   // cache nulls too so a missing lump isn't re-read every shot
            return c;
        }

        private object Build(Sfx sfx)
        {
            try
            {
                string name = "DS" + DoomInfo.SfxNames[(int)sfx].ToString().ToUpperInvariant();
                int lump = _content.Wad.GetLumpNumber(name);
                if (lump < 0) return null;
                byte[] data = _content.Wad.ReadLump(lump);
                if (data == null || data.Length < 8) return null;

                int rate = data[2] | (data[3] << 8);
                int count = data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24);
                if (rate < 8000 || rate > 48000) rate = 11025;

                int start = 8, end = Math.Min(data.Length, start + count);
                if (count > 32) { start += 16; end -= 16; }   // DMX pads 16 samples at each end
                int n = Math.Max(1, end - start);

                var s = new float[n];
                for (int i = 0; i < n; i++) s[i] = (data[start + i] - 128) / 128f;
                return FiresAudio.Clip(name, s, rate);
            }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null; _src = null; _cache.Clear();
        }
    }
}
