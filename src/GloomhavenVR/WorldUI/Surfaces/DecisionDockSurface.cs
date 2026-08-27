using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using Script.GUI.Popups;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NetProtocol = GloomhavenVR.Net.NetProtocol;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// DECISION DOCK (test #22, generalizes the test-#21 take-damage dock): every
/// in-scenario decision/confirmation the game shows as a flat modal window — the
/// take-damage burn choice (<c>TakeDamagePanel</c>), the burn-confirm dialog
/// (<c>UIManager.dialogPopup</c>: "burn this card / choose another"), and any prompt
/// added later to the <see cref="ModalFallback.DecisionDock"/> registry — is invisible
/// in VR and would deadlock the game waiting for a click. Pre-#22 the generic
/// <see cref="ModalFallback"/> floated the WHOLE 1920×1080 window in front of the HMD
/// and asserted ModalUI, which disabled the card fan's palm gate — even though these
/// prompts have follow-ups that NEED the fan (the burn choice continues in a LoseCard
/// fan pick; test #21 log: <c>fan state: mode=LoseCard … gateEnabled=False
/// (vrMode=ModalUI)</c>). Test #21 solved that for TakeDamagePanel but docked the row
/// CENTERED OVER the two parked cards (the test-#22 complaint that the buttons "fly
/// over the cards").
///
/// This single surface docks ONLY the interactive widget row of whichever prompt is
/// open — the REAL game buttons/toggles, so labels/localization and the native
/// interactable/dim states ride along — onto <see cref="PlayTray.DecisionMount"/>, the
/// reserved zone BELOW the two cards (see the collision math in PlayTray.BuildMounts).
/// The row is found STRUCTURALLY (never by name) per prompt — the deepest common
/// ancestor of the actionable widgets that is a strict descendant of the window root
/// (see <see cref="ModalFallback.DecisionDock.IsolateRow"/>) — so it survives hierarchy
/// renames and drags nothing but the buttons. Poke AND laser click it through the
/// standard conversion registration (Convert's pokeable default → UguiPokeSurfaces +
/// RayUguiDriver), exactly like the floated modal the clicks already worked on.
///
/// The REST of the window — vignette, backdrop, the flat window frame AND the embedded
/// card (whose real rendering + burn VFX is a PARALLEL worker's job, not this one's) —
/// is VISUALLY suppressed the way the 2D hand is
/// (<see cref="Cards.Patches.HandSuppression"/>): the window root's CanvasGroup is
/// forced alpha 0 / blocksRaycasts false every tick (the game rewrites alpha on
/// Show/ToggleVisibility), and nested <see cref="Canvas"/> components under the root
/// (which render INDEPENDENTLY of the root CanvasGroup alpha) are component-disabled,
/// stopping their whole subtree rendering (the CanvasConversion nested-canvas lesson,
/// tests #19/#20). Nothing is destroyed; everything is restored on undock.
///
/// MODE MACHINE (task 3): no ModalUI is asserted for a docked decision —
/// <see cref="ModalFallback"/> stands its generic path down for any window
/// <see cref="ModalFallback.DecisionDock.ClaimsWindow"/> claims (both the ID-tracked
/// TakeDamagePanel AND the poll-tracked dialogPopup), so the flow mode
/// (HalfSelection/LoseCard/ChooseCards) and the fan's palm gate stay live through the
/// whole prompt, and the game itself never locks the UI here. The burn-confirm dialog
/// FOLLOWS a LoseCard pick, so the fan MUST stay live.
///
/// TIMING (task 4): docking is LEVEL-TRIGGERED on the game's own window open state
/// (<see cref="ModalFallback.DecisionDock.ActivePrompt"/> → the prompt's IsOpen), never
/// on a hover preview — nothing about hovering a choice pre-transforms the board, and
/// the row now lives BELOW the cards, so there is no over-slot overlay to mistake for a
/// hover change. The dock appears only while a prompt is ACTUALLY OPEN and vanishes the
/// moment it closes.
///
/// LIFECYCLE (task 5): any close path (any choice, game-side close, scene death)
/// releases the conversion — which restores the row to its exact 2D home
/// (CanvasConversion restore records) and lifts the suppression — and dock/undock are
/// logged once per flip. Decisions are sequential (the burn-confirm opens only after
/// the take-damage panel handed off), so this surface docks ONE prompt at a time.
///
/// ONE CHARACTER OWNS A DECISION (user ruling 2026-08-08: "Die Entscheidung soll auch
/// nur für den jeweiligen Character angezeigt werden! Wechsle ich den Character, während
/// ich eine Entscheidung treffen muss, soll auch die Entscheidung nicht mehr angezeigt
/// werden bei dem neuen Character. Erst wenn ich wieder zum aktuellen Character wechsle
/// … will ich wieder, dass sie entsprechend angezeigt wird."). The docked row is no longer
/// a fixture of the BOARD — it belongs to the character the prompt was RAISED FOR, read
/// from the game's own model (<see cref="PromptOwner"/>: the attacked actor, the
/// short-resting hand, the hand whose pick opened the confirm — never from turn state,
/// because a decision outlives a turn boundary). While the player has FOCUSED somebody
/// else (<c>Board.CharacterFocus.Focused</c>), the row is render-hidden; the moment they
/// look back at the owner it is shown again, unchanged, in the very same place.
///
/// AND THE RULE COVERS THE WHOLE DISPLAY, NOT JUST THE ROW (user, ModBuild 86 hardware test:
/// "Buttons und Text also die GESAMTE Entscheidungsanzeige soll pro Character angezeigt
/// werden und dementsprechend wechseln bzw. verschwinden"). A prompt is drawn by SEVERAL mod
/// surfaces — this widget row, the prompt's persistent instruction TEXT
/// (<see cref="DamageTooltipSurface"/>: the game's HelpBox, parked just above the row) and the
/// use bars (<see cref="UseBarsSurface"/>) — and every one of them now hides on the SAME
/// verdict from the SAME resolver, <see cref="PromptFocus.ShouldHide"/>. That is the only
/// place the <c>Focused</c>-vs-owner comparison is written and it delegates attribution to the
/// one <see cref="PromptOwner"/> switch below, so a further piece of a prompt is wired in by
/// calling it — never by re-deriving the owner.
///
/// AND THE HIDE TRAVELS (user ruling 2026-08-08: "generell gilt die Regel, das man alle
/// Interaktionen, Animationen und Anzeigen des Controllboards in MP auch synchronisieren
/// soll … so wie der Spieler sie sieht. Sie unterscheidet sich also ggf. von der Anzeige auf
/// dem eigenen board."). A remote control board is a picture of ITS OWNER'S board, so while
/// this row is hidden the mod publishes no decision at all — wire records 12 (labels) and 23
/// (states + prompt kind) stop riding and the board-UI decision-drawer bit clears — and every
/// peer's mirrored dock empties in the same frames this one does. That REVERSES the principle
/// this file used to state at the <see cref="UpdateFocusVisibility"/> call site ("a local view
/// change may never edit what other machines see"): under the ruling the owner's view IS what
/// peers must see.
///
/// AND HIDING CANNOT DISTURB IT — structurally, not by care (see
/// <see cref="ApplyFocusHide"/>). The hide toggles <c>Canvas.enabled</c> and
/// <c>Renderer.enabled</c> on the MOD-OWNED converted host subtree (nested canvases and the
/// mixed-reality backing plate included — a plate is a MeshRenderer, and leaving it on was
/// the ModBuild 84 "leerer Hintergrund" report), and nothing else: no game method is
/// called, no GameObject the game owns is deactivated — so not one game widget's
/// <c>OnDisable</c> runs (<c>ExtendedButton.OnDisable</c> raises <c>ActiveChanged</c>/
/// <c>onDeselected</c>, which is exactly why SetActive is NOT the mechanism), no
/// <c>onClick</c> is invoked, no <c>UIWindow</c> is closed. The prompt stays OPEN, the
/// Choreographer stays parked in its wait state, and the row's widgets keep every bit of
/// their state (toggles, selection, pooled labels). Input cannot reach a hidden row either,
/// which is the point: both the laser (<c>RayUguiDriver</c>) and the fingertip
/// (<c>PokeInteractor</c>) skip a canvas that is not <c>isActiveAndEnabled</c>, so the
/// answer can only ever be given while the owner is in view.
///
/// SAFETY NET (task 5): while a claim holds the generic fallback stands down — but if
/// the row cannot be isolated (widgets missing/not yet pooled, common ancestor
/// degenerates to the window root, Convert failure) the claim is RELEASED after a short
/// grace (<see cref="ClaimGraceSeconds"/>) via
/// <see cref="ModalFallback.DecisionDock.MarkGaveUp"/> and ModalFallback floats the
/// whole window generically next tick (window + ModalUI): a worse experience, never a
/// deadlock. The manual A/X screen chord stays the universal rescue (it releases the
/// conversion so the full window shows on the 2D composite).
/// </summary>
internal sealed class DecisionDockSurface : WorldSurface
{
    /// <summary>Claim grace: how long a prompt may fail to isolate before the generic fallback takes over.</summary>
    private const float ClaimGraceSeconds = 1.5f;

    /// <summary>
    /// The one prompt of the registry that draws a PROMPT TEXT above its widgets — the take-damage
    /// panel, whose HelpBox line <see cref="DamageTooltipSurface"/> converts. Named once here and
    /// read wherever the row has to reserve the text's slice of the area, so "does this prompt have
    /// a line?" is a single comparison instead of a literal repeated per call site.
    /// </summary>
    private const string TakeDamagePromptName = "TakeDamagePanel";

    /// <summary>
    /// How long the row waits for the prompt TEXT to publish its measured bottom edge before
    /// seating itself as if the prompt had no line (see <see cref="Place"/>). The text converts one
    /// tick after the row docks — <see cref="DamageTooltipSurface.WantConverted"/> gates on
    /// <see cref="DockingTakeDamage"/>, which this surface only sets once its own row is up — so the
    /// row would otherwise reveal at the ceiling and drop by the text's height a frame later, which
    /// is the "aufplopped … dann lädt die richtige Position nach" the user ruled out. Holding the
    /// REVEAL (never the placement) removes the jump; the grace makes sure a tip that never converts
    /// cannot leave the decision invisible.
    /// </summary>
    private const float TextSeatGraceSeconds = 0.75f;

    /// <summary>
    /// Density-scale guards, mirroring <see cref="TrayMountedPanelSurface"/>
    /// (test #16): max 1 keeps small content content-true, min 0.5 keeps a
    /// misfired-huge measurement readable (slight dock overflow over shrinking
    /// below readability).
    /// </summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>
    /// Choice widgets are the one thing the player MUST read and hit under
    /// pressure — 0.8× the shared tray density renders them 1.25× bigger (the
    /// element-board rationale); the dock fit clamp still bounds the row to its
    /// mount budget.
    ///
    /// <para>INTERNAL, not private, since ModBuild 105: a peer's mirrored copy of this row
    /// (<c>Net.RemoteDecisionWidgets</c>) fits the SAME widgets into the SAME mount budget and has
    /// to do it at the same density, or the 1:1 row renders 1.25x too small on every other client —
    /// which is exactly the class of drift the 1:1 rule exists to stop. Read, never written.</para>
    /// </summary>
    internal const float DensityScale = 0.8f;

    /// <summary>No-tray fallback float distance (HMD-anchored, the ModalFallback pattern).</summary>
    private const float FloatDistanceMeters = 1.1f;

    /// <summary>Extra shrink on the no-tray float (the ModalFallback window factor).</summary>
    private const float FloatScaleFactor = 0.7f;

    /// <summary>Live instance for the static claim query (single instance per driver).</summary>
    internal static DecisionDockSurface? Instance { get; private set; }

    /// <summary>
    /// True while a decision prompt's widget row is ACTUALLY docked on the board.
    /// <see cref="UseBarsSurface"/> reads this to shift its bar stack below the decision
    /// row's zone — the two co-occur during take-damage and must never collide.
    /// </summary>
    internal static bool RowDocked => Instance != null && Instance.Panel != null
                                      && !Instance._rowHiddenForFocus;

    /// <summary>
    /// True while a docked row is RENDER-HIDDEN because the player is looking at a character the
    /// prompt does not belong to. Read cross-module by the multiplayer sender: since the
    /// 2026-08-08 ruling a peer's mirrored board must show what this board SHOWS, so a hidden row
    /// clears the board-UI decision drawer bit as well as the two decision records — see
    /// <see cref="UpdateFocusVisibility"/> and <c>Net.NetAvatarDriver</c>'s board-UI sampler.
    /// </summary>
    internal static bool RowFocusHidden => Instance != null && Instance._rowHiddenForFocus;

    /// <summary>
    /// Live world-metre offset (along <c>DecisionMount.up</c>, relative to the mount
    /// position) of the docked decision row's visible BOTTOM edge — the lowest visible
    /// widget graphic of the active prompt, glyph/plate-true, produced by the SAME
    /// measurement walk the placement's top-edge anchor uses (so top and bottom can never
    /// disagree about what "the row" is). Null while no row is mount-docked or nothing has
    /// been measured yet. <see cref="UseBarsSurface"/> hangs its bar stack a small clearance
    /// below THIS instead of the mount's worst-case ±MaxHeight/2 extent — the paranoia
    /// spacing floated the armor-bonus bar FAR below the take-damage row (screenshot
    /// abstand.png), visually disconnecting one decision area into two. One-directional by
    /// construction, so no feedback loop: the bars only READ this value, while the row's own
    /// measurement walks exclusively its own converted subtree / the prompt's serialized
    /// widgets — separate host canvases the bar stack can never appear in.
    /// </summary>
    internal static float? RowBottomUpMeters { get; private set; }

    /// <summary>
    /// Live world-metre offset (along <c>DecisionMount.up</c>, relative to the mount position) of
    /// the docked decision row's visible TOP edge — the same measurement walk that produces
    /// <see cref="RowBottomUpMeters"/>, taken at the other end. Null while no row is MOUNT-docked
    /// (HMD float, nothing docked, nothing measured yet).
    ///
    /// <para>THIS IS THE SEAM THE PROMPT TEXT SITS ON (user, ModBuild 89: "Wenn ich den ganzen
    /// Entscheidungsbereich nach unten verschiebe mit dem Offset, dann soll der Text … auch
    /// entsprechend mit nach unten verschoben werden — aktuell reagiert er wie ein Element das
    /// nicht zu dem Bereich dazugehört"). <see cref="DamageTooltipSurface"/> hangs the HelpBox
    /// line's BOTTOM edge one <c>[Cards] DecisionGap</c> above THIS value instead of computing a
    /// second seat of its own from the same constants — two independent computations of one seat
    /// is exactly how the text and its buttons drifted apart (the text used to be parked a fixed
    /// 0.11 m over the decision MOUNT, whose Y the row's placement cancelled out of its own solve,
    /// so the mount's Y offset moved the text and nothing else).</para>
    ///
    /// <para>AND THE OFFSET NOW MOVES BOTH (ModBuild 90): coupling the text to this edge answered
    /// only half of that sentence — it made the two inseparable, but the dial the user was holding
    /// still moved neither, because the cancellation was still there. <see cref="MountOffsetUp"/>
    /// feeds the mount's own up-axis displacement back into the seat solve, so this value is
    /// unchanged by the offset while the MOUNT it is measured from carries the whole area — row,
    /// text and use bars — the distance the player dialled.</para>
    ///
    /// <para>SINCE ModBuild 91 THIS IS A RESULT, NOT AN ANCHOR (user hardware test: "Die Anzeige der
    /// Initiativ-Schuhe war mir zu niedrig also habe ich es höher gemacht, dann später kam eine
    /// Schadens-Entscheidung — der Text war damit viel zu hoch … Ich hätte erwartet, dass der höchste
    /// Punkt bei den Initiativ-Schuhen auch der höchste Punkt ist, an dem der Text angezeigt wird").
    /// The area used to be solved BOTTOM-UP from this edge, so every element a prompt added grew the
    /// display UPWARD and one offset could not fit two prompts. It is solved TOP-DOWN from
    /// <see cref="AreaCeilingUp"/> now: the text (if the prompt has one) takes the ceiling, the row's
    /// top hangs one <c>DecisionGap</c> under the text's measured bottom, and this value is whatever
    /// that produces. The gap between the two — the only thing this seam ever expressed — is
    /// untouched.</para>
    ///
    /// <para>UNLIKE <see cref="RowBottomUpMeters"/> THIS SURVIVES THE FOCUS HIDE. The bars must
    /// fall back when the row is not SEEN (they would hang off a phantom edge); the prompt text
    /// must do the opposite — it keeps placing while hidden so that focusing the owner again
    /// reveals it at its FINAL geometry, never mid-placement (the "never reveal before the final
    /// geometry" rule). Both surfaces still hide together on the one shared verdict
    /// (<see cref="PromptFocus.ShouldHide"/>); what stays live here is geometry, not visibility.</para>
    /// </summary>
    internal static float? RowTopUpMeters { get; private set; }

    /// <summary>
    /// True while THIS surface has the <c>TakeDamagePanel</c>'s widget row docked on the
    /// board (task A). The take-damage widgets carry the game's mouse-hover preview
    /// handlers (<c>OnMouseEnter*/OnMouseExit*</c> → <c>Preview*/ResetPreviewing</c>),
    /// authored for a stable desktop cursor. Under a jittering VR laser they fire
    /// enter/exit dozens of times a second, thrashing the LoseCard preview fan
    /// (hand↔discard, log test #23) and making the burn pick unusable; the deliberate
    /// toggle CLICK already drives the preview, so <see cref="Patches.TakeDamagePanelSafety"/>
    /// stands the hover handlers down while this is set. Read cross-module (guarded by
    /// null-check on <see cref="Instance"/>); never true unless a live dock holds.
    /// </summary>
    internal static bool DockingTakeDamage { get; private set; }

