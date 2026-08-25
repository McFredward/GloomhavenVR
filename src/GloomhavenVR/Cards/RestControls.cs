using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Short-rest / long-rest controls on the play tray — two comfortable tray-styled
/// <see cref="PlayTray.BoardButton"/>s (test #24 item 3) sitting together in the rest
/// zone, replacing the old small round disc tokens.
///
/// Short rest: press → <c>ShortRest.MouseClick()</c> (via CardsGameApi.ToggleShortRest),
/// i.e. exactly the 2D widget path — the game's own yes/no confirmation dialog (now
/// docked on the board, item 5), then <c>CardsHandUI.PerformShortRest</c> with its
/// burn/redraw dialogs. The mod short button is the SOLE short-rest control: the native
/// "Kurze Rast" widget never docks any more (<see cref="Surfaces.TrayControlDockSurface"/>
/// hardcodes <c>ShortRestDocked =&gt; false</c> and its docked-control list is empty), and
/// <see cref="TickStatus"/> never consults it — visibility is
/// <c>RestUiOffered &amp;&amp; (canShort || shortSelected)</c>, the outer term being the game's own
/// offer predicate (see <see cref="RestUiOffered"/>).
/// This doc used to describe a hide/reappear handshake with the native widget; there is none.
/// Long rest: press → toggles the long-rest pseudo-card (CardID −1) through the game's
/// own fan selection (CardsGameApi.ToggleLongRest) — there is NO discrete native
/// long-rest button, so the long-rest control STAYS mod-drawn; the burn-a-discarded-
/// card step arrives later as <c>CardHandMode.LoseCard</c> served by the fan.
/// Buttons dim when the action is unavailable and accent when selected (same gating
/// the 2D widgets use — see CardsGameApi.CanShortRest/CanLongRest).
/// </summary>
internal sealed class RestControls
{
    private PlayTray.BoardButton? _shortButton;
    private PlayTray.BoardButton? _longButton;
    private bool _locHooked;

    // Issue 3 (rest-button relevance visibility): last-logged visibility of each keycap, so the
    // hide/appear flips are logged edge-triggered (once per change) rather than per frame. Seeded
    // true — the keycaps are built visible, so the first "not relevant" tick logs the hide.
    private bool _lastShortVisible = true;
    private bool _lastLongVisible = true;

    // REST GATE (ModBuild 134/135 report, see TickStatus): edge state for the ONE log line that
    // proves the offer gate — not the availability signal — is what removed a keycap. False until
    // a stale …Selected flag is actually being suppressed.
    private bool _lastGateSuppressing;

    /// <summary>Raised on press; CardsDriver queues the actual game call.</summary>
    internal System.Action? ShortRestRequested;
    internal System.Action? LongRestRequested;

    // ---- multiplayer board-UI read seam (Net.NetAvatarDriver.TickExtrasSend) -----------------
    // The RestControls instance is a private of CardsDriver, so the LIVE keycap visibility is
    // published as statics the same way PileBrowser.Current / ItemsPile.Current publish theirs.
    // Written by TickStatus (per frame while the tray shows), cleared on Destroy so a torn-down
    // board never advertises stale rest discs.

    /// <summary>True while the SHORT-rest keycap is visible on the local board.</summary>
    internal static bool ShortRestShown { get; private set; }

    /// <summary>True while the LONG-rest keycap is visible on the local board.</summary>
    internal static bool LongRestShown { get; private set; }

    // ---- multiplayer cap-STATE read seam (board-UI record byte 2) -----------------------------
    // "Buttons dim when the action is unavailable and accent when selected" (the class doc's own
    // sentence) was true LOCALLY and invisible to everyone else: a peer's mirrored discs were drawn
    // at their authored ACCENT colour permanently, so a dead disc and a selected one looked the
    // same as an available one. Both halves of the pair are published here, off the very values
    // TickStatus hands SetState, so the wire state IS the rendered state.

    /// <summary>True while the SHORT-rest keycap is ENABLED (a short rest is available). A visible
    /// but DISABLED disc is what the owner sees after selecting a rest that is no longer offered —
    /// dark wood, not the accent.</summary>
    internal static bool ShortRestEnabled { get; private set; }

