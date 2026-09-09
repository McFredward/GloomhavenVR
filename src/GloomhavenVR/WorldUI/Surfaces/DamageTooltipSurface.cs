using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using NetProtocol = GloomhavenVR.Net.NetProtocol;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// DAMAGE TOOLTIP (test #23, item 5). While the take-damage widget row is docked on the
/// control board (<see cref="DecisionDockSurface"/>), <c>TakeDamagePanel.ShowDamageTooltip</c>
/// (TakeDamagePanel.cs:319) pushes the hover/mandatory-use hint into a <c>HelpBox</c>
/// window — usually <c>InitiativeTrack.Instance.helpBox</c> (the plain "deal damage" tip,
/// :354) or the global <c>Singleton&lt;HelpBox&gt;</c> (the lethal mandatory-use hint,
/// :335). Both are self-contained <c>[RequireComponent(typeof(UIWindow))]</c> windows with
/// their own <c>Canvas</c> (HelpBox.cs:7/49), rendered by the perspective UI camera. In VR
/// that reads two ways the user flagged:
///   - 3D-TILTED: the HelpBox content carries baked local rotations / z offsets, the same
///     styling that tilts the combat log — it needs the <see cref="Flatten2D"/> pass.
///   - TOO HIGH: <c>InitiativeTrack.helpBox</c> sits up by the initiative track, nowhere
///     near the docked buttons the player is reading.
/// This surface converts whichever HelpBox is currently showing the damage tip (flattened)
/// and parks it just ABOVE the docked widget row, facing the player — adjacent to the
/// buttons, not floating high. It is a strict descendant of the take-damage dock: it only
/// runs while <see cref="DecisionDockSurface.DockingTakeDamage"/> holds and the tip window
/// is open, and it restores the HelpBox to its exact 2D home (CanvasConversion restore
/// records) the moment either drops — nothing is destroyed.
///
/// NOT A HOVER TOOLTIP — NEVER SEAT THIS IN THE BOARD TOOLTIP AREA (user ruling 2026-08-04,
/// the ModBuild-46 regression): despite the game's "GUI_TOOLTIP_*" loc keys, this HelpBox is
/// the PERSISTENT instruction line of the take-damage decision flow — TakeDamagePanel shows
/// it when the prompt opens / a toggle clears (ShowDamageTooltip, TakeDamagePanel.cs:319,
/// called from ClearSelectedToggle:314 and ToggleVisibility:1068) and hides it only when the
/// prompt closes (ResetAndHide:1044). Nothing about it is pointer-driven. It IS "der Text der
/// Entscheidungsknoepfe": together with the row it forms the decision dock, and the user's
/// board-local <c>[Cards] DecisionGap_*</c> stepper tunes the distance between exactly this
/// text and the buttons — a distance that only means anything while the text sits over the
/// row. Since ModBuild 89 that is structural rather than hopeful: <see cref="Place"/> seats this
/// line FROM the row's own placed top edge (<see cref="DecisionDockSurface.RowTopUpMeters"/>) at
/// exactly that gap, so the text is a MEMBER of the decision area — no dial can move, scale or
/// re-anchor it apart from its buttons, because it no longer computes a seat of its own.
/// ModBuild 46 rerouted this surface through
/// <c>WorldTooltips.TryGetBoardAreaPose</c> ("every board-owned tooltip in one area"), which
/// tore the prompt text away from its buttons into the top-left tooltip corner and voided the
/// tuned gap — the user's rule is narrower: the tooltip area is ONLY for true MOUSEOVER
/// tooltips (things shown because the pointer hovers something, i.e. the shared
/// <c>UITooltip</c> canvas <c>WorldTooltips</c> presents). Persistent flow text stays with
/// the surface that owns it.
///
/// The global HelpBox doubles as the game's general hint strip; converting it here only
/// ever happens DURING the take-damage dock (where the strip is showing the damage tip),
/// and reverses on undock, so its normal use elsewhere is untouched.
///
/// ONE CHARACTER OWNS THIS TEXT, TOO (user, ModBuild 86 hardware test: "Der Text der
/// Entscheidung zB 'Schadensphase: Erleide entweder Schaden, verbrenne …' ist immer noch
/// sichtbar auch wenn man den Character wechselt — Buttons und Text also die GESAMTE
/// Entscheidungsanzeige soll pro Character angezeigt werden"). ModBuild 84/85 gave the
/// owner rule to the widget ROW (<see cref="DecisionDockSurface"/>) and to the use bars
/// (<see cref="UseBarsSurface"/>) — and it worked: the hardware log shows the row hiding "1
/// canvas and 1 renderer". But this surface, which draws the OTHER half of the same dock,
/// had no owner rule at all, so the sentence describing another character's decision stayed
/// on the board through every character switch. It hides now on the SAME verdict from the
/// SAME resolver (<see cref="DecisionDockSurface.PromptFocus.ShouldHide"/> →
/// <c>DecisionDockSurface.PromptOwner</c>) — there is no second owner switch anywhere — and
/// through the SAME shared, idempotent, exact-restore mechanism
/// (<see cref="CanvasConversion.ApplyOwnerRenderHide"/>), which also switches off the MR
/// backing plate (a MeshRenderer, invisible to a canvas-only hide — the ModBuild 84 "leerer
/// Hintergrund" lesson) and sets <c>ConvertedPanel.OwnerRenderHidden</c> so
/// <see cref="MrBacking"/> refuses to build one while hidden.
///
/// <para>It cannot disturb the prompt, for the same structural reason the row's hide cannot:
/// only <c>Canvas.enabled</c>/<c>Renderer.enabled</c> are written on the mod-owned converted
/// host subtree. Nothing is deactivated, so no game <c>OnDisable</c> runs — and the HelpBox
/// is not even interactive, it is pure text. <see cref="Place"/> keeps running while hidden,
/// so focusing the owner again reveals it at its final geometry, never mid-placement.</para>
/// </summary>
internal sealed class DamageTooltipSurface : WorldSurface
{
    /// <summary>Density-scale guards, mirroring <see cref="DecisionDockSurface"/>.</summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>Tooltip text must stay readable under pressure — same density as the docked row.</summary>
    private const float DensityScale = 0.8f;

