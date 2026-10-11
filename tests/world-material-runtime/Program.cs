using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using Props=GloomhavenVR.Board.FigureGrab.PropGrab;
using LocalProps=GloomhavenVR.Board.FigureGrab.HeldProps;
using RemoteProps=GloomhavenVR.Board.FigureGrab.NetHeldProps;

public static partial class WorldMaterialProgram
{
    private static int _checks;
    public static int Assertions => _checks;
    private static void Check(bool value,string message){_checks++;if(!value)throw new Exception(message);}
    private static void Tick(GameObject host,int times=1)
    {
        Component driver=host.GetComponents<Component>().First(c=>c.GetType().Name=="Driver");
        MethodInfo update=driver.GetType().GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance)!;
        for(int i=0;i<times;i++)update.Invoke(driver,null);
    }
    private static MeshRenderer Source(string name,Transform parent,Mesh mesh,params Material[] slots)
    {
        GameObject go=new(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=slots;return renderer;
    }
    private static Color Center(Camera camera)
    {
        camera.Render();var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        RenderTexture old=RenderTexture.active;RenderTexture.active=camera.targetTexture;
        image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();Color pixel=image.GetPixel(32,32);
        RenderTexture.active=old;Object.DestroyImmediate(image);return pixel;
    }
    private static Material Native(string name,string shader="Amp_Basic_N_MRAO")
    {
        var material=new Material(Shader.Find(shader)){name=name};
        material.SetColor("_Tint",Color.red);material.SetFloat("_Cutoff",0f);return material;
    }
    public static int Run()
    {
        _checks=0;var host=new GameObject("Budget host");var scenario=new GameObject("Native scenario");
        scenario.AddComponent<ProceduralScenario>();var generated=new GameObject("Generated Content");generated.transform.SetParent(scenario.transform,false);
        var room=new GameObject("Room A");room.transform.SetParent(generated.transform,false);room.AddComponent<ProceduralMapTile>();
        // Dynamically loaded model assemblies do not undergo Unity's script importer;
        // establish the exact native RequireComponent tuple explicitly as the boundary.
        if(room.GetComponent<ProceduralStyle>()==null)room.AddComponent<ProceduralStyle>();
        if(room.GetComponent<ApparanceEntity>()==null)room.AddComponent<ApparanceEntity>();
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);Mesh originalMesh=cube.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(cube);
        var first=Native("First floor");var second=Native("Other masonry");second.SetColor("_Tint",Color.blue);
        var source=Source("Native floor",room.transform,originalMesh,first);
        var cameraGo=new GameObject("Actual camera");var camera=cameraGo.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;
        camera.targetTexture=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat);camera.targetTexture.Create();
        int notifications=0;WorldMaterialBudget.ConfigureSourceChanged(renderer=>{notifications++;WorldMaterialBudget.MaterialReady(renderer);});
        float ambient=.65f;WorldMaterialBudget.ConfigureAmbientWeight(()=>ambient);
        WorldMaterialBudget.Install(host);Tick(host,16);WorldMaterialBudget.MaterialReady(source);
        Color shown=Center(camera);Material variant=source.sharedMaterial;
        Check(WorldMaterialBudget.IsOwnedVariant(variant)&&variant!=first,"requested world stage produces a private variant on actual native renderer");
        Check(source.GetComponent<MeshFilter>().sharedMesh==originalMesh&&source.enabled&&!source.forceRenderingOff,"world stage preserves original geometry and visible room renderer");
        Check(first.shader.name=="Amp_Basic_N_MRAO"&&WorldMaterialBudget.CanonicalMaterial(variant)==first,"private world shader never mutates original native material ownership");
        Check(variant.GetFloat("_GHVRWorldMaterialMode")==2&&variant.GetFloat("_GHVRWorldNativeRoute")==1,"actual private material binds independently requested mode and audited HIGH route");
        Check(Mathf.Approximately(variant.GetFloat("_GHVRWorldAmbientWeight"),.65f),"actual private material binds independently requested ambient weight");
        Check(variant.GetTag("RenderType",false,"")==first.GetTag("RenderType",false,"")&&variant.renderQueue==first.renderQueue,"native replacement-camera render type and queue survive private shader selection");
        Check(shown.r>.7f&&shown.g<.1f,"actual camera renders selected private world slot with original native tint");
        NativeWriteObserver.ArrayWrites=0;int previousNotifications=notifications;Center(camera);
        Check(NativeWriteObserver.ArrayWrites==0&&notifications==previousNotifications,"settled world slots neither rewrite native arrays nor loop source notifications");
        // Current property state and local keyword identity refresh in a genuine later camera.
        first.SetColor("_Tint",Color.green);first.EnableKeyword("_WALLFADE_ON_ON");shown=Center(camera);
        Check(shown.g>.7f&&variant.IsKeywordEnabled("_WALLFADE_ON_ON"),"between-eye native in-place material edits and exact keywords reach current variant pixels");
        ambient=.2f;Center(camera);
        Check(Mathf.Approximately(variant.GetFloat("_GHVRWorldAmbientWeight"),.2f),"between-eye ambient weight changes reach the current private variant");
        ambient=float.NaN;Center(camera);
        Check(variant.GetFloat("_GHVRWorldAmbientWeight")==1f,"invalid ambient input falls back to native ambient contribution");
        ambient=-1f;Center(camera);Check(variant.GetFloat("_GHVRWorldAmbientWeight")==0f,"ambient contribution clamps a negative external input");
        ambient=2f;Center(camera);Check(variant.GetFloat("_GHVRWorldAmbientWeight")==1f,"ambient contribution clamps an excessive external input");
        WorldMaterialBudget.ConfigureAmbientWeight(()=>1f);
        first.DisableKeyword("_WALLFADE_ON_ON");
        var block=new MaterialPropertyBlock();block.SetColor("_Tint",Color.blue);source.SetPropertyBlock(block);
        shown=Center(camera);Check(shown.b>.7f&&shown.g<.1f,"native renderer-wide MPB overrides remain attached to actual original renderer");
        block.Clear();source.SetPropertyBlock(block);
        source.sharedMaterials=new[]{first,second};Center(camera);
        var read=new MaterialPropertyBlock();block.SetColor("_Tint",Color.yellow);source.SetPropertyBlock(block,1);Center(camera);source.GetPropertyBlock(read,1);
        Check(source.sharedMaterials.Length==2&&read.GetColor("_Tint")==Color.yellow&&WorldMaterialBudget.CanonicalMaterial(source.sharedMaterials[1])==second,"native material subslot count order and independent MPB values are preserved");
        block.Clear();source.SetPropertyBlock(block,1);
        var foreign=Native("Foreign slot","Fixture/UnreviewedWorld");Material ownedSlot=source.sharedMaterials[1];
        source.sharedMaterials=new[]{foreign,ownedSlot};WorldMaterialBudget.BeforeNativeRendererWrite(source);
        Check(source.sharedMaterials[0]==foreign&&source.sharedMaterials[1]==second,"conditional restoration preserves same-count foreign slot replacement and restores only owned slot");
        source.sharedMaterials=new[]{first,second};Center(camera);
        WorldMaterialBudget.BeforeNativeContentChange();var clone=Object.Instantiate(room);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second})&&clone.GetComponentInChildren<MeshRenderer>().sharedMaterials.SequenceEqual(new[]{first,second}),"native content boundary restores original slot composition before real Unity cloning");
        Object.DestroyImmediate(clone);
        Center(camera);var inherited=Object.Instantiate(room);PerfConfig.WorldMaterialQualityMode=0;Tick(host);
        Check(inherited.GetComponentInChildren<MeshRenderer>().sharedMaterials.SequenceEqual(new[]{first,second}),"complete Off also repairs unregistered inherited variant references");
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"world stage zero immediately restores complete native original material slots");
        Object.DestroyImmediate(inherited);PerfConfig.WorldMaterialQualityMode=1;Tick(host,16);Center(camera);
        Check(source.sharedMaterial.GetFloat("_GHVRWorldMaterialMode")==1,"simple lighting stage is independent and applies live");
        PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        var interactive=source.gameObject.AddComponent<CInteractable>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"current added native interaction between eyes revokes world material ownership");
        Object.DestroyImmediate(interactive);var animator=source.gameObject.AddComponent<Animator>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"same-count native component replacement stays authoritative between eyes");
        Object.DestroyImmediate(animator);Center(camera);
        var ui=new GameObject("Native UI");ui.AddComponent<Canvas>();source.transform.SetParent(ui.transform,false);Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"late native reparent into UI restores original materials before rendering");
        source.transform.SetParent(room.transform,false);Object.DestroyImmediate(ui);Center(camera);
        source.enabled=false;Center(camera);Check(!source.enabled&&source.sharedMaterial==first,"native hidden room renderer remains disabled with original slots");
        source.enabled=true;room.SetActive(false);Center(camera);Check(!room.activeSelf&&source.sharedMaterial==first,"closed native room remains inactive with no visibility intervention");
        room.SetActive(true);Center(camera);
        var style=room.GetComponent<ProceduralStyle>();style.AnimateStyle=true;Center(camera);
        Check(source.sharedMaterial==first,"live native animated style retains original rendering while required static tile generators are admitted");
        style.AnimateStyle=false;Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"actual tile required generator and static style boundary regains world material ownership");
        GloomhavenVR.Board.FigureGrab.HeldProps.Visuals.Add(source.gameObject);Center(camera);
        Check(source.sharedMaterial==first,"current held native world prop retains original material ownership");GloomhavenVR.Board.FigureGrab.HeldProps.Visuals.Clear();Center(camera);
        Object.DestroyImmediate(source.GetComponent<MeshFilter>());Center(camera);
        Check(source.sharedMaterial==first,"missing current native mesh filter restores world-owned references before culling");
        source.gameObject.AddComponent<MeshFilter>().sharedMesh=originalMesh;Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"replacement native mesh filter is read again and can regain safe world shading between eyes");
        MeshFilter previousFilter=source.GetComponent<MeshFilter>();Object.DestroyImmediate(previousFilter);
        MeshFilter replacement=source.gameObject.AddComponent<MeshFilter>();replacement.sharedMesh=originalMesh;Center(camera);
        Check(replacement!=previousFilter&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"same-count native MeshFilter replacement without intermediate render is authoritative");
        int meshNotifications=notifications;var changedMesh=Object.Instantiate(originalMesh);replacement.sharedMesh=changedMesh;Center(camera);
        Check(source.GetComponent<MeshFilter>().sharedMesh==changedMesh&&notifications>meshNotifications,"live native mesh-reference swap notifies earlier consumers without changing original geometry");replacement.sharedMesh=originalMesh;Center(camera);Object.DestroyImmediate(changedMesh);
        source.forceRenderingOff=true;Center(camera);Check(source.forceRenderingOff&&source.sharedMaterial==first,"foreign native source mask refuses material ownership without clearing mask");
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>renderer==source);Center(camera);
        Check(source.forceRenderingOff&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"known authorized render substitute mask remains eligible for live world shading");
        source.forceRenderingOff=false;WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>false);
        foreach(string effect in new[]{"_AddVertexAnim","_UseEmissiveMap","_EmissionMap","_AdvancedEmission","_MossTexture_ON"})
        {
            first.SetFloat(effect,1f);Center(camera);Check(source.sharedMaterial==first,"live original effect retains native material family: "+effect);first.SetFloat(effect,0f);Center(camera);
        }
        block.SetFloat("_AddVertexAnim",1f);source.SetPropertyBlock(block,1);Center(camera);
        Check(source.sharedMaterials[1]==second&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0]),"live native effect veto is per material subslot and preserves independent safe slot");
        source.SetPropertyBlock(null,1);Center(camera);
        foreach(string pass in new[]{"FORWARD","ShadowCaster","CUSTOM_SHADOW_PASS"})
        {
            first.SetShaderPassEnabled(pass,false);Center(camera);Check(source.sharedMaterial==first,"live disabled native material pass remains original: "+pass);
            first.SetShaderPassEnabled(pass,true);Center(camera);Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"native pass re-enable regains safe world shading: "+pass);
        }
        var dynamicTexture=new RenderTexture(1,1,0);dynamicTexture.Create();block.Clear();block.SetTexture("_MainTex",dynamicTexture);source.SetPropertyBlock(block,1);Center(camera);
        Check(source.sharedMaterials[1]==second&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0]),"current per-slot native video/render texture remains original while unrelated static slot is simplified");source.SetPropertyBlock(null,1);Object.DestroyImmediate(dynamicTexture);Center(camera);
        first.EnableKeyword("_WORLDSPACE_ON");first.EnableKeyword("_DIFUSE_ALPHA_ON_ON");Center(camera);
        Check(source.sharedMaterial==first,"unproven native worldspace alpha program combination retains original shader");
        first.DisableKeyword("_WORLDSPACE_ON");first.DisableKeyword("_DIFUSE_ALPHA_ON_ON");
        var unknown=source.gameObject.AddComponent<UnknownNativeAnimation>();Center(camera);
        Check(source.sharedMaterial==first,"unknown scripted animation remains native despite positive world ancestry");Object.DestroyImmediate(unknown);
        Families(source,first,second,camera);
        EmptyBlockParity(source,first,second,camera);
        CurrentOwnerParity(room,source,first,second,camera);
        NativeScenarioScopes(host,room,source,first,second,camera);
        MapScopes(host,originalMesh,first,camera);
        Scale(host,room,source,first,second,camera);
        PropGuards(host,room,source,first,second,camera);
        LateChanges(host,source,first,second,camera);
        RenewedRefusals(host,source,first,second,camera);
        CameraMasks(host,room,source,first,second,camera);
        PerformanceWalls(host,room,source,first,second,camera);
        bool prepared=false;WorldMaterialBudget.ConfigureAssetPreparation(()=>{prepared=true;return false;});Center(camera);
        Check(prepared&&source.sharedMaterial==first,"cold asset preparation refusal keeps native source materials valid");WorldMaterialBudget.ConfigureAssetPreparation(()=>true);
        WorldMaterialBudget.BeforeNativeRendererWrite(source);BundleShaders.Missing=true;
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        Check(source.sharedMaterial==first,"unavailable shader retains valid native original fallback");BundleShaders.Missing=false;
        // An explicit option transition retries asset resolution after unavailable content.
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        VRSession.IsRunning=false;Tick(host);Check(source.sharedMaterial==first,"VR teardown boundary restores native shader and source slots");VRSession.IsRunning=true;
        PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        Material retained=WorldMaterialBudget.VariantFor(first);
        WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>throw new InvalidOperationException("fixture consumer refusal"));
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);
        Check(retained!=null&&source.sharedMaterial==first&&VRLog.Messages.Any(m=>m.Contains("consumer disposal failed")),"consumer cleanup exception retains live private materials and restores native slots instead of destroying consumer references");
        WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{});
        WorldMaterialBudget.Shutdown();VRSession.Harmony.UnpatchSelf();
        Object.DestroyImmediate(source.gameObject);Object.DestroyImmediate(scenario);Object.DestroyImmediate(host);
        Object.DestroyImmediate(camera.targetTexture);Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(first);Object.DestroyImmediate(second);Object.DestroyImmediate(foreign);
        WallAttachmentParity();
        return _checks;
    }
    public static IEnumerator RunAsync()
    {
        Run();
        // Run completed a plugin owner's teardown. A fresh owner exercises the
        // production boundary's real reinstall contract in the same managed domain.
        VRSession.Harmony=new HarmonyLib.Harmony("world.material.lifecycle."+typeof(VRSession).Assembly.GetName().Name);
        var host=new GameObject("World lifecycle host");
        Scene retained=SceneManager.CreateScene("WorldMaterialRetained");
        var scenario=new GameObject("Retained native scenario");SceneManager.MoveGameObjectToScene(scenario,retained);
        scenario.AddComponent<ProceduralScenario>();
        var generated=new GameObject("Generated Content");generated.transform.SetParent(scenario.transform,false);
        scenario.AddComponent<ProceduralPlacementNotifierHandler>();scenario.AddComponent<LightShadowsModifierController>();
        scenario.AddComponent<ApparanceMap>();scenario.AddComponent<ProceduralMapConfig>();
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);Mesh mesh=cube.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(cube);
        var original=Native("Retained original");var source=Source("Retained masonry",generated.transform,mesh,original);
        var cameraGo=new GameObject("Retained camera");var camera=cameraGo.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;
        camera.targetTexture=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat);camera.targetTexture.Create();
        WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{});
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>WorldMaterialBudget.MaterialReady(renderer));
        WorldMaterialBudget.ConfigureCanonicalSource(material=>material);
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(_=>false);
        WorldMaterialBudget.ConfigureAssetPreparation(()=>true);
        PerfConfig.WorldMaterialQualityMode=0;WorldMaterialBudget.Install(host);Tick(host);
        try
        {
            NativeWriteObserver.MaterialReads=0;NativeWriteObserver.MapInventories=0;
            IDisposable firstPass=WorldMaterialBudget.BeginMaterialReadPass();firstPass.Dispose();
            for(int i=0;i<64;i++)
            {
                using IDisposable pass=WorldMaterialBudget.BeginMaterialReadPass();
                Check(ReferenceEquals(pass,firstPass),"settled Off reuses a no-op read pass without allocating owned pass objects");
                WorldMaterialBudget.BeforeNativeRendererWrite(source);
            }
            Check(NativeWriteObserver.MaterialReads==0,"settled Off performs no native renderer slot reads");
            yield return SceneManager.LoadSceneAsync("WorldMaterialUnrelated",LoadSceneMode.Additive);
            Scene unrelated=SceneManager.GetSceneByName("WorldMaterialUnrelated");
            Check(unrelated.IsValid()&&unrelated.isLoaded,"actual Unity additive scene load completes for Off discovery boundary");
            Check(NativeWriteObserver.MapInventories==0,"settled Off additive scene loading performs no scene material inventory");
            yield return SceneManager.UnloadSceneAsync(unrelated);
            Check(retained.isLoaded&&source!=null&&source.sharedMaterial==original,"Off additive unload preserves retained native scenery");
            PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source!.sharedMaterial),"retained scene adopts the requested material stage before unrelated unload");
            yield return SceneManager.LoadSceneAsync("WorldMaterialUnrelated",LoadSceneMode.Additive);
            unrelated=SceneManager.GetSceneByName("WorldMaterialUnrelated");
            Check(unrelated.isLoaded&&retained.isLoaded,"actual Unity additive scenes coexist before native unload");
            yield return SceneManager.UnloadSceneAsync(unrelated);
            Check(!unrelated.isLoaded&&retained.isLoaded&&source!=null,"actual unrelated scene unload retains the native source scene");
            Tick(host,16);Color shown=Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source!.sharedMaterial)&&shown.r>.7f,
                "unrelated additive unload reseeds retained scenery and renders its requested private stage");
            Check(source.GetComponent<MeshFilter>().sharedMesh==mesh&&source.enabled&&!source.forceRenderingOff,
                "retained-scene recovery leaves native geometry and visibility authoritative");
            PerfConfig.WorldMaterialQualityMode=0;
            using(IDisposable pending=WorldMaterialBudget.BeginMaterialReadPass())
                Check(!ReferenceEquals(pending,firstPass),"pending Off retains an owned read pass while existing variant consumers need restoration");
            WorldMaterialBudget.BeforeNativeRendererWrite(source);
            Check(source.sharedMaterial==original,"pending Off restores existing private source references before native writes");
            Tick(host);
            NativeWriteObserver.MaterialReads=0;
            using(IDisposable settled=WorldMaterialBudget.BeginMaterialReadPass())
                Check(ReferenceEquals(settled,firstPass),"completed Off transition returns to the allocation-free read pass");
            WorldMaterialBudget.BeforeNativeRendererWrite(source);
            Check(NativeWriteObserver.MaterialReads==0,"completed Off transition again avoids native slot reads");
        }
        finally
        {
            WorldMaterialBudget.Shutdown();
            Object.DestroyImmediate(camera.targetTexture);Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(host);
            Object.DestroyImmediate(scenario);Object.DestroyImmediate(original);
        }
        yield return SceneManager.UnloadSceneAsync(retained);
    }
    private static void Families(MeshRenderer source,Material first,Material second,Camera camera)
    {
        string[] names={"Amp_Basic_N_MRAO","Amp_Low/Amp_Basic_N_MRAO_Low","Amp_Basic_WallFade","Amp_Low/Amp_Basic_WallFade_Low","Amp_Basic","Amp_Low/Amp_Basic_Low"};
        int[][] allowed={new[]{0,2,6},new[]{0,24},new[]{0,32,1},new[]{0,8,32,40,1},new[]{0,1},new[]{0,8,1}};
        string[] keywords={"_WORLDSPACE_ON","_WALLFADE_ON_ON","_DIFUSE_ALPHA_ON_ON","_DESATURATION_ON","_TOGGLEWALLFADE_ON","_TOGGLEWALLFADEOFF_ON"};
        for(int route=0;route<names.Length;route++)
        {
            Material material=Native("Native family contract",names[route]);
            for(int mask=0;mask<64;mask++)
            {
                material.shaderKeywords=keywords.Where((k,bit)=>(mask&(1<<bit))!=0).ToArray();
                Material candidate=WorldMaterialBudget.VariantFor(material);bool accepted=allowed[route].Contains(mask);
                Check(WorldMaterialBudget.IsOwnedVariant(candidate)==accepted,"joint native program intersection is authoritative: route="+(route+1)+" mask="+mask);
                if(accepted)Check(candidate.GetFloat("_GHVRWorldNativeRoute")==route+1,"audited native family route is bound independently");
            }
            Object.DestroyImmediate(material);
        }
        Material standard=new(Shader.Find("Standard"));standard.SetFloat("_Mode",0f);standard.SetFloat("_SrcBlend",1f);standard.SetFloat("_DstBlend",0f);standard.SetFloat("_ZWrite",1f);standard.SetColor("_EmissionColor",new Color(0,0,0,1));standard.shaderKeywords=Array.Empty<string>();
        source.sharedMaterials=new[]{standard};Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial)&&source.sharedMaterial.GetFloat("_GHVRWorldNativeRoute")==9,"opaque Standard black RGB emission remains supported independently from alpha");
        var texture=new Texture2D(1,1);var block=new MaterialPropertyBlock();block.SetTexture("_EmissionMap",texture);source.SetPropertyBlock(block);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"Standard texture-valued MPB emission channel is never misread as AMP float switch");
        block.SetFloat("_Mode",1f);source.SetPropertyBlock(block);Center(camera);Check(source.sharedMaterial==standard,"native MPB blend mode override remains original rather than fixed opaque private pass");source.SetPropertyBlock(null);
        standard.EnableKeyword("_ALPHATEST_ON");Center(camera);Check(source.sharedMaterial==standard,"actual stripped native Standard alpha-test program is not invented by private shader");standard.DisableKeyword("_ALPHATEST_ON");
        standard.SetFloat("_Mode",1f);Center(camera);Check(source.sharedMaterial==standard,"unsupported actual native Standard cutout mode remains original");
        source.sharedMaterials=new[]{first,second};Center(camera);Object.DestroyImmediate(standard);Object.DestroyImmediate(texture);
    }
    private static void MapScopes(GameObject host,Mesh mesh,Material material,Camera camera)
    {
        var root=new GameObject("worldMap");var decor=Source("Native map decoration",root.transform,mesh,material);decor.transform.position=new Vector3(20,0,0);WorldMaterialBudget.MaterialReady(decor);Center(camera);
        Check(decor.sharedMaterial==material,"a generic worldMap name alone never establishes native world provenance");
        var map=new GameObject("Native map controller").AddComponent<MapChoreographer>();map.worldMap=root;
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(decor.sharedMaterial),"actual native MapChoreographer worldMap field establishes positive map decoration scope");
        var actor=decor.gameObject.AddComponent<ActorBehaviour>();Center(camera);Check(decor.sharedMaterial==material,"actor inside proven map producer root remains native");Object.DestroyImmediate(actor);
        var canvas=root.AddComponent<Canvas>();Center(camera);Check(decor.sharedMaterial==material,"UI subtree inside proven map producer root remains native");Object.DestroyImmediate(canvas);
        var extra=new GameObject("Native registered town scenery");var town=Source("Town static decoration",extra.transform,mesh,material);town.transform.position=new Vector3(-20,0,0);WorldMaterialBudget.RegisterWorldRoot(extra);Tick(host,4);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(town.sharedMaterial),"explicit actual producer root registration extends positive world scope without name allowlists");
        Object.DestroyImmediate(root);Object.DestroyImmediate(map.gameObject);Object.DestroyImmediate(extra);
    }
    private static void NativeScenarioScopes(GameObject host,GameObject room,MeshRenderer source,Material first,Material second,Camera camera)
    {
        GameObject scenario=room.transform.parent.parent.gameObject;
        // Exact authored ProcGen Maps root and all 129 shipped/editor map roots.
        // Original callbacks are explicit boundaries; native source-object hashes
        // and complete MonoBehaviour tuples live in native-world-scope.json.
        var placement=scenario.AddComponent<ProceduralPlacementNotifierHandler>();
        var shadows=scenario.AddComponent<LightShadowsModifierController>();
        var config=room.AddComponent<ProceduralMapConfig>();var map=room.AddComponent<ApparanceMap>();
        Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"exact original ProcGen root and native map coordinator tuple admits world shading independently of geometry substitutes");
        var actor=source.gameObject.AddComponent<ActorBehaviour>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"actor inside exact native coordinator tuple never inherits scenery shading");
        Object.DestroyImmediate(actor);var interaction=source.gameObject.AddComponent<CInteractable>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"interactive child inside exact native coordinator tuple never inherits scenery shading");
        Object.DestroyImmediate(interaction);Center(camera);
        var stranger=room.AddComponent<UnknownNativeAnimation>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"late unknown behaviour inside exact original native map tuple remains original between eyes");
        Object.DestroyImmediate(stranger);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"removal of unknown map behaviour restores safe source shading synchronously");
        Object.DestroyImmediate(map);var subclass=room.AddComponent<UnreviewedMapSubclass>();Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"unreviewed subclass never inherits permission from data-only native ApparanceMap");
        Object.DestroyImmediate(subclass);map=room.AddComponent<ApparanceMap>();Center(camera);
        var foreign=new GameObject("Foreign scripted room");foreign.AddComponent<UnknownNativeAnimation>();source.transform.SetParent(foreign.transform,false);Center(camera);
        Check(source.sharedMaterials.SequenceEqual(new[]{first,second}),"late reparent away from original native map producer retains native materials");
        source.transform.SetParent(room.transform,false);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"original map ancestry regained after foreign reparent is checked freshly");
        Object.DestroyImmediate(foreign);Object.DestroyImmediate(map);Object.DestroyImmediate(config);Object.DestroyImmediate(placement);Object.DestroyImmediate(shadows);
        // A full original room tuple must work from cold discovery, not only from
        // a Surface accepted before the coordinator components were added.
        placement=scenario.AddComponent<ProceduralPlacementNotifierHandler>();shadows=scenario.AddComponent<LightShadowsModifierController>();
        config=room.AddComponent<ProceduralMapConfig>();map=room.AddComponent<ApparanceMap>();
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial)&&PerfMonitor.Counts["WorldMaterial.Candidates"]>0,
            "cold bounded discovery accepts authored native ProcGen ancestry without capped terrain factory admission");
        Object.DestroyImmediate(map);Object.DestroyImmediate(config);Object.DestroyImmediate(placement);Object.DestroyImmediate(shadows);
    }
    private static void Scale(GameObject host,GameObject room,MeshRenderer source,Material first,Material second,Camera camera)
    {
        var copies=new List<MeshRenderer>();for(int i=0;i<63;i++){var clone=Object.Instantiate(source.gameObject);clone.transform.SetParent(room.transform,false);copies.Add(clone.GetComponent<MeshRenderer>());WorldMaterialBudget.MaterialReady(copies[copies.Count-1]);}
        NativeWriteObserver.MaterialCopies=0;NativeWriteObserver.ArrayWrites=0;Center(camera);
        Check(NativeWriteObserver.MaterialCopies==2&&PerfMonitor.Counts["WorldMaterial.VariantSlots"]==128,"world material refresh scales with two unique originals instead of 128 native slots");
        NativeWriteObserver.MaterialCopies=0;NativeWriteObserver.ArrayWrites=0;Center(camera);
        Check(NativeWriteObserver.MaterialCopies==2&&NativeWriteObserver.ArrayWrites==0,"settled 64-source world stage copies unique materials without per-eye native array writes");
        int ownershipReads=0,consumerNotifications=0;WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>{ownershipReads++;return false;});
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>{consumerNotifications++;WorldMaterialBudget.MaterialReady(renderer);});Center(camera);
        Check(ownershipReads==0&&consumerNotifications==0,"settled safe visible sources do not read substitute ownership or trigger needless consumer notifications");
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>WorldMaterialBudget.MaterialReady(renderer));
        Debug.Log("World material scaling: 64 sources /128slots; copies="+NativeWriteObserver.MaterialCopies+"; arrayWrites="+NativeWriteObserver.ArrayWrites);
        var block=new MaterialPropertyBlock();block.SetColor("_Tint",Color.white);source.SetPropertyBlock(block);
        foreach(var clone in copies)clone.SetPropertyBlock(block);
        NativeWriteObserver.RendererBlockReads=0;NativeWriteObserver.SlotBlockReads=0;Center(camera);
        Check(NativeWriteObserver.RendererBlockReads==64&&NativeWriteObserver.SlotBlockReads==128,
            "settled 64-source two-slot MPBs read renderer-wide blocks once per source and keep each slot independent");
        Debug.Log("World material MPB scaling: 64 sources /128slots; wideReads="+NativeWriteObserver.RendererBlockReads+"; slotReads="+NativeWriteObserver.SlotBlockReads);
        block.SetFloat("_AddVertexAnim",1f);source.SetPropertyBlock(block,1);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0])&&source.sharedMaterials[1]==second,
            "live per-slot effect remains authoritative with shared renderer-wide block reads");
        source.SetPropertyBlock(null);source.SetPropertyBlock(null,1);foreach(var clone in copies)clone.SetPropertyBlock(null);
        foreach(var clone in copies)Object.DestroyImmediate(clone.gameObject);
    }
    private static void PropGuards(GameObject host,GameObject room,MeshRenderer source,Material first,Material second,Camera camera)
    {
        source.sharedMaterials=new[]{first,second};
        var registered=new GameObject("Registered original prop");registered.transform.SetParent(room.transform,false);
        var other=new GameObject("Same-count replacement visual");other.transform.SetParent(room.transform,false);
        foreach(bool shared in new[]{true,false})
        {
            PerfConfig.SharedEnvironmentMaterialReadsOn=shared;Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"current noninteractive scenery stays eligible with shared reads="+shared);
            object key=Props.Register(registered);source.transform.SetParent(registered.transform,false);Center(camera);
            Check(source.sharedMaterial==first,"registered unheld prop subtree remains native with shared reads="+shared);
            source.transform.SetParent(room.transform,false);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"reparent away from registered visual regains safe ownership with shared reads="+shared);
            Props.Registry[key].Visual=source.gameObject;Center(camera);
            Check(source.sharedMaterial==first,"same-count registered visual replacement is current at next eye with shared reads="+shared);
            Props.Registry.Remove(key);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"unregistered original visual regains world shading with shared reads="+shared);
            RemoteProps.Visuals.Add(source.gameObject);Center(camera);
            Check(source.sharedMaterial==first,"remote held original visual remains native with shared reads="+shared);
            RemoteProps.Visuals[0]=other;Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"same-count remote held visual replacement is current with shared reads="+shared);
            RemoteProps.Visuals.Clear();LocalProps.Visuals.Add(registered);source.transform.SetParent(registered.transform,false);Center(camera);
            Check(source.sharedMaterial==first,"local held ancestor visual remains native with shared reads="+shared);
            LocalProps.Visuals.Clear();source.transform.SetParent(room.transform,false);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"local release and current reparent restore safe scenery with shared reads="+shared);
            object? lateKey=null;Camera.CameraCallback late=cam=>{if(cam==camera)lateKey=Props.Register(source.gameObject);};Camera.onPreCull+=late;
            Center(camera);Camera.onPreCull-=late;
            Check(source.sharedMaterial==first,"final native boundary observes same-camera late prop registration with shared reads="+shared);
            Props.Registry.Remove(lateKey!);Center(camera);
        }
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;
        var visuals=new List<GameObject>();var keys=new List<object>();var copies=new List<MeshRenderer>();
        for(int i=0;i<128;i++){var visual=new GameObject("Registered visual "+i);visual.transform.SetParent(room.transform,false);visuals.Add(visual);keys.Add(Props.Register(visual));}
        for(int i=0;i<63;i++){var clone=Object.Instantiate(source.gameObject);clone.transform.SetParent(room.transform,false);copies.Add(clone.GetComponent<MeshRenderer>());WorldMaterialBudget.MaterialReady(copies[copies.Count-1]);}
        NativeWriteObserver.PropCopies=0;NativeWriteObserver.RegistryVisits=0;Center(camera);
        int sharedVisits=NativeWriteObserver.RegistryVisits;
        Check(NativeWriteObserver.PropCopies==1&&sharedVisits==128,"64-source pass enumerates 128 exact grabbable roots once instead of per ancestor");
        Check(PerfMonitor.Counts["WorldMaterial.PropRootReads"]==128&&PerfMonitor.Counts["WorldMaterial.ScopeNodeReads"]>=64,
            "Debug operation counts expose exact prop roots and memoized ancestry work");
        PerfConfig.SharedEnvironmentMaterialReadsOn=false;NativeWriteObserver.PropCopies=0;NativeWriteObserver.RegistryVisits=0;Center(camera);
        int unsharedVisits=NativeWriteObserver.RegistryVisits;
        Check(NativeWriteObserver.PropCopies==0&&unsharedVisits>sharedVisits*64,"configurable unshared path retains fresh original registry guard while shared path removes repeated work");
        Check(copies.All(copy=>WorldMaterialBudget.IsOwnedVariant(copy.sharedMaterial)),"shared and original guard paths retain complete room renderer material coverage");
        Debug.Log("World ownership scaling: sources=64 roots=128; sharedRegistryVisits="+sharedVisits+"; unsharedRegistryVisits="+unsharedVisits);
        foreach(object key in keys)Props.Registry.Remove(key);foreach(GameObject visual in visuals)Object.DestroyImmediate(visual);foreach(MeshRenderer copy in copies)Object.DestroyImmediate(copy.gameObject);
        PerfConfig.SharedEnvironmentMaterialReadsOn=true;
        int ambientReads=0;WorldMaterialBudget.ConfigureAmbientWeight(()=>{ambientReads++;return .42f;});Center(camera);
        Check(ambientReads==1&&source.sharedMaterials.All(m=>Mathf.Approximately(m.GetFloat("_GHVRWorldAmbientWeight"),.42f)),"one synchronous pass reads current ambient config once for all originals");
        WorldMaterialBudget.ConfigureAmbientWeight(()=>1f);
        using(IDisposable pass=WorldMaterialBudget.BeginMaterialReadPass())
        {
            Material initial=WorldMaterialBudget.VariantFor(first);first.SetColor("_Tint",Color.cyan);
            WorldMaterialBudget.BeforeNativeRendererWrite(source);Material refreshed=WorldMaterialBudget.VariantFor(first);
            Check(initial==refreshed&&refreshed.GetColor("_Tint")==Color.cyan,"native writer boundary invalidates prepared material reads within an outer pass");
            Center(camera);LocalProps.Visuals.Add(source.gameObject);Center(camera);
            Check(source.sharedMaterial==first,"nested actual camera boundary refreshes current local held roots and cached ancestry");
            LocalProps.Visuals.Clear();Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"nested actual camera boundary observes current local release without waiting for outer pass disposal");
            Shader previous=first.shader;string[] keywords=first.shaderKeywords;Color tint=first.GetColor("_Tint");
            Camera.CameraCallback late=cam=>
            {
                if(cam!=camera)return;
                first.shader=Shader.Find("Amp_Low/Amp_Basic_Low");first.shaderKeywords=new[]{"_DESATURATION_ON"};first.SetColor("_Tint",Color.magenta);
                WorldMaterialBudget.ConfigureAmbientWeight(()=>.24f);
            };
            Camera.onPreCull+=late;Color current=Center(camera);Camera.onPreCull-=late;
            Material currentVariant=source.sharedMaterial;
            Check(currentVariant.GetFloat("_GHVRWorldNativeRoute")==6&&currentVariant.IsKeywordEnabled("_DESATURATION_ON")
                &&currentVariant.GetColor("_Tint")==Color.magenta&&Mathf.Approximately(currentVariant.GetFloat("_GHVRWorldAmbientWeight"),.24f)
                &&current.r>.7f&&current.b>.7f&&current.g<.1f,
                "nested actual final camera boundary reads current native shader keywords tint and ambient into current pixels");
            first.shader=previous;first.shaderKeywords=keywords;first.SetColor("_Tint",tint);WorldMaterialBudget.ConfigureAmbientWeight(()=>1f);
        }
        Object.DestroyImmediate(registered);Object.DestroyImmediate(other);
        Center(camera);NativeWriteObserver.MeshReads=0;room.SetActive(false);Center(camera);
        Check(NativeWriteObserver.MeshReads==0&&source.sharedMaterial==first,"inactive native candidates restore original slots without reading mesh filters");
        room.SetActive(true);Center(camera);Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"reactivated native room checks current mesh and restores complete shading immediately");
    }
    private static void RenewedRefusals(GameObject host,MeshRenderer source,Material first,Material second,Camera camera)
    {
        MeshRenderer? proxy=null;int revoked=0,reads=0,releaseReads=0,notifications=0;var block=new MaterialPropertyBlock();
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>{reads++;return renderer==source&&proxy!=null&&proxy.enabled;});
        WorldMaterialBudget.ConfigureRenderSubstituteRevocation(renderer=>{releaseReads++;return renderer==source&&proxy!=null&&proxy.enabled;});
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>{if(renderer==source){notifications++;if(proxy!=null&&proxy.enabled){revoked++;proxy.enabled=false;source.forceRenderingOff=false;}}});
        foreach(string refusal in new[]{"scope","slot-effect","shader","empty-slots","inactive"})
        {
            source.enabled=true;source.sharedMaterials=new[]{first,second};WorldMaterialBudget.MaterialReady(source);Center(camera);
            UnknownNativeAnimation? unknown=null;
            if(refusal=="scope")unknown=source.gameObject.AddComponent<UnknownNativeAnimation>();
            if(refusal=="slot-effect"){block.SetFloat("_AddVertexAnim",1f);source.SetPropertyBlock(block,1);}
            int before=revoked;
            for(int eye=0;eye<2;eye++)
            {
                Camera.CameraCallback renew=cam=>
                {
                    if(cam!=camera)return;
                    first.DisableKeyword("_ENABLE_ANIM");
                    Material factory=WorldMaterialBudget.VariantFor(first);
                    proxy=Source("Renewed earlier substitute",null!,source.GetComponent<MeshFilter>().sharedMesh,factory);
                    source.forceRenderingOff=true;
                    if(refusal=="shader")first.EnableKeyword("_ENABLE_ANIM");
                    if(refusal=="empty-slots")source.sharedMaterials=Array.Empty<Material>();
                    if(refusal=="inactive")source.enabled=false;
                };
                Camera.onPreCull+=renew;reads=0;releaseReads=0;Color shown=Center(camera);Camera.onPreCull-=renew;
                Check(proxy!=null&&!proxy.enabled&&!source.forceRenderingOff&&revoked==before+eye+1,
                    "successive actual camera culls revoke each renewed "+refusal+" substitute, eye="+eye);
                Check(reads<=1&&releaseReads<=1,"each renewed refused source reads eligibility and exact current revocation ownership at most once: "+refusal+" eye="+eye);
                if(refusal=="scope"||refusal=="shader")Check(source.sharedMaterial==first,"repeated refused source retains original native material: "+refusal);
                if(refusal=="slot-effect")Check(source.sharedMaterials[1]==second&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0]),"renewed slot effect preserves independent safe world slot and current native effect slot");
                if(refusal=="empty-slots"||refusal=="inactive")Check(shown.maxColorComponent<.1f,"renewed substitute cannot revive a native empty or hidden source: "+refusal);
                Object.DestroyImmediate(proxy!.gameObject);proxy=null;
            }
            if(unknown!=null)Object.DestroyImmediate(unknown);source.SetPropertyBlock(null,1);block.Clear();first.DisableKeyword("_ENABLE_ANIM");source.enabled=true;
        }
        source.sharedMaterials=new[]{first,second};Center(camera);
        var preparedOnly=source.gameObject.AddComponent<UnknownNativeAnimation>();
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>renderer==source);
        WorldMaterialBudget.ConfigureRenderSubstituteRevocation(_=>false);Center(camera);int firstRefusal=notifications;Center(camera);Center(camera);
        Check(notifications==firstRefusal,"unchanged prepared-only refused source does not revoke or re-adopt without a live or queued consumer");
        Object.DestroyImmediate(preparedOnly);Center(camera);
        source.sharedMaterials=new[]{first,second};Center(camera);
        proxy=Source("Live substitute before Off",null!,source.GetComponent<MeshFilter>().sharedMesh,WorldMaterialBudget.VariantFor(first));source.forceRenderingOff=true;
        WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{if(proxy!=null){proxy.enabled=false;source.forceRenderingOff=false;}});
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);
        Check(!proxy.enabled&&!source.forceRenderingOff&&source.sharedMaterials.SequenceEqual(new[]{first,second}),"Off revokes a renewed live consumer before disposal and restores exact original slots");
        Object.DestroyImmediate(proxy.gameObject);proxy=null;WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{});
        PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);WorldMaterialBudget.ConfigureSourceChanged(renderer=>WorldMaterialBudget.MaterialReady(renderer));WorldMaterialBudget.ConfigureRenderSubstituteOwnership(_=>false);WorldMaterialBudget.ConfigureRenderSubstituteRevocation(_=>false);Center(camera);
    }
    private static void LateChanges(GameObject host,MeshRenderer source,Material first,Material second,Camera camera)
    {
        source.sharedMaterials=new[]{first};WorldMaterialBudget.MaterialReady(source);Center(camera);
        Camera.CameraCallback write=cam=>{if(cam==camera)first.SetColor("_Tint",Color.magenta);};Camera.onPreCull+=write;
        Color shown=Center(camera);Camera.onPreCull-=write;
        Check(shown.r>.7f&&shown.b>.7f&&shown.g<.1f,"final native cull boundary sees late material edits in the same actual camera picture");
        bool revoke=false;WorldMaterialBudget.ConfigureSourceChanged(renderer=>{if(renderer==source){revoke=true;source.forceRenderingOff=false;}});
        WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>renderer==source);
        // Factory-only proxy variant: no owned reference in the original source array.
        source.sharedMaterial=first;source.forceRenderingOff=true;Material proxyMaterial=WorldMaterialBudget.VariantFor(first);
        var proxy=Source("Earlier private draw",null!,source.GetComponent<MeshFilter>().sharedMesh,proxyMaterial);
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>{if(renderer==source){revoke=true;source.forceRenderingOff=false;proxy.enabled=false;}});
        Animator added=null!;Camera.CameraCallback late=cam=>{if(cam==camera)added=source.gameObject.AddComponent<Animator>();};Camera.onPreCull+=late;
        shown=Center(camera);Camera.onPreCull-=late;
        Check(revoke&&!source.forceRenderingOff&&!proxy.enabled&&source.sharedMaterial==first&&shown.r>.7f,"late native scope refusal revokes an earlier proxy-only variant before actual culling");
        WorldMaterialBudget.ConfigureSourceChanged(renderer=>WorldMaterialBudget.MaterialReady(renderer));
        Object.DestroyImmediate(added);Object.DestroyImmediate(proxy.gameObject);
        // Native may deliberately remove all material slots after an earlier proxy
        // read. Empty native output must revoke that private draw in the same camera.
        source.sharedMaterials=new[]{first};Center(camera);source.sharedMaterials=Array.Empty<Material>();source.forceRenderingOff=true;
        var emptyConsumer=Source("Earlier draw before empty slots",null!,source.GetComponent<MeshFilter>().sharedMesh,WorldMaterialBudget.VariantFor(first));
        bool emptyRevoked=false;WorldMaterialBudget.ConfigureSourceChanged(renderer=>{if(renderer==source){emptyRevoked=true;source.forceRenderingOff=false;emptyConsumer.enabled=false;}});
        shown=Center(camera);Check(emptyRevoked&&!emptyConsumer.enabled&&source.sharedMaterials.Length==0&&shown.maxColorComponent<.1f,"empty native material slots revoke an earlier factory-only draw without recreating room materials");
        Object.DestroyImmediate(emptyConsumer.gameObject);
        bool disposed=false;source.sharedMaterial=first;source.forceRenderingOff=true;
        var consumer=Source("Earlier queued consumer",null!,source.GetComponent<MeshFilter>().sharedMesh,WorldMaterialBudget.VariantFor(first));
        WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{disposed=true;consumer.enabled=false;source.forceRenderingOff=false;});
        PerfConfig.WorldMaterialQualityMode=0;Tick(host);
        Check(disposed&&!consumer.enabled&&!source.forceRenderingOff&&source.sharedMaterial==first,"stage zero releases factory-only render consumers before any private variant disposal");
        Object.DestroyImmediate(consumer.gameObject);WorldMaterialBudget.ConfigureBeforeVariantDisposal(()=>{});
        PerfConfig.WorldMaterialQualityMode=2;Tick(host,16);
        source.sharedMaterials=new[]{first,second};WorldMaterialBudget.ConfigureSourceChanged(renderer=>WorldMaterialBudget.MaterialReady(renderer));WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer=>false);Center(camera);
    }
}
