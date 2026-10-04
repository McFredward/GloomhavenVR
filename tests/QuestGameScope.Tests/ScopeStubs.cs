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
    public static class Time { public static float unscaledTime; public static double unscaledTimeAsDouble => unscaledTime; }
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
        public static Object[] FindObjectsOfType(Type type, bool includeInactive)
        {
            Resources.TargetedQueries.Add(type);
            var found = Resources.All.Where(value => !value.Destroyed && type.IsAssignableFrom(value.GetType()))
                .OfType<Component>().Where(value => value.gameObject.scene.IsValid() && value.gameObject.scene.isLoaded
                    && (includeInactive || value.gameObject.activeInHierarchy)).Cast<Object>().ToArray();
            Resources.TargetedObjects += found.Length;
            return found;
        }
        public static void Destroy(Object value)
        {
            value.Destroyed = true;
            if (value is Component component) component.gameObject.Detach(component);
        }
    }
    public sealed class GameObject : Object
    {
        readonly List<Component> components = new();
        internal readonly List<GameObject> Children = new();
        internal GameObject? Parent;
        public string name = "untrusted display name";
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public SceneManagement.Scene scene;
        public GameObject()
        {
            scene = SceneManagement.SceneManager.DefaultScene;
            scene.Data?.Roots.Add(this);
        }
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
        public void AddChild(GameObject child)
        {
            child.Parent?.Children.Remove(child);
            child.scene.Data?.Roots.Remove(child);
            Children.Add(child); child.Parent = this; child.MoveToScene(scene);
        }
        public void MoveToScene(SceneManagement.Scene value)
        {
            scene.Data?.Roots.Remove(this); scene = value;
            if (Parent == null) scene.Data?.Roots.Add(this);
            foreach (GameObject child in Children) child.MoveToScene(value);
        }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) where T : Component
        {
            result.Clear(); AppendComponents(includeInactive, result);
        }
        void AppendComponents<T>(bool includeInactive, List<T> result) where T : Component
        {
            if (Destroyed || (!includeInactive && !activeSelf)) return;
            foreach (Component component in components)
                if (!component.Destroyed && component is T item) result.Add(item);
            foreach (GameObject child in Children) child.AppendComponents(includeInactive, result);
        }
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
        public static int GlobalEnumerations, TargetedObjects;
        public static readonly List<Type> TargetedQueries = new();
        public static Object[] FindObjectsOfTypeAll(Type type)
        {
            TargetedQueries.Add(type);
            var found = All.Where(value => !value.Destroyed && type.IsAssignableFrom(value.GetType())).ToArray();
            TargetedObjects += found.Length;
            return found;
        }
        public static T[] FindObjectsOfTypeAll<T>()
        { GlobalEnumerations++; return All.Where(value => !value.Destroyed).OfType<T>().ToArray(); }
    }
}
namespace UnityEngine.SceneManagement
{
    public sealed class SceneData
    {
        public int Handle;
        public bool Loaded = true;
        public readonly List<UnityEngine.GameObject> Roots = new();
    }
    public readonly struct Scene
    {
        internal readonly SceneData? Data;
        public Scene(bool valid, bool loaded) { Data = valid ? new SceneData { Handle = -1, Loaded = loaded } : null; }
        internal Scene(SceneData data) { Data = data; }
        public int handle => Data?.Handle ?? 0;
        public bool isLoaded => Data?.Loaded == true;
        public int rootCount => Data?.Roots.Count ?? 0;
        public bool IsValid() => Data != null;
        public void GetRootGameObjects(List<UnityEngine.GameObject> result)
        {
            result.Clear(); if (Data == null) return;
            foreach (UnityEngine.GameObject root in Data.Roots)
                if (!root.Destroyed && root.Parent == null) result.Add(root);
        }
    }
    public static class SceneManager
    {
        static readonly List<Scene> Scenes = new();
        public static Scene DefaultScene;
        public static Scene Persistent;
        static SceneManager() { Reset(); }
        public static void Reset()
        {
            Scenes.Clear(); DefaultScene = AddScene(); Persistent = new Scene(new SceneData { Handle = 500 });
        }
        public static int sceneCount => Scenes.Count;
        public static Scene GetSceneAt(int index) => Scenes[index];
        public static Scene AddScene()
        { var scene = new Scene(new SceneData { Handle = Scenes.Count + 1 }); Scenes.Add(scene); return scene; }
        public static void Unload(Scene scene) { scene.Data!.Loaded = false; Scenes.Remove(scene); }
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
    public string? tooltipText;
    public string? ShownTooltipText { get; private set; }
    public int SetTextCalls;
    public bool RefreshRequested;
    public string? Subtext;
    public bool CanBeShown => enabled && TooltipEnabled && !string.IsNullOrEmpty(ShownTooltipText ?? tooltipText) && gameObject.activeInHierarchy;
    public void SetText(string? text, bool refreshTooltip = false, string? subtext = null)
    { ShownTooltipText = text; RefreshRequested = refreshTooltip; Subtext = subtext; SetTextCalls++; }
}
public class UIMenuOption : UnityEngine.MonoBehaviour
{
    Action? onSelected;
    UITextTooltipTarget? tooltip;
    public bool IsInteractable { get; set; } = true;
    public bool TooltipEnabled => tooltip != null && tooltip.enabled;
    public string? TooltipText => tooltip?.ShownTooltipText;
    public object? CallbackIdentity => onSelected;
    public UIMenuOption() { tooltip = gameObject.AddComponent<UITextTooltipTarget>(); tooltip.enabled = false; }
    public void SetTooltip(bool enabled, string? text)
    {
        // Actual UIMainMenuOption.SetTooltip silently returns when its serialized
        // pointer is null, even if an authored tooltip exists on its GameObject.
        if (tooltip == null) return;
        if (!string.IsNullOrEmpty(text)) tooltip.SetText(text);
        tooltip.enabled = enabled;
    }
    public void UseUnlinkedAuthoredTooltip()
    {
        tooltip!.enabled = true;
        tooltip.TooltipEnabled = true;
        tooltip.tooltipText = "GUI_MAIN_MENU_GUILDMASTER_TOOLTIP";
        tooltip = null;
    }
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
        public UIMainOptionsMenu()
        {
            var button = new UIMenuOption(); button.UseUnlinkedAuthoredTooltip();
            guildmasterButton = new MenuOptionWrapper(button);
        }
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
