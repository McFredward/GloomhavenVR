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
/// the lesson's ONE affordance itself, as a plain uGUI button inside the game's own box (see
/// <see cref="Decorate"/>). It is styled from the box's own close button, so it is the game's look
/// in the game's window; it is not a second window.</para>
///
/// <para>ONE BUTTON, ON THE WINDOW, CENTRED ALONG ITS BOTTOM EDGE (user ruling 2026-09-02, after
/// hardware: <i>"sind die buttons nicht auf dem Fenster …, die button(s) sollte mittig zentriert
/// unten am Fenster hängen. Weiterhin möchte ich keinen button 'geht gerade Nicht' und
/// 'Überspringen' soll nur für die jeweilige Aufgabe gelten."</i>). ModBuild 343 built TWO buttons
/// and placed them by COPYING the close button's anchors and nudging one of them a width to the
/// left; the hardware shot (.planning/debug/tutorial-buttons.jpg) shows the result hanging off the
/// panel's bottom-left corner, the left one entirely outside the frame. The placement is no longer
/// copied from anything — see <see cref="Decorate"/> for the parent and the anchoring, and why they
/// are derived from the rect the GAME ITSELF reserves bottom padding in.</para>
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

    /// <summary>How much clear space the button gets under it and over it inside the panel's
    /// bottom strip, as a fraction of the button's OWN height.
    ///
    /// <para>A FRACTION RATHER THAN A CONSTANT, because nobody here knows what one unit of this
    /// canvas is worth. Solving it from the hardware shot: 343's two buttons were placed one
    /// <c>size.x + 16</c> apart, their measured centres are 488 image pixels apart and each button
    /// measures about 476 pixels wide, which puts the canvas at roughly 1.8 image pixels per unit
    /// and the button at about 260 × 37 units. So a margin written as "12 units" would have been
    /// 22 pixels on that shot and something else entirely on a differently scaled window. The
    /// button's own height is the one length already expressed in the right units.</para>
    /// </summary>
    private const float BottomMarginFraction = 0.25f;

    private static CLevelMessage? _message;
    private static CLevelMessagePage? _page;

    // The two text objects the game resolved for OUR message, captured by the override hooks.
    private static TextMeshProUGUI? _titleText;
    private static TextMeshProUGUI? _bodyText;

    // The lesson's ONE affordance, built into the game's box (see Decorate).
    private static GameObject? _actionButton;
    private static TextMeshProUGUI? _actionLabelText;

    private static string _title = string.Empty;
    private static string _body = string.Empty;
    private static string _actionLabel = string.Empty;
    private static float _shownAt = -1f;
    private static bool _contentSeen;
    private static bool _warnedNoButtons;
    private static bool _geometryLogged;

    private static Action? _onAction;

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
    ///
    /// <para>NO PROGRESS INDICATOR ANY MORE (user ruling 2026-09-02: <i>"Diese komischen Punkte
    /// die den Fortschritt anzeigen soll auch weg."</i>). Until now the body carried an extra line
    /// holding an ASCII bar and an "8/14" counter; both are gone, and with them the reason the bar
    /// was ASCII in the first place (the box uses the GAME's font, which has no block-drawing
    /// glyph and would have rendered a fallback box). The card is now title and instruction only.
    /// </para>
    /// </summary>
    internal static void SetStep(string title, string body, string actionLabel)
    {
        _title = title ?? string.Empty;
        _actionLabel = actionLabel ?? string.Empty;
        _body = body ?? string.Empty;
        PushText();
    }

    /// <summary>
    /// Open the game's box on our message. Refuses — rather than elbowing anything aside — when
    /// the handler is missing, a scripted box owns the window, a display delay is running, or the
    /// scenario-completion one-shot is armed (dismissing a message while
    /// <c>m_ActionForNextMessageDismissal</c> is set ENDS the level, LevelEventsController.cs:929).
    /// </summary>
    internal static bool Show(Action onAction)
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

        _onAction = onAction;
        _contentSeen = false;
        _message = BuildMessage();
        _page = _message.Pages[0];
        _shownAt = Time.unscaledTime;
        // The game's own show path for a scripted box message (LevelEventsController.cs:869).
        handler.ShowBoxMessage(_message);
        return true;
    }

    /// <summary>
    /// THE SINGLE EXIT. Removes our button, then closes the window through the game's own
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
        _onAction = null;
        _shownAt = -1f;
        _contentSeen = false;
        _geometryLogged = false;
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
        // ONE SHOT, once the layout has actually run, and the latch is set HERE rather than
        // inside the describer so the describer stays a pure read. It retries for a few frames
        // because the rect is not resolved until the canvas rebuilds, and it latches either way
        // at the content deadline — a probe that has answered is spent, and one that cannot
        // answer has to SAY SO rather than run silently for the rest of the lesson.
        if (!_geometryLogged && _actionButton != null)
        {
            if (TryDescribeGeometry(out string geometry))
            {
                _geometryLogged = true;
                // HW-VERIFY: a standing hardware question is waiting on this line — it must stay
                // at a tier the DEFAULT log level prints (Note/Alert/Error).
                // scripts/check-hw-verify.py enforces it.
                VRLog.Note("Tutorial", "Controls lesson button placement: " + geometry);
            }
            else if (_shownAt >= 0f
                     && Time.unscaledTime - _shownAt >= ContentDeadlineSeconds)
            {
                _geometryLogged = true;
                // HW-VERIFY: a standing hardware question is waiting on this line — it must stay
                // at a tier the DEFAULT log level prints (Note/Alert/Error).
                // scripts/check-hw-verify.py enforces it.
                VRLog.Note("Tutorial", "Controls lesson button placement could NOT be measured: "
                    + $"{ContentDeadlineSeconds:0} s after the box opened the panel's rect is "
                    + "still unresolved, so the button was never laid out. Whatever the headset "
                    + "shows, it was not placed by the anchoring in ControlsBox.Decorate.");
            }
        }
    }

    /// <summary>
    /// Where the button ACTUALLY landed, once the canvas has laid it out — the question 343 could
    /// not answer without a headset, asked of the layout instead of of a screenshot.
    ///
    /// <para>It compares the button's four corners against its parent's IN THE PARENT'S OWN LOCAL
    /// SPACE, so the answer does not depend on canvas scale, on the world-space conversion the mod
    /// applies to this window, or on which eye rendered it. Returns false while the parent has not
    /// been laid out yet (zero-width rect), so the caller simply asks again next frame rather than
    /// printing a number taken before the rebuild.</para>
    /// </summary>
    private static bool TryDescribeGeometry(out string text)
    {
        text = string.Empty;
        try
        {
            if (_actionButton == null)
                return false;
            var rect = (RectTransform)_actionButton.transform;
            if (rect.parent is not RectTransform content)
                return false;
            Rect panel = content.rect;
            if (panel.width < 1f || panel.height < 1f || rect.rect.width < 1f)
                return false;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 bl = content.InverseTransformPoint(corners[0]);   // bottom-left
            Vector3 tr = content.InverseTransformPoint(corners[2]);   // top-right
            bool inside = bl.x >= panel.xMin - 0.5f && bl.y >= panel.yMin - 0.5f
                          && tr.x <= panel.xMax + 0.5f && tr.y <= panel.yMax + 0.5f;
            float centreOffset = ((bl.x + tr.x) * 0.5f) - panel.center.x;
            var group = content.GetComponent<VerticalLayoutGroup>();
            int reserved = group != null && group.padding != null ? group.padding.bottom : -1;

            text = $"panel rect {panel.width:0}x{panel.height:0} (x {panel.xMin:0}..{panel.xMax:0}, "
                 + $"y {panel.yMin:0}..{panel.yMax:0}), button {rect.rect.width:0}x"
                 + $"{rect.rect.height:0} occupying x {bl.x:0}..{tr.x:0}, y {bl.y:0}..{tr.y:0} in "
                 + $"the panel's own space; reserved bottom padding {reserved}. ON THE PANEL: "
                 + $"{inside}; off the horizontal centre by {centreOffset:0.0}. If ON THE PANEL is "
                 + "False, or the offset is not near zero, the parent this is anchored to is not "
                 + "the rect that draws the window (ControlsBox.Decorate).";
            return true;
        }
        catch (Exception ex)
        {
            text = $"could not be measured: {ex.GetType().Name}: {ex.Message}";
            return true;
        }
    }

    private static void PushText()
    {
        if (_titleText != null && !string.Equals(_titleText.text, _title, StringComparison.Ordinal))
            _titleText.text = _title;
        if (_bodyText != null && !string.Equals(_bodyText.text, _body, StringComparison.Ordinal))
            _bodyText.text = _body;
        if (_actionLabelText != null
            && !string.Equals(_actionLabelText.text, _actionLabel, StringComparison.Ordinal))
            _actionLabelText.text = _actionLabel;
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
    /// ours it builds the lesson's ONE button into the game's box; for anything else it removes
    /// it, so a leftover can never ride along on a scripted message that reuses the same layout
    /// object (<c>LevelMessageUILayoutGroup.Show</c> hides and re-inits the shared prefabs,
    /// LevelMessageUILayoutGroup.cs:48-57).
    ///
    /// <para>WHERE THE BUTTON ATTACHES, AND WHY IT IS NOT WHERE THE CLOSE BUTTON SITS. 343 parented
    /// to <c>closeButton.transform.parent</c> and copied the close button's anchors, pivot and
    /// anchoredPosition verbatim; its own report said the landing spot could not be checked without
    /// hardware. It could not, and it was wrong: in .planning/debug/tutorial-buttons.jpg the copy
    /// STRADDLES the panel's bottom border — measured on the 3840×2160 shot, the panel's frame ends
    /// at y=1220 and the button spans y=1200…1267, so two thirds of it hangs below the window — and
    /// the second button, one width further left, is off the panel entirely.</para>
    ///
    /// <para>The one thing that shot does prove about the template is HORIZONTAL: the copy's centre
    /// landed at x=2008 against a panel centre of x=1995, i.e. the close button is already centred
    /// on the panel. So the parent is right and only the vertical placement and the second slot were
    /// wrong — which is why this now derives the geometry instead of copying it.</para>
    ///
    /// <para>THE PARENT IS THE RECT THE GAME ITSELF RESERVES BOTTOM PADDING IN:
    /// <c>title.transform.parent</c>, the <see cref="VerticalLayoutGroup"/> that holds the title,
    /// the page container and the pagination. <c>LevelMessageUILayout.Init</c> sets that group's
    /// <c>padding.bottom</c> to 40 (60 on a gamepad) for a <c>FixedLowerRight</c> box the moment it
    /// decides to show its own Continue button, and back to 0 when it does not
    /// (LevelMessageUILayout.cs:119-127) — so the game's own bottom affordance lives in the strip
    /// that padding opens up, INSIDE this rect. The hardware shot corroborates it: the strip below
    /// the body is drawn in the panel's own dark background all the way down to the gold frame, so
    /// this rect is the panel, not something inset from it.</para>
    ///
    /// <para>THE ANCHORING follows from that: anchor and pivot both at the rect's BOTTOM CENTRE
    /// (0.5, 0), <c>anchoredPosition</c> a positive <see cref="BottomMarginFraction"/> of the
    /// button's height straight up. Bottom-centre of the panel plus a margin is on the panel and
    /// centred, which is what was asked for, and it needs no number read off a prefab. The button
    /// is a POINT-anchored child with an explicit <c>sizeDelta</c>, never a stretch child whose
    /// anchors would then own its size.</para>
    ///
    /// <para>AND IT CARRIES A <see cref="LayoutElement"/> WITH <c>ignoreLayout</c>, because the
    /// parent is a layout group: without it the group would treat the button as another vertical
    /// element, position it in the flow and (with childForceExpandWidth) stretch it across the
    /// panel. Ignored, its own anchors decide, and the reserved padding keeps the body text off
    /// it.</para>
    ///
    /// <para>ONE MORE THING 343 GOT WRONG, and it is why the strip was too shallow: it wrote
    /// <c>group.padding.bottom = 40</c>, MUTATING the existing <see cref="RectOffset"/> in place.
    /// <c>LayoutGroup.padding</c> only marks the layout dirty from its SETTER
    /// (<c>SetProperty</c>), so an in-place field write can be read whenever the next rebuild
    /// happens to run and never at all if none does. A whole new RectOffset is assigned here, and
    /// the rebuild asked for explicitly.</para>
    /// </summary>
    internal static void Decorate(LevelMessageUILayout ui, CLevelMessage? message)
    {
        DestroyButtons();
        if (!Owns(message))
            return;

        ExtendedButton? template = ui.closeButton;
        // The panel's content rect — see the doc above for why this, and not the close button's
        // parent, is what the button is anchored to.
        RectTransform? content = ui.title != null && ui.title.transform.parent != null
            ? ui.title.transform.parent as RectTransform
            : null;
        Transform? parent = content;
        if (parent == null)
            parent = template != null ? template.transform.parent : null;
        if (parent == null)
            parent = ui.transform;

        _actionButton = BuildButton(parent, template, "GloomhavenVR.LessonAction",
            out _actionLabelText, out Vector2 size, () => Fire(_onAction, "ACTION"));

        // Reserve the strip the button sits in, exactly the way the game reserves one for its own
        // Continue button — otherwise the last line of the body would run underneath it.
        if (content != null && _actionButton != null)
            Reserve(content, Mathf.CeilToInt(size.y * (1f + 2f * BottomMarginFraction)));

        PushText();
        if (_actionButton == null && !_warnedNoButtons)
        {
            _warnedNoButtons = true;
            VRLog.Warn("Tutorial", "Controls lesson could not build its buttons into the tutorial "
                + "box. Every step still ends by itself when the player performs it, but a step "
                + "whose motion the room cannot offer can then only be left by switching the "
                + "lesson off ([Compat] ControlsLesson).");
        }
    }

    /// <summary>Open a bottom strip of <paramref name="bottom"/> units inside the panel's content
    /// rect for the button to sit in. Assigning a NEW <see cref="RectOffset"/> rather than writing
    /// <c>padding.bottom</c> is what makes the layout group notice — see <see cref="Decorate"/>.
    /// </summary>
    private static void Reserve(RectTransform content, int bottom)
    {
        var group = content.GetComponent<VerticalLayoutGroup>();
        if (group == null)
            return;
        RectOffset p = group.padding;
        if (p != null && p.bottom >= bottom)
            return;
        group.padding = p == null
            ? new RectOffset(0, 0, 0, bottom)
            : new RectOffset(p.left, p.right, p.top, bottom);
        LayoutRebuilder.MarkLayoutForRebuild(content);
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
    /// <param name="size">The size the button was actually given, so the caller can reserve a
    /// bottom strip that fits it.</param>
    private static GameObject? BuildButton(Transform parent, ExtendedButton? template, string name,
        out TextMeshProUGUI? label, out Vector2 size, Action onClick)
    {
        label = null;
        size = new Vector2(200f, 52f);
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
            // The template's RESOLVED rect, never its sizeDelta: on a stretch child sizeDelta is
            // an inset pair, not a size. Falls back only if the close button has never been laid
            // out (it is deactivated for every message that is not dismiss-triggered).
            if (templateRect != null && templateRect.rect.width > 1f
                && templateRect.rect.height > 1f)
                size = templateRect.rect.size;

            // BOTTOM CENTRE OF THE PANEL, a margin up — see Decorate for why this rather than a
            // copy of the template's anchors. Point anchors plus an explicit sizeDelta: the size
            // is the button's own, not something the anchors own.
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, size.y * BottomMarginFraction);
            // The parent is a layout group; without this it would lay the button out in the
            // vertical flow and stretch it to the panel's width.
            go.AddComponent<LayoutElement>().ignoreLayout = true;

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
            ControlsButtonFeedback? feedback = AttachFeedback(go, button, template);
            button.onClick.AddListener(() =>
            {
                // The sound first, then the step change — a click that advances the lesson also
                // rebuilds this button, and a sound asked for after that would be asked for by a
                // component that is already being destroyed.
                if (feedback != null)
                    feedback.PlayClick();
                onClick();
            });

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

    /// <summary>
    /// GIVE THE PLAIN BUTTON THE GAME'S BEHAVIOUR (user ruling 2026-09-02: <i>"Weiterhin geben die
    /// Tutorial-Buttons von unserem Teil kein Feedback (zB Ton) … daher sollen sich die buttons
    /// auch normal verhalten."</i>).
    ///
    /// <para>TWO HALVES, and only one of them needs code. An <c>ExtendedButton</c>'s hover and
    /// press LOOK are stock <see cref="Selectable"/> — its <c>DoStateTransition</c> override
    /// (ExtendedButton.cs:551) adds nothing to <c>base</c> — so copying <c>colors</c>,
    /// <c>spriteState</c> and <c>transition</c> off the template reproduces the visual half
    /// exactly. <c>Transition.Animation</c> is the one value that cannot be copied: it would drive
    /// an <c>Animator</c> our object does not have, and a Selectable set to Animation with no
    /// Animator simply shows nothing, so it degrades to ColorTint with the template's own colours.
    /// </para>
    ///
    /// <para>The SOUND half is <see cref="ControlsButtonFeedback"/>. The ids are resolved by the
    /// game's own rule (ExtendedButton.cs:418 — the button's own field, else its
    /// <c>AudioButtonProfile</c> asset's) applied to the box's close button, then to
    /// <c>UIInfoTools.Instance.generalAudioButtonProfile</c>, which is the asset the game treats
    /// as its default button sounds and which several of its own screens read directly.</para>
    ///
    /// <para>Returns null when the component could not be added; the button still works, silently,
    /// which is what it did before this existed.</para>
    /// </summary>
    private static ControlsButtonFeedback? AttachFeedback(GameObject go, Button button,
                                                          ExtendedButton? template)
    {
        try
        {
            if (template != null)
            {
                button.colors = template.colors;
                button.spriteState = template.spriteState;
                button.transition = template.transition == Selectable.Transition.Animation
                    ? Selectable.Transition.ColorTint
                    : template.transition;
            }

            AudioButtonProfile? templateProfile = template != null ? template.audioProfile : null;
            UIInfoTools? tools = UIInfoTools.Instance;
            AudioButtonProfile? general = tools != null ? tools.generalAudioButtonProfile : null;

            var feedback = go.AddComponent<ControlsButtonFeedback>();
            feedback.EnterItem = Pick(template != null ? template.mouseEnterAudioItem : null,
                templateProfile != null ? templateProfile.mouseEnterAudioItem : null,
                general != null ? general.mouseEnterAudioItem : null);
            feedback.ExitItem = Pick(template != null ? template.mouseExitAudioItem : null,
                templateProfile != null ? templateProfile.mouseExitAudioItem : null,
                general != null ? general.mouseExitAudioItem : null);
            feedback.DownItem = Pick(template != null ? template.mouseDownAudioItem : null,
                templateProfile != null ? templateProfile.mouseDownAudioItem : null,
                general != null ? general.mouseDownAudioItem : null);
            feedback.UpItem = Pick(template != null ? template.mouseUpAudioItem : null,
                templateProfile != null ? templateProfile.mouseUpAudioItem : null,
                general != null ? general.mouseUpAudioItem : null);
            feedback.ClickItem = Pick(template != null ? template.mouseClickAudioItem : null,
                templateProfile != null ? templateProfile.mouseClickAudioItem : null,
                general != null ? general.mouseClickAudioItem : null);

            // The hover scale, if the template carries one. A factor at or below 1 means the
            // template does not grow on hover, and neither do we.
            if (template != null && template.highlightScaleFactor > 1f)
            {
                feedback.HighlightScale = template.highlightScaleFactor;
                feedback.AnimationDuration = template.animationDuration > 0f
                    ? template.animationDuration
                    : 0.1f;
            }
            return feedback;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", "Controls lesson could not give its button the game's own "
                + $"feedback: {ex.GetType().Name}: {ex.Message}. The button still works; it is "
                + "silent.");
            return null;
        }
    }

    /// <summary>The game's own id resolution (ExtendedButton.cs:418): the widget's own field wins,
    /// then its profile asset's, then the general profile's. Empty means "play nothing", which is
    /// a real answer — several of the game's buttons have no hover sound at all.</summary>
    private static string Pick(string? own, string? profile, string? general)
    {
        if (!string.IsNullOrEmpty(own))
            return own!;
        if (!string.IsNullOrEmpty(profile))
            return profile!;
        return general ?? string.Empty;
    }

    private static void DestroyButtons()
    {
        if (_actionButton != null)
            UnityEngine.Object.Destroy(_actionButton);
        _actionButton = null;
        _actionLabelText = null;
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
        _onAction = null;
        _shownAt = -1f;
        _contentSeen = false;
        _geometryLogged = false;
        _title = _body = _actionLabel = string.Empty;
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
