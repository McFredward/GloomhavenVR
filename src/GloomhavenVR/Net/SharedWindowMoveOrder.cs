namespace GloomhavenVR.Net;

internal enum SharedWindowMovePhase : byte
{
    Initial,
    Held,
    Released,
    Automatic,
}

/// <summary>Ordering only; original19/21/72/73 still own the pose and native content.
/// A claim is stable for one gesture, while its sequence orders every pose snapshot.
/// Neither receiver arrival time nor the author's frame rate elects the mover.</summary>
internal struct SharedWindowMoveEntry
{
    internal byte Kind;
    internal SharedWindowMovePhase Phase;
    internal uint ContentKey;
    internal int OpeningAuthor;
    internal uint Opening;
    internal uint Claim;
    internal uint Sequence;
    // Automatic room making succeeds the observed manual release, not every newer grip.
    internal uint ParentClaim;
    internal int ParentAuthor;

    internal bool Valid => Kind >= 1 && Kind <= 6 && Phase <= SharedWindowMovePhase.Automatic
        && ContentKey != 0 && OpeningAuthor > 0 && Opening != 0 && Sequence != 0
        && (Phase == SharedWindowMovePhase.Initial ? Claim == 0 : Claim != 0)
        && (Phase == SharedWindowMovePhase.Automatic
            ? (ParentClaim == 0 ? ParentAuthor == 0 : ParentAuthor > 0)
            : ParentClaim == 0 && ParentAuthor == 0);

    internal bool SameOpening(in SharedWindowMoveEntry other) => Kind == other.Kind
        && ContentKey == other.ContentKey && OpeningAuthor == other.OpeningAuthor && Opening == other.Opening;
}

/// <summary>Additive123. A malformed row, duplicate kind or truncated payload rejects the
/// complete companion rather than lending one gesture's order to another window.</summary>
internal sealed class SharedWindowMoveOrder
{
    internal const int MaxEntries = 6, EntryBytes = 30;
    internal readonly SharedWindowMoveEntry[] Entries;
    internal SharedWindowMoveOrder(SharedWindowMoveEntry[] entries) => Entries = entries;

    internal bool Valid
    {
        get
        {
            if (Entries == null || Entries.Length > MaxEntries) return false;
            int kinds = 0;
            foreach (SharedWindowMoveEntry row in Entries)
            {
                if (!row.Valid || (kinds & (1 << row.Kind)) != 0) return false;
                kinds |= 1 << row.Kind;
            }
            return true;
        }
    }

    internal bool TryGet(int kind, uint key, out SharedWindowMoveEntry entry)
    {
        foreach (SharedWindowMoveEntry row in Entries)
            if (row.Kind == kind && row.ContentKey == key) { entry = row; return true; }
        entry = default;
        return false;
    }

    internal static bool Write(byte[] bytes, ref int at, SharedWindowMoveOrder order)
    {
        if (!order.Valid) return false;
        int length = 1 + EntryBytes * order.Entries.Length;
        if (at < 0 || at > bytes.Length - length - 2) return false;
        bytes[at++] = NetProtocol.ExtIdSharedWindowMoveOrder;
        bytes[at++] = (byte)length;
        bytes[at++] = (byte)order.Entries.Length;
        foreach (SharedWindowMoveEntry row in order.Entries)
        {
            bytes[at++] = row.Kind;
            bytes[at++] = (byte)row.Phase;
            AvatarSerializer.WriteU32(bytes, ref at, row.ContentKey);
            AvatarSerializer.WriteU32(bytes, ref at, (uint)row.OpeningAuthor);
            AvatarSerializer.WriteU32(bytes, ref at, row.Opening);
            AvatarSerializer.WriteU32(bytes, ref at, row.Claim);
            AvatarSerializer.WriteU32(bytes, ref at, row.Sequence);
            AvatarSerializer.WriteU32(bytes, ref at, row.ParentClaim);
            AvatarSerializer.WriteU32(bytes, ref at, (uint)row.ParentAuthor);
        }
        return true;
    }

    internal static bool TryRead(byte[] bytes, int at, int length, out SharedWindowMoveOrder? order)
    {
        order = null;
        if (at < 0 || length < 1 || at > bytes.Length - length) return false;
        int count = bytes[at++];
        if (count > MaxEntries || length != 1 + EntryBytes * count) return false;
        var rows = new SharedWindowMoveEntry[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = new SharedWindowMoveEntry { Kind = bytes[at++], Phase = (SharedWindowMovePhase)bytes[at++],
                ContentKey = AvatarSerializer.ReadU32(bytes, ref at),
                OpeningAuthor = unchecked((int)AvatarSerializer.ReadU32(bytes, ref at)),
                Opening = AvatarSerializer.ReadU32(bytes, ref at),
                Claim = AvatarSerializer.ReadU32(bytes, ref at), Sequence = AvatarSerializer.ReadU32(bytes, ref at),
                ParentClaim = AvatarSerializer.ReadU32(bytes, ref at),
                ParentAuthor = unchecked((int)AvatarSerializer.ReadU32(bytes, ref at)) };
        }
        var read = new SharedWindowMoveOrder(rows);
        if (!read.Valid) return false;
        order = read;
        return true;
    }
}