    /// <summary>True while the SHORT-rest keycap is ACCENTED (that rest is selected).</summary>
    internal static bool ShortRestAccent { get; private set; }

    /// <summary>True while the LONG-rest keycap is ENABLED — see <see cref="ShortRestEnabled"/>.</summary>
    internal static bool LongRestEnabled { get; private set; }

    /// <summary>True while the LONG-rest keycap is ACCENTED (selected).</summary>
    internal static bool LongRestAccent { get; private set; }

    internal void EnsureBuilt(PlayTray tray)
    {
        // Feature 6a: the rest controls seat in the board's two rest-notches. PER-BOARD: the
        // diameter/side, the full X/Y/Z offset AND the cap SHAPE (Round disc / Square keycap)
        // come from the active board's config (the debug menu tunes them live) — this REPLACES
        // the old unreliable raycast auto-seating, so the buttons seat at a PREDICTABLE anchor +
        // per-board offset depth. Round-2: an extra per-board SPACING spreads the two apart.
        ControlBoard active = CardsConfig.CurrentBoard;
        float diameter = CardsConfig.RestButtonDiameter.Value;
        bool round = CardsConfig.RestButtonShape(active).Value == ButtonShape.Round;

        // Category split ([RestButtons], user: "every value applies ONLY to its own category" —
        // same pattern PlayTray.BuildButtons uses for [BoardButtons]): the rest keycaps read the
        // [RestButtons] set EXCLUSIVELY — SQUARE caps take its Width/Height (rectangular caps),
        // and BOTH shapes take its Depth/Travel. ROUND discs keep the per-board authored diameter
        // (the square W/H do not apply, exactly like the Confirm/Undo round caps). The numeric
        // defaults reproduce today's look bit-identically (0.105 × 0.105 × 0.012 / 4 mm travel =
        // the authored RestButtonDiameter / RoundButtonThickness / BoardButton.CapTravel).
        WorldUI.ButtonTuning.Bind();
        float thickness = WorldUI.ButtonTuning.RestCapDepth;
        float travel = WorldUI.ButtonTuning.RestCapTravel;
        // ---- FIT THE DISC TO THIS BOARD'S REST PAD, exactly as PlayTray.BuildButtons fits the
        // keycaps to their recess, and for the same reason: the tuned per-board diameters (91 / 71 /
        // 71 mm) overhang two of the three AUTHORED pads (floors measured at 81.6 / 81.7 / 68.1 mm).
        // Only ever shrinks — [Cards] RestButtonDiameter and [RestButtons] W/H stay the ceiling — and
        // does nothing at all on a board that carries no measurement, which is every bundle shipped so
        // far. The margin is this family's OWN [RestButtons] Travel, the same rule the board caps use
        // with theirs.
        _padMinHalf = tray.RestMinHalf;
        float restMargin = Mathf.Clamp(travel, 0.001f, 0.008f);
        var size = round
            ? new Vector2(diameter, diameter)                                                  // round: per-board diameter
            : new Vector2(WorldUI.ButtonTuning.RestCapWidth, WorldUI.ButtonTuning.RestCapHeight); // square: [RestButtons] W/H
        size = BoardAnchors.FitCapSize(size, _padMinHalf, restMargin);
        // A ROUND disc has to fit the pad in BOTH axes, so its diameter is the smaller fitted side.
        if (round)
        {
            diameter = Mathf.Min(size.x, size.y);
            size = new Vector2(diameter, diameter);
        }
        _capSize = size;
        // Per-board disc offset: X/Y in the board plane, Z = proud depth toward the player
        // (NEGATIVE = prouder). Replaces the old localPosition(insetX,0,0) + raycast reseat.
        Vector3 offset = CardsConfig.RestButtonOffset.Value;
        float spacing = CardsConfig.RestStackSpacing.Value;
        // THIS BOARD'S OWN REST-PAD PITCH, read off the two pad anchors the board itself exports —
        // the per-board term that used to be three hand-dialled RestButtonSpacing_{board} constants
        // (114.8 mm Oak, 120.2 Steel, 105.0 Bronze). The dial on top of it is a dimensionless
        // multiplier, so one shared entry is right on every board. See BoardAnchors.StackPitch.
        _padPitch = BoardAnchors.StackPitch(tray.ShortRestAnchor, tray.LongRestAnchor)
                    ?? Defaults.StackPitchFallback;
        int built = 0;

        if (_shortButton == null && tray.ShortRestAnchor != null)
        {
            _shortButton = PlayTray.BoardButton.Create(tray.ShortRestAnchor, size,
                new Color(0.62f, 0.52f, 0.30f), // T4: parchment-gold accent when selected (antique)
                // No game key exists for a short rest button (GUI_SHORT_REST is absent) — mod string.
                Core.Loc.Mod("short_rest"),
                () => ShortRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel);
            _shortButton.WireCap = Net.NetProtocol.CapPressShortRest; // mirror the press dip to peers
            tray.RegisterLaserTarget(_shortButton.Collider!, _shortButton);
            built++;
        }
        if (_longButton == null && tray.LongRestAnchor != null)
        {
            _longButton = PlayTray.BoardButton.Create(tray.LongRestAnchor, size,
                new Color(0.37f, 0.44f, 0.56f), // T4: antique slate-blue accent when selected
                Core.Loc.Game("GUI_LONG_REST", "Long rest"),
                () => LongRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel);
            _longButton.WireCap = Net.NetProtocol.CapPressLongRest;
            tray.RegisterLaserTarget(_longButton.Collider!, _longButton);
            built++;
        }

        SetOffset(offset, spacing); // shared X/Y nudge in plane, Z proud, ± the scaled pad pitch along Y

        // Live language following: the rest-button captions are built once, so re-read them
        // whenever the game language changes (subscribe once; Destroy detaches).
        if (!_locHooked)
        {
            _locHooked = true;
            Core.Loc.OnChanged += RefreshLabels;
        }

        if (built > 0)
            Core.VRLog.Info("Cards", $"RestControls: built {built} {(round ? "ROUND" : "SQUARE")} rest button(s) " +
                $"for {active} (size {size.x:F3}×{size.y:F3} m, [RestButtons] W/H/D " +
                $"{WorldUI.ButtonTuning.RestCapWidth:F3}/{WorldUI.ButtonTuning.RestCapHeight:F3}/{WorldUI.ButtonTuning.RestCapDepth:F3} m, " +
                $"travel {WorldUI.ButtonTuning.RestCapTravel:F3} m, offset {offset}, spacing {spacing:F3} m) — " +
                "per-board predictable seat, no raycast.");
    }

