namespace GloomhavenVR.Net;

/// <summary>
/// THE PAGING OF EXTENSION RECORD 28 — the mechanism that removes the board-tuning record's
/// capacity ceiling, on both ends of the wire.
///
/// ─── THE RULING THIS SERVES ────────────────────────────────────────────────────────────────────
/// (user, 2026-08-09, verbatim) "Die 1:1 Regel besagt dass alle remote Spieler immer 1:1 das am
/// Board (mit den expliziten ausgemachten Ausnahmen) sieht. Ändert ein Spieler also die Positionen
/// für sich selber, so sollen alle anderen diese Position bei seinem board auch sehen."
///
/// That is a GUARANTEE, not a feature. Every dial a player can turn that changes how their control
/// board LOOKS has to reach every peer — with no capacity ceiling that silently drops some of them.
///
/// ─── WHAT WAS BROKEN ───────────────────────────────────────────────────────────────────────────
/// The extension tail writes each record's length in ONE BYTE, so a record's payload cannot exceed
/// 255. Record 28 had grown to EXACTLY 255 (66 dials). The sampler refused the whole record rather
/// than truncating it — the safe direction, but the OUTCOME was still that the eleventh dial of the
/// next feature could not ride at all, and the failure was invisible from inside a headset: a peer
/// simply drew that part of the board at the shipped default. The ceiling had already forced two
/// dials into a narrower container purely to fit, which is the shape of a design about to break.
///
/// ─── THE FIX: RANGE-PAGED FULL-SLICE SNAPSHOTS ─────────────────────────────────────────────────
/// Record 28's payload is now ONE PAGE of the sender's tuning rather than the whole of it. A page is
/// <c>[pageIndex][pageCount][sig u16 LE][idLo][idHi][n][n × [id][value]]</c> and it is A COMPLETE
/// STATEMENT ABOUT THE FIELD-ID RANGE <c>[idLo, idHi]</c>: every dial of the sender's whose id falls
/// in that range and differs from the shipped default is in the page, and a dial in that range that
/// is NOT in the page is at its default. The pages of one generation TILE the whole id space
/// contiguously — page 0 starts at 0, each page starts one past the previous page's idHi, the last
/// page ends at 255 — so a full set of pages is a complete snapshot with no gaps by construction.
///
/// <para>WHY FULL SLICES AND NOT DELTAS. A delta scheme needs a "this dial went back to its default"
/// message, and a lost delta is a permanent desync that nothing ever repairs. A full-slice page
/// re-states its whole range every time it goes out, so a dial returning to its default is expressed
/// by its simple ABSENCE from the next page — the same rule the record has always used — and any
/// lost page is repaired by the next pass of the cycle. There is no state on the wire that can rot.</para>
///
/// <para>WHY IT HAS NO CEILING. Per-PACKET size stays bounded (a page is ≤255 bytes, which is what
/// the tail can express); TOTAL size is bounded only by how many pages the sender chooses to cycle
/// through. Adding a dial adds bytes to a page and, at the margin, one more page — never a refusal.
/// The one remaining structural bound is the FIELD-ID SPACE (247 usable ids, see
/// <see cref="NetProtocol.BoardTuneMaxFields"/>), and that is a BUILD-TIME resource: a new dial needs
/// a new <c>Tune*</c> id constant, so running out is a compile-time event a human reads, never a
/// runtime drop a player never sees. <c>scripts/check-wire-coverage.py</c> fails the guard while any
/// range is close to full.</para>
///
/// <para>THE CONVERGENCE GUARANTEE, stated so it can be tested. The sender emits ONE page per extras
/// packet and cycles 0,1,…,pageCount−1,0,… forever; extras go out at
/// <see cref="NetProtocol.ExtrasSendRateHz"/> = 5 Hz at rest and at
/// <see cref="NetProtocol.SendRateHz"/> = 15 Hz whenever anything else in the packet is moving.
/// Therefore: A PEER THAT JOINS AT TIME T HAS THE SENDER'S COMPLETE TUNED STATE BY
/// T + pageCount × 200 ms (≤400 ms at today's two pages, ≤1.0 s at the id space's proven maximum of
/// five), AND ANY SINGLE DIAL CHANGE IS REFLECTED WITHIN THE SAME BOUND OF THE EDGE — the change
/// pre-empts the extras gate and restarts the cycle at page 0, so the bound is measured from the
/// drag, not from the next tick. Nothing is ever displayed torn: see the publication rule below.</para>
///
/// <para>"TODAY'S TWO PAGES" IS A NUMBER THAT MOVES, so it is checked rather than remembered. The
/// sampler's complete field run went 76 dials / 284 bytes to 86 / 314 when the item-cue and
/// item-berth dials were wired (record 28 ids 80 / 161..169) — the first dials added since the
/// ceiling came down — and then to 105 / 370 when the KEYCAP GEOMETRY family joined (ids 81..98 +
/// 228: the [RoundButtons] / [BoardButtons] / [BoardDashboard] / [RestButtons] cap sizes, seats and
/// press travels, which had been frozen constants in <c>Net/RemoteBoardFurniture.cs</c> for as long
/// as the ceiling stood). It STILL splits into TWO pages: page 0 fills to 246 of its 248 field
/// bytes in every one of those three censuses — the eighteen new lengths simply push twelve factor
/// fields over onto page 1 instead of the twenty-nine that used to make the split — and page 1 now
/// holds 124 of its own 248. So the bound is still ≤400 ms, with room for ~41 more 3-byte dials
/// before page 1 fills and the bound becomes ≤600 ms; <c>tests/GloomhavenVR.WireTests</c> pins the
/// split at the real census so the day it moves is the day a test says so, in a sentence, instead
/// of the day somebody re-derives it.</para>
///
/// <para>NO TORN VIEW, NO FLICKER. The receiver accumulates pages into a working set and PUBLISHES
/// only when it holds every page of ONE generation (the <c>sig</c> is the generation id — an FNV-1a
/// digest of the sender's complete field list, so pages of two different tunings can never be
/// mistaken for each other). Until the new generation is complete the PREVIOUS complete assembly
/// stays in force, so a peer dragging a slider never makes another player's copy of their board
/// flicker back to the shipped defaults for a fifth of a second — it simply changes when the whole
/// new picture has arrived. A peer who has never had a complete generation renders the shipped
/// defaults, which is exactly what every build before record 28 rendered.</para>
///
/// <para>WIRE COMPATIBILITY, DELIBERATELY NOT PRESERVED. This changes record 28's payload LAYOUT, so
/// a reader on an older build would mis-parse it (it would read the page header as a field count and
/// a field id). That is acceptable and it is acceptable for exactly one reason, stated here rather
/// than left implicit: <see cref="NetProtocol.ModBuild"/>'s handshake is BLOCKING — two peers whose
/// build numbers differ get the version-mismatch dialog and never exchange an interpreted packet
/// (<c>VersionGuard</c>). There is therefore no such thing as an old reader receiving a new page. The
/// extension tail's own skip-by-length contract is untouched: a reader that does not know id 28 at
/// all still steps over it correctly, which is the property that matters for FUTURE additions.</para>
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28's framing). Pure bytes — no Unity, no config,
/// no game model — which is what lets <c>tests/GloomhavenVR.WireTests</c> link this file verbatim and
/// pin the paging, the convergence and the boundary the old scheme used to drop at.</remarks>
internal static class BoardTunePages
{
    /// <summary>
    /// The GENERATION ID of a complete field list: an FNV-1a 32 digest of the bytes, folded to 16
    /// bits and forced non-zero (0 is the assembler's "no generation yet").
    ///
    /// <para>It answers exactly one question — "do these two pages describe the SAME tuning?" — and
    /// it answers it without the sender having to keep a counter that survives a rejoin. The LENGTH
    /// is mixed in first so that two field lists which differ only by a trailing run cannot collide
    /// trivially. A 1-in-65536 accidental collision would cost a mixed-generation assembly until the
    /// next dial edit, i.e. a cosmetic mis-seat on a rarely-touched value; a counter would have cost
    /// a desync on every rejoin, which is worse and far more likely.</para>
    /// </summary>
    internal static ushort Signature(byte[] fields, int offset, int length)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)length) * 16777619u;
            for (int i = 0; i < length; i++)
                h = (h ^ fields[offset + i]) * 16777619u;
            ushort s = (ushort)((h ^ (h >> 16)) & 0xFFFF);
            return s == 0 ? (ushort)1 : s;
        }
    }

    /// <summary>
    /// Walk the sparse field list <paramref name="fields"/> (ascending <c>[id][value]</c> pairs, no
    /// count byte) and return the byte length of the run that starts at <paramref name="from"/> and
    /// still fits one page, together with how many fields that run holds and the id of its last
    /// field. Returns false when the list is malformed (an unknown-width id) — the caller then
    /// refuses to send rather than emitting a page a receiver cannot parse.
    /// </summary>
    internal static bool MeasurePage(byte[] fields, int from, int length,
                                     out int runBytes, out int runFields, out byte lastId)
    {
        runBytes = 0;
        runFields = 0;
        lastId = 0;
        int i = from;
        while (i < length)
        {
            byte id = fields[i];
            int width = NetProtocol.BoardTuneFieldWidth(id);
            if (width == 0 || i + 1 + width > length)
                return false;                       // unknown width / torn tail — never guess
            if (runBytes + 1 + width > NetProtocol.BoardTunePageMaxFieldBytes)
                break;                              // full: the rest goes on the next page
            runBytes += 1 + width;
            runFields++;
            lastId = id;
            i += 1 + width;
        }
        // A single field can never exceed a page on its own (the widest is 7 bytes against a
        // 248-byte budget), so an empty run here means the very first field was malformed.
        return runFields > 0 || from >= length;
    }

    /// <summary>
    /// How many pages a field list of <paramref name="length"/> bytes splits into, or −1 when the
    /// list is malformed. 0 for an empty list — an untuned player writes NO record at all, which is
    /// what keeps their packet byte-identical to a pre-record sender's.
    /// </summary>
    internal static int PageCount(byte[] fields, int length)
    {
        if (length <= 0)
            return 0;
        int pages = 0;
        int at = 0;
        while (at < length)
        {
            if (!MeasurePage(fields, at, length, out int runBytes, out int runFields, out _)
                || runFields <= 0)
                return -1;
            at += runBytes;
            pages++;
            if (pages > NetProtocol.BoardTuneMaxPages)
                return -1;
        }
        return pages;
    }

    /// <summary>
    /// Write page <paramref name="page"/> of <paramref name="fields"/> into <paramref name="dest"/>
    /// and return its length, or 0 when there is nothing to write / the request is out of range.
    ///
    /// <para>The page's id RANGE is what makes it a complete statement rather than a fragment: page 0
    /// claims from id 0, every later page claims from one past its predecessor's last field id, and
    /// the LAST page claims to 255. The ranges therefore tile [0,255] with no gap and no overlap, so
    /// a receiver holding every page of a generation holds the whole tuning — and a receiver holding
    /// SOME of them still knows exactly which ranges it may believe.</para>
    /// </summary>
    internal static int WritePage(byte[] fields, int length, int page, ushort signature, byte[] dest)
    {
        if (fields == null || dest == null || length <= 0 || page < 0)
            return 0;

        int total = PageCount(fields, length);
        if (total <= 0 || page >= total)
            return 0;

        // Walk to the requested page, carrying the id the previous page ended on: the next page's
        // idLo is one past it, which is what closes the range with no gap.
        int at = 0;
        int idLo = 0;
        int runBytes = 0, runFields = 0;
        byte lastId = 0;
        for (int p = 0; p <= page; p++)
        {
            if (p > 0)
            {
                idLo = lastId + 1;
                at += runBytes;
            }
            if (!MeasurePage(fields, at, length, out runBytes, out runFields, out lastId))
                return 0;
        }
        bool isLast = page == total - 1;
        byte idHi = isLast ? (byte)255 : lastId;

        if (dest.Length < NetProtocol.BoardTunePageHeaderBytes + runBytes)
            return 0;

        dest[NetProtocol.BoardTunePageIndexAt] = (byte)page;
        dest[NetProtocol.BoardTunePageCountAt] = (byte)total;
        dest[NetProtocol.BoardTunePageSigAt] = (byte)(signature & 0xFF);
        dest[NetProtocol.BoardTunePageSigAt + 1] = (byte)(signature >> 8);
        dest[NetProtocol.BoardTunePageIdLoAt] = (byte)idLo;
        dest[NetProtocol.BoardTunePageIdHiAt] = idHi;
        dest[NetProtocol.BoardTunePageFieldCountAt] = (byte)runFields;
        System.Buffer.BlockCopy(fields, at, dest, NetProtocol.BoardTunePageHeaderBytes, runBytes);
        return NetProtocol.BoardTunePageHeaderBytes + runBytes;
    }
}

