// Stable real-lamp contributions. ForwardBase OnlyDirectional excludes Unity's
// point-light SH approximation, so a practical is evaluated exactly once here.
// World positions and range math stay float at the game's ~198 world units/metre.
float4 _TownPracticalPositions[32];
float4 _TownPracticalColours[32];
int _TownPracticalCount;

void TownPracticals(float3 position, half3 normal, half3 view, half exponent,
                    out half3 diffuse, out half3 specular)
{
    diffuse = 0;
    specular = 0;
    [loop] for (int i = 0; i < _TownPracticalCount; ++i)
    {
        float4 lamp = _TownPracticalPositions[i];
        if (lamp.w <= 0) continue;
        float3 offset = lamp.xyz - position;
        float squareDistance = max(dot(offset, offset), 0.000001);
        float rangeDistance = squareDistance * lamp.w;
        if (rangeDistance >= 1) continue;
        half3 direction = offset * rsqrt(squareDistance);
        // Match the existing vertex-light falloff near a stand, then smoothly reach
        // zero at the actual lamp range instead of popping at a renderer boundary.
        float attenuation = (1 - smoothstep(0.64, 1.0, rangeDistance)) / (1 + 25 * rangeDistance);
        half incidence = saturate(dot(normal, direction));
        half3 colour = _TownPracticalColours[i].rgb * (attenuation * incidence);
        diffuse += colour;
        if (exponent > 0)
            specular += colour * pow(saturate(dot(normal, normalize(direction + view))), exponent);
    }
}
