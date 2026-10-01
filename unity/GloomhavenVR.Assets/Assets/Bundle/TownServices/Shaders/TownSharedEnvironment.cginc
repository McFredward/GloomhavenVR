// Native SH polynomial packing, applied to shared town surfaces only.
#ifndef GHVR_TOWN_SHARED_ENVIRONMENT
#define GHVR_TOWN_SHARED_ENVIRONMENT
half _TownSharedAmbient, _TownSharedKey;
half4 _TownAmbientAr, _TownAmbientAg, _TownAmbientAb, _TownAmbientBr, _TownAmbientBg, _TownAmbientBb, _TownAmbientC;
half4 _TownKeyDirection, _TownKeyColour;
half3 TownAmbient(half3 normal)
{
    if (_TownSharedAmbient < .5h) return ShadeSH9(half4(normal, 1));
    half4 n = half4(normal, 1);
    half3 value = half3(dot(_TownAmbientAr, n), dot(_TownAmbientAg, n), dot(_TownAmbientAb, n));
    half4 b = n.xyzz * n.yzzx;
    value += half3(dot(_TownAmbientBr, b), dot(_TownAmbientBg, b), dot(_TownAmbientBb, b));
    value += _TownAmbientC.rgb * (n.x * n.x - n.y * n.y);
    #if defined(UNITY_COLORSPACE_GAMMA)
        value = LinearToGammaSpace(value);
    #endif
    return value;
}
half3 TownKeyDirection(float3 position)
{
    return normalize(_TownSharedKey > .5h ? _TownKeyDirection.xyz : UnityWorldSpaceLightDir(position));
}
half3 TownKeyColour()
{
    return _TownSharedKey > .5h ? _TownKeyColour.rgb : _LightColor0.rgb;
}
#endif
