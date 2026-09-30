using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object value) { }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => gameObject.GetComponentsInChildren<T>();
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        internal readonly List<Transform> Children = new();
        public void SetParent(Transform parent) => parent.Children.Add(this);
    }
    public sealed class RectTransform : Transform { }
    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public RectTransform transform { get; }
        public GameObject()
        {
            transform = new RectTransform { gameObject = this };
            _components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            if (typeof(T) == typeof(CanvasGroup) && GetComponent<CanvasGroup>() != null)
                throw new InvalidOperationException("Unity permits one CanvasGroup per GameObject");
            var value = new T { gameObject = this }; _components.Add(value); return value;
        }
        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>() where T : Component
        {
            var result = new List<T>(); Add(this, result); return result.ToArray();
            static void Add(GameObject current, List<T> into)
            {
                into.AddRange(current.GetComponents<T>());
                foreach (Transform child in current.transform.Children) Add(child.gameObject, into);
            }
        }
    }
    public sealed class CanvasGroup : Behaviour
    {
        public float alpha = 1f;
        public bool interactable = true, blocksRaycasts = true, ignoreParentGroups;
    }
    public static class Canvas
    {
        public static event Action? willRenderCanvases;
        public static void Render() => willRenderCanvases?.Invoke();
    }
}
namespace UnityEngine.UI
{
    public sealed class LayoutElement : UnityEngine.Behaviour { public bool ignoreLayout; }
}
public sealed class UIPartyCharacterEnhancementAbilityCardsDisplay : UnityEngine.MonoBehaviour
{
    public readonly List<object> slotsPool = new();
}
