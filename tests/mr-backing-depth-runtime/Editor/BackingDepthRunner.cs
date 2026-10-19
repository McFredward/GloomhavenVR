using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using GloomhavenVR.WorldUI;

public static class BackingDepthRunner
{
    [Serializable] public sealed class NativeGeometry { public string visibleRectPath; public Node[] nodes; }
    [Serializable] public sealed class Node
    {
        public string path, parent; public Vector3 position, scale; public Quaternion rotation;
        public Vector2 anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot;
    }
    private static int _checks;
    private static readonly List<GameObject> Owned = new List<GameObject>();
    private static string _output;
    private static void Check(bool condition, string reason)
    { _checks++; if (!condition) throw new Exception("MR depth assertion: " + reason); }
    private static GameObject New(string name, bool rect = false)
    { var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name); Owned.Add(go); return go; }
    private static string Argument(string name)
    { var args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, name) + 1]; }

    public static void Run()
    {
        _output = Argument("-mrDepthOutput");
        try
        {
            var geometry = JsonUtility.FromJson<NativeGeometry>(File.ReadAllText(Argument("-nativeInitiativeGeometry")));
            foreach (float worldScale in new[] { 1f, 12.01f, 25.69f })
            foreach (bool remote in new[] { false, true })
            foreach (bool native in new[] { false, true })
                Case(geometry, worldScale, remote, native);
            File.WriteAllText(_output + "/result.txt", "MR backing depth: " + _checks + " actual Unity assertions passed.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        { Debug.LogException(ex); File.WriteAllText(_output + "/result.txt", ex.ToString()); EditorApplication.Exit(1); }
    }

    private static void Case(NativeGeometry geometry, float worldScale, bool remote, bool native)
    {
        var board = New(remote ? "RemoteBoard" : "LocalBoard").transform;
        board.localScale = Vector3.one * worldScale;
        board.rotation = Quaternion.Euler(15, 45, 0);
        var mount = New("InitiativeMount").transform; mount.SetParent(board, false);
        mount.localPosition = new Vector3(0, .172f, -.008f);
        var host = (RectTransform)New("GloomhavenVR.Panel_InitiativeTrack", true).transform;
        host.sizeDelta = new Vector2(1028, 171);
        var canvas = host.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 300;
        var dock = new DockProbe(host, mount); dock.Apply();
        Check(Mathf.Abs(host.lossyScale.x - worldScale / 2400f) < 1e-5f, "original dock density includes board/world scale");
        var target = (RectTransform)New("InitiativeModule", true).transform; target.SetParent(host, false);
        var holder = (RectTransform)New("InitiativeTrack", true).transform; holder.SetParent(target, false);
        holder.gameObject.AddComponent<Canvas>().overrideSorting = false;
        var nodes = new Dictionary<string, RectTransform>(); RectTransform entry = null;
        foreach (Node node in geometry.nodes)
        {
            var rect = (RectTransform)New(node.path, true).transform;
            rect.SetParent(string.IsNullOrEmpty(node.parent) ? holder : nodes[node.parent], false);
            rect.anchorMin = node.anchorMin; rect.anchorMax = node.anchorMax; rect.pivot = node.pivot;
            rect.sizeDelta = node.sizeDelta; rect.anchoredPosition = node.anchoredPosition;
            rect.localRotation = node.rotation; rect.localScale = node.scale;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, node.position.z);
            nodes[node.path] = rect;
            if (entry == null)
            {
                entry = rect;
                // The original HorizontalLayoutGroup owns these resolved root values. Its
                // dynamic slot is zero here; every descendant remains exact native geometry.
                entry.sizeDelta = new Vector2(134, 130); entry.anchoredPosition = Vector2.zero;
            }
        }
        RectTransform portrait = nodes[geometry.visibleRectPath];
        portrait.gameObject.AddComponent<Image>().color = Color.red;
        var camera = New("Head").AddComponent<Camera>(); canvas.worldCamera = camera;
        camera.nearClipPlane = .0001f * worldScale; camera.farClipPlane = 10 * worldScale;
        camera.orthographic = true; camera.orthographicSize = .12f * worldScale;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = native ? Color.clear : Color.green;
        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad); Owned.Add(plate); plate.transform.SetParent(host, false);
        var material = new Material(Shader.Find("GloomhavenVR/Overlay")); material.color = new Color(.12f,.11f,.10f,1);
        material.SetInt("_ZWrite",1); material.SetInt("_ZTest",4); material.SetInt("_Cull",0);
        material.SetInt("_SrcBlend",1); material.SetInt("_DstBlend",0); material.renderQueue = 2998;
        plate.GetComponent<MeshRenderer>().sharedMaterial = material; plate.GetComponent<MeshRenderer>().sortingOrder = canvas.sortingOrder;
        PanelLayout.Scale = worldScale;
        Rect bounds; float backZ;
        Check(PanelInkBounds.Measure(host, portrait, out bounds, out backZ) && Mathf.Abs(backZ) < .001f,
            "exact original native portrait chain initially lies on host plane");
        MrBacking.Apply(plate.transform, host, bounds.size + new Vector2(16,16), bounds.center, backZ);
        PointCamera(camera, host, portrait, worldScale);
        Check(Render(camera, "", false).r > .9f, "coplanar original initiative remains visible");
        // Exact original AnimateInitiativeReorder/LeanTween.moveX write: change only WORLD x.
        // The hardware log records 31.51px foreign drift during this native world-X handoff.
        // Under the production rotated dock this is real out-of-plane geometry, not an
        // invented child local-z offset; NormalizeDepth deliberately excludes entry roots.
        float xForZ = host.InverseTransformVector(Vector3.right).z;
        Vector3 world = entry.position; world.x += 31.51f / xForZ; entry.position = world;
        Check(PanelInkBounds.Measure(host, portrait, out bounds, out backZ) && Mathf.Abs(backZ - 31.51f) < .01f,
            "native world-X update under original dock rotation produces measured child depth");
        MrBacking.Apply(plate.transform, host, bounds.size + new Vector2(16,16), bounds.center, backZ);
        PointCamera(camera, host, portrait, worldScale);
        string label = (remote ? "remote" : "local") + (native ? "-native" : "-chroma") + "-" + worldScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        Check(Render(camera, label, true).r > .9f, "depth-bearing original initiative remains in front of opaque backing");
        float physicalGap = (plate.transform.localPosition.z - backZ) * host.lossyScale.z / worldScale;
        Check(Mathf.Abs(physicalGap - .002f) < .00001f, "actual original plate separation remains two tracking millimetres");
        // MR off never moves the native widget; its RGB remains the original canvas output.
        plate.SetActive(false);
        Check(Render(camera, "", false).r > .9f && Mathf.Abs(host.InverseTransformPoint(entry.position).z - backZ) < .01f,
            "MR off restores ordinary UI without changing original portrait pose");
        plate.SetActive(true);
        // A genuinely nearer depth-writing object must retain physical occlusion.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); Owned.Add(cube);
        cube.transform.rotation = host.rotation;
        cube.transform.position = portrait.TransformPoint(portrait.rect.center) - host.forward * .01f * worldScale;
        cube.transform.localScale = new Vector3(.03f,.03f,.002f) * worldScale;
        var blocker = new Material(Shader.Find("Unlit/Color")); blocker.color = Color.cyan; cube.GetComponent<MeshRenderer>().sharedMaterial = blocker;
        Color occluded = Render(camera, "", false);
        Check(occluded.g > .9f && occluded.r < .1f, "honest nearer geometry still occludes MR UI and backing");
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(blocker);
        foreach (var owned in Owned) if (owned != null) UnityEngine.Object.DestroyImmediate(owned); Owned.Clear();
    }

    private static void PointCamera(Camera camera, Transform host, RectTransform portrait, float scale)
    {
        Vector3 center = portrait.TransformPoint(portrait.rect.center);
        camera.transform.position = center - host.forward * 2 * scale;
        camera.transform.rotation = host.rotation;
    }

    private static Color Render(Camera camera, string label, bool save)
    {
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(384,384,24,RenderTextureFormat.ARGB32); rt.Create();
        camera.targetTexture = rt; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var texture = new Texture2D(384,384,TextureFormat.RGBA32,false); texture.ReadPixels(new Rect(0,0,384,384),0,0); texture.Apply();
        Color result = texture.GetPixel(192,192);
        if (save) File.WriteAllBytes(_output + "/" + label + ".png", texture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        return result;
    }
}

