using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class ActorBarDepthProgram
{
    private static int _count;
    private static void Check(bool value,string message) { _count++; if(!value)throw new InvalidOperationException(message); }
    private static Mesh Quad(Color color)
    {
        var mesh=new Mesh(); mesh.vertices=new[]{new Vector3(-.35f,-.35f,0),new Vector3(.35f,-.35f,0),new Vector3(.35f,.35f,0),new Vector3(-.35f,.35f,0)};
        mesh.triangles=new[]{0,1,2,0,2,3}; mesh.colors=new[]{color,color,color,color}; return mesh;
    }
    public static int Run(string evidence)
    {
        _count=0; Shader.SetGlobalInt("unity_GUIZTestMode",(int)CompareFunction.LessEqual);
        var host=new GameObject("Original bar host",typeof(RectTransform),typeof(Canvas));
        Canvas canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
        var cameraGo=new GameObject("Depth fixture camera");Camera camera=cameraGo.AddComponent<Camera>();camera.enabled=false;
        camera.transform.position=new Vector3(0,0,-5);camera.orthographic=true;camera.orthographicSize=1.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Opaque wall in front of complete native bar";wall.transform.position=new Vector3(0,0,-1);wall.transform.localScale=new Vector3(4,2,.1f);
        var wallMaterial=new Material(Shader.Find("Unlit/Color"));wallMaterial.color=new Color(.02f,.02f,.02f,1);wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;
        Shader uiShader=Shader.Find("Fixture/ActorBarNativeUI");Check(uiShader!=null&&uiShader.isSupported,"real UI fixture shader available");
        var originals=new List<Material>(); var meshes=new List<Mesh>(); var graphics=new List<Graphic>();
        Material Source(Color color)
        {
            var m=new Material(uiShader);m.SetColor("_Color",color);m.SetInt("unity_GUIZTestMode",4);m.SetFloat("_Stencil",7);m.SetFloat("_StencilComp",8);m.SetVector("_ClipRect",new Vector4(-2,-1,2,1));originals.Add(m);return m;
        }
        GameObject Child(string name,Transform parent,float x)
        {var child=new GameObject(name,typeof(RectTransform));child.transform.SetParent(parent,false);child.transform.localPosition=new Vector3(x,0,0);return child;}
        var image=Child("Original circle image",host.transform,-1).AddComponent<Image>(); image.material=Source(Color.red);image.rectTransform.sizeDelta=new Vector2(.7f,.7f);graphics.Add(image);
        var text=Child("Original circled number TextMeshProUGUI",host.transform,0).AddComponent<TextMeshProUGUI>();text.text="";text.fontSharedMaterial=Source(Color.green);graphics.Add(text);
        var sub=TMP_SubMeshUI.AddSubTextObject(text,new MaterialReference {index=1,material=Source(Color.blue)});
        sub.name="Original inline sprite TMP_SubMeshUI";sub.transform.localPosition=new Vector3(1,0,0);graphics.Add(sub);
        Mesh textQuad=Quad(Color.white),spriteQuad=Quad(Color.white);meshes.Add(textQuad);meshes.Add(spriteQuad);
        var target=new RenderTexture(128,128,24);target.Create();camera.targetTexture=target;var read=new Texture2D(128,128,TextureFormat.RGBA32,false);
        int[] Render(string name)
        {
            Canvas.ForceUpdateCanvases();
            text.canvasRenderer.SetMesh(textQuad);text.canvasRenderer.materialCount=1;text.canvasRenderer.SetMaterial(text.materialForRendering,Texture2D.whiteTexture);
            sub.canvasRenderer.SetMesh(spriteQuad);sub.canvasRenderer.materialCount=1;sub.canvasRenderer.SetMaterial(sub.materialForRendering,Texture2D.whiteTexture);
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();RenderTexture.active=previous;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(evidence,name+".png"),read.EncodeToPNG());
            var n=new int[3];foreach(var p in read.GetPixels32()){if(p.r>60)n[0]++;if(p.g>60)n[1]++;if(p.b>60)n[2]++;}return n;
        }
        var owner=ActorBars.Create(host);
        ActorBars.Scan(owner,true);var hidden=Render("occluded");Check(hidden[0]==0&&hidden[1]==0&&hidden[2]==0,"enabled occlusion hides native circle, number and inline symbol behind wall");
        ActorBars.Restore(owner);ActorBars.Scan(owner,false);
        foreach(var graphic in graphics)
        {
            Material material=graphic is TMP_SubMeshUI s?s.sharedMaterial:graphic is TMP_Text t?t.fontSharedMaterial:graphic.material;
            Check(material.GetFloat("_Stencil")==7&&material.GetVector("_ClipRect")==new Vector4(-2,-1,2,1),"original stencil and clip state survive depth mode");
        }
        var shown=Render("through-wall");
        Check(shown[0]>100&&shown[1]>100&&shown[2]>100,"through-wall mode renders native circle, actual TMP number and TMP inline symbol together");
        foreach(var graphic in graphics)
        {
            Material material=graphic is TMP_SubMeshUI s?s.sharedMaterial:graphic is TMP_Text t?t.fontSharedMaterial:graphic.material;
            Check(material.GetInt("unity_GUIZTestMode")==8&&material.GetInt("_ZTestMode")==8,"both native depth aliases cover every constituent");
        }
        int held=owner.DepthMats.Count;ActorBars.Scan(owner,false);Check(owner.DepthMats.Count==held,"repeat scan retains clones without allocating another native TMP material");
        Material changed=Source(new Color(.2f,.8f,.2f,1));text.fontSharedMaterial=changed;
        ActorBars.Scan(owner,false);Check(text.fontSharedMaterial!=changed&&text.fontSharedMaterial.GetInt("unity_GUIZTestMode")==8,"native font replacement is immediately re-adopted on the next scan");
        var late=Child("Late pooled circle",host.transform,0).AddComponent<Image>();late.rectTransform.sizeDelta=new Vector2(.7f,.7f);late.material=Source(Color.white);ActorBars.Scan(owner,false);
        Check(late.material.GetInt("unity_GUIZTestMode")==8,"late pooled graphic receives current whole-bar mode");
        Material foreign=Source(Color.cyan);sub.sharedMaterial=foreign;ActorBars.Restore(owner);
        Check(image.material==originals[0]&&text.fontSharedMaterial==changed&&sub.sharedMaterial==foreign,"restore uses original exact native font binding and preserves later foreign replacement");
        ActorBars.Scan(owner,true);hidden=Render("occluded-again");Check(hidden[0]==0&&hidden[1]==0&&hidden[2]==0,"live return to wall occlusion restores complete native widget depth: "+string.Join(",",hidden)
            +"; image="+image.material.GetInt("unity_GUIZTestMode")+"/"+image.materialForRendering.GetInt("unity_GUIZTestMode")
            +"; text="+text.fontSharedMaterial.GetInt("unity_GUIZTestMode")+"/"+text.materialForRendering.GetInt("unity_GUIZTestMode")
            +"; sub="+sub.sharedMaterial.GetInt("unity_GUIZTestMode")+"/"+sub.materialForRendering.GetInt("unity_GUIZTestMode")
            +"; late="+late.material.GetInt("unity_GUIZTestMode")+"/"+late.materialForRendering.GetInt("unity_GUIZTestMode"));
        ActorBars.Restore(owner);camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraGo);UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(wallMaterial);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);
        foreach(var material in originals)UnityEngine.Object.DestroyImmediate(material);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
        return _count;
    }
}
