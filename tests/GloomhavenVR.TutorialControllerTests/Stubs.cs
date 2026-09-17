using UnityEngine;
namespace UnityEngine {
public class Object {
    public bool Destroyed { get; private set; }
    public static bool operator ==(Object? a, Object? b) {
        bool noA = ReferenceEquals(a, null), noB = ReferenceEquals(b, null);
        if (noA && noB) return true;
        if (noA) return b!.Destroyed;
        if (noB) return a!.Destroyed;
        return ReferenceEquals(a, b);
    }
    public static bool operator !=(Object? a, Object? b) => !(a == b);
    public override bool Equals(object? other) => other is Object obj ? this == obj : other is null && Destroyed;
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    public static GameObject Instantiate(GameObject source, Transform parent, bool worldPositionStays) { var clone = source.Clone(); clone.transform.SetParent(parent, false); return clone; }
    public static void Destroy(Object? obj) {
        if (obj == null) return;
        if (obj is GameObject go) {
            foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
            foreach (Component component in go.Components) component.Destroyed = true;
            go.transform.SetParent(null, false);
            go.transform.Destroyed = true;
        }
        obj!.Destroyed = true;
    }
}
public class GameObject : Object {
    public string name; public int layer; public bool activeSelf = true;
    public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
    public Transform transform; public readonly List<Component> Components = new();
    public GameObject(string name) { this.name = name; transform = new Transform(this); }
    public void SetActive(bool active) => activeSelf = active;
    public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
    public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault()!;
    public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => transform.GetComponentsInChildren<T>(includeInactive);
    public GameObject Clone() { var go = new GameObject(name) { layer = layer }; foreach (var c in Components) if(c is Renderer r) go.AddComponent<Renderer>().enabled = r.enabled; foreach(var t in transform) t.gameObject.Clone().transform.SetParent(go.transform, false); return go; }
    public static GameObject CreatePrimitive(PrimitiveType type) { var go = new GameObject("sphere"); go.AddComponent<Renderer>(); go.AddComponent<Collider>(); return go; }
}
public class Component : Object { public GameObject gameObject = null!; public Transform transform => gameObject.transform; }
public class Transform : Object, IEnumerable<Transform> {
    public readonly GameObject gameObject; public string name => gameObject.name;
    public Transform? parent; public readonly List<Transform> Children = new();
    public Vector3 localPosition, localScale; public Quaternion localRotation;
    public Transform(GameObject go) { gameObject = go; }
    public void SetParent(Transform? target, bool worldPositionStays) { parent?.Children.Remove(this); parent = target; target?.Children.Add(this); }
    public Transform Find(string name) => Children.Find(t => t.name == name)!;
    public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.Components.OfType<T>().Concat(Children.SelectMany(c => c.GetComponentsInChildren<T>(includeInactive))).ToArray();
    public IEnumerator<Transform> GetEnumerator() => Children.GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
public struct Vector3(float x, float y, float z) { public float x = x, y = y, z = z; public static Vector3 zero => new(0,0,0); public static Vector3 one => new(1,1,1); public static Vector3 operator *(Vector3 v, float f) => new(v.x*f, v.y*f, v.z*f); }
public struct Quaternion { public static Quaternion identity => new(); }
public struct Color(float r,float g,float b,float a) { public float r = r, g = g, b = b, a = a; public static Color Lerp(Color from,Color to,float t)=>to; }
public static class Mathf { public const float PI = MathF.PI; public static float Sin(float x)=>MathF.Sin(x); public static float Max(float a,float b)=>MathF.Max(a,b); public static float Abs(float a)=>MathF.Abs(a); public static bool Approximately(float a,float b)=>Abs(a-b)<0.00001f; public static float MoveTowards(float a,float b,float delta)=>Abs(a-b)<=delta?b:a+MathF.Sign(b-a)*delta; }
public static class Time { public static float unscaledTime, unscaledDeltaTime = 0.02f; }
public class Material { public Color color; }
public class MaterialPropertyBlock { public void Clear() {} public void SetColor(string name,Color value) {} public void SetFloat(string name,float value) {} }
public class Renderer : Component { public bool enabled = true; public Material material = new(); public bool Lit; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows; public void SetPropertyBlock(MaterialPropertyBlock? block)=>Lit=block!=null; }
public class Collider : Component {}
public enum PrimitiveType { Sphere }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace UnityEngine.XR { public enum XRNode { LeftHand, RightHand } public struct InputDevice { public bool isValid=>true; public string name=>"Oculus Touch Controller OpenXR"; } public static class InputDevices { public static InputDevice GetDeviceAtXRNode(XRNode node)=>new(); } }
namespace GloomhavenVR.Core {
internal static class VRLog { public static void Note(string tag,string text) {} public static void Warn(string tag,string text) {} }
internal static class VRLayers { public static void Apply(GameObject go) { go.layer=31; foreach(var t in go.transform) Apply(t.gameObject); } }
}
namespace GloomhavenVR.WorldUI {
internal static class WorldUIAssets {
    internal static bool MissingRight;
    internal static GameObject? TryLoadPrefab(string path) {
        if (MissingRight && path.EndsWith("_right.prefab")) return null;
        var root = new GameObject("controller");
        foreach (string key in new[]{"body","trigger","squeeze","thumbstick","button_primary","button_secondary"}) {
            var part = new GameObject(key); part.transform.SetParent(root.transform,false);
            if(key != "squeeze") part.AddComponent<Renderer>();
            new GameObject("Anchor").transform.SetParent(part.transform,false);
        }
        return root;
    }
    internal static Material CreateFlatMaterial(Color color)=>new(){color=color};
}
}
namespace GloomhavenVR.Hands {
internal enum HandSide { Left, Right }
internal sealed class HandRig { public Transform Root = null!; }
internal sealed class VRHand {
    public HandSide Side; public Transform transform; public HandRig Rig; public Grabber? Grabber => null;
    public VRHand(HandSide side) { Side=side; transform=new GameObject(side.ToString()).transform; var root=new GameObject("HandRoot");root.transform.SetParent(transform,false);root.AddComponent<Renderer>();var disabled=new GameObject("already hidden");disabled.AddComponent<Renderer>().enabled=false;disabled.transform.SetParent(root.transform,false);Rig=new(){Root=root.transform}; }
}
internal sealed class Grabber { public object? Held => null; }
internal static class VRHands { public static VRHand Left = new(HandSide.Left), Right = new(HandSide.Right), Primary = Right; }
}
namespace GloomhavenVR.Rig {
internal sealed class Setting<T>(T value) { public T Value=value; }
internal enum TurnMode { Off, Snap }
internal static class ComfortSettings {
    public static bool IsBound = true;
    public static Setting<bool> WorldGrabEnabled=new(true),ScaleEnabled=new(true),RotateEnabled=new(true),FlightEnabled=new(true),LaserCarryReel=new(true);
    public static Setting<Hands.HandSide> FlightHand=new(Hands.HandSide.Left),TurnHand=new(Hands.HandSide.Right);
    public static Setting<TurnMode> Turn=new(TurnMode.Snap);
    public static Setting<float> RecenterHoldSeconds=new(1);
}
internal static class LocalTurnControl { public static Hands.HandSide Resolve(Hands.HandSide side)=>side; }
}
namespace GloomhavenVR.Cards { internal sealed class VRCard {} internal static class ItemsPile { internal sealed class ItemChip {} } internal static class HeldCardGrip { public static bool Enabled=true; public static bool InHand(Hands.HandSide hand)=>false; } internal static class CardsConfig { public static Rig.Setting<float> BoardMinWidthMeters=new(.18f),BoardMaxWidthMeters=new(1.4f); } }
namespace GloomhavenVR.Board { internal static class BoardConfig { public static Rig.Setting<bool> TouchTilesWithFingertip=new(true); } }
namespace GloomhavenVR.Compat { internal static class ControlsProgress { internal static float Accumulated=>0; internal static void Notify(ControlAction action,float amount=1) {} } }
