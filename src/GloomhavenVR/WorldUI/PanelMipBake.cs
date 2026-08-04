using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Runtime MIP BAKE for the WorldUI panel surfaces -- the initiative track and the
/// mouseover tooltip box (user report: "Aliasing ist extrem stark bei den Raendern der
/// Mouse-Overlay-Hints und auch den Linien und Raendern der Bilder in der
/// Initiativreihenfolge ... bei den Karten hast du das bereits geschafft").
///
/// ROOT CAUSE -- the SAME data defect the card faces had (Cards/CardFaceMipBake.cs, the
/// proven prior art this class deliberately mirrors). The INITIATIVE TEXTURE DIAG
/// hardware log line already proved it for the track: every texture the adopted track
/// canvas samples ships with mipmapCount == 1, aniso 1, Bilinear. The tooltip canvas
/// draws from the same family of mipless UI atlases. A mipless bilinear texture under
/// minification (a portrait or a tooltip frame on a world-space quad at board distance
/// covers far fewer pixels than its texels) aliases in TEXTURE space: MSAA resolves
/// geometry edges only and supersampling merely shrinks the footprint further, so the
/// shimmer survives both -- exactly as it did on the cards until the mip bake.
///
/// MECHANISM -- one shared cache, two graphic kinds:
/// <list type="bullet">
/// <item><description><c>Image</c> (frame / selection / line sprites): swapped through
/// <see cref="CardFaceMipBake.ReplacementFor"/>, the proven sprite-equivalence path
/// (same rect / pivot / PPU / border on a mipmapped trilinear/aniso copy; rotated,
/// tight-packed and inexact-trim sprites are SKIPPED with a log line, never guessed --
/// the v3 card-corruption hard rule). Sharing the Cards cache is deliberate: a tooltip
/// icon on an atlas a card face already baked reuses that copy instead of holding a
/// second ~85 MB one.</description></item>
/// <item><description><c>RawImage</c> (the track's <c>m_AvatarImage</c> portraits):
/// there is NO sprite to swap -- the game's <c>CharacterPortraitsProvider</c> assigns a
/// raw <c>Texture</c> plus a <c>uvRect</c> (decompiled InitiativeTrackActorAvatar.cs:
/// <c>UpdateTexture(texture, coords)</c>). The whole texture is baked via
/// <see cref="CardFaceMipBake.BakedTextureFor(Texture2D)"/> (identical dimensions, so
/// the uvRect stays valid untouched) and <c>RawImage.texture</c> is pointed at the
/// copy. Only <c>Texture2D</c> sources bake; anything else (a RenderTexture, null
/// after the provider's <c>UnloadedTexture</c>) is left alone.</description></item>
/// </list>
///
/// RE-ASSERT, CHANGE-GATED -- both surfaces are POOLED and REBUILT by the game: the
/// track re-registers every avatar per round (<c>SetAttributesDirect</c> -&gt;
/// <c>RegisterNewUser</c> -&gt; <c>UpdateTexture</c> puts the ORIGINAL mipless texture
/// back, and portraits arrive async from the misc_characterportraits bundle), and the
/// tooltip's line objects are pooled per hover. So the owners re-run
/// <see cref="Rescan"/> (the track on a 1 s cadence while converted, mirroring
/// CardFace.MipRescanInterval; the tooltip on its already-gated visible frames) and the
/// scan itself is idempotent-cheap: an Image already wearing a baked sprite and a
/// RawImage already pointing at a baked copy resolve via dictionary hits, and a write
/// happens ONLY when the game genuinely swapped a graphic back (the CardParticlesOff
/// re-assert spirit -- never per-frame churn). The scans use the List-based
/// GetComponentsInChildren overloads into reused scratch lists, so the steady state
/// allocates nothing.
///
/// RESTORE -- mutate-and-restore house style: <see cref="Restore"/> hands every live
/// graphic its original sprite/texture back (sprites via
/// <see cref="CardFaceMipBake.RestoreSprites"/>, raw textures via the baked-to-original
/// map here; an original the game has meanwhile Destroyed -- e.g. its rebuilt
/// portrait mega-texture -- restores to null, the game's own <c>UnloadedTexture</c>
/// contract, and the provider re-assigns on its next refresh). Called when the track
/// surface releases/shuts down and when the tooltip presentation restores to screen
/// space. The baked textures themselves stay in the session-lifetime shared cache --
/// the CardFaceMipBake precedent (bake once per content identity, budgeted and logged,
/// never destroyed mid-session) -- because a card face may still be sampling the very
/// atlas copy a tooltip icon shares.
///
/// Config gate: [WorldUI] PanelMipBake (mirrors [Cards] FaceMipBake, read live).
/// Every actual bake logs name/size/mips/VRAM through the shared MIP BAKE log lines,
/// so the hardware log carries the coverage evidence per texture; this class adds one
/// line per SURFACE naming how many graphics were swapped.
/// </summary>
internal static class PanelMipBake
{
    /// <summary>Reused Image scan buffer (List overload of GetComponentsInChildren -- no
    /// steady-state allocation; the tooltip rescans on every visible frame).</summary>
    private static readonly List<Image> ImageScratch = new(64);

    /// <summary>Reused RawImage scan buffer (same discipline).</summary>
    private static readonly List<RawImage> RawScratch = new(16);

