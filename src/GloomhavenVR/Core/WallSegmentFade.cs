using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Live-tunable wall-see-through decision thresholds (canonical <see cref="ModuleConfig.Create"/>
/// pattern — <c>dev.gloomhavenvr.wallfade.cfg</c>). The fade DECISION constants that needed
/// hardware iteration every round (on/off view-coverage fractions and the two un-fade dwells)
/// are config entries now: the <see cref="WallSegmentFade"/> driver re-reads them through the
/// clamped accessors on EVERY evaluation tick, and the in-VR settings panel exposes them as
/// debug-menu steppers next to the Wall see-through toggle — so threshold tuning happens live
/// in the headset and persists (BepInEx saves on every entry write). The remaining constants
/// (EMA taus, sample-band geometry…) stay code-owned; they were stable across rounds.
/// </summary>
internal static class WallFadeTuning
{
    private static ConfigFile? _file;

    /// <summary>Smoothed view-coverage fraction at/above which a wall fades OUT (Schmitt high bar).</summary>
    internal static ConfigEntry<float>? OnFraction;
    /// <summary>Schmitt low bar: once faded, the wall stays faded while the fraction is at/above this.</summary>
    internal static ConfigEntry<float>? OffFraction;
    /// <summary>Seconds continuously below the low bar before un-fading after a recent perspective change.</summary>
    internal static ConfigEntry<float>? ExitDwellMoved;
    /// <summary>Un-fade dwell when the head only rotated (no recent translation/world-grab/recenter).</summary>
    internal static ConfigEntry<float>? ExitDwellStationary;
    /// <summary>Fort/keep superstructures: adopt plain meshes stacked on a tracked wall into that
    /// wall's fade (occlusion AABB + dissolve ride-along — see WallSegmentFade.Stacked.cs).</summary>
    internal static ConfigEntry<bool>? StackedShellFade;
    /// <summary>MP: also fade the walls a TEAMMATE's wall fade currently hides (wire record 17;
    /// receiver-side gate — own fades are always broadcast, see WallSegmentFade.Net.cs).</summary>
    internal static ConfigEntry<bool>? SyncPeerFades;
    /// <summary>ModBuild 259: a wall run carved into per-renderer pieces decides ONCE, on the
    /// union of its pieces' coverage (see WallSegmentFade.Inside.cs, WallRun).</summary>
    internal static ConfigEntry<bool>? SplitRunUnified;
    /// <summary>ModBuild 261: let a split-run piece that the standing-prop FLOOR arm refuses ride
    /// its run as a PASSENGER (see WallSegmentFade.cs, SplitPieceRefusalReason).</summary>
    internal static ConfigEntry<bool>? SplitRunAdoptGroundScenery;
    /// <summary>ModBuild 271: while the player has zoomed himself INTO the play field, every wall
    /// is held solid and nothing fades (see the record on WallSegmentFade.Inside.cs).</summary>
    internal static ConfigEntry<bool>? WalkInsideStandDown;
    /// <summary>ModBuild 271: the board's wall crest in REAL METRES that separates "standing in a
    /// room" from "leaning over a 61 cm diorama" — the term both retired attempts lacked.</summary>
    internal static ConfigEntry<float>? WalkInsideMinCrestMetres;
    /// <summary>ModBuild 272: hysteresis band under <see cref="WalkInsideMinCrestMetres"/>, as a
    /// fraction of it, so a zoom parked on the bar cannot make the mode chatter.</summary>
    internal static ConfigEntry<float>? WalkInsideCrestReleaseFraction;
    /// <summary>ModBuild 272: INSIDE Schmitt HIGH bar, as a fraction of the board's crest height.</summary>
    internal static ConfigEntry<float>? InsideEnterDepth;
    /// <summary>ModBuild 272: INSIDE Schmitt LOW bar, as a fraction of the board's crest height.</summary>
    internal static ConfigEntry<float>? InsideExitDepth;
    /// <summary>ModBuild 272: seconds the walk-in conjunction must hold before the mode engages.</summary>
    internal static ConfigEntry<float>? WalkInsideEnterDwell;
    /// <summary>ModBuild 272: seconds the walk-in conjunction must have failed before it releases.</summary>
    internal static ConfigEntry<float>? WalkInsideExitDwell;
    /// <summary>ModBuild 272: how far BELOW the crest plane the head must be, as a fraction of the
    /// crest height. 0 = the shipped rule, "anywhere under the crest plane".</summary>
    internal static ConfigEntry<float>? WalkInsideHeadBelowCrestFraction;
    /// <summary>ModBuild 278: seconds between two RESCAN CYCLES — the pipeline whose commit is
    /// the ~90 ms atomic frame. Was <c>FadeDriver.RescanIntervalSeconds</c>, a private const.</summary>
    internal static ConfigEntry<float>? RescanIntervalSecondsEntry;
    /// <summary>ModBuild 278: seconds between two fade DECISIONS (sample visibility + blocked
    /// fraction). The [WallFade] door onto what [Optimize] WallFadeEvalInterval already did.</summary>
    internal static ConfigEntry<float>? EvalIntervalSecondsEntry;
    /// <summary>ModBuild 278: suspend the decision, the coverage sampling and the rescan cadence
    /// while the walk-in stand-down holds every wall solid anyway.</summary>
    internal static ConfigEntry<bool>? WalkInSuspendSampling;
    /// <summary>ModBuild 278: name WHICH renderers moved the scene half of the skip signature on
    /// a cycle that refused to skip (see WallSegmentFadeCulprits.cs).</summary>
    internal static ConfigEntry<bool>? SignatureCulpritCensus;
    /// <summary>ModBuild 279 (Option A): let the skip signature stop listening to renderers the
    /// round-7 ruling puts beyond every adoption lane's reach. Ships OFF — see
    /// <see cref="FigureExemptSkipOn"/>.</summary>
    internal static ConfigEntry<bool>? FigureExemptSkip;
    /// <summary>ModBuild 281 (PERF B step 3): measure how much one commit CHURNS the wall table
    /// — the population the sliced commit's carry-forward has to survive. Read-only; see
    /// WallSegmentFade.CommitGate.cs.</summary>
    internal static ConfigEntry<bool>? CommitTableGate;
    /// <summary>ModBuild 281 (PERF B): the per-frame millisecond budget every SLICED stage of
    /// the rescan pipeline spends. Was three separate <c>private const float … = 1.5f</c>
    /// declarations that the source itself said were deliberately the same number.</summary>
    internal static ConfigEntry<float>? SliceBudgetMillis;
    /// <summary>One-shot marker, not a setting — see the migration block in <see cref="Bind"/>.</summary>
    internal static ConfigEntry<bool>? BarsMigrated252;
    /// <summary>One-shot marker, not a setting — see the second migration block in <see cref="Bind"/>.</summary>
    internal static ConfigEntry<bool>? BarsMigrated256;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("wallfade");
        OnFraction = config.Bind("WallFade", "OnFraction", Defaults.OnFraction,
            "Fade a wall when it hides at least this (EMA-smoothed) fraction of the frustum-visible " +
            "FLOOR (hex-tile plane) samples of some room — 0.25 = wall hides 25% of the floor you " +
            "are looking at (Schmitt trigger high bar). Live; clamped 0.05-0.95.");
        OffFraction = config.Bind("WallFade", "OffFraction", Defaults.OffFraction,
            "Once faded, keep the wall faded while the smoothed floor-coverage fraction stays at or " +
            "above this (Schmitt trigger low bar). Live; clamped 0.01-0.95 and never above OnFraction.");
        ExitDwellMoved = config.Bind("WallFade", "ExitDwellMovedSeconds", Defaults.ExitDwellMovedSeconds,
            "Seconds the fraction must stay below OffFraction before the wall un-fades when the " +
            "PERSPECTIVE recently changed (real head translation / world-grab / recenter). Live.");
        ExitDwellStationary = config.Bind("WallFade", "ExitDwellStationarySeconds", Defaults.ExitDwellStationarySeconds,
            "Un-fade dwell while the head has only ROTATED recently — rotation alone should almost " +
            "never bring a wall back. Live; never below ExitDwellMovedSeconds.");
        StackedShellFade = config.Bind("WallFade", "StackedShellFade", Defaults.StackedShellFade,
            "Fade fort/keep superstructures with their wall: meshes WITHOUT a fade shader that sit " +
            "stacked directly on a tracked wall run (battlements, upper stories) join that wall's " +
            "occlusion box and dissolve/reappear with its fade — without this, a multi-story keep " +
            "stays fully solid because only its bottom course is real wall geometry. OFF = vanilla " +
            "look for such shells. Live (applies at the next 2s rescan).");
        SyncPeerFades = config.Bind("WallFade", "SyncPeerFades", Defaults.SyncPeerFades,
            "Multiplayer: walls that fade for a TEAMMATE also fade for you (and reappear when " +
            "they do for them) — same animation as your own wall fades. Receiver-side setting: " +
            "your own fades are always broadcast (bytes are cheap), each player's toggle decides " +
            "only what THEY see, so toggling mid-session needs no renegotiation. Live.");
        SplitRunUnified = config.Bind("WallFade", "SplitRunUnified", Defaults.SplitRunUnified,
            "A wall run whose geometry rings its own room is tracked one renderer at a time (so " +
            "its union box can never be the occluder box). ON = those pieces still MEASURE " +
            "separately but DECIDE together, on the union of what they hide: the wall disappears " +
            "with all its trees, scrub and part-walls, or it is fully there. OFF = each piece " +
            "decides alone, which is the ModBuild 258 behaviour where single trunks vanished and " +
            "part-walls stayed. Unsplit walls are unaffected either way. Live (next evaluation).");
        SplitRunAdoptGroundScenery = config.Bind("WallFade", "SplitRunAdoptGroundScenery",
            Defaults.SplitRunAdoptGroundScenery,
            "The scrub, stones and low bushes standing along a split wall run carry the game's own " +
            "wall-fade material, but the mod refuses them as floor-standing props and they stay as " +
            "a hedge after the wall is gone (99 of 140 pieces of 'Wall 1' in the ModBuild 260 log). " +
            "ON = those pieces ride their run's fade as PASSENGERS: they disappear with the wall, " +
            "and they never contribute a single cell to the coverage that decides it, so WHEN a " +
            "wall fades is bit-for-bit unchanged. Figures/actors are still refused (the FIGURE arm " +
            "is untouched) and anything lying in the ground band is still stripped separately. " +
            "OFF (the default) = ModBuild 260 behaviour. BLUNT BY DESIGN: it moves the WHOLE " +
            "refused class, including low scenery the user has ruled may stay (a well, a low " +
            "stone formation) — turn it on only to see the hedge go, and read the SPLIT-RUN " +
            "LEFTOVER line's ALLOWED/FLOATING/OBSTRUCTING split for what it costs. " +
            "Live (applies at the next 2s rescan).");
        WalkInsideStandDown = config.Bind("WallFade", "WalkInStandDown", Defaults.WalkInStandDown,
            "When you zoom in far enough that you are STANDING INSIDE the play field — head " +
            "inside the board's footprint AND below the wall crests, on a board whose walls are " +
            "at least WalkInMinCrestMetres tall in real metres — a special mode engages in which " +
            "EVERY wall is held fully visible and nothing fades any more, for as long as you are " +
            "in there. Walls that were already faded come back through exactly the same animated " +
            "un-fade as always (nothing snaps), and while the mode holds neither a teammate's " +
            "synced fade nor a gate lift can hide a wall again. Step back out, or zoom out, and " +
            "every wall returns to its own coverage decision. OFF = walls keep fading around you " +
            "while you stand between them, which is the ModBuild 270 behaviour. Live (the very " +
            "next frame).");
        WalkInsideMinCrestMetres = config.Bind("WallFade", "WalkInMinCrestMetres",
            Defaults.WalkInMinCrestMetres,
            "How tall the board's walls have to be, in REAL METRES at your live zoom, before " +
            "'inside the play field' is allowed to mean it. This one number is the whole " +
            "safeguard: at a tabletop zoom the board is a diorama with 60 cm walls, so merely " +
            "LEANING OVER your own table already puts your head inside its volume — an earlier " +
            "build shipped a stand-down that fired on exactly that and was rejected in one " +
            "session. Standing between the walls of a room reads 1.6-1.9 m. WHAT YOUR OWN " +
            "HARDWARE HAS ACTUALLY READ (ModBuild 271 session, the crest in real metres at each " +
            "zoom you held): 2.31 m nine times and 1.42 m once — both above this bar, and the " +
            "mode engaged twice — then 0.94 m three times and 0.82 m twice, both under it, where " +
            "it refused. So the boundary you are moving sits between 0.94 and 1.42: set it near " +
            "0.90 to have the mode also cover the two shallower zooms, leave it at 1.20 to keep " +
            "them out. Raise it if the mode still engages when you only lean in; lower it if it " +
            "refuses while you are plainly standing inside. SET IT TO 0 TO SWITCH THE HEIGHT-BAR " +
            "TEST OFF ENTIRELY — the mode then fires on nothing but 'my head is inside the " +
            "board's footprint and under its crest', which at a tabletop zoom means LEANING OVER " +
            "YOUR OWN TABLE turns every wall solid. That exact behaviour shipped once and was " +
            "rejected in a single session; 0 is offered because it is your call, not because it " +
            "is a good default. The INSIDE THE MAP log line prints the live metre reading against " +
            "this bar every time, and names the term that refused when it did. The mode releases " +
            "only below WalkInCrestReleaseFraction x this value, so it cannot flicker on the " +
            "boundary. Live; clamped 0.00-5.00 (0 = test disabled).");
        WalkInsideCrestReleaseFraction = config.Bind("WallFade", "WalkInCrestReleaseFraction",
            Defaults.WalkInCrestReleaseFraction,
            "Hysteresis for the wall-height bar above: once the walk-in mode HOLDS, it keeps " +
            "holding until the board's crest falls below this fraction of WalkInMinCrestMetres. " +
            "At the shipped 0.85 with a 1.20 m bar the mode engages at 1.20 m and lets go at " +
            "1.02 m, so a zoom parked exactly on the bar cannot make every wall flicker solid " +
            "and transparent again. 1.00 removes the band entirely (engage and release on the " +
            "same number — expect chatter if you hover there); 0.30 makes the mode very sticky, " +
            "holding all walls solid until you have zoomed most of the way back out. Live; " +
            "clamped 0.10-1.00. Inert while WalkInMinCrestMetres is 0.");
        InsideEnterDepth = config.Bind("WallFade", "InsideEnterDepthFraction",
            Defaults.InsideEnterDepthFraction,
            "How far INSIDE the board's volume your head has to be before you count as being in " +
            "the play field, as a fraction of that board's own wall height (so it means the same " +
            "thing on a low ruin and on a keep — it is deliberately not a fixed distance). " +
            "0.10 = your head must be a tenth of a wall-height past the boundary. 0 = the moment " +
            "you touch the volume at all; 1.00 = a whole wall-height deep, which on most " +
            "scenarios you can never reach and effectively switches the whole mode off. Raise it " +
            "if the mode engages while you are still at the board edge. Live; clamped 0.00-2.00.");
        InsideExitDepth = config.Bind("WallFade", "InsideExitDepthFraction",
            Defaults.InsideExitDepthFraction,
            "The other half of the same Schmitt pair: how far OUTSIDE the board's volume your " +
            "head has to travel before you stop counting as being in the play field, again as a " +
            "fraction of that board's wall height. The gap between this and " +
            "InsideEnterDepthFraction is the dead band your head has to cross to flip the verdict " +
            "back — at the shipped 0.10/0.35 that band is 0.45 wall-heights wide. Lower it " +
            "towards 0 and the mode drops the instant you drift out (and can re-engage a moment " +
            "later — chatter); raise it towards 1.00 and you can lean well clear of the board " +
            "with every wall still held solid. Live; clamped 0.00-3.00.");
        WalkInsideEnterDwell = config.Bind("WallFade", "WalkInEnterDwellSeconds",
            Defaults.WalkInEnterDwellSeconds,
            "Seconds every condition of the walk-in mode must hold TOGETHER before it actually " +
            "engages. Short by design (0.20 s shipped): stepping into the field is a deliberate " +
            "act and the walls should be solid by the time you have looked up. Raise it to 1-2 s " +
            "if a zoom that merely passes through the field flips the walls on in passing; 0 = " +
            "engage on the very first frame that qualifies. Live; clamped 0.00-10.00.");
        WalkInsideExitDwell = config.Bind("WallFade", "WalkInExitDwellSeconds",
            Defaults.WalkInExitDwellSeconds,
            "Seconds the walk-in mode waits, after the conditions stop being met, before it lets " +
            "the walls fade again. Long by design (2.50 s shipped, the same value the ordinary " +
            "un-fade dwell uses): a wall going transparent because your head drifted a centimetre " +
            "over the boundary is exactly the churn this mode exists to stop. Raise it to 5-10 s " +
            "to make leaving very forgiving; 0 = the walls are free to fade again the frame you " +
            "step out. NOTE: turning the mode OFF at WalkInStandDown, or losing the board volume " +
            "on a scene change, always releases immediately — neither is a moving head, so " +
            "neither is what this dwell debounces. Live; clamped 0.00-60.00.");
        WalkInsideHeadBelowCrestFraction = config.Bind("WallFade", "WalkInHeadBelowCrestFraction",
            Defaults.WalkInHeadBelowCrestFraction,
            "How far BELOW the wall crests your head must be for the walk-in mode, as a fraction " +
            "of the board's wall height. 0 (shipped) means the rule is simply 'below the crest " +
            "plane' — anywhere under the tops of the walls counts, which is what standing in a " +
            "room means. Raise it to demand that you are genuinely DOWN among the walls rather " +
            "than at eye level with their tops: 0.25 = a quarter of a wall-height below the " +
            "crest, 0.50 = half way down. Useful if the mode engages while you are still looking " +
            "over the walls from just inside the footprint. Too high and it can never engage at " +
            "all, because your eye would have to be near the floor. Live; clamped 0.00-1.00.");

        RescanIntervalSecondsEntry = config.Bind("WallFade", "RescanIntervalSeconds",
            Defaults.RescanIntervalSeconds,
            "How often the mod REBUILDS its table of which renderers belong to which wall — the "
            + "sweep/classify/survey pipeline whose last step is one atomic frame. THIS IS THE "
            + "SETTING BEHIND THE SHORT HITCHES: on the ModBuild 277 hardware log that final "
            + "frame measured 85.6 ms on average and 134.0 ms at worst, 33 times in the "
            + "session. Raising this number divides HOW MANY of those frames happen and makes "
            + "not one of them shorter — at 4.0 you get half as many, at 8.0 a quarter. WHAT IT "
            + "COSTS: decision latency. The table in force is what every fade is decided "
            + "against, so a wall that has just been built, revealed or regenerated waits up to "
            + "this long before it can fade or reappear at all. A ROOM REVEAL IS NOT AFFECTED — "
            + "that opens a cycle immediately whatever this says, as does any wall the game "
            + "regenerates mid-fade. The [WallSegmentFade] BUDGET line prints 'DECISION LATENCY "
            + "— the table in force stood at most X s without a rebuild', which is the real "
            + "number to read this against. NOTE that most cycles already SKIP the expensive "
            + "frame entirely (80 of 113 in that same log), so raising this thins out the ones "
            + "that are left rather than removing a fixed cost. Live; clamped 0.50-15.00.");
        EvalIntervalSecondsEntry = config.Bind("WallFade", "EvalIntervalSeconds",
            Defaults.EvalIntervalSeconds,
            "How often the mod CHECKS whether a wall is hiding the floor you are looking at — "
            + "the per-frame half: it projects every room's floor samples through your head "
            + "camera and re-measures every wall against them. 0 = every single frame, which is "
            + "what has shipped so far. This is the cadence 'wie oft gecheckt wird ob eine Wand "
            + "etwas verdeckt' in the literal sense; it is NOT what causes the short hitches "
            + "(that is RescanIntervalSeconds above), it is a small steady cost paid on every "
            + "frame forever. RAISING IT IS SAFE UP TO A POINT AND THE POINT IS KNOWN: the "
            + "decision this feeds is already deliberately slow — an EMA over the coverage, a "
            + "Schmitt trigger with two separate bars, and dwell timers of 0.20 s before a "
            + "wall may go transparent and 2.50-7.00 s before it may come back — and the "
            + "smoothing advances by the time since the last CHECK rather than per frame, so "
            + "this dial does not stretch it. The shortest thing it can distort is that 0.20 s "
            + "dwell: at or above 0.20 the dwell stops debouncing anything, because one check "
            + "arms it and the very next check already satisfies it. Stay well under that — "
            + "0.05 (20 Hz) still needs four checks in a row to agree before a wall goes "
            + "transparent and delays that by at most a tenth of a second, which is inside the "
            + "fade animation's own smear and cannot be seen. IF THIS IS 0, the older "
            + "[Optimize] WallFadeEvalInterval in dev.gloomhavenvr.perf.cfg still applies; any "
            + "non-zero value here overrides it. Live; clamped 0.00-0.25.");
        WalkInSuspendSampling = config.Bind("WallFade", "WalkInSuspendSampling",
            Defaults.WalkInSuspendSampling,
            "While you are standing INSIDE the play field (see 'Im Spielfeld: alle Wände "
            + "massiv'), stop measuring the walls altogether instead of measuring them and "
            + "throwing the answer away. In that mode every wall is held fully solid by decree, "
            + "so the coverage check, the fade decision and the periodic table rebuild are all "
            + "computing a verdict that the very next line of code overrules — this switch just "
            + "stops paying for it, which is the frame time back for free while you are down "
            + "among the walls. NOTHING IS BROKEN BY IT: a rebuild already in flight is allowed "
            + "to finish rather than being torn up mid-way, a room the game reveals while you "
            + "are in there still triggers one immediately, and the moment you step or zoom out "
            + "the very next frame resumes both the measuring and the rebuild — no waiting out "
            + "a skipped cadence. Multiplayer is unaffected: what your teammates see is decided "
            + "on THEIR machines, and a fade you had before you walked in is still broadcast. "
            + "OFF = keep measuring while the mode holds, which is the ModBuild 277 behaviour. "
            + "Live (the very next frame).");
        SignatureCulpritCensus = config.Bind("WallFade", "SignatureCulpritCensus",
            Defaults.SignatureCulpritCensus,
            "DIAGNOSTIC, not a behaviour. When the mod decides it has to rebuild its wall table "
            + "because 'the scene changed', print WHICH renderers changed — grouped by name, "
            + "with the full group count and an explicit count of anything the line did not "
            + "have room for. THE QUESTION IT WAS BUILT FOR HAS BEEN ANSWERED, WHICH IS WHY IT "
            + "IS NOW OFF BY DEFAULT: 28 of the 33 rebuilds in the ModBuild 277 log fired on "
            + "that one term, and this line named the cause — a third of them were triggered by "
            + "nothing but the mod's OWN objects, the hand laser and the pointer, each of which "
            + "costs a full rebuild it could never affect. Switch it back on if you are working "
            + "on that. It runs only on a cycle that is already going to rebuild, at most once "
            + "every few seconds, and reports its own cost as the step 'WallFade.SigDiag' so it "
            + "can never become an unmeasured tax. Live.");
        FigureExemptSkip = config.Bind("WallFade", "FigureExemptSkip",
            Defaults.FigureExemptSkip,
            "EXPERIMENTAL, OFF BY DEFAULT, AND MEASURED EITHER WAY. Heroes, monsters and their "
            + "effects move constantly, and every time one of them appears, dies or is switched "
            + "on the mod decides its wall table might have changed and rebuilds the whole thing "
            + "— a rebuild that is measured at about 95 milliseconds, which is what a short "
            + "stutter is made of. This switch lets the mod ignore figures when it asks 'did "
            + "anything change', because no wall system is ever allowed to touch a figure "
            + "anyway. IT DOES NOT MAKE A REBUILD FASTER — it makes rebuilds rarer, which is not "
            + "the same thing and is not what was asked for. IT IS OFF BECAUSE ONE PATH IN THE "
            + "MOD STILL READS A FIGURE'S ON/OFF STATE while deciding a whole prop group's fate, "
            + "and if that path ever fires the mod could miss a real change and leave a wall "
            + "solid for up to thirty rescans. WITH IT OFF, THIS BUILD STILL MEASURES IT: the "
            + "log's FIGURE EXEMPTION clause reports how many rebuilds it WOULD have saved and "
            + "names the renderers it would have stopped listening to, so the decision to switch "
            + "it on can be made from a real session instead of from an argument. Live.");

        CommitTableGate = config.Bind("WallFade", "CommitTableGate", Defaults.CommitTableGate,
            "DIAGNOSTIC, not a behaviour — it reads the wall table and writes nothing. Each time "
            + "the mod rebuilds its wall table it takes a copy before and after and reports what "
            + "CHANGED: walls that disappeared from the table while they were still half-faded, "
            + "walls whose set of meshes changed, and meshes that moved from one wall to "
            + "another. That is the exact list of things that would go wrong if the rebuild were "
            + "spread over sixty frames instead of happening in one — which is the change that "
            + "would remove the short stutters for good. THAT MEASUREMENT HAS BEEN TAKEN AND "
            + "THAT CHANGE IS NOT BEING MADE — you reported the stutters gone, so this is OFF "
            + "by default now and only worth switching on if the spread-out rebuild is ever "
            + "picked up again. It is NOT a comparison of the current rebuild against a "
            + "spread-out one; nothing in this build performs a spread-out rebuild, and the log "
            + "line says so itself. Runs only on a cycle that is already rebuilding, prints at "
            + "most every 20 seconds, and reports its own cost as the step "
            + "'WallFade.TableGate'. Live.");
        SliceBudgetMillis = config.Bind("WallFade", "SliceBudgetMillis",
            Defaults.SliceBudgetMillis,
            "How many milliseconds per frame the mod may spend on the SPREAD-OUT half of its "
            + "wall work — classifying the scene's renderers, surveying them and warming the "
            + "table. At 90 Hz a frame is 11.11 ms, so the shipped 1.5 leaves the frame intact "
            + "and the job simply takes more frames (about 12-18 of them). Raise it to finish a "
            + "rebuild sooner at the cost of a fuller frame; lower it if you see the mod itself "
            + "in a frame-time graph. This does NOT bound the one big rebuild frame that causes "
            + "the short stutters — that step is still atomic, and making it obey this number is "
            + "the next piece of work. Live; clamped 0.25-8.0.");

        // ---- ONE-SHOT: carry the corrected Schmitt pair into an EXISTING cfg ---------------
        //
        // WHY IT IS NEEDED AT ALL. BepInEx keeps a value that is already present in the cfg, so
        // fixing Defaults.OnFraction/OffFraction only reaches a FRESH install. Every existing
        // install would keep On 0.10 / Off 0.20, Off would be clamped back below On, and the
        // degenerate single-bar trigger — and the group churn it causes — would survive the very
        // build that fixes it.
        //
        // WHY IT IS SAFE TO OVERWRITE. The trigger is not "the value differs from the new
        // default", it is the one shape that CANNOT be a deliberate tuning: Off >= On. A Schmitt
        // trigger is defined by a band between a high bar and a low one; a low bar at or above
        // the high bar leaves no band at all, so the whole hysteresis mechanism is inoperative.
        // Nobody tunes their way to that on purpose, and the shipped pair reached it by having
        // the two constants transposed. A user who set some other SANE pair (Off < On) — however
        // far from the default — is left completely alone.
        //
        // WHY IT WRITES THE DEFAULTS RATHER THAN SWAPPING HIS TWO NUMBERS. Swapping would give
        // 0.20/0.10 and would look more conservative, but it would leave a migrated install
        // permanently different from a fresh one, and every later bug report would then depend on
        // which install it came from. Adopting the shipped pair makes the two identical.
        //
        // WHY IT CAN ONLY HAPPEN ONCE. The marker is written true BEFORE anything else, whatever
        // the outcome of the test — so even a fresh install (where the pair is already sane and
        // nothing is rewritten) burns the one-shot. After this, both bars are ordinary live
        // settings for ever: tune them to anything, including a degenerate pair, and they stay
        // exactly as tuned. Setting the marker back to false by hand re-arms it.
        BarsMigrated252 = config.Bind("WallFade", "WallFadeBarsMigrated252",
            Defaults.WallFadeBarsMigrated252,
            "One-shot migration marker, not a setting. FALSE on a fresh install; set TRUE the "
            + "first time this build inspects an existing config. If OnFraction/OffFraction were "
            + "found in the impossible order (OffFraction at or above OnFraction — no Schmitt "
            + "band at all, which is how they shipped transposed), they are rewritten to the "
            + "corrected defaults at the same moment. Afterwards both bars are ordinary settings "
            + "again and any value you tune is kept for ever. Set this back to false to re-run.");
        if (BarsMigrated252 != null && !BarsMigrated252.Value)
        {
            BarsMigrated252.Value = true;
            float hadOn = OnFraction != null ? OnFraction.Value : Defaults.OnFraction;
            float hadOff = OffFraction != null ? OffFraction.Value : Defaults.OffFraction;
            bool degenerate = hadOff >= hadOn;
            if (degenerate && OnFraction != null && OffFraction != null)
            {
                OnFraction.Value = Defaults.OnFraction;
                OffFraction.Value = Defaults.OffFraction;
            }
            VRLog.Info("WallSegmentFade", degenerate
                ? $"One-shot migration: this config carried [WallFade] OnFraction {hadOn:F2} with "
                  + $"OffFraction {hadOff:F2} — the low bar at or above the high bar, which is no "
                  + "Schmitt band at all and is how the two shipped defaults were transposed. The "
                  + "effective pair was therefore a single shared threshold, and every wall's "
                  + "coverage crossed it at the same moment (the group churn reported 2026-08-24: "
                  + "'entweder alle grünen Wände verschwinden auf einmal, oder alle sind da'). "
                  + $"Rewritten to the corrected defaults {Defaults.OnFraction:F2}/"
                  + $"{Defaults.OffFraction:F2}, which is exactly what a fresh install now gets. "
                  + "This runs ONCE — both bars are ordinary settings from here on and anything "
                  + "you tune is kept."
                : $"One-shot migration: nothing to do — [WallFade] OnFraction {hadOn:F2} / "
                  + $"OffFraction {hadOff:F2} already form a valid Schmitt band (low bar below "
                  + "high bar), so they were left exactly as they are. The marker is now spent "
                  + "and these bars will never be rewritten again.");
        }

        // ---- ONE-SHOT: move the band into the gap the hardware actually shows -------------
        //
        // WHY A SECOND MARKER. The 252 one-shot is SPENT on every install that has run that
        // build: it wrote 0.25/0.10 and set itself true. Correcting the two constants again
        // would therefore reach nobody who is already testing — the same reach problem, one
        // build later.
        //
        // WHY THIS OVERWRITE IS NARROW. It fires ONLY on the exact pair the 252 one-shot itself
        // wrote. A value tuned since then — by hand or by the settings-panel steppers — does not
        // match and is left alone. This is not "the value differs from the new default"; it is
        // "the value is provably still the one a previous migration put there".
        //
        // WHY 0.35/0.20. Not taste, and not raising a bar until a symptom stops: the ModBuild
        // 255 log's coverage distributions are bimodal per wall, with NOTHING between 0.25 and
        // 0.44. The old pair straddled the wrong side of that gap — enter 0.25 is exactly a
        // resting wall's reading, so it latched on sight, and exit 0.10 is unreachable on a
        // 16-cell grid whose quantum is 0.0625. Both new bars sit inside the empty gap.
        BarsMigrated256 = config.Bind("WallFade", "WallFadeBarsMigrated256",
            Defaults.WallFadeBarsMigrated256,
            "One-shot migration marker, not a setting. FALSE on a fresh install; set TRUE the "
            + "first time this build inspects an existing config. If OnFraction/OffFraction are "
            + "still exactly the 0.25/0.10 pair the ModBuild 252 migration wrote, they are moved "
            + "to 0.35/0.20 — the band the hardware distributions put between a wall at rest and "
            + "a wall genuinely in the way. Anything you have tuned since is left untouched. Set "
            + "this back to false to re-run.");
        if (BarsMigrated256 != null && !BarsMigrated256.Value)
        {
            BarsMigrated256.Value = true;
            float hadOn = OnFraction != null ? OnFraction.Value : Defaults.OnFraction;
            float hadOff = OffFraction != null ? OffFraction.Value : Defaults.OffFraction;
            bool isLegacy252Pair = Mathf.Abs(hadOn - 0.25f) < 0.001f
                                   && Mathf.Abs(hadOff - 0.10f) < 0.001f;
            if (isLegacy252Pair && OnFraction != null && OffFraction != null)
            {
                OnFraction.Value = Defaults.OnFraction;
                OffFraction.Value = Defaults.OffFraction;
            }
            VRLog.Info("WallSegmentFade", isLegacy252Pair
                ? $"One-shot migration: this config still carried the ModBuild 252 pair "
                  + $"[WallFade] OnFraction {hadOn:F2} / OffFraction {hadOff:F2}. On a 16-cell "
                  + "floor grid the quantum is 0.0625, so an exit bar of 0.10 means 'at most ONE "
                  + "blocked cell' — a wall resting at two cells (0.125) could never release, "
                  + "and an enter bar of 0.25 is exactly what a wall at rest reads, so it "
                  + "latched on sight. That is the one-way ratchet reported 2026-08-24 ('einmal "
                  + "ausgeblendet ist es super schwer sie wieder einzublenden, egal welche "
                  + $"Position ich einnehme'). Moved to {Defaults.OnFraction:F2}/"
                  + $"{Defaults.OffFraction:F2}, which sits in the empty gap between the two "
                  + "modes every wall's coverage actually shows (at rest 0.13-0.25, in the way "
                  + "0.44-0.95). This runs ONCE and anything you tune from here is kept."
                : $"One-shot migration: nothing to do — [WallFade] OnFraction {hadOn:F2} / "
                  + $"OffFraction {hadOff:F2} is not the pair the ModBuild 252 migration wrote, "
                  + "so it is a tuned value and was left exactly as it is. The marker is now "
                  + "spent and these bars will never be rewritten again.");
        }
    }

    // Clamped live accessors — safe before Bind() (fall back to the shipped defaults).
    internal static float On => Clamped(OnFraction, 0.25f, 0.05f, 0.95f);

    /// <summary>
    /// Fraction of <see cref="On"/> the low bar falls back to when the configured pair cannot
    /// form a Schmitt band at all. 0.6 keeps a band wide enough to survive the EMA's own jitter
    /// (tau 0.15 s) without making a wall that genuinely stopped occluding wait for a near-zero
    /// reading before it comes back.
    /// </summary>
    private const float DegenerateBandFallback = 0.6f;

    /// <summary>
    /// Schmitt LOW bar. A Schmitt trigger needs <c>Off &lt; On</c>; a pair where the configured
    /// low bar is at or above the high bar has no band at all and the trigger degenerates into a
    /// single threshold that a wall's coverage crosses back and forth on EMA noise.
    ///
    /// <para>THE SHIPPED DEFAULTS WERE SUCH A PAIR FROM THE STEPPERS LANDING UNTIL ModBuild 252,
    /// WHICH CORRECTED THEM AT SOURCE TO 0.25/0.10 — so this clause is now a GUARD against a
    /// hand-written cfg, not a description of what the mod ships. Read the rest as history.
    /// <c>Defaults.OnFraction = 0.1f</c> with <c>Defaults.OffFraction = 0.2f</c> — the low bar was
    /// authored ABOVE the high bar, which reads like the two constants were transposed. The old
    /// accessor clamped it with <c>Min(Off, On)</c>, so both bars became 0.10 and the log said so
    /// on every heartbeat and every BOARD VOLUME line — <c>on ≥0.10, off &lt;0.10</c>,
    /// <c>the live 0.10/0.10</c> — for anyone who read it as a pair rather than as two numbers.
    /// That is the mechanism behind the group churn in the ModBuild 250 hardware log, where all
    /// four walls flip OFF together and ON together at lines 9471/9477, 10856/10870, 11746/11751,
    /// 12660/12667 and 12706/12709: four coverages sitting near one shared bar with no band
    /// between them.</para>
    ///
    /// <para>A CONFIGURED low bar BELOW the high bar is honoured exactly as written — this only
    /// repairs the degenerate case. ModBuild 252 fixed the two defaults to a real pair AND added
    /// the one-shot <c>WallFadeBarsMigrated252</c> above, which repairs an EXISTING cfg that
    /// still holds the transposed pair (a changed default never reaches a cfg BepInEx has already
    /// written). So on a fresh or migrated install this branch is unreachable and that is the
    /// intended end state; it survives only for a cfg somebody edits into the impossible order by
    /// hand.</para>
    /// </summary>
    internal static float Off
    {
        get
        {
            float on = On;
            float configured = Clamped(OffFraction, 0.10f, 0.01f, 0.95f);
            return configured < on ? configured : on * DegenerateBandFallback;
        }
    }
    internal static float DwellMoved => Clamped(ExitDwellMoved, 2.5f, 0.1f, 60f);
    internal static float DwellStationary =>
        Mathf.Max(Clamped(ExitDwellStationary, 7f, 0.1f, 120f), DwellMoved);
    internal static bool StackedShells => StackedShellFade == null || StackedShellFade.Value;
    internal static bool SyncPeer => SyncPeerFades == null || SyncPeerFades.Value;
    /// <summary>ModBuild 259 kill switch — printed live on the SPLIT RUN line, because a remedy
    /// that silently did not run has cost this project a whole build before.</summary>
    internal static bool SplitRunUnifiedOn => SplitRunUnified == null || SplitRunUnified.Value;
    /// <summary>ModBuild 261 — shipped OFF, and printed live on the SPLIT RUN line for the same
    /// reason as the one above: a remedy that silently did not run has cost this project a build.</summary>
    internal static bool AdoptGroundScenery =>
        SplitRunAdoptGroundScenery != null && SplitRunAdoptGroundScenery.Value;
    /// <summary>ModBuild 271 kill switch ("Das soll in Erweitert deaktivierbar sein"). Read live
    /// every frame and printed live on the INSIDE THE MAP line, because a remedy that silently
    /// did not run has cost this project a whole build before.</summary>
    internal static bool WalkInStandDown =>
        WalkInsideStandDown == null || WalkInsideStandDown.Value;
    /// <summary>ModBuild 271 — the real-metre crest bar. The number inside Clamped() is only the
    /// PRE-BIND fallback; the shipped default is <c>Defaults.WalkInMinCrestMetres</c>.
    /// <para>ModBuild 272 widened the LOWER clamp from 0.30 to 0 so the term can be switched off
    /// outright (see the bound description). The DEFAULT is untouched at 1.20, so this changes
    /// nothing for anyone who does not type a smaller number on purpose.</para></summary>
    internal static float WalkInMinCrestMetres =>
        Clamped(WalkInsideMinCrestMetres, 1.2f, 0f, 5f);
    /// <summary>ModBuild 272 — release band under <see cref="WalkInMinCrestMetres"/>. The number
    /// inside Clamped() is only the PRE-BIND fallback; the shipped default is
    /// <c>Defaults.WalkInCrestReleaseFraction</c>. The two must stay equal.</summary>
    internal static float WalkInCrestReleaseFraction =>
        Clamped(WalkInsideCrestReleaseFraction, 0.85f, 0.10f, 1f);
    /// <summary>ModBuild 272 — INSIDE Schmitt HIGH bar as a fraction of the crest height. The
    /// number inside Clamped() is only the PRE-BIND fallback; the shipped default is
    /// <c>Defaults.InsideEnterDepthFraction</c>. The two must stay equal.</summary>
    internal static float InsideEnterDepthFraction =>
        Clamped(InsideEnterDepth, 0.10f, 0f, 2f);
    /// <summary>ModBuild 272 — INSIDE Schmitt LOW bar as a fraction of the crest height. The
    /// number inside Clamped() is only the PRE-BIND fallback; the shipped default is
    /// <c>Defaults.InsideExitDepthFraction</c>. The two must stay equal.
    /// <para>NOT forced above <see cref="InsideEnterDepthFraction"/>. The two bars measure from
    /// OPPOSITE sides of the same box — enter is a depth INSIDE it, exit a distance OUTSIDE it —
    /// so unlike the coverage pair there is no order between them that can degenerate: any
    /// non-negative pair leaves a band of (enter + exit) crest-heights, and only 0/0 collapses
    /// it to the box surface itself. Clamping one against the other here would forbid perfectly
    /// sane tunings such as "enter deep, leave the moment I am out".</para></summary>
    internal static float InsideExitDepthFraction =>
        Clamped(InsideExitDepth, 0.35f, 0f, 3f);
    /// <summary>ModBuild 272 — walk-in ENTER dwell. The number inside Clamped() is only the
    /// PRE-BIND fallback; the shipped default is <c>Defaults.WalkInEnterDwellSeconds</c>, which
    /// equals the <c>EnterDwellSeconds</c> constant the latch used before it had its own dial.</summary>
    internal static float WalkInEnterDwellSeconds =>
        Clamped(WalkInsideEnterDwell, 0.20f, 0f, 10f);
    /// <summary>ModBuild 272 — walk-in EXIT dwell. The number inside Clamped() is only the
    /// PRE-BIND fallback; the shipped default is <c>Defaults.WalkInExitDwellSeconds</c>, which
    /// equals <c>Defaults.ExitDwellMovedSeconds</c> — the value the latch borrowed from
    /// <see cref="DwellMoved"/> before it had its own dial.</summary>
    internal static float WalkInExitDwellSeconds =>
        Clamped(WalkInsideExitDwell, 2.5f, 0f, 60f);
    /// <summary>ModBuild 272 — how far below the crest plane the head must be, as a fraction of
    /// the crest height. The number inside Clamped() is only the PRE-BIND fallback; the shipped
    /// default is <c>Defaults.WalkInHeadBelowCrestFraction</c>, and it is 0 precisely so the test
    /// stays the shipped <c>margin &lt; 0</c>. The two must stay equal.</summary>
    internal static float WalkInHeadBelowCrestFraction =>
        Clamped(WalkInsideHeadBelowCrestFraction, 0f, 0f, 1f);

    /// <summary>
    /// ModBuild 278 — seconds between two RESCAN CYCLES. The number inside Clamped() is only the
    /// PRE-BIND fallback; the shipped default is <c>Defaults.RescanIntervalSeconds</c>, and the
    /// two must stay equal (this project has lost two rounds to reading the fallback as the
    /// shipped default — see the ledger entry "A clamp fallback is not a default").
    ///
    /// <para>THE LOWER CLAMP IS 0.5 AND IT IS A SAFETY BAR, not taste. The cycle's own stages are
    /// budgeted per frame (census 1.5 ms, survey 1.5 ms, prepare 1.5 ms) and a scene of ~5800
    /// renderers needs on the order of 45-50 frames to walk them all. Below about half a second
    /// at 90 Hz the next cycle would be due before the previous one finished, so the pipeline
    /// would never be idle and <c>_rescanStage</c> would never return to Idle — which also
    /// starves the wall-path audit, whose whole gate is "only while the pipeline is idle". 0.5 s
    /// is 45 frames, i.e. exactly at that boundary with the budgets as they ship.</para>
    ///
    /// <para>THE UPPER CLAMP IS 15 s because that is where the STALENESS CEILING starts to be
    /// the binding term instead of this one: the ceiling forces one commit after 30 skipped
    /// cycles, so at 15 s the fail-safe is 7.5 minutes away and the table's own decision latency
    /// (a newly generated wall cannot fade for up to 15 s) is already far past what anyone would
    /// accept. A number beyond that is not a tuning, it is switching the rebuild off.</para>
    /// </summary>
    internal static float RescanIntervalSeconds =>
        Clamped(RescanIntervalSecondsEntry, 2f, 0.5f, 15f);

    /// <summary>
    /// ModBuild 278 — seconds between two fade DECISIONS. The number inside Clamped() is only the
    /// PRE-BIND fallback; the shipped default is <c>Defaults.EvalIntervalSeconds</c>.
    ///
    /// <para>0 HERE MEANS "NOT SET HERE", NOT "EVERY FRAME" — see
    /// <see cref="EffectiveEvalIntervalSeconds"/>. The distinction exists so that surfacing this
    /// dial cannot silently discard a value a tester already typed into
    /// <c>[Optimize] WallFadeEvalInterval</c>, which is the only door this cadence had before
    /// today and which several perf captures were taken with.</para>
    ///
    /// <para>THE UPPER CLAMP IS 0.25 s, matching the [Optimize] entry it fronts. It is above the
    /// 0.20 s fade-in dwell on purpose: past that bar the dwell stops debouncing (one evaluation
    /// arms it, the next satisfies it) and the description says so in as many words. Forbidding
    /// it would be a clamp pretending to be advice; the honest arrangement is a bound that stops
    /// where the [Optimize] twin stops, plus a description that names the number at which the
    /// behaviour changes and why.</para>
    /// </summary>
    internal static float EvalIntervalSeconds =>
        Clamped(EvalIntervalSecondsEntry, 0f, 0f, 0.25f);

    /// <summary>
    /// The decision cadence actually in force: this section's dial when it is set, and the older
    /// <c>[Optimize] WallFadeEvalInterval</c> otherwise.
    ///
    /// <para>WHY NOT SIMPLY MOVE THE ENTRY. BepInEx keeps whatever is already written in a cfg,
    /// so deleting the [Optimize] key would strand every value typed into it and moving the
    /// default across would strand it silently — the reader would keep working and quietly read
    /// 0. WHY NOT max() OR min() OF THE TWO: both make two independent dials interact in a way
    /// neither description can state, and "I set it to 0.05 and nothing happened because another
    /// file said 0.10" is a bug report nobody can diagnose from a headset. A precedence with one
    /// sentinel is the only shape whose behaviour fits in a sentence, and that sentence is in
    /// both descriptions.</para>
    /// </summary>
    internal static float EffectiveEvalIntervalSeconds
    {
        get
        {
            float own = EvalIntervalSeconds;
            return own > 0f ? own : PerfConfig.WallFadeInterval;
        }
    }

    /// <summary>ModBuild 278 kill switch for the walk-in suspension. Read live every frame and
    /// printed live on the suspension's own edge line, because a remedy that silently did not
    /// run has cost this project a whole build before.</summary>
    internal static bool WalkInSuspendSamplingOn =>
        WalkInSuspendSampling == null || WalkInSuspendSampling.Value;

    /// <summary>ModBuild 278 — the WHICH-RENDERERS census (WallSegmentFadeCulprits). Defaults ON
    /// while unbound: an instrument that is off in the capture that was supposed to answer the
    /// question is a wasted round, and this project has had three of those.</summary>
    internal static bool SignatureCulpritCensusOn =>
        SignatureCulpritCensus == null || SignatureCulpritCensus.Value;

    /// <summary>
    /// ModBuild 279 (Option A) — MAY THE SKIP SIGNATURE STOP LISTENING TO FIGURES?
    ///
    /// <para><b>DEFAULTS OFF, AND THE DEFAULT IS THE DECISION.</b> The narrowed signature, its
    /// shadow comparison, its three counters and the culprit naming are ALL ON regardless: this
    /// build MEASURES the narrowing on his hardware. What the dial gates is whether the narrowed
    /// half is allowed to DECIDE.</para>
    ///
    /// <para><b>WHY OFF, stated as the evidence rather than as caution.</b> The design this came
    /// from rests on one claim: that <c>PurgeFigureRenderers</c> is phase 1 of the commit and
    /// therefore "a figure moving, being highlighted, or being toggled provably cannot change the
    /// commit's output". Read against the source, that claim is false twice over.
    /// <c>PurgeFigureRenderers</c> is RESTITUTION, not prevention — it walks
    /// <c>_mountedTouched</c> only, and it EXEMPTS <c>IsWallGeneratedDressing</c>, so a
    /// wall-generated cloth post that the figure guard classifies as a figure legitimately stays
    /// in the table (this predicate's own term excludes those, so that half is covered). The one
    /// that is not covered: <c>WallSegmentFade.PropUnit.cs</c> :1212 asks
    /// <c>IsActuallyDrawing</c> — <c>enabled</c> AND <c>activeInHierarchy</c> — of every
    /// prop-unit MEMBER, and it asks it FIVE LINES BEFORE the figure refusal at :1267. So a
    /// figure that is a member of a prop unit decides, by its liveness alone, whether that whole
    /// unit is refused. That path is recorded as firing ZERO times in the ModBuild-261 session,
    /// which is a good reason to expect the narrowing to be safe and not a reason to assert
    /// it.</para>
    ///
    /// <para><b>WHAT A WRONG SKIP COSTS, which is why the asymmetry matters.</b> The backstop is
    /// the 30-cycle staleness ceiling, so the worst case is bounded — but bounded at thirty
    /// rescan cadences of a wall that should have opened and did not, which is
    /// "Wanddurchsicht hört auf zu funktionieren", indistinguishable from the mod being switched
    /// off, and a thing the user has ruled must never happen. Against that, the upside of turning
    /// it on one build earlier is a hitch rate he has already said out loud is not what he asked
    /// for: <i>"Ich will aber eigentlich gar keine spürbaren Ruckler - nicht nur seltenere."</i>
    /// One observe-only build costs a hardware session that is being run anyway for PERF E's
    /// numbers; a wrong skip costs the picture.</para>
    ///
    /// <para><b>WHAT TURNS IT ON.</b> The next log's FIGURE EXEMPTION clause and the SIGNATURE
    /// CULPRITS lines beside it. If <c>_narrowOnlyUnexplained</c> is 0, and the renderers named
    /// as moving the full signature on the counted cycles carry the FIGURE bit, the dial is a
    /// one-line change with the evidence behind it.</para>
    /// </summary>
    internal static bool FigureExemptSkipOn =>
        FigureExemptSkip != null && FigureExemptSkip.Value;

    /// <summary>ModBuild 281 — the churn gate. Defaults ON when unbound, exactly like
    /// <see cref="SignatureCulpritCensus"/> beside it: the reason this build exists is to
    /// produce that line, and an instrument that silently does not run because Bind has not
    /// happened yet is the "gated remedy never ran" entry in this project's ledger.</summary>
    internal static bool CommitTableGateOn => CommitTableGate == null || CommitTableGate.Value;

    /// <summary>ModBuild 281 — the per-frame budget of every sliced rescan stage.
    ///
    /// <para>THE NUMBER INSIDE <c>Clamped()</c> IS THE PRE-BIND FALLBACK, NOT THE SHIPPED
    /// DEFAULT. The shipped default is <c>Defaults.SliceBudgetMillis</c>, and confusing the two
    /// has cost this project two rounds, twice. They agree at 1.5 and must be changed
    /// together.</para></summary>
    internal static float SliceBudget => Clamped(SliceBudgetMillis, 1.5f, 0.25f, 8f);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);
}

/// <summary>
/// ISSUE #4 round 3 (wall see-through, VR redesign) — WHOLE-WALL fade with temporal hysteresis.
///
/// HARDWARE VERDICT on round 2 (per-pixel head-camera occlusion feed, removed with this class's
/// predecessor <c>WallFadeOcclusionFeed</c>): unusable in VR. The game's fade is a SCREEN-SPACE
/// per-pixel discard (wall fragment samples <c>_TilesOcclusionMap</c> at its own screen UV and
/// clips when it occludes the play area behind that pixel) — designed for a slow RTS camera.
/// Under fast head movement PARTS of walls pop in/out every frame. This driver replaces the
/// per-pixel screen-space decision with a per-WALL-SEGMENT decision computed on the CPU from the
/// head position, temporally smoothed, and delivered through the wall shaders' OWN fade path via
/// per-renderer <see cref="MaterialPropertyBlock"/>s.
///
/// WALL UNIT (research): scenario walls are <c>ProceduralWall</c> components (GH.Runtime,
/// Apparance procedural entities — one component per wall run with Left/RightCorner + Length;
/// all live instances in the publicized static <c>ProceduralWall.m_WallCache</c>). Their
/// generated child MeshRenderers carry the fade-capable shaders <c>Amp_Basic_WallFade</c>
/// (misc_high_shaders bundle) / <c>Amp_Low/Amp_Basic_WallFade_Low</c> (misc_shaders bundle).
/// The play area is the revealed room tiles: exactly <c>TilesOcclusionGenerator.s_Instance
/// .m_RoomRenderers</c> (fed per revealed room by <c>TilesOcclusionVolume.IsVisible()</c> ==
/// <c>m_HexMap.Revealed</c>) — the same renderer set the game itself rasterizes into the
/// occlusion map.
///
/// FADE MECHANISM (chosen: (a) the game's own map mechanism, forced per renderer — DXBC
/// disassembly of both wall fragment shaders, scratch <c>walldisasm[-low]</c>):
/// <list type="bullet">
/// <item>LOW variant (<c>Amp_Low/Amp_Basic_WallFade_Low</c>, misc_shaders — what the Quest
///   rig's hardware log reported): gate <c>ine cb0[4].x,0</c> (<c>ToggleWallFade</c>, int)
///   AND object-space Y &gt;= 0.4 (hard pre-clip FOUNDATION BAND — the base course never
///   fades); then <c>m = (occ.a &gt;= fragDepth) ? 1 : (1-occ.r)</c>,
///   <c>discard if m - _Cutoff &lt; 0</c> (<c>_Cutoff</c> = "Mask Clip Value", cb0[4].y).</item>
/// <item>HIGH variant (<c>Amp_Basic_WallFade</c>, misc_high_shaders), blob216 lines 165-229:
///   same map term <c>m</c>, <c>M = m·_ToggleWallfade</c> (material float, cb0[6].x);
///   <c>S = smoothstep(sat(3.33·((0.02·dist + screenRadial)^8 + (1-worldY)/3)))</c> — the
///   world-Y foundation ramp and the screen-edge vignette are SUMMED INSIDE one scalar;
///   <c>n</c> = time-drifting world-space simplex noise; <c>A = max(M,S) + 42n·(1-max(M,S))</c>;
///   <c>B = (M&gt;0) ? 1 : S</c>; <c>discard if 1 + ToggleWallFade·(A·B-1) - _Cutoff &lt; 0</c>
///   (<c>_Cutoff</c> = cb0[6].z).</item>
/// </list>
/// Unity property precedence is MPB &gt; material &gt; global, so a per-renderer
/// MaterialPropertyBlock can open the gate (<c>ToggleWallFade=1</c>), substitute the map
/// (<c>_TilesOcclusionMap</c> = a small CONSTANT texture) and set <c>_Cutoff</c> /
/// <c>_ToggleWallfade</c>. Concretely:
/// <list type="bullet">
/// <item>TRANSITION (0&lt;fade&lt;1): map = low-frequency VALUE-NOISE texture (r in [0.06,1],
///   a=0 — fails the reversed-Z depth compare, so <c>m = 1-noise</c>),
///   <c>_Cutoff = lerp(-0.05, 1, fade)</c> → progressive dissolve; the high variant
///   additionally dithers/vignettes with its own view terms. The noise is sampled at SCREEN
///   UV by the shader itself, so the pattern slides under head motion — confined to the
///   ~0.35s dissolve, cosmetic (under conventional-Z it would degrade to an end-of-sweep
///   pop; the rig is D3D11 reversed-Z).</item>
/// <item>HELD FADED (fade=1) — R3 (foundation-band fix; the R2 held state below deleted
///   the base course, the user's bug): drive EXACTLY the value the flat game's own
///   occlusion map delivers over a revealed room. The game never touches <c>_Cutoff</c>
///   at all — it only sets the global <c>ToggleWallFade=1</c> (ActivateWallFadeInGame
///   .Start, Main.Awake) and rasterizes the revealed-room footprints into the
///   screen-space RT <c>_TilesOcclusionMap</c> (TilesOcclusionGenerator
///   .UpdateCommandBuffers: rooms drawn on a (0,0,0,1)-cleared target, blurred, bound
///   globally); over a room interior the blurred map reads occ.r≈1, so the wall shader
///   computes <c>m = 1-occ.r ≈ 0</c> and its OWN foundation terms do the rest. Held MPB
///   on BOTH variants: map = constant r=1 <b>a=0</b> texture → <c>m = 0</c>
///   view-independently (a=0 fails the depth compare for every visible fragment under
///   either Z convention — reversed-Z and conventional fragDepth are both &gt; 0 except
///   the degenerate exact far/near-plane pixel), <c>_Cutoff</c> = the material's
///   AUTHORED "Mask Clip Value" (clamped 0.05–0.95; with m = 0 any 0&lt;c&lt;1 yields
///   the same held geometry — the authored value only shapes the HIGH dither density,
///   matching the flat game exactly), <c>_ToggleWallfade=1</c>. LOW (blob264 lines
///   46-49, 69-71): <c>clip = 0 - c &lt; 0</c> → constant discard wherever objY ≥ 0.4;
///   the base course below the shader's hard object-Y gate stays solid. HIGH (blob216
///   lines 216-229): <c>M = 0</c> → <c>B = S</c>, <c>A = S + 42n(1-S)</c>, so at the
///   foundation the world-Y ramp (1-worldY)/3 saturates S to 1 → <c>A·B = 1</c>,
///   <c>clip = 1-c &gt; 0</c> — solid base band, noise MULTIPLIED BY ZERO, worldY-only
///   (view-independent); up the wall S → 0 → <c>clip = -c &lt; 0</c> — constant
///   discard; between (worldY ≈ 0.4..1) the game's own noise-dithered band edge.
///   RESIDUAL VIEW COUPLING (HIGH only, game-native, accepted because the spec is
///   "exactly the flat game's faded wall"): S also sums the screen-radial vignette
///   (0.02·dist+screenRadial)^8, so peripheral pixels — and whole walls beyond
///   ~45 wu from the head, where min(0.02·dist,1)+radial ≥ 1 — keep the upper wall
///   partially visible exactly as the flat game does near screen edges / zoomed out.
///   The per-wall fade DECISION stays CPU-side and view-independent. IMPOSSIBILITY
///   NOTE (why the vignette cannot be stripped while keeping the band): band term and
///   vignette are summed inside S BEFORE the single cutoff compare, and every vignette
///   coefficient is an immediate DXBC literal — the only strictly view-independent
///   HIGH deliveries are m=1 constants (clip = 1-c everywhere: whole wall visible, or
///   with c&gt;1 the R2 TOTAL discard that erased the foundation). Every fade logs the
///   wall's shader variant + applied cutoff so a hardware log pins down which math
///   applied.</item>
/// <item>SOLID (fade=0): the MPB is REMOVED — with <see cref="Compat.WallFadeDisable"/> now
///   pinning the GLOBAL <c>ToggleWallFade</c> to 0 unconditionally (the game-camera
///   TilesOcclusionGenerator still publishes a head-viewpoint-invalid map; globally-open
///   fade would sample garbage), an untouched renderer is bit-for-bit today's solid wall.</item>
/// </list>
///
/// OCCLUSION DECISION (per segment, VR-stable — round 7, PER-WALL ROOM COVERAGE; user
/// spec: a wall's fraction literally means "share of ITS OWN room's floor (hex tiles)
/// hidden from the current viewpoint" — 0.25 = a quarter of that room's floor is behind
/// the wall).
///
/// ROUND-6 HARDWARE KILL FACTOR (log: EVERY diag line carried the !ABOVE-WALL tripwire):
/// the floor plane was taken from the room renderers' bounds.max.y — but m_RoomRenderers
/// are the game's top-down occlusion-map PROXY meshes; only their XZ footprint matches
/// the tiles, their AABB tops sat ~9 wu above the actual tile plane (log: sampY 9.05 vs
/// wall AABB tops ≤ 3.67, actual floor ≈ 0). Every head→sample ray therefore ran
/// entirely ABOVE every wall box — blocked counts were permanently 0/48 (the one logged
/// frame where the head dipped to y = −0.79 instantly read raw 0.69, proving the ray
/// math fine and the FRAME wrong). Round 7 anchors the floor plane per room on the
/// game's own tile data: each <c>TilesOcclusionVolume</c> maps its <c>Renderers</c> to
/// its <c>CentralTile</c> (a <c>TileBehaviour</c> whose transform sits ON the tile
/// plane — the game spawns its worldspace tile UI at exactly that position), so sample
/// height = CentralTile.position.y + 0.05 wu; rooms without a volume match fall back to
/// the median anchored height (then, with zero anchors, to the old bounds top — and the
/// !ABOVE-WALL tripwire stays to catch that in hardware logs).
///
/// METRIC: at rescan every wall is associated with ONE room (smallest XZ gap between the
/// wall AABB and the room AABB — walls border their room, gap ≈ 0; ties by nearer
/// center). Per frame:
///   fraction = (points of the wall's room floor grid that are IN VIEW-DIRECTION and
///               whose head→point segment the wall AABB clearly interrupts)
///              / (ALL floor-grid points of that room).
/// Frustum culling (viewport test, 0.20 margin) applies to the NUMERATOR ONLY — a floor
/// point outside the view cannot be "hidden by the wall" in the user's sense — while the
/// denominator stays the room's WHOLE grid so the number reads literally as "this wall
/// hides X% of the room's floor" and the VR-menu stepper values keep their plain meaning.
/// "Clearly interrupts" = AABB entry distance &lt; dist − max(0.5·thickness clamped
/// 0.10–0.90 wu, 0.05·dist), OR the sample lies inside the AABB (the round-4
/// 88%-of-distance rule discarded exactly the near-edge first-row samples that carry the
/// whole signal; a pure thickness epsilon was still too strict for long grazing rays —
/// the 5%-of-distance term keeps the margin proportionate). Head inside the wall AABB
/// counts as 1.0 (wall in the face).
///
/// THE DENOMINATOR IS THE PLAYABLE TILES (ModBuild 258 — user report 2026-08-24,
/// wandproblem3.jpg, his own emphasis: "wenn man aber die wand gegenüber einguckt DIE
/// NICHTS VERDECKT von den spielbaren tiles sollte sie direkt unfaden"). The floor grid
/// spreads a grid×grid lattice over the room's axis-aligned BOUNDING BOX, and until
/// ModBuild 257 nothing tested whether a lattice position landed on a tile at all. A
/// Gloomhaven room is a HEX CLUSTER, so that rectangle's corners are dead space, and the
/// ModBuild 257 log shows all four green walls pinned at a DIFFERENT corner of it —
/// 'Wall 4' at cells #0,#1,#4,#8, 'Wall 2' at #7,#11,#14,#15, 'Wall 1' at #2,#3, 'Wall 3'
/// at #12 — each at the corner nearest itself, and each taken by a prop standing OFF the
/// field ('FR_Pillar_Tree_Trunk_01/_02', 'Blocks'). Four of sixteen is 0.25, which sits
/// between the 0.20 exit bar and the 0.35 enter bar, so 'Wall 2' and 'Wall 4' printed the
/// SAME reading in both states (13 samples at "ema 0.25 blk 4/16 FADED" against 11 at
/// "ema 0.25 blk 4/16 solid") and could leave neither; 'Wall 1' pinned at TWO cells
/// (0.125 &lt; 0.20) and released, and that two-versus-four was the entire difference
/// between the wall the user says works and the two he reports stuck. Since ModBuild 258
/// each lattice position is MOVED to the nearest playable hex centre that no other
/// position has claimed, from the game's own tile registry
/// (<c>ObjectCacheService.GetTileBehaviors</c>, keyed by the same CMap the room registry
/// groups by) — so every sample stands on floor the player can stand on, and the count
/// stays min(grid², hexes) = 16, leaving the quantum and both bars in cells exactly where
/// they were. See <c>CollectPlayableTiles</c> and <c>RebuildSamples</c>; a room whose
/// hexes cannot be resolved keeps the bounding-box grid and says so, in those words, on
/// the SAMPLE GRID line.
///
/// That fixed 'Wall 4' — user, on the ModBuild 258 build: <i>"Schaut man auf die Tür dann
/// hast du mit deinem letzten Fix nun die rechte Wand vollständig gefixed"</i> — and left
/// 'Wall 2' pinned at the same four cells, #7,#11,#14,#15, because ModBuild 258's
/// "playable" only meant "a TileBehaviour keyed to this CMap inside this room's footprint".
/// The room's own EDGE hexes passed that test, which is the user's next sentence: <i>"Mir
/// ist aufgefallen dass dieses Wand eigene nicht-spielbare tiles hat … Nur spielbare tiles
/// sollen berücksichtigt werden"</i>. ModBuild 259 therefore narrows the set with the
/// PERSISTENT half of the game's own passability test <c>AStar.CNode.NavTo</c> —
/// <c>Walkable &amp;&amp; !Blocked</c>, where <c>Walkable</c> is literally
/// "<c>!FlagsSet(EFlags.Blocked | EFlags.Edge)</c>" as <c>ScenarioManager.Load</c> writes it
/// — and refuses its two transient terms, which are per-A*-query scratch written from actor
/// positions. See <c>ClassifyHex</c> for every term and its writer. The narrowing applies
/// only while at least grid² playable hexes survive, so the denominator, the quantum and
/// both bars cannot move; a room below that keeps ModBuild 258's set and the SAMPLE GRID
/// line prints PLAYABLE FILTER HELD BACK with the bars the filtered set would have needed.
///
/// LOGICAL ROOM GROUPING (keep round 4 — user ruling: "Ich will weiterhin die normale
/// Raum-Logik", which retired round 3's distance-based cross-room MAX after one ModBuild):
/// the game's unit of reveal is the <c>CMap</c> (ScenarioRuleLibrary) reached via
/// <c>TilesOcclusionVolume.CentralTile.m_ClientTile.m_Tile.m_HexMap</c> — the exact object
/// whose <c>Revealed</c> flag the volume's own <c>IsVisible()</c> reads. One revealed room
/// may ship SEVERAL occlusion volumes ('Volume_1..6' of the keep are sub-volumes of ONE
/// CMap), and the registry used to treat every volume renderer as its own room — so the
/// wall in front of the player hid "another room's" samples that were in truth the SAME
/// room, and its own-room fraction read 0.00 forever. The registry now merges volume
/// renderers per (CMap, quantized floor height) into one LOGICAL room — union XZ footprint,
/// one grid, one fail-safe/anchor state — and the metric stays strict own-room accounting
/// against that merged grid. Single-volume rooms group to themselves (identical math);
/// terraced same-CMap volumes at different heights stay separate so each keeps its true
/// sample plane.
///
/// TRIGGER (fully stepper-driven, WallFadeTuning live config): the raw fraction is
/// EMA-smoothed (tau 0.15s), then compared against the VR-menu steppers — ON at
/// ≥ OnFraction (default 0.25), and once faded a SCHMITT TRIGGER holds down to
/// OffFraction (default 0.10). HEAD-MOTION DECOUPLING: fade-IN needs only a short 0.2s
/// dwell (prompt); fade-OUT is deliberately DELAYED — ExitDwellMoved (2.5s) continuously
/// below the low bar when the PERSPECTIVE recently (≤3s) actually changed (real head
/// TRANSLATION &gt;0.18m tracking-space, rig-root motion from world-grab/snap-turn,
/// recenter/rig rebuild, room-bounds shift), stretching to ExitDwellStationary (7s) when
/// the head only rotated. No other head-motion-coupled term exists in the decision. The
/// fade value itself stays exponentially damped (tau 0.12s ≈ 0.35s visible transition).
///
/// DOORWAY RULING (user, 2026-08-02 — final, supersedes every previous doorway ruling):
/// archway/doorway segments NEVER fade — no open/closed differentiation, no hard hide.
/// They are always solid. Fade renderers hugging a door prop are still RECOGNIZED per
/// door (spatial link to the UnityGameEditorDoorProp roots,
/// <see cref="FadeDriver.FindDoorwayRoot"/>) so an archway can never merge into a
/// fadeable wall segment — the per-door segment is held permanently SOLID and never
/// enters the fade decision. The three rounds of open-doorway fade/hide experiments
/// (and how to revive them) are parked in <c>.planning/doorway-fade-experiments.md</c>.
///
/// MULTIPLAYER: purely local rendering (MaterialPropertyBlocks + locally created textures);
/// nothing synced, peers unaffected.
/// Gated LIVE by [Compat] WallFade — OFF clears every block
/// immediately (exactly today's solid walls, zero per-frame cost beyond the enabled check).
/// </summary>
internal static partial class WallSegmentFade
{
    private const string Name = "WallSegmentFade";
    private const string DriverName = "GloomhavenVR.WallSegmentFade";

    private static FadeDriver? _driver;

    /// <summary>Install the fade driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        WallFadeTuning.Bind(); // decision thresholds are live config (debug-menu steppers)
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FadeDriver>();
        VRLog.Info(Name,
            $"installed (WallFade={(Plugin.WallFade != null && Plugin.WallFade.Value ? "on" : "off")}) — " +
            "whole-wall fade: per-ProceduralWall ROOM-coverage decision (fraction of the " +
            "wall's own room's tile-anchored floor grid hidden from the head, frustum-culled " +
            "numerator, EMA + stepper-driven Schmitt trigger + perspective-anchored dwell), " +
            "delivered via per-renderer MaterialPropertyBlocks through the wall shaders' own " +
            "map/cutoff fade path.");
    }

    /// <summary>Clear every property block and destroy the driver (hot-reload safe).</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.Teardown(); }
        catch { /* scene already tearing down */ }
        try { UnityEngine.Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private static bool Enabled => Plugin.WallFade != null && Plugin.WallFade.Value;

    /// <summary>
    /// THE game's own marker for "this mesh fades when it hides the play area": its material
    /// runs one of the WallFade shader family (Amp_Basic_WallFade, Amp_Low/Amp_Basic_WallFade_Low,
    /// and any themed sibling — the flat game's fade is purely shader-driven, it keeps no object
    /// list). Single source of truth for "is this renderer fade-capable".
    /// </summary>
    internal static bool IsWallFadeShaderName(string shaderName) => shaderName.Contains("WallFade");

    /// <summary>
    /// Foliage family (Amp_Basic_Foliage, Amp_Basic_Foliage_Prop_Shader, …): the grasses, vines
    /// and bushes DRESSING a wall. They carry no WallFade path, so when the wall dissolves they
    /// used to stay behind as a view-blocking "Gestrüpp-Wand" (user report + gebüsch.png). They
    /// are collected as ATTACHMENTS of their wall's segment and hidden with it.
    /// </summary>
    internal static bool IsFoliageShaderName(string shaderName) => shaderName.Contains("Foliage");

    /// <summary>Per-wall-segment fade state.</summary>
    private sealed partial class Segment
    {
        /// <summary>Tracking anchor and dictionary key: the <see cref="ProceduralWall"/> for
        /// cache-listed walls; for shader-ADOPTED groups (advanced tilesets put fade-capable wall
        /// meshes on map tiles instead of ProceduralWall entities) the nearest
        /// <see cref="ProceduralTileObserver"/> ancestor, else the renderer's parent transform.</summary>
        public Component? Anchor;
        /// <summary>True = from ProceduralWall.m_WallCache (renderers re-collected from the wall's
        /// own subtree); false = adopted by shader match (renderers re-collected by the rescan sweep).</summary>
        public bool FromWallCache;
        public readonly List<MeshRenderer> Renderers = new();
        /// <summary>Refresh scratch: the renderer list BEFORE the current refresh, so a renderer
        /// that left a still-faded segment gets its property block cleared instead of keeping a
        /// stale fade forever.</summary>
        public readonly List<MeshRenderer> PrevRenderers = new();
        /// <summary>Foliage ATTACHMENTS (grass/vines/bushes dressing this wall — Foliage-family
        /// shaders, no fade path of their own): dissolved via an alpha-cutoff ramp while the wall
        /// fades and fully hidden in the held state, restored exactly when the wall returns.</summary>
        public readonly List<MeshRenderer> Foliage = new();
        public readonly List<MeshRenderer> PrevFoliage = new();
        /// <summary>0 = restored/untouched, 1 = dissolving (cutoff MPB set), 2 = hidden.</summary>
        public int FoliageState;
        /// <summary>ASSET-COMPLETE fade siblings (the Torbogen ruling, adopted groups only): a
        /// mixed doorway/arch asset carries the WallFade shader on its FRAME/PILLAR meshes but
        /// not on the wooden door wings / arch trim — fading only the shader-matched renderers
        /// left a floating door remnant (torbogen.png). These are the non-fade renderers under
        /// the same prefab-ish asset root (see <see cref="FadeDriver.FindAssetRoot"/>): hidden
        /// via renderer.enabled once the segment reaches the held state, restored exactly on
        /// unfade — the foliage restore discipline, no material mutation.</summary>
        public readonly List<MeshRenderer> Siblings = new();
        public readonly List<MeshRenderer> PrevSiblings = new();
        /// <summary>0 = restored/untouched, 2 = hidden (siblings have no dissolve ramp — they
        /// run arbitrary opaque shaders, so they pop with the END of the wall's dissolve).</summary>
        public int SiblingState;
        /// <summary>DOORWAY marker (user ruling 2026-08-02: doorways NEVER fade): non-null
        /// keys this segment to its door prop root ('ThinDoor : (guid)', the
        /// UnityGameEditorDoorProp object) — every fade renderer hugging that door lands in
        /// this ONE per-door segment, which the decision loop holds permanently SOLID. The
        /// keying exists purely so archway frames can never merge into a fadeable wall
        /// segment; parked experiments in .planning/doorway-fade-experiments.md.</summary>
        public Transform? DoorRoot;
        public Bounds Bounds;
        public bool HasBounds;
        /// <summary>
        /// PERF S5 — the union of <see cref="Renderers"/> as the DRIFT PROBE last measured it,
        /// and the probe is its only writer.
        ///
        /// <para>WHY NOT <see cref="Bounds"/>, AND WHY NOT A COMMIT-TIME COPY OF IT. Neither is
        /// comparable with a live re-union of this list. <see cref="Bounds"/> is EXTENDED after
        /// collection by the stacked and gate phases, so it always reads larger; and a copy
        /// taken at <see cref="FadeDriver.FinishRefresh"/> is taken before
        /// <see cref="FadeDriver.StripGroundRenderers"/> and
        /// <c>EnforcePropUnitCohesion</c> have REMOVED members from the very list it would be
        /// compared against, so it always reads larger too. Both would report drift on every
        /// ground-stripped wall forever, i.e. the skip would never fire — the shape of failure
        /// this project files under "a gated remedy never ran". The probe therefore takes its
        /// own baseline off the FINAL table, once per commit, and compares like with like.</para>
        /// </summary>
        public Bounds ProbeBounds;
        public bool ProbeBoundsValid;
        /// <summary>
        /// ModBuild 261 — WHY this segment owns no wall renderer, when that is the reason it is
        /// boundless. Non-null ONLY for a split-run piece whose renderer was refused at the wall
        /// choke point (<see cref="FadeDriver.CollectWallFadeInfo"/>); null everywhere else.
        ///
        /// <para>It exists because the ModBuild 260 log reported 99 of 'Wall 1''s 140 pieces as
        /// "held solid by an older fail-safe" and named NO-BOUNDS as the reason. NO-BOUNDS is the
        /// CONSEQUENCE — a refused renderer is never added to <c>Renderers</c>, so the AABB the
        /// bounds are rebuilt from is empty. Naming the proximate cause sent four builds looking
        /// at the wrong rule; this field carries the real one into the leftover audit.</para>
        /// </summary>
        public string? GeometryRefusedWhy;
        /// <summary>
        /// ModBuild 261 — this split-run piece is only wall geometry because
        /// <c>[WallFade] SplitRunAdoptGroundScenery</c> stood the FLOOR arm down for it. It RIDES
        /// its run's verdict and never contributes a cell to the coverage that decides it.
        ///
        /// <para>WHY A PASSENGER AND NOT A MEMBER. The union is what makes a run fade, and a bush
        /// standing a metre inside the room would hide floor it has no business voting on — a
        /// recruited piece that voted could make walls fade EARLIER, and the trigger would then
        /// have changed in the same build as the population. Keeping the vote out means the
        /// ModBuild 260 RUN FADE numbers are directly comparable with the dial in either
        /// position, which is the only way the next log can attribute what it sees.</para>
        /// </summary>
        public bool RunPassenger;

        /// <summary>Last raw (unsmoothed) occlusion verdict and when it first held.</summary>
        public bool PendingRaw;
        public float PendingSince;
        /// <summary>Debounced state the fade animates toward.</summary>
        public bool State;
        /// <summary>Damped fade value in [0,1]; 0 = solid (no MPB), 1 = held fully faded.</summary>
        public float Fade;
        /// <summary>Whether our MPB is currently applied to the renderers.</summary>
        public bool HasBlock;

        /// <summary>Blocked-test distance epsilon (world units, ~half the wall thickness).</summary>
        public float BlockEps = 0.3f;
        /// <summary>Index into the room tables of the ONE room this wall belongs to (XZ-nearest
        /// room AABB, recomputed at rescan); -1 while unassociated.</summary>
        public int RoomIndex = -1;
        /// <summary>ROOM SEAM (user report 2026-08-09 — the wall piece that faded "falsch rum"):
        /// the OTHER decision-valid rooms whose bounds this wall also borders, i.e. every room
        /// besides <see cref="RoomIndex"/> within <see cref="FadeDriver.RoomBorderBandWU"/> of
        /// it. Empty for the overwhelming majority of walls; non-empty only for a wall standing
        /// in the seam BETWEEN two rooms, where "which room is mine" is a coin flip and getting
        /// it wrong inverts the fade. See <see cref="FadeDriver.BlockedFraction"/>.</summary>
        public readonly List<int> BorderRooms = new();
        /// <summary>Which room actually decided the last coverage reading (diag) — differs from
        /// <see cref="RoomIndex"/> only for seam walls.</summary>
        public int LastDecidingRoom = -1;
        /// <summary>R2 diag: which fade-shader variant(s) this segment's renderers carry.</summary>
        public bool VariantHigh;
        public bool VariantLow;
        /// <summary>Distinct fade-shader name(s) seen on the renderers ("+"-joined).</summary>
        public string ShaderNames = "?";
        /// <summary>How many renderers joined via the round-8 TOGGLE-NATIVE path (materials
        /// with _Cutoff + _WallFade_On/_ToggleWallfade — the masonry) — diag/fade-ON label.</summary>
        public int ToggleNative;
        /// <summary>Authored "Mask Clip Value" (<c>_Cutoff</c>) of the wall's fade material,
        /// clamped to (0,1) — the held state drives exactly this value like the flat game
        /// (which never writes _Cutoff at all). 0.5 fallback when unreadable.</summary>
        public float HeldCutoff = 0.5f;
        /// <summary>Whether <see cref="HeldCutoff"/> came from the material (diag).</summary>
        public bool CutoffAuthored;
        /// <summary>True when this segment's AABB engulfs its own room's floor samples AND it
        /// cannot be split further (single renderer): the coverage metric is meaningless for it,
        /// so it is held permanently SOLID (vanilla look). Re-derived every rescan.</summary>
        public bool Engulfing;

        /// <summary>
        /// SPLIT-RUN MEMBERSHIP (ModBuild 259, user ruling 2026-08-24: <i>"Entweder verschwindet
        /// die ganze Wand mit ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist
        /// vollständig da. So ein Zwischending soll es nicht geben."</i>). Non-null when this
        /// segment is ONE RENDERER carved out of a bigger group by
        /// <see cref="FadeDriver.NeutralizeEngulfingSegments"/>; it holds the ANCHOR of the group
        /// it was carved from — the <see cref="ProceduralWall"/> for a cache run, the tile
        /// observer / parent transform for an adopted one.
        ///
        /// <para>WHY IT IS A STRUCTURAL FACT AND NOT A THRESHOLD. The split site holds the owning
        /// group in its hand: <see cref="FadeDriver.RefreshSplitWall"/> is called
        /// <c>RefreshSplitWall(wall)</c> and enumerates exactly <c>wall</c>'s own subtree, so
        /// "which run does this trunk belong to" is answered by the game's own generation
        /// hierarchy. No distance test, no tolerance, nothing to tune.</para>
        ///
        /// <para>WHAT IT CHANGES. The split exists so that a room-ENGULFING union AABB can never
        /// be the occluder box (the jungle-ground defect — see NeutralizeEngulfingSegments), and
        /// that stays: every piece keeps measuring its own coverage against its own renderer. What
        /// stops is each piece DECIDING on that number alone. The run's coverage is the UNION of
        /// its members' blocked cells, and one Schmitt trigger drives every member. See
        /// <see cref="FadeDriver.EvaluateSplitRuns"/>.</para>
        /// </summary>
        public Component? RunOwner;
        /// <summary>True once this segment has been carved out of a group, whether or not
        /// <see cref="RunOwner"/> is still alive. The pair distinguishes "never split" from
        /// "split, and the run it belonged to has been destroyed" — the ORPHAN case, which falls
        /// back to its own decision and is counted on the SPLIT RUN line.</summary>
        public bool FromSplitRun;
        /// <summary>Set by <see cref="FadeDriver.EvaluateSplitRuns"/> on every evaluation: this
        /// piece's verdict came from its run this pass, so the decision loop must not re-derive
        /// one. Sticky across skipped evaluations by design — the run's state is what it would
        /// have re-derived anyway.</summary>
        public bool RunDriven;

        /// <summary>EMA-smoothed view-coverage fraction the Schmitt trigger reads.</summary>
        public float Smooth;
        public bool SmoothInit;
        // Last-tick raw numbers, kept for the throttled diagnostic.
        public float LastRaw;
        public int LastBlocked;
        /// <summary>In-view sample count of THIS wall's room last tick (numerator candidates).</summary>
        public int LastRoomVisible;
        /// <summary>Total floor-grid points of this wall's room (the fraction denominator).</summary>
        public int LastRoomTotal;

        /// <summary>
        /// PER-CELL ATTRIBUTION (ModBuild 256). Which floor cells of the deciding room this wall
        /// blocked on the last evaluation — room-relative indices — and, for the first of them,
        /// WHICH renderer accepted the ray.
        ///
        /// <para>Every argument about this subsystem since ModBuild 250 has come down to one
        /// unanswerable question: when a wall reads "blocks 2 of 16", is that two real cells of
        /// hidden floor or a residue of geometry that should not be in the numerator? A count
        /// cannot answer it and a coverage fraction cannot answer it. The cell indices plus the
        /// blocking piece's name answer it in one log line, for good.</para>
        ///
        /// <para>Cost is a cleared list and at most one integer append per blocked sample, on the
        /// evaluation path only — no allocation after the first few frames, since the list keeps
        /// its capacity.</para>
        /// </summary>
        public readonly List<int> LastBlockedCells = new();
        /// <summary>Renderer that accepted the ray for the FIRST blocked cell — resolved to a
        /// name only when the falsifier prints, so the hot path never touches a string.</summary>
        public Renderer? LastBlockerPiece;

        /// <summary>
        /// WHICH OF THIS SEGMENT'S LISTS the first blocker came from (ModBuild 257). A literal, so
        /// no allocation: <c>"Renderers"</c>, <c>"Foliage"</c>, <c>"Siblings"</c>, <c>"Body"</c> or
        /// <c>"Stacked"</c>.
        ///
        /// <para>WHY IT MUST BE IN THE LOG. <see cref="FadeDriver.RayHitsWallMesh"/> walks five
        /// lists and a NAME alone cannot say which one answered — and the fix for a wrongly
        /// included piece is at a completely different site depending on the answer. A piece in
        /// <c>Renderers</c> was collected by the tileset's own parenting and the membership
        /// classifier (<see cref="WallStandingProp"/>) is the right place; a piece in
        /// <c>Stacked</c> or <c>Siblings</c> was ADOPTED, and the fix belongs at that adoption
        /// site. ModBuild 256 shipped the blocker's name and the next round still had to derive the
        /// list from a different census's preview line.</para>
        /// </summary>
        public string LastBlockerList = "-";

        /// <summary>
        /// Did the first blocker take its cell by a RAY ENTRY (it stands between the eye and that
        /// floor point) or by <c>Bounds.Contains</c> (the floor point is INSIDE its box)?
        ///
        /// <para>THE DISTINCTION IS THE RATCHET. A ray hit depends on where the head is; a
        /// Contains hit does not, so a piece that straddles the floor plane claims its cells from
        /// every viewing angle forever. That is the shape of the ModBuild-256 report: 'Wall 4'
        /// reports <c>blk 4/16 cells #0,#1,#4,#8</c> in 24 separate samples and 'Wall 2' reports
        /// <c>blk 4/16 cells #7,#11,#14,#15</c> in 21 — identical sets, opposite corners, whatever
        /// the head did — and 4/16 = 0.25 sits above the 0.20 exit bar, so neither wall can ever
        /// fall back out. <see cref="LastContainsCells"/> carries the count for the whole
        /// pass.</para>
        /// </summary>
        public bool LastBlockerByContains;

        /// <summary>
        /// How many of <see cref="LastBlockedCells"/> were taken by a <c>Contains</c> claim rather
        /// than a ray entry — head-INDEPENDENT blocking, i.e. the coverage floor this wall can
        /// never fall below from any position. See <see cref="LastBlockerByContains"/>.
        ///
        /// <para>THE LATCH IS AN INTEGER, and the whole subsystem turns on it: on a 16-cell floor
        /// grid the exit bar of 0.20 releases a wall at 3/16 = 0.1875 and holds it at 4/16 = 0.25,
        /// so FOUR permanently-blocked cells latch a wall for the session and three do not. That is
        /// the arithmetic behind <i>"drehe ich mich einmal im Kreis ist alles gefaded — mach ich
        /// das nochmal bleibt alles gefaded"</i>. It is not a fact about trees: trees are merely
        /// what supplies the four cells in the reported forest scenario, and a wall's own base
        /// course straddles the floor plane too. Taking the trees out of the membership is only a
        /// cure if it drops the count to three or fewer, which is why this counter ships in the
        /// same build as the cure rather than after it.</para>
        /// </summary>
        public int LastContainsCells;
    }

    private sealed partial class FadeDriver : MonoBehaviour
    {
        // --- decision constants (see class header) ------------------------------------------
        // SCALE SEMANTICS (round-5 audit): every linear constant below is WORLD units (wu)
        // unless it says "real/tracking meters". The rig root is scaled UP by WorldScale
        // (hardware log: 20.3; range ~11–20), i.e. 1 real meter = 11–20 wu and 1 wu = 5–9
        // real cm; board geometry keeps original game units (hex tile ≈ 1.72 wu). Sample
        // height needs no inference since round 6: the samples sit ON the room tile
        // bounds' top surface — the tile plane itself (see RebuildSamples).
        // On/off fractions + the two exit dwells are LIVE CONFIG now (WallFadeTuning — the
        // settings panel's debug steppers drive them in-headset); read fresh every evaluation.
        private const float EnterDwellSeconds = 0.20f; // short fade-IN prompt dwell (~0.2s per spec)
        private const float HeadMoveReevalMeters = 0.18f; // REAL tracking-space meters (scale-independent)
        private const float ReevalArmSeconds = 3f;     // how long a perspective change keeps re-eval armed
        private const float FadeTauSeconds = 0.12f;    // exp. fade time constant (~0.35s to 95%)
        private const float FractionTauSeconds = 0.15f; // EMA over the raw fraction (jitter killer)
        private const float FloorSampleEpsilon = 0.05f; // wu above the tile-anchored floor plane
        private const float BlockEpsMinWorld = 0.10f;  // wu — thickness-epsilon clamp (lo)
        private const float BlockEpsMaxWorld = 0.90f;  // wu — thickness-epsilon clamp (hi)
        private const float BlockEpsDistFraction = 0.05f; // blocked eps = max(thicknessEps, 5% of dist)
        private const float FrustumMargin = 0.20f;     // viewport slack (also covers per-eye vs mono skew)
        private const int MaxTotalSamples = 96;        // precomputed floor samples (all rooms)
        /// <summary>
        /// Seconds between two rescan cycles. A LIVE CONFIG VALUE since ModBuild 278 (user
        /// request 2026-08-25: <i>"Würde es helfen hier die Abtastrate … etwas zu verringern?
        /// Am Besten lass sie in den Einstellungen selber einstellen können."</i>) — it was
        /// <c>private const float RescanIntervalSeconds = 2f</c> from the subsystem's first
        /// build until then, and <c>Defaults.RescanIntervalSeconds</c> holds that same 2.0 so
        /// nothing moves at the shipped value.
        ///
        /// <para>READ IT FOR DISPLAY AND FOR SCHEDULING, NEVER FOR JUDGING A CYCLE THAT IS
        /// ALREADY OPEN. The value can change between the frame a cycle was scheduled on and
        /// the frame it opens, and one term of the skip decision is a comparison against the
        /// cadence — see <see cref="_scheduledRescanInterval"/>, which is what that comparison
        /// must use and why.</para>
        /// </summary>
        private static float RescanIntervalSeconds => WallFadeTuning.RescanIntervalSeconds;
        private const float DiagIntervalSeconds = 2f;  // throttled hardware diagnostic cadence

        private static readonly int TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap");
        private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade");
        private static readonly int ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        /// <summary>Round-8 (BODY SHADER PROPERTIES dump, ModBuild 64): the keep masonry's
        /// <c>Amp_Basic_N_MRAO</c> exposes <c>_Cutoff</c> + <c>_WallFade_On</c> — the Amp
        /// family compiles the wall-fade subgraph into "plain" shaders behind this material
        /// toggle. Our MPB opens it per renderer alongside the standard fade set.</summary>
        private static readonly int WallFadeOnMatId = Shader.PropertyToID("_WallFade_On");
        /// <summary>Round-9 game-wide audit: the third toggle spelling. The game's own
        /// <c>ToggleWallFadeScript</c> writes this int per renderer at Start — an authored
        /// per-asset OPT-OUT (0 = never fade). Honored: a material whose only gate is this
        /// and reads 0 is authored always-solid and left alone (the doorway-ruling spirit);
        /// it is never pinned to 1 in the MPB.</summary>
        private static readonly int ToggleWallFadeLocalMatId =
            Shader.PropertyToID("_ToggleWallFadeLocal");
        /// <summary>The compile-time keyword behind <c>_WallFade_On</c> (ModBuild-65
        /// adjudication: wall materials ship authored 1 + keyword <c>_WALLFADE_ON_ON</c> —
        /// fade branch present, MPB driveable; floor materials ship 0 + no keyword — branch
        /// absent, MPB inert).</summary>
        private const string WallFadeOnKeyword = "_WALLFADE_ON_ON";

        private readonly List<Component> _deadKeys = new();
        /// <summary>Renderers owned by wall-cache segments this rescan — the adoption sweep must
        /// never create a second segment for them.</summary>
        private readonly HashSet<MeshRenderer> _claimedRenderers = new();
        /// <summary>Per-shader "is wall-fade-capable" verdict cache (the rescan sweep tests every
        /// scene renderer; a scene has ~26 distinct materials over a handful of shaders).</summary>
        private readonly Dictionary<Shader, bool> _shaderVerdict = new();
        /// <summary>Same cache for the foliage-family verdict.</summary>
        private readonly Dictionary<Shader, bool> _shaderFoliageVerdict = new();
        /// <summary>Cutoff the dissolve ramps TOWARD: safely above every texel's alpha, so a
        /// fully-faded cutout leaf discards completely.
        ///
        /// <para>The matching ramp START used to be a hardcoded 0.35 — a GUESS at "the common
        /// authored Mask Clip Value" — written through one shared MPB to every foliage material
        /// in the scene. ModBuild 254 retired that: foliage goes through <c>ClassifyProp</c> /
        /// <c>DriveProp</c> like every other attachment class, and that path already ramps from
        /// <c>p.BaseCutoff</c>, the material's OWN authored value, read per piece. A guessed
        /// start is a step at both ends of the ramp for any material that authored something
        /// else, and a material with no live cutoff at all got no dissolve whatsoever.</para>
        /// </summary>
        private const float FoliageCutoffEnd = 1.2f;
        /// <summary>Minimum vertical extent, as a fraction of the NARROWER horizontal extent,
        /// for a piece to be admitted to the occlusion numerator — see
        /// <see cref="IsStandingPiece"/>. 0.5 sits an order of magnitude clear of both
        /// populations in the logged tileset (ground mats ≈0.08, the scrub wall's own bushes
        /// 0.59-1.43, a wall slab ≥6), so it is not a value that wants tuning.</summary>
        private const float StandingPieceRatio = 0.5f;
        /// <summary>True while <see cref="RoomBlockedFraction"/> is measuring a segment's OWN
        /// room, so the per-cell attribution list describes exactly one room's grid — a seam
        /// wall's alt-room passes run with it false.</summary>
        private bool _attributeCells = true;
        /// <summary>Above this fade the foliage renderer is DISABLED outright — the cutoff ramp
        /// only removes cutout texels, and any opaque twig material would otherwise survive.</summary>
        private const float FoliageHideFade = 0.99f;
        // Rescan census (heartbeat diagnostics): how many fade-capable renderers exist, how many
        // the wall cache claimed, how many the shader sweep adopted — and the shader names seen on
        // cache walls that carry NO fade-capable renderer at all (the tripwire for a tileset whose
        // wall shaders are named outside the WallFade family).
        private int _censusFadeRenderers;
        private int _censusClaimed;
        private int _censusAdopted;
        private int _censusWallsWithoutFade;
        private readonly HashSet<string> _unfadeableWallShaders = new();
        private int _heartbeatSegCount = -1;
        /// <summary>Fade-capable renderer census as last heartbeat-logged — a change re-arms
        /// the heartbeat (round 5: the one stale pre-generation heartbeat hid the TRIPWIRE
        /// for five hardware rounds).</summary>
        private int _heartbeatFadeRenderers = -1;

        private readonly List<KeyValuePair<Component, Segment>> _fatScratch = new();
        /// <summary>An adopted GROUP whose horizontal AABB is thicker than this (wu; a wall run's
        /// thin extent is ≤ ~2 wu, a hex tile ≈ 1.72 wu, a room ≥ ~8 wu) is split per renderer.</summary>
        private const float GroupSlabMaxHorizontal = 3.5f;

        // ---- asset-complete fade (Torbogen ruling) ----------------------------------------
        /// <summary>Rescan-scope dedupe: every renderer attached as an asset sibling this rescan
        /// — exactly ONE segment may own (and restore) a sibling, or two owners would fight over
        /// renderer.enabled every frame.</summary>
        private readonly HashSet<MeshRenderer> _siblingOwned = new();
        private readonly List<MeshRenderer> _subtreeScratch = new();
        /// <summary>An ancestor whose subtree holds more MeshRenderers than this is a CONTAINER
        /// (the 'L :' Apparance layer/section-root class), not a prefab-sized asset — the walk
        /// stops there and attaches nothing (fail-open: the ugly remnant stays visible, which
        /// beats hiding unrelated geometry). A doorway asset is ~2 frames + 4 pillars + 2 wings
        /// + trim + lock ≈ ≤ 12 renderers.</summary>
        private const int MaxAssetRootRenderers = 16;
        /// <summary>Ancestor-walk depth cap — prefab roots sit 1-3 levels above their meshes;
        /// anything deeper is scene structure.</summary>
        private const int MaxAssetRootDepth = 4;
        // ---- doorway recognition (user ruling 2026-08-02: doorways NEVER fade) ------------
        /// <summary>Door prop roots rebuilt each rescan: every live
        /// <c>UnityGameEditorDoorProp</c> transform (the 'ThinDoor : (guid)' objects the
        /// Choreographer opens via <c>ObjectCacheService.GetPropObject</c>). Fade renderers
        /// adjacent to one of these are grouped into a per-DOOR segment (see
        /// <see cref="FindDoorwayRoot"/>) that is held permanently SOLID — an archway must
        /// never merge into a fadeable wall segment. Entrance/exit doors lose their prop
        /// component at spawn (ApparanceLayer.Create destroys procDoor) — they are not
        /// listed and their frames keep the generic adopted behaviour (accepted).</summary>
        private readonly List<Transform> _doorRoots = new();
        /// <summary>Max XZ gap (wu) between a fade renderer's AABB and a door prop position for
        /// the renderer to count as that doorway's frame — mirrors the game's own wall-search
        /// radius around a door (ProceduralTile.FindMapTileByPosition: FindWallsNear(pos, 2.2f)).
        /// Frames/pillars hug the door; the next parallel wall run is ≥ a hex (~1.72 wu) of
        /// clear floor away, so 2.2 cannot swallow a neighbouring wall.</summary>
        private const float DoorwayLinkMaxXZ = 2.2f;

        /// <summary>Room-registry census as last logged (reveal re-anchor diagnostic).</summary>
        private int _lastRoomCensusCount = -1;
        private int _lastRoomCensusAnchored = -1;
        // LOGICAL ROOM GROUPING (round 4): per-renderer game-room identity (the CMap behind
        // the volume's CentralTile — the object whose .Revealed the game itself reveals),
        // its display label, and the merged-room tables the registry builds from them.
        private readonly Dictionary<MeshRenderer, object> _roomMapByRenderer = new();
        private readonly Dictionary<MeshRenderer, string> _roomMapLabelByRenderer = new();
        private readonly Dictionary<(object, int), int> _keyToRoomScratch = new();
        private readonly List<int> _roomRendererCounts = new(); // volume renderers merged per room
        // PLAYABLE-TILE DENOMINATOR (ModBuild 258 — user report 2026-08-24, wandproblem3.jpg:
        // "wenn man aber die wand gegenüber einguckt DIE NICHTS VERDECKT von den spielbaren
        // tiles sollte sie direkt unfaden"). The room's playable hexes, keyed by the game's own
        // CMap — the SAME object the room registry already groups rooms by. See
        // CollectPlayableTiles for the source and RebuildSamples for what the grid does with it.
        private readonly List<object?> _roomMapKeys = new();     // game CMap per LOGICAL room (null = none)
        private readonly Dictionary<object, List<Vector3>> _tilesByMap = new(); // hex centres per CMap
        private readonly List<List<Vector3>> _tileListPool = new(); // reused hex lists (no per-rescan alloc)
        private int _tileListsUsed;
        // PLAYABILITY VERDICT PER HEX (ModBuild 259) — index-aligned with _tilesByMap's list for
        // the same CMap, one HexPlayable/Hex* code per hex. See ClassifyHex for the predicate and
        // the game source behind every term.
        private readonly Dictionary<object, List<byte>> _tileWhyByMap = new();
        private readonly List<List<byte>> _tileWhyPool = new(); // rented in lockstep with _tileListPool
        private readonly List<bool> _tileTaken = new();         // greedy snap: hex already claimed
        private readonly List<Vector3> _tileScratch = new();    // this room's in-footprint hexes
        private readonly List<Vector3> _tilePlayScratch = new();// …of those, the PLAYABLE ones
        private readonly List<int> _roomTileCount = new();      // hexes usable per room (diag)
        private readonly List<int> _roomTileTotal = new();      // hexes on the room's CMap (diag)
        // ModBuild 259 per-room playability census — one counter per REASON, so the next log says
        // which term did the work rather than only that something did.
        private readonly List<int> _roomTileFootprint = new();  // in-footprint hexes, before playability
        private readonly List<int> _roomTilePlayable = new();   // …of those, the playable ones
        private readonly List<bool> _roomPlayableUsed = new();  // false = filter held back (see below)
        private readonly List<int> _roomCutEdge = new();        // EFlags.Edge on the hex
        private readonly List<int> _roomCutBlockedFlag = new(); // EFlags.Blocked on the hex
        private readonly List<int> _roomCutNodeBlocked = new(); // CNode.Blocked (obstacle/entrance prop)
        private readonly List<int> _roomCutNotWalkable = new(); // CNode.Walkable false, flags clean
        private readonly List<int> _roomTileUnreadable = new(); // no verdict — KEPT (fail-open)
        private readonly List<float> _roomSnapMax = new();      // worst lattice→hex move, wu (diag)
        private readonly List<float> _roomSnapSum = new();      // summed move, wu (diag → mean)
        private readonly List<bool> _roomTileGrid = new();      // false = fell back to the bounding box
        private int _sampleGridCells;                           // grid*grid this rescan (diag)
        private int _tilesResolved, _tilesUnkeyed;              // hex census this rescan (diag)
        private bool _tileSourceLive;                           // the game's tile registry answered
        private string _lastSampleCensus = string.Empty;        // change-gated census line
        // GENERALITY INSTRUMENTS (ModBuild 262 lane F — measurement only, no fade behaviour
        // touched). Reused scratch so the census allocates nothing per rescan: one set for
        // "which CMaps of the registry actually got a sample grid" (F8), one set + histogram
        // for the biome distribution (F4). Both are read at the existing census cadence.
        private readonly HashSet<object> _censusMapsSampled = new();
        private readonly HashSet<object> _censusMapsSeen = new();
        private readonly Dictionary<string, int> _censusBiomes = new();
        private readonly bool[] _sampleVisible = new bool[MaxTotalSamples]; // per-frame frustum flags
        private readonly Dictionary<MeshRenderer, float> _floorYByRenderer = new(); // volume anchors
        private readonly List<float> _floorYScratch = new();    // median fallback scratch
        private readonly List<Material> _matScratch = new();
        private readonly System.Text.StringBuilder _diagSb = new();
        private float _nextDiagTime;

        // [Optimize] WallFadeEvalInterval state: when the visibility/coverage DECISION last ran and
        // what it last reported (the fade + material writes keep running every frame regardless).
        private float _nextEvalTime;
        private float _lastEvalTime;
        private int _lastVisibleCount;
        private MaterialPropertyBlock? _mpb;

        private Texture2D? _noiseTex;    // transition dissolve pattern (r in [0.06,1], a=0)
        private Texture2D? _occludedTex; // held-faded constant (r=1, a=0 → map term m = 0)

        private float _nextRescan;

        /// <summary>
        /// The cadence value the cycle in flight (or the last one) was SCHEDULED with — the
        /// number <c>_cycleOpenedEarly</c> must be judged against, never the live dial. See the
        /// long note at the scheduling site for the two ways reading the live value goes wrong
        /// and which of them silently defeats the PERF S5 skip outright.
        ///
        /// <para>Starts at 0 on purpose. The first cycle of a session is judged against
        /// <c>_lastCycleOpenedAt = float.NegativeInfinity</c>, so its gap is +Infinity and the
        /// comparison is false whatever this holds; there is no value that could make a
        /// first-of-session cycle read as asked-for, and 0 states that rather than pretending
        /// to a cadence no cycle was ever scheduled with.</para>
        /// </summary>
        private float _scheduledRescanInterval;

        /// <summary>
        /// ModBuild 278 — the walk-in suspension latch, as it stood at the END of the last tick.
        /// See <see cref="UpdateSamplingSuspension"/>.
        /// </summary>
        private bool _samplingSuspended;

        /// <summary>Counters carried across the suspension so its two edge lines can state what
        /// was actually stood down and what it saved, rather than what it intended to.</summary>
        private int _suspendEdges;
        private float _suspendedSince;
        private int _suspendedEvaluations;
        private int _suspendedCycles;
        /// <summary>Shadow cadence clock used ONLY to count the rescan ticks a stand-down
        /// swallowed. The real <c>_nextRescan</c> must stay in the past while suspended (that is
        /// what makes the release immediate), so it cannot double as this counter's clock.</summary>
        private float _suspendedNextTick;

        /// <summary>Rescan scratch for the two registry reads that replaced the rescan's
        /// <c>FindObjectsOfType&lt;TilesOcclusionVolume&gt;</c> and
        /// <c>FindObjectsOfType&lt;UnityGameEditorDoorProp&gt;</c> walks (PERF S1 — see
        /// <see cref="SceneRegistry"/>). Reused, so the swap allocates nothing per rescan
        /// where the sweeps allocated a fresh array each time.</summary>
        private readonly List<TilesOcclusionVolume> _volumeScratch = new();
        private readonly List<UnityGameEditorDoorProp> _doorPropScratch = new();

        // ==================================================================================
        // PERF S2 (2026-08-23) — THE 118 ms RESCAN.
        //
        // USER REPORT, verbatim: "Ich hab nun mal eine Map aufgemacht mit vielen Details und
        // hab dort zum Testen alle Räume aufgemacht. Ich merke deutliche Laggs wenn ich alle
        // Räume von oben anschaue. In VR ist dieses überblickende 'von oben schauen' sehr
        // wichtig, dass es möglich ist. Doch es waren schon sehr starke Laggs, dass ein
        // flüssiges Spielen nicht möglich ist."
        //
        // THE MEASUREMENT (ModBuild 226 hardware log, .planning/debug/Player.log). Every
        // [Perf] STEPS window reported
        //     WallFade.Rescan 115.6–118.2 ms avg, worst 141.18 ms, ~59 ms/s, frames 12–15
        // and every [Perf] SPLIT window in the same session reported
        //     STALLS: 13–16 logic frame(s) over 100 ms in this window.
        // Fifteen rescans, fifteen stalls, one per two seconds. At 90 Hz each one drops about
        // ten frames. The stall was ENTIRELY ours; the SPLIT line's stock explanation ("a
        // synchronous scene load, an asset-bundle decompress or a room regeneration, NOT
        // steady-state cost") is wrong for this session and misled nobody only because the
        // STEPS line named the step outright.
        //
        // WHERE THE 118 ms WENT. The rescan took ONE full-scene
        // FindObjectsOfType<Renderer>() (O(every loaded object), not O(matches)) and then
        // walked the resulting 8630-renderer array FOUR MORE TIMES — once for
        // AdoptShaderMatchedWalls, once for CollectWaterFeatures, once for
        // CollectStackCandidates, once for CollectWallMountedProps. Each of those walks
        // re-derived, per renderer, facts that are properties of the renderer and not of the
        // caller:
        //   * GetSharedMaterials + shader-name tests — THREE to FOUR times per renderer per
        //     rescan (wall-fade family, foliage family, water family);
        //   * r.name — an interop STRING ALLOCATION, three times per renderer per rescan
        //     (IsModObject in three of the four passes, plus the water name-token family);
        //   * m.shader.name — another interop string allocation per material, taken by the
        //     water test for EVERY scene renderer;
        //   * r.bounds — a native call per renderer per pass.
        // Twenty-six thousand interop string allocations and ~35 000 native material fetches
        // per rescan, for a scene whose fade-capable renderer count is 848.
        //
        // THE SHAPE OF THE FIX. Three levers, in order of what they bought:
        //  1. ONE CENSUS. The scene array is classified exactly ONCE per rescan cycle into the
        //     RendererFact table below, and all four passes read that table. Nothing about
        //     WHICH renderers a pass sees changes — the table stores the same predicates the
        //     passes used to compute inline, evaluated with the same code (IsModObject,
        //     RendererUsesWallFade, RendererUsesFoliage, the water test,
        //     IsMountableRendererType), from the same array, in the same order.
        //  2. A PER-FRAME BUDGET. The census is a PURE READ — it touches no segment, no
        //     renderer, no material — so it can be spread across frames with a resumable
        //     cursor without ever exposing a half-built segment table (the precedent in this
        //     codebase is the ≤400 transforms/frame sweep). Only the COMMIT stage mutates,
        //     and it runs whole, in one frame, exactly as the old rescan did.
        //  3. AN EVENT-DRIVEN SWEEP. FindObjectsOfType itself cannot be sliced, so it is taken
        //     only when the scene's STRUCTURAL SIGNATURE moved (room reveal, a new wall in the
        //     wall cache, a new map tile / door prop / occlusion volume) or when the snapshot
        //     aged past SnapshotMaxAgeSeconds. Between those, the same array is re-classified
        //     — which re-reads every renderer's live bounds and enabled flag, so nothing that
        //     MOVED is stale; the only thing a reused snapshot cannot see is a renderer that
        //     was CREATED since it was taken. See SnapshotMaxAgeSeconds for the bound on that
        //     and for why FastReclaimRegeneratedShell already covers the case that matters.
        // ==================================================================================

        /// <summary>
        /// One scene renderer's rescan-relevant classification, computed ONCE per cycle by
        /// <see cref="ClassifySlice"/> and read by all four collection passes.
        ///
        /// <para>WHY A STRUCT ARRAY. The table is walked three more times after it is built
        /// (stack candidates, mounted dressing, and the wall adoption index), and those walks
        /// must be pure managed float compares — the whole point is that the native calls
        /// happen once. A struct array keeps the walk cache-linear and allocation-free; the
        /// table is grown, never reallocated per cycle.</para>
        ///
        /// <para>WHAT IS AUTHORITATIVE AND WHAT IS A PREFILTER. <see cref="Bounds"/> is a
        /// SNAPSHOT taken during the census slices, i.e. up to a handful of frames before the
        /// commit. It is used ONLY to reject candidates cheaply (the ground band and the
        /// union-reach rects) — every bound that ends up inside a segment's decision AABB is
        /// re-read LIVE from the renderer in the commit stage, exactly as before. Scenery does
        /// not move between two frames; figures do, and figures are excluded on their own
        /// account by <see cref="IsFigureOrActorRenderer"/> long before geometry is consulted.
        /// </para>
        /// </summary>
        private struct RendererFact
        {
            public Renderer? R;
            /// <summary>Non-null iff <see cref="R"/> is a MeshRenderer (the type test the
            /// adoption and stack passes ran inline).</summary>
            public MeshRenderer? Mesh;
            public Bounds Bounds;
            /// <summary>The ANCHOR POINT the mounted/stacked prefilters test — exactly the
            /// quantity those passes compute inline: <c>transform.position</c> for a
            /// ParticleSystemRenderer (round-13: a particle system is anchored by its EMITTER,
            /// never by its live plume bounds, which drift every frame) and
            /// <c>(bounds.center.x, bounds.min.y, bounds.center.z)</c> for anything else, so
            /// <c>Anchor.y</c> IS the <c>anchorY</c> both passes use.</summary>
            public Vector3 Anchor;
            /// <summary>Fixed for a renderer's lifetime, so a census verdict on it can never
            /// go stale: the mod LAYER and the 'GloomhavenVR.' name prefix are both stamped at
            /// creation. (<c>enabled</c> is deliberately NOT cached — the game flips it at
            /// will, and a stale <c>enabled</c> used as a reject would NARROW a candidate set.
            /// Every pass that cares reads it live.)</summary>
            public bool Mod;
            public bool WallFadeShader;
            public bool FoliageShader;
            public bool WaterSurface;
            public bool Particles;
            public bool Mountable;
            /// <summary>ModBuild 278 — the renderer's name, kept as a REFERENCE to the string
            /// <see cref="ClassifyMaterialsAndName"/> has already allocated for the mod-object
            /// and water-family tests. It costs a field and no interop call, and it is the only
            /// way the signature-culprit census can name a renderer that has since been
            /// DESTROYED (see WallSegmentFadeCulprits: a leaver cannot be asked its own name).
            /// Re-read only on a COLD classify, exactly like every other verdict here.</summary>
            public string? Name;
            /// <summary>ModBuild 279 (Option A) — is this renderer one the round-7 ruling puts
            /// beyond every adoption lane's reach, i.e. <c>IsFigureOrActorRenderer</c> AND NOT
            /// <c>IsWallGeneratedDressing</c>?
            ///
            /// <para>READ LIVE EVERY CYCLE, not cached on a cold classify like the seven verdict
            /// bits above it. Those are properties of the renderer's own components and
            /// materials; this one is a property of its ANCESTRY, which the game can change
            /// under us by reparenting. A stale TRUE here is the one error that costs something
            /// (see the narrowed signature) so it is the one that is never allowed to
            /// persist.</para>
            ///
            /// <para>WHY THE SECOND CONJUNCT IS NOT OPTIONAL — this is a correction to the
            /// design document, made against the source. The mounted sweep's two
            /// <c>IsWallGeneratedDressing</c> arms (the non-mountable structural skip and the
            /// round-7 guard itself) adopt a renderer that IS a figure when that predicate holds
            /// (ModBuild 266, the cloth post under 'Wall 4/Generated Content'), and
            /// <c>PurgeFigureRenderers</c> carries the identical exemption, so such a renderer
            /// legitimately survives in the table and its liveness DOES decide the commit's
            /// output. Exempting it from the signature would drop a real membership change.
            /// </para></summary>
            public bool Figure;
        }

        /// <summary>The scene snapshot this cycle is classifying (the array
        /// <c>FindObjectsOfType&lt;Renderer&gt;</c> returned). Reused across cycles when the
        /// structural signature has not moved — see the PERF S2 note above.</summary>
        private Renderer[] _snapshot = System.Array.Empty<Renderer>();

        /// <summary>Classification of <see cref="_snapshot"/>, index for index. Grown to fit,
        /// never shrunk — a rescan must not allocate.</summary>
        private RendererFact[] _facts = System.Array.Empty<RendererFact>();

        /// <summary>How many entries of <see cref="_facts"/> are live this cycle.</summary>
        private int _factCount;

        /// <summary>Indices into <see cref="_facts"/> of the MeshRenderers carrying a wall-fade
        /// shader — the adoption pass's whole input, in snapshot order (the order decides which
        /// renderer seeds a group's anchor, so it is preserved exactly).</summary>
        private readonly List<int> _factWallFade = new();

        /// <summary>Indices into <see cref="_facts"/> of the water surfaces — the water pass's
        /// whole input, in snapshot order.</summary>
        private readonly List<int> _factWater = new();

        /// <summary>Water-shader verdict per Shader, the same per-Shader memo
        /// <see cref="_shaderVerdict"/> and <see cref="_shaderFoliageVerdict"/> use. Before
        /// this existed the water test read <c>m.shader.name</c> — an interop string
        /// allocation — for every material of every scene renderer, every rescan. See
        /// <see cref="IsWaterShader"/>.</summary>
        private readonly Dictionary<Shader, bool> _shaderWaterVerdict = new();

        /// <summary>The two name tests <see cref="CollectWallFadeInfo"/> runs, plus the name
        /// itself, cached per Shader.
        ///
        /// <para>PERF S4. That method is the choke point every wall-renderer collection path
        /// goes through, and it read <c>m.shader.name</c> — an interop STRING ALLOCATION — for
        /// every material of every child renderer of every cache wall, every commit, and then
        /// ran two <c>Contains</c> over it. On the ModBuild 271 scenario that is thousands of
        /// allocations per commit thrown away immediately. A shader's name is immutable, so one
        /// entry per Shader answers all of them; the cache is the same shape and the same
        /// lifetime as <see cref="_shaderVerdict"/> and <see cref="_shaderFoliageVerdict"/>
        /// beside it, and the verdicts are the SAME two expressions, not equivalents.</para></summary>
        private readonly Dictionary<Shader, ShaderFadeName> _shaderFadeName = new();

        /// <summary>One shader's cached name facts — see <see cref="_shaderFadeName"/>.</summary>
        private readonly struct ShaderFadeName
        {
            internal readonly string Name;
            internal readonly bool ByName;
            internal readonly bool Low;

            internal ShaderFadeName(string name)
            {
                Name = name;
                ByName = name.Contains("WallFade");
                Low = name.Contains("Low");
            }
        }

        /// <summary>The per-Shader name facts, derived on first sight.</summary>
        private ShaderFadeName FadeNameOf(Shader sh)
        {
            if (!_shaderFadeName.TryGetValue(sh, out ShaderFadeName info))
            {
                info = new ShaderFadeName(sh.name);
                _shaderFadeName[sh] = info;
            }
            return info;
        }

        private enum RescanStage
        {
            /// <summary>No cycle in flight; the segment table is the last committed one.</summary>
            Idle,
            /// <summary>Walking <see cref="_snapshot"/> with <see cref="_classifyCursor"/>,
            /// filling <see cref="_facts"/>. Pure reads — nothing is mutated.</summary>
            Classify,
            /// <summary>PERF S5: re-deriving the CHEAP membership signature of everything the
            /// commit's output depends on, so a cycle whose inputs have not moved can be
            /// skipped outright. Pure reads; it keeps nothing but one 64-bit hash. See
            /// WallSegmentFade.Prepare.cs, THE SKIP INVARIANT.</summary>
            Survey,
            /// <summary>PERF S4: warming the commit's expensive derivations off the commit
            /// frame. Pure reads, and the ONLY thing it keeps is memo entries — see
            /// WallSegmentFade.Prepare.cs and THE PREPARE INVARIANT stated there.</summary>
            Prepare,
            /// <summary>Census complete; the next frame runs the (now cheap) mutation passes
            /// whole, in one frame.</summary>
            Commit,
        }

        private RescanStage _rescanStage = RescanStage.Idle;
        private int _classifyCursor;
        /// <summary>COLD = this cycle took a fresh sweep, so every fact must be derived from
        /// scratch (materials, names, types). WARM = the snapshot was reused, so only the
        /// values that can change without the renderer being recreated are refreshed: liveness,
        /// <c>enabled</c> and bounds. A renderer's shader family and name do not change over
        /// its lifetime; its transform does, every frame.</summary>
        private bool _classifyCold;

        /// <summary>
        /// Set whenever the wall system REASSIGNS a renderer's <c>sharedMaterials</c> — the
        /// dissolve swap (<c>WallSegmentFade.Dissolve.cs</c>) puts body meshes onto copies of
        /// the masonry fade shader and puts the authored array back on unfade. That is the one
        /// way a renderer's SHADER FAMILY can change without the renderer being recreated, so
        /// it is the one thing that can make a warm census's cached
        /// <c>WallFadeShader</c>/<c>FoliageShader</c>/<c>WaterSurface</c> verdicts wrong. The
        /// next cycle re-derives every fact from scratch — the budget absorbs it, and the
        /// alternative (a warm cycle carrying a stale shader verdict for up to
        /// <see cref="SnapshotMaxAgeSeconds"/>) would be a look change, which this round
        /// forbids. Static because the restore path is static; there is one driver.
        /// </summary>
        private static bool _censusMaterialsDirty;

        private bool _rescanUrgent;
        private float _snapshotTakenAt = float.NegativeInfinity;
        private int _snapshotSignature = -1;

        /// <summary>Millisecond budget the census may spend on ONE frame. 1.5 ms against an
        /// 11.11 ms budget leaves the frame intact; the cycle simply takes more frames. The
        /// old code spent 118 ms on one frame and dropped ten.</summary>
        /// <para>ModBuild 281 (PERF B): this was a <c>private const float … = 1.5f</c>. It is now
        /// the live <c>[WallFade] SliceBudgetMillis</c> dial, shipped at the same 1.5 — a tuning
        /// surface, not a retune, so a fresh install and an install that never opens the menu
        /// behave exactly as ModBuild 280 did. The three budgets this replaced were three
        /// separate constants whose own doc comments said they were deliberately the same number
        /// for the same reason; one dial is that statement made enforceable. It will also be the
        /// budget of the SLICED COMMIT when that lands, which is why it is named for the slice
        /// and not for any one stage.</para>
        private static float ClassifyBudgetMillis => WallFadeTuning.SliceBudget;

        /// <summary>Budget for a cycle triggered by a ROOM REVEAL rather than by the timer.
        /// A reveal invalidates the room registry outright and walls of an unanchored room are
        /// held fail-safe SOLID until it re-anchors, so a leisurely census there would show as
        /// "the walls stopped fading for a moment after the door opened". A reveal already
        /// coincides with the game's own room-generation hitch, so this is the one place where
        /// spending more is free.</summary>
        private const float ClassifyUrgentBudgetMillis = 6f;

        /// <summary>How many facts to classify between two clock reads. The Stopwatch read is
        /// itself ~20 ns, so checking it per renderer would be a measurable share of a walk
        /// whose per-item cost is what this whole change is about.</summary>
        private const int ClassifyChunk = 256;

        /// <summary>
        /// How stale a reused scene snapshot may get before a fresh
        /// <c>FindObjectsOfType&lt;Renderer&gt;</c> is taken regardless of the structural
        /// signature.
        ///
        /// <para>WHAT A REUSED SNAPSHOT CAN MISS, EXACTLY. Only a renderer CREATED since the
        /// sweep. Destroyed ones are caught by the per-fact null check; moved ones by the warm
        /// re-classification, which re-reads bounds every cycle. Every creation path this
        /// system cares about moves the structural signature and takes a fresh sweep on the
        /// spot: a room reveal (m_RoomRenderers), a new wall (ProceduralWall.m_WallCache), a
        /// new map tile / door prop / occlusion volume (the SceneRegistry counts), a scene load
        /// (OnSceneLoaded resets the cycle). The one path that does NOT is Apparance
        /// REGENERATING content under an existing tile — and that is precisely the case
        /// <see cref="FastReclaimRegeneratedShell"/> exists for, which sweeps on its own 0.25 s
        /// cadence while any stack-carrying wall is held faded. This value bounds everything
        /// else at six seconds, three rescans' worth, against the two seconds it used to be.
        /// </para>
        /// </summary>
        private const float SnapshotMaxAgeSeconds = 6f;

        /// <summary>
        /// Slack applied to every geometric test that reads a CENSUS bound rather than a live
        /// one, so a cached AABB can only ever REJECT what the live test would also reject.
        ///
        /// <para>WHY IT IS SAFE AT THIS SIZE. A fact's bounds are at most one census's worth of
        /// frames old — a handful of frames at 90 Hz, a fraction of a second at the 18 fps this
        /// scenario actually runs at. The renderers these prefilters gate are static scenery
        /// (masonry courses, sconces, banners, shell stories): they do not move at all. The
        /// things that DO move are figures and their accessories, and those are excluded by
        /// <see cref="IsFigureOrActorRenderer"/> on their own account, before any geometry is
        /// consulted. 1 wu is a whole ground-exclusion band — far more than anything static can
        /// travel in that window — and every candidate that survives a cached prefilter is
        /// re-tested against its LIVE bounds before it can be claimed.</para>
        /// </summary>
        private const float CensusBoundsSlackWU = 1.0f;

        /// <summary>Cycle accounting for the one attributable diagnostic line
        /// (<see cref="LogRescanBudget"/>) — reset every time that line is printed.</summary>
        private int _cycleCount;
        private int _cycleSweeps;
        private int _cycleClassifyFrames;
        private float _cycleWorstFrameMillis;
        private float _cycleWorstCommitMillis;
        private float _cycleWorstSweepMillis;
        private float _nextBudgetLogTime;
        private bool _budgetLoggedOnce;

        /// <summary>PERF S4 — the commit's own phase accounting, so the budget line can tell
        /// "sliced and now costs 1.5 ms/frame" from "sliced and one phase still costs 60 ms"
        /// without another round. These name the single most expensive phase inside the worst
        /// commit of the window.
        ///
        /// <para>ModBuild 284 — <c>_cycleCommitFrames</c> WAS HERE AND IS GONE, and the reason
        /// is a record rather than a tidy-up. It was a field initialised to 1 and never assigned
        /// anywhere, printed as <c>COMMIT SPREAD: {n} frame(s)</c> — a literal wearing a
        /// counter's clothes, which is this project's "an instrument shipped and lying" shape in
        /// miniature. The clause beside it already states the same fact honestly
        /// ("N of N phase(s) still run ATOMICALLY"). If PERF B build 2 ever lands and the commit
        /// really is spread, the counter comes back as a counter — incremented at the slice
        /// boundary — and not as an initialiser. See .planning/perf/WALL-COMMIT-B-BUILD2.md.</para></summary>
        private int _cycleWorstCommitPhase = -1;
        private float _cycleWorstCommitPhaseMillis;

        /// <summary>True between <c>BeginPrepareStage</c>'s <c>BeginStandingPropScope</c> and
        /// the commit that consumes it. The commit asserts it: a commit that reached the wall
        /// cache with no scope open would be running the standing rule against the PREVIOUS
        /// rescan's verdict memos, which is a statue the wall system may claim as masonry.</summary>
        private bool _standingScopeOpen;

        /// <summary>How many commits had to open the standing scope themselves because the
        /// prepare stage had not — a FAILURE count, printed whatever its value.</summary>
        private int _cycleScopeSelfOpened;

        /// <summary>How often the budget line prints. Deliberately NOT per cycle (that would
        /// be one line every two seconds in a 9 MB log) and deliberately NOT change-triggered:
        /// a sweep that only logs when it finds something hides that it never ran, and this
        /// project has paid for that lesson. It prints on the FIRST completed cycle and then
        /// every five seconds, whatever it found — including nothing.</summary>
        private const float BudgetLogIntervalSeconds = 5f;

        /// <summary>Shared clock for the per-frame budget. One instance, started once — a
        /// Stopwatch that is allocated per slice would be its own cost.</summary>
        private static readonly System.Diagnostics.Stopwatch RescanClock =
            System.Diagnostics.Stopwatch.StartNew();

        // Perspective-change tracking (arms aggressive re-evaluation for ReevalArmSeconds).
        private float _lastReevalTime = float.NegativeInfinity;
        private int _lastPoseVersion = -1;
        private Vector3 _headAnchor;      // head localPosition (tracking-space meters)
        private bool _headAnchorInit;
        private Vector3 _rigPos;          // rig-root snapshot (world-grab / snap-turn detection)
        private Quaternion _rigRot;
        private float _rigScale;
        private bool _rigSnapInit;
        private Vector3 _roomCenter;      // combined room-bounds center (board-move detection)
        private bool _roomCenterInit;
        private bool _roomBoundsMoved;
        private bool _wasActive;
        private bool _failureLogged;
        private bool _heartbeatLogged;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy() => Teardown();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Scenario scenes are additive; walls/volumes stream in — rescan promptly. Old
            // renderers die with their scene, so blocks need no explicit clearing here.
            _nextRescan = 0f;
            _live.BuiltRoomCount = -1;
            _heartbeatLogged = false;
            _nextDiagTime = 0f;
            _shaderVerdict.Clear(); // scene shaders died with their bundles — no dead keys
            _shaderWaterVerdict.Clear(); // …and so did the water shaders (PERF S2)
            AbandonRescanCycle();   // a census of the OLD scene may never commit into the new one
            _live.SplitAnchors.Clear();
            _runs.Clear();          // …and so do the split-run verdicts keyed off those anchors
            _lastRoomCensusCount = -1; // fresh scene = fresh room registry (reveal diagnostics)
            _lastRoomCensusAnchored = -1;
            // A cached board volume belongs to the scene it was measured in: judging a new
            // scenario's head position against the old scenario's box could stand the fade
            // policy down for the frames between the load and the first commit.
            InvalidateBoardVolume();
            _lastLoggedMountedCount = -1;    // re-print the dressing census for the new scene
            _lastLoggedMountedRejected = -1;
            _lastLoggedStackedCount = -1;    // …and the stacked-shell census
            _lastLoggedStackedRejected = -1;
            _nextFastReclaim = 0f;           // fresh scene = fresh regen-churn sweep state
            _fastReclaimTotal = 0;
            _nextFastReclaimLog = 0f;
            _heartbeatFadeRenderers = -1;
            _lastLoggedFigureGuarded = -1;   // re-print the figure-guard proof line
            _live.CornerPieces.Clear();           // corner ownership dies with the scene
            _lastLoggedCornerCount = -1;
            _dumpedBodyShaders.Clear();      // re-dump body shader properties per scene
            _gateSliverLogged.Clear();       // re-log sliver-skipped gates per scene
            _ownershipChanges.Clear();       // fresh churn ledger per scene
            _masonryFadeShader = null;       // re-capture the dissolve-swap template
            _swapTotal = 0;
            _nativeTotal = 0;
            _nextSwapLog = 0f;
            _live.ArchRects.Clear();              // arch protection dies with the scene…
            _gateMemory.Clear();             // …and so does the reborn-gate state memory
            _live.WaterRects.Clear();             // …and the fountain/pond protection rects
            _waterCensusSig = -1;            // …so the next scenario re-prints its census
            _lastLoggedReanchorCount = -1;   // re-print the re-anchor census
            _lastLoggedSeamCount = -1;       // …and the room-seam census (2026-08-09)
            _peerFades.Clear();              // peers re-state their fades for the new scene
            _loggedToggleMats.Clear();       // …and the toggle-native material lines
            _segmentListedIndex.Clear();     // scene renderers died with the scene — no dead keys
            _volumeScratch.Clear();
            _doorPropScratch.Clear();
        }

        /// <summary>
        /// LateUpdate on purpose: runs after every game Update, so the generator's renderer
        /// lists and the head pose are final for this frame. Fully guarded — a throw here must
        /// never starve the game loop (WorldUI lesson).
        /// </summary>
        private void LateUpdate()
        {
            // FRAME-ORDER WallSegmentFade.FadeDriver.LateUpdate LateUpdate-required [WallFade.Late]
            //   LateUpdate is the requirement, not a preference — see the doc comment above.
            //   Machine-checked so a later "all drivers tick in Update" tidy-up fails at commit
            //   time instead of producing a fade that samples last frame's head pose.
            try
            {
                // Perf attribution (2026-07 perf pass): the per-segment visibility sweep walks
                // every wall's floor samples against the head pose EVERY FRAME, which makes it a
                // prime suspect for head-motion-correlated cost — so it gets its own measured
                // scope. The scope never alters the try/catch semantics around it.
                using (PerfMonitor.Scope("WallFade.Late"))
                    Tick();
            }
            catch (Exception e)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    VRLog.Warn(Name, $"driver tick threw (logged once): {e}");
                }
            }
        }

        private void Tick()
        {
            Camera? head = Rig.VRRigDriver.HeadCamera;
            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            bool active = Enabled && VRSession.IsRunning && head != null && gen != null;
            if (!active)
            {
                // Toggled off / no scenario: revert to exactly-solid immediately.
                if (_wasActive)
                {
                    ClearAllBlocks("inactive (toggle off / no scenario / no head)");
                    // The INSIDE-the-map verdict describes a board that is no longer being
                    // watched; a stale YES would stand the fade policy down on the next
                    // scenario's very first frames.
                    ResetInsideBoardState();
                    // PERF S2: a census in flight is measured against a scene we are no longer
                    // watching — resume would commit it blind. Drop it and its snapshot; the
                    // next active tick opens a fresh cycle.
                    AbandonRescanCycle();
                    // ModBuild 262: a wall-path audit half-way through its wall list is measuring
                    // a scene we have stopped watching, and resuming it after a scenario change
                    // would mix two scenes into one reading. Drop it; the next active tick opens
                    // a fresh pass.
                    _pathAuditRunning = false;
                    _pathAuditWalls.Clear();
                    _nextPathAudit = 0f;
                    // ModBuild 284: and the BACKOFF goes with it. A converged audit from the
                    // previous scenario must not decide the cadence of the next one's first
                    // pass — that is the same "a gate outliving its edge" shape the rest of
                    // this block exists to close.
                    _pathAuditStableRuns = 0;
                    // NOT _pathAuditTallySig = 0 — a hash of 0 is a hash a real tally could
                    // legitimately produce, and an accidental match would hand the next
                    // scenario's first pass a stable run it never earned. The VALIDITY flag is
                    // what is dropped. _pathAuditPasses is deliberately NOT reset either: its
                    // line says "Pass N of this SESSION", and resetting it would quietly change
                    // what a printed number means.
                    _pathAuditTallyValid = false;
                    _pathAuditInterval = InsideLogIntervalSeconds;
                    // ModBuild 278: the walk-in latch is already dropped by ResetInsideBoardState
                    // above; the suspension it authorises must go with it, or the rescan-cadence
                    // gate would still be closed when the next scenario's very first tick asks
                    // for a table. See ReleaseSamplingSuspension — the flag is read EARLY in the tick
                    // and written LATE, so it cannot be left to unwind itself.
                    ReleaseSamplingSuspension(Time.unscaledTime,
                        "the subsystem went inactive (toggle off, no scenario, or no head)");
                }
                _wasActive = false;
                return;
            }
            _wasActive = true;

            float now = Time.unscaledTime;
            bool sweptThisFrame = false;
            // PERF S2: the rescan is a three-stage pipeline now (sweep → budgeted census →
            // commit), not a single 118 ms call. A cycle is only STARTED when none is in
            // flight, so the reveal edge below cannot re-trigger every frame while the census
            // is still walking — _live.BuiltRoomCount is not updated until the commit runs.
            // ModBuild 278 — WALK-IN SUSPENSION, the cadence half. See UpdateSamplingSuspension
            // for the whole record; here it does exactly one thing: while the latch holds, no
            // NEW cycle is opened on the cadence. A REVEAL still opens one (the second clause
            // below is untouched by the gate) because a room the game has just revealed is a
            // table the mod does not have, and a stand-down that shipped a missing room would
            // be a correctness bug wearing a performance fix's clothes.
            //
            // WHY _nextRescan IS DELIBERATELY LEFT IN THE PAST while suspended, rather than
            // being pushed forward: that is what makes the RELEASE EDGE IMMEDIATE for free. On
            // the first frame after the latch drops, `now >= _nextRescan` is already true by a
            // wide margin, so the cycle opens that frame with no forced commit and no special
            // release path to get wrong. It also keeps _cycleOpenedEarly honest — the gap since
            // the last cycle is then LARGER than the cadence, which is the opposite of "asked
            // for", so a long stand-down cannot be mistaken for a regeneration request.
            if (_rescanStage == RescanStage.Idle
                && ((!_samplingSuspended && now >= _nextRescan)
                    || gen!.m_RoomRenderers.Count != _live.BuiltRoomCount))
            {
                // PERF S5 — DID SOMETHING ASK FOR THIS CYCLE? Six sites across the subsystem
                // zero _nextRescan to mean "geometry regenerated mid-fade, re-collect promptly"
                // (WallSegmentFade.cs, Body, Stacked x2, PropUnit, Mounted). Zeroing makes the
                // cycle look DUE rather than early, so the request cannot be read off
                // _nextRescan itself — it is read off the GAP instead: a cycle that opened
                // sooner than the cadence allows was asked for, and an asked-for cycle always
                // commits. Four of those six sites live in files this lane does not own, which
                // is exactly why the detector is on this side of the call.
                //
                // ModBuild 278 — THE COMPARISON READS THE INTERVAL THIS CYCLE WAS SCHEDULED
                // WITH, NOT THE LIVE ONE, and that distinction is the whole reason
                // _scheduledRescanInterval exists. The cadence became a live config dial in
                // this build, so the two can differ by any amount at the instant the player
                // moves the stepper. Comparing the gap against the LIVE value gets it wrong in
                // both directions and one of them is catastrophic:
                //   * dial RAISED (2 -> 8) — the cycle was scheduled 2 s ago, the gap is 2 s,
                //     and 2 < 7.95 reads TRUE. One ordinary cycle is misread as asked-for and
                //     commits. Harmless: one extra ~90 ms frame, once, on the frame the player
                //     changed the setting.
                //   * dial LOWERED (8 -> 2) — the gap is 8 s, 8 < 1.95 is false. Also harmless.
                // But the shape that would be catastrophic is the same bug written the other
                // way round: if the gap were ever routinely BELOW the value it is compared
                // against, _cycleOpenedEarly would be true on EVERY cycle, every cycle would be
                // "asked for", and the PERF S5 skip — 80 of 113 cycles in the ModBuild 277 log
                // — would fire exactly never. That is a 71 % regression that logs nothing and
                // looks like "the perf work did not help". Latching the scheduling value makes
                // the comparison exact by construction instead of approximately right, and the
                // BUDGET line's "asked for" counter is the shipped falsifier: it read 0 across
                // the whole ModBuild 277 log and must still read 0 in a session where nothing
                // regenerates, whatever the dial is set to.
                _cycleOpenedEarly = now - _lastCycleOpenedAt < _scheduledRescanInterval - 0.05f;
                _lastCycleOpenedAt = now;
                _scheduledRescanInterval = RescanIntervalSeconds;
                _nextRescan = now + _scheduledRescanInterval;
                // A frame that had to take the FindObjectsOfType sweep has already spent more
                // than the budget allows, so it does no census work on top — the census starts
                // on the next frame. Nothing waits on it: the segment table in force is the
                // last committed one, exactly as it was between two old rescans.
                sweptThisFrame =
                    BeginRescanCycle(gen!, now, urgent: gen!.m_RoomRenderers.Count != _live.BuiltRoomCount);
            }
            // MEASURE THE OUTCOME, NOT THE READINESS OF THE MECHANISM. This counts the cadence
            // ticks the suspension actually swallowed, and the SAMPLING RESUMED line prints the
            // number — so "the suspension ran" is a figure in a hardware log rather than a claim
            // about a branch that may or may not have been reached.
            //
            // IT COUNTS CADENCE PERIODS, NOT FRAMES, and the difference is the whole value of
            // the number: _nextRescan is deliberately NOT advanced while suspended (that is what
            // makes the release edge immediate), so a naive "is a cycle due?" test would be true
            // on every one of the ~5400 frames a 60-second stand-down covers and would report a
            // saving 180x larger than the truth. A separate shadow clock advances at the live
            // cadence instead, so a 60-second stand-down at the shipped 2.0 s reports 30.
            if (_samplingSuspended && gen!.m_RoomRenderers.Count == _live.BuiltRoomCount)
            {
                float period = Mathf.Max(RescanIntervalSeconds, 0.05f);
                // Bounded: at most one whole window's worth of catch-up per frame, so a long
                // editor pause or a load-screen stall cannot spin here.
                for (int guard = 0; guard < 64 && now >= _suspendedNextTick; guard++)
                {
                    _suspendedCycles++;
                    _suspendedNextTick += period;
                }
            }

            if (_rescanStage != RescanStage.Idle && !sweptThisFrame)
                StepRescanCycle(gen!, now);
            if (_live.Segments.Count == 0 || _live.RoomBounds.Count == 0)
                return;

            Transform headT = head!.transform;
            Vector3 headPos = headT.position;
            UpdatePerspectiveState(headT, now);

            // INSIDE THE MAP (user report 2026-08-24: "wenn ich mich so klein mache, dass ich IN
            // der Map stehe, dann sollten alle Wände voll sichtbar sein"). A point against ONE
            // cached Bounds — see WallSegmentFade.Inside.cs for the test, its two bars, the
            // release-on-entry and why the fade criterion is right outside and blind inside.
            // Called EVERY frame and not on the evaluation cadence below: it is ~15 float ops,
            // and the boundary crossing is a deliberate act whose edge must not wait out a
            // skipped evaluation. rigScale is diagnostic ONLY — every bar in the test is in
            // world units, compared against world-unit geometry.
            float rigScale = headT.lossyScale.x;
            bool insideBoard = UpdateInsideBoard(headPos, now, rigScale);
            // WALK-IN STAND-DOWN (ModBuild 271, user request 2026-08-25: "Wenn ein Spieler IN das
            // Spielfeld geht weil er so nah ranzoomed und dann im Spielfeld ist … ausnahmslos
            // alle Wände sichtbar und nichts mehr faded"). The verdict above is STILL an
            // observation; this strictly narrower latch is the only thing in this subsystem that
            // gates a fade, and the term that makes a third attempt defensible is the board's
            // wall crest in REAL METRES — see UpdateWalkInside and the record on
            // WallSegmentFade.Inside.cs. Same frequency as the verdict it refines: it reads two
            // dials and four cached numbers, and its edge must not wait out a skipped evaluation.
            bool walkInside = UpdateWalkInside(now, rigScale);
            // ModBuild 278 — and the very next thing, so the latch and the suspension can never
            // be more than this one statement apart. See UpdateSamplingSuspension.
            bool suspended = UpdateSamplingSuspension(walkInside, now);

            // [Optimize] WallFadeEvalInterval (2026-07 perf pass). The expensive half of this tick
            // is the DECISION: UpdateSampleVisibility projects every room's floor samples through
            // the head camera and BlockedFraction re-measures every segment against them, every
            // frame — work that is by definition head-motion correlated. The cheap half is the
            // per-segment exponential FADE plus its material write, which must stay per-frame or
            // the fade would visibly step.
            //
            // So the interval gates the decision only; the fade keeps running at full rate toward
            // whatever the last decision was. That is safe by construction because the decision it
            // feeds is ALREADY deliberately slow — an EMA, a Schmitt trigger and second-scale dwell
            // hysteresis (see the thresholds below) — so sampling it at 20 Hz instead of 90 Hz
            // cannot change which walls fade, only when within a fraction of the dwell.
            //
            // DEFAULT 0 = every frame = today's behaviour; the [Perf] STEPS line's "WallFade.Late"
            // entry is what decides whether raising it is worth anything on real hardware.
            //
            // ModBuild 278 — THE DIAL MOVED HOUSE AND KEPT ITS OLD DOOR. The cadence now also
            // reads [WallFade] EvalIntervalSeconds, which is where a player will actually look
            // for it ("Bild & Darstellung", beside the rest of the wall see-through); the older
            // [Optimize] WallFadeEvalInterval still works and is what applies while the new one
            // is 0. See WallFadeTuning.EffectiveEvalIntervalSeconds for why a precedence and not
            // a max().
            bool evaluate = true;
            float evalInterval = WallFadeTuning.EffectiveEvalIntervalSeconds;
            // ModBuild 278 — WALK-IN SUSPENSION, the decision half (user request 2026-08-25:
            // "In dem Modus in dem man IM dem Level ist, kann das 'Abtasten' komplett
            // deaktiviert werden so lange man in dem Modus ist um hier auch Performance zu
            // sparen."). While the latch holds, the walk-in branch of the loop below forces
            // EVERY segment solid two branches before any coverage number is consulted, so
            // UpdateSampleVisibility and BlockedFraction are computing a verdict that is
            // overruled by decree in the same pass. Skipping them changes no pixel.
            //
            // WHAT IT DOES *NOT* SKIP, and this is the part that keeps the mode correct: the
            // per-segment fade ramp and its material write still run every frame, so the walls
            // the mode holds solid still come back through the ordinary ANIMATED un-fade rather
            // than snapping — which was the ModBuild 271 ruling and is not negotiable.
            if (suspended)
                evaluate = false;
            else if (evalInterval > 0f)
            {
                if (now < _nextEvalTime)
                    evaluate = false;
                else
                    _nextEvalTime = now + evalInterval;
            }
            if (suspended)
                _suspendedEvaluations++;
            int visibleCount = evaluate ? UpdateSampleVisibility(head!) : _lastVisibleCount;
            _lastVisibleCount = visibleCount;
            bool reevalArmed = now - _lastReevalTime <= ReevalArmSeconds;

            float fadeStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeTauSeconds);
            // The coverage EMA advances by the time since the last EVALUATION, not since the last
            // frame — otherwise skipping evaluations would silently stretch its time constant and
            // change the fade decision, which is exactly what the interval must NOT do.
            //
            // At interval 0 every frame evaluates, so this term is the frame delta again — measured
            // as a difference of two accumulated unscaledTime values rather than read from
            // unscaledDeltaTime, i.e. equal to within float noise, not bit-identical. The one real
            // difference is the 0.5 s clamp: after a long stall the old code fed the full stall
            // duration into the EMA (snapping the coverage almost to the raw sample), this one caps
            // the step. That is the safer direction for a hitch and cannot fire in steady state.
            float evalDt = evaluate
                ? (_lastEvalTime > 0f ? Mathf.Min(now - _lastEvalTime, 0.5f) : Time.unscaledDeltaTime)
                : 0f;
            if (evaluate)
                _lastEvalTime = now;
            float fracStep = 1f - Mathf.Exp(-evalDt / FractionTauSeconds);
            // Live thresholds (WallFadeTuning, clamped): tuning a stepper in the settings
            // panel re-shapes the Schmitt trigger / dwells on the very next evaluation.
            float onFraction = WallFadeTuning.On;
            float offFraction = WallFadeTuning.Off;
            float exitDwellMoved = WallFadeTuning.DwellMoved;
            float exitDwellStationary = WallFadeTuning.DwellStationary;
            // INSIDE THE MAP is still an OBSERVATION and still gates nothing — see the record
            // in WallSegmentFade.Inside.cs. Two builds tried to make THAT term a policy (a
            // raised bar in 241-250, a hard stand-down in 251) and both were rejected. What
            // gates, since ModBuild 271, is `walkInside`: the same verdict AND a real-metre
            // crest bar AND the HEIGHT slab AND a readable rig scale. Outside that mode the
            // per-wall metric below is the whole decision, exactly as before.
            BeginPerWallCensus();
            _lastHeadPos = headPos;
            // ModBuild 259 (user ruling 2026-08-24: "Entweder verschwindet die ganze Wand mit
            // ALLEM was dazu gehört … oder sie ist vollständig da"). A wall run that
            // NeutralizeEngulfingSegments carved into per-renderer pieces decides ONCE, on the
            // UNION of its pieces' blocked cells, and every piece takes that verdict. Runs BEFORE
            // the loop so each member's State is already the run's when its ramp and Apply run —
            // no one-frame skew between the trunk and the masonry beside it. Segments with no
            // RunOwner (every unsplit ProceduralWall: 'Wall 2', 'Wall 3', 'Wall 4' in the
            // ModBuild 258 log) never enter this method and keep the branch below verbatim.
            if (evaluate && WallFadeTuning.SplitRunUnifiedOn)
                EvaluateSplitRuns(headPos, now, fracStep, onFraction, offFraction,
                    reevalArmed ? exitDwellMoved : exitDwellStationary);
            else if (evaluate)
                ClearSplitRunDrive(); // dial off: every piece decides for itself, as in 258
            foreach (Segment seg in _live.Segments.Values)
            {
                // BOUNDLESS FAIL-SAFE (round 14 — user report: "Das Element über dem Rechteck
                // des Torbogens ist nun dauerhaft ausgeblendet und kommt auch nicht wieder,
                // obwohl es den Raum nicht verdeckt"). A segment without a decision AABB cannot
                // be judged, and the old `continue` skipped its ENTIRE tick — decision, fade
                // ramp AND Apply — so everything it had hidden stayed hidden with no path back:
                // a permanent latch, invisible in the diag (which skips boundless segments too)
                // and silent in the log (no state flip can happen if the state machine never
                // runs). That is the one thing the wall system may never do. A boundless
                // segment is now forced OFF and still runs the ramp + Apply below, so its
                // renderers, foliage, siblings, mounted props, stacked shell and body meshes
                // come back through the NORMAL animated un-fade. GATE COLUMNS are the class
                // that reaches this state (they legitimately own zero wall renderers, so every
                // pass that rebuilds an AABB from the renderer union can strip their bounds);
                // EnsureGateBounds re-anchors them from the arch seed at the next rescan, and
                // WatchLatch names anything that still disagrees.
                if (!seg.HasBounds)
                {
                    seg.State = false;
                    seg.PendingRaw = false;
                }
                // Undecidable-as-one-unit segments (see NeutralizeEngulfingSegments) are held
                // solid: state forced off, the fade below decays any residual block away.
                // DOORWAY segments (user ruling 2026-08-02): archways/doorways NEVER fade —
                // held permanently solid, no open/closed differentiation; recognition (the
                // per-door DoorRoot keying) exists only so their renderers cannot merge into
                // a fadeable wall segment. Any in-flight fade/MPB decays away right here.
                // ROOM-REVEAL FAIL-SAFE (fehlender_boden.png ruling): a wall whose room has no
                // VALID floor grid — unassociated, tile-UNANCHORED plane (median/bounds guess),
                // or a zero-sample grid (over the MaxTotalSamples budget) — must never fade:
                // its coverage would be measured against the wrong plane or the wrong room
                // (the mid-scenario reveal case), and a wrong fade deletes geometry. Solid is
                // the vanilla look, strictly safe; the wall joins the fade the moment its room
                // is anchored (next 2s rescan / reveal-triggered rescan).
                else if (seg.Engulfing || seg.DoorRoot != null
                    || !RoomDecisionValid(seg.RoomIndex))
                {
                    seg.State = false;
                    seg.PendingRaw = false;
                }
                // WALK-IN STAND-DOWN (ModBuild 271). The player has zoomed himself INTO the
                // play field; every wall is held fully solid while he is in there, without
                // exception. Two assignments and nothing else — the SAME pair every other
                // forced-solid arm above uses — so the fade ramp below delivers the ordinary
                // ANIMATED un-fade and no renderer is ever snapped. The 251 failure was not
                // this mechanism (it "worked exactly as designed"); it was a trigger that fired
                // when the user leaned over a 61 cm tabletop diorama.
                //
                // WHY IT SITS ABOVE THE SPLIT-RUN BRANCH AND NOT BELOW IT. A split-run member's
                // verdict belongs to its run, and EvaluateSplitRuns above has already written
                // that run's state for this pass. If this branch were below RunDriven, a faded
                // run would re-assert `State = true` on every one of its ~40 members one branch
                // later and the mode would visibly fail on exactly the biggest walls in the
                // scenario. Placing it above makes the member unreachable by its run while the
                // mode holds — and the run itself is deliberately left RUNNING (it keeps
                // measuring, keeps its EMA and keeps its own verdict), so when the latch
                // releases the member resumes from a LIVE reading rather than a stale one and
                // the wall does not snap back to a verdict taken minutes ago.
                //
                // It sits BELOW the two fail-safe arms above because those are unconditional:
                // a boundless segment, a doorway and a room with no valid floor grid must be
                // solid whatever this dial says, and routing them through here would make their
                // safety depend on a config entry.
                else if (walkInside)
                {
                    // Counted BEFORE the assignment, so on the engaging pass this is what was
                    // genuinely hidden at the moment the mode took over — the walls the user is
                    // about to watch reappear.
                    if (seg.State || seg.Fade > 0.01f)
                        _walkHeldHidden++;
                    _walkHeld++;
                    seg.State = false;
                    seg.PendingRaw = false;
                    // A FROZEN EMA IS THE 251 SYMPTOM WITH A DELAY. This branch skips the
                    // coverage evaluation, so an unsplit wall's Smooth would sit at whatever it
                    // read the moment the mode engaged — and if the player stood inside for a
                    // minute, every wall would come out of the mode holding the SAME minute-old
                    // reading and could re-fade together on the next dwell. That is exactly the
                    // "alle auf einmal" he rejected. Dropping SmoothInit costs nothing while the
                    // mode holds (Smooth keeps its last value for the diag lines) and makes the
                    // first evaluation after the release re-seed from that wall's OWN live
                    // coverage, so the walls leave the mode disagreeing, as they entered it.
                    // Split runs need no equivalent: EvaluateSplitRuns keeps running above, so
                    // their EMA is already live when the latch drops.
                    seg.SmoothInit = false;
                }
                // SPLIT-RUN MEMBER (ModBuild 259): its coverage was already measured in
                // EvaluateSplitRuns and its verdict belongs to the RUN, not to it. The flow is
                // strictly one-way — a run reads its members' cells, a member reads its run's
                // state — so no piece can ever be its own cause. RunDriven is deliberately sticky
                // across a skipped evaluation: re-asserting the run's last state is exactly what
                // the branch below would have done with a stale reading anyway.
                else if (seg.RunDriven)
                {
                    bool want = RunStateOf(seg);
                    seg.PendingRaw = want;
                    if (seg.State != want)
                        seg.State = want; // the RUN FADE line logs the edge once for the whole run
                }
                // Room-coverage metric (EMA-smoothed) with the stepper-driven Schmitt
                // trigger + dwell hysteresis. The un-fade dwell is long, and much longer
                // still unless the perspective (head position / world grip) recently
                // changed — rotation-only head motion keeps the current state sticky.
                else if (evaluate)
                {
                    float fraction = BlockedFraction(seg, headPos);
                    seg.LastRaw = fraction;
                    if (!seg.SmoothInit)
                    {
                        seg.SmoothInit = true;
                        seg.Smooth = fraction;
                    }
                    else
                    {
                        seg.Smooth += (fraction - seg.Smooth) * fracStep;
                    }
                    bool raw = seg.Smooth >= (seg.State ? offFraction : onFraction);
                    if (raw != seg.PendingRaw)
                    {
                        seg.PendingRaw = raw;
                        seg.PendingSince = now;
                    }
                    if (seg.PendingRaw != seg.State)
                    {
                        float dwell = seg.PendingRaw
                            ? EnterDwellSeconds
                            : (reevalArmed ? exitDwellMoved : exitDwellStationary);
                        if (now - seg.PendingSince >= dwell)
                        {
                            seg.State = seg.PendingRaw;
                            LogStateFlip(seg); // R2 deliverable: shader variant applied
                        }
                    }
                }

                // R2 (user ruling 2026-08-24: "Ich will aber das jede Wand einzeln verschwinden
                // kann und andere bleiben"). Every wall's verdict is now decided ONLY by the
                // branches above, from its own coverage of its own room. Nothing in this loop
                // reads a scene-wide switch any more, which is what makes independence a
                // property of the code rather than a hope. NotePerWallVerdict records each
                // wall's own numbers so the falsifier can show them DISAGREEING.
                NotePerWallVerdict(seg);

                // Critically-damped-style exponential fade toward the debounced state — OR a
                // PEER's synced fade (MP sync, wire record 17): effective target =
                // max(local decision, any live peer set containing this wall's key). Composed
                // at the TARGET so the identical ramp, delivery and restore machinery runs
                // and a synced fade is visually indistinguishable from a local one;
                // dwell-free by design (the deciding peer already dwelled). Doorway segments
                // stay exempt here too — they never fade anywhere, on any machine.
                // ModBuild 271: "ausnahmslos alle Wände sichtbar" has to hold in MULTIPLAYER
                // too, so while the walk-in mode holds a PEER's synced fade may not resurrect a
                // wall this pass just forced solid. RECEIVER SIDE ONLY — our own fades are still
                // broadcast unchanged, the wire format is untouched, and each client's mode
                // decides only what THAT client sees, so nothing needs renegotiating.
                int peerFadeId = 0;
                bool remoteFade = seg.DoorRoot == null && !walkInside
                    && RemoteWantsFade(seg, now, out peerFadeId);
                LogRemoteFadeEdge(seg, remoteFade, peerFadeId);
                // GATE LIFT (round 12): the embedding wall fades with its gate column's
                // decision — same max-composition as the peer sync, native delivery.
                // Round-13 LINGER: the lift survives the gate segment's death (Apparance
                // prop churn destroys/rebirths the door prop every few seconds) so the
                // embedding wall does not flap with the prop lifecycle.
                // ModBuild 271: the gate lift is suppressed by the walk-in mode for the same
                // reason as the peer fade. The gate column's own State is already forced false
                // above, but the lift LINGERS GateLiftLingerSeconds past it, so without this
                // term an embedding wall would stay hidden for seconds after the mode engaged —
                // an exception, and the user asked for none.
                bool gateLift = seg.DoorRoot == null && !walkInside
                    && ((seg.GateLift != null && seg.GateLift.State)
                        || now < seg.GateLiftUntil);
                if (seg.DoorRoot == null && seg.GateLift != null && seg.GateLift.State)
                    seg.GateLiftUntil = now + GateLiftLingerSeconds;
                LogGateLiftEdge(seg, gateLift);
                float target = (seg.State || remoteFade || gateLift) ? 1f : 0f;
                seg.Fade += (target - seg.Fade) * fadeStep;
                if (Mathf.Abs(target - seg.Fade) < 0.005f)
                    seg.Fade = target;
                NotePerWallOutcome(seg, remoteFade, gateLift, peerFadeId);
                // Round-14 watchdog: a fade the live coverage no longer supports must be
                // impossible to miss in the next hardware log (see WatchLatch).
                WatchLatch(seg, now, reevalArmed ? exitDwellMoved : exitDwellStationary,
                    remoteFade, gateLift);
                // A gate column's decision state must survive the door prop's death WITHOUT
                // outliving the evidence for it — snapshot it live, not once per rescan.
                if (seg.IsGateColumn)
                    WriteGateMemory(seg);

                Apply(seg);
            }

            // WALK-IN EDGE (ModBuild 271) — unthrottled and NOT gated by QuietDiagnostics: a
            // mode that overrules every wall in the scenario may never engage or release
            // silently. Printed HERE, after the loop, because the census it carries (how many
            // walls it held, how many of those were hidden at that moment) can only be counted
            // by the loop that just ran.
            if (_walkEdgePending)
                LogWalkInsideEdge(headPos);

            // [Optimize] QuietDiagnostics: the 'diag:' sweep (every DiagIntervalSeconds = 2 s,
            // i.e. 0.5 Hz — the "2 Hz" this comment claimed until ModBuild 284 was wrong by 4x)
            // was once the mod's longest log line and the single biggest contributor to the
            // hardware log's size, because it named EVERY tracked wall. It has named only the
            // three widest-covering segments since PERF S1, reusing one shared StringBuilder, so
            // it costs three Object.name reads and ~30 ToString allocations twice a second.
            //
            // IT STAYS ON BY DEFAULT AND IT IS THE ONE TO KEEP. It is the only line that reads a
            // wall's coverage, its EMA, its Schmitt state, its live fade and its four tripwires
            // (!ABOVE-WALL / !UNANCHORED / !NOGRID / !FAT+!ENGULF) side by side — i.e. the only
            // thing that separates "stuck faded", "never fades", "coverage has gone constant"
            // and "this room has no sample grid" without another build. A clean performance
            // capture that wants only the [Perf] lines is what this switch is for.
            if (now >= _nextDiagTime && !PerfConfig.Quiet)
            {
                _nextDiagTime = now + DiagIntervalSeconds;
                LogDiagnostic(headPos, visibleCount);
            }

            // The INSIDE falsifier, on the same cadence and only while the rule is IN FORCE —
            // its edges print unthrottled from UpdateInsideBoard, and the BOARD VOLUME line
            // proves the rule was armed even in a session where the player never went in.
            if (insideBoard && now >= _nextInsideLogTime && !PerfConfig.Quiet)
            {
                _nextInsideLogTime = now + InsideLogIntervalSeconds;
                LogInsideState(headPos, rigScale, edge: false);
            }
            // R2/R1 falsifiers, on the diag cadence: the per-wall spread, and the animation path
            // every fade in flight actually took.
            if (now >= _nextPerWallLogTime && !PerfConfig.Quiet)
            {
                _nextPerWallLogTime = now + DiagIntervalSeconds;
                LogPerWallIndependence();
                LogSplitRuns(now); // ModBuild 259: one wall, one verdict — and the orphan count
                LogAnimationPaths();
                LogStepEdges();
            }

            // Shared corner pieces (round 7): min-fade of the adjacent walls, per frame.
            ApplyCornerPieces();
            // WHICH RENDERERS ARE ACTUALLY BEING FADED, BY NAME (user report 2026-08-19,
            // skelet.jpg: "Der Schädel ist immer noch nicht sichtbar"). Deliberately HERE — after
            // every applier has run, so the line reports what was written and not what was
            // intended (the ModBuild-164 lesson). Rate-limited and change-triggered; the
            // expensive half only runs on a frame where the written set moved. See
            // WallSegmentFade.FadeCensus.cs.
            LogFadeWriteCensus(now);
            // Regenerated shell pieces (Apparance churn) must be re-hidden faster than the
            // 2s rescan — see the fast-reclaim doc in WallSegmentFade.Stacked.cs.
            FastReclaimRegeneratedShell(now);

            // WALL-PATH AUDIT, sliced (ModBuild 262). Stepped ONLY while the rescan pipeline is
            // idle, so this budget and the census budget can never land on the same frame.
            // Diagnostics only — it reads renderers and writes nothing.
            //
            // ModBuild 278: …and not at all while the walk-in suspension holds. This audit
            // explains WHY a given wall did or did not take the fade path; inside the mode no
            // wall takes any path, every verdict it would print is "held solid by decree", and
            // it measured 2.29 ms avg / 8.4 ms per second in the ModBuild 277 log — the second
            // largest per-frame cost in the subsystem after the decision itself. It resumes on
            // the same edge everything else does, and _nextPathAudit is likewise left in the
            // past so the first pass after the release runs immediately.
            if (_rescanStage == RescanStage.Idle && !suspended)
                StepWallPathAudit(now);

            // Re-log the heartbeat when the tracked set changes materially (walls stream in over
            // several rescans as Apparance generates, and adopted tilesets appear late) — the
            // first heartbeat of a scenario otherwise reports a half-built table forever.
            if (_heartbeatLogged && _heartbeatSegCount >= 0
                && Mathf.Abs(_live.Segments.Count - _heartbeatSegCount) >= 5)
                _heartbeatLogged = false;
            // …and when the fade-capable renderer census changes (round 5): five hardware
            // rounds ran on a single STALE pre-generation heartbeat ("fade-capable 0") that
            // hid the unfadeable-wall TRIPWIRE — the line that names the shaders of cache
            // walls carrying NO fade-capable renderer (this keep tileset's masonry).
            if (_heartbeatLogged && _censusFadeRenderers != _heartbeatFadeRenderers)
                _heartbeatLogged = false;

            // [Optimize] QuietDiagnostics (PERF S1, 2026-08-09): the heartbeat block below is
            // the mod's second-largest burst and it was NOT gated. It re-arms on any ±5
            // segment change — i.e. constantly under Apparance's regen churn — and then runs
            // TWO more full-scene FindObjectsOfType walks (LogFloorColumnCensus's
            // MeshRenderer(includeInactive) sweep and LogMapTileCensus's ProceduralMapTile
            // one, ~10-15 ms EACH in the big room) plus a whole-table walk and a 40-line
            // string. It is pure DIAGNOSTIC output — nothing below writes a renderer, a
            // material or a segment — so suppressing it cannot change a single pixel; a
            // capture that wants only the [Perf] lines gets its frame time back. Leaving
            // _heartbeatLogged false means the census prints on the very next tick if the
            // switch is turned back off mid-session.
            if (!_heartbeatLogged && !PerfConfig.Quiet)
            {
                using var _censusScope = PerfMonitor.Scope("WallFade.Census");
                _heartbeatLogged = true;
                _heartbeatSegCount = _live.Segments.Count;
                _heartbeatFadeRenderers = _censusFadeRenderers;
                LogFloorColumnCensus();
                LogMountedCensus();
                // ModBuild 262: LogWallPathAudit is NO LONGER driven from here. Hanging off this
                // block is exactly why the 260 log holds three of them and no steady state — the
                // block re-arms only on a ±5 segment change or a fade-census change, and it
                // cannot simply be re-armed on a timer because it measured 25.3 ms avg / 34.3 ms
                // worst on its 3 frames. It has its own sliced cadence now; see StepWallPathAudit.
                int highSegs = 0, lowSegs = 0, adoptedSegs = 0, engulfSegs = 0, foliage = 0;
                int siblings = 0, failSafeSegs = 0, doorways = 0, mounted = 0, stacked = 0;
                int bodyWalls = 0, bodyMeshes = 0, gates = 0;
                foreach (Segment s in _live.Segments.Values)
                {
                    if (s.VariantHigh) highSegs++;
                    if (s.VariantLow) lowSegs++;
                    if (!s.FromWallCache) adoptedSegs++;
                    if (s.Engulfing) engulfSegs++;
                    foliage += s.Foliage.Count;
                    siblings += s.Siblings.Count;
                    mounted += s.Mounted.Count;
                    stacked += s.Stacked.Count;
                    if (s.Body.Count > 0)
                    {
                        bodyWalls++;
                        bodyMeshes += s.Body.Count;
                    }
                    if (!RoomDecisionValid(s.RoomIndex)) failSafeSegs++;
                    if (s.DoorRoot != null)
                        doorways++;
                    if (s.IsGateColumn)
                        gates++;
                }
                // ROUND-12 FAIL-SAFE FORENSICS (zero ADJACENT RE-ANCHOR lines at reach 4.0
                // — is the reach too short, or do these walls have no bounds at all?): name
                // each fail-safe wall with its nearest-anchored-room XZ gap (or NO-BOUNDS).
                // MODBUILD 261 — THE LINE WAS A HEAD, NOT A SAMPLE, AND THAT IS WHY IT SAT
                // UNACTIONED FOR TWELVE ROUNDS. It named the first 10 segments in dictionary
                // order and every one of them read NO-BOUNDS, so "the next lever" was read as
                // "raise the re-anchor reach". The ModBuild 260 heartbeat says 127 of 175
                // segments are fail-safe solid, and the reach cannot be the reason for a single
                // one of them: AssociateRooms leaves RoomIndex at -1 for a BOUNDLESS segment and
                // only ever re-anchors a segment that already has bounds. The three counts below
                // are over the WHOLE population, so the next log states which lever exists at all
                // — a nonzero 'beyond reach' is the only reading that makes the reach the lever.
                var fsSb = new System.Text.StringBuilder();
                int fsListed = 0, fsNoBounds = 0, fsBeyondReach = 0, fsWithinReach = 0;
                int fsRefused = 0;
                foreach (Segment s in _live.Segments.Values)
                {
                    if (RoomDecisionValid(s.RoomIndex) || s.DoorRoot != null)
                        continue;
                    if (!s.HasBounds)
                    {
                        fsNoBounds++;
                        if (s.GeometryRefusedWhy != null)
                            fsRefused++;
                        // A refused split piece is boundless because it owns no renderer at all
                        // (ModBuild 261) — it is NOT a wall the geometry pass failed on, and
                        // lumping the two together is what made the 260 report unreadable.
                        if (fsListed < 10)
                        {
                            if (fsListed++ > 0) fsSb.Append(", ");
                            fsSb.Append('\'')
                                .Append(s.Anchor != null ? s.Anchor.name : "<dead>")
                                .Append(s.GeometryRefusedWhy != null
                                    ? "' NO-RENDERER (choke-point refusal, not a bounds failure)"
                                    : "' NO-BOUNDS");
                        }
                        continue;
                    }
                    float bestSq = float.PositiveInfinity;
                    for (int ri = 0; ri < _live.RoomBounds.Count; ri++)
                    {
                        if (!RoomDecisionValid(ri))
                            continue;
                        Bounds room = _live.RoomBounds[ri];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - s.Bounds.max.x,
                            s.Bounds.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - s.Bounds.max.z,
                            s.Bounds.min.z - room.max.z));
                        float sq = gx * gx + gz * gz;
                        if (sq < bestSq)
                            bestSq = sq;
                    }
                    if (float.IsInfinity(bestSq)
                        || bestSq > AdjacentReanchorMaxGapWU * AdjacentReanchorMaxGapWU)
                        fsBeyondReach++;
                    else
                        fsWithinReach++;
                    if (fsListed < 10)
                    {
                        if (fsListed++ > 0) fsSb.Append(", ");
                        fsSb.Append('\'')
                            .Append(s.Anchor != null ? s.Anchor.name : "<dead>")
                            .Append("' gap ")
                            .Append(float.IsInfinity(bestSq)
                                ? "n/a" : Mathf.Sqrt(bestSq).ToString("F1"));
                    }
                }
                if (fsNoBounds + fsBeyondReach + fsWithinReach > 0)
                    VRLog.Info(Name,
                        $"FAIL-SAFE GAPS: {fsNoBounds + fsBeyondReach + fsWithinReach} segment(s) "
                        + $"held FAIL-SAFE solid, split by the reason over the WHOLE population: "
                        + $"{fsNoBounds} have NO BOUNDS AT ALL (AssociateRooms skips them, so the "
                        + "re-anchor reach is not their lever and never was) — of those "
                        + $"{fsRefused} own NO RENDERER because the wall choke point refused them "
                        + "as floor-standing props, i.e. they are not walls at all and no wall "
                        + "rule can move them, "
                        + $"{fsBeyondReach} have bounds but border no decision-valid room within "
                        + $"the {AdjacentReanchorMaxGapWU:0.0} wu reach (THIS is the only class "
                        + "raising the reach could rescue), "
                        + $"{fsWithinReach} have bounds and ARE within reach yet still read "
                        + "invalid (that would be a bug in the re-anchor, and it must be zero). "
                        + $"First {fsListed} by name: {fsSb} (round-12 datum, made honest in "
                        + "ModBuild 261 — the old line printed only these names and every one of "
                        + "them was NO-BOUNDS, which read as 'the reach is too short').");

                string unfadeable = _censusWallsWithoutFade > 0
                    ? $"; TRIPWIRE {_censusWallsWithoutFade} cache wall(s) carry NO fade-capable "
                      + $"renderer — their shaders: {string.Join(", ", _unfadeableWallShaders)} "
                      + $"— {bodyWalls} plain-mesh BODY column(s) ({bodyMeshes} mesh(es), "
                      + $"renderer.enabled fallback for materials WITHOUT native fade "
                      + $"controls; spanning courses hide, only fully-in-band courses stay "
                      + $"solid [round 8])"
                    : string.Empty;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': tracking "
                    + $"{_live.Segments.Count} wall segments ({_live.Segments.Count - adoptedSegs} from the "
                    + $"wall cache + {adoptedSegs} ADOPTED by shader, grouped by tile/parent; "
                    + $"fade-capable renderers {_censusFadeRenderers} = {_censusClaimed} claimed "
                    + $"+ {_censusAdopted} adopted; {_live.SplitAnchors.Count} room-engulfing wall(s) "
                    + $"split per renderer, {engulfSegs} unsplittable held solid; {foliage} foliage "
                    + $"attachment(s) + {siblings} asset-sibling(s) + {mounted} wall-mounted "
                    + $"prop(s) (torches/candles — renderer.enabled only, Lights never touched) "
                    + $"ride their wall's fade; {stacked} STACKED SHELL piece(s) (fort/keep "
                    + $"superstructure meshes — extend their wall's occlusion AABB, dissolve "
                    + $"with it; [WallFade] StackedShellFade) ride their wall column; "
                    + $"{doorways} DOORWAY segment(s) held permanently solid (doorway fade "
                    + $"disabled — user ruling 2026-08-02); {gates} GATE column(s) (the wall "
                    + $"EMBEDDING a doorway — fades like any wall, only the arch rect stays "
                    + $"solid — user ruling 2026-08-07); "
                    + $"{failSafeSegs} wall(s) FAIL-SAFE solid (room unanchored/no floor grid)"
                    + $"{unfadeable}) "
                    + $"(shader variants: {lowSegs} LOW / "
                    + $"{highSegs} HIGH) against {_live.RoomBounds.Count} LOGICAL room(s) "
                    + $"(grouped from {_live.BuiltRoomCount} volume renderer(s) by the game's CMap "
                    + $"room identity — round 4) / {_live.AllSamples.Count} floor samples "
                    + $"({_live.RoomsAnchored}/"
                    + $"{_live.RoomBounds.Count} rooms tile-anchored, plane +"
                    + $"{FloorSampleEpsilon:0.00} wu, y {_live.SampleYMin:F2}..{_live.SampleYMax:F2}; "
                    + $"ModBuild 259: every sample sits on a hex the GAME calls playable — "
                    + $"CNode.Walkable and not CNode.Blocked, i.e. neither an EDGE hex nor one "
                    + $"an obstacle prop stands on — where the game's tile registry could name "
                    + $"the room's hexes ({_tilesResolved} hex(es) keyed this rescan); see the "
                    + $"SAMPLE GRID line for the per-room funnel and which term cut what) — "
                    + $"per-wall ROOM-coverage fade (strict own-room accounting; "
                    + $"EMA tau {FractionTauSeconds:0.00}s; on ≥{onFraction:0.00}, off "
                    + $"<{offFraction:0.00}; dwell {EnterDwellSeconds:0.00}s in, "
                    + $"{exitDwellMoved:0.0}s out moved / "
                    + $"{exitDwellStationary:0.0}s stationary — live config [WallFade]; "
                    + $"tau {FadeTauSeconds:0.00}s).");
            }
        }

        // ---- occlusion decision -----------------------------------------------------------

        /// <summary>
        /// Track PERSPECTIVE changes that should re-arm aggressive re-evaluation (each sets
        /// <see cref="_lastReevalTime"/> = now): recenter / rig rebuild (RigPoseVersion bumps
        /// there), rig-root motion beyond epsilon (world-grab drag/scale, snap-turn), a
        /// room-bounds shift flagged by <see cref="Rescan"/> (board moved/tilted), and — the
        /// primary anchor — real head TRANSLATION: the head camera's localPosition lives in
        /// tracking space (meters, independent of the diorama scale), so a &gt;0.18m move from
        /// the anchor re-arms and re-anchors while micro-sway and pure rotation never do.
        /// </summary>
        private void UpdatePerspectiveState(Transform headT, float now)
        {
            int pv = Rig.VRRigDriver.RigPoseVersion;
            if (pv != _lastPoseVersion)
            {
                _lastPoseVersion = pv;
                _lastReevalTime = now;
            }

            Transform? rig = Rig.VRRigDriver.RigRoot;
            if (rig != null)
            {
                Vector3 p = rig.position;
                Quaternion q = rig.rotation;
                float s = rig.lossyScale.x;
                if (!_rigSnapInit)
                {
                    _rigSnapInit = true;
                    _rigPos = p;
                    _rigRot = q;
                    _rigScale = s;
                }
                else if ((p - _rigPos).sqrMagnitude > 0.0004f * s * s // 2cm real, scale-aware
                         || Quaternion.Angle(q, _rigRot) > 0.5f
                         || Mathf.Abs(s - _rigScale) > 0.005f * Mathf.Max(_rigScale, 0.001f))
                {
                    _rigPos = p;
                    _rigRot = q;
                    _rigScale = s;
                    _lastReevalTime = now;
                }
            }

            if (_roomBoundsMoved)
            {
                _roomBoundsMoved = false;
                _lastReevalTime = now;
            }

            Vector3 headLocal = headT.localPosition;
            if (!_headAnchorInit)
            {
                _headAnchorInit = true;
                _headAnchor = headLocal;
            }
            else if ((headLocal - _headAnchor).sqrMagnitude
                     > HeadMoveReevalMeters * HeadMoveReevalMeters)
            {
                _headAnchor = headLocal;
                _lastReevalTime = now;
            }
        }

        /// <summary>
        /// Refresh the per-sample frustum flags (viewport test with margin) for ALL
        /// precomputed floor samples. Returns the overall visible count (diagnostic only —
        /// the metric reads the flags per room).
        /// </summary>
        private int UpdateSampleVisibility(Camera head)
        {
            int visible = 0;
            int n = Mathf.Min(_live.AllSamples.Count, _sampleVisible.Length);
            for (int i = 0; i < n; i++)
            {
                // Mono view/projection of the head camera; per-eye stereo frustums differ
                // only by half the IPD and a slightly wider horizontal FOV — FrustumMargin
                // (0.20 viewport-relative) generously covers that skew.
                Vector3 vp = head.WorldToViewportPoint(_live.AllSamples[i]);
                bool vis = vp.z > 0f
                    && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin
                    && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin;
                _sampleVisible[i] = vis;
                if (vis)
                    visible++;
            }
            return visible;
        }

        /// <summary>
        /// Can the coverage metric be trusted for this room? Requires: an associated room, a
        /// TILE-ANCHORED floor plane (an unanchored plane is a median/bounds GUESS — the exact
        /// class of frame error the round-6 hardware log caught, and the mid-scenario-reveal
        /// hazard: a freshly revealed room without its volume anchor yet), and a non-empty
        /// sample grid (rooms past the MaxTotalSamples budget get none). Walls failing this are
        /// held SOLID by the decision loop — fade decisions on a guessed frame delete geometry.
        /// </summary>
        private bool RoomDecisionValid(int room) =>
            room >= 0
            && room < _live.RoomFloorAnchored.Count && _live.RoomFloorAnchored[room]
            && room < _live.RoomSampleCount.Count && _live.RoomSampleCount[room] > 0;

        /// <summary>
        /// Fraction of the wall's OWN room's floor grid that the wall hides from the head:
        /// numerator = room grid points that are in view-direction (frustum flag) AND whose
        /// head→point segment the wall AABB clearly interrupts; denominator = the room's
        /// WHOLE grid (see class header for why the denominator is not frustum-culled).
        ///
        /// SELF-TEST (worked example, world scale 20 — 1 real m = 20 wu, 1 wu = 5 real cm;
        /// numbers chosen to match the round-6 hardware log: floor tile plane y = 0, wall
        /// tops ≈ 3.5, head standing 0.6 real m above the board):
        ///   Room: 5-hex ≈ 8.6 wu square footprint, x ∈ [0.3, 8.9], 4×4 grid → 16 points
        ///     (denominator), sample columns at x ≈ {1.38, 3.53, 5.68, 7.83}, y = 0.05.
        ///   Wall: run along the room's near edge, AABB x ∈ [−0.5, 0.3] (thickness 0.8 →
        ///     BlockEps = 0.4), y ∈ [−0.3, 3.5], z spanning the room. Player looks into
        ///     the room → all 16 points pass the frustum test.
        ///   (a) LEANING IN, head (−1.0, 4.5, roomMidZ) — 22.5 real cm above the floor,
        ///     5 cm outside the wall face. A ray to column x_s drops 4.45 wu; it reaches
        ///     the wall-top plane y = 3.5 at parameter t = 1.0/4.45 = 0.2247, i.e. at
        ///     x = −1 + 0.2247·(x_s+1): col 1 → x = −0.47, col 2 → x = 0.02 (both inside
        ///     the slab [−0.5, 0.3] → blocked), col 3 → x = 0.50, col 4 → x = 0.98 (past
        ///     the slab while still above the top → miss). Epsilon check col 2: dist =
        ///     √(4.53² + 4.45²) = 6.35, entry ≈ 0.2247·6.35 = 1.43 &lt; 6.35 −
        ///     max(0.4, 0.05·6.35 = 0.32) = 5.95 ✓. → 8/16 = 0.50 ≥ 0.25 → ON: the EMA
        ///     (tau 0.15s) crosses 0.25 after 0.15·ln(0.50/(0.50−0.25)) ≈ 0.10s, plus the
        ///     0.2s enter dwell → fades ~0.3s after the lean settles.
        ///   (b) STANDING TALL, head (−6, 12, roomMidZ) — 0.6 real m up, 0.3 m back: only
        ///     col 1 is shadowed (slab crossing y: 3.10→1.80 inside; col 2 stays ≥ 4.10
        ///     above the top) → 4/16 = 0.25, exactly the default bar — the marginal case.
        ///   Between (a) and (b) the shadow reach grows continuously as the head lowers,
        ///   so a pose hiding ~30% of the room (5/16 = 0.3125, e.g. col 1 + the first
        ///   oblique col-2 point) sits comfortably above the default 0.25: EMA crosses at
        ///   0.15·ln(0.3125/0.0625) ≈ 0.24s → ON ~0.45s after the pose settles. A wall
        ///   hiding ~30% of its room's floor therefore reliably triggers at default 0.25.
        /// </summary>
        private float BlockedFraction(Segment seg, Vector3 headPos)
        {
            seg.LastBlocked = 0;
            seg.LastRoomVisible = 0;
            seg.LastRoomTotal = 0;
            seg.LastDecidingRoom = -1;
            seg.LastBlockedCells.Clear();
            seg.LastBlockerPiece = null;
            seg.LastBlockerList = "-";
            seg.LastBlockerByContains = false;
            seg.LastContainsCells = 0;
            int room = seg.RoomIndex;
            if (room < 0 || room >= _live.RoomSampleCount.Count)
                return 0f;
            int total = _live.RoomSampleCount[room];
            seg.LastRoomTotal = total;
            seg.LastDecidingRoom = room;
            if (total <= 0)
                return 0f;

            // THE "WALL IN THE FACE" SHORT-CIRCUIT IS GONE (ModBuild 255), and it turns out it
            // was never needed. It returned a hard 1f — "this wall hides the ENTIRE room" —
            // whenever the head sat inside one wall renderer's AABB, and it reported the room's
            // TOTAL as visible while doing so, which is what put `blk 16/16 v16` in the diag
            // next to a frustum reading of `vis 12/16`. That is the signature the user was
            // looking at: "Wie du siehst bei den anderen Wänden sind sie wieder dauerhaft
            // ausgeblendet obwohl sie es nicht müssten" — 'Wall 2' sat at raw 1.00 in 44 of its
            // diag samples this session, and every one of them came from here rather than from
            // any measurement of what the wall covers.
            //
            // THE ORDINARY RAY MATH ALREADY HANDLES A HEAD INSIDE GEOMETRY. Bounds.IntersectRay
            // returns true for a ray whose origin is inside the box, at distance 0, so every
            // sample genuinely behind that piece is counted blocked by the normal path — and
            // only those. A head buried in a tree trunk's AABB corner now costs that wall the
            // samples the trunk actually covers instead of all of them. Nothing is lost by
            // deleting the shortcut; the escape hatch it was protecting is the ray test itself.
            //
            // HeadInsideWallMesh is retained: WallSegmentFade.Inside.cs still reports it, and it
            // remains the honest "sealed in masonry" predicate. It simply no longer overrides a
            // measurement with an assertion.

            // STRICT OWN-ROOM accounting (round 4 — user ruling "normale Raum-Logik"; the
            // round-3 cross-room MAX is retired). The room is the LOGICAL room now: all
            // volume renderers of one game CMap merged into one grid, so a wall that
            // fronts a multi-volume room measures against that room's WHOLE floor.
            float fraction = RoomBlockedFraction(seg, headPos, room,
                out int blocked, out int visible, out _);
            seg.LastBlocked = blocked;
            seg.LastRoomVisible = visible;

            // ROOM SEAM (user report 2026-08-09, the wall that faded "falsch rum"): a wall
            // standing BETWEEN two rooms owns both of them — see the long note in
            // AssociateRooms for why picking one of them is a coin flip and why picking the
            // wrong one inverts the fade exactly as reported. Its coverage is therefore the
            // MAX over the rooms it borders: whichever room it is currently hiding from the
            // head is the room the user wants opened, and the wall must fade from EITHER
            // side. Segment.BorderRooms is empty for every wall that borders one room, so
            // this loop does not run at all for them — their behaviour is untouched.
            for (int i = 0; i < seg.BorderRooms.Count; i++)
            {
                int alt = seg.BorderRooms[i];
                if (alt < 0 || alt >= _live.RoomSampleCount.Count || _live.RoomSampleCount[alt] <= 0)
                    continue;
                // Cell attribution describes the OWN room only (see _attributeCells): a seam
                // wall's alt-room passes must not append their cells to it, or the list would
                // be a union of rooms and mean nothing.
                _attributeCells = false;
                float altFraction = RoomBlockedFraction(seg, headPos, alt,
                    out int altBlocked, out int altVisible, out int altTotal);
                _attributeCells = true;
                if (altFraction <= fraction)
                    continue;
                fraction = altFraction;
                seg.LastBlocked = altBlocked;
                seg.LastRoomVisible = altVisible;
                seg.LastRoomTotal = altTotal;
                seg.LastDecidingRoom = alt;
            }
            return fraction;
        }

        /// <summary>The per-room half of the metric: fraction of ROOM's grid that is in
        /// view-direction AND whose head→point segment this wall's AABB clearly
        /// interrupts.</summary>
        private float RoomBlockedFraction(Segment seg, Vector3 headPos, int room,
            out int blockedOut, out int visibleOut, out int totalOut)
        {
            blockedOut = 0;
            visibleOut = 0;
            totalOut = room >= 0 && room < _live.RoomSampleCount.Count ? _live.RoomSampleCount[room] : 0;
            if (totalOut <= 0)
                return 0f;

            Bounds b = seg.Bounds;
            int start = _live.RoomSampleStart[room];
            int end = Mathf.Min(start + totalOut, Mathf.Min(_live.AllSamples.Count, _sampleVisible.Length));
            float thicknessEps = seg.BlockEps;
            int blocked = 0, roomVisible = 0;
            for (int i = start; i < end; i++)
            {
                if (!_sampleVisible[i])
                    continue; // out of view-direction — cannot be "hidden by the wall"
                roomVisible++;
                Vector3 sample = _live.AllSamples[i];
                Vector3 to = sample - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                // Generous "clearly before the point": wall entry must precede the sample
                // by max(half wall thickness, 5% of the ray length) — thickness alone is
                // too strict for long grazing rays, a pure percentage was the round-4 bug.
                float eps = Mathf.Max(thicknessEps, BlockEpsDistFraction * dist);
                var ray = new Ray(headPos, to / dist);
                // BROAD PHASE ONLY. seg.Bounds rejects the ray cheaply; it may never ACCEPT
                // one on its own — see RayHitsWallMesh for why a union AABB is not a wall.
                if (!b.IntersectRay(ray, out float d) || (d >= dist - eps && !b.Contains(sample)))
                    continue;
                if (!RayHitsWallMesh(seg, ray, dist, eps, sample, out Renderer? by,
                                     out string byList, out bool byContains))
                {
                    continue;
                }
                blocked++;
                // Per-cell attribution: the room-relative cell index, the piece that took the
                // first one, WHICH LIST that piece came from and whether it was a ray entry or a
                // Contains claim. See Segment.LastBlockedCells / LastBlockerList /
                // LastContainsCells for why each of those exists. Suppressed for a seam wall's
                // alt-room passes so the list always describes ONE room.
                if (_attributeCells)
                {
                    seg.LastBlockedCells.Add(i - start);
                    if (byContains)
                        seg.LastContainsCells++;
                    // Keyed on the LIST and not on the piece: the fail-open case has no piece at
                    // all, and it is exactly the case that must not be reported as blank.
                    if (seg.LastBlockerList == "-")
                    {
                        seg.LastBlockerPiece = by;
                        seg.LastBlockerList = byList;
                        seg.LastBlockerByContains = byContains;
                    }
                }
            }
            blockedOut = blocked;
            visibleOut = roomVisible;
            return blocked / (float)totalOut;
        }

        /// <summary>
        /// NARROW PHASE: does any of this segment's own wall MESHES actually interrupt the
        /// head→sample ray? Same "clearly before the point" rule as the broad phase, applied to
        /// the renderer's own world AABB instead of the segment's union box.
        ///
        /// <para>WHY THIS EXISTS (user report 2026-08-24, walls_gone.jpg — looking down at the
        /// diorama from outside with essentially every wall dissolved and the gate structure
        /// itself see-through): <i>"In der map die ich gerade verteste, sind so gut wie dauerhaft
        /// alle Wände ausgeblendet ohne ersichtlichen Grund. Wenn ich von einer Seite schaue
        /// erwarte ich das nur die wände ausgeblendet würden, die mir die Sicht versperren würden.
        /// Stattdessen werden aber auch die Gegenüberliegende Wände ausgeblendet, die dahinter
        /// nichts haben."</i></para>
        ///
        /// <para>A <c>Segment</c> is a whole <c>ProceduralWall</c> RUN, and <c>seg.Bounds</c> is
        /// the axis-aligned UNION of every renderer it owns — masonry, pillars, adopted stacked
        /// shells and, in this tileset, TREES. In the ModBuild 250 log 'Wall 2' carries
        /// <c>FR_Wall_Grassy_Verge_Thin_Narrow_01@2.0</c> next to
        /// <c>FR_Pillar_Tree_Trunk_03@5.0</c> and <c>FR_Pillar_Tree_Trunk_03 (1)@4.9</c>, so its
        /// box is <c>wy[-0.52..9.47]</c> — ten world units tall over masonry two units high, and
        /// several units thick in both horizontal axes. That box is mostly AIR, and a ray through
        /// air was being counted as a ray through a wall.</para>
        ///
        /// <para>THE INSTRUMENT HAD BEEN SAYING SO ALL ALONG. <c>AppendSegDiag</c> prints
        /// <c>!FAT</c> when <c>min(size.x, size.z) &gt; GroupSlabMaxHorizontal</c> (3.5 wu), with
        /// the comment "its coverage numbers may read permanently high … distinguishes 'genuinely
        /// occluding' from 'AABB artifact'". 73 of the 74 diag lines in that session carry
        /// <c>!FAT</c> and ZERO carry <c>!ENGULF</c>: every wall the player was looking at was a
        /// fat box, and nothing acted on the flag, because
        /// <see cref="NeutralizeEngulfingSegments"/> only splits a fat segment that also
        /// XZ-CONTAINS 40% of its room's samples — a perimeter L-run never does.</para>
        ///
        /// <para>WHY THIS ANSWERS "die Gegenüberliegende Wände" BY CONSTRUCTION rather than with
        /// another threshold. The suggestion on the table was a directional term — fade only a
        /// wall between the head and the room centre. That would work for the reported view and
        /// would be wrong for a wall that genuinely occludes from an oblique angle, and it would
        /// be a second heuristic stacked on the first. Mesh-accurate blocking needs no such term:
        /// a ray from an elevated head to a floor sample TERMINATES at that sample, and the far
        /// wall's masonry stands beyond it, so the far wall cannot be hit. The opposite wall
        /// stops fading because it stops measuring as an occluder, not because a rule exempted
        /// it.</para>
        ///
        /// <para>WHAT COUNTS AS THE WALL — CORRECTED IN ModBuild 252. The first version of this
        /// method walked only <see cref="Segment.Renderers"/> and the plain-body meshes, on the
        /// reasoning that foliage is "dressing that rides the wall rather than geometry the eye
        /// reads as a wall". For ivy on masonry that is true. For the tileset's SCRUB WALLS it is
        /// exactly backwards, and the user reported the consequence within one build:
        /// <i>"Die anderen 'gestrüpp-wände' versperren mir nun auch manchmal die Sicht. Das darf
        /// niemals passieren."</i> A <c>FR_Wall_Grassy_Verge_Thin_Narrow</c> run is ONE wall
        /// renderer (<c>..._01</c>, 2.0 wu tall) plus THREE foliage attachments
        /// (<c>..._Bushes_01</c>, <c>..._Ivy_Grass_01</c>, <c>..._Plants_01</c>) — the log
        /// attributes them as <c>← foliage of 'Wall 2'</c> against <c>← wall renderer of
        /// 'Wall 2'</c>, and 345 such attachments exist in that scenario. The opaque mass a
        /// player reads as "wall" is the foliage; measuring only the masonry made these walls
        /// score as non-occluders, so they stayed solid and blocked his view of the board.</para>
        ///
        /// <para>THE RULE IS NOW STRUCTURAL, not a list of names: a renderer counts as this
        /// wall's occluding geometry exactly when it RIDES this wall's fade — because if it
        /// disappears when the wall fades, then while the wall is solid it is part of what the
        /// wall is hiding. That is one criterion, it needs no per-tileset knowledge, and it
        /// cannot disagree with what the player sees dissolve. Foliage, asset siblings and
        /// stacked shell pieces all qualify.</para>
        ///
        /// <para>MOUNTED DRESSING IS THE ONE EXCLUSION and it is deliberate: torches, candles,
        /// sparks and fireflies ride the fade too, but they are small emissive props and
        /// PARTICLE systems whose world bounds are animated and frequently far larger than
        /// anything opaque in them. Counting a spark cloud as an occluder would put the coverage
        /// metric back on exactly the "box full of air" footing this narrow phase exists to end.
        /// They are named in the falsifier's excluded count so the choice stays visible.</para>
        ///
        /// <para>COST. The broad phase is unchanged, so a ray the union box rejects costs exactly
        /// what it did before; only an ACCEPTED ray pays the walk. The logged scenario's fadeable
        /// walls own 41 + 23 + 14 + 34 wall renderers plus 345 foliage attachments against a
        /// 16-sample grid, and the walk returns on the FIRST hit, which for a genuine occluder is
        /// almost immediate.</para>
        ///
        /// <para>FAIL-OPEN: a segment with no wall meshes of its own (renderer list emptied by
        /// Apparance churn between rescans) keeps the broad-phase verdict, so this can never
        /// make a wall LESS able to fade than the geometry it currently owns justifies.</para>
        ///
        /// <para>WHAT IT REPORTS (ModBuild 257) and why the name alone was not enough. ModBuild
        /// 256 added <paramref name="by"/> — the piece that accepted the first blocking ray — and
        /// it settled the diagnosis in one grep: the wall the user calls correct is decided by
        /// masonry, the three he calls broken are decided by tree trunks. It could not settle
        /// WHERE TO FIX IT, because the same name can arrive through five different lists and each
        /// has a different owner: a piece in <see cref="Segment.Renderers"/> came from the
        /// tileset's own parenting (fix in the membership classifier), a piece in
        /// <see cref="Segment.Stacked"/> or <see cref="Segment.Siblings"/> was ADOPTED (fix at the
        /// adoption site). <paramref name="fromList"/> says which. <paramref name="byContains"/>
        /// says whether the cell was taken by a ray entry or by <c>Bounds.Contains(sample)</c> —
        /// head-dependent measurement against head-independent latch; see
        /// <see cref="Segment.LastContainsCells"/>.</para>
        /// </summary>
        /// <param name="fromList">Which list answered — a literal, so no allocation.</param>
        /// <param name="byContains">True when the floor point sits INSIDE the piece's box rather
        /// than behind it, i.e. when the block does not depend on where the head is.</param>
        private static bool RayHitsWallMesh(Segment seg, Ray ray, float dist, float eps,
            Vector3 sample, out Renderer? by, out string fromList, out bool byContains)
        {
            int meshes = 0;
            by = null;
            fromList = "-";
            byContains = false;
            // Wall renderers (the fade masonry).
            for (int i = 0; i < seg.Renderers.Count; i++)
            {
                if (HitsPiece(seg.Renderers[i], ray, dist, eps, sample, ref meshes,
                              out byContains))
                {
                    by = seg.Renderers[i];
                    fromList = "Renderers";
                    return true;
                }
            }
            // FOLIAGE — for a scrub wall this IS the wall (see the header).
            for (int i = 0; i < seg.Foliage.Count; i++)
            {
                if (HitsPiece(seg.Foliage[i], ray, dist, eps, sample, ref meshes, out byContains))
                {
                    by = seg.Foliage[i];
                    fromList = "Foliage";
                    return true;
                }
            }
            // Asset siblings: door wings, arch trim — opaque, and they vanish with the wall.
            for (int i = 0; i < seg.Siblings.Count; i++)
            {
                if (HitsPiece(seg.Siblings[i], ray, dist, eps, sample, ref meshes, out byContains))
                {
                    by = seg.Siblings[i];
                    fromList = "Siblings";
                    return true;
                }
            }
            // Plain wall body (masonry with no fade shader of its own).
            for (int i = 0; i < seg.Body.Count; i++)
            {
                if (HitsPiece(seg.Body[i].Renderer, ray, dist, eps, sample, ref meshes,
                              out byContains))
                {
                    by = seg.Body[i].Renderer;
                    fromList = "Body";
                    return true;
                }
            }
            // Stacked shell pieces already extend this wall's occlusion AABB by design.
            for (int i = 0; i < seg.Stacked.Count; i++)
            {
                if (HitsPiece(seg.Stacked[i].Renderer, ray, dist, eps, sample, ref meshes,
                              out byContains))
                {
                    by = seg.Stacked[i].Renderer;
                    fromList = "Stacked";
                    return true;
                }
            }
            // seg.Mounted is deliberately NOT walked — see the header's exclusion note.
            // No meshes to ask: keep the broad-phase verdict rather than silently un-fading a
            // wall whose renderer list is mid-refresh. Named in the log as its own case: a wall
            // reading high coverage attributed to 'fail-open' is not measuring anything at all,
            // and that is a different defect from one attributed to a piece.
            byContains = false;
            if (meshes == 0)
            {
                fromList = "fail-open (no meshes to ask)";
                return true;
            }
            return false;
        }

        /// <summary>
        /// THE STANDING TEST — RETIRED FROM THE NUMERATOR IN ModBuild 255, kept as the shape
        /// statistic the admission census reports. It never excluded a single piece on the two
        /// walls that were wrongly faded, and the only thing it DID exclude was real wall
        /// geometry.
        ///
        /// <para>WHY IT WAS REDUNDANT. <see cref="StripGroundRenderers"/> already removes every
        /// renderer and every foliage attachment whose AABB top reaches no higher than
        /// <c>GroundExclusionHeightWU</c> = 1.0 wu above its room's floor plane, on the far
        /// better criterion of absolute height rather than aspect ratio. By the time a piece can
        /// be asked about, it is more than a wall-height-fifth tall, and the flat ground plates
        /// that look like the culprits — <c>FR_Floor_Grass_Half_01</c>,
        /// <c>FR_Floor_Grass_BAY s(1.779, 0.357, 1.997)</c> — are 0.36 wu tall and were stripped
        /// long before. They are parented under <c>Walls/Wall 1/Generated Content/…</c> in the
        /// scene hierarchy, which is what makes them look like wall geometry in a hierarchy
        /// census, but they are not in the segment at all.</para>
        ///
        /// <para>WHY IT WAS HARMFUL. The ModBuild 254 log's own admission line reads
        /// <c>'Wall 1' 178 admitted / 0 excluded | 'Wall 2' 146 admitted / 0 excluded |
        /// 'Wall 3' 10 admitted / 4 excluded, widest excluded 'WallTop' 2.4 wu</c>. Zero
        /// exclusions where the defect was, and on the one wall where it did fire it excluded
        /// <c>WallTop</c> — a capstone course, which is real masonry standing at the top of a
        /// wall and genuinely does hide what is behind it. A test that removes wall caps from
        /// the numerator and no ground dressing at all is a net loss, so it is gone.</para>
        ///
        /// <para>The original reasoning below is kept because the MECHANISM it identified was
        /// real and is still live: <c>rb.Contains(sample)</c> lets a piece spanning the floor
        /// plane claim samples from any angle. The ground strip is what actually protects
        /// against it, and this method now only reports the shape distribution so the next log
        /// can show whether anything flat is surviving that strip.</para>
        ///
        /// <para>ORIGINAL NOTE — the discriminator between geometry you cannot see past and
        /// dressing you look OVER. A piece would be admitted only when its vertical extent is at
        /// least <see cref="StandingPieceRatio"/> of its NARROWER horizontal extent.
        ///
        /// <para>WHY THIS EXISTS (user report 2026-08-24, Wandproblem.jpg): <i>"Ich schaue nur
        /// von einer Seite, d.h. es gibt keinen Grund für die Wand gegenüber ausgeblendet zu
        /// sein … Alle diese 'Wände' mit Gestrüp haben auch größere nicht begehbare Flächen die
        /// auch ausgeblendet werden … kann es sein, dass diese Flächen irgendeine Rolle bei dem
        /// Problem spielen?"</i> He was right, and it was a regression of my own making. ModBuild
        /// 252 admitted a piece to the numerator on one rule — "it counts as occluding exactly
        /// when it RIDES this wall's fade" — which correctly caught the scrub walls' bushes and
        /// incorrectly caught the wide, ground-hugging verge and undergrowth areas that belong
        /// to the same wall run but spread several world units INTO the room.</para>
        ///
        /// <para>THE MECHANISM IS NOT THE RAY, IT IS <c>Contains</c>. A mat lying on the floor
        /// spans the floor plane, so the room's own floor samples sit INSIDE its AABB, and the
        /// <c>rb.Contains(sample)</c> clause marks every one of them blocked — from any viewing
        /// angle whatsoever, because containment has nothing to do with where the head is. That
        /// is how 'Wall 2' reached <c>blk 16/16</c>: not by standing between the eye and the
        /// floor, but by lying ON it. Nothing that is genuinely a wall at the room's edge can
        /// interrupt every ray to its own room's floor, which is exactly the tell.</para>
        ///
        /// <para>HIS RULING IS UNAFFECTED, AND THAT IS THE POINT OF PUTTING THE TEST HERE.
        /// "Wenn die Wand ausgeblendet wird sollen die auch mit ausgeblendet werden wie es der
        /// Fall ist" — the ground areas must still DISSOLVE with their wall, and they still do:
        /// this test lives in the occlusion numerator only. What a wall HIDES and what a wall
        /// TAKES WITH IT when it goes are two different questions, and ModBuild 252 answered
        /// both with one predicate. They are separate now.</para>
        ///
        /// <para>THE RATIO IS STRUCTURAL, NOT A NAME LIST, and it is checked against the logged
        /// geometry: the scrub wall's own pieces are
        /// <c>..._Bushes_01 s(2.6,1.3,2.2)</c> → 1.3 vs 2.2 = 0.59, <c>..._Ivy_Grass_01
        /// s(1.8,2.0,1.4)</c> → 1.43, <c>..._Plants_01 s(1.4,0.9,1.2)</c> → 0.75 — all admitted,
        /// so the scrub walls keep blocking exactly as ModBuild 252 correctly made them. A wall
        /// slab <c>s(0.5,3,8)</c> scores 6.0 and a ground mat <c>s(6,0.4,5)</c> scores 0.08.
        /// The narrower horizontal extent is deliberately the denominator: a long wall RUN is
        /// wide in one axis and thin in the other, and using the wider one would exclude it.</para>
        /// </summary>
        /// <summary>
        /// Stable per-piece point in the ramp at which a channel-less foliage attachment
        /// switches off. Spread over 0.10..0.92 rather than 0..1 so that nothing vanishes on the
        /// very first frame of the fade (which would read as a pop of its own) and everything is
        /// gone before the held state begins.
        ///
        /// <para>Derived from an instance id, which is stable for the lifetime of the object — so
        /// a piece leaves and returns at the same point of every transition and never flickers by
        /// re-randomising per frame. Apparance regenerating the mesh gives it a new id and
        /// therefore a new slot, which is harmless: the slot only has to be stable WITHIN a
        /// transition.</para>
        ///
        /// <para><b>MODBUILD 261 — THE KEY IS THE PROP UNIT, NOT THE RENDERER.</b> A conifer is
        /// several renderers under one prop root, and a per-renderer key gave the trunk one
        /// threshold and the needles another: the prop TEARS mid-ramp, which is the user's "die
        /// Blätter laden nach". The key is now the prop-unit root whenever one is known, so every
        /// renderer of one prop leaves at the same instant.</para>
        ///
        /// <para>THE MEMO IS READ-ONLY HERE, AND THAT IS LOAD-BEARING. This runs from
        /// <see cref="Apply"/>, i.e. EVERY FRAME for every channel-less foliage piece.
        /// <c>PropUnitRootOf</c> is a hierarchy climb whose per-node facts are memoised PER
        /// COMMIT and dropped before the tick — calling it here would be a per-frame scene walk,
        /// the defect class this subsystem has shipped three times. <c>_live.PropUnitRootMemo</c> is
        /// filled during the rescan and cleared by <c>BeginPropUnitScope</c>, so a lookup is O(1)
        /// and a MISS falls back to the renderer id, which is exactly the ModBuild 260 behaviour.
        /// </para>
        ///
        /// <para><b>MODBUILD 261 — THIS IS THE ONLY PLACE A STAGGER THRESHOLD IS COMPUTED.</b>
        /// Two lanes independently moved the key from the renderer to the prop unit and each
        /// wrote its own copy: this one, and <c>UnitStaggerThreshold</c> in
        /// WallSegmentFade.PropUnit.cs. The arithmetic agreed; the KEY RESOLUTION did not. This
        /// one read <see cref="CommittedTable.PropUnitRootMemo"/> and fell back to the RENDERER id on a miss;
        /// the other called <c>PropUnitRootOf</c> live, which always finds the root. A prop whose
        /// pieces are split between <c>seg.Foliage</c> (this path) and <c>seg.UnitDressing</c>
        /// (that one) therefore got TWO different thresholds whenever the memo happened not to
        /// hold that parent — and whether it did depended on rescan order, i.e. the prop tore
        /// INTERMITTENTLY, which is how the whole class has been reported. The duplicate is gone:
        /// both appliers call this method, so "the two agree" is not a contract to keep, it is
        /// the absence of a second implementation to disagree with.</para>
        ///
        /// <para>THE MEMO IS READ-ONLY HERE, AND THAT IS LOAD-BEARING. This runs from
        /// <see cref="Apply"/>, i.e. EVERY FRAME for every channel-less foliage piece and every
        /// channel-less unit-dressing piece. <c>PropUnitRootOf</c> is a hierarchy climb whose
        /// per-node facts are memoised PER COMMIT and dropped before the tick — calling it here
        /// would be a per-frame scene walk, the defect class this subsystem has shipped three
        /// times. <c>WarmStaggerKeys</c> (WallSegmentFade.PropUnit.cs) resolves the parent of
        /// every renderer in both lists at the END of the prop-unit commit phase, when both lists
        /// are final and the per-node fact memos are still open, so every lookup here is an O(1)
        /// dictionary hit.</para>
        ///
        /// <para>HOW A FUTURE EDIT THAT BREAKS THE AGREEMENT IS CAUGHT. Not by this comment.
        /// (a) There is one implementation, so no second one can drift. (b) A memo ABSENCE — the
        /// only remaining state in which two members of one prop can key differently — is counted
        /// by <see cref="NoteStaggerKeyMiss"/> and printed with an <c>[ALARM]</c> on the SHOW EDGE
        /// line; it can only become non-zero if an applier starts stagger-keying a list
        /// <c>WarmStaggerKeys</c> does not walk. (c) The picture-side falsifier, SHOW EDGE's TORN
        /// RETURN term, now watches BOTH lists (ModBuild 261 — it only ever saw the dressing half,
        /// which is the half that was already right) and reads the fades off the renderers, so it
        /// can contradict this rule outright.</para>
        ///
        /// <para>A memo entry whose VALUE is null is a resolved "this piece is in no prop unit",
        /// not a miss: both lists then key on the renderer's own id and still agree.</para>
        ///
        /// <para>THE EVIDENCE (user, 2026-08-24, <c>wände_problem4.mp4</c>): <i>"immer wenn sie
        /// auftaucht sieht man wie die Blätter vom Baum erst irgendwie anders geladen werden und
        /// dann sichtbar richtig 'nachladen'"</i>. The fir at t = 25.10–25.52 s stands as a bare
        /// twig skeleton — trunk and branch geometry drawn, needle cards absent — and at
        /// t = 25.533 s the ENTIRE crown appears in ONE frame, in exactly the places the twigs
        /// already were: same silhouette, same trunk shading, same texture detail. Not a material
        /// swap (the ModBuild-260 STEP totals read <c>0 material swap</c>, <c>0 swap removed</c>
        /// for the whole session), not a mip (a mip change blurs, it does not remove geometry),
        /// not an LOD (an LOD change moves the branches). A VISIBILITY switch on part of a prop
        /// while the rest of the same prop is drawn — which is what two disagreeing thresholds
        /// look like. The spread BETWEEN props stays: it is what ModBuild 255 bought and the user
        /// has accepted, and hashing the root rather than the renderer preserves it exactly.
        /// </para>
        /// </summary>
        private float StaggerThresholdFor(Renderer r)
        {
            Transform? root = StaggerRootOf(r, out bool resolved);
            if (!resolved)
                NoteStaggerKeyMiss(r);
            // Knuth multiplicative hash on the key id, folded to [0.10, 0.92).
            uint h = (uint)(root != null ? root.GetInstanceID() : r.GetInstanceID()) * 2654435761u;
            return 0.10f + (h >> 8) / (float)(1 << 24) * 0.82f;
        }

        /// <summary>The prop unit this renderer staggers with, or null when it staggers alone.
        /// O(1): a read-only lookup in the memo <c>WarmStaggerKeys</c> filled during the commit —
        /// see <see cref="StaggerThresholdFor"/> for why this may never resolve the root itself.
        /// <paramref name="resolved"/> is false ONLY when the memo has no entry for the parent at
        /// all, which is the one state in which two members of one prop can key differently; the
        /// SHOW EDGE audit uses the same grouping so that its TORN RETURN term tests exactly the
        /// pieces this rule claims to have kept together.</summary>
        private Transform? StaggerRootOf(Renderer r, out bool resolved)
        {
            Transform? parent = r.transform.parent;
            if (parent == null)
            {
                resolved = true; // nothing to group by — every path keys on the renderer's own id
                return null;
            }
            resolved = _live.PropUnitRootMemo.TryGetValue(parent, out Transform? root);
            return resolved ? root : null;
        }

        /// <summary>
        /// Is this piece delivered by the foliage <c>_Cutoff</c> LERP in <see cref="DriveProp"/> —
        /// the ONE delivery in this file whose mid-ramp value can discard every texel the
        /// renderer has?
        ///
        /// <para>The three other deliveries cannot. An alpha ramp at fade f still draws the piece
        /// at alpha 1-f. A real Amp dissolve draws a partially dissolved piece. And
        /// <see cref="DriveNativeProp"/> is the WALL'S OWN occlusion-map/_Cutoff sweep, which is
        /// the masonry dissolve the user has accepted on both edges and which this round may not
        /// disturb. Only <c>Mathf.Lerp(p.BaseCutoff, FoliageCutoffEnd, fade)</c> crosses 1, and
        /// texture alpha never exceeds 1 — a fact about textures, not a tuned number — so past
        /// that point the renderer is drawn, paid for and contributes nothing.</para>
        ///
        /// <para>The test mirrors <see cref="DriveProp"/>'s own branch structure exactly:
        /// NativeFade returns first, and inside the block <c>if (ColorId >= 0)</c> wins over
        /// <c>else if (CutoffId >= 0)</c>. If that structure is ever changed this predicate must
        /// change with it; the SHOW EDGE audit reads the property block and will say so.</para>
        /// </summary>
        private static bool RidesCutoffLerp(MountedProp p) =>
            !p.NativeFade && p.ColorId < 0 && p.CutoffId >= 0;

        /// <summary>
        /// SHOW ONE ATTACHMENT PIECE — the single implementation of every applier's show branch,
        /// and with it THE RETURN HALF OF THE STAGGER RULE (ModBuild 265).
        ///
        /// <para>THE RULE: A PIECE IS NOT SHOWN UNTIL IT CAN BE DRAWN AS AUTHORED. On the way
        /// back, a piece we hid stays disabled until the fade reaches the threshold its PROP UNIT
        /// already returns on, and is then enabled in the same frame it is written with its
        /// authored value — never with an intermediate one. Nothing about the way OUT changes:
        /// outbound a piece is <see cref="ReturnPhase.Free"/> the whole way, this method drives
        /// the ordinary ramp, and only <c>want == 2</c> hides it.</para>
        ///
        /// <para>THE NUMBER THAT MADE THE RULE (LogOutput.log, ModBuild 264): SHOW EDGE reports
        /// 123 of 278 and 123 of 234 pieces "shown with a clip value that discards every texel",
        /// every named one of them at fade 0.82-0.92 with <c>_Cutoff</c> 1.07-1.20 against an
        /// authored 0.50. 0.91 is the FIRST frame of an un-fade (fadeStep = 1-exp(-1/90/0.12) =
        /// 0.0885, so fade 1.00 becomes 0.9115 in one frame) and <c>FoliageHideFade</c> is 0.99,
        /// so the old show branch turned those renderers on with <c>Lerp(0.50, 1.20, 0.91)</c> =
        /// 1.14 — every texel discarded — and then resolved them out of nothing as the clip fell
        /// back through 1.0 (at fade 0.714) to 0.50 (at fade 0, t = 0.12*ln(1/0.005) = 0.64 s).
        /// That ramp IS the user's "1s undefinierter Matsch an den Ästen".</para>
        ///
        /// <para>WHY THE STAGGER THRESHOLD AND NOT "WAIT FOR FADE 0". Waiting for the drive value
        /// to reach the authored one means waiting for fade 0, which is the frame the wall is
        /// solid: the whole cutoff population would then return in ONE pop, 0.36-0.63 s after the
        /// staggered population it shares its trees with — the ModBuild-261 TORN RETURN defect
        /// rebuilt on purpose. <see cref="StaggerThresholdFor"/> is keyed on the PROP UNIT ROOT,
        /// so gating on it returns a tree's cutoff leaves on the same frame as that same tree's
        /// channel-less ones. No new threshold is introduced: this is the existing rule read in
        /// the other direction.</para>
        ///
        /// <para>LATENCY — NOTHING IS DELAYED. A cutoff piece is FULLY AUTHORED at fade
        /// threshold ∈ [0.10, 0.92), i.e. 0.010-0.276 s into the un-fade (t = 0.12*ln(1/fade)),
        /// against 0.64 s before this change. Every such piece reaches its final look EARLIER
        /// than it used to, by 33 to 57 frames at 90 Hz. What is later is only the frame it
        /// starts being DRAWN, and until now every one of those frames drew nothing.</para>
        ///
        /// <para>COST: one enum compare per piece per frame in the steady state, plus the O(1)
        /// <see cref="StaggerThresholdFor"/> memo read that only the held cutoff pieces make and
        /// only until they return. No property-block read-back and no scene walk. MULTIPLAYER:
        /// presentation only — local scene state, no networked state, no wire record; a peer's
        /// synced fade drives the identical ramp through the identical appliers.</para>
        ///
        /// <para>WHAT WOULD FALSIFY IT: SHOW EDGE's "shown with a clip value that discards every
        /// texel" must read 0. It reads the renderer's own property block on the edge frame, so
        /// no ledger claim can satisfy it. A non-zero residue whose fades are NOT the first
        /// frames of an un-fade would be a piece adopted mid-return that this rule never held
        /// (<see cref="ReturnPhase.Free"/>) — a different defect, and the fades in the line say
        /// which one it is.</para>
        /// </summary>
        /// <param name="rampFade">The fade this applier would otherwise DRIVE the piece at
        /// (ApplyMounted leads by <c>MountedFadeLead</c>, the held branches pass 1).</param>
        /// <param name="segFade">The SEGMENT's own fade — the quantity
        /// <see cref="StaggerThresholdFor"/> is compared against everywhere else, so that a prop
        /// split across two appliers cannot key on two different numbers.</param>
        private void ShowAttachmentPiece(MountedProp p, Renderer r, float rampFade, float segFade)
        {
            switch (p.Return)
            {
                case ReturnPhase.HeldHidden when RidesCutoffLerp(p):
                    if (segFade >= StaggerThresholdFor(r))
                    {
                        // Still hidden: nothing written, nothing drawn. The disable is not a
                        // no-op — Apparance re-enables regenerated pieces over a wall we hold
                        // (the reason no applier here has a held-state early-out), and such a
                        // piece would otherwise draw the held block's clip 1.20 and show
                        // nothing while its opaque slots kept drawing.
                        if (r.enabled)
                            r.enabled = false;
                        ShowEdge(p, false, segFade);
                        return;
                    }
                    DriveProp(p, 0f); // authored, on the very frame it is turned on
                    p.Return = ReturnPhase.ReturnedAuthored;
                    break;
                case ReturnPhase.ReturnedAuthored:
                    // THE LATCH IS RELEASED WHEN THE PIECE IS OUTBOUND AGAIN — the same
                    // threshold, read in the same direction as the hold. Without this, a return
                    // INTERRUPTED by a new fade (the user turns his head back mid-ramp, which is
                    // the common case, not the corner one) would leave the piece latched at its
                    // authored look all the way up to FoliageHideFade and then switch it off in
                    // one frame: the fade-OUT pop this subsystem spent ModBuild 255 removing.
                    // Above the threshold the ordinary ramp is safe by construction — the clip
                    // only reaches 1.0 at fade 0.714, and every value below that still draws —
                    // so resuming it here cannot produce the blank frame the hold exists for.
                    if (segFade >= StaggerThresholdFor(r))
                    {
                        p.Return = ReturnPhase.Free;
                        DriveProp(p, rampFade);
                    }
                    break; // otherwise its block already holds the authored values; re-driving is the bug
                default:
                    DriveProp(p, rampFade); // outbound, or a piece the cutoff lerp cannot blank
                    break;
            }
            ShowEdge(p, true, segFade);
            if (!r.enabled)
                r.enabled = true;
        }

        private static bool IsStandingPiece(in Bounds rb)
        {
            Vector3 s = rb.size;
            float thin = Mathf.Min(s.x, s.z);
            return s.y >= StandingPieceRatio * thin;
        }

        /// <summary>One piece of a wall against the head→sample ray, with the same "clearly
        /// before the point" rule the broad phase uses. Counts the piece so the caller can tell
        /// "nothing blocked" from "nothing to ask", and skips anything that fails the standing
        /// test — since ModBuild 255 there is no such test in this path; see
        /// <see cref="IsStandingPiece"/> for why it was retired.</summary>
        private static bool HitsPiece(Renderer? r, Ray ray, float dist, float eps, Vector3 sample,
            ref int meshes, out bool byContains)
        {
            byContains = false;
            if (r == null)
                return false;
            Bounds rb = r.bounds;
            meshes++;
            if (!rb.IntersectRay(ray, out float rd))
                return false;
            if (rd < dist - eps)
                return true;
            // CONTAINS is reported separately (ModBuild 257) and NOT changed. A floor sample
            // inside a piece's box is blocked from every head position there is, so a piece that
            // straddles the floor plane sets a coverage FLOOR the wall can never fall below —
            // which is a latch, not a measurement. The instrument has to be able to say how many
            // of a wall's cells are of that kind before anyone touches the clause: see
            // Segment.LastContainsCells.
            byContains = rb.Contains(sample);
            return byContains;
        }

        /// <summary>
        /// Is the head inside one of this segment's own wall MESHES — the honest "camera sealed
        /// in masonry" test, and the only thing that may still hide a wall while the INSIDE rule
        /// holds. Names the mesh so the falsifier can print it.
        ///
        /// <para>The union-box version of this test (<c>seg.Bounds.Contains(headPos)</c>) was the
        /// defect behind the FIRST of the two 2026-08-24 reports: <i>"ich bin voll IN dem Spiel
        /// drin und schaue nach draußen nicht nach drinnen, warum wird es dann ausgeblendet?"</i>
        /// (wandausblendung.jpg). 'Wall 4' in that log owns scattered
        /// <c>PCG_FR_Pillar_Tree_Trunk_01_PR</c> pieces at world XZ (-9.4,-2.4), (-12.9,-0.4),
        /// (-6.9,1.9) and (-10.4,3.9), so its box spans roughly x[-13..-6], z[-3..4],
        /// y[-0.42..5.00] — a player standing on open floor is inside it. 75 of that session's
        /// diag samples show the resulting hard <c>1f</c> (a wall reporting <c>blk16/16 v16</c>
        /// against the room TOTAL while the frustum held only <c>vis 8/16</c>); the ModBuild 241
        /// session the escape hatch was designed against contained exactly TWO in 25 MB.</para>
        ///
        /// <para>Caller-guarded by the cheap union test: a mesh box is contained in the union
        /// box, so <c>!seg.Bounds.Contains(headPos)</c> already proves this false.</para>
        /// </summary>
        private static bool HeadInsideWallMesh(Segment seg, Vector3 headPos, out string meshName)
        {
            meshName = "-";
            for (int i = 0; i < seg.Renderers.Count; i++)
            {
                if (ContainsHead(seg.Renderers[i], headPos, ref meshName))
                    return true;
            }
            // FOLIAGE IS DELIBERATELY NOT ASKED HERE, and this reverses a ModBuild 252 change.
            // 252 added it by symmetry with RayHitsWallMesh — "on a scrub wall the foliage IS
            // the wall" — but the two tests answer different questions. RayHitsWallMesh asks
            // "does this hide the floor", where a bush counts. This asks "is the camera SEALED
            // INSIDE opaque geometry with no way out", which is the only thing that justifies
            // the hard 1f, and a head in a bush is not that: cutout foliage is see-through by
            // construction and the player can simply look past it. The ModBuild 253 log caught
            // the consequence — 'Wall 2' reporting blk 16/16 v16 while the frustum held only
            // vis 15/16, i.e. the whole wall dissolved through the escape hatch because a head
            // brushed a bush's AABB. The hatch is masonry only.
            for (int i = 0; i < seg.Body.Count; i++)
            {
                if (ContainsHead(seg.Body[i].Renderer, headPos, ref meshName))
                    return true;
            }
            return false;
        }

        private static bool ContainsHead(Renderer? r, Vector3 headPos, ref string meshName)
        {
            if (r == null || !r.bounds.Contains(headPos))
                return false;
            meshName = r.name;
            return true;
        }

        /// <summary>
        /// Throttled hardware diagnostic (1 line / <see cref="DiagIntervalSeconds"/>s while
        /// WallFade is active): overall sample/frustum stats plus the top-3 candidate walls
        /// by smoothed fraction — name, raw/EMA fraction, blocked/visible counts, state and
        /// fade, wall-AABB y-range, blocked epsilon, and an explicit "!ABOVE-WALL" marker
        /// when every sample sits above that wall's AABB top (the round-4 scale bug this
        /// line exists to catch). Next hardware log pinpoints any remaining miss from this.
        /// </summary>
        /// <summary>
        /// R2 deliverable: every debounced fade state flip logs the wall's fade-shader
        /// name(s) + variant and which held-state math therefore applies (rare event —
        /// unthrottled on purpose so hardware logs pin each fade to its variant).
        /// </summary>
        private void LogStateFlip(Segment seg)
        {
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            if (!seg.FromWallCache)
                wall += "~"; // shader-adopted group (tile/parent-anchored), not a cache wall
            string variant = seg.VariantHigh ? (seg.VariantLow ? "HIGH+LOW" : "HIGH") : "LOW";
            if (seg.State)
            {
                // Which renderers this fade actually touches (name@AABB-top, first six): the
                // decisive line when a "hole" appears — if a floor piece is listed here, it is
                // either a ground renderer the strip missed or ground fused into a wall MESH.
                var rl = new System.Text.StringBuilder();
                int listed = 0;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                        continue;
                    if (listed++ >= 6) { rl.Append(", …"); break; }
                    if (rl.Length > 0) rl.Append(", ");
                    rl.Append(r.name).Append('@').Append(r.bounds.max.y.ToString("F1"));
                }
                string cutoff = $"map occ(r=1,a=0)→m=0, _Cutoff={seg.HeldCutoff:0.00} " +
                    (seg.CutoffAuthored ? "(authored)" : "(fallback)");
                VRLog.Info(Name,
                    $"fade ON '{wall}' shader '{seg.ShaderNames}' [{variant}] " +
                    $"({seg.Renderers.Count} renderer(s), {seg.ToggleNative} toggle-native: " +
                    $"{rl}; +{seg.Foliage.Count} foliage, " +
                    $"+{seg.Siblings.Count} asset-sibling(s), +{seg.Mounted.Count} mounted " +
                    $"prop(s) [{MountedNames(seg)}], +{seg.Stacked.Count} stacked shell " +
                    $"piece(s), +{seg.Body.Count} plain body mesh(es); " +
                    // ROUND 15: the blanket '[enabled-only]' marker was a lie once the
                    // attachments learned to dissolve — it is now the live channel breakdown,
                    // and 'enabled-only 0' is the proof that nothing in this fade pops. The
                    // DISSOLVE CENSUS line names any remainder and why.
                    $"delivery: {DissolveBreakdown(seg)}) — " +
                    $"held state: " +
                    cutoff + " → " +
                    (seg.VariantHigh
                        ? "world-Y foundation gradient solid (S=1 ⇒ clip=1-c), upper wall " +
                          "discarded (clip=-c); game-native screen vignette/0.02·dist terms " +
                          "remain inside S — flat-game faded look"
                        : "discard above object-Y 0.4 only — base course below the hard " +
                          "shader gate stays solid (flat-game faded look, view-independent)"));
            }
            else
            {
                VRLog.Info(Name, $"fade OFF '{wall}' [{variant}] — MPB removed, solid.");
            }
        }

        private void LogDiagnostic(Vector3 headPos, int visibleCount)
        {
            if (_live.Segments.Count == 0)
                return;
            Segment? s1 = null, s2 = null, s3 = null;
            foreach (Segment seg in _live.Segments.Values)
            {
                if (!seg.HasBounds)
                    continue;
                if (s1 == null || seg.Smooth > s1.Smooth) { s3 = s2; s2 = s1; s1 = seg; }
                else if (s2 == null || seg.Smooth > s2.Smooth) { s3 = s2; s2 = seg; }
                else if (s3 == null || seg.Smooth > s3.Smooth) { s3 = seg; }
            }
            if (s1 == null)
                return;

            _diagSb.Length = 0;
            _diagSb.Append("diag: vis ").Append(visibleCount).Append('/').Append(_live.AllSamples.Count)
                   .Append(" headY ").Append(headPos.y.ToString("F2"))
                   .Append(" sampY[").Append(_live.SampleYMin.ToString("F2")).Append("..")
                   .Append(_live.SampleYMax.ToString("F2")).Append(']');
            AppendSegDiag(s1);
            AppendSegDiag(s2);
            AppendSegDiag(s3);
            VRLog.Info(Name, _diagSb.ToString());
        }

        private void AppendSegDiag(Segment? seg)
        {
            if (seg == null)
                return;
            string name = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            if (name.Length > 24)
                name = name.Substring(0, 24);
            if (!seg.FromWallCache)
                name += "~"; // shader-adopted group
            Bounds b = seg.Bounds;
            _diagSb.Append(" | '").Append(name)
                   .Append("' r").Append(seg.RoomIndex);
            // ROOM SEAM (2026-08-09): a wall between two rooms is judged against both — print
            // which one actually decided, so the next hardware log reads the side directly
            // instead of leaving it to inference ("r0>1" = own room 0, room 1 won the max).
            if (seg.BorderRooms.Count > 0)
            {
                _diagSb.Append('>');
                if (seg.LastDecidingRoom >= 0)
                    _diagSb.Append(seg.LastDecidingRoom);
                else
                    _diagSb.Append('?');
            }
            _diagSb.Append(" raw").Append(seg.LastRaw.ToString("F2"))
                   .Append(" ema").Append(seg.Smooth.ToString("F2"))
                   .Append(" blk").Append(seg.LastBlocked).Append('/').Append(seg.LastRoomTotal)
                   .Append(" v").Append(seg.LastRoomVisible)
                   .Append(seg.State ? " ON " : " off ").Append(seg.Fade.ToString("F2"))
                   .Append(" wy[").Append(b.min.y.ToString("F2")).Append("..")
                   .Append(b.max.y.ToString("F2")).Append(']')
                   .Append(" e").Append(seg.BlockEps.ToString("F2"))
                   .Append(seg.VariantHigh ? (seg.VariantLow ? " vH+L" : " vHIGH")
                       : (seg.VariantLow ? " vLOW" : seg.Body.Count > 0 ? " vBODY" : " vLOW"));
            // Stacked shell pieces riding this wall (keep stories) — only printed when any
            // exist, so scenes without superstructures keep their diag lines unchanged.
            if (seg.Stacked.Count > 0)
                _diagSb.Append(" S").Append(seg.Stacked.Count);
            // Plain-mesh wall body (round 6): masonry without a fade shader, enabled-only.
            if (seg.Body.Count > 0)
                _diagSb.Append(" B").Append(seg.Body.Count);
            // Tripwire: this wall's own room's sample plane sits above the wall AABB top —
            // the exact frame-mismatch class the round-6 hardware log caught (sampY 9.05 vs
            // wall tops ≤3.67: bounds-derived plane, occlusion-proxy meshes).
            int room = seg.RoomIndex;
            float planeY = room >= 0 && room < _live.RoomFloorY.Count
                ? _live.RoomFloorY[room] + FloorSampleEpsilon
                : _live.SampleYMin;
            if (planeY > b.max.y)
                _diagSb.Append(" !ABOVE-WALL");
            if (room >= 0 && room < _live.RoomFloorAnchored.Count && !_live.RoomFloorAnchored[room])
                _diagSb.Append(" !UNANCHORED");
            // Room grid empty (over the sample budget) or no room at all: the wall is held
            // solid by the reveal fail-safe — visible in the log as the reason it never fades.
            if (room < 0 || (room < _live.RoomSampleCount.Count && _live.RoomSampleCount[room] <= 0))
                _diagSb.Append(" !NOGRID");
            // A single mesh whose AABB is fat in BOTH horizontal axes (ring/corner piece) cannot
            // be split further — its coverage numbers may read permanently high. Flagged so the
            // hardware log distinguishes "genuinely occluding" from "AABB artifact"; !ENGULF
            // additionally marks the ones the containment test therefore holds solid.
            if (Mathf.Min(b.size.x, b.size.z) > GroupSlabMaxHorizontal)
                _diagSb.Append(" !FAT");
            if (seg.Engulfing)
                _diagSb.Append(" !ENGULF");
            // Doorway recognition (user ruling 2026-08-02): held permanently solid.
            if (seg.DoorRoot != null)
                _diagSb.Append(" DOORWAY");
            // Gate column (user ruling 2026-08-07): the doorway-EMBEDDING wall — fades.
            if (seg.IsGateColumn)
                _diagSb.Append(" GATE");
        }

        // ---- fade delivery ----------------------------------------------------------------

        /// <summary>
        /// Apply the segment's fade through the shader's own path (see class header).
        /// Reapplied every frame while faded because Apparance may regenerate wall renderers
        /// mid-fade; a null renderer triggers a prompt rescan.
        /// </summary>
        /// <summary>Return one foliage renderer to its vanilla state (visible, no MPB).</summary>
        private static void RestoreFoliageRenderer(MeshRenderer r)
        {
            if (r == null)
                return;
            if (!r.enabled)
                r.enabled = true;
            r.SetPropertyBlock(null);
        }

        /// <summary>Restore ALL of a segment's foliage — called whenever the segment leaves the
        /// table or goes solid, so no bush can stay hidden without an owner.</summary>
        // Non-static since ModBuild 255: the swap-removal in-edge is counted here, and the STEP
        // census is what proves the fade-out / fade-in asymmetry.
        private void RestoreSegmentFoliage(Segment seg)
        {
            if (seg.FoliageState == 0 && seg.FoliageProps.Count == 0)
                return;
            seg.FoliageState = 0;
            foreach (MeshRenderer f in seg.Foliage)
            {
                if (f != null)
                    RestoreFoliageRenderer(f);
            }
            // ModBuild 254: foliage carries real dissolve records now, so restoring it has to
            // undo them exactly the way siblings do — authored materials back and OUR copies
            // destroyed. Anything still recorded is no longer in the foliage list (Apparance
            // churn, a dead renderer): its copy must die with it, or the swap leaks a material
            // per regenerated bush.
            foreach (MountedProp p in seg.FoliageProps.Values)
            {
                Renderer r = p.Renderer;
                bool wroteBlock = p.NativeFade || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                if (p.SwapCopies != null)
                    NoteSwapEdge(seg, p, installed: false); // in-edge: already solid
                RestorePropSwap(p, r);
                if (r == null)
                    continue;
                if (wroteBlock)
                    r.SetPropertyBlock(null);
                if (!r.enabled)
                    r.enabled = true;
            }
            seg.FoliageProps.Clear();
        }

        /// <summary>Put ONE asset sibling back exactly as authored: its dissolve channel undone
        /// (authored materials back, our copies destroyed), property block cleared, renderer
        /// visible again. Round 15 — before that, siblings were a pure enabled toggle.</summary>
        private static void RestoreSiblingProp(Segment seg, MeshRenderer? r)
        {
            if (r == null)
                return;
            if (seg.SiblingProps.TryGetValue(r, out MountedProp? p))
            {
                seg.SiblingProps.Remove(r);
                bool wroteBlock = p.NativeFade || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                RestorePropSwap(p, r);
                if (wroteBlock)
                    r.SetPropertyBlock(null);
            }
            if (!r.enabled)
                r.enabled = true;
        }

        /// <summary>Restore ALL of a segment's asset siblings (doors/trim of a mixed asset) —
        /// called on every path where the segment stops owning them (unfade, segment drop,
        /// group split, toggle-off, teardown), so no door can stay hidden without an owner.
        /// Round 15: also undoes their dissolve channel, and sweeps records whose renderer died
        /// so a material copy can never leak.</summary>
        private static void RestoreSegmentSiblings(Segment seg)
        {
            if (seg.SiblingState == 0)
                return;
            seg.SiblingState = 0;
            foreach (MeshRenderer s in seg.Siblings)
                RestoreSiblingProp(seg, s);
            // Anything still recorded is no longer in the sibling list (asset churn / a dead
            // renderer): our copies must die with it either way, and a survivor is fully
            // re-authorized here rather than left swapped with no owner.
            foreach (MountedProp p in seg.SiblingProps.Values)
            {
                Renderer r = p.Renderer;
                bool wroteBlock = p.NativeFade || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                RestorePropSwap(p, r);
                if (r == null)
                    continue;
                if (wroteBlock)
                    r.SetPropertyBlock(null);
                if (!r.enabled)
                    r.enabled = true;
            }
            seg.SiblingProps.Clear();
        }

        /// <summary>
        /// Dissolve the segment's asset siblings alongside its fade (Torbogen ruling: the WHOLE
        /// doorway asset disappears, not just its shader-matched frame/pillars).
        ///
        /// ROUND 15 — siblings were the last enabled-only class: they stayed fully solid through
        /// the dissolve and switched off at the end, on the (round-3) assumption that "siblings
        /// run arbitrary opaque shaders where a cutoff MPB means nothing". That assumption is
        /// what the gate bug disproved for every attachment type — a channel-less material gets
        /// COPIES on the game's masonry fade shader, a toggle-native one gets the wall's own
        /// map/_Cutoff ramp (see WallSegmentFade.Dissolve.cs). So they ramp now, on both edges,
        /// with the guaranteed renderer-disable still at the end of the sweep.
        /// </summary>
        private void ApplySiblings(Segment seg)
        {
            if (seg.Siblings.Count == 0)
            {
                if (seg.SiblingState != 0)
                    RestoreSegmentSiblings(seg);
                return;
            }
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentSiblings(seg);
                return;
            }
            // No held-state early-out (round 5, regen churn): re-hide per frame — in the
            // steady state this is one enabled compare per sibling.
            foreach (MeshRenderer s in seg.Siblings)
            {
                if (s == null)
                    continue;
                if (!seg.SiblingProps.TryGetValue(s, out MountedProp? p))
                {
                    p = ClassifyProp(s);
                    seg.SiblingProps[s] = p;
                }
                bool hadSwap = p.SwapCopies != null;
                EnsureDissolveChannel(p);
                if (!hadSwap && p.SwapCopies != null)
                    NoteSwapEdge(seg, p, installed: true);
                // MODBUILD 271 — THE UNION RULE, at this lane's driving call site. The fade is
                // the MAX of this segment's and of every fade-eligible segment this sibling's own
                // AABB actually reaches into, so a door leaf standing inside a neighbour's hole
                // can never be driven at a LOWER fade than that hole. Nothing here writes a
                // segment; see FadeDriver._mountedUnion for the rule and its constraint.
                //
                // THE LANE'S OWN LIMIT, stated rather than glossed: the whole-segment early-out
                // above is deliberately NOT lifted for siblings and foliage, unlike the two
                // dressing lanes (ApplyMounted, ApplyUnitDressing). Their release is
                // segment-scoped by construction — RestoreSegmentSiblings clears SiblingProps and
                // undoes the material swaps for the whole list at once — so a PER-PIECE release
                // would leave our dissolve copies installed on a piece nobody drives, which is
                // exactly the SHOW EDGE line's "not as authored" fault. So on this lane the rule
                // can raise a piece whose OWN wall is already fading and cannot rescue one whose
                // own wall is solid. The UNION RULE census reports both numbers separately (the
                // rule's own count, and what the appliers actually raised), so this limit shows
                // up as a gap in the log rather than as a silence.
                float eff = UnionFade(s, seg);
                DriveProp(p, eff);
                if (eff >= FoliageHideFade)
                {
                    if (s.enabled)
                        s.enabled = false;
                }
                else if (!s.enabled)
                {
                    s.enabled = true;
                }
            }
            seg.SiblingState = want;
        }

        /// <summary>
        /// Drive the segment's foliage attachments alongside its fade: mid-dissolve the cutout
        /// leaves ride an alpha-cutoff ramp (visually the same dissolve as the wall), and in the
        /// held state the renderer is disabled outright so opaque twig materials vanish too.
        /// All of it reverses exactly on unfade.
        /// </summary>
        private void ApplyFoliage(Segment seg)
        {
            if (seg.Foliage.Count == 0)
            {
                if (seg.FoliageState != 0)
                    RestoreSegmentFoliage(seg);
                return;
            }
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentFoliage(seg);
                return;
            }
            // No held-state early-out (round 5, regen churn): a bush regenerated while the
            // wall is held faded must be re-hidden this frame, not never.
            foreach (MeshRenderer f in seg.Foliage)
            {
                if (f == null)
                    continue;
                // ModBuild 254: classify per material rather than writing one shared _Cutoff MPB
                // blind to all of them — ClassifyProp reads what this material can actually
                // express, and DriveProp ramps it from the material's OWN authored base value.
                // ModBuild 255 kept that and dropped the swap fallback it originally came with
                // (see the note further down).
                if (!seg.FoliageProps.TryGetValue(f, out MountedProp? p))
                {
                    p = ClassifyProp(f);
                    seg.FoliageProps[f] = p;
                }
                // FOLIAGE IS NEVER MATERIAL-SWAPPED (ModBuild 255). EnsureDissolveChannel is
                // deliberately NOT called here: for a leaf card, replacing an alpha-cutout
                // shader with the masonry fade shader is the single most visible thing that can
                // happen to it, and it happens in one frame at the START of the fade-out. That
                // is the asymmetry in the report — out pops, in animates — because the same
                // change on the way back lands after the piece is already solid.
                //
                // A piece with a channel of its own ramps on it. A piece WITHOUT one is
                // dissolved by STAGGERING its disable across the ramp instead: each piece gets a
                // stable threshold from its own identity hash, so a wall's foliage switches off
                // progressively over the transition rather than all at once at the end. With 123
                // attachments on 'Wall 2' and 345 in the scenario, that is fine-grained enough
                // to read as the mass dissolving, and it needs NO shader property, NO material
                // change and NO knowledge of the tileset — which is the point, because whether
                // Amp_Basic_Foliage even exposes a usable cutoff is not something this codebase
                // has ever been able to confirm.
                bool ownChannel = p.System != null || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                NoteFoliageChannel(seg, p);
                // MODBUILD 271 — THE UNION RULE at this lane's driving call sites: the MAX of
                // this segment's fade and of every fade-eligible segment this leaf's own AABB
                // actually reaches into. A bush standing inside a neighbour's hole can never be
                // driven at a LOWER fade than that hole. Nothing here writes a segment. The
                // whole-segment early-out above is NOT lifted on this lane — see the same note
                // in ApplySiblings for why (RestoreSegmentFoliage is segment-scoped and undoes
                // the material swaps for the whole list), and see the UNION RULE census, which
                // reports the rule's own count and what the appliers actually raised separately
                // so this limit reads as a gap rather than as a silence.
                float eff = UnionFade(f, seg);
                if (ownChannel)
                {
                    if (eff >= FoliageHideFade)
                    {
                        DriveProp(p, eff);
                        p.Return = ReturnPhase.HeldHidden; // ModBuild 265 — see ShowAttachmentPiece
                        if (f.enabled)
                            f.enabled = false;
                        ShowEdge(p, false, eff);
                    }
                    else
                    {
                        // ModBuild 265: this branch is now audited. It never called ShowEdge, so
                        // the foliage pieces WITH a channel of their own were the one attachment
                        // population the SHOW EDGE line could not see — the same blind spot the
                        // TORN RETURN term had before ModBuild 261. Expect the line's denominator
                        // to grow; the fault terms are what must read 0.
                        ShowAttachmentPiece(p, f, eff, eff);
                    }
                    continue;
                }
                // Staggered: hidden once the ramp passes this PROP UNIT's threshold (ModBuild 261
                // — it was the renderer's own until the conifers tore, "die Blätter laden nach").
                // The order is arbitrary but STABLE: a piece does not flicker by re-randomising
                // between frames, and every renderer of one prop leaves at the same instant.
                bool hide = eff >= StaggerThresholdFor(f);
                if (f.enabled == hide)
                    f.enabled = !hide;
                // ModBuild 261: the TORN RETURN falsifier used to watch the unit-dressing list
                // only — the half that was already keyed on the prop unit. The half that could
                // disagree with it was this one, and it was invisible. Same call, same records.
                ShowEdge(p, !hide, eff);
            }
            seg.FoliageState = want;
        }

        private void Apply(Segment seg)
        {
            // DOORWAY segments (user ruling 2026-08-02) take this same path: the decision
            // loop pins their state to solid, so the fade decays to 0 and any residual
            // MPB/foliage/sibling state clears through the normal branches below.
            ApplyFoliage(seg);
            ApplySiblings(seg);
            ApplyMounted(seg);
            ApplyStacked(seg);
            ApplyBody(seg);
            // ROUND-15 DISSOLVE CENSUS: the appliers above have just established each piece's
            // dissolve channel, so this is the moment the breakdown is true. Logged once per
            // fade episode (whatever drove it — local decision, peer sync, gate lift), re-logged
            // only when the enabled-only count changes. See WallSegmentFade.Dissolve.cs.
            if (seg.Fade > 0f)
                LogDissolveCensus(seg);
            else
                seg.DissolveCensusLogged = false;
            if (seg.Fade <= 0f)
            {
                if (seg.HasBlock)
                {
                    seg.HasBlock = false;
                    NoteBlockEdge(seg, installed: false); // in-edge: already solid, invisible
                    foreach (MeshRenderer r in seg.Renderers)
                    {
                        if (r != null)
                            r.SetPropertyBlock(null);
                    }
                }
                return;
            }

            if (!EnsureTextures())
                return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetInteger(ToggleWallFadeId, 1);
            // HIGH-variant map scale M = m·_ToggleWallfade (cb0[6].x) — pin to 1 so the held
            // math below holds regardless of the material's authored value; the LOW shader
            // has no such property (MPB entry simply unused there).
            _mpb.SetFloat(ToggleWallfadeMatId, 1f);
            // TOGGLE-NATIVE materials (round 8, Amp_Basic_N_MRAO masonry): open their gate
            // too — the same fade subgraph behind a differently-named material switch.
            // Unused entry on the classic WallFade shaders, exactly like _ToggleWallfade on
            // LOW. KNOWN RISK (logged per material by LogToggleNativeMaterialOnce): if
            // Amplify compiled the switch as a compile-time keyword, this float is inert and
            // the material becomes the shader-swap candidate — the toggle diag line plus the
            // next hardware round adjudicate.
            _mpb.SetFloat(WallFadeOnMatId, 1f);
            // THE NOISE MAP IS THE DISSOLVE. THE OCCLUDED MAP IS A SWITCH. (ModBuild 255 —
            // reverting my own ModBuild 252 change, which was wrong and is the primary cause of
            // the report "das aufploppen und verschwinden der Wände ist jetzt plötzlich keine
            // smoothe animation mehr" — note "jetzt plötzlich", i.e. since that build.)
            //
            // 252 replaced the noise ramp with a sweep of _Cutoff against the OCCLUDED map, on
            // the reasoning that the HIGH shader's world-Y term S is a gradient, so a rising c
            // would dissolve the wall top-down. That reasoning ignored the clause written three
            // lines further down in this very file: with M = 0 the shader multiplies the
            // NOISE BY ZERO. Removing the noise removes the only per-pixel variation the cutoff
            // had to sweep across, which leaves clip = -c for the whole upper wall: solid while
            // c < 0, discarded the instant c > 0. With c = Lerp(-0.05, 0.50, Fade) that crossing
            // happens at Fade ≈ 0.09 — one frame into a 0.35 s ramp. It was never a gradient
            // sweep; it was a one-frame switch wearing a ramp's clothes.
            //
            // HARDWARE CONFIRMS IT, in both directions. Frame-by-frame on the user's 30 fps
            // capture (wände_probleme.mp4): pop-OUT at #148→#149 (t 4.900→4.933) and pop-IN at
            // #349→#350 (t 11.600→11.633, camera shift measured at exactly (0,0)), each a single
            // 33 ms step with flat patch means on both sides and ZERO intermediate samples. A
            // 0.35 s ramp would have produced about ten.
            //
            // So the noise path is restored for every variant. Its cost is the known step at the
            // Fade == 1 boundary — the foundation band winks as the map swaps — which is real,
            // is what 252 set out to fix, and is a band at the wall's base rather than the whole
            // wall. It is the pre-252 behaviour that drew no complaint for many builds. The
            // ANIMATION line reports it as the residual rather than claiming it away.
            if (seg.Fade >= 1f)
            {
                // Held fully faded (R3, foundation-band fix): constant r=1,a=0 map → map
                // term m = 1-r = 0 view-independently (a=0 fails the depth compare for
                // every visible fragment under either Z convention), _Cutoff = the
                // material's own authored Mask Clip Value — exactly the state the flat
                // game's occlusion map produces over a revealed room. LOW: clip = -c < 0
                // discards everything ABOVE the shader's hard objY-0.4 gate, base course
                // solid. HIGH: M=0 → the shader's own world-Y ramp keeps the foundation
                // gradient solid (S=1 → A·B=1, noise ×0) and discards the upper wall
                // (S=0 → clip = -c). Full math + residual game-native vignette terms in
                // the class header.
                _mpb.SetTexture(TilesOcclusionMapId, _occludedTex!);
                _mpb.SetFloat(CutoffId, seg.HeldCutoff);
                NoteAnimationPath(seg, smooth: false);
            }
            else
            {
                // Dissolve: sweep the clip threshold across the noise texture's value range
                // (screen-space pattern — cosmetic, confined to the ~0.35s transition). This is
                // the ONLY path in this method that produces intermediate pixels: m = 1-r varies
                // per texel across the noise, so a rising c retires the wall progressively.
                _mpb.SetTexture(TilesOcclusionMapId, _noiseTex!);
                // THE RAMP STARTS BELOW ZERO ON PURPOSE (ModBuild 256). seg.Fade cannot be
                // observed near 0: fadeStep = 1-exp(-dt/0.12) is ~0.088 at 90 Hz, so the very
                // first frame after a state flip already reads Fade ≈ 0.09. With the old
                // Lerp(-0.05, 1, Fade) that frame carried c ≈ +0.045, which clips every fragment
                // whose noise m = 1-r falls under it — roughly 5 % of the wall's pixels gone in
                // one frame, on geometry that was fully solid the frame before. That is the
                // out-edge the STEP census counts (22 block installs at fade 0.083-0.093 in the
                // ModBuild 255 log) and it is a step bolted onto the ramp, not part of it.
                //
                // Starting at -0.15 keeps c NEGATIVE for the whole first frame (at Fade 0.09,
                // c = -0.046), and c < 0 clips nothing at all because m >= 0 everywhere. The
                // wall's first faded frame is therefore pixel-identical to its solid state and
                // the dissolve begins on frame two, inside the ramp where it belongs. The cost
                // is that the sweep now covers 1.15 of range instead of 1.05 over the same
                // 0.35 s — imperceptibly faster, and it still ends fully clipped at Fade 1.
                _mpb.SetFloat(CutoffId, Mathf.Lerp(-0.15f, 1f, seg.Fade));
                NoteAnimationPath(seg, smooth: true);
            }

            if (!seg.HasBlock)
                NoteBlockEdge(seg, installed: true); // out-edge: solid geometry, fully visible
            seg.HasBlock = true;
            bool lostRenderer = false;
            foreach (MeshRenderer r in seg.Renderers)
            {
                if (r == null)
                {
                    lostRenderer = true;
                    continue;
                }
                r.SetPropertyBlock(_mpb);
            }
            if (lostRenderer)
                _nextRescan = 0f; // wall regenerated mid-fade — re-collect promptly
        }

        // ---- segment / play-area bookkeeping ------------------------------------------------

        /// <summary>
        /// Refresh the play-area bounds (from the generator's live room-renderer list) and the
        /// wall-segment table (from ProceduralWall.m_WallCache, publicized static). Renderers
        /// are matched by shader name ("WallFade") so props/doors under the same entity are
        /// never touched.
        /// </summary>
        /// <summary>
        /// FIGURE RESTITUTION (round 7): sweep the shared hidden/ramped ledger against
        /// <see cref="IsFigureOrActorRenderer"/> and restore every violator NOW — a figure
        /// renderer adopted before the guard existed (or through any future gap) must come
        /// back the moment the guard classifies it, and the guarded collectors + sticky
        /// loops will not re-take it. Runs first in every rescan; logs once per incident.
        /// </summary>
        private readonly List<MountedProp> _figurePurgeScratch = new();

        private void PurgeFigureRenderers()
        {
            if (_mountedTouched.Count == 0)
                return;
            _figurePurgeScratch.Clear();
            foreach (MountedProp p in _mountedTouched.Values)
            {
                // MODBUILD 266: a piece the WALL GENERATOR built and no actor owns is wall
                // dressing, so the restitution sweep must not take it straight back off the
                // lane that just adopted it (a remedy undone by the guard it is exempt from
                // would have shipped as "no improvement" — the gated-remedy failure this repo
                // has paid for). The actor veto inside IsWallGeneratedDressing is what keeps
                // this narrower than the guard, never wider: a real figure fails it twice over.
                if (p.Renderer != null && IsFigureOrActorRenderer(p.Renderer)
                    && !IsWallGeneratedDressing(p.Renderer))
                {
                    _figurePurgeScratch.Add(p);
                }
            }
            if (_figurePurgeScratch.Count == 0)
                return;
            var names = new System.Text.StringBuilder();
            foreach (MountedProp p in _figurePurgeScratch)
            {
                if (names.Length > 0)
                    names.Append(", ");
                names.Append('\'').Append(p.Renderer.name).Append('\'');
            }
            // ***THE RESTITUTION ITSELF. NOT A DIAGNOSTIC. DO NOT FOLD IT BACK INTO THE LOOP
            // ABOVE.*** Until ModBuild 284 this RestoreProp call sat inside the name-building
            // loop, so a pass that trimmed the WARN's string building would have deleted the
            // round-7 restitution with it — this project's ledger has an entry for exactly that
            // (a fix living inside the instrument meant to test it) and this file has another
            // one at ReleaseSamplingSuspension. The split is order-preserving and read-identical:
            // _figurePurgeScratch comes from _mountedTouched, which is keyed BY RENDERER, so no
            // two entries share a renderer and restoring one cannot change another's name.
            foreach (MountedProp p in _figurePurgeScratch)
                RestoreProp(p);
            VRLog.Warn(Name,
                $"FIGURE RESTITUTION: restored {_figurePurgeScratch.Count} previously-adopted "
                + $"FIGURE renderer(s) ({names}) — figures are NEVER touched by any wall "
                + "system (round-7 ruling, same severity as the Lights rule).");
            _figurePurgeScratch.Clear();
        }

        // ---- the rescan pipeline (PERF S2) -------------------------------------------------

        /// <summary>
        /// The scene's STRUCTURAL SIGNATURE: the counters that move when a renderer this system
        /// cares about is CREATED. Cheap by construction — five list/registry counts, no walk.
        ///
        /// <para>Every one of these is monotonic within a scene and every one of them is the
        /// game's own bookkeeping, not ours: <c>m_RoomRenderers</c> grows on a room reveal,
        /// <c>ProceduralWall.m_WallCache</c> grows as Apparance builds walls, and the three
        /// SceneRegistry registries grow as tiles / door props / occlusion volumes enrol in
        /// their own lifecycle methods (they count enrolled entries, destroyed ones included,
        /// so a churned scene still shows the growth). A change here means the last snapshot
        /// can no longer be complete, so the next cycle sweeps.</para>
        /// </summary>
        private static int SceneStructureSignature(TilesOcclusionGenerator gen)
        {
            int sig = 17;
            try
            {
                sig = sig * 31 + gen.m_RoomRenderers.Count;
                sig = sig * 31 + (ProceduralWall.m_WallCache?.Count ?? 0);
                sig = sig * 31 + SceneRegistry.MapTiles.Count;
                sig = sig * 31 + SceneRegistry.DoorProps.Count;
                sig = sig * 31 + SceneRegistry.Volumes.Count;
            }
            catch
            {
                // A game structure mid-build: treat it as "changed" so the cycle sweeps. Never
                // as "unchanged", which would let a reused snapshot outlive its scene.
                return _structureUnknown++;
            }
            return sig;
        }

        private static int _structureUnknown = int.MinValue / 2;

        /// <summary>
        /// Open a rescan cycle: take (or reuse) the scene snapshot, then hand over to
        /// <see cref="StepRescanCycle"/>. The SWEEP is the one part that cannot be sliced —
        /// <c>FindObjectsOfType</c> is atomic and O(every loaded object) — so it gets its own
        /// PerfMonitor scope and is taken as rarely as correctness allows (see
        /// <see cref="SnapshotMaxAgeSeconds"/>).
        /// </summary>
        /// <returns>True when this frame had to take the (atomic, unsliceable) scene sweep, so
        /// the caller can leave the census to the next frame instead of stacking it on top.
        /// </returns>
        private bool BeginRescanCycle(TilesOcclusionGenerator gen, float now, bool urgent)
        {
            _rescanUrgent = urgent;
            int sig = SceneStructureSignature(gen);
            bool mustSweep = _snapshot.Length == 0
                || sig != _snapshotSignature
                || now - _snapshotTakenAt >= SnapshotMaxAgeSeconds
                || float.IsNegativeInfinity(_snapshotTakenAt);
            if (mustSweep)
            {
                using (PerfMonitor.Scope("WallFade.Sweep"))
                {
                    float t0 = (float)RescanClock.Elapsed.TotalMilliseconds;
                    _snapshot = UnityEngine.Object.FindObjectsOfType<Renderer>();
                    float ms = (float)RescanClock.Elapsed.TotalMilliseconds - t0;
                    if (ms > _cycleWorstSweepMillis)
                        _cycleWorstSweepMillis = ms;
                }
                _snapshotSignature = sig;
                _snapshotTakenAt = now;
                _cycleSweeps++;
                _classifyCold = true;
            }
            else
            {
                // WARM: same array, re-read live. See RendererFact for what that can and
                // cannot miss — and _censusMaterialsDirty for the one thing that forces a
                // full re-derivation without a fresh sweep.
                _classifyCold = _censusMaterialsDirty;
            }
            // PERF S5: the dissolve's own material swap is an input the signature cannot see
            // (our copies carry the same shader family, so the fact bits do not move), so the
            // edge is carried to the skip decision explicitly rather than inferred.
            _cycleMaterialsDirty = _censusMaterialsDirty;
            _censusMaterialsDirty = false;
            ResetSceneFactSignature();
            if (_facts.Length < _snapshot.Length)
                _facts = new RendererFact[Mathf.NextPowerOfTwo(Mathf.Max(_snapshot.Length, 256))];
            _factCount = _snapshot.Length;
            _classifyCursor = 0;
            _factWallFade.Clear();
            _factWater.Clear();
            _rescanStage = RescanStage.Classify;
            return mustSweep;
        }

        /// <summary>
        /// Advance the cycle within this frame's budget. CLASSIFY is resumable and touches
        /// nothing; COMMIT is atomic and is where every mutation lives, so it is never split.
        /// </summary>
        private void StepRescanCycle(TilesOcclusionGenerator gen, float now)
        {
            float budget = _rescanUrgent ? ClassifyUrgentBudgetMillis : ClassifyBudgetMillis;
            float frameStart = (float)RescanClock.Elapsed.TotalMilliseconds;

            if (_rescanStage == RescanStage.Classify)
            {
                _cycleClassifyFrames++;
                using (PerfMonitor.Scope("WallFade.Classify"))
                {
                    // ModBuild 279 (Option A) — ONE ANCESTRY MEMO WINDOW PER CLASSIFY BATCH.
                    // RendererFact.Figure needs IsFigureOrActorRenderer for every renderer in the
                    // snapshot, and that predicate's fast path only exists inside an open window
                    // (FigureAncestryMemo: valid for ONE synchronous pass, because nothing can
                    // re-parent an actor while one is running). Classify SPANS FRAMES, so without
                    // this the climb would be cold for every renderer of every slice. This is the
                    // same bracket StepPrepare already opens around its own slice, and for the
                    // same reason; both windows close at the frame boundary, so neither is
                    // widened by a single frame and the round-7 re-parent case the "belt over the
                    // prefilter" re-checks exist for is untouched.
                    BeginFigureMemo();
                    try
                    {
                        while (_classifyCursor < _factCount)
                        {
                            int end = Mathf.Min(_classifyCursor + ClassifyChunk, _factCount);
                            ClassifySlice(_classifyCursor, end);
                            _classifyCursor = end;
                            if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                                break;
                        }
                    }
                    finally
                    {
                        EndFigureMemo();
                    }
                }
                if (_classifyCursor < _factCount)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
                // PERF S5: the census hands over to the SURVEY stage, which is what decides
                // whether this cycle needs a commit at all. Opening it is one list copy over
                // the wall cache; the walk itself is budgeted below.
                _rescanStage = RescanStage.Survey;
                BeginSurveyStage();
                if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget * 0.5f)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
            }

            if (_rescanStage == RescanStage.Survey)
            {
                _cycleSurveyFrames++;
                float surveyStart = (float)RescanClock.Elapsed.TotalMilliseconds;
                bool surveyDone;
                using (PerfMonitor.Scope("WallFade.Survey"))
                {
                    // A reveal cycle commits whatever the survey finds, and it already coincides
                    // with the game's own room-generation hitch — so the walk is allowed the same
                    // wider budget the census and the warm take there, rather than stretching a
                    // foregone conclusion over twenty frames. Mirrors PrepareUrgentBudgetMillis.
                    float surveyBudget = _rescanUrgent
                        ? PrepareUrgentBudgetMillis : SurveyBudgetMillis;
                    surveyDone = StepSurvey(frameStart, surveyBudget);
                }
                float surveyMs = (float)RescanClock.Elapsed.TotalMilliseconds - surveyStart;
                _cycleSurveyTotalMillis += surveyMs;
                if (surveyMs > _cycleWorstSurveyMillis)
                    _cycleWorstSurveyMillis = surveyMs;
                if (!surveyDone)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
                FinishSurveySignature();
                // THE DECISION. Every term inside is a comparison against what the table IN
                // FORCE was built from; a mismatch on any of them commits, and each one is
                // counted and named on the BUDGET line. See WallSegmentFade.Prepare.cs.
                if (CommitWouldChangeNothing(gen, now))
                {
                    _skipRun++;
                    if (_skipRun > _cycleWorstSkipRun)
                        _cycleWorstSkipRun = _skipRun;
                    _cycleSkipped++;
                    _rescanStage = RescanStage.Idle;
                    _rescanUrgent = false;
                    ClearSurveyState();
                    // A skipped cycle never opens the prepare stage, so the prewarm from the
                    // last committing one would otherwise hold ~3000 Transform references for
                    // as long as the skip run lasts — up to the staleness ceiling rather than
                    // the two seconds the subsystem's own comments assume. Dropped here, where
                    // BeginPrepareStage would have dropped it.
                    _propUnitRootPrewarm.Clear();
                    _prepAnchors.Clear();
                    _cycleCount++;
                    NoteCycleFrame(frameStart);
                    NoteTableAge(now);
                    LogRescanBudget(now);
                    return;
                }
                // PERF S4: only now is the expensive warm worth paying for.
                _rescanStage = RescanStage.Prepare;
                BeginPrepareStage(gen);
                if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget * 0.5f)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
            }

            if (_rescanStage == RescanStage.Prepare)
            {
                _cyclePrepareFrames++;
                float prepStart = (float)RescanClock.Elapsed.TotalMilliseconds;
                bool done;
                using (PerfMonitor.Scope("WallFade.Prepare"))
                {
                    float prepBudget = _rescanUrgent
                        ? PrepareUrgentBudgetMillis : PrepareBudgetMillis;
                    done = StepPrepare(frameStart, prepBudget);
                }
                float prepMs = (float)RescanClock.Elapsed.TotalMilliseconds - prepStart;
                _cyclePrepareTotalMillis += prepMs;
                if (prepMs > _cycleWorstPrepareMillis)
                    _cycleWorstPrepareMillis = prepMs;
                if (!done)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
                _rescanStage = RescanStage.Commit;
                // Only run the commit on the SAME frame when this frame barely cost anything —
                // otherwise the frame that finishes the warm would also carry the commit and we
                // would be back to one fat frame, just a smaller one.
                if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget * 0.5f)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
            }

            // PERF S4: the warm was allowed by a gate taken one or more frames ago. Re-ask it
            // here, on the frame that will consume it — see VerifyPrepareStillValid.
            VerifyPrepareStillValid(gen);

            // ('WallFade.Rescan' covers the COMMIT only since PERF S2 — the sweep and the
            // census report as 'WallFade.Sweep' and 'WallFade.Classify', so the three costs are
            // separable.)
            // PERF B step 3 — the CHURN gate, and it is DELIBERATELY OUTSIDE the scope below.
            //
            // The gate copies the committed table before and after the commit and diffs the pair
            // (WallSegmentFade.CommitGate.cs). It is read-only and cannot change a pixel. But
            // 'WallFade.Rescan' is the step whose ~95 ms IS the user's Ruckler and the number
            // this whole round is judged against, and _cycleWorstCommitMillis feeds the BUDGET
            // line's worst-commit figure. Taking the snapshots inside either one would fold the
            // instrument into the quantity it exists to protect, and would make the ModBuild 281
            // log incomparable with ModBuild 280's — an instrument that becomes part of its own
            // measurement, which is a defect class this project has a ledger entry for. Outside
            // both, the gate reports separately as 'WallFade.TableGate' and the commit's number
            // means in this build exactly what it meant in the last one.
            //
            // THE RETURN VALUE IS LOAD-BEARING: an AFTER snapshot with no BEFORE would compare a
            // full table against an empty one and print a spectacular, entirely fictional churn
            // figure.
            bool tableGate = BeginCommitTableGate();
            try
            {
                // PERF S1 kept its own scope here and the name is load-bearing: 'WallFade.Rescan'
                // is what the [Perf] STEPS line ranks and what the integrator greps.
                using (PerfMonitor.Scope("WallFade.Rescan"))
                {
                    float c0 = (float)RescanClock.Elapsed.TotalMilliseconds;
                    Rescan(gen);
                    float ms = (float)RescanClock.Elapsed.TotalMilliseconds - c0;
                    if (ms > _cycleWorstCommitMillis)
                        _cycleWorstCommitMillis = ms;
                }
            }
            finally
            {
                // In a finally so a commit that THROWS still closes the gate: leaving a BEFORE
                // snapshot standing would make the next commit's report a diff across two
                // commits, silently doubling every churn count it prints.
                if (tableGate)
                    EndCommitTableGate(Time.unscaledTime);
            }
            _rescanStage = RescanStage.Idle;
            _rescanUrgent = false;
            // PERF S5: the table in force is now the one these two signatures describe. They
            // are the ones the survey and the census took EARLIER in this cycle, which is the
            // fail-safe direction: if the world moved between the survey and this commit, the
            // next cycle's live survey disagrees with what is banked here and commits again.
            // The hash can therefore cause an unnecessary commit and never a wrong skip.
            AdoptCommittedSignature(now);
            // The warm belongs to the cycle that consumed it: holding the wall list and the
            // prewarm past this point would keep a scene's worth of transform references alive
            // across the two-second gap to the next cycle.
            ClearPrepareState();
            ClearSurveyState();
            _standingScopeOpen = false;
            _cycleCount++;
            NoteCycleFrame(frameStart);
            NoteTableAge(now);
            LogRescanBudget(now);
        }

        /// <summary>
        /// Drop the cycle in flight and every reference it holds. Called on scene load and
        /// teardown: a census taken over the OLD scene may never reach a commit, and the
        /// snapshot array must not keep a scene's worth of dead renderers alive. The next tick
        /// opens a fresh cycle with a fresh sweep (the signature check sees an empty snapshot).
        /// </summary>
        private void AbandonRescanCycle()
        {
            _rescanStage = RescanStage.Idle;
            _rescanUrgent = false;
            // PERF S4: a prepare stage in flight was warming against a scene we have stopped
            // watching, and its per-node fact window may be open on this frame. Close both —
            // an open window outside a synchronous pass is exactly the widened-memo defect the
            // stage's own comments refuse.
            ClearPrepareState();
            // PERF S5: a signature describes a scene we have stopped watching, and the drift
            // ring holds a table's worth of Segment references. Both go with the cycle — a
            // banked signature that outlived its scene would let the first cycle of the NEXT
            // one skip its commit, which is a table that never gets built at all.
            ClearSurveyState();
            _committedSigValid = false;
            // ModBuild 278: the culprit baseline describes a scene we have stopped watching, and
            // it holds a scene's worth of name references. It goes with the signature it
            // explains — a stale baseline would make the first refusal of the NEXT scenario
            // report every renderer in it as ENTERED, which is true and useless.
            _bankedFacts.Clear();
            _bankedFactsValid = false;
            _skipRun = 0;
            _driftRing.Clear();
            _driftCursor = 0;
            _lastCommitAt = float.NegativeInfinity;
            _propUnitRootPrewarm.Clear();
            _prepAnchors.Clear();
            EndNodeFactMemos();
            _standingScopeOpen = false;
            _classifyCursor = 0;
            _factCount = 0;
            _factWallFade.Clear();
            _factWater.Clear();
            _snapshot = System.Array.Empty<Renderer>();
            _facts = System.Array.Empty<RendererFact>();
            _snapshotSignature = -1;
            _snapshotTakenAt = float.NegativeInfinity;
        }

        private void NoteCycleFrame(float frameStart)
        {
            float ms = (float)RescanClock.Elapsed.TotalMilliseconds - frameStart;
            if (ms > _cycleWorstFrameMillis)
                _cycleWorstFrameMillis = ms;
        }

        /// <summary>
        /// Classify <c>[from, to)</c> of the snapshot into <see cref="_facts"/>. This is the
        /// ONLY place the four collection passes' per-renderer predicates are evaluated, and it
        /// evaluates each of them exactly once per renderer per cycle.
        ///
        /// <para>SAME VERDICTS, SAME CODE. Every flag below is filled by the very method the
        /// pass used to call inline — <see cref="IsModObject"/>,
        /// <see cref="RendererUsesWallFade"/>, <see cref="RendererUsesFoliage"/>,
        /// <see cref="IsWaterShader"/> + <see cref="IsWaterNameFamily"/>,
        /// <see cref="IsMountableRendererType"/> — over the same
        /// array in the same order. The two index lists are filled under the same conditions
        /// their pass's own loop used, so the passes see the same members in the same sequence
        /// (which matters: the first fade renderer of a group seeds that group's anchor).</para>
        ///
        /// <para>WARM SLICES re-read only what a live renderer can change without being
        /// recreated: liveness and the transform-derived bounds/anchor. Shader family, name and
        /// component type are fixed for a renderer's lifetime, so re-deriving them would be
        /// spending the exact interop cost this change exists to remove (the one exception,
        /// our own dissolve material swap, forces a cold cycle — see
        /// <c>_censusMaterialsDirty</c>). <c>enabled</c> is not cached at all: see
        /// <see cref="RendererFact.Mod"/> for why. The
        /// index lists are rebuilt on every slice, warm or cold, so a renderer that died is
        /// dropped from them immediately.</para>
        /// </summary>
        private void ClassifySlice(int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                Renderer? r = _snapshot[i];
                ref RendererFact f = ref _facts[i];
                if (r == null)
                {
                    f.R = null;
                    f.Mesh = null;
                    f.Figure = false;
                    // PERF S5: a hole in the snapshot is itself a fact — a renderer that died
                    // since the sweep changes what every pass below sees, so it must move the
                    // signature. A fixed per-hole term, accumulated commutatively like every
                    // other, so N holes read as N holes whatever order they appear in.
                    // ModBuild 279: into ALL THREE accumulators — a hole is a hole under every
                    // reading of the signature, and a term that moved one but not another would
                    // read as a narrowing effect when it is nothing of the kind.
                    FoldSceneFact(DeadRendererSigTerm);
                    FoldNarrowSceneFact(DeadRendererSigTerm);
                    FoldFigureSetFact(DeadRendererSigTerm);
                    continue;
                }
                bool cold = _classifyCold || !ReferenceEquals(f.R, r);
                if (cold)
                {
                    f.R = r;
                    f.Mesh = r as MeshRenderer;
                    f.Particles = r is ParticleSystemRenderer;
                    f.Mountable = IsMountableRendererType(r);
                    ClassifyMaterialsAndName(r, ref f);
                }
                f.Bounds = r.bounds;
                f.Anchor = f.Particles
                    ? r.transform.position
                    : new Vector3(f.Bounds.center.x, f.Bounds.min.y, f.Bounds.center.z);
                // ModBuild 279 (Option A). LIVE, on warm cycles too — see RendererFact.Figure.
                // The ancestry climb is memoised, and the window is opened per classify BATCH by
                // StepRescanCycle (FigureAncestryMemo's contract is one synchronous pass, and
                // classify spans frames — without that window the climb would be cold for every
                // renderer of every slice and the classify budget would blow).
                // IsWallGeneratedDressing is asked ONLY of a renderer the figure guard already
                // caught, which is the small minority, and its own ancestor walk is memoised by
                // the same window.
                f.Figure = IsFigureOrActorRenderer(r) && !IsWallGeneratedDressing(r);

                // AdoptShaderMatchedWalls' input: `any is MeshRenderer && RendererUsesWallFade`.
                // No enabled/mod filter — the old loop had none either.
                if (f.Mesh != null && f.WallFadeShader)
                    _factWallFade.Add(i);
                // CollectWaterFeatures' input. `enabled` is deliberately NOT part of the
                // membership test even though the pass's own guard has it: it is the one flag
                // the game flips at will, so a census verdict could go stale and drop a water
                // surface — and a dropped water surface means a fountain that fades, which the
                // 2026-08-09 ruling forbids ("lass den Brunnen niemals faden"). The pass reads
                // it LIVE instead; membership here is only the parts that cannot change.
                if (f.WaterSurface && !f.Mod)
                    _factWater.Add(i);

                // PERF S5 — THE SCENE HALF OF THE SKIP SIGNATURE, folded here because this loop
                // already visits every renderer in the snapshot exactly once per cycle and the
                // fold is two multiplies. It carries the renderer's IDENTITY plus every verdict
                // the four collection passes read off this table, so a renderer that appeared,
                // died or changed shader family moves the hash and the cycle commits.
                //
                // WHY `activeInHierarchy` AND NOT `renderer.enabled`, which is the flag the
                // stacked / mounted / prop-unit collection filters actually read. Because THIS
                // SUBSYSTEM WRITES `enabled` and does not write SetActive: hiding a wall's
                // siblings, shell pieces, foliage and mounted dressing is `r.enabled = false`
                // (grep the file set — there is no SetActive call anywhere in it). Folding
                // `enabled` would therefore make the signature move every time a wall fades or
                // unfades, i.e. exactly when the player is moving, and the skip would fire
                // almost never — a remedy gated behind its own side effect, which is a defect
                // class this project has already paid for. `activeInHierarchy` is the flag the
                // GAME flips (ProceduralMapTile.ShowContent — see the WALL-PATH AUDIT's own
                // note at the 'INACTIVE:' clause below), so it detects the game's changes and
                // is blind to ours by construction.
                //
                // WHAT THAT LEAVES UNCOVERED, said plainly: a GAME-side `renderer.enabled`
                // flip that is not accompanied by any other change. The staleness ceiling is
                // the backstop for it and the SKIP clause counts when the ceiling fires.
                //
                // What is deliberately NOT here is BOUNDS — those are already accepted as up to
                // one rescan interval stale by every consumer, and the two things that can move
                // them wholesale (a reveal, a board move) have gates of their own, with the
                // drift probe underneath for a piece that moved on its own.
                int bits = (f.Mesh != null ? 1 : 0)
                         | (f.Particles ? 2 : 0)
                         | (f.Mountable ? 4 : 0)
                         | (f.Mod ? 8 : 0)
                         | (f.WallFadeShader ? 16 : 0)
                         | (f.FoliageShader ? 32 : 0)
                         | (f.WaterSurface ? 64 : 0)
                         | (r.gameObject.activeInHierarchy ? 128 : 0);
                ulong ident = FoldSig(FnvOffset, r.GetInstanceID());
                FoldSceneFact(FoldSig(ident, bits));

                // ================================================================================
                // ModBuild 279, OPTION A — THE NARROWED SIGNATURE, COMPUTED IN SHADOW.
                // ================================================================================
                //
                // THE USER'S ESCALATION: "Ich will aber eigentlich gar keine spürbaren Ruckler -
                // nicht nur seltenere." This does NOT answer it — it makes the ~95 ms commits
                // rarer, which is the thing he said was not enough. It is here because it is
                // nearly free and because it reduces how often the sliced rebuild (a later
                // build) has to run at all.
                //
                // THE TERM. `f.Figure` is a renderer the round-7 ruling puts beyond every
                // adoption lane's reach. For such a renderer the narrowed signature drops the
                // ONE bit the game flips constantly — `activeInHierarchy`, which the ModBuild-277
                // log's decoded refusals show as the sole mover in 5 of 17 sampled refusals — and
                // sets a FIGURE bit in its place, so that a renderer CROSSING into or out of the
                // figure class still moves the hash. Identity is still folded, so a figure
                // appearing or dying still moves it too.
                //
                // WHAT IS DELIBERATELY *NOT* DROPPED, and this is a correction to the design
                // document, which proposed folding "identity plus the Figure bit only". The
                // seven COLD verdict bits stay. A figure-classified renderer with a water-family
                // shader still enters `_factWater` (CollectWaterFeatures has no figure guard at
                // all — WallSegmentFade.Water.cs) and its rect protects a fountain, so its
                // WaterSurface bit really can change the commit's output. Those bits move only
                // on a COLD classify, so keeping them costs nothing in refusals and removes a
                // whole class of "can a figure ever be X" arguments I would otherwise have to
                // win by assertion.
                //
                // AND THE HONEST LIMIT, carried here rather than in a report: the exemption is
                // NOT the "provably cannot change the commit's output" the design claims.
                // PurgeFigureRenderers is RESTITUTION, not prevention — it walks _mountedTouched
                // only, and it exempts IsWallGeneratedDressing (which is why that term is in
                // f.Figure). And WallSegmentFade.PropUnit.cs :1212 asks IsActuallyDrawing —
                // enabled AND activeInHierarchy — of every prop-unit MEMBER, and does so BEFORE
                // the figure refusal five lines below it, so a figure member's liveness decides
                // whether the whole unit is refused. That path fired zero times in the
                // ModBuild-261 session, but "zero observed" is not "cannot". This is why the
                // narrowing ships behind a dial with the full signature always on and a census
                // that names what the narrowing dropped.
                bool figure = f.Figure;
                FoldNarrowSceneFact(FoldSig(ident,
                    WallSegmentFadeCulprits.NarrowedBits(bits, figure)));
                // The FIGURE-SET half: identity plus the figure verdict alone. It exists for one
                // question and one only — when the narrowed signature moves and the full one does
                // not, was that a figure-verdict CROSSING (legitimate, and the narrowed term is
                // supposed to catch it) or an arithmetic impossibility (an instrument bug)? See
                // CommitWouldChangeNothing's scene term.
                FoldFigureSetFact(FoldSig(ident, figure ? 1 : 0));
            }
        }

        // ModBuild 279: the narrowed half's bit selection is WallSegmentFadeCulprits
        // .NarrowedBits — ONE definition, in the Unity-free file, so the wire suite can drive it
        // exhaustively over all 256 patterns on both arms before anybody trusts it.

        /// <summary>
        /// The four name/shader verdicts a scene renderer carries, derived from ONE
        /// <c>GetSharedMaterials</c> and ONE <c>r.name</c> read.
        ///
        /// <para>WHY THIS EXISTS AS ITS OWN METHOD. The four collection passes asked four
        /// separate questions of the same renderer — <see cref="RendererUsesWallFade"/>,
        /// <see cref="RendererUsesFoliage"/>, the water test (<see cref="IsWaterShader"/> +
        /// <see cref="IsWaterNameFamily"/>) and
        /// <see cref="IsModObject"/> — and each of those opened its own
        /// <c>GetSharedMaterials</c> and/or its own <c>r.name</c>. Both are INTEROP calls and
        /// <c>r.name</c> allocates a managed string every time. Over 8630 renderers that was
        /// ~26 000 material fetches and ~26 000 string allocations per rescan for four
        /// questions with a single, shared answer. The verdict logic below is those four
        /// methods' bodies, unchanged, sharing one fetch: the per-Shader memos are the same
        /// three dictionaries they already used (so a shader seen by any of the three questions
        /// answers instantly for the others), the mod test is the same layer-or-prefix pair,
        /// and the water name family is the same token list, consulted — as before — only when
        /// the shader family already said no.</para>
        /// </summary>
        private void ClassifyMaterialsAndName(Renderer r, ref RendererFact f)
        {
            bool wallFade = false, foliage = false, water = false;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            for (int mi = 0; mi < _matScratch.Count; mi++)
            {
                Material mat = _matScratch[mi];
                if (mat == null)
                    continue;
                Shader sh = mat.shader;
                if (sh == null)
                    continue;
                if (!wallFade)
                {
                    if (!_shaderVerdict.TryGetValue(sh, out bool capable))
                    {
                        capable = IsWallFadeShaderName(sh.name);
                        _shaderVerdict[sh] = capable;
                    }
                    wallFade = capable;
                }
                if (!foliage)
                {
                    if (!_shaderFoliageVerdict.TryGetValue(sh, out bool leafy))
                    {
                        leafy = IsFoliageShaderName(sh.name);
                        _shaderFoliageVerdict[sh] = leafy;
                    }
                    foliage = leafy;
                }
                if (!water)
                    water = IsWaterShader(sh);
            }
            // The wall-fade and foliage families are MeshRenderer questions (that is the type
            // both RendererUsesWallFade and RendererUsesFoliage take); the water family is not
            // — the water test takes a plain Renderer and the water pass ran it over the whole
            // sweep, so it stays that way.
            f.WallFadeShader = wallFade && f.Mesh != null;
            f.FoliageShader = foliage && f.Mesh != null;

            string n = r.name;
            f.Name = n; // ModBuild 278 — see RendererFact.Name; the string is already allocated
            // IsModObject, verbatim: the mod layer OR the repo-convention name prefix (hardware
            // round 3 — the MR sky backing 'GloomhavenVR.MrBacking' leaked into the near-miss
            // census through the layer-only test).
            f.Mod = r.gameObject.layer == VRLayers.ModLayer
                || n.StartsWith("GloomhavenVR.", StringComparison.Ordinal);
            // The authored water name family, consulted — as before — only when the shader
            // family already said no. See WallSegmentFade.Water.cs.
            f.WaterSurface = water || IsWaterNameFamily(n);
        }

        /// <summary>
        /// THE ATTRIBUTABLE LINE. One [WallSegmentFade] BUDGET line every
        /// <see cref="BudgetLogIntervalSeconds"/>, printed whatever it found — including
        /// nothing. It names how many objects were walked, WHERE they came from (a fresh sweep
        /// or a reused snapshot), how the frame budget was spent, and the worst single frame
        /// any stage of the pipeline cost in the window. That last number is the one the
        /// integrator reads against [Perf] STEPS' 'WallFade.Rescan' worst field.
        ///
        /// <para>PERF S3 (2026-08-23): the line also carries the COMMIT's per-phase breakdown,
        /// and that is deliberately part of the SAME line rather than a separate one a capture
        /// might not have switched on. It exists because the whole stall was once reported as
        /// ONE number for twenty-three phases — the instrument blind spot this project keeps
        /// paying for. See WallSegmentFade.CommitPhases.cs.</para>
        ///
        /// <para>THE HISTORY THIS LINE USED TO NARRATE — ModBuild 226's 118 ms frame, 228's
        /// 96.81 ms, 271's 123.78 ms, and the phase split each of them showed — is
        /// .planning/perf/WALL-FADE-CLOSEOUT.md §3 as of ModBuild 284. It was constant prose
        /// printed every five seconds; the numbers below are the only part of this line that
        /// measures anything.</para>
        /// </summary>
        private void LogRescanBudget(float now)
        {
            if (_budgetLoggedOnce && now < _nextBudgetLogTime)
                return;
            _budgetLoggedOnce = true;
            _nextBudgetLogTime = now + BudgetLogIntervalSeconds;
            var sb = new System.Text.StringBuilder(1024);
            sb.Append($"BUDGET: {_cycleCount} rescan cycle(s) completed since the last line — ")
              .Append($"{_factCount} scene renderer(s) classified per cycle, {_cycleSweeps} of them ")
              .Append("from a FRESH FindObjectsOfType<Renderer> sweep (worst ")
              .Append($"{_cycleWorstSweepMillis:F2}ms) and the rest from the reused snapshot ")
              .Append($"(structural signature unchanged, age cap {SnapshotMaxAgeSeconds:0.0}s); ")
              .Append($"census spread over {_cycleClassifyFrames} frame(s) at ")
              .Append($"{ClassifyBudgetMillis:0.0}ms/frame ({ClassifyUrgentBudgetMillis:0.0}ms on a ")
              .Append($"room-reveal edge); {_factWallFade.Count} fade-capable + {_factWater.Count} ")
              .Append($"water renderer(s) indexed; WORST COMMIT {_cycleWorstCommitMillis:F2}ms, ")
              .Append($"WORST SINGLE FRAME across all stages {_cycleWorstFrameMillis:F2}ms.");
            // PERF S4 — WHAT OWNED THE WORST COMMIT FRAME. A reader has to be able to tell
            // "sliced and now costs 1.5 ms/frame" from "sliced and one phase still costs 60 ms"
            // without another round, so the line names the worst phase with its number and says
            // how many phases are still atomic. It is 24 of 24 today and that is a DECISION, not
            // an omission — WallSegmentFade.Prepare.cs holds the argument.
            sb.Append($" COMMIT: the worst commit frame's most expensive phase was ")
              .Append($"{WorstCommitPhaseName} at {_cycleWorstCommitPhaseMillis:F2}ms; ")
              .Append($"{CommitPhaseCount} of {CommitPhaseCount} phase(s) still run ATOMICALLY; ")
              .Append($"the standing-prop scope was opened by the commit itself on ")
              .Append($"{_cycleScopeSelfOpened} cycle(s) (>0 means the prepare stage did not run).");
            AppendSkipClause(sb);
            AppendPrepareClause(sb);
            AppendCommitPhaseBreakdown(sb);
            // ModBuild 284 — THE HISTORY CLAUSE IS GONE FROM HERE AND LIVES IN
            // .planning/perf/WALL-FADE-CLOSEOUT.md §3, WHICH IS WHERE IT WAS ALWAYS BEING READ
            // FROM. Four constant appends (~310 characters) narrating the ModBuild 226 / 228 /
            // 271 numbers were printed every BudgetLogIntervalSeconds for the life of the
            // subsystem so that a reader could compare TODAY's numbers against them — but a
            // reader who has this line in front of him has a log, and the comparison baseline
            // belongs in the document he reads the log against. What stays here is only what
            // this window actually measured.
            VRLog.Info(Name, sb.ToString());
            _cycleCount = 0;
            _cycleSweeps = 0;
            _cycleClassifyFrames = 0;
            _cycleWorstFrameMillis = 0f;
            _cycleWorstCommitMillis = 0f;
            _cycleWorstSweepMillis = 0f;
            _cyclePrepareFrames = 0;
            _cycleWorstPrepareMillis = 0f;
            _cyclePrepareTotalMillis = 0f;
            _cyclePrepWarmedRenderers = 0;
            _cyclePrepWarmedRoots = 0;
            _cyclePrepRefusedReveal = 0;
            _cyclePrepRefusedBoard = 0;
            _cyclePrepDroppedAnchors = 0;
            _cycleScopeSelfOpened = 0;
            _cycleSurveyFrames = 0;
            _cycleWorstSurveyMillis = 0f;
            _cycleSurveyTotalMillis = 0f;
            _cycleSurveyRenderers = 0;
            _cycleSkipped = 0;
            _cycleCommitted = 0;
            _cycleWorstSkipRun = 0;
            _cycleWorstTableAgeSeconds = 0f;
            _cycleProbedSegments = 0;
            _noSkipNoTable = 0;
            _noSkipEarly = 0;
            _noSkipReveal = 0;
            _noSkipBoard = 0;
            _noSkipScene = 0;
            _noSkipWalls = 0;
            _noSkipCeiling = 0;
            _noSkipDrift = 0;
            _noSkipMaterials = 0;
            // ModBuild 279 (Option A): window counters like every other one beside them, so the
            // FIGURE EXEMPTION clause reports THIS window and not the session.
            _narrowWouldSkip = 0;
            _narrowOnlyFigureCrossing = 0;
            _narrowOnlyUnexplained = 0;
            // _lastNoSkipDetail is NOT reset: a window in which nothing refused must still say
            // what the last refusal was, or the clause reads as a dead instrument on exactly
            // the windows the change is working.
            _windowWorstCommitTotalMillis = 0f;
            _cycleWorstCommitPhase = -1;
            _cycleWorstCommitPhaseMillis = 0f;
        }

        private void Rescan(TilesOcclusionGenerator gen)
        {
            // PERF S1: one memo scope for the whole (synchronous) rescan — see
            // FigureAncestryMemo for why that cannot change a single figure verdict.
            BeginFigureMemo();
            try { RescanCore(gen); }
            finally { EndFigureMemo(); }
        }

        /// <summary>
        /// THE COMMIT. Every mutation of the segment table lives below this line, in the order
        /// the comments at each call site justify — and that order is load-bearing several
        /// times over (figures first so nothing can re-take an actor renderer; the water rects
        /// before every adoption pass; the mounted pass last because its rule needs the final
        /// AABBs). Nothing here may be reordered for cost.
        ///
        /// <para>PERF S3 (2026-08-23): the body that used to be one 300-line method is now
        /// twenty-three named phases, each in its own <see cref="CommitPhase"/> scope. The
        /// phases are EXACTLY the statements that were here — the five that were written inline
        /// (<see cref="CommitTileAnchors"/>, <see cref="CommitRoomRegistry"/>,
        /// <see cref="CommitDeadSegments"/>, <see cref="CommitWallCache"/>,
        /// <see cref="CommitDoorRoots"/>) were lifted into methods verbatim so they could carry
        /// a scope, and the other eighteen were already calls. No statement moved past another
        /// one. See WallSegmentFade.CommitPhases.cs for why the measurement had to come before
        /// the fix.</para>
        /// </summary>
        private void RescanCore(TilesOcclusionGenerator gen)
        {
            BeginCommitPhases();
            try
            {
                // Figures first (round 7): nothing below may keep or re-take an actor renderer.
                using (Phase(CommitPhase.Figures))
                    PurgeFigureRenderers();
                using (Phase(CommitPhase.TileAnchors))
                    CommitTileAnchors();
                using (Phase(CommitPhase.RoomRegistry))
                    CommitRoomRegistry(gen);
                using (Phase(CommitPhase.DeadSegments))
                    CommitDeadSegments();
                using (Phase(CommitPhase.WallCache))
                    CommitWallCache();
                using (Phase(CommitPhase.Doors))
                    CommitDoorRoots();
                // GATE COLUMNS (user ruling 2026-08-07): the wall EMBEDDING each doorway fades
                // like any wall — only the arch rect stays solid. Seeded before the adoption/
                // stacked passes so they see the gate's bounds and face domain.
                using (Phase(CommitPhase.GateSeed))
                    SeedGateColumns(_doorPropScratch);
                // Second discovery source: ADOPT every other fade-capable renderer in the scene.
                // The user report behind this ("fortgeschritteneres Szenario mit ganz anderen
                // Mauern — dort werden sie nicht mehr ausgeblendet"): advanced tilesets ship wall
                // meshes as map-tile geometry, not as ProceduralWall entities, so the wall cache
                // never listed them — yet their materials run the same WallFade shader family,
                // because that is how the FLAT game fades them. The shader is the game's own
                // definition of "this is a fadeable wall", so it is our discovery key too.
                // PERF S2: the scene sweep AND the per-renderer classification both happened HERE
                // until 2026-08-23 — one FindObjectsOfType<Renderer> plus four full walks of the
                // resulting 8630-entry array, 118 ms on one frame every two seconds. The sweep now
                // runs in BeginRescanCycle (rarely) and the classification in ClassifySlice (spread
                // over frames); this pass and the three below read the finished RendererFact table.
                // Nothing about WHICH renderers they see changed — see the PERF S2 note by the
                // table's declaration for the argument, predicate by predicate.
                using (Phase(CommitPhase.Adopt))
                    AdoptShaderMatchedWalls();
                // WATER FEATURES (user ruling 2026-08-09, brunnen.png — "lass den Brunnen niemals
                // faden"): rebuild the fountain/pond protection rects BEFORE the ground strip and
                // every adoption pass, so no pass can ever see a fountain's basin as fadeable and
                // leave its water plane hanging in mid-air. See WallSegmentFade.Water.cs.
                using (Phase(CommitPhase.Water))
                    CollectWaterFeatures();

                using (Phase(CommitPhase.Samples))
                    RebuildSamples();
                using (Phase(CommitPhase.Rooms))
                    AssociateRooms();
                using (Phase(CommitPhase.Ground))
                    StripGroundRenderers();
                using (Phase(CommitPhase.Engulf))
                    NeutralizeEngulfingSegments();
                // Second ground pass ON PURPOSE: NeutralizeEngulfingSegments creates fresh
                // per-renderer segments AFTER the first strip, so a ground-level renderer inside a
                // just-split group would otherwise be fade-eligible for one full rescan interval —
                // exactly the "floor vanishes at the wall's foot" class. The pass is idempotent and
                // the table is ~tens of segments, so running it twice is noise.
                using (Phase(CommitPhase.Ground2))
                    StripGroundRenderers();
                // Fort/keep superstructures (WallSegmentFade.Stacked.cs): AFTER ground strip +
                // engulf neutralization (needs the final base AABBs and room grids), BEFORE the
                // mounted pass (which must see the EXTENDED AABBs so torches hanging on the shell
                // attach to the same wall the shell rides).
                using (Phase(CommitPhase.Stacked))
                    CollectStackedShellPieces();
                // PROP UNIT COHESION (user report 2026-08-19, skelet.jpg — "Der Kopf des Skeletts wird
                // immer noch ausgeblendet"): the statue's skull sat in one wall unit's renderer list
                // and its body in another's, and the two walls fade independently, so the statue was
                // decapitated. Regroup every multi-part prop and give each unit ONE owner. HERE on
                // purpose: after every pass that can put a renderer into a segment (so all the claims
                // are in), and BEFORE the sibling and mounted passes, so both of those see the
                // corrected lists — including the mounted pass's ownership table, whose NEAR-MISS
                // census is what reported this defect. See WallSegmentFade.PropUnit.cs.
                using (Phase(CommitPhase.PropUnits))
                    EnforcePropUnitCohesion();
                using (Phase(CommitPhase.Siblings))
                    CollectAdoptedSiblings();
                // LAST on purpose: the mounted-dressing rule is geometric (airborne over the room
                // plane + hugging the wall slab), so it needs the FINAL segment table, their room
                // association and their ground-stripped (now shell-extended) AABBs.
                using (Phase(CommitPhase.Mounted))
                    CollectWallMountedProps();
                // MP sync (record 17): refresh every segment's cross-machine wire key — needs the
                // final table and the room labels (part of the key derivation).
                using (Phase(CommitPhase.WireKeys))
                    ComputeWireKeys();
                // Gate-lift links (round 12): bind embedding walls to their gate columns.
                using (Phase(CommitPhase.GateLift))
                    LinkGateLifts();
                // ROUND-14 BOUNDS GUARANTEE, deliberately LAST: no gate column may leave a rescan
                // without a decision AABB — a boundless segment is one the coverage decision cannot
                // reach, and an unreachable segment can hold its pieces hidden forever.
                using (Phase(CommitPhase.GateBounds))
                    EnsureGateBounds();
                // The standing-prop proof line, after every collection pass has run so its "claims
                // refused" count is the rescan's total (WallSegmentFade.Standing.cs).
                using (Phase(CommitPhase.StandCensus))
                    LogStandingPropCensus();
                // The prop-unit proof line, for the same reason and in the same place: emitted after
                // the pass has run, so every number in it is an outcome (WallSegmentFade.PropUnit.cs).
                using (Phase(CommitPhase.UnitCensus))
                    LogPropUnitCensus();
                // INSIDE THE MAP (user report 2026-08-24 — WallSegmentFade.Inside.cs): cache the
                // board's own volume for the per-frame O(1) "is the player standing IN the map"
                // test. Deliberately after GateBounds, which is itself deliberately last: this
                // phase reads the room registry AND every segment's FINAL decision AABB.
                using (Phase(CommitPhase.BoardVolume))
                    CommitBoardVolume();
            }
            finally
            {
                // In a finally so a throwing phase still leaves the window arithmetic consistent
                // — a diagnostic that lies after an exception is worse than none.
                EndCommitPhases();
            }
        }

        /// <summary>COMMIT PHASE 2 — see <see cref="RescanCore"/>. Lifted verbatim.</summary>
        private void CommitTileAnchors()
        {
            // Tile-plane anchors (round 7): each TilesOcclusionVolume knows its room's
            // renderers AND its CentralTile, whose transform sits ON the tile plane. The
            // renderer bounds are only trusted for the XZ footprint — their Y is the
            // occlusion-proxy artifact the round-6 hardware log caught (tops ~9 wu above
            // the actual floor, see class header).
            _floorYByRenderer.Clear();
            _roomMapByRenderer.Clear();
            _roomMapLabelByRenderer.Clear();
            // PERF S1: was a full-scene FindObjectsOfType<TilesOcclusionVolume> (~10-15 ms in
            // the big room — the call is O(every loaded object), not O(volumes)). The registry
            // returns the IDENTICAL set: every volume enrols in its own Start (the method that
            // also announces it to the occlusion generator), the store was seeded from a real
            // sweep at install, and Collect applies the same activeInHierarchy + hideFlags
            // filters FindObjectsOfType does. See Core/SceneRegistry.cs.
            SceneRegistry.Volumes.Collect(_volumeScratch);
            foreach (TilesOcclusionVolume v in _volumeScratch)
            {
                if (v == null || v.CentralTile == null || v.Renderers == null)
                    continue;
                float tileY = v.CentralTile.transform.position.y;
                // ROUND-4 ROOM IDENTITY (user ruling: "normale Raum-Logik" — the game's own
                // room is the unit): CentralTile.m_ClientTile.m_Tile.m_HexMap is the CMap
                // this volume's own IsVisible() reads .Revealed from — the game's unit of
                // reveal. Every volume of one revealed room carries the same CMap.
                object? mapKey = null;
                string mapLabel = "?";
                try
                {
                    ScenarioRuleLibrary.CMap? map = v.CentralTile.m_ClientTile?.m_Tile?.m_HexMap;
                    if (map != null)
                    {
                        mapKey = map;
                        mapLabel = string.IsNullOrEmpty(map.RoomName)
                            ? map.MapInstanceName : map.RoomName;
                    }
                }
                catch { /* client-tile chain mid-build — renderers stay singleton rooms */ }
                foreach (MeshRenderer vr in v.Renderers)
                {
                    if (vr == null)
                        continue;
                    _floorYByRenderer[vr] = tileY;
                    if (mapKey != null)
                    {
                        _roomMapByRenderer[vr] = mapKey;
                        _roomMapLabelByRenderer[vr] = mapLabel;
                    }
                }
            }
            // Same phase, same data family: the room's PLAYABLE hexes, for the sample grid.
            CollectPlayableTiles();
        }

        /// <summary>
        /// THE DENOMINATOR'S SOURCE (ModBuild 258). Collect every playable hex in the scenario,
        /// grouped by the game's own room object (<c>CMap</c>) — the identical key
        /// <see cref="CommitRoomRegistry"/> already groups logical rooms by, so a room and its
        /// hexes cannot disagree about which room they belong to.
        ///
        /// <para>WHY THIS EXISTS. Up to ModBuild 257 the floor grid was a 4×4 lattice over the
        /// room's axis-aligned BOUNDING BOX (RebuildSamples, unchanged since round 6), and
        /// nothing anywhere tested whether a lattice position landed on a tile at all. A
        /// Gloomhaven room is a HEX CLUSTER; the corners of a rectangle drawn round it contain
        /// no playable tile. The ModBuild 257 hardware log shows the consequence with no room
        /// for interpretation — all four green walls pinned at a DIFFERENT corner of that
        /// rectangle, each at the corner nearest itself: 'Wall 4' at cells #0,#1,#4,#8 (corner
        /// ix0/iz0), 'Wall 2' at #7,#11,#14,#15 (corner ix3/iz3), 'Wall 1' at #2,#3 (corner
        /// ix0/iz3), 'Wall 3' at #12 (corner ix3/iz0). The pieces taking those cells are props
        /// standing OFF the field ('FR_Pillar_Tree_Trunk_01/_02', 'Blocks'). Four cells of
        /// sixteen is 0.25, which sits between the 0.20 exit bar and the 0.35 enter bar, so
        /// 'Wall 2' and 'Wall 4' held whatever state they were in — 13 samples at
        /// <c>ema 0.25 blk 4/16 FADED</c> and 11 at <c>ema 0.25 blk 4/16 solid</c>, the SAME
        /// reading in both states. 'Wall 1' pinned at TWO cells (0.125 &lt; 0.20) and released;
        /// that two-versus-four was the whole difference between the wall the user says works
        /// and the two he reports stuck.
        ///
        /// <para>WHY THE GAME'S REGISTRY AND NOT A SWEEP. <c>ObjectCacheService</c> maintains
        /// this set itself (<c>TileBehaviour.OnEnable/OnDisable</c>), so reading it is a walk
        /// over a few hundred already-collected components — no <c>FindObjectsOfType</c>, and
        /// the set is exactly the hexes that are live right now.</para>
        ///
        /// <para>WHY THE LIVE TRANSFORM AND NOT <c>CMapTile.Position</c>. Both are the same
        /// number at scenario init (<c>UnityGameEditorRuntime.InitialiseScenario</c> matches
        /// them with a 0.1 wu tolerance), but <c>CMapTile.Position</c> is the AUTHORED world
        /// position and would silently disagree with <see cref="CommittedTable.RoomBounds"/> — which comes
        /// from live <c>renderer.bounds</c> — the moment anything reparents or rescales the
        /// board. The transform cannot drift from the renderer bounds because it is the same
        /// frame.</para>
        ///
        /// <para>ModBuild 259 — "PLAYABLE" NOW MEANS PLAYABLE. ModBuild 258 left
        /// <c>CMapTile.Flags</c> unread and its own report said so: at that point neither
        /// flag's correct setting could be read off a log, and this subsystem had already
        /// shipped two discriminators (a 2.5 wu height cap, a 2.0 h/w aspect bar) that the very
        /// next hardware log falsified. The user has since supplied the missing input in words
        /// — <i>"Mir ist aufgefallen dass dieses Wand eigene nicht-spielbare tiles hat … Nur
        /// spielbare tiles sollen berücksichtigt werden"</i>, and earlier, on 2026-08-24,
        /// <i>"dahinter sind bisher nicht entdeckte tiles, werden sie auch bereits als Raum
        /// gezählt? … das soll aber nicht der Fall sein"</i>. The predicate is therefore NOT
        /// invented here: it is the persistent half of the game's own passability test,
        /// <c>CNode.NavTo</c>. See <see cref="ClassifyHex"/> for every term and its writer.</para>
        /// </summary>
        private void CollectPlayableTiles()
        {
            _tilesByMap.Clear();
            _tileWhyByMap.Clear();
            _tileListsUsed = 0;
            _tilesResolved = 0;
            _tilesUnkeyed = 0;
            _tileSourceLive = false;
            if (!Singleton<Script.Controller.ObjectCacheService>.IsInitialized)
                return;
            HashSet<TileBehaviour>? tiles;
            try
            {
                tiles = Singleton<Script.Controller.ObjectCacheService>.Instance.GetTileBehaviors();
            }
            catch
            {
                return; // service mid-teardown — every room falls back to the box, as before
            }
            if (tiles == null)
                return;
            _tileSourceLive = true;
            foreach (TileBehaviour tb in tiles)
            {
                if (tb == null)
                    continue;
                object? key;
                try
                {
                    key = tb.m_ClientTile?.m_Tile?.m_HexMap;
                }
                catch
                {
                    key = null; // client-tile chain mid-build, exactly as CommitTileAnchors treats it
                }
                if (key == null)
                {
                    _tilesUnkeyed++;
                    continue;
                }
                List<byte> why;
                if (!_tilesByMap.TryGetValue(key, out List<Vector3> list))
                {
                    list = RentTileList(out why);
                    _tilesByMap[key] = list;
                    _tileWhyByMap[key] = why;
                }
                else
                {
                    why = _tileWhyByMap[key];
                }
                list.Add(tb.transform.position);
                why.Add(ClassifyHex(tb)); // index-aligned with `list` by construction
                _tilesResolved++;
            }
        }

        // ── PLAYABILITY REASON CODES (ModBuild 259) ──────────────────────────────────────────
        // One code per hex, so the census can name the TERM that removed it. Every code below
        // is a term of the game's own passability test; nothing here is a threshold.
        private const byte HexPlayable = 0;     // in the denominator
        private const byte HexEdgeFlag = 1;     // EFlags.Edge  — the room's own border hexes
        private const byte HexBlockedFlag = 2;  // EFlags.Blocked — authored as unplayable
        private const byte HexNodeBlocked = 3;  // CNode.Blocked — an obstacle/entrance prop stands here
        private const byte HexNotWalkable = 4;  // CNode.Walkable false with clean flags (disagreement)
        private const byte HexUnreadable = 5;   // no verdict available — KEPT, fail-open

        /// <summary>
        /// IS THIS HEX FLOOR A FIGURE COULD OCCUPY? The predicate is the game's own, not one
        /// invented here. <c>AStar.CNode.NavTo</c> (ScenarioRuleLibrary/AStar/CNode.cs) is what
        /// the game asks before letting a figure step onto a node:
        /// <c>toNode.Walkable &amp;&amp; !toNode.SuperBlocked &amp;&amp; ((!toNode.Blocked &amp;&amp;
        /// !toNode.TransientBlocked) || ignoreBlocked)</c>. This takes the two PERSISTENT terms
        /// and deliberately drops the two transient ones:
        ///
        /// <para><b>Walkable — TAKEN.</b> Written in exactly one place at scenario load,
        /// <c>ScenarioManager.Load</c>: <c>if (cTile != null &amp;&amp;
        /// !cTile.m_Hex.FlagsSet(EFlags.Blocked | EFlags.Edge)) { Walkable = true; }</c>. So
        /// <c>Walkable</c> IS the authored flag pair, and <c>FlagsSet</c> is
        /// <c>(Flags &amp; flags) != 0</c>, i.e. ANY of the two (EFlags: Blocked = 1, Edge = 2).
        /// The flags are read here FIRST — same data, one indirection less, and it lets the
        /// census separate Edge from Blocked, which is the distinction the user's report turns
        /// on. <c>EFlags.Edge</c> is set by <c>UnityGameEditorRuntime.InitialiseScenario</c> for
        /// every scene object of type <c>ObjectImportType.EdgeTile</c>, and those tiles are
        /// added to the ROOM'S OWN <c>CMap.MapTiles</c> and DO get a <c>TileBehaviour</c>
        /// (<c>ClientScenarioManager.Create</c> wires one for every CTile) — which is precisely
        /// "diese Wand hat eigene nicht-spielbare tiles": they were in ModBuild 258's
        /// denominator. <c>CObjectDoor.SetDoor</c> also sets <c>Walkable = true</c> on a door's
        /// hex, so a doorway hex stays in the denominator; doorway segments never fade anyway
        /// (user ruling 2026-08-02).</para>
        ///
        /// <para><b>Blocked — TAKEN.</b> Two writers, both plain local scene construction:
        /// <c>UnityGameEditorObject.Start</c> sets it for every <c>Coverage</c> marker under a
        /// scene prop and clears it again in <c>ReleasePathing</c>/<c>OnDestroy</c>, and
        /// <c>ApparanceLayer.Create</c> sets it on a dungeon entrance/exit door hex. A hex with
        /// an obstacle standing on it is floor no figure can occupy.</para>
        ///
        /// <para><b>SuperBlocked and TransientBlocked — REFUSED, and this is not caution but
        /// correctness.</b> Both are per-QUERY scratch: <c>CPathFinder.Lock()</c> stamps them
        /// from <c>QueuedTransient*BlockedLists</c> while a path is being solved and
        /// <c>CPathFinder.Unlock()</c> clears TransientBlocked over the whole grid again. They
        /// are written from ACTOR POSITIONS (<c>CActor</c>, <c>CEnemyActor</c>,
        /// <c>CPlayerActor</c> all queue them), so a 2 s rescan would sample a race, the value
        /// would differ between clients, and the meaning is wrong anyway: a hex a monster is
        /// standing on is still floor the player is looking at and still a reason to fade a
        /// wall in front of it.</para>
        ///
        /// <para><b>IsBridge / IsBridgeOpen — REFUSED.</b> Set only by <c>CObjectDoor</c> on a
        /// DOOR's hex. <c>NavTo</c> does not consult <c>IsBridgeOpen</c> at all — the
        /// bridge-open test lives in <c>CPathFinder.FindPath</c>'s path assembly, not in
        /// per-node passability — and a door hex flips its state during play, which would move
        /// the denominator mid-scenario for no visual reason.</para>
        ///
        /// <para><b>CMap.Revealed — REFUSED HERE, and the reason is structural.</b> Reveal is
        /// per-CMap, i.e. per ROOM, never per hex (<c>CMap.Revealed</c>, set true in
        /// <c>CMap.OpenRoom</c>). Applied to hexes it can only ever remove ALL of a room's hexes
        /// or none, which is the denominator collapse constraint 1 forbids. The user's
        /// "undiscovered tiles must not count" is already answered one level up and by
        /// construction: a room only enters <see cref="CommittedTable.RoomBounds"/> when the game appends its
        /// occlusion volume renderer, and a hex only enters a room's set when its CMap is THAT
        /// room's CMap and it lies inside that room's own footprint. The ModBuild 258 log is the
        /// evidence: 184 hexes were keyed to a room while the registry held exactly ONE room,
        /// so 140 hexes of not-yet-revealed rooms were already outside every denominator. The
        /// census prints the room's Revealed flag so the next log can falsify that claim rather
        /// than inherit it.</para>
        ///
        /// <para>MULTIPLAYER. Every term is derived from local scene state that each client
        /// builds identically from the same scenario state; no networked state is added, read
        /// or written, and nothing here writes game state.</para>
        /// </summary>
        private static byte ClassifyHex(TileBehaviour tb)
        {
            ScenarioRuleLibrary.CTile? tile;
            try
            {
                tile = tb.m_ClientTile?.m_Tile;
            }
            catch
            {
                return HexUnreadable; // client-tile chain mid-build, as everywhere else here
            }
            if (tile == null)
                return HexUnreadable;
            try
            {
                ScenarioRuleLibrary.CMapTile hex = tile.m_Hex;
                if (hex != null)
                {
                    if (hex.FlagsSet(ScenarioRuleLibrary.EFlags.Edge))
                        return HexEdgeFlag;
                    if (hex.FlagsSet(ScenarioRuleLibrary.EFlags.Blocked))
                        return HexBlockedFlag;
                }
            }
            catch
            {
                return HexUnreadable;
            }
            try
            {
                AStar.CPathFinder pathFinder = ScenarioRuleLibrary.ScenarioManager.PathFinder;
                if (pathFinder == null)
                    return HexUnreadable;
                AStar.CNode[,] nodes = pathFinder.Nodes;
                if (nodes == null)
                    return HexUnreadable;
                AStar.Point at = tile.m_ArrayIndex;
                if (at.X < 0 || at.Y < 0
                    || at.X >= nodes.GetLength(0) || at.Y >= nodes.GetLength(1))
                    return HexUnreadable;
                AStar.CNode node = nodes[at.X, at.Y];
                if (node == null)
                    return HexUnreadable;
                if (node.Blocked)
                    return HexNodeBlocked;
                if (!node.Walkable)
                    return HexNotWalkable;
            }
            catch
            {
                return HexUnreadable; // PathFinder torn down between scenarios — fail OPEN
            }
            return HexPlayable;
        }

        /// <summary>A cleared hex list from the rescan-scoped pool — the room count is tiny and
        /// stable, so after the first rescan this never allocates. The playability-reason list
        /// is rented in lockstep so the two stay index-aligned by construction.</summary>
        private List<Vector3> RentTileList(out List<byte> why)
        {
            if (_tileListsUsed < _tileListPool.Count && _tileListsUsed < _tileWhyPool.Count)
            {
                List<Vector3> reused = _tileListPool[_tileListsUsed];
                why = _tileWhyPool[_tileListsUsed];
                _tileListsUsed++;
                reused.Clear();
                why.Clear();
                return reused;
            }
            var fresh = new List<Vector3>();
            why = new List<byte>();
            _tileListPool.Add(fresh);
            _tileWhyPool.Add(why);
            _tileListsUsed++;
            return fresh;
        }

        /// <summary>COMMIT PHASE 3 — see <see cref="RescanCore"/>. Lifted verbatim.</summary>
        private void CommitRoomRegistry(TilesOcclusionGenerator gen)
        {
            // LOGICAL ROOM GROUPING (round 4): the keep ships ONE game room as SIX occlusion
            // sub-volumes ('Volume_1..6', all under map tile 'E', same CMap); treating each
            // volume renderer as its own room split the room's floor grid six ways, walls
            // were assigned to one sixth each, and the front wall's own-room coverage read
            // 0.00 although it hid the (whole) room. Registry entries therefore merge per
            // (CMap, quantized anchor height): union XZ bounds, ONE grid, one anchor state.
            // Renderers without a CMap (no volume / chain unbuilt) stay singleton rooms —
            // exactly the old behaviour, and unanchored ones stay fail-safe solid.
            _live.RoomBounds.Clear();
            _live.RoomFloorY.Clear();
            _live.RoomFloorAnchored.Clear();
            _live.RoomLabels.Clear();
            _roomRendererCounts.Clear();
            _roomMapKeys.Clear();
            _keyToRoomScratch.Clear();
            foreach (MeshRenderer r in gen.m_RoomRenderers)
            {
                if (r == null)
                    continue;
                bool anchored = _floorYByRenderer.TryGetValue(r, out float floorY);
                object? key = anchored && _roomMapByRenderer.TryGetValue(r, out object k)
                    ? k : null;
                if (key != null)
                {
                    // Same CMap on a DIFFERENT floor level (terraced rooms) must not share
                    // one sample plane — the anchor height is part of the key (0.5 wu bins).
                    (object, int) groupKey = (key, Mathf.RoundToInt(floorY * 2f));
                    if (_keyToRoomScratch.TryGetValue(groupKey, out int idx))
                    {
                        Bounds merged = _live.RoomBounds[idx];
                        merged.Encapsulate(r.bounds);
                        _live.RoomBounds[idx] = merged;
                        _roomRendererCounts[idx]++;
                        continue;
                    }
                    _keyToRoomScratch[groupKey] = _live.RoomBounds.Count;
                }
                _live.RoomBounds.Add(r.bounds);
                _live.RoomFloorY.Add(anchored ? floorY : float.NaN);
                _live.RoomFloorAnchored.Add(anchored);
                _live.RoomLabels.Add(key != null && _roomMapLabelByRenderer.TryGetValue(r, out string lbl)
                    ? lbl : r.name);
                _roomRendererCounts.Add(1);
                // ModBuild 258: the room's own CMap, kept per LOGICAL room so RebuildSamples can
                // ask CollectPlayableTiles for THIS room's hexes. Null for a renderer with no
                // volume/CMap — that room keeps the bounding-box grid, fail-safe and unchanged.
                _roomMapKeys.Add(key);
            }
            _live.BuiltRoomCount = gen.m_RoomRenderers.Count;
            // PERF S4, GATE 3's reference reading — taken HERE because this is the phase that
            // builds _live.RoomFloorY, so the probe and the planes are from the same instant. See
            // BoardStillWhereTheFloorPlanesSayItIs.
            _live.PrepBoardProbeValid = false;
            foreach (MeshRenderer probe in gen.m_RoomRenderers)
            {
                if (probe == null)
                    continue;
                _live.PrepBoardProbePos = probe.transform.position;
                _live.PrepBoardProbeValid = true;
                break;
            }

            // Fallback for rooms without a volume match: median anchored height (rooms of
            // one scenario share the board plane), else the old bounds top — with the
            // !ABOVE-WALL/!UNANCHORED diag tripwires flagging that degraded mode.
            _floorYScratch.Clear();
            for (int i = 0; i < _live.RoomFloorY.Count; i++)
            {
                if (_live.RoomFloorAnchored[i])
                    _floorYScratch.Add(_live.RoomFloorY[i]);
            }
            _live.RoomsAnchored = _floorYScratch.Count;
            float fallbackY = float.NaN;
            if (_floorYScratch.Count > 0)
            {
                _floorYScratch.Sort();
                fallbackY = _floorYScratch[_floorYScratch.Count / 2];
            }
            for (int i = 0; i < _live.RoomFloorY.Count; i++)
            {
                if (_live.RoomFloorAnchored[i])
                    continue;
                _live.RoomFloorY[i] = float.IsNaN(fallbackY) ? _live.RoomBounds[i].max.y : fallbackY;
            }

            // ROOM-REVEAL DIAGNOSTIC (mid-scenario door open → Choreographer
            // .RevealRoomCreateCharacterActors → TilesOcclusionGenerator.UpdateAwaitingVolumes
            // appends the new room's renderers; our Tick sees the count change and rescans
            // immediately): one unmissable line whenever the room registry or its anchor count
            // changes, plus a fresh heartbeat, so the next hardware log PROVES the registry
            // re-anchored on reveal instead of leaving it to inference.
            if (_live.RoomBounds.Count != _lastRoomCensusCount
                || _live.RoomsAnchored != _lastRoomCensusAnchored)
            {
                bool reveal = _lastRoomCensusCount >= 0 && _live.RoomBounds.Count > _lastRoomCensusCount;
                // Grouping census: which logical rooms exist and how many volume renderers
                // each merged ('E'×6 = the keep's six sub-volumes as ONE room — round 4).
                var groups = new System.Text.StringBuilder();
                for (int i = 0; i < _live.RoomLabels.Count && i < 8; i++)
                {
                    if (groups.Length > 0)
                        groups.Append(", ");
                    groups.Append('\'').Append(_live.RoomLabels[i]).Append('\'');
                    if (i < _roomRendererCounts.Count && _roomRendererCounts[i] > 1)
                        groups.Append('×').Append(_roomRendererCounts[i]);
                }
                if (_live.RoomLabels.Count > 8)
                    groups.Append(", …");
                VRLog.Info(Name,
                    $"room registry {(reveal ? "REVEAL re-anchor" : "refresh")}: "
                    + $"{Mathf.Max(_lastRoomCensusCount, 0)}→{_live.RoomBounds.Count} LOGICAL room(s) "
                    + $"from {_live.BuiltRoomCount} volume renderer(s), grouped by the game's room "
                    + $"identity (CMap via CentralTile.m_ClientTile.m_Tile.m_HexMap — round 4): "
                    + $"[{groups}]; "
                    + $"{_live.RoomsAnchored}/{_live.RoomBounds.Count} tile-anchored — walls of unanchored "
                    + "rooms are held SOLID (fail-safe) until their volume anchors.");
                _lastRoomCensusCount = _live.RoomBounds.Count;
                _lastRoomCensusAnchored = _live.RoomsAnchored;
                _heartbeatLogged = false; // re-print the full table against the new room set
            }

            // Board moved/tilted or a room got revealed → the perspective onto the play area
            // changed; flag it so UpdatePerspectiveState re-arms aggressive re-evaluation.
            if (_live.RoomBounds.Count > 0)
            {
                Vector3 combined = Vector3.zero;
                foreach (Bounds b in _live.RoomBounds)
                    combined += b.center;
                combined /= _live.RoomBounds.Count;
                if (_roomCenterInit && (combined - _roomCenter).sqrMagnitude > 0.0001f)
                    _roomBoundsMoved = true;
                _roomCenter = combined;
                _roomCenterInit = true;
            }
        }

        /// <summary>COMMIT PHASE 4 — see <see cref="RescanCore"/>. Lifted verbatim.</summary>
        private void CommitDeadSegments()
        {
            // Drop segments whose anchor died (their renderers died with them). Attachments may
            // OUTLIVE the anchor (a split piece's asset siblings live in a different subtree),
            // so restore them first — a hidden door whose owner segment vanished would otherwise
            // stay invisible forever (the foliage-orphan lesson, applied to every attachment).
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                if (kv.Key == null)
                {
                    RestoreSegmentFoliage(kv.Value);
                    RestoreSegmentSiblings(kv.Value);
                    RestoreSegmentMounted(kv.Value);
                    RestoreSegmentStacked(kv.Value);
                    RestoreSegmentBody(kv.Value);
                    _deadKeys.Add(kv.Key!); // destroyed Unity object — reference still hashes
                }
            }
            foreach (Component dead in _deadKeys)
                _live.Segments.Remove(dead);
        }

        /// <summary>COMMIT PHASE 5 — see <see cref="RescanCore"/>. Lifted verbatim.</summary>
        private void CommitWallCache()
        {
            // STANDING PROPS (user report 2026-08-15, skelet.jpg): a fresh verdict scope for
            // this rescan, opened before the first renderer is collected into any segment so no
            // path can claim a standing prop even once. See WallSegmentFade.Standing.cs.
            //
            // PERF S4: the scope now opens one stage EARLIER, in BeginPrepareStage, because the
            // memos it clears are the ones that stage fills — and it reads exactly the same
            // table there (nothing mutates _live.Segments between the two points). This is the
            // BACKSTOP, not the normal path: a commit reached with no scope open would run the
            // standing rule against the PREVIOUS rescan's verdict memos. It is counted and
            // printed rather than silently corrected, because a backstop that fires every cycle
            // means the stage never ran.
            if (!_standingScopeOpen)
            {
                BeginStandingMemoScope();
                _cycleScopeSelfOpened++;
            }
            BeginStandingCensusScope();
            // The per-node subtree facts PropUnitRootOf reads are opened HERE and only here, so
            // their window is still exactly ONE commit — the constancy argument in
            // _nodeRendererCount is about a synchronous pass and may not be widened by a frame.
            // The prepare stage opens and closes its own window per frame for the same reason.
            ClearNodeFactMemos();

            // Adopt new walls / refresh renderer lists, shader-variant info and bounds.
            _claimedRenderers.Clear();
            _censusWallsWithoutFade = 0;
            _unfadeableWallShaders.Clear();
            List<ProceduralWall> cache = ProceduralWall.m_WallCache;
            for (int i = 0; i < cache.Count; i++)
            {
                ProceduralWall wall = cache[i];
                if (wall == null)
                    continue;
                if (_live.SplitAnchors.Contains(wall))
                {
                    RefreshSplitWall(wall);
                    continue;
                }
                if (!_live.Segments.TryGetValue(wall, out Segment? seg))
                {
                    seg = new Segment { Anchor = wall, FromWallCache = true };
                    _live.Segments.Add(wall, seg);
                }
                RefreshSegment(seg);
                foreach (MeshRenderer r in seg.Renderers)
                    _claimedRenderers.Add(r);
            }
        }

        /// <summary>COMMIT PHASE 6 — see <see cref="RescanCore"/>. Lifted verbatim.</summary>
        private void CommitDoorRoots()
        {
            // Doorway registry (recognition only — doorways never fade, user ruling
            // 2026-08-02): the live door props, refreshed before the adoption sweep so
            // FindDoorwayRoot can re-anchor frame/pillar renderers per door.
            _doorRoots.Clear();
            // PERF S1: registry read instead of the second full-scene FindObjectsOfType —
            // identical set, see the volume comment above and Core/SceneRegistry.cs.
            SceneRegistry.DoorProps.Collect(_doorPropScratch);
            foreach (UnityGameEditorDoorProp dp in _doorPropScratch)
            {
                if (dp != null)
                    _doorRoots.Add(dp.transform);
            }
        }

        /// <summary>A renderer whose AABB TOP reaches no higher than this above its room's floor
        /// plane is GROUND (or base course), never a wall — see <see cref="StripGroundRenderers"/>.
        /// 1 wu ≈ half a hex; the flat game's own foundation band keeps roughly this zone solid.</summary>
        private const float GroundExclusionHeightWU = 1.0f;

        /// <summary>
        /// THE JUNGLE GROUND FIX (second round — the split alone did not do it): this tileset's
        /// ProceduralWall entities carry ~24 fade-capable renderers EACH (734 under 31 walls in
        /// the hardware log), and among them are the room-edge GROUND hexes the wall grows from.
        /// Fading the wall MPB'd those too, and because the jungle meshes reach down the diorama
        /// skirt, the shader's foundation band sits below the map and the held-state discard ate
        /// the floor — near-camera-only (0.02·dist term), hence "hole in VR, floor on the flat
        /// screen". A renderer LYING AT the floor plane cannot possibly hide that floor from a
        /// head above, so it has no business being part of a fade: strip every renderer whose
        /// AABB top is within <see cref="GroundExclusionHeightWU"/> of its room's floor plane
        /// from the segment (clearing our block off it if one is applied), recompute the
        /// segment's AABB from what remains, and drop segments with nothing left.
        /// </summary>
        private void StripGroundRenderers()
        {
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                Segment seg = kv.Value;
                if (!seg.HasBounds || seg.RoomIndex < 0 || seg.RoomIndex >= _live.RoomFloorY.Count)
                    continue;
                float ceiling = _live.RoomFloorY[seg.RoomIndex] + GroundExclusionHeightWU;
                bool changed = false;
                for (int i = seg.Renderers.Count - 1; i >= 0; i--)
                {
                    MeshRenderer r = seg.Renderers[i];
                    // WATER FEATURE (user ruling 2026-08-09, brunnen.png): a fountain's basin
                    // stands just ABOVE the ground band — that is precisely why the ground
                    // strip missed it and it faded with 'Wall 1' while its water plane, which
                    // has no fade channel at all, stayed hanging in mid-air. Water-protected
                    // pieces leave the segment on the same path as the ground band, so they
                    // are held solid with the same machinery (block cleared, AABB rebuilt).
                    if (r == null || (r.bounds.max.y > ceiling && !IsWaterProtected(r.bounds)))
                        continue;
                    if (seg.HasBlock)
                        r.SetPropertyBlock(null); // it was mid-fade — return it to solid NOW
                    seg.Renderers.RemoveAt(i);
                    changed = true;
                }
                // Ground-level foliage (grass tufts ON the floor) stays visible always — only
                // wall-dressing foliage rides the fade. Restore anything already touched.
                for (int i = seg.Foliage.Count - 1; i >= 0; i--)
                {
                    MeshRenderer f = seg.Foliage[i];
                    if (f == null || (f.bounds.max.y > ceiling && !IsWaterProtected(f.bounds)))
                        continue;
                    if (seg.FoliageState != 0)
                        RestoreFoliageRenderer(f);
                    seg.Foliage.RemoveAt(i);
                }
                // BODY ground rule (round 8 — REVERT of the round-7 spanning-course rule,
                // which froze this keep solid: ALL 31 masonry courses span from the ground
                // band upward, so "spanning stays solid" classified the entire visible wall
                // as foundation and only the top assets flickered). Only courses ENTIRELY
                // inside the band stay solid; spanning courses hide with the wall —
                // visibility beats foundation on this enabled-only fallback, and foundation
                // preservation is the NATIVE path's job now (toggle-capable masonry fades
                // through its own shader incl. the world-Y gradient).
                for (int i = seg.Body.Count - 1; i >= 0; i--)
                {
                    Renderer br = seg.Body[i].Renderer;
                    if (br == null || (br.bounds.max.y > ceiling && !IsWaterProtected(br.bounds)))
                        continue;
                    if (seg.BodyState != 0)
                        RestoreProp(seg.Body[i]);
                    seg.Body.RemoveAt(i);
                    changed = true;
                }
                if (!changed)
                    continue;
                if (seg.Renderers.Count == 0 && seg.Body.Count == 0 && !seg.IsGateColumn)
                {
                    // (gate columns legitimately survive empty — round-13 lifecycle rule)
                    // Segment leaves the table — free ALL its attachments (bushes, doors, dressing).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreSegmentMounted(seg);
                    RestoreSegmentStacked(seg);
                    RestoreSegmentBody(seg);
                    _deadKeys.Add(kv.Key);
                    continue;
                }
                // ROUND 14: a GATE COLUMN's decision AABB is its ARCH SEED (plus the stacked /
                // pillar extensions), never a renderer union — it legitimately owns zero wall
                // renderers, so rebuilding its bounds here would leave it BOUNDLESS, i.e.
                // unevaluated, i.e. latched in whatever fade state it held (the "masonry above
                // the arch never comes back" report). Ground-stripping its adopted pillars is
                // still correct; its bounds are not this pass's to rebuild.
                if (seg.IsGateColumn)
                    continue;
                // Recompute the AABB from the surviving (actual wall) renderers + body.
                seg.HasBounds = false;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                        continue;
                    if (!seg.HasBounds)
                    {
                        seg.Bounds = r.bounds;
                        seg.HasBounds = true;
                    }
                    else
                    {
                        seg.Bounds.Encapsulate(r.bounds);
                    }
                }
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer == null)
                        continue;
                    if (!seg.HasBounds)
                    {
                        seg.Bounds = p.Renderer.bounds;
                        seg.HasBounds = true;
                    }
                    else
                    {
                        seg.Bounds.Encapsulate(p.Renderer.bounds);
                    }
                }
                if (seg.HasBounds)
                {
                    float thickness = Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z);
                    seg.BlockEps = Mathf.Clamp(0.5f * thickness, BlockEpsMinWorld, BlockEpsMaxWorld);
                }
            }
            foreach (Component dead in _deadKeys)
                _live.Segments.Remove(dead);
        }

        /// <summary>
        /// THE JUNGLE-TILESET GROUND BUG (post-association pass, both discovery sources): a wall
        /// whose geometry RINGS its room has an AABB that CONTAINS the room's own floor samples,
        /// so BlockedFraction reads ~100% from every head position — permanent fade — and because
        /// those meshes reach down the diorama skirt, the shader's foundation band sits below the
        /// map and the held-state discard eats the GROUND at the wall's foot. Near-camera-only
        /// (the HIGH variant's 0.02·dist term), which is why the flat mirror still showed a floor
        /// while VR showed holes.
        ///
        /// The trigger is CONTAINMENT, deliberately not AABB fatness: an L-shaped stone corner
        /// run also has a fat AABB but contains only its corner quadrant (≈25% of the grid) —
        /// those keep today's whole-wall behaviour. A segment whose AABB XZ-contains ≥
        /// <see cref="EngulfSampleFraction"/> of its own room's samples cannot make a meaningful
        /// occlusion decision as ONE unit: multi-renderer segments are SPLIT per renderer (each
        /// piece is a proper slab deciding for itself; membership persists via _live.SplitAnchors),
        /// and an unsplittable single-renderer segment is held SOLID (vanilla look — strictly
        /// better than permanently missing ground) and flagged !ENGULF in the diag.
        /// </summary>
        private const float EngulfSampleFraction = 0.4f;

        private void NeutralizeEngulfingSegments()
        {
            _fatScratch.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                Segment seg = kv.Value;
                if (!seg.HasBounds || seg.RoomIndex < 0)
                    continue;
                if (seg.DoorRoot != null)
                    continue; // doorway: held permanently solid anyway — and a split would
                              // strip the DoorRoot off the pieces, making the archway fadeable
                if (seg.IsGateColumn)
                    continue; // gate columns are lifecycle-protected (round 13) — an engulf
                              // split would destroy the segment the arch/lift depend on
                if (Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z) <= GroupSlabMaxHorizontal)
                    continue; // thin slab — cannot contain a room
                if (InsideOwnRoomFraction(seg) < EngulfSampleFraction)
                    continue; // fat but bordering (L-corner) — legitimate whole-wall behaviour
                if (seg.Renderers.Count <= 1)
                {
                    // Unsplittable (single mesh — including an already-split piece that is
                    // itself room-sized): undecidable as one unit — hold solid.
                    seg.Engulfing = true;
                    continue;
                }
                _fatScratch.Add(kv);
            }

            foreach (KeyValuePair<Component, Segment> engulfing in _fatScratch)
            {
                Segment group = engulfing.Value;
                _live.SplitAnchors.Add(engulfing.Key);
                _live.Segments.Remove(engulfing.Key);
                RestoreSegmentFoliage(group); // pieces re-adopt the bushes on the next rescan
                RestoreSegmentSiblings(group); // ditto for asset siblings (doors/trim)
                RestoreSegmentMounted(group);  // …and for the wall-mounted dressing (torches)
                RestoreSegmentStacked(group);  // …and for stacked shell pieces (keep stories)
                RestoreSegmentBody(group);     // …and for plain wall-body meshes (masonry)
                if (group.HasBlock)
                {
                    foreach (MeshRenderer r in group.Renderers)
                    {
                        if (r != null)
                            r.SetPropertyBlock(null);
                    }
                }
                foreach (MeshRenderer r in group.Renderers)
                {
                    if (r == null)
                        continue;
                    if (!_live.Segments.TryGetValue(r, out Segment? sub))
                    {
                        sub = new Segment { Anchor = r, FromWallCache = group.FromWallCache };
                        _live.Segments.Add(r, sub);
                    }
                    // ModBuild 259: the piece remembers the GROUP it was carved out of, so the
                    // run can decide once for all of them (Segment.RunOwner).
                    sub.RunOwner = engulfing.Key;
                    sub.FromSplitRun = true;
                    BeginRefresh(sub);
                    bool relaxG = WallFadeTuning.AdoptGroundScenery;
                    if (CollectWallFadeInfo(r, sub, relaxG))
                    {
                        sub.Renderers.Add(r);
                        sub.Bounds = r.bounds;
                        sub.HasBounds = true;
                        sub.RunPassenger = relaxG && IsStandingFigureProp(r);
                    }
                    else
                    {
                        sub.GeometryRefusedWhy = relaxG
                            ? SplitPieceFigureRefusalReason : SplitPieceRefusalReason;
                    }
                    FinishRefresh(sub);
                    if (group.FromWallCache)
                        _claimedRenderers.Add(r);
                }
            }

            // The freshly split pieces need a room before the next decision tick.
            if (_fatScratch.Count > 0)
                AssociateRooms();
        }

        /// <summary>Fraction of the segment's OWN room's floor samples that lie inside the
        /// segment AABB's XZ footprint (Y ignored — wall AABBs span the whole column).</summary>
        private float InsideOwnRoomFraction(Segment seg) =>
            InsideRoomFraction(seg.Bounds, seg.RoomIndex);

        /// <summary>Same containment test for an arbitrary AABB — the stacked-shell pass
        /// pre-checks a WOULD-BE extended wall AABB against the room grid before adopting a
        /// piece (a shell ringing the room must never join, or coverage reads 100% forever).</summary>
        private float InsideRoomFraction(Bounds b, int room)
        {
            if (room < 0 || room >= _live.RoomSampleCount.Count)
                return 0f;
            int total = _live.RoomSampleCount[room];
            if (total <= 0)
                return 0f;
            int start = _live.RoomSampleStart[room];
            int end = Mathf.Min(start + total, _live.AllSamples.Count);
            int inside = 0;
            for (int i = start; i < end; i++)
            {
                Vector3 s = _live.AllSamples[i];
                if (s.x >= b.min.x && s.x <= b.max.x && s.z >= b.min.z && s.z <= b.max.z)
                    inside++;
            }
            return inside / (float)total;
        }

        /// <summary>
        /// Sweep all live MeshRenderers for wall-fade-capable materials that no wall-cache
        /// segment claimed, and group them into segments: by the nearest
        /// <see cref="ProceduralTileObserver"/> ancestor (the generation unit of tile-borne wall
        /// geometry — room-chunk granularity, same scale as a ProceduralWall run), else by the
        /// renderer's parent. Runs inside the 2s rescan; the shader verdict is cached per Shader
        /// so the steady-state cost is one dictionary probe per renderer.
        /// </summary>
        private void AdoptShaderMatchedWalls()
        {
            // Reset adopted segments for re-fill; keep their smoothing/fade state (keyed by
            // anchor, so a stable group keeps its EMA and dwell across rescans).
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                // Gate columns are seeded by SeedGateColumns (already refreshed this rescan)
                // and own no wall renderers — the shader sweep must not reset them.
                if (!kv.Value.FromWallCache && !kv.Value.IsGateColumn)
                    BeginRefresh(kv.Value);
            }

            _censusFadeRenderers = 0;
            _censusAdopted = 0;
            // PERF S2: `_factWallFade` holds exactly the indices the old loop's guard
            // (`any is MeshRenderer && RendererUsesWallFade(it)`) let through, in snapshot
            // order — which is load-bearing here, because the FIRST fade renderer of a group
            // is the one that seeds that group's anchor and bounds. Renderers destroyed since
            // the census are dropped by the null check below, exactly as the old `r == null`
            // arm did.
            for (int fi = 0; fi < _factWallFade.Count; fi++)
            {
                MeshRenderer? r = _facts[_factWallFade[fi]].Mesh;
                if (r == null)
                    continue;
                _censusFadeRenderers++;
                if (_claimedRenderers.Contains(r))
                    continue;
                // A fade renderer under a ProceduralWall belongs to that wall's segment; it can
                // only get here mid-stream (Apparance still generating) — the next rescan's
                // RefreshSegment picks it up, and adopting it now would double-track it.
                if (r.GetComponentInParent<ProceduralWall>() != null)
                    continue;
                Component? anchor = r.GetComponentInParent<ProceduralTileObserver>();
                if (anchor == null)
                    anchor = r.transform.parent != null ? r.transform.parent : r.transform;
                Component? splitRunOwner = null;
                // DOORWAY override (user ruling 2026-08-02: doorways NEVER fade): a fade
                // renderer hugging a door prop is that DOORWAY's frame/pillar — anchor it on
                // the door root so every renderer of one archway lands in ONE per-door segment
                // the decision loop holds permanently SOLID. This outranks both the
                // tile/parent grouping (the round-1 'L :' layer container that mixed two
                // doorways into one look-at segment — which would fade as a wall) and the
                // split routing (a doorway is archway-sized, never a room-engulfing slab).
                Transform? doorRoot = FindDoorwayRoot(r);
                if (doorRoot != null)
                {
                    // ROUND-11/12 SPLIT: the permanently-solid DOORWAY segment holds ONLY
                    // the arch. A door-hugging fade renderer OUTSIDE the arch — the
                    // flanking PILLARS ("Säulen die nicht zum Rechteck gehören") and the
                    // torch fires on them — joins the GATE COLUMN's own renderer set
                    // instead and fades NATIVELY with the gate face (round 12: the
                    // round-11 "leave unclaimed" left them solid forever). Sliver-skipped
                    // doors keep the old full-radius grouping.
                    if (HasGateColumnFor(doorRoot) && !IsArchProtected(r.bounds, r.name))
                    {
                        UnityGameEditorDoorProp? gdp =
                            doorRoot.GetComponent<UnityGameEditorDoorProp>();
                        if (gdp != null && _live.Segments.TryGetValue(gdp, out Segment? gseg)
                            && gseg.IsGateColumn && CollectWallFadeInfo(r, gseg))
                        {
                            gseg.Renderers.Add(r);
                            Bounds gb = gseg.Bounds;
                            gb.Encapsulate(r.bounds);
                            gseg.Bounds = gb;
                        }
                        continue;
                    }
                    anchor = doorRoot;
                }
                // A group that proved too fat to be a slab is tracked per renderer instead
                // (see _live.SplitAnchors) — route straight to the per-renderer segment so its
                // smoothing state survives every rescan.
                else if (_live.SplitAnchors.Contains(anchor))
                {
                    splitRunOwner = anchor;
                    anchor = r;
                }
                if (!_live.Segments.TryGetValue(anchor, out Segment? seg))
                {
                    seg = new Segment { Anchor = anchor, FromWallCache = false };
                    _live.Segments.Add(anchor, seg);
                    BeginRefresh(seg);
                }
                if (splitRunOwner != null)
                {
                    // ModBuild 259: a renderer routed to a per-renderer segment because its
                    // GROUP was split still belongs to that group's run (Segment.RunOwner).
                    seg.RunOwner = splitRunOwner;
                    seg.FromSplitRun = true;
                }
                seg.DoorRoot = doorRoot; // re-stamped every rescan (null for non-doorways)
                if (CollectWallFadeInfo(r, seg))
                {
                    seg.Renderers.Add(r);
                    if (!seg.HasBounds)
                    {
                        seg.Bounds = r.bounds;
                        seg.HasBounds = true;
                    }
                    else
                    {
                        seg.Bounds.Encapsulate(r.bounds);
                    }
                    _censusAdopted++;
                }
                else if (splitRunOwner != null)
                {
                    // MODBUILD 262 — THE THIRD SPLIT-RUN CREATION SITE NOW RECORDS ITS REFUSALS.
                    // The other two (NeutralizeEngulfingSegments, RefreshSplitWall) both stamp
                    // GeometryRefusedWhy on the else branch; this one had no else branch at all,
                    // so a piece refused HERE reached SweepRunLeftovers with a null reason AND no
                    // bounds and was tagged "no decision AABB, and NOT a choke-point refusal …
                    // a different defect from the 260 population" — which was false for exactly
                    // this piece and made the line claiming a COMPLETE per-reason distribution
                    // wrong with it. Only 4 renderers took this path in the whole ModBuild 260
                    // session, so the blast radius is small; the cost of leaving it was a
                    // phantom defect class for the next round to chase.
                    //
                    // The 2-arg call means figureArmOnly = false, i.e. the FULL choke point
                    // including the FLOOR arm, which is the plain refusal reason. Falsified by a
                    // piece printing this reason that CollectWallFadeInfo would have accepted.
                    seg.GeometryRefusedWhy = SplitPieceRefusalReason;
                }
            }
            _censusClaimed = _censusFadeRenderers - _censusAdopted;

            // Finalize adopted segments: stale-block cleanup + thickness epsilon; drop the empty
            // ones (their renderers died or stopped matching). Groups whose AABB engulfs their
            // own room are handled by NeutralizeEngulfingSegments AFTER room association — the
            // containment test needs the room's samples, and plain AABB fatness is not enough
            // (an L-shaped corner run is fat too and must keep whole-wall behaviour).
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                Segment seg = kv.Value;
                if (seg.IsGateColumn)
                {
                    // Gate columns may legitimately own zero renderers (kept alive), but
                    // the ones that adopted pillar/torch renderers this sweep (round 12)
                    // still need the leaver cleanup + epsilon derivation.
                    FinishRefresh(seg);
                    continue;
                }
                if (seg.FromWallCache)
                    continue;
                FinishRefresh(seg);
                if (seg.Renderers.Count == 0)
                {
                    // Adopted group dissolved (renderers died / stopped matching) — its hidden
                    // attachments must not outlive it (restore-everywhere discipline).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreSegmentMounted(seg);
                    RestoreSegmentStacked(seg);
                    _deadKeys.Add(kv.Key);
                }
            }
            foreach (Component dead in _deadKeys)
                _live.Segments.Remove(dead);
        }

        /// <summary>
        /// Per-renderer tracking for a cache wall whose combined AABB proved too fat (see the
        /// split in Rescan): every fade-capable renderer under the wall gets its own segment,
        /// keyed by the renderer, and is claimed so the adoption sweep leaves it alone. Dead
        /// renderers fall out via the dead-key sweep (their key is the renderer itself).
        /// </summary>
        private readonly List<Segment> _splitPieceScratch = new();

        /// <summary>
        /// ModBuild 261 — the ONE way a split piece can be refused, stated once and proved here
        /// rather than re-derived at two call sites.
        ///
        /// <para>PROOF OF EXHAUSTIVENESS. <see cref="CollectWallFadeInfo"/> returns false for
        /// exactly two reasons: (a) <c>IsStandingFigureProp</c>, and (b) no shared material with
        /// either a WallFade-family shader NAME or a live toggle. Neither split site can reach
        /// (b): <see cref="RefreshSplitWall"/> pre-filters with <c>RendererUsesWallFade</c>, which
        /// is the same <c>IsWallFadeShaderName</c> test the NAME arm uses, and the engulf split
        /// only ever iterates <c>group.Renderers</c>, i.e. renderers that already returned true
        /// from this very method earlier in the same rescan. So (a) is the whole set — and if a
        /// future change breaks that, the leftover audit will print this string for a piece the
        /// STANDING PROP census does not name, which is the falsifier.</para>
        /// </summary>
        private const string SplitPieceRefusalReason =
            "REFUSED AS WALL GEOMETRY at the choke point — a floor-standing prop "
            + "(WallSegmentFade.Standing.cs, FLOOR arm), so the piece owns NO renderer at all. "
            + "Boundless is the CONSEQUENCE, not the cause: nothing was ever collected to build "
            + "an AABB from. Relaxing the boundless fail-safe for this piece cannot move a pixel "
            + "— it has nothing to fade. The renderer is counted in the WALL-PATH AUDIT's "
            + "'standing-prop' class and named in the STANDING PROP census. Turn on "
            + "[WallFade] SplitRunAdoptGroundScenery to recruit this class as run passengers";

        /// <summary>The same refusal with the FLOOR arm already stood down — whatever is left is
        /// a FIGURE or actor, which the recruitment deliberately never touches. A large count of
        /// THIS while the dial is on would mean the hedge is figure-shaped, and that would be a
        /// finding rather than a fix.</summary>
        private const string SplitPieceFigureRefusalReason =
            "REFUSED AS WALL GEOMETRY at the choke point with the FLOOR arm ALREADY STOOD DOWN "
            + "(SplitRunAdoptGroundScenery is on) — so this is the FIGURE/actor arm, ModBuild 157, "
            + "and it is the one arm the recruitment must never relax. The piece owns no renderer "
            + "and no wall rule can move it";

        private void RefreshSplitWall(ProceduralWall wall)
        {
            _splitPieceScratch.Clear();
            MeshRenderer[] all = wall.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null || !RendererUsesWallFade(r))
                    continue;
                if (!_live.Segments.TryGetValue(r, out Segment? sub))
                {
                    sub = new Segment { Anchor = r, FromWallCache = true };
                    _live.Segments.Add(r, sub);
                }
                // ModBuild 259: re-stamped every rescan from the wall this method was CALLED
                // with — the run membership is the game's own parenting, not a proximity guess.
                sub.RunOwner = wall;
                sub.FromSplitRun = true;
                BeginRefresh(sub);
                // ModBuild 261: the FLOOR arm stands down here ONLY while the dial is on, and a
                // piece it would have refused is marked a PASSENGER — it rides the run's fade and
                // is kept out of the union, so the trigger is unchanged in either dial position.
                bool relaxG = WallFadeTuning.AdoptGroundScenery;
                if (CollectWallFadeInfo(r, sub, relaxG))
                {
                    sub.Renderers.Add(r);
                    sub.Bounds = r.bounds;
                    sub.HasBounds = true;
                    sub.RunPassenger = relaxG && IsStandingFigureProp(r);
                }
                else
                {
                    sub.GeometryRefusedWhy = relaxG
                        ? SplitPieceFigureRefusalReason : SplitPieceRefusalReason;
                }
                _claimedRenderers.Add(r);
                _splitPieceScratch.Add(sub);
            }

            // The wall's foliage dressing rides the NEAREST piece's fade (XZ distance between
            // AABB centers): a bush hangs on the piece it grows from, and that is the piece
            // whose fade makes it a view-blocking leftover.
            foreach (MeshRenderer r in all)
            {
                // Same standing-prop exclusion as the unsplit path, and the same two arms
                // (FIGURE + TREE, never the plain FLOOR arm): a floor-standing figure prop is
                // never a wall's foliage dressing, nor is a free-standing tree's canopy, while a
                // bush standing on the ground still is (WallSegmentFade.Standing.cs).
                if (r == null || !RendererUsesFoliage(r) || IsStandingFigureOnlyProp(r))
                    continue;
                Segment? best = null;
                float bestSq = float.PositiveInfinity;
                Vector3 c = r.bounds.center;
                foreach (Segment piece in _splitPieceScratch)
                {
                    if (!piece.HasBounds)
                        continue;
                    float dx = piece.Bounds.center.x - c.x;
                    float dz = piece.Bounds.center.z - c.z;
                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = piece;
                    }
                }
                best?.Foliage.Add(r);
            }

            foreach (Segment sub in _splitPieceScratch)
                FinishRefresh(sub);
            _splitPieceScratch.Clear();
        }

        /// <summary>
        /// ASSET-ROOT RULE (Torbogen ruling, req: whole asset hides): for a fade-capable
        /// renderer of an ADOPTED group, the asset root is the NEAREST ancestor whose subtree
        /// also contains at least one non-fade MeshRenderer, provided that subtree stays under
        /// <see cref="MaxAssetRootRenderers"/> renderers and within
        /// <see cref="MaxAssetRootDepth"/> levels. Evidence (hardware log + torbogen.png): the
        /// adopted group 'L : (guid)' is an Apparance layer root whose fade renderers are
        /// CR_ST_Door_01_Frame_Thin + EN_CR_Pillar_Thin — the doorway's wooden wings and arch
        /// trim are NON-fade siblings under the same per-doorway subtree (the door prop is an
        /// Apparance ProceduralProp: frame, pillars, wings and trim are generated into one
        /// subtree; Choreographer.OpenDoor animates that same object). Walking up from the
        /// frame finds that per-doorway node BEFORE the renderer-count cap trips on the layer
        /// root. FAILURE MODES (accepted, fail-open): (a) frames parented flat under a big
        /// container → cap trips → nothing attached, today's remnant stays; (b) an asset root
        /// that also parents small unrelated dressing hides it with the doorway — bounded by
        /// the count cap and the per-renderer ground/actor/tile exclusions below.
        /// </summary>
        private Transform? FindAssetRoot(MeshRenderer fadeRenderer)
        {
            Transform? node = fadeRenderer.transform.parent;
            for (int depth = 0; node != null && depth < MaxAssetRootDepth; depth++)
            {
                node.GetComponentsInChildren(includeInactive: false, _subtreeScratch);
                if (_subtreeScratch.Count > MaxAssetRootRenderers)
                    return null; // container scale ('L :' layer/section root) — stop, attach nothing
                foreach (MeshRenderer c in _subtreeScratch)
                {
                    if (c != null && !RendererUsesWallFade(c))
                        return node; // nearest ancestor that mixes fade + non-fade = the asset
                }
                node = node.parent;
            }
            return null;
        }

        /// <summary>
        /// The door prop whose position the renderer's AABB hugs (XZ gap ≤
        /// <see cref="DoorwayLinkMaxXZ"/>), nearest wins — the linkage from a fade renderer to
        /// its OWNING door. Deliberately spatial, not hierarchical: the hardware log proved the
        /// frames are parented flat under a big 'L :' Apparance section container (the ancestor
        /// walk's documented fail-open), while the door prop is a SIBLING subtree — but the door
        /// object knows exactly where it stands, and archway frames exist only around doors.
        /// </summary>
        private Transform? FindDoorwayRoot(MeshRenderer r)
        {
            if (_doorRoots.Count == 0)
                return null;
            Bounds b = r.bounds;
            Transform? best = null;
            float bestSq = DoorwayLinkMaxXZ * DoorwayLinkMaxXZ;
            foreach (Transform door in _doorRoots)
            {
                if (door == null)
                    continue;
                Vector3 p = door.position;
                float gx = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x));
                float gz = Mathf.Max(0f, Mathf.Max(b.min.z - p.z, p.z - b.max.z));
                float sq = gx * gx + gz * gz;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = door;
                }
            }
            return best;
        }

        /// <summary>
        /// ASSET-COMPLETE FADE collection (adopted groups only — cache walls keep their
        /// deliberate "props/doors under the same entity are never touched" contract, their
        /// foliage path already covers the dressing): re-attach, per rescan, every non-fade
        /// renderer under each fade renderer's asset root (<see cref="FindAssetRoot"/>) so the
        /// WHOLE mixed asset hides with the segment. Exclusions, in order: renderers the game
        /// itself disabled (unless WE hid them), fade-capable renderers (tracked as segments),
        /// already-owned siblings (one owner per renderer), GROUND-ish renderers (AABB top
        /// within the ground band of the room's anchored floor plane — floor never fades, in
        /// any attachment type), and renderers under live game logic (ProceduralWall = cache
        /// territory, ActorBehaviour = characters, TileBehaviour = worldspace tile UI/logic).
        /// Runs after room association + ground strip because the ground exclusion needs the
        /// room plane; segments without a trusted plane attach nothing (they are fail-safe
        /// solid anyway). Leavers are restored exactly like foliage leavers.
        /// </summary>
        private void CollectAdoptedSiblings()
        {
            _siblingOwned.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                if (seg.FromWallCache)
                    continue;
                seg.PrevSiblings.Clear();
                seg.PrevSiblings.AddRange(seg.Siblings);
                seg.Siblings.Clear();
                if (seg.DoorRoot != null)
                {
                    // DOORWAY segment (user ruling 2026-08-02: never fades — held
                    // permanently solid): needs no sibling attachments. Restore anything a
                    // previous owner state still hides (ownership handover; nothing may
                    // stay hidden without an owner).
                    if (seg.SiblingState != 0)
                        RestoreSegmentSiblings(seg);
                    seg.PrevSiblings.Clear();
                    continue;
                }
                if (RoomDecisionValid(seg.RoomIndex))
                {
                    float ceiling = _live.RoomFloorY[seg.RoomIndex] + GroundExclusionHeightWU;
                    foreach (MeshRenderer r in seg.Renderers)
                    {
                        if (r == null)
                            continue;
                        Transform? root = FindAssetRoot(r);
                        if (root == null)
                            continue;
                        root.GetComponentsInChildren(includeInactive: false, _subtreeScratch);
                        foreach (MeshRenderer c in _subtreeScratch)
                        {
                            if (c == null || _siblingOwned.Contains(c))
                                continue;
                            // A renderer the GAME disabled is not ours to manage — except
                            // one WE hid last rescan (still held faded): dropping it now
                            // would re-enable + re-hide it in a one-frame flash.
                            if (!c.enabled
                                && !(seg.SiblingState == 2 && seg.PrevSiblings.Contains(c)))
                                continue;
                            if (RendererUsesWallFade(c))
                                continue;
                            if (c.bounds.max.y <= ceiling)
                                continue; // floor-ish — never rides a fade, in any form
                            if (IsWaterProtected(c.bounds))
                                continue; // fountain/pond (user ruling 2026-08-09) — never fades
                            if (c.GetComponentInParent<ProceduralWall>() != null
                                || c.GetComponentInParent<ActorBehaviour>() != null
                                || c.GetComponentInParent<TileBehaviour>() != null)
                                continue;
                            _siblingOwned.Add(c);
                            seg.Siblings.Add(c);
                        }
                    }
                }
                if (seg.SiblingState != 0)
                {
                    // Restore leavers NOW — nothing else ever points at them again (round 15:
                    // including their dissolve channel, so no authored material stays swapped
                    // once nothing points at the renderer).
                    foreach (MeshRenderer prev in seg.PrevSiblings)
                    {
                        if (prev != null && !seg.Siblings.Contains(prev))
                            RestoreSiblingProp(seg, prev);
                    }
                    if (seg.Siblings.Count == 0)
                        seg.SiblingState = 0;
                }
                seg.PrevSiblings.Clear();
            }
        }

        /// <summary>
        /// GROUND FORENSICS (green-scenario "see-through floor", round 3): one line per room at
        /// heartbeat time naming every renderer whose AABB stands over the ROOM CENTER — the
        /// floor candidates (AABB top near the floor plane), what lies BELOW (the "inner walls"
        /// the user sees through the holes) and what hangs ABOVE. Each entry carries shader,
        /// material render queue, static-batch flag and our-MPB flag, so the next log says
        /// WHICH mesh the missing floor is, WHAT shader it runs and WHO touched it — instead of
        /// a fourth guessed fix.
        /// </summary>
        private void LogFloorColumnCensus()
        {
            if (_live.RoomBounds.Count == 0)
                return;
            // includeInactive ON (fehlender_boden2.png round 2): the first census could not
            // tell "no floor renderer EXISTS" from "a floor renderer exists but something
            // disabled it" — the exact fork between "Apparance never synthesized the room"
            // (the confirmed reveal bug: ApparanceEntity.CheckEntity destroys hidden rooms'
            // native entities and re-synthesis runs against the parked Camera.main viewpoint)
            // and "our fade / the game disabled it". Disabled entries now carry WHO: the
            // renderer's own enabled flag and the first inactive ancestor by name.
            MeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(includeInactive: true);
            var sb = new System.Text.StringBuilder();
            int rooms = Mathf.Min(_live.RoomBounds.Count, 8);
            for (int r = 0; r < rooms; r++)
            {
                Vector3 center = _live.RoomBounds[r].center;
                float floorY = r < _live.RoomFloorY.Count ? _live.RoomFloorY[r] : 0f;
                sb.Length = 0;
                sb.Append("FLOOR CENSUS room ").Append(r);
                if (r < _live.RoomLabels.Count)
                    sb.Append(" '").Append(_live.RoomLabels[r]).Append('\'');
                if (r < _roomRendererCounts.Count && _roomRendererCounts[r] > 1)
                    sb.Append('x').Append(_roomRendererCounts[r]);
                sb.Append(" center(").Append(center.x.ToString("F1")).Append(',')
                  .Append(center.z.ToString("F1")).Append(") floorY ").Append(floorY.ToString("F2"))
                  .Append(':');
                int listed = 0;
                foreach (MeshRenderer mr in all)
                {
                    if (mr == null)
                        continue;
                    Bounds b = mr.bounds;
                    if (center.x < b.min.x || center.x > b.max.x
                        || center.z < b.min.z || center.z > b.max.z)
                        continue;
                    if (listed++ >= 10) { sb.Append(" …"); break; }
                    string zone = b.max.y < floorY - 0.5f ? "BELOW"
                        : b.max.y <= floorY + 1.5f ? "FLOOR"
                        : "ABOVE";
                    Material? m = mr.sharedMaterial;
                    sb.Append(" [").Append(zone).Append("] '").Append(mr.name)
                      .Append("' y[").Append(b.min.y.ToString("F1")).Append("..")
                      .Append(b.max.y.ToString("F1")).Append("] sh='")
                      .Append(m != null && m.shader != null ? m.shader.name : "?")
                      .Append("' q").Append(m != null ? m.renderQueue : -1)
                      .Append(mr.isPartOfStaticBatch ? " BATCHED" : "")
                      .Append(mr.HasPropertyBlock() ? " OUR-MPB" : "");
                    // WHO turned it off: renderer.enabled = a component write (our fade only
                    // ever touches foliage/siblings this way); inactive hierarchy = a
                    // SetActive by name of the first inactive ancestor (ProceduralMapTile
                    // .ShowContent visibility toggles read as 'Generated Content'/'Preview').
                    if (!mr.enabled)
                        sb.Append(" OFF");
                    if (!mr.gameObject.activeInHierarchy)
                    {
                        Transform? t = mr.transform;
                        while (t != null && t.gameObject.activeSelf)
                            t = t.parent;
                        sb.Append(" INACTIVE:'")
                          .Append(t != null ? t.name : "?").Append('\'');
                    }
                    sb.Append(';');
                }
                if (listed == 0)
                    sb.Append(" (no renderer over the room center at all — nothing exists, "
                        + "not even disabled: the geometry was never generated)");
                VRLog.Info(Name, sb.ToString());
            }
            LogMapTileCensus(sb);
            LogApparanceViewpoint(sb);
        }

        /// <summary>
        /// One line per <see cref="ProceduralMapTile"/>: the game-side visibility state plus
        /// the Apparance generation state — visibility (Preview vs All), whether 'Generated
        /// Content' exists, how many of its children are active, whether the 'Preview' child
        /// (the scattered hex islands of an unrevealed room) is still showing, and the
        /// entity's IsPopulated/native-handle/IsBusy status plus the tile's distance from the
        /// synthesis viewpoint. Decides in one log whether a missing room floor is a
        /// VISIBILITY failure (Preview stuck on), a SYNTHESIS failure (visibility All,
        /// generation root empty — the parked-viewpoint reveal bug), or a DETAIL failure
        /// (built far from the viewpoint, coarse content only — the round-3 suspect).
        ///
        /// Round 3 added PER-CHILD forensics: the 'children 1/2 active, 229 renderer(s)'
        /// summary could not say WHICH child of 'Generated Content' holds the renderers —
        /// so a follow-up line per direct child names it, its activeSelf/activeInHierarchy
        /// state, its renderer counts (total / active-in-hierarchy / actually drawing), its
        /// combined bounds, and a sample of renderer names+y-bands+state. That splits
        /// "content exists but was left inactive" from "only preview-grade content was ever
        /// synthesized" without another blind hardware round.
        /// </summary>
        private void LogMapTileCensus(System.Text.StringBuilder sb)
        {
            // Where the engine is generating detail around RIGHT NOW — mirror of
            // ApparanceEngine.UpdateEngine's own source selection (focus override first,
            // Camera.main otherwise). Distances on the MAPTILE lines are measured to this.
            Vector3? viewpoint = null;
            try
            {
                ApparanceEngine? engineNow = ApparanceEngine.Instance;
                if (engineNow != null && engineNow.EnableDetailFocus && engineNow.DetailFocus != null)
                    viewpoint = engineNow.DetailFocus.transform.position;
                else if (Camera.main != null)
                    viewpoint = Camera.main.transform.position;
            }
            catch { /* engine mid-teardown — distances become 'n/a' */ }

            ProceduralMapTile[] tiles =
                UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>(includeInactive: true);
            foreach (ProceduralMapTile tile in tiles)
            {
                if (tile == null)
                    continue;
                sb.Length = 0;
                sb.Append("MAPTILE '").Append(tile.name)
                  .Append("' pos(").Append(tile.transform.position.x.ToString("F1")).Append(',')
                  .Append(tile.transform.position.z.ToString("F1"))
                  .Append(") vis=").Append(tile.visibility)
                  .Append(tile.gameObject.activeInHierarchy ? "" : " INACTIVE");
                if (viewpoint.HasValue)
                    sb.Append(" focusDist=")
                      .Append(Vector3.Distance(viewpoint.Value, tile.transform.position).ToString("F1"));
                Transform? gen = FindChildByName(tile.transform, "Generated Content");
                if (gen == null)
                {
                    sb.Append(" genContent=NONE (never generated)");
                }
                else
                {
                    int children = gen.childCount, active = 0, renderers = 0;
                    bool previewActive = false;
                    for (int i = 0; i < children; i++)
                    {
                        Transform c = gen.GetChild(i);
                        if (c.gameObject.activeSelf)
                        {
                            active++;
                            if (c.name == "Preview")
                                previewActive = true;
                        }
                    }
                    _subtreeScratch.Clear();
                    gen.GetComponentsInChildren(includeInactive: true, _subtreeScratch);
                    renderers = _subtreeScratch.Count;
                    sb.Append(" genContent=").Append(gen.gameObject.activeSelf ? "on" : "OFF")
                      .Append(" children ").Append(active).Append('/').Append(children)
                      .Append(" active, ").Append(renderers).Append(" renderer(s)")
                      .Append(previewActive ? ", PREVIEW STILL ON" : "");
                }
                try
                {
                    ApparanceEntity? entity = tile.GetComponent<ApparanceEntity>();
                    if (entity != null)
                        sb.Append(" entity populated=").Append(entity.IsPopulated)
                          .Append(" handle=").Append(entity.m_EntityHandle != 0 ? "built" : "NONE")
                          .Append(" busy=").Append(entity.IsBusy)
                          .Append(" dynDetail=").Append(entity.DynamicDetail);
                }
                catch { sb.Append(" entity=?"); }
                VRLog.Info(Name, sb.ToString());

                if (gen != null)
                    LogGeneratedContentChildren(sb, tile.name, gen);
            }
        }

        /// <summary>Renderer sample cap per 'Generated Content' child line — enough names to
        /// recognize the asset family (Unseen preview hexes vs full CR floor/wall pieces)
        /// without flooding a heartbeat.</summary>
        private const int MaxChildRendererSamples = 8;

        /// <summary>
        /// The per-child forensics behind a MAPTILE line: for each DIRECT child of the
        /// tile's 'Generated Content' (the containers <c>ProceduralMapTile.ShowContent</c>
        /// toggles — 'Preview' vs the synthesized full-content groups), log activation,
        /// renderer census and bounds. Bounds are the union of renderer AABBs
        /// (world-space); inactive renderers still report usable transform-derived bounds,
        /// which is exactly what we need to see WHERE never-shown content would render.
        /// </summary>
        private void LogGeneratedContentChildren(
            System.Text.StringBuilder sb, string tileName, Transform gen)
        {
            for (int i = 0; i < gen.childCount; i++)
            {
                Transform child = gen.GetChild(i);
                _subtreeScratch.Clear();
                child.GetComponentsInChildren(includeInactive: true, _subtreeScratch);

                int total = _subtreeScratch.Count, activeInHier = 0, drawing = 0;
                Bounds union = default;
                bool haveBounds = false;
                foreach (MeshRenderer mr in _subtreeScratch)
                {
                    if (mr == null)
                        continue;
                    bool act = mr.gameObject.activeInHierarchy;
                    if (act)
                    {
                        activeInHier++;
                        if (mr.enabled)
                            drawing++;
                    }
                    Bounds b = mr.bounds;
                    if (!haveBounds) { union = b; haveBounds = true; }
                    else union.Encapsulate(b);
                }

                sb.Length = 0;
                sb.Append("MAPTILE '").Append(tileName)
                  .Append("' child[").Append(i).Append("] '").Append(child.name)
                  .Append("' self=").Append(child.gameObject.activeSelf ? "on" : "OFF")
                  .Append(" hier=").Append(child.gameObject.activeInHierarchy ? "on" : "OFF")
                  .Append(" renderers ").Append(total)
                  .Append(" (").Append(activeInHier).Append(" activeInHierarchy, ")
                  .Append(drawing).Append(" drawing)");
                if (haveBounds)
                    sb.Append(" bounds c(").Append(union.center.x.ToString("F1")).Append(',')
                      .Append(union.center.y.ToString("F1")).Append(',')
                      .Append(union.center.z.ToString("F1"))
                      .Append(") s(").Append(union.size.x.ToString("F1")).Append(',')
                      .Append(union.size.y.ToString("F1")).Append(',')
                      .Append(union.size.z.ToString("F1")).Append(')');

                int listed = 0;
                foreach (MeshRenderer mr in _subtreeScratch)
                {
                    if (mr == null)
                        continue;
                    if (listed >= MaxChildRendererSamples) { sb.Append(" …"); break; }
                    Bounds b = mr.bounds;
                    bool disabled = mr.gameObject.activeInHierarchy && !mr.enabled;
                    sb.Append(listed == 0 ? "; sample: '" : " '").Append(mr.name)
                      .Append("'[").Append(mr.gameObject.activeInHierarchy
                          ? (mr.enabled ? "on" : "disabled") : "off")
                      .Append(" y").Append(b.min.y.ToString("F1")).Append("..")
                      .Append(b.max.y.ToString("F1"));
                    // ROUND 4: a sampled DISABLED renderer names its game MaterialLoader
                    // state (ml=no-loader / never-started / loading a/b / null-result a/b /
                    // done-stuck / done) — same classifier the MaterialLoaderHeal watchdog
                    // acts on, so the next hardware log proves WHICH stranding mechanism
                    // left the revealed room's renderers active-but-disabled.
                    if (disabled)
                    {
                        try { sb.Append(" ml=").Append(MaterialLoaderHeal.DescribeForRenderer(mr)); }
                        catch { sb.Append(" ml=?"); }
                    }
                    sb.Append(']');
                    listed++;
                }
                VRLog.Info(Name, sb.ToString());
            }
        }

        /// <summary>
        /// The Apparance synthesis viewpoint line: which position the engine is generating
        /// detail around — the parked Camera.main (the bug) or the mod's head-tracking
        /// DetailFocus override (<see cref="ApparanceDetailFocus"/>, the fix). Proves from a
        /// hardware log that the override engaged, and where the parked camera actually sat.
        /// </summary>
        private static void LogApparanceViewpoint(System.Text.StringBuilder sb)
        {
            sb.Length = 0;
            sb.Append("APPARANCE VIEWPOINT: ");
            try
            {
                ApparanceEngine? engine = ApparanceEngine.Instance;
                if (engine == null)
                {
                    sb.Append("no engine instance");
                }
                else if (engine.EnableDetailFocus && engine.DetailFocus != null)
                {
                    Vector3 p = engine.DetailFocus.transform.position;
                    sb.Append("DetailFocus override '").Append(engine.DetailFocus.name)
                      .Append("' at (").Append(p.x.ToString("F1")).Append(',')
                      .Append(p.y.ToString("F1")).Append(',')
                      .Append(p.z.ToString("F1")).Append(')');
                }
                else
                {
                    Camera? main = Camera.main;
                    if (main != null)
                    {
                        Vector3 p = main.transform.position;
                        sb.Append("Camera.main '").Append(main.name)
                          .Append("' (PARKED under VR) at (").Append(p.x.ToString("F1"))
                          .Append(',').Append(p.y.ToString("F1")).Append(',')
                          .Append(p.z.ToString("F1")).Append(')');
                    }
                    else
                    {
                        sb.Append("no Camera.main — engine falls back to first active camera");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }
            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>Breadth-limited recursive child search by exact name (the game's own
        /// FindInChildren equivalent — map tiles nest 'Generated Content' a level down).</summary>
        private static Transform? FindChildByName(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name == name)
                    return c;
                Transform? deep = FindChildByName(c, name);
                if (deep != null)
                    return deep;
            }
            return null;
        }

        /// <summary>Mod-owned visual (hands, cards, panels, MR backing plates…)? Never scenery:
        /// such a renderer must neither be adopted by ANY attachment sweep nor appear in their
        /// candidate/near-miss diagnostics. Two signals, because not every mod object lives on
        /// the mod layer: hardware round 3 caught the MR sky backing 'GloomhavenVR.MrBacking'
        /// (y[21.3..33.3]) in the stacked-shell NEAR-MISS census — every mod-created object
        /// carries the 'GloomhavenVR.' name prefix (repo convention), so that prefix is the
        /// second, layer-independent test.</summary>
        private static bool IsModObject(Renderer r) =>
            r.gameObject.layer == VRLayers.ModLayer
            || r.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal);

        /// <summary>
        /// FIGURES ARE NEVER TOUCHED — round-7 ruling, same severity as the Lights rule
        /// (mauern_problem_neu.png: the BRUTE's horned back-of-head was PERMANENTLY hidden —
        /// an accessory mesh adopted by a wall sweep inside a faded wall's ring-spanning
        /// AABB). Airtight, deliberately over-broad (fail-open = the renderer stays visible):
        /// <list type="bullet">
        /// <item>every <see cref="SkinnedMeshRenderer"/>, outright — characters are skinned,
        ///   scenery is not; the banner case loses skinned support, accepted;</item>
        /// <item>anything under an <c>ActorBehaviour</c> ancestor (the game's board actor);</item>
        /// <item>anything under a <c>CInteractableActor</c> ancestor (the game's interactable
        ///   figure root — the same component FigureGrab picks by);</item>
        /// <item>anything under an <c>Animator</c> ancestor — a horn/head accessory hangs off
        ///   a BONE, and whatever the actor component layout, the animation rig root is
        ///   always above it. Also excludes animated props (chests…), which no wall system
        ///   should ever hide anyway.</item>
        /// </list>
        /// Checked by EVERY adoption sweep (stack candidates + adoption + fast reclaim, wall
        /// body, mounted dressing, corner pieces) and enforced retroactively by
        /// <see cref="PurgeFigureRenderers"/> each rescan (restitution: a previously-adopted
        /// figure renderer is restored the moment this guard classifies it).
        /// </summary>
        private static bool IsFigureOrActorRenderer(Renderer r)
        {
            if (r is SkinnedMeshRenderer)
                return true;
            // FAST PATH (PERF S1) — only inside an open memo scope, and only for a renderer
            // whose whole ancestor chain is active. See FigureAncestryMemo.
            if (_figureMemoActive && r.gameObject.activeInHierarchy)
                return FigureAncestry(r.transform);
            return r.GetComponentInParent<ActorBehaviour>() != null
                || r.GetComponentInParent<CInteractableActor>() != null
                || r.GetComponentInParent<Animator>() != null;
        }

        /// <summary>
        /// PERF S1 (2026-08-09): memo for <see cref="IsFigureOrActorRenderer"/>'s ancestor
        /// search, keyed by transform.
        ///
        /// <para>WHY IT WAS WORTH IT. The guard runs THREE separate
        /// <c>GetComponentInParent</c> walks, and the stacked-shell candidate prefilter calls
        /// it for EVERY renderer in the scene — ~3000 of them per rescan, each walk visiting
        /// every level up to the scene root. That is tens of thousands of native component
        /// lookups per rescan, and it was the largest non-<c>FindObjectsOfType</c> item in the
        /// 50-97 ms rescan.</para>
        ///
        /// <para>WHY THE VERDICT IS UNCHANGED, RENDERER FOR RENDERER. "Is there an
        /// ActorBehaviour / CInteractableActor / Animator on this transform or any ancestor"
        /// is exactly what the three <c>GetComponentInParent</c> calls answer, and it is a
        /// property of the CHAIN, so a transform's answer is its own components OR its
        /// parent's answer — which is what <see cref="FigureAncestry"/> computes and caches.
        /// Two guards keep it exact rather than merely equivalent-in-practice:</para>
        /// <list type="bullet">
        /// <item>ACTIVE CHAINS ONLY. <c>GetComponentInParent&lt;T&gt;()</c> without
        ///   <c>includeInactive</c> considers only active GameObjects; for a renderer that is
        ///   <c>activeInHierarchy</c> every ancestor is active by definition, so on that path
        ///   the qualifier is vacuous and the memo cannot disagree. A renderer whose chain is
        ///   NOT fully active takes the original three calls verbatim.</item>
        /// <item>SCOPED, NEVER PERSISTENT. The memo is only consulted between
        ///   <see cref="BeginFigureMemo"/> and <see cref="EndFigureMemo"/>, which bracket ONE
        ///   synchronous pass (a rescan, a fast-reclaim sweep). No game code runs inside such
        ///   a pass, so nothing can re-parent an actor mid-pass — the case the round-7 "belt
        ///   over the prefilter" re-check exists for is a re-parent between FRAMES, and the
        ///   memo is empty at every frame boundary.</item>
        /// </list>
        /// </summary>
        private static readonly Dictionary<Transform, bool> FigureAncestryMemo = new(1024);
        private static bool _figureMemoActive;

        private static void BeginFigureMemo()
        {
            FigureAncestryMemo.Clear();
            GameLogicAncestryMemo.Clear();
            WallGeneratorAncestryMemo.Clear();
            _figureMemoActive = true;
        }

        private static void EndFigureMemo()
        {
            _figureMemoActive = false;
            FigureAncestryMemo.Clear(); // never hold transform references across frames
            GameLogicAncestryMemo.Clear();
            WallGeneratorAncestryMemo.Clear();
        }

        /// <summary>Does this transform or any ancestor carry one of the figure components?
        /// (Memoized upward — see <see cref="FigureAncestryMemo"/>.)</summary>
        private static bool FigureAncestry(Transform t)
        {
            if (FigureAncestryMemo.TryGetValue(t, out bool cached))
                return cached;
            bool here = t.GetComponent<ActorBehaviour>() != null
                || t.GetComponent<CInteractableActor>() != null
                || t.GetComponent<Animator>() != null;
            Transform? parent = t.parent;
            bool verdict = here || (parent != null && FigureAncestry(parent));
            FigureAncestryMemo[t] = verdict;
            return verdict;
        }

        /// <summary>
        /// "Is this renderer LIVE GAME LOGIC or WORLDSPACE UI?" — the second ancestry question
        /// every adoption sweep asks, and until PERF S3 the only one that was not memoised.
        ///
        /// <para>THE CALL SITES, verbatim, all five of them:
        /// <c>c.GetComponentInParent&lt;TileBehaviour&gt;() != null ||
        /// c.GetComponentInParent&lt;Canvas&gt;() != null</c> — the stacked-shell adoption rounds
        /// and corner collection (<c>WallSegmentFade.Stacked.cs</c>, four sites across the
        /// rescan pass and the fast-reclaim pass) and the mounted-dressing sweep
        /// (<c>WallSegmentFade.Mounted.cs</c>). Each of those runs the pair over a candidate set
        /// that scales with the scene, and each pair is two ancestor walks to the scene root.</para>
        ///
        /// <para>WHY THE VERDICT IS UNCHANGED. Identical to the argument for
        /// <see cref="FigureAncestryMemo"/>, term for term: the question is a property of the
        /// CHAIN, so a transform's answer is its own components OR its parent's answer; the memo
        /// is only consulted for a renderer whose chain is fully active (which is when
        /// <c>GetComponentInParent&lt;T&gt;()</c>'s implicit active-only qualifier is vacuous),
        /// and only inside the SAME <see cref="BeginFigureMemo"/>/<see cref="EndFigureMemo"/>
        /// window, which brackets one synchronous pass during which no game code runs and
        /// nothing can be re-parented. Anything else takes the two original calls.</para>
        ///
        /// <para>The third clause those call sites carry — <c>GetComponent&lt;TMP_Text&gt;()</c>
        /// — is a question about the renderer's OWN GameObject, not its ancestry, and is left
        /// exactly where it is.</para>
        /// </summary>
        private static readonly Dictionary<Transform, bool> GameLogicAncestryMemo = new(1024);

        /// <summary>Does this renderer sit under a <c>TileBehaviour</c> (live game logic) or a
        /// <c>Canvas</c> (worldspace UI)? See <see cref="GameLogicAncestryMemo"/>.</summary>
        private static bool HasGameLogicAncestry(Component c)
        {
            if (_figureMemoActive && c.gameObject.activeInHierarchy)
                return GameLogicAncestry(c.transform);
            return c.GetComponentInParent<TileBehaviour>() != null
                || c.GetComponentInParent<Canvas>() != null;
        }

        private static bool GameLogicAncestry(Transform t)
        {
            if (GameLogicAncestryMemo.TryGetValue(t, out bool cached))
                return cached;
            bool here = t.GetComponent<TileBehaviour>() != null
                || t.GetComponent<Canvas>() != null;
            Transform? parent = t.parent;
            bool verdict = here || (parent != null && GameLogicAncestry(parent));
            GameLogicAncestryMemo[t] = verdict;
            return verdict;
        }

        /// <summary>
        /// MODBUILD 266 — "DID THE WALL GENERATOR BUILD THIS?", memoised exactly like
        /// <see cref="FigureAncestryMemo"/> and <see cref="GameLogicAncestryMemo"/>: same
        /// chain-property argument, same active-chain qualifier, same
        /// <see cref="BeginFigureMemo"/>/<see cref="EndFigureMemo"/> window. The question itself
        /// is the one term the WALL MEMBER leftover class already resolves membership with
        /// (<c>WallSegmentFade.Inside.cs</c>, <c>IsWallGeneratedMember</c>:
        /// <c>r.GetComponentInParent&lt;ProceduralWall&gt;() != null</c>) and the one the
        /// unit-affinity walk stops at — no second resolver is introduced.
        /// </summary>
        private static readonly Dictionary<Transform, bool> WallGeneratorAncestryMemo = new(1024);

        private static bool HasWallGeneratorAncestry(Component c)
        {
            if (_figureMemoActive && c.gameObject.activeInHierarchy)
                return WallGeneratorAncestry(c.transform);
            return c.GetComponentInParent<ProceduralWall>() != null;
        }

        private static bool WallGeneratorAncestry(Transform t)
        {
            if (WallGeneratorAncestryMemo.TryGetValue(t, out bool cached))
                return cached;
            bool here = t.GetComponent<ProceduralWall>() != null;
            Transform? parent = t.parent;
            bool verdict = here || (parent != null && WallGeneratorAncestry(parent));
            WallGeneratorAncestryMemo[t] = verdict;
            return verdict;
        }

        /// <summary>
        /// MODBUILD 266 — WALL DRESSING BY PROVENANCE, THE ANSWER TO "die Flaggen an den Wänden
        /// faden nicht mit der Wand mit" (user, 2026-08-25, Flaggen.jpg: "Von hinten sind dann
        /// noch die Stangen zu sehen … Ich möchte, dass die Flagge inklusive der Stange
        /// vollständig mit faded").
        ///
        /// <para><b>THE RULE.</b> A renderer the WALL GENERATOR built is wall dressing —
        /// whatever its renderer TYPE and whatever ANIMATES it. Membership in a
        /// <c>ProceduralWall</c> subtree is the discriminator, which is the same term the WALL
        /// MEMBER leftover class is already resolved with; nothing is keyed on a name family
        /// ("Banner", "Hanging", "Flag"), because the standing requirement is that this works in
        /// every scenario and room in the game and a tileset is free to name its hangings
        /// anything.</para>
        ///
        /// <para><b>THE TWO REFUSALS IT LIFTS, from the ModBuild-265 log verbatim.</b>
        /// <c>[FLOATING] 'EN_CR_Hanging_01_Cloth_Post'[skinned] foot 2.26 wu … DRAWING 3.75 wu
        /// from 'Wall 4' whose fade is 1.00 — not adopted because: renderer type
        /// SkinnedMeshRenderer is not scenery</c> and <c>[FLOATING] 'CR_BT_BanditBanner_Wall'
        /// [mesh] foot 2.98 wu … — not adopted because: FIGURE (never touched — round-7 ruling,
        /// Lights-rule severity)</c>. Both hang under <c>Wall N/Generated Content/…</c>, i.e.
        /// inside the wall's own subtree, and the same log's whole-unit line names them as the
        /// only holdouts of a unit the wall is otherwise fading:
        /// <c>TORN 'PCG_Test_Feature_Small_2' 19/21 written … LEFT SOLID under the same root:
        /// EN_CR_Hanging_01_Cloth_Post, EN_CR_Hanging_01_Mesh</c>.</para>
        ///
        /// <para><b>WHY IT CANNOT WIDEN THE FIGURE GATE — the round-7 ruling is untouched
        /// (FIGURES ARE NEVER TOUCHED, Lights-rule severity).</b>
        /// <see cref="IsFigureOrActorRenderer"/> itself is NOT changed: this is a separate,
        /// strictly narrower predicate that a call site may consult, and it carries the
        /// <c>ActorBehaviour</c> / <c>CInteractableActor</c> parent chain as an ABSOLUTE VETO.
        /// The load-bearing claim is that a figure is never a child of a wall's
        /// <c>Generated Content</c>, and it is CHECKED rather than asserted — from the game's own
        /// code, not from our ledger: <c>Choreographer</c> parents every figure it spawns to the
        /// BOARD root and nowhere else (<c>gameObject.transform.SetParent(ClientScenarioManager
        /// .s_ClientScenarioManager.m_Board.transform)</c> at :904 for enemies/characters, the
        /// same call at :1087, and <c>ObjectPool.Spawn(characterPrefabFromBundle,
        /// …m_Board.transform, …)</c> at :1191 — all three spawn paths), while the actor prefab
        /// that carries <c>ActorBehaviour</c> is parented under the figure's own rig
        /// <c>Animator</c>. A <c>ProceduralWall</c> is an Apparance <c>ProceduralTileObserver</c>
        /// hanging off a map tile, so it is on no figure's ancestor chain. The round-7 incident
        /// (the BRUTE's horned head, mauern_problem_neu.png) was a GEOMETRIC over-reach — an
        /// accessory swallowed by a faded wall's ring-spanning AABB — and hierarchy would have
        /// refused it on both terms at once.</para>
        ///
        /// <para>WHAT IS DELIBERATELY NOT DONE: the <c>SkinnedMeshRenderer</c> arm and the
        /// <c>Animator</c> arm of <see cref="IsFigureOrActorRenderer"/> stay exactly as they are
        /// for every renderer OUTSIDE a wall subtree, which is every figure in the game. The
        /// mod-object test is asked first so a mod-owned visual can never be read as scenery.</para>
        ///
        /// <para>COST: one memoised ancestor walk, and the un-memoised actor pair only for a
        /// renderer that IS inside a wall subtree — a handful per rescan. Rescan cadence only,
        /// never per frame, no scene sweep. MULTIPLAYER: a read of local scene hierarchy;
        /// no decision is networked, no wire field, no peer-visible state.</para>
        ///
        /// <para>FALSIFIED BY: a FIGURE RESTITUTION line, or a mounted census naming a hero,
        /// monster or summon renderer — then a figure IS reachable inside a wall subtree and the
        /// provenance term is not the discriminator.</para>
        /// </summary>
        private static bool IsWallGeneratedDressing(Renderer r)
        {
            if (r == null || IsModObject(r))
                return false;
            if (!HasWallGeneratorAncestry(r))
                return false;
            // THE ABSOLUTE VETO. Never relaxed, and never memoised through the figure memo:
            // that memo bundles Animator in with the two actor components, and Animator is
            // exactly the term this predicate exists to stop deciding on its own.
            return r.GetComponentInParent<ActorBehaviour>() == null
                && r.GetComponentInParent<CInteractableActor>() == null;
        }

        /// <summary>Any shared material on a foliage-family shader? (Cached per Shader.)</summary>
        private bool RendererUsesFoliage(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    continue;
                Shader sh = m.shader;
                if (sh == null)
                    continue;
                if (!_shaderFoliageVerdict.TryGetValue(sh, out bool foliage))
                {
                    foliage = IsFoliageShaderName(sh.name);
                    _shaderFoliageVerdict[sh] = foliage;
                }
                if (foliage)
                    return true;
            }
            return false;
        }

        /// <summary>Any shared material on a wall-fade-capable shader? (Cached per Shader.)</summary>
        private bool RendererUsesWallFade(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    continue;
                Shader sh = m.shader;
                if (sh == null)
                    continue;
                if (!_shaderVerdict.TryGetValue(sh, out bool capable))
                {
                    capable = IsWallFadeShaderName(sh.name);
                    _shaderVerdict[sh] = capable;
                }
                if (capable)
                    return true;
            }
            return false;
        }

        /// <summary>ADJACENT RE-ANCHOR reach (round 6, wu; round 11 raised 2.0 → 4.0): a
        /// wall whose nearest room is unanchorable re-anchors to an anchored logical room
        /// only when it PHYSICALLY borders it — XZ gap at most this. Round-11 hardware: the
        /// two gate-flanking TOWERS stood permanently solid among 18 fail-safe walls — they
        /// PROTRUDE outward from the gate face on the rock base, beyond the old 2.0-wu
        /// reach of the room footprint. 4.0 covers tower/buttress protrusion while distant
        /// unrevealed rooms (other map tiles ≥ ~11 wu away) still can never reach. Every
        /// re-anchored wall is named in the census line below.</summary>
        private const float AdjacentReanchorMaxGapWU = 4.0f;

        /// <summary>Round-11 census: which walls the adjacent re-anchor rescued this rescan
        /// (name + XZ gap) — the log line that shows the towers joining the fade.</summary>
        private readonly List<string> _reanchorCensus = new();
        private int _lastLoggedReanchorCount = -1;

        /// <summary>ROOM SEAM band (wu) — user report 2026-08-09, the wall piece that faded
        /// "falsch rum". A wall whose AABB is within this of a SECOND decision-valid room's
        /// box borders that room too and is judged against it as well (max coverage wins; see
        /// the long note in <see cref="AssociateRooms"/>). Sized as ONE WALL THICKNESS, from
        /// the hardware log itself: this tileset's slabs measure 0.8–1.8 wu across (segment
        /// BlockEps 0.45–0.90 = half-thickness) and the two room boxes of the reported level
        /// sit 0.65 wu apart, so a partition standing in that seam is within ~1.25 wu of both
        /// while a wall well inside one room is not. Deliberately far below the 4.0 wu
        /// re-anchor reach: this must catch partitions, never distant rooms.</summary>
        private const float RoomBorderBandWU = 1.25f;

        /// <summary>Census of the seam walls (name + the rooms they are judged against) —
        /// change-triggered, so the next hardware log names them without spamming.</summary>
        private readonly List<string> _seamCensus = new();
        private int _lastLoggedSeamCount = -1;

        /// <summary>
        /// Is this material's wall-fade subgraph PRESENT AND DRIVEABLE (round-9 game-wide
        /// audit — all known gate spellings, each with its liveness rule)? Requires
        /// <c>_Cutoff</c> (the clip the fade sweeps), then:
        /// <list type="bullet">
        /// <item><c>_ToggleWallfade</c> — a runtime float uniform (DXBC-verified on the HIGH
        ///   variant + ParticleMaster): always driveable → native.</item>
        /// <item><c>_WallFade_On</c> — a compile-time switch (keyword
        ///   <c>_WALLFADE_ON_ON</c>; ModBuild-65 adjudication): native only when the
        ///   authored value is 1 or the keyword is enabled — otherwise the fade branch is
        ///   absent from the compiled variant and the MPB float is inert (the round-8
        ///   floors), so the renderer belongs to the enabled fallback instead.</item>
        /// <item><c>_ToggleWallFadeLocal</c> — the game's authored per-asset opt-out
        ///   (<c>ToggleWallFadeScript</c> writes it at Start): native only when it reads
        ///   nonzero. An opted-out asset is authored ALWAYS-SOLID and is honored (never
        ///   pinned to 1 — the doorway-ruling spirit).</item>
        /// </list>
        /// </summary>
        private static bool HasLiveWallFadeToggle(Material m)
        {
            if (!m.HasProperty(CutoffId))
                return false;
            if (m.HasProperty(ToggleWallfadeMatId))
                return true;
            if (m.HasProperty(WallFadeOnMatId))
                return m.GetFloat(WallFadeOnMatId) != 0f || m.IsKeywordEnabled(WallFadeOnKeyword);
            if (m.HasProperty(ToggleWallFadeLocalMatId))
                return Mathf.Abs(m.GetFloat(ToggleWallFadeLocalMatId)) > 0.5f;
            return false;
        }

        /// <summary>A wall-fade gate EXISTS on some material but is not live (authored off /
        /// keyword-absent variant) — the audit line's 'gated-off' class: authored to stay
        /// solid, deliberately untouched.</summary>
        private bool HasGatedOffWallFadeToggle(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null || !m.HasProperty(CutoffId))
                    continue;
                bool hasGate = m.HasProperty(WallFadeOnMatId)
                    || m.HasProperty(ToggleWallfadeMatId)
                    || m.HasProperty(ToggleWallFadeLocalMatId);
                if (hasGate && !HasLiveWallFadeToggle(m))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// WALL-PATH AUDIT (round-9 game-wide generalization; user: "wende das mit ALLEN
        /// Mauer-Assets aus ALLEN Levels genauso an"): one heartbeat line that classifies
        /// EVERY cache wall's child renderers by the discovery/delivery path that owns them —
        /// the self-audit that lets every future hardware log prove a new tileset is covered
        /// without another blind round. Classes: native-name (WallFade shader family),
        /// toggle-native (live gate — round 8), attachment-claimed (body/stacked/mounted/
        /// corner via the ownership table), foliage, ground-band (solid by design),
        /// figure-guarded, gated-off (authored always-solid — honored), and UNCLAIMED.
        /// UNCLAIMED &gt; 0 is the alarm: an asset family fell through every path.
        /// </summary>
        /// <summary>
        /// ModBuild 262 — the audit is SLICED and repeats, and it has the two buckets it was
        /// missing.
        ///
        /// <para>WHY IT HAD TO CHANGE. In the ModBuild 260 log this line fired THREE times, all
        /// inside the first quarter of the session, and never again: it hung off the heartbeat
        /// block, which re-arms only on a ±5 segment change or a fade-renderer census change. So
        /// its <c>154 → 111 UNCLAIMED</c> was a startup transient with no follow-up measurement,
        /// and 111 was then quoted for two rounds as if it were steady state. The heartbeat
        /// itself cannot simply be re-armed: <c>WallFade.Census</c> measured 25.3 ms average
        /// (worst 34.3 ms) over its 3 frames in that log, because it also runs two whole-scene
        /// <c>FindObjectsOfType</c> walks. This audit is therefore driven on its own cadence and
        /// SPENDS AT MOST <see cref="ClassifyBudgetMillis"/> PER FRAME — the number this file
        /// already committed to for census work — resuming next frame, and only while the rescan
        /// pipeline is idle so the two budgets can never land on the same frame. Its own
        /// <c>WallFade.PathAudit</c> scope states the real cost in the next log; if that scope
        /// reports a per-frame worst above the budget, this slicing is broken and the number
        /// falsifies it.</para>
        ///
        /// <para>THE FALSE POSITIVE IT HAD, in this repo's own words at
        /// <c>WallSegmentFade.Water.cs:18-23</c>: <c>StripGroundRenderers</c> removes a
        /// water-protected renderer from BOTH lists, and the water surface stands above the
        /// ground band by construction (that is exactly why the ground strip missed it), so it
        /// landed in <c>UNCLAIMED [ALARM]</c> with nothing wrong with it. The 260 log proves it
        /// live — <c>CR_FR_Wall_Rocky_Verge_Bushes_02 (1)</c> is UNCLAIMED on both audits that
        /// saw any walls and is named on all 22 water-protection lines — and that piece is the
        /// Brunnen the user EXPRESSLY allows to stay. Water and the doorway arch now have their
        /// own buckets, both of them standing user rulings (2026-08-09 and 2026-08-02).</para>
        ///
        /// <para>AND THE UNCLAIMED BUCKET NOW CARRIES A VERDICT. UNCLAIMED renderers are the one
        /// population neither leftover instrument can see — unclaimed means no list owns them, so
        /// the split-run sweep cannot reach them, and the mounted sweep only sees the ones that
        /// are airborne AND within <c>MountedNearMissXZ</c> of a faded wall. They get the same
        /// three classes and the same two threshold-free definitions as everything else.</para>
        ///
        /// <para>ModBuild 284 — CONVERGENCE, AND WHY THIS PASS WAS NOT SIMPLY RETIRED. It was the
        /// second-largest per-frame cost in the whole subsystem — 2.29 ms avg / 8.4 ms per second
        /// in the ModBuild 277 log — and it is pure diagnostic, on a topic the user closed at
        /// ModBuild 283. That is the shape of this project's "a probe that answered is spent"
        /// entry, where a readiness probe kept blitting for 44,200 ticks after it had answered.
        /// It is NOT retired anyway, because it is the ONLY instrument that can raise
        /// <c>UNCLAIMED &gt; 0</c> — the alarm that a new tileset's asset family fell through
        /// every delivery path — and it is the line a reader greps to decide a tileset is
        /// covered. Deleting it would trade a measured 0.75 % of wall-clock for a blind spot,
        /// and this subsystem's own history is a list of blind spots that cost builds.
        ///
        /// <para>So the pass BACKS OFF INSTEAD: 2 s, then 4, 8, 16, 32, 60 s while its own bucket
        /// tally does not move, and straight back to 2 s the moment it does. At convergence that
        /// is ~0.26 ms/s. The re-arm predicate is the audit's OWN OUTPUT and says nothing about
        /// the scene, which is deliberate — there is no scene predicate here to be wrong about,
        /// and a new scenario moves the wall count, which moves the tally, which re-arms the
        /// cadence without anything having to notice the scenario. The failure mode if the idea
        /// is wrong is that a diagnostic goes quiet; it can never change a pixel, because nothing
        /// in this pass writes.</para>
        ///
        /// <para>Its falsifier ships with it: the line states the interval it is ACTUALLY running
        /// at and how many consecutive passes agreed, so "this converged 40 s ago" and "this is
        /// broken and stopped" are different readings rather than the same silence. The scenario
        /// teardown at <c>SetActive(false)</c> resets the backoff along with the pass.</para>
        /// </summary>
        private void StepWallPathAudit(float now)
        {
            if (PerfConfig.Quiet)
                return;
            if (!_pathAuditRunning)
            {
                if (now < _nextPathAudit)
                    return;
                _pathAuditWalls.Clear();
                foreach (Segment s in _live.Segments.Values)
                {
                    if (s.FromWallCache && s.Anchor != null)
                        _pathAuditWalls.Add(s);
                }
                if (_pathAuditWalls.Count == 0)
                {
                    _nextPathAudit = now + InsideLogIntervalSeconds;
                    return;
                }
                _pathAuditCursor = 0;
                _pathAuditRunning = true;
                _paName = _paToggle = _paAttach = _paFoliage = 0;
                _paGround = _paFigures = _paStanding = _paGatedOff = 0;
                _paWater = _paArch = _paUnclaimed = _paNoRoom = 0;
                _paUnclaimedByClass.Clear();
                _paUnclaimedNames.Clear();
                _paUnclaimedAllowed.Clear();
            }
            using (PerfMonitor.Scope("WallFade.PathAudit"))
            {
                float frameStart = (float)RescanClock.Elapsed.TotalMilliseconds;
                // HOISTED (ModBuild 281): the budget became a live config dial in this build,
                // and this is the one of its four read sites that sits INSIDE a loop. Reading a
                // ConfigEntry per audited wall would put a dictionary-backed property on a hot
                // path to save nothing; latching it per frame also means one frame's slice
                // cannot be judged against two different budgets, which is the same reason
                // _scheduledRescanInterval exists one screen up.
                float auditBudget = ClassifyBudgetMillis;
                while (_pathAuditCursor < _pathAuditWalls.Count)
                {
                    AuditOneCacheWall(_pathAuditWalls[_pathAuditCursor]);
                    _pathAuditCursor++;
                    if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart
                        >= auditBudget)
                    {
                        return; // resume on the next idle tick — the pass is not finished
                    }
                }
            }
            _pathAuditRunning = false;
            _pathAuditPasses++;
            // ModBuild 284 — THE PASS BACKS OFF WHILE ITS OWN VERDICT IS NOT MOVING, and does so
            // on nothing but its own output. See the CONVERGENCE paragraph on the doc block above.
            int tally = PathAuditTallySignature();
            if (_pathAuditTallyValid && tally == _pathAuditTallySig)
            {
                if (_pathAuditStableRuns < PathAuditMaxBackoffShift)
                    _pathAuditStableRuns++;
            }
            else
            {
                _pathAuditStableRuns = 0;
            }
            _pathAuditTallySig = tally;
            _pathAuditTallyValid = true;
            _pathAuditInterval = Mathf.Min(
                InsideLogIntervalSeconds * (1 << _pathAuditStableRuns),
                PathAuditMaxIntervalSeconds);
            _nextPathAudit = now + _pathAuditInterval;
            LogWallPathAudit();
        }

        /// <summary>Every bucket this pass counted, folded into one int — the audit's OWN
        /// VERDICT and nothing about the scene. Two passes with the same signature classified
        /// the same populations into the same paths, which is the only question this instrument
        /// exists to answer.
        ///
        /// <para>THE THREE UNCLAIMED CLASSES ARE IN IT ON PURPOSE, and they are the reason this
        /// cannot converge on a scene that is actually broken: <c>ClassifyLeftover</c> reads LIVE
        /// blocked/visible sample counts, so a genuinely floating or obstructing renderer moves
        /// this signature as the player moves and holds the pass at its base cadence. A clean
        /// scene has all three at zero and settles. That asymmetry is the design.</para></summary>
        private int PathAuditTallySignature()
        {
            _paUnclaimedByClass.TryGetValue("FLOATING", out int uFloating);
            _paUnclaimedByClass.TryGetValue("OBSTRUCTING", out int uObstructing);
            _paUnclaimedByClass.TryGetValue("ALLOWED", out int uAllowed);
            int h = 17;
            unchecked
            {
                h = h * 31 + _pathAuditWalls.Count;
                h = h * 31 + _paName;
                h = h * 31 + _paToggle;
                h = h * 31 + _paAttach;
                h = h * 31 + _paFoliage;
                h = h * 31 + _paGround;
                h = h * 31 + _paFigures;
                h = h * 31 + _paStanding;
                h = h * 31 + _paGatedOff;
                h = h * 31 + _paWater;
                h = h * 31 + _paArch;
                h = h * 31 + _paNoRoom;
                h = h * 31 + _paUnclaimed;
                h = h * 31 + uFloating;
                h = h * 31 + uObstructing;
                h = h * 31 + uAllowed;
            }
            return h;
        }

        /// <summary>One cache wall's subtree, classified into the audit's buckets. Split out of
        /// <see cref="LogWallPathAudit"/> so the pass can be suspended between walls; a wall is
        /// never split, so no bucket can ever be counted twice.</summary>
        private void AuditOneCacheWall(Segment seg)
        {
            if (seg.Anchor == null)
                return;
            int segToggle = Mathf.Min(seg.ToggleNative, seg.Renderers.Count);
            _paToggle += segToggle;
            _paName += seg.Renderers.Count - segToggle;
            // ModBuild 262 lane F: hold the room verdict, do not encode it as -inf. With no
            // room decision the ceiling below is float.NegativeInfinity, so EVERY ground
            // renderer of such a wall failed the ground-band test and fell through to
            // UNCLAIMED — the alarm then named asset families while the real state was "this
            // wall's room was never anchored". The 2026-08-24 log shows the scale: 171 cache
            // walls of which 127 read FAIL-SAFE solid on the same heartbeat, next to 111
            // UNCLAIMED. The band is UNEVALUABLE for those, not failed, and gets its own bucket.
            bool roomKnown = RoomDecisionValid(seg.RoomIndex);
            float ceiling = roomKnown
                ? _live.RoomFloorY[seg.RoomIndex] + GroundExclusionHeightWU
                : float.NegativeInfinity;
            // The LIST overload, not the array one (ModBuild 262). The old audit ran 3 times in a
            // whole session so an array per wall was free; at the new cadence it would be 171
            // array allocations every 2 s for nothing. The scratch list is a field and is reused.
            seg.Anchor.GetComponentsInChildren(includeInactive: false, _pathAuditScratch);
            foreach (MeshRenderer r in _pathAuditScratch)
            {
                if (r == null || seg.Renderers.Contains(r))
                    continue; // counted above (native/toggle split)
                if (seg.Foliage.Contains(r))
                {
                    _paFoliage++;
                }
                else if (_attachmentOwned.ContainsKey(r))
                {
                    _paAttach++; // body/stacked/mounted/corner — all fade-delivered
                }
                else if (IsModObject(r) || !r.enabled)
                {
                    // not scenery / game-disabled — no path applies, not an alarm
                }
                else if (IsStandingFigureProp(r))
                {
                    // Own bucket on purpose (2026-08-15): "figure-guarded" used to mean
                    // "an adoption sweep declined it" while the wall path could still be
                    // fading it. Anything counted HERE is refused by the wall path too, so
                    // the audit and the delivery can no longer disagree.
                    _paStanding++;
                }
                else if (IsFigureOrActorRenderer(r))
                {
                    _paFigures++;
                }
                else if (roomKnown && r.bounds.max.y <= ceiling)
                {
                    _paGround++;
                }
                // THE TWO STANDING RULINGS, ASKED BEFORE THE ALARM (ModBuild 262 — see the doc on
                // StepWallPathAudit). Both are enforced as spatial protection rects that PULL the
                // renderer back off its wall, which is precisely what left it owned by no list.
                // Asked after the ground band so that band keeps its meaning, and before
                // gated-off/UNCLAIMED so a ruling can never be reported as a defect.
                else if (IsWaterProtected(r.bounds))
                {
                    _paWater++;
                }
                else if (IsArchProtected(r.bounds, r.name))
                {
                    _paArch++;
                }
                else if (HasGatedOffWallFadeToggle(r))
                {
                    _paGatedOff++;
                }
                else if (!roomKnown)
                {
                    // Asked LAST, so the two standing rulings and the authored gate keep
                    // precedence: a water feature on an unanchored room is still WATER.
                    // Everything reaching here would have gone to UNCLAIMED and then to
                    // ClassifyLeftover, whose FIRST test is this same predicate and whose
                    // only possible answer is "UNJUDGED (no anchored floor plane for this
                    // room)" with foot/top/blocked/visible all zero. Nothing is lost by not
                    // calling it, and the headline UNCLAIMED count stops carrying a
                    // population that was never judgeable in the first place.
                    _paNoRoom++;
                }
                else
                {
                    _paUnclaimed++;
                    // THE VERDICT, not just the name (ModBuild 262). 260's name list was capped
                    // at 160 CHARACTERS — six names out of 111 — which is a mode and not a
                    // distribution, the in-repo lesson that has already cost a build. The class
                    // tally below is complete and untruncated.
                    string cls = ClassifyLeftover(r, seg.RoomIndex, out int blocked,
                                                  out float foot, out float top,
                                                  out int visible);
                    _paUnclaimedByClass.TryGetValue(cls, out int seen);
                    _paUnclaimedByClass[cls] = seen + 1;
                    List<string> into =
                        cls == "ALLOWED" ? _paUnclaimedAllowed : _paUnclaimedNames;
                    if (into.Count >= MountedLeftoverCap)
                        continue;
                    string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    into.Add(
                        (cls == "ALLOWED" ? string.Empty : $"[{cls}] ")
                        + $"'{r.name}' under '{wall}' (room {seg.RoomIndex}, fade "
                        + $"{seg.Fade:F2}): foot {foot:F2} wu / top {top:F2} wu over the room "
                        // No [EXEMPT] tag here on purpose: the water and arch rulings are asked
                        // in the chain ABOVE this branch, so nothing that reaches UNCLAIMED can
                        // be carrying one.
                        + $"floor, hides {blocked} of {visible} in-view playable-tile sample(s)");
                }
            }
        }

        private void LogWallPathAudit()
        {
            _paUnclaimedByClass.TryGetValue("FLOATING", out int uFloating);
            _paUnclaimedByClass.TryGetValue("OBSTRUCTING", out int uObstructing);
            _paUnclaimedByClass.TryGetValue("ALLOWED", out int uAllowed);
            int uOther = _paUnclaimed - uFloating - uObstructing - uAllowed;
            // ModBuild 262 lane F: the pass guard above returns when there are no cache WALLS,
            // never when the walls hold no RENDERERS — and a wall cache that registers before
            // the tileset's meshes exist reached the all-clear text with every bucket at zero.
            // Both hardware sessions printed exactly that: "32 cache wall(s) … 0 UNCLAIMED —
            // every wall renderer is owned by a path" beside a heartbeat reading "fade-capable
            // renderers 0 = 0 claimed + 0 adopted" (LogOutput.log:956/:958 and
            // second_logs/LogOutput.log:923). A vacuous all-clear is worse than no line at all,
            // because this is the line a reader greps to decide a new tileset is covered.
            int classified = _paName + _paToggle + _paAttach + _paFoliage + _paGround
                + _paStanding + _paFigures + _paWater + _paArch + _paGatedOff + _paNoRoom
                + _paUnclaimed;
            VRLog.Info(Name,
                $"WALL-PATH AUDIT scene='{SceneManager.GetActiveScene().name}' "
                + $"{TilesetLabel()}: "
                + $"{_pathAuditWalls.Count} cache wall(s) — renderers: {_paName} native-name + "
                + $"{_paToggle} toggle-native (MPB fade), {_paAttach} attachment-claimed "
                + $"(body/stacked/mounted/corner), {_paFoliage} foliage, {_paGround} ground-band "
                + $"(solid by design), {_paStanding} standing-prop (floor-standing figure/actor "
                + "prop — never wall geometry on ANY path, WallSegmentFade.Standing.cs), "
                + $"{_paFigures} figure-guarded, {_paWater} WATER FEATURE (user ruling "
                + "2026-08-09 — pulled off its wall on purpose; before ModBuild 262 every one of "
                + "these was counted UNCLAIMED [ALARM] BY CONSTRUCTION, which is where the "
                + "Brunnen he expressly allows had been sitting), "
                + $"{_paArch} doorway arch (user ruling 2026-08-02 — same story), "
                + $"{_paGatedOff} gated-off (authored always-solid — honored), "
                + $"{_paNoRoom} no-room-decision (their wall's room is unanchored or has no "
                + "sample grid this pass, so the ground band is UNEVALUABLE and the class is "
                + "unknown — before ModBuild 262 lane F every one of these was counted as "
                + "UNCLAIMED and then classed UNJUDGED, which is why the alarm named asset "
                + "families while the real state was an unanchored room; compare the "
                + "heartbeat's FAIL-SAFE solid count on the same tick), "
                + $"{_paUnclaimed} UNCLAIMED"
                + (_paUnclaimed > 0
                    ? " [fell through every path — BY THE USER'S THREE CLASSES: "
                      + $"{uFloating} FLOATING, {uObstructing} OBSTRUCTING, {uAllowed} ALLOWED"
                      + (uOther > 0
                          ? $", {uOther} UNJUDGED (an input was missing — no playable-tile "
                            + "grid for the room, or no sample of it in view this tick; the "
                            + "third reason, no anchored floor plane, is now its own "
                            + "no-room-decision bucket above and can no longer appear here; "
                            + "never read as ALLOWED)"
                          : string.Empty)
                      + ". ONLY FLOATING and OBSTRUCTING are the alarm (ruling 2026-08-24) — an "
                      + "UNCLAIMED renderer standing on the floor and hiding nothing is a thing "
                      + "he says may stay, and 154/111 were quoted as defect counts for two "
                      + $"rounds without this split. Defects (up to {MountedLeftoverCap}): "
                      + string.Join("; ", _paUnclaimedNames)
                      + $" | ALLOWED (up to {MountedLeftoverCap}): "
                      + string.Join("; ", _paUnclaimedAllowed)
                      + "]"
                    : classified == 0
                        ? " — NOTHING MEASURED. The cache walls hold ZERO child renderers this "
                          + "pass, so this line has classified nothing: it is NOT an all-clear "
                          + "and must not be read as one. Wait for a pass whose fade-capable "
                          + "renderer count is non-zero and read THAT audit."
                        : $" — all {classified} classified wall renderer(s) are owned by a "
                          + "path.")
                + $" Pass {_pathAuditPasses} of this session, sliced at "
                + $"{ClassifyBudgetMillis:0.0} ms/frame. NEXT PASS IN {_pathAuditInterval:0.0} s"
                + (_pathAuditStableRuns > 0
                    ? $" — this verdict has now come out IDENTICAL {_pathAuditStableRuns + 1} "
                      + $"passes running, so the cadence has backed off from "
                      + $"{InsideLogIntervalSeconds:0.0} s (ceiling "
                      + $"{PathAuditMaxIntervalSeconds:0.0} s). A LONG interval here means "
                      + "CONVERGED, not stopped; any bucket moving puts it straight back to "
                      + $"{InsideLogIntervalSeconds:0.0} s, and a scenario change resets it."
                    : $" — the base cadence, because this pass's buckets DIFFER from the "
                      + "previous one's (or it is the first pass of the scenario).")
                + " See the WallFade.PathAudit [Perf] scope for what this cost.");
        }

        // ---- WALL-PATH AUDIT slicing state ---------------------------------------------------
        /// <summary>Cache walls of the pass in flight — snapshotted at pass start so a segment
        /// table that churns mid-pass cannot double-count or skip a wall.</summary>
        private readonly List<Segment> _pathAuditWalls = new();
        private int _pathAuditCursor;
        private bool _pathAuditRunning;
        private float _nextPathAudit;
        private int _pathAuditPasses;
        /// <summary>The previous pass's bucket tally (see <see cref="PathAuditTallySignature"/>),
        /// and how many consecutive passes have now agreed with it.</summary>
        private int _pathAuditTallySig;
        private bool _pathAuditTallyValid;
        private int _pathAuditStableRuns;
        /// <summary>The interval the pass that just finished scheduled the next one with. Printed
        /// on the line, because a line that quotes a CONSTANT cadence while running on a backed-off
        /// one is an instrument describing a build it is not part of.</summary>
        private float _pathAuditInterval = InsideLogIntervalSeconds;
        /// <summary>Backoff ceiling as a shift of <see cref="InsideLogIntervalSeconds"/>: 2 s
        /// doubling to 4, 8, 16, 32, 64 — clamped by
        /// <see cref="PathAuditMaxIntervalSeconds"/>.</summary>
        private const int PathAuditMaxBackoffShift = 5;
        /// <summary>Hard ceiling on the backed-off cadence. A converged audit still re-measures
        /// once a minute, so a defect that appears in a scene nothing else disturbs is still
        /// found — it is a backoff, never a latch.</summary>
        private const float PathAuditMaxIntervalSeconds = 60f;
        private int _paName, _paToggle, _paAttach, _paFoliage;
        private int _paGround, _paFigures, _paStanding, _paGatedOff;
        private int _paWater, _paArch, _paUnclaimed;
        /// <summary>Renderers whose wall's room has NO valid decision this pass, so the ground
        /// band cannot be evaluated at all. See the branch in <see cref="AuditOneCacheWall"/>.
        /// </summary>
        private int _paNoRoom;
        private readonly Dictionary<string, int> _paUnclaimedByClass = new();
        private readonly List<string> _paUnclaimedNames = new();
        private readonly List<string> _paUnclaimedAllowed = new();
        /// <summary>Reused subtree buffer — see the note at the GetComponentsInChildren call.
        /// </summary>
        private readonly List<MeshRenderer> _pathAuditScratch = new();

        /// <summary>Toggle-native materials already logged (round 8, cap 6): one line per
        /// material with its authored gate/cutoff values and shader keywords — the datum
        /// that adjudicates the keyword risk (see the Apply comment) from the next log.</summary>
        private readonly HashSet<string> _loggedToggleMats = new();

        private void LogToggleNativeMaterialOnce(Material m)
        {
            if (_loggedToggleMats.Count >= 6
                || !_loggedToggleMats.Add(m.shader.name + "/" + m.name))
                return;
            string wallFadeOn = m.HasProperty(WallFadeOnMatId)
                ? m.GetFloat(WallFadeOnMatId).ToString("0.##") : "n/a";
            string toggleWallfade = m.HasProperty(ToggleWallfadeMatId)
                ? m.GetFloat(ToggleWallfadeMatId).ToString("0.##") : "n/a";
            string cutoff = m.HasProperty(CutoffId)
                ? m.GetFloat(CutoffId).ToString("0.##") : "n/a";
            string local = m.HasProperty(ToggleWallFadeLocalMatId)
                ? m.GetFloat(ToggleWallFadeLocalMatId).ToString("0.##") : "n/a";
            string keywords;
            try { keywords = string.Join(",", m.shaderKeywords); }
            catch { keywords = "?"; }
            VRLog.Info(Name,
                $"TOGGLE-NATIVE MATERIAL '{m.name}' (shader '{m.shader.name}'): authored "
                + $"_WallFade_On={wallFadeOn}, _ToggleWallfade={toggleWallfade}, "
                + $"_ToggleWallFadeLocal={local}, _Cutoff={cutoff}, keywords=[{keywords}] — "
                + "native MPB fade path engaged (round 8; round-9 liveness rules: keyword-off "
                + "_WallFade_On variants and _ToggleWallFadeLocal opt-outs are NOT routed "
                + "here).");
        }

        /// <summary>
        /// Bind every wall segment to the ONE room whose AABB it borders: smallest XZ gap
        /// between wall AABB and room AABB (a wall bordering its room touches it → gap 0;
        /// Y is ignored — room-bounds Y is the untrusted proxy axis). Near-ties (a door
        /// wall between two rooms) go to the room whose center is nearer to the wall.
        ///
        /// ROUND-6 ADJACENT RE-ANCHOR: when the nearest room is NOT decision-valid
        /// (unanchored / no grid — the keep log: 23 perimeter walls stuck fail-safe on
        /// never-anchoring neighbor entries) but the wall PHYSICALLY borders an anchored
        /// logical room (gap ≤ <see cref="AdjacentReanchorMaxGapWU"/>), the wall is
        /// assigned to THAT room. This is still strict own-room accounting — the wall is
        /// simply bound to the room it actually encloses; an unanchorable sliver between
        /// the wall and the real room no longer steals the assignment. Walls bordering NO
        /// anchored room keep the fail-safe (solid).
        /// </summary>
        private void AssociateRooms()
        {
            _reanchorCensus.Clear();
            _seamCensus.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.RoomIndex = -1;
                seg.BorderRooms.Clear();
                if (!seg.HasBounds)
                    continue;
                Bounds w = seg.Bounds;
                float bestGap = float.PositiveInfinity;
                float bestCenter = float.PositiveInfinity;
                for (int r = 0; r < _live.RoomBounds.Count; r++)
                {
                    Bounds room = _live.RoomBounds[r];
                    float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                    float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                    float gap = gx * gx + gz * gz;
                    float cx = room.center.x - w.center.x;
                    float cz = room.center.z - w.center.z;
                    float center = cx * cx + cz * cz;
                    if (gap < bestGap - 0.0001f
                        || (gap <= bestGap + 0.0001f && center < bestCenter))
                    {
                        bestGap = gap;
                        bestCenter = center;
                        seg.RoomIndex = r;
                    }
                }

                if (seg.RoomIndex >= 0 && !RoomDecisionValid(seg.RoomIndex))
                {
                    // Adjacent re-anchor (see the method doc): nearest DECISION-VALID room
                    // the wall actually borders, if any.
                    float maxGapSq = AdjacentReanchorMaxGapWU * AdjacentReanchorMaxGapWU;
                    float altGap = float.PositiveInfinity;
                    float altCenter = float.PositiveInfinity;
                    int alt = -1;
                    for (int r = 0; r < _live.RoomBounds.Count; r++)
                    {
                        if (!RoomDecisionValid(r))
                            continue;
                        Bounds room = _live.RoomBounds[r];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                        float gap = gx * gx + gz * gz;
                        if (gap > maxGapSq)
                            continue;
                        float cx = room.center.x - w.center.x;
                        float cz = room.center.z - w.center.z;
                        float center = cx * cx + cz * cz;
                        if (gap < altGap - 0.0001f
                            || (gap <= altGap + 0.0001f && center < altCenter))
                        {
                            altGap = gap;
                            altCenter = center;
                            alt = r;
                        }
                    }
                    if (alt >= 0)
                    {
                        seg.RoomIndex = alt;
                        if (_reanchorCensus.Count < 12)
                        {
                            string n = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                            _reanchorCensus.Add($"'{n}' gap {Mathf.Sqrt(altGap):F1}");
                        }
                    }
                }

                // ---- ROOM SEAM (user report 2026-08-09: one wall piece faded INVERTED) ------
                // The pick above is "nearest room box, ties by nearest room centre". For a wall
                // standing in the SEAM between two rooms that is a coin flip decided by tenths
                // of a wu: the hardware log's two rooms are map tiles 'E' (x 17.1..25.9,
                // z -4.1..4.1) and 'LL' (x 9.3..27.0, z 4.7..15.9) — their boxes are 0.65 wu
                // apart, while a masonry slab here is 0.8..1.8 wu THICK. Every partition
                // therefore overlaps or nearly overlaps BOTH boxes and the winner is decided by
                // which side the slab happens to lean.
                //
                // That coin flip is the whole bug, because the coverage metric is what carries
                // the side: a wall fades when it hides its OWN room's floor from the head, which
                // is "outside-in" by construction — and measuring a seam wall against the room
                // on the WRONG side inverts it exactly as reported ("von außen faded es nicht,
                // aber von innen"). Standing in room 2 you are outside room 1, the wall hides
                // room 1's floor, so it fades; standing outside room 2 it hides nothing of room
                // 1, so it stays. Every other wall in the level borders one room and behaves.
                //
                // The fix does not try to guess the coin flip right — it removes the flip. A
                // seam wall genuinely belongs to BOTH rooms it separates: from either side it is
                // the thing hiding the room you are looking into, and the standing invariant
                // ("Fading geht immer darum den Raum freizulegen von außen nach innen") holds
                // for both. So the wall records every decision-valid room it BORDERS and
                // BlockedFraction takes the max over them. This is NOT the retired round-3
                // cross-room MAX, which maxed over ALL rooms including ones the wall stood far
                // away from; the band is one wall thickness, so a wall that borders exactly one
                // room — the overwhelming majority — is bit-for-bit unchanged.
                //
                // MULTIPLAYER: nothing to send. Which wall a head occludes is a per-player fact
                // by definition, and the opt-in peer sync (wire record 17) already carries the
                // RESULT — this only changes how the LOCAL decision is computed, from the same
                // replicated room geometry on every machine, so the wire format, the key
                // derivation and the record are untouched.
                if (seg.RoomIndex >= 0)
                {
                    for (int r = 0; r < _live.RoomBounds.Count; r++)
                    {
                        if (r == seg.RoomIndex || !RoomDecisionValid(r))
                            continue;
                        Bounds room = _live.RoomBounds[r];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                        if (gx * gx + gz * gz > RoomBorderBandWU * RoomBorderBandWU)
                            continue;
                        seg.BorderRooms.Add(r);
                    }
                    if (seg.BorderRooms.Count > 0 && _seamCensus.Count < 10)
                    {
                        string n = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                        _seamCensus.Add($"'{n}' r{seg.RoomIndex}+{string.Join("+", seg.BorderRooms)}");
                    }
                }
            }
            if (_seamCensus.Count != _lastLoggedSeamCount)
            {
                _lastLoggedSeamCount = _seamCensus.Count;
                if (_seamCensus.Count > 0)
                    VRLog.Info(Name,
                        $"ROOM SEAM: {_seamCensus.Count} wall(s) stand between TWO rooms "
                        + $"(both room boxes within {RoomBorderBandWU:0.00} wu = one wall "
                        + "thickness) and are judged against EACH of them, max coverage wins — "
                        + "the 2026-08-09 inverted-wall report: a seam wall hides whichever room "
                        + "you are NOT in, so it must fade from either side. Non-seam walls keep "
                        + $"strict own-room accounting unchanged: {string.Join(", ", _seamCensus)}.");
            }
            if (_reanchorCensus.Count != _lastLoggedReanchorCount)
            {
                _lastLoggedReanchorCount = _reanchorCensus.Count;
                if (_reanchorCensus.Count > 0)
                    VRLog.Info(Name,
                        $"ADJACENT RE-ANCHOR: {_reanchorCensus.Count} wall(s) bound to the "
                        + $"anchored room they border (reach ≤{AdjacentReanchorMaxGapWU:0.0} wu "
                        + $"— round 11: gate towers protrude on the rock base): "
                        + $"{string.Join(", ", _reanchorCensus)}.");
            }
        }

        /// <summary>
        /// Precompute the FLOOR occlusion samples: a per-room XZ grid (footprint from the
        /// room renderer bounds — XZ is the trusted axis) placed ON the tile-anchored
        /// floor plane (<see cref="CommittedTable.RoomFloorY"/> + <see cref="FloorSampleEpsilon"/>), as
        /// dense as the room budget allows under <see cref="MaxTotalSamples"/>
        /// (4×4 → 3×3 → 2×2 → center per room). Samples are room-contiguous; each room's
        /// [start,count) range doubles as the coverage-fraction denominator.
        ///
        /// <para>ModBuild 258 — THE DENOMINATOR IS THE PLAYABLE TILES. The lattice above is
        /// still what spreads the samples over the room, but a lattice position is no longer
        /// a sample: each one is MOVED to the nearest playable hex centre that no other
        /// lattice position has claimed (<see cref="CollectPlayableTiles"/> for the hex source
        /// and for the four-corner evidence that made this necessary). Every sample therefore
        /// stands on floor the player can stand on, which is precisely the user's own wording:
        /// <i>"wenn man aber die wand gegenüber einguckt DIE NICHTS VERDECKT von den
        /// spielbaren tiles sollte sie direkt unfaden"</i>.
        ///
        /// <para>WHY RELOCATE RATHER THAN DROP. Dropping the off-tile positions would shrink
        /// the denominator — 16 cells becoming 11 raises the quantum from 0.0625 to 0.0909 and
        /// moves BOTH Schmitt bars in cell terms, so the same change would need the bars
        /// re-derived in the same build. It would also need a point-in-hex radius, i.e. a
        /// threshold, and this subsystem has already shipped two thresholds that the next
        /// hardware log falsified. Greedy nearest-unclaimed relocation needs no threshold at
        /// all and keeps the count at exactly <c>min(grid², hexes)</c>: for every room in the
        /// ModBuild 257 log that is 16, so the quantum, the exit bar (4 cells) and the enter
        /// bar (6 cells) are bit-for-bit what they were. One variable moved this round.</para>
        ///
        /// <para>WHAT WOULD FALSIFY IT. The census line below prints each room's hex count. A
        /// room reporting FEWER than <c>grid²</c> hexes has a denominator smaller than 16 and
        /// its bars no longer sit at 4/6 cells — that is the number to check before blaming
        /// anything else. A room reporting <c>FELL BACK TO THE BOUNDING BOX</c> is running
        /// ModBuild 257 behaviour exactly and cannot have been fixed or broken by this
        /// change.</para>
        ///
        /// <para>ModBuild 259 — THE FILTER, AND THE ONE CASE THAT HOLDS IT BACK. The hex set is
        /// now narrowed to the hexes <see cref="ClassifyHex"/> calls playable, because ModBuild
        /// 258's "playable" only meant "a TileBehaviour keyed to this CMap inside this room's
        /// footprint" and the room's own EDGE hexes passed that. The narrowing is applied ONLY
        /// while at least <c>grid²</c> playable hexes survive. Below that the relocation would
        /// start dropping lattice positions, <c>min(grid², hexes)</c> would fall, the quantum
        /// would rise and BOTH Schmitt bars would move in cell terms — a bar re-derivation this
        /// build is not allowed to make silently. So a room with too few playable hexes keeps
        /// ModBuild 258's unfiltered in-footprint set, bit for bit, and the census says
        /// PLAYABLE FILTER HELD BACK with both counts and the bars the filtered set WOULD have
        /// produced. That is the fallback ladder constraint 1 asks for: playable hexes →
        /// in-footprint hexes (258) → bounding box (257).</para>
        ///
        /// <para>WHY THE FILTER CANNOT MOVE 'Wall 4' THE WRONG WAY, in the log's own numbers.
        /// 'Wall 4' is the wall the user confirmed fixed. Its ModBuild 258 readings are
        /// <c>blk 1/16 solid</c> ×20, <c>blk 2/16 solid</c> ×8 and <c>blk 3/16 solid</c> ×6
        /// (0.19, under the 0.20 exit bar) when it should be solid, and <c>blk 16/16</c> when it
        /// should be faded. Removing hexes can only push a lattice position to a hex FURTHER
        /// from the room's edge, i.e. further from the wall that was intercepting it, so a
        /// wall's pinned-cell count can only fall or stay — its stuck-faded failure mode moves
        /// away, not toward. And its fade-side margin is ten cells (16/16 against a 6-cell enter
        /// bar), so no rearrangement of 16 samples inside the same room can cost it the fade.
        /// The denominator itself is unchanged either way: 16 in the filtered branch by the
        /// <c>grid²</c> guard above, 16 in the held-back branch by definition.</para>
        ///
        /// <para>COST. One <c>grid² × hexes</c> distance scan per room per rescan (~2 s):
        /// 16 × ~50 × ~6 rooms ≈ 5k squared-distance tests, all at build time. The per-wall,
        /// per-sample ray loop is untouched — it still walks <c>_live.RoomSampleCount[room]</c>
        /// entries of <see cref="CommittedTable.AllSamples"/> and never learns where they came from.</para>
        /// </summary>
        private void RebuildSamples()
        {
            _live.AllSamples.Clear();
            _live.RoomSampleStart.Clear();
            _live.RoomSampleCount.Clear();
            _roomTileCount.Clear();
            _roomTileTotal.Clear();
            _roomSnapMax.Clear();
            _roomSnapSum.Clear();
            _roomTileGrid.Clear();
            _roomTileFootprint.Clear();
            _roomTilePlayable.Clear();
            _roomPlayableUsed.Clear();
            _roomCutEdge.Clear();
            _roomCutBlockedFlag.Clear();
            _roomCutNodeBlocked.Clear();
            _roomCutNotWalkable.Clear();
            _roomTileUnreadable.Clear();
            _live.SampleYMin = float.PositiveInfinity;
            _live.SampleYMax = float.NegativeInfinity;
            int rooms = _live.RoomBounds.Count;
            if (rooms == 0)
            {
                _live.SampleYMin = _live.SampleYMax = 0f;
                _sampleGridCells = 0;
                LogSampleGridCensus();
                return;
            }
            int grid = rooms * 16 <= MaxTotalSamples ? 4
                : rooms * 9 <= MaxTotalSamples ? 3
                : rooms * 4 <= MaxTotalSamples ? 2
                : 1;
            _sampleGridCells = grid * grid;
            for (int r = 0; r < rooms; r++)
            {
                _live.RoomSampleStart.Add(_live.AllSamples.Count);
                // Diag rows, added for EVERY room including the ones that bail below, so their
                // index stays the room index.
                _roomTileCount.Add(0);
                _roomTileTotal.Add(0);
                _roomSnapMax.Add(0f);
                _roomSnapSum.Add(0f);
                _roomTileGrid.Add(false);
                _roomTileFootprint.Add(0);
                _roomTilePlayable.Add(0);
                _roomPlayableUsed.Add(false);
                _roomCutEdge.Add(0);
                _roomCutBlockedFlag.Add(0);
                _roomCutNodeBlocked.Add(0);
                _roomCutNotWalkable.Add(0);
                _roomTileUnreadable.Add(0);
                if (_live.AllSamples.Count + grid * grid > MaxTotalSamples)
                {
                    _live.RoomSampleCount.Add(0); // over budget — room gets no grid this rescan
                    continue;
                }
                Bounds b = _live.RoomBounds[r];
                float y = _live.RoomFloorY[r] + FloorSampleEpsilon;
                if (y < _live.SampleYMin) _live.SampleYMin = y;
                if (y > _live.SampleYMax) _live.SampleYMax = y;

                // This room's playable hexes, by the game's own room object. Absent → the
                // ModBuild 257 bounding-box grid, unchanged, and the census says so in words.
                object? mapKey = r < _roomMapKeys.Count ? _roomMapKeys[r] : null;
                List<Vector3>? hexes = null;
                if (mapKey != null && _tilesByMap.TryGetValue(mapKey, out List<Vector3> found)
                    && found.Count > 0)
                {
                    _roomTileTotal[r] = found.Count;
                    // KEEP ONLY THE HEXES IN THIS ROOM'S OWN FOOTPRINT. One CMap can be TWO
                    // logical rooms here — CommitRoomRegistry keys them by (CMap, anchor height)
                    // precisely because a terraced room must not share one sample plane — and
                    // both would otherwise be handed the CMap's whole hex list, so the upper
                    // level could snap its samples onto the lower level's tiles in XZ. The
                    // footprint is the same box the lattice is drawn over, so a hex outside it
                    // is one the lattice never reached anyway. Not a tuning: no margin, no
                    // radius, just the room's own bounds.
                    _tileScratch.Clear();
                    _tilePlayScratch.Clear();
                    // ModBuild 259: same walk, and the playability verdict CollectPlayableTiles
                    // already computed for each hex is tallied by REASON here — so the census
                    // says which term removed the hexes, or that none did.
                    _tileWhyByMap.TryGetValue(mapKey, out List<byte> why);
                    for (int t = 0; t < found.Count; t++)
                    {
                        Vector3 h = found[t];
                        if (h.x < b.min.x || h.x > b.max.x || h.z < b.min.z || h.z > b.max.z)
                            continue;
                        _tileScratch.Add(h);
                        // No verdict list (registry raced the rescan) → treat every hex as
                        // playable, which is exactly ModBuild 258's set. Fail OPEN, never toward
                        // a smaller denominator.
                        byte code = why != null && t < why.Count ? why[t] : HexUnreadable;
                        switch (code)
                        {
                            case HexEdgeFlag: _roomCutEdge[r]++; break;
                            case HexBlockedFlag: _roomCutBlockedFlag[r]++; break;
                            case HexNodeBlocked: _roomCutNodeBlocked[r]++; break;
                            case HexNotWalkable: _roomCutNotWalkable[r]++; break;
                            case HexUnreadable:
                                _roomTileUnreadable[r]++;
                                _tilePlayScratch.Add(h);
                                break;
                            default:
                                _tilePlayScratch.Add(h);
                                break;
                        }
                    }
                    _roomTileFootprint[r] = _tileScratch.Count;
                    _roomTilePlayable[r] = _tilePlayScratch.Count;
                    // THE GUARD (constraint 1 + 3). Use the filtered set only while it still
                    // carries at least one hex per lattice position: below grid² the relocation
                    // drops positions, min(grid², hexes) falls, the quantum rises and both
                    // Schmitt bars move in cell terms. A held-back room keeps ModBuild 258's
                    // denominator unchanged and the census prints both counts.
                    if (_tilePlayScratch.Count >= grid * grid)
                    {
                        hexes = _tilePlayScratch;
                        _roomPlayableUsed[r] = true;
                    }
                    else if (_tileScratch.Count > 0)
                    {
                        hexes = _tileScratch;
                    }
                }
                _roomTileCount[r] = hexes != null ? hexes.Count : 0;
                if (hexes == null)
                {
                    for (int ix = 0; ix < grid; ix++)
                    {
                        float bx = Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / grid);
                        for (int iz = 0; iz < grid; iz++)
                        {
                            float bz = Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / grid);
                            _live.AllSamples.Add(new Vector3(bx, y, bz));
                        }
                    }
                    _live.RoomSampleCount.Add(grid * grid);
                    continue;
                }

                _roomTileGrid[r] = true;
                _tileTaken.Clear();
                for (int t = 0; t < hexes.Count; t++)
                    _tileTaken.Add(false);
                int placed = 0;
                float worst = 0f, moved = 0f;
                for (int ix = 0; ix < grid; ix++)
                {
                    float x = Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / grid);
                    for (int iz = 0; iz < grid; iz++)
                    {
                        float z = Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / grid);
                        int best = -1;
                        float bestSq = float.PositiveInfinity;
                        for (int t = 0; t < hexes.Count; t++)
                        {
                            if (_tileTaken[t])
                                continue;
                            float dx = hexes[t].x - x, dz = hexes[t].z - z;
                            float sq = dx * dx + dz * dz;
                            if (sq >= bestSq)
                                continue;
                            bestSq = sq;
                            best = t;
                        }
                        if (best < 0)
                            continue; // fewer hexes than lattice cells — every hex is already a sample
                        _tileTaken[best] = true;
                        // The hex gives XZ; Y stays the room's tile-anchored sample plane, so the
                        // round-6 frame guarantee (and the !ABOVE-WALL tripwire built on it) is
                        // exactly as it was.
                        _live.AllSamples.Add(new Vector3(hexes[best].x, y, hexes[best].z));
                        placed++;
                        float d = Mathf.Sqrt(bestSq);
                        moved += d;
                        if (d > worst) worst = d;
                    }
                }
                _roomSnapMax[r] = worst;
                _roomSnapSum[r] = moved;
                _live.RoomSampleCount.Add(placed);
            }
            if (float.IsInfinity(_live.SampleYMin))
                _live.SampleYMin = _live.SampleYMax = 0f;
            LogSampleGridCensus();
        }

        /// <summary>
        /// THE PROOF LINE FOR ModBuild 258. Per room: how many playable hexes the game's own
        /// registry gave us, how many of the <c>grid²</c> lattice positions became samples,
        /// how far they had to move to reach a tile, the resulting quantum, and where the two
        /// live Schmitt bars land IN CELLS — the arithmetic that latched 'Wall 2' and 'Wall 4'
        /// in ModBuild 257 (4 cells = 0.25, above the 0.20 exit bar and below the 0.35 enter
        /// bar, so the same reading appeared in BOTH states).
        ///
        /// <para>The max-move column is the direct measure of how wrong the bounding box was:
        /// a room whose worst lattice position sits more than a hex pitch (~1.72 wu) from any
        /// tile had lattice cells in dead space, which is the defect. A room reporting FELL
        /// BACK TO THE BOUNDING BOX is running ModBuild 257 unchanged.</para>
        ///
        /// <para>ModBuild 259 adds the FUNNEL and the per-term cut columns: CMap hexes → hexes
        /// inside this room's own footprint → PLAYABLE hexes, with one column per term of
        /// <see cref="ClassifyHex"/>. The whole point of the columns is that they can read all
        /// zero: if no term cut anything, the playability filter is inert for this room and the
        /// next round must not spend a build on it. A room printing PLAYABLE FILTER HELD BACK
        /// kept ModBuild 258's denominator and prints the two bars the filtered set WOULD have
        /// produced — the numbers to read before anyone shrinks a denominator.</para>
        ///
        /// <para>Change-gated on its own text: the rescan runs every 2 s and this would
        /// otherwise be the noisiest line in the log. It reprints the moment any number in it
        /// moves — including a room revealing, which is exactly when it is wanted.</para>
        /// </summary>
        /// <summary>The room's own <c>CMap.Revealed</c>, as text. REPORTED ONLY — see
        /// <see cref="ClassifyHex"/> for why reveal cannot be a per-hex term. It is here so the
        /// next hardware log can falsify the claim that an unrevealed room never reaches the
        /// registry, rather than leaving that claim to inference (user, twice: "dahinter sind
        /// bisher nicht entdeckte tiles … das soll aber nicht der Fall sein"). A room printing
        /// <c>Revealed=NO</c> while it holds a sample grid is the finding.</summary>
        private string RoomRevealedLabel(int r)
        {
            if (r >= _roomMapKeys.Count)
                return "?";
            try
            {
                return _roomMapKeys[r] is ScenarioRuleLibrary.CMap map
                    ? (map.Revealed ? "yes" : "NO")
                    : "?";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// TILESET IDENTITY (ModBuild 262 lane F — measurement only). EVERY scenario in the game
        /// loads into ONE additively-loaded scene named "ProcGen"
        /// (<c>Choreographer.c_ProcGenSceneName</c> :434, <c>LoadProcGenScene</c> :14722-14728),
        /// so <c>scene='ProcGen'</c> — the only identity either census line has ever carried —
        /// cannot say WHICH tileset ran. That is precisely the user's question: <i>"wende das
        /// mit ALLEN Mauer-Assets aus ALLEN Levels genauso an"</i>. No hardware log to date can
        /// be read as evidence about a second tileset, because no log ever named the first one.
        ///
        /// <para>The game's own answer is one hop from data this subsystem already holds:
        /// <c>CMap.SelectedPossibleRoom</c> (CMap.cs:41) carries <c>EBiome</c> and
        /// <c>ESubBiome</c> (ScenarioPossibleRoom.cs:16-25 and :28-68), and the CMap per LOGICAL
        /// room is already in <see cref="_roomMapKeys"/>. Read-only, presentation-only, local
        /// scene state — nothing here writes game state and nothing here is networked, so it is
        /// multiplayer-compatible by construction.</para>
        ///
        /// <para>HOW TO READ IT: the distribution names biome/sub-biome and how many DISTINCT
        /// CMaps carry each, over the union of the CMaps the tile registry knows and the CMaps
        /// the logical-room registry knows — the union, because a room whose volume renderer
        /// never resolved a CMap is invisible to <see cref="_roomMapKeys"/> while its tiles are
        /// still keyed in <see cref="_tilesByMap"/>. THE ONE-LINE READING THAT MEANS "NOT
        /// GENERIC" is a distribution that is one single biome in every hardware log there is:
        /// the subsystem would then still have been measured on exactly one tileset, whatever
        /// the wall counts say. The trailing "with no CMap of their own" count is the registry
        /// entries whose volume renderer never resolved one — they cannot be identified here
        /// and cannot be sampled either, see REGISTRY COVERAGE on the SAMPLE GRID line.</para>
        ///
        /// <para>COST: one walk of the tile registry's CMap keys plus the logical-room list —
        /// both single- to low-double-digit — at the existing census cadence and the existing
        /// path-audit cadence. Never per frame, never a new scene sweep, no allocation beyond
        /// the returned string (the histogram and the dedup set are reused fields).</para>
        /// </summary>
        private string TilesetLabel()
        {
            _censusBiomes.Clear();
            _censusMapsSeen.Clear();
            int noMap = 0;
            foreach (object key in _tilesByMap.Keys)
                AccumulateBiome(key);
            for (int r = 0; r < _roomMapKeys.Count; r++)
            {
                object? key = _roomMapKeys[r];
                if (key == null)
                    noMap++;
                else
                    AccumulateBiome(key);
            }
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in _censusBiomes)
            {
                if (sb.Length > 0)
                    sb.Append(" + ");
                sb.Append(kv.Key).Append(" x").Append(kv.Value);
            }
            if (sb.Length == 0)
                sb.Append("no CMap identified yet");
            return $"AUTHORED ROOM TEMPLATE (level-editor field, NOT the built tileset — ModBuild 263 printed Crypt/Catacombs for a confirmed FOREST session; verify against the PCG_ prefixes in this same log) {sb} over {_censusMapsSeen.Count} distinct CMap(s); "
                + $"{_roomMapKeys.Count} logical room(s) registered, {noMap} of them with no "
                + "CMap of their own";
        }

        /// <summary>One CMap into the biome histogram, deduplicated by reference. Every read is
        /// wrapped: the possible-room chain is built by the game and can be mid-build.</summary>
        private void AccumulateBiome(object key)
        {
            if (!_censusMapsSeen.Add(key))
                return;
            string label;
            try
            {
                if (key is not ScenarioRuleLibrary.CMap map)
                {
                    label = "not-a-CMap";
                }
                else
                {
                    // ModBuild 264 — THIS IS THE AUTHORED TEMPLATE, NOT THE BUILT TILESET, and
                    // it has already been observed contradicting the geometry in the same log.
                    // ModBuild 263 printed 'Crypt/Catacombs x5' for a session the user confirmed
                    // was the FOREST map he has been testing all along, and the same log carries
                    // 800 PCG_FR_ instantiations against 94 PCG_CR_ and 53 PCG_CV_. CMap
                    // .SelectedPossibleRoom is the LEVEL EDITOR's selection (its only other
                    // reader in the game is LevelEditorController.cs:406-411) and ESubBiome
                    // .Catacombs is marked [Obsolete] — so this is a design-time field, and a
                    // design-time field describing what a room COULD be is not a measurement of
                    // what was built. Labelled as such rather than deleted, because the two
                    // disagreeing is itself the finding.
                    // FALSIFIER, and the number to trust instead: the PCG_ prefix distribution of
                    // what was actually instantiated. Until that is accumulated here, read it out
                    // of the log directly — grep -o "PCG_[A-Z][A-Z]_" | sort | uniq -c.
                    var room = map.SelectedPossibleRoom;
                    label = room == null
                        ? "CMap-without-possible-room"
                        : room.Biome.ToString() + "/" + room.SubBiome.ToString();
                }
            }
            catch
            {
                label = "unreadable";
            }
            _censusBiomes.TryGetValue(label, out int n);
            _censusBiomes[label] = n + 1;
        }

        private void LogSampleGridCensus()
        {
            var sb = new System.Text.StringBuilder();
            int tileRooms = 0, boxRooms = 0;
            // ModBuild 262 — THE GENERICITY ALARM (user, 2026-08-24: "das Level … ist nur eines
            // von vielen … der Code [muss] generisch auf alle Szenarios und Räume im gesamten
            // Spiel funktionieren"). Every number this subsystem has been reasoned about came
            // from ONE forest ProcGen room with 16 sample cells, and 16 is not a property of this
            // code: the denominator is min(grid², playable hexes) and `grid` itself falls 4→3→2→1
            // purely on the REVEALED ROOM COUNT (6 / 10 / 24 rooms against the MaxTotalSamples
            // budget). Two derived quantities can therefore degenerate in a scenario nobody has
            // tested, and both are counted here rather than guessed at:
            //   • NO CELLS AT ALL — the room went over the sample budget, so no wall of it can
            //     ever be measured and they are all held solid. A wall that never fades.
            //   • THE SCHMITT BARS COLLAPSE IN CELL TERMS — ceil(On·n) == ceil(Off·n), so the
            //     enter and exit bars are the SAME number of samples and the trigger has no
            //     hysteresis left in the only unit it can actually move in. With the pair LIVE
            //     in the 2026-08-24 log (on 0.35 / off 0.20 — which ARE the shipped defaults,
            //     Defaults.OnFraction/OffFraction; the 0.25/0.10 next to WallFadeTuning.On/Off
            //     are the pre-Bind CLAMP FALLBACKS and reach no install) that is
            //     every n ≤ 2 (n=2: both bars 1 cell; n=1: both bars 1 cell), and the
            //     separation is already down to ONE cell for every n ≤ 11 against the TWO
            //     cells the 16-cell room has. Which n degenerate therefore moves with the cfg,
            //     which is why all four counters below read the LIVE bars rather than a
            //     constant. NOT a defect on its own — the EMA and the
            //     second-scale dwell are independent of n and still hold — but it is the term
            //     that goes first in a small room, and no build may claim a fade behaves the
            //     same there until this number has been read from one.
            // Reported only. Changing either bar is a fade-behaviour change and needs its own
            // round; this line exists so that round starts from a measurement.
            int roomsNoGrid = 0, roomsBarsCollapsed = 0, roomsBarsOneApart = 0;
            int roomsZeroRelease = 0; // ModBuild 262 lane F — see the block by the bars below
            for (int r = 0; r < _live.RoomSampleCount.Count; r++)
            {
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append("room ").Append(r);
                if (r < _live.RoomLabels.Count)
                    sb.Append(" '").Append(_live.RoomLabels[r]).Append('\'');
                int cells = _live.RoomSampleCount[r];
                if (r < _roomTileGrid.Count && _roomTileGrid[r])
                {
                    tileRooms++;
                    // ModBuild 259 — the funnel, term by term. The point of this row is that a
                    // reader can see WHICH term removed the hexes: if every cut column is 0 the
                    // playability filter did nothing and the defect is elsewhere.
                    int foot = _roomTileFootprint[r], play = _roomTilePlayable[r];
                    // ModBuild 262 lane F (F11): the hexes the lattice DRAWS FROM — not the
                    // denominator. The denominator is min(grid-squared, this set), which is
                    // exactly `cells`, and it is printed under its own label below.
                    int hexSet = _roomPlayableUsed[r] ? play : foot;
                    sb.Append(": ").Append(_roomTileTotal[r]).Append(" hex(es) on this room's CMap")
                      .Append(_roomTileTotal[r] != foot
                          ? $" → {foot} inside this room's own footprint (the rest sit outside "
                            + "it — a terraced CMap is two rooms here)"
                          : $" → all {foot} inside this room's own footprint")
                      .Append(" → ").Append(play).Append(" PLAYABLE [cut ")
                      .Append(_roomCutEdge[r]).Append(" EDGE-flag + ")
                      .Append(_roomCutBlockedFlag[r]).Append(" BLOCKED-flag + ")
                      .Append(_roomCutNodeBlocked[r])
                      .Append(" node-Blocked (an obstacle or dungeon-entrance prop stands on the "
                          + "hex) + ")
                      .Append(_roomCutNotWalkable[r])
                      .Append(" node-!Walkable with clean flags (a node/flag disagreement — "
                          + "expect 0); ")
                      .Append(_roomTileUnreadable[r])
                      .Append(" hex(es) gave no verdict and were KEPT, fail-open]")
                      .Append("; room CMap Revealed=").Append(RoomRevealedLabel(r))
                      .Append(_roomPlayableUsed[r]
                          ? $"; HEX SET = the {play} playable hex(es)"
                          : "; PLAYABLE FILTER HELD BACK — " + play + " playable hex(es) is fewer "
                            + "than the " + _sampleGridCells + " lattice position(s), so the "
                            + "hex set stays ModBuild 258's " + foot + " in-footprint hex(es) "
                            + "and NEITHER the quantum nor the two bars move. Using the " + play
                            + " would have made the quantum "
                            + (play > 0 ? (1f / play).ToString("F4") : "n/a") + " → exit bar "
                            + (play > 0 ? Mathf.CeilToInt(WallFadeTuning.Off * play) : 0)
                            + " cell(s), enter bar "
                            + (play > 0 ? Mathf.CeilToInt(WallFadeTuning.On * play) : 0)
                            + " cell(s) — that is a bar re-derivation, and this build does not "
                            + "make it")
                      .Append("; DENOMINATOR IN FORCE = ").Append(cells)
                      .Append(" sample(s) = min(grid² ").Append(_sampleGridCells)
                      .Append(", hex set ").Append(hexSet)
                      .Append("), so the quantum below is 1/").Append(cells)
                      .Append(" and NOT 1/").Append(hexSet)
                      .Append(". ModBuild 262 lane F fix: this clause used to print the hex-set "
                          + "size under the word DENOMINATOR, which is the number that reached "
                          + "the user as 'the playable hexes'. The 2026-08-24 log printed "
                          + "'DENOMINATOR = the 32 playable hex(es)' beside 'quantum 0.0625' — "
                          + "1/32 is 0.031, so the line contradicted itself, and the class "
                          + "header of this file had the right relation min(grid², hexes) all "
                          + "along. FALSIFIER for this fix: the quantum printed below must "
                          + "equal 1/DENOMINATOR IN FORCE to four decimals. ")
                      .Append(cells).Append(" of ").Append(_sampleGridCells)
                      .Append(" lattice position(s) kept ON A TILE (")
                      .Append(_sampleGridCells - cells)
                      .Append(" dropped — a lattice position is only dropped when the room has "
                          + "fewer hexes than positions, never for being off-tile: off-tile "
                          + "positions are MOVED to the nearest unclaimed hex), moved max ")
                      .Append(_roomSnapMax[r].ToString("F2")).Append(" wu / mean ")
                      .Append((cells > 0 ? _roomSnapSum[r] / cells : 0f).ToString("F2"))
                      .Append(" wu");
                }
                else
                {
                    boxRooms++;
                    sb.Append(": FELL BACK TO THE BOUNDING BOX (")
                      .Append(cells == 0
                          ? "over the " + MaxTotalSamples + "-sample budget — no grid at all, "
                            + "walls of this room are held solid"
                          : r < _roomMapKeys.Count && _roomMapKeys[r] == null
                              ? "no CMap on this room's volume renderer, so its hexes cannot be "
                                + "identified"
                              : !_tileSourceLive
                                  ? "ObjectCacheService did not answer — no tile registry this "
                                    + "rescan"
                                  : r < _roomTileTotal.Count && _roomTileTotal[r] > 0
                                      ? $"the CMap has {_roomTileTotal[r]} hex(es) but NONE "
                                        + "inside this room's own bounds — read that as a "
                                        + "footprint/registry disagreement, not as an empty room"
                                      : "the tile registry answered but holds no hex for this "
                                        + "room's CMap")
                      .Append("), ").Append(cells).Append(" box cell(s) — ModBuild 257 behaviour "
                          + "exactly, unchanged by this build");
                }
                if (cells > 0)
                {
                    int exitCells = Mathf.CeilToInt(WallFadeTuning.Off * cells);
                    int enterCells = Mathf.CeilToInt(WallFadeTuning.On * cells);
                    sb.Append(", quantum ").Append((1f / cells).ToString("F4"))
                      .Append(" → exit bar ").Append(exitCells)
                      .Append(" cell(s), enter bar ").Append(enterCells).Append(" cell(s)");
                    if (enterCells <= exitCells)
                    {
                        roomsBarsCollapsed++;
                        sb.Append(" [BARS COLLAPSED — enter and exit are the SAME cell count, so "
                                  + "this room's trigger has no hysteresis left in samples; only "
                                  + "the EMA and the dwell separate on/off here]");
                    }
                    else if (enterCells - exitCells < 2)
                    {
                        roomsBarsOneApart++;
                        sb.Append(" [bars ONE cell apart — a single sample crosses the whole "
                                  + "band; the 16-cell room this subsystem was reasoned about "
                                  + "has two]");
                    }
                    // ModBuild 262 lane F — a THIRD degeneracy, independent of the two above
                    // and asked separately because it is a different test. A wall is held on
                    // while coverage >= Off, so it can only RELEASE at blocked < Off·n, i.e. at
                    // exitCells - 1 cells. An exit bar of 1 therefore means it releases only
                    // with ZERO blocked samples: a one-way ratchet, faded until nothing at all
                    // is behind it. Computed from the LIVE bars, not a constant, because which
                    // cell counts degenerate depends on the tuned pair. THE SHIPPED DEFAULTS ARE
                    // Defaults.OnFraction 0.35 / OffFraction 0.20 (Defaults.Core.cs:167-168) —
                    // NOT the 0.25/0.10 that WallFadeTuning.On/Off name, which are the CLAMP
                    // FALLBACK arguments used only before Bind() and are not what any install
                    // receives. (ModBuild 262's first pass read those fallbacks as the defaults
                    // and reworded this block around them; corrected here. Same shape as the
                    // ModBuild 252 defaults slip: a value that LOOKS like a default because it
                    // sits next to the constant is not the one that ships.) At the shipped
                    // 0.35/0.20 pair this test holds for every n <= 5; at the spent 252 pair
                    // 0.25/0.10 it would hold for every n <= 10, which is why the count is
                    // computed from the LIVE bars and never from a constant — a player whose cfg
                    // still carries an older pair degenerates at a different room size than a
                    // fresh install does. FALSIFIER: a room printing exit bar >= 2 is not in
                    // this count.
                    if (exitCells <= 1)
                    {
                        roomsZeroRelease++;
                        sb.Append(" [exit bar 1 cell — this room's walls can only release with "
                                  + "ZERO blocked samples, a one-way ratchet]");
                    }
                }
                else
                {
                    roomsNoGrid++;
                }
            }
            // ── REGISTRY COVERAGE (ModBuild 262 lane F — F8) ────────────────────────────
            // The per-room rows above itemise ONLY the rooms that got a grid, so a census that
            // samples one room out of five reads exactly like a census of a one-room scenario.
            // The 2026-08-24 log: "184 hex(es) keyed to a room" against "room 0 'Room_5': 44
            // hex(es) on this room's CMap" — 140 hexes, 76 %, sat on CMaps absent from
            // _live.RoomBounds and were never measured. Both numbers were already on this line;
            // nobody could subtract them because the second one is buried per room. The ALARM
            // text states the TWO things that can then happen to a wall bordering such a room —
            // held FAIL-SAFE SOLID, or bound to the nearest OTHER room's floor grid — which are
            // the mechanisms behind "walls that should fade never do" and its mirror, "a wall
            // fades for a room I am not in". Cost: one walk of the logical-room list at the
            // existing census cadence, reusing _censusMapsSampled — no allocation, no scene
            // sweep, nothing per frame.
            _censusMapsSampled.Clear();
            int hexesSampled = 0, keylessRooms = 0;
            for (int r = 0; r < _live.RoomSampleCount.Count; r++)
            {
                object? key = r < _roomMapKeys.Count ? _roomMapKeys[r] : null;
                if (key == null)
                {
                    keylessRooms++;
                    continue;
                }
                if (r < _roomTileGrid.Count && _roomTileGrid[r]
                    && _tilesByMap.TryGetValue(key, out List<Vector3> hexList)
                    && _censusMapsSampled.Add(key))
                {
                    hexesSampled += hexList.Count;
                }
            }
            int registryMaps = _tilesByMap.Count;
            int sampledMaps = _censusMapsSampled.Count;
            string coverage =
                $"REGISTRY COVERAGE: {sampledMaps} of {registryMaps} distinct CMap(s) in the "
                + $"tile registry got a sample grid, covering {hexesSampled} of "
                + $"{_tilesResolved} keyed hex(es)"
                + (registryMaps > sampledMaps
                    ? $" — ALARM: {registryMaps - sampledMaps} CMap(s) / "
                      + $"{_tilesResolved - hexesSampled} hex(es) are in the registry and in NO "
                      + "room's denominator. THIS IS THE ONE-LINE READING THAT MEANS NOT "
                      + "GENERIC: the per-room rows above cannot show it, because they itemise "
                      + "only the rooms that WERE sampled. What happens to a wall bordering one "
                      + "of those rooms has two cases and AssociateRooms decides which. If the "
                      + "CMap does have a logical-room entry but no grid, RoomDecisionValid is "
                      + "false for it and the wall is held FAIL-SAFE SOLID unless the adjacent "
                      + "re-anchor finds a decision-valid room within AdjacentReanchorMaxGapWU. "
                      + "If the CMap has NO logical-room entry at all, AssociateRooms has "
                      + "nothing to pick and binds the wall to the NEAREST OTHER room, so its "
                      + "coverage is measured against a floor grid the player is not standing "
                      + "on — that one fades and unfades on the wrong room and raises no alarm "
                      + "anywhere else."
                    : registryMaps == 0
                        ? " — NOTHING MEASURED: the tile registry holds no CMap this rescan, so "
                          + "this clause has compared nothing and is not an all-clear."
                        : " — every CMap the registry knows is in some room's denominator.");
            string line =
                $"SAMPLE GRID: {TilesetLabel()}; {tileRooms} room(s) on the PLAYABLE-TILE "
                + $"denominator, {boxRooms} "
                + $"on the bounding box; tile registry {(_tileSourceLive ? "live" : "ABSENT")} "
                + $"({_tilesResolved} hex(es) keyed to a room, {_tilesUnkeyed} without a CMap "
                + $"and ignored). {coverage} — {sb}. The denominator is per room and the ray "
                + "loop is "
                + "unchanged; a wall's coverage is still blocked-samples over THIS room's "
                + "sample count. GENERICITY (ModBuild 262): the lattice is "
                + $"{_sampleGridCells} cell(s) this scenario because it holds "
                + $"{_live.RoomSampleCount.Count} room(s) — grid is 4 up to 6 rooms, 3 up to 10, 2 up "
                + $"to 24 and 1 beyond, against the {MaxTotalSamples}-sample budget, so ROOM "
                + "COUNT alone moves every room's quantum and both Schmitt bars in cell terms. "
                + $"{roomsNoGrid} room(s) got NO grid at all (their walls are held solid and can "
                + $"never fade), {roomsBarsCollapsed} have the two bars COLLAPSED onto the same "
                + $"cell count, {roomsBarsOneApart} have them ONE cell apart, and "
                + $"{roomsZeroRelease} have an exit bar of 1 cell — a faded wall there can only "
                + "release with ZERO blocked samples, a one-way ratchet, which is a different "
                + "test from the other two and is asked separately. All four read 0 in the "
                + "ModBuild 260 forest scenario, which is a SINGLE room whose CMap holds 44 "
                + "hexes, 42 of them inside its own footprint and 32 PLAYABLE, sampled at 16 "
                + "cells with the bars 4 and 6 — a sample of one. Those are three different "
                + "numbers and only the last one is a candidate denominator term; quoting the "
                + "42 as 'the playable hexes' is the ModBuild 261 error this build's DENOMINATOR "
                + "IN FORCE clause exists to stop. A non-zero here is the first measurement of "
                + $"what a small or many-roomed scenario actually does. COMPOUNDER: {keylessRooms} "
                + $"of the {_live.RoomSampleCount.Count} registry room(s) carry NO CMap — the room "
                + "registry adds one entry per CMap and floor-height bin PLUS one singleton per "
                + "room renderer with no CMap key (CommitRoomRegistry), so that room count is "
                + "NOT the game's room count, and partial CMap resolution silently coarsens the "
                + "grid ladder for EVERY room by inflating it.";
            if (line == _lastSampleCensus)
                return;
            _lastSampleCensus = line;
            VRLog.Info(Name, line);
        }

        /// <summary>
        /// Re-collect a segment's fade-capable renderers, combined AABB and shader-variant
        /// info (R2 diag: LOW = name contains "Low", e.g. Amp_Low/Amp_Basic_WallFade_Low
        /// from misc_shaders; anything else WallFade-capable is the HIGH misc_high_shaders
        /// variant).
        /// </summary>
        private void RefreshSegment(Segment seg)
        {
            BeginRefresh(seg);
            if (seg.Anchor == null)
                return;
            MeshRenderer[] all = seg.Anchor.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null)
                    continue;
                if (!CollectWallFadeInfo(r, seg))
                {
                    // Not fade-capable — but a foliage dressing of this wall rides its fade
                    // (the "Gestrüpp-Wand" report). Ground-level tufts are dropped later by
                    // StripGroundRenderers, exactly like ground geometry. A floor-standing
                    // FIGURE prop is excluded here too: the foliage list hides its renderers
                    // outright, so a mossy statue would vanish whole instead of losing a head.
                    // Deliberately NOT the 2026-08-19 FLOOR arm — a bush is a multi-piece thing
                    // standing on the ground under the height cap, so that rule would hand the
                    // Gestrüpp-Wand report straight back. Since ModBuild 257 this call also
                    // carries the TREE arm, which is the half of wand_problem2.jpg that lives in
                    // THIS list: the trunk is fade-capable and goes above, the canopy is
                    // Foliage-shaded and comes here, and a canopy intercepts more rays to the
                    // floor grid than a trunk does. See WallSegmentFade.Standing.cs,
                    // IsStandingFigureOnlyProp.
                    if (RendererUsesFoliage(r) && !IsStandingFigureOnlyProp(r))
                        seg.Foliage.Add(r);
                    continue;
                }
                seg.Renderers.Add(r);
                if (!seg.HasBounds)
                {
                    seg.Bounds = r.bounds;
                    seg.HasBounds = true;
                }
                else
                {
                    seg.Bounds.Encapsulate(r.bounds);
                }
                // Renderer may be brand new (Apparance rebuild) while the segment is mid-fade —
                // Apply() runs every frame for faded segments and will cover it.
            }
            FinishRefresh(seg);

            // Census tripwire: a cache wall WITH renderers but ZERO fade-capable ones means a
            // tileset whose wall shaders live outside the WallFade family — the one case neither
            // discovery source can fade. The heartbeat prints the shader names so the next
            // hardware log identifies the family to add.
            if (seg.Renderers.Count == 0 && all.Length > 0)
            {
                _censusWallsWithoutFade++;
                if (_unfadeableWallShaders.Count < 8)
                {
                    foreach (MeshRenderer r in all)
                    {
                        if (r == null)
                            continue;
                        _matScratch.Clear();
                        r.GetSharedMaterials(_matScratch);
                        foreach (Material m in _matScratch)
                        {
                            if (m != null && m.shader != null && _unfadeableWallShaders.Count < 8)
                                _unfadeableWallShaders.Add(m.shader.name);
                        }
                    }
                }
                // ROUND 6: the tripwire case is no longer diagnose-only — these plain
                // meshes ARE the visible wall (the keep masonry); collect them as the
                // segment's BODY (bounds + enabled-only delivery, WallSegmentFade.Body.cs).
                CollectPlainWallBody(seg, all);
            }
            else if (seg.Body.Count > 0)
            {
                // The wall (re)gained real fade renderers — the game's own shader path
                // wins; release the body takeover cleanly.
                RestoreSegmentBody(seg);
                seg.Body.Clear();
            }
        }

        /// <summary>Reset a segment's collected state for re-fill, parking the previous renderer
        /// list so <see cref="FinishRefresh"/> can clear property blocks off leavers.</summary>
        private static void BeginRefresh(Segment seg)
        {
            seg.PrevRenderers.Clear();
            seg.PrevRenderers.AddRange(seg.Renderers);
            seg.Renderers.Clear();
            seg.PrevFoliage.Clear();
            seg.PrevFoliage.AddRange(seg.Foliage);
            seg.Foliage.Clear();
            seg.HasBounds = false;
            seg.GeometryRefusedWhy = null; // re-derived by the split paths every rescan
            seg.RunPassenger = false;      // ditto — a dial turned off mid-session must not stick
            seg.VariantHigh = false;
            seg.VariantLow = false;
            seg.ToggleNative = 0;
            seg.ShaderNames = "?";
            seg.HeldCutoff = 0.5f;
            seg.CutoffAuthored = false;
            seg.Engulfing = false; // re-derived by NeutralizeEngulfingSegments after association
        }

        /// <summary>Post-refresh bookkeeping: clear our property block from renderers that LEFT a
        /// currently-faded segment (they would otherwise keep the fade forever — nothing else
        /// ever touches them again), then derive the blocked-test epsilon from the new bounds.</summary>
        private static void FinishRefresh(Segment seg)
        {
            if (seg.HasBlock)
            {
                foreach (MeshRenderer prev in seg.PrevRenderers)
                {
                    if (prev != null && !seg.Renderers.Contains(prev))
                        prev.SetPropertyBlock(null);
                }
            }
            seg.PrevRenderers.Clear();
            // Foliage that LEFT the segment is restored unconditionally — a hidden bush no
            // list points at any more would otherwise stay invisible forever. ModBuild 254:
            // foliage carries dissolve RECORDS now, so a leaver must also have its swapped
            // material copies destroyed and its authored materials put back. Without this the
            // swap leaks one material per bush per Apparance regeneration, which on a 345-piece
            // scenario is the fastest leak in the subsystem. Unconditional on the record rather
            // than on FoliageState, because a piece can hold a swap while the segment reads
            // solid (the record survives one frame longer than the state).
            foreach (MeshRenderer prev in seg.PrevFoliage)
            {
                if (prev == null || seg.Foliage.Contains(prev))
                    continue;
                if (seg.FoliageProps.TryGetValue(prev, out MountedProp? gone))
                {
                    seg.FoliageProps.Remove(prev);
                    RestorePropSwap(gone, prev);
                }
                if (seg.FoliageState != 0)
                    RestoreFoliageRenderer(prev);
            }
            seg.PrevFoliage.Clear();
            if (seg.HasBounds)
            {
                // Blocked-test epsilon ≈ half the wall run's thickness (the smaller
                // horizontal AABB extent), clamped to sane world-unit bounds — an L-shaped
                // corner run has two large extents, hence the upper clamp.
                float thickness = Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z);
                seg.BlockEps = Mathf.Clamp(0.5f * thickness, BlockEpsMinWorld, BlockEpsMaxWorld);
            }
        }

        /// <summary>
        /// Does any shared material use one of the wall-fade shaders — by NAME (the
        /// WallFade family) or by TOGGLE (round 8: materials exposing <c>_Cutoff</c> plus
        /// <c>_WallFade_On</c>/<c>_ToggleWallfade</c>, i.e. the same Amp fade subgraph
        /// behind a material switch; the keep's masonry Amp_Basic_N_MRAO is one)? Also
        /// records the shader name(s) and LOW/HIGH variant flags on the segment
        /// (rescan-time only). Deliberately NOT part of <see cref="RendererUsesWallFade"/>:
        /// the toggle test only runs for renderers that are already wall geometry by
        /// construction (children of cache walls) — N_MRAO dresses half the scenery, and a
        /// scene-wide toggle-based adoption would claim all of it as walls.
        /// </summary>
        /// <param name="figureArmOnly">ModBuild 261, and ONLY ever true from the two split-run
        /// sites while <c>[WallFade] SplitRunAdoptGroundScenery</c> is on. It swaps the guard for
        /// <see cref="IsStandingFigureOnlyProp"/> — the FIGURE arm alone, which is the exact
        /// substitution the foliage paths have made since ModBuild 167, not a new rule. Figures
        /// and actors stay refused; only the plain FLOOR arm steps aside, and only for a piece
        /// that then rides its run as a PASSENGER without ever voting on it.</param>
        private bool CollectWallFadeInfo(MeshRenderer r, Segment seg, bool figureArmOnly = false)
        {
            // STANDING PROPS ARE NEVER WALL GEOMETRY (user report 2026-08-15, skelet.jpg —
            // the skeleton statue's skull faded with 'Wall 1' while its body stayed). This is
            // the ONE choke point every wall-renderer collection path goes through — the cache
            // refresh, the split-wall refresh, the shader-adoption sweep and the gate-column
            // branch all add the renderer only when this returns true — so the guard lives here
            // rather than four times over. Refusing BEFORE the material walk also keeps the
            // prop out of ToggleNative counts, ShaderNames, the masonry template donor and the
            // authored-cutoff pick: the segment must not learn its wall math from a statue.
            // See WallSegmentFade.Standing.cs for the rule and why the plain figure guard is
            // not it.
            if (figureArmOnly ? IsStandingFigureOnlyProp(r) : IsStandingFigureProp(r))
            {
                NoteStandingPropBlocked(r, seg);
                // Restitution: if this renderer was in THIS segment's list before the rule
                // existed (or before the prop moved into the ground band), it may be carrying
                // our fade block right now. FinishRefresh only clears leavers while the
                // segment HasBlock; clear it here unconditionally so a prop can never stay
                // half-dissolved because its owner happened to be solid this frame.
                if (seg.PrevRenderers.Contains(r))
                    r.SetPropertyBlock(null);
                return false;
            }
            bool any = false;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null)
                    continue;
                // PERF S4: the name and its two Contains tests come out of the per-Shader cache
                // (_shaderFadeName) instead of an interop allocation per material per renderer
                // per commit. Same two expressions, same order, one entry per Shader.
                ShaderFadeName nameInfo = FadeNameOf(m.shader);
                string shaderName = nameInfo.Name;
                bool byName = nameInfo.ByName;
                bool low = nameInfo.Low;
                bool byToggle = !byName && HasLiveWallFadeToggle(m);
                if (!byName && !byToggle)
                    continue;
                any = true;
                if (byToggle)
                {
                    seg.ToggleNative++;
                    LogToggleNativeMaterialOnce(m);
                    CaptureMasonryTemplate(m); // round-11 dissolve-swap template donor
                    shaderName += "(toggle-native)";
                }
                // Held-state cutoff = the material's authored "Mask Clip Value" — the flat
                // game never writes _Cutoff, so this IS the value its fade runs with.
                // Clamped away from 0/1: with the held map's m = 0 any 0<c<1 produces the
                // identical geometry (c only shapes the HIGH variant's dither density),
                // while c = 0 would disable the LOW discard and c ≥ 1 would kill the HIGH
                // foundation band (clip = 1-c).
                if (!seg.CutoffAuthored && m.HasProperty(CutoffId))
                {
                    seg.HeldCutoff = Mathf.Clamp(m.GetFloat(CutoffId), 0.05f, 0.95f);
                    seg.CutoffAuthored = true;
                }
                // Read off the RAW name's cached fact: the "(toggle-native)" suffix appended
                // above carries no capital L, so this is the same answer the old
                // shaderName.Contains("Low") gave on either branch.
                if (low)
                    seg.VariantLow = true;
                else
                    seg.VariantHigh = true;
                if (seg.ShaderNames == "?")
                    seg.ShaderNames = shaderName;
                else if (!seg.ShaderNames.Contains(shaderName))
                    seg.ShaderNames += "+" + shaderName;
            }
            return any;
        }

        // ---- textures / teardown ------------------------------------------------------------

        /// <summary>
        /// Create the two delivery textures. Noise: 64x64 value noise (Mathf.PerlinNoise),
        /// rank-flattened to a uniform histogram over [0.06,1] so the _Cutoff sweep dissolves at
        /// a constant area-rate; low frequency keeps the left/right-eye patterns correlated
        /// (screen-space sampling differs per eye only by disparity); alpha 0 = "play area
        /// behind every pixel" under the shader's reversed-Z compare, m = 1-noise. Occluded
        /// (held) texture: r=1 AND a=0 — the depth compare fails for every visible fragment
        /// under either Z convention, so the map term m = 1-r = 0 is a CONSTANT: exactly the
        /// value the flat game's occlusion map yields over a revealed room, which lets each
        /// shader variant's own foundation-band terms survive (see class header).
        /// </summary>
        private bool EnsureTextures()
        {
            if (_noiseTex != null && _occludedTex != null)
                return true;

            const int size = 64;
            _noiseTex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            int n = size * size;
            var values = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                int x = i % size, y = i / size;
                // ~5 noise cells across the texture (which spans the SCREEN when sampled).
                values[i] = Mathf.PerlinNoise(x * (5f / size) + 11.31f, y * (5f / size) + 47.77f);
                order[i] = i;
            }
            Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));
            var pixels = new Color32[n];
            for (int rank = 0; rank < n; rank++)
            {
                byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(0.06f, 1f, (rank + 0.5f) / n) * 255f);
                pixels[order[rank]] = new Color32(r, 0, 0, 0);
            }
            _noiseTex.SetPixels32(pixels);
            _noiseTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _occludedTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeOccluded",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var occluded = new Color32[4];
            for (int i = 0; i < 4; i++)
                occluded[i] = new Color32(255, 0, 0, 0); // r=1, a=0 → m ≡ 1-r = 0 ("room behind")
            _occludedTex.SetPixels32(occluded);
            _occludedTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return true;
        }

        private void ClearAllBlocks(string reason)
        {
            int cleared = 0;
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.State = false;
                seg.PendingRaw = false;
                seg.Fade = 0f;
                seg.Smooth = 0f;
                seg.SmoothInit = false;
                RestoreSegmentFoliage(seg);
                RestoreSegmentSiblings(seg);
                RestoreSegmentMounted(seg);
                RestoreSegmentStacked(seg);
                RestoreSegmentBody(seg);
                if (!seg.HasBlock)
                    continue;
                seg.HasBlock = false;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                    {
                        r.SetPropertyBlock(null);
                        cleared++;
                    }
                }
            }
            // Toggle-off / no-scenario stops the rescan entirely, so the orphan guard would never
            // run again: empty the hidden ledger right here. "WallFade off" must mean vanilla,
            // with nothing of ours left switched off anywhere.
            RestoreAllMountedProps();
            if (cleared > 0)
                VRLog.Info(Name, $"cleared property blocks on {cleared} renderers ({reason}).");
        }

        /// <summary>Full teardown: revert every renderer and destroy our textures.</summary>
        internal void Teardown()
        {
            try { ClearAllBlocks("teardown"); }
            catch { /* renderers already dying with the scene */ }
            // Nothing we ever hid may survive a teardown — including a prop whose owner segment
            // died on some path before it could restore it (the orphan ledger's last stop).
            try { RestoreAllMountedProps(); }
            catch { /* renderers already dying with the scene */ }
            _live.Segments.Clear();
            _runs.Clear(); // run verdicts belong to the scenario they were measured in
            _live.RoomBounds.Clear();
            _live.RoomFloorY.Clear();
            _live.RoomFloorAnchored.Clear();
            _live.RoomLabels.Clear();
            _roomRendererCounts.Clear();
            _keyToRoomScratch.Clear();
            _roomMapByRenderer.Clear();
            _roomMapLabelByRenderer.Clear();
            _live.RoomSampleStart.Clear();
            _live.RoomSampleCount.Clear();
            _live.AllSamples.Clear();
            _roomMapKeys.Clear();
            _tilesByMap.Clear();
            _tileWhyByMap.Clear();
            _tileListPool.Clear();
            _tileWhyPool.Clear();
            _tileListsUsed = 0;
            _tileTaken.Clear();
            _tileScratch.Clear();
            _tilePlayScratch.Clear();
            _roomTileCount.Clear();
            _roomTileTotal.Clear();
            _roomSnapMax.Clear();
            _roomSnapSum.Clear();
            _roomTileGrid.Clear();
            _roomTileFootprint.Clear();
            _roomTilePlayable.Clear();
            _roomPlayableUsed.Clear();
            _roomCutEdge.Clear();
            _roomCutBlockedFlag.Clear();
            _roomCutNodeBlocked.Clear();
            _roomCutNotWalkable.Clear();
            _roomTileUnreadable.Clear();
            _floorYByRenderer.Clear();
            _live.CornerPieces.Clear();
            _peerFades.Clear();
            // Round 14: gate lifecycle state is per-scenario — a stale arch rect or a
            // remembered fade from the previous table must never seed the next one.
            _live.ArchRects.Clear();
            _gateMemory.Clear();
            _gateSliverLogged.Clear();
            // …and so is the water-feature protection (user ruling 2026-08-09).
            _live.WaterRects.Clear();
            _waterCensusSig = -1;
            AbandonRescanCycle();
            _shaderWaterVerdict.Clear();
            _shaderFadeName.Clear();
            if (_noiseTex != null)
            {
                try { Destroy(_noiseTex); } catch { /* already gone */ }
                _noiseTex = null;
            }
            if (_occludedTex != null)
            {
                try { Destroy(_occludedTex); } catch { /* already gone */ }
                _occludedTex = null;
            }
        }
    }
}