namespace GloomhavenVR.WorldUI
{
    internal static class PanelLayout { internal static float Scale; internal static float WorldScale => Scale; }
    internal sealed class ConvertedPanel
    {
        internal GameObject HostGo; internal RectTransform HostRect; internal object OrderCluster;
        internal Transform HostTransform => HostGo.transform;
    }
    internal static class PlayTray { internal static readonly object Current = new object(); internal const float TrayPixelsPerMeter = 2400; }
    internal class SlotPanelSurface { protected virtual void Place() { } }
    internal partial class TrayMountedPanelSurface : SlotPanelSurface
    {
        protected ConvertedPanel Panel; protected Transform Mount;
        private int _dockLoggedMountId, _mountVisibleFrame;
        private const int MountHideGraceFrames = 2;
        protected float DensityScale => 1; protected float MountMaxHeight => .14f;
        protected bool FitWidthToMount => true; protected float MountWidth => .56f;
        protected Vector2 GrowDirection => Vector2.up; protected Vector3 MountOffset => Vector3.zero;
        private const float MinDensityScale = .5f, MaxDensityScale = 1;
        internal float AppliedFitScale, AppliedMetersPerPixel, AppliedHeightMeters;
        private void LogDockedRect(Transform mount) { }
    }
    internal sealed class DockProbe : TrayMountedPanelSurface
    {
        internal DockProbe(RectTransform host, Transform mount) { Panel = new ConvertedPanel { HostGo = host.gameObject, HostRect = host }; Mount = mount; }
        internal void Apply() { Place(); }
    }
}
