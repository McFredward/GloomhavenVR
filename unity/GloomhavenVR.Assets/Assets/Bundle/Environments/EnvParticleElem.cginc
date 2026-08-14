// ============================================================================
//  ELEMENT ART — the shared element response of the two bundled particle
//  shaders (EnvParticleAdd, EnvParticleAlpha). One file because the two differ
//  only in their blend mode: an emitter should not answer the elements
//  differently depending on whether it is additive or alpha-blended, and two
//  copies of this arithmetic would drift apart the first time one was tuned.
//
//  See EnvParticleAdd.shader's header for WHAT the two mechanisms are (a GATE
//  for emitters that exist for one element, a MODULATION for emitters that
//  exist anyway) and why a gate has to collapse geometry rather than fade alpha.
//
//  All of it happens ONCE PER VERTEX — four times per particle — and only while
//  something is up. The fragment stage of both shaders is untouched.
// ============================================================================
#ifndef GHVR_ENV_PARTICLE_ELEM_INCLUDED
#define GHVR_ENV_PARTICLE_ELEM_INCLUDED

float4 _ElemOwn, _ElemOwn2;     // gate weights   (fire,ice,air,earth) / (light,dark,-,-)
float4 _ElemMod, _ElemMod2;     // modulation weights, same order
fixed4 _ElemCol;
float _ElemGain, _ElemAlpha, _ElemTintAmt, _ElemSpark;
// The shared environment epoch — the twinkle below has to run on the SAME clock
// on every client or two players watch the same ember flicker out of phase.
float _GhvrTimeOfs;

/// Apply the gate and the modulation to one particle vertex.
/// `col` is the particle's colour (vertex colour x material tint) and is
/// modified in place. Returns false when the emitter's element is down, i.e.
/// when the caller must collapse the quad.
///
/// THE WEIGHTS ARE A DOT PRODUCT, not a switch, and that is deliberate: an
/// emitter can be owned by a COMBINATION (the steam of Fire+Ice would be
/// (0.5,0.5,0,0)) and a modulation can be signed, which is how the ground fog
/// thickens under Dark and thins under Light in one term — see BuildEnvironments
/// for the authored weights and the reasoning behind each.
bool GhvrParticleElem (float4 vertex, inout float4 col)
{
    GhvrElem e = GhvrElems();
    if (e.live <= 0.0)
    {
        // Nothing is up. A GATED emitter is invisible (its element is down by
        // definition); an unconditional one is exactly what it always was.
        float w = dot(_ElemOwn, float4(1, 1, 1, 1)) + _ElemOwn2.x + _ElemOwn2.y;
        return w <= 0.0;
    }

    float4 A = float4(e.fire, e.ice, e.air, e.earth);
    float2 B = float2(e.light, e.dark);

    // ---- the gate ----
    float w2 = dot(_ElemOwn, float4(1, 1, 1, 1)) + _ElemOwn2.x + _ElemOwn2.y;
    if (w2 > 0.0)
    {
        float g = saturate(dot(_ElemOwn, A) + dot(_ElemOwn2.xy, B));
        if (g <= 0.0) return false;
        // Fade IN with the element as well as switching on: ElementMood ramps
        // over a second and the waning plateau breathes 0.28..0.52, so a swarm
        // that snapped to full would throw away the one cue the smoothing exists
        // to give — that an element is on its way out.
        col.a *= g;
    }

    // ---- the modulation ----
    float m = dot(_ElemMod, A) + dot(_ElemMod2.xy, B);
    if (m != 0.0)
    {
        col.rgb *= max(1.0 + _ElemGain * m, 0.0);
        col.a *= max(1.0 + _ElemAlpha * m, 0.0);
        col.rgb = lerp(col.rgb, _ElemCol.rgb, saturate(m * _ElemTintAmt));

        // The twinkle that turns a soft bokeh into a spark. A per-PARTICLE
        // random would need a custom vertex stream on every emitter that shares
        // this shader (a layout mismatch on any one of them draws garbage), so
        // the phase comes from the particle's own WORLD POSITION instead: three
        // smooth waves, decorrelated enough that two particles a hand apart
        // twinkle independently, and smooth enough that the four corners of one
        // quad always agree — a quantised hash would tear a single sprite into
        // four differently-lit corners.
        // NOT a hash: sin() as an animation curve is fine, sin() inside a hash
        // is banned (its last bits differ between GPU vendors).
        if (_ElemSpark > 0.0)
        {
            float3 wp = mul(unity_ObjectToWorld, vertex).xyz;
            float t = _Time.y + _GhvrTimeOfs;
            float s = sin(wp.x * 2.7 + t * 6.1)
                    * sin(wp.z * 3.1 - t * 4.7)
                    * sin(wp.y * 2.3 + t * 5.3);
            col.rgb *= max(1.0 + _ElemSpark * saturate(m) * s, 0.0);
        }
    }
    return true;
}

#endif // GHVR_ENV_PARTICLE_ELEM_INCLUDED
