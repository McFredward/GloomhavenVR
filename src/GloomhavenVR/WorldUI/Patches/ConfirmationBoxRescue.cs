using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// <b>THE INVARIANT: A CLICK ON A CONTROL THAT ASKS THE PLAYER TO CONFIRM MUST ALWAYS END IN
/// EXACTLY ONE OF TWO OBSERVABLE STATES — a usable confirmation is on screen, or the action has
/// happened. NEVER "nothing".</b> That is the whole of this file. It is deliberately written as
/// an invariant over the confirmation box's own show path and NOT as a fix for one caller,
/// because the user ruled out the one-caller reading in as many words.
///
/// <para><b>USER, 2026-09-03, verbatim (the report):</b> <i>"WICHTIG: Erneutes DEADLOCK: ich habe
/// auf 'Quest verwerfen' in einem Szenario geklickt und es nichts weiter erschienen. Das DARF
/// NICHT PASSIEREN. Somit komm ich jetzt nicht aus dem Szenario raus. Normalerweise sollte der
/// Dialog kommen. Zuvor war ich im Tutorial und konnte diesmal zurückkehren."</i></para>
///
/// <para><b>USER, same day, rejecting a per-route workaround (the scope ruling):</b> <i>"Deine
/// Idee 'nimm im Pausenmenü „Hauptmenü“ statt „Quest verwerfen“' ist keine Alternative. Das
/// 'Quest verwerfen' nicht funktioniert ist ein echter bug und muss auf jeden Fall gefixed
/// werden! SO etwas darf niemals auftreten, auch nicht bei 'Hauptmenu'"</i> — so the remedy is
/// anchored on the SHOW PATH every caller funnels through, not on any one menu row.</para>
///
/// <para><b>WHAT ACTUALLY BROKE (proven, .planning/debug/second_logs/Player.log, ModBuild 357).</b>
/// The mod's canvas conversion re-parents a game window under a float host it creates with
/// <c>new GameObject(...)</c>, i.e. in the ACTIVE scene. A GameObject belongs to the scene of its
/// ROOT, so that reparent MIGRATED the game's <c>Confirmation Box_MainMenu_pc</c> — which lives
/// under the persistent <c>Persistent UI_unified</c> root — out of DontDestroyOnLoad and into the
/// tutorial's scene. Log :10601 then shows Unity REFUSING the restore reparent ("Cannot set the
/// parent of the GameObject 'Confirmation Box_MainMenu_pc' while activating or deactivating the
/// parent GameObject 'GloomhavenVR.Panel_Modal_Confirmation Box_MainMenu_pc'") because that
/// release ran inside the materialise carrier's <c>OnDisable</c>. The box was destroyed with the
/// host / the scene. <c>UIConfirmationBoxManager.MainMenuInstance</c> is persistent and kept its
/// managed reference, so two scene loads later, in a scenario, <c>ResetConfirmationBox()</c> ran
/// on a dead object and died at <c>primaryContent.SetActive(true)</c> — a
/// <c>NullReferenceException</c> raised inside <c>(wrapper managed-to-native)
/// UnityEngine.GameObject.SetActive</c>, i.e. a live managed reference with a DEAD NATIVE
/// POINTER. (A never-serialized field would have thrown at the callvirt in
/// <c>ResetConfirmationBox</c> itself; the wrapper frame is the proof that it was destroyed.)
/// The throw escaped <c>async void UIMenuOptionToggle.OnToggleValueChanged</c> and reached the
/// synchronization context unhandled. <c>UIScenarioEscMenu.QuitDungeon()</c> had ALREADY hidden
/// the pause menu and taken its <c>UiNavigationBlocker</c>, so the player was left standing in a
/// scenario with no menu, no dialog and no way out.</para>
///
/// <para><b>THE PRIMARY FIX IS NOT HERE.</b> It is in <c>CanvasConversion</c>: the float host now
/// joins the target's own scene before the reparent, the restore reparent is VERIFIED instead of
/// assumed, and a host is never destroyed while it still holds game content. This file is the
/// belt-and-braces that has to hold even when something we have not thought of breaks the box
/// next time — because "the dialog did not appear" is a dead end with no feedback at all, and
/// the standing ruling is that it must ALWAYS be possible to leave a menu and a scenario.</para>
///
/// <para><b>COVERAGE — WHICH CALLERS.</b> Every confirmation the game raises through the
/// <c>ConfirmationBox</c> component funnels into one of exactly three methods that call
/// <c>ResetConfirmationBox()</c> (decompiled/GH.Runtime/ConfirmationBox.cs): the private
/// 9-argument <c>ShowGenericConfirmation</c> (:329, reached from the public 12-arg overload :311
/// that <c>UIConfirmationBoxManager</c> uses, from <c>ShowGenericCancelConfirmation</c> :209 and
/// from <c>ShowCancelActiveAbility</c> :407), the public 8-argument <c>ShowGenericConfirmation</c>
/// (:217, the innermost one), and <c>ShowGenericSpendConfirmation</c> (:355). All three are
/// patched. That covers BOTH box families that use this component — the in-scenario
/// <c>ConfirmationBox</c> ('Confirmation Box_pc'/'_gamepad', <c>Singleton&lt;UIConfirmationBox
/// Manager&gt;</c>) and <c>MainMenuConfirmationBox</c> ('Confirmation Box_MainMenu_pc'/'_gamepad',
/// <c>UIConfirmationBoxManager.MainMenuInstance</c>) — and therefore every exit row of the pause
/// menu: "Quest verwerfen" (<c>UIScenarioEscMenu.QuitDungeon</c>), "Hauptmenü"
/// (<c>ESCMenu.LoadMainMenu</c>) and "Beenden" (<c>ESCMenu.ExitGame</c>), all three of which go
/// through the 12-arg overload into :329.</para>
///
/// <para><b>WHAT IS NOT COVERED, AND WHY.</b> <c>UIWindowID.CharacterConfirmationBox</c> is
/// <c>UIAdventureCharacterConfirmationBox</c> and <c>UIWindowID.MutiplayerConfirmationBox</c> is
/// its own multiplayer widget — neither is a <c>ConfirmationBox</c>, neither shares
/// <c>ResetConfirmationBox</c>, and neither is an exit from a scenario or a menu. They are named
/// here so the next reader does not have to re-derive that.</para>
///
/// <para><b>TWO NETS, NOT ONE.</b> A PREFIX diverts when the box is provably unusable (a dead
/// field is a fact we can read before the game touches it), and a FINALIZER diverts when the
/// game's own show path threw anyway — a cause we did not predict. The finalizer is what makes
/// the invariant hold in general: whatever goes wrong inside <c>ShowGenericConfirmation</c>, the
/// player still gets a dialog carrying the caller's own question and the caller's own
/// callbacks.</para>
///
/// <para><b>THE FALLBACK INVOKES THE GAME'S OWN <c>UnityAction</c>s AND NOTHING ELSE.</b> No game
/// state is written from here by any other route; confirming really performs the caller's action
/// (for "Quest verwerfen" that is <c>Choreographer.AbandonScenario()</c> plus its blocker
/// release), cancelling really performs the caller's cancel (which is what un-latches the pause
/// menu row and releases <c>UiNavigationBlocker</c>). The dialog itself is
/// <see cref="VersionDialog"/> — the mod's existing from-scratch world-space panel, reused
/// rather than re-invented, precisely because it owes the game's modal stack nothing and can
/// appear at any moment.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire. It is a local presentation fallback
/// for a local UI failure; two clients may legitimately disagree about whether their own copy of
/// a UI prefab is intact, and the action that is finally invoked is the game's own, on the
/// clicking client, exactly as if the game's box had been shown.</para>
/// </summary>
internal static class ConfirmationRescue
{
    private const string Scope = "WorldUI";

