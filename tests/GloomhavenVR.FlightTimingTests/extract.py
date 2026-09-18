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
card = source('RemoteBoardCard'); fx=source('RemoteCardFx'); board=source('RemoteControlBoard'); fan=source('RemoteHandFan'); active=source('RemoteActiveCards')
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
assert 'f.HandCardId = int.MinValue;' in tick and 'CompleteFlightLanding(NetCardFx.To(f.Endpoints), f.SourceActor, f.Generation, completedActiveCardId)'  in tick
assert '_owner.FlightOwnsRecess(i)' in method(board,'private void SeatSlots('), 'Arrival seat lacks flight ownership'
assert '_cards[i].ClearFlightTransfer();' in board, 'Focus change retains old departure fence'
assert 'out bool exhausted' in method(board,'internal void CompleteFlightLanding(')
assert 'exhausted ||' in method(board,'internal void CompleteFlightLanding(')
assert 'CompleteFlightSeat(completedHandCardId, f.SourceActor);' in tick
assert 'HandAbilityCards' in play and 'hand.Count != source.Value.Count' in play
assert 'TryFlightSeat(returning.CardInstanceID, out b,' in play
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
internal sealed class DummyActive { public int Refreshes; public void Refresh(CPlayerActor a,int completed=int.MinValue) { Refreshes++; } public void TransferToFlight(int id,CPlayerActor? a,long generation) {} public void PrepareFlightArrival(int id) {} }
internal sealed class DummyFan { public int Refreshes; public void RefreshFlightSeats() { Refreshes++; } }
internal sealed class BoardFixture {
private const int SlotCount=2;
private object _owner=new();
private DummyRoot _root=new();
public CardFixture[] _cards={new(),new()};
public DummyActive _active=new();
private readonly System.Collections.Generic.Dictionary<(int Actor,int Card),long> _activeFlightOwners=new();
private int _slotOccupiedMask;
private readonly System.Collections.Generic.Dictionary<int,long[]> _flightOwners=new();
public CAbilityCard? Landing;
public void SuppressBurnRecess(int slot) { if(slot>=0 && slot<2) _cards[slot].Blank(); }
public void SeatSlots(CPlayerActor actor,bool front,bool exhausted) { _cards[0].Set(Landing,front,actor); }
""" + method(board,'private bool ClaimActiveFlight(') + method(board,'internal void BeginActiveFlight(') + "internal bool OwnsActiveFlight(int cardId,CPlayerActor? actor,long generation) => actor != null && _activeFlightOwners.TryGetValue((actor.Id,cardId),out long epoch) && epoch == generation;" + method(board,'private bool ClaimFlightSlot(') + "internal bool OwnsFlightSlot(int slot,CPlayerActor? actor,long generation) => actor != null && _flightOwners.TryGetValue(actor.Id,out long[]? epochs) && epochs[slot] == generation;" + method(board,'internal int TransferCardToFlight(') + method(board,'internal void BeginFlightArrival(') + method(board,'internal void CompleteFlightLanding(') + """
}
internal sealed class OwnerFixture {
public BoardFixture Board=new(); public DummyFan HandFan=new();
public int TransferCardToFlight(CardFxAnchor from,int id,CPlayerActor? actor,long generation) => Board.TransferCardToFlight(from,id,actor,generation);
public void BeginFlightArrival(int slot,CPlayerActor? actor,long generation) => Board.BeginFlightArrival(slot,actor,generation);
public bool OwnsFlightSlot(int slot,CPlayerActor? actor,long generation) => Board.OwnsFlightSlot(slot,actor,generation);
public void BeginActiveFlight(int id,CPlayerActor? actor,long generation) => Board.BeginActiveFlight(id,actor,generation);
public bool OwnsActiveFlight(int id,CPlayerActor? actor,long generation) => Board.OwnsActiveFlight(id,actor,generation);
public void SuppressBurnRecess(int slot) => Board.SuppressBurnRecess(slot);
}
internal sealed class Flight { public long Generation=1; public bool Active=true,Drawable=true,SourceTransferred; public DummyRoot Go=new(); public CPlayerActor? SourceActor; public CAbilityCard? FaceCard; public int TransferredCardId=int.MinValue, ActiveCardId=int.MinValue; public byte Endpoints; }
internal sealed class FxFixture {
public OwnerFixture _owner=new(); private static bool CanDrawFlight(Flight f) => f.Drawable && f.Active;
public void Apply(Flight f) => TransferVisibleFlight(f);
""" + method(fx,'private void TransferVisibleFlight(') + """
}
"""
fixture=fixture.replace('public bool Visible, Vanishing;','public bool Visible, Vanishing; public bool FlightTransferred => _flightDepartedId != int.MinValue; private bool _materialiseSeeded; public bool Seeded => _materialiseSeeded;')
fixture=fixture.replace('public void ClearFlightTransfer() {', method(card,'internal void PrepareFlightArrival(')+'\npublic void ClearFlightTransfer() {')
compact = re.search(r'if \(compact && \(wire & \(1 << i\)\) != 0\) next\+\+;', board)
assert compact is not None, 'Suppressed wire-empty recess consumes another card'
fixture += 'internal static class CompactFixture { public static int Advance(bool compact,int wire,int i,int next) { ' + compact.group(0) + ' return next; } }'
capture = re.search(r'_flightHomePositions\[i\] = pos;\s*_flightHomeRotations\[i\] = rot;\s*_flightHomeScales\[i\] = Mathf.Max\(1e-4f, swapScale\);',fan)
assert capture is not None, 'Recovery home capture missing'
layout=method(fan,'private void LayoutCards(')
assert layout.index('_flightHomePositions[i] = pos;') < layout.index('float popT = PopAmount('), 'Recovery home includes hover pop'
assert layout.index('_flightHomePositions[i] = pos;') < layout.index('t.localPosition = pos;'), 'Recovery home follows the drawn reflow position'
assert 'f.Rotation = f.RecoveryHome ? recoveryRotation : SlabRotation;' in play, 'Recovery uses board rotation'
assert 'RemoteHandFan.ClosedFlightRotation(_owner, b)' in play, 'Closed recovery uses board rotation'
assert 'f.ToWidth = _cardWidth * recoveryHomeScale;' in play, 'Recovery ignores native home scale'
assert 'f.FromWidth *= scale / Mathf.Max(1e-5f, _owner.AppliedScale);' in play, 'Recovery pile width uses the wrong parent scale'
assert 'f.RecoveryHome ? _owner.AppliedScale : DrawnBoardScale' in tick, 'Recovery follows board rather than hand parent scale'
assert 'f.To = handSeat' not in tick and 'f.Rotation = recoveryRotation' not in tick
arrival_start = active.rfind('            if (', 0, active.index('_arrivalDeadlines[flying] = Time.unscaledTime;'))
arrival = active[arrival_start:active.index('            if (inTheAir)\n', arrival_start)]
assert 'PrepareFlightArrival();' in active and 'Refresh(actor, activeCardId)' in board
assert 'if (_seatedIds.Contains(flying) && !inTheAir)' in active, 'Previously seated active card ignores actual incoming flight'
fixture += """
internal struct Quaternion {
public System.Numerics.Quaternion Value;
public static Quaternion identity => new(){Value=System.Numerics.Quaternion.Identity};
public static Quaternion AroundZ(float radians) => new(){Value=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,radians)};
public static Quaternion operator*(Quaternion a,Quaternion b) => new(){Value=a.Value*b.Value};
}
internal static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
internal sealed class Transform {
public System.Numerics.Vector3 position;
public Quaternion rotation=Quaternion.identity;
public float scale=1;
public System.Numerics.Vector3 localPosition,localScale; public Quaternion localRotation=Quaternion.identity;
public System.Numerics.Vector3 TransformPoint(System.Numerics.Vector3 local) => position + System.Numerics.Vector3.Transform(local*scale,rotation.Value);
}
internal sealed class HomeFixture {
public DummyRoot _root=new();
public System.Collections.Generic.List<CAbilityCard> _handBuffer=new(){new(1),new(2)};
public System.Collections.Generic.List<DummyRoot> _cards=new(){new(),new()};
private readonly System.Numerics.Vector3[] _flightHomePositions=new System.Numerics.Vector3[2];
private readonly Quaternion[] _flightHomeRotations=new Quaternion[2];
private readonly float[] _flightHomeScales=new float[2];
public CPlayerActor? _shownActor;
private const float SlabScale=2f;
public float[] _pop=new float[2];
public void RefreshFlightSeats() {}
public void Capture(int i,System.Numerics.Vector3 pos,Quaternion rot,float swapScale) {
""" + capture.group(0) + """
}
""" + method(fan,'internal bool TryFlightSeat(') + method(fan,'internal void CompleteFlightSeat(').replace('Vector3.one','System.Numerics.Vector3.One') + """
}
internal static class Time { public static float unscaledTime; }
internal static class NetProtocol { public const float CardFxSeconds=0.4f; }
internal sealed class ArrivalOwner { public bool Flying; public bool IsCardFlyingToActive(int id)=>Flying; }
internal sealed class ActiveArrivalFixture {
public System.Collections.Generic.Dictionary<int,float> _arrivalDeadlines=new();
public System.Collections.Generic.HashSet<int> _flyingSeats=new(); public ArrivalOwner _owner=new();
public bool Hold(int flying,int completedFlightCardId) { int i=0; _flyingSeats.Clear(); bool inTheAir=_owner.IsCardFlyingToActive(flying);
""" + arrival + "return _flyingSeats.Contains(0); } }"
fixture=fixture.replace('using System;','using System;\nusing Vector3=System.Numerics.Vector3;',1)
fixture=fixture.replace('internal sealed class DummyRoot { public bool activeSelf=true; }','internal sealed class DummyRoot { public bool activeSelf=true; public Transform transform=new(); }')
out.write_text(fixture)
print('Flight timing production integration: all checks passed.')

# Run the original local transition with presentation writes represented by spies. The
# binding checks keep both entry paths on that transition before they claim the tween.
local = re.sub(r'//[^\n]*|/\*.*?\*/', '', (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text(), flags=re.S)
for signature in ['internal void FlyToPile(', 'internal void FlyFromPile(']:
    entry = method(local, signature)
    assert 'PrepareFlightVisual();' in entry and entry.index('PrepareFlightVisual();') < entry.index('_flying = true;'), 'Local flight must restore its visible surface before taking ownership'
    assert 'if (IsHeld)' in entry and entry.index('if (IsHeld)') < entry.index('PrepareFlightVisual();'), 'Held cards must retain their visual ownership'
fixture += """
internal sealed class LocalFlightFixture {
public bool _appearing, _vanishing;
public Action? _vanishDone;
public float Alpha;
public bool BodyVisible;
private void SetVisualAlpha(float alpha) => Alpha=alpha;
private void SetBodyVisible(bool visible) => BodyVisible=visible;
private void NoteFlightFadeHandover() {}
public void Prepare() => PrepareFlightVisual();
""" + method(local, 'private void PrepareFlightVisual()') + "\n}\n"
out.write_text(fixture)
