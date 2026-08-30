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

    /// <summary>The step depends on something the room may not be offering right now (an open
    /// window to reel, a card in hand). It is still shown and still checked — it simply must not
    /// be the thing that strands a player, so the panel offers "skip" from the first frame
    /// instead of after a delay.</summary>
    internal readonly bool Situational;

    internal ControlsStep(ControlAction action, string id, string? key, float target = 1f,
                          bool situational = false, string? keyNameId = null)
    {
        Action = action;
        Id = id;
        Key = key;
        Target = target;
        Situational = situational;
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
/// everything after that is a convenience the player can walk away from at any point via SKIP.</para>
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
        new(ControlAction.None, "ctl_welcome", null),

        // --- what you cannot play without -------------------------------------------------
        new(ControlAction.LaserClick, "ctl_laser", ControllerKey.Trigger),
        new(ControlAction.ProximityGrab, "ctl_grab", ControllerKey.Trigger),

        // GETTING ABOUT, ALL FOUR TOGETHER AND ONE-HANDED FIRST (user, 2026-08-29: fly and turn
        // belong straight after the drag). Drag moves the TABLE, fly and turn move YOU, and those
        // three are what a player reaches for in the first minute — each needs one hand and one
        // stick. The two-handed zoom and rotate come after, because they are a different gesture
        // (both sticks clicked in at once) and asking for it before the one-handed stick is
        // understood is what makes the stick feel like five unrelated controls.
        new(ControlAction.WorldDrag, "ctl_drag", ControllerKey.Thumbstick, target: 0.25f),
        new(ControlAction.Fly, "ctl_fly", ControllerKey.Thumbstick, target: 0.8f),
        new(ControlAction.SnapTurn, "ctl_turn", ControllerKey.Thumbstick, target: 20f),
        new(ControlAction.WorldZoom, "ctl_zoom", ControllerKey.Thumbstick, target: 0.35f),
        new(ControlAction.WorldRotate, "ctl_rotate", ControllerKey.Thumbstick, target: 25f),

        // --- the game ---------------------------------------------------------------------
        new(ControlAction.CardTake, "ctl_card_take", ControllerKey.Trigger, situational: true),
        new(ControlAction.CardInHand, "ctl_card_hold", ControllerKey.Squeeze, situational: true),

        // --- conveniences -----------------------------------------------------------------
        new(ControlAction.FingertipPick, "ctl_fingertip", ControllerKey.Squeeze, situational: true),
        new(ControlAction.PanelReel, "ctl_reel", ControllerKey.Thumbstick, situational: true),
        new(ControlAction.Ping, "ctl_ping", ControllerKey.Primary, situational: true,
            keyNameId: "ctl_key_primary"),
        new(ControlAction.Recenter, "ctl_recenter", ControllerKey.Secondary,
            keyNameId: "ctl_key_secondary"),
        // LAST, and on purpose: it is the card that hands the player everything else. The closing
        // card then points at the settings they have just seen how to reach.
        new(ControlAction.OpenMenu, "ctl_menu", ControllerKey.Primary,
            keyNameId: "ctl_key_primary"),

        new(ControlAction.None, "ctl_done", null),
    };

    /// <summary>Progress of the running step, 0..1, for the panel's bar. A discrete step is
    /// either not done or done; an analog one fills as the player moves.</summary>
    internal static float Progress(in ControlsStep step)
    {
        if (step.Action == ControlAction.None)
            return 1f;
        return step.Target <= 0f ? 1f
            : UnityEngine.Mathf.Clamp01(ControlsProgress.Accumulated / step.Target);
    }

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
