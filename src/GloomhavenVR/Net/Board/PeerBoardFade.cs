using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using static GloomhavenVR.Core.ModuleConfig;

namespace GloomhavenVR.Net;

/// <summary>
/// What a peer's control board does while it stands between this viewer and the play field.
/// <see cref="Off"/> is the shipped behaviour, bit for bit: nothing is measured, nothing is
/// written, no material is touched.
/// </summary>
internal enum PeerBoardFadeMode
{
    /// <summary>Never yield. A peer's board renders exactly as it does today (default).</summary>
    Off = 0,

    /// <summary>Fade to <see cref="PeerBoardFadeTuning.Alpha"/> while the board occludes the
    /// play field, and back to solid when it stops.</summary>
    Transparent = 1,

    /// <summary>Disappear entirely while the board occludes the play field (the same decision,
    /// the same hysteresis, target alpha 0).</summary>
    Hidden = 2,
}

/// <summary>
/// Live-tunable decision thresholds for the peer-board see-through (canonical
/// <see cref="ModuleConfig.Create"/> pattern — <c>dev.gloomhavenvr.boardfade.cfg</c>): the two
/// Schmitt bars and the two un-fade dwells are the values that needed hardware iteration for the
/// WALLS, so they are config here from the start, re-read through clamped accessors on EVERY
/// evaluation tick. The remaining constants (the EMA tau, the fade tau, the sample-grid
/// geometry) are not merely code-owned but SHARED — they are the wall's own, by reference, from
/// <see cref="OcclusionFade"/>.
///
/// <para>THIS CLASS USED TO SAY IT WAS "modelled one for one on <c>WallFadeTuning</c>", AND THAT
/// IS EXACTLY THE CLAIM THAT ROTTED (user, 2026-08-27: <i>"ich will dass die Logik für das Board
/// die selbe ist, am besten derselbe code"</i>). A hand-copy is a snapshot of a design, and this
/// one was taken before ModBuild 252 replaced the collapsing <c>Min(off, on)</c> low bar. The
/// bars and dwells below stay separate CONFIG — a board is not a wall and its coverage is
/// measured against a different denominator, so one number could not serve both — but the RULE
/// that reads them is now literally the wall's, in Core/OcclusionFade.cs.</para>
/// </summary>
internal static class PeerBoardFadeTuning
{
    private static ConfigFile? _file;

    /// <summary>Off (shipped behaviour) / Transparent / Hidden — see <see cref="PeerBoardFadeMode"/>.</summary>
    internal static ConfigEntry<PeerBoardFadeMode>? FadeMode;
    /// <summary>Residual opacity of an occluding board in <see cref="PeerBoardFadeMode.Transparent"/>.</summary>
    internal static ConfigEntry<float>? OccludedAlpha;
    /// <summary>Smoothed view-coverage fraction at/above which a board yields (Schmitt high bar).</summary>
    internal static ConfigEntry<float>? OnFraction;
    /// <summary>Schmitt low bar: once yielded, it stays yielded while the fraction is at/above this.</summary>
    internal static ConfigEntry<float>? OffFraction;
    /// <summary>Seconds continuously below the low bar before coming back after a perspective change.</summary>
    internal static ConfigEntry<float>? ExitDwellMoved;
    /// <summary>Come-back dwell while the head has only ROTATED (no recent translation/recenter).</summary>
    internal static ConfigEntry<float>? ExitDwellStationary;
    /// <summary>One-shot marker for the 2026-08-28 dwell alignment — see <see cref="Bind"/>.</summary>
    internal static ConfigEntry<bool>? DwellsMigrated312;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("boardfade");
        // THE SIX DEFAULTS LIVE IN Defaults/Defaults.Net.cs, not here (2026-08-22 settings audit,
        // §6 "Housekeeping"): scripts/rebase-defaults.py maps a tuned cfg entry onto exactly one
        // annotated Defaults line and reports anything it cannot map rather than guessing, so a
        // literal at this call site would make every [PeerBoardFade] key UNMAPPED and silently
        // lose the user's tuning at the next re-base. The values are unchanged.
        FadeMode = config.Bind("PeerBoardFade", "Mode", Defaults.PeerBoardFade_Mode,
            "What a MITSPIELER's control board does while it stands between you and the play " +
            "field. THE MENU DOES NOT SHOW THESE MEMBER NAMES — the values are drawn as " +
            "'Permanently visible' / 'See-through' / 'Hidden' (DE: 'Permanent sichtbar' / " +
            "'Durchsichtig' / 'Ausgeblendet'); the tokens named here are what the cfg FILE " +
            "stores, and they are deliberately unchanged so an installed cfg keeps parsing. " +
            "Off = the board is never made see-through, i.e. it stays permanently visible " +
            "(nothing is measured or written). Transparent = it " +
            "fades to OccludedAlpha while it hides part of the board you are looking at. Hidden " +
            "= it disappears for as long as it does. PURELY LOCAL: the owner and every other " +
            "player still see their board exactly as before, and nothing goes on the wire. " +
            "Composes UNDER the [Net] RemoteBoards mode: this can only ever make a board that " +
            "mode already draws LESS visible, never more. Your OWN board is never affected.");
        OccludedAlpha = config.Bind("PeerBoardFade", "OccludedAlpha", Defaults.PeerBoardOccludedAlpha,
            "Residual opacity of an occluding peer board in Transparent mode: 0 = invisible " +
            "(same as the 'Hidden' value), 1 = solid (same as the 'Permanently visible' " +
            "value, cfg token Off). Live; clamped 0-0.95.");
        OnFraction = config.Bind("PeerBoardFade", "OnFraction", Defaults.PeerBoardOnFraction,
            "A peer board yields when it hides at least this (EMA-smoothed) fraction of the " +
            "play-field sample points currently IN YOUR VIEW — 0.12 = the board covers an " +
            "eighth of the map you are looking at (Schmitt trigger high bar). Live; clamped " +
            "0.02-0.95.");
        OffFraction = config.Bind("PeerBoardFade", "OffFraction", Defaults.PeerBoardOffFraction,
            "Once yielded, the board stays yielded while the smoothed coverage fraction stays " +
            "at or above this (Schmitt trigger low bar). Live; clamped 0.01-0.95 and never " +
            "above OnFraction.");
        ExitDwellMoved = config.Bind("PeerBoardFade", "ExitDwellMovedSeconds", Defaults.PeerBoardExitDwellMoved,
            "Seconds the coverage must stay below OffFraction before the board comes back when " +
            "the PERSPECTIVE recently changed (real head translation / rig recenter / the owner " +
            "moving their board). Live.");
        ExitDwellStationary = config.Bind("PeerBoardFade", "ExitDwellStationarySeconds",
            Defaults.PeerBoardExitDwellStationary,
            "Come-back dwell while the head has only ROTATED recently — rotation alone should " +
            "almost never bring a board back. Live; never below ExitDwellMovedSeconds.");

        // ONE-SHOT: THE 2026-08-28 DWELL ALIGNMENT, WHICH A CHANGED DEFAULT CANNOT DELIVER ON ITS
        // OWN. The user asked for these two to match the walls' ("Pass die default configs ... an
        // die der Waende an") and the shipped constants moved 2.5/7 -> 0.5/3.6 — but BepInEx has
        // already WRITTEN 2.5 and 7 into every existing dev.gloomhavenvr.boardfade.cfg, and a
        // default never reaches a key that is already on disk. Without this block the alignment
        // would have been correct in the source, green in every checker, and inert on the one rig
        // that asked for it. That is the same trap [WallFade] WallFadeBarsMigrated252 was written
        // for, and this is that block in its shape.
        //
        // IT REWRITES ONLY THE EXACT OLD PAIR. A cfg holding 2.5 AND 7 is a cfg nobody has touched
        // — those were the shipped numbers — so moving it is completing the default change, not
        // overriding a choice. Any other value, on either key, is a decision somebody made and is
        // left alone; the marker is still spent, so a deliberate 2.5 typed in later is never
        // second-guessed.
        DwellsMigrated312 = config.Bind("PeerBoardFade", "DwellsMigrated312",
            Defaults.PeerBoardDwellsMigrated312,
            "One-shot migration marker, not a setting. FALSE on a fresh install; set TRUE the " +
            "first time this build inspects an existing config. If ExitDwellMovedSeconds and " +
            "ExitDwellStationarySeconds were both found at the OLD shipped pair (2.5 / 7), they " +
            "are moved to the wall see-through's own values (0.5 / 3.6) at the same moment, " +
            "because a changed default cannot reach a key BepInEx has already written. Any other " +
            "value is left alone. Afterwards both are ordinary settings again and anything you " +
            "tune is kept for ever. Set this back to false to re-run.");
        if (DwellsMigrated312 is { Value: false })
        {
            DwellsMigrated312.Value = true;
            float hadMoved = ExitDwellMoved != null ? ExitDwellMoved.Value : Defaults.PeerBoardExitDwellMoved;
            float hadStat = ExitDwellStationary != null
                ? ExitDwellStationary.Value
                : Defaults.PeerBoardExitDwellStationary;
            // The OLD shipped pair, named here rather than in Defaults: they are history now, and a
            // Defaults entry for a retired value would look like something a fresh install gets.
            bool untouched = Mathf.Approximately(hadMoved, 2.5f) && Mathf.Approximately(hadStat, 7f);
            if (untouched && ExitDwellMoved != null && ExitDwellStationary != null)
            {
                ExitDwellMoved.Value = Defaults.PeerBoardExitDwellMoved;
                ExitDwellStationary.Value = Defaults.PeerBoardExitDwellStationary;
            }
            VRLog.Info("Net", untouched
                ? $"One-shot migration: [PeerBoardFade] ExitDwellMovedSeconds {hadMoved:F1} / " +
                  $"ExitDwellStationarySeconds {hadStat:F1} were the OLD shipped pair, i.e. nobody " +
                  "had tuned them. Moved to the wall see-through's own values " +
                  $"{Defaults.PeerBoardExitDwellMoved:F1} / " +
                  $"{Defaults.PeerBoardExitDwellStationary:F1}, which is what a fresh install now " +
                  "gets and what the user asked these two to match. The board fade was hand-copied " +
                  "from the wall before the wall's own tuning round, so it had been carrying the " +
                  "wall's PRE-tuning numbers while its doc still called them 'the wall's'. This " +
                  "runs ONCE — both are ordinary settings from here on and anything you tune is kept."
                : $"One-shot migration: nothing to do — [PeerBoardFade] ExitDwellMovedSeconds " +
                  $"{hadMoved:F1} / ExitDwellStationarySeconds {hadStat:F1} is not the old shipped " +
                  "pair, so somebody has chosen these and they were left exactly as they are. The " +
                  "marker is now spent and these two will never be rewritten again.");
        }
    }

    // Clamped live accessors — safe before Bind() (fall back to the shipped defaults, i.e. OFF).
    internal static PeerBoardFadeMode Mode => FadeMode == null ? PeerBoardFadeMode.Off : FadeMode.Value;
    internal static float Alpha => Mode == PeerBoardFadeMode.Hidden
        ? 0f
        : Clamped(OccludedAlpha, 0.25f, 0f, 0.95f);
    internal static float On => Clamped(OnFraction, 0.12f, 0.02f, 0.95f);
    /// <summary>Schmitt LOW bar, through the shared degenerate-band guard.
    /// <para>WAS <c>Mathf.Min(configured, On)</c> until 2026-08-27, which is the repair the wall
    /// fade REPLACED in ModBuild 252 because it does not repair anything: with the low bar at or
    /// above the high bar, Min collapses BOTH onto one threshold and the Schmitt trigger
    /// degenerates into a single bar that a coverage sitting near it crosses back and forth on
    /// EMA noise. On the wall that showed up as four walls flipping ON and OFF together in the
    /// ModBuild 250 log. The shipped board defaults (0.12 / 0.05) form a valid band, so this only
    /// ever bit a hand-edited cfg — but it was a live defect that survived here for the single
    /// reason this whole file has now been restructured to make impossible.</para>
    /// <para>0.05 inside <c>Clamped</c> is the PRE-BIND fallback and it agrees with the shipped
    /// default, <c>Defaults.PeerBoardOffFraction</c>. They must be changed together.</para></summary>
    internal static float Off =>
        OcclusionFade.SchmittLowBar(Clamped(OffFraction, 0.05f, 0.01f, 0.95f), On);
    // The numbers inside Clamped(...) are PRE-BIND fallbacks, not the shipped defaults, and they
    // move WITH Defaults.PeerBoardExitDwell* on purpose: a fallback that disagrees with the
    // shipped default is a second, invisible set of values that only ever appears before Bind()
    // -- a trap this project has paid for twice. Both now read the wall's own pair.
    internal static float DwellMoved => Clamped(ExitDwellMoved, 0.5f, 0.1f, 60f);
    internal static float DwellStationary =>
        Mathf.Max(Clamped(ExitDwellStationary, 3.6f, 0.1f, 120f), DwellMoved);

    // Clamped() moved to Core.ModuleConfig on 2026-09-05 (redundancy survey R31): this file held
    // one of three byte-identical private copies, and only one of the three carried the warning
    // about what its fallback argument is. It is imported by the `using static` at the top of
    // the file, so the call sites and their fallback literals above are unchanged, character for
    // character — the point of the row is that ONE implementation cannot drift, not that the
    // three copies happen to agree today.
}

/// <summary>
/// THE PLAY FIELD, AS A HANDFUL OF POINTS — the denominator every peer board is measured
/// against, built once per rescan and frustum-tested once per FRAME for all of them together.
///
/// <para>WHY A SAMPLE GRID AND NOT A SCREEN-SPACE FOOTPRINT TEST. The wall fade answers exactly
/// the same question ("does this thing hide the floor I am looking at?") by shooting the head→
/// floor-sample segments at the occluder and counting hits, and its whole hysteresis design — an
/// EMA over a FRACTION, a Schmitt trigger on that fraction, second-scale dwells — is built on a
/// scalar coverage number. A screen-rect overlap test answers a different, cruder question (it
/// cannot tell a board that is BEHIND the map from one in front of it, and it has no notion of
/// "how much"), and it would have needed its own hysteresis story. Reusing the wall's metric
/// means reusing the wall's proven thresholds.</para>
///
/// <para>SOURCE. <c>SceneRegistry.MapTiles</c> — the self-maintaining registry of every active
/// <c>ProceduralMapTile</c> that already replaced the mod's periodic
/// <c>FindObjectsOfType</c> sweeps, so a rescan here is a walk of ~10 entries and never a heap
/// scan. Each tile contributes a small grid over its own collider footprint, seated on the TOP
/// of that footprint (the tile plane the figures stand on), capped at
/// <see cref="MaxSamples"/> points over the whole map. Outside a scenario (menus, the void) the
/// registry is empty, the sample list is empty, and every board's coverage is 0 — nothing
/// fades, which is the correct answer where there is no play field.</para>
///
/// <para><b>ONLY DISCOVERED ROOMS ARE PART OF THE DENOMINATOR</b> (user ruling 2026-09:
/// <i>"Boards sollen NICHT transparent werden wenn sie nur unaufgedeckte/unentdeckte Tiles
/// verdecken, sondern nur bei bereits aufgedeckten Tiles."</i>). Until then this loop applied no
/// reveal filter at all, so a room still standing in its fog-of-war <c>Preview</c> stand-in
/// contributed sample points exactly like a room the player can see into — and with the two
/// active tiles of the shipped hardware capture, ONE unrevealed room was half the denominator.
/// The host's log shows what that cost: <c>9/18</c> blocked, <c>raw 0.500</c>, against an
/// <c>OnFraction</c> bar of 0.12. The board was being asked to get out of the way of fog.</para>
///
/// <para>The test is <see cref="SceneRegistry.IsTileDiscovered"/> — the game's own
/// <c>visibility == Visibility.All</c> comparison, shared with <c>UnseenTileOrder</c> so the two
/// subsystems cannot come to disagree about which rooms exist. It also brings this metric into
/// line with the WALL see-through, which has always had the rule by construction (its play area
/// is the game's revealed-room renderer list).</para>
///
/// <para>THE DEGENERATE CASE IS THE CORRECT ANSWER, and it is not new: with every tile
/// undiscovered the sample list is empty, every board's coverage is 0 and nothing fades. There
/// is nothing worth seeing through a board for. The census line below is what tells a hardware
/// capture "filtered" apart from "no map": it names the ACTIVE tile count, the DISCOVERED count
/// and how many were skipped as undiscovered.</para>
/// </summary>
internal static class PeerBoardPlayArea
{
    /// <summary>Total sample budget over the WHOLE map. The wall fade's own budget (96) for the
    /// same kind of grid; the cost that matters is one <c>WorldToViewportPoint</c> each, once per
    /// frame for every board together.</summary>
    private const int MaxSamples = OcclusionFade.MaxFloorSamples;
    /// <summary>World units above the tile's own top surface, so a sample is never swallowed by
    /// the floor mesh it sits on (the wall fade's <c>FloorSampleEpsilon</c>).</summary>
    private const float FloorSampleEpsilon = OcclusionFade.FloorSampleEpsilon;
    /// <summary>Viewport slack on the frustum test — also covers the mono-vs-per-eye skew, which
    /// is the same 0.20 the wall fade uses for the same reason.</summary>
    private const float FrustumMargin = OcclusionFade.FrustumMargin;
    /// <summary>Grid rebuild cadence. The wall's twin is <c>[WallFade] RescanIntervalSeconds</c>,
    /// a live dial since ModBuild 278 and shipped at this same 2 s. NOT wired to that dial: it
    /// governs a ~90 ms sliced wall-table commit, and a walk of ~10 registry entries has nothing
    /// to gain from the same knob and would make one number mean two things.</summary>
    private const float RescanSeconds = 2f;
    /// <summary>
    /// Frustum-test and decision cadence. The answer feeds a decision that is deliberately slow
    /// (an EMA, a Schmitt trigger and second-scale dwells), so sampling it at 20 Hz instead of
    /// 90 Hz cannot change WHICH boards fade — only when, within a twentieth of a dwell.
    ///
    /// <para>THE OLD COMMENT HERE CLAIMED "the same argument (and the same conclusion) as
    /// <c>PerfConfig.WallFadeInterval</c>", AND THAT WAS WRONG ON BOTH HALVES. The wall's cadence
    /// SHIPS AT 0 — every frame — through two doors (<c>[WallFade] EvalIntervalSeconds</c> wins
    /// when non-zero, <c>[Optimize] WallFadeEvalInterval</c> otherwise), and its 0 is not a
    /// conclusion at all: <c>Defaults.EvalIntervalSeconds</c> says in as many words that a
    /// non-zero default would be "a silent behaviour change smuggled in on a surfacing commit"
    /// and that the number is a human's to choose from a hardware capture.</para>
    ///
    /// <para>SO THIS ONE STAYS HARDCODED AT 20 Hz, and the argument is the ASYMMETRY the old
    /// comment gestured at without doing the arithmetic. The wall's decision runs ONCE for the
    /// scene; this one runs once per PEER. A 96-sample pass at 90 Hz is 8,640 sample-tests a
    /// second for the wall and 8,640 x N for the boards — at a four-player table that is 25,920
    /// ray-versus-box tests a second added to an 11.11 ms budget, to move an event that is
    /// debounced by a 0.20 s dwell at the very shortest. Sharing the wall's dial would import a
    /// 4.5x per-peer cost on the strength of a default that was chosen to change nothing.</para>
    ///
    /// <para>AND IT GETS NO DIAL OF ITS OWN. A third cadence key for a sub-feature is exactly
    /// what the standing no-new-config-key ruling is about, and nobody will tune from a headset
    /// a cadence whose only visible effect is a twentieth of a dwell.</para>
    /// </summary>
    internal const float EvalIntervalSeconds = 0.05f;

