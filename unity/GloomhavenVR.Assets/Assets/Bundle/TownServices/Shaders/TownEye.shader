Shader "GloomhavenVR/TownEye"
{
    Properties
    {
        _MainTex ("Eye atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TownVisibility ("Town visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EyeVertex
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "TownEyeLighting.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 frag(EyeVarying input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                EyeDissolve(input.objectPosition);
                half3 diffuse, specular;
                EyeLighting(input.worldPosition, normalize(input.normal), normalize(UnityWorldSpaceViewDir(input.worldPosition)), 64, input.vertexLights, diffuse, specular);
                fixed4 color = fixed4(tex2D(_MainTex, input.uv).rgb * _Color.rgb * diffuse + specular * 0.025h, 1);
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