/// <summary>
/// THE SENDER'S SIDE of record 28's paging: holds the sampled field list, splits it into pages and
/// hands out ONE page per extras packet, cycling forever.
///
/// <para>CYCLING FOREVER rather than sending each page once is not waste, it is the repair
/// mechanism: the sender cannot know when a peer joins, and the transport does not promise delivery
/// of an extras packet. A peer who arrives mid-cycle, or who loses a page, therefore converges on
/// the next pass with no handshake, no request/response and no per-peer state on the sender. It also
/// costs nothing over the previous build, which re-sent the WHOLE record on every packet.</para>
///
/// <para>A CHANGE RESTARTS THE CYCLE AT PAGE 0 — not because page 0 is special, but because it makes
/// the convergence bound measurable from the EDGE (the drag) rather than from wherever the cursor
/// happened to be: pageCount packets after the change, every peer has the whole new picture.</para>
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28). No Unity, no config — see
/// <see cref="BoardTunePages"/>.</remarks>
internal sealed class BoardTunePageSender
{
    private readonly byte[] _fields = new byte[NetProtocol.BoardTuneMaxFieldBytes];
    private int _length;
    private ushort _signature;
    private int _pageCount;
    private int _cursor;

    /// <summary>Pages the current tuning splits into (0 = nothing tuned ⇒ no record at all).</summary>
    internal int PageCount => _pageCount;

