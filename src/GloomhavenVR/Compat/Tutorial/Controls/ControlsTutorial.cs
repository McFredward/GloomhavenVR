using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE CONTROLS PHASE of the first tutorial: the hands become the player's actual controllers,
/// one key lights up at a time, and each step ends when the player DOES the thing rather than
/// when they read about it.
///
/// <para>WHY IT IS A PHASE BESIDE THE TUTORIAL AND NOT A STEP INSIDE IT. The tutorial's own chain
/// is a serialized <c>CCustomLevelData</c> blob in the game's data package (see
/// <see cref="TutorialVR"/>): every hint appears and closes on a <c>CLevelTrigger</c> matching an
/// engine SEvent or a client <c>UIEvent</c>. There is no trigger for "the player turned the table
/// with both thumbsticks", so a controls step could only be woven in by inventing synthetic events
/// — writing mod state into the machine the entire rest of the tutorial depends on, to teach
/// something that has nothing to do with the rules of Gloomhaven. This phase therefore runs
/// ALONGSIDE that chain and touches none of it: no message is suppressed, no trigger is posted, no
/// game state is written. The one existing bridge in <see cref="TutorialVR"/> stays exactly as it
/// was.</para>
///
/// <para>WHAT IT DOES TOUCH is entirely the mod's own: two controller models in place of the hand
/// meshes, one panel, and a read of the mod's own input. Ending it puts every renderer back by
/// identity.</para>
///
/// <para>SINGLE-PLAYER BY CONSTRUCTION. <see cref="TutorialVR.IsTutorialActive"/> refuses while
/// <c>FFSNetwork.IsOnline</c>, and tutorials are single-player game modes to begin with, so
/// nothing here goes on the wire and no peer can see a hand that has turned into a controller.
/// The replay entry point is gated the same way for the same reason.</para>
/// </summary>
internal static class ControlsTutorial
{
    /// <summary>How long after the scenario starts before the lesson appears. The scene is still
    /// settling at frame zero — hands, bundle and board all arrive over the first second — and a
    /// panel that spawns into that lands somewhere nobody asked for.</summary>
    private const float StartDelaySeconds = 2.5f;

    /// <summary>How long a completed step stays on screen showing its filled bar before the next
    /// one replaces it. Long enough to register that it was the motion that did it.</summary>
    private const float CompletedDwellSeconds = 1.1f;

    private static ControlsPanel? _panel;
    private static ControllerVisual? _left, _right;
    private static int _index = -1;
    private static float _startAt;
    private static float _completedAt;
    private static bool _pending;
    private static bool _disabledByError;

    internal static bool IsRunning => _panel != null && _panel.IsShowing;

    /// <summary>The lesson is switchable off entirely ([Tutorial] ControlsLesson).</summary>
    /// <summary>[Compat] ControlsLesson, plus the error latch.</summary>
    internal static bool Enabled =>
        !_disabledByError && (Plugin.ControlsLesson?.Value ?? true);

    /// <summary>
    /// Ask for the lesson at the start of a tutorial scenario. Deliberately only a REQUEST: the
    /// hands, the asset bundle and the world panel machinery all come up over the first second or
    /// so of a scenario, so the actual start waits for <see cref="Tick"/> to find them.
    /// </summary>
    internal static void RequestForTutorial()
    {
        if (!Enabled || IsRunning)
            return;
        _pending = true;
        _startAt = Time.unscaledTime + StartDelaySeconds;
        VRLog.Info("Tutorial", "Controls lesson queued for this tutorial scenario "
            + $"(starts in {StartDelaySeconds:0.0} s, once the hands and the bundle are up). "
            + "Switch it off permanently with [Tutorial] ControlsLesson = false.");
    }

    internal static void Tick()
    {
        if (_disabledByError)
            return;
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            _disabledByError = true;
            VRLog.Error("Tutorial", "The controls lesson threw and is disabled for this session "
                + $"(the hands are restored): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            try { Stop(); } catch (Exception) { /* teardown must not throw twice */ }
        }
    }

    private static void TickCore()
    {
        if (_pending && Time.unscaledTime >= _startAt)
        {
            if (VRHands.Left == null || VRHands.Right == null)
                return;   // hands not up yet; try again next frame
            _pending = false;
            Begin();
        }
        if (!IsRunning)
            return;

        _panel!.Tick();
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
            if (Time.unscaledTime - _completedAt >= CompletedDwellSeconds)
                Advance();
            return;
        }
        if (ControlsLesson.IsComplete(in step))
        {
            _completedAt = Time.unscaledTime;
            ControlsProgress.StopWaiting();
            RefreshPanel(done: true);
            VRHands.Left?.SendHaptic(HapticPreset.ClickPulse);
            VRHands.Right?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Tutorial", $"Controls lesson: '{step.Id}' done.");
            return;
        }
        RefreshPanel(done: false);
    }

    private static void Begin()
    {
        _panel = new ControlsPanel(OnNext, OnSkip);
        _panel.Show();
        if (!_panel.IsShowing)
        {
            _panel = null;
            VRLog.Warn("Tutorial", "Controls lesson could not build its panel — not started.");
            return;
        }
        _left = new ControllerVisual(VRHands.Left!);
        _right = new ControllerVisual(VRHands.Right!);
        _left.Show();
        _right.Show();
        _index = -1;
        Advance();
        VRLog.Info("Tutorial", $"Controls lesson started: {ControlsLesson.Steps.Length} steps, "
            + $"showing the '{ControllerVisual.DeviceId}' controller.");
    }

    private static void Advance()
    {
        _completedAt = 0f;
        _index++;
        if (_index >= ControlsLesson.Steps.Length)
        {
            Stop();
            return;
        }
        ref readonly ControlsStep step = ref ControlsLesson.Steps[_index];
        ControlsProgress.BeginWaiting(step.Action);
        _left?.Highlight(step.Key);
        _right?.Highlight(step.Key);
        RefreshPanel(done: false);
    }

    private static void RefreshPanel(bool done)
    {
        if (_panel == null || _index < 0 || _index >= ControlsLesson.Steps.Length)
            return;
        ref readonly ControlsStep step = ref ControlsLesson.Steps[_index];
        bool teaches = step.Action != ControlAction.None;
        string body = Loc.Mod(step.Id + "_b");
        if (done)
            body += "\n\n" + Loc.Mod("ctl_good");
        _panel.SetStep(
            Loc.Mod(step.Id + "_t"),
            body,
            teaches ? $"{_index}/{TeachingStepCount}" : string.Empty,
            teaches ? Loc.Mod(step.Situational ? "ctl_cant" : "ctl_next") : Loc.Mod("ctl_next"),
            done ? 1f : ControlsLesson.Progress(in step),
            teaches);
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
        VRLog.Info("Tutorial", $"Controls lesson skipped by the player at step {_index} "
            + $"('{(_index >= 0 && _index < ControlsLesson.Steps.Length ? ControlsLesson.Steps[_index].Id : "?")}').");
        Stop();
    }

    /// <summary>End the lesson and put everything back. Safe at any point, and safe twice.</summary>
    internal static void Stop()
    {
        ControlsProgress.StopWaiting();
        _left?.Hide();
        _right?.Hide();
        _left = _right = null;
        _panel?.Close();
        _panel = null;
        _index = -1;
        _completedAt = 0f;
        _pending = false;
    }

    /// <summary>Session teardown — the hands are going away, so drop every reference to them.</summary>
    internal static void Shutdown()
    {
        Stop();
        ControlsProgress.Shutdown();
    }
}
