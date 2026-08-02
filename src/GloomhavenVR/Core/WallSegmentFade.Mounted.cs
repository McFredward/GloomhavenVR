using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WALL-MOUNTED DRESSING (user report 2026-08-02, schwebende_items.png): "Wenn Wände
/// ausgeblendet werden, bleiben trotzdem noch die Elemente daran zurück wie die Flammen der
/// Fackeln oder Kerzen. Das schwebt dann in der Luft. Ich will dass alles ausgeblendet wird
/// was auch in der Wand hängt und sonst frei in der Luft schweben würde."
///
/// WHY THE EXISTING ATTACHMENT TYPES DID NOT COVER IT — both are structural, this dressing is
/// not:
/// <list type="bullet">
/// <item>FOLIAGE rides its wall via a SHADER family test (Amp_Basic_Foliage…). A torch sconce,
///   a candle rack or a flame billboard runs an ordinary opaque/particle shader, so no foliage
///   verdict ever matches them.</item>
/// <item>ASSET SIBLINGS ride their wall via the HIERARCHY (nearest mixed fade/non-fade ancestor,
///   <see cref="FadeDriver.FindAssetRoot"/>) and are collected for ADOPTED groups only — cache
///   walls are explicitly excluded there. The hardware log of this report shows the faded wall
///   as a CACHE wall ('Wall 3', 41 renderers, +0 foliage +0 asset-sibling), i.e. the sibling
///   pass never even looked at it, and Apparance hangs the dressing props in a different
///   subtree anyway.</item>
/// </list>
///
/// THE RULE IS THE USER'S OWN PHRASING, taken literally and geometrically: a renderer rides a
/// wall's fade when it (a) is AIRBORNE — its AABB bottom stands at least
/// <see cref="FadeDriver.MountedClearanceWU"/> above that room's tile-anchored floor plane, so
/// it cannot be resting on the floor and WOULD float once the wall goes — and (b) HUGS that
/// wall — horizontal AABB gap to the wall slab ≤ <see cref="FadeDriver.MountedLinkMaxXZ"/>,
/// vertically inside the wall's own span (plus a small cap overhang). Floor-standing braziers,
/// chests, tokens, obstacles and figures fail (a) by construction; a chandelier in the middle of
/// the room fails (b). Nearest wall wins, one owner per renderer.
///
/// LIGHTS ARE NEVER TOUCHED (user, same message: "Die Lichter selber sollen nie ausgeblendet
/// werden — also an den Lichtverhältnissen darf sich durch das Ausblenden nie etwas ändern").
/// The ONLY mutation in this whole file is <c>Renderer.enabled = false</c> on the visible mesh /
/// particle / sprite. A <see cref="Light"/> is not a Renderer: it keeps emitting, its range,
/// colour, shadows and cookie are untouched, and no GameObject is ever deactivated (which WOULD
/// take the light with it). Same for halos, lens flares and light probes. Reversal is the exact
/// inverse (<c>enabled = true</c>) — nothing else was ever written.
///
/// TIMING: props switch off at the END of the wall's ~0.35s dissolve (<see cref="FadeDriver
/// .FoliageHideFade"/>, the same threshold the asset siblings use) and come back the moment the
/// fade drops — the wall is essentially gone by then, so the flame does not vanish in front of
/// an intact wall.
///
/// MULTIPLAYER: local rendering only (renderer.enabled on locally-owned scenery), nothing on the
/// wire, peers unaffected — same contract as every other WallSegmentFade attachment.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>WALL-MOUNTED props (torch sconces, candle racks, flame billboards, banners —
        /// see the file header): airborne renderers hugging this wall, hidden with it via
        /// <c>enabled</c> only. Never contains a Light (Lights are not Renderers).</summary>
        public readonly List<Renderer> Mounted = new();
        public readonly List<Renderer> PrevMounted = new();
        /// <summary>0 = restored/untouched, 2 = hidden (no dissolve ramp — arbitrary shaders).</summary>
        public int MountedState;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>AIRBORNE bar: a prop whose AABB bottom sits at least this far (wu) above its
        /// room's floor plane cannot be standing ON the floor — it hangs, and would float once
        /// the wall is gone. Deliberately the same 1.0 wu the ground exclusion uses (≈ half a hex
        /// tile), so "ground" and "airborne" are complementary by construction.</summary>
        private const float MountedClearanceWU = GroundExclusionHeightWU;
        /// <summary>Max horizontal AABB gap (wu) between a prop and the wall slab it rides. A
        /// sconce/candle rack touches its wall (gap ≈ 0); the next parallel wall run is ≥ a hex
        /// (~1.72 wu) of clear floor away, so this cannot reach across a room.</summary>
        private const float MountedLinkMaxXZ = 0.9f;
        /// <summary>How far (wu) above the wall's own AABB top a prop may still start — cap-mounted
        /// dressing sits slightly proud of the wall top.</summary>
        private const float MountedLinkMaxAboveTopWU = 0.6f;
        /// <summary>Dressing-sized only: a prop bigger than this in ANY axis (wu) is architecture,
        /// not dressing, and is left alone (fail-open — the remnant stays visible, which beats
        /// hiding a structure).</summary>
        private const float MountedMaxSpanWU = 3.0f;
        /// <summary>Runaway guard — no wall run carries more dressing than this.</summary>
        private const int MountedMaxPerSegment = 32;
        /// <summary>Diagnostic radius (wu): an airborne renderer this close to a wall but NOT
        /// attached is logged with its rejection reason, so a leftover that still floats in a
        /// hardware screenshot is decidable from the log alone.</summary>
        private const float MountedNearMissXZ = 2.5f;
        /// <summary>Cap on the per-heartbeat census/near-miss lists (log hygiene).</summary>
        private const int MountedCensusCap = 12;

        /// <summary>Renderers owned by a mounted list THIS rescan (one owner per renderer).</summary>
        private readonly HashSet<Renderer> _mountedOwned = new();
        /// <summary>Every renderer WE currently hold hidden — the orphan guard's ledger. A prop
        /// in here whose owner segment died is re-enabled by the next rescan even if no explicit
        /// restore path fired.</summary>
        private readonly HashSet<Renderer> _mountedHidden = new();
        /// <summary>Renderers already spoken for by another attachment type (wall renderers,
        /// foliage, asset siblings) — rebuilt each rescan.</summary>
        private readonly HashSet<Renderer> _attachmentOwned = new();
        private readonly List<Renderer> _mountedScratch = new();
        private readonly List<string> _mountedCensus = new();
        private readonly List<string> _mountedRejects = new();
        private int _censusMounted;
        private int _censusMountedRejected;
        private int _lastLoggedMountedCount = -1;
        private int _lastLoggedMountedRejected = -1;

        /// <summary>Return one mounted prop to vanilla (visible). enabled-toggle only.</summary>
        private void RestoreMountedRenderer(Renderer? r)
        {
            _mountedHidden.Remove(r!); // destroyed Unity object — the reference still hashes
            if (r == null)
                return;
            if (!r.enabled)
                r.enabled = true;
        }

        /// <summary>Restore ALL of a segment's mounted props — called on every path where the
        /// segment stops owning them (unfade, segment drop, group split, toggle-off, teardown),
        /// so no torch can stay hidden without an owner.</summary>
        private void RestoreSegmentMounted(Segment seg)
        {
            if (seg.MountedState == 0)
                return;
            seg.MountedState = 0;
            foreach (Renderer m in seg.Mounted)
                RestoreMountedRenderer(m);
        }

        /// <summary>
        /// Hide the segment's mounted props exactly while the segment holds fully faded. No
        /// dissolve ramp (they run arbitrary opaque/particle shaders where a cutoff MPB means
        /// nothing), so they switch off at the END of the wall's dissolve and back on the moment
        /// the fade drops. Renderer.enabled ONLY — Lights are untouched by construction.
        /// </summary>
        private void ApplyMounted(Segment seg)
        {
            if (seg.Mounted.Count == 0)
                return;
            if (seg.Fade < FoliageHideFade)
            {
                RestoreSegmentMounted(seg);
                return;
            }
            if (seg.MountedState == 2)
                return; // already hidden — nothing per-frame to do
            foreach (Renderer m in seg.Mounted)
            {
                if (m == null || !m.enabled)
                    continue;
                m.enabled = false;
                _mountedHidden.Add(m);
            }
            seg.MountedState = 2;
        }

        /// <summary>Re-enable EVERY renderer in the hidden ledger and empty it (teardown / mod
        /// disable): after this call the mod holds nothing hidden, owner or not.</summary>
        private void RestoreAllMountedProps()
        {
            if (_mountedHidden.Count == 0)
                return;
            _mountedScratch.Clear();
            _mountedScratch.AddRange(_mountedHidden);
            foreach (Renderer r in _mountedScratch)
                RestoreMountedRenderer(r);
            _mountedScratch.Clear();
            _mountedHidden.Clear();
            foreach (Segment seg in _segments.Values)
                seg.MountedState = 0;
        }

        /// <summary>Renderer families that can be wall dressing. SkinnedMeshRenderer is excluded
        /// on purpose (characters, our own hands); Line/Trail renderers are effects, never
        /// scenery.</summary>
        private static bool IsMountableRendererType(Renderer r) =>
            r is MeshRenderer || r is ParticleSystemRenderer || r is SpriteRenderer;

        /// <summary>Horizontal (XZ) gap between two AABBs; 0 when their footprints overlap.</summary>
        private static float HorizontalGap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Re-attach, per rescan, every airborne dressing renderer hugging a wall segment (see the
        /// file header for the rule and the light guarantee). Runs LAST in <c>Rescan</c>: it needs
        /// the final segment table, their room association and their ground-stripped AABBs.
        /// Leavers and orphans are restored here, so nothing can stay hidden without an owner.
        /// </summary>
        /// <param name="sceneRenderers">The rescan's single scene sweep (shared with the wall
        /// adoption pass — one FindObjectsOfType per rescan, not two).</param>
        private void CollectWallMountedProps(Renderer[] sceneRenderers)
        {
            _mountedOwned.Clear();
            _attachmentOwned.Clear();
            _mountedCensus.Clear();
            _mountedRejects.Clear();
            _censusMounted = 0;
            _censusMountedRejected = 0;

            // Park the previous lists and record who is already spoken for.
            foreach (Segment seg in _segments.Values)
            {
                seg.PrevMounted.Clear();
                seg.PrevMounted.AddRange(seg.Mounted);
                seg.Mounted.Clear();
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null) _attachmentOwned.Add(r);
                }
                foreach (MeshRenderer f in seg.Foliage)
                {
                    if (f != null) _attachmentOwned.Add(f);
                }
                foreach (MeshRenderer s in seg.Siblings)
                {
                    if (s != null) _attachmentOwned.Add(s);
                }
            }

            // Cheap pre-filter for "airborne": the LOWEST tile-anchored floor plane in the scene.
            // Rooms without an anchor are fail-safe solid anyway (their walls never fade).
            float minFloorY = float.PositiveInfinity;
            for (int i = 0; i < _roomFloorY.Count && i < _roomFloorAnchored.Count; i++)
            {
                if (_roomFloorAnchored[i] && _roomFloorY[i] < minFloorY)
                    minFloorY = _roomFloorY[i];
            }

            if (!float.IsInfinity(minFloorY) && sceneRenderers != null)
            {
                float airborneBar = minFloorY + MountedClearanceWU;
                foreach (Renderer c in sceneRenderers)
                {
                    if (c == null || !IsMountableRendererType(c))
                        continue;
                    if (c.gameObject.layer == VRLayers.ModLayer)
                        continue; // mod-owned visual (hands, cards, panels) — never scenery
                    if (_attachmentOwned.Contains(c) || _mountedOwned.Contains(c))
                        continue;
                    if (c is MeshRenderer mr && RendererUsesWallFade(mr))
                        continue; // a wall in its own right (tracked as a segment)
                    Bounds b = c.bounds;
                    // Anything sitting essentially ON the floor is not a candidate at all and is
                    // dropped here (the cheap bulk filter). Between that and the airborne bar
                    // lies the ONE failure mode this rule can plausibly get wrong — a sconce or
                    // bracket whose mesh reaches far enough down to look floor-supported — so
                    // those still run the wall search and are LOGGED with their exact bottom
                    // height instead of disappearing silently from the diagnostics.
                    if (b.min.y < minFloorY + MountedClearanceWU * 0.25f)
                        continue;
                    bool belowBar = b.min.y < airborneBar;

                    // Nearest eligible wall wins. Doorway segments (never fade) and segments
                    // without a trusted room plane attach nothing.
                    Segment? best = null;
                    float bestGap = float.PositiveInfinity;
                    float nearestAny = float.PositiveInfinity;
                    foreach (Segment seg in _segments.Values)
                    {
                        if (!seg.HasBounds || seg.DoorRoot != null || !RoomDecisionValid(seg.RoomIndex))
                            continue;
                        float gap = HorizontalGap(seg.Bounds, b);
                        if (gap < nearestAny)
                            nearestAny = gap;
                        if (belowBar || gap > MountedLinkMaxXZ || gap >= bestGap)
                            continue;
                        if (b.min.y < _roomFloorY[seg.RoomIndex] + MountedClearanceWU)
                            continue; // airborne against THIS room's plane, not just the lowest
                        if (b.min.y > seg.Bounds.max.y + MountedLinkMaxAboveTopWU)
                            continue; // floats above the wall, not in it
                        if (b.max.y < seg.Bounds.min.y)
                            continue; // below the wall's span
                        if (seg.Mounted.Count >= MountedMaxPerSegment)
                            continue;
                        bestGap = gap;
                        best = seg;
                    }
                    if (belowBar)
                    {
                        NoteMountedReject(c, b, nearestAny,
                            $"bottom {b.min.y:F2} under the airborne bar {airborneBar:F2} — "
                            + "reads as floor-supported, so it would NOT float");
                        continue;
                    }
                    if (best == null)
                    {
                        NoteMountedReject(c, b, nearestAny, "no wall within reach / outside its span");
                        continue;
                    }
                    if (b.size.x > MountedMaxSpanWU || b.size.y > MountedMaxSpanWU
                        || b.size.z > MountedMaxSpanWU)
                    {
                        NoteMountedReject(c, b, bestGap, "too big for dressing (architecture)");
                        continue;
                    }
                    if (c.GetComponentInParent<ActorBehaviour>() != null
                        || c.GetComponentInParent<TileBehaviour>() != null
                        || c.GetComponentInParent<Canvas>() != null
                        || c.GetComponent<TMPro.TMP_Text>() != null)
                    {
                        NoteMountedReject(c, b, bestGap, "game logic / worldspace UI");
                        continue;
                    }
                    // A renderer the GAME disabled is not ours to manage — except one WE hold
                    // hidden (dropping it now would re-enable + re-hide it in a one-frame flash).
                    if (!c.enabled && !_mountedHidden.Contains(c))
                        continue;
                    best.Mounted.Add(c);
                    _mountedOwned.Add(c);
                    _censusMounted++;
                    if (_mountedCensus.Count < MountedCensusCap)
                    {
                        string wall = best.Anchor != null ? best.Anchor.name : "<dead>";
                        _mountedCensus.Add(
                            $"'{c.name}'[{RendererKind(c)}] y[{b.min.y:F1}..{b.max.y:F1}] "
                            + $"gap {bestGap:F2} → '{wall}'");
                    }
                }
            }

            // Leavers: restore anything this segment held hidden that it no longer owns.
            foreach (Segment seg in _segments.Values)
            {
                if (seg.MountedState != 0)
                {
                    foreach (Renderer prev in seg.PrevMounted)
                    {
                        if (prev != null && !seg.Mounted.Contains(prev))
                            RestoreMountedRenderer(prev);
                    }
                    if (seg.Mounted.Count == 0)
                        seg.MountedState = 0;
                }
                seg.PrevMounted.Clear();
            }

            // ORPHAN GUARD (the foliage-orphan lesson, made unconditional): anything in our hidden
            // ledger that no live segment owns any more comes back NOW — even if the segment died
            // on a path that forgot to restore. Worst case a prop stays hidden for one rescan.
            if (_mountedHidden.Count > 0)
            {
                _mountedScratch.Clear();
                _mountedScratch.AddRange(_mountedHidden);
                foreach (Renderer r in _mountedScratch)
                {
                    if (r == null || !_mountedOwned.Contains(r))
                        RestoreMountedRenderer(r);
                }
                _mountedScratch.Clear();
            }

            // Apparance streams the dressing in over several rescans, so the scenario's first
            // heartbeat would report a half-built table forever: re-log whenever the attached
            // set actually changed. Steady state prints nothing.
            if (_censusMounted != _lastLoggedMountedCount
                || _censusMountedRejected != _lastLoggedMountedRejected)
                LogMountedCensus();
        }

        /// <summary>First few mounted prop names for the fade-ON line (static — LogStateFlip is).</summary>
        private static string MountedNames(Segment seg)
        {
            if (seg.Mounted.Count == 0)
                return "none";
            var sb = new System.Text.StringBuilder();
            int listed = 0;
            foreach (Renderer m in seg.Mounted)
            {
                if (m == null)
                    continue;
                if (listed++ >= 4) { sb.Append(", …"); break; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(m.name).Append('@').Append(m.bounds.min.y.ToString("F1"));
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        private static string RendererKind(Renderer r) =>
            r is ParticleSystemRenderer ? "particles" : r is SpriteRenderer ? "sprite" : "mesh";

        private void NoteMountedReject(Renderer c, Bounds b, float gap, string why)
        {
            if (gap > MountedNearMissXZ)
                return; // not near any wall — not a leftover candidate at all
            _censusMountedRejected++;
            if (_mountedRejects.Count < MountedCensusCap)
                _mountedRejects.Add($"'{c.name}'[{RendererKind(c)}] y[{b.min.y:F1}..{b.max.y:F1}] gap {gap:F2}: {why}");
        }

        /// <summary>
        /// Heartbeat forensics for the "schwebende Items" class: WHAT rides a wall's fade and —
        /// the decisive half — which airborne renderer NEAR a wall was rejected and WHY. If a
        /// flame still floats in the next hardware screenshot, the matching NEAR-MISS entry names
        /// it, its height band, its gap to the wall and the rule that excluded it.
        /// </summary>
        private void LogMountedCensus()
        {
            _lastLoggedMountedCount = _censusMounted;
            _lastLoggedMountedRejected = _censusMountedRejected;
            if (_censusMounted == 0 && _censusMountedRejected == 0)
                return;
            string riding = _mountedCensus.Count > 0
                ? string.Join("; ", _mountedCensus)
                : "none";
            string misses = _mountedRejects.Count > 0
                ? " | NEAR-MISS (stays visible): " + string.Join("; ", _mountedRejects)
                : string.Empty;
            VRLog.Info(Name,
                $"WALL-MOUNTED DRESSING: {_censusMounted} prop(s) ride their wall's fade "
                + $"(airborne ≥{MountedClearanceWU:0.0} wu over the room floor, XZ gap "
                + $"≤{MountedLinkMaxXZ:0.00} wu, ≤{MountedMaxSpanWU:0.0} wu across; "
                + $"renderer.enabled only — Lights/halos are NEVER touched): {riding}"
                + $"{misses} ({_censusMountedRejected} near-miss total).");
        }
    }
}
