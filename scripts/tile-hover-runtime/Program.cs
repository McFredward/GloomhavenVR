using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Board;
using GloomhavenVR.Board.Patches;
using ScenarioRuleLibrary;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class HoverCounter : MonoBehaviour, IHoverable
{
    public int Entered, Exited;
    public void OnCursorEnter() { Entered++; }
    public void OnCursorExit() { Exited++; }
}

public static class TileHoverProgram
{
    private static int _assertions;
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException(message); }
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object value, string property, object replacement)
    {
        var found = value.GetType().GetProperty(property)!;
        found.DeclaringType!.GetProperty(property)!.GetSetMethod(true)!.Invoke(value, new[] { replacement });
    }
    private static CObjectTrap Trap(string guid)
    {
        var trap = new CObjectTrap(); Set(trap, "PropGuid", guid); Set(trap, "PrefabName", "BearTrap"); return trap;
    }
    private static readonly MethodInfo RunHover = typeof(HoverPickPatch).GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Tick(HoverRegisterer registerer)
    {
        try { Check(!(bool)RunHover.Invoke(null, new object[] { registerer })!, "scenario hover never falls through to parked mouse"); }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    private static CTile Tile(Transform root, string name, Vector3 local, Mesh mesh)
    {
        var go = new GameObject(name); go.SetActive(false); go.layer = 9;
        go.transform.SetParent(root, false); go.transform.localPosition = local;
        var behaviour = go.AddComponent<TileBehaviour>(); behaviour.enabled = false; var tile = new CTile();
        behaviour.m_ClientTile = new CClientTile { m_GameObject = go, m_Tile = tile, m_TileBehaviour = behaviour };
        var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true;
        go.SetActive(true); return tile;
    }
    private static void Aim(Transform board, Vector3 floor, BoardPick.PickSource source = BoardPick.PickSource.Far)
    {
        Vector3 origin = board.TransformPoint(new Vector3(-2f, 2f, floor.z));
        BoardPick.Ray(origin, board.TransformPoint(floor) - origin, source);
    }
    public static int Run(string nativePath)
    {
        _assertions = 0; NativeHex native = JsonUtility.FromJson<NativeHex>(File.ReadAllText(nativePath));
        var mesh = new Mesh { vertices = native.vertices, triangles = native.triangles }; mesh.RecalculateBounds();
        Check(native.hexBoxEnabled == false && native.hexConvex, "native selection is the convex hex mesh, never its disabled rectangular box");
        var controllerHost = new GameObject("native Controller identity"); controllerHost.SetActive(false);
        var controller = controllerHost.AddComponent<Controller>(); controller.m_HexSelectionRaycastLayer = 1 << 9;
        controller.m_ActiveSelectionRaycastLayer = 1 << 9;
        typeof(Controller).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, controller);
        foreach (float scale in new[] { .25f, 1f, 4f })
        foreach (float yaw in new[] { 0f, 37f, 123f })
        {
            var board = new GameObject("board"); board.transform.position = new Vector3(4f, 0f, -3f);
            board.transform.rotation = Quaternion.Euler(0f, yaw, 0f); board.transform.localScale = Vector3.one * scale;
            var own = Tile(board.transform, "own native hex", Vector3.zero, mesh);
            var neighbour = Tile(board.transform, "neighbour native hex", new Vector3(native.pitch, 0, 0), mesh);
            var trap = Trap("target"); own.m_Props.Add(trap);
            var other = Trap("other"); neighbour.m_Props.Add(other);
            var prop = new GameObject(trap.InstanceName); prop.SetActive(false); prop.layer = 8;
            prop.transform.SetParent(board.transform, false);
            var authored = prop.AddComponent<UnityGameEditorObject>(); authored.enabled = false; authored.PropObject = trap;
            var box = prop.AddComponent<BoxCollider>(); box.size = native.trapSize; box.center = native.trapCenter;
            var counter = prop.AddComponent<HoverCounter>(); prop.SetActive(true);
            var host = new GameObject("native HoverRegisterer"); host.SetActive(false);
            var registerer = host.AddComponent<HoverRegisterer>();
            typeof(HoverRegisterer).GetField("targetLayer", Fields)!.SetValue(registerer, (LayerMask)(1 << 8));
            var hovered = new List<IHoverable>();
            typeof(HoverRegisterer).GetField("m_CursorTargets", Fields)!.SetValue(registerer, hovered);
            typeof(HoverRegisterer).GetField("m_CurrentRaycastTargets", Fields)!.SetValue(registerer, new List<IHoverable>());
            Physics.SyncTransforms(); HoverPickPatch.Reset();

            Aim(board.transform, new Vector3(.7f, 0f, 0f));
            Check(BoardPick.TryGetHoveredTile(out var picked) && ReferenceEquals(picked, own), "inside edge resolves original native own hex");
            Tick(registerer); Tick(registerer);
            Check(counter.Entered == 1 && counter.Exited == 0 && hovered.Count == 1, "own native tile enters once and remains stable");

            // An oblique laser still intersects the original unit-cube BearTrap collider, but
            // its floor point is only 17 mm past this hex edge, inside the adjacent real hex.
            Aim(board.transform, new Vector3(.87f, 0f, 0f));
            Check(BoardPick.TryGetGameRay(out var origin, out var direction, out var max)
                  && Physics.Raycast(origin, direction, max, 1 << 8), "native trap collider reproduces the early off-tile hit");
            Check(BoardPick.TryGetHoveredTile(out picked) && ReferenceEquals(picked, neighbour), "native adjacent hex wins the actual floor pick");
            Tick(registerer);
            Check(counter.Exited == 1 && hovered.Count == 0, "off-tile laser drains original hover exactly once");
            Tick(registerer);
            Check(counter.Entered == 1 && counter.Exited == 1, "neighbouring native hex never enters trap hover");

            // Preserve the native edge result too. An oblique ray may continue through the
            // narrow top-face gap into the neighbour's side; neither outcome belongs to own.
            Aim(board.transform, new Vector3(.86f, 0f, 0f));
            Check(!BoardPick.TryGetHoveredTile(out picked) || !ReferenceEquals(picked, own), "native edge never snaps back to the original tile");
            Tick(registerer); Check(hovered.Count == 0, "edge-gap laser never enters trap hover");
            var neighbourCollider = board.transform.GetChild(1).GetComponent<MeshCollider>();
            neighbourCollider.enabled = false; Physics.SyncTransforms();
            Aim(board.transform, new Vector3(.87f, 0f, 0f));
            Check(!BoardPick.TryGetHoveredTile(out picked), "laser beyond the outer native hex edge hits no tile");
            Tick(registerer); Check(hovered.Count == 0, "outer-board miss never enters an overhanging prop hover");
            neighbourCollider.enabled = true; Physics.SyncTransforms();

            Aim(board.transform, new Vector3(.7f, 0f, 0f)); Tick(registerer);
            Check(counter.Entered == 2, "returning to the actual tile re-enters normally");
            PointerBoundary.IsPointerOverUI = true; Tick(registerer); PointerBoundary.IsPointerOverUI = false;
            Check(counter.Exited == 2 && hovered.Count == 0, "native UI gate still pairs the active hover exit");
            BoardPick.ActiveMask = 1 << 8; controller.m_ActiveSelectionRaycastLayer = 1 << 8;
            Aim(board.transform, new Vector3(.7f, 0f, 0f));
            Check(!BoardPick.TryGetHoveredTile(out picked), "different active selection mask sees a prop instead of a hex");
            Tick(registerer); Check(hovered.Count == 1, "passive tooltip keeps the native hex mask across active selection changes");
            BoardPick.ActiveMask = 1 << 9; controller.m_ActiveSelectionRaycastLayer = 1 << 9;

            // Exact current membership, not prefab type or the visual's possibly old reference.
            own.m_Props.Clear(); own.m_Props.Add(other);
            Check(!TileHoverAdmission.Contains(own, prop), "same prefab with a different GUID cannot steal another tile's tooltip");
            own.m_Props[0] = Trap("target");
            Check(TileHoverAdmission.Contains(own, prop), "native rules refresh retains exact immutable prop identity");
            authored.PropObject = null; Check(TileHoverAdmission.Contains(own, prop), "authored initial prop resolves by the original exact instance name");
            prop.name = other.InstanceName; Check(!TileHoverAdmission.Contains(own, prop), "unbound visual cannot match a different same-prefab instance");
            prop.name = trap.InstanceName; authored.PropObject = trap;
            own.m_Props.Clear(); var spawner = new CSpawner(); Set(spawner, "Prop", trap); own.m_Spawners.Add(spawner);
            Check(TileHoverAdmission.Contains(own, prop), "native spawner tile membership remains hoverable");
            own.m_Spawners.Clear();
            Aim(board.transform, new Vector3(.7f, 0f, 0f), BoardPick.PickSource.Near); Tick(registerer);
            Check(hovered.Count == 1, "fingertip collider reach retains its original admission");
            Aim(board.transform, new Vector3(.7f, 0f, 0f), BoardPick.PickSource.None); Tick(registerer);
            Check(hovered.Count == 0, "missing VR pointer drains original hover without parked-mouse fallback");
            Object.DestroyImmediate(board); Object.DestroyImmediate(host); Physics.SyncTransforms();
        }
        typeof(Controller).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
        Object.DestroyImmediate(controllerHost); Object.DestroyImmediate(mesh); return _assertions;
    }
    [Serializable] private sealed class NativeHex
    {
        public Vector3[] vertices = null!; public int[] triangles = null!;
        public Vector3 trapSize = default, trapCenter = default;
        public float pitch = 0; public bool hexBoxEnabled = false, hexConvex = false;
    }
}
