using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog { internal static bool Debug; internal static bool Wants(VRLogLevel level) => Debug; }
    internal static class BundleShaders
    {
        internal static Shader Resolve(string name, string scope, string reason, string fallback)
            => UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/HeadUnlit.shader");
    }
    internal static class PerfConfig
    {
        internal static int WorldMaterialQualityMode => 0;
        internal static bool TerrainSubstitutionOn => true;
        internal static bool CheapWallShadingOn => false;
        internal static int TerrainDetailPercent => 100;
        internal static bool StaticScenarioBatchesOn => false;
        internal static bool EnvironmentDrawInstancingOn => false;
    }
}
namespace GloomhavenVR.Core.Events
{
    internal enum VRMode { Menu2D, TableIdle }
    internal static class VRModeStateMachine
    {
        internal static bool ScenarioBoardExists;
        internal static VRMode CurrentMode;
    }
}
public class SceneController : MonoBehaviour
{
    public static SceneController Instance;
    public bool IsLoading, ScenarioIsLoading;
}
public class ProceduralMapTile : MonoBehaviour
{
    public enum Visibility { Hidden, Preview, PreviewWithDoors, All }
    public Visibility visibility;
    public bool IsPlacementCompleted;
}
public class ApparanceEntity : MonoBehaviour { public bool IsBusy; }
public class ApparanceMeshInstances : MonoBehaviour { public int instanceCount; }
public class ApparanceEngine : MonoBehaviour
{
    public static ApparanceEngine Instance;
    public bool EnableInstancedRendering;
    public List<ApparanceEntity> EntityComponents = new List<ApparanceEntity>();
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver : MonoBehaviour
    {
        private enum RigKind { None, Menu, Scenario }
        private RigKind _kind;
        private Camera _camera, _anchor;
        private Camera ResolveScenarioCamera() => _anchor;
        internal void Configure(Camera head, Camera anchor, bool scenario)
        { _camera = head; _anchor = anchor; _kind = scenario ? RigKind.Scenario : RigKind.Menu; }
        internal void RunNow() { _questScenarioDue = float.NegativeInfinity; TickQuestScenarioEvidence(); }
        internal static RenderTexture DrawProbe(Renderer renderer, Material material)
            => RenderQuestScenarioProbe(renderer, renderer.GetComponent<MeshFilter>().sharedMesh, material, null);
    }
}
public static class QuestScenarioEvidenceProbe
{
    private static readonly List<string> Records = new List<string>();
    private static int _assertions;
    private static void Check(bool value, string text)
    { _assertions++; if (!value) throw new InvalidOperationException(text); }

    public static void Run()
    {
        int exit = 0;
        try
        {
            Application.logMessageReceived += Capture;
            var root = new GameObject("Fixture scenario");
            var head = root.AddComponent<Camera>(); head.enabled = false; head.cullingMask = 1 << 10;
            head.transform.position = new Vector3(0, 3, -3); head.transform.LookAt(Vector3.zero);
            var anchor = new GameObject("ScenarioCamera").AddComponent<Camera>(); anchor.enabled = false; anchor.cullingMask = 1 << 10;
            ApparanceEngine.Instance = root.AddComponent<ApparanceEngine>();
            var tile = new GameObject("Native tile").AddComponent<ProceduralMapTile>();
            tile.visibility = ProceduralMapTile.Visibility.All; tile.IsPlacementCompleted = true;
            ApparanceEngine.Instance.EntityComponents.Add(tile.gameObject.AddComponent<ApparanceEntity>());
            var generated = new GameObject("Generated Content"); generated.transform.parent = tile.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor main";
            floor.layer = 10; floor.transform.parent = generated.transform; floor.transform.localScale = new Vector3(4, .1f, 4);
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            Mesh original = floor.GetComponent<MeshFilter>().sharedMesh;
            Material originalMaterial = floorRenderer.sharedMaterial;
            Mesh second = UnityEngine.Object.Instantiate(original); second.name = "Generated lit wall";
            var wall = new GameObject("Native wall", typeof(MeshFilter), typeof(MeshRenderer));
            wall.layer = 11; wall.transform.parent = generated.transform;
            wall.GetComponent<MeshFilter>().sharedMesh = second;
            wall.GetComponent<MeshRenderer>().sharedMaterial = originalMaterial;
            var instances = generated.AddComponent<ApparanceMeshInstances>(); instances.instanceCount = 7;
            var driver = root.AddComponent<GloomhavenVR.Rig.VRRigDriver>(); driver.Configure(head, anchor, true);

            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = false; GloomhavenVR.Core.VRLog.Debug = true;
            driver.RunNow(); Check(Records.Count == 0, "Desktop does not observe native scene");
            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = true; GloomhavenVR.Core.VRLog.Debug = false;
            driver.RunNow(); Check(Records.Count == 0, "Normal logging does not observe native scene");
            GloomhavenVR.Core.VRLog.Debug = true; driver.Configure(head, anchor, false);
            driver.RunNow(); Check(Records.Count == 0, "Menu does not consume late scenario evidence");
            driver.Configure(head, anchor, true);
            SceneController.Instance = root.AddComponent<SceneController>(); SceneController.Instance.ScenarioIsLoading = true;
            driver.RunNow(); Check(Records.Count == 0, "Loading cannot spend both settled scenario observations");
            SceneController.Instance.ScenarioIsLoading = false;
            driver.Configure(head, anchor, false); GloomhavenVR.Core.Events.VRModeStateMachine.ScenarioBoardExists = true;
            driver.RunNow();
            Check(Records.Count == 4, "One real native scenario snapshot emits four bounded records");
            Check(Records[0].Contains("rig=Menu") && Records[0].Contains("board=True"), "Stuck menu rig still emits actual scenario evidence");
            Check(Records[0].Contains("headMask=00000400") && Records[0].Contains("scenarioMask=00000400"), "Actual camera masks are reported");
            Check(Records[1].Contains("tiles=1 active=1 all=1 complete=1 busy=0 generatedRoots=1"), "Actual generation and placement state is reported");
            Check(Records[1].Contains("renderers=2") && Records[1].Contains("layerExcluded=1"), "Actual renderer layer refusal is counted");
            Check(Records[1].Contains("activeInstancers=1 instances=7"), "Native non-renderer instancing is counted independently");
            Check(Records[2].Contains("role=floor") && Records[2].Contains("renderer=Floor main"), "Floor representative is a real native renderer");
            Check(Records[2].Contains("Normal:Float32:3") && Records[2].Contains("TexCoord0:Float32:2"), "Native vertex format and UV inputs are observed");
            Check(!Records[2].Contains("Color:Float32"), "Absent color stays absent, not rewritten");
            Check(floor.GetComponent<MeshFilter>().sharedMesh == original && floorRenderer.sharedMaterial == originalMaterial, "Diagnostic preserves actual native mesh and material identity");
            Check(original.vertexCount == 24 && !original.HasVertexAttribute(VertexAttribute.Color), "Diagnostic preserves actual native vertex attributes");
            driver.RunNow(); Check(Records.Count == 8, "Second observation uses the complete eight-record launch budget");
            driver.RunNow(); Check(Records.Count == 8, "Same rig stops after bounded evidence");
            var next = new GameObject("Next rig").AddComponent<GloomhavenVR.Rig.VRRigDriver>(); next.Configure(head, anchor, true); next.RunNow();
            Check(Records.Count == 8, "Recreated rig cannot reset launch cap");
            Check(Records.All(x => Encoding.UTF8.GetByteCount(x) <= 904), "Every complete record fits one Android logcat message");
            Directory.CreateDirectory("ProbeOutput");
            File.WriteAllLines("ProbeOutput/records.log", Records);
            File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions + ",\"hardwareVerified\":false}\n");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally { Application.logMessageReceived -= Capture; UnityEditor.EditorApplication.Exit(exit); }
    }
    private static void Capture(string text, string stack, LogType type)
    { if (text.StartsWith("[Quest startup] scenario ", StringComparison.Ordinal)) Records.Add(text); }

