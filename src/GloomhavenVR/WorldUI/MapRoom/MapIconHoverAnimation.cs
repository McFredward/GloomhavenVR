using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MOUSEOVER OF A MAP SYMBOL — which half of it runs, and the measurement that proves it.
///
/// <para>TWO ROUNDS, TWO SENTENCES, AND THEY ARE NOT IN CONFLICT ONCE THE HALVES ARE NAMED.</para>
///
/// <para>USER, 2026-09-03, verbatim: <i>"Bitte deaktiviere die animationen für das mouseover im
/// Kartenraum wenn ich über ein Kartensymbol hovere - an der Stelle möchte ich es nicht."</i>
/// ModBuild 365 answered that by putting back TWO of the writes <c>MapLocation.Highlight</c>
/// makes: the 20 % scale step on <c>MeshParent</c> and the <c>NodeHoverIndicator</c> particle
/// effect.</para>
///
/// <para>USER, 2026-09-05, verbatim: <i>"Wenn du im flat game über ein Symbol hoverest auf der map
/// wird es temporär etwas größer damit es hervorgehoben ist - das ist aktuell bei uns gar nicht
/// mehr aktiv bzw. nicht sichtbar (eventuell weil ich die Größe geändert habe?). Das will ich
/// wieder haben, unabhängig der eingestellten Symbolgröße."</i></para>
///
/// <para>HE IS RIGHT THAT IT IS GONE, AND HIS GUESS ABOUT THE CAUSE IS WRONG — it was us, not his
/// size setting. The 365 round called the scale step an ANIMATION and it is not one. Read the
/// game's own method (decompiled GH.Runtime/MapLocation.cs:545-551): it is a single assignment,
/// <c>MeshParent.transform.localScale = m_DefaultNodeScale * 1.2f</c>, with no tween, no
/// coroutine, no Animator and no interpolation anywhere near it. It is a STATE, held for exactly
/// as long as the pointer is on the symbol, and on 09-05 he names it for what it is: the thing
/// that makes the symbol <i>"hervorgehoben"</i>. The particle effect beside it is the one that
/// really is an animation — an <c>EffectAlphaFadeParticles</c> with a live <c>ParticleSystem</c>
/// and a 0.6 s alpha-fade coroutine. So the suppression is split along the line the two sentences
/// actually draw, rather than one of them being taken as a retraction of the other:</para>
///
/// <list type="number">
/// <item><b>THE 20 % SCALE STEP — RESTORED (ModBuild 425).</b> The mod no longer writes
///   <c>MeshParent.localScale</c> at all, in either direction. The highlight the player sees is
///   the GAME's own, raised by the game's own <c>Highlight</c> in the game's own order; we simply
///   stopped undoing it. That is deliberately not the same thing as drawing our own pop, and the
///   <c>// HW-VERIFY</c> line below says which of the two the log is describing.</item>
/// <item><b>THE PARTICLE EFFECT — STILL SUPPRESSED</b>, under the unchanged
///   <c>[MapRoom] HoverAnimation</c> dial (shipped off). It is the moving thing he asked to lose
///   and he has not asked for it back. <see cref="UndoHoverAnimation"/> now puts back that write
///   and only that write.</item>
/// <item><b>THE HIGHLIGHT SPRITE and THE MATERIAL SWAP</b> — never touched by either round. Both
///   are instant on/off of a still image (:527-528, :552).</item>
/// </list>
///
/// <para>"UNABHÄNGIG DER EINGESTELLTEN SYMBOLGRÖSSE" IS A STRUCTURAL PROPERTY HERE, NOT A CLAMP WE
/// MAINTAIN. The three symbol-size dials (<c>[MapRoom] IconScale</c>,
/// <c>GloomhavenIconScale</c>, <c>CityIconScale</c>) never touch a game transform: they are a
/// multiplier <see cref="MapIconLayer"/> applies when it bakes the drawn quad from
/// <c>decal.lossyScale.xz</c>. The game's 1.2x lands on <c>MeshParent</c>, and
/// <c>MapLocation.Init</c> instantiates the decal as a CHILD of <c>MeshParent</c>
/// (MapLocation.cs:401) — so the decal's <c>lossyScale</c> carries the 1.2x, and the drawn quad
/// comes out at <c>authored x dial x 1.2</c>. The dial and the highlight MULTIPLY. The pop is
/// therefore 20 % of whatever size he chose, equally visible at 0.5x and at 2.3x, and there is no
/// absolute target size anywhere on the path that could swallow it at a large setting. The
/// instrument prints both factors and their product so this is read off the log rather than
/// argued from this paragraph.</para>
///
/// <para>THE RESTORE IS EXACT BY CONSTRUCTION, AND NOT BECAUSE WE ARE CAREFUL. Every write the
/// game makes to this transform is an assignment from <c>m_DefaultNodeScale</c> — either that
/// field or that field times a constant (:545). <c>m_DefaultNodeScale</c> is recorded ONCE, inside
/// <c>MapLocation.Init</c> (:433), from the authored <c>localScale</c>. No write anywhere reads
/// the CURRENT scale, so nothing can accumulate: a thousand hovers land on the same two values,
/// and the exit value is bit-for-bit the value Init recorded. Since 425 the mod contributes no
/// write of its own to that transform in either direction, so there is not even a second writer to
/// order against. The instrument still compares the exit value to <c>m_DefaultNodeScale</c>
/// component-wise with <c>float.Equals</c> rather than <c>Vector3 ==</c> (which is an approximate
/// compare and would call a slow leak "equal"), so a drift would be reported and not assumed
/// away.</para>
///
/// <para>WHAT WAS RULED OUT BEFORE THIS WAS WRITTEN, because two other causes were plausible and
/// both are cheap to state:
/// <list type="bullet">
/// <item>A WRITE WAR over the icon's scale — the mod re-writing every tick what the game's hover
///   had just set, and winning. It is not that: <see cref="MapIconLayer"/> READS
///   <c>decal.lossyScale</c> and writes nothing above the decal. Its only transform write in this
///   room is the party marker's own root (<c>ApplyTokenScale</c>), which is not a location. A
///   tree-wide grep for <c>MeshParent</c> and <c>m_DefaultNodeScale</c> finds no other mod
///   writer.</item>
/// <item>THE GAME'S HOVER NEVER FIRING IN VR, because the laser feeds clicks but not
///   enter/exit. It is not that either: <see cref="MapLocationInteractor.SetHover"/> calls
///   <c>MapLocation.OnPointerExit</c> and <c>OnPointerEnter</c> on the real component, which is
///   why the highlight sprite and the quest preview card have been working all along.</item>
/// </list></para>
///
/// <para>MULTIPLAYER: nothing to mirror, and nothing new to mirror. A hover is the viewer's own
/// pointer and this class writes no game state. The map room does put a peer's pointed-at location
/// on the wire (<c>PresenceState.MapRoomPickKey</c>), but the receiver deliberately never routes
/// it through a <c>MapLocation</c> — <c>Net/Remote/RemoteMapRoom</c>'s own header: <i>"calling
/// MapLocation.OnPointerEnter for a peer would fight this client's own pointer … Nothing here
/// touches a MapLocation at all; the clone is fed from the location, never through it."</i> So a
/// peer's hover cannot inflate one of our icons and ours cannot inflate one of theirs.</para>
/// </summary>
internal static class MapIconHoverAnimation
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// The scale factor the game multiplies <c>MeshParent</c> by while a location is highlighted
    /// (<c>c_HighlightedNodeScaleFactor</c>, decompiled MapLocation.cs:121, applied at :545-551).
    ///
    /// <para>SINCE ModBuild 425 THIS IS DOCUMENTATION AND AN EXPECTATION, NOT A LEVER. Nothing
    /// applies it, divides by it or restores it: the game raises the highlight and the mod leaves
    /// it standing. It is kept because the instrument prints the factor it MEASURED beside the
    /// factor the game's source says it should be, and a disagreement between those two numbers is
    /// the fastest possible signal that a game update moved the constant — the kind of thing a
    /// hard-coded 1.2 elsewhere in the tree would hide rather than report.</para>
    /// </summary>
    internal const float HighlightedNodeScaleFactor = 1.2f;

    /// <summary>Inflation ratios outside this band are not a highlight, they are a fault (a
    /// <c>MeshParent</c> mid-teardown, a location whose <c>m_DefaultNodeScale</c> was never
    /// recorded). <see cref="InflationOf"/> answers 1 for them rather than handing
    /// <see cref="MapIconHoverPads"/> a divisor that would inflate or collapse a hit box — the
    /// game only ever writes 1x or 1.2x, so anything an order of magnitude away is noise.</summary>
    private const float MinPlausibleInflation = 0.25f;

    /// <inheritdoc cref="MinPlausibleInflation"/>
    private const float MaxPlausibleInflation = 4f;

    /// <summary>Which verdict line has already been printed for the map room now standing:
    /// <c>0</c> none, <c>1</c> SUPPRESSED, <c>2</c> KEPT. A hover fires constantly, so the lines are
    /// edge-triggered on this — but on the VERDICT and not merely on "have we said anything",
    /// because the dial is live and the obvious way to test this feature is to stand in the map
    /// room and toggle it in the options pane. Latching on a bare "already reported" would print
    /// the first state and then stay silent through every flip, which is the one reading a
    /// hardware round needs. Each TRANSITION prints once; a value that does not change costs one
    /// integer compare per hover.</summary>
    private static int _verdict;

    /// <summary>True while the map room is standing and the dial says the hover ANIMATION is off,
    /// i.e. while the postfix will actually undo anything. Since ModBuild 425 the only thing under
    /// that word is the <c>NodeHoverIndicator</c> particle effect; the 20 % scale step is the
    /// game's highlight and runs either way. See the class doc for why the two were separated.
    /// </summary>
    internal static bool Suppressing =>
        MapRoomDriver.Active && !(Plugin.MapHoverAnimation?.Value ?? Defaults.MapHoverAnimation);

    /// <summary>
    /// HOW MUCH BIGGER THAN ITS AUTHORED SIZE IS THIS LOCATION'S DRAWN ICON RIGHT NOW? The one
    /// question <see cref="MapIconHoverPads"/> has to answer to size a hit pad, because it measures
    /// the pad off <c>decal.lossyScale</c> and that value already carries whatever
    /// <c>MapLocation.Highlight</c> last put on <c>MeshParent</c>.
    ///
    /// <para>IT IS MEASURED, NOT MODELLED, AND THAT IS THE POINT (ModBuild 425). The previous
    /// answer was a policy — a bool <c>IsInflated</c> that reconstructed "the last Highlight call
    /// was (true, …)" from <c>IsHighlighted</c> and <c>IsSelected</c> and multiplied a hard-coded
    /// 1.2. That model is wrong at two of the game's own four call sites into <c>Highlight</c>:
    /// <c>Select</c> passes <c>Highlight(true, true)</c> WITHOUT setting <c>m_IsHighlighted</c>
    /// (MapLocation.cs:660-668) and <c>Deselect</c> passes <c>Highlight(false)</c> without clearing
    /// it (:670-677), so the flag and the transform can disagree in both directions. Asking the
    /// transform what it actually says is shorter, cannot drift from the drawn quad, needs no
    /// knowledge of which write is currently in force, and survives a game update that changes the
    /// factor or adds a fifth call site.</para>
    ///
    /// <para>Cheap enough for the pad path it sits on: two transform reads and a divide, no
    /// allocation, no <c>GetComponent</c>.</para>
    /// </summary>
    internal static float InflationOf(global::MapLocation? loc)
    {
        if (loc == null)
            return 1f;
        GameObject meshParent = loc.MeshParent;
        if (meshParent == null)
            return 1f;
        float ratio = RatioOf(meshParent.transform.localScale, loc.m_DefaultNodeScale);
        return ratio >= MinPlausibleInflation && ratio <= MaxPlausibleInflation ? ratio : 1f;
    }

    /// <summary>
    /// <paramref name="now"/> as a multiple of <paramref name="authored"/>, off the first axis the
    /// authored value actually has an extent on. Uniform by construction — every write to this
    /// transform scales the whole <c>Vector3</c> by one scalar — so the axis chosen cannot change
    /// the answer; the search exists only so a decal authored flat on one axis
    /// (<c>OverrideLocationScale</c>, MapLocation.cs:418) still yields a ratio instead of a
    /// division by zero. Returns 1 when there is no usable axis, which reads as "not inflated" and
    /// is the safe answer for every caller.
    /// </summary>
    private static float RatioOf(Vector3 now, Vector3 authored)
    {
        const float Usable = 1e-6f;
        if (Mathf.Abs(authored.x) > Usable)
            return now.x / authored.x;
        if (Mathf.Abs(authored.z) > Usable)
            return now.z / authored.z;
        if (Mathf.Abs(authored.y) > Usable)
            return now.y / authored.y;
        return 1f;
    }

    /// <summary>Component-wise bit equality. Deliberately NOT <c>Vector3 ==</c>, which compares
    /// with a 1e-5 tolerance per axis and would report a slow leak as an exact restore — the one
    /// failure this instrument exists to catch.</summary>
    private static bool BitEqual(Vector3 a, Vector3 b) =>
        a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);

    /// <summary>Forget the printed verdict and the per-outcome report gate, so the next map room
    /// says which way it went again. Called from the postfix the frame it finds the room gone.
    ///
    /// <para>Block-bodied on purpose: as an expression body the assignment shares a line with the
    /// method signature, and <c>check-instrument-writes.py</c> reads a field occurrence as a WRITE
    /// only when it starts its own line — so the reset was scored as a non-diagnostic READ of a
    /// field the loggers write, i.e. as load-bearing instrument state. It is not: nothing outside
    /// the verdict lines ever looks at it. Keeping the write on its own line lets the checker see
    /// what the code actually does.</para></summary>
    internal static void Rearm()
    {
        _verdict = 0;
        _pendingArmed = false;
        _highlightOutcomes.Clear();
    }

    /// <summary>
    /// Put back the ANIMATED write <c>MapLocation.Highlight</c> just made for a hover — since
    /// ModBuild 425 that is the <c>NodeHoverIndicator</c> particle effect and nothing else. Called
    /// from <c>WorldUI.Patches.MapLocationHoverAnimationGate</c>'s postfix and from nowhere else.
    ///
    /// <para>THE SCALE STEP IS NO LONGER TOUCHED. It was, from 365 to 424, and the class doc
    /// carries the user's 09-05 sentence that took it back. Leaving the game's own
    /// <c>MeshParent.localScale</c> write standing is the whole fix: there is nothing to restore,
    /// nothing to order against the game's own restore, and no second writer to lose a write war
    /// to.</para>
    /// </summary>
    /// <param name="loc">The location whose hover was just raised.</param>
    internal static void UndoHoverAnimation(global::MapLocation loc)
    {
        if (loc == null)
            return;

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

        Report(loc, particlesStopped);
    }

    /// <summary>
    /// The SUPPRESSED verdict, printed the first time a hover is actually acted on and again after
    /// any flip of the dial. Edge-triggered on <see cref="_verdict"/> because a hover fires on every
    /// icon the beam crosses; <see cref="Rearm"/> clears it when the room stands down, so the next
    /// room says it again.
    /// </summary>
    private static void Report(global::MapLocation loc, bool particlesStopped)
    {
        if (_verdict == 1)
            return;
        _verdict = 1;
        string where = loc != null && loc.name != null ? loc.name : "?";
        VRLog.Note(Scope, "MAP ICON MOUSEOVER PARTICLE ANIMATION SUPPRESSED — first hover of this "
                          + $"map room landed on '{where}'; NodeHoverIndicator particles stopped = "
                          + $"{(particlesStopped ? "yes" : "they were not running")}. Since ModBuild "
                          + "425 the 20% scale highlight is NOT part of this and is left running — "
                          + "see the MAP ICON HOVER HIGHLIGHT line for it. User 2026-09-03: \"Bitte "
                          + "deaktiviere die animationen für das mouseover im Kartenraum wenn ich "
                          + "über ein Kartensymbol hovere - an der Stelle möchte ich es nicht.\" "
                          + "Turn [MapRoom] HoverAnimation on to get the particle effect back.");
    }

    /// <summary>
    /// The counterpart line for the OTHER state: the room stands, a hover happened, and the dial
    /// says the particle animation was deliberately kept. Without it "no suppression line in the
    /// log" has two readings — the dial is on, or the patch never ran — and a hardware round would
    /// have to guess which. Same edge, same rearm; a flip of the dial in the options pane prints
    /// whichever of the two is now true.
    /// </summary>
    internal static void ReportKept(global::MapLocation loc)
    {
        if (_verdict == 2)
            return;
        _verdict = 2;
        string where = loc != null && loc.name != null ? loc.name : "?";
        VRLog.Note(Scope, "MAP ICON MOUSEOVER PARTICLE ANIMATION KEPT — first hover of this map "
                          + $"room landed on '{where}' and [MapRoom] HoverAnimation is ON, so the "
                          + "game's NodeHoverIndicator particle effect was left running on purpose. "
                          + "Switch that setting off (Umgebung & Ton > Karte 3D) for the "
                          + "no-animation behaviour. The 20% scale highlight is independent of this "
                          + "dial and runs either way since ModBuild 425.");
    }

    // ---- THE HOVER HIGHLIGHT MEASUREMENT (ModBuild 425) --------------------------------------
    //
    // Armed by MapLocationInteractor.SetHover the moment the game's OnPointerEnter has returned,
    // read back by the same method when the pointer leaves that icon. Enter and exit are the two
    // halves of ONE reading and they are strictly interleaved — SetHover exits `had` before it
    // enters `want` — so a single pending record is enough and no dictionary is needed. Everything
    // it holds is a value type or a string already owned by the location, so an arm costs no
    // allocation on a path the beam walks every time it crosses an icon.

    private static int _pendingId;
    private static bool _pendingArmed;
    private static string _pendingName = "?";
    private static float _pendingDial = 1f;
    private static float _pendingFactor = 1f;
    private static bool _pendingHighlighted;
    private static Vector3 _pendingAuthored;
    private static Vector3 _pendingHovered;

    /// <summary>Outcome keys already printed for the map room now standing — the dial in force, the
    /// factor measured and whether the exit restored exactly. One line per distinct OUTCOME rather
    /// than one per icon: forty icons all behaving identically are one fact, and the FIRST icon
    /// that behaves differently gets its own line naming itself. Keying on the dial as well means a
    /// step of the symbol-size slider re-prints, which is exactly the experiment his sentence asks
    /// for ("unabhängig der eingestellten Symbolgröße").</summary>
    private static readonly HashSet<string> _highlightOutcomes = new();

    /// <summary>
    /// Record what the game's highlight did to this icon, immediately after
    /// <c>MapLocation.OnPointerEnter</c> has run. Records only; the line is printed on the way out,
    /// when the restore can be reported in the same sentence as the growth.
    /// </summary>
    internal static void ReportHoverEnter(global::MapLocation? loc)
    {
        _pendingArmed = false;
        if (loc == null)
            return;
        GameObject meshParent = loc.MeshParent;
        if (meshParent == null)
            return;

        _pendingId = loc.GetInstanceID();
        _pendingName = loc.name ?? "?";
        _pendingAuthored = loc.m_DefaultNodeScale;
        _pendingHovered = meshParent.transform.localScale;
        _pendingFactor = RatioOf(_pendingHovered, _pendingAuthored);
        _pendingDial = MapIconLayer.ScaleForLocation(loc);
        _pendingHighlighted = loc.IsHighlighted;
        _pendingArmed = true;
    }

    /// <summary>
    /// The pointer has left <paramref name="loc"/> and the game's <c>OnPointerExit</c> has run.
    /// Print the whole reading — configured size, factor applied, resulting size, exit restore — as
    /// one sentence, once per distinct outcome per map room.
    /// </summary>
    internal static void ReportHoverExit(global::MapLocation? loc)
    {
        if (!_pendingArmed || loc == null || loc.GetInstanceID() != _pendingId)
        {
            _pendingArmed = false;
            return;
        }
        _pendingArmed = false;

        GameObject meshParent = loc.MeshParent;
        if (meshParent == null)
            return;
        Vector3 onExit = meshParent.transform.localScale;
        bool restored = BitEqual(onExit, _pendingAuthored);
        bool grew = _pendingFactor > 1.001f;

        string key = $"{_pendingDial:F2}|{_pendingFactor:F3}|{(restored ? 1 : 0)}|{(grew ? 1 : 0)}"
                     + $"|{(_pendingHighlighted ? 1 : 0)}";
        if (!_highlightOutcomes.Add(key))
            return;

        string grewClause = grew
            ? "GREW as intended"
            : _pendingHighlighted
                ? "DID NOT GROW although the game reports it highlighted — the factor above is the fault"
                : "DID NOT GROW because the game refused to highlight this location at all "
                  + "(MapLocation.CanHighlight -> IsSelectable false); the MAP HOVER VERDICT line "
                  + "beside this one names which gate";
        string restoreClause = restored
            ? "EXACTLY (bit-equal, so no size can leak across hovers)"
            : "*** NOT exactly — the configured size did NOT come back, which is a leak ***";

        // HW-VERIFY: the line that decides the 2026-09-05 map-icon hover round — user: "Wenn du im
        // flat game über ein Symbol hoverest auf der map wird es temporär etwas größer damit es
        // hervorgehoben ist … Das will ich wieder haben, unabhängig der eingestellten Symbolgröße."
        // It must say WHOSE highlight ran (the game's, or one we drew), on which symbol, at which
        // configured symbol size, by what factor, to what size, and whether the exit put the
        // configured size back bit-for-bit. It must stay at a tier the shipped default prints.
        VRLog.Note(Scope, "MAP ICON HOVER HIGHLIGHT — this is the GAME's own highlight running "
                          + "(MapLocation.Highlight raised by OnPointerEnter); the mod draws no pop "
                          + "of its own and, since ModBuild 425, writes nothing to MeshParent in "
                          + $"either direction. Symbol '{_pendingName}': configured symbol size dial "
                          + $"x{_pendingDial:F2}; the game took MeshParent from {_pendingAuthored} "
                          + $"to {_pendingHovered} = x{_pendingFactor:F3} "
                          + $"(c_HighlightedNodeScaleFactor = {HighlightedNodeScaleFactor:F2}), so "
                          + $"the drawn quad went from x{_pendingDial:F2} to "
                          + $"x{_pendingDial * _pendingFactor:F2} of its authored footprint — the "
                          + "dial and the highlight MULTIPLY, so the pop is the same 20% at every "
                          + $"symbol size. {grewClause}. On exit MeshParent read {onExit}, which is "
                          + $"m_DefaultNodeScale {restoreClause}.");
    }
}