    private static readonly List<Vector3> Samples = new(MaxSamples);
    private static readonly bool[] Visible = new bool[MaxSamples];
    private static readonly List<ProceduralMapTile> TileScratch = new(32);
    private static int _visibleCount;
    private static int _frame = -1;
    private static float _nextRescan;
    private static float _nextVisibility;
    private static int _loggedCensus = int.MinValue;

    /// <summary>How many play-field samples exist at all (0 = no map: nothing may fade).</summary>
    internal static int Count => Samples.Count;
    /// <summary>How many of them are in the head frustum this frame (the fraction's denominator).</summary>
    internal static int VisibleCount => _visibleCount;
    internal static bool IsVisible(int i) => Visible[i];
    internal static Vector3 Sample(int i) => Samples[i];

    /// <summary>
    /// Rebuild the grid on its slow cadence and re-run the frustum test — at most ONCE per frame
    /// no matter how many peer boards ask, which is what keeps the shared half of this feature
    /// O(1) in the number of peers.
    /// </summary>
    internal static void EnsureFresh(Camera head, float now)
    {
        if (_frame == Time.frameCount || now < _nextVisibility)
            return;
        _frame = Time.frameCount;
        _nextVisibility = now + EvalIntervalSeconds;
        if (now >= _nextRescan)
        {
            _nextRescan = now + RescanSeconds;
            Rebuild();
        }
        UpdateVisibility(head);
    }

    private static void Rebuild()
    {
        Samples.Clear();
        TileScratch.Clear();
        SceneRegistry.MapTiles.Collect(TileScratch);
        int active = TileScratch.Count;
        // THE REVEAL FILTER (user ruling 2026-09 — see the class header). An undiscovered room is
        // fog: nothing on it is worth seeing, so a board that hides only fog is not in the way and
        // must not fade. Dropping the tiles here rather than skipping them in the grid loop is
        // deliberate — the per-tile budget below divides MaxSamples by the tile COUNT, so leaving
        // an undiscovered tile in the list would keep stealing a ninth of the budget from the
        // rooms that do count.
        for (int i = TileScratch.Count - 1; i >= 0; i--)
        {
            if (!SceneRegistry.IsTileDiscovered(TileScratch[i]))
                TileScratch.RemoveAt(i);
        }
        int tiles = TileScratch.Count;
        if (tiles == 0)
        {
            // No map, or a map whose every room is still fog. Both mean the same thing to every
            // board on this client — coverage 0, nothing fades — and the census line tells them
            // apart for the next capture.
            LogCensusIfChanged(0, active, tiles);
            return;
        }

        // Split the budget over the tiles, as a square grid per tile (3×3 / 2×2 / centre only).
        // A hex map tile is a room piece several hexes across, so one point per tile would be a
        // caricature of the floor; 3×3 resolves "the board hides the left half of this room".
        int perTile = Mathf.Clamp(MaxSamples / tiles, 1, 9);
        int side = perTile >= 9 ? 3 : perTile >= 4 ? 2 : 1;
        for (int t = 0; t < tiles && Samples.Count < MaxSamples; t++)
        {
            ProceduralMapTile tile = TileScratch[t];
            if (tile == null)
                continue;
            BoxCollider? box = tile.BoxCollider;
            // The same bounds ladder ApparanceDetailFocus uses for the same objects: the tile's
            // own box when it has one, its position with a generous default footprint when it
            // does not (world units — a map tile is ~20 wu across).
            Bounds b = box != null
                ? box.bounds
                : new Bounds(tile.transform.position, new Vector3(20f, 4f, 20f));
            float y = b.max.y + FloorSampleEpsilon;
            for (int ix = 0; ix < side && Samples.Count < MaxSamples; ix++)
            {
                for (int iz = 0; iz < side && Samples.Count < MaxSamples; iz++)
                {
                    float fx = (ix + 0.5f) / side;
                    float fz = (iz + 0.5f) / side;
                    Samples.Add(new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), y,
                                            Mathf.Lerp(b.min.z, b.max.z, fz)));
                }
            }
        }
        LogCensusIfChanged(Samples.Count, active, tiles);
    }

    private static void UpdateVisibility(Camera head)
    {
        _visibleCount = 0;
        for (int i = 0; i < Samples.Count; i++)
        {
            // Mono view/projection of the head camera. Under MultiPass the two eye frusta differ
            // by half the IPD and a little horizontal skew; the 0.20 viewport margin covers that
            // generously, and — this is the point — it is ONE answer used by BOTH eyes, so the
            // decision it feeds cannot differ between them (see the stereo note on PeerBoardFade).
            bool vis = OcclusionFade.InFrustum(head, Samples[i]);
            Visible[i] = vis;
            if (vis)
                _visibleCount++;
        }
    }

    /// <summary>
    /// One line per real change of the grid census — the evidence that the metric has a
    /// denominator at all, and the ONE line that tells "the reveal filter removed everything"
    /// apart from "no map is loaded". Those two produce the identical downstream behaviour
    /// (coverage 0 for every board, nothing fades), so without the two tile counts a capture
    /// cannot say which of them happened.
    ///
    /// <para><c>Note</c> and not <c>Info</c>, with the marker: a co-player runs at the shipped
    /// default level, where an <c>Info</c> line is not printed at all — and this line is the
    /// first thing "the peer's board still does not fade" has to be read against. It is gated on
    /// a real change of the (samples, active, discovered) triple, so a session in one room writes
    /// it once.</para>
    /// </summary>
    private static void LogCensusIfChanged(int count, int active, int discovered)
    {
        int signature = (count * 397 + active) * 397 + discovered;
        if (signature == _loggedCensus)
            return;
        _loggedCensus = signature;
        int skipped = active - discovered;
        // HW-VERIFY
        VRLog.Note("Net", $"Peer-board see-through: play-field grid rebuilt — {count} sample " +
                          $"point(s) over {discovered} DISCOVERED map tile(s); {skipped} of the " +
                          $"{active} active tile(s) were skipped as UNDISCOVERED (still showing " +
                          "the fog-of-war Preview stand-in, so nothing on them is worth seeing " +
                          "and a board that hides only them is not in anybody's way). This is " +
                          "the denominator every peer board's coverage fraction is measured " +
                          "against: 0 sample points means no board on this client can ever be " +
                          "judged occluding — read the two tile counts to see whether that is " +
                          "because no map is loaded (0 active) or because the whole map is still " +
                          "fog (0 discovered).");
    }
}

