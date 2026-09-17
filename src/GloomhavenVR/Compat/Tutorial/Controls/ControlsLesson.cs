using GloomhavenVR.Hands;
using GloomhavenVR.Rig;

namespace GloomhavenVR.Compat;

/// <summary>One taught control: what to say, which key to light, and what counts as done.</summary>
internal readonly struct ControlsStep
{
    /// <summary>What <see cref="ControlsProgress"/> must see. <see cref="ControlAction.None"/>
    /// marks a step with no motion to perform — the welcome and the closing card.</summary>
    internal readonly ControlAction Action;

    /// <summary>Loc id stem; the title is <c>{Id}_t</c> and the body <c>{Id}_b</c>.</summary>
    internal readonly string Id;

    /// <summary>Key to light on the applicable controller(s), or null.</summary>
    internal readonly string? Key;

    /// <summary>How much of <see cref="Action"/> completes it, in that action's own units
    /// (metres, degrees, log2 octaves, or 1 for a discrete event).</summary>
    internal readonly float Target;

    /// <summary>
    /// Loc id of a KEY NAME to substitute into <c>{0}</c> of the body, or null. It exists because
    /// the same key is not called the same thing on every device: the Steam Frame's four top
    /// inputs are a D-PAD, and telling its owner to "press A" names nothing their thumb can find.
    /// <see cref="ControllerVisual.HasDpad"/> picks the <c>_dpad</c> variant.
    /// </summary>
    internal readonly string? KeyNameId;

    // `Situational` lived here — "the step depends on something the room may not be offering right
    // now (an open window to reel, a card in hand)". Its ONE reader picked the box's second button
    // label, "GEHT GERADE NICHT", for such a step. The user removed that button on 2026-09-02 and
    // every card now offers the same ÜBERSPRINGEN, so the flag decided nothing and is gone rather
    // than left sitting in the table unread. Which steps need the room in a particular state is
    // still readable from the section comments in ControlsLesson.Steps.

    internal ControlsStep(ControlAction action, string id, string? key,
                          float target = 1f, string? keyNameId = null)
    {
        Action = action;
        Id = id;
        Key = key;
        Target = target;
        KeyNameId = keyNameId;
    }
}

