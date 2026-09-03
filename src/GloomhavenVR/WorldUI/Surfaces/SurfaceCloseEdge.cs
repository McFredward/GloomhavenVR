using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// THE CLOSE EDGE FOR THE <see cref="FloatingDecisionSurface"/> FAMILY — the one notification
/// <c>WindowMaterialise</c> already has for every modal window and could never have for these.
///
/// <para><b>WHY IT HAS TO EXIST, AND WHY ONE TICK LATER IS TOO LATE.</b>
/// <c>WindowMaterialise</c> learns that a modal is closing from <c>VREvents.WindowVisibility</c>,
/// published by the Harmony postfix on <c>UIWindow.EvaluateAndTransitionToVisualState</c>
/// (Core/Events/GameEventPatches.cs:99) — the single choke point every UIWindow visibility change
/// funnels through. These popups carry no <c>UIWindow</c>; that is the whole premise of
/// <see cref="FloatingDecisionSurface"/>. Their close is the game's own <c>Hide()</c>, and both of
/// them end it the same way:</para>
/// <code>
///   UIDistributePointsPopup.Hide()  → … → window.SetActive(false)   (decompiled :129-141)
///   UIAbilityCardPicker.Hide()      →     window.SetActive(false)   (decompiled :124-126)
/// </code>
/// <para>A hard cut, no tween. Every surface in this family polls exactly that bit
/// (<c>ShownPanel()</c>'s terms are <c>window.activeSelf</c>), so by the time the surface notices,
/// the entire subtree is inactive — and <c>WindowMaterialiseDebris.BuildEmissionTable</c>
/// (WindowMaterialiseDebris.cs:641) skips every element that is not <c>activeInHierarchy</c>. A
/// dissolve started from the surface's own release tick would therefore tear ZERO shards out of a
/// window whose pixels were already gone, and buy a 0.9 s delay on the release with it: every
/// counter true, nothing on the screen. That is ModBuild 387's lesson, and this prefix is what
/// avoids repeating it — it runs BEFORE the deactivate, in the same method, with the content still
/// live.</para>
///
/// <para><b>IT IS A NOTIFICATION AND NOTHING ELSE.</b> Both prefixes return <c>void</c>, so neither
/// can skip, delay or alter the game's <c>Hide()</c>: Harmony only lets a prefix veto when it returns
/// <c>bool</c>, and these do not. Neither reads or writes any game state beyond the popup's own
/// <c>window</c> reference, neither touches the promise
/// <c>MapChoreographer.WaitDistributionEnds</c> waits on, and both are wrapped so a throw inside a
/// decoration can never reach the game's call stack. If this file were deleted the mod would behave
/// exactly as it did in ModBuild 394: the popups would close with no vanish, and nothing else would
/// change.</para>
///
/// <para><b>WHY IT IS INSTALLED FROM HERE AND NOT FROM A MODULE.</b> Every other WorldUI patch is
/// registered from <c>WorldUIModule.Init</c>, which is outside this lane's owned paths. Installing
/// on the first conversion of a decision surface is equivalent in practice and stricter in one
/// respect: a player who never opens one of these popups never gets the patch at all. REQUESTED
/// CHANGE for the integrator: move the two <c>PatchAll</c> calls into <c>WorldUIModule.Init</c>
/// beside the others and make <see cref="EnsureInstalled"/> a no-op — that is the only reason this
/// class carries an installer of its own.</para>
///
/// <para><b>MULTIPLAYER:</b> nothing crosses the wire. A prefix that raises a local presentation
/// event on the client whose game closed a local popup is per-client by construction.</para>
/// </summary>
internal static class SurfaceCloseEdge
{
    private const string Scope = "WorldUI";

    private static bool _installed;

