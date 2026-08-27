using System;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine.Events;

namespace GloomhavenVR.Compat;

/// <summary>
/// USER REPORT 2026-08-2x (PRIORITÄT): "Ich konnte im Szenario den Multiplayer nicht mehr starten.
/// Wenn ich auf den button gedrückt habe, ist nichts passiert!" — Esc menu → Multiplayer →
/// "Host Session" from INSIDE a running scenario. Pressed four times, nothing visible happened.
///
/// <para><b>WHAT ACTUALLY HAPPENS.</b> The click lands and Bolt starts: the session is created and
/// the peer can even join (Player.log ModBuild 232:11014 the click, :11190 "McFredward (PlayerID: 1)
/// JOINED the session"). What does NOT happen is everything the game does IN REACTION to hosting
/// having started, because the game's own reaction chain is amputated by a NullReferenceException
/// thrown out of a STALE listener:</para>
/// <code>
/// NullReferenceException
///   at UILoadoutManager.MPConfirmEnterScenario () [0x000f9]
///   at UILoadoutManager.OnSwitchedToMultiplayer () [0x0001b]
///   at UnityEngine.Events.UnityEvent.Invoke ()
///   at FFSNet.NetworkManager.CreateSession () [0x0001e]     // Player.log:11230-11242
/// </code>
///
/// <para><b>THE EXACT DEREFERENCE.</b> IL_00f9 of <c>UILoadoutManager.MPConfirmEnterScenario</c> is
/// <c>callvirt UIMapMultiplayerController::ShowLoadoutMultiplayer()</c> on the value returned by
/// <c>Singleton&lt;UIMapMultiplayerController&gt;::get_Instance()</c> (verified by disassembling
/// GH.Runtime.dll — the two preceding IL offsets are the <c>UIReadyToggle.Initialize</c> and
/// <c>SetInteractable</c> calls the log prints as "MP Ready Toggle set to VISIBLE/INTERACTABLE"
/// immediately before the throw). That is decompiled <c>UILoadoutManager.cs:516</c>:
/// <c>Singleton&lt;UIMapMultiplayerController&gt;.Instance.ShowLoadoutMultiplayer();</c> — and
/// <c>Singleton&lt;T&gt;.Instance</c> is a plain static set in <c>Awake</c> and nulled in
/// <c>OnDestroy</c> (Singleton.cs:5-19), so inside a scenario, where the campaign map's
/// multiplayer controller does not exist, it is null. The NRE is on the INSTANCE, not inside
/// <c>ShowLoadoutMultiplayer</c> — that method would have its own stack frame otherwise.
///
/// </para><para><b>WHY THE LISTENER IS EVEN THERE.</b> <c>UILoadoutManager.MultiplayerStartup</c>
/// (UILoadoutManager.cs:447-472) registers <c>OnSwitchedToMultiplayer</c> on
/// <c>HostingStartedEvent</c> in its OFFLINE branch (:469-470) — i.e. every single-player run of
/// the pre-scenario loadout screen arms it. The ONLY place it is ever removed is
/// <c>OnDestroy</c> (:127); <c>OnHidden</c> (:397) — which is also what <c>OnDisable</c> calls
/// (:375-378) — only runs <c>ClearEvents</c> (:402-415), which unsubscribes the party display and
/// the quest, and NOT the hosting event. So closing, hiding or deactivating the loadout screen
/// leaves this listener armed, and it fires the next time the player hosts — from anywhere,
/// including a scenario where its whole subject is gone.
///
/// </para><para><b>WHY THAT MAKES "NOTHING HAPPEN".</b> <c>NetworkManager.CreateSession</c>
/// (NetworkManager.cs:291-296) is <c>GenerateSessionID(); BoltMatchmaking.CreateSession(SessionID);
/// HostingStartedEvent?.Invoke();</c> — the invoke is LAST, so the session survives. But
/// <c>UnityEvent.Invoke</c> walks its listener list in a plain loop with no per-listener catch, so
/// a listener that throws ABORTS EVERY LISTENER REGISTERED AFTER IT. The stale loadout listener was
/// registered on the campaign map, long before anything in the scenario, so it is early in the list
/// and it takes the whole rest with it — including <c>Choreographer.OnSwitchedToMultiplayer</c>
/// (Choreographer.cs:14496, which calls <c>UIScenarioMultiplayerController.ShowMultiPlayer()</c> and
/// subscribes every PlayerRegistry join/leave callback), <c>ESCMenu.OnSwitchedToMultiplayer</c>
/// (ESCMenu.cs:335, the online checkbox), <c>NetworkManager.StartVoiceHost</c> (NetworkManager.cs:205)
/// and — last and most visibly — <c>UIMultiplayerEscSubmenu.OnHostingStartedCallback</c>
/// (UIMultiplayerEscSubmenu.cs:554), the callback handed to <c>ToggleServer</c> by the very button
/// he pressed, whose <c>ShowOnlineOptions(true)</c> is what swaps the "Start Session" button for
/// the invite code and the online options, and whose <c>UIMultiplayerNotifications.ShowStartedSession()</c>
/// is the toast. None of it ran. Presses 2-4 did nothing at all because <c>ToggleServer</c>
/// early-outs once <c>FFSNetwork.IsOnline</c> is true.
///
/// </para><para><b>THE GUARD.</b> A PREFIX on <c>OnSwitchedToMultiplayer</c> that declines to run the
/// vanilla method when its subject is absent, rather than a finalizer that swallows the throw: a
/// finalizer would leave the method HALF-RUN (the ready toggle initialised and made interactable
/// over a screen that is not there — exactly the two log lines that precede the crash, followed by
/// the game immediately hiding it again). The precondition is read from source and is the
/// conjunction of the two things every statement in <c>MPConfirmEnterScenario</c> assumes:
/// <list type="number">
/// <item><c>__instance.IsOpen</c> (<c>_window.IsOpen</c>, UILoadoutManager.cs:57) — the loadout
///   screen this listener belongs to is on screen. Everything the method does is loadout-screen UI:
///   the ready toggle it shows is the loadout screen's "Enter Scenario" toggle, and
///   <c>HideConfirmWarning</c> / <c>RefreshRequirements</c> /
///   <c>SetActiveSinglePlayerLongConfirmButton</c> all drive this component's own serialized
///   widgets.</item>
/// <item><c>Singleton&lt;UIMapMultiplayerController&gt;.IsInitialized</c> — the object line 516
///   dereferences unconditionally.</item>
/// </list>
///
/// </para><para><b>PROVABLY INERT IN THE SUPPORTED FLOW.</b> Hosting FROM the loadout screen is a real
/// flow and <c>MPConfirmEnterScenario</c> is exactly right there. In that flow both terms hold:
/// the window is open (the listener is armed from <c>EnableLoadoutInteraction</c>, which the
/// loadout screen calls while it is up, and nothing on the Esc-menu path hides it — in the ModBuild
/// 232 log 'UI Loadout Window' is reported SHOWN at :5197 and stays open until the scenario loads
/// at :5964), and the map's <c>UIMapMultiplayerController</c> exists — it must, because
/// <c>MapChoreographer.OnSwitchedToMultiplayer</c> (MapChoreographer.cs:3169-3173) is registered
/// EARLIER on the same event and dereferences <c>Singleton&lt;UIMapMultiplayerController&gt;.Instance</c>
/// unconditionally too, so if it were null the map flow would already be broken before this
/// listener ran. When both hold, this prefix returns true and vanilla runs bit-for-bit.
/// And in no case does the guard suppress a call that would otherwise have SUCCEEDED: with the
/// controller missing the method throws, and with the window closed there is no screen for the
/// toggle it shows.
///
/// </para><para><b>ON DECLINE</b> the guard does exactly what vanilla's own first statement does — remove
/// the listener (UILoadoutManager.cs:526) — because this listener is a one-shot by the game's own
/// design and leaving it armed only re-arms the same trap. It then logs ONE Warning per hosting
/// edge. It does NOT touch <c>HostingEndedEvent</c>: vanilla adds
/// <c>OnSwitchedToSinglePlayer</c> there at the END of the method (:531), and adding it here would
/// register a single-player switch for a screen that never switched to multiplayer.
///
/// </para><para><b>VANILLA OR MOD?</b> Nothing of this mod is on the stack, the mod never touches
/// <c>HostingStartedEvent</c>, <c>MultiplayerStartup</c> or <c>ToggleServer</c> (grep: zero hits
/// outside doc comments), and its only contact with the loadout screen — the world-space float —
/// restores the original parent on release (CanvasConversion.4.Lifecycle.cs:154) and is provably
/// released before the scenario loads (Player.log:5955 "6 floated window(s) released"). The leak
/// site named above is vanilla code. See <see cref="HostingChainWatch"/> for the instrument that
/// will settle the one thing the log could not: whether the stale <c>UILoadoutManager</c> is a
/// destroyed object still referenced by the delegate, or a live but deactivated one.
/// </para></summary>
[HarmonyPatch(typeof(UILoadoutManager), "OnSwitchedToMultiplayer")]
internal static class LoadoutHostingGuard
{
    private const string Scope = "Net";