    /// <summary>
    /// MULTIPLAYER READ SEAM (wire record <c>NetProtocol.ExtIdDecisionLines</c>): the labels of
    /// the docked row's PRESSABLE widgets, one per line ('\n'-joined), or null while no row is
    /// VISIBLE on this board. Peers render one inert button plate per line at their copy of this
    /// board's decision seat, so the remote dock shows the owner's ACTUAL choices instead of an
    /// empty drawer (user report 2026-08-04: "die remote decision buttons ... 1:1").
    ///
    /// IDENTITY GATE, BY SAMPLING RULE: only TMP texts UNDER a <c>Selectable</c> are collected —
    /// action wordings authored from generic GUI_* keys ("Verbrennen", "Ja"/"Nein", the burn
    /// counts). The prompt/description text of a dialog is deliberately never sampled: a confirm
    /// dialog's body can embed the card it is about, and the standing wire rule is absolute (no
    /// card identity; reveals only through <c>Net.RevealGate</c> — suppression is the designed
    /// failure direction). Recomputed on the shared content cadence (0.25 s) while docked, which
    /// also picks up the game re-labelling a pooled button mid-prompt.
    ///
    /// <para>WITHDRAWN WHILE THE ROW IS FOCUS-HIDDEN (user ruling 2026-08-08 — see
    /// <see cref="UpdateFocusVisibility"/>): what rides the wire is what this board SHOWS, so a row
    /// hidden because the player looked at another character publishes nothing and every peer's
    /// mirrored dock empties with it.</para>
    /// </summary>
    internal static string? WireButtonLines { get; private set; }

    /// <summary>
    /// MULTIPLAYER READ SEAM (wire record <c>NetProtocol.ExtIdDecisionState</c>): WHICH prompt is
    /// docked, as one of <c>NetProtocol.DecisionKind*</c>. 0 (<c>DecisionKindNone</c>) while no row
    /// is visible or the prompt is one this build does not classify. Peers use it to decide whether
    /// the take-damage instruction line belongs under their mirrored plates.
    /// </summary>
    internal static byte WirePromptKind { get; private set; }

    /// <summary>Number of valid entries in <see cref="_wireOptionStates"/> — one per published
    /// line of <see cref="WireButtonLines"/>, in the same order (the same walk fills both).</summary>
    private static int _wireOptionCount;

    /// <summary>
    /// Per-option state bytes for wire record 23 (<c>NetProtocol.DecisionOptionOfferedBit</c> /
    /// <c>DecisionOptionDimmedBit</c> / <c>DecisionOptionChosenBit</c>), index-aligned with
    /// <see cref="WireButtonLines"/>. Read through <see cref="CopyWireOptionStates"/> so the
    /// sender copies a snapshot instead of holding this buffer.
    /// </summary>
    private static readonly byte[] _wireOptionStates =
        new byte[Net.NetProtocol.DecisionStateMaxOptions];

    /// <summary>
    /// Copy the published per-option states into <paramref name="into"/> and return how many were
    /// written (never more than the shorter of the two buffers). The states describe the SAME
    /// widgets, in the SAME order, as the lines in <see cref="WireButtonLines"/>: offered means the
    /// owner can press it, dimmed is the game's 0.7-alpha "your character cannot" look, chosen is a
    /// toggle that is already on. That triple is what makes a peer's mirrored row read like the
    /// owner's instead of three equally-live plates.
    /// </summary>
    internal static int CopyWireOptionStates(byte[] into)
    {
        if (into == null)
            return 0;
        int n = _wireOptionCount;
        if (n > into.Length)
            n = into.Length;
        if (n > _wireOptionStates.Length)
            n = _wireOptionStates.Length;
        for (int i = 0; i < n; i++)
            into[i] = _wireOptionStates[i];
        return n;
    }

    /// <summary>
    /// MULTIPLAYER READ SEAM (wire record <c>NetProtocol.ExtIdDecisionWidgets</c>): what the docked
    /// TAKE-DAMAGE option is painting on ITSELF — <c>DecisionWidgetLethalBit</c> (the game swapped
    /// its normal damage icon for the fatal one), <c>DecisionWidgetShieldedBit</c> (the amount is in
    /// the shield colour), <c>DecisionWidgetMandatoryBit</c> (the mandatory-use highlight is lit)
    /// and <c>DecisionWidgetDamageValidBit</c> (<see cref="WireDamageAmount"/> means something).
    /// Zero for every other prompt, and while nothing is docked.
    /// </summary>
    internal static byte WireWidgetFlags { get; private set; }

    /// <summary>The number the docked take-damage option displays — the game's own
    /// <c>TakeDamagePanel.damageAmount</c> text, which changes live as the owner toggles shield
    /// items and bonuses. Meaningful only with <c>DecisionWidgetDamageValidBit</c> in
    /// <see cref="WireWidgetFlags"/>.</summary>
    internal static byte WireDamageAmount { get; private set; }

    /// <summary>
    /// Per-option ROLE codes for wire record 29 (<c>NetProtocol.DecisionRole*</c>), index-aligned
    /// with <see cref="WireButtonLines"/> and <see cref="_wireOptionStates"/> — the SAME walk fills
    /// all three, which is what lets a receiver resolve "option 2" against its OWN copy of the game
    /// widget without a second description of the row. Read through
    /// <see cref="CopyWireOptionRoles"/>.
    /// </summary>
    private static readonly byte[] _wireOptionRoles =
        new byte[Net.NetProtocol.DecisionStateMaxOptions];

    /// <summary>
    /// The <c>Selectable</c>s the last full walk published, index-aligned with
    /// <see cref="_wireOptionStates"/> — held ONLY so the per-frame pointer pass
    /// (<see cref="PointerBitsMoved"/>) can re-ask "is the owner on this option" without repeating
    /// the walk, the TMP lookups and the string join at 90 Hz. Entries past the published count are
    /// nulled, so a torn-down row cannot be read back through this array.
    /// </summary>
    private static readonly Selectable?[] _wireOptionWidgets =
        new Selectable?[Net.NetProtocol.DecisionStateMaxOptions];

    /// <summary>The roles last PUBLISHED (the change gate's other half — a role set can move while
    /// the wordings and the states hold, e.g. the game activating a widget that was hidden).</summary>
    private static readonly byte[] _publishedOptionRoles =
        new byte[Net.NetProtocol.DecisionStateMaxOptions];

    /// <summary>
    /// Copy the published per-option roles into <paramref name="into"/> and return how many were
    /// written (never more than the shorter of the two buffers). A role NAMES the game widget the
    /// option is — never what it would do to a card — so a peer can clone the real button out of
    /// their own copy of the prompt instead of drawing a lookalike.
    /// </summary>
    internal static int CopyWireOptionRoles(byte[] into)
    {
        if (into == null)
            return 0;
        int n = _wireOptionCount;
        if (n > into.Length)
            n = into.Length;
        if (n > _wireOptionRoles.Length)
            n = _wireOptionRoles.Length;
        for (int i = 0; i < n; i++)
            into[i] = _wireOptionRoles[i];
        return n;
    }

    /// <summary>Next unscaled time <see cref="WireButtonLines"/> is re-sampled while docked.</summary>
    private float _nextWireLinesAt;

    /// <summary>Join scratch for the wire-lines sample (no steady-state allocation — the joined
    /// string itself is change-gated by comparison before it replaces the published one).</summary>
    private static readonly System.Text.StringBuilder WireLinesScratch = new(96);

    /// <summary>The prompt currently docked (or being docked); null while none is open.</summary>
    private ModalFallback.DecisionDock.Prompt? _active;

    /// <summary>The active prompt's window (open-state + suppression target).</summary>
    private UIWindow? _activeWindow;

    private float _wantSince;
    private bool _targetWarned;
    private bool _hmdFloatPlaced;

    /// <summary>Unscaled time the row started waiting for the prompt TEXT's measured bottom edge
    /// (0 = not waiting) — see <see cref="TextSeatGraceSeconds"/> and <see cref="HeldTooLongForText"/>.</summary>
    private float _textWaitSince;
    private bool _textWaitWarned;

    // Suppression records (restored on undock; nothing destroyed).
    private UIWindow? _suppressedWindow;
    private CanvasGroup? _suppressedGroup;
    private readonly List<Canvas> _disabledCanvases = new(2);

    /// <summary>
    /// The pick confirm dialog's CANCEL option ("Waehle eine andere Karte" /
    /// GUI_CHOOSE_OTHER_CARD) while WE hid it from the docked row — see
    /// <see cref="ApplyPickCancelSuppression"/>. Null while nothing is hidden.
    /// </summary>
    private InputButton? _hiddenCancelButton;
    private bool _cancelSuppressionLogged;
    private static readonly List<Canvas> CanvasScratch = new(8);

    // ---- one character owns a decision (user ruling 2026-08-08; see the class doc) -------

    /// <summary>Canvases WE disabled to render-hide the docked row while the player is looking at
    /// another character. Held by reference, so the restore lands even if the conversion was
    /// released in between (the canvases are then back in their 2D home, where they belong
    /// enabled).</summary>
    private readonly List<Canvas> _focusHiddenCanvases = new(4);

    /// <summary>Renderers WE disabled for the same hide — same exact-restore contract as
    /// <see cref="_focusHiddenCanvases"/>. The MR backing plate lives here (an opaque plate
    /// MeshRenderer <see cref="MrBacking"/> parents under the host rect): it is not a Canvas, so a
    /// canvas-only hide left it drawing as an empty dark rectangle where the row had been —
    /// hardware report ModBuild 84.</summary>
    private readonly List<Renderer> _focusHiddenRenderers = new(4);

    /// <summary>True while the docked row is render-hidden because the focused character is not
    /// the one the prompt belongs to.</summary>
    private bool _rowHiddenForFocus;

    /// <summary>Change-dedup for the hide/show line: last (owner, looked-at) pair logged.</summary>
    private string? _loggedFocusVisibility;

    /// <summary>Component counts the CURRENT focus hide has switched off in total (across the
    /// re-asserting ticks), and whether the MR backing plate was among them — reported by the hide
    /// log so the next hardware log proves the plate is gone, not just the widgets.</summary>
    private int _focusHiddenCanvasCount;
    private int _focusHiddenRendererCount;
    private bool _focusHiddenPlate;

    // ---- docked-row adjustments (user #5 antique tint; user #14 placement-driven gap) ---

    /// <summary>
    /// USER #14 — THE GAP FIX, ground-truth reasoned (see <see cref="Place"/>). The
    /// tray's brass grab bar
    /// (<c>PlayTray.BuildHandle</c>: root-local y −0.19, zone −0.215..−0.165) sits along
    /// the board's LOWER EDGE — exactly where the game draws the decision prompt text
    /// ("Schadensphase: Erleide entweder Schaden…"). That prompt is NOT a child of the
    /// isolated widget row (the row is the deepest common ancestor of the three serialized
    /// widgets — <see cref="ModalFallback.DecisionDock.IsolateRow"/> — and every hardware
    /// log reports "no prompt text above the widgets", so no in-row measurement can ever
    /// find or close the gap the user sees). So the gap is driven from a MOD-OWNED
    /// reference instead: the grab-bar bottom, minus this clearance, is the prompt anchor
    /// under which the interactive widget block is placed.
    /// </summary>
    /// <remarks>INTERNAL since the 1:1 remote mirror: <c>Net.RemoteBoardFurniture</c> hangs its
    /// mirrored decision row from the same reference (bar bottom − clearance − DecisionGap) and
    /// used to keep a hand-copied duplicate of this number.</remarks>
    internal const float BarClearanceMeters = 0.008f;

    /// <summary>
    /// Fallback prompt-reference height above the <see cref="PlayTray.DecisionMount"/>
    /// (world m at diorama scale 1) used only when the tray exposes no grab-bar zone —
    /// the measured bar-zone-bottom distance (mount y −0.29, bar zone bottom −0.215 ≈
    /// 0.075 m above the mount) so the stepper keeps a sensible reference either way.
    /// </summary>
    private const float NoBarPromptRefUp = 0.075f;

    /// <summary>
    /// Antique multiply-tint for the docked row's widget backgrounds (user #5): the
    /// game-default light stone sprite sinks toward the dark wood / aged brass family
    /// of the mod's board buttons (the VR-settings gear look). Multiplied onto the
    /// authored Graphic colour, so per-widget differences survive; uGUI ColorTint
    /// transitions multiply on the CanvasRenderer ON TOP of this, so pressed/disabled
    /// dimming keeps working.
    ///
    /// INTERNAL since the 1:1 remote mirror (user 2026-08-07: "Die Entscheidungsbuttons sollen 1:1
    /// genau so aussehen (Position und Größe und Erscheinungsbild) und genau das beinhalten was der
    /// Spieler sieht"): <c>Net.RemoteBoardFurniture.SetDecisionLines</c> paints its mirrored plates
    /// with THIS constant over the SAME sampled game button sprite the owner's docked widgets wear,
    /// so the copy and the original cannot drift apart through a hand-copied colour.
    /// </summary>
    internal static readonly Color AntiqueTint = new(0.58f, 0.46f, 0.31f, 1f);

    private readonly List<(Graphic graphic, Color color)> _tintedGraphics = new(8);
    private Canvas? _deliberateCanvas;
    private bool _placementLogged;
    private float _lastLoggedGapPx = float.NaN;
    private float _lastLoggedOffsetUp = float.NaN;
    private static bool _takeDamageDumped;
    private static readonly List<Selectable> SelectableScratch = new(8);
    private static readonly List<TMP_Text> TextScratch = new(8);
    private static readonly List<Graphic> GraphicScratch = new(16);
    private static readonly List<RectTransform> WidgetRectScratch = new(4);
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    public DecisionDockSurface()
    {
        Instance = this;
        // User #11b: the gap target is live-tunable — re-apply the row adjustments in
        // place when it changes while a row is docked (AdjustDockedRow restores first,
        // so re-running is idempotent). Unsubscribed in Shutdown.
        // Null-guarded: this surface is constructed during module init, which may run before
        // CardsConfig has bound its entries (the same reason the old WorldUI bind was guarded).
        for (int b = 0; b < 3; b++)
        {
            ConfigEntry<float> gap = CardsConfig.DecisionGap((ControlBoard)b);
            if (!ReferenceEquals(gap, null))
                gap.SettingChanged += OnRowGapSettingChanged;
        }
    }

    /// <summary>
    /// User #11b/#14: live re-apply of the docked-row gap when the config bind changes.
    /// The gap now drives the docked block's VERTICAL PLACEMENT (a quantity the mod owns
    /// outright — see <see cref="Place"/>), so re-run the placement immediately; the tint
    /// is gap-independent, so it does not need re-applying here.
    /// </summary>
    private void OnRowGapSettingChanged(object? sender, System.EventArgs e)
    {
        if (Panel != null)
            Place();
    }

    public override string Name => "DecisionDock";
    protected override bool ConfigEnabled => WorldUIConfig.DecisionDock.Value;

    /// <summary>
    /// Level-triggered on the game's own window state (the ModalFallback contract):
    /// converted while the active prompt is open + claimed, released the moment it
    /// closes, its claim is handed back after the grace, or the manual screen chord
    /// forces the full 2D composite (the window must be in the composite the screen
    /// mirrors). Equivalent to <see cref="ModalFallback.DecisionDock.ClaimsWindow"/>
    /// so the surface docks EXACTLY what the generic path stands down for.
    /// </summary>
    protected override bool WantConverted
    {
        get
        {
            if (!base.WantConverted || FlatScreen.ManualScreenActive)
                return false;
            if (PickConfirmOnBoardKeycaps())
                return false; // both of this prompt's options are physical keycaps — see the doc
            return _activeWindow != null && ModalFallback.DecisionDock.ClaimsWindow(_activeWindow);
        }
    }

    /// <summary>
    /// USER RULING 2026-08-24 (verbatim, with redundanter_knopf.jpg): "Wenn man alle 3 Karten
    /// abgelegt hat sieht man den 'Karten ablegen' Knopf sowie auch einen identischen Knopf unten in
    /// der Entscheidungsleiste. Der ist redundant und kann raus, da es ja als physischer Knopf
    /// existiert."
    ///
    /// <para>THE PICK CONFIRM DIALOG HAS NO DOCKED ROW LEFT TO DRAW. Its CANCEL option was already
    /// removed from the dock by the 2026-08-04 ruling (see
    /// <see cref="ApplyPickCancelSuppression"/>) because grabbing the laid-down card IS that action;
    /// its COMMIT option is the green board keycap the photograph shows, wearing the popup's OWN
    /// localized label (<c>CardsDriver.UpdatePickStatus</c> →
    /// <c>CardsGameApi.PickDialogOptionLabel(cancel: false)</c> → <c>PlayTray.SetPickStatus</c>), and
    /// pressing it runs the popup's own commit callback (<c>CardsDriver.OnConfirmRequested</c> →
    /// <c>CardsGameApi.ConfirmPickDialog</c>). With both options already in the world, everything the
    /// dock could still put under the board is a DUPLICATE — and a docked row with nothing left in it
    /// is worse than none: it is an empty strip with a backing plate and a reserved gap.</para>
    ///
    /// <para>SO THE SURFACE DOES NOT PRESENT AT ALL for this one prompt, rather than presenting an
    /// empty row. No conversion means no host, no MR backing plate, no reserved area and no wire
    /// publication — there is nothing to blank. The window itself stays alpha-0/nested-canvas
    /// suppressed exactly as before (<see cref="ApplySuppression"/> keeps running in the undocked
    /// branch of <see cref="Tick"/>), so the flat popup does not reappear in the HMD, and the CLAIM
    /// is kept, so the generic <see cref="ModalFallback"/> does not float the whole window either.</para>
    ///
    /// <para>AND IT IS GATED ON THE KEYCAP ACTUALLY BEING THERE (<see cref="PlayTray.PickCommitCapOffered"/>
    /// — the cap's own live visibility, not a second derivation of it). No tray, a foreign-character
    /// view, a state where the board offers no placement: the cap is not shown, this reads false, and
    /// the row docks exactly as it did before. A decision may never become unanswerable — that is the
    /// standing safety rule of this surface and it is not weakened here.</para>
    ///
    /// <para>NOTHING IS HIDDEN ON THE GAME'S SIDE BY THIS PATH, WHICH IS THE POINT. The obvious
    /// alternative — deactivating the commit option's GameObject the way
    /// <see cref="ApplyPickCancelSuppression"/> deactivates the cancel one — would have walked
    /// straight back into the ModBuild 248 trap: <c>CardsGameApi.ConfirmPickDialog</c> skips an
    /// option whose <c>gameObject.activeSelf</c> is false (CardsGameApi.cs:480) and
    /// <c>PickDialogOptionLabel</c> skips it too (CardsGameApi.cs:555), so hiding the button would
    /// have made the board keycap unpressable AND stripped it of its "KARTEN ABWERFEN" label in one
    /// stroke. Not docking leaves both options ACTIVE, so every seam — the keycap commit, the keycap
    /// cancel (<c>CancelPickConfirmDialog</c> takes its untouched <c>popup.Cancel()</c> branch) and
    /// the card-grab reopen — runs the game's own full path.</para>
    ///
    /// <para>MULTIPLAYER: this is local presentation on both ends. Nothing docks ⇒ nothing is
    /// sampled into wire records 12/24/29, which is the 1:1 rule doing its job — a peer's mirrored
    /// board shows what THIS board shows, and this board shows the two keycaps, whose labels already
    /// ride the wire as the cap-label records. No card identity, no new channel.</para>
    /// </summary>
    private bool PickConfirmOnBoardKeycaps()
    {
        if (_active == null || _active.Name != "DialogPopup")
            return false;
        // STRUCTURAL identification — the very same one ApplyPickCancelSuppression uses, so this can
        // never fire on a scenario choice or any other DialogPopup (they keep their full docked row).
        if (CardsGameApi.PickConfirmCancelButton() == null)
            return false;
        return PlayTray.Current?.PickCommitCapOffered == true;
    }

