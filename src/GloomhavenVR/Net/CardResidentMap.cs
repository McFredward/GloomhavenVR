using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Matches locally resolved card instances across a layout change; never a wire identity.</summary>
internal static class CardResidentMap
{
    internal static bool TryBuild(IReadOnlyList<int> previous, IReadOnlyList<int> next, int[] into)
    {
        if (into.Length < next.Count || !Unique(previous) || !Unique(next)) return false;
        for (int i = 0; i < next.Count; i++)
        {
            into[i] = -1;
            for (int j = 0; j < previous.Count; j++)
                if (next[i] == previous[j]) { into[i] = j; break; }
        }
        return true;
    }

    private static bool Unique(IReadOnlyList<int> values)
    {
        for (int i = 0; i < values.Count; i++)
            for (int j = 0; j < i; j++)
                if (values[i] == values[j]) return false;
        return true;
    }
}
