using System;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using Script.GUI.Controller.Keyboard;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Text entry in VR: an on-screen keyboard that appears by itself whenever a text field takes
/// focus, so a player with no physical keyboard in reach can still name a party.
///
/// <para>THE GAME ALREADY HAS THE KEYBOARD. <c>UIKeyboard</c> exists for console controller input —
/// a localized key layout (one <c>KeyboardConfig</c> per language), a pool of <c>UIKeyboardKey</c>
/// buttons, and a single <c>OnSelectedKeyCode</c> event. Building one would have meant drawing a
/// second keyboard beside the game's, in the mod's own art, in one language. This drives the game's
/// instead: same look, same localization, and it inherits every layout the game ships.</para>
///
/// <para>WHY IT NEVER APPEARS ON ITS OWN. The game shows it from
/// <c>ControllerInputKeyboard.OnEnabledControllerControl</c> — i.e. only in gamepad mode. The mod
/// forces MOUSE mode (<see cref="InputModeGuard"/>) so the laser and fingertip can drive uGUI, which
/// means that path is never taken. The same choice is what makes the keys clickable at all:
/// <c>UIKeyboardKey.Awake</c> only wires <c>button.onClick</c> when <c>InputManager.GamePadInUse</c>
/// is false. Mouse mode is therefore both the reason it stays hidden and the reason it works once
/// shown — this class only has to decide WHEN.</para>
///
/// <para>NOTHING IS DRAWN AND NOTHING IS PLACED. The keyboard is uGUI inside the game's own canvas,
/// so it reaches the player through the flat screen exactly like the window that owns the text
/// field. No world-space panel, no placement, no new input path.</para>
///
/// <para>REVERSIBILITY. The only writes are <c>Show()</c>/<c>Hide()</c> on an object the game owns
/// and one listener on its event, both undone by <see cref="Detach"/> and by
/// <see cref="Shutdown"/>. Desktop play never reaches any of it.</para>
/// </summary>
internal static class VRKeyboard
{
    /// <summary>The field the keyboard is currently serving, or null when it is hidden.</summary>
    private static TMP_InputField? _field;

    /// <summary>The keyboard we showed. Held so the exact same one is hidden again.</summary>
    private static UIKeyboard? _keyboard;

    private static UnityEngine.Events.UnityAction<KeyCode>? _listener;
    private static bool _probed;

    /// <summary>True while the keyboard is up for a field.</summary>
    internal static bool IsOpen => _field != null && _keyboard != null;

    /// <summary>
    /// Called every frame from <c>WorldUIModule.Update</c> (through TickGuard, so a throw here can
    /// never starve input).
    /// </summary>
    internal static void Tick()
    {
        if (!WorldUIConfig.KeyboardEnabled.Value)
        {
            if (IsOpen)
                Detach("switched off in the config");
            return;
        }

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        TMP_InputField? focused = FindFocusedField();

        if (focused == null)
        {
            if (IsOpen)
                Detach("the text field lost focus");
            return;
        }

        if (ReferenceEquals(focused, _field))
            return;

        Attach(focused);
    }

    /// <summary>
    /// The field the player is typing into.
    ///
    /// <para>Two sources, because neither alone is enough. <c>EventSystem.currentSelectedGameObject</c>
    /// is what a click sets, but selection survives the field being closed, so it can name a field
    /// nobody is editing. <c>TMP_InputField.isFocused</c> is the field's own answer and is authoritative,
    /// so it decides; the selection is only used to find the candidate cheaply.</para>
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

        // A field can hold focus without being the selected object (the game re-selects rows and
        // containers around it). One sweep over the live fields settles it; there are a handful.
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
        Detach("a different field took focus");

        UIKeyboard? keyboard = FindKeyboard(field);
        Probe(field, keyboard);

        if (keyboard == null)
        {
            VRLog.Warn("WorldUI", "VR keyboard: a text field took focus but no UIKeyboard exists in "
                                  + "the scene to show for it — text entry needs a physical keyboard "
                                  + "here. The PROBE line above lists what was searched.");
            return;
        }

        _field = field;
        _keyboard = keyboard;
        _listener = code => TickGuard.Run("VRKeyboard.Key", () => OnKey(code), "WorldUI");
        keyboard.OnSelectedKeyCode.AddListener(_listener);
        keyboard.Show();

        VRLog.Info("WorldUI", $"VR keyboard: shown for '{field.name}' (keyboard '{keyboard.name}'). "
                              + "Keys route through the field's own ProcessEvent, so the game sees "
                              + "ordinary typing.");
    }

    private static void Detach(string reason)
    {
        if (_keyboard != null)
        {
            if (_listener != null)
                _keyboard.OnSelectedKeyCode.RemoveListener(_listener);
            _keyboard.Hide();
            VRLog.Info("WorldUI", $"VR keyboard: hidden ({reason}).");
        }

        _keyboard = null;
        _listener = null;
        _field = null;
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
                field.onEndEdit?.Invoke(field.text);
                Detach("Enter");
                return;

            case KeyCode.Escape:
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
    /// being edited, and a clicked key moves focus around; editing the text is unambiguous.
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
    private static void Probe(TMP_InputField field, UIKeyboard? keyboard)
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

            sb.Append("\n  chosen: ").Append(keyboard == null ? "NONE" : Path(keyboard.transform));
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
        _probed = false;
    }
}
