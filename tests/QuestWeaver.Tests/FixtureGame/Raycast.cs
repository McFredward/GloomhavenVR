// Unity-free stack/receiver witnesses for the coordinate substitutions. The
// actual game/UI permission implementations are separately pinned in the audit.
namespace UnityEngine
{
    public readonly record struct Vector2(float x, float y);
    public readonly record struct Vector3(float x, float y, float z);
    public class Camera
    {
        public virtual Vector3 ScreenToViewportPoint(Vector3 screen)
        { RaycastWitness.Trace.Add("native-viewport"); return new Vector3(screen.x / 10, screen.y / 10, screen.z); }
    }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { public UnityEngine.Vector2 position = new(20, 30); public bool Allowed = true; }
    public readonly record struct RaycastResult(float Value);
    public class EventSystem
    {
        public virtual void RaycastAll(PointerEventData data, List<RaycastResult> results)
        { RaycastWitness.Trace.Add("native-all"); if (data.Allowed) results.Add(new RaycastResult(data.position.x)); }
    }
}
namespace UnityEngine.UI
{
    public static class MultipleDisplayUtilities
    {
        public static UnityEngine.Vector3 RelativeMouseAtScaled(UnityEngine.Vector2 position)
        { RaycastWitness.Trace.Add("native-display"); return new UnityEngine.Vector3(position.x + 100, position.y, 0); }
    }
    public class GraphicRaycaster
    {
        public UnityEngine.Camera Camera = new();
        public void Raycast(UnityEngine.EventSystems.PointerEventData data, List<UnityEngine.EventSystems.RaycastResult> results)
        {
            if (!data.Allowed) { RaycastWitness.Trace.Add("rejected"); return; }
            try
            {
                UnityEngine.Vector3 screen = MultipleDisplayUtilities.RelativeMouseAtScaled(data.position);
                UnityEngine.Vector3 viewport = Camera.ScreenToViewportPoint(screen);
                results.Add(new UnityEngine.EventSystems.RaycastResult(viewport.x));
            }
            finally { RaycastWitness.Trace.Add("finally"); }
        }
    }
}
namespace InControl
{
    public class PointerInputModuleExtended
    {
        public UnityEngine.EventSystems.EventSystem System = new();
        public UnityEngine.EventSystems.PointerEventData Data = new();
        public int GetMousePointerEventData(int id)
        { var results = new List<UnityEngine.EventSystems.RaycastResult>(); System.RaycastAll(Data, results); return results.Count; }
    }
}
public static class RaycastWitness
{
    public static readonly List<string> Trace = new();
    public static bool Scoped;
    public static bool Available = true;
    public static bool IsAvailable() => Available;
    public static bool AlwaysAvailable() => true;
    public static UnityEngine.UI.GraphicRaycaster? Expected;
    public static UnityEngine.EventSystems.PointerEventData? Data;
    public static UnityEngine.Vector3 Display(UnityEngine.Vector2 position, UnityEngine.UI.GraphicRaycaster owner,
        UnityEngine.EventSystems.PointerEventData data)
    {
        if (!ReferenceEquals(owner, Expected) || !ReferenceEquals(data, Data)) throw new InvalidOperationException("display-owner");
        Trace.Add("display");
        return Scoped ? new UnityEngine.Vector3(position.x, position.y, 0) : UnityEngine.UI.MultipleDisplayUtilities.RelativeMouseAtScaled(position);
    }
    public static UnityEngine.Vector3 Viewport(UnityEngine.Vector3 original, UnityEngine.Vector3 screen, UnityEngine.Camera camera,
        UnityEngine.UI.GraphicRaycaster owner, UnityEngine.EventSystems.PointerEventData data)
    {
        if (!ReferenceEquals(owner, Expected) || !ReferenceEquals(data, Data) || !ReferenceEquals(camera, owner.Camera)
            || screen.y != 30 || original.y != 3) throw new InvalidOperationException("viewport-stack");
        Trace.Add("viewport"); return original;
    }
    public static UnityEngine.Vector3 WrongViewport(UnityEngine.Vector3 original, UnityEngine.Vector3 screen, UnityEngine.Camera camera,
        UnityEngine.UI.GraphicRaycaster owner, UnityEngine.EventSystems.PointerEventData data)
    { throw new InvalidOperationException("viewport-control"); }
    public static void Mouse(UnityEngine.EventSystems.EventSystem system, UnityEngine.EventSystems.PointerEventData data,
        List<UnityEngine.EventSystems.RaycastResult> results, InControl.PointerInputModuleExtended owner)
    {
        if (!ReferenceEquals(system, owner.System) || !ReferenceEquals(data, owner.Data)) throw new InvalidOperationException("mouse-owner");
        Trace.Add("mouse"); system.RaycastAll(data, results);
    }
}
