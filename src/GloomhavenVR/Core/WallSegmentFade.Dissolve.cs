using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// DISSOLVE CHANNEL — round 15 (user, 2026-08-08, on the masonry above the gate arch: "Die
/// Mauer über dem Torbogen verschwindet jetzt und taucht wieder auf wie gewollt, allerdings
/// OHNE Animation! Ich will, dass auch bei diesem Element (wie bei allen) die Animation beim
/// Verschwinden und Auftauchen spielt."). The standing rule is absolute: EVERYTHING that
/// fades, fades with the animation — on BOTH edges.
///
/// WHY THE GATE'S PIECES POPPED (ModBuild-82 hardware log, read from source + log):
/// <list type="number">
/// <item>The gate column owns ZERO wall renderers of its own; its six embedding courses
///   ('polySurface1/2', 'TO_Fort_WallTop02 (1)', 'TO_Fort_LowWall_01 (1)') arrive as STACKED
///   SHELL pieces, i.e. as <see cref="MountedProp"/>s delivered by <c>DriveProp</c>.</item>
/// <item>Those very renderer NAMES are listed as the TOGGLE-NATIVE wall renderers of 'Wall
///   1/6/7/8' in the same log, and the toggle census names their materials
///   ('CV_Generic_Rock_M', 'CV_UnderWall_Rock_M', … — Amp_Basic_N_MRAO, authored
///   <c>_WallFade_On=1</c>, keyword <c>_WALLFADE_ON_ON</c>). So every one of them satisfies
///   <see cref="FadeDriver.HasLiveWallFadeToggle"/> — and the round-11 swap therefore
///   DECLINED them with "native path already animates this piece".</item>
/// <item>But on the gate column there IS no native path: the wall-renderer MPB ramp in
///   <c>Apply</c> only writes <c>Segment.Renderers</c>, which is empty for a gate. What the
///   pieces actually got was <c>DriveProp</c>'s generic FOLIAGE cutoff lerp — <c>_Cutoff</c>
///   alone, with no <c>_TilesOcclusionMap</c> and no <c>ToggleWallFade</c> — which the Amp
///   fade subgraph does not read as a dissolve. Nothing animated; the guaranteed
///   <c>renderer.enabled=false</c> at the end of the ramp was the whole visible event. POP.</item>
/// <item>Compounding it on the FIRST fade of a session: the swap template is captured lazily
///   from the first live toggle-native WALL material, and in the ModBuild-82 log the gate's
///   first <c>fade ON</c> (line 545) precedes the first TOGGLE-NATIVE MATERIAL line (715) —
///   so even a piece that had needed the swap could not have got one.</item>
/// </list>
///
/// THE FIX — one channel decision per piece, made once, covering every enabled-only class
/// (stacked-shell rides, plain wall bodies, corner pieces, mounted dressing):
/// <list type="bullet">
/// <item>NATIVE: every material slot carries a LIVE wall-fade toggle ⇒ the piece is driven
///   with the wall renderers' OWN MPB ramp (<see cref="FadeDriver.DriveNativeProp"/>: noise
///   map + <c>_Cutoff</c> sweep during the transition, held occlusion map at fade 1). This is
///   the game's own masonry dissolve, per-pixel and opaque. No material is touched at all.</item>
/// <item>SWAP: some slot has no working fade channel ⇒ that slot is replaced by a COPY on the
///   masonry fade shader (template = the first live toggle-native material seen this scene,
///   now donated by props as well as walls, so the capture can no longer lose the race), and
///   the piece is driven by the same native ramp. Slots that were already native are kept as
///   authored and never destroyed.</item>
/// <item>ENABLED-ONLY is what is left after both, and every such piece now carries the REASON
///   it could not be given a channel (<see cref="MountedProp.DissolveWhy"/>) into the
///   per-segment DISSOLVE CENSUS line — a remaining popper is attributable from the log
///   instead of from the eye.</item>
/// </list>
///
/// Opaque per-pixel clip only — no alpha blending anywhere (MR chroma-key ruling). RESTORE is
/// exact: authored <c>sharedMaterials</c> reassigned bit-for-bit, only OUR copies destroyed,
/// MPB cleared — on unfade, ownership release, toggle-off, teardown and scene change.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>Round-15 DISSOLVE CENSUS bookkeeping: one line per fade episode (reset when
        /// the segment goes fully solid), re-logged only when the enabled-only count changes.</summary>
        public bool DissolveCensusLogged;
        public int DissolveCensusEnabledOnly = -1;
        public float NextDissolveCensus;

        /// <summary>ROUND 15: asset siblings were the LAST enabled-only class — they used to
        /// stay fully solid through the whole dissolve and then switch off at the end ("no
        /// dissolve ramp — siblings run arbitrary opaque shaders where a cutoff MPB means
        /// nothing"), which is precisely the pop the standing rule forbids. They now carry a
        /// <see cref="MountedProp"/> record each, so the same channel decision (native toggle /
        /// material swap) and the same ramp apply. Kept per SEGMENT rather than in the shared
        /// mounted ledger on purpose: siblings are owned structurally, not by the mounted
        /// sweep, and putting them in that ledger would make the orphan guard release them
        /// every rescan.</summary>
        public readonly Dictionary<MeshRenderer, MountedProp> SiblingProps = new();

        /// <summary>Per-foliage dissolve records, same shape and same reasoning as
        /// <see cref="SiblingProps"/>. ModBuild 254: foliage was the LAST attachment class still
        /// driven by the round-3 shared-MPB guess (one <c>_Cutoff</c> lerp from a hardcoded
        /// 0.35, written blind to every foliage material, then <c>renderer.enabled = false</c>
        /// at the end of the ramp). Round 15 had already retired that assumption for siblings —
        /// "a channel-less material gets COPIES on the game's masonry fade shader, a
        /// toggle-native one gets the wall's own map/_Cutoff ramp" — and foliage simply never
        /// got the same treatment. It is the largest population of all (345 attachments in the
        /// ModBuild 253 scenario, 123 on 'Wall 2' alone), so it is also the most visible thing
        /// in the scene when the guess is wrong.</summary>
        public readonly Dictionary<MeshRenderer, MountedProp> FoliageProps = new();
    }

    private sealed partial class FadeDriver
    {
        /// <summary>The masonry fade shader donated by the first live toggle-native material of
        /// the scene (null = no swap available; pieces without a native channel then stay
        /// enabled-only and say so in the census).</summary>
        private Shader? _masonryFadeShader;
        /// <summary>Census: pieces currently dissolving on the native ramp without any material
        /// change, and pieces dissolving on swapped copies.</summary>
        private int _nativeTotal;
        private int _swapTotal;
        private float _nextSwapLog;
        /// <summary>Scratch for the per-segment DISSOLVE CENSUS line.</summary>
        private readonly List<string> _dissolveWhyScratch = new();

        /// <summary>Capture the swap template from a LIVE toggle-native material — called both
        /// from CollectWallFadeInfo (wall renderers) and from
        /// <see cref="EnsureDissolveChannel"/> (any attachment piece). The prop-side donation
        /// exists because the wall-side one loses the race on the first fade of a scenario:
        /// the ModBuild-82 log shows the gate's first fade ON BEFORE the first toggle-native
        /// wall material was ever seen.</summary>
        private void CaptureMasonryTemplate(Material m)
        {
            if (_masonryFadeShader == null && m.shader != null)
                _masonryFadeShader = m.shader;
        }

        /// <summary>
        /// Decide ONCE how this piece dissolves, and put that channel in place. Scope: plain
        /// pieces with no channel of their own — particles, alpha props and Amp-dissolve props
        /// already animate through <see cref="DriveProp"/> and are left exactly as they were.
        /// Figures can never get here (every collector guards), and the arch/doorway never
        /// fades at all.
        /// </summary>
        private void EnsureDissolveChannel(MountedProp p)
        {
            if (p.SwapChecked)
                return;
            Renderer r = p.Renderer;
            if (r == null)
                return;

            // Channels that already animate: leave them alone (and mark them decided, so the
            // census can tell "animates by its own means" from "could not be given a channel").
            if (p.System != null || p.ColorId >= 0 || p.DissolveControlId >= 0)
            {
                p.SwapChecked = true;
                p.DissolveWhy = null;
                return;
            }

            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            if (_matScratch.Count == 0)
            {
                p.DissolveWhy = "renderer has no materials";
                p.SwapChecked = true;
                return;
            }
            int needSwap = 0;
            foreach (Material m in _matScratch)
            {
                if (m == null)
                {
                    // Half-built renderer (Apparance streams materials in): do NOT latch a
                    // verdict — retry on the next frame of the ramp.
                    p.DissolveWhy = "half-built renderer (null material slot) — retried next frame";
                    return;
                }
                if (HasLiveWallFadeToggle(m))
                    CaptureMasonryTemplate(m); // late-donor: props may beat the walls to it
                else
                    needSwap++;
            }

            if (needSwap == 0)
            {
                // NATIVE: the piece's own materials run the game's masonry fade branch. It
                // only ever lacked the DRIVE — which is the wall renderers' map/_Cutoff MPB
                // ramp, not the foliage cutoff lerp DriveProp used to hand it (round-15 gate
                // bug). Nothing is swapped, nothing is destroyed, nothing to restore but the
                // property block.
                p.SwapChecked = true;
                p.NativeFade = true;
                p.DissolveWhy = null;
                _nativeTotal++;
                return;
            }

            if (_masonryFadeShader == null)
            {
                p.DissolveWhy = $"{needSwap} material slot(s) have no fade channel and no swap "
                    + "template was captured in this scene (no live toggle-native material seen "
                    + "yet) — enabled-only";
                return; // NOT latched: a template may still arrive this scene
            }
            if (r is not MeshRenderer mr || mr == null)
            {
                p.DissolveWhy = $"{RendererKind(r)} with {needSwap} channel-less material "
                    + "slot(s) — only MeshRenderer materials may be swapped";
                p.SwapChecked = true;
                return;
            }

            Material[] src = mr.sharedMaterials;
            if (src == null || src.Length == 0)
                return; // raced with the collection above — retry next frame
            var copies = new Material[src.Length];
            var owned = new bool[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                Material? m = src[i];
                if (m == null)
                {
                    // Raced with a half-built renderer: abort cleanly — destroy the copies
                    // already built for earlier slots (never leak one) and retry next frame.
                    for (int j = 0; j < i; j++)
                    {
                        if (owned[j] && copies[j] != null)
                            UnityEngine.Object.Destroy(copies[j]);
                    }
                    return;
                }
                if (HasLiveWallFadeToggle(m))
                {
                    copies[i] = m; // already native — keep the authored material untouched
                }
                else
                {
                    copies[i] = BuildSwapMaterial(m);
                    owned[i] = true;
                }
            }
            p.SwapOriginals = src;
            p.SwapCopies = copies;
            p.SwapOwned = owned;
            p.SwapChecked = true;
            p.NativeFade = true; // driven by the same native ramp
            p.DissolveWhy = null;
            mr.sharedMaterials = copies;
            // PERF S2: this renderer's shader family just changed under the scene census —
            // force the next cycle to re-derive its facts. See _censusMaterialsDirty.
            _censusMaterialsDirty = true;
            _swapTotal++;
            float now = Time.unscaledTime;
            if (now >= _nextSwapLog)
            {
                _nextSwapLog = now + 5f;
                VRLog.Info(Name,
                    $"DISSOLVE-SWAP: '{mr.name}' (+{_swapTotal - 1} earlier) dissolves on "
                    + $"'{_masonryFadeShader.name}' material copies for {needSwap} of "
                    + $"{src.Length} slot(s) — textures/props copied from the authored "
                    + "materials, opaque per-pixel clip (no alpha blending — MR ruling); "
                    + "authored materials restored on unfade (round 15: everything that fades "
                    + "animates, on both edges).");
            }
        }

        /// <summary>One swap copy: template shader + every template-declared property the
        /// source material can donate; fade keyword and gate enabled on OUR copy only.</summary>
        private Material BuildSwapMaterial(Material source)
        {
            var mat = new Material(_masonryFadeShader!)
            {
                name = "GloomhavenVR.DissolveSwap." + source.name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Shader sh = _masonryFadeShader!;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string prop = sh.GetPropertyName(i);
                if (!source.HasProperty(prop))
                    continue;
                switch (sh.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        mat.SetColor(prop, source.GetColor(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        mat.SetVector(prop, source.GetVector(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        mat.SetFloat(prop, source.GetFloat(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        mat.SetTexture(prop, source.GetTexture(prop));
                        mat.SetTextureScale(prop, source.GetTextureScale(prop));
                        mat.SetTextureOffset(prop, source.GetTextureOffset(prop));
                        break;
                }
            }
            // A '_MainTex'-less source (e.g. 'Standard' uses _MainTex too, so this is rare)
            // falls back to the renderer's main texture via Unity's implicit mapping.
            if (mat.GetTexture("_MainTex") == null && source.mainTexture != null)
                mat.SetTexture("_MainTex", source.mainTexture);
            // OUR copy opens the fade branch (never a shared game material).
            mat.EnableKeyword(WallFadeOnKeyword);
            if (mat.HasProperty(WallFadeOnMatId))
                mat.SetFloat(WallFadeOnMatId, 1f);
            if (mat.HasProperty(CutoffId))
                mat.SetFloat(CutoffId, 0.5f);
            return mat;
        }

        /// <summary>
        /// Drive a piece through the WALL RENDERERS' OWN fade ramp (called from DriveProp when
        /// <see cref="MountedProp.NativeFade"/> is set — natively-toggled materials as well as
        /// swapped copies). Identical math to <c>Apply</c>'s wall path: the held occlusion map
        /// (r=1,a=0 ⇒ map term 0) with the piece's AUTHORED Mask Clip Value at fade 1, and the
        /// noise map with a swept <c>_Cutoff</c> during the transition — an opaque per-pixel
        /// clip dissolve (no alpha blending — MR chroma-key ruling).
        /// </summary>
        private void DriveNativeProp(MountedProp p, float fade)
        {
            if (!EnsureTextures())
                return;
            _mountedMpb ??= new MaterialPropertyBlock();
            _mountedMpb.Clear();
            _mountedMpb.SetInteger(ToggleWallFadeId, 1);
            _mountedMpb.SetFloat(ToggleWallfadeMatId, 1f);
            _mountedMpb.SetFloat(WallFadeOnMatId, 1f);
            if (fade >= 1f)
            {
                _mountedMpb.SetTexture(TilesOcclusionMapId, _occludedTex!);
                // The piece's own authored clip value, clamped exactly like a wall's
                // HeldCutoff (0 would disable the LOW discard, ≥1 would kill the HIGH
                // foundation band).
                _mountedMpb.SetFloat(CutoffId,
                    p.CutoffId >= 0 ? Mathf.Clamp(p.BaseCutoff, 0.05f, 0.95f) : 0.5f);
            }
            else
            {
                _mountedMpb.SetTexture(TilesOcclusionMapId, _noiseTex!);
                _mountedMpb.SetFloat(CutoffId, Mathf.Lerp(-0.05f, 1f, fade));
            }
            p.Renderer.SetPropertyBlock(_mountedMpb);
        }

        /// <summary>Undo a swap exactly: authored materials back (renderer permitting), and only
        /// the copies WE created destroyed — a natively-toggled slot that was kept as authored
        /// is a shared game material and must never be touched. A renderer that died mid-swap
        /// still has our copies destroyed (no leak).</summary>
        private static void RestorePropSwap(MountedProp p, Renderer? r)
        {
            p.NativeFade = false;
            p.DissolveWhy = null;
            if (p.SwapCopies == null)
            {
                p.SwapChecked = false;
                return;
            }
            if (r is MeshRenderer mr && mr != null && p.SwapOriginals != null)
            {
                mr.sharedMaterials = p.SwapOriginals;
                // PERF S2: the authored materials are back — the census's cached shader
                // verdict for this renderer is stale. See _censusMaterialsDirty.
                _censusMaterialsDirty = true;
            }
            for (int i = 0; i < p.SwapCopies.Length; i++)
            {
                if (p.SwapOwned != null && i < p.SwapOwned.Length && !p.SwapOwned[i])
                    continue; // authored, shared, never ours
                Material m = p.SwapCopies[i];
                if (m != null)
                    UnityEngine.Object.Destroy(m);
            }
            p.SwapCopies = null;
            p.SwapOwned = null;
            p.SwapOriginals = null;
            p.SwapChecked = false;
        }

        // ---- per-segment dissolve census ---------------------------------------------------

        /// <summary>Minimum gap between two DISSOLVE CENSUS lines for the same segment.</summary>
        private const float DissolveCensusIntervalSeconds = 5f;
        /// <summary>How many enabled-only reasons a census line names.</summary>
        private const int DissolveCensusReasonCap = 6;

        /// <summary>Which dissolve channel a piece currently runs on.</summary>
        private static int DissolveClassOf(MountedProp p)
        {
            if (p.Renderer == null)
                return -1;
            if (p.SwapCopies != null)
                return 1;               // swapped copies (+ native ramp)
            if (p.NativeFade)
                return 0;               // the game's own masonry fade branch
            if (p.System != null || p.ColorId >= 0 || p.DissolveControlId >= 0)
                return 2;               // alpha / particle / Amp-dissolve — animates already
            return 3;                   // enabled-only: no animation
        }

        /// <summary>
        /// The channel a piece runs on, or — for one not yet evaluated — the channel it WILL
        /// get, decided by exactly the predicates <see cref="EnsureDissolveChannel"/> uses.
        /// The prediction exists because the <c>fade ON</c> line is written by the decision
        /// loop, one step BEFORE the frame's <c>Apply</c> establishes the channels; without it
        /// the marker on that line could only ever read "pending".
        /// </summary>
        private int PredictDissolveClass(MountedProp p)
        {
            int c = DissolveClassOf(p);
            return c != 3 ? c : PredictClassOfRenderer(p.Renderer);
        }

        /// <summary>The channel a renderer with no record yet would be given (asset siblings are
        /// only recorded once their segment actually starts fading).</summary>
        private int PredictClassOfRenderer(Renderer? r)
        {
            if (r == null)
                return -1;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            if (_matScratch.Count == 0)
                return 3;
            int needSwap = 0;
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    return 3; // half-built — undecidable right now
                if (!HasLiveWallFadeToggle(m))
                    needSwap++;
            }
            if (needSwap == 0)
                return 0;
            return _masonryFadeShader != null && r is MeshRenderer ? 1 : 3;
        }

        /// <summary>Compact channel breakdown of everything riding this segment's fade — the
        /// round-15 replacement for the <c>fade ON</c> line's blanket '[enabled-only]' marker.
        /// 'enabled-only 0' means nothing in this fade can pop.</summary>
        private string DissolveBreakdown(Segment seg)
        {
            int native = 0, swap = 0, own = 0, pop = 0;
            foreach (MountedProp p in seg.Stacked)
                Bump(PredictDissolveClass(p));
            foreach (MountedProp p in seg.Body)
                Bump(PredictDissolveClass(p));
            foreach (MountedProp p in seg.Mounted)
                Bump(PredictDissolveClass(p));
            foreach (MeshRenderer s in seg.Siblings)
            {
                if (s == null)
                    continue; // never a dictionary key (and nothing left to classify)
                Bump(seg.SiblingProps.TryGetValue(s, out MountedProp? sp)
                    ? PredictDissolveClass(sp)
                    : PredictClassOfRenderer(s));
            }
            foreach (CornerPiece cp in _cornerPieces)
            {
                if (ReferenceEquals(cp.A, seg))
                    Bump(PredictDissolveClass(cp.Prop));
            }
            return $"{native} native-dissolve, {swap} dissolve-swap, {own} own alpha/particle "
                + $"channel, {pop} enabled-only"
                + (pop > 0 ? " ⚠ POPS — see the DISSOLVE CENSUS line" : string.Empty);

            void Bump(int c)
            {
                switch (c)
                {
                    case 0: native++; break;
                    case 1: swap++; break;
                    case 2: own++; break;
                    case 3: pop++; break;
                }
            }
        }

        private void TallyPiece(MountedProp p, ref int native, ref int swapped, ref int own,
            ref int enabledOnly)
        {
            switch (DissolveClassOf(p))
            {
                case 0: native++; break;
                case 1: swapped++; break;
                case 2: own++; break;
                case 3:
                    enabledOnly++;
                    if (_dissolveWhyScratch.Count < DissolveCensusReasonCap)
                    {
                        _dissolveWhyScratch.Add($"'{p.Renderer.name}': "
                            + (p.DissolveWhy ?? "channel not evaluated yet (adopted while the "
                                + "segment was already held faded — it gets one on the un-fade "
                                + "edge)"));
                    }
                    break;
            }
        }

        /// <summary>
        /// DISSOLVE CENSUS (round 15): one line per fading segment naming how many of its
        /// attached pieces dissolve NATIVELY (own toggle-native materials, driven by the wall
        /// ramp), how many via the SWAP, how many animate through their own alpha/particle
        /// channel — and how many are still delivered ENABLED-ONLY, each with the reason it
        /// could not be given a channel. After round 15 a healthy line reads "0 enabled-only";
        /// anything else names the popper and why, so it never has to be found by eye again.
        /// </summary>
        private void LogDissolveCensus(Segment seg)
        {
            int pieces = seg.Mounted.Count + seg.Stacked.Count + seg.Body.Count
                + seg.Siblings.Count;
            int corners = 0;
            foreach (CornerPiece cp in _cornerPieces)
            {
                if (ReferenceEquals(cp.A, seg))
                    corners++;
            }
            pieces += corners;
            if (pieces == 0)
                return; // nothing attached (yet) — wall renderers dissolve natively by design

            _dissolveWhyScratch.Clear();
            int native = 0, swapped = 0, own = 0, enabledOnly = 0;
            foreach (MountedProp p in seg.Stacked)
                TallyPiece(p, ref native, ref swapped, ref own, ref enabledOnly);
            foreach (MountedProp p in seg.Body)
                TallyPiece(p, ref native, ref swapped, ref own, ref enabledOnly);
            foreach (MountedProp p in seg.Mounted)
                TallyPiece(p, ref native, ref swapped, ref own, ref enabledOnly);
            foreach (MountedProp p in seg.SiblingProps.Values)
                TallyPiece(p, ref native, ref swapped, ref own, ref enabledOnly);
            foreach (CornerPiece cp in _cornerPieces)
            {
                if (ReferenceEquals(cp.A, seg))
                    TallyPiece(cp.Prop, ref native, ref swapped, ref own, ref enabledOnly);
            }

            float now = Time.unscaledTime;
            if (seg.DissolveCensusLogged)
            {
                // After the first line of an episode only a CHANGED popper count is news, and
                // never more often than the throttle (Apparance re-streams pieces mid-fade).
                if (enabledOnly == seg.DissolveCensusEnabledOnly || now < seg.NextDissolveCensus)
                    return;
            }
            seg.DissolveCensusLogged = true;
            seg.DissolveCensusEnabledOnly = enabledOnly;
            seg.NextDissolveCensus = now + DissolveCensusIntervalSeconds;

            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            string reasons = enabledOnly > 0
                ? " — STILL POPPING: " + string.Join("; ", _dissolveWhyScratch)
                  + (enabledOnly > _dissolveWhyScratch.Count ? "; …" : string.Empty)
                : " — nothing pops.";
            VRLog.Info(Name,
                $"DISSOLVE CENSUS {(seg.IsGateColumn ? "GATE " : string.Empty)}'{wall}': "
                + $"{native} piece(s) dissolve NATIVELY (own live wall-fade toggle, driven by "
                + $"the wall map/_Cutoff ramp), {swapped} via the material SWAP, {own} through "
                + $"their own alpha/particle channel, {enabledOnly} still ENABLED-ONLY "
                + $"({seg.Stacked.Count} stacked / {seg.Body.Count} body / {seg.Mounted.Count} "
                + $"mounted / {seg.SiblingProps.Count} sibling / {corners} corner){reasons}"
                + $" [scene totals: {_nativeTotal} native, {_swapTotal} swapped]");
            _dissolveWhyScratch.Clear();
        }
    }
}