    /// <summary>The mod-owned stand-in. One instance: two confirmations can never be pending.</summary>
    private static readonly VersionDialog Dialog = new();

    /// <summary>Serialized members of <c>ConfirmationBox</c> that its show path dereferences.
    /// Every one of them is a <c>UnityEngine.Object</c>, so ONE test — Unity's overloaded
    /// <c>== null</c> — answers both "never assigned" and "destroyed at runtime".</summary>
    private static readonly string[] RequiredMembers =
    {
        "confirmationBox",           // the UIWindow it Show()s
        "primaryContent",            // <- the one that threw on 2026-09-03
        "secondaryGeneralContent",
        "secondaryContent",
        "reportContent",
        "header",
        "confirmButton",
        "cancelButton",
        "reportButton",
        "confirmButtonText",
        "cancelButtonText",
        "headerText",
        "primaryContentText",
        "secondaryGeneralContentText",
    };

    private static readonly Dictionary<string, FieldInfo?> Fields = new(RequiredMembers.Length);
    private static bool _fieldsResolved;
    private static bool _armedLogged;

    /// <summary>Per-frame keep-in-view for the stand-in (registered by <c>WorldUIModule</c>).</summary>
    internal static void Tick()
    {
        if (Dialog.IsShowing)
            Dialog.Tick();
    }

