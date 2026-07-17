using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Native button look, sampled from the LIVE game UI so a mod-drawn button reads as
/// one of the game's own (test #25 item 3 — "genau so aussehen als sei es ein
/// originaler button"). Every mod button that has no native uGUI counterpart to dock
/// (the LONG REST control, the tray gear/SET + FOLLOW/PINNED frame buttons, the
/// CONFIRM/UNDO twins when their native widget is unavailable, and the WorldUI
/// <see cref="ButtonCluster"/> caps) skins itself from here.
///
/// WHAT IS SAMPLED at runtime off a real <c>Selectable</c> (the game's
/// <c>ExtendedButton : Button</c> inherits the whole uGUI <c>Selectable</c> visual
/// system — <c>image.sprite</c>, <c>spriteState</c>, <c>colors</c>):
/// - the shared 9-sliced button BACKGROUND sprite — the sprite the MOST buttons wear
///   (a frequency vote over every live <c>Selectable</c>), so we reuse the generic
///   stone/parchment button art, not one special button's icon;
/// - the per-state <c>SpriteState</c> (pressed / disabled / highlighted sprites) when
///   the button uses SpriteSwap — swapped straight onto our face;
/// - the <c>ColorBlock</c>, used to DERIVE the relative pressed/disabled dimming the
///   game applies (pressed⁄normal and disabled⁄normal grayscale ratios) so our tints
///   track the game's without copying its 50%-alpha multiplier verbatim (that
///   multiplier is authored to sit over the game's LIT panel; our face floats on the
///   dark board, so it stays opaque and only re-applies the game's relative dimming);
/// - the HUD <c>TMP_FontAsset</c> (MarcellusSC-Regular SDF — the same asset
///   <see cref="WorldUIAssets.TryAssignGameFont"/> harvests off the ReadyButton label).
///
/// The sprite is rendered in world space by a 9-sliced <see cref="SpriteRenderer"/>
/// (<c>SpriteDrawMode.Sliced</c>) so it keeps the native corner rounding / border at
/// any button footprint without stretching. Nothing here loads the game's
/// <c>misc_gui</c> bundle — it only reuses objects already alive in the scene.
///
/// FALLBACK: until a live button exists to sample (early scene, main menu) callers
/// keep their own procedural cube+colour look; <see cref="EnsureSampled"/> re-tries
/// cheaply each call and the skin snaps in as soon as the game HUD is up.
/// </summary>
internal static class NativeButtonSkin
{
    /// <summary>Which visual a face should wear (maps a button's enabled/accent/press state).</summary>
    internal enum FaceState { Idle, Accent, Pressed, Disabled }

    private static bool _sampled;
    private static Sprite? _normalSprite;
    private static Sprite? _pressedSprite;
    private static Sprite? _disabledSprite;
    private static Sprite? _highlightedSprite;
    private static TMP_FontAsset? _font;

    // Relative dimming the game applies (grayscale ratios off the sampled ColorBlock;
    // uGUI defaults 88/128 ≈ 0.69 pressed, 64/128 = 0.5 disabled).
    private static float _pressedMul = 0.7f;
    private static float _disabledMul = 0.5f;

    /// <summary>The game's parchment-gold accent (#EACF8C) — its de-facto emphasis colour.</summary>
    private static readonly Color AccentGold = new(0.918f, 0.812f, 0.549f, 1f);

    /// <summary>Native label colour: warm parchment (#F3DDAB), the game's button-text gold.</summary>
    internal static readonly Color LabelColor = new(0.953f, 0.867f, 0.671f, 1f);

    /// <summary>True once the live button sprite has been harvested from the scene.</summary>
    internal static bool HasSprite => EnsureSampled() && _normalSprite != null;

    /// <summary>True once the game HUD font has been harvested (label styling can proceed).</summary>
    internal static bool HasFont => EnsureSampled() && _font != null;

    /// <summary>
    /// A world-space 9-sliced face carrying the native button sprite, parented at unit
    /// scale under <paramref name="parent"/> and sized to <paramref name="size"/>
    /// tray-local meters (SpriteDrawMode.Sliced keeps the borders crisp — no stretch).
    /// Returns null when no native sprite has been sampled yet, so the caller falls
    /// back to its procedural cap. <paramref name="localZ"/> places the face on the
    /// viewer side; <paramref name="sortingOrder"/> lifts it above the backing plate.
    /// </summary>
    internal static SpriteRenderer? CreateFace(Transform parent, Vector2 size, float localZ, int sortingOrder)
    {
        if (!HasSprite)
            return null;
        var go = new GameObject("NativeFace");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0f, localZ);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _normalSprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = Color.white;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    /// <summary>Sprite for a state (its SpriteSwap sprite if the game supplies one, else the shared background).</summary>
    internal static Sprite? SpriteFor(FaceState state) => state switch
    {
        FaceState.Pressed => _pressedSprite != null ? _pressedSprite : _normalSprite,
        FaceState.Disabled => _disabledSprite != null ? _disabledSprite : _normalSprite,
        FaceState.Accent => _highlightedSprite != null ? _highlightedSprite : _normalSprite,
        _ => _normalSprite,
    };

