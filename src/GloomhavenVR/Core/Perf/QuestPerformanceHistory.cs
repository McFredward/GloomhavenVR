using System;
using System.Globalization;

namespace GloomhavenVR.Core;

/// <summary>
/// Keep already composed measurement windows when Android's logcat ring has moved
/// on. No new timer, scene census, render callback or frame sampling is installed.
/// The caller supplies the existing Debug/Quest gate before formatting context.
/// </summary>
internal sealed class QuestPerformanceHistory
{
    private const int Capacity = 24;
    private const int MaximumLineCharacters = 16384;
    private readonly string[] _lines = new string[Capacity];
    private int _count, _cursor;

    internal void Record(bool enabled, int frame, double seconds, string scene, string line)
    {
        if (!enabled || string.IsNullOrEmpty(line)) return;
        // These are end-of-window aggregate reports only. Never retain per-frame
        // SPIKE streams or unrelated text from an arbitrary logging call site.
        if (!line.StartsWith("FRAME ", StringComparison.Ordinal)
            && !line.StartsWith("STEPS ", StringComparison.Ordinal)
            && !line.StartsWith("SPLIT ", StringComparison.Ordinal)
            && !line.StartsWith("COUNTS ", StringComparison.Ordinal)) return;
        if (line.Length > MaximumLineCharacters)
            line = line.Substring(0, MaximumLineCharacters) + " [retained summary truncated]";
        _lines[_cursor] = "window scene=" + scene + " endFrame=" + frame
            + " endSeconds=" + seconds.ToString("F2", CultureInfo.InvariantCulture) + " " + line;
        _cursor = (_cursor + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    internal string[] Snapshot()
    {
        var result = new string[_count];
        int first = (_cursor - _count + Capacity) % Capacity;
        for (int index = 0; index < result.Length; index++) result[index] = _lines[(first + index) % Capacity];
        return result;
    }

    internal void Clear()
    {
        Array.Clear(_lines, 0, _lines.Length); _count = _cursor = 0;
    }
}
