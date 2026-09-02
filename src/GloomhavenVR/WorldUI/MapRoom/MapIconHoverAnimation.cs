using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MOUSEOVER ANIMATION OF A MAP SYMBOL, SWITCHED OFF AT ITS SOURCE (ModBuild 365).
///
/// <para>USER, 2026-09-03, verbatim: <i>"Bitte deaktiviere die animationen für das mouseover im
/// Kartenraum wenn ich über ein Kartensymbol hovere - an der Stelle möchte ich es nicht."</i>
/// Kartenraum = the 3D map room; Kartensymbol = a location icon on the parchment. He asked for the
/// ANIMATIONS to stop. He did not ask to lose the hover, the highlight, the quest-preview card or
/// the click, and none of them changes.</para>
///
/// <para>THE DRIVER, NAMED. There is no tween, no Animator and no coroutine on the map-icon hover
/// path: it is one method, <c>MapLocation.Highlight(bool active, bool isSelected)</c>, decompiled
/// GH.Runtime/MapLocation.cs:525-555, reached from <c>OnPointerEnter</c> (:253-262) — which in this
/// room is called by <see cref="MapLocationInteractor.SetHover"/> from the VR ray — and from
/// <c>ForceHighlight</c> (:293-308) and <c>Select</c>/<c>Deselect</c> (:660-676). It does FOUR
/// visible things, and they are not the same kind of thing:
/// <list type="number">
/// <item><b>A 20 % SCALE POP</b> — <c>MeshParent.transform.localScale = m_DefaultNodeScale * 1.2f</c>
///   (:545-551, the constant is <c>c_HighlightedNodeScaleFactor</c> at :121), bracketed by a
///   <c>SetActive(false)/SetActive(true)</c> of the same object. The drawn icon is a child of
///   <c>MeshParent</c> (:401), and <see cref="MapIconLayer"/> builds each icon quad from
///   <c>decal.lossyScale.xz</c> every frame — so this write is exactly what makes the symbol JUMP
///   in the headset. <b>SUPPRESSED.</b></item>
/// <item><b>A PARTICLE EFFECT</b> — <c>nodeHoverIndicator</c>, an <c>EffectAlphaFadeParticles</c>
///   instantiated from <c>GlobalSettings.MapLocationEffectsPrefabs.NodeHoverIndicatorPrefab</c>
///   (:981-987) and switched on at :533-535. It is a live <c>ParticleSystem</c> with an alpha-fade
///   coroutine over <c>animTime</c> = 0.6 s, i.e. the one thing on the icon that is unambiguously
///   an animation. <b>SUPPRESSED.</b></item>
/// <item><b>A HIGHLIGHT SPRITE</b> — <c>highlightRenderer.enabled = active</c> (:527-528). An
///   instant on/off of a still sprite. <b>LEFT RUNNING</b>: it does not move, and it is the
///   affordance that says WHICH symbol the beam is on. Removing it would remove the hover
///   indication, which is not what he asked for.</item>
/// <item><b>A MATERIAL SWAP</b> — <c>decal.ReferenceToMaterial = highlightedMeshMaterial</c>
///   (:552), i.e. the symbol's artwork brightens. Also instant, also still. <b>LEFT
///   RUNNING</b>, same reason.</item>
/// </list>
/// The fifth and sixth things <c>Highlight</c> does are NOT presentation and are not touched by any
/// of this: <c>MapChoreographer.OnMapLocationHighlight</c> (:553 → MapChoreographer.cs:1304-1425)
/// moves the party token and refreshes the route lines, and <c>UpdateMarkers</c> (:554) is what
/// raises the quest-preview card the player reads.</para>
///
/// <para>WHY AN UNDO AND NOT A SKIP. A prefix that skipped <c>Highlight</c> outright would take the
/// card and the navigation with it, and a transpiler that removed two statements would be a second
/// copy of the game's method to keep in sync. The postfix
/// (<c>WorldUI.Patches.MapLocationHoverAnimationGate</c>) instead puts back the two writes the
/// method just made, in the same frame, before anything renders — so every other consequence of a
/// hover happens exactly as the game intends and in the game's own order.</para>
///
/// <para>HOVER ONLY, NOT SELECTION. The gate is <c>active &amp;&amp; !isSelected</c>. A SELECTED
/// location keeps its 1.2x — that is the "this one is chosen" state, not a mouseover, and he named
/// the mouseover.</para>
///
/// <para>NOTHING IS WRITTEN TO GAME STATE. Two presentation writes are restored to values the game
/// itself computed one line earlier: <c>MeshParent.localScale</c> back to the game's own
/// <c>m_DefaultNodeScale</c>, and the hover indicator back to the exact three assignments the
/// game's own hover-EXIT branch makes (:539-541). No selection, no navigation, no location data,
/// no rule library, no Bolt. And it is scoped to the 3D map room by
/// <see cref="MapRoomDriver.Active"/>: on the flat 2D campaign map ([Rig] Vanilla2DMap on) and in
/// every other scene the postfix returns before touching anything.</para>
///
/// <para>MULTIPLAYER: nothing to mirror. A hover is the viewer's own pointer. The map room does put
/// a peer's pointed-at location on the wire (<c>PresenceState.MapRoomPickKey</c>), but the receiver
/// deliberately never routes it through a <c>MapLocation</c> — <c>Net/Remote/RemoteMapRoom</c>'s
/// own header: <i>"calling MapLocation.OnPointerEnter for a peer would fight this client's own
/// pointer … Nothing here touches a MapLocation at all; the clone is fed from the location, never
/// through it."</i> So a peer's hover never reaches <c>Highlight</c>, this suppression cannot change
/// what any peer sees, and there is no owner-follows-owner question to answer.</para>
/// </summary>
internal static class MapIconHoverAnimation
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// The scale factor the game multiplies <c>MeshParent</c> by while a location is highlighted
    /// (<c>c_HighlightedNodeScaleFactor</c>, decompiled MapLocation.cs:121, applied at :545-551).
    /// Named here as well as in <see cref="MapIconHoverPads"/> because the two ask DIFFERENT
    /// questions of the same number — that class divides it out of a hit box, this one decides
    /// whether it is ever applied — and a shared constant between them would tie a hit-box
    /// measurement to a presentation policy.
    /// </summary>
    internal const float HighlightedNodeScaleFactor = 1.2f;

    /// <summary>Which verdict line has already been printed for the map room now standing:
    /// <c>0</c> none, <c>1</c> SUPPRESSED, <c>2</c> KEPT. A hover fires constantly, so the lines are
    /// edge-triggered on this — but on the VERDICT and not merely on "have we said anything",
    /// because the dial is live and the obvious way to test this feature is to stand in the map
    /// room and toggle it in the options pane. Latching on a bare "already reported" would print
    /// the first state and then stay silent through every flip, which is the one reading a
    /// hardware round needs. Each TRANSITION prints once; a value that does not change costs one
    /// integer compare per hover.</summary>
    private static int _verdict;

    /// <summary>True while the map room is standing and the dial says the animation is off, i.e.
    /// while the postfix will actually undo anything. Everything else in the mod that needs to know
    /// whether an icon is currently inflated asks through <see cref="IsInflated"/> rather than
    /// reading this.</summary>
    internal static bool Suppressing =>
        MapRoomDriver.Active && !(Plugin.MapHoverAnimation?.Value ?? Defaults.MapHoverAnimation);

    /// <summary>
    /// IS THIS LOCATION'S DRAWN ICON CURRENTLY INFLATED BY THE HIGHLIGHT FACTOR? The one question
    /// <see cref="MapIconHoverPads"/> has to answer to size a hit pad, and the reason it cannot
    /// just keep asking <c>loc.IsHighlighted</c>.
    ///
    /// <para>With the animation running (the dial on, or any scene that is not the map room) the
    /// answer is what it has always been: the game inflates on <c>Highlight(active: true, …)</c>,
    /// and <c>m_IsHighlighted</c> tracks that argument.</para>
    ///
    /// <para>With it suppressed, the HOVER half of that is put back in the same frame and only a
    /// SELECTION's inflation is left standing — which is why the suppressed answer is the
    /// conjunction. Every call site inside <c>MapLocation</c> passes its own <c>m_IsSelected</c> as
    /// the second argument (:260, :301, :375) or sets the field immediately before passing a
    /// literal (:664, :674), so <c>IsHighlighted &amp;&amp; IsSelected</c> reconstructs "the last
    /// Highlight call was <c>(true, true)</c>" — exactly the case the postfix does not touch.</para>
    /// </summary>
    internal static bool IsInflated(global::MapLocation? loc)
    {
        if (loc == null)
            return false;
        return Suppressing ? loc.IsHighlighted && loc.IsSelected : loc.IsHighlighted;
    }

    /// <summary>Forget the printed verdict, so the next map room says which way it went again.
    /// Called from the postfix the frame it finds the room gone.
    ///
    /// <para>Block-bodied on purpose: as an expression body the assignment shares a line with the
    /// method signature, and <c>check-instrument-writes.py</c> reads a field occurrence as a WRITE
    /// only when it starts its own line — so the reset was scored as a non-diagnostic READ of a
    /// field the loggers write, i.e. as load-bearing instrument state. It is not: nothing outside
    /// the two verdict lines ever looks at it. Keeping the write on its own line lets the checker
    /// see what the code actually does.</para></summary>
    internal static void Rearm()
    {
        _verdict = 0;
    }

    /// <summary>
    /// Put back the two animated writes <c>MapLocation.Highlight</c> just made for a HOVER. Called
    /// from <c>WorldUI.Patches.MapLocationHoverAnimationGate</c>'s postfix and from nowhere else.
    /// </summary>
    /// <param name="loc">The location whose hover was just raised.</param>
    internal static void UndoHoverAnimation(global::MapLocation loc)
    {
        if (loc == null)
            return;

        bool scalePutBack = false;
        bool zeroDefault = false;
        Vector3 defaultScale = loc.m_DefaultNodeScale;
        GameObject meshParent = loc.MeshParent;
        if (meshParent != null)
        {
            // THE ZERO GUARD IS NOT DECORATION. m_DefaultNodeScale is recorded inside the location's
            // own setup (decompiled MapLocation.cs:433) from MeshParent's authored localScale; a
            // Highlight that somehow ran before that would hand us Vector3.zero, and writing it
            // would not "restore" the icon, it would DELETE it — MapIconLayer floors the drawn quad
            // at 0.01 but the pad would collapse and the symbol would be a smear. Refusing the write
            // leaves the game's own 1.2x standing, i.e. the pre-364 picture, which is a visible
            // no-op and not a broken map.
            if (defaultScale.sqrMagnitude <= 0f)
            {
                zeroDefault = true;
            }
            else
            {
                // No SetActive(false)/SetActive(true) bracket around this write, unlike the game's
                // own (:548-550). That toggle exists to make the Decalicious decal re-project, and
                // a deferred decal contributes nothing to the forward head camera anyway — the icon
                // the player sees is MapIconLayer's own quad, rebuilt from decal.lossyScale EVERY
                // frame. Repeating the toggle would restart every particle system under MeshParent
                // once per hover for no picture at all.
                meshParent.transform.localScale = defaultScale;
                scalePutBack = true;
            }
        }

        // The hover indicator, returned to the state the game's OWN hover-exit branch leaves it in
        // (decompiled MapLocation.cs:539-541) — the same three assignments in the same order, so a
        // later real un-hover finds nothing unexpected and the component never drifts out of the
        // two states the game knows about.
        bool particlesStopped = false;
        EffectAlphaFadeParticles indicator = loc.nodeHoverIndicator;
        if (indicator != null && indicator.gameObject.activeSelf)
        {
            indicator.gameObject.SetActive(false);
            indicator.enabled = false;
            indicator.fadeOut = true;
            particlesStopped = true;
        }

        Report(loc, scalePutBack, zeroDefault, particlesStopped);
    }

    /// <summary>
    /// The SUPPRESSED verdict, printed the first time a hover is actually acted on and again after
    /// any flip of the dial. Edge-triggered on <see cref="_verdict"/> because a hover fires on every
    /// icon the beam crosses; <see cref="Rearm"/> clears it when the room stands down, so the next
    /// room says it again.
    /// </summary>
    private static void Report(global::MapLocation loc, bool scalePutBack, bool zeroDefault,
                               bool particlesStopped)
    {
        if (_verdict == 1)
            return;
        _verdict = 1;
        string where = loc != null && loc.name != null ? loc.name : "?";
        // HW-VERIFY: this is the line that decides the 2026-09-03 map-icon mouseover round — it says
        // whether the suppression ran at all, on which symbol, and which of the two animated writes
        // it actually reached. It must stay at a tier the shipped default prints.
        VRLog.Note(Scope, "MAP ICON MOUSEOVER ANIMATION SUPPRESSED — first hover of this map room "
                          + $"landed on '{where}'; MeshParent scale put back to the game's own "
                          + $"m_DefaultNodeScale = {(scalePutBack ? "yes" : zeroDefault ? "NO, the game had not recorded one yet (left at 1.2x)" : "no MeshParent")}, "
                          + $"NodeHoverIndicator particles stopped = {(particlesStopped ? "yes" : "they were not running")}. "
                          + "The hover itself is untouched: the highlight sprite, the highlighted "
                          + "symbol material, the quest preview card and the click all still run. "
                          + "User 2026-09-03: \"Bitte deaktiviere die animationen für das mouseover "
                          + "im Kartenraum wenn ich über ein Kartensymbol hovere - an der Stelle "
                          + "möchte ich es nicht.\" Turn [MapRoom] HoverAnimation on to get the "
                          + "game's original behaviour back.");
    }

    /// <summary>
    /// The counterpart line for the OTHER state: the room stands, a hover happened, and the dial
    /// says the animation was deliberately kept. Without it "no suppression line in the log" has two
    /// readings — the dial is on, or the patch never ran — and a hardware round would have to guess
    /// which. Same edge, same rearm; a flip of the dial in the options pane prints whichever of the
    /// two is now true.
    /// </summary>
    internal static void ReportKept(global::MapLocation loc)
    {
        if (_verdict == 2)
            return;
        _verdict = 2;
        string where = loc != null && loc.name != null ? loc.name : "?";
        // HW-VERIFY: the negative half of the same verdict — it distinguishes "the dial is on" from
        // "the patch never fired", which is the whole question if he reports the symbols still move.
        VRLog.Note(Scope, "MAP ICON MOUSEOVER ANIMATION KEPT — first hover of this map room landed "
                          + $"on '{where}' and [MapRoom] HoverAnimation is ON, so the game's own 20% "
                          + "scale pop and its NodeHoverIndicator particle effect were left running "
                          + "on purpose. Switch that setting off (Umgebung & Ton > Karte 3D) for the "
                          + "no-animation behaviour.");
    }
}
