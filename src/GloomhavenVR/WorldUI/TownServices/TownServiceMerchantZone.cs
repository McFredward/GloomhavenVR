using System;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-interactive physical transaction mark. It is visible only while the
/// player holds a card that the original service currently permits them to trade.</summary>
internal sealed class TownServiceMerchantZone : IDisposable
{
    private readonly GameObject _root;
    private readonly CanvasGroup _gate;
    private readonly TMP_Text _label;
    internal readonly bool Selling;
    internal Transform Root=>_root.transform;
    internal TownServiceMerchantZone(Transform parent,bool selling,TMP_Text? font)
    {
        Selling=selling;_root=CreateTemplate(font);Root.SetParent(parent,false);
        Root.localPosition=new Vector3(selling?.22f:-.22f,.015f,-.20f);
        _gate=_root.GetComponent<CanvasGroup>();_label=Root.Find("Caption").GetComponent<TMP_Text>();
        Refresh();Loc.OnChanged+=Refresh;SetShown(false,0f);
    }
    internal static GameObject CreateTemplate(TMP_Text? font)
    {
        var root=new GameObject("MerchantTransactionZone",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup));
        var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(380f,260f);
        rect.localScale=Vector3.one*.001f;rect.localRotation=Quaternion.Euler(90f,0f,0f);
        root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        CanvasGroup gate=root.GetComponent<CanvasGroup>();gate.interactable=false;gate.blocksRaycasts=false;
        var outline=new GameObject("Border",typeof(RectTransform),typeof(Image));outline.transform.SetParent(rect,false);
        var border=(RectTransform)outline.transform;border.sizeDelta=rect.sizeDelta;
        var image=outline.GetComponent<Image>();image.color=new Color(.24f,.67f,.34f,.28f);image.raycastTarget=false;
        var caption=new GameObject("Caption",typeof(RectTransform),typeof(TextMeshProUGUI));caption.transform.SetParent(rect,false);
        var text=caption.GetComponent<TextMeshProUGUI>();text.rectTransform.sizeDelta=new Vector2(360f,70f);
        if(font!=null){text.font=font.font;text.fontSharedMaterial=font.fontSharedMaterial;}
        text.fontSize=36f;text.alignment=TextAlignmentOptions.Center;text.color=new Color(.98f,.94f,.8f);text.raycastTarget=false;
        VRLayers.Apply(root);return root;
    }
    private void Refresh()=>_label.text=Loc.Mod(Selling?"town_merchant_sell":"town_merchant_buy");
    internal void SetShown(bool shown,float visibility)=>_gate.alpha=shown?visibility:0f;
    public void Dispose(){Loc.OnChanged-=Refresh;UnityEngine.Object.Destroy(_root);}
}
