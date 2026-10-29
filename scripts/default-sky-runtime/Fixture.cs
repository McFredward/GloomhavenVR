using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class DefaultSkyFixture
{
    private static int _checks;
    private static readonly BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    private static void Check(bool condition,string label)
    {if(!condition)throw new InvalidOperationException(label);_checks++;}
    private static void Put(Type type,string name,object? value)=>type.GetField(name,Flags)!.SetValue(null,value);
    private static T? Get<T>(Type type,string name)where T:class=>type.GetField(name,Flags)!.GetValue(null)as T;
    private static Camera Head(Transform rig)
    {
        var go=new GameObject("actual head",typeof(Camera));go.transform.SetParent(rig,false);
        var head=go.GetComponent<Camera>();head.enabled=false;head.nearClipPlane=.05f;head.farClipPlane=100;
        head.fieldOfView=70;head.aspect=1;head.cullingMask=1<<VRLayers.ModLayer;head.clearFlags=CameraClearFlags.SolidColor;
        head.backgroundColor=Color.magenta;return head;
    }
    private static Texture2D Capture(Camera head,string file)
    {
        var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32){antiAliasing=1};target.Create();
        var saved=head.targetTexture;var active=RenderTexture.active;head.targetTexture=target;
        head.Render();RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
        image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();head.targetTexture=saved;RenderTexture.active=active;
        target.Release();Object.DestroyImmediate(target);if(file.Length>0)File.WriteAllBytes(file,image.EncodeToPNG());return image;
    }
    private static int ColorPixels(Texture2D image,Color expected)
    {
        var match=(Color32)expected;int count=0;
        foreach(var pixel in image.GetPixels32())if(Math.Abs(pixel.r-match.r)<3&&Math.Abs(pixel.g-match.g)<3&&Math.Abs(pixel.b-match.b)<3)count++;
        return count;
    }
    private static void InstallWitness(Mesh mesh,Material original,AssetBundle bank,Shader witness)
    {
        // One explicit graphics-API port: original native Windows programs cannot
        // execute in the Linux GL editor. Geometry, encoded textures, production
        // lifecycle, actual queue/depth reset and final pixels otherwise execute.
        var material=new Material(witness);material.SetTexture("_Map00",original.GetTexture("_Map00"));
        Put(typeof(NativeDefaultSky),"_mesh",mesh);Put(typeof(NativeDefaultSky),"_sourceMaterial",material);
        Put(typeof(NativeDefaultSky),"_bank",bank);Put(typeof(NativeDefaultSky),"_failed",false);
    }
    public static IEnumerator Run(string output)
    {
        var rig=new GameObject("menu rig").transform;var head=Head(rig);VRRigDriver.RigRoot=rig;VRRigDriver.HeadCamera=head;
        var witness=Shader.Find("AMP_SkyShader");Check(witness!=null,"explicit GL witness shader is available");
        // The real resource path executes first. An unsupported original shader
        // is a qualified API limit and must never silently become pink art.
        Renderer? originalAttempt=NativeDefaultSky.Acquire();
        if(originalAttempt!=null)NativeDefaultSky.Reset();
        else Check(VRLog.Lines.Count==1&&VRLog.Lines[0].Contains("Default sky unavailable"),"unsupported native API has one useful failure report");
        using var input=typeof(DefaultSkyFixture).Assembly.GetManifestResourceStream(NativeDefaultSky.Resource)!;
        using var bytes=new MemoryStream();input.CopyTo(bytes);var bank=AssetBundle.LoadFromMemory(bytes.ToArray());
        Check(bank!=null,"actual packaged native sky bank loads in the game-exact editor");
        var mesh=bank!.LoadAsset<Mesh>("native/default-sky/mesh");var material=bank.LoadAsset<Material>("native/default-sky/material");
        File.WriteAllText(Path.Combine(output,"native-assets.txt"),string.Join("\n",bank.GetAllAssetNames())+"\nmesh="+(mesh==null?"null":mesh.name+" vertices="+mesh.vertexCount)+"\n");
        Check(mesh!=null&&mesh.name=="GH_SkySphere"&&mesh.vertexCount>100,"actual original native sky mesh resolves without a scenario");
        Check(material!=null&&material.name=="BG_SkyClouds 2"&&material.shader.name=="AMP_SkyShader","actual original native material and shader identities resolve");
        Check(material!.GetTexture("_Map00").width==4096&&material.GetTexture("_Map2").width==512,"original native streamed cloud and noise textures resolve");
        File.AppendAllText(Path.Combine(output,"native-assets.txt"),"api="+SystemInfo.graphicsDeviceType+"; originalShaderSupported="+material.shader.isSupported+"; originalColdAcquisition="+(originalAttempt!=null)+"\n");
        var sourceTexture=material.GetTexture("_Map00");InstallWitness(mesh!,material,bank,witness!);
        MenuWorldContext.IsReady=false;
        Check(NativeDefaultSky.Acquire()==null,"direct default acquisition cannot reveal surroundings before the actual menu is ready");
        SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer==null&&Get<Renderer>(typeof(SkyBackdrop),"_sky")==null,"studio intro retains a geometry-free default surroundings state");
        var intro=Capture(head,Path.Combine(output,"intro-no-default.png"));
        Check(ColorPixels(intro,Color.magenta)==256*256,"no default environment contributes pixels during the intro");Object.DestroyImmediate(intro);
        MenuWorldContext.IsReady=true;
        SkyBackdrop.Tick(false);var cold=NativeDefaultSky.Renderer;
        Check(cold!=null&&cold.enabled&&cold.gameObject.layer==26,"cold main menu acquires its original default sky without a native scene");
        Check(cold!.GetComponents<Component>().Length==3,"default sky has render components only without native controllers or callbacks");
        Check(Get<Renderer>(typeof(SkyBackdrop),"_sky")==cold,"backdrop and fallback own the same exact renderer");
        Check(cold.sharedMaterial.GetTexture("_Map00")==sourceTexture,"private sky material preserves original native texture identity");
        Check(cold.sharedMaterial.renderQueue==1000,"native sky color precedes foreground geometry");
        Check(NativeDefaultSky.MinFarWorldUnits()>0f,"live private Default geometry owns a finite clip budget");
        var initial=Capture(head,Path.Combine(output,"cold-menu-default.png"));
        Check(ColorPixels(initial,Color.magenta)==0,"cold menu sky fills the head raster instead of exposing the clear color");
        Object.DestroyImmediate(initial);
        var cross=MenuBackdropProbe.Run(head,output);while(cross.MoveNext())yield return cross.Current;_checks+=MenuBackdropProbe.Assertions;cold=NativeDefaultSky.Renderer!;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Quad);cube.name="native foreground at original floor queue";cube.layer=26;
        var ink=new Material(Shader.Find("DefaultSkyFixture/Foreground"));ink.color=Color.red;ink.renderQueue=1900;
        cube.GetComponent<Renderer>().sharedMaterial=ink;cube.transform.localScale=Vector3.one*18;
        Matrix4x4 fixedBubble=cold.transform.localToWorldMatrix;
        float travel=cold.bounds.extents.x*.03f;
        for(int pose=0;pose<8;pose++)
        {
            head.transform.position=new Vector3(pose*travel,Mathf.Sin(pose)*travel,pose*-travel);
            head.transform.rotation=Quaternion.Euler(pose*5-15,pose*19,0);head.farClipPlane=pose%2==0?100:220;
            cube.transform.localScale=Vector3.one*(head.farClipPlane*.18f);
            SkyBackdrop.Tick(false);cube.transform.SetPositionAndRotation(head.transform.position+head.transform.forward*(head.farClipPlane*.9f),head.transform.rotation);
            Check(cold.transform.localToWorldMatrix.Equals(fixedBubble),"default bubble world geometry remains fixed through head motion and far-plane changes");
            var image=Capture(head,pose==0||pose==7?Path.Combine(output,"moving-default-"+pose+".png"):"");
            Check(ColorPixels(image,Color.red)>300,"default sky depth reset preserves original foreground pixels during head motion");
            Check(ColorPixels(image,Color.magenta)==0,"default sky remains fully visible through motion and far-plane changes");
            Check(NativeDefaultSky.Renderer==cold,"head motion never rebuilds the selected default sky");Object.DestroyImmediate(image);
            yield return null;
        }
        Object.Destroy(cube);Object.Destroy(ink);yield return null;
        var finite=FiniteBubbleProbe.Run(head,cold,output);while(finite.MoveNext())yield return finite.Current;_checks+=FiniteBubbleProbe.Assertions;
        MenuWorldContext.IsReady=false;SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer==null&&Get<Renderer>(typeof(SkyBackdrop),"_sky")==null,"loss of ready menu admission removes an already acquired default environment");
        yield return null;
        var hiddenAgain=Capture(head,Path.Combine(output,"not-ready-no-default.png"));
        Check(ColorPixels(hiddenAgain,Color.magenta)==256*256,"retired default geometry leaves no not-ready head pixels");Object.DestroyImmediate(hiddenAgain);
        MenuWorldContext.IsReady=true;SkyBackdrop.Tick(false);cold=NativeDefaultSky.Renderer;
        Check(cold!=null,"actual menu readiness can reacquire default after a complete admission transition");
        // A new real rig is distinct from motion on the same rig.
        var mapRig=new GameObject("map rig").transform;
        mapRig.position=new Vector3(-184.45f,-154.55f,.18f);mapRig.localScale=Vector3.one*198.12f;
        var mapHead=Head(mapRig);mapHead.transform.localPosition=new Vector3(0,.905f,0);mapHead.farClipPlane=1000f;
        VRRigDriver.RigRoot=mapRig;VRRigDriver.HeadCamera=mapHead;VRRigDriver.BaseWorldScale=198.12f;
        _=new VRRigDriver(mapHead,mapRig,1000f,198.12f);
        SkyBackdrop.Tick(false);var map=NativeDefaultSky.Renderer;
        Check(map!=null&&!ReferenceEquals(map,cold)&&!cold!.gameObject.activeSelf,"real menu-to-map rig replacement retires the old default presentation");
        int beforeMapClip=FiniteBubbleProbe.Assertions;
        var mapClip=FiniteBubbleProbe.MapClip(mapHead,mapRig,map!,output);while(mapClip.MoveNext())yield return mapClip.Current;
        _checks+=FiniteBubbleProbe.Assertions-beforeMapClip;
        Check(mapHead.cullingMask==(1<<26)&&mapHead.nearClipPlane==.5f,"default sky acquisition retains the actual clip policy near clamp and head mask");
        var foreignHead=Head(mapRig);foreignHead.farClipPlane=777f;mapHead.farClipPlane=73f;
        VRRigDriver.RefreshDefaultSkyClipPlanes(foreignHead);
        Check(foreignHead.farClipPlane==777f&&mapHead.farClipPlane==73f,"foreign acquisition heads cannot refresh another live rig clip policy");
        VRRigDriver.HeadCamera=foreignHead;VRRigDriver.RefreshDefaultSkyClipPlanes(mapHead);
        Check(mapHead.farClipPlane==73f,"a replaced head cannot refresh a stale rig instance camera");
        VRRigDriver.HeadCamera=mapHead;VRRigDriver.Instance!.TickClipProbe();Object.Destroy(foreignHead.gameObject);
        var hidden=GameObject.CreatePrimitive(PrimitiveType.Sphere);hidden.name="GH_SkySphere_invisible_native";
        Check(SkyBackdrop.FindSky()==hidden.GetComponent<Renderer>(),"native hiding queries remain independent of the head visibility mask");
        for(int i=0;i<62;i++){SkyBackdrop.Tick(false);yield return null;}
        Check(NativeDefaultSky.Renderer==map,"native sky outside the head mask cannot suppress a visible map default sky");
        Object.DestroyImmediate(hidden);mapHead.cullingMask|=1;SkyBackdrop.Tick(false);
        SceneManager.LoadScene("SkyArrival",LoadSceneMode.Additive);yield return null;
        Renderer? nativeRenderer=null;
        foreach(var candidate in Object.FindObjectsOfType<Renderer>())if(candidate.gameObject.name=="GH_SkySphere")nativeRenderer=candidate;
        Check(nativeRenderer!=null,"actual serialized native arrival scene loaded");var nativeMaterial=nativeRenderer!.sharedMaterial;
        SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer==null&&Get<Renderer>(typeof(SkyBackdrop),"_sky")==nativeRenderer,"late native scenario sky takes authority without a second generated dome");
        Check(NativeDefaultSky.MinFarWorldUnits()==0f,"original scenario authority retains a zero private Default budget");
        var idleClip=new VRRigDriver(mapHead,mapRig,1000f,198.12f);idleClip.TickClipProbe();
        Check(mapHead.farClipPlane==1000f,"actual clip consumer leaves the accepted native scenario far policy unchanged");
        Matrix4x4 nativePose=nativeRenderer.transform.localToWorldMatrix;
        mapHead.transform.position+=new Vector3(200,30,100);SkyBackdrop.Tick(false);
        Check(nativeRenderer.transform.localToWorldMatrix.Equals(nativePose),"player motion never rewrites the accepted native scenario sky geometry");
        mapHead.cullingMask=1<<26;SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer!=null,"cached native sky gives way when the menu head cannot render its layer");
        mapHead.cullingMask|=1;SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer==null&&Get<Renderer>(typeof(SkyBackdrop),"_sky")==nativeRenderer,"native sky resumes immediately when the same head makes its layer visible");
        SkyBackdrop.Tick(true);Check(nativeMaterial.renderQueue==2111&&nativeRenderer.enabled,"mixed reality restores native sky material without changing its renderer permission");
        var unload=SceneManager.UnloadSceneAsync("SkyArrival");while(unload!=null&&!unload.isDone)yield return null;
        SkyBackdrop.Tick(false);Check(NativeDefaultSky.Renderer!=null,"return to map without a native sky immediately reacquires Default");
        SkyBackdrop.Tick(true);Check(NativeDefaultSky.Renderer==null,"mixed reality and non-default styles remove the generated default sky");
        Check(NativeDefaultSky.MinFarWorldUnits()==0f,"mixed reality and other styles retain a zero private Default clip budget");
        idleClip.TickClipProbe();Check(mapHead.farClipPlane==1000f,"actual clip consumer restores its native budget for mixed reality and other styles");
        SkyBackdrop.Tick(false);Check(NativeDefaultSky.Renderer!=null,"default resumes after independent sky ownership ends");
        VRSession.IsRunning=false;SkyBackdrop.Tick(false);
        Check(NativeDefaultSky.Renderer==null&&Get<AssetBundle>(typeof(NativeDefaultSky),"_bank")==null,"VR exit releases owned default geometry and native bank assets");
        Check(NativeDefaultSky.MinFarWorldUnits()==0f,"VR shutdown retains a zero private Default clip budget");
        idleClip.TickClipProbe();Check(mapHead.farClipPlane==1000f,"actual clip consumer retains its native budget after VR shutdown");
        Object.Destroy(rig.gameObject);Object.Destroy(mapRig.gameObject);yield return null;
        File.WriteAllText(Path.Combine(output,"result.txt"),"PASS default sky: "+_checks+" assertions; actual native bank and production lifecycle/depth rendering; GL shader witness is explicit.\n");
    }
}
