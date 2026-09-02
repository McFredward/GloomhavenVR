using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE CONTROLS PHASE of the first tutorial: the game's own tutorial box teaches the VR controls
/// before it teaches Gloomhaven, one key at a time, and each step ends when the player DOES the
/// thing rather than when they read about it.
///
/// <para>AND THE CARD SAYS WHETHER IT HAS (user, 2026-09-03: <i>"Beim Tutorial sollte schon
/// angezeigt werden irgendwie ob es erfüllt wurde oder nicht"</i>). Until now a step that had not
/// been done said NOTHING about itself, and a step that had been done added a small "Erledigt."
/// for 1.1 s — so the answer to "have I done this yet?" was absent in the state the player is
/// actually in most of the time. Every teaching card now carries a <see cref="ControlsStepState"/>
/// word under its instruction from the moment it appears. The state is decided in exactly three
/// places — <see cref="PrepareStep"/> when the card opens, the completion branch of
/// <see cref="TickCore"/>, and <see cref="OnAction"/> for a skip — and <see cref="RefreshBox"/>
/// only ever renders what those three set; it never re-derives it, because a second read of the
/// completion predicate cannot tell "you just did it" from "it was already true when you got
/// here", and the player is owed the difference.</para>
///
/// <para>IT IS NOT THE PROGRESS DOTS COMING BACK. Those were removed on 2026-09-02 (<i>"Diese
/// komischen Punkte die den Fortschitt anzeigen soll auch weg"</i>) and they measured the LESSON:
/// an ASCII bar that filled as the step's accumulator rose, plus an "8/14" counter. This is a
/// BINARY state of ONE CARD, it holds one of four words, and nothing in the box knows or shows how
/// many steps there are or which one this is. See <see cref="ControlsBox.SetStep"/>.</para>
///
/// <para>IT IS PART OF THE TUTORIAL NOW, NOT BESIDE IT (user ruling 2026-09-02, verbatim: <i>"ich
/// will dass die neuen Aufgaben und der neue Text IN das standart Tutorial integriert wird … es
/// soll das standart Fenster sein das nach dem Dialog kommt und durch das Tutorial führt"</i>).
/// The previous version built a panel of its own and ran ALONGSIDE the scripted chain, on the
/// argument that a step ending when the player turns the table has no trigger to offer. The
/// argument was right about the mechanism and wrong about the conclusion; what it costs to do it
/// properly is written out in <see cref="ControlsBox"/>.</para>
///
/// <para>WHERE IT SLOTS IN, taken from the game's own queue rather than from a name. The tutorial's
/// first message is a <c>StoryDialog</c> (<c>TB_1</c>) and the first box that "leads through the
/// tutorial" is displayed by that dialogue's dismissal (<c>TB_2_1</c>, display trigger
/// <c>LevelMessageDismissed ctxId='TB_1'</c>). So the lesson arms at scenario start, WAITS for the
/// dialogue to be dismissed, and then takes the box for itself. Any tutorial with no
/// <c>StoryDialog</c> queued at all starts after <see cref="StartDelaySeconds"/> instead — the
/// wait is on a message that exists, never on one that might.</para>
///
/// <para>WHAT HAPPENS TO THE SCRIPTED CHAIN, and it is the only thing this class does to the game.
/// From the moment the dialogue closes until the lesson ends, <see cref="TutorialChainHold"/>
/// withholds every scripted level message at <c>LevelEventsController.ShowLevelMessage</c> and
/// re-issues them, in order, through the game's own entry point on release. Nothing is dropped and
/// no trigger is consumed, delayed or re-evaluated by us: the controller's own bookkeeping runs
/// identically whether we hold or not (see that class). In the tutorial exactly one message is
/// withheld, <c>TB_2_1</c> — so when the lesson ends, the tutorial resumes at the very window it
/// would have opened, which is what "integrated into the tutorial" means here.</para>
///
/// <para>THE COST OF THE HOLD, stated plainly: while the lesson runs the scripted tutorial does not
/// advance. That is deliberate — the alternative is two windows telling the player two things at
/// once, which is the defect hardware round 2 already found — but it means the hold needs an
/// anti-brick backstop, and it has three: the lesson's own ceiling
/// (<see cref="MaxLessonSeconds"/>), the scenario boundary (<see cref="Reset"/>), and the error
/// latch. Every one of them releases the hold.</para>
///
/// <para>A SECOND COST, WRITTEN DOWN BECAUSE IT WAS ALMOST BLAMED FOR SOMETHING ELSE. The game
/// swaps its <c>InteractabilityManager</c> profile ONLY from
/// <c>LevelEventsController.MessageWasDisplayed</c> and <c>MessageWasDismissed</c>
/// (LevelEventsController.cs:949-958 / 983-990). A withheld message reaches neither, so whatever
/// profile the last DISPLAYED message loaded stays loaded for as long as the hold lasts — in the
/// tutorial that is the messageless profile the opening dialogue's dismissal installed, and
/// <c>InteractabilityManager.ShouldTryPreventControl()</c> stays armed on it. The hold does not
/// CREATE that veto (vanilla is under it between any two messages) but it does extend the window,
/// up to <see cref="MaxLessonSeconds"/>. When the 2026-09-02 main-menu deadlock was investigated
/// this was the leading suspect and the log ruled it out: the lesson had ended and released the
/// chain 700 log lines and three scripted boxes earlier. Anyone reaching for it again should check
/// the same thing first — the release line names itself
/// ("VR step HOLD RELEASED"), and the cause that round was
/// <c>UIMenuOption.Select()</c>'s <c>if (isSelected) return;</c> latch, not this.</para>
///
/// <para>WHAT IT TOUCHES OTHERWISE is entirely the mod's own: two controller models in place of the
/// hand meshes — and, since the 2026-09-02 ruling, ONLY while a step actually asks for or shows a
/// key press — and a read of the mod's own input. Ending it puts every renderer back by
/// identity.</para>
///
/// <para>SINGLE-PLAYER BY CONSTRUCTION. <see cref="TutorialVR.IsTutorialActive"/> refuses while
/// <c>FFSNetwork.IsOnline</c>, and tutorials are single-player game modes to begin with, so nothing
/// here goes on the wire and no peer can see a hand that has turned into a controller. This is not
/// a feature that "needs sync designed in": there is no second player in the context it runs in,
/// and the one event our window's dismissal produces (<c>LevelMessageDismissed</c>) never leaves
/// the local <c>LevelEventsController</c> — <c>UIEventManager.OnEventLogged</c> has exactly one
/// subscriber.</para>
/// </summary>
internal static class ControlsTutorial
{
    /// <summary>Chain-hold owner token — see <see cref="TutorialChainHold.Engage"/>. Two owners
    /// can never overlap in practice (the lesson holds the chain from the dialogue's dismissal, so
    /// the chain cannot reach the step <see cref="TutorialGrabStep"/> attaches to), and the token
    /// turns that from a hope into a checked invariant.</summary>
    internal const string HoldOwner = "controls-lesson";

