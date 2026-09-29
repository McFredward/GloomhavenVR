using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// THE FLICKER THAT APPEARS WHEN THE PER-PIXEL LIGHT CAP IS 0 — round two, and this time the
/// remedy owns the NUMBER instead of one writer's field.
///
/// <para>USER REPORT, ModBuild 227, verbatim: <i>"Die Performance scheint erheblich von den
/// Pixellichtern abzuhängen. […] Allerdings bringt '0' an manchen Elementen ein komisches Flackern
/// mit sich. Kannst du das fixen?"</i> — and after ModBuild 228 shipped the first attempt:
/// <i>"hat schon richtig viel gebracht, das meiste Flackern ist nun weg. An manchen Stellen ist es
/// immer noch."</i>, pointing at a stone protruding from an archway
/// (<c>.planning/debug/finales_flackern.mp4</c>).</para>
///
/// <para>WHY ModBuild 228's REMEDY COULD NOT REACH THE RESIDUAL — measured, not assumed. That build
/// damped the flicker by scaling <c>LightFlicker.amount</c>, and its own log proves the scaling
/// worked and did not finish the job:</para>
/// <list type="bullet">
/// <item><c>REMEDY: flicker amplitude scaled to 0% of authored on 46 component(s)</c> — every
/// <c>LightFlicker</c> in the scene was set to <c>amount = 0</c>, and
/// <c>decompiled/ThirdParty/LightFlicker.cs:50</c> is
/// <c>lightRef.intensity = initialValue + num * amount</c>, so at <c>amount = 0</c> that component
/// is INERT on intensity. It cannot be moving a light any more.</item>
/// <item>And yet <c>LIGHT WATCH</c> kept counting <c>10 … 148 intensity move(s) past 5%</c> per 15 s
/// window, <c>worst 6% … 22% on 'Point light (2)'</c>, in every single window of that session.
/// SOMETHING ELSE writes <see cref="Light.intensity"/> every frame, and ModBuild 228's remedy did
/// not know it existed.</item>
/// </list>
///
/// <para>THE OTHER WRITERS, read out of the decompiled tree rather than guessed:</para>
/// <list type="bullet">
/// <item><b><c>FireLight</c></b>
/// (<c>decompiled/GH.Runtime.FirstPass/UnityStandardAssets.Effects/FireLight.cs:23</c>):
/// <c>m_Light.intensity = 2f * Mathf.PerlinNoise(m_Rnd + Time.time, …)</c> — a SECOND Perlin
/// flicker, full-range (0..2, no <c>initialValue</c> to sit on), with NO amplitude field to damp at
/// all. It also writes <c>transform.localPosition = Vector3.up + noise</c> every frame
/// (FireLight.cs:27), i.e. it moves its light by up to ±0.5 world units per axis.</item>
/// <item><b><c>RFX4_ParticleLight</c></b> (<c>decompiled/GH.Runtime/RFX4_ParticleLight.cs:51</c>):
/// <c>lights[i].intensity = particleAlpha/255f * LightIntencityMultiplayer</c>, plus a per-frame
/// <c>SetActive</c>/<c>position</c>/<c>range</c> write, on up to 20 pooled lights it creates itself.</item>
/// <item><b><c>RFX4_LightCurves</c></b> (<c>decompiled/GH.Runtime/RFX4_LightCurves.cs:38</c>):
/// <c>lightSource.intensity = LightCurve.Evaluate(t) * GraphIntensityMultiplier</c>.</item>
/// <item><b><c>BeamEffectScript</c></b> (<c>decompiled/GH.Runtime/BeamEffectScript.cs:106,127</c>),
/// <b><c>ParticlesFadeScript</c></b> (<c>decompiled/GH.Runtime/ParticlesFadeScript.cs:116</c>),
/// <b><c>RFX4_EffectSettingVisible</c></b>
/// (<c>decompiled/GH.Runtime/RFX4_EffectSettingVisible.cs:105,110</c>) — all spell/VFX driven.</item>
/// <item><b><c>CharacterRevealScript</c></b>
/// (<c>decompiled/GH.Runtime/CharacterRevealScript.cs:72</c>): <c>dirLight.intensity += …</c> — the
/// only INCREMENTAL writer in the tree, and therefore the only one this class must never write to
/// (see <see cref="RuleAdditive"/>).</item>
/// <item><b><c>DynamicAmbience.SetLightLevel</c></b>
/// (<c>decompiled/GH.Runtime/DynamicAmbience.cs:169</c>): <c>obj.intensity = light.intensity * level</c>
/// over a 0.5 s room cross-fade. That one is a WANTED change, not flicker.</item>
/// </list>
///
/// <para>WHAT THE PICTURE ACTUALLY DOES, from the video the user pointed at. Taking a luminance
/// trace of one terrain patch over the stillest window of <c>finales_flackern.mp4</c> (t = 1.3..3.1 s,
/// global frame-to-frame delta ~1.4/255) gives a SQUARE STEP of about 18 %, holding for one to five
/// frames, over a whole surface patch — while a neighbouring block over the same frames ramps
/// smoothly. So the step is LOCAL, it is not exposure, and a smooth Perlin input is producing a
/// DISCONTINUOUS output. That is the four-slot signature: at <c>pixelLightCount = 0</c> a renderer
/// gets at most four per-vertex light slots plus SH, re-ranked EVERY FRAME by influence, and two
/// lights near-tied for the fourth slot swap places whenever a smooth 20 % intensity wobble — or a
/// moving light — crosses the tie. The renderer's light SET then changes discontinuously. A small
/// stone protruding from an archway is its own renderer and pops against the wall behind it, which
/// keeps a different, stable set.</para>
///
/// <para><b>SO THE CURE IS NOT "LESS FLICKER". THE CURE IS THAT THE RANKING INPUTS STOP MOVING.</b>
/// Constant intensity and constant position for the ordinary scene lights ⇒ a stable ranking ⇒
/// nothing left to step. That is what this class now does, and it does it by owning the FINAL VALUE
/// rather than any writer's amplitude field.</para>
///
/// <para>HOW: <b>DAMP IN LateUpdate, NOT BY EDITING A WRITER.</b> This project's standing ruling is
/// DO NOT WIN A WRITE WAR — CONCEDE THE FLAG, OWN THE NUMBER (project memory
/// "dont-win-a-write-war"). Every writer above runs in <c>Update</c> (or in a coroutine, which Unity
/// also runs before <c>LateUpdate</c>), and every one of them except
/// <see cref="RuleAdditive">CharacterRevealScript</see> writes an ABSOLUTE value derived from its own
/// captured baseline — so a single <c>LateUpdate</c> pass, after all of them and before the frame
/// renders, is the last word without a Harmony patch, without per-script knowledge, and without ever
/// fighting anyone. Per recorded light this class keeps an exponential rolling baseline of intensity
/// and of <c>localPosition</c> with time constant <c>[Lights] StabiliserResponseSeconds</c> and
/// writes</para>
/// <code>value = baseline + (rawWrittenThisFrame - baseline) * FlickerDamping</code>
/// <para>with <c>FlickerDamping</c> 1.0 = untouched and 0.0 = perfectly steady. The baseline is
/// always advanced from the RAW value the game wrote, NEVER from our own output — updating it from
/// the output would be a feedback loop that walks the light wherever the filter's own error points.
/// A slow, genuine change (DynamicAmbience's 0.5 s room cross-fade) therefore still ARRIVES, it just
/// arrives filtered by roughly one time constant; that lag is the price of the whole mechanism and
/// is stated here so nobody rediscovers it as a bug.</para>
///
/// <para>WHAT IS <b>NOT</b> DAMPED, and why the exclusion is deliberately NARROW. A spell flash and a
/// dying particle light are SUPPOSED to jump. But the obvious rule — "skip any light with a
/// <c>ParticleSystem</c> on itself or an ancestor" — would have exempted exactly the lights the user
/// is complaining about, because a torch's steady point light lives inside the same prop as its fire
/// emitter. That is the "gated remedy never ran" shape (project memory): the fix would ship, run,
/// change nothing, and look like a falsified hypothesis. So ancestor-<c>ParticleSystem</c>
/// containment is NOT a rule here. The three rules that ARE applied each name one component whose
/// decompiled source says it owns that light's whole life:</para>
/// <list type="number">
/// <item><see cref="RuleOwnVfx"/> — the light's OWN GameObject carries an <c>RFX4_*</c> component
/// (<c>RFX4_LightCurves</c>, <c>RFX4_ParticleLight</c>, <c>RFX4_EffectSettingVisible</c>, …). Those
/// drive an animation curve or a particle alpha straight onto the light they sit on.</item>
/// <item><see cref="RuleParticlePool"/> — the light's IMMEDIATE PARENT carries
/// <c>RFX4_ParticleLight</c>. That component creates its lights as bare <c>new GameObject()</c>
/// children of itself (RFX4_ParticleLight.cs:29-36) — they carry no script of their own — and then
/// SetActive/position/intensity-writes them every frame (RFX4_ParticleLight.cs:44-56). This is
/// PARENT-only, not ancestor: it is keyed to the one component whose documented job is to own
/// exactly those children.</item>
/// <item><see cref="RuleAdditive"/> — the light's IMMEDIATE PARENT carries
/// <c>CharacterRevealScript</c>, whose <c>dirLight</c> is <c>transform.Find("Directional Light")</c>
/// (CharacterRevealScript.cs:36) and which writes <c>dirLight.intensity += …</c>
/// (CharacterRevealScript.cs:72). An INCREMENTAL writer would integrate our output back into its own
/// state, so this light is excluded for correctness, not for taste.</item>
/// </list>
/// <para>Note the project memory "containment is not identity": <c>GetComponentInParent</c> answers
/// "related to an X", never "IS an X". Rules 2 and 3 ask about the immediate parent ON PURPOSE,
/// because "my parent is the script that spawned and drives me" is exactly the question meant, and
/// they are written as a one-level parent lookup rather than an ancestor walk so they cannot quietly
/// widen into containment. <c>BeamEffectScript</c> and <c>ParticlesFadeScript</c> are deliberately
/// NOT excluded: both write ABSOLUTE values to a light they merely REFERENCE, both live for a
/// couple of seconds, and the warm-up skip below already covers a light that young.</para>
///
/// <para>Two further runtime skips, both instrumented so a permanently-skipped population cannot
/// masquerade as a working damper: a light held for less than <see cref="WarmUpSeconds"/> s is never
/// damped (a freshly spawned light has no baseline worth damping against), and a light re-entering
/// the scene from an inactive GameObject re-arms that warm-up and snaps its baseline, so
/// DynamicAmbience's <c>SetActive(level &gt; 0f)</c> cross-fade endpoints never damp against a stale
/// level.</para>
///
/// <para>THERE IS NO LARGE-EXCURSION BYPASS ON INTENSITY, on purpose. An earlier draft passed a
/// deviation past 75 % of baseline straight through as "a genuine event". Measured against the
/// actual writers that is unsafe: <c>FireLight</c>'s own range is 0..2 around a mean of ~1, so its
/// LOUDEST swings — precisely the ones being complained about — would have been the ones let
/// through. The deviation is still COUNTED (see <c>large raw deviation(s)</c> in the watch line) so
/// that if a genuine event ever looks wrong, the log names the light; but it is not gated on.</para>
///
/// <para>POSITION IS DAMPED TOO, and it never was before. <c>LightFlicker.Update</c> writes
/// <c>transform.position = initialPosition + noise * locationAdjustAmount * 2f</c> whenever
/// <c>adjustLocation</c> is set — INDEPENDENTLY of <c>amount</c> (LightFlicker.cs:52-56), which is
/// why ModBuild 228's watcher still measured a steady 0.0199 world-unit drift on
/// <c>FireTorch_PointLight</c> in every window including the ones where damping was 0. A light that
/// MOVES re-ranks by distance every frame just as surely as one that brightens. The same mix is
/// applied to <c>localPosition</c>, with two guards: it is skipped for any frame in which the
/// light's PARENT moved (damping a local offset against a moving parent would pin the light to a
/// stale place), and it is released permanently for any light whose BASELINE travels more than
/// <see cref="PositionRelocateWU"/> world units from where it started — that is the "written an
/// absolute world position that no longer matches its prop" case ModBuild 228's watcher already
/// warned about, and for that light a stale local position would be a hard visual bug rather than a
/// lag. Both are counted in the census.</para>
///
/// <para>THE PIN IS NOW STABLE, AND IT NO LONGER BUYS A SHADOW MAP BACK. ModBuild 228 re-elected the
/// pinned light on every 10 s scan — its log reads <c>1..4 pin change(s) this cycle</c> in
/// essentially every scan, with the identity walking 'Point Light' → 'Spotlight' →
/// 'Point Light_Forest' → … — because the score is computed against the head position and the player
/// walks. Each change moves a light between the vertex and the pixel path, which is a visible
/// lighting jump: the remedy was manufacturing a step of its own. And one scan logged
/// <c>'Point Light_Forest' Auto→ForcePixel (CASTS SHADOWS — this one buys a shadow map back)</c>,
/// i.e. it re-armed the exact cost cap 0 exists to avoid. Both are fixed:
/// <see cref="PinChallengeMargin"/> + <see cref="PinDwellSeconds"/> hysteresis means an incumbent
/// keeps its slot unless a challenger is a clear 1.5x better AND the incumbent has held for 20 s,
/// every change is logged as a sentence naming loser, winner and margin, and a pinned light's
/// <c>shadows</c> is forced to <see cref="LightShadows.None"/> for exactly as long as it is pinned
/// (authored value recorded, restored on unpin) so promoting it to the pixel path costs one forward
/// pass and NOT a shadow map. ModBuild 228 also LEAKED pins: <c>ApplyPinning</c> only iterated the
/// lights that were still enabled and active, so a pinned light that went inactive was never
/// unpinned — which is why that log's authored-ForcePixel census climbed 1 → 4 while
/// <c>PinnedPixelLights</c> was 1. The unpin sweep now runs over every held record.</para>
///
/// <para>AND THE INSTRUMENT MEASURES THE OUTCOME, NOT THE PATH. This project's hardest-won rule is
/// that a fix measured by the wrong thing carries zero information. So the watcher reports BOTH
/// sides of the damper: the RAW moves it saw (directly comparable with ModBuild 228's numbers,
/// because the raw signal is untouched) and the RESIDUAL moves that survived into the rendered
/// value. "92 raw / 0 residual" is a working damper; "92 raw / 88 residual" is a damper that never
/// ran on those lights, and the same line says which lights were excluded and by which rule so the
/// two cannot be confused. The census carries a per-light MonoBehaviour TYPE HISTOGRAM, which is the
/// thing ModBuild 228 lacked: it only knew about <c>LightFlicker</c>, and that is precisely why that
/// round could not name the culprit. Every reported line carries its own MEASURED self-cost. No line
/// asserts a mechanism the instrument cannot observe.</para>
///
/// <para>MULTIPLAYER: local presentation only. Every write here is to a local
/// <see cref="Light"/>; nothing is networked, nothing changes game state, and two players on
/// different settings simply see different lighting quality — the same class of difference as the
/// MSAA row.</para>
/// </summary>
internal static class LightStabiliser
{
    /// <summary>Seconds between full re-scans while engaged (rooms open and spawn new torches).</summary>
    private const float RescanSeconds = 10f;

