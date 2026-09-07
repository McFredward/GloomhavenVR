using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE PRE-REGISTERED A/B FOR THE HELD-PROP WHITE FLASH — round fifteen, and it is an EXPERIMENT
/// rather than a fix. It holds <c>_EnableOcclusionMap</c> at 0 for the duration of the VR head
/// camera's own render pass and puts it back afterwards, so one hold decides whether the game's
/// screen-space occlusion map is what paints a held trap ivory white.
///
/// <para><b>WHY THE GLOBAL IS THE RIGHT LEVER.</b> <c>tools/ShaderDisasm/FINDINGS.md</c> is a
/// DISASSEMBLY, not an inference: in <c>VFX/ParticleMasterUnlitAdd_Shd</c> the whole occlusion term
/// is wrapped in <c>movc r0.x, (_EnableOcclusionMap != 0), computed, 1</c> — at 0 the term is forced
/// to 1 and bypassed. It is a runtime uniform branch and NOT a shader keyword, which is why one
/// float switches it and why a <c>MaterialPropertyBlock</c> was not needed to reach it.</para>
///
/// <para><b>WHY THE MAP IS SUSPECT AT ALL.</b> The generator's command buffer is attached at
/// <c>CameraEvent.BeforeGBuffer</c> on the game's <c>ScenarioCamera</c>
/// (decompiled <c>GH.Runtime/TilesOcclusionGenerator.cs</c>), and it publishes
/// <c>_TilesOcclusionMap</c>, <c>_ObjectOcclusion</c> and <c>_EnableOcclusionMap = 1</c> as GLOBALS.
/// Any shader sampling one of them on the head camera does so through <c>ComputeScreenPos</c>, i.e.
/// in the HEAD camera's screen space, against a map authored in the ScenarioCamera's. The ModBuild
/// 465 <c>HELD-PROP POSITION SWEEP</c> measured that mismatch as a number — head-to-generator UV gap
/// <c>1.059 / 1.084 / 1.116 / 1.204</c> across its four rungs, against its own pre-registered bar of
/// 0.5 for "fetches a part of the map belonging to a different part of the world".</para>
///
/// <para><b>AND WHY IT IS RUN NOW AND WAS NOT RUN BEFORE.</b> §21.4 of
/// <c>.planning/held-prop-flash-experiments.md</c> made this experiment conditional on one reading:
/// <i>"Run it only if channel 5 reports a non-zero registered count."</i> The 465 log reports
/// <c>renderers under the prop present in m_ObjectRenderers: 1</c>, and the volume's own row reads
/// <c>'OcclusionVolume' enabled True, MeshRenderer on the same object: YES, present in
/// m_ObjectRenderers: YES</c>. The precondition is met. (It also corrects round fourteen: the trap
/// carries its <c>ObjectOcclusionVolume</c> on a CHILD that has its own <c>MeshRenderer</c>, so the
/// "a volume on a skinned prop registers nothing" rule — true in general, since
/// <c>m_ObjectRenderers</c> is a <c>List&lt;MeshRenderer&gt;</c> — does not apply to this prop.)</para>
///
/// <para><b>THE SHAPE THE PICTURE ASKS FOR, MEASURED FROM THE USER'S OWN VIDEO.</b>
/// <c>.planning/debug/falle_aufblitzen.mp4</c>, photometry at 10 Hz in LINEAR light: the flash is a
/// saturating RAMP of about 1.0-1.4 s that then vanishes COMPLETELY inside one 0.1 s step, twice in
/// eighteen seconds. The light it adds is NEUTRAL — the delta normalised to red reads
/// <c>(1.000, 0.995, 1.010)</c> on the stone ring — which is what rules OUT this mod's own overlays,
/// whose only two tints are amber <c>(1.00, 0.62, 0.26)</c> and cool blue
/// <c>(0.45, 0.62, 1.00, 0.30)</c>. A wall FINISHING a fade produces exactly that shape: the
/// occlusion term ramps in while the wall dissolves and drops in a single frame when the wall stops
/// being drawn into the map at all.</para>
///
/// <para><b>THE COST, STATED RATHER THAN HIDDEN.</b> The gate is GLOBAL, so holding it at 0 for the
/// head camera's pass also strips the occlusion term from every OTHER shader that reads it during
/// that pass — the flame and wall-fade path <c>ParticleMasterUnlitAdd_Shd</c> among them. Flames
/// will show through walls while this is on. That is why it is a dial, why the dial is OFF by
/// default, and why its description says so in both languages: the user turns it on for one hold
/// and turns it off again.</para>
///
/// <para><b>WHY A MISSED RESTORE CANNOT LATCH — AND IT IS MEASURED, NOT ASSERTED.</b> The
/// <c>SetGlobalFloat(_EnableOcclusionMap, 1f)</c> is INSIDE the generator's command buffer
/// (decompiled <c>GH.Runtime/TilesOcclusionGenerator.cs:191</c>, added at
/// <c>CameraEvent.BeforeGBuffer</c> on line 192), which re-executes every frame that camera
/// renders. VR does not stop it rendering: <c>VRCameraPolicy</c> forces game cameras to
/// <c>StereoTargetEyeMask.None</c>, which makes them desktop-only, NOT disabled. So even if
/// <c>onPostRender</c> never fires for a head-camera pass, the game itself puts the global back on
/// the very next frame the ScenarioCamera draws.</para>
///
/// <para>That was the whole argument until ModBuild 467, and it was an assertion about a camera
/// nobody had counted. The line now carries <c>GENERATOR CAMERA PASSES</c> beside the head-camera
/// count: they are the renders of <c>TilesOcclusionGenerator.s_Instance</c>'s own camera seen from
/// the same hook, in the same session. A count at or above the head-camera count means the buffer
/// really did re-run between our passes and the worst case of a missed restore is one frame; a
/// count of 0 means the premise did NOT hold this session and a restore shortfall would latch until
/// the dial is turned off. The class restores it anyway, and reports the restore count beside the
/// suppress count so a mismatch is visible rather than assumed.</para>
///
/// <para><b>WHY THE WRITE IN THE RENDER PHASE IS ALLOWED HERE.</b> The project's rule
/// (<c>CanvasConversion.AssertNotInRenderPhase</c>, quoted in <c>CameraOrderProbe</c>) is about
/// VISIBILITY and ORDERING — enabling renderers or moving camera depths mid-frame, which lands in
/// one eye and not the other. This writes a shader uniform in <c>onPreRender</c> and restores it in
/// <c>onPostRender</c> of the SAME camera, so every eye pass is bracketed symmetrically and no
/// renderer's visibility or order changes. It is the same scoping <c>FlatScreenStereo</c> already
/// uses for its per-render material override.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here is game state, nothing is sent, nothing is read from a
/// peer: it is a local shader uniform held across one local camera's render. A peer running with the
/// dial off sees exactly what they saw before, and a flat player is untouched — the head camera does
/// not exist for them.</para>
/// </summary>
internal static class PropOcclusionGate
{
    private const string Scope = "FigureGrab";

