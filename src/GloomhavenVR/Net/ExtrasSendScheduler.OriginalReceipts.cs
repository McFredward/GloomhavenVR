using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

internal sealed partial class ExtrasSendScheduler
{
    // Exact-original receipts share the ordinary bounded event clock. They are
    // metadata acknowledgements, not another gameplay action or send budget.
    private readonly Queue<byte[]> _originalReceipts = new();
    private double _nextOriginalReceipt;

    private void EnqueueOriginalReceipt(byte[] bytes, int length)
    {
        if (!TownServices.TownServiceOriginalReceiptCodec.TryRead(bytes, length, out _))
            throw new ArgumentException("Invalid exact town-original receipt.", nameof(bytes));
        if (_originalReceipts.Count >= 64)
            throw new InvalidOperationException("Exact town-original receipt queue is full.");
        var copy = new byte[length];
        Buffer.BlockCopy(bytes, 0, copy, 0, length);
        _originalReceipts.Enqueue(copy);
    }

    private byte[]? TakeOriginalReceipt(double now)
    {
        if (now < _nextOriginalReceipt || _originalReceipts.Count == 0) return null;
        _nextOriginalReceipt = now + .05;
        return _originalReceipts.Dequeue();
    }

    private void ClearOriginalReceipts()
    { _originalReceipts.Clear(); _nextOriginalReceipt = 0; }
}
