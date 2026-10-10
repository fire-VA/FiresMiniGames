using System;
using System.Reflection;
using UnityEngine;

namespace FiresMiniGames.Modules.Common
{
    /// <summary>
    /// Reflection-driven audio — a net48 Fires mod cannot reference UnityEngine.AudioModule types directly (CS1705:
    /// AudioModule wants netstandard 2.1). So AudioClip/AudioSource are created and driven via reflected members
    /// (the FiresCore.UI.PencilSfx recipe), and sources are routed through Core's LiveAudioRouting so they obey the
    /// player's volume sliders and don't pick up dungeon reverb. Clips/sources are passed around as <c>object</c>.
    /// </summary>
    internal static class FiresAudio
    {
        private static readonly Type TClip = Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule");
        private static readonly Type TSource = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
        private static readonly Type TSettings = Type.GetType("UnityEngine.AudioSettings, UnityEngine.AudioModule");
        private static readonly PropertyInfo POutRate = TSettings?.GetProperty("outputSampleRate");

        /// <summary>The device output sample rate (Hz). Procedural sources must produce at this rate or they play at
        /// the wrong pitch. Falls back to 44100 if AudioSettings can't be reached.</summary>
        public static int OutputSampleRate
        {
            get { try { if (POutRate != null) return (int)POutRate.GetValue(null); } catch { } return 44100; }
        }

        private static readonly MethodInfo MCreate = TClip?.GetMethod("Create", new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) });
        private static readonly MethodInfo MSetData = TClip?.GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
        private static readonly MethodInfo MPlayOneShot = TSource?.GetMethod("PlayOneShot", new[] { TClip, typeof(float) });
        private static readonly MethodInfo MPlay = TSource?.GetMethod("Play", Type.EmptyTypes);
        private static readonly MethodInfo MStop = TSource?.GetMethod("Stop", Type.EmptyTypes);
        private static readonly PropertyInfo PClip = TSource?.GetProperty("clip");
        private static readonly PropertyInfo PLoop = TSource?.GetProperty("loop");
        private static readonly PropertyInfo PVol = TSource?.GetProperty("volume");
        private static readonly PropertyInfo PSpatial = TSource?.GetProperty("spatialBlend");
        private static readonly PropertyInfo PPlayOnAwake = TSource?.GetProperty("playOnAwake");
        private static readonly PropertyInfo PBypass = TSource?.GetProperty("bypassReverbZones");

        public static bool Available => TClip != null && TSource != null && MCreate != null && MPlayOneShot != null;

        // Add a 2D AudioSource to go, route it (GUI mixer + reverb bypass), return it as object.
        public static object AddSource(GameObject go, float volume, bool loop)
        {
            if (TSource == null || go == null) return null;
            var src = go.AddComponent(TSource);
            PSpatial?.SetValue(src, 0f);
            PPlayOnAwake?.SetValue(src, false);
            PVol?.SetValue(src, volume);
            PBypass?.SetValue(src, true);
            if (loop) PLoop?.SetValue(src, true);
            try { FiresCore.Services.LiveAudioRouting.RouteUiSfx(go); } catch { }
            return src;
        }

        public static object Clip(string name, float[] samples, int rate)
        {
            if (MCreate == null || samples == null || samples.Length == 0) return null;
            var clip = MCreate.Invoke(null, new object[] { name, samples.Length, 1, rate, false });
            MSetData?.Invoke(clip, new object[] { samples, 0 });
            return clip;
        }

        public static void PlayOneShot(object source, object clip, float volume)
        {
            if (source == null || clip == null || MPlayOneShot == null) return;
            try { MPlayOneShot.Invoke(source, new object[] { clip, volume }); } catch { }
        }

        public static void PlayLoop(object source, object clip)
        {
            if (source == null || clip == null) return;
            try { PClip?.SetValue(source, clip); PLoop?.SetValue(source, true); MPlay?.Invoke(source, null); } catch { }
        }

        public static void Stop(object source) { if (source != null) try { MStop?.Invoke(source, null); } catch { } }
    }
}