    /// <summary>The game's master gate for the whole occlusion term (proven by disassembly).</summary>
    private static readonly int EnableOcclusionMapId = Shader.PropertyToID("_EnableOcclusionMap");

    private static bool _hooked;

    /// <summary>True between our own <c>onPreRender</c> and <c>onPostRender</c> for the head camera.</summary>
    private static bool _inside;

    /// <summary>The value we took out, put back verbatim rather than assumed to be 1.</summary>
    private static float _saved = 1f;

    // ---- what the one answer-bearing line reports -------------------------------------------
    private static int _suppressed;      // head-camera renders we wrote 0 into
    private static int _restored;        // head-camera renders we wrote the old value back into
    private static int _preAlreadyZero;  // renders where the value was ALREADY 0 before we touched it
    private static int _readBackNonZero; // renders where reading straight back after the write was NOT 0
    private static float _preMin = float.PositiveInfinity;
    private static float _preMax = float.NegativeInfinity;
    private static string _camName = "";
    private static bool _reported;

    // ---- WHAT THE HOOK ACTUALLY SAW, so the "it never matched" verdict can NAME its cause ----
    //
    // ModBuild 466 shipped a verdict that asserted a mechanism it could not observe. Its whole
    // INERT branch keyed on `_suppressed == 0` and said "THE HOOK NEVER MATCHED A HEAD CAMERA" —
    // which is only ONE of the two ways that count reaches zero, and it was the wrong one. The 466
    // log has `OCCLUSION GATE A/B ARMED` ZERO times and the INERT line exactly once, from module
    // teardown: the hook was never installed at all, because the dial was off all session (the user
    // could not find it — see the Test-Auslöser row that now carries it). Meanwhile the sibling
    // instrument in this same folder read `HEAD CAMERA: 'GloomhavenVR.HeadCamera'` FOUR times in
    // that same session off the very same `VRRigDriver.HeadCamera` property, so the camera match
    // this line accused was demonstrably fine. A reader who trusted the sentence would have spent a
    // round rewriting a correct match.
    //
    // These five fields are what it takes for the line to report the cause rather than guess it:
    // whether the hook was ever installed, how many camera renders went past it, how many of those
    // found a null head camera, which cameras they were, and how often the generator's own camera
    // rendered (the missed-restore premise, counted instead of asserted).
    private static bool _everArmed;
    private static int _armedAtFrame = -1;
    private static int _camPasses;
    private static int _headNullPasses;
    private static int _genPasses;