    /// <summary>
    /// Live-apply (Part F): move both rest buttons to a new shared X/Y/Z nudge + stack spacing in
    /// place (no rebuild). <paramref name="spacing"/> is a MULTIPLE of this board's own measured pad
    /// pitch, not a metre gap: the short (upper) disc takes +half a scaled step along the board's
    /// short axis, the long (lower) disc −half, so at the shipped 1 both sit dead centre in their
    /// own authored pads on every board and the term contributes exactly nothing.
    /// </summary>
    internal void SetOffset(Vector3 offset, float spacing)
    {
        // …AND EACH DISC STAYS INSIDE ITS OWN PAD. Identical rule and identical function to the board
        // keycaps (BoardAnchors.ClampSeatPose): on a board whose pads the assembler measured, the
        // in-plane part of the tuned offset AND the spacing term are bounded by the slack between
        // this disc and the pad rim; Z is untouched. On an unmeasured board nothing is bounded and
        // this is bit-identical to the previous build — including Oak's tuned +8 mm X nudge, which a
        // canonical/mirrored test would have thrown away on the bundle he is running right now.
        Vector3 shortPose = BoardAnchors.ClampSeatPose(
            offset, BoardAnchors.StackDelta(0, 2, _padPitch, spacing), _padMinHalf, _capSize);
        Vector3 longPose = BoardAnchors.ClampSeatPose(
            offset, BoardAnchors.StackDelta(1, 2, _padPitch, spacing), _padMinHalf, _capSize);
        if (_shortButton != null)
            _shortButton.transform.localPosition = shortPose;
        if (_longButton != null)
            _longButton.transform.localPosition = longPose;
        LogPadClamp(offset, spacing, shortPose, longPose);
    }

    /// <summary>The tighter authored rest pad's half-extents, or null on an unmeasured board.</summary>
    private Vector2? _padMinHalf;