    /// <summary>How long after the scenario starts the lesson may begin when there is no dialogue
    /// to wait for. The scene is still settling at frame zero — hands, bundle and board all arrive
    /// over the first second.</summary>
    private const float StartDelaySeconds = 2.5f;

    /// <summary>Settle time between the dialogue's dismissal and our show. The dismissal posts a
    /// UIEvent the controller processes on a later frame; opening our box into that handover would
    /// race the handler's own bookkeeping. The chain is already held at this point, so the wait
    /// costs the player nothing.</summary>
    private const float ArmSettleSeconds = 0.75f;

    /// <summary>
    /// How long a step that the player JUST SATISFIED stays on screen showing ERFÜLLT before the
    /// next card replaces it.
    ///
    /// <para>WHY THERE IS A HOLD AT ALL, AND WHY IT GOT LONGER THAN 343's 1.1 s. The completing
    /// motion is performed with the eyes on the WORLD, not on the box — a snap turn, a drag, a
    /// card coming out of the fan — so the gaze has to travel back to a window in the lower right
    /// before the state word can be read at all. 1.1 s was set when the card answered with a
    /// progress bar the player was already watching fill; with the bar gone the answer exists only
    /// in the box, and a tick that is replaced before the eye arrives is not feedback. This is a
    /// saccade plus one short word, and it is still short enough that a run of quick steps does
    /// not feel gated.</para>
    /// </summary>
    private const float CompletedDwellSeconds = 1.6f;

    /// <summary>
    /// The dwell for a card that was ALREADY satisfied the moment it appeared — the player reached
    /// "hold a card" still holding one. It is longer than <see cref="CompletedDwellSeconds"/> for
    /// the obvious reason: nothing was done to earn it, so the player has not looked up, has not
    /// read the card, and would otherwise see one frame of a control being taught and then the
    /// next card. This is a read of one short instruction from a standing start.
    /// </summary>
    private const float PreSatisfiedDwellSeconds = 2.6f;

    /// <summary>
    /// The dwell for a card the player SKIPPED without satisfying it.
    ///
    /// <para>SHORT ON PURPOSE, AND INTERRUPTIBLE. The welcome card tells the player that pressing
    /// the button through to the end is how they leave the lesson, so this dwell sits on the exit
    /// path: sixteen cards at a full dwell each would turn "let me out" into a quarter of a minute
    /// of enforced waiting. So it is brief, and <see cref="OnAction"/> treats a second press during
    /// it as "yes, I meant it" and advances at once — somebody deliberately leaving is never
    /// slowed, and somebody who pressed once still sees that the card was NOT ticked off.</para>
    /// </summary>
    private const float SkippedDwellSeconds = 0.7f;

