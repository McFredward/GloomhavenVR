using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE THREE ASSET CHOICES ARE PICTURES, NOT A LIST OF WORDS.
///
/// <para>USER RULING (2026-09-02, verbatim): <i>"Hierbei will ich, dass das umstellen der Assets
/// etwas präsenter wird. Am Besten will ich dass die Umgebung (Wald, Keller, Default, Schwarz,
/// Mixed Reality) als Kacheln mit einem Bild darin angeboten werden. Genau für die Hände und
/// Masken."</i> Three pickers — environment, hands, masks — become a strip of picture tiles. Every
/// other setting in the pane keeps its row; this is not a new menu, it is a different control on
/// three rows that were dropdowns.</para>
///
/// <para><b>WHY A DROPDOWN WAS THE WRONG CONTROL HERE.</b> The other named-choice rows in this
/// window pick a BEHAVIOUR ("Laser only", "Always") and a word says it exactly. These three pick an
/// APPEARANCE, and "Runenschleier" tells a player nothing about what will be on their face. A
/// dropdown also hides every alternative until it is opened, which is the opposite of what the
/// ruling asks for ("präsenter").</para>
///
/// <para><b>THE FIVE ENVIRONMENT TILES ARE NOT ONE ENUM.</b> <see cref="SkyStyle"/> has four
/// members; "Mixed Reality" is a separate dial, <c>[MixedReality] Enabled</c>, and its own class
/// doc says so in as many words ("DELIBERATELY NOT MIXED REALITY: MR is its own dial with its own
/// precedence"). The player nevertheless experiences five alternatives, because MR OVERRIDES the
/// sky — with it on, no <see cref="SkyStyle"/> value is visible. So the strip presents the five
/// states the player can actually be in, and the two keys behind it are kept consistent:
/// <list type="bullet">
///   <item>picking a sky tile writes <c>[Sky] Style</c>, and clears <c>[MixedReality] Enabled</c>
///   ONLY IF it was set — otherwise the player would pick "Keller" and go on seeing passthrough;</item>
///   <item>picking the MR tile sets <c>[MixedReality] Enabled</c> and LEAVES <c>[Sky] Style</c>
///   ALONE, so turning MR off again returns the environment the player had chosen. MR is a mode
///   laid over the choice, not a replacement for it, and the config should survive it.</item>
/// </list>
/// No key changes its meaning, its range or its default, and no new key is introduced.</para>
///
/// <para><b>MULTIPLAYER.</b> Presentation only. The tiles write the same three entries the
/// dropdowns wrote, through the same <c>Apply</c> wrapper, so hand style and mask id keep riding
/// the wire exactly as before and the environment stays what it always was — each player's own
/// room. Nothing here reads a peer's copy of a key, and nothing here is mirrored.</para>
///
/// <para><b>ART SHIPS INSIDE THE PLUGIN DLL.</b> Eleven 320x240 PNGs, 425,715 bytes measured, as
/// <c>EmbeddedResource</c> — the route <see cref="EmbeddedTexture"/> exists for and states the case
/// for: the asset bundle is 74,558,728 bytes and putting eleven thumbnails in it would cost every
/// user a full re-install instead of a DLL drop. See <c>GloomhavenVR.csproj</c>.</para>
///
/// <para><b>THERE IS NEVER AN EMPTY STRIP.</b> A tile whose PNG is missing or undecodable keeps its
/// plate, its border and its label and stays clickable; the picker degrades to labelled tiles,
/// which is still a working control. <see cref="EmbeddedTexture.Get"/> caches the null, so a
/// missing resource costs one lookup for the process, not one per tile per page build.</para>
/// </summary>
internal static partial class VROptionsTab
{
    // ---- geometry -------------------------------------------------------------------------
    //
    // A FIXED TILE WIDTH, not a share of the row. Sharing the row would make the three-tile pickers
    // draw tiles half again as wide as the five-tile one, and the picture inside would then float in
    // the middle of its own plate (preserveAspect fits the 4:3 art to the SHORTER side). One width
    // means one apparent size across all three strips. The layout group still shrinks toward
    // TileMinWidth when the pane is narrower than 5 tiles, so a small pane loses size, not tiles.
    private const float TileWidth = 232f;
    private const float TileMinWidth = 116f;
    private const float TileHeight = 204f;
    private const float TileGap = 10f;

