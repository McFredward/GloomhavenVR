using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#pragma warning disable CS8600, CS8619 // Native methods predate nullable reference annotations.
namespace GloomhavenVR.Cards;
// Original fresh-state methods below are copied unchanged from native ItemCardEffects.
// Audited decompiled reference SHA256: bdef3573062c9e3a74e2018cb0a94844302bc29fb9514eb93eb8eec906184378.
// Pool/controller/model construction and particle execution remain boundary ports.
internal sealed class NativeItemEffects661 : MonoBehaviour {
    private static readonly int _posAndBounds=Shader.PropertyToID("_PosAndBounds"),_greyOut=Shader.PropertyToID("_GreyOut"),_flow=Shader.PropertyToID("_Flow"),_dissolve=Shader.PropertyToID("_Dissolve"),_burn=Shader.PropertyToID("_Burn");
    public Image?[] imgComp=Array.Empty<Image?>();public TextMeshProUGUI?[] txtComp=Array.Empty<TextMeshProUGUI?>();
    private Color[] txtColourStore=Array.Empty<Color>(),imgColourStore=Array.Empty<Color>();private bool[] txtGradientStore=Array.Empty<bool>();
    private int txtCount,imgCount;private Image fgFx=null!;private string fxAnim="_FXAnim";private ParticleSystem? fx_Smoke=null;
    public Vector2 cardBounds;
    internal static void Bind(Transform[] nodes,string path) {
        using var r=new BinaryReader(File.OpenRead(path));
        var effect=nodes[0].gameObject.AddComponent<NativeItemEffects661>();
        effect.imgComp=new Image?[r.ReadUInt16()];for(int i=0;i<effect.imgComp.Length;i++){int n=r.ReadInt16();effect.imgComp[i]=n<0?null:nodes[n].GetComponent<Image>();}
        effect.txtComp=new TextMeshProUGUI?[r.ReadUInt16()];for(int i=0;i<effect.txtComp.Length;i++){int n=r.ReadInt16();effect.txtComp[i]=n<0?null:nodes[n].GetComponent<TextMeshProUGUI>();}
        effect.fgFx=nodes[r.ReadInt16()].GetComponent<Image>();effect.Initialize();
    }
	private void Initialize()
	{
		txtColourStore = new Color[txtComp.Length];
		txtGradientStore = new bool[txtComp.Length];
		txtCount = txtComp.Length;
		for (int i = 0; i < txtCount; i++)
		{
			if (txtComp[i] != null)
			{
				txtColourStore[i] = txtComp[i].color;
				txtGradientStore[i] = txtComp[i].enableVertexGradient;
			}
		}
		imgColourStore = new Color[imgComp.Length];
		imgCount = imgComp.Length;
		for (int j = 0; j < imgCount; j++)
		{
			if (imgComp[j] != null)
			{
				imgColourStore[j] = imgComp[j].color;
			}
		}
		RectTransform rectTransform = base.transform as RectTransform;
		cardBounds = new Vector2(rectTransform.rect.width, rectTransform.rect.height);
		Image[] array = imgComp;
		foreach (Image image in array)
		{
			if (image != null)
			{
				image.material = new Material(image.material);
			}
		}
		fgFx.material = new Material(fgFx.material);
		for (int l = 0; l < imgCount; l++)
		{
			if (imgComp[l] != null)
			{
				imgComp[l].material.SetVector(_posAndBounds, new Vector4(rectTransform.position.x, rectTransform.position.y, cardBounds.x, cardBounds.y));
			}
		}
		RestoreCard();
	}
	public void RestoreCard()
	{
		for (int i = 0; i < imgCount; i++)
		{
			if (imgComp[i] != null)
			{
				imgComp[i].material.SetFloat(_greyOut, 0f);
				imgComp[i].material.SetFloat(_flow, 0f);
				imgComp[i].material.SetFloat(_dissolve, 0f);
				imgComp[i].material.SetFloat(_burn, 0f);
			}
		}
		if (fx_Smoke != null)
		{
			fx_Smoke.gameObject.SetActive(value: false);
		}
		if (fgFx != null)
		{
			fgFx.material.SetFloat(fxAnim, 0f);
		}
		for (int j = 0; j < imgCount; j++)
		{
			if (imgComp[j] != null)
			{
				imgComp[j].GetComponent<Image>().color = imgColourStore[j];
			}
		}
		for (int k = 0; k < txtCount; k++)
		{
			if (txtComp[k] != null)
			{
				txtComp[k].color = txtColourStore[k];
			}
		}
	}
}