    /// <summary>How long the lesson may wait for the tutorial's dialogue to be dismissed before it
    /// gives up on running at all. Generous: a player may read the opening text slowly or take the
    /// headset off. Giving up is silent-ish (one Note) and leaves the tutorial bit-for-bit
    /// vanilla — starting mid-flow instead would be the worse failure.</summary>
    private const float DialogWaitCeilingSeconds = 600f;

    /// <summary>Once the chain is held, how long the lesson has to actually get its box open
    /// before the whole thing is abandoned and the chain handed back. Reaching it means the hands
    /// or the message handler never came up; the tutorial must not be stuck behind that.</summary>
    private const float OpenCeilingSeconds = 30f;

    /// <summary>
    /// THE ABSOLUTE CEILING for the whole lesson (box open → done), and the only time-based exit.
    /// It is not a display timeout: while the lesson runs the scripted chain is WAITING on it, so
    /// this is the anti-brick backstop for the hold. It must never expire on a player who is
    /// simply slow — sixteen steps, read at leisure, with a headset break in the middle — and it
    /// must still bound a wedged state to one sitting. Twenty minutes satisfies both. It logs a
    /// loud <c>Warn</c> when it fires precisely because reaching it always means something else
    /// broke.
    /// </summary>
    private const float MaxLessonSeconds = 1200f;

    private enum Phase
    {
        Idle = 0,
        WaitingForDialog,   // armed; the tutorial's StoryDialog has not been dismissed yet
        Opening,            // chain held, waiting for the hands and a free box window
        Running,            // the box is open on a step
    }

    private static Phase _phase;
    private static ControllerVisual? _left, _right;
    private static int _index = -1;
    private static float _openAt;        // earliest allowed open time
    private static float _armedAt;       // when the lesson was queued for this scenario
    private static float _heldAt;        // when the chain hold was engaged
    private static float _runningAt;     // when the box actually opened
    /// <summary>True while the card is HOLDING on a settled state. A separate flag rather than
    /// a positive-time sentinel: <c>Time.unscaledTime</c> is a real clock and a "> 0" test on it
    /// is a hidden assumption about when the lesson can start.</summary>
    private static bool _dwelling;
    private static float _dwellSince;    // when the hold began
    private static float _dwellSeconds;  // how long that hold lasts
    private static ControlsStepState _state;
    /// <summary>Was the running step ALREADY satisfied on the frame it was applied? Decided once,
    /// at <see cref="ApplyStep"/>, because it is a claim about the moment the card appeared and a
    /// later read of the same predicate cannot tell the two apart.</summary>
    private static bool _preSatisfied;
    private static float _windowClosedSince = -1f;
    private static string? _openingDialog;   // the story dialogue whose dismissal is our slot
    private static bool _controllersUp;
    private static bool _disabledByError;

    internal static bool IsRunning => _phase == Phase.Running && ControlsBox.IsShowing;

    /// <summary>[Compat] ControlsLesson, plus the error latch.</summary>
    internal static bool Enabled =>
        !_disabledByError && (Plugin.ControlsLesson?.Value ?? true);