    /// <summary>Module shutdown / hot reload: the stand-in must never outlive the mod.</summary>
    internal static void Reset() => Dialog.Close();

    /// <summary>
    /// Name the FIRST dead member of a confirmation box, or null when it is usable. Naming the
    /// member rather than returning a bool is deliberate: a hardware log has to say WHICH part
    /// died, not that "something" did.
    /// </summary>
    internal static string? DeadPart(ConfirmationBox? box)
    {
        if (box == null)
            return "the ConfirmationBox component itself (destroyed or missing)";
        ResolveFields();
        for (int i = 0; i < RequiredMembers.Length; i++)
        {
            string name = RequiredMembers[i];
            if (!Fields.TryGetValue(name, out FieldInfo? f) || f == null)
                continue; // a member this build of the game does not have — not evidence of damage
            object? raw;
            try { raw = f.GetValue(box); }
            catch { continue; }
            var obj = raw as UnityEngine.Object;
            if (raw != null && obj == null)
                continue; // not a UnityEngine.Object at all — nothing to judge
            if (obj == null)
                return name;
        }
        return null;
    }

    private static void ResolveFields()
    {
        if (_fieldsResolved)
            return;
        _fieldsResolved = true;
        for (int i = 0; i < RequiredMembers.Length; i++)
        {
            string name = RequiredMembers[i];
            Fields[name] = AccessTools.Field(typeof(ConfirmationBox), name);
        }
    }

