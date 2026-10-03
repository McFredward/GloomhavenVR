using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

public static class InstancingProgram
{
    private static int count;
    private static bool on;
    private static void Check(bool ok, string message)
    { count++; if (!ok) throw new InvalidOperationException(message); }
    private static object Driver => typeof(ScenarioStructuralInstancing).GetField("_driver", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Tick() => Driver.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Driver,null);
    private static GameObject Child(string name, Transform parent)
    { var child=new GameObject(name); child.transform.SetParent(parent,false); return child; }
    private static MeshRenderer Surface(Transform parent, Mesh mesh, Material material, float x)
    {
        var child=Child("Native surface",parent);child.transform.localPosition=Vector3.right*x;
        child.AddComponent<MeshFilter>().sharedMesh=mesh; child.AddComponent<BoxCollider>();
        MeshRenderer renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;return renderer;
    }
    private static Color32[] Picture(Camera camera)
    {
        RenderTexture old=RenderTexture.active;camera.Render();RenderTexture.active=camera.targetTexture;
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,128,128),0,0);texture.Apply();
        Color32[] pixels=texture.GetPixels32();UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=old;return pixels;
    }
    public static int Run()
    {
        Check(SystemInfo.supportsInstancing,"actual GL graphics device supports instancing");
        VRSession.IsRunning=true;on=false;
        var scenario=new GameObject("Native scenario");scenario.AddComponent<ProceduralScenario>();
        var tile=Child("Native tile",scenario.transform);tile.AddComponent<ProceduralMapTile>();
        var generated=Child("Generated Content",tile.transform);
        var host=new GameObject("GloomhavenVR.InstancingProbe");ScenarioStructuralInstancing.Install(host,()=>on);
        var mesh=new Mesh { name="Native test masonry" };
        mesh.vertices=new[]{new Vector3(-.4f,-.4f,0),new Vector3(.4f,-.4f,0),new Vector3(.4f,.4f,0),new Vector3(-.4f,.4f,0)};
        mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
        var original=new Material(Shader.Find("Amp_Basic_N_MRAO"));original.enableInstancing=false;
        var a=Surface(generated.transform,mesh,original,-.6f);var b=Surface(generated.transform,mesh,original,.6f);
        var camera=new GameObject("Graphics fixture").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.5f;
        camera.transform.position=new Vector3(0,0,-5);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.targetTexture=new RenderTexture(128,128,24);
        Color32[] before=Picture(camera);
        int visible=0;foreach(Color32 p in before)if(p.r>20)visible++;
        Check(visible>1000,"original native renderer path is actually drawn");
        Bounds boundsA=a.bounds,boundsB=b.bounds;Transform parentA=a.transform.parent;int idA=a.GetInstanceID();
        Collider collider=a.GetComponent<Collider>();
        on=true;ScenarioStructuralInstancing.BeforeLoadingComplete();
        Check(original.enableInstancing,"repeated exact native mesh/material enables original GPU instancing flag");
        Check(a.sharedMaterial==original&&b.sharedMaterial==original&&a.GetInstanceID()==idA
            &&a.transform.parent==parentA&&a.bounds==boundsA&&b.bounds==boundsB&&collider.enabled,
            "instancing retains exact native renderer mesh geometry parent colliders and identity");
        Check(a.enabled&&b.enabled&&!a.forceRenderingOff&&!b.forceRenderingOff,
            "instancing never takes native visibility ownership");
        Color32[] after=Picture(camera);int changed=0;
        for(int i=0;i<before.Length;i++)if(!before[i].Equals(after[i]))changed++;
        Check(changed==0,"GPU instancing preserves exact fixture pixels and original per-object matrices");
        var block=new MaterialPropertyBlock();block.SetColor("_Color",Color.blue);a.SetPropertyBlock(block);
        Color32[] blocked=Picture(camera);int blue=0;foreach(Color32 p in blocked)if(p.b>100&&p.r<20)blue++;
        Check(blue>500,"native non-instanced wall property block retains independent renderer appearance");
        a.SetPropertyBlock(null);a.gameObject.SetActive(false);Picture(camera);
        Check(!a.gameObject.activeSelf&&b.enabled,"native room culling stays independent after instancing");a.gameObject.SetActive(true);
        on=false;Tick();Check(!original.enableInstancing,"owned material flag restores immediately on option off");
        on=true;ScenarioStructuralInstancing.BeforeLoadingComplete();original.enableInstancing=false;Tick();
        Surface(generated.transform,mesh,original,4);
        ScenarioStructuralInstancing.Placed(generated);ScenarioStructuralInstancing.BeforeLoadingComplete();
        Check(!original.enableInstancing,"foreign native flag changes are never fought or re-enabled");
        on=false;Tick();original.enableInstancing=true;on=true;ScenarioStructuralInstancing.BeforeLoadingComplete();on=false;Tick();
        Check(original.enableInstancing,"already enabled native flag retains its exact original state");original.enableInstancing=false;
        // Boundaries each receive their own material so another valid group cannot grant
        // the same shared material an enable flag and mask a rejected candidate.
        var excluded=new List<(Material Material,MeshRenderer Renderer)>();
        foreach(string kind in new[]{"Actor","Animator","Canvas","NativeProp","Door","Preview","UnknownShader","PropertyBlock","UniqueMesh","OutsideScenario"})
        {
            var parent=Child(kind,generated.transform);
            if(kind=="Actor")parent.AddComponent<ActorBehaviour>();
            if(kind=="Animator")parent.AddComponent<Animator>();
            if(kind=="Canvas")parent.AddComponent<Canvas>();
            if(kind=="NativeProp")parent.AddComponent<ProceduralProp>();
            if(kind=="Door")parent.AddComponent<ProceduralDoorway>();
            if(kind=="OutsideScenario")parent.transform.SetParent(null);
            var material=new Material(kind=="UnknownShader"?Shader.Find("Unlit/Color"):original.shader);
            material.enableInstancing=false;
            MeshRenderer first=Surface(parent.transform,mesh,material,5),second=Surface(parent.transform,mesh,material,6);
            if(kind=="PropertyBlock"){first.SetPropertyBlock(block);second.SetPropertyBlock(block);}
            if(kind=="UniqueMesh")second.GetComponent<MeshFilter>().sharedMesh=UnityEngine.Object.Instantiate(mesh);
            excluded.Add((material,first));
        }
        on=true;
        foreach(var entry in excluded)ScenarioStructuralInstancing.Placed(entry.Renderer.transform.parent.gameObject);
        ScenarioStructuralInstancing.BeforeLoadingComplete();
        foreach(var entry in excluded)Check(!entry.Material.enableInstancing,"unknown or native interaction boundary retains rendering: "+entry.Renderer.transform.parent.name);
        ScenarioStructuralInstancing.Shutdown();
        Check(!original.enableInstancing,"VR shutdown restores owned native flags without destroying sources");
        Check(VRLog.Faults.Count==0,"optional driver never reports native continuation failures");
        UnityEngine.Object.DestroyImmediate(scenario);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(camera.gameObject);
        UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(original);
        foreach(var entry in excluded)if(entry.Material!=null)UnityEngine.Object.DestroyImmediate(entry.Material);
        return count;
    }
}
