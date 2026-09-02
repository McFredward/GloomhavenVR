using System;
using GloomhavenVR.Core;
using ScenarioRuleLibrary.CustomLevels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE LESSON, INSIDE THE GAME'S OWN TUTORIAL BOX (user ruling 2026-09-02, verbatim: <i>"ich will
/// dass die neuen Aufgaben und der neue Text IN das standart Tutorial integriert wird … es soll
/// das standart Fenster sein das nach dem Dialog kommt und durch das Tutorial führt"</i>).
///
/// <para>WHICH WINDOW THAT IS, read from the game's own data rather than guessed. The tutorial's
/// flow dump (.planning/debug/LogOutput.log:9252-9256) lists <c>TB_1</c> as layout
/// <c>StoryDialog</c> — the dialogue — and the very next entry, <c>TB_2_1</c>, as layout
/// <c>FixedLowerRight</c> displayed by <c>LevelMessageDismissed ctxId='TB_1'</c>. So "the standard
/// window that comes after the dialogue and leads through the tutorial" is the
/// <c>FixedLowerRight</c> BOX message, shown by
/// <c>LevelMessagesUIHandler.ShowBoxMessage</c> (LevelMessagesUIHandler.cs:83) and closed by
/// <c>HideCurrentlyShownBoxMessage</c> (LevelMessagesUIHandler.cs:153). This class puts every
/// lesson step in exactly that window.</para>
///
/// <para>THE OBSTACLE THE OLD PANEL NAMED IS REAL, AND THIS IS HOW IT IS PAID. The retired
/// <c>ControlsPanel</c> argued that the box "appears because a trigger fired and closes because
/// another one did", and that a step ending when the player physically turns the table has no
/// trigger to offer. True — so this message offers NONE. It is a mod-owned
/// <see cref="CLevelMessage"/> that:
/// <list type="number">
/// <item>is never in <c>LevelEventsController.m_MessagesToShow</c>, so its DISPLAY trigger is
///   never consulted — the mod shows it directly through the handler's public entry point, the
///   same one <c>ShowLevelMessageRoutine</c> uses (LevelEventsController.cs:869);</item>
/// <item>carries a DISMISS trigger that cannot match anything (<c>EventTriggerTypeInt =
///   int.MaxValue</c>, <c>IsUIEventTypeTrigger=false</c>) — both arms of
///   <c>ShouldEventCauseTrigger</c> compare the trigger's type against the event's
///   (LevelEventsController.cs:561/632), so no SEvent and no UIEvent can close it behind our
///   back;</item>
/// <item>is closed by the mod, through the game's own <c>HideCurrentlyShownBoxMessage</c>, when
///   the lesson is over.</item>
/// </list>
/// No synthetic trigger is posted and no trigger store is written. The one UIEvent our dismissal
/// produces is <c>LevelMessageDismissed</c> carrying <see cref="MessageName"/>, which no scripted
/// trigger in the tutorial references (every ctxId in the flow dump is <c>TB_*</c>/<c>HT_*</c>).</para>
///
/// <para>WHY <c>IsTriggeredByDismiss</c> IS FALSE, AND WHAT THAT COSTS. That flag is read by three
/// separate machines. <c>LevelEventsController.MessageWasDisplayed</c> loads the modal
/// dismiss-message interactability profile for a dismiss-triggered box
/// (LevelEventsController.cs:1059); <c>ModalFallback.ActionDismissedLevelMessage</c> classifies the
/// window BLOCKING for one (ModalFallback.7.Close.cs:533 — ModalUI lock, ray pick gate, card input
/// block); and <c>LevelMessageUILayout.Init</c> only then shows the box's own Continue button
/// (LevelMessageUILayout.cs:115). A lesson step that asks the player to drag the table, take a
/// card or click a hex cannot run under the first two. So the flag is false — the window floats
/// visible and imposes ZERO input restrictions, exactly like every action-dismissed scripted strip
/// — and the price is the third one: the game's Continue button stays hidden and this class builds
/// the lesson's two affordances itself, as plain uGUI buttons inside the game's own box (see
/// <see cref="Decorate"/>). They are styled from the box's own close button, so they are the game's
/// look in the game's window; they are not a second window.</para>
///
/// <para>ZERO GAME STATE IS WRITTEN. With <c>IsTriggeredByDismiss=false</c>, an empty
/// <c>InteractabilityProfileForMessage</c>, <c>ShouldPauseGame=false</c>, <c>ShowScreenBG=false</c>
/// and a default camera profile, <c>MessageWasDisplayed</c> and <c>MessageWasDismissed</c> both run
/// to completion without loading a profile, pausing the world or moving a camera
/// (LevelEventsController.cs:943-999). The window is shown and hidden and nothing else happens.</para>
///
/// <para>NEVER AN EMPTY WINDOW. The message always carries a non-empty title and exactly ONE page
/// whose text this class supplies; <see cref="Show"/> refuses outright if either is blank, and
/// <see cref="ContentDeadlineSeconds"/> is the backstop for the case where the box opens but the
/// page override never lands (a prefab without a pagination handler would do that) — the lesson
/// then closes itself and says so at <c>Error</c>.</para>
///
/// <para>SINGLE-PLAYER BY CONSTRUCTION, like the rest of the tutorial work:
/// <see cref="TutorialVR.IsTutorialActive"/> refuses while <c>FFSNetwork.IsOnline</c> and tutorial
/// game modes are single-player to begin with. Nothing here is serialized, and
/// <c>UIEventManager.OnEventLogged</c> has exactly one subscriber, the local
/// <c>LevelEventsController</c> — UIEvents never reach Photon/FFSNet.</para>
/// </summary>
internal static class ControlsBox
{
    /// <summary>Our message's identity in the game's logs and in the <c>LevelMessageDismissed</c>
    /// event our dismissal posts. Prefixed so it can never collide with a scripted name.</summary>
    internal const string MessageName = "GLOOMHAVENVR_CTL_LESSON";

