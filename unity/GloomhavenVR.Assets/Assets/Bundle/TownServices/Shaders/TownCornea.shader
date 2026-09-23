Shader "GloomhavenVR/TownCornea"
{
    Properties
    {
        _TownVisibility ("Town visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Back ZWrite Off Blend One One
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
            fixed4 frag(EyeVarying input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                EyeDissolve(input.objectPosition);
                half3 normal = normalize(input.normal);
                half3 view = normalize(UnityWorldSpaceViewDir(input.worldPosition));
                half3 diffuse, specular;
                EyeLighting(input.worldPosition, normal, view, 144, input.vertexLights, diffuse, specular);
                half fresnel = 0.025h + 0.975h * pow(1.0h - saturate(dot(normal, view)), 5);
                fixed4 color = fixed4(specular * (0.32h + fresnel), 0);
                UNITY_APPLY_FOG_COLOR(input.fogCoord, color, fixed4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
