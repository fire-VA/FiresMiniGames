using System;
using System.Collections.Generic;

namespace FiresMiniGames.Modules.Common
{
    public enum Wave { Square, Square25, Triangle, Saw, Noise }

    /// <summary>
    /// A tiny NES-style tone synth that produces float PCM (mono, -1..1). UnityEngine-free so it can be WAV-tested
    /// headlessly; the Unity side wraps the buffers in AudioClips. Used for The Legend of Fire's original score and
    /// sound effects (all tunes here are original compositions, not anyone's copyrighted melody).
    /// </summary>
    public static class Chiptune
    {
        public const int Rate = 22050;

        private static uint _lfsr = 0xACE1u;
        private static float NoiseSample() { _lfsr ^= _lfsr << 13; _lfsr ^= _lfsr >> 17; _lfsr ^= _lfsr << 5; return ((_lfsr & 0xFF) / 127.5f) - 1f; }

        // Note name -> frequency. "C4","D#4","G5", or "-" for a rest.
        public static float Note(string n)
        {
            if (string.IsNullOrEmpty(n) || n == "-") return 0f;
            int i = 0; char L = char.ToUpperInvariant(n[i++]);
            int semis; switch (L) { case 'C': semis = 0; break; case 'D': semis = 2; break; case 'E': semis = 4; break; case 'F': semis = 5; break; case 'G': semis = 7; break; case 'A': semis = 9; break; default: semis = 11; break; }
            if (i < n.Length && (n[i] == '#' || n[i] == '+')) { semis++; i++; } else if (i < n.Length && n[i] == 'b') { semis--; i++; }
            int oct = 4; if (i < n.Length && int.TryParse(n.Substring(i), out var o)) oct = o;
            int midi = (oct + 1) * 12 + semis;      // C4 = midi 60
            return 440f * (float)Math.Pow(2.0, (midi - 69) / 12.0);
        }

        // A single voiced note with a linear attack/release envelope.
        public static float[] Tone(float freq, float ms, Wave w = Wave.Square, float vol = 0.28f, float attackMs = 3f, float releaseMs = 24f)
        {
            int n = Math.Max(1, (int)(Rate * ms / 1000f));
            var buf = new float[n];
            if (freq <= 0f) return buf;                 // rest
            int atk = Math.Max(1, (int)(Rate * attackMs / 1000f));
            int rel = Math.Max(1, (int)(Rate * releaseMs / 1000f));
            double phase = 0, step = freq / Rate;
            for (int i = 0; i < n; i++)
            {
                float s;
                switch (w)
                {
                    case Wave.Square: s = phase < 0.5 ? 1f : -1f; break;
                    case Wave.Square25: s = phase < 0.25 ? 1f : -1f; break;
                    case Wave.Triangle: s = (float)(4 * Math.Abs(phase - 0.5) - 1); break;
                    case Wave.Saw: s = (float)(2 * phase - 1); break;
                    default: s = NoiseSample(); break;
                }
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i > n - rel) env = Math.Max(0f, (n - i) / (float)rel);
                buf[i] = s * vol * env;
                phase += step; if (phase >= 1.0) phase -= 1.0;
            }
            return buf;
        }

        // Pitch sweep (freq0 -> freq1) — good for beams, pickups, zaps.
        public static float[] Sweep(float f0, float f1, float ms, Wave w = Wave.Square, float vol = 0.28f)
        {
            int n = Math.Max(1, (int)(Rate * ms / 1000f));
            var buf = new float[n];
            double phase = 0;
            int rel = Math.Max(1, n / 6);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = f0 + (f1 - f0) * t;
                float s;
                switch (w) { case Wave.Triangle: s = (float)(4 * Math.Abs(phase - 0.5) - 1); break; case Wave.Noise: s = NoiseSample(); break; default: s = phase < 0.5 ? 1f : -1f; break; }
                float env = i > n - rel ? Math.Max(0f, (n - i) / (float)rel) : 1f;
                buf[i] = s * vol * env;
                phase += f / Rate; if (phase >= 1.0) phase -= 1.0;
            }
            return buf;
        }

        public static float[] Cat(params float[][] parts)
        {
            int total = 0; foreach (var p in parts) total += p.Length;
            var buf = new float[total]; int o = 0;
            foreach (var p in parts) { Array.Copy(p, 0, buf, o, p.Length); o += p.Length; }
            return buf;
        }

        // Mix b into a at offset (sample), extending a if needed (returns possibly-new buffer).
        public static float[] MixInto(float[] a, float[] b, int offset)
        {
            int need = offset + b.Length;
            if (need > a.Length) { var na = new float[need]; Array.Copy(a, na, a.Length); a = na; }
            for (int i = 0; i < b.Length; i++) { float v = a[offset + i] + b[i]; a[offset + i] = v > 1f ? 1f : (v < -1f ? -1f : v); }
            return a;
        }

        // A monophonic melody line: (note, ms) pairs on one waveform.
        public static float[] Melody(Wave w, float vol, params (string note, float ms)[] notes)
        {
            var parts = new List<float[]>();
            foreach (var (note, ms) in notes) parts.Add(Tone(Note(note), ms, w, vol));
            return Cat(parts.ToArray());
        }
    }
}