    /// <summary>Seconds between watcher summaries. Only emitted when the window saw something.</summary>
    private const float WatchReportSeconds = 15f;

    /// <summary>An intensity move past this fraction of the light's observed floor is an EVENT.
    /// Deliberately the same 5 % ModBuild 228 used, so its numbers and this build's are comparable
    /// line for line.</summary>
    private const float IntensityEventFraction = 0.05f;

    /// <summary>Upper bound on <c>[Lights] PinnedPixelLights</c> — see the entry's own description.</summary>
    private const int MaxPinned = 4;

    /// <summary>A light held for less than this is never damped: it has no baseline worth damping
    /// against, and a fresh spawn caught mid-fade-in would be dragged back toward its first frame.</summary>
    private const float WarmUpSeconds = 2f;

    /// <summary>A challenger must beat the weakest incumbent by this factor to take a pinned slot.</summary>
    private const float PinChallengeMargin = 1.5f;

    /// <summary>…and the incumbent must have held its slot for at least this long.</summary>
    private const float PinDwellSeconds = 20f;

    /// <summary>Baseline travel (world units, from where the light was first seen) past which
    /// position damping is released for that light: it is being RELOCATED, not jittered.</summary>
    private const float PositionRelocateWU = 0.25f;

    /// <summary>An intensity deviation past this fraction of the baseline is COUNTED as a large
    /// deviation in the watch line. It is not gated on — see the class doc.</summary>
    private const float LargeDeviationFraction = 0.75f;

    /// <summary>How many excluded light names the census prints before it says "and N more".</summary>
    private const int ExcludedNameCap = 10;

    /// <summary>How many worst movers the watch line names.</summary>
    private const int TopMoverCount = 3;

    // ---- exclusion rule names. These strings are printed verbatim in the census, so that the next
    // ---- log answers "was this light in the damped set at all?" without another round.
    private const string RuleOwnVfx = "vfx-own-script";
    private const string RuleParticlePool = "vfx-particle-pool-child";
    private const string RuleAdditive = "additive-writer-parent";

    internal static ConfigEntry<bool>? Enabled;
    internal static ConfigEntry<int>? PinnedPixelLights;
    internal static ConfigEntry<float>? FlickerDamping;
    internal static ConfigEntry<float>? ResponseSeconds;

    private static bool _bound;

    /// <summary>One recorded <see cref="Light"/>: the authored state, the damper's state, and the
    /// watcher's state — one object per light in the scene, walked once per LateUpdate.</summary>
    private sealed class LightRecord
    {
        internal Light Light = null!;
        internal Transform Transform = null!;
        internal string Name = "";

        // ---- authored facts, captured ONCE at adoption ----------------------------------------
        internal LightRenderMode OriginalMode;
        internal LightShadows AuthoredShadows;
        internal bool Baked;
        /// <summary>MonoBehaviour type names on the light's OWN GameObject — the type histogram's
        /// raw material, and the thing that makes a writer attributable.</summary>
        internal string[] OwnScripts = System.Array.Empty<string>();
        /// <summary>Empty = damped. Otherwise the rule name that excluded it, printed in the census.</summary>
        internal string ExcludeRule = "";
        internal float AdoptedAt;

        // ---- pinning ---------------------------------------------------------------------------
        internal bool Pinned;
        internal bool ShadowsSuppressed;
        internal float PinnedSince;
        internal float LastScore;

        // ---- damper state ----------------------------------------------------------------------
        internal bool HaveBaseline;
        internal float BaselineIntensity;
        internal Vector3 BaselineLocal;
        internal Vector3 FirstBaselineLocal;
        internal bool PositionDamped = true;
        internal bool RelocationReleased;
        internal bool HaveParentPose;
        internal Vector3 LastParentPos;
        internal Quaternion LastParentRot;
        internal float LastOutIntensity;

        // ---- watcher state (raw signal — untouched by the damper, so comparable with MB228) -----
        internal bool LastActive;
        internal bool LastEnabled;
        internal LightShadows LastShadows;
        internal float LastRawIntensity;
        internal float IntensityFloor = float.MaxValue;

        // ---- per-window counters ---------------------------------------------------------------
        internal int ActivationEvents;
        internal int EnableEvents;
        internal int ShadowEvents;
        internal int RawIntensityEvents;
        internal float WorstRawJump;
        internal int ResidualIntensityEvents;
        internal float WorstResidualJump;
        internal int LargeDeviationEvents;
        internal float WorstRawPosDev;
        internal float WorstResidualPosDev;

        internal void ResetWindow()
        {
            ActivationEvents = 0; EnableEvents = 0; ShadowEvents = 0;
            RawIntensityEvents = 0; WorstRawJump = 0f;
            ResidualIntensityEvents = 0; WorstResidualJump = 0f;
            LargeDeviationEvents = 0;
            WorstRawPosDev = 0f; WorstResidualPosDev = 0f;
        }
    }

    private static readonly List<LightRecord> Lights = new(64);

    /// <summary>Adoption scratch for <c>GetComponents</c> — reused so the sweep allocates nothing
    /// per light beyond the type-name array it actually keeps.</summary>
    private static readonly List<MonoBehaviour> ScriptBuf = new(16);

