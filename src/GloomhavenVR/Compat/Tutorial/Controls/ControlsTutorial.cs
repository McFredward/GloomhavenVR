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

    /// <summary>How long a completed step stays on screen showing its filled bar before the next
    /// one replaces it. Long enough to register that it was the motion that did it.</summary>
    private const float CompletedDwellSeconds = 1.1f;

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
    private static float _completedAt;
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
                + "treat it as a symptom — check whether the NEXT/SKIP buttons were built into "
                + "the box at all (a Warn above says so if they were not). Handing the scripted "
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

        // A state-shaped step can already be satisfied when it opens (the player may reach the
        // card step holding a card); an event-shaped one cannot, and polling it would be noise.
        ControlsLesson.PollStates(in step);

        if (_completedAt > 0f)
        {
            if (now - _completedAt >= CompletedDwellSeconds)
                Advance();
            return;
        }
        if (ControlsLesson.IsComplete(in step))
        {
            _completedAt = now;
            ControlsProgress.StopWaiting();
            RefreshBox(done: true);
            VRHands.Left?.SendHaptic(HapticPreset.ClickPulse);
            VRHands.Right?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Tutorial", $"Controls lesson: '{step.Id}' done.");
            return;
        }
        RefreshBox(done: false);
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
        _completedAt = 0f;
        ControlsProgress.BeginWaiting(ControlsLesson.Steps[0].Action);
        RefreshBox(done: false);
        if (!ControlsBox.Show(OnNext, OnSkip))
            return;   // window not free yet — the Opening ceiling bounds the retry

        AuditStepTable();
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
    /// The two facts <see cref="ControlsStep.ShowsController"/> and <see cref="ControlsStep.Key"/>
    /// are declared separately on purpose (see that field). They agree for every row today; if a
    /// future edit makes them disagree, that is either a deliberate case or a typo, and the log is
    /// where the difference has to show up rather than in a player's headset.
    /// </summary>
    private static void AuditStepTable()
    {
        for (int i = 0; i < ControlsLesson.Steps.Length; i++)
        {
            ref readonly ControlsStep s = ref ControlsLesson.Steps[i];
            if (s.ShowsController == (s.Key != null))
                continue;
            VRLog.Warn("Tutorial", $"Controls lesson step '{s.Id}' declares "
                + $"ShowsController={s.ShowsController} but "
                + $"{(s.Key == null ? "lights no key" : $"lights '{s.Key}'")}. The controller "
                + "models follow the DECLARATION, not the key — if that is not what was meant, "
                + "fix the step table.");
        }
    }

    private static void Advance()
    {
        _completedAt = 0f;
        _index++;
        if (_index >= ControlsLesson.Steps.Length)
        {
            Stop("the lesson reached its last card");
            return;
        }
        ControlsProgress.BeginWaiting(ControlsLesson.Steps[_index].Action);
        ApplyStep(_index);
        RefreshBox(done: false);
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
        _left?.Highlight(step.Key);
        _right?.Highlight(step.Key);
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
            _left?.Hide();
            _right?.Hide();
        }
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson: controller meshes "
            + $"{(visible ? "SHOWN" : "HIDDEN (the player's own hands are back)")} for step "
            + $"'{stepId}' — they are up only while a step asks for or demonstrates a key press.");
    }

    private static void RefreshBox(bool done)
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
        if (done)
            body += "\n\n" + Loc.Mod("ctl_good");
        ControlsBox.SetStep(
            Loc.Mod(step.Id + "_t"),
            body,
            teaches ? $"{_index}/{TeachingStepCount}" : string.Empty,
            teaches ? Loc.Mod(step.Situational ? "ctl_cant" : "ctl_next") : Loc.Mod("ctl_next"),
            done ? 1f : ControlsLesson.Progress(in step),
            teaches,
            offerSkip: true);
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

    /// <summary>Steps that actually teach a control — the welcome and closing cards are not
    /// counted, because "1 of 15" when two of them are prose is a promise the lesson does not
    /// keep.</summary>
    private static int TeachingStepCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < ControlsLesson.Steps.Length; i++)
                if (ControlsLesson.Steps[i].Action != ControlAction.None)
                    n++;
            return n;
        }
    }

    private static void OnNext() => Advance();

    private static void OnSkip()
    {
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", $"Controls lesson skipped by the player at step {_index} "
            + $"('{(_index >= 0 && _index < ControlsLesson.Steps.Length ? ControlsLesson.Steps[_index].Id : "?")}').");
        Stop("the player pressed SKIP");
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
        _completedAt = 0f;
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
        _completedAt = 0f;
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
