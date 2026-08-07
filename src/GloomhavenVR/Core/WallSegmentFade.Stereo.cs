using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// PER-EYE STRADDLE MEASUREMENT (round 14) — diagnostics only, nothing rendered differs.
///
/// USER RULE (2026-08-07, Lights/figures severity): "Entweder faded es auf beiden Augen oder
/// gar nicht" — a wall must never be faded in one eye and solid in the other.
///
/// WHY THIS FILE EXISTS AND CHANGES NOTHING. The rivalry is inside the GAME's own masonry
/// shader, and the algebra says it cannot be removed without removing the look with it (full
/// derivation in the <see cref="WallSegmentFade"/> class header). Rather than trade the look
/// away on a second guess, this measures the phenomenon on the rig: it evaluates the shader's
/// OWN discard scalar for BOTH eyes, from the live stereo view/projection matrices, over the
/// wall's AABB — and says per wall whether the two eyes can currently disagree, by how much,
/// and which term dominates. The next hardware log therefore either confirms the derivation
/// with numbers or refutes it, before anything about the rendering changes.
///
/// THE SHADER MATH IT MIRRORS (verified against the DXBC of <c>Amp_Basic_WallFade</c>,
/// blob216.PIXEL lines 190-229 — every constant below is an immediate literal there):
/// <code>
///   V = min(0.02·|worldPos − _WorldSpaceCameraPos|, 1) + min(radius, 1)
///   radius = |(0.5·aspect·(u−0.5), 0.5·(v−0.5))|        // u,v = ComputeScreenPos, per EYE
///   t = min(1, 3.3333·min(1, V^8 + max(1−worldY,0)/3))
///   S = t²(3−2t)                                        // smoothstep
///   discard  ⟺  S² + ν·S·(1−S) &lt; _Cutoff              // held state (map term m ≡ 0)
/// </code>
/// <c>ν</c> is the shader's world-space simplex noise, sampled over ±0.875 (measured), so a
/// pixel is CERTAINLY discarded below <c>S_gone</c>, CERTAINLY solid above <c>S_solid</c>, and
/// noise-dithered between — both roots computed from the wall's own authored Mask Clip Value.
///
/// Under MultiPass every one of those inputs is per eye: each pass carries its own view matrix,
/// its own <c>_WorldSpaceCameraPos</c> and its own screen coordinates. Two eyes therefore
/// evaluate the SAME wall at two different V, and where those land on opposite sides of
/// <c>S_gone</c> the wall is gone in one eye and standing in the other.
///
/// WHY NO FIX SHIPS WITH THIS (the closed proof, on the shader the keep actually uses —
/// <c>Amp_Basic_N_MRAO</c>, DXBC blob388 <c>_WALLFADE_ON_ON</c>, wall-fade block lines
/// 188-247; the same subgraph as <c>Amp_Basic_WallFade</c> at a different cbuffer register).
/// The full discard is
/// <code>
///   M = m·_EnableOcclusionMap        // cb0[10].x — an undeclared GLOBAL uniform, MPB-settable
///   A = max(M,S) + ν·(1 − max(M,S))  // ν = 42·snoise(worldPos·(6,7,10)) ∈ [−0.875, 0.875]
///   B = (M &gt; 0) ? 1 : S
///   discard  ⟺  saturate(1 + ToggleWallFade·(A·B − 1)) − _Cutoff &lt; 0     // line 244: mad_SAT
/// </code>
/// The mod's only reachable inputs are <c>_TilesOcclusionMap</c> (screen-UV sampled, so any
/// non-constant map is itself per eye), <c>_EnableOcclusionMap</c>, <c>ToggleWallFade</c> and
/// <c>_Cutoff</c> — all of them scalars, one value per renderer per frame. Then:
/// <list type="bullet">
/// <item>with <c>M &lt; 1</c> anywhere, <c>max(M,S)</c> reads S there, so the per-eye vignette
///   reaches the compare — and it does so with authority: wherever the vignette drives S to 1
///   the pixel is solid for every <c>_Cutoff &lt; 1</c>, and <c>_Cutoff ≥ 1</c> discards the
///   whole wall including the foundation. Partial + eye-identical is impossible here.</item>
/// <item>with <c>M ≥ 1</c> the vignette is gone — but so is the world-Y foundation ramp (both
///   live inside the same S), and the <c>mad_sat</c> then clamps <c>A·B &gt; 1</c> back to
///   exactly 1, so the discard degenerates to <c>1 − _Cutoff</c>: one comparison for the whole
///   renderer, i.e. the binary pop the user rejected. On the unsaturated
///   <c>Amp_Basic_WallFade</c> the same state would have left a per-pixel dissolve on the
///   shader's own world-space noise (<c>ν &gt; (M−c)/(M−1)</c>, view-independent) — the
///   saturate on the masonry variant is what closes that door.</item>
/// <item>the vignette's own coefficients (0.02, ^8, /3, 3.3333) are shader immediates, and its
///   two per-eye inputs, <c>_WorldSpaceCameraPos</c> and <c>_ScreenParams</c>, live in the
///   engine-owned <c>UnityPerCamera</c> cbuffer, which no MaterialPropertyBlock can reach.</item>
/// </list>
/// So on this shader "per-pixel dissolve" and "both eyes agree" are mutually exclusive, and the
/// choice between them is the user's, not ours. This file measures which poses actually pay
/// the price so that choice can be made on numbers.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>Last logged per-eye verdict (edge-triggered logging).</summary>
        public int StraddleVerdict = -1;
        public float NextStraddleLog;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>Straddle sweep cadence (s) — piggybacks the 2 Hz diag budget.</summary>
        private const float StraddleIntervalSeconds = 2f;
        /// <summary>Re-log cadence for an UNCHANGED verdict (s): the edge is what matters, but a
        /// standing rivalry must still be visible in a log that starts mid-session.</summary>
        private const float StraddleRelogSeconds = 20f;
        private float _nextStraddleSweep;

        /// <summary>Verdicts, ordered by severity (the value stored on the segment).</summary>
        private const int StraddleBothGone = 0;
        private const int StraddleBothSolid = 1;
        private const int StraddleBothDithered = 2;
        private const int StraddleDisagree = 3;

        /// <summary>
        /// Sweep every currently-fading HIGH-variant wall and report whether its two eyes can
        /// disagree at the present pose. Called once per <see cref="StraddleIntervalSeconds"/>
        /// from the driver tick; skipped entirely when the head camera has no stereo matrices
        /// (flat-screen / editor), so it costs nothing off-rig.
        /// </summary>
        private void SweepEyeStraddle(Camera head, float now)
        {
            if (now < _nextStraddleSweep || PerfConfig.Quiet)
                return;
            _nextStraddleSweep = now + StraddleIntervalSeconds;
            if (head == null || !head.stereoEnabled)
                return;

            Matrix4x4 vpL = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                * head.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 vpR = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)
                * head.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            Vector3 eyeL = EyePosition(head, Camera.StereoscopicEye.Left);
            Vector3 eyeR = EyePosition(head, Camera.StereoscopicEye.Right);
            // The shader's aspect is _ScreenParams.x/.y of the pass being rendered — under
            // MultiPass that is the EYE render target, not the desktop window.
            float aspect = EyeAspect(head);

            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds || seg.Fade <= 0f || !seg.VariantHigh)
                    continue; // the LOW variant has no vignette at all — nothing to straddle
                ReportStraddle(seg, vpL, vpR, eyeL, eyeR, aspect, now);
            }
        }

        private void ReportStraddle(Segment seg, Matrix4x4 vpL, Matrix4x4 vpR,
            Vector3 eyeL, Vector3 eyeR, float aspect, float now)
        {
            // Discard roots of  S² + ν·S(1−S) = c  at the noise extremes (ν = ±NoisePeak):
            //   S below the ν = +peak root  ⇒ discarded in EVERY pixel,
            //   S above the ν = −peak root  ⇒ solid in every pixel,
            //   between                     ⇒ the game's own dither band.
            float c = Mathf.Clamp(seg.HeldCutoff, 0.01f, 0.99f);
            float sGone = SolveDiscardRoot(c, NoisePeak);
            float sSolid = SolveDiscardRoot(c, -NoisePeak);

            float sMaxL = 0f, sMaxR = 0f, vMaxL = 0f, vMaxR = 0f;
            int samples = 0;
            Bounds b = seg.Bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                if (!EyeShaderS(corner, vpL, eyeL, aspect, out float sL, out float vL)
                    || !EyeShaderS(corner, vpR, eyeR, aspect, out float sR, out float vR))
                    continue; // behind an eye's near plane — that eye does not shade it
                samples++;
                if (sL > sMaxL) { sMaxL = sL; vMaxL = vL; }
                if (sR > sMaxR) { sMaxR = sR; vMaxR = vR; }
            }
            if (samples == 0)
                return; // wall entirely behind the head — nothing on screen to disagree about

            bool anySolidL = sMaxL >= sGone, anySolidR = sMaxR >= sGone;
            int verdict = anySolidL != anySolidR ? StraddleDisagree
                : !anySolidL ? StraddleBothGone
                : (sMaxL >= sSolid && sMaxR >= sSolid) ? StraddleBothSolid
                : StraddleBothDithered;

            bool edge = verdict != seg.StraddleVerdict;
            if (!edge && now < seg.NextStraddleLog)
                return;
            seg.StraddleVerdict = verdict;
            seg.NextStraddleLog = now + StraddleRelogSeconds;
            // Only the rivalry verdict is a warning; the healthy ones are informational and
            // exist so a log without warnings still PROVES the sweep ran on this pose.
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            string body =
                $"EYE-STRADDLE '{wall}' [HIGH] fade {seg.Fade:0.00} — the game shader's own "
                + $"discard scalar evaluated for BOTH eyes over the wall AABB "
                + $"({samples}/8 corners on screen, aspect {aspect:0.000}, cutoff {c:0.00}): "
                + $"L V={vMaxL:0.000} S={sMaxL:0.000} | R V={vMaxR:0.000} S={sMaxR:0.000} | "
                + $"ΔV={Mathf.Abs(vMaxL - vMaxR):0.000} ΔS={Mathf.Abs(sMaxL - sMaxR):0.000}; "
                + $"thresholds from this wall's Mask Clip Value: fully discarded below "
                + $"S={sGone:0.000}, fully solid above S={sSolid:0.000} (dithered between) ⇒ ";
            if (verdict == StraddleDisagree)
            {
                VRLog.Warn(Name, body
                    + $"EYES DISAGREE — {(anySolidL ? "LEFT" : "RIGHT")} eye still shows masonry "
                    + $"where the {(anySolidL ? "RIGHT" : "LEFT")} eye discarded it. This is the "
                    + "game shader's SCREEN-RADIAL vignette, which is centred on each eye's own "
                    + "screen: the asymmetric OpenXR frusta put the two rings ~15% of a screen "
                    + "width apart, and the 8th power turns that into a full 0/1 flip. The mod "
                    + "cannot reach it: the vignette is summed into the same saturate as the "
                    + "world-Y foundation ramp, before the single cutoff compare, and every "
                    + "coefficient is a shader immediate.");
            }
            else
            {
                VRLog.Info(Name, body + (verdict switch
                {
                    StraddleBothGone => "both eyes fully faded (no rivalry possible at this pose)",
                    StraddleBothSolid => "both eyes fully SOLID — the vignette is saturating the "
                        + "whole wall, so the fade is a no-op here (game-native behaviour)",
                    _ => "both eyes inside the shader's own dither band — same side of the "
                        + "crossover, no whole-wall disagreement",
                }) + ".");
            }
        }

        /// <summary>Measured extreme of the shader's <c>42·snoise(worldPos·(6,7,10))</c> term
        /// (60k samples of the exact Ashima simplex the DXBC implements: min −0.875, max +0.874,
        /// σ 0.265). It bounds how far the noise can push a pixel either side of the cutoff.</summary>
        private const float NoisePeak = 0.875f;

        /// <summary>Root of <c>S² + ν·S(1−S) = c</c> in S for a fixed ν (the shader's held-state
        /// discard test). Falls back to the noise-free root <c>√c</c> when the quadratic
        /// degenerates (ν = 1) or has no root in range.</summary>
        private static float SolveDiscardRoot(float c, float nu)
        {
            float a = 1f - nu, bq = nu, cq = -c;
            if (Mathf.Abs(a) < 1e-4f)
                return Mathf.Clamp01(Mathf.Abs(bq) < 1e-4f ? Mathf.Sqrt(c) : c / bq);
            float disc = bq * bq - 4f * a * cq;
            if (disc < 0f)
                return Mathf.Clamp01(Mathf.Sqrt(c));
            float root = (-bq + Mathf.Sqrt(disc)) / (2f * a);
            return Mathf.Clamp01(root);
        }

        /// <summary>Evaluate the shader's <c>S</c> (and the vignette scalar <c>V</c> that feeds
        /// it) for one world point in ONE eye. False when the point is behind that eye.</summary>
        private static bool EyeShaderS(Vector3 world, Matrix4x4 vp, Vector3 eye, float aspect,
            out float s, out float v)
        {
            s = 0f;
            v = 0f;
            Vector4 clip = vp * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= 1e-5f)
                return false;
            float u = clip.x / clip.w * 0.5f + 0.5f;
            float sv = clip.y / clip.w * 0.5f + 0.5f;
            float rx = 0.5f * aspect * (u - 0.5f);
            float ry = 0.5f * (sv - 0.5f);
            float radius = Mathf.Min(Mathf.Sqrt(rx * rx + ry * ry), 1f);
            float dist = Mathf.Min(0.02f * Vector3.Distance(world, eye), 1f);
            v = dist + radius;
            float t = Mathf.Min(1f, 3.3333333f * Mathf.Min(1f,
                Mathf.Pow(v, 8f) + Mathf.Max(1f - world.y, 0f) / 3f));
            s = t * t * (3f - 2f * t);
            return true;
        }

        /// <summary>World-space position of one eye (the inverse of its stereo view matrix —
        /// exactly what the engine writes into the pass's <c>_WorldSpaceCameraPos</c>).</summary>
        private static Vector3 EyePosition(Camera head, Camera.StereoscopicEye eye)
        {
            Matrix4x4 inv = head.GetStereoViewMatrix(eye).inverse;
            return new Vector3(inv.m03, inv.m13, inv.m23);
        }

        /// <summary>The aspect the shader sees (<c>_ScreenParams.x/.y</c> of the eye pass).</summary>
        private static float EyeAspect(Camera head)
        {
            if (XRSettings.enabled && XRSettings.eyeTextureHeight > 0)
                return XRSettings.eyeTextureWidth / (float)XRSettings.eyeTextureHeight;
            return head.pixelHeight > 0 ? head.pixelWidth / (float)head.pixelHeight : 1f;
        }
    }
}
