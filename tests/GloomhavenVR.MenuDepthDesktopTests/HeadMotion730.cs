using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private sealed partial class Scene
    {
        internal RectTransform RootA => _canvasA;
        internal RectTransform RootB => _canvasB;
        internal void HideSpatial730() { _red.gameObject.SetActive(false); _green.gameObject.SetActive(false); }
    }
    private static void Pose730(Transform t, Matrix4x4 matrix)
    {
        t.SetPositionAndRotation(matrix.GetColumn(3), Quaternion.LookRotation(matrix.GetColumn(2),matrix.GetColumn(1)));
        t.localScale = new Vector3(matrix.GetColumn(0).magnitude,matrix.GetColumn(1).magnitude,matrix.GetColumn(2).magnitude);
    }
    private static Matrix4x4 BodyMap730(Camera source, Transform ground, RectTransform raw)
    {
        Rect r=raw.rect; Vector3 right=raw.TransformVector(Vector3.right),up=raw.TransformVector(Vector3.up);
        float scale=Mathf.Max(2.4f/(r.width*right.magnitude*.82f),2.4f/(r.height*up.magnitude*.75f));
        right.Normalize();up.Normalize();Vector3 forward=Vector3.Cross(right,up).normalized;
        Vector3 center=raw.TransformPoint(r.center),origin=ground.position+source.transform.up*1.2f;
        Matrix4x4 native=Matrix4x4.identity;
        Vector3 nr=source.transform.right,nu=source.transform.up,nf=source.transform.forward;
        native.SetRow(0,new Vector4(nr.x,nr.y,nr.z,-Vector3.Dot(nr,origin)));
        native.SetRow(1,new Vector4(nu.x,nu.y,nu.z,-Vector3.Dot(nu,origin)));
        native.SetRow(2,new Vector4(nf.x,nf.y,nf.z,-Vector3.Dot(nf,origin)));
        Matrix4x4 physical=Matrix4x4.identity;
        physical.SetColumn(0,new Vector4(right.x,right.y,right.z,0)/scale);
        physical.SetColumn(1,new Vector4(up.x,up.y,up.z,0)/scale);
        physical.SetColumn(2,new Vector4(forward.x,forward.y,forward.z,0)/scale);
        physical.SetColumn(3,new Vector4(center.x,center.y,center.z,1));
        return physical*native;
    }
    private static double Corr730(Color32[] actual,Color32[] reference,bool model,out double contrast)
    {
        double sa=0,sb=0,saa=0,sbb=0,sab=0;int n=0;
        for(int i=0;i<actual.Length;i++)
        {
            double a=model?Math.Max(0,actual[i].g-actual[i].r):actual[i].r;
            // Reference is direct original UI. Match the existing native
            // photograph's final alpha multiplication before comparing pixels;
            // this preserves native partial glyph coverage and CanvasGroup tint.
            double b=model?Math.Max(0,reference[i].g-reference[i].r):reference[i].r*reference[i].a/255.0;
            if(a+b<5)continue; sa+=a;sb+=b;saa+=a*a;sbb+=b*b;sab+=a*b;n++;
        }
        if (n <= 60)
        {
            Check(saa < 1000 && sbb < 1000,"an edge-on original plane has no invented visible ink");
            contrast=0;return 1;
        }
        double va=saa-sa*sa/n,vb=sbb-sb*sb/n;contrast=Math.Sqrt(va/n);
        return (sab-sa*sb/n)/Math.Sqrt(Math.Max(1e-12,va*vb));
    }
    private static GameObject CloneUi730(RectTransform source,Matrix4x4 mapping)
    {
        var clone=Object.Instantiate(source.gameObject);clone.name="independent direct native UI reference";
        clone.SetActive(false);var rect=(RectTransform)clone.transform;var canvas=clone.GetComponent<Canvas>();
        Vector2 size=source.rect.size,pivot=source.pivot;Matrix4x4 matrix=mapping*source.localToWorldMatrix;
        canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=null;
        rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=pivot;rect.sizeDelta=size;Pose730(rect,matrix);
        foreach(Transform child in clone.GetComponentsInChildren<Transform>(true))child.gameObject.layer=26;
        clone.SetActive(true);return clone;
    }
    public static IEnumerator HeadMotion730(string output,Action<int> complete)
        => HeadMotionCore730(output, complete, 640, 480, false);
    public static IEnumerator HeadRaster4040(string output,Action<int> complete)
        => HeadMotionCore730(output, complete, 4040, 4040, true);
    public static IEnumerator HeadAnnotations730(string output,Action<int> complete)
        => HeadMotionCore730(output, complete, 640, 480, false, true);
    public static IEnumerator HeadFigureCard732(string output,Action<int> complete)
        => HeadMotionCore730(output,complete,640,480,false,false,true);
    public static IEnumerator HeadPitch736(string output,Action<int> complete)
        => HeadMotionCore730(output,complete,640,480,false,false,false,true);
    public static IEnumerator HeadPitchRt736(string output,Action<int> complete)
    {
        HeadPitchBackend736.TopOrigin=true;
        return HeadMotionCore730(output,complete,640,480,false,false,false,true);
    }
    public static IEnumerator HeadAliasing742(string output,Action<int> complete)
        => HeadMotionCore730(output,complete,640,480,false,false,false,false,true);
    private static IEnumerator HeadMotionCore730(string output,Action<int> complete,int width,int height,bool hardwareRaster,bool annotations=false,bool cards=false,bool pitch=false,bool aliasing=false)
    {
        _count=0;DesktopEyeInput.Available=true;
        for(int route=0;route<(annotations?4:2);route++)
        using(var scene=new Scene())
        {
            int path=route%2;
            scene.HideSpatial730();scene.NativePixels725(4);
            scene.Head.orthographic=false;scene.Head.fieldOfView=70;scene.Head.nearClipPlane=.01f;
            scene.HeadTarget.Release();scene.HeadTarget.width=width;scene.HeadTarget.height=height;scene.HeadTarget.Create();
            if(pitch)File.WriteAllText(Path.Combine(output,"native-gpu-conventions-"+path+".txt"),"api="+SystemInfo.graphicsDeviceType
                +";uvTop="+SystemInfo.graphicsUVStartsAtTop+";reversedZ="+SystemInfo.usesReversedZBuffer+"\nCPU="+scene.Head.projectionMatrix
                +"\nRTGPU="+GL.GetGPUProjectionMatrix(scene.Head.projectionMatrix,true));
            scene.ModelCamera.allowHDR=scene.ModelCamera.allowMSAA=false;
            scene.ModelCamera.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            scene.OriginalA.allowHDR=scene.OriginalB.allowHDR=false;scene.OriginalA.allowMSAA=scene.OriginalB.allowMSAA=false;
            scene.OriginalA.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            Transform holder=scene.ModelCamera.GetComponent<Character3DDisplayManager>().Holder;
            foreach(Transform child in holder)child.gameObject.SetActive(false);
            var ground=new GameObject("original native actor ground");ground.transform.SetParent(holder,false);
            ground.transform.position=scene.ModelCamera.transform.TransformPoint(new Vector3(0,-1.2f,2.56f));
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.layer=8;body.transform.SetParent(ground.transform,false);
            body.transform.localPosition=new Vector3(0,.9f,-.12f);body.transform.localScale=new Vector3(.65f,1.8f,.65f);
            if(aliasing)body.transform.localRotation=Quaternion.Euler(0,17,11);
            var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);texture.filterMode=FilterMode.Point;
            // The stationary-pitch model is only about21 pixels wide. Retain
            // at least two pixels per checker cell across backends; the original
            //52-frame point-sampled detail oracle keeps its exact input below.
            if(pitch)texture.filterMode=FilterMode.Bilinear;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {int cell=pitch?16:8;float v=((x/cell+y/cell)&1)==0?1f:.22f;texture.SetPixel(x,y,new Color(v,v,v,1));}texture.Apply();
            var material=new Material(Shader.Find("Unlit/Texture"));material.mainTexture=texture;material.color=Color.white;
            // Cyan separates the model's independent high-frequency signal from white UI ink.
            var bodyMaterial=new Material(Shader.Find("Unlit/Transparent"));bodyMaterial.mainTexture=texture;bodyMaterial.color=Color.cyan;
            // Built-in Unlit/Transparent does not tint, so use the original UI-compatible material below.
            Object.DestroyImmediate(bodyMaterial);
            var cyanTexture=Object.Instantiate(texture);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){Color c=cyanTexture.GetPixel(x,y);c.r=0;cyanTexture.SetPixel(x,y,c);}cyanTexture.Apply();
            bodyMaterial=new Material(Shader.Find("Unlit/Texture"));bodyMaterial.mainTexture=cyanTexture;
            body.GetComponent<MeshRenderer>().sharedMaterial=bodyMaterial;
            scene.ModelCamera.GetComponent<Character3DDisplayManager>().BindOriginal(ground.transform);
            RawImage raw=null!;foreach(var image in Object.FindObjectsOfType<RawImage>())if(ReferenceEquals(image.texture,scene.ModelTarget))raw=image;
            raw.rectTransform.anchoredPosition=new Vector2(-320,-10);raw.rectTransform.sizeDelta=new Vector2(420,800);
            var grid=new GameObject("original native UI high-frequency print",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));grid.layer=5;
            grid.transform.SetParent(scene.RootA,false);var gridImage=grid.GetComponent<RawImage>();gridImage.texture=texture;
            gridImage.rectTransform.sizeDelta=new Vector2(500,500);gridImage.rectTransform.anchoredPosition=new Vector2(300,-30);
            if(aliasing)
            {
                gridImage.texture=Texture2D.whiteTexture;
                gridImage.rectTransform.sizeDelta=new Vector2(330,320);
                gridImage.rectTransform.localRotation=Quaternion.Euler(0,0,23);
            }
            var label=new GameObject("original native text quality witness",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));label.layer=5;
            label.transform.SetParent(scene.RootA,false);var text=label.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text="WINDOW 730";text.fontSize=60;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;
            text.rectTransform.sizeDelta=new Vector2(600,90);text.rectTransform.anchoredPosition=new Vector2(250,350);
            var tooltip=new GameObject("original right tooltip root without Graphic",typeof(RectTransform));tooltip.layer=5;
            tooltip.transform.SetParent(scene.RootA,false);var tooltipRect=(RectTransform)tooltip.transform;
            tooltipRect.sizeDelta=new Vector2(600,100);tooltipRect.anchoredPosition=new Vector2(900,-350);
            if(annotations)tooltipRect.localPosition+=Vector3.forward*100;
            var originalPlate=new GameObject("original tooltip plate child",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));originalPlate.layer=5;originalPlate.transform.SetParent(tooltip.transform,false);
            var tip=originalPlate.GetComponent<RawImage>();tip.texture=texture;tip.rectTransform.sizeDelta=new Vector2(600,100);
            var nativeText=new GameObject("exact native description Graphic child",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));nativeText.layer=5;nativeText.transform.SetParent(tooltip.transform,false);
            var nativeLabel=nativeText.GetComponent<Text>();nativeLabel.font=Resources.GetBuiltinResource<Font>("Arial.ttf");nativeLabel.fontSize=40;nativeLabel.text="CHARACTER QUEST";nativeLabel.alignment=TextAnchor.MiddleCenter;nativeLabel.rectTransform.sizeDelta=new Vector2(600,100);
            if(annotations)nativeLabel.rectTransform.localPosition+=Vector3.forward*80;
            HeadAnnotation730? annotation=new HeadAnnotation730(nativeLabel,tooltipRect);MenuDepthSources.Register(annotation);
            RectTransform? cardRect=null;Material? cardMaterial=null,referenceCardMaterial=null;MenuDepthCardPlane? cardPlane=null;float cardDepth=0;
            if(cards)
            {
                var card=new GameObject("original card overlapping real character depth",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(ItemCardUI));card.layer=5;card.transform.SetParent(scene.RootA,false);
                cardRect=(RectTransform)card.transform;cardRect.sizeDelta=new Vector2(500,650);cardRect.anchoredPosition=raw.rectTransform.anchoredPosition;
                var print=card.GetComponent<Image>();print.color=Color.blue;cardMaterial=new Material(Shader.Find("GUI/AbilityCard_Shd"));print.material=cardMaterial;
                cardPlane=card.AddComponent<MenuDepthCardPlane>();cardPlane.Configure(card.GetComponent<ItemCardUI>(),cardRect,new Graphic[]{print},null);
                cardDepth=-cardRect.rect.width*.104f*cardRect.TransformVector(Vector3.right).magnitude/cardRect.TransformVector(Vector3.forward).magnitude;
                // Isolate the real figure/card depth relationship. Whole-window
                // UI order, sampling and ink have their separate52-frame oracle.
                foreach(var other in scene.RootA.GetComponentsInChildren<Graphic>(true))
                    if(other!=raw&&other!=print)other.enabled=false;
                foreach(var other in scene.RootB.GetComponentsInChildren<Graphic>(true))other.enabled=false;
                File.WriteAllText(Path.Combine(output,"figure-card-boundaries.txt"),"Original figure source and card plane execute; native shader availability uses the declared GL stand-ins. Other synthetic UI witnesses are hidden only in this isolated overlap case. Whole-window52-frame tests are unchanged.\n");
            }
            if(annotations)
            {
                Check(tooltip.GetComponent<Graphic>()==null&&ReferenceEquals(annotation.Graphic,nativeLabel),"native annotation membership uses the visible child of a root without Graphic");
                WorldUIConfig.CharacterPreviewDepth.Value=route<2?false:true;
                scene.ModelCamera.GetComponent<Character3DDisplayManager>().enabled=route<2;
                raw.gameObject.SetActive(false);MenuDepthCharacters.Tick();
            }
            scene.Flat.Begin();Canvas.ForceUpdateCanvases();scene.OriginalA.Render();scene.OriginalB.Render();
            Check(MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalA,raw.rectTransform,Vector3.zero,out _,out Matrix4x4 mappingA),"native UI root has an independent physical mapping");
            Check(MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalB,scene.RootB,Vector3.zero,out _,out Matrix4x4 mappingB),"native overlay root has its original physical mapping");
            Matrix4x4 bodyMap=mappingA*BodyMap730(scene.ModelCamera,ground.transform,raw.rectTransform);
            raw.gameObject.SetActive(false);
            GameObject referenceA=CloneUi730(scene.RootA,mappingA),referenceB=CloneUi730(scene.RootB,mappingB);
            RectTransform? referenceCard=cards?(RectTransform)referenceA.transform.Find(cardRect!.name):null;
            if(referenceCard!=null)
            {
                referenceCard.localPosition+=Vector3.forward*cardDepth;
                referenceCard.GetComponent<Image>().enabled=false;
                // An independently opaque physical card writes ordinary mesh
                // depth. The original flat GUI shader's ZWriteOff would let the
                // later native overlay draw through this intended raised plane.
                var physicalCard=GameObject.CreatePrimitive(PrimitiveType.Cube);physicalCard.layer=26;physicalCard.name="independent opaque printed card plane";
                physicalCard.transform.SetParent(referenceCard,false);physicalCard.transform.localPosition=referenceCard.rect.center;
                physicalCard.transform.localScale=new Vector3(referenceCard.rect.width,referenceCard.rect.height,.00001f);
                referenceCardMaterial=new Material(Shader.Find("Unlit/Color"));referenceCardMaterial.color=Color.blue;physicalCard.GetComponent<Renderer>().sharedMaterial=referenceCardMaterial;
            }
            raw.gameObject.SetActive(!annotations);
            var referenceBody=Object.Instantiate(body);referenceBody.name="independent native actor in fixed physical world";referenceBody.layer=26;
            referenceBody.transform.SetParent(null,false);Pose730(referenceBody.transform,bodyMap*body.transform.localToWorldMatrix);
            var referenceMaterial=new Material(Shader.Find("GloomhavenVR.Tests/GlassAperture730"));referenceMaterial.mainTexture=cyanTexture;
            Matrix4x4 worldToGlass=(mappingA*raw.rectTransform.localToWorldMatrix).inverse;
            referenceMaterial.SetMatrix("_WorldToGlass",worldToGlass);
            Rect aperture=raw.rectTransform.rect;
            referenceMaterial.SetVector("_GlassRect",new Vector4(aperture.xMin,aperture.yMin,aperture.xMax,aperture.yMax));
            referenceBody.GetComponent<MeshRenderer>().sharedMaterial=referenceMaterial;
            if(annotations)referenceBody.SetActive(false);
            GameObject? panelDisplay=null;PanelSupersample.Entry? entry=null;
            if(path==1)
            {
                scene.Flat.End();scene.Screen.GetComponent<Renderer>().enabled=false;referenceB.SetActive(false);
                Matrix4x4 matrix=mappingA*scene.RootA.localToWorldMatrix;Vector2 size=scene.RootA.rect.size;Vector2 pivot=scene.RootA.pivot;
                scene.RootA.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                scene.RootA.anchorMin=scene.RootA.anchorMax=Vector2.zero;scene.RootA.pivot=pivot;scene.RootA.sizeDelta=size;Pose730(scene.RootA,matrix);
                panelDisplay=new GameObject("original engaged panel photograph",typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(RawImage));panelDisplay.layer=27;
                var canvas=panelDisplay.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                var rect=(RectTransform)panelDisplay.transform;rect.sizeDelta=size;rect.pivot=pivot;Pose730(rect,matrix);
                panelDisplay.GetComponent<RawImage>().texture=scene.Ui;
                scene.OriginalA.targetTexture=scene.Ui;
                entry=PanelSupersample.BindHead(scene.RootA,scene.OriginalA,scene.Ui,panelDisplay.GetComponent<RawImage>());
                // Converted-window membership is an explicit fixture input;
                // the dedicated card enrollment suite executes the real driver.
                if(cardPlane!=null)cardPlane.Rebind(scene.RootA,cardPlane.DepthGraphics);
                // The source calibration changes with this real conversion lifecycle, not with a head pose.
                MenuDepthCharacters.Tick();
            }
            try
            {
            Vector3 screenPosition=scene.Screen.position,screenScale=scene.Screen.localScale;Quaternion screenRotation=scene.Screen.rotation;
            var ids=new HashSet<int>();var sourceIds=new HashSet<int>();bool sourceDimensionsExact=true;double minimumUi=1,minimumModel=1;
            var aliasingProof=new AliasingProof742();
            Camera.CameraCallback sample=c=>
            {
                if(c==scene.ModelCamera&&c.targetTexture!=scene.ModelTarget)
                {
                    sourceIds.Add(c.targetTexture.GetInstanceID());
                    sourceDimensionsExact &= c.targetTexture.width==MenuDepthHeadRaster.Pixels.x&&c.targetTexture.height==MenuDepthHeadRaster.Pixels.y
                        && c.targetTexture.antiAliasing==1&&!c.targetTexture.useMipMap&&!c.targetTexture.descriptor.useDynamicScale;
                }
            };
            Camera.onPreRender+=sample;
            using(var csv=new StreamWriter(Path.Combine(output,"head-motion-"+path+".csv")))
            {
                csv.WriteLine("frame,eye,uiCorrelation,modelCorrelation,uiContrast,modelContrast");
                for(int step=0;step<(aliasing?12:pitch?7:hardwareRaster||annotations||cards?4:52);step++)
                {
                    int frame=hardwareRaster||annotations||cards?new[]{0,13,40,51}[step]:step;
                    int before=Time.frameCount;while(Time.frameCount==before)yield return null;
                    Vector3 position;
                    if(frame<4)position=Vector3.zero;
                    else if(frame<16){float t=(frame-4)/11f;position=new Vector3(Mathf.Lerp(0,.35f,t),.13f*Mathf.Sin(t*3),Mathf.Lerp(0,.9f,t));}
                    else if(frame<28){float t=(frame-16)/11f;position=Vector3.Lerp(new Vector3(.35f,.13f,.9f),new Vector3(.8f,.12f,1.9f),t);}
                    else if(frame<40){float t=(frame-28)/11f;position=Vector3.Lerp(new Vector3(.8f,.12f,1.9f),new Vector3(.25f,.1f,3.1f),t);}
                    else if(frame==40)position=new Vector3(.8f,.12f,2f); // exact glass-plane crossing
                    else if(frame<48){float t=(frame-41)/6f;position=Vector3.Lerp(new Vector3(.25f,.1f,3.1f),new Vector3(-.3f,-.12f,.4f),t);}
                    else position=Vector3.zero;
                    if(pitch)position=Vector3.zero;
                    if(aliasing)position=new Vector3(step*.00117f,step*.00073f,step*.00143f);
                    Quaternion rotation=pitch?Quaternion.Euler(-18+step*6,0,0):Quaternion.LookRotation(scene.Screen.position-position,Vector3.up);
                    Vector3 left=position-rotation*Vector3.right*.032f,right=position+rotation*Vector3.right*.032f;
                    if(frame==40) left=scene.Screen.TransformPoint(new Vector3(.2f,.03f,0)); // actual eye exactly coplanar, not merely head centre
                    if(frame==40&&path==0)
                    {
                        Check(!MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalA,raw.rectTransform,left,out _),"legacy off-axis mapping retains its coplanar gate");
                        Check(MenuDepthScreenProjection.TryMap(scene.Screen,scene.OriginalA,raw.rectTransform,left,out _,out _,allowCoplanar:true),"fixed head mapping admits the exact physical glass-plane crossing");
                    }
                    scene.Head.transform.SetPositionAndRotation(left,rotation);
                    scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Left,Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(left,rotation,Vector3.one).inverse);
                    scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Right,Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(right,rotation,Vector3.one).inverse);
                    for(int eye=0;eye<2;eye++)
                    {
                        Vector3 eyePosition=eye==0?left:right;scene.Head.transform.SetPositionAndRotation(eyePosition,rotation);
                        scene.Head.worldToCameraMatrix=scene.Head.GetStereoViewMatrix(eye==0?Camera.StereoscopicEye.Left:Camera.StereoscopicEye.Right);
                        int modelBefore=scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>().Draws;
                        int uiBefore=scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws;
                        scene.Head.cullingMask=1<<27;scene.Render(eye!=0);
                        if(frame==40&&path==0&&eye==0)
                        {
                            // This observes the real coordinator after the exact
                            // crossing, before secondary render-count symptoms.
                            // Annotation matrix mapping has its own coplanar use;
                            // the mutation targets only this actual capture eye.
                            bool stereo=scene.Flat.Right!=null&&scene.Flat.Right.IsCreated();
                            File.WriteAllText(Path.Combine(output,"coplanar-coordinator.txt"),"eye="+eyePosition.ToString("R")+";stereo="+stereo
                                +";nativeUiDraws="+(scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws-uiBefore));
                            Check(stereo,"fixed head coordinator retains stereo capture at the exact physical glass-plane crossing");
                        }
                        if(annotations)Check(path==0?scene.Flat.Right!=null:entry!.MenuDepthDisplay!=null,"original outboard annotations admit both real head routes when character depth is off or not ready");
                        Check(scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>().Draws-modelBefore==(!annotations&&eye==0?2:0),
                            "each head stereo pair executes the original native model image effect exactly once per eye");
                        Check(scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws-uiBefore==(eye==0?2:0),
                            "each head stereo pair executes the original native UI image effect exactly once per eye");
                        Texture2D actual=Read(scene.HeadTarget,Path.Combine(output,"actual-"+path+"-"+frame+"-"+eye+".png"));
                        Texture2D? unfiltered=null;
                        Vector3 referenceEye = worldToGlass.MultiplyPoint3x4(eyePosition);
                        if(frame==40&&eye==0)
                        {
                            // The eye was authored by screen.TransformPoint(z=0).
                            // Matrix inversion in this independent reference may
                            // accumulate native-unit epsilon; retain the exact
                            // authored physical plane rather than picking a side.
                            File.WriteAllText(Path.Combine(output,"coplanar-reference-"+path+".txt"),referenceEye.ToString("R"));
                            referenceEye.z=0;
                        }
                        referenceMaterial.SetVector("_Eye",referenceEye);
                        VRRigDriver.HeadCamera=null;scene.Head.cullingMask=1<<26;
                        Texture2D reference;
                        if(aliasing)
                        {
                            scene.Head.Render();unfiltered=Read(scene.HeadTarget,Path.Combine(output,"unfiltered-"+path+"-"+frame+"-"+eye+".png"));
                            reference=SupersampledReference742(scene,Path.Combine(output,"reference-"+path+"-"+frame+"-"+eye+".png"),4);
                        }
                        else reference=SupersampledReference742(scene,Path.Combine(output,"reference-"+path+"-"+frame+"-"+eye+".png"),2);
                        VRRigDriver.HeadCamera=scene.Head;
                        Color32[] actualPixels=actual.GetPixels32(),referencePixels=reference.GetPixels32();
                        if(cards&&frame==0&&eye==0)
                        {
                            int overlap=0;var plane=new Plane(referenceCard!.forward,referenceCard.position);
                            for(int p=0;p<referencePixels.Length;p++)
                            {
                                if(referencePixels[p].g-referencePixels[p].r<30)continue;
                                Ray ray=scene.Head.ViewportPointToRay(new Vector3((p%width+.5f)/width,(p/width+.5f)/height));
                                if(plane.Raycast(ray,out float distance)&&referenceCard.rect.Contains(referenceCard.InverseTransformPoint(ray.GetPoint(distance))))overlap++;
                            }
                            File.WriteAllText(Path.Combine(output,"real-figure-card-overlap-"+path+".txt"),overlap.ToString());
                            Check(overlap>20,"the independent original figure really stands in front of the raised native card");
                        }
                        long actualInk=0,referenceInk=0;
                        for(int p=0;p<actualPixels.Length;p++){actualInk+=Math.Max(actualPixels[p].r,actualPixels[p].g);referenceInk+=Math.Max(referencePixels[p].r,referencePixels[p].g);}
                        Check(referenceInk<1000||actualInk>referenceInk/10,"actual final head retains original window and figure ink for both eyes");
                        double ui=Corr730(actualPixels,referencePixels,false,out double uiContrast);
                        double model=Corr730(actualPixels,referencePixels,true,out double modelContrast);
                        csv.WriteLine(frame+","+eye+","+ui+","+model+","+uiContrast+","+modelContrast);csv.Flush();
                        minimumUi=Math.Min(minimumUi,ui);minimumModel=Math.Min(minimumModel,model);
                        if(aliasing)
                        {
                            aliasingProof.Add(actual,unfiltered!,reference);
                            Object.DestroyImmediate(unfiltered);
                        }
                        else
                        {
                            Check(ui>.99,"actual final head text and XY print retain direct native-world pixel detail during movement");
                            if(!annotations)Check(model>.97,"actual final head figure retains direct native-world XY surface detail during movement");
                        }
                        Check(sourceDimensionsExact,"native scene scratch always retains actual head pixel dimensions during movement");
                        Check(scene.Screen.position==screenPosition&&scene.Screen.localScale==screenScale&&scene.Screen.rotation==screenRotation,"moving head raster never moves the original physical window");
                        if(path==0&&scene.Flat.Right!=null)
                        {
                            ids.Add(scene.Flat.Right.GetInstanceID());
                            Check(MenuDepthHeadRaster.Matches(scene.Flat.Right,scene.Ui.descriptor,new Vector2Int(width,height)),"final NativeFlat raster has the exact normalized head descriptor");
                        }
                        if(entry!=null)
                        {
                            Check(entry.MenuDepthDisplay!=null,"actual panel coordinator keeps the fixed head display admitted");
                            Check(entry.MenuDepthBytes==MenuDepthHeadRaster.Bytes(scene.Ui.descriptor,new Vector2Int(width,height)),"actual panel ledger charges both exact head rasters independently of mono budget");
                        }
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                    }
                }
            }
            Camera.onPreRender-=sample;
            Check(annotations?sourceIds.Count==0:sourceIds.Count<12,"continuous motion reuses bounded head-grid capacity instead of reallocating on every pose");
            if(aliasing)aliasingProof.Complete(Path.Combine(output,"antialiasing-"+path+".txt"));
            if(path==0)Check(ids.Count==1,"continuous motion retains one final right-eye head raster identity");
            File.WriteAllText(Path.Combine(output,"summary-"+path+".txt"),"UI="+minimumUi+",model="+minimumModel+",targetIDs="+ids.Count+",sourceIDs="+sourceIds.Count);
            }
            finally
            {
            if(annotation!=null)MenuDepthSources.Unregister(annotation);
            WorldUIConfig.CharacterPreviewDepth.Value=true;
            if(entry!=null)PanelSupersample.EndHead(entry);if(panelDisplay!=null)Object.DestroyImmediate(panelDisplay);
            Object.DestroyImmediate(referenceA);Object.DestroyImmediate(referenceB);Object.DestroyImmediate(referenceBody);
            Object.DestroyImmediate(referenceMaterial);
            Object.DestroyImmediate(texture);Object.DestroyImmediate(cyanTexture);Object.DestroyImmediate(material);Object.DestroyImmediate(bodyMaterial);
            if(cardMaterial!=null)Object.DestroyImmediate(cardMaterial);
            if(referenceCardMaterial!=null)Object.DestroyImmediate(referenceCardMaterial);
            }
        }
        complete(_count);
    }
}
