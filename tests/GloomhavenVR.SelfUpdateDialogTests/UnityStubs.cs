using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object target) { }
    }

    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }

    public class MonoBehaviour : Component { }

    public class Transform : Component
    {
        internal Transform(GameObject owner) => gameObject = owner;
        public Transform? parent { get; private set; }
        public Vector3 localScale;
        public void SetParent(Transform parentTransform, bool worldPositionStays) => parent = parentTransform;
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public sealed class RectTransform : Transform
    {
        internal RectTransform(GameObject owner) : base(owner) { }
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector2 offsetMin;
        public Vector2 offsetMax;
    }

    public sealed class GameObject : Object
    {
        private static readonly List<GameObject> Created = new();
        public GameObject(string objectName, params Type[] components)
        {
            name = objectName;
            transform = Array.IndexOf(components, typeof(RectTransform)) >= 0
                ? new RectTransform(this)
                : new Transform(this);
            Created.Add(this);
        }

        public static IReadOnlyList<GameObject> All => Created;
        public static void ClearAll() => Created.Clear();
        public string name { get; }
        public int layer;
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            return component;
        }

        public void SetActive(bool value) => activeSelf = value;
    }

    public sealed class Camera : Component { }

    public sealed class Canvas : Component
    {
        public RenderMode renderMode;
        public Camera? worldCamera;
    }

    public sealed class GraphicRaycaster : Component { }

    public enum RenderMode { WorldSpace }

    public struct Vector2
    {
        public Vector2(float xValue, float yValue) { x = xValue; y = yValue; }
        public float x;
        public float y;
        public static Vector2 zero => new(0f, 0f);
        public static Vector2 one => new(1f, 1f);
    }

    public struct Vector3
    {
        public Vector3(float xValue, float yValue, float zValue) { x = xValue; y = yValue; z = zValue; }
        public float x;
        public float y;
        public float z;
        public static Vector3 one => new(1f, 1f, 1f);
        public static Vector3 operator *(Vector3 value, float scalar) => new(value.x * scalar, value.y * scalar, value.z * scalar);
    }

    public struct Quaternion { }

    public struct Color
    {
        public Color(float red, float green, float blue, float alpha = 1f) { r = red; g = green; b = blue; a = alpha; }
        public float r;
        public float g;
        public float b;
        public float a;
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();

    public sealed class UnityEvent
    {
        public void AddListener(UnityAction action) { }
    }
}

namespace UnityEngine.UI
{
    public sealed class Image : UnityEngine.Component { public UnityEngine.Color color; }
    public sealed class Button : UnityEngine.Component { public UnityEngine.Events.UnityEvent onClick { get; } = new(); }
}

namespace TMPro
{
    [Flags]
    public enum FontStyles { Normal = 0, Bold = 1 }
    public enum TextAlignmentOptions { Top, TopLeft, Center }
    public class TMP_Text : UnityEngine.Component { public string text = string.Empty; }
    public sealed class TextMeshProUGUI : TMP_Text
    {
        public float fontSize;
        public FontStyles fontStyle;
        public TextAlignmentOptions alignment;
        public bool enableWordWrapping;
        public UnityEngine.Color color;
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Error(string category, string message) { } }
}

namespace GloomhavenVR.Hands.Interact
{
    internal static class UguiPokeSurfaces
    {
        internal static void Register(UnityEngine.Canvas canvas) { }
        internal static void Unregister(UnityEngine.Canvas canvas) { }
    }
}

namespace GloomhavenVR.WorldUI
{
    internal static class CanvasConversion { internal static UnityEngine.Camera? WorldCamera => null; }
    internal static class PanelLayout { internal static float WorldScale => 1f; }
    internal static class PanelPlacement
    {
        internal static void ClampIntoView(UnityEngine.Camera head, float scale, ref UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation) => rotation = new();
        internal static void Spawn(UnityEngine.Camera head, float scale, out UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation) { position = new(); rotation = new(); }
    }
    internal static class MrBacking { internal static void Opacify(UnityEngine.UI.Image image) { } }
    internal static class VRLayers { internal static void Apply(UnityEngine.GameObject target) { } }
    internal static class WorldUIAssets { internal static void TryAssignGameFont(TMPro.TextMeshProUGUI text) { } }
    internal static class WorldUIConfig
    {
        internal static readonly FloatSetting CanvasScaleMm = new();
        internal sealed class FloatSetting { internal float Value = 1f; }
    }
}