    /// <summary>Change-dedup for the seat line (row edge / text seat / clearance), keyed on the
    /// rounded millimetres so a settled dock logs it once.</summary>
    private string? _loggedSeat;

    /// <summary>The HelpBox window currently converted (open + showing the damage tip).</summary>
    private HelpBox? _active;

    // ---- one character owns a decision, TEXT edition (user ruling ModBuild 86) --------------

    /// <summary>Canvases WE disabled to render-hide the prompt text while the player is looking at
    /// another character. Held by reference so the restore lands even if the conversion was released
    /// in between (the HelpBox is then back in its 2D home, where it belongs enabled).</summary>
    private readonly List<Canvas> _focusHiddenCanvases = new(4);

    /// <summary>Renderers WE disabled for the same hide — the MR backing plate lives here.</summary>
    private readonly List<Renderer> _focusHiddenRenderers = new(4);

    /// <summary>True while the prompt text is render-hidden for another character's focus.</summary>
    private bool _hiddenForFocus;

    /// <summary>Totals across the re-asserting ticks of the CURRENT hide, and whether the MR plate
    /// was among them — reported by the log so a hardware log proves the plate went with the text.</summary>
    private int _focusHiddenCanvasCount;
    private int _focusHiddenRendererCount;
    private bool _focusHiddenPlate;

    /// <summary>Change-dedup for the hide/show line.</summary>
    private string? _loggedFocusVisibility;

    /// <summary>
    /// MULTIPLAYER READ SEAM (wire record <c>NetProtocol.ExtIdDecisionState</c>, flags bits 3..5):
    /// WHICH of <c>ShowDamageTooltip</c>'s branches the owner's tip window is showing right now, as
    /// one of <c>NetProtocol.DecisionText*</c>. <c>DecisionTextNone</c> while no tip is on show.
    ///
    /// <para>A NUMBER, NEVER THE TEXT — and that is the whole design of the remote prompt line.
    /// The receiver composes the sentence from its OWN localization table, because every input of
    /// the game's branch selection except the branch itself is already replicated to it: in an
    /// online game the non-controlling clients get the same damage message and their
    /// <c>TakeDamagePanel.ShowOtherPlayer</c> stores the attacked actor, the damaging ability and
    /// the numbers before hiding the window. What they CANNOT know is which branch the owner's
    /// client took, because two of the four conditions are that client's own UI state (the
    /// <c>UIActiveBonusBar</c> selection, the currently-toggled burn option) — so exactly that
    /// travels, in three bits.</para>
    ///
    /// <para>WHY NOT SEND THE COMPOSED LINE, which is what records 7/9/12/13 do for their text: the
    /// MANDATORY-USE branch builds its sentence by prefixing the NAMES OF ACTIVE-BONUS CARDS
    /// (TakeDamagePanel.cs:325-333). The standing rule is absolute — no card identity on this wire,
    /// ever; reveals only through <c>Net.RevealGate</c> — so that string may not travel, and a
    /// record that carried the text "except in one branch" would be a rule with a hole in it. The
    /// mirrored line therefore renders the mandatory hint WITHOUT the card names: less information
    /// than the owner has, which is the designed failure direction.</para>
    /// </summary>
    internal static byte WireTextVariant { get; private set; }

    /// <summary>Change gate for the wire-variant log line (never per frame).</summary>
    private static byte _loggedWireVariant = 0xFF;

    /// <summary>
    /// THE SEAM THE WIDGET ROW NOW HANGS FROM (ModBuild 91). World-metre offset, along the decision
    /// mount's up axis and relative to the mount position, of this prompt line's placed BOTTOM edge —
    /// the same units <see cref="DecisionDockSurface.RowTopUpMeters"/> is published in. Null while no
    /// line is converted and placed.
    ///
    /// <para>THE DIRECTION OF THE COUPLING FLIPPED, THE COUPLING DID NOT. ModBuild 89/90 seated this
    /// line one <c>[Cards] DecisionGap</c> ABOVE the row's measured top, which made the two
    /// inseparable — but it also meant the area was solved bottom-up, so this line grew the display
    /// UPWARD and the same offset could not fit a prompt that has a line and one that does not (the
    /// user's initiative-boots vs damage-decision report). The line now takes the area's CEILING
    /// (<see cref="DecisionDockSurface.AreaCeilingUp"/>) — a height no prompt can change — and the row
    /// hangs one gap below THIS edge. Same single seat, same single gap, same "whatever moves one
    /// moves the other in the same frame"; only the anchor moved from the bottom of the area to its
    /// top.</para>
    ///
    /// <para>Published through the focus hide, exactly like the row's edges: geometry stays live while
    /// the display is render-hidden so that focusing the owner again reveals it already final.</para>
    /// </summary>
    internal static float? TextBottomUpMeters { get; private set; }

