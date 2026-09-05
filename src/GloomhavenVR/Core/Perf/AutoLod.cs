using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// THE 2,560 <c>AutomaticLOD</c> BEHAVIOURS ON UNITY'S UPDATE LIST — what they cost, what they
/// decide, and what actually picks the level of detail this game renders at.
///
/// <para>WHERE THIS COMES FROM. ModBuild 227's <c>[Perf] SIM</c> line is the first instrument this
/// project has ever had that walks Unity's own per-frame lists, and its first hardware reading
/// (2026-08-22 <c>Player.log</c>) says: <b>Update 2986, LateUpdate 66, FixedUpdate 29 — of which
/// AutomaticLOD is 2560/2560 [U]</b>. That is <b>86 % of everything Unity calls every frame</b>,
/// against 11 Update entries owned by the mod (0.4 %). The same windows put main-thread logic at
/// ~34 % of a 16.96 ms frame (5.74 ms mean, p95 9.32 ms) with the mod at ~19 % of the total, so the
/// great majority of that list is the game's own work and no renderer count could ever see it.</para>
///
/// <para>A CORRECTION, ON THE RECORD. The ModBuild 227 investigation wrote that "the game has no
/// LODGroup at all — one hit, third-party AutomaticLOD, unreferenced by GH.Runtime". The second
/// half of that sentence is <b>wrong and is hereby superseded</b>: the component is instantiated
/// 2,560 times and every one of them is enabled and ticking. The first half is wrong too, from the
/// mod's own <c>[Perf] GFX</c> line in the same log: <b>"LOD groups: 1277 active, 1277 enabled"</b>.
/// Both halves were inferred from a source search — <c>GH.Runtime</c> genuinely never names
/// <c>AutomaticLOD</c> — and a source search cannot see a component that is attached to an ASSET.
/// This is the same lesson as <c>PerfSceneProfile.AppendLodGroups</c>'s own header comment, which
/// had already written it down: "answering from source alone is unsafe".</para>
///
/// <para><b>FINDING 1 — THE UPDATE IS INERT, AND THE CHEAPEST LEVER IS A NO-OP.</b> Read
/// <c>decompiled/ThirdParty/AutomaticLOD.cs:225</c>. <c>Update</c>'s very first statement is
/// <c>if (!m_bUseAutomaticCameraLODSwitch || LODSwitchMode == SwitchMode.UnityLODGroup) return;</c>.
/// <c>SetupLODGroup</c> (:1622) returns immediately unless <c>m_switchMode == UnityLODGroup</c>, so
/// <b>every LODGroup in the scene was created by an AutomaticLOD in exactly the mode that makes its
/// Update return on line one</b> — which is why 1,277 groups and 2,560 components pair up (1,277
/// roots plus their dependent children, whose <c>LODSwitchMode</c> property resolves through
/// <c>m_LODObjectRoot</c> to the same value). The census below measures that instead of assuming
/// it. Two consequences:</para>
/// <list type="bullet">
/// <item>The camera expression on <c>:248</c> — the one whose last fallback is <c>Camera.main</c> —
/// sits <b>after</b> that return. So does every read of the public static
/// <c>AutomaticLOD.UserDefinedLODCamera</c>. Pinning that static to the VR head camera, the
/// cheapest lever on the list and the one that looked most attractive, is therefore <b>provably a
/// no-op for every instance in UnityLODGroup mode</b>. It is not shipped, and this comment is why.</item>
/// <item>What each instance still costs is Unity's per-behaviour dispatch (Mono, not IL2CPP) plus
/// the one or two <c>UnityEngine.Object</c> alive-checks inside the <c>LODSwitchMode</c> property,
/// 2,560 times per frame. That is real and it is removable: a behaviour whose <c>Update</c>
/// provably returns on its first statement can be taken off the list entirely by setting
/// <c>enabled = false</c> — see IDLE SKIP below.</item>
/// </list>
///
/// <para><b>FINDING 2 — UNITY'S LODGroup DECIDES THE LEVEL, NOT THIS COMPONENT.</b> The level of
/// detail actually rendered comes from the 1,277 <see cref="LODGroup"/>s and therefore from
/// <see cref="QualitySettings.lodBias"/> (live: 2.00) and <b>the culling camera's field of view</b>.
/// That matters in VR far more than it does flat, and it is the mechanism behind the separate
/// report that surfaces read mushy up close (<c>.planning/debug/matschige_texturen.jpg</c>). Unity
/// selects a level from the RELATIVE SCREEN HEIGHT
/// <c>h = worldSize / (2 · distance · tan(fov/2)) · lodBias</c>, and the head camera's per-eye
/// vertical FOV is roughly 90–100° against a flat game camera's 40–60°. At the same distance the
/// VR eye therefore sees an object at a <b>fraction</b> of the screen height the asset author tuned
/// the transitions against, and every group transitions to a coarser mesh that much earlier.
/// <c>lodBias</c> is exactly the dial that cancels it — the correction factor is
/// <c>tan(fovVR/2) / tan(fovFlat/2)</c> — but the factor is a MEASUREMENT, not a guess, so the
/// census prints both FOVs, the ratio, and the lodBias that would restore the flat game's choice.
/// <c>[Optimize] LodBias</c> is the dial; it defaults to 0 = "leave the quality level's own value",
/// i.e. today's behaviour, because nothing here gets changed before the number is in a log.</para>
///
/// <para><b>FINDING 3 — "MUSHY" HAS A SECOND MECHANISM AND NOBODY HAS EVER PRINTED IT.</b> LOD
/// swaps MESHES; it cannot blur a texture. The game writes
/// <c>QualitySettings.masterTextureLimit = (int)TextureQuality</c> in
/// <c>GH.Runtime Gloomhaven/GraphicProfile.Setup()</c>, from a save-file enum
/// <c>TextureQualityRender { FULL=0, HALF=1, QUARTER=2, EIGHTHEN=3 }</c> — and a non-zero value
/// <b>discards that many top mip levels of every texture in the game</b>, which is the textbook
/// cause of "matschige Texturen" and is invisible to every render-state line the mod prints today.
/// Worse, <c>GraphicProfile</c>'s deserialiser falls back to <c>EIGHTHEN</c> (=3, the WORST) for
/// any saved value it does not recognise. The census reads that integer and names the game's own
/// options row, because if it is not 0 then no LOD lever in this file is the fix and the whole
/// question is answered by one line in the log instead of another hardware round.</para>
///
/// <para><b>WHAT IS SHIPPED.</b> Three things, each behind its own dial with an annotated default
/// in <c>Defaults.Core.cs</c>:</para>
/// <list type="number">
/// <item><b>The census</b> (<c>[Perf] LodCensus</c>, ON). One <c>[Perf] LOD</c> line naming how
/// many AutomaticLOD instances exist, their effective switch mode, how many take the early-out,
/// which camera each would resolve against if it ever got that far, the LOD-level counts, the
/// LODGroup population, and <b>the distribution of levels the scene is actually running at for the
/// VR head camera</b>, computed with Unity's own documented formula. It prints
/// <b>unconditionally</b> — including "found nothing" — because a scan that only speaks on success
/// hides that it never ran (<c>.planning</c>, ModBuild 149).</item>
/// <item><b>Idle skip</b> (<c>[Optimize] AutomaticLodIdleSkip</c>, ON). Sets <c>enabled = false</c>
/// on exactly those instances whose <c>LODSwitchMode</c> the sweep has just READ as
/// <c>UnityLODGroup</c> — the same test the component performs on itself — so this is not "we
/// think it is inert", it is per-instance proof. Every instance it touched is restored on
/// teardown, on a dial flip and on hot-reload.</item>
/// <item><b>LOD bias</b> (<c>[Optimize] LodBias</c>, 0 = leave alone). The one-line image-quality
/// lever the census sizes.</item>
/// </list>
///
/// <para><b>WHY NOT A HARMONY PATCH ON <c>Awake</c>.</b> It would catch every future instance for
/// free and is the obvious design (<see cref="SceneRegistry"/> does exactly that for three other
/// types). Rejected for this round: it adds a patch class to the shared inventory in a round where
/// a parallel worker owns the neighbouring files, and the alternative measures well — one typed
/// <c>FindObjectsOfType&lt;AutomaticLOD&gt;</c> on a 15 s cadence, which the sweep TIMES and prints,
/// against a saving paid on every one of ~1,350 frames in that window. If the printed walk cost
/// ever turns out to be a visible hitch, the patch is the upgrade.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here crosses the wire. Disabling a local behaviour whose
/// Update provably returns on its first statement changes no game state, no mesh and no rule; the
/// lodBias dial is a local render setting. Two clients with different dials see the same game.</para>
///
/// <para><b>FLAT SCREEN.</b> The census runs either way — a flat session is exactly the reference
/// reading we want — but the two BEHAVIOUR dials are gated on <see cref="VRSession.IsRunning"/>.
/// A session that never reached VR must be the vanilla game.</para>
///
/// <para><b>THE STATIC WE DO NOT TOUCH.</b> <c>AutomaticLOD.UserDefinedLODCamera</c> is a
/// process-wide static on a third-party type shared by every scene, with no owner and no restore
/// path: a scene change destroys the camera it points at and leaves the static holding a destroyed
/// object, which the component then treats as null and falls back to <c>Camera.main</c> from. The
/// census READS it (and reports who is holding it, if anyone) and never writes it — which costs
/// nothing, because Finding 1 says every read of it in this game is downstream of a return.</para>
/// </summary>
internal static class AutoLod
{
    private const string Scope = "Perf";

