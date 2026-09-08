namespace GloomhavenVR.Net;

/// <summary>Carry fan poses by the original model seat across one held-card removal or return.
/// The old applied order and new wire order describe different arcs of the SAME full hand.
/// No card identity is read, and invalid/ambiguous membership never supplies a pose.</summary>
internal static class FanReflowMap
{
    internal static bool TryBuild(int oldCount, int newCount, int heldSeat,
        int[]? oldOrder, int oldOrderCount, int[]? newOrder, int newOrderCount, int[] into)
    {
        int fullCount = oldCount > newCount ? oldCount : newCount;
        bool removing = oldCount == newCount + 1;
        bool returning = newCount == oldCount + 1;
        if ((!removing && !returning) || oldCount < 0 || newCount < 0 || into.Length < newCount
            || heldSeat < 0 || heldSeat >= fullCount
            || !Valid(oldOrder, oldOrderCount, oldCount, fullCount)
            || !Valid(newOrder, newOrderCount, newCount, fullCount)) return false;
        // The smaller arc must omit precisely the named held seat. An unrelated membership
        // change cannot borrow this transition's index map just because its count differs by one.
        for (int i = 0; i < (removing ? newCount : oldCount); i++)
            if (Seat(i, removing ? newOrder : oldOrder, removing ? newOrderCount : oldOrderCount,
                heldSeat) == heldSeat) return false;
        for (int next = 0; next < newCount; next++)
        {
            int model = Seat(next, newOrder, newOrderCount, removing ? heldSeat : -1);
            into[next] = -1;
            for (int old = 0; old < oldCount; old++)
                if (Seat(old, oldOrder, oldOrderCount, returning ? heldSeat : -1) == model)
                {
                    into[next] = old;
                    break;
                }
        }
        return true;
    }

    private static int Seat(int index, int[]? order, int count, int omitted) => count > 0
        ? order![index] : omitted >= 0 && index >= omitted ? index + 1 : index;

    private static bool Valid(int[]? order, int count, int arcCount, int fullCount)
    {
        if (count == 0) return true;
        if (order == null || count != arcCount || count > order.Length) return false;
        for (int i = 0; i < count; i++)
        {
            if (order[i] < 0 || order[i] >= fullCount) return false;
            for (int j = 0; j < i; j++) if (order[j] == order[i]) return false;
        }
        return true;
    }
}
