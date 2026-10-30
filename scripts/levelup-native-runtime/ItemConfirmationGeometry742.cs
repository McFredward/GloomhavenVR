using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private static IEnumerator ItemConfirmationGeometry742(Camera head,RectTransform ownerRoot,string output)
    {
        Vector3 oldPosition=ownerRoot.position,oldScale=ownerRoot.localScale;Quaternion oldRotation=ownerRoot.rotation;
        Vector3 cameraPosition=head.transform.position;Quaternion cameraRotation=head.transform.rotation;Color clear=head.backgroundColor;
        bool ortho=head.orthographic;float near=head.nearClipPlane,far=head.farClipPlane,fov=head.fieldOfView;int mask=head.cullingMask;
        float oldWorldScale=PanelLayout.WorldScale;PanelLayout.WorldScale=198.12f;
        var ownerPanel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,ownerRoot))!;
        int oldBase=ownerPanel.BaseSortingOrder;ownerPanel.BaseSortingOrder=1000;
        var ink=Image(ownerRoot,"original item-owner photograph overlap",1920,1080,new Color(.42f,.11f,.07f,1));ink.transform.SetAsFirstSibling();
        var persistentHome=Rect("native item persistent home",null,1920,1080);Object.DontDestroyOnLoad(persistentHome.gameObject);
        var sceneHome=Rect("native item scene home",null,1920,1080);SceneManager.MoveGameObjectToScene(sceneHome.gameObject,ownerRoot.gameObject.scene);
        var sourceWindow=Rect("native shop inventory window",ownerRoot,1620,1080);sourceWindow.gameObject.AddComponent<UIWindow>();
        sourceWindow.anchoredPosition=new Vector2(47,-18);var shop=sourceWindow.gameObject.AddComponent<UIShopItemInventory>();
        var document=new XmlDocument();document.Load(Path.Combine(_inputs,"native-item-confirmation-tree.xml"));
        var imported=ImportNode739(document.DocumentElement!,persistentHome,new Dictionary<string,TMP_FontAsset>());imported.gameObject.SetActive(true);
        var content=(RectTransform)imported.Find("Content");var backdrop=imported.GetComponent<Image>();var blur=imported.Find("Blur").GetComponent<Image>();
        imported.Find("Content/Header/Title").GetComponent<TMP_Text>().text="Bind this item?";
        imported.Find("Content/Information/TextMeshPro Text").GetComponent<TMP_Text>().text="Original item information";
        Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(imported);Canvas.ForceUpdateCanvases();
        var item=imported.gameObject.AddComponent<UIItemConfirmationBox>();var window=imported.gameObject.AddComponent<UIWindow>();
        UIItemConfirmationBox? previousItem=Singleton<UIItemConfirmationBox>.Instance;Singleton<UIItemConfirmationBox>.Instance=item;shop.Item=item;
        head.orthographic=false;head.nearClipPlane=1;head.farClipPlane=2000;head.fieldOfView=60;head.cullingMask=(1<<5)|(1<<26);
        head.backgroundColor=new Color(.12f,.27f,.4f,1);var rows=new List<string>();
        try
        {
            foreach(string route in new[]{"item-persistent","item-same-scene"})
            {
                RectTransform home=route=="item-persistent"?persistentHome:sceneHome;imported.SetParent(home,false);
                int nativeScene=imported.gameObject.scene.handle;
                AccessTools.Method(typeof(UIShopItemInventory),"OnSelectedItemSell").Invoke(shop,null);ConfirmationOnOwner.Tick();
                Check(ConfirmationOnOwner.IsOwned(window),"original native item confirmation uses its exact producer window in either scene");
                ConfirmationAA745(window,sourceWindow,route);
                var panel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,imported))!;CanvasConversion.ResetOrder739();
                for(int frame=0;frame<10;frame++)
                {
                    ownerRoot.SetPositionAndRotation(new Vector3(267.92f+frame*.13f,174.92f-frame*.04f,58.59f),Quaternion.Euler(0,37+frame*.11f,0));
                    ownerRoot.localScale=Vector3.one*(.08275f+frame*.00001f);ConfirmationOnOwner.Tick();CanvasConversion.SweepBackgrounds739(panel);
                    Check(Vector3.Distance(panel.HostRect.position,sourceWindow.TransformPoint(new Vector3(0,0,-32)))<.0001f
                        &&Vector3.Distance(panel.HostRect.lossyScale,sourceWindow.lossyScale)<.000001f
                        &&imported.gameObject.scene.handle==nativeScene,
                        "original item foreground follows accepted owner geometry while retaining native scene lifetime");
                    for(int side=0;side<2;side++)for(int eye=0;eye<2;eye++)
                    {
                        Transform mount=panel.HostRect;Vector3 centre=content.position;
                        head.transform.position=centre+mount.forward*(side==0?-120:120)
                            +mount.right*((eye==0?-6.34f:6.34f)+frame*.023f)+mount.up*Mathf.Sin(frame*.6f)*.13f;
                        head.transform.rotation=Quaternion.LookRotation(centre-head.transform.position,mount.up);CanvasConversion.TickPanelOrder();
                        string stem=route+"-direct-"+frame+"-"+side+"-"+eye;
                        Texture2D actual=Capture(head,frame==0||frame==9?Path.Combine(output,stem+".png"):"");
                        bool backingEnabled=backdrop.enabled,blurEnabled=blur.enabled;backdrop.enabled=blur.enabled=false;
                        int order=panel.HostCanvas.sortingOrder;panel.HostCanvas.sortingOrder=ownerPanel.HostCanvas.sortingOrder+16;
                        Texture2D reference=Capture(head,frame==0?Path.Combine(output,stem+"-reference.png"):"");
                        panel.HostCanvas.sortingOrder=order;backdrop.enabled=backingEnabled;blur.enabled=blurEnabled;
                        float difference=Difference(actual,reference);int exterior=ExteriorDifference739(actual,reference,head,content);
                        int referenceInk=BrightPixels(reference);
                        rows.Add(stem+","+difference.ToString("R",CultureInfo.InvariantCulture)+","+exterior+","+BrightPixels(actual)+","+referenceInk);
                        Object.DestroyImmediate(actual);Object.DestroyImmediate(reference);
                        File.WriteAllLines(Path.Combine(output,"native-item-confirmation-direct742.csv"),rows);
                        Check(referenceInk>10,"original native item reference retains visible foreground instead of a blank passing image");
                        Check(exterior==0,"item direct confirmation has no full-frame black or GrabPass surface outside the native content");
                        Check(difference<.00001f,"item direct confirmation retains every original foreground pixel through head motion");
                    }
                    yield return null;
                }
                Check(!backdrop.enabled&&!blur.enabled,"item native full-frame surfaces retire before original foreground presentation");
                item.IsActive=window.IsVisible=false;ConfirmationOnOwner.Tick();
                Check(backdrop.enabled&&blur.enabled&&imported.parent==home&&imported.gameObject.scene.handle==nativeScene,
                    "native item close restores original graphics and exact home in both scenes");
            }
        }
        finally
        {
            item.IsActive=window.IsVisible=false;ConfirmationOnOwner.Tick();Singleton<UIItemConfirmationBox>.Instance=previousItem;
            ownerRoot.SetPositionAndRotation(oldPosition,oldRotation);ownerRoot.localScale=oldScale;
            head.transform.SetPositionAndRotation(cameraPosition,cameraRotation);head.orthographic=ortho;head.nearClipPlane=near;head.farClipPlane=far;head.fieldOfView=fov;head.cullingMask=mask;head.backgroundColor=clear;
            ownerPanel.BaseSortingOrder=oldBase;PanelLayout.WorldScale=oldWorldScale;CanvasConversion.ResetOrder739();
            Object.DestroyImmediate(ink.gameObject);Object.DestroyImmediate(persistentHome.gameObject);Object.DestroyImmediate(sceneHome.gameObject);Object.DestroyImmediate(sourceWindow.gameObject);
            File.WriteAllText(Path.Combine(output,"native-item-confirmation-direct742-limits.txt"),
                "Read-only original level9/3340 item-dialog static geometry/layout/images/fonts and real GL final head pixels execute. Actual production source ownership, scopes, background and order kernels execute. Native gameplay/session controllers, dynamic spawned item card, Windows shader fragments and headset pacing do not execute. Whole conversion allocation is an explicit port as in the containing generic-dialog proof.");
        }
    }
}