    /// <summary>Seconds after a scene becomes active before the first sweep runs.</summary>
    private const float FirstSweepDelaySeconds = 8f;

    /// <summary>Hard cap on census lines per scene, so a growing population cannot flood the log.</summary>
    private const int MaxCensusPerScene = 6;

    /// <summary>Re-emit the census when the population has grown by at least this fraction.</summary>
    private const float RegrowthFraction = 0.25f;

    /// <summary>...and by at least this many instances, so a tiny scene cannot re-trigger on noise.</summary>
    private const int RegrowthFloor = 64;

    /// <summary>How many individual LODGroups the census names, nearest to the head first.</summary>
    private const int NamedGroups = 8;

    /// <summary>Instances a single sweep must disable before it is worth closing a measurement window on.</summary>
    private const int MarkThreshold = 200;

    private static AutoLodHost? _host;

    /// <summary>Instances THIS class set <c>enabled = false</c> on — the restore list, and nothing else.</summary>
    private static readonly List<AutomaticLOD> Disabled = new(2600);

    private static readonly List<AutomaticLOD> Scratch = new(64);

    private static float _nextSweep;
    private static int _sceneHandle;
    private static int _censusCount;
    private static int _lastCensusPopulation = -1;
    private static bool _sawScene;
    private static bool _markedThisScene;

    /// <summary><see cref="QualitySettings.lodBias"/> as it was before the dial first wrote it.</summary>
    private static float _lodBiasOriginal;
    private static bool _lodBiasHeld;

    /// <summary><c>m_renderCamera</c> is private; the census reports it, so it is reflected once.</summary>
    private static FieldInfo? _renderCameraField;
    private static bool _renderCameraLookedUp;

    // ==========================================================================================
    //  lifecycle
    // ==========================================================================================

    /// <summary>
    /// Attach the sweep host to the mod's own Core root. Idempotent, and safe before VR is up: the
    /// census is worth having on the flat screen (it is the reference reading the VR one is judged
    /// against) and the two behaviour dials check <see cref="VRSession.IsRunning"/> themselves.
    /// </summary>
    internal static void Install(GameObject root)
    {
        PerfConfig.Bind();
        if (_host != null)
            return;
        _host = root.AddComponent<AutoLodHost>();
        _nextSweep = Time.unscaledTime + FirstSweepDelaySeconds;
    }

    /// <summary>
    /// Put every instance we disabled back, and put <see cref="QualitySettings.lodBias"/> back.
    /// Called from <see cref="CoreModule.Shutdown"/>, so it runs on hot-reload as well as on exit —
    /// the mod must never leave a game component switched off behind it.
    /// </summary>
    internal static void Shutdown()
    {
        int restored = RestoreDisabled();
        bool bias = ReleaseLodBias();
        if (restored > 0 || bias)
            VRLog.Info(Scope, $"[Optimize] AutomaticLOD teardown — re-enabled {restored} behaviour(s)"
                              + (bias ? $" and put QualitySettings.lodBias back to {_lodBiasOriginal:F2}" : "")
                              + ".");
        Disabled.Clear();
        _host = null;
        _sawScene = false;
        _markedThisScene = false;
        _censusCount = 0;
        _lastCensusPopulation = -1;
    }

    // ==========================================================================================
    //  the sweep
    // ==========================================================================================

