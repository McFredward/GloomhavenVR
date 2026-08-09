using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9b (where a FOREIGN transparent surface belongs on the distance ladder).
// A NEW part file rather than new members in part 8/9 for the reason part 8's own header gives:
// the refactor guard tracks the partial class's member and static-initializer order, and the
// filename sort ('.9.' < '.9b.') appends this part AFTER part 9, so nothing existing moves. This
// part deliberately declares NO fields — only a const and one method — so it cannot perturb the
// static-initializer order at all.

internal static partial class CanvasConversion
{
    // ---- ranking a surface the mod does NOT own against the ladder ----------------------------
    //
    // USER REPORT (2026-08-09, verbatim): "Wenn nicht der Mixed-Reality-Modus an ist, sind die
    // noch-nicht-entdeckten Tiles durchsichtig. Das ist auch so gewollt. Allerdings ist nicht
    // alles des Boards dahinter sichtbar. Die Entscheidungssymbole, die Initiativreihenfolge und
    // mancher Text des Boards verschwindet dahinter. Wenn ich da durch sehen kann, will ich auch
    // alles dahinter durchsehen können, nicht nur manche Elemente."
    //
    // THE MECHANISM, and why it is the SAME defect part 9 was written for, one level further out.
    // The game's fog-of-war hex kit (Amp_Basic_Unseen, and the 'Simple Tile'/'EN_CR_FloorTiles…'
    // pieces that ride with it under a map tile's 'Generated Content/Preview' node) draws at
    // renderQueue 3000 with RenderType 'Overlay', ZTest LEqual — and its FIRST pass hardcodes
    // ZWrite ON (ShaderOcclusionPatcher's scan of the shipped bundles: 'Amp_Basic_Unseen | 0/0,
    // 0/1 FORWARD | 4 = LEqual | On/Off'; MixedReality.cs:403-413 already records this hazard for
    // the MR backings). So an undiscovered tile is a TRANSLUCENT surface that WRITES DEPTH, at
    // the default sortingOrder 0 — i.e. it is painted BEFORE every converted panel (>= 100) and
    // before the control board's furniture band (>= 95), and everything painted after it that
    // lies behind it fails the depth test and disappears. That is exactly the split the user
    // reports, and it splits along the SAME line part 9 found:
    //   * the board SLAB, its lip, the keycap bodies and the fan/tray card slabs are opaque or
    //     AlphaTest (queue <= 2500). They are already in the framebuffer when the tile blends
    //     over them, so they show through it — "das Board ist dahinter sichtbar";
    //   * the initiative track, the decision dock and every other converted panel are depthless
    //     uGUI at ~3000 on the ladder, and the board's own transparent text (status placard,
    //     round readout, engraved keycap labels, pile captions) is the furniture band. Both are
    //     painted AFTER the tile and are erased by its depth — "die Entscheidungssymbole, die
    //     Initiativreihenfolge und mancher Text verschwindet dahinter".
    //
    // WHY THE FIX CANNOT BE "STOP THE TILE WRITING DEPTH", which is what depth-honesty would
    // normally ask for: that ZWrite is a hardcoded pass state. The in-game probe
    // (MixedReality.cs:2129-2140, the properties _ZWrite/_ZTest/_SrcBlend/_DstBlend/_Cull) came
    // back 'hardcoded' for all five — the material exposes no _ZWrite, so neither a
    // MaterialPropertyBlock nor a material write can clear it. Clearing it would need a patched
    // shader in a bundle, and this repo's standing rules forbid both touching game data and
    // shipping a bundle for a fix that has a mod-side answer. The mod-side lever that remains is
    // DRAW ORDER — and once the order is depth-correct the depth write is harmless: a surface
    // that is painted LAST cannot erase anything, because everything behind it is already there.
    //
    // WHAT THIS METHOD ANSWERS. Given a foreign transparent surface at <paramref name="eyeDistance"/>,
    // where must it sit so that painter's order holds in BOTH directions - over everything the
    // ladder puts behind it, under everything the ladder puts in front of it?
    //   behindTop  = the highest order among ladder panels (and furniture bands) measurably
    //                FARTHER than the surface. The surface must beat all of it.
    //   frontFloor = the lowest order among panels (and bands) measurably NEARER. The surface
    //                must not reach it.
    //   result     = min(behindTop + lift, frontFloor), never below behindTop.
    // The lift clears the FOLLOWERS of the farthest-behind panel (close X +2, grab bar +4, the
    // game tooltip laid on a menu plane +10 — see PanelOrderStep's doc), which belong to that
    // panel and are just as much "behind" as it is. The clamp to frontFloor is what keeps the
    // step-16 budget honest in the one geometry where the lift would overshoot: a board whose
    // furniture band tops out at 99 with the next panel already at 100 leaves no integer room, so
    // the surface TIES with that panel — and a tie is the correct answer there, because Unity
    // resolves a sortingOrder tie by renderQueue and then by distance, i.e. by exactly the thing
    // this whole ladder is a proxy for.
    //
    // A TIE INSIDE <see cref="OrderSwapMarginMeters"/> COUNTS AS NEITHER. Two surfaces the player
    // cannot tell apart in depth must not push each other around; the margin is the same dead
    // band the ladder's own swap gate uses, so a foreign surface cannot chatter against a panel
    // it is level with.
    //
    // RETURNS <see cref="NoSurfaceBehind"/> WHEN NOTHING IS BEHIND. That is not a fallback, it is
    // the conservative half of the contract: with no panel and no furniture band behind it, the
    // surface has nothing of ours to reveal, and lifting it anyway would only buy the one
    // regression this manoeuvre can cause — a foreign transparent surface climbing over OTHER
    // order-0 art that happens to be in front of it and writes no depth of its own. Callers park
    // such a surface at its authored order, which keeps the untouched scene bit-identical to the
    // shipped build.
    //
    // <para>Reads the CURRENT frame's ladder when called from inside <see cref="TickPanelOrder"/>
    // after the orders are assigned (that is where <see cref="Core.UnseenTileOrder"/> calls it),
    // and the previous frame's for anyone calling from their own LateTick.</para>

