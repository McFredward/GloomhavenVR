// Eyes and skin share the same actual town lamps. No Unity per-renderer light
// ranking or stage-specific VERTEXLIGHT_ON branch may remove a corneal highlight.
#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "TownPracticalLighting.cginc"
struct EyeInput
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct EyeVarying
{
    float4 position : SV_POSITION;
    float3 worldPosition : TEXCOORD0;
    half3 normal : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float3 objectPosition : TEXCOORD3;
    UNITY_FOG_COORDS(4)
    UNITY_VERTEX_OUTPUT_STEREO
};
float4 _MainTex_ST;
half _TownVisibility;
EyeVarying EyeVertex(EyeInput input)
{
    EyeVarying output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_OUTPUT(EyeVarying, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.position = UnityObjectToClipPos(input.vertex);
    output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
    output.objectPosition = input.vertex.xyz;
    output.normal = UnityObjectToWorldNormal(input.normal);
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    UNITY_TRANSFER_FOG(output, output.position);
    return output;
}
void EyeDissolve(float3 position)
{
    if (_TownVisibility < 0.99999h)
    {
        float3 cell = floor(position * 128.0);
        float noise = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        clip(_TownVisibility - max(noise, 0.0001));
    }
}
void EyeLighting(float3 position, half3 normal, half3 view, half exponent,
                 out half3 diffuse, out half3 specular)
{
    diffuse = max(0, ShadeSH9(half4(normal, 1)));
    specular = 0;
    half3 light = normalize(UnityWorldSpaceLightDir(position));
    half ndl = saturate(dot(normal, light));
    diffuse += _LightColor0.rgb * ndl;
    specular += _LightColor0.rgb * ndl * pow(saturate(dot(normal, normalize(light + view))), exponent);
    half3 practicalDiffuse, practicalSpecular;
    TownPracticals(position, normal, view, exponent, practicalDiffuse, practicalSpecular);
    diffuse += practicalDiffuse;
    specular += practicalSpecular;
}
