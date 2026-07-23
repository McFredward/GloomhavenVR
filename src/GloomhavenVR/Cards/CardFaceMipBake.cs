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
/// (<c>textureRect</c>) is blitted into its own small mipmapped texture at the trim
/// offset (<c>textureRectOffset</c>; margins restored as real transparent texels), so
/// the replacement is an untrimmed FullRect sprite that renders IDENTICALLY under
/// uGUI (Image draws rect + outer UV + trim padding; baking the margins in reproduces
/// that exactly with zero padding). HARD SAFETY RULE (card-corruption bug, v3
/// post-mortem): a sprite is only swapped when its reconstruction is provably exact —
/// everything else keeps the original mipless sprite WITH a log line naming the
/// reason. Unswappable classes: ROTATED atlas placement (a rect sprite cannot express
/// it), TIGHT atlas packing (the polygon meshes of different sprites interleave, so
/// any rectangular region copy drags neighboring sprites' pixels into the FullRect
/// replacement — this is exactly what corrupted the icons in
/// card_bug_screenshot.png; v3's mesh-UV-bounds fallback did that and is deleted),
/// and any trim region whose integer geometry does not fit its logical rect exactly
/// (no silent clamping — a cropped icon is corruption too). The game freely
/// reassigns sprites on state changes
/// and card art loads ASYNC, so CardFace re-runs <see cref="Rescan"/> on adoption and
/// on a slow (1 s) cadence while adopted; <see cref="RestoreSprites"/> puts the
/// original sprites back before a face is returned to the game (full-restore
/// contract, Patches/CardLifecyclePatches.cs).
///
/// Budget: at most <see cref="MaxBakedTextures"/> unique textures, each ≤
/// <see cref="MaxTextureDim"/>² (a 4096² RGBA32 mip chain ≈ 85 MB VRAM — PCVR renders
/// on the desktop GPU, and in practice the card canvases sample 1-2 big atlases plus
/// a handful of tiny strips). Every bake logs name/size/mip count/VRAM so the
/// hardware log carries the evidence. Failures are per-texture and permanent (no
/// retry storms); the face then simply keeps sampling the original mipless atlas —
/// never worse than before. Config gate: [Cards] FaceMipBake.
/// </summary>
internal static class CardFaceMipBake
{
    /// <summary>
    /// Hard cap on unique baked ATLAS textures (VRAM guard). The first cut's cap of 8 was fully
    /// consumed in the hardware log (MIP BAKE 8/8 — per-class art strips arrive async and stack
    /// up across classes), so any later class' art was silently left mipless; 24 covers a full
    /// four-class party with headroom, and hitting the cap now LOGS instead of silently skipping.
    /// </summary>
    private const int MaxBakedTextures = 24;

    /// <summary>Hard cap on PER-SPRITE bakes (trimmed/tight-packed sprites get their own small texture).</summary>
    private const int MaxSpriteBakes = 48;

    /// <summary>Largest side for a per-sprite baked texture (card-face sprites are ≤ ~1024).</summary>
    private const int MaxSpriteDim = 2048;

    /// <summary>Largest texture side we bake (the big sprite atlases are exactly 4096).</summary>
    private const int MaxTextureDim = 4096;

    /// <summary>Anisotropic level for the baked copies (oblique fan/dock viewing angles).</summary>
    private const int BakedAnisoLevel = 8;

    /// <summary>src texture instance id → baked mipmapped copy (null = failed/skipped, never retried).</summary>
    private static readonly Dictionary<int, Texture2D?> s_bakedByTexture = new(8);

    /// <summary>src sprite instance id → replacement sprite on the baked copy (null = not swappable).</summary>
    private static readonly Dictionary<int, Sprite?> s_replacementBySource = new(64);

    /// <summary>replacement sprite instance id → its original, for <see cref="RestoreSprites"/>.</summary>
    private static readonly Dictionary<int, Sprite> s_originalByReplacement = new(64);

    private static int s_bakeCount;
    private static int s_spriteBakeCount;
    private static bool s_swapLogged;
    private static bool s_errorLogged;

