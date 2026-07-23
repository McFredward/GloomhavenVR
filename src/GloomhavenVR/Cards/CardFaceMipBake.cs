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
/// <c>Image.sprite</c> setter. Geometry, layout and atlas UVs are exactly reproduced
/// (only untrimmed, unrotated, rect-packed sprites are swapped — anything else is
/// skipped rather than risked). The game freely reassigns sprites on state changes
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
    /// <summary>Hard cap on unique baked textures (VRAM guard; the diag showed 5 uniques).</summary>
    private const int MaxBakedTextures = 8;

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
    /// sight; null when the sprite cannot be reproduced EXACTLY (tight packing,
    /// rotation, trimming) or its texture is not worth baking (already mipped) — those
    /// keep the original, never a visual risk.
    /// </summary>
    private static Sprite? ReplacementFor(Sprite source)
    {
        int id = source.GetInstanceID();
        if (s_replacementBySource.TryGetValue(id, out Sprite? cached))
            return cached;

        Sprite? made = null;
        Texture2D? baked = BakedTextureFor(source);
        if (baked != null)
        {
            // Only sprites whose atlas placement is a plain unrotated, untrimmed rect
            // reproduce exactly as a FullRect sprite on the copy. textureRect throws for
            // tight-packed sprites — treat that as "skip".
            Rect texRect;
            bool rectOk;
            try
            {
                texRect = source.textureRect;
                rectOk = source.packingRotation == SpritePackingRotation.None
                         && source.textureRectOffset == Vector2.zero
                         && texRect.width >= 1f && texRect.height >= 1f
                         && Mathf.Abs(texRect.width - source.rect.width) < 0.5f
                         && Mathf.Abs(texRect.height - source.rect.height) < 0.5f;
            }
            catch (System.Exception)
            {
                texRect = default;
                rectOk = false;
            }

            if (rectOk)
            {
                var pivot = new Vector2(
                    source.rect.width > 0f ? source.pivot.x / source.rect.width : 0.5f,
                    source.rect.height > 0f ? source.pivot.y / source.rect.height : 0.5f);
                made = Sprite.Create(baked, texRect, pivot, source.pixelsPerUnit, 0,
                    SpriteMeshType.FullRect, source.border);
                made.name = source.name + " (VR-mip)";
                s_originalByReplacement[made.GetInstanceID()] = source;
            }
        }

        s_replacementBySource[id] = made;
        return made;
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
        if (tex.mipmapCount <= 1 // an already-mipped texture needs nothing from us
            && s_bakeCount < MaxBakedTextures
            && tex.width <= MaxTextureDim && tex.height <= MaxTextureDim
            && tex.width >= 2 && tex.height >= 2)
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
