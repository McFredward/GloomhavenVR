using System;
using Code.State;
using Script.GUI.SMNavigation;

namespace GloomhavenVR.Core.Events;

/// <summary>
/// Subscribes to the game's free C# static events (no Harmony needed, CARDS.md §7.3)
/// and re-publishes them on <see cref="VREvents"/>. All subscriptions are undone in
/// <see cref="Unsubscribe"/> (hot-reload contract).
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   // AbilityCardUI (global namespace)
///   public static event Action&lt;AbilityCardUI, bool&gt; CardHoveringStateChanged;
///   public static event Action&lt;AbilityCardUI, bool&gt; CardSelectionStateChanged;
///   // FullAbilityCard (global namespace)
///   public static event Action&lt;bool&gt; FullCardHoveringStateChanged;
///   // ShortRest (global namespace)
///   public static event Action&lt;ShortRest, bool&gt; OnSelectShortRest;
///   // Script.GUI.SMNavigation.UINavigation : Singleton&lt;UINavigation&gt;
///   public NavigationStateMachine StateMachine { get; private set; }   // created once in Setup()
///   public static event Action&lt;UINavigation&gt; OnInitialize;             // fired at end of Setup()
///   // Code.State.StateMachine (base of NavigationStateMachine)
///   public event Action&lt;IState&gt; EventStateChanged;
/// </code>
/// </summary>
internal static class GameEventBridge
{
    private static bool _subscribed;
    private static StateMachine? _hookedStateMachine;

    // Delegate instances kept so unsubscription removes exactly what we added.
    private static readonly Action<AbilityCardUI, bool> OnCardSelection = (card, sel) => VREvents.Raise(new CardSelectionEvent(card, sel));
    private static readonly Action<AbilityCardUI, bool> OnCardHover = (card, hov) => VREvents.Raise(new CardHoverEvent(card, hov));
    private static readonly Action<bool> OnFullCardHover = hov => VREvents.Raise(new FullCardHoverEvent(hov));
    private static readonly Action<ShortRest, bool> OnShortRest = (widget, sel) => VREvents.Raise(new ShortRestEvent(widget, sel));
    private static readonly Action<IState> OnNavState = state => VREvents.Raise(new NavStateEvent(state));
    private static readonly Action<UINavigation> OnNavInitialize = HookNavigation;

    internal static void Subscribe()
    {
        if (_subscribed)
            return;
        _subscribed = true;

        AbilityCardUI.CardSelectionStateChanged += OnCardSelection;
        AbilityCardUI.CardHoveringStateChanged += OnCardHover;
        FullAbilityCard.FullCardHoveringStateChanged += OnFullCardHover;
        ShortRest.OnSelectShortRest += OnShortRest;

        // UINavigation is a DontDestroyOnLoad singleton whose StateMachine is created
        // once in Setup(). Depending on load order we either hook it right away or
        // when OnInitialize fires.
        UINavigation.OnInitialize += OnNavInitialize;
        if (Singleton<UINavigation>.IsInitialized)
            HookNavigation(Singleton<UINavigation>.Instance);

        VRLog.Debug("Events", "Game event bridge subscribed.");
    }

    internal static void Unsubscribe()
    {
        if (!_subscribed)
            return;
        _subscribed = false;

        AbilityCardUI.CardSelectionStateChanged -= OnCardSelection;
        AbilityCardUI.CardHoveringStateChanged -= OnCardHover;
        FullAbilityCard.FullCardHoveringStateChanged -= OnFullCardHover;
        ShortRest.OnSelectShortRest -= OnShortRest;

        UINavigation.OnInitialize -= OnNavInitialize;
        if (_hookedStateMachine != null)
        {
            _hookedStateMachine.EventStateChanged -= OnNavState;
            _hookedStateMachine = null;
        }

        VRLog.Debug("Events", "Game event bridge unsubscribed.");
    }

    private static void HookNavigation(UINavigation navigation)
    {
        StateMachine? machine = navigation != null ? navigation.StateMachine : null;
        if (machine == null || ReferenceEquals(machine, _hookedStateMachine))
            return;

        if (_hookedStateMachine != null)
            _hookedStateMachine.EventStateChanged -= OnNavState;

        _hookedStateMachine = machine;
        machine.EventStateChanged += OnNavState;
        VRLog.Debug("Events", "Hooked UINavigation.StateMachine.EventStateChanged.");
    }
}