    /// <summary>
    /// Install the two prefixes once. Called from
    /// <see cref="FloatingDecisionSurface.OnConverted"/>, i.e. only after a decision panel has
    /// actually been floated — by which point <c>VRSession.Harmony</c> has existed since
    /// <c>Plugin.Awake</c>.
    ///
    /// <para>A FAILURE HERE COSTS THE VANISH AND NOTHING ELSE, and it says so: with no patch there
    /// is no close edge, so <see cref="SurfaceMaterialise.OnGameClosing"/> is never called, no
    /// vanish is ever started, and every release runs on the surface's own tick exactly as it did
    /// before this feature existed.</para>
    /// </summary>
    internal static void EnsureInstalled()
    {
        if (_installed)
            return;
        _installed = true;
        try
        {
            VRSession.Harmony?.PatchAll(typeof(UIDistributePointsPopup_Hide_Patch));
            VRSession.Harmony?.PatchAll(typeof(UIAbilityCardPicker_Hide_Patch));
            // HW-VERIFY
            VRLog.Note(Scope, "SURFACE CLOSE EDGE INSTALLED: prefixes on UIDistributePointsPopup."
                              + "Hide and UIAbilityCardPicker.Hide are live. They only NOTIFY — both "
                              + "return void, so neither can skip or delay the game's own close — "
                              + "and they exist because both methods end in window.SetActive(false), "
                              + "which takes the whole subtree inactive one statement later. That is "
                              + "the last instant at which a dissolve has any element left to tear a "
                              + "shard out of. WITHOUT THIS LINE there is no vanish animation on any "
                              + "decision window and every release happens on the surface tick, "
                              + "exactly as in every build before this one.");
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, "SURFACE CLOSE EDGE: installing the close-edge prefixes FAILED "
                               + $"({ex.GetType().Name}: {ex.Message}). Decision windows close "
                               + "exactly as they did before — no vanish animation, no deferred "
                               + "release, no change to the decision path. Nothing else in the mod "
                               + "depends on these patches.");
        }
    }

    /// <summary>
    /// Hand the window to the effect's owner, and swallow anything that goes wrong.
    ///
    /// <para>A PREFIX IS THE GAME'S CALL STACK. A throw out of here would propagate into
    /// <c>Hide()</c> and could leave the popup half-closed with its slots still subscribed — on the
    /// one window whose absence produced four deadlock reports. So the body is wrapped, the
    /// failure is reported once, and the game's close continues untouched.</para>
    /// </summary>
    internal static void Publish(GameObject? window)
    {
        if (window == null)
            return;
        try
        {
            SurfaceMaterialise.OnGameClosing(window);
        }
        catch (Exception ex)
        {
            if (_publishFailed)
                return;
            _publishFailed = true;
            VRLog.Error(Scope, "SURFACE CLOSE EDGE: publishing a decision window's close threw "
                               + $"({ex.GetType().Name}: {ex.Message}). The game's own Hide() is "
                               + "unaffected — this prefix returns void and cannot skip it — and the "
                               + "panel is released on the surface's next tick with no animation, "
                               + "which is this feature's documented off-state. This line prints once per "
                               + "process.");
        }
    }

    private static bool _publishFailed;
}

/// <summary>
/// The map-side reward popup and the two scenario distribute popups (flows 3, 4 and 5). One method
/// covers all three: they are three scene instances of one component.
///
/// <para>A TOP-LEVEL CLASS AND NOT A NESTED ONE, for a mechanical reason worth stating so the next
/// author does not "tidy" it back: <c>scripts/patch-inventory.sh</c> writes the class name into
/// docs/PATCH-INVENTORY.md and <c>scripts/check-desync-surface.py</c> parses it back with
/// <c>`([A-Za-z0-9_]+)`</c>. A nested class is written as <c>Outer.Inner</c>, which that pattern
/// cannot match — so the row is silently attributed to whichever patch class was listed above it,
/// and the desync classification the gate exists to enforce lands on the wrong patch. Every other
/// patch class in this tree is top-level, and this is why.</para>
/// </summary>
[HarmonyPatch(typeof(UIDistributePointsPopup), nameof(UIDistributePointsPopup.Hide))]
internal static class UIDistributePointsPopup_Hide_Patch
{
    private static void Prefix(UIDistributePointsPopup __instance) =>
        SurfaceCloseEdge.Publish(__instance?.window);
}

/// <summary>The doom pickers (flow 2). See <see cref="UIDistributePointsPopup_Hide_Patch"/> for why
/// this is a top-level class.</summary>
[HarmonyPatch(typeof(UIAbilityCardPicker), nameof(UIAbilityCardPicker.Hide))]
internal static class UIAbilityCardPicker_Hide_Patch
{
    private static void Prefix(UIAbilityCardPicker __instance) =>
        SurfaceCloseEdge.Publish(__instance?.window);
}
