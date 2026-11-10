using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private static int White759(Texture2D image)
    {
        int count=0;Color32[] data=image.GetPixels32();
        for(int y=image.height*35/100;y<image.height*65/100;y++)for(int x=image.width*30/100;x<image.width*70/100;x++)
        {Color32 p=data[y*image.width+x];if(p.r>90&&p.g>90&&p.b>90&&Math.Abs(p.r-p.g)<6&&Math.Abs(p.g-p.b)<6)count++;}
        return count;
    }

    // Native owner admission/scene bootstrap are explicit inputs as in745.
    // The exact shipped confirmation pose kernel, owned-member capture and
    // complete SS/MenuDepth head coordinator execute, including both final eyes.
    public static IEnumerator HeadPopup759(string output,Action<int> complete)
        =>HeadPopupCore759(output,complete,new[]{1.15f,1f,47.69f,200f});
    // Explicit diagnostic only: the observed.02 native-SDF loss is retained;
    // later values are not claimed as executed when that first witness fails.
    public static IEnumerator HeadPopupLow759(string output,Action<int> complete)
        =>HeadPopupCore759(output,complete,new[]{.02f,.05f,.2f,10f});
    private static IEnumerator HeadPopupCore759(string output,Action<int> complete,float[] scales)
    {
        _count=0;DesktopEyeInput.Available=true;
        foreach(float rig in scales)
        foreach(bool owned in new[]{true})
        using(var scene=new Scene())
        using(var font=new NativeFont742(output))
        {
            MenuDepthCharacters.Shutdown();scene.HideSpatial730();
            foreach(var g in scene.RootA.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            foreach(var g in scene.RootB.GetComponentsInChildren<Graphic>(true))g.enabled=false;
            scene.Screen.GetComponent<Renderer>().enabled=false;
            scene.HeadTarget.Release();scene.HeadTarget.width=640;scene.HeadTarget.height=480;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=65f;scene.Head.aspect=4f/3f;
            scene.Head.nearClipPlane=Mathf.Clamp(.05f*rig,.01f,5f);scene.Head.farClipPlane=Mathf.Max(1000,1000*rig);
            Vector3 origin=new Vector3(19.99f,14.72f,23.49f)*(rig/47.69f);Quaternion facing=Quaternion.Euler(0,16.6f,0);
            scene.Head.transform.SetPositionAndRotation(origin,facing);
            scene.OriginalA.cullingMask=1<<24;scene.OriginalA.targetTexture=scene.Ui;scene.OriginalA.enabled=false;
            var host=ConfirmationCanvas745("actual fitted popup producer759",24,scene.OriginalA,new Vector2(412,1080),origin+facing*Vector3.forward*(1.4f*rig));
            host.rotation=facing;host.localScale=Vector3.one*(.00105f*rig);
            var display=ConfirmationCanvas745("actual producer photograph759",27,scene.Head,host.rect.size,host.position);
            display.rotation=host.rotation;display.localScale=host.localScale;
            var image=display.gameObject.AddComponent<RawImage>();image.texture=scene.Ui;
            var entry=PanelSupersample.BindHead(host,scene.OriginalA,scene.Ui,image);
            // Production BuildCamera/SyncProjection keep the private capture
            // parented to this host while original canvases use the actual head
            // for native TMP/input calculations. The desktop fixture's unrelated
            // original screen-camera transform is not this converted-window input.
            scene.OriginalA.transform.SetParent(host,false);scene.OriginalA.transform.localPosition=new Vector3(0,0,-1080);
            scene.OriginalA.transform.localRotation=Quaternion.identity;scene.OriginalA.transform.localScale=Vector3.one;
            host.GetComponent<Canvas>().worldCamera=scene.Head;
            var nativeTarget=new GameObject("original full-screen ESC root759",typeof(RectTransform));nativeTarget.layer=24;
            nativeTarget.transform.SetParent(host,false);var targetRect=(RectTransform)nativeTarget.transform;
            targetRect.sizeDelta=new Vector2(1920,1080);targetRect.anchoredPosition=new Vector2(769,0);entry.Panel.Target=targetRect;
            var column=new GameObject("original opaque ESC menu column759",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            column.layer=24;column.transform.SetParent(targetRect,false);var columnInk=column.GetComponent<Image>();
            columnInk.color=new Color(.16f,.10f,.045f);columnInk.rectTransform.sizeDelta=new Vector2(368,1080);
            columnInk.rectTransform.anchoredPosition=new Vector2(-769,0);
            var member=ConfirmationCanvas745("native confirmation759",owned?23:24,scene.OriginalA,new Vector2(700,440),host.position);
            Object.DontDestroyOnLoad(member.gameObject);var memberPanel=PanelSupersample.MemberPanel(member);
            member.GetComponent<Canvas>().worldCamera=scene.Head;
            var original=ConfirmationCanvas745("original pooled confirmation canvas759",owned?23:24,scene.Head,new Vector2(700,440),member.position);
            original.SetParent(member,false);original.localScale=Vector3.one;original.localPosition=Vector3.zero;
            memberPanel.Target=original;var nativeCanvas=original.GetComponent<Canvas>();
            ConfirmationOnOwner.Owned=nativeCanvas;
            var plate=new GameObject("original dark popup body759",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            plate.layer=member.gameObject.layer;plate.transform.SetParent(original,false);var background=plate.GetComponent<Image>();background.color=new Color(.07f,.07f,.07f);
            background.rectTransform.sizeDelta=new Vector2(700,440);
            var button=new GameObject("original native choice759",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));
            button.layer=member.gameObject.layer;button.transform.SetParent(original,false);var ink=button.GetComponent<Image>();
            ink.color=new Color(.25f,.18f,.08f);ink.rectTransform.sizeDelta=new Vector2(307,65);ink.rectTransform.anchoredPosition=new Vector2(0,-125);
            font.Label(button.transform,"original native choice glyphs759","Continue",new Vector2(307,65),Vector2.zero,30);
            font.Label(original,"original native question glyphs759","Are you sure?",new Vector2(640,100),new Vector2(0,65),34);
            int clicks=0;button.GetComponent<Button>().onClick.AddListener(()=>clicks++);
            var events=new GameObject("actual original popup EventSystem759",typeof(EventSystem));
            PanelSupersample.Entry? standaloneEntry=null;
            try
            {
                if(owned)
                {
                    PopupPlacement759.Place(entry.Panel,memberPanel,targetRect);
                    PanelSupersample.BindConfirmationMember(entry.Panel,memberPanel);
                }
                else
                {
                    // Exact original standalone position/metric: DialogSurface
                    // places at .75 tracking metres and .7 times the UI scale.
                    // Its converter camera is represented by an admitted entry.
                    member.SetPositionAndRotation(origin+facing*Vector3.forward*(.75f*rig),facing);member.localScale=Vector3.one*(.001f*.7f*rig);
                    var standaloneDisplay=ConfirmationCanvas745("standalone native photograph759",27,scene.Head,member.rect.size,member.position);
                    standaloneDisplay.rotation=member.rotation;standaloneDisplay.localScale=member.localScale;
                    var standaloneImage=standaloneDisplay.gameObject.AddComponent<RawImage>();standaloneImage.texture=scene.Background;
                    standaloneEntry=PanelSupersample.BindHead(member,scene.OriginalA,scene.Background,standaloneImage);
                    PanelSupersample.ForgetMember(memberPanel); // Standalone has its own original entry, not an owned member.
                    entry.Panel.OwnerRenderHidden=true;
                }
                Canvas.ForceUpdateCanvases();
                string prefix=(owned?"owned-":"standalone-")+rig.ToString(CultureInfo.InvariantCulture);
                for(int pose=0;pose<2;pose++)
                {
                    int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;
                    // The original is a live, separately owned native Canvas,
                    // exactly as the current758 replay. Execute the shipped
                    // eligibility and canvas-disable loop after reveal; admission
                    // mode/refusal/ledger scheduling are explicit inputs here.
                    for(int sweep=0;sweep<8;sweep++)PopupBlackout759.Sweep(nativeCanvas);
                    Vector3 head=origin+facing*new Vector3(pose*.012f,pose*.004f,pose*.005f)*rig;
                    scene.Head.transform.SetPositionAndRotation(head,facing);
                    foreach(Camera.StereoscopicEye eye in new[]{Camera.StereoscopicEye.Left,Camera.StereoscopicEye.Right})
                    {
                        Vector3 at=head+facing*Vector3.right*((eye==Camera.StereoscopicEye.Left?-.032f:.032f)*rig);
                        scene.Head.SetStereoViewMatrix(eye,Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(at,facing,Vector3.one).inverse);
                    }
                    for(int eye=0;eye<2;eye++)
                    {
                        scene.Head.cullingMask=1<<27;scene.Render(eye!=0);
                        var actual=Read(scene.HeadTarget,Path.Combine(output,prefix+"-"+pose+"-"+eye+".png"));
                        var current=owned?entry:standaloneEntry!;
                        File.AppendAllText(Path.Combine(output,"popup759-pixels.csv"),prefix+","+pose+","+eye+","+White759(actual)+","+current.MenuDepthFailures+","+member.position+","+member.lossyScale+"\n");
                        Check(White759(actual)>20,nativeCanvas.enabled
                            ?"captured original popup glyphs stay in the expected visible producer region at every world scale"
                            :"producer-owned original confirmation survives the real blackout sweep in final head pixels");
                        Check(current.MenuDepthDisplay!=null&&current.MenuDepthFailures==0,
                            "real popup head coordinator keeps its original admitted capture at every world scale");
                        Object.DestroyImmediate(actual);
                    }
                    Check(nativeCanvas.enabled,"producer-owned original confirmation keeps its revealed native canvas enabled");
                }
                Check(clicks==0,"capturing native popup eyes never invokes an original choice callback");
                ExecuteEvents.Execute(button,new PointerEventData(events.GetComponent<EventSystem>()){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
                Check(clicks==1,"captured original popup choice retains its explicit native callback");
                var unconverted=new GameObject("genuine unconverted native window759",typeof(RectTransform),typeof(Canvas));
                PopupBlackout759.Sweep(unconverted.GetComponent<Canvas>());
                Check(!unconverted.GetComponent<Canvas>().enabled,"the original blackout still suppresses a genuinely unconverted native window");Object.DestroyImmediate(unconverted);
                ConfirmationOnOwner.Owned=null;PopupBlackout759.Sweep(nativeCanvas);
                Check(!nativeCanvas.enabled,"retired native owner does not leak its exemption into the next pooled opening");
                nativeCanvas.enabled=true;ConfirmationOnOwner.Owned=nativeCanvas;PopupBlackout759.Sweep(nativeCanvas);
                Check(nativeCanvas.enabled,"reopened producer-owned native canvas retains its own new presentation lease");
            }
            finally
            {
                if(standaloneEntry!=null)PanelSupersample.EndHead(standaloneEntry);
                else PanelSupersample.ForgetMember(memberPanel);
                PanelSupersample.EndHead(entry);Object.DestroyImmediate(member.gameObject);Object.DestroyImmediate(host.gameObject);Object.DestroyImmediate(display.gameObject);Object.DestroyImmediate(events);
            }
        }
        complete(_count);
    }
}
