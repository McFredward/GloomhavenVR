using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static int _checks;
    private static readonly Dictionary<Mesh,Mesh[]> Bank = new();
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static Component Driver(GameObject host) => host.GetComponent(Type.GetType("GloomhavenVR.Core.ScenarioTerrainBudget+Driver"));
    private static void Tick(GameObject host, float delta = 1f/60f) => Driver(host).GetType().GetMethod("Tick", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(Driver(host),new object[]{delta});
    private static bool Lookup(Mesh original, int detail, out Mesh result)
    { if (Bank.TryGetValue(original,out Mesh[] data)) { result=data[detail>=100?0:1]; return true; } result=null!; return false; }
    private static int Triangles(Mesh mesh) { int total=0; for(int i=0;i<mesh.subMeshCount;i++) total+=(int)mesh.GetIndexCount(i)/3; return total; }
    private static Mesh Box(string name)
    {
        var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var uv=new List<Vector2>(); var indices=new List<int>();
        for(int side=0;side<6;side++)
        {
            Vector3 n=side switch {0=>Vector3.forward,1=>Vector3.back,2=>Vector3.left,3=>Vector3.right,4=>Vector3.up,_=>Vector3.down};
            Vector3 a=side<2?Vector3.right:Vector3.forward; Vector3 b=Vector3.Cross(n,a);
            int offset=vertices.Count;
            for(int y=0;y<=8;y++) for(int x=0;x<=8;x++)
            { vertices.Add(n*.5f+a*(x/8f-.5f)+b*(y/8f-.5f)); normals.Add(n); uv.Add(new Vector2(x/8f,y/8f)); }
            for(int y=0;y<8;y++) for(int x=0;x<8;x++)
            { int i=offset+y*9+x; indices.AddRange(new[]{i,i+1,i+10,i,i+10,i+9}); }
        }
        var mesh=new Mesh {name=name}; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0); mesh.RecalculateBounds(); return mesh;
    }
    private static Mesh Coarse(Mesh exact)
    {
        Mesh coarse=Object.Instantiate(exact); Vector3[] vertices=coarse.vertices;
        for(int i=0;i<vertices.Length;i++) vertices[i]=new Vector3(Mathf.Round(vertices[i].x*2)/2,Mathf.Round(vertices[i].y*2)/2,Mathf.Round(vertices[i].z*2)/2);
        var triangles=new List<int>(); int[] source=exact.triangles;
        for(int i=0;i<source.Length;i+=3)
            if(Vector3.Cross(vertices[source[i+1]]-vertices[source[i]],vertices[source[i+2]]-vertices[source[i]]).sqrMagnitude>1e-10f)
                triangles.AddRange(new[]{source[i],source[i+1],source[i+2]});
        coarse.vertices=vertices; coarse.triangles=triangles.ToArray(); coarse.bounds=exact.bounds; return coarse;
    }
    private static MeshRenderer Surface(GameObject scenario,string name,Vector3 position,Material material,bool wallOwned=true)
    {
        var tile=new GameObject(name+" owner"); tile.transform.SetParent(scenario.transform,false);
        if(wallOwned) ProceduralWall.m_WallCache.Add(tile.AddComponent<ProceduralWall>()); else tile.AddComponent<ProceduralMapTile>();
        var content=new GameObject("Generated Content"); content.transform.SetParent(tile.transform,false);
        var obj=new GameObject(name); obj.transform.SetParent(content.transform,false); obj.transform.position=position;
        Mesh exact=Box(name); Mesh coarse=Coarse(exact); Mesh original=Object.Instantiate(exact); original.name=name; original.UploadMeshData(true);
        Bank.Add(original,new[]{exact,coarse}); obj.AddComponent<MeshFilter>().sharedMesh=original;
        var r=obj.AddComponent<MeshRenderer>(); r.sharedMaterial=material; return r;
    }
    private static List<MeshRenderer> Proxies(GameObject host)
    { return new List<MeshRenderer>(host.GetComponentsInChildren<MeshRenderer>(true)); }
    private static void Render(Camera camera) { camera.Render(); }
    private static bool DuringRender(Camera camera,Func<bool> observe)
    {
        bool result=false; Camera.CameraCallback callback=cam=>{if(cam==camera)result=observe();};
        Camera.onPreCull+=callback;
        try { camera.Render(); } finally { Camera.onPreCull-=callback; }
        return result;
    }
    private static void Morph(GameObject host,int count=28) { for(int i=0;i<count;i++) Tick(host); }
    private static Color[] Pixels(Camera camera)
    {
        Render(camera); RenderTexture.active=camera.targetTexture;
        var image=new Texture2D(96,96,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,96,96),0,0); image.Apply(); Color[] pixels=image.GetPixels(); Object.DestroyImmediate(image); RenderTexture.active=null; return pixels;
    }
    private static int Visible(Color[] pixels) { int n=0; foreach(Color pixel in pixels) if(pixel.r+pixel.g+pixel.b>.05f)n++; return n; }
    private static void DisposeBank() { foreach(var entry in Bank) foreach(Mesh mesh in entry.Value) Object.DestroyImmediate(mesh); Bank.Clear(); }
    public static int Run()
    {
        PerfConfig.SharedEnvironmentMaterialReadsOn=false; PerfConfig.TerrainCameraSourceLimit=0; PerfConfig.TerrainSubstitutionOn=true;
        _checks=0; Bank.Clear(); ProceduralWall.m_WallCache.Clear(); VRLog.Faults.Clear(); BundleShaders.Throw=false;
        PerfConfig.CheapWallShadingOn=true; PerfConfig.TerrainDetailPercent=100; PerfConfig.DistantTerrainDetailPercent=100;
        var host=new GameObject("GloomhavenVR.TerrainOwner"); var scenario=new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
        var material=new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint",new Color(.8f,.4f,.2f,1));
        var cameraGo=new GameObject("Camera"); var camera=cameraGo.AddComponent<Camera>(); camera.transform.position=new Vector3(0,1,-3); camera.transform.LookAt(new Vector3(0,1,0));
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.orthographic=true; camera.orthographicSize=2f;
        camera.targetTexture=new RenderTexture(96,96,24,RenderTextureFormat.ARGBFloat); GloomhavenVR.Rig.VRRigDriver.HeadCamera=camera;
        bool ready=false,unavailable=false;
        ScenarioTerrainBudget.ConfigureAssetPreparation(()=>ready,()=>unavailable);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey,Lookup); ScenarioTerrainBudget.Install(host);
        var wall=Surface(scenario,"CV_Wall_Generic_01",new Vector3(0,1,0),material);
        var floor=Surface(scenario,"CV_Floor_Basic",new Vector3(1,0,0),material,false);
        var floorHex=Surface(scenario,"EN_CR_FloorHex_Edge_Even2",new Vector3(2,0,0),material,false);
        var wallFloorHex=Surface(scenario,"EN_CR_FloorHex_Edge_Even2",new Vector3(3,0,0),material);
        var anonymous=Surface(scenario,"Mesh",new Vector3(4,0,0),material);
        var foundation=Surface(scenario,"CR_RU_UnderWall_01_Slabs",new Vector3(5,0,0),material);
        var tileWall=Surface(scenario,"CV_Wall_Generic_01",new Vector3(6,0,0),material,false);
        Mesh floorOriginal=floor.GetComponent<MeshFilter>().sharedMesh;
        Mesh original=wall.GetComponent<MeshFilter>().sharedMesh; Material[] slots=wall.sharedMaterials;
        var collider=wall.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=original;
        ScenarioTerrainBudget.QueueRoot(scenario); Tick(host);
        FieldInfo pending=Driver(host).GetType().GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic);
        Check(Proxies(host).Count==0&&((System.Collections.ICollection)pending.GetValue(Driver(host))).Count>0,
            "temporary bank readiness retains discovery while native rendering continues");
        unavailable=true; Tick(host); Tick(host);
        Check(Proxies(host).Count==0&&((System.Collections.ICollection)pending.GetValue(Driver(host))).Count==0,
            "terminal bank failure clears pending discovery without holding native continuation");
        Check(VRLog.Faults.FindAll(text=>text.Contains("assets unavailable")).Count==1,"terminal asset fallback reports bounded useful ordinary context");
        ready=true; unavailable=false; Tick(host);
        Check(Proxies(host).Count==1&&!ScenarioTerrainBudget.OwnsRenderSubstitute(floor),"eligible non-floor terrain has private proxies while floors remain native for submission budgets");
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(floorHex)&&!ScenarioTerrainBudget.OwnsRenderSubstitute(wallFloorHex)
            &&floorHex.sharedMaterial==material&&wallFloorHex.sharedMaterial==material,"FloorHex stays native under either map tile or explicit wall ownership");
        Check((bool)typeof(ScenarioTerrainBudget).GetMethod("FloorIdentity",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{floorHex.GetComponent<MeshFilter>().sharedMesh}),"complete floor identity veto recognizes FloorHex without underscore pattern");
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(anonymous)&&!ScenarioTerrainBudget.OwnsRenderSubstitute(foundation),
            "bank provenance alone never admits anonymous geometry or floor foundation slabs");
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(tileWall),"audited wall name under map tile without ProceduralWall stays native");
        Check(ScenarioTerrainBudget.OwnsRenderSubstitute(wall)&&!ScenarioTerrainBudget.HasCurrentRenderLease(wall),
            "prepared terrain ownership is distinct from a current unacquired camera lease");
        Check(DuringRender(camera,()=>ScenarioTerrainBudget.HasCurrentRenderLease(wall)),
            "terrain current lease is observable during its actual camera draw");
        Check(!ScenarioTerrainBudget.HasCurrentRenderLease(wall),
            "terrain current lease ends after its paired post-render recovery");
        Check(Visible(Pixels(camera))>100,"cheap terrain submits actual nonempty camera pixels");
        Check(!wall.forceRenderingOff&&!floor.forceRenderingOff&&Proxies(host).TrueForAll(r=>!r.enabled),"post-render restores every native source and disables private proxies");
        Check(wall.sharedMaterials[0]==slots[0]&&wall.GetComponent<MeshFilter>().sharedMesh==original&&collider.sharedMesh==original,"native source mesh materials and collision are untouched");
        var clone=Object.Instantiate(wall.gameObject); Check(clone.GetComponent<MeshRenderer>().sharedMaterials.Length==1&&clone.GetComponent<MeshRenderer>().sharedMaterial==material,"native clone retains nonempty original material slots");
        Check(clone.transform.childCount==0,"native clone inherits no private proxy"); Object.DestroyImmediate(clone);
        Check(wall.GetComponent<MeshRenderer>().isPartOfStaticBatch==false,"native source never acquires internal static batch state");

        MeshRenderer liveProxy=Proxies(host).Find(r=>r.transform.position==wall.transform.position)!;
        liveProxy.transform.hasChanged=false; TerrainWriteObserver.PoseWrites=0; Render(camera);
        Check(!liveProxy.transform.hasChanged&&TerrainWriteObserver.PoseWrites==0,"settled terrain camera retains private proxy transform without repeated native writes");
        wall.transform.position+=Vector3.right*.1f;
        Check(DuringRender(camera,()=>liveProxy.transform.position==wall.transform.position),"native structural movement remains immediate after private pose reuse");
        wall.transform.position-=Vector3.right*.1f; Render(camera);
        wall.shadowCastingMode=ShadowCastingMode.TwoSided; wall.receiveShadows=false;
        wall.lightProbeUsage=LightProbeUsage.CustomProvided; wall.reflectionProbeUsage=ReflectionProbeUsage.Simple;
        wall.sortingOrder=7; wall.allowOcclusionWhenDynamic=false; wall.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        var anchor=new GameObject("Native probe anchor"); wall.probeAnchor=anchor.transform;
        Check(DuringRender(camera,()=>liveProxy.shadowCastingMode==wall.shadowCastingMode&&liveProxy.receiveShadows==wall.receiveShadows
            &&liveProxy.lightProbeUsage==wall.lightProbeUsage&&liveProxy.reflectionProbeUsage==wall.reflectionProbeUsage
            &&liveProxy.probeAnchor==anchor.transform&&liveProxy.sortingOrder==7&&!liveProxy.allowOcclusionWhenDynamic
            &&liveProxy.motionVectorGenerationMode==wall.motionVectorGenerationMode),"every native renderer-state edit remains live after exact private write reuse");
        wall.shadowCastingMode=ShadowCastingMode.On; wall.receiveShadows=true;
        wall.lightProbeUsage=LightProbeUsage.BlendProbes; wall.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
        wall.sortingOrder=0; wall.allowOcclusionWhenDynamic=true; wall.motionVectorGenerationMode=MotionVectorGenerationMode.Object;
        wall.probeAnchor=null; Object.DestroyImmediate(anchor);

        // Static Camera callbacks still run when their MonoBehaviour host is inactive.
        // Compare actual camera pixels with the native route, not post-render flags alone.
        PerfConfig.CheapWallShadingOn=false; Color[] nativeView=Pixels(camera);
        PerfConfig.CheapWallShadingOn=true; host.SetActive(false);
        bool inactiveMasksClear=DuringRender(camera,()=>!wall.forceRenderingOff&&!floor.forceRenderingOff
            &&Proxies(host).TrueForAll(r=>!r.enabled));
        Color[] inactiveView=Pixels(camera); bool sameInactiveView=true;
        for(int pixel=0;pixel<nativeView.Length;pixel++)
            if(Mathf.Abs(nativeView[pixel].r-inactiveView[pixel].r)>1e-5f
                ||Mathf.Abs(nativeView[pixel].g-inactiveView[pixel].g)>1e-5f
                ||Mathf.Abs(nativeView[pixel].b-inactiveView[pixel].b)>1e-5f)sameInactiveView=false;
        Color nativeCenter=nativeView[48*96+48];
        Check(nativeCenter.r+nativeCenter.g+nativeCenter.b>.05f&&sameInactiveView,
            "deactivated terrain host retains original wall pixels instead of masking for inactive proxies");
        Check(inactiveMasksClear&&!Driver(host).GetComponent<Behaviour>().isActiveAndEnabled,
            "deactivated terrain host acquires no native masks in an actual camera callback");
        host.SetActive(true);
        Check(DuringRender(camera,()=>wall.forceRenderingOff&&Proxies(host).Exists(r=>r.enabled&&r.gameObject.activeInHierarchy)),
            "reactivated terrain host resumes its paired live renderer lease");

        PerfConfig.CheapWallShadingOn=false; PerfConfig.TerrainDetailPercent=0; Tick(host,5f);
        MeshFilter proxyFilter=Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.GetComponent<MeshFilter>();
        Pixels(camera);
        Mesh first=proxyFilter.sharedMesh;
        Check(first!=null&&first.vertexCount==Bank[original][0].vertexCount,"geometry transition preserves original vertex indexing");
        Check(first!.vertices[10]!=Bank[original][1].vertices[10],"stalled frame retains visible intermediate geometry");
        Check(Triangles(first!)==Triangles(original),"geometry transition keeps exact original topology before endpoint");
        Morph(host); Pixels(camera);
        Check(Triangles(proxyFilter.sharedMesh)<Triangles(original)/2,"coarse 3D endpoint materially reduces submitted triangles");
        Check(proxyFilter.sharedMesh.bounds.size.z>0,"strongest terrain retains actual 3D volume");
        Check(Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.sharedMaterial==material,"coarse geometry retains native shading when cheap shading is off");
        Check(floor.GetComponent<MeshFilter>().sharedMesh==floorOriginal&&floor.sharedMaterial==material
            &&!ScenarioTerrainBudget.OwnsRenderSubstitute(floor),"floor never morphs or fades during geometry reductions");
        Check(PerfMonitor.Counts["Terrain.SubmittedTriangles"]<PerfMonitor.Counts["Terrain.OriginalTriangles"],"production triangle receipt measures actual submitted reduction");

        PerfConfig.TerrainDetailPercent=100; PerfConfig.DistantTerrainDetailPercent=0; PerfConfig.TerrainDistanceMeters=2f;
        camera.transform.position=new Vector3(0,1,-1); Tick(host); Morph(host); Pixels(camera);
        Check(proxyFilter.sharedMesh==original||!ScenarioTerrainBudget.OwnsRenderSubstitute(wall),"near terrain uses selected full geometry");
        camera.transform.position=new Vector3(0,1,-4); Morph(host); Pixels(camera);
        Check(ScenarioTerrainBudget.OwnsRenderSubstitute(wall)&&DuringRender(camera,()=>wall.forceRenderingOff)
            &&Triangles(proxyFilter.sharedMesh)<Triangles(original)/2,"independent distant terrain detail cap selects coarser original 3D geometry");
        var hand=new GameObject("Hand").AddComponent<GloomhavenVR.Hands.VRHand>(); hand.HasPose=true; hand.transform.position=wall.transform.position;
        GloomhavenVR.Hands.VRHands.Left=hand; Morph(host); Pixels(camera);
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(wall),"tracked hand proximity restores exact original geometry");
        hand.HasPose=false; Morph(host); Pixels(camera);
        Check(ScenarioTerrainBudget.OwnsRenderSubstitute(wall),"tracking loss is read on the next terrain Update rather than retaining the previous hand pose");
        hand.HasPose=true; hand.transform.position=wall.bounds.max+Vector3.right*.18f; hand.WorldScale=2f;
        Morph(host); Pixels(camera);
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(wall),"current tracked hand scale restores full geometry at the native metric proximity threshold");
        hand.WorldScale=.5f; Morph(host); Pixels(camera);
        Check(ScenarioTerrainBudget.OwnsRenderSubstitute(wall),"changed tracked hand scale is read on the next terrain Update");
        GloomhavenVR.Hands.VRHands.Left=null; Object.DestroyImmediate(hand.gameObject);

        PerfConfig.CheapWallShadingOn=true; PerfConfig.TerrainDetailPercent=100; PerfConfig.DistantTerrainDetailPercent=100; Morph(host);
        wall.renderingLayerMask=8u;
        Check(DuringRender(camera,()=>wall.forceRenderingOff
            &&Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.renderingLayerMask==8u),
            "actual terrain camera proxy preserves current native rendering layers");
        wall.renderingLayerMask=2u;
        Check(DuringRender(camera,()=>Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.renderingLayerMask==2u),
            "native rendering layer edit remains live between terrain camera invocations");
        wall.renderingLayerMask=1u;
        bool leased=false,restored=false,leaseBeforeMaskEdit=false,leaseAfterMaskEdit=false,leaseAfterRelease=true;
        Camera.CameraCallback inspect=cam=>
        {
            if(cam!=camera)return;
            leased=wall.forceRenderingOff;
            leaseBeforeMaskEdit=ScenarioTerrainBudget.HasCurrentRenderLease(wall);
            wall.forceRenderingOff=false;
            leaseAfterMaskEdit=ScenarioTerrainBudget.HasCurrentRenderLease(wall);
            ScenarioTerrainBudget.BeforeNativeRendererWrite(wall);
            restored=!wall.forceRenderingOff&&Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.enabled==false;
            leaseAfterRelease=ScenarioTerrainBudget.HasCurrentRenderLease(wall);
        };
        Camera.onPreCull+=inspect; Render(camera); Camera.onPreCull-=inspect;
        Check(leased&&restored,"native pre-cull write synchronously revokes an already prepared proxy");
        Check(leaseBeforeMaskEdit&&leaseAfterMaskEdit&&!leaseAfterRelease,
            "terrain current lease survives late native mask edits until explicit owner release");
        Check(PerfMonitor.Counts["Terrain.OriginalTriangles"]==0&&PerfMonitor.Counts["Terrain.CheapSurfaces"]==0,
            "revoked native-write camera leases are absent from terrain completion counters");
        material.EnableKeyword("_WALLFADE_ON_ON"); material.SetFloat("_WallFade_On",1f); Pixels(camera);
        Check(Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.sharedMaterial.GetFloat("_GHVRTerrainNativeRoute")==3,"native keyword edits remain live between actual camera invocations");
        material.DisableKeyword("_WALLFADE_ON_ON"); material.SetFloat("_WallFade_On",0);
        var variant=new Material(material); material.EnableKeyword("_WALLFADE_ON_ON"); material.SetFloat("_WallFade_On",1f);
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(current=>current==variant?material:current); wall.sharedMaterial=variant;
        Pixels(camera);
        Check(Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.sharedMaterial.GetFloat("_GHVRTerrainNativeRoute")==3,
            "existing environment material variant resolves to genuine original native fade state");
        wall.sharedMaterial=material; Object.DestroyImmediate(variant); material.DisableKeyword("_WALLFADE_ON_ON"); material.SetFloat("_WallFade_On",0);
        var block=new MaterialPropertyBlock(); block.SetColor("_Tint",Color.green); wall.SetPropertyBlock(block,0); Pixels(camera);
        var observed=new MaterialPropertyBlock(); Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.GetPropertyBlock(observed,0);
        Check(observed.GetColor("_Tint")==Color.green,"native material-slot MPB precedence is retained"); wall.SetPropertyBlock(null,0);
        Render(camera); liveProxy.GetPropertyBlock(observed,0);
        Check(observed.isEmpty,"removed native slot property block is cleared before the next terrain camera draw");
        var liveTexture=new Texture2D(1,1); liveTexture.SetPixel(0,0,Color.magenta); liveTexture.Apply();
        block.Clear(); block.SetColor("_Tint",Color.blue); block.SetTexture("_MainTex",liveTexture); wall.SetPropertyBlock(block);
        Render(camera); liveProxy.GetPropertyBlock(observed);
        Check(observed.GetColor("_Tint")==Color.blue&&observed.GetTexture("_MainTex")==liveTexture,
            "new renderer-wide color and texture property block after an empty eye remains immediate");
        wall.SetPropertyBlock(null); Render(camera); liveProxy.GetPropertyBlock(observed);
        Check(observed.GetColor("_Tint")==default&&observed.GetTexture("_MainTex")==null&&observed.GetFloat("_GHVRTerrainNeverFade")==0
            &&observed.GetFloat("_GHVRWorldNeverFade")==0,"removed native renderer block restores empty-source defaults and both native wall fade channels");
        TerrainReadObserver.Reset(); Render(camera);
        Check(TerrainReadObserver.PropertyGuards==1&&TerrainReadObserver.PropertyReads==0
            &&TerrainReadObserver.EffectReads==0&&TerrainReadObserver.PropertyWrites==0,
            "settled empty native MPB checks one fresh guard and skips all block reads effect reads and private rewrites");
        block.SetColor("_Tint",Color.red); wall.SetPropertyBlock(block); TerrainReadObserver.Reset(); Render(camera);
        // The existing renderer-effect causal control deliberately removes that
        // entire check. Do not let this access ceiling mask its later native-effect
        // assertion with an unrelated missing-read failure.
        Check(TerrainReadObserver.EffectReads<=3&&TerrainReadObserver.PropertyReads==2,
            "empty native material-slot block skips effect reads while a nonempty renderer block remains fresh");
        wall.SetPropertyBlock(null); Render(camera); Object.DestroyImmediate(liveTexture);
        PropertyBridgeChannels(host,scenario,material);
        var liveEffect=new MaterialPropertyBlock(); liveEffect.SetFloat("_AddVertexAnim",1f); wall.SetPropertyBlock(liveEffect);
        Check(!DuringRender(camera,()=>wall.forceRenderingOff),"live renderer-wide vertex effect retains native geometry and shader"); wall.SetPropertyBlock(null);
        liveEffect.Clear(); liveEffect.SetFloat("_UseEmissiveMap",1f); wall.SetPropertyBlock(liveEffect,0);
        Check(!DuringRender(camera,()=>wall.forceRenderingOff),"live material-slot emissive effect retains native geometry and shader"); wall.SetPropertyBlock(null,0);
        var command=new CommandBuffer(); camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,command); Pixels(camera);
        Check(!DuringRender(camera,()=>wall.forceRenderingOff),"native command-buffer camera keeps original renderer geometry"); camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,command); command.Dispose();
        var ownedCommand=new CommandBuffer(); camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,ownedCommand);
        FieldInfo commandPointer=typeof(CommandBuffer).GetField("m_Ptr",BindingFlags.NonPublic|BindingFlags.Instance)
            ??throw new Exception("actual Unity command buffer native identity unavailable");
        Check(commandPointer.GetValue(ownedCommand) is IntPtr available&&available!=IntPtr.Zero,
            "actual Unity command buffer native identity is available to explicit ownership boundary");
        ScenarioTerrainBudget.ConfigureNativeCameraConsumers(cam=>
        {
            foreach(CameraEvent evt in Enum.GetValues(typeof(CameraEvent)))
                foreach(CommandBuffer consumer in cam.GetCommandBuffers(evt))
                    if(cam!=camera||evt!=CameraEvent.BeforeForwardOpaque
                        ||!(commandPointer.GetValue(ownedCommand) is IntPtr owned)||owned==IntPtr.Zero
                        ||!(commandPointer.GetValue(consumer) is IntPtr actual)||actual!=owned)return true;
            return false;
        });
        Check(DuringRender(camera,()=>wall.forceRenderingOff),"exact known environment command buffer does not disable terrain rendering");
        var foreignCommand=new CommandBuffer(); camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,foreignCommand);
        Check(!DuringRender(camera,()=>wall.forceRenderingOff),"additional native command buffer retains original identity despite a known mod buffer");
        camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,foreignCommand); foreignCommand.Dispose();
        camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,ownedCommand); ownedCommand.Dispose();
        ScenarioTerrainBudget.ConfigureNativeCameraConsumers(cam=>cam.commandBufferCount>0);

        var excluded=Surface(scenario,"CV_Wall_Generic_01",new Vector3(2,1,0),material); excluded.gameObject.AddComponent<ActorBehaviour>(); ScenarioTerrainBudget.QueueRoot(excluded.gameObject); Tick(host);
        Check(Proxies(host).Count==1,"actor-owned source with genuine bank mesh is never scenery");
        wall.gameObject.AddComponent<CInteractable>(); Pixels(camera);
        Check(!DuringRender(camera,()=>wall.forceRenderingOff),"late native interaction veto preserves original presentation");
        Object.DestroyImmediate(wall.GetComponent<CInteractable>());
        // Two independently visible sources sharing one original exercise actual
        // bank reads, then a changed verdict between genuine camera invocations.
        var repeated=Object.Instantiate(wall.gameObject).GetComponent<MeshRenderer>();
        repeated.transform.SetParent(wall.transform.parent,false);
        repeated.transform.localPosition+=new Vector3(8,0,0);
        ScenarioTerrainBudget.QueueRoot(repeated.gameObject); Tick(host);
        int bankReads=0; bool bankAllowed=true;
        ScenarioTerrainBudget.ConfigureMeshBank(mesh=>{bankReads++; return bankAllowed&&Bank.ContainsKey(mesh);},Lookup);
        bankReads=0;
        Check(DuringRender(camera,()=>wall.forceRenderingOff&&repeated.forceRenderingOff)&&bankReads==1,
            "repeated native mesh admission is read exactly once in a synchronous camera invocation");
        bankAllowed=false; bankReads=0;
        Check(!DuringRender(camera,()=>wall.forceRenderingOff||repeated.forceRenderingOff)&&bankReads==1,
            "changed bank admission is re-read between actual camera invocations");
        bankAllowed=true; repeated.gameObject.SetActive(false); bankReads=0;
        Check(DuringRender(camera,()=>wall.forceRenderingOff&&!repeated.forceRenderingOff)&&bankReads==1,
            "inactive repeated sources retain native flags and do not require bank admission");
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey,Lookup);
        Object.DestroyImmediate(repeated.gameObject);
        BudgetScaling(host,wall,camera);
        ConfigurableTerrainGate(host,wall,camera);
        GloomhavenVR.Board.FigureGrab.HeldProps.SetRoots(scenario); Pixels(camera);
        Check(Proxies(host).TrueForAll(r=>!r.enabled),"held-source veto retains native rendering"); GloomhavenVR.Board.FigureGrab.HeldProps.SetRoots();
        wall.enabled=false; Pixels(camera); Check(!DuringRender(camera,()=>wall.forceRenderingOff),"native disabled visibility immediately suppresses private proxy"); wall.enabled=true;
        wall.forceRenderingOff=true; Pixels(camera); Check(wall.forceRenderingOff&&Proxies(host).TrueForAll(r=>!r.enabled),"foreign native render mask is preserved"); wall.forceRenderingOff=false;
        Mesh foreign=Box("foreign"); wall.GetComponent<MeshFilter>().sharedMesh=foreign; Tick(host); Pixels(camera);
        Check(wall.GetComponent<MeshFilter>().sharedMesh==foreign&&!ScenarioTerrainBudget.OwnsRenderSubstitute(wall)
            &&!DuringRender(camera,()=>wall.forceRenderingOff),"foreign native mesh replacement is retained and owned proxy retired");
        Object.DestroyImmediate(foreign);
        var exactOnly=Surface(scenario,"CV_Pillar_Generic_01",new Vector3(8,1,0),material);
        Mesh exactOnlySource=exactOnly.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(Bank[exactOnlySource][1]); Bank[exactOnlySource][1]=Object.Instantiate(Bank[exactOnlySource][0]);
        PerfConfig.CheapWallShadingOn=false; PerfConfig.TerrainDetailPercent=0;
        ScenarioTerrainBudget.QueueRoot(exactOnly.gameObject); Morph(host);
        Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(exactOnly)&&!DuringRender(camera,()=>exactOnly.forceRenderingOff)
            &&exactOnly.GetComponent<MeshFilter>().sharedMesh==exactOnlySource,
            "coarse bank tier without actual triangle saving retains the native renderer");
        PerfConfig.CheapWallShadingOn=true; PerfConfig.TerrainDetailPercent=100;
        WorldMaterialBridge(host,scenario,camera,material);
        var faultWall=Surface(scenario,"CV_Wall_Generic_02",new Vector3(-1,1,0),material);
        BundleShaders.Throw=true; Component oldDriver=Driver(host); ScenarioTerrainBudget.Shutdown(); Object.DestroyImmediate(oldDriver);
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Tick(host); Render(camera);
        Check(VRLog.Faults.Exists(text=>text.Contains("fixture shader resolver fault"))&&!faultWall.forceRenderingOff&&!floor.forceRenderingOff,"shader failure restores native rendering without escaping continuation");
        ScenarioTerrainBudget.Shutdown(); BundleShaders.Throw=false;
        ShaderPixels(camera);
        NoisePixels(camera);
        Check(VRLog.Faults.FindAll(text=>text.Contains("presentation failed")).Count==1,"optional failure reports once with useful normal-level context");
        scenario.SetActive(false); NativeCoverage(camera);
        Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(camera.targetTexture); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(material); DisposeBank();
        return _checks;
    }
    private sealed class MaterialPass : IDisposable
    {
        internal static int Open, Started;
        internal MaterialPass() { Open++; Started++; }
        public void Dispose() { Open--; }
    }
    private static void WorldMaterialBridge(GameObject host,GameObject scenario,Camera camera,Material original)
    {
        var source=Surface(scenario,"CV_Wall_Generic_04",new Vector3(0,1,0),original);
        var variant=new Material(original) {name="Fixture.PrivateWorldMaterial"};
        bool allowed=true,world=true;
        int calls=0;
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material=>{calls++;return allowed?variant:material;},
            ()=>new MaterialPass(),()=>world,material=>material==variant);
        ScenarioTerrainBudget.QueueRoot(source.gameObject); Tick(host);
        try
        {
            Check(DuringRender(camera,()=>source.forceRenderingOff && Proxies(host).Exists(proxy=>proxy.enabled&&proxy.sharedMaterial==variant)),
                "terrain world bridge submits the global owner variant on private geometry");
            Check(calls>0&&MaterialPass.Open==0&&MaterialPass.Started>0&&source.sharedMaterial==original,
                "terrain world material reads are invocation-scoped and never replace native source slots");
            allowed=false;
            Check(DuringRender(camera,()=>!source.forceRenderingOff&&!Proxies(host).Exists(proxy=>proxy.enabled&&proxy.sharedMaterial==variant)),
                "world shader refusal retains whole native source without falling through to legacy cheap shader");
            allowed=true;
            Check(DuringRender(camera,()=>source.forceRenderingOff),"world material recovery can resubmit after a current native refusal");
            ScenarioTerrainBudget.BeforeNativeContentChange();
            Check(!source.forceRenderingOff&&Proxies(host).TrueForAll(proxy=>!proxy.enabled),
                "variant disposal bridge revokes factory-only terrain consumers synchronously");
            world=false;
            Check(DuringRender(camera,()=>source.forceRenderingOff&&!Proxies(host).Exists(proxy=>proxy.enabled&&proxy.sharedMaterial==variant)),
                "world mode off restores independently configured legacy wall shading");
        }
        finally
        {
            ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material=>material,()=>new MaterialPass(),()=>false,_=>false);
            ScenarioTerrainBudget.BeforeNativeContentChange();
            source.gameObject.SetActive(false);
            Object.DestroyImmediate(variant);
        }
    }
    private static void BudgetScaling(GameObject host,MeshRenderer original,Camera camera)
    {
        var clones=new List<MeshRenderer>();
        for(int i=0;i<95;i++)
        {
            var clone=Object.Instantiate(original.gameObject).GetComponent<MeshRenderer>();
            clone.transform.SetParent(original.transform.parent,false);
            clone.transform.localPosition=original.transform.localPosition;
            clones.Add(clone); ScenarioTerrainBudget.QueueRoot(clone.gameObject);
        }
        for(int i=0;i<16;i++)Tick(host);
        var left=new GameObject("Scaling left hand").AddComponent<GloomhavenVR.Hands.VRHand>();
        var right=new GameObject("Scaling right hand").AddComponent<GloomhavenVR.Hands.VRHand>();
        left.HasPose=right.HasPose=true; left.transform.position=Vector3.one*100; right.transform.position=Vector3.one*-100;
        GloomhavenVR.Hands.VRHands.Left=left; GloomhavenVR.Hands.VRHands.Right=right;
        TerrainReadObserver.Reset(); Tick(host);
        Check(TerrainReadObserver.HeadPositions==1&&TerrainReadObserver.HeadScales==1&&TerrainReadObserver.HandPositions==2,
            "ninety-six prepared terrain sources read current head and both tracked positions only once per synchronous Update");
        GloomhavenVR.Hands.VRHands.Left=null; GloomhavenVR.Hands.VRHands.Right=null;
        Object.DestroyImmediate(left.gameObject); Object.DestroyImmediate(right.gameObject);
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;
        PerfConfig.TerrainCameraSourceLimit=12; TerrainWriteObserver.MaterialReads=0; TerrainReadObserver.Reset();
        Check(DuringRender(camera,()=>clones.FindAll(renderer=>renderer.forceRenderingOff).Count
            +(original.forceRenderingOff?1:0)==12),"terrain CPU cap admits only bounded private substitutes while every remaining original stays native");
        int cappedReads=TerrainWriteObserver.MaterialReads;
        Check(cappedReads==12&&PerfMonitor.Counts["Terrain.CameraCandidates"]==12,
            "actual per-eye native material reads scale with terrain CPU cap instead of all prepared sources");
        // The existing enabled-visibility negative control removes that native
        // getter altogether; permit its missing read here so it still reaches
        // the later actual disabled-source visibility assertion. This ceiling
        // continues to reject an exhausted cap reading the whole remainder.
        Check(TerrainReadObserver.EnabledReads<=12&&TerrainReadObserver.ActiveReads==12&&TerrainReadObserver.MaskReads==12,
            "exhausted shared terrain cap skips all native visibility reads for its untouched prepared remainder");
        int cappedEnabled=TerrainReadObserver.EnabledReads,cappedActive=TerrainReadObserver.ActiveReads,cappedMasks=TerrainReadObserver.MaskReads;
        Check(clones.TrueForAll(renderer=>renderer.enabled&&!renderer.forceRenderingOff)
            && original.enabled&&!original.forceRenderingOff,"budget fallback preserves every original room source after render");
        int[] chosen=null!;
        DuringRender(camera,()=>{chosen=clones.FindAll(renderer=>renderer.forceRenderingOff).ConvertAll(renderer=>renderer.GetInstanceID()).ToArray();return true;});
        Check(DuringRender(camera,()=>clones.FindAll(renderer=>renderer.forceRenderingOff).ConvertAll(renderer=>renderer.GetInstanceID()).ToArray().SequenceEqual(chosen)),
            "settled terrain CPU selection is stable across genuine successive camera invocations");
        var deferred=clones.First(renderer=>!chosen.Contains(renderer.GetInstanceID()));
        Check(DuringRender(camera,()=>ScenarioTerrainBudget.OwnsRenderSubstitute(deferred)
            &&!ScenarioTerrainBudget.HasCurrentRenderLease(deferred)
            &&clones.Where(renderer=>renderer.forceRenderingOff).All(ScenarioTerrainBudget.HasCurrentRenderLease)),
            "prepared cap-deferred terrain source has no current lease while admitted sources retain theirs");
        deferred.gameObject.SetActive(false); Render(camera);
        Check(PerfMonitor.Counts["ScenarioTerrain.BudgetDeferred"]==84&&PerfMonitor.Counts["Terrain.CameraBudgetFallback"]==0,
            "unexamined inactive terrain remainder is deferred preparation and never claimed as examined active fallback");
        deferred.gameObject.SetActive(true);
        PerfConfig.SharedEnvironmentMaterialReadsOn=false; TerrainReadObserver.Reset(); Render(camera);
        Check(TerrainReadObserver.EnabledReads<=96&&TerrainReadObserver.ActiveReads==96&&TerrainReadObserver.MaskReads==96
            &&PerfMonitor.Counts["Terrain.CameraBudgetFallback"]==84&&PerfMonitor.Counts["ScenarioTerrain.BudgetDeferred"]==0,
            "shared-read off retains exact examined active fallback counts and its complete legacy visibility path");
        Debug.Log("Terrain visibility source-bound reads (shared/legacy,96 sources/cap12): enabled="+cappedEnabled+"/"+TerrainReadObserver.EnabledReads
            +", active="+cappedActive+"/"+TerrainReadObserver.ActiveReads+", mask="+cappedMasks+"/"+TerrainReadObserver.MaskReads+".");
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;
        // A prior eye with a wider budget cannot leave a lease behind when the
        // following eye now rejects that exact source before any native read.
        PerfConfig.TerrainCameraSourceLimit=0;
        // Execute the genuine entry without its paired post-render to represent
        // an interrupted prior camera. The next actual Render must release ALL
        // previous leases, including sources now deferred without native reads.
        Component owner=Driver(host);
        owner.GetType().GetMethod("HandlePreCull",BindingFlags.NonPublic|BindingFlags.Instance)!
            .Invoke(owner,new object[]{camera});
        bool wider=deferred.forceRenderingOff&&Proxies(host).FindAll(proxy=>proxy.enabled).Count==96;
        PerfConfig.TerrainCameraSourceLimit=12;
        Check(wider&&DuringRender(camera,()=>!deferred.forceRenderingOff
            &&Proxies(host).FindAll(proxy=>proxy.enabled).Count==12),
            "narrowed next-eye terrain budget releases wider previous leases before deferring untouched native sources");
        deferred.forceRenderingOff=true;
        Check(DuringRender(camera,()=>deferred.forceRenderingOff),
            "foreign mask on a deferred source survives the following capped terrain eye");
        Check(deferred.forceRenderingOff,"post-render does not recover a deferred foreign native mask");
        deferred.forceRenderingOff=false;
        // Same-count replace, rename and live source edits occur between actual eyes.
        var edited=clones.First(renderer=>chosen.Contains(renderer.GetInstanceID()));
        var script=edited.gameObject.AddComponent<CInteractable>(); Render(camera);
        Check(!DuringRender(camera,()=>edited.forceRenderingOff),"new native component between eyes revokes capped terrain admission");
        Object.DestroyImmediate(script); edited.gameObject.AddComponent<Animator>();
        Check(!DuringRender(camera,()=>edited.forceRenderingOff),"same-count native component replacement between eyes remains freshly guarded");
        Object.DestroyImmediate(edited.GetComponent<Animator>());
        edited.transform.name="Preview";
        Check(!DuringRender(camera,()=>edited.forceRenderingOff),"current source rename cannot reuse an old capped native scope");
        edited.transform.name="Body";
        TerrainWriteObserver.MaterialReads=0; PerfConfig.TerrainCameraSourceLimit=0;
        Check(DuringRender(camera,()=>clones.TrueForAll(renderer=>renderer.forceRenderingOff)&&original.forceRenderingOff),
            "terrain cap zero restores unlimited eligible private submissions live");
        Check(TerrainWriteObserver.MaterialReads==96,"unlimited terrain fallback executes all original native material reads");
        TerrainReadObserver.Reset(); Render(camera);
        Check(TerrainReadObserver.PropertyGuards==96&&TerrainReadObserver.PropertyReads==0
            &&TerrainReadObserver.EffectReads==0&&TerrainReadObserver.PropertyWrites==0,
            "ninety-six empty native terrain blocks retain per-eye guards without repeated copies or private writes");
        SharedWorldMaterialReads(host,original,camera,clones);
        PropOwnershipPasses(host, original, camera, clones);
        edited.transform.localPosition=new Vector3(100,0,0); TerrainWriteObserver.MaterialReads=0;
        Check(!DuringRender(camera,()=>edited.forceRenderingOff)&&TerrainWriteObserver.MaterialReads==95,
            "actual current camera frustum rejects offscreen substitute work while preserving native source");
        Plane[] authored=GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix);
        Matrix4x4 eyeProjection=camera.projectionMatrix;
        Plane[] shifted=GeometryUtility.CalculateFrustumPlanes(eyeProjection*Matrix4x4.TRS(new Vector3(-100,0,0),Quaternion.identity,Vector3.one)*camera.worldToCameraMatrix);
        var union=typeof(ScenarioTerrainBudget).GetMethod("OutsideBounds",BindingFlags.NonPublic|BindingFlags.Static)!;
        Check(!GeometryUtility.TestPlanesAABB(authored,edited.bounds)&&GeometryUtility.TestPlanesAABB(shifted,edited.bounds),
            "stereo union fixture really places current native source in one alternate eye frustum");
        Check(!(bool)union.Invoke(null,new object[]{edited.bounds,authored,authored,shifted,true})!,
            "terrain union retains a source visible exclusively to the second eye");
        Matrix4x4 oldCulling=camera.cullingMatrix; camera.cullingMatrix=Matrix4x4.zero;
        Check(DuringRender(camera,()=>edited.forceRenderingOff),"invalid native camera planes preserve full original terrain admission");
        camera.cullingMatrix=oldCulling;
        PerfConfig.SharedEnvironmentMaterialReadsOn=false; TerrainWriteObserver.MaterialReads=0;
        Check(DuringRender(camera,()=>edited.forceRenderingOff)&&TerrainWriteObserver.MaterialReads==96,
            "shared-read off restores the complete legacy terrain admission path");
        PerfConfig.SharedEnvironmentMaterialReadsOn=false; PerfConfig.TerrainCameraSourceLimit=0;
        foreach(var clone in clones)Object.DestroyImmediate(clone.gameObject);
        Tick(host);
        Debug.Log("Terrain source-bound scaling: capped material reads="+cappedReads+"/96; native source output retained.");
    }
    private static void SharedWorldMaterialReads(GameObject host,MeshRenderer original,Camera camera,List<MeshRenderer> clones)
    {
        Material native=original.sharedMaterial;
        var variant=new Material(native) {name="Fixture.SharedWorldVariant"};
        var alias=new Material(native) {name="Fixture.SharedCanonicalAlias"};
        int canonicalReads=0,variantReads=0,modeReads=0;
        bool allowed=true;
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(material=>
        { canonicalReads++;return material==alias?native:material; });
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material=>
        { variantReads++; variant.CopyPropertiesFromMaterial(material); return allowed?variant:material; },
            ()=>new MaterialPass(),()=>{modeReads++;return true;},material=>material==variant);
        try
        {
            original.sharedMaterial=alias;
            foreach(MeshRenderer clone in clones)clone.sharedMaterial=alias;
            Check(DuringRender(camera,()=>original.forceRenderingOff&&clones.TrueForAll(clone=>clone.forceRenderingOff)),
                "shared world terrain variants preserve every admitted native source with an exact original alias");
            Check(canonicalReads==1&&variantReads==1&&modeReads==1&&MaterialPass.Open==0,
                "ninety-six repeated terrain sources resolve canonical world material and variant exactly once within one camera pass");
            Debug.Log("Terrain world source-bound reads (96 repeated sources, one camera): original="+canonicalReads+", factory="+variantReads+", mode="+modeReads+".");
            canonicalReads=variantReads=modeReads=0;
            Color previous=native.GetColor("_Tint"); native.SetColor("_Tint",Color.cyan);
            Check(DuringRender(camera,()=>Proxies(host).FindAll(proxy=>proxy.enabled)
                .TrueForAll(proxy=>proxy.sharedMaterial==variant&&proxy.sharedMaterial.GetColor("_Tint")==Color.cyan))
                &&canonicalReads==1&&variantReads==1,
                "shared terrain material maps expire before the next actual eye and copy its changed native artwork");
            native.SetColor("_Tint",previous);
            allowed=false; canonicalReads=variantReads=0;
            Check(DuringRender(camera,()=>!original.forceRenderingOff&&clones.TrueForAll(clone=>!clone.forceRenderingOff))
                &&canonicalReads==1&&variantReads==1,
                "changed world variant refusal between actual eyes keeps every repeated terrain source native");
            allowed=true;
            // The public alias is stable, but its current canonical original
            // changes to an unsupported native queue at the next actual eye.
            alias.renderQueue=3000;
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material=>alias);
            Check(!DuringRender(camera,()=>original.forceRenderingOff),
                "changed canonical native material mapping between actual eyes revokes the old world variant route");
            alias.renderQueue=native.renderQueue;
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material=>
            { canonicalReads++;return material==alias?native:material; });
            PerfConfig.SharedEnvironmentMaterialReadsOn=false; canonicalReads=variantReads=modeReads=0;
            Check(DuringRender(camera,()=>original.forceRenderingOff&&clones.TrueForAll(clone=>clone.forceRenderingOff))
                &&canonicalReads==192&&variantReads==96&&modeReads==1,
                "shared-read off restores repeated canonical and world factory calls for the complete legacy terrain path");
        }
        finally
        {
            ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material=>material,()=>new MaterialPass(),()=>false,_=>false);
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material=>material);
            original.sharedMaterial=native;
            foreach(MeshRenderer clone in clones)clone.sharedMaterial=native;
            ScenarioTerrainBudget.BeforeNativeContentChange();
            PerfConfig.SharedEnvironmentMaterialReadsOn=true;
            Object.DestroyImmediate(variant); Object.DestroyImmediate(alias);
        }
    }
    private static void PropertyBridgeChannels(GameObject host,GameObject scenario,Material material)
    {
        // Floors are intentionally never admitted by the production owner. Directly
        // execute the shared proxy helper to pin its compatibility contract for the
        // world and legacy shaders, without broadening actual floor admission.
        var floor=Surface(scenario,"CV_Floor_Basic",new Vector3(12,0,0),material,false);
        Type type=typeof(ScenarioTerrainBudget).GetNestedType("Surface",BindingFlags.NonPublic)!;
        object surface=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,
            new object[]{floor,floor.GetComponent<MeshFilter>(),host.transform},null)!;
        try
        {
            type.GetMethod("EnsureSlots",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[]{1});
            type.GetMethod("SetMaterial",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[]{0,material});
            var native=new MaterialPropertyBlock(); native.SetColor("_Tint",Color.cyan); floor.SetPropertyBlock(native,0);
            Check((bool)type.GetMethod("PrepareProxy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,
                new object[]{false,default(Matrix4x4),default(Vector3)})!,"private floor helper accepts unchanged native property blocks");
            var proxy=(MeshRenderer)type.GetField("_proxyRenderer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(surface)!;
            var read=new MaterialPropertyBlock(); proxy.GetPropertyBlock(read);
            Check(read.GetFloat("_GHVRTerrainNeverFade")==1&&read.GetFloat("_GHVRWorldNeverFade")==1,
                "private floor renderer block sets legacy and world never-fade channels together");
            proxy.GetPropertyBlock(read,0);
            Check(read.GetColor("_Tint")==Color.cyan&&read.GetFloat("_GHVRTerrainNeverFade")==1&&read.GetFloat("_GHVRWorldNeverFade")==1,
                "private floor slot block preserves native color and both never-fade channels");
        }
        finally
        {
            var privateObject=(GameObject)type.GetField("_proxy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(surface)!;
            type.GetMethod("Dispose",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,null);
            Object.DestroyImmediate(privateObject); floor.gameObject.SetActive(false);
        }
    }
    private static void ShaderPixels(Camera camera)
    {
        // Full native scene/controllers/art are outside this fixture. These rendered
        // shader samples exercise the production fragment and original MPB bindings.
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh testMesh=cube.GetComponent<MeshFilter>().mesh; Vector3[] testVertices=testMesh.vertices;
        for(int i=0;i<testVertices.Length;i++)testVertices[i]+=Vector3.up;
        testMesh.vertices=testVertices; testMesh.RecalculateBounds();
        var mat=new Material(Shader.Find("GloomhavenVR/ScenarioCheapTerrain")); cube.GetComponent<MeshRenderer>().sharedMaterial=mat;
        mat.SetColor("_Tint",Color.white); mat.SetFloat("_GHVRTerrainNativeRoute",1);
        camera.transform.position=new Vector3(0,1,-3); camera.transform.LookAt(new Vector3(0,1,0));
        var map=new Texture2D(8,8,TextureFormat.RGBAFloat,false,true); map.filterMode=FilterMode.Bilinear;
        Color[] pixels=new Color[64]; for(int y=0;y<8;y++)for(int x=0;x<8;x++)pixels[y*8+x]=new Color(.06f+.94f*x/7f,0,0,0); map.SetPixels(pixels); map.Apply();
        var block=new MaterialPropertyBlock(); block.SetTexture("_TilesOcclusionMap",map); block.SetInteger("ToggleWallFade",1); block.SetFloat("_EnableOcclusionMap",1);
        int previous=int.MaxValue,changes=0;
        for(int step=0;step<=10;step++)
        {
            block.SetFloat("_Cutoff",Mathf.Lerp(-.15f,1f,step/10f)); cube.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            Color[] rendered=Pixels(camera); int visible=Visible(rendered); Debug.Log("Terrain shader cutoff="+step+" pixels="+visible+" center="+rendered[48*96+48]);
            if(step==0||step==10)
            {
                var image=new Texture2D(96,96,TextureFormat.RGBA32,false); image.SetPixels(rendered); image.Apply();
                File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("GHVR_TERRAIN_EVIDENCE"),"shader-"+step+".png"),image.EncodeToPNG()); Object.DestroyImmediate(image);
            }
            Check(visible<=previous,"original bilinear LOW cutoff sequence is continuous and monotone"); if(visible!=previous)changes++; previous=visible;
        }
        Check(changes>=4,"production native wall map has multiple visible intermediate frames");
        block.SetFloat("_GHVRTerrainNeverFade",1f); cube.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        Color center=Pixels(camera)[48*96+48];
        Check(center.r+center.g+center.b>.05f,"never-fade floor ignores native wall channel at fully faded endpoint");
        var cheaperHigh=new Material(Shader.Find("Fixture/TerrainCheapHighBoundary"));
        var originalHigh=new Material(Shader.Find("Fixture/NativeHighWall"));
        cheaperHigh.SetColor("_Tint",Color.white); originalHigh.SetColor("_Tint",Color.white);
        cheaperHigh.SetFloat("_NativeNoise",.003f); originalHigh.SetFloat("_NativeNoise",.003f);
        block.SetFloat("_GHVRTerrainNeverFade",0);
        foreach(int route in new[]{2,3})
        {
            cheaperHigh.SetFloat("_GHVRTerrainNativeRoute",route); originalHigh.SetFloat("_NativeToggleVariant",route==3?1f:0f);
            for(int step=0;step<=10;step++)
            {
                block.SetFloat("_Cutoff",Mathf.Lerp(-.15f,1f,step/10f)); cube.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                cube.GetComponent<MeshRenderer>().sharedMaterial=cheaperHigh; Color[] actual=Pixels(camera);
                cube.GetComponent<MeshRenderer>().sharedMaterial=originalHigh; Color[] expected=Pixels(camera);
                bool same=true; for(int pixel=0;pixel<actual.Length;pixel++)
                    if((actual[pixel].r+actual[pixel].g+actual[pixel].b>.05f)!=(expected[pixel].r+expected[pixel].g+expected[pixel].b>.05f))same=false;
                Check(same,"production HIGH and toggle-native map foundation vignette cutoff retain original fragment coverage");
            }
        }
        block.SetInteger("ToggleWallFade",0);
        foreach(int route in new[]{2,3})
        {
            cheaperHigh.SetFloat("_GHVRTerrainNativeRoute",route); originalHigh.SetFloat("_NativeToggleVariant",route==3?1f:0f);
            foreach(float cutoff in new[]{-.15f,.5f,1f,1.2f})
            {
                block.SetFloat("_Cutoff",cutoff); cube.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                cube.GetComponent<MeshRenderer>().sharedMaterial=cheaperHigh; Color[] actual=Pixels(camera);
                cube.GetComponent<MeshRenderer>().sharedMaterial=originalHigh; Color[] expected=Pixels(camera);
                Check(actual.Select(pixel=>pixel.r+pixel.g+pixel.b>.05f).SequenceEqual(expected.Select(pixel=>pixel.r+pixel.g+pixel.b>.05f)),
                    "inactive HIGH and toggle-native clip retain original authored cutoff including above one");
            }
        }
        mat.SetFloat("_GHVRTerrainNativeRoute",1f); block.SetFloat("_Cutoff",1.2f);
        cube.GetComponent<MeshRenderer>().sharedMaterial=mat; cube.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        Check(Visible(Pixels(camera))>0,"inactive original LOW route never clips even for authored cutoff above one");
        Object.DestroyImmediate(cheaperHigh); Object.DestroyImmediate(originalHigh);
        Object.DestroyImmediate(map); Object.DestroyImmediate(testMesh); Object.DestroyImmediate(cube); Object.DestroyImmediate(mat);
    }
    private static void NoisePixels(Camera camera)
    {
        Check(NoiseSamples.Data.Length==20,"all twenty independent native bytecode noise vectors execute");
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.position=new Vector3(0,1,0);
        var material=new Material(Shader.Find("Fixture/TerrainNativeNoise")); cube.GetComponent<MeshRenderer>().sharedMaterial=material;
        foreach(NoiseSamples.Sample vector in NoiseSamples.Data)
        {
            material.SetVector("_SamplePosition",new Vector4(vector.Position.x,vector.Position.y,vector.Position.z,0));
            Color pixel=Pixels(camera)[48*96+48]; float actual=(pixel.r-.5f)*2f;
            Check(Mathf.Abs(actual-vector.Value)<.002f,"production simplex matches original native DXBC instruction samples");
        }
        Object.DestroyImmediate(cube); Object.DestroyImmediate(material);
    }
}