    /// <summary>
    /// Ask for the lesson at the start of a tutorial scenario. Deliberately only a REQUEST: the
    /// hands, the asset bundle and the message handler all come up over the first second or so,
    /// and the lesson's slot is after the tutorial's opening dialogue, which the player closes
    /// when they are ready.
    /// </summary>
    /// <param name="openingDialogName">The MessageName of the FIRST <c>StoryDialog</c> in the
    /// level's own message queue, or null when the level queues none. Read from the controller's
    /// own <c>m_MessagesToShow</c> by the caller, in queue order, because a level can carry more
    /// than one story dialogue — the tutorial has two, the opening <c>TB_1</c> and the closing
    /// <c>TB_25</c> (flow dump entries [0] and [59]) — and the lesson's slot is after the first
    /// one only. Waiting for a dialogue this level never queues is how a lesson silently never
    /// runs, so a null starts it on the plain delay instead.</param>
    internal static void RequestForTutorial(string? openingDialogName)
    {
        if (!Enabled || _phase != Phase.Idle)
            return;
        _armedAt = Time.unscaledTime;
        _openAt = _armedAt + StartDelaySeconds;
        _openingDialog = openingDialogName;
        _phase = string.IsNullOrEmpty(openingDialogName) ? Phase.Opening : Phase.WaitingForDialog;
        if (_phase == Phase.Opening)
            EngageHold("this tutorial queues no story dialogue, so the lesson takes the tutorial "
                + "box straight away");
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", "Controls lesson queued for this tutorial scenario — it will run "
            + (_phase == Phase.WaitingForDialog
                ? $"IN THE GAME'S OWN TUTORIAL BOX, starting when the opening story dialogue "
                  + $"'{openingDialogName}' is dismissed (that dismissal is the display trigger "
                  + "of the first box the tutorial itself would open). "
                : $"IN THE GAME'S OWN TUTORIAL BOX in {StartDelaySeconds:0.0} s (this level "
                  + "queues no story dialogue to wait for). ")
            + "Switch it off permanently with [Compat] ControlsLesson = false.");
    }

    /// <summary>
    /// A scripted message was dismissed. The only one this class cares about is the tutorial's
    /// opening <c>StoryDialog</c>: that dismissal is the display trigger of the first box that
    /// leads through the tutorial, so it is exactly the moment the lesson takes that box over.
    /// The hold is engaged HERE, inside <c>MessageWasDismissed</c>, so the follow-up message the
    /// same dismissal triggers is caught rather than raced.
    /// </summary>
    internal static void NoteMessageDismissed(CLevelMessage? messageDismissed)
    {
        if (_disabledByError || _phase != Phase.WaitingForDialog || messageDismissed == null)
            return;
        if (!string.Equals(messageDismissed.MessageName, _openingDialog, StringComparison.Ordinal))
            return;
        _phase = Phase.Opening;
        _openAt = Time.unscaledTime + ArmSettleSeconds;
        EngageHold($"the tutorial's opening dialogue ('{messageDismissed.MessageName}') was "
            + "dismissed, so the controls lesson now owns the tutorial box");
    }

    internal static void Tick()
    {
        if (_disabledByError || _phase == Phase.Idle)
            return;
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            _disabledByError = true;
            VRLog.Error("Tutorial", "The controls lesson threw and is disabled for this session "
                + $"(the hands are restored and the tutorial chain is handed back): "
                + $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            try { Stop("the lesson threw"); }
            catch (Exception) { TutorialChainHold.Release(HoldOwner, "the lesson threw during its own teardown"); }
        }
    }

    private static void TickCore()
    {
        float now = Time.unscaledTime;

        if (_phase == Phase.WaitingForDialog)
        {
            if (now - _armedAt < DialogWaitCeilingSeconds)
                return;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Tutorial", "Controls lesson ABANDONED: the tutorial's opening story "
                + $"dialogue was still not dismissed after {DialogWaitCeilingSeconds:0} s. The "
                + "tutorial is left exactly as the game wrote it; nothing was held back.");
            Stop("the dialogue was never dismissed");
            return;
        }

        if (_phase == Phase.Opening)
        {
            if (now - _heldAt >= OpenCeilingSeconds)
            {
                VRLog.Warn("Tutorial", "Controls lesson could not open the tutorial box within "
                    + $"{OpenCeilingSeconds:0} s of taking the chain (hands up: "
                    + $"{VRHands.Left != null && VRHands.Right != null}, message handler: "
                    + $"{LevelMessagesUIHandler.s_Instance != null}). Handing the scripted chain "
                    + "back so the tutorial can continue without the lesson.");
                Stop("the box could never be opened");
                return;
            }
            if (now < _openAt)
                return;
            if (VRHands.Left == null || VRHands.Right == null)
                return;   // hands not up yet; try again next frame
            Begin();
            return;
        }

        // ---- running -------------------------------------------------------------------------
        if (!ControlsBox.IsShowing)
        {
            // The window is no longer ours. Unreachable while the hold is engaged (every scripted
            // message goes through the held funnel), kept because a lesson that has lost its
            // window must not keep the chain waiting on it.
            Stop("the tutorial box is no longer ours");
            return;
        }
        if (ControlsBox.ContentDeadlinePassed)
        {
            VRLog.Error("Tutorial", "Controls lesson: the tutorial box opened but its page text "
                + "was never handed to us — that is an EMPTY window, which this project forbids. "
                + "Closing it and handing the scripted chain back. Check whether "
                + "LevelMessagePageUI.OnLanguageChanged is still the single text funnel.");
            Stop("the box would have been empty");
            return;
        }
        if (WindowHiddenTooLong(now))
        {
            VRLog.Warn("Tutorial", "Controls lesson: the game's box WINDOW has been hidden for "
                + $"{WindowGoneSecondsToStop:0.0} s while the handler still reports our message as "
                + "displayed — something else closed the window under us (an escape action is the "
                + "candidate). Ending the lesson so it cannot run invisibly, and handing the "
                + "scripted chain back.");
            Stop("the box window was closed under us");
            return;
        }
        if (now - _runningAt >= MaxLessonSeconds)
        {
            VRLog.Warn("Tutorial", $"Controls lesson: still running after {MaxLessonSeconds:0} s. "
                + "That should be impossible for a player who is simply taking their time, so "
                + "treat it as a symptom — check whether the box's button was built into "
                + "the box at all (a Warn above says so if it was not). Handing the scripted "
                + "chain back now so the run can continue.");
            Stop("the absolute ceiling was reached (anti-brick backstop)");
            return;
        }

        ControlsBox.Tick();
        _left?.Tick();
        _right?.Tick();

        if (_index < 0 || _index >= ControlsLesson.Steps.Length)
            return;
        ref readonly ControlsStep step = ref ControlsLesson.Steps[_index];

        // THE CARD IS SETTLED AND HOLDING. Nothing is polled and nothing can change the state
        // under it — ControlsProgress has already been stopped — so the word the player is reading
        // cannot flip while they read it.
        if (_dwelling)
        {
            if (now - _dwellSince >= _dwellSeconds)
                Advance();
            return;
        }

        // A state-shaped step can already be satisfied when it opens (the player may reach the
        // card step holding a card); an event-shaped one cannot, and polling it would be noise.
        ControlsLesson.PollStates(in step);

        if (ControlsLesson.IsComplete(in step))
        {
            // A card the player walked into already satisfied says so, and does NOT buzz: a haptic
            // pulse is the reward for having done something, and they have not.
            if (_preSatisfied)
            {
                EnterDwell(ControlsStepState.AlreadyDone, PreSatisfiedDwellSeconds, step.Id);
                return;
            }
            EnterDwell(ControlsStepState.Done, CompletedDwellSeconds, step.Id);
            VRHands.Left?.SendHaptic(HapticPreset.ClickPulse);
            VRHands.Right?.SendHaptic(HapticPreset.ClickPulse);
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Tutorial", $"Controls lesson: '{step.Id}' done.");
            return;
        }
        RefreshBox();
    }

    /// <summary>
    /// Settle the running card on a state, show it, and hold there for <paramref name="seconds"/>.
    ///
    /// <para>THE PROGRESS CHANNEL IS STOPPED FIRST, in every case including the skip. While a card
    /// is holding on ÜBERSPRUNGEN the player is still moving their hands, and a stray notification
    /// arriving mid-dwell would turn the word they are looking at into ERFÜLLT — which would be
    /// the card contradicting itself about the one thing it exists to say.</para>
    /// </summary>
    private static void EnterDwell(ControlsStepState state, float seconds, string stepId)
    {
        ControlsProgress.StopWaiting();
        _state = state;
        _dwelling = true;
        _dwellSince = Time.unscaledTime;
        _dwellSeconds = seconds;
        RefreshBox();
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson STATE LINE: step '{stepId}' settled on "
            + $"{state} and the card holds there for {seconds:0.0} s before the next one. This is "
            + "a state of THIS CARD only — there is no lesson-wide counter or bar anywhere in the "
            + "box.");
    }

    /// <summary>How long the box GROUP's window may be shut while the handler still says our
    /// message is the displayed one. A show/hide fade is 0.1 s (UIWindow default) and the open
    /// itself takes a frame or two, so anything held this long is a real close, not a transition.
    /// </summary>
    private const float WindowGoneSecondsToStop = 1.5f;

    /// <summary>
    /// The one state the handler's own bookkeeping cannot report: the message is still "currently
    /// displayed" but the WINDOW behind it has been hidden by someone else (the box group keeps
    /// its default escape-key action for non-HelpText layouts,
    /// LevelMessageUILayoutGroup.cs:59). A lesson running behind a shut window is invisible, and an
    /// invisible lesson holding the scripted chain is the worst state available here.
    /// </summary>
    private static bool WindowHiddenTooLong(float now)
    {
        // THE ONE LEGITIMATE REASON THE BOX IS AWAY, and the lesson's own step 15 causes it: the
        // player opened the pause menu, which is exactly what 'ctl_menu' asks them to do. The
        // game's own level-message group already treats an open ESCMenu as a special case
        // (LevelMessageUILayoutGroup.cs:65), so a lesson that ended itself here would punish the
        // player for performing the step it just set them.
        if (Singleton<ESCMenu>.IsInitialized && Singleton<ESCMenu>.Instance != null
            && Singleton<ESCMenu>.Instance.IsOpen)
        {
            _windowClosedSince = -1f;
            return false;
        }
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        LevelMessageUILayoutGroup? group = handler != null ? handler.LevelMessageBoxLayoutGroup : null;
        UIWindow? window = group != null ? group.window : null;   // publicized private
        if (window == null || window.IsOpen)
        {
            _windowClosedSince = -1f;
            return false;
        }
        if (_windowClosedSince < 0f)
        {
            _windowClosedSince = now;
            return false;
        }
        return now - _windowClosedSince >= WindowGoneSecondsToStop;
    }

    private static void Begin()
    {
        // The very first card names the device in words, so resolve it BEFORE any model is shown —
        // since 2026-09-02 the models only appear on steps that ask for a key.
        ControllerVisual.EnsureResolved(VRHands.Left ?? VRHands.Right);

        _index = 0;
        _dwelling = false;
        _dwellSince = 0f;
        PrepareStep(0);
        RefreshBox();
        if (!ControlsBox.Show(OnAction))
            return;   // window not free yet — the Opening ceiling bounds the retry

        AuditStepTable();
        AuditStateLine();
        _phase = Phase.Running;
        _runningAt = Time.unscaledTime;
        _windowClosedSince = -1f;
        ApplyStep(0);
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson started INSIDE the game's own tutorial box "
            + $"('{ControlsBox.MessageName}', layout FixedLowerRight — the same window the "
            + $"tutorial's TB_* steps use): {ControlsLesson.Steps.Length} steps, controller model "
            + $"'{ControllerVisual.DeviceId}'. The scripted chain is held until it ends.");
    }

    /// <summary>
    /// PRINT THE RESOLVED STEP TABLE, once, at the top of a run.
    ///
    /// <para>It used to WARN whenever <see cref="ControlsStep.ShowsController"/> disagreed with
    /// <see cref="ControlsStep.Key"/> being non-null, on the argument that the two agreed for every
    /// row and a disagreement was therefore a typo. Since the 2026-09-02 per-step ruling three rows
    /// disagree ON PURPOSE — take a card, hold a card and the fingertip pick all name a key and
    /// still show the player's own hand — so that warning would now fire three times a run and
    /// mean nothing. What is worth having instead is the table itself: one line saying what the
    /// headset was told to show for each step, so a hardware log answers "did my decision arrive"
    /// without a screenshot per card.</para>
    /// </summary>
    private static void AuditStepTable()
    {
        var table = new System.Text.StringBuilder(512);
        for (int i = 0; i < ControlsLesson.Steps.Length; i++)
        {
            ref readonly ControlsStep s = ref ControlsLesson.Steps[i];
            if (i > 0)
                table.Append(", ");
            table.Append(s.Id).Append('=')
                 .Append(s.ShowsController ? "controller" : "hand");
            if (s.Key != null)
                table.Append('(').Append(s.Key).Append(')');
        }
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", "Controls lesson step table, as the headset will show it — "
            + "'controller' means the device model replaces the hand for that card, 'hand' means "
            + "the player keeps their own hand and the key is named in words only: "
            + table);
    }

    /// <summary>
    /// SAY, ONCE, THAT THE COMPLETION INDICATOR IS ARMED AND IN WHICH WORDS.
    ///
    /// <para>This is the line a hardware round reads to separate "the indicator was not in the
    /// build" from "the indicator was in the build and the player did not see it". It resolves all
    /// four words through <see cref="Loc"/> in the language actually selected, so a missing
    /// translation shows up here rather than as a blank line in the headset — and it is printed at
    /// the top of the run, before any card can settle, because the per-card
    /// <c>Controls lesson STATE LINE</c> lines only appear once something HAPPENS.</para>
    /// </summary>
    private static void AuditStateLine()
    {
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", "Controls lesson STATE LINE armed — every teaching card now carries "
            + "one coloured word under its instruction saying whether THAT CARD has been "
            + $"satisfied: open='{Loc.Mod("ctl_state_open")}', done='{Loc.Mod("ctl_state_done")}', "
            + $"already='{Loc.Mod("ctl_state_already")}', skipped='{Loc.Mod("ctl_state_skipped")}'. "
            + "The welcome and closing cards carry none, and nothing in the box counts steps.");
    }

    private static void Advance()
    {
        _dwelling = false;
        _dwellSince = 0f;
        _index++;
        if (_index >= ControlsLesson.Steps.Length)
        {
            Stop("the lesson reached its last card");
            return;
        }
        PrepareStep(_index);
        ApplyStep(_index);
        RefreshBox();
    }

    /// <summary>
    /// ARM THE CARD'S CHECK, and decide once whether it was ALREADY SATISFIED before the player
    /// had a chance to read it.
    ///
    /// <para>WHY THAT VERDICT IS TAKEN HERE AND NOWHERE ELSE. Two of the steps are STATES rather
    /// than events — a card is in the hand or it is not — and a player can reach either of them
    /// already in that state (they take a card on <c>ctl_card_take</c> and are still gripping it
    /// when <c>ctl_card_hold</c> comes up, which is exactly what the 357 hardware log shows: the
    /// two <c>done.</c> lines are 57 log lines apart). One frame later the completion predicate
    /// reads TRUE in both cases and cannot tell "you just did it" from "it was already true", so
    /// the distinction only exists on the frame the card is applied. Every other step's action is
    /// an EVENT that can only be counted while <see cref="ControlsProgress.Waiting"/> names it, and
    /// <see cref="ControlsProgress.BeginWaiting"/> has just zeroed the accumulator, so this reads
    /// false for all of them — which is the correct answer, not a coincidence.</para>
    /// </summary>
    private static void PrepareStep(int index)
    {
        ref readonly ControlsStep step = ref ControlsLesson.Steps[index];
        ControlsProgress.BeginWaiting(step.Action);
        ControlsLesson.PollStates(in step);
        _preSatisfied = ControlsLesson.IsComplete(in step);
        // A card with no task carries no state line at all — see ControlsStepState.None.
        _state = step.Action == ControlAction.None
            ? ControlsStepState.None
            : ControlsStepState.Open;
    }

    /// <summary>
    /// THE RULE THAT DECIDES WHETHER A CONTROLLER MESH IS ON SCREEN (user ruling 2026-09-02): the
    /// running step's own <see cref="ControlsStep.ShowsController"/> declaration, and nothing else.
    /// A step that declares it gets both models plus the key highlight; every other step — the
    /// welcome card, the closing card, and any future step that asks for no press — gets the
    /// player's hands back. Show/Hide are idempotent, and Hide restores only the renderers this
    /// class switched off, by identity.
    /// </summary>
    private static void ApplyStep(int index)
    {
        ref readonly ControlsStep step = ref ControlsLesson.Steps[index];
        SetControllersVisible(step.ShowsController, step.Id);
        // A hand step lights NOTHING even when it names a key — there is no model to light it on,
        // and leaving `_lit` pointing at a key would make Highlight early-return on the next
        // controller step that happens to want the same one.
        string? key = step.ShowsController ? step.Key : null;
        _left?.Highlight(key);
        _right?.Highlight(key);
    }

    private static void SetControllersVisible(bool visible, string stepId)
    {
        if (visible == _controllersUp)
            return;
        _controllersUp = visible;
        if (visible)
        {
            if (VRHands.Left == null || VRHands.Right == null)
            {
                _controllersUp = false;
                return;
            }
            _left ??= new ControllerVisual(VRHands.Left);
            _right ??= new ControllerVisual(VRHands.Right);
            _left.Show();
            _right.Show();
        }
        else
        {
            // ANIMATED, not torn down: BeginHide shrinks the model and destroys it when it reaches
            // nothing. The teardown paths (Stop, Reset, Shutdown) call Hide() straight afterwards,
            // which forces it gone in the same frame — a lesson that is ending must not leave a
            // controller mid-shrink in the player's hand.
            _left?.BeginHide();
            _right?.BeginHide();
        }
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson: controller meshes swapping "
            + $"{(visible ? "IN over the hands" : "OUT, giving the player's own hands back")} for "
            + $"step '{stepId}' — they are up only while a step asks for or demonstrates a key "
            + "press, and the swap itself is animated (see the swap measurement line).");
    }

    /// <summary>Write the running card, in the state <see cref="_state"/> currently says it is in.
    /// The state is set by <see cref="PrepareStep"/> and <see cref="EnterDwell"/> and never derived
    /// here — a second reading of the completion predicate could not tell Done from AlreadyDone,
    /// and would flip the word under a player who is mid-read.</summary>
    private static void RefreshBox()
    {
        if (_index < 0 || _index >= ControlsLesson.Steps.Length)
            return;
        ref readonly ControlsStep step = ref ControlsLesson.Steps[_index];
        bool teaches = step.Action != ControlAction.None;
        string body = Loc.Mod(step.Id + "_b");
        // The key's NAME, and the controller's, are filled in per device: "press A" names nothing
        // on a Steam Frame, whose top inputs are a D-pad.
        if (step.KeyNameId != null)
            body = SafeFormat(body, Loc.Mod(step.KeyNameId
                + (ControllerVisual.HasDpad ? "_dpad" : string.Empty)));
        else if (step.Id == "ctl_welcome")
            body = SafeFormat(body, ControllerVisual.DeviceLabel);
        // THE BOX'S ONE BUTTON, and what it says (user ruling 2026-09-02). A teaching card offers
        // ÜBERSPRINGEN, which now leaves THAT CARD and nothing else; a prose card — the welcome and
        // the closing card, which have no task to skip — offers WEITER. Same button, same handler,
        // same effect: go to the next card. There is no "GEHT GERADE NICHT" any more; on a step the
        // room cannot currently offer, skipping the step IS "not now".
        ControlsBox.SetStep(
            Loc.Mod(step.Id + "_t"),
            body,
            Loc.Mod(teaches ? "ctl_skip" : "ctl_next"),
            _state);
    }

    /// <summary>A body whose {0} could not be filled is still a usable instruction; a lesson
    /// that disarms itself over a translator's stray brace is not. Never throws.</summary>
    private static string SafeFormat(string body, string argument)
    {
        try
        {
            return string.Format(body, argument);
        }
        catch (FormatException)
        {
            return body;
        }
    }

    /// <summary>
    /// THE BOX'S ONE BUTTON, pressed. It advances by exactly one card and nothing more (user
    /// ruling 2026-09-02: <i>"'Überspringen' soll nur für die jeweilige Aufgabe gelten"</i>).
    ///
    /// <para>WHAT THAT CHANGED, stated plainly, because it is a weakening. Until now this button
    /// ended the whole lesson in one press, and <see cref="ControlsBox"/>'s doc argued that a
    /// tutorial able to strand somebody is worse than one they leave early. It still cannot strand
    /// anybody: the button is on EVERY card including the last, so the way out of the lesson as a
    /// whole is to keep pressing it — at most <c>Steps.Length</c> presses from anywhere, and the
    /// press on the closing card runs <see cref="Advance"/> past the end of the table, which is
    /// <see cref="Stop"/>. What is gone is the ONE-PRESS exit; nothing else. The
    /// <see cref="MaxLessonSeconds"/> ceiling, the scenario boundary and the error latch are all
    /// still there and all still release the chain.</para>
    ///
    /// <para>SKIPPING IS NOT FULFILLING, and since 2026-09-03 the card says which of the two just
    /// happened rather than going quiet. A press on a TEACHING card that has not been satisfied
    /// settles it on ÜBERSPRUNGEN for <see cref="SkippedDwellSeconds"/> first; a press on a prose
    /// card, or a SECOND press while a settled card is holding, advances at once. That second rule
    /// is what keeps the exit path free: the welcome card tells the player that pressing through
    /// is how they leave, and somebody doing that must never be made to wait.</para>
    /// </summary>
    private static void OnAction()
    {
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson skipped by the player at step {_index} "
            + $"('{(_index >= 0 && _index < ControlsLesson.Steps.Length ? ControlsLesson.Steps[_index].Id : "?")}')"
            + " — the box's one button advances to the NEXT card; it no longer ends the lesson.");
        bool onCard = _index >= 0 && _index < ControlsLesson.Steps.Length;
        // Already holding on a settled word — they have seen it and pressed anyway. Go.
        if (_dwelling || !onCard)
        {
            Advance();
            return;
        }
        ref readonly ControlsStep step = ref ControlsLesson.Steps[_index];
        if (step.Action == ControlAction.None)
        {
            Advance();   // a prose card has nothing to be skipped past
            return;
        }
        EnterDwell(ControlsStepState.Skipped, SkippedDwellSeconds, step.Id);
    }

    /// <summary>Forget everything the CURRENT CARD was saying about itself. Called from both
    /// teardown paths, so a lesson that starts again can never inherit the last one's word.
    /// </summary>
    private static void ResetCardState()
    {
        _dwelling = false;
        _dwellSince = 0f;
        _dwellSeconds = 0f;
        _state = ControlsStepState.None;
        _preSatisfied = false;
    }

    private static void EngageHold(string why)
    {
        _heldAt = Time.unscaledTime;
        TutorialChainHold.Engage(HoldOwner, why);
    }

    /// <summary>
    /// THE SINGLE EXIT. Puts the hands back, closes the box through the game's own path, and hands
    /// the scripted chain back — in that order, and safe at any point and twice.
    /// </summary>
    internal static void Stop(string reason)
    {
        ControlsProgress.StopWaiting();
        SetControllersVisible(false, "lesson end");
        _left?.Hide();
        _right?.Hide();
        _left = _right = null;
        _controllersUp = false;
        ControlsBox.Close(reason);
        _index = -1;
        ResetCardState();
        _openingDialog = null;
        _phase = Phase.Idle;
        TutorialChainHold.Release(HoldOwner, reason);
    }

    /// <summary>Scenario boundary — anything still pending belongs to the level that just ended.
    /// The hold is DISCARDED rather than released: the withheld messages belong to that level too.
    /// </summary>
    internal static void Reset()
    {
        if (_phase != Phase.Idle && TutorialChainHold.IsHeldBy(HoldOwner))
            TutorialChainHold.Discard("a new scenario started while the controls lesson was running");
        ControlsProgress.StopWaiting();
        SetControllersVisible(false, "scenario boundary");
        _left?.Hide();
        _right?.Hide();
        _left = _right = null;
        _controllersUp = false;
        ControlsBox.Reset();
        _index = -1;
        ResetCardState();
        _openingDialog = null;
        _phase = Phase.Idle;
    }

    /// <summary>Session teardown — the hands are going away, so drop every reference to them.</summary>
    internal static void Shutdown()
    {
        Stop("the session is shutting down");
        ControlsProgress.Shutdown();
    }
}
