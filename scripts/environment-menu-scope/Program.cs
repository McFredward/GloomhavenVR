using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using Object=UnityEngine.Object;

public static class EnvironmentScopeProgram
{
    private static readonly List<Object> Owned=new();private static int checks;
    private static AssetBundle? shipping;
    private static readonly FieldInfo RoomField=typeof(SkyAlternative).GetField("_roomGo",BindingFlags.NonPublic|BindingFlags.Static)!;
    private static readonly FieldInfo PrefabsField=typeof(SkyAlternative).GetField("Prefabs",BindingFlags.NonPublic|BindingFlags.Static)!;
    private static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    private static T Keep<T>(T value) where T:Object {Owned.Add(value);return value;}
    private static void Near(float actual,float expected,string why)=>Check(Mathf.Abs(actual-expected)<.002f,why+": "+actual+" versus "+expected);
    private static GameObject Room(){var room=(GameObject?)RoomField.GetValue(null);Check(room!=null,"selected custom room is visible in the main menu without a native board");return room!;}
    private static GameObject Cube(Transform parent,string name,Vector3 position,Vector3 size,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
        go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go;
    }
    private static GameObject Prefab(string name,Color color)
    {
        var prefab=Keep(new GameObject(name));prefab.SetActive(false);
        var room=new GameObject("RoomGeo");room.transform.SetParent(prefab.transform,false);
        var material=Keep(new Material(Shader.Find("Unlit/Color")));material.color=color;
        Cube(room.transform,"floor",new Vector3(0,-.05f,0),new Vector3(8,.1f,8),material);
        Cube(room.transform,"front",new Vector3(0,1.5f,4),new Vector3(8,3,.1f),material);
        Cube(room.transform,"left",new Vector3(-4,1.5f,0),new Vector3(.1f,3,8),material);
        Cube(room.transform,"right",new Vector3(4,1.5f,0),new Vector3(.1f,3,8),material);
        var marker=new GameObject("PlaySpace");marker.transform.SetParent(prefab.transform,false);marker.transform.localScale=Vector3.one*8;
        return prefab;
    }
    private static Transform Rig(Vector3 position,float scale,float yaw)
    {
        var root=Keep(new GameObject("fixture live rig")).transform;root.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));root.localScale=Vector3.one*scale;
        var head=new GameObject("fixture tracked head").AddComponent<Camera>();head.transform.SetParent(root,false);head.transform.localPosition=new Vector3(.4f,1.6f,-.3f);
        head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;head.cullingMask=VRLayers.ModLayerMask;
        head.nearClipPlane=.01f*scale;head.farClipPlane=1000*scale;head.fieldOfView=75;head.enabled=false;
        VRRigDriver.RigRoot=root;VRRigDriver.HeadCamera=head;VRRigDriver.RigPoseVersion++;return root;
    }
    private static int Capture(string path,Color expected,bool requireCenter=true)
    {
        var head=VRRigDriver.HeadCamera!;var target=new RenderTexture(320,240,24,RenderTextureFormat.ARGB32);target.Create();
        var read=new Texture2D(320,240,TextureFormat.RGBA32,false);var prior=RenderTexture.active;
        try
        {
            head.targetTexture=target;head.Render();RenderTexture.active=target;read.ReadPixels(new Rect(0,0,320,240),0,0);read.Apply(false,false);
            File.WriteAllBytes(path,read.EncodeToPNG());int colored=0;
            foreach(Color32 pixel in read.GetPixels32())if(pixel.r+pixel.g+pixel.b>30)colored++;
            Check(colored>10000,"selected menu environment contributes real head-camera pixels on the mod layer");
            Color center=read.GetPixel(160,120);if(requireCenter)Check(expected.r>expected.g?center.r>center.g+.1f:center.g>center.r+.1f,"live style changes show the selected menu room color");return colored;
        }
        finally{head.targetTexture=null;RenderTexture.active=prior;Object.DestroyImmediate(read);target.Release();Object.DestroyImmediate(target);}
    }
    private static float SurfaceY(GameObject room,string name)
    {
        foreach(var filter in room.GetComponentsInChildren<MeshFilter>(true))
            if(filter.name==name&&filter.sharedMesh!=null)
                return filter.transform.TransformPoint(filter.sharedMesh.bounds.center).y;
        throw new Exception("missing packaged central "+name+" surface");
    }
    private static void Witness(GameObject room,Color color)
    {
        // Only the graphics API is ported: the shipped Windows-only programs are
        // not GL executable. Keep every authored mesh/transform and its bounds.
        var material=Keep(new Material(Shader.Find("Unlit/Color")));material.color=color;
        foreach(var renderer in room.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)materials[i]=material;
            renderer.sharedMaterials=materials;
        }
    }
    public static IEnumerator Run(string output)
    {
        checks=0;VRSession.IsRunning=true;VRModeStateMachine.TableInFrontOfPlayer=false;MenuWorldContext.IsReady=false;MapRoomDriver.Active=false;
        SkyAlternative.BindConfig(new BepInEx.Configuration.ConfigFile());
        var prefabs=(GameObject?[])PrefabsField.GetValue(null)!;
        prefabs[1]=Prefab("fixture Cellar",new Color(.8f,.2f,.1f));prefabs[2]=Prefab("fixture Swamp",new Color(.1f,.7f,.25f));
        Transform rig=Rig(new Vector3(13,4,-9),1,37);var head=VRRigDriver.HeadCamera!;
        SkyAlternative.Style.Value=SkyStyle.Cellar;
        Check(!SkyAlternative.Tick()&&RoomField.GetValue(null)==null,"studio intro never reveals custom surroundings before the real menu is ready");
        Vector3 provisional=rig.position;
        head.transform.localPosition=Vector3.zero;rig.position+=new Vector3(0,1,0);
        Check(!SkyAlternative.Tick()&&RoomField.GetValue(null)==null,"untracked startup cannot seat a room at a provisional camera anchor");
        head.transform.localPosition=new Vector3(.4f,1.6f,-.3f);rig.position=provisional;
        VRRigDriver.RigPoseVersion++;
        Check(!SkyAlternative.Tick()&&RoomField.GetValue(null)==null,"completed recenter alone cannot reveal the environment behind the intro");
        MenuWorldContext.IsReady=true;
        Check(SkyAlternative.Tick(),"selected custom room is visible in the main menu without a native board");
        GameObject room=Room();Check(room.activeInHierarchy,"cold main-menu room completes its placement before reveal");
        Vector3 floor=rig.TransformPoint(new Vector3(.4f,0,-.3f));
        Near(room.transform.position.y,floor.y,"main-menu room floor matches tracking floor");
        Check(Vector3.Distance(new Vector3(room.transform.position.x,floor.y,room.transform.position.z),floor)<.002f,"main-menu room starts around the tracked floor position");
        Near(room.transform.localScale.x,4.5f*1.2f/8f,"menu room uses the fixed perceived reference footprint");
        Near(Quaternion.Angle(room.transform.rotation,rig.rotation),0,"menu room and sky use the fixed rig yaw");
        yield return null;
        Check(room.GetComponentsInChildren<Collider>(true).Length==0,"menu environment colliders cannot intercept native UI rays");
        int first=Capture(Path.Combine(output,"menu-cold.png"),new Color(.8f,.2f,.1f));
        Vector3 position=room.transform.position,scale=room.transform.localScale;Quaternion rotation=room.transform.rotation;
        for(int i=0;i<8;i++)
        {
            head.transform.localPosition=new Vector3(.4f+i*.025f,1.6f+(i%2)*.02f,-.3f+i*.02f);
            rig.position+=new Vector3(.01f,0,.005f);Check(SkyAlternative.Tick(),"menu room stays active during tracked and stick motion");
            Check(room==Room()&&room.transform.position==position&&room.transform.rotation==rotation&&room.transform.localScale==scale,"menu room remains world-fixed under head and rig motion");
            Capture(Path.Combine(output,"menu-motion-"+i+".png"),new Color(.8f,.2f,.1f));yield return null;
        }
        VRRigDriver.RigPoseVersion++;SkyAlternative.Tick();
        Check(room==Room()&&room.transform.position==position,"same-rig recenter never re-seats the occupied menu room");
        rig.localScale=Vector3.one*2;SkyAlternative.NotifyRigScaled(head.transform.position,1,2);SkyAlternative.Tick();
        Check(room.transform.position==position&&room.transform.localScale==scale,"zoom preserves the room's world geometry");
        Check(SkyAlternative.MinFarWorldUnits(2)>=200,"menu environment retains its full live far-plane budget");

        SkyAlternative.Style.Value=SkyStyle.SwampNight;SkyAlternative.Tick();GameObject swamp=Room();
        Check(!ReferenceEquals(swamp,room),"switching styles replaces the previous room");yield return null;
        Capture(Path.Combine(output,"menu-swamp.png"),new Color(.1f,.7f,.25f));

        // The original fixture had only tiny synthetic open rooms and missed the
        // actual cellar's closed ceiling/outdoor bounds and seated 1.01m pose.
        shipping=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("GHVR_SCOPE_ENVIRONMENT_BUNDLE"));
        Check(shipping!=null,"actual packaged environment bank loads in the game-exact Unity runtime");
        var realCellar=shipping!.LoadAsset<GameObject>("Assets/Bundle/Environments/Env_Cellar.prefab");
        var realSwamp=shipping.LoadAsset<GameObject>("Assets/Bundle/Environments/Env_Swamp.prefab");
        Check(realCellar!=null&&realSwamp!=null,"both actual shipped RoomGeo/PlaySpace prefabs resolve");
        prefabs[1]=realCellar;prefabs[2]=realSwamp;
        var oldRig=rig;rig=Rig(new Vector3(0,1,-10),1,0);head=VRRigDriver.HeadCamera!;
        Object.DestroyImmediate(oldRig.gameObject);head.transform.localPosition=Vector3.zero;
        MenuWorldContext.IsReady=false;
        Check(!SkyAlternative.Tick()&&RoomField.GetValue(null)==null,"packaged room is absent while the fresh startup rig has not recentered");
        head.transform.localPosition=new Vector3(.11f,1.01f,.10f);
        rig.position=new Vector3(-.11f,-.01f,-10.10f);VRRigDriver.RigPoseVersion++;
        MenuWorldContext.IsReady=true;SkyAlternative.Style.Value=SkyStyle.Cellar;
        Check(SkyAlternative.Tick(),"actual packaged cellar appears after native menu and recenter admission");
        var cellar=Room();float floorY=SurfaceY(cellar,"Floor"),ceilingY=SurfaceY(cellar,"Ceiling");
        Near(head.transform.position.y,(floorY+ceilingY)*.5f,"packaged cellar eye is vertically centered between its actual floor and ceiling");
        Check(head.transform.position.y-floorY>1.3f&&ceilingY-head.transform.position.y>1.3f,"seated hardware pose leaves generous real floor and ceiling clearance");
        Near(cellar.transform.position.x,head.transform.position.x,"packaged cellar starts horizontally centered at the ready eye X");
        Near(cellar.transform.position.z,head.transform.position.z,"packaged cellar starts horizontally centered at the ready eye Z");
        Near(cellar.transform.localScale.x,5.4f/6.5f,"actual cellar uses its shipped 6.5m PlaySpace rather than its outdoor art extent");
        Check(cellar.GetComponentsInChildren<Collider>(true).Length==0,"actual packaged geometry remains noninteractive to laser rays");
        Witness(cellar,new Color(.8f,.2f,.1f));yield return null;
        Capture(Path.Combine(output,"packaged-cellar-ready.png"),new Color(.8f,.2f,.1f));
        Vector3 fixedPosition=cellar.transform.position,fixedScale=cellar.transform.localScale;Quaternion fixedRotation=cellar.transform.rotation;
        for(int pose=0;pose<4;pose++)
        {
            head.transform.localPosition+=new Vector3(.02f,.015f,-.01f);rig.position+=new Vector3(.03f,.01f,.02f);
            SkyAlternative.Tick();Check(cellar.transform.position==fixedPosition&&cellar.transform.rotation==fixedRotation,"packaged cellar never follows the player after its one centered spawn");
            Capture(Path.Combine(output,"packaged-cellar-motion-"+pose+".png"),new Color(.8f,.2f,.1f));yield return null;
        }
        SkyAlternative.Style.Value=SkyStyle.SwampNight;SkyAlternative.Tick();var actualSwamp=Room();
        Near(actualSwamp.transform.localScale.x,5.4f/9f,"actual open-air room retains its shipped 9m clearing metric");
        Near(actualSwamp.transform.position.y,rig.TransformPoint(new Vector3(head.transform.localPosition.x,0,head.transform.localPosition.z)).y,"open-air menu room keeps the completed tracking floor without an invented ceiling");
        Witness(actualSwamp,new Color(.1f,.7f,.25f));yield return null;
        Capture(Path.Combine(output,"packaged-swamp-ready.png"),new Color(.1f,.7f,.25f),false);
        File.WriteAllText(Path.Combine(output,"packaged-placement.txt"),"readyEye="+head.transform.position+";cellarFloor="+floorY+";cellarCeiling="+ceilingY+";cellarPosition="+fixedPosition+";cellarScale="+fixedScale+"\nThe packaged meshes/transforms/PlaySpace execute unmodified; an explicit Unlit/Color GL witness replaces only Windows-authored room materials.\n");
        prefabs[1]=Prefab("fixture Cellar after shipping check",new Color(.8f,.2f,.1f));prefabs[2]=Prefab("fixture Swamp after shipping check",new Color(.1f,.7f,.25f));
        // Restore a synthetic menu room before retaining the original native
        // map/scenario arithmetic checks and their exact fixed-art reference.
        SkyAlternative.Style.Value=SkyStyle.Cellar;SkyAlternative.Tick();SkyAlternative.Style.Value=SkyStyle.SwampNight;SkyAlternative.Tick();swamp=Room();

        Transform priorRig=rig;rig=Rig(new Vector3(71,23,-41),200,18);Object.DestroyImmediate(priorRig.gameObject);
        VRModeStateMachine.TableInFrontOfPlayer=true;MapRoomDriver.Active=true;
        var parchment=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));parchment.name="native map parchment";parchment.transform.position=new Vector3(70,40,-20);parchment.transform.localScale=new Vector3(300,2,160);
        MapRoomDriver.ParchmentRenderer=parchment.GetComponent<MeshRenderer>();Bounds bounds=MapRoomDriver.ParchmentRenderer.bounds;
        SkyAlternative.Tick();GameObject mapRoom=Room();
        Check(!ReferenceEquals(mapRoom,swamp),"a replacement map rig rebuilds the menu room at the map's world metric");
        Near(mapRoom.transform.localScale.x,4.5f*300/8,"native map width keeps the existing room proportions");
        Near(mapRoom.transform.position.y,bounds.min.y-.75f*300,"native map floor keeps the original proportional float gap");
        Check(parchment.transform.position==new Vector3(70,40,-20)&&parchment.transform.localScale==new Vector3(300,2,160),"map surroundings never alter native parchment geometry");yield return null;

        priorRig=rig;rig=Rig(new Vector3(-31,-3,8),10,25);Object.DestroyImmediate(priorRig.gameObject);
        MapRoomDriver.Active=false;MapRoomDriver.ParchmentRenderer=null;Object.DestroyImmediate(parchment);
        var board=Keep(new GameObject("native scenario root"));board.transform.rotation=Quaternion.Euler(0,25,0);
        var tile=Cube(board.transform,"native tile",new Vector3(-10,0,0),new Vector3(2,.2f,2),Keep(new Material(Shader.Find("Unlit/Color"))));
        var second=Cube(board.transform,"native tile2",new Vector3(10,0,0),new Vector3(2,.2f,2),tile.GetComponent<Renderer>().sharedMaterial);
        Singleton<ObjectCacheService>.Instance.Tiles.Add(tile.AddComponent<TileBehaviour>());Singleton<ObjectCacheService>.Instance.Tiles.Add(second.AddComponent<TileBehaviour>());
        SkyAlternative.Tick();GameObject scenario=Room();Check(!ReferenceEquals(scenario,mapRoom),"a replacement scenario rig rebuilds the room rather than retaining map coordinates");
        float worldExtent=Mathf.Max(Mathf.Abs(second.transform.position.x-tile.transform.position.x),Mathf.Abs(second.transform.position.z-tile.transform.position.z))+2;
        Near(scenario.transform.localScale.x,4.5f*worldExtent/8,"native scenario tiles retain the existing world extent normalization");
        Near(scenario.transform.position.y,-.1f-.75f*worldExtent,"native scenario floor retains the existing underside and float gap");
        Check(tile.transform.localPosition==new Vector3(-10,0,0)&&second.transform.localPosition==new Vector3(10,0,0),"scenario surroundings never alter native tile positions");

        SkyAlternative.Style.Value=SkyStyle.OffBlack;Check(SkyAlternative.Tick(),"OffBlack retains environment ownership in every mode");
        Check(RoomField.GetValue(null)==null&&SkyAlternative.MinFarWorldUnits(10)==0,"OffBlack removes room geometry and its far-plane claim");
        SkyAlternative.Style.Value=SkyStyle.Default;Check(!SkyAlternative.Tick(),"Default returns sky ownership to the original-default presenter");
        SkyAlternative.Style.Value=SkyStyle.Cellar;SkyAlternative.Tick();SkyAlternative.StandDown();
        Check(RoomField.GetValue(null)==null&&SkyAlternative.MinFarWorldUnits(10)==0,"mixed-reality stand-down leaves no room geometry or budget");
        SkyAlternative.Tick();Check(RoomField.GetValue(null)!=null,"selected room resumes after mixed-reality ownership ends");
        VRSession.IsRunning=false;Check(!SkyAlternative.Tick()&&RoomField.GetValue(null)==null,"VR stop restores a geometry-free environment state");
        File.WriteAllText(Path.Combine(output,"limits.txt"),"The whole production SkyAlternative class executes with actual Unity transforms, instantiation, layers, graphics and deferred destruction. Native menu readiness/config/session/Haunt/audio/MR-owner boundaries are explicit ports. The actual packaged cellar and swamp geometry, PlaySpace and central floor/ceiling execute; their Windows-authored materials use an explicit unlit GL color witness. Synthetic rooms retain the existing map/scenario placement controls. This fixture does not certify native lifecycle admission, original shader appearance, Default sky, HMD timing, networking or audio. First colored pixels="+first);
        yield return checks;
    }
    public static void Cleanup()
    {
        SkyAlternative.RestoreAll();VRRigDriver.HeadCamera=null;VRRigDriver.RigRoot=null;
        foreach(var value in Owned)if(value!=null)Object.DestroyImmediate(value);Owned.Clear();
        Singleton<ObjectCacheService>.Instance.Tiles.Clear();
        var prefabs=(GameObject?[])PrefabsField.GetValue(null)!;Array.Clear(prefabs,0,prefabs.Length);
        if(shipping!=null)shipping.Unload(true);shipping=null;MenuWorldContext.IsReady=false;
    }
}