    /// <summary>Generation id of the current tuning — the digest every page of it carries.</summary>
    internal ushort Signature => _signature;

    /// <summary>Total field bytes the current tuning occupies (diagnostics / logs).</summary>
    internal int FieldBytes => _length;

    /// <summary>Fields in the current tuning (diagnostics / logs).</summary>
    internal int FieldCount { get; private set; }

    /// <summary>True when the last <see cref="Update"/> could not represent the tuning at all. The
    /// caller must then say so LOUDLY and send nothing — the one thing this design must never do is
    /// drop a dial in silence.</summary>
    internal bool Overflowed { get; private set; }

    /// <summary>
    /// Adopt a freshly sampled field list. Returns true when the tuning CHANGED (a new generation),
    /// which is the caller's cue to log and to let the change pre-empt the extras gate.
    ///
    /// <para>Byte-compares against the field list already held, so re-sampling an unchanged config —
    /// which is what happens on every send — costs one memcmp and allocates nothing.</para>
    /// </summary>
    internal bool Update(byte[]? fields, int length)
    {
        if (fields == null || length <= 0)
        {
            // NOTHING TUNED (or nothing sampled yet — a packet can go out before CardsConfig.Bind
            // completes). Not an error: it is the ordinary state of an untuned player, and it is
            // signalled to peers by writing no record at all.
            if (_length == 0 && _pageCount == 0 && !Overflowed)
                return false;
            Reset();
            return true;
        }
        if (length > _fields.Length)
        {
            // CANNOT HAPPEN while every dial has a distinct id: the buffer is the id space's own
            // worst case. If it ever does, the honest answer is to refuse and shout — see the
            // caller's log — because the alternative is a silently short tuning.
            Overflowed = true;
            _length = 0;
            _pageCount = 0;
            FieldCount = 0;
            _signature = 0;
            return true;
        }

        bool same = length == _length && !Overflowed;
        if (same)
        {
            for (int i = 0; i < length; i++)
            {
                if (_fields[i] != fields[i])
                {
                    same = false;
                    break;
                }
            }
        }
        if (same)
            return false;

        Overflowed = false;
        for (int i = 0; i < length; i++)
            _fields[i] = fields[i];
        _length = length;
        _signature = length > 0 ? BoardTunePages.Signature(_fields, 0, length) : (ushort)0;
        int pages = BoardTunePages.PageCount(_fields, length);
        if (pages < 0)
        {
            // A malformed field list (an id whose width this build does not know). Refuse the whole
            // tuning rather than emit a page a receiver would abandon halfway — same direction the
            // sampler has always taken, and it is loud at the caller.
            Overflowed = true;
            _pageCount = 0;
            FieldCount = 0;
            _length = 0;
            return true;
        }
        _pageCount = pages;
        FieldCount = CountFields(_fields, length);
        _cursor = 0;
        return true;
    }

