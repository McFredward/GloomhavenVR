using System;
using System.Collections.Generic;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// DEPENDENT OPTIONS FOLD OUT UNDER THE SETTING THAT ENABLES THEM.
///
/// <para>ROOT CAUSE OF ITS EXISTENCE (user report 2026-08-11, item 4, verbatim): "Manche Optionen
/// sind nur relevant wenn bestimmte einstellungen gewählt wurden. Beispiel: 'Neigungslimit unten'
/// und 'Neigungslimit oben' ist nur relveant, wenn bei 'Brett Bewegung' 'Begrenzt mit Neigung'
/// eingestellt ist. Durchsuche die Optionen nach weiteren solchen Fällen und bau es so ein, dass
/// diese Optionen unter den entsprechenden Einstellungen dann erst aufklappen."</para>
///
/// <para>THE MECHANISM. A row can declare "visible only while entry (Section, Key) has a value the
/// predicate accepts" — one parent per row, either for one exact row (<see cref="DependentRows"/>)
/// or for a whole config section whose every entry is the tuning OF one master switch
/// (<see cref="DependentSections"/>; the master itself is exempt by the self-guard, so a section
/// rule can never hide its own switch). The check is TRANSITIVE: a child is visible only while its
/// parent's own dependency chain is also satisfied (ScaleMin needs ScaleEnabled, which needs
/// WorldGrabEnabled), bounded by a fixed depth so a declaration mistake can never loop.</para>
///
/// <para>THE REFRESH PATH IS THE ONE THE VARIANT FOLD ALREADY USES: editing a parent runs the same
/// rebuild <c>Apply</c> triggers for the board/hand-style choosers (<c>SelectsAVariant</c> ‖
/// <see cref="IsDependencyParent"/>), so dependent rows appear and disappear the moment the parent
/// changes, in whichever view is open. The filter itself rides <c>IsRowVisible</c> beside the
/// per-variant filter — curated tabs, the automatic topic pages and the hand-arranged trees all
/// go through it, exactly as they already fold per-variant rows.</para>
///
/// <para>DELIBERATELY CONSERVATIVE. Only rows that are TRULY INERT without their parent are
/// declared — each rule below cites the bound description that says so. Anything arguable
/// (SpawnInCircle's index fallback, DecisionPokeDeliberate vs. the world-panel poke, the [Perf]
/// pins, RevealIgnoreWhenGrabbing) stays visible: a row wrongly shown costs a glance, a row
/// wrongly hidden costs a search.</para>
///
/// <para>MISSING PARENTS DEGRADE SILENTLY. Another lane is deleting dials right now; a rule whose
/// parent is no longer in the catalog simply stops gating (the child shows), the same tolerance
/// the curated list's <c>Lookup</c> already has for missing rows themselves.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>"Visible while the parent entry's value satisfies the predicate."</summary>
    private readonly struct DependencyRule
    {
        internal readonly string ParentSection;
        internal readonly string ParentKey;
        internal readonly Func<object?, bool> Satisfied;

        internal DependencyRule(string parentSection, string parentKey, Func<object?, bool> satisfied)
        {
            ParentSection = parentSection;
            ParentKey = parentKey;
            Satisfied = satisfied;
        }
    }

    /// <summary>Parent bool is ON.</summary>
    private static readonly Func<object?, bool> On = static v => v is bool b && b;

    /// <summary>
    /// Parent value's name equals <paramref name="name"/> — one comparison shape for enums (boxed
    /// member → member name) and curated strings alike, case-insensitive because the string
    /// entries' own readers compare that way (see ConfigCatalog.CuratedChoices).
    /// </summary>
    private static Func<object?, bool> Named(string name) =>
        v => string.Equals(v?.ToString(), name, StringComparison.OrdinalIgnoreCase);

    private static Func<object?, bool> NotNamed(string name) =>
        v => !string.Equals(v?.ToString(), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Per-row dependencies. Each rule names the parent whose bound description declares the child
    /// inert without it; the comment carries the evidence.
    /// </summary>
    private static readonly Dictionary<string, DependencyRule> DependentRows = new(StringComparer.Ordinal)
    {
        // ---- Komfort ▸ Drehen: the two speed dials belong to their mode, the hand to any ------
        ["Comfort/SnapTurnDegrees"] = new("Comfort", "TurnMode", Named("Snap")),
        ["Comfort/SmoothTurnSpeed"] = new("Comfort", "TurnMode", Named("Smooth")),
        ["Comfort/TurnHand"] = new("Comfort", "TurnMode", NotNamed("Off")),

        // ---- Komfort ▸ Fortbewegung: "Off = that stick does nothing" ---------------------------
        ["Comfort/FlightDirection"] = new("Comfort", "FlightEnabled", On),
        ["Comfort/FlightMaxSpeed"] = new("Comfort", "FlightEnabled", On),
        ["Comfort/FlightHand"] = new("Comfort", "FlightEnabled", On),

        // ---- Komfort ▸ Welt greifen: every gesture dial rides the grab, the two clamps ride the
        //      pinch ("Two-grip pinch scales the table") — ScaleMin/Max chain through ScaleEnabled
        //      to WorldGrabEnabled transitively.
        ["Comfort/VerticalDrag"] = new("Comfort", "WorldGrabEnabled", On),
        ["Comfort/RotateEnabled"] = new("Comfort", "WorldGrabEnabled", On),
        ["Comfort/ScaleEnabled"] = new("Comfort", "WorldGrabEnabled", On),
        ["Comfort/ScaleMin"] = new("Comfort", "ScaleEnabled", On),
        ["Comfort/ScaleMax"] = new("Comfort", "ScaleEnabled", On),

        // ---- Grafik ▸ Leistung: "Has no effect at all when EnableGraphicsJobs is false" --------
        ["Core/AutoRestartForGraphicsJobs"] = new("Core", "EnableGraphicsJobs", On),

        // ---- Grafik ▸ Darstellung: the element-mood strength is the tuning OF the toggle — its
        //      bound description opens with "Has no effect at all while 'EnvironmentResponse' is
        //      off", and the code makes that literally true: with the toggle off the whole per-frame
        //      path early-outs and the published master is 0 whatever the dial says
        //      (Core/ElementMood.Tick).
        ["Elements/ResponseStrength"] = new("Elements", "EnvironmentResponse", On),

        // ---- Brett & Karten: THE NAMED CASE — the pitch window only exists in the
        //      "Begrenzt mit Neigung" scheme (BoardMoveMode.LimitedPitch, whose own enum doc names
        //      BoardPitchMin/Max as the window it clamps to). All six per-board keys, so the pair
        //      folds on every board; the variant filter then shows the selected board's.
        ["Cards/BoardPitchMin_Oak"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),
        ["Cards/BoardPitchMin_Steel"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),
        ["Cards/BoardPitchMin_Bronze"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),
        ["Cards/BoardPitchMax_Oak"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),
        ["Cards/BoardPitchMax_Steel"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),
        ["Cards/BoardPitchMax_Bronze"] = new("Cards", "BoardMoveMode", Named("LimitedPitch")),

        // ---- Karten & Fächer: both thresholds' descriptions open with "RevealMode=tilt:" -------
        ["Cards/RevealEnterDegrees"] = new("Cards", "RevealMode", Named("tilt")),
        ["Cards/RevealExitDegrees"] = new("Cards", "RevealMode", Named("tilt")),

        // ---- Tafeln ▸ Lebensbalken: the five Bar* dials USED to fold under [WorldUI]
        //      ActorBars. That parent is gone (user ruling 2026-08-13 — the bars themselves
        //      are not optional), so they are top-level rows now: there is no longer a state
        //      in which they have nothing to size, clamp or occlude.

        // ---- Tafeln ▸ 2D-Schirm, stereo block: "0 = mono (same as StereoScreen=false)";
        //      VideoDepth is "How far … (VideoDepthLayer)" and chains through it.
        ["WorldUI/ScreenDepthStrength"] = new("WorldUI", "StereoScreen", On),
        ["WorldUI/ScreenParallaxScale"] = new("WorldUI", "StereoScreen", On),
        ["WorldUI/VideoDepthLayer"] = new("WorldUI", "StereoScreen", On),
        ["WorldUI/VideoDepth"] = new("WorldUI", "VideoDepthLayer", On),

        // ---- Tafeln ▸ 2D-Schirm, map block: the two-step chain (MapWindOpacity →
        //      MapAlbedoRender → ScreenLeftMirrorFallback) is gone with its two parents (user
        //      ruling 2026-08-13: both of their OFF states left the campaign map black). The
        //      map rescue always runs, so the clouds dial always has a capture to tame.

        // ---- Figuren greifen: "Ignored while StretchLimits is off" — the NEW case wired with
        //      this mechanism. Chains through the [FigureGrab] section rule to GrabFigures.
        ["FigureGrab/StretchScaleMin"] = new("FigureGrab", "StretchLimits", On),
        ["FigureGrab/StretchScaleMax"] = new("FigureGrab", "StretchLimits", On),

        // ---- Avatar & Mehrspieler ▸ Zusammen spielen: [Net] Enabled "Turn OFF to fully remove
        //      the networking hook" — no remote players to tag, no peer boards to show, no
        //      handshake to guard, no peer fades to receive. SyncPeerFades' exact rule also
        //      overrides the [WallFade] section rule: its master is the netcode, not the fade.
        ["Net/NameTags"] = new("Net", "Enabled", On),
        ["Net/RemoteBoards"] = new("Net", "Enabled", On),
        ["Net/VersionGuard"] = new("Net", "Enabled", On),
        ["WallFade/SyncPeerFades"] = new("Net", "Enabled", On),
    };

    /// <summary>
    /// Whole-section dependencies, for the sections that ARE one feature's tuning: every entry
    /// (except the master itself — the self-guard) folds under the feature switch. An entry bound
    /// into such a section later is gated by existing, which is the right default for a section
    /// whose name is the feature.
    /// </summary>
    private static readonly Dictionary<string, DependencyRule> DependentSections = new(StringComparer.Ordinal)
    {
        // The wall-fade tuning ([WallFade] OnFraction/OffFraction/ExitDwell*/StackedShellFade)
        // tunes a fade that [Compat] WallFade must first turn on. SyncPeerFades is carved out
        // above — its master is [Net] Enabled.
        ["WallFade"] = new("Compat", "WallFade", On),
        // Everything in [MixedReality] shapes the passthrough compositing MR itself performs.
        ["MixedReality"] = new("MixedReality", "Enabled", On),
        // "OFF: buttons pop in/out instantly (no dust, no fade)" — the three animation dials
        // have nothing to time or emit.
        ["ButtonAnim"] = new("ButtonAnim", "Enable", On),
        // [Keyboard] has no master any more ([Keyboard] Enabled removed 2026-08-13), so
        // AutoCapitalise stands on its own — the keyboard is always there to shift.
        // The whole grab family — pick radius, stretch gesture, held pose, held info — tunes a
        // grab that GrabFigures=false removes outright.
        ["FigureGrab"] = new("FigureGrab", "GrabFigures", On),
    };

    /// <summary>Ids of every entry that gates other rows — the rebuild-on-edit trigger set.</summary>
    private static readonly HashSet<string> DependencyParents = BuildDependencyParents();

    private static HashSet<string> BuildDependencyParents()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DependencyRule rule in DependentRows.Values)
            ids.Add(Id(rule.ParentSection, rule.ParentKey));
        foreach (DependencyRule rule in DependentSections.Values)
            ids.Add(Id(rule.ParentSection, rule.ParentKey));
        return ids;
    }

    /// <summary>Editing this entry can fold or unfold other rows — the pane must rebuild.</summary>
    private static bool IsDependencyParent(ConfigCatalog.ConfigItem item) =>
        DependencyParents.Contains(Id(item.Section, item.Key));

    /// <summary>Longest declared chain is 2 (VideoDepth → VideoDepthLayer → StereoScreen);
    /// anything deeper is a declaration cycle and stops gating. (The map chain that used to be
    /// the example died with its two parents — user ruling 2026-08-13.)</summary>
    private const int MaxDependencyDepth = 4;

    /// <summary>This row declares a parent — it is drawn slightly indented beneath it.</summary>
    private static bool HasDependency(ConfigCatalog.ConfigItem item) =>
        TryGetDependency(item.Section, item.Key, out _);

    private static bool TryGetDependency(string section, string key, out DependencyRule rule)
    {
        if (DependentRows.TryGetValue(Id(section, key), out rule))
            return true;
        if (DependentSections.TryGetValue(section, out rule))
            // The self-guard: a section rule must never gate its own master switch.
            return !(string.Equals(rule.ParentSection, section, StringComparison.Ordinal)
                     && string.Equals(rule.ParentKey, key, StringComparison.Ordinal));
        return false;
    }

    /// <summary>
    /// Is this row's dependency (if any) satisfied? True for a row with no declaration, for a rule
    /// whose parent has left the catalog (silent degrade — see the class doc), and for a predicate
    /// that throws; transitively false while any ancestor in the chain says no.
    /// </summary>
    private static bool DependencyMet(ConfigCatalog.ConfigItem item) =>
        DependencyMet(item.Section, item.Key, 0);

    private static bool DependencyMet(string section, string key, int depth)
    {
        if (depth >= MaxDependencyDepth)
            return true;
        if (!TryGetDependency(section, key, out DependencyRule rule))
            return true;

        EnsureLookup();
        if (!ByKey.TryGetValue(Id(rule.ParentSection, rule.ParentKey), out ConfigCatalog.ConfigItem? parent)
            || parent == null)
            return true; // the parent dial is gone — the child degrades to always-visible

        try
        {
            if (!rule.Satisfied(parent.Entry.BoxedValue))
                return false;
        }
        catch (Exception)
        {
            return true; // an unreadable parent must not hide a working child
        }

        return DependencyMet(rule.ParentSection, rule.ParentKey, depth + 1);
    }

    /// <summary>
    /// THE one row filter: the per-variant fold and the dependency fold together. Every view —
    /// curated tabs, automatic topic pages, the hand-arranged trees — asks this and nothing else,
    /// so the two mechanisms can never disagree between pages.
    /// </summary>
    private static bool IsRowVisible(ConfigCatalog.ConfigItem item) =>
        IsShownForCurrentVariant(item) && DependencyMet(item);
}
