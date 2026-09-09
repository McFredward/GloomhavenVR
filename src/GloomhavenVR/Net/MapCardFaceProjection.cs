using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Project a public map loadout onto the owner's actual fan slots. Negative entries
/// reserve held cards retained by a native fan rebuild; ordinary plucks remove their source seat.</summary>
internal static class MapCardFaceProjection
{
    internal static bool TryBuild(int sourceCount, int heldSourceA, int arcA, int heldSourceB,
        int arcB, int count, List<int> into)
    {
        into.Clear();
        if (sourceCount < 0 || count < 0 || heldSourceA < -1 || heldSourceB < -1
            || heldSourceA >= sourceCount || heldSourceB >= sourceCount
            || (heldSourceA >= 0 && heldSourceA == heldSourceB)
            || !ValidSlot(arcA, count) || !ValidSlot(arcB, count)
            || (arcA != byte.MaxValue && arcA == arcB)) return false;
        int source = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == arcA) { into.Add(-1); continue; }
            if (i == arcB) { into.Add(-2); continue; }
            while (source < sourceCount && (source == heldSourceA || source == heldSourceB)) source++;
            if (source >= sourceCount) { into.Clear(); return false; }
            into.Add(source++);
        }
        while (source < sourceCount && (source == heldSourceA || source == heldSourceB)) source++;
        if (source != sourceCount) { into.Clear(); return false; }
        return true;
    }

    private static bool ValidSlot(int slot, int count) => slot == byte.MaxValue || (slot >= 0 && slot < count);
}