    /// <summary>Forget the held tuning so the next <see cref="Update"/> reads as a change. Called
    /// when a session ends: the next session must re-state the tuning from scratch (a peer at the
    /// new table has never seen a page of ours) and its change-gated log must fire once.</summary>
    internal void Reset()
    {
        _length = 0;
        _pageCount = 0;
        _signature = 0;
        _cursor = 0;
        FieldCount = 0;
        Overflowed = false;
    }

    /// <summary>Write the NEXT page into <paramref name="dest"/> and advance the cursor; returns its
    /// length, or 0 when nothing is tuned (⇒ no record, ⇒ the extension tail does not even open for
    /// it, ⇒ an untuned packet is byte-identical to a pre-record sender's).</summary>
    internal int NextPage(byte[] dest)
    {
        if (_pageCount <= 0 || Overflowed)
            return 0;
        if (_cursor >= _pageCount)
            _cursor = 0;
        int n = BoardTunePages.WritePage(_fields, _length, _cursor, _signature, dest);
        _cursor++;
        if (_cursor >= _pageCount)
            _cursor = 0;
        return n;
    }

    private static int CountFields(byte[] fields, int length)
    {
        int n = 0, i = 0;
        while (i < length)
        {
            int width = NetProtocol.BoardTuneFieldWidth(fields[i]);
            if (width == 0 || i + 1 + width > length)
                break;
            n++;
            i += 1 + width;
        }
        return n;
    }
}

