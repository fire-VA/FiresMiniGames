using UnityEngine;

namespace FiresMiniGames.Modules.Nes
{
    /// <summary>
    /// Turns the emulator APU's sample stream into real sound. The NES core produces audio on the game thread (one
    /// emulated frame at a time); this component's OnAudioFilterRead — Unity's audio-thread pull callback, whose
    /// float[]/int signature dodges the AudioModule reference net48 can't make — drains those samples into the output
    /// buffer. A single-producer / single-consumer ring decouples the two threads; on underrun the last sample is
    /// faded to silence instead of clicking. A looping silent AudioSource on the same GameObject keeps the filter
    /// callback firing; the NES is mono, so each sample is written to every output channel.
    /// </summary>
    public sealed class NesAudioSink : MonoBehaviour
    {
        private float[] _ring;
        private int _cap;
        private volatile int _writePos;
        private volatile int _readPos;
        private float _lastOut;
        public float Gain = 0.55f;

        public void Init(int capacity)
        {
            _cap = Mathf.Max(2048, capacity);
            _ring = new float[_cap];
            _writePos = 0;
            _readPos = 0;
            _lastOut = 0f;
        }

        // Producer: called on the game thread after each emulated frame. Drops the tail if the ring is full so
        // latency can't grow without bound.
        public void Push(float[] src, int count)
        {
            var ring = _ring;
            if (ring == null) return;
            int w = _writePos;
            int r = _readPos;
            for (int i = 0; i < count; i++)
            {
                int next = w + 1; if (next >= _cap) next = 0;
                if (next == r) break;
                ring[w] = src[i];
                w = next;
            }
            _writePos = w;
        }

        // Consumer: Unity audio thread. `data` is interleaved by `channels`.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            var ring = _ring;
            if (ring == null || channels <= 0) return;
            int r = _readPos;
            int w = _writePos;
            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                if (r != w) { _lastOut = ring[r] * Gain; r++; if (r >= _cap) r = 0; }
                else _lastOut *= 0.99f;   // underrun — fade the held sample toward silence
                int b = i * channels;
                for (int c = 0; c < channels; c++) data[b + c] = _lastOut;
            }
            _readPos = r;
        }
    }
}
