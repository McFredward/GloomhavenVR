import pathlib,re,sys
root=pathlib.Path(sys.argv[1]); out=pathlib.Path(sys.argv[2])
def source(name):
    return re.sub(r'//[^\n]*|/\*.*?\*/','',(root/'src/GloomhavenVR/Net'/name).read_text(),flags=re.S)
def method(s,name):
    start=s.index(name); b=s.index('{',start); end=b+1; n=1
    while n:
        n+=(s[end]=='{')-(s[end]=='}'); end+=1
    return s[start:end]
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
internal sealed class Character { internal List<CAbilityCard> LostAbilityCards=new(), PermanentlyLostAbilityCards=new(); }
internal sealed class CPlayerActor { internal int Id; internal Character CharacterClass=new(); internal CPlayerActor(int id) { Id=id; } }
internal sealed class AbilityCardUI { internal CAbilityCard? AbilityCard; }
internal static class RemoteBoardFocus { internal static Dictionary<int,CPlayerActor> Actors=new(); internal static CPlayerActor? ActorById(int id)=>Actors.GetValueOrDefault(id); }
internal sealed class Burn { internal bool Active=true, HandoverLogged; internal int ActorId; internal AbilityCardUI? Widget; }
internal sealed class BurnFixture {
internal List<Burn> _burns=new();
'''+method(burn,'internal CPlayerActor? PresentationActor')+'''
}
internal sealed class Panel { internal int Holds; internal void HoldBurnPresentation(CPlayerActor? a) { Holds++; } }
internal sealed class Owner { internal bool HoldsBurnCardLayout; internal bool BurnOwnsRecess(int i)=>false; }
internal sealed class ActiveFixture {
internal Owner _owner=new(); internal List<Panel> _cards=new(){new(),new()}; internal int Reflows;
'''+gate(active,'public void Refresh(','bool showFronts =')+'''Reflows++; }
}
internal sealed class BoardFixture {
internal Owner _owner=new(); internal CPlayerActor? _latchedActor; internal Panel[] _cards={new(),new()}; const int SlotCount=2; internal int Replacements;
void SuppressBurnRecess(int i) {}
'''+gate(board,'private void SeatSlots(','int wire =').replace('private void SeatSlots','internal void SeatSlots')+'''Replacements++; }
}
internal sealed class CardAppearanceState { internal int SourceActorId; internal CAbilityCard? Original; }
internal sealed class CardAppearanceSnapshot { internal float SampleTime; internal CardAppearanceSnapshot(float time){SampleTime=time;} }
internal static class CardAppearanceProvenance { internal static CAbilityCard? Resolve(CardAppearanceState s)=>s.Original; }
internal static class MirrorFixture {
internal sealed class Frame { internal CardAppearanceSnapshot? Current, Previous; }
internal static Dictionary<int,Frame> Frames=new();
internal static bool Available=true; internal static float Progress; internal static CardAppearanceState? From,To;
private static bool TryGet(int p,CPlayerActor a,CAbilityCard c,out CardAppearanceState? previous,out CardAppearanceState? current,out float progress) {previous=From;current=To;progress=Progress;return Available;}
'''+method(mirror,'internal static bool HasPresentedThrough(')+''' }
'''
out.write_text(fixture)
