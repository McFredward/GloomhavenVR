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
                half3 albedo = tex2D(_MainTex, input.uv).rgb;
                // The brown atlas has strongly red pigment. Correct only that
                // pigment inside its measured iris; retain the sclera, pupil
                // and the enchantress's green iris exactly as authored.
                half iris = 1 - smoothstep(0.108h, 0.118h, length(input.uv - half2(0.704h, 0.703h)));
                half redPigment = saturate((albedo.r - max(albedo.g, albedo.b)) * 4);
                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                half3 brown = lerp(albedo, luminance * half3(1.12h, 0.88h, 0.65h), 0.7h);
                albedo = lerp(albedo, brown, iris * redPigment);
                fixed4 color = fixed4(albedo * _Color.rgb * diffuse + specular * 0.025h, 1);
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
