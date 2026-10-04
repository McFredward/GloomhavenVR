using System;
using System.Collections;
using System.IO;
using System.Linq;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
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

        // Exercise the actual extracted production stat preparation with real Unity UI images.
        // The shared sprite/mip cache is an explicit external boundary; no fake native Show or
        // actor identity is used to construct content that is not already present in the UI.
        var statRoot=new GameObject("original stat panel",typeof(RectTransform));statRoot.SetActive(false);
        var realStat=statRoot.AddComponent<ActorStatPanel>();Singleton<ActorStatPanel>.Instance=realStat;
        var authoredSprite=Sprite.Create(mask,new Rect(0,0,32,32),Vector2.one*.5f);
        var image=statRoot.AddComponent<Image>();image.sprite=authoredSprite;
        var statRaw=new GameObject("original stat raw",typeof(RectTransform));statRaw.transform.SetParent(statRoot.transform,false);
        var raw=statRaw.AddComponent<RawImage>();raw.texture=mask;
        var enemyRoot=new GameObject("original enemy panel",typeof(RectTransform));enemyRoot.SetActive(false);
        Singleton<EnemyCurrentTurnStatPanel>.Instance=enemyRoot.AddComponent<EnemyCurrentTurnStatPanel>();
        enemyRoot.AddComponent<Image>().sprite=authoredSprite; // shared original art is queued once
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==2,
            "loading stat preparation deduplicates original inactive UI resources");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationCompleted==1
            &&!GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady,
            "stat preparation performs only one resource per loader tick");
        Check(CardFaceMipBake.Sprites.Contains(authoredSprite),
            "original stat sprite cache is ready before preview");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady
            &&CardFaceMipBake.Textures.Contains(mask),"original raw stat texture uses the same shared cache");
        Check(image.sprite==authoredSprite&&raw.texture==mask&&!statRoot.activeSelf&&!enemyRoot.activeSelf
            &&ActorStatPanel.Shows==0&&EnemyCurrentTurnStatPanel.Shows==0,
            "stat warming never mutates original UI, activates panels or executes native Show");
        CardFaceMipBake.Sprites.Clear();CardFaceMipBake.Textures.Clear();
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=false;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==0
            &&GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady,
            "disabled mip setting adds no loading resource work");
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=true;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation();
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=false;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady
            &&CardFaceMipBake.Sprites.Count==0&&CardFaceMipBake.Textures.Count==0,
            "mip setting disabled mid-preparation drains without stale work or spinner");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.ResetInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==0,
            "stat reset releases every queued original image reference");
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=true;
        Singleton<ActorStatPanel>.Instance=null;Singleton<EnemyCurrentTurnStatPanel>.Instance=null;
        Object.DestroyImmediate(statRoot);Object.DestroyImmediate(enemyRoot);Object.DestroyImmediate(authoredSprite);

        // These selectors mirror the shipped ActorStatPanel methods (recorded in source
        // evidence); the provider/pin boundary is inert. Exercise the whole production
        // collector and queue against original field shapes, never a made-up native Show.
        var tools=new UIInfoTools();UIInfoTools.Instance=tools;
        var preview=Sprite.Create(mask,new Rect(0,0,16,16),Vector2.one*.5f);
        var fallback=Sprite.Create(mask,new Rect(16,0,16,16),Vector2.one*.5f);
        tools.Characters[("Brute","custom hero")]=new CharacterConfigUI{scenarioPreviewInfoPortrait=preview};
        tools.Characters[("Elementalist","")]=new CharacterConfigUI();tools.HeroPortraits[("Elementalist","")]=fallback;
        var monsterRef=new SpriteMemoryManagement.ReferenceToSprite{Sprite=preview,Pending=true};
        var propRef=new SpriteMemoryManagement.ReferenceToSprite{Sprite=fallback};
        var summonRef=new SpriteMemoryManagement.ReferenceToSprite{Sprite=preview};
        tools.Portraits[("SunDemon","custom monster")]=monsterRef;
        tools.Portraits[("prop-portrait","custom monster")]=propRef;
        tools.Portraits[("SlimeSpirit","custom summon")]=summonRef;
        ScenarioRuleClient.SRLYML.MonsterConfigs.Add(new MonsterConfigYMLData{ID="monster",Portrait="custom monster"});
        ScenarioRuleClient.SRLYML.MonsterConfigs.Add(new MonsterConfigYMLData{ID="summon",Portrait="custom summon"});
        ActorBehaviour OriginalActor(ScenarioRuleLibrary.CActor value)
        { var behaviour=new GameObject("original portrait owner").AddComponent<ActorBehaviour>();behaviour.Actor=value;return behaviour; }
        var player=new ScenarioRuleLibrary.CPlayerActor();player.Class.DefaultModel="Brute";player.CharacterClass.CharacterYML.CustomCharacterConfig="custom hero";
        var other=new ScenarioRuleLibrary.CPlayerActor();other.Class.DefaultModel="Elementalist";
        var monster=new ScenarioRuleLibrary.CEnemyActor();monster.MonsterClass.DefaultModel="SunDemon";monster.MonsterClass.MonsterYML.CustomConfig="monster";
        var prop=new ScenarioRuleLibrary.CObjectActor{IsAttachedToProp=true};prop.MonsterClass=monster.MonsterClass;prop.AttachedProp.PropHealthDetails.ActorSpriteName="prop-portrait";
        var summon=new ScenarioRuleLibrary.CHeroSummonActor{Prefab="SlimeSpirit"};summon.HeroSummonClass.SummonYML.CustomConfig="summon";
        ActorBehaviour[] owners={OriginalActor(player),OriginalActor(other),OriginalActor(monster),OriginalActor(monster),OriginalActor(prop),OriginalActor(summon)};
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=false;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation(owners);
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==0&&tools.Requested.Count==0,
            "disabled panel mip setting does not resolve or pin original actor portraits");
        GloomhavenVR.WorldUI.WorldUIConfig.PanelMipBake.Value=true;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation(owners);
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==5
            &&ScenarioCardPreparation.References.Count==3&&CardArtPin.Pins.Count==0,
            "original monster and object portrait selectors use shared deferred reference queue with native custom/summon keys");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(CardFaceMipBake.Sprites.Contains(preview)&&CardFaceMipBake.Sprites.Contains(fallback),
            "original player scenario preview and native fallback portrait are warm before Show");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationCompleted==2
            &&!GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady&&CardArtPin.Pins.Contains(monsterRef),
            "incomplete original portrait pin retains loader work without touching a native loading request");
        monsterRef.Pending=false;
        for(int i=0;i<3;i++)GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady
            &&tools.Requested.Contains(("prop-portrait","custom monster"))&&tools.Requested.Contains(("SlimeSpirit","custom summon"))
            &&ActorStatPanel.Shows==0&&EnemyCurrentTurnStatPanel.Shows==0,
            "all existing actor portrait families reach the same cache without native lifecycle");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.ResetInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==0&&CardArtPin.Pins.Count==3,
            "stat preparation reset clears borrowed jobs while the shared pin owner retains lifetime");
        UIInfoTools.Instance=null;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.BeginInteractionPreparation(owners);
        Check(!GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady
            &&GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==1,
            "late native UI resources keep one deferred portrait discovery job under the loader");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(!GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady,
            "missing native tools cannot silently mark original portraits prepared");
        UIInfoTools.Instance=tools;
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationTotal==5,
            "late native tools trigger exactly one original portrait discovery before completion");
        for(int i=0;i<5;i++)GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.TickInteractionPreparation();
        Check(GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.InteractionPreparationReady,
            "deferred original portrait discovery drains the same shared job queue");
        GloomhavenVR.WorldUI.Surfaces.StatPanelSurface.ResetInteractionPreparation();
        foreach(var owner in owners)Object.DestroyImmediate(owner.gameObject);
        UIInfoTools.Instance=new UIInfoTools();CardArtPin.Pins.Clear();ScenarioCardPreparation.References.Clear();
        CardFaceMipBake.Sprites.Clear();CardFaceMipBake.Textures.Clear();
        Object.DestroyImmediate(preview);Object.DestroyImmediate(fallback);

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
        // Real imported native sleeping/flying bones exercise the same prepared cache used by
        // local and remote holds. Prepare in a DIFFERENT state to catch stale loader poses.
        animator.Play("Base Layer.Flying",0,.11f);animator.Update(0);
        shape.sharedMaterial=original;
        var currentLod=source.AddComponent<LODGroup>();currentLod.enabled=false;
        Renderer lodBody=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000);
        currentLod.SetLODs(new[]{new LOD(.23f,new[]{lodBody})});
        FigureInteractionPreparation.Begin();HeldFigures.Actors.Add(actor);
        FigureGhosts.NotifyHeld(actor,Vector3.zero,Quaternion.identity);
        GameObject immediateWhileLoading=FigureGhosts.GhostFor(actor)!;
        FigureInteractionPreparation.Tick();
        Check(FigureInteractionPreparation.IsReady&&FigureGhosts.GhostFor(actor)==immediateWhileLoading
            &&immediateWhileLoading.activeSelf,
            "pending preparation preserves an immediate live local pickup ghost");
        FigureInteractionPreparation.CancelPreparation();
        Check(FigureInteractionPreparation.IsReady&&FigureGhosts.GhostFor(actor)==immediateWhileLoading
            &&immediateWhileLoading.activeSelf,
            "preparation timeout cancellation preserves an active hold and immediate input");
        HeldFigures.Actors.Clear();FigureGhosts.ReleaseIfUnheld(actor);FigureInteractionPreparation.Reset();yield return null;
        int preparationAwakes=NativeCallbackProbe.Awakes, preparationEnters=NativeStateProbe.Enters;
        FigureInteractionPreparation.Begin();
        for(int tick=0;tick<1000&&!FigureInteractionPreparation.IsReady;tick++)FigureInteractionPreparation.Tick();
        Check(FigureInteractionPreparation.IsReady,
            "loading preparation completes finite original actor work");
        // The skip-inactive-tint defect now affects the entirely inactive prepared ghost
        // before the later dormant-child probe: no original surface survives construction.
        Check(FigureInteractionPreparation.PreparedCount==1,
            "inactive native surface is tinted before it can activate (prepared actor visual exists)");
        Check(FigureInteractionPreparation.CompletedCount==FigureInteractionPreparation.TotalCount,
            "preparation progress drains with finite original resource work");
        Check(NativeCallbackProbe.Awakes==preparationAwakes&&NativeStateProbe.Enters==preparationEnters,
            "preparation does not execute original native callbacks or advance Animator");
        var neverActivated=Resources.FindObjectsOfTypeAll<FigureVisualMirror>()
            .Single(m=>m.gameObject.name=="VRFigureGhost"&&!m.gameObject.activeSelf);
        Material unusedTint=neverActivated.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .First(r=>r.sharedMesh.vertexCount>1000).sharedMaterial;
        FigureInteractionPreparation.Reset();yield return null;
        Check(neverActivated==null&&unusedTint==null,
            "reset releases owned material of a prepared ghost that never activated");
        FigureInteractionPreparation.Begin();
        for(int tick=0;tick<1000&&!FigureInteractionPreparation.IsReady;tick++)FigureInteractionPreparation.Tick();
        animator.Play("Base Layer.Sleeping",0,.63f);animator.Update(0);
        shape.SetBlendShapeWeight(0,66);original.SetTextureOffset("_Diffuse",new Vector2(.125f,0));
        HeldFigures.Actors.Add(actor);FigureGhosts.NotifyHeld(actor,Vector3.zero,Quaternion.identity);
        GameObject firstPrepared=FigureGhosts.GhostFor(actor)!;
        var preparedMirror=firstPrepared.GetComponent<FigureVisualMirror>();
        Check(preparedMirror.InitialAnimatorPoses[0].States[0].fullPathHash==Animator.StringToHash("Base Layer.Sleeping")
            &&Mathf.Abs(preparedMirror.InitialAnimatorPoses[0].States[0].normalizedTime-.63f)<.001,
            "prepared acquire binds current sleeping pose rather than loader flying pose");
        var preparedShape=firstPrepared.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .First(r=>r.sharedMesh==shapeMesh);
        var currentMask=new MaterialPropertyBlock();preparedShape.GetPropertyBlock(currentMask,0);
        Check(Mathf.Abs(preparedShape.GetBlendShapeWeight(0)-66)<.001
            &&Mathf.Abs(currentMask.GetVector("_AlphaMaskTex_ST").z-.125f)<.001,
            "prepared acquire refreshes current blend weights and same-material native mask UVs");
        HeldFigures.Actors.Clear();FigureGhosts.ReleaseIfUnheld(actor);
        Check(!firstPrepared.activeSelf&&FigureGhosts.GhostFor(actor)==null,
            "released prepared ghost parks immediately without rendering");
        yield return null; // Pool lifetime must survive deferred destruction before next pickup.
        animator.Play("Base Layer.Flying",0,.41f);animator.Update(0);
        NetHeldFigures.Actors.Add(actor);FigureGhosts.NotifyHeld(actor,Vector3.one,Quaternion.Euler(0,17,0));
        GameObject remotePrepared=FigureGhosts.GhostFor(actor)!;
        Check(remotePrepared==firstPrepared,"local and remote pickup reuse the same original visual cache");
        Check(preparedMirror.InitialAnimatorPoses[0].States[0].fullPathHash==Animator.StringToHash("Base Layer.Flying")
            &&Mathf.Abs(preparedMirror.InitialAnimatorPoses[0].States[0].normalizedTime-.41f)<.001,
            "pooled remote acquire refreshes current native state and phase");
        Check(remotePrepared.transform.position==Vector3.one,
            "pooled acquire uses current authoritative home pose");
        Shader sourceShader=original.shader;int sourceQueue=original.renderQueue;
        original.shader=plain.shader;original.renderQueue=sourceQueue; // isolate shader identity from queue identity
        Check(!preparedMirror.MatchesPreparedSource(source),
            "same material with changed shader invalidates prepared surface classification");
        original.shader=sourceShader;original.renderQueue=sourceQueue;
        Check(preparedMirror.MatchesPreparedSource(source),
            "original shader restoration preserves the exact prepared identity receipt");
        currentLod.SetLODs(new[]{new LOD(.47f,new[]{lodBody})});
        Check(!preparedMirror.MatchesPreparedSource(source),
            "changed native LOD transition invalidates prepared renderer table");
        currentLod.SetLODs(new[]{new LOD(.23f,new[]{lodBody})});
        NetHeldFigures.Actors.Clear();FigureGhosts.ReleaseIfUnheld(actor);
        Renderer replaced=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000);
        var replacementSkin=(SkinnedMeshRenderer)replaced;
        Mesh initialMesh=replacementSkin.sharedMesh;
        Mesh replacementMesh=Object.Instantiate(initialMesh);replacementSkin.sharedMesh=replacementMesh;
        HeldFigures.Actors.Add(actor);FigureGhosts.NotifyHeld(actor,Vector3.zero,Quaternion.identity);
        Check(FigureGhosts.GhostFor(actor)!=firstPrepared,
            "changed original mesh invalidates prepared identity and preserves immediate fallback");
        HeldFigures.Actors.Clear();FigureGhosts.ReleaseIfUnheld(actor);
        replacementSkin.sharedMesh=initialMesh;FigureGhosts.Clear();yield return null;
        Check(firstPrepared==null&&FigureInteractionPreparation.PreparedCount==0,
            "scene reset releases every prepared visual and native source reference");
        Object.DestroyImmediate(replacementMesh);
        shape.sharedMaterial=plain;shape.SetBlendShapeWeight(0,35);original.SetTextureOffset("_Diffuse",Vector2.zero);
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
            Renderer nativeBody=source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh.vertexCount>1000);
            Renderer ghostBody=ghost!.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh==((SkinnedMeshRenderer)nativeBody).sharedMesh);
            Check(ghostBody.name==nativeBody.name&&!ModVisualOwnership.IsName(ghostBody.name)
                &&ghostBody.gameObject.layer==nativeBody.gameObject.layer&&ghostBody.gameObject.layer!=VRLayers.ModLayer,
                "genuine native-named/layer ghost child cannot rely on a prefix or mod layer");
            Check(!WallSegmentFade.OwnershipProbe.LiveOwned(nativeBody),"original native actor remains outside mod wall ownership");
            var ownership=new WallSegmentFade.OwnershipProbe();
            var ghostRow=ownership.Survey(ghostBody);
            Check(ghostRow.Mod&&ghostRow.Exempt&&ghostRow.ExemptRows==1&&ghostRow.Folded==0
                &&ghostRow.Scene==0&&ghostRow.Narrow==0&&ghostRow.Figures==0,
                "native-named ghost child keeps exact wall-census exemption");
            Check(WallSegmentFade.OwnershipProbe.LiveOwned(ghostBody),"native-named ghost child is never a live wall attachment");
            ghost.SetActive(false);
            ghostRow=ownership.Survey(ghostBody);
            Check(ghostRow.Mod&&ghostRow.Exempt&&ghostRow.Scene==0,
                "inactive native LOD descendants retain their genuine mirror owner");
            ghost.SetActive(true);
            var nativeRow=new WallSegmentFade.OwnershipProbe().Survey(nativeBody);
            Check(!nativeRow.Mod&&!nativeRow.Exempt&&nativeRow.Folded==1&&nativeRow.Scene!=0,
                "original native actor preserves the complete wall signature contribution");

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
            int ghostId=ghostBody.GetInstanceID();
            ownership.Survey(ghostBody);
            // Release now pools an inert prepared twin; reset is the genuine destruction edge.
            FigureInteractionPreparation.Reset();
            yield return null;
            var deadRow=ownership.Survey(ghostBody);
            Check(ghostBody==null&&deadRow.Exempt&&deadRow.ExemptRows==1&&deadRow.Folded==0
                &&deadRow.RowId==ghostId&&deadRow.Scene==0&&deadRow.Narrow==0&&deadRow.Figures==0,
                "destroyed native-named ghost row retains exact exemption");
        }
        var wallMaterial=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeWallFade.shader"));
        Renderer nativeWall=Surface("native original wall",quad,wallMaterial);nativeWall.transform.position=Vector3.right*40;
        GameObject wallCopy=FigureVisualMirror.CloneVisual(nativeWall.gameObject,Vector3.right*40,Quaternion.identity,Vector3.one,out _);
        Renderer wallRenderer=wallCopy.GetComponent<Renderer>();var wallOwnership=new WallSegmentFade.OwnershipProbe();
        var wallRow=wallOwnership.Survey(wallRenderer);
        Check(wallRow.Mod&&wallRow.WallFade&&wallRow.WallCount==1&&!wallRow.Exempt
            &&wallRow.Folded==1&&wallRow.Scene!=0,"real wall-shader mirror keeps conservative signature");
        Object.DestroyImmediate(wallCopy);wallRow=wallOwnership.Survey(wallRenderer);
        Check(!wallRow.Exempt&&wallRow.Folded==1&&wallRow.Scene!=0,
            "destroyed wall-shader mirror still changes structural signature");
        var waterMaterial=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/NativeWater_Shd.shader"));
        Renderer nativeWater=Surface("native original Fountain",quad,waterMaterial);nativeWater.transform.position=Vector3.right*40;
        var waterRow=new WallSegmentFade.OwnershipProbe().Survey(nativeWater);
        Check(!waterRow.Mod&&waterRow.WaterCount==1&&waterRow.Folded==1&&waterRow.Scene!=0,
            "native water keeps protection and conservative signature");
        Object.DestroyImmediate(nativeWater.gameObject);Object.DestroyImmediate(nativeWall.gameObject);
        // resources.assets native HexHighlight pathID5746 has the selector and emitter
        // on its root; a foreign emitter beneath the same root must retain native facts.
        var selectorRoot=new GameObject("HexHighlight native root topology");selectorRoot.transform.position=Vector3.right*40;
        selectorRoot.AddComponent<HexSelect_Control>();var rootSystem=selectorRoot.AddComponent<ParticleSystem>();
        var rootSelection=rootSystem.GetComponent<ParticleSystemRenderer>();var selectionOwnership=new WallSegmentFade.OwnershipProbe();
        var selectionRow=selectionOwnership.Survey(rootSelection);
        Check(selectionRow.Mod&&selectionRow.Exempt&&selectionRow.Folded==0
            &&selectionRow.Scene==0&&selectionRow.Narrow==0&&selectionRow.Figures==0,
            "exact native root selector emitter keeps its wall-census exemption");
        var foreignRoot=new GameObject("foreign child emitter");foreignRoot.transform.SetParent(selectorRoot.transform,false);
        var foreignSystem=foreignRoot.AddComponent<ParticleSystem>();var foreignSelection=foreignSystem.GetComponent<ParticleSystemRenderer>();
        var foreignRow=new WallSegmentFade.OwnershipProbe().Survey(foreignSelection);
        Check(!foreignRow.Mod&&!foreignRow.Exempt&&foreignRow.Folded==1&&foreignRow.Scene!=0,
            "foreign emitter beneath native selector remains a conservative wall-census input");
        Object.DestroyImmediate(selectorRoot);selectionRow=selectionOwnership.Survey(rootSelection);
        Check(selectionRow.Exempt&&selectionRow.Folded==0&&selectionRow.Scene==0,
            "destroyed native root selector keeps its exact signature exemption");
        // Same native FootstepSound topology: the returned pool instance is parented to an
        // animated foot beneath an actual ActorBehaviour. Exercise true Unity inactive/null
        // semantics and reparenting instead of admitting effects by name or Animator alone.
        var footActor=new GameObject("native foot owner");footActor.AddComponent<ActorBehaviour>();
        var foot=new GameObject("native animated foot");foot.transform.SetParent(footActor.transform,false);
        var pfx=new GameObject("pooled native footsteps");pfx.transform.SetParent(foot.transform,false);
        var particle=pfx.AddComponent<ParticleSystem>().GetComponent<ParticleSystemRenderer>();
        var particleOwnership=new WallSegmentFade.OwnershipProbe();
        var particleRow=particleOwnership.Survey(particle);
        Check(!particleRow.Mod&&particleRow.Exempt&&particleRow.Scene==0&&particleRow.Narrow==0&&particleRow.Figures==0,
            "native actor particles keep all wall signature halves unchanged");
        pfx.SetActive(false);particleRow=particleOwnership.Survey(particle);
        Check(particleRow.Exempt&&particleRow.Scene==0,"native pooled child activation never rebuilds its unrelated wall table");
        pfx.transform.SetParent(null,false);particleRow=particleOwnership.Survey(particle);
        Check(!particleRow.Exempt&&particleRow.Folded==1,"detached native particles restore conservative wall membership");
        pfx.transform.SetParent(foot.transform,false);footActor.SetActive(false);particleRow=particleOwnership.Survey(particle);
        Check(!particleRow.Exempt,"inactive native actor never grants new particle signature exemptions");
        footActor.SetActive(true);particle.sharedMaterial=waterMaterial;particleRow=new WallSegmentFade.OwnershipProbe().Survey(particle);
        Check(!particleRow.Exempt&&particleRow.WaterCount==1,"native actor water particles retain protection rects");
        particle.sharedMaterial=plain;particleOwnership=new WallSegmentFade.OwnershipProbe();particleOwnership.Survey(particle);
        Object.DestroyImmediate(pfx);particleRow=particleOwnership.Survey(particle);
        Check(particleRow.Exempt&&particleRow.Scene==0,"destroyed native actor particle retains the last live exact exemption");
        Object.DestroyImmediate(footActor);
        Renderer geometry=Surface("native wall geometry read",quad,plain);
        WallCommitGeometryReads.Begin(true);
        Bounds exact=geometry.bounds;
        for(int repeat=0;repeat<500;repeat++)
            Check(WallCommitGeometryReads.Read(geometry)==exact,"synchronous wall geometry reads retain exact native mesh bounds");
        Check(WallCommitGeometryReads.NativeReads==1&&WallCommitGeometryReads.ReusedReads==499,
            "five hundred repeated commit reads cross the native bounds boundary once");
        WallCommitGeometryReads.End();
        WallCommitGeometryReads.Begin(false);
        for(int i=0;i<20;i++) WallCommitGeometryReads.Read(geometry);
        Check(WallCommitGeometryReads.NativeReads==20 && WallCommitGeometryReads.ReusedReads==0 && WallCommitGeometryReads.RetainedCount==0,
            "disabled commit cache reports actual native reads without retaining geometry");
        WallCommitGeometryReads.End();geometry.transform.position=Vector3.up*3;
        Check(WallCommitGeometryReads.RetainedCount==0&&WallCommitGeometryReads.Read(geometry)==geometry.bounds,
            "publication close releases native geometry and immediately observes new frame movement");
        Object.DestroyImmediate(geometry.gameObject);
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