/// <summary>
/// THE CONTROLS LESSON — the list of things a newcomer has to be told, in the order that makes
/// each one useful before the next.
///
/// <para>HOW THE LIST WAS DECIDED. It is not a survey of the input code; it is every row of
/// docs/PLAYING.md's control tables plus the two card modes, cross-checked back against the code
/// that reads each key (WorldGrab's stick CLICK, SnapTurn's stick AXIS, Flight's push,
/// ProximityGrabber's trigger, BoardClickDriver's laser and fingertip routes, LaserCarryReel's
/// stick, BoardPing's A/X, the B+Y recentre chord, and HeldCardGrip's grip-plus-trigger). Written
/// out that way it comes to thirteen, which is more than a tutorial should ask anyone to sit
/// through in one go — so the order matters: the first seven are what you cannot play without, and
/// everything after that is a convenience the player can step straight past — since 2026-09-02 the
/// box's button skips THE CARD IN FRONT OF THEM rather than the whole lesson, so walking away from
/// the tail of the list is a run of presses instead of one.</para>
///
/// <para>ORDER. Getting about comes first, because both of the cards that used to precede it were
/// removed on 2026-09-03 — point-and-click and then reach-and-grab, each for the same reason he
/// gave twice: the player has already done it, or cannot do it here (see the two gaps in the
/// table). ALL FOUR WAYS OF GETTING ABOUT, TOGETHER: pull yourself along, fly, turn, then zoom
/// and rotate. The one-handed three come first and the two-handed pair after (user, 2026-08-29) —
/// moving yourself is what a newcomer reaches for in the first minute, and it needs the same single
/// stick the drag just taught. Postponing it behind the two-handed gestures, as the first version
/// did, split the stick's lesson in half and put the harder grip in the middle of it. Then the
/// control board by its bar (added 2026-09-03 at the user's request) — the first GRIP, alone,
/// before the cards need the trigger and then the two together. Then the cards, which are the
/// game. Everything after is a convenience.</para>
///
/// <para>AND EVERY ROW IS NOW CONDITIONAL ON THE PLAYER'S OWN SETTINGS — see
/// <see cref="Availability"/>, which is the single place that knows which dial gates which step and
/// which hand actually performs it.</para>
///
/// <para>WHY THE THUMBSTICK APPEARS FIVE TIMES, and why that is the honest way round. Drag,
/// fly, snap-turn, zoom and rotate all live on the thumbstick, distinguished by CLICKING it in
/// versus PUSHING it, and by one hand versus two. Merging them into "the thumbstick moves you"
/// would light one key and teach nothing; five short steps, each ending the moment the player
/// does the actual motion, is what makes the distinction land. They are now CONSECUTIVE, which
/// is what lets the five read as one lesson about one stick rather than as five unrelated
/// controls that happen to share it.</para>
/// </summary>
internal static class ControlsLesson
{
    internal static readonly ControlsStep[] Steps =
    {
        // Both models remain visible throughout the lesson (user ruling, build 518).
        // Prose cards simply clear both highlights; visibility is not a per-step choice.
        new(ControlAction.None, "ctl_welcome", null),

        // --- what you cannot play without -------------------------------------------------
        // ctl_laser (ControlAction.LaserClick, "Point and click") STOOD HERE and is gone — user
        // ruling 2026-09-03, verbatim: "Entferne den ersten Test 'Zeigen und Auswählen' - wenn man
        // es bis hier hin geschaft hat kennt man das schon - hat keinen Mehrwert." The lesson runs
        // INSIDE the game's own tutorial box and only opens once the tutorial's opening story
        // dialogue has been DISMISSED, and dismissing it is a laser click on a button — so every
        // player who can see this card has already performed the control it taught. His own log
        // agreed before he said it: the run in .planning/debug/second_logs/Player.log skipped it.
        // ControlAction.LaserClick stays in the enum and BoardClickDriver still reports it; with no
        // step waiting on it ControlsProgress.Notify returns on its first line and it costs nothing.
        //
        // ctl_grab (ControlAction.ProximityGrab, "Zugreifen" / "Reach out and grab") STOOD HERE
        // and is gone — user ruling 2026-09-03, verbatim: "Entferne die erste Aufgabe 'Zugreifen'
        // - hier ist nicht klar was du damit meinst und die Aufgabe hat keinen Mehrwert." It had
        // become the first TASK earlier the same day, when ctl_laser was removed for the same
        // reason; the welcome card above it asks for nothing and is not an Aufgabe.
        //
        // WHY IT WAS ALSO UNSATISFIABLE, which is the part worth writing down. The card said "move
        // your hand to the thing itself and squeeze the TRIGGER" with no object — the list naming
        // WHAT to reach for was cut as noise on 2026-09-03 — and the room cannot supply one: the
        // lesson runs inside the tutorial's own box before a single card has been played, with no
        // chest, no coin pile and no loose prop within reach. The only thing that could have
        // completed it was the card fan, which ctl_card_take teaches four rows down with a target
        // the player can actually see. A step that can never complete is the failure mode this
        // lesson keeps having to be rescued from, and this one had it.
        //
        // THE ENUM VALUE AND ITS CALL SITE STAY, exactly as ControlAction.LaserClick's did.
        // ControlsProgress.Waiting is only ever set to an action some step declares, so with no
        // such step Notify returns on its first line and ProximityGrabber.cs:647 costs two static
        // reads on the grab path. Deleting the value would mean editing Hands/, which this lane
        // does not own, to remove a call that already does nothing.

        // GETTING ABOUT, ALL FOUR TOGETHER AND ONE-HANDED FIRST (user, 2026-08-29: fly and turn
        // belong straight after the drag). All four move YOU — the one-stick drag included, which
        // is the 2026-09-03 correction: WorldGrab applies its motion inversely to the RIG ROOT, so
        // the table only appears to slide. They are what a player reaches for in the first minute —
        // each needs one hand and one stick. The two-handed zoom and rotate come after, because they are a different gesture
        // (both sticks clicked in at once) and asking for it before the one-handed stick is
        // understood is what makes the stick feel like five unrelated controls.
        new(ControlAction.WorldDrag, "ctl_drag", ControllerKey.Thumbstick,
            target: 0.25f),
        new(ControlAction.Fly, "ctl_fly", ControllerKey.Thumbstick,
            target: 0.8f),
        new(ControlAction.SnapTurn, "ctl_turn", ControllerKey.Thumbstick,
            target: 20f),
        new(ControlAction.WorldZoom, "ctl_zoom", ControllerKey.Thumbstick,
            target: 0.35f),
        new(ControlAction.WorldRotate, "ctl_rotate", ControllerKey.Thumbstick,
            target: 25f),

        // THE CONTROL BOARD, CARRIED BY ITS BAR (user request 2026-09-03, verbatim: "noch einen
        // Test: der das Kontrollbrett mit der GRIP-Taste an der Grab-Bar greift und bewegt").
        //
        // WHY HERE. It is the first card that touches the GRIP, and it sits between the five stick
        // cards and the two card cards for the same reason the sticks are consecutive: one key at
        // a time. The stick lesson has just ended; this introduces the grip ALONE, on the one
        // object in the room that is always there to grip (the board is built whenever a hand is
        // active); then ctl_card_take asks for the TRIGGER alone, and ctl_card_hold combines the
        // two ("hold the GRIP as well") — a card that would be teaching two keys at once if neither
        // had been met before. It also puts the board where the player wants it BEFORE the card
        // cards ask them to drop cards into its slots.
        //
        // CONTROLLER, by the rule above the table: the step turns on FINDING A KEY the player may
        // not have found yet. ControllerKey.Squeeze lights on the models that have a grip part and
        // falls back to the anchor marker or to nothing on the two that do not (the Index's grip is
        // a force sensor with no mesh; the generic model has no separate grip) — the word GRIP in
        // the body is the channel that works on all three, which is why it is shouted.
        //
        // WHAT COMPLETES IT: 0.20 m of REAL palm travel, net from the point where the bar was
        // taken, with the GRIP held on the PlayTray's own bar (WorldUI.PanelGrabHandle reports it
        // for that owner only, and never for a laser carry — that is the trigger, and another
        // card). Net rather than summed so tracking jitter on a still hand cannot accumulate into
        // a completion; 0.20 m is a deliberate move and well under the reach of a seated arm.
        new(ControlAction.BoardCarry, "ctl_board", ControllerKey.Squeeze,
            target: 0.20f),

        // THE CONTROL BOARD, RESIZED BY BOTH GRIPS (user request 2026-09-03, verbatim: "Dass man
        // das (Kontroll-)Brett mit den GRIP-Tasten kleiner und größer skalieren kann sollte als
        // Test/Schritt im Tutorial ergänzt werden").
        //
        // THE GESTURE, AS THE CODE HAS IT (WorldUI.PanelGrabHandle, not invented here): the bar
        // under the board is ONE grabbable with two grip slots — CanGrab stays true while a slot is
        // free, so a second palm inside the same bar zone (PlayTray.BuildHandle's trigger box, 62 %
        // of the board wide) squeezing its GRIP lands in _handB, and from that frame the pair
        // resizes: target localScale = scale-at-engage × (hand distance / distance-at-engage),
        // clamped LIVE into the board's own apparent-width window (PlayTray's GrabScaleLimits,
        // the [Cards] BoardMin/BoardMaxWidthMeters band). Hands apart grows it, together shrinks
        // it; the pair's midpoint carries it along. Both hands on the BAR — a grip anywhere else
        // on the board takes nothing.
        //
        // WHY HERE: straight after the one-hand carry, on the same bar with the same key, so the
        // card adds exactly one thing — the second hand. Both controllers light (LessonHands.Both
        // is the instruction, as for the recentre chord: both grips ARE pressed).
        //
        // WHAT COMPLETES IT: the board's localScale moved by 15 % or more, in EITHER direction,
        // from where it stood when the step's first two-hand pinch began — |scale/scale0 − 1| as a
        // rising maximum, in ControlsProgress.Accumulated, across as many pinches as it takes. The
        // origin is the STEP's (ControlsProgress.BoardScaleStart), not the pinch's, so two small
        // pinches in the same direction add up. 15 % is a change the eye cannot miss and a tenth of
        // the shipped window's span, so a board parked against one limit still has the other way.
        new(ControlAction.BoardScale, "ctl_board_scale", ControllerKey.Squeeze,
            target: 0.15f),

        // --- the game ---------------------------------------------------------------------
        // These two, and the three conveniences below, need the room to be in a particular state —
        // a card in the fan, a window open to reel. That used to set a `Situational` flag that
        // relabelled the box's second button; the button is gone and so is the flag, and the
        // player steps past any of them with the same ÜBERSPRINGEN as every other card.
        // Card and fingertip tasks retain both controller models as requested in build 518.
        // Their trigger/grip is highlighted on every hand allowed by Availability.
        new(ControlAction.CardTake, "ctl_card_take", ControllerKey.Trigger),
        new(ControlAction.CardInHand, "ctl_card_hold", ControllerKey.Squeeze),

        // --- conveniences -----------------------------------------------------------------
        new(ControlAction.FingertipPick, "ctl_fingertip", ControllerKey.Squeeze),
        new(ControlAction.PanelReel, "ctl_reel", ControllerKey.Thumbstick),
        new(ControlAction.Ping, "ctl_ping", ControllerKey.Primary,
            keyNameId: PrimaryKeyNameId),
        new(ControlAction.Recenter, "ctl_recenter", ControllerKey.Secondary,
            keyNameId: "ctl_key_secondary"),
        // LAST, and on purpose: it is the card that hands the player everything else. The closing
        // card then points at the settings they have just seen how to reach. CONTROLLER: it is a
        // face button on a named hand, and getting the wrong hand is the whole failure mode.
        new(ControlAction.OpenMenu, "ctl_menu", ControllerKey.Primary,
            keyNameId: PrimaryKeyNameId),

        // Both controllers stay visible until the closing card is dismissed.
        new(ControlAction.None, "ctl_done", null),
    };