    /// <summary>
    /// Is a damage-tip HelpBox OPEN right now? Read by <see cref="DecisionDockSurface.Place"/> to
    /// know that the docked prompt is going to grow a text line and that it must therefore wait for
    /// <see cref="TextBottomUpMeters"/> before revealing its row — this surface can only convert one
    /// tick after that row docks (see <see cref="WantConverted"/>), so without the question the row
    /// would reveal at the ceiling and drop a frame later.
    /// </summary>
    internal static bool TipWindowOpen => OpenTip() != null;

    public override string Name => "DamageTooltip";
    protected override bool ConfigEnabled => WorldUIConfig.DecisionDock.Value;
    protected override bool Flatten2D => true;

    /// <summary>
    /// The HelpBox showing the damage tip right now: prefer the initiative-track box
    /// (the common "deal damage" tip that appears too high), fall back to the global
    /// box (the lethal mandatory-use hint). Null when neither is open.
    /// </summary>
    private static HelpBox? OpenTip()
    {
        InitiativeTrack? track = InitiativeTrack.Instance;
        HelpBox? trackBox = track != null ? track.helpBox : null;
        if (IsOpen(trackBox))
            return trackBox;
        HelpBox? global = Singleton<HelpBox>.IsInitialized ? Singleton<HelpBox>.Instance : null;
        if (IsOpen(global))
            return global;
        return null;
    }

    private static bool IsOpen(HelpBox? box) =>
        box != null && box.myWindow != null && box.myWindow.IsOpen;

    /// <summary>
    /// Converted while the take-damage row is docked AND the tip window is open — plus, since the
    /// 2026-08-08 ruling, only while that row is actually SHOWN. The tip is "der Text der
    /// Entscheidungsknoepfe" (see the class doc): leaving it up over an empty seat while the row is
    /// render-hidden for another character's focus would show half a decision that belongs to
    /// somebody else — and it is that same half a peer's mirrored board would then have to draw. So
    /// the text follows its buttons in both places; returning the focus re-converts it, at the same
    /// seat, because <see cref="Place"/> is derived purely from the mount.
    /// </summary>
    protected override bool WantConverted
    {
        get
        {
            if (!base.WantConverted || FlatScreen.ManualScreenActive
                || !DecisionDockSurface.DockingTakeDamage || DecisionDockSurface.RowFocusHidden)
                return false;
            return OpenTip() != null;
        }
    }

    protected override RectTransform? FindTarget()
    {
        _active = OpenTip();
        return _active != null ? _active.transform as RectTransform : null;
    }

    public override void Tick()
    {
        bool hadPanel = Panel != null;
        base.Tick(); // convert / release / Place (level-triggered on WantConverted)

        PublishWireVariant();

        if (Panel != null && !hadPanel)
            VRLog.Info("WorldUI", "DAMAGE TOOLTIP: HelpBox docked flat above the take-damage row " +
                                  "(flattened, brought down beside the buttons) — restored to 2D when it closes.");
        else if (Panel == null && hadPanel)
        {
            _active = null;
            _loggedFocusVisibility = null; // the next dock states its focus verdict afresh
            _loggedSeat = null;            // …and its seat afresh
            VRLog.Info("WorldUI", "DAMAGE TOOLTIP: HelpBox released — restored to its 2D home.");
        }

        // THE SEAM THE ROW SEATS FROM IS DROPPED ONLY WHEN THIS PROMPT GENUINELY HAS NO LINE ANY
        // MORE — never merely because the line is between conversions. The focus hide releases this
        // surface outright (WantConverted gates on RowFocusHidden) and a re-convert needs a frame or
        // two to fit, so nulling on release would send the widget row up to the area ceiling and back
        // down every time the player looks away from the owner and back: exactly the pop-in the
        // "never reveal before the final geometry" rule forbids. Latching it means the row is already
        // in its final place when the display returns.
        if (!TipWindowOpen || !DecisionDockSurface.DockingTakeDamage)
            TextBottomUpMeters = null;

        // ONE CHARACTER OWNS A DECISION — the prompt TEXT follows the widget row (see the class
        // doc). Level-triggered and re-asserted every converted tick, exactly like the row's hide.
        if (Panel != null)
            UpdateFocusVisibility();
        else
            RestoreFocusHide("the prompt text released");
    }

