using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>A per-window causal register. Receiver time is used only to expire a live grip,
/// never to order claims. A winning receipt survives the sender leaving, so an older mover
/// cannot pull a settled window back. A genuine new grab observes the clock and advances it.</summary>
internal sealed class SharedWindowMoveRegister
{
    internal const float GripLeaseSeconds = 3f;
    private readonly int _local;
    private readonly byte _kind;
    private readonly Dictionary<int, Receipt> _peers = new();
    private readonly Dictionary<int, uint> _retired = new();
    private uint _clock, _nextOpening, _sequence;
    private uint _key;
    private int _openingAuthor;
    private uint _opening;
    private bool _view, _localClaim;
    private bool _announces;
    private int _initialPublisher;
    private uint _claim;
    private SharedWindowMovePhase _phase;
    private int _winner;
    private uint _winningClaim;
    private bool _winnerAutomatic, _closedByAutomatic;
    private int _automaticAuthor = -1;
    private uint _parentClaim, _winningBaseClaim;
    private int _parentAuthor, _winningBaseAuthor;

    private readonly struct Receipt
    {
        internal readonly SharedWindowMoveEntry Row;
        internal readonly float At;
        internal Receipt(in SharedWindowMoveEntry row, float at) { Row = row; At = at; }
    }

    internal SharedWindowMoveRegister(int local, byte kind) { _local = local; _kind = kind; }
    internal uint ContentKey => _key;
    internal bool Ready => _view && _openingAuthor > 0 && _opening != 0;
    internal bool HasLocalClaim => Ready && _localClaim;
    internal bool LocalWins => HasLocalClaim && (_winner == 0 || _winner == _local);
    internal SharedWindowMovePhase LocalPhase => _phase;
    internal int Winner => _winner;

    internal static bool Newer(uint value, uint previous) => value != previous && unchecked((int)(value - previous)) > 0;
    internal static int Compare(uint left, int leftAuthor, uint right, int rightAuthor)
        => left == right ? leftAuthor.CompareTo(rightAuthor) : left == 0 ? -1 : right == 0 ? 1
            : Newer(left, right) ? 1 : -1;
    private static uint Next(uint value) { unchecked { value++; } return value == 0 ? 1u : value; }
    private void See(uint value) { if (_clock == 0 || Newer(value, _clock)) _clock = value; }

    /// <summary>Native content identity, independent of the grabbable host/composite instance.</summary>
    internal void Open(uint key, int openingAuthor)
    {
        if (key == 0) { Close(); return; }
        _announces = openingAuthor == _local; _initialPublisher = openingAuthor;
        if (_view && _key == key)
        {
            if (!Ready && _announces) { _nextOpening = Next(_nextOpening); AdoptOpening(_local, _nextOpening); }
            return;
        }
        Close();
        _announces = openingAuthor == _local; _initialPublisher = openingAuthor;
        _view = true; _key = key;
        if (openingAuthor == _local)
        {
            _nextOpening = Next(_nextOpening);
            AdoptOpening(_local, _nextOpening);
        }
    }

    internal void Close()
    {
        if (_openingAuthor > 0 && _opening != 0) _retired[_openingAuthor] = _opening;
        _view = false; _key = 0; _openingAuthor = 0; _opening = 0;
        _localClaim = false; _claim = 0; _winner = 0; _winningClaim = 0; _winnerAutomatic = false;
        _peers.Clear();
    }

    private void AdoptOpening(int author, uint opening)
    {
        _openingAuthor = author; _opening = opening;
        _localClaim = false; _claim = 0; _winner = 0; _winningClaim = 0; _winnerAutomatic = false;
        _winningBaseClaim = 0; _winningBaseAuthor = 0; _closedByAutomatic = false;
        _peers.Clear();
        _phase = SharedWindowMovePhase.Initial;
    }

    internal bool Observe(int sender, in SharedWindowMoveEntry row, int openingAuthor, float now, bool allowOpeningRelay = false)
    {
        if (sender <= 0 || sender == _local || !row.Valid || row.Kind != _kind
            || !_view || row.ContentKey != _key) return false;
        if (row.Phase == SharedWindowMovePhase.Initial && sender != openingAuthor) return false;
        bool sameOpening = row.OpeningAuthor == _openingAuthor && row.Opening == _opening;
        if (!sameOpening)
        {
            // Only the current placement author announces an opening, including when a flat
            // native host requires the existing single compatible VR surrogate.
            if (sender != openingAuthor || (row.OpeningAuthor != openingAuthor && !allowOpeningRelay)) return false;
            if (_retired.TryGetValue(row.OpeningAuthor, out uint retired) && !Newer(row.Opening, retired)) return false;
            if (_openingAuthor == row.OpeningAuthor && _opening != 0 && !Newer(row.Opening, _opening)) return false;
            AdoptOpening(row.OpeningAuthor, row.Opening);
        }
        if (_peers.TryGetValue(sender, out Receipt previous))
        {
            if (!Newer(row.Sequence, previous.Row.Sequence)) return false;
            if (row.Claim != previous.Row.Claim && previous.Row.Claim != 0
                && (row.Claim == 0 || !Newer(row.Claim, previous.Row.Claim))) return false;
        }
        // The native publisher can change between local samples. Keep the receive-side
        // initial-pose gate on that current role without replacing the accepted opening/claim.
        _initialPublisher = openingAuthor;
        _peers[sender] = new Receipt(in row, now);
        // Initial has no manual claim. Its existing monotone sequence also carries the
        // publisher's observed clock, so a cold late viewer can make a genuinely newer grab
        // after publisher handover. Only fully validated receipts may advance that clock.
        See(row.Phase == SharedWindowMovePhase.Initial ? row.Sequence : row.Claim);
        Elect(sender, in row, now);
        return true;
    }

