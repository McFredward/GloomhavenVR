using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PLAIN-MESH WALL BODY — round 6 of the keep saga (heartbeat, 2026-08-05 23:39: "TRIPWIRE
/// 5 cache wall(s) carry NO fade-capable renderer — their shaders: Amp_Basic_N_MRAO,
/// VFX/ParticleMasterUnlitAdd_Shd, Amp_Basic" and "23 wall(s) FAIL-SAFE solid").
///
/// THE FINDING: on this tileset the keep's visible masonry consists of the cache walls' OWN
/// child meshes — but those meshes carry no WallFade-family shader, so
/// <c>RefreshSegment</c> (which collects fade-capable renderers only) left 23 of the 26
/// cache walls EMPTY: no renderers, no bounds, no room, "FAIL-SAFE solid" forever. The
/// MPB map/cutoff path cannot touch these shaders anyway; the mechanism proven to work on
/// hardware is <c>renderer.enabled = false</c> (the stacked/mounted delivery).
///
/// THE RULE: a cache wall whose subtree has MeshRenderers but ZERO fade-capable ones gets a
/// BODY — its plain child meshes collected as <see cref="MountedProp"/>s. The body gives
/// the segment its AABB (so the normal room association + coverage decision run on the real
/// masonry footprint, and stacked pieces can chain onto THEIR OWN face's column instead of
/// gravitating to the one torch-anchored wall — the round-6 S97-on-'Wall 2' imbalance), and
/// it delivers the fade by the pop-beats-invisible rule: best-effort cutoff/alpha ramp
/// during the dissolve, guaranteed <c>renderer.enabled=false</c> at the held threshold,
/// restored bit-for-bit on unfade. Ground-band meshes are stripped like everything else
/// (the foundation stays, matching the flat game's own band); doorway segments never get a
/// body decision (unchanged ruling); Lights are never written to. Held-state enforcement is
/// per-frame and the regen fast-reclaim covers body meshes too — Apparance regenerates the
/// masonry exactly like the shell.
///
/// A wall that GAINS fade-capable renderers (tileset mix / regen) drops its body takeover
/// on that same rescan — the game's own shader path always wins where it exists.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>PLAIN WALL BODY (round 6): the cache wall's own masonry meshes when none
        /// of them carries a fade-capable shader — they ARE the wall the user sees, fading
        /// via the enabled-toggle delivery. Empty for walls with real fade renderers.</summary>
        public readonly List<MountedProp> Body = new();
        public readonly List<MountedProp> PrevBody = new();
        /// <summary>0 = restored/untouched, 1 = dissolving, 2 = hidden.</summary>
        public int BodyState;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>Shaders already property-dumped this scene (one line each, cap 4) — the
        /// round-7 datum for a possible future WallFade-shader swap on body materials.</summary>
        private readonly HashSet<string> _dumpedBodyShaders = new();

        /// <summary>
        /// ONE-SHOT SHADER PROPERTY DUMP (round 7, defect a): name every property of a body
        /// wall's shader so the NEXT log decides, from data instead of another blind round,
        /// (a) whether the Amp masonry shaders expose the dissolve pair
        /// (_Toggle_Dissolve/_InvisibilityControl — then the ramp above is already a real
        /// dissolve) and (b) whether a material COPY could be swapped to Amp_Basic_WallFade
        /// (property compatibility: _MainTex/_BumpMap/… overlap) for the native fade path
        /// including the world-Y foundation gradient.
        /// </summary>
        private void DumpBodyShaderOnce(Material? mat)
        {
            if (mat == null || mat.shader == null || _dumpedBodyShaders.Count >= 4
                || !_dumpedBodyShaders.Add(mat.shader.name))
                return;
            var sb = new System.Text.StringBuilder();
            sb.Append("BODY SHADER PROPERTIES '").Append(mat.shader.name).Append("': ");
            try
            {
                Shader sh = mat.shader;
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append(sh.GetPropertyName(i)).Append('(')
                      .Append(sh.GetPropertyType(i)).Append(')');
                }
            }
            catch (System.Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }
            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>Restore ALL of a segment's body meshes — called on every path where the
        /// segment stops owning them, so no wall course can stay hidden without an owner.</summary>
        private void RestoreSegmentBody(Segment seg)
        {
            if (seg.BodyState == 0)
                return;
            seg.BodyState = 0;
            foreach (MountedProp p in seg.Body)
                RestoreProp(p);
        }

        /// <summary>
        /// Collect the plain child meshes of a cache wall with zero fade-capable renderers
        /// (called from <see cref="RefreshSegment"/> inside the tripwire branch): they become
        /// the segment's BODY and its AABB. Reuses the shared prop ledger so a mesh we are
        /// currently hiding is re-recognized (and its authored snapshot preserved) instead of
        /// skipped as "game-disabled".
        /// </summary>
        private void CollectPlainWallBody(Segment seg, MeshRenderer[] all)
        {
            seg.PrevBody.Clear();
            seg.PrevBody.AddRange(seg.Body);
            seg.Body.Clear();
            foreach (MeshRenderer r in all)
            {
                if (r == null || IsModObject(r))
                    continue;
                if (IsFigureOrActorRenderer(r))
                    continue; // FIGURES are never touched (round-7 ruling, Lights severity)
                if (RendererUsesFoliage(r))
                    continue; // rides the wall as a foliage attachment already
                if (!r.enabled && !_mountedTouched.ContainsKey(r))
                    continue; // the GAME disabled it — not ours to manage
                DumpBodyShaderOnce(r.sharedMaterial); // round 7: one line per shader, cap 4
                if (!_mountedTouched.TryGetValue(r, out MountedProp? prop))
                    prop = ClassifyProp(r);
                seg.Body.Add(prop);
                Bounds b = r.bounds;
                if (!seg.HasBounds)
                {
                    seg.Bounds = b;
                    seg.HasBounds = true;
                }
                else
                {
                    seg.Bounds.Encapsulate(b);
                }
            }
            if (seg.Body.Count > 0)
            {
                // The fade-ON line and the diag need an honest mechanism label — there is
                // no shader variant on a body wall.
                if (seg.ShaderNames == "?")
                    // Round 15: a body wall's meshes carry NO live wall-fade toggle by
                    // construction (that is why the segment got a body at all), so they
                    // dissolve through the material SWAP — not by popping.
                    seg.ShaderNames = "plain (no fade shader — dissolve-swap delivery)";
                float thickness = Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z);
                seg.BlockEps = Mathf.Clamp(0.5f * thickness, BlockEpsMinWorld, BlockEpsMaxWorld);
            }
            // Leavers: restore anything this segment held that it no longer owns.
            if (seg.BodyState != 0)
            {
                foreach (MountedProp prev in seg.PrevBody)
                {
                    if (prev.Renderer != null && !seg.Body.Contains(prev))
                        RestoreProp(prev);
                }
                if (seg.Body.Count == 0)
                    seg.BodyState = 0;
            }
            seg.PrevBody.Clear();
        }

        /// <summary>
        /// Drive the wall body alongside the segment's fade — same discipline as
        /// <see cref="ApplyStacked"/>: best-effort ramp, guaranteed disable at the held
        /// threshold, per-frame held-state enforcement (regen churn), lost mesh → prompt
        /// rescan, everything reversed exactly on unfade via the shared ledger.
        /// </summary>
        private void ApplyBody(Segment seg)
        {
            if (seg.Body.Count == 0)
                return;
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentBody(seg);
                return;
            }
            bool lost = false;
            foreach (MountedProp p in seg.Body)
            {
                if (p.Renderer == null)
                {
                    lost = true;
                    continue;
                }
                if (want == 2)
                {
                    if (p.Renderer.enabled)
                    {
                        _mountedTouched[p.Renderer] = p;
                        EnsureDissolveChannel(p);
                        DriveProp(p, 1f);
                        p.Renderer.enabled = false;
                    }
                }
                else
                {
                    _mountedTouched[p.Renderer] = p;
                    EnsureDissolveChannel(p); // round 15: everything that fades animates
                    DriveProp(p, seg.Fade);
                    if (!p.Renderer.enabled)
                        p.Renderer.enabled = true;
                }
            }
            if (lost)
                _nextRescan = 0f; // masonry regenerated mid-fade — re-collect promptly
            seg.BodyState = want;
        }
    }
}
