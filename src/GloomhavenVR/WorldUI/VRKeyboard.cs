using System;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Patches;
using Script.GUI.Controller;
using Script.GUI.Controller.Keyboard;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Text entry in VR: an on-screen keyboard that appears when the player clicks into a text field
/// and goes away when they click anywhere else, so a player with no physical keyboard in reach can
/// still name a party.
///
/// <para>CLICKS, NOT FOCUS — and that distinction cost two attempts. Focus looks like the natural
/// signal and is the wrong one in both directions: pressing a key on the keyboard hands the
/// EventSystem's selection to that key, so the field deactivates and a focus-driven keyboard closes
/// on the very first letter; and a field can hold focus long after the player has clicked away, so
/// focus also fails to say when to close. <see cref="NoticeClick"/> owns opening and closing;
/// <see cref="Tick"/> only watches for the two ways the keyboard can vanish without a click (the
/// game's Escape, and its window closing) and for a field the game focuses by itself.</para>
///
/// <para>THE GAME ALREADY HAS THE KEYBOARD. <c>UIKeyboard</c> exists for console controller input —
/// a localized key layout (one <c>KeyboardConfig</c> per language), a pool of <c>UIKeyboardKey</c>
/// buttons, and a single <c>OnSelectedKeyCode</c> event. Building one would have meant drawing a
/// second keyboard beside the game's, in the mod's own art, in one language. This drives the game's
/// instead: same look, same localization, and it inherits every layout the game ships.</para>
///
/// <para>MOUSE MODE CUTS BOTH WAYS — this is the crux, and the first attempt only saw one half.
/// The mod forces MOUSE mode (<see cref="InputModeGuard"/>) so the laser and fingertip can drive
/// uGUI. That is what makes the keys clickable at all: <c>UIKeyboardKey.Awake</c> wires
/// <c>button.onClick</c> only when <c>InputManager.GamePadInUse</c> is false. It is ALSO why the
/// popup would not stay up — <c>ControllerInputElement.OnEnable</c> hides the keyboard outright in
/// mouse mode, so <c>Show()</c> undid itself inside its own call. <see cref="KeyboardAutoHideBlock"/>
/// suppresses that one auto-hide, for this one keyboard instance. Neither half can be had by
/// flipping the flag; both come from it.</para>
///
/// <para>ONE WRITER FOR THE TEXT. The popup ships its own <c>ControllerInputKeyboard</c>, already
/// listening on <c>OnSelectedKeyCode</c> and typing into the very field the player is editing —
/// leaving it attached would insert every character twice. Its listener is therefore parked while
/// the mod owns the keyboard and restored on release, which also lets
/// <see cref="WorldUIConfig.KeyboardAutoCase"/> mean something: the game's handler would emit the
/// raw upper-case KeyCode names and no setting could change it.</para>
///
/// <para>NOTHING IS DRAWN AND NOTHING IS PLACED. The keyboard is uGUI inside the game's own canvas,
/// so it reaches the player through the flat screen exactly like the window that owns the text
/// field. No world-space panel, no placement, no new input path.</para>
///
/// <para>REVERSIBILITY. Three writes, all on objects the game owns and all undone by
/// <see cref="Detach"/> / <see cref="Shutdown"/>: <c>Show()</c>/<c>Hide()</c>, one listener added,
/// one listener parked. Desktop play never reaches any of it.</para>
/// </summary>
internal static class VRKeyboard
{
    /// <summary>The field the keyboard is currently serving, or null when it is hidden.</summary>
    private static TMP_InputField? _field;

    /// <summary>The keyboard we showed. Held so the exact same one is hidden again.</summary>
    private static UIKeyboard? _keyboard;

    /// <summary>
    /// The popup's own input handler, when it has one. Held for two reasons: its listener is parked
    /// while we type (see the class remarks), and <see cref="OwnsKeyboardOf"/> answers the
    /// auto-hide patch from it.
    /// </summary>
    private static ControllerInputKeyboard? _native;

    /// <summary>
    /// A field whose keyboard was dismissed. Held until that field loses focus, so a dismissal
    /// sticks instead of being undone a frame later by the fallback opener below. A fresh click
    /// into the field clears it — dismissing is not the same as refusing forever.
    /// </summary>
    private static TMP_InputField? _refused;

    private static UnityAction<KeyCode>? _listener;
    private static UnityAction<KeyCode>? _parked;
    private static bool _probed;