    /// <summary>
    /// Cadence gate + scene-change detection. One float compare per frame in the common case.
    /// </summary>
    private static void Tick()
    {
        // A dial flipped back to off must undo itself immediately, not at the next sweep.
        if (Disabled.Count > 0 && !IdleSkipActive)
        {
            int restored = RestoreDisabled();
            VRLog.Info(Scope, $"[Optimize] AutomaticLodIdleSkip switched OFF — re-enabled {restored} "
                              + "AutomaticLOD behaviour(s); Unity's Update list grows back by that "
                              + "many entries and the next [Perf] SIM line will show it.");
        }
        if (_lodBiasHeld && PerfConfig.LodBiasOverride <= 0f)
        {
            float back = _lodBiasOriginal;
            ReleaseLodBias();
            VRLog.Info(Scope, $"[Optimize] LodBias cleared — QualitySettings.lodBias back to {back:F2} "
                              + "(the quality level's own value).");
        }

        int handle = 0;
        try
        {
            handle = SceneManager.GetActiveScene().handle;
        }
        catch (Exception)
        {
            // Cannot tell where we are: fall through on the existing cadence rather than re-arm.
        }
        // handle == 0 means the read above threw. Treating that as a scene change would re-arm the
        // delay on EVERY frame and the sweep would then never run at all — the "gated remedy never
        // ran" shape, where a silent failure looks exactly like a fix that did nothing.
        if (handle != 0 && handle != _sceneHandle)
        {
            _sceneHandle = handle;
            _sawScene = false;
            _markedThisScene = false;
            _censusCount = 0;
            _lastCensusPopulation = -1;
            _nextSweep = Time.unscaledTime + FirstSweepDelaySeconds;
            // The instances we disabled belong to the scene that is going away. Do not carry
            // destroyed references across; the next sweep re-derives the whole set from scratch.
            PruneDisabled();
        }

        float now = Time.unscaledTime;
        if (now < _nextSweep)
            return;
        _nextSweep = now + PerfConfig.LodSweepSeconds;

        // Nothing in the pre-menu scenes is worth walking (hardware 2026-07: five renderers in the
        // intro) and a fault in a walk that runs before the settings pane exists cannot be switched
        // off from inside the headset. Same gate, same source as PerfSceneProfile.
        if (PerfSceneProfile.IsPreMenuScene())
            return;

        Sweep();
    }

    /// <summary>
    /// One typed scan of every active <c>AutomaticLOD</c>: classify, disable the ones proven inert,
    /// and emit the census when this scene has not had one yet or the population has grown.
    /// </summary>
    private static void Sweep()
    {
        Stopwatch watch = Stopwatch.StartNew();

        AutomaticLOD[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<AutomaticLOD>();
        }
        catch (Exception e)
        {
            // A missing/renamed ThirdParty type would land here. Say so ONCE, then stop sweeping:
            // an instrument that cannot read its subject must not keep paying for the attempt.
            _nextSweep = float.MaxValue;
            VRLog.Warn(Scope, "[Perf] LOD — the AutomaticLOD scan threw and this sweep has switched "
                              + $"itself off for the session ({e.GetType().Name}: {e.Message}). No "
                              + "instance was touched, nothing was disabled, and QualitySettings is "
                              + "untouched.");
            return;
        }

        Census c = default;
        c.Total = all.Length;
        c.Levels = new int[8];
        c.CurrentLevel = new int[8];

        bool skip = IdleSkipActive;
        for (int i = 0; i < all.Length; i++)
        {
            AutomaticLOD a = all[i];
            if (a == null)
                continue;
            if (a.enabled)
                c.Enabled++;

            // The EFFECTIVE mode, read through the component's own public property, which resolves
            // a dependent child through m_LODObjectRootPersist / m_LODObjectRoot exactly as Update
            // does. Reading the private m_switchMode instead would misclassify every child.
            AutomaticLOD.SwitchMode mode;
            try
            {
                mode = a.LODSwitchMode;
            }
            catch (Exception)
            {
                c.ModeUnreadable++;
                continue;
            }
            switch (mode)
            {
                case AutomaticLOD.SwitchMode.UnityLODGroup: c.ModeUnityLodGroup++; break;
                case AutomaticLOD.SwitchMode.SwitchMesh: c.ModeSwitchMesh++; break;
                default: c.ModeSwitchGameObject++; break;
            }

            if (a.IsRootAutomaticLOD())
                c.Roots++;
            else
                c.Dependents++;

            if (a.m_evalMode == AutomaticLOD.EvalMode.CameraDistance)
                c.EvalDistance++;
            else
                c.EvalCoverage++;

            if (a.m_originalMesh != null)
                c.WithOriginalMesh++;

            int levels = a.GetLODLevelCount();
            c.Levels[Mathf.Clamp(levels, 0, c.Levels.Length - 1)]++;

            int current = a.GetCurrentLODLevel();
            if (current < 0)
                c.NeverSwitched++;
            else
                c.CurrentLevel[Mathf.Clamp(current, 0, c.CurrentLevel.Length - 1)]++;

            // INERT means: Update returns on its FIRST statement, so it reads no camera, calls no
            // GetLODLevelUsingCamera and switches no mesh. This is the component's own test, run on
            // the component's own value — not an assumption about how the assets were authored.
            bool inert = mode == AutomaticLOD.SwitchMode.UnityLODGroup;
            if (inert)
                c.Inert++;

            if (skip && inert && a.enabled)
            {
                a.enabled = false;
                Disabled.Add(a);
                c.NewlyDisabled++;
            }
        }

        watch.Stop();
        c.WalkMs = (float)watch.Elapsed.TotalMilliseconds;
        c.DisabledHeld = Disabled.Count;

        ApplyLodBias();

        bool first = !_sawScene;
        _sawScene = true;
        bool grown = _lastCensusPopulation >= 0
                     && c.Total >= _lastCensusPopulation + RegrowthFloor
                     && c.Total >= _lastCensusPopulation * (1f + RegrowthFraction);

        if ((first || grown) && _censusCount < MaxCensusPerScene && PerfConfig.LodCensusOn)
        {
            _censusCount++;
            _lastCensusPopulation = c.Total;
            EmitCensus(ref c, all, first);
        }
        else if (c.NewlyDisabled > 0)
        {
            VRLog.Info(Scope, $"[Optimize] AutomaticLodIdleSkip: {c.NewlyDisabled} newly-revealed "
                              + $"AutomaticLOD behaviour(s) taken off Unity's Update list "
                              + $"({Disabled.Count} held in total). Scan {c.WalkMs:F1}ms.");
        }

        // TURN "expected gain" INTO "measured gain" IN THE FIRST HARDWARE LOG. Taking a large block
        // off Unity's Update list is exactly the kind of change that a 30 s averaging window
        // straddles and then reports as one meaningless middle number — the failure this project
        // built MarkChange for. Closing the window on the change means the [Perf] SPLIT line before
        // it describes ONLY the old state and the one after it ONLY the new one, so the logic span
        // either falls or it does not, and either answer is worth having. Once per scene, and only
        // for a block big enough to be visible above the ~1 ms noise floor the ZOOM clause measures.
        if (!_markedThisScene && c.NewlyDisabled >= MarkThreshold)
        {
            _markedThisScene = true;
            PerfMonitor.MarkChange(
                $"[Optimize] AutomaticLodIdleSkip took {c.NewlyDisabled} AutomaticLOD behaviour(s) "
                + "off Unity's per-frame Update list. Compare the 'logic (Update->LateUpdate)' span "
                + "on the [Perf] SPLIT line above against the one below, and check the [Perf] SIM "
                + "line's Update count actually fell by that many — if it did not, the disable did "
                + "not take and the comparison is meaningless");
        }
    }

