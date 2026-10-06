using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static partial class EnvironmentProgram
{
    private static int count;
    private static int materialReads;
    private static int bankReadRequests;
    public static LightProbes? NativeProbeFixture;
    // The source binder adds only this counter at the complete production
    // CompatibleMaterial entry; all actual Unity shader/property reads still execute.
    public static void RecordMaterialRead() => materialReads++;
    public static void RecordBankRead() => bankReadRequests++;
    private static readonly List<string> InvalidCallbackMessages = new();
    private static void EngineMessage(string text, string stack, LogType type)
    {
        if(text.Contains("message may not have any parameters")) InvalidCallbackMessages.Add(text);
    }
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }
    private static object Driver => typeof(ScenarioEnvironmentBudget).GetField("_driver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Tick(string method = "Update", params object[] args)
    { Driver.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Driver, args); }
    private static int Members(string field)
    {
        object value = Driver.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Driver)!;
        return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
    }
    private static void Configure(bool batch, bool simple, int effects)
    { PerfConfig.StaticScenarioBatchesOn = batch; PerfConfig.SimpleEnvironmentShadingOn = simple; PerfConfig.EnvironmentEffectsDensityPercent = effects; }

    private static void PresentationPreparationVisibility()
    {
        using var room = new Room();
        Check(!ScenarioEnvironmentBudget.IsPreparingPresentation,
            "all environment budgets off never request a preparation spinner");
        room.Floor(); room.Floor();
        Configure(true, true, 100);
        ScenarioEnvironmentBudget.Placed(room.Generated);
        Check(ScenarioEnvironmentBudget.IsPreparingPresentation,
            "new native room discovery requests preparation before its first work tick");
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(!ScenarioEnvironmentBudget.IsPreparingPresentation,
            "finished discovery and substitute construction release preparation immediately");
        Tick("HandlePreCull", room.Camera);
        Check(!ScenarioEnvironmentBudget.IsPreparingPresentation,
            "ordinary camera leases never extend asset preparation");
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(room.Generated.GetComponentInChildren<MeshRenderer>());
        Check(!ScenarioEnvironmentBudget.IsPreparingPresentation,
            "normal wall invalidation without queued work never creates a loading spinner");
        Tick("HandlePostRender", room.Camera);
        Configure(false, false, 100); Tick();
        Check(!ScenarioEnvironmentBudget.IsPreparingPresentation,
            "disabled budget cleanup cannot leave preparation latched");
    }

    private sealed class Room : IDisposable
    {
        internal readonly GameObject Root, Host, Generated;
        internal readonly ProceduralMapTile Tile;
        internal readonly Material Original;
        internal readonly Camera Camera;
        internal int LastRenderedChunks;
        internal Action? ObserveRender;
        private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        private readonly List<GameObject> foreign = new List<GameObject>();
        internal Room()
        {
            VRSession.IsRunning = true;
            VRLog.Faults.Clear();
            PerfMonitor.Counts.Clear();
            VRLog.DebugLines.Clear(); VRLog.DebugEnabled = true; PerfMonitor.ThrowDrawTrace = false;
            Configure(false, false, 100);
            PerfConfig.SharedEnvironmentMaterialReadsOn = true; PerfConfig.EnvironmentMeshBankOn = false; PerfConfig.EnvironmentDrawInstancingOn = false;
            ScenarioEnvironmentBudget.ConfigureStructuralBatching(() => false);
            Root = new GameObject("RuntimeFixture.Scenario"); Root.AddComponent<ProceduralScenario>();
            var tile = Child("RuntimeFixture.Tile", Root.transform); Tile = tile.AddComponent<ProceduralMapTile>();
            Generated = Child("Generated Content", tile.transform);
            Host = new GameObject("GloomhavenVR.RuntimeFixture.Driver");
            ScenarioEnvironmentBudget.Install(Host);
            Check(InvalidCallbackMessages.Count == 0,
                "actual Unity AddComponent produces no invalid engine callback messages");
            Original = Material();
            var camera = Child("RuntimeFixture.Camera", Root.transform);
            Camera = camera.AddComponent<Camera>();
            Camera.orthographic = true; Camera.orthographicSize = 3f;
            Camera.transform.position = new Vector3(1.5f, 8f, 0f);
            Camera.transform.rotation = Quaternion.Euler(90,0,0);
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = Color.black;
            Camera.enabled = false;
        }
        internal static GameObject Child(string name, Transform parent)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child; }
        internal Material Material()
        {
            Shader shader = Shader.Find("Amp_Basic_N_MRAO");
            Check(shader != null && shader.isSupported, "native surrogate shader imports on the actual graphics device");
            var material = new Material(shader) { name = "RuntimeFixture.NativeMaterial" };
            material.SetColor("_Tint", new Color(.8f, .4f, .2f, 0f));
            assets.Add(material); return material;
        }
        internal Mesh Mesh(bool readable = true)
        {
            var mesh = new Mesh { name = "RuntimeFixture.Mesh" };
            mesh.vertices = new[] { new Vector3(-.6f,0,-.6f), new Vector3(.6f,0,-.6f), new Vector3(.6f,0,.6f), new Vector3(-.6f,0,.6f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0,2,1,0,3,2 }; mesh.RecalculateBounds();
            if (!readable) mesh.UploadMeshData(true);
            assets.Add(mesh); return mesh;
        }
        internal MeshRenderer Surface(string name, Transform? parent = null, float x = 1f, Material? material = null, bool readable = true)
        {
            var obj = Child(name, parent ?? Generated.transform); obj.transform.localPosition = new Vector3(x, 0, 0);
            obj.AddComponent<MeshFilter>().sharedMesh = Mesh(readable);
            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material ?? Original;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            obj.AddComponent<BoxCollider>(); return renderer;
        }
        internal MeshRenderer Floor(float x = 1f) => Surface("CV_Floor_Base_Fixture", x: x);
        internal ParticleSystem Particle(string name, Transform? parent = null, bool loop = true)
        {
            var obj = Child(name, parent ?? Generated.transform);
            var system = obj.AddComponent<ParticleSystem>(); system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.loop = loop; main.duration = 5f; main.startLifetime = 2f;
            system.Play(false); return system;
        }
        internal MeshRenderer[] Chunks()
        {
            var result = new List<MeshRenderer>();
            var batches = (System.Collections.IEnumerable)Driver.GetType().GetField("_batches",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Driver)!;
            foreach (object batch in batches)
            {
                var renderer = (MeshRenderer)batch.GetType().GetField("Renderer",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(batch)!;
                if (renderer != null) result.Add(renderer);
            }
            return result.ToArray();
        }
        internal Color32[] Render()
        {
            RenderTexture? previous = RenderTexture.active;
            var target = new RenderTexture(48, 48, 24); target.Create();
            var image = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            Exception? callbackFailure = null;
            Camera.CameraCallback observe = camera =>
            {
                if (camera != Camera) return;
                try
                {
                    LastRenderedChunks = 0;
                    foreach (var chunk in Chunks()) if (chunk.enabled) LastRenderedChunks++;
                    ObserveRender?.Invoke();
                }
                catch (Exception error) { callbackFailure ??= error; }
            };
            Camera.onPreCull += observe;
            try
            {
                Camera.targetTexture = target; Camera.Render();
                if (callbackFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,48,48),0,0); image.Apply(); return image.GetPixels32();
            }
            finally
            {
                Camera.targetTexture = null; RenderTexture.active = previous;
                Camera.onPreCull -= observe;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }
        internal GameObject ForeignSceneTile()
        {
            Scene scene = SceneManager.CreateScene("RuntimeFixture.Foreign." + typeof(EnvironmentProgram).Assembly.GetName().Name);
            var tile = new GameObject("RuntimeFixture.ForeignTile"); tile.AddComponent<ProceduralMapTile>();
            SceneManager.MoveGameObjectToScene(tile, scene); foreign.Add(tile);
            return Child("Generated Content", tile.transform);
        }
        public void Dispose()
        {
            ScenarioEnvironmentBudget.Shutdown();
            UnityEngine.Object.DestroyImmediate(Host); UnityEngine.Object.DestroyImmediate(Root);
            foreach (var obj in foreign) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            foreach (var asset in assets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            Configure(false, false, 100); SceneController.Instance.IsLoading = SceneController.Instance.ScenarioIsLoading = false;
        }
    }

    private static void ScopeAndMaterials()
    {
        using var room = new Room();
        var floor = room.Floor(); var ordinary = room.Surface("Dungeon_Wall_Trim", x: 2f);
        var excluded = new List<MeshRenderer>();
        var actor = Room.Child("Actor", room.Generated.transform); actor.AddComponent<ActorBehaviour>();
        excluded.Add(room.Surface("CV_Floor_Base_Actor", actor.transform));
        var prop = Room.Child("Prop", room.Generated.transform); prop.AddComponent<ProceduralProp>();
        excluded.Add(room.Surface("CV_Floor_Base_Prop", prop.transform));
        var ui = Room.Child("UI", room.Generated.transform); ui.AddComponent<Canvas>();
        var uiRenderer = room.Surface("CV_Floor_Base_UI", ui.transform); excluded.Add(uiRenderer);
        var animated = Room.Child("Animated", room.Generated.transform); animated.AddComponent<Animator>();
        excluded.Add(room.Surface("CV_Floor_Base_Animated", animated.transform));
        var held = Room.Child("Held", room.Generated.transform); held.AddComponent<Rigidbody>();
        excluded.Add(room.Surface("CV_Floor_Base_Held", held.transform));
        var interactable = Room.Child("Interactable", room.Generated.transform); interactable.AddComponent<CInteractable>();
        excluded.Add(room.Surface("CV_Floor_Base_Interactable", interactable.transform));
        var doorway = Room.Child("Doorway", room.Generated.transform); doorway.AddComponent<ProceduralDoorway>();
        excluded.Add(room.Surface("CV_Floor_Base_Doorway", doorway.transform));
        var editorDoor = Room.Child("EditorDoor", room.Generated.transform); editorDoor.AddComponent<UnityGameEditorDoorProp>();
        excluded.Add(room.Surface("CV_Floor_Base_EditorDoor", editorDoor.transform));
        var skinned = Room.Child("Skinned", room.Generated.transform); skinned.AddComponent<SkinnedMeshRenderer>();
        excluded.Add(room.Surface("CV_Floor_Base_Skinned", skinned.transform));
        var preview = Room.Child("Preview", room.Generated.transform);
        excluded.Add(room.Surface("CV_Floor_Base_Preview", preview.transform));
        var owned = Room.Child("GloomhavenVR.NativeClone", room.Generated.transform);
        excluded.Add(room.Surface("CV_Floor_Base_ModClone", owned.transform));
        excluded.Add(room.Surface("CV_Floor_Base_NoGenerated", room.Tile.transform));
        excluded.Add(room.Surface("CV_Floor_Base_NoScenario", room.ForeignSceneTile().transform));
        var floorNamedParent = Room.Child("CV_Floor_Base_Ancestor",room.Generated.transform);
        excluded.Add(room.Surface("MountedDecoration",floorNamedParent.transform));
        var raised = room.Surface("CV_Floor_Base_Raised"); raised.transform.localPosition += Vector3.up;
        excluded.Add(raised);
        var pillar = room.Surface("CV_Floor_Base_PillarFoot");
        var pillarMesh = pillar.GetComponent<MeshFilter>().sharedMesh;
        pillarMesh.vertices = new[] { new Vector3(-.6f,-2f,-.6f),new Vector3(.6f,-2f,-.6f),new Vector3(.6f,0f,.6f),new Vector3(-.6f,0f,.6f) };
        pillarMesh.RecalculateBounds(); excluded.Add(pillar);

        var animatedMaterial = room.Material(); animatedMaterial.SetFloat("_AddVertexAnim",1);
        excluded.Add(room.Surface("Native_Grass", material: animatedMaterial));
        var waterMaterial = room.Material(); waterMaterial.shader = Shader.Find("Unlit/Color");
        excluded.Add(room.Surface("Native_Water", material: waterMaterial));
        var emissiveMaterial = room.Material(); emissiveMaterial.SetFloat("_UseEmissiveMap",1);
        excluded.Add(room.Surface("Native_Emissive", material: emissiveMaterial));
        var fadedMaterial = room.Material(); fadedMaterial.SetFloat("_WallFade_On",1);
        var nativeWall = room.Surface("Dungeon_Visible_Wall", material: fadedMaterial); excluded.Add(nativeWall);
        var keywordMaterial = room.Material(); keywordMaterial.EnableKeyword("_WALLFADE_ON_ON");
        Check(keywordMaterial.IsKeywordEnabled("_WALLFADE_ON_ON"), "surrogate native wall keyword gate is functional before admission");
        excluded.Add(room.Surface("Dungeon_Keyword_Wall", material: keywordMaterial));
        var queuedMaterial = room.Material(); queuedMaterial.renderQueue = 3000;
        excluded.Add(room.Surface("Transparent_Trim", material: queuedMaterial));

        var originals = new List<Material>(); foreach (var item in excluded) originals.Add(item.sharedMaterial);
        var texture = new Texture2D(2,2); texture.SetPixels(new[] { Color.red,Color.red,Color.red,Color.red }); texture.Apply();
        room.Original.SetTexture("_MainTex",texture); room.Original.SetFloat("_UVTiling",1.3f); room.Original.SetFloat("_UV_Offset",.17f);
        Configure(true,true,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(VRLog.Faults.Count == 0, "scope and material preparation completes without swallowed faults");
        Check(floor.sharedMaterial.shader.name == "GloomhavenVR/ScenarioSimpleEnvironment", "proven native floor receives the real imported simple shader");
        Check(ordinary.sharedMaterial == room.Original, "non-floor opaque trim preserves native material despite compatible shader family");
        Check(uiRenderer.sharedMaterial == room.Original, "foreign UI material stays untouched");
        for (int i=0;i<excluded.Count;i++)
            Check(excluded[i].sharedMaterial == originals[i] && !excluded[i].forceRenderingOff,
                "foreign actor/UI/held/water/foliage/dissolve/native scope exclusions retain original rendering: " + excluded[i].name);
        Check(room.Chunks().Length == 0 && !ordinary.forceRenderingOff, "only proven floor surfaces enter render substitutes");
        var variant = floor.sharedMaterial;
        Check(variant.GetTexture("_MainTex") == texture && Mathf.Abs(variant.GetFloat("_UVTiling")-1.3f)<.001f
            && Mathf.Abs(variant.GetFloat("_UV_Offset")-.17f)<.001f && variant.GetColor("_Tint") == room.Original.GetColor("_Tint"),
            "simple variant preserves original texture, tint and authored UV properties");
        var nativeClone = UnityEngine.Object.Instantiate(floor.gameObject,room.Generated.transform,false);
        Check(nativeClone.GetComponent<MeshRenderer>().sharedMaterials.Length == 1
            && nativeClone.GetComponent<MeshRenderer>().sharedMaterial == variant
            && !nativeClone.GetComponent<MeshRenderer>().forceRenderingOff
            && !nativeClone.GetComponent<MeshRenderer>().isPartOfStaticBatch,
            "native procedural clone retains a real material slot, original mesh and unmasked renderer between cameras");
        var surfaces = (System.Collections.IDictionary)Driver.GetType().GetField("_surfaces",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Driver)!;
        object ownedSurface = surfaces[floor.GetInstanceID()]!;
        ownedSurface.GetType().GetMethod("RestoreMaterial",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ownedSurface,null);
        Check(floor.sharedMaterial == room.Original, "owned native material restore takes effect immediately before any clone sweep");
        room.Original.SetFloat("_Diffuse_Boost",1.8f); ScenarioEnvironmentBudget.MaterialReady(floor);
        Check(Mathf.Abs(floor.sharedMaterial.GetFloat("_Diffuse_Boost")-1.8f)<.001f,
            "native in-place material completion refreshes the applied variant");
        var foreignReplacement = room.Material(); ordinary.sharedMaterial = foreignReplacement;
        Configure(false,false,100); Tick();
        Check(floor.sharedMaterial == room.Original, "turning simplification off restores exact original material references");
        Check(nativeClone.GetComponent<MeshRenderer>().sharedMaterial == room.Original,
            "unannounced native clone restores its original material before the simple variant is destroyed");
        Check(ordinary.sharedMaterial == foreignReplacement, "restore cannot overwrite a foreign material replacement");
        Check(room.Original.shader.name == "Amp_Basic_N_MRAO" && room.Original.GetTexture("_MainTex") == texture,
            "original material and texture are never rewritten in place");
        UnityEngine.Object.DestroyImmediate(texture);
    }

    private static void AmbientScopes()
    {
        using var room = new Room();
        var families = new List<ParticleSystem>();
        foreach (string name in new[] { "p_Moths_Cave(Clone)", "Candle_Fire_FX_Small(Instance)", "p_fire_torch", "p_fire_torch_blue", "p_fireflies", "p_Fireflies" }) families.Add(room.Particle(name));
        var combat = room.Particle("P_attack_Fire");
        var unlisted = room.Particle("Looping_Cloud");
        var oneshot = room.Particle("p_fire_torch", loop:false);
        var actor = Room.Child("Actor",room.Generated.transform); actor.AddComponent<ActorBehaviour>();
        var actorAmbient = room.Particle("p_fire_torch",actor.transform);
        var family = Room.Child("Candle_Fire_FX_Holder",room.Generated.transform);
        var condition = room.Particle("P_condition_burning",family.transform);
        var foreignMasked = room.Particle("p_Moths_ForeignMasked"); foreignMasked.GetComponent<ParticleSystemRenderer>().forceRenderingOff = true;
        var foreignPaused = room.Particle("p_Moths_ForeignPaused"); foreignPaused.Pause(false);
        Configure(false,false,0); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        foreach (var effect in families)
            Check(!effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff && effect.isPaused, "zero ambient budget pauses exact native families without leaking render masks between cameras");
        bool allAmbientMasked = false;
        room.ObserveRender = () =>
        {
            allAmbientMasked = true;
            foreach (var effect in families) allAmbientMasked &= effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff;
        };
        room.Render(); room.ObserveRender = null;
        Check(allAmbientMasked, "real camera callback masks all admitted ambient families during rendering");
        foreach (var effect in families) Check(!effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff,
            "real post-render callback restores ambient masks before native cloning");
        Check(!combat.GetComponent<ParticleSystemRenderer>().forceRenderingOff && combat.isPlaying, "combat effects never become ambience merely because they loop");
        Check(!unlisted.GetComponent<ParticleSystemRenderer>().forceRenderingOff && unlisted.isPlaying, "unlisted native loop remains untouched");
        Check(!oneshot.GetComponent<ParticleSystemRenderer>().forceRenderingOff && oneshot.isPlaying, "one-shot native effect remains untouched");
        Check(!actorAmbient.GetComponent<ParticleSystemRenderer>().forceRenderingOff && actorAmbient.isPlaying, "actor-owned ambient-looking effect remains untouched");
        Check(!condition.GetComponent<ParticleSystemRenderer>().forceRenderingOff && condition.isPlaying, "condition child cannot inherit an ambient family above it");
        Configure(false,false,100); Tick();
        foreach (var effect in families)
            Check(!effect.GetComponent<ParticleSystemRenderer>().forceRenderingOff && effect.isPlaying, "ambient budget restore resumes only its owned mask and pause");
        Check(foreignMasked.GetComponent<ParticleSystemRenderer>().forceRenderingOff, "ambient restore retains a preexisting foreign rendering mask");
        Check(foreignPaused.isPaused, "ambient restore retains a preexisting foreign pause");
        Check(VRLog.Faults.Count == 0, "native ambient preparation completes without faults");
    }

    private static void ShaderRendering()
    {
        using var room = new Room();
        var floor = room.Floor();
        Configure(false,true,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        int visible = 0;
        foreach (Color32 pixel in room.Render()) if (pixel.r > pixel.g && pixel.g >= pixel.b && pixel.r > 5) visible++;
        Check(visible > 20, "real production simple shader emits finite tinted floor pixels on the graphics device");
        Check(floor.sharedMaterial.FindPass("SHADOWCASTER") >= 0 || floor.sharedMaterial.FindPass("ShadowCaster") >= 0,
            "production simplified surface retains a real imported shadow caster pass");
        var transparent = new Texture2D(2,2); transparent.SetPixels(new[] { Color.clear,Color.clear,Color.clear,Color.clear }); transparent.Apply();
        room.Original.SetTexture("_MainTex",transparent); room.Original.SetFloat("_Difuse_Alpha_On",1); room.Original.SetFloat("_Cutoff",.5f);
        ScenarioEnvironmentBudget.MaterialReady(floor);
        bool black = true; foreach (Color32 pixel in room.Render()) black &= pixel.r < 2 && pixel.g < 2 && pixel.b < 2;
        Check(black, "production simplified shader respects the original alpha-cutout texture on actual pixels");
        UnityEngine.Object.DestroyImmediate(transparent);
    }

    private static void BatchesAndFallback()
    {
        using var room = new Room();
        var first = room.Floor(1f); var second = room.Floor(2f);
        var collider = first.GetComponent<BoxCollider>();
        Color32[] originalPixels = room.Render();
        Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 1 && !first.forceRenderingOff && !second.forceRenderingOff, "compatible static floor geometry creates one render substitute without cloning-visible masks");
        var chunk = room.Chunks()[0];
        Check(chunk.GetComponent<MeshFilter>().sharedMesh.vertexCount == 8 && first.GetComponent<MeshFilter>().sharedMesh.vertexCount == 4,
            "combined geometry contains both originals without rewriting native meshes");
        bool drawLease = false;
        room.ObserveRender = () => drawLease = first.forceRenderingOff && second.forceRenderingOff && chunk.enabled;
        Color32[] combinedPixels = room.Render(); room.ObserveRender = null; int drawn = 0; bool same = true;
        for (int i=0;i<originalPixels.Length;i++) { if (originalPixels[i].r > 10) drawn++; same &= originalPixels[i].Equals(combinedPixels[i]); }
        Check(drawn > 20 && same, "actual graphics rendering preserves the native opaque floor pixels after combining");
        Check(drawLease && !first.forceRenderingOff && !second.forceRenderingOff && !chunk.enabled,
            "real camera callback pair owns masks only during rendering");
        Check(PerfMonitor.Counts["Environment.ChunkSources"]==2&&PerfMonitor.Counts["Environment.ChunkGroups"]==1,
            "completed exact chunk camera reports actually leased sources and groups");
        Check(collider.enabled && first.gameObject.activeInHierarchy, "native collider and hierarchy survive render substitution");

        var clone = UnityEngine.Object.Instantiate(first.gameObject,room.Generated.transform,false);
        var cloneRenderer = clone.GetComponent<MeshRenderer>();
        Check(!cloneRenderer.forceRenderingOff && cloneRenderer.sharedMaterials.Length == 1
            && cloneRenderer.sharedMaterial == room.Original && !cloneRenderer.isPartOfStaticBatch
            && clone.GetComponent<MeshFilter>().sharedMesh == first.GetComponent<MeshFilter>().sharedMesh,
            "actual native Object.Instantiate between cameras retains nonempty original slots, mesh and render flags");
        clone.SetActive(false);

        var nested = Room.Child("RuntimeFixture.NestedCamera",room.Root.transform).AddComponent<Camera>();
        nested.CopyFrom(room.Camera); nested.enabled = false;
        var nestedTarget = new RenderTexture(16,16,24); nestedTarget.Create(); nested.targetTexture = nestedTarget;
        bool nestedRan = false, innerMask = false, outerRetained = false;
        Camera.CameraCallback nestedProbe = camera =>
        {
            if (camera == nested) { innerMask = first.forceRenderingOff && chunk.enabled; return; }
            if (camera != room.Camera || nestedRan) return;
            nestedRan = true; nested.Render(); outerRetained = first.forceRenderingOff && chunk.enabled;
        };
        Camera.onPreCull += nestedProbe;
        try { room.Render(); }
        finally { Camera.onPreCull -= nestedProbe; nested.targetTexture = null; nestedTarget.Release(); UnityEngine.Object.DestroyImmediate(nestedTarget); UnityEngine.Object.DestroyImmediate(nested.gameObject); }
        Check(nestedRan && innerMask && outerRetained && !first.forceRenderingOff && !chunk.enabled,
            "actual nested camera renders keep the outer lease and restore after the outer post callback");
        Tick("HandlePreCull",room.Camera);
        Check(first.forceRenderingOff, "interrupted pre-cull establishes the production draw lease");
        object recovery = typeof(ScenarioEnvironmentBudget).GetField("_recovery",BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(recovery.GetType(),typeof(DefaultExecutionOrder))!;
        recovery.GetType().GetMethod("Update",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recovery,null);
        Check(order.order < 0 && !first.forceRenderingOff && !chunk.enabled,
            "early production recovery restores a missing post callback before native content creation");
        Tick("HandlePreCull",room.Camera); ((Behaviour)Driver).enabled = false;
        Check(!first.forceRenderingOff && !chunk.enabled, "actual MonoBehaviour disable releases an interrupted camera mask");
        ((Behaviour)Driver).enabled = true;

        // Static Camera callbacks remain subscribed when the mod host is inactive.
        // Proxy chunks live outside that host, so a missing admission guard would
        // still submit their geometry. Observe the real render and original pixels.
        room.Host.SetActive(false);
        bool inactiveOriginals = false;
        room.ObserveRender = () => inactiveOriginals = !first.forceRenderingOff
            && !second.forceRenderingOff && !chunk.enabled;
        Color32[] inactivePixels = room.Render(); room.ObserveRender = null;
        Check(inactiveOriginals && room.LastRenderedChunks == 0
            && originalPixels.SequenceEqual(inactivePixels),
            "inactive environment host uses original camera pixels without private chunk leases");
        room.Host.SetActive(true); room.Render();
        Check(room.LastRenderedChunks == 1 && !first.forceRenderingOff && !chunk.enabled,
            "reactivated environment host resumes its completed private draw lease");

        first.enabled = false; room.Render();
        Check(room.LastRenderedChunks == 0 && !second.forceRenderingOff, "real camera pre-cull rejects changed native visibility in the same render");
        first.enabled = true; room.Render();
        Check(room.LastRenderedChunks == 1, "compatible original visibility can resume the existing chunk");
        first.transform.localPosition += Vector3.up; room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "real camera pre-cull rejects changed source transforms");
        first.transform.localPosition -= Vector3.up; room.Render();
        var oldMesh = first.GetComponent<MeshFilter>().sharedMesh;
        first.GetComponent<MeshFilter>().sharedMesh = room.Mesh(); room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "real camera pre-cull rejects changed source meshes");
        first.GetComponent<MeshFilter>().sharedMesh = oldMesh; room.Render();
        var foreignMaterial = room.Material(); first.sharedMaterial = foreignMaterial; room.Render();
        Check(room.LastRenderedChunks == 0 && !second.forceRenderingOff, "real camera pre-cull rejects changed source materials");
        first.sharedMaterial = room.Original; room.Render();
        first.sharedMaterials = new[] { room.Original, foreignMaterial }; room.Render();
        Check(room.LastRenderedChunks == 0 && !second.forceRenderingOff, "real camera pre-cull rejects newly multi-material originals");
        first.sharedMaterials = new[] { room.Original }; room.Render();
        var block = new MaterialPropertyBlock(); block.SetColor("_Tint",Color.green); first.SetPropertyBlock(block); room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "real camera pre-cull rejects native per-renderer effect properties");
        first.SetPropertyBlock(null); room.Render();
        second.receiveShadows = true; room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "real camera pre-cull rejects a source render-flag change");
        first.receiveShadows = true; room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "common source render-flag changes cannot retain old combined rendering flags");
        Configure(false,false,100); Tick();
        Check(!first.forceRenderingOff && !second.forceRenderingOff && room.Chunks().Length == 0, "turning batching off restores original renderer visibility");
        Check(VRLog.Faults.Count == 0, "batch preparation and real render validation finish without faults");
    }

    private static Mesh NativePillar(bool large = false)
    {
        string path = Environment.GetEnvironmentVariable("GHVR_ENVIRONMENT_NATIVE_MESH")!;
        Check(!string.IsNullOrEmpty(path), "audited native pillar geometry is supplied by the extractor");
        if (large) path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"native-pillar-large.json");
        NativeMesh data = JsonUtility.FromJson<NativeMesh>(System.IO.File.ReadAllText(path));
        Check(data.vertices.Length == (large ? 1595 : 1440) && data.name == (large ? "EN_CR_Pillar_Large" : "EN_CR_Pillar_Thin"), "actual native pillar identity and original vertex count remain intact");
        var mesh = new Mesh { name = data.name, vertices = data.vertices, normals = data.normals, uv = data.uv, triangles = data.triangles };
        mesh.RecalculateBounds(); return mesh;
    }
    [Serializable] private sealed class NativeMesh
    {
        public string name = "";
        public Vector3[] vertices = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>();
        public Vector2[] uv = Array.Empty<Vector2>();
        public int[] triangles = Array.Empty<int>();
    }
    private static void StructuralChunks()
    {
        using var room = new Room();
        var mesh = NativePillar(); var largeMesh = NativePillar(true);
        var first = room.Surface("NativePillarA", x:1f);
        var second = room.Surface("NativePillarB", x:2.2f);
        first.GetComponent<MeshFilter>().sharedMesh = mesh;
        second.GetComponent<MeshFilter>().sharedMesh = largeMesh;
        var collider = first.GetComponent<BoxCollider>();
        Matrix4x4 originalTransform = first.transform.localToWorldMatrix;
        Configure(false,true,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 0 && first.sharedMaterial == room.Original,
            "structural setting off never simplifies or batches non-floor masonry");
        ScenarioEnvironmentBudget.ConfigureStructuralBatching(() => true);
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 1 && first.sharedMaterial.shader.name == "GloomhavenVR/ScenarioSimpleEnvironment",
            "audited native masonry creates a bounded structural render substitute with explicit simpler shading");
        var chunk = room.Chunks()[0];
        Check(chunk.GetComponent<MeshFilter>().sharedMesh.vertexCount == 3035 && first.GetComponent<MeshFilter>().sharedMesh == mesh
            && first.transform.localToWorldMatrix == originalTransform && collider.enabled && !first.isPartOfStaticBatch,
            "native structural identity mesh collider transforms and non-static-batch state remain exact");
        Tick("HandlePreCull",room.Camera);
        Check(first.forceRenderingOff && second.forceRenderingOff && chunk.enabled,
            "structural draw lease reduces two different native mesh submissions to one exact geometry chunk");
        Tick("HandlePostRender",room.Camera);
        // Compare geometry using the SAME explicit quality-compromise material on both
        // paths. The original Windows shader itself is not executable on this GL host.
        Material simple = first.sharedMaterial;
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);
        first.sharedMaterial = simple; second.sharedMaterial = simple;
        Color32[] originalPixels = room.Render();
        first.sharedMaterial = room.Original; second.sharedMaterial = room.Original;
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Color32[] chunkPixels = room.Render(); bool same = true; int drawn = 0;
        for (int i=0;i<originalPixels.Length;i++) { same &= originalPixels[i].Equals(chunkPixels[i]); if (originalPixels[i].r>10) drawn++; }
        Check(drawn > 15 && same, "actual native pillar geometry has identical rendered pixels across structural source and chunk paths");
        var command = new CommandBuffer { name = "Native identity probe" };
        var commandMaterial = room.Material(); commandMaterial.SetColor("_Tint",Color.blue);
        command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        command.ClearRenderTarget(true,true,Color.green);
        command.SetViewProjectionMatrices(room.Camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(room.Camera.projectionMatrix,true));
        command.DrawRenderer(first,commandMaterial);
        room.Camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, command);
        bool commandSourcesNative = false;
        room.ObserveRender = () => commandSourcesNative = !first.forceRenderingOff && !second.forceRenderingOff && !chunk.enabled;
        Color32[] combinedCommandPixels = room.Render();
        room.ObserveRender = null;
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);
        first.sharedMaterial = simple;
        Color32[] sourceCommandPixels = room.Render(); bool sameCommand = true; int green = 0;
        for(int i=0;i<sourceCommandPixels.Length;i++) { sameCommand &= sourceCommandPixels[i].Equals(combinedCommandPixels[i]); if(sourceCommandPixels[i].b>10)green++; }
        Check(commandSourcesNative && sameCommand && green>15 && room.LastRenderedChunks == 0, "native command-buffer DrawRenderer keeps the original structural renderer identity and geometry: same="+sameCommand+", green="+green+", rendered chunks="+room.LastRenderedChunks);
        room.Camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects,command); command.Release();
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Tick("HandlePreCull",room.Camera);
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);
        Check(!first.forceRenderingOff && !second.forceRenderingOff && first.sharedMaterial == room.Original && room.Chunks().Length == 0,
            "wall effect write restores structural sources and material synchronously inside an active camera lease");
        var block = new MaterialPropertyBlock(); block.SetColor("_Tint",Color.blue); first.SetPropertyBlock(block);
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 0, "structural per-renderer wall effects stay native after re-admission");
        first.SetPropertyBlock(null);
        first.sharedMaterial = room.Original; second.sharedMaterial = room.Original;
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Tick("HandlePreCull",room.Camera); ScenarioEnvironmentBudget.BeforeNativeContentChange();
        Check(!first.forceRenderingOff && !second.forceRenderingOff, "native reveal prefix releases structural render masks before visibility changes");
        second.gameObject.SetActive(false); room.Render();
        Check(room.LastRenderedChunks == 0 && !first.forceRenderingOff, "structural cull boundary invalidates the substitute before the next camera sees it");
        second.gameObject.SetActive(true);
        var foreign = room.Material(); first.sharedMaterial = foreign;
        ScenarioEnvironmentBudget.ConfigureStructuralBatching(() => false); Tick();
        Check(first.sharedMaterial == foreign && second.sharedMaterial == room.Original && room.Chunks().Length == 0,
            "structural toggle restoration preserves exact foreign materials and native unowned renderer flags");
        ScenarioEnvironmentBudget.ConfigureStructuralBatching(() => true); Configure(false,false,100); Tick();
        Check(room.Chunks().Length == 0 && second.sharedMaterial == room.Original,
            "structural chunks require explicit simple-environment shading rather than reinterpret a native object-space shader");
        UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(largeMesh);
    }

    private static void IncrementalAndUnsafeMeshes()
    {
        using var room = new Room();
        var unreadable = room.Surface("CV_Floor_Base_Unreadable", readable:false);
        var secondUnreadable = room.Surface("CV_Floor_Base_Unreadable2", readable:false);
        var nonFloor = room.Surface("Dungeon_Wall_Static", x:2f);
        var mirrored = room.Floor(); mirrored.transform.localScale = new Vector3(-1,1,1);
        var hidden = room.Floor(); hidden.forceRenderingOff = true;
        var lodParent = Room.Child("LOD",room.Generated.transform); lodParent.AddComponent<LODGroup>();
        var lod = room.Surface("CV_Floor_Base_LOD",lodParent.transform);
        var lightmapped = room.Floor(); lightmapped.lightmapIndex = 0;
        var specialFlags = room.Floor(); specialFlags.receiveShadows = true;
        // More than 128 nodes exercise bounded walk; seven spatial groups exercise
        // the normal two-chunk publish cap and the complete loading-time drain.
        for (int i=0;i<350;i++) Room.Child("InertNode"+i,room.Generated.transform);
        var floors = new List<MeshRenderer>();
        for (int i=0;i<14;i++) floors.Add(room.Floor(1f + (i/2)*5f));
        ScenarioEnvironmentBudget.Placed(room.Tile.gameObject);
        Check(Members("_pending") == 0, "all settings off avoids procedural queue work entirely");
        Configure(true,false,100); Tick();
        Check(Members("_pending") > 0 && room.Chunks().Length == 0, "normal-frame native traversal is bounded before mesh publication");
        for (int i=0;i<20 && Members("_pending")>0;i++) Tick();
        Check(Members("_pending") == 0 && room.Chunks().Length <= 2 && Members("_parts") > 0,
            "normal update publishes at most two small chunks while retaining pending parts");
        int before = room.Chunks().Length; Tick();
        Check(room.Chunks().Length - before <= 2, "subsequent normal update retains existing chunks and adds at most two");
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(VRLog.Faults.Count == 0, "unreadable meshes never raise or hide a preparation failure");
        Check(Members("_pending") == 0 && Members("_parts") == 0 && room.Chunks().Length >= 7,
            "native loading-complete prefix prepares all outstanding floor chunks before loading ends");
        bool unsafeMasked = false;
        room.ObserveRender = () => unsafeMasked = unreadable.forceRenderingOff || secondUnreadable.forceRenderingOff || nonFloor.forceRenderingOff
            || mirrored.forceRenderingOff || lod.forceRenderingOff || lightmapped.forceRenderingOff || specialFlags.forceRenderingOff;
        room.Render(); room.ObserveRender = null;
        Check(!unsafeMasked && hidden.forceRenderingOff,
            "unreadable, non-floor, mirrored, foreign-hidden, LOD, baked and distinct-flag originals safely skip substitution");
        foreach (var item in room.Chunks()) Check(item.GetComponent<MeshFilter>().sharedMesh.vertexCount <= 96,
            "generated chunk keeps the configured 24-member bound");

        var retained = room.Chunks()[0];
        var actor = Room.Child("Actor",room.Generated.transform); actor.AddComponent<ActorBehaviour>();
        var foreign = room.Surface("CV_Floor_Base_Actor",actor.transform);
        ScenarioEnvironmentBudget.MaterialReady(foreign);
        Check(room.Chunks().Length >= 7 && retained != null, "foreign native material completion cannot invalidate unrelated floor chunks");
        int chunkCount = room.Chunks().Length;
        ScenarioEnvironmentBudget.MaterialReady(floors[floors.Count-1]);
        Check(room.Chunks().Length == chunkCount-1, "native floor material completion invalidates only its affected chunk");
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == chunkCount, "native material completion can prepare its replacement without destroying unrelated chunks");
        int deadId = floors[floors.Count-1].GetInstanceID();
        UnityEngine.Object.DestroyImmediate(floors[floors.Count-1].gameObject);
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        var sourceMap = (System.Collections.IDictionary)Driver.GetType().GetField("_batchBySource",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Driver)!;
        Check(!sourceMap.Contains(deadId), "destroyed native floor source cannot remain in the chunk ownership ledger");
        VRSession.IsRunning = false; Tick();
        Check(room.Chunks().Length == 0 && !unreadable.forceRenderingOff && hidden.forceRenderingOff,
            "VR stop restores only owned floor substitutions and retains foreign masks");
        Check(VRLog.Faults.Count == 0, "unreadable meshes never raise or hide a preparation failure");
    }

    private static void ChunkPopulation()
    {
        using var room = new Room();
        var originals = new List<MeshRenderer>();
        for (int i=0;i<55;i++) originals.Add(room.Floor());
        Configure(true,false,100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 3, "55 same-cell native floors split into bounded 24-member portions");
        int vertices = 0;
        foreach (var chunk in room.Chunks())
        {
            int size = chunk.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            Check(size <= 96, "populated combined chunk cannot exceed the 24-native-member bound");
            vertices += size;
        }
        Check(vertices == 220, "bounded portions retain every native floor vertex exactly once");
        foreach (var original in originals)
            Check(!original.forceRenderingOff && original.sharedMaterial == room.Original
                && original.GetComponent<MeshFilter>().sharedMesh.vertexCount == 4,
                "populated chunk creation keeps native clone sources intact between cameras");
        Check(VRLog.Faults.Count == 0, "populated native floor preparation completes without faults");
    }

    private static void NativeCompletionSurvivesPreparationFault()
    {
        using var room = new Room();
        var floor = room.Floor();
        Configure(false,true,100); BundleShaders.ThrowResolve = true;
        bool continued = false;
        try { ScenarioEnvironmentBudget.MaterialReady(floor); continued = true; }
        finally { BundleShaders.ThrowResolve = false; }
        Check(continued && floor.sharedMaterial == room.Original && !floor.forceRenderingOff && VRLog.Faults.Count == 1,
            "optional material preparation failure cannot gate a successful native continuation");
        ScenarioEnvironmentBudget.MaterialReady(floor); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(VRLog.Faults.Count == 1 && floor.sharedMaterial == room.Original,
            "failed optional preparation remains disabled and reports its failure once");
        int placed = 0, ready = 0, writes = 0;
        ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_ => placed++, _ => ready++,
            renderer => { writes++; renderer.forceRenderingOff = false; }, () => { }, _ => false);
        try
        {
            // The independent terrain owner may still have an active camera lease when
            // only environment preparation fails. Its synchronous native seams stay live.
            floor.forceRenderingOff = true;
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(floor);
            ScenarioEnvironmentBudget.MaterialReady(floor);
            ScenarioEnvironmentBudget.Placed(room.Generated);
            Check(placed == 1 && ready == 1 && writes == 1 && !floor.forceRenderingOff,
                "terrain native write and placement bridges survive an independent environment failure");
        }
        finally { ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_ => { }, _ => { }, _ => { }, () => { }, _ => false); }
    }

    private static void NativeWallChannelsAndRenderedClock()
    {
        using var room = new Room();
        string nativeFloorPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(
            Environment.GetEnvironmentVariable("GHVR_ENVIRONMENT_NATIVE_MESH")!)!,"native-floor-material.json");
        var native = JsonUtility.FromJson<NativeFloorMaterial>(System.IO.File.ReadAllText(nativeFloorPath));
        var live = room.Material(); live.name = native.name;
        live.SetFloat("_WallFade_On",native.wallFade); live.SetFloat("_Cutoff",native.cutoff);
        Check(live.name=="CV_Floor_Basic_M" && live.GetFloat("_WallFade_On")==1,
            "actual original CV_Floor_Basic_M authored wall-fade gate is imported verbatim: name="+live.name+", gate="+live.GetFloat("_WallFade_On"));
        var saved = new Material(Shader.Find("GloomhavenVR/ScenarioSimpleEnvironment"));
        saved.CopyPropertiesFromMaterial(live);
        Debug.Log("Native material copy probe: shader="+saved.shader.name+", HasProperty(_WallFade_On)="
            +saved.HasProperty("_WallFade_On")+", saved float="+saved.GetFloat("_WallFade_On"));
        Check(saved.GetFloat("_WallFade_On")==1,
            "actual Unity material copy retains original saved native wall-fade data even when the cheap shader lacks its fragment branch");
        UnityEngine.Object.DestroyImmediate(saved);
        var liveFloor = room.Surface("CV_Floor_Basic_Fade", material:live);
        var keyword = room.Material(); keyword.EnableKeyword("_WALLFADE_ON_ON");
        var keywordFloor = room.Surface("CV_Floor_Basic_Keyword", material:keyword);
        var local = room.Material(); local.SetFloat("_ToggleWallFadeLocal",-1);
        var localFloor = room.Surface("CV_Floor_Basic_Local", material:local);
        Configure(true,true,100); ScenarioEnvironmentBudget.ConfigureStructuralBatching(() => true);
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(liveFloor.sharedMaterial == live && keywordFloor.sharedMaterial == keyword
            && localFloor.sharedMaterial == local,
            "live native floor wall channels retain original shaders rather than copied inert shader properties");

        var lateMaterial = room.Material(); var late = room.Surface("CV_Floor_Basic_Late",material:lateMaterial);
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(late.sharedMaterial != lateMaterial, "inactive native channel can initially use explicit simpler floor shading");
        lateMaterial.SetFloat("_WallFade_On",1);
        room.Render();
        Check(late.sharedMaterial == lateMaterial && !late.forceRenderingOff,
            "late native material gate retires the simpler variant before actual camera culling");
        lateMaterial.SetFloat("_WallFade_On",0);
        ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(late.sharedMaterial != lateMaterial, "native channel closure permits a later safe re-admission");
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(late);
        Check(late.sharedMaterial == lateMaterial, "native effect writes restore owned floor shaders before the original setter");

        // Move all other surfaces out of this render. Above the native LOW foundation
        // gate, use the actual wall MPB + original rank-flattened Perlin textures.
        foreach (var renderer in room.Generated.GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
        var wall = room.Surface("CV_Wall_Fade_Runtime",x:1.5f,material:live);
        Mesh mesh = wall.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = mesh.vertices;
        for(int i=0;i<vertices.Length;i++) vertices[i].y = 1f;
        mesh.vertices=vertices; mesh.RecalculateBounds();
        using var fixture = new WallSegmentFade.Fixture(wall);
        int Visible(Color32[] pixels) { int n=0; foreach(var p in pixels) if(p.r>15)n++; return n; }
        fixture.Step(0f,false); int solid = Visible(room.Render());
        Check(solid>20,"original wall geometry renders before its dissolve starts");
        float first = fixture.Step(1f,true); int firstPixels = Visible(room.Render());
        Check(first>0f && first<.3f && firstPixels>0 && firstPixels<=solid,
            "stalled wall frame preserves rendered intermediate pixels instead of completing/binarizing the dissolve: fade="+first+", pixels="+firstPixels+"/"+solid);
        var block = new MaterialPropertyBlock(); wall.GetPropertyBlock(block);
        Debug.Log("Wall MPB probe: intGate="+block.GetInteger(Shader.PropertyToID("ToggleWallFade"))
            +", floatGate="+block.GetFloat(Shader.PropertyToID("_WallFade_On"))+", floatToggle="+block.GetFloat(Shader.PropertyToID("_ToggleWallfade"))
            +", cutoff="+block.GetFloat(Shader.PropertyToID("_Cutoff"))+", objectY="+mesh.vertices[0].y);
        Check(block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")).name == "GloomhavenVR.WallFadeNoise",
            "original wall setter uses the actual production noise texture during its visible transition");
        int decreasing=0, last=firstPixels;
        for(int i=0;i<30;i++)
        {
            float progress=fixture.Step(i==2 ? 2f : 1f/90f,true);
            int pixels=Visible(room.Render());
            Check(pixels<=last,"actual wall dissolve never resurrects pixels while progressing toward its held state");
            if(pixels<last && pixels>0)decreasing++;
            last=pixels;
            if(progress==1f)break;
        }
        for(int i=0;i<70;i++) fixture.Step(1f/90f,true);
        Check(decreasing>=5 && Visible(room.Render())==0,
            "visible native wall delivery has multiple decreasing frames and a complete held endpoint: changing="+decreasing+", pixels="+Visible(room.Render())+"/"+solid);
        wall.GetPropertyBlock(block);
        Check(block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")).name == "GloomhavenVR.WallFadeOccluded",
            "only the fully held original wall receives its binary occluded texture");
        int growing=0; last=0;
        for(int i=0;i<80;i++)
        {
            fixture.Step(i==0 ? 1f : 1f/90f,false);
            int pixels=Visible(room.Render());
            Check(pixels>=last,"actual wall return never loses pixels while its dissolve reverses");
            if(pixels>last && pixels<solid) growing++;
            last=pixels;
        }
        Check(growing>=5 && last==solid && !wall.HasPropertyBlock(),
            "return after a stalled frame has visible intermediate samples and restores the original unblocked wall");
        Configure(false,false,100); Tick();
        Check(wall.sharedMaterial==live && !wall.forceRenderingOff,
            "all environment optimizations off preserve native wall rendering and its authored live material");
    }

    private static void SharedOriginalMaterialValidation()
    {
        using var room = new Room();
        var secondMaterial = room.Material();
        var first = new List<MeshRenderer>();
        var second = new List<MeshRenderer>();
        for (int i = 0; i < 24; i++)
        {
            first.Add(room.Floor());
            second.Add(room.Surface("CV_Floor_Base_SharedSecond", material: secondMaterial));
        }
        Configure(false, true, 100);
        ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(Members("_surfaces") == 48, "shared original fixture adopts every eligible surface");
        materialReads = 0;
        room.Render();
        Check(materialReads == 2,
            "one camera validates each shared original material once while retaining every surface: reads=" + materialReads);
        foreach (var renderer in first)
            Check(renderer.sharedMaterial != room.Original, "unchanged shared original keeps its simpler presentation");

        var field = Driver.GetType().GetField("_preCullMaterialVerdicts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object storage = field.GetValue(Driver)!;
        var retire = (Action)Delegate.CreateDelegate(typeof(Action), Driver,
            Driver.GetType().GetMethod("RetireChangedNativeMaterials", BindingFlags.Instance | BindingFlags.NonPublic)!);
        for (int i = 0; i < 16; i++) retire();
        long calibration = GC.GetAllocatedBytesForCurrentThread();
        var allocationProbe = new byte[8192]; GC.KeepAlive(allocationProbe);
        bool counterSupported = GC.GetAllocatedBytesForCurrentThread() > calibration;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++) retire();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(ReferenceEquals(storage, field.GetValue(Driver)),
            "warmed material validation reuses its bounded dictionary storage");
        Check(!counterSupported || allocated == 0,
            "supported allocation counter proves warmed material validation allocates nothing: bytes=" + allocated);
        Debug.Log("Shared-material allocation receipt: calibrated counter=" + counterSupported
            + "; bytes=" + allocated + "; same dictionary=True; live originals=2, sources=48");

        // These are two successive actual Camera.Render invocations, modelling the
        // native edit seam between MultiPass eyes without claiming an XR pixel test.
        room.Original.EnableKeyword("_WALLFADE_ON_ON");
        room.Render();
        foreach (var renderer in first)
            Check(renderer.sharedMaterial == room.Original && !renderer.forceRenderingOff,
                "native keyword edit between camera invocations restores every sharing surface before culling");
        foreach (var renderer in second)
            Check(renderer.sharedMaterial != secondMaterial,
                "other shared original remains admitted after one material changes");
        Check(Members("_preCullMaterialVerdicts") == 0,
            "material verdicts retain no original references between camera invocations");

        var block = new MaterialPropertyBlock(); block.SetFloat("_WallFade_On", 1f);
        second[0].SetPropertyBlock(block);
        room.Render();
        Check(second[0].sharedMaterial == secondMaterial && !second[0].forceRenderingOff,
            "shared original verdict never bypasses an individual native property-block veto");
        Check(second[1].sharedMaterial != secondMaterial,
            "individual property-block retirement leaves its unchanged shared-material sibling admitted");
        var foreign = room.Material(); second[1].sharedMaterial = foreign;
        secondMaterial.SetFloat("_ToggleWallFadeLocal", -1f);
        room.Render();
        Check(second[1].sharedMaterial == foreign,
            "shared-material retirement preserves a later foreign material replacement");
        for (int i = 2; i < second.Count; i++)
            Check(second[i].sharedMaterial == secondMaterial && !second[i].forceRenderingOff,
                "native property edit restores remaining shared originals without a stale cross-camera verdict");
        Check(Members("_surfaces") == 0, "retired shared originals release the complete surface ledger");
    }

    // Actual original Apply/DriveNativeProp bodies and historical two textures execute.
    // This shader is an explicitly bounded GL branch surrogate, not original Windows
    // bytecode. Different valid simplex samples can have different HIGH pixel curves;
    // the PC-approved historical delivery is proved without inventing a universal
    // progressive-area claim for that branch.
    private static void NativeHighHistoricalDelivery(bool toggleNative=false,bool mounted=false)
    {
        using var room = new Room();
        Shader shader = Shader.Find("Fixture/NativeHighWall");
        Check(shader != null && shader.isSupported, "documented native HIGH fragment branch imports on real graphics");
        var material = new Material(shader);
        material.SetColor("_Tint",new Color(.8f,.4f,.2f,1));
        material.SetFloat("_NativeToggleVariant",toggleNative?1f:0f);
        material.SetFloat("_EnableOcclusionMap",0f); // suspended native flat-camera producer
        var wall = room.Surface("NativeHigh.UpperWall",x:1.1f,material:material);
        room.Camera.orthographicSize=.55f;
        room.Camera.transform.position=new Vector3(1.1f,8f,0);
        Mesh mesh = wall.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices=mesh.vertices;
        for(int i=0;i<vertices.Length;i++)vertices[i].y=1.5f;
        mesh.vertices=vertices;mesh.RecalculateBounds();
        using var fixture = new WallSegmentFade.Fixture(wall);
        fixture.SetHigh(true);
        void Present(float fade)
        {if(mounted)fixture.PresentMounted(wall,fade);else fixture.Present(fade);}
        string evidence=Environment.GetEnvironmentVariable("GHVR_ENVIRONMENT_EVIDENCE")!;
        string caseName=typeof(EnvironmentProgram).Assembly.GetName().Name+"-"+(toggleNative?"toggle":"high")+(mounted?"-prop":"-wall");
        int Visible(Color32[] pixels)
        {
            int n=0;foreach(var pixel in pixels)if(pixel.r>15)n++;
            System.IO.File.AppendAllText(System.IO.Path.Combine(evidence,caseName+"-pixels.txt"),n+"\n");
            return n;
        }
        void Capture(string stage)
        {
            var pixels=room.Render();var image=new Texture2D(48,48,TextureFormat.RGBA32,false);
            image.SetPixels32(pixels);image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(evidence,caseName+"-"+stage+".png"),image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        var block = new MaterialPropertyBlock();
        Present(0);int solid=Visible(room.Render());Capture("solid");
        Check(solid>20,"original HIGH upper geometry paints before its historical transition");
        Texture? noise=null;var outPixels=new List<int>();var inPixels=new List<int>();
        foreach(float fade in new[]{.0625f,.125f,.25f,.375f,.5f,.625f,.75f,.875f,.9375f,.984375f})
        {
            Present(fade);int visible=Visible(room.Render());wall.GetPropertyBlock(block);outPixels.Add(visible);
            var map=block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap"));
            Check(map!=null&&map.name=="GloomhavenVR.WallFadeNoise"
                &&map.filterMode==FilterMode.Bilinear&&map.wrapMode==TextureWrapMode.Repeat,
                "historical transition keeps its bilinear continuous noise texture");
            Check(Mathf.Abs(block.GetFloat(Shader.PropertyToID("_Cutoff"))-Mathf.Lerp(mounted?-.05f:-.15f,1f,fade))<.00001f,
                "historical transition keeps its continuous cutoff sweep");
            Check(block.GetFloat(Shader.PropertyToID("_EnableOcclusionMap"))==1f,
                mounted?"original mounted prop supplies its actual native map-enable binding":"original wall supplies its actual native map-enable binding");
            if(noise==null)noise=map;
            Check(map==noise,"every intermediate frame shares the same historical map without a binary texture bank");
            if(fade==.5f)Capture("mid");
        }
        Present(1);int held=Visible(room.Render());Capture("held");outPixels.Add(held);wall.GetPropertyBlock(block);
        var heldMap=block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap"));
        Check(held==0&&heldMap.name=="GloomhavenVR.WallFadeOccluded"
            &&Mathf.Abs(block.GetFloat(Shader.PropertyToID("_Cutoff"))-.5f)<.00001f,
            "native held endpoint preserves original authored cutoff and hidden upper geometry");
        foreach(float fade in new[]{.9375f,.75f,.5f,.25f,.125f,.0625f})
        {
            Present(fade);inPixels.Add(Visible(room.Render()));wall.GetPropertyBlock(block);
            Check(block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap"))==noise,
                "return reuses the original continuous map without per-frame texture allocation");
            Check(Mathf.Abs(block.GetFloat(Shader.PropertyToID("_Cutoff"))-Mathf.Lerp(mounted?-.05f:-.15f,1f,fade))<.00001f,
                "historical return uses the same continuous cutoff sweep");
        }
        Present(0);Capture("returned");inPixels.Add(Visible(room.Render()));
        System.IO.File.WriteAllText(System.IO.Path.Combine(evidence,caseName+"-curve.txt"),
            "OUT="+string.Join(",",outPixels)+"; IN="+string.Join(",",inPixels)+"\n");
        Check(Visible(room.Render())==solid&&(mounted||!wall.HasPropertyBlock()),
            "native solid endpoint restores the unchanged original material and removes its owned wall block");
        vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i].y=0;
        mesh.vertices=vertices;mesh.RecalculateBounds();
        Present(0);int foundation=Visible(room.Render());
        foreach(float fade in new[]{.125f,.5f,.9375f,1f})
        {Present(fade);Check(Visible(room.Render())==foundation,"native HIGH foundation remains solid through transition and held state");}
        Capture("foundation");
        Check(wall.sharedMaterial==material,"native wall shader and original material identity are never substituted");
        Present(0);UnityEngine.Object.DestroyImmediate(material);
    }

    [Serializable] private sealed class NativeFloorMaterial
    {
        public string name = "";
        public float wallFade = 0f, cutoff = 0f;
    }

    private static List<string> WallDrawLines() => VRLog.DebugLines.FindAll(line => line.StartsWith("DRAW DELIVERY:"));

    private static void WallDrawDeliveryTrace()
    {
        using var room = new Room();
        var wall = room.Surface("WallTrace.NativeFixture");
        var native = wall.sharedMaterial;
        native.SetFloat("_WallFade_On", 1f); native.EnableKeyword("_WALLFADE_ON_ON");
        var block = new MaterialPropertyBlock();
        int cut = Shader.PropertyToID("_Cutoff");
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = room.Camera;
        try
        {
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                VRLog.DebugEnabled = false;
                fixture.Step(1f/90f, true); room.Render();
                Check(WallDrawLines().Count == 0, "normal logging performs no wall draw capture");
                wall.GetPropertyBlock(block);
                Check(wall.HasPropertyBlock() && block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")) != null,
                    "disabled diagnostic leaves original wall delivery intact");
            }
            VRLog.DebugEnabled = true; VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                fixture.Step(1f/90f,true);
                var foreign = Room.Child("WallTrace.ForeignCamera", room.Root.transform).AddComponent<Camera>();
                foreign.enabled = false;
                GloomhavenVR.Rig.VRRigDriver.HeadCamera = foreign;
                room.Render();
                Check(WallDrawLines().Count == 0, "only the actual head camera captures wall delivery");
                GloomhavenVR.Rig.VRRigDriver.HeadCamera = room.Camera;
                room.Render();
                Check(WallDrawLines().Count == 1 && WallDrawLines()[0].Contains("lateToDrawChanged=False")
                    && WallDrawLines()[0].Contains("kw=[_WALLFADE_ON_ON]") && WallDrawLines()[0].Contains("route=toggle-native"),
                    "actual head-camera callback reads the delivered native renderer, shader and keyword");
                room.Render();
                Check(WallDrawLines().Count == 1, "same-frame additional eyes cannot duplicate wall draw samples");

                fixture.TraceWrite(wall, 0, .26f);
                room.ObserveRender = () => { wall.GetPropertyBlock(block); block.SetFloat(cut,-.3333f); wall.SetPropertyBlock(block); };
                room.Render(); room.ObserveRender = null;
                Check(WallDrawLines().Count == 2 && WallDrawLines()[1].Contains("lateToDrawChanged=True")
                    && WallDrawLines()[1].Contains("cutoff=-0.3333"),
                    "draw capture reads an intervening native MPB write between actual Apply and camera submission");
                wall.GetPropertyBlock(block);
                Check(Mathf.Abs(block.GetFloat(cut)+.3333f)<.00001f && wall.enabled && !wall.forceRenderingOff,
                    "read-only draw diagnostic preserves the intervening native renderer effect");
                fixture.TraceWrite(wall,0,.19f);
                room.ObserveRender = () => { native.DisableKeyword("_WALLFADE_ON_ON"); native.SetFloat("_WallFade_On",0f); wall.forceRenderingOff = true; };
                room.Render(); room.ObserveRender = null;
                Check(WallDrawLines().Count == 3 && WallDrawLines()[2].Contains("lateToDrawChanged=True")
                    && WallDrawLines()[2].Contains("forceOff=True") && WallDrawLines()[2].Contains("kw=[]")
                    && WallDrawLines()[2].Contains("wallOn=0.00"),
                    "draw capture exposes native keyword, authored material-gate and renderer-mask changes after Apply");
                Check(wall.forceRenderingOff && !native.IsKeywordEnabled("_WALLFADE_ON_ON") && native.GetFloat("_WallFade_On") == 0f,
                    "draw diagnostic preserves native keyword and render-mask writes");
                wall.forceRenderingOff = false; native.EnableKeyword("_WALLFADE_ON_ON"); native.SetFloat("_WallFade_On",1f);
            }
            VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                fixture.Step(1f/90f,true);
                var indexed = new MaterialPropertyBlock(); indexed.SetFloat(cut,.8123f);
                wall.SetPropertyBlock(indexed,0);
                room.Render();
                Check(WallDrawLines().Count == 1 && WallDrawLines()[0].Contains("slotBlock={cutoff=0.8123"),
                    "draw capture exposes material-index overrides with precedence over the renderer block");
                wall.GetPropertyBlock(block,0);
                Check(Mathf.Abs(block.GetFloat(cut)-.8123f)<.00001f, "draw diagnostic never clears native material-index overrides");
                wall.SetPropertyBlock(null,0);
            }
            VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                // Same production pre-render, renderer and MPB path. Surrogate shader
                // names are metadata boundary fixtures; native HIGH pixels are not claimed.
                Shader savedShader=native.shader;
                try
                {
                    native.shader=Shader.Find("WallTrace.WallFade.High");
                    for(int id=0;id<10;id++) { fixture.TraceWrite(wall,id,.09f); room.Render(); }
                    Check(WallDrawLines().Count==2, "first native HIGH route reserves LOW and toggle-native coverage");
                    native.shader=Shader.Find("WallTrace.WallFade.Low");
                    for(int id=10;id<14;id++) { fixture.TraceWrite(wall,id,.09f); room.Render(); }
                    native.shader=savedShader;
                    for(int id=14;id<18;id++) { fixture.TraceWrite(wall,id,.09f); room.Render(); }
                    Check(WallDrawLines().Count==6 && WallDrawLines().FindAll(line=>line.Contains("route=LOW ")).Count==2
                        && WallDrawLines().FindAll(line=>line.Contains("route=toggle-native ")).Count==2,
                        "three native route families remain independently observable");
                    var slots=wall.sharedMaterials;
                    var high=new Material(Shader.Find("WallTrace.WallFade.High"));
                    var low=new Material(Shader.Find("WallTrace.WallFade.Low"));
                    try
                    {
                        wall.sharedMaterials=new[]{high,low};
                        for(int id=18;id<22;id++) { fixture.TraceWrite(wall,id,.09f); room.Render(); }
                        Check(WallDrawLines().Count == 6, "wall draw episodes are bounded for a whole scene");
                    }
                    finally { wall.sharedMaterials=slots; UnityEngine.Object.DestroyImmediate(high); UnityEngine.Object.DestroyImmediate(low); }
                }
                finally { native.shader=savedShader; }
            }
            VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                foreach(float fade in new[]{.09f,.26f,.51f,.76f,1f,.76f,.51f,.26f,.2494f})
                { fixture.TraceWrite(wall,0,fade); room.Render(); }
                int before=WallDrawLines().Count;
                fixture.TraceWrite(wall,0,0f); room.Render();
                Check(WallDrawLines().Count==before+1 && WallDrawLines()[before].Contains("terminal=True")
                    && WallDrawLines()[before].Contains("fade=0.0000") && WallDrawLines()[before].Contains("block=False")
                    && WallDrawLines()[before].Contains("lateToDrawChanged=False"),
                    "restored zero endpoint is sampled despite sharing the last returning bucket");
                fixture.TraceWrite(wall,0,.09f); room.Render();
                Check(WallDrawLines().Count==before+1, "completed draw episode cannot absorb a second outward cycle");
            }
            VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                fixture.TraceWrite(wall,0,.09f); room.Render();
                for(int cycle=0;cycle<30;cycle++)
                    foreach(float fade in new[]{.26f,.51f,.76f,.51f,.26f,.09f})
                    { fixture.TraceWrite(wall,0,fade); room.Render(); }
                Check(WallDrawLines().Count==11, "intermediate samples reserve one bounded endpoint slot");
                fixture.TraceWrite(wall,0,0f); room.Render();
                Check(WallDrawLines().Count==12 && WallDrawLines()[11].Contains("terminal=True")
                    && WallDrawLines()[11].Contains("block=False"),
                    "repeated partial reversals retain the final restored endpoint within the sample cap");
                for(int i=0;i<10;i++) { fixture.TraceWrite(wall,0,.09f); room.Render(); }
                Check(WallDrawLines().Count==12, "endpoint completion stays bounded across later cycles");
            }
            VRLog.DebugLines.Clear();
            using (var fixture = new WallSegmentFade.Fixture(wall))
            {
                fixture.Step(1f/90f,true); PerfMonitor.ThrowDrawTrace = true;
                room.Render(); room.Render(); PerfMonitor.ThrowDrawTrace = false;
                Check(VRLog.DebugLines.FindAll(line => line.StartsWith("DRAW DELIVERY disabled")).Count == 1,
                    "a diagnostic fault is contained once without interrupting native rendering");
                fixture.Step(1f/90f,true); room.Render();
                Check(WallDrawLines().Count == 0 && wall.HasPropertyBlock(),
                    "failed diagnostic stays disabled while native wall delivery continues");
            }
            VRLog.DebugLines.Clear(); room.Render();
            Check(WallDrawLines().Count == 0, "actual disable lifecycle removes the head-camera diagnostic callback");
        }
        finally
        {
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
            VRLog.DebugEnabled = true; PerfMonitor.ThrowDrawTrace = false; room.ObserveRender = null;
        }
    }

    public static int Run()
    {
        count = 0;
        InvalidCallbackMessages.Clear();
        Application.logMessageReceived += EngineMessage;
        try
        {
            PresentationPreparationVisibility(); ScopeAndMaterials(); AmbientScopes(); ShaderRendering(); BatchesAndFallback(); ChunkPopulation(); IncrementalAndUnsafeMeshes(); NativeCompletionSurvivesPreparationFault(); StructuralChunks(); NativeWallChannelsAndRenderedClock();
            int sharedStart = count;
            // Keep each dedicated probe/flag oracle ahead of supplementary lightmap checks:
            // broad causal mutations must fail at their own native lighting boundary first.
            SharedOriginalMaterialValidation(); SharedReadOptionToggle(); VerifiedEnvironmentBank(); UnreadableExactChunks(); ExplicitCameraInstances(); MultipleSubmeshInstances(); RevealedClonePixels(); SupplementaryNativeGeometry(); NativeCameraCallbackFailureIsProcessBound(); NativeCameraBoundaryLifecycle(); NativeCameraBoundaryOrder(); CommonAbsentLighting(); LateNativeLightingFlags(); NativeChunkLightmapWrites(); LateNativeCommandBufferConsumer(); LivePreparationReport(); DetailedPreparationRefusals(); NativeObjectLighting(); ProbeRejectionSkipsPrivateGeometry(); NativeMaterialLoadStart();
            Debug.Log("Shared-material validation assertions=" + (count - sharedStart));
            NativeHighHistoricalDelivery();
            NativeHighHistoricalDelivery(toggleNative:true);
            NativeHighHistoricalDelivery(mounted:true);
            NativeHighHistoricalDelivery(toggleNative:true,mounted:true);
            WallDrawDeliveryTrace();
            return count;
        }
        finally { Application.logMessageReceived -= EngineMessage; }
    }
}
