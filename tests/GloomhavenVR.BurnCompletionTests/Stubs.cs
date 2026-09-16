using System;
using System.Collections.Generic;
namespace UnityEngine { internal static class Time { internal static float unscaledTime; } }
namespace ScenarioRuleLibrary
{
    internal sealed class CAbilityCard { }
    internal sealed class CPlayerActor
    {
        internal int Id;
        internal bool Controlled=true;
        internal readonly Character CharacterClass = new();
    }
    internal sealed class Character
    {
        internal readonly List<CAbilityCard> LostAbilityCards = new(), PermanentlyLostAbilityCards = new(),
            HandAbilityCards = new(), RoundAbilityCards = new(), Pool = new();
    }
}
internal sealed class CardEffects {
internal float Value; internal bool Running; internal FullAbilityCard? Full;
internal T? GetComponent<T>() where T:class => Full as T;
internal T? GetComponentInParent<T>() where T:class => Full as T;
}
internal sealed class FullAbilityCard { internal CardEffects cardEffects = new(); internal ScenarioRuleLibrary.CPlayerActor? playerActor; internal ScenarioRuleLibrary.CAbilityCard? AbilityCard; }
internal sealed class AbilityCardUI
{
    internal FullAbilityCard fullAbilityCard = new();
    internal ScenarioRuleLibrary.CPlayerActor PlayerActor = null!;
    internal ScenarioRuleLibrary.CAbilityCard AbilityCard = null!;
}
internal sealed class CardsHandUI { internal readonly List<AbilityCardUI> Cards = new(); }
internal sealed class CardsHandManager
{
    internal static CardsHandManager Instance = new();
    internal readonly Dictionary<ScenarioRuleLibrary.CPlayerActor, CardsHandUI> Hands = new();
    internal CardsHandUI? GetHand(ScenarioRuleLibrary.CPlayerActor? actor) => actor == null ? null : Hands.GetValueOrDefault(actor);
}
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string category, string message) { } } }
namespace GloomhavenVR.Cards
{
    internal static class CardsDriver { internal static bool ExpectFlight; internal static bool ExpectsBurnFlight(ScenarioRuleLibrary.CAbilityCard card)=>ExpectFlight; }
    internal static class BurnArtwork {
internal static CardEffects? EffectsOf(FullAbilityCard? full)=>full?.cardEffects;
internal static CardEffects? EffectsOf(AbilityCardUI? widget)=>widget?.fullAbilityCard.cardEffects;
internal static bool Playing(CardEffects? effects)=>effects?.Running==true;
internal static bool LosingCards(AbilityCardUI? widget)=>false;
}
    internal static class CardsGameApi
    {
        internal static bool ControlsActor(ScenarioRuleLibrary.CPlayerActor actor)=>actor.Controlled;
        internal static void GetPileArcWidgets(CardsHandUI hand, bool burnt, List<AbilityCardUI> output)
        { output.Clear(); output.AddRange(hand.Cards); }
    }
}
namespace GloomhavenVR.Net
{
    internal static class CardBurnCompletionHistory { internal const int CountMax=128; }
    internal static class NetCardFx { internal static void NoteBurnCompletionCapture(CardAppearanceState state,float clock) {} internal static void ForgetBurnCompletion(CardAppearanceState state) { Progress.Remove((state.ActorId,state.PoolSeat)); NoFlight.Remove((state.ActorId,state.PoolSeat)); }
internal static readonly HashSet<(int,ushort)> Progress=new(), NoFlight=new();
internal static bool CompleteBurnWithoutFlight(CardAppearanceState state,float time) { if(!Progress.Remove((state.ActorId,state.PoolSeat)))return false;NoFlight.Add((state.ActorId,state.PoolSeat));return true; }
internal static void NoteBurnProgress(CardAppearanceState state,float time,bool running) {if(running){Progress.Add((state.ActorId,state.PoolSeat));NoFlight.Remove((state.ActorId,state.PoolSeat));}else Progress.Remove((state.ActorId,state.PoolSeat));}
}
    internal static class NetAvatarDriver {internal static bool CanPublishNativePresentation=true;}
    internal static class NetFigures { internal static int StableActorId(ScenarioRuleLibrary.CPlayerActor actor) => actor.Id; }
    internal sealed class CardAppearanceState
    {
        internal const int CountMax = 32;
        internal int ActorId, SourceActorId;
        internal ushort PoolSeat, PoolCount;
        internal byte FaceCode, ListCount;
        internal float[] Nodes = Array.Empty<float>(), ExtraGroups = Array.Empty<float>();
        internal bool Validate() => SourceActorId != 0 && ListCount != 0;
        internal CardAppearanceState Copy()
        {
            var copy = (CardAppearanceState)MemberwiseClone();
            copy.Nodes = (float[])Nodes.Clone(); copy.ExtraGroups = (float[])ExtraGroups.Clone(); return copy;
        }
    }
    internal sealed class CardAppearanceCapture
    {
        internal readonly CardAppearanceState Candidate = new();
        internal readonly CaptureBindings Bindings;
        internal CardAppearanceCapture(CardEffects effects) { Bindings = new(effects); }
    }
    internal sealed class CaptureBindings(CardEffects effects)
    {
        internal float[] Capture() => new[] { effects.Value };
        internal float[] CaptureExtraGroups() => Array.Empty<float>();
    }
    internal static class CardAppearanceAddress
    {
        internal static bool TryPile(List<AbilityCardUI> pile, ScenarioRuleLibrary.CAbilityCard card,
            Func<AbilityCardUI, ScenarioRuleLibrary.CAbilityCard?> identity, bool burnt, out byte code, out byte count)
        {
            count = (byte)pile.Count; int index = pile.FindIndex(widget => ReferenceEquals(identity(widget), card));
            code = (byte)(index + 1); return index >= 0;
        }
    }
    internal static class CardAppearanceProvenance
    {
        internal static readonly Dictionary<int, ScenarioRuleLibrary.CPlayerActor> Actors = new();
        internal static bool Capture(CardAppearanceState state, ScenarioRuleLibrary.CPlayerActor actor, ScenarioRuleLibrary.CAbilityCard card)
        {
            state.SourceActorId = actor.Id; state.PoolSeat = (ushort)actor.CharacterClass.Pool.IndexOf(card);
            state.PoolCount = (ushort)actor.CharacterClass.Pool.Count; return state.PoolSeat < state.PoolCount;
        }
        internal static ScenarioRuleLibrary.CAbilityCard? Resolve(CardAppearanceState state)
        {
            if (!Actors.TryGetValue(state.SourceActorId, out var actor)) return null;
            var pool = actor.CharacterClass.Pool;
            return pool.Count == state.PoolCount && state.PoolSeat < pool.Count ? pool[state.PoolSeat] : null;
        }
    }
    internal static partial class CardAppearanceSampler
    {
        private static readonly List<AbilityCardUI> Pile = new();
        internal static List<CardAppearanceState> Retained(params CardAppearanceState[] live)
        { var states = new List<CardAppearanceState>(live); AppendBurnFinals(states); return states; }
        internal static void AdvanceProgressForTest()=>RefreshBurnProgress();
        internal static void ResetForTest() { ClearBurnProgress(); ClearBurnFinals(); _finalCapacityLogged = false; }
    }
}
