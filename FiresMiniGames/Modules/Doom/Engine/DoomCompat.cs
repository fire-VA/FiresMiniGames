using System.Collections.Generic;
using System.IO;

// net48/Unity-Mono compatibility shims for the vendored ManagedDoom engine. These live in the ManagedDoom
// namespace so the engine's unqualified Console/MathF/DoomMath references and the TryAdd/ReadExactly extensions
// resolve here (parent-namespace lookup) without editing the ~200 engine files. The only mechanical source edits
// applied to the vendored tree are: DoomMath.Clamp -> DoomMath.Clamp, ExceptionDispatchInfo.Capture(e).Throw() ->
// Capture(e).Throw(), Split(char,opts) -> Split(char[],opts), and the byte-wise WriteData in Renderer.cs.
namespace ManagedDoom
{
    // Console isn't in Unity's Mono profile — buffer Write fragments and flush whole lines to a pluggable sink
    // (FiresMiniGames wires it to Debug.Log; a headless harness can point it at the real console). No UnityEngine
    // dependency here, so the engine assembly also builds in a plain net console for verification.
    internal static class Console
    {
        internal static System.Action<string> Sink;
        private static readonly System.Text.StringBuilder Buf = new System.Text.StringBuilder();

        public static void Write(string s) { Buf.Append(s); }
        public static void Write(object o) { Buf.Append(o); }
        public static void WriteLine() { Flush(); }
        public static void WriteLine(string s) { Buf.Append(s); Flush(); }
        public static void WriteLine(object o) { Buf.Append(o); Flush(); }

        private static void Flush()
        {
            if (Buf.Length == 0) return;
            var s = Buf.ToString();
            Buf.Length = 0;
            if (Sink != null) { try { Sink(s); } catch { } }
        }
    }

    // MathF (netstandard2.1) is absent — forward to double Math. Only the members the engine uses are needed;
    // a broad set is cheap and future-proofs the renderer.
    internal static class MathF
    {
        public const float PI = 3.14159265358979f;
        public static float Abs(float x) => System.Math.Abs(x);
        public static float Sqrt(float x) => (float)System.Math.Sqrt(x);
        public static float Sin(float x) => (float)System.Math.Sin(x);
        public static float Cos(float x) => (float)System.Math.Cos(x);
        public static float Tan(float x) => (float)System.Math.Tan(x);
        public static float Atan(float x) => (float)System.Math.Atan(x);
        public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);
        public static float Floor(float x) => (float)System.Math.Floor(x);
        public static float Ceiling(float x) => (float)System.Math.Ceiling(x);
        public static float Round(float x) => (float)System.Math.Round(x);
        public static float Pow(float x, float y) => (float)System.Math.Pow(x, y);
        public static float Log(float x) => (float)System.Math.Log(x);
        public static float Exp(float x) => (float)System.Math.Exp(x);
        public static float Min(float a, float b) => System.Math.Min(a, b);
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static float Sign(float x) => System.Math.Sign(x);
    }

    // DoomMath.Clamp (netstandard2.1) is absent — the vendored tree calls DoomMath.Clamp instead.
    internal static class DoomMath
    {
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static long Clamp(long v, long lo, long hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }

    internal static class DoomCompatExtensions
    {
        public static bool TryAdd<TK, TV>(this Dictionary<TK, TV> dict, TK key, TV value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }

        // Stream.ReadExactly (net7) — loop until the buffer is full or the stream ends.
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
