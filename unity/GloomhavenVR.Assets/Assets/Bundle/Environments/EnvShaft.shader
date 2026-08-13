// GloomhavenVR — moonlight shafts breaking through the forest canopy.
//
// The one thing that sells a night forest is the LIGHT, so the beams are real
// world-space geometry: each shaft is a pair of crossed tapered blades built by
// BuildEnvironmentRooms.cs, running from a gap in the canopy down into the
// clearing along the moon bearing. Crossed blades (never a camera-facing card —
// the permanent VR constraint) keep the shaft readable from every yaw and are
// identical in both eyes.
//
// UV convention from the builder: u = across the blade (0..1), v = along the
// beam (0 at the canopy gap, 1 where it dies on the forest floor). Vertex alpha
// scales the whole shaft so the builder can dim distant ones.
//
// VERTEX COLOUR RGB IS DATA, NOT A TINT (ModBuild 137). r = fade-in length,
// g = fade-out length, both in v units. The builder derives them from METRES
// and divides by each shaft's own length, so shafts of different lengths share
// one material and still fade over the same distance. This mattered the moment
// the shafts were run up THROUGH the canopy tear (so that the light is seen
// entering where the moon is seen): a fade of a fixed fifth of the length would
// have put the fade-out exactly across the opening and hidden the one thing the
// change exists to show. The channel used to be white and multiplied into the
// tint, so nothing else had to change.
//
// Additive, no depth write, Transparent queue: trunks and canopy occlude the
// beams correctly (ZTest LEqual against the opaque pass), and additive blending
// is order-independent so overlapping shafts never sort wrong.
Shader "GloomhavenVR/EnvShaft"
{
    Properties
    {
        _Tint ("Beam color (a = strength)", Color) = (0.62,0.72,1.0,0.5)
        _Softness ("Cross-section softness", Range(0.5,8)) = 3.0
        _Shimmer ("Shimmer amount", Range(0,1)) = 0.25
        _ShimmerSpeed ("Shimmer speed", Range(0,2)) = 0.22
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Softness, _Shimmer, _ShimmerSpeed;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 col : COLOR; float3 wn : TEXCOORD1; float3 wp : TEXCOORD2; };

            #define SKY_PERIOD 2880.0

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.col = v.color;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD);
                // across the blade: soft gaussian core, zero at both rims
                float x = (i.uv.x - 0.5) * 2.0;
                float across = exp(-x * x * _Softness);
                // along the beam: fades in at the canopy gap, dies out before it
                // reaches the ground (a shaft has no visible end, only a pool).
                // Both lengths come from the vertex colour, in v units.
                float v = i.uv.y;
                float along = smoothstep(0.0, i.col.r, v)
                            * smoothstep(1.0, 1.0 - i.col.g, v);
                // slow drifting density — motes and mist crossing the beam
                float sh = 1.0 + _Shimmer * (sin(v * 7.3 + t * _ShimmerSpeed * 6.1)
                                           * sin(v * 2.7 - t * _ShimmerSpeed * 3.3 + x * 1.9));
                // A blade seen face-on is a slab of lit air; seen edge-on it is
                // nothing. Without this a wide blade turns into a visible PANE OF
                // GLASS across the view. The two crossed blades are 90 deg apart,
                // so their sum stays roughly constant as you turn — which is what
                // a real shaft does. View-dependent but smooth and symmetric, so
                // it fuses in stereo (unlike any screen-space pattern).
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float facing = abs(dot(normalize(i.wn), V));
                float a = across * along * sh * facing * _Tint.a * i.col.a;
                return fixed4(_Tint.rgb * a, 1.0);   // col.rgb is data, see header
            }
            ENDCG
        }
    }
    Fallback Off
}
