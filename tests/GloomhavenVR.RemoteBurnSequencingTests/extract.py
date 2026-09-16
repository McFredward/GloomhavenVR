import pathlib,re,sys
root=pathlib.Path(sys.argv[1]); out=pathlib.Path(sys.argv[2])
def source(name):
    return re.sub(r'//[^\n]*|/\*.*?\*/','',(root/'src/GloomhavenVR/Net'/name).read_text(),flags=re.S)
def method(s,name):
    start=s.index(name); b=s.index('{',start); end=b+1; n=1
    while n:
        n+=(s[end]=='{')-(s[end]=='}'); end+=1
    return s[start:end]
def expression(s,name):
    start=s.index(name); return s[start:s.index(";",start)+1]
def gate(s,name,after):
    m=method(s,name); return m[:m.index(after)]
burn=source('Remote/RemoteBurnFx.cs'); avatar=source('Remote/RemoteAvatar.cs'); active=source('Remote/RemoteActiveCards.cs'); board=source('Remote/RemoteControlBoard.cs'); fan=source('Remote/RemoteHandFan.cs'); browse=source('Remote/RemoteBrowserFan.cs'); mirror=source('CardAppearanceMirror.cs')
assert avatar.index('_burnFx.PreparePresentation();') < avatar.index('_handFan.Tick(dt);') < avatar.index('_controlBoard.Tick(dt);'), 'Burn discovery must precede every remote card layout'
assert 'Handover(' not in method(burn,'private bool TryApplyRelease('), 'Receiving release must not bypass native playback'
drive=method(burn,'private void Drive(')
assert 'bool release = b.OwnerReleased && (b.CompletionTime < 0f' in drive and 'CardAppearanceMirror.HasPresentedThrough(b.PresentationPlayer, actor, card, b.CompletionTime)' in drive, 'Burn flight must wait for exact native completion'
assert drive.index('_owner.SuppressBurnActiveCard(b.CardId)') < drive.index('b.Go.SetActive(showSlab)'), 'Active burn must remove original renderer before showing its slab'
assert method(fan,'public void Tick(').index('_owner.HoldsBurnCardLayout') < method(fan,'public void Tick(').index('BeginSwap('), 'Burn hold must precede fan exchange'
assert method(browse,'public void Tick(').index('_owner.HoldsBurnCardLayout') < method(browse,'public void Tick(').index('BeginEmerge('), 'Burn hold must precede pile fan rebuild'
assert 'owner.BurnPresentationActor' in source('Remote/RemoteBoardFocus.cs'), 'Early focus packet must retain the burning actor'
fx=source('Remote/RemoteCardFx.cs')
assert '_owner.HoldsBurnCardLayout' in method(fx,'private bool TryPlay('), 'Following flights must wait for the earlier native burn'
assert method(fx,'public void Tick(').index('new PendingFlight(pending.Endpoints') < method(fx,'public void Tick(').index('Time.unscaledTime - pending.ReceivedAt'), 'Burn wait must not spend queued flight resolution lifetime'
fixture='''using System;
using System.Collections.Generic;
internal sealed class CAbilityCard { internal int CardInstanceID; internal CAbilityCard(int id) { CardInstanceID=id; } }
internal sealed class Character { internal List<CAbilityCard> LostAbilityCards=new(), PermanentlyLostAbilityCards=new(), HandAbilityCards=new(), RoundAbilityCards=new(), ActivatedCards=new(); }
internal sealed class CPlayerActor { internal int Id; internal Character CharacterClass=new(); internal CPlayerActor(int id) { Id=id; } }
internal sealed class AbilityCardUI { internal CAbilityCard? AbilityCard; }
internal static class RemoteBoardFocus { internal static CPlayerActor? Requested; internal static CPlayerActor? DisplayedActor(Owner owner,out bool focus,out bool exhausted,bool ignoreBurnHold=false) {focus=exhausted=false;return Requested;} internal static Dictionary<int,CPlayerActor> Actors=new(); internal static CPlayerActor? ActorById(int id)=>Actors.GetValueOrDefault(id); }
internal sealed class Burn { internal bool Active=true, HandoverLogged; internal int ActorId; internal AbilityCardUI? Widget; internal CAbilityCard? OriginalCard; }
internal sealed class BurnFixture {
internal List<Burn> _burns=new();
internal sealed class PendingRelease { internal float CompletionTime=-1; internal bool FallbackPlayed; internal int ActorId,PresentationPlayer; internal CAbilityCard? OriginalCard; }
internal List<PendingRelease> _pendingReleases=new(); internal int _watchActor; internal Owner _owner=new();
'''+method(burn,'internal bool IncomingBurnPending').replace('CardAppearanceMirror.','MirrorFixture.')+method(burn,'internal bool HasObservedBurn(')+method(burn,'internal CPlayerActor? PresentationActor')+method(burn,'private bool PendingRecovered(').replace('CardAppearanceMirror.','MirrorFixture.')+'''
}
internal sealed class Panel { internal int Holds; internal void HoldBurnPresentation(CPlayerActor? a, bool alreadyPublic=false) { Holds++; } }
internal sealed class Owner { internal int PlayerId=7; internal bool HoldsBurnCardLayout; internal bool BurnOwnsRecess(int i)=>false; }
internal sealed class ActiveFixture {
internal Owner _owner=new(); internal List<Panel> _cards=new(){new(),new()}; internal int Reflows, Pulses; private void DrivePulse(CPlayerActor actor) {Pulses++;}
'''+gate(active,'public void Refresh(','bool showFronts =')+'''Reflows++; }
}
internal sealed class BoardFixture {
internal Owner _owner=new(); internal CPlayerActor? _latchedActor; internal Panel[] _cards={new(),new()}; const int SlotCount=2; internal int Replacements;
void SuppressBurnRecess(int i) {}
'''+gate(board,'private void SeatSlots(','int wire =').replace('private void SeatSlots','internal void SeatSlots')+'''Replacements++; }
}
internal static class NetFigures {internal static int StableActorId(CPlayerActor a)=>a.Id;}
internal static class NetProtocol {internal const byte HeldFaceListHand=1,HeldFaceListActive=2; internal static byte HeldFaceList(byte code)=>code;}
internal static class CardPlumeState {internal const byte RoundList=3;}
internal sealed class CardAppearanceState { internal int ActorId; internal byte FaceCode; internal int SourceActorId; internal CAbilityCard? Original; }
internal sealed class CardAppearanceSnapshot { internal CardAppearanceState[] States=Array.Empty<CardAppearanceState>(); internal float SampleTime; internal CardAppearanceSnapshot(float time){SampleTime=time;} }
internal static class CardAppearanceProvenance {
internal static Dictionary<(int,ushort,ushort),CAbilityCard> Originals=new();
internal static CAbilityCard? Resolve(int actor,ushort seat,ushort count)=>Originals.GetValueOrDefault((actor,seat,count));
internal static CAbilityCard? Resolve(CardAppearanceState s)=>s.Original;
}
internal static class MirrorFixture {
internal sealed class Frame { internal CardAppearanceSnapshot? Current, Previous; internal Dictionary<CAbilityCard,(int Actor,float Time,CardAppearanceState Source)> Presented=new(); }
private static bool Recovered(CPlayerActor actor,CAbilityCard card)=>actor.CharacterClass.HandAbilityCards.Contains(card)||actor.CharacterClass.RoundAbilityCards.Contains(card);
internal static Dictionary<int,Frame> Frames=new();
internal static bool Available=true; internal static float Progress; internal static CardAppearanceState? From,To;
private static bool TryGet(int p,CPlayerActor a,CAbilityCard c,out CardAppearanceState? previous,out CardAppearanceState? current,out float progress) {previous=From;current=To;progress=Progress;return Available;}
'''+method(mirror,'internal static bool HasPresentedThrough(')+method(mirror,'internal static bool HasRecoveredSourceAfter(')+expression(mirror,'internal static bool OwnerBurnInProgress(')+''' }
'''
fixture += """
namespace ScenarioRuleLibrary { internal static class ScenarioManager {internal static object? Scenario=new();} }
internal readonly record struct CardFlightSource(int ActorId,byte Seat,byte Count);
internal readonly record struct CardBurnCompletion(byte Sequence,int ActorId,int SourceActorId,ushort PoolSeat,ushort PoolCount,float Time,bool InProgress=false) {
internal (int,int,ushort,ushort) Key=>(ActorId,SourceActorId,PoolSeat,PoolCount);
internal byte Endpoints=>0x41; internal byte Flags=>0; internal byte FlightFlags=>0;
internal CardFlightSource Source=>new(ActorId,0,0);
}
internal sealed class CardBurnCompletionHistory {internal const int CountMax=128; internal CardBurnCompletion[] Entries=Array.Empty<CardBurnCompletion>();}
internal static class CardFlightVisibility {internal static void ObserveOwnerRelease(int actor,byte endpoints,byte flags,CardFlightSource source,float time,CAbilityCard card) {}}
internal static class NetAvatarDriver {
internal static bool OwnershipReady=true; internal static RemoteAvatar? Controller;
internal static bool TryGetCharacterDecisionOwner(CPlayerActor actor,out RemoteAvatar? controller) {controller=Controller;return OwnershipReady;}
internal static void MirrorCharacterCardFlight(RemoteAvatar sender,byte endpoints,byte flags,CardFlightSource source,float time,CAbilityCard card) {}
}
internal sealed class RemoteAvatar {
internal int PlayerId=7; internal BurnFixture _burnFx=new(); internal int Dispatched;
private Dictionary<(int Actor,int Source,ushort Seat,ushort Count),float> _burnCompletionTimes=new();
private object? _burnCompletionScenario; private bool _burnCompletionsInitialized;
private HashSet<int> _burnProgressActors=new();
private HashSet<(int Actor,int Source,ushort Seat,ushort Count)> _burnProgressKeys=new();
internal bool HasBurnInProgress(int actorId)=>_burnProgressActors.Contains(actorId);
private void PlayMirroredCardFlight(byte endpoints,byte flags,CardFlightSource source,float time,int player,CAbilityCard card) {Dispatched++;}
""" + method(avatar,'private void ApplyBurnCompletions(').replace('private void ApplyBurnCompletions','internal void ApplyBurnCompletions') + "\n}\n"
out.write_text(fixture)
