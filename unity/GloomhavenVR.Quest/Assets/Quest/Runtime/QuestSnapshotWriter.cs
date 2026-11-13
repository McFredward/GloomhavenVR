#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GloomhavenVR.Quest
{
    /// <summary>One managed file writer, retaining at most the active and latest snapshots.
    /// Unity serialization stays on its owner thread; no Unity API runs here.</summary>
    internal sealed class QuestSnapshotWriter : IDisposable
    {
        readonly object sync = new object();
        readonly string path;
        readonly Action<string, string> publish;
        string pending, failure;
        bool running, closed;
        double completedMaxMs;

        internal QuestSnapshotWriter(string path) : this(path, Publish) { }
        internal QuestSnapshotWriter(string path, Action<string, string> publish)
        { this.path = path; this.publish = publish; }

        internal void Enqueue(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            lock (sync)
            {
                if (closed) return;
                pending = json;
                if (running) return;
                running = true;
                try
                {
                    if (!ThreadPool.QueueUserWorkItem(Drain))
                        throw new InvalidOperationException("Snapshot worker was not queued.");
                }
                catch { running = false; pending = null; Monitor.PulseAll(sync); throw; }
            }
        }

        void Drain(object unused)
        {
            while (true)
            {
                string json;
                lock (sync)
                {
                    json = pending; pending = null;
                    if (json == null) { running = false; Monitor.PulseAll(sync); return; }
                }
                long start = Stopwatch.GetTimestamp();
                string error = null;
                try { publish(path, json); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                lock (sync)
                {
                    completedMaxMs = Math.Max(completedMaxMs,
                        (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
                    if (error != null && failure == null) failure = error;
                }
            }
        }

        internal void TakeCompleted(out double maximumMs, out string error)
        {
            lock (sync)
            {
                maximumMs = completedMaxMs; error = failure;
                completedMaxMs = 0; failure = null;
            }
        }

        internal void Flush()
        { lock (sync) while (running) Monitor.Wait(sync); }

        public void Dispose()
        { lock (sync) { closed = true; while (running) Monitor.Wait(sync); } }

        internal static void Publish(string path, string json)
        {
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
#endif
