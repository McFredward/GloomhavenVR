// Native lists, Unity flight interpolation, pile renderers and networking are explicit ports.
// The production confirmation, retirement, destination classifier and launch methods execute.
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net
{
    internal struct RigPose { public Vector3 Position; public Quaternion Rotation; }
    internal sealed class CardAppearanceState { internal int ActorId,SourceActorId; internal ushort PoolSeat,PoolCount; }
    internal static class CardAppearanceProvenance
    {
        internal static bool Capture(CardAppearanceState state,CPlayerActor actor,CAbilityCard original)
        {state.SourceActorId=actor.Id;state.PoolSeat=7;state.PoolCount=20;return true;}
    }
    internal sealed record CardDismissFlightOrigin(int SourceActorId,ushort PoolSeat,ushort PoolCount,RigPose BerthPose,float Width);
    internal readonly record struct CardFlightSource(int ActorId,byte Seat,byte Count);
    internal enum CardFxAnchor {Board,Slot0,Slot1,Discard,Burnt,HandFan,Active}
    internal static class RemoteBoardFocus { internal static CPlayerActor? ActorById(int id)=>id!=0?new(){Id=id}:null; }
    internal static class CardFlightVisibility {internal static byte ConsumeBurn(CAbilityCard? card)=>0;}
    internal static class CardAppearanceSampler {internal static float RetainBurnCompletion(AbilityCardUI widget,object? full)=>5f;}
    internal static class NetCardFx
    {
        internal sealed record Sent(CardFxAnchor From,CardFxAnchor To,CardFlightSource? Source,float Completion,CardDismissFlightOrigin? Dismiss);
        internal static readonly List<Sent> Events=new();
        internal static void Report(CardFxAnchor from,CardFxAnchor to,byte flags,CardFlightSource? source,float completionTime,CardDismissFlightOrigin? dismissOrigin)
            =>Events.Add(new(from,to,source,completionTime,dismissOrigin));
    }
}
namespace GloomhavenVR.Cards.Board
{
    internal static class CharacterFocus {internal static bool ReadOnlyView=>false;}
}
namespace GloomhavenVR.Cards
{
    internal enum PileKind {Discard,Burnt}
    internal static class CardFlightLedger {internal static void Note(string a,string b,string c,string d) {}}
    internal sealed partial class CardsDriver
    {
        private readonly HashSet<VRCard> _lastHalfCards=new(),_lastFieldCards=new(),_lastActiveCards=new(),_flyingToPile=new();
        private readonly Dictionary<AbilityCardUI,Net.CardFlightSource> _activeExitOrigins=new(),_activeCellSources=new();
        private readonly Dictionary<AbilityCardUI,Vector3> _lastCardWorldPos=new();
        private readonly Dictionary<AbilityCardUI,Quaternion> _lastCardWorldRot=new();
        private readonly Dictionary<AbilityCardUI,float> _lastCardWorldWidth=new();
        private readonly Dictionary<AbilityCardUI,int> _loggedFlightRefusal=new();
        private readonly Dictionary<AbilityCardUI,bool> _burnHoldSince=new();
        private readonly PilePort _piles=new();
        private readonly FactoryPort _factory=new();
        private const float FlyToPileSeconds=.4f;
        internal bool BurnReleased=true;
        private readonly HashSet<AbilityCardUI> Completed=new();
        private bool IsCompletedBurn(AbilityCardUI widget)=>Completed.Contains(widget);
        private void CompleteBurnClaim(AbilityCardUI widget)=>Completed.Add(widget);
        private void ClearBurnHold(AbilityCardUI widget) { }
        private bool TryTakeBurnFlightSlot(AbilityCardUI widget,VRCard card)
        {if(!BurnReleased)_burnHoldSince[widget]=true;else _burnHoldSince.Remove(widget);return BurnReleased;}
        private Vector3 BoardUp()=>Vector3.up;
        private float BoardArcMin()=>.1f;
        private static Net.CardFxAnchor PileAnchor(PileKind pile)=>pile==PileKind.Burnt?Net.CardFxAnchor.Burnt:Net.CardFxAnchor.Discard;
        private Net.CardFxAnchor BurnOrPileOrigin(AbilityCardUI? widget,int seat)=>widget!=null&&_activeExitOrigins.ContainsKey(widget)?Net.CardFxAnchor.Active:Net.CardFxAnchor.Board;
        private static string ModelListName(RoundCardExit value)=>value.ToString();
        private void LogFlightRefused(VRCard card,RoundCardExit exit,CPlayerActor? owner,bool fromPickField=false) { }
        private sealed class PilePort
        {
            internal bool TryGetPileWorld(PileKind kind,out Vector3 position,out float width)
            {position=kind==PileKind.Discard?new(2,0,0):new(3,0,0);width=.05f;return true;}
        }
        private sealed class FactoryPort {internal int Parks;internal readonly List<VRCard> All=new();internal void Park(VRCard card){Parks++;card.IsFlying=false;card.gameObject.SetActive(false);}}
        internal int Parks=>_factory.Parks;
        internal void ContinueFlight(VRCard card)=>TryStartFlyToPile(Presented!,card);
        // Only the large layout downstream is substituted. The ordering is source-asserted in
        // run.py and the actual departure launcher/classifier execute on either side of it.
        internal void RebuildActiveRemoval()
        {
            _lastActiveCards.Clear();
            foreach(VRCard card in _active.Cards) _lastActiveCards.Add(card);
            AdvanceCommittedActiveDismiss(Presented);
            _active.Cards.RemoveAll(card=>!ActiveCardSet.IsActive(card.GameCard.PlayerActor!,card.GameCard.AbilityCard));
            _active.Grid.gameObject.SetActive(_active.Cards.Count>0);
            NativeHandoffZoneSweep();
        }
    }
}