    /// <summary>
    /// Hide/show the converted prompt text against the SHARED focus verdict. The owner is resolved
    /// exactly once, by <see cref="DecisionDockSurface.PromptFocus.ShouldHide"/> — this surface
    /// deliberately owns no attribution logic of its own, so the text can never disagree with the
    /// buttons it belongs to (which is the ModBuild 86 bug, in one sentence).
    /// </summary>
    private void UpdateFocusVisibility()
    {
        bool hide = DecisionDockSurface.PromptFocus.ShouldHide(out CPlayerActor? owner,
                                                              out CPlayerActor? focused);
        if (hide)
            ApplyFocusHide();
        else
            RestoreFocusHide(null);

        DecisionDockSurface.PromptFocus.Report(Name, hide, _focusHiddenCanvasCount,
            _focusHiddenRendererCount, _focusHiddenPlate);

        // Same dedup shape as the row's: the counts are part of the key because the hide is
        // re-asserted every tick, so a plate the MR sweep builds a frame later genuinely changes
        // what is hidden. The sets are finite, so this can never become a per-frame log.
        string state = $"{(hide ? "hidden" : "shown")}|{Board.CharacterFocus.Describe(owner)}|" +
                       $"{Board.CharacterFocus.Describe(focused)}|" +
                       $"{_focusHiddenCanvasCount}|{_focusHiddenRendererCount}|{_focusHiddenPlate}";
        if (_loggedFocusVisibility == state)
            return;
        _loggedFocusVisibility = state;
        if (hide)
            VRLog.Info("WorldUI", "DECISION DOCK: the prompt TEXT (HelpBox, 'Schadensphase: …') belongs to " +
                                  $"'{Board.CharacterFocus.Describe(owner)}' and the player is looking at " +
                                  $"'{Board.CharacterFocus.Describe(focused)}' — RENDER-HIDDEN: " +
                                  $"{_focusHiddenCanvasCount} canvas(es) and {_focusHiddenRendererCount} " +
                                  "renderer(s) disabled on the mod-owned host subtree + extra render roots, " +
                                  $"MR backing plate {(_focusHiddenPlate ? "INCLUDED" : "not present (no plate on this panel yet)")}. " +
                                  "It hides WITH the widget row — the whole decision display belongs to one " +
                                  "character. The HelpBox itself is untouched: still open, still holding its " +
                                  "text, and it reappears unchanged the moment the owner is focused again.");
        else
            VRLog.Info("WorldUI", "DECISION DOCK: the prompt TEXT (HelpBox) is VISIBLE — " +
                                  (owner == null
                                      ? "the prompt is not attributable to a single character, so it shows to " +
                                        "whoever is looking (the safe direction, matching the widget row)."
                                      : $"owner '{Board.CharacterFocus.Describe(owner)}' is the character in " +
                                        "view" + (focused == null ? " (no focus override — following the game)." : ".")));
    }

    /// <summary>
    /// RENDER-HIDE the converted prompt text and NOTHING ELSE — the shared mechanism the docked row
    /// uses (<see cref="CanvasConversion.ApplyOwnerRenderHide"/>): <c>Canvas.enabled</c> and
    /// <c>Renderer.enabled</c> off across the mod-owned host subtree and every registered extra
    /// render root, each one recorded so the restore is exact. Idempotent and re-asserted every
    /// converted tick, so an MR plate built a frame later is caught next tick — and
    /// <c>ConvertedPanel.OwnerRenderHidden</c> (set inside the helper, before any component is
    /// touched) makes <see cref="MrBacking"/> refuse to build one at all, so there is no flash.
    /// </summary>
    private void ApplyFocusHide()
    {
        ConvertedPanel? panel = Panel;
        if (panel == null || panel.HostGo == null)
            return;
        bool first = !_hiddenForFocus;
        _hiddenForFocus = true;
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
        // Diagnostic only (the hide itself is name-blind): name the MR plate in the log if this pass
        // — or an earlier one for the same hide — actually switched it off.
        for (int i = before; i < _focusHiddenRenderers.Count && !_focusHiddenPlate; i++)
        {
            Renderer r = _focusHiddenRenderers[i];
            if (r != null && r.gameObject.name == MrBacking.PlateObjectName)
                _focusHiddenPlate = true;
        }
    }

    /// <summary>
    /// Undo <see cref="ApplyFocusHide"/>: re-enable exactly the canvases AND renderers WE disabled
    /// and clear <c>ConvertedPanel.OwnerRenderHidden</c>. Idempotent, and safe after the conversion
    /// was already released — the components are held by reference and belong enabled wherever they
    /// now live (the HelpBox's 2D home restores them enabled too).
    /// </summary>
    private void RestoreFocusHide(string? reason)
    {
        if (_focusHiddenCanvases.Count == 0 && _focusHiddenRenderers.Count == 0 && !_hiddenForFocus)
            return;
        CanvasConversion.LiftOwnerRenderHide(Panel, _focusHiddenCanvases, _focusHiddenRenderers);
        _hiddenForFocus = false;
        int canvases = _focusHiddenCanvasCount;
        int renderers = _focusHiddenRendererCount;
        _focusHiddenCanvasCount = 0;
        _focusHiddenRendererCount = 0;
        _focusHiddenPlate = false;
        if (reason != null)
        {
            _loggedFocusVisibility = null;
            VRLog.Info("WorldUI", $"DECISION DOCK: prompt-text focus hide lifted ({reason}) — all {canvases} " +
                                  $"canvas(es) and {renderers} renderer(s) the mod disabled are enabled " +
                                  "again (the MR backing plate among them); the HelpBox was never touched.");
        }
    }

    /// <summary>
    /// Re-evaluate <see cref="WireTextVariant"/> and log it once per change. Published only while
    /// this surface is really showing a tip on a VISIBLE take-damage row — the same gate
    /// <c>DecisionDockSurface</c> publishes its labels on, so the peer can never draw a prompt line
    /// over plates it does not have (or the reverse).
    /// </summary>
    private void PublishWireVariant()
    {
        byte variant = Panel != null || DecisionDockSurface.DockingTakeDamage
            ? ClassifyTip() : NetProtocol.DecisionTextNone;
        // The names ride ONLY with the variant that prints them, so every other prompt keeps the
        // packet it had. Resolved here rather than inside ClassifyTip because that method is a pure
        // classification and must stay one.
        string? names = null;
        if (variant == NetProtocol.DecisionTextMandatoryUse
            && Singleton<TakeDamagePanel>.IsInitialized)
        {
            TakeDamagePanel? panel = Singleton<TakeDamagePanel>.Instance;
            if (panel != null)
                names = SampleMandatoryNames(panel);
        }
        WireMandatoryNames = names;
        WireTextVariant = variant;
        if (_loggedWireVariant == variant)
            return;
        _loggedWireVariant = variant;
        VRLog.Info("WorldUI", variant == NetProtocol.DecisionTextNone
            ? "DAMAGE TOOLTIP: wire text variant cleared — peers draw no prompt line under their " +
              "mirrored decision plates."
            : $"DAMAGE TOOLTIP: wire text variant {variant} published (record 23 flags bits 3..5) — " +
              "peers compose the SAME instruction line from their own localization; the composed " +
              "text never rides the wire (the mandatory-use branch embeds active-bonus card names).");
    }