    /// <summary>This board's own rest-pad PITCH, measured off its two pad anchors at build time —
    /// the per-board term the retired <c>RestButtonSpacing_{board}</c> constants used to guess at.
    /// The fallback until a board has been measured.</summary>
    private float _padPitch = Defaults.StackPitchFallback;

    /// <summary>The disc size actually built (already pad-fitted) — the clamp's bound is the gap
    /// between THIS disc and the pad rim.</summary>
    private Vector2 _capSize = new(Defaults.RestButtons_Width, Defaults.RestButtons_Height);

    /// <summary>Last clamp reported, so a live re-layout only speaks up when the outcome changed.</summary>
    private Vector3 _padClampLogged = new(float.NaN, float.NaN, float.NaN);

    /// <summary>One line whenever the pad clamp actually BIT, naming the dial. Silent otherwise.</summary>
    private void LogPadClamp(Vector3 offset, float spacing, Vector3 shortPose, Vector3 longPose)
    {
        if (_padMinHalf == null)
            return;
        Vector3 wantShort = offset + new Vector3(0f, BoardAnchors.StackDelta(0, 2, _padPitch, spacing), 0f);
        Vector3 wantLong = offset + new Vector3(0f, BoardAnchors.StackDelta(1, 2, _padPitch, spacing), 0f);
        if (!BoardAnchors.SeatPoseWasClamped(wantShort, shortPose)
            && !BoardAnchors.SeatPoseWasClamped(wantLong, longPose))
            return;
        if ((shortPose - _padClampLogged).sqrMagnitude < 1e-12f)
            return;
        _padClampLogged = shortPose;
        Vector2 slack = BoardAnchors.SeatSlack(_padMinHalf.Value, _capSize);
        Core.VRLog.Info("Cards", "RestControls: PAD CLAMP — the tuned in-plane offset would have put a rest " +
            $"disc outside its own pad, so it was bounded to the ±({slack.x * 1000f:F1}, {slack.y * 1000f:F1}) mm " +
            "of slack the disc has in the pad. [Cards] RestButtonOffset = " +
            $"({offset.x:F3}, {offset.y:F3}, {offset.z:F3}), RestStackSpacing = {spacing:F3}× this board's " +
            $"own {_padPitch * 1000f:F1} mm pad pitch ⇒ " +
            $"short wanted ({wantShort.x:F4}, {wantShort.y:F4}) got ({shortPose.x:F4}, {shortPose.y:F4}), " +
            $"long wanted ({wantLong.x:F4}, {wantLong.y:F4}) got ({longPose.x:F4}, {longPose.y:F4}) m. " +
            "Z untouched. Those values were measured against the board asset installed when they were " +
            "tuned; this board's pads are authored and measured, so the pad is where the disc goes.");
    }

    /// <summary>Re-read the rest-button captions in the current language (live-follow, Loc.OnChanged).</summary>
    internal void RefreshLabels()
    {
        _shortButton?.SetLabel(Core.Loc.Mod("short_rest"));
        _longButton?.SetLabel(Core.Loc.Game("GUI_LONG_REST", "Long rest"));
    }

    internal void Destroy()
    {
        if (_locHooked)
        {
            Core.Loc.OnChanged -= RefreshLabels;
            _locHooked = false;
        }
        if (_shortButton != null)
            Object.DestroyImmediate(_shortButton.gameObject);
        if (_longButton != null)
            Object.DestroyImmediate(_longButton.gameObject);
        _shortButton = null;
        _longButton = null;
        ShortRestShown = false; // never advertise rest discs off a torn-down board
        LongRestShown = false;
        ShortRestEnabled = ShortRestAccent = false;
        LongRestEnabled = LongRestAccent = false;
    }