    /// <summary>Cached <c>UILoadoutManager.OnSwitchedToMultiplayer</c> — needed to rebuild an
    /// equal <see cref="UnityAction"/> for <c>RemoveListener</c> (UnityEvent matches on
    /// target+method, so a freshly created delegate over the same pair removes the registered
    /// one).</summary>
    private static MethodInfo? _listenerMethod;

    /// <summary>Declines are one per hosting edge by construction; the counter exists so a
    /// repeat can never be mistaken for a single event, and so the line stays bounded.</summary>
    private static int _declines;

    private const int MaxDetailedDeclines = 5;

    /// <summary>
    /// Returns false to skip the vanilla method. Never throws: any failure inside the guard falls
    /// through to <c>true</c>, which is the vanilla path (this patch must not be able to make the
    /// game worse than not being here).
    /// </summary>
    private static bool Prefix(UILoadoutManager __instance)
    {
        try
        {
            bool windowOpen = IsOpen(__instance);
            bool controllerAlive = Singleton<UIMapMultiplayerController>.IsInitialized
                                   && Singleton<UIMapMultiplayerController>.Instance != null;

            if (windowOpen && controllerAlive)
                return true; // supported flow — vanilla runs unchanged

            _declines++;
            Disarm(__instance);

            if (_declines <= MaxDetailedDeclines)
            {
                VRLog.Warn(Scope,
                    "HOSTING CHAIN GUARD: declined a STALE UILoadoutManager.OnSwitchedToMultiplayer "
                    + $"(decline #{_declines}) — loadout window open: {(windowOpen ? "YES" : "NO")}, "
                    + $"Singleton<UIMapMultiplayerController>: {(controllerAlive ? "ALIVE" : "NULL")}. "
                    + "Vanilla MPConfirmEnterScenario (UILoadoutManager.cs:474-517) would have run "
                    + "to line 516 and thrown a NullReferenceException on "
                    + "Singleton<UIMapMultiplayerController>.Instance, and because UnityEvent.Invoke "
                    + "has no per-listener catch that throw ABORTS EVERY LISTENER REGISTERED AFTER "
                    + "IT on NetworkManager.HostingStartedEvent — the scenario's own "
                    + "Choreographer.OnSwitchedToMultiplayer, the Esc menu's online checkbox, the "
                    + "voice host, and UIMultiplayerEscSubmenu.OnHostingStartedCallback, which is "
                    + "the invite-code panel and the 'session started' toast. That is the user's "
                    + "\"nichts passiert\". The listener is a leftover: MultiplayerStartup arms it in "
                    + "its offline branch (:469-470) and only OnDestroy (:127) ever removes it — "
                    + "OnHidden/OnDisable do not. It has been removed here, exactly as vanilla's own "
                    + "first statement (:526) does, so the chain now completes. "
                    + "GREP 'HOSTING STARTED' for the roster line that proves it.");
                if (_declines == MaxDetailedDeclines)
                    VRLog.Warn(Scope, "HOSTING CHAIN GUARD: further declines will be silent "
                        + "(this listener is a one-shot — more than a handful means something is "
                        + "re-arming it, and that is the next thing to look at).");
            }

            return false;
        }
        catch (Exception e)
        {
            // A guard that cannot decide must not decide. One line, then vanilla.
            VRLog.Warn(Scope, $"HOSTING CHAIN GUARD: precondition check threw {e.GetType().Name} "
                + $"({e.Message}) — running vanilla OnSwitchedToMultiplayer unchanged.");
            return true;
        }
    }

