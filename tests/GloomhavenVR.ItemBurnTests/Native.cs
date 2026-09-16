// Native ItemCardEffects.BurnCardTimeline, copied verbatim from the read-only game reference.
using System.Collections;
using UnityEngine;
using Chronos;
public class ItemCardEffects {
    public float fx_Overlay_FlowSpeed;
    public float fx_Overlay_Flow_NoiseTiling;
    public float fx_Overlay_Glow;
    public float fx_Overlay_OffsetStrength;
    public float fx_Overlay_ThinHighlight_Max;
    public float fx_Overlay_ThinHighlight_Min;
    public float fx_Overlay_ThinHighlights;
    public float fx_Overlay_anim;
    public Color fx_Overlay_color;
    public Texture2D fx_Overlay_texture = new();
    public Color fx_Smoke_color;
    public float mc_AnimNoise_Mask_tile;
    public float mc_Burn;
    public Color mc_Burn_ColourTint;
    public float mc_Dissolve;
    public float mc_Dissolve_VerticalGradient;
    public float mc_Flow;
    public float mc_Flow_Offset;
    public float mc_Flow_Speed;
    public float mc_GreyOut;
    public int _animNoiseMask;
    public int _burn;
    public int _burnColourTint;
    public int _dissolve;
    public int _dissolveVerticalGradient;
    public int _flow;
    public int _flowNoiseTiling;
    public int _flowOffset;
    public int _flowSpeed;
    public int _glow;
    public int _greyOut;
    public int _offsetStrength;
    public int _particleTexture;
    public int _speed;
    public int _thinHighlightMax;
    public int _thinHighlightMin;
    public int _thinHighlights;
    public int _tintColor;
    public Image[] imgComp = { new Image() };
    public Image[] txtComp = { new Image() };
    public int imgCount = 1, txtCount = 1;
    public Image? fgFx = new();
    public Image? fx_Smoke = new();
    public Texture2D overlayFrameBurn = new();
    public string fxAnim = "_FXAnim";
	public IEnumerator BurnCardTimeline(bool burnAnim)
	{
		if (fgFx == null)
		{
			yield break;
		}
		fgFx.gameObject.SetActive(value: true);
		float dTime = 0f;
		float startTime = Timekeeper.instance.m_GlobalClock.time;
		float burnTime = 0.001f;
		mc_Burn = 0.691f;
		mc_Burn_ColourTint = new Color(0.36862746f, 0.14509805f, 0.07450981f, 0.601f);
		mc_Flow = 1f;
		mc_Flow_Offset = 0.03f;
		mc_Flow_Speed = 0.4f;
		mc_AnimNoise_Mask_tile = 40f;
		mc_Dissolve = 0.646f;
		mc_Dissolve_VerticalGradient = 0.2f;
		mc_GreyOut = 1f;
		fx_Smoke_color = new Color(0.14901961f, 0.14901961f, 0.14901961f, 0.5f);
		fx_Overlay_color = new Color(1f, 0.3f, 0f, 0.8f);
		fx_Overlay_anim = 0.5f;
		fx_Overlay_texture = overlayFrameBurn;
		fx_Overlay_FlowSpeed = 0.5f;
		fx_Overlay_OffsetStrength = 0.2f;
		fx_Overlay_ThinHighlights = 0.463f;
		fx_Overlay_ThinHighlight_Min = 0f;
		fx_Overlay_ThinHighlight_Max = 0.195f;
		fx_Overlay_Flow_NoiseTiling = 6f;
		fx_Overlay_Glow = 3f;
		for (int i = 0; i < imgCount; i++)
		{
			if (imgComp[i] != null)
			{
				imgComp[i].material.SetFloat(_burn, mc_Burn);
				imgComp[i].material.SetColor(_burnColourTint, mc_Burn_ColourTint);
				imgComp[i].material.SetFloat(_flowOffset, mc_Flow_Offset);
				imgComp[i].material.SetFloat(_flowSpeed, mc_Flow_Speed);
				imgComp[i].material.SetTextureScale(_animNoiseMask, new Vector2(mc_AnimNoise_Mask_tile, mc_AnimNoise_Mask_tile));
				imgComp[i].material.SetFloat(_dissolveVerticalGradient, mc_Dissolve_VerticalGradient);
			}
		}
		fgFx.material.SetColor(_tintColor, fx_Overlay_color);
		fgFx.material.SetTexture(_particleTexture, fx_Overlay_texture);
		fgFx.material.SetFloat(_speed, fx_Overlay_FlowSpeed);
		fgFx.material.SetFloat(_offsetStrength, fx_Overlay_OffsetStrength);
		fgFx.material.SetFloat(_thinHighlights, fx_Overlay_ThinHighlights);
		fgFx.material.SetFloat(_thinHighlightMin, fx_Overlay_ThinHighlight_Min);
		fgFx.material.SetFloat(_thinHighlightMax, fx_Overlay_ThinHighlight_Max);
		fgFx.material.SetFloat(_flowNoiseTiling, fx_Overlay_Flow_NoiseTiling);
		fgFx.material.SetFloat(_glow, fx_Overlay_Glow);
		if (burnAnim)
		{
			while (Timekeeper.instance.m_GlobalClock.time - startTime < burnTime)
			{
				dTime += Timekeeper.instance.m_GlobalClock.deltaTime / burnTime;
				for (int j = 0; j < imgCount; j++)
				{
					if (imgComp[j] != null)
					{
						imgComp[j].material.SetFloat(_greyOut, Mathf.Clamp01(dTime));
						imgComp[j].material.SetFloat(_flow, Mathf.Clamp01(dTime));
						imgComp[j].material.SetFloat(_dissolve, Mathf.Lerp(0f, mc_Dissolve, Mathf.Clamp01(dTime)));
					}
				}
				if (fgFx != null)
				{
					fgFx.material.SetFloat(fxAnim, Mathf.Clamp(dTime * 4f, 0f, 1f) / 2f);
				}
				if (fx_Smoke != null)
				{
					fx_Smoke.gameObject.SetActive(value: true);
				}
				for (int k = 0; k < txtCount; k++)
				{
					if (txtComp[k] != null)
					{
						txtComp[k].color = new Color(0.5f, 0.5f, 0.5f, 1f);
					}
				}
				yield return new WaitForEndOfFrame();
			}
			yield break;
		}
		for (int l = 0; l < imgCount; l++)
		{
			if (imgComp[l] != null)
			{
				imgComp[l].material.SetFloat(_greyOut, 1f);
				imgComp[l].material.SetFloat(_flow, 1f);
				imgComp[l].material.SetFloat(_dissolve, mc_Dissolve);
			}
		}
		fgFx.material.SetFloat(fxAnim, 0.5f);
		for (int m = 0; m < txtCount; m++)
		{
			if (txtComp[m] != null)
			{
				txtComp[m].color = new Color(0.5f, 0.5f, 0.5f, 1f);
			}
		}
	}
}
