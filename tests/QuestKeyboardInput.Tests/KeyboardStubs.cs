using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> All = new();
        public string name = "";
        protected Object() { All.Add(this); }
        public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Object
            => All.OfType<T>().Where(x => includeInactive || x is not Component c || c.gameObject.activeInHierarchy).ToArray();
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string label = "") { name = label; transform = new Transform(this); }
        public void SetActive(bool value) { activeSelf = value; }
        public T? GetComponent<T>() where T : Component => All.OfType<T>().FirstOrDefault(c => c.gameObject == this);
        public T? GetComponentInParent<T>() where T : Component => transform.GetComponentInParent<T>();
    }
    public class Transform : Object
    {
        public readonly GameObject gameObject;
        public Transform? parent;
        public Transform(GameObject host) { gameObject = host; name = host.name; }
        public bool IsChildOf(Transform ancestor) { for (Transform? t = this; t != null; t = t.parent) if (t == ancestor) return true; return false; }
        public T? GetComponentInChildren<T>(bool includeInactive) where T : Component
            => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => All.OfType<T>().Where(c => c.transform.IsChildOf(this) && (includeInactive || c.gameObject.activeInHierarchy)).ToArray();
        public T? GetComponentInParent<T>() where T : Component
        { for (Transform? node = this; node != null; node = node.parent) { var value = node.gameObject.GetComponent<T>(); if (value != null) return value; } return null; }
    }
    public class Component : Object
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public T? GetComponentInParent<T>() where T : Component => transform.GetComponentInParent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => transform.GetComponentsInChildren<T>(includeInactive);
    }
    public class MonoBehaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public enum RuntimePlatform { Android, WindowsPlayer, MetroPlayerX86, MetroPlayerX64, MetroPlayerARM }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.Android; }
    public static class TouchScreenKeyboard { public static bool isSupported = true; }
    public static class Time { public static float unscaledTime; }
    public enum KeyCode { A, B, C, X, Alpha1, Alpha2, Space, Backspace, Delete, Return, KeypadEnter, Escape }
    public class Event { public char character; }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public class UnityEvent<T>
    {
        private readonly List<UnityAction<T>> handlers = new();
        public int ListenerCount => handlers.Count;
        public void AddListener(UnityAction<T> handler) => handlers.Add(handler);
        public void RemoveListener(UnityAction<T> handler) => handlers.RemoveAll(h => h == handler);
        public void Invoke(T value) { foreach (var h in handlers.ToArray()) h(value); }
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem { public static EventSystem? current; public UnityEngine.GameObject? currentSelectedGameObject; }
}
namespace TMPro
{
    public class TMP_InputField : UnityEngine.MonoBehaviour
    {
        string value = "";
        bool hidden;
        public int HideWrites, ActivationRequests, OverlayRequests, ProcessEvents, Validations;
        public bool shouldHideMobileInput, readOnly, isFocused, interactable = true;
        public bool shouldHideSoftKeyboard { get => hidden; set { hidden = value; HideWrites++; } }
        public int caretPosition, characterLimit, lineType;
        public Func<char, char>? Validator;
        public readonly UnityEngine.Events.UnityEvent<string> onValueChanged = new();
        public readonly UnityEngine.Events.UnityEvent<string> onEndEdit = new();
        public string text { get => value; set { this.value = value; onValueChanged.Invoke(value); } }
        public bool IsInteractable() => interactable;
        public void ForceLabelUpdate() { }
        public void ActivateInputField()
        {
            ActivationRequests++;
            if (UnityEngine.TouchScreenKeyboard.isSupported && !shouldHideSoftKeyboard) OverlayRequests++;
            isFocused = true;
        }
        public void DeactivateInputField() { isFocused = false; }
        // Managed boundary reproduces only the owned TMP's observed editing admission.
        // This is not a Unity text-rendering or Android keyboard simulation.
        bool InPlaceEditing()
        {
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer) return true;
            if (UnityEngine.TouchScreenKeyboard.isSupported && shouldHideSoftKeyboard) return true;
            return !(UnityEngine.TouchScreenKeyboard.isSupported && !shouldHideSoftKeyboard && !shouldHideMobileInput);
        }
        public void ProcessEvent(UnityEngine.Event input)
        {
            ProcessEvents++;
            if (readOnly || !InPlaceEditing()) return;
            Validations++;
            char character = Validator?.Invoke(input.character) ?? input.character;
            if (character == '\0' || (characterLimit > 0 && value.Length >= characterLimit)) return;
            text = value.Insert(caretPosition, character.ToString());
            caretPosition++;
        }
    }
}
public class UIKeyboard : UnityEngine.MonoBehaviour
{
    public readonly UnityEngine.Events.UnityEvent<UnityEngine.KeyCode> OnSelectedKeyCode = new();
    public bool IsActive => gameObject.activeInHierarchy;
}
namespace Script.GUI.Controller
{
    public class ControllerInputKeyboard : UnityEngine.MonoBehaviour
    {
        public UIKeyboard keyboard = null!;
        public TMPro.TMP_InputField m_KeyboardInputField = null!;
        public int KeysProcessed;
        public void ProcessKeyCode(UnityEngine.KeyCode code)
        { KeysProcessed++; foreach (char value in KeyCodeConverter.ConvertToValue(code)) m_KeyboardInputField.ProcessEvent(new UnityEngine.Event { character = value }); }
    }
}
namespace Script.GUI.Controller.Keyboard { public class UIKeyboardKey : UnityEngine.MonoBehaviour { } }
public static class KeyCodeConverter
{
    public static string ConvertToValue(UnityEngine.KeyCode code) => code switch
    {
        UnityEngine.KeyCode.Space => " ", UnityEngine.KeyCode.Alpha1 => "1", UnityEngine.KeyCode.Alpha2 => "2", _ => code.ToString()
    };
}
namespace GloomhavenVR.Core
{
    public static class QuestStandalonePlatform { public static bool Enabled; }
    public static class VRSession { public static bool IsRunning = true; }
    public static class TickGuard { public static void Run(string name, Action callback, string module) => callback(); }
    public static class VRLog
    {
        public static readonly List<string> Messages = new();
        public static void Warn(string section, string text) => Messages.Add(text);
        public static void Info(string section, string text) => Messages.Add(text);
    }
}
namespace GloomhavenVR
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public static class Plugin { public static readonly Setting<bool> DevMode = new(false); }
}
namespace GloomhavenVR.WorldUI
{
    public static class WorldUIConfig { public static readonly GloomhavenVR.Setting<bool> KeyboardAutoCase = new(true); }
}
namespace GloomhavenVR.WorldUI.Patches
{
    public static class InputModeGuard { }
    public static class KeyboardHideSuppressor { }
    public static class KeyboardAutoHideBlock { public static void EnsureRegistered() { } }
    public static class InputFieldFocusWatch
    {
        public static bool Installed = true;
        public static TMPro.TMP_InputField? Focused;
        public static void EnsureRegistered() { }
        public static void Clear() { Focused = null; }
    }
}
