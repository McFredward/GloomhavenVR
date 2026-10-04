#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Globalization;

namespace GloomhavenVR.Quest
{
    [Serializable]
    internal sealed class QuestFrameSnapshot
    {
        public string scene;
        public bool incrementalGc, sampling;
        public int frames, over40Ms, over100Ms, gcCollections, spikeCount, spikeCursor;
        public int discoveryScans, discoverySceneComponents;
        public double discoveryLastMs, discoveryWorstMs;
        public double sampledSeconds;
        public float worstFrameMs, maxSnapshotWriteMs;
        public float[] spikeTimes = new float[24], spikeFrameMs = new float[24];
        public int[] spikeGcDelta = new int[24];
    }

    /// <summary>Read-only, allocation-free frame accumulation with bounded Debug reports.</summary>
    internal sealed class QuestFrameEvidence
    {
        internal readonly QuestFrameSnapshot Snapshot = new QuestFrameSnapshot();
        bool running;
        int lastGc, reports, reportedSpikes;
        float nextReport;

        internal string Observe(string scene, bool active, bool debug, float deltaSeconds, float now, int collections, bool incremental)
        {
            // The maintainer's B619 menu report is a repeated hitch, not evidence
            // that GC, logging or any single subsystem caused it. Never force a
            // collection or tune native gameplay in order to obtain a measurement.
            if (!debug || !active || string.IsNullOrEmpty(scene))
            { running = false; Snapshot.sampling = false; return null; }
            if (!running || Snapshot.scene != scene)
            {
                if (Snapshot.scene != scene)
                {
                    Snapshot.scene = scene; Snapshot.frames = Snapshot.over40Ms = Snapshot.over100Ms = 0;
                    Snapshot.gcCollections = Snapshot.spikeCount = Snapshot.spikeCursor = 0; Snapshot.sampledSeconds = 0; Snapshot.worstFrameMs = 0;
                    reports = reportedSpikes = 0;
                }
                running = true; lastGc = collections; nextReport = now + 15;
                Snapshot.sampling = true; Snapshot.incrementalGc = incremental;
                // A resume/scene boundary's delta includes time without active
                // gameplay. Do not attribute that gap to a menu frame or GC.
                return null;
            }
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0) return null;
            float milliseconds = deltaSeconds * 1000;
            int gcDelta = Math.Max(0, collections - lastGc); lastGc = collections;
            Snapshot.frames++; Snapshot.sampledSeconds += deltaSeconds;
            Snapshot.gcCollections += gcDelta;
            Snapshot.incrementalGc = incremental;
            Snapshot.worstFrameMs = Math.Max(Snapshot.worstFrameMs, milliseconds);
            if (milliseconds >= 100) Snapshot.over100Ms++;
            if (milliseconds >= 40)
            {
                Snapshot.over40Ms++;
                // Retain the latest bounded window, so startup spikes cannot
                // crowd out the sustained menu hitches reported after B620.
                int index = Snapshot.spikeCursor;
                Snapshot.spikeCursor = (index + 1) % Snapshot.spikeTimes.Length;
                Snapshot.spikeCount = Math.Min(Snapshot.spikeCount + 1, Snapshot.spikeTimes.Length);
                Snapshot.spikeTimes[index] = now;
                Snapshot.spikeFrameMs[index] = milliseconds;
                Snapshot.spikeGcDelta[index] = gcDelta;
                if (reportedSpikes++ < 4)
                    return "[Quest startup] frame spike scene=" + scene + " ms=" + F(milliseconds)
                        + " gcDelta=" + gcDelta + " incremental=" + incremental;
            }
            if (now < nextReport || reports >= 8) return null;
            nextReport = now + 15; reports++;
            return "[Quest startup] frame summary scene=" + scene + " frames=" + Snapshot.frames
                + " seconds=" + F(Snapshot.sampledSeconds) + " worstMs=" + F(Snapshot.worstFrameMs)
                + " over40Ms=" + Snapshot.over40Ms + " over100Ms=" + Snapshot.over100Ms
                + " gcCollections=" + Snapshot.gcCollections + " incremental=" + incremental
                + " maxSnapshotWriteMs=" + F(Snapshot.maxSnapshotWriteMs)
                + " discoveryScans=" + Snapshot.discoveryScans + " discoveryLastMs=" + F(Snapshot.discoveryLastMs)
                + " discoveryWorstMs=" + F(Snapshot.discoveryWorstMs) + " sceneComponents=" + Snapshot.discoverySceneComponents;
        }
        internal void RecordDiscovery(int scans, int sceneComponents, double lastMs, double worstMs)
        {
            Snapshot.discoveryScans = scans; Snapshot.discoverySceneComponents = sceneComponents;
            Snapshot.discoveryLastMs = lastMs; Snapshot.discoveryWorstMs = worstMs;
        }
        internal void RecordSnapshotWrite(double milliseconds)
        {
            if (!double.IsNaN(milliseconds) && !double.IsInfinity(milliseconds) && milliseconds >= 0)
                Snapshot.maxSnapshotWriteMs = Math.Max(Snapshot.maxSnapshotWriteMs, (float)milliseconds);
        }
        static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
    }
}
#endif