    /// <summary>Thickness of the border ring — the SELECTED marker. 5 px reads at arm's length;
    /// a 1 px outline is the kind of hairline that disappears in a headset.</summary>
    private const float TileBorder = 5f;

    /// <summary>Bottom strip of the tile that carries the name. The label is not optional: art
    /// alone cannot distinguish two dark rooms, and it is the whole control when art is missing.</summary>
    private const float TileLabelHeight = 27f;

    private const float TileLabelSize = 16f;

    // ---- colours --------------------------------------------------------------------------
    //
    // Three redundant cues say "this one". A border alone is not enough in a headset (it is a thin
    // ring at the edge of vision), so the selected tile ALSO shows its picture at full brightness
    // while the others are dimmed, and its label goes bold and gold. Any one of the three read on
    // its own is enough to answer "which is on?".
    private static readonly Color TileBorderOn = new(0.85f, 0.69f, 0.30f, 1f);
    private static readonly Color TileBorderOnHot = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Color TileBorderOff = new(0.19f, 0.17f, 0.14f, 0.92f);
    private static readonly Color TileBorderOffHot = new(0.50f, 0.40f, 0.18f, 0.95f);
    private static readonly Color TileBorderPressed = new(0.95f, 0.78f, 0.36f, 1f);
    private static readonly Color TilePlate = new(0.07f, 0.065f, 0.06f, 1f);
    private static readonly Color TilePictureOn = Color.white;
    private static readonly Color TilePictureOff = new(0.56f, 0.55f, 0.53f, 1f);
    private static readonly Color TileLabelOff = new(0.72f, 0.70f, 0.66f, 1f);

    /// <summary>Built sprites, by manifest name. The TEXTURE is already cached for the process by
    /// <see cref="EmbeddedTexture"/>; this caches the Sprite wrapper so re-opening the tab does not
    /// allocate eleven more of them. Misses are cached as null for the same reason.</summary>
    private static readonly Dictionary<string, Sprite?> TileSprites = new(11);

    /// <summary>One choice in a picture picker.</summary>
    private sealed class VariantTile
    {
        /// <summary>Manifest name of the embedded PNG, or empty for a tile that has no art.</summary>
        internal string Resource = string.Empty;

        internal Func<string> Label = () => string.Empty;

        internal Func<bool> Selected = () => false;

        /// <summary>
        /// Write the choice. Returns TRUE when the write changed which rows the pane should have,
        /// so the page needs a rebuild rather than a repaint.
        ///
        /// <para>It is a return value rather than a fixed flag on the tile because the only case is
        /// dynamic: a sky tile rebuilds the page ONLY IF it had to clear <c>[MixedReality]
        /// Enabled</c> on the way past, and whether it had to is not known until the click.
        /// <c>Apply</c> already covers the other two pickers on its own — hand style is a variant
        /// selector, and it rebuilds for those.</para>
        /// </summary>
        internal Func<bool> Choose = () => false;
    }