    /// <summary>
    /// WHICH branch of the game's own <c>TakeDamagePanel.ShowDamageTooltip</c> (TakeDamagePanel.cs:319)
    /// is on screen, re-evaluated here in the SAME order the game evaluates it — mandatory-use hint,
    /// wound wording, companion/plain summon, plain deal-damage. Reading the panel's own fields
    /// rather than the rendered string on purpose: the string is localized and rich-text-formatted,
    /// so classifying it back would be a parser with a language dependency, while the fields are
    /// the very inputs the game branched on.
    ///
    /// <para>Any failure — no panel, a half-torn model, a game-side API that moved — degrades to
    /// <c>DecisionTextNone</c> (no line on the peer), never to a wrong line.</para>
    /// </summary>
    private static byte ClassifyTip()
    {
        try
        {
            TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                ? Singleton<TakeDamagePanel>.Instance
                : null;
            if (p == null || !p.IsOpen)
                return NetProtocol.DecisionTextNone;

            // 1. MANDATORY USE — the game's own expression, verbatim (the lethal variant drops the
            //    "prevent only if lethal" exemption). Only shown while no burn option is toggled.
            if (p.currentlyToggled == null && MandatoryActiveBonusPending(p))
                return NetProtocol.DecisionTextMandatoryUse;

            // 2. WOUND: the damaging ability's type, or — when the panel holds no ability — the
            //    current damage data's source type, exactly as the game reads it.
            CAbility? ability = p.damageAbility;
            if (ability != null)
            {
                if (ability.AbilityType == CAbility.EAbilityType.Wound)
                    return NetProtocol.DecisionTextWounded;
            }
            else if (GameState.CurrentDamageData != null
                     && GameState.CurrentDamageData.DamageSourceAbilityType == CAbility.EAbilityType.Wound)
            {
                return NetProtocol.DecisionTextWounded;
            }

            // 3. SUMMON / COMPANION.
            if (p.actorBeingAttacked is CHeroSummonActor summon)
                return summon.IsCompanionSummon
                    ? NetProtocol.DecisionTextCompanion
                    : NetProtocol.DecisionTextSummon;

            // 4. The plain instruction ("Schadensphase: Erleide entweder Schaden, verbrenne …").
            return NetProtocol.DecisionTextDealDamage;
        }
        catch (System.Exception)
        {
            return NetProtocol.DecisionTextNone;
        }
    }

    /// <summary>
    /// MULTIPLAYER READ SEAM (wire record <c>NetProtocol.ExtIdDecisionNames</c>): the '\n'-joined
    /// LOCALIZATION KEYS of the cards the owner's mandatory-use hint is prefixed with. Null
    /// whenever nothing may or need be said.
    /// </summary>
    internal static string? WireMandatoryNames { get; private set; }

