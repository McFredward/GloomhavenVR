using System;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE GAME'S OWN BUTTON FEEL, ON A BUTTON THE GAME DID NOT BUILD (user ruling 2026-09-02, after
/// hardware: <i>"Weiterhin geben die Tutorial-Buttons von unserem Teil kein Feedback (zB Ton) - es
/// soll sich verhalten wie als wäre es ein normaler Teil des Tutorials, daher sollen sich die
/// buttons auch normal verhalten."</i>).
///
/// <para>WHY THIS COMPONENT EXISTS AT ALL, rather than "use the game's button". The lesson's button
/// cannot be an <c>ExtendedButton</c>: that class routes every click through
/// <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c> (ExtendedButton.cs:170/200),
/// which passes only widgets an interactability PROFILE has isolated — and loading a profile for
/// the lesson's message is exactly what would gate off the board, the cards and the tiles the
/// lesson is teaching. So the button is a plain uGUI <see cref="UnityEngine.UI.Button"/>, and a
/// plain Button is silent. Everything an <c>ExtendedButton</c> would have done is reproduced here
/// instead, from the game's own data.</para>
///
/// <para>THE SPLIT, and it is worth being precise about which half is free. An
/// <c>ExtendedButton</c>'s HIGHLIGHT is not its own code: <c>DoStateTransition</c>
/// (ExtendedButton.cs:551) only calls <c>base.DoStateTransition</c>, so the colour and sprite
/// swap on hover and press are stock <c>Selectable</c>. Copying <c>transition</c>, <c>colors</c>
/// and <c>spriteState</c> off the template — which <see cref="ControlsBox"/> does — therefore
/// reproduces the whole visual half with no code at all. What is genuinely the extended class's
/// own is (a) the SOUND, through <c>PlaySound</c> (ExtendedButton.cs:238), and (b) a hover SCALE
/// tween (<c>ToggleHighlight</c>, ExtendedButton.cs:434). Those two are what this component is.
/// </para>
///
/// <para>THE SOUND IDS ARE READ OFF THE TEMPLATE, NOT CHOSEN HERE. The game resolves each id as
/// "the button's own field, or the shared <c>AudioButtonProfile</c> asset's if that is empty"
/// (ExtendedButton.cs:418), and <see cref="ControlsBox"/> applies exactly that rule to the box's
/// own close button, then to <c>UIInfoTools.Instance.generalAudioButtonProfile</c>, the asset the
/// game itself treats as the default set. Only the CLICK carries a hardcoded last resort
/// (<c>PlaySound_UIButtonSelect</c>, the id already verified in this repo). The others do not: a
/// hover sound the game never plays on its own buttons would not be "behaving like a normal part
/// of the tutorial", it would be louder than one.</para>
///
/// <para>PLAYED LISTENER-ANCHORED through <see cref="GameAudio.PlayListenerAnchored"/>, which is
/// the same <c>AudioController.Play(id)</c> call <c>AudioControllerUtils.PlaySound</c> makes. The
/// positional overload is silent in VR for a 3D item — that is a solved bug in this repo and this
/// class does not get to rediscover it.</para>
///
/// <para>NOTHING HERE MAY THROW INTO THE INPUT PATH. The pointer callbacks are invoked from the
/// EventSystem's dispatch, and <c>Update</c> runs every frame; an escaping exception in either
/// costs the player their hands. Both are wrapped, and a throw disarms the component for the rest
/// of the session rather than repeating once a frame.</para>
/// </summary>
internal sealed class ControlsButtonFeedback : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    /// <summary>The last-resort click id, and the ONLY hardcoded one. Verified elsewhere in this
    /// repo as a real <c>AudioController</c> item (see Cards/Driver/CardsDriver.2.Update.cs's
    /// known-item list); <see cref="GameAudio.PlayListenerAnchored"/> validates it again at call
    /// time and stays silent rather than logging a Unity error if it is not there.</summary>
    private static readonly string[] ClickFallbacks = { "PlaySound_UIButtonSelect" };

    internal string? EnterItem;
    internal string? ExitItem;
    internal string? DownItem;
    internal string? UpItem;
    internal string? ClickItem;

    /// <summary>Copied from the template's <c>highlightScaleFactor</c>; 1 disables the tween.</summary>
    internal float HighlightScale = 1f;

    /// <summary>Copied from the template's <c>animationDuration</c>.</summary>
    internal float AnimationDuration = 0.1f;

    private Transform? _target;
    private float _scale = 1f;
    private float _wanted = 1f;
    private bool _disabled;

    /// <summary>One-shot across the session: the first CLICK reports whether a sound actually came
    /// out and which id produced it. The user's complaint was silence, so "we asked for a sound"
    /// is not an answer the next hardware round can use — whether <c>AudioController.Play</c>
    /// handed back an object is.</summary>
    private static bool _clickReported;

    private void Awake() => _target = transform;

    public void OnPointerEnter(PointerEventData eventData)
    {
        _wanted = HighlightScale;
        Play(EnterItem, null, "hover");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _wanted = 1f;
        Play(ExitItem, null, "unhover");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // The game's press is an INSTANT half-step, not a tween (ExtendedButton.cs:210).
        _scale = _wanted = (HighlightScale + 1f) * 0.5f;
        Apply();
        Play(DownItem, null, "press");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _wanted = HighlightScale;
        Play(UpItem, null, "release");
    }

    /// <summary>Called from the button's own click listener rather than through a second
    /// <c>IPointerClickHandler</c> on the same object, so the ORDER of the sound and the lesson's
    /// step change is written down here instead of depending on component order.</summary>
    internal void PlayClick()
    {
        bool played = Play(ClickItem, ClickFallbacks, "click", out string item, out bool valid,
                           out string note);
        if (_clickReported)
            return;
        _clickReported = true;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", "Controls lesson button CLICK sound, first press of the session: "
            + $"id '{item}', valid={valid}, AudioController returned an object={played}{note}. "
            + "played=False with valid=True means the controller refused it (audio off, or the "
            + "item's own MinTimeBetweenPlayCalls throttle), not that the id is wrong.");
    }

    private void Update()
    {
        if (_disabled || Mathf.Approximately(_scale, _wanted))
            return;
        try
        {
            // Units of SCALE per second: the whole travel is |HighlightScale - 1|, and the game
            // spends animationDuration doing it. Unscaled time, because the game pauses.
            float speed = Mathf.Abs(HighlightScale - 1f) / Mathf.Max(AnimationDuration, 0.0001f);
            _scale = Mathf.MoveTowards(_scale, _wanted,
                                       speed * Mathf.Max(Time.unscaledDeltaTime, 0f));
            Apply();
        }
        catch (Exception ex)
        {
            _disabled = true;
            VRLog.Warn("Tutorial", "Controls lesson button hover tween threw and is disabled for "
                + $"this session: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Apply()
    {
        if (_target != null)
            _target.localScale = new Vector3(_scale, _scale, 1f);
    }

    private void Play(string? item, string[]? fallbacks, string which)
        => Play(item, fallbacks, which, out _, out _, out _);

    private bool Play(string? item, string[]? fallbacks, string which,
                      out string chosen, out bool valid, out string note)
    {
        chosen = item ?? string.Empty;
        valid = false;
        note = string.Empty;
        if (_disabled)
            return false;
        try
        {
            if (string.IsNullOrEmpty(item) && (fallbacks == null || fallbacks.Length == 0))
                return false;   // the game plays nothing here either
            return GameAudio.PlayListenerAnchored(item, fallbacks, out chosen, out valid, out note);
        }
        catch (Exception ex)
        {
            _disabled = true;
            VRLog.Warn("Tutorial", $"Controls lesson button {which} sound threw and every sound "
                + $"on this button is disabled for the session: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