    /// <summary>
    /// The rows that are drawn as picture tiles instead of a control. A pure section/key table, the
    /// same shape as <c>HasSpecialRow</c> — the two never overlap in effect because this hook runs
    /// first and returns, so <c>[Sky] Style</c> and <c>[Net] MaskId</c> keep their dropdown
    /// definitions in the curated file as the code path nobody reaches while this one builds.
    /// </summary>
    private static bool HasVariantTiles(ConfigCatalog.ConfigItem item) =>
        (string.Equals(item.Section, "Sky", StringComparison.Ordinal)
         && string.Equals(item.Key, "Style", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Hands", StringComparison.Ordinal)
            && string.Equals(item.Key, "HandStyle", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Net", StringComparison.Ordinal)
            && string.Equals(item.Key, "MaskId", StringComparison.Ordinal));

    /// <summary>
    /// THE ONE ENTRY POINT. Returns false for every other setting, so a single call at the top of
    /// <c>BuildRow</c> is the whole integration.
    /// </summary>
    private static bool TryBuildVariantTiles(Transform parent, ConfigCatalog.ConfigItem item,
                                             string? caption, string? hintKey)
    {
        if (!HasVariantTiles(item))
            return false;

        VariantTile[]? tiles = VariantTilesFor(item);
        if (tiles == null || tiles.Length == 0)
            return false; // let the ordinary row ladder have it — never leave the setting unbuilt

        // The name of the setting, on its own line above the strip. A tile carries the name of the
        // CHOICE; the strip still has to say what is being chosen.
        BuildHeader(parent, Caption(item, caption), hintKey, sub: true);

        var strip = new GameObject("VariantTiles_" + item.Section + "_" + item.Key,
                                   typeof(RectTransform));
        var stripRect = (RectTransform)strip.transform;
        stripRect.SetParent(parent, worldPositionStays: false);
        Rows.Add(strip); // ClearRows owns it from here — a strip that outlived a rebuild would stack

        var row = strip.AddComponent<HorizontalLayoutGroup>();
        row.spacing = TileGap;
        row.childAlignment = TextAnchor.UpperLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.padding = new RectOffset(0, 0, 2, 14);

        var size = strip.AddComponent<LayoutElement>();
        size.preferredHeight = TileHeight + 16f;
        size.minHeight = TileHeight + 16f;
        size.flexibleHeight = 0f;

        var built = new List<BuiltTile>(tiles.Length);
        int withArt = 0;

        for (int i = 0; i < tiles.Length; i++)
        {
            if (BuildOneTile(stripRect, tiles[i], item, built))
                withArt++;
        }

        // Paint the initial selection through the same function the clicks use, so "what selected
        // looks like" is written down once.
        RepaintVariantTiles(built);

        // HW-VERIFY: the next hardware round has to answer whether the strips drew and whether the
        // embedded art decoded on the headset; both are one number each and neither is visible in
        // any other line.
        VRLog.Note("WorldUI",
            $"Variant tiles for {item.Section}/{item.Key}: {tiles.Length} tile(s), " +
            $"{withArt} with art, {tiles.Length - withArt} label-only.");

        return true;
    }

    /// <summary>The pieces of one built tile that the repaint has to reach.</summary>
    private sealed class BuiltTile
    {
        internal Button Button = null!;
        internal Image? Picture;
        internal TMP_Text Label = null!;
        internal VariantTile Tile = null!;
    }

    /// <summary>One tile. Returns true when it got a picture (false = label-only, still usable).</summary>
    private static bool BuildOneTile(RectTransform strip, VariantTile tile,
                                     ConfigCatalog.ConfigItem item, List<BuiltTile> built)
    {
        // THE FRAME IS THE TILE: full rect, and the RAYCAST TARGET. Its visible part is the border
        // ring (the plate covers the middle), so one Graphic is both the hit area for laser and
        // poke AND the surface the Button tints. A Button whose target sits under an opaque child
        // gets no visible hover; a Button with no raycastable Graphic at all gets no clicks.
        var tileGo = new GameObject(tile.Label(), typeof(RectTransform));
        var tileRect = (RectTransform)tileGo.transform;
        tileRect.SetParent(strip, worldPositionStays: false);

        var element = tileGo.AddComponent<LayoutElement>();
        element.preferredWidth = TileWidth;
        element.minWidth = TileMinWidth;
        element.flexibleWidth = 0f;
        element.preferredHeight = TileHeight;
        element.minHeight = TileHeight;
        element.flexibleHeight = 0f;

        var frame = tileGo.AddComponent<Image>();
        frame.color = Color.white; // the ColorBlock below carries the real colour; see RepaintTile
        frame.raycastTarget = true;

        var plate = MakeTileChild<Image>(tileRect, "Plate", TileBorder, TileBorder);
        plate.color = TilePlate;
        plate.raycastTarget = false;

        // THE PICTURE IS LAID OUT AGAINST THE RECT, NOT AGAINST ITS DRAWN PIXELS. preserveAspect
        // fits the 4:3 texture inside this rect and centres it; the alternative — sizing the rect to
        // the art's visible content — is how a logo once shipped 2.35x too wide on this project.
        Image? picture = null;
        Sprite? sprite = TileSprite(tile.Resource);
        if (sprite != null)
        {
            picture = MakeTileChild<Image>(tileRect, "Picture", TileBorder, TileBorder);
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.offsetMin = new Vector2(TileBorder, TileLabelHeight);
            pictureRect.offsetMax = new Vector2(-TileBorder, -TileBorder);
            picture.sprite = sprite;
            picture.preserveAspect = true;
            picture.raycastTarget = false;
        }

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.SetParent(tileRect, worldPositionStays: false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.offsetMin = new Vector2(TileBorder, TileBorder);
        labelRect.offsetMax = new Vector2(-TileBorder, TileBorder + TileLabelHeight);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        NativeButtonSkin.ApplyFont(label);
        label.text = tile.Label();
        label.fontSize = TileLabelSize;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        // The name must never be replaced by an ellipsis (standing ruling for this pane): shrink
        // the glyphs instead and keep the word readable.
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = TileLabelSize;
        label.raycastTarget = false;

        var button = tileGo.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.ColorTint;
        VariantTile captured = tile;
        button.onClick.AddListener(() => OnVariantTileClicked(item, captured, built));

        built.Add(new BuiltTile { Button = button, Picture = picture, Label = label, Tile = tile });
        return picture != null;
    }

    private static T MakeTileChild<T>(RectTransform parent, string name, float inset, float insetY)
        where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, worldPositionStays: false);
        // ANCHORS OWN A STRETCH CHILD'S SIZE: these stay at (0,0)-(1,1) and the inset is expressed
        // as offsets. Collapsing them to a point and setting sizeDelta would size the child to ZERO.
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, insetY);
        rect.offsetMax = new Vector2(-inset, -insetY);
        return go.AddComponent<T>();
    }