    private static bool _engaged;
    private static float _nextRescanTime;
    private static float _nextWatchReportTime;
    private static int _watchWindows;

    /// <summary>Measured self-cost of the LateUpdate damper, accumulated over the report window.</summary>
    private static readonly System.Diagnostics.Stopwatch CostClock = new();
    private static long _costTicks;
    private static int _costFrames;

    /// <summary>
    /// LIGHT-FRAMES the damper DID NOT touch this window, split by why. These exist so that a
    /// permanently-skipped population cannot masquerade as a working damper: a healthy session shows
    /// a warm-up figure of roughly (newly spawned lights) x (2 s x frame rate) and nothing more, and
    /// an exclusion figure that matches the excluded-light count times the window's frames. If the
    /// residual move count stays high AND one of these is large, the answer is "the damper never ran
    /// on those lights", not "the damping is too weak" — and separating those two is exactly what
    /// would otherwise cost a hardware round.
    /// </summary>
    private static long _warmUpSkips;
    private static long _excludedSkips;

    /// <summary>Whether the census has been emitted at least once for the current engagement.
    /// It is unconditional the first time, so the DAMPED SET clause (which names the excluded lights
    /// and their rules) is guaranteed to appear even in a session where the pin never changes and no
    /// new light is ever adopted — the hysteresis added in this build makes that the NORMAL case,
    /// and ModBuild 228's census only printed on nearly every scan because its pinning churned.</summary>
    private static bool _censusPrinted;

    /// <summary>Scan-time census of the game's own <c>LightFlicker</c> population. Nothing is
    /// WRITTEN to those components any more — ModBuild 228 did, and its own log proved that silencing
    /// them left 10..148 intensity moves per window standing — but their authored numbers are still
    /// the cheapest available evidence about what the scene is trying to do.</summary>
    private struct FlickerCensus
    {
        internal int Count;
        internal float MinAmount, MaxAmount, SumAmount;
        internal float MinSpeed, MaxSpeed;
        internal int JitterCount;
        internal float MaxJitter;
        internal int WithoutLight;
    }

    private static FlickerCensus _flickers;

    // -----------------------------------------------------------------------------------------
    // Config
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Ride-along bind onto the rig module's own config file — the same pattern (and the same file)
    /// as <c>SkyAlternative</c>, <c>ElementMood</c>, <c>Haunt</c> and <c>EnvSound</c>. It rides
    /// <c>RenderQuality</c> because it is a property OF the per-pixel light cap: it exists to make
    /// that row's 0 usable, and a player looking for it on disk will look where the cap is.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;

        Enabled = file.Bind("Lights", "StabiliseAtZeroCap", Defaults.StabiliseAtZeroCap,
            "Stop lights STEPPING between quality tiers while the per-pixel light cap "
            + "([RenderQuality] PixelLightCount) is 0. At 0 every light in the room competes for the "
            + "same four per-vertex slots per renderer, the ranking is recomputed EVERY FRAME from "
            + "intensity and distance, and this dungeon has several scripts animating light "
            + "intensities and light POSITIONS every frame (the game's own LightFlicker, the fire "
            + "props' FireLight, the spell effects' RFX4 family) — so two nearly-tied torches swap "
            + "rank and a whole facet of an archway changes brightness in one frame. That is the "
            + "'komisches Flackern' the cap's 0 brings with it. This holds the RANKING INPUTS still: "
            + "after every one of the game's own writes and before the frame renders, it filters "
            + "each light's intensity and local position toward a slow rolling average (see "
            + "FlickerDamping and StabiliserResponseSeconds), and it pins a small number of lights "
            + "to the per-pixel path (see PinnedPixelLights) so they leave the competition "
            + "altogether. PRESENTATION ONLY and fully reversible: it writes Light.intensity, "
            + "Light.transform.localPosition, Light.renderMode and (only on a pinned light, only "
            + "while it is pinned) Light.shadows. It never edits any game component's fields, so "
            + "there is no write war to lose. It does nothing at all while the cap is -1 or above 0, "
            + "because the boundary is quiet there. Whatever it does, it says so in the log "
            + "([Rig] LIGHT STABILISER / LIGHT WATCH / LIGHT PIN), and the watch line reports BOTH "
            + "the moves it intercepted and the moves it left behind.");

        PinnedPixelLights = file.Bind("Lights", "PinnedPixelLights", Defaults.PinnedPixelLights,
            new ConfigDescription(
                "How many lights keep the smooth PER-PIXEL falloff while the cap is 0 (0 = none, "
                + "pin nothing and rely on damping alone). Unity's forward path renders a light whose "
                + "renderMode is ForcePixel per-pixel WHATEVER the cap says, so this is the way to "
                + "spend a little of what the cap saved exactly where it is seen: the strongest "
                + "light near your head keeps its soft round falloff and stops taking part in the "
                + "per-frame ranking, while the other 40-odd stay per-vertex and free. The set is "
                + "chosen once every 10 s, never per frame, and an incumbent now KEEPS its slot "
                + "unless a challenger is a clear 1.5x stronger and the incumbent has held for at "
                + "least 20 s — because a re-election is itself a visible lighting jump, and the "
                + "previous build re-elected on nearly every scan as the player walked. A pinned "
                + "light's shadow casting is switched off for exactly as long as it is pinned (the "
                + "authored value is restored on unpin), so promoting it costs one forward pass and "
                + "NOT the shadow map the cap had just removed. COSTS: each pinned light is one "
                + "additional forward pass for every renderer it touches, which is the same coin the "
                + "cap saves; 1 is a small, visible amount of it.",
                new AcceptableValueRange<int>(0, MaxPinned)));

        FlickerDamping = file.Bind("Lights", "FlickerDamping", Defaults.FlickerDamping,
            new ConfigDescription(
                "How much of each light's per-frame WOBBLE survives while the stabiliser is engaged "
                + "(1 = untouched, 0 = perfectly steady light). Every frame, after the game's own "
                + "scripts have written and before the frame renders, each watched light is set to "
                + "'slow rolling average + (what the game just wrote - that average) x this value'. "
                + "At 0 the light holds absolutely still, which is what stops two nearly-tied lights "
                + "from crossing each other's rank and stepping a whole surface's brightness. The "
                + "rolling average still FOLLOWS a genuine change (a room's ambience cross-fade "
                + "arrives about one StabiliserResponseSeconds late), so this dims nothing and "
                + "brightens nothing; it only removes the shake. Raise it toward 1 if the torches "
                + "feel dead, lower it toward 0 if any stepping is left. Note that most of this "
                + "dungeon's fire animation is not in the lights at all: of the 46 flicker "
                + "components the previous build counted, 34 carry no Light and animate only a MESH, "
                + "so the fire keeps moving at 0.",
                new AcceptableValueRange<float>(0f, 1f)));