    /// <summary>Which controller(s) a step is performed with. A flag set rather than a
    /// <c>HandSide?</c> because "both" is a real answer for most rows and has to be expressible
    /// alongside "the left one only" — see <see cref="Availability"/>.</summary>
    [System.Flags]
    internal enum LessonHands
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = Left | Right,
    }

    /// <summary>The verdict on one step, against the settings AS THEY ARE RIGHT NOW.</summary>
    internal readonly struct StepAvailability
    {
        /// <summary>False ⇒ the lesson must never display this card at all.</summary>
        internal readonly bool Available;

        /// <summary>The hand(s) that actually perform it, and therefore the only hand(s) whose key
        /// may light. <see cref="LessonHands.None"/> whenever <see cref="Available"/> is false.
        /// </summary>
        internal readonly LessonHands Hands;

        /// <summary>One clause naming the SETTING that decided it, for the hardware log. Never
        /// null: a verdict with no reason is a verdict nobody can check.</summary>
        internal readonly string Why;

        internal StepAvailability(bool available, LessonHands hands, string why)
        {
            Available = available;
            Hands = hands;
            Why = why;
        }
    }

    /// <summary>
    /// DOES THIS STEP APPLY TO THIS PLAYER, AND ON WHICH HAND? (user ruling 2026-09-03, verbatim:
    /// <i>"Beim 'Dich selbst Bewegen' Test sollte nur derjenige joystick (links/rechts) leuchten,
    /// der auch tatsächlich bewegt, abhängig von den aktuellen Einstellungen. Ist die Option
    /// deaktiviert sollte dieser Test übersprungen werden, das gilt für alle Tests - sie sollen
    /// sich je nach aktuellen Einstellungen anpassen!"</i>)
    ///
    /// <para>ONE SWITCH, AND ALL OF THE SETTINGS KNOWLEDGE LIVES IN IT. The alternative — a
    /// per-step predicate in the table, or a gate read at the point each card is applied — was
    /// rejected for the reason this project has been bitten by repeatedly: a rule written down in
    /// several places is a rule that will disagree with itself, and the two questions "may this card
    /// be shown?" and "which key lights?" have to come out of the SAME read of the SAME dial, or a
    /// step ends up displayed with nothing lit on either controller.</para>
    ///
    /// <para>UNBOUND CONFIG FAILS OPEN, deliberately. <c>ComfortSettings.IsBound</c> is false before
    /// the config binds and again after teardown, and every dial below would then read a
    /// <c>null!</c> entry. Answering "available, both hands" there means the worst case is a card
    /// teaching a control the player has switched off — visible, skippable and obvious. The other
    /// way round the lesson silently deletes itself, which is the failure nobody ever reports.</para>
    ///
    /// <para>WHAT WAS FOUND RATHER THAN ASSUMED. The last five actions were expected to be ungated;
    /// three of them are not:
    /// <list type="bullet">
    /// <item><c>FingertipPick</c> HAS a switch — <c>[Board] TouchTilesWithFingertip</c>, which
    /// <c>BoardPick</c> calls "the SINGLE switch" for the near pick. With it off no fingertip touch
    /// is ever produced, so the card could not be satisfied by any amount of trying.</item>
    /// <item><c>CardInHand</c> HAS a switch — <c>[Cards] InHandHold</c>, read through
    /// <c>HeldCardGrip.Enabled</c> (itself defensive about the config's lifetime). With it off the
    /// grip does nothing while a card is held, which is exactly what that card teaches.</item>
    /// <item><c>Ping</c> has no on/off switch but IS one-handed: <c>BoardPing.Tick</c> reads
    /// <c>VRHands.Primary</c> and nothing else, so the A/X press only pings on the DOMINANT hand.
    /// Lighting both would send the left-handed half of the players to the wrong thumb.</item>
    /// <item><c>ProximityGrab</c> and <c>CardTake</c> really are ungated — neither
    /// <c>ProximityGrabber</c> nor the palm fan binds an enable entry — and really are two-handed.
    /// </item>
    /// </list></para>
    ///
    /// <para>THE HANDS ARE RESOLVED THE WAY THE REST OF THE MOD RESOLVES THEM: the dominant hand
    /// through <c>VRHands.Primary</c> (<c>[Hands] PrimaryHand</c>), and the turn/flight dials through
    /// <c>LocalTurnControl.Resolve</c> — the one resolver <c>Flight</c> and <c>AoeControl</c> already
    /// share. A second copy of either switch would be a second place to disagree about which stick
    /// is which, which is the bug <c>LocalTurnControl</c> exists to have ended.</para>
    /// </summary>
    internal static StepAvailability Availability(in ControlsStep step)
    {
        // A prose card asks for nothing, so no setting can take it away — and that is what keeps
        // the lesson from ever being empty: whatever the resolver does to the teaching rows,
        // ctl_welcome and ctl_done are always shown.
        if (step.Action == ControlAction.None)
            return new StepAvailability(true, LessonHands.Both, "a prose card, gated by nothing");

        if (!ComfortSettings.IsBound)
            return new StepAvailability(true, LessonHands.Both,
                "[Comfort] is not bound yet — every step fails OPEN rather than vanishing");

        switch (step.Action)
        {
            case ControlAction.WorldDrag:
                return ComfortSettings.WorldGrabEnabled.Value
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Comfort] WorldGrabEnabled — either stick clicked in drags")
                    : Off("[Comfort] WorldGrabEnabled = false");

            case ControlAction.WorldZoom:
                if (!ComfortSettings.WorldGrabEnabled.Value)
                    return Off("[Comfort] WorldGrabEnabled = false (the two-hand grab IS the zoom)");
                return ComfortSettings.ScaleEnabled.Value
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Comfort] WorldGrabEnabled + ScaleEnabled — the gesture needs both sticks")
                    : Off("[Comfort] ScaleEnabled = false");

            case ControlAction.WorldRotate:
                if (!ComfortSettings.WorldGrabEnabled.Value)
                    return Off("[Comfort] WorldGrabEnabled = false (the two-hand grab IS the rotate)");
                return ComfortSettings.RotateEnabled.Value
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Comfort] WorldGrabEnabled + RotateEnabled — the gesture needs both sticks")
                    : Off("[Comfort] RotateEnabled = false");

            case ControlAction.Fly:
                if (!ComfortSettings.FlightEnabled.Value)
                    return Off("[Comfort] FlightEnabled = false");
                // Forward/strafe flight reads the stick on [Comfort] FlightHand and only that one
                // (Flight.ResolveFlightHand). The VERTICAL lift rides [Comfort] TurnHand instead,
                // but it is off by default and is not the motion this card asks for.
                return One(LocalTurnControl.Resolve(ComfortSettings.FlightHand.Value),
                    "[Comfort] FlightEnabled, FlightHand = " + ComfortSettings.FlightHand.Value);

            case ControlAction.SnapTurn:
                if (ComfortSettings.Turn.Value == TurnMode.Off)
                    return Off("[Comfort] TurnMode = Off");
                return One(LocalTurnControl.Resolve(ComfortSettings.TurnHand.Value),
                    "[Comfort] TurnMode = " + ComfortSettings.Turn.Value
                    + ", TurnHand = " + ComfortSettings.TurnHand.Value);

            case ControlAction.PanelReel:
                if (!ComfortSettings.LaserCarryReel.Value)
                    return Off("[Comfort] LaserCarryReel = false");
                // The reel only runs INSIDE a laser carry, and the laser carry is dominant-hand-only
                // (RayGrabDriver.Tick's opening comment; Flight.cs restates it where it arbitrates
                // the reel against forward flight). So it is the dominant stick or no stick.
                return One(DominantSide(),
                    "[Comfort] LaserCarryReel — and a laser carry only ever runs on the dominant "
                    + "hand ([Hands] PrimaryHand -> " + DominantSide() + ")");

            case ControlAction.Recenter:
                return ComfortSettings.RecenterHoldSeconds.Value > 0f
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Comfort] RecenterHoldSeconds > 0 — a two-hand chord, so both light")
                    : Off("[Comfort] RecenterHoldSeconds = 0 (the chord cannot fire)");

            case ControlAction.FingertipPick:
                return FingertipTouchOn()
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Board] TouchTilesWithFingertip — either fingertip commits")
                    : Off("[Board] TouchTilesWithFingertip = false");

            case ControlAction.CardInHand:
                return Cards.HeldCardGrip.Enabled
                    ? new StepAvailability(true, LessonHands.Both,
                        "[Cards] InHandHold — either hand may take its card into the fist")
                    : Off("[Cards] InHandHold = false");

            case ControlAction.Ping:
                return One(DominantSide(),
                    "no on/off switch, but BoardPing reads VRHands.Primary only "
                    + "([Hands] PrimaryHand -> " + DominantSide() + ")");

            case ControlAction.OpenMenu:
                // The card already says "the hand you do NOT point with"; this is that sentence
                // made true on the model, by the same derivation NonDominantHold performs.
                return One(Opposite(DominantSide()),
                    "always available; the pause-menu tap is on the NON-dominant hand "
                    + "([Hands] PrimaryHand -> " + DominantSide() + ")");

            case ControlAction.BoardCarry:
                // Checked rather than assumed: the board's bar has no on/off dial. [Cards]
                // BoardMoveMode only chooses HOW a carry may rotate the board (Frei / Begrenzt /
                // Begrenzt mit Neigung — PlayTray's IPanelGrabOwner.CarryMode), every mode moves
                // it; PanelGrabHandle.GrabWithGrip is a constant true; and the bar itself is built
                // with the board (PlayTray.BuildHandle). Either hand's grip takes it.
                return new StepAvailability(true, LessonHands.Both,
                    "no setting gates the board's bar ([Cards] BoardMoveMode only picks the carry "
                    + "mode) — either hand's GRIP takes it");

            case ControlAction.BoardScale:
                // The pinch itself has no on/off dial either, but its RESULT is clamped into the
                // [Cards] BoardMinWidthMeters..BoardMaxWidthMeters apparent-width band
                // (PlayTray's GrabScaleLimits, live every resize frame). A band whose span is below
                // 1.15² cannot offer a 15 % change in at least one direction from every position
                // inside it — so the card would be unsatisfiable, and is removed rather than shown.
                // The max is floored at min + 2 cm exactly as PlayTray.MaxWidthMeters floors it.
                return BoardResizeSpan() >= 1.15f * 1.15f
                    ? new StepAvailability(true, LessonHands.Both,
                        "no setting gates the two-hand pinch; [Cards] BoardMin/BoardMaxWidthMeters "
                        + $"span x{BoardResizeSpan():0.00} leaves 15 % of room in at least one "
                        + "direction — both GRIPs press, so both light")
                    : Off("[Cards] BoardMin/BoardMaxWidthMeters span "
                        + $"x{BoardResizeSpan():0.00} is too narrow for a 15 % change");

            default:
                // CardTake — no config entry gates it and it works on either hand. LaserClick and
                // ProximityGrab no longer have a step at all (both removed 2026-09-03) but both
                // actions still exist and are still reported by their call sites, so they are
                // answered here rather than left to an accident.
                return new StepAvailability(true, LessonHands.Both, "no setting gates this step");
        }
    }

    private static StepAvailability Off(string why)
    {
        return new StepAvailability(false, LessonHands.None, why);
    }

    private static StepAvailability One(HandSide side, string why)
    {
        return new StepAvailability(true,
            side == HandSide.Left ? LessonHands.Left : LessonHands.Right, why);
    }

    /// <summary>The loc id to substitute into a card's <c>{0}</c>, and why that one.</summary>
    internal readonly struct StepPhrasing
    {
        /// <summary>Loc id of the CONTROL NAME the body should say, or null when the body takes
        /// no argument at all.</summary>
        internal readonly string? ArgumentId;

        /// <summary>One clause naming what decided it, for the hardware log. Never null.</summary>
        internal readonly string Why;

        internal StepPhrasing(string? argumentId, string why)
        {
            ArgumentId = argumentId;
            Why = why;
        }
    }

    /// <summary>
    /// WHAT THE CARD CALLS THE CONTROL, RESOLVED AGAINST THE SETTINGS AS THEY ARE RIGHT NOW
    /// (user ruling 2026-09-03: the text must name the RIGHT stick instead of just "the stick",
    /// and must state the one case that is true now instead of writing out both eventualities
    /// like <c>(auf der linken Hand "x")</c> — his own example being <i>"Tipp kurz X"</i> for the
    /// menu when right-handed mode is on).
    ///
    /// <para>IT TAKES THE VERDICT RATHER THAN RE-READING THE DIALS, and that is the whole point.
    /// <see cref="Availability"/> already resolves which hand performs each step, from the one
    /// place that knows which dial gates which step; a second read here would be a second chance
    /// to disagree about which stick is which, which is the class of bug <c>LocalTurnControl</c>
    /// and <see cref="Availability"/> both exist to have ended. So the caller passes the verdict
    /// it is already holding and this only turns <see cref="LessonHands"/> into words. The prose
    /// and the lit key therefore cannot come apart: they are the same answer rendered twice.</para>
    ///
    /// <para>WHICH CARDS TAKE AN ARGUMENT, and what happens to the rest. Three name a STICK
    /// (fly, snap turn, panel reel — the three whose acting hand a dial can move) and three name
    /// a FACE BUTTON (ping, menu, recentre). Every other card either names its key in words that
    /// are the same on both hands (TRIGGER, GRIP) or genuinely uses both sticks at once — the
    /// two-handed zoom and rotate, and the one-stick drag, where EITHER stick drags and no
    /// setting can make it one-sided. Those bodies carry no <c>{0}</c> and get null, which is the
    /// resolved answer for them and not a hedge.</para>
    ///
    /// <para>WHAT CANNOT BE RESOLVED, AND WHAT IT FALLS BACK TO. Exactly one thing: while
    /// <c>[Comfort]</c> is unbound (before the config binds, and again after teardown)
    /// <see cref="Availability"/> fails open with <see cref="LessonHands.Both"/> for every step,
    /// because no dial can be read at all. A stick card then says <c>ctl_stick_either</c> and a
    /// face-button card falls back to <c>ctl_key_primary</c> — the old both-eventualities string,
    /// kept alive for precisely this case and reachable through no other path. Both are true
    /// statements about a lesson that has no settings to consult, and neither occurs in normal
    /// play, where the config binds long before a scenario starts. The recentre chord is the
    /// other permanent <see cref="LessonHands.Both"/>, and there B AND Y really are both pressed:
    /// naming both is the instruction, not a hedge, so it keeps its own fixed name.</para>
    /// </summary>
    internal static StepPhrasing Phrasing(in ControlsStep step, in StepAvailability verdict)
    {
        switch (step.Action)
        {
            // The three sticks a dial can move. The card says which one, and the same verdict
            // lights that same controller in ControlsTutorial.ApplyStep.
            case ControlAction.Fly:
            case ControlAction.SnapTurn:
            case ControlAction.PanelReel:
                if (verdict.Hands == LessonHands.Left)
                    return new StepPhrasing("ctl_stick_left", "resolved LEFT — " + verdict.Why);
                if (verdict.Hands == LessonHands.Right)
                    return new StepPhrasing("ctl_stick_right", "resolved RIGHT — " + verdict.Why);
                return new StepPhrasing("ctl_stick_either",
                    "resolved EITHER (both hands perform it, or the dials are unreadable) — "
                    + verdict.Why);
            default:
                break;
        }

        if (step.KeyNameId == null)
            return new StepPhrasing(null, "this card's body takes no control name");

        // The B+Y recentre chord: a two-hand chord names both keys because both are pressed.
        // Only the DEVICE variant is resolved for it.
        if (!string.Equals(step.KeyNameId, PrimaryKeyNameId, System.StringComparison.Ordinal))
            return new StepPhrasing(step.KeyNameId + DpadSuffix(),
                "a fixed key name for this card" + DpadWhy());

        // A/X on ONE named hand — the ping and the pause-menu tap.
        if (verdict.Hands == LessonHands.Left)
            return new StepPhrasing("ctl_key_a_left" + DpadSuffix(),
                "resolved to the LEFT controller's face button" + DpadWhy() + " — " + verdict.Why);
        if (verdict.Hands == LessonHands.Right)
            return new StepPhrasing("ctl_key_a_right" + DpadSuffix(),
                "resolved to the RIGHT controller's face button" + DpadWhy() + " — " + verdict.Why);
        // UNRESOLVABLE — see the doc: an unbound [Comfort] is the only way here. Fall back to the
        // both-eventualities name rather than guessing a hand, and say so in the log.
        return new StepPhrasing(PrimaryKeyNameId + DpadSuffix(),
            "COULD NOT be resolved to one hand, so the both-hands name is used" + DpadWhy()
            + " — " + verdict.Why);
    }

    /// <summary>The table's own both-eventualities name for the A/X face button. Named once so the
    /// table row, the fallback and the test for "is this the resolvable one?" cannot drift.</summary>
    internal const string PrimaryKeyNameId = "ctl_key_primary";

    private static string DpadSuffix() => ControllerVisual.HasDpad ? "_dpad" : string.Empty;

    private static string DpadWhy() =>
        ControllerVisual.HasDpad ? " (D-pad device)" : " (lettered face buttons)";

    /// <summary>The dominant controller. <c>VRHands.Primary</c> is null before the hands come up;
    /// Right is both the shipped <c>[Hands] PrimaryHand</c> and what <c>LocalTurnControl.Resolve</c>
    /// answers in the same situation, so the two agree instead of quietly naming different sticks
    /// for the frame before the hands arrive.</summary>
    private static HandSide DominantSide()
    {
        VRHand? primary = VRHands.Primary;
        return primary != null ? primary.Side : HandSide.Right;
    }

    private static HandSide Opposite(HandSide side)
    {
        return side == HandSide.Left ? HandSide.Right : HandSide.Left;
    }

    /// <summary>The board's apparent-width window as a RATIO, max over min — the same two dials
    /// and the same min + 2 cm floor as <c>PlayTray.MaxWidthMeters</c>/<c>MinWidthMeters</c>, read
    /// defensively for the same reason as <see cref="FingertipTouchOn"/>: <c>CardsConfig</c> binds
    /// on its own schedule. Fails OPEN to the shipped 18–140 cm span (×7.78).</summary>
    private static float BoardResizeSpan()
    {
        try
        {
            float min = Cards.CardsConfig.BoardMinWidthMeters != null
                ? Cards.CardsConfig.BoardMinWidthMeters.Value : 0.18f;
            float max = Cards.CardsConfig.BoardMaxWidthMeters != null
                ? Cards.CardsConfig.BoardMaxWidthMeters.Value : 1.4f;
            min = UnityEngine.Mathf.Max(min, 1e-3f);
            max = UnityEngine.Mathf.Max(max, min + 0.02f);
            return max / min;
        }
        catch (System.Exception)
        {
            return 1.4f / 0.18f;
        }
    }

    /// <summary>[Board] TouchTilesWithFingertip, read the way <c>HeldCardGrip.Enabled</c> reads its
    /// own switch: the entry is a <c>null!</c> field until BoardConfig binds, and the lesson can be
    /// armed on either side of that lifetime.</summary>
    private static bool FingertipTouchOn()
    {
        try
        {
            return Board.BoardConfig.TouchTilesWithFingertip != null
                   && Board.BoardConfig.TouchTilesWithFingertip.Value;
        }
        catch (System.Exception)
        {
            return true;   // fail OPEN, as above: a visible card beats a silently deleted one
        }
    }

    // Progress(in ControlsStep) lived here and fed the box's ASCII progress bar. The bar is gone
    // (user ruling 2026-09-02) and it had exactly one caller, so it went with it. Its body was a
    // pure read — step.Action, step.Target and ControlsProgress.Accumulated, clamped — and wrote
    // nothing, which is why deleting it is safe: the completion test below reads the same
    // Accumulated for itself and is untouched.

    /// <summary>Has the running step been satisfied?</summary>
    internal static bool IsComplete(in ControlsStep step)
    {
        if (step.Action == ControlAction.None)
            return false; // advanced by the panel's button, never by itself
        return ControlsProgress.Accumulated >= step.Target;
    }

    /// <summary>Poll-only checks, for the two states that leave no event behind. A card sitting
    /// in the hand is a STATE — the player can reach the step already holding one — and a state
    /// that was entered before the step began would never arrive as a notification.</summary>
    internal static void PollStates(in ControlsStep step)
    {
        switch (step.Action)
        {
            case ControlAction.CardTake:
                if (HoldsCard(VRHands.Left) || HoldsCard(VRHands.Right))
                    ControlsProgress.Notify(ControlAction.CardTake);
                break;
            case ControlAction.CardInHand:
                if (Cards.HeldCardGrip.InHand(HandSide.Left)
                    || Cards.HeldCardGrip.InHand(HandSide.Right))
                    ControlsProgress.Notify(ControlAction.CardInHand);
                break;
        }
    }

    private static bool HoldsCard(VRHand? hand) =>
        hand?.Grabber?.Held is Cards.VRCard or Cards.ItemsPile.ItemChip;
}
