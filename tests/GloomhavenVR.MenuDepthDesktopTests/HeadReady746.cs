using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private static Image ReadyGraphic746(Transform parent,string name,Vector2 size,Vector2 position,Color color,int layer)
    {
        var node=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
        node.layer=layer;node.transform.SetParent(parent,false);var image=node.GetComponent<Image>();
        image.rectTransform.sizeDelta=size;image.rectTransform.anchoredPosition=position;image.color=color;return image;
    }
    private static Color ReadyHeadPixel746(Scene scene,Texture2D image,Vector3 world)
    {
        Vector3 pixel=scene.Head.WorldToScreenPoint(world);
        return image.GetPixel(Mathf.RoundToInt(pixel.x),Mathf.RoundToInt(pixel.y));
    }
    public static IEnumerator HeadReady746(string output,Action<int> complete)
    {
        _count=0;DesktopEyeInput.Available=true;
        using(var scene=new Scene())
        {
            MenuDepthCharacters.Shutdown();scene.HideSpatial730();
            foreach(var root in new[]{scene.RootA,scene.RootB})foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))graphic.enabled=false;
            scene.Screen.GetComponent<Renderer>().enabled=false;
            scene.HeadTarget.Release();scene.HeadTarget.width=1280;scene.HeadTarget.height=960;scene.HeadTarget.Create();
            scene.Head.orthographic=false;scene.Head.fieldOfView=60;scene.Head.aspect=4f/3;scene.Head.nearClipPlane=.01f;scene.Head.farClipPlane=20;
            scene.OriginalA.cullingMask=1<<24;scene.OriginalA.enabled=false;scene.OriginalA.orthographic=true;
            scene.OriginalA.clearFlags=CameraClearFlags.SolidColor;scene.OriginalA.backgroundColor=Color.clear;
            for(int phase=0;phase<3;phase++)
            {
                var root=ConfirmationCanvas745("Multiplayer Ready Toggle",24,scene.OriginalA,new Vector2(307,65),new Vector3(0,0,2));
                root.localScale=Vector3.one*.003f;root.gameObject.AddComponent<UIReadyToggle>().enabled=false;
                var plate=ReadyGraphic746(root,"original native button plate",new Vector2(307,65),Vector2.zero,new Color(.5f,.5f,.5f,1),24);
                var left=ReadyGraphic746(root,"original left vote symbol",new Vector2(64,80),new Vector2(-48,73),Color.green,24);
                var right=ReadyGraphic746(root,"original right vote symbol",new Vector2(64,80),new Vector2(48,73),Color.green,24);
                // The first native transition observed in745 reaches x-290 at1034px width.
                // It is real drawn overspill, so preserve it just as the settled symbols.
                if(phase==1)left.rectTransform.anchoredPosition=new Vector2(-290,73);
                if(phase==2)Object.DestroyImmediate(root.GetComponent<UIReadyToggle>());
                Canvas.ForceUpdateCanvases();
                Vector3 leftPosition=left.transform.position,rightPosition=right.transform.position;
                var display=ConfirmationCanvas745("captured ready-toggle pane",27,scene.Head,root.rect.size,root.position);
                display.localScale=root.localScale;var image=display.gameObject.AddComponent<RawImage>();
                var entry=PanelSupersample.BindHead(root,scene.OriginalA,scene.Ui,image);
                Rect frame=PanelSupersample.MeasureReadyFrame(entry);
                // Exact allocator/projection inputs after production's frame coordinator. A fixed
                // generous test RT isolates geometry from allocation; production caps are unchanged.
                scene.Ui.Release();scene.Ui.width=Mathf.CeilToInt(frame.width*3);scene.Ui.height=Mathf.CeilToInt(frame.height*3);scene.Ui.Create();
                image.texture=scene.Ui;entry.RtW=scene.Ui.width;entry.RtH=scene.Ui.height;
                display.sizeDelta=frame.size;display.position=root.TransformPoint(frame.center);
                scene.OriginalA.targetTexture=scene.Ui;scene.OriginalA.aspect=frame.width/frame.height;
                scene.OriginalA.orthographicSize=frame.height*.003f*.5f;
                scene.OriginalA.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(frame.center.x,frame.center.y,-700)),Quaternion.identity);
                scene.OriginalA.ResetProjectionMatrix();scene.OriginalA.Render();
                Rect ink=Rect.MinMaxRect(phase==1?-322:-80,33,80,113);
                Check(SharedWindowBadgeLayout.TryCompactSeat(root.rect,ink,true,51,12,out Vector2 seat),"short native button uses a separate compact mark seat");
                var badge=ConfirmationCanvas745("mod multiplayer badge",28,scene.Head,new Vector2(51,51),root.TransformPoint(seat));badge.localScale=root.localScale;
                ReadyGraphic746(badge,"mod mark raster",new Vector2(51,51),Vector2.zero,Color.blue,28);
                scene.Head.cullingMask=(1<<27)|(1<<28);scene.Eyes(Vector3.zero);scene.Render(false);
                Texture2D head=Read(scene.HeadTarget,Path.Combine(output,"ready-"+phase+"-head.png"));
                Color top=ReadyHeadPixel746(scene,head,right.transform.TransformPoint(new Vector3(0,34,0)));
                if(phase<2)Check(top.g>.65f&&top.b<.3f,"full native vote symbols survive the actual measured capture and final head pixels");
                else Check(frame.yMax<=97,"ordinary unrelated panels retain the existing expansion cap");
                Color originalButton=ReadyHeadPixel746(scene,head,root.TransformPoint(new Vector3(20,0,0)));
                Check(originalButton.b<.7f&&originalButton.r>.2f,"compact multiplayer badge leaves the entire native button and vote ink unobscured");
                Check(left.transform.position==leftPosition&&right.transform.position==rightPosition&&plate.rectTransform.sizeDelta==new Vector2(307,65),
                    "native button and animated vote widgets retain their original transforms and sizes");
                Object.DestroyImmediate(head);PanelSupersample.EndHead(entry);Object.DestroyImmediate(root.gameObject);Object.DestroyImmediate(display.gameObject);Object.DestroyImmediate(badge.gameObject);
                yield return null;
            }
        }
        complete(_count);
    }
}
