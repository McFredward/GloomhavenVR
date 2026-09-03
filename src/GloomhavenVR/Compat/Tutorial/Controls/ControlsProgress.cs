using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>Every control the lesson can teach. One value per THING THE PLAYER DOES, not per
/// key — "drag the table" and "fly" are different lessons that happen to share a thumbstick,
/// and lighting the same key twice for two different motions is exactly what confuses a
/// newcomer.</summary>
internal enum ControlAction
{
    None = 0,
    WorldDrag,      // one stick clicked in, hand moves -> the table slides
    WorldRotate,    // both sticks clicked in, hands turn around each other
    WorldZoom,      // both sticks clicked in, hands apart/together
    Fly,            // push the movement stick
    SnapTurn,       // flick a stick sideways
    // NO STEP TEACHES THIS ANY MORE (user ruling 2026-09-03, verbatim: "Entferne den ersten Test
    // 'Zeigen und Auswählen' - wenn man es bis hier hin geschaft hat kennt man das schon - hat
    // keinen Mehrwert"). The reason it is safe as well as right: the lesson takes over the game's
    // own tutorial box, and that box only opens once the tutorial's opening story dialogue has been
    // DISMISSED — which the player does by pointing the laser at a button and pulling the trigger.
    // The card taught the control the player had just used to make the card appear.
    //
    // THE VALUE STAYS, and so does BoardClickDriver's Notify call site. Waiting is only ever set to
    // an action a step declares, so with no such step Notify's first line — `Waiting != action` —
    // returns immediately and the call site costs two static reads on the board's click path,
    // exactly as it did for every other never-running action. Deleting the value instead would mean
    // editing a file this lane does not own to remove a call that already does nothing.
    LaserClick,     // point the laser, pull the trigger
    FingertipPick,  // hold grip, touch the board with a fingertip
    ProximityGrab,  // reach out, squeeze the trigger
    CardTake,       // palm up, take a card out of the fan
    CardInHand,     // hold grip while taking -> the card sits IN the hand
    PanelReel,      // laser holds a window, stick reels it closer/further
    Ping,           // A/X marks a hex for the others
    Recenter,       // hold B+Y to re-seat yourself
    OpenMenu,       // short tap of the NON-dominant A/X -> the pause menu
    // The control board, taken by the GRIP at its bar and carried (user request 2026-09-03).
    // Reported by WorldUI.PanelGrabHandle for the PlayTray's handle only, in real metres of NET
    // palm displacement from where the bar was taken — never the laser carry, which is a trigger
    // gesture, and never another panel's bar. Appended, not inserted: nothing persists these
    // values, but a value that keeps its number keeps every log line that ever printed it true.
    BoardCarry,     // hold GRIP on the bar under the control board, move the hand -> the board follows
    // The control board, RESIZED by both GRIPs on its bar (user request 2026-09-03: "dass man
    // das Brett mit den GRIP-Tasten kleiner und größer skalieren kann"). Reported by the same
    // WorldUI.PanelGrabHandle, for the PlayTray's handle only, as the FRACTION the board's
    // localScale has moved from where it stood when the step's first two-hand pinch began —
    // |scale / scale0 - 1|, a rising maximum, so Accumulated IS that maximum. Appended, as above.
    BoardScale,     // both GRIPs on the bar, hands apart/together -> the board grows/shrinks
}

/// <summary>
/// THE CHECK CHANNEL for the controls lesson: the mod's own subsystems say what the player
/// just did, and the lesson decides whether that was the thing it asked for.
///
/// <para>WHY A CHANNEL AND NOT POLLING. Some of these are STATES the lesson could read for
/// itself — a card is in the hand or it is not — but most are EVENTS that leave no trace a
/// frame later: a snap turn happens and is over, a ping is posted and is gone. Polling can
/// only see the first kind, and a lesson that could verify half its own steps would have to
/// explain to the player why the other half never tick.</para>
///
/// <para>WHY IT IS SEPARATE FROM <see cref="TutorialVR.NotifyLocomotion"/>, which the same four
/// call sites already feed. That one exists to break the tutorial's flat-camera deadlock and is
/// load-bearing: it posts a real UIEvent into the game's scripted chain. It also deliberately
/// CONFLATES what this must separate — world-drag and flight both report metres there, because
/// for its purpose any locomotion counts. Widening it would put a lesson's needs inside a
/// deadlock-breaker, so this is its own two-field channel instead.</para>
///
/// <para>COLD PATH IS TWO STATIC READS. <see cref="Notify"/> returns immediately unless a lesson
/// is actually waiting for something, so the call sites — several of which run every frame during
/// a drag — cost nothing in the 99.9 % of play where no lesson is running.</para>
/// </summary>
internal static class ControlsProgress
{
    /// <summary>What the running step is waiting for, or <see cref="ControlAction.None"/>.
    /// Written only by the lesson driver.</summary>
    internal static ControlAction Waiting { get; set; }