    /// <summary>
    /// Face tint for a state. Opaque white idle (show the sprite as authored); the
    /// game's relative pressed/disabled dimming; parchment gold for the emphasis
    /// (ready/selected/confirmed) state. When a real SpriteSwap sprite exists for the
    /// state the tint stays white (the swapped sprite already encodes the state look).
    /// </summary>
    internal static Color ColorFor(FaceState state) => state switch
    {
        FaceState.Pressed => _pressedSprite != null ? Color.white : Grey(_pressedMul, 1f),
        FaceState.Disabled => _disabledSprite != null ? Color.white : Grey(_disabledMul, 0.9f),
        FaceState.Accent => _highlightedSprite != null ? Color.white : AccentGold,
        _ => Color.white,
    };

    /// <summary>Drive a live face to a state (sprite swap + tint in one call).</summary>
    internal static void Apply(SpriteRenderer face, FaceState state)
    {
        Sprite? sprite = SpriteFor(state);
        if (sprite != null && face.sprite != sprite)
            face.sprite = sprite;
        Color color = ColorFor(state);
        if (face.color != color)
            face.color = color;
    }

    /// <summary>Give a world TMP label the game's HUD font (no-op until one is sampled).</summary>
    internal static void ApplyFont(TMP_Text label)
    {
        EnsureSampled();
        if (_font != null && label.font != _font)
            label.font = _font;
        else if (_font == null)
            WorldUIAssets.TryAssignGameFont(label); // ReadyButton-label path (same asset)
    }

    /// <summary>
    /// Harvest the shared button sprite + SpriteState + ColorBlock + font from the live
    /// UI once. Cheap and idempotent — retried each call until a real button appears.
    /// Picks the sprite worn by the MOST interactable image-backed <c>Selectable</c>s
    /// (the generic button art), preferring 9-sliced (bordered) sprites.
    /// </summary>
    internal static bool EnsureSampled()
    {
        if (_sampled)
            return true;

        Selectable[] all = Object.FindObjectsOfType<Selectable>(includeInactive: false);
        if (all.Length == 0)
            return false;

        Sprite? best = null;
        Selectable? bestOwner = null;
        int bestCount = 0;
        var tally = new System.Collections.Generic.Dictionary<Sprite, int>();
        var firstOwner = new System.Collections.Generic.Dictionary<Sprite, Selectable>();
        foreach (Selectable sel in all)
        {
            if (sel == null || sel.image == null)
                continue;
            Sprite? sprite = sel.image.sprite;
            if (sprite == null)
                continue;
            int count = tally.TryGetValue(sprite, out int c) ? c + 1 : 1;
            tally[sprite] = count;
            if (!firstOwner.ContainsKey(sprite))
                firstOwner[sprite] = sel;
            // Weight sliced (bordered) sprites ahead of flat ones so a full-rect
            // toggle/panel sprite never outvotes the real 9-sliced button frame.
            int weighted = count + (sprite.border != Vector4.zero ? 1000 : 0);
            int bestWeighted = bestCount + (best != null && best.border != Vector4.zero ? 1000 : 0);
            if (weighted > bestWeighted)
            {
                best = sprite;
                bestOwner = firstOwner[sprite];
                bestCount = count;
            }
        }

        if (best != null && bestOwner != null)
        {
            _normalSprite = best;
            SpriteState ss = bestOwner.spriteState;
            _pressedSprite = ss.pressedSprite;
            _disabledSprite = ss.disabledSprite;
            _highlightedSprite = ss.highlightedSprite;

            ColorBlock cb = bestOwner.colors;
            float normal = cb.normalColor.grayscale;
            if (normal > 0.01f)
            {
                _pressedMul = Mathf.Clamp(cb.pressedColor.grayscale / normal, 0.4f, 1f);
                _disabledMul = Mathf.Clamp(cb.disabledColor.grayscale / normal, 0.3f, 1f);
            }
        }

        // Font: the same HUD asset WorldUIAssets harvests off the ReadyButton label,
        // else the first TMP label found on any sampled button.
        if (_font == null)
        {
            foreach (Selectable sel in all)
            {
                if (sel == null)
                    continue;
                var tmp = sel.GetComponentInChildren<TMP_Text>();
                if (tmp != null && tmp.font != null)
                {
                    _font = tmp.font;
                    break;
                }
            }
        }

        _sampled = _normalSprite != null || _font != null;
        if (_sampled)
            VRLog.Info("WorldUI", "NativeButtonSkin sampled the live UI: " +
                                  $"sprite='{(_normalSprite != null ? _normalSprite.name : "none")}' " +
                                  $"(sliced={(_normalSprite != null && _normalSprite.border != Vector4.zero)}), " +
                                  $"stateSprites=[p:{_pressedSprite != null},d:{_disabledSprite != null},h:{_highlightedSprite != null}], " +
                                  $"dim(pressed={_pressedMul:F2},disabled={_disabledMul:F2}), " +
                                  $"font='{(_font != null ? _font.name : "none")}'.");
        return _sampled;
    }

    private static Color Grey(float v, float a) => new(v, v, v, a);

    /// <summary>Drop cached references (hot-reload / scene teardown safe).</summary>
    internal static void Reset()
    {
        _sampled = false;
        _normalSprite = _pressedSprite = _disabledSprite = _highlightedSprite = null;
        _font = null;
        _pressedMul = 0.7f;
        _disabledMul = 0.5f;
    }
}