    /// <summary>
    /// The title/page key we hand the game. Both are replaced by this class before the frame is
    /// drawn (the overrides run inside the very <c>Init</c>/<c>OnLanguageChanged</c> that resolves
    /// them), so the value is never seen; it is a key the game certainly ships purely so
    /// <c>LocalizationManager.GetTranslation</c> does not log "term not found" and paint
    /// "UNDEFINED &lt;color=red&gt;…". Same reasoning as <see cref="TutorialGrabStep"/>'s.
    /// </summary>
    private const string PlaceholderKey = "GUI_CONTINUE";

    /// <summary>How long after the box opens the page text must have been overridden at least
    /// once. Reaching it means the box is on screen without our content — the one way this path
    /// could produce an empty window — so the lesson closes itself and logs loudly.</summary>
    private const float ContentDeadlineSeconds = 4f;

    /// <summary>Cells in the ASCII progress bar. ASCII on purpose: the box uses the GAME's font,
    /// and a block-drawing glyph that font does not carry renders as a fallback box.</summary>
    private const int BarCells = 12;

    private static CLevelMessage? _message;
    private static CLevelMessagePage? _page;

    // The two text objects the game resolved for OUR message, captured by the override hooks.
    private static TextMeshProUGUI? _titleText;
    private static TextMeshProUGUI? _bodyText;

    // The lesson's own affordances, built into the game's box (see Decorate).
    private static GameObject? _nextButton;
    private static GameObject? _skipButton;
    private static TextMeshProUGUI? _nextLabelText;
    private static TextMeshProUGUI? _skipLabelText;

    private static string _title = string.Empty;
    private static string _body = string.Empty;
    private static string _nextLabel = string.Empty;
    private static bool _skipOffered;
    private static float _shownAt = -1f;
    private static bool _contentSeen;
    private static bool _warnedNoButtons;

    private static Action? _onNext;
    private static Action? _onSkip;

