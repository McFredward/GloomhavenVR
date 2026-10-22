namespace GloomhavenVR.Net;

/// <summary>
/// Record121 carries each posed window's captured render reference. Record119's scale is
/// the coordinate frame's zoom, which can differ from the reference retained when a window
/// opened. Reusing that coordinate scale (or the receiver's current scene scale) changes
/// physical geometry while leaving the reconstructed pose correct. These six optional
/// references are an atomic, allocation-free part of the same presence snapshot.
/// </summary>
internal struct SharedWindowScaleContext
{
    internal const byte DefinedMask = 63;
    internal const int MaxPayloadBytes = 25;
    internal byte Mask;
    private float _kind1, _kind2, _kind3, _kind4, _kind5, _kind6;

    internal bool Has(int kind) => (Mask & SharedWindowPoseContext.KindBit(kind)) != 0;

    internal float Reference(int kind)
    {
        if (!Has(kind)) return 0f;
        return kind switch
        {
            1 => _kind1, 2 => _kind2, 3 => _kind3,
            4 => _kind4, 5 => _kind5, 6 => _kind6, _ => 0f
        };
    }

    // Invalid numeric input remains invalid rather than silently dropping only one member
    // of a cohort; the writer validates the complete context before changing any bytes.
    internal void Set(int kind, float value)
    {
        byte bit = SharedWindowPoseContext.KindBit(kind);
        if (bit == 0) return;
        Mask |= bit;
        switch (kind)
        {
            case 1: _kind1 = value; break;
            case 2: _kind2 = value; break;
            case 3: _kind3 = value; break;
            case 4: _kind4 = value; break;
            case 5: _kind5 = value; break;
            case 6: _kind6 = value; break;
        }
    }

    internal bool Valid
    {
        get
        {
            if (Mask == 0 || (Mask & ~DefinedMask) != 0) return false;
            for (int kind = 1; kind <= 6; kind++)
            {
                if (!Has(kind)) continue;
                float value = Reference(kind);
                if (!SharedWindowPoseContext.Finite(value) || value < 0.0001f || value > 1000000f)
                    return false;
            }
            return true;
        }
    }

    private int PayloadBytes
    {
        get
        {
            int length = 1;
            for (int kind = 1; kind <= 6; kind++)
                if (Has(kind)) length += 4;
            return length;
        }
    }

    internal static bool Write(byte[] bytes, ref int at, in SharedWindowScaleContext context)
    {
        if (!context.Valid) return false;
        int length = context.PayloadBytes;
        if (at < 0 || at > bytes.Length - length - 2) return false;
        bytes[at++] = NetProtocol.ExtIdSharedWindowScaleContext;
        bytes[at++] = (byte)length;
        bytes[at++] = context.Mask;
        for (int kind = 1; kind <= 6; kind++)
            if (context.Has(kind)) AvatarSerializer.WriteF32(bytes, ref at, context.Reference(kind));
        return true;
    }

    internal static bool TryRead(byte[] bytes, int at, int length, out SharedWindowScaleContext context)
    {
        context = default;
        if (length < 1 || at < 0 || at > bytes.Length - length) return false;
        var read = new SharedWindowScaleContext { Mask = bytes[at++] };
        if (read.Mask == 0 || (read.Mask & ~DefinedMask) != 0 || length < read.PayloadBytes)
            return false;
        for (int kind = 1; kind <= 6; kind++)
            if (read.Has(kind)) read.Set(kind, AvatarSerializer.ReadF32(bytes, ref at));
        if (!read.Valid) return false;
        context = read;
        return true;
    }
}