    /// <summary>How much of it has been done since the step started. Units are the action's
    /// own: metres for drag/fly, degrees for rotate/turn, log2 octaves for zoom, and 1 per
    /// event for the discrete ones.</summary>
    internal static float Accumulated { get; private set; }

    /// <summary>
    /// How long the GRIP has been held on the control board's bar during the current carry,
    /// seconds — written per frame by the reporter of <see cref="ControlAction.BoardCarry"/>
    /// (<c>WorldUI.PanelGrabHandle</c>) while that step waits, read once by the lesson's
    /// completion line. It is a second quantity the one-number channel cannot carry, and the
    /// hardware line for that step is required to state both: the metres carried AND the hold.
    /// </summary>
    internal static float BoardCarryGripSeconds { get; set; }

    /// <summary>
    /// The board's localScale.x the moment the FIRST two-hand pinch of the current
    /// <see cref="ControlAction.BoardScale"/> step began, or 0 while none has. Owned here rather
    /// than in the reporter because it is a fact about the STEP, not about one pinch: a player who
    /// grows the board 10 %, lets go, and grows it 10 % more has changed it 21 %, and the second
    /// pinch must measure against the same origin as the first. Zeroed with the channel.
    /// </summary>
    internal static float BoardScaleStart { get; set; }

    /// <summary>The board's localScale.x as last seen by the reporter of
    /// <see cref="ControlAction.BoardScale"/> — the END value the completion line prints.</summary>
    internal static float BoardScaleLive { get; set; }

    /// <summary>Seconds BOTH grips have been held on the board's bar during the current
    /// <see cref="ControlAction.BoardScale"/> step, summed across pinches. The second quantity
    /// that step's hardware line states beside the size change.</summary>
    internal static float BoardScalePinchSeconds { get; set; }

    private static bool _disabledByError;

    internal static void BeginWaiting(ControlAction action)
    {
        Waiting = action;
        Accumulated = 0f;
        BoardCarryGripSeconds = 0f;
        BoardScaleStart = 0f;
        BoardScaleLive = 0f;
        BoardScalePinchSeconds = 0f;
    }

    internal static void StopWaiting() => BeginWaiting(ControlAction.None);

    /// <summary>
    /// Report that the player did <paramref name="action"/>. <paramref name="amount"/> is in the
    /// action's own units (see <see cref="Accumulated"/>); discrete actions pass 1.
    /// </summary>
    /// <remarks>There is deliberately no "and with the other hand too" bookkeeping. The three
    /// controls that need both hands — rotate, zoom and the B+Y recentre chord — are two-handed
    /// BY CONSTRUCTION: the two-hand world grab does not begin until both sticks are clicked in,
    /// and a chord is not a chord with one button. A second check would only be able to agree.
    /// </remarks>
    internal static void Notify(ControlAction action, float amount = 1f)
    {
        // Cold path: two static reads. This runs inside per-frame drag and turn code.
        if (_disabledByError || Waiting != action || action == ControlAction.None)
            return;
        try
        {
            if (!(amount > 0f) || float.IsNaN(amount))
                return;
            Accumulated += amount;
        }
        catch (Exception ex)
        {
            // TickGuard spirit: a lesson's bookkeeping must never take world-grab, flight or
            // the board's click path down with it. Disarm for the session, log once with stack.
            _disabledByError = true;
            Waiting = ControlAction.None;
            VRLog.Error("Tutorial", "The controls lesson's progress channel threw and is "
                + $"disabled for this session: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    internal static void Shutdown()
    {
        Waiting = ControlAction.None;
        Accumulated = 0f;
    }
}
