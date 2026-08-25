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
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel,
                // THE CAP SYMBOL IS THE PAD'S OWN STENCIL. `rest_short` — the crescent-and-embers
                // device — is the motif carved into the SHORT-rest pad this disc sits in, on this
                // very board (`unity/board-prep/tex_atlas.py`, rest_pads), taken from the sheet
                // that style's board took it from. The design asks the cap to "echo the pad it
                // sits beside"; using the pad's own binary stencil is not an echo, it is the same
                // shape, and it costs no generated image. With a symbol on the face the disc drops
                // its word entirely and the word is ENGRAVED INTO THE BOARD above the pad instead
                // (BoardEngraving) — the user's "nativ und immersiv in dem board verarbeitet".
                capRole: CapRole.ShortRest, capStyle: active);
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
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel,
                capRole: CapRole.LongRest, capStyle: active);
            _longButton.WireCap = Net.NetProtocol.CapPressLongRest;
            tray.RegisterLaserTarget(_longButton.Collider!, _longButton);
            built++;
        }

        // The engraved board captions, built once beside their pads. Only when the cap actually
        // WEARS a symbol: on a bundle with no keycap atlas the discs keep their own word labels
        // (CapSymbols.SymbolOnly is false there), and engraving the same word into the board as
        // well would be the caption twice.
        bool engrave = CapSymbols.TryAtlas(active, out _, out _);
        if (engrave && _shortCaption == null && tray.ShortRestAnchor != null)
            _shortCaption = BoardEngraving.Create(tray.ShortRestAnchor, "ShortRestEngraving",
                Vector3.zero, BoardEngraving.RestCaptionBox,
                BoardEngraving.CaptionMaxFontSize, active);
        if (engrave && _longCaption == null && tray.LongRestAnchor != null)
            _longCaption = BoardEngraving.Create(tray.LongRestAnchor, "LongRestEngraving",
                Vector3.zero, BoardEngraving.RestCaptionBox,
                BoardEngraving.CaptionMaxFontSize, active);
        // Into the board's furniture draw-order group, like every other depthless transparent
        // renderer the board carries — see BoardEngraving.Create for why a carving needs it too.
        if (_shortCaption != null) PlayTray.AdoptFurniture(_shortCaption.gameObject);
        if (_longCaption != null) PlayTray.AdoptFurniture(_longCaption.gameObject);
        RefreshLabels();

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
        // The captions ride their own disc's clamped pose, in the board PLANE only: they take the
        // disc's x/y and BoardEngraving's own flush depth, never the disc's proud Z — a caption is
        // cut into the board, the disc stands on it.
        if (_shortCaption != null)
            _shortCaption.transform.localPosition = new Vector3(
                shortPose.x, shortPose.y + CaptionOffsetY, BoardEngraving.ProudLocalZ);
        if (_longCaption != null)
            _longCaption.transform.localPosition = new Vector3(
                longPose.x, longPose.y - CaptionOffsetY, BoardEngraving.ProudLocalZ);
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

    /// <summary>
    /// THE ENGRAVED CAPTIONS — "Kurze Rast" / "Lange Rast" cut into the board beside their own pads.
    ///
    /// <para>They exist because the discs no longer carry a word: with the pad's own crescent-and-
    /// embers / spoked-wheel device carved into the cap face (CapRole.ShortRest / LongRest), a
    /// caption on the cap would be the same statement twice and would crowd out the symbol. The
    /// user's rule for that trade is explicit — decide per case, and put yourself in a player who
    /// knows neither the symbols nor the game. So the word survives, it just moves into the board,
    /// where it teaches once and then stops competing for attention. His own example was this
    /// control: "Eventuell kannst du auch Text dynamisch in das board mit einarbeiten? Zb über dem
    /// long-rest button."</para>
    ///
    /// <para>They are PARENTED TO THE SAME ANCHORS as the discs and positioned from the same
    /// clamped pose, so every tuning dial that moves a disc moves its caption with it. That is not
    /// tidiness: RestButtonOffset carries 44 cm mirror-compensation values on two of the three
    /// shipped boards, and a caption at a fixed board position would be left behind on the far side
    /// of the board from the control it names.</para>
    ///
    /// <para>SHORT ABOVE, LONG BELOW — the two pads are only ~105-120 mm apart and each is ~80 mm
    /// across, so the gap between them is about 25 mm and cannot hold two captions. The upper pad's
    /// caption goes up into the board's top margin and the lower pad's goes down into its bottom
    /// margin, which is where the room actually is.</para>
    /// </summary>
    private TMPro.TextMeshPro? _shortCaption, _longCaption;

    /// <summary>Clearance from this board's built disc to its caption, from the ONE definition both
    /// this board and every peer's mirror of it use (<see cref="BoardEngraving.RestCaptionOffsetY"/>).</summary>
    private float CaptionOffsetY => BoardEngraving.RestCaptionOffsetY(_capSize.y);

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
        // The cap's own string is kept up to date whether or not it is drawn: it is what the
        // multiplayer cap-label seam publishes, and it is what comes back if a bundle without the
        // keycap atlas is installed.
        _shortButton?.SetLabel(Core.Loc.Mod("short_rest"));
        _longButton?.SetLabel(Core.Loc.Game("GUI_LONG_REST", "Long rest"));
        // THE ENGRAVED CAPTIONS FOLLOW THE LANGUAGE, which is the entire reason they are TMP text
        // laid into the board rather than pixels baked into the board's atlas. Loc.OnChanged is
        // already subscribed for the caps; the captions ride the same notification.
        //
        // THE ENGRAVING SAYS EXACTLY WHAT THE CAP USED TO SAY, through the same two keys, and that
        // is deliberate rather than lazy. New Loc keys would be two more strings that can drift
        // from the caps they replaced — and Loc.Mod has NO fallback, so a key that is added on one
        // side and missed on the other renders as the raw key on the board. Upper-cased here
        // because a cut plaque is lettered in caps, which is a presentation decision and belongs
        // in the presenter, not in a second pair of translations. The German is the longer of each
        // pair ("KURZE RAST" vs "SHORT REST" is a wash; "LANGE RAST" vs "LONG REST" is one glyph
        // wider) and CaptionBox is sized so both fit at the same size.
        BoardEngraving.SetText(_shortCaption, Core.Loc.Mod("short_rest").ToUpperInvariant());
        BoardEngraving.SetText(_longCaption,
            Core.Loc.Game("GUI_LONG_REST", "Long rest").ToUpperInvariant());
        // A late-arriving HUD font (NativeButtonSkin harvests it off a live widget) leaves the
        // engraving unstyled at build time; re-apply here, where it is cheap and idempotent.
        ControlBoard style = CardsConfig.CurrentBoard;
        if (_shortCaption != null) BoardEngraving.Restyle(_shortCaption, style);
        if (_longCaption != null) BoardEngraving.Restyle(_longCaption, style);
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
        if (_shortCaption != null)
            Object.DestroyImmediate(_shortCaption.gameObject);
        if (_longCaption != null)
            Object.DestroyImmediate(_longCaption.gameObject);
        _shortButton = null;
        _longButton = null;
        _shortCaption = null;
        _longCaption = null;
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
        // A CAPTION FOR A CONTROL THAT IS NOT THERE IS A LABEL ON AN EMPTY PATCH OF BOARD. The
        // discs vanish whenever the game is not offering a rest (action phase, enemy turns, every
        // start-of-round prompt after selection), so their engravings go with them. Straight
        // SetActive rather than the dust dissolve: the caps CRUMBLE because they are objects
        // standing on the board, and a cut in the board is not an object — it is simply not
        // carved while there is nothing to name. The change gate keeps this off the per-frame path.
        if (_shortCaption != null && _shortCaption.gameObject.activeSelf != shortVisible)
            _shortCaption.gameObject.SetActive(shortVisible);
        if (_longCaption != null && _longCaption.gameObject.activeSelf != longVisible)
            _longCaption.gameObject.SetActive(longVisible);
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