    /// <summary>
    /// Collect the card-name KEYS the game itself prefixes onto the mandatory-use line
    /// (TakeDamagePanel.cs:325-333) — the same bar, the same filter, in the same order.
    ///
    /// <para>KEYS AND NOT WORDS. The game renders each through
    /// <c>LocalizationNameConverter.MultiLookupLocalization(bonus.BaseCard.Name)</c>, so
    /// <c>BaseCard.Name</c> is the key; sending keys lets every receiver read the names in ITS own
    /// language instead of the sender's. It also keeps this seam free of the red-and-font markup the
    /// game wraps them in — the receiver applies its own, which is what the mirrored line has always
    /// done with the hint itself.</para>
    ///
    /// <para>THE GATE IS NOT OPTIONAL AND IT IS NOT DECORATIVE. A card name is card identity, and
    /// the standing rule is that identity reveals only through <see cref="Net.RevealGate"/>. This
    /// used to ask <c>PeersSeeOurCardFronts</c> alone — the very predicate the board tooltip's text
    /// already rides — and return null when it was closed, which renders as the bare hint, i.e.
    /// exactly what every build before ModBuild 307 drew.</para>
    ///
    /// <para>IT HAD NO CARD TERM, AND EVERY CARD ON THIS BAR WOULD HAVE PASSED ONE (2026-09-07).
    /// <c>Net.RevealGate.PeersMayNameOurCard</c> is the phase term this asked PLUS the burn/active
    /// exception for the particular card, "which can only widen" — and the cards this bar names are
    /// ACTIVE BONUSES, i.e. in <c>CCharacterClass.ActivatedCards</c> by construction, which is the
    /// first list <c>RevealGate.IsPubliclyRevealedCard</c> walks. So inside the game's secret
    /// selection window a watcher read the peer's active card face-up on their matrix and got a
    /// nameless hint beside it, which is the same drift the board tooltip's gate carried.</para>
    ///
    /// <para>THE WIDENING IS PER CARD AND FAILS CLOSED. A bonus whose card cannot be NAMED — no
    /// <c>CAbilityCard</c> behind it (an item bonus), no <c>CPlayerActor</c> to ask about — keeps
    /// the phase answer, so a shut phase still publishes nothing for it. And because the bar is
    /// wrapped whole, one such card withholds the ENTIRE bar rather than half of it.</para>
    ///
    /// <para>Wrapped whole: a half-torn bar must publish NO names, never a wrong one.</para>
    /// </summary>
    private static string? SampleMandatoryNames(TakeDamagePanel p)
    {
        try
        {
            // THE PHASE HALF, READ ONCE. It is no longer a bail: a card the phase covers may still
            // be public in its own right, and that is asked per card below.
            bool phaseOpen = Net.RevealGate.PeersSeeOurCardFronts;
            if (!Singleton<UIActiveBonusBar>.IsInitialized)
                return null;
            UIActiveBonusBar bar = Singleton<UIActiveBonusBar>.Instance;
            System.Collections.Generic.List<CActiveBonus>? pending =
                bar != null ? bar.GetNonSelectedActiveBonus() : null;
            if (pending == null || pending.Count == 0)
                return null;
            bool lethal = p.CalculateCurrentDamage() > 0 && p.IsLethalDamage;
            var sb = new System.Text.StringBuilder(48);
            for (int i = 0; i < pending.Count; i++)
            {
                CActiveBonus bonus = pending[i];
                // The game's own two filters, verbatim — the lethal variant drops the
                // "prevent only if lethal" exemption (TakeDamagePanel.cs:321).
                if (bonus?.Ability?.ActiveBonusData == null
                    || bonus.Ability.ActiveBonusData.ToggleIsOptional)
                    continue;
                if (!lethal && bonus is CPreventDamageActiveBonus prevent
                    && prevent.PreventOnlyIfLethal)
                    continue;
                // …AND THE CARD HALF. Asked of Net.RevealGate, never re-derived here, so this
                // seam and the face a peer is looking at cannot answer differently.
                if (!phaseOpen && !CardIsPublic(bonus))
                    return null;   // wrapped whole — one covered card withholds the bar
                string? key = bonus.BaseCard != null ? bonus.BaseCard.Name : null;
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (sb.Length > 0)
                    sb.Append('\n');
                // A key with a newline in it would split into two names on the peer; the game has
                // none, and flattening costs nothing next to trusting that.
                sb.Append(key!.Replace('\n', ' ').Replace('\r', ' ').Trim());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception)
        {
            return null; // no names is the safe answer; the hint stands alone, as it always did
        }
    }

    /// <summary>
    /// Is <paramref name="bonus"/>'s source card ALREADY public to peers in its own right — the
    /// exception that outranks the phase (<c>Net.RevealGate.IsPubliclyRevealedCard</c>, reached
    /// through <c>PeersMayNameOurCard</c> so this file states no rule of its own)?
    ///
    /// <para>Two things have to be named before the question can be asked at all: the card
    /// (<c>CBaseCard</c> carries no instance id — only <c>CAbilityCard</c> does) and the
    /// <c>CPlayerActor</c> whose <c>CharacterClass</c> holds it. The bonus's own Actor is tried
    /// first and its Caster second, because an active bonus placed on an ally still lives in the
    /// CASTER's <c>ActivatedCards</c>. Anything unnamed answers FALSE, which withholds — this
    /// file's standing direction of failure.</para>
    /// </summary>
    private static bool CardIsPublic(CActiveBonus bonus)
    {
        if (bonus.BaseCard is not CAbilityCard card || card == null)
            return false;
        int id = card.CardInstanceID;
        if (bonus.Actor is CPlayerActor owner && owner != null
            && Net.RevealGate.PeersMayNameOurCard(owner, id))
            return true;
        return bonus.Caster is CPlayerActor caster && caster != null
               && Net.RevealGate.PeersMayNameOurCard(caster, id);
    }

    /// <summary>The game's own mandatory-active-bonus test (TakeDamagePanel.cs:321), re-evaluated
    /// against the live bar. It is the one input of the tip's branch selection that is pure LOCAL UI
    /// state — which is precisely why the resulting variant has to ride the wire.</summary>
    private static bool MandatoryActiveBonusPending(TakeDamagePanel p)
    {
        if (!Singleton<UIActiveBonusBar>.IsInitialized)
            return false;
        UIActiveBonusBar bar = Singleton<UIActiveBonusBar>.Instance;
        if (bar == null)
            return false;
        System.Collections.Generic.List<CActiveBonus> pending = bar.GetNonSelectedActiveBonus();
        if (pending == null)
            return false;
        bool lethal = p.CalculateCurrentDamage() > 0 && p.IsLethalDamage;
        return lethal
            ? pending.Count(it => !it.Ability.ActiveBonusData.ToggleIsOptional) > 0
            : pending.Count(it => !it.Ability.ActiveBonusData.ToggleIsOptional
                                  && (!(it is CPreventDamageActiveBonus prevent)
                                      || !prevent.PreventOnlyIfLethal)) > 0;
    }

    /// <summary>
    /// Park just above the docked widget ROW — measured, not guessed — content-fitted into the
    /// same width budget and facing the player. While no mount exists the surface simply holds
    /// off (the dock itself has already floated to the HMD in that case; a mispositioned tip is
    /// never a lock).
    ///
    /// THE TEXT IS A MEMBER OF THE DECISION AREA, NOT A NEIGHBOUR OF IT (user, ModBuild 89
    /// hardware test: "Wenn ich den ganzen Entscheidungsbereich nach unten verschiebe mit dem
    /// Offset, dann soll der Text (der über den Buttons ist) auch entsprechend mit nach unten
    /// verschoben werden — aktuell reagiert er wie ein Element das nicht zu dem Bereich dazugehört
    /// bzw. entkoppelt ist"). WHY THE TWO DRIFTED APART: this method used to park the tip a fixed
    /// <c>0.11 × trayScale</c> above the decision MOUNT, while
    /// <see cref="DecisionDockSurface.Place"/> anchors the widget block to the PROMPT REFERENCE
    /// (the grab-bar bottom) in a solve the mount's own position cancels out of
    /// (DecisionDockSurface.Place, the "TEXT→BUTTON DISTANCE" block). Two seats, two anchors, one
    /// dial moving only one of them: <c>[Cards] DecisionOffset_&lt;board&gt;.y</c> slid the TEXT down
    /// the board and left the buttons pinned under the bar — and it shipped at −0.157 on every
    /// board, i.e. the drift was in every install.
    ///
    /// NOW: the seat comes FROM the row's placed geometry —
    /// <see cref="DecisionDockSurface.RowTopUpMeters"/>, the row's own measured top edge, the same
    /// walk that publishes its bottom edge for the use bars — and the line's BOTTOM edge hangs one
    /// <c>[Cards] DecisionGap</c> above it. That is the stepper's literal documented meaning
    /// ("distance between the decision PROMPT TEXT and the TOP of the decision buttons"), so the
    /// player's tuned value keeps its exact effect, and it puts the line back on the prompt
    /// reference — the board's lower edge. Nothing is recomputed here that the row already
    /// computed: whatever moves the row (its offset, DecisionScale, the gap, the board) moves this
    /// text by the same amount in the same frame, because it IS the row's number.
    ///
    /// <para>SINCE ModBuild 90 that includes the offset's Y (user: "Wenn ich den ganzen
    /// Entscheidungsbereich nach unten verschiebe mit dem Offset, dann soll der Text … auch
    /// entsprechend mit nach unten verschoben werden"). The Y component used to cancel out of the
    /// row's own solve, so coupling this text to the row made it a no-op for BOTH halves;
    /// <see cref="DecisionDockSurface.MountOffsetUp"/> now feeds it back into that solve and the
    /// whole area — mount, row, this line, the use bars — travels together. Nothing changes here:
    /// this seat is still one gap above the row's top edge, which is the point.</para>
    ///
    /// ROOT CAUSE of the ModBuild-46 regression this reverts ("Der Text der
    /// Entscheidungsknoepfe ... ist ploetzlich Teil der Tooltip-Section"): commit 6cfab0f
    /// seated this pose through <c>WorldTooltips.TryGetBoardAreaPose</c> on the reading
    /// "every board-owned tooltip belongs in the unified area". Wrong classification — see
    /// the class doc: this HelpBox is the decision dock's persistent PROMPT TEXT, not a
    /// mouseover tooltip, and moving it to the board's top-left corner (a) split the dock's
    /// text from its buttons and (b) made the user-tuned <c>[Cards] DecisionGap_*</c>
    /// text-to-buttons distance meaningless (DecisionDockSurface.Place anchors the widget
    /// block a gap below the prompt reference at the board's lower edge — the spot this text
    /// occupies). Seated back over its own buttons, at the mount's rotation and tray scale. The
    /// tooltip area stays reserved for the genuine hover tooltips WorldTooltips presents.
    /// </summary>
    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            Panel.OrderCluster = null; // no board — not part of any draw cluster
            TextBottomUpMeters = null; // no mount to seat on ⇒ the row must not hang off a phantom edge
            return;
        }
        // Board-owned tip parked on the board plane: join the board's draw-order cluster so the
        // board's transparent furniture stays structurally below it — see
        // ConvertedPanel.OrderCluster.
        Panel.OrderCluster = PlayTray.Current;

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return; // not measured yet — TextBottomUpMeters keeps its last value (see Tick)

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            PlayTray.DecisionMountWidth * density / rect.width,
            PlayTray.DecisionMountMaxHeight * density / rect.height);
        float metersPerPx = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density;
        float worldPerPx = metersPerPx * trayScale;

        // THE SEAT: THE AREA'S CEILING (see the method doc). This line is the topmost element of a
        // prompt that has one, so its TOP edge takes the ceiling — a height that is the same whatever
        // prompt is open — and the widget row hangs one [Cards] DecisionGap below the BOTTOM edge
        // this produces. rect.yMax is pivot-relative, so this solves the pivot from the top edge; a
        // two-line prompt therefore grows DOWNWARD (pushing its own buttons and the use bars down)
        // instead of upward past the ceiling, which is the whole ModBuild 90 report.
        //
        // AND IT IS THE LINE'S OWN TOP EDGE, NOT ITS HOST'S (user, ModBuild 102: "Weiterhin
        // rutschen die Elemente immer direkt so beginn tiefer als es sein müsste … sie sollten
        // sich immer am oberen Rand orientieren"). The content fit leaves slack around the
        // measured glyph union and CENTERS the union in the host it produces
        // (ConvertedPanel.FitContentPadding), so seating rect.yMax at the ceiling put the visible
        // TEXT that slack below it — and then handed the same error down twice, because the row
        // hangs one gap under the bottom edge published here and the use bars hang under the row.
        // Subtracting the published slack is what makes "the top edge is the offset" exact.
        Vector3 up = mount.up;
        float ceilingUp = DecisionDockSurface.AreaCeilingUp(mount, up, trayScale, out string ceilNote);
        float padUp = Panel.FitContentPadding.y * worldPerPx;
        float lineHeight = Mathf.Max(0f, rect.height * worldPerPx - 2f * padUp);
        float seatUp = ceilingUp - (rect.yMax * worldPerPx - padUp);

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(mount.position + up * seatUp, mount.rotation);
        host.localScale = Vector3.one * worldPerPx;

        // Fitted and placed, and only NOW visible — the same rule the widget row follows (user ruling
        // 2026-08-03: "es soll DIREKT richtig angezeigt werden"). The activation used to happen above
        // the rect check, so an unfitted line was revealed at its pre-fit size and pre-place pose for
        // the settle frames.
        if (!Panel.HostGo.activeSelf)
            Panel.HostGo.SetActive(true);

        // Publish the MEASURED bottom edge — the seam the widget row seats from. Kept live through
        // the focus hide on purpose (see the property doc): the row must return at final geometry.
        float textBottomUp = ceilingUp - lineHeight;
        TextBottomUpMeters = textBottomUp;

        LogSeat(ceilingUp, ceilNote, textBottomUp, lineHeight,
                DecisionDockSurface.GapBoardMeters * trayScale,
                DecisionDockSurface.RowTopUpMeters,
                DecisionDockSurface.MountOffsetUp(mount, up));
    }

    /// <summary>
    /// One line, change-gated on the rounded millimetres, stating the ROW's placed top edge, the
    /// text's own bottom edge and the clearance between them — all three mount-relative along the
    /// same up axis, so a hardware log shows the two halves of the decision area agreeing (or, if
    /// this ever regresses, disagreeing, in one grep). Never per frame: the values are static while
    /// a dock is settled, and the surface only lives while a prompt is open.
    ///
    /// <para>It also states the OFFSET actually applied and the seat it produced (ModBuild 90).
    /// <c>[Cards] DecisionOffset_&lt;board&gt;.y</c> displaces the mount AND the block together, so
    /// the mount-relative numbers above are deliberately blind to it — the proof that the dial is
    /// live has to be read against something the offset does NOT move, and that is the prompt
    /// reference (the grab-bar bottom, i.e. the board's lower edge): the block top sits the gap
    /// MINUS the applied offset below it. Zero offset ⇒ exactly the gap ⇒ the shipped picture.</para>
    /// </summary>
    private void LogSeat(float ceilingUp, string ceilNote, float textBottomUp, float lineHeight,
                         float clearance, float? rowTopUp, float offsetUp)
    {
        string key = $"{ceilingUp * 1000f:F0}|{textBottomUp * 1000f:F0}|{clearance * 1000f:F0}" +
                     $"|{offsetUp * 1000f:F0}|{(rowTopUp.HasValue ? (rowTopUp.Value * 1000f).ToString("F0") : "-")}";
        if (_loggedSeat == key)
            return;
        _loggedSeat = key;
        VRLog.Info("WorldUI", "DECISION DOCK SEAT: the decision AREA's CEILING is " +
                              $"{ceilingUp * 1000f:F0} mm above the decision mount ({ceilNote}), and the " +
                              "TOPMOST element of this prompt is the prompt TEXT (HelpBox, " +
                              $"'Schadensphase: …'), whose top edge sits AT that ceiling. It is " +
                              $"{lineHeight * 1000f:F0} mm tall, so its bottom edge is at " +
                              $"{textBottomUp * 1000f:F0} mm and the widget row's top hangs one clearance " +
                              $"of {clearance * 1000f:F0} mm = [Cards] " +
                              $"DecisionGap_{Cards.CardsConfig.CurrentBoard} × the dock scale below it, at " +
                              (rowTopUp.HasValue ? $"{rowTopUp.Value * 1000f:F0} mm" : "(row not measured yet)") +
                              ". The area is laid out DOWNWARD from the ceiling — text, then the gap, then " +
                              "the buttons, then the use-bar drawer — so a taller prompt extends DOWN and " +
                              "the topmost pixel is at the same height for EVERY prompt (compare this " +
                              "number with the DECISION DOCK SEAT line the initiative-boots bar logs: they " +
                              "must be equal). [Cards] " +
                              $"DecisionOffset_{Cards.CardsConfig.CurrentBoard} has displaced the mount — " +
                              $"and with it this ceiling and everything under it — {offsetUp * 1000f:F1} mm " +
                              "along that up axis; the gap is the distance WITHIN the area and no dial but " +
                              "DecisionGap moves it.");
    }

    /// <summary>
    /// Re-place at the end of the frame: this tip pose-follows a control-board mount, so like every
    /// other board-docked element it must be written AFTER the board's own Update-phase carry writer
    /// or it renders one frame behind a moving board — see <see cref="WorldSurface.LateTick"/>.
    /// </summary>
    public override void LateTick()
    {
        if (Panel != null)
            Place();
    }

    public override void Shutdown()
    {
        // BEFORE the release: never strand a disabled canvas/renderer on a HelpBox that is about
        // to be handed back to its 2D home (the row's Shutdown ordering, same reason).
        RestoreFocusHide("the prompt-text surface is shutting down");
        base.Shutdown(); // releases the conversion → HelpBox back in its 2D home
        _active = null;
        _loggedFocusVisibility = null;
        _loggedSeat = null;
        TextBottomUpMeters = null;                      // …nor may the seam the row seats from
        WireTextVariant = NetProtocol.DecisionTextNone; // must not survive a module re-init
        WireMandatoryNames = null;                      // …nor may the card names
        _loggedWireVariant = 0xFF;
    }
}
