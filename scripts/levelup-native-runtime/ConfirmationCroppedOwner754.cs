using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    [Serializable] private sealed class EscapeLayout754
    {
        public float columnWidth=0f;
        public Vector2 anchorMin=default,anchorMax=default,pivot=default,anchoredPosition=default;
    }

    public static IEnumerator RunCroppedConfirmation754(string output)
    {
        _checks=0;WorldUIConfig.ConversionActive=true;FlatScreen.ManualScreenActive=false;
        VRModeStateMachine.TableInFrontOfPlayer=true;MenuWorldContext.IsReady=false;
        NativeFont();
        var harmony=_harmony=new Harmony("GloomhavenVR.CroppedConfirmation754."+typeof(LevelUpNativeProgram).Assembly.GetName().Name);
        foreach(Type patch in new[]{typeof(ConfirmationProducerScopePatch),typeof(ConfirmationRequestOriginPatch)})
            harmony.CreateClassProcessor(patch).Patch();
        var run=CroppedConfirmation754(output,harmony);while(run.MoveNext())yield return run.Current;
        harmony.UnpatchSelf();_harmony=null;
        yield return _checks;
    }

    private static IEnumerator CroppedConfirmation754(string output,Harmony harmony,float rigScale=1f,bool perspective=false)
    {
        var authored=JsonUtility.FromJson<EscapeLayout754>(File.ReadAllText(Path.Combine(_inputs,"native-escape-layout.json")));
        UIConfirmationBoxManager? previous=Singleton<UIConfirmationBoxManager>.Instance;
        Camera? previousCamera=CanvasConversion.WorldCamera;
        bool addedEvent=EventSystem.current==null;
        var eventObject=addedEvent?new GameObject("cropped confirmation pointer",typeof(EventSystem)):null;
        var root=Rect("actual scenario ESC producer frame",null,1920,1080);
        var ownerWindow=root.gameObject.AddComponent<UIWindow>();
        var source=root.gameObject.AddComponent<ESCMenu>();
        var owner=CanvasConversion.Convert(root,"scenario ESC fitted owner")!;
        var column=Rect("UI Menu Panel",root,authored.columnWidth,0);
        column.anchorMin=authored.anchorMin;column.anchorMax=authored.anchorMax;
        column.pivot=authored.pivot;column.sizeDelta=new Vector2(authored.columnWidth,0);
        column.anchoredPosition=authored.anchoredPosition;
        // This is the settled output of ordinary content fitting: it re-centers
        // the original narrow left column while retaining the full native root.
        // Native scene bootstrap, fitting and gameplay/session controllers are
        // explicit surrounding inputs. Production owner selection/pose executes.
        Vector3 nativeColumnCenter=owner.HostRect.InverseTransformPoint(column.TransformPoint(column.rect.center));
        owner.HostRect.sizeDelta=new Vector2(authored.columnWidth+24,700);
        root.anchoredPosition=-(Vector2)nativeColumnCenter;
        owner.HostRect.SetPositionAndRotation(new Vector3(2,3,4)*rigScale,Quaternion.identity);
        owner.HostRect.localScale=Vector3.one*(.001f*rigScale);
        Check(Vector2.Distance(owner.HostRect.InverseTransformPoint(column.TransformPoint(column.rect.center)),Vector2.zero)<.01f,
            "native left-anchored ESC column is centered in its fitted visible frame");
        var home=Rect("native persistent confirmation home",null,1920,1080);Object.DontDestroyOnLoad(home.gameObject);
        var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-confirmation-tree.xml"));
        var imported=ImportNode739(document.DocumentElement!,home,new Dictionary<string,TMP_FontAsset>());
        imported.Find("Box/Box Content/Swappable Content/Secondary Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Swappable Content/Secondary General Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Report content").gameObject.SetActive(false);
        var body=imported.Find("Box/Box Content/Swappable Content/Primary Content/Text").GetComponent<TMP_Text>();
        var box=imported.gameObject.AddComponent<ConfirmationBox>();
        var nativeCanvas=imported.gameObject.AddComponent<Canvas>();nativeCanvas.renderMode=RenderMode.WorldSpace;
        var nativeRaycaster=imported.gameObject.AddComponent<GraphicRaycaster>();
        var window=imported.gameObject.AddComponent<UIWindow>();box.IsOpen=window.IsOpen=window.IsVisible=false;
        var manager=new UIConfirmationBoxManager{CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;source.Manager=manager;
        var eye=new GameObject("actual cropped-owner eye").AddComponent<Camera>();eye.enabled=false;
        eye.orthographic=!perspective;eye.orthographicSize=.8f;eye.fieldOfView=65f;eye.aspect=4f/3f;
        eye.nearClipPlane=perspective?Mathf.Clamp(.05f*rigScale,.01f,5f):.01f;eye.farClipPlane=1000f*Mathf.Max(rigScale,1f);
        eye.clearFlags=CameraClearFlags.SolidColor;eye.backgroundColor=Color.black;eye.cullingMask=(1<<5)|(1<<26);
        var target=new RenderTexture(800,600,24);target.Create();eye.targetTexture=target;CanvasConversion.WorldCamera=eye;
        nativeCanvas.worldCamera=eye;
        owner.HostCanvas.worldCamera=eye;
        int explicitClicks=0;
        var yes=imported.Find("Box/Box Content/Buttons/Yes").gameObject.AddComponent<Button>();yes.onClick.AddListener(()=>explicitClicks++);
        var no=imported.Find("Box/Box Content/Buttons/No").gameObject.AddComponent<Button>();no.onClick.AddListener(()=>explicitClicks++);
        var rows=new List<string>();RectTransform? nested=null;
        try
        {
            foreach(string route in new[]{"LoadMainMenu","ExitGame","nested"})
            {
                ESCMenu producer=source;RectTransform expectedMount=root;
                if(route=="nested")
                {
                    nested=Rect("actual nested native ESC page",root,368,600);nested.anchoredPosition=(Vector2)nativeColumnCenter+new Vector2(0,-24);
                    nested.gameObject.AddComponent<UIWindow>();producer=nested.gameObject.AddComponent<ESCMenu>();producer.Manager=manager;
                    expectedMount=nested;
                }
                body.text=route=="ExitGame"?"Exit Game?":"Return to Main Menu?";
                AccessTools.DeclaredMethod(typeof(ESCMenu),route=="ExitGame"?"ExitGame":"LoadMainMenu").Invoke(producer,null);
                ConfirmationOnOwner.Tick();
                Check(ConfirmationOnOwner.IsOwned(window),"actual native escape producer keeps the original confirmation inside its visible owner");
                var member=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,imported))!;
                Check(!imported.IsChildOf(root)&&imported.gameObject.scene.name=="DontDestroyOnLoad",
                    "cropped escape containment retains original persistent lifetime without adopting the gameplay producer");
                for(int frame=0;frame<3;frame++)
                {
                    owner.HostRect.SetPositionAndRotation(new Vector3(2+frame*.07f,3-frame*.03f,4)*rigScale,Quaternion.Euler(0,frame*13,0));
                    owner.HostRect.localScale=Vector3.one*((.0008f+frame*.0002f)*rigScale);
                    // Native/fixed-fit presentation can scale or turn the target
                    // independently of its physical host. Center repair must not
                    // silently replace this inherited metric with the host metric.
                    root.localScale=new Vector3(1f-frame*.08f,1f-frame*.05f,1f+frame*.07f);
                    root.localRotation=Quaternion.Euler(0,frame*3,0);
                    ConfirmationOnOwner.Tick();
                    for(int sweep=0;sweep<8;sweep++)PopupBlackout759.Sweep(window);
                    Check(nativeCanvas.enabled,"actual native owner lease prevents the original blackout sweep from disabling its pooled canvas");
                    Vector3 center=route=="nested"?expectedMount.position:owner.HostRect.position;
                    Vector3 expected=center+expectedMount.TransformVector(new Vector3(0,0,-32));
                    eye.transform.SetPositionAndRotation(center-expectedMount.forward*(perspective?1.4f*rigScale:2f),expectedMount.rotation);
                    Texture2D actual=Capture(eye,Path.Combine(output,"cropped-"+route+"-"+frame+".png"));
                    Vector3 actualPosition=member.HostRect.position;Quaternion actualRotation=member.HostRect.rotation;
                    member.HostRect.SetPositionAndRotation(expected,expectedMount.rotation);
                    Texture2D reference=Capture(eye,Path.Combine(output,"cropped-reference-"+route+"-"+frame+".png"));
                    member.HostRect.SetPositionAndRotation(actualPosition,actualRotation);
                    // This converter-service port renders directly. At very small
                    // scales its unnormalized native SDF loses glyphs in BOTH
                    // independent images; frame pixels/pose/input remain causal.
                    // Text is separately required by the real head coordinator.
                    Check((perspective?FramePixels759(reference)>100:BrightPixels(reference)>20)&&Difference(actual,reference)<.01f,
                        "whole-window confirmation final pixels stay on fitted visible owner");
                    Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                    Check(Vector3.Distance(member.HostRect.position,expected)<.00001f*Mathf.Max(rigScale,1f)
                        &&Vector3.Distance(member.HostRect.lossyScale,expectedMount.lossyScale)<.000001f*Mathf.Max(rigScale,1f)
                        &&Quaternion.Angle(member.HostRect.rotation,expectedMount.rotation)<.01f,
                        "root and nested confirmations preserve exact accepted visible owner pose through drag and resize");
                    foreach(Button button in new[]{yes,no})
                    {
                        if(perspective)
                        {
                            Vector3 direction=(button.transform.position-eye.transform.position).normalized;
                            Check(GloomhavenVR.Hands.Interact.PopupRay759.Test(member.HostCanvas,eye.transform.position,direction,20f*rigScale,out _,out _),
                                "perspective native popup accepts the real world ray before original graphic-button dispatch");
                        }
                        var data=new PointerEventData(EventSystem.current){position=eye.WorldToScreenPoint(button.transform.position)};
                        var hits=new List<RaycastResult>();nativeRaycaster.Raycast(data,hits);
                        Check(hits.Exists(hit=>hit.gameObject==button.gameObject||hit.gameObject.transform.IsChildOf(button.transform)),
                            "visible original escape confirmation buttons retain their actual native graphic hit");
                    }
                    rows.Add(route+" frame="+frame+" rig="+rigScale+" near="+eye.nearClipPlane+" nativeRootOffset="+root.anchoredPosition+" host="+member.HostRect.position);
                }
                Check(explicitClicks==0,"presentation never invokes an exit or decline callback");
                ExecuteEvents.Execute(yes.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
                Check(explicitClicks==1,"explicit pointer click reaches the original confirmation control exactly once");explicitClicks=0;
                box.IsOpen=window.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();
                Check(imported.parent==home&&!ConfirmationOnOwner.IsOwned(window),"native escape close restores its exact pooled home");
            }
            File.WriteAllLines(Path.Combine(output,"cropped-owner754.txt"),rows);
        }
        finally
        {
            box.IsOpen=window.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();
            Singleton<UIConfirmationBoxManager>.Instance=previous;CanvasConversion.WorldCamera=previousCamera;
            CanvasConversion.Release(owner);Object.DestroyImmediate(root.gameObject);Object.DestroyImmediate(home.gameObject);
            Object.DestroyImmediate(eye.gameObject);target.Release();Object.DestroyImmediate(target);
            if(eventObject!=null)Object.DestroyImmediate(eventObject);
        }
        yield return null;
    }
}