    /// <summary>Distinct non-matching camera names, capped: this names a cause, it is not a census.</summary>
    private static readonly List<string> OtherCameras = new(MaxNamedCameras);

    private const int MaxNamedCameras = 8;

    /// <summary>Cached so the generator lookup is a static field read per callback, not a GetComponent.</summary>
    private static TilesOcclusionGenerator? _gen;
    private static Camera? _genCam;

    /// <summary>
    /// Arm or disarm to match the dial. Idempotent, so it is safe to call from module init AND from
    /// the config's <c>SettingChanged</c> — the user flipping it in the VR options menu takes effect
    /// on the next frame without a restart, which is the whole point of a one-hold experiment.
    /// </summary>
    internal static void Sync()
    {
        bool wanted = FigureGrabConfig.OcclusionMapOffOnHeadCamera != null
                      && FigureGrabConfig.OcclusionMapOffOnHeadCamera.Value;
        if (wanted == _hooked)
            return;
        _hooked = wanted;

        if (wanted)
        {
            _suppressed = _restored = _preAlreadyZero = _readBackNonZero = 0;
            _camPasses = _headNullPasses = _genPasses = 0;
            OtherCameras.Clear();
            _gen = null;
            _genCam = null;
            _preMin = float.PositiveInfinity;
            _preMax = float.NegativeInfinity;
            _camName = "";
            _reported = false;
            _everArmed = true;
            _armedAtFrame = Time.frameCount;
            Camera.onPreRender += OnPreRender;
            Camera.onPostRender += OnPostRender;
            // HW-VERIFY: this line says the experiment is ARMED. It is not the answer — the
            // OCCLUSION GATE A/B line below is — but a round that reads "no change" has to be able
            // to tell "the gate was off the whole time" from "the gate was held down and the
            // picture did not care", and those two are different findings.
            VRLog.Note(Scope, "[Props] OCCLUSION GATE A/B ARMED — _EnableOcclusionMap will be held "
                + "at 0 for the VR head camera's own render pass and restored immediately after it. "
                + "THIS IS AN EXPERIMENT, NOT A FIX, AND IT HAS A COST THE PLAYER WILL SEE: the "
                + "gate is the game's GLOBAL master switch for the occlusion term "
                + "(tools/ShaderDisasm/FINDINGS.md proves it by disassembly), so while it is down "
                + "the flame and wall-fade shaders lose their occlusion too and flames will show "
                + "through walls. Turn it on for ONE hold, look at the trap in your hand, turn it "
                + "off again. WHAT THE ANSWER LOOKS LIKE: white GONE while this is on => the "
                + "prop's shader samples the map and the camera mismatch is the painter; white "
                + "UNCHANGED => the occlusion channel is excluded BY EXPERIMENT and the last "
                + "candidate is the shader's own view-dependence.");
        }
        else
        {
            Camera.onPreRender -= OnPreRender;
            Camera.onPostRender -= OnPostRender;
            RestoreNow("dial turned off");
            Report("the dial was turned off", force: true);
        }
    }

