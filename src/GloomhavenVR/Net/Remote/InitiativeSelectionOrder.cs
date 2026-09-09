using System;

namespace GloomhavenVR.Net;

/// <summary>One selection window's player order and settled native slots. A card commit may
/// re-sort the viewer's source rows or briefly omit record27; neither changes the held picture.
/// Only a new round, leaving selection, or an actual visible membership change starts a new latch.
/// No initiative value, card identity, viewer ownership flag or elapsed timeout is an input.</summary>
internal sealed class InitiativeSelectionOrder
{
    private readonly int[] _ownerIds;
    private readonly float[] _slotX;
    private int _count, _round = int.MinValue;

    internal InitiativeSelectionOrder(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ownerIds = new int[capacity]; _slotX = new float[capacity];
    }

    internal bool TryResolve(bool selecting, int round, bool settled, int[] rowIds, float[] sourceSlotX,
        int rowCount, int[] ownerIds, int ownerCount, float[] outputByRow)
    {
        if (!selecting) { Reset(); return false; }
        if (_round != round) { _count = 0; _round = round; }
        if (!ValidIds(rowIds, rowCount) || sourceSlotX.Length < rowCount || outputByRow.Length < rowCount)
            return false;
        for (int i = 0; i < rowCount; i++)
            if (float.IsNaN(sourceSlotX[i]) || float.IsInfinity(sourceSlotX[i])) return false;

        if (_count != rowCount || !SameSet(_ownerIds, _count, rowIds, rowCount)) _count = 0;
        if (_count == 0)
        {
            // A newly appearing track has no prior picture to hold. Wait for the ORIGINAL
            // layout to settle rather than inventing slots from an intermediate tween pose.
            if (!settled || !ValidIds(ownerIds, ownerCount)
                || !SameSet(ownerIds, ownerCount, rowIds, rowCount)) return false;
            _count = rowCount;
            Array.Copy(ownerIds, _ownerIds, _count);
            Array.Copy(sourceSlotX, _slotX, _count);
        }
        // Resolve every row before the caller writes a transform. SameSet guarantees one match.
        for (int row = 0; row < rowCount; row++)
            for (int rank = 0; rank < _count; rank++)
                if (rowIds[row] == _ownerIds[rank]) { outputByRow[row] = _slotX[rank]; break; }
        return true;
    }

    internal void Reset() { _count = 0; _round = int.MinValue; }

    private bool ValidIds(int[] ids, int count)
    {
        if (ids == null || count < 1 || count > _ownerIds.Length || count > ids.Length) return false;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == 0) return false;
            for (int k = 0; k < i; k++) if (ids[k] == ids[i]) return false;
        }
        return true;
    }
    private static bool SameSet(int[] a, int countA, int[] b, int countB)
    {
        if (countA != countB) return false;
        for (int i = 0; i < countA; i++)
        {
            bool found = false;
            for (int k = 0; k < countB; k++) if (a[i] == b[k]) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
