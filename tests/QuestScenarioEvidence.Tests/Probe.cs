using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Cards
{
    internal static class QuestArtworkEvidence { internal static void ScenarioSources() { } }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog
    {
        internal static bool Debug;
        internal static int Warnings, Notes, Alerts;
        internal static void Note(string category, string message) { Notes++; UnityEngine.Debug.Log(message); }
        internal static void Alert(string category, string message) { Alerts++; UnityEngine.Debug.LogWarning(message); }
        internal static bool Wants(VRLogLevel level) => Debug;
        internal static void Warn(string category, string message) { Warnings++; UnityEngine.Debug.LogWarning(message); }
    }
    internal static class VRSession { internal static bool IsRunning = true; internal static bool? InputFocus; internal static HarmonyLib.Harmony Harmony; }
    internal static class MaterialLoaderHeal
    {
        internal static readonly Dictionary<int, string> States = new Dictionary<int, string>();
        internal static string DescribeForRenderer(Renderer renderer)
            => States.TryGetValue(renderer.GetInstanceID(), out string value) ? value : "no-loader";
    }
    internal static class ScenarioEnvironmentBudget
    {
        internal static readonly Dictionary<Material, Material> Originals = new Dictionary<Material, Material>();
        internal static Material CanonicalMaterial(Material material)
            => Originals.TryGetValue(material, out Material original) ? original : material;
    }
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
    public int LoadingClosures;
    public IEnumerator WaitForPlayers() { yield break; }
    public void DisableLoadingScreen() { IsLoading = false; LoadingClosures++; }
}
public class ProceduralBase : MonoBehaviour
{
    private bool _completed;
    public bool IsPlacementCompleted
    {
        get => _completed;
        set { _completed = value; if (value) ContentPlacementCompleted?.Invoke(this); }
    }
    public event Action<ProceduralBase> ContentPlacementCompleted;
}
public class ProceduralTileObserver : ProceduralBase { }
public class ProceduralMapTile : ProceduralTileObserver
{
    public enum Visibility { Hidden, Preview, PreviewWithDoors, All }
    public Visibility visibility;
}
public class ApparanceEntity : MonoBehaviour
{
    public bool IsBusy, IsPopulated = true, Frozen;
    public int m_EntityHandle = 42;
    public uint ProcedureID = 123;
    public object PartialParameterOverride { get; set; } = new object();
}
public class ApparanceMeshInstances : MonoBehaviour { public int instanceCount; }
public class ApparanceEngine : MonoBehaviour
{
    public static int NativeStarts, NativeUpdates;
    private void UpdateEngine() { }
    private void Start() { NativeStarts++; m_LastRunTime = s_LastRunTime; }
    private void Update() { NativeUpdates++; }
    public static ApparanceEngine Instance;
    public bool EnableInstancedRendering;
    public ApparanceResources Resources;
    public static long s_LastRunTime = 2;
    public long m_LastRunTime = 1;
    // Controlled engine boundary for the production observer's fail-closed path;
    // this fixture never executes original Apparance managed/native engine code.
    public static bool FailCensus;
    public static int CensusReads;
    private readonly List<ApparanceEntity> _entities = new List<ApparanceEntity>();
    public List<ApparanceEntity> EntityComponents
    {
        get
        {
            CensusReads++;
            if (FailCensus) throw new InvalidOperationException("Controlled engine census failure");
            return _entities;
        }
    }
}
public class ApparanceResources : MonoBehaviour { }
public class ObjectOcclusionVolume : MonoBehaviour { }
public class TilesOcclusionVolume : MonoBehaviour { }
public class MaterialLoader : MonoBehaviour
{
    public List<MaterialLoaderData> LoadersData = new List<MaterialLoaderData>();
}
public class MaterialLoaderData
{
    public Renderer Renderer;
    public List<object> MaterialReferences = new List<object>();
    public FixtureMaterialHandle[] _handles;
    public Material[] _loadedMaterials;
}
public struct FixtureMaterialHandle
{
    public bool Valid, IsDone;
    public Material Result;
    public bool IsValid() => Valid;
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
        internal void RunLoadingWait() { _questScenarioDue = Time.realtimeSinceStartup - 1f; TickQuestScenarioEvidence(); }
        internal void RunLoadingTimeout() { _questScenarioDue = Time.realtimeSinceStartup - 31f; TickQuestScenarioEvidence(); }
        internal static int LoaderIndex(string text) => QuestScenarioLoaderIndex(text);
        internal static string InactiveRoot(Transform source) => QuestScenarioInactiveRoot(source);
        internal static RenderTexture DrawProbe(Renderer renderer, Material material)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (!TryQuestScenarioPart(renderer, mesh, out int part, out _)) throw new InvalidOperationException("No native triangle material part.");
            return RenderQuestScenarioProbe(renderer, mesh, material, null, part);
        }
        internal static bool SelectPart(Renderer renderer, out int part, out Material material)
            => TryQuestScenarioPart(renderer, renderer.GetComponent<MeshFilter>().sharedMesh, out part, out material);
        internal static Material[] EmissionSlots(Renderer renderer) => QuestScenarioEmissiveSlots(renderer);
        internal static RenderTexture DrawLitProbe(Renderer renderer, Material control = null, Material[] overrides = null)
            => RenderQuestScenarioObserved(renderer, renderer.GetComponent<MeshFilter>().sharedMesh, control,
                QuestScenarioLights(renderer.gameObject.layer, out _), overrides);
        internal static int ActualLightCount(Renderer renderer) => QuestScenarioLights(renderer.gameObject.layer, out _).Count;
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
            wall.GetComponent<MeshRenderer>().sharedMaterial = null;
            var previewTile = new GameObject("Preview tile").AddComponent<ProceduralMapTile>();
            previewTile.visibility = ProceduralMapTile.Visibility.Preview;
            previewTile.gameObject.AddComponent<ApparanceEntity>();
            var previewRoot = new GameObject("Generated Content"); previewRoot.transform.parent = previewTile.transform;
            var previewFloor = GameObject.CreatePrimitive(PrimitiveType.Cube); previewFloor.name = "Healthy preview floor";
            previewFloor.layer = 10; previewFloor.transform.parent = previewRoot.transform;
            previewFloor.transform.localScale = new Vector3(8, .1f, 8);
            GloomhavenVR.Core.MaterialLoaderHeal.States[floorRenderer.GetInstanceID()] = "never-started loader='active native source'";
            GloomhavenVR.Core.MaterialLoaderHeal.States[wall.GetComponent<Renderer>().GetInstanceID()] = "never-started loader='excluded preview'";
            var instances = generated.AddComponent<ApparanceMeshInstances>(); instances.instanceCount = 7;
            var driver = root.AddComponent<GloomhavenVR.Rig.VRRigDriver>(); driver.Configure(head, anchor, true);

            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = false; GloomhavenVR.Core.VRLog.Debug = true;
            driver.RunNow(); Check(Records.Count == 0, "Desktop does not observe native scene");
            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = true; GloomhavenVR.Core.VRLog.Debug = false;
            driver.RunNow(); Check(Records.Count == 0, "Normal logging does not observe native scene");
            GloomhavenVR.Core.VRLog.Debug = true; driver.Configure(head, anchor, false);
            driver.RunNow(); Check(Records.Count == 0, "Menu does not consume late scenario evidence");
            driver.Configure(head, anchor, false); GloomhavenVR.Core.Events.VRModeStateMachine.ScenarioBoardExists = true;
            SceneController.Instance = root.AddComponent<SceneController>(); SceneController.Instance.ScenarioIsLoading = true;
            driver.RunLoadingWait(); Check(Records.Count == 0, "Loading cannot spend both settled scenario observations");
            driver.RunLoadingTimeout();
            Check(Records.Count == 4, "One real native scenario snapshot emits four bounded records");
            Check(Records[0].Contains("rig=Menu") && Records[0].Contains("board=True"), "Stuck menu rig still emits actual scenario evidence");
            Check(Records[0].Contains("loading=True"), "Stuck native loading cannot hide evidence beyond the bounded grace");
            Check(Records[0].Contains("headMask=00000400") && Records[0].Contains("scenarioMask=00000400"), "Actual camera masks are reported");
            Check(Records[1].Contains("tiles=2 active=2 all=1 complete=1 busy=0 generatedRoots=2"), "Actual generation and placement state is reported");
            Check(Records[1].Contains("renderers=3") && Records[1].Contains("layerExcluded=1"), "Actual renderer layer refusal is counted");
            Check(Records[1].Contains("unsupported=1 unsupportedShown=0 unloadedShown=1"),
                "Excluded native placeholder materials cannot be diagnosed as shown shader failures");
            Check(Records[1].Contains("rooms=name:visibility:complete:handle:shown/scanned(cap3):")
                && Records[1].Contains("Native tile:All:1:1:1/2;"),
                "Room visibility, placement and actual admitted renderer counts remain distinct");
            Check(Records[1].Contains("Preview tile:Preview:0:1:1/1;"),
                "Visible native preview content is not incorrectly counted as zero by full-room admission");
            Check(Records[0].Contains("headForward=0,-0.707,0.707") && Records[1].Contains("frustum=mono"),
                "Camera direction and mono frustum are not claimed as actual XR eye culling");
            Check(Records[1].Contains("activeInstancers=1 instances=7"), "Native non-renderer instancing is counted independently");
            Check(Records[2].Contains("role=floor") && Records[2].Contains("renderer=Floor main"), "Floor representative is a real native renderer");
            Check(!Records[2].Contains("Healthy preview floor"), "A larger healthy preview floor cannot mask the requested All-room failure");
            Check(Records[2].Contains("Normal:Float32:3") && Records[2].Contains("TexCoord0:Float32:2"), "Native vertex format and UV inputs are observed");
            Check(!Records[2].Contains("Color:Float32"), "Absent color stays absent, not rewritten");
            Check(floor.GetComponent<MeshFilter>().sharedMesh == original && floorRenderer.sharedMaterial == originalMaterial, "Diagnostic preserves actual native mesh and material identity");
            Check(original.vertexCount == 24 && !original.HasVertexAttribute(VertexAttribute.Color), "Diagnostic preserves actual native vertex attributes");
            SceneController.Instance.ScenarioIsLoading = false;
            floorRenderer.enabled = false;
            previewFloor.SetActive(false);
            wall.SetActive(false); // Component still enabled: distinguish the native hide owners.
            tile.IsPlacementCompleted = false;
            ApparanceEngine.Instance.enabled = false;
            ApparanceEngine.Instance.EntityComponents[0].m_EntityHandle = 0;
            GloomhavenVR.Core.MaterialLoaderHeal.States[floorRenderer.GetInstanceID()] = "loading 1/2";
            GloomhavenVR.Core.MaterialLoaderHeal.States[wall.GetComponent<Renderer>().GetInstanceID()] = "never-started loader='inactive preview'";
            driver.RunNow(); Check(Records.Count == 8, "Second observation uses the complete eight-record launch budget");
            Check(Records[5].Contains("enabled=0 ") && Records[5].Contains("componentEnabled=2 hierarchyActive=1"),
                "Inactive and component-disabled native renderers are counted independently");
            Check(Records[5].Contains("rootActive=2 rootChildren=3") && Records[5].Contains("entityEnabled=2 populated=2 handles=1 frozen=0 noProcedure=0 parameterized=2"),
                "Native hierarchy and generation admission distinguish a dormant entity from a completed world");
            Check(Records[5].Contains("tileEnabled=2 observers=2/2"), "Actual tile and inherited observer callbacks are counted independently of their entities");
            Check(Records[5].Contains("engineActive=False"), "Actual original engine state remains distinct from a registered preview handle");
            Check(Records[5].Contains("ml=no/empty/never/loading/failed/unassigned/foreign/done:1,0,1,1,0,0,0,0"),
                "Material-loader diagnosis remains independent of renderer enabled and hierarchy flags");
            Check(Records[5].Contains("unsupported=1 unsupportedShown=0 unloadedShown=0"),
                "Inactive native preview loaders do not imply a missing visible room material");
            Check(Records[5].Contains("Native tile:All:0:0:0/2;"), "An ungenerated native room is reported without fabricated completion");
            Check(Records[6].Contains("role=hidden-candidate") && Records[6].Contains("renderer=Floor main")
                && Records[6].Contains("enabled=False active=True") && Records[6].Contains("ml=loading 1/2"),
                "A completely black world still reports an actual disabled native mesh and loader state");
            Check(!floorRenderer.enabled && !wall.activeSelf && wall.GetComponent<Renderer>().enabled,
                "Read-only evidence cannot reveal hidden native content or finish its material load");
            Check(Records[6].Contains("indices=36") && Records[6].Contains("shader="), "Native hidden candidate retains topology and actual shader evidence");
            Check(GloomhavenVR.Rig.VRRigDriver.InactiveRoot(wall.transform) == "Native wall", "Nearest native inactive root is named");
            Check(GloomhavenVR.Rig.VRRigDriver.InactiveRoot(floor.transform) == "none", "A component-disabled renderer has no invented inactive ancestor");
            foreach (var state in new[] { "no-loader", "empty-refs", "never-started loader='source'", "loading 1/2", "null-result 1/2", "done-stuck", "done-disabled(foreign)", "done" }.Select((value, index) => new { value, index }))
                Check(GloomhavenVR.Rig.VRRigDriver.LoaderIndex(state.value) == state.index, "Actual existing loader-state token is counted: " + state.value);
            driver.RunNow(); Check(Records.Count == 8, "Same rig stops after bounded evidence");
            var next = new GameObject("Next rig").AddComponent<GloomhavenVR.Rig.VRRigDriver>(); next.Configure(head, anchor, true); next.RunNow();
            Check(Records.Count == 8, "Recreated rig cannot reset launch cap");
            Check(Records.All(x => Encoding.UTF8.GetByteCount(x) <= 904), "Every complete record fits one Android logcat message");
            Lifecycle();
            Directory.CreateDirectory("ProbeOutput");
            File.WriteAllLines("ProbeOutput/records.log", Records);
            File.WriteAllText("ProbeOutput/result.json", "{\"assertions\":" + _assertions + ",\"hardwareVerified\":false}\n");
        }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally { Application.logMessageReceived -= Capture; UnityEditor.EditorApplication.Exit(exit); }
    }
    private static void Capture(string text, string stack, LogType type)
    { if (text.StartsWith("[Quest startup] scenario ", StringComparison.Ordinal)) Records.Add(text); }

    private static void Lifecycle()
    {
        // Real Unity enabled/hierarchy state; the bridge and native task decoder
        // are external boundaries. A successful source fixture cannot establish
        // that the Android worker produces a complete playable room.
        foreach (ApparanceEngine previous in UnityEngine.Object.FindObjectsOfType<ApparanceEngine>(true))
            UnityEngine.Object.DestroyImmediate(previous);
        foreach (ProceduralMapTile previous in UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>(true))
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBeforeWorld.unity"))
            throw new InvalidOperationException("Could not retain the existing diagnostic scene fixture.");
        Scene unified = NativeScene("Gloomhaven_unified");
        NativeScene("Game");
        NativeScene("ProcGen");
        var host = new GameObject("Apparance Engine"); SceneManager.MoveGameObjectToScene(host, unified);
        var engine = host.AddComponent<ApparanceEngine>(); ApparanceEngine.Instance = engine;
        engine.Resources = host.AddComponent<ApparanceResources>();
        engine.enabled = false;
        var tile = new GameObject("Requested native tile").AddComponent<ProceduralMapTile>();
        tile.visibility = ProceduralMapTile.Visibility.All;
        var entity = tile.gameObject.AddComponent<ApparanceEntity>(); entity.m_EntityHandle = 0;
        engine.EntityComponents.Add(entity);
        var generated = new GameObject("Generated Content"); generated.transform.parent = tile.transform;
        var loading = SceneController.Instance;
        loading.IsLoading = loading.ScenarioIsLoading = false;
        const float before = 1f, after = 5f;
        Action consider = () => { GloomhavenVR.Core.QuestProceduralLifecycle.Tick(before, false); GloomhavenVR.Core.QuestProceduralLifecycle.Tick(after, false); };
        Action reset = () => { GloomhavenVR.Core.QuestProceduralLifecycle.Uninstall(); engine.enabled = false; };

        GloomhavenVR.Core.QuestStandalonePlatform.Enabled = false;
        consider(); Check(!engine.enabled, "Desktop cannot recover a native component");
        GloomhavenVR.Core.QuestStandalonePlatform.Enabled = true;
        GloomhavenVR.Core.VRSession.IsRunning = false;
        consider(); Check(!engine.enabled, "Stopped VR cannot recover a native component");
        GloomhavenVR.Core.VRSession.IsRunning = true;
        GloomhavenVR.Core.QuestProceduralLifecycle.Tick(before, true);
        GloomhavenVR.Core.QuestProceduralLifecycle.Tick(after, true);
        Check(!engine.enabled, "Paused application cannot recover a native component");
        Check(GloomhavenVR.Core.QuestProceduralLifecycle.Describe(engine).Contains("admission=os-paused"), "OS pause reports its actual admission blocker");
        loading.ScenarioIsLoading = true;
        loading.IsLoading = true;
        GloomhavenVR.Core.QuestProceduralLifecycle.Tick(before, false);
        Check(engine.enabled && loading.IsLoading && loading.ScenarioIsLoading,
            "Original generation resumes during loading without changing native loading ownership");
        reset(); loading.IsLoading = loading.ScenarioIsLoading = false;
        host.SetActive(false);
        consider(); Check(!engine.enabled && !host.activeSelf, "Inactive native owner is never activated");
        host.SetActive(true);
        var duplicate = new GameObject("Inactive duplicate engine"); duplicate.SetActive(false); duplicate.AddComponent<ApparanceEngine>();
        consider(); Check(!engine.enabled, "Duplicate native owners prevent an Instance ownership guess");
        Check(GloomhavenVR.Core.QuestProceduralLifecycle.Describe(engine).Contains("loaded=2 active=0"), "Census retains disabled and inactive duplicate owners");
        UnityEngine.Object.DestroyImmediate(duplicate);
        engine.Resources = null;
        consider(); Check(!engine.enabled, "Incomplete native resources prevent lifecycle recovery");
        engine.Resources = host.GetComponent<ApparanceResources>();
        entity.m_EntityHandle = 42;
        consider(); Check(!engine.enabled, "An existing native handle prevents initial-generation recovery");
        entity.m_EntityHandle = 0;
        // ApparanceEntity.Start can create a handle before the disabled engine's
        // first Start. Another registered owner is not evidence of a running
        // engine. Preserve both this pooled entity and an already generated
        // preview tile while the actual visible room requests initial work.
        var pooled = new GameObject("Pooled non-room entity").AddComponent<ApparanceEntity>();
        pooled.m_EntityHandle = 77; pooled.gameObject.SetActive(false); engine.EntityComponents.Add(pooled);
        consider();
        Check(engine.enabled && pooled.m_EntityHandle == 77 && !pooled.gameObject.activeSelf,
            "An unrelated registered native handle cannot block the visible ungenerated room");
        reset();
        var preview = new GameObject("Native preview tile").AddComponent<ProceduralMapTile>();
        preview.visibility = ProceduralMapTile.Visibility.Preview; preview.IsPlacementCompleted = true;
        var previewEntity = preview.gameObject.AddComponent<ApparanceEntity>();
        previewEntity.m_EntityHandle = 88; engine.EntityComponents.Add(previewEntity);
        var previewGenerated = new GameObject("Generated Content"); previewGenerated.transform.parent = preview.transform;
        new GameObject("Native preview leaf").transform.parent = previewGenerated.transform;
        consider();
        Check(engine.enabled && previewEntity.m_EntityHandle == 88 && preview.IsPlacementCompleted && previewGenerated.transform.childCount == 1,
            "Existing native preview content cannot block the visible ungenerated room");
        reset();
        entity.Frozen = true;
        consider(); Check(!engine.enabled && entity.Frozen, "Frozen native entity retains ownership");
        entity.Frozen = false;
        entity.IsPopulated = false;
        consider(); Check(!engine.enabled && !entity.IsPopulated, "Native population gate is never fabricated");
        entity.IsPopulated = true;
        entity.ProcedureID = 0;
        consider(); Check(!engine.enabled, "Missing original procedure cannot be treated as valid");
        entity.ProcedureID = 123;
        tile.visibility = ProceduralMapTile.Visibility.Hidden;
        consider(); Check(!engine.enabled, "Hidden tile never requests lifecycle recovery");
        tile.visibility = ProceduralMapTile.Visibility.All;
        tile.enabled = false;
        consider(); Check(!engine.enabled && !tile.enabled, "Disabled native observer is never enabled");
        tile.enabled = true;
        tile.IsPlacementCompleted = true;
        consider(); Check(!engine.enabled, "Completed placement never requests initial-generation recovery");
        tile.IsPlacementCompleted = false;
        var content = new GameObject("Original generated leaf"); content.transform.parent = generated.transform;
        consider(); Check(!engine.enabled && generated.transform.childCount == 1, "Generated native content prevents initial-generation recovery");
        UnityEngine.Object.DestroyImmediate(content);
        int notes = GloomhavenVR.Core.VRLog.Notes;
        GloomhavenVR.Core.QuestProceduralLifecycle.Tick(before, false);
        Check(engine.enabled && engine.isActiveAndEnabled, "Only the unique disabled native engine resumes its original lifecycle");
        Check(GloomhavenVR.Core.QuestProceduralLifecycle.Describe(engine).Contains("admission=restored"),
            "Qualified native readiness introduces no arbitrary post-loader delay");
        Check(GloomhavenVR.Core.VRLog.Notes == notes + 1, "Successful lifecycle recovery reports one bounded anomaly");
        Check(!tile.IsPlacementCompleted && entity.m_EntityHandle == 0 && entity.IsPopulated && generated.transform.childCount == 0,
            "Recovery cannot fabricate generation handles, placement, parameters or visible geometry");
        string census = GloomhavenVR.Core.QuestProceduralLifecycle.Describe(engine);
        Check(census.Contains("component=True self=True hierarchy=True") && census.Contains("codeStarted=False") && census.Contains("recovery=attempted"),
            "Census reports actual native owner state and original startup epoch");
        engine.enabled = false;
        consider(); Check(!engine.enabled, "A second disable owner is never fought or retried");
        reset();
        consider(); Check(engine.enabled, "A new installed lifetime can recover its own original component");
        GloomhavenVR.Core.QuestProceduralLifecycle.Uninstall();
        Check(engine.enabled, "Shutdown does not break a successfully restored native lifecycle");
        UnityEngine.Object.DestroyImmediate(host);
        UnityEngine.Object.DestroyImmediate(tile.gameObject);
        UnityEngine.Object.DestroyImmediate(pooled.gameObject);
        UnityEngine.Object.DestroyImmediate(preview.gameObject);
    }
    private static Scene NativeScene(string name)
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Additive);
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/" + name + ".unity"))
            throw new InvalidOperationException("Could not create native scene fixture: " + name);
        return scene;
    }

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
            int[] triangles = mesh.GetTriangles(0); mesh.subMeshCount = 2;
            mesh.SetTriangles(Array.Empty<int>(), 0); mesh.SetTriangles(triangles, 1);
            renderer.sharedMaterials = new[] { white, material };
            mesh.UploadMeshData(true);
            Check(!mesh.isReadable && !mesh.HasVertexAttribute(VertexAttribute.Color), "Actual mesh is unreadable and has no COLOR input");
            Check(GloomhavenVR.Rig.VRRigDriver.SelectPart(renderer, out int part, out Material selected) && part == 1 && selected == material,
                "Empty submesh0 selects real triangle submesh1 and its actual material slot");
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
            Check(source.GetComponent<MeshFilter>().sharedMesh == mesh && renderer.sharedMaterials[1] == material, "GPU probe keeps actual native mesh and material identity");
            Check(!mesh.isReadable && !mesh.HasVertexAttribute(VertexAttribute.Color), "GPU probe neither decodes nor adds native vertex data");
            ordinary.Render(); Color32[] after = Read(ordinary.targetTexture);
            Check(before.SequenceEqual(after), "The following ordinary camera's complete RGBA image is unchanged");
            Shader standard = Shader.Find("Standard");
            Check(standard != null && standard.isSupported, "Lighting fixture uses an actual light-consuming native Unity shader");
            var lit = new Material(standard) { color = Color.white, mainTexture = Texture2D.whiteTexture };
            renderer.sharedMaterials = new[] { lit, lit };
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2(); QualitySettings.pixelLightCount = 0;
            var lightObject = new GameObject("Native DontSave room light"); lightObject.hideFlags = HideFlags.DontSave;
            Light nativeLight = lightObject.AddComponent<Light>(); nativeLight.type = LightType.Point;
            nativeLight.transform.position = new Vector3(.2f, 2, -.2f); nativeLight.range = 20;
            nativeLight.intensity = 5; nativeLight.renderMode = LightRenderMode.Auto; nativeLight.cullingMask = 1 << 10;
            var excludedObject = new GameObject("Native light for another layer");
            Light excludedLight = excludedObject.AddComponent<Light>(); excludedLight.type = LightType.Directional;
            excludedLight.intensity = 100; excludedLight.cullingMask = 1 << 11;
            Check(GloomhavenVR.Rig.VRRigDriver.ActualLightCount(renderer) == 1,
                "Only actual source-layer native lights are admitted, including DontSave clones");
            Vector3 lightPosition = nativeLight.transform.position;
            RenderTexture observed = GloomhavenVR.Rig.VRRigDriver.DrawLitProbe(renderer);
            Color32[] litPixels = Read(observed); observed.Release();
            Check(litPixels.Count(pixel => pixel.r > 2 || pixel.g > 2 || pixel.b > 2) > 50,
                "Private native renderer receives original zero-pixel-cap room lighting");
            nativeLight.enabled = false;
            RenderTexture darkObserved = GloomhavenVR.Rig.VRRigDriver.DrawLitProbe(renderer);
            Color32[] darkPixels = Read(darkObserved); darkObserved.Release();
            Check(darkPixels.All(pixel => pixel.r <= 2 && pixel.g <= 2 && pixel.b <= 2),
                "Removing the actual native light extinguishes the lit probe, without borrowing an excluded light");
            Check(nativeLight.transform.position == lightPosition && !nativeLight.enabled && nativeLight.cullingMask == 1 << 10
                && excludedLight.enabled && excludedLight.cullingMask == 1 << 11,
                "Observation leaves actual native light pose, mode, mask and enabled state untouched");
            Check(Resources.FindObjectsOfTypeAll<Light>().Where(light => light.name == "Observed native light").All(light => !light.enabled),
                "Private copied lights cannot affect the following head render");
            Check(Resources.FindObjectsOfTypeAll<Renderer>().Where(item => item.name == "Observed native mesh").All(item => !item.enabled),
                "Private copied geometry is disabled before ordinary rendering resumes");
            Check(renderer.sharedMaterials[0] == lit && renderer.sharedMaterials[1] == lit && source.GetComponent<MeshFilter>().sharedMesh == mesh,
                "All native material slots and unreadable geometry remain unchanged");
            Check(GloomhavenVR.Rig.VRRigDriver.EmissionSlots(renderer) == null,
                "An unrelated native shader cannot enter the exact Amp_Basic emission observation");
            Shader emissionShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeEmission.shader");
            Check(emissionShader != null && emissionShader.isSupported && emissionShader.name == "Amp_Basic",
                "Emission binding fixture has the exact admitted shader and explicit native property shape");
            var emission0 = new Material(emissionShader) { mainTexture = Texture2D.whiteTexture };
            emission0.SetColor("_Tint", new Color(.2f, .7f, .4f, 0));
            var emission1 = new Material(emission0);
            renderer.sharedMaterials = new[] { emission0, lit };
            Check(GloomhavenVR.Rig.VRRigDriver.EmissionSlots(renderer) == null,
                "Mixed native material slots are refused before any partial emissive override");
            renderer.sharedMaterials = new[] { emission0, emission1 };
            var nativeBlock = new MaterialPropertyBlock(); nativeBlock.SetFloat("_EmissionMap", 0);
            renderer.SetPropertyBlock(nativeBlock);
            Check(GloomhavenVR.Rig.VRRigDriver.EmissionSlots(renderer) == null,
                "Native property blocks cannot silently veto an allegedly emissive material arm");
            renderer.SetPropertyBlock(null);
            Material[] emissionSlots = GloomhavenVR.Rig.VRRigDriver.EmissionSlots(renderer);
            Check(emissionSlots != null && emissionSlots.Length == 2 && emissionSlots[0] != emission0 && emissionSlots[1] != emission1,
                "Native emission observation owns distinct complete material copies");
            Check(emissionSlots[0].GetColor("_Tint") == emission0.GetColor("_Tint") && emissionSlots[0].mainTexture == emission0.mainTexture,
                "Private native emission copies preserve actual tint and texture inputs");
            RenderTexture emissionObserved = GloomhavenVR.Rig.VRRigDriver.DrawLitProbe(renderer, null, emissionSlots);
            Color32[] emissionPixels = Read(emissionObserved); emissionObserved.Release();
            Check(emissionPixels.Count(pixel => pixel.g > 100) > 50,
                "Private native emission arm draws actual texture color without scene lights");
            RenderTexture unmodifiedObserved = GloomhavenVR.Rig.VRRigDriver.DrawLitProbe(renderer);
            Color32[] unmodifiedPixels = Read(unmodifiedObserved); unmodifiedObserved.Release();
            Check(unmodifiedPixels.All(pixel => pixel.r <= 2 && pixel.g <= 2 && pixel.b <= 2),
                "Following native draw retains its original non-emissive output");
            Check(emission0.GetFloat("_EmissionMap") == 0 && emission1.GetFloat("_EmissionMap") == 0
                && renderer.sharedMaterials[0] == emission0 && renderer.sharedMaterials[1] == emission1,
                "Emission observation never edits a source material property or live slot");
            foreach (Material copy in emissionSlots) UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(emission0); UnityEngine.Object.DestroyImmediate(emission1);
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
