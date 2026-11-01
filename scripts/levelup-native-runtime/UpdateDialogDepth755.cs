using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    public static IEnumerator RunUpdateDialog755(string output)
    {
        _checks=0;NativeFont();WorldUIAssets.Font755=_nativeFont;
        CanvasConversion.ActivePanels.Clear();CanvasConversion.ResetOrder739();
        var eye=new GameObject("update prompt head",typeof(Camera)).GetComponent<Camera>();
        eye.enabled=false;eye.orthographic=true;eye.orthographicSize=.45f;eye.aspect=4f/3;
        eye.nearClipPlane=.01f;eye.farClipPlane=10;eye.clearFlags=CameraClearFlags.SolidColor;
        eye.backgroundColor=Color.black;eye.cullingMask=~0;
        var target=new RenderTexture(640,480,24);target.Create();eye.targetTexture=target;
        CanvasConversion.WorldCamera=eye;
        var events=new GameObject("update prompt pointer",typeof(EventSystem));
        var rod=GameObject.CreatePrimitive(PrimitiveType.Cube);
        rod.name="depth-writing main-menu rod witness";
        rod.transform.position=new Vector3(0,0,2.2f);rod.transform.localScale=new Vector3(.95f,.04f,.01f);
        var rodMaterial=new Material(Resources.Load<Shader>("Overlay755"));
        rodMaterial.color=Color.cyan;rodMaterial.SetFloat("_ZWrite",1);
        rod.GetComponent<Renderer>().sharedMaterial=rodMaterial;
        rod.GetComponent<Renderer>().sortingOrder=4; // Original GrabBarLayout.BarOrderOffset.
        var hand=GameObject.CreatePrimitive(PrimitiveType.Cube);hand.name="near solid hand witness";
        hand.transform.position=new Vector3(0,0,1.5f);hand.transform.localScale=new Vector3(.07f,.10f,.02f);
        var handMaterial=new Material(Shader.Find("Unlit/Color")){color=Color.green};
        hand.GetComponent<Renderer>().sharedMaterial=handMaterial;hand.SetActive(false);
        var dialog=new SelfUpdateDialog();int update=0,ignore=0,close=0;
        try
        {
            dialog.ShowChoice("Update available","A newer release is ready.","Ignore","Update",()=>ignore++,()=>update++);
            // Main menu body geometry is deliberately absent: this proof isolates
            // the reported later overlay renderer against the complete real
            // update canvas. Rod geometry and placement are surrounding inputs;
            // shader/depth, production UI and actual ordering execute unchanged.
            var root=UpdateRoot755(dialog);
            var canvas=root.GetComponent<Canvas>();
            Check(root!=null&&canvas!=null,"whole production update prompt creates its original canvas");
            for(int mode=0;mode<3;mode++)
            {
                if(mode==1)dialog.ShowProgress("Downloading","Cancel",()=>close++);
                if(mode==2)dialog.ShowDone("Ready","Close",()=>close++);
                CanvasConversion.TickPanelOrder();
                Texture2D image=Capture(eye,Path.Combine(output,"update-mode-"+mode+"-behind.png"));
                Check(Cyan755(image,.2f)<5,"nearer update dialog conceals the farther overlay rod in final pixels");
                Check(Cyan755(image,.42f)>30,"update prompt leaves non-overlapping rod pixels visible");
                Object.DestroyImmediate(image);
                rod.transform.position=new Vector3(0,0,1.8f);
                image=Capture(eye,Path.Combine(output,"update-mode-"+mode+"-front.png"));
                Check(Cyan755(image,.2f)>150,"physically nearer rod remains in front of the update window");Object.DestroyImmediate(image);
                hand.SetActive(true);
                image=Capture(eye,Path.Combine(output,"update-mode-"+mode+"-hand.png"));
                Color center=image.GetPixel(image.width/2,image.height/2);
                Check(center.g>.8f&&center.r<.1f&&center.b<.1f,"solid hand nearer than both UI and rod retains its depth");Object.DestroyImmediate(image);
                hand.SetActive(false);rod.transform.position=new Vector3(0,0,2.2f);
                Check(CanvasConversion.StandaloneCount755==1,"mode changes retain one update ordering lease without re-registration");
            }
            Check(update==0&&ignore==0&&close==0,"rendering update modes never invokes user decisions");
            dialog.ShowChoice("Update available","A newer release is ready.","Ignore","Update",()=>ignore++,()=>update++);
            root=UpdateRoot755(dialog);
            Canvas.ForceUpdateCanvases();
            var button=root.transform.Find("Update").GetComponent<Button>();
            var pointer=new PointerEventData(EventSystem.current){position=eye.WorldToScreenPoint(button.transform.position)};
            var hits=new System.Collections.Generic.List<RaycastResult>();
            root.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
            Check(hits.Exists(hit=>hit.gameObject==button.gameObject||hit.gameObject.transform.IsChildOf(button.transform)),
                "real update button remains a graphic ray target after ordering enrollment");
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            Check(update==1&&ignore==0&&close==0,"explicit pointer click invokes only the original chosen update delegate");
            dialog.Close();
            Check(CanvasConversion.StandaloneCount755==0,"closing update prompt removes its exact standalone ordering lease");
            yield return null;
            Texture2D closed=Capture(eye,Path.Combine(output,"update-closed.png"));
            Check(Cyan755(closed,.2f)>150,"closing update prompt restores the unobstructed rod pixels");Object.DestroyImmediate(closed);
            dialog.ShowChoice("Update available","A newer release is ready.","Ignore","Update",()=>ignore++,()=>update++);
            Check(CanvasConversion.StandaloneCount755==1,"reopening update prompt creates one fresh lease without stale windows");
            dialog.Close();Check(CanvasConversion.StandaloneCount755==0,"final update prompt teardown leaves no ordering lease");
        }
        finally
        {
            dialog.Close();CanvasConversion.WorldCamera=null;WorldUIAssets.Font755=null;
            Object.DestroyImmediate(rod);Object.DestroyImmediate(hand);Object.DestroyImmediate(events);
            Object.DestroyImmediate(rodMaterial);Object.DestroyImmediate(handMaterial);
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(eye.gameObject);
        }
        File.WriteAllText(Path.Combine(output,"limits755.txt"),"Whole production SelfUpdateDialog and exact production standalone registration, withdrawal and window-order kernels execute with actual Unity uGUI/TMP and original Overlay shader. Rod/hand geometry, placement, font assignment, MR opacity and surface registry are declared fixture inputs. No native main-menu capture bootstrap, real hands, D3D11, headset or multiplayer claim.\n");
        yield return _checks;
    }

    private static int Cyan755(Texture2D image,float worldX)
    {
        // Count only a small fixed physical rod patch, avoiding title/body glyphs.
        int middle=image.width/2+(int)(worldX/.6f*(image.width/2));int count=0;
        for(int y=image.height/2-5;y<=image.height/2+5;y++)
            for(int x=middle-10;x<=middle+10;x++)
            {Color p=image.GetPixel(x,y);if(p.r<.15f&&p.g>.65f&&p.b>.65f)count++;}
        return count;
    }

    private static GameObject UpdateRoot755(SelfUpdateDialog dialog)=>(GameObject)typeof(SelfUpdateDialog)
        .GetField("_root",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(dialog)!;
}