/// <summary>
/// THE RECEIVER'S SIDE of record 28's paging: one per peer, it accumulates pages and publishes a
/// complete tuning — in exactly the <c>[n][n × [id][value]]</c> shape <see cref="RemoteBoardTuning"/>
/// and <see cref="NetProtocol.FindBoardTuneField"/> already read, so not one consumer of a dial had
/// to change for any of this.
///
/// <para>THE PUBLICATION RULE IS THE WHOLE DESIGN: an assembly is published only when every page of
/// ONE generation is held. A generation is identified by the sender's digest, so pages of two
/// different tunings cannot be blended; and while a new generation is still arriving the PREVIOUS
/// complete one stays published, so nothing on screen is ever torn and nothing flickers back to the
/// shipped defaults mid-drag.</para>
///
/// <para>BOUNDED BY THE ID SPACE, NOT BY A GUESS. The accumulator refuses a generation claiming more
/// than <see cref="NetProtocol.BoardTuneMaxPages"/> pages or more than
/// <see cref="NetProtocol.BoardTuneMaxFieldBytes"/> of fields. Both numbers are DERIVED from the 247
/// usable field ids and their widths, so a well-formed sender can never approach them; a sender that
/// does is corrupt or hostile, and it is refused with a log rather than allowed to size an
/// allocation from the wire.</para>
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28). No Unity, no config — see
/// <see cref="BoardTunePages"/>.</remarks>
internal sealed class BoardTunePageAssembler
{
    private ushort _generation;
    private int _pageCount;
    private readonly byte[][] _slice = new byte[NetProtocol.BoardTuneMaxPages][];
    private readonly int[] _sliceLength = new int[NetProtocol.BoardTuneMaxPages];
    private readonly int[] _sliceFields = new int[NetProtocol.BoardTuneMaxPages];
    private readonly bool[] _seen = new bool[NetProtocol.BoardTuneMaxPages];
    private int _seenCount;

