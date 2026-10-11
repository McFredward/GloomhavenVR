// Exact recovered native GUI/AbilityCard_Shd property table.
// Fresh-item fragment execution is the declared Unity UI/Default renderer port.
Shader "GloomhavenVR/FixtureNativeItem661" {
	Properties {
		_Color ("Tint", Vector) = (1,1,1,1)
		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_ColorMask ("Color Mask", Float) = 15
		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
		_MainTex ("_MainTex", 2D) = "white" {}
		_MainColour ("_MainColour", Vector) = (1,1,1,1)
		_Burn ("Burn", Range(0, 1)) = 0
		_Burn_ColourTint ("Burn_ColourTint", Vector) = (1,0.4964139,0.2216981,1)
		_Burn_Glow ("Burn_Glow", Range(1, 5)) = 0
		_Burn_GradientMin ("Burn_GradientMin", Range(0, 1)) = 0.4
		_Burn_GradientMax ("Burn_GradientMax", Range(0, 1)) = 0.6
		_Mask_Hardness ("Mask_Hardness", Range(0, 5)) = 1
		_Mask_OuterMin ("Mask_OuterMin", Range(0, 1)) = 0
		_Mask_OuterMax ("Mask_OuterMax", Range(0, 1)) = 0.5
		_Mask_InnerMin ("Mask_InnerMin", Range(0, 1)) = 0.5
		_Mask_InnerMax ("Mask_InnerMax", Range(0, 1)) = 1
		_Mask_Inverse ("Mask_Inverse", Float) = 0
		_AnimNoise_Mask ("AnimNoise_Mask", 2D) = "white" {}
		_AnimNoise_Min ("AnimNoise_Min", Range(0, 1)) = 0
		_AnimNoise_Max ("AnimNoise_Max", Range(0, 1)) = 0.8
		_AnimNoise_Exponent ("AnimNoise_Exponent", Range(0, 1)) = 0
		_AnimNoise_SpeedX ("AnimNoise_SpeedX", Range(0, 1)) = 1
		_AnimNoise_SpeedY ("AnimNoise_SpeedY", Range(0, 1)) = 1
		_Flow ("Flow", Range(0, 1)) = 0
		_Flow_Map ("Flow_Map", 2D) = "bump" {}
		_Flow_Offset ("Flow_Offset", Float) = 0
		_Flow_Speed ("Flow_Speed", Float) = 0.2
		_Dissolve ("Dissolve", Range(0, 1)) = 0
		_Dissolve_GradientMin ("Dissolve_GradientMin", Range(0, 1)) = 0.4
		_Dissolve_GradientMax ("Dissolve_GradientMax", Range(0, 1)) = 0.6
		_Dissolve_VerticalGradient ("Dissolve_VerticalGradient", Range(0, 1)) = 0
		_RedOut ("RedOut", Range(0, 1)) = 0
		_RedOutTint ("RedOutTint", Vector) = (1,1,1,1)
		_GreyOut ("GreyOut", Range(0, 1)) = 0
		_GreyOutTint ("GreyOutTint", Vector) = (1,1,1,1)
		[Toggle] _OverlayOn ("OverlayOn", Float) = 0
		_OverlayIntensity ("OverlayIntensity", Range(0, 1)) = 0
		_OverlayCol ("OverlayCol", Vector) = (1,0,0,0.5372549)
		_PosAndBounds ("_PosAndBounds", Vector) = (0,0,0,0)
		[HideInInspector] _texcoord ("", 2D) = "white" {}
	}
 SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } UsePass "UI/Default/Default" }
}