    /// <summary>
    /// How often the fallback sweep over every live text field may run. The sweep is
    /// <c>FindObjectsOfType</c> and cost 2.3 ms EVERY FRAME in the first version — 180 ms/s of pure
    /// searching for a keyboard nobody had asked for. It is only a fallback (a click selects the
    /// field, and an open keyboard re-checks its own field directly), so a fifth of a second between
    /// tries is imperceptible and 60x cheaper.
    ///
    /// <para>THROTTLING IT WAS NOT ENOUGH, and the hardware log says so plainly: measured INSIDE a
    /// loaded scenario the same call is ~18 ms, not 2.3 ms, so five runs a second cost 86 ms/s and
    /// dropped a frame every single time (90 Hz budget: 11.1 ms). <see cref="InputFieldFocusWatch"/>
    /// now delivers activation as an event and this interval only governs the degraded path where
    /// that patch could not be applied.</para>
    /// </summary>
    private const float SweepInterval = 0.2f;

    private static float _lastSweep = float.NegativeInfinity;

    /// <summary>True while the mod is showing a keyboard for a field.</summary>
    internal static bool IsShowing => _field != null && _keyboard != null;

    /// <summary>
    /// Asked by <see cref="KeyboardHideSuppressor"/>: is this the handler belonging to the keyboard
    /// the mod is currently showing? Scoping by instance keeps every other keyboard in the game
    /// exactly as it is.
    /// </summary>
    internal static bool OwnsKeyboardOf(ControllerInputKeyboard handler)
        => IsShowing && handler != null && ReferenceEquals(handler.keyboard, _keyboard);

    /// <summary>
    /// Called every frame from <c>WorldUIModule.Update</c> (through TickGuard, so a throw here can
    /// never starve input).
    /// </summary>
    internal static void Tick()
    {
        // [Keyboard] Enabled is GONE (user ruling 2026-08-13): the on-screen keyboard comes up for
        // every focused text field, always. Its OFF meant "text fields need a real keyboard",
        // which in a headset is a text field you cannot fill — and naming a party is a REQUIRED
        // step of starting a campaign. The keyboard only ever appears while a field has focus, so
        // there was nothing to switch off but the ability to proceed.
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        // Registered here rather than next to the first attach: the watch has to be listening BEFORE
        // the game focuses anything, or the very activation it exists to catch happens unobserved.
        // Idempotent and cheap after the first call (one bool).
        InputFieldFocusWatch.EnsureRegistered();

        // FOCUS IS NOT WHAT KEEPS THE KEYBOARD OPEN — that was the second bug in a row, and this is
        // the whole reason for the split below. Clicking a key moves the EventSystem's selection to
        // that key, the field is deselected and deactivates itself, and a keyboard that closes when
        // its field loses focus therefore closes on the first key the player presses. Opening and
        // closing are click-driven (see NoticeClick); Tick only watches for the ways the keyboard can
        // disappear WITHOUT a click, and keeps the field usable.
        if (IsShowing)
        {
            if (_keyboard == null || !_keyboard.IsActive)
            {
                // The popup is an EscapableGameObject: the game's own Escape deactivates it.
                _refused = _field;
                Detach("closed by the game (Escape)");
                return;
            }

            if (_field == null || !_field.isActiveAndEnabled)
            {
                Detach("the text field went away (its window closed)");
                return;
            }

            // AND THE FIELD IS DELIBERATELY LEFT UNFOCUSED. Re-activating it looks like the obvious
            // improvement — a blinking caret in the field being typed into — and it destroys typing:
            // on desktop ActivateInputFieldInternal calls OnFocus(), which does SelectAll() because
            // TMP's onFocusSelectAll defaults to true. Re-focusing after every key would therefore
            // select the whole text before every key, and each new character would REPLACE the field
            // instead of extending it — the field could never hold more than one letter.
            //
            // Nothing needs the focus: ProcessEvent goes straight to KeyPressed with no focus check,
            // DeactivateInputField leaves the caret where it was, and ForceLabelUpdate (called after
            // every key) is what re-hides the placeholder. Losing focus is harmless here; only the
            // old close-on-focus-loss rule made it look otherwise.
            return;
        }

        // The refusal is answered by the refused field itself, not by the search below: the search is
        // throttled, so "found nothing" also means "did not look this frame", and reading that as
        // "focus is gone" would quietly lift the refusal a fifth of a second after a dismissal.
        if (_refused != null)
        {
            if (_refused.isFocused && _refused.IsInteractable())
                return;
            _refused = null; // that field is done; a later click may open the keyboard again
        }

        // Fallback opener, for a field the game focuses by itself rather than by a click. A click
        // never gets here — NoticeClick has already opened the keyboard by then.
        TMP_InputField? focused = FindFocusedField();
        if (focused != null)
            Attach(focused);
    }

