using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Element infusions ("Elemente") — GLOBAL
// =================================================================================================

/// <summary>
/// ModBuild 486: native widgets are the sole presentation. Message10/record52 supplies the OWNER'S
/// rendered element state, original animation outputs, FX values and fitted geometry. The original
/// local singleton supplies matching native art and binding targets only. RemoteNativeElements
/// applies owner output after mirror/layout writes; viewer-local availability or animation phase
/// cannot substitute for the owner's picture. The historical GLOBAL and procedural-composition
/// discussion below documents retired paths, retained only as uncalled diagnostic/source history.
/// It is not an availability fallback. The remote-only initiative badge is permanently hidden.
///
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
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
///  THE WHITE SQUARE, ROUND TWO — IT WAS NOT A DEAD TEXTURE, IT WAS "WIRD ERSTELLT"
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
/// The ladder above shipped in ModBuild 445 and the user saw the square again on 447, this time
/// with the observation that decides it: <i>"Bei mir steht im Text 'wird erstellt' mit einer
/// Animation in dem Moment in dem dieses weiße Viereck zu sehen ist — das will ich auch beim remote
/// board genau so sehen (1:1 Regel)."</i>
///
/// <b>THE 447 LOGS FALSIFY THE DEAD-TEXTURE HYPOTHESIS OUTRIGHT.</b> Both machines printed one
/// resolve line and it reads <c>STRONG 6/6, WANING 6/6, CREATION 6/6; every state has usable
/// authored artwork</c>, and NEITHER log carries a single "cannot be drawn" line from
/// <see cref="LogFallback"/>. So the ladder never stepped down: rung ONE drew, with a live sprite
/// on a live texture, and the picture was still a white square. A second dead-atlas site cannot be
/// the cause of a square painted by a rung that reported healthy artwork.
///
/// <b>WHAT IT ACTUALLY IS.</b> The owner does not see "the creation disc". Vanilla's creating cell
/// (<c>InfusionElementUI.SetState</c>, case <c>Inert</c> with <c>isCreating</c>) is a COMPOSITION:
/// <c>elementImage</c> off, <c>creationImage</c> on wearing <c>creationIcon</c> AT THE PREFAB'S OWN
/// TINT AND ALPHA, the <c>creatingElementText</c> label ("wird erstellt") over
/// <c>creationTextBackgroundImage</c>, a <c>creationBumpImage</c>, and <c>animatorCreating</c> +
/// <c>loopAnimatorCreating</c> running. This strip drew exactly ONE of those terms — the raw
/// <c>creationIcon</c>, at <c>Color.white</c>, opaque, at full chip size. An artwork authored to be
/// tinted and composited, drawn untinted and alone, is a flat pale block; that is the reported
/// square, and it is why it appears exactly when the owner's cell says "wird erstellt".
///
/// <b>THE REMEDY IS THE MIRROR, NOT A BLANK.</b> Blanking the chip would satisfy the first half of
/// the report and breach the 1:1 rule on the second, which is the half the user actually specified.
/// So the creating cell is now MEASURED OFF THE LIVE WIDGET, exactly as the availability ring
/// already was (<see cref="ResolveRings"/>): the disc's authored sprite AND colour AND its size
/// relative to <c>elementImage</c>, and the label's string, colour, size and CENTRE OFFSET, and its
/// background sprite, colour and size — all as ratios of the element disc, so the mirror keeps the
/// authored proportion at whatever chip size this strip runs at. See <see cref="CreatingCell"/> and
/// <see cref="ResolveCreatingCells"/>. Zero wire: same singleton, same global state as everything
/// else this class reads.
///
/// <b>AND A MEASURED GUARD SO NO RUNG CAN EVER PAINT A WHITE BLOCK AGAIN.</b> The 445 guard tested
/// the sprite's <c>texture</c> for liveness — a STATE probe, and it passed a healthy sprite whose
/// drawn RESULT was still a white square. <see cref="InkOf"/> measures the PICTURE instead: an 8×8
/// downsample of the sprite region that actually reaches the renderer (post-<see cref="Baked"/>, so
/// a bad mip bake is covered too), giving mean RGBA and the spread across the rect. A draw whose
/// artwork is a featureless near-white block AND whose tint is near-white and opaque is refused at
/// the single choke point every rung goes through (<see cref="TryApplyIcon"/>) and steps down to
/// the generic element glyph, with the measured numbers on the record. This is the project's own
/// "measure the picture, not the state" rule applied to the instrument that missed it.
///
/// <b>THE ANIMATION.</b> <c>animatorCreating</c> / <c>loopAnimatorCreating</c> are authored
/// <c>GUIAnimator</c> / <c>LoopAnimator</c> curves living in prefab scene data this mod cannot
/// read — the same wall the CREATED pop already hit. The user asked for an animation in so many
/// words, so the creating cell breathes on a house curve (<see cref="CreationBreathSeconds"/>), and
/// that substitution is stated rather than hidden: the mirror says "this element is forming", on
/// the mod's own rhythm, which is a truthful statement of an event the owner really does see move.
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
///  ROUND THREE — "ES SOLL NICHT NACHGEBAUT WERDEN": THE MIRROR, NOT THE REBUILD
/// ═══════════════════════════════════════════════════════════════════════════════════════════════
/// <i>"Das 'Wird erstellt' sieht nicht identisch aus. Es soll nicht nachgebaut werden (was du
/// vermutlich gemacht hast) sondern 1:1 genau so aussehen wie für den Spieler auch … Auch ist es bei
/// den remote boards manchmal komplett weg."</i> (2026-09-05, element_diskrepanz1/2.jpg.) He is
/// right about what the block above did, and the screenshot shows three separate misses in one
/// frame: the caption in the wrong FACE and the wrong COLOUR (a default sans in orange, against the
/// owner's small-caps serif in white), a plain WHITE BAR where the disc belongs, and no disc.
///
/// <b>THE 448 LOGS NAME THE MECHANISM, AND IT IS THE APPROACH AND NOT A BUG IN IT.</b> Both clients
/// printed, for a chip in creation:
/// <code>
///   Ice OWNER … creation=on 'IceCreating' (1.00,1.00,1.00) a0.09
///     | MIRROR chip=shown state=CREATING rung=AUTHORED-DISC art='IceCreating (VR-mip)' a0.00 plate=on
/// </code>
/// The owner's number MOVES (0.09 on one client, 0.43, 0.31, 0.11) because
/// <c>animatorCreating</c> / <c>loopAnimatorCreating</c> are driving it; the mirror's is a hard
/// <c>0.00</c>, because <c>ResolveCreatingCells</c> captured <c>creationImage.color</c> ONCE, at
/// resolve time — when nothing was in creation and the animator was parked at its init state, i.e.
/// invisible. So the disc was drawn at alpha zero and the "white square" the user photographed was
/// never the disc at all: it is the caption's own background plate, drawn opaque because its colour
/// was latched from the same parked widget. Measuring an authored tint off a widget whose tint is
/// ANIMATOR STATE cannot be made to work by measuring it better.
///
/// <b>AND SetState PROVES THERE IS NOTHING ELSE TO MEASURE.</b> Read the vanilla creating branch
/// (decompiled/GH.Runtime/InfusionElementUI.cs:102-117, case <c>Inert</c>): it writes
/// <c>effectsControl.ToggleEnable(true)</c>, <c>elementImage.enabled = false</c>,
/// <c>creationImage.enabled = isCreating</c>, the tooltip text and <c>SetAvailable(false)</c>, and
/// calls <c>ShowCreating()</c> only when <c>lastState != newState</c>. (An earlier version of this
/// sentence counted "exactly ONE field"; the count was wrong, the point stands.) It never touches
/// <c>creatingElementText</c>, <c>creationTextBackgroundImage</c> or <c>creationBumpImage</c>.
/// Their visibility, their alphas, their positions — the whole picture — are authored GUIAnimator
/// curves in prefab scene data. A composition can only ever be a guess at them.
///
/// <b>SO THE STRIP MIRRORS THE WIDGET.</b> Rung ZERO is now <see cref="RemoteWidgetMirror"/> against
/// <c>InfusionBoardUI.Instance.transform</c> — the identical demotion the initiative track and the
/// objectives panel already went through when the user rejected THEIR mod-drawn stand-ins, and the
/// same answer to the same complaint. <c>Pair.Apply</c> copies each node's active flag, rect pose,
/// <c>Graphic.enabled</c>, colour, sprite, TMP text and CanvasGroup alpha every frame and SHARES the
/// animated material instance, so all of it arrives at once and none of it is guessed:
/// <list type="bullet">
/// <item>the caption's FACE and its per-element glow material — <c>Awake</c> clones
///   <c>createElementText.fontSharedMaterial</c> and <c>Init</c> writes the element colour into its
///   glow; <c>Object.Instantiate</c> copies both references, which is why the clone reads in the
///   game's own small-caps face and not in a mod-built TMP default;</item>
/// <item>every animator-driven alpha and position, including the creation disc's pulse and the
///   caption plate's — the terms the composition latched at zero and at opaque white;</item>
/// <item>the two <c>UIFX</c> quads, which the composition explicitly did not reproduce:
///   <c>UIFX_MaterialFX_Control</c> instantiates its own material and animates <c>_FXAnim</c> on
///   it, and <c>Pair.CopyMaterial</c> shares that very instance, so the effect plays on the mirror
///   for free and with no second animator;</item>
/// <item>RESERVED and the whole draw-list rule, which vanilla expresses as
///   <c>gameObject.SetActive</c> and the drive copies verbatim.</item>
/// </list>
/// The composition below is KEPT, whole, as the fallback for a client with no infusion board up
/// (menu / loading) — and as the answer to "es darf niemals leere Fenster geben": a clone that is up
/// and driving but has put no ink on the board for three content ticks while the owner's board is
/// showing elements is demoted back to it. That test is a MEASURE of the clone's own graphics
/// (<see cref="MirrorInk"/>), never "the clone exists", because "the clone exists" is the same
/// species of claim as the <c>chip=shown</c> that 448 printed over a disc at alpha zero.
///
/// <b>THE SECOND HALF: "manchmal komplett weg".</b> A separate cause, and the cadence is it. The
/// composition repainted only from <see cref="Refresh"/>, which the board calls at 4 Hz behind a
/// signature gate — while the creating state is a window <c>InfusionBoardUI.UpdateBoard</c> closes
/// the instant the column goes non-inert. An infusion that resolves inside one 250 ms period is
/// therefore never sampled in creation at all, and the mirror shows nothing where the owner saw a
/// caption and a pulse. The clone is driven from <see cref="TickLive"/> instead, per FRAME, off the
/// same widget the owner is watching, so there is no sampling window left to fall through.
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
/// <b>THIS FILE USED TO SAY: "REJECTED: routing the real <c>InfusionBoardUI</c> through
/// <see cref="RemoteWidgetMirror"/> — a mirrored WIDGET would have carried the local hover, tooltip
/// target and GUIAnimator run state onto every peer's board, and those really are the viewer's."
/// EVERY CLAUSE OF THAT SENTENCE WAS WRONG,</b> and the block below is what replaced it. The hover
/// and the tooltip target are components, and <c>RemoteWidgetMirror.Neutralize</c> DESTROYS every
/// component that is not pure presentation before the clone ever wakes — <c>UITextTooltipTarget</c>
/// among them — so neither can ride a clone anywhere. And the GUIAnimator run state is not the
/// viewer's at all: there is one infusion board per SCENARIO, the animators are started from
/// <c>SetState</c>, which is driven by the same host-replicated messages this class already reads,
/// so the run state is exactly as global as the column it animates. It is, in fact, the one thing
/// this strip most needed and could not otherwise have.
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

    /// <summary>Verbatim <c>ElementBoardSurface.DensityScale</c> — the per-panel multiplier on the
    /// shared tray density this dock (and only this dock) applies to the game's own element board.
    /// A local copy for the reason every other remote-board constant is one: WorldUI's protected
    /// override cannot be read from here, and a mirrored panel drawn at another density is not a
    /// mirror. <see cref="RemoteObjectivesPanel"/> carries the same copy for its own dock.</summary>
    private const float ElementDensityScale = 0.8f;

    /// <summary>
    /// THE GAME'S OWN ELEMENT BOARD, CLONED AND LIVE-DRIVEN — rung ZERO, ahead of every rung the
    /// composition below still has. See THE MIRROR, NOT THE REBUILD in the class doc.
    /// </summary>
    private readonly RemoteWidgetMirror _mirror;
    private readonly RemoteNativeElements _native = new();

    internal void SetNativeState(NativeBoardState? state, List<NativeBoardState> history) =>
        _native.SetState(state, history);

    /// <summary>Which peer's board this strip is on. Diagnostic only — it is what lets the parity
    /// line's one-shot key be PER BOARD, so a second board's empty strip cannot be swallowed by a
    /// first board's identical-looking verdict. See <see cref="EmitParity"/>.</summary>
    private readonly int _playerId;

    /// <summary>Which mechanism the last <see cref="Refresh"/> drew with — the clone, the mod-drawn
    /// composition, or nothing at all. Reported by <see cref="EmitParity"/>.</summary>
    private RemoteWidgetMirror.Fidelity _drawnBy = RemoteWidgetMirror.Fidelity.None;
    private RemoteBoardRecoveryEdge _nativeRecovery;
    internal bool HasMissingClone => _drawnBy == RemoteWidgetMirror.Fidelity.MirroredWidget && !_mirror.HasLiveClone;
    internal bool NeedsNativeRecovery => _nativeRecovery.Pending || _drawnBy != RemoteWidgetMirror.Fidelity.MirroredWidget;

    /// <summary>Consecutive content ticks on which the clone was up and driving but put NO ink on
    /// the board while the owner's own element board was showing something. See the demotion in
    /// <see cref="Refresh"/>.</summary>
    private int _mirrorBlankTicks;
    private float _nextBlankProbeAt;

    /// <summary>Latched by that demotion; cleared by an empty board or a clone rebuild.</summary>
    private bool _mirrorBlanked;

    /// <summary>The MIRRORED path's parity gate — the owner's discrete state (<c>sig</c>) times four
    /// plus the clamped blank count. Its own gate because that path runs on every content tick
    /// rather than only on a repaint. The product cannot wrap: <c>sig</c> is bounded by
    /// 3^6 · 64^3 = 191,102,976 and four times that is well inside <c>int.MaxValue</c>.</summary>
    private int _paritySig = -1;

    /// <summary>One-shot for the demotion line — a mechanism change is worth stating once, and a
    /// board that keeps flapping would otherwise say it every 0.75 s.</summary>
    private static bool s_demotionLogged;

    /// <summary><see cref="RemoteWidgetMirror.RebuildStamp"/> as of the last tick — a rebuilt clone
    /// is a new object and must not inherit the previous one's blank verdict.</summary>
    private int _mirrorStamp = -1;

    /// <summary>How long a mirror may draw nothing over a non-empty board before the mod-drawn
    /// composition takes over: 3 content ticks, i.e. 0.75 s at the board's 4 Hz cadence. Long enough
    /// that a single tick caught mid-rebuild cannot flap the two mechanisms, short enough that a
    /// blank dock is never something a player has time to read as "the elements are gone".</summary>
    private const int BlankTicksBeforeFallback = 3;

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

    // ------------------------------------------------------------------ the "wird erstellt" cell --

    /// <summary>
    /// The owner's CREATING cell for one element, measured off the live
    /// <c>InfusionElementUI</c> — the mirror of what vanilla's <c>SetState(Inert, isCreating: true)</c>
    /// composes. Every size and offset is expressed as a RATIO of the element disc's own rect
    /// (<c>elementImage</c>), so the mirror keeps the authored proportion at whatever
    /// <see cref="ChipSize"/> this strip runs at — the same rule <see cref="ResolveRings"/> uses for
    /// the availability ring, for the same reason: a guessed proportion is a second opinion about
    /// something the widget already states.
    ///
    /// <para><see cref="Measured"/> false means the widget could not be read (no singleton, a zero
    /// rect, a throw). The cell then falls back to <see cref="DiscRatioFallback"/> and the label is
    /// laid out from <see cref="TextSizeFallback"/> / <see cref="TextAtFallback"/> — a stand-in for
    /// the authored layout, not a shipped proportion, and the resolve line says which one is
    /// standing.</para>
    /// </summary>
    private struct CreatingCell
    {
        /// <summary>True once the live widget answered with a usable rect for the element disc.</summary>
        public bool Measured;

        /// <summary>The authored <c>creationImage</c> sprite.</summary>
        public Sprite? Disc;

        /// <summary>Its PREFAB tint and alpha. Drawing this untinted at full opacity is what
        /// painted the reported white square.</summary>
        public Color DiscColor;

        /// <summary>Its size as a fraction of the element disc's own rect.</summary>
        public float DiscRatio;

        /// <summary>The authored <c>creatingElementText</c> — the localized "wird erstellt" string
        /// as the OWNER's own client composed it, which is also why this mirror needs no EN/DE
        /// string of its own.</summary>
        public string? Text;

        /// <summary>The label's authored colour.</summary>
        public Color TextColor;

        /// <summary>The label's box, in disc-axis units.</summary>
        public Vector2 TextSize;

        /// <summary>The label's CENTRE offset from the disc's centre, in disc-axis units. Centres,
        /// not <c>anchoredPosition</c>: an anchored position runs to each rect's own pivot, so two
        /// siblings' anchored positions are not comparable — this project has that filed under its
        /// own name.</summary>
        public Vector2 TextAt;

        /// <summary>The authored <c>creationTextBackgroundImage</c> behind the label. A null sprite
        /// leaves the plate off; a mirror cannot invent a shape.</summary>
        public Sprite? Plate;

        /// <summary>The plate's authored tint.</summary>
        public Color PlateColor;

        /// <summary>The plate's box, in disc-axis units.</summary>
        public Vector2 PlateSize;

        /// <summary>The plate's centre offset from the disc's centre, in disc-axis units.</summary>
        public Vector2 PlateAt;
    }

    private readonly CreatingCell[] _cells = new CreatingCell[6];

    /// <summary>The creation disc:element disc size ratio used when the authored rects cannot be
    /// measured. A FALLBACK, not a shipped proportion — see <see cref="CreatingCell.Measured"/>.
    /// 1.0 because vanilla swaps one Image for the other inside the same cell.</summary>
    private const float DiscRatioFallback = 1.0f;

    /// <summary>
    /// Label box when the authored rect cannot be measured, in disc-axis units.
    ///
    /// <para>NOT A GUESS — taken from the 447 hardware capture of the owner's own panel:
    /// <c>Host rect fit 'GloomhavenVR.Panel_ElementBoard'</c> reports the creating cell's
    /// <c>Creating icon/CreatingText</c> as 199x50 px beside a 52x52 px <c>Element icon</c>, i.e.
    /// 3.83 x 0.96 disc-axis units. It is still only the FALLBACK: the live measurement in
    /// <see cref="ResolveCreatingCells"/> is what normally decides, and these numbers stand in when
    /// that measurement cannot run.</para>
    /// </summary>
    private static readonly Vector2 TextSizeFallback = new(3.83f, 0.96f);

    /// <summary>
    /// Label centre offset from the disc centre when the authored rect cannot be measured, in
    /// disc-axis units.
    ///
    /// <para>Same capture, same line: the caption's rect runs x −242..−43 against a disc centred at
    /// 0, so it sits BESIDE the disc on the left and vertically centred on it — not under it, which
    /// is what a mirror written from intuition would have assumed.</para>
    /// </summary>
    private static readonly Vector2 TextAtFallback = new(-2.74f, 0f);

    /// <summary>The "wird erstellt" label per chip, and the authored plate behind it. Built at
    /// board-build time like every other renderer here (<c>VRLayers.Apply</c> runs over the finished
    /// board ONCE — a renderer minted later is invisible to the mod head camera), and left disabled
    /// until an element is actually in creation.</summary>
    private readonly TextMeshPro[] _labels = new TextMeshPro[6];

    /// <summary>The plate behind the label — see <see cref="_labels"/>.</summary>
    private readonly SpriteRenderer[] _plates = new SpriteRenderer[6];

    /// <summary>Whether chip <c>i</c> is currently drawing the CREATING cell — the gate for the
    /// breath in <see cref="ApplyChip"/> and the reason <see cref="TickTransitions"/> keeps ticking
    /// a chip that has no ramp running.</summary>
    private readonly bool[] _creatingNow = new bool[6];

    /// <summary>
    /// Period of the creating BREATH, seconds. The owner's <c>loopAnimatorCreating</c> is a prefab
    /// curve this mod cannot read (see the class doc), and the user asked for the animation in so
    /// many words, so the cell pulses on the mod's own rhythm instead of standing still.
    ///
    /// <para>SLOW ON PURPOSE. This project has a ledger of pulses the user called "nervig"; a
    /// 1.8 s period is a breath, not a flicker, and the floor below never takes the cell far enough
    /// down to read as a dropout.</para>
    /// </summary>
    private const float CreationBreathSeconds = 1.8f;

    /// <summary>How far down the breath dips the creating cell's opacity (1 = untouched).</summary>
    private const float CreationBreathFloor = 0.62f;

    /// <summary>The ALPHA the last repaint wrote into chip <c>i</c>'s ICON — the sprite path's twin
    /// of <see cref="_quadBaseAlpha"/>, and it exists because the creating disc now carries the
    /// PREFAB'S OWN alpha. The class used to argue it needed no twin, on the grounds that the one
    /// dim in the sprite path was expressed in RGB; mirroring an authored transparency is not a dim
    /// and cannot honestly be folded into a colour, so the twin is here and
    /// <see cref="ApplyChip"/> stays the sole owner of every renderer's final alpha.</summary>
    private readonly float[] _iconBaseAlpha = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>The box the caption was last FIT to, world metres — the change gate in front of
    /// <c>TmpFit.Fit</c>, which forces a mesh update and is not free at a 4 Hz repaint.</summary>
    private readonly Vector2[] _labelBox = new Vector2[6];

    /// <summary>Which rung of the draw ladder chip <c>i</c> landed on at the last repaint, indexed
    /// into <see cref="RungNames"/> — the MIRROR half of the parity line. Recorded rather than
    /// re-derived: a second derivation of the same decision would be a claim measuring itself.
    /// </summary>
    private readonly int[] _rung = new int[6];

    /// <summary>The name of the sprite chip <c>i</c> actually handed its renderer, or null when it
    /// drew no sprite at all. The other half of the same record.</summary>
    private readonly string?[] _rungArt = new string?[6];

    /// <summary>Rung names for the parity line, indexed by <see cref="_rung"/>.</summary>
    private static readonly string[] RungNames =
        { "not-drawn", "AUTHORED-DISC", "GENERIC-GLYPH", "COLOUR-CHIP" };

    /// <summary>Every distinct owner-vs-mirror verdict already on the record — see
    /// <see cref="EmitParity"/>. Capped per VERDICT, never per call, so a new combination is never
    /// swallowed by an old one; STATIC because the infusion table is scenario-wide and every peer
    /// board in the room would otherwise print the same verdict once per peer.</summary>
    private static readonly HashSet<string> s_parityLogged = new HashSet<string>();

    /// <summary>Ceiling on distinct parity verdicts, so a pathological state churn cannot turn the
    /// line into a flood. When it is reached the fact is stated once, rather than the line simply
    /// going quiet — a cap that goes silent is its own defect.</summary>
    private const int MaxParityVerdicts = 40;

    /// <summary>Set once the verdict cap above has been reported.</summary>
    private static bool s_parityCapped;

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

    /// <summary>Base-6 fold of the six element indices in the game's own row order — the second
    /// half of the repaint gate. The gate is an AND of the two, so every existing
    /// <c>_signature = -1</c> invalidation still forces a full repaint on its own (a base-6 fold is
    /// never negative and can never match -1); this field needs no invalidation of its own.</summary>
    private int _orderSignature = -1;

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

    public RemoteElementStrip(int playerId, Transform boardRoot, in RemoteBoardLayout layout)
    {
        _playerId = playerId;
        // MOUNT (position + authored per-board scale, exactly like PlayTray.BuildMounts sets its
        // own element mount) …
        var mount = new GameObject("ElementMount").transform;
        mount.SetParent(boardRoot, worldPositionStays: false);
        mount.localPosition = layout.ElementMount;
        mount.localScale = Vector3.one * layout.ElementScale;

        // RUNG ZERO — THE GAME'S OWN ELEMENT BOARD, CLONED. Same mount, and the same three dock
        // numbers ElementBoardSurface docks the OWNER's copy of this very panel with
        // (PlayTray.ElementMountWidth x ElementMountMaxHeight, growing LEFT off the mount origin,
        // at that surface's own 0.8 density) — a mirror fitted with different numbers is not a
        // mirror. fitWidth stays at the shared default because ElementBoardSurface does not
        // override FitWidthToMount; the objectives dock's `false` is specific to a panel whose
        // content is FORCED to its width budget, which this one's is not.
        _mirror = new RemoteWidgetMirror("ElementBoard", mount,
            PlayTray.ElementMountWidth, PlayTray.ElementMountMaxHeight, Vector2.left,
            densityScale: ElementDensityScale,
            backingContentRoot: source => source.GetComponent<InfusionBoardUI>()?.elementsHolder);

        // … and the FALLBACK strip, half a dock width to the LEFT of it — the mount's origin is
        // RIGHT-centre growing left (the objectives convention), so the shift belongs INSIDE the
        // mount, where the scale applies to it too.
        _root = new GameObject("Elements").transform;
        _root.gameObject.SetActive(false); // retired surrogate: never visible, including construction failure
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

            // THE "WIRD ERSTELLT" CELL — the plate first, then the label over it, both PROUD of the
            // disc (-Z, the module convention) because vanilla's own creating cell draws its caption
            // over the strip and calls SetAsLastSibling to say so. Built here, disabled, for the
            // reason everything else here is built here: VRLayers.Apply runs over the finished board
            // once, and a renderer minted later is invisible to the mod head camera.
            var plateGo = new GameObject("CreatingPlate");
            plateGo.transform.SetParent(chip, worldPositionStays: false);
            plateGo.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            var plate = plateGo.AddComponent<SpriteRenderer>();
            plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plate.receiveShadows = false;
            plate.enabled = false;
            _plates[i] = plate;

            var labelGo = new GameObject("CreatingLabel");
            labelGo.transform.SetParent(chip, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.text = string.Empty;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.enabled = false;
            _labels[i] = label;

            ResetCell(i);

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
            {
                // THE CREATING BREATH. A chip with no ramp running still MOVES while its element is
                // forming, which is the animation the user asked to see mirrored. ApplyChip owns the
                // breath, so re-applying the settled point is the whole of it. Free for every chip
                // that is not creating.
                if (_creatingNow[i] && _chips[i].gameObject.activeSelf)
                    ApplyChip(i, 1f);
                continue;
            }
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
    /// <para>The scale rides the CHIP ROOT, whose children (the fallback quad, the sprite icon, the
    /// availability ring, and the creating cell's plate and caption) already carry their own fitted
    /// scales — so the settle multiplies onto whichever of them is rendering without any of them
    /// needing to know about it. EVERY alpha is written even though only some renderers are
    /// enabled, for the same reason <c>RemoteBoardCard.ApplyMaterialise</c> writes all three of its
    /// materials: the repaint can switch which path draws while a ramp is running, and the others
    /// must not be sitting at full opacity when it does. Each renderer that is not opaque to begin
    /// with is scaled off its own AUTHORED base rather than off 1 — <c>_quadBaseAlpha</c>,
    /// <see cref="_iconBaseAlpha"/>, <c>_ringColors</c>, and the creating cell's two authored
    /// colours.</para>
    /// </summary>
    private void ApplyChip(int i, float visible)
    {
        float a = Mathf.Clamp01(visible);
        // THE CREATING BREATH rides ON TOP of the ramp rather than beside it, so ONE method still
        // owns every renderer's final alpha — the reason this method writes all of them at once.
        float ca = a * (_creatingNow[i] ? Breath() : 1f);
        _chips[i].localScale = Vector3.one * Mathf.Lerp(ChipSettleScale, 1f, a);
        Color quad = _mats[i].color;
        quad.a = _quadBaseAlpha[i] * ca;
        _mats[i].color = quad;
        Color icon = _icons[i].color;
        icon.a = _iconBaseAlpha[i] * ca;
        _icons[i].color = icon;
        Color ring = _ringColors[i];
        ring.a *= a;
        _rings[i].color = ring;
        // The label and its plate belong to the creating cell alone, so they take the same ramp AND
        // the same breath as the disc — the three move as one, which is what the owner's animator
        // does with them. Both read their AUTHORED colour as the base, for the reason _ringColors
        // does: neither is opaque to begin with.
        if (_plates[i].enabled)
        {
            Color plate = _cells[i].PlateColor;
            plate.a *= ca;
            _plates[i].color = plate;
        }
        if (_labels[i].enabled)
        {
            Color text = _cells[i].TextColor;
            text.a *= ca;
            _labels[i].color = text;
        }
    }

    /// <summary>
    /// The creating cell's opacity multiplier this frame: a raised cosine between
    /// <see cref="CreationBreathFloor"/> and 1 over <see cref="CreationBreathSeconds"/>.
    ///
    /// <para>UNSCALED TIME, like every other clock in this file — the element board moves during
    /// card phases, which pause <c>timeScale</c>. A FIXED period, deliberately: this project has a
    /// bug class filed for animations whose frequency is scaled by a strength term, and there is no
    /// strength here to scale it with.</para>
    /// </summary>
    private static float Breath()
    {
        // No zero-period guard: the period is a compile-time constant above zero, and a guard the
        // compiler can prove dead is a warning, not a safety net.
        float phase = Mathf.Repeat(Time.unscaledTime, CreationBreathSeconds) / CreationBreathSeconds;
        float wave = 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
        return Mathf.Lerp(CreationBreathFloor, 1f, wave);
    }

    /// <summary>Re-read the infusion table AND the three overlay masks, and repaint on an actual
    /// change to any of them.</summary>
    public void Refresh()
    {
        // The local surface displays only InfusionBoardUI. Keep the original clone in charge
        // even if a transient blank sample cannot be measured; model-derived chips are a different
        // picture and must not replace native art or interrupt its running animation.
        bool mirrored = TryMirror();
        _drawnBy = mirrored ? RemoteWidgetMirror.Fidelity.MirroredWidget
                           : RemoteWidgetMirror.Fidelity.None;
        _root.gameObject.SetActive(false);
        int infused = 0;
        var state = new ElementInfusionBoardManager.EColumn[6];
        int signature = mirrored ? 1 : 0;
        for (int i = 0; i < 6; i++)
        {
            try { state[i] = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { state[i] = ElementInfusionBoardManager.EColumn.Inert; }
            if (state[i] != ElementInfusionBoardManager.EColumn.Inert) infused++;
            signature = signature * 3 + (int)state[i];
        }
        ActiveCount = infused;
        ReadOverlay(out int creating, out int reserved, out int available);
        signature = ((signature * 64 + creating) * 64 + reserved) * 64 + available;
        if (signature != _paritySig)
        {
            _paritySig = signature;
            EmitParity(state, creating, reserved, available);
        }
    }

    // Historical fallback and its diagnostic tokens are retained for log/source compatibility;
    // no presentation entry point invokes it. Native recovery never demotes to this composition.
    private void RefreshLegacyComposition()
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

        // THE ROW ORDER IS PART OF THE PICTURE AND THEREFORE PART OF THE GATE. It gets its OWN
        // signature rather than being folded into the one above, and that is arithmetic and not
        // taste: `sig` is already up to 729 * 64^3 = 191,102,976 and a permutation of six adds a
        // factor of 720, which overflows int outright. Base-6 over six digits is 46,656 states and
        // fits with room to spare.
        //
        // It cannot be left out on the grounds that a reorder always comes with a `creating` bit
        // change. It does today — SetAsLastSibling fires from the Inert branch, and UpdateBoard
        // only passes isCreating true for elements in elementsInCreation — but that is two game
        // files happening to agree, not an invariant this file may rest a repaint gate on.
        int[] order = ReadRowOrder();
        int orderSig = 0;
        for (int k = 0; k < 6; k++)
            orderSig = orderSig * 6 + order[k];

        // ═══ RUNG ZERO: MIRROR THE REAL BOARD ═══════════════════════════════════════════════════
        // Attempted on EVERY content tick and deliberately OUTSIDE the signature gate. The clone's
        // own rebuild test is a STRUCTURE test, not a state test, and the state this class gates on
        // is not the state the creating cell is made of: vanilla drives that cell's alphas,
        // positions and enabled flags from GUIAnimator curves that no column value can predict. A
        // mirror behind an infusion-table gate would freeze mid-animation.
        ActiveCount = infused;

        // Is there anything for a mirror to be WRONG about? The game's own draw rule, so this is the
        // same population EmitParity calls `ownerDraws` — see the class doc for why a RESERVED
        // element is excluded (it is hidden on both sides by construction).
        bool ownerHasPicture = false;
        for (int i = 0; i < 6; i++)
        {
            if ((state[i] != ElementInfusionBoardManager.EColumn.Inert
                 || (creating & (1 << i)) != 0)
                && (reserved & (1 << i)) == 0)
            {
                ownerHasPicture = true;
                break;
            }
        }
        if (!ownerHasPicture || _mirrorStamp != _mirror.RebuildStamp)
        {
            // A CLEAN SLATE RE-ARMS THE MIRROR. Latching the demotion below for the life of the
            // board would turn one bad frame into a permanently mod-drawn strip; an empty board and
            // a freshly rebuilt clone are both moments where the old verdict has stopped being
            // about anything.
            _mirrorStamp = _mirror.RebuildStamp;
            _mirrorBlankTicks = 0;
            _nextBlankProbeAt = 0f;
            _mirrorBlanked = false;
        }

        if (!_mirrorBlanked && TryMirror())
        {
            _drawnBy = RemoteWidgetMirror.Fidelity.MirroredWidget;
            _mirrorStamp = _mirror.RebuildStamp;
            bool blank = ownerHasPicture && !MirrorHasPicture();
            if (!blank)
                _mirrorBlankTicks = 0;
            else if (Time.unscaledTime >= _nextBlankProbeAt)
            {
                // Content now refreshes on real state edges. The diagnostic's three samples
                // must not become three frames merely because several changes arrived together.
                _nextBlankProbeAt = Time.unscaledTime + RemoteBoardContent.DefaultRefreshSeconds;
                _mirrorBlankTicks++;
            }
            if (_mirrorBlankTicks < BlankTicksBeforeFallback)
            {
                // The composition must repaint FROM SCRATCH if the mirror ever falls over, so the
                // signature is deliberately NOT consumed here — the same reason
                // RemoteObjectivesPanel clears its own gate on the mirrored path.
                _signature = -1;
                _transitionsSeeded = false;
                // …but the PARITY line does get a gate of its own, because this path runs every
                // content tick whether or not anything moved and its verdict string is not free to
                // build. `sig` is every DISCRETE fact about the owner's board (six columns plus the
                // creating / reserved / available masks); the animated numbers inside one state are
                // exactly what the verdict KEY already refuses to spend its budget on. The blank
                // count is in the gate too, so a mirror going dark under an unchanged board still
                // speaks.
                int gate = sig * 4 + Mathf.Min(_mirrorBlankTicks, 3);
                if (gate != _paritySig)
                {
                    _paritySig = gate;
                    EmitParity(state, creating, reserved, available);
                }
                return;
            }
            // "Es darf niemals leere Fenster geben." A clone that is up, paired and driving but has
            // put no ink on the board for three quarters of a second while the owner's board is
            // showing elements is not a mirror of anything, and the mod-drawn composition — however
            // approximate — beats a blank dock. Confirmed over several content ticks rather than
            // one, so a single frame caught mid-rebuild cannot flap the two mechanisms.
            _mirrorBlanked = true;
            _mirror.SetShown(false);
            _signature = -1;
            _paritySig = -1;  // a later re-promotion must re-state, not inherit this gate
            if (!s_demotionLogged)
            {
                s_demotionLogged = true;
                // HW-VERIFY: the DEMOTION event itself, once per session — the moment this strip
                // stopped mirroring and started composing, stated separately from the per-state
                // ELEMENT PARITY verdicts because a mechanism change is not a state change. Its
                // ABSENCE is the pass: a session whose parity lines all read via=MIRRORED-WIDGET and
                // that carries no line of this token never had to fall back. Its presence names the
                // board and the states involved, and the ELEMENT PARITY lines with MIRROR BLANK
                // immediately above it hold the per-element readback that decided it.
                VRLog.Note("Net", $"ELEMENT PARITY: board {_playerId} DEMOTED the mirrored element "
                                  + $"board to the mod-drawn composition — the clone was up and "
                                  + $"driving but put no ink on the strip for "
                                  + $"{BlankTicksBeforeFallback} content ticks while the owner's "
                                  + "own board was showing elements. Read the MIRROR BLANK lines "
                                  + "above for which element and which clone graphic was dark. "
                                  + "The strip re-arms the mirror the next time the board goes "
                                  + "empty or the clone is rebuilt, so this is not permanent.");
            }
        }
        _drawnBy = RemoteWidgetMirror.Fidelity.ModDrawn;
        if (!_root.gameObject.activeSelf)
            _root.gameObject.SetActive(true);

        if (sig == _signature && orderSig == _orderSignature)
            return;
        bool orderMoved = _orderSignature >= 0 && orderSig != _orderSignature;
        _signature = sig;
        _orderSignature = orderSig;
        if (orderMoved)
            LogRowOrder(order);

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
        //
        // AND IT COVERS THE CAPTION TOO. The "wird erstellt" cell hangs BELOW the chip run and is
        // wider than one chip, so a plate sized to the discs alone would have left the caption
        // floating over the passthrough room — the exact complaint the plate exists to answer, one
        // element further down. The rect is therefore accumulated as a UNION over the loop below
        // and written once after it; the seed is the run rect this paragraph describes, so a repaint
        // with nothing in creation produces byte-identical geometry to before.
        bool anyChips = visible > 0;
        if (_mrPlate.gameObject.activeSelf != anyChips)
            _mrPlate.gameObject.SetActive(anyChips);
        float padX = 0.005f;
        float runHalf = (visible - 1) * 0.5f * ChipStep + ChipSize * 0.5f;
        float plateMinX = -runHalf - padX;
        float plateMaxX = runHalf + padX;
        float plateMinY = -ChipSize * 0.5f - padX;
        float plateMaxY = ChipSize * 0.5f + padX;

        // Pack the visible chips left-to-right and centre the run, exactly like the game's own
        // horizontal element holder does with its layout group.
        //
        // IN THE HOLDER'S OWN CHILD ORDER, NOT IN EElement ORDER (R2 finding F14). Unity's
        // horizontal and grid layout groups seat ACTIVE children by sibling index and skip inactive
        // ones — which is the same "pack the visible ones" rule this loop uses, keyed on a
        // different order. The game permanently moves an element to the end of the holder when it
        // enters creation and never puts it back, so its row is a history of creations; dealing
        // slots by ascending `i` reproduced that row only until the first element was infused. See
        // ReadRowOrder for the decompiled citations. The loop BODY is untouched by this: everything
        // in it is keyed on the element index `i` and only `slot` runs in row order.
        float left = -(visible - 1) * 0.5f * ChipStep;
        int slot = 0;
        for (int k = 0; k < 6; k++)
        {
            int i = order[k];
            bool on = show[i];
            bool wasOn = _shownOn[i];
            bool wasInList = _wasInList[i];
            _shownOn[i] = on;
            _wasInList[i] = inList[i];
            if (!on)
            {
                // The creating cell is part of the CREATING state and nothing else, so it leaves
                // with the chip — including during a crumble, where the caption would otherwise
                // outlive the state it names.
                _creatingNow[i] = false;
                _rung[i] = 0;
                _rungArt[i] = null;
                HideCreatingCell(i);
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

            // ── THE OWNER'S "WIRD ERSTELLT" CELL ────────────────────────────────────────────────
            // Everything vanilla composes at SetState(Inert, isCreating: true) — the creation disc
            // AT ITS AUTHORED TINT AND SIZE, the caption, the caption's plate — is decided here,
            // ahead of the ladder, because it supplies the disc's own sprite, tint, alpha and size
            // for that state. Drawing the raw creationIcon untinted and alone is what painted the
            // reported white square; see the class doc.
            _creatingNow[i] = inCreation;
            Sprite? wantSprite = _sprites[i, want];
            var wantTint = Color.white;
            float wantAlpha = 1f;
            float wantSize = ChipSize;
            if (inCreation)
            {
                CreatingCell cell = _cells[i];
                if (Usable(cell.Disc))
                {
                    wantSprite = cell.Disc;
                    wantTint = cell.DiscColor;
                    wantAlpha = Mathf.Clamp01(cell.DiscColor.a);
                }
                wantSize = ChipSize * (cell.DiscRatio > 0.05f ? cell.DiscRatio : DiscRatioFallback);
                ApplyCreatingCell(i);
                // The MR backing has to reach under the caption as well — see the plate paragraph
                // above. Measured from the SEATED chip, so the union is in strip-local metres like
                // the seed rect.
                if (_labels[i].enabled || _plates[i].enabled)
                {
                    float cx = _chips[i].localPosition.x + cell.TextAt.x * ChipSize;
                    float cy = cell.TextAt.y * ChipSize;
                    float hw = Mathf.Max(cell.TextSize.x, _plates[i].enabled ? cell.PlateSize.x : 0f)
                               * ChipSize * 0.5f;
                    float hh = Mathf.Max(cell.TextSize.y, _plates[i].enabled ? cell.PlateSize.y : 0f)
                               * ChipSize * 0.5f;
                    plateMinX = Mathf.Min(plateMinX, cx - hw - padX);
                    plateMaxX = Mathf.Max(plateMaxX, cx + hw + padX);
                    plateMinY = Mathf.Min(plateMinY, cy - hh - padX);
                    plateMaxY = Mathf.Max(plateMaxY, cy + hh + padX);
                }
            }
            else
            {
                HideCreatingCell(i);
            }

            // THE DRAW LADDER. Three rungs, and the invariant across all of them is that no rung
            // may ever hand an UNDRAWABLE sprite to a SpriteRenderer — that is what paints the
            // white square. Every rung either draws real artwork or steps down, and every step
            // down goes on the record naming the element and the state. Since the 2026-09-05 round
            // "undrawable" is MEASURED and not merely tested for liveness: see TryApplyIcon.
            if (TryApplyIcon(i, wantSprite, wantSize, wantTint, wantAlpha, want))
            {
                _rung[i] = 1;
                // RUNG 1, THE REAL DISC. For STRONG and WANING that is the sprite itself, untinted
                // and at full chip size — the artwork carries its own look and vanilla swaps the
                // sprite rather than dimming or shrinking it. For CREATING it is the sprite AT THE
                // OWNER'S OWN TINT, ALPHA AND RELATIVE SIZE, decided just above: that state's
                // artwork is authored to be composited, and drawing it untinted is what produced
                // the reported white square.
                if (_quads[i].enabled)
                    _quads[i].enabled = false;
            }
            else if (TryApplyIcon(i, GenericIcon(i), want == 0 ? ChipSize : ChipSize * 0.74f,
                                  want == 0 ? Color.white : new Color(0.55f, 0.55f, 0.55f),
                                  1f, want))
            {
                _rung[i] = 2;
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
                _rung[i] = 3;
                _rungArt[i] = null;
                _iconBaseAlpha[i] = 1f;
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

            // A SETTLED chip's EndChipRamp is a no-op by design, so a creating cell this repaint
            // just switched ON would keep the previous frame's colours until the pump's next tick —
            // and if the pump is not running (a board root that is off), forever. One settled write
            // here closes that without adding a second alpha owner: ApplyChip still computes every
            // renderer's final value, this only asks it to.
            if (_creatingNow[i] && !_ramping[i])
                ApplyChip(i, 1f);
        }

        // THE MR PLATE, written once from the union accumulated above.
        if (anyChips)
        {
            _mrPlate.transform.localPosition =
                new Vector3((plateMinX + plateMaxX) * 0.5f, (plateMinY + plateMaxY) * 0.5f, 0.004f);
            _mrPlate.transform.localScale =
                new Vector3(plateMaxX - plateMinX, plateMaxY - plateMinY, 1f);
        }

        // Every element the strip is going to show this repaint has now been seeded once, so the
        // NEXT change is a real one and animates. See _transitionsSeeded.
        _transitionsSeeded = true;

        // THE 1:1 LINE. Last, because it reports what the paint above actually did.
        EmitParity(state, creating, reserved, available);
    }

    /// <summary>
    /// RUNG ZERO — clone the game's own element board and let it drive itself.
    ///
    /// <para>THE SOURCE IS THE <c>InfusionBoardUI</c> ROOT, verbatim
    /// <c>ElementBoardSurface.FindTarget()</c>, and the node identity is load-bearing rather than
    /// incidental. <see cref="RemoteWidgetMirror"/>'s EXACT measure path
    /// (<c>TryDockRect</c>) matches a live <c>ConvertedPanel</c> by <c>ReferenceEquals(p.Target,
    /// source)</c> and hands back the owner's OWN fitted host rect — "genau die gleiche Größe" by
    /// construction rather than by re-measurement. Handing it <c>elementsHolder</c> instead (the
    /// panel's <c>FitContentRoot</c>) would miss that match and drop the mirror onto the graphics-
    /// union fallback for the life of the session. Both mirrored panels this mod already ships pass
    /// their surface's <c>FindTarget</c> node for the same reason. Which path actually ran is stated
    /// in the mirror's own fit line ("measured via converted host rect" / "via graphics union"), so
    /// this claim is checkable in the next hardware log rather than assumed.</para>
    ///
    /// <para>WHAT THE CLONE CARRIES THAT NO COMPOSITION COULD. Everything the creating cell is made
    /// of is written by <c>animatorCreating</c> / <c>loopAnimatorCreating</c> —
    /// <c>InfusionElementUI.SetState</c> sets exactly ONE field on that path
    /// (<c>creationImage.enabled</c>) and never touches the caption, the caption plate or the bump
    /// image at all. <see cref="RemoteWidgetMirror.Pair.Apply"/> copies the source's LIVE graphic
    /// enabled flag, colour, sprite, rect pose, localScale and active flag every frame, and SHARES
    /// the animated material instance, so the animator's output arrives without the animator. That
    /// list also happens to contain every term the composition got wrong.</para>
    ///
    /// <para>Returns false while the original source, owner frame or safe native fit is unavailable.
    /// The normal refresh path retries; no procedural strip replaces the missing native content.</para>
    /// </summary>
    private bool TryMirror()
    {
        Transform? target = null;
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            target = board != null ? board.transform : null;
        }
        catch { target = null; }

        _native.Configure(_mirror);
        if (!_mirror.Refresh(target) && _mirror.CloneOf(target) == null)
        {
            _mirror.SetShown(false);
            return false;
        }

        _mirror.SetShown(true);
        if (InfusionBoardUI.Instance == null || !_native.Apply(_mirror, InfusionBoardUI.Instance)
            || !_mirror.Refresh(target))
        { _mirror.SetShown(false); return false; }
        // Refresh may synchronize viewer animation fields; owner output has the final write.
        if (!_native.Apply(_mirror, InfusionBoardUI.Instance))
        { _mirror.SetShown(false); return false; }
        // The mod-drawn strip and its MR plate go down as ONE object: the clone brings its own
        // MrBacking surface, so leaving the fallback plate up would put two plates on one dock.
        if (_root.gameObject.activeSelf)
            _root.gameObject.SetActive(false);
        _nativeRecovery.Reset();
        return true;
    }

    /// <summary>Refresh original clone content, then apply the owner's source-time rendered frame.
    /// The owner has the final write after any mirror layout or viewer animation synchronization.</summary>
    public void TickLive()
    {
        _native.Configure(_mirror);
        _mirror.TickLive();
        InfusionBoardUI? source = InfusionBoardUI.Instance;
        bool applied = source != null && _native.Apply(_mirror, source);
        bool recovered = _nativeRecovery.Observe(applied);
        if (!applied) _mirror.SetShown(false);
        else if (recovered)
        {
            // A native material/source can become ready without another packet or geometry
            // revision. Re-enter the original validated fit/show path on that local edge;
            // never simply show an unfitted host, and never wait for the recovery timer.
            bool mirrored = TryMirror();
            _drawnBy = mirrored ? RemoteWidgetMirror.Fidelity.MirroredWidget : RemoteWidgetMirror.Fidelity.None;
            if (!mirrored)
            {
                // Apply readiness alone is insufficient: keep retrying until the complete
                // native fit/show transaction succeeds, even when this owner frame is unchanged.
                _nativeRecovery.Observe(false);
                _mirror.SetShown(false);
            }
        }
    }

    /// <summary>Drop the clone and its MrBacking registration. The mod-drawn half dies with the
    /// board root that owns it, as it always has.</summary>
    public void Destroy() { _native.Destroy(); _mirror.Destroy(); }

    /// <summary>Scratch for <see cref="ReadRowOrder"/>: the six element indices in the order the
    /// game's own holder draws them. Reused every tick so the read allocates nothing.</summary>
    private readonly int[] _rowOrder = { 0, 1, 2, 3, 4, 5 };

    /// <summary>
    /// THE ORDER THE GAME'S OWN ELEMENT ROW IS IN — element indices sorted by the sibling index of
    /// their <c>InfusionElementUI</c> under <c>InfusionBoardUI.elementsHolder</c>. Identity
    /// (0..5, i.e. plain <c>EElement</c> order) whenever the board cannot be read.
    ///
    /// <para><b>WHY THIS IS NOT ENUM ORDER, AND THE MEASUREMENT THAT SAYS SO</b> (R2 finding F14,
    /// 2026-09-07). <c>InfusionElementUI.ShowCreating()</c> ends with
    /// <c>base.transform.SetAsLastSibling()</c> (<c>decompiled/GH.Runtime/InfusionElementUI.cs:158</c>),
    /// called from the <c>Inert</c> branch of <c>SetState</c> at <c>:108</c> on the
    /// <c>lastState != newState</c> edge. Nothing anywhere in the game puts it back:
    /// <c>ShowCreated()</c> (<c>:161-166</c>) and <c>StopAnimations()</c> (<c>:168-173</c>) touch no
    /// sibling, and <c>InfusionBoardUI</c> references <c>elementsHolder</c> in exactly ONE place —
    /// the <c>Instantiate</c> loop at <c>:64-71</c> — so an element that has entered creation is
    /// permanently at the end and later creations stack after it. The row is therefore a running
    /// history of creations, with never-created elements left ahead of them in enum order.</para>
    ///
    /// <para><b>AND SIBLING ORDER IS DRAWN ORDER, which is deducible from source rather than
    /// prefab data.</b> All six cells are <c>Instantiate</c>d from ONE prefab into ONE parent with
    /// no position argument (<c>InfusionBoardUI.cs:64-71</c>), and NOTHING in either game class
    /// writes an element's <c>anchoredPosition</c>, <c>localPosition</c> or sibling index other
    /// than the <c>SetAsLastSibling</c> above. Six identical rects cannot produce the row the
    /// player sees, so a child-order-driven layout component on the holder is the only thing that
    /// can be seating them — which is what the packing loop's own comment has always asserted. The
    /// assertion is right; the loop under it was dealing slots by enum index anyway.</para>
    ///
    /// <para>ZERO WIRE, AND THE DIVERGENCE IS INTRA-CLIENT. This strip is classified GLOBAL and
    /// reads the local <c>InfusionBoardUI</c> for every overlay it draws, so "the owner's row" is
    /// the very widget this same client has docked on its own board through
    /// <c>ElementBoardSurface</c>. Before this fix the docked board and a fallen-back remote strip
    /// showed the same six elements in two different left-to-right orders IN THE SAME FRAME.</para>
    ///
    /// <para>ABSENCE KEEPS THE OLD BEHAVIOUR, exactly as <see cref="ReadOverlay"/> does: no
    /// singleton, a null dictionary, a missing cell or a throw all leave the identity order, which
    /// is byte-for-byte the row this strip drew before this method existed.</para>
    /// </summary>
    private int[] ReadRowOrder()
    {
        for (int i = 0; i < 6; i++)
            _rowOrder[i] = i;
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            Dictionary<ElementInfusionBoardManager.EElement, InfusionElementUI>? ui =
                board != null ? board.elementsUI : null;
            if (ui == null)
                return _rowOrder;

            // Sibling index per element, or int.MaxValue for one that cannot be resolved — an
            // unreadable cell sorts to the end rather than displacing the five that ARE readable.
            var seat = new int[6];
            for (int i = 0; i < 6; i++)
            {
                seat[i] = int.MaxValue;
                if (ui.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI one)
                    && one != null && one.transform != null)
                    seat[i] = one.transform.GetSiblingIndex();
            }

            // Insertion sort over six entries: no allocation, no comparer delegate, and STABLE, so
            // two cells reporting the same seat keep enum order between them instead of swapping
            // frame to frame.
            for (int a = 1; a < 6; a++)
            {
                int key = _rowOrder[a];
                int b = a - 1;
                while (b >= 0 && seat[_rowOrder[b]] > seat[key])
                {
                    _rowOrder[b + 1] = _rowOrder[b];
                    b--;
                }
                _rowOrder[b + 1] = key;
            }
        }
        catch
        {
            for (int i = 0; i < 6; i++)
                _rowOrder[i] = i;
        }
        return _rowOrder;
    }

    /// <summary>
    /// SAY WHEN THE GAME RE-ORDERED ITS OWN ELEMENT ROW, and what the strip is now dealing.
    /// Change-gated on the order alone, so a session where nothing is ever infused prints nothing
    /// and a session with two creations prints twice — never per tick.
    ///
    /// <para>PURE, and INSTRUMENT-ONLY: it latches nothing (the gate lives beside the write it
    /// records, in <c>Refresh</c>), so retiring this method can break nothing.</para>
    /// </summary>
    private void LogRowOrder(int[] order)
    {
        // HW-VERIFY: grep ELEMENT ROW ORDER. The deciding field is the sequence itself — compare it
        // against the left-to-right order of the DOCKED element board in the same frame, which on
        // this same client is the real game widget. They must read the same. A line whose sequence
        // is still 0,1,2,3,4,5 after an element has been infused through a creation pulse means the
        // sibling read found no board and fell back to enum order (see ReadRowOrder), not that the
        // game kept enum order.
        VRLog.Note("Net", $"ELEMENT ROW ORDER [board {_playerId}]: the game's own elementsHolder now "
            + $"reads {(ElementInfusionBoardManager.EElement)order[0]}, "
            + $"{(ElementInfusionBoardManager.EElement)order[1]}, "
            + $"{(ElementInfusionBoardManager.EElement)order[2]}, "
            + $"{(ElementInfusionBoardManager.EElement)order[3]}, "
            + $"{(ElementInfusionBoardManager.EElement)order[4]}, "
            + $"{(ElementInfusionBoardManager.EElement)order[5]} by sibling index, and the "
            + "mod-drawn strip deals its slots in that order. InfusionElementUI.ShowCreating ends "
            + "with SetAsLastSibling and nothing in the game restores the seat, so this sequence is "
            + "a running history of creations rather than EElement order — which is what the strip "
            + "used to deal by.");
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
    internal static void ReadOverlay(out int creating, out int reserved, out int available)
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
    /// — either because it is unusable (see <see cref="Usable"/>) or because its MEASURED artwork
    /// under the requested tint is the featureless white block this whole ladder exists to make
    /// impossible (see <see cref="InkOf"/>). Either way the caller walks down one rung instead of
    /// shipping a white square.</para>
    ///
    /// <para>Only the RGB is written here: the ALPHA belongs to <see cref="ApplyChip"/>, which may
    /// be running a transition ramp at the same time. <paramref name="baseAlpha"/> is recorded in
    /// <see cref="_iconBaseAlpha"/> for that method to scale — the sprite path DOES need a base
    /// now, because the creating disc carries the prefab's own transparency and a transparency
    /// cannot honestly be folded into a colour the way the generic glyph's dim is.</para>
    /// </summary>
    private bool TryApplyIcon(int i, Sprite? sprite, float worldSize, Color rgb, float baseAlpha,
                              int state)
    {
        if (!Usable(sprite))
            return false;
        SpriteRenderer icon = _icons[i];
        Sprite show = Baked(sprite!);

        // ── THE MEASURED WHITE-BLOCK REFUSAL ────────────────────────────────────────────────────
        // The 445 guard asked whether the sprite's texture was alive. That is a STATE probe, and
        // the 447 logs show it passing every disc ("STRONG 6/6, WANING 6/6, CREATION 6/6") while
        // the user was still photographing a white square. So this asks about the PICTURE instead:
        // if the artwork that will actually reach the renderer is a featureless near-white block
        // AND the tint about to be laid over it is near-white and opaque, the result is the exact
        // rectangle the report is about, and this rung refuses it and lets the caller step down.
        // Measured on `show`, i.e. AFTER Baked(), so a bad mip bake is covered by the same test.
        SpriteInk ink = InkOf(show);
        if (ink.WhiteBlock && NearWhiteOpaque(rgb, baseAlpha))
        {
            LogWhiteBlock(i, state, show, ink, rgb, baseAlpha);
            return false;
        }
        _rungArt[i] = show.name;
        _iconBaseAlpha[i] = Mathf.Clamp01(baseAlpha);

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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  THE "WIRD ERSTELLT" CELL — drawing it, and measuring it off the owner's own widget
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw chip <paramref name="i"/>'s CREATING cell: the caption the owner reads as "wird
    /// erstellt" and the authored plate behind it, both at the proportion and offset measured off
    /// the owner's own widget (<see cref="ResolveCreatingCells"/>). The creation DISC itself is not
    /// drawn here — it goes through the shared ladder in <see cref="Refresh"/>, so it is covered by
    /// the same white-block refusal as every other state.
    ///
    /// <para>The caption STRING is re-read live rather than taken from the resolve snapshot: it is
    /// localized by the game's own text component, which need not have run when this strip first
    /// resolved against the singleton. An empty string leaves the label off — a mirror with nothing
    /// to say says nothing rather than drawing an empty box.</para>
    /// </summary>
    private void ApplyCreatingCell(int i)
    {
        CreatingCell cell = _cells[i];

        // THE PLATE. Off when the widget carries no sprite for it — a sprite-less Image is a plain
        // tinted rectangle in vanilla, not a shape, and inventing one is the mistake the
        // availability ring already refuses to make (see ResolveRings).
        SpriteRenderer plate = _plates[i];
        if (Usable(cell.Plate))
        {
            Sprite show = Baked(cell.Plate!);
            if (plate.sprite != show)
                plate.sprite = show;
            FitSprite(plate.transform, show, cell.PlateSize * ChipSize);
            plate.transform.localPosition = new Vector3(cell.PlateAt.x * ChipSize,
                                                        cell.PlateAt.y * ChipSize, -0.001f);
            if (!plate.enabled)
                plate.enabled = true;
        }
        else if (plate.enabled)
        {
            plate.enabled = false;
        }

        // THE CAPTION.
        TextMeshPro label = _labels[i];
        string text = LiveCreatingText(i) ?? cell.Text ?? string.Empty;
        if (text.Length == 0)
        {
            if (label.enabled)
                label.enabled = false;
            return;
        }
        var box = new Vector2(Mathf.Max(0.004f, cell.TextSize.x * ChipSize),
                              Mathf.Max(0.004f, cell.TextSize.y * ChipSize));
        // Change-gated: TmpFit.Fit re-runs auto-sizing, which is not free at a repaint cadence.
        if (label.text != text || _labelBox[i] != box)
        {
            label.text = text;
            _labelBox[i] = box;
            TmpFit.Fit(label, box.x, box.y, wrap: false);
        }
        label.transform.localPosition = new Vector3(cell.TextAt.x * ChipSize,
                                                    cell.TextAt.y * ChipSize, -0.002f);
        if (!label.enabled)
            label.enabled = true;
    }

    /// <summary>Take the creating cell down — the caption and its plate belong to that state alone.
    /// Free once they are already off.</summary>
    private void HideCreatingCell(int i)
    {
        if (_plates[i].enabled)
            _plates[i].enabled = false;
        if (_labels[i].enabled)
            _labels[i].enabled = false;
    }

    /// <summary>The owner's live "wird erstellt" string for element <paramref name="i"/>, or null
    /// when there is no widget to read (menu / loading). Read-only.</summary>
    private static string? LiveCreatingText(int i)
    {
        InfusionElementUI? one = Widget(i);
        if (one == null)
            return null;
        try
        {
            TextMeshProUGUI caption = one.creatingElementText;
            return caption != null ? caption.text : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stretch a sprite renderer's transform so <paramref name="show"/> covers
    /// <paramref name="worldSize"/> world units — NON-uniformly, on both axes, because the thing
    /// being mirrored is a uGUI <c>Image</c> stretched to its own rect and that is what a uGUI
    /// Image does. (The element DISC keeps the uniform larger-axis fit of
    /// <see cref="TryApplyIcon"/>: a disc must not be squashed.)
    /// </summary>
    private static void FitSprite(Transform t, Sprite show, Vector2 worldSize)
    {
        Vector3 size = show.bounds.size; // rect / pixelsPerUnit, world units at scale 1
        float fx = size.x > 0.0001f ? worldSize.x / size.x : 1f;
        float fy = size.y > 0.0001f ? worldSize.y / size.y : 1f;
        var scale = new Vector3(fx, fy, 1f);
        if (t.localScale != scale)
            t.localScale = scale;
    }

    /// <summary>See <see cref="ResolveCreatingCells"/> — the fallback state one cell stands at
    /// before (and after a failed) measurement. Seeded at board-build time as well as at every
    /// resolve, so a strip that draws a creating chip before the singleton ever answered still
    /// draws a legible caption instead of a transparent one at a zero-sized box.</summary>
    private void ResetCell(int i)
    {
        _cells[i] = default;
        _cells[i].Disc = _sprites[i, 2];
        _cells[i].DiscColor = Color.white;
        _cells[i].DiscRatio = DiscRatioFallback;
        _cells[i].TextColor = Color.white;
        _cells[i].TextSize = TextSizeFallback;
        _cells[i].TextAt = TextAtFallback;
        _cells[i].PlateColor = Color.white;
        _cells[i].PlateSize = TextSizeFallback;
        _cells[i].PlateAt = TextAtFallback;
    }

    /// <summary>
    /// Measure the owner's CREATING cell off the live element widgets — the creation disc's sprite,
    /// PREFAB TINT and size, the caption's string, colour, box and centre offset, and the caption
    /// plate's sprite, tint, box and offset. Everything is normalised by the ELEMENT DISC's own
    /// rect, so the mirror inherits the authored proportion at any chip size, exactly as
    /// <see cref="ResolveRings"/> does for the availability ring.
    ///
    /// <para>Read-only: sprites are assets, the widgets are only measured. A widget that cannot be
    /// measured leaves <see cref="CreatingCell.Measured"/> false and the fallback layout standing,
    /// and the counts below say how many of each of the four things were found — one number per
    /// population, because a single "n/6 creating cells" would be true of four different worlds and
    /// this file has already paid for that mistake once (see <see cref="TryResolveSprites"/>).</para>
    /// </summary>
    private void ResolveCreatingCells(InfusionBoardUI board, out int measured, out int discs,
                                      out int captions, out int plates)
    {
        measured = 0;
        discs = 0;
        captions = 0;
        plates = 0;
        for (int i = 0; i < 6; i++)
            ResetCell(i);

        Dictionary<ElementInfusionBoardManager.EElement, InfusionElementUI>? ui = board.elementsUI;
        if (ui == null)
            return;
        for (int i = 0; i < 6; i++)
        {
            if (!ui.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI one)
                || one == null)
            {
                continue;
            }
            Transform frame = one.transform;
            Image disc = one.elementImage;
            if (disc == null
                || !RectInFrame(frame, disc.rectTransform, out Vector2 discAt, out Vector2 discSize))
            {
                continue;
            }
            float axis = Mathf.Max(discSize.x, discSize.y);
            if (axis <= 0.0001f)
                continue;
            _cells[i].Measured = true;
            measured++;

            Image creation = one.creationImage;
            if (creation != null)
            {
                // THE TINT IS THE POINT. Vanilla never draws this sprite untinted at full opacity;
                // doing so is what produced the reported white square.
                _cells[i].DiscColor = creation.color;
                if (Usable(creation.sprite))
                {
                    _cells[i].Disc = creation.sprite;
                    discs++;
                }
                if (RectInFrame(frame, creation.rectTransform, out _, out Vector2 cSize))
                {
                    float cAxis = Mathf.Max(cSize.x, cSize.y);
                    if (cAxis > 0.0001f)
                        _cells[i].DiscRatio = cAxis / axis;
                }
            }

            TextMeshProUGUI caption = one.creatingElementText;
            if (caption != null)
            {
                _cells[i].Text = caption.text;
                _cells[i].TextColor = caption.color;
                if (RectInFrame(frame, caption.rectTransform, out Vector2 tAt, out Vector2 tSize)
                    && tSize.x > 0.0001f && tSize.y > 0.0001f)
                {
                    _cells[i].TextSize = tSize / axis;
                    _cells[i].TextAt = (tAt - discAt) / axis;
                }
                if (!string.IsNullOrEmpty(_cells[i].Text))
                    captions++;
            }

            Image bg = one.creationTextBackgroundImage;
            if (bg != null && Usable(bg.sprite))
            {
                _cells[i].Plate = bg.sprite;
                _cells[i].PlateColor = bg.color;
                if (RectInFrame(frame, bg.rectTransform, out Vector2 pAt, out Vector2 pSize)
                    && pSize.x > 0.0001f && pSize.y > 0.0001f)
                {
                    _cells[i].PlateSize = pSize / axis;
                    _cells[i].PlateAt = (pAt - discAt) / axis;
                }
                plates++;
            }
        }
    }

    /// <summary>
    /// One authored rect expressed in <paramref name="frame"/>'s space: its CENTRE and its SIZE.
    ///
    /// <para>Centres, never <c>anchoredPosition</c> — an anchored position runs to each rect's own
    /// pivot, so two siblings' anchored positions are not comparable and this project has that
    /// filed under its own name. The size is scaled by the rect's lossy scale relative to the
    /// frame's, so a rect under an intermediate scale is measured in the frame's units and not its
    /// own. False whenever anything read back is non-finite, in which case the caller keeps its
    /// fallback.</para>
    /// </summary>
    private static bool RectInFrame(Transform frame, RectTransform? r, out Vector2 centre,
                                    out Vector2 size)
    {
        centre = Vector2.zero;
        size = Vector2.zero;
        if (frame == null || r == null)
            return false;
        Rect rect = r.rect;
        if (!IsFinite(rect.width) || !IsFinite(rect.height))
            return false;
        Vector3 local = frame.InverseTransformPoint(r.TransformPoint(rect.center));
        if (!IsFinite(local.x) || !IsFinite(local.y))
            return false;
        Vector3 fs = frame.lossyScale;
        Vector3 rs = r.lossyScale;
        float kx = Mathf.Abs(fs.x) > 0.0001f ? rs.x / fs.x : 1f;
        float ky = Mathf.Abs(fs.y) > 0.0001f ? rs.y / fs.y : 1f;
        if (!IsFinite(kx) || !IsFinite(ky))
            return false;
        centre = new Vector2(local.x, local.y);
        size = new Vector2(Mathf.Abs(rect.width * kx), Mathf.Abs(rect.height * ky));
        return true;
    }

    /// <summary>A real number — neither NaN nor infinite.</summary>
    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  MEASURING THE PICTURE — the white-block probe
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// What one sprite's artwork actually looks like: mean colour and alpha over its rect, and the
    /// largest single-texel departure from that mean. <see cref="Measured"/> false means the probe
    /// could not run (an unblittable texture, a device that refused the readback) — an unmeasured
    /// sprite is NEVER refused, because a probe that failed must not be allowed to make the picture
    /// worse than it was.
    /// </summary>
    private readonly struct SpriteInk
    {
        internal readonly bool Measured;
        internal readonly float R;
        internal readonly float G;
        internal readonly float B;
        internal readonly float A;

        /// <summary>Largest per-texel departure from the mean across all four channels — the term
        /// that separates a flat block from artwork. A disc, a glyph or a caption plate all carry a
        /// silhouette and therefore a large spread; a featureless fill does not.</summary>
        internal readonly float Spread;

        internal SpriteInk(float r, float g, float b, float a, float spread)
        {
            Measured = true;
            R = r;
            G = g;
            B = b;
            A = a;
            Spread = spread;
        }

        /// <summary>A featureless near-white block: opaque nearly everywhere, near-white nearly
        /// everywhere, and almost no variation across the rect. The thresholds are deliberately
        /// tight — this must catch the reported rectangle and nothing that is real artwork.
        /// </summary>
        internal bool WhiteBlock =>
            Measured && A > 0.90f && R > 0.90f && G > 0.90f && B > 0.90f && Spread < 0.06f;

        public override string ToString() =>
            Measured
                ? $"rgb({R:F2},{G:F2},{B:F2}) a{A:F2} spread {Spread:F2}"
                : "unmeasured (the readback did not run)";
    }

    /// <summary>Edge of the square the ink probe downsamples a sprite into. 8 x 8 = 64 texels: far
    /// too coarse to be a picture, exactly enough to answer "is this a flat block".</summary>
    private const int InkProbeSize = 8;

    /// <summary>Ink measurements by sprite instance id. CLEARED whenever the strip re-resolves
    /// against a NEW <c>InfusionBoardUI</c>, because Unity reuses instance ids after a destroy and a
    /// stale entry would answer about somebody else's artwork — the same hazard
    /// <see cref="Baked"/> defends against one indirection out.</summary>
    private static readonly Dictionary<int, SpriteInk> s_ink = new Dictionary<int, SpriteInk>();

    /// <summary>
    /// Measure <paramref name="sprite"/>'s artwork — see <see cref="SpriteInk"/>. One tiny GPU
    /// downsample per sprite, cached: <see cref="InkProbeSize"/> squared texels through a temporary
    /// RenderTexture, which is why this can sit in a repaint path at all. The atlas region comes
    /// from <c>textureRect</c>, so a packed sprite is measured on ITS OWN region and not the whole
    /// sheet.
    ///
    /// <para>Restores <c>RenderTexture.active</c> unconditionally: <c>Graphics.Blit</c> rebinds the
    /// target, and this project has already paid for a blit that left someone else's target bound.
    /// Every failure yields an UNMEASURED reading, never a verdict.</para>
    /// </summary>
    private static SpriteInk InkOf(Sprite sprite)
    {
        int id = sprite.GetInstanceID();
        if (s_ink.TryGetValue(id, out SpriteInk known))
            return known;

        SpriteInk ink = default;
        RenderTexture? prev = RenderTexture.active;
        RenderTexture? rt = null;
        Texture2D? tmp = null;
        try
        {
            Texture2D tex = sprite.texture;
            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                Rect region = sprite.textureRect;
                var scale = new Vector2(region.width / tex.width, region.height / tex.height);
                var offset = new Vector2(region.x / tex.width, region.y / tex.height);
                rt = RenderTexture.GetTemporary(InkProbeSize, InkProbeSize, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt, scale, offset);
                RenderTexture.active = rt;
                tmp = new Texture2D(InkProbeSize, InkProbeSize, TextureFormat.RGBA32,
                                    mipChain: false, linear: false);
                tmp.ReadPixels(new Rect(0f, 0f, InkProbeSize, InkProbeSize), 0, 0);
                Color32[] px = tmp.GetPixels32();
                if (px != null && px.Length > 0)
                {
                    float sr = 0f, sg = 0f, sb = 0f, sa = 0f;
                    for (int k = 0; k < px.Length; k++)
                    {
                        sr += px[k].r;
                        sg += px[k].g;
                        sb += px[k].b;
                        sa += px[k].a;
                    }
                    float n = px.Length * 255f;
                    float mr = sr / n, mg = sg / n, mb = sb / n, ma = sa / n;
                    float spread = 0f;
                    for (int k = 0; k < px.Length; k++)
                    {
                        spread = Mathf.Max(spread, Mathf.Abs(px[k].r / 255f - mr));
                        spread = Mathf.Max(spread, Mathf.Abs(px[k].g / 255f - mg));
                        spread = Mathf.Max(spread, Mathf.Abs(px[k].b / 255f - mb));
                        spread = Mathf.Max(spread, Mathf.Abs(px[k].a / 255f - ma));
                    }
                    ink = new SpriteInk(mr, mg, mb, ma, spread);
                }
            }
        }
        catch
        {
            ink = default; // unmeasured — never a verdict
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (tmp != null)
                UnityEngine.Object.Destroy(tmp);
        }

        s_ink[id] = ink;
        return ink;
    }

    /// <summary>Would this tint leave a white block white? Only a near-white, near-opaque tint
    /// does; any real colour or any transparency turns the block into something the report is not
    /// about, and refusing those would cost artwork for nothing.</summary>
    private static bool NearWhiteOpaque(Color rgb, float alpha) =>
        alpha > 0.90f && rgb.r > 0.90f && rgb.g > 0.90f && rgb.b > 0.90f;

    /// <summary>One refused white block on the record — capped per (element, state) verdict through
    /// the same one-shot table the fallback ladder uses.</summary>
    private static void LogWhiteBlock(int i, int state, Sprite show, SpriteInk ink, Color rgb,
                                      float alpha)
    {
        if (i < 0 || i >= 6 || state < 0 || state > 2 || s_fallbackLogged[i, state])
            return;
        s_fallbackLogged[i, state] = true;
        // HW-VERIFY: this is the line that says the reported WHITE SQUARE was caught at the draw
        // site and refused, with the measurement that decided it — not a liveness flag, the actual
        // pixels that were about to reach the renderer.
        VRLog.Note("Net", $"Remote element strip: {(ElementInfusionBoardManager.EElement)i}'s " +
                          $"{StateNames[state]} artwork '{show.name}' measures {ink} — a " +
                          "FEATURELESS NEAR-WHITE BLOCK, and the tint about to be laid over it is " +
                          $"({rgb.r:F2},{rgb.g:F2},{rgb.b:F2}) a{alpha:F2}, i.e. near-white and " +
                          "opaque. That product IS the white square the user photographed, so this " +
                          "rung is refused and the ladder steps down to the game's generic element " +
                          "glyph. The element stays identifiable; what is lost is one state's art.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  THE 1:1 LINE — what the owner is in, beside what the mirror drew
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Report, per element, the state the OWNER's own widget is in and the state this MIRROR just
    /// drew — the one grep that settles 1:1 for the element strip.
    ///
    /// <para>THE TWO HALVES ARE TWO POPULATIONS, deliberately. The mirror half is what
    /// <see cref="Refresh"/> just decided; the owner half is read back off the live
    /// <c>InfusionElementUI</c> widgets — the actual Images and the actual caption the owner's
    /// docked panel is drawing. Deriving both from <c>ElementColumn</c> would have been a claim
    /// measuring itself, which is a bug class this project has filed.</para>
    ///
    /// <para>THE FALSIFIER. This line prints on the FIRST repaint of every strip, whatever the
    /// board holds, and then once per DISTINCT verdict. In a session where no element is ever
    /// created it therefore still prints, reading "nothing in creation on either side" — so silence
    /// of the token <c>ELEMENT PARITY</c> does not mean success, it means this strip never
    /// repainted at all, which is a different defect.</para>
    /// </summary>
    private void EmitParity(ElementInfusionBoardManager.EColumn[] state, int creating, int reserved,
                            int available)
    {
        string verdict;
        string key;
        try
        {
            var sb = new System.Text.StringBuilder(640);
            // THE CAP KEY IS THE VERDICT CLASS, NOT THE LINE. The owner's GUIAnimator drives real
            // NUMBERS — the caption's alpha, the creation Image's tint — that move every frame while
            // the animation runs, so keying the one-shot on the printed text would burn the whole
            // budget on one infusion and then go quiet exactly when a second element mattered. The
            // key therefore carries only the DISCRETE facts (which element, which state, which rung,
            // which artwork, what is on and off) and the line carries the numbers.
            var kb = new System.Text.StringBuilder(160);
            // WHICH BOARD, IN BOTH HALVES. The one-shot set is STATIC — it has to be, or six chips
            // would each burn a slot on the same infusion — and until now the key carried nothing
            // about WHOSE board the verdict came from. With two peers up, board B's verdict was
            // swallowed whenever board A had already reached the same one, which is precisely the
            // reading the second half of the user's report needs: "the mirror is empty ON THIS
            // BOARD" is not the same fact as "a mirror somewhere drew this".
            string via = _drawnBy == RemoteWidgetMirror.Fidelity.MirroredWidget
                ? "MIRRORED-WIDGET"
                : _drawnBy == RemoteWidgetMirror.Fidelity.None ? "NATIVE-PENDING" : "MOD-DRAWN";
            sb.Append("[board ").Append(_playerId).Append("] via=").Append(via).Append(": ");
            kb.Append(_playerId).Append(via);
            int spoken = 0;
            int blank = 0;
            for (int i = 0; i < 6; i++)
            {
                bool mirrorDraws = MirrorInk(i);
                bool ownerDraws = state[i] != ElementInfusionBoardManager.EColumn.Inert
                                  || (creating & (1 << i)) != 0
                                  || (reserved & (1 << i)) != 0;
                if (!mirrorDraws && !ownerDraws)
                    continue;
                if (spoken > 0)
                    sb.Append("  ;;  ");
                spoken++;
                // THE SECOND HALF OF THE REPORT, COUNTED. A reserved element is hidden on BOTH
                // sides by construction, so it is not a blank — everything else the owner is
                // drawing and this board is not, is.
                bool blanked = ownerDraws && !mirrorDraws && (reserved & (1 << i)) == 0;
                if (blanked)
                    blank++;
                string mirrorState = (creating & (1 << i)) != 0
                                     && state[i] == ElementInfusionBoardManager.EColumn.Inert
                    ? "CREATING"
                    : state[i].ToString();
                sb.Append((ElementInfusionBoardManager.EElement)i).Append(' ')
                  .Append(OwnerClause(i))
                  .Append(" | MIRROR ")
                  .Append(blanked ? "DREW NOTHING " : string.Empty)
                  .Append("chip=").Append(mirrorDraws ? "shown" : "hidden")
                  .Append(" state=").Append(mirrorState)
                  .Append(' ').Append(MirrorClause(i))
                  .Append(" reserved=").Append((reserved & (1 << i)) != 0 ? "yes" : "no")
                  .Append(" avail=").Append((available & (1 << i)) != 0 ? "yes" : "no");
                kb.Append(i).Append(mirrorState).Append(MirrorKey(i))
                  .Append(mirrorDraws ? 'D' : 'h').Append(blanked ? 'B' : 'b')
                  .Append((reserved & (1 << i)) != 0 ? 'S' : 's')
                  .Append((available & (1 << i)) != 0 ? 'A' : 'a')
                  .Append(OwnerKey(i)).Append('|');
            }
            if (spoken == 0)
            {
                // THE FALSIFIER'S TWO HALVES, SPELT OUT SO THEY CANNOT BE READ AS ONE. This branch
                // is "the owner has nothing up either" — an empty MIRROR over a non-empty owner is
                // the `blank` count above and prints as MIRROR BLANK, never here.
                sb.Append("nothing drawn on either side — all six elements Inert, none in creation, "
                          + "none reserved. The strip DID repaint; it had nothing to mirror. This is "
                          + "NOT the 'mirror is empty' reading: that one prints as MIRROR BLANK "
                          + "with the owner clause beside it");
                kb.Append("empty");
            }
            else if (blank > 0)
            {
                sb.Insert(0, "MIRROR BLANK (" + blank + " of " + spoken + ") — ");
            }
            verdict = sb.ToString();
            key = kb.ToString();
        }
        catch (System.Exception ex)
        {
            verdict = "the parity readback threw (" + ex.GetType().Name + ": " + ex.Message + ")";
            key = "threw:" + ex.GetType().Name;
        }

        if (s_parityLogged.Contains(key))
            return;
        if (s_parityLogged.Count >= MaxParityVerdicts)
        {
            if (s_parityCapped)
                return;
            s_parityCapped = true;
            // HW-VERIFY: a cap that goes quiet is its own defect — this says the line stopped and
            // why, instead of leaving a reader to mistake the silence for a settled board.
            VRLog.Note("Net", $"ELEMENT PARITY: {MaxParityVerdicts} distinct owner-vs-mirror " +
                              "verdicts recorded; further NEW verdicts are no longer printed. The " +
                              "cap is on DISTINCT verdicts, not on calls, so everything up to here " +
                              "is a complete census of the states this session reached.");
            return;
        }
        s_parityLogged.Add(key);
        // HW-VERIFY: THE 1:1 line for the element strip. OWNER is read off the game's own live
        // InfusionElementUI widgets (what the owner's docked panel is drawing); MIRROR is what this
        // strip just painted. One line per distinct verdict, and it prints on the first repaint of
        // every strip — see the falsifier in this method's doc.
        VRLog.Note("Net", "ELEMENT PARITY: " + verdict);
    }

    /// <summary>The game's live element widget for element <paramref name="i"/>, or null when there
    /// is none to read (menu / loading / a half-built board). One lookup, used by everything in this
    /// file that needs the owner's own cell — it used to be copied out four times.</summary>
    private static InfusionElementUI? Widget(int i)
    {
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            Dictionary<ElementInfusionBoardManager.EElement, InfusionElementUI>? ui = board != null
                ? board.elementsUI
                : null;
            if (ui == null
                || !ui.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI one)
                || one == null)
            {
                return null;
            }
            return one;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The CLONE node paired with one of the owner's components, or null while the
    /// mod-drawn fallback is up. Diagnostic-only, and read-only by construction —
    /// <see cref="RemoteWidgetMirror.CloneOf"/> hands back a node this class never writes.</summary>
    private T? Mirrored<T>(T? source) where T : Component
    {
        Transform? node = source != null ? _mirror.CloneOf(source.transform) : null;
        return node != null ? node.GetComponent<T>() : null;
    }

    /// <summary>Does this graphic put INK on the screen — on at a visible alpha inside a live
    /// branch? "Enabled" alone is the state probe that let a chip drawn at alpha 0.00 be reported
    /// as <c>chip=shown</c> for a whole hardware round.</summary>
    private static bool Ink(Graphic? g)
        => g != null && g.enabled && g.gameObject.activeInHierarchy && g.color.a > 0.02f;

    /// <summary>
    /// DID THIS MIRROR ACTUALLY DRAW ELEMENT <paramref name="i"/> — measured, not asserted.
    ///
    /// <para>On the mirrored path it is a readback off the CLONE's own graphics; on the mod-drawn
    /// path it is the ladder's rung AND the alpha that rung committed. The alpha term is the whole
    /// point: ModBuild 448 reported <c>chip=shown rung=AUTHORED-DISC</c> for a disc it had just
    /// drawn at <c>a0.00</c>, so the instrument agreed with a picture that was not there.</para>
    /// </summary>
    private bool MirrorInk(int i)
    {
        try
        {
            if (_drawnBy == RemoteWidgetMirror.Fidelity.MirroredWidget)
            {
                InfusionElementUI? one = Widget(i);
                if (one == null)
                    return false;
                Transform? cell = _mirror.CloneOf(one.transform);
                if (cell == null || !cell.gameObject.activeInHierarchy)
                    return false;
                return Ink(Mirrored(one.elementImage))
                       || Ink(Mirrored(one.creationImage))
                       || Ink(Mirrored<TMP_Text>(one.creatingElementText))
                       || Ink(Mirrored(one.creationTextBackgroundImage));
            }

            if (_rung[i] == 0)
                return false;
            if (_rung[i] == 3)
                return _quadBaseAlpha[i] > 0.02f;
            if (_iconBaseAlpha[i] > 0.02f)
                return true;
            // A transparent disc is still a drawn cell if the caption beside it carries the picture.
            return (_labels[i].enabled && _cells[i].TextColor.a > 0.02f)
                   || (_plates[i].enabled && _cells[i].PlateColor.a > 0.02f);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Did the mirror put ink anywhere on this strip? The demotion test in
    /// <see cref="Refresh"/>, and the reason it is a MEASURE and not "the clone exists".</summary>
    private bool MirrorHasPicture()
    {
        for (int i = 0; i < 6; i++)
        {
            if (MirrorInk(i))
                return true;
        }
        return false;
    }

    /// <summary>The MIRROR half of the parity line: what THIS board actually put on screen for
    /// element <paramref name="i"/>. Two shapes, because there are two mechanisms and reporting
    /// them in one vocabulary would hide which was running.</summary>
    private string MirrorClause(int i)
    {
        try
        {
            if (_drawnBy != RemoteWidgetMirror.Fidelity.MirroredWidget)
            {
                return "rung=" + RungNames[Mathf.Clamp(_rung[i], 0, RungNames.Length - 1)]
                       + " art='" + (_rungArt[i] ?? "-") + "'"
                       + " a" + _iconBaseAlpha[i].ToString("F2")
                       + " caption=" + (_labels[i].enabled ? "'" + _labels[i].text + "'" : "off")
                       + " plate=" + (_plates[i].enabled ? "on" : "off")
                       + " ring=" + (_rings[i].enabled ? "on" : "off");
            }

            InfusionElementUI? one = Widget(i);
            if (one == null)
                return "clone=no owner widget to pair with";
            Transform? cell = _mirror.CloneOf(one.transform);
            if (cell == null)
                return "clone=NOT PAIRED (this element is not in the mirrored subtree)";
            var caption = Mirrored<TMP_Text>(one.creatingElementText);
            Image? ring = Mirrored(one.availableHighlight);
            return "clone cell=" + (cell.gameObject.activeInHierarchy ? "shown" : "hidden")
                   + " disc=" + ImageClause(Mirrored(one.elementImage))
                   + " creation=" + ImageClause(Mirrored(one.creationImage))
                   + " caption=" + (caption != null
                       ? "'" + caption.text + "' (enabled=" + caption.enabled
                         + ", a" + caption.color.a.ToString("F2") + ")"
                       : "none")
                   + " plate=" + ImageClause(Mirrored(one.creationTextBackgroundImage))
                   + " ring=" + (ring != null && ring.enabled ? "on" : "off");
        }
        catch (System.Exception ex)
        {
            return "the mirror readback threw (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>The DISCRETE half of <see cref="MirrorClause"/> — the cap key. Every animated NUMBER
    /// is absent for the reason <see cref="OwnerKey"/> states: a key that moves with a running
    /// animation spends the whole verdict budget on one infusion.</summary>
    private string MirrorKey(int i)
    {
        try
        {
            if (_drawnBy != RemoteWidgetMirror.Fidelity.MirroredWidget)
            {
                return RungNames[Mathf.Clamp(_rung[i], 0, RungNames.Length - 1)] + (_rungArt[i] ?? "-")
                       + (_labels[i].enabled ? 'C' : 'c') + (_plates[i].enabled ? 'P' : 'p')
                       + (_rings[i].enabled ? 'R' : 'r');
            }
            InfusionElementUI? one = Widget(i);
            Transform? cell = one != null ? _mirror.CloneOf(one.transform) : null;
            if (cell == null)
                return "unpaired";
            return (cell.gameObject.activeInHierarchy ? "K" : "k")
                   + ImageKey(Mirrored(one!.elementImage))
                   + ImageKey(Mirrored(one.creationImage))
                   + (Ink(Mirrored<TMP_Text>(one.creatingElementText)) ? "C" : "c")
                   + ImageKey(Mirrored(one.creationTextBackgroundImage));
        }
        catch
        {
            return "unreadable";
        }
    }

    /// <summary>The OWNER half of the parity line for element <paramref name="i"/>: what the game's
    /// own element widget is actually drawing. Read-only and fully guarded — a diagnostic may never
    /// be the thing that takes a board down.</summary>
    private static string OwnerClause(int i)
    {
        try
        {
            if (InfusionBoardUI.Instance == null)
                return "OWNER no InfusionBoardUI (menu/loading)";
            InfusionElementUI? one = Widget(i);
            if (one == null)
                return "OWNER no element widget";
            TextMeshProUGUI caption = one.creatingElementText;
            Image ring = one.availableHighlight;
            return "OWNER cell=" + (one.gameObject.activeSelf ? "shown" : "hidden")
                   + " disc=" + ImageClause(one.elementImage)
                   + " creation=" + ImageClause(one.creationImage)
                   + " caption=" + (caption != null
                       ? "'" + caption.text + "' (enabled=" + caption.enabled
                         + ", active=" + caption.gameObject.activeInHierarchy
                         + ", a" + caption.color.a.ToString("F2") + ")"
                       : "none")
                   + " avail=" + (ring != null && ring.enabled ? "on" : "off");
        }
        catch (System.Exception ex)
        {
            return "OWNER unreadable (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// The DISCRETE half of the owner's state — which Images are on and what they are wearing, and
    /// whether the caption is up at all. Every animated NUMBER is deliberately absent: this is the
    /// cap key for <see cref="EmitParity"/>, and a key that moves with a running animation would
    /// spend the whole verdict budget on one infusion. The numbers still reach the log; they just
    /// do not decide whether the line prints.
    /// </summary>
    private static string OwnerKey(int i)
    {
        try
        {
            InfusionElementUI? one = Widget(i);
            if (one == null)
                return "nowidget";
            TextMeshProUGUI caption = one.creatingElementText;
            Image ring = one.availableHighlight;
            return (one.gameObject.activeSelf ? "W" : "w")
                   + ImageKey(one.elementImage) + ImageKey(one.creationImage)
                   + (caption != null && caption.enabled && caption.gameObject.activeInHierarchy
                          && caption.color.a > 0.02f
                      ? "T" : "t")
                   + (ring != null && ring.enabled ? "H" : "h");
        }
        catch
        {
            return "unreadable";
        }
    }

    /// <summary>One Image reduced to "on/off plus which sprite" — see <see cref="OwnerKey"/>.
    /// </summary>
    private static string ImageKey(Image? img)
    {
        if (img == null)
            return "-";
        Sprite? sprite = img.sprite;
        return (img.enabled ? "+" : "=") + (sprite != null ? sprite.name : "?");
    }

    /// <summary>One authored Image, as the parity line needs it: on/off, its sprite and its tint.
    /// </summary>
    private static string ImageClause(Image? img)
    {
        if (img == null)
            return "none";
        Sprite? sprite = img.sprite;
        Color c = img.color;
        return (img.enabled ? "on" : "off")
               + " '" + (sprite != null ? sprite.name : "-") + "'"
               + $" ({c.r:F2},{c.g:F2},{c.b:F2}) a{c.a:F2}";
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
            ResolveCreatingCells(board, out int cells, out int cellDiscs, out int cellCaptions,
                                 out int cellPlates);
            _spritesResolved = true;
            // A NEW infusion board is a new set of assets and therefore a new verdict: re-arm the
            // one-shot so the re-resolve is on the record instead of inheriting the old board's
            // line. (See _spriteSourceId for why re-resolving at all is the white-square fix.)
            if (sourceId != _spriteSourceId)
            {
                _resolveLogged = false;
                // AND DROP THE INK MEASUREMENTS. They are keyed on sprite instance id, and Unity
                // reuses instance ids after a destroy — a stale entry would answer this board's
                // question about the previous board's artwork.
                s_ink.Clear();
            }
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
                                  $"be invented), {ratios}/6 authored ring:disc ratios measured. " +
                                  $"CREATING cells (the owner's \"wird erstellt\" state, mirrored " +
                                  $"since the 2026-09-05 round): {cells}/6 measured against the " +
                                  $"element disc's own rect, {cellDiscs}/6 carry a usable authored " +
                                  $"creationImage sprite, {cellCaptions}/6 a non-empty caption " +
                                  $"string, {cellPlates}/6 a usable caption-plate sprite. Four " +
                                  "numbers and not one, because a single count would be true of " +
                                  "four different worlds — the same mistake the disc counter above " +
                                  "already paid for. An unmeasured cell keeps the fallback layout " +
                                  "and still shows the caption.");
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