/// <summary>
/// USER REQUEST 15 (2026-08): "…möchte ich auch dass man zusätzlich einstellen kann, dass die
/// Boards transparent werden oder verschwinden wenn sie Teile des Spielfeldes verdecken aus der
/// aktuellen View. Das soll rein lokal sein. Ich möchte hier mit einer Gleichen oder ähnlichen
/// Logik arbeiten wie es bei den Wänden bereits der Fall ist."
///
/// <para>ONE OF THESE RIDES ON EVERY PEER BOARD ROOT (attached by
/// <see cref="RemoteBoardFurniture"/>, which is the one per-board constructor that is handed the
/// board root). It measures — from THIS viewer's head, every frame — how much of the play field
/// the board it sits on is hiding, debounces that exactly the way the wall fade debounces a wall,
/// and drives the whole board's opacity. Purely local: no wire byte, no game state, no effect on
/// the owner or on any other viewer. The board's own pose, content and draw order are untouched.</para>
///
/// <para><b>WHAT IS SHARED WITH <c>WallSegmentFade</c> — THE CODE, NOT A COPY OF IT</b> (user,
/// 2026-08-27: <i>"ich will dass die Logik für das Board die selbe ist, am besten derselbe
/// code"</i>). Until then this paragraph said the wall's decision had been "reused verbatim in
/// shape and in numbers", which is what a hand-copy always says on the day it is written and
/// stops being true on the next hardware round. It had by then diverged three ways: the low bar
/// still used the collapsing <c>Min</c> the wall replaced in ModBuild 252, the coverage metric
/// still asserted full coverage whenever the eye was inside the box — the short circuit the wall
/// DELETED in ModBuild 255 after it produced 44 bogus <c>raw 1.00</c> readings on one wall in one
/// session — and the perspective watch never learned about the rig root, so a two-handed zoom,
/// the largest viewpoint change this mod offers, registered as no change at all. All three are
/// gone, because the EMA, the Schmitt band, the two-sided dwell, the exponential ramp, the
/// frustum test, the degenerate-band guard and the perspective watch are now ONE body of code in
/// <c>Core/OcclusionFade.cs</c> that both subsystems call.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT SHARED (the older text, still true).</b> Reused
/// verbatim in shape and in numbers: the coverage metric (fraction of the head-visible floor
/// samples whose head→sample segment the occluder interrupts), the EMA over that fraction
/// (tau 0.15 s), the Schmitt trigger on the smoothed value, the short enter dwell (0.20 s) and
/// the two long exit dwells (2.5 s after a perspective change, 7 s while merely rotating), the
/// perspective-change arming (0.18 REAL tracking metres of head translation, 3 s arm window,
/// plus the rig pose version), and the critically-damped exponential ramp (tau 0.12 s) toward the
/// debounced state. Not reusable: the wall's OCCLUDER PROXY (a world-axis AABB) and its DELIVERY
/// (the game masonry shader's own <c>_Cutoff</c> discard). A control board is a thin slab at an
/// arbitrary attitude — its world AABB is mostly air, and a board seen edge-on would measure as a
/// wall-sized blocker — so the proxy here is the board's own BOARD-LOCAL box, which is an exact
/// oriented box test for a few more flops. And nothing on a peer board runs a fade shader, so the
/// delivery is the mod's own (below).</para>
///
/// <para><b>WHY THIS CANNOT REPRODUCE THE PARKED ONE-EYED WALL FADE</b>
/// (<c>.planning/wall-fade-stereo-rivalry.md</c>, user ruling: "das darf niemals passieren.
/// Entweder faded es auf beiden Augen oder gar nicht"). That defect is structural to the game's
/// masonry shader: its discard scalar contains a screen-radial term measured from EACH EYE'S OWN
/// screen centre, raised to the 8th power, so under MultiPass the two eyes evaluate different
/// discard conditions per fragment. Every decision in this class is a CPU scalar computed ONCE
/// per frame from the MONO head camera (position, and one shared frustum answer for the sample
/// grid), and it is delivered as a UNIFORM per-renderer alpha — one number for the whole surface,
/// identical in both stereo passes — or as a whole-renderer cull, which is equally per-eye
/// identical. There is no per-fragment, view-dependent term anywhere in the chain, and no
/// screen-space dissolve. The one thing that would reintroduce the defect — a screen-space
/// dither/dissolve pattern measured from the eye centre — is deliberately not used.</para>
///
/// <para><b>DELIVERY, AND THE HONEST LIMIT OF "TRANSPARENT".</b> A peer board is three families
/// of surface and each takes alpha differently:</para>
/// <list type="bullet">
/// <item>UI graphics (every mirrored widget, every TMP label on a canvas, the hosted card faces)
///   — ONE <see cref="CanvasGroup"/> on the board root. Its alpha multiplies down through nested
///   canvases, so it never touches the mirrors' OWN CanvasGroups (which
///   <c>RemoteWidgetMirror</c> drives from the source widget) and cannot fight them.</item>
/// <item>Renderers whose material can already blend (the Sprites/Default quads, the glows, the
///   3D TMP labels) — a <see cref="MaterialPropertyBlock"/> carrying the material's OWN live
///   colour with its alpha scaled. The material is never modified, so the per-frame colour
///   writers on this board (the cap press paint, the glow pulse) keep working and compose with
///   the fade instead of being frozen by it.</item>
/// <item>Renderers whose material CANNOT blend — and that is the important one: the real board
///   slab, the keycaps, the handle bars and the status plates all run the bundled
///   <c>GloomhavenVR/BoardLit</c>, an OPAQUE shader that exposes no <c>_Mode</c>, no
///   <c>_Surface</c>, no <c>_SrcBlend</c>/<c>_DstBlend</c> and no <c>_ZWrite</c> (measured, not
///   assumed — <c>HandGhost.MakeTransparent</c> documents the same finding from the hardware log
///   that made the ghost hand invisible-by-no-op). Writing alpha into such a material is a
///   guaranteed silent no-op. So a private CLONE of the material gets an unlit alpha-blended
///   shader (Sprites/Default, carrying <c>_MainTex</c> and tint across) for exactly as long as
///   the board is yielding, and the original shared material is put back on release. This is the
///   <c>HandGhost</c> recipe, and it is the only way "transparent" can mean transparent for the
///   slab rather than quietly meaning "hidden".</item>
/// </list>
/// <para>RESIDUE, STATED: while a board is swapped, the ordinary material writers on the swapped
/// surfaces (a style re-tint, a cap state colour) write to the material that is currently off the
/// renderer, so such a change appears when the board comes back rather than during the fade. Only
/// the swapped (opaque, unblendable) family is affected — the animated surfaces (glow pulse, cap
/// press) are in the MPB family, which composes live. If <c>Sprites/Default</c> cannot be found
/// at all, the unblendable family is CULLED past the half-way point of the ramp instead — it
/// pops, it is per-eye identical, and it is a fail-safe that has never been observed.</para>
///
/// <para>SECOND RESIDUE, AND IT IS A LOOK CHANGE, STATED BEFORE ANYONE REPORTS IT: the swapped
/// clone is UNLIT, so for the duration of the fade the slab and the keycaps lose their bevel
/// shading and read as flat albedo. The swap happens on the first frame of the ramp, i.e. while
/// the board is still ~100% opaque, so the flattening is visible for a moment before the fade
/// covers it. This is INFERRED, not measured — I have no headset here. It is also unavoidable on
/// this shader: BoardLit exists precisely because Gloomhaven's scenario/void scenes carry no
/// lights (a Standard "Fade" material would render the board black), so the alpha-capable target
/// has to be an unlit one. The alternative is not a prettier fade, it is no fade at all for the
/// slab — i.e. "Transparent" quietly meaning "Hidden", which is the outcome this class exists to
/// avoid. If the flash is judged worse than the flattening, the honest knobs are: use
/// <see cref="PeerBoardFadeMode.Hidden"/> (no partial state to look at), or delay the swap to a
/// lower alpha at the cost of the slab holding solid through the first part of the ramp.</para>
///
/// <para>PER-FRAME COST (arithmetic, not a hardware measurement — the numbers to check against
/// the next <c>[Perf]</c> capture). Shared, once per frame for ALL peers: up to 96
/// <c>WorldToViewportPoint</c> calls, and those are gated to 20 Hz. Per peer per DECISION tick
/// (also 20 Hz): one <c>InverseTransformPoint</c> plus, for each in-view sample, one
/// <c>MultiplyPoint3x4</c> and one <c>Bounds.IntersectRay</c> — ~96 × ~40 flops ≈ a few
/// microseconds. Per peer per FRAME: the exponential ramp (three flops) and, only while the board
/// is actually faded, one property-block write per renderer (~60 renderers on a full board). Per
/// peer every 0.5 s: one <c>GetComponentsInChildren</c> over the board plus eight corner
/// transforms per active renderer. With the mode Off — the default — every one of those is
/// skipped by the first statement of <see cref="LateUpdate"/>.</para>
///
/// <para><b>HOW IT COMPOSES WITH THE EXISTING DISPLAY MODES.</b> The rule is one sentence: the
/// [Net] <c>RemoteBoards</c> mode decides WHETHER a peer's board is drawn at all, and this
/// setting decides only HOW MUCH of an already-drawn board you see. It is strictly subtractive
/// and can never make a hidden board appear. The mechanism is structural rather than a rule
/// somebody has to remember: <c>RemoteControlBoard</c> deactivates the board ROOT when its mode
/// says hide, and a component on a deactivated root does not tick — so with "Aus" this class
/// never runs, and with "Aktionsphase" it runs exactly during the phases the board is shown and
/// restores itself the moment the gate shuts (<see cref="OnDisable"/>).</para>
///
/// <para><b>YOUR OWN BOARD IS EXEMPT, structurally: this component only ever exists on a
/// <see cref="RemoteControlBoard"/> root.</b> That is deliberate and it is what the user asked
/// for — the reported problem is "die Boards der Mitspieler verdecken die Sicht und ich muss sie
/// bitten, ihr Board zu verschieben", i.e. precisely the boards you cannot move. Your own board
/// is where you placed it, it is the surface you reach into and read continuously, and it is one
/// grab away from moving; fading it out from under your own hands would be a different feature
/// with a different failure mode.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY rendering, zero wire — a local viewing preference over
/// surfaces that are already local copies. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class PeerBoardFade : MonoBehaviour
{
    // ---- decision constants: ALIASES of the shared ones, so there is one definition ----------
    // The wall's copies are aliases of the same names. A `const = const` alias is resolved at
    // compile time, so nothing about the emitted code changes — it only makes it impossible for
    // these numbers to drift apart again, which is how this file came to be three fixes behind.
    // HeadMoveReevalMeters and ReevalArmSeconds are not here any more: they moved wholesale into
    // PerspectiveWatch, together with the rig-root clause this file never had.
    /// <summary>Short fade-OUT prompt dwell.</summary>
    private const float EnterDwellSeconds = OcclusionFade.EnterDwellSeconds;
    /// <summary>Exponential fade time constant (~0.35 s to 95%).</summary>
    private const float FadeTauSeconds = OcclusionFade.FadeTauSeconds;
    /// <summary>EMA over the raw coverage fraction (the jitter killer).</summary>
    private const float FractionTauSeconds = OcclusionFade.FractionTauSeconds;
    /// <summary>How often the board's surface census and occluder box are rebuilt. A peer board
    /// grows and loses surfaces all session long (cards, chips, cloned widgets), so a one-shot
    /// scan would fade the board it was built with and nothing that came after — the same reason
    /// <c>BoardVisual.AdoptBoardOrder</c> runs on a cadence.</summary>
    private const float SurfaceScanSeconds = 0.5f;
    private const float DiagIntervalSeconds = 2f;
    /// <summary>Below this effective alpha the whole board is culled rather than drawn: at 1% the
    /// surface contributes nothing but overdraw, and a cull is the cheapest possible delivery.</summary>
    private const float CullAlpha = 0.01f;

    /// <summary>At or above this driven alpha the board IS solid and the delivery is taken back
    /// down (<see cref="Release"/>). Unchanged in value from the bare <c>0.999f</c> that used to sit
    /// inline in <see cref="Apply"/>; named because <see cref="SettleAlpha"/> now has to be read
    /// against it.</summary>
    private const float SolidAlpha = 0.999f;

    /// <summary>
    /// Driven alpha at or above which an installed clone carries its ORIGINAL depth state.
    ///
    /// <para><b>IT WAS 0.97, AND THAT IS THE PART OF THE ModBuild 445 REPAIR THAT DID NOT WORK.</b>
    /// 445 was right that the discontinuity is the depth write and not the opacity, and its own
    /// instrument proves the mechanism ran: every RELEASED line in the 447 capture reads
    /// <c>all N clone(s) had already been put back into their ORIGINAL depth state</c>. The user
    /// reported the plop again anyway, and the reason is arithmetic 445 did not do. A depth write
    /// coming back does not change the slab — it hides everything drawn AFTER the slab that is
    /// physically behind it, which is the board's own content, riding <c>sortingOrder >= 100</c>
    /// precisely so it draws over the slab. That content is faded by the SAME alpha, so the size of
    /// the step is the alpha at which the depth state moves. At 0.97 the step was 97 % of that
    /// content, with 0.03 of ramp — about 30 ms — left to bury it. 445 moved the step 30 ms earlier
    /// and called it buried; it is not buried, because 3 % of remaining opacity carries no motion.
    /// </para>
    ///
    /// <para><b>THE STEP IS IRREDUCIBLE, SO PUT IT WHERE IT IS SMALLEST AND LEAST TERMINAL — WHICH
    /// IS THIS FILE'S OWN EXISTING RULE, APPLIED TO THE OTHER IRREDUCIBLE STEP.</b> The cull
    /// fail-safe band in <see cref="Apply"/> already says it in words: place the one unavoidable
    /// step "EARLY on the fade-out and LATE on the fade-in, i.e. inside the moving picture on both
    /// sides rather than at the end of one of them". The depth band was the one place that rule was
    /// not followed. At 0.55/0.40 the step is 40–55 % instead of 97 %, it lands with roughly half
    /// the alpha range and a third of a second of ramp still to run on BOTH sides, and — the term
    /// that actually settles the report — the material restore at
    /// <see cref="SolidAlpha"/> is preceded by 0.45 of alpha in the original depth state rather
    /// than by 0.03.</para>
    ///
    /// <para>ZWRITE CANNOT SIMPLY STAY ON FOR THE WHOLE RAMP, and that is why this is a band and
    /// not a deletion: a slab that writes depth at 25 % opacity depth-rejects every TRANSPARENT
    /// thing behind it, and the transparent things behind a peer's board are the hex decals and
    /// figure glows on the play field — the exact pixels this feature exists to uncover. The band's
    /// low bar is what hands those back.</para>
    /// </summary>
    private const float SettleAlpha = 0.55f;

    /// <summary>Driven alpha below which a settled clone goes back to its FADE depth state — it
    /// yields its depth write so the play field's own transparent overlays are visible through the
    /// faded board. A Schmitt band under <see cref="SettleAlpha"/>, so a ramp that reverses inside
    /// the band cannot strobe the depth state at the frame rate.</summary>
    private const float UnsettleAlpha = 0.40f;

    /// <summary>Driven alpha below which a renderer that could NOT be given an alpha-capable clone
    /// is hidden outright (the fail-safe family — see <see cref="Apply"/>). Paired with
    /// <see cref="FailSafeShowAlpha"/> as a Schmitt band, because a bare midpoint threshold on a
    /// continuous ramp toggles on every reversal near it.</summary>
    private const float FailSafeCullAlpha = 0.5f;

    /// <summary>Driven alpha at or above which a hidden fail-safe renderer is shown again.</summary>
    private const float FailSafeShowAlpha = 0.8f;
    /// <summary>Segment-length fraction by which the board must be IN FRONT of a sample before it
    /// counts as blocking it — the analogue of the wall's <c>BlockEpsDistFraction</c>. Keeps a
    /// board lying essentially ON the sample from claiming it.
    /// <para>DELIBERATELY NOT THE WALL'S NUMBER, and this is the one term where the two
    /// subsystems are supposed to disagree. The wall's rule is
    /// <c>max(halfWallThickness, 0.05 x dist)</c>: it has a thickness term because a wall IS
    /// thick, and the percentage exists only to keep long grazing rays honest ("a pure percentage
    /// was the round-4 bug"). A control board is a centimetre-thick slab with no half-thickness
    /// worth clamping, so the percentage is the whole rule here and 0.02 is sized for a slab
    /// rather than for masonry.</para></summary>
    private const float BlockEpsFraction = 0.02f;

    /// <summary>How far outside the board's own occluder box a <see cref="FollowRule.WhileOverBoard"/>
    /// root still counts as being "in front of the board", as a fraction of the box's larger
    /// footprint dimension. Half a board-width — see <see cref="OverBoard"/> for why this is
    /// derived from the board rather than tuned, and why it is not a config key.</summary>
    private const float FollowReachFactor = 0.5f;

    /// <summary>Colour properties an alpha write is attempted on, in this order. <c>_Color</c>
    /// covers Sprites/Default, Standard and BoardLit's clone; <c>_BaseColor</c> the URP-shaped
    /// materials; <c>_TintColor</c> the particle/additive glows; <c>_FaceColor</c> the 3D TMP SDF
    /// labels, whose fill alpha lives nowhere else.</summary>
    private static readonly int[] ColorIds =
    {
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_FaceColor"),
    };

    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int VertexColorId = Shader.PropertyToID("_VertexColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    /// <summary>
    /// <c>GloomhavenVR/BoardLit</c>'s opt-in transparency scalar (ModBuild 351). Its presence on a
    /// material is what tells this class the running bundle carries a BoardLit that can blend —
    /// i.e. whether the slab family keeps its LIT shader across the fade or has to be swapped onto
    /// an unlit one. Absent on every bundle up to and including the one shipped at ModBuild 350,
    /// which is why the swap path below is not dead code.
    /// </summary>
    private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");

    /// <summary>Name marker on every material clone this class installs. Read by
    /// <c>BoardVisual.AdoptBoardOrder</c>, which must NOT adopt a surface that is only
    /// transparent because it is currently fading (see the note there).</summary>
    internal const string CloneMarker = " (PeerBoardFade)";

    /// <summary>Queue a swapped-in clone draws at: the standard transparent tier. It keeps
    /// sortingOrder 0 while every other transparent surface on the board rides the furniture
    /// cluster at order ≥ 100, so the slab draws UNDER its own content, which is the only
    /// stacking that can look right.</summary>
    private const int SwapRenderQueue = 3000;

    private sealed class Surface
    {
        public Renderer Renderer = null!;
        /// <summary>True when every material on this renderer can blend as authored (no swap
        /// needed) — the MPB family.</summary>
        public bool Blendable;
        /// <summary>The heaviest <see cref="Delivery"/> any of this renderer's materials needs —
        /// the census line's evidence for WHICH of the two clone recipes the board slab family got,
        /// which is the one thing that decides whether the fade-in still steps.</summary>
        public Delivery Kind;
        /// <summary>The renderer's materials as we found them; non-null only while swapped.</summary>
        public Material[]? Original;
        /// <summary>Our private clones, parallel to <see cref="Original"/>; entries that needed
        /// no clone are the original material itself and are never destroyed.</summary>
        public Material[]? Installed;
        /// <summary>True while the installed clones carry the ORIGINAL depth state (ZWrite and
        /// cull mode) rather than the fade one — the whole top half of the ramp, in both
        /// directions. See <see cref="SettleDepthState"/>.</summary>
        public bool Settled;
        /// <summary>True while this renderer is hidden by the CULL FAIL-SAFE: no alpha-capable
        /// shader could be installed on it at all, so "fading" it means hiding it. Held as state
        /// rather than recomputed per frame so the decision can carry hysteresis.</summary>
        public bool FailSafeHidden;
        public bool Seen;
    }

    private readonly List<Surface> _surfaces = new(64);
    private readonly Dictionary<Renderer, Surface> _known = new(64);
    private readonly List<Renderer> _rendererScratch = new(64);
    private MaterialPropertyBlock? _mpb;
    /// <summary>Every uGUI alpha carrier this driver owns: the board root's group first, then one
    /// per live follower root. Written as one number in <see cref="Apply"/> — see
    /// <see cref="Follow"/> for why a follower must not get a carrier of its own.</summary>
    private readonly List<CanvasGroup> _groups = new(4);
    private CanvasGroup? _group;
    /// <summary>Follower roots seen by the last census, in registration order.</summary>
    private readonly List<Transform> _followers = new(4);
    /// <summary>Next census's follower set, built while <see cref="_followers"/> still holds the
    /// previous one (the freeze test reads it) and swapped in at the end.</summary>
    private readonly List<Transform> _followerScratch = new(4);
    /// <summary>Registered roots the last census REFUSED — a <see cref="FollowRule.WhileOverBoard"/>
    /// root its owner is not currently holding over the board. Reported, so a "the peer's hand fan
    /// still does not fade" line can say whether the registration or the predicate is the reason.</summary>
    private int _followersHeld;

    private Bounds _localBox;
    private bool _hasBox;
    private float _nextSurfaceScan;

    // --- decision state (the wall's Segment fields, one board's worth) ---
    private OcclusionGate _gate;
    private float _fade;
    /// <summary>The alpha last handed to <see cref="Apply"/>. 1 = solid. Held as its own field so
    /// a retirement (see <see cref="RetireToSolid"/>) can ramp the number the board is actually
    /// wearing, rather than re-deriving it from <see cref="_fade"/> through a mode that may have
    /// changed underneath it.</summary>
    private float _drivenAlpha = 1f;
    private bool _engaged;
    private float _nextEvalTime;
    private float _lastEvalTime;

    // --- perspective tracking ---
    private readonly PerspectiveWatch _perspective = new();
    private Vector3 _lastBoardPos;
    private Quaternion _lastBoardRot = Quaternion.identity;
    private bool _boardPoseInit;

    // --- diagnostics ---
    private int _playerId = -1;
    private float _nextDiag;
    private int _lastBlocked;
    private int _lastVisible;
    private float _lastRaw;
    private bool _loggedState;
    /// <summary>Seeded TRUE (state = solid) so only a REAL flip ever writes a line: a board that
    /// comes and goes with the action-phase gate would otherwise announce "OFF" on every reveal.</summary>
    private bool _loggedStateInit = true;
    private int _loggedSurfaces = -1;
    /// <summary>One-shot: the cull fail-safe has fired at least once on this board. It never has
    /// in any capture, and a line the first time it does is the only way that would be known.</summary>
    private bool _loggedFailSafe;

    /// <summary>
    /// Ensure the board rooted at <paramref name="boardRoot"/> carries the see-through driver.
    /// Idempotent. Binds the module config on the way through, so the settings rows exist as soon
    /// as the first peer board does.
    /// </summary>
    internal static PeerBoardFade? Attach(Transform? boardRoot)
    {
        if (boardRoot == null)
            return null;
        PeerBoardFadeTuning.Bind();
        PeerBoardFade? existing = boardRoot.gameObject.GetComponent<PeerBoardFade>();
        if (existing != null)
            return existing;
        var added = boardRoot.gameObject.AddComponent<PeerBoardFade>();
        // Registered HERE and not in Awake/OnEnable: Attach is the one seam that is handed the
        // board root, and a board root that is built inactive (the [Net] RemoteBoards gate) would
        // never run either callback — yet it is still a peer board that exists and that the
        // consumers below have to be able to enumerate.
        Live.Add(added);
        return added;
    }

    /// <summary>
    /// Every peer board this process currently carries a driver for, in build order. One entry per
    /// peer control board, added by <see cref="Attach"/> and removed by <see cref="OnDestroy"/>;
    /// entries may be inactive (a board parked by the RemoteBoards gate) and callers that care must
    /// say so. This is the only enumeration of peer board ROOTS in the mod — the driver is the one
    /// class that is handed each of them — so nothing needs a second registry beside it.
    /// </summary>
    private static readonly List<PeerBoardFade> Live = new(4);

    /// <summary>
    /// Append the ROOT transform of every peer control board that is currently shown.
    ///
    /// <para>WHO ASKS. <c>WorldUI.Surfaces.EnemyRevealSurface</c>'s board-clearance floor, which
    /// swings a new enemy-info plant up until the player's sight line to its bottom edge clears the
    /// top edge of every control board between them. Until 2026-09 that floor asked
    /// <c>PlayTray.Current</c> and nothing else, so it knew the viewer's OWN board and was blind to
    /// every peer's — which is precisely the hardware report "mit der Tiefe der Gegnerinfo beim
    /// remote board stimmt auch was nicht ... beim eigenen board ist alles wie es sein sollte".</para>
    ///
    /// <para>Shown, not merely built: a board the RemoteBoards gate has parked inactive draws
    /// nothing and can occlude nothing, and lifting a panel out of an invisible board would be an
    /// unexplained jump. The caller appends rather than receives a list so the scan allocates
    /// nothing per plant.</para>
    /// </summary>
    internal static void CollectShownBoardRoots(List<Transform> into)
    {
        if (into == null)
            return;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PeerBoardFade d = Live[i];
            if (d == null)
            {
                Live.RemoveAt(i); // destroyed without OnDestroy (scene teardown)
                continue;
            }
            if (d.gameObject.activeInHierarchy)
                into.Add(d.transform);
        }
    }

    /// <summary>The owning peer, for the diagnostic line only. Handed in from the board's own
    /// refresh (the constructor does not know it yet).</summary>
    internal void Note(int playerId) => _playerId = playerId;

    // ------------------------------------------------------------------------- FOLLOWERS -------

    /// <summary>
    /// Extra roots, per owning peer, whose renderers this driver fades WITH the board even though
    /// they are not under it in the hierarchy. See <see cref="Follow"/>.
    /// </summary>
    private static readonly Dictionary<int, List<FollowerEntry>> FollowerRoots = new(8);

    /// <summary>One registered follower root and the rule that decides whether it is in the fade
    /// set right now. See <see cref="FollowRule"/>.</summary>
    private readonly struct FollowerEntry
    {
        internal readonly Transform Root;
        internal readonly FollowRule Rule;
        internal FollowerEntry(Transform root, FollowRule rule)
        {
            Root = root;
            Rule = rule;
        }
    }

    /// <summary>
    /// WHEN a registered root belongs to the board's fade set. The distinction is the whole of
    /// user item 11a and it is a real one — see <see cref="Follow"/> for the argument.
    /// </summary>
    internal enum FollowRule
    {
        /// <summary>It belongs to the board unconditionally: it is posed FROM the board's pose and
        /// exists nowhere else. The three original followers (item arc, discard browse, card FX)
        /// are all of this kind, and this is the default so their call sites are unchanged.</summary>
        Always = 0,

        /// <summary>It belongs to the board only while it is physically parked OVER it. This is
        /// for HAND-anchored surfaces, which are avatar content wherever else they go and become
        /// "eine der Faecher vor dem Brett" only when their owner holds them there.</summary>
        WhileOverBoard = 1,
    }

    /// <summary>
    /// USER ITEM 7 (2026-09): "Die offenen Faecher, die ueber dem board schweben und zu dem board
    /// gehoeren (Gegenstaende, verbrannt, abgeworfen) sollen auch in dem selben Masse transparent
    /// sein, wenn das board transparent ist (durch ausfaden)."
    ///
    /// <para><b>WHY A REGISTRY AND NOT A HIERARCHY WALK.</b> Those fans BELONG to the board and are
    /// posed from it every frame, but they are not CHILDREN of it: <c>RemoteItemFan</c>,
    /// <c>RemoteBrowserFan</c> and <c>RemoteCardFx</c> each mint a scene-root
    /// <c>DontDestroyOnLoad</c> / <c>HideAndDontSave</c> GameObject and push the peer's board pose
    /// onto it (this is stated on purpose in <c>RemoteBoardVisibility</c>: "THREE transient reading
    /// fans that are rendered by their OWN classes and are NOT children of the board root"). This
    /// driver's census is <c>GetComponentsInChildren</c> on the board root, so it has never been
    /// able to see them — which is why a faded board left its item arc and its discard browse
    /// hanging fully opaque in mid-air. Re-parenting them onto the board is not an option: the two
    /// fans deliberately ease to a DIFFERENT smoothing than the board root does, and one of them
    /// re-parents a single slab into the board's item-use recess and back.</para>
    ///
    /// <para><b>WHAT MAKES IT "IN DEM SELBEN MASSE" AND NOT A SECOND FADE THAT LOOKS SIMILAR.</b>
    /// A registered root's renderers are appended to the SAME <c>_surfaces</c> list the board's own
    /// are in, and are written by the SAME <see cref="Apply"/> loop from the SAME local
    /// <c>alpha</c>, on the same frame. There is no second ramp, no second gate, no second config
    /// read and no second number anywhere in this class — a follower cannot drift from the board
    /// because there is nothing for it to drift from.</para>
    ///
    /// <para><b>WHAT A FOLLOWER DELIBERATELY DOES NOT DO: it never enters the occluder box.</b> The
    /// DECISION is measured on the board's own oriented box against the tuned
    /// <c>[PeerBoardFade] OnFraction</c> / <c>OffFraction</c> bars; a fan hovering a hand's width
    /// above the board would inflate that box and re-tune both bars by the back door. Delivery
    /// covers board + fans, the metric stays the board's.</para>
    ///
    /// <para><b>USER ITEM 11a (2026-09) ADDED THE SECOND RULE</b>: <i>"Die Faecher vor dem Brett
    /// sind nicht mit transparent in der Auswahlphase (wo nur die Rueckseiten sichtbar sind),
    /// sollen sie aber sein."</i> The fan he means is the peer's HAND fan (and its
    /// <c>RemoteEmptyFanHint</c> placard), which <c>RemoteHandFan.EnsureRoot</c> parents to the
    /// peer's HAND HOLDER and which therefore appears in neither this driver's
    /// <c>GetComponentsInChildren</c> census nor its follower registry. In the selection phase
    /// that fan hangs in front of its owner's board, showing card BACKS, and it stayed fully solid
    /// while the board behind it dissolved.</para>
    ///
    /// <para><b>THE CLASSIFICATION TENSION, AND HOW IT IS RESOLVED RATHER THAN PAPERED OVER.</b>
    /// <c>RemoteBoardGate</c> classifies hand-held surfaces as AVATAR content on purpose and
    /// deliberately does NOT gate them: hiding a peer's hands is a different feature with a
    /// different failure mode, and the setting is named "Mitspieler-Boards". Registering the hand
    /// fan here unconditionally would quietly move that line — a peer holding their cards up
    /// beside their face, nowhere near their board, would watch them dissolve because a board they
    /// are not standing behind is occluding somebody's view of the map.</para>
    ///
    /// <para>So the two questions are kept apart, because they ARE apart. <c>RemoteBoardGate</c>
    /// answers "may this surface be drawn at all", and its answer for hand-held content is
    /// unchanged: always yes. This registry answers "is this surface, right now, one of the things
    /// standing between the viewer and the play field that the board is already yielding for", and
    /// for a hand-anchored root that is a question about WHERE THE HAND IS. The predicate is
    /// <see cref="OverBoard"/>: the root's origin lies within half a board-width of the board's own
    /// oriented occluder box. It is measured in BOARD-LOCAL space, so it means the same thing at
    /// any diorama scale and from any viewer's seat, and it is exactly the user's own words — the
    /// fans IN FRONT OF the board.</para>
    ///
    /// <para><b>AND THE MEMBERSHIP CANNOT FLICKER, which would be worse than either extreme.</b>
    /// A root joining or leaving mid-fade is a POP by construction: joining installs clones and
    /// snaps the surface to the current alpha, leaving calls <see cref="Restore"/> and snaps it
    /// back to solid. A geometric predicate sampled on a 2 Hz census against a hand that is
    /// drifting would do exactly that, twice a second. So the conditional membership is decided
    /// ONLY WHILE THE BOARD IS SOLID and then held for the whole fade episode: once
    /// <see cref="_engaged"/> is true the set is frozen, and a hand that wanders off keeps fading
    /// until the board itself comes back. One stale follower for the tail of one fade is a cost;
    /// a fan strobing in and out of the set is a defect.</para>
    ///
    /// <para>Idempotent, and safe before the board exists — the list is keyed by player id and read
    /// on the driver's own 2 Hz census. Costs nothing at all while <c>[PeerBoardFade] Mode</c> is
    /// Off, because the driver never ticks. Unity-null roots are swept on the next census, so a fan
    /// that is destroyed with its peer needs no teardown call; <see cref="Unfollow"/> exists for a
    /// caller that gives a root up while the peer lives on. Re-registering a root that is already
    /// known UPDATES its rule rather than being ignored, so a caller cannot be left with a stale
    /// classification after a self-heal.</para>
    /// </summary>
    internal static void Follow(int playerId, Transform? followerRoot,
                               FollowRule rule = FollowRule.Always)
    {
        if (followerRoot == null || playerId < 0)
            return;
        if (!FollowerRoots.TryGetValue(playerId, out List<FollowerEntry> list))
            FollowerRoots[playerId] = list = new List<FollowerEntry>(4);
        for (int i = 0; i < list.Count; i++)
        {
            if (!ReferenceEquals(list[i].Root, followerRoot))
                continue;
            if (list[i].Rule != rule)
                list[i] = new FollowerEntry(followerRoot, rule);
            return;
        }
        list.Add(new FollowerEntry(followerRoot, rule));
    }

    /// <summary>Stop fading <paramref name="followerRoot"/> with <paramref name="playerId"/>'s
    /// board. Never needed for a root that is simply destroyed (the census sweeps Unity-null
    /// entries); needed only when a live root stops belonging to the board — and NOT the way a
    /// <see cref="FollowRule.WhileOverBoard"/> root leaves the set, which is the census's own
    /// per-scan decision and leaves the registration in place.</summary>
    internal static void Unfollow(int playerId, Transform? followerRoot)
    {
        if (followerRoot == null
            || !FollowerRoots.TryGetValue(playerId, out List<FollowerEntry> list))
        {
            return;
        }
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(list[i].Root, followerRoot))
                list.RemoveAt(i);
        }
    }

    /// <summary>
    /// THE ONE PLACE A PEER-BOARD SURFACE'S MATERIAL SLOT MAY BE REWRITTEN WHILE THIS DRIVER IS
    /// LIVE. Writes <paramref name="want"/> into slot <paramref name="index"/> of
    /// <paramref name="r"/> and returns whether anything moved (idempotent: an unchanged slot is
    /// not written and not reported).
    ///
    /// <para><b>WHY IT HAD TO EXIST.</b> <c>Cards/Art/CardMesh.SetBodyFaceHosted</c> states the
    /// hazard exactly: "anyone writing <c>sharedMaterials</c> while a board is faded would destroy
    /// those clones and snap the card back to opaque for the rest of the ramp", and concludes that
    /// a card-side correction must therefore be a MESH swap. That conclusion is right for the
    /// correction it was written about and wrong as a general rule — it leaves the mod with no way
    /// at all to change what a mirrored card's FRONT FAN is wearing, which is the surface user
    /// item 10 photographed (a peer's browse-fan card front framed by the card BACK's gold
    /// lattice). The hazard is not the write; it is a write that the fade driver does not hear
    /// about. This is the write it hears about.</para>
    ///
    /// <para><b>WHAT IT DOES WHILE SWAPPED.</b> <see cref="Surface.Original"/> is the array this
    /// class remembers verbatim and hands back in <see cref="Restore"/>, so it is the AUTHORED
    /// array and the one a caller means. The new material replaces that slot there, its own
    /// private clone replaces the same slot in <see cref="Surface.Installed"/> (the stale clone is
    /// destroyed, and only if it really was a clone), and every OTHER slot — and therefore the
    /// whole rest of the ramp — is untouched. A settled surface is re-settled so the fresh clone
    /// carries the same depth state its siblings do. Not swapped: a plain write plus a refreshed
    /// <see cref="Delivery"/> classification, because the new material may need a different one.</para>
    ///
    /// <para>A renderer no driver knows — a LOCAL card, a map-room slab, a peer surface on a board
    /// whose driver has not censused it yet — takes the plain write. That is correct rather than a
    /// fallback: nothing composites it, so there is nothing to keep in step.</para>
    /// </summary>
    internal static bool SetSubmeshMaterial(Renderer? r, int index, Material? want)
    {
        if (r == null || want == null || index < 0)
            return false;
        for (int i = 0; i < Live.Count; i++)
        {
            PeerBoardFade d = Live[i];
            if (d == null || !d._known.TryGetValue(r, out Surface s))
                continue;
            return d.WriteSurfaceMaterial(s, index, want);
        }
        Material[] mats = r.sharedMaterials;
        if (index >= mats.Length || ReferenceEquals(mats[index], want))
            return false;
        var next = new Material[mats.Length];
        System.Array.Copy(mats, next, mats.Length);
        next[index] = want;
        r.sharedMaterials = next;
        return true;
    }

    /// <summary>The swapped-aware half of <see cref="SetSubmeshMaterial"/> — see its doc.</summary>
    private bool WriteSurfaceMaterial(Surface s, int index, Material want)
    {
        Renderer r = s.Renderer;
        if (r == null)
            return false;
        Material[]? original = s.Original;
        Material[] authored = original ?? r.sharedMaterials;
        if (index >= authored.Length || ReferenceEquals(authored[index], want))
            return false;
        var nextAuthored = new Material[authored.Length];
        System.Array.Copy(authored, nextAuthored, authored.Length);
        nextAuthored[index] = want;
        if (original == null || s.Installed == null)
        {
            r.sharedMaterials = nextAuthored;
        }
        else
        {
            Material[] installed = s.Installed;
            Material stale = installed[index];
            if (stale != null && !ReferenceEquals(stale, original[index]))
                Object.Destroy(stale);
            installed[index] = CanBlend(want) ? want : MakeTransparentClone(want);
            s.Original = nextAuthored;
            if (s.Settled)
                SettleDepthState(s, settled: true);
            r.sharedMaterials = installed;
        }
        s.Blendable = CanBlendAll(nextAuthored);
        s.Kind = HeaviestDelivery(nextAuthored);
        return true;
    }

    /// <summary>
    /// Is this driver compositing its set at an alpha below solid RIGHT NOW — or about to be?
    ///
    /// <para>THE BAR IS THIS CLASS'S OWN <see cref="SolidAlpha"/>, NOT A BARE 1, AND THAT IS THE
    /// LOAD-BEARING PART. The four existing self-checks in this file ask
    /// <c>_engaged || _drivenAlpha &lt; 1f</c>, which is the right question for "is there anything
    /// left to hand back". It is the WRONG bar for a consumer that has to agree with what the
    /// surfaces are actually WEARING: <see cref="Apply"/> calls <see cref="Release"/> — restoring
    /// every original material, so the board is back in its opaque, depth-writing state — the
    /// moment alpha reaches <see cref="SolidAlpha"/> (0.999). A consumer holding out for 1f would
    /// spend those frames believing the board is still see-through, which for the face-hosting
    /// switch means a card's front fan is still dropped after its material has stopped
    /// compensating: the exact hole this predicate exists to close, several frames wide, once per
    /// fade episode. Reading the driver's OWN threshold makes the two agree by construction rather
    /// than through two constants somebody has to keep equal.</para>
    ///
    /// <para><see cref="_engaged"/> is the first arm because it is the authority on whether the
    /// clone materials are INSTALLED, which is the physical fact a consumer cares about; the alpha
    /// arm covers the ramp on either side of it. And the ramp reaches its target exactly rather
    /// than approaching it — <c>OcclusionFade.Ramp</c> snaps within <c>FadeSnapEpsilon</c> — so
    /// neither arm can park a consumer just below its threshold for ever.</para>
    /// </summary>
    internal bool CompositingBelowSolid => _engaged || _drivenAlpha < SolidAlpha;

    /// <summary>
    /// Which <see cref="PeerBoardFade"/> driver — if any — composites <paramref name="t"/>.
    ///
    /// <para>WHAT THE QUESTION IS FOR. It is the second term of the card-body FACE-HOSTING switch
    /// (<c>Cards.CardMesh.SetBodyFaceHosted</c>, decided in <c>Net/Remote/RemoteCardArt</c>). That
    /// switch drops a card body's FRONT FAN while a printed face stands in front of it, which kills
    /// the front/back bleed a fading board turns that fan into — and which ALSO removes the card's
    /// only depth-writing front surface, because the print is a uGUI canvas and uGUI writes no
    /// depth. So the switch has to find the driver that can composite the slab, and then ask it
    /// whether it is doing so (<see cref="CompositingBelowSolid"/>).</para>
    ///
    /// <para>IT USED TO BE A MEMBERSHIP QUESTION AND THAT WAS THE 2026-09 DEFECT. Until ModBuild 448
    /// this was <c>BelongsToAFadeSet</c>, a CAPABILITY test — "can this surface ever fade?" — and its
    /// answer was fed straight to the switch, so a peer's card dropped its front fan the moment it
    /// joined a fade set and never got it back. The comment that justified the substitution said the
    /// price was "a peer's printed card does not stamp depth while their board is still solid, which
    /// is a surface nowhere near the viewer's own hands". THAT PREMISE IS FALSE and the hardware
    /// report falsified it three times over (remote-fächer-tiefenproblem1..3): in room-scale VR the
    /// viewer walks up to a peer's board, reaches across it, and holds their own card over it, so
    /// the viewer's own ghost hand (renderQueue 3100, ZWrite off, ZTest LEqual) and wrist HUD (a
    /// world-space canvas at LEqual) stand exactly where a peer's fan cards are and came straight
    /// through them — the same picture 446 fixed for the map-room hand, on the mirrored surfaces.
    /// A capability test is not a policy: the bleed exists only while the surface is ACTUALLY being
    /// composited below 1, so that is what the switch now asks.</para>
    ///
    /// <para>THE ANTI-STROBE OBJECTION, ANSWERED. The old comment feared "a mesh swap on every ramp
    /// edge for ever". The edge count is two per fade EPISODE per card, not two per frame: the
    /// ramp is monotone within an episode and the episodes themselves are already debounced by this
    /// driver's Schmitt trigger and dwell. The swap is change-gated, serves a CACHED mesh
    /// (<c>CardMesh</c>'s face-hosted variant cache), and writes one <c>sharedMesh</c> reference —
    /// against a peer's card permanently losing the depth stamp its local twin keeps.</para>
    ///
    /// <para>TWO POPULATIONS, BOTH READ OFF THIS DRIVER'S OWN BOOKKEEPING rather than off a list of
    /// class names somebody has to keep current: anything under a transform that CARRIES a driver
    /// (the board root and everything parented beneath it), and anything under a root that was
    /// handed to <see cref="Follow"/> (the item arc, the discard browse, the card FX, the hand fan
    /// and its placard). A <see cref="FollowRule.WhileOverBoard"/> root counts whatever its owner's
    /// hand is doing right now, for the same anti-strobe reason.</para>
    ///
    /// <para>Null for every LOCAL surface — the player's own scenario hand, the map-room hand, the
    /// item chips on their own board — because none of them is a peer's board and none of them is
    /// registered here. That is the whole point: those keep their front fan and go on stamping the
    /// depth that rejects the ghost hand and the wrist HUD standing behind them. A peer's card on a
    /// SOLID board now answers the same way, which is the fix.</para>
    /// </summary>
    /// <param name="t">The surface to resolve.</param>
    /// <param name="followerRoot">The REGISTERED follower root <paramref name="t"/> hangs under, or
    /// null when it is simply parented beneath the board itself. A follower root's membership is
    /// conditional — a <see cref="FollowRule.WhileOverBoard"/> fan its owner has taken away from the
    /// board is registered but NOT composited — so a caller that needs the live state has to hand
    /// this back to <see cref="IsFollowing"/> rather than assume registration means membership.</param>
    internal static PeerBoardFade? DriverFor(Transform? t, out Transform? followerRoot)
    {
        followerRoot = null;
        if (t == null)
            return null;
        // Under a board root: the driver's own surface census is GetComponentsInChildren on the
        // transform it sits on, so "carries a driver somewhere up my parent chain" is precisely
        // "that driver's census would find me". Inactive included — a fan parked inactive is still
        // a member of the set it rejoins the moment it is shown.
        PeerBoardFade? own = t.GetComponentInParent<PeerBoardFade>(includeInactive: true);
        if (own != null)
            return own;
        foreach (KeyValuePair<int, List<FollowerEntry>> byPlayer in FollowerRoots)
        {
            List<FollowerEntry> list = byPlayer.Value;
            for (int i = 0; i < list.Count; i++)
            {
                Transform root = list[i].Root;
                if (root != null && t.IsChildOf(root))
                {
                    followerRoot = root;
                    return DriverOf(byPlayer.Key);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Is <paramref name="followerRoot"/> in this driver's CURRENT fade set — i.e. did the last
    /// census admit it?
    ///
    /// <para>WHY REGISTRATION IS NOT ENOUGH, AND WHY THIS EXISTS. A
    /// <see cref="FollowRule.WhileOverBoard"/> root — the peer's hand fan and its placard — is
    /// registered for the whole session but composited only while its owner is actually holding it
    /// over the board (<see cref="RefreshSurfaces"/>'s <c>OverBoard</c> test, frozen while
    /// <see cref="_engaged"/>). A consumer that read only <see cref="CompositingBelowSolid"/> would
    /// therefore treat a hand fan held AWAY from a fading board as see-through when the driver is
    /// writing nothing to it at all — and for the face-hosting switch that means dropping the depth
    /// stamp of a fully opaque card for no benefit whatever, which is a fresh copy of the very
    /// defect this round is removing.</para>
    ///
    /// <para>Reference compares over the census's own admitted list, never a transform walk: the
    /// caller has already resolved WHICH registered root its surface hangs under, and that answer
    /// can only change when the surface is rebuilt.</para>
    /// </summary>
    internal bool IsFollowing(Transform? followerRoot)
    {
        if (followerRoot == null)
            return false;
        for (int i = 0; i < _followers.Count; i++)
        {
            if (ReferenceEquals(_followers[i], followerRoot))
                return true;
        }
        return false;
    }

    /// <summary>The live driver for one peer, or null while their board has not been built (or has
    /// been torn down) yet. A follower root registered ahead of its board therefore resolves to NO
    /// driver — and that is the SAFE direction for every consumer here: "not compositing" means the
    /// card keeps its front fan and goes on stamping depth, which is what a local card does.</summary>
    private static PeerBoardFade? DriverOf(int playerId)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            PeerBoardFade d = Live[i];
            if (d != null && d._playerId == playerId)
                return d;
        }
        return null;
    }

    private void LateUpdate()
    {
        PeerBoardFadeMode mode = PeerBoardFadeTuning.Mode;
        if (mode == PeerBoardFadeMode.Off)
        {
            // OFF IS BIT FOR BIT TODAY'S BEHAVIOUR — but it gets there over the ORDINARY RAMP
            // (user item 10). This used to be a bare ResetToSolid(), i.e. `_fade = 0f` and a
            // material restore in the same frame: switching the dropdown to "Aus" made every
            // currently-faded peer board snap solid on one frame, which is the very step the user
            // reported at the end of a fade-in, delivered at its full magnitude. There is nothing
            // about the mode being Off that requires the transition to be instant; it only requires
            // that the class stop writing, and it does, ~0.6 s later and for ever after.
            if (_engaged || _drivenAlpha < 1f)
                RetireToSolid("the [PeerBoardFade] Mode dial was moved to Off");
            return;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            // No head means no viewer and therefore no decision to make — but the board may be
            // mid-fade, and handing it back over the ramp costs three flops a frame for six frames.
            if (_engaged || _drivenAlpha < 1f)
                RetireToSolid("the VR head camera went away, so there is no viewpoint to judge from");
            return;
        }

        float now = Time.unscaledTime;
        PeerBoardPlayArea.EnsureFresh(head, now);
        UpdatePerspectiveState(head.transform, now);
        if (now >= _nextSurfaceScan)
        {
            _nextSurfaceScan = now + SurfaceScanSeconds;
            RefreshSurfaces();
        }

        // ---- the decision, in the wall's own order: raw fraction → EMA → Schmitt → dwell ------
        // THE DECISION IS GATED, THE RAMP IS NOT (the wall's split, and for its reason): the
        // expensive half is the per-sample geometry, which is head-motion correlated; the cheap
        // half is the exponential ramp plus its writes, which must stay per-frame or the fade
        // would visibly step. The EMA advances by the time since the last EVALUATION, never since
        // the last frame — otherwise the cadence would silently stretch its time constant and
        // change WHICH boards fade, which is exactly what it must not do.
        float onFraction = PeerBoardFadeTuning.On;
        float offFraction = PeerBoardFadeTuning.Off;
        bool reevalArmed = _perspective.Armed(now);
        float exitDwell = reevalArmed
            ? PeerBoardFadeTuning.DwellMoved
            : PeerBoardFadeTuning.DwellStationary;

        // THE SCHMITT TRIGGER AND THE DWELL MOVED INSIDE THE EVALUATION GATE (2026-08-27). Until
        // then the gate covered the coverage measurement and the EMA, and these two ran every
        // frame over a smoothed value that could not have changed since the last tick — so the
        // decision was two thirds gated and the flip landed on the frame the dwell expired instead
        // of on the next evaluation. The wall has always run all three inside the gate. Adopting
        // that costs at most one evaluation period (50 ms) on a dwell that is 0.2 s at the very
        // shortest, and removes a per-frame branch per peer.
        bool evaluate = now >= _nextEvalTime;
        if (evaluate)
        {
            _nextEvalTime = now + PeerBoardPlayArea.EvalIntervalSeconds;
            float fraction = BlockedFraction(head.transform.position);
            _lastRaw = fraction;
            float evalDt = OcclusionFade.EvalDelta(now, _lastEvalTime, Time.unscaledDeltaTime);
            _lastEvalTime = now;
            float fracStep = OcclusionFade.StepFactor(evalDt, FractionTauSeconds);
            // EMA -> Schmitt -> dwell, in Core/OcclusionFade.cs — the same statements a wall
            // segment and a split wall run execute, against this board's own bars.
            _gate.Evaluate(fraction, now, fracStep, onFraction, offFraction,
                           EnterDwellSeconds, exitDwell);
        }

        // Critically-damped-style exponential ramp toward the debounced state — the wall's, and
        // the reason a board never snaps even when the decision does.
        float fadeStep = OcclusionFade.StepFactor(Time.unscaledDeltaTime, FadeTauSeconds);
        _fade = OcclusionFade.Ramp(_fade, _gate.Latched ? 1f : 0f, fadeStep);

        float alpha = Mathf.Lerp(1f, Mathf.Clamp01(PeerBoardFadeTuning.Alpha), _fade);
        _drivenAlpha = alpha;
        Apply(alpha);
        LogStateIfChanged(alpha, onFraction, offFraction, exitDwell, reevalArmed);
        if (now >= _nextDiag && !PerfConfig.Quiet && (_engaged || _gate.Latched))
        {
            _nextDiag = now + DiagIntervalSeconds;
            LogDiagnostic(alpha, onFraction, offFraction, reevalArmed);
        }
    }

    /// <summary>
    /// The board root going inactive is the [Net] RemoteBoards gate shutting (mode Off, or the
    /// secret selection phase under "Aktionsphase") — or the board being torn down for a rebuild.
    /// Both must leave the board exactly as it was found, and must forget the decision: a board
    /// that comes back is judged fresh against the view it comes back into, never against the one
    /// it left.
    ///
    /// <para>THIS ONE STAYS INSTANT, and that is not an oversight (user item 10 asked for every
    /// un-ramped path to be named). A ramp needs frames, and a component whose GameObject has just
    /// been deactivated gets none — the board is not being shown at a different opacity, it is
    /// being taken off the screen entirely, so there is no picture for a ramp to smooth. What DID
    /// need fixing is that a snap and a ramp used to be indistinguishable in a capture, because
    /// none of these paths wrote a line at all. They all do now, and they say which one they
    /// were.</para>
    /// </summary>
    private void OnDisable() => ResetToSolid("the board root was deactivated (the [Net] " +
                                             "RemoteBoards gate shut, or the board is rebuilding)");

    private void OnDestroy()
    {
        Live.Remove(this);
        ResetToSolid("the board was destroyed");
    }

    /// <summary>
    /// Hand the board back over the SAME exponential ramp a normal un-fade uses, then reset. The
    /// ramp is driven on the delivered ALPHA rather than on <see cref="_fade"/> on purpose: the
    /// mode may already have changed under us, and <c>PeerBoardFadeTuning.Alpha</c> reads the mode,
    /// so re-deriving alpha from <c>_fade</c> after a Hidden→Off switch would jump the board from
    /// invisible to a quarter-opaque on the first retirement frame — a step introduced by the very
    /// code that exists to remove one.
    /// </summary>
    private void RetireToSolid(string why)
    {
        float step = OcclusionFade.StepFactor(Time.unscaledDeltaTime, FadeTauSeconds);
        _drivenAlpha = OcclusionFade.Ramp(_drivenAlpha, 1f, step);
        if (_drivenAlpha < 1f)
        {
            Apply(_drivenAlpha);
            return;
        }
        ResetToSolid(why + ", and the board has finished ramping back to solid");
    }

    private void ResetToSolid(string why)
    {
        bool wasFaded = _engaged || _drivenAlpha < 1f;
        Release();
        if (wasFaded)
            LogDelivery("RESET", why, 0);
        _fade = 0f;
        _drivenAlpha = 1f;
        _gate.Reset();
        _lastEvalTime = 0f;
        _nextEvalTime = 0f;
        _loggedStateInit = true;
        _loggedState = false;
    }

    // ------------------------------------------------------------------ the coverage metric ----

    /// <summary>
    /// Fraction of the play-field samples IN VIEW whose head→sample segment this board's own
    /// oriented box interrupts.
    ///
    /// <para>WHY THE DENOMINATOR IS THE IN-VIEW SET AND NOT THE WHOLE MAP (the one deliberate
    /// departure from <c>WallSegmentFade</c>, which divides by its room's WHOLE grid). A wall
    /// belongs to a room and is judged against that room; a peer's board belongs to nothing and
    /// floats anywhere. Measured against the whole map, a board could hide every hex you are
    /// actually looking at and still score a few percent — the metric would be dominated by parts
    /// of the map behind you. "How much of what I am looking at is this board eating" is the
    /// question the user asked, and it is the only one whose answer is stable when you turn.</para>
    ///
    /// <para>AWKWARD CASES, and what this returns for them. EDGE-ON: the box is a few centimetres
    /// thick in board-local Z, so a board seen edge-on intersects almost no segments and scores
    /// ~0 — it does not fade, which is right, because edge-on it is not hiding anything. (A
    /// world-axis AABB, the wall's proxy, would have scored it like a wall — that is the reason
    /// for the local-space test.) BEHIND THE MAP: the hit must land strictly between the eye and
    /// the sample, so a board on the far side of the hexes it appears to overlap blocks nothing.
    /// BETWEEN TWO PLAYERS: irrelevant by construction — the only viewpoint that exists here is
    /// this client's head, and the answer is computed independently on every machine. IN YOUR
    /// FACE (the eye inside the box): the samples the board actually covers, and NOT a hard 1.0 —
    /// see the ModBuild 255 note at the loop below for why the short circuit that used to assert
    /// that was deleted from the wall and is now deleted from here.</para>
    ///
    /// <para>KNOWN OVER-COUNT, stated: the proxy is the board's furnished BOX, not its silhouette,
    /// so the notches between an off-edge dock and the slab (the initiative mirror hangs above the
    /// top edge, the piles off the right) are counted as solid. That makes the metric slightly
    /// generous — a board fades a little earlier than its literal pixels justify — which is the
    /// safe direction for a feature whose failure mode is "the board is still in my way", and it
    /// costs one box test instead of a per-renderer sweep.</para>
    /// </summary>
    private float BlockedFraction(Vector3 headPos)
    {
        _lastBlocked = 0;
        _lastVisible = PeerBoardPlayArea.VisibleCount;
        if (!_hasBox || _lastVisible <= 0)
            return 0f;

        Transform t = transform;
        Vector3 localEye = t.InverseTransformPoint(headPos);
        // THE "BOARD IN THE FACE" SHORT CIRCUIT IS GONE (2026-08-27), and it was never needed —
        // the same deletion the wall fade made in ModBuild 255, for the same reason and with the
        // same evidence. It returned a hard 1f, "this board hides the ENTIRE play field", the
        // moment the eye entered the box, and reported every in-view sample as blocked while
        // doing so. On the wall that assertion produced 'Wall 2' sitting at raw 1.00 in 44 of its
        // diagnostic samples in one session, none of which came from any measurement of what the
        // wall covered. The ordinary ray math already handles an eye inside the geometry:
        // Bounds.IntersectRay returns TRUE at distance 0 for a ray whose origin is inside the
        // box, so every sample genuinely behind the board is counted by the normal path below,
        // and only those.
        Matrix4x4 toLocal = t.worldToLocalMatrix;
        int blocked = 0;
        int n = PeerBoardPlayArea.Count;
        for (int i = 0; i < n; i++)
        {
            if (!PeerBoardPlayArea.IsVisible(i))
                continue;
            Vector3 localSample = toLocal.MultiplyPoint3x4(PeerBoardPlayArea.Sample(i));
            Vector3 seg = localSample - localEye;
            float len = seg.magnitude;
            if (len <= 1e-4f)
                continue;
            var ray = new Ray(localEye, seg / len);
            // The wall's rule, term for term: a hit that does not clearly PRECEDE the sample is
            // not an occlusion — unless the sample itself is inside the box, which is a sample
            // buried in the board and hidden whatever the ray says. A hit at distance 0 is the eye
            // inside the box and COUNTS, which is what replaced the short circuit deleted above;
            // there is no `hit <= 0` rejection any more, and Bounds.IntersectRay never reports a
            // negative distance.
            if (!_localBox.IntersectRay(ray, out float hit))
                continue;
            if (hit >= len * (1f - BlockEpsFraction) && !_localBox.Contains(localSample))
                continue;
            blocked++;
        }
        _lastBlocked = blocked;
        return (float)blocked / _lastVisible;
    }

    /// <summary>
    /// Arm the SHORT exit dwell whenever the viewpoint really changed. Three of the four causes
    /// are the shared <see cref="PerspectiveWatch"/>: the rig pose version (a recenter / rig
    /// rebuild), the RIG ROOT moving, turning or being rescaled, and a real head translation of
    /// <see cref="OcclusionFade.HeadMoveReevalMetres"/> TRACKING metres. The fourth is the
    /// board-specific one the wall cannot have, and the one that matters most here because the
    /// user's whole complaint is about a board somebody else is dragging around: the OWNER MOVING
    /// THE BOARD, which reaches the watch through <see cref="PerspectiveWatch.Note"/>.
    /// </summary>
    private void UpdatePerspectiveState(Transform headT, float now)
    {
        // THREE OF THE FOUR CAUSES ARE THE SHARED WATCH (Core/OcclusionFade.cs): the rig pose
        // version, the RIG ROOT moving/turning/rescaling, and real head translation.
        //
        // THE RIG-ROOT CLAUSE IS NEW HERE AND IT IS THE INTERESTING ONE. It was in the wall fade
        // from the start and this file never had it, because the hand-copy took the head-motion
        // clause and stopped. It cannot be derived from the head: WorldGrab zooms and drags by
        // writing the RIG ROOT's position, rotation and scale, and the head camera is a child of
        // that root - so the head's own tracking-space position does not move by a millimetre
        // while the player hauls the whole diorama past his face. Zooming into the board is the
        // single largest viewpoint change this mod offers, and until now a board that stopped
        // occluding right after one waited out the LONG "you only turned your head" dwell.
        //
        // The fourth cause is this file's own and has no wall equivalent: the OWNER MOVING THE
        // BOARD. It matters most of all here, because the user's whole complaint is about a board
        // somebody else is dragging around, and it reaches the shared watch through Note() exactly
        // as the wall's room-bounds shift does.
        _perspective.Tick(headT, now);

        Transform t = transform;
        if (!_boardPoseInit)
        {
            _boardPoseInit = true;
            _lastBoardPos = t.position;
            _lastBoardRot = t.rotation;
        }
        else if ((t.position - _lastBoardPos).sqrMagnitude > 0.0004f
                 || Quaternion.Angle(t.rotation, _lastBoardRot) > 2f)
        {
            _lastBoardPos = t.position;
            _lastBoardRot = t.rotation;
            _perspective.Note(now);
        }
    }

    // ------------------------------------------------------------------- the surface census ----

    /// <summary>
    /// Re-take the board's surface census and its occluder box. Renderers that appeared since the
    /// last scan join (and are swapped immediately when the board is already yielding, so a card
    /// dealt mid-fade does not blink in solid); renderers that went away are restored and dropped.
    ///
    /// <para>The occluder box is built from the board-local extents of the ACTIVE renderers only —
    /// a hidden card occludes nothing — and it deliberately includes the docks, the initiative
    /// mirror and the owner tag, because those are exactly the parts that hang off the board and
    /// eat the view.</para>
    /// </summary>
    private void RefreshSurfaces()
    {
        for (int i = 0; i < _surfaces.Count; i++)
            _surfaces[i].Seen = false;

        Transform root = transform;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        _hasBox = false;
        _rendererScratch.Clear();
        GetComponentsInChildren(true, _rendererScratch);
        AdoptRenderers(_rendererScratch, toLocal, contributeBox: true);

        // The board's floating fans (user items 7 and 11a). Their renderers join the SAME list and
        // are driven by the SAME alpha; their extents deliberately do NOT join the occluder box, or
        // the decision this board is judged by would silently change with what its owner happens to
        // have open. See Follow() for the two membership rules and for why the conditional one is
        // frozen while the board is engaged.
        //
        // THE FREEZE IS THE WHOLE ANTI-FLICKER ARGUMENT and it is one line: while _engaged, a
        // WhileOverBoard root keeps whatever membership it had, because joining or leaving the set
        // mid-fade is a pop either way (a join swaps materials and snaps to the live alpha; a
        // leave calls Restore and snaps to solid). The predicate is only ever asked of a board
        // that is currently solid, where both answers are invisible.
        bool freeze = _engaged;
        _followerScratch.Clear();
        _followersHeld = 0;
        if (FollowerRoots.TryGetValue(_playerId, out List<FollowerEntry> registered))
        {
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                if (registered[i].Root == null)
                    registered.RemoveAt(i); // a fan destroyed with its peer
            }
            for (int i = 0; i < registered.Count; i++)
            {
                FollowerEntry entry = registered[i];
                bool admit = entry.Rule == FollowRule.Always
                    || (freeze ? WasFollowing(entry.Root) : OverBoard(entry.Root));
                if (admit)
                    _followerScratch.Add(entry.Root);
                else
                    _followersHeld++;
            }
        }
        _followers.Clear();
        for (int i = 0; i < _followerScratch.Count; i++)
        {
            Transform f = _followerScratch[i];
            _followers.Add(f);
            _rendererScratch.Clear();
            f.GetComponentsInChildren(true, _rendererScratch);
            AdoptRenderers(_rendererScratch, toLocal, contributeBox: false);
        }
        if (_engaged)
            EnsureGroups();

        for (int i = _surfaces.Count - 1; i >= 0; i--)
        {
            Surface s = _surfaces[i];
            if (s.Seen)
                continue;
            Restore(s);
            if (s.Renderer != null)
                _known.Remove(s.Renderer);
            else
                PruneDeadKeys();
            _surfaces.RemoveAt(i);
        }
        LogCensusIfChanged();
    }

    /// <summary>Was this root in the fade set at the previous census? The freeze's memory — see
    /// the anti-flicker paragraph on <see cref="Follow"/>. A linear scan of at most a handful of
    /// entries, run once per root per 2 Hz census.</summary>
    private bool WasFollowing(Transform root)
    {
        for (int i = 0; i < _followers.Count; i++)
        {
            if (ReferenceEquals(_followers[i], root))
                return true;
        }
        return false;
    }

    /// <summary>
    /// "IS THIS ROOT ONE OF THE FANS IN FRONT OF THE BOARD?" — the <see cref="FollowRule.WhileOverBoard"/>
    /// predicate, and the whole of what keeps user item 11a from silently reclassifying a peer's
    /// hands as board furniture.
    ///
    /// <para>Measured in BOARD-LOCAL space against the board's own occluder box, which this census
    /// has just rebuilt from the live renderers two dozen lines above — so it costs one
    /// <c>InverseTransformPoint</c> and one <c>Bounds.SqrDistance</c>, it means the same thing at
    /// any diorama scale, and it does not depend on where the VIEWER is standing (two clients must
    /// agree about whether a fan belongs to a board, even though they never compare notes).</para>
    ///
    /// <para>THE ROOT'S ORIGIN, NOT ITS RENDERERS' BOUNDS, and that is deliberate. A hand fan's
    /// slabs are laid out fresh every frame — they bow, they toe in, they ease as cards are added
    /// — so a bounds-based test would breathe. The root is the fan's anchor point (the owner's
    /// palm plus their own tuned offset) and it moves only when the hand does.</para>
    ///
    /// <para>THE MARGIN IS THE BOARD'S OWN SIZE, not a tuned number: half the larger of the box's
    /// two footprint dimensions, i.e. "within about a half board-width of the board". A board is
    /// the only object in the expression, so there is nothing for a config key to mean here, and a
    /// dial whose only visible effect is which of two surfaces starts fading half a second earlier
    /// is exactly what the standing no-new-key ruling refuses.</para>
    ///
    /// <para>Answers FALSE while the board has no occluder box at all (a board whose renderers are
    /// all inactive), which is the safe direction: no box means no fade decision either.</para>
    /// </summary>
    private bool OverBoard(Transform? root)
    {
        if (root == null || !_hasBox)
            return false;
        Vector3 local = transform.InverseTransformPoint(root.position);
        float reach = FollowReachFactor * Mathf.Max(_localBox.size.x, _localBox.size.z);
        return _localBox.SqrDistance(local) <= reach * reach;
    }

    /// <summary>
    /// Register one root's renderers as driven surfaces, and — for the BOARD's own root only —
    /// grow the occluder box by their board-local extents.
    ///
    /// <para><paramref name="contributeBox"/> is the whole difference between the board and a
    /// follower, and it is what keeps user item 7 from re-tuning user request 15 behind its back:
    /// a fan is faded WITH the board and is never part of what decides that the board should
    /// fade.</para>
    /// </summary>
    private void AdoptRenderers(List<Renderer> found, in Matrix4x4 toLocal, bool contributeBox)
    {
        for (int i = 0; i < found.Count; i++)
        {
            Renderer r = found[i];
            if (r == null)
                continue;
            if (!_known.TryGetValue(r, out Surface s))
            {
                Delivery kind = HeaviestDelivery(r.sharedMaterials);
                s = new Surface { Renderer = r, Kind = kind, Blendable = kind == Delivery.Blends };
                _known[r] = s;
                _surfaces.Add(s);
                if (_engaged && !s.Blendable)
                    Swap(s);
            }
            s.Seen = true;

            if (!contributeBox || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            Bounds lb = r.localBounds;
            Matrix4x4 m = toLocal * r.localToWorldMatrix;
            Vector3 c = lb.center;
            Vector3 e = lb.extents;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3(
                    c.x + ((k & 1) == 0 ? -e.x : e.x),
                    c.y + ((k & 2) == 0 ? -e.y : e.y),
                    c.z + ((k & 4) == 0 ? -e.z : e.z));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!_hasBox)
                {
                    _hasBox = true;
                    _localBox = new Bounds(p, Vector3.zero);
                }
                else
                {
                    _localBox.Encapsulate(p);
                }
            }
        }
    }

    /// <summary>
    /// Refresh the list of uGUI alpha carriers: one <see cref="CanvasGroup"/> on the board root and
    /// one on each live follower root. A group's alpha multiplies DOWN through every nested canvas,
    /// so the mirrors' and the card faces' own groups are never written to and cannot be fought.
    ///
    /// <para>Called from <see cref="Engage"/> and from the census while engaged, never while the
    /// mode is Off — adding a component to somebody else's object is a write, and "Off is bit for
    /// bit today's behaviour" has to keep meaning that.</para>
    /// </summary>
    private void EnsureGroups()
    {
        _groups.Clear();
        if (_group == null)
            _group = gameObject.GetComponent<CanvasGroup>();
        if (_group == null)
            _group = gameObject.AddComponent<CanvasGroup>();
        _groups.Add(_group);
        for (int i = 0; i < _followers.Count; i++)
        {
            Transform f = _followers[i];
            if (f == null)
                continue;
            CanvasGroup g = f.GetComponent<CanvasGroup>();
            if (g == null)
            {
                g = f.gameObject.AddComponent<CanvasGroup>();
                // A fan is a read-only mirror of somebody else's cards. Its group exists to carry
                // opacity and must not start blocking or accepting input on the way in.
                g.interactable = false;
                g.blocksRaycasts = false;
            }
            _groups.Add(g);
        }
    }

    /// <summary>A destroyed renderer is a Unity-null KEY that <c>Remove</c> can no longer find;
    /// sweep them out wholesale rather than leaving the dictionary to grow across rebuilds.</summary>
    private void PruneDeadKeys()
    {
        _known.Clear();
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (_surfaces[i].Renderer != null)
                _known[_surfaces[i].Renderer] = _surfaces[i];
        }
    }

    // ------------------------------------------------------------------------- the delivery ----

    /// <summary>
    /// Deliver <paramref name="alpha"/> to every driven surface, and — the fade-IN repair, user
    /// item 10 — take the delivery apparatus back down in TWO steps instead of one.
    ///
    /// <para><b>WHAT THE MEASUREMENT ACTUALLY SAID.</b> The reported defect is asymmetric: <i>"Wenn
    /// die Board-Transparenz verschwindet, ploppt es einfach auf statt smooth wieder
    /// nicht-transparent zu werden."</i> The obvious reading — that the ramp is missing on the way
    /// in — is FALSE, and both halves of the arithmetic say so. <see cref="OcclusionFade.Ramp"/>
    /// runs every frame in both directions and its snap epsilon is 0.005 in FADE units, which at
    /// the shipped <c>OccludedAlpha</c> is 0.004 of ALPHA: the opacity is continuous through the
    /// release to within four thousandths. And the ModBuild 351 lighting step is gone on any
    /// current bundle — the census line reports zero unlit-swap renderers, so the slab family keeps
    /// its own shader across the whole ramp.</para>
    ///
    /// <para><b>WHAT IS LEFT, AND IT IS THE ONLY THING LEFT.</b> With the shader kept and the alpha
    /// continuous, the engaged and released states differ in exactly three render-state terms:
    /// render queue (3000 vs the material's own 2000), ZWrite (off vs on) and cull mode (forced
    /// Back vs the material's <c>_Cull = 0</c>). Two of those are invisible at full opacity — a
    /// queue change on a surface that now writes depth, and a back-face mode on a watertight solid
    /// whose near face wins the depth test. <b>ZWrite is not.</b> While it is off, every transparent
    /// surface drawn after the slab composites THROUGH it whatever the depth says — that is the
    /// board's own content, which rides sortingOrder >= 100 precisely so it draws over the slab —
    /// and on the single frame the clone comes off, everything of it that is physically behind the
    /// slab is occluded again. That is a one-frame change in what the board looks like, and on the
    /// fade-IN it is the LAST event of the transition with nothing after it to bury it. On the
    /// fade-OUT the same change lands on the ramp's first step and is followed by 0.6 s of
    /// continuing motion, which is the asymmetry the user is describing.</para>
    ///
    /// <para><b>THE REPAIR IS TO MOVE THE DISCONTINUITY, NOT THE THRESHOLD.</b> Releasing at a
    /// lower alpha — the obvious suggestion — makes it WORSE: the apparatus is what carries the
    /// alpha, so taking it away early trades a render-state step for an opacity step of the whole
    /// remaining ramp. Instead the clone, which we own outright, is put back into its ORIGINAL
    /// depth state at <see cref="SettleAlpha"/> while it is still installed (see
    /// <see cref="SettleDepthState"/>), and the material restore at <see cref="SolidAlpha"/>
    /// becomes a no-op in every observable term: same shader, same depth state, same cull, and
    /// <c>SrcAlpha/OneMinusSrcAlpha</c> at alpha 1 is <c>One/Zero</c>.</para>
    ///
    /// <para><b>AND THE BAND WAS PUT IN THE WRONG PLACE, WHICH IS WHY HE REPORTED IT AGAIN</b>
    /// (2026-09-05: <i>"Das Ploppen wenn die Transparenz aus geht, ist immer noch der Fall"</i>).
    /// 445 set the band at 0.97/0.94 and argued the residual blend there was 3 % of the pixel, "i.e.
    /// below anything the eye can attribute to a step". That sizes the wrong quantity: the slab's
    /// own pixels do not move when ZWrite returns, the CONTENT BEHIND IT disappears, and that
    /// content is at the driven alpha — 0.97 — not at 3 %. The band is 0.55/0.40 now, which halves
    /// the magnitude and, more importantly, puts the step in the middle of a moving ramp on both
    /// sides instead of 30 ms from its end. This is the rule the fail-safe band two paragraphs
    /// below has always followed; the depth band was the one place it was not applied. The other
    /// half of the same user sentence — <i>"alle Controll boards sollen gar keine back-faces
    /// haben"</i> — removes the cull term from this list entirely, because the board material now
    /// ships <c>Cull Back</c> and there is nothing for the settle to hand back.
    /// <see cref="LogReleaseStep"/> prints all five terms on the release frame so the next round
    /// reads which of them moved rather than inferring it from this paragraph.</para>
    /// </summary>
    private void Apply(float alpha)
    {
        if (alpha >= SolidAlpha)
        {
            Release();
            return;
        }
        Engage();

        bool cull = alpha <= CullAlpha;
        _mpb ??= new MaterialPropertyBlock();
        int failSafe = 0;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            Surface s = _surfaces[i];
            Renderer r = s.Renderer;
            if (r == null)
                continue;
            // Nothing to blend WITH: an unblendable material that could not be swapped (no
            // alpha-capable shader in the process at all) is the fail-safe family. It cannot fade,
            // so "fading" it means hiding it, and that step is irreducible — no arrangement of a
            // per-renderer alpha can make an opaque shader translucent. What IS reducible is the
            // step happening twice: the old rule was a bare `alpha < 0.5f`, a hard threshold sitting
            // exactly at the ramp's midpoint, so a decision that reversed near it (the peer nudges
            // their board, the coverage crosses back) toggled the renderer on and off at the frame
            // rate. It is a Schmitt band now, and the band is placed so the one unavoidable step
            // lands EARLY on the fade-out and LATE on the fade-in, i.e. inside the moving picture
            // on both sides rather than at the end of one of them.
            bool blendable = s.Blendable || s.Installed != null;
            if (!blendable && (s.FailSafeHidden ? alpha >= FailSafeShowAlpha
                                                : alpha < FailSafeCullAlpha))
            {
                s.FailSafeHidden = !s.FailSafeHidden;
            }
            if (!blendable && s.FailSafeHidden)
                failSafe++;
            bool off = cull || (!blendable && s.FailSafeHidden);
            if (r.forceRenderingOff != off)
                r.forceRenderingOff = off;
            if (off || !blendable)
                continue;
            // THE DEPTH BAND (user item 10, and the 2026-09-05 repeat of it — see this method's
            // own header). Only the clone's depth state moves here; its blend state, its shader and
            // its alpha are untouched, so a ramp that reverses simply crosses back over the band.
            // The band sits at 0.55/0.40 and NOT near solid: the step's magnitude is the alpha at
            // which it lands, so putting it at 0.97 made it a 97 % step with 30 ms of ramp left.
            if (s.Installed != null)
            {
                if (!s.Settled && alpha >= SettleAlpha)
                    SettleDepthState(s, settled: true);
                else if (s.Settled && alpha < UnsettleAlpha)
                    SettleDepthState(s, settled: false);
            }
            WriteAlpha(r, alpha);
        }
        if (failSafe > 0 && !_loggedFailSafe)
        {
            _loggedFailSafe = true;
            LogFailSafe(failSafe);
        }

        // ONE number, every carrier, this frame. The board root's group and the fans' are written
        // from the same local — see Follow() for why "in dem selben Masse" is a property of the
        // code's shape here rather than of two formulas agreeing.
        for (int i = 0; i < _groups.Count; i++)
        {
            CanvasGroup g = _groups[i];
            if (g != null)
                g.alpha = alpha;
        }
    }

    /// <summary>
    /// The renderer's own live colour with its alpha scaled, through a property block. Re-read
    /// from the material EVERY frame on purpose: that is what makes the fade compose with the
    /// board's own colour animations (cap press, glow pulse) instead of freezing them.
    /// </summary>
    private void WriteAlpha(Renderer r, float alpha)
    {
        Material? m = r.sharedMaterial;
        if (m == null || _mpb == null)
            return;
        _mpb.Clear();
        bool any = false;
        for (int i = 0; i < ColorIds.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            Color c = m.GetColor(ColorIds[i]);
            c.a *= alpha;
            _mpb.SetColor(ColorIds[i], c);
            any = true;
        }
        // GloomhavenVR/BoardLit carries its opacity in a scalar of its own rather than in the tint's
        // alpha channel, deliberately: the alpha CHANNEL of every existing BoardLit material is data
        // nobody has ever read (the shader threw it away), so driving the fade from it would have
        // changed the framebuffer alpha of the hands and the local board as a side effect. NOT a
        // double multiply with the loop above — BoardLit's colour term is alb.rgb, which never sees
        // _Color.a, so the two writes reach different halves of one fragment.
        if (m.HasProperty(FadeAlphaId))
        {
            _mpb.SetFloat(FadeAlphaId, m.GetFloat(FadeAlphaId) * alpha);
            any = true;
        }
        if (any)
            r.SetPropertyBlock(_mpb);
    }

    private void Engage()
    {
        if (_engaged)
            return;
        _engaged = true;
        // One group on the ROOT (and one per follower root): its alpha multiplies down through
        // every nested canvas, so the mirrors' own CanvasGroups (driven from their source widgets)
        // are never written to.
        EnsureGroups();
        int swapped = 0;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            Surface s = _surfaces[i];
            if (s.Blendable || s.Installed != null)
                continue;
            Swap(s);
            if (s.Installed != null)
                swapped++;
        }
        LogDelivery("ENGAGED", $"{swapped} renderer(s) took a private alpha-capable clone", swapped);
    }

    private void Release()
    {
        if (_engaged)
        {
            int cloned = 0;
            int settled = 0;
            var step = default(ReleaseStep);
            for (int i = 0; i < _surfaces.Count; i++)
            {
                if (_surfaces[i].Installed != null)
                    cloned++;
                if (_surfaces[i].Settled)
                    settled++;
                step.Measure(_surfaces[i]);
                Restore(_surfaces[i]);
            }
            _engaged = false;
            LogReleaseStep(in step, cloned, settled);
            LogDelivery("RELEASED", cloned == 0
                ? "no renderer on this board needed a clone at all — every material already blends, "
                  + "so the delivery was nothing but property-block alpha and there was never a "
                  + "render-state step to remove"
                : settled == cloned
                    ? $"all {cloned} clone(s) were already carrying their ORIGINAL depth state when "
                      + $"the ramp reached alpha {SettleAlpha:0.00}, so this restore handed back a "
                      + "surface that had been drawing exactly as the solid one does for the whole "
                      + "top half of the ramp — read RELEASE STEP for what actually moved"
                    : $"only {settled} of {cloned} clone(s) were carrying their original depth "
                      + "state, so this restore ALSO handed back ZWrite in one frame for the rest — "
                      + "that is the fade-in step user item 10 is about, and a board reading like "
                      + "this after an un-fade is the one that still plops",
                settled);
        }
        else
        {
            // Not engaged, but a previous engage may have left flags on renderers that have since
            // been re-registered — cheap and idempotent.
            for (int i = 0; i < _surfaces.Count; i++)
            {
                Renderer r = _surfaces[i].Renderer;
                if (r != null && r.forceRenderingOff)
                    r.forceRenderingOff = false;
            }
        }
        // Every carrier, not just the board's: a follower left at 0.25 while the board went solid
        // would be the same defect this feature is fixing, in the other direction. _group is
        // _groups[0] whenever either is set, so there is nothing else to release.
        for (int i = 0; i < _groups.Count; i++)
        {
            CanvasGroup g = _groups[i];
            if (g != null)
                g.alpha = 1f;
        }
    }

    /// <summary>Install private, alpha-capable clones of the materials on an unblendable
    /// renderer. Never touches a shared asset: the clone is ours, the original array is
    /// remembered verbatim and put back in <see cref="Restore"/>.</summary>
    private void Swap(Surface s)
    {
        Renderer r = s.Renderer;
        if (r == null || s.Installed != null)
            return;
        Material[] originals = r.sharedMaterials;
        if (originals.Length == 0)
            return;
        var installed = new Material[originals.Length];
        bool anyClone = false;
        for (int i = 0; i < originals.Length; i++)
        {
            Material? m = originals[i];
            if (m == null || CanBlend(m))
            {
                installed[i] = m!;
                continue;
            }
            Material clone = MakeTransparentClone(m);
            installed[i] = clone;
            anyClone = ReferenceEquals(clone, m) ? anyClone : true;
        }
        if (!anyClone)
            return; // no alpha-capable shader available: the cull fail-safe takes this renderer
        s.Original = originals;
        s.Installed = installed;
        r.sharedMaterials = installed;
    }

    private void Restore(Surface s)
    {
        Renderer r = s.Renderer;
        if (r != null)
        {
            if (r.forceRenderingOff)
                r.forceRenderingOff = false;
            r.SetPropertyBlock(null);
            if (s.Original != null)
                r.sharedMaterials = s.Original;
        }
        if (s.Installed != null && s.Original != null)
        {
            for (int i = 0; i < s.Installed.Length; i++)
            {
                Material inst = s.Installed[i];
                if (inst != null && !ReferenceEquals(inst, s.Original[i]))
                    Object.Destroy(inst);
            }
        }
        s.Original = null;
        s.Installed = null;
        s.Settled = false;
        s.FailSafeHidden = false;
    }

    /// <summary>
    /// WHERE THE ONE IRREDUCIBLE RENDER-STATE STEP OF A FADE IS ALLOWED TO LAND — the user item 10
    /// repair, corrected on 2026-09-05 after he reported the plop again (read <see cref="Apply"/>'s
    /// header for why this is the term that matters and the release threshold is not, and
    /// <see cref="SettleAlpha"/> for why the band moved from 0.97/0.94 down to 0.55/0.40).
    ///
    /// <para>The clones on this renderer are OURS — nothing else in the process holds a reference
    /// to them — so their depth state can be moved without touching a shared asset, without
    /// re-cloning and without a frame in which the renderer has no material. At
    /// <see cref="SettleAlpha"/> they are put back onto the ORIGINAL material's ZWrite and cull
    /// mode, read from the array this class kept verbatim in <see cref="Surface.Original"/>. The
    /// board then draws for the whole top half of the ramp exactly as it will draw when it is
    /// solid — correct depth, correct back-face rule, its own content occluded where the slab is
    /// in front of it. The material restore that follows is then observably a no-op, and it is
    /// preceded by 0.45 of alpha in that state rather than by 0.03.</para>
    ///
    /// <para><b>WHAT THE STEP ACTUALLY IS, because 445 sized it wrong.</b> A depth write coming
    /// back does not change the slab's own pixels at all — it hides everything drawn AFTER the slab
    /// that is physically behind it. On a control board that is the board's own content, riding
    /// <c>sortingOrder >= 100</c> so it draws over the slab, and that content is carrying the SAME
    /// driven alpha. So the step's magnitude IS the alpha at which this method runs, and putting it
    /// at 0.97 made it a 97 % change of that content with 30 ms of ramp left behind it. The band's
    /// job is not to make the step small enough to miss; it is to make it land where the picture is
    /// still moving on both sides, which is this file's own stated rule for the cull fail-safe.</para>
    ///
    /// <para><b>WHAT IS DELIBERATELY NOT SETTLED: the blend state and the render queue.</b> The
    /// blend must stay <c>SrcAlpha/OneMinusSrcAlpha</c> or the residual would not be drawn at all —
    /// and at alpha 1 that blend IS <c>One/Zero</c> in every channel, so there is nothing to hand
    /// back. The queue stays at <see cref="SwapRenderQueue"/> because moving a still-blending
    /// surface into the OPAQUE queue puts it in a front-to-back pass where the background behind it
    /// may not have been drawn yet: the residual would composite against whatever the frame was
    /// cleared to. Kept in the transparent queue WITH depth writes, it composites against the
    /// finished opaque image, which is right. The queue's own step at the final restore is then
    /// invisible, because by that point the surface is opaque and writes depth either way — and
    /// that "either way" is now guaranteed by half the ramp rather than by the last three per cent.
    /// Both terms are printed by <see cref="LogReleaseStep"/> so they can be RULED OUT from a
    /// reading instead of from this paragraph.</para>
    ///
    /// <para>CULL IS NOW A NO-OP ON BOTH SIDES and is kept only for old bundles and for the
    /// unlit-swap recipe: the board material ships <c>Cull Back</c> since 2026-09-05
    /// (<c>VRCardFactory.EnforceSingleSided</c> enforces it on whatever bundle is loaded), so
    /// <see cref="ForceSingleSided"/> finds nothing to force and the hand-back below writes Back
    /// over Back.</para>
    ///
    /// <para>Reversible, and it has to be: a board that starts fading out again from 0.6 crosses
    /// <see cref="UnsettleAlpha"/> and gets its fade depth state back, which is why this takes a
    /// direction rather than being a one-way finish.</para>
    /// </summary>
    private void SettleDepthState(Surface s, bool settled)
    {
        Material[]? installed = s.Installed;
        Material[]? original = s.Original;
        if (installed == null || original == null)
            return;
        s.Settled = settled;
        for (int i = 0; i < installed.Length && i < original.Length; i++)
        {
            Material inst = installed[i];
            Material orig = original[i];
            // An entry that needed no clone is the original material itself — a shared asset, and
            // the one thing this class must never write.
            if (inst == null || orig == null || ReferenceEquals(inst, orig))
                continue;
            if (inst.HasProperty(ZWriteId))
            {
                inst.SetInt(ZWriteId, settled
                    ? (orig.HasProperty(ZWriteId) ? Mathf.RoundToInt(orig.GetFloat(ZWriteId)) : 1)
                    : 0);
            }
            if (!inst.HasProperty(CullId))
                continue;
            if (settled)
            {
                if (orig.HasProperty(CullId))
                    inst.SetFloat(CullId, orig.GetFloat(CullId));
            }
            else
            {
                ForceSingleSided(inst);
            }
        }
    }

    /// <summary>
    /// A private clone of <paramref name="m"/> that actually blends. Two recipes, and which one
    /// runs is now the difference between a fade the user calls an animation and one he calls a
    /// plop — see <see cref="Deliver"/> for the classification and the note below for why.
    /// Returns the input unchanged when no alpha-capable shader exists in the process at all.
    ///
    /// <para><b>THE STATE FLIP IS ALWAYS PREFERRED, BECAUSE IT KEEPS THE SHADER.</b> A clone that
    /// keeps <c>GloomhavenVR/BoardLit</c> keeps its baked studio lighting, its opt-in specular and
    /// its <c>Cull [_Cull]</c>, so nothing about how the surface READS changes when the clone goes
    /// on or comes off — only its opacity moves, which is the entire point. A clone on a different
    /// shader cannot: every alpha-capable shader in reach is UNLIT, so installing one steps the
    /// slab from <c>alb * (_Ambient + key + fill)</c> to flat <c>alb</c> in a single frame. With
    /// the shipped board material (<c>_Ambient</c> 0.5, <c>_LightBoost</c> 0.85, <c>_SpecStrength</c>
    /// 0.85) that factor is about x1.27 on the board's readable FRONT and x0.5 on its BACK plate,
    /// plus the whole specular lobe appearing or vanishing. On the way OUT that step lands at alpha
    /// ~1 and is instantly buried under 0.36 s of fading; on the way IN it is the LAST event of the
    /// transition with nothing after it. That asymmetry is the reported defect — "beim Transparent
    /// machen faded es in ner Animation aus, anders rum aber nicht, da ploppt das board ploetzlich
    /// auf" — and the state flip is the only thing that removes it rather than moving it.</para>
    ///
    /// <para><b>WHICH RECIPE BOARDLIT TAKES IS DECIDED BY THE BUNDLE THAT IS LOADED, NOT BY A
    /// CONFIG KEY.</b> A bundle baked at ModBuild 351 or later ships a BoardLit with
    /// <c>_SrcBlend</c>/<c>_DstBlend</c>/<c>_ZWrite</c>/<c>_FadeAlpha</c> (all defaulted so an
    /// un-flipped material is bit-identical), and the slab family takes the state flip. Against the
    /// bundle shipped at ModBuild 350 those properties do not exist, the family takes the swap, and
    /// the fade is exactly as good as it was except for the two repairs below — which is what makes
    /// this a DLL-only drop that is a strict improvement, with the lighting step as the one thing a
    /// re-bake buys.</para>
    ///
    /// <para><b>THE SWAP TARGET CHANGED, AND BOTH HALVES OF THAT MATTER.</b> It was a bare
    /// <c>Shader.Find("Sprites/Default")</c>; it is now <c>GloomhavenVR/Overlay</c> through
    /// <see cref="BundleShaders"/>, with <c>Sprites/Default</c> kept only as the last resort.
    /// <list type="bullet">
    /// <item>RESOLUTION. <c>Shader.Find</c> only sees shaders something has already LOADED
    ///   (Core/BundleShaders.cs, a trap that has cost this project two builds). When it returned
    ///   null the clone was never installed, the renderer stayed classified unblendable, and
    ///   <see cref="Apply"/> put it on the <c>alpha &lt; 0.5</c> CULL fail-safe instead — which on a
    ///   fade-IN means that family stays hidden for 83 ms after the rest of the board is already
    ///   back, i.e. one board arriving in two instalments. <c>BundleShaders</c> loads the asset out
    ///   of the open bundle by path, so it does not depend on anything else having asked first.</item>
    /// <item>CULLING. <c>Sprites/Default</c> is <c>Cull Off</c> and <c>ZWrite Off</c>, hard-coded.
    ///   Every board material USED TO ship <c>_Cull = 0</c> as well (a leftover from the pre-rebuild
    ///   AI shells); since 2026-09-05 <c>BuildBoard.cs</c> bakes <c>Cull Back</c> and
    ///   <c>VRCardFactory.EnforceSingleSided</c> enforces it on whatever bundle is loaded, so this
    ///   paragraph now only describes the fallback shader. On an OPAQUE board that is invisible, because the near face wins
    ///   the depth test. On a FADING one it is the whole of user item 4b: with no back-face culling
    ///   and no depth write, the board's own interior and its back plate composite through the front
    ///   for the entire ramp — "wenn man reinschaut muss man nicht das innere sehen koennen" — and
    ///   they stop doing so on the single frame the clone comes off, which is the second, later
    ///   event the user saw ("und die Rueckseite des boards etwas verzoegert"). Both recipes now
    ///   force <see cref="ForceSingleSided"/>, so the interior is never drawn in any state and there
    ///   is no back-face event left to lag.</item>
    /// </list></para>
    /// </summary>
    private static Material MakeTransparentClone(Material m)
    {
        if (Deliver(m) == Delivery.SwapShader)
        {
            Shader? target = BundleShaders.Resolve(
                "GloomhavenVR/Overlay", "Net",
                "the peer-board see-through can fade an opaque board surface on a SINGLE-SIDED, "
                + "depth-tested, alpha-blended clone",
                "the peer-board see-through falls back to the built-in Sprites/Default, which is "
                + "Cull Off — so a fading board shows its own interior — and resolves only if "
                + "something else in the process has already loaded it");
            target ??= Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (target == null)
                return m;
            // Read the look BEFORE the swap — property ids resolve against the current shader.
            Texture? tex = m.HasProperty(MainTexId) ? m.GetTexture(MainTexId)
                : m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : null;
            Color tint = Color.white;
            for (int i = 0; i < ColorIds.Length; i++)
            {
                if (!m.HasProperty(ColorIds[i]))
                    continue;
                tint = m.GetColor(ColorIds[i]);
                break;
            }
            var swapped = new Material(target) { name = m.name + CloneMarker, color = tint };
            if (tex != null)
                swapped.mainTexture = tex;
            // GloomhavenVR/Overlay ships two-sided, depth-less and additive-capable because its
            // other callers are HUD plates and figure glows. A faded board slab is none of those:
            // it is ordinary geometry that has to keep looking like geometry.
            if (swapped.HasProperty(ZTestId))
                swapped.SetInt(ZTestId, (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (swapped.HasProperty(SrcBlendId))
                swapped.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (swapped.HasProperty(DstBlendId))
                swapped.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (swapped.HasProperty(ZWriteId))
                swapped.SetInt(ZWriteId, 0);
            // The board mesh is not authored FOR this shader, so its colour stream — whatever the
            // FBX happens to carry — must not multiply into the tint. Overlay's own doc states the
            // failure mode: a mesh that bakes a mask into its vertex colours multiplies the clone
            // to nothing and paints no pixels at all.
            if (swapped.HasProperty(VertexColorId))
                swapped.SetFloat(VertexColorId, 0f);
            ForceSingleSided(swapped);
            swapped.renderQueue = SwapRenderQueue;
            return swapped;
        }

        var clone = new Material(m) { name = m.name + CloneMarker };
        if (clone.HasProperty(ModeId))
            clone.SetFloat(ModeId, 2f); // Standard: 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
        if (clone.HasProperty(SurfaceId))
            clone.SetFloat(SurfaceId, 1f); // URP Lit/Unlit: 0 Opaque, 1 Transparent
        if (clone.HasProperty(SrcBlendId))
            clone.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (clone.HasProperty(DstBlendId))
            clone.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (clone.HasProperty(ZWriteId))
            clone.SetInt(ZWriteId, 0);
        clone.DisableKeyword("_ALPHATEST_ON");
        clone.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        clone.EnableKeyword("_ALPHABLEND_ON");
        clone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ForceSingleSided(clone);
        if (clone.renderQueue < SwapRenderQueue)
            clone.renderQueue = SwapRenderQueue;
        return clone;
    }

    /// <summary>
    /// USER ITEM 4b, and it is one line because the board's geometry was never the problem.
    /// "Die boards benoetigen keine backfaces (wenn man reinschaut muss man nicht das innere sehen
    /// koennen)."
    ///
    /// <para>The board mesh is a watertight, outward-wound solid with a real exterior back plate —
    /// measured, not assumed: <c>gen_winding.py</c> reports 0 inward-wound faces and positive signed
    /// volume on all three styles, and <c>BOARD-CONTRACT.md</c> requires 0 hole loops and 0
    /// non-manifold edges. So no mesh change is needed for the board as it is DRAWN. What used to
    /// reveal the interior is that the material said <c>_Cull = 0</c> — vestigial, inherited from
    /// the pre-rebuild photogrammetry shells that really did have holes — and that a transparent
    /// surface has no depth test to hide the far side behind the near one.</para>
    ///
    /// <para>THE ORIGINAL IS SINGLE-SIDED NOW TOO (2026-09-05, the user's <i>"alle Controll boards
    /// sollen gar keine back-faces haben"</i>), so on a current bundle this method finds nothing to
    /// force and cull cannot change at any point of a fade — which also deletes one of the two
    /// render-state flips <see cref="SettleDepthState"/> used to make in the same frame. It is kept
    /// because the fallback swap target (<c>Sprites/Default</c>) is hard-coded <c>Cull Off</c> and
    /// because a board whose material somehow arrives two-sided must still not show its inside.</para>
    ///
    /// <para>Only <c>Off</c> is corrected. A clone whose original already culls Front is a
    /// deliberate inside-out surface and stays one.</para>
    /// </summary>
    private static void ForceSingleSided(Material clone)
    {
        if (!clone.HasProperty(CullId))
            return;
        if (Mathf.Approximately(clone.GetFloat(CullId), (float)UnityEngine.Rendering.CullMode.Off))
            clone.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Back);
    }

    /// <summary>How one material's alpha has to be delivered.</summary>
    private enum Delivery
    {
        /// <summary>It ALREADY blends as authored — a property-block alpha reaches the screen and
        /// nothing has to be cloned.</summary>
        Blends,
        /// <summary>Its shader CAN blend but this material is configured opaque: clone it and flip
        /// the blend state, keeping the shader and therefore the lighting and the cull mode.</summary>
        FlipState,
        /// <summary>Its shader cannot blend at all (no blend state in the pass): the material has
        /// to be cloned onto a different, alpha-capable shader.</summary>
        SwapShader,
    }

    /// <summary>
    /// How <paramref name="m"/>'s alpha must be delivered.
    ///
    /// <para>THE FIRST TERM IS THE CONFIGURED STATE, NOT THE EXPOSED KNOBS, AND THAT IS THE
    /// CORRECTION (ModBuild 351). This used to be one boolean, <c>CanBlend</c>, that answered
    /// "does the shader expose a blend switch?" and treated YES as "this material blends". Those
    /// are different questions — the project's own "a capability test is not a policy" — and the
    /// gap between them is not academic: a Standard material sitting in <c>_Mode = Opaque</c>
    /// exposes <c>_Mode</c>, <c>_SrcBlend</c>, <c>_DstBlend</c> and <c>_ZWrite</c>, was therefore
    /// classified as blendable, was never cloned, and had a property-block alpha written into it
    /// every frame that its opaque blend state discarded. Such a surface did not fade at all, in
    /// either direction, and nothing in the log said so.</para>
    ///
    /// <para>IT ALSO HAD TO CHANGE BEFORE THE SHADER DID. <c>GloomhavenVR/BoardLit</c> now exposes
    /// <c>_SrcBlend</c>/<c>_DstBlend</c> (defaulted to <c>One</c>/<c>Zero</c>, i.e. opaque). Under
    /// the old test that alone would have re-classified every board slab, keycap and handle bar as
    /// "blendable", stopped the clone from being installed, and left the whole see-through
    /// delivering nothing but a discarded alpha write — a shader improvement that silently
    /// switches the feature off. The transparent QUEUE is the one signal that says what a material
    /// actually does rather than what its shader could be made to do.</para>
    /// </summary>
    private static Delivery Deliver(Material m)
    {
        if (m.renderQueue > 2500)
            return Delivery.Blends;
        if (m.HasProperty(ModeId) || m.HasProperty(SurfaceId)
            || (m.HasProperty(SrcBlendId) && m.HasProperty(DstBlendId)))
            return Delivery.FlipState;
        return Delivery.SwapShader;
    }

    /// <summary>Does a property-block alpha reach the screen on this material as it stands?</summary>
    private static bool CanBlend(Material m) => Deliver(m) == Delivery.Blends;

    private static bool CanBlendAll(Material[] materials) =>
        HeaviestDelivery(materials) == Delivery.Blends;

    /// <summary>The most work any one material on this renderer needs. A renderer is treated as a
    /// unit because <c>sharedMaterials</c> is swapped as a unit.</summary>
    private static Delivery HeaviestDelivery(Material[] materials)
    {
        Delivery worst = Delivery.Blends;
        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m == null)
                continue;
            Delivery d = Deliver(m);
            if (d > worst)
                worst = d;
        }
        return worst;
    }

    // ------------------------------------------------------------------------ diagnostics ------

    /// <summary>
    /// THE RENDER STATE ON BOTH SIDES OF THE RELEASE INSTANT, read off the two materials rather
    /// than derived from the flags that decided them.
    ///
    /// <para>WHY IT HAD TO EXIST. Two rounds have now been spent on "the un-fade still pops", and
    /// both of them INFERRED the step from the code: 445 enumerated three render-state terms from
    /// <c>MakeTransparentClone</c> and argued two of them invisible. Nothing in the log could
    /// contradict that enumeration, because nothing in the log contained a render state. Every
    /// term below is read from <c>Material</c> A (the clone, i.e. the frame BEFORE the restore) and
    /// <c>Material</c> B (the original, i.e. the frame AFTER it), on the release frame, so the next
    /// round reads a step instead of arguing one.</para>
    /// </summary>
    private struct ReleaseStep
    {
        public int Clones;
        public int ZWriteDiff;
        public int CullDiff;
        public int BlendDiff;
        public int QueueDiff;
        public int ShaderDiff;
        /// <summary>The first differing pair, in words, so a non-zero count above names something.</summary>
        public string? Worst;

        public void Measure(Surface s)
        {
            Material[]? installed = s.Installed;
            Material[]? original = s.Original;
            if (installed == null || original == null)
                return;
            for (int i = 0; i < installed.Length && i < original.Length; i++)
            {
                Material a = installed[i], b = original[i];
                if (a == null || b == null || ReferenceEquals(a, b))
                    continue;
                Clones++;
                if (a.shader != b.shader)
                {
                    ShaderDiff++;
                    Worst ??= $"shader '{a.shader.name}' -> '{b.shader.name}' on '{b.name}'";
                }
                if (a.renderQueue != b.renderQueue)
                {
                    QueueDiff++;
                    Worst ??= $"queue {a.renderQueue} -> {b.renderQueue} on '{b.name}'";
                }
                if (Differs(a, b, ZWriteId))
                {
                    ZWriteDiff++;
                    Worst ??= $"ZWrite {Read(a, ZWriteId):0} -> {Read(b, ZWriteId):0} on '{b.name}'";
                }
                if (Differs(a, b, CullId))
                {
                    CullDiff++;
                    Worst ??= $"cull {Read(a, CullId):0} -> {Read(b, CullId):0} on '{b.name}'";
                }
                if (Differs(a, b, SrcBlendId) || Differs(a, b, DstBlendId))
                {
                    BlendDiff++;
                    Worst ??= $"blend {Read(a, SrcBlendId):0}/{Read(a, DstBlendId):0} -> " +
                              $"{Read(b, SrcBlendId):0}/{Read(b, DstBlendId):0} on '{b.name}'";
                }
            }
        }

        private static float Read(Material m, int id) => m.HasProperty(id) ? m.GetFloat(id) : -1f;

        private static bool Differs(Material a, Material b, int id) =>
            (a.HasProperty(id) || b.HasProperty(id))
            && !Mathf.Approximately(Read(a, id), Read(b, id));
    }

    /// <summary>
    /// ONE line on the frame the clones come off, naming every render-state term that MOVED and
    /// every one that did not. This is the line a repeat of <i>"das Ploppen wenn die Transparenz
    /// aus geht"</i> is judged against, and it is deliberately a MEASUREMENT of both sides rather
    /// than a restatement of the decision that produced them.
    ///
    /// <para><b>HOW TO READ IT.</b> <c>ZWrite 0</c> and <c>cull 0</c> are the answer: the depth
    /// band (<see cref="SettleAlpha"/>/<see cref="UnsettleAlpha"/>) has had the clone in its
    /// original depth state since alpha 0.55, and the board material now ships <c>Cull Back</c>
    /// (<c>VRCardFactory.EnforceSingleSided</c>), so neither can move here. <c>blend</c> and
    /// <c>queue</c> are EXPECTED to be non-zero and are the two terms this class deliberately does
    /// not settle — <c>SrcAlpha/OneMinusSrcAlpha</c> at alpha 1 is <c>One/Zero</c> in every
    /// channel, and a queue change on a surface that is already writing depth reorders nothing —
    /// so they are printed to be RULED OUT, not to be fixed. <c>shader</c> non-zero means the
    /// unlit-swap recipe is in play and the bundle is older than ModBuild 351; that one IS a
    /// visible step and the census line says how many renderers it covers.</para>
    ///
    /// <para><b>THE FALSIFIER, stated so silence cannot be read as success.</b> In a session where
    /// no peer board ever fades, this line NEVER APPEARS — no engage, no release, nothing to
    /// measure. Absence therefore proves nothing about the pop. The line that says the subsystem
    /// was alive at all is <c>see-through census</c> (printed on the first census of every board,
    /// faded or not); the line that says a transition happened is <c>see-through delivery</c>. So
    /// the only reading that means "the fix holds" is: census present, delivery RELEASED present,
    /// and this line present with <c>ZWrite 0, cull 0, shader 0</c>. Census present with no
    /// RELEASED means the board never fell far enough to fade, and the item is untested.</para>
    /// </summary>
    private void LogReleaseStep(in ReleaseStep step, int cloned, int settled)
    {
        if (step.Clones == 0)
            return;
        // HW-VERIFY
        VRLog.Note("Net", $"Peer board [{_playerId}] RELEASE STEP at alpha {_drivenAlpha:0.000}: " +
            $"{step.Clones} clone/original material pair(s) compared on the release frame — " +
            $"ZWrite {step.ZWriteDiff}, cull {step.CullDiff}, shader {step.ShaderDiff}, " +
            $"blend {step.BlendDiff}, queue {step.QueueDiff} differing; " +
            $"{settled} of {cloned} clone(s) were in their ORIGINAL depth state" +
            (step.Worst == null ? "" : $"; first difference: {step.Worst}") + ". THE FRAME BEFORE " +
            "is the clone's state, THE FRAME AFTER is the original's, both read off the materials " +
            "here rather than off the flags that set them. ZWrite, cull and shader must all read " +
            "0: the depth band settles at alpha 0.55 with half the ramp still to run, the board " +
            "material is Cull Back on both sides, and a lit-clone keeps its shader. blend and " +
            "queue are EXPECTED non-zero and are not defects — SrcAlpha/OneMinusSrcAlpha at " +
            "alpha 1 is One/Zero, and a queue move on a surface that already writes depth " +
            "reorders nothing. PURELY LOCAL: nothing here touched the owner's board or the wire.");
    }

    /// <summary>How many driven surfaces currently carry a clone that has been put back into its
    /// ORIGINAL depth state (<see cref="SettleDepthState"/>). Diagnostic only.</summary>
    private int SettledCount()
    {
        int n = 0;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (_surfaces[i].Settled)
                n++;
        }
        return n;
    }

    /// <summary>
    /// ONE line per DELIVERY transition — engage, release, reset — and the instrumentation hole
    /// that made user item 10 cost a hardware round to find.
    ///
    /// <para><b>THERE WAS NO LINE HERE AT ALL.</b> <see cref="Engage"/>, <see cref="Release"/>,
    /// <see cref="Restore"/> and the old <c>ResetToSolid</c> wrote nothing, so in a capture an
    /// instantaneous snap back to solid and a 0.6 s ramp back to solid produced byte-identical
    /// evidence: one state line saying OFF, and then silence. Every question about the SHAPE of a
    /// transition — which is the entire complaint — was unanswerable from the log, and the round
    /// was spent inferring the shape from the code instead of reading it.</para>
    ///
    /// <para>What each line settles:</para>
    /// <list type="bullet">
    /// <item>ENGAGED / RELEASED come in pairs. A RELEASED with no ENGAGED before it is a board
    ///   that was handed back without ever having been taken.</item>
    /// <item>RELEASED reports how many clones were carrying their ORIGINAL depth state
    ///   (<see cref="SettleDepthState"/>). A release with 0 of them on a board that has clones is a
    ///   release the ramp got to before the depth band did, and the one-frame ZWrite hand-back is
    ///   still in the picture. THIS COUNT IS NOT THE ANSWER ON ITS OWN, and the 447 capture is why:
    ///   every RELEASED line in it read "all N", the repair had run on every release, and the user
    ///   reported the plop again — a flag saying the depth state was handed back EARLY says nothing
    ///   about how early. <see cref="LogReleaseStep"/> is the line that measures it.</item>
    /// <item>RESET names its cause in words — the mode dial, a lost head camera, the board root
    ///   being deactivated by the [Net] gate, a destroy — and says whether the board got there over
    ///   the ramp or instantly. The two instant ones are structural (a deactivated or destroyed
    ///   object has no frames to ramp in) and say so.</item>
    /// </list>
    /// <para><c>Note</c>, with the marker, for the reason the state line is: the observer in a
    /// multiplayer round is a co-player running at the shipped default level. Bounded by the same
    /// hysteresis — a transition is a seconds-scale event, not a frame-scale one.</para>
    /// </summary>
    private void LogDelivery(string what, string why, int count)
    {
        // HW-VERIFY
        VRLog.Note("Net", $"Peer board [{_playerId}] see-through delivery {what} at alpha " +
            $"{_drivenAlpha:0.000} (fade {_fade:0.000}, decision {(_gate.Latched ? "ON" : "OFF")}, " +
            $"mode {PeerBoardFadeTuning.Mode}, {_surfaces.Count} driven surface(s), {count} " +
            $"affected): {why}. The delivery is the apparatus — private material clones, their " +
            "depth state, the render queue and the CanvasGroups — and it is the only part of a " +
            "fade that cannot be ramped, so WHEN it moves is what decides whether a transition " +
            "reads as an animation or as a step. PURELY LOCAL: nothing here touched the owner's " +
            "board or went on the wire.");
    }

    /// <summary>
    /// The cull fail-safe fired: at least one renderer on this board has no alpha-capable shader
    /// available anywhere in the process, so it cannot fade and is hidden instead. Documented as
    /// "never observed" since the feature shipped — which is a claim no capture could have
    /// falsified, because nothing said so. One line per board per session.
    /// </summary>
    private void LogFailSafe(int count)
    {
        // HW-VERIFY
        VRLog.Alert("Net", $"Peer board [{_playerId}] see-through CULL FAIL-SAFE: {count} of " +
            $"{_surfaces.Count} renderer(s) could not be given an alpha-capable material clone at " +
            "all (neither GloomhavenVR/Overlay nor Sprites/Default nor UI/Default resolved), so " +
            "they are HIDDEN while the board yields instead of fading. That step cannot be " +
            "smoothed — an opaque shader has no opacity to ramp — but it is now inside a Schmitt " +
            "band (hide below alpha 0.50, show again at 0.80) rather than on a bare midpoint " +
            "threshold. The gain is that the step happens ONCE PER TRANSITION instead of once per " +
            "ramp reversal near the midpoint, and that on the way back in it lands with a fifth " +
            "of the ramp still to run rather than at the end of it. If this line appears, the " +
            "bundle's shader set is the thing to look at, not this driver.");
    }

    /// <summary>
    /// ONE line per real state flip, naming the peer, the test, the margin it crossed, the alpha
    /// it is being driven to and how long the dwell held it. This is the line a "the board still
    /// blocks my view" / "the board keeps flickering" report is answered against: if it never
    /// appears the board was never JUDGED occluding (read the coverage in the 2 s diag), and if it
    /// appears in pairs seconds apart the dwells are too short for that pose.
    ///
    /// <para><b>IT WAS <c>VRLog.Info</c>, AND THAT MADE IT INVISIBLE ON THE ONE MACHINE IT HAS TO
    /// BE READ ON.</b> <c>Info</c> maps to the DEBUG tier, which a shipped build does not print, so
    /// every hardware round in which the co-player is the observer came back with no state line at
    /// all — the exact failure <c>scripts/check-hw-verify.py</c> was written for, one subsystem
    /// over. It is <c>Note</c> now and carries the marker. The cost is bounded by the hysteresis
    /// that is the point of this class: the two shipped hardware captures hold 29 and 31 flips for
    /// a whole session.</para>
    /// </summary>
    private void LogStateIfChanged(float alpha, float on, float off, float dwell, bool armed)
    {
        bool state = _gate.Latched;
        if (_loggedStateInit && state == _loggedState)
            return;
        _loggedStateInit = true;
        _loggedState = state;
        // HW-VERIFY
        VRLog.Note("Net", $"Peer board [{_playerId}] see-through {(state ? "ON" : "OFF")} " +
            $"({PeerBoardFadeTuning.Mode}) — occlusion test: oriented board box vs the head→" +
            $"play-field segments, {_lastBlocked}/{_lastVisible} in-view sample(s) blocked, raw " +
            $"{_lastRaw:0.000}, smoothed {_gate.Smooth:0.000} vs the {(state ? on : off):0.00} " +
            $"bar it just crossed (margin {Mathf.Abs(_gate.Smooth - (state ? on : off)):0.000}); " +
            $"driving alpha {alpha:0.00} over ~{FadeTauSeconds * 3f:0.00}s. Hysteresis held it for " +
            $"{(state ? EnterDwellSeconds : dwell):0.0}s of continuous agreement " +
            $"({(armed ? "perspective recently changed" : "head only rotating")}). " +
            "PURELY LOCAL — the owner's board is untouched and nothing went on the wire.");
        LogTransparencySet(state, alpha);
    }

    /// <summary>
    /// THE "ENTWEDER ALLES ODER NICHTS" READING (user item 10, 2026-09-06, verbatim: <i>"wird das
    /// Board transparent, soll alles mit transparent werden, also entweder alles oder nichts —
    /// nicht so eine Mischung"</i>).
    ///
    /// <para>A peer's board and every surface mounted on it are ONE transparency state. That is a
    /// rule about a SET, so the instrument reports the SET: the alpha this driver is driving, and
    /// beside it the alpha every carrier's renderers actually RESOLVE to. It does not leave a
    /// reader to compare two numbers — it counts the surfaces whose resolved alpha differs from
    /// the board's and says, in those words, that they are the Mischung the rule forbids.</para>
    ///
    /// <para><b>WHAT "RESOLVED" MEANS, because it is not the same as "written".</b>
    /// <see cref="Apply"/> writes the driven alpha only to surfaces that can carry it. A surface
    /// that is <c>forceRenderingOff</c> resolves to 0 (the cull, or the fail-safe on a material no
    /// alpha-capable shader could be installed on). A surface that is neither blendable NOR
    /// clone-carrying and is NOT hidden resolves to <b>1.000</b> — it is being written an alpha its
    /// blend state discards, so it draws SOLID in front of a see-through board, and it is exactly
    /// the picture the user photographed. Those are the two ways a member of the set can leave it
    /// without anybody being told.</para>
    ///
    /// <para><b>AND THE THIRD WAY, WHICH IS NOT A SURFACE AT ALL.</b> A registered
    /// <see cref="FollowRule.WhileOverBoard"/> root its owner is not currently holding over the
    /// board is HELD OUT of the census, so none of its renderers appear above at any alpha: it
    /// draws solid and this line names it separately, by root name, rather than letting it vanish
    /// into a count. A surface that is on the board and registered NOWHERE cannot be seen from
    /// here at all — that is the blind spot, and it is stated below rather than left implicit.</para>
    ///
    /// <para>Cadence: this rides <see cref="LogStateIfChanged"/>, i.e. one line per real Schmitt
    /// flip. The two shipped hardware captures hold 29 and 31 flips for a whole session, so the
    /// cost is two lines per flip and nothing per frame.</para>
    /// </summary>
    private void LogTransparencySet(bool seeThrough, float alpha)
    {
        float board = Mathf.Clamp01(alpha);
        int drivenSolid = 0;   // written an alpha their blend state throws away → they draw SOLID
        int hidden = 0;        // forceRenderingOff: the cull, or the unblendable fail-safe
        int agree = 0;
        string? firstOffender = null;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            Surface s = _surfaces[i];
            Renderer r = s.Renderer;
            if (r == null)
                continue;
            if (r.forceRenderingOff)
            {
                hidden++;
                continue;
            }
            if (s.Blendable || s.Installed != null)
            {
                agree++;
                continue;
            }
            drivenSolid++;
            firstOffender ??= $"'{r.name}' (material '{(r.sharedMaterial != null ? r.sharedMaterial.name : "<none>")}', "
                              + $"queue {(r.sharedMaterial != null ? r.sharedMaterial.renderQueue : -1)}, "
                              + $"delivery {s.Kind})";
        }

        var carriers = new System.Text.StringBuilder(256);
        carriers.Append("board root '").Append(name).Append("'");
        for (int i = 0; i < _followers.Count; i++)
        {
            Transform f = _followers[i];
            if (f != null)
                carriers.Append(" + follower '").Append(f.name).Append("'");
        }

        var heldOut = new System.Text.StringBuilder(64);
        int held = 0;
        if (FollowerRoots.TryGetValue(_playerId, out List<FollowerEntry> registered))
        {
            for (int i = 0; i < registered.Count; i++)
            {
                Transform root = registered[i].Root;
                if (root == null || WasFollowing(root))
                    continue;
                held++;
                if (held <= 3)
                    heldOut.Append(held > 1 ? ", " : string.Empty).Append('\'').Append(root.name).Append('\'');
            }
        }

        // HW-VERIFY: user item 10 — one transparency state for a peer's board and everything on it.
        VRLog.Note("Net", $"PEER BOARD TRANSPARENCY SET [player {_playerId}]: the board is now "
            + (seeThrough ? "SEE-THROUGH" : "SOLID")
            + $" and DRIVING alpha {board:0.000} ({PeerBoardFadeTuning.Mode}) onto {carriers} — "
            + $"{_surfaces.Count} renderer(s) plus {_groups.Count} CanvasGroup carrier(s), all off "
            + $"this one number. RESOLVED: {agree} surface(s) resolve to {board:0.000} — the "
            + $"board's own — and {hidden} to 0.000 (culled or fail-safe hidden, which is the one "
            + "irreducible step on a material no alpha-capable shader could be installed on). "
            + (drivenSolid == 0
                ? "NO surface resolves to anything else, so the board and everything mounted on it "
                  + "are one transparency state."
                : $"{drivenSolid} surface(s) RESOLVE TO 1.000 WHILE THE BOARD IS AT {board:0.000} — "
                  + "they are being written an alpha their blend state discards, so they draw SOLID "
                  + "in front of a see-through board. THAT IS THE MISCHUNG user item 10 forbids and "
                  + $"this line is the defect, not a warning about one. First: {firstOffender}.")
            + (held == 0
                ? " No registered root is held out of the set."
                : $" SEPARATELY: {held} registered root(s) are HELD OUT ({heldOut}) — a "
                  + "WhileOverBoard follower whose owner is not holding it over the board right "
                  + "now. Their renderers are in NO count above and they draw solid; that is "
                  + "correct only while the root really is somewhere else.")
            + " WORKING = one line per flip with 0 surface(s) at 1.000 and the agree count equal to "
            + $"{_surfaces.Count} minus the hidden count. INERT = this line never appears in a "
            + "session in which 'see-through ON' does, which means the flip logger ran and this did "
            + "not. STILL BEYOND THE INSTRUMENT = a surface drawn over this board that is neither a "
            + "child of the board root nor a registered follower: it is in no count here at any "
            + "alpha, so a photograph of a solid card over a faded board WITH this line reading 0 "
            + "at 1.000 means the surface is not in the set at all — read the census's follower "
            + "count, not this line.");
    }

    /// <summary>Throttled state line while a board is yielding — the numbers that say WHY it is
    /// where it is, so a mis-judged board can be diagnosed without a screenshot. Off under
    /// <c>PerfConfig.Quiet</c>, exactly like the wall's own 2 Hz sweep.</summary>
    private void LogDiagnostic(float alpha, float onFraction, float offFraction, bool reevalArmed)
    {
        VRLog.Info("Net", $"Peer board [{_playerId}] see-through diag: state {(_gate.Latched ? "ON" : "OFF")}, " +
            $"fade {_fade:0.00} → alpha {alpha:0.00}; coverage raw {_lastRaw:0.000} smoothed " +
            $"{_gate.Smooth:0.000} ({_lastBlocked}/{_lastVisible} of {PeerBoardPlayArea.Count} play-field " +
            $"samples in view); bars on {onFraction:0.00} / off {offFraction:0.00}; " +
            $"{(reevalArmed ? "short" : "long")} exit dwell armed; box (board-local) " +
            $"{_localBox.size.x:0.00}×{_localBox.size.y:0.00}×{_localBox.size.z:0.00} m; " +
            $"{_surfaces.Count} surface(s) driven, {SettledCount()} clone(s) already back in " +
            $"their original depth state; {_followers.Count} follower root(s), {_followersHeld} " +
            "held out.");
    }

    /// <summary>
    /// One line per real change of the surface census: how many renderers this board offers, how
    /// they split across the three delivery recipes, and how many FOLLOWER roots (the floating
    /// item / burned / discard fans) are being faded with it.
    ///
    /// <para>THIS IS THE LINE THE NEXT HARDWARE ROUND HAS TO ANSWER TWO QUESTIONS FROM, which is
    /// why it is <c>Note</c> and not <c>Info</c> — a co-player runs at the shipped default level,
    /// and an Info line there is not printed at all:</para>
    /// <list type="bullet">
    /// <item>"lit-clone" vs "unlit-swap" says whether the running BUNDLE carries the ModBuild 351
    ///   BoardLit. lit-clone means the slab keeps its shader across the whole ramp and the fade-in
    ///   cannot step in brightness. unlit-swap means the bundle is the ModBuild 350 one and the
    ///   x1.27-on-the-front / x0.5-on-the-back lighting step at the end of the fade-in is still
    ///   there — i.e. "the board still plops" is EXPECTED and is a re-bake, not a code round.</item>
    /// <item>A follower count of 0 on a peer whose item fan is visibly still solid says the
    ///   registration never happened, not that the fade failed.</item>
    /// </list>
    /// </summary>
    private void LogCensusIfChanged()
    {
        int blends = 0, flip = 0, swap = 0;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            switch (_surfaces[i].Kind)
            {
                case Delivery.Blends: blends++; break;
                case Delivery.FlipState: flip++; break;
                default: swap++; break;
            }
        }
        int signature = (((_surfaces.Count * 397 + flip) * 397 + swap) * 397 + _followers.Count)
                        * 397 + _followersHeld;
        if (signature == _loggedSurfaces)
            return;
        _loggedSurfaces = signature;
        // HW-VERIFY
        VRLog.Note("Net", $"Peer board [{_playerId}] see-through census: {_surfaces.Count} renderer(s) " +
            $"— {blends} already blend (property-block alpha, nothing cloned), {flip} take a " +
            "LIT-CLONE (their own shader kept, blend state flipped, so the surface reads the same " +
            $"at every point of the ramp), {swap} take an UNLIT-SWAP onto GloomhavenVR/Overlay " +
            "(their shader has no blend state at all, so the clone loses the baked lighting and " +
            "the fade-in still ends on a one-frame brightness step — that is a BUNDLE age, not a " +
            "decision: a bundle baked at ModBuild 351+ moves the board slab family from unlit-swap " +
            $"to lit-clone). Every clone is forced single-sided, so a fading board never shows its " +
            $"interior. {_followers.Count} follower root(s) fade with this board off the same " +
            $"alpha and {_followersHeld} registered root(s) are currently HELD OUT. A held root is " +
            "always a hand-anchored one (the peer's hand fan or its 'keine Handkarten' placard, " +
            "which follow the board only while their owner parks them over it — see Follow()); " +
            "the item / burned / discard fans belong to the board unconditionally and are never " +
            "held. So a peer whose hand fan is visibly still solid IN FRONT OF their fading board " +
            "is a held count that should have been a followed count, and a hand fan fading while " +
            "its owner holds it up beside their face is the reverse — those two readings, not a " +
            "screenshot, are what the 'over the board' predicate is judged by. Every UI graphic " +
            "rides one CanvasGroup per root.");
    }
}
