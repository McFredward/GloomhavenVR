using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.Adventure;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    { internal HarmonyPatch(Type _, string __) { } }
    internal static class AccessTools
    {
        internal static FieldInfo? Field(Type type, string name) => type.GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
namespace Assets.Script.Misc
{
    internal interface ICallbackPromise { void Then(Action onDone); }
    internal sealed class CallbackPromise : ICallbackPromise
    {
        private Action? _done;
        private bool _resolved;
        internal static CallbackPromise Resolved() { var promise = new CallbackPromise(); promise.Resolve(); return promise; }
        internal void Resolve() { _resolved = true; _done?.Invoke(); }
        public void Then(Action onDone) { if (_resolved) onDone(); else _done += onDone; }
    }
}
namespace MapRuleLibrary.Adventure
{
    internal static class AdventureState { internal static MapState? MapState; }
    internal sealed class MapState { internal bool IsCampaign; internal HeadquartersState HeadquartersState = new(); }
    internal sealed class HeadquartersState
    { internal bool MerchantUnlocked, TempleUnlocked, EnhancerUnlocked; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Note(string _, string __) { } }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class Setting { internal bool Value; }
    internal static class WorldUIConfig { internal static readonly Setting ImmersiveTownServices = new(); }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active = true; }
}
internal static class Singleton<T> where T : class { internal static T? Instance; }
internal enum EMapFTUEStep { None, CreatedSecondCharacter, VisitMerchant, BuyItem, InteractWithMap }
internal interface IMapFTUEStep { EMapFTUEStep Step { get; } Assets.Script.Misc.ICallbackPromise StartStep(); }
internal sealed class UIMapFTUEStep : IMapFTUEStep
{
    internal UIMapFTUEStep(EMapFTUEStep step) { Step = step; }
    public EMapFTUEStep Step { get; }
    public Assets.Script.Misc.ICallbackPromise StartStep()
    {
        object?[] args = { this, null };
        bool runOriginal = (bool)(typeof(TownServiceTutorialStepPatch).GetMethod("Prefix",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args) ?? true);
        return runOriginal ? new Assets.Script.Misc.CallbackPromise()
            : (Assets.Script.Misc.ICallbackPromise)args[1]!;
    }
}
internal sealed class UIShopItemWindow
{
    // Name deliberately matches the game's serialized private field.
    private readonly UIMapFTUEStep ftueStep;
    internal UIShopItemWindow(UIMapFTUEStep step) { ftueStep = step; }
}
internal sealed class MapFTUEManager
{
    internal static bool IsPlaying;
    private readonly HashSet<EMapFTUEStep> _completed = new();
    private IMapFTUEStep? _current;
    private Assets.Script.Misc.CallbackPromise? _pending;
    internal readonly List<EMapFTUEStep> Started = new();
    internal event Action<EMapFTUEStep>? Finished;
    internal EMapFTUEStep CurrentStep => _current?.Step ?? EMapFTUEStep.None;
    internal bool HasCompletedStep(EMapFTUEStep step) => _completed.Contains(step);
    internal void SetComplete(EMapFTUEStep step) => _completed.Add(step);
    internal void CompleteStep(EMapFTUEStep step)
    { if (CurrentStep == step) _pending?.Resolve(); }
    internal Assets.Script.Misc.ICallbackPromise StartStep(IMapFTUEStep step)
    {
        if (HasCompletedStep(step.Step)) return Assets.Script.Misc.CallbackPromise.Resolved();
        Started.Add(step.Step);
        _current = step;
        var promise = step.StartStep();
        _pending = promise as Assets.Script.Misc.CallbackPromise;
        promise.Then(() => { _completed.Add(step.Step); _current = null; _pending = null; Finished?.Invoke(step.Step); });
        typeof(TownServiceTutorialSequencePatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { this, step });
        return promise;
    }
}

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string text)
    { _checks++; if (!condition) throw new Exception(text); }
    private static void Setup(bool immersive, bool campaign, bool merchantStep = true)
    {
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = true;
        WorldUIConfig.ImmersiveTownServices.Value = immersive;
        AdventureState.MapState = new MapState { IsCampaign = campaign,
            HeadquartersState = { MerchantUnlocked = true, TempleUnlocked = true, EnhancerUnlocked = true } };
        MapFTUEManager.IsPlaying = true;
        Singleton<MapFTUEManager>.Instance = new MapFTUEManager();
        Singleton<MapFTUEManager>.Instance.SetComplete(EMapFTUEStep.CreatedSecondCharacter);
        Singleton<UIShopItemWindow>.Instance = merchantStep
            ? new UIShopItemWindow(new UIMapFTUEStep(EMapFTUEStep.BuyItem)) : null;
    }
    private static void Main()
    {
        for (byte service = 1; service <= 3; service++)
        {
            Check(!TownServiceAvailability.FromNativeState(service, false, false, false, false, true, true),
                "A locked native station and furniture must not appear");
            Check(TownServiceAvailability.FromNativeState(service, true, true, true, false, false, false),
                "An unlocked Campaign/Guildmaster station must appear outside FTUE");
        }
        Check(!TownServiceAvailability.FromNativeState(1, true, true, true, true, false, false),
            "Merchant stays locked until the second character");
        Check(TownServiceAvailability.FromNativeState(1, true, true, true, true, true, false),
            "Merchant unlocks after the second character");
        Check(!TownServiceAvailability.FromNativeState(2, true, true, true, true, true, false)
              && !TownServiceAvailability.FromNativeState(3, true, true, true, true, true, false),
            "Temple and enchantress retain the game's BuyItem FTUE gate");
        Check(!TownServiceAvailability.FromNativeState(4, true, true, true, false, true, true),
            "Unknown service cannot acquire a stand");
        Check(!TownServiceAvailability.ShouldPublish(false, true, true),
            "A remote visit and enabled setting cannot publish a locked resident");
        Check(TownServiceAvailability.ShouldPublish(true, true, false)
              && TownServiceAvailability.ShouldPublish(true, false, true)
              && !TownServiceAvailability.ShouldPublish(true, false, false),
            "Owner and opted-out observer publish only eligible resident sessions");
        // The production loop is the integration boundary: it must retire a locked station
        // before Acquire and continue without setting the aggregate active flag false. Other
        // unlocked residents then remain visible to both the author and observers.
        string population = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs")));
        int loop = population.IndexOf("for (byte service = 1; service <= 3; service++)", StringComparison.Ordinal);
        int guard = population.IndexOf("if (!unlocked)", loop, StringComparison.Ordinal);
        int dispose = population.IndexOf("locked.Station.Dispose()", guard, StringComparison.Ordinal);
        int acquire = population.IndexOf("Acquire(service)", dispose, StringComparison.Ordinal);
        Check(loop >= 0 && guard > loop && dispose > guard && acquire > dispose,
            "Production retires locked NPC and furniture before any station acquisition");
        Check(population.Substring(guard, acquire - guard).Contains("continue;", StringComparison.Ordinal)
              && !population.Substring(guard, acquire - guard).Contains("published.Active = false", StringComparison.Ordinal),
            "One locked service must not hide the aggregate multiplayer resident channel");

        Setup(true, true);
        MapFTUEManager tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Immersive onboarding must advance both native flat-only steps through their promises");
        Check(tutorial.Started.Count == 2 && tutorial.Started[0] == EMapFTUEStep.VisitMerchant
              && tutorial.Started[1] == EMapFTUEStep.BuyItem,
            "The game's native BuyItem step must start once, after VisitMerchant");
        Check(TownServiceAvailability.NativeUnlocked(2) && TownServiceAvailability.NativeUnlocked(3),
            "Later map destinations unlock through native completed steps");
        AdventureState.MapState!.HeadquartersState.TempleUnlocked = false;
        Check(TownServiceAvailability.NativeUnlocked(1)
              && !TownServiceAvailability.NativeUnlocked(2)
              && TownServiceAvailability.NativeUnlocked(3),
            "A locked temple does not disable independently unlocked merchant/enchantress");
        var peerA = new HashSet<byte>(); var peerB = new HashSet<byte>();
        for (int tick = 0; tick < 3; tick++)
            foreach (HashSet<byte> peer in new[] { peerA, peerB })
                for (byte service = 1; service <= 3; service++)
                    if (TownServiceAvailability.ShouldPublish(TownServiceAvailability.NativeUnlocked(service), true, false))
                        peer.Add(service);
        Check(peerA.SetEquals(new byte[] { 1, 3 }) && peerB.SetEquals(peerA),
            "A locked temple has no published stand on either peer while other services remain");
        AdventureState.MapState.HeadquartersState.TempleUnlocked = true;
        for (int tick = 0; tick < 3; tick++)
            foreach (HashSet<byte> peer in new[] { peerA, peerB })
                for (byte service = 1; service <= 3; service++)
                    if (TownServiceAvailability.ShouldPublish(TownServiceAvailability.NativeUnlocked(service), true, false))
                        peer.Add(service);
        Check(peerA.SetEquals(new byte[] { 1, 2, 3 }) && peerB.SetEquals(peerA)
              && peerA.Count == 3 && peerB.Count == 3,
            "Unlock during map browsing converges to exactly one resident per service on each peer");

        Setup(false, true);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant) && tutorial.Started.Count == 1,
            "Flat mode retains the original merchant onboarding");
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceTutorialPatches.Tick();
        TownServiceTutorialPatches.Tick();
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Switching the option on during a pending flat tutorial completes its native continuation");
        TownServiceTutorialPatches.Tick();
        Check(tutorial.Started.Count == 2, "The option transition does not start BuyItem twice");

        Setup(true, false);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "Guildmaster tutorial is independent from Campaign map onboarding");

        Setup(true, true);
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "The 2D map and tutorial scenes keep their original flat merchant flow");

        Setup(true, true, merchantStep: false);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "Missing original BuyItem step must fall back to the intact native lesson");
        Singleton<UIShopItemWindow>.Instance = new UIShopItemWindow(new UIMapFTUEStep(EMapFTUEStep.BuyItem));
        TownServiceTutorialPatches.Tick();
        TownServiceTutorialPatches.Tick();
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Late shop singleton resolves the already-started native lesson without a stranded map");
        AdventureState.MapState = null;
        Check(!TownServiceAvailability.NativeUnlocked(1), "No station before a savegame is loaded");
        Console.WriteLine($"Town resident availability/onboarding: {_checks} assertions.");
    }
}