    /// <summary>One-shot log latch for the stand-down line; re-armed on every prompt change.</summary>
    private bool _pickKeycapStandDownLogged;

    /// <summary>
    /// The active prompt's interactive row, isolated structurally (see the class doc).
    /// Null (→ retry while the grace runs) when the widgets are missing/not yet pooled
    /// or the ancestor degenerates to the window root — docking the root would drag
    /// the vignette + card along, which is the pre-#22 behavior the generic fallback
    /// already provides.
    /// </summary>
    protected override RectTransform? FindTarget()
    {
        if (_active == null || _activeWindow == null)
            return null;
        RectTransform? row = _active.FindRow();
        if (row == null)
        {
            WarnTargetOnce(_active.Name);
            return null;
        }
        return row;
    }

    private void WarnTargetOnce(string prompt)
    {
        if (_targetWarned)
            return;
        _targetWarned = true;
        VRLog.Warn("WorldUI", $"DECISION DOCK: cannot isolate the widget row of '{prompt}' " +
                              "(widgets missing/not yet pooled, or the common ancestor is the window " +
                              $"root). Claim releases after {ClaimGraceSeconds:F1}s; the generic modal " +
                              "fallback (floating window + ModalUI) takes over.");
    }

    public override void Tick()
    {
        // Roll-up of the PREVIOUS frame's focus hides, across every surface that draws a piece of
        // the prompt. Emitted here because this is the FIRST decision surface in the WorldUI tick
        // order, so everything reported since the last call is one complete, settled frame — see
        // PromptFocus.Flush.
        PromptFocus.Flush();

        // Pick the currently-open, still-claimed prompt (decisions are sequential, so
        // at most one). ActivePrompt already skips a window the grace handed back.
        ModalFallback.DecisionDock.Prompt? active = ModalFallback.DecisionDock.ActivePrompt();
        UIWindow? window = active != null ? active.Window() : null;
        bool open = window != null && window.IsOpen;

        // Track the active prompt; a new/switched/closed prompt re-arms the latches.
        if (!ReferenceEquals(window, _activeWindow))
        {
            // The active prompt CHANGED (test #26: the short-rest Yes/No closed and the
            // burn/redraw DialogPopup opened in its place). WorldSurface only converts
            // while Panel == null, so a live conversion of the PREVIOUS prompt would
            // stick on the board forever and the new prompt would never dock — nor float,
            // since the claim keeps the generic path down. Tear the old conversion down
            // here (restore its suppression + release its row) so base.Tick re-converts
            // the new target THIS tick.
            if (Panel != null || _suppressedWindow != null)
            {
                UnregisterDeliberateCanvas();
                RestoreFocusHide("the active prompt changed");
                RestoreRowAdjustments();
                RestorePickCancelSuppression();
                RestoreSuppression();
                RowBottomUpMeters = null; // stale row gone; the next prompt's Place re-publishes
                RowTopUpMeters = null;    // …and the prompt text falls back to the computed seat
                if (Panel != null && ReleaseCurrentPanel())
                    VRLog.Info("WorldUI", "DECISION DOCK: active prompt changed — previous row " +
                                          "released so the next prompt's row can dock in its place.");
            }
            _activeWindow = open ? window : null;
            _active = open ? active : null;
            _wantSince = 0f;
            _targetWarned = false;
            _pickKeycapStandDownLogged = false; // a new prompt re-arms the stand-down line
            _textWaitSince = 0f;   // a new prompt waits for ITS OWN line, from scratch
            _textWaitWarned = false;
        }
        else if (!open)
        {
            _activeWindow = null;
            _active = null;
        }

        // 2D-FLASH FIX (burn-confirm): alpha-0 the flat window the MOMENT the prompt is claimed
        // and open, before/independent of the row docking. The DialogPopup that confirms a card
        // burn re-parents the LIVE fullAbilityCard into itself and activates (DialogPopup.Show ->
        // gameObject.SetActive(true)); its CanvasGroup fades the whole popup (backdrop + full card)
        // in. Previously the window was suppressed only once the widget row had been isolated and
        // docked (Panel != null), so during the convert grace (>= 1 frame, up to ClaimGraceSeconds
        // if the row is not yet pooled) the raw popup rendered in the HMD as a brief 2D flash.
        // Doing the cheap CanvasGroup suppression up-front removes it; the nested-canvas (vignette)
        // suppression stays in the docked branch, where the row is already outside the window.
        if (_activeWindow != null)
            SuppressWindowGroup(_activeWindow);
        else if (_suppressedWindow != null)
            RestoreSuppression(); // claimed window closed without ever docking a row

        bool hadPanel = Panel != null;
        base.Tick(); // convert / release / Place (level-triggered on WantConverted)

        if (Panel != null)
        {
            _wantSince = 0f;
            if (!hadPanel)
            {
                VRLog.Info("WorldUI", $"DECISION DOCK: '{_active?.Name}' widget row docked below the cards " +
                                      "(window/vignette/card suppressed, fan gate stays live, no ModalUI) — " +
                                      "restored to 2D when the prompt closes.");
                DumpTakeDamageHierarchyOnce(); // user #14 STEP 1: one-time ground-truth hierarchy dump
                AdjustDockedRow();          // user #5: antique tint (gap is now placement-driven, see Place)
                RegisterDeliberateCanvas(); // user #13b: decision buttons take the deliberate v1 poke press
                _placementLogged = false;
                _lastLoggedGapPx = float.NaN;
                // A row that DID dock re-arms the keycap stand-down line, so a later flip back to
                // "the board answers this one" states itself again instead of latching silently.
                _pickKeycapStandDownLogged = false;
            }
            ApplySuppression(_activeWindow!); // non-null: WantConverted required IsOpen
            ApplyPickCancelSuppression();     // user ruling 2026-08-04: no "choose another card" button on the dock
            // ONE CHARACTER OWNS A DECISION (user ruling 2026-08-08): render-hide the row while
            // the player is looking at somebody else. Level-triggered, like every other
            // suppression here — see ApplyFocusHide for why it cannot disturb the prompt.
            //
            // Deliberately AFTER the two GAME-SIDE suppressions above, which must keep running
            // while the row is hidden because neither is about what THIS player is looking at: the
            // window must stay alpha-0 (or the flat prompt would reappear in the HMD the moment the
            // row hides) and the pooled cancel option must stay hidden (or it would flash back on
            // return).
            UpdateFocusVisibility();
            // …and deliberately BEFORE the MP publication, which IS about what this player sees.
            //
            // THE REVERSED PRINCIPLE (user ruling 2026-08-08, verbatim: "generell gilt die Regel,
            // das man alle Interaktionen, Animationen und Anzeigen des Controllboards in MP auch
            // synchronisieren soll … so wie der Spieler sie sieht. Sie unterscheidet sich also ggf.
            // von der Anzeige auf dem eigenen board."). This call sequence used to be the other way
            // round, on the stated principle that "a local view change may never edit what other
            // machines see" — so record 12 kept riding while the row was focus-hidden and peers
            // showed a decision the owner could not see. Under the ruling that is exactly backwards:
            // a remote board is a picture of ITS OWNER'S board, and their board shows nothing here
            // while the row is hidden. The sampler therefore publishes nothing while
            // _rowHiddenForFocus, the board-UI drawer bit clears with it (NetAvatarDriver), and the
            // whole decision display disappears from every peer's copy exactly as it disappeared
            // from the owner's own.
            SampleWireDecisionState();
            // The one surface that must accept input even under the game's UI-lock
            // raycaster mirror — the ModalFallback floating-modal exemption. Skipped while the
            // row is hidden: a hidden row must not be clickable, and the canvas hide already
            // stops both interactors (this only avoids re-arming the raycaster behind them).
            if (!_rowHiddenForFocus && Panel.HostRaycaster != null && !Panel.HostRaycaster.enabled)
                Panel.HostRaycaster.enabled = true;
        }
        else
        {
            _hmdFloatPlaced = false;
            // No prompt open ⇒ the roll-up's context is nobody's. The use bars keep reporting (they
            // outlive a prompt), so the line stays truthful instead of attributing them to a stale
            // prompt name.
            PromptFocus.SetContext(null, null, null);
            RestoreFocusHide("the row undocked");
            if (WireButtonLines != null)
            {
                // Records 12, 24 and 29 stop riding the moment the row undocks
                // (PublishWireDecision logs the withdrawal; the release line below states the
                // undock itself).
                _nextWireLinesAt = 0f;
                PublishWireDecision(null, NetProtocol.DecisionKindNone, 0, 0, 0);
            }
            RowBottomUpMeters = null; // no docked row → the bar stack falls back to the zone top
            RowTopUpMeters = null;    // …and any prompt text falls back to the computed seat
            if (hadPanel)
            {
                UnregisterDeliberateCanvas();
                RestoreRowAdjustments();
                RestorePickCancelSuppression();
                RestoreSuppression();
                VRLog.Info("WorldUI", "DECISION DOCK: widget row released — restored to its 2D home " +
                                      $"(open={open}), suppression lifted, row style/layout restored.");
            }
            // THE PICK CONFIRM DRAWS NOTHING HERE (user ruling 2026-08-24 — see
            // PickConfirmOnBoardKeycaps). The row is deliberately not converted, so:
            //  * the window's own suppression must keep running from THIS branch, or the flat popup
            //    (backdrop + the vignette's nested canvases) would render in the HMD — the docked
            //    branch is where ApplySuppression normally lives, and there is no docked branch now;
            //  * the claim grace below must NOT fire. It exists for a row that FAILED to isolate;
            //    handing this window to the generic float would put the whole 1920x1080 popup in
            //    front of the player for a prompt the board already answers.
            bool keycapStandDown = _activeWindow != null && PickConfirmOnBoardKeycaps();
            if (keycapStandDown)
            {
                ApplySuppression(_activeWindow!);
                _wantSince = 0f;
                if (!_pickKeycapStandDownLogged)
                {
                    _pickKeycapStandDownLogged = true;
                    VRLog.Info("WorldUI", "DECISION DOCK: pick confirm STANDS DOWN — the whole docked row " +
                                          "is suppressed for this prompt, not just its cancel option. Both " +
                                          "of its options exist as PHYSICAL board keycaps (commit = the " +
                                          "green cap wearing the popup's own label, cancel = the UNDO cap / " +
                                          "grabbing the laid-down card), so a docked bar would be a second " +
                                          "copy of a control the player already has in the world (user " +
                                          "ruling 2026-08-24, redundanter_knopf.jpg). NOTHING is presented: " +
                                          "no host, no backing plate, no title, no reserved gap and no wire " +
                                          "decision — an empty strip would be worse than none. The window " +
                                          "stays alpha-0 + nested-canvas suppressed and the dock KEEPS its " +
                                          "claim, so the generic modal fallback does not float it either. " +
                                          "Both option buttons stay ACTIVE, so the keycap commit " +
                                          "(ConfirmPickDialog) and DialogPopup.Cancel both run the game's " +
                                          "own full path — the ModBuild 248 inactive-button trap cannot " +
                                          "apply to a button nobody hid.");
                }
            }

            // Claim grace: claimed but unconverted (row not isolatable / Convert
            // failed) → after the grace, hand the window to the generic fallback.
            if (!keycapStandDown && _activeWindow != null
                && ModalFallback.DecisionDock.ClaimsWindow(_activeWindow))
            {
                if (_wantSince <= 0f)
                {
                    _wantSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _wantSince > ClaimGraceSeconds)
                {
                    ModalFallback.DecisionDock.MarkGaveUp(_activeWindow);
                    VRLog.Warn("WorldUI", $"DECISION DOCK: '{_active?.Name}' row not converted within " +
                                          $"{ClaimGraceSeconds:F1}s — claim released, the generic modal " +
                                          "fallback floats the whole window for this prompt.");
                }
            }
        }

        // Hover-thrash guard flag (task A): the take-damage widget row is docked and
        // driven by the VR laser/poke, so its mouse-hover preview handlers must stand
        // down (see the property doc). Only ever true while its row is actually docked.
        DockingTakeDamage = Panel != null && _active != null && _active.Name == TakeDamagePromptName;
    }

