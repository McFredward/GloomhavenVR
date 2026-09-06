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
/// <para><b>WHY A MISSED RESTORE CANNOT LATCH.</b> The <c>SetGlobalFloat(_EnableOcclusionMap, 1f)</c>
/// is INSIDE the generator's command buffer, which re-executes every frame the ScenarioCamera
/// renders. So even if <c>onPostRender</c> never fires for a head-camera pass, the game itself puts
/// the global back on the very next frame. This class restores it anyway, and its line reports the
/// restore count beside the suppress count so a mismatch is visible rather than assumed.</para>
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
            _preMin = float.PositiveInfinity;
            _preMax = float.NegativeInfinity;
            _camName = "";
            _reported = false;
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
        if (cam == null || !ReferenceEquals(cam, VRRigDriver.HeadCamera))
            return;

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
    /// </summary>
    private static void Report(string closing, bool force)
    {
        if (_reported || (!force && _suppressed < 120))
            return;
        if (!force && _suppressed == 0)
            return; // unreachable via the render-loop caller; forced callers own the INERT line
        _reported = true;

        string verdict = _suppressed == 0
            ? "*** INERT — THE HOOK NEVER MATCHED A HEAD CAMERA, so the gate was never down and "
              + "NOTHING was switched off. This session's report says nothing about the occlusion "
              + "map either way; do not read 'the white was unchanged' as an exclusion. ***"
            : _preAlreadyZero == _suppressed
                ? "*** NULL PERTURBATION — the global was ALREADY 0 on every pass, so nothing was "
                  + "switched off and 'the white is unchanged' would be an ABSENCE and not an "
                  + "EXCLUSION. This is the reading round twelve's experiment turned out to have. ***"
                : _readBackNonZero > 0
                    ? "*** THE WRITE DID NOT TAKE on some passes — read-back was non-zero "
                      + $"{_readBackNonZero} time(s). Treat any 'no change' report as INERT. ***"
                    : "*** WORKING — the gate was genuinely down for every head-camera pass. A 'no "
                      + "change' report is now a real EXCLUSION of the occlusion channel. ***";

        // HW-VERIFY: this is the answer-bearing line of round fifteen's experiment. It must stay at
        // a tier the DEFAULT log level prints. scripts/check-hw-verify.py enforces the position of
        // this comment.
        VRLog.Note(Scope, $"[Props] OCCLUSION GATE A/B — {closing}. {verdict} "
            + $"HEAD CAMERA MATCHED: '{(_camName.Length == 0 ? "<none>" : _camName)}'. "
            + $"Head-camera passes SUPPRESSED {_suppressed}, RESTORED {_restored} "
            + $"(a shortfall does not latch: the game re-publishes _EnableOcclusionMap = 1 from "
            + $"inside TilesOcclusionGenerator's command buffer every frame the ScenarioCamera "
            + $"renders, so the worst case is one frame). VALUE FOUND BEFORE THE WRITE: "
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
