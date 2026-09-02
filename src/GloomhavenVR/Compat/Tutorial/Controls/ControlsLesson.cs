using GloomhavenVR.Hands;

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
/// <para>ORDER. Point and click first, because that is how the player answers the panel in front
/// of them. Then reach and grab, which is the one thing a flat-screen player has no instinct for.
/// Then ALL FOUR WAYS OF GETTING ABOUT, TOGETHER: drag the table, fly, turn, then zoom and rotate.
/// The one-handed three come first and the two-handed pair after (user, 2026-08-29) — moving
/// yourself is what a newcomer reaches for in the first minute, and it needs the same single stick
/// the drag just taught. Postponing it behind the two-handed gestures, as the first version did,
/// split the stick's lesson in half and put the harder grip in the middle of it. Then the cards,
/// which are the game. Everything after is a convenience.</para>
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
        // CONTROLLER: the trigger is the first key the lesson names, and a player who cannot find
        // it cannot do anything else in the game.
        new(ControlAction.LaserClick, "ctl_laser", ControllerKey.Trigger, showsController: true),
        // CONTROLLER: the new fact here is PROXIMITY, and reaching moves the whole device, not the
        // fingers — a controller in the same place reaches identically. The trigger is named again
        // for a second purpose, so it stays lit while that second purpose is learnt.
        new(ControlAction.ProximityGrab, "ctl_grab", ControllerKey.Trigger, showsController: true),

        // GETTING ABOUT, ALL FOUR TOGETHER AND ONE-HANDED FIRST (user, 2026-08-29: fly and turn
        // belong straight after the drag). Drag moves the TABLE, fly and turn move YOU, and those
        // three are what a player reaches for in the first minute — each needs one hand and one
        // stick. The two-handed zoom and rotate come after, because they are a different gesture
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
