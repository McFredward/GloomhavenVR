using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// Aliasing round 3 / T3 — runtime MIP BAKE for the card faces.
///
/// PROBLEM (proved by the FACE TEXTURE DIAG hardware log line): the game ships every
/// texture the adopted card canvases sample WITHOUT mipmaps —
/// 'sactx-…-BattleOverlayCanvas' 4096x4096 mips=1 aniso=1 Bilinear, plus the small
/// per-class strips (AC_Brute_LeftDefault 376x82 mips=1, …). A mipless bilinear
/// texture under minification (a card at fan/dock distance covers far fewer pixels
/// than its texels) aliases in TEXTURE space: neither MSAA (geometry edges only) nor
/// eye-buffer supersampling at 2x can remove it — the shimmer is baked into the
/// sampling, and the user confirmed it survives 2x supersampling.
///
/// WHY the obvious fixes don't exist: Unity cannot add mip levels to an existing
/// Texture2D (mip count is fixed at construction; <c>Apply(updateMipmaps)</c> only
/// refills levels that already exist), <c>Sprite.texture</c> is read-only, and giving
/// the uGUI Images an override material with a swapped <c>_MainTex</c> would break
/// per-sprite atlas UVs.
///
/// CHOSEN mechanism (the one seam uGUI actually offers): swap the SPRITE, not the
/// texture. Each unique mipless face texture is baked ONCE — GPU blit (works on
/// non-readable/compressed atlases) → CPU readback into a NEW mipmapped RGBA32
/// Texture2D (full mip chain, trilinear, aniso 8, CPU copy released) — and every face
/// sprite gets an EQUIVALENT replacement created on the baked copy with the SAME
/// rect / pivot / pixels-per-unit / 9-slice border, assigned via the public
/// <c>Image.sprite</c> setter. Geometry, layout and atlas UVs are exactly reproduced.
/// TRIMMED rect-packed sprites — the shimmer-persists follow-up: the first cut
/// skipped them, leaving every trimmed atlas sprite sampling the mipless original —
/// are handled by a PER-SPRITE bake: the sprite's stored atlas region
/// (<c>textureRect</c>) is copied into its own small mipmapped texture at the trim
/// offset (<c>textureRectOffset</c>; margins restored as real transparent texels), so
/// the replacement is an untrimmed FullRect sprite that renders IDENTICALLY under
/// uGUI (Image draws rect + outer UV + trim padding; baking the margins in reproduces
/// that exactly with zero padding).
///
/// v4 REGION-PIXEL SOURCE (card-corruption bug, second post-mortem): v3 obtained the
/// region via <c>ReadPixels(new Rect(srcX, srcY, w, h), …)</c> — a SUB-RECT readback
/// from a blitted RenderTexture. On D3D11 (the player) the RT is stored with a
/// top-left origin; Unity's flip compensation makes a FULL-rect ReadPixels come out
/// upright (which is why the whole-atlas <see cref="Bake"/> is proven correct on
/// device — a full rect is invariant under y → H−y−h), but a PARTIAL rect's Y origin
/// lands at the flipped position, so the copy pulled rows from around
/// atlasH − srcY − h: a completely different part of the atlas — the white/garbled
/// neighbor content in card_bug_screenshot.png. Classic UNITY_UV_STARTS_AT_TOP trap,
/// invisible to GL-minded reasoning. v4 removes the second GPU path entirely: region
/// pixels are CPU ROW-SLICES (<c>Array.Copy</c>) of the SAME full-atlas readback the
/// proven-correct whole-atlas bake produces (one readback per atlas, cached). If the
/// atlas readback is upright on a platform — and the on-device card art proves it is —
/// CPU slices of it are upright by construction on every platform; there is no second
/// orientation left to get wrong.
///
/// v4 CACHING (the hardware log showed the same 4096² atlas whole-baked twice at
/// ~85 MB VRAM each — two Texture2D instances wrap the same atlas — and 'Red(Clone)'
/// region-baked 4× from the identical textureRect): whole-atlas bakes are additionally
/// deduplicated by content identity (name|size|format — the sactx atlas names carry a
/// content hash), atlas CPU readbacks are cached per atlas under a byte budget, and
/// per-sprite region textures are cached by (atlas identity, region geometry) so
/// duplicate sprites share one texture and one budget slot.
///
/// HARD SAFETY RULE (card-corruption bug, v3
/// post-mortem): a sprite is only swapped when its reconstruction is provably exact —
/// everything else keeps the original mipless sprite WITH a log line naming the
/// reason. Unswappable classes: ROTATED atlas placement (a rect sprite cannot express
/// it), TIGHT atlas packing (the polygon meshes of different sprites interleave, so
/// any rectangular region copy drags neighboring sprites' pixels into the FullRect
/// replacement; v3's mesh-UV-bounds fallback did that and is deleted),
/// and any trim region whose integer geometry does not fit its logical rect exactly
/// (no silent clamping — a cropped icon is corruption too). The game freely
/// reassigns sprites on state changes
/// and card art loads ASYNC, so CardFace re-runs <see cref="Rescan"/> on adoption and
/// on a slow (1 s) cadence while adopted; <see cref="RestoreSprites"/> puts the
/// original sprites back before a face is returned to the game (full-restore
/// contract, Patches/CardLifecyclePatches.cs).
///
/// Budget: a VRAM BYTE ceiling, <see cref="MaxBakedVramBytes"/>, shared by the
/// whole-atlas and per-sprite paths (each texture still ≤ <see cref="MaxTextureDim"/>²).
/// It used to be two COUNT caps, and that is what caused the 2026-08 "wieder starkes
/// Aliasing" regression — read <see cref="MaxBakedVramBytes"/> for the post-mortem.
/// Every bake logs name/size/mip count/filter mode/VRAM and the running budget, so the
/// hardware log carries the evidence. Failures are per-texture and permanent (no
/// retry storms); the face then simply keeps sampling the original mipless atlas —
/// never worse than before. Config gate: [Cards] FaceMipBake.
/// </summary>
internal static class CardFaceMipBake
{
    /// <summary>
    /// THE budget that actually matters: total VRAM held by every baked copy (whole-atlas AND
    /// per-sprite), because VRAM — not "how many textures" — is the scarce resource.
    ///
    /// <para>ALIASING REGRESSION, 2026-08 (user: "Die Linien und Rahmen auf allen Karten und den
    /// Gegnerinfos haben wieder starkes Aliasing — ich hatte das Gefühl das war schonmal
    /// besser."). He was right, and this constant is why. The old budgets were COUNTS —
    /// <c>MaxBakedTextures = 32</c> and <c>MaxSpriteBakes = 48</c> — sized back when the ability
    /// card faces were the only consumer of this cache. Since then ItemsPile (item cards),
    /// <c>WorldUI.PanelMipBake</c> (initiative track + tooltips), <c>Net.RemoteElementStrip</c>
    /// and <c>Net.RemoteCardArt</c> all joined the SAME pool, first-come-first-served. The
    /// hardware log shows the result exactly:</para>
    /// <list type="bullet">
    /// <item><description>slot 48/48 was consumed by main-menu / button chrome
    /// ('BtnAtlas_Shadow_34', 'Menu_Pointer', 'Highlighted_avatar_selector', 'Sarala_*',
    /// 'Trust_Icon' …) BEFORE the scenario's card art had finished loading;</description></item>
    /// <item><description>then 87 consecutive "budget exhausted" skips — including every class'
    /// FRAME art (<c>AC_Brute_Background</c>, <c>AC_Berserker_Background</c>,
    /// <c>AC_Elementalist_Background</c>, <c>AC_Summoner_Background</c>, <c>AC_Enemy</c>), every
    /// element icon (<c>ConsumeIce</c>, <c>CreateDark_Highlight</c> …) and every item card icon:
    /// literally "die Linien und Rahmen auf allen Karten";</description></item>
    /// <item><description>and 32/32 on the texture side, which left the enemy portraits
    /// 'cultist', 'living bones', 'living corpse elite' and 'city guard elite' mipless: literally
    /// "die Gegnerinfos".</description></item>
    /// </list>
    /// <para>The counts were a bad proxy because the members differ by three orders of magnitude:
    /// a 128² per-sprite bake is ~87 KB of VRAM while the 4096² BattleOverlayCanvas atlas is
    /// ~85 MB, and the old code charged them the same "one slot". The whole per-sprite pool in
    /// that log — all 48 bakes — cost under 6 MB. So the budget is now measured in BYTES: the
    /// tiny sprites that carry the card lines and frames cost essentially nothing and can never
    /// again be crowded out by menu chrome, while the genuinely expensive 4096² atlases stay
    /// bounded. 384 MB is ~3× the ~125 MB the full hardware log actually baked (one 4096² atlas
    /// + all class art + all portraits + every sprite), i.e. headroom for a second big atlas and
    /// a full four-class party, on the desktop GPU that renders PCVR.</para>
    /// </summary>
    private const long MaxBakedVramBytes = 384L * 1024 * 1024;

    /// <summary>
    /// Runaway guard only — NOT a tuning knob. The real gate is <see cref="MaxBakedVramBytes"/>;
    /// this exists so a pathological stream of unique textures cannot grow the dictionaries
    /// without bound even if every entry is 4 KB. Deliberately far above any observed content
    /// (the hardware log's whole run baked 32 textures and 48 sprites).
    /// </summary>
    private const int MaxBakedTextures = 256;

    /// <summary>Runaway guard on PER-SPRITE bakes — see <see cref="MaxBakedTextures"/>.</summary>
    private const int MaxSpriteBakes = 512;

    /// <summary>Largest side for a per-sprite baked texture (card-face sprites are ≤ ~1024).</summary>
    private const int MaxSpriteDim = 2048;

    /// <summary>Largest texture side we bake (the big sprite atlases are exactly 4096).</summary>
    private const int MaxTextureDim = 4096;

    /// <summary>Anisotropic level for the baked copies (oblique fan/dock viewing angles).</summary>
    private const int BakedAnisoLevel = 8;

    /// <summary>
    /// Byte budget for cached CPU atlas readbacks (the per-sprite slicer's pixel source;
    /// a 4096² atlas is 64 MB of RAM). Over budget the readback still happens — it is
    /// just not retained, so correctness never depends on the cache.
    /// </summary>
    private const long MaxAtlasPixelCacheBytes = 256L * 1024 * 1024;

    /// <summary>src texture instance id → baked mipmapped copy (null = failed/skipped, never retried).</summary>
    private static readonly Dictionary<int, Texture2D?> s_bakedByTexture = new(8);

    /// <summary>
    /// Content identity (name|size|format) → baked whole-atlas copy. Dedupes DISTINCT
    /// Texture2D instances that wrap the same atlas — the hardware log showed the same
    /// 4096² 'sactx-…-811e9640' whole-baked twice at ~85 MB VRAM each. The sactx names
    /// embed a content hash and the strip textures are uniquely named assets, so
    /// name|size|format identifies content in this game.
    /// </summary>
    private static readonly Dictionary<string, Texture2D?> s_bakedByIdentity = new(8);

    /// <summary>Content identity → full-atlas CPU readback (mip 0; null = readback failed, never retried).</summary>
    private static readonly Dictionary<string, Color32[]?> s_atlasPixelsByIdentity = new(4);

    /// <summary>(atlas identity | region geometry) → per-sprite region texture (null = failed, never retried).</summary>
    private static readonly Dictionary<string, Texture2D?> s_regionTextureByKey = new(32);

    /// <summary>Bytes currently held by <see cref="s_atlasPixelsByIdentity"/>.</summary>
    private static long s_atlasPixelCacheBytes;

    /// <summary>src sprite instance id → replacement sprite on the baked copy (null = not swappable).</summary>
    private static readonly Dictionary<int, Sprite?> s_replacementBySource = new(64);

