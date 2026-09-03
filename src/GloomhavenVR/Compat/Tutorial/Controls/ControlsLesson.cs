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

    /// <summary>Key to light on both controllers, or null.</summary>
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

    /// <summary>
    /// THIS STEP ASKS FOR, OR DEMONSTRATES, A KEY (user ruling 2026-09-02: <i>"Die 3D-meshes der
    /// Controller sollen NUR dann angezeigt werden wenn eine Aufgabe des Tutorials gerade etwas
    /// verlangt oder zeigt das man etwas drücken muss mit den entsprechenden Highlights."</i>).
    ///
    /// <para>It is DECLARED per step rather than derived from <see cref="Key"/> being non-null,
    /// and 2026-09-02's second ruling is what that declaration was for: <i>"Im Tutorial entscheide
    /// für jede Aufgabe ob man controller oder Hände sehen sollte. zB 'drehe die Handfläche zu
    /// dir' sollte man auch die Hand sehen und nicht die Controller."</i> 343 set it true for all
    /// fourteen teaching steps, which was too coarse — several of them teach a POSE, and a
    /// controller model cannot show a palm turning over or a fingertip touching a hex.</para>
    ///
    /// <para>THE RULE THE TABLE IS DECIDED BY, written down so the next row can be judged the same
    /// way: show the CONTROLLER when the step turns on FINDING A KEY the player might not find;
    /// show the HAND when it turns on the SHAPE OR ORIENTATION OF THE HAND ITSELF. Three rows come
    /// out as hand — take a card (a palm turns towards you), hold a card (it sits between thumb and
    /// finger), pick with a fingertip (a controller has no fingertip) — and those three name their
    /// key in words instead. Each row carries its own reason as a comment.</para>
    ///
    /// <para>So <see cref="Key"/> and this flag now DISAGREE on purpose for three rows, and
    /// <see cref="ControlsTutorial"/> no longer warns about the disagreement — it prints the whole
    /// resolved table once instead, so a hardware log says what the headset was told to show.</para>
    /// </summary>
    internal readonly bool ShowsController;

    internal ControlsStep(ControlAction action, string id, string? key, bool showsController,
                          float target = 1f, string? keyNameId = null)
    {
        Action = action;
        Id = id;
        Key = key;
        ShowsController = showsController;
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
/// <para>ORDER. Reach and grab first, which is the one thing a flat-screen player has no instinct
/// for. (Point-and-click used to come before it and was removed on 2026-09-03 — see the gap in the
/// table.) Then ALL FOUR WAYS OF GETTING ABOUT, TOGETHER: pull yourself along, fly, turn, then zoom
/// and rotate. The one-handed three come first and the two-handed pair after (user, 2026-08-29) —
/// moving yourself is what a newcomer reaches for in the first minute, and it needs the same single
/// stick the drag just taught. Postponing it behind the two-handed gestures, as the first version
/// did, split the stick's lesson in half and put the harder grip in the middle of it. Then the
/// cards, which are the game. Everything after is a convenience.</para>
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
        // HAND: a prose card that asks for nothing. It names the device in words, which is why
        // ControllerVisual.EnsureResolved runs before any model is ever shown.
        new(ControlAction.None, "ctl_welcome", null, showsController: false),

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
        // CONTROLLER: the new fact here is PROXIMITY, and reaching moves the whole device, not the
        // fingers — a controller in the same place reaches identically. The trigger is named again
        // for a second purpose, so it stays lit while that second purpose is learnt.
        new(ControlAction.ProximityGrab, "ctl_grab", ControllerKey.Trigger, showsController: true),

        // GETTING ABOUT, ALL FOUR TOGETHER AND ONE-HANDED FIRST (user, 2026-08-29: fly and turn
        // belong straight after the drag). All four move YOU — the one-stick drag included, which
        // is the 2026-09-03 correction: WorldGrab applies its motion inversely to the RIG ROOT, so
        // the table only appears to slide. They are what a player reaches for in the first minute —
        // each needs one hand and one stick. The two-handed zoom and rotate come after, because they are a different gesture
        // (both sticks clicked in at once) and asking for it before the one-handed stick is
        // understood is what makes the stick feel like five unrelated controls.
        new(ControlAction.WorldDrag, "ctl_drag", ControllerKey.Thumbstick, showsController: true,
            target: 0.25f),
        new(ControlAction.Fly, "ctl_fly", ControllerKey.Thumbstick, showsController: true,
            target: 0.8f),
        new(ControlAction.SnapTurn, "ctl_turn", ControllerKey.Thumbstick, showsController: true,
            target: 20f),
        new(ControlAction.WorldZoom, "ctl_zoom", ControllerKey.Thumbstick, showsController: true,
            target: 0.35f),
        new(ControlAction.WorldRotate, "ctl_rotate", ControllerKey.Thumbstick, showsController: true,
            target: 25f),

        // --- the game ---------------------------------------------------------------------
        // These two, and the three conveniences below, need the room to be in a particular state —
        // a card in the fan, a window open to reel. That used to set a `Situational` flag that
        // relabelled the box's second button; the button is gone and so is the flag, and the
        // player steps past any of them with the same ÜBERSPRINGEN as every other card.
        // HAND, and this is the row the user pointed at: "Dreh eine Handfläche zu dir" is an
        // ORIENTATION OF THE HAND, and a controller model cannot show a palm turning over.
        new(ControlAction.CardTake, "ctl_card_take", ControllerKey.Trigger, showsController: false),
        // HAND: the taught thing is a card sitting BETWEEN THUMB AND FINGER and being turned round.
        // The grip is named in words instead — and on two of the three shipped models it could not
        // have been lit anyway (the Index's grip is a force sensor with no mesh, the generic model
        // has no separate grip part).
        new(ControlAction.CardInHand, "ctl_card_hold", ControllerKey.Squeeze, showsController: false),

        // --- conveniences -----------------------------------------------------------------
        // HAND, necessarily: the instruction is "touch the board with a FINGERTIP", and a
        // controller has no fingertip to touch it with. Showing one here would contradict the card.
        new(ControlAction.FingertipPick, "ctl_fingertip", ControllerKey.Squeeze,
            showsController: false),
        new(ControlAction.PanelReel, "ctl_reel", ControllerKey.Thumbstick, showsController: true),
        new(ControlAction.Ping, "ctl_ping", ControllerKey.Primary, showsController: true,
            keyNameId: "ctl_key_primary"),
        new(ControlAction.Recenter, "ctl_recenter", ControllerKey.Secondary, showsController: true,
            keyNameId: "ctl_key_secondary"),
        // LAST, and on purpose: it is the card that hands the player everything else. The closing
        // card then points at the settings they have just seen how to reach. CONTROLLER: it is a
        // face button on a named hand, and getting the wrong hand is the whole failure mode.
        new(ControlAction.OpenMenu, "ctl_menu", ControllerKey.Primary, showsController: true,
            keyNameId: "ctl_key_primary"),

        // HAND: prose again, and the lesson hands the player back their own hands as it ends.
        new(ControlAction.None, "ctl_done", null, showsController: false),
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

            default:
                // ProximityGrab, CardTake, LaserClick — no config entry gates any of them and all
                // three work on either hand. LaserClick no longer has a step (2026-09-03) but the
                // action still exists, so it is answered here rather than left to an accident.
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
