using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object=UnityEngine.Object;

public static class MenuBackdropProbe
{
    public static int Assertions;
    private static void Check(bool okay,string label){Assertions++;if(!okay)throw new InvalidOperationException(label);}
    private static GameObject Quad(string name,int layer,Material material,Vector3 at,Vector3 scale)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=name;go.layer=layer;Object.Destroy(go.GetComponent<Collider>());
        go.transform.position=at;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;return go;
    }
    private static Texture2D Capture(Camera head,string path)
    {
        var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);target.Create();var saved=head.targetTexture;var active=RenderTexture.active;
        var result=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
        try{head.targetTexture=target;head.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,256,256),0,0);result.Apply();File.WriteAllBytes(path,result.EncodeToPNG());return result;}
        finally{head.targetTexture=saved;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);}
    }
    private static int Pixels(Texture2D image,Color expected)
    {
        Color32 match=expected;int result=0;foreach(var pixel in image.GetPixels32())if(Math.Abs(pixel.r-match.r)<3&&Math.Abs(pixel.g-match.g)<3&&Math.Abs(pixel.b-match.b)<3)result++;return result;
    }
    public static IEnumerator Run(Camera head,string output)
    {
        Assertions=0;
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);var red=new Color(1,0,0,0);
        for(int y=0;y<2;y++)for(int x=0;x<2;x++)texture.SetPixel(x,y,red);texture.Apply();
        var rt=new RenderTexture(64,64,0,RenderTextureFormat.ARGB32);rt.Create();Graphics.Blit(texture,rt);
        var screenQuad=GameObject.CreatePrimitive(PrimitiveType.Quad);screenQuad.layer=VRLayers.ModLayer;Object.Destroy(screenQuad.GetComponent<Collider>());
        screenQuad.transform.position=new Vector3(0,0,2);screenQuad.transform.localScale=new Vector3(2.4f,1.8f,1);
        var screen=new FlatScreen();screen.BindOpaque(screenQuad,rt);
        Material originalMaterial=screen.ProbeMaterial;Shader originalShader=originalMaterial.shader;
        GameObject? splitBackground=null;Material? glass=null;Texture2D? clear=null;
        var roomMaterial=new Material(Shader.Find("DefaultSkyFixture/Foreground"));roomMaterial.color=Color.green;roomMaterial.renderQueue=1900;
        var handMaterial=new Material(roomMaterial);handMaterial.color=Color.yellow;handMaterial.renderQueue=2000;
        var room=Quad("surrounding room surface at original floor queue",VRLayers.ModLayer,roomMaterial,new Vector3(0,0,3.4f),Vector3.one*20);
        var near=Quad("near physical hand witness",VRLayers.ModLayer,handMaterial,new Vector3(.4f,0,1.2f),Vector3.one*.18f);
        var far=Quad("hand behind the physical window",VRLayers.ModLayer,handMaterial,new Vector3(-.4f,0,2.6f),Vector3.one*.18f);
        Vector3 savedPos=head.transform.position;Quaternion savedRot=head.transform.rotation;
        try
        {
            for(int split=0;split<2;split++)
            {
                if(split==1)
                {
                    splitBackground=Quad("actual split opaque background",VRLayers.ModLayer,originalMaterial,new Vector3(0,0,2.06f),new Vector3(2.49f,1.87f,1));
                    screen.SplitProbe(screenQuad,splitBackground);
                    clear=new Texture2D(2,2,TextureFormat.RGBA32,false,true);for(int i=0;i<4;i++)clear.SetPixel(i%2,i/2,Color.clear);clear.Apply();
                    glass=new Material(Shader.Find("Sprites/Default")){mainTexture=clear};screenQuad.GetComponent<Renderer>().sharedMaterial=glass;
                }
                for(int back=0;back<2;back++)
                {
                    head.transform.SetPositionAndRotation(back==0?Vector3.zero:new Vector3(0,0,4),Quaternion.Euler(0,back==0?0:180,0));
                    room.transform.position=new Vector3(0,0,back==0?3.4f:.6f);
                    near.transform.position=new Vector3(.4f,0,back==0?1.2f:2.8f);far.transform.position=new Vector3(-.4f,0,back==0?2.6f:1.4f);
                    SkyBackdrop.Tick(false);screen.TickBackdrop();
                    var image=Capture(head,Path.Combine(output,"actual-menu-default-"+(split==0?"single":"split")+"-"+(back==0?"front":"rear")+".png"));
                    try
                    {
                        int redCount=Pixels(image,Color.red),yellow=Pixels(image,Color.yellow);Color center=image.GetPixel(128,128);
                        File.AppendAllText(Path.Combine(output,"menu-backdrop-pixels.txt"),"split="+split+";back="+back+";red="+redCount+";nearHand="+yellow+";center="+center+";material="+screen.MaterialState+"\n");
                        Check(center.r>.9f&&center.g<.1f&&redCount>10000,"actual world-menu opaque RT survives original Default sky and later room geometry");
                        Check(yellow>100&&yellow<3000,"physical hand in front remains visible while a hand behind the menu stays occluded");
                        Vector3 behind=head.WorldToViewportPoint(far.transform.position);Color hidden=image.GetPixel(Mathf.RoundToInt(behind.x*255),Mathf.RoundToInt(behind.y*255));
                        Check(hidden.r>.9f&&hidden.g<.1f,"actual world menu writes depth in front of a later hand renderer behind it");
                        Vector3 infront=head.WorldToViewportPoint(near.transform.position);Color visible=image.GetPixel(Mathf.RoundToInt(infront.x*255),Mathf.RoundToInt(infront.y*255));
                        Check(visible.r>.9f&&visible.g>.9f&&visible.b<.1f,"actual world menu lets the nearer physical hand occlude its opaque image");
                        Check(FlatScreen.OpaqueBackdrop==(splitBackground!=null?splitBackground:screenQuad).GetComponent<Renderer>(),"world-menu depth material remains authoritative opaque MR coverage");
                    }
                    finally{Object.DestroyImmediate(image);}
                    yield return null;
                }
            }
            room.SetActive(false);near.SetActive(false);far.SetActive(false);
            MenuWorldContext.IsReady=false;SkyBackdrop.Tick(false);screen.TickBackdrop();
            Check(ReferenceEquals(screen.ProbeMaterial,originalMaterial)&&originalMaterial.shader==originalShader&&originalMaterial.renderQueue==1000,"leaving ready menu restores the original intro material identity shader and queue");
            Check(NativeDefaultSky.Renderer==null,"intro admission removes the actual default environment while restoring the stock screen");
            var intro=Capture(head,Path.Combine(output,"actual-intro-original-screen.png"));
            try{Check(Pixels(intro,Color.red)>10000,"original intro opaque copy still ignores native RT alpha zero");}finally{Object.DestroyImmediate(intro);}
            MenuWorldContext.IsReady=true;SkyBackdrop.Tick(false);screen.TickBackdrop();
            Check(ReferenceEquals(screen.ProbeMaterial,originalMaterial)&&originalMaterial.shader.name=="GloomhavenVR/Overlay","ready menu restoration reuses the same exact stereo/capture material identity");
            var surface=splitBackground!.GetComponent<Renderer>();var foreign=new Material(originalMaterial);
            surface.sharedMaterial=foreign;Check(FlatScreen.OpaqueBackdrop==null,"foreign visible material cannot inherit opaque window coverage");surface.sharedMaterial=originalMaterial;Object.Destroy(foreign);
            Shader owned=originalMaterial.shader;originalMaterial.shader=Shader.Find("Sprites/Default");Check(FlatScreen.OpaqueBackdrop==null,"foreign shader cannot inherit the cached opaque window coverage");originalMaterial.shader=owned;
            screen.TickBackdrop();Check(FlatScreen.OpaqueBackdrop==surface,"owned opaque window coverage recovers after the foreign shader is removed");
        }
        finally
        {
            screenQuad.SetActive(false);if(splitBackground!=null)splitBackground.SetActive(false);
            head.transform.SetPositionAndRotation(savedPos,savedRot);screen.DestroyProbe();Object.Destroy(screenQuad);Object.Destroy(room);Object.Destroy(near);Object.Destroy(far);
            Object.Destroy(roomMaterial);Object.Destroy(handMaterial);Object.Destroy(texture);if(splitBackground!=null)Object.Destroy(splitBackground);if(glass!=null)Object.Destroy(glass);if(clear!=null)Object.Destroy(clear);rt.Release();Object.Destroy(rt);
        }
    }
}
