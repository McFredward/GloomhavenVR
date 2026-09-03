using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// GATE QUERY — a READ-ONLY view of the arch rect this subsystem already computes, for callers
/// outside the wall fade. No state, no writes, no behaviour: a getter.
///
/// <para><b>WHY IT EXISTS.</b> User, 2026-09-03, on the health bar of a destructible door:
/// "<i>Die Tür selber ist ja in einen Torbogen eingebettet, ich will auch nicht, dass die healtbar
/// dann IN dem torbogen drin ist sondern darüber.</i>" The door leaf's own body is not the thing
/// the bar has to clear — the stone frame around it reaches considerably higher. The ModBuild 396
/// log has both numbers on one line:</para>
/// <code>
/// GATE COLUMN 'ThickDoor : (9a662a0d-…)': arch rect x[-1.1..1.1] z[-3.8..-2.2] topY 4.6
///   (from 4 door-named renderer(s) …) — seed bounds wy[-0.10..3.31]
/// </code>
/// <para>The leaf tops out at 3.31 wu and the arch at 4.6 — and that ~1.3 wu gap is exactly the
/// complaint. This subsystem measured the arch already, for its own reason (the doorway-embedding
/// split, user ruling 2026-08-07: the arch stays solid while the wall around it fades). A SECOND
/// measurement of the same geometry is two numbers that drift apart, and this project has been
/// bitten by exactly that. So the bar reads THIS one.</para>
///
/// <para><b>WHY A SEPARATE FILE.</b> Purely so the addition cannot collide with concurrent work on
/// the existing parts. It declares no static field, so <c>check-partial-order.py</c>'s hazard — a
/// static initialiser reaching across parts, whose order is the MSBuild glob's — cannot arise here.</para>
///
/// <para><b>WHAT IT READS, AND IN WHICH ORDER.</b> Two sources, most specific first:</para>
/// <list type="number">
///   <item>the GATE COLUMN segment keyed by the door's own <c>UnityGameEditorDoorProp</c>
///   component — the exact key <c>SeedGateColumns</c> files it under, so there is no matching to
///   get wrong;</item>
///   <item>failing that, the PERSISTENT arch-rect list, by XZ containment of the probe point. That
///   list outlives the segment on purpose (Apparance prop churn destroys the door prop and with it
///   the gate segment; the rects are retained for ten seconds so the arch protection can never
///   gap), so it answers during exactly the windows in which the first source cannot.</item>
/// </list>
///
/// <para><b>A MISSING ARCH AND AN ARCH OF HEIGHT ZERO MUST NOT READ THE SAME</b> — hence the
/// <c>how</c> string, which every caller is expected to print. "No gate column" is the ordinary
/// answer for a destructible prop that is not a door, and it is not a failure.</para>
///
/// <para><b>MULTIPLAYER.</b> The arch is derived from the scene geometry every peer builds from the
/// same replicated map, and this query adds nothing of its own; two modded peers read the same
/// number.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    /// <summary>
    /// The top of the arch surrounding <paramref name="doorVisualRoot"/>, in world units.
    ///
    /// <para>False when this object is not a door with a live gate column — which is the normal
    /// answer for a destructible obstacle, for a door whose seed was rejected as a floor-level
    /// sliver, and for every board on which the fade driver is not running. In each of those cases
    /// <paramref name="how"/> says which.</para>
    /// </summary>
    /// <param name="doorVisualRoot">The prop's visual root (the <c>'ThickDoor : (guid)'</c> object
    /// the door prop resolves to), or any object under it.</param>
    /// <param name="archTopY">World Y of the arch top. Untouched when the result is false.</param>
    /// <param name="how">Where the answer came from, or why there is none. Always set.</param>
    internal static bool TryGetArchTopY(GameObject? doorVisualRoot, out float archTopY, out string how)
    {
        archTopY = 0f;
        if (doorVisualRoot == null)
        {
            how = "no object to ask about";
            return false;
        }
        FadeDriver? driver = _driver;
        if (driver == null)
        {
            how = "the wall-fade driver is not installed on this board, so no arch has been measured";
            return false;
        }
        return driver.QueryArchTopY(doorVisualRoot, out archTopY, out how);
    }

    private sealed partial class FadeDriver
    {
        /// <summary>The read-only half of <see cref="WallSegmentFade.TryGetArchTopY"/>; see that
        /// method. Touches nothing: two lookups and a rectangle test.</summary>
        internal bool QueryArchTopY(GameObject doorVisualRoot, out float archTopY, out string how)
        {
            archTopY = 0f;

            // SOURCE 1 — the gate column itself, under its own key. GetComponentInParent as well as
            // the direct component, because a caller may hand us any object in the door's subtree.
            UnityGameEditorDoorProp? dp = doorVisualRoot.GetComponent<UnityGameEditorDoorProp>();
            if (dp == null)
                dp = doorVisualRoot.GetComponentInChildren<UnityGameEditorDoorProp>(includeInactive: true);
            if (dp == null)
                dp = doorVisualRoot.GetComponentInParent<UnityGameEditorDoorProp>();

            if (dp != null && _live.Segments.TryGetValue(dp, out Segment? gate)
                && gate != null && gate.IsGateColumn)
            {
                archTopY = gate.ArchTopY;
                how = $"the LIVE gate column keyed by this door's own UnityGameEditorDoorProp "
                      + $"'{dp.name}' (arch rect x[{gate.ArchMinX:F1}..{gate.ArchMaxX:F1}] "
                      + $"z[{gate.ArchMinZ:F1}..{gate.ArchMaxZ:F1}])";
                return true;
            }

            // SOURCE 2 — the persistent rect list, by XZ containment of the door's own position.
            // It survives the Apparance churn that kills the segment above, which is the only
            // reason this fallback exists; a bar must not drop into the frame for the two seconds
            // it takes a gate column to be reborn.
            Vector3 probe = doorVisualRoot.transform.position;
            bool found = false;
            float best = 0f;
            int candidates = 0;
            for (int i = 0; i < _live.ArchRects.Count; i++)
            {
                ArchRect a = _live.ArchRects[i];
                if (probe.x < a.MinX || probe.x > a.MaxX || probe.z < a.MinZ || probe.z > a.MaxZ)
                    continue;
                candidates++;
                // The HIGHEST containing rect. Two arches cannot overlap in XZ on an authored map,
                // so this is a tie-break that should never be needed; taking the higher one errs
                // toward a bar that clears too much rather than one buried in stone.
                if (!found || a.TopY > best)
                {
                    best = a.TopY;
                    found = true;
                }
            }
            if (found)
            {
                archTopY = best;
                how = $"the PERSISTENT arch-rect list ({candidates} rect(s) contain this door's XZ "
                      + $"position; the gate column itself is not live right now"
                      + (dp == null
                          ? " and this object carries no UnityGameEditorDoorProp"
                          : $", its UnityGameEditorDoorProp '{dp.name}' has no segment") + ")";
                return true;
            }

            how = dp == null
                ? "NO GATE COLUMN — this prop is not a door (no UnityGameEditorDoorProp anywhere on "
                  + "it), so there is no arch to clear and its own body decides. This is the normal "
                  + "answer for a destructible obstacle and is not a failure"
                : $"NO GATE COLUMN — '{dp.name}' is a door prop but the fade subsystem holds no arch "
                  + $"for it ({_live.ArchRects.Count} arch rect(s) on this board, none containing its "
                  + "XZ position). SeedGateColumns skips a door whose seed is a floor-level sliver, "
                  + "which is the case that reads like this";
            return false;
        }
    }
}
