using System;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MenuDepthCharacterNativeSoftMask728
{
    public static int NativeSoftMask728(string output)
    {
        int checks=0;
        using (var scene=new MenuDepthCharacterPresentation725.Scene(output,false,false))
        {
            scene.NativeCamera.enabled=false;
            var pixels=new Texture2D(96,48,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            var mask=new Texture2D(96,48,TextureFormat.RGBA32,false) { wrapMode=TextureWrapMode.Clamp };
            for(int y=0;y<48;y++) for(int x=0;x<96;x++)
            {
                pixels.SetPixel(x,y,new Color(.2f+x/150f,.15f+y/70f,.8f-x/240f,.25f+y/64f));
                // Alpha deliberately differs from every RGB channel.
                mask.SetPixel(x,y,new Color(.9f,.1f,.7f,.12f+x/150f));
            }
            pixels.Apply(); mask.Apply(); Graphics.Blit(pixels,scene.Native);
            var material=new Material(Shader.Find("UI/Inverted soft alpha mask (compatible with canvas group)"));
            material.SetTexture("_Mask",mask); material.SetTextureScale("_Mask",new Vector2(1.3f,.8f)); material.SetTextureOffset("_Mask",new Vector2(-.14f,.12f));
            material.SetTextureScale("_MainTex",new Vector2(.8f,.9f)); material.SetTextureOffset("_MainTex",new Vector2(.05f,-.07f));
            var clip=new GameObject("native non-keyword rectangular mask",typeof(RectTransform),typeof(RectMask2D)); clip.layer=5;
            var rectangle=(RectTransform)clip.transform; rectangle.SetParent(scene.Root,false); rectangle.sizeDelta=new Vector2(600,450);
            scene.Image.transform.SetParent(rectangle,false); scene.Image.material=material;
            scene.Image.uvRect=new Rect(.1f,.2f,.7f,.65f); scene.Image.color=new Color(.7f,.8f,.9f,.45f);
            scene.Root.gameObject.AddComponent<CanvasGroup>().alpha=.37f;
            var native=new GameObject("original native softmask reference",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage)); native.layer=5;
            native.transform.SetParent(rectangle,false); var original=native.GetComponent<RawImage>();
            original.rectTransform.sizeDelta=scene.Image.rectTransform.sizeDelta; original.texture=scene.Native;
            original.material=material; original.color=scene.Image.color; original.uvRect=scene.Image.uvRect;
            var backing=new GameObject("original independent UI destination",typeof(RectTransform),typeof(Canvas)); backing.layer=5;
            var backingRect=(RectTransform)backing.transform; backingRect.sizeDelta=scene.Root.sizeDelta;
            backingRect.SetPositionAndRotation(scene.Root.position+scene.Root.forward*.01f,scene.Root.rotation); backingRect.localScale=scene.Root.localScale;
            backing.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace; backing.GetComponent<Canvas>().sortingOrder=-10;
            var destination=backing.AddComponent<RawImage>(); destination.texture=Texture2D.whiteTexture;
            scene.Head.backgroundColor=Color.clear;
            scene.Head.transform.SetPositionAndRotation(scene.Root.position-scene.Root.forward*1.2f,scene.Root.rotation);
            foreach(float alpha in new[]{0f,.35f,1f})
            {
                backing.SetActive(alpha>0); destination.color=new Color(.2f,.1f,.7f,alpha);
                scene.Image.gameObject.SetActive(false); native.SetActive(false);
                Texture2D backdrop=Draw(scene.Head,scene.Final,Path.Combine(output,"native-destination-"+alpha+".png"));
                float destinationAlpha=backdrop.GetPixel(backdrop.width/2,backdrop.height/2).a;
                checks++; if(Mathf.Abs(destinationAlpha-alpha)>.012f)
                    throw new Exception("each native UI destination must actually establish its distinct transparent partial or opaque alpha");
                Object.DestroyImmediate(backdrop);
                native.SetActive(true);
                Texture2D expected=Draw(scene.Head,scene.Final,Path.Combine(output,"native-softmask-"+alpha+".png"));
                native.SetActive(false); scene.Image.gameObject.SetActive(true);
                Texture2D actual=Draw(scene.Head,scene.Final,Path.Combine(output,"transport-softmask-"+alpha+".png"));
                Color32[] a=expected.GetPixels32(),b=actual.GetPixels32(); double sum=0; int visible=0;
                for(int i=0;i<a.Length;i++)
                {
                    sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)+Math.Abs(a[i].a-b[i].a);
                    if(a[i].r>0 || a[i].g>0 || a[i].b>0) visible++;
                }
                checks++; if(visible<1000) throw new Exception("the original native softmask reference must actually draw coloured translucent pixels");
                double mean=sum/(a.Length*4);
                File.AppendAllText(Path.Combine(output,"native-softmask-rgba.txt"),"destination="+alpha+";actualDestinationAlpha="+destinationAlpha+";meanChannelDifference="+mean+";visible="+visible+"\n");
                checks++; if(mean>.12) throw new Exception("the exact native softmask RGBA blend survives transparent partial and opaque UI destinations");
                Object.DestroyImmediate(expected); Object.DestroyImmediate(actual);
            }
            Object.DestroyImmediate(material); Object.DestroyImmediate(pixels); Object.DestroyImmediate(mask);
        }
        return checks;
    }
    private static Texture2D Draw(Camera camera,RenderTexture target,string file)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture old=RenderTexture.active; RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); RenderTexture.active=old;
        File.WriteAllBytes(file,image.EncodeToPNG()); return image;
    }
}
