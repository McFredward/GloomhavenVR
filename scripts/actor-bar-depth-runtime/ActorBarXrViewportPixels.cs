// Build675: the actual OpenXR provider reports normalized viewports. Exercise the
// production conversion with simultaneous original game glyphs, moving foreground
// and distinct eye poses. This is a real GPU provider-boundary analogue, not XR
// device execution; the source conversion is exactly the one admitted by Viewport.
using System;
using System.IO;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class ActorBarXrViewportPixels
{
    private static int _checks;
    private static void Check(bool value,string why) {_checks++;if(!value)throw new InvalidOperationException(why);}
    private static int Green(Color32[] pixels,int left,int right)
    {
        int count=0;for(int y=0;y<128;y++)for(int x=left;x<right;x++)
        {Color32 p=pixels[y*128+x];if(p.g>80&&p.g>p.r+25&&p.g>p.b+25)count++;}return count;
    }
    private static int Diff(Color32[] a,Color32[] b)
    {int count=0;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))count++;return count;}
    private static Color32[] Read(RenderTexture target,string path)
    {
        RenderTexture before=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(128,128,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
        Color32[] pixels=image.GetPixels32();File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=before;Object.DestroyImmediate(image);return pixels;
    }
    public static int Run(string evidence,Camera camera,TextMeshProUGUI text,Material original,Material foreground)
    {
        _checks=0;
        text.ForceMeshUpdate();Canvas.ForceUpdateCanvases();
        Mesh glyph=text.mesh;Matrix4x4 glyphMatrix=text.transform.localToWorldMatrix;
        var nativeMaterial=new Material(original);nativeMaterial.SetInt("unity_GUIZTestMode",4);
        var selective=new Material(original);selective.shader=GloomhavenVR.Core.BundleShaders.Resolve("GloomhavenVR/ActorBarMobileSDF","Fixture","native actor depth","missing");
        selective.CopyPropertiesFromMaterial(original);selective.SetInt("unity_GUIZTestMode",8);
        var card=GameObject.CreatePrimitive(PrimitiveType.Cube);card.name="Single moving native foreground card";
        card.transform.localScale=new Vector3(.65f,1.1f,.02f);Renderer renderer=card.GetComponent<Renderer>();renderer.sharedMaterial=foreground;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="World wall remains absent from selective capture";
        wall.transform.position=new Vector3(0,0,-1);wall.transform.localScale=new Vector3(4,3,.02f);Renderer wallRenderer=wall.GetComponent<Renderer>();wallRenderer.sharedMaterial=foreground;
        var reference=new RenderTexture(128,128,24);reference.Create();var actual=new RenderTexture(128,128,24);actual.Create();
        var depth=new RenderTexture(new RenderTextureDescriptor(128,128,RenderTextureFormat.Depth,24){msaaSamples=1,shadowSamplingMode=ShadowSamplingMode.None});depth.filterMode=FilterMode.Point;depth.Create();
        var commands=new CommandBuffer {name="Normalized XR viewport with three independent actors"};
        int depthId=Shader.PropertyToID("_GHVRActorForegroundDepth");Vector3 savedCamera=camera.transform.position;
        using(var output=new StreamWriter(Path.Combine(evidence,"xr-normalized-multi-actor-results.txt")))
        {
            foreach(Rect provider in new[]{new Rect(0,0,1,1),new Rect(0,0,91f/128,83f/128),new Rect(11f/128,7f/128,91f/128,83f/128)})
            foreach(float eye in new[]{-.035f,.035f})
            {
                camera.transform.position=new Vector3(eye,0,-5);
                // Native reference uses the independently constructed pixel viewport;
                // the production derivative/capture uses actual provider conversion.
                Rect nativeViewport=new Rect(provider.x*128,provider.y*128,provider.width*128,provider.height*128);
                Rect captureViewport=HealthbarForegroundDepth.XrPixelViewport(provider,128,128);
                int caseIndex=0;
                foreach(float position in new[]{-2f,-.9f,0f,.9f,2f,0f,-2f})
                {
                    bool hasCard=Math.Abs(position)<1.5f;card.transform.position=new Vector3(position,0,-2);
                    commands.Clear();commands.SetGlobalVector("_ScreenParams",new Vector4(128,128,1f+1f/128,1f+1f/128));
                    commands.SetRenderTarget(depth);commands.ClearRenderTarget(true,false,Color.clear);commands.SetViewport(captureViewport);
                    if(hasCard)commands.DrawRenderer(renderer,foreground,0,0);
                    commands.SetGlobalTexture(depthId,depth);commands.SetGlobalVector("_GHVRActorForegroundViewport",new Vector4(captureViewport.x,captureViewport.y,captureViewport.width,captureViewport.height));
                    commands.SetGlobalVector("_GHVRActorForegroundSize",new Vector4(1f/128,1f/128,128,128));commands.SetGlobalFloat("_GHVRActorForegroundReady",1);
                    void Draw(RenderTexture target,Material material,Rect viewport,bool includeWall)
                    {
                        commands.SetRenderTarget(target);commands.ClearRenderTarget(true,true,Color.black);commands.SetViewport(viewport);
                        if(includeWall)commands.DrawRenderer(wallRenderer,foreground,0,0);
                        if(hasCard)commands.DrawRenderer(renderer,foreground,0,0);
                        foreach(float actor in new[]{-.9f,0f,.9f})commands.DrawMesh(glyph,Matrix4x4.Translate(new Vector3(actor,0,0))*glyphMatrix,material,0,0);
                    }
                    Draw(reference,nativeMaterial,nativeViewport,false);
                    Draw(actual,selective,captureViewport,true);
                    commands.SetGlobalFloat("_GHVRActorForegroundReady",0);commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);commands.SetViewport(new Rect(0,0,128,128));
                    camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha,commands);try{camera.Render();}finally{camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha,commands);}
                    string label="xr-"+provider.x+"-"+provider.width+"-eye-"+eye+"-frame-"+caseIndex++;
                    Color32[] expected=Read(reference,Path.Combine(evidence,label+"-native.png")),shown=Read(actual,Path.Combine(evidence,label+"-selective.png"));
                    int diff=Diff(expected,shown);output.WriteLine(label+"; provider="+provider+"; capture="+captureViewport+"; card="+(hasCard?position.ToString():"absent")+"; differingPixels="+diff);output.Flush();
                    Check(diff==0,"normalized XR viewport preserves all native pixels for independently occluded actors: "+label+" difference="+diff);
                    int[] counts=new int[3];for(int actor=0;actor<3;actor++)
                    {
                        int left=Mathf.FloorToInt(nativeViewport.x+nativeViewport.width*actor/3),right=Mathf.CeilToInt(nativeViewport.x+nativeViewport.width*(actor+1)/3);
                        counts[actor]=Green(shown,left,right);
                    }
                    if(hasCard)
                    {
                        int covered=position<-.5f?0:position>.5f?2:1;
                        Check(counts[covered]==0,"moving card hides only the intersected actor's actual native glyphs");
                        for(int actor=0;actor<3;actor++)if(actor!=covered)Check(counts[actor]>10,"uncovered actor stays visible through a real depth-writing world wall on every eye/frame");
                    }
                    else Check(counts[0]>10&&counts[1]>10&&counts[2]>10,"three simultaneous original healthbars recover without a stale foreground sample");
                }
            }
        }
        Check(HealthbarForegroundDepth.XrPixelViewport(new Rect(0,0,1,1),3560,3560)==new Rect(0,0,3560,3560),"normalized OpenXR viewport maps to the actual Build674 3560x3560 eye allocation");
        camera.transform.position=savedCamera;commands.Release();foreach(Object owned in new Object[]{nativeMaterial,selective,card,wall,reference,actual,depth})Object.DestroyImmediate(owned);
        return _checks;
    }
}
