extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private static IEnumerator ConfirmationFrames738(Camera head,ConvertedPanel panel,RectTransform ownerRoot,
        ConfirmationBox box,Image blur,string output,string route)
    {
        Vector3 rootPosition=ownerRoot.position,rootScale=ownerRoot.localScale;Quaternion rootRotation=ownerRoot.rotation;
        Vector3 headPosition=head.transform.position;Quaternion headRotation=head.transform.rotation;
        bool ortho=head.orthographic;float near=head.nearClipPlane,far=head.farClipPlane,fov=head.fieldOfView;int mask=head.cullingMask;
        // Exact hardware-scale geometry; frame/sample coordinates are explicit
        // inputs. Projection, UI raster, original TMP/button shader and filtered
        // final head photograph execute. No actual native Windows FX are claimed.
        ownerRoot.SetPositionAndRotation(new Vector3(267.92f,174.92f,58.59f),Quaternion.Euler(0,37,0));
        ownerRoot.localScale=Vector3.one*.208f;
        if(route=="owned")ConfirmationOnOwner.Tick();
        else {panel.HostRect.SetPositionAndRotation(ownerRoot.position,ownerRoot.rotation);panel.HostRect.localScale=ownerRoot.lossyScale;}
        head.orthographic=false;head.nearClipPlane=1f;head.farClipPlane=2000;head.fieldOfView=60;head.cullingMask=(1<<5)|(1<<26);
        var samples=new List<string>();var differences=new List<float>();float worst=0;
        try
        {
            using(var capture=new PanelSupersample.ConfirmationCapture(panel))
            {
                for(int frame=0;frame<12;frame++)
                {
                    for(int eye=0;eye<2;eye++)
                    {
                        Transform mount=panel.HostRect;
                        Vector3 centre=mount.position;
                        head.transform.position=centre-mount.forward*334f+mount.right*((eye==0?-6.34f:6.34f)+frame*.013f)
                            +mount.up*(Mathf.Sin(frame*.7f)*.07f);
                        head.transform.rotation=Quaternion.LookRotation(centre-head.transform.position,mount.up);
                        capture.Draw();
                        string suffix=frame==0||frame==11?Path.Combine(output,route+"-confirmation-"+frame+"-"+eye+".png"):"";
                        Texture2D actual=Capture(head,suffix);
                        bool enabled=blur.enabled;blur.enabled=false;capture.Draw();
                        Texture2D reference=Capture(head,frame==0?Path.Combine(output,route+"-confirmation-reference-"+eye+".png"):"");
                        blur.enabled=enabled;
                        Check(BrightPixels(reference)>100,route+" confirmation reference retains visible native glyphs in every final head photograph");
                        float difference=Difference(actual,reference);worst=Mathf.Max(worst,difference);
                        differences.Add(difference);
                        samples.Add(frame+","+eye+","+difference.ToString("R")+","+BrightPixels(actual)+","+BrightPixels(reference));
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                    }
                    yield return null;
                }
                // Record the whole time sequence before judging each unchanged
                // per-frame oracle, including the failing historical variants.
                foreach(float difference in differences)
                    Check(difference<.00001f,route+" confirmation keeps final pixels independent of its native full-frame GrabPass");
                Check(capture.Active&&capture.Captures==48,"confirmation takes an isolated mono capture with resolved mips and a noninteractive display photograph");
            }
        }
        finally
        {
            ownerRoot.SetPositionAndRotation(rootPosition,rootRotation);ownerRoot.localScale=rootScale;
            head.transform.SetPositionAndRotation(headPosition,headRotation);head.orthographic=ortho;head.nearClipPlane=near;head.farClipPlane=far;head.fieldOfView=fov;head.cullingMask=mask;
            File.WriteAllLines(Path.Combine(output,route+"-confirmation-frames.csv"),samples);
            File.WriteAllText(Path.Combine(output,route+"-confirmation-limits.txt"),"Production admission/camera/projection/neutralization/resolve/display kernels run. Allocator/layer lease/outer loop and session inputs are explicit ports. Native full-stretch Blur identity, One/Zero+LEqual+ZWriteOn states are independently parsed; GL fragment is an explicit GrabPass mechanism witness, not original D3D11 program pixels. Max native-backdrop contribution="+worst);
        }
    }

    private static IEnumerator ConfirmationPresentation738(Camera head,RectTransform ownerRoot,UILevelUpWindow owner,
        UIConfirmationBoxManager manager,ConfirmationBox box,UIWindow boxWindow,RectTransform boxRect,string output)
    {
        var originalBlur=Image(boxRect,"original full-stretch Blur",1920,1080,Color.white);originalBlur.transform.SetAsFirstSibling();
        originalBlur.raycastTarget=false;originalBlur.material=new Material(Resources.Load<Shader>("ConfirmationGrab738"));
        Text(boxRect,"original confirmation question","Take this ability card?",550,100).rectTransform.anchoredPosition=new Vector2(0,40);
        var yesRect=Rect("original Yes button",boxRect,190,65);yesRect.anchoredPosition=new Vector2(-120,-55);
        var noRect=Rect("original No button",boxRect,190,65);noRect.anchoredPosition=new Vector2(120,-55);
        native::ExtendedButton MakeButton(RectTransform rect,string label)
        {
            var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.2f,.2f,.2f,1);
            var button=rect.gameObject.AddComponent<native::ExtendedButton>();button.targetGraphic=image;button.useAudioController=false;button.textLanguageKey="";
            Text(rect,"original "+label+" glyphs",label,180,60).alignment=TMPro.TextAlignmentOptions.Center;
            return button;
        }
        var yes=MakeButton(yesRect,"YES");var no=MakeButton(noRect,"NO");int confirms=0,cancels=0;
        void Close(){box.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=false;}
        yes.onClick.AddListener(()=>{confirms++;Close();});no.onClick.AddListener(()=>{cancels++;Close();});
        var member=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,boxRect))!;
        IEnumerator frames=ConfirmationFrames738(head,member,ownerRoot,box,originalBlur,output,"owned");while(frames.MoveNext())yield return frames.Current;
        Check(box.gameObject.scene.name=="DontDestroyOnLoad"&&box.IsOpen,"filtered confirmation retains its original persistent scene and native open state");
        member.HostRaycaster.enabled=false;ConfirmationOnOwner.Tick();
        Check(member.HostRaycaster.enabled,"filtered confirmation stays usable under the containing modal raycaster lock");
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        _allowNativeButton=false;yes.OnPointerClick(pointer);Check(confirms==0&&box.IsOpen,"filtered original Yes respects its native permission gate");
        _allowNativeButton=true;yes.OnPointerClick(pointer);_allowNativeButton=false;ConfirmationOnOwner.Tick();
        Check(confirms==1&&cancels==0&&!box.IsOpen,"filtered original Yes dispatches once and lets the native close retire presentation");

        // The same original pooled box can next belong to an unowned producer.
        // Exercise the actual historical standalone DialogSurface route too.
        var previous=ConfirmationOnOwner.CurrentScope;ConfirmationOnOwner.CurrentScope=null;ConfirmationOnOwner.Record(manager);ConfirmationOnOwner.CurrentScope=previous;
        box.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=true;
        var surface=new DialogSurface();surface.Tick();
        var standalone=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,boxRect))!;
        originalBlur.enabled=true;originalBlur.color=Color.white;originalBlur.material=new Material(Resources.Load<Shader>("ConfirmationGrab738"));
        frames=ConfirmationFrames738(head,standalone,ownerRoot,box,originalBlur,output,"standalone");while(frames.MoveNext())yield return frames.Current;
        standalone.HostRaycaster.enabled=false;surface.Tick();Check(standalone.HostRaycaster.enabled,"standalone filtered confirmation retains the native modal input exemption");
        _allowNativeButton=true;no.OnPointerClick(pointer);_allowNativeButton=false;surface.Tick();
        Check(confirms==1&&cancels==1&&!box.IsOpen,"filtered original No dispatches once without inventing confirmation");
        box.IsOpen=boxWindow.IsOpen=boxWindow.IsVisible=true;surface.Tick();
        Check(CanvasConversion.ActivePanels.Exists(p=>ReferenceEquals(p.Target,boxRect))&&box.gameObject.scene.name=="DontDestroyOnLoad","filtered standalone pooled reopen restores its original native dialog and persistent lifetime");
        FlatScreen.ManualScreenActive=true;surface.Tick();Check(box.IsOpen&&!CanvasConversion.ActivePanels.Exists(p=>ReferenceEquals(p.Target,boxRect)),"manual flat rescue retires filtered presentation without answering the native dialog");
        FlatScreen.ManualScreenActive=false;surface.Tick();Check(box.IsOpen&&CanvasConversion.ActivePanels.Exists(p=>ReferenceEquals(p.Target,boxRect)),"return to VR restores the same native confirmation without a new show edge");
        surface.Shutdown();Record(manager,owner);ConfirmationOnOwner.Tick();
        Object.DestroyImmediate(yesRect.gameObject);Object.DestroyImmediate(noRect.gameObject);
    }
}
