using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator HeadCardEdge732(string output,Action<int> complete)
        => HeadCardEdgeCore732(output,complete,false);
    public static IEnumerator HeadNativeCardEdge732(string output,Action<int> complete)
        => HeadCardEdgeCore732(output,complete,true);
    private static IEnumerator HeadCardEdgeCore732(string output,Action<int> complete,bool native)
    {
        _count=0;DesktopEyeInput.Available=true;
        using(var scene=new Scene())
        {
            scene.HideSpatial730();scene.NativePixels725(2);MenuDepthCharacters.Shutdown();
            scene.OriginalA.allowHDR=scene.OriginalB.allowHDR=false;
            scene.OriginalA.allowMSAA=scene.OriginalB.allowMSAA=false;
            foreach(var preview in Object.FindObjectsOfType<RawImage>())if(ReferenceEquals(preview.texture,scene.ModelTarget))preview.gameObject.SetActive(false);
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.HeadTarget.Release();scene.HeadTarget.width=640;scene.HeadTarget.height=480;scene.HeadTarget.Create();
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);texture.filterMode=FilterMode.Point;
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)texture.SetPixel(x,y,new Color(0,1,0,x>56&&(y<8||y>55)?0:1));texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f));
            var card=new GameObject("original native item card at unchanged window edge",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(ItemCardUI));card.layer=5;card.transform.SetParent(scene.RootA,false);
            var rect=(RectTransform)card.transform;rect.sizeDelta=new Vector2(80,80);rect.anchoredPosition=new Vector2(scene.RootA.rect.xMax-40,0);var image=card.GetComponent<Image>();image.sprite=sprite;
            Material? nativeSource=null;
            if(native)
            {
                nativeSource=new Material(Shader.Find("GUI/AbilityCard_Shd"));nativeSource.SetFloat("_Flow",.37f);image.material=nativeSource;
                File.WriteAllText(Path.Combine(output,"native-shader-boundary.txt"),"Native source/derived shader availability is explicitly ported to two declared GL stand-ins. Exact original D3D11 compiled bytes/state audit is separate; these are not native FX pixels.\n");
            }
            var plane=card.AddComponent<MenuDepthCardPlane>();plane.Configure(card.GetComponent<ItemCardUI>(),rect,new Graphic[]{image},null);
            Material originalMaterial=image.material;Vector3 originalPosition=rect.localPosition,originalScale=rect.localScale;
            var surface=image.GetComponent<MenuDepthGraphic>();
            foreach(string name in new[]{"_grid","_triangles"})
            {
                object buffer=typeof(MenuDepthGraphic).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(surface);
                Check((int)buffer.GetType().GetProperty("Capacity").GetValue(buffer)==0,"plane-only card helpers allocate no heightfield mesh buffers");
            }
            var hidden=new GameObject("ordinary original UI beyond native frame",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));hidden.layer=5;hidden.transform.SetParent(scene.RootA,false);
            var hiddenRect=(RectTransform)hidden.transform;hiddenRect.sizeDelta=new Vector2(4,80);hiddenRect.anchoredPosition=new Vector2(scene.RootA.rect.xMax+2,0);hidden.GetComponent<Image>().color=Color.blue;hidden.transform.SetSiblingIndex(0);
            scene.Flat.Begin();Canvas.ForceUpdateCanvases();scene.OriginalA.Render();scene.OriginalB.Render();
            rect.anchoredPosition=new Vector2(scene.RootA.rect.xMax-rect.rect.width*.5f,0);
            hiddenRect.anchoredPosition=new Vector2(scene.RootA.rect.xMax+2,0);Canvas.ForceUpdateCanvases();
            Check(MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalA,scene.RootA,Vector3.zero,out _,out Matrix4x4 mapping),"native card has an independent original root camera mapping");
            float depth=-rect.rect.width*.065f*rect.TransformVector(Vector3.right).magnitude/rect.TransformVector(Vector3.forward).magnitude;
            Matrix4x4 expected=mapping*rect.localToWorldMatrix*Matrix4x4.Translate(new Vector3(0,0,depth));
            var reference=new GameObject("independent original raised item print",typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(Image));reference.layer=26;
            var referenceRect=(RectTransform)reference.transform;reference.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            referenceRect.anchorMin=referenceRect.anchorMax=Vector2.zero;referenceRect.pivot=rect.pivot;referenceRect.sizeDelta=rect.rect.size;
            reference.GetComponent<Image>().sprite=sprite;Pose730(referenceRect,expected);
            if(native)reference.GetComponent<Image>().material=nativeSource;
            File.WriteAllText(Path.Combine(output,"card-reference-frame.txt"),"root="+scene.RootA.rect+";card="+rect.rect+";depth="+depth+";original="+rect.localToWorldMatrix+";map="+mapping+";wanted="+expected+";actualReference="+referenceRect.localToWorldMatrix+";referenceRect="+referenceRect.rect);
            int nativeDraws=0,cloneId=0;bool stableClone=true;
            Camera.CameraCallback probe=c=>
            {
                if((c==scene.OriginalA||c==scene.OriginalB)&&MenuDepthHeadRaster.Active)nativeDraws++;
                if(c==scene.OriginalA&&MenuDepthHeadRaster.Active)
                {
                    var nativeMaterial=image.canvasRenderer.GetMaterial();
                    var clone=(Material)typeof(MenuDepthGraphic).GetField("_depthMaterial",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(surface);
                    int current=clone!=null?clone.GetInstanceID():0;
                    if(cloneId==0)cloneId=current;else stableClone&=cloneId==current;
                    if(native)Check(clone!=null&&clone.GetFloat("_Flow")==nativeSource!.GetFloat("_Flow"),"native card depth copies the live original effect property");
                    File.AppendAllText(Path.Combine(output,"card-capture-state.txt"),"ready="+plane.IsReady+";leased="+plane.HasHeadLease+";z="+rect.localPosition.z+";renderer="+(nativeMaterial!=null?nativeMaterial.shader.name:"null")+";clone="+current+"\n");
                }
            };
            Camera.onPreRender+=probe;
            try
            {
                scene.Eyes(new Vector3(.4f,0,1.5f));int before=Time.frameCount;while(Time.frameCount==before)yield return null;
                for(int eye=0;eye<2;eye++)
                {
                    scene.Render(eye!=0);Texture2D actual=Read(scene.HeadTarget,Path.Combine(output,"actual-edge-"+eye+".png"));
                    int mask=scene.Head.cullingMask;VRRigDriver.HeadCamera=null;scene.Head.cullingMask=1<<26;scene.Head.Render();VRRigDriver.HeadCamera=scene.Head;scene.Head.cullingMask=mask;
                    Texture2D wanted=Read(scene.HeadTarget,Path.Combine(output,"reference-edge-"+eye+".png"));
                    var a=actual.GetPixels32();var b=wanted.GetPixels32();int total=0,matched=0,leaked=0;
                    for(int i=0;i<a.Length;i++)
                    {
                        Ray ray=scene.Head.ViewportPointToRay(new Vector3((i%actual.width+.5f)/actual.width,(i/actual.width+.5f)/actual.height));
                        var glass=new Plane(scene.Screen.forward,scene.Screen.position);if(!glass.Raycast(ray,out float distance))continue;
                        Vector3 p=scene.Screen.InverseTransformPoint(ray.GetPoint(distance));if(Math.Abs(p.x)<=.5f&&Math.Abs(p.y)<=.5f)continue;
                        bool expectedGreen=b[i].g>150&&b[i].r<40&&b[i].b<40;
                        if(expectedGreen){total++;if(a[i].g>150&&a[i].r<40&&a[i].b<40)matched++;}
                        if(a[i].b>150&&a[i].r<40&&a[i].g<40)leaked++;
                    }
                    File.AppendAllText(Path.Combine(output,"card-edge-coverage.txt"),"eye="+eye+";nativeSpriteOutside="+total+";actualMatches="+matched+";ordinaryBlueLeaks="+leaked+"\n");
                    Check(total>20,"the independent native raised card really protrudes beyond its unchanged window frame");
                    Check(matched>=total*.95f,"the real head compositor preserves the accepted native card sprite outside its window frame");
                    Check(leaked==0,"transparent native card corners do not grant ordinary off-window UI permission");
                    Object.DestroyImmediate(actual);Object.DestroyImmediate(wanted);
                }
                Check(nativeDraws==4,"the raised card transports native sprite depth without any additional native UI render");
                Check(stableClone&&cloneId!=0,"both card eyes reuse the same cached native material clone");
                Check(ReferenceEquals(image.material,originalMaterial)&&ReferenceEquals(image.materialForRendering,originalMaterial),"ending the head lease restores the original native card material authority");
                Check(rect.localPosition==originalPosition&&rect.localScale==originalScale,"ending the head lease restores the original complete card root transform");
                if(native)
                {
                    nativeSource!.SetFloat("_Flow",.63f);
                    int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;
                    scene.Render(false);
                    Check(stableClone,"the warm native card depth material survives original effect animation without cloning again");
                }
                var sampling=new MenuDepthEye(false,new Vector3(0,0,-100),scene.Head);
                plane.PrimeDirectCapture(sampling);
                using(MenuDepthHeadRaster.Begin(new Vector2Int(640,480)))
                {
                    Check(!plane.HasHeadLease&&ReferenceEquals(surface.GetModifiedMaterial(originalMaterial),originalMaterial),"an unrelated head capture cannot adopt an already primed direct card material");
                }
                plane.CancelDirectCapture();
                using(MenuDepthHeadRaster.Begin(new Vector2Int(640,480)))
                {
                    plane.BeforeCapture(sampling);Check(plane.HasHeadLease,"an actual head-scoped card loan owns its native depth transport");
                    Object.DestroyImmediate(surface);
                    bool safe=true;try{plane.AfterCapture();plane.Rebind(null,new Graphic[]{image});}catch(MissingReferenceException){safe=false;}
                    Check(safe&&image.GetComponent<MenuDepthGraphic>()!=null,"destroyed native child helpers are safely released and rebound");
                }
                plane.enabled=false;
                Check(ReferenceEquals(image.materialForRendering,originalMaterial)&&rect.localPosition==originalPosition,"disabled card owners release the scoped native depth material and root pose");
            }
            finally{Camera.onPreRender-=probe;MenuDepthSources.Unregister(plane);Object.DestroyImmediate(reference);if(nativeSource!=null)Object.DestroyImmediate(nativeSource);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }
        complete(_count);
    }
}