    /// <summary>
    /// A tile was clicked. Wrapped whole: <c>UnityEvent.Invoke</c> has no per-listener catch, so an
    /// exception raised here would amputate every listener queued after it — on a canvas the player
    /// still has to click their way back out of.
    /// </summary>
    private static void OnVariantTileClicked(ConfigCatalog.ConfigItem item, VariantTile tile,
                                             List<BuiltTile> built)
    {
        try
        {
            // Apply() repaints the pane's value labels and rebuilds the page by itself when the
            // edited entry is a variant selector (hand style) or a dependency parent.
            bool rebuild = false;
            Apply(item, () => rebuild = tile.Choose());

            // Every object below may already have been destroyed by that rebuild; each access is
            // null-guarded (Unity's == null answers true for a destroyed object).
            RepaintVariantTiles(built);

            if (rebuild)
                TickGuard.Run("VROptionsTab.VariantTiles", Rebuild, "WorldUI");
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"variant tile click on {item.Section}/{item.Key} threw " +
                                  $"({e.GetType().Name}: {e.Message}).");
        }
    }

    /// <summary>Repaint every tile in a strip from its own <c>Selected</c> predicate.</summary>
    private static void RepaintVariantTiles(List<BuiltTile> built)
    {
        for (int i = 0; i < built.Count; i++)
        {
            BuiltTile made = built[i];
            bool on;
            try
            {
                on = made.Tile.Selected();
            }
            catch
            {
                on = false; // an unreadable entry must not stop the rest of the strip repainting
            }

            if (made.Button != null)
            {
                ColorBlock colors = made.Button.colors;
                colors.normalColor = on ? TileBorderOn : TileBorderOff;
                colors.highlightedColor = on ? TileBorderOnHot : TileBorderOffHot;
                colors.pressedColor = TileBorderPressed;
                colors.selectedColor = colors.normalColor;
                colors.disabledColor = TileBorderOff;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                made.Button.colors = colors;
            }

            if (made.Picture != null)
                made.Picture.color = on ? TilePictureOn : TilePictureOff;

            if (made.Label != null)
            {
                made.Label.color = on ? TileBorderOn : TileLabelOff;
                made.Label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }

    /// <summary>The sprite for one embedded PNG, or null when it is absent or undecodable.</summary>
    private static Sprite? TileSprite(string resource)
    {
        if (string.IsNullOrEmpty(resource))
            return null;
        if (TileSprites.TryGetValue(resource, out Sprite? cached))
            return cached;

        Sprite? sprite = null;
        // Colour art, so linear:false. Clamp, not Repeat: at a tile's edge bilinear filtering would
        // otherwise fetch the opposite side of the picture.
        Texture2D? texture = EmbeddedTexture.Get(resource, linear: false, TextureWrapMode.Clamp);
        if (texture != null)
        {
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                   new Vector2(0.5f, 0.5f), 100f);
            sprite.name = resource;
        }

        TileSprites[resource] = sprite;
        return sprite;
    }

    /// <summary>Drop the built sprites (module shutdown / hot reload), matching
    /// <c>WorldUIAssets.Reset</c>'s contract. The textures themselves belong to
    /// <see cref="EmbeddedTexture"/> and are not touched here.</summary>
    internal static void ResetVariantTiles() => TileSprites.Clear();
}
