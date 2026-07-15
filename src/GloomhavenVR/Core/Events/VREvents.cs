using System;
using Code.State;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Core.Events;

// ---------------------------------------------------------------------------
// Typed event payloads. All structs (no per-event heap allocation); fields are
// readonly references into live game objects — consume synchronously, do not
// cache across frames unless you know the object's lifetime.
// ---------------------------------------------------------------------------

/// <summary>An engine message reached the main thread via the Choreographer pump.</summary>
internal readonly struct ChoreoMessageEvent
{
    /// <summary>Message discriminator (~200 values, see CMessageData.MessageType).</summary>
    public readonly CMessageData.MessageType Type;

    /// <summary>The full message (downcast to the concrete C*_MessageData for payloads).</summary>
    public readonly CMessageData Message;

    public ChoreoMessageEvent(CMessageData message)
    {
        Type = message.m_Type;
        Message = message;
    }
}

/// <summary>The Choreographer switched its wait/interaction state.</summary>
internal readonly struct ChoreoStateEvent
{
    public readonly Choreographer.ChoreographerStateType State;

    public ChoreoStateEvent(Choreographer.ChoreographerStateType state) => State = state;
}

/// <summary>The UI navigation state machine entered a new state (gamepad-focus SM, fires in mouse mode too).</summary>
internal readonly struct NavStateEvent
{
    /// <summary>The new state instance (compare via <c>State.GetType()</c>).</summary>
    public readonly IState State;

    public NavStateEvent(IState state) => State = state;
}

/// <summary>An ability card in the 2D hand got (de)selected.</summary>
internal readonly struct CardSelectionEvent
{
    public readonly AbilityCardUI Card;
    public readonly bool Selected;

    public CardSelectionEvent(AbilityCardUI card, bool selected)
    {
        Card = card;
        Selected = selected;
    }
}

/// <summary>The mouse pointer started/stopped hovering an ability card in the 2D hand.</summary>
internal readonly struct CardHoverEvent
{
    public readonly AbilityCardUI Card;
    public readonly bool Hovering;

    public CardHoverEvent(AbilityCardUI card, bool hovering)
    {
        Card = card;
        Hovering = hovering;
    }
}

/// <summary>A full-size (played) card is being hovered (half-selection affordance).</summary>
internal readonly struct FullCardHoverEvent
{
    public readonly bool Hovering;

    public FullCardHoverEvent(bool hovering) => Hovering = hovering;
}

/// <summary>The short-rest token was (un)selected during card selection.</summary>
internal readonly struct ShortRestEvent
{
    public readonly ShortRest Widget;
    public readonly bool Selected;

    public ShortRestEvent(ShortRest widget, bool selected)
    {
        Widget = widget;
        Selected = selected;
    }
}

/// <summary>
/// The game locked/unlocked its 2D UI (UIManager.ToggleLockUI disables every
/// GraphicRaycaster — the game's modality mechanism, UI-ARCH §4.3).
/// </summary>
internal readonly struct UiLockEvent
{
    public readonly bool Locked;

    public UiLockEvent(bool locked) => Locked = locked;
}

/// <summary>
/// Static VR event bus (FROZEN Phase-2 API). Feature modules subscribe here instead
/// of patching game code themselves.
///
/// Threading rules:
/// - Every event fires on the Unity MAIN thread (the sources are the Choreographer
///   pump, uGUI callbacks and Harmony postfixes on main-thread methods).
/// - Handlers must NEVER block (the ScenarioRuleLibrary worker thread is fed from
///   here — a blocked handler stalls the whole game; see CARDS.md §8 spin-wait trap).
/// - Handlers should not throw; a throwing handler is logged and does not take the
///   game down, but it aborts the remaining handlers of that single raise.
/// - Do not do per-frame work in handlers; they are edge-triggered notifications.
/// </summary>
internal static class VREvents
{
    /// <summary>Engine → UI message processed (Choreographer.ProcessMessage postfix).</summary>
    public static event Action<ChoreoMessageEvent>? ChoreographerMessage;

    /// <summary>Choreographer wait-state changed (SetChoreographerState postfix).</summary>
    public static event Action<ChoreoStateEvent>? ChoreographerStateChanged;

    /// <summary>UINavigation.StateMachine.EventStateChanged re-published.</summary>
    public static event Action<NavStateEvent>? NavigationStateChanged;

    /// <summary>AbilityCardUI.CardSelectionStateChanged re-published.</summary>
    public static event Action<CardSelectionEvent>? CardSelectionChanged;

    /// <summary>AbilityCardUI.CardHoveringStateChanged re-published.</summary>
    public static event Action<CardHoverEvent>? CardHoverChanged;

    /// <summary>FullAbilityCard.FullCardHoveringStateChanged re-published.</summary>
    public static event Action<FullCardHoverEvent>? FullCardHoverChanged;

    /// <summary>ShortRest.OnSelectShortRest re-published.</summary>
    public static event Action<ShortRestEvent>? ShortRestSelected;

    /// <summary>UIManager.ToggleLockUI observed (true = UI locked/modal).</summary>
    public static event Action<UiLockEvent>? UiLockChanged;

    // -- publishers (called by the bridge/patches; not part of the frozen surface) --

    internal static void Raise(in ChoreoMessageEvent e) => Invoke(ChoreographerMessage, e, nameof(ChoreographerMessage));
    internal static void Raise(in ChoreoStateEvent e) => Invoke(ChoreographerStateChanged, e, nameof(ChoreographerStateChanged));
    internal static void Raise(in NavStateEvent e) => Invoke(NavigationStateChanged, e, nameof(NavigationStateChanged));
    internal static void Raise(in CardSelectionEvent e) => Invoke(CardSelectionChanged, e, nameof(CardSelectionChanged));
    internal static void Raise(in CardHoverEvent e) => Invoke(CardHoverChanged, e, nameof(CardHoverChanged));
    internal static void Raise(in FullCardHoverEvent e) => Invoke(FullCardHoverChanged, e, nameof(FullCardHoverChanged));
    internal static void Raise(in ShortRestEvent e) => Invoke(ShortRestSelected, e, nameof(ShortRestSelected));
    internal static void Raise(in UiLockEvent e) => Invoke(UiLockChanged, e, nameof(UiLockChanged));

    /// <summary>
    /// Invoke guard: a subscriber exception must never propagate into the game's
    /// message pump (we're inside Harmony postfixes on Choreographer.Update paths).
    /// </summary>
    private static void Invoke<T>(Action<T>? handler, in T payload, string eventName) where T : struct
    {
        if (handler == null)
            return;
        try
        {
            handler(payload);
        }
        catch (Exception ex)
        {
            VRLog.Error("Events", $"Subscriber to {eventName} threw: {ex}");
        }
    }
}
