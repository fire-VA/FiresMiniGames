using System;
using System.Diagnostics;
using System.IO;

// net48 / Unity-Mono compatibility shims for the vendored sGBA core (Asphaltian/sGBA, GPL-3.0). Mirrors the
// DoomCompat.cs pattern: every type here lives in the sGBA namespace so the engine's unqualified references
// (MathF.*, Log.*, and the Sio primitive RealTimeSince) resolve here via same-namespace lookup without
// editing the engine files. The engine originally compiled against s&box's implicit global usings and its GPU/task
// frontend; those provide netstandard2.1+ helpers (System.MathF, Array.Fill, Math.Clamp, BitOperations.PopCount)
// that the net48 targeting pack lacks. The three qualified calls that can't be shimmed by same-namespace lookup are
// redirected to GbaCompat by a small mechanical edit set applied to the vendored copies:
//   GbaSavedata.cs      : Array.Fill( ... )                     -> GbaCompat.Fill( ... )      (7 sites)
//   GbaAudio.Channels.cs: Math.Clamp( ... )                    -> GbaCompat.Clamp( ... )     (2 sites)
//   ArmCore.cs          : System.Numerics.BitOperations.PopCount -> GbaCompat.PopCount        (1 site)
// (plus single-arg Array.Clear(x) -> Array.Clear(x,0,x.Length) and 4 Span AsSpan().Clear/CopyTo -> Array.Clear/Copy,
//  all pure net48-API rewrites that keep using System.Array.) A "using System;" was also prepended to each vendored
// file, since the originals leaned on s&box's implicit global usings.
namespace sGBA
{
    // System.MathF (netstandard2.1) is absent from net48. Only the members the kept core touches are needed
    // (Pow in GbaBios.Decompress, PI/Cos/Sin in GbaBios.Memory); a broader set is cheap and harmless. Inside the
    // sGBA namespace this hides System.MathF even where it exists (the net8 gba_proof), so behaviour is identical
    // on both targets.
    internal static class MathF
    {
        public const float PI = 3.14159265358979f;
        public static float Abs(float x) => Math.Abs(x);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Floor(float x) => (float)Math.Floor(x);
        public static float Ceiling(float x) => (float)Math.Ceiling(x);
        public static float Round(float x) => (float)Math.Round(x);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }

    // GbaLog's default backend calls an unqualified Log.Info (s&box's Sandbox.Diagnostics.Log). Buffer nothing —
    // just forward whole lines to a pluggable sink so no UnityEngine dependency leaks into the engine assembly.
    // The Unity host (GbaRuntime) points Sink at Debug.Log; the headless gba_proof can point it at Console.
    internal static class Log
    {
        internal static Action<string> Sink;

        public static void Info(string s) { Emit(s); }
        public static void Info(object o) { Emit(o?.ToString()); }
        public static void Warning(string s) { Emit(s); }
        public static void Error(string s) { Emit(s); }
        public static void Trace(string s) { Emit(s); }

        private static void Emit(string s)
        {
            var sink = Sink;
            if (sink != null) { try { sink(s); } catch { } }
        }
    }

    // Redirect targets for the handful of qualified netstandard2.1+/.NET-Core calls that can't be resolved by
    // same-namespace lookup (Array.Fill, Math.Clamp, System.Numerics.BitOperations.PopCount).
    internal static class GbaCompat
    {
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static long Clamp(long v, long lo, long hi) => v < lo ? lo : (v > hi ? hi : v);

        public static void Fill(byte[] array, byte value)
        {
            for (int i = 0; i < array.Length; i++) array[i] = value;
        }

        public static void Fill(byte[] array, byte value, int start, int count)
        {
            int end = start + count;
            for (int i = start; i < end; i++) array[i] = value;
        }

        // Population count of a 32-bit word (SWAR); replaces System.Numerics.BitOperations.PopCount, absent on net48.
        public static int PopCount(uint v)
        {
            v -= (v >> 1) & 0x55555555u;
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            v = (v + (v >> 4)) & 0x0F0F0F0Fu;
            return (int)((v * 0x01010101u) >> 24);
        }
    }

    // The vendored GbaSerialize / GbaCartridgeHardware call BinaryReader.Read(byte[]) (the netcore Read(Span<byte>)
    // overload byte[] implicitly binds to). net48's BinaryReader has no single-array Read, so supply it as a
    // same-namespace extension that fills the whole buffer. On net8 (gba_proof) the instance Read(Span<byte>) still
    // wins — an instance method always outranks an extension — so behaviour is identical on both targets.
    internal static class GbaCompatExtensions
    {
        public static int Read(this BinaryReader r, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int n = r.Read(buffer, offset, buffer.Length - offset);
                if (n <= 0) break;
                offset += n;
            }
            return offset;
        }
    }

    // s&box's RealTimeSince — a monotonic "seconds since I was last assigned" stopwatch. Assigning a float (usually
    // 0f) resets it; reading as float yields elapsed wall-clock seconds. Backed here by a shared monotonic Stopwatch
    // so it needs no engine/Unity clock. Used only by the Sio link-cable host (LinkCableHost.TimeSinceStateSent),
    // which the single-player core boot never drives — present so the vendored Sio subsystem compiles. Public because
    // it is the type of a public property (LinkCableInstance.TimeSinceStateSent).
    public struct RealTimeSince
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double Now => Clock.Elapsed.TotalSeconds;

        private double _start;

        public float Absolute => (float)(Now - _start);

        public static implicit operator float(RealTimeSince ts) => ts.Absolute;
        public static implicit operator RealTimeSince(float seconds) => new RealTimeSince { _start = Now - seconds };

        public override string ToString() => Absolute.ToString();
    }
}