    /// <summary>Eligibility belongs to the current native map census. A former automatic
    /// author cannot retain authority through its larger clock or ID after that census changes.
    /// The accepted manual release basis remains a watermark, never a fabricated new gesture.</summary>
    internal void SetAutomaticAuthor(int author, float now)
    {
        if (_automaticAuthor == author) return;
        _automaticAuthor = author;
        if (!_winnerAutomatic || _winner == author) return;
        if (_winner == _local && _phase == SharedWindowMovePhase.Automatic) _localClaim = false;
        _winner = _winningBaseAuthor; _winningClaim = _winningBaseClaim;
        _winnerAutomatic = false; _closedByAutomatic = _winningBaseClaim != 0;
        foreach (KeyValuePair<int, Receipt> peer in _peers)
        { SharedWindowMoveEntry row = peer.Value.Row; Elect(peer.Key, in row, now); }
    }

    private bool WinnerHeld(float now)
        => !_closedByAutomatic && (_winner == _local ? _localClaim && _phase == SharedWindowMovePhase.Held
            : _peers.TryGetValue(_winner, out Receipt owner) && owner.Row.Phase == SharedWindowMovePhase.Held
                && now - owner.At <= GripLeaseSeconds);

    private void Elect(int sender, in SharedWindowMoveEntry row, float now)
    {
        if (row.Phase == SharedWindowMovePhase.Initial) return;
        // Auto is causally after the manual release it observed. Its own (potentially much
        // higher) Lamport clock never ranks ahead of a fresh manual gesture on an older basis.
        bool automatic = row.Phase == SharedWindowMovePhase.Automatic;
        if (automatic && _automaticAuthor >= 0 && sender != _automaticAuthor) return;
        uint basis = automatic ? row.ParentClaim : row.Claim;
        int basisAuthor = automatic ? row.ParentAuthor : sender;
        int comparison = Compare(basis, basisAuthor, _winningBaseClaim, _winningBaseAuthor);
        if (_winner == 0 || comparison > 0 || (comparison == 0
            && ((automatic && !_winnerAutomatic) || (automatic == _winnerAutomatic
                && Compare(row.Claim, sender, _winningClaim, _winner) > 0))))
        {
            _winner = sender; _winningClaim = row.Claim; _winnerAutomatic = automatic; _closedByAutomatic = false;
            _winningBaseClaim = basis; _winningBaseAuthor = basisAuthor;
        }
        if (_winner != _local) _localClaim = false;
    }

    internal bool CanBegin(float now)
    {
        if (!Ready) return false;
        if (_winner == _local && _localClaim) return true;
        return !WinnerHeld(now);
    }

    internal bool Begin(float now, bool automatic = false)
    {
        if (!CanBegin(now) || (automatic && (WinnerHeld(now)
            || (_automaticAuthor >= 0 && _automaticAuthor != _local)))) return false;
        _parentClaim = automatic ? _winningBaseClaim : 0;
        _parentAuthor = automatic ? _winningBaseAuthor : 0;
        _clock = Next(_clock); _claim = _clock; _localClaim = true;
        _phase = automatic ? SharedWindowMovePhase.Automatic : SharedWindowMovePhase.Held;
        _winner = _local; _winningClaim = _claim;
        _winnerAutomatic = automatic; _closedByAutomatic = false;
        _winningBaseClaim = automatic ? _parentClaim : _claim;
        _winningBaseAuthor = automatic ? _parentAuthor : _local;
        return true;
    }

    internal void Release()
    {
        // Revocation/forced drop has already cleared _localClaim. It cannot manufacture a
        // fresh release claim and take the window back from the accepted remote controller.
        if (_localClaim && _winner == _local && _phase == SharedWindowMovePhase.Held) _phase = SharedWindowMovePhase.Released;
    }

    // Cancellation publishes the existing claim's final pose without re-facing. A revoked
    // gesture has no local claim, so a loser can never regain authority through this route.
    internal void Abandon() => Release();

    internal bool Sample(out SharedWindowMoveEntry row)
    {
        row = default;
        if (!Ready || (!_localClaim && !_announces)) return false;
        // A claimless current publisher retains the accepted manual clock. Carry it in the
        // existing sequence without inventing a grab/release or changing the row grammar.
        if (!_localClaim && Newer(_clock, _sequence)) _sequence = _clock;
        _sequence = Next(_sequence);
        if (!_localClaim) See(_sequence);
        row = new SharedWindowMoveEntry { Kind = _kind, ContentKey = _key,
            OpeningAuthor = _openingAuthor, Opening = _opening, Claim = _localClaim ? _claim : 0,
            Phase = _localClaim ? _phase : SharedWindowMovePhase.Initial, Sequence = _sequence,
            ParentClaim = _localClaim ? _parentClaim : 0, ParentAuthor = _localClaim ? _parentAuthor : 0 };
        return row.Valid;
    }

    internal bool Accepts(int sender, in SharedWindowMoveEntry row) => Ready && row.Kind == _kind
        && row.ContentKey == _key && row.OpeningAuthor == _openingAuthor && row.Opening == _opening
        // A retired automatic operation leaves only the observed release's causal watermark.
        // Its old manual samples cannot become a live grip or rewind the settled pose again.
        && !_closedByAutomatic
        && (row.Phase == SharedWindowMovePhase.Initial ? _winner == 0 && sender == _initialPublisher
            : _winner == sender && _winningClaim == row.Claim);

    internal bool TryGet(int sender, out SharedWindowMoveEntry row)
    {
        if (_peers.TryGetValue(sender, out Receipt value)) { row = value.Row; return true; }
        row = default; return false;
    }
}