    /// <summary>
    /// Refresh each rest keycap's RELEVANCE visibility + availability/selected state (per frame
    /// while the tray shows). Issue 3 (user): the short/long-rest buttons must DISAPPEAR when a
    /// rest isn't a relevant option — exactly like the played cards clear during the action phase
    /// / enemy turns. The <see cref="PlayTray.BoardButton.SetVisible"/> flip animates via the
    /// button worker.
    /// </summary>
    /// <summary>
    /// The game's OWN "is a rest offered at all right now" predicate, quoted rather than invented.
    /// The flat UI uses exactly <c>PhaseManager.PhaseType == CPhase.PhaseType.
    /// SelectAbilityCardsOrLongRest &amp;&amp; !IsImprovedLongResting</c> in all three places it
    /// decides whether a rest control exists:
    /// <list type="bullet">
    /// <item><c>CardsHandUI.UpdateShortRest</c> — CardsHandUI.cs:700 — <c>shortRest.Show</c> on
    /// true, else <c>shortRest.ResetSelection; shortRest.Hide</c>. Note the RESET: vanilla
    /// throws the SELECTION away when it stops offering, which is why "selected" may never
    /// outrank this predicate;</item>
    /// <item><c>CardsHandUI.UpdateCards</c> — CardsHandUI.cs:1307 — activates the long-rest
    /// pseudo-card <c>CardID −1</c>, which IS the game's only long-rest control — there is no
    /// discrete long-rest widget;</item>
    /// <item><c>CardsHandUI.HidePreviewActionCards</c> — CardsHandUI.cs:1487 — the same pseudo-card
    /// gets <c>SetActive</c> off the same phase compare.</item>
    /// </list>
    /// Verified: <c>public bool IsImprovedLongResting</c> — CardsHandUI.cs:244 — which dereferences
    /// <c>playerActor</c> as soon as the phase matches, hence the null guard before it. Cheap: one
    /// enum compare outside the selection phase, no allocation.
    /// </summary>
    private static bool RestUiOffered(CardsHandUI hand) =>
        PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest
        && hand.PlayerActor != null
        && !hand.IsImprovedLongResting;