    private byte[] _assembled = System.Array.Empty<byte>();
    private int _assembledLength;

    /// <summary>The complete tuning payload — <c>[n][n × [id][value]]</c>, ready for
    /// <see cref="NetProtocol.FindBoardTuneField"/>. Empty until a first generation completes.</summary>
    internal byte[] Assembled => _assembled;

    /// <summary>Valid length of <see cref="Assembled"/>; 0 means "nothing published yet" ⇒ every
    /// consumer keeps its own shipped default, which is precisely the pre-record picture.</summary>
    internal int AssembledLength => _assembledLength;

    /// <summary>Pages the generation currently being accumulated claims (0 = none seen).</summary>
    internal int PageCount => _pageCount;

    /// <summary>Pages of that generation actually held. <c>PagesSeen == PageCount</c> is the
    /// convergence condition, and it is what the wire tests assert against.</summary>
    internal int PagesSeen => _seenCount;

    /// <summary>Generation id being accumulated (the sender's digest; 0 = none).</summary>
    internal ushort Generation => _generation;

    /// <summary>Set when a page had to be refused. One-shot per generation at the caller's log — the
    /// design's promise is that a thing it cannot represent is LOUD, never silent.</summary>
    internal bool Refused { get; private set; }

    /// <summary>Forget everything, published assembly included. Called when a peer stops sending
    /// record 28 at all, which means exactly "every dial is back at its shipped default" — the same
    /// signal an untuned sender has always given by omitting the record.</summary>
    internal void Reset()
    {
        _generation = 0;
        _pageCount = 0;
        _seenCount = 0;
        _assembledLength = 0;
        Refused = false;
        for (int p = 0; p < _seen.Length; p++)
            _seen[p] = false;
    }

    /// <summary>
    /// Accept one record-28 page. Returns true when THIS page completed a generation whose assembled
    /// bytes differ from what is currently published — i.e. exactly when the caller must re-resolve
    /// <see cref="RemoteBoardTuning"/> and bump its revision. False is the normal answer: a repeat of
    /// a page already held costs a byte compare and allocates nothing.
    /// </summary>
    internal bool Accept(byte[]? page, int offset, int length)
    {
        if (page == null || offset < 0 || length < NetProtocol.BoardTunePageHeaderBytes
            || offset + length > page.Length)
        {
            Refused = true;
            return false;
        }

        int index = page[offset + NetProtocol.BoardTunePageIndexAt];
        int count = page[offset + NetProtocol.BoardTunePageCountAt];
        ushort sig = (ushort)(page[offset + NetProtocol.BoardTunePageSigAt]
                              | (page[offset + NetProtocol.BoardTunePageSigAt + 1] << 8));
        int fieldCount = page[offset + NetProtocol.BoardTunePageFieldCountAt];
        int fieldBytes = length - NetProtocol.BoardTunePageHeaderBytes;

        // STRUCTURAL REFUSAL, never a partial belief. Every bound here is derived from the field-id
        // space (see the class doc), so a well-formed sender cannot trip one.
        if (count < 1 || count > NetProtocol.BoardTuneMaxPages || index >= count
            || fieldBytes > NetProtocol.BoardTunePageMaxFieldBytes
            || fieldCount > NetProtocol.BoardTuneMaxFields
            || !FieldsWellFormed(page, offset + NetProtocol.BoardTunePageHeaderBytes, fieldBytes,
                                 fieldCount))
        {
            Refused = true;
            return false;
        }

        if (sig != _generation || count != _pageCount)
        {
            // A NEW GENERATION. The working set is cleared, but the PUBLISHED assembly is left
            // standing: that is what keeps another player's board from flickering back to the
            // shipped defaults while its owner drags a slider.
            _generation = sig;
            _pageCount = count;
            _seenCount = 0;
            Refused = false;
            for (int p = 0; p < _seen.Length; p++)
                _seen[p] = false;
        }

        StoreSlice(index, page, offset + NetProtocol.BoardTunePageHeaderBytes, fieldBytes, fieldCount);
        if (!_seen[index])
        {
            _seen[index] = true;
            _seenCount++;
        }
        if (_seenCount < _pageCount)
            return false;

        return Publish();
    }