    public static void RunGpu()
    {
        int exit = 0;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Shader shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/HeadUnlit.shader");
            Check(shader != null && shader.isSupported, "Native GPU uses the actual shipped no-COLOR control shader");
            var material = new Material(shader) { color = new Color(.2f, .8f, .4f, 1), mainTexture = Texture2D.whiteTexture };
            var white = new Material(shader) { color = Color.white, mainTexture = Texture2D.whiteTexture };
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube); source.layer = 10;
            var renderer = source.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            source.transform.localScale = new Vector3(2, .1f, 2);
            Mesh mesh = UnityEngine.Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
            source.GetComponent<MeshFilter>().sharedMesh = mesh;
            mesh.UploadMeshData(true);
            Check(!mesh.isReadable && !mesh.HasVertexAttribute(VertexAttribute.Color), "Actual mesh is unreadable and has no COLOR input");
            var ordinary = new GameObject("Ordinary camera").AddComponent<Camera>(); ordinary.enabled = false;
            ordinary.cullingMask = 1 << 10; ordinary.transform.position = new Vector3(0, 3, -3); ordinary.transform.LookAt(Vector3.zero);
            ordinary.targetTexture = new RenderTexture(48, 32, 24);
            ordinary.clearFlags = CameraClearFlags.SolidColor; ordinary.backgroundColor = Color.black;
            ordinary.Render(); Color32[] before = Read(ordinary.targetTexture);
            var sentinel = new RenderTexture(7, 5, 0); sentinel.Create(); RenderTexture.active = sentinel;
            RenderTexture native = GloomhavenVR.Rig.VRRigDriver.DrawProbe(renderer, material);
            Check(RenderTexture.active == sentinel, "Actual native draw restores prior active target");
            Color32[] nativePixels = Read(native);
            Check(nativePixels.Count(x => x.r > 1 || x.g > 1 || x.b > 1) > 50, "Actual unreadable native mesh produces visible GPU pixels");
            RenderTexture control = GloomhavenVR.Rig.VRRigDriver.DrawProbe(renderer, white);
            Color32[] controlPixels = Read(control);
            Check(controlPixels.Count(x => x.r > 200 && x.g > 200 && x.b > 200) > 50, "POSITION/UV control renders the exact unreadable native geometry");
            Check(source.GetComponent<MeshFilter>().sharedMesh == mesh && renderer.sharedMaterial == material, "GPU probe keeps actual native mesh and material identity");
            Check(!mesh.isReadable && !mesh.HasVertexAttribute(VertexAttribute.Color), "GPU probe neither decodes nor adds native vertex data");
            ordinary.Render(); Color32[] after = Read(ordinary.targetTexture);
            Check(before.SequenceEqual(after), "The following ordinary camera's complete RGBA image is unchanged");
            Directory.CreateDirectory("ProbeOutput");
            File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions + ",\"hardwareVerified\":false,\"backend\":\"" + SystemInfo.graphicsDeviceType + "\"}\n");
            native.Release(); control.Release(); sentinel.Release(); ordinary.targetTexture.Release();
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally { RenderTexture.active = previous; UnityEditor.EditorApplication.Exit(exit); }
    }
    private static Color32[] Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
        Color32[] pixels = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = previous; return pixels;
    }
}