    /// <summary>
    /// Swap every mipless-atlas sprite under <paramref name="faceRoot"/> (the adopted
    /// FullAbilityCard) for its mip-baked equivalent. Idempotent and cheap once warm:
    /// already-swapped Images and known sprites resolve via dictionary hits. Guarded —
    /// a bake surprise must never break card adoption.
    /// </summary>
    internal static void Rescan(Component? faceRoot)
    {
        if (faceRoot == null || CardsConfig.FaceMipBake == null || !CardsConfig.FaceMipBake.Value)
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
                    continue; // already sampling a baked copy
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
    /// detail and must never leak into the restored 2D widget). Guarded like Rescan.
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
    /// </summary>
    private static Sprite? ReplacementFor(Sprite source)
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
                Texture2D? baked = BakedTextureFor(source);
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
    /// Per-sprite bake for TRIMMED rect-packed sprites: blit the sprite's stored atlas
    /// region (<c>textureRect</c>) into its OWN small mipmapped texture at the trim
    /// offset (<c>textureRectOffset</c>) — the trimmed-away margins become real
    /// transparent texels, so the replacement is an untrimmed FullRect sprite that uGUI
    /// renders identically (Image inset-by-padding + trimmed UVs ≡ full-rect quad +
    /// margins baked in). ONLY exact reconstructions bake: a sprite whose region can't
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
        if (s_spriteBakeCount >= MaxSpriteBakes)
        {
            LogSpriteSkip(source, $"per-sprite bake budget exhausted ({MaxSpriteBakes})");
            return null;
        }

        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Texture2D tex;
            try
            {
                Graphics.Blit(atlas, rt); // same orientation contract as the proven full-atlas Bake
                RenderTexture.active = rt;
                tex = new Texture2D(fullW, fullH, TextureFormat.RGBA32, mipChain: true, linear: false)
                {
                    name = source.name + " (VR-mip-trim)",
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = BakedAnisoLevel,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixels32(new Color32[fullW * fullH]); // transparent trim margins
                tex.ReadPixels(new Rect(srcX, srcY, w, h), dstX, dstY);
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            Vector4 border = source.border;
            Sprite made = Sprite.Create(tex, new Rect(0f, 0f, fullW, fullH), NormalizedPivot(source),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            made.name = source.name + " (VR-mip)";
            s_spriteBakeCount++;
            // Full reconstruction parameters on the record, so a hardware log alone can
            // verify the copy is exact (rect vs textureRect, offset, pivot, packing).
            VRLog.Info("Cards", $"MIP BAKE sprite ({s_spriteBakeCount}/{MaxSpriteBakes}): '{source.name}' " +
                                $"rect {fullW}x{fullH} ← textureRect {w}x{h} at ({srcX},{srcY}) on '{atlas.name}' " +
                                $"{atlas.width}x{atlas.height}, trim offset +{dstX},+{dstY}, " +
                                $"pivot ({source.pivot.x:F1},{source.pivot.y:F1})px, ppu {source.pixelsPerUnit:F1}, " +
                                $"border ({border.x:F0},{border.y:F0},{border.z:F0},{border.w:F0}), " +
                                $"{PackingLabel(source)} → own mipmapped texture ({tex.mipmapCount} mips), " +
                                "margins restored.");
            return made;
        }
        catch (System.Exception ex)
        {
            LogSpriteSkip(source, $"per-sprite bake failed ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    /// <summary>One log line per skipped sprite (the null cache makes each fire at most once).</summary>
    private static void LogSpriteSkip(Sprite source, string reason)
    {
        Texture2D? tex = source.texture;
        VRLog.Warn("Cards", $"MIP BAKE skip: sprite '{source.name}' on '{(tex != null ? tex.name : "?")}' " +
                            $"stays MIPLESS — {reason}.");
    }

    /// <summary>Baked mipmapped copy of the sprite's texture (cached; null = skipped/failed).</summary>
    private static Texture2D? BakedTextureFor(Sprite source)
    {
        Texture2D? tex = source.texture;
        if (tex == null)
            return null;
        int id = tex.GetInstanceID();
        if (s_bakedByTexture.TryGetValue(id, out Texture2D? known))
            return known;

        Texture2D? baked = null;
        bool withinBudget = s_bakeCount < MaxBakedTextures
            && tex.width <= MaxTextureDim && tex.height <= MaxTextureDim
            && tex.width >= 2 && tex.height >= 2;
        if (tex.mipmapCount <= 1 && !withinBudget)
        {
            // The first cut skipped this silently — the 8/8 cap in the hardware log meant any
            // later class' art stayed mipless with no evidence. Now it's on the record.
            VRLog.Warn("Cards", $"MIP BAKE skip: texture '{tex.name}' {tex.width}x{tex.height} stays " +
                                $"MIPLESS — budget {s_bakeCount}/{MaxBakedTextures} or size outside " +
                                $"[2, {MaxTextureDim}].");
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
                float vramMb = tex.width * (long)tex.height * 4L * 4f / 3f / (1024f * 1024f);
                VRLog.Info("Cards", $"MIP BAKE ({s_bakeCount}/{MaxBakedTextures}): '{tex.name}' " +
                                    $"{tex.width}x{tex.height} mips 1 → {baked.mipmapCount} " +
                                    $"(RGBA32 Trilinear aniso {BakedAnisoLevel}, ~{vramMb:F0} MB VRAM) — " +
                                    "card-face sprites re-created on the baked copy.");
            }
        }
        s_bakedByTexture[id] = baked;
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