    /// <summary>Validate a page's field run at the widths its ids declare — the same walk
    /// <see cref="NetProtocol.FindBoardTuneField"/> performs, done ONCE here so a torn or
    /// unknown-width page is refused whole instead of assembling into a payload that every reader
    /// would silently abandon halfway.</summary>
    private static bool FieldsWellFormed(byte[] page, int at, int bytes, int declaredFields)
    {
        int i = 0, n = 0;
        while (i < bytes)
        {
            int width = NetProtocol.BoardTuneFieldWidth(page[at + i]);
            if (width == 0 || i + 1 + width > bytes)
                return false;
            i += 1 + width;
            n++;
        }
        return n == declaredFields;
    }

    private void StoreSlice(int index, byte[] page, int at, int bytes, int fields)
    {
        byte[]? dst = _slice[index];
        if (dst != null && _sliceLength[index] == bytes)
        {
            bool same = true;
            for (int b = 0; b < bytes; b++)
            {
                if (dst[b] != page[at + b])
                {
                    same = false;
                    break;
                }
            }
            if (same)
            {
                _sliceFields[index] = fields;
                return;                          // the steady-state path: no copy, no allocation
            }
        }
        if (dst == null || dst.Length < bytes)
            dst = new byte[bytes < 32 ? 32 : bytes];
        System.Buffer.BlockCopy(page, at, dst, 0, bytes);
        _slice[index] = dst;
        _sliceLength[index] = bytes;
        _sliceFields[index] = fields;
    }

    /// <summary>Concatenate the held slices into <c>[n][fields]</c>. Returns true only when the
    /// result DIFFERS from what is already published — a sender re-cycling an unchanged tuning must
    /// not make its peers tear down and rebuild a control board five times a second.</summary>
    private bool Publish()
    {
        int bytes = 0, fields = 0;
        for (int p = 0; p < _pageCount; p++)
        {
            bytes += _sliceLength[p];
            fields += _sliceFields[p];
        }
        if (bytes > NetProtocol.BoardTuneMaxFieldBytes || fields > NetProtocol.BoardTuneMaxFields)
        {
            Refused = true;
            return false;
        }

        int total = 1 + bytes;
        if (_assembledLength == total)
        {
            bool same = _assembled[0] == (byte)fields;
            int w = 1;
            for (int p = 0; same && p < _pageCount; p++)
            {
                byte[] s = _slice[p]!;
                for (int b = 0; b < _sliceLength[p]; b++)
                {
                    if (_assembled[w + b] != s[b])
                    {
                        same = false;
                        break;
                    }
                }
                w += _sliceLength[p];
            }
            if (same)
                return false;
        }

        if (_assembled.Length < total)
            _assembled = new byte[total];
        _assembled[0] = (byte)fields;
        int at = 1;
        for (int p = 0; p < _pageCount; p++)
        {
            System.Buffer.BlockCopy(_slice[p]!, 0, _assembled, at, _sliceLength[p]);
            at += _sliceLength[p];
        }
        _assembledLength = total;
        return true;
    }

    /// <summary>One-line dump for the receive log — says at a glance whether a peer's board is being
    /// drawn from a complete generation or is still converging.</summary>
    public override string ToString() =>
        _pageCount <= 0
            ? "no pages"
            : $"generation {_generation:X4}, {_seenCount}/{_pageCount} page(s), " +
              (_assembledLength > 0
                  ? $"{_assembled[0]} dial(s) published ({_assembledLength} B)"
                  : "nothing published yet");
}
