using System;
using System.Collections.Generic;
using System.Linq;

// A managed component/scene seam, not a Unity rendering or pointer simulation.
// Original reflected type/field/API names are independently checked against the
// native GH.Runtime assembly by the private SDK fixture.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public enum SystemLanguage { English, German }
    public static class Application { public static SystemLanguage systemLanguage; }
    public static class Time { public static float unscaledTime; }
    public static class Debug
    {
        public static readonly List<string> Logs = new();
        public static readonly List<string> Errors = new();
        public static void Log(object value) => Logs.Add(value.ToString()!);
        public static void LogError(object value) => Errors.Add(value.ToString()!);
    }
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            value.Destroyed = true;
            if (value is Component component) component.gameObject.Detach(component);
        }
    }
    public readonly struct Scene
    {
        readonly bool valid;
        public readonly bool isLoaded;
        public Scene(bool valid, bool loaded) { this.valid = valid; isLoaded = loaded; }
        public bool IsValid() => valid;
    }
    public sealed class GameObject : Object
    {
        readonly List<Component> components = new();
        public string name = "untrusted display name";
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public Scene scene = new(true, true);
        internal void Attach(Component component) { if (!components.Contains(component)) components.Add(component); }
        internal void Detach(Component component) => components.Remove(component);
        public void SetActive(bool value) => activeSelf = value;
        public Component? GetComponent(Type type) => components.FirstOrDefault(c => !c.Destroyed && type.IsAssignableFrom(c.GetType()));
        public T? GetComponent<T>() where T : Component => GetComponent(typeof(T)) as T;
        public Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type)!;
            component.gameObject.Detach(component);
            component.gameObject = this;
            Attach(component);
            return component;
        }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public int ComponentCount<T>() where T : Component => components.Count(c => !c.Destroyed && c is T);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public Component() { gameObject = new GameObject(); gameObject.Attach(this); }
        public Component? GetComponent(Type type) => gameObject.GetComponent(type);
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public MonoBehaviour() => Resources.All.Add(this);
    }
    public static class Resources
    {
        public static readonly List<Object> All = new();
        public static T[] FindObjectsOfTypeAll<T>() => All.Where(value => !value.Destroyed).OfType<T>().ToArray();
    }
}
namespace UnityEngine.UI
{
    public class Selectable : UnityEngine.MonoBehaviour
    {
        public bool interactable = true;
    }
    public class Button : Selectable
    {
        public Action? onClick;
        public void Press() { if (interactable) onClick?.Invoke(); }
    }
    public sealed class Toggle : Selectable { public bool isOn; }
}
public class NativeLocalizedButton : UnityEngine.UI.Button
{
    public string textLanguageKey = "GUI_LOAD";
    public string TextLanguageKey { set => textLanguageKey = value; }
}
public class ExtendedButton : NativeLocalizedButton { }
public class UIPromotionDLCSlot : UnityEngine.MonoBehaviour
{
    readonly UnityEngine.UI.Button button;
    public readonly object PromotionImage = new();
    public readonly object OriginalTitle = new();
    public UIPromotionDLCSlot() { button = new UnityEngine.UI.Button(); }
    public UnityEngine.UI.Button NativeButton => button;
}
public sealed class UIBuyDLCSlot : UIPromotionDLCSlot { }
public sealed class UIDLCSelectorOption : UnityEngine.MonoBehaviour
{
    readonly UnityEngine.UI.Toggle _gamepadToggle = new();
    readonly UnityEngine.GameObject _dlcPurchaseablePanel = new();
    public readonly UnityEngine.UI.Toggle MouseToggle = new();
    public readonly object PromotionImage = new();
    public readonly object OriginalDescription = new();
    public UIDLCSelectorOption() { _dlcPurchaseablePanel.SetActive(false); }
    public UnityEngine.UI.Toggle NativeGamepadToggle => _gamepadToggle;
    public UnityEngine.GameObject PurchasePanel => _dlcPurchaseablePanel;
}
public class UITooltipTarget : UnityEngine.MonoBehaviour { public bool TooltipEnabled { get; set; } }
public sealed class UITextTooltipTarget : UITooltipTarget
{
    public string? ShownTooltipText { get; private set; }
    public int SetTextCalls;
    public bool RefreshRequested;
    public string? Subtext;
    public bool CanBeShown => TooltipEnabled && !string.IsNullOrEmpty(ShownTooltipText) && gameObject.activeInHierarchy;
    public void SetText(string? text, bool refreshTooltip = false, string? subtext = null)
    { ShownTooltipText = text; RefreshRequested = refreshTooltip; Subtext = subtext; SetTextCalls++; }
}
public class UIMenuOption : UnityEngine.MonoBehaviour
{
    Action? onSelected;
    public bool IsInteractable { get; set; } = true;
    public bool TooltipEnabled;
    public string? TooltipText;
    public void SetTooltip(bool enabled, string text) { TooltipEnabled = enabled; TooltipText = text; }
    public void Bind(GLOOM.MainMenu.MenuSuboption option) { var closure = new Selection(option); onSelected = closure.Invoke; }
    sealed class Selection
    {
        readonly GLOOM.MainMenu.MenuSuboption option;
        internal Selection(GLOOM.MainMenu.MenuSuboption option) => this.option = option;
        internal void Invoke() { }
    }
}
public sealed class MenuOptionWrapper
{
    public UIMenuOption Button { get; }
    public MenuOptionWrapper(UIMenuOption button) => Button = button;
}
namespace GLOOM.MainMenu
{
    public sealed class MenuSuboption { public string NameLocKey { get; } public MenuSuboption(string key) => NameLocKey = key; }
    public sealed class UIMainMenuSuboption : UIMenuOption { }
    public sealed class UIMainOptionsMenu : UnityEngine.MonoBehaviour
    {
        readonly MenuOptionWrapper guildmasterButton;
        public UIMainOptionsMenu() => guildmasterButton = new MenuOptionWrapper(new UIMenuOption());
        public UIMenuOption Guildmaster => guildmasterButton.Button;
    }
    public sealed class UILoadGameSlot : UnityEngine.MonoBehaviour
    {
        readonly ExtendedButton loadButton = new();
        public readonly UnityEngine.UI.Button DeleteButton = new();
        public readonly object CharacterImages = new();
        public ExtendedButton NativeLoad => loadButton;
    }
    public sealed class UICreateGameDLCStep : UnityEngine.MonoBehaviour
    {
        public readonly UnityEngine.UI.Button ConfirmButton = new();
    }
}
namespace VoiceChat
{
    public sealed class VoceChatOptions : UnityEngine.MonoBehaviour
    {
        readonly ExtendedButton _switchChatButton = new();
        public ExtendedButton NativeButton => _switchChatButton;
    }
}
