using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WATER FEATURE — the fountain exemption (user ruling 2026-08-09, brunnen.png):
/// "In dem zweiten Raum des Levels gibt es einen Brunnen der mit den Mauern wegfaded, aber
/// das Wasser nicht (siehe brunnen.png) — mach dafür eine Ausnahme und lass den Brunnen
/// niemals faden — er ist tief genug, dass er die Sicht nicht blockiert."
///
/// WHAT THE HARDWARE LOG SHOWS (ModBuild-18 LogOutput.log, scene 'ProcGen', resource list
/// 'PCG_StillWaters'). The fountain is not one mesh, it is a small assembly and the fade
/// system splits it in two:
/// <list type="bullet">
/// <item>the WATER surface, <c>'FR_SW_Pond_Small'</c>, appears in the WALL-PATH AUDIT under
///   <c>UNCLAIMED [ALARM — fell through every path]</c>. It carries no wall-fade material,
///   no live <c>_WallFade_On</c> toggle and no alpha channel any dissolve could drive — the
///   fade system has literally NO way to take it with anything. Its AABB starts 0.38 wu over
///   the floor plane and its top clears the 1.0 wu ground band, which is exactly why the
///   ground strip does not claim it either.</item>
/// <item>the STONEWORK/bank around it sits in the same ProceduralWall subtree and DOES carry
///   the masonry family's authored toggle (the log's TOGGLE-NATIVE MATERIAL census names
///   <c>'CV_Floor_Scatter_M'</c>, <c>'CV_Generic_Rock_M'</c>, <c>'CV_UnderWall_Rock_M'</c>,
///   <c>'TO_EXT_Floor_Clutter'</c> — floor scatter and rock materials the flat game fades
///   along with the wall they dress). So the basin fades with 'Wall 1' and the water plane
///   stays behind, hanging in mid-air. That is the screenshot.</item>
/// </list>
///
/// THE RULING IS AN EXEMPTION, NOT A SYNCHRONISATION: the fountain never fades, because it
/// is low enough not to block the view. So this file does NOT teach the water to dissolve —
/// it holds the whole water feature permanently SOLID, exactly the way the GATE COLUMN file
/// holds the arch rect solid, and for the same structural reason: a spatial protection rect
/// that every adopter consults.
///
/// WHAT THIS KEYS ON, and how far it generalises (the honest version):
/// <list type="number">
/// <item>THE ANCHOR IS THE WATER, not the stone. Detected by the game's own WATER SHADER
///   family (<c>Water_Shd</c>, <c>Water_Shd_Trans</c>, <c>Water_Shr_Low</c>,
///   <c>Water_Shr_Trans_Low</c> — all four load in <c>always_loaded_base*</c>, see
///   Player.log) OR by the authored asset-name family in
///   <see cref="FadeDriver.WaterNameTokens"/> (<c>Pond</c>, <c>Fountain</c>, <c>Waterfall</c>,
///   <c>Basin</c>, <c>Trough</c>, <c>Cistern</c>). Both are asset-level facts that are
///   identical in every scenario and every room that places the prop — NOT a coordinate and
///   not an instance name. Confidence: HIGH for the Still Waters set the report comes from
///   (its pond is literally named <c>FR_SW_Pond_Small</c>), MEDIUM for water features of
///   other sets, which is why the name list is a plain, documented array: a new set that
///   names its well <c>'..._Well_...'</c> is one token away from being covered.</item>
/// <item>THE PROTECTED SET IS SPATIAL, deliberately not hierarchical — the same lesson
///   <see cref="FadeDriver.FindDoorwayRoot"/> learned: Apparance parents these props flat
///   under big 'L :' section containers, so the basin is a SIBLING subtree of the water, not
///   an ancestor of it. The water's AABB, grown by <see cref="FadeDriver.WaterMarginXZ"/> in
///   XZ and up to water-top + <see cref="FadeDriver.WaterHeadroomWU"/>, is the rect; anything
///   CENTERED in it and not reaching above it is basin, bank or rim and stays solid.</item>
/// <item>THE HEIGHT GATE IS THE USER'S OWN REASON. "Er ist tief genug, dass er die Sicht
///   nicht blockiert" is a statement about height, so it is enforced as one: a water surface
///   whose top stands more than <see cref="FadeDriver.WaterFeatureMaxHeightWU"/> over the
///   room floor plane creates NO rect at all. A pond, a fountain basin and a trough are low
///   props and are exempt; a waterfall pouring down a keep wall is not, and keeps fading with
///   the masonry it belongs to. This is what stops the rule from quietly turning into "any
///   wall standing next to water never fades".</item>
/// </list>
///
/// THE SIBLINGS (the "one named exception that leaves three siblings broken" check): the
/// same log's mounted census shows the split is not unique to the pond MESH — the fountain's
/// own <c>'P_Waterfall_Circle_Small'</c> / <c>'Sparks (1)'</c> particle emitters sit at 0.51
/// and 0.95 wu and are rejected from the wall-mounted path as floor-supported, i.e. they too
/// stay while their stonework goes. Because the rect protects by GEOMETRY, all of them are
/// covered by the one rule: emitter, water plane, bank and basin are inside the same rect and
/// all four now stay solid together. No other prop family in this log shows the body/effect
/// split (the candle flames and torch fires that also appear UNCLAIMED are wall-mounted
/// dressing, already carried by <see cref="FadeDriver.CollectWallMountedProps"/>), so the rule
/// is deliberately not widened past water.
///
/// MULTIPLAYER: purely local. A protection rect changes which renderers a LOCAL head's
/// coverage decision may hide; it produces no decision, no wire record and no peer-visible
/// state. Every peer runs the identical geometric rule against the identical scene, so the
/// fountain is solid on every machine without anything crossing the wire.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>A persistent water-feature protection rectangle. Persistent for the same
        /// reason the arch rects are (round 13): Apparance destroys and rebirths these props
        /// every few seconds, and a protection that blinks with the prop would let one rescan
        /// adopt the basin into a wall — after which the fade owns it until the NEXT rescan
        /// drops it, i.e. a visible flash of exactly the bug this file exists to remove.</summary>
        internal struct WaterRect
        {
            public float MinX, MaxX, MinZ, MaxZ, TopY;
            public float LastSeen;
        }


        /// <summary>Unseen for this long → the water feature is really gone (scene change,
        /// room torn down), not merely mid-churn. Same value/reasoning as the arch rects.</summary>
        private const float WaterRectRetainSeconds = 10f;

        /// <summary>How far past the water surface's own XZ footprint the protection reaches —
        /// the basin wall / earth bank ring around it. 1.0 wu ≈ half a hex, the same reach
        /// <c>ArchMarginXZ</c> uses to catch a doorway's frame trims.</summary>
        private const float WaterMarginXZ = 1.0f;

        /// <summary>How far ABOVE the water surface the protection reaches — the basin rim and
        /// a fountain's spout stand a little proud of the water. Kept small on purpose: the
        /// masonry course a fountain happens to stand in front of must still fade.</summary>
        private const float WaterHeadroomWU = 1.0f;

        /// <summary>THE USER'S OWN CRITERION, as a number ("er ist tief genug, dass er die
        /// Sicht nicht blockiert"): a water surface standing higher than this over its room's
        /// floor plane is not a low prop and gets NO exemption. 3.0 wu is well above the
        /// fountain in the report (water top just over the 1.0 wu ground band) and well below
        /// this tileset's wall tops (3.3–5.9 wu in the same log), so a waterfall running down
        /// a wall face cannot claim the exemption and freeze that wall solid.</summary>
        private const float WaterFeatureMaxHeightWU = 3.0f;

        /// <summary>How much of a piece's XZ footprint must lie inside the rect for it to count
        /// as part of the water feature when its CENTRE falls outside (the long bank case).
        /// Majority, not mere intersection — a wall course clipping the rect's corner is
        /// masonry and must keep fading, exactly like the arch rect's containment rule.</summary>
        private const float WaterContainmentMin = 0.5f;

        /// <summary>Authored asset-name family of water surfaces. Extend this array to cover a
        /// new tileset's water prop — that is the ONLY change such a set should need (the
        /// shader test below already covers anything using the game's own water shaders).
        /// Matched case-insensitively as a substring of the renderer name; 'Pond' is what the
        /// Still Waters set (<c>FR_SW_Pond_Small</c>) in the 2026-08-09 report uses.</summary>
        private static readonly string[] WaterNameTokens =
            { "Pond", "Fountain", "Waterfall", "Basin", "Trough", "Cistern" };

        /// <summary>Diagnostic signature of the last logged water-feature census (change
        /// triggered — this line must never become a per-rescan spam source).</summary>
        private int _waterCensusSig = -1;

        // WHAT A WATER SURFACE IS — the anchor of a water feature: shader family first (the
        // game's own definition of water, so it needs no name list at all), authored name
        // family second.
        //
        // PERF S2 (2026-08-23) — THIS USED TO BE ONE METHOD, `IsWaterSurface(Renderer)`, and
        // that fact is no longer true: it is the two halves below. The reason is cost, not
        // taste. The whole-renderer form opened its OWN GetSharedMaterials and read its OWN
        // r.name — and the rescan called it for every one of the scene's 8630 renderers while
        // three other passes were separately opening GetSharedMaterials and reading r.name for
        // the same renderer. The rescan census (ClassifyMaterialsAndName in WallSegmentFade.cs)
        // now asks all four questions from ONE fetch and ONE name read, calling exactly these
        // two helpers for the water arm, so there is still only one copy of each test. Any
        // caller that genuinely holds only a Renderer can rebuild the old method from them in
        // three lines — see WaterTerrainVR.cs, which documents its own water test as
        // deliberately mirroring this one.

        /// <summary>Does this shader belong to the water family? 'Water_Shd',
        /// 'Water_Shd_Trans', 'Water_Shr_Low', 'Water_Shr_Trans_Low' — the four water shaders
        /// this game ships, all sharing the 'Water_Sh' stem. Memoized per Shader exactly like
        /// <c>_shaderVerdict</c> (wall fade) and <c>_shaderFoliageVerdict</c> (foliage): the
        /// answer is a property of the shader asset and never changes, while <c>sh.name</c> is
        /// an interop STRING ALLOCATION that used to be paid per material per renderer per
        /// rescan.</summary>
        private bool IsWaterShader(Shader sh)
        {
            if (!_shaderWaterVerdict.TryGetValue(sh, out bool water))
            {
                water = sh.name.IndexOf("Water_Sh", StringComparison.OrdinalIgnoreCase) >= 0;
                _shaderWaterVerdict[sh] = water;
            }
            return water;
        }

        /// <summary>Does this renderer NAME belong to the authored water family? Consulted only
        /// when the shader family already said no, exactly as before.</summary>
        private static bool IsWaterNameFamily(string n)
        {
            foreach (string token in WaterNameTokens)
            {
                if (n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Rebuild the water-feature protection rects from the rescan cycle's scene census.
        /// Runs inside Rescan AFTER the room registry is built (it needs the tile-anchored
        /// floor plane for the height gate) and BEFORE the ground strip and every adoption
        /// pass, so no pass can ever see a fountain as fadeable.
        ///
        /// <para>PERF S2: the input is <c>_factWater</c> — the census indices of the renderers
        /// that satisfied this pass's own guard (<c>!null &amp;&amp; enabled &amp;&amp;
        /// !IsModObject &amp;&amp; IsWaterSurface</c>), in snapshot order. The pass used to
        /// evaluate that guard itself over all 8630 scene renderers, which meant an interop
        /// name allocation and a shader-name allocation per renderer per rescan.</para>
        /// </summary>
        private void CollectWaterFeatures()
        {
            float now = Time.unscaledTime;
            // Prune first: rects whose water has not been seen for a while (scene torn down).
            for (int i = _live.WaterRects.Count - 1; i >= 0; i--)
            {
                if (now - _live.WaterRects[i].LastSeen > WaterRectRetainSeconds)
                    _live.WaterRects.RemoveAt(i);
            }

            // Height gate baseline: the LOWEST tile-anchored floor plane in the scene, the same
            // cheap convention the mounted pass uses. Without an anchored room there is no
            // trusted plane at all — and every wall is fail-safe solid then anyway, so leaving
            // the rects as they are costs nothing.
            float minFloorY = float.PositiveInfinity;
            for (int i = 0; i < _live.RoomFloorY.Count && i < _live.RoomFloorAnchored.Count; i++)
            {
                if (_live.RoomFloorAnchored[i] && _live.RoomFloorY[i] < minFloorY)
                    minFloorY = _live.RoomFloorY[i];
            }
            if (float.IsInfinity(minFloorY))
                return;

            int found = 0, tooTall = 0;
            var names = new System.Text.StringBuilder();
            for (int wi = 0; wi < _factWater.Count; wi++)
            {
                Renderer? any = _facts[_factWater[wi]].R;
                if (any == null || !any.enabled)
                    continue; // `enabled` is read LIVE — see the census's membership note
                // LIVE bounds, not the census snapshot: this AABB becomes a PROTECTION RECT
                // that other passes measure against, so it is authoritative geometry and is
                // read here exactly as before. The census only decided membership.
                Bounds b = any.bounds;
                if (b.max.y - minFloorY > WaterFeatureMaxHeightWU)
                {
                    // NOT a low prop — a waterfall on a wall face, a water wall. It keeps
                    // whatever behaviour it had; the exemption is for things that do not
                    // block the view (user ruling 2026-08-09).
                    tooTall++;
                    continue;
                }
                found++;
                if (names.Length < 140)
                {
                    if (names.Length > 0)
                        names.Append(", ");
                    names.Append('\'').Append(any.name).Append("' top ")
                         .Append((b.max.y - minFloorY).ToString("F1"));
                }
                UpsertWaterRect(b, now);
            }

            // Change-triggered census only (the arch-rect lesson: a per-rescan line here would
            // dwarf the hardware log).
            int sig = found * 131 + tooTall * 7 + _live.WaterRects.Count;
            if (sig == _waterCensusSig)
                return;
            _waterCensusSig = sig;
            if (found == 0 && tooTall == 0)
                return;
            VRLog.Info(Name,
                $"WATER FEATURE: {found} low water surface(s) hold their whole prop "
                + $"permanently SOLID (user ruling 2026-08-09, brunnen.png — 'lass den Brunnen "
                + $"niemals faden, er ist tief genug, dass er die Sicht nicht blockiert'): "
                + $"{names}; rect = water AABB +{WaterMarginXZ:0.0} wu XZ, up to water top "
                + $"+{WaterHeadroomWU:0.0} wu, so basin/bank/rim and the fountain's own "
                + $"emitters stay with the water instead of fading out from under it. "
                + $"{tooTall} water surface(s) stand higher than {WaterFeatureMaxHeightWU:0.0} wu "
                + $"over the floor and get NO exemption (a waterfall on a wall face DOES block "
                + $"the view). {_live.WaterRects.Count} live rect(s).");
        }

        /// <summary>Upsert one rect (merging with a rect already covering the same spot — a
        /// pond is often several water quads) and stamp it as seen.</summary>
        private void UpsertWaterRect(Bounds water, float now)
        {
            float minX = water.min.x - WaterMarginXZ;
            float maxX = water.max.x + WaterMarginXZ;
            float minZ = water.min.z - WaterMarginXZ;
            float maxZ = water.max.z + WaterMarginXZ;
            float topY = water.max.y + WaterHeadroomWU;
            for (int i = 0; i < _live.WaterRects.Count; i++)
            {
                WaterRect a = _live.WaterRects[i];
                // Same feature = overlapping rects. Merging keeps a multi-quad pond ONE
                // protection instead of a ragged set of them.
                if (a.MinX > maxX || a.MaxX < minX || a.MinZ > maxZ || a.MaxZ < minZ)
                    continue;
                a.MinX = Mathf.Min(a.MinX, minX);
                a.MaxX = Mathf.Max(a.MaxX, maxX);
                a.MinZ = Mathf.Min(a.MinZ, minZ);
                a.MaxZ = Mathf.Max(a.MaxZ, maxZ);
                a.TopY = Mathf.Max(a.TopY, topY);
                a.LastSeen = now;
                _live.WaterRects[i] = a;
                return;
            }
            _live.WaterRects.Add(new WaterRect
            {
                MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ,
                TopY = topY, LastSeen = now,
            });
        }

        /// <summary>
        /// Is this piece part of a water feature — basin, bank, rim, the water itself, or an
        /// emitter standing in it? CENTERED in the rect and not reaching above it, the same
        /// containment shape the arch protection uses (a wall-wide course merely crossing the
        /// rect is masonry, not fountain, and must keep fading).
        ///
        /// Consulted by EVERY path that can hide a renderer: the ground strip that owns a
        /// segment's own wall renderers, and the stacked / corner / fast-reclaim / mounted /
        /// sibling adopters. One rule, one place — see the file header for what it keys on.
        /// </summary>
        private bool IsWaterProtected(Bounds b)
        {
            if (_live.WaterRects.Count == 0)
                return false;
            Vector3 c = b.center;
            for (int i = 0; i < _live.WaterRects.Count; i++)
            {
                WaterRect a = _live.WaterRects[i];
                if (b.max.y > a.TopY)
                    continue; // rises out of the feature — masonry behind it, not fountain
                if (c.x >= a.MinX && c.x <= a.MaxX && c.z >= a.MinZ && c.z <= a.MaxZ)
                    return true;
                // …or MOSTLY inside it. brunnen.png shows the bank as one long mound running
                // along the pond's edge: its AABB centre can fall just outside the ring while
                // most of its footprint lies over the water. Same dual test the arch rect uses
                // (centred OR contained), so an elongated bank cannot slip through the middle.
                float ox = Mathf.Min(b.max.x, a.MaxX) - Mathf.Max(b.min.x, a.MinX);
                float oz = Mathf.Min(b.max.z, a.MaxZ) - Mathf.Max(b.min.z, a.MinZ);
                if (ox <= 0f || oz <= 0f)
                    continue;
                float area = Mathf.Max(b.size.x * b.size.z, 0.0001f);
                if (ox * oz / area >= WaterContainmentMin)
                    return true;
            }
            return false;
        }
    }
}