    /// <summary>
    /// PUT A USABLE CONFIRMATION ON SCREEN. Returns true when the stand-in is up, i.e. when the
    /// game's own path may be skipped or its exception swallowed.
    ///
    /// <para>If a stand-in cannot be built (no head camera — VR is not running, so there is
    /// nothing to float it in front of), the caller's CANCEL is invoked instead and the failure
    /// is logged at Error. That is the honest degradation: cancel unwinds whatever the caller
    /// took before asking (the pause menu's <c>UiNavigationBlocker</c>, the exit row's
    /// <c>isSelected</c> latch), so the player is back where he started and can act again —
    /// which is still not "nothing". It never silently CONFIRMS: abandoning a quest the player
    /// was never asked about would be a worse bug than the one being fixed.</para>
    /// </summary>
    private static bool Divert(ConfirmationBox? box, string why, string? title, string? explanation,
        UnityAction? onConfirm, UnityAction? onCancel)
    {
        try
        {
            ClearRequestedFlag(box);

            string head = string.IsNullOrEmpty(title) ? Translate("GUI_CONFIRMATION", "?") : title!;
            string body = explanation ?? string.Empty;
            string yes = Translate("YES", "Ja");
            string no = Translate("NO", "Nein");

            if (CanvasConversion.WorldCamera == null)
            {
                // HW-VERIFY: the last-resort branch. Reading this line means the player was NOT
                // shown a dialog and the caller's cancel was run instead.
                VRLog.Error(Scope, "CONFIRMATION RESCUE COULD NOT DRAW: the game's confirmation box "
                    + $"is unusable ({why}) and there is no VR head camera to float the mod's "
                    + "stand-in in front of, so the caller's CANCEL was invoked instead of showing "
                    + $"anything. The question that was lost was: '{head}'. The player is back "
                    + "where he started and can click again; nothing was confirmed on his behalf.");
                onCancel?.Invoke();
                return true;
            }

            Dialog.Show(head, body, yes, no,
                () => Run(onConfirm, "confirm"),
                () => Run(onCancel, "cancel"));

            // HW-VERIFY: THE LINE THAT DECIDES THE NEXT HARDWARE ROUND. If it appears, the game's
            // confirmation box was broken and the mod put a working one up in its place.
            VRLog.Alert(Scope, "CONFIRMATION RESCUE ENGAGED: the game's confirmation box could not "
                + $"be shown — {why}. A mod-owned stand-in is now floating in front of the player "
                + $"carrying the game's own question ('{head}') and the game's own confirm/cancel "
                + "UnityActions, so confirming performs exactly the action the caller passed. "
                + "THIS IS THE 2026-09-03 DEADLOCK NET: without it the click produces NOTHING AT "
                + "ALL and the player cannot leave the scenario. If you are reading this in a "
                + "hardware log, the box is damaged and the CanvasConversion scene/detach fixes "
                + "did not prevent it — that is the bug, not this line.");
            return true;
        }
        catch (Exception e)
        {
            VRLog.Error(Scope, "CONFIRMATION RESCUE ITSELF THREW: "
                + $"{e.GetType().Name}: {e.Message}. Falling through to the game's own path.");
            return false;
        }
    }

