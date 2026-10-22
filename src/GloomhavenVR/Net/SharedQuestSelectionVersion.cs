using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>
/// Causal register for the map's browsing opening, never a gameplay authority. Native player IDs
/// are full ConnectionId integers, not four seat indices. Only an authenticated sender authors
/// its own counter; an explicit selected-state relay retains the original author's opening.
/// A clear names the exact opening that player saw. Compare openings first,
/// then prefer the tombstone for that opening. Thus concurrent select B beats clear A in either
/// arrival order, a missed B followed by clear B still converges, and an old clear cannot close
/// a later opening of the same quest. Native admission remains outside this pure policy.
/// </summary>
internal sealed class SharedQuestSelectionVersion
{
    internal readonly struct Event
    {
        internal readonly uint Counter, ParentCounter, Key;
        internal readonly int Author, ParentAuthor;
        internal Event(uint counter, int author, uint parentCounter, int parentAuthor, uint key)
        { Counter = counter; Author = author; ParentCounter = parentCounter; ParentAuthor = parentAuthor; Key = key; }
        internal ulong Token => TokenOf(Counter, Author);
        internal ulong Opening => Key != 0u ? Token : TokenOf(ParentCounter, ParentAuthor);
    }

    private uint _clock;
    private Event _current, _own;
    private bool _haveCurrent, _haveOwn;
    private ulong _retiredThrough;
    private bool _haveRetirement;
    private readonly Dictionary<int, uint> _seen = new();

    internal uint Clock => _clock;
    internal bool HasOwn => _haveOwn;
    internal Event Own => _own;
    internal bool HasCurrent => _haveCurrent;
    internal Event Current => _current;

    internal static ulong TokenOf(uint counter, int author) => counter == 0u ? 0ul
        : ((ulong)counter << 32) | unchecked((uint)author);

    internal void BindBaseline(uint key)
    {
        if (!_haveCurrent && !_haveRetirement)
        { _current = new Event(0u, 0, 0u, 0, key); _haveCurrent = true; }
    }

    internal bool TryPublish(int author, uint key, ulong displayedOpening, out Event authored)
    {
        authored = default;
        if (author <= 0 || _clock == uint.MaxValue)
            return false; // no truncation or wrap can alias an earlier opening
        uint parent = (uint)(displayedOpening >> 32);
        int parentAuthor = parent == 0u ? 0 : unchecked((int)(uint)displayedOpening);
        if ((parent != 0u && parentAuthor <= 0) || parent > _clock)
            return false;
        authored = new Event(_clock + 1u, author, parent, parentAuthor, key);
        _own = authored;
        _haveOwn = true;
        Observe(author, authored.Counter, authored.ParentCounter, authored.ParentAuthor, key, out _);
        return true;
    }

    internal bool Observe(int sender, uint counter, uint parent, int parentAuthor, uint key,
        out Event current)
    {
        current = _current;
        bool relay = key != 0u && parent == counter && parentAuthor > 0;
        if (sender <= 0 || counter == 0u || (!relay && parent >= counter)
            || (parent == 0u ? parentAuthor != 0 : parentAuthor <= 0))
            return false;
        if (counter > _clock)
            _clock = counter;
        if (!_seen.TryGetValue(sender, out uint seen) || counter > seen)
            _seen[sender] = counter;
        // An explicit selected-state relay names the existing opening without minting one.
        // Its authenticated transport sender is a carrier, never substituted for the author.
        Event candidate = relay ? new Event(counter, parentAuthor, 0u, 0, key)
            : new Event(counter, sender, parent, parentAuthor, key);
        if (_haveRetirement && candidate.Opening <= _retiredThrough)
            return false;
        if (_haveCurrent)
        {
            int order = candidate.Opening.CompareTo(_current.Opening);
            if (order < 0)
                return false;
            if (order == 0)
            {
                // Once closed, a repeated snapshot of that same opening cannot resurrect it.
                if (_current.Key == 0u && candidate.Key != 0u)
                    return false;
                if ((_current.Key == 0u) == (candidate.Key == 0u)
                    && candidate.Token <= _current.Token)
                    return false;
            }
        }
        _current = candidate;
        _haveCurrent = true;
        current = candidate;
        return true;
    }

    /// <summary>Native map/quest retirement is a fence, not a fabricated human clear event.</summary>
    internal void RetireOwn()
    {
        if (_haveOwn)
            Retire(_own.Opening);
        _haveOwn = false;
        _own = default;
    }

    internal void RetireCurrent()
    {
        if (_haveCurrent)
            Retire(_current.Opening);
        _haveCurrent = false;
    }

    private void Retire(ulong opening)
    {
        if (!_haveRetirement || opening > _retiredThrough)
            _retiredThrough = opening;
        _haveRetirement = true;
        if (_haveCurrent && _current.Opening <= _retiredThrough)
            _haveCurrent = false;
    }

    internal void Reset()
    {
        _clock = 0u; _current = _own = default;
        _haveCurrent = _haveOwn = _haveRetirement = false;
        _retiredThrough = 0ul;
        _seen.Clear();
    }
}