    /// <summary>baked raw-texture instance id -&gt; the game texture it replaced. First
    /// original wins (a second Texture2D instance deduped onto the same baked copy keeps
    /// the first mapping -- both are game-owned content-identical textures, and restore
    /// must be deterministic).</summary>
    private static readonly Dictionary<int, Texture> s_rawOriginalByBaked = new(16);

    /// <summary>Surfaces that already logged their first successful swap (one live line
    /// per surface, matching CardFaceMipBake's one-shot swap log).</summary>
    private static readonly HashSet<string> s_swapLogged = new(4);

    /// <summary>One warning per session on a throwing scan/restore -- a bake surprise must
    /// never spam nor break the owning surface (same latch as CardFaceMipBake).</summary>
    private static bool s_errorLogged;

    /// <summary>
    /// Swap every mipless-texture graphic under <paramref name="root"/> for its mip-baked
    /// equivalent (see the class doc). Idempotent and cheap once warm; guarded so a bake
    /// surprise can never break the owning surface's tick. Safe to call on any cadence --
    /// all writes are change-gated by construction (an already-swapped graphic is
    /// recognized and skipped).
    /// </summary>
    internal static void Rescan(Component? root, string surface)
    {
        if (root == null || WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
            return;
        try
        {
            int swapped = 0;

            // Images: the frame / selection / digit / tooltip-line sprites. includeInactive
            // because the game toggles sub-widgets (selection frames, dead-image overlays,
            // pooled tooltip lines) -- swap them while hidden so re-activation shows the
            // baked copy at once.
            ImageScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, ImageScratch);
            for (int i = 0; i < ImageScratch.Count; i++)
            {
                Image img = ImageScratch[i];
                if (img == null)
                    continue;
                Sprite? sprite = img.sprite;
                if (sprite == null || CardFaceMipBake.IsBakedSprite(sprite))
                    continue; // no sprite / already sampling a baked copy
                Sprite? replacement = CardFaceMipBake.ReplacementFor(sprite);
                if (replacement != null)
                {
                    img.sprite = replacement;
                    swapped++;
                }
            }
            ImageScratch.Clear();

            // RawImages: the initiative portraits (see the class doc's RawImage note).
            RawScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, RawScratch);
            for (int i = 0; i < RawScratch.Count; i++)
            {
                RawImage raw = RawScratch[i];
                // The Unity-null check on tex matters: a Destroyed Texture2D still passes the
                // C# type pattern, and touching its width/height would throw out of the scan.
                if (raw == null || raw.texture is not Texture2D tex || tex == null)
                    continue; // null/destroyed (provider unloaded) or not a bakeable plain texture
                if (s_rawOriginalByBaked.ContainsKey(tex.GetInstanceID()))
                    continue; // already pointing at one of our baked copies
                if (tex.mipmapCount > 1)
                    continue; // the source already has mips -- nothing to fix
                Texture2D? baked = CardFaceMipBake.BakedTextureFor(tex);
                if (baked == null)
                    continue; // budget/size/failed -- keeps the original, never worse
                int bakedId = baked.GetInstanceID();
                if (!s_rawOriginalByBaked.ContainsKey(bakedId))
                    s_rawOriginalByBaked[bakedId] = tex;
                raw.texture = baked; // same dimensions -> the game's uvRect stays valid
                swapped++;
            }
            RawScratch.Clear();

            if (swapped > 0 && s_swapLogged.Add(surface))
            {
                VRLog.Info("WorldUI", $"MIP BAKE live on '{surface}': {swapped} graphic(s) now sample " +
                                      "mipmapped trilinear/aniso copies of the game's mipless textures " +
                                      "(shared cache with the card faces) -- texture-space shimmer " +
                                      "addressed at the data; see the MIP BAKE lines for each texture.");
            }
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("WorldUI", $"Panel mip-bake rescan failed on '{surface}' " +
                                      $"({ex.GetType().Name}: {ex.Message}) -- the surface keeps the " +
                                      "original mipless graphics.");
            }
        }
    }

    /// <summary>
    /// Hand every graphic under <paramref name="root"/> its ORIGINAL sprite/texture back --
    /// called before a surface returns its canvas to the game (track release/shutdown,
    /// tooltip restore to screen space), so the 2D UI is left exactly as authored. The
    /// full-restore counterpart of <see cref="Rescan"/>; guarded the same way.
    /// </summary>
    internal static void Restore(Component? root)
    {
        if (root == null)
            return;
        try
        {
            // Sprites: the shared cache knows every replacement it minted.
            CardFaceMipBake.RestoreSprites(root);

            // Raw textures: baked copy back to the recorded original. An original the game
            // has Destroyed since (its portrait mega-texture is rebuilt on demand) restores
            // to null -- the game's own UnloadedTexture state, re-filled by the provider.
            if (s_rawOriginalByBaked.Count == 0)
                return;
            RawScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, RawScratch);
            for (int i = 0; i < RawScratch.Count; i++)
            {
                RawImage raw = RawScratch[i];
                if (raw == null || raw.texture == null)
                    continue;
                if (s_rawOriginalByBaked.TryGetValue(raw.texture.GetInstanceID(), out Texture original))
                    raw.texture = original != null ? original : null;
            }
            RawScratch.Clear();
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("WorldUI", $"Panel mip-bake restore failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }
}