    private static void Run(UnityAction? action, string which)
    {
        if (action == null)
        {
            VRLog.Note(Scope, $"CONFIRMATION RESCUE {which}: the caller passed no {which} action, "
                + "so the stand-in just closed. Nothing was written to the game.");
            return;
        }
        try
        {
            action.Invoke();
            VRLog.Note(Scope, $"CONFIRMATION RESCUE {which}: the caller's own UnityAction ran. The "
                + "outcome is exactly what the game's own dialog would have produced.");
        }
        catch (Exception e)
        {
            VRLog.Error(Scope, $"CONFIRMATION RESCUE {which} action threw: "
                + $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// <c>ShowGenericConfirmation</c> sets <c>IsRequested = true</c> BEFORE it touches anything
    /// that can die, so a throw leaves that flag standing forever — and it is read as "a
    /// confirmation is pending" by the game and by the mod (<c>EnemyInfoPhaseSkip</c>,
    /// <c>FlatScreen</c>). Lowering it is un-sticking a presentation latch on an object we are
    /// already replacing; it is not game state, and it is the only thing written here besides
    /// the caller's own actions.
    /// </summary>
    private static void ClearRequestedFlag(ConfirmationBox? box)
    {
        if (box == null)
            return;
        try
        {
            MethodInfo? setter = AccessTools.PropertySetter(typeof(ConfirmationBox), "IsRequested");
            setter?.Invoke(box, new object[] { false });
        }
        catch
        {
            // Never fatal — the stand-in is up either way.
        }
    }

    private static string Translate(string key, string fallback)
    {
        try
        {
            return GLOOM.LocalizationManager.TryGetTranslation(key, out string t)
                   && !string.IsNullOrEmpty(t)
                ? t
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    // ---- the shared decision, used by all three patch classes ---------------------------

    /// <summary>
    /// PREFIX SIDE: divert when the box is PROVABLY unusable. Returns false to skip the game's
    /// method (Harmony's "do not run the original").
    /// </summary>
    internal static bool PrefixDecide(ConfirmationBox box, string? title, string? explanation,
        UnityAction? onConfirm, UnityAction? onCancel)
    {
        ArmOnce();
        string? dead = DeadPart(box);
        if (dead == null)
            return true; // healthy — the game's own dialog, untouched
        return !Divert(box, $"{dead} is null or destroyed", title, explanation, onConfirm, onCancel);
    }

    /// <summary>
    /// FINALIZER SIDE: the game's path threw for a reason we did not predict. Put the stand-in up
    /// and swallow the exception (returning null from a finalizer suppresses it) — an unhandled
    /// throw here reaches an <c>async void</c> and takes the whole click with it.
    /// </summary>
    internal static Exception? FinalizerDecide(ConfirmationBox box, Exception? __exception,
        string? title, string? explanation, UnityAction? onConfirm, UnityAction? onCancel)
    {
        if (__exception == null)
            return null;
        string why = $"it threw {__exception.GetType().Name}: {__exception.Message}";
        return Divert(box, why, title, explanation, onConfirm, onCancel) ? null : __exception;
    }

    /// <summary>
    /// One-shot proof of life. A guard whose only log line is change-gated is indistinguishable
    /// from a guard that never ran ([[held-instrument-reads-as-dead]]), and this one is expected
    /// to be silent in every healthy session.
    /// </summary>
    private static void ArmOnce()
    {
        if (_armedLogged)
            return;
        _armedLogged = true;
        // HW-VERIFY: proof the rescue is LIVE. Its absence means the patch never ran at all,
        // which is a different bug from "the box was fine".
        VRLog.Note(Scope, "CONFIRMATION RESCUE ARMED: the first confirmation of this session went "
            + "through the guard. From here on, every ConfirmationBox show path is checked before "
            + "it runs and wrapped in a finalizer, so a click on 'Quest verwerfen', 'Hauptmenü', "
            + "'Beenden' or any other confirm control can only end with a usable dialog on screen "
            + "or with the action performed — never with nothing. Silence from CONFIRMATION "
            + "RESCUE ENGAGED after this line means the game's own box was healthy every time.");
    }
}

/// <summary>
/// The 9-argument private <c>ConfirmationBox.ShowGenericConfirmation</c> — the funnel every
/// pause-menu exit reaches (public 12-arg overload → here), plus
/// <c>ShowGenericCancelConfirmation</c> and <c>ShowCancelActiveAbility</c>. It is the only one of
/// the three that carries BOTH the confirm and the cancel <c>UnityAction</c>, which is why it is
/// the primary anchor: cancelling has to run the caller's own cancel or the pause menu row stays
/// latched and <c>UiNavigationBlocker</c> stays held.
/// </summary>
[HarmonyPatch]
internal static class ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch
{
    private static MethodBase? TargetMethod() =>
        ConfirmationBoxRescueTargets.ShowGenericConfirmation(withCancelAction: true);

    private static bool Prefix(ConfirmationBox __instance, string title, string explanation,
        UnityAction onActionConfirmed, UnityAction onActionCancelled) =>
        ConfirmationRescue.PrefixDecide(__instance, title, explanation,
            onActionConfirmed, onActionCancelled);

    private static Exception? Finalizer(ConfirmationBox __instance, Exception __exception,
        string title, string explanation, UnityAction onActionConfirmed,
        UnityAction onActionCancelled) =>
        ConfirmationRescue.FinalizerDecide(__instance, __exception, title, explanation,
            onActionConfirmed, onActionCancelled);
}

/// <summary>
/// The 8-argument public <c>ConfirmationBox.ShowGenericConfirmation</c> (:217) — the innermost
/// one, and the only one a caller outside this file's known set could reach directly. It carries
/// no cancel action, so the stand-in's "Nein" simply closes, which is what the game's own Cancel
/// would do for such a caller.
/// </summary>
[HarmonyPatch]
internal static class ConfirmationBox_ShowGenericConfirmation_Single_Rescue_Patch
{
    private static MethodBase? TargetMethod() =>
        ConfirmationBoxRescueTargets.ShowGenericConfirmation(withCancelAction: false);

    private static bool Prefix(ConfirmationBox __instance, string title, string explanation,
        UnityAction onActionConfirmed) =>
        ConfirmationRescue.PrefixDecide(__instance, title, explanation, onActionConfirmed, null);

    private static Exception? Finalizer(ConfirmationBox __instance, Exception __exception,
        string title, string explanation, UnityAction onActionConfirmed) =>
        ConfirmationRescue.FinalizerDecide(__instance, __exception, title, explanation,
            onActionConfirmed, null);
}

/// <summary>
/// <c>ConfirmationBox.ShowGenericSpendConfirmation</c> (:355) — the third and last entry point
/// that calls <c>ResetConfirmationBox</c>. Not an exit control, but the same failure CLASS: the
/// user's ruling is that a confirmation must never silently fail to appear, so it is covered too.
/// </summary>
[HarmonyPatch]
internal static class ConfirmationBox_ShowGenericSpendConfirmation_Rescue_Patch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(ConfirmationBox), "ShowGenericSpendConfirmation");

    private static bool Prefix(ConfirmationBox __instance, string title,
        UnityAction onActionConfirmed, UnityAction onActionCancelled) =>
        ConfirmationRescue.PrefixDecide(__instance, title, null,
            onActionConfirmed, onActionCancelled);

    private static Exception? Finalizer(ConfirmationBox __instance, Exception __exception,
        string title, UnityAction onActionConfirmed, UnityAction onActionCancelled) =>
        ConfirmationRescue.FinalizerDecide(__instance, __exception, title, null,
            onActionConfirmed, onActionCancelled);
}

/// <summary>
/// Overload resolution for the two <c>ShowGenericConfirmation</c> methods that call
/// <c>ResetConfirmationBox</c>. Resolved by SHAPE (does parameter 4 carry a cancel
/// <c>UnityAction</c>?) rather than by a full type list, so a signature that gains an optional
/// argument in a game patch still resolves instead of silently patching nothing.
/// </summary>
internal static class ConfirmationBoxRescueTargets
{
    internal static MethodBase? ShowGenericConfirmation(bool withCancelAction)
    {
        List<MethodInfo> all = AccessTools.GetDeclaredMethods(typeof(ConfirmationBox));
        MethodInfo? best = null;
        for (int i = 0; i < all.Count; i++)
        {
            MethodInfo m = all[i];
            if (m.Name != "ShowGenericConfirmation")
                continue;
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length < 4)
                continue;
            if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string))
                continue;
            if (ps[2].ParameterType != typeof(UnityAction))
                continue;
            if ((ps[3].ParameterType == typeof(UnityAction)) != withCancelAction)
                continue;
            // The public 12-argument overload starts with the same four parameter types as the
            // private 9-argument one, but only FORWARDS to it. The method that actually calls
            // ResetConfirmationBox is the SHORTER of the two, so take the fewest parameters.
            if (best == null || ps.Length < best.GetParameters().Length)
                best = m;
        }
        if (best != null)
            return best;
        // 2026-09 refactor, F-77 — the ONE silent line in a file whose every other VRLog call is
        // at a printing tier, and it reports the guard being switched OFF. The file's own markers
        // say what is at stake: "THE LINE THAT DECIDES THE NEXT HARDWARE ROUND", and "proof the
        // rescue is LIVE. Its absence means the patch never ran at all" — with one arm inert the log
        // carries neither that arm's ARMED line nor any explanation. Bounded by construction: this
        // is a Harmony TargetMethod resolver with two call sites at registration, so at most two
        // lines per session. Alert, not Error: the mod still works, the arm is simply absent.
        VRLog.Alert("WorldUI", "CONFIRMATION RESCUE: no ConfirmationBox.ShowGenericConfirmation "
            + $"overload with{(withCancelAction ? "" : "out")} a cancel action was found — that "
            + "arm of the deadlock guard is INERT for this build of the game.");
        return null;
    }
}