    // ==========================================================================================
    //  the census line
    // ==========================================================================================

    /// <summary>
    /// The measurement. Emitted whatever it found — a "0 instances, 0 LODGroups" line is the only
    /// thing that separates "there is nothing here" from "the scan never ran", and this project has
    /// paid for that distinction twice.
    /// </summary>
    private static void EmitCensus(ref Census c, AutomaticLOD[] all, bool first)
    {
        Stopwatch watch = Stopwatch.StartNew();
        StringBuilder sb = new StringBuilder(4096);

        sb.Append("[Perf] LOD — WHAT DECIDES THE LEVEL OF DETAIL, and what the 2,560-entry "
                  + "AutomaticLOD block on Unity's Update list actually does (")
          .Append(first ? "first reading in this scene" : "re-read: the population grew")
          .Append(", sampled once)");

        // ---- the AutomaticLOD population -----------------------------------------------------
        sb.Append(" | AutomaticLOD: ").Append(c.Total).Append(" instance(s) on ACTIVE GameObject(s), ")
          .Append(c.Enabled).Append(" enabled, ").Append(c.Roots).Append(" root / ")
          .Append(c.Dependents).Append(" dependent");

        if (c.Total == 0)
        {
            sb.Append(" — NONE. This scene has no AutomaticLOD at all, so nothing on Unity's Update "
                      + "list belongs to it here and no idle-skip lever exists. That is a real "
                      + "reading, not a failed scan");
        }
        else
        {
            sb.Append(" | effective LODSwitchMode (read through the component's own property, so a "
                      + "dependent child resolves through its root exactly as Update does): "
                      + "UnityLODGroup ").Append(c.ModeUnityLodGroup)
              .Append(", SwitchMesh ").Append(c.ModeSwitchMesh)
              .Append(", SwitchGameObject ").Append(c.ModeSwitchGameObject);
            if (c.ModeUnreadable > 0)
                sb.Append(", unreadable ").Append(c.ModeUnreadable);

            sb.Append(" | INERT: ").Append(c.Inert).Append(" of ").Append(c.Total)
              .Append(" take AutomaticLOD.Update's FIRST-LINE early-out (decompiled "
                      + "ThirdParty/AutomaticLOD.cs:227, LODSwitchMode == UnityLODGroup) and "
                      + "therefore never read a camera, never call GetLODLevelUsingCamera and never "
                      + "switch a mesh");
            if (c.Inert == c.Total && c.Total > 0)
                sb.Append(" — ALL OF THEM. AutomaticLOD.UserDefinedLODCamera is read on :248, AFTER "
                          + "that return, so pinning that static to the VR head camera is a "
                          + "PROVABLE NO-OP here and is deliberately not shipped");
            else if (c.Inert < c.Total)
                sb.Append(" — but ").Append(c.Total - c.Inert)
                  .Append(" do NOT, and those are the ones a camera pin would reach. Read the "
                          + "camera clause below before touching them: SwitchMesh replaces "
                          + "MeshFilter.sharedMesh, so changing their level changes what any game "
                          + "code reading the active mesh reads");

            sb.Append(" | evalMode: ScreenCoverage ").Append(c.EvalCoverage)
              .Append(", CameraDistance ").Append(c.EvalDistance)
              .Append(" | with an original mesh: ").Append(c.WithOriginalMesh)
              .Append(" | LOD levels per instance: ");
            AppendHistogram(sb, c.Levels, "x");
            sb.Append(" | GetCurrentLODLevel(): never switched (-1) ").Append(c.NeverSwitched);
            if (c.Total - c.NeverSwitched > 0)
            {
                sb.Append(", switched to ");
                AppendHistogram(sb, c.CurrentLevel, "LOD");
            }

            AppendCameraClause(sb, all);
        }

        // ---- what Unity's own LOD system is doing ----------------------------------------------
        AppendLodGroupClause(sb);

        // ---- the competing mechanism for "mushy" ------------------------------------------------
        AppendTextureClause(sb);

        // ---- what this file changed --------------------------------------------------------------
        sb.Append(" | IDLE SKIP: ");
        if (!IdleSkipActive)
        {
            sb.Append("OFF ([Optimize] AutomaticLodIdleSkip")
              .Append(VRSession.IsRunning ? " = false" : " is VR-only and this is a flat session")
              .Append(") — nothing was disabled and Unity's Update list is untouched");
        }
        else
        {
            sb.Append(c.NewlyDisabled).Append(" newly disabled this sweep, ").Append(c.DisabledHeld)
              .Append(" held in total. Each one is a behaviour whose Update this sweep READ as "
                      + "UnityLODGroup, i.e. one that returns on its first statement. Unity's "
                      + "Update list should drop by that many entries — the next [Perf] SIM line "
                      + "is where to read it, and it is the only honest measurement of the gain");
        }

        sb.Append(" | LOD BIAS: ");
        if (!_lodBiasHeld)
            sb.Append("[Optimize] LodBias = 0, so QualitySettings.lodBias is the quality level's own "
                      + "value (").Append(QualitySettings.lodBias.ToString("F2"))
              .Append(") and this build changes nothing about which level is chosen");
        else
            sb.Append("held at ").Append(QualitySettings.lodBias.ToString("F2"))
              .Append(" by [Optimize] LodBias (was ").Append(_lodBiasOriginal.ToString("F2"))
              .Append("); every LODGroup transition moves that much further out");

        watch.Stop();
        sb.Append(" | COST, measured not asserted: the scan took ").Append(c.WalkMs.ToString("F1"))
          .Append("ms and this line took ").Append(watch.Elapsed.TotalMilliseconds.ToString("F1"))
          .Append("ms, once, on a ").Append(PerfConfig.LodSweepSeconds.ToString("F0"))
          .Append("s sweep cadence (") .Append(_censusCount).Append(" of ").Append(MaxCensusPerScene)
          .Append(" census line(s) this scene)");

        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>
    /// WHICH CAMERA WOULD DECIDE, if any instance ever got past the early-out. Three sources in the
    /// component's own precedence order (<c>:248</c>): the process-wide static, the per-instance
    /// <c>m_renderCamera</c> written from <c>OnWillRenderObject</c> (i.e. whichever camera rendered
    /// the object LAST in the frame — with a head camera, a ScenarioCamera and four panel cameras
    /// alive, that is not a stable answer), and <see cref="Camera.main"/>.
    /// </summary>
    private static void AppendCameraClause(StringBuilder sb, AutomaticLOD[] all)
    {
        sb.Append(" | WHICH CAMERA WOULD DECIDE (AutomaticLOD.cs:248, in the component's own "
                  + "precedence order): UserDefinedLODCamera = ");
        try
        {
            Camera? user = AutomaticLOD.UserDefinedLODCamera;
            sb.Append(user != null ? $"'{user.name}' (someone is holding this static)" : "NONE (nobody sets it)");
        }
        catch (Exception e)
        {
            sb.Append("n/a (").Append(e.GetType().Name).Append(')');
        }

        sb.Append(" -> m_renderCamera: ");
        FieldInfo? field = RenderCameraField();
        if (field == null)
        {
            sb.Append("n/a (the private field could not be reflected)");
        }
        else
        {
            Dictionary<string, int> byName = new(8);
            int nulls = 0;
            for (int i = 0; i < all.Length; i++)
            {
                AutomaticLOD a = all[i];
                if (a == null)
                    continue;
                Camera? cam = null;
                try
                {
                    cam = field.GetValue(a) as Camera;
                }
                catch (Exception)
                {
                    // one unreadable instance must not cost the whole clause
                }
                if (cam == null)
                {
                    nulls++;
                    continue;
                }
                string name = cam.name;
                byName.TryGetValue(name, out int n);
                byName[name] = n + 1;
            }
            sb.Append("unset ").Append(nulls);
            foreach (KeyValuePair<string, int> pair in byName)
                sb.Append(", '").Append(pair.Key).Append("' ").Append(pair.Value);
            if (nulls == all.Length)
                sb.Append(" — every one unset, which is what an INERT population looks like: "
                          + "OnWillRenderObject only writes this field on an ENABLED behaviour, and "
                          + "nothing reads it before Update's return anyway");
        }

        Camera? main = Camera.main;
        sb.Append(" -> Camera.main = ").Append(main != null ? $"'{main.name}'" : "NONE");
        Camera? head = VRCameraPolicy.AllowedHead;
        sb.Append(" (the VR head camera is ").Append(head != null ? $"'{head.name}'" : "NOT BUILT")
          .Append(head != null && main == head ? ", and it IS Camera.main)" : ")");
    }

    /// <summary>
    /// The clause that answers the question worth the most: WHAT LEVEL IS THE SCENE ACTUALLY
    /// RUNNING AT, and would a flat camera have chosen differently.
    ///
    /// <para>The level is computed here, from Unity's own documented relative-screen-height
    /// formula, because Unity exposes no read-back of the level it picked: <see cref="LODGroup"/>
    /// has <c>ForceLOD</c> but no "current LOD", and <see cref="Renderer.isVisible"/> is
    /// "visible to ANY camera" — with the game's ScenarioCamera also rendering these objects, that
    /// cannot separate the eyes' choice from anyone else's. So this is a MODEL of Unity's choice,
    /// stated as such, and its inputs (size, distance, FOV, bias) are all printed beside it so the
    /// arithmetic can be checked rather than trusted.</para>
    /// </summary>
    private static void AppendLodGroupClause(StringBuilder sb)
    {
        LODGroup[] groups;
        int enabledGroups;
        try
        {
            // THE SWEEP IS LodGroupCensus'S SINCE 2026-09-05 (redundancy survey R43). The [Perf]
            // GFX line asks the same question with the same API on a different cadence, and until
            // now this one reported only the ACTIVE count while that one reported active AND
            // enabled — so a log carrying both showed two numbers with nothing saying they were two
            // sweeps of two populations taken at two moments. The sentence below is unchanged; the
            // enabled count and the population rule are APPENDED to it.
            LodGroupCensus.Result lod = LodGroupCensus.Sweep();
            groups = lod.Groups;
            enabledGroups = lod.Enabled;
        }
        catch (Exception e)
        {
            sb.Append(" | LODGroups n/a (").Append(e.GetType().Name).Append(')');
            return;
        }

        sb.Append(" | THE LEVEL IS UNITY'S, NOT AutomaticLOD'S: ").Append(groups.Length)
          .Append(" LODGroup(s) active");
        sb.Append(", ").Append(enabledGroups).Append(" of them enabled");
        LodGroupCensus.AppendPopulationRule(sb, "[Optimize] AutomaticLOD", PerfConfig.LodSweepSeconds);
        if (groups.Length == 0)
        {
            sb.Append(" — NONE, so QualitySettings.lodBias and maximumLODLevel are inert here and "
                      + "no LOD-side lever exists in this scene no matter how attractive it sounds");
            return;
        }
        sb.Append(". Every one of them was created by an AutomaticLOD in UnityLODGroup mode "
                  + "(AutomaticLOD.SetupLODGroup:1625 returns unless m_switchMode == UnityLODGroup), "
                  + "which is why the two populations pair up");

        Camera? head = VRCameraPolicy.AllowedHead;
        Camera? flat = FindFlatReferenceCamera(head);
        float bias = QualitySettings.lodBias;
        int maxLod = QualitySettings.maximumLODLevel;

        // ---- the two cameras, in the terms the LOD formula actually uses ----------------------
        sb.Append(" | HEAD CAMERA: ");
        if (head == null)
        {
            sb.Append("NOT BUILT (flat session, or the rig has not come up yet) — the level "
                      + "distribution below is therefore computed for ");
            head = flat;
            if (head == null)
            {
                sb.Append("nothing, and is omitted");
            }
            else
            {
                sb.Append("the game's own camera instead, which makes this line the FLAT REFERENCE "
                          + "READING the VR one has to be judged against: ");
                AppendCameraGeometry(sb, head);
            }
        }
        else
        {
            AppendCameraGeometry(sb, head);
        }

        if (head == null)
        {
            sb.Append(" | no camera to resolve against, so no level distribution this sweep");
            return;
        }

        float headFov = EffectiveVerticalFov(head);

        if (flat != null && flat != head)
        {
            sb.Append(" | FLAT REFERENCE CAMERA: ");
            AppendCameraGeometry(sb, flat);
            float flatFov = EffectiveVerticalFov(flat);
            float tHead = Mathf.Tan(headFov * 0.5f * Mathf.Deg2Rad);
            float tFlat = Mathf.Tan(flatFov * 0.5f * Mathf.Deg2Rad);
            if (tFlat > 1e-4f && tHead > 1e-4f)
            {
                float penalty = tHead / tFlat;
                sb.Append(" | FOV PENALTY: tan(").Append(headFov.ToString("F1")).Append("/2) / tan(")
                  .Append(flatFov.ToString("F1")).Append("/2) = ").Append(penalty.ToString("F2"))
                  .Append("x. Unity picks a level from h = worldSize / (2 x distance x tan(fov/2)) x "
                          + "lodBias, so AT THE SAME DISTANCE the VR eye sees an object at 1/")
                  .Append(penalty.ToString("F2"))
                  .Append(" of the screen height the asset author tuned the transitions against, and "
                          + "every LODGroup switches to a coarser mesh that much earlier. lodBias "
                          + "does NOT cancel this on its own — it is one global multiplier that both "
                          + "cameras get — so the correction has to be deliberate: at the live bias "
                          + "of ").Append(bias.ToString("F2")).Append(", [Optimize] LodBias = ")
                  .Append((bias * penalty).ToString("F2"))
                  .Append(" makes the VR eye choose EXACTLY the level the flat presentation chooses "
                          + "at the same distance. The cost is finer meshes further out, i.e. "
                          + "submission volume, which the [Perf] SPLIT line prices. If the "
                          + "distribution below is already dominated by LOD0 then this penalty is "
                          + "real arithmetic with no visible consequence and the dial is not the fix");
            }
        }
        else
        {
            sb.Append(" | no separate flat reference camera in this scene, so no FOV comparison");
        }

        // ---- the distribution ------------------------------------------------------------------
        int[] chosen = new int[9];
        int culled = 0, unreadable = 0, outsideFrustum = 0;
        int[] levelCount = new int[9];
        NearestGroup[] nearest = new NearestGroup[NamedGroups];
        for (int i = 0; i < nearest.Length; i++)
            nearest[i].Distance = float.MaxValue;

        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(head);
        Vector3 eye = head.transform.position;

        for (int i = 0; i < groups.Length; i++)
        {
            LODGroup g = groups[i];
            if (g == null || !g.enabled)
                continue;
            LOD[] lods;
            try
            {
                lods = g.GetLODs();
            }
            catch (Exception)
            {
                unreadable++;
                continue;
            }
            if (lods == null || lods.Length == 0)
            {
                unreadable++;
                continue;
            }
            levelCount[Mathf.Clamp(lods.Length, 0, levelCount.Length - 1)]++;

            Transform t = g.transform;
            Vector3 lossy = t.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            float worldSize = g.size * scale;
            Vector3 refPoint = t.TransformPoint(g.localReferencePoint);
            float distance = Vector3.Distance(eye, refPoint);

            float relative;
            if (head.orthographic)
                relative = worldSize / (2f * Mathf.Max(head.orthographicSize, 1e-4f));
            else
                relative = worldSize / (2f * Mathf.Max(distance, 1e-4f)
                                        * Mathf.Max(Mathf.Tan(headFov * 0.5f * Mathf.Deg2Rad), 1e-4f));
            relative *= Mathf.Max(bias, 1e-4f);

            int level = -1;
            for (int k = 0; k < lods.Length; k++)
            {
                if (relative >= lods[k].screenRelativeTransitionHeight)
                {
                    level = k;
                    break;
                }
            }
            level = level < 0 ? -1 : Mathf.Max(level, maxLod);

            bool inView = GeometryUtility.TestPlanesAABB(
                frustum, new Bounds(refPoint, Vector3.one * Mathf.Max(worldSize, 0.001f)));
            if (!inView)
            {
                outsideFrustum++;
                continue;
            }
            if (level < 0)
                culled++;
            else
                chosen[Mathf.Clamp(level, 0, chosen.Length - 1)]++;

            RecordNearest(nearest, g, distance, worldSize, relative, level, lods.Length);
        }

        sb.Append(" | levels available per group: ");
        AppendHistogram(sb, levelCount, "x");
        sb.Append(" | WHAT LEVEL THE SCENE IS RUNNING AT for '").Append(head.name)
          .Append("' at lodBias ").Append(bias.ToString("F2")).Append(", maximumLODLevel ")
          .Append(maxLod).Append(" — of the ").Append(groups.Length).Append(" group(s), ")
          .Append(outsideFrustum).Append(" are outside the frustum and the rest resolve to: ");
        AppendHistogram(sb, chosen, "LOD");
        sb.Append(", LOD-culled entirely ").Append(culled);
        if (unreadable > 0)
            sb.Append(", unreadable ").Append(unreadable);
        sb.Append(". THIS IS A MODEL OF UNITY'S CHOICE, not a read-back: LODGroup exposes ForceLOD "
                  + "but no 'current LOD', and Renderer.isVisible is 'visible to ANY camera' — with "
                  + "the game's own ScenarioCamera rendering the same objects it cannot separate the "
                  + "eyes' choice from anyone else's. Every input to the arithmetic is printed here "
                  + "so it can be checked instead of trusted");

        AppendNearest(sb, nearest, head);
    }

    /// <summary>The nearest groups, named — the ones a player standing in the dungeon is looking at.</summary>
    private static void AppendNearest(StringBuilder sb, NearestGroup[] nearest, Camera head)
    {
        sb.Append(" | THE ").Append(NamedGroups).Append(" GROUP(S) NEAREST THE HEAD (the surfaces "
                  + "the 'matschige Texturen' report is about are whatever is a few world units in "
                  + "front of the eye; name=distance/size -> level of total, verts = the chosen "
                  + "level's vertex count against LOD0's): ");
        bool any = false;
        for (int i = 0; i < nearest.Length; i++)
        {
            if (nearest[i].Group == null)
                continue;
            NearestGroup n = nearest[i];
            sb.Append(any ? ", '" : "'").Append(n.Name ?? "?").Append("' d=")
              .Append(n.Distance.ToString("F1")).Append("wu size=").Append(n.Size.ToString("F1"))
              .Append("wu h=").Append(n.RelativeHeight.ToString("F3")).Append(" -> ")
              .Append(n.Level < 0 ? "CULLED" : "LOD" + n.Level).Append(" of ").Append(n.Levels);
            int verts = n.Level >= 0 ? CountVerts(n.Group, n.Level) : -1;
            int verts0 = CountVerts(n.Group, 0);
            if (verts >= 0 && verts0 > 0)
                sb.Append(" verts ").Append(verts).Append('/').Append(verts0);
            any = true;
        }
        if (!any)
            sb.Append("none inside the frustum");
        sb.Append(". The head stands at ").Append(head.transform.position.ToString("F1"))
          .Append(" with a rig scale of ")
          .Append(head.transform.lossyScale.x.ToString("F1"))
          .Append(" world units per tracking metre, which is why 'a few metres away' and 'a few "
                  + "world units away' are completely different distances here and why the flat "
                  + "game's authored transition heights cannot be assumed to transfer");
    }

    /// <summary>
    /// <see cref="QualitySettings.masterTextureLimit"/> — the competing mechanism for "mushy", and
    /// the one number no line in this mod's log has ever carried.
    /// </summary>
    private static void AppendTextureClause(StringBuilder sb)
    {
        int limit;
        try
        {
            limit = QualitySettings.masterTextureLimit;
        }
        catch (Exception e)
        {
            sb.Append(" | masterTextureLimit n/a (").Append(e.GetType().Name).Append(')');
            return;
        }

        string name = limit switch
        {
            0 => "FULL",
            1 => "HALF",
            2 => "QUARTER",
            3 => "EIGHTHEN",
            _ => "out of range"
        };

        sb.Append(" | MUSHY SURFACES HAVE A SECOND MECHANISM AND THIS IS IT: "
                  + "QualitySettings.masterTextureLimit = ").Append(limit).Append(" (the game's own "
                  + "Optionen > Grafik > Texturqualitaet = ").Append(name)
          .Append(", GH.Runtime Gloomhaven/GraphicProfile.Setup writes it from the save file's "
                  + "TextureQualityRender enum { FULL=0, HALF=1, QUARTER=2, EIGHTHEN=3 })");
        if (limit > 0)
            sb.Append(". IT IS NOT ZERO, so the engine is discarding the top ").Append(limit)
              .Append(" mip level(s) of EVERY texture in the game before it is ever sampled. That "
                      + "is the textbook cause of blurry surfaces, it is nothing to do with LOD "
                      + "(LOD swaps MESHES and cannot blur a texture), and no lever in this file "
                      + "can fix it: set that row back to the highest setting in the GAME's own "
                      + "options. Note GraphicProfile's deserialiser falls back to EIGHTHEN — the "
                      + "WORST value — for any saved entry it does not recognise, so a stale or "
                      + "hand-edited save lands here silently");
        else
            sb.Append(". It is 0, so no mip level is being discarded and blurry surfaces are NOT "
                      + "this — which leaves the mesh level above, the texture's own authored "
                      + "resolution against a 3072px-per-eye target, and anisotropy (live: ")
              .Append(QualitySettings.anisotropicFiltering).Append(')');
    }

    // ==========================================================================================
    //  helpers
    // ==========================================================================================

    /// <summary>
    /// The vertical FOV the camera actually RENDERS with. For an XR camera these two disagree:
    /// <see cref="Camera.fieldOfView"/> is the mono property (often still whatever the rig set it
    /// to) while the eye pass is drawn with the runtime's own asymmetric projection. Both are
    /// printed by <see cref="AppendCameraGeometry"/>; this returns the projection-derived one,
    /// because that is what the eye sees.
    /// </summary>
    private static float EffectiveVerticalFov(Camera cam)
    {
        try
        {
            Matrix4x4 proj = cam.stereoEnabled
                ? cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                : cam.projectionMatrix;
            float m11 = Mathf.Abs(proj.m11);
            if (m11 > 1e-4f)
                return 2f * Mathf.Atan(1f / m11) * Mathf.Rad2Deg;
        }
        catch (Exception)
        {
            // fall through to the property
        }
        return cam.fieldOfView;
    }

    private static void AppendCameraGeometry(StringBuilder sb, Camera cam)
    {
        sb.Append('\'').Append(cam.name).Append("' fieldOfView property ")
          .Append(cam.fieldOfView.ToString("F1")).Append(" vs ")
          .Append(EffectiveVerticalFov(cam).ToString("F1"))
          .Append(" derived from the projection matrix (THIS is what the pass renders with; for an "
                  + "XR camera the two routinely disagree, and which one Unity's culling feeds the "
                  + "LOD solver is precisely why both are printed), stereo=")
          .Append(cam.stereoEnabled ? cam.stereoTargetEye.ToString() : "off")
          .Append(" aspect=").Append(cam.aspect.ToString("F2"))
          .Append(" near/far=").Append(cam.nearClipPlane.ToString("F2")).Append('/')
          .Append(cam.farClipPlane.ToString("F0"))
          .Append(" scale=").Append(cam.transform.lossyScale.x.ToString("F2"));
    }

    /// <summary>
    /// The game's own dungeon camera, as the reference the VR head is judged against. Named by the
    /// <c>[Perf] SPLIT</c> line, which sees it render every frame beside the head camera.
    /// </summary>
    private static Camera? FindFlatReferenceCamera(Camera? head)
    {
        Camera? fallback = null;
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam == head)
                continue;
            string name = cam.name;
            if (name.IndexOf("Scenario", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) < 0)
                return cam;
            if (fallback == null && name.IndexOf("Main Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                fallback = cam;
        }
        return fallback;
    }

    private static void RecordNearest(NearestGroup[] nearest, LODGroup g, float distance,
                                      float size, float relative, int level, int levels)
    {
        int worst = 0;
        for (int i = 1; i < nearest.Length; i++)
        {
            if (nearest[i].Distance > nearest[worst].Distance)
                worst = i;
        }
        if (distance >= nearest[worst].Distance)
            return;

        // Deliberately does NOT resolve the vertex counts here: RecordNearest is called for every
        // in-frustum group that beats the current worst, and GetLODs() allocates. The handful of
        // survivors get their counts once, in AppendNearest.
        nearest[worst].Group = g;
        nearest[worst].Name = g.name;
        nearest[worst].Distance = distance;
        nearest[worst].Size = size;
        nearest[worst].RelativeHeight = relative;
        nearest[worst].Level = level;
        nearest[worst].Levels = levels;
    }

    /// <summary>
    /// Vertex count of one LOD level's renderers — the direct evidence for "the silhouette went
    /// simple". Only ever called for the handful of named groups.
    /// </summary>
    private static int CountVerts(LODGroup? g, int level)
    {
        if (g == null)
            return -1;
        try
        {
            LOD[] lods = g.GetLODs();
            if (lods == null || level < 0 || level >= lods.Length)
                return -1;
            Renderer[] renderers = lods[level].renderers;
            if (renderers == null)
                return 0;
            int total = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                    continue;
                MeshFilter? mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    total += mf.sharedMesh.vertexCount;
                    continue;
                }
                SkinnedMeshRenderer? smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh != null)
                    total += smr.sharedMesh.vertexCount;
            }
            return total;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static void AppendHistogram(StringBuilder sb, int[] counts, string prefix)
    {
        bool any = false;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;
            if (any)
                sb.Append(", ");
            sb.Append(prefix).Append(i).Append(' ').Append(counts[i]);
            any = true;
        }
        if (!any)
            sb.Append("none");
    }

    private static FieldInfo? RenderCameraField()
    {
        if (_renderCameraLookedUp)
            return _renderCameraField;
        _renderCameraLookedUp = true;
        try
        {
            _renderCameraField = typeof(AutomaticLOD).GetField(
                "m_renderCamera", BindingFlags.Instance | BindingFlags.NonPublic);
        }
        catch (Exception)
        {
            _renderCameraField = null;
        }
        return _renderCameraField;
    }

    // ==========================================================================================
    //  the two behaviour dials
    // ==========================================================================================

    /// <summary>
    /// Idle skip is a BEHAVIOUR change on the game's own components, so it is gated on VR: a
    /// session that never reached VR must be the vanilla game, unmodified.
    /// </summary>
    private static bool IdleSkipActive => VRSession.IsRunning && PerfConfig.AutomaticLodIdleSkipOn;

    private static int RestoreDisabled()
    {
        int restored = 0;
        for (int i = 0; i < Disabled.Count; i++)
        {
            AutomaticLOD a = Disabled[i];
            if (a == null)
                continue;
            a.enabled = true;
            restored++;
        }
        Disabled.Clear();
        return restored;
    }

    /// <summary>Drop destroyed entries. Unity fake-null: the managed reference survives Destroy.</summary>
    private static void PruneDisabled()
    {
        Scratch.Clear();
        for (int i = 0; i < Disabled.Count; i++)
        {
            if (Disabled[i] != null)
                Scratch.Add(Disabled[i]);
        }
        Disabled.Clear();
        Disabled.AddRange(Scratch);
        Scratch.Clear();
    }

    /// <summary>
    /// Apply (or re-assert) <c>[Optimize] LodBias</c>. NOT a write war: the game writes
    /// <see cref="QualitySettings.lodBias"/> only indirectly, via
    /// <c>QualitySettings.SetQualityLevel</c> when the player changes a graphics preset, and that
    /// resets it to the level's own value. Re-asserting on the sweep cadence (not per frame) puts
    /// the dial back after such a change without ever contending for the value.
    /// </summary>
    private static void ApplyLodBias()
    {
        float wanted = VRSession.IsRunning ? PerfConfig.LodBiasOverride : 0f;
        if (wanted <= 0f)
            return;
        float live = QualitySettings.lodBias;
        if (!_lodBiasHeld)
        {
            _lodBiasOriginal = live;
            _lodBiasHeld = true;
            VRLog.Info(Scope, $"[Optimize] LodBias = {wanted:F2} — QualitySettings.lodBias was "
                              + $"{live:F2} (the quality level's own value) and is now held at "
                              + $"{wanted:F2}. Every LODGroup transition moves {wanted / Mathf.Max(live, 1e-4f):F2}x "
                              + "further out, which is finer meshes for longer and more submission "
                              + "volume — the [Perf] SPLIT line prices it. Set the entry back to 0 "
                              + "to restore the level's own value.");
        }
        if (!Mathf.Approximately(live, wanted))
            QualitySettings.lodBias = wanted;
    }

    private static bool ReleaseLodBias()
    {
        if (!_lodBiasHeld)
            return false;
        try
        {
            QualitySettings.lodBias = _lodBiasOriginal;
        }
        catch (Exception)
        {
            // A quality-level change during teardown can make this throw; nothing to do about it.
        }
        _lodBiasHeld = false;
        return true;
    }

    // ==========================================================================================
    //  data
    // ==========================================================================================

    private struct Census
    {
        public int Total;
        public int Enabled;
        public int Roots;
        public int Dependents;
        public int ModeUnityLodGroup;
        public int ModeSwitchMesh;
        public int ModeSwitchGameObject;
        public int ModeUnreadable;
        public int Inert;
        public int EvalCoverage;
        public int EvalDistance;
        public int WithOriginalMesh;
        public int NeverSwitched;
        public int NewlyDisabled;
        public int DisabledHeld;
        public float WalkMs;
        public int[] Levels;
        public int[] CurrentLevel;
    }

    private struct NearestGroup
    {
        public LODGroup? Group;
        public string? Name;
        public float Distance;
        public float Size;
        public float RelativeHeight;
        public int Level;
        public int Levels;
    }

    /// <summary>
    /// Sweep host. Fully guarded and SELF-DISABLING on the first throw, for the same reason
    /// <c>PerfMonitor.PerfHost</c> is: instrumentation that can throw is worse than no
    /// instrumentation, because an unguarded Update takes the rest of the frame's Updates with it
    /// and can starve the input pipeline.
    /// </summary>
    private sealed class AutoLodHost : MonoBehaviour
    {
        private bool _faulted;

        private void Update()
        {
            if (_faulted)
                return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                _faulted = true;
                try
                {
                    RestoreDisabled();
                    ReleaseLodBias();
                }
                catch (Exception)
                {
                    // best effort — the report below is what matters
                }
                VRLog.Error(Scope, "[Perf] LOD sweep threw and DISABLED ITSELF; every AutomaticLOD "
                                   + "it had switched off has been switched back on and lodBias is "
                                   + $"restored: {e}");
            }
        }
    }
}