    /// <summary>Sentinel from <see cref="OrderSeenThrough"/>: the ladder has nothing behind this
    /// surface, so the caller must leave the surface's authored order alone.</summary>
    internal const int NoSurfaceBehind = int.MinValue;

    /// <summary>
    /// The sortingOrder a FOREIGN transparent surface (one the mod does not own and cannot
    /// re-author — today: the game's undiscovered-room hex kit) must take so that it composites
    /// correctly with the panel ladder and the board furniture band: above everything measurably
    /// farther, below everything measurably nearer. See the header for the full argument.
    /// </summary>
    /// <param name="eye">Eye position (the same one <see cref="TickPanelOrder"/> measured with).</param>
    /// <param name="eyeDistance">Distance from <paramref name="eye"/> to the nearest point of the
    /// surface — the same measure <see cref="PanelEyeDistance"/> answers for a panel.</param>
    /// <param name="lift">Head-room above the farthest-behind slot for that slot's own order
    /// followers. Must stay under <see cref="PanelOrderStep"/> (same contract as every follower).</param>
    /// <returns><see cref="NoSurfaceBehind"/> when nothing on the ladder is behind the surface.</returns>
    internal static int OrderSeenThrough(Vector3 eye, float eyeDistance, int lift)
    {
        int behindTop = NoSurfaceBehind;
        int frontFloor = int.MaxValue;

        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            if (p == null || !p.IsAlive)
                continue;
            // Hidden panels stay ranked, exactly as the ladder itself keeps ranking them: both
            // hides are transient, and a panel must be composited correctly the INSTANT it comes
            // back rather than six settle frames later.
            if (p.OrderDistance > eyeDistance + OrderSwapMarginMeters)
            {
                if (p.DrawSortingOrder > behindTop)
                    behindTop = p.DrawSortingOrder;
            }
            else if (p.OrderDistance < eyeDistance - OrderSwapMarginMeters)
            {
                if (p.DrawSortingOrder < frontFloor)
                    frontFloor = p.DrawSortingOrder;
            }
        }

        // The board's non-canvas transparent furniture is ranked as a BAND, not a slot (part 9),
        // so "behind" means clear its TOP and "in front" means stay under its BASE. Whenever the
        // board carries any docked panel the panel loop above already dominates this — the docked
        // panels sit at or above the band's own rank — but a board whose panels are all gone (no
        // scenario UI up, everything closed) still has a placard and engraved labels to reveal,
        // and that case exists only here.
        for (int g = 0; g < FurnitureGroups.Count; g++)
        {
            FurnitureGroup group = FurnitureGroups[g];
            if (!group.Anchor.FurnitureOrderAlive || group.AppliedRank < 0)
                continue;
            float d = group.Anchor.FurnitureEyeDistance(eye);
            int bandBase = FurnitureBandBase(group.AppliedRank);
            if (d > eyeDistance + OrderSwapMarginMeters)
            {
                int bandTop = bandBase + FurnitureBandWidth - 1;
                if (bandTop > behindTop)
                    behindTop = bandTop;
            }
            else if (d < eyeDistance - OrderSwapMarginMeters)
            {
                if (bandBase < frontFloor)
                    frontFloor = bandBase;
            }
        }

        if (behindTop == NoSurfaceBehind)
            return NoSurfaceBehind;

        int want = behindTop + lift;
        if (want > frontFloor)
            want = frontFloor;
        if (want < behindTop)
            want = behindTop; // a nearer slot BELOW a farther one: tie with what is behind, and
                              // let Unity's own distance tie-break do the rest
        return want;
    }
}