    /// <summary>Is OUR message the one the game currently has in its box window?</summary>
    internal static bool IsShowing
    {
        get
        {
            LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
            return _message != null && handler != null
                   && ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, _message);
        }
    }

    /// <summary>True once the box has been opened AND the page text has actually been written by
    /// us at least once — i.e. the window provably has content.</summary>
    internal static bool HasContent => _contentSeen;

    /// <summary>The one deadline this class owns: the box is open but our page text never
    /// arrived. Read by the driver, which then ends the lesson rather than leave a blank box.
    /// </summary>
    internal static bool ContentDeadlinePassed =>
        _message != null && !_contentSeen && _shownAt >= 0f
        && Time.unscaledTime - _shownAt >= ContentDeadlineSeconds;

    /// <summary>
    /// Write the running step into the box. Called BEFORE <see cref="Show"/> for the first step so
    /// the window can never open blank, and on every refresh afterwards.
    /// </summary>
    internal static void SetStep(string title, string body, string counter, string nextLabel,
                                float progress, bool showBar, bool offerSkip)
    {
        _title = title ?? string.Empty;
        _nextLabel = nextLabel ?? string.Empty;
        _skipOffered = offerSkip;
        string text = body ?? string.Empty;
        if (showBar)
            text += "\n\n" + Bar(progress)
                    + (string.IsNullOrEmpty(counter) ? string.Empty : "   " + counter);
        _body = text;
        PushText();
    }

    /// <summary>The step's progress as an ASCII bar: 0..1 over <see cref="BarCells"/> cells.</summary>
    private static string Bar(float progress)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(progress) * BarCells), 0, BarCells);
        return "[" + new string('#', filled) + new string('-', BarCells - filled) + "]";
    }

    /// <summary>
    /// Open the game's box on our message. Refuses — rather than elbowing anything aside — when
    /// the handler is missing, a scripted box owns the window, a display delay is running, or the
    /// scenario-completion one-shot is armed (dismissing a message while
    /// <c>m_ActionForNextMessageDismissal</c> is set ENDS the level, LevelEventsController.cs:929).
    /// </summary>
    internal static bool Show(Action onNext, Action onSkip)
    {
        if (_message != null)
            return true;
        if (string.IsNullOrEmpty(_title) || string.IsNullOrEmpty(_body))
        {
            VRLog.Error("Tutorial", "Controls lesson refused to open the tutorial box: the first "
                + "step had no title or no body text. A tutorial box with nothing in it is a "
                + "defect, so the lesson does not start.");
            return false;
        }
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        LevelEventsController? controller = LevelEventsController.s_Instance;
        if (handler == null || controller == null || InteractabilityManager.s_Instance == null)
            return false;
        if (handler.DisplayDelayInEffect || handler.CurrentlyDisplayedBoxMessage != null)
            return false;
        if (controller.m_ActionForNextMessageDismissal != null)
            return false;

        _onNext = onNext;
        _onSkip = onSkip;
        _contentSeen = false;
        _message = BuildMessage();
        _page = _message.Pages[0];
        _shownAt = Time.unscaledTime;
        // The game's own show path for a scripted box message (LevelEventsController.cs:869).
        handler.ShowBoxMessage(_message);
        return true;
    }

    /// <summary>
    /// THE SINGLE EXIT. Removes our buttons, then closes the window through the game's own
    /// dismissal path — but only while the window provably still shows OUR message, so the
    /// handler's bookkeeping can never be left half-updated.
    /// </summary>
    internal static void Close(string reason)
    {
        DestroyButtons();
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        bool ours = _message != null && handler != null
                    && ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, _message);
        _message = null;
        _page = null;
        _titleText = null;
        _bodyText = null;
        _onNext = null;
        _onSkip = null;
        _shownAt = -1f;
        _contentSeen = false;
        if (!ours)
            return;
        handler!.HideCurrentlyShownBoxMessage();
        VRLog.Info("Tutorial", $"Controls lesson closed the game's tutorial box — {reason}.");
    }

    /// <summary>Per-frame text upkeep. The game re-resolves the title and the page on every
    /// language / controller change, so our text is written back whenever it differs.</summary>
    internal static void Tick()
    {
        if (_message == null)
            return;
        PushText();
    }

    private static void PushText()
    {
        if (_titleText != null && !string.Equals(_titleText.text, _title, StringComparison.Ordinal))
            _titleText.text = _title;
        if (_bodyText != null && !string.Equals(_bodyText.text, _body, StringComparison.Ordinal))
            _bodyText.text = _body;
        if (_nextLabelText != null
            && !string.Equals(_nextLabelText.text, _nextLabel, StringComparison.Ordinal))
            _nextLabelText.text = _nextLabel;
        if (_skipButton != null && _skipButton.activeSelf != _skipOffered)
            _skipButton.SetActive(_skipOffered);
    }

    // ---- the text overrides, called from the existing hint postfixes -------------------------

    /// <summary>Is this the mod-owned lesson message?</summary>
    internal static bool Owns(CLevelMessage? message) =>
        message != null && _message != null && ReferenceEquals(message, _message);

    /// <summary>TITLE override. Also captures the text object so the running step can be written
    /// straight into it without waiting for the game to re-resolve anything.</summary>
    internal static bool TryTitle(CLevelMessage message, TextMeshProUGUI? target, out string text)
    {
        text = string.Empty;
        if (!Owns(message))
            return false;
        if (target != null)
            _titleText = target;
        text = _title;
        return true;
    }

    /// <summary>
    /// BODY override, matched on the PAGE OBJECT rather than on a loc key. Our page carries a real
    /// game key (see <see cref="PlaceholderKey"/>) precisely so the game's localization stays
    /// quiet, which means the key cannot identify us — the reference can, and cannot collide.
    /// </summary>
    internal static bool TryPage(CLevelMessagePage? page, TextMeshProUGUI? target, out string text)
    {
        text = string.Empty;
        if (page == null || _page == null || !ReferenceEquals(page, _page))
            return false;
        if (target != null)
            _bodyText = target;
        _contentSeen = true;
        text = _body;
        return true;
    }

    /// <summary>
    /// Called from the existing <c>LevelMessageUILayout.Init</c> postfix for EVERY message. For
    /// ours it builds the lesson's two buttons into the game's box; for anything else it removes
    /// them, so a leftover can never ride along on a scripted message that reuses the same layout
    /// object (<c>LevelMessageUILayoutGroup.Show</c> hides and re-inits the shared prefabs,
    /// LevelMessageUILayoutGroup.cs:48-57).
    /// </summary>
    internal static void Decorate(LevelMessageUILayout ui, CLevelMessage? message)
    {
        DestroyButtons();
        if (!Owns(message))
            return;

        ExtendedButton? template = ui.closeButton;
        Transform? parent = template != null ? template.transform.parent : ui.transform;
        if (parent == null)
            parent = ui.transform;

        // The game reserves the bottom strip of a FixedLowerRight box for its Continue button, but
        // only when it believes the message is dismiss-triggered (LevelMessageUILayout.cs:119-127);
        // ours is not, so the same padding is applied here for the buttons we put there instead.
        if (message!.LayoutType == CLevelMessage.ELevelMessageLayoutType.FixedLowerRight
            && ui.title != null && ui.title.transform.parent != null)
        {
            var group = ui.title.transform.parent.GetComponent<VerticalLayoutGroup>();
            if (group != null)
                group.padding.bottom = 40;
        }

        _nextButton = BuildButton(parent, template, "GloomhavenVR.LessonNext", 0f,
            out _nextLabelText, () => Fire(_onNext, "NEXT"));
        _skipButton = BuildButton(parent, template, "GloomhavenVR.LessonSkip", -1f,
            out _skipLabelText, () => Fire(_onSkip, "SKIP"));
        if (_skipLabelText != null)
            _skipLabelText.text = Loc.Mod("ctl_skip");
        if (_skipButton != null)
            _skipButton.SetActive(_skipOffered);
        PushText();
        if (_nextButton == null && !_warnedNoButtons)
        {
            _warnedNoButtons = true;
            VRLog.Warn("Tutorial", "Controls lesson could not build its buttons into the tutorial "
                + "box. Every step still ends by itself when the player performs it, but a step "
                + "whose motion the room cannot offer can then only be left by switching the "
                + "lesson off ([Compat] ControlsLesson).");
        }
    }

    /// <summary>
    /// One lesson affordance, built from the box's OWN close button so it wears the game's look:
    /// same rect, same background sprite and colour, same font. It is a PLAIN uGUI
    /// <see cref="Button"/> and deliberately not an <c>ExtendedButton</c> — the extended one routes
    /// every click through <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c>
    /// (ExtendedButton.cs:170), which only passes widgets an interactability PROFILE isolated, and
    /// loading a profile for our message is exactly what would gate off the board, the cards and
    /// the tiles the lesson is teaching. A plain Button is gated by nothing, and the mod's laser
    /// and fingertip both drive it: <see cref="Hands.Interact.RayUguiDriver"/> resolves clicks with
    /// <c>ExecuteEvents.GetEventHandler&lt;IPointerClickHandler&gt;</c>, which a plain Button
    /// satisfies.
    /// </summary>
    /// <param name="slot">0 for the primary slot (where the game's own button sits), -1 for one
    /// button's width to its left.</param>
    private static GameObject? BuildButton(Transform parent, ExtendedButton? template, string name,
        float slot, out TextMeshProUGUI? label, Action onClick)
    {
        label = null;
        if (parent == null)
            return null;
        try
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = (RectTransform)go.transform;

            var image = go.AddComponent<Image>();
            RectTransform? templateRect = template != null
                ? template.GetComponent<RectTransform>() : null;
            Image? templateImage = template != null ? template.GetComponent<Image>() : null;
            Vector2 size = templateRect != null && templateRect.rect.width > 1f
                ? templateRect.rect.size
                : new Vector2(200f, 52f);
            if (templateRect != null)
            {
                rect.anchorMin = templateRect.anchorMin;
                rect.anchorMax = templateRect.anchorMax;
                rect.pivot = templateRect.pivot;
                rect.sizeDelta = templateRect.sizeDelta;
                rect.anchoredPosition = templateRect.anchoredPosition
                                        + new Vector2(slot * (size.x + 16f), 0f);
            }
            else
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.sizeDelta = size;
                rect.anchoredPosition = new Vector2(-16f + slot * (size.x + 16f), 16f);
            }
            if (templateImage != null)
            {
                image.sprite = templateImage.sprite;
                image.color = templateImage.color;
                image.type = templateImage.type;
                image.material = templateImage.material;
                image.preserveAspect = templateImage.preserveAspect;
            }
            else
            {
                image.color = new Color(0.22f, 0.24f, 0.30f, 0.96f);
            }
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.layer = go.layer;
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            TextMeshProUGUI? templateLabel = template != null ? template.buttonText : null;
            if (templateLabel != null && templateLabel.font != null)
            {
                tmp.font = templateLabel.font;
                tmp.fontSharedMaterial = templateLabel.fontSharedMaterial;
                tmp.fontSize = templateLabel.fontSize;
                tmp.fontStyle = templateLabel.fontStyle;
                tmp.color = templateLabel.color;
            }
            else
            {
                tmp.fontSize = 24f;
                tmp.color = Color.white;
                WorldUI.WorldUIAssets.TryAssignGameFont(tmp);
            }
            label = tmp;
            return go;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"Controls lesson could not build '{name}': "
                + $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>A button listener that throws would take the rest of the uGUI click chain with it
    /// (<c>UnityEvent.Invoke</c> has no per-listener catch). Never let one out.</summary>
    private static void Fire(Action? action, string which)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            VRLog.Error("Tutorial", $"Controls lesson {which} button threw: "
                + $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void DestroyButtons()
    {
        if (_nextButton != null)
            UnityEngine.Object.Destroy(_nextButton);
        if (_skipButton != null)
            UnityEngine.Object.Destroy(_skipButton);
        _nextButton = _skipButton = null;
        _nextLabelText = _skipLabelText = null;
    }

    /// <summary>Scenario boundary: forget everything without touching the game (the level that
    /// owned the window is gone).</summary>
    internal static void Reset()
    {
        DestroyButtons();
        _message = null;
        _page = null;
        _titleText = null;
        _bodyText = null;
        _onNext = null;
        _onSkip = null;
        _shownAt = -1f;
        _contentSeen = false;
        _title = _body = _nextLabel = string.Empty;
        _skipOffered = false;
    }

    /// <summary>
    /// The mod-owned message. Box layout (<c>FixedLowerRight</c>) = the very window the tutorial's
    /// own <c>TB_*</c> steps use. One page, because <c>LevelMessageUILayout.Init</c> only builds
    /// pages when there are any (LevelMessageUILayout.cs:153) and a box with no page body is the
    /// empty window this project forbids. The dismiss trigger is deliberately UNMATCHABLE, and
    /// <c>IsTriggeredByDismiss=false</c> keeps the window non-blocking — see the class doc for
    /// what each of those two flags buys and costs.
    /// </summary>
    private static CLevelMessage BuildMessage()
    {
        var message = new CLevelMessage
        {
            MessageName = MessageName,
            LayoutType = CLevelMessage.ELevelMessageLayoutType.FixedLowerRight,
            TitleKey = PlaceholderKey,
            DisplayDelay = 0f,
            ShouldPauseGame = false,
            ShowScreenBG = false,
        };
        message.Pages.Add(new CLevelMessagePage(PlaceholderKey));
        message.DismissTrigger.IsTriggeredByDismiss = false;
        message.DismissTrigger.IsUIEventTypeTrigger = false;
        message.DismissTrigger.EventTriggerTypeInt = int.MaxValue;
        message.DismissTrigger.EventTriggerSubTypeInt = int.MaxValue;
        return message;
    }
}