    /// <summary>
    /// Dock on the tray's <see cref="PlayTray.DecisionMount"/> (pose-follow, shared tray
    /// density — the <see cref="TrayMountedPanelSurface"/> math), horizontally CENTERED on
    /// the mount, VERTICALLY driven by <see cref="CardsConfig.DecisionGap"/>. While
    /// no usable mount exists (Cards module off, tray hidden/destroyed) the row floats
    /// HMD-anchored at reading distance instead (placed once, ModalFallback pattern) — a
    /// decision must never be invisible.
    ///
    /// USER #14 — PLACEMENT-DRIVEN GAP (the fix that cannot silently no-op). The mod owns
    /// where this block docks outright, so the gap stepper drives a MOD-OWNED quantity:
    /// the interactive-widget block's TOP is placed <see cref="CardsConfig.DecisionGap"/>
    /// board-local metres BELOW the prompt reference — the tray grab-bar
    /// bottom along the board's lower edge, where the game draws the decision prompt text.
    /// Shrink the gap → the whole docked block rises toward the prompt; grow it → the block
    /// drops. Because the block top is measured from the VISIBLE widget graphics (glyph/plate
    /// bounds, not the authored option-column rects), the buttons themselves land at the
    /// chosen distance regardless of where the prompt text lives (H1: it is external to the
    /// row). The grab bar can never be overlapped: the widget-block top is always at least
    /// the clearance below the bar zone (gap ≥ 0). Every dock — and every live gap change —
    /// logs the resolved reference, the applied gap px and the chosen block Y.
    /// </summary>
    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            Panel.OrderCluster = null; // HMD-floated — not part of the board's draw cluster
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            if (!_hmdFloatPlaced)
                _hmdFloatPlaced = TryPlaceAtHmd();
            RowBottomUpMeters = null; // HMD-floated, not on the mount — the bar stack must not hang off it
            RowTopUpMeters = null;    // …and neither may the prompt text (there is no mount to seat it on)
            return;
        }
        _hmdFloatPlaced = false;
        // Docked on the control board: furniture must stay structurally below this row at
        // every viewing angle — see ConvertedPanel.OrderCluster.
        Panel.OrderCluster = PlayTray.Current;

        // SHOW ONLY WHEN IT IS ALREADY RIGHT (user ruling 2026-08-03: "Wenn die
        // Entscheidungsbutton & Text aufplopped sieht man ihn erst kleiner/an einer Stelle für
        // eine Sekunde, dann lädt die richtige eingestellte Position nach. So soll das nicht sein
        // — es soll DIREKT richtig angezeigt werden.").
        //
        // The host used to be activated HERE, before the rect check below — and the rect is not
        // content-fitted for the first frames (CanvasConversion.TickFit needs a measure/settle
        // pass), so the early return left the row VISIBLE at its pre-fit size and its pre-place
        // pose. That is precisely the "smaller, in the wrong spot, for about a second". This is
        // the same lesson the floated windows learned: never reveal before the final geometry is
        // known. The activation now happens at the END of a placement that actually ran, so the
        // first frame the player sees is already the fitted size at the configured spot.
        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
        {
            if (Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(false); // not measured yet — stay INVISIBLE, never half-placed
            return;
        }

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            PlayTray.DecisionMountWidth * density / rect.width,
            PlayTray.DecisionMountMaxHeight * density / rect.height);
        float scale = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density * trayScale;

        Transform host = Panel.HostTransform;
        Vector3 up = mount.up;
        // Set the final orientation + scale BEFORE measuring: the widget-block-top and
        // row-top offsets below are taken relative to the host pivot (translation-
        // invariant), so the position can be solved afterward from those measurements.
        host.rotation = mount.rotation;
        host.localScale = Vector3.one * scale;

        // THE AREA'S CEILING (world m above the mount along up) — the ONE anchor every piece of
        // this display is laid out downward from since ModBuild 91. See AreaCeilingUp.
        float ceilingUp = AreaCeilingUp(mount, up, trayScale, out string refNote);

        // Top/bottom-most VISIBLE widget graphics, world m above the host pivot (fall back
        // to the fitted row edges when a prompt has no resolvable widgets). The FALLBACK is the
        // host rect, which carries the fit's slack around the content on every side
        // (ConvertedPanel.FitContentPadding) — take it off, or a prompt that falls back seats its
        // row that much below the ceiling while every prompt that resolves its widgets is flush.
        float padUp = Panel.FitContentPadding.y * scale;
        WidgetBlockEdgesAbovePivot(host, up, rect.yMax * scale - padUp, rect.yMin * scale + padUp,
            out float blockTopAbovePivot, out float blockBottomAbovePivot);

        // TEXT→BUTTON DISTANCE, AND NOTHING ELSE TOUCHES IT (user ruling 2026-08-03: "Der Abstand
        // von Text zu buttons bei den Decision buttons soll sich NICHT verändern wenn ich die
        // Größe oder offsets anpasse").
        //
        // It used to be [WorldUI] DecisionRowGapPx — pixels IN THE ROW'S OWN SCALE, i.e.
        // gapPx * scale, where `scale` is the fitted density above. That made the gap a FUNCTION
        // of the dock size: the fit clamps between MinDensityScale and MaxDensityScale, so a
        // resize moved the buttons up to 2x closer to or further from the prompt text without
        // anyone touching a gap setting. That is exactly the reported "ich hab die Größe angepasst
        // und dann war der Text direkt über den Buttons".
        //
        // The gap is now [Cards] DecisionGap_<board> in BOARD-LOCAL METRES, scaled only by the
        // tray's own scale. It is therefore invariant under the row's fit scale (the size) by
        // construction, and equally invariant under DecisionOffset — the offset enters the solve
        // as ONE rigid displacement of the whole area (see the block below), never as a term
        // between the text and the buttons. One setting, one distance.
        //
        // AND SINCE ModBuild 89 THE TEXT IS ON THE OTHER END OF THAT SAME NUMBER (user: the prompt
        // text "reagiert wie ein Element das nicht zu dem Bereich dazugehört"). The HelpBox line and
        // this row are placed from ONE seat with exactly this gap between them, so whatever moves one
        // (offset, DecisionScale, the gap itself, the board) moves the other by the same amount in
        // the same frame. THAT COUPLING IS UNCHANGED HERE — only which end of it is anchored moved.
        //
        // THE AREA HANGS FROM ITS CEILING NOW (user, ModBuild 90 hardware test: "Die Anzeige der
        // Initiativ-Schuhe war mir zu niedrig also habe ich es höher gemacht, dann später kam eine
        // Schadens-Entscheidung — der Text war damit viel zu hoch … Ich hätte erwartet, dass der
        // höchste Punkt bei den Initiativ-Schuhen auch der höchste Punkt ist, an dem der Text
        // angezeigt wird, und nicht darüber hinaus — also dass die Offsets für die gesamte Area
        // gelten"). ModBuild 90 anchored the ROW under the prompt reference and hung everything else
        // off it, so the display was solved BOTTOM-UP: a prompt with a text line put that line one
        // gap ABOVE the row, i.e. the area's top edge was `+ text height` higher than the top edge of
        // a prompt without one, and the tallest prompt overshot by exactly the pieces it added. One
        // offset therefore could not fit two prompts — tune it for the short one (the initiative
        // boots' ± bar, which is use bars alone) and the damage decision's sentence lands far above
        // it.
        //
        // The solve runs TOP-DOWN from AreaCeilingUp instead: the ceiling is the same world height
        // whatever prompt is open, and the contents are laid out DOWNWARD from it — first the prompt
        // TEXT (which takes the ceiling itself and publishes its measured bottom edge), then one
        // DecisionGap, then this widget row, then the use-bar drawer under that. A prompt with no
        // line simply starts at the row, so its top edge is the ceiling too. That is what makes the
        // offset one dial for the whole area: it moves the MOUNT, the ceiling is mount-relative
        // (unlike the old prompt-reference anchor, whose measurement from the mount cancelled the
        // mount's own Y back out — the ModBuild 90 MountOffsetUp correction that this replaces), so
        // Y now displaces the ceiling exactly as X and Z displace the area sideways and proud, and
        // every element rides along because every element descends from the ceiling.
        float gapBoardMeters = GapBoardMeters;
        float gapMeters = gapBoardMeters * trayScale;
        float offsetUp = MountOffsetUp(mount, up); // DIAGNOSTIC ONLY (the mount already carries it)

        // The prompt TEXT's own measured bottom edge, when this prompt has a line and the surface
        // that draws it has placed it. WHY THE ROW WAITS FOR IT: the text converts one tick AFTER
        // this row docks (DamageTooltipSurface gates on DockingTakeDamage), so on the first frames
        // the row would take the ceiling and then drop by the line's height — see TextSeatGraceSeconds.
        bool wantsText = _active != null && _active.Name == TakeDamagePromptName
                         && DamageTooltipSurface.TipWindowOpen;
        float? textBottomUp = wantsText ? DamageTooltipSurface.TextBottomUpMeters : null;
        bool textPending = false;
        if (wantsText && !textBottomUp.HasValue)
            textPending = !HeldTooLongForText();
        else
            _textWaitSince = 0f; // the wait ended (measured, or this prompt has no line)

        float targetBlockTopUp = textBottomUp.HasValue ? textBottomUp.Value - gapMeters : ceilingUp;
        float d = targetBlockTopUp - blockTopAbovePivot; // shift along up from mount.position
        Vector3 pos = mount.position + up * d;

        host.position = pos;

        // Fitted, placed, and only NOW visible (see the note above the rect check) — and not even
        // then while the prompt's own text line has yet to measure: the row's seat is that line's
        // bottom edge minus the gap, so revealing first would show the row at the ceiling for a frame
        // and then drop it (the "never reveal before the final geometry" rule). An ALREADY visible
        // row is never re-hidden: a line that appears mid-prompt is a genuine re-layout, not a pop-in.
        if (!Panel.HostGo.activeSelf && !textPending)
            Panel.HostGo.SetActive(true);

        // Publish the row's MEASURED bottom edge (mount-relative, along up) for the
        // UseBarsSurface stack — the bars hang a small clearance below the row the player
        // actually SEES instead of the mount's worst-case extent (see RowBottomUpMeters doc).
        // A row that is render-hidden for another character's focus is not SEEN, so it publishes
        // nothing and the bar stack falls back to the zone top. Place itself keeps running while
        // hidden on purpose: the geometry stays live, so looking back at the owner reveals the row
        // already in its final place (the "never reveal before the final geometry" rule above).
        RowBottomUpMeters = _rowHiddenForFocus ? null : d + blockBottomAbovePivot;

        // …and the MEASURED TOP edge, which is where the prompt TEXT hangs from (see
        // RowTopUpMeters). It equals targetBlockTopUp by construction — d was solved to put the
        // block top exactly there — so the text and the buttons are placed from ONE number, and it
        // stays published through the focus hide so the text returns at its final geometry.
        RowTopUpMeters = d + blockTopAbovePivot;

        // Change-gated on the gap AND on the applied offset: the offset is the other dial that moves
        // this seat, and a live tweak of it must re-state the line or the log cannot prove it landed.
        if (!_placementLogged || float.IsNaN(_lastLoggedGapPx) || float.IsNaN(_lastLoggedOffsetUp)
            || Mathf.Abs(gapBoardMeters - _lastLoggedGapPx) >= 0.0005f
            || Mathf.Abs(offsetUp - _lastLoggedOffsetUp) >= 0.0005f)
        {
            _placementLogged = true;
            _lastLoggedGapPx = gapBoardMeters;
            _lastLoggedOffsetUp = offsetUp;
            float topmostUp = textBottomUp.HasValue ? ceilingUp : RowTopUpMeters!.Value;
            float areaHeight = topmostUp - (d + blockBottomAbovePivot);
            VRLog.Info("WorldUI", $"DECISION DOCK SEAT: '{_active?.Name}' — the decision AREA's CEILING is " +
                                  $"{ceilingUp * 1000f:F0} mm above the decision mount ({refNote}); the " +
                                  "TOPMOST element of this prompt is " +
                                  (textBottomUp.HasValue
                                      ? $"the prompt TEXT, whose top edge sits AT the ceiling and whose " +
                                        $"measured bottom is {textBottomUp.Value * 1000f:F0} mm — a line " +
                                        $"{(ceilingUp - textBottomUp.Value) * 1000f:F0} mm tall — with the " +
                                        $"widget row's top one gap under it"
                                      : "the widget ROW itself (this prompt draws no text line), whose top " +
                                        "sits AT the ceiling") +
                                  $" at {RowTopUpMeters!.Value * 1000f:F0} mm; the row's measured bottom is " +
                                  $"{(d + blockBottomAbovePivot) * 1000f:F0} mm, so the whole area is " +
                                  $"{areaHeight * 1000f:F0} mm tall (budget " +
                                  $"{PlayTray.DecisionMountMaxHeight * trayScale * 1000f:F0} mm — an over-tall " +
                                  "prompt keeps the ceiling and grows DOWNWARD into the free air below the " +
                                  $"board, never up past it). [Cards] DecisionGap_{CardsConfig.CurrentBoard}=" +
                                  $"{gapBoardMeters * 1000f:F1} mm = {gapMeters * 1000f:F0} mm world spaces the " +
                                  $"text from the buttons and NOTHING else; [Cards] " +
                                  $"DecisionOffset_{CardsConfig.CurrentBoard} has displaced the mount — and " +
                                  $"therefore this ceiling and everything under it — {offsetUp * 1000f:F1} mm " +
                                  $"along the dock up-axis. Host shifted {d * 1000f:F0} mm at row scale " +
                                  $"{scale:F5} (tray {trayScale:F3}). THE CEILING IS PROMPT-INDEPENDENT: this " +
                                  "number must read the same for the initiative-boots bar and for the damage " +
                                  "decision.");
        }
    }

    /// <summary>
    /// Has the row waited past <see cref="TextSeatGraceSeconds"/> for a prompt text that never
    /// measured? Level-triggered from <see cref="Place"/>: the first pending frame starts the clock,
    /// any frame that is not pending clears it (see <see cref="_textWaitSince"/>). A tip window that
    /// is open but whose surface cannot convert must never leave the decision invisible, so the row
    /// gives up waiting, seats itself at the ceiling and says so once.
    /// </summary>
    private bool HeldTooLongForText()
    {
        if (_textWaitSince <= 0f)
        {
            _textWaitSince = Time.unscaledTime;
            return false;
        }
        if (Time.unscaledTime - _textWaitSince <= TextSeatGraceSeconds)
            return false;
        if (!_textWaitWarned)
        {
            _textWaitWarned = true;
            VRLog.Warn("WorldUI", "DECISION DOCK: the prompt's HelpBox line is open but has published no " +
                                  $"measured geometry within {TextSeatGraceSeconds:F2}s — the widget row " +
                                  "takes the area CEILING itself rather than staying hidden. The text, if it " +
                                  "arrives later, will re-seat the row one gap below it in that frame.");
        }
        return true;
    }

    /// <summary>The live text↔buttons gap, <c>[Cards] DecisionGap_&lt;board&gt;</c> in BOARD-LOCAL
    /// metres (never negative). Read by <see cref="Place"/> for the row's seat and by
    /// <see cref="DamageTooltipSurface"/> for the clearance it hangs the prompt line at — ONE
    /// stepper, ONE distance, on both ends of it.</summary>
    internal static float GapBoardMeters =>
        Mathf.Max(0f, CardsConfig.DecisionGap(CardsConfig.CurrentBoard).Value);

    /// <summary>
    /// The PROMPT REFERENCE in world metres above <paramref name="mount"/> along
    /// <paramref name="up"/>: the tray grab-bar's bottom edge minus <see cref="BarClearanceMeters"/>
    /// — the board's lower edge, under which the whole decision area hangs. Falls back to
    /// <see cref="NoBarPromptRefUp"/> when the tray exposes no bar zone.
    /// </summary>
    private static float PromptReferenceUp(Transform mount, Vector3 up, float trayScale,
                                           out string refNote)
    {
        if (PlayTray.Current?.HandleZone is BoxCollider barZone)
        {
            Transform bt = barZone.transform;
            float barCenterUp = Vector3.Dot(bt.TransformPoint(barZone.center) - mount.position, up);
            float barHalfUp = 0.5f * barZone.size.y * Mathf.Abs(bt.lossyScale.y);
            refNote = "grab-bar bottom";
            return barCenterUp - barHalfUp - BarClearanceMeters * trayScale;
        }
        refNote = "no-bar board-edge estimate";
        return NoBarPromptRefUp * trayScale;
    }

    /// <summary>
    /// THE DECISION AREA'S CEILING: world metres above <paramref name="mount"/> along
    /// <paramref name="up"/> at which the TOPMOST pixel of the whole decision display sits — the
    /// prompt text if the prompt has one, otherwise the widget row, otherwise the use-bar drawer.
    /// The single anchor every one of those three surfaces lays itself out DOWNWARD from
    /// (<see cref="Place"/>, <see cref="DamageTooltipSurface.Place"/>,
    /// <c>UseBarsSurface.StackDocked</c>), so the display's top edge is at the same height whatever
    /// prompt is open — the ModBuild 90 report, in one number.
    ///
    /// <para>IT IS THE DRAWER ZONE'S OWN TOP EDGE, <c>DecisionMountMaxHeight/2</c> above the mount —
    /// which is exactly where the use-bar stack has always started when no decision row was up, i.e.
    /// where the user has already tuned the initiative-boots bar to sit. Choosing the authored zone
    /// top rather than the old prompt reference is what makes the offset ONE dial: the mount carries
    /// <c>[Cards] DecisionOffset_&lt;board&gt;</c>, and a mount-relative ceiling therefore carries it
    /// natively on all three axes — no <see cref="MountOffsetUp"/> correction, no cancellation to
    /// undo (see the block in <see cref="Place"/>).</para>
    ///
    /// <para>AND WHAT TOUCHES IT IS THE VISIBLE PIXEL, NOT THE HOST RECT (user ruling 2026-08-09:
    /// "Der obere Rand des Entscheidungsbereichs wurde mit dem offset festgelegt. Von da sollen die
    /// Elemente immer ausnahmslos anfangen und nach unten wachsen"). A converted host is the fitted
    /// content union PLUS the fit's slack on every side, with the union centred inside it
    /// (<see cref="ConvertedPanel.FitContentPadding"/>) — so a seat solved from <c>rect.yMax</c> put
    /// the visible element that slack too low, and handed the same error to everything hanging off
    /// its published bottom edge (the line → the row → the bars, three times over). All three
    /// surfaces subtract it now; this row already measured its own widget graphics, so only its
    /// no-widgets FALLBACK needed the correction.</para>
    ///
    /// <para>CLAMPED AT THE BOARD'S LOWER EDGE (<see cref="PromptReferenceUp"/>: the grab-bar bottom
    /// minus <see cref="BarClearanceMeters"/>). Dialling the offset far enough up would otherwise
    /// push the area into the tray's brass grab bar and then over the board itself. The clamp is
    /// applied HERE, in the one place all three surfaces read, precisely so that hitting it moves
    /// them together — a limit that stopped the row while the bars kept climbing would re-open the
    /// very disagreement this method exists to close.</para>
    /// </summary>
    internal static float AreaCeilingUp(Transform mount, Vector3 up, float trayScale,
                                        out string note)
    {
        float zoneTop = PlayTray.DecisionMountMaxHeight * 0.5f * trayScale;
        float barLimit = PromptReferenceUp(mount, up, trayScale, out string barNote);
        if (zoneTop <= barLimit)
        {
            note = "drawer-zone top";
            return zoneTop;
        }
        note = $"CLAMPED to the {barNote} — the offset would have pushed the area into the board";
        return barLimit;
    }

    /// <summary>Overload for call sites that do not log the note.</summary>
    internal static float AreaCeilingUp(Transform mount, Vector3 up, float trayScale) =>
        AreaCeilingUp(mount, up, trayScale, out _);

    /// <summary>
    /// The up-axis displacement <c>[Cards] DecisionOffset_&lt;board&gt;</c> has already applied to
    /// <paramref name="mount"/> itself (world metres along <paramref name="up"/>) — literally the
    /// authored offset carried into world space by the mount's own parent, so it is exact under any
    /// board scale and cannot drift from what <c>PlayTray.SetDecisionLayout</c> wrote.
    ///
    /// <para>SINCE ModBuild 91 IT IS A DIAGNOSTIC, NOT A TERM OF THE SOLVE. ModBuild 90 added it back
    /// into the seat because that seat was measured FROM the mount against the prompt reference, so
    /// the mount's own Y cancelled out of <c>mount.position + up * d</c> and the Y dial was a no-op.
    /// The area now hangs from <see cref="AreaCeilingUp"/>, which is mount-relative by construction,
    /// so Y rides along exactly as X and Z always did and there is nothing left to correct. It stays
    /// because the LOG has to be able to state how far the player's dial has moved the area — the one
    /// question the mount-relative numbers deliberately cannot answer.</para>
    ///
    /// <para>The shipped default is 0 on every board: the 157 mm this dial used to carry lives in
    /// <c>PlayTray.DecisionMountBase</c> since the dial went live, so honouring it moved nothing.
    /// See the migration in <c>CardsConfig</c> (<c>DecisionOffsetYRebased</c>).</para>
    /// </summary>
    internal static float MountOffsetUp(Transform mount, Vector3 up)
    {
        Vector3 off = CardsConfig.DecisionOffset(CardsConfig.CurrentBoard).Value;
        Transform? parent = mount.parent; // Unity-null aware
        return Vector3.Dot(parent != null ? parent.TransformVector(off) : off, up);
    }

    /// <summary>
    /// Sample the docked row for the multiplayer wire: the PRESSABLE-widget labels (record 12, see
    /// <see cref="WireButtonLines"/>) AND, in the same walk, each option's STATE plus the prompt
    /// kind (record 23). Every active <c>Selectable</c> under the docked target, in hierarchy order
    /// (which is the row's visual order), contributes its first TMP text and one state byte, so the
    /// two records are index-aligned by construction — there is no second walk that could disagree
    /// about what "option 2" is. Runs on the shared content cadence while a row is docked — a
    /// handful of GetComponentsInChildren walks over a ≤6-widget subtree, allocation-free until the
    /// joined string actually changes. Never throws its way out of Tick: a half-torn-down row
    /// degrades to "nothing published", which peers render as no decision at all.
    ///
    /// <para>NOTHING IS PUBLISHED WHILE THE ROW IS RENDER-HIDDEN for another character's focus, and
    /// the withdrawal bypasses the cadence so it lands on the very tick the row disappears — see
    /// the call site for the ruling that reversed this.</para>
    /// </summary>
    private void SampleWireDecisionState()
    {
        // FOCUS-HIDDEN ⇒ WITHDRAW IMMEDIATELY (not on the next 0.25 s tick): the owner's board went
        // blank at that seat this frame, and the peer copies must go blank with it.
        if (_rowHiddenForFocus)
        {
            _nextWireLinesAt = 0f;
            PublishWireDecision(null, NetProtocol.DecisionKindNone, 0, 0, 0);
            return;
        }
        // THE OWNER POINTER DOES NOT WAIT FOR THE CADENCE — see PointerBitsMoved for why a hover
        // sampled at 4 Hz reaches a peer as a stutter. A moved bit clears the gate and the full
        // walk below runs on this very tick, exactly as the focus-hidden withdrawal above does.
        if (PointerBitsMoved())
            _nextWireLinesAt = 0f;
        if (Time.unscaledTime < _nextWireLinesAt)
            return;
        _nextWireLinesAt = Time.unscaledTime + 0.25f;
        string? lines = null;
        int options = 0;
        byte widgetFlags = 0;
        byte damage = 0;
        try
        {
            RectTransform? root = Panel?.Target;
            if (root != null)
            {
                SelectableScratch.Clear();
                root.GetComponentsInChildren(includeInactive: false, SelectableScratch);
                WireLinesScratch.Length = 0;
                for (int i = 0; i < SelectableScratch.Count; i++)
                {
                    Selectable sel = SelectableScratch[i];
                    if (sel == null || !sel.gameObject.activeInHierarchy)
                        continue;
                    TMP_Text? label = sel.GetComponentInChildren<TMP_Text>(includeInactive: false);
                    string text = label != null ? label.text : string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    if (WireLinesScratch.Length > 0)
                        WireLinesScratch.Append('\n');
                    // Labels are single-line wordings; a stray newline inside one would split it
                    // into two plates on the peer, so it is flattened to a space here.
                    WireLinesScratch.Append(text.Replace('\n', ' ').Replace('\r', ' ').Trim());
                    if (options < _wireOptionStates.Length)
                    {
                        // ONE WALK FILLS ALL THREE RECORDS — the wording (12), the state (24) and
                        // now the widget ROLE (29). Index alignment is therefore structural: there
                        // is no second walk that could disagree about what "option 2" is, which is
                        // exactly the property a receiver needs to resolve the role against its own
                        // copy of the game widget.
                        _wireOptionRoles[options] = SampleOptionRole(sel);
                        _wireOptionWidgets[options] = sel;
                        _wireOptionStates[options++] = SampleOptionState(sel, root);
                    }
                }
                if (WireLinesScratch.Length > 0)
                    lines = WireLinesScratch.ToString();
                if (lines != null)
                    widgetFlags = SampleWidgetNumbers(out damage);
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("WorldUI", $"DECISION DOCK: wire decision sample failed ({e.Message}) — " +
                                  "peers keep the plain drawer this cadence.");
            lines = null;
            options = 0;
            widgetFlags = 0;
            damage = 0;
        }
        // Everything past what this walk published is not an option any more — null it so the
        // per-frame pointer pass can never read a widget belonging to a row that is gone.
        for (int i = lines == null ? 0 : options; i < _wireOptionWidgets.Length; i++)
            _wireOptionWidgets[i] = null;
        PublishWireDecision(lines, lines == null ? NetProtocol.DecisionKindNone : PromptKindCode(),
            lines == null ? 0 : options, lines == null ? (byte)0 : widgetFlags,
            lines == null ? (byte)0 : damage);
    }

    /// <summary>
    /// Has a POINTER bit moved since the last publish — i.e. has the owner put their beam on a
    /// different option, taken it off, or pressed / released one?
    ///
    /// <para>WHY THIS EXISTS AT ALL: the rest of the decision sample runs on a 0.25 s cadence,
    /// which is right for what it measures (a toggle state, a damage number — facts that change
    /// when the owner commits something). A hover is not that kind of fact. Sampled at 4 Hz, a beam
    /// sweeping the row would reach a peer as a stutter that lands on options the owner never
    /// stopped on, and a press shorter than a quarter second would never be seen at all. So the
    /// pointer bits get their own test, run every tick, and a move CLEARS THE PUBLISH GATE — the
    /// same bypass the focus-hidden withdrawal above uses, for the same reason: some things must
    /// land on the tick they happen.</para>
    ///
    /// <para>It is cheap on purpose. No walk, no <c>GetComponentsInChildren</c>, no string: at most
    /// eight cached widgets, two dictionary probes each, and nothing is written — the FULL walk
    /// stays the only writer of <see cref="_wireOptionStates"/>, so this can never disagree with
    /// what is published. It is a question, not a second sampler.</para>
    /// </summary>
    private static bool PointerBitsMoved()
    {
        const byte pointerBits =
            NetProtocol.DecisionOptionHoveredBit | NetProtocol.DecisionOptionPressedBit;
        int n = _wireOptionCount;
        if (n > _wireOptionWidgets.Length)
            n = _wireOptionWidgets.Length;
        for (int i = 0; i < n; i++)
        {
            Selectable? sel = _wireOptionWidgets[i];
            if (sel == null)
                continue; // the widget died under us — the next full walk settles the row
            if (SamplePointerBits(sel) != (byte)(_wireOptionStates[i] & pointerBits))
                return true;
        }
        return false;
    }

    /// <summary>
    /// THE OWNER-SIDE POINTER BITS for one option — hovered (bit 3) and pressed (bit 4) of wire
    /// record 24, the only two facts in that record a receiver cannot derive for itself.
    ///
    /// <para>THE SOURCE IS THE MOD PLUMBING, NOT THE WIDGET, and that is deliberate.
    /// <c>Selectable</c> keeps its hover and press latches private and <c>UnityEngine.UI</c> is not
    /// publicized, so the widget cannot be asked directly. But it does not need to be: every VR
    /// pointer path — both lasers, both fingertip pokes — funnels its enter/exit through
    /// <c>UguiHoverTracker</c> and its down/up through <c>UguiPressTracker</c>, and those tables ARE
    /// what drives the highlight the owner is looking at. Reading them is reading the cause of the
    /// picture rather than guessing at it, which is the same move
    /// <c>Net.InitiativeHoverSampler</c> makes when it polls the avatar own <c>highlighted</c>
    /// latch.</para>
    ///
    /// <para>WHAT IT DOES NOT SEE, said plainly rather than left to be discovered: the DESKTOP
    /// MOUSE. A flat-screen mouse hover would light the owner widget without appearing in either
    /// table. In VR that pointer is deliberately cut off the world surfaces
    /// (<c>WorldUI.Patches.MouseWorldSurfaceCut</c>) and the decision row is a world surface, so
    /// the gap is closed by construction here — but it is a gap in the SOURCE, not an oversight,
    /// and if a flat-screen path ever docks this row it is the thing to revisit.</para>
    ///
    /// <para>A NON-INTERACTABLE OPTION REPORTS NEITHER. Unity own state machine gives the disabled
    /// tint priority over highlighted and pressed alike, so a greyed widget that happens to sit
    /// under the beam still draws greyed on the owner screen. Publishing a hover for it would make
    /// a peer paint a picture the owner never sees.</para>
    /// </summary>
    private static byte SamplePointerBits(Selectable sel)
    {
        try
        {
            if (!sel.IsInteractable())
                return 0;
            GameObject go = sel.gameObject;
            byte flags = 0;
            if (Hands.Interact.UguiHoverTracker.IsHovered(go))
                flags |= NetProtocol.DecisionOptionHoveredBit;
            if (Hands.Interact.UguiPressTracker.IsPressed(go))
                flags |= NetProtocol.DecisionOptionPressedBit;
            return flags;
        }
        catch (System.Exception)
        {
            return 0; // no pointer is the safe answer: the peer keeps the plain, un-hovered look
        }
    }

    /// <summary>
    /// WHICH GAME WIDGET one docked option IS, as wire record 29's role code — resolved by
    /// REFERENCE against the prompt's own serialized fields, never by name and never by index.
    ///
    /// <para>That is the whole point of the record: a receiver owns the same prompt prefab, so a
    /// role lets it resolve the option to ITS OWN copy of that widget and mirror the real button —
    /// the game's art, the game's icons, the wording in the VIEWER's language. An option this build
    /// does not code (the short-rest <c>YesNoDialog</c>, whose dialog belongs to a HAND, and a
    /// <c>DialogPopup</c>'s pooled option buttons, created and labelled per prompt — see
    /// <c>NetProtocol.DecisionRoleMax</c> for why neither is resolvable on a peer) publishes
    /// <c>DecisionRoleUnknown</c>, and the receiver falls back to the mod-drawn plate with record
    /// 12's wording — the pre-record look, which is the safe direction.</para>
    /// </summary>
    private static byte SampleOptionRole(Selectable sel)
    {
        try
        {
            TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                ? Singleton<TakeDamagePanel>.Instance
                : null;
            if (p != null)
            {
                if (p.burnAvailableCardsToggle != null
                    && ReferenceEquals(sel.transform, p.burnAvailableCardsToggle.transform))
                    return NetProtocol.DecisionRoleBurnAvailable;
                if (p.burnDiscardedCardsToggle != null
                    && ReferenceEquals(sel.transform, p.burnDiscardedCardsToggle.transform))
                    return NetProtocol.DecisionRoleBurnDiscarded;
                if (p.takeDamageButton != null
                    && ReferenceEquals(sel.transform, p.takeDamageButton.transform))
                    return NetProtocol.DecisionRoleTakeDamage;
            }
        }
        catch (System.Exception)
        {
            // Unattributable is a legal answer and the safe one — never a guessed role.
        }
        return NetProtocol.DecisionRoleUnknown;
    }

    /// <summary>
    /// The take-damage option's own RUNTIME PICTURE for wire record 29: the damage number it
    /// displays and the three booleans that decide how it displays it. Read off the game's OWN
    /// widgets, so it is by construction what the owner is looking at.
    ///
    /// <para>WHY IT HAS TO TRAVEL AT ALL, when the peer's <c>TakeDamagePanel</c> exists too:
    /// <c>ShowOtherPlayer</c> stores the numbers but never re-paints the widget row (it ends in
    /// <c>myWindow.Hide(instant: true)</c>, TakeDamagePanel.cs:1102-1134), and the amount moves LIVE
    /// on the deciding client as they toggle shield items and damage-preventing bonuses. So the
    /// peer's own copy of the button carries a stale number and the wrong icon; these four bits are
    /// what make the mirrored widget say what the owner's says.</para>
    ///
    /// <para>A DISPLAYED HP NUMBER IS NOT AN IDENTITY: every client already renders the same
    /// preview on the attacked actor's world-space health bar
    /// (<c>WorldspacePanelUIController.PreviewSimpleDamage</c>, called from <c>ShowOtherPlayer</c>
    /// itself). Nothing here reads a card, an ability or an item.</para>
    /// </summary>
    private static byte SampleWidgetNumbers(out byte damage)
    {
        damage = 0;
        byte flags = 0;
        try
        {
            TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                ? Singleton<TakeDamagePanel>.Instance
                : null;
            if (p == null)
                return 0;
            if (p.fatalDamageObject != null && p.fatalDamageObject.activeInHierarchy)
                flags |= NetProtocol.DecisionWidgetLethalBit;
            if (p.mandatoryTakeDamageHighlight != null
                && p.mandatoryTakeDamageHighlight.activeInHierarchy)
                flags |= NetProtocol.DecisionWidgetMandatoryBit;
            if (p.damageAmount != null)
            {
                if (p.damageAmount.color == p.shieldAppliedColor)
                    flags |= NetProtocol.DecisionWidgetShieldedBit;
                // The game writes this text itself (RefreshDamageInformation), so parsing it back
                // is reading the very glyphs the owner sees. An unparsable or out-of-range value
                // publishes NO number at all rather than a wrong one.
                if (int.TryParse(p.damageAmount.text, out int value) && value >= 0 && value <= 255)
                {
                    damage = (byte)value;
                    flags |= NetProtocol.DecisionWidgetDamageValidBit;
                }
            }
        }
        catch (System.Exception)
        {
            damage = 0;
            return 0;
        }
        return flags;
    }

    /// <summary>
    /// The state of ONE docked option, as wire record 23 describes it: OFFERED when the game says
    /// the owner may press it (<c>Selectable.IsInteractable</c> — the very flag
    /// <c>CardsDriver.TickTakeDamageOptions</c> re-asserts from the panel's own formula), DIMMED
    /// when a <c>CanvasGroup</c> between the widget and the row root holds it below full alpha (the
    /// game's 0.7 "your character does not have the cards for this" look, which is a DIFFERENT
    /// picture from merely un-pressable and must not be collapsed into it), and CHOSEN when it is a
    /// <c>Toggle</c> that is currently on (the burn option the owner has picked but not committed).
    ///
    /// <para>Plus, since ModBuild 300, the two POINTER bits — see
    /// <see cref="SamplePointerBits"/>. They are folded in here rather than sampled separately so
    /// this method stays the ONE writer of an option state byte: the per-frame pass that watches
    /// them only ever asks a question.</para>
    /// </summary>
    private static byte SampleOptionState(Selectable sel, RectTransform root)
    {
        byte flags = SamplePointerBits(sel);
        if (sel.IsInteractable())
            flags |= NetProtocol.DecisionOptionOfferedBit;
        if (sel is Toggle toggle && toggle.isOn)
            flags |= NetProtocol.DecisionOptionChosenBit;
        // Walk widget → row root (never past it: the host's own group is the mod's, not the
        // prompt's) and take the lowest alpha any group on the way imposes.
        float alpha = 1f;
        Transform? t = sel.transform;
        while (t != null)
        {
            var group = t.GetComponent<CanvasGroup>();
            if (group != null && group.alpha < alpha)
                alpha = group.alpha;
            if (ReferenceEquals(t, root))
                break;
            t = t.parent;
        }
        if (alpha < 0.999f)
            flags |= NetProtocol.DecisionOptionDimmedBit;
        return flags;
    }

    /// <summary>The wire code (<c>NetProtocol.DecisionKind*</c>) of the prompt currently docked.
    /// A prompt this build does not classify publishes <c>DecisionKindNone</c>, which peers render
    /// as "mirror the plates, draw no prompt text" — the safe direction.</summary>
    private byte PromptKindCode() => _active?.Name switch
    {
        "TakeDamagePanel" => NetProtocol.DecisionKindTakeDamage,
        "YesNoDialog" => NetProtocol.DecisionKindShortRestYesNo,
        "DialogPopup" => NetProtocol.DecisionKindDialogPopup,
        _ => NetProtocol.DecisionKindNone,
    };

    /// <summary>Publish (change-gated) what the THREE decision records carry, and log the change
    /// once. The values move together — the labels (12), their states (24) and their widget roles +
    /// numbers (29) describe ONE row — so they share one gate and one line.</summary>
    private static void PublishWireDecision(string? lines, byte kind, int options,
        byte widgetFlags, byte damage)
    {
        bool same = lines == WireButtonLines && kind == WirePromptKind && options == _wireOptionCount
                    && widgetFlags == WireWidgetFlags && damage == WireDamageAmount;
        if (same && lines != null)
        {
            // Same row, same count: the STATES and the ROLES may still have moved (a toggle
            // flipped, the game re-asserted a gate, a widget the owner could not see became
            // active) — a real change peers must see, so compare them too.
            for (int i = 0; i < options; i++)
            {
                if (_wireOptionStates[i] != _publishedOptionStates[i]
                    || _wireOptionRoles[i] != _publishedOptionRoles[i])
                {
                    same = false;
                    break;
                }
            }
        }
        if (same)
            return;
        WireButtonLines = lines;
        WirePromptKind = kind;
        WireWidgetFlags = widgetFlags;
        WireDamageAmount = damage;
        _wireOptionCount = options;
        for (int i = 0; i < options; i++)
        {
            _publishedOptionStates[i] = _wireOptionStates[i];
            _publishedOptionRoles[i] = _wireOptionRoles[i];
        }
        if (lines == null)
        {
            VRLog.Info("WorldUI", "DECISION DOCK: wire decision cleared (no visible row) — records 12, " +
                                  "24 and 29 stop riding, so every peer's mirrored decision empties too.");
            return;
        }
        var states = new System.Text.StringBuilder(64);
        for (int i = 0; i < options; i++)
        {
            if (i > 0)
                states.Append(", ");
            byte f = _wireOptionStates[i];
            states.Append('#').Append(i).Append('=')
                  .Append((f & NetProtocol.DecisionOptionOfferedBit) != 0 ? "OFFERED" : "greyed");
            if ((f & NetProtocol.DecisionOptionDimmedBit) != 0)
                states.Append("+dim");
            if ((f & NetProtocol.DecisionOptionChosenBit) != 0)
                states.Append("+CHOSEN");
            if ((f & NetProtocol.DecisionOptionHoveredBit) != 0)
                states.Append("+HOVER");
            if ((f & NetProtocol.DecisionOptionPressedBit) != 0)
                states.Append("+PRESS");
            states.Append("/role ").Append(RoleName(_wireOptionRoles[i]));
        }
        VRLog.Info("WorldUI", $"DECISION DOCK: wire decision published — {lines.Split('\n').Length} " +
                              $"label(s) \"{lines.Replace('\n', '|')}\", prompt kind {kind}, states " +
                              $"[{states}] (record 12: pressable-widget labels only, never a dialog's " +
                              "description text; record 24: the states + the prompt kind; record 29: " +
                              $"the widget ROLES above plus damage " +
                              $"{((widgetFlags & NetProtocol.DecisionWidgetDamageValidBit) != 0 ? damage.ToString() : "n/a")}" +
                              $"{((widgetFlags & NetProtocol.DecisionWidgetLethalBit) != 0 ? " LETHAL" : string.Empty)}" +
                              $"{((widgetFlags & NetProtocol.DecisionWidgetShieldedBit) != 0 ? " shielded" : string.Empty)}" +
                              $"{((widgetFlags & NetProtocol.DecisionWidgetMandatoryBit) != 0 ? " mandatory-hl" : string.Empty)}" +
                              "). A peer with a role for an option clones ITS OWN copy of that game " +
                              "widget — real art, real icons, its own language; an option with role " +
                              "'unknown' still falls back to the mod-drawn plate + the record-12 wording.");
    }

    /// <summary>Human-readable role for the published diagnostic (a hardware log has to say WHICH
    /// widget a peer was told to clone, or a "the buttons look wrong" report is unanswerable).</summary>
    private static string RoleName(byte role) => role switch
    {
        NetProtocol.DecisionRoleBurnAvailable => "burn-available",
        NetProtocol.DecisionRoleBurnDiscarded => "burn-discarded",
        NetProtocol.DecisionRoleTakeDamage => "take-damage",
        _ => "unknown",
    };

    /// <summary>The option states last PUBLISHED — the change gate's memory, so a toggle flip is
    /// detected without re-sampling twice per tick.</summary>
    private static readonly byte[] _publishedOptionStates =
        new byte[Net.NetProtocol.DecisionStateMaxOptions];

    /// <summary>HMD-anchored fallback float (the ModalFallback.PlaceAtHmd pattern). True when placed.</summary>
    private bool TryPlaceAtHmd()
    {
        if (Panel == null)
            return false;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return false;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (FloatDistanceMeters * scale);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(Panel, pos, rot, scale * FloatScaleFactor);
        return true;
    }

    // ---- docked-row adjustments (users #5 + #7a) ----------------------------------------

    /// <summary>
    /// One-shot per dock. USER #5 — antique restyle only (the text↔widget gap is no longer
    /// an in-row layout shuffle; it is driven by the docked block's PLACEMENT, see
    /// <see cref="Place"/>). Every Selectable background in the docked row is multiply-
    /// tinted toward the mod's dark-wood/aged-brass board-button family and its labels turn
    /// parchment gold (<see cref="NativeButtonSkin.LabelColor"/>), so the docked native
    /// buttons ("Auswahl beenden", Ja/Nein, burn choices …) read like the VR-settings gear
    /// instead of the game's default look. Everything is recorded and handed back by
    /// <see cref="RestoreRowAdjustments"/> on undock — live game widgets are never
    /// permanently mutated.
    /// </summary>
    private void AdjustDockedRow()
    {
        RestoreRowAdjustments(); // never double-record
        RectTransform? root = Panel?.Target;
        if (root == null)
            return;

        SelectableScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, SelectableScratch);
        int styled = 0;
        for (int i = 0; i < SelectableScratch.Count; i++)
        {
            Selectable sel = SelectableScratch[i];
            if (sel == null)
                continue;
            Graphic? bg = sel.targetGraphic != null ? sel.targetGraphic : sel.image;
            if (bg != null)
            {
                _tintedGraphics.Add((bg, bg.color));
                bg.color = bg.color * AntiqueTint;
                styled++;
            }
            TextScratch.Clear();
            sel.GetComponentsInChildren(includeInactive: false, TextScratch);
            for (int t = 0; t < TextScratch.Count; t++)
            {
                TMP_Text label = TextScratch[t];
                if (label == null)
                    continue;
                _tintedGraphics.Add((label, label.color));
                Color gold = NativeButtonSkin.LabelColor;
                label.color = new Color(gold.r, gold.g, gold.b, label.color.a);
            }
        }

        VRLog.Info("WorldUI", $"DECISION DOCK: row adjusted — {styled} widget background(s) antique-tinted " +
                              "(dark-wood/brass + parchment labels, the VR-settings-button style); the " +
                              "text↔widget gap is now driven by the DecisionGap placement (see Place).");
    }

    /// <summary>
    /// USER #14 STEP 1 — ONE-TIME GROUND TRUTH: on the first TakeDamage dock, dump BOTH the
    /// isolated docked row subtree (<c>Panel.Target</c>) AND the whole
    /// <c>TakeDamagePanel.Instance</c> window subtree to the log, indented by depth, with
    /// each node's active-in-hierarchy state, its TMP text (trimmed) + glyph-true Y bounds
    /// or its Image/Selectable rect Y bounds (each in ITS OWN subtree-root local space), and
    /// a marker for the three serialized widget fields. This resolves — from the next
    /// hardware log — whether the "Schadensphase…" prompt text lives inside the isolated row
    /// (then a measurement bug, H2) or is a sibling/banner OUTSIDE it (H1). Cheap, guarded by
    /// a static latch, kept in the shipped build.
    /// </summary>
    private void DumpTakeDamageHierarchyOnce()
    {
        if (_takeDamageDumped || _active?.Name != "TakeDamagePanel")
            return;
        RectTransform? root = Panel?.Target;
        TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;
        if (root == null || p == null)
            return;
        _takeDamageDumped = true;

        var widgets = new HashSet<Transform>();
        if (p.burnAvailableCardsToggle != null) widgets.Add(p.burnAvailableCardsToggle.transform);
        if (p.burnDiscardedCardsToggle != null) widgets.Add(p.burnDiscardedCardsToggle.transform);
        if (p.takeDamageButton != null) widgets.Add(p.takeDamageButton.transform);

        var sb = new System.Text.StringBuilder(4096);
        sb.Append("DECISION DOCK DIAGNOSTIC (one-time, first TakeDamage dock) — ground-truth hierarchy.\n");
        sb.Append("Y bounds are subtree-root-local px [bottom..top]; <WIDGET> marks a serialized field.\n");
        sb.Append("=== A. isolated docked ROW subtree (Panel.Target) ===\n");
        DumpSubtree(sb, root, root, 0, widgets);
        if (p.transform is RectTransform windowRt)
        {
            sb.Append("=== B. TakeDamagePanel WINDOW subtree (Singleton<TakeDamagePanel>.Instance) ===\n");
            DumpSubtree(sb, windowRt, windowRt, 0, widgets);
        }
        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>Recursively append one transform (and its children) to the diagnostic dump.</summary>
    private static void DumpSubtree(System.Text.StringBuilder sb, RectTransform reference, Transform node,
        int depth, HashSet<Transform> widgets)
    {
        sb.Append(' ', depth * 2);
        sb.Append(node.name).Append(" [active=").Append(node.gameObject.activeInHierarchy).Append(']');
        if (widgets.Contains(node))
            sb.Append(" <WIDGET>");
        var tmp = node.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            string t = tmp.text ?? string.Empty;
            if (t.Length > 40) t = t.Substring(0, 40);
            t = t.Replace("\n", "\\n").Replace("\r", string.Empty);
            sb.Append(" TMP \"").Append(t).Append("\" glyphY=[")
              .Append(GlyphEdgeIn(reference, tmp, top: false).ToString("F0")).Append("..")
              .Append(GlyphEdgeIn(reference, tmp, top: true).ToString("F0")).Append(']');
        }
        else if (node is RectTransform rt && (node.GetComponent<Graphic>() != null || node.GetComponent<Selectable>() != null))
        {
            string kind = node.GetComponent<Selectable>() != null ? "SEL" : "IMG";
            sb.Append(' ').Append(kind).Append(" rectY=[")
              .Append(EdgeYIn(reference, rt, min: true).ToString("F0")).Append("..")
              .Append(EdgeYIn(reference, rt, min: false).ToString("F0")).Append(']');
        }
        sb.Append('\n');
        for (int i = 0; i < node.childCount; i++)
            DumpSubtree(sb, reference, node.GetChild(i), depth + 1, widgets);
    }

    /// <summary>
    /// The active prompt's actionable widget ROOT RECTS, straight from the game's own
    /// serialized fields (no structural guessing): TakeDamagePanel's two burn toggles +
    /// take-damage button, YesNoDialog's yes/no buttons, DialogPopup's pooled option
    /// buttons. These are the ONLY rects the gap fix ever moves — leaf displacement,
    /// never a shared ancestor (user #13a). Empty list → the caller logs and no-ops.
    /// </summary>
    private void ResolvePromptWidgets(List<RectTransform> widgets)
    {
        widgets.Clear();
        switch (_active?.Name)
        {
            case "TakeDamagePanel":
            {
                TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                    ? Singleton<TakeDamagePanel>.Instance
                    : null;
                if (p == null)
                    return;
                AddWidgetRect(widgets, p.burnAvailableCardsToggle != null ? p.burnAvailableCardsToggle.transform : null);
                AddWidgetRect(widgets, p.burnDiscardedCardsToggle != null ? p.burnDiscardedCardsToggle.transform : null);
                AddWidgetRect(widgets, p.takeDamageButton != null ? p.takeDamageButton.transform : null);
                return;
            }
            case "YesNoDialog":
            {
                YesNoDialog? d = CardsGameApi.ShortRestDialog();
                if (d == null)
                    return;
                AddWidgetRect(widgets, d.yesButton != null ? d.yesButton.transform : null);
                AddWidgetRect(widgets, d.noButton != null ? d.noButton.transform : null);
                return;
            }
            case "DialogPopup":
            {
                UIManager? m = UIManager.Instance;
                DialogPopup? d = m != null ? m.dialogPopup : null;
                List<InputButton>? buttons = d != null ? d.optionButtons : null;
                if (buttons == null)
                    return;
                for (int i = 0; i < buttons.Count; i++)
                {
                    InputButton ib = buttons[i];
                    if (ib != null && ib.gameObject.activeInHierarchy && ib.ExtendedButton != null)
                        AddWidgetRect(widgets, ib.ExtendedButton.transform);
                }
                return;
            }
        }
    }

    private static void AddWidgetRect(List<RectTransform> widgets, Transform? t)
    {
        if (t is RectTransform rt && !widgets.Contains(rt))
            widgets.Add(rt);
    }

    /// <summary>
    /// USER #14 placement measurement, extended for the bar-stack gap fix: the top- and
    /// bottom-most VISIBLE widget graphics of the active prompt, expressed in world metres
    /// ABOVE the host pivot along <paramref name="up"/> (translation-invariant — measured
    /// relative to the host's current position, valid for the position <see cref="Place"/>
    /// is about to solve). Glyph-true for TMP labels, rect corners for plates/images. Falls
    /// back to <paramref name="rowTopAbovePivot"/>/<paramref name="rowBottomAbovePivot"/>
    /// (the fitted row edges) when the prompt exposes no resolvable widget graphics. The TOP
    /// anchors the BUTTONS themselves — not the authored option-column rects — so the gap the
    /// user tunes is the real distance from the board edge to the pressable widgets; the
    /// BOTTOM (one walk, same visibility rules — top and bottom can never disagree) feeds
    /// <see cref="RowBottomUpMeters"/> so the use-bars stack hangs directly under the row.
    /// </summary>
    private void WidgetBlockEdgesAbovePivot(Transform host, Vector3 up,
        float rowTopAbovePivot, float rowBottomAbovePivot, out float top, out float bottom)
    {
        ResolvePromptWidgets(WidgetRectScratch);
        Vector3 origin = host.position;
        top = float.MinValue;
        bottom = float.MaxValue;
        for (int i = 0; i < WidgetRectScratch.Count; i++)
        {
            RectTransform w = WidgetRectScratch[i];
            if (w == null)
                continue;
            GraphicScratch.Clear();
            w.GetComponentsInChildren(includeInactive: false, GraphicScratch);
            for (int g = 0; g < GraphicScratch.Count; g++)
            {
                Graphic gr = GraphicScratch[g];
                if (gr == null)
                    continue;
                if (gr is TMP_Text label)
                {
                    label.ForceMeshUpdate();
                    Bounds b = label.textBounds;
                    if (string.IsNullOrEmpty(label.text) || b.size.y <= 0.001f)
                    {
                        top = Mathf.Max(top, WorldUpEdge(label.rectTransform, origin, up, topEdge: true));
                        bottom = Mathf.Min(bottom, WorldUpEdge(label.rectTransform, origin, up, topEdge: false));
                    }
                    else
                    {
                        Vector3 hi = label.transform.TransformPoint(new Vector3(b.center.x, b.max.y, 0f));
                        Vector3 lo = label.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f));
                        top = Mathf.Max(top, Vector3.Dot(hi - origin, up));
                        bottom = Mathf.Min(bottom, Vector3.Dot(lo - origin, up));
                    }
                }
                else
                {
                    var rt = (RectTransform)gr.transform;
                    top = Mathf.Max(top, WorldUpEdge(rt, origin, up, topEdge: true));
                    bottom = Mathf.Min(bottom, WorldUpEdge(rt, origin, up, topEdge: false));
                }
            }
        }
        if (top <= float.MinValue)
        {
            top = rowTopAbovePivot;
            bottom = rowBottomAbovePivot;
        }
    }

    /// <summary>Highest/lowest of a rect's four world corners projected onto <paramref name="up"/>, relative to <paramref name="origin"/>.</summary>
    private static float WorldUpEdge(RectTransform rt, Vector3 origin, Vector3 up, bool topEdge)
    {
        rt.GetWorldCorners(CornerScratch);
        float edge = topEdge ? float.MinValue : float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float y = Vector3.Dot(CornerScratch[i] - origin, up);
            edge = topEdge ? Mathf.Max(edge, y) : Mathf.Min(edge, y);
        }
        return edge;
    }

    /// <summary>
    /// Glyph-true top/bottom edge (root-local Y) of a TMP label: the rendered text
    /// bounds, not the authored rect — dialog labels are routinely authored in rects far
    /// taller than their glyphs, which made rect-based gaps lie (user #11b). Falls back
    /// to the rect edge when the label has no rendered glyphs.
    /// </summary>
    private static float GlyphEdgeIn(RectTransform root, TMP_Text label, bool top)
    {
        label.ForceMeshUpdate(); // one-shot per dock; the row was just reparented, ensure fresh bounds
        Bounds b = label.textBounds;
        if (string.IsNullOrEmpty(label.text) || b.size.x <= 0.001f || b.size.y <= 0.001f)
            return EdgeYIn(root, label.rectTransform, min: !top);
        Vector3 world = label.transform.TransformPoint(new Vector3(b.center.x, top ? b.max.y : b.min.y, 0f));
        return root.InverseTransformPoint(world).y;
    }

    /// <summary>Min/max local-Y of a rect's corners expressed in <paramref name="root"/> space.</summary>
    private static float EdgeYIn(RectTransform root, RectTransform rt, bool min)
    {
        rt.GetWorldCorners(CornerScratch);
        float edge = min ? float.MaxValue : float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float y = root.InverseTransformPoint(CornerScratch[i]).y;
            edge = min ? Mathf.Min(edge, y) : Mathf.Max(edge, y);
        }
        return edge;
    }

    /// <summary>Undo <see cref="AdjustDockedRow"/> — the antique tint colours back to the game's own values (placement leaves the row's own transforms untouched).</summary>
    private void RestoreRowAdjustments()
    {
        for (int i = 0; i < _tintedGraphics.Count; i++)
        {
            if (_tintedGraphics[i].graphic != null)
                _tintedGraphics[i].graphic.color = _tintedGraphics[i].color;
        }
        _tintedGraphics.Clear();
    }

    // ---- deliberate poke press-mode (user #13b) -------------------------------------------

    /// <summary>
    /// USER #13b: decision buttons take the DELIBERATE v1 poke press — plane contact
    /// only ARMS (pressed visual + light haptic), the click fires on the conscious
    /// WITHDRAWAL back past ReleaseDepth, and sweep-throughs/side-exits cancel silently
    /// — while every other converted surface keeps the v3 push-in depth-fire
    /// (PokePressDepthMm). The dock tags its host canvas in
    /// <see cref="Hands.Interact.DeliberatePokeSurfaces"/> per dock; the interactor
    /// reads [WorldUI] DecisionPokeDeliberate LIVE, so that config is a pure escape
    /// hatch (false → decision buttons press like everything else, no re-dock needed).
    /// </summary>
    private void RegisterDeliberateCanvas()
    {
        Canvas? host = Panel?.HostCanvas;
        if (host == null)
            return;
        _deliberateCanvas = host;
        Hands.Interact.DeliberatePokeSurfaces.Register(host, $"decision dock '{_active?.Name}'");
    }

    private void UnregisterDeliberateCanvas()
    {
        if (_deliberateCanvas is not null)
        {
            Hands.Interact.DeliberatePokeSurfaces.Unregister(_deliberateCanvas);
            _deliberateCanvas = null;
        }
    }

    // ---- one character owns a decision (user ruling 2026-08-08) --------------------------

    /// <summary>
    /// WHICH CHARACTER does the open prompt belong to — resolved from the GAME'S OWN MODEL, never
    /// from turn state. That distinction is the requirement, not a preference: a decision routinely
    /// outlives the turn that raised it (a take-damage prompt is raised DURING an enemy's turn for
    /// a hero who is not acting; the boots' ± choice walks the party outside anybody's turn;
    /// <c>CheckForForgoActionActiveBonuses</c> is a phase of its own the game SITS IN until the
    /// prompt is answered), so <c>Choreographer.CurrentActor</c> would attribute half of them to
    /// the wrong character or to nobody.
    ///
    /// <list type="bullet">
    /// <item><b>TakeDamagePanel</b> → <c>actorToShowCardsFor ?? actorBeingAttacked</c>. Both are
    ///   serialized on the panel (TakeDamagePanel.cs:97/99, publicized) and they are the very pair
    ///   the game's own control test keys on (<c>ThisPlayerHasTakeDamageControl</c>, :133-141):
    ///   when they differ, the burn pick targets <c>actorToShowCardsFor</c>'s hand, so that is the
    ///   character the decision is FOR. A summon resolves to its Summoner through
    ///   <see cref="CardsGameApi.TakeDamageSubject"/>, the same mapping the initiative track uses.</item>
    /// <item><b>YesNoDialog</b> (short-rest confirmation) → the hand whose <c>ShortRest</c> owns
    ///   this very dialog instance (<c>CardsHandUI.shortRest.yesNoDialog</c>, instantiated per hand
    ///   by <c>ShortRest.Init</c>, ShortRest.cs:94-98). Identity match, not a name or a guess.</item>
    /// <item><b>DialogPopup</b> → first the SHORT-REST burn/redraw confirm: the hand the game is
    ///   short-resting is the one whose <c>ShortRestedCard</c> is non-null (set synchronously right
    ///   before the popup is shown, CardsHandUI.cs:826/850, nulled in FinalizeShortRest :973 — so
    ///   there is no frame where the popup is up and the owner unknown). Otherwise the PICK confirm,
    ///   whose owner is the hand whose <c>OnCardSelected</c> opened it —
    ///   <c>CardsHandManager.CurrentHand</c> in a modal pick mode, the exact attribution
    ///   <see cref="CardsGameApi.PickConfirmCancelButton"/> already relies on.</item>
    /// </list>
    ///
    /// <para>Returns null when the prompt is NOT attributable to one character (a scenario-choice
    /// DialogPopup, a half-torn model, an unknown future prompt). Null means "belongs to everyone"
    /// and the row is ALWAYS shown — the failure direction has to be visible, because an invisible
    /// prompt nobody can answer is the deadlock this whole surface exists to prevent.</para>
    /// </summary>
    private CPlayerActor? PromptOwner()
    {
        try
        {
            switch (_active?.Name)
            {
                case "TakeDamagePanel":
                {
                    TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                        ? Singleton<TakeDamagePanel>.Instance
                        : null;
                    if (p == null || !p.IsOpen)
                        return null;
                    CPlayerActor? cards = p.actorToShowCardsFor;
                    return cards != null ? cards : CardsGameApi.TakeDamageSubject();
                }

                case "YesNoDialog":
                {
                    YesNoDialog? dialog = CardsGameApi.ShortRestDialog();
                    if (dialog == null)
                        return null;
                    List<CardsHandUI>? hands = AllHands();
                    if (hands == null)
                        return null;
                    for (int i = 0; i < hands.Count; i++)
                    {
                        CardsHandUI h = hands[i];
                        if (h != null && h.shortRest != null
                            && ReferenceEquals(h.shortRest.yesNoDialog, dialog))
                            return h.PlayerActor;
                    }
                    return null;
                }

                case "DialogPopup":
                {
                    List<CardsHandUI>? hands = AllHands();
                    if (hands != null)
                    {
                        for (int i = 0; i < hands.Count; i++)
                        {
                            CardsHandUI h = hands[i];
                            if (h != null && h.ShortRestedCard != null)
                                return h.PlayerActor;
                        }
                    }
                    CardsHandManager manager = CardsHandManager.Instance;
                    CardsHandUI? current = manager != null ? manager.CurrentHand : null;
                    if (current != null && CardsGameApi.IsPickConfirmDialogOpen(current))
                        return current.PlayerActor;
                    return null;
                }
            }
        }
        catch (System.Exception)
        {
            // Attribution is a PRESENTATION question; a half-torn model must never make a live
            // prompt disappear. Unknown owner ⇒ always shown (see the doc).
            return null;
        }
        return null;
    }

    /// <summary>Every hand widget the game built on this client (one per player actor —
    /// <c>CardsHandManager.CardHandsUI</c>, public). Null while the manager is gone.</summary>
    private static List<CardsHandUI>? AllHands()
    {
        CardsHandManager manager = CardsHandManager.Instance;
        return manager != null ? manager.CardHandsUI : null;
    }

    /// <summary>
    /// THE WHOLE DECISION DISPLAY BELONGS TO ONE CHARACTER (user, ModBuild 86 hardware test: "Der
    /// Text der Entscheidung zB 'Schadensphase: Erleide entweder Schaden, verbrenne …' ist immer
    /// noch sichtbar auch wenn man den Character wechselt — Buttons und Text also die GESAMTE
    /// Entscheidungsanzeige soll pro Character angezeigt werden").
    ///
    /// <para>A decision prompt is drawn by MORE THAN ONE mod surface. ModBuild 84/85 gave the rule
    /// to the docked widget ROW (<see cref="DecisionDockSurface"/>) and to the use bars
    /// (<see cref="UseBarsSurface"/>), but the take-damage prompt's persistent instruction TEXT is
    /// a THIRD surface — <see cref="DamageTooltipSurface"/> converts the game's <c>HelpBox</c>
    /// window and parks it just above the row — and it had no owner rule at all, so the sentence
    /// stayed on the board for every character. This class is the seam that makes "one more piece
    /// of the prompt" a two-line change instead of a fourth copy of the rule.</para>
    ///
    /// <para>ONE RESOLVER, ONE PREDICATE. <see cref="ShouldHide"/> is the ONLY place the
    /// <c>Focused</c>-vs-owner comparison is written, and the owner comes from the ONE
    /// <see cref="PromptOwner"/> switch on the live instance — there is deliberately no second
    /// attribution anywhere. Its fail-open contract rides along unchanged: no focus override, or an
    /// owner that cannot be resolved, ⇒ SHOWN, because an invisible prompt nobody can answer is the
    /// deadlock the dock exists to prevent.</para>
    ///
    /// <para>ONE LOG LINE. Each surface <see cref="Report"/>s what IT switched off; the roll-up is
    /// emitted from <see cref="Flush"/> at the top of the next <see cref="Tick"/> — i.e. after a
    /// COMPLETE frame in which every decision surface has had its say, in a fixed order that does
    /// not depend on which surface ticks last. Change-gated on the full combined state, so a focus
    /// flip costs exactly one line and a steady state costs none.</para>
    ///
    /// <para>MULTIPLAYER: nothing here touches the wire. Hiding is a LOCAL VIEW change, and record
    /// 12 (<see cref="WireButtonLines"/>) keeps riding while a piece is hidden for exactly the
    /// reason documented at the <see cref="UpdateFocusVisibility"/> call site — the peers' plate
    /// reports that this player has a pending decision, which is still true.</para>
    /// </summary>
    internal static class PromptFocus
    {
        /// <summary>What each surface reported this frame (finite: 3-4 entries).</summary>
        private static readonly List<(string surface, bool hidden, int canvases, int renderers, bool plate)>
            Reports = new(4);

        private static string _prompt = "none";
        private static string _owner = "?";
        private static string _focused = "?";
        private static string? _logged;

        /// <summary>
        /// SHARED VERDICT — true while the active decision prompt belongs to a character the player
        /// is NOT looking at, i.e. while every surface drawing a piece of it must be render-hidden.
        /// Owner attribution is delegated to the single <see cref="PromptOwner"/> switch on the live
        /// surface instance; no caller may re-derive it.
        ///
        /// ─── WHAT CHANGED (boots report, ModBuild 137) AND WHAT DELIBERATELY DID NOT ──────────
        /// <para>The character in view is <see cref="Board.CharacterFocus.PresentedActor"/> — what
        /// the board is ACTUALLY drawing — with the explicit override as fallback while the card
        /// pipeline has not resolved a hand yet. It used to be the override ALONE, which is the same
        /// latent hole the use bars were reported for: <c>Focused</c> is null whenever the player
        /// simply follows the game, and when it is non-null it can still name a character whose hand
        /// widget has not been built, so the comparison ran against a view that was not on screen.
        /// Both are now compared against the truth (<c>USE BARS</c>' <c>BAR FOCUS</c> line is the
        /// twin of this one).</para>
        ///
        /// <para>THE MULTIPLAYER CLAUSE THE USE BARS NEEDED IS NOT HERE, ON PURPOSE — the hole is
        /// not real for this surface, and adding a dead guard would cost a deadlock the day it fired
        /// on a misattribution. The bars are HUD singletons the game raises on EVERY client
        /// (<c>Choreographer.CheckForInitiativeAdjustments</c> → <c>UIActiveBonusBar.ShowActiveBonus</c>,
        /// gated only on the ready button); none of the three prompts this dock attributes behaves
        /// that way, verified one by one:</para>
        /// <list type="bullet">
        /// <item><b>TakeDamagePanel</b> — <c>UIScenarioMultiplayerController.RefreshDamagePhase</c>
        ///   (:212-242) computes the very same actor <see cref="PromptOwner"/> resolves and routes a
        ///   non-controlling client to <c>ShowOtherPlayer</c>, which ends in
        ///   <c>myWindow.Hide(instant: true)</c> (TakeDamagePanel.cs:1133). So the panel is OPEN only
        ///   where the owner is <c>IsUnderMyControl</c> (or the attacked actor is an enemy, where the
        ///   host owns it and <c>CardsGameApi.TakeDamageSubject</c> answers null ⇒ fail open). A
        ///   foreign owner with an open panel does not exist.</item>
        /// <item><b>YesNoDialog</b> (short-rest confirm) — instantiated per hand and shown only from
        ///   <c>ShortRest.Select</c> ← <c>MouseClick</c> (ShortRest.cs:202-250). No network path shows
        ///   it; a peer's rest never raises it here.</item>
        /// <item><b>DialogPopup</b> — the burn/redraw confirm is raised inside
        ///   <c>CardsHandUI.PerformShortRest</c>, which is the local <c>yesNoDialog</c> callback
        ///   (CardsHandUI.cs:1810), and the pick confirm belongs to the hand whose modal pick THIS
        ///   client opened. Both are local by construction.</item>
        /// </list>
        /// <para>Its fail-open contract rides along unchanged: no owner, or no character in view at
        /// all, ⇒ SHOWN — an invisible prompt nobody can answer is the deadlock this surface exists
        /// to prevent, and the take-damage prompt of the local player must always be answerable.</para>
        /// </summary>
        internal static bool ShouldHide(out CPlayerActor? owner, out CPlayerActor? inView)
        {
            owner = Instance != null ? Instance.PromptOwner() : null;
            CPlayerActor? focused = Board.CharacterFocus.Focused;
            inView = Board.CharacterFocus.PresentedActor ?? focused;
            return focused != null && owner != null && inView != null
                   && !ReferenceEquals(inView, owner);
        }

        /// <summary>The prompt/owner/looked-at context for the roll-up line (written by the dock,
        /// which is the surface that knows which prompt is open).</summary>
        internal static void SetContext(string? prompt, CPlayerActor? owner, CPlayerActor? focused)
        {
            _prompt = prompt ?? "none";
            _owner = Board.CharacterFocus.Describe(owner);
            _focused = Board.CharacterFocus.Describe(focused);
        }

        /// <summary>One surface's contribution to this frame's roll-up. Called every tick the
        /// surface is live (hidden or not), so a surface that simply stops reporting disappears from
        /// the line — which is itself the signal that it released.</summary>
        internal static void Report(string surface, bool hidden, int canvases, int renderers, bool plate)
        {
            for (int i = 0; i < Reports.Count; i++)
            {
                if (Reports[i].surface == surface)
                {
                    Reports[i] = (surface, hidden, canvases, renderers, plate);
                    return;
                }
            }
            Reports.Add((surface, hidden, canvases, renderers, plate));
        }

        /// <summary>
        /// Emit the previous frame's roll-up if it changed, then clear for the frame about to run.
        /// Called from the TOP of <see cref="Tick"/>: the dock is the first decision surface in
        /// <c>WorldUIModule.BuildTickSteps</c>, so everything reported since the last call is a full,
        /// settled frame.
        /// </summary>
        internal static void Flush()
        {
            if (Reports.Count == 0)
            {
                if (_logged != null)
                    _logged = null; // nothing live any more; the next prompt logs afresh
                return;
            }

            var sb = new System.Text.StringBuilder(160);
            int hiddenCount = 0;
            for (int i = 0; i < Reports.Count; i++)
            {
                var r = Reports[i];
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(r.surface).Append(r.hidden ? " HIDDEN" : " shown");
                if (r.hidden)
                {
                    sb.Append(" (").Append(r.canvases).Append(" canvas, ")
                      .Append(r.renderers).Append(" renderer")
                      .Append(r.plate ? ", MR plate" : ", no plate").Append(')');
                    hiddenCount++;
                }
            }
            string body = sb.ToString();
            int total = Reports.Count;
            string state = $"{_prompt}|{_owner}|{_focused}|{body}";
            Reports.Clear();
            if (_logged == state)
                return;
            _logged = state;
            VRLog.Info("WorldUI", $"DECISION DOCK FOCUS ROLL-UP: prompt '{_prompt}' belongs to '{_owner}', " +
                                  $"the player is looking at '{_focused}' — {hiddenCount} of {total} live " +
                                  "decision surface(s) render-hidden. Per surface: " + body +
                                  ". This line lists the WHOLE decision display (widget row, prompt text, " +
                                  "use bars); any piece that is 'shown' while the others are HIDDEN is a " +
                                  "leak of another character's decision.");
        }

        /// <summary>Module teardown: no prompt, no reports, next one logs afresh.</summary>
        internal static void Reset()
        {
            Reports.Clear();
            _logged = null;
            _prompt = "none";
            _owner = "?";
            _focused = "?";
        }
    }

    /// <summary>
    /// Level-triggered visibility of the docked row against the FOCUSED character. Hides only
    /// while BOTH facts hold: the player has taken an explicit focus
    /// (<c>Board.CharacterFocus.Focused</c> non-null — following the game is never "looking
    /// elsewhere"), and the prompt has a resolvable owner that is not that character. Either fact
    /// missing ⇒ shown, which keeps every pre-feature situation byte-for-byte as it was: a player
    /// who never touches the focus feature can never lose sight of a prompt.
    ///
    /// <para>The way back is automatic and needs no special case: focusing the owner makes
    /// <c>CharacterFocus.ResolveHand</c> drop the override the moment the game presents that hand
    /// ("the game now presents the focused character"), so <c>Focused</c> returns to null and this
    /// shows the row again — at the same geometry, since <see cref="Place"/> kept running.</para>
    ///
    /// <para>MULTIPLAYER: the flag this sets is <see cref="RowFocusHidden"/>, and since the
    /// 2026-08-08 ruling it is a WIRE input, not a private view state — the sampler that runs right
    /// after this publishes nothing while it holds, and <c>Net.NetAvatarDriver</c> clears the
    /// board-UI decision-drawer bit on it. So the hide is not "local": it is a change to what this
    /// board shows, and every peer's copy of this board follows it.</para>
    /// </summary>
    private void UpdateFocusVisibility()
    {
        // THE ONE PREDICATE, resolved once for every surface that draws a piece of this prompt
        // (see PromptFocus) — this surface no longer owns a private copy of it.
        bool hide = PromptFocus.ShouldHide(out CPlayerActor? owner, out CPlayerActor? focused);
        bool was = _rowHiddenForFocus;

        if (hide)
        {
            ApplyFocusHide();
        }
        else
        {
            RestoreFocusHide(null);
            // Re-place IMMEDIATELY on the un-hide tick. This method runs AFTER base.Tick's Place,
            // which — while the flag was still set — published RowBottomUpMeters as null; without
            // this, RowDocked would read true with no measured edge for the rest of the Update and
            // UseBarsSurface (which ticks after us) would stack against the worst-case fallback for
            // one frame. LateTick would correct it before the frame is drawn, but publishing the
            // real edge here keeps the two surfaces consistent within a single tick.
            if (was && Panel != null)
                Place();
        }

        // The count/plate fields are part of the dedup key on purpose: the hide is re-asserted every
        // docked tick, so a canvas or renderer that only appears LATER (a pooled option button, an
        // MR plate the sweep built a frame after the row docked) genuinely changes what is hidden
        // and deserves one more line. The sets are finite, so this can never become a per-frame log.
        // Roll-up context + this surface's own contribution: PromptFocus prints ONE line per flip
        // that names EVERY piece of the prompt and what each one switched off (the ModBuild 86
        // report was "the row hides, the TEXT stays" — a per-surface log could not have shown that
        // the text was a surface at all).
        PromptFocus.SetContext(_active?.Name, owner, focused);
        PromptFocus.Report(Name, hide, _focusHiddenCanvasCount, _focusHiddenRendererCount, _focusHiddenPlate);

        string state = $"{(hide ? "hidden" : "shown")}|{Board.CharacterFocus.Describe(owner)}|" +
                       $"{Board.CharacterFocus.Describe(focused)}|" +
                       $"{_focusHiddenCanvasCount}|{_focusHiddenRendererCount}|{_focusHiddenPlate}";
        if (_loggedFocusVisibility == state)
            return;
        _loggedFocusVisibility = state;
        if (hide)
            VRLog.Info("WorldUI", $"DECISION DOCK: '{_active?.Name}' belongs to " +
                                  $"'{Board.CharacterFocus.Describe(owner)}' and the player is looking at " +
                                  $"'{Board.CharacterFocus.Describe(focused)}' — the row is RENDER-HIDDEN: " +
                                  $"{_focusHiddenCanvasCount} canvas(es) and {_focusHiddenRendererCount} " +
                                  "renderer(s) disabled on the mod-owned host subtree + extra render roots, " +
                                  $"MR backing plate {(_focusHiddenPlate ? "INCLUDED (the empty dark rectangle is gone)" : "not present (no plate on this panel yet)")}. " +
                                  "The prompt itself is untouched: its " +
                                  "UIWindow is still open, its widgets keep their state, nothing was " +
                                  "answered, cancelled or closed, and it reappears unchanged the moment the " +
                                  "owner is focused again. MULTIPLAYER (ruling 2026-08-08): this board now " +
                                  "shows nothing at the decision seat, so it PUBLISHES nothing there either " +
                                  "— wire records 12 and 23 stop riding and the board-UI decision-drawer bit " +
                                  "clears, and every peer's mirrored copy of this board empties with it.");
        else
            VRLog.Info("WorldUI", $"DECISION DOCK: '{_active?.Name}' row VISIBLE — " +
                                  (owner == null
                                      ? "the prompt is not attributable to a single character, so it is " +
                                        "shown to whoever is looking (the safe direction: an unanswerable " +
                                        "prompt is a deadlock)."
                                      : $"owner '{Board.CharacterFocus.Describe(owner)}' is the character in " +
                                        "view" + (focused == null ? " (no focus override — following the game)." : ".")));
    }

    /// <summary>
    /// RENDER-HIDE the docked row — and NOTHING ELSE. The only things written are
    /// <c>Canvas.enabled = false</c> and <c>Renderer.enabled = false</c> on the mod's own converted
    /// host subtree (a nested canvas renders independently of its ancestors, the CanvasConversion
    /// lesson that <see cref="ApplySuppression"/> already applies to the window) and on the panel's
    /// registered extra render roots.
    ///
    /// <para>WHY RENDERERS TOO (hardware report ModBuild 84: "Der mixed-reality Hintergrund für die
    /// decision ist auch bei den anderen Characteren noch zu sehen aber leer"). The MIXED-REALITY
    /// BACKING PLATE is an opaque quad <c>MeshRenderer</c> that <see cref="MrBacking"/> parents
    /// under the host rect — it is not a Canvas and the uGUI path never touches it, so the
    /// canvas-only hide left it drawing: an empty dark rectangle exactly where the row had been,
    /// for every character that is not the prompt's owner. The walk is name-blind (every Renderer
    /// under the host goes off, so any future mod-drawn child is covered), and it also walks
    /// <c>ConvertedPanel.ExtraRenderRoots</c> — the <see cref="GrabbableModal"/> grab bar hangs off
    /// a scene-root holder OUTSIDE the host, so no host-subtree walk could ever reach it. The shared
    /// mechanism lives in <see cref="CanvasConversion.ApplyOwnerRenderHide"/>, which also sets
    /// <c>ConvertedPanel.OwnerRenderHidden</c> so <see cref="MrBacking"/> refuses to BUILD a plate
    /// for a hidden panel in the first place (this surface ticks earlier in the same Update than the
    /// plate sweep, so there is no one-frame plate flash).</para>
    ///
    /// <para>WHY NOT <c>SetActive(false)</c>, which would be the obvious hide. The row's children
    /// ARE the game's live widgets, so deactivating the host deactivates them, and the game's
    /// buttons have real <c>OnDisable</c> behaviour: <c>ExtendedButton.OnDisable</c> raises
    /// <c>ActiveChanged(false)</c>, un-highlights, and — when the EventSystem's selected object is
    /// that button — clears the selection and invokes <c>onDeselected</c> (ExtendedButton.cs:300-320).
    /// None of that is an answer, but all of it is the mod reaching into the prompt, which is
    /// exactly the property the focus feature is not allowed to lose. Disabling a Canvas COMPONENT
    /// runs no game code at all.</para>
    ///
    /// <para>WHY IT IS ALSO INPUT-TIGHT: both VR interactors skip a canvas that is not
    /// <c>isActiveAndEnabled</c> (<c>RayUguiDriver</c>:132/312 for the laser,
    /// <c>PokeInteractor</c>:298 for the fingertip), so a hidden row cannot be pressed by
    /// accident — the decision can only be answered while its owner is in view.</para>
    ///
    /// <para>Idempotent and re-asserted every docked tick, so a canvas the conversion adds later
    /// (a pooled option button bringing its own) is caught on the next frame.</para>
    /// </summary>
    private void ApplyFocusHide()
    {
        ConvertedPanel? panel = Panel;
        if (panel == null || panel.HostGo == null)
            return;
        bool first = !_rowHiddenForFocus;
        _rowHiddenForFocus = true;
        RowBottomUpMeters = null; // the use-bars stack must not hang off a row nobody can see
        // RowTopUpMeters deliberately KEEPS its value here: the prompt text hides on this same
        // verdict but keeps PLACING while hidden, so it must return at the row's final geometry
        // rather than at a fallback seat computed for one frame (see RowTopUpMeters).
        if (first)
        {
            _focusHiddenCanvasCount = 0;
            _focusHiddenRendererCount = 0;
            _focusHiddenPlate = false;
        }

        int before = _focusHiddenRenderers.Count;
        CanvasConversion.ApplyOwnerRenderHide(panel, _focusHiddenCanvases, _focusHiddenRenderers,
            out int canvases, out int renderers);
        _focusHiddenCanvasCount += canvases;
        _focusHiddenRendererCount += renderers;
        // Diagnostic only (see MrBacking.PlateObjectName): name the MR plate in the log if this
        // pass — or an earlier one for the same hide — actually switched it off. The hide itself
        // never looks at names.
        for (int i = before; i < _focusHiddenRenderers.Count && !_focusHiddenPlate; i++)
        {
            Renderer r = _focusHiddenRenderers[i];
            if (r != null && r.gameObject.name == MrBacking.PlateObjectName)
                _focusHiddenPlate = true;
        }
    }

    /// <summary>
    /// Undo <see cref="ApplyFocusHide"/>: re-enable every canvas AND every renderer WE disabled
    /// (exactly those — <see cref="CanvasConversion.LiftOwnerRenderHide"/> restores the recorded
    /// set and nothing else) and clear <c>ConvertedPanel.OwnerRenderHidden</c>. Idempotent, and safe
    /// after the conversion was already released — the components are held by reference and belong
    /// enabled wherever they now live (their 2D home restores them enabled too).
    /// </summary>
    private void RestoreFocusHide(string? reason)
    {
        if (_focusHiddenCanvases.Count == 0 && _focusHiddenRenderers.Count == 0 && !_rowHiddenForFocus)
            return;
        CanvasConversion.LiftOwnerRenderHide(Panel, _focusHiddenCanvases, _focusHiddenRenderers);
        _rowHiddenForFocus = false;
        int canvases = _focusHiddenCanvasCount;
        int renderers = _focusHiddenRendererCount;
        _focusHiddenCanvasCount = 0;
        _focusHiddenRendererCount = 0;
        _focusHiddenPlate = false;
        if (reason != null)
        {
            _loggedFocusVisibility = null;
            VRLog.Info("WorldUI", $"DECISION DOCK: focus hide lifted ({reason}) — all {canvases} " +
                                  $"canvas(es) and {renderers} renderer(s) the mod disabled are " +
                                  "enabled again (the MR backing plate among them); the prompt was " +
                                  "never touched.");
        }
    }

    // ---- window-remainder suppression (HandSuppression pattern; nothing destroyed) -----

    /// <summary>
    /// Cheap, idempotent alpha-0 of the window's root CanvasGroup (+ blocksRaycasts off),
    /// re-asserted every claimed tick. Applied the instant a decision prompt is claimed and
    /// open — even before its widget row is isolated/docked — so the raw flat window (the
    /// burn-confirm DialogPopup and its full card) never flashes in the HMD during the convert
    /// grace (2D-flash-on-burn fix). Records the window/group so <see cref="RestoreSuppression"/>
    /// hands them back. The nested-canvas (vignette/backdrop) suppression is applied separately
    /// in <see cref="ApplySuppression"/> once the row is docked and therefore outside the window.
    /// </summary>
    private void SuppressWindowGroup(UIWindow window)
    {
        _suppressedWindow = window;

        CanvasGroup? group = window.m_CanvasGroup != null
            ? window.m_CanvasGroup
            : window.GetComponent<CanvasGroup>();
        if (group != null)
        {
            _suppressedGroup = group;
            if (group.alpha != 0f)
                group.alpha = 0f;
            if (group.blocksRaycasts)
                group.blocksRaycasts = false;
        }
    }

    /// <summary>
    /// Full docked-tick suppression: the cheap CanvasGroup alpha-0 (via
    /// <see cref="SuppressWindowGroup"/>) PLUS disabling the window's nested vignette/backdrop
    /// canvases. Re-asserted every docked tick (the game rewrites the CanvasGroup alpha on
    /// Show/ToggleVisibility and could re-enable canvases live). The converted row is OUTSIDE the
    /// window subtree while docked, so neither touches it.
    /// </summary>
    private void ApplySuppression(UIWindow window)
    {
        SuppressWindowGroup(window);

        // Backdrops/vignettes are often NESTED CANVASES (adopted with
        // overrideSorting=true when the whole window floated) — a nested canvas
        // renders independently of ancestor CanvasGroup alpha, so it needs the
        // component disabled (stops its entire subtree rendering; the standard
        // hide-UI optimization, see CanvasConversion.AdoptNestedCanvases).
        CanvasScratch.Clear();
        window.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        for (int i = 0; i < CanvasScratch.Count; i++)
        {
            Canvas nested = CanvasScratch[i];
            if (nested == null || !nested.enabled)
                continue;
            nested.enabled = false;
            if (!_disabledCanvases.Contains(nested))
            {
                _disabledCanvases.Add(nested);
                VRLog.Info("WorldUI", $"DECISION DOCK: nested canvas '{nested.name}' disabled " +
                                      "(vignette/backdrop/card suppressed while the widget row is docked).");
            }
        }
        CanvasScratch.Clear();
    }

    /// <summary>
    /// USER RULING 2026-08-04 ("daher kann diese Option als Button komplett entfernt
    /// werden (und der verbleibende Button mittig angeordnet werden)"): while the
    /// docked prompt is the burn/lose PICK CONFIRM DialogPopup, its CANCEL option
    /// ("Waehle eine andere Karte" / GUI_CHOOSE_OTHER_CARD) is REMOVED from the docked
    /// row — physically grabbing a laid-down pick card drives that exact option through
    /// the game's own seam (CardsDriver.MaybeReopenPickSelection →
    /// CardsGameApi.CancelPickConfirmDialog → DialogPopup.Cancel → this very button's
    /// onClick), so the button and the grab were two affordances for one action, and the
    /// button's presence next to the commit invited the wrong reach.
    ///
    /// Identified STRUCTURALLY (never by screen text): <see
    /// cref="CardsGameApi.PickConfirmCancelButton"/> returns
    /// <c>optionButtons[cancelOption]</c> of the live <c>UIManager.dialogPopup</c> only
    /// while the current hand is in a modal pick mode — any other DialogPopup use
    /// (scenario choices etc.) returns null and keeps its full option row.
    ///
    /// CENTERING rides the existing machinery, so the user's tuned decision offsets/gap
    /// are untouched: the option holder's HorizontalLayoutGroup re-lays out the
    /// remaining button(s), the content fit (CanvasConversion.TickFit) re-measures the
    /// VISIBLE union and re-centers it in the host, and <see cref="Place"/> measures the
    /// widget block from ACTIVE widgets only (<see cref="ResolvePromptWidgets"/> skips
    /// inactive buttons) against the same DecisionGap placement anchor as before.
    ///
    /// THAT SENTENCE USED TO SAY "hiding the GameObject is safe: DialogPopup.Cancel invokes
    /// the onClick regardless of active state". IT IS FALSE, and it cost a build (ModBuild
    /// 248): <c>DialogPopup.Cancel()</c> gates on
    /// <c>optionButtons[cancelOption].ExtendedButton.gameObject.activeSelf</c>
    /// (GH.Runtime/DialogPopup.cs:389-403), so for as long as this suppression is applied the
    /// game's own cancel is a SILENT NO-OP — which is exactly what broke the forced-discard
    /// "choose another card" flow. The suppression stays (grabbing the laid-down card IS that
    /// action in VR); what changed is that the mod no longer relies on <c>Cancel()</c> to
    /// reach the button. See <c>Cards.CardsGameApi.CancelPickConfirmDialog</c>, which resolves
    /// the option itself and invokes the same onClick when it is inactive.
    /// HelperTools.NormalizePool re-activates pooled option buttons on every Show.
    /// Re-asserted every docked tick (level-triggered, the house pattern); restored on
    /// undock/prompt change/shutdown.
    ///
    /// <para>SINCE 2026-08-24 THIS ONLY RUNS IN THE FALLBACK CASE. When the board offers the pick
    /// confirm's COMMIT as a physical keycap — the ordinary case — the surface does not dock this
    /// prompt at all (<see cref="PickConfirmOnBoardKeycaps"/>), so no option is hidden and every
    /// seam takes the game's own untouched path. This method is reached only when the row docks
    /// anyway because that keycap is NOT offered, and there the 2026-08-04 ruling and the ModBuild
    /// 248 inactive-button workaround both still apply exactly as written above.</para>
    /// </summary>
    private void ApplyPickCancelSuppression()
    {
        if (_active == null || _active.Name != "DialogPopup")
        {
            RestorePickCancelSuppression();
            return;
        }
        InputButton? cancel = CardsGameApi.PickConfirmCancelButton();
        if (cancel == null)
        {
            RestorePickCancelSuppression(); // not the pick confirm — full option row stays
            return;
        }
        if (_hiddenCancelButton != null && !ReferenceEquals(cancel, _hiddenCancelButton))
            RestorePickCancelSuppression(); // pooled button identity moved — never strand a hidden one
        _hiddenCancelButton = cancel;
        if (cancel.gameObject.activeSelf)
        {
            cancel.gameObject.SetActive(false);
            if (!_cancelSuppressionLogged)
            {
                _cancelSuppressionLogged = true;
                VRLog.Info("WorldUI", "DECISION DOCK: pick confirm CANCEL option ('choose another card', " +
                                      "optionButtons[cancelOption]) hidden from the docked row — grabbing the " +
                                      "laid-down card IS that action (reopen seam); the remaining commit " +
                                      "option re-centers via the content fit, DecisionGap placement unchanged.");
            }
        }
    }

    /// <summary>Undo <see cref="ApplyPickCancelSuppression"/>: reactivate the hidden cancel
    /// option button (idempotent; the game's own button pool would also re-activate it on the
    /// next DialogPopup.Show). The one-shot log latch re-arms for the next pick confirm.</summary>
    private void RestorePickCancelSuppression()
    {
        InputButton? hidden = _hiddenCancelButton;
        _hiddenCancelButton = null;
        _cancelSuppressionLogged = false;
        if (hidden != null && !hidden.gameObject.activeSelf)
            hidden.gameObject.SetActive(true);
    }

    /// <summary>
    /// Undo (undock/shutdown): re-enable every canvas WE disabled; give the
    /// CanvasGroup back only while the window is still open (a closed window's
    /// group belongs to the game's own hide fade — the HandSuppression.Restore rule).
    /// </summary>
    private void RestoreSuppression()
    {
        for (int i = 0; i < _disabledCanvases.Count; i++)
        {
            if (_disabledCanvases[i] != null)
                _disabledCanvases[i].enabled = true;
        }
        _disabledCanvases.Clear();

        CanvasGroup? group = _suppressedGroup;
        UIWindow? window = _suppressedWindow;
        _suppressedGroup = null;
        _suppressedWindow = null;
        if (group != null && window != null && window.IsOpen)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
    }

    /// <summary>
    /// Re-place at the end of the frame so the docked prompt row is as rigid on the board as the
    /// card piles are — the row pose-follows <c>PlayTray.DecisionMount</c>, and an Update-phase copy
    /// of that mount can be a frame stale while the board is being carried (see
    /// <see cref="WorldSurface.LateTick"/> for the ordering root cause). <see cref="Place"/> is
    /// derived purely from the mount and the fitted rect, so re-running it is idempotent; the HMD
    /// fallback float stays place-once through its own <c>_hmdFloatPlaced</c> latch.
    /// </summary>
    public override void LateTick()
    {
        if (Panel != null)
            Place();
    }

    public override void Shutdown()
    {
        for (int b = 0; b < 3; b++)
            {
                ConfigEntry<float> gap = CardsConfig.DecisionGap((ControlBoard)b);
                if (!ReferenceEquals(gap, null))
                    gap.SettingChanged -= OnRowGapSettingChanged; // user #11b live-apply
            }
        bool hadPanel = Panel != null;
        UnregisterDeliberateCanvas();
        RestoreFocusHide("the surface is shutting down"); // BEFORE the release: never strand a disabled canvas
        base.Shutdown(); // releases the conversion → row back in its 2D home
        if (hadPanel)
        {
            RestoreRowAdjustments();
            RestorePickCancelSuppression();
            RestoreSuppression();
        }
        _loggedFocusVisibility = null;
        PromptFocus.Reset(); // the cross-surface roll-up must not survive a module re-init either
        _active = null;
        _activeWindow = null;
        _wantSince = 0f;
        _targetWarned = false;
        _hmdFloatPlaced = false;
        _textWaitSince = 0f;
        _textWaitWarned = false;
        _placementLogged = false;
        _lastLoggedGapPx = float.NaN;
        RowBottomUpMeters = null;
        RowTopUpMeters = null;
        WireButtonLines = null;    // records 12, 24 and 29 must not survive a module re-init
        WirePromptKind = NetProtocol.DecisionKindNone;
        WireWidgetFlags = 0;
        WireDamageAmount = 0;
        _wireOptionCount = 0;
        _nextWireLinesAt = 0f;
        _takeDamageDumped = false; // re-emit the one-time ground-truth dump after a module re-init
        DockingTakeDamage = false;
        ModalFallback.DecisionDock.Reset(); // any grace hand-off drops with us
        if (ReferenceEquals(Instance, this))
            Instance = null; // claim drops → generic fallback owns the windows again
    }
}
