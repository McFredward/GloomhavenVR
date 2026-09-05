using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Element infusions ("Elemente") — GLOBAL
// =================================================================================================

/// <summary>
/// The element infusion board, drawn in the LEFT column below the objectives — the mirror of the
/// local board's docked <c>ElementBoardSurface</c>, seated at
/// <see cref="RemoteBoardLayout.ElementMount"/> (<c>PlayTray.ElementMountBase</c> plus the AUTHORED
/// per-board <c>ElementsOffset</c>, keyed by the peer's synced style). The seat used to drop that
/// per-board term, which on the Steel and Bronze boards buried the strip 40 mm behind where the
/// owner has it — part of defect (c) of the 1:1-parity round.
///
/// SOURCE (global, zero wire): <c>ElementInfusionBoardManager.ElementColumn(EElement)</c>, the exact
/// static the game's own <c>InfusionBoardUI.UpdateBoard</c> reads to decide each chip's state. The
/// infusion table is scenario-wide and identical on every client, so this needs no traffic and
/// reveals nothing.
///
/// PRESENTATION (user report 2026-08-04, element-darstellung.png "man sieht dort nur Quadrate"):
/// each chip is the game's OWN element disc — the exact strong/waning SPRITE the local docked
/// <c>InfusionBoardUI</c> shows, resolved from the singleton's authored per-element config
/// (<c>elementConfigs[i].strongIcon</c> / <c>.waningIcon</c>, the very sprites its Awake hands each
/// <c>InfusionElementUI</c>). Drawn by a <see cref="SpriteRenderer"/>, which honours atlas packing
/// (rect, rotation, tight meshes) that a hand-UV'd quad cannot, and routed through the shared
/// <see cref="CardFaceMipBake"/> cache so the disc samples a mipmapped copy of the game's mipless
/// UI atlas instead of shimmering (the proven card-face treatment).
///
/// WHY THE OLD LOOK WAS "blank colored squares", root cause read from source: the chips were bare
/// <c>BoardVisual.Quad</c>s wearing an UNTEXTURED <c>BoardVisual.Unlit(tint)</c> material — no
/// texture was ever assigned anywhere in this file, so a flat tinted rectangle was the DESIGNED
/// output, not a load failure. (No RenderTexture path is involved — irrelevant here, the icons are
/// plain sprites.) The tinted quad survives only as the FALLBACK for frames where the game's
/// infusion board singleton does not exist yet (menu / loading window), exactly the situations the
/// hardcoded colour table already covered — and, since the 2026-09 round, it is the LAST of three
/// rungs rather than the only alternative to the disc (see THE WHITE SQUARE below).
///
/// STATES mirror vanilla (<c>InfusionElementUI.SetState</c>): an INERT element is not drawn at all
/// (vanilla <c>SetActive(false)</c>s it), STRONG shows <c>strongIcon</c>, WANING shows
/// <c>waningIcon</c> — both untinted at full size, because the waning artwork itself conveys the
/// state (vanilla swaps the sprite; it does not dim or shrink). The dim+shrink treatment remains
/// only on the colour-quad fallback, where there is no artwork to do that job.
///
/// TRANSITIONS, AND THE HONEST LINE THROUGH THE MIDDLE OF THEM. The owner's docked board plays four
/// distinct animations, and they do NOT come from one place — which is the whole finding:
/// <list type="bullet">
/// <item>CREATED (<c>InfusionElementUI.ShowCreated</c>, played on <c>null|Inert -&gt; Strong</c> and
///   <c>null|Inert -&gt; Waning</c>) and its counterpart, an element dropping OUT of the board's
///   list. Both are a pure function of <c>ElementInfusionBoardManager.ElementColumn</c> — the exact
///   static <c>InfusionBoardUI.UpdateBoard</c> itself switches on (InfusionBoardUI.cs:196-236,
///   InfusionElementUI.cs:117-147) — which is scenario-wide, host-replicated and already the ONLY
///   input this strip reads. So they are DERIVABLE, they need no wire field, and
///   <see cref="TickTransitions"/> below plays them. <c>Strong -&gt; Waning</c> deliberately gets
///   none: vanilla's own branch there calls <c>StopAnimations()</c> and only swaps the sprite,
///   which this strip already did.</item>
/// <item>CREATING (the pending "this element is being infused" pulse, <c>ShowCreating</c> +
///   <c>creationImage</c>), RESERVED (which HIDES the chip outright,
///   <c>InfusionBoardUI.UpdateBoard</c> line "else if (IsElementReserved…) SetActive(false)") and
///   AVAILABLE (<c>SetAvailableElements</c> -&gt; <c>availableHighlight.enabled</c>). All three come
///   from <c>InfusionBoardUI</c>'s private lists <c>elementsInCreation</c> /
///   <c>elementsReserved</c> and from a <c>SetAvailableElements</c> call. THEY ARE ALSO DERIVABLE
///   AND THEY ARE NOW DRAWN — see the section below, which retires the wire debt this bullet used
///   to file.</item>
/// </list>
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
///  THE WHITE SQUARE (user report 2026-09, weisses_viereck.jpg: "Vom Remote-Board die Elemente sind
///  manchmal ein weisses Viereck")
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
/// The screenshot shows ONE plain white axis-aligned square in the peer board's left column, below
/// the objectives text. It is this strip and not the local converted <c>Panel_ElementBoard</c>: the
/// square measures 50 x 48 px against a 960 px-wide peer board, i.e. 0.032 m at <c>BoardW</c> 0.64
/// — <see cref="ChipSize"/> — and it sits at <c>PlayTray.ElementMountBase</c> minus half
/// <see cref="Width"/>, which is where this class puts chip zero. The local panel is docked to the
/// LOCAL tray, is budgeted 0.26 x 0.12 m, and its "keep the full root rect" fallback yields a
/// panel-shaped rect roughly 8x the observed area at a 2.2:1 aspect. Neither its place, its size
/// nor its shape is what the photo shows.
///
/// <b>WHY IT IS WHITE, WHICH IS THE PART THAT NAMES THE MECHANISM.</b> The tinted colour chip
/// CANNOT produce it: every entry in <see cref="Fallback"/> is a saturated element colour (Air is
/// a blue-grey 184,204,219 in 8-bit; Light a pale yellow 255,242,148), while the photographed
/// square is 255,255,255 across its interior with no colour cast and — the second tell — hard edges
/// with no bloom halo, so it is not a blown-out bright colour either. Pure white at full opacity
/// with a rectangular footprint is the signature of ONE thing in Unity: a <see cref="SpriteRenderer"/>
/// whose sprite has no live texture. Unity then binds the built-in white texture and draws the
/// sprite's quad. A sprite that has outlived its atlas is NOT null — fake-null covers a destroyed
/// <c>Sprite</c>, not a live one whose <c>texture</c> went away — so every <c>!= null</c> test in
/// this file passed it straight through to the renderer.
///
/// <b>WHAT WAS FIXED.</b> Four things, and the first three are the class, not the instance:
/// <list type="bullet">
/// <item><see cref="Usable"/> — a sprite reaches a renderer only if its TEXTURE is alive too. This
///   holds at the disc, at the availability ring and at the mip-baked replacement alike.</item>
/// <item>A three-rung DRAW LADDER in <see cref="Refresh"/>: the authored per-state disc; else the
///   game's own generic element glyph (<c>UIInfoTools.GetElementIcon</c>), dimmed and shrunk to
///   carry the state the missing artwork would have carried; else the flat colour chip, whose tint
///   <see cref="ColorFor"/> now refuses to let be white. No rung can draw a bare white square, and
///   every step down is logged at a tier the shipped build prints, naming the ELEMENT and WHICH OF
///   THE THREE STATES was missing.</item>
/// <item><see cref="_spriteSourceId"/> — the sprite set is re-resolved when the game builds a new
///   <c>InfusionBoardUI</c>, instead of latching for the life of the board. That is the mechanism
///   behind "manchmal": it takes a scenario change to strand the strip on a dead atlas.</item>
/// <item><see cref="TryResolveSprites"/> counted three populations as one number — it stored
///   strong, waning and creation, then reported success on <c>strongIcon</c> ALONE. Both hardware
///   logs therefore said "6/6 element disc sprites" while a null waning or creation icon sat in the
///   array with nothing to say so. The three are counted, named and reported separately now, and
///   the unusable ones are listed by element and state.</item>
/// </list>
///
/// The transition ramp itself borrows the mod's own card vocabulary — <c>VRCard.SmootherStep</c>
/// over <c>DockAppearSeconds</c> (0.28 s) / <c>DockVanishSeconds</c> (0.30 s) — because the owner's
/// <c>animatorCreated</c> is an authored <c>GUIAnimator</c> subclass whose curve lives in prefab
/// scene data this mod cannot read. Stated rather than hidden: the mirror says "an element just
/// came up / just went out" on the house curve, which is a truthful statement of an event the owner
/// really does see animate. It emits NO dust — the owner's is a UI animator, not a particle puff,
/// and manufacturing one would be inventing a picture rather than mirroring it.
///
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
///  IN CREATION, RESERVED, AVAILABLE — THE DEBT, AND WHY IT CLOSES FOR ZERO WIRE BYTES
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
/// The bullet above used to end "They are NOT replicated … the wire debt this lane files rather
/// than fakes." <b>That sentence was wrong</b>, and the correction is worth more than the wire
/// field would have been. It was reached by listing the writers of those three lists and stopping
/// there; the writers were right and the conclusion did not follow, because the game reaches every
/// one of them on a NON-controlling client too, through its own proxy paths.
///
/// <b>WHAT THE THREE STATES ARE, from source</b> (decompiled/GH.Runtime/InfusionBoardUI.cs,
/// .../InfusionElementUI.cs):
/// <list type="bullet">
/// <item><b>IN CREATION</b> — <c>UpdateBoard</c> appends its argument to <c>elementsInCreation</c>,
///   then DROPS from that list every element whose column has already gone non-inert, and builds
///   its draw list as "every non-inert element PLUS everything still in creation". So an element
///   that is Inert but in creation IS DRAWN, wearing <c>creationImage</c> — the authored
///   <c>creationIcon</c>, not the strong/waning disc (<c>SetState</c>, case <c>Inert</c>:
///   <c>elementImage.enabled = false; creationImage.enabled = isCreating</c>).</item>
/// <item><b>RESERVED</b> — <c>else if (IsElementReserved(key)) SetActive(false)</c>. A reserved
///   element is hidden outright, even when it is Strong, and hidden INSTANTLY: that branch never
///   reaches <c>SetState</c>, so no animator runs in either direction. (The <c>isReserved</c>
///   parameter <c>SetState</c> declares is dead in both senses — the only branch that calls
///   <c>SetState</c> is the one the reserved test already excluded, so it is always false, and the
///   method body never reads it.)</item>
/// <item><b>AVAILABLE</b> — <c>InfusionElementUI.availableHighlight.enabled</c>, an authored ring
///   Image behind the disc which vanilla's <c>Init</c> has already tinted per element with
///   <c>UIInfoTools.GetElementHighlightColor(type, availableHighlight.color.a)</c>. It rides a
///   SEPARATE entry point, <c>SetAvailableElements</c>, which <c>UpdateBoard</c> never calls — so
///   this is a THIRD independent mask, not a corollary of the other two, which is why an estimate
///   of "two masks" undercounts it. The only coupling is one-way: <c>SetState</c>'s Inert case
///   calls <c>SetAvailable(false)</c>.</item>
/// </list>
///
/// <b>WHY THEY ARE REPLICATED AFTER ALL — traced call site by call site.</b> There is exactly ONE
/// <c>InfusionBoardUI.Instance</c> per client and exactly one infusion board per scenario, so these
/// lists never describe "the local viewer": they describe WHOEVER IS CURRENTLY ACTING. And the game
/// drives them on every client:
/// <list type="bullet">
/// <item><c>UpdateBoard(list)</c> from the <c>ElementsInfused</c> choreographer message
///   (Choreographer.cs:10350) and from <c>CardsHandManager.UpdateElements</c> (:10186) — the
///   replicated message stream every client processes. <c>FullAbilityCard.UpdateElements</c> (:701,
///   the site the old bullet cited as :726) is reached from there, and only adds an infusion whose
///   ability <c>AbilityHasHappened</c>.</item>
/// <item><c>AugmentationHolder.ActiveAugment</c> / <c>DisactiveAugment</c> — reached on a
///   non-controlling client by <c>GameActionType.AugmentCardAbility</c> / <c>CancelAbilityAugment</c>
///   (34/35) → <c>CardsHandManager.ProxyToggleAugment</c> → <c>FullAbilityCard.ProxyToggleAugment</c>
///   → <c>UIUseAugmentationsBar.ProxyToggleAugment</c>, which pre-fills the picked elements with
///   <c>SetConsumes</c> and then calls the SAME <c>UIUseAugmentation.Select()</c> the owner's click
///   calls. Its <c>PickController.Pick()</c> returns true without opening a picker precisely because
///   the elements are already set, so <c>ActiveAugment</c> — and its <c>ReserveElements</c> — runs.
///   Augment GROUPS take the same route (36/37 → <c>ProxyToggleAugmentationGroup</c>).</item>
/// <item>Items: <c>UIUseItemsBar.ProxyUseItemBonus</c> calls <c>ElementInfusionBoardManager.Infuse</c>,
///   <c>InfusionBoardUI.UpdateBoard</c> and <c>ReserveElements</c> BY NAME on the receiving client.
///   Ability infusions: <c>UIUseAbilitiesBar.ProxyInfuseAbility</c> → <c>InfuseAbility</c> →
///   <c>Select</c> → <c>ConsumeOrInfuseIfPossible</c> → <c>Infuse</c>/<c>Consume</c>.</item>
/// <item><c>SetAvailableElements</c> has ONE call site in the whole game,
///   <c>UIUseAugmentationsBar.RefreshAvailableElements</c> (:126), whose input is
///   <c>augmentSlots.Keys</c> — the consume buttons the bar is showing for the current actor's
///   current ability, filtered by <c>CPhaseAction.ActionAugmentationsAvailableForCurrentAbility()</c>,
///   i.e. rule-library state. That bar is populated on every client, not just the acting one: the
///   game's own proxy path THROWS ("No such augment exists on the bar") if it is not, and
///   <c>CardsActionControlller.PickUnselectedInfusions</c> has an explicit
///   <c>!actorPicking.IsUnderMyControl</c> branch that goes on to show the bar anyway.</item>
/// </list>
/// This is the same finding <see cref="RemoteUseBarSymbols"/> already landed for the four use bars
/// (per-client singletons the game raises from replicated messages, with no <c>IsUnderMyControl</c>
/// test on the raise), arrived at again from the element board's end.
///
/// <b>AND RECORD 28 WOULD NOT HAVE CARRIED THEM ANYWAY.</b> <c>NetProtocol.ExtIdBoardTuning</c> is
/// the board TUNING record: <c>PresenceState.HasBoardTuning</c> is set only when the tuning pager
/// returns a non-empty page, which happens only while a dial differs from its shipped default, and
/// it is PAGED so one packet carries a slice. An untuned player — the common case — emits no
/// record 28 at all, so per-frame element state placed there would simply never arrive for most
/// players. If these ever DID need a wire they would need a record of their own.
///
/// <b>SECRECY: NOTHING TO GATE, checked rather than assumed.</b> The worry worth having was that
/// <c>elementsInCreation</c> might be fed from a player's pending card SELECTION and so leak part of
/// a card front the 1:1 rule's own exception protects. It is not: every writer of that list runs in
/// the action phase (see the trace above — <c>AbilityHasHappened</c>, committed <c>GameAction</c>s,
/// a post-resolution message). And the picture is not private in any case: the infusion board is a
/// single shared HUD widget vanilla already draws to every player, so this mirror shows a viewer
/// nothing their own flat UI is not showing them. <see cref="RevealGate"/> is therefore deliberately
/// NOT consulted here — a gate that can only ever be open would be a second secrecy policy with no
/// rule behind it.
///
/// <b>REJECTED: routing the real <c>InfusionBoardUI</c> through <see cref="RemoteWidgetMirror"/>.</b>
/// One singleton, N boards. That is not a state hazard, since the state IS global — but a mirrored
/// WIDGET would have carried the local hover, tooltip target and GUIAnimator run state onto every
/// peer's board, and those really are the viewer's.
///
/// <b>WHAT ANIMATES, AND WHAT DOES NOT.</b> The ramp above stays keyed on the game's own DRAW LIST
/// (non-inert ∪ in-creation), because that is the set whose entries and exits vanilla animates with
/// <c>ShowCreated</c> / <c>StopAnimations</c>. A chip that vanishes or returns because it was
/// RESERVED or UNRESERVED is hidden and shown INSTANTLY, mirroring the <c>SetActive</c> pair that
/// bypasses <c>SetState</c> entirely. The creation PULSE (<c>ShowCreating</c>'s loop) is still not
/// reproduced — its curve is prefab scene data, exactly as the paragraph above says of
/// <c>animatorCreated</c> — so a chip entering creation arrives on the house ramp, which is a
/// truthful "it just appeared" and not an invented pulse.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ElementInfusionBoardManager.ElementColumn</c> for the column, plus the local client's
/// own <c>InfusionBoardUI</c> for the creating / reserved / available overlay and for every sprite,
/// tint and ring ratio drawn here (game-owned state and assets, READ-ONLY — this class calls no
/// mutating <c>InfusionBoardUI</c> method, so the element board the game desync-checks every round
/// is never written). See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteElementStrip
{
    /// <summary>Element dock width budget — <c>PlayTray.ElementMountWidth</c>. The mount origin is
    /// RIGHT-centre growing LEFT (the objectives convention), so the strip centres half a width to
    /// the left of it.</summary>
    private const float Width = PlayTray.ElementMountWidth;
    private const float ChipSize = 0.030f;
    private const float ChipStep = 0.038f;

    private static readonly Color[] Fallback =
    {
        new(0.95f, 0.40f, 0.15f), // Fire
        new(0.45f, 0.80f, 1.00f), // Ice
        new(0.72f, 0.80f, 0.86f), // Air
        new(0.45f, 0.72f, 0.30f), // Earth
        new(1.00f, 0.95f, 0.58f), // Light
        new(0.56f, 0.40f, 0.82f), // Dark
    };

    private readonly Transform _root;

    /// <summary>
    /// MIXED-REALITY backing plate behind the chip run (user: the MR text/content backing must
    /// cover REMOTE boards too). The owner's element board is a CONVERTED panel, so MrBacking's
    /// panel sweep puts an opaque host plate behind it in MR; this mod-drawn mirror is no panel
    /// and its discs floated bare over the passthrough room. The plate is authored at ALPHA 0 —
    /// a fully transparent draw, so normal mode renders pixel-identically — and registered with
    /// <c>MrBacking.Opacify</c>, which drives it to alpha 1 while MR is on and restores the
    /// recorded 0 exactly on off. Refit to the visible run on every repaint.
    /// </summary>
    private readonly MeshRenderer _mrPlate;
    private readonly Transform[] _chips = new Transform[6];
    private readonly MeshRenderer[] _quads = new MeshRenderer[6];
    private readonly Material[] _mats = new Material[6];
    private readonly SpriteRenderer[] _icons = new SpriteRenderer[6];

    /// <summary>The AVAILABILITY RING behind each disc — the mirror of
    /// <c>InfusionElementUI.availableHighlight</c>. A sibling of the icon under the same chip root,
    /// seated slightly toward the board (+Z) so it draws BEHIND the disc as the authored Image does,
    /// and scaled by the chip root, so the transition ramp carries it along for free. Disabled
    /// unless the element is both drawn and available.</summary>
    private readonly SpriteRenderer[] _rings = new SpriteRenderer[6];

    /// <summary>The game's authored per-element disc sprites, resolved lazily off
    /// <c>InfusionBoardUI.Instance</c> (null slots until the singleton exists — menu/loading).
    /// [i,0] = <c>strongIcon</c>, [i,1] = <c>waningIcon</c>, [i,2] = <c>creationIcon</c> — the same
    /// three sprites the singleton's own Awake hands each <c>InfusionElementUI.Init</c>, where they
    /// land as <c>completeElement</c>, <c>waningElement</c> and <c>elementCreation</c>.
    ///
    /// <para>THE THREE COLUMNS ARE THREE POPULATIONS AND ARE COUNTED AS THREE. A resolver that
    /// summed them into one number is why a missing waning or creation disc reached hardware
    /// unreported — see <see cref="TryResolveSprites"/> and <see cref="StateNames"/>. Slots may be
    /// null (menu / loading) and may also hold a sprite whose texture has been unloaded, which is
    /// worse than null; <see cref="Usable"/> is the test, never <c>!= null</c>.</para></summary>
    private readonly Sprite?[,] _sprites = new Sprite?[6, 3];

    /// <summary>The authored availability ring per element, read off the live
    /// <c>InfusionElementUI.availableHighlight</c>: its sprite, its already-per-element tint (set by
    /// vanilla's <c>Init</c> from <c>UIInfoTools.GetElementHighlightColor</c>) and its size RELATIVE
    /// to the element disc, so the mirror inherits the authored proportion instead of guessing one.
    /// A null sprite means "no ring can be drawn honestly" and the ring stays off.</summary>
    private readonly Sprite?[] _ringSprites = new Sprite?[6];
    private readonly Color[] _ringColors = new Color[6];
    private readonly float[] _ringRatio = new float[6];

    /// <summary>The ring:disc size ratio used when the authored rects cannot be measured (a zero or
    /// non-finite rect). A FALLBACK, not a shipped proportion — the real one is read at runtime in
    /// <see cref="ResolveRings"/>.</summary>
    private const float RingRatioFallback = 1.35f;

    private bool _spritesResolved;
    private bool _resolveLogged;

    /// <summary>
    /// <c>GetInstanceID()</c> of the <c>InfusionBoardUI</c> the sprites in <see cref="_sprites"/>
    /// and the ring measurements were taken from; 0 while nothing is resolved.
    ///
    /// <para>WHY RESOLVING ONCE IS NOT ENOUGH — the 2026-09 report, "die Elemente sind manchmal ein
    /// weisses Viereck". <see cref="_spritesResolved"/> used to latch for the life of the board, so
    /// a strip that resolved against one scenario's infusion board kept pointing its
    /// SpriteRenderers at THAT board's sprites for every scenario after it. A sprite whose atlas has
    /// been unloaded is NOT null: Unity's fake-null covers a destroyed <c>Sprite</c> object, not a
    /// surviving <c>Sprite</c> whose <c>texture</c> went away — and a <c>SpriteRenderer</c> holding
    /// one binds the built-in WHITE texture and draws a flat white rectangle at full opacity, one
    /// chip wide. "Manchmal" is the tell: it takes a board rebuild to get there. Comparing the
    /// singleton's identity costs one int per cadence tick and re-resolves the moment the game
    /// builds a new infusion board.</para>
    /// </summary>
    private int _spriteSourceId;

    /// <summary>
    /// One-shot-per-verdict record of which (element, state) pairs have already reported that they
    /// could not draw their authored disc — second index as in <see cref="_sprites"/>: 0 strong,
    /// 1 waning, 2 creation.
    ///
    /// <para>STATIC because the infusion table is scenario-wide: every peer board in the room draws
    /// the same six elements from the same singleton, so a per-strip flag would print one identical
    /// verdict per peer. Capped per VERDICT CLASS rather than per call, so a second element
    /// degrading is never swallowed by the first one's line.</para>
    /// </summary>
    private static readonly bool[,] s_fallbackLogged = new bool[6, 3];

    /// <summary>Bit i set once element i's refused near-white highlight tint has been reported —
    /// see <see cref="ColorFor"/>. One line per element, never per repaint.</summary>
    private static int s_whiteTintLogged;

    /// <summary>The names of the three authored disc states, indexed like <see cref="_sprites"/>'s
    /// second axis. Every line this class logs about a missing disc names the state through this
    /// array, so no reader ever has to infer WHICH of the three a count was about.</summary>
    private static readonly string[] StateNames = { "STRONG", "WANING", "CREATION" };

    private int _signature = -1;

    // ------------------------------------------------------- created / consumed transitions --

    /// <summary>Whether chip <c>i</c> was actually DRAWN on the previous repaint — the edge the
    /// crumble rests on. Kept beside <see cref="_signature"/> rather than derived from it: the
    /// signature is one packed number for the whole strip and cannot say WHICH element moved.
    /// </summary>
    private readonly bool[] _shownOn = new bool[6];

    /// <summary>Whether chip <c>i</c> was in the game's own DRAW LIST (non-inert ∪ in-creation) on
    /// the previous repaint. It is NOT the same edge as <see cref="_shownOn"/> and the difference is
    /// the point: a chip can leave the picture because it was RESERVED, which vanilla hides
    /// instantly with a bare <c>SetActive(false)</c> that never reaches an animator. Ramps are keyed
    /// on THIS array so only a real board change animates.</summary>
    private readonly bool[] _wasInList = new bool[6];

    /// <summary>Per-chip ramp: whether one is running, whether it is the arrival direction, and how
    /// far in it is. Six parallel arrays rather than a struct list because the set is fixed at six
    /// and indexed by <c>EElement</c> everywhere else in this file.</summary>
    private readonly bool[] _ramping = new bool[6];
    private readonly bool[] _rampIn = new bool[6];
    private readonly float[] _rampElapsed = new float[6];

    /// <summary>The alpha the last repaint wrote into chip <c>i</c>'s FALLBACK quad colour — 1 for
    /// a strong chip, 0.80 for the dimmed waning stand-in. The ramp scales THIS rather than
    /// overwriting it, or a waning fallback chip would fade in to the wrong opacity. The sprite
    /// path needs no twin, and that is a deliberate constraint rather than a happy accident: the
    /// one place a sprite chip is DIMMED — the generic-glyph rung, for a non-strong state — expresses
    /// the dim in RGB and never in alpha, so <see cref="ApplyChip"/> stays the sole owner of every
    /// icon's alpha and there is no second base to keep in step.</summary>
    private readonly float[] _quadBaseAlpha = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>
    /// THE NO-STORM SEED. The first repaint after a board build populates the whole strip at once,
    /// and every element already up would otherwise materialise in front of a peer who was looking
    /// at a settled board a frame earlier — an animation nobody's owner is playing. Same rule, same
    /// reason, as <c>CardsDriver</c>'s <c>_dockAnimSuppressed</c> and <c>RemoteBoardCard</c>'s own
    /// seed: the first content is silent, every later change animates.
    /// </summary>
    private bool _transitionsSeeded;

    /// <summary>The settle scale a chip grows from / shrinks to under the fade. The mod's own card
    /// number (<c>VRCard.DustSettleScale</c>, a <c>private const</c> over there), restated here
    /// rather than borrowed from <see cref="RemoteBoardCard"/> because a strip and a card recess
    /// share no tuning surface and coupling them would only invent one.</summary>
    private const float ChipSettleScale = 0.82f;

    /// <summary>How many elements are currently INFUSED — non-inert in
    /// <c>ElementInfusionBoardManager</c> (diagnostics; <c>RemoteControlBoard</c> logs it as
    /// "N infused"). Deliberately NOT the number of chips drawn: an element in creation adds a chip
    /// without being infused, and a reserved element is infused without being drawn.</summary>
    public int ActiveCount { get; private set; }

    public RemoteElementStrip(Transform boardRoot, in RemoteBoardLayout layout)
    {
        // MOUNT (position + authored per-board scale, exactly like PlayTray.BuildMounts sets its
        // own element mount) …
        var mount = new GameObject("ElementMount").transform;
        mount.SetParent(boardRoot, worldPositionStays: false);
        mount.localPosition = layout.ElementMount;
        mount.localScale = Vector3.one * layout.ElementScale;

        // … and the strip itself, half a dock width to the LEFT of it — the mount's origin is
        // RIGHT-centre growing left (the objectives convention), so the shift belongs INSIDE the
        // mount, where the scale applies to it too.
        _root = new GameObject("Elements").transform;
        _root.SetParent(mount, worldPositionStays: false);
        _root.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);

        // MR backing plate (see the field doc): the repo's dark panel neutral at alpha 0, seated
        // slightly BEHIND the chips toward the board (+Z) like every remote text plate.
        _mrPlate = BoardVisual.Quad(_root, "MrPlate", new Vector2(1f, 1f),
            BoardVisual.Unlit(new Color(0.12f, 0.11f, 0.10f, 0f)));
        _mrPlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        _mrPlate.gameObject.SetActive(false);
        WorldUI.MrBacking.Opacify(_mrPlate.sharedMaterial);

        for (int i = 0; i < 6; i++)
        {
            // One positioned CHIP root per element; the quad fallback and the sprite icon are
            // siblings under it, so the layout below moves one transform whichever renders.
            // Everything is built HERE, at board-build time, because VRLayers.Apply runs over the
            // finished board once — a renderer created later would miss the re-layer and be
            // invisible to the mod head camera.
            var chip = new GameObject($"Element_{(ElementInfusionBoardManager.EElement)i}").transform;
            chip.SetParent(_root, worldPositionStays: false);
            _chips[i] = chip;

            _mats[i] = BoardVisual.Unlit(Fallback[i]);
            _quads[i] = BoardVisual.Quad(chip, "Fallback", new Vector2(1f, 1f), _mats[i]);

            // AVAILABILITY RING, built BEFORE the icon and seated toward the board (+Z, the same
            // convention the MR plate uses) so it draws behind the disc. Built HERE for the same
            // reason everything else is — VRLayers.Apply runs over the finished board once.
            var ringGo = new GameObject("AvailableRing");
            ringGo.transform.SetParent(chip, worldPositionStays: false);
            ringGo.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            var ring = ringGo.AddComponent<SpriteRenderer>();
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;
            _rings[i] = ring;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(chip, worldPositionStays: false);
            var icon = iconGo.AddComponent<SpriteRenderer>();
            icon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            icon.receiveShadows = false;
            icon.enabled = false;
            _icons[i] = icon;

            chip.gameObject.SetActive(false);
        }

        // The transition clock. This class is plain C# and its owner refreshes content on a 4 Hz
        // cadence — one or two samples across a 0.3 s ramp, i.e. the snap the ramp exists to
        // remove — so the strip carries its own tick, exactly as RemoteBoardCard's MaterialisePump
        // and RemoteBoardFurniture's RemoteCapFx do. It rides the strip root, so it stops with the
        // board: a strip nobody is drawing has nothing to animate.
        _root.gameObject.AddComponent<ElementTransitionPump>().Strip = this;
    }

    /// <summary>
    /// Advance every running chip transition by one frame — the CREATED pop and its counterpart,
    /// the crumble of an element that just left the game's draw list. See the class doc for the
    /// evidence that these are derivable from <c>ElementColumn</c>, and for why a chip that leaves
    /// or returns because of a RESERVATION is switched instantly instead: vanilla's reserved branch
    /// is a bare <c>SetActive(false)</c> that never reaches an animator.
    ///
    /// <para>Curve, durations and hitch cap are the mod's card ramp, referenced rather than copied
    /// (<c>VRCard.SmootherStep</c>, <c>DockAppearSeconds</c> 0.28 s, <c>DockVanishSeconds</c>
    /// 0.30 s), on UNSCALED time because the element board moves during card phases, which pause
    /// <c>timeScale</c>. Never throws: every write is to an object this class minted.</para>
    /// </summary>
    internal void TickTransitions()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        for (int i = 0; i < 6; i++)
        {
            if (!_ramping[i])
                continue;
            _rampElapsed[i] += dt;
            float duration = _rampIn[i] ? VRCard.DockAppearSeconds : VRCard.DockVanishSeconds;
            float t = duration > 0f ? Mathf.Clamp01(_rampElapsed[i] / duration) : 1f;
            float st = VRCard.SmootherStep(t);
            ApplyChip(i, _rampIn[i] ? st : 1f - st);
            if (t < 1f)
                continue;
            _ramping[i] = false;
            // Both directions land SETTLED, not at the ramp's end value: a chip that crumbled away
            // is about to be hidden and must come back opaque and full-size the next time its
            // element is infused, exactly as VRCard resets its own alpha before parking.
            ApplyChip(i, 1f);
            if (!_rampIn[i] && _chips[i].gameObject.activeSelf)
                _chips[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Arm chip <paramref name="i"/>'s transition and paint its frame zero here rather
    /// than on the next tick — otherwise an arriving element shows one fully settled frame before
    /// it starts arriving, which is the snap the ramp exists to remove.</summary>
    private void BeginChipRamp(int i, bool arriving)
    {
        _ramping[i] = true;
        _rampIn[i] = arriving;
        _rampElapsed[i] = 0f;
        ApplyChip(i, arriving ? 0f : 1f);
    }

    /// <summary>Settle chip <paramref name="i"/> at once — a repaint of a chip that is STAYING up
    /// (the Strong to Waning swap, which vanilla explicitly does not animate) must not inherit a
    /// half-faded alpha from a ramp that was still running. Free when nothing is running.</summary>
    private void EndChipRamp(int i)
    {
        if (!_ramping[i])
            return;
        _ramping[i] = false;
        _rampElapsed[i] = 0f;
        ApplyChip(i, 1f);
    }

    /// <summary>
    /// Write one point of chip <paramref name="i"/>'s ramp. <paramref name="visible"/> is the
    /// smootherstepped fraction read from the arrival end, so one method serves both directions.
    ///
    /// <para>The scale rides the CHIP ROOT, whose two children (the fallback quad and the sprite
    /// icon) already carry their own fitted scales — so the settle multiplies onto whichever of the
    /// two is rendering without either of them needing to know about it. Both alphas are written
    /// even though only one renderer is enabled, for the same reason
    /// <c>RemoteBoardCard.ApplyMaterialise</c> writes all three of its materials: the repaint can
    /// switch which path draws while a ramp is running, and the other one must not be sitting at
    /// full opacity when it does. The AVAILABILITY RING is the third such renderer and takes the
    /// same treatment, scaled off its AUTHORED alpha (<c>_ringColors</c>) for the reason
    /// <c>_quadBaseAlpha</c> exists — the ring is not opaque to begin with.</para>
    /// </summary>
    private void ApplyChip(int i, float visible)
    {
        float a = Mathf.Clamp01(visible);
        _chips[i].localScale = Vector3.one * Mathf.Lerp(ChipSettleScale, 1f, a);
        Color quad = _mats[i].color;
        quad.a = _quadBaseAlpha[i] * a;
        _mats[i].color = quad;
        Color icon = _icons[i].color;
        icon.a = a;
        _icons[i].color = icon;
        Color ring = _ringColors[i];
        ring.a *= a;
        _rings[i].color = ring;
    }

    /// <summary>Re-read the infusion table AND the three overlay masks, and repaint on an actual
    /// change to any of them.</summary>
    public void Refresh()
    {
        // Late sprite resolution: the singleton comes up with the scenario UI, typically after the
        // board was built. Cheap while unresolved (one null check per cadence tick); on success the
        // signature is invalidated so the very next repaint switches the quads over to the discs.
        //
        // AND RE-RESOLUTION WHEN THE GAME BUILDS A NEW INFUSION BOARD. The second term is the fix
        // for the white square: sprites captured from a previous scenario's singleton outlive their
        // atlas, and a sprite that outlives its texture draws WHITE rather than nothing. See
        // _spriteSourceId. Cheap when warm too — one instance-id compare per cadence tick.
        if ((!_spritesResolved || SpriteSourceChanged()) && TryResolveSprites())
            _signature = -1;

        int sig = 0;
        int infused = 0;
        var state = new ElementInfusionBoardManager.EColumn[6];
        for (int i = 0; i < 6; i++)
        {
            ElementInfusionBoardManager.EColumn col;
            try { col = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { col = ElementInfusionBoardManager.EColumn.Inert; }
            state[i] = col;
            sig = sig * 3 + (int)col;
            if (col != ElementInfusionBoardManager.EColumn.Inert)
                infused++;
        }

        // The three overlay masks the column cannot express — see the class doc for why they are
        // ZERO wire and why reading the local singleton yields the same answer on every client. All
        // three degrade to 0, which is byte-for-byte the picture this strip drew before them.
        ReadOverlay(out int creating, out int reserved, out int available);
        // The signature must cover everything the repaint below can depend on, or a change that
        // touches only an overlay bit repaints nothing. 3^6 = 729 column states times three 6-bit
        // masks: 729 * 64^3 = 191,102,976 < int.MaxValue, so this cannot wrap.
        sig = ((sig * 64 + creating) * 64 + reserved) * 64 + available;

        if (sig == _signature)
            return;
        _signature = sig;
        ActiveCount = infused;

        // WHICH CHIPS ARE DRAWN — InfusionBoardUI.UpdateBoard's own rule, term for term:
        //   inList = every non-inert element PLUS everything still in elementsInCreation;
        //   drawn  = inList MINUS everything reserved ("else if (IsElementReserved…)
        //            SetActive(false)"), because a reserved element is hidden even when it is Strong.
        var inList = new bool[6];
        var show = new bool[6];
        int visible = 0;
        for (int i = 0; i < 6; i++)
        {
            inList[i] = state[i] != ElementInfusionBoardManager.EColumn.Inert
                        || (creating & (1 << i)) != 0;
            show[i] = inList[i] && (reserved & (1 << i)) == 0;
            if (show[i])
                visible++;
        }

        // MR plate: hug the visible run (centred at the strip origin, like the run itself), with a
        // small out-pad so the disc edges sit on plate rather than passthrough room; hidden while
        // no element is up (an empty strip must not show a bare plate in MR).
        //
        // RESIDUE, STATED: the plate is sized to the run this repaint LANDS on, so an element that
        // is crumbling out (see TickTransitions) sits beside the shrunken plate for up to
        // DockVanishSeconds (0.30 s) — in MR only, over passthrough, fading the whole time. Sizing
        // it to the union instead would make the plate itself jump wider than the discs for the
        // same 0.30 s, which is the more visible of the two artefacts on a backing whose entire job
        // is to be unnoticed.
        bool anyChips = visible > 0;
        if (_mrPlate.gameObject.activeSelf != anyChips)
            _mrPlate.gameObject.SetActive(anyChips);
        if (anyChips)
        {
            float runW = (visible - 1) * ChipStep + ChipSize + 0.010f;
            _mrPlate.transform.localScale = new Vector3(runW, ChipSize + 0.010f, 1f);
        }

        // Pack the visible chips left-to-right and centre the run, exactly like the game's own
        // horizontal element holder does with its layout group.
        float left = -(visible - 1) * 0.5f * ChipStep;
        int slot = 0;
        for (int i = 0; i < 6; i++)
        {
            bool on = show[i];
            bool wasOn = _shownOn[i];
            bool wasInList = _wasInList[i];
            _shownOn[i] = on;
            _wasInList[i] = inList[i];
            if (!on)
            {
                // LEFT THE BOARD (consumed, or waned all the way out). It keeps its seat and its
                // artwork and crumbles in place — the run below has already re-centred WITHOUT it,
                // which is why the position write is inside the `on` branch and this chip is not
                // repositioned. TickTransitions hides it when the ramp lands.
                //
                // RESERVED is the other way out of the picture and it does NOT ramp: vanilla's
                // reserved branch is a bare SetActive(false) that never reaches SetState, so there
                // is no animation to mirror. The `!inList[i]` term is what tells the two apart.
                if (wasOn && !inList[i] && _transitionsSeeded)
                {
                    BeginChipRamp(i, arriving: false);
                }
                else
                {
                    EndChipRamp(i);
                    if (_chips[i].gameObject.activeSelf && !_ramping[i])
                        _chips[i].gameObject.SetActive(false);
                }
                continue;
            }
            if (!_chips[i].gameObject.activeSelf)
                _chips[i].gameObject.SetActive(true);
            bool strong = state[i] == ElementInfusionBoardManager.EColumn.Strong;
            // IN CREATION is exactly vanilla's `isCreating`: in the creation list AND still Inert.
            // It cannot be anything else — UpdateBoard removes an element from elementsInCreation
            // the moment its column goes non-inert, before it builds the list — so an Inert chip
            // that is drawn at all is a creating one.
            bool inCreation = state[i] == ElementInfusionBoardManager.EColumn.Inert;
            _chips[i].localPosition = new Vector3(left + slot * ChipStep, 0f, 0f);
            slot++;

            // AVAILABILITY RING: only ever under a chip that is actually drawn. In vanilla the ring
            // is a child of the element GameObject, so hiding the element hides the ring with it
            // even though availableHighlight.enabled stays set — this AND reproduces that.
            Sprite? ringSprite = _ringSprites[i];
            if ((available & (1 << i)) != 0 && Usable(ringSprite))
                ApplyRing(i, ringSprite!, ChipSize * _ringRatio[i]);
            else if (_rings[i].enabled)
                _rings[i].enabled = false;

            // WHICH OF THE THREE AUTHORED DISCS THIS STATE WANTS. Named once, here, and carried
            // into every log line below as an INDEX INTO StateNames — the three states were being
            // summed into one number by the resolver, which is why a missing waning or creation
            // disc could reach hardware with nothing in the log to say so.
            int want = inCreation ? 2 : (strong ? 0 : 1);

            // THE DRAW LADDER. Three rungs, and the invariant across all of them is that no rung
            // may ever hand an UNDRAWABLE sprite to a SpriteRenderer — that is what paints the
            // white square. Every rung either draws real artwork or steps down, and every step
            // down goes on the record naming the element and the state.
            if (TryApplyIcon(i, _sprites[i, want], ChipSize, Color.white))
            {
                // RUNG 1, THE REAL DISC: vanilla parity is the sprite itself — untinted, full chip
                // size in every state (the waning and creation artwork carry their own look;
                // vanilla swaps the sprite, it does not dim or shrink — see the class doc).
                if (_quads[i].enabled)
                    _quads[i].enabled = false;
            }
            else if (TryApplyIcon(i, GenericIcon(i), want == 0 ? ChipSize : ChipSize * 0.74f,
                                  want == 0 ? Color.white : new Color(0.55f, 0.55f, 0.55f)))
            {
                // RUNG 2, THE GAME'S OWN GENERIC ELEMENT GLYPH (UIInfoTools.GetElementIcon, i.e.
                // ElementConfigUI.icon — the symbol the game draws for this element everywhere
                // outside the infusion board). It is real authored art for the RIGHT element, so a
                // missing waning or creation disc now costs the STATE's artwork and not the
                // element's identity. Dimmed and shrunk for the two non-strong states, exactly as
                // the colour chip below has always been, because the glyph itself says nothing
                // about the state; the strong state keeps full size and no dim, since the glyph is
                // then saying precisely what the strong disc would.
                LogFallback(i, want, "the game's own generic element glyph, dimmed and shrunk for a "
                                     + "non-strong state");
                if (_quads[i].enabled)
                    _quads[i].enabled = false;
            }
            else
            {
                // RUNG 3, THE COLOUR CHIP (no game UI at all — menu / loading, the only case left
                // now that rung 2 covers a missing disc): the tinted quad, with the old dim+shrink
                // standing in for the missing waning artwork. An in-creation element gets the same
                // dim+shrink for the same reason — there is no artwork to say "forming" with, and a
                // full-size chip would read as a finished infusion.
                //
                // THE TINT CANNOT BE WHITE: ColorFor refuses a near-white highlight colour in
                // favour of this file's per-element table, which contains no white. A white chip
                // here would be indistinguishable from the very defect this ladder exists to make
                // impossible.
                if (_icons[i].enabled)
                    _icons[i].enabled = false;
                if (!_quads[i].enabled)
                    _quads[i].enabled = true;
                Color c = ColorFor((ElementInfusionBoardManager.EElement)i, i);
                _mats[i].color = strong ? c : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.80f);
                // The base the ramp SCALES (see _quadBaseAlpha) — recorded on the same statement
                // that writes the colour, so the two can never be about different repaints.
                _quadBaseAlpha[i] = _mats[i].color.a;
                float s = strong ? ChipSize : ChipSize * 0.74f;
                _quads[i].transform.localScale = new Vector3(s, s, 1f);
                LogFallback(i, want, "the flat per-element colour chip (no game UI is up to supply "
                                     + "artwork of any kind)");
            }

            // CREATED, or a settled repaint. The paint above has just overwritten the quad's and the
            // ring's alpha, so a running ramp has to be re-applied (or ended) here and not before it.
            // Keyed on the DRAW LIST, not on `wasOn`: a chip coming back from a reservation is a
            // SetActive(true) in vanilla, not an arrival, and must not pop.
            if (!wasInList && _transitionsSeeded)
                BeginChipRamp(i, arriving: true);
            else
                EndChipRamp(i);
        }

        // Every element the strip is going to show this repaint has now been seeded once, so the
        // NEXT change is a real one and animates. See _transitionsSeeded.
        _transitionsSeeded = true;
    }

    /// <summary>
    /// The three overlay masks the element COLUMN cannot express, read off the local
    /// <c>InfusionBoardUI</c> singleton — bit i = element i in the game's own <c>EElement</c> order.
    ///
    /// <para>This is a READ of publicized private fields (<c>elementsInCreation</c>,
    /// <c>elementsReserved</c>, <c>elementsUI</c> and, through the last,
    /// <c>InfusionElementUI.availableHighlight</c>) and nothing else — the same treatment
    /// <c>elementConfigs</c> already gets in <see cref="TryResolveSprites"/>. Nothing here mutates
    /// the infusion board.</para>
    ///
    /// <para>EVERY failure — no singleton (menu / loading), a null list, an element index outside
    /// 0..5, or a throw — yields all-zero masks, which is precisely the picture this strip drew
    /// before the overlay existed. Absence keeps the old behaviour, deliberately.</para>
    /// </summary>
    private static void ReadOverlay(out int creating, out int reserved, out int available)
    {
        creating = 0;
        reserved = 0;
        available = 0;
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            if (board == null)
                return;

            List<ElementInfusionBoardManager.EElement>? inCreation = board.elementsInCreation;
            if (inCreation != null)
            {
                for (int k = 0; k < inCreation.Count; k++)
                {
                    int i = (int)inCreation[k];
                    if (i >= 0 && i < 6)
                        creating |= 1 << i;
                }
            }

            List<ElementInfusionBoardManager.EElement>? held = board.elementsReserved;
            if (held != null)
            {
                for (int k = 0; k < held.Count; k++)
                {
                    int i = (int)held[k];
                    if (i >= 0 && i < 6)
                        reserved |= 1 << i;
                }
            }

            // AVAILABLE is not a list on the board — vanilla's SetAvailableElements pushes it
            // straight into each InfusionElementUI's ring Image, so the enabled flag on that Image
            // IS the state. Reading it, rather than recomputing GetAvailableElements(), means the
            // mirror cannot disagree with the widget it mirrors.
            Dictionary<ElementInfusionBoardManager.EElement, InfusionElementUI>? ui = board.elementsUI;
            if (ui != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (!ui.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI one)
                        || one == null)
                        continue;
                    Image ring = one.availableHighlight;
                    if (ring != null && ring.enabled)
                        available |= 1 << i;
                }
            }
        }
        catch
        {
            // A throw must cost the overlay, never the strip. Fall all the way back to "no overlay".
            creating = 0;
            reserved = 0;
            available = 0;
        }
    }

    /// <summary>
    /// Point chip <paramref name="i"/>'s ring at (the mip-baked copy of) <paramref name="sprite"/>,
    /// tint it with the game's own per-element highlight colour and fit it to
    /// <paramref name="worldSize"/> world units on its larger bounds axis — the same fit rule
    /// <see cref="TryApplyIcon"/> uses, so ring and disc keep the authored proportion at any chip size.
    /// The colour written is the AUTHORED one; <see cref="ApplyChip"/> is what scales its alpha down
    /// while a transition runs, so this must not pre-multiply it.
    /// </summary>
    private void ApplyRing(int i, Sprite sprite, float worldSize)
    {
        SpriteRenderer ring = _rings[i];
        Sprite show = Baked(sprite);
        if (ring.sprite != show)
            ring.sprite = show;
        if (ring.color != _ringColors[i])
            ring.color = _ringColors[i];
        Vector3 size = show.bounds.size;
        float axis = Mathf.Max(size.x, size.y);
        float fit = axis > 0.0001f ? worldSize / axis : 1f;
        var scale = new Vector3(fit, fit, 1f);
        if (ring.transform.localScale != scale)
            ring.transform.localScale = scale;
        if (!ring.enabled)
            ring.enabled = true;
    }

    /// <summary>
    /// May this strip hand <paramref name="sprite"/> to a <see cref="SpriteRenderer"/>?
    ///
    /// <para>THE SECOND TERM IS THE WHOLE POINT. Every draw decision in this file used to ask only
    /// <c>sprite != null</c>, and that question cannot see the failure behind the 2026-09 report:
    /// a LIVE <c>Sprite</c> whose <c>texture</c> has gone away. Unity's fake-null covers a
    /// DESTROYED sprite object, not a surviving sprite pointing at an unloaded atlas, and a
    /// <c>SpriteRenderer</c> given one silently binds the built-in WHITE texture and draws a flat
    /// white rectangle at full opacity — a bare white square exactly one chip wide. One reference
    /// comparison is the difference between that and an honest fallback.</para>
    /// </summary>
    private static bool Usable(Sprite? sprite) => sprite != null && sprite.texture != null;

    /// <summary>
    /// Point chip <paramref name="i"/>'s icon at (the mip-baked copy of) <paramref name="sprite"/>,
    /// tint it <paramref name="rgb"/> and fit it to <paramref name="worldSize"/> world units on its
    /// larger bounds axis. A SpriteRenderer draws a sprite at <c>rect/pixelsPerUnit</c> world units,
    /// so the fit divides the target size by that axis — the artwork fills the chip without
    /// distortion whatever the atlas padding is.
    ///
    /// <para>Returns FALSE without touching the renderer when the sprite cannot be drawn honestly
    /// (see <see cref="Usable"/>), which is what lets the caller walk down its fallback ladder
    /// instead of shipping a white square. Only the RGB is written here: the ALPHA belongs to
    /// <see cref="ApplyChip"/>, which may be running a transition ramp at the same time — which is
    /// also why the dim of a non-strong state is expressed in RGB and not in alpha, so the sprite
    /// path needs no <see cref="_quadBaseAlpha"/> twin.</para>
    /// </summary>
    private bool TryApplyIcon(int i, Sprite? sprite, float worldSize, Color rgb)
    {
        if (!Usable(sprite))
            return false;
        SpriteRenderer icon = _icons[i];
        Sprite show = Baked(sprite!);
        if (icon.sprite != show)
            icon.sprite = show;
        Color tint = icon.color;
        if (tint.r != rgb.r || tint.g != rgb.g || tint.b != rgb.b)
            icon.color = new Color(rgb.r, rgb.g, rgb.b, tint.a);
        Vector3 size = show.bounds.size; // rect / pixelsPerUnit, world units at scale 1
        float axis = Mathf.Max(size.x, size.y);
        float fit = axis > 0.0001f ? worldSize / axis : 1f;
        var scale = new Vector3(fit, fit, 1f);
        if (icon.transform.localScale != scale)
            icon.transform.localScale = scale;
        if (!icon.enabled)
            icon.enabled = true;
        return true;
    }

    /// <summary>
    /// The mip-baked copy of <paramref name="source"/>, or <paramref name="source"/> itself.
    ///
    /// <para>Shared mip-bake cache (the local card-face treatment): the element discs live on the
    /// game's mipless UI atlases, and a mipless bilinear sprite on a world-space board aliases in
    /// texture space. <c>ReplacementFor</c> returns null both for "already mipped" and for sprites
    /// it must not reproduce (rotated/tight packing) — the original then renders as-is, which is
    /// never worse than before. Guarded: a bake surprise must not cost the strip its icons.</para>
    ///
    /// <para>TWO CHECKS ON WHAT COMES BACK, and both can only ever REJECT a replacement and keep
    /// the original, so neither can make the picture worse. (1) The replacement's own texture must
    /// still be alive — a baked <c>Texture2D</c> that has been unloaded leaves a live sprite that
    /// draws the built-in white texture, which is the same white-square failure <see cref="Usable"/>
    /// guards the source against, one indirection further out. (2) Its logical rect must match the
    /// source's: <c>CardFaceMipBake</c> keys its cache on <c>Sprite.GetInstanceID()</c> and Unity
    /// REUSES instance ids after an object is destroyed, so a scenario change can in principle
    /// answer this strip with a copy of somebody else's art. Both bake paths build the replacement
    /// at the source's own rect size, so a size mismatch means the cache answered about a different
    /// sprite. This is a read-only defence at the CALL SITE: <c>CardFaceMipBake</c> is shared with
    /// the card faces, the panels, the initiative track and the tooltips, and its caches are not
    /// this lane's to change.</para>
    /// </summary>
    private static Sprite Baked(Sprite source)
    {
        try
        {
            Sprite? baked = CardFaceMipBake.ReplacementFor(source);
            if (baked != null && baked.texture != null
                && Mathf.Abs(baked.rect.width - source.rect.width) < 0.5f
                && Mathf.Abs(baked.rect.height - source.rect.height) < 0.5f)
            {
                return baked;
            }
        }
        catch { /* keep the original sprite */ }
        return source;
    }

    /// <summary>
    /// The game's own GENERIC glyph for element <paramref name="i"/> —
    /// <c>UIInfoTools.GetElementIcon</c>, i.e. <c>ElementConfigUI.icon</c>, the symbol the game
    /// draws for this element everywhere outside the infusion board itself. Read-only, and null
    /// whenever <c>UIInfoTools</c> is not up (menu / loading), which is the only case the colour
    /// chip below it still has to cover.
    /// </summary>
    private static Sprite? GenericIcon(int i)
    {
        try
        {
            if (UIInfoTools.Instance != null)
                return UIInfoTools.Instance.GetElementIcon((ElementInfusionBoardManager.EElement)i);
        }
        catch { /* menu / loading window */ }
        return null;
    }

    /// <summary>
    /// Put one (element, state) fallback on the record — once per verdict for the whole session,
    /// see <see cref="s_fallbackLogged"/>.
    ///
    /// <para>WHY THIS IS A NOTE AND NOT AN Info. This defect survived a 100 MB two-machine capture
    /// because the only element line either log carried was a single summary count, and the
    /// fallback itself said nothing at all. A line the co-player's shipped log level does not print
    /// is not an instrument, so the verdict prints at the default level.</para>
    /// </summary>
    private static void LogFallback(int i, int state, string what)
    {
        if (i < 0 || i >= 6 || state < 0 || state > 2 || s_fallbackLogged[i, state])
            return;
        s_fallbackLogged[i, state] = true;
        // HW-VERIFY: the white-square round reads THIS line. It names the element AND which of the
        // three authored discs was undrawable — the pair the old "6/6 disc sprites" summary could
        // not express — and says what was drawn in its place.
        VRLog.Note("Net", $"Remote element strip: {(ElementInfusionBoardManager.EElement)i}'s " +
                          $"{StateNames[state]} disc cannot be drawn — the authored sprite is " +
                          "absent, or it survived its own texture (a sprite whose atlas was " +
                          "unloaded is NOT null, and a SpriteRenderer holding one paints a plain " +
                          $"WHITE square). Falling back to {what}. The element is still shown and " +
                          "still identifiable; what is lost is the authored artwork for that one " +
                          "state.");
    }

    /// <summary>Has the game built a DIFFERENT infusion board since this strip took its sprites?
    /// See <see cref="_spriteSourceId"/> — the sprites of a torn-down scenario's board outlive
    /// their atlas, and that is the white square. Guarded and free: one instance-id compare.
    /// </summary>
    private bool SpriteSourceChanged()
    {
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            return board != null && board.GetInstanceID() != _spriteSourceId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pull the authored strong / waning / creation disc sprites off the game's infusion-board
    /// singleton — <c>InfusionBoardUI.elementConfigs</c> (publicized serialized field), the exact
    /// array its own Awake feeds every <c>InfusionElementUI.Init</c> — and, from the live element
    /// widgets it built there, the AVAILABILITY RING's sprite, tint and size ratio. Read-only:
    /// sprites are assets and the widgets are only measured, never mutated. Returns true once at
    /// least one sprite resolved; a throwing/absent singleton just leaves the fallback chips in
    /// place until the next cadence tick.
    ///
    /// <para>THE COUNT USED TO BE ONE NUMBER FOR THREE POPULATIONS, and that is why a bare white
    /// square reached hardware. The old body stored all three sprites and then counted success on
    /// <c>strongIcon</c> ALONE, so it printed "6/6 element disc sprites" while a null
    /// <c>waningIcon</c> or <c>creationIcon</c> sat in the array with nothing anywhere to say so.
    /// This project has the bug filed under its own name — an instrument that measures one term
    /// agrees with every reading and answers no question — so the three states are counted, named
    /// and reported SEPARATELY here, and the elements that came back unusable are listed by
    /// name.</para>
    ///
    /// <para>"Unusable" is <see cref="Usable"/>, not <c>!= null</c>: a sprite that survived its own
    /// texture is exactly as undrawable as a missing one and looks far worse — see that method.
    /// </para>
    /// </summary>
    private bool TryResolveSprites()
    {
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            if (board == null || board.elementConfigs == null)
                return false;
            int sourceId = board.GetInstanceID();
            var found = new int[3];
            var missing = new List<string>();
            var configs = board.elementConfigs;
            for (int c = 0; c < configs.Length; c++)
            {
                int i = (int)configs[c].element;
                if (i < 0 || i >= 6)
                    continue;
                _sprites[i, 0] = configs[c].strongIcon;
                _sprites[i, 1] = configs[c].waningIcon;
                _sprites[i, 2] = configs[c].creationIcon;
                for (int st = 0; st < 3; st++)
                {
                    if (Usable(_sprites[i, st]))
                        found[st]++;
                    else
                        missing.Add($"{(ElementInfusionBoardManager.EElement)i}/{StateNames[st]}");
                }
            }
            if (found[0] + found[1] + found[2] == 0)
                return false;
            ResolveRings(board, out int widgets, out int images, out int ringSprites, out int ratios);
            _spritesResolved = true;
            // A NEW infusion board is a new set of assets and therefore a new verdict: re-arm the
            // one-shot so the re-resolve is on the record instead of inheriting the old board's
            // line. (See _spriteSourceId for why re-resolving at all is the white-square fix.)
            if (sourceId != _spriteSourceId)
                _resolveLogged = false;
            _spriteSourceId = sourceId;
            if (!_resolveLogged)
            {
                _resolveLogged = true;
                // HW-VERIFY: the white-square round reads THIS line. The three disc states are
                // counted separately and the unusable ones are named, because the single number
                // this line used to print could not distinguish "all three present" from "strong
                // present, the other two null" — which is precisely the gap the defect lived in.
                VRLog.Note("Net", $"Remote element strip: discs resolved from InfusionBoardUI " +
                                  $"#{sourceId} — STRONG {found[0]}/6, WANING {found[1]}/6, " +
                                  $"CREATION {found[2]}/6" +
                                  (missing.Count > 0
                                      ? $"; UNUSABLE (null, or a sprite that outlived its texture): "
                                        + string.Join(", ", missing.ToArray()) +
                                        " — those states fall back to the game's generic element " +
                                        "glyph rather than to a blank chip"
                                      : "; every state has usable authored artwork") +
                                  $". Availability rings: {widgets}/6 element widgets found, " +
                                  $"{images}/6 of them carry an availableHighlight Image, " +
                                  $"{ringSprites}/6 of those Images have a usable SPRITE (an Image " +
                                  "with no sprite is a plain tinted rectangle in vanilla, not a " +
                                  "ring, and is deliberately not mirrored — a shape would have to " +
                                  $"be invented), {ratios}/6 authored ring:disc ratios measured.");
            }
            return true;
        }
        catch (System.Exception ex)
        {
            if (!_resolveLogged)
            {
                _resolveLogged = true;
                // HW-VERIFY: the only line that distinguishes "the singleton threw at us" from
                // "the singleton is not up yet" — without it both look like a silent fallback.
                VRLog.Alert("Net", $"Remote element strip: disc sprite resolution FAILED " +
                                   $"({ex.GetType().Name}: {ex.Message}) — every chip falls back to " +
                                   "the game's generic element glyph, or to the flat colour chip if " +
                                   "even that is unavailable.");
            }
            return false;
        }
    }

    /// <summary>
    /// The colour of the LAST-RUNG chip for element <paramref name="e"/>: the game's own per-element
    /// highlight colour when <c>UIInfoTools</c> is up, else this file's table.
    ///
    /// <para>AND IT MAY NEVER BE WHITE. The whole job of the last rung is a chip that reads as
    /// "element X, artwork missing"; a WHITE one reads as the defect this ladder exists to remove,
    /// and it is indistinguishable on screen from the untextured-sprite failure whatever produced
    /// it. So a highlight colour that comes back within a hair of white is refused in favour of the
    /// local table, which is authored per element and contains no white — and the refusal is
    /// reported, once per element, because a silently substituted colour is how a wrong assumption
    /// survives a capture.</para>
    /// </summary>
    private static Color ColorFor(ElementInfusionBoardManager.EElement e, int index)
    {
        try
        {
            if (UIInfoTools.Instance != null)
            {
                Color c = UIInfoTools.Instance.GetElementHighlightColor(e, 1f);
                if (c.r < 0.97f || c.g < 0.97f || c.b < 0.97f)
                    return c;
                if ((s_whiteTintLogged & (1 << index)) == 0)
                {
                    s_whiteTintLogged |= 1 << index;
                    // HW-VERIFY: if this ever prints, the game's own highlight colour for an element
                    // is white and the last-rung chip would have been the reported white square.
                    VRLog.Note("Net", $"Remote element strip: the game's highlight colour for {e} is " +
                                      $"({c.r:F2},{c.g:F2},{c.b:F2}) — indistinguishable from white, " +
                                      "which is the exact picture the missing-artwork chip must never " +
                                      "show. Using this file's authored element colour instead.");
                }
            }
        }
        catch { /* menu / loading window — fall through */ }
        return Fallback[index];
    }

    /// <summary>
    /// Measure the AUTHORED availability ring off the live element widgets the singleton built in
    /// its Awake (<c>InfusionBoardUI.elementsUI</c> → <c>InfusionElementUI.availableHighlight</c> and
    /// <c>.elementImage</c>, all publicized). Records, per element: the ring sprite; its colour —
    /// which vanilla's own <c>Init</c> has ALREADY tinted per element via
    /// <c>UIInfoTools.GetElementHighlightColor(type, availableHighlight.color.a)</c>, so it is taken
    /// verbatim rather than re-derived; and the ring:disc size ratio, so the mirror keeps the
    /// authored proportion at whatever chip size this strip runs at.
    ///
    /// <para>The ratio is measured on the LARGER axis of each RectTransform's rect, matching the fit
    /// rule <see cref="TryApplyIcon"/> and <see cref="ApplyRing"/> use. A rect that is zero or
    /// non-finite leaves <see cref="RingRatioFallback"/> standing for that element and nothing else
    /// changes; a missing sprite leaves the ring OFF for that element entirely (an honest blank
    /// beats a ring at a guessed size).</para>
    ///
    /// <para>WHY IT REPORTS FOUR NUMBERS AND NOT ONE. Both hardware logs said "0/6 availability
    /// rings", and that single number was true of at least three different worlds: the singleton
    /// exposing no <c>elementsUI</c> dictionary at all; the widgets existing but carrying no
    /// <c>availableHighlight</c>; and the Images existing with NO SPRITE ON THEM — which is a
    /// perfectly ordinary authored uGUI look (a sprite-less <c>Image</c> draws a plain rectangle
    /// tinted by its colour) and means there is no ring SHAPE to mirror at all. A mirror cannot
    /// invent one, so 0/6 in that last world is correct behaviour and not a defect; the point of
    /// splitting the count is that the next log SAYS WHICH world it is instead of leaving three
    /// candidates alive. Same bug class as the disc counter above, one field over.</para>
    /// </summary>
    private void ResolveRings(InfusionBoardUI board, out int widgets, out int images,
                              out int sprites, out int ratios)
    {
        widgets = 0;
        images = 0;
        sprites = 0;
        ratios = 0;
        for (int i = 0; i < 6; i++)
        {
            _ringSprites[i] = null;
            _ringColors[i] = Color.white;
            _ringRatio[i] = RingRatioFallback;
        }
        Dictionary<ElementInfusionBoardManager.EElement, InfusionElementUI>? ui = board.elementsUI;
        if (ui == null)
            return;
        for (int i = 0; i < 6; i++)
        {
            if (!ui.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI one) || one == null)
                continue;
            widgets++;
            Image ring = one.availableHighlight;
            if (ring == null)
                continue;
            images++;
            if (!Usable(ring.sprite))
                continue;
            _ringSprites[i] = ring.sprite;
            _ringColors[i] = ring.color;
            sprites++;

            Image disc = one.elementImage;
            if (disc == null)
                continue;
            RectTransform ringRect = ring.rectTransform;
            RectTransform discRect = disc.rectTransform;
            if (ringRect == null || discRect == null)
                continue;
            float ringAxis = Mathf.Max(ringRect.rect.width, ringRect.rect.height);
            float discAxis = Mathf.Max(discRect.rect.width, discRect.rect.height);
            if (discAxis <= 0.0001f || ringAxis <= 0.0001f
                || float.IsNaN(ringAxis) || float.IsNaN(discAxis)
                || float.IsInfinity(ringAxis) || float.IsInfinity(discAxis))
                continue;
            _ringRatio[i] = ringAxis / discAxis;
            ratios++;
        }
    }
}

/// <summary>
/// The per-frame pump for one strip's chip transitions — the element-board twin of
/// <c>RemoteBoardCard.MaterialisePump</c>, and it exists for the identical reason: the strip is
/// plain C# and its owner repaints it on a 4 Hz content cadence, which cannot carry a 0.3 s ramp.
/// It rides the strip root, so a board that stops being drawn stops ticking.
/// </summary>
internal sealed class ElementTransitionPump : MonoBehaviour
{
    /// <summary>The strip this pump drives. Assigned once, at build time.</summary>
    internal RemoteElementStrip? Strip;

    private void Update() => Strip?.TickTransitions();
}
