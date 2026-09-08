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
///   _GhvrElemA    = float4(Fire,  Ice,  Air,   Earth)   // each 0..1
///   _GhvrElemB    = float4(Light, Dark, Master, Peak)
///   _GhvrElemGrow = float(EarthGrowth)                  // 0..1, see THE GROWTH CHANNEL
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
/// <item><b>_GhvrElemB.w — PEAK.</b> The largest of the six intensities AND of the growth channel,
/// BEFORE master. Purely a convenience: it is the one component a shader can branch or LOD on ("is
/// anything up at all?") without reading and max-ing two vectors. Redundant by construction, and
/// cheap — one float compare per element in a loop this file already runs. <b>THE GROWTH CHANNEL IS
/// IN IT ON PURPOSE</b>: since 2026-09-05 the art can still be DRAWING (grass standing in a room
/// while it withers) with all six intensities already at 0, and a peak that said 0 there would make
/// the one number the bundle branches on (<c>e.live</c>, EnvElement.cginc rule 2) a lie — every
/// consumer that brackets its element block on it would fold the grass flat in one frame, which is
/// precisely the pop this channel exists to prevent.</item>
/// <item><b>_GhvrElemGrow — EARTH'S GROW-IN.</b> A separate scalar with a separate curve, and the
/// whole of THE GROWTH CHANNEL below. Like the six it is NOT pre-multiplied by the master.</item>
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
/// (<c>Net.Remote.RemoteElementStrip.Refresh</c> — cited by NAME, not by line: the line numbers
/// this comment used to carry were stale two builds after they were written): six
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
/// (<c>Net.Remote.RemoteElementStrip</c>'s class doc: "CLASSIFICATION: GLOBAL — scenario-wide state,
/// bit-identical on every client, ZERO wire"). The only thing that is NOT automatically identical
/// is the SMOOTHING, which is why the curve is driven by the shared clock — see below.</para>
///
/// <para><b>THE ONE EXCEPTION, and it is not this state: THE DEBUG TEST OVERRIDE.</b> Since
/// 2026-08-15 the Erweitert page's element latches ARE on the wire (extension record 32,
/// <c>Net/RemoteTestTriggers</c>) on the user's ruling that a debug press must be visible to
/// everyone. That does not touch the paragraph above: what travels is a pair of six-bit masks
/// describing a LIE this file tells between sensing and publishing, never the game's element board,
/// which is still read and never written on every client.</para>
///
/// <para><b>THE CURVE.</b> Each element carries a 0..1 intensity rather than the raw enum:</para>
/// <list type="bullet">
/// <item><c>Strong</c> → target <b>1.00</b>, rock steady. This is the state the player also HEARS
/// (<c>InfusionElementUI.SetState</c> plays <c>changeToStrongElementAudioItem</c> on the transition
/// into Strong, GH.Runtime/InfusionElementUI.cs:120-134), so it is the one that must read as "fully
/// charged".</item>
/// <item><c>Waning</c> → a plateau of <b>0.40</b>, ROCK STEADY TOO. Until 2026-09-06 this plateau
/// BREATHED (0.40 ± 0.12 on a 2.4 s sine) and that breath was the defect this file's second
/// hardware round is about — see NO ENVIRONMENT EFFECT MAY BLINK below. The reading a player gets
/// from across the table is now the NUMBER: 0.40 against Strong's 1.00 is a 2.5x difference in
/// every effect the channel drives, which is "weniger intensiv" in the user's own words and is
/// visible at a glance without anything moving. <b>REJECTED: a monotone decay across the waning
/// round.</b> Nothing tells this code when the round ends — <c>EndRound</c> (:122) is event-driven,
/// not timed, so a decay ramp would have to guess a duration and would then either finish early
/// (showing 0 while the element is still usable) or be cut off mid-fall. <b>REJECTED: making Waning
/// carry PRESENCE like the growth channel does</b> (i.e. publishing 1.00 for Waning as well). It
/// would obey the no-blinking ruling and throw away the other half of the sentence: the user asked
/// for half strength to be "weniger in der Anzahl oder weniger intensiv", so the two states must
/// still differ — just not in time.</item>
/// <item><c>Inert</c> → <b>0.00</b>.</item>
/// <item>Every transition RAMPS over <see cref="RampSeconds"/> = <b>1.0 s</b> on a smoothstep, from
/// whatever the intensity happened to be at the moment of the change — so a Strong→Waning→Inert
/// chain never pops and never restarts from the wrong value. This is the ONE motion left in the
/// channel and it is a ONE-SHOT: it is triggered by the game's board changing, it finishes, and it
/// does not come back until the board moves again.</item>
/// </list>
///
/// <para><b>NO ENVIRONMENT EFFECT MAY BLINK — the standing ruling this channel is built around
/// (2026-09-06).</b> USER, on hardware, verbatim: "Auch das Eis blinkt in nem Loop wenn es nur zur
/// Hälfte aktiv ist. Ich hatte die 'Hälften' nie getestet daher ist mir das nie aufgefallen. Ich
/// will so ein Blinken generell nicht. Die Umgebungseffekte sollen genau wie beim 'vollen' sein -
/// nur weniger in der Anzahl oder weniger intensiv - aber niemals blinkend."</para>
///
/// <para>The last sentence is a RULE and not a bug report about ice: <b>a half-active element
/// differs from a full one in COUNT or in AMPLITUDE, never in TIME.</b> Ice is simply what he
/// happened to be looking at.</para>
///
/// <para><b>THE MECHANISM, and why it could only be fixed here.</b> The waning plateau was the ONLY
/// term anywhere on the element→environment path that was a function of the clock AT ONE STRENGTH
/// AND NOT AT THE OTHERS. Strong published a constant 1.00 and Inert a constant 0.00; Waning
/// published <c>0.40 + 0.12·sin(2π·clock/2.4)</c>. Everything downstream inherited it, and the
/// consumers that inherited it WORST are the ones built on a THRESHOLD, where a smooth ±0.12 on the
/// input is a hard on/off on the output:
/// <list type="bullet">
/// <item>the frost frontier (<c>GhvrGrow</c>, EnvGrowth.cginc) — through its ease, the breath swept
/// the coverage threshold 0.72…0.84 across a frontier only 0.12 wide, i.e. the WHOLE frontier width,
/// coherently over the entire room, every 2.4 s. That is the ice the user watched;</item>
/// <item>the haunt schedule (<c>GhvrHauntAtRaw</c> / <see cref="Haunt"/>'s C# mirror) — Dark and
/// Light bend <c>freq</c>, which a <c>step()</c> turns into whether a slot fires at all, so a
/// half-active Dark flipped apparitions on and off on the same 2.4 s;</item>
/// <item>the element sound beds (<c>EnvSound.TickBeds</c>) — the bed level tracks the published
/// value fast enough to follow it, so a half-active Air or Fire was audibly swelling and ebbing.</item>
/// </list>
/// A remedy at any one of those would have been a remedy for one of them. The term is here, so the
/// fix is here, and it is a deletion.</para>
///
/// <para><b>THE EVIDENCE, from the shipped log and with no new instrument</b> (ModBuild 448,
/// <c>] [Core] ELEMENT MOOD:</c>, host and peer). Ice went Waning at shared clock 2819.28 s. At
/// 3169.61 s — 350 s later, three hundred ramps after the transition, with the board perfectly
/// still — the host printed <c>Ice=Waning now 0.29</c>; at 3190.30 s it printed <c>Ice=…now 0.52</c>.
/// The same number, 0.23 apart, with nothing in the game having changed. The peer, on its own
/// machine, printed 0.52 at 3190.29 s — identical, which is the shared clock working exactly as
/// designed and is why BOTH players saw the same blink rather than only one of them.</para>
///
/// <para><b>WHAT WAS DELIBERATELY NOT CHANGED, because it is not this defect.</b> The art carries
/// its own clocks — the candle flicker, <c>GhvrEmberBreath</c>, <c>GhvrWind</c>, the spark sine,
/// <c>GhvrGrowCreep</c>'s slow travelling frost frontier (the user's own ModBuild 142 request,
/// "animiert und immersiv"). Every one of them runs IDENTICALLY at Strong, at Waning and at every
/// value in between; an element scales their amplitude and never their rate (the project's
/// frequency-scrub rule, EnvGrowth.cginc). They are what the full-strength picture is made of, and
/// the ruling says half must look like full — so removing them would break the sentence it is meant
/// to satisfy. What the ruling forbids is a term that is periodic BECAUSE the element is half, and
/// after this round there is not one.</para>
///
/// <para><b>THE GROWTH CHANNEL, and why a second curve rather than a second reading of the first
/// one (2026-09-05).</b> USER, on hardware: "Das Gras im Wald, das wegen dem Element aufgetaucht
/// ist, wächst und verschwindet in einem Loop statt einmal zu wachsen und dann konstant da zu sein!
/// Wieso das? Das soll nicht sein. Es ist aufgefallen als das Element nur halb aktiv war."</para>
///
/// <para>"Nur halb aktiv" names the state exactly: it is the WANING PLATEAU, and the breath THE
/// PLATEAU CARRIED AT THE TIME was the loop. That round argued the breath was right for a PIXEL
/// effect — a frost frontier that advances and retreats a few centimetres is a surface being taken
/// and given back — and wrong only for GEOMETRY. THE NEXT ROUND FALSIFIED THAT ARGUMENT: the user
/// reported the same loop on the ICE, i.e. on the pixel effect the exemption was written for, and
/// ruled the breath out everywhere. The paragraph is kept as the record of a half-fix. Earth's only
/// remaining effect on a surface
/// is <c>GhvrGrowCard</c> (EnvGrowth.cginc), a VERTEX FOLD that collapses a grass card onto its own
/// base edge; the fold is driven through a THRESHOLD, so a card is either standing or has zero area.
/// Sweeping that threshold with a 2.4 s sine does not make the grass breathe, it makes each blade
/// near the frontier stand up and lie flat, forever. Measured on the preview harness (the forest's
/// FloorToMoon station, one clock, only the published Earth value differing): the two phases 1.2 s
/// apart differ in 6,140 pixels, against 24,026 for the whole difference between no grass and full
/// grass — a quarter of the grass toggling every 1.2 s.</para>
///
/// <para><b>SO GROWTH IS PUBLISHED SEPARATELY, AND IT CARRIES PRESENCE RATHER THAN STRENGTH.</b>
/// <c>_GhvrElemGrow</c> is 1 while Earth is up AT ALL — Strong and Waning are the same number, and
/// that identity is the fix: nothing the element does between "charged" and "about to go" can move a
/// blade of grass. Only Inert is 0. The six intensities are untouched, so the frost path, the glow,
/// the flames and the particles all kept the breath they were tuned with. THAT LAST CLAUSE IS WHAT
/// CAME BACK: the frost is one of those, and it is what the user saw next. The breath is gone from
/// the six as well now, and this channel's identity argument — Strong and Waning are the same
/// number — is unchanged and is still the reason the grass cannot move.</para>
///
/// <para><b>WHAT HAPPENS WHEN EARTH GOES INERT, which is a choice and not a default.</b> Grass that
/// never dies is wrong: a scenario would accumulate vegetation nobody can explain and the room would
/// never return to the state it was authored in. Grass that pops away is wrong too, and it is the
/// same fault as the loop — a plant is not a HUD chip and may not answer at HUD speed. So the third
/// thing: the growth channel RISES over <see cref="RampSeconds"/>, exactly as it does today (the
/// grow-in is the one thing about Earth the user has never objected to, and this preserves it to the
/// number), and FALLS over <see cref="WitherSeconds"/> — nine seconds, and a ONE-SHOT rather than a
/// cycle: it runs once, reaches 0 and stops, so it can never be mistaken for a loop even by someone
/// who walks in on the middle of one. It is one-way while Earth stands: within a single presence episode
/// the channel rises to 1 and then does not move at all.</para>
///
/// <para><b>AND WHY THAT IS STILL BIT-IDENTICAL BETWEEN CLIENTS — the part a true ratchet would have
/// broken.</b> The obvious spelling of "grows once and stays" is an accumulator,
/// <c>grow = max(grow, target)</c>. It is wrong here and the reason is worth writing down: an
/// accumulator is HISTORY, and two clients do not share a history. A client present when Earth was
/// Strong would hold 1.0 through the waning round; a client who joined DURING that round would never
/// have seen the Strong and would hold the waning value forever after. Two players, one room, two
/// different lawns, permanently, with no wire message that could ever reconcile them. What is
/// published instead is a pure function of the CURRENT column — present or not — smoothed by the
/// same closed-form, shared-clock ramp the six use. The only history in it is the ramp's FROM, and a
/// FROM is dead <see cref="RampSeconds"/> after the last change: any client settled for longer than
/// one ramp is at exactly the target, whatever it saw before. The one bounded disagreement left is a
/// client who joins DURING a wither — it starts at 0 while the others finish falling — and that is
/// self-healing within <see cref="WitherSeconds"/> and is the same class of skew the six already
/// have. ZERO WIRE BYTES, on the same argument as the rest of this file: the element board is
/// replicated and desync-checked by the game, and the clock is already shared.</para>
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
/// <item>THERE IS NO LONGER A SECOND KIND. The waning BREATH used to be an ABSOLUTE-PHASE function
/// of the clock (<c>sin(2π·clock/period)</c>), the offset did NOT cancel there, and that was the
/// whole reason the shared clock was used. With the breath deleted every remaining use of the clock
/// in this file is a DIFFERENCE (<c>clock − Since[i]</c>, <c>clock − _growSince</c>), so the offset
/// cancels everywhere and the published channel no longer has a phase for two clients to disagree
/// about at all. The shared clock stays, because the ramp anchors must come from the same monotone
/// base as the evaluation — but the failure mode this item was written to prevent is now structurally
/// impossible rather than merely avoided.</item>
/// </list></para>
///
/// <para><b>THE DEGRADATION, STATED RATHER THAN HIDDEN.</b> The environment clock is only actually
/// SHARED while the style is <c>Cellar</c> or <c>SwampNight</c>: <c>SkyAlternative.WireStyleCode</c>
/// (SkyAlternative.cs:1785) reports 0 for <c>Default</c>, for <c>OffBlack</c> and under mixed
/// reality, and with nothing to agree about the offset stays 0 — so on those styles
/// <c>EnvClockSeconds</c> is just <c>Time.timeSinceLevelLoad</c>, a per-client clock. THAT USED TO
/// COST SOMETHING AND NO LONGER DOES: while the plateau breathed, its phase was per-client on those
/// styles, and since this channel deliberately KEEPS SENSING UNDER MIXED REALITY (see below) two
/// players in passthrough watched the same waning element breathe out of phase — a 1:1 breach with
/// no wire message that could have fixed it. With the breath gone the six carry no phase at all, and
/// what is left costs nothing on those styles either, because it is the same clock the
/// rest of the environment already runs on: whatever the art lane animates will be exactly as
/// shared as the candle flicker and the rat beside it, no more and no less.</para>
///
/// <para><b>SCOPE AND COST.</b> Ticked from <see cref="SkyAlternative.Tick"/> — the environment
/// driver, and the one entry point that already sees every relevant state: it runs once per frame
/// under <c>MixedReality.Tick</c>'s MR-OFF branch (MixedReality.cs:664), and the MR-ON branch calls
/// <c>SkyAlternative.StandDown()</c> (MixedReality.cs:672) which stands this down too. Sitting here
/// rather than in <c>VRRigDriver._tailSteps</c> is deliberate: that array is a LOCKED frame order
/// (.planning/refactor/FRAME-ORDER.lock, <c>VRRigDriver._tailSteps</c>) whose last step must stay
/// last, and this feature has no ordering relationship with any camera-state step in it. Gating is
/// scenario-scoped on <c>Events.VRModeStateMachine.ScenarioBoardExists</c>, the same condition the
/// environment itself uses (SkyAlternative.cs:767).</para>
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide game state, bit-identical on every client and
/// desync-checked by the game itself, ZERO wire. Same classification and same reasoning as
/// <c>Net/RemoteElementStrip</c>. The DEBUG TEST OVERRIDE laid over it is also GLOBAL and IS on the
/// wire since 2026-08-15 (extension record 32); the two settings stay LOCAL and are never
/// transmitted or overridden. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class ElementMood
{
    // ---- the contract, as identifiers -----------------------------------------------------------

    /// <summary>Global <c>float4(Fire, Ice, Air, Earth)</c>. Quote this name, not a literal.</summary>
    internal const string ElemAName = "_GhvrElemA";

    /// <summary>Global <c>float4(Light, Dark, Master, Peak)</c>. Quote this name, not a literal.</summary>
    internal const string ElemBName = "_GhvrElemB";

    /// <summary>Global <c>float</c> — Earth's grow-in, 0..1. Quote this name, not a literal.</summary>
    internal const string ElemGrowName = "_GhvrElemGrow";

    private static readonly int ElemAId = Shader.PropertyToID(ElemAName);
    private static readonly int ElemBId = Shader.PropertyToID(ElemBName);
    private static readonly int ElemGrowId = Shader.PropertyToID(ElemGrowName);

    // ---- the curve, in numbers ------------------------------------------------------------------

    /// <summary>How long a transition takes, in shared-clock seconds. One second: long enough that
    /// the ~10 ms detection skew between two clients is a few percent of it, short enough that the
    /// environment has visibly answered before the player has finished reading the HUD chip.</summary>
    private const float RampSeconds = 1.0f;

    /// <summary>Waning sits at this, well under Strong's 1.0, AND IT DOES NOT MOVE — the gap is the
    /// whole READING, and since 2026-09-06 it is the only difference a half-active element is allowed
    /// to have (see NO ENVIRONMENT EFFECT MAY BLINK in the class doc). A glance must separate
    /// "charged" from "about to go", and it does that on the number, never on a motion.</summary>
    private const float WaningPlateau = 0.40f;

    /// <summary>The span the steadiness probe looks forward over, in shared-clock seconds. It is the
    /// PERIOD OF THE BREATH THIS RULING DELETED, kept as a live number with exactly one consumer —
    /// <see cref="TimeDrift"/> — rather than as a comment, because its whole job is to be the window
    /// in which a re-introduced 2.4 s oscillation would show up as a non-zero drift in the hardware
    /// line. Anything periodic at or under this period cannot hide inside it.</summary>
    private const float ProbeSpanSeconds = 2.4f;

    /// <summary>How long the growth channel takes to fall to 0 once Earth is Inert, in shared-clock
    /// seconds. It is deliberately NOT <see cref="RampSeconds"/>: the grow-in may answer at the speed
    /// of the HUD because a player who has just infused Earth is looking for an answer, but a plant
    /// going away at that speed is the "pops away" fault, which reads as a glitch rather than as an
    /// end. It is also a ONE-SHOT and not a cycle: it runs once, reaches 0 and stops, which is what
    /// keeps it on the right side of the no-blinking ruling.</summary>
    private const float WitherSeconds = 9.0f;

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
            + "freshly infused element reads as fully charged and a waning one is visibly weaker — "
            + "fewer frost patches, dimmer light, less of everything — so you can see it is about to "
            + "go out without reading the element strip. Nothing blinks or pulses at any strength: "
            + "the response fades in and out over about a second when the board changes and then "
            + "holds perfectly still. OFF removes the reaction "
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

    // ---- TEST TRIGGER — the local debug override -------------------------------------------------
    //
    // USER REQUEST (hardware, ModBuild 141, verbatim): "ich brauche zum Testen im Erweitert Menu die
    // möglichkeit die Elemente und Easter eggs einzeln auf Knopfdruck auslösen zu können." A tester
    // cannot judge six element responses by waiting for a scenario to infuse them in the right order,
    // so the Erweitert menu gets a page of buttons (WorldUI/VROptionsTab.9.TestTriggers.cs) that each
    // pretend an element is up. (The original version of this aid pretended ONE element was up for
    // eight seconds; the follow-up request below turned both of those numbers into "as many as you
    // press, for as long as you leave them pressed".)
    //
    // WHY THE OVERRIDE SITS HERE, ON THE PUBLISHED SIDE, AND NOT ON THE GAME'S BOARD.
    // ElementInfusionBoardManager.SetElementInstantly(EElement, EColumn)
    // (ScenarioRuleLibrary/ElementInfusionBoardManager.cs:264) is the obvious call and it is the WRONG
    // one: the element board is a MULTIPLAYER DESYNC INVARIANT. ScenarioState.ElementColumn is
    // serialized (ScenarioState.cs:110/704/846), restored (:1374) and COMPARED between clients every
    // round with its own mismatch codes 117/118 in CompareStates(…, isMPCompare: true) (:1992-2032),
    // run from Choreographer.StartMPEndOfRoundCompare (GH.Runtime/Choreographer.cs:14320-14340). A
    // test button that wrote the board would therefore desync the session at the next end-of-round
    // compare — the one failure mode this project's standing "everything is synchronised 1:1" rule
    // exists to prevent. The override instead lives between SENSING and PUBLISHING: the game's board
    // is read exactly as before and never written, and only the number this file hands to the shaders
    // is substituted. Nothing is game state and every client's copy of the game's board is
    // bit-identical throughout.
    //
    // WHAT DID CHANGE, 2026-08-15: THE OVERRIDE ITSELF IS NOW SYNCHRONISED. The sentence that closed
    // the paragraph above used to read "Nothing goes on the wire ... the only thing that differs is
    // what THIS headset draws", and the user has ruled otherwise (verbatim): "Auch wenn jemand im
    // Debugmenu ein Event startet sollte dies auch von ALLEN im Multiplayer sichtbar sein statt nur
    // lokal, also synchronisiert werden."
    //
    // NOTHING ABOUT THE PLACEMENT ARGUMENT ABOVE IS WEAKENED BY THAT, which is the whole reason it
    // cost this file no code at all. What travels is a pair of six-bit MASKS in extension record 32
    // (Net/RemoteTestTriggers) — "these elements are being pretended into these columns" — and a
    // receiver applies them through the very same Force/ClearForce buttons the tester presses. So the
    // game's ElementInfusionBoardManager is still read and never written, on every client, however
    // many elements are latched; the end-of-round desync compare (codes 117/118) still has nothing to
    // disagree about; and the only thing that is now shared is the LIE, which is what was asked for.
    //
    // NO ANCHOR IS SENT WITH THE MASKS, deliberately. An element force is a STATE: each client's own
    // poll below sees the column change and anchors its own 1 s ramp then, exactly as it does for a
    // real infusion, leaving the same bounded detection skew a real infusion already has. There is
    // no term left with a phase to get wrong: the waning BREATH was deleted on 2026-09-06 (see the
    // class doc — NO ENVIRONMENT EFFECT MAY BLINK), so every column now publishes a constant and
    // the only per-client thing is the 1 s ramp each one anchors for itself.
    //
    // LOCAL SETTINGS HAVE PRECEDENCE (user ruling, same day): a peer's force is refused at the door
    // by Net/RemoteTestTriggers.Drive unless the receiver's own environment dial matches the sender's
    // AND their EnvironmentResponse switch is on, so a remote force can never become a latch here and
    // can never switch this feature on for someone who switched it off. The `!EnvironmentResponse.Value
    // && !Forcing` gate in Tick therefore still means what it always meant — a LOCAL press overrides
    // the LOCAL switch, by the tester's own hand. ResponseStrength is NOT treated as a permission: it
    // is a magnitude the player chose and it scales a synced force exactly as it scales a real
    // infusion, which is the same reason Force() already refuses to override it.
    //
    // FOLLOW-UP USER REQUEST (hardware, verbatim): "In der Triggertestview möchte ich wenn ich etwas
    // triggere das es dauerhaft an ist und mit erneutem toggle wieder ausgemacht wird. So kann ich
    // die Mischungen besser testen." One press LATCHES the element on and it stays on until the same
    // button is pressed again. That one sentence reverses BOTH of the rulings this block used to
    // carry, and it reverses them for a reason rather than a preference:
    //
    //   * "dauerhaft an" ends the timed hold. The old expiry was eight seconds — sized so a tester
    //     could see the Waning plateau breathe one and a half times, back when it breathed at all —
    //     which is right for "show me
    //     this once" and wrong for "leave it standing while I look at something else". Judging how
    //     two elements sit together cannot be done in eight seconds, and re-pressing a button every
    //     eight seconds is not a test, it is a metronome. There is now NO expiry at all: the only
    //     things that end a latch are the same button, the stop row, and a stand-down (scenario end,
    //     VR teardown, mixed reality — all of which take the environment with them anyway).
    //   * "die Mischungen" ends the single slot. The earlier ruling here was "REJECTED: forcing
    //     several elements at once — the user asked to trigger them 'einzeln', and six overlapping
    //     responses are a lightshow rather than a test". That was written for a tester judging ONE
    //     response in isolation; the user has now asked for precisely the case it excluded, and a
    //     MIXTURE cannot be built out of a channel that holds one element. So the latch is
    //     PER-ELEMENT: any subset of the six can stand at once, each released by its own button.
    //
    // WHAT DOES NOT CHANGE is the placement, and it is the load-bearing part: the override still sits
    // between SENSING and PUBLISHING (see the paragraph above), so the game's element board is still
    // read and never written however many elements are latched and however long they stand.
    //
    // REJECTED: zeroing the elements that are NOT latched. They keep their real sensed state, so the
    // override adds a lie only where the tester asked for one. In practice the rest are Inert outside
    // combat anyway, and if they are not, the tester is seeing the truth beside the forcing.
    //
    // REJECTED: a single latch that simply never expires. It would have satisfied the first half of
    // the sentence and failed the second — "so kann ich die Mischungen besser testen" is the REASON
    // the user gives for wanting the latch at all, and a mixture needs at least two.

    /// <summary>Whether element <c>i</c> is currently latched by a test trigger. Six independent
    /// flags rather than one index, because the request is explicitly about MIXTURES.</summary>
    private static readonly bool[] ForceLatched = new bool[Count];

    /// <summary>The state each latched element is being pretended into (Strong or Waning). Only
    /// meaningful where <see cref="ForceLatched"/> is true — a separate flag rather than using
    /// <c>Inert</c> as the "not latched" sentinel, so that "latched" and "which column" stay two
    /// independent facts and a future Inert button would need no rework.</summary>
    private static readonly ElementInfusionBoardManager.EColumn[] ForceColumn =
        new ElementInfusionBoardManager.EColumn[Count];

    /// <summary>Shared-clock time each latch was pressed. DIAGNOSTICS ONLY now that nothing expires:
    /// it is what lets a log reader say "this one has been standing since 41 s" while reading a
    /// mixture, and it is deliberately not used by any decision below.</summary>
    private static readonly float[] ForceSince = new float[Count];

    /// <summary>How many of the six are latched. Kept as a counter rather than scanned, because
    /// <see cref="Forcing"/> is read on the off path of <see cref="Tick"/> every frame and that path
    /// is the one this file promises costs nothing.</summary>
    private static int _forceCount;

    /// <summary>
    /// Whether a force could do anything at all right now. It deliberately does NOT include
    /// <see cref="EnvironmentResponse"/>: the switch is a preference the button may override for as
    /// long as its latch stands (see <see cref="Force"/>), while these two are "there is no
    /// environment and no board" — nothing to force, and nothing a button can conjure.
    /// </summary>
    internal static bool ForceReady => VRSession.IsRunning && Events.VRModeStateMachine.ScenarioBoardExists;

    /// <summary>True while ANY element is latched. It is what keeps <see cref="Tick"/> alive past the
    /// player's own off-switch, and with the latch now unbounded in time that override lasts exactly
    /// as long as the latch does — which is the point of the request.</summary>
    internal static bool Forcing => _forceCount > 0;

    /// <summary>
    /// Is this exact button lit? The test page asks once per row after every press, so a tester
    /// building a mixture can see what is standing without reading the log.
    /// </summary>
    /// <param name="element">Element index, 0..5 in the game's own EElement order.</param>
    /// <param name="waning">Which of the element's two rows is asking: true = Waning, false = Strong.
    /// The two rows latch the SAME element into DIFFERENT columns, so exactly one of them can be lit
    /// at a time and neither may claim the other's state.</param>
    internal static bool IsForced(int element, bool waning)
    {
        if (element < 0 || element >= Count || !ForceLatched[element])
            return false;

        return ForceColumn[element] == (waning
                                            ? ElementInfusionBoardManager.EColumn.Waning
                                            : ElementInfusionBoardManager.EColumn.Strong);
    }

    /// <summary>
    /// TOGGLE one element's test latch, and hold it until it is pressed again. Returns true when the
    /// press changed something; false — and says so in the log — when there was nothing to force, so
    /// a press outside a scenario is inert rather than an exception.
    ///
    /// <para><b>THE THREE CASES, and the middle one is the one worth stating.</b> Each element has TWO
    /// rows on the test page (Strong and Waning), so a press means one of three things:</para>
    /// <list type="number">
    /// <item>the element is not latched → LATCH it into the pressed column;</item>
    /// <item>it is latched into the OTHER column → SWITCH it, staying latched. Reading this as
    /// "release, then press the other one" would be two presses' worth of work for one press, and the
    /// tester who presses Waning while Strong stands has said what they want unambiguously;</item>
    /// <item>it is latched into the SAME column → RELEASE it. That is "mit erneutem toggle wieder
    /// ausgemacht" verbatim, and it is why the rule is per-BUTTON rather than per-element: a latch
    /// released by a button the tester did not press would be a button that lies.</item>
    /// </list>
    ///
    /// <para>IT OVERRIDES THE FEATURE'S OWN ON/OFF SWITCH, deliberately, and the page says so in
    /// German. The alternative (refuse while <see cref="EnvironmentResponse"/> is off) makes a button
    /// that does nothing and looks broken — indistinguishable from the very fault this aid exists to
    /// find, and with the switch two menu pages away the tester would have no way to tell which it
    /// was. What HAS changed with the latch is the DURATION of that override: it used to be bounded by
    /// eight seconds and is now bounded only by the tester letting go of it. It is still local, it
    /// still NEVER writes the setting — the toggle still shows the player's own choice — and the
    /// moment the last latch is released that choice stands again on the very next tick (Tick's off
    /// path is guarded by <see cref="Forcing"/>, so there is no frame in between). The STRENGTH dial
    /// is NOT overridden — that one is a magnitude the tester chose, and showing them an intensity
    /// they did not configure would be a different lie; if it is at 0 the log says the press will be
    /// invisible and why.</para>
    /// </summary>
    /// <param name="element">Element index, 0..5 in the game's own EElement order.</param>
    /// <param name="waning">true = the Waning plateau, false = Strong. Both are still numbers now —
    /// see NO ENVIRONMENT EFFECT MAY BLINK in the class doc.</param>
    internal static bool Force(int element, bool waning)
    {
        if (!_bound)
            Rig.RenderQuality.Bind();

        if (element < 0 || element >= Count)
            return false;

        var name = (ElementInfusionBoardManager.EElement)element;
        ElementInfusionBoardManager.EColumn wanted = waning
            ? ElementInfusionBoardManager.EColumn.Waning
            : ElementInfusionBoardManager.EColumn.Strong;

        // PRESSED AGAIN = OFF. Checked BEFORE ForceReady on purpose: releasing a latch must work in
        // every state the latch can survive into, and a tester whose scenario has ended between the
        // two presses would otherwise be told "there is no scenario board" by a button whose whole
        // job at that moment is to stop doing something.
        if (ForceLatched[element] && ForceColumn[element] == wanted)
        {
            ClearForce(element, "the tester pressed the same test trigger again");
            return true;
        }

        if (!ForceReady)
        {
            VRLog.Info("Core", $"ELEMENT TEST TRIGGER ignored — {name} was not latched because "
                               + (VRSession.IsRunning ? "there is no scenario board" : "VR is not running")
                               + ". The element channel is not live outside a scenario, so there is no "
                               + "environment to answer and nothing to see; the button is inert here on "
                               + "purpose rather than arming an override that would fire later.");
            return false;
        }

        bool switched = ForceLatched[element];
        ElementInfusionBoardManager.EColumn before = ForceColumn[element];
        if (!switched)
            _forceCount++;
        ForceLatched[element] = true;
        ForceColumn[element] = wanted;
        ForceSince[element] = SkyAlternative.EnvClockSeconds;

        float master = Mathf.Max(0f, ResponseStrength.Value);
        VRLog.Info("Core", $"ELEMENT TEST TRIGGER: {name} latched to {wanted}"
                           + (switched
                                  ? $", switched from {before} without leaving the latch"
                                  : string.Empty)
                           + $" at shared clock {ForceSince[element]:F2}s and held INDEFINITELY — it "
                           + "ends when the same button is pressed again, when the stop row is "
                           + "pressed, or when the channel stands down. Heading for "
                           + $"{TargetLabel(wanted)} over the usual {RampSeconds:F1}s ramp. "
                           + $"{LatchedList()}. Master factor {master:F2}"
                           + (EnvironmentResponse.Value
                                  ? " and the setting is on"
                                  : " — the 'EnvironmentResponse' setting is OFF and this latch "
                                    + "overrides it for as long as it stands; the setting itself is "
                                    + "untouched and takes over again the moment the last latch goes")
                           + (master <= 0f
                                  ? ". NOTE: the strength dial is at 0, so every element effect "
                                    + "multiplies by 0 and this latch will be INVISIBLE — that is the "
                                    + "dial, not a fault"
                                  : string.Empty)
                           + ". SYNCHRONISED SINCE 2026-08-15: the latch set goes on the wire as "
                           + "extension record 32 whenever THIS client owns the override, so every "
                           + "player who has the same environment selected and 'EnvironmentResponse' "
                           + "on sees the same mixture at the same moment. It is still NOT game state: "
                           + "the game's element board (ElementInfusionBoardManager) is READ and never "
                           + "written on any client — however many elements are latched and however "
                           + "long they stand — so the end-of-round desync compare "
                           + "(ScenarioState.CompareStates codes 117/118) has nothing to disagree "
                           + "about. What travels is six bits per column, never the board and never a "
                           + "ramp; each client anchors its own. A peer whose environment differs or "
                           + "whose switch is off simply does not see it and is NOT desynced.");
        return true;
    }

    /// <summary>
    /// Release ONE element's latch. Idempotent, and it re-anchors nothing by hand: the next tick reads
    /// the real column, sees it differ from the latched one, and ramps back from wherever the eye last
    /// saw the intensity — the same path a real Strong → Inert transition takes.
    /// </summary>
    internal static void ClearForce(int element, string why)
    {
        if (element < 0 || element >= Count || !ForceLatched[element])
            return;

        var name = (ElementInfusionBoardManager.EElement)element;
        ElementInfusionBoardManager.EColumn column = ForceColumn[element];
        float since = ForceSince[element];
        ForceLatched[element] = false;
        ForceColumn[element] = ElementInfusionBoardManager.EColumn.Inert;
        ForceSince[element] = 0f;
        _forceCount = Mathf.Max(0, _forceCount - 1);

        VRLog.Info("Core", $"ELEMENT TEST TRIGGER off — {name} is no longer latched to {column} ({why}); "
                           + $"it had stood since shared clock {since:F2}s. The real sensed state takes "
                           + $"over on the next tick and ramps in over {RampSeconds:F1}s. "
                           + (_forceCount > 0
                                  ? LatchedList() + " — the rest of the mixture is untouched."
                                  : "Nothing is latched any more, so the player's own "
                                    + "'EnvironmentResponse' setting decides again from the next "
                                    + "tick."));
    }

    /// <summary>
    /// Release EVERY latch — the stop row, and every stand-down route (see <see cref="StandDown"/>,
    /// which calls this before its own idempotence guard so that no latch can survive a teardown).
    /// Idempotent, and silent when there was nothing latched: the one press that must always leave a
    /// trace is the stop row's, and that row writes its own line whether or not anything was standing.
    /// </summary>
    internal static void ClearForce(string why)
    {
        // The counter first, and not merely for tidiness: StandDown calls this on EVERY frame the
        // feature spends switched off, so the "costs nothing when off" promise this file makes would
        // otherwise have quietly become six array reads per frame instead of one compare.
        if (_forceCount <= 0)
            return;

        for (int i = 0; i < Count; i++)
            ClearForce(i, why);
    }

    /// <summary>The latched elements as one readable clause, for the log lines that have to let a
    /// reader see a MIXTURE rather than deduce it from a stack of earlier presses.</summary>
    private static string LatchedList()
    {
        if (_forceCount <= 0)
            return "Nothing is latched";

        var sb = new StringBuilder(96);
        sb.Append("Latched now: ");
        bool first = true;
        for (int i = 0; i < Count; i++)
        {
            if (!ForceLatched[i])
                continue;
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append((ElementInfusionBoardManager.EElement)i).Append('=').Append(ForceColumn[i]);
        }
        return sb.ToString();
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

    /// <summary>Earth's published grow-in, 0..1 — see THE GROWTH CHANNEL in the class doc. Four
    /// fields rather than an array because there is exactly one of them: only Earth grows anything,
    /// and a six-wide channel would invite the next reader to look for the other five.</summary>
    private static float _grow;

    /// <summary>The grow-in at the moment of the last presence change — the growth ramp's FROM.</summary>
    private static float _growFrom;

    /// <summary>Shared-clock time of the last presence change — the growth ramp's anchor.</summary>
    private static float _growSince;

    /// <summary>Whether Earth was up AT ALL (Strong or Waning) at the last tick. This is the whole
    /// state the growth curve is a function of, and keeping it as its own bool rather than
    /// re-deriving "not Inert" at three places is what makes the edge unambiguous.</summary>
    private static bool _growPresent;

    /// <summary>Earth's column at the last GROWTH LOG. Its own field, and not <c>Column[Earth]</c>,
    /// because the line it gates has to fire on the transition the presence bool deliberately does
    /// NOT see: Strong to Waning is the state the complaint was reported from, and it is exactly the
    /// transition at which the grow-in must be shown NOT moving. -1 = nothing logged yet.</summary>
    private static int _growLoggedColumn = -1;

    /// <summary>Base-3 signature of the six columns; the edge detector, exactly as
    /// <c>RemoteElementStrip.Refresh</c> uses it. -1 = nothing observed yet.</summary>
    private static int _signature = -1;

    /// <summary>The signature the last SETTLED line was printed for — a second detector beside
    /// <see cref="_signature"/>, and it has to be its own field: the edge line fires the instant the
    /// board moves, when every element is mid-ramp and therefore legitimately time-varying, which is
    /// the one state in which the steadiness reading would be meaningless. -1 = nothing yet.</summary>
    private static int _steadySignature = -1;

    /// <summary>Six bits: element <c>i</c> has been observed in the Waning column at least once
    /// while the channel was live. THE FALSIFIER FIELD — see <see cref="LogSteady"/>. It survives a
    /// stand-down on purpose, because the question it answers ("was the half state ever reached in
    /// this session?") is about the session and not about one scenario.</summary>
    private static int _halfSeenMask;

    /// <summary>How many settled lines have been printed. Change-gated lines look DEAD when their
    /// reason is constant, so the line carries its own ordinal and a reader can tell "nothing has
    /// changed since #4" from "the instrument stopped".</summary>
    private static int _steadyLines;

    /// <summary>The last vectors actually written, so a write happens only on a real change.</summary>
    private static Vector4 _lastA;
    private static Vector4 _lastB;
    private static float _lastGrow;

    /// <summary>The channel is live (gates passed) and is publishing a mood.</summary>
    private static bool _live;

    /// <summary>Zeros have been published at least once. Starts false so the very first tick — even
    /// one that lands straight in the off path — writes the master 0 the art multiplies by, rather
    /// than trusting that nothing else ever wrote these globals.</summary>
    private static bool _zeroed;

    /// <summary>
    /// One element's live intensity AS THE SHADERS SEE IT: the smoothed 0..1 value already
    /// multiplied by the published master, so a caller needs no "is the feature on" branch — with
    /// the feature off, or outside a scenario, every element reads 0 and every expression built on
    /// it collapses.
    ///
    /// <para>WHY THIS ACCESSOR EXISTS AT ALL, when the class doc says the channel IS the two shader
    /// globals. Because a C# consumer appeared that is not a material: <see cref="Haunt.Resolve"/>
    /// has to reproduce the GPU's haunt schedule bit-for-bit, and that schedule bends on Dark, Light
    /// and Ice (<c>EnvHaunt.cginc</c>'s <c>GhvrHauntElems()</c>). Its C# mirror must read the SAME
    /// numbers from the SAME place — <c>Shader.GetGlobalVector</c> would read them back out of the
    /// graphics device a frame late and is not free, and a second smoothing pass would be a second
    /// source of truth. So the one writer hands its own values out directly.</para>
    ///
    /// <para>MULTIPLIED BY MASTER, NOT RAW, because that is what <c>GhvrElems()</c> returns on the
    /// GPU side. Handing out the raw value would make every caller responsible for remembering the
    /// multiply, and the first one to forget would have a schedule that disagreed with the picture.</para>
    /// </summary>
    /// <param name="element">Element index, 0..5 in the game's own EElement order
    /// (Fire, Ice, Air, Earth, Light, Dark). Anything else reads 0.</param>
    internal static float Live(int element)
    {
        if (element < 0 || element >= Count || !_live)
            return 0f;
        return Value[element] * Mathf.Max(0f, ResponseStrength.Value);
    }

    // ---- the curve, as one function ---------------------------------------------------------------

    /// <summary>
    /// WHAT A COLUMN IS WORTH. A PURE FUNCTION OF THE COLUMN AND OF NOTHING ELSE — no clock, no
    /// phase, no history — and that signature is the fix of 2026-09-06 rather than an implementation
    /// detail. Strong is 1.00, Waning is <see cref="WaningPlateau"/>, Inert is 0.00, for ALL SIX
    /// ELEMENTS, forever.
    ///
    /// <para>Waning used to return <c>0.40 + 0.12*sin(2*pi*clock/2.4)</c>. USER, on hardware
    /// (verbatim): "Auch das Eis blinkt in nem Loop wenn es nur zur Hälfte aktiv ist. ... Ich will so
    /// ein Blinken generell nicht. Die Umgebungseffekte sollen genau wie beim 'vollen' sein - nur
    /// weniger in der Anzahl oder weniger intensiv - aber niemals blinkend." A half-active element
    /// may differ from a full one in COUNT or in AMPLITUDE and never in TIME, and this method is the
    /// one place the channel could have carried a time term at all.</para>
    /// </summary>
    private static float TargetFor(ElementInfusionBoardManager.EColumn column) => column switch
    {
        ElementInfusionBoardManager.EColumn.Strong => 1f,
        ElementInfusionBoardManager.EColumn.Waning => WaningPlateau,
        _ => 0f,
    };

    /// <summary>
    /// One element's published intensity at an ARBITRARY shared-clock instant. The per-frame
    /// smoothing calls it with `now`; <see cref="TimeDrift"/> calls it with `now + k` to measure
    /// whether the curve moves on its own.
    ///
    /// <para><b>ONE CODE PATH ON PURPOSE.</b> The instrument that proves "nothing here is
    /// time-varying" must evaluate the very function the shaders are fed, or it is an assertion
    /// wearing a measurement's clothes: a future edit that put a clock back into the target would
    /// then be invisible to the line whose whole job is to catch it. So there is exactly one
    /// evaluator and both callers use it.</para>
    ///
    /// <para>Closed form rather than a per-frame MoveTowards: the intensity is a pure function of
    /// (from, target, elapsed), so it is frame-rate independent by construction and two clients that
    /// anchored at the same shared-clock time agree exactly, with no accumulated integration error to
    /// drift apart.</para>
    /// </summary>
    private static float ValueAt(int i, float clock)
    {
        float t = RampSeconds > 0f ? Mathf.Clamp01((clock - Since[i]) / RampSeconds) : 1f;
        t = t * t * (3f - 2f * t);                     // smoothstep — no corner at either end
        return Mathf.Clamp01(Mathf.Lerp(From[i], TargetFor(Column[i]), t));
    }

    /// <summary>
    /// HOW MUCH THIS ELEMENT'S PUBLISHED VALUE WOULD MOVE OVER THE NEXT <see cref="ProbeSpanSeconds"/>
    /// SECONDS IF THE GAME'S BOARD NEVER CHANGED. This is the field that makes the ruling checkable
    /// from a Player.log instead of from someone staring at ice: 0.000 means the channel is a
    /// constant at this strength, i.e. nothing downstream can be periodic because of the element.
    ///
    /// <para>It is a MEASUREMENT and not a claim. It re-runs <see cref="ValueAt"/> — the shipped
    /// evaluator — at five instants a fifth of the deleted breath's period apart, which is a Nyquist
    /// margin of 2.5x against that exact waveform and against anything faster expressed at that
    /// amplitude. If a future edit puts any oscillation back into the target, this reads non-zero on
    /// the first settled line and names the element.</para>
    ///
    /// <para>THE ONE HONEST NON-ZERO is a transition still in flight: within <see cref="RampSeconds"/>
    /// of a column change the value is legitimately travelling, so the reading is the ramp's
    /// remaining distance. The caller only ever prints this from a SETTLED state (see
    /// <see cref="LogSteady"/>), so a non-zero reading on that line is a defect and nothing else.</para>
    /// </summary>
    private static float TimeDrift(int i, float clock)
    {
        float lo = ValueAt(i, clock);
        float hi = lo;
        for (int k = 1; k <= 5; k++)
        {
            float v = ValueAt(i, clock + ProbeSpanSeconds * (k / 5f));
            if (v < lo)
                lo = v;
            if (v > hi)
                hi = v;
        }
        return hi - lo;
    }

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
        //
        // …UNLESS A TEST LATCH IS STANDING. A press on the Erweitert page overrides the switch for as
        // long as the latch is held (the reasoning is at Force()), which is exactly one extra int
        // compare on the off path — a counter, not a scan of six flags, so the "costs nothing when
        // off" promise survives an unbounded hold as cheaply as it survived an eight-second one.
        if (!EnvironmentResponse.Value && !Forcing)
        {
            StandDown("the setting is off");
            return;
        }

        // A force may not outlive the thing it was drawn on — and it does not have to be dropped
        // here by hand, because StandDown drops it on EVERY route, teardown included.
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

        // THERE IS NO EXPIRY HERE ANY MORE, and the deletion is the request itself ("dauerhaft an").
        // What used to stand at this point was an eight-second hold plus a "the shared clock jumped
        // backwards" escape, and both are gone:
        //   * the hold, because a latch that ends by itself is not a latch;
        //   * the backwards-clock escape, because it was only ever protecting the SUBTRACTION
        //     `clock - _forceSince`, and no decision reads that difference now — the latch is a bool.
        //     A backwards jump still has to be handled for the RAMP, and it already is, per element,
        //     in the sense loop below (the `clock < Since[i]` re-anchor). Dropping the tester's
        //     latches because the clock changed owner would now be the bug rather than the fix.
        // The off-switch fallthrough that lived here is gone with it: nothing in this method can end
        // a latch any more, so there is no longer a frame in which the force disappears mid-tick and
        // the switch has to be re-consulted. Every route that DOES end one (the button, the stop row,
        // StandDown) either runs outside Tick or stands the channel down in the same breath.

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

            // THE OVERRIDE, and it is one line because it is placed where a lie costs the least: the
            // game's board has already been read (and never written), and everything downstream —
            // the edge detector, the ramp anchoring, the peak, the log — treats the forced column
            // exactly like a sensed one. So a latch ramps IN like a real infusion and,
            // when it is released, ramps OUT like a real one, with no second code path to keep in
            // step. THAT IS ALSO WHY MIXTURES NEEDED NO OTHER CHANGE: this loop already ran six
            // times and the peak is a max over all six — so six latched elements smooth and publish
            // exactly as six real infusions would. The only edit the request needed here was
            // `== _forceIndex` becoming a per-element flag.
            if (ForceLatched[i])
                column = ForceColumn[i];

            signature = signature * 3 + (int)column;

            // THE FALSIFIER'S BOOKKEEPING. A Waning column IS the "nur zur Hälfte aktiv" state the
            // ruling is about, so the census is taken here, where the column is known and before any
            // smoothing can round it away. Without it a clean hardware line would be unreadable: it
            // would say the same thing in a session that proved the fix and in a session that never
            // reached the half state at all.
            if (column == ElementInfusionBoardManager.EColumn.Waning)
                _halfSeenMask |= 1 << i;

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
        // THE LINE THAT USED TO SIT HERE WAS THE DEFECT, and it is worth naming rather than merely
        // deleting: `waning = WaningPlateau + WaningEbbAmplitude * sin(clock * 2pi / 2.4)`. It made
        // the PUBLISHED CHANNEL a function of the clock at exactly one strength — half — which is
        // the one difference the user has now ruled out. There is no target function of the clock
        // any more; see ValueAt.
        float peak = 0f;
        for (int i = 0; i < Count; i++)
        {
            float v = ValueAt(i, clock);
            Value[i] = v;
            if (v > peak)
                peak = v;
        }

        // ---- GROW -------------------------------------------------------------------------------
        // THE SECOND CURVE, and everything about it that matters is in THE GROWTH CHANNEL in the
        // class doc. In one line: it is a function of Earth's PRESENCE, never of Earth's strength,
        // because the art it drives is a geometry fold behind a threshold and a threshold swept by
        // the waning breath is grass that stands up and lies flat every 2.4 s.
        //
        // Earth by its ENUM rather than by 3: the index order is the game's own EElement order and
        // this is the one place in the file that depends on WHICH element it is.
        bool present = Column[(int)ElementInfusionBoardManager.EElement.Earth]
                       != ElementInfusionBoardManager.EColumn.Inert;
        if (present != _growPresent || !_live)
        {
            // Anchored at the CURRENT value, exactly as the six are: Earth re-infused three seconds
            // into a wither grows back from the grass that is still standing, not from bare ground.
            _growFrom = _live ? _grow : 0f;
            _growSince = clock;
            _growPresent = present;
        }
        else if (clock < _growSince)
        {
            // The shared clock jumped backwards — same re-anchor, same reason, as the sense loop.
            _growFrom = _grow;
            _growSince = clock;
        }

        // Two durations, one curve: fast up, slow down. Not two code paths — the closed form is the
        // same one the six use, so a client that anchored at the same shared-clock instant computes
        // the same number with no accumulated integration error.
        float growDur = present ? RampSeconds : WitherSeconds;
        float gt = growDur > 0f ? Mathf.Clamp01((clock - _growSince) / growDur) : 1f;
        gt = gt * gt * (3f - 2f * gt);
        _grow = Mathf.Clamp01(Mathf.Lerp(_growFrom, present ? 1f : 0f, gt));

        // THE ONE HARDWARE READING. Gated on EARTH'S COLUMN, not on the presence bool: the state the
        // complaint came from is Strong -> Waning, at which presence does not change and the grow-in
        // must be seen HOLDING. So the line fires on a few transitions per scenario, and each one
        // prints the published intensity beside the held grow-in.
        int earthColumn = (int)Column[(int)ElementInfusionBoardManager.EElement.Earth];
        if (earthColumn != _growLoggedColumn)
        {
            _growLoggedColumn = earthColumn;
            LogGrowth(clock);
        }

        // PEAK COVERS THE GROWTH TOO. During a wither the six can all be 0 while the grass is still
        // standing, and peak is what the bundle's `e.live` gate is built from — see the PEAK bullet
        // in the class doc for why a peak of 0 there would fold the grass in one frame.
        if (_grow > peak)
            peak = _grow;

        // ---- PUBLISH ----------------------------------------------------------------------------
        float master = Mathf.Max(0f, ResponseStrength.Value);
        var a = new Vector4(Value[0], Value[1], Value[2], Value[3]);
        var b = new Vector4(Value[4], Value[5], master, peak);

        Write(a, b, _grow);
        _live = true;
        _zeroed = false;

        // ---- DIAGNOSE ---------------------------------------------------------------------------
        if (changed && signature != _signature)
            LogEdge(signature, master, clock);
        _signature = signature;

        // THE SETTLED READING — the one line the no-blinking ruling is checked from. It fires ONE
        // ramp after each edge rather than on the edge itself, because on the edge every element is
        // legitimately travelling and a steadiness measurement taken there would read non-zero for a
        // correct build. Two lines per board change, a handful per scenario.
        //
        // Gated on the SIX only, deliberately: the growth channel's wither is nine seconds long, so
        // including it would suppress this line for nine seconds after every Earth expiry — and the
        // wither is a ONE-SHOT that the line reports rather than something the ruling forbids.
        if (signature != _steadySignature && Settled(clock))
        {
            _steadySignature = signature;
            LogSteady(clock, master);
        }
    }

    /// <summary>Has every one of the six finished its transition ramp at this instant? The settled
    /// state is the only one in which "does this value move on its own?" has a meaningful answer.</summary>
    private static bool Settled(float clock)
    {
        for (int i = 0; i < Count; i++)
        {
            if (clock - Since[i] < RampSeconds)
                return false;
        }
        return true;
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
    ///
    /// <para><b>A SYNCED RELEASE DOES NOT COME THROUGH HERE, and that was checked rather than
    /// assumed (2026-08-15).</b> When a peer drops the shared override — by pressing the row again,
    /// by the stop row, by leaving, or by their environment tearing down — the net layer releases
    /// this client's copy through <see cref="ClearForce(int,string)"/>, one element at a time. That
    /// path deliberately re-anchors nothing: the next tick reads the real column, sees it differ and
    /// RAMPS back over <see cref="RampSeconds"/>, exactly as a real Strong→Inert transition does. So
    /// a remote release fades and nobody's trees snap. The instant answer here stays reserved for the
    /// routes it was written for, all of which are LOCAL: this player's own off-switch, and the
    /// moments at which the environment itself is going away. Do not "unify" the two — an off-switch
    /// that takes a second to answer reads as broken, which is the defect this contract prevents.</para>
    /// </summary>
    /// <param name="why">Named in the one log line this emits, so a log reader can tell "the player
    /// turned it off" from "the scenario ended" without guessing.</param>
    internal static void StandDown(string why)
    {
        // BEFORE the idempotence guard, on purpose: every route that drops this channel — teardown
        // included (SkyAlternative.cs:889) — must also drop a test override, and the guard would
        // otherwise let one survive a stand-down that had already published its zeros.
        ClearForce(why);

        if (!_live && _zeroed)
            return;

        bool wasLive = _live;
        _live = false;
        _zeroed = true;
        _signature = -1;
        // ...and the settled line re-baselines with it, so the next live scenario states its
        // steadiness afresh instead of staying silent because it happens to open on the same board.
        // _halfSeenMask and _steadyLines are NOT cleared: they are the session's census, and a
        // falsifier that reset every time the player looked at the map would answer nothing.
        _steadySignature = -1;
        for (int i = 0; i < Count; i++)
        {
            Column[i] = ElementInfusionBoardManager.EColumn.Inert;
            Value[i] = 0f;
            From[i] = 0f;
            Since[i] = 0f;
        }

        // THE GRASS GOES WITH THEM, AND IT GOES INSTANTLY — no wither. The paragraph above states
        // why the drop is not ramped, and every word of it applies here: an off-switch that takes
        // nine seconds reads as broken, and the other routes through here (scenario end, mixed
        // reality, teardown) are moments at which the room the grass stands in is itself
        // disappearing. The wither is for the one case it was written for, which is Earth running
        // out WHILE the environment stands.
        _grow = 0f;
        _growFrom = 0f;
        _growSince = 0f;
        _growPresent = false;
        // ...and the grass line re-baselines on the next live scenario rather than staying silent
        // because the last one happened to end on the same column.
        _growLoggedColumn = -1;

        Write(Vector4.zero, Vector4.zero, 0f);

        if (wasLive)
            VRLog.Info("Core", $"ELEMENT MOOD off — {why}. {ElemAName}, {ElemBName} and " +
                               $"{ElemGrowName} published as zero, so the master factor every element " +
                               "effect multiplies by is 0, Earth's grow-in is 0 and nothing is left " +
                               "standing in the environment's materials.");
    }

    /// <summary>
    /// The ONE writer of <see cref="ElemAName"/>, <see cref="ElemBName"/> and
    /// <see cref="ElemGrowName"/> — the same discipline <c>SkyAlternative.ApplyTimeOfs</c> states for
    /// <c>_GhvrTimeOfs</c>, and for the same reason: they are globals, so a second writer anywhere
    /// would fight this one with no way to see it. Writes only what actually moved by more than
    /// <see cref="WriteEpsilon"/>.
    ///
    /// <para>The three are written by ONE method rather than two, and after 2026-09-05 that is a
    /// requirement rather than tidiness: a caller that published A and B and forgot the growth would
    /// leave a room's grass standing at the previous scenario's value with no log line and no
    /// visible fault anywhere near the cause.</para>
    /// </summary>
    private static void Write(Vector4 a, Vector4 b, float grow)
    {
        // A poisoned value must never reach a shader global — NaN propagates through every material
        // that multiplies by it and paints holes that look like a bundle fault, three lanes away
        // from here.
        if (!Finite(a) || !Finite(b) || float.IsNaN(grow) || float.IsInfinity(grow))
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
        if (Mathf.Abs(grow - _lastGrow) > WriteEpsilon)
        {
            _lastGrow = grow;
            Shader.SetGlobalFloat(ElemGrowId, grow);
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
    /// THE GRASS LINE, and the one instrument this round's fix can be judged from without a headset
    /// debugger. It is <c>Note</c> rather than <c>Info</c> on purpose: it exists to be read in a
    /// shipped build's Player.log, which is where the defect was reported from.
    ///
    /// <para>WHAT IT DECIDES. The complaint is that the grass loops while Earth is "nur halb aktiv",
    /// i.e. Waning. So the line prints, at every change of EARTH'S column: the column, the
    /// intensity that column produces, and the grow-in. The fix is confirmed by ONE relation across
    /// two consecutive lines — Earth going Strong -> Waning must change the intensity and must NOT
    /// change the grow-in, which must read 1.00 in both. A grow-in that tracks the intensity is the
    /// defect still standing; a grow-in strictly between 0 and 1 while Earth is up is a ramp caught
    /// mid-flight, which is only correct within a second of the transition above it.</para>
    /// </summary>
    private static void LogGrowth(float clock)
    {
        var column = Column[(int)ElementInfusionBoardManager.EElement.Earth];
        float earth = Value[(int)ElementInfusionBoardManager.EElement.Earth];
        // HW-VERIFY: the grow-in beside the intensity it must not follow. Two consecutive lines
        // across Strong -> Waning are the whole test.
        VRLog.Note("Core", "EARTH GROWTH: Earth is now " + column
                   + ", published intensity " + earth.ToString("F2")
                   + " which since 2026-09-06 is a CONSTANT at every column — 1.00 Strong, "
                   + WaningPlateau.ToString("F2") + " Waning, 0.00 Inert, no breath at any of them "
                   + "(see ELEMENT STEADY) — and grow-in "
                   + _grow.ToString("F2") + " heading for " + (_growPresent ? "1.00" : "0.00")
                   + ". THE GRASS FOLLOWS THE GROW-IN AND NOTHING ELSE: Strong and Waning are the "
                   + "same grow-in, so no change between the two can move a blade; it rises over "
                   + RampSeconds.ToString("F1") + "s once, holds while Earth stands, and withers "
                   + "over " + WitherSeconds.ToString("F1") + "s only when Earth goes Inert. "
                   + "Shared clock " + clock.ToString("F2") + "s, master "
                   + Mathf.Max(0f, ResponseStrength.Value).ToString("F2")
                   + ", zero wire: the element board is the game's own replicated state.");
    }

    /// <summary>
    /// THE STEADINESS LINE — the instrument the 2026-09-06 ruling is checked from, and the reason
    /// nobody has to sit and watch ice again.
    ///
    /// <para>USER, verbatim: "Auch das Eis blinkt in nem Loop wenn es nur zur Hälfte aktiv ist. ...
    /// Ich will so ein Blinken generell nicht. Die Umgebungseffekte sollen genau wie beim 'vollen'
    /// sein - nur weniger in der Anzahl oder weniger intensiv - aber niemals blinkend."</para>
    ///
    /// <para><b>WHAT IT PRINTS, per element:</b> the column (the state), the published strength, and
    /// DRIFT — how far that strength would move over the next <see cref="ProbeSpanSeconds"/> seconds
    /// if the game's board never changed. The third field is the one that decides the item, and it is
    /// measured rather than asserted: <see cref="TimeDrift"/> re-runs the shipped evaluator
    /// (<see cref="ValueAt"/>) at five future instants, so a clock put back into the curve by a
    /// future edit reads non-zero here and names the element. EVERY DRIFT MUST BE 0.000. The line is
    /// only ever emitted from a settled state (see <see cref="Settled"/>), so there is no honest
    /// non-zero reading on it.</para>
    ///
    /// <para><b>THE FALSIFIER, and it is a field and not a hope.</b> A green line means nothing at
    /// all if the half state was never reached — which is exactly how this defect survived: the user
    /// says he "hatte die 'Hälften' nie getestet". So the line also carries HALF-ACTIVE SO FAR, the
    /// census of which elements have been seen Waning this session. If it reads "none", the drift
    /// readings are all from Strong and Inert and the run proves NOTHING about the ruling — the
    /// tester has to put an element into Waning (the Erweitert page's Waning row does it for any of
    /// the six) and look again. Silence is not success either: no line at all means the channel never
    /// went live — no scenario, mixed reality, or 'EnvironmentResponse' off — and again says nothing.</para>
    ///
    /// <para>Change-gated on the board signature, so it does not repeat while nothing moves; the
    /// ordinal is in the line because a change-gated instrument with a constant reason is
    /// indistinguishable from a dead one.</para>
    /// </summary>
    private static void LogSteady(float clock, float master)
    {
        _steadyLines++;

        var sb = new StringBuilder(480);
        sb.Append("ELEMENT STEADY #").Append(_steadyLines).Append(": ");
        float worst = 0f;
        int worstElement = -1;
        for (int i = 0; i < Count; i++)
        {
            float drift = TimeDrift(i, clock);
            if (drift > worst)
            {
                worst = drift;
                worstElement = i;
            }
            if (i > 0)
                sb.Append("  ");
            sb.Append((ElementInfusionBoardManager.EElement)i).Append('=').Append(Column[i])
              .Append(' ').Append(Value[i].ToString("F2"))
              .Append(" drift ").Append(drift.ToString("F3"));
        }

        sb.Append(" | ").Append(worst <= 0f
                                   ? "NO DRIVEN TERM IS TIME-VARYING AT ANY STRENGTH"
                                   : "TIME-VARYING AT "
                                     + (worstElement >= 0
                                            ? ((ElementInfusionBoardManager.EElement)worstElement).ToString()
                                            : "an element") + " BY " + worst.ToString("F3")
                                     + " — THE NO-BLINKING RULING IS BROKEN")
          .Append(", measured over the next ").Append(ProbeSpanSeconds.ToString("F1"))
          .Append("s by re-running the shipped curve at five future instants, from a settled board");

        // HALF-ACTIVE SO FAR — the falsifier. See the doc block: a clean drift reading taken in a
        // session that never reached Waning is not evidence, and this field is what stops it being
        // read as evidence.
        sb.Append(" | HALF-ACTIVE SO FAR: ");
        if (_halfSeenMask == 0)
        {
            sb.Append("none — no element has been Waning in this session yet, so the readings above "
                      + "are from Strong and Inert only and prove NOTHING about the half state. Put "
                      + "one element on the Erweitert page's Waning row and read the next line");
        }
        else
        {
            bool first = true;
            for (int i = 0; i < Count; i++)
            {
                if ((_halfSeenMask & (1 << i)) == 0)
                    continue;
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append((ElementInfusionBoardManager.EElement)i);
            }
            sb.Append(" have been Waning at least once, so a 0.000 above is real evidence for them");
        }

        sb.Append(" | THE RULE: a half-active element differs from a full one in AMPLITUDE only — "
                  + "Strong publishes 1.00 and Waning publishes ")
          .Append(WaningPlateau.ToString("F2"))
          .Append(", both perfectly still. Fewer or weaker, never in time. The only motion left in "
                  + "this channel is the one-shot ")
          .Append(RampSeconds.ToString("F1"))
          .Append("s transition ramp, which ends, and Earth's one-shot ")
          .Append(WitherSeconds.ToString("F1")).Append("s wither");

        sb.Append(" | grow-in ").Append(_grow.ToString("F2"))
          .Append(_growPresent
                      ? ", Earth is up and it is HELD: presence only, the same number for Strong and Waning"
                      : ", Earth inert: withering once to 0 and stopping");

        sb.Append(" | master ").Append(master.ToString("F2"))
          .Append(", shared clock ").Append(clock.ToString("F2")).Append("s, sig ")
          .Append(_steadySignature)
          .Append(". Change-gated on the board, so nothing after this line until the board moves.");

        // HW-VERIFY: per element the state, the strength, and whether ANY driven term is
        // time-varying at that strength. Every drift must read 0.000; HALF-ACTIVE SO FAR says
        // whether the reading is evidence at all.
        VRLog.Note("Core", sb.ToString());
    }

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
        // The latched elements are NAMED in the same line as the values, because the whole point of
        // the test page is that the reader of this log can tell "the environment answered the press"
        // from "the environment answered the game" without correlating two timestamps — and with
        // mixtures that now means naming ALL of them, since a line that named one of three would be
        // worse than one that named none.
        if (_forceCount > 0)
        {
            sb.Append(" | TEST TRIGGERS ACTIVE: ").Append(LatchedList())
              .Append(", each held until its own button is pressed again, no timer");
            for (int i = 0; i < Count; i++)
            {
                if (ForceLatched[i])
                    sb.Append("; ").Append((ElementInfusionBoardManager.EElement)i)
                      .Append(" since ").Append(ForceSince[i].ToString("F2")).Append('s');
            }
            sb.Append(" (synchronised as extension record 32 when this client owns the override; "
                      + "the game's board is still never written)");
        }

        sb.Append(" | master ").Append(master.ToString("F2"))
          // The setting can be OFF here while the channel publishes, because a test press overrides
          // it for its hold — so the line states which it is instead of asserting "on". No brackets
          // in either branch: scripts/patch-inventory.py's scanner walks string literals INTACT, so
          // a bracket that opens in one branch of a ternary and closes outside it is unbalanced to
          // the parser even though it is balanced at runtime.
          .Append(EnvironmentResponse.Value
                      ? ", setting on, strength "
                      : ", SETTING OFF and overridden by a test trigger, strength ")
          .Append(ResponseStrength.Value.ToString("F2"))
          .Append(", sig ").Append(signature)
          .Append(", shared clock ").Append(clock.ToString("F2")).Append('s');
        sb.Append(" | published as ").Append(ElemAName).Append("=(Fire,Ice,Air,Earth) ")
          .Append(ElemBName).Append("=(Light,Dark,Master,Peak); transitions ramp over ")
          .Append(RampSeconds.ToString("F1"))
          .Append("s and then HOLD — Strong is 1.00, Waning is ")
          .Append(WaningPlateau.ToString("F2"))
          .Append(" and Inert is 0.00, all three perfectly still. Nothing on this channel is a "
                  + "function of the clock at one strength and not at another; see ELEMENT STEADY "
                  + "for the measured drift.");
        // THE GROWTH CHANNEL, on the same line as the values it is derived from — because the whole
        // question a reader brings to this line after 2026-09-05 is "did the grass move when it
        // should not have?", and that is answered by seeing Earth's column, Earth's published
        // intensity and the grow-in side by side: the grow-in must read 1.00 for BOTH Strong and
        // Waning and must never take a value in between while Earth stands.
        sb.Append(" | ").Append(ElemGrowName).Append(" ").Append(_grow.ToString("F2"))
          .Append(_growPresent
                      ? " -> 1.00, Earth is up: Strong and Waning are the SAME grow-in, so no "
                        + "difference between the two can move a blade"
                      : " -> 0.00, Earth inert: withering")
          .Append(", rises over ").Append(RampSeconds.ToString("F1"))
          .Append("s and withers over ").Append(WitherSeconds.ToString("F1"))
          .Append("s, presence only, never strength");
        sb.Append(" | NO WIRE FOR THE BOARD, on purpose: the element board is scenario-wide state the game itself "
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
        // NO "+-" ANY MORE, and its absence is the fix in one glyph: the plus-minus was the breath,
        // and a Waning element now heads for one number and stays on it.
        ElementInfusionBoardManager.EColumn.Waning => WaningPlateau.ToString("F2"),
        _ => "0.00",
    };
}
