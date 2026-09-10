namespace GloomhavenVR.Net;

/// <summary>Exact, allocation-free edge detection for a mutable sampled fan order and gap.</summary>
internal sealed class FanPresentationSendState
{
    private readonly int[] _order = new int[NetProtocol.FanArcOrderMaxSeats];
    private bool _initialized;
    private int _count;
    private int _gap = -1;

    internal void Reset() => _initialized = false;

    internal bool HasChanged(bool hasOrder, int count, int[] order, int gap)
    {
        count = ValidCount(hasOrder, count, order);
        if (!_initialized || count != _count || gap != _gap) return true;
        for (int i = 0; i < count; i++)
            if (_order[i] != order[i]) return true;
        return false;
    }

    internal void MarkSent(bool hasOrder, int count, int[] order, int gap)
    {
        _count = ValidCount(hasOrder, count, order);
        for (int i = 0; i < _count; i++) _order[i] = order[i];
        _gap = gap;
        _initialized = true;
    }

    private static int ValidCount(bool hasOrder, int count, int[] order) =>
        hasOrder && count > 0 && count <= NetProtocol.FanArcOrderMaxSeats
        && count <= order.Length ? count : 0;
}
