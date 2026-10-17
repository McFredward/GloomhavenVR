using System;
using System.Collections.Generic;

// Deliberate portable hierarchy/activation boundary. The generated production methods
// make all occupancy/glow decisions; this class records their writes, not rasterization.
public sealed class FixtureObject
{
    public bool activeSelf = true;
    public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
    public readonly FixtureTransform transform;
    public object? Component;
    public int ActiveWrites;
    public FixtureObject() { transform = new FixtureTransform(this); }
    public void SetActive(bool value) { activeSelf = value; ActiveWrites++; }
}
public sealed class FixtureTransform
{
    public readonly FixtureObject gameObject;
    public FixtureTransform? parent;
    private readonly List<FixtureTransform> children = new();
    public FixtureTransform(FixtureObject owner) { gameObject = owner; }
    public int childCount => children.Count;
    public FixtureTransform GetChild(int index) => children[index];
    public T? GetComponent<T>() where T : class => gameObject.Component as T;
    public void SetParent(FixtureTransform? value)
    { parent?.children.Remove(this); parent = value; parent?.children.Add(this); }
}
public static class FixtureClock { public static float unscaledTime; }
namespace GloomhavenVR.Cards
{
    internal sealed class VRCard
    {
        internal readonly FixtureObject gameObject = new();
        internal FixtureTransform transform => gameObject.transform;
        internal bool IsHeld;
        internal FullAbilityCard? FullCard;
        internal VRCard() { gameObject.Component = this; }
    }
    internal sealed partial class PlayTray
    {
        private int _wantedMask = -1;
        private readonly FixtureObject?[] _wantedHighlights = { new(), new() };
        private readonly FixtureTransform?[] _slots = { new FixtureObject().transform, new FixtureObject().transform };
        internal bool ConfirmControlShown, UndoControlShown, ItemUseSlotShown, ItemUseCapShown, SkipCapShown;
        internal bool ConfirmCapAccent, ConfirmCapConfirmed, SkipCapEnabled;
        internal int HighlightedSlot = -1;
        internal FixtureTransform Slot(int slot) => _slots[slot]!;
        internal FixtureTransform? SlotTransform(int slot) => _slots[slot];
        internal bool WantedActive(int slot) => _wantedHighlights[slot]!.activeSelf;
    }
    internal static class RestControls
    {
        internal static bool ShortRestShown, LongRestShown, ShortRestEnabled, ShortRestAccent, LongRestEnabled, LongRestAccent;
    }
    internal static class CardsConfig
    {
        internal sealed class Flag { internal bool Value = true; }
        internal static readonly Flag TrayFollow = new();
    }
    internal static class PileViewer { internal static bool ItemsUsableCueOn; }
}
namespace GloomhavenVR.WorldUI.ModalFallback
{ internal static class DecisionDock { internal static object? ActivePrompt() => null; } }
namespace GloomhavenVR.WorldUI.Surfaces
{ internal static class DecisionDockSurface { internal static bool RowFocusHidden; } }
namespace GloomhavenVR.Net
{
    internal static class HeadMaskLibrary { internal const int MaskCount = 3; }
    internal static partial class RevealGate
    {
        internal static bool InScenario = true;
        internal static bool IsSecretSelectionPhase = true;
    }
    internal sealed partial class RemoteAvatar
    {
        internal int PlayerId = 2;
        internal bool HasBoardUi, HasCapStates, ItemsPileUsableCue, SlotOccupancyKnown;
        internal byte BoardButtonsMask, CapStateMask;
        internal int WantedGlowMask, SnapGlowSlot = -1, CapPressKey = -1, BoardSlotMask;
        internal byte HandCardCount, MapLoadoutCount;
        internal int[]? FanArcOrder;
        internal int FanArcOrderCount, FanInsertionGap = -1;
        internal bool DominantRight;
        internal bool SlotOrderKnown, SlotOrderSwapped;
        private bool _loggedSlots;
    }
    internal sealed partial class RemoteBoardFurniture
    {
        private int _shownWantedMask = -1, _shownSnapMask = -1;
        private readonly FixtureObject?[] _wanted = { new(), new() }, _snap = { new(), new() };
        private readonly bool[] _wasFilled = new bool[2];
        private readonly float[] _snapLitAt = { float.NegativeInfinity, float.NegativeInfinity };
        private const float SnapGlowSeconds = .6f;
        internal bool WantedActive(int slot) => _wanted[slot]!.activeSelf;
        internal bool SnapActive(int slot) => _snap[slot]!.activeSelf;
        internal int Writes => _wanted[0]!.ActiveWrites + _wanted[1]!.ActiveWrites;
    }
    internal sealed partial class RemoteControlBoard
    {
        private readonly RemoteAvatar _owner;
        private readonly CAbilityCard?[] _ordered = new CAbilityCard?[2];
        internal RemoteControlBoard(RemoteAvatar owner) { _owner = owner; }
        private void LogSlotOrderIfChanged() { }
        internal CAbilityCard? Ordered(int slot) => _ordered[slot];
    }
}
namespace GloomhavenVR.Hands { internal static class HandStyles { internal const int Count = 3; } }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Info(string scope, string text) { }
        internal static void Warn(string scope, string text) { }
        internal static void Note(string scope, string text) { }
    }
}
// Model/widget identities are explicit fixture inputs. No gameplay controller,
// focus manager or native selection continuation is replaced by these checks.
internal sealed class CAbilityCard { internal string Name = ""; internal int Initiative; }
internal sealed class FullAbilityCard { internal CAbilityCard? abilityCard; }
internal sealed class CCharacterClass
{
    internal readonly List<CAbilityCard> RoundAbilityCards = new();
    internal CAbilityCard? InitiativeAbilityCard;
}
internal sealed class CPlayerActor { internal readonly CCharacterClass CharacterClass = new(); }
internal sealed class CardsHandUI { internal CPlayerActor? PlayerActor; }
internal static class FFSNetwork { internal static bool IsOnline = true; }
