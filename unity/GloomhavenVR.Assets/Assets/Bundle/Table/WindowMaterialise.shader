// THE DEBRIS a floating window breaks into while it materialises or dematerialises.
//
// WHAT DRAWS THIS. Two MeshRenderers per animating window, parented to the window's world-space
// host rect and carrying the SAME vertex buffer with two different index lists: the shards that
// were launched toward the player and the shards that were launched away from it. They are seated
// on the panel's own distance ladder at +1 and -1, so the window is drawn between them
// (WorldUI/WindowMaterialiseDebris.cs explains why a single renderer cannot work: converted panels
// write no depth, ever, so sortingOrder is the only thing that can put geometry behind one).
//
// EVERY VERTEX IS A WHOLE PARTICLE'S STATE. The mesh is built once per effect; nothing on the CPU
// touches it again. The trajectory below is a closed function of ONE uniform, _Front, so a frame of
// this effect costs two SetFloats and one SetPropertyBlock no matter how many shards are in the
// air.
//
// ---------------------------------------------------------------------------------------------
// WHY THERE IS NO CAMERA, NO HEAD POSE AND NO CLOCK IN THIS FILE
// ---------------------------------------------------------------------------------------------
// THE USER'S RULING, 2026-08-26: "Der Effekt soll nicht an den Kopfbewegungen gebunden sein."
// A camera-facing billboard is head-bound BY DEFINITION - Unity's default particle render mode
// orients every quad toward the rendering camera, so the debris would silently rotate as he turned
// his head. That is why these are SOLIDS: four-faced closed tetrahedra with their own randomised
// orientation and their own tumble axis, whose appearance from any direction is a consequence of
// where they are and not of where he is looking. The same ruling already cost this project four
// rounds on the water surface, which was rebuilt with "NO VIEW DIRECTION ANYWHERE IN IT - no cube
// sample, no reflect(), no Fresnel, not even a half-vector specular".
//
// TRAP 1 - STEREO RIVALRY. Under MultiPass a billboard's orientation is recomputed per eye, and a
// screen-space threshold gives each eye a different value for the same surface point. The headset
// reads either as flicker rather than as texture. This project has paid for that lesson twice
// (memory: "aliasing-is-per-eye", "flicker-is-elements-toggling"). Here BOTH eyes rasterise the
// same triangles at the same world positions, because every vertex position is a function of the
// vertex's own attributes and per-draw uniforms and of nothing else. Genuine world-space geometry
// is per-eye correct BY CONSTRUCTION, which is a real argument in favour of this redesign over the
// painted plume it replaces - that one was correct for the same reason, but only because it never
// left the window's plane, which is exactly what the user objected to.
//
// The one remaining per-eye risk is spatial: sub-pixel geometry aliases differently in each eye
// whatever the shader does. That is answered in C# rather than here, by a 4 mm floor on shard size
// (~0.25 deg at 0.9 m, roughly ten headset pixels) which WindowMaterialiseDebris logs.
//
// TRAP 4 - FREQUENCY SCRUBBING. A strength dial that multiplies a FREQUENCY riding the shared
// clock is correct only at t=0. There is no clock here at all. The one time-like input is _Front,
// written once per frame by C#, and it is used only as a POSITION (where the erosion front stands)
// from which each shard's age is derived. _SizeScale is a pure amplitude. No dial multiplies a
// frequency, because no frequency exists.
//
// TRAP 5 - WINDING. A shard is a CLOSED solid, so unlike the flat quad this file used to draw its
// signed volume is meaningful and is the strong form of the gate. It is checked in C#
// (WindowMaterialiseDebris.GateShardWinding: positive signed volume AND every face normal pointing
// away from the shard's own centroid) and logged once per process, because nine meshes have now
// shipped in this project wound against the side they are seen from. Cull Back is therefore load
// bearing here, not a default - a shard that fails the gate disappears rather than looking odd,
// which is what makes the gate worth having.
//
// TRAP 6 - CULLING CANNOT SEE VERTEX SHADERS. Every shard's real position is computed below, so
// Unity's bounds would measure the undisplaced birth cloud - a box the size of the window - and
// pop the debris away as it travelled. The bounds are authored in C#, grown by the furthest any
// shard can reach. Same failure class as the displaced-geometry arc sweep in this project's memory.
//
// OPAQUE, WITH ZWRITE. Not a transparent blend: the shards are chips of a solid thing, they must
// occlude each other correctly in a cloud, and they must punch into the depth buffer so that room
// geometry drawn before them (walls at queue 2000, the MR backing plate at 2998) occludes them and
// they occlude anything depth-testing drawn after. Alpha blending would have needed per-shard
// depth sorting on the CPU every frame, which is the cost this design exists to avoid.
//
// MUST BE COMPILED BY 2021.3.5f1 (`/home/claw/unity-2021.3.5`). A shader compiled by 2021.3.45
// renders PINK in game - see MapUnlit.shader's header.
Shader "GloomhavenVR/WindowMaterialise"
{
    Properties
    {
        _Tint ("Shard tint", Color) = (0.80, 0.74, 0.62, 1)

        // The animation state. C# owns both of these; nothing here reads a clock or a camera.
        _Front ("Debris front position (threshold units)", Float) = -0.15
        _SizeScale ("Master size multiplier (intensity x tail fade)", Float) = 1.0

        // The field, mirrored from WorldUI/WindowMaterialiseField.cs. The values here are only what
        // an editor preview would show; C# pushes the live ones through a MaterialPropertyBlock.
        _LifeSpan ("Shard lifetime (threshold units)", Float) = 1.15
        _Wind ("Downwind direction, CANVAS-local xy (unit)", Vector) = (0.92, 0.39, 0, 0)
        _Drift ("Downwind travel at age 1 (host-local units)", Float) = 460
        _Fall ("Fall at age 1 (host-local units)", Float) = 100
        _SpinTurns ("Spin scale", Float) = 1.0

        // Shading. A FIXED WORLD direction - not a light, not a view vector, not a half vector.
        _KeyDir ("Key light direction (world, unit)", Vector) = (0.42, 0.78, -0.46, 0)
        _Ambient ("Ambient term", Range(0,1)) = 0.34
        _Key ("Key term", Range(0,2)) = 0.78
        _Fill ("Back-fill term", Range(0,1)) = 0.20
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+250" "IgnoreProjector"="True" }
        Pass
        {
            // See TRAP 5. The shard is a closed solid whose winding is gated in C#.
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;   // birth point, host-local
                float3 normal : NORMAL;     // face normal, shard-local
                float4 tangent : TANGENT;   // corner offset xyz (shard-local, unit), size w
                float4 uv0 : TEXCOORD0;     // birth uv xy, erosion threshold z, seedA w
                float4 uv1 : TEXCOORD1;     // tumble axis xyz, turns w
                float4 uv2 : TEXCOORD2;     // out-of-plane velocity x, drift scale y, seedB z, wander w
                float4 color : COLOR;       // per-shard shade jitter
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;     // WORLD normal. Derived from unity_ObjectToWorld only.
                float4 col : TEXCOORD1;
            };

            fixed4 _Tint;
            float _Front, _SizeScale, _LifeSpan, _Drift, _Fall, _SpinTurns;
            float _Ambient, _Key, _Fill;
            float4 _Wind, _KeyDir;

            // Rodrigues. The tumble is a function of the shard's own age, so it is head-independent
            // and identical in both eyes; a shard presents whatever face its own history gives it.
            float3 spin(float3 v, float3 k, float th)
            {
                float c = cos(th), s = sin(th);
                return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
            }

            v2f vert (appdata v)
            {
                v2f o;

                // ---- AGE. Mirrors WindowMaterialiseField.Age exactly ---------------------------
                // The threshold rode in on the vertex; it is the value of the SAME erosion field the
                // window's own CanvasRenderer alphas are driven by, sampled at the point this shard
                // was torn from. That is what makes a shard leave in the frame its own patch of
                // window goes dark.
                float age = saturate((_Front - v.uv0.z) / max(_LifeSpan, 1e-3));

                // ---- SIZE. Mirrors WindowMaterialiseField.SizeEnvelope exactly ------------------
                // Exactly 0 at age 0 and at age 1, which is what leaves a completed appear with no
                // debris sitting on the finished window and a completed vanish with none in the air.
                float env = smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.72, 1.0, age));
                float size = v.tangent.w * env * _SizeScale;

                // ---- THE TRAJECTORY, in the host canvas's own local units ----------------------
                float a2 = age * age;
                float3 p = v.vertex.xyz;

                // Downwind, IN the window's plane.
                p.xy += _Wind.xy * (_Drift * v.uv2.y * pow(age, 1.35));

                // OUT OF THE PLANE. This term is the entire redesign: the effect it replaces had no
                // such term, every flake it drew stayed in the window's plane, and the user called
                // the result "eher ein 2D-Effekt". The sign is a per-shard constant baked on the
                // CPU, so which shards go behind the window never depends on where the head is.
                p.z += v.uv2.x * age;

                // A little fall, and a per-shard wander so the cloud does not read as a rigid field
                // being translated. Three sines of the shard's OWN seeds against its OWN age - a
                // function of _Front, never of a clock.
                p.y -= _Fall * a2;
                float wa = v.uv2.w * age;
                p += wa * float3(sin(6.28318 * (v.uv0.w + 1.7 * age)),
                                 sin(6.28318 * (v.uv2.z + 2.3 * age)),
                                 sin(6.28318 * (v.uv0.w + v.uv2.z + 1.3 * age)));

                // ---- TUMBLE ---------------------------------------------------------------------
                float th = 6.28318 * v.uv1.w * _SpinTurns * age;
                float3 axis = v.uv1.xyz;
                float3 corner = spin(v.tangent.xyz, axis, th);
                float3 nrm = spin(v.normal, axis, th);

                float3 obj = p + corner * size;
                o.pos = UnityObjectToClipPos(float4(obj, 1.0));
                // UnityObjectToWorldNormal reads unity_WorldToObject. No camera matrix is involved.
                o.nrm = UnityObjectToWorldNormal(nrm);
                o.col = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // A FIXED WORLD KEY, and deliberately not a real light: the game's lighting is a
                // deferred rig the mod does not own, and a Fresnel or half-vector term would put the
                // head back into the effect through the shading instead of through the geometry.
                // Two opposed lambert terms so a face turned away from the key is dim rather than
                // black, which is what makes the four facets of a shard read as a solid.
                float3 n = normalize(i.nrm);
                float3 L = normalize(_KeyDir.xyz);
                float d = dot(n, L);
                float shade = _Ambient + _Key * saturate(d) + _Fill * saturate(-d);
                return fixed4(_Tint.rgb * shade * i.col.rgb, 1.0);
            }
            ENDCG
        }
    }
}
