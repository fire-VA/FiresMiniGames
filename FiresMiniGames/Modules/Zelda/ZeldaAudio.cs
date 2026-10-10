using System;
using System.Collections.Generic;
using UnityEngine;
using FiresMiniGames.Modules.Common;

namespace FiresMiniGames.Modules.Zelda
{
    /// <summary>
    /// The Legend of Fire's sound: original chiptune SFX and a looping overworld theme, synthesised by
    /// <see cref="Chiptune"/> and played through <see cref="FiresAudio"/> (reflection — net48 can't touch
    /// AudioModule types directly). The melody is an original heroic tune, not anyone's copyrighted theme. Sources
    /// live on a DontDestroyOnLoad object created per session and torn down on Dispose; clips/sources are object-typed.
    /// </summary>
    internal sealed class ZeldaAudio
    {
        private GameObject _go;
        private object _sfxSrc, _musicSrc;
        private readonly Dictionary<ZeldaGame.Sfx, object> _clips = new Dictionary<ZeldaGame.Sfx, object>();
        private object _theme;
        private bool _themeStarted;

        public ZeldaAudio()
        {
            _go = new GameObject("FiresZeldaAudio");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _sfxSrc = FiresAudio.AddSource(_go, 0.7f, false);
            _musicSrc = FiresAudio.AddSource(_go, 0.4f, true);
            BuildClips();
        }

        private object C(string name, float[] s) => FiresAudio.Clip(name, s, Chiptune.Rate);

        private void BuildClips()
        {
            _clips[ZeldaGame.Sfx.Sword] = C("z_sword", Chiptune.Sweep(760, 1500, 55, Wave.Square, 0.30f));
            _clips[ZeldaGame.Sfx.Beam] = C("z_beam", Chiptune.Sweep(1700, 520, 200, Wave.Square25, 0.24f));
            _clips[ZeldaGame.Sfx.Hurt] = C("z_hurt", Chiptune.Cat(Chiptune.Tone(240, 70, Wave.Noise, 0.30f), Chiptune.Tone(150, 130, Wave.Square, 0.30f)));
            _clips[ZeldaGame.Sfx.EnemyHit] = C("z_ehit", Chiptune.Tone(320, 45, Wave.Noise, 0.24f));
            _clips[ZeldaGame.Sfx.EnemyDie] = C("z_edie", Chiptune.Sweep(700, 120, 240, Wave.Noise, 0.30f));
            _clips[ZeldaGame.Sfx.Pickup] = C("z_pick", Chiptune.Melody(Wave.Square, 0.28f, ("C5", 45), ("E5", 45), ("G5", 70)));
            _clips[ZeldaGame.Sfx.Rupee] = C("z_rupee", Chiptune.Melody(Wave.Square, 0.26f, ("E6", 35), ("B5", 70)));
            _clips[ZeldaGame.Sfx.Heart] = C("z_heart", Chiptune.Melody(Wave.Triangle, 0.32f, ("C5", 55), ("G5", 95)));
            _clips[ZeldaGame.Sfx.Secret] = C("z_secret", Chiptune.Melody(Wave.Square, 0.26f, ("G4", 85), ("A4", 85), ("B4", 85), ("C5", 85), ("D5", 150)));
            _clips[ZeldaGame.Sfx.Text] = C("z_text", Chiptune.Tone(1200, 18, Wave.Square, 0.14f));
            _clips[ZeldaGame.Sfx.Start] = C("z_start", Chiptune.Melody(Wave.Square, 0.30f, ("C5", 80), ("E5", 80), ("G5", 80), ("C6", 170)));
            _theme = C("z_theme", BuildTheme());
        }

        private static float[] BuildTheme()
        {
            // Original heroic overworld tune (NOT the Zelda theme): a square lead over a triangle bass, ~9s loop.
            var lead = Chiptune.Melody(Wave.Square, 0.20f,
                ("E5", 200), ("E5", 200), ("G5", 200), ("A5", 400), ("G5", 200), ("E5", 200), ("D5", 400),
                ("C5", 200), ("D5", 200), ("E5", 400), ("G5", 200), ("E5", 200), ("D5", 200), ("C5", 400),
                ("A4", 200), ("C5", 200), ("E5", 400), ("D5", 200), ("C5", 200), ("D5", 200), ("E5", 400), ("-", 200));
            var bass = Chiptune.Melody(Wave.Triangle, 0.24f,
                ("C3", 400), ("C3", 400), ("G3", 400), ("G3", 400), ("A3", 400), ("A3", 400), ("F3", 400), ("G3", 400),
                ("C3", 400), ("C3", 400), ("F3", 400), ("G3", 400), ("C3", 800), ("G3", 400), ("C3", 400));
            var mix = new float[Math.Max(lead.Length, bass.Length)];
            mix = Chiptune.MixInto(mix, lead, 0);
            mix = Chiptune.MixInto(mix, bass, 0);
            return mix;
        }

        public void Play(ZeldaGame.Sfx s)
        {
            if (_clips.TryGetValue(s, out var c)) FiresAudio.PlayOneShot(_sfxSrc, c, 0.9f);
        }

        public void StartTheme()
        {
            if (_themeStarted || _theme == null) return;
            FiresAudio.PlayLoop(_musicSrc, _theme);
            _themeStarted = true;
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null; _sfxSrc = null; _musicSrc = null;
        }
    }
}
