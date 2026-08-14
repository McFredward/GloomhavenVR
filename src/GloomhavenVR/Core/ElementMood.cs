using System.Text;
using BepInEx.Configuration;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  ELEMENT MOOD — the six Gloomhaven element infusions, sensed, smoothed and published as shader
//  globals so the VR environment can react to them. THIS FILE IS THE SENSING AND PLUMBING HALF
//  ONLY: it renders nothing. The art (shaders, particles, light) is a separate lane and reads the
//  channel documented under "THE PUBLISHED CHANNEL" below.
// =================================================================================================

/// <summary>
/// Publishes the live element-infusion board as two global shader vectors, smoothed over time, so
/// any material in the mod's bundled environment can tint, flicker or bloom with the elements that
/// are currently up — without any of those materials needing a script, a component or a reference
/// to anything in this mod.
///
/// <para><b>THE PUBLISHED CHANNEL (the contract — this class doc is the ONE canonical place; the
/// bundle's shaders quote these names verbatim and nothing else in the mod may write them).</b>
/// Two <c>float4</c> globals, set with <see cref="Shader.SetGlobalVector"/>:</para>
/// <code>
///   _GhvrElemA = float4(Fire,  Ice,  Air,   Earth)   // each 0..1
///   _GhvrElemB = float4(Light, Dark, Master, Peak)
/// </code>
/// <list type="bullet">
/// <item><b>x,y,z,w of A and x,y of B</b> — the per-element INTENSITY, 0..1, already smoothed (see
/// "THE CURVE"). Index order is the game's own <c>EElement</c> order
/// (<c>ElementInfusionBoardManager.EElement</c>, ScenarioRuleLibrary/ElementInfusionBoardManager.cs:10-20),
/// so <c>_GhvrElemA.x</c> is Fire because <c>EElement.Fire == 0</c>.</item>
/// <item><b>_GhvrElemB.z — MASTER.</b> The player's on/off toggle and intensity dial folded into ONE
/// number ([Elements] EnvironmentResponse × [Elements] ResponseStrength). It is deliberately NOT
/// pre-multiplied into the six, so a shader needs exactly one extra multiply and one uniform read
/// to honour the setting: <c>fire = _GhvrElemA.x * _GhvrElemB.z</c>. 0 means "the feature is off,
/// draw nothing" — and 0 is what stands whenever the channel is not live (no scenario, no VR
/// session, mixed reality on, teardown), so a material that respects master can never be caught
/// showing a stale mood.</item>
/// <item><b>_GhvrElemB.w — PEAK.</b> The largest of the six intensities, BEFORE master. Purely a
/// convenience: it is the one component a shader can branch or LOD on ("is anything up at all?")
/// without reading and max-ing two vectors. Redundant by construction, and cheap — one float
/// compare per element in a loop this file already runs.</item>
/// </list>
/// <para>WHY GLOBALS AND NOT A COMPONENT. There is currently no way for the mod to reach a material
/// on a spawned environment prefab: <see cref="SkyAlternative"/> writes none (no
/// <c>SetFloat</c>/<c>sharedMaterial</c>/<c>MaterialPropertyBlock</c> anywhere in it), its
/// <c>_skyGo</c>/<c>_roomGo</c> roots are <c>private static</c> with no accessor
/// (SkyAlternative.cs:574-575), and the asset bundle ships no MonoBehaviours at all. The one
/// channel that already crosses that gap is a global shader value with a single writer —
/// <c>Shader.SetGlobalFloat(_GhvrTimeOfs)</c> in <c>SkyAlternative.ApplyTimeOfs</c>
/// (SkyAlternative.cs:1863-1872) — and this mirrors it exactly, including the one-writer
/// discipline, the NaN/Inf rejection and the write-only-on-change rule.</para>
///
/// <para><b>REJECTED: a per-material MaterialPropertyBlock sweep.</b> It would need a renderer walk
/// of the spawned environment every time the mood changed, a name-to-renderer mapping the bundle
/// does not carry, and it would still miss particle systems. A global is one write for the whole
/// scene and is what the existing clock channel already proved works across the bundle boundary.</para>
///
/// <para><b>SOURCE, AND WHY THIS POLLS.</b> The board is
/// <c>ElementInfusionBoardManager</c> — entirely <c>static</c>, no instance, no null check
/// (ScenarioRuleLibrary/ElementInfusionBoardManager.cs:8). The one accessor used here is
/// <c>ElementColumn(EElement)</c> (:92-95); <c>GetElementColumn</c> (:42) is deliberately NOT used
/// because it hands out the LIVE backing array and a game site mutates through it
/// (GH.Runtime/LevelEditorElementInfusePanel.cs:90). There is NO event, delegate or observable for
/// a change: nine separate methods write the board (<c>Reset</c> :44, <c>RestoreState</c> :65,
/// <c>SetElementColumn</c> :72, <c>EndTurn</c> :97, <c>EndRound</c> :122, <c>Consume</c> :205,
/// <c>EnemyConsume</c> :225, <c>SetElementInstantly</c> :264) and none funnels through a common
/// setter. So this polls, in exactly the shape the mod's existing reader of this same state uses
/// (<c>Net/RemoteElementStrip.Refresh</c>, RemoteElementStrip.cs:154-180): six
/// <c>ElementColumn(i)</c> reads, a base-3 signature as the change detector, a try/catch around the
/// read.</para>
///
/// <para><b>ONLY 0..5 ARE READ.</b> <c>EElement</c> has a seventh member, <c>Any</c> (index 6),
/// which is not an element: the backing array is length 7 and <c>Consume(EElement.Any, …)</c>
/// writes index 6 (:44, :205). The game's own HUD repaint loops <c>for (i = 0; i &lt; 6; i++)</c>
/// for the same reason (GH.Runtime/InfusionBoardUI.cs:220-222).</para>
///
/// <para><b>MULTIPLAYER: ZERO NEW WIRE BYTES, and that is a decision, not an omission.</b> The
/// standing project rule is that every feature must be multiplayer-compatible, so the next reader
/// will ask — the answer is that the GAME already replicates this state and treats a mismatch as a
/// desync. <c>ScenarioState.ElementColumn</c> is serialized (ScenarioRuleLibrary/ScenarioState.cs:110,
/// 704, 846), restored into the live board (:1374), and compared every round as a DESYNC INVARIANT
/// with its own mismatch codes 117/118 in <c>ScenarioState.CompareStates(…, isMPCompare: true)</c>
/// (:1992-2032), run from <c>Choreographer.StartMPEndOfRoundCompare()</c>
/// (GH.Runtime/Choreographer.cs:14320-14340). Putting element columns on the mod's wire would
/// therefore duplicate a value the game guarantees is already identical, and would create a second
/// source of truth that could disagree with the one the game desync-checks against. The mod already
/// classified this exact state the same way for the remote element strip
/// (Net/RemoteElementStrip.cs:23-26, 53-55: "CLASSIFICATION: GLOBAL — scenario-wide state,
/// bit-identical on every client, ZERO wire"). The only thing that is NOT automatically identical
/// is the SMOOTHING, which is why the curve is driven by the shared clock — see below.</para>
///
/// <para><b>THE CURVE.</b> Each element carries a 0..1 intensity rather than the raw enum:</para>
/// <list type="bullet">
/// <item><c>Strong</c> → target <b>1.00</b>, rock steady. This is the state the player also HEARS
/// (<c>InfusionElementUI.SetState</c> plays <c>changeToStrongElementAudioItem</c> on the transition
/// into Strong, GH.Runtime/InfusionElementUI.cs:120-134), so it is the one that must read as "fully
/// charged".</item>
/// <item><c>Waning</c> → a plateau of <b>0.40</b> that BREATHES: 0.40 ± 0.12, i.e. 0.28…0.52 on a
/// 2.4 s sine. The breath is the point — it is what lets a player see from across the table that an
/// element is on its way out without reading the HUD, and it is unmistakably different from Strong,
/// which does not move at all. <b>REJECTED: a monotone decay across the waning round.</b> Nothing
/// tells this code when the round ends — <c>EndRound</c> (:122) is event-driven, not timed, so a
/// decay ramp would have to guess a duration and would then either finish early (showing 0 while
/// the element is still usable) or be cut off mid-fall.</item>
/// <item><c>Inert</c> → <b>0.00</b>.</item>
/// <item>Every transition RAMPS over <see cref="RampSeconds"/> = <b>1.0 s</b> on a smoothstep, from
/// whatever the intensity happened to be at the moment of the change — so a Strong→Waning→Inert
/// chain never pops and never restarts from the wrong value.</item>
/// </list>
///
/// <para><b>WHY THE SHARED CLOCK, PRECISELY.</b> The curve is a function of
/// <see cref="SkyAlternative.EnvClockSeconds"/> (SkyAlternative.cs:1746) — the mod's shared
/// multiplayer environment epoch — and not of <c>Time.time</c>. The two halves of the curve depend
/// on it differently, and the distinction is worth stating because getting it wrong is invisible:
/// <list type="number">
/// <item>The RAMP is a DIFFERENCE of two readings of that clock (<c>clock − since[i]</c>), so the
/// shared offset cancels and the ramp would numerically survive <c>Time.time</c>. What still has to
/// hold is that both readings come from the SAME monotone base — mixing bases (anchor on one clock,
/// evaluate on another) would produce a constant error the size of the offset, which is exactly the
/// bug this note exists to prevent. Two clients can still differ here by their DETECTION SKEW: each
/// anchors <c>since[i]</c> when its own poll first sees the change. That skew is bounded by one
/// frame of polling plus whatever the game's own replication costs, i.e. a few percent of a 1 s
/// ramp.</item>
/// <item>The waning BREATH is an ABSOLUTE-PHASE function of the clock (<c>sin(2π·clock/period)</c>).
/// The offset does NOT cancel there, and this is the whole reason the shared clock is used: on
/// <c>Time.time</c> two players would watch the same waning element breathe in opposite phase.</item>
/// </list></para>
///
/// <para><b>THE DEGRADATION, STATED RATHER THAN HIDDEN.</b> The environment clock is only actually
/// SHARED while the style is <c>Cellar</c> or <c>SwampNight</c>: <c>SkyAlternative.WireStyleCode</c>
/// (SkyAlternative.cs:1785) reports 0 for <c>Default</c>, for <c>OffBlack</c> and under mixed
/// reality, and with nothing to agree about the offset stays 0 — so on those styles
/// <c>EnvClockSeconds</c> is just <c>Time.timeSinceLevelLoad</c>, a per-client clock, and the
/// breath phase is per-client. That costs nothing today (those styles draw no mod environment for
/// the art to breathe on) and it costs nothing tomorrow either, because it is the same clock the
/// rest of the environment already runs on: whatever the art lane animates will be exactly as
/// shared as the candle flicker and the rat beside it, no more and no less.</para>
///
/// <para><b>SCOPE AND COST.</b> Ticked from <see cref="SkyAlternative.Tick"/> — the environment
/// driver, and the one entry point that already sees every relevant state: it runs once per frame
/// under <c>MixedReality.Tick</c>'s MR-OFF branch (MixedReality.cs:684), and the MR-ON branch calls
/// <c>SkyAlternative.StandDown()</c> (MixedReality.cs:692) which stands this down too. Sitting here
/// rather than in <c>VRRigDriver._tailSteps</c> is deliberate: that array is a LOCKED frame order
/// (.planning/refactor/FRAME-ORDER.lock, <c>VRRigDriver._tailSteps</c>) whose last step must stay
/// last, and this feature has no ordering relationship with any camera-state step in it. Gating is
/// scenario-scoped on <c>Events.VRModeStateMachine.ScenarioBoardExists</c>, the same condition the
/// environment itself uses (SkyAlternative.cs:767).</para>
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide game state, bit-identical on every client and
/// desync-checked by the game itself, ZERO wire. Same classification and same reasoning as
/// <c>Net/RemoteElementStrip</c>. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class ElementMood
{
    // ---- the contract, as identifiers -----------------------------------------------------------

    /// <summary>Global <c>float4(Fire, Ice, Air, Earth)</c>. Quote this name, not a literal.</summary>
    internal const string ElemAName = "_GhvrElemA";

    /// <summary>Global <c>float4(Light, Dark, Master, Peak)</c>. Quote this name, not a literal.</summary>
    internal const string ElemBName = "_GhvrElemB";

    private static readonly int ElemAId = Shader.PropertyToID(ElemAName);
    private static readonly int ElemBId = Shader.PropertyToID(ElemBName);

    // ---- the curve, in numbers ------------------------------------------------------------------

    /// <summary>How long a transition takes, in shared-clock seconds. One second: long enough that
    /// the ~10 ms detection skew between two clients is a few percent of it, short enough that the
    /// environment has visibly answered before the player has finished reading the HUD chip.</summary>
    private const float RampSeconds = 1.0f;

    /// <summary>Waning sits at this, well under Strong's 1.0 — the gap is the READING, not a
    /// dimming: a glance must separate "charged" from "about to go".</summary>
    private const float WaningPlateau = 0.40f;

    /// <summary>Half-depth of the waning breath (0.40 ± 0.12 → 0.28…0.52). Big enough to be a
    /// motion rather than a shimmer, small enough that waning never brushes Strong.</summary>
    private const float WaningEbbAmplitude = 0.12f;

    /// <summary>Breath period in shared-clock seconds. Slow — this is a "running out" signal, not
    /// an alarm.</summary>
    private const float WaningEbbPeriodSeconds = 2.4f;

    /// <summary>Below this, a component change is not worth a uniform write. Two orders under the
    /// smallest step the art can show, and it is what makes the settled case (nothing waning,
    /// nothing ramping) cost zero shader writes per frame.</summary>
    private const float WriteEpsilon = 0.002f;

    /// <summary>The six real elements. <c>EElement.Any</c> (index 6) is NOT one — see the class
    /// doc, and the game's own <c>for (i = 0; i &lt; 6; i++)</c> in
    /// <c>InfusionBoardUI.UpdateBoard</c>.</summary>
    private const int Count = 6;

    // ---- config ---------------------------------------------------------------------------------

    /// <summary>Master switch. OPTIONAL CONTENT under the standing settings ruling: it adds or
    /// removes an effect, it does not repair a broken interaction.</summary>
    internal static ConfigEntry<bool> EnvironmentResponse = null!;

    /// <summary>Intensity dial, folded into the published master so a shader needs one multiply.</summary>
    internal static ConfigEntry<float> ResponseStrength = null!;

    private static bool _bound;

    /// <summary>
    /// Bind the two dials into the RIG module's config file, riding along after
    /// <c>SkyAlternative.BindConfig</c> — the same file and the same ride-along pattern the
    /// environment choice already uses (Rig/RenderQuality.cs, which owns that file), because these
    /// two settings are read in the same breath as <c>[Sky] Style</c> and belong next to it on disk.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;

        EnvironmentResponse = file.Bind("Elements", "EnvironmentResponse", Defaults.ElementMoodEnabled,
            "Let the 3D environment react to the ELEMENT INFUSIONS on the board (fire, ice, air, "
            + "earth, light, dark). ON publishes the live element state to the environment's "
            + "materials, so the surroundings can answer the elements that are currently up — a "
            + "freshly infused element reads as fully charged, a waning one slowly breathes so you "
            + "can see it is about to go out without reading the element strip, and the response "
            + "fades in and out over about a second instead of popping. OFF removes the reaction "
            + "completely and costs nothing at all: one value is published once to switch every "
            + "effect off and then nothing is read or written per frame. Purely local presentation "
            + "— it changes NOTHING about the game state and adds NO network traffic, because the "
            + "element board is scenario-wide state the game already keeps identical on every "
            + "client (and checks for desync itself every round). Only inside a running scenario, "
            + "like the environment itself; mixed reality switches it off with the environment. "
            + "Applies live.");

        ResponseStrength = file.Bind("Elements", "ResponseStrength", Defaults.ElementMoodStrength,
            new ConfigDescription(
                "How strongly the environment answers the elements. 1 = as designed. Lower is "
                + "subtler, 0 is the same as switching the reaction off, above 1 overdrives it. "
                + "Has no effect at all while 'EnvironmentResponse' is off. Applies live.",
                new AcceptableValueRange<float>(0f, 2f)));
    }

    // ---- live state ------------------------------------------------------------------------------

    /// <summary>Last observed column per element (the raw game state).</summary>
    private static readonly ElementInfusionBoardManager.EColumn[] Column =
        new ElementInfusionBoardManager.EColumn[Count];

    /// <summary>Published intensity per element, 0..1.</summary>
    private static readonly float[] Value = new float[Count];

    /// <summary>Intensity at the moment of the last transition — the ramp's FROM.</summary>
    private static readonly float[] From = new float[Count];

    /// <summary>Shared-clock time of the last transition — the ramp's anchor.</summary>
    private static readonly float[] Since = new float[Count];

    /// <summary>Base-3 signature of the six columns; the edge detector, exactly as
    /// <c>RemoteElementStrip.Refresh</c> uses it. -1 = nothing observed yet.</summary>
    private static int _signature = -1;

    /// <summary>The last vectors actually written, so a write happens only on a real change.</summary>
    private static Vector4 _lastA;
    private static Vector4 _lastB;

    /// <summary>The channel is live (gates passed) and is publishing a mood.</summary>
    private static bool _live;

    /// <summary>Zeros have been published at least once. Starts false so the very first tick — even
    /// one that lands straight in the off path — writes the master 0 the art multiplies by, rather
    /// than trusting that nothing else ever wrote these globals.</summary>
    private static bool _zeroed;

    // ---- per-frame driver -------------------------------------------------------------------------

    /// <summary>
    /// One frame of sensing, smoothing and publishing. Called from the top of
    /// <see cref="SkyAlternative.Tick"/>, before its style branch — the mood is scoped to the
    /// SCENARIO, not to a particular environment style, so that the art lane is free to answer the
    /// elements on anything the mod draws rather than only on the two bundled shells.
    ///
    /// <para>POLLING CADENCE: EVERY FRAME, and the argument is that a slower rate cannot be
    /// cheaper in any way that matters. The sensing is six reads of a <c>static</c> array behind a
    /// one-line accessor — no scene walk, no <c>Find</c>, no allocation — while the SMOOTHING has
    /// to be evaluated every frame anyway to be smooth, so a poll throttle would add a timer and a
    /// second code path to save six array reads out of a loop that is already running. What the
    /// per-frame poll buys is that the environment answers within one frame of the state changing,
    /// which is the same frame budget the game's own HUD repaint has.</para>
    ///
    /// <para>REJECTED: patching the HUD repaint choke point. <c>InfusionBoardUI.UpdateBoard</c>
    /// (GH.Runtime/InfusionBoardUI.cs:204) is genuinely the one place every element repaint ends
    /// up, and <c>InfusionElementUI.SetState</c> (GH.Runtime/InfusionElementUI.cs:88, 120-134) is
    /// where the player hears the "element charged" chime. A Harmony postfix there would make the
    /// environment move on exactly the chime's frame instead of within a frame or two of it — worth
    /// at most ~22 ms of lead on a response that then takes a full second to ramp, i.e. about 2 % of
    /// the transition. Against that: a new patch on a game UI method (patch inventory, patch-surface
    /// gate, a call path that also runs on the loading screen), for a channel that must keep working
    /// when that UI does not exist at all. The poll is the smaller thing, and it is the shape the
    /// mod already uses for this exact state.</para>
    /// </summary>
    internal static void Tick()
    {
        // The dials live in the rig file; RenderQuality.Bind rides both SkyAlternative.BindConfig
        // and this one along, so one call is enough and it is idempotent.
        if (!_bound)
            Rig.RenderQuality.Bind();

        // OFF ⇒ master 0 once, then nothing. This is the whole of requirement "costs nothing when
        // off": one bool read per frame and an early return.
        if (!EnvironmentResponse.Value)
        {
            StandDown("the setting is off");
            return;
        }

        if (!VRSession.IsRunning)
        {
            StandDown("VR is not running");
            return;
        }

        // MIXED REALITY KEEPS SENSING — and that is a deliberate exception to "MR always wins".
        //
        // USER REQUIREMENT (ModBuild 139, verbatim): "Überlege dir auch generische Effekte dafür
        // wenn gar keine Umgebung eingstellt ist oder mixed reality aktiviert ist." The elements
        // are asked to reach the player in EVERY presentation, including passthrough — so this
        // channel may not be switched off by MR. The MR ruling it appears to contradict is about
        // the SKY and the ROOM: those are opaque geometry, and geometry over passthrough is
        // exactly what a chroma key cannot have. A published number is not geometry.
        //
        // THE CONSTRAINT MOVES TO THE ART, where it belongs and where it can be honoured: whatever
        // reads these globals while MR is on must be ADDITIVE light and particles anchored to the
        // BOARD, never a full-frame colour cast and never an occluding surface (the project's MR
        // rule: boards get _Cull = 0, geometry is never welded in). A driver that stood itself
        // down here would make that impossible for the art to ever do.
        //
        // REJECTED: publishing a separate "MR is on" flag so the art could branch. The art already
        // knows — the thing that draws in MR is a different object from the thing that draws in a
        // room, and an object that only exists in one of the two needs no flag to tell it which.

        // SCENARIO-ONLY SCOPE — the same gate the environment uses (SkyAlternative.cs:767).
        // Outside a live scenario board the infusion table is stale leftovers from the last
        // scenario (the game clears it in ElementInfusionBoardManager.Reset, :44, but not
        // necessarily before we would have read it), and there is nothing to be atmospheric about.
        if (!Events.VRModeStateMachine.ScenarioBoardExists)
        {
            StandDown("no scenario board");
            return;
        }

        float clock = SkyAlternative.EnvClockSeconds;

        // ---- SENSE ------------------------------------------------------------------------------
        // Six reads of a static array (ElementColumn, :92-95). try/catch per read, like
        // RemoteElementStrip: this is game state read from outside the game's own call order, and a
        // throw here must degrade to "inert", never take the environment driver down with it.
        int signature = 0;
        bool changed = false;
        for (int i = 0; i < Count; i++)
        {
            ElementInfusionBoardManager.EColumn column;
            try
            {
                column = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i);
            }
            catch
            {
                column = ElementInfusionBoardManager.EColumn.Inert;
            }

            signature = signature * 3 + (int)column;

            if (column != Column[i] || !_live)
            {
                // Anchor the ramp AT the current intensity, so a Strong → Waning → Inert chain that
                // turns over mid-ramp continues from where the eye last saw it instead of snapping
                // back to the previous plateau.
                From[i] = _live ? Value[i] : 0f;
                Since[i] = clock;
                Column[i] = column;
                changed = true;
            }
            else if (clock < Since[i])
            {
                // The shared clock JUMPED BACKWARDS — a new clock owner or a scene reload reset
                // (SkyAlternative.FollowEnvClock's jump band). Re-anchor at the current value
                // instead of letting `clock - Since[i]` go negative and freeze the ramp at its FROM
                // until the clock has caught up again.
                From[i] = Value[i];
                Since[i] = clock;
            }
        }

        // ---- SMOOTH -----------------------------------------------------------------------------
        float waning = WaningPlateau
                       + WaningEbbAmplitude * Mathf.Sin(clock * (2f * Mathf.PI / WaningEbbPeriodSeconds));
        float peak = 0f;
        for (int i = 0; i < Count; i++)
        {
            float target = Column[i] switch
            {
                ElementInfusionBoardManager.EColumn.Strong => 1f,
                ElementInfusionBoardManager.EColumn.Waning => waning,
                _ => 0f,
            };

            // Closed form rather than a per-frame MoveTowards: the intensity is a pure function of
            // (from, target, elapsed), so it is frame-rate independent by construction and two
            // clients that anchored at the same shared-clock time agree exactly, with no
            // accumulated integration error to drift apart.
            float t = RampSeconds > 0f ? Mathf.Clamp01((clock - Since[i]) / RampSeconds) : 1f;
            t = t * t * (3f - 2f * t);                 // smoothstep — no corner at either end
            float v = Mathf.Clamp01(Mathf.Lerp(From[i], target, t));
            Value[i] = v;
            if (v > peak)
                peak = v;
        }

        // ---- PUBLISH ----------------------------------------------------------------------------
        float master = Mathf.Max(0f, ResponseStrength.Value);
        var a = new Vector4(Value[0], Value[1], Value[2], Value[3]);
        var b = new Vector4(Value[4], Value[5], master, peak);

        Write(a, b);
        _live = true;
        _zeroed = false;

        // ---- DIAGNOSE ---------------------------------------------------------------------------
        if (changed && signature != _signature)
            LogEdge(signature, master, clock);
        _signature = signature;
    }

    /// <summary>
    /// Drop the channel: publish master 0 (and every element 0) ONCE, then do nothing until the
    /// gates open again. Idempotent — the guard is what makes the off path free, and what stops a
    /// per-frame log.
    ///
    /// <para>The drop is INSTANT, not ramped, and that is deliberate: a toggle the player just
    /// moved must answer immediately or it reads as broken, and every other route here (scenario
    /// end, mixed reality, teardown) is a moment at which the environment itself is disappearing —
    /// there is nothing left to fade. Coming back is the ramped direction: the next live tick
    /// re-anchors every element at 0 and fades the mood in over <see cref="RampSeconds"/>.</para>
    /// </summary>
    /// <param name="why">Named in the one log line this emits, so a log reader can tell "the player
    /// turned it off" from "the scenario ended" without guessing.</param>
    internal static void StandDown(string why)
    {
        if (!_live && _zeroed)
            return;

        bool wasLive = _live;
        _live = false;
        _zeroed = true;
        _signature = -1;
        for (int i = 0; i < Count; i++)
        {
            Column[i] = ElementInfusionBoardManager.EColumn.Inert;
            Value[i] = 0f;
            From[i] = 0f;
            Since[i] = 0f;
        }

        Write(Vector4.zero, Vector4.zero);

        if (wasLive)
            VRLog.Info("Core", $"ELEMENT MOOD off — {why}. {ElemAName} and {ElemBName} published as " +
                               "zero, so the master factor every element effect multiplies by is 0 and " +
                               "nothing is left standing in the environment's materials.");
    }

    /// <summary>
    /// The ONE writer of <see cref="ElemAName"/> and <see cref="ElemBName"/> — the same discipline
    /// <c>SkyAlternative.ApplyTimeOfs</c> states for <c>_GhvrTimeOfs</c>, and for the same reason:
    /// they are globals, so a second writer anywhere would fight this one with no way to see it.
    /// Writes only what actually moved by more than <see cref="WriteEpsilon"/>.
    /// </summary>
    private static void Write(Vector4 a, Vector4 b)
    {
        // A poisoned value must never reach a shader global — NaN propagates through every material
        // that multiplies by it and paints holes that look like a bundle fault, three lanes away
        // from here.
        if (!Finite(a) || !Finite(b))
            return;

        if (Differs(a, _lastA))
        {
            _lastA = a;
            Shader.SetGlobalVector(ElemAId, a);
        }
        if (Differs(b, _lastB))
        {
            _lastB = b;
            Shader.SetGlobalVector(ElemBId, b);
        }
    }

    private static bool Finite(Vector4 v) =>
        !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
        !float.IsNaN(v.z) && !float.IsInfinity(v.z) && !float.IsNaN(v.w) && !float.IsInfinity(v.w);

    private static bool Differs(Vector4 v, Vector4 w) =>
        Mathf.Abs(v.x - w.x) > WriteEpsilon || Mathf.Abs(v.y - w.y) > WriteEpsilon ||
        Mathf.Abs(v.z - w.z) > WriteEpsilon || Mathf.Abs(v.w - w.w) > WriteEpsilon;

    // ---- diagnostics --------------------------------------------------------------------------

    /// <summary>
    /// THE line. EDGE ONLY — one per actual change of the six columns, never per frame — because
    /// until the art lane exists this log is the ONLY way anyone (including the user, on hardware,
    /// with no headset debugger) can verify that the sensing, the gating and the publishing are
    /// right. It names every element with its raw game state AND the intensity that state is
    /// heading for, the master factor, and the fact that none of this needs the wire, with the
    /// citation — because "everything must be synced" is a standing project rule and this looks
    /// like an exception until you read why it is not one.
    /// </summary>
    private static void LogEdge(int signature, float master, float clock)
    {
        var sb = new StringBuilder(320);
        sb.Append("ELEMENT MOOD: ");
        for (int i = 0; i < Count; i++)
        {
            if (i > 0)
                sb.Append("  ");
            sb.Append((ElementInfusionBoardManager.EElement)i).Append('=').Append(Column[i])
              .Append(" now ").Append(Value[i].ToString("F2"))
              .Append(" -> ").Append(TargetLabel(Column[i]));
        }
        sb.Append(" | master ").Append(master.ToString("F2"))
          .Append(" (setting on, strength ").Append(ResponseStrength.Value.ToString("F2")).Append(')')
          .Append(", sig ").Append(signature)
          .Append(", shared clock ").Append(clock.ToString("F2")).Append('s');
        sb.Append(" | published as ").Append(ElemAName).Append("=(Fire,Ice,Air,Earth) ")
          .Append(ElemBName).Append("=(Light,Dark,Master,Peak); transitions ramp over ")
          .Append(RampSeconds.ToString("F1")).Append("s, Waning breathes ")
          .Append((WaningPlateau - WaningEbbAmplitude).ToString("F2")).Append("..")
          .Append((WaningPlateau + WaningEbbAmplitude).ToString("F2")).Append(" every ")
          .Append(WaningEbbPeriodSeconds.ToString("F1")).Append("s on the shared environment clock.");
        sb.Append(" | NO WIRE, on purpose: the element board is scenario-wide state the game itself "
                  + "serializes (ScenarioState.cs:110/704/846), restores (:1374) and desync-checks "
                  + "every round with codes 117/118 (:1992-2032, run from "
                  + "Choreographer.StartMPEndOfRoundCompare, Choreographer.cs:14320-14340), so it is "
                  + "already bit-identical on every client — same classification as "
                  + "Net/RemoteElementStrip.");
        VRLog.Info("Core", sb.ToString());
    }

    private static string TargetLabel(ElementInfusionBoardManager.EColumn column) => column switch
    {
        ElementInfusionBoardManager.EColumn.Strong => "1.00",
        ElementInfusionBoardManager.EColumn.Waning =>
            WaningPlateau.ToString("F2") + "+-" + WaningEbbAmplitude.ToString("F2"),
        _ => "0.00",
    };
}