    /// <summary><c>UILoadoutManager.IsOpen</c> forwards to <c>_window.IsOpen</c> and <c>_window</c>
    /// is assigned in <c>Awake</c>; a component whose Awake never ran (or a destroyed one) would
    /// throw here, and "cannot answer" means "not the screen on display".</summary>
    private static bool IsOpen(UILoadoutManager manager)
    {
        try
        {
            return manager != null && manager.IsOpen;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Remove this instance's listener from <c>HostingStartedEvent</c>. Removing during
    /// the very invoke that is walking the list is what the vanilla method itself does on its
    /// first line, so it is proven safe (UnityEvent iterates a separate executing snapshot).</summary>
    private static void Disarm(UILoadoutManager manager)
    {
        try
        {
            NetworkManager manager2 = FFSNetwork.Manager;
            if (manager2 == null)
                return;
            _listenerMethod ??= AccessTools.Method(typeof(UILoadoutManager), "OnSwitchedToMultiplayer");
            if (_listenerMethod == null)
                return;
            var action = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), manager, _listenerMethod);
            manager2.HostingStartedEvent?.RemoveListener(action);
        }
        catch (Exception e)
        {
            VRLog.Warn(Scope, $"HOSTING CHAIN GUARD: could not remove the stale listener "
                + $"({e.GetType().Name}: {e.Message}) — it stays armed and will be declined again.");
        }
    }
}
