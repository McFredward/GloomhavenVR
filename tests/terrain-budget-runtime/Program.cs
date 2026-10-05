using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class TerrainProgram
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
        Check(Visible(Pixels(camera))>100,"cheap terrain submits actual nonempty camera pixels");
        Check(!wall.forceRenderingOff&&!floor.forceRenderingOff&&Proxies(host).TrueForAll(r=>!r.enabled),"post-render restores every native source and disables private proxies");
        Check(wall.sharedMaterials[0]==slots[0]&&wall.GetComponent<MeshFilter>().sharedMesh==original&&collider.sharedMesh==original,"native source mesh materials and collision are untouched");
        var clone=Object.Instantiate(wall.gameObject); Check(clone.GetComponent<MeshRenderer>().sharedMaterials.Length==1&&clone.GetComponent<MeshRenderer>().sharedMaterial==material,"native clone retains nonempty original material slots");
        Check(clone.transform.childCount==0,"native clone inherits no private proxy"); Object.DestroyImmediate(clone);
        Check(wall.GetComponent<MeshRenderer>().isPartOfStaticBatch==false,"native source never acquires internal static batch state");

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
        bool leased=false,restored=false;
        Camera.CameraCallback inspect=cam=>
        {
            if(cam!=camera)return;
            leased=wall.forceRenderingOff;
            ScenarioTerrainBudget.BeforeNativeRendererWrite(wall);
            restored=!wall.forceRenderingOff&&Proxies(host).Find(r=>r.transform.position==wall.transform.position)!.enabled==false;
        };
        Camera.onPreCull+=inspect; Render(camera); Camera.onPreCull-=inspect;
        Check(leased&&restored,"native pre-cull write synchronously revokes an already prepared proxy");
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
        GloomhavenVR.Board.FigureGrab.HeldProps.Held=true; Pixels(camera);
        Check(Proxies(host).TrueForAll(r=>!r.enabled),"held-source veto retains native rendering"); GloomhavenVR.Board.FigureGrab.HeldProps.Held=false;
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
        var faultWall=Surface(scenario,"CV_Wall_Generic_02",new Vector3(-1,1,0),material);
        BundleShaders.Throw=true; Component oldDriver=Driver(host); ScenarioTerrainBudget.Shutdown(); Object.DestroyImmediate(oldDriver);
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Tick(host); Render(camera);
        Check(VRLog.Faults.Exists(text=>text.Contains("fixture shader resolver fault"))&&!faultWall.forceRenderingOff&&!floor.forceRenderingOff,"shader failure restores native rendering without escaping continuation");
        ScenarioTerrainBudget.Shutdown(); BundleShaders.Throw=false;
        ShaderPixels(camera);
        NoisePixels(camera);
        Check(VRLog.Faults.FindAll(text=>text.Contains("presentation failed")).Count==1,"optional failure reports once with useful normal-level context");
        Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(camera.targetTexture); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(material); DisposeBank();
        return _checks;
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