        ResponseSeconds = file.Bind("Lights", "StabiliserResponseSeconds",
            Defaults.StabiliserResponseSeconds,
            new ConfigDescription(
                "How fast the stabiliser's rolling average follows a REAL change in a light, in "
                + "seconds. This is the only thing FlickerDamping = 0 does not freeze: a light that "
                + "genuinely gets brighter (a room's ambience cross-fade, a torch being lit) arrives "
                + "about this many seconds late, smoothly, while a per-frame shake never arrives at "
                + "all. Lower it if lighting changes feel sluggish; raise it if a slow change is "
                + "still being reproduced as a series of small steps. 0.75 s is comfortably longer "
                + "than any per-frame flicker and comfortably shorter than the 0.5 s room cross-fade "
                + "it has to pass through.",
                new AcceptableValueRange<float>(0.1f, 5f)));
    }

    // -----------------------------------------------------------------------------------------
    // Engage / release, and the scan cadence
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Per-frame step, called from <see cref="RenderQuality.Tick"/> immediately after the cap is
    /// asserted — the stabiliser is a FUNCTION of that cap, so it must read the value the same tick
    /// wrote rather than last frame's. (It is deliberately not a seventh entry in
    /// <c>VRRigDriver._tailSteps</c>: that array is the mod's most order-sensitive list and is
    /// locked by name in <c>.planning/refactor/FRAME-ORDER.lock</c>; nesting here expresses the real
    /// dependency and leaves the locked order untouched.)
    ///
    /// <para>This runs in the rig's UPDATE. It engages, releases and re-scans; it does NOT damp.
    /// Damping has to happen after every game <c>Update</c> has written, which is what
    /// <see cref="LateDriver"/> is for.</para>
    /// </summary>
    internal static void Tick(int effectiveCap)
    {
        bool want = Enabled != null && Enabled.Value && effectiveCap == 0;

        if (!want)
        {
            if (_engaged)
                Release(effectiveCap < 0
                    ? "the per-pixel cap was released back to the game's own value"
                    : $"the per-pixel cap moved to {effectiveCap}, where the tier boundary is quiet");
            return;
        }

        if (!_engaged)
        {
            _engaged = true;
            _nextRescanTime = 0f;             // scan immediately
            _nextWatchReportTime = Time.unscaledTime + WatchReportSeconds;
            _watchWindows = 0;
            _costTicks = 0;
            _costFrames = 0;
            _warmUpSkips = 0;
            _excludedSkips = 0;
            _censusPrinted = false;
            InstallDriver();
        }

        if (Time.unscaledTime >= _nextRescanTime)
        {
            _nextRescanTime = Time.unscaledTime + RescanSeconds;
            using (PerfMonitor.Scope("Rig.LightStabiliser.Scan"))
                Scan();
        }
    }

    /// <summary>
    /// The whole-scene sweep, and the ONLY place one runs. It is on a 10 s cadence rather than a
    /// transition edge because the dungeon SPAWNS torches as rooms open
    /// (<c>ProceduralMapTile.ShowContent</c> flips whole "Generated Content" subtrees active), so a
    /// scan-once design would stabilise the entrance room and nothing after it. It carries its own
    /// PerfMonitor scope: if <c>Rig.LightStabiliser.Scan</c> ever ranks near the mod's real work on
    /// the [Perf] STEPS line, the instrument has become the thing it measures — that is the check
    /// this project learned to ship after one FindObjectOfType owned a whole frame.
    /// </summary>
    private static void Scan()
    {
        LightFlicker[] flickers;
        Light[] lights;
        try
        {
            flickers = Object.FindObjectsOfType<LightFlicker>();
            lights = Object.FindObjectsOfType<Light>();
        }
        catch (System.Exception e)
        {
            VRLog.Info("Rig", $"LIGHT STABILISER: the scene sweep threw '{e.Message}' — nothing was "
                              + "changed this cycle; it retries on the next cadence.");
            return;
        }

        CensusFlickers(flickers);
        int newLights = AdoptLights(lights);
        PruneDestroyed();
        int pinned = ApplyPinning();

        if (newLights == 0 && pinned == 0 && _censusPrinted)
            return; // nothing moved this cycle — say nothing rather than repeat a line every 10 s

        _censusPrinted = true;
        LogCensus(lights.Length, newLights, pinned);
    }

    /// <summary>
    /// Read the authored <c>LightFlicker</c> numbers. READ ONLY — this class no longer writes
    /// <c>amount</c>. ModBuild 228 did, its log recorded <c>flicker amplitude scaled to 0% of
    /// authored on 46 component(s)</c>, and LightFlicker.cs:50 makes that provably inert on
    /// intensity — yet 10..148 intensity moves per window survived. Editing one writer's field was
    /// the wrong shape of remedy; the numbers it produced are still worth having, because the
    /// authored max of 1.0000 (against a shipped default of 0.01) is what says this scene's torches
    /// are authored a hundred times louder than the component's own defaults.
    /// </summary>
    private static void CensusFlickers(LightFlicker[] found)
    {
        var c = new FlickerCensus
        {
            MinAmount = float.MaxValue,
            MinSpeed = float.MaxValue,
        };

        for (int i = 0; i < found.Length; i++)
        {
            LightFlicker f = found[i];
            if (f == null)
                continue;
            c.Count++;
            if (f.GetComponent<Light>() == null)
                c.WithoutLight++;
            if (f.adjustLocation)
            {
                c.JitterCount++;
                c.MaxJitter = Mathf.Max(c.MaxJitter, f.locationAdjustAmount);
            }
            c.MinSpeed = Mathf.Min(c.MinSpeed, f.speed);
            c.MaxSpeed = Mathf.Max(c.MaxSpeed, f.speed);
            c.MinAmount = Mathf.Min(c.MinAmount, f.amount);
            c.MaxAmount = Mathf.Max(c.MaxAmount, f.amount);
            c.SumAmount += f.amount;
        }

        if (c.MinAmount == float.MaxValue) c.MinAmount = 0f;
        if (c.MinSpeed == float.MaxValue) c.MinSpeed = 0f;
        _flickers = c;
    }

    /// <summary>
    /// Record every Light not already held, capturing the AUTHORED state (renderMode, shadows) and
    /// the MonoBehaviour type names on the light's own GameObject before this class touches
    /// anything. The exclusion decision is made here, ONCE, and cached: it is a fact about the
    /// prefab, and re-deciding it per frame would be both wasteful and non-deterministic.
    /// </summary>
    private static int AdoptLights(Light[] found)
    {
        int adopted = 0;
        float now = Time.unscaledTime;

        for (int i = 0; i < found.Length; i++)
        {
            Light l = found[i];
            // Town practicals already have a deterministic owner-controlled fade. Adopting
            // them would delay that fade and pin late-loaded candles toward a zero baseline.
            // Exact ownership only: native torches and other lights retain the existing policy.
            if (l == null || WorldUI.TownServiceLighting.Owns(l) || IndexOfLight(l) >= 0)
                continue;

            Transform t = l.transform;
            GameObject go = l.gameObject;

            // Type names on the light's OWN GameObject. This is the census's histogram source and
            // the first exclusion rule's input. GetComponents into a reused list so the sweep does
            // not allocate a fresh array per light on top of the one it keeps.
            go.GetComponents(ScriptBuf);
            var names = new List<string>(ScriptBuf.Count);
            for (int s = 0; s < ScriptBuf.Count; s++)
            {
                MonoBehaviour mb = ScriptBuf[s];
                if (mb == null)          // a missing-script slot decompiles to a null component
                    continue;
                names.Add(mb.GetType().Name);
            }

            Lights.Add(new LightRecord
            {
                Light = l,
                Transform = t,
                Name = l.name,
                OriginalMode = l.renderMode,
                AuthoredShadows = l.shadows,
                Baked = l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked,
                OwnScripts = names.ToArray(),
                ExcludeRule = ClassifyExclusion(names, t),
                AdoptedAt = now,
                LastActive = go.activeInHierarchy,
                LastEnabled = l.enabled,
                LastShadows = l.shadows,
                LastRawIntensity = l.intensity,
                IntensityFloor = l.intensity,
                LastOutIntensity = l.intensity,
            });
            adopted++;
        }

        return adopted;
    }

    /// <summary>
    /// THE EXCLUSION RULE, and it is deliberately narrow. See the class doc for why the obvious rule
    /// ("skip anything with a ParticleSystem on an ancestor") is NOT used: it would have exempted
    /// exactly the torch lights the user is complaining about, and a fix that never runs on the
    /// reported case looks identical to a falsified one.
    ///
    /// <para>Returns the rule name, or "" for "damp this light".</para>
    /// </summary>
    private static string ClassifyExclusion(List<string> ownScripts, Transform t)
    {
        // R1 — an RFX4_* component ON THE LIGHT ITSELF drives that light's whole life from a curve
        // or a particle alpha: RFX4_LightCurves.cs:38, RFX4_ParticleLight.cs:51,
        // RFX4_EffectSettingVisible.cs:105. A spell flash is SUPPOSED to jump.
        for (int i = 0; i < ownScripts.Count; i++)
            if (ownScripts[i].StartsWith("RFX4_", System.StringComparison.Ordinal))
                return RuleOwnVfx;

        Transform? parent = t.parent;
        if (parent == null)
            return "";

        // R2 and R3 ask about the IMMEDIATE PARENT ONLY — one level, never an ancestor walk. Project
        // memory "containment is not identity": GetComponentInParent answers "related to an X". Here
        // the question genuinely is "is my parent the script that created and drives me", which is a
        // one-level question, and writing it as one level is what stops it widening into
        // containment later.
        parent.gameObject.GetComponents(ScriptBuf);
        for (int i = 0; i < ScriptBuf.Count; i++)
        {
            MonoBehaviour mb = ScriptBuf[i];
            if (mb == null)
                continue;
            string n = mb.GetType().Name;

            // R2 — RFX4_ParticleLight creates its lights as bare `new GameObject()` children of
            // itself (RFX4_ParticleLight.cs:29-36), so those lights carry no script of their own and
            // R1 cannot see them; it then SetActive/position/intensity-writes them every Update
            // (RFX4_ParticleLight.cs:44-56). Nothing else in the tree owns its children that way.
            if (n == "RFX4_ParticleLight")
                return RuleParticlePool;

            // R3 — CharacterRevealScript resolves transform.Find("Directional Light")
            // (CharacterRevealScript.cs:36) and writes `dirLight.intensity += …`
            // (CharacterRevealScript.cs:72). It is the ONLY incremental intensity writer in the
            // decompiled tree, and an incremental writer would integrate OUR output back into its
            // own state. This exclusion is for correctness, not for taste.
            if (n == "CharacterRevealScript")
                return RuleAdditive;
        }

        return "";
    }

    private static int IndexOfLight(Light l)
    {
        for (int i = 0; i < Lights.Count; i++)
            if (ReferenceEquals(Lights[i].Light, l))
                return i;
        return -1;
    }

    /// <summary>Drop destroyed entries so the per-frame walk stays the size of the scene.</summary>
    private static void PruneDestroyed()
    {
        for (int i = Lights.Count - 1; i >= 0; i--)
            if (Lights[i].Light == null)
                Lights.RemoveAt(i);
    }

    // -----------------------------------------------------------------------------------------
    // Pinning
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Choose the pinned set and write <see cref="LightRenderMode.ForcePixel"/> onto it.
    ///
    /// <para>THE RANK IS COMPUTED HERE AND NOWHERE ELSE — once per 10 s scan. The proxy is Unity's
    /// own shape (intensity x range², attenuated by the squared distance to the head) rather than
    /// the raw intensity, because a bright light two rooms away is not the one whose falloff the
    /// player is looking at; a light with no head to measure against falls back to
    /// <c>intensity x range</c>, which is stable and view-independent.</para>
    ///
    /// <para>TWO THINGS ARE DIFFERENT FROM ModBuild 228, and both come out of its own log.</para>
    /// <list type="number">
    /// <item><b>HYSTERESIS.</b> That build re-scored against the head every 10 s and swapped
    /// whenever the ordering changed, which as the player walks is nearly every scan — its log reads
    /// <c>1..4 pin change(s) this cycle</c> almost throughout, with the pinned identity walking
    /// 'Point Light' → 'Spotlight' → 'Point Light_Forest'. Every one of those moves a light between
    /// the vertex and the pixel path, which IS a visible lighting jump: the remedy was manufacturing
    /// steps. An incumbent now keeps its slot unless a challenger scores <see cref="PinChallengeMargin"/>x
    /// higher AND the incumbent has held for <see cref="PinDwellSeconds"/> s, and at most one swap
    /// happens per scan.</item>
    /// <item><b>NO SHADOW MAP IS BOUGHT BACK.</b> That build merely DEPRIORITISED shadow casters and
    /// then boasted in its census when it pinned one anyway ("CASTS SHADOWS — this one buys a shadow
    /// map back"). That was a cost, not a feature: at cap 0 an Auto light renders no shadow map, and
    /// promoting it to ForcePixel is what turns the map back on. So a pinned light's
    /// <c>shadows</c> is forced to <see cref="LightShadows.None"/> for exactly as long as it is
    /// pinned, and its authored value is restored on unpin. With that, a shadow caster is no worse a
    /// candidate than any other and the 0.25x penalty is gone — it existed only to avoid a cost this
    /// now removes directly.</item>
    /// </list>
    ///
    /// <para>AND THE UNPIN SWEEP RUNS OVER EVERY HELD RECORD. ModBuild 228 iterated only the lights
    /// that were still enabled and active, so a pinned light that went inactive was never unpinned
    /// and its <c>Pinned</c> flag stayed set forever — which is why that log's ForcePixel census
    /// climbed from 1 to 4 while <c>PinnedPixelLights</c> was 1.</para>
    /// </summary>
    private static int ApplyPinning()
    {
        int want = Mathf.Clamp(PinnedPixelLights!.Value, 0, MaxPinned);
        float now = Time.unscaledTime;

        Vector3 head = Vector3.zero;
        bool haveHead = false;
        Camera? cam = VRRigDriver.HeadCamera;
        if (cam != null)
        {
            head = cam.transform.position;
            haveHead = true;
        }

        int changed = 0;

        // 1. THE LEAK FIX. Release the pin on every held record that is no longer a valid candidate
        //    — destroyed, disabled, deactivated or baked. This walks ALL records, not just the ones
        //    that survive the candidate filter below, which is exactly what ModBuild 228 did not do.
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            if (!r.Pinned)
                continue;
            Light l = r.Light;
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || r.Baked)
            {
                Unpin(r);
                changed++;
            }
        }

        // 2. Score the candidates.
        var scored = new List<(float score, LightRecord rec)>(Lights.Count);
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            Light l = r.Light;
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy)
                continue;
            if (r.Baked)
                continue;                                   // already in the lightmap
            if (r.OriginalMode == LightRenderMode.ForceVertex)
                continue;                                   // opted out of the pixel path itself

            float reach = Mathf.Max(l.range, 0.001f);
            float score = haveHead
                ? l.intensity * reach * reach
                  / Mathf.Max((l.transform.position - head).sqrMagnitude, 0.001f)
                : l.intensity * reach;
            r.LastScore = score;
            scored.Add((score, r));
        }
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        // 3. Incumbents keep their slots. Collect them weakest-first so the contest below is against
        //    the weakest one.
        var incumbents = new List<LightRecord>(MaxPinned);
        for (int i = scored.Count - 1; i >= 0; i--)
            if (scored[i].rec.Pinned)
                incumbents.Add(scored[i].rec);

        // 3a. Too many incumbents (the row was lowered): drop the weakest until the count fits.
        while (incumbents.Count > want)
        {
            LightRecord drop = incumbents[0];
            incumbents.RemoveAt(0);
            Unpin(drop);
            changed++;
            VRLog.Info("Rig", $"LIGHT PIN: '{drop.Name}' gave up its pinned per-pixel slot because "
                              + $"[Lights] PinnedPixelLights is now {want} — its authored renderMode "
                              + $"({drop.OriginalMode}) and shadow setting ({drop.AuthoredShadows}) "
                              + "are back exactly as the prefab had them.");
        }

        // 3b. Free slots: fill them from the best unpinned candidates, no dwell test — an empty slot
        //     has no incumbent to protect and leaving it empty would be a remedy that never ran.
        for (int i = 0; i < scored.Count && incumbents.Count < want; i++)
        {
            LightRecord r = scored[i].rec;
            if (r.Pinned)
                continue;
            Pin(r, now);
            incumbents.Insert(0, r);      // weakest-first list; a fresh pin is the newest, not the weakest
            changed++;
            VRLog.Info("Rig", $"LIGHT PIN: '{r.Name}' took an EMPTY pinned per-pixel slot "
                              + $"(influence score {scored[i].score:F2}; {incumbents.Count} of {want} "
                              + $"slot(s) now filled). Its authored renderMode was {r.OriginalMode} "
                              + $"and its authored shadows {r.AuthoredShadows}"
                              + (r.ShadowsSuppressed
                                  ? " — shadow casting is SUPPRESSED for as long as it is pinned, so "
                                    + "the promotion costs one forward pass and not the shadow map "
                                    + "the cap had just removed."
                                  : " — it casts no shadow, so the promotion costs one forward pass "
                                    + "and nothing else.")
                              + " Both are restored on unpin.");
        }

        // 3c. THE CONTEST — at most one swap per scan, and only with a clear margin and an expired
        //     dwell. Anything less than that is the per-frame re-rank this class exists to remove,
        //     wearing the mod's name.
        //
        //     Re-sort weakest-first before contesting: 3b inserts a freshly pinned light at the
        //     front, which is only coincidentally the weakest, and contesting against the wrong
        //     incumbent would quietly protect the weak slot forever.
        incumbents.Sort((a, b) => a.LastScore.CompareTo(b.LastScore));
        if (want > 0 && incumbents.Count == want)
        {
            LightRecord weakest = incumbents[0];
            float weakestScore = weakest.LastScore;
            for (int i = 0; i < scored.Count; i++)
            {
                LightRecord ch = scored[i].rec;
                if (ch.Pinned)
                    continue;
                float chScore = scored[i].score;
                if (chScore <= weakestScore * PinChallengeMargin)
                    break;                   // scored is sorted desc: nobody after this can qualify
                float held = now - weakest.PinnedSince;
                if (held < PinDwellSeconds)
                {
                    VRLog.Info("Rig", $"LIGHT PIN: '{ch.Name}' out-scores the pinned "
                                      + $"'{weakest.Name}' {chScore:F2} to {weakestScore:F2} "
                                      + $"({chScore / Mathf.Max(weakestScore, 0.0001f):F2}x, past the "
                                      + $"{PinChallengeMargin:F1}x challenge margin) but the "
                                      + $"incumbent has held for only {held:F1}s of the "
                                      + $"{PinDwellSeconds:F0}s minimum dwell, so NOTHING CHANGED. "
                                      + "A pin change is a visible lighting jump; the previous build "
                                      + "made one on nearly every scan as the player walked.");
                    break;
                }
                Unpin(weakest);
                Pin(ch, now);
                changed += 2;
                VRLog.Info("Rig", $"LIGHT PIN: '{ch.Name}' TOOK the pinned per-pixel slot from "
                                  + $"'{weakest.Name}' — influence score {chScore:F2} against "
                                  + $"{weakestScore:F2}, i.e. "
                                  + $"{chScore / Mathf.Max(weakestScore, 0.0001f):F2}x, clear of the "
                                  + $"{PinChallengeMargin:F1}x challenge margin, and the incumbent "
                                  + $"had held its slot {held:F1}s of the {PinDwellSeconds:F0}s "
                                  + "minimum dwell. '" + weakest.Name + "' is back on its authored "
                                  + $"renderMode {weakest.OriginalMode} and shadows "
                                  + $"{weakest.AuthoredShadows}; '{ch.Name}' is ForcePixel with "
                                  + (ch.ShadowsSuppressed ? "its shadow casting suppressed"
                                                          : "no shadow to suppress")
                                  + " until it loses the slot. IF THE LIGHTING VISIBLY JUMPED AT THIS "
                                  + "TIMESTAMP, THIS LINE IS WHY.");
                break;
            }
        }

        return changed;
    }

    /// <summary>Promote to the per-pixel path and take its shadow map away for the duration.</summary>
    private static void Pin(LightRecord r, float now)
    {
        Light l = r.Light;
        if (l == null)
            return;
        r.Pinned = true;
        r.PinnedSince = now;
        l.renderMode = LightRenderMode.ForcePixel;
        if (l.shadows != LightShadows.None)
        {
            r.AuthoredShadows = l.shadows;     // re-read: the game may have re-authored it since adoption
            l.shadows = LightShadows.None;
            r.ShadowsSuppressed = true;
            r.LastShadows = LightShadows.None; // our own write must not read back as a watcher event
        }
    }

    /// <summary>Hand the authored renderMode and shadow setting back.</summary>
    private static void Unpin(LightRecord r)
    {
        r.Pinned = false;
        Light l = r.Light;
        if (l == null)
        {
            r.ShadowsSuppressed = false;
            return;
        }
        l.renderMode = r.OriginalMode;
        if (r.ShadowsSuppressed)
        {
            l.shadows = r.AuthoredShadows;
            r.ShadowsSuppressed = false;
            r.LastShadows = r.AuthoredShadows;
        }
    }

    // -----------------------------------------------------------------------------------------
    // The damper — LateUpdate, after every game Update has written, before the frame renders
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The LateUpdate host. It exists because the damper's whole correctness argument is ORDER: it
    /// has to run after <c>LightFlicker.Update</c>, <c>FireLight.Update</c>,
    /// <c>RFX4_LightCurves.Update</c> and every coroutine, and before the frame renders. Unity runs
    /// all <c>Update</c>s and all coroutine resumptions before any <c>LateUpdate</c>, so this is that
    /// slot. The object is created on engage and destroyed on release, so a session that never sets
    /// the cap to 0 pays for nothing.
    /// </summary>
    private sealed class LateDriver : MonoBehaviour
    {
        /// <summary>[Optimize] CacheTickDelegates — one Action for the life of the driver instead of
        /// a fresh allocation from the method group every LateUpdate (gen0 pressure = head-turn
        /// hitches, the same reason BoardPing.Update and SelectionReadyHighlighter cache theirs).</summary>
        private static readonly System.Action Tick = DampAll;

        private void LateUpdate() => TickGuard.Run("Rig.LightStabiliser.Damp", Tick, "Rig");
    }

    private static LateDriver? _driver;

    private static void InstallDriver()
    {
        if (_driver != null)
            return;
        var go = new GameObject("GloomhavenVR.LightStabiliser");
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<LateDriver>();
    }

    private static void DestroyDriver()
    {
        if (_driver == null)
            return;
        try { Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    /// <summary>
    /// ONE walk over the held lights per frame. It measures the RAW signal the game just wrote
    /// (which is what makes this build's numbers comparable with ModBuild 228's), then writes the
    /// damped value, then measures what survived. It is the same walk for both jobs on purpose:
    /// the instrument and the remedy see exactly the same frames, so a discrepancy between them
    /// cannot be an artefact of sampling at different times — this project has paid for the opposite
    /// arrangement ("the supersampler ran at factor 1.0 and a still window merely FROZE the alias
    /// phase").
    /// </summary>
    private static void DampAll()
    {
        if (!_engaged || Lights.Count == 0)
            return;

        CostClock.Restart();

        float damping = Mathf.Clamp01(FlickerDamping!.Value);
        float tau = Mathf.Max(ResponseSeconds!.Value, 0.01f);
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        // Exponential rolling average with a TIME CONSTANT rather than a per-frame lerp factor: the
        // headset runs 90 Hz and the flat window does not, and a fixed per-frame factor would mean a
        // different response at every frame rate. k = 1 - e^(-dt/tau) is the frame-rate-independent
        // form and is exact for any dt.
        float k = 1f - Mathf.Exp(-dt / tau);
        // At damping 1.0 the output IS the input; writing it back would be a pointless write on
        // every light every frame (and would make the residual counters meaningless by construction).
        bool writing = damping < 0.999f;
        float now = Time.unscaledTime;

        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            Light l = r.Light;
            if (l == null)
                continue;

            // ---- watcher: activation / enable / shadows on the RAW state -----------------------
            bool active = l.gameObject.activeInHierarchy;
            if (active != r.LastActive)
            {
                r.ActivationEvents++;
                r.LastActive = active;
                if (active)
                {
                    // Coming BACK from inactive (DynamicAmbience.SetLightLevel's
                    // `SetActive(level > 0f)`, DynamicAmbience.cs:170, or a tile's content being
                    // shown): whatever level it returns at is a new fact, not a deviation from the
                    // level it had before it left. Snap the baseline and re-arm the warm-up so it is
                    // never damped back toward a stale value.
                    r.HaveBaseline = false;
                    r.HaveParentPose = false;
                    r.AdoptedAt = now;
                }
            }

            bool on = l.enabled;
            if (on != r.LastEnabled)
            {
                r.EnableEvents++;
                r.LastEnabled = on;
                // Same argument as the activation flip above: FireLight.Extinguish sets
                // `m_Light.enabled = false` (FireLight.cs:34) and a re-lit torch comes back at
                // whatever level its script decides, which is a new fact and not a deviation.
                if (on) { r.HaveBaseline = false; r.HaveParentPose = false; r.AdoptedAt = now; }
            }

            LightShadows sh = l.shadows;
            if (sh != r.LastShadows) { r.ShadowEvents++; r.LastShadows = sh; }

            if (!active || !on)
            {
                // Nothing is writing it and nothing is drawing it. Drop the cached parent pose so
                // the first frame back is never mistaken for "the parent stood still" — that stale
                // comparison would let a light whose prop moved while it was hidden be pinned to a
                // local offset measured in a different place.
                r.HaveParentPose = false;
                continue;
            }

            // ---- intensity ---------------------------------------------------------------------
            float raw = l.intensity;

            if (raw < r.IntensityFloor)
                r.IntensityFloor = raw;
            float floorRef = Mathf.Max(r.IntensityFloor, 0.001f);
            float rawJump = Mathf.Abs(raw - r.LastRawIntensity);
            if (rawJump > floorRef * IntensityEventFraction)
            {
                r.RawIntensityEvents++;
                r.WorstRawJump = Mathf.Max(r.WorstRawJump, rawJump / floorRef);
            }
            r.LastRawIntensity = raw;

            if (!r.HaveBaseline)
            {
                r.HaveBaseline = true;
                r.BaselineIntensity = raw;
                r.BaselineLocal = r.FirstBaselineLocal = r.Transform.localPosition;
                r.LastOutIntensity = raw;
            }

            bool damp = writing;
            if (damp && r.ExcludeRule.Length > 0) { damp = false; _excludedSkips++; }
            else if (damp && now - r.AdoptedAt < WarmUpSeconds) { damp = false; _warmUpSkips++; }

            float dev = raw - r.BaselineIntensity;
            float baseRef = Mathf.Max(Mathf.Abs(r.BaselineIntensity), 0.001f);
            if (Mathf.Abs(dev) > baseRef * LargeDeviationFraction)
                r.LargeDeviationEvents++;   // COUNTED, never gated on — see the class doc

            float outIntensity = damp ? r.BaselineIntensity + dev * damping : raw;
            // Advance the baseline from the RAW value, never from our own output. Updating it from
            // the output is a feedback loop: the filter would chase its own error and walk the light.
            r.BaselineIntensity += dev * k;

            if (damp)
                l.intensity = outIntensity;

            float outJump = Mathf.Abs(outIntensity - r.LastOutIntensity);
            if (outJump > floorRef * IntensityEventFraction)
            {
                r.ResidualIntensityEvents++;
                r.WorstResidualJump = Mathf.Max(r.WorstResidualJump, outJump / floorRef);
            }
            r.LastOutIntensity = outIntensity;

            // ---- position ----------------------------------------------------------------------
            Transform t = r.Transform;
            Transform? parent = t.parent;
            Vector3 pPos = parent != null ? parent.position : Vector3.zero;
            Quaternion pRot = parent != null ? parent.rotation : Quaternion.identity;
            // A light whose PARENT moved this frame must not have its local offset pinned: its
            // localPosition is a stale-free quantity only while the frame under it is still. This is
            // the "angular sizing needs the scale" discipline — establish the frame before touching
            // the number.
            bool parentStill = r.HaveParentPose
                               && (pPos - r.LastParentPos).sqrMagnitude < 1e-8f
                               && Quaternion.Angle(pRot, r.LastParentRot) < 0.01f;
            r.LastParentPos = pPos;
            r.LastParentRot = pRot;
            r.HaveParentPose = true;

            Vector3 rawLocal = t.localPosition;
            Vector3 posDev = rawLocal - r.BaselineLocal;
            float rawPosDev = posDev.magnitude;
            if (rawPosDev > r.WorstRawPosDev)
                r.WorstRawPosDev = rawPosDev;

            // RELOCATION RELEASE. If the BASELINE itself has travelled away from where the light was
            // first seen, the light is being moved, not jittered — a prop that walked, or a script
            // writing an absolute world position that no longer matches its owner (which ModBuild
            // 228's watch line already warned about in words). Holding a stale local position for
            // that light would be a hard visual bug, not a lag, so position damping is released for
            // it permanently and counted.
            if (r.PositionDamped
                && (r.BaselineLocal - r.FirstBaselineLocal).magnitude > PositionRelocateWU)
            {
                r.PositionDamped = false;
                r.RelocationReleased = true;
            }

            bool dampPos = damp && r.PositionDamped && parentStill;
            Vector3 baseBefore = r.BaselineLocal;
            Vector3 outLocal = dampPos ? baseBefore + posDev * damping : rawLocal;
            r.BaselineLocal += posDev * k;

            if (dampPos)
                t.localPosition = outLocal;

            // Measured against the SAME (pre-update) baseline as rawPosDev above, so the two numbers
            // in the watch line are the same quantity with and without the damper and can be read as
            // a ratio. Comparing one against the old baseline and the other against the new one would
            // be an instrument that flatters itself.
            float residualPosDev = (outLocal - baseBefore).magnitude;
            if (residualPosDev > r.WorstResidualPosDev)
                r.WorstResidualPosDev = residualPosDev;
        }

        CostClock.Stop();
        _costTicks += CostClock.ElapsedTicks;
        _costFrames++;

        if (Time.unscaledTime >= _nextWatchReportTime)
        {
            _nextWatchReportTime = Time.unscaledTime + WatchReportSeconds;
            ReportWatch();
            // THE WINDOW RESET IS THE MECHANISM'S, not the report's: the accumulators above are
            // written here, so they are cleared here, immediately after the line that printed
            // them. ReportWatch is text only and can be gated or retired without losing the reset.
            _costTicks = 0;
            _costFrames = 0;
            _warmUpSkips = 0;
            _excludedSkips = 0;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Reporting
    // -----------------------------------------------------------------------------------------

    /// <summary>Measured self-cost clause. MEASURED, never asserted — a per-frame LateUpdate over
    /// forty-odd lights ought to be microseconds, and "ought to" is not a number.</summary>
    private static string CostClause()
    {
        if (_costFrames == 0)
            return "COST, measured not asserted: the damper did not run this window (0 frames).";
        double totalMs = _costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double meanUs = totalMs * 1000.0 / _costFrames;
        return $"COST, measured not asserted: the LateUpdate damper walked {Lights.Count} light(s) "
               + $"over {_costFrames} frame(s) for {totalMs:F1}ms total, mean {meanUs:F1}µs/frame "
               + "(Stopwatch around the walk itself, not an estimate).";
    }

    /// <summary>Aggregate the per-light MonoBehaviour type names into a histogram string. THIS is
    /// what ModBuild 228 was missing: it knew only about <c>LightFlicker</c>, so when silencing
    /// LightFlicker left the intensity churn standing, the log could not name what was writing.</summary>
    private static string ScriptHistogram()
    {
        var hist = new Dictionary<string, int>(16);
        int none = 0;
        for (int i = 0; i < Lights.Count; i++)
        {
            string[] names = Lights[i].OwnScripts;
            if (names.Length == 0) { none++; continue; }
            for (int n = 0; n < names.Length; n++)
                hist[names[n]] = hist.TryGetValue(names[n], out int c) ? c + 1 : 1;
        }

        var ordered = new List<KeyValuePair<string, int>>(hist);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new StringBuilder(200);
        for (int i = 0; i < ordered.Count && i < 12; i++)
            sb.Append(i == 0 ? "" : ", ").Append(ordered[i].Key).Append(' ').Append(ordered[i].Value);
        if (ordered.Count > 12)
            sb.Append(", and ").Append(ordered.Count - 12).Append(" further type(s)");
        if (none > 0)
            sb.Append(ordered.Count > 0 ? ", " : "").Append("none ").Append(none);
        if (sb.Length == 0)
            sb.Append("(no MonoBehaviour on any watched light)");
        return sb.ToString();
    }

    /// <summary>
    /// The census. Everything in it is a number nobody has had before, and after ModBuild 228 the
    /// most important one is the TYPE HISTOGRAM: the writer that kept the churn alive with every
    /// LightFlicker silenced has to be a type in that list.
    /// </summary>
    private static void LogCensus(int lightCount, int newLights, int pinChanges)
    {
        float damping = Mathf.Clamp01(FlickerDamping!.Value);
        float tau = ResponseSeconds!.Value;
        float meanAmount = _flickers.Count > 0 ? _flickers.SumAmount / _flickers.Count : 0f;
        float now = Time.unscaledTime;

        // Authored-state census, read from the RECORDS' captured originals rather than from the live
        // Light. ModBuild 228 read the live renderMode and therefore counted its OWN pinned lights as
        // authored-ForcePixel: its log climbed "1 ForcePixel" → "4 ForcePixel" while
        // PinnedPixelLights was 1, which read as a fact about the scene and was a fact about the mod.
        int authoredForcePixel = 0, authoredForceVertex = 0, baked = 0, shadowCasters = 0;
        int forcePixelShadowCasters = 0;
        int excluded = 0, ownVfx = 0, poolChild = 0, additive = 0, warmingUp = 0;
        int posReleased = 0, pinnedNow = 0, shadowsSuppressed = 0;
        var excludedNames = new List<string>(ExcludedNameCap + 1);
        var pinnedNames = new List<string>(MaxPinned);

        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            if (r.Baked) baked++;
            else if (r.OriginalMode == LightRenderMode.ForceVertex) authoredForceVertex++;
            else if (r.OriginalMode == LightRenderMode.ForcePixel) authoredForcePixel++;

            if (!r.Baked && r.AuthoredShadows != LightShadows.None)
            {
                shadowCasters++;
                if (r.OriginalMode == LightRenderMode.ForcePixel)
                    forcePixelShadowCasters++;
            }

            if (r.ExcludeRule.Length > 0)
            {
                excluded++;
                if (r.ExcludeRule == RuleOwnVfx) ownVfx++;
                else if (r.ExcludeRule == RuleParticlePool) poolChild++;
                else if (r.ExcludeRule == RuleAdditive) additive++;
                if (excludedNames.Count < ExcludedNameCap)
                    excludedNames.Add($"'{r.Name}' [{r.ExcludeRule}]");
            }
            else if (now - r.AdoptedAt < WarmUpSeconds)
            {
                warmingUp++;
            }

            if (r.RelocationReleased) posReleased++;
            if (r.Pinned)
            {
                pinnedNow++;
                if (r.ShadowsSuppressed) shadowsSuppressed++;
                pinnedNames.Add($"'{r.Name}' {r.OriginalMode}→ForcePixel"
                                + (r.ShadowsSuppressed
                                    ? $" (authored {r.AuthoredShadows}, SUPPRESSED while pinned)"
                                    : " (casts no shadow)"));
            }
        }

        var sb = new StringBuilder(2600);
        sb.Append("LIGHT STABILISER engaged at per-pixel cap 0 — holding ").Append(Lights.Count)
          .Append(" of ").Append(lightCount).Append(" scene light(s), ").Append(newLights)
          .Append(" newly recorded; ").Append(pinChanges).Append(" pin change(s) this cycle. ");

        sb.Append("REMEDY: every held light's intensity and localPosition is filtered in LateUpdate "
                  + "to 'rolling average + (what the game wrote - that average) x ")
          .Append(damping.ToString("F2")).Append("', rolling-average time constant ")
          .Append(tau.ToString("F2")).Append("s. NOTE THE CHANGE OF MECHANISM from ModBuild 228: "
                  + "that build scaled LightFlicker.amount and its own log recorded 'flicker "
                  + "amplitude scaled to 0% of authored on 46 component(s)' — which "
                  + "decompiled/ThirdParty/LightFlicker.cs:50 makes provably inert on intensity — "
                  + "and STILL measured 10..148 intensity moves per 15s window. So this class no "
                  + "longer edits anyone's fields; it owns the final value instead, which reaches "
                  + "FireLight, the RFX4 family and anything else present or future without knowing "
                  + "their names. ");

        sb.Append("WRITER ATTRIBUTION — MonoBehaviour types on the watched lights' own GameObjects: ")
          .Append(ScriptHistogram())
          .Append(". Whatever kept writing intensity after every LightFlicker was silenced is a type "
                  + "in that list; ModBuild 228's census knew only LightFlicker, which is exactly why "
                  + "it could not name the culprit. ");

        sb.Append("DAMPED SET: ").Append(Lights.Count - excluded).Append(" of ").Append(Lights.Count)
          .Append(" light(s) are in the damped set; ").Append(excluded)
          .Append(" excluded (").Append(ownVfx).Append(' ').Append(RuleOwnVfx).Append(", ")
          .Append(poolChild).Append(' ').Append(RuleParticlePool).Append(", ")
          .Append(additive).Append(' ').Append(RuleAdditive).Append(")");
        if (excludedNames.Count > 0)
        {
            sb.Append(": ");
            for (int i = 0; i < excludedNames.Count; i++)
                sb.Append(i == 0 ? "" : ", ").Append(excludedNames[i]);
            if (excluded > excludedNames.Count)
                sb.Append(", and ").Append(excluded - excludedNames.Count).Append(" more");
        }
        sb.Append(". ").Append(warmingUp)
          .Append(" further light(s) are inside the ").Append(WarmUpSeconds.ToString("F0"))
          .Append("s warm-up at this instant and are not damped yet. READ THIS BEFORE READING THE "
                  + "WATCH LINE: if the residual move count below stays high, this clause says "
                  + "whether that is a damper that is too weak or a light that was never in the set. "
                  + "The exclusion rules are deliberately narrow and are keyed to a named component "
                  + "on the light itself or on its IMMEDIATE parent (RFX4_* / RFX4_ParticleLight's "
                  + "own pooled children / CharacterRevealScript's additively-written child light). "
                  + "Ancestor-ParticleSystem containment is NOT a rule: a torch's steady point light "
                  + "lives in the same prop as its fire emitter, and excluding on containment would "
                  + "have exempted exactly the lights this whole build is about. ");

        sb.Append("FLICKER CENSUS (read only — nothing is written to these components any more): ")
          .Append(_flickers.Count).Append(" LightFlicker(s), authored amount min ")
          .Append(_flickers.MinAmount.ToString("F4")).Append(" / mean ")
          .Append(meanAmount.ToString("F4")).Append(" / max ")
          .Append(_flickers.MaxAmount.ToString("F4")).Append(", speed ")
          .Append(_flickers.MinSpeed.ToString("F1")).Append("..")
          .Append(_flickers.MaxSpeed.ToString("F1")).Append(" (the component's SHIPPED defaults are "
                  + "amount 0.0100 and speed 8.0). ")
          .Append(_flickers.JitterCount)
          .Append(" of them also JITTER THE LIGHT'S POSITION (adjustLocation), worst "
                  + "locationAdjustAmount ").Append(_flickers.MaxJitter.ToString("F3"))
          .Append(" world units — LightFlicker.cs:52-56 writes that position INDEPENDENTLY of "
                  + "amount, which is why ModBuild 228 still measured a steady 0.0199-world-unit "
                  + "drift on 'FireTorch_PointLight' in every window including the ones where the "
                  + "damping was 0. A light that moves re-ranks by DISTANCE every frame just as "
                  + "surely as one that brightens, so position is damped now too. ")
          .Append(_flickers.WithoutLight)
          .Append(" flicker(s) carry no Light at all and only animate a MESH — that is why holding "
                  + "the lights perfectly still does not make the fire look dead. ")
          .Append(posReleased).Append(" light(s) have had position damping RELEASED because their "
                  + "own baseline travelled past ").Append(PositionRelocateWU.ToString("F2"))
          .Append(" world units (they are being relocated, not jittered). ");

        sb.Append("LIGHT CENSUS at cap 0, from the AUTHORED state captured at adoption: ")
          .Append(Lights.Count).Append(" held light(s); ").Append(baked).Append(" baked, ")
          .Append(authoredForceVertex).Append(" ForceVertex, ").Append(authoredForcePixel)
          .Append(" ForcePixel, ").Append(shadowCasters).Append(" authored shadow caster(s). ");
        if (authoredForcePixel > 0)
        {
            sb.Append("THAT AUTHORED ForcePixel COUNT IS NOT ZERO, AND IT MATTERS: Unity's forward "
                      + "path promotes an Important light per-pixel BEFORE it consults "
                      + "pixelLightCount, so those ").Append(authoredForcePixel)
              .Append(" are still per-pixel at cap 0 and still cost one extra pass per renderer they "
                      + "touch — ").Append(forcePixelShadowCasters)
              .Append(" of them also still render a shadow map. 'Cap 0' is therefore not the zero it "
                      + "looks like, and the [Perf] GFX line cannot see this because it reads the "
                      + "authored shadows flag and knows nothing about the cap. ");
        }
        else
        {
            sb.Append("No light is AUTHORED ForcePixel, so cap 0 really is zero per-pixel lights and "
                      + "zero shadow maps from the game's own set: the ").Append(shadowCasters)
              .Append(" light(s) the [Perf] GFX line calls REAL-TIME SHADOW-CASTING are counted from "
                      + "their authored shadows flag and are NOT on the bill at this cap. ");
        }

        sb.Append("PINNED: ").Append(pinnedNow).Append(" light(s) on the per-pixel path");
        if (pinnedNow > 0)
        {
            sb.Append(": ");
            for (int i = 0; i < pinnedNames.Count; i++)
                sb.Append(i == 0 ? "" : ", ").Append(pinnedNames[i]);
            sb.Append("; ").Append(shadowsSuppressed)
              .Append(" of them had shadow casting suppressed for the duration of the pin, so the "
                      + "promotion costs one forward pass and NOT the shadow map the cap removed "
                      + "(ModBuild 228 pinned a shadow caster and called it a feature — it was a "
                      + "cost)");
        }
        else
        {
            sb.Append(" (PinnedPixelLights is 0)");
        }
        sb.Append(". Everything here is recorded and restored when the cap leaves 0 or the rig tears "
                  + "down — grep '[Rig] LIGHT STABILISER released'. ");

        sb.Append(CostClause());

        VRLog.Info("Rig", sb.ToString());
    }

    /// <summary>
    /// THE OUTCOME LINE. It reports BOTH sides of the damper — what the game wrote (RAW) and what
    /// survived into the rendered value (RESIDUAL) — because a fix measured by the wrong thing
    /// carries zero information, and this project has paid for that twice. The RAW column is
    /// deliberately computed exactly the way ModBuild 228 computed its single column, so the two
    /// logs can be compared line for line.
    /// </summary>
    private static void ReportWatch()
    {
        int totalActivation = 0, totalEnable = 0, totalShadow = 0;
        int totalRaw = 0, totalResidual = 0, totalLarge = 0;
        float worstRaw = 0f, worstResidual = 0f, worstRawPos = 0f, worstResidualPos = 0f;
        LightRecord? worstRawRec = null;
        LightRecord? worstResidualRec = null;
        LightRecord? worstRawPosRec = null;
        int excluded = 0, warmingUp = 0;
        float now = Time.unscaledTime;

        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            totalActivation += r.ActivationEvents;
            totalEnable += r.EnableEvents;
            totalShadow += r.ShadowEvents;
            totalRaw += r.RawIntensityEvents;
            totalResidual += r.ResidualIntensityEvents;
            totalLarge += r.LargeDeviationEvents;
            if (r.WorstRawJump > worstRaw) { worstRaw = r.WorstRawJump; worstRawRec = r; }
            if (r.WorstResidualJump > worstResidual)
            {
                worstResidual = r.WorstResidualJump;
                worstResidualRec = r;
            }
            if (r.WorstRawPosDev > worstRawPos) { worstRawPos = r.WorstRawPosDev; worstRawPosRec = r; }
            if (r.WorstResidualPosDev > worstResidualPos) worstResidualPos = r.WorstResidualPosDev;
            if (r.ExcludeRule.Length > 0) excluded++;
            else if (now - r.AdoptedAt < WarmUpSeconds) warmingUp++;
        }

        _watchWindows++;
        bool quiet = totalActivation == 0 && totalEnable == 0 && totalShadow == 0
                     && totalRaw == 0 && worstRawPos < 0.0005f;

        if (!quiet)
        {
            var sb = new StringBuilder(2400);
            sb.Append("LIGHT WATCH (").Append(WatchReportSeconds.ToString("F0")).Append("s window ")
              .Append(_watchWindows).Append(", ").Append(Lights.Count)
              .Append(" light(s) watched at per-pixel cap 0, ").Append(Lights.Count - excluded)
              .Append(" of them in the damped set, ").Append(warmingUp)
              .Append(" in warm-up right now): ").Append(totalActivation)
              .Append(" GameObject activation flip(s), ").Append(totalEnable)
              .Append(" Light.enabled flip(s), ").Append(totalShadow)
              .Append(" shadows-flag change(s). ");

            sb.Append("INTENSITY, BOTH SIDES OF THE DAMPER: ").Append(totalRaw)
              .Append(" RAW move(s) past ").Append(IntensityEventFraction.ToString("P0"))
              .Append(" of the light's own observed floor (worst ")
              .Append(worstRaw.ToString("P0")).Append(" on '").Append(worstRawRec?.Name ?? "-")
              .Append("'") .Append(DampStatus(worstRawRec, now)).Append("), leaving ")
              .Append(totalResidual).Append(" RESIDUAL move(s) in the value actually rendered "
                      + "(worst ").Append(worstResidual.ToString("P0")).Append(" on '")
              .Append(worstResidualRec?.Name ?? "-").Append("'")
              .Append(DampStatus(worstResidualRec, now)).Append("). INTERCEPTED: ")
              .Append(totalRaw - totalResidual).Append(" of ").Append(totalRaw).Append(". ");

            sb.Append("POSITION: worst RAW localPosition deviation from the rolling baseline ")
              .Append(worstRawPos.ToString("F4")).Append(" world units on '")
              .Append(worstRawPosRec?.Name ?? "-").Append("'")
              .Append(DampStatus(worstRawPosRec, now)).Append(", worst RESIDUAL ")
              .Append(worstResidualPos.ToString("F4")).Append(". ");

            sb.Append(totalLarge).Append(" raw deviation(s) past ")
              .Append(LargeDeviationFraction.ToString("P0"))
              .Append(" of a light's own baseline were seen and DAMPED ANYWAY — that count is here "
                      + "so a genuine large event that ends up looking wrong is attributable; it is "
                      + "not gated on, because FireLight's own range is 0..2 around a mean of ~1 "
                      + "(FireLight.cs:23) and a large-deviation bypass would have let exactly the "
                      + "loudest complained-of swings through. ");

            AppendTopMovers(sb, now);

            sb.Append("SKIPPED, in light-frames, so a permanently-skipped population cannot pass for "
                      + "a working damper: ").Append(_warmUpSkips)
              .Append(" skipped by the ").Append(WarmUpSeconds.ToString("F0"))
              .Append("s warm-up and ").Append(_excludedSkips)
              .Append(" skipped by an exclusion rule, out of roughly ")
              .Append((long)Lights.Count * _costFrames).Append(" light-frame(s) walked. ");

            sb.Append("READ IT LIKE THIS. RAW high + RESIDUAL ~0 = the damper is working and the "
                      + "ranking inputs are now still; if the archway STILL steps at that point, the "
                      + "cause is not the lights' own values and the next round should read the "
                      + "renderer side (per-object light-list churn that leaves no trace on the "
                      + "Light) instead. RAW high + RESIDUAL nearly as high = the damper did not run "
                      + "on those lights: check the named lights' status tags above and the "
                      + "'DAMPED SET' clause of the census. Activation or enabled flips are "
                      + "DynamicAmbience's room cross-fade (SetActive(level>0) on cloned lights, "
                      + "DynamicAmbience.cs:170) or ProceduralMapTile.ShowContent turning a tile's "
                      + "props on and off — those step the ranking of every renderer near them and "
                      + "no per-light filter can smooth them. ")
              .Append(CostClause());

            VRLog.Info("Rig", sb.ToString());
        }
        else if (_watchWindows == 1)
        {
            VRLog.Info("Rig", $"LIGHT WATCH ({WatchReportSeconds:F0}s window 1, {Lights.Count} "
                              + "light(s) watched at per-pixel cap 0): NOTHING HAPPENED — no "
                              + "activation, enable or shadow flips, no RAW intensity move past "
                              + $"{IntensityEventFraction:P0} of any light's own floor, no position "
                              + "deviation. This line is printed for the first quiet window ONLY, so "
                              + "its absence later means 'still quiet', not 'not running'. A quiet "
                              + "watcher plus a live flicker report is itself a finding: it "
                              + "falsifies every light-state mechanism and points at the renderer "
                              + "side (per-object light-list churn that leaves no trace on the Light "
                              + "itself, or geometry/material work) instead. " + CostClause());
        }

        for (int i = 0; i < Lights.Count; i++)
            Lights[i].ResetWindow();
        // The four cost/skip accumulators are reset by the caller (DampAll), right after this
        // returns — the same statement order, in the method that writes them.
    }

    /// <summary>
    /// The status tag that makes a named light's number readable in one pass: was it damped, or was
    /// it never in the set? Without this, a high residual is ambiguous between "the damping is too
    /// weak" and "that light was excluded", and separating those two would cost a hardware round.
    /// </summary>
    private static string DampStatus(LightRecord? r, float now)
    {
        if (r == null)
            return "";
        if (r.ExcludeRule.Length > 0)
            return $", NOT DAMPED — excluded by rule {r.ExcludeRule}";
        if (now - r.AdoptedAt < WarmUpSeconds)
            return ", NOT DAMPED — still inside the warm-up";
        return r.PositionDamped
            ? ", DAMPED"
            : ", DAMPED on intensity, position damping RELEASED (baseline relocating)";
    }

    /// <summary>The three loudest raw movers, each with both columns and its status. This is the
    /// clause that answers "were 'FireTorch_PointLight' and 'Point light (2)' in the damped set at
    /// all?" without a second hardware round.</summary>
    private static void AppendTopMovers(StringBuilder sb, float now)
    {
        var top = new List<LightRecord>(TopMoverCount);
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            if (r.RawIntensityEvents == 0 && r.WorstRawPosDev < 0.0005f)
                continue;
            int at = 0;
            while (at < top.Count && top[at].WorstRawJump >= r.WorstRawJump)
                at++;
            if (at < TopMoverCount)
            {
                top.Insert(at, r);
                if (top.Count > TopMoverCount)
                    top.RemoveAt(top.Count - 1);
            }
        }

        if (top.Count == 0)
            return;

        sb.Append("LOUDEST MOVERS THIS WINDOW: ");
        for (int i = 0; i < top.Count; i++)
        {
            LightRecord r = top[i];
            sb.Append(i == 0 ? "" : "; ").Append('\'').Append(r.Name).Append("' raw ")
              .Append(r.RawIntensityEvents).Append(" move(s)/worst ")
              .Append(r.WorstRawJump.ToString("P0")).Append(" → residual ")
              .Append(r.ResidualIntensityEvents).Append(" move(s)/worst ")
              .Append(r.WorstResidualJump.ToString("P0")).Append(", raw pos dev ")
              .Append(r.WorstRawPosDev.ToString("F4")).Append("wu")
              .Append(DampStatus(r, now))
              .Append(", scripts on its own GameObject: ")
              .Append(r.OwnScripts.Length == 0 ? "none" : string.Join("+", r.OwnScripts));
        }
        sb.Append(". ");
    }

    // -----------------------------------------------------------------------------------------
    // Release
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Hand everything back. Called when the cap leaves 0, when the config row is switched off, on
    /// scene change and from <c>VRRigDriver.TearDownRig</c>/<c>OnDestroy</c>. Every mutation this
    /// class makes has to be reversible — the same rule that made <c>ApplyPixelLights</c>'s -1
    /// RESTORE rather than merely stop.
    ///
    /// <para>Note what does NOT need restoring any more: this class no longer writes any game
    /// component's field. The only persistent writes are <c>renderMode</c> and (on a pinned light)
    /// <c>shadows</c>. The damped <c>intensity</c> and <c>localPosition</c> need no restore at all,
    /// because every writer in the tree re-writes an absolute value from its own captured baseline on
    /// its very next Update — one frame after release the game owns those numbers again outright.</para>
    /// </summary>
    internal static void Release(string why)
    {
        if (!_engaged && Lights.Count == 0 && _driver == null)
            return;

        int unpinned = 0;
        int shadowsRestored = 0;
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            if (!r.Pinned)
                continue;
            if (r.ShadowsSuppressed)
                shadowsRestored++;
            Unpin(r);
            unpinned++;
        }

        int held = Lights.Count;
        Lights.Clear();
        _engaged = false;
        DestroyDriver();

        VRLog.Info("Rig", $"LIGHT STABILISER released — {why}. Restored the authored renderMode on "
                          + $"{unpinned} pinned light(s) and the authored shadow setting on "
                          + $"{shadowsRestored} of them; dropped {held} watched light(s) and "
                          + "destroyed the LateUpdate damper. Nothing of this class's is left on the "
                          + "scene: the damped intensity and localPosition need no restore, because "
                          + "every writer in the game re-writes an absolute value from its own "
                          + "captured baseline on its next Update.");
    }
}