    /// <summary>replacement sprite instance id → its original, for <see cref="RestoreSprites"/>.</summary>
    private static readonly Dictionary<int, Sprite> s_originalByReplacement = new(64);

    /// <summary>src sprite instance id → PUNCHED replacement (frame-erased copy; null = refused or
    /// no frame, latched). See <see cref="PunchedReplacementFor"/>.</summary>
    private static readonly Dictionary<int, Sprite?> s_punchedBySource = new(8);

    /// <summary>(atlas identity | region geometry | "punch") → punched region texture (null =
    /// refused/no-frame, cached so class-mates and peer clones share one verdict and one
    /// texture).</summary>
    private static readonly Dictionary<string, Texture2D?> s_punchedTexByKey = new(8);

    /// <summary>Punch verdict text per content key, for the cached-refusal debug line.</summary>
    private static readonly Dictionary<string, string> s_punchVerdictByKey = new(8);

    private static int s_bakeCount;
    private static int s_spriteBakeCount;
    private static bool s_swapLogged;
    private static bool s_errorLogged;

    /// <summary>VRAM currently held by every baked copy (whole-atlas + per-sprite) — the
    /// quantity <see cref="MaxBakedVramBytes"/> bounds. Charged on success only.</summary>
    private static long s_bakedVramBytes;

    /// <summary>One line the first time the VRAM budget genuinely binds, so a hardware log says
    /// "we ran out of the resource we meant to ration" instead of the old, misleading "we ran out
    /// of slots" (which is what produced the 2026-08 aliasing regression).</summary>
    private static bool s_vramBudgetLogged;

    /// <summary>
    /// VRAM an RGBA32 texture with a full mip chain occupies: w·h·4 for mip 0 plus the
    /// geometric ¼-per-level tail, i.e. ×4/3. The one costing formula for both bake paths, so
    /// a 128² sprite (~87 KB) and a 4096² atlas (~85 MB) are charged what they actually cost.
    /// </summary>
    private static long MipChainBytes(int width, int height) => (long)width * height * 4L * 4L / 3L;

    /// <summary>Budget verdict + one-time log when the VRAM ceiling is what stops a bake.</summary>
    private static bool FitsVramBudget(long cost, string what)
    {
        if (s_bakedVramBytes + cost <= MaxBakedVramBytes)
            return true;
        if (!s_vramBudgetLogged)
        {
            s_vramBudgetLogged = true;
            VRLog.Warn("Cards", $"MIP BAKE VRAM budget reached at ~{s_bakedVramBytes / (1024f * 1024f):F0} MB " +
                                $"of {MaxBakedVramBytes / (1024f * 1024f):F0} MB — '{what}' (~{cost / (1024f * 1024f):F1} MB) " +
                                "and anything after it keep the game's mipless originals. This is the REAL " +
                                "ceiling; if card lines/frames shimmer again, raise MaxBakedVramBytes, not a count.");
        }
        return false;
    }

    /// <summary>
    /// Resolved budget usage, for the callers that log WHEN a bake happened (the card
    /// faces log it on every art arrival that actually baked something). Kept as one
    /// string so every module words the ceiling identically — the 2026-08 regression was
    /// diagnosed purely from these numbers in the hardware log.
    /// </summary>
    internal static string BudgetSummary =>
        $"~{s_bakedVramBytes / (1024f * 1024f):F0} MB of {MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM " +
        $"({s_bakeCount} texture(s) + {s_spriteBakeCount} sprite region(s))" +
        (s_vramBudgetLogged ? " — CEILING REACHED, later art keeps the mipless original" : string.Empty);

    /// <summary>VRAM currently held by every baked copy — see <see cref="MaxBakedVramBytes"/>.</summary>
    internal static long BakedVramBytes => s_bakedVramBytes;

