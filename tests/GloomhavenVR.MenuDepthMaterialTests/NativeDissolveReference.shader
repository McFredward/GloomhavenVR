// Independent reference transcribed from the original UI/Dissolve mask DXBC.
// Native fragment SHA256 6759a3905d6da9e7464b1c344227dd6e2ab902265e3c08d7b16af1b5a7fee65e.
// Native vertex SHA256 076c53f158c731b2703f8baa965b0b668ddd6df4f2f07875ca6e081db5592fbe.
// Register bindings and direct SHDR disassembly are retained with every proof run.
// This is a GL-compilable source reference, not execution of the D3D11-only game program.
Shader "UI/Dissolve mask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base (RGB)", 2D) = "white" {}
        _Mask ("Alpha mask (R)", 2D) = "white" {}
        _Cutoff ("Cutoff", Range(0,1)) = .5
        _EdgeWeight ("EdgeWeight", Range(0,5)) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="true" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Mask;
            float4 _MainTex_ST, _Mask_ST;
            float _Cutoff, _EdgeWeight;
            struct Input { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                o.vertex = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, v.vertex));
                o.color = v.color;
                o.uv.xy = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.uv.zw = v.uv * _Mask_ST.xy + _Mask_ST.zw;
                return o;
            }
            float4 frag(Output i) : SV_Target
            {
                // The operation order is the original sample/lt/discard/sample/
                // mul/add/mul/div/mul/mov sequence, not a generic dissolve guess.
                float4 r0 = tex2D(_Mask, i.uv.zw);
                if (r0.x < _Cutoff) discard;
                float4 r1 = tex2D(_MainTex, i.uv.xy) * i.color;
                r0.y = r0.x - _Cutoff;
                r0.y *= r1.w;
                r0.x = r0.y / r0.x;
                float alpha = r0.x * _EdgeWeight;
                return float4(r1.xyz, alpha);
            }
            ENDCG
        }
    }
}
