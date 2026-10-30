extern alias native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private static void ComposedLoadout747(Camera head,RectTransform home,RectTransform story,UIWindow storyWindow,string output)
    {
        var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-narrative-loadout.xml"));
        var host=ImportNode739(document.DocumentElement!,home,new Dictionary<string,TMP_FontAsset>(),true);
        host.gameObject.SetActive(true);
        foreach(CanvasGroup group in host.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        var info=(RectTransform)host.Find("UI Loadout Quest Information");info.gameObject.SetActive(true);
        var picture=info.Find("Holder/Paper/Image").GetComponent<Image>();
        foreach(Transform node in host.GetComponentsInChildren<Transform>(true))
            if(picture.transform.IsChildOf(node))node.gameObject.SetActive(true);
        var art=LoadTexture("native-loadout-illustration");
        picture.sprite=Sprite.Create(art,new Rect(0,0,art.width,art.height),new Vector2(.5f,.5f),100);
        picture.sprite.name="CR_RU_01_Demon";
        var window=host.gameObject.AddComponent<UIWindow>();
        // The exact original serialized fields are audited by the read-only
        // exporter. Only their controller identities/session readiness are ports.
        var manager=host.gameObject.AddComponent<UILoadoutManager>();
        manager.questInfo=info.gameObject.AddComponent<UILoadoutQuestWindow>();
        var viewer=info.gameObject.AddComponent<StoryImageViewer>();manager.questInfo.imagePaper=viewer;
        viewer.container=info.gameObject;viewer.imageHolder=picture;
        var background=info.GetComponent<Image>();
        var blurs=new[]{info.Find("Blur").GetComponent<Image>(),info.Find("Blur (1)").GetComponent<Image>()};
        var targets=new[]{background,blurs[0],blurs[1]};
        Check(background.sprite.name=="Black_Backdrop"&&Mathf.Abs(background.color.a-.5176471f)<.00001f,
            "actual composed host retains the native half-alpha dimmer provenance before leasing");
        foreach(Image blur in blurs)Check(blur.material.shader.name=="Custom/SimpleGrabPassBlur",
            "both native loadout blur graphics retain their original exact source role");
        var flags=new Dictionary<Graphic,bool>();foreach(Graphic graphic in host.GetComponentsInChildren<Graphic>(true))flags.Add(graphic,graphic.enabled);
        var storyChildren=new List<Transform>();foreach(Transform child in story)storyChildren.Add(child);
        var storyBackground=story.Find("UI Story Box/UI Story Background").GetComponent<Image>();
        bool storyBackgroundEnabled=storyBackground.enabled;
        var rows=new List<string>{"mode,phase,path,view,mean_rgb_error"};
        var singleplayerPixels=new Dictionary<string,Color32[]>();
        bool wasOnline=FFSNetwork.IsOnline;
        try
        {
            foreach(bool online in new[]{false,true})
            {
                FFSNetwork.IsOnline=online;string mode=online?"mp":"sp";
                Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                    "actual composed native loadout accepts its original story producer in both session inputs");
                Check(!storyBackground.enabled,"actual story Park and ApplyPose hide only the native dimmer Image");
                var panel=CanvasConversion.Convert(host,"actual composed loadout",useModLayer:true)!;
                panel.HostRect.SetPositionAndRotation(Vector3.zero,Quaternion.identity);panel.HostRect.localScale=Vector3.one*.001f;
                try
                {
                    foreach(bool steady in new[]{false,true})
                    {
                        if(steady){background.enabled=true;NativeStoryComposite746.TickOriginal(host);}
                        foreach(bool captured in new[]{false,true})
                        using(var capture=captured?new PanelSupersample.ConfirmationCapture(panel):null)
                        for(int view=0;view<4;view++)
                        {
                            head.transform.position=view==0?new Vector3(0,0,-2.1f):view==1?new Vector3(.9f,.25f,-2):view==2?new Vector3(-.8f,-.15f,-2):new Vector3(0,0,2.1f);
                            head.transform.rotation=Quaternion.LookRotation(-head.transform.position,Vector3.up);
                            string label="loadout-"+mode+"-"+(steady?"steady":"begin")+"-"+(captured?"capture":"direct")+"-"+view;
                            capture?.Draw();Texture2D actual=Capture(head,Path.Combine(output,label+".png"));
                            string parityKey=(steady?"steady":"begin")+"-"+(captured?"capture":"direct")+"-"+view;
                            if(!online)singleplayerPixels.Add(parityKey,actual.GetPixels32());
                            else
                            {
                                Color32[] sp=singleplayerPixels[parityKey],mp=actual.GetPixels32();bool equal=sp.Length==mp.Length;
                                for(int i=0;equal&&i<sp.Length;i++)equal=sp[i].Equals(mp[i]);
                                Check(equal,"singleplayer and multiplayer input modes retain identical composed native pixels");
                            }
                            var enabled=new bool[targets.Length];
                            for(int i=0;i<targets.Length;i++){enabled[i]=targets[i].enabled;targets[i].enabled=false;}
                            capture?.Draw();Texture2D reference=Capture(head,Path.Combine(output,label+"-reference.png"));
                            float error=Difference(actual,reference);rows.Add(mode+","+(steady?"steady":"begin")+","+(captured?"capture":"direct")+","+view+","+error.ToString("R"));
                            File.WriteAllLines(Path.Combine(output,"loadout-composed-pixels.csv"),rows);
                            for(int i=0;i<targets.Length;i++)targets[i].enabled=enabled[i];
                            Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                            Check(error<.00001f,"actual composed loadout final head pixels contain no native host dimmer in SP and MP");
                        }
                    }
                    foreach(Graphic graphic in flags.Keys)
                        if(Array.IndexOf(targets,graphic)<0)Check(graphic.enabled==flags[graphic],
                            "composite host preserves every original illustration paper mask button and transparent input graphic flag");
                    Check(picture.enabled&&picture.sprite.name=="CR_RU_01_Demon"&&info.gameObject.activeInHierarchy,
                        "actual native host illustration and StoryImageViewer container remain live");
                }
                finally{CanvasConversion.Release(panel);NativeStoryComposite746.UnparkOriginal();}
                Check(storyBackground.enabled==storyBackgroundEnabled,"composite unpark restores every original narrative dimmer flag");
                foreach(Image target in targets)Check(target.enabled==flags[target],
                    "actual native host dimmer and both blur flags restore before the next native loadout phase");
                for(int i=0;i<storyChildren.Count;i++)Check(storyChildren[i].parent==story&&storyChildren[i].GetSiblingIndex()==i,
                    "actual composed story hand-back preserves every original source parent and sibling index");
            }
            foreach(Image target in targets)target.enabled=false;
            Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                "actual host with natively disabled dimmers still composes");
            NativeStoryComposite746.UnparkOriginal();
            foreach(Image target in targets)Check(!target.enabled,
                "native-disabled loadout graphics stay disabled after composite hand-back");
            foreach(Image target in targets)target.enabled=flags[target];

            var foreign=Rect("foreign native loadout owner",home,1920,1080);
            info.SetParent(foreign,false);
            Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                "story Park remains a presentation operation when a native viewer changes owner");
            Check(background.enabled,"foreign native loadout viewer is never leased through a stale manager reference");
            NativeStoryComposite746.UnparkOriginal();info.SetParent(host,false);Object.DestroyImmediate(foreign.gameObject);

            Sprite dimmerSource=background.sprite;background.sprite=picture.sprite;
            Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                "native viewer graphic can be reused for original illustration content");
            Check(background.enabled,"native viewer illustration content is never mistaken for its desktop dimmer");
            NativeStoryComposite746.UnparkOriginal();background.sprite=dimmerSource;

            bool raycast=background.raycastTarget;background.raycastTarget=true;
            Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                "native viewer input role is an explicit producer input");
            Check(background.enabled,"native loadout input graphics cannot join the desktop dimmer lease");
            NativeStoryComposite746.UnparkOriginal();background.raycastTarget=raycast;

            Check(NativeStoryComposite746.ParkOriginal(window,host,storyWindow,picture.rectTransform),
                "actual host can compose before a pooled content reuse");
            background.sprite=picture.sprite;NativeStoryComposite746.UnparkOriginal();
            Check(!background.enabled,"pooled native loadout content is not re-enabled by an expired dimmer lease");
            background.sprite=dimmerSource;background.enabled=true;
            // Native pointer progression is retained, rather than replacing the
            // game's Continue action with a mod callback or closing its source.
            var button=host.Find("EnterButton").gameObject.AddComponent<native::ExtendedButton>();
            button.useAudioController=false;button.textLanguageKey="";button.targetGraphic=host.Find("EnterButton").GetComponent<Image>();
            button.gameObject.SetActive(true);int clicks=0;button.onClick.AddListener(()=>clicks++);
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
            Check(clicks==1,"actual native loadout confirmation dispatches its original pointer action exactly once");
        }
        finally{FFSNetwork.IsOnline=wasOnline;NativeStoryComposite746.UnparkOriginal();Object.DestroyImmediate(host.gameObject);}
    }
}
