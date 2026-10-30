using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using GloomhavenVR.WorldUI.MenuDepth;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Cards;
using Object=UnityEngine.Object;

public sealed class LaserSubmittedMesh : BaseMeshEffect
{
    internal Mesh Submitted=null!;
    protected override void Awake(){base.Awake();Submitted=new Mesh();}
    public override void ModifyMesh(VertexHelper vertices)=>vertices.FillMesh(Submitted);
    protected override void OnDestroy(){Object.DestroyImmediate(Submitted);base.OnDestroy();}
}

public sealed class LaserNativeCallbacks : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int Clicks,Downs,Ups,Enters,Exits,Drags;
    public void OnPointerClick(PointerEventData data)=>Clicks++;
    public void OnPointerDown(PointerEventData data)=>Downs++;
    public void OnPointerUp(PointerEventData data)=>Ups++;
    public void OnPointerEnter(PointerEventData data)=>Enters++;
    public void OnPointerExit(PointerEventData data)=>Exits++;
    public void OnBeginDrag(PointerEventData data) { }
    public void OnDrag(PointerEventData data)=>Drags++;
    public void OnEndDrag(PointerEventData data) { }
}

public static partial class MenuDepthLaserProgram
{
    private static int _checks;
    private static readonly List<Object> _assets=new();
    private static readonly Func<long> AllocationCount=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static)!);
    private static T Own<T>(T value) where T:Object {_assets.Add(value);return value;}
    private static void Check(bool condition,string message){_checks++;if(!condition)throw new Exception(message);}
    private static RectTransform Rect(string name,Transform? parent,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));go.layer=5;
        var rect=(RectTransform)go.transform;rect.SetParent(parent,false);rect.sizeDelta=size;return rect;
    }
    private static Image Picture(string name,Transform parent,Vector2 size,Color color)
    {var rect=Rect(name,parent,size);var image=rect.gameObject.AddComponent<Image>();image.color=color;return image;}
    private sealed class Scene : IDisposable
    {
        internal readonly Camera Head;
        internal readonly RectTransform Root;
        internal readonly Canvas Canvas;
        internal readonly RenderTexture Target;
        internal readonly VRHand Hand=new(){Side=HandSide.Right};
        internal readonly RayUguiDriver Driver;
        private readonly GameObject _eventSystem;
        internal Scene()
        {
            _eventSystem=new GameObject("native Unity EventSystem",typeof(EventSystem));
            Head=new GameObject("final head camera").AddComponent<Camera>();Head.enabled=false;
            Head.transform.position=new Vector3(0,0,-3);Head.transform.rotation=Quaternion.identity;
            Head.nearClipPlane=.02f;Head.farClipPlane=20;Head.fieldOfView=50;
            Head.clearFlags=CameraClearFlags.SolidColor;Head.backgroundColor=new Color(.1f,.2f,.3f,1f);Head.cullingMask=1<<5;
            Target=new RenderTexture(640,480,24,RenderTextureFormat.ARGB32);Target.Create();Head.targetTexture=Target;
            Root=Rect("original converted window",null,new Vector2(1000,800));Root.localScale=Vector3.one*.002f;
            Canvas=Root.gameObject.AddComponent<Canvas>();Canvas.renderMode=RenderMode.WorldSpace;Canvas.worldCamera=Head;
            Canvas.overrideSorting=true;Canvas.sortingOrder=100;Root.gameObject.AddComponent<GraphicRaycaster>();
            UguiPokeSurfaces.Surfaces.Add(Canvas);VRHands.Primary=Hand;Driver=new RayUguiDriver(Hand);
        }
        internal void Aim(Vector3 point)
        {Vector3 origin=Head.transform.position+Head.transform.right*.15f;Hand.Ray.Current=new PickPose(origin,(point-origin).normalized);}
        internal void Tick()=>Driver.Tick();
        internal void Draw(string path)
        {
            Canvas.ForceUpdateCanvases();Head.Render();RenderTexture previous=RenderTexture.active;RenderTexture.active=Target;
            var pixels=new Texture2D(Target.width,Target.height,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,Target.width,Target.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);RenderTexture.active=previous;
        }
        public void Dispose()
        {
            Driver.Cancel();UguiPokeSurfaces.Surfaces.Clear();UguiPokeSurfaces.Nested.Clear();VRHands.Primary=null;
            Object.DestroyImmediate(Root.gameObject);Head.targetTexture=null;Target.Release();Object.DestroyImmediate(Target);Object.DestroyImmediate(Head.gameObject);Object.DestroyImmediate(_eventSystem);
        }
    }
    public static IEnumerator Run(string output,Action<int> count)
    {
        _checks=0;
        var nativePorts=NativePorts();
        try
        {
        using(var scene=new Scene())
        {
            var card=Rect("original FullAbilityCard",scene.Root,new Vector2(300,400));
            var owner=card.gameObject.AddComponent<FullAbilityCard>();
            Image image=Picture("original print",card,card.rect.size,Color.red);
            var texture=Own(new Texture2D(16,16,TextureFormat.RGBA32,false));var colors=new Color32[256];
            for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,255,255,255);
            texture.SetPixels32(colors);texture.Apply();image.sprite=Own(Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f)));
            var callback=card.gameObject.AddComponent<LaserNativeCallbacks>();
            var plane=card.gameObject.AddComponent<MenuDepthCardPlane>();plane.Configure(owner,card,new[]{image},scene.Root);
            Canvas.ForceUpdateCanvases();yield return null;
            foreach(float yaw in new[]{0f,35f,-28f})
            {
                scene.Root.rotation=Quaternion.Euler(0,yaw,0);
                Vector3 want=card.TransformPoint(new Vector3(30,40,-31.2f));scene.Aim(want);
                Check(MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var hit),
                    "registered original full-card source intersects its actual raised plane");
                Check(Vector3.Distance(hit.WorldPoint,want)<.0001f,"laser point matches independent physical card-width relief before native callbacks");
                Check(Vector3.Distance(hit.NativePoint,card.TransformPoint(new Vector3(30,40,0)))<.0001f,"physical card hit maps back to the original native event coordinate");
                scene.Tick();Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,want)<.0001f,
                    "actual RayUguiDriver clamps the visible laser to spatial card geometry");
                Check(scene.Driver.Hovered!=null&&scene.Driver.Hovered.transform.IsChildOf(card),"actual native raycaster retains the original card event target");
                int clicks=callback.Clicks;scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();scene.Hand.TriggerDown=false;
                scene.Hand.TriggerPressed=false;scene.Tick();Check(callback.Clicks==clicks+1,"native original pointer press and release click exactly once on raised ink");
                plane.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));
                scene.Draw(Path.Combine(output,"card-yaw"+yaw+".png"));plane.AfterCapture();
            }
        }
        IEnumerator character=CharacterProof(output);
        while(character.MoveNext())yield return character.Current;
        if(Environment.GetEnvironmentVariable("GHVR_LASER_CASE")=="source-draw-throws") {count(_checks);yield break;}
        IEnumerator filters=FiltersAndNativeProof(output);
        while(filters.MoveNext())yield return filters.Current;
        IEnumerator story=StoryProof(output);
        while(story.MoveNext())yield return story.Current;
        IEnumerator native=NativeMeshProof(output);
        while(native.MoveNext())yield return native.Current;
        count(_checks);
        }
        finally {for(int i=_assets.Count-1;i>=0;i--)if(_assets[i]!=null)Object.DestroyImmediate(_assets[i]);_assets.Clear();nativePorts.UnpatchAll(nativePorts.Id);}
    }

    private static object Field(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
    private static IEnumerator FiltersAndNativeProof(string output)
    {
        using(var scene=new Scene())
        {
            var card=Rect("native read-only card",scene.Root,new Vector2(300,400));
            var group=card.gameObject.AddComponent<CanvasGroup>();
            var owner=card.gameObject.AddComponent<FullAbilityCard>();
            Image ink=Picture("native alpha ink",card,card.rect.size,Color.white);
            var tex=Own(new Texture2D(32,32,TextureFormat.RGBA32,false){name="laser-source-alpha-"+Guid.NewGuid()});
            var colors=new Color32[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++)colors[y*32+x]=new Color32(255,0,0,(byte)(x<16?0:255));
            tex.SetPixels32(colors);tex.Apply();var original=Own(Sprite.Create(tex,new Rect(8,0,16,32),new Vector2(.5f,.5f)));
            ink.sprite=CardFaceMipBake.PresentationFor(original);
            Check(ink.sprite!=original&&CardFaceMipBake.TryGetCachedAtlasPixels(tex,out var cached)&&cached!=null,"alpha comes from the existing real native mip-bake receipt");
            var plane=card.gameObject.AddComponent<MenuDepthCardPlane>();plane.Configure(owner,card,new[]{ink},scene.Root);
            var callback=card.gameObject.AddComponent<LaserNativeCallbacks>();
            Image behind=Picture("unrelated original button behind preview",scene.Root,card.rect.size,Color.blue);
            var behindEvents=behind.gameObject.AddComponent<LaserNativeCallbacks>();
            Canvas.ForceUpdateCanvases();yield return null;
            scene.Aim(card.TransformPoint(new Vector3(-80,0,-31.2f)));
            Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"transparent source texels leave real holes in the raised card silhouette");
            Vector3 physical=card.TransformPoint(new Vector3(80,0,-31.2f));scene.Aim(physical);scene.Tick();
            Check(scene.Driver.Hovered==ink.gameObject,"raised ink wins over unrelated native graphics sorted above its old proxy");
            group.blocksRaycasts=false;for(int i=0;i<=UguiPointer.ExitHysteresisFrames;i++)scene.Tick();
            Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,physical)<.0001f&&scene.Driver.Hovered==null,"visible read-only card retains physical occlusion without stealing the row behind it");
            int n=behindEvents.Clicks;scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();scene.Hand.TriggerDown=false;scene.Hand.TriggerPressed=false;scene.Tick();
            Check(callback.Clicks==0&&behindEvents.Clicks==n,"read-only 3D ink never invents a click or clicks through to the underlying native button");
            group.blocksRaycasts=true;
            ink.raycastTarget=false;for(int i=0;i<=UguiPointer.ExitHysteresisFrames;i++)scene.Tick();Check(scene.Driver.HasHit&&scene.Driver.Hovered==null,"original raycastTarget remains authoritative for a visible physical card");ink.raycastTarget=true;
            var pad=Picture("native poke-only child",card,card.rect.size,Color.clear);pad.gameObject.AddComponent<PokeOnlyTarget>();
            Canvas.ForceUpdateCanvases();scene.Tick();Check(scene.Driver.Hovered==ink.gameObject,"far-ray surface selection retains the native poke-only exclusion");Object.DestroyImmediate(pad.gameObject);
            var maskRect=Rect("native clip aperture",scene.Root,new Vector2(80,400));maskRect.gameObject.AddComponent<RectMask2D>();card.SetParent(maskRect,false);
            Canvas.ForceUpdateCanvases();scene.Aim(card.TransformPoint(new Vector3(80,0,-31.2f)));scene.Tick();
            Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"actual native RectMask2D clips the printed source at its mapped logical pixel");
            card.SetParent(scene.Root,false);Object.DestroyImmediate(maskRect.gameObject);Canvas.ForceUpdateCanvases();
            scene.Root.GetComponent<GraphicRaycaster>().enabled=false;for(int i=0;i<=UguiPointer.ExitHysteresisFrames;i++)scene.Tick();Check(scene.Driver.Hovered==null,"disabled original host raycaster cannot gain permission from 3D geometry");scene.Root.GetComponent<GraphicRaycaster>().enabled=true;
            ink.gameObject.SetActive(false);Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"hidden card source leaves no invisible geometry");ink.gameObject.SetActive(true);
            group.alpha=0;Canvas.ForceUpdateCanvases();Check(!MenuDepthSources.TryRaycast(scene.Canvas,scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"native zero-alpha groups do not retain a raised hit");group.alpha=1;
            Object.DestroyImmediate(card.gameObject);Object.DestroyImmediate(behind.gameObject);
            var modal=Rect("native confirmation physically seated at 32px",scene.Root,new Vector2(350,180));modal.localPosition=new Vector3(0,0,-32);
            Picture("original confirmation plate",modal,modal.rect.size,Color.gray);
            Image button=Picture("original confirmation YES",modal,new Vector2(100,60),Color.green);
            var nativeEvents=button.gameObject.AddComponent<LaserNativeCallbacks>();
            Canvas.ForceUpdateCanvases();yield return null;Vector3 want=button.transform.position;scene.Aim(want);scene.Tick();
            Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,want)<.0001f,"actual original 32px confirmation members supply their physical world plane");
            Check(scene.Driver.Hovered==button.gameObject,"native confirmation root preserves the original child button as top callback target; got "+(scene.Driver.Hovered!=null?scene.Driver.Hovered.name:"null")+"; button-depth="+button.depth);
            scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();scene.Hand.TriggerDown=false;
            scene.Aim(button.transform.TransformPoint(new Vector3(20,0,0)));scene.Tick();scene.Hand.TriggerPressed=false;scene.Tick();
            Check(nativeEvents.Downs==1&&nativeEvents.Ups==1&&nativeEvents.Clicks==1&&nativeEvents.Drags>0,"native confirmation retains one press drag release and click on its original button");
            scene.Draw(Path.Combine(output,"native-confirmation.png"));
            // A large, actually registered character menu is deliberately used
            // here: both rays must remain bounded even when their host-plane
            // miss requires checking raised members outside the native frame.
            for(int i=0;i<1500;i++){Image flat=Picture("native flat row "+i,scene.Root,new Vector2(8,8),Color.white);flat.rectTransform.anchoredPosition=new Vector2(300+i%50,200+i/50);}
            Canvas.ForceUpdateCanvases();yield return null;
            Vector3 hitOrigin=scene.Head.transform.position,hitDir=(want-hitOrigin).normalized;
            Vector3 missDir=(new Vector3(4,4,0)-hitOrigin).normalized;
            for(int i=0;i<5;i++){MenuDepthSources.TryNativeRaycast(scene.Canvas,scene.Canvas,hitOrigin,hitDir,20,out _);MenuDepthSources.TryNativeRaycast(scene.Canvas,scene.Canvas,hitOrigin,missDir,20,out _);}
            long allocated=AllocationCount();var clock=Stopwatch.StartNew();int hits=0;
            for(int i=0;i<100;i++){if(MenuDepthSources.TryNativeRaycast(scene.Canvas,scene.Canvas,hitOrigin,hitDir,20,out _))hits++;MenuDepthSources.TryNativeRaycast(scene.Canvas,scene.Canvas,hitOrigin,missDir,20,out _);}
            clock.Stop();allocated=AllocationCount()-allocated;
            File.WriteAllText(Path.Combine(output,"native-ui-query-cost.json"),"{\"registered_graphics\":"+GraphicRegistry.GetGraphicsForCanvas(scene.Canvas).Count+",\"hit_and_miss_pairs\":100,\"milliseconds\":"+clock.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"allocated_bytes\":"+allocated+"}");
            Check(hits==100&&allocated<=2048&&clock.Elapsed.TotalMilliseconds<500,"populated native UI hit and miss scans have bounded work with no per-ray managed allocations");
        }
    }

    private static IEnumerator StoryProof(string output)
    {
        using(var scene=new Scene())
        {
            Image image=Picture("original narrative art",scene.Root,new Vector2(600,400),Color.white);
            var tex=Own(new Texture2D(16,16,TextureFormat.RGBA32,false));var pixels=new Color32[256];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,120,0,255);tex.SetPixels32(pixels);tex.Apply();
            image.sprite=Own(Sprite.Create(tex,new Rect(0,0,16,16),new Vector2(.5f,.5f)));
            MenuDepthHeightfield field=MenuDepthHeightfield.Read(File.ReadAllBytes(Environment.GetEnvironmentVariable("GHVR_LASER_FIELD")!));
            var surface=image.gameObject.AddComponent<MenuDepthGraphic>();surface.Configure(image,field,.18f);
            var witness=image.gameObject.AddComponent<LaserSubmittedMesh>();
            Image advance=Picture("native invisible story advance",scene.Root,image.rectTransform.rect.size,Color.clear);var callback=advance.gameObject.AddComponent<LaserNativeCallbacks>();
            Canvas.ForceUpdateCanvases();yield return null;
            Mesh rendered=witness.Submitted;
            Vector3[] vertices=rendered.vertices;int[] indices=rendered.triangles;
            Check(vertices.Length>100&&indices.Length>300,"native story relief builds its actual displayed triangulated heightfield");
            int triangle=indices.Length/6*3;triangle-=triangle%3;
            Vector3 sample=(vertices[indices[triangle]]+vertices[indices[triangle+1]]+vertices[indices[triangle+2]])/3f;
            foreach(float yaw in new[]{0f,44f,-38f})
            {
                scene.Root.SetPositionAndRotation(new Vector3(.05f,.07f,.03f),Quaternion.Euler(12,yaw,3));scene.Root.localScale=new Vector3(.0022f,.0019f,.0025f);
                Vector3 want=image.transform.TransformPoint(sample);scene.Aim(want);
                Check(surface.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var hit),"story query intersects actual rendered relief geometry");
                Check(Vector3.Distance(hit.WorldPoint,want)<.0003f,"heightfield ray matches a barycentre read from the actual Unity CanvasRenderer mesh through rotated translated scaled hosts");
                scene.Tick();Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,want)<.0003f,"laser reaches narrative relief behind an invisible native advance target");
                Check(scene.Driver.Hovered==advance.gameObject,"narrative relief retains the real native story-advance input owner");
                surface.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));scene.Draw(Path.Combine(output,"story-yaw"+yaw+".png"));surface.AfterCapture();
            }
            // Original painted UI on the glass occludes art which really is
            // behind it; an input-only transparent target above did not.
            advance.color=Color.blue;Canvas.ForceUpdateCanvases();scene.Tick();
            Check(Mathf.Abs(scene.Root.InverseTransformPoint(scene.Hand.Ray.WorldUiHit).z)<.001f,"nearer painted native UI occludes narrative depth behind the glass");
            surface.enabled=false;advance.color=Color.clear;Canvas.ForceUpdateCanvases();scene.Tick();
            Check(Mathf.Abs(scene.Root.InverseTransformPoint(scene.Hand.Ray.WorldUiHit).z)<.001f,"disabled 3D effects retain the native flat-window fallback");
        }
    }

    private static IEnumerator NativeMeshProof(string output)
    {
        AssetBundle bundle=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("GHVR_LASER_NATIVE_BRUTE")!);
        Check(bundle!=null,"original native Brute bundle loads read-only in the actual Unity toolchain");
        try
        {
            Mesh nativeMesh=null!;var nativeNames=new List<string>();
            foreach(GameObject asset in bundle!.LoadAllAssets<GameObject>())
                foreach(SkinnedMeshRenderer renderer in asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh=renderer.sharedMesh;if(mesh==null)continue;nativeNames.Add(asset.name+"/"+renderer.name+":"+mesh.name+":"+mesh.vertexCount);
                    if(asset.name=="HE_Brute_PR"&&renderer.name=="HE_Brute_Mesh"&&mesh.vertexCount==9447)nativeMesh=mesh;
                }
            File.WriteAllLines(Path.Combine(output,"original-native-mesh-members.txt"),nativeNames);
            Check(nativeMesh!=null&&nativeMesh.vertexCount==9447&&nativeMesh.triangles.Length/3==13421,"actual imported Brute topology matches the root's independently audited native asset");
            using(var scene=new Scene())
            {
                var native=new GameObject("native body query context");var camera=native.AddComponent<Camera>();camera.transform.position=new Vector3(50,0,-5);camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<8;camera.fieldOfView=15;
                var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
                var holder=new GameObject("original model holder").transform;holder.SetParent(native.transform,false);holder.localPosition=new Vector3(0,0,5);
                var main=new GameObject("audited native visual root metric one").transform;main.SetParent(holder,false);
                var body=new GameObject("HE_Brute_Mesh").AddComponent<SkinnedMeshRenderer>();body.gameObject.layer=8;body.transform.SetParent(main,false);body.sharedMesh=nativeMesh;body.localBounds=nativeMesh!.bounds;
                var material=Own(new Material(Shader.Find("Unlit/Color")){color=Color.red});var materials=new Material[nativeMesh.subMeshCount];for(int i=0;i<materials.Length;i++)materials[i]=material;body.sharedMaterials=materials;
                Matrix4x4[] bindposes=nativeMesh.bindposes;var bones=new Transform[bindposes.Length];
                for(int i=0;i<bones.Length;i++){var bone=new GameObject("native bind-pose bone "+i).transform;bone.SetParent(main,false);Matrix4x4 pose=bindposes[i].inverse;bone.localPosition=pose.GetColumn(3);bone.localRotation=pose.rotation;bone.localScale=pose.lossyScale;bones[i]=bone;}
                body.bones=bones;body.rootBone=main;body.updateWhenOffscreen=true;
                var manager=native.AddComponent<Character3DDisplayManager>();manager.Bind(holder,main);
                var portrait=Rect("real native mesh portrait",scene.Root,new Vector2(600,720)).gameObject.AddComponent<RawImage>();portrait.texture=target;
                var source=new MenuDepthCharacterSource(portrait,camera,manager,holder,m=>m.Hidden);MenuDepthSources.Register(source);
                try
                {
                    Canvas.ForceUpdateCanvases();yield return null;
                    source.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));scene.Draw(Path.Combine(output,"actual-native-brute.png"));source.AfterCapture();
                    Check(source.RayVisibleMembers==1,"actual source draw observes the original native Brute skin");
                    var view=(MenuDepthCharacterProjection.View)Field(Field(source,"_surface"),"_view");Matrix4x4 nativeToPhysical=portrait.transform.localToWorldMatrix*view.NativeToGraphic;
                    var oracleMesh=Own(new Mesh());body.BakeMesh(oracleMesh,false);var collider=main.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=oracleMesh;Physics.SyncTransforms();
                    Vector3 center=body.bounds.center;scene.Aim(nativeToPhysical.MultiplyPoint3x4(center));Matrix4x4 inv=nativeToPhysical.inverse;var ray=new Ray(inv.MultiplyPoint3x4(scene.Hand.Ray.Current.Origin),inv.MultiplyVector(scene.Hand.Ray.Current.Direction).normalized);
                    Check(collider.Raycast(ray,out RaycastHit oracle,100),"independently baked native mesh collider receives the aimed Brute body ray");
                    int initialBakes=source.RayBakes;Check(source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var hit),"actual Brute skin triangle query resolves current native geometry");
                    Check(Vector3.Distance(hit.WorldPoint,nativeToPhysical.MultiplyPoint3x4(oracle.point))<.0003f,"native Brute query agrees with independent Unity collider at the exact physical visible surface");
                    for(int i=0;i<4;i++)source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _);
                    long triangles=source.RayTriangles;long allocated=AllocationCount();var watch=Stopwatch.StartNew();
                    for(int i=0;i<60;i++)Check(source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"two-hand shared native mesh hit remains live");
                    watch.Stop();allocated=AllocationCount()-allocated;
                    long tested=source.RayTriangles-triangles;
                    Check(source.RayBakes==initialBakes+1,"both hands share at most one potentially-hit skin bake in the same input frame");
                    Check(tested==60*13421&&allocated<=2048&&watch.Elapsed.TotalMilliseconds<1000,"actual 13421-triangle skin queries remain bounded and allocate no managed per-ray data");
                    int bakes=source.RayBakes;triangles=source.RayTriangles;
                    for(int i=0;i<120;i++)source.TryRaycast(scene.Head.transform.position,Vector3.up,20,out _);
                    Check(source.RayBakes==bakes&&source.RayTriangles==triangles,"both hands missing the visible native footprint do not bake skins or walk triangles");
                    File.WriteAllText(Path.Combine(output,"native-body-query-cost.json"),"{\"mesh\":\"HE_Brute_Mesh\",\"vertices\":9447,\"triangles_per_hit\":13421,\"hit_queries\":60,\"miss_queries\":120,\"milliseconds\":"+watch.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"allocated_bytes\":"+allocated+",\"bakes_for_shared_frame\":"+(bakes-initialBakes)+"}");
                }
                finally {source.Dispose();MenuDepthSources.Unregister(source);camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(native);}
            }
        }
        finally {bundle!.Unload(true);}
    }
    private static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 scale,Color color)
    {
        GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.layer=8;go.transform.SetParent(parent,false);
        go.transform.localPosition=position;go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=Own(new Material(Shader.Find("Unlit/Color")){color=color});return go;
    }
    private static IEnumerator CharacterProof(string output)
    {
        using(var scene=new Scene())
        {
            var native=new GameObject("original native character camera");var camera=native.AddComponent<Camera>();
            camera.transform.position=new Vector3(50,0,-5);camera.nearClipPlane=.01f;camera.farClipPlane=100;
            camera.cullingMask=1<<8;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.fieldOfView=15;
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var holder=new GameObject("original native model holder").transform;holder.SetParent(native.transform,false);holder.localPosition=new Vector3(0,0,5);
            var main=new GameObject("original main ground").transform;main.SetParent(holder,false);
            GameObject first=Cube("original LOD0",main,new Vector3(0,1,0),new Vector3(1,2,.8f),Color.red);
            GameObject second=Cube("original LOD1",main,new Vector3(0,1,0),new Vector3(.8f,2,.4f),Color.green);
            var lod=main.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.6f,new[]{first.GetComponent<Renderer>()}),new LOD(.01f,new[]{second.GetComponent<Renderer>()})});lod.RecalculateBounds();
            var manager=native.AddComponent<Character3DDisplayManager>();manager.Bind(holder,main);
            var portrait=Rect("original native RawImage",scene.Root,new Vector2(600,720)).gameObject.AddComponent<RawImage>();portrait.texture=target;
            portrait.gameObject.AddComponent<LaserNativeCallbacks>();
            var source=new MenuDepthCharacterSource(portrait,camera,manager,holder,m=>m.Hidden);MenuDepthSources.Register(source);
            var foreign=new GameObject("unrelated native mono LOD camera").AddComponent<Camera>();foreign.enabled=false;
            foreign.transform.position=new Vector3(50,1,-10);foreign.fieldOfView=60;foreign.cullingMask=1<<8;foreign.nearClipPlane=.02f;foreign.farClipPlane=100;foreign.targetTexture=target;
            Camera.CameraCallback unrelated=seen=>{if(seen==camera&&(bool)Field(source,"_rayCollecting"))foreign.Render();};
            Camera.onPreCull+=unrelated;
            try
            {
                Vector3 origin=scene.Head.transform.position+Vector3.right*.15f;
                Check(!source.TryRaycast(origin,Vector3.forward,20,out _),"character ray geometry requires a completed actual source-camera draw");
                float yaw=0;MenuDepthCharacterSource.YawProvider=(image,identity)=>yaw;
                foreach(float angle in new[]{0f,70f,180f})
                {
                    yaw=angle;
                    scene.Root.rotation=Quaternion.Euler(0,angle==180f?180f:0f,0);
                    var eye=new MenuDepthEye(false,scene.Head.transform.position,scene.Head);
                    source.BeforeCapture(eye);scene.Draw(Path.Combine(output,"character-yaw"+angle+".png"));source.AfterCapture();
                    if(Environment.GetEnvironmentVariable("GHVR_LASER_CASE")=="source-draw-throws")
                    {
                        Check(source.RayVisibleMembers==1,"throw control begins with a completed real original source draw");
                        bool threw=false;try {source.BeforeCapture(eye);}catch(InvalidOperationException error){threw=error.Message=="fixture source draw failed";}
                        Check(threw,"fault control injects a failure at the actual original Camera.Render boundary");
                        Check(source.RayReceiptFrame<0&&!source.TryRaycast(origin,Vector3.forward,20,out _),"failed original draws invalidate the completed source receipt instead of leaving stale physical geometry");
                        Check(!(bool)Field(source,"_rayCollecting")&&((HashSet<Renderer>)Field(source,"_rayPending")).Count==0,"failed original draws release their observer lease and pending membership");
                        Check(Vector3.Distance(camera.transform.position,new Vector3(50,0,-5))<.00001f,"failed original draw restores the original native camera position");
                        yield break;
                    }
                    Check(source.RayVisibleMembers==1,"actual source-camera receipts select exactly the native visible Unity LOD member");
                    var members=(HashSet<Renderer>)Field(source,"_rayMembers");Renderer visible=null!;
                    foreach(Renderer renderer in members)visible=renderer;
                    var surface=Field(source,"_surface");var view=(MenuDepthCharacterProjection.View)Field(surface,"_view");
                    Matrix4x4 nativeToPhysical=portrait.transform.localToWorldMatrix*view.NativeToGraphic;
                    Vector3 physicalCenter=nativeToPhysical.MultiplyPoint3x4(visible.bounds.center);scene.Aim(physicalCenter);
                    Matrix4x4 inverse=nativeToPhysical.inverse;
                    var nativeRay=new Ray(inverse.MultiplyPoint3x4(scene.Hand.Ray.Current.Origin),inverse.MultiplyVector(scene.Hand.Ray.Current.Direction).normalized);
                    Physics.SyncTransforms();Check(visible.GetComponent<Collider>().Raycast(nativeRay,out RaycastHit oracle,100),"independent Unity collider observes the displayed native cube surface");
                    Vector3 expected=nativeToPhysical.MultiplyPoint3x4(oracle.point);
                    Check(source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out var hit),"current native model mesh is intersected through the source's actual presentation");
                    Check(Vector3.Distance(hit.WorldPoint,expected)<.0002f,"character triangle hit matches independent Unity geometry under front side rear and presentation yaw");
                    scene.Tick();Check(scene.Driver.HasHit&&Vector3.Distance(scene.Hand.Ray.WorldUiHit,expected)<.0002f,
                        "actual laser driver stops on visible character geometry instead of its RawImage glass");
                    int before=source.RayVisibleMembers;camera.Render();Check(source.RayVisibleMembers==before,"unrelated native mono draw cannot overwrite the source's actual LOD receipt");
                    Check(((HashSet<Renderer>)Field(source,"_rayPending")).Count==0,"unleased original-camera draws never enter a pending source membership receipt");
                }
                manager.Replace(main);
                source.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));source.AfterCapture();
                manager.Replace(main);
                source.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));source.AfterCapture();
                yield return null;
                source.BeforeCapture(new MenuDepthEye(false,scene.Head.transform.position,scene.Head));source.AfterCapture();
                Check(source.RayVisibleMembers==1,"same-frame pooled original replacement retains a live witness after deferred destruction");
                manager.Hidden=true;Check(!source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"native-hidden portraits never retain stale 3D laser geometry");manager.Hidden=false;
                yield return null;yield return null;
                Check(!source.TryRaycast(scene.Hand.Ray.Current.Origin,scene.Hand.Ray.Current.Direction,20,out _),"source geometry expires when its actual capture stops drawing");
            }
            finally
            {
                source.Dispose();MenuDepthSources.Unregister(source);MenuDepthCharacterSource.YawProvider=null;
                Camera.onPreCull-=unrelated;foreign.targetTexture=null;Object.DestroyImmediate(foreign.gameObject);
                camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(native);
            }
        }
    }
}
