import pathlib, re, sys
root = pathlib.Path(sys.argv[1]); out = pathlib.Path(sys.argv[2])
def source(name):
    text = (root / ('src/GloomhavenVR/Net/Remote/' + name + '.cs')).read_text()
    return re.sub(r'//[^\n]*|/\*.*?\*/', '', text, flags=re.S)
def method(text, signature):
    start = text.index(signature); brace = text.index('{', start); depth = 1; end = brace+1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}'); end += 1
    return text[start:end]
card = source('RemoteBoardCard'); fx=source('RemoteCardFx'); board=source('RemoteControlBoard')
transfer=method(card,'internal int TransferToFlight(')
set_prefix=method(card,'public void Set(').split('if (empty == _shownEmpty')[0]
anon_prefix=method(card,'public void SetAnonymousBack(').split('if (!_shownEmpty')[0]
# These boundaries must precede all native presentation, including cached Set early-outs.
assert 'BeginVanish' not in set_prefix
assert '_flightDepartedId' in set_prefix
assert 'Blank();' in transfer
# Production orchestration, with comments removed: no replica can satisfy these checks.
play=method(fx,'private bool TryPlay('); tick=method(fx,'public void Tick(')
assert 'TransferVisibleFlight(f);' in play and play.index('TransferVisibleFlight(f);') < play.index('f.Go.SetActive(CanDrawFlight(f));'), 'Flight draw precedes source transfer'
assert tick.index('TransferVisibleFlight(f);') < tick.index('f.Go.SetActive(drawable);'), 'Delayed artwork draw precedes source transfer'
assert 'f.HandCardId = int.MinValue;' in tick and 'CompleteFlightLanding(NetCardFx.To(f.Endpoints), f.SourceActor, f.Generation)'  in tick
assert '_owner.FlightOwnsRecess(i)' in method(board,'private void SeatSlots('), 'Arrival seat lacks flight ownership'
assert '_cards[i].ClearFlightTransfer();' in board, 'Focus change retains old departure fence'
assert 'out bool exhausted' in method(board,'internal void CompleteFlightLanding(')
assert 'exhausted ||' in method(board,'internal void CompleteFlightLanding(')
assert 'RefreshFlightSeats();' in tick
assert 'HandAbilityCards' in play and 'hand.Count != source.Value.Count' in play
assert 'TryFlightSeat(returning.CardInstanceID, out b)' in play
assert 'f.To = handSeat' not in tick, 'Recovery destination must remain captured'
assert 'return arrivalRule ?? arrivalRefusal' in method(fx,'private string ResolveFace('), 'Covered arrival falls through into unrelated departed identity'
# Extract source handover policies verbatim. Only Unity/native renderer calls are stubbed.
fixture='''using System;
internal sealed class CAbilityCard { public int CardInstanceID; public CAbilityCard(int id) { CardInstanceID=id; } }
internal sealed class CPlayerActor { public int Id; public CPlayerActor(int id) { Id=id; } }
internal sealed class CardFixture {
private const int AnonymousCardId=int.MinValue+1;
private int _shownId=int.MinValue, _flightDepartedId=int.MinValue, _flightDepartedOwner=int.MinValue;
private bool _shownEmpty=true;
private long _flightGeneration;
private readonly System.Collections.Generic.Dictionary<int,long> _retiredFlightGenerations=new();
public bool Visible, Vanishing;
private static int OwnerKey(CPlayerActor? owner) => owner?.Id ?? 0;
public void Blank() { Visible=false; Vanishing=false; _shownEmpty=true; _shownId=int.MinValue; }
'''+transfer+'\n'+set_prefix+'''
if (empty) { if (Visible) Vanishing=true; _shownEmpty=true; _shownId=int.MinValue; return; }
_shownEmpty=false; _shownId=id; Visible=true; Vanishing=false;
}
public void SetAnonymousBack() {
if (_flightDepartedId != int.MinValue) { Blank(); return; }
_shownEmpty=false; _shownId=AnonymousCardId; Visible=true;
}
public void ClearFlightTransfer() { _flightDepartedId=_flightDepartedOwner=int.MinValue; }
}
'''
# Use the actual anonymous guard rather than its test host's implementation.
actual_anon=method(card,'public void SetAnonymousBack(')
guard=actual_anon[actual_anon.index('{')+1:actual_anon.index('if (!_shownEmpty')]
fixture=fixture.replace('if (_flightDepartedId != int.MinValue) { Blank(); return; }',guard.strip())
fixture += """
internal enum CardFxAnchor { Board, Slot0, Slot1, Active, HandFan, Discard }
internal static class NetCardFx {
public static CardFxAnchor From(byte e) => (CardFxAnchor)(e & 15);
public static CardFxAnchor To(byte e) => (CardFxAnchor)(e >> 4);
}
internal sealed class DummyRoot { public bool activeSelf=true; }
internal static class RemoteBoardFocus {
public static CPlayerActor? Current;
public static CPlayerActor? DisplayedActor(object owner, out bool focus) { focus=false; return Current; }
public static CPlayerActor? DisplayedActor(object owner, out bool focus, out bool exhausted) { focus=false; exhausted=Exhausted; return Current; }
public static bool Exhausted;
}
internal static class NetFigures { public static int StableActorId(CPlayerActor actor) => actor.Id; }
internal static class RevealGate { public static bool ShowPeerCardFronts(CPlayerActor a) => true; }
internal sealed class DummyActive { public int Refreshes; public void Refresh(CPlayerActor a) { Refreshes++; } public void TransferToFlight(int id,CPlayerActor? a,long generation) {} }
internal sealed class DummyFan { public int Refreshes; public void RefreshFlightSeats() { Refreshes++; } }
internal sealed class BoardFixture {
private const int SlotCount=2;
private object _owner=new();
private DummyRoot _root=new();
public CardFixture[] _cards={new(),new()};
public DummyActive _active=new();
private int _slotOccupiedMask;
private readonly System.Collections.Generic.Dictionary<int,long[]> _flightOwners=new();
public CAbilityCard? Landing;
public void SuppressBurnRecess(int slot) { if(slot>=0 && slot<2) _cards[slot].Blank(); }
public void SeatSlots(CPlayerActor actor,bool front,bool exhausted) { _cards[0].Set(Landing,front,actor); }
""" + method(board,'private bool ClaimFlightSlot(') + "internal bool OwnsFlightSlot(int slot,CPlayerActor? actor,long generation) => actor != null && _flightOwners.TryGetValue(actor.Id,out long[]? epochs) && epochs[slot] == generation;" + method(board,'internal int TransferCardToFlight(') + method(board,'internal void BeginFlightArrival(') + method(board,'internal void CompleteFlightLanding(') + """
}
internal sealed class OwnerFixture {
public BoardFixture Board=new(); public DummyFan HandFan=new();
public int TransferCardToFlight(CardFxAnchor from,int id,CPlayerActor? actor,long generation) => Board.TransferCardToFlight(from,id,actor,generation);
public void BeginFlightArrival(int slot,CPlayerActor? actor,long generation) => Board.BeginFlightArrival(slot,actor,generation);
public bool OwnsFlightSlot(int slot,CPlayerActor? actor,long generation) => Board.OwnsFlightSlot(slot,actor,generation);
public void SuppressBurnRecess(int slot) => Board.SuppressBurnRecess(slot);
}
internal sealed class Flight { public long Generation=1; public bool Active=true,Drawable=true,SourceTransferred; public DummyRoot Go=new(); public CPlayerActor? SourceActor; public CAbilityCard? FaceCard; public int TransferredCardId=int.MinValue; public byte Endpoints; }
internal sealed class FxFixture {
public OwnerFixture _owner=new(); private static bool CanDrawFlight(Flight f) => f.Drawable;
public void Apply(Flight f) => TransferVisibleFlight(f);
""" + method(fx,'private void TransferVisibleFlight(') + """
}
"""
fixture=fixture.replace('public bool Visible, Vanishing;','public bool Visible, Vanishing; public bool FlightTransferred => _flightDepartedId != int.MinValue; private bool _materialiseSeeded; public bool Seeded => _materialiseSeeded;')
fixture=fixture.replace('public void ClearFlightTransfer() {', method(card,'internal void PrepareFlightArrival(')+'\npublic void ClearFlightTransfer() {')
compact = re.search(r'if \(compact && \(wire & \(1 << i\)\) != 0\) next\+\+;', board)
assert compact is not None, 'Suppressed wire-empty recess consumes another card'
fixture += 'internal static class CompactFixture { public static int Advance(bool compact,int wire,int i,int next) { ' + compact.group(0) + ' return next; } }'
out.write_text(fixture)
print('Flight timing production integration: all checks passed.')
