using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// The exact native geometry and census use chains are real. Native lifecycle/script callbacks
// are explicit inert fixture boundaries, not original gameplay execution or headset pixels.
internal static class ArchitectureProgram
{
    [DataContract] private sealed class Data { [DataMember] public NativeMesh[] meshes = null!; [DataMember] public Graph[] graphs = null!; }
    [DataContract] private sealed class NativeMesh { [DataMember] public string key = null!, name = null!, path = null!; [DataMember] public bool readable = false; }
    [DataContract] private sealed class Graph { [DataMember] public string source = null!, root = null!; [DataMember] public Member[] members = null!; }
    [DataContract] private sealed class Member { [DataMember] public string meshKey = null!; [DataMember] public bool ornament = false, unresolved = false; [DataMember] public NativeNode[] chain = null!; }
    [DataContract] private sealed class NativeNode
    { [DataMember] public string name = null!; [DataMember] public float[] localPosition = null!, localRotation = null!, localScale = null!; [DataMember] public NativeComponent[] components = null!; }
    [DataContract] private sealed class NativeComponent
    {
        [DataMember] public string type = null!, script = null!;
        [DataMember] public bool enabled = false, isTrigger = false;
        [DataMember] public float[] Center = null!, Size = null!;
    }
    [DataContract] private sealed class Coverage
    {
        [DataMember] public string scope = "Exact readonly original mesh geometry/census prefab-chain reconstruction; native generation/callbacks are inert boundaries, no hardware claim.";
        [DataMember] public int contexts, originalMeshes, originalMeshUses, positiveUses, admittedUses, admittedUnits, refusedUses, newlyAdmittedUses, admittedTriangles;
        [DataMember] public List<CoverageRow> rows = new();
    }
    [DataContract] private sealed class CoverageRow
    { [DataMember] public string source = null!, root = null!; [DataMember] public int positive, admitted, units, triangles; }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Classifier = typeof(ScenarioSceneryBudget).GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static int _count;
    private static Material _material = null!;
    private static readonly List<Mesh> Meshes = new();
    private static int _density;
    private static bool _throw;
    private static void Check(bool value, string message) { _count++; if (!value) throw new Exception(message); }
    private static GameObject Node(Transform parent, string name)
    { var node = new GameObject(name); node.transform.SetParent(parent, false); return node; }
    private static Mesh Load(ArchitectureNativeFiles.Entry entry)
    {
        Mesh mesh = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(entry.Mesh));
        mesh.name = entry.Name; if (!entry.Readable) mesh.UploadMeshData(true); Meshes.Add(mesh); return mesh;
    }
    private static MeshRenderer Leaf(Transform parent, string name, Mesh original)
    {
        var node = Node(parent, name); node.AddComponent<MeshFilter>().sharedMesh = original;
        var renderer = node.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material; return renderer;
    }
    private static string Kind(MeshRenderer renderer, ProceduralMapTile tile)
    { object?[] args = { renderer, tile, null, null }; Classifier.Invoke(null, args); return args[3]!.ToString()!; }
    private static Component Driver(GameObject host)
    {
        ScenarioSceneryBudget.Install(host); Component driver = host.GetComponents<MonoBehaviour>().Single();
        ((Behaviour)driver).enabled = false; return driver;
    }
    private static void Tick(Component driver, int count = 1)
    { for (int i = 0; i < count; i++) { SceneryClock.Now += .05f; driver.GetType().GetMethod("Update", Private)!.Invoke(driver, null); } }
    private static void Complete(ProceduralMapTile tile)
    { ScenarioSceneryBudget.ContentPlaced(tile); ScenarioSceneryBudget.BeforeLoadingComplete(); }
    private static bool Admit(MeshRenderer renderer, ProceduralMapTile tile) =>
        ScenarioArchitecturalDetailBudget.TryAdmit(renderer, tile, out _);
    private static void NativeComponents(Transform node, NativeNode original)
    {
        foreach (var component in original.components)
        {
            if (component.type == "Transform" || component.type == "MeshFilter" || component.type == "MeshRenderer") continue;
            Type? type = component.type == "MonoBehaviour" ? component.script switch
            {
                "MaterialLoader" => typeof(MaterialLoader), "DetailsDisabler" => typeof(DetailsDisabler),
                "DetailLevelDisableProvider" => typeof(DetailLevelDisableProvider),
                "ImportantObjectsShadowsDisabler" => typeof(ImportantObjectsShadowsDisabler),
                "PropObjectsShadowsDisabler" => typeof(PropObjectsShadowsDisabler),
                _ => typeof(UnknownNativeCallback)
            } : component.type switch
            {
                "BoxCollider" => typeof(BoxCollider), "MeshCollider" => typeof(MeshCollider),
                "SphereCollider" => typeof(SphereCollider), "CapsuleCollider" => typeof(CapsuleCollider),
                "Animator" => typeof(Animator), "Animation" => typeof(Animation), "Light" => typeof(Light),
                "ParticleSystem" => typeof(ParticleSystem), "Rigidbody" => typeof(Rigidbody), "LODGroup" => typeof(LODGroup),
                _ => typeof(UnknownNativeCallback)
            };
            if (node.GetComponent(type) != null) continue;
            Component added = node.gameObject.AddComponent(type);
            if (added is Collider collider)
            {
                collider.enabled = component.enabled; collider.isTrigger = component.isTrigger;
                if (collider is BoxCollider box && component.Center != null && component.Size != null)
                { box.center = V(component.Center); box.size = V(component.Size); }
            }
        }
    }
    private static Vector3 V(float[] value) => new(value[0], value[1], value[2]);
    private static Coverage NativeCensus(Transform parent, ProceduralMapTile tile)
    {
        using var input = File.OpenRead(ArchitectureNativeFiles.Graphs);
        Data data = (Data)new DataContractJsonSerializer(typeof(Data)).ReadObject(input);
        var nativeMeshes = new Dictionary<string, Mesh>();
        foreach (var row in data.meshes)
        {
            Mesh mesh = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(row.path));
            mesh.name = row.name; if (!row.readable) mesh.UploadMeshData(true); Meshes.Add(mesh); nativeMeshes.Add(row.key, mesh);
        }
        var coverage = new Coverage { contexts = data.graphs.Length, originalMeshes = nativeMeshes.Count };
        foreach (Graph graph in data.graphs)
        {
            var holder = Node(parent, "Native graph " + graph.root); holder.AddComponent<ProceduralWall>();
            var generated = Node(holder.transform, "Generated Content");
            var nodes = new Dictionary<string, Transform>();
            var positives = new List<MeshRenderer>();
            var row = new CoverageRow { source = graph.source, root = graph.root };
            foreach (Member member in graph.members)
            {
                Transform previous = generated.transform; string path = "";
                foreach (NativeNode original in member.chain)
                {
                    // Include exact local poses: equal display names are not sufficient
                    // to collapse distinct native authored instances into one density unit.
                    path += "/" + original.name + "|" + String.Join(",", original.localPosition.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
                        + "|" + String.Join(",", original.localRotation.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                    if (!nodes.TryGetValue(path, out Transform node))
                    {
                        node = Node(previous, original.name).transform; nodes.Add(path, node);
                        node.localPosition = V(original.localPosition); node.localScale = V(original.localScale);
                        node.localRotation = new Quaternion(original.localRotation[0], original.localRotation[1], original.localRotation[2], original.localRotation[3]);
                        NativeComponents(node, original);
                    }
                    previous = node;
                }
                // Distinct source uses can share an authored leaf path. Keep any such
                // additional exact mesh member instead of silently dropping an occurrence.
                if (previous.GetComponent<MeshFilter>() != null) previous = Node(previous, "Additional original use").transform;
                previous.gameObject.AddComponent<MeshFilter>().sharedMesh = nativeMeshes[member.meshKey];
                var renderer = previous.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
                if (member.unresolved) previous.gameObject.AddComponent<UnknownNativeCallback>();
                coverage.originalMeshUses++;
                if (member.ornament) positives.Add(renderer);
            }
            var units = new HashSet<int>();
            foreach (MeshRenderer renderer in positives)
            {
                coverage.positiveUses++; row.positive++;
                if (!ScenarioArchitecturalDetailBudget.TryAdmit(renderer, tile, out Transform? unit)) continue;
                Check(unit != null, "native graph accepted ornament has a complete positive unit");
                coverage.admittedUses++; row.admitted++; units.Add(unit!.GetInstanceID());
                Mesh original = renderer.GetComponent<MeshFilter>().sharedMesh;
                int triangles = 0; for (int sub = 0; sub < original.subMeshCount; sub++) triangles += (int)original.GetIndexCount(sub) / 3;
                coverage.admittedTriangles += triangles; row.triangles += triangles;
                if (Kind(renderer, tile) == "Architecture") coverage.newlyAdmittedUses++;
            }
            row.units = units.Count; coverage.admittedUnits += units.Count; coverage.rows.Add(row);
            UnityEngine.Object.DestroyImmediate(holder);
        }
        coverage.refusedUses = coverage.positiveUses - coverage.admittedUses;
        Check(coverage.positiveUses > 21 && coverage.originalMeshUses > coverage.positiveUses,
            "native census retains actual repeated use contexts and non-ornament sibling geometry");
        Check(coverage.admittedUses > 0 && coverage.refusedUses > 0, "native census positive proof admits represented trim and refuses unrepresented or interactive units");
        using (var output = File.Create(ArchitectureNativeFiles.ProofOutput)) new DataContractJsonSerializer(typeof(Coverage)).WriteObject(output, coverage);
        return coverage;
    }
    internal static int Run()
    {
        _count = 0; Meshes.Clear(); _density = 100; _throw = false;
        Scene active = SceneManager.GetActiveScene(); Scene scene = SceneManager.CreateScene("ArchitectureProcGen " + Guid.NewGuid());
        Choreographer.s_Choreographer.m_ProcGenScene = scene;
        var scenario = new GameObject("Exact native architecture fixture"); scenario.AddComponent<ProceduralScenario>();
        SceneManager.MoveGameObjectToScene(scenario, scene);
        var tileObject = Node(scenario.transform, "Native tile"); var tile = tileObject.AddComponent<ProceduralMapTile>();
        tileObject.AddComponent<CInteractableTile>(); SceneRegistry.MapTiles.Tiles.Add(tile);
        var wall = Node(tile.transform, "Native wall"); wall.AddComponent<ProceduralWall>();
        var generated = Node(wall.transform, "Generated Content");
        var host = new GameObject("Architecture fixture driver"); Component? driver = null;
        _material = new Material(Shader.Find("Unlit/Color"));
        try
        {
            PerfConfig.ScenarioSceneryDensityPercentValue = PerfConfig.ScenarioVegetationDensityPercentValue = PerfConfig.ScenarioDecorationDensityPercentValue = 100;
            VRSession.IsRunning = true;
            ScenarioSceneryBudget.ConfigureArchitectureDetailDensity(() => _throw ? throw new InvalidOperationException("fixture density read") : _density);
            Mesh coreMesh = Load(ArchitectureNativeFiles.Core), floorMesh = Load(ArchitectureNativeFiles.Floor);
            var core = Leaf(generated.transform, "Actual retained wall body", coreMesh);
            var floor = Leaf(generated.transform, "Actual retained floor body", floorMesh);
            Check(ScenarioEnvironmentMeshBank.RoomArchitectureRole(coreMesh) == 2 && !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(coreMesh), "actual SHA-backed native core is retained structure");
            Check(ScenarioEnvironmentMeshBank.RoomArchitectureRole(floorMesh) == 1, "actual SHA-backed native floor is retained floor");
            var originals = ArchitectureNativeFiles.Ornaments.Select(Load).ToArray();
            foreach (Mesh mesh in originals) Check(ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(mesh), "actual source SHA and exact ornament signature: " + mesh.name);
            Mesh wrong = UnityEngine.Object.Instantiate(originals[0]); Meshes.Add(wrong); wrong.name = originals[0].name; wrong.bounds = new Bounds(Vector3.zero, Vector3.one * 99);
            Check(!ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(wrong), "ornament name with changed native metadata is not admitted");
            Check(!ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(coreMesh), "retained source-proven wall core cannot inherit ornament policy");
            Mesh wrongTopology = UnityEngine.Object.Instantiate(originals[0]); Meshes.Add(wrongTopology); wrongTopology.name = originals[0].name;
            for (int sub = 0; sub < wrongTopology.subMeshCount; sub++) wrongTopology.SetIndices(wrongTopology.GetIndices(sub), UnityEngine.MeshTopology.Lines, sub, false);
            Check(!ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(wrongTopology), "same-metadata native non-triangle topology remains unadmitted");
            Mesh badSource = UnityEngine.Object.Instantiate(originals[0]); Meshes.Add(badSource); badSource.name = "GloomhavenVR.Fixture.Ornament.BadSource";
            Check(!ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(badSource), "ornament source SHA mismatch retains native visual");

            var group = Node(generated.transform, "Native multipart trim");
            var first = Leaf(group.transform, "Original trim A", originals[0]);
            var second = Leaf(group.transform, "Original trim B", originals[1]);
            Check(ScenarioArchitecturalDetailBudget.TryAdmit(first, tile, out Transform? firstUnit) && firstUnit == group.transform,
                "non-LOD multipart ornament promotes to its complete safe original subtree");
            Check(ScenarioArchitecturalDetailBudget.TryAdmit(second, tile, out Transform? secondUnit) && secondUnit == firstUnit,
                "non-LOD sibling ornaments share the same density unit");
            Check(Kind(first, tile) == "Architecture", "catalog positive optional detail selects architecture budget");
            var mixed = Node(generated.transform, "Mixed original room unit");
            var mixedTrim = Leaf(mixed.transform, "Trim", originals[0]); var mixedCore = Leaf(mixed.transform, "Required body", coreMesh);
            Check(ScenarioArchitecturalDetailBudget.TryAdmit(mixedTrim, tile, out Transform? mixedUnit) && mixedUnit == mixedTrim.transform,
                "required native body is never promoted into an ornament unit");
            var lonelyWall = Node(tile.transform, "Sole boundary wall"); lonelyWall.AddComponent<ProceduralWall>();
            var lonelyGenerated = Node(lonelyWall.transform, "Generated Content");
            var lonelyTrim = Leaf(lonelyGenerated.transform, "Original sole WallTop", originals[0]);
            Check(!Admit(lonelyTrim, tile), "sole WallTop boundary without an actual retained body remains native");
            core.enabled = false; mixedCore.enabled = false;
            Check(!Admit(first, tile), "hidden native required core refuses detachable trim admission"); core.enabled = true; mixedCore.enabled = true;
            var floorDetail = Leaf(generated.transform, "Original floor detail", originals.First(m => m.name == "CR_OS_Floor_Detail_02_V01"));
            floor.enabled = false; Check(!Admit(floorDetail, tile), "floor ornament requires an actual retained floor"); floor.enabled = true;

            var ownBox = Node(generated.transform, "Native composite box"); var box = ownBox.AddComponent<BoxCollider>();
            var boxedTrim = Leaf(ownBox.transform, "Original box trim", originals[0]);
            Check(!Admit(boxedTrim, tile), "distant retained wall cannot represent a lone ornament collider");
            var boxedCore = Leaf(ownBox.transform, "Original shared box body", coreMesh);
            Check(Admit(boxedTrim, tile), "actual retained body represents its native composite collider");
            box.isTrigger = true; Check(!Admit(boxedTrim, tile), "native trigger protects even a positive ornamental mesh"); box.isTrigger = false;
            var leafBox = first.gameObject.AddComponent<BoxCollider>(); Check(!Admit(first, tile), "native leaf collider protects its original geometry"); UnityEngine.Object.DestroyImmediate(leafBox);
            var light = group.AddComponent<Light>(); Check(!Admit(first, tile), "native light ancestor protects ornamental visual geometry"); UnityEngine.Object.DestroyImmediate(light);
            var childLightNode = Node(group.transform, "Native light child"); childLightNode.AddComponent<Light>();
            Check(ScenarioArchitecturalDetailBudget.TryAdmit(first, tile, out Transform? lightUnit) && lightUnit == first.transform,
                "native light child prevents complete-unit promotion without hiding that light"); UnityEngine.Object.DestroyImmediate(childLightNode);
            var callback = group.AddComponent<UnknownNativeCallback>(); Check(!Admit(first, tile), "unknown native callback protects positive ornament"); UnityEngine.Object.DestroyImmediate(callback);
            var animation = group.AddComponent<Animator>(); Check(!Admit(first, tile), "native animation is never removed by architecture density"); UnityEngine.Object.DestroyImmediate(animation);
            var gameplay = group.AddComponent<UnityGameEditorObject>(); gameplay.PropObject = new object(); Check(!Admit(first, tile), "native PropObject protects positive ornament"); UnityEngine.Object.DestroyImmediate(gameplay);
            PropGrab.Registered = group.transform; Check(!Admit(first, tile), "registered grabbable positive ornament remains native"); PropGrab.Registered = null;
            HeldProps.Held = group.transform; Check(!Admit(first, tile), "local held positive ornament remains native"); HeldProps.Held = null;
            NetHeldProps.Visual = group; NetHeldProps.Any = true; Check(!Admit(first, tile), "remote held positive ornament remains native"); NetHeldProps.Visual = null; NetHeldProps.Any = false;

            // Actual prefab graphs remain separate from controlled mutation contexts.
            NativeCensus(tile.transform, tile);
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", Color.magenta); block.SetFloat("_Cutoff", .43f); core.SetPropertyBlock(block);
            Material nativeMaterial = core.sharedMaterial; Mesh nativeMesh = core.GetComponent<MeshFilter>().sharedMesh;
            var units = new List<(MeshRenderer A, MeshRenderer B)>();
            for (int i = 0; i < 80; i++)
            { var parent = Node(generated.transform, "Complete trim " + i); units.Add((Leaf(parent.transform, "A", originals[0]), Leaf(parent.transform, "B", originals[1]))); }
            var foreign = Leaf(generated.transform, "Foreign masked ornament", originals[0]); foreign.forceRenderingOff = true;
            driver = Driver(host); _density = 0; Tick(driver); Complete(tile);
            Check(first.forceRenderingOff && second.forceRenderingOff && boxedTrim.forceRenderingOff && floorDetail.forceRenderingOff,
                "architecture zero masks positive represented trim with legacy budgets at 100");
            Check(!core.forceRenderingOff && !floor.forceRenderingOff && !boxedCore.forceRenderingOff && !lonelyTrim.forceRenderingOff,
                "architecture zero preserves actual room cores and sole boundaries");
            Check(box.enabled, "architecture zero preserves native composite collider exactly");
            Check(core.sharedMaterial == nativeMaterial && core.GetComponent<MeshFilter>().sharedMesh == nativeMesh, "retained required core keeps original material and mesh");
            var read = new MaterialPropertyBlock(); core.GetPropertyBlock(read);
            Check(read.GetColor("_Color") == Color.magenta && read.GetFloat("_Cutoff") == .43f, "retained original core keeps native color and continuous fade MPB");
            _density = 35; Tick(driver, 100);
            Check(units.All(pair => pair.A.forceRenderingOff == pair.B.forceRenderingOff), "35 percent keeps complete non-LOD multipart ornaments together");
            int hidden = units.Count(pair => pair.A.forceRenderingOff);
            Check(hidden > 0 && hidden < units.Count, "35 percent selects a deterministic partial population of whole architecture units");
            var selected = units.Select(pair => pair.A.forceRenderingOff).ToArray(); Complete(tile); Tick(driver, 20);
            Check(selected.SequenceEqual(units.Select(pair => pair.A.forceRenderingOff)), "architecture rediscovery keeps deterministic complete-unit membership");
            var visibleBeforeCoreLoss = units.Where(pair => !pair.A.forceRenderingOff).ToArray();
            core.enabled = false; _density = 0; Tick(driver);
            Check(visibleBeforeCoreLoss.All(pair => !pair.A.forceRenderingOff && !pair.B.forceRenderingOff), "retuning retained records after core loss cannot create a new mask");
            Tick(driver, 100);
            core.enabled = true; Complete(tile);
            _density = 100; Tick(driver, 100);
            Check(!first.forceRenderingOff && !second.forceRenderingOff && units.All(pair => !pair.A.forceRenderingOff && !pair.B.forceRenderingOff) && foreign.forceRenderingOff,
                "architecture master off or 100 restores only owned masks and preserves foreign forceRenderingOff");
            _density = -20; Tick(driver); Complete(tile);
            Check(first.forceRenderingOff, "negative architecture density clamps to zero");
            _density = 140; Tick(driver, 100);
            Check(!first.forceRenderingOff && foreign.forceRenderingOff, "architecture density over 100 clamps to full native detail");
            _density = 0; Tick(driver); Complete(tile);
            core.enabled = false; Tick(driver, 100);
            Check(!first.forceRenderingOff && !second.forceRenderingOff, "native core hidden after admission rescues dependent trim through bounded witness guard");
            core.enabled = true; Complete(tile); Check(first.forceRenderingOff, "native retained core recovery admits optional trim again");
            Mesh oldCore = core.GetComponent<MeshFilter>().sharedMesh; core.GetComponent<MeshFilter>().sharedMesh = originals[0]; Tick(driver, 100);
            Check(!first.forceRenderingOff, "native required core replaced by an ornament rescues dependent trim"); core.GetComponent<MeshFilter>().sharedMesh = oldCore; Complete(tile);
            // Local and remote held rescue use the actual scenery identity index this frame.
            HeldProps.Held = group.transform; Tick(driver);
            Check(!first.forceRenderingOff && !second.forceRenderingOff, "local held architecture unit restores in its current frame"); HeldProps.Held = null; Complete(tile);
            NetHeldProps.Any = true; NetHeldProps.Visual = group; Tick(driver);
            Check(!first.forceRenderingOff && !second.forceRenderingOff, "remote held architecture unit restores in its current frame"); NetHeldProps.Any = false; NetHeldProps.Visual = null; Complete(tile);
            Transform previous = group.transform.parent; group.transform.SetParent(host.transform, true); Tick(driver, 100);
            Check(!first.forceRenderingOff && !second.forceRenderingOff, "reparented architecture leaves original room ownership and restores bounded masks"); group.transform.SetParent(previous, true); Complete(tile);
            int notes = VRLog.Messages.Count(s => s.Contains("Architectural detail density")); _throw = true; Tick(driver, 100);
            Check(!first.forceRenderingOff && foreign.forceRenderingOff, "architecture settings failure fails open to original visuals");
            Check(VRLog.Messages.Count(s => s.Contains("Architectural detail density")) == notes + 1, "architecture settings failure emits one bounded informative note"); _throw = false;
            _density = 0; Tick(driver); Complete(tile); VRSession.IsRunning = false; Tick(driver);
            Check(!first.forceRenderingOff && foreign.forceRenderingOff && box.enabled, "VR teardown restores optional architecture ownership and preserves native collision");
        }
        finally
        {
            ScenarioSceneryBudget.Shutdown(); ScenarioSceneryBudget.ConfigureArchitectureDetailDensity(() => 100);
            SceneRegistry.MapTiles.Tiles.Clear(); HeldProps.Held = null; PropGrab.Registered = null; NetHeldProps.Any = false; NetHeldProps.Visual = null;
            VRSession.IsRunning = true; _throw = false;
            UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(scenario); UnityEngine.Object.DestroyImmediate(_material);
            foreach (Mesh mesh in Meshes) UnityEngine.Object.DestroyImmediate(mesh); Meshes.Clear();
            if (active.IsValid()) SceneManager.SetActiveScene(active); SceneManager.UnloadSceneAsync(scene);
        }
        return _count;
    }
}