    /// <summary>Module shutdown / hot reload: unhook and hand the global back.</summary>
    internal static void Reset()
    {
        if (_hooked)
        {
            Camera.onPreRender -= OnPreRender;
            Camera.onPostRender -= OnPostRender;
            _hooked = false;
        }
        RestoreNow("module shutdown");
        Report("the module shut down", force: true);
    }

    private static void OnPreRender(Camera cam)
    {
        if (cam == null)
            return;

        // EVERY camera render is counted BEFORE the match, and that is the whole repair to round
        // fifteen's verdict: a hook that saw 0 renders, a hook that saw 4000 renders and none of
        // them the head, and a hook that was never installed are three different findings, and the
        // shipped line could tell none of them apart.
        _camPasses++;
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            _headNullPasses++;
        NoteGeneratorPass(cam);

        if (!ReferenceEquals(cam, head))
        {
            // Name what DID render, so an "it never matched" line points at a camera list instead of
            // at a mechanism. Collected only until the head is found: after that it has answered,
            // and this project has already paid for a probe that kept measuring past its answer.
            if (_suppressed == 0 && OtherCameras.Count < MaxNamedCameras && !OtherCameras.Contains(cam.name))
                OtherCameras.Add(cam.name);
            return;
        }

        // A pass that never reached OnPostRender (camera torn down mid-render). Hand the value back
        // before taking a new one, so we can never save our own 0 over the game's 1.
        if (_inside)
            RestoreNow("a head-camera pass ended without onPostRender");

        _saved = Shader.GetGlobalFloat(EnableOcclusionMapId);
        _preMin = Mathf.Min(_preMin, _saved);
        _preMax = Mathf.Max(_preMax, _saved);
        if (_saved == 0f)
            _preAlreadyZero++;

        Shader.SetGlobalFloat(EnableOcclusionMapId, 0f);
        // READ IT STRAIGHT BACK. "The write ran" and "the write took" are different claims, and
        // this project has shipped a remedy whose delivery path was never verified more than once.
        if (Shader.GetGlobalFloat(EnableOcclusionMapId) != 0f)
            _readBackNonZero++;

        _inside = true;
        _suppressed++;
        if (_camName.Length == 0)
            _camName = cam.name;
    }

    private static void OnPostRender(Camera cam)
    {
        if (cam == null || !ReferenceEquals(cam, VRRigDriver.HeadCamera) || !_inside)
            return;
        Shader.SetGlobalFloat(EnableOcclusionMapId, _saved);
        _inside = false;
        _restored++;
        Report("the first head-camera passes are on record", force: false);
    }

    /// <summary>
    /// Count the renders of the camera the occlusion generator's command buffer hangs on.
    ///
    /// <para>THIS IS THE NO-LATCH PREMISE, MEASURED. The class doc argues that a missed restore
    /// cannot latch because <c>TilesOcclusionGenerator</c> re-publishes
    /// <c>_EnableOcclusionMap = 1</c> from inside a command buffer at
    /// <c>CameraEvent.BeforeGBuffer</c> every frame its camera renders. That is true of the
    /// decompiled source (line 191) and says nothing about whether the camera renders HERE, under a
    /// VR rig — which is exactly the shape of assumption this file has now been burned by once. So
    /// it is counted: the report prints these passes beside the head-camera passes, and a reader can
    /// see the buffer re-running instead of taking it on the source's word.</para>
    ///
    /// <para>The generator is resolved through its own static instance and cached, so the per-render
    /// cost is a static field read and a reference compare — never a <c>GetComponent</c> per camera
    /// per frame, which on a four-camera scene is 240 calls a second for a diagnostic.</para>
    /// </summary>
    private static void NoteGeneratorPass(Camera cam)
    {
        TilesOcclusionGenerator? gen = TilesOcclusionGenerator.s_Instance;
        if (!ReferenceEquals(gen, _gen))
        {
            _gen = gen;
            _genCam = gen != null ? gen.GetComponent<Camera>() : null;
        }

        if (_genCam != null && ReferenceEquals(cam, _genCam))
            _genPasses++;
    }

