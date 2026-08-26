// GloomhavenVR — VoiceSpatialProbe: the fallback capture, and the one place it is allowed to live.
//
// OnAudioFilterRead on the GameObject that carries the AudioListener sees the FINAL MIX: every
// source summed, every pan applied, every rolloff applied. That is the buffer this probe wants.
//
// THE MISTAKE THIS FILE EXISTS TO NOT MAKE. Put this same component on the SOURCE's GameObject and
// it still runs, still fills buffers, still produces a tidy CSV — of the PRE-spatialisation signal.
// A left-hand bearing and a right-hand bearing would read identically, the null control would agree
// with the spatial case, and the rig would be reporting the sine it generated itself. So the tap is
// attached by the runner to the listener object and nowhere else, and this comment is the record of
// why that is not an arbitrary choice.
//
// Threading: OnAudioFilterRead runs on the AUDIO thread. Buffers are copied and queued under a lock;
// the main thread drains them in Update. Nothing is analysed on the audio thread.
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.VoiceProbe
{
    public sealed class ListenerTap : MonoBehaviour
    {
        private readonly Queue<float[]> _queue = new Queue<float[]>();
        private readonly object _lock = new object();

        /// <summary>Dropped buffers, if the main thread ever falls far enough behind to matter.</summary>
        internal long Dropped { get; private set; }

        /// <summary>How many buffers the audio thread has handed over. Zero means it never ran.</summary>
        internal long Delivered { get; private set; }

        private const int MaxQueued = 512;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var copy = new float[data.Length];
            System.Array.Copy(data, copy, data.Length);
            lock (_lock)
            {
                if (_queue.Count >= MaxQueued) { _queue.Dequeue(); Dropped++; }
                _queue.Enqueue(copy);
                Delivered++;
            }
        }

        /// <summary>Main thread: next captured buffer, or null when there is none.</summary>
        internal float[] Take()
        {
            lock (_lock) { return _queue.Count > 0 ? _queue.Dequeue() : null; }
        }
    }
}
