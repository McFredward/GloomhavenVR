using System;
using System.Collections;
using System.IO;
using System.Linq;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "";
    private static Camera camera = null!;
    private static string output = "";
    private static void Check(bool value, string label) { Checks++; if (!value) throw new InvalidOperationException(label); }
    private static string Arg(string key) { string[] args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    private static Color32[] Picture(string name)
    {
        camera.Render(); RenderTexture.active = camera.targetTexture;
        var tex = new Texture2D(256,256,TextureFormat.RGBA32,false);
        tex.ReadPixels(new Rect(0,0,256,256),0,0);tex.Apply();
        File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());
        Color32[] pixels=tex.GetPixels32();Object.DestroyImmediate(tex);return pixels;
    }
    private static int Lit(Color32[] pixels) => pixels.Count(p => p.r > 10 || p.g > 10 || p.b > 10);
    private static int Difference(Color32[] a,Color32[] b) => a.Zip(b,(x,y)=>(x.r>10||x.g>10||x.b>10)!=(y.r>10||y.g>10||y.b>10)).Count(x=>x);
    private static Mesh Quad()
    {
        var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();return mesh;
    }
    private static Renderer Surface(string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.layer=27;go.AddComponent<MeshFilter>().sharedMesh=mesh;
        Renderer r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;return r;
    }
    public static IEnumerator Run()
    {
        output=Path.Combine(Arg("-evidenceRoot"),typeof(InteractionProgram).Assembly.GetName().Name!);Directory.CreateDirectory(output);
        PlayTray.Shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Overlay_"+typeof(InteractionProgram).Assembly.GetName().Name+".shader");
        Check(PlayTray.Shader!=null&&PlayTray.Shader.isSupported,"production overlay shader supported");
        camera=new GameObject("graphics probe").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.cullingMask=1<<27;camera.orthographic=true;camera.orthographicSize=1.1f;camera.nearClipPlane=.01f;camera.farClipPlane=100;
        camera.transform.position=new Vector3(0,0,-5);camera.targetTexture=new RenderTexture(256,256,24);
        RenderSettings.fog=false;
        Material plain=FigureOverlay.MakeOverlayMaterial(Color.white,false)!;
        Check(plain.GetFloat("_UseAlphaMask")==0,"existing widget mask default remains neutral");
        Mesh quad=Quad();Renderer native=Surface("native alpha card",quad,plain);
        int neutral=Lit(Picture("widget-neutral"));Check(neutral>40000,"unmasked widget remains a complete quad");
        var mask=new Texture2D(32,32,TextureFormat.RGBA32,false);mask.filterMode=FilterMode.Point;mask.wrapMode=TextureWrapMode.Clamp;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)mask.SetPixel(x,y,new Color(1,1,1,(x-16)*(x-16)+(y-16)*(y-16)<80?1:0));mask.Apply();
        var original=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeCutout.shader"));original.SetTexture("_Diffuse",mask);original.SetFloat("_Cutoff",.5f);
        native.sharedMaterial=original;
        Material glow=FigureOverlay.MakeOverlayMaterial(Color.white,true)!;
        Renderer overlay=Surface("actual additive overlay",quad,glow);
        FigureOverlayMasks.Apply(native,overlay,new[]{original});native.enabled=false;
        Color32[] clipped=Picture("additive-cutout");int alpha=Lit(clipped);
        Check(alpha>5000&&alpha<neutral*.4,"native alpha silhouette prevents rectangular additive quads");
        glow.mainTextureOffset=new Vector2(.31f,.45f);
        Check(Difference(clipped,Picture("cutout-after-pulse-scroll"))==0,"pulse scroll cannot displace native alpha UVs");
        original.SetTextureScale("_Diffuse",new Vector2(.5f,1));original.SetTextureOffset("_Diffuse",new Vector2(.25f,0));
        native.enabled=true;FigureOverlayMasks.Apply(native,overlay,new[]{original});native.enabled=false;
        Check(Lit(Picture("cutout-native-uv"))>alpha*1.8,"native mask texture scale and offset retained");
        original.SetTextureScale("_Diffuse",Vector2.one);original.SetTextureOffset("_Diffuse",Vector2.zero);original.SetFloat("_Cutoff",0);
        native.enabled=true;FigureOverlayMasks.Apply(native,overlay,new[]{original});native.enabled=false;
        Material depth=new Material(plain);depth.SetInt("_SrcBlend",0);depth.SetInt("_DstBlend",1);depth.SetInt("_ZWrite",1);depth.renderQueue=2490;
        Renderer depthTwin=Surface("native masked depth twin",quad,depth);FigureOverlayMasks.Copy(overlay,depthTwin,1);
        Material panel=new Material(plain);panel.color=Color.red;panel.renderQueue=3100;Renderer behind=Surface("panel behind ghost",quad,panel);behind.transform.position=new Vector3(0,0,1);
        Color32[] depthPixels=Picture("depth-cutout-hole");
        Check(depthPixels.Count(p=>p.r>100&&p.g<10)>neutral*.6,"zero-alpha texels never stamp rectangular ghost depth");
        Check(depthPixels.Count(p=>p.r>100&&p.g>100)>alpha*.9,"visible silhouette still occludes panel behind it");
        Object.DestroyImmediate(native.gameObject);Object.DestroyImmediate(overlay.gameObject);Object.DestroyImmediate(depthTwin.gameObject);Object.DestroyImmediate(behind.gameObject);

        var split=new Mesh();split.vertices=new[]{new Vector3(-1,-1,0),new Vector3(0,-1,0),new Vector3(0,1,0),new Vector3(-1,1,0),new Vector3(0,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(0,1,0)};
        split.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero,Vector2.right,Vector2.one,Vector2.up};split.subMeshCount=2;split.SetTriangles(new[]{0,1,2,0,2,3},0);split.SetTriangles(new[]{4,5,6,4,6,7},1);split.RecalculateBounds();
        Renderer mixed=Surface("mixed native submeshes",split,original);mixed.sharedMaterials=new[]{original,plain};
        Renderer mixedOverlay=Surface("mixed overlay submeshes",split,glow);mixedOverlay.sharedMaterials=new[]{glow,glow};
        FigureOverlayMasks.Apply(mixed,mixedOverlay,mixed.sharedMaterials);mixed.enabled=false;
        int mixedPixels=Lit(Picture("mixed-alpha-opaque-submeshes"));
        Check(Mathf.Abs(mixedPixels-(neutral+alpha)*.5f)<400,"each native submesh retains its own opaque/cutout mask");
        mixed.enabled=true;FigureOverlayMasks.Apply(mixed,mixedOverlay,new[]{original});mixed.enabled=false;
        Check(Mathf.Abs(Lit(Picture("shared-alpha-submesh-material"))-alpha)<400,"last native cutout material reused on remaining submeshes");
        Object.DestroyImmediate(mixed.gameObject);Object.DestroyImmediate(mixedOverlay.gameObject);

        // Native asset provenance is required. These are the shipped SpittingDrake rig, meshes
        // and sleeping/flying clips, with a fixture controller to isolate state from game rules.
        AssetBundle bundle=AssetBundle.LoadFromFile(Arg("-nativeDrakeBundle"));Check(bundle!=null,"native drake bundle loads");
        GameObject prefab=bundle!.LoadAsset<GameObject>("Assets/Content/Characters/Monsters/MO_SpittingDrake/MO_SpittingDrake_PR.prefab");
        Check(prefab!=null,"native drake prefab available");
        GameObject source=FigureVisualMirror.CloneVisual(prefab!,Vector3.zero,Quaternion.identity,Vector3.one,out var unused);Object.DestroyImmediate(unused);
        foreach(Transform t in source.GetComponentsInChildren<Transform>(true))t.gameObject.layer=27;
        foreach(Renderer r in source.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=Enumerable.Repeat(plain,r.sharedMaterials.Length).ToArray();
        Animator nativeAnimator=prefab!.GetComponentsInChildren<Animator>(true).First(a=>a.runtimeAnimatorController!=null&&a.runtimeAnimatorController.name=="SpittingDrake_Controller");
        AnimationClip[] clips=nativeAnimator.runtimeAnimatorController.animationClips;
        AnimationClip sleep=clips.Single(c=>c.name=="Spitting_Drake_Sleeping_Idle_v001"),fly=clips.Single(c=>c.name=="Spitting_Flying_Idle_v001");
        Check(sleep.length>0&&fly.length>0,"genuine native sleeping/flying clips available");
        string path=AnimationUtility.CalculateTransformPath(nativeAnimator.transform,prefab.transform);
        Transform animated=path.Length==0?source.transform:source.transform.Find(path);
        Animator animator=animated.gameObject.AddComponent<Animator>();animator.avatar=nativeAnimator.avatar;
        var controller=AnimatorController.CreateAnimatorControllerAtPath("Assets/"+typeof(InteractionProgram).Assembly.GetName().Name+".controller");
        controller.AddParameter("Sleeping",AnimatorControllerParameterType.Bool);controller.AddParameter("Rate",AnimatorControllerParameterType.Float);controller.AddParameter("Variant",AnimatorControllerParameterType.Int);
        AnimatorState flying=controller.layers[0].stateMachine.AddState("Flying");flying.motion=fly;
        AnimatorState sleeping=controller.layers[0].stateMachine.AddState("Sleeping");sleeping.motion=sleep;
        sleeping.AddStateMachineBehaviour<NativeStateProbe>();flying.AddStateMachineBehaviour<NativeStateProbe>();
        controller.layers[0].stateMachine.defaultState=flying;AssetDatabase.SaveAssets();
        controller.AddLayer("Secondary pose");var second=controller.layers[1];second.defaultWeight=.23f;
        var layers=controller.layers;layers[1]=second;controller.layers=layers;
        animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
        source.AddComponent<NativeCallbackProbe>();animator.SetBool("Sleeping",true);animator.SetFloat("Rate",.73f);animator.SetInteger("Variant",2);
        animator.Play("Base Layer.Sleeping",0,.37f);animator.Update(0);
        var actor=source.AddComponent<ActorBehaviour>();actor.m_RootGameObject=source;actor.m_AnimatedGameObject=animated.gameObject;
        Material ringMaterial=new Material(plain);ringMaterial.color=Color.red;
        Renderer ring=Surface("selection ring",quad,ringMaterial);ring.transform.SetParent(source.transform,false);ring.transform.localPosition=new Vector3(3,0,0);ring.transform.localScale=Vector3.one*.1f;actor.m_Hilight=ring.gameObject;
        var shapeMesh=Object.Instantiate(quad);shapeMesh.bindposes=new[]{Matrix4x4.identity};shapeMesh.boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},4).ToArray();
        shapeMesh.AddBlendShapeFrame("native shape",100,Enumerable.Repeat(new Vector3(0,.1f,0),4).ToArray(),new Vector3[4],new Vector3[4]);
        var shapeObject=new GameObject("shape proof");shapeObject.transform.SetParent(source.transform,false);shapeObject.transform.localPosition=new Vector3(3,0,0);
        var shape=shapeObject.AddComponent<SkinnedMeshRenderer>();shape.sharedMesh=shapeMesh;shape.sharedMaterial=plain;shape.bones=new[]{shapeObject.transform};shape.rootBone=shapeObject.transform;shape.SetBlendShapeWeight(0,35);
        Bounds bounds=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000).bounds;
        camera.orthographicSize=Mathf.Max(bounds.extents.x,bounds.extents.y)*1.4f;camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,bounds.center.z-15);
        animator.Play("Base Layer.Flying",0,.37f);animator.Update(.001f);yield return null;
        Color32[] flyingImage=Picture("native-flying-source");
        animator.Play("Base Layer.Sleeping",0,.37f);animator.Update(.001f);yield return null;
        Color32[] sleepingImage=Picture("native-sleeping-source");Check(Lit(sleepingImage)>300,"native sleeping source renders real skinned body");
        Check(Difference(flyingImage,sleepingImage)>300,"native sleeping and flying states have visibly distinct evaluated poses");
        foreach(bool remote in new[]{false,true})
        {
            ring.gameObject.SetActive(true);ring.enabled=true;
            source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=Vector3.one;
            animator.Play("Base Layer.Sleeping",0,.37f);animator.Update(0);
            int awakes=NativeCallbackProbe.Awakes,enters=NativeStateProbe.Enters,events=NativeCallbackProbe.Events;
            if(remote)NetHeldFigures.Actors.Add(actor);else HeldFigures.Actors.Add(actor);
            var excluded=new GameObject("VRExcludedArt");excluded.transform.SetParent(source.transform,false);
            var excludedChild=new GameObject("native-looking child");excludedChild.transform.SetParent(excluded.transform,false);
            FigureGhosts.NotifyHeld(actor,Vector3.zero,Quaternion.identity);GameObject ghost=FigureGhosts.GhostFor(actor)!;
            Check(ghost!=null,"local/remote held native sleeping ghost exists");
            var mirror=ghost!.GetComponent<FigureVisualMirror>();
            Check(ghost.GetComponentsInChildren<Animator>(true).Length==0,"ghost never owns native Animator/controller callbacks");
            Check(ghost.GetComponentsInChildren<NativeCallbackProbe>(true).Length==0,"ghost never instantiates native gameplay Awake");
            Check(mirror.InitialAnimatorPoses.Length==1,"startup records original Animator metadata");
            var snapshot=mirror.InitialAnimatorPoses[0];Check(snapshot.States[0].fullPathHash==Animator.StringToHash("Base Layer.Sleeping"),"startup records native sleeping fullPathHash");
            Check(Mathf.Abs(snapshot.States[0].normalizedTime-.37f)<.001,"startup records exact original clip phase");
            Check(snapshot.Parameters.Length==3&&snapshot.Values.Contains(.73f)&&snapshot.Integers.Contains(2)&&snapshot.Booleans.Contains(true),"startup records controller parameter values");
            Check(snapshot.Weights.Length==2&&Mathf.Abs(snapshot.Weights[1]-.23f)<.001f,"startup records every native animation layer and weight");
            Check(ghost.GetComponentsInChildren<Collider>(true).Length==0&&ghost.GetComponentsInChildren<Cloth>(true).Length==0,"home ghost never instantiates native physics");
            Renderer ringCopy=ghost.transform.Find("selection ring").GetComponent<Renderer>();
            Check(ringCopy.sharedMaterial==ringMaterial,"home selection ring retains exact native material");
            ring.gameObject.SetActive(false);ring.enabled=false;mirror.Sync();
            Check(ringCopy.gameObject.activeSelf&&ringCopy.enabled,"home selection ring ignores in-hand source-ring suppression");
            shape.SetBlendShapeWeight(0,77);mirror.Sync();
            Check(ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh==shapeMesh).All(r=>Mathf.Abs(r.GetBlendShapeWeight(0)-77)<.001),"evaluated blend shape weights match on ghost and its depth twin");
            source.transform.position=new Vector3(50,0,0);source.transform.localScale=Vector3.one*2;
            FigureGhosts.Tick();mirror.Sync();
            Transform excludedCopy=ghost.transform.Find("VRExcludedArt");
            Check(excludedCopy!=null&&!excludedCopy.gameObject.activeSelf,"excluded mod subtree stays inactive in same-frame sync");
            Object.DestroyImmediate(excluded);
            Check(Difference(sleepingImage,Picture((remote?"remote":"local")+"-sleeping-ghost"))<Lit(sleepingImage)*.05,"sleeping ghost snapshot pixels match native sleeping source");
            for(int frame=0;frame<4;frame++)
            {
                animator.Update(.12f);yield return null;
                Transform sourceBone=animated.GetComponentsInChildren<Transform>().First(t=>t.name=="L_wingSkel01_JNT");
                Transform ghostBone=ghost.transform.Find(AnimationUtility.CalculateTransformPath(sourceBone,source.transform));
                Check(Quaternion.Angle(sourceBone.localRotation,ghostBone.localRotation)<.001,"evaluated animation phase remains exact in local/remote hold");
                foreach(var r in ghost.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Check(r.bones.All(b=>b==null||b.IsChildOf(ghost.transform)),"ghost uses its own bone hierarchy");
                Check(ghost.transform.position==Vector3.zero,"held root motion/scale cannot move home ghost");
            }
            // The home-pose owner can update board transforms; the pose mirror must not restore
            // its own obsolete startup world pose during LateUpdate.
            ghost.transform.position=new Vector3(2,3,4);ghost.transform.rotation=Quaternion.Euler(0,31,0);ghost.transform.localScale=Vector3.one*.7f;
            mirror.Sync();Check(ghost.transform.position==new Vector3(2,3,4)&&Mathf.Abs(ghost.transform.localScale.x-.7f)<.0001,"board/home translate and zoom remain authoritative over mirror");
            ghost.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);ghost.transform.localScale=Vector3.one;
            Check(NativeCallbackProbe.Awakes==awakes&&NativeCallbackProbe.Events==events,"ghost never causes native Awake/animation events");
            // Source Animator state changes are authoritative; the ghost copies evaluated pose,
            // never independently restarts a generic idle, even during a native blend.
            animator.CrossFade("Base Layer.Flying",.25f);animator.Update(.1f);mirror.Sync();
            Check(NativeStateProbe.Enters>enters,"source native state machine callback is a live negative control");
            int beforeMirror=NativeStateProbe.Enters;mirror.Sync();
            Check(NativeStateProbe.Enters==beforeMirror,"only source native state machine callback runs");
            Renderer src=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000);
            src.forceRenderingOff=true;mirror.Sync();Renderer clone=ghost.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh==((SkinnedMeshRenderer)src).sharedMesh);
            Check(clone.forceRenderingOff,"source cosmetic/LOD forceRenderingOff survives ghost mirroring");src.forceRenderingOff=false;mirror.Sync();Check(!clone.forceRenderingOff,"source visibility restoration survives ghost mirroring");
            HeldFigures.Actors.Clear();NetHeldFigures.Actors.Clear();FigureGhosts.ReleaseIfUnheld(actor);
            Check(FigureGhosts.GhostFor(actor)==null&&!ghost.activeSelf,"action release disables ghost immediately before deferred destroy");
            yield return null;
        }
        var dormantRoot=new GameObject("native actor with dormant surface");
        var dormant=Surface("dormant native surface",quad,ringMaterial);dormant.transform.SetParent(dormantRoot.transform,false);dormant.gameObject.SetActive(false);
        var visible=Surface("visible native surface",quad,plain);visible.transform.SetParent(dormantRoot.transform,false);
        GameObject dormantGhost=FigureOverlay.BuildFrozenGhost(dormantRoot,Vector3.zero,Quaternion.identity,Vector3.one,FigureOverlay.MakeOverlayMaterial(Color.blue,false)!,out _)!;
        Renderer dormantCopy=dormantGhost.transform.Find("dormant native surface").GetComponent<Renderer>();
        Check(dormantCopy.sharedMaterial!=ringMaterial,"inactive native surface is tinted before it can activate");
        dormant.gameObject.SetActive(true);dormantGhost.GetComponent<FigureVisualMirror>().Sync();
        Check(dormantCopy.enabled&&dormantCopy.gameObject.activeInHierarchy,"inactive-to-active native surface retains evaluated visibility");
        Object.DestroyImmediate(dormantGhost);Object.DestroyImmediate(dormantRoot);
        camera.orthographicSize=1.1f;camera.transform.position=new Vector3(0,0,-5);
        Renderer loading=Surface("native material loader pending",quad,plain);loading.transform.position=Vector3.right*30;loading.enabled=false;
        GameObject loadingGhost=FigureOverlay.BuildFrozenGhost(loading.gameObject,Vector3.zero,Quaternion.identity,Vector3.one,FigureOverlay.MakeOverlayMaterial(Color.blue,false)!,out _)!;
        Check(loadingGhost.GetComponent<Renderer>()!=null&&!loadingGhost.GetComponent<Renderer>().enabled,"pending native material surface remains hidden");
        loading.sharedMaterial=original;loading.enabled=true;loadingGhost.GetComponent<FigureVisualMirror>().Sync();
        foreach(Renderer r in loadingGhost.GetComponentsInChildren<Renderer>())
        {
            var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,0);
            Check(block.GetFloat("_UseAlphaMask")==1&&block.GetTexture("_AlphaMaskTex")==mask,"first material-ready edge refreshes ghost and depth cutout masks");
        }
        int loadedPixels=Lit(Picture("native-material-ready-cutout"));Check(loadedPixels>alpha*.9&&loadedPixels<alpha*1.1,"native material readiness cannot reveal rectangular ghost pixels");
        Object.DestroyImmediate(loadingGhost);Object.DestroyImmediate(loading.gameObject);
        Renderer highlightSource=Surface("native highlight material readiness",quad,plain);
        var highlightContainer=new GameObject("production highlight container");
        Renderer highlightCopy=Surface("live highlight readiness",quad,glow);highlightCopy.transform.SetParent(highlightContainer.transform,false);FigureOverlayMasks.Apply(highlightSource,highlightCopy,new[]{plain});FigureVisualMirror.BindHighlight(highlightSource,highlightCopy);
        highlightSource.enabled=false;highlightCopy.GetComponent<FigureVisualMirror>().Sync();Check(!highlightCopy.enabled,"native loading disables its existing highlight immediately");
        highlightSource.sharedMaterial=original;highlightSource.enabled=true;highlightCopy.GetComponent<FigureVisualMirror>().Sync();
        var highlightMask=new MaterialPropertyBlock();highlightCopy.GetPropertyBlock(highlightMask,0);
        Check(highlightCopy.enabled&&highlightMask.GetFloat("_UseAlphaMask")==1&&highlightMask.GetTexture("_AlphaMaskTex")==mask,"material-ready edge refreshes the live highlight mask");
        Object.DestroyImmediate(highlightSource.gameObject);Object.DestroyImmediate(highlightContainer);
        var spriteSource=new GameObject("native sprite ring");var sprite=spriteSource.AddComponent<SpriteRenderer>();
        sprite.sprite=Sprite.Create(mask,new Rect(0,0,32,32),new Vector2(.5f,.5f));sprite.color=Color.cyan;sprite.flipX=true;sprite.sharedMaterial=ringMaterial;
        GameObject spriteGhost=FigureOverlay.BuildFrozenGhost(spriteSource,Vector3.zero,Quaternion.identity,Vector3.one,FigureOverlay.MakeOverlayMaterial(Color.blue,false)!,out _,spriteSource.transform)!;
        var spriteCopy=spriteGhost.GetComponent<SpriteRenderer>();
        Check(spriteCopy!=null&&spriteCopy.sprite==sprite.sprite&&spriteCopy.color==sprite.color&&spriteCopy.flipX,"native sprite selection art preserves original presentation");
        Check(spriteCopy!.sharedMaterial==ringMaterial,"native sprite ring preserves exact original material");
        Object.DestroyImmediate(spriteGhost);Object.DestroyImmediate(spriteSource);
        source.transform.position=Vector3.zero;
        Renderer body=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000);body.forceRenderingOff=true;
        var highlight=new FigureHighlight();highlight.Apply(source,source,null,"native force off",out _);
        Check(!source.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="VROverlay"&&r.sharedMesh==((SkinnedMeshRenderer)body).sharedMesh),"highlight admission respects source forceRenderingOff");
        highlight.Clear();FigureGhosts.Clear();bundle.Unload(false);
        Metrics="native SpittingDrake rig + Sleeping_Idle/Flying_Idle, local/remote evaluated pose + pixels; alpha="+alpha+" neutral="+neutral;
    }
}