    private static void RestoreNow(string why)
    {
        if (!_inside)
            return;
        Shader.SetGlobalFloat(EnableOcclusionMapId, _saved);
        _inside = false;
        _restored++;
        VRLog.Debug(Scope, $"[Props] occlusion gate released out of band ({why}).");
    }

    /// <summary>
    /// The one line. Emitted once per arming, as soon as there is enough to read — a per-frame
    /// version of this would be 38 lines a second and this project has already paid for a probe
    /// that kept printing after it had answered.
    ///
    /// <para><paramref name="force"/> IS WHAT MAKES THE `INERT` VERDICT REACHABLE, and its absence
    /// was a real defect in the first draft of this class. The render-loop caller waits for 120
    /// head-camera passes so the numbers mean something; but a run where the hook NEVER matched a
    /// head camera has 0 passes and would therefore have printed NOTHING AT ALL — the one outcome
    /// a reader most needs told, because it is the one where "the white was unchanged" says
    /// nothing. Disarm and shutdown force the line out whatever the count is. A silent instrument
    /// reads exactly like a negative result, and this file has been burned by that before.</para>
    ///
    /// <para><b>AND THE FORCED LINE THEN NAMED THE WRONG CAUSE, which is the ModBuild 467 repair.</b>
    /// Round fifteen's verdict had ONE branch for <c>_suppressed == 0</c> and it read "THE HOOK NEVER
    /// MATCHED A HEAD CAMERA". Zero is reached two ways and that sentence only describes one of them.
    /// In the 466 session it described the wrong one: <c>OCCLUSION GATE A/B ARMED</c> appears zero
    /// times in that log, so the hook was never installed — the dial was off all session — while the
    /// sibling sweep in this folder printed <c>HEAD CAMERA: 'GloomhavenVR.HeadCamera'</c> four times
    /// off the same property the match uses. The three states are now separate branches with separate
    /// numbers behind them, and NEVER-ARMED is a short line of its own rather than three paragraphs
    /// of epistemology about an experiment nobody ran.</para>
    /// </summary>
    private static void Report(string closing, bool force)
    {
        if (_reported || (!force && _suppressed < 120))
            return;
        if (!force && _suppressed == 0)
            return; // unreachable via the render-loop caller; forced callers own the INERT line
        _reported = true;

        // THE DIAL WAS NEVER TURNED ON. Short and separate on purpose: this is the reading almost
        // every session will produce, it is not a result about the occlusion map, and three
        // paragraphs of how-to-read-the-picture under it would be noise in every log the mod writes.
        // It still has to be SAID, because a session that ran no experiment and a session whose
        // experiment changed nothing look identical from the outside.
        if (!_everArmed)
        {
            // HW-VERIFY: the NEVER-ARMED reading of round fifteen's A/B. Distinct from the INERT and
            // WORKING readings below by construction — this branch is the only one that can print
            // without an ARMED line above it in the same log.
            VRLog.Note(Scope, "[Props] OCCLUSION GATE A/B — NEVER ARMED. The dial "
                + "[FigureGrab] OcclusionMapOffOnHeadCamera was OFF for this whole session, so the "
                + "experiment did not run and NOTHING about the occlusion map was measured either "
                + "way. This is NOT a camera-match failure and it is NOT a null result: do not read "
                + "'the white was unchanged' as an exclusion of anything. The switch is in the VR "
                + "options under Erweitert > Test-Ausloeser (Test triggers), captioned "
                + "'Test: occlusion map off in VR' / 'Test: Verdeckungskarte in VR aus'; turn it on, "
                + "pick a trap up once, turn it off again, and this line becomes a reading.");
            return;
        }

        int armedFrames = _armedAtFrame < 0 ? 0 : Mathf.Max(0, Time.frameCount - _armedAtFrame);
        string others = OtherCameras.Count == 0
            ? "<none>"
            : string.Join(", ", OtherCameras.ToArray())
              + (OtherCameras.Count >= MaxNamedCameras ? ", ... (list capped)" : "");

        string verdict = _camPasses == 0
            ? $"*** INERT — ARMED, but Camera.onPreRender did not fire ONCE in {armedFrames} frame(s): "
              + "no camera rendered through the built-in callback at all, so the hook cannot have "
              + "matched or missed anything. This says nothing about the occlusion map. ***"
            : _suppressed == 0
                ? $"*** INERT — ARMED, and {_camPasses} camera render(s) went past this hook over "
                  + $"{armedFrames} frame(s), but NONE of them was the rig's own head camera "
                  + $"(VRRigDriver.HeadCamera). It read null on {_headNullPasses} of those renders; "
                  + $"the cameras that DID render were: {others}. The gate was never down, so do not "
                  + "read 'the white was unchanged' as an exclusion. ***"
                : _preAlreadyZero == _suppressed
                    ? "*** NULL PERTURBATION — the global was ALREADY 0 on every pass, so nothing was "
                      + "switched off and 'the white is unchanged' would be an ABSENCE and not an "
                      + "EXCLUSION. This is the reading round twelve's experiment turned out to have. ***"
                    : _readBackNonZero > 0
                        ? "*** THE WRITE DID NOT TAKE on some passes — read-back was non-zero "
                          + $"{_readBackNonZero} time(s). Treat any 'no change' report as INERT. ***"
                        : "*** WORKING — the gate was genuinely down for every head-camera pass. A 'no "
                          + "change' report is now a real EXCLUSION of the occlusion channel. ***";

        // The no-latch premise, counted rather than quoted. The old line asserted the command buffer
        // re-runs every frame; this one says how many times it was SEEN to.
        string latch = _genPasses == 0
            ? "GENERATOR CAMERA PASSES 0 — the camera carrying TilesOcclusionGenerator's command "
              + "buffer was NOT seen rendering while the gate was armed, so the 'a missed restore "
              + "cannot latch' argument is UNSUPPORTED this session and a shortfall above would "
              + "stand until the dial is turned off."
            : $"GENERATOR CAMERA PASSES {_genPasses} against {_suppressed} head pass(es) — the "
              + "command buffer that re-publishes _EnableOcclusionMap = 1 "
              + "(GH.Runtime/TilesOcclusionGenerator.cs:191, CameraEvent.BeforeGBuffer) really did "
              + "re-run, so the worst case of a restore shortfall is one frame. MEASURED, not assumed.";

        // HW-VERIFY: this is the answer-bearing line of round fifteen's experiment. It must stay at
        // a tier the DEFAULT log level prints. scripts/check-hw-verify.py enforces the position of
        // this comment.
        VRLog.Note(Scope, $"[Props] OCCLUSION GATE A/B — {closing}. {verdict} "
            + $"HEAD CAMERA MATCHED: '{(_camName.Length == 0 ? "<none>" : _camName)}'. "
            + $"Head-camera passes SUPPRESSED {_suppressed}, RESTORED {_restored}. {latch} "
            + "VALUE FOUND BEFORE THE WRITE: "
            + $"{_preMin:0.###}..{_preMax:0.###} over those passes, already 0 on {_preAlreadyZero} "
            + $"of them. READ-BACK AFTER THE WRITE was non-zero on {_readBackNonZero}. "
            + "HOW TO READ THIS AGAINST THE PICTURE, AND THE BAR IS THE USER'S EYE AND NOT A "
            + "NUMBER: the flash measured off .planning/debug/falle_aufblitzen.mp4 is a saturating "
            + "~1.0-1.4 s ramp that then clears completely inside one 0.1 s step, and the light it "
            + "adds is NEUTRAL (delta normalised to red 1.000, 0.995, 1.010 on the stone ring). "
            + "That neutrality is what excludes this mod's own overlays, whose only two tints are "
            + "amber (1.00,0.62,0.26) and cool blue (0.45,0.62,1.00,0.30). WHAT IS STILL BEYOND "
            + "THIS INSTRUMENT: whether Amp_Char_Shader — the shader the trap actually draws on — "
            + "reads these globals AT ALL. tools/ShaderDisasm can answer that, but the game's "
            + "asset bundles are not on the build machine (ressources/ holds only Managed/), so it "
            + "cannot be dumped here and no sibling shader's disassembly may be read as its "
            + "answer. This A/B is the substitute, and it answers the same question from the "
            + "outside: at 0 the term is bypassed in EVERY shader that reads it, whatever they are.");
    }
}
