using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
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
    /// Arm the WorldUI panel ARRIVAL WATCH (ModBuild 192) from the two entry points every consumer
    /// of this cache passes through. It is a managed bool test once armed, and a no-op while
    /// [WorldUI] PanelMipBake is off.
    ///
    /// <para>WHY IT IS TRIGGERED FROM HERE, in a Cards file, for a WorldUI mechanism — this is a
    /// deliberate trade and not an accident. The user's report is the merchant's item card
    /// ("werden 1 Sekunde mit dem starken aliasing angezeigt dann sieht man wie es verschwindet"),
    /// which lives on a FLOATED window in the map room. Read from source: every caller of
    /// <c>WorldUI.PanelMipBake.Rescan</c> is a SCENARIO surface (initiative track, stat panels,
    /// prop-info, enemy reveal, the scenario tooltip canvas), so in the map room nothing in that
    /// file is ever executed and a watch installed from there would never start. The ONE piece of
    /// mod code that provably runs against the shop window is <c>PanelSamplingProbe</c>, and it
    /// reaches the mod only through THESE TWO methods — the ModBuild 191 hardware log proves it,
    /// with the shop's 'Filter_*', 'second skin' and 'Iron_Helmet' bakes all logged by this class
    /// while the window was floated. Arming here is therefore the only seam available without
    /// editing a file this lane does not own. Cards → WorldUI is an existing direction in this
    /// codebase (twenty-odd Cards files already use WorldUI types), so no layering is inverted.</para>
    ///
    /// <para>RESOLVED AT INTEGRATION (ModBuild 192): the natural home was taken. The arrival tick
    /// is now a step in <c>WorldUIModule</c>'s LateTick list ("PanelMipBake.Arrivals"), which is the
    /// per-frame owner this class was standing in for, so the two arming calls that used to sit in
    /// <see cref="ReplacementFor"/> and <see cref="BakedTextureFor"/> are gone and this class is
    /// back to doing one thing. <c>PanelMipBake</c> still self-installs its own pump for the
    /// scenario path, and <c>TickArrivals</c> guards on the frame number, so the two owners can
    /// never double-swap.</para>
    /// </summary>

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

        // ITEM CARD BURN FOOTPRINT (2026-08 report: "Den glühenden Effekt auf den verbrannten
        // Gegenstandskarten ist nur ganz leicht am Rand sichtbar"). Same seam, same reason as the
        // silhouette offer above: this is the one pump the hosted ITEM card already runs, so the
        // fix needs no edit in a file it does not own. A no-op for every root that is not a hosted
        // item card, and a float compare once the card is seated — see CardFxBounds' class doc.
        // Also ahead of the FaceMipBake gate: it is a separate feature and must not be switched off
        // by a texture-quality dial.
        CardFxBounds.Reseat(faceRoot);

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
                if (s_originalByReplacement.ContainsKey(sprite.GetInstanceID()))
                    continue; // already sampling one of OUR baked copies
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

    /// <summary>Read-only original asset identity for owner-rendered public element sprite banks.</summary>
    internal static Sprite OriginalFor(Sprite sprite) =>
        s_originalByReplacement.TryGetValue(sprite.GetInstanceID(), out Sprite original) ? original : sprite;

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