    /// <summary>
    /// A click landed on <paramref name="target"/> (null = on nothing). Called from
    /// <c>FlatScreen.DirectClick</c>, which is where BOTH pointer paths — laser trigger and
    /// fingertip poke — deliver their clicks, before the click itself is executed.
    ///
    /// <para>This is the whole open/close policy, and it is stated in clicks because that is what
    /// the player is doing: click into a text field and the keyboard appears; click anywhere that is
    /// neither the keyboard nor that field and it goes away. Focus cannot express it — pressing a
    /// key takes focus off the field, and the field can keep focus long after the player has moved
    /// on.</para>
    /// </summary>
    internal static void NoticeClick(GameObject? target)
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        TMP_InputField? field = target != null ? target.GetComponentInParent<TMP_InputField>() : null;

        if (field != null && field.IsInteractable() && !field.readOnly)
        {
            _refused = null; // an explicit click into a field always wins over an earlier dismissal
            if (!ReferenceEquals(field, _field))
                Attach(field);
            return;
        }

        if (!IsShowing)
            return;

        // Inside the keyboard: a key, its background, its padding. All of it keeps the keyboard up.
        if (target != null && _keyboard != null && target.transform.IsChildOf(_keyboard.transform))
            return;

        _refused = _field;
        Detach("clicked away from the keyboard");
    }

    /// <summary>
    /// A field the game has focused on its own, for the fallback opener — a click never reaches
    /// here, because <see cref="NoticeClick"/> has already opened the keyboard by then.
    ///
    /// <para>Two sources, because neither alone is enough. <c>EventSystem.currentSelectedGameObject</c>
    /// is what a click sets, but selection survives the field being closed, so it can name a field
    /// nobody is editing. <c>TMP_InputField.isFocused</c> is the field's own answer and is
    /// authoritative, so it decides; the selection only supplies the candidate cheaply, and the
    /// sweep behind it is a throttled fallback for the case where the game has re-selected some row
    /// or container around the field.</para>
    /// </summary>
    private static TMP_InputField? FindFocusedField()
    {
        EventSystem? events = EventSystem.current;
        GameObject? selected = events != null ? events.currentSelectedGameObject : null;

        if (selected != null)
        {
            var onSelected = selected.GetComponent<TMP_InputField>();
            if (onSelected != null && onSelected.isFocused && onSelected.IsInteractable())
                return onSelected;
        }

        // PUSHED, NOT POLLED. While InputFieldFocusWatch is live it holds the field TMP last
        // activated, which is exactly the set the sweep below could ever have returned (isFocused is
        // TMP's m_AllowInput, written only on the ActivateInputField path). The same two acceptance
        // tests still decide, so a field that has gone stale since is rejected as before — and the
        // sweep, the mod's single most expensive step at 86 ms/s inside a scenario, never runs.
        if (InputFieldFocusWatch.Installed)
        {
            TMP_InputField? activated = InputFieldFocusWatch.Focused;
            return activated != null && activated.isFocused && activated.IsInteractable()
                ? activated
                : null;
        }

        // Fallback for a TMP the watch could not patch: the original throttled sweep, unchanged.
        float now = Time.unscaledTime;
        if (now - _lastSweep < SweepInterval)
            return null;
        _lastSweep = now;

        foreach (TMP_InputField candidate in UnityEngine.Object.FindObjectsOfType<TMP_InputField>())
        {
            if (candidate != null && candidate.isFocused && candidate.IsInteractable())
                return candidate;
        }

        return null;
    }

    // ==========================================================================================
    //  Attach / detach
    // ==========================================================================================

    private static void Attach(TMP_InputField field)
    {
        // Clicking straight from one field into another: release the first, or its listener stays
        // wired and its popup stays up.
        if (IsShowing)
            Detach("a different field was clicked");

        UIKeyboard? keyboard = FindKeyboard(field);
        ControllerInputKeyboard? native = keyboard != null ? FindNativeHandler(keyboard) : null;
        Probe(field, keyboard, native);

        if (keyboard == null)
        {
            VRLog.Warn("WorldUI", "VR keyboard: a text field took focus but no UIKeyboard exists in "
                                  + "the scene to show for it — text entry needs a physical keyboard "
                                  + "here. The PROBE line above lists what was searched.");
            _refused = field; // say it once per focus, not once per sweep
            return;
        }

        // Ownership must be established BEFORE the popup is activated: the auto-hide fires from
        // inside SetActive, and the patch decides by asking OwnsKeyboardOf.
        _field = field;
        _keyboard = keyboard;
        _native = native;
        KeyboardAutoHideBlock.EnsureRegistered();

        _listener = code => TickGuard.Run("VRKeyboard.Key", () => OnKey(code), "WorldUI");
        keyboard.OnSelectedKeyCode.AddListener(_listener);
        SetShown(keyboard, true);

        // AFTER activating, never before: the popup starts inactive, so ControllerInputKeyboard.Awake
        // — which is where its key listener is added — has not run until this very SetActive. Parking
        // first would have removed a listener that did not exist yet, and Awake would then have added
        // it back, restoring the double character this is here to prevent.
        ParkNativeTyping();

        // The post-condition, logged either way: the whole bug was Show() silently undoing itself,
        // so "did it stay up" is the one fact worth stating outright.
        if (keyboard.IsActive)
        {
            VRLog.Info("WorldUI", $"VR keyboard: SHOWN and still up for '{field.name}' "
                                  + $"(keyboard '{keyboard.name}'"
                                  + (native != null ? ", native handler parked" : ", no native handler")
                                  + "). Keys route through the field's own ProcessEvent, so the game "
                                  + "sees ordinary typing.");
        }
        else
        {
            // Not reachable by either known closer: the navigation state is no longer entered at
            // all, and the mouse-mode auto-hide is suppressed by instance. So if this fires there is
            // a THIRD closer, and the message has to say what has already been ruled out — followed
            // by _refused, so the finding is reported once instead of five times a second.
            VRLog.Warn("WorldUI", $"VR keyboard: activated '{keyboard.name}' for '{field.name}' and it "
                                  + "went inactive again inside the same call. Both known closers are "
                                  + "out (UIKeyboard.Show/OnShow is bypassed, and "
                                  + "ControllerInputKeyboard's mouse-mode auto-hide is patched out for "
                                  + "this instance), so a third one owns it — check for a "
                                  + "KeyboardAutoHideBlock warning above first, then for a "
                                  + "game-side log line between the escapable add and remove.");
            _refused = field;
            Detach("it would not stay open");
        }
    }

    private static void Detach(string reason)
    {
        if (_keyboard != null)
        {
            if (_listener != null)
                _keyboard.OnSelectedKeyCode.RemoveListener(_listener);
            RestoreNativeTyping();
            SetShown(_keyboard, false);
            VRLog.Info("WorldUI", $"VR keyboard: hidden ({reason}).");
        }

        _keyboard = null;
        _native = null;
        _listener = null;
        _parked = null;
        _field = null;
    }

    /// <summary>
    /// Activate the popup DIRECTLY instead of through <c>UIKeyboard.Show()</c>/<c>Hide()</c>, which
    /// would additionally fire <c>OnShow</c>/<c>OnHide</c> — and those enter and leave a gamepad
    /// NAVIGATION STATE (<c>KeyboardStateSwitcher</c> → <c>UINavigation.StateMachine.Enter</c> →
    /// <c>UiNavigationManager.SetCurrentRoot</c>). VR has no use for it: the laser and the fingertip
    /// drive uGUI through mouse mode, and handing selection to a controller-navigation root is a way
    /// to have it taken away from the pointer.
    ///
    /// <para>It also settles a diagnosis that the source alone could not. The log proved something
    /// deactivated the popup synchronously inside <c>Show()</c>, but two candidates fit: the
    /// mouse-mode auto-hide (<see cref="KeyboardAutoHideBlock"/>) and that navigation state. The
    /// popup's layout is scene data, so which of the two owns it is not readable from the decompiled
    /// code. Not calling <c>Show()</c> removes the second candidate outright rather than betting on
    /// the first, and the two measures are independent — whichever it was, the popup stays up.</para>
    ///
    /// <para>Symmetry is the requirement: a state never entered must never be exited, so hiding
    /// bypasses <c>Hide()</c> the same way.</para>
    /// </summary>
    private static void SetShown(UIKeyboard keyboard, bool shown)
    {
        if (keyboard.gameObject.activeSelf != shown)
            keyboard.gameObject.SetActive(shown);
    }

    /// <summary>
    /// Take the popup's own handler off the key event while we own it, so each key produces exactly
    /// one character. A freshly built delegate over the same target and method removes the
    /// registered one: <c>UnityEvent</c> matches listeners by target and method, not by delegate
    /// identity.
    /// </summary>
    private static void ParkNativeTyping()
    {
        if (_native == null || _keyboard == null)
            return;

        try
        {
            var handler = new UnityAction<KeyCode>(_native.ProcessKeyCode);
            _keyboard.OnSelectedKeyCode.RemoveListener(handler);
            _parked = handler;
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR keyboard: could not park the native key handler ({e.Message}) "
                                  + "— characters may arrive twice.");
            _parked = null;
        }
    }

    private static void RestoreNativeTyping()
    {
        if (_parked == null || _keyboard == null)
            return;

        try
        {
            _keyboard.OnSelectedKeyCode.AddListener(_parked);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR keyboard: could not restore the native key handler ({e.Message}).");
        }
    }

    /// <summary>
    /// A keyboard to show. Preferred order: one under the same window as the field (the authored
    /// pairing — the create-game step ships its own), then any keyboard in the scene.
    /// </summary>
    private static UIKeyboard? FindKeyboard(TMP_InputField field)
    {
        Transform? node = field.transform;
        while (node != null)
        {
            UIKeyboard? nearby = node.GetComponentInChildren<UIKeyboard>(true);
            if (nearby != null)
                return nearby;
            node = node.parent;
        }

        UIKeyboard[] all = UnityEngine.Object.FindObjectsOfType<UIKeyboard>(true);
        return all.Length > 0 ? all[0] : null;
    }

    /// <summary>
    /// The <c>ControllerInputKeyboard</c> bound to this keyboard, if the window ships one. Matched by
    /// its own <c>keyboard</c> reference rather than by position in the hierarchy, so it is found
    /// wherever the scene author put it.
    /// </summary>
    private static ControllerInputKeyboard? FindNativeHandler(UIKeyboard keyboard)
    {
        Transform? node = keyboard.transform;
        while (node != null)
        {
            foreach (ControllerInputKeyboard candidate in
                     node.GetComponentsInChildren<ControllerInputKeyboard>(true))
            {
                if (candidate != null && ReferenceEquals(candidate.keyboard, keyboard))
                    return candidate;
            }
            node = node.parent;
        }
        return null;
    }

    // ==========================================================================================
    //  Typing
    // ==========================================================================================

    /// <summary>
    /// One key. Characters go in through the field's own <c>ProcessEvent</c> — the same path the
    /// game's console keyboard uses — so validation, the character limit and the on-change events
    /// all behave as if the player had typed.
    /// </summary>
    private static void OnKey(KeyCode code)
    {
        TMP_InputField? field = _field;
        if (field == null)
            return;

        switch (code)
        {
            case KeyCode.Backspace:
            case KeyCode.Delete:
                Backspace(field);
                return;

            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                // Let the field's own submit run, then get out of the way: the player is done.
                // _refused, because the field may well keep focus through its own submit.
                field.onEndEdit?.Invoke(field.text);
                _refused = field;
                Detach("Enter");
                return;

            case KeyCode.Escape:
                _refused = field;
                Detach("Escape");
                return;
        }

        string value = KeyCodeConverter.ConvertToValue(code);
        if (string.IsNullOrEmpty(value))
            return;

        value = ApplyCase(field, value);
        foreach (char c in value)
            field.ProcessEvent(new Event { character = c });

        field.ForceLabelUpdate();
    }

    /// <summary>
    /// Delete the last character directly rather than through a synthesized backspace event.
    /// <c>ProcessEvent</c> needs a caret position TMP only maintains while the field is genuinely
    /// being edited, and a clicked key moves focus around; editing the text is unambiguous. This is
    /// what the game's own handler does for its Delete key too.
    /// </summary>
    private static void Backspace(TMP_InputField field)
    {
        string text = field.text;
        if (text.Length == 0)
            return;

        field.text = text.Substring(0, text.Length - 1);
        field.caretPosition = field.text.Length;
        field.ForceLabelUpdate();
    }

    /// <summary>
    /// SENTENCE CASE, and only because the game's keyboard has no shift key: it emits KeyCodes and
    /// <c>KeyCodeConverter</c> maps letters to their upper-case name, so typing straight through
    /// produces "MY BRAVE PARTY". Capitalising the first letter of each word and lowering the rest
    /// gives "My Brave Party" — right for the names these fields hold.
    ///
    /// <para>It is a config switch because it IS a guess about intent: off, every letter arrives as
    /// the game's own keyboard produces it.</para>
    /// </summary>
    private static string ApplyCase(TMP_InputField field, string value)
    {
        if (!WorldUIConfig.KeyboardAutoCase.Value || value.Length != 1 || !char.IsLetter(value[0]))
            return value;

        string text = field.text;
        bool startOfWord = text.Length == 0 || char.IsWhiteSpace(text[text.Length - 1]);
        return startOfWord
            ? value.ToUpperInvariant()
            : value.ToLowerInvariant();
    }

    // ==========================================================================================
    //  Diagnostics
    // ==========================================================================================

    /// <summary>
    /// One-shot dump of what is actually there. The keyboard's layout is authored data — the key
    /// pool, the fixed keys and the per-language configs are all serialized — so what it offers
    /// cannot be read from the decompiled source. This says it, once, so the next decision (whether
    /// a shift key can be added to the layout, and where) is made against the real thing.
    /// </summary>
    private static void Probe(TMP_InputField field, UIKeyboard? keyboard, ControllerInputKeyboard? native)
    {
        if (_probed)
            return;
        _probed = true;

        try
        {
            var sb = new StringBuilder();
            sb.Append("VR keyboard PROBE — field '").Append(field.name).Append("' (limit ");
            sb.Append(field.characterLimit).Append(", line type ").Append(field.lineType).Append(").");

            TMP_InputField[] fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true);
            sb.Append("\n  text fields in the scene: ").Append(fields.Length);
            for (int i = 0; i < fields.Length && i < 12; i++)
            {
                sb.Append("\n    ").Append(Path(fields[i].transform));
                if (!fields[i].gameObject.activeInHierarchy)
                    sb.Append("  (inactive)");
            }

            UIKeyboard[] boards = UnityEngine.Object.FindObjectsOfType<UIKeyboard>(true);
            sb.Append("\n  UIKeyboard instances: ").Append(boards.Length);
            for (int i = 0; i < boards.Length; i++)
            {
                sb.Append("\n    ").Append(Path(boards[i].transform));
                sb.Append(boards[i].IsActive ? "  (shown)" : "  (hidden)");
                DescribeKeys(sb, boards[i]);
            }

            // Where the auto-hide comes from, and which field the parked handler would have typed
            // into — the two facts the double-insert and the zero-frame popup both turned on.
            var handlers = UnityEngine.Object.FindObjectsOfType<ControllerInputKeyboard>(true);
            sb.Append("\n  ControllerInputKeyboard instances: ").Append(handlers.Length);
            foreach (ControllerInputKeyboard handler in handlers)
            {
                if (handler == null)
                    continue;
                sb.Append("\n    ").Append(Path(handler.transform)).Append("  → field '");
                sb.Append(handler.m_KeyboardInputField == null
                    ? "none"
                    : handler.m_KeyboardInputField.name).Append('\'');
            }

            sb.Append("\n  chosen keyboard: ").Append(keyboard == null ? "NONE" : Path(keyboard.transform));
            sb.Append("\n  chosen native handler: ")
              .Append(native == null ? "NONE (mod types on its own)" : Path(native.transform));
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR keyboard: probe threw ({e.Message}) — the keyboard still works.");
        }
    }

    /// <summary>The keys a keyboard actually offers — the question a shift key depends on.</summary>
    private static void DescribeKeys(StringBuilder sb, UIKeyboard keyboard)
    {
        try
        {
            var codes = new List<string>(48);
            foreach (UIKeyboardKey key in keyboard.GetComponentsInChildren<UIKeyboardKey>(true))
            {
                if (key != null)
                    codes.Add(key.name);
            }
            sb.Append("\n      keys (").Append(codes.Count).Append("): ")
              .Append(string.Join(" ", codes.ToArray()));
        }
        catch (Exception e)
        {
            sb.Append("\n      keys: unreadable (").Append(e.GetType().Name).Append(')');
        }
    }

    private static string Path(Transform node)
    {
        var sb = new StringBuilder(64);
        sb.Append(node.name);
        for (Transform? p = node.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    /// <summary>Hide the keyboard and drop every listener (hot-reload teardown; never throws).</summary>
    internal static void Shutdown()
    {
        try
        {
            Detach("shutdown");
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR keyboard: teardown threw ({e.Message}).");
        }
        _refused = null;
        _probed = false;
        _lastSweep = float.NegativeInfinity;
        InputFieldFocusWatch.Clear();
    }
}
