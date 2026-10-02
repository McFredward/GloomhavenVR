namespace GloomhavenVR.Core;

internal static partial class PerfMonitor
{
    /// <summary>
    /// One named counter's window accumulator. A CLASS for the same reason <see cref="Step"/> is:
    /// the hot path mutates it in place after a single dictionary lookup, with no copy-back.
    /// </summary>
    private sealed class Tally
    {
        public Tally(string name) => Name = name;
        public readonly string Name;
        public long WindowTotal;
        public long WindowWorstFrame;

        /// <summary>Print this counter even in a window where it totalled ZERO — see
        /// <see cref="PerfMonitor.Register"/>. Off by default so the line does not fill with rows
        /// for subsystems that are simply not standing this session.</summary>
        public bool PrintZero;

        private long _frame;

        public void Add(long amount)
        {
            _frame += amount;
        }

        // Commit with frame/step/camera samples, never during a current callback.
        // A setting boundary can discard that callback's frame without subtracting
        // an already-published total or trying to reconstruct its worst-frame value.
        public void RollFrame()
        {
            WindowTotal += _frame;
            if (_frame > WindowWorstFrame) WindowWorstFrame = _frame;
            _frame = 0L;
        }
        public void ResetWindow() { WindowTotal = 0L; WindowWorstFrame = 0L; _frame = 0L; }
    }
}
