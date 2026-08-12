// GloomhavenVR — night-sky dome shader (Sumpfnacht). Renders a vertical sky gradient
// plus a procedurally generated star field (Env_Stars.png: R = star brightness,
// G = per-star twinkle phase). Twinkle is driven purely by shader _Time — data-driven
// animation, no scripts, world-anchored, stereo-correct (the dome is a real inverted
// sphere mesh ~45 m out). Background-queue backdrop, no depth write, exactly the
// SkyPanoramic contract so it never occludes anything.
Shader "GloomhavenVR/EnvStars"
{
    Properties
    {
        _MainTex ("Star field (R=brightness, G=phase)", 2D) = "black" {}
        _StarCol ("Star color", Color) = (0.9,0.95,1.0,1)
        _StarBoost ("Star intensity", Range(0,4)) = 1.6
        _TwinkleSpeed ("Twinkle speed", Range(0,10)) = 2.2
        _TwinkleAmp ("Twinkle amount", Range(0,1)) = 0.45
        _TopCol ("Sky zenith color", Color) = (0.008,0.012,0.028,1)
        _HorizonCol ("Sky horizon color", Color) = (0.045,0.07,0.11,1)
    }
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _StarCol, _TopCol, _HorizonCol;
            float _StarBoost, _TwinkleSpeed, _TwinkleAmp;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.wp - _WorldSpaceCameraPos);
                float h = saturate(d.y);                      // 0 at horizon, 1 at zenith
                float3 sky = lerp(_HorizonCol.rgb, _TopCol.rgb, pow(h, 0.6));

                fixed4 s = tex2D(_MainTex, i.uv);
                float tw = 1.0 - _TwinkleAmp * (0.5 + 0.5 * sin(_Time.y * _TwinkleSpeed + s.g * 40.0));
                float star = s.r * tw * _StarBoost;
                // fade stars into the horizon haze
                star *= saturate(d.y * 4.0 + 0.25);

                return fixed4(sky + _StarCol.rgb * star, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