    /// <summary>
    /// Swap every mipless-atlas sprite under <paramref name="faceRoot"/> (the adopted
    /// FullAbilityCard) for its mip-baked equivalent. Idempotent and cheap once warm:
    /// already-swapped Images and known sprites resolve via dictionary hits. Guarded —
    /// a bake surprise must never break card adoption.
    /// <para>This is the BULK pass (walks the whole face). The per-frame, allocation-free
    /// equivalent that keeps an already-adopted face fresh the instant the game assigns new art
    /// lives in <see cref="CardFace.MaintainArtArrival"/> and goes through
    /// <see cref="ReplacementFor"/> directly.</para>
    /// </summary>
    internal static void Rescan(Component? faceRoot)
    {
        if (faceRoot == null)
            return;

        // CARD SILHOUETTE (2026-08-11 report: "die meshes genau die Ränder der Karten selber").
        // This is the ONE per-face pump both card kinds already run — CardFace drives it on
        // adoption and on a 1 s cadence for ability faces, ItemsPile on host, on a per-frame art
        // poll for ~2 s and on a 1 s cadence for item faces — so it is also where a face is
        // offered to the alpha-footprint capture. Doing it here means the item path needs no
        // change in a file the silhouette work does not own, and no second update loop exists to
        // fall out of step with this one. Offer is a cheap no-op once a kind is done and skips
        // its readback entirely while the face's sprite set is unchanged; a root that is neither
        // card kind (Net/RemoteCardArt passes a bare Canvas) is ignored inside it.
        //
        // DELIBERATELY AHEAD OF THE FaceMipBake GATE below: the silhouette is a separate feature
        // and must not be switched off by a texture-quality dial.
        CardFace.Offer(faceRoot);

        if (CardsConfig.FaceMipBake == null || !CardsConfig.FaceMipBake.Value)
            return;
        try
        {
            int swapped = 0;
            // includeInactive: the game toggles face sub-widgets (enhancement slots, XP
            // orbs) — swap them while hidden so re-activation shows the baked copy at once.
            Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (Image img in images)
            {
                if (img == null)
                    continue;
                Sprite? sprite = img.sprite;
                if (sprite == null)
                    continue;
                if (s_originalByReplacement.TryGetValue(sprite.GetInstanceID(), out Sprite wornOriginal))
                {
                    // Already sampling one of OUR baked copies — but if its original has since
                    // gained a PUNCHED variant (the printed frame erased, round 8), upgrade: a
                    // face must never keep the framed copy once the frameless one exists. One
                    // dictionary probe in the steady state; the punched sprite is registered in
                    // the same restore map, so RestoreSprites is unaffected.
                    // ROUND 10: a CROP sprite (CardFaceCrop's rect-cropped copy) is exempt — it
                    // is only ever valid together with the shrunken RectTransform CardFaceCrop
                    // maintains, and "upgrading" it to the full punched copy would render the
                    // full-plate art squeezed into the cropped rect.
                    if (wornOriginal != null && !IsCropSprite(sprite)
                        && s_punchedBySource.TryGetValue(wornOriginal.GetInstanceID(), out Sprite? upgraded)
                        && upgraded != null && !ReferenceEquals(upgraded, sprite))
                    {
                        img.sprite = upgraded;
                        swapped++;
                    }
                    continue;
                }
                Sprite? replacement = ReplacementFor(sprite);
                if (replacement != null)
                {
                    img.sprite = replacement;
                    swapped++;
                }
            }
            if (swapped > 0 && !s_swapLogged)
            {
                s_swapLogged = true;
                VRLog.Info("Cards", $"MIP BAKE live (T3): {swapped} card-face Image sprite(s) on the " +
                                    "first adopted card now sample mipmapped trilinear/aniso copies of " +
                                    "the game's mipless atlases — texture-space card shimmer addressed " +
                                    "at the data, not the camera.");
            }
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("Cards", $"Face mip-bake rescan failed ({ex.GetType().Name}: {ex.Message}) — " +
                                    "faces keep the original mipless sprites.");
            }
        }
    }

    /// <summary>
    /// Put the ORIGINAL sprites back on every Image under <paramref name="faceRoot"/>
    /// that currently wears one of our replacements — called by CardFace.Restore before
    /// the face returns to the game's pool (the baked copies are a VR-side presentation
    /// detail and must never leak into the restored 2D widget), and by
    /// <c>WorldUI.PanelMipBake.Restore</c> for the initiative track / tooltip canvases
    /// whose sprites were swapped through the same shared cache. Guarded like Rescan.
    /// </summary>
    internal static void RestoreSprites(Component? faceRoot)
    {
        if (faceRoot == null || s_originalByReplacement.Count == 0)
            return;
        try
        {
            Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (Image img in images)
            {
                if (img == null)
                    continue;
                Sprite? sprite = img.sprite;
                if (sprite != null
                    && s_originalByReplacement.TryGetValue(sprite.GetInstanceID(), out Sprite original)
                    && original != null)
                {
                    img.sprite = original;
                }
            }
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("Cards", $"Face mip-bake restore failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }

    /// <summary>
    /// The mip-baked replacement for <paramref name="source"/>, minted+cached on first
    /// sight; null when its texture needs nothing (already mipped) or the sprite truly
    /// cannot be reproduced (ROTATED atlas placement, TIGHT atlas packing, inexact trim
    /// geometry, budget) — skips are LOGGED once per sprite so the hardware log shows
    /// exactly which face elements stay mipless. Untrimmed rect sprites ride the shared
    /// atlas copy; trimmed rect-packed sprites get a per-sprite region bake with the
    /// trim margins restored as real texels.
    /// INTERNAL (not private) since the WorldUI mip pass: <c>WorldUI.PanelMipBake</c> swaps
    /// sprites on the initiative track and the tooltip canvas through this same entry, so a
    /// tooltip icon living on an atlas a card face already paid for reuses the cached bake
    /// (and vice versa) instead of holding a second ~85 MB copy. The caches, budgets and
    /// skip verdicts are deliberately ONE pool — content identity does not care which module
    /// samples the atlas.
    /// </summary>
    internal static Sprite? ReplacementFor(Sprite source)
    {
        int id = source.GetInstanceID();
        if (s_replacementBySource.TryGetValue(id, out Sprite? cached))
            return cached;

        Sprite? made = null;
        Texture2D? srcTex = source.texture;
        if (srcTex != null && srcTex.mipmapCount <= 1) // already-mipped textures need nothing
        {
            if (IsRotatedPacked(source))
            {
                LogSpriteSkip(source, "ROTATED atlas placement — not expressible as a rect sprite");
            }
            else if (IsTightPacked(source))
            {
                // TIGHT packing interleaves different sprites' polygon meshes: any
                // rectangular copy of this sprite's atlas area includes NEIGHBORING
                // sprites' pixels, which a FullRect replacement then renders (the
                // v3 card-corruption bug). Not reproducible as a rect sprite — skip.
                LogSpriteSkip(source, "TIGHT atlas packing — neighboring sprites share its rectangular " +
                                      "atlas area, a rect copy would render their fragments");
            }
            else if (TryExactRect(source, out Rect texRect))
            {
                // Fast path: plain unrotated, untrimmed rect — an equivalent FullRect
                // sprite on the SHARED mipmapped atlas copy (one bake serves many sprites).
                Texture2D? baked = BakedTextureFor(srcTex);
                if (baked != null)
                {
                    made = Sprite.Create(baked, texRect, NormalizedPivot(source), source.pixelsPerUnit, 0,
                        SpriteMeshType.FullRect, source.border);
                    made.name = source.name + " (VR-mip)";
                }
            }
            else
            {
                // Trimmed or tight-packed (unrotated): per-sprite bake, margins restored.
                made = TrimmedReplacementFor(source, srcTex);
            }
            if (made != null)
                s_originalByReplacement[made.GetInstanceID()] = source;
        }

        s_replacementBySource[id] = made;
        return made;
    }

    // ------------------------------------------------------- frame punch (round 8) --
    //
    // ROUND 9 NOTE, read first: everything below (the luma-BFS erosion) is now the FALLBACK.
    // The primary mechanism is the GEOMETRIC punch further down (OutlinePunchedReplacementFor),
    // driven by the card outline CardOutline derives from the bright trim contour — the 112 log
    // measured this BFS stopping at 24 px with 102 of 264 band probes still dark, because the
    // frame's dark pixels are not luma-connected to the boundary. Do not re-tune the BFS; the
    // definition of "frame" moved from per-pixel darkness to geometry.
    //
    // THE BLACK BAND IS IN THE ART'S OWN PIXELS. Two hardware runs measured it with the capture's
    // dark-border peel: ModBuild 110 "945 texel(s) … max depth 11 of 11", ModBuild 111 "1263
    // texel(s) = 1.64 % of the face … max depth 18 of 18 texels; mean luma of what went = 22". An
    // opaque, near-black, boundary-connected ring, thicker than every guessed cap. Everything the
    // seven previous rounds built either shaped the MESH (which sits BEHIND opaque art — invisible)
    // or clipped the face to an outline that INCLUDES the frame (round 5's verified stencil clip —
    // border unchanged). The only mechanism that can remove those pixels from the picture is to
    // remove them from what the face RENDERS — and the face already renders mod-owned copies: this
    // class swaps every card-face sprite onto per-sprite mip-baked replacements. So the punch is
    // pixel surgery on OUR OWN copies: alpha → 0 on the frame pixels, uGUI's default UI shader
    // alpha-blends, and behind them the mesh is clipped by the same measurement (CardFace samples
    // the PUNCHED sprites when it stamps the footprint), so nothing paints there and the board
    // shows through.
    //
    // WHAT MAY BE PUNCHED is decided by the CALLER (CardFace.FramePunch), which has the face
    // context: only a sprite whose drawn rect spans >= CardFace.MinOutlineCoverage of a card face
    // — the same "what defines the outline must span the card" gate the silhouette capture uses.
    // An icon, a button, a portrait never reaches this code. WHICH PIXELS are frame is decided
    // here, per sprite, in the sprite's own pixel space: opaque (a >= 128), near-black
    // (luma <= PunchLumaMax — the measured ring's mean luma was 22), connected to the sprite's own
    // boundary (BFS seeded only from border/transparent-adjacent texels, so a dark ornament in the
    // middle is unreachable), and the depth is LEARNED, not guessed — two guessed caps in a row
    // were too small. The erosion runs until the luma condition stops it; a hard sanity ceiling
    // (PunchDepthCeilingFraction of the short side) and an area cap trigger the all-or-nothing
    // discard, because a dark CARD is not a dark FRAME and the standing rule is to degrade to
    // today's look, never to a guessed one.
    //
    // SAFETY AUDIT (the round-4 objection, answered rather than avoided):
    //   • The pixel source is a FRESH row-slice copy of the cached whole-atlas readback
    //     (AtlasPixelsFor) — the shared atlas readback and the shared whole-atlas bake are NEVER
    //     mutated, so no other sprite slicing from them can be affected.
    //   • The punched sprite is registered in s_originalByReplacement exactly like every other
    //     replacement, so RestoreSprites hands the game back its ORIGINAL sprite on every existing
    //     restore path (CardFace.Restore, ItemsPile's pool recycle, PanelMipBake.Restore) with
    //     zero changes there.
    //   • s_replacementBySource is re-pointed at the punched copy, so every later swap — the
    //     Rescan bulk pass, CardArtWatch's arrival seam, and both of those on a PEER's clone
    //     (Net/RemoteCardArt drives the same two seams on the same shared cache) — serves the
    //     punched copy with no further coordination.
    //   • Textures are content-keyed (atlas identity + region + "punch"), so every card of a class
    //     and every peer clone share one punched texture and one budget charge.

    /// <summary>Maximum Rec.601 luminance (0..255) a punched frame texel may have — the same
    /// "der schwarze Rand" definition the capture's peel uses (its measured ring: mean luma 22).</summary>
    private const byte PunchLumaMax = 48;

    /// <summary>Sanity ceiling on the LEARNED erosion depth, as a fraction of the sprite's short
    /// side. The measured band is ~4.8 % of the card height (~8 % of the background sprite's short
    /// side); 15 % is comfortably above any frame and comfortably below hollowing a card out.
    /// Deeper than this ⇒ the whole punch is discarded (all-or-nothing), never partially applied.</summary>
    private const float PunchDepthCeilingFraction = 0.15f;

    /// <summary>Hard ceiling on what the punch may take, as a fraction of the sprite's opaque
    /// area. The measured frame (two long-edge bands plus thin sides) is ~14 %; a full ring at the
    /// depth ceiling would be ~44 % and is discarded — a dark CARD is not a dark FRAME.</summary>
    private const float PunchMaxAreaFraction = 0.30f;

    /// <summary>The ORIGINAL game sprite behind one of our baked replacements, or null when
    /// <paramref name="sprite"/> is not ours. Lets a caller holding a face Image resolve the punch
    /// key regardless of whether the swap already happened.</summary>
    internal static Sprite? OriginalOf(Sprite? sprite)
    {
        if (sprite == null)
            return null;
        return s_originalByReplacement.TryGetValue(sprite.GetInstanceID(), out Sprite orig) ? orig : null;
    }

    /// <summary>
    /// The frame-punched replacement for <paramref name="source"/> (a GAME sprite, never one of our
    /// replacements — resolve through <see cref="OriginalOf"/> first), minted and cached on first
    /// sight; null when the sprite carries no printed frame or the punch was refused (latched, one
    /// log line per content). On success <see cref="ReplacementFor"/> is re-pointed at the punched
    /// copy, so every subsequent swap on any face — local, item, peer clone — serves it.
    /// </summary>
    internal static Sprite? PunchedReplacementFor(Sprite source)
    {
        int id = source.GetInstanceID();
        if (s_punchedBySource.TryGetValue(id, out Sprite? cached))
            return cached;

        Sprite? made = null;
        try
        {
            made = MintPunched(source);
        }
        catch (System.Exception ex)
        {
            LogPunchSkip(source, $"punch failed ({ex.GetType().Name}: {ex.Message})");
        }
        if (made != null)
        {
            s_originalByReplacement[made.GetInstanceID()] = source; // full-restore contract
            s_replacementBySource[id] = made;                        // all future swaps serve the punched copy
        }
        s_punchedBySource[id] = made;
        return made;
    }

    // ----------------------------------------------- geometric frame punch (round 9) --
    //
    // ROUND 9 SUPERSEDES THE LUMA RULE WITH GEOMETRY. The ModBuild-112 log proves the BFS above
    // is structurally insufficient: its punched background still painted 102 of 264 near-black
    // band probes (the erosion stopped at 24 px because brighter features interrupt the dark
    // connectivity), and the two action-half plates (55 of 72 band probes) never reached the
    // punch at all — the ">= 70 % of the face" area gate excluded them although they are genuine
    // face LAYERS spanning the full card width at top and bottom. The region to erase is now
    // defined ONCE, geometrically, by CardOutline (the card's bright-trim contour, derived from
    // the background sprite), and applied to EVERY full-span face layer: a pixel outside the
    // outline is frame no matter how dark, how bright, or how connected. The luma-BFS punch
    // above remains only as the fallback when the outline derivation refuses (validation gates
    // in CardOutline) — i.e. the look then degrades to exactly ModBuild 112, never to a guess.
    //
    // SAFETY AUDIT (unchanged from round 8, re-verified for this path):
    //   • Pixel source is a FRESH row-slice copy (SlicePixelsFor / the loop below) of the cached
    //     whole-atlas readback — the shared readback and the shared whole-atlas bake are NEVER
    //     mutated.
    //   • The punched sprite registers in s_originalByReplacement exactly like every other
    //     replacement, so RestoreSprites hands the game its ORIGINAL sprite on every existing
    //     restore path (CardFace.Restore, ItemsPile's pool recycle, PanelMipBake.Restore).
    //   • s_replacementBySource is re-pointed at the punched copy, so the Rescan bulk pass,
    //     CardArtWatch's arrival seam and both of those on a PEER's clone (Net/RemoteCardArt)
    //     serve it with no further coordination.
    //   • Textures are content-keyed (atlas identity + region + outline identity + drawn rect),
    //     so every card of a class and every peer clone share one punched texture and one
    //     budget charge.

    /// <summary>Erased fraction above which the geometric punch refuses — if most of a "face
    /// layer" maps outside the card, the drawn-rect mapping is wrong, not the art.</summary>
    private const float OutlinePunchMaxEraseFraction = 0.9f;

    /// <summary>
    /// How a face layer's sprite pixels map into face-normalized space (round 9). The ModBuild-112
    /// sweep proves this cannot be Simple-only: the two action-half plates painted 55 of their 72
    /// band probes yet appeared in NEITHER of the sweep's counts — at ~18 % drawn coverage they can
    /// only have fallen to the <c>Image.Type.Simple</c> filter, i.e. they are the game's
    /// prefab-serialized 9-SLICED button plates (<c>FullAbilityCardAction.actionButton</c> is a
    /// <c>Button</c>; a plate Image under a Button is classically sliced). A sliced image does not
    /// map its sprite uniformly onto its rect — corners draw at native border size, edges/center
    /// stretch — so the punch must map each pixel through the same piecewise-linear function uGUI's
    /// <c>GenerateSlicedSprite</c> uses, or it would erase the wrong pixels. <see cref="MapU"/> /
    /// <see cref="MapV"/> replicate exactly that (borders divided by the multiplied
    /// pixels-per-unit, then clamp-scaled when the rect is smaller than the combined borders —
    /// built by the sweep from live values, nothing hard-coded). A uniform mapping (Simple, Filled
    /// at full fill, sliced with a zero border) degenerates to a plain lerp over the drawn rect.
    /// </summary>
    internal readonly struct PunchMapping
    {
        /// <summary>The layer's drawn rect in face-normalized space.</summary>
        internal readonly Rect FaceRect;

        /// <summary>9-slice borders in SPRITE pixels (x=left, y=bottom, z=right, w=top); zero =
        /// uniform mapping.</summary>
        internal readonly Vector4 SpriteBorderPx;

        /// <summary>The same borders as drawn, in FACE-normalized units.</summary>
        internal readonly Vector4 DestBorderFace;

        internal readonly bool Sliced;

        internal PunchMapping(Rect faceRect)
        {
            FaceRect = faceRect;
            SpriteBorderPx = Vector4.zero;
            DestBorderFace = Vector4.zero;
            Sliced = false;
        }

        internal PunchMapping(Rect faceRect, Vector4 spriteBorderPx, Vector4 destBorderFace)
        {
            FaceRect = faceRect;
            SpriteBorderPx = spriteBorderPx;
            DestBorderFace = destBorderFace;
            Sliced = spriteBorderPx != Vector4.zero;
        }

        /// <summary>Face-u of sprite pixel-center x (in sprite px) for a sprite of width w.</summary>
        internal float MapU(float sx, float w) => Sliced
            ? Piece(sx, w, SpriteBorderPx.x, SpriteBorderPx.z, FaceRect.xMin, FaceRect.width,
                    DestBorderFace.x, DestBorderFace.z)
            : FaceRect.xMin + sx / w * FaceRect.width;

        /// <summary>Face-v of sprite pixel-center y (in sprite px) for a sprite of height h.</summary>
        internal float MapV(float sy, float h) => Sliced
            ? Piece(sy, h, SpriteBorderPx.y, SpriteBorderPx.w, FaceRect.yMin, FaceRect.height,
                    DestBorderFace.y, DestBorderFace.w)
            : FaceRect.yMin + sy / h * FaceRect.height;

        /// <summary>One 9-slice axis: [0..b0] → the near dest border, [size-b1..size] → the far
        /// one, the middle stretched between. Degenerate borders fall back to the uniform lerp.</summary>
        private static float Piece(float s, float size, float b0, float b1,
                                   float f0, float fsize, float d0, float d1)
        {
            if (b0 + b1 >= size - 0.5f || d0 + d1 >= fsize || (b0 <= 0f && b1 <= 0f))
                return f0 + s / size * fsize;
            if (s <= b0)
                return f0 + (b0 > 0f ? s / b0 * d0 : 0f);
            if (s >= size - b1)
                return f0 + fsize - d1 + (b1 > 0f ? (s - (size - b1)) / b1 * d1 : 0f);
            return f0 + d0 + (s - b0) / (size - b0 - b1) * (fsize - d0 - d1);
        }

        /// <summary>Cache-key fragment — two mappings that differ produce different punches.</summary>
        internal string CacheKey => Sliced
            ? $"{R(FaceRect)}|sb{V(SpriteBorderPx)}|db{V(DestBorderFace)}"
            : R(FaceRect);

        private static string R(Rect r) => $"{r.xMin:F3},{r.yMin:F3},{r.width:F3},{r.height:F3}";

        private static string V(Vector4 v) => $"{v.x:F3},{v.y:F3},{v.z:F3},{v.w:F3}";
    }

    /// <summary>Face-normalized drawn rect each geometrically punched source was minted for. A
    /// SECOND materially different rect for the same sprite instance would need a different
    /// punched copy — it is refused with one log line instead of served a wrong one (never
    /// observed: the face layers are one Image each; this is the guard, not the expectation).</summary>
    private static readonly Dictionary<int, Rect> s_punchRectBySource = new(8);

    private static bool s_punchRectMismatchLogged;

    /// <summary>
    /// The GEOMETRICALLY punched replacement for <paramref name="source"/> (a GAME sprite —
    /// resolve through <see cref="OriginalOf"/> first): every pixel that maps outside
    /// <paramref name="outline"/> under <paramref name="mapping"/> (uniform for Simple images,
    /// piecewise 9-slice for sliced plates) is erased to alpha 0. Minted and cached on first
    /// sight; null when the sprite draws nothing outside the outline (clean — no copy needed) or
    /// the punch was refused (latched, logged with numbers). On success all future swaps on any
    /// face — local, item, peer clone — serve the punched copy, exactly like the round-8 plumbing.
    /// </summary>
    internal static Sprite? OutlinePunchedReplacementFor(Sprite source, in PunchMapping mapping,
                                                         CardOutline outline)
    {
        int id = source.GetInstanceID();
        if (s_punchedBySource.TryGetValue(id, out Sprite? cached))
        {
            if (cached != null && s_punchRectBySource.TryGetValue(id, out Rect usedRect)
                && RectsDiffer(usedRect, mapping.FaceRect) && !s_punchRectMismatchLogged)
            {
                s_punchRectMismatchLogged = true;
                VRLog.Warn("Cards", $"CARD FRAME PUNCH: sprite '{source.name}' is drawn at two different " +
                                    $"face rects ({usedRect} vs {mapping.FaceRect}) — serving the copy " +
                                    "punched for the FIRST; if a band survives on one placement only, this " +
                                    "line is the reason.");
            }
            return cached;
        }

        Sprite? made = null;
        try
        {
            made = MintOutlinePunched(source, mapping, outline);
        }
        catch (System.Exception ex)
        {
            LogPunchSkip(source, $"geometric punch failed ({ex.GetType().Name}: {ex.Message})");
        }
        if (made != null)
        {
            s_originalByReplacement[made.GetInstanceID()] = source; // full-restore contract
            s_replacementBySource[id] = made;                        // all future swaps serve the punched copy
            s_punchRectBySource[id] = mapping.FaceRect;
        }
        s_punchedBySource[id] = made;
        return made;
    }

    private static bool RectsDiffer(Rect a, Rect b) =>
        Mathf.Abs(a.xMin - b.xMin) > 0.01f || Mathf.Abs(a.yMin - b.yMin) > 0.01f
        || Mathf.Abs(a.width - b.width) > 0.01f || Mathf.Abs(a.height - b.height) > 0.01f;

    /// <summary>Build the geometrically punched sprite — see the round-9 block above.</summary>
    private static Sprite? MintOutlinePunched(Sprite source, in PunchMapping mapping, CardOutline outline)
    {
        Texture2D? atlas = source.texture;
        if (!TryGetRegionGeometry(source, out int fullW, out int fullH, out int srcX, out int srcY,
                out int w, out int h, out int dstX, out int dstY, out string? geomRefusal)
            || atlas == null)
        {
            LogPunchSkip(source, geomRefusal ?? "no texture");
            return null;
        }
        Rect drawnFaceRect = mapping.FaceRect;
        if (drawnFaceRect.width <= 0.001f || drawnFaceRect.height <= 0.001f)
        {
            LogPunchSkip(source, $"degenerate drawn rect {drawnFaceRect.width:F3}x{drawnFaceRect.height:F3}");
            return null;
        }

        string key = $"{IdentityOf(atlas)}|{srcX},{srcY},{w}x{h}|{fullW}x{fullH}|+{dstX},+{dstY}" +
                     $"|{outline.SourceKey}|{mapping.CacheKey}|opunch";
        if (s_punchedTexByKey.TryGetValue(key, out Texture2D? punchedTex))
        {
            if (punchedTex == null)
            {
                VRLog.Debug("Cards", $"CARD FRAME PUNCH (geometric): '{source.name}' shares an earlier " +
                                     $"verdict — {(s_punchVerdictByKey.TryGetValue(key, out string v) ? v : "refused")}.");
                return null;
            }
            return MakePunchedSprite(source, punchedTex, fullW, fullH);
        }

        Color32[]? atlasPixels = AtlasPixelsFor(atlas);
        if (atlasPixels == null || atlasPixels.Length != atlas.width * atlas.height)
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = "atlas readback unavailable";
            LogPunchSkip(source, "whole-atlas CPU readback failed — no pixel source to punch");
            return null;
        }
        // FRESH copy — the cached atlas readback is shared by every per-sprite bake and is never
        // mutated. Default Color32 = transparent trim margins, exactly like TrimmedReplacementFor.
        var slice = new Color32[fullW * fullH];
        for (int row = 0; row < h; row++)
        {
            System.Array.Copy(atlasPixels, (srcY + row) * atlas.width + srcX,
                slice, (dstY + row) * fullW + dstX, w);
        }

        EraseOutsideOutline(slice, fullW, fullH, mapping, outline, out long opaqueCount, out int erased);
        if (erased == 0)
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = "draws nothing outside the card outline (clean)";
            VRLog.Info("Cards", $"CARD FRAME PUNCH (geometric): '{source.name}' {fullW}x{fullH} draws " +
                                "nothing outside the card outline — clean by construction, no copy needed.");
            return null;
        }
        if (opaqueCount > 0 && erased > opaqueCount * OutlinePunchMaxEraseFraction)
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = $"implausible erase ({erased} of {opaqueCount} opaque px)";
            VRLog.Info("Cards", $"CARD FRAME PUNCH (geometric) refused: '{source.name}' {fullW}x{fullH} — " +
                                $"{(float)erased / opaqueCount:P0} of its opaque pixels map outside the " +
                                $"outline (gate {OutlinePunchMaxEraseFraction:P0}); the drawn-rect mapping " +
                                "is implausible for a face layer. This sprite keeps its unpunched copy and " +
                                "renders exactly as today.");
            return null;
        }

        long cost = MipChainBytes(fullW, fullH);
        if (s_spriteBakeCount >= MaxSpriteBakes || !FitsVramBudget(cost, source.name + " (opunch)"))
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = "bake budget exhausted";
            LogPunchSkip(source, $"bake budget exhausted (~{s_bakedVramBytes / (1024f * 1024f):F0} MB of " +
                                 $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM)");
            return null;
        }
        var tex = new Texture2D(fullW, fullH, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = source.name + " (VR-mip-punch)",
            filterMode = FilterMode.Trilinear,
            anisoLevel = BakedAnisoLevel,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(slice);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        s_spriteBakeCount++;
        s_bakedVramBytes += cost;
        s_punchedTexByKey[key] = tex;
        VRLog.Info("Cards", $"CARD FRAME PUNCH (geometric): '{source.name}' {fullW}x{fullH} — {erased} px = " +
                            $"{(float)erased / ((long)fullW * fullH):P2} of the sprite erased as OUTSIDE the " +
                            $"card outline derived from '{outline.SourceName}' (bands {outline.BandSummary}; " +
                            $"drawn rect x {drawnFaceRect.xMin:F3}..{drawnFaceRect.xMax:F3}, y " +
                            $"{drawnFaceRect.yMin:F3}..{drawnFaceRect.yMax:F3} of the face, " +
                            $"{(mapping.Sliced ? "9-slice" : "uniform")} mapping). " +
                            $"~{cost / (1024f * 1024f):F1} MB VRAM; budget {BudgetSummary}. The face renders " +
                            "this copy from its first pixel and the silhouette capture samples IT — mesh " +
                            "and face share ONE geometry.");
        return MakePunchedSprite(source, tex, fullW, fullH);
    }

    /// <summary>THE ERASE, shared by the geometric punch and the rect crop: a pixel is frame iff
    /// its face-space position is outside the outline — no luma rule, no connectivity, no depth
    /// cap. Every consumer reads the SAME <c>InsideFace</c> answer. The per-axis face positions
    /// are precomputed once (the 9-slice mapping is piecewise per axis, so u depends only on x
    /// and v only on y). Mutates <paramref name="slice"/> (alpha → 0).</summary>
    private static void EraseOutsideOutline(Color32[] slice, int fullW, int fullH,
                                            in PunchMapping mapping, CardOutline outline,
                                            out long opaqueCount, out int erased)
    {
        var us = new float[fullW];
        for (int x = 0; x < fullW; x++)
            us[x] = mapping.MapU(x + 0.5f, fullW);
        var vs = new float[fullH];
        for (int y = 0; y < fullH; y++)
            vs[y] = mapping.MapV(y + 0.5f, fullH);
        opaqueCount = 0;
        erased = 0;
        for (int y = 0; y < fullH; y++)
        {
            float v = vs[y];
            int row = y * fullW;
            for (int x = 0; x < fullW; x++)
            {
                int i = row + x;
                if (slice[i].a == 0)
                    continue;
                opaqueCount++;
                if (!outline.InsideFace(us[x], v))
                {
                    slice[i].a = 0;
                    erased++;
                }
            }
        }
    }

    // ------------------------------------------------------ geometric rect crop (round 10) --
    //
    // ROUND 10 — THE SHADER-AGNOSTIC HALF OF THE FIX. The ModBuild-113 run proved the geometric
    // punch erased the outside-outline pixels to alpha 0 (its own log line carries the count) and
    // the user still saw an IDENTICAL band even where only the punched background paints. The one
    // explanation class left is that erasing to transparent black does not change what his
    // renderer draws there: CardEffects.Awake gives every card-face Image a CUSTOM material
    // (image2.material = new Material(image2.material)) and drives _Dissolve/_Burn/_GreyOut on it
    // — a shader whose blend state cannot be read offline. If that shader outputs opaque
    // (e.g. a dissolve-clip shader whose clip term ignores texture alpha at _Dissolve = 0),
    // alpha-0 pixels whose RGB is black render... black. Identical. Which would also explain,
    // retroactively, why NINE texture-side attempts produced zero visible change while the band
    // visibly vanishes during the game's own dissolve ANIMATION (the clip path finally runs).
    //
    // So this path removes the band GEOMETRICALLY: the punched pixel slice is CROPPED to the
    // card outline's extent bounding box (CardOutline.ExtentFace — protrusions included, so no
    // card pixel is ever cut), and CardFaceCrop shrinks/repositions the layer's RectTransform so
    // its drawn rect equals exactly the face rect the kept pixels cover. Then no rasterized
    // geometry exists in the band at all and the shader's alpha semantics CANNOT matter.
    //
    // EXACTNESS. Uniform (Simple) layers: a uniform map restricted to a pixel sub-block equals
    // the uniform map of the cropped sprite over the sub-block's face rect — pixel-identical.
    // Sliced layers: the crop removes x0/y0 pixels from the near edges and shrinks the sprite
    // borders by the same amounts, so every kept corner pixel keeps its drawn position (corner
    // spans shorten by exactly the crop) and the center stretch keeps its endpoints. A crop that
    // eats past a border zeroes it — the center then stretches marginally differently, which is
    // the sane reading for pixels that were frame anyway.
    //
    // SAFETY AUDIT (extends the round-8/9 audit):
    //   • Pixel source is the same FRESH slice; shared readbacks never mutated.
    //   • The crop sprite registers in s_originalByReplacement (RestoreSprites hands the game
    //     its exact original on every existing restore path) but NOT in s_replacementBySource:
    //     a crop sprite is only valid together with the shrunken rect CardFaceCrop maintains, so
    //     no generic swap path (Rescan bulk, arrival watch, peer clone) may ever serve it. The
    //     Rescan upgrade branch additionally skips crop sprites (IsCropSprite).
    //   • Peers and item faces keep the alpha punch exactly as in ModBuild 113 — the crop rides
    //     the per-frame Maintain seam only the locally adopted ability face has.
    //   • Textures are content-keyed like every other bake; class-mates share one crop texture.

    /// <summary>Cached result of one crop bake (per content+outline+mapping identity).</summary>
    private readonly struct CropBake
    {
        internal readonly Texture2D Tex;
        internal readonly Rect TargetFaceRect;
        internal readonly Vector4 Border;
        internal readonly int W;
        internal readonly int H;

        internal CropBake(Texture2D tex, Rect targetFaceRect, Vector4 border, int w, int h)
        {
            Tex = tex;
            TargetFaceRect = targetFaceRect;
            Border = border;
            W = w;
            H = h;
        }
    }

    /// <summary>(content | outline | mapping | "crop") → crop bake (null = refused, latched).</summary>
    private static readonly Dictionary<string, CropBake?> s_croppedTexByKey = new(8);

    /// <summary>src sprite instance id → crop sprite (null = refused/clean, latched).</summary>
    private static readonly Dictionary<int, Sprite?> s_croppedBySource = new(8);

    /// <summary>src sprite instance id → the face rect its crop sprite must be drawn in.</summary>
    private static readonly Dictionary<int, Rect> s_croppedRectBySource = new(8);

    /// <summary>src sprite instance id → latched crop refusal text (for repeat callers).</summary>
    private static readonly Dictionary<int, string> s_cropRefusalBySource = new(8);

    /// <summary>Instance ids of every crop sprite ever minted — the generic swap paths use this
    /// to keep their hands off a sprite that is only valid with CardFaceCrop's shrunken rect.</summary>
    private static readonly HashSet<int> s_cropSpriteIds = new(8);

    /// <summary>Is this one of the rect-crop sprites (valid only with its cropped rect)?</summary>
    internal static bool IsCropSprite(Sprite? sprite) =>
        sprite != null && s_cropSpriteIds.Contains(sprite.GetInstanceID());

    /// <summary>
    /// The CROPPED punched replacement for <paramref name="source"/> (a GAME sprite — resolve
    /// through <see cref="OriginalOf"/> first): the punched pixel slice cut down to the outline's
    /// extent bbox, plus the exact face rect (<paramref name="targetFaceRect"/>) the caller must
    /// shrink the layer's drawn rect to. Null with a stated <paramref name="refusal"/> when the
    /// layer draws nothing outside the extent bbox (clean — keep the punched copy and today's
    /// rect) or the crop was refused (latched, logged once with numbers). Never registered as a
    /// generic replacement — see the round-10 block above.
    /// </summary>
    internal static Sprite? OutlineCroppedReplacementFor(Sprite source, in PunchMapping mapping,
                                                         CardOutline outline, out Rect targetFaceRect,
                                                         out string? refusal)
    {
        targetFaceRect = default;
        refusal = null;
        int id = source.GetInstanceID();
        if (s_croppedBySource.TryGetValue(id, out Sprite? cachedSprite))
        {
            if (cachedSprite == null)
            {
                refusal = s_cropRefusalBySource.TryGetValue(id, out string r) ? r : "refused earlier";
                return null;
            }
            targetFaceRect = s_croppedRectBySource[id];
            return cachedSprite;
        }

        Sprite? made = null;
        try
        {
            made = MintOutlineCropped(source, mapping, outline, out targetFaceRect, out refusal);
        }
        catch (System.Exception ex)
        {
            refusal = $"crop failed ({ex.GetType().Name}: {ex.Message})";
            LogPunchSkip(source, refusal);
        }
        s_croppedBySource[id] = made;
        if (made != null)
        {
            s_originalByReplacement[made.GetInstanceID()] = source; // full-restore contract
            s_cropSpriteIds.Add(made.GetInstanceID());
            s_croppedRectBySource[id] = targetFaceRect;
        }
        else
        {
            s_cropRefusalBySource[id] = refusal ?? "refused";
        }
        return made;
    }

    /// <summary>Build the cropped punched sprite — see the round-10 block above.</summary>
    private static Sprite? MintOutlineCropped(Sprite source, in PunchMapping mapping,
                                              CardOutline outline, out Rect targetFaceRect,
                                              out string? refusal)
    {
        targetFaceRect = default;
        // Content cache FIRST, on geometry alone — the pixel slice below is a multi-MB copy
        // that must only ever be paid once per content (class-mates share one crop bake).
        string? contentKey = ContentKeyOf(source, out refusal);
        if (contentKey == null)
            return null;
        string key = $"{contentKey}|{outline.SourceKey}|{mapping.CacheKey}|crop";
        if (s_croppedTexByKey.TryGetValue(key, out CropBake? knownBake))
        {
            if (knownBake == null)
            {
                refusal = s_punchVerdictByKey.TryGetValue(key, out string v) ? v : "refused earlier";
                return null;
            }
            CropBake kb = knownBake.Value;
            targetFaceRect = kb.TargetFaceRect;
            return MakeCropSprite(source, kb);
        }

        Color32[]? slice = SlicePixelsFor(source, out int fullW, out int fullH, out _, out refusal);
        if (slice == null)
            return null;

        // Keep range: pixel centers whose face position lies inside the outline's extent bbox,
        // intersected with the layer's own drawn rect. MapU/MapV are monotone per axis.
        Rect ext = outline.ExtentFace;
        int x0 = 0;
        while (x0 < fullW && mapping.MapU(x0 + 0.5f, fullW) < ext.xMin - 1e-4f)
            x0++;
        int x1 = fullW - 1;
        while (x1 >= 0 && mapping.MapU(x1 + 0.5f, fullW) > ext.xMax + 1e-4f)
            x1--;
        int y0 = 0;
        while (y0 < fullH && mapping.MapV(y0 + 0.5f, fullH) < ext.yMin - 1e-4f)
            y0++;
        int y1 = fullH - 1;
        while (y1 >= 0 && mapping.MapV(y1 + 0.5f, fullH) > ext.yMax + 1e-4f)
            y1--;
        if (x1 < x0 || y1 < y0)
        {
            refusal = "the layer's drawn rect lies entirely outside the outline extent bbox — " +
                      "not a face layer under this mapping";
            s_croppedTexByKey[key] = null;
            s_punchVerdictByKey[key] = refusal;
            LogPunchSkip(source, refusal);
            return null;
        }
        int cropW = x1 - x0 + 1;
        int cropH = y1 - y0 + 1;
        if (x0 == 0 && y0 == 0 && cropW == fullW && cropH == fullH)
        {
            refusal = "draws nothing outside the outline extent bbox (clean — punched copy and " +
                      "today's rect already suffice)";
            s_croppedTexByKey[key] = null;
            s_punchVerdictByKey[key] = refusal;
            return null;
        }
        if ((long)cropW * cropH < (long)fullW * fullH / 2)
        {
            refusal = $"implausible crop: only {cropW}x{cropH} of {fullW}x{fullH} px would remain " +
                      "(under 50 %) — the mapping is wrong for a face layer, not the art";
            s_croppedTexByKey[key] = null;
            s_punchVerdictByKey[key] = refusal;
            LogPunchSkip(source, refusal);
            return null;
        }

        long cost = MipChainBytes(cropW, cropH);
        if (s_spriteBakeCount >= MaxSpriteBakes || !FitsVramBudget(cost, source.name + " (crop)"))
        {
            refusal = $"bake budget exhausted (~{s_bakedVramBytes / (1024f * 1024f):F0} MB of " +
                      $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM)";
            s_croppedTexByKey[key] = null;
            s_punchVerdictByKey[key] = refusal;
            LogPunchSkip(source, refusal);
            return null;
        }

        // Erase outside the outline first (the crop keeps the bbox; inside it the same alpha-0
        // punch applies), then cut the kept block into its own texture.
        EraseOutsideOutline(slice, fullW, fullH, mapping, outline, out _, out int erased);
        var cropped = new Color32[cropW * cropH];
        for (int row = 0; row < cropH; row++)
            System.Array.Copy(slice, (y0 + row) * fullW + x0, cropped, row * cropW, cropW);

        // The EXACT face rect the kept pixel block's edges cover — the rect CardFaceCrop must
        // shrink the layer to. Pixel EDGES, not centers: MapU(x0) is the left edge of pixel x0.
        targetFaceRect = Rect.MinMaxRect(
            mapping.MapU(x0, fullW), mapping.MapV(y0, fullH),
            mapping.MapU(x1 + 1f, fullW), mapping.MapV(y1 + 1f, fullH));

        // Sliced layers: shrink the borders by exactly what the crop removed, so every kept
        // corner pixel keeps its drawn position. Zero borders stay zero.
        Vector4 srcBorder = source.border;
        var border = new Vector4(
            Mathf.Max(0f, srcBorder.x - x0),
            Mathf.Max(0f, srcBorder.y - y0),
            Mathf.Max(0f, srcBorder.z - (fullW - 1 - x1)),
            Mathf.Max(0f, srcBorder.w - (fullH - 1 - y1)));
        if (border.x + border.z > cropW)
            border.x = border.z = 0f;
        if (border.y + border.w > cropH)
            border.y = border.w = 0f;

        var tex = new Texture2D(cropW, cropH, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = source.name + " (VR-mip-crop)",
            filterMode = FilterMode.Trilinear,
            anisoLevel = BakedAnisoLevel,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(cropped);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        s_spriteBakeCount++;
        s_bakedVramBytes += cost;
        var bake = new CropBake(tex, targetFaceRect, border, cropW, cropH);
        s_croppedTexByKey[key] = bake;
        VRLog.Info("Cards", $"CARD FRAME CROP bake: '{source.name}' {fullW}x{fullH} → {cropW}x{cropH} px " +
                            $"(cut {x0}/{fullW - 1 - x1} px left/right, {y0}/{fullH - 1 - y1} px " +
                            $"bottom/top; {erased} px inside the kept block additionally punched to " +
                            $"alpha 0). Target drawn rect x {targetFaceRect.xMin:F3}..{targetFaceRect.xMax:F3}, " +
                            $"y {targetFaceRect.yMin:F3}..{targetFaceRect.yMax:F3} of the face " +
                            $"({(mapping.Sliced ? $"sliced, borders {srcBorder} → {border}" : "uniform")}). " +
                            $"~{cost / (1024f * 1024f):F1} MB VRAM; budget {BudgetSummary}. Once the rect " +
                            "shrinks to this target, NO rasterized geometry exists in the frame band — the " +
                            "band is gone regardless of the card shader's alpha semantics.");
        return MakeCropSprite(source, bake);
    }

    /// <summary>Sprite on a crop bake. ppu preserved (the sliced dest-border arithmetic depends
    /// on it); pivot centered — uGUI's Simple/Sliced rendering never reads the sprite pivot, and
    /// CardFaceCrop places the rect explicitly.</summary>
    private static Sprite MakeCropSprite(Sprite source, in CropBake bake)
    {
        Sprite made = Sprite.Create(bake.Tex, new Rect(0f, 0f, bake.W, bake.H),
            new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect, bake.Border);
        made.name = source.name + " (VR-mip-cropped)";
        return made;
    }

    /// <summary>
    /// The shared geometry contract of every pixel-surgery path (BFS punch, geometric punch,
    /// outline derivation): only an exact, unrotated, non-tight rect reconstruction qualifies —
    /// the same contract <see cref="TrimmedReplacementFor"/> enforces, factored out in round 9 so
    /// three callers cannot drift apart. False = a stated refusal; the caller logs it.
    /// </summary>
    private static bool TryGetRegionGeometry(Sprite source, out int fullW, out int fullH,
                                             out int srcX, out int srcY, out int w, out int h,
                                             out int dstX, out int dstY, out string? refusal)
    {
        fullW = fullH = srcX = srcY = w = h = dstX = dstY = 0;
        Texture2D? atlas = source.texture;
        if (atlas == null)
        {
            refusal = "no texture";
            return false;
        }
        if (IsRotatedPacked(source) || IsTightPacked(source))
        {
            refusal = "rotated/tight atlas packing — its region cannot be extracted as a rect";
            return false;
        }
        Rect tr;
        Vector2 off;
        try
        {
            tr = source.textureRect;
            off = source.textureRectOffset;
        }
        catch (System.Exception)
        {
            refusal = "textureRect unavailable (tight-packed mesh geometry)";
            return false;
        }
        fullW = Mathf.RoundToInt(source.rect.width);
        fullH = Mathf.RoundToInt(source.rect.height);
        srcX = Mathf.RoundToInt(tr.x);
        srcY = Mathf.RoundToInt(tr.y);
        w = Mathf.RoundToInt(tr.width);
        h = Mathf.RoundToInt(tr.height);
        dstX = Mathf.RoundToInt(off.x);
        dstY = Mathf.RoundToInt(off.y);
        if (fullW < 32 || fullH < 32 || w < 1 || h < 1)
        {
            refusal = $"degenerate/too-small geometry (rect {fullW}x{fullH}, textureRect {w}x{h}) " +
                      "— a card face layer is never this small";
            return false;
        }
        if (fullW > MaxSpriteDim || fullH > MaxSpriteDim)
        {
            refusal = $"logical rect {fullW}x{fullH} exceeds the {MaxSpriteDim} per-sprite cap";
            return false;
        }
        if (srcX < 0 || srcY < 0 || srcX + w > atlas.width || srcY + h > atlas.height
            || dstX < 0 || dstY < 0 || dstX + w > fullW || dstY + h > fullH)
        {
            refusal = $"trim region does not fit exactly (atlas region {w}x{h} at {srcX},{srcY} " +
                      $"in {atlas.width}x{atlas.height}, offset +{dstX},+{dstY} in rect {fullW}x{fullH})";
            return false;
        }
        refusal = null;
        return true;
    }

    /// <summary>
    /// The content identity of a sprite's atlas region — the key <see cref="CardOutline"/>
    /// caches its derivations (and refusals) under. Geometry only, NO pixel work: the sweep
    /// resolves this every second, and the pixels are read once per key at most. Null with a
    /// stated <paramref name="refusal"/> when the region cannot be extracted exactly.
    /// </summary>
    internal static string? ContentKeyOf(Sprite source, out string? refusal)
    {
        if (!TryGetRegionGeometry(source, out int fullW, out int fullH, out int srcX, out int srcY,
                out int rw, out int rh, out int dstX, out int dstY, out refusal))
            return null;
        Texture2D atlas = source.texture!; // non-null — TryGetRegionGeometry checked
        return $"{IdentityOf(atlas)}|{srcX},{srcY},{rw}x{rh}|{fullW}x{fullH}|+{dstX},+{dstY}";
    }

    /// <summary>
    /// A FRESH full-rect pixel slice of a GAME sprite (row 0 = sprite bottom; trim margins as
    /// real transparent texels), plus its content identity — the pixel source
    /// <see cref="CardOutline"/> derives the card's true outline from (round 9). Always a copy:
    /// the shared atlas readback is never handed out, so no caller can mutate it. Null with a
    /// stated <paramref name="refusal"/> when the sprite's region cannot be extracted exactly.
    /// </summary>
    internal static Color32[]? SlicePixelsFor(Sprite source, out int w, out int h,
                                              out string? contentKey, out string? refusal)
    {
        w = 0;
        h = 0;
        contentKey = null;
        if (!TryGetRegionGeometry(source, out int fullW, out int fullH, out int srcX, out int srcY,
                out int rw, out int rh, out int dstX, out int dstY, out refusal))
            return null;
        Texture2D atlas = source.texture!; // non-null — TryGetRegionGeometry checked
        Color32[]? atlasPixels = AtlasPixelsFor(atlas);
        if (atlasPixels == null || atlasPixels.Length != atlas.width * atlas.height)
        {
            refusal = "whole-atlas CPU readback failed — no pixel source";
            return null;
        }
        var slice = new Color32[fullW * fullH];
        for (int row = 0; row < rh; row++)
        {
            System.Array.Copy(atlasPixels, (srcY + row) * atlas.width + srcX,
                slice, (dstY + row) * fullW + dstX, rw);
        }
        w = fullW;
        h = fullH;
        contentKey = $"{IdentityOf(atlas)}|{srcX},{srcY},{rw}x{rh}|{fullW}x{fullH}|+{dstX},+{dstY}";
        refusal = null;
        return slice;
    }

    /// <summary>Build the punched sprite for <paramref name="source"/> — see the block comment
    /// above for the derivation. Only exact rect reconstructions are attempted (same contract as
    /// <see cref="TrimmedReplacementFor"/>); anything else is a logged skip and today's look.
    /// <para>ROUND 9: this luma-BFS punch is now the FALLBACK, used only when no validated
    /// <see cref="CardOutline"/> exists for the face's background art (see
    /// <see cref="OutlinePunchedReplacementFor"/> for the primary mechanism and the 112 evidence
    /// for why connectivity alone could not finish the job).</para></summary>
    private static Sprite? MintPunched(Sprite source)
    {
        Texture2D? atlas = source.texture;
        if (!TryGetRegionGeometry(source, out int fullW, out int fullH, out int srcX, out int srcY,
                out int w, out int h, out int dstX, out int dstY, out string? geomRefusal)
            || atlas == null)
        {
            LogPunchSkip(source, geomRefusal ?? "no texture");
            return null;
        }

        string key = $"{IdentityOf(atlas)}|{srcX},{srcY},{w}x{h}|{fullW}x{fullH}|+{dstX},+{dstY}|punch";
        if (s_punchedTexByKey.TryGetValue(key, out Texture2D? punchedTex))
        {
            if (punchedTex == null)
            {
                VRLog.Debug("Cards", $"CARD FRAME PUNCH: '{source.name}' shares an earlier verdict — " +
                                     $"{(s_punchVerdictByKey.TryGetValue(key, out string v) ? v : "refused")}.");
                return null;
            }
            return MakePunchedSprite(source, punchedTex, fullW, fullH);
        }

        long cost = MipChainBytes(fullW, fullH);
        if (s_spriteBakeCount >= MaxSpriteBakes || !FitsVramBudget(cost, source.name + " (punch)"))
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = "bake budget exhausted";
            LogPunchSkip(source, $"bake budget exhausted (~{s_bakedVramBytes / (1024f * 1024f):F0} MB of " +
                                 $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM)");
            return null;
        }
        Color32[]? atlasPixels = AtlasPixelsFor(atlas);
        if (atlasPixels == null || atlasPixels.Length != atlas.width * atlas.height)
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = "atlas readback unavailable";
            LogPunchSkip(source, "whole-atlas CPU readback failed — no pixel source to erode");
            return null;
        }
        // FRESH copy — the cached atlas readback is shared by every per-sprite bake and is never
        // mutated. Default Color32 = transparent trim margins, exactly like TrimmedReplacementFor.
        var slice = new Color32[fullW * fullH];
        for (int row = 0; row < h; row++)
        {
            System.Array.Copy(atlasPixels, (srcY + row) * atlas.width + srcX,
                slice, (dstY + row) * fullW + dstX, w);
        }

        string? refusal = ErodeDarkFrame(slice, fullW, fullH,
            out int peeled, out int maxDepth, out int ceiling, out int meanLuma);
        if (refusal == null && peeled == 0)
            refusal = "no opaque near-black boundary-connected band — this art carries no printed frame";
        if (refusal != null)
        {
            s_punchedTexByKey[key] = null;
            s_punchVerdictByKey[key] = refusal;
            VRLog.Info("Cards", $"CARD FRAME PUNCH refused: '{source.name}' {fullW}x{fullH} — {refusal} " +
                                $"(qualify: alpha >= 128, luma <= {PunchLumaMax}; measured depth {maxDepth}px, " +
                                $"ceiling {ceiling}px, mean luma {meanLuma}). This sprite keeps its unpunched " +
                                "copy and renders exactly as today.");
            return null;
        }

        var tex = new Texture2D(fullW, fullH, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = source.name + " (VR-mip-punch)",
            filterMode = FilterMode.Trilinear,
            anisoLevel = BakedAnisoLevel,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(slice);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        s_spriteBakeCount++;
        s_bakedVramBytes += cost;
        s_punchedTexByKey[key] = tex;
        // THE MEASURED FRAME DEPTH, on the record — the number two rounds of capped peels could
        // not report. The next reader learns the frame's true thickness from this line alone.
        VRLog.Info("Cards", $"CARD FRAME PUNCH: '{source.name}' {fullW}x{fullH} — {peeled} px = " +
                            $"{(float)peeled / ((long)fullW * fullH):P2} of the sprite erased as a printed " +
                            $"frame (opaque, luma <= {PunchLumaMax}, boundary-connected; mean luma {meanLuma}). " +
                            $"MEASURED depth {maxDepth}px = {(float)maxDepth / Mathf.Min(fullW, fullH):P1} of " +
                            $"the short side (learned, sanity ceiling {ceiling}px = " +
                            $"{PunchDepthCeilingFraction:P0}). ~{cost / (1024f * 1024f):F1} MB VRAM; budget " +
                            $"{BudgetSummary}. The face now renders this copy from its first pixel, and the " +
                            "silhouette capture samples IT — mesh and face share one measurement.");
        return MakePunchedSprite(source, tex, fullW, fullH);
    }

    /// <summary>Equivalent FullRect sprite on a punched texture (geometry identical to the
    /// unpunched replacement, so layout/pivot/border reproduce exactly).</summary>
    private static Sprite MakePunchedSprite(Sprite source, Texture2D tex, int fullW, int fullH)
    {
        Sprite made = Sprite.Create(tex, new Rect(0f, 0f, fullW, fullH), NormalizedPivot(source),
            source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
        made.name = source.name + " (VR-mip-punched)";
        return made;
    }

    /// <summary>
    /// The erosion (see the frame-punch block): BFS from the sprite's own boundary over opaque
    /// near-black texels, depth LEARNED rather than capped, all-or-nothing above the sanity
    /// ceiling or the area cap. Mutates <paramref name="px"/> (alpha → 0) ONLY on success; returns
    /// null with <paramref name="peeled"/> = 0 when nothing qualifies, a refusal reason otherwise.
    /// </summary>
    private static string? ErodeDarkFrame(Color32[] px, int w, int h,
                                          out int peeled, out int maxDepth, out int ceiling, out int meanLuma)
    {
        peeled = 0;
        maxDepth = 0;
        meanLuma = 0;
        ceiling = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(w, h) * PunchDepthCeilingFraction));

        long opaque = 0;
        for (int i = 0; i < px.Length; i++)
            if (px[i].a >= 128)
                opaque++;
        if (opaque == 0)
            return "sprite has no opaque pixels";

        bool Qualifies(int i)
        {
            Color32 c = px[i];
            if (c.a < 128)
                return false;
            int y601 = (c.r * 299 + c.g * 587 + c.b * 114) / 1000;
            return y601 <= PunchLumaMax;
        }

        var depth = new ushort[px.Length];
        var queue = new Queue<int>(1024);
        var taken = new List<int>(1024);
        long lumaSum = 0;
        void Seed(int i)
        {
            if (depth[i] != 0 || !Qualifies(i))
                return;
            depth[i] = 1;
            queue.Enqueue(i);
            taken.Add(i);
            lumaSum += (px[i].r * 299 + px[i].g * 587 + px[i].b * 114) / 1000;
        }
        // Seeds: every qualifying texel on the image border, plus every qualifying texel adjacent
        // to a transparent one — "connected to the sprite's own boundary" by construction.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (px[i].a >= 128)
                {
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                        Seed(i);
                    continue;
                }
                if (x > 0) Seed(i - 1);
                if (x < w - 1) Seed(i + 1);
                if (y > 0) Seed(i - w);
                if (y < h - 1) Seed(i + w);
            }
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int d = depth[i];
            if (d > maxDepth)
                maxDepth = d;
            int x = i % w, y = i / w;
            void Step(int n)
            {
                if (depth[n] != 0 || !Qualifies(n))
                    return;
                depth[n] = (ushort)(d + 1);
                queue.Enqueue(n);
                taken.Add(n);
                lumaSum += (px[n].r * 299 + px[n].g * 587 + px[n].b * 114) / 1000;
            }
            if (x > 0) Step(i - 1);
            if (x < w - 1) Step(i + 1);
            if (y > 0) Step(i - w);
            if (y < h - 1) Step(i + w);
        }
        if (taken.Count == 0)
            return null; // peeled stays 0 — the caller words it as "no printed frame"
        meanLuma = (int)(lumaSum / taken.Count);
        if (maxDepth > ceiling)
            return $"erosion reached depth {maxDepth}px > the {ceiling}px sanity ceiling " +
                   $"({PunchDepthCeilingFraction:P0} of the short side) — this is dark ART, not a frame";
        if (taken.Count > opaque * PunchMaxAreaFraction)
            return $"{taken.Count}px = {(float)taken.Count / opaque:P0} of the opaque area exceeds the " +
                   $"{PunchMaxAreaFraction:P0} cap — a dark CARD is not a dark FRAME";
        for (int p = 0; p < taken.Count; p++)
            px[taken[p]].a = 0;
        peeled = taken.Count;
        return null;
    }

    /// <summary>One log line per refused punch (latched by the per-sprite verdict cache).</summary>
    private static void LogPunchSkip(Sprite source, string reason)
    {
        Texture2D? tex = source.texture;
        VRLog.Info("Cards", $"CARD FRAME PUNCH skip: sprite '{source.name}' on " +
                            $"'{(tex != null ? tex.name : "?")}' keeps its unpunched copy — {reason}.");
    }

    /// <summary>Pivot in normalized rect space, as Sprite.Create wants it.</summary>
    private static Vector2 NormalizedPivot(Sprite source) => new(
        source.rect.width > 0f ? source.pivot.x / source.rect.width : 0.5f,
        source.rect.height > 0f ? source.pivot.y / source.rect.height : 0.5f);

    /// <summary>Rotated atlas placement? (Guarded — packingRotation is atlas-only API.)</summary>
    private static bool IsRotatedPacked(Sprite source)
    {
        try
        {
            return source.packed && source.packingRotation != SpritePackingRotation.None;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Tight (polygon-mesh) atlas packing? (Guarded — packingMode is atlas-only API.)
    /// Belt: the mode query; braces: even when this misses (packed lies for some
    /// atlas flavors), a tight sprite's <c>textureRect</c> throws, and both
    /// <see cref="TryExactRect"/> and <see cref="TrimmedReplacementFor"/> treat that
    /// as a skip — a tight sprite can never reach a bake.
    /// </summary>
    private static bool IsTightPacked(Sprite source)
    {
        try
        {
            return source.packed && source.packingMode == SpritePackingMode.Tight;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>Packing description for the reconstruction-parameter log line.</summary>
    private static string PackingLabel(Sprite source)
    {
        try
        {
            if (!source.packed)
                return "unpacked";
            return source.packingMode == SpritePackingMode.Tight ? "packed-tight" : "packed-rect";
        }
        catch (System.Exception)
        {
            return "packing-unknown";
        }
    }

    /// <summary>
    /// True when the sprite's atlas placement is a plain unrotated, UNTRIMMED rect —
    /// exactly reproducible on the shared atlas copy. textureRect throws for
    /// tight-packed sprites — treat that as "not exact".
    /// </summary>
    private static bool TryExactRect(Sprite source, out Rect texRect)
    {
        try
        {
            texRect = source.textureRect;
            return source.textureRectOffset == Vector2.zero
                   && texRect.width >= 1f && texRect.height >= 1f
                   && Mathf.Abs(texRect.width - source.rect.width) < 0.5f
                   && Mathf.Abs(texRect.height - source.rect.height) < 0.5f;
        }
        catch (System.Exception)
        {
            texRect = default;
            return false;
        }
    }

    /// <summary>
    /// Per-sprite bake for TRIMMED rect-packed sprites: copy the sprite's stored atlas
    /// region (<c>textureRect</c>) into its OWN small mipmapped texture at the trim
    /// offset (<c>textureRectOffset</c>) — the trimmed-away margins become real
    /// transparent texels, so the replacement is an untrimmed FullRect sprite that uGUI
    /// renders identically (Image inset-by-padding + trimmed UVs ≡ full-rect quad +
    /// margins baked in). v4: the region pixels are a CPU ROW-SLICE of the cached
    /// FULL-atlas readback (the exact operation the proven-correct whole-atlas bake
    /// performs) — v3's sub-rect <c>ReadPixels</c> read from a D3D11-flipped Y origin
    /// and copied NEIGHBORING atlas content into the icons (card_bug_screenshot.png).
    /// Region textures are cached by (atlas identity, region geometry), so duplicate
    /// sprites ('Red(Clone)' ×4 in the log) share one texture and one budget slot.
    /// ONLY exact reconstructions bake: a sprite whose region can't
    /// be derived (tight-packed → textureRect throws) or whose integer geometry does
    /// not fit its logical rect precisely is SKIPPED — never clamped, cropped or
    /// guessed (v3 corrupted card icons by guessing). Null + one log line on any skip.
    /// </summary>
    private static Sprite? TrimmedReplacementFor(Sprite source, Texture2D atlas)
    {
        Rect tr;
        Vector2 off;
        try
        {
            tr = source.textureRect;
            off = source.textureRectOffset;
        }
        catch (System.Exception)
        {
            // textureRect throws for tight-packed atlas sprites that slipped past the
            // packingMode query — same corruption class, same verdict: keep the original.
            LogSpriteSkip(source, "textureRect unavailable (tight-packed mesh geometry) — atlas region " +
                                  "cannot be extracted as a rect without dragging in neighboring sprites");
            return null;
        }

        int fullW = Mathf.RoundToInt(source.rect.width);
        int fullH = Mathf.RoundToInt(source.rect.height);
        int srcX = Mathf.RoundToInt(tr.x);
        int srcY = Mathf.RoundToInt(tr.y);
        int w = Mathf.RoundToInt(tr.width);
        int h = Mathf.RoundToInt(tr.height);
        int dstX = Mathf.RoundToInt(off.x);
        int dstY = Mathf.RoundToInt(off.y);
        if (fullW < 1 || fullH < 1 || w < 1 || h < 1)
        {
            LogSpriteSkip(source, $"degenerate geometry (rect {fullW}x{fullH}, textureRect {w}x{h})");
            return null;
        }
        if (fullW > MaxSpriteDim || fullH > MaxSpriteDim)
        {
            LogSpriteSkip(source, $"logical rect {fullW}x{fullH} exceeds the {MaxSpriteDim} per-sprite cap");
            return null;
        }
        // Exact-fit contract: the trimmed region must sit fully inside BOTH the atlas
        // and the logical rect. Anything else would need clamping = cropped/misplaced
        // content on the card — skip instead (hard rule: corrupt never, mipless ok).
        if (srcX < 0 || srcY < 0 || srcX + w > atlas.width || srcY + h > atlas.height
            || dstX < 0 || dstY < 0 || dstX + w > fullW || dstY + h > fullH)
        {
            LogSpriteSkip(source, $"trim region does not fit exactly (atlas region {w}x{h} at {srcX},{srcY} " +
                                  $"in {atlas.width}x{atlas.height}, offset +{dstX},+{dstY} in rect {fullW}x{fullH})");
            return null;
        }
        try
        {
            // Region-texture cache: identical geometry on the same atlas content shares
            // one texture (the log showed 'Red(Clone)' baked 4× from one textureRect).
            string regionKey = $"{IdentityOf(atlas)}|{srcX},{srcY},{w}x{h}|{fullW}x{fullH}|+{dstX},+{dstY}";
            if (s_regionTextureByKey.TryGetValue(regionKey, out Texture2D? regionTex))
            {
                if (regionTex == null)
                {
                    LogSpriteSkip(source, "identical region bake failed earlier (cached verdict)");
                    return null;
                }
                VRLog.Info("Cards", $"MIP BAKE sprite reuse: '{source.name}' shares the cached region " +
                                    $"texture {fullW}x{fullH} ← ({srcX},{srcY}) on '{atlas.name}'.");
            }
            else
            {
                // Budget: BYTES, not slots (see MaxBakedVramBytes — the 2026-08 regression was
                // a 48-slot count cap that menu chrome consumed before the card frames loaded).
                long spriteCost = MipChainBytes(fullW, fullH);
                if (s_spriteBakeCount >= MaxSpriteBakes
                    || !FitsVramBudget(spriteCost, source.name))
                {
                    LogSpriteSkip(source, $"bake budget exhausted (~{s_bakedVramBytes / (1024f * 1024f):F0} MB " +
                                          $"of {MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM, " +
                                          $"{s_spriteBakeCount}/{MaxSpriteBakes} sprite bakes)");
                    return null;
                }
                // Pixel SOURCE (v4): the cached CPU readback of the WHOLE atlas — obtained by
                // the same full-rect Blit+ReadPixels the proven-correct whole-atlas bake uses.
                // No sub-rect GPU readback exists anymore (v3's sub-rect ReadPixels read from
                // a D3D11-flipped Y origin = neighboring atlas content in the icons).
                Color32[]? atlasPixels = AtlasPixelsFor(atlas);
                if (atlasPixels == null || atlasPixels.Length != atlas.width * atlas.height)
                {
                    LogSpriteSkip(source, atlasPixels == null
                        ? "whole-atlas CPU readback failed"
                        : $"whole-atlas readback size mismatch ({atlasPixels.Length} px for {atlas.width}x{atlas.height})");
                    s_regionTextureByKey[regionKey] = null;
                    return null;
                }
                var slice = new Color32[fullW * fullH]; // default Color32 = transparent trim margins
                for (int row = 0; row < h; row++)
                {
                    System.Array.Copy(atlasPixels, (srcY + row) * atlas.width + srcX,
                        slice, (dstY + row) * fullW + dstX, w);
                }
                regionTex = new Texture2D(fullW, fullH, TextureFormat.RGBA32, mipChain: true, linear: false)
                {
                    name = source.name + " (VR-mip-trim)",
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = BakedAnisoLevel,
                    wrapMode = TextureWrapMode.Clamp,
                };
                regionTex.SetPixels32(slice);
                regionTex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                s_spriteBakeCount++;
                s_bakedVramBytes += spriteCost;
                s_regionTextureByKey[regionKey] = regionTex;
                // Full reconstruction parameters on the record, so a hardware log alone can
                // verify the copy is exact (rect vs textureRect, offset, pivot, packing).
                VRLog.Info("Cards", $"MIP BAKE sprite ({s_spriteBakeCount}/{MaxSpriteBakes}): '{source.name}' " +
                                    $"rect {fullW}x{fullH} ← textureRect {w}x{h} at ({srcX},{srcY}) on '{atlas.name}' " +
                                    $"{atlas.width}x{atlas.height}, trim offset +{dstX},+{dstY}, " +
                                    $"pivot ({source.pivot.x:F1},{source.pivot.y:F1})px, ppu {source.pixelsPerUnit:F1}, " +
                                    $"border ({source.border.x:F0},{source.border.y:F0},{source.border.z:F0},{source.border.w:F0}), " +
                                    $"{PackingLabel(source)} → own mipmapped texture ({regionTex.mipmapCount} mips, " +
                                    $"{regionTex.filterMode} aniso {regionTex.anisoLevel}, " +
                                    $"~{spriteCost / 1024f:F0} KB VRAM; budget ~{s_bakedVramBytes / (1024f * 1024f):F0}/" +
                                    $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB), " +
                                    "margins restored — pixels: CPU row-slice of cached whole-atlas readback " +
                                    "(v4, no sub-rect GPU readback).");
            }

            Sprite made = Sprite.Create(regionTex, new Rect(0f, 0f, fullW, fullH), NormalizedPivot(source),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
            made.name = source.name + " (VR-mip)";
            return made;
        }
        catch (System.Exception ex)
        {
            LogSpriteSkip(source, $"per-sprite bake failed ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    /// <summary>Content identity of a texture: dedupes distinct instances wrapping the same atlas.</summary>
    private static string IdentityOf(Texture2D tex) => $"{tex.name}|{tex.width}x{tex.height}|{tex.format}";

    /// <summary>
    /// Full-atlas CPU readback (mip 0, bottom-left row order — Unity texture layout),
    /// cached by content identity under <see cref="MaxAtlasPixelCacheBytes"/>. This is
    /// THE pixel source for every per-sprite region slice: obtained via the identical
    /// full-rect Blit+ReadPixels the whole-atlas <see cref="Bake"/> performs, which the
    /// on-device card art proves upright — so CPU slices of it are upright by
    /// construction on every platform. Null (cached) = readback failed, no retries.
    /// Over budget the pixels are returned UNCACHED — slower next time, never wrong.
    /// </summary>
    private static Color32[]? AtlasPixelsFor(Texture2D atlas)
    {
        string identity = IdentityOf(atlas);
        if (s_atlasPixelsByIdentity.TryGetValue(identity, out Color32[]? known))
            return known;

        Color32[]? pixels = null;
        try
        {
            pixels = ReadbackAtlasPixels(atlas);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"MIP BAKE atlas readback of '{atlas.name}' {atlas.width}x{atlas.height} " +
                                $"failed ({ex.GetType().Name}: {ex.Message}) — its trimmed sprites stay mipless.");
        }

        long bytes = (long)atlas.width * atlas.height * 4L;
        if (pixels == null)
        {
            s_atlasPixelsByIdentity[identity] = null; // cache the failure — no retry storms
        }
        else if (s_atlasPixelCacheBytes + bytes <= MaxAtlasPixelCacheBytes)
        {
            s_atlasPixelsByIdentity[identity] = pixels;
            s_atlasPixelCacheBytes += bytes;
            VRLog.Info("Cards", $"MIP BAKE atlas readback cached: '{atlas.name}' {atlas.width}x{atlas.height} " +
                                $"(~{bytes / (1024f * 1024f):F0} MB CPU, cache total " +
                                $"~{s_atlasPixelCacheBytes / (1024f * 1024f):F0} MB) — slice source for per-sprite bakes.");
        }
        else
        {
            VRLog.Info("Cards", $"MIP BAKE atlas readback of '{atlas.name}' used transiently — CPU pixel cache " +
                                $"budget reached (~{s_atlasPixelCacheBytes / (1024f * 1024f):F0} MB).");
        }
        return pixels;
    }

    /// <summary>
    /// The one readback primitive: full-rect Blit + full-rect ReadPixels — byte-for-byte
    /// the operation the proven-correct whole-atlas <see cref="Bake"/> performs. A full
    /// rect is invariant under the D3D11 top-left flip (y → H−y−h maps 0→0), which is
    /// exactly why this path renders correctly on device while v3's sub-rect read did not.
    /// </summary>
    private static Color32[] ReadbackAtlasPixels(Texture2D src)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        Texture2D? tmp = null;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            tmp = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: false, linear: false);
            tmp.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            return tmp.GetPixels32();
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (tmp != null)
                Object.Destroy(tmp);
        }
    }

    /// <summary>One log line per skipped sprite (the null cache makes each fire at most once).</summary>
    private static void LogSpriteSkip(Sprite source, string reason)
    {
        Texture2D? tex = source.texture;
        VRLog.Warn("Cards", $"MIP BAKE skip: sprite '{source.name}' on '{(tex != null ? tex.name : "?")}' " +
                            $"stays MIPLESS — {reason}.");
    }

    /// <summary>True when <paramref name="sprite"/> is one of OUR baked replacements — the
    /// rescan loops (here and in <c>WorldUI.PanelMipBake</c>) use it to recognize an Image
    /// that already samples a baked copy without a second dictionary shape.</summary>
    internal static bool IsBakedSprite(Sprite sprite) =>
        s_originalByReplacement.ContainsKey(sprite.GetInstanceID());

    /// <summary>
    /// Baked mipmapped copy of a whole texture (cached; null = skipped/failed, never retried).
    /// INTERNAL since the WorldUI mip pass: <c>WorldUI.PanelMipBake</c> feeds the initiative
    /// track's <c>RawImage</c> portraits (the game assigns a raw Texture + uvRect there, no
    /// sprite exists to swap) through this same texture-level cache, so the budget, the
    /// content-identity dedupe and the bake log lines cover every module's bakes uniformly.
    /// </summary>
    internal static Texture2D? BakedTextureFor(Texture2D? tex)
    {
        if (tex == null)
            return null;
        int id = tex.GetInstanceID();
        if (s_bakedByTexture.TryGetValue(id, out Texture2D? known))
            return known;

        // Content-identity dedupe: a SECOND Texture2D instance wrapping the same atlas
        // (the log's double ~85 MB bake of 'sactx-…-811e9640') reuses the first bake.
        string identity = IdentityOf(tex);
        if (tex.mipmapCount <= 1 && s_bakedByIdentity.TryGetValue(identity, out Texture2D? alias))
        {
            if (alias != null)
            {
                float aliasMb = tex.width * (long)tex.height * 4L * 4f / 3f / (1024f * 1024f);
                VRLog.Info("Cards", $"MIP BAKE alias reuse: another instance of '{tex.name}' " +
                                    $"{tex.width}x{tex.height} shares the existing baked copy " +
                                    $"(~{aliasMb:F0} MB VRAM saved).");
            }
            s_bakedByTexture[id] = alias;
            return alias;
        }

        Texture2D? baked = null;
        // Budget: BYTES, not slots. The old count cap of 32 was reached in the hardware log by
        // small chrome textures and left the enemy portraits ('cultist', 'living bones',
        // 'living corpse elite', 'city guard elite', 512² ≈ 1.4 MB each) mipless — the
        // "Gegnerinfos" half of the 2026-08 aliasing report. See MaxBakedVramBytes.
        long cost = MipChainBytes(tex.width, tex.height);
        bool sizeOk = tex.width <= MaxTextureDim && tex.height <= MaxTextureDim
            && tex.width >= 2 && tex.height >= 2;
        bool withinBudget = s_bakeCount < MaxBakedTextures && sizeOk
            && (tex.mipmapCount > 1 || FitsVramBudget(cost, tex.name));
        if (tex.mipmapCount <= 1 && !withinBudget)
        {
            // The first cut skipped this silently — the 8/8 cap in the hardware log meant any
            // later class' art stayed mipless with no evidence. Now it's on the record.
            VRLog.Warn("Cards", $"MIP BAKE skip: texture '{tex.name}' {tex.width}x{tex.height} stays " +
                                $"MIPLESS — {(sizeOk ? $"budget (~{s_bakedVramBytes / (1024f * 1024f):F0} MB of " +
                                    $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB VRAM, {s_bakeCount}/{MaxBakedTextures} " +
                                    $"textures; this one wants ~{cost / (1024f * 1024f):F1} MB)"
                                    : $"size outside [2, {MaxTextureDim}]")}.");
        }
        if (tex.mipmapCount <= 1 // an already-mipped texture needs nothing from us
            && withinBudget)
        {
            try
            {
                baked = Bake(tex);
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("Cards", $"MIP BAKE of '{tex.name}' ({tex.width}x{tex.height}) failed " +
                                    $"({ex.GetType().Name}: {ex.Message}) — faces keep sampling the " +
                                    "mipless original.");
            }
            if (baked != null)
            {
                s_bakeCount++;
                s_bakedVramBytes += cost;
                VRLog.Info("Cards", $"MIP BAKE ({s_bakeCount}/{MaxBakedTextures}): '{tex.name}' " +
                                    $"{tex.width}x{tex.height} mips 1 → {baked.mipmapCount} " +
                                    $"(RGBA32 {baked.filterMode} aniso {baked.anisoLevel}, " +
                                    $"~{cost / (1024f * 1024f):F1} MB VRAM; budget " +
                                    $"~{s_bakedVramBytes / (1024f * 1024f):F0}/" +
                                    $"{MaxBakedVramBytes / (1024f * 1024f):F0} MB) — " +
                                    "consumer graphics (card faces / WorldUI panels) re-created on the baked copy.");
            }
        }
        s_bakedByTexture[id] = baked;
        if (tex.mipmapCount <= 1)
            s_bakedByIdentity[identity] = baked; // null verdicts dedupe too — no retry storms per alias
        return baked;
    }

    /// <summary>
    /// One-time bake: GPU blit of the (possibly compressed, non-CPU-readable) source
    /// into a temporary RT, readback into a NEW Texture2D built WITH a mip chain, then
    /// <c>Apply(updateMipmaps: true, makeNoLongerReadable: true)</c> — generates the
    /// full chain and drops the CPU copy. sRGB round-trip preserving: the temp RT is
    /// sRGB and the new texture is created non-linear, so values match the original in
    /// both linear and gamma color-space projects.
    /// </summary>
    private static Texture2D Bake(Texture2D src)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = src.name + " (VR-mip)",
                filterMode = FilterMode.Trilinear,
                anisoLevel = BakedAnisoLevel,
                wrapMode = src.wrapMode,
            };
            tex.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            // Retain mip 0 as the per-sprite slice source while the CPU copy is still
            // resident — saves the per-sprite path a second full readback of this atlas.
            try
            {
                string identity = IdentityOf(src);
                long bytes = (long)src.width * src.height * 4L;
                if (!s_atlasPixelsByIdentity.ContainsKey(identity)
                    && s_atlasPixelCacheBytes + bytes <= MaxAtlasPixelCacheBytes)
                {
                    s_atlasPixelsByIdentity[identity] = tex.GetPixels32();
                    s_atlasPixelCacheBytes += bytes;
                }
            }
            catch (System.Exception)
            {
                // Cache miss only — AtlasPixelsFor can still read this atlas back itself.
            }
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