    internal void TickStatus(CardsHandUI? hand)
    {
        // ---- REST GATE (user report, ModBuild 134/135 round, verbatim) ---------------------------
        //   "Der Character mit den Initativschuhen hat lange Rast gewählt. Trotzdem muss er zu
        //    beginn der Runde eine Entscheidung treffen die Schuhe zu benutzen oder nicht. Während
        //    dieser Entscheidung blieb der 'Lange Rast' Knopf bestehen und verschwindet nicht -
        //    egal welchen Character ich auswähle. Die Knopf macht in dieser Phase keinen Sinn.
        //    Nachdem er die Enstcheidung bestätigt hat ist der Knopf verschwunden. Fix das noch."
        //
        // ROOT CAUSE. The boots decision is its own PHASE after card selection:
        // CheckForInitiativeAdjustments, walked one actor at a time by GameState.cs:1976-1999 and
        // announced per actor as CMessageData.MessageType.CheckForInitiativeAdjustments →
        // UIActiveBonusBar.ShowActiveBonus with AdjustInitiative — Choreographer.cs:11670-11692;
        // see also CardsGameApi.InitiativeAdjustHand, which already resolves that flow's actor.
        // canLong/canShort DO fence on the selection phase and were both false there — but the
        // visibility term was `canX || xSelected`, and CardsHandUI.IsLongRestSelected — :2662,
        // selectedCardsUI.Count == 1 && CardID == −1 — stays TRUE from the moment long rest is
        // committed until the fan's selection is torn down at the end of that decision. So the
        // OR-term alone held the keycap up for the whole prompt, and it vanished the instant the
        // player confirmed — exactly the reported timing. "Egal welchen Character ich auswähle"
        // follows: this tick reads CardsHandManager.CurrentHand, and vanilla deliberately does NOT
        // SwitchHand during CheckForInitiativeAdjustments — InitiativeTrackPlayerAvatar.cs:24 — so
        // the long-rester's hand stayed the one being sampled no matter who was picked.
        //
        // THE FIX. A keycap may never outlive the game's own OFFER: both visibilities are now
        // AND-ed with RestUiOffered — the flat UI's own predicate, see its doc — so the
        // "…Selected" terms can only keep a keycap up INSIDE the window in which vanilla itself
        // still draws a rest control. Vanilla resets the short-rest selection when it hides
        // (CardsHandUI.cs:706), which is the same statement in the game's own code.
        //
        // REJECTED. (a) Special-casing the boots phase — e.g. hiding while
        // CardsGameApi.InitiativeAdjustHand is non-null — enumerates prompts instead of stating the
        // rule; every other post-selection prompt would keep its own stale keycap. (b) Dropping the
        // "…Selected" OR-terms outright: the short keycap must stay up while the game's own yes/no
        // confirmation is open, and the long keycap must stay ACCENTED while the fan holds the −1
        // card — both live inside the selection phase, which the gate preserves. (c) Gating on
        // WorldUI.ModalFallback.BlockingWindowModalActive: the boots prompt is an ActiveBonusBar
        // plus a ReadyButton, not a blocking window, so it would not fire at all — and it would
        // wrongly hide the keycaps for unrelated modals DURING selection. (d) Clearing the
        // selection ourselves the way UpdateShortRest does: a per-frame DISPLAY tick must never
        // write rule-engine state.
        //
        // A rest is only OFFERED during the card-SELECTION phase (SelectAbilityCardsOrLongRest),
        // and the game distinguishes short vs long availability independently — so each keycap
        // follows its OWN availability signal (CardsGameApi.CanShortRest / CanLongRest, both of
        // which already gate on the selection phase and return false during action/enemy turns,
        // mirroring the card board's IsActionTurn clear). A keycap stays up while its rest is
        // SELECTED / mid-choice (|| …Selected) so it does not vanish the instant it is chosen —
        // its accent then reads as the commitment — but ONLY inside the offer window above, which
        // is the whole point of the gate. Item 1 note kept: the native "Kurze Rast"
        // widget never docks (TrayControlDockSurface.ShortRestDocked permanently false), so this
        // mod keycap is the sole short-rest control; long rest never had a discrete uGUI widget.
        bool canShort = false, canLong = false, shortSelected = false, longSelected = false;
        bool offered = false;
        if (hand != null)
        {
            offered = RestUiOffered(hand);
            canShort = CardsGameApi.CanShortRest(hand);
            canLong = CardsGameApi.CanLongRest(hand);
            shortSelected = CardsGameApi.IsShortRestSelected(hand);
            longSelected = CardsGameApi.IsLongRestSelected(hand);
        }

        bool shortVisible = offered && (canShort || shortSelected);
        bool longVisible = offered && (canLong || longSelected);

        // The ONE gate line: fires only on the edge where the offer gate is actually suppressing a
        // keycap a stale selection flag would otherwise have kept up — i.e. precisely the reported
        // boots-decision case — so the next hardware log proves the gate fired and why.
        bool gateSuppressing = !offered && (shortSelected || longSelected);
        if (gateSuppressing != _lastGateSuppressing)
        {
            _lastGateSuppressing = gateSuppressing;
            if (gateSuppressing)
                Core.VRLog.Info("Cards", "REST GATE: rest keycaps hidden — the game offers no rest control here " +
                                         $"{(PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest ? "because the improved-short-rest long rest is running" : $"in phase {PhaseManager.PhaseType}")}" +
                                         $", while a stale selection flag was still set: short={shortSelected}, long={longSelected}. " +
                                         "Same predicate as CardsHandUI.UpdateShortRest / the long-rest pseudo-card.");
        }

        if (shortVisible != _lastShortVisible || longVisible != _lastLongVisible)
        {
            _lastShortVisible = shortVisible;
            _lastLongVisible = longVisible;
            Core.VRLog.Info("Cards", $"Rest buttons visibility: short={shortVisible} " +
                                     $"[canShort={canShort}, selected={shortSelected}], long={longVisible} " +
                                     $"[canLong={canLong}, selected={longSelected}], offered={offered} " +
                                     "— hidden when the game itself offers no rest control: action phase, " +
                                     "enemy turns, and every start-of-round prompt after selection.");
        }

        _shortButton?.SetVisible(shortVisible);
        _longButton?.SetVisible(longVisible);
        _shortButton?.SetState(canShort, accent: shortSelected);
        _longButton?.SetState(canLong, accent: longSelected);

        // Publish for the multiplayer board-UI record (only meaningful while the caps exist).
        ShortRestShown = _shortButton != null && shortVisible;
        LongRestShown = _longButton != null && longVisible;
        // …and the cap STATES the very SetState calls above just applied (record 4 byte 2), so a
        // peer's mirrored disc dims and accents in the same frames the owner's does.
        ShortRestEnabled = canShort;
        ShortRestAccent = shortSelected;
        LongRestEnabled = canLong;
        LongRestAccent = longSelected;
    }
}
