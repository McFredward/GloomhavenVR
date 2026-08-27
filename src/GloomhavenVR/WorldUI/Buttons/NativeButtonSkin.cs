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
/// the board keycaps, the turn-flow SKIP among them) skins itself from here.
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

    /// <summary>
    /// Target WORLD thickness of the native 9-slice border on a tray button (~6 mm).
    /// The game authors its button sprites at a pixels-per-unit tuned for full-screen
    /// uGUI; <see cref="SpriteRenderer"/> in <see cref="SpriteDrawMode.Sliced"/> draws
    /// each border at <c>border_px / pixelsPerUnit</c> metres, so on a few-cm tray face
    /// the border dwarfs the whole button and the nine slices collapse to four
    /// overlapping corner tiles (test #26: "4 black fields in a grid with a gold frame,
    /// no text"). <see cref="ReborderForWorld"/> re-expresses each sampled sprite at a
    /// PPU that renders its border at this thickness.
    /// </summary>
    private const float SlicedBorderMeters = 0.006f;

    private static bool _sampled;
    private static Sprite? _normalSprite;
    private static Sprite? _pressedSprite;
    private static Sprite? _disabledSprite;
    private static Sprite? _highlightedSprite;
    private static TMP_FontAsset? _font;

    /// <summary>Runtime sprite copies we minted (re-bordered for world scale) — destroyed on Reset.</summary>
    private static readonly System.Collections.Generic.List<Sprite> _created = new(4);

    // Relative dimming the game applies (grayscale ratios off the sampled ColorBlock;
    // uGUI defaults 88/128 ≈ 0.69 pressed, 64/128 = 0.5 disabled).
    private static float _pressedMul = 0.7f;
    private static float _disabledMul = 0.5f;

    /// <summary>
    /// Relative brightening the game applies to a HOVERED widget — its own
    /// <c>ColorBlock.highlightedColor</c> against <c>normalColor</c>, sampled exactly as the two
    /// above are.
    ///
    /// <para>ADDED IN ModBuild 308 FOR A REASON WORTH KEEPING. When the mirrored decision row got
    /// the owner's hover (ModBuild 300), the mod-drawn PLATE fallback beside it did NOT — and the
    /// refusal was deliberate and recorded: a plate has no <c>Selectable</c> to read a
    /// <c>ColorBlock</c> off, so any hover factor there would have been INVENTED. This is that
    /// factor, taken from the same place the pressed and disabled ones already come from: the
    /// game's own button. It is the answer that note said was on the shelf.</para></summary>
    private static float _highlightedMul = 1.05f;

    /// <summary>Emphasis tint for native faces — T4: softened from the game's #EACF8C
    /// toward an aged parchment-brass so an accented cap glows warm, not neon.</summary>
    private static readonly Color AccentGold = new(0.84f, 0.72f, 0.48f, 1f);

    /// <summary>
    /// Native label colour. CONTRAST FIX (user #3 — "the text has the SAME colour as the
    /// buttons"): the old warm parchment-gold (#F3DDAB) sat only a hue apart from the warm
    /// brass/parchment cap faces (rest-short #9E8540, confirm-brass #AD843D, follow-brass
    /// #94752D) — same family, low luminance separation, and the wood-grain grain darkened
    /// the glyph region too. Pushed to a BRIGHT, near-white warm parchment (#FBF3E0): clearly
    /// lighter AND much less saturated than any cap accent, so it reads even on the lightest
    /// brass caps, and its luminance (~0.94) sits well above every cap face (0.14 dark-wood
    /// disabled … 0.53 brightest brass). Legibility on the LIGHT caps comes from the dark
    /// outline+underlay in <see cref="StyleEngravedLabel"/> (a dark keyline separates the
    /// bright glyphs from a bright cap); legibility on the DARK caps comes from this bright
    /// fill. The antique look is kept — it is a paler parchment, not a cold white.
    /// USER DEBUG OPTION (2026-07 — "give me the TEXT COLORS as a debug option"): now the live
    /// <see cref="ButtonTuning.LabelColor"/> bind ([ButtonColors] LabelR/G/B), whose DEFAULT is
    /// this exact #FBF3E0, so nothing changes until the user tunes it. Every consumer (cluster
    /// caps, board keycaps, docked native captions) reads this property, so a stepper edit
    /// re-colours them all (keycaps on the ButtonTuning.Version rebuild, the cluster per-tick).</summary>
    internal static Color LabelColor => ButtonTuning.LabelColor;

    /// <summary>Engraved-label outline colour — live <see cref="ButtonTuning.LabelOutlineColor"/>
    /// bind ([ButtonColors] LabelOutlineR/G/B); default dark umber, fully opaque.</summary>
    private static Color EngraveOutlineColor => ButtonTuning.LabelOutlineColor;

    /// <summary>Engraved-label outline width, fraction of the SDF spread — live
    /// <see cref="ButtonTuning.LabelOutlineWidth"/> bind (default 0.20).</summary>
    private static float EngraveOutlineWidth => ButtonTuning.LabelOutlineWidth;

    /// <summary>One-shot log guard for the applied label colour/outline (user #3).</summary>
    private static bool _styleLogged;

    /// <summary>The game's own HOVER brightening, ≥ 1 — see <see cref="_highlightedMul"/>. Read by
    /// the mirrored decision plates and use-bar tiles, which have no <c>Selectable</c> of their own
    /// to ask.</summary>
    internal static float HighlightMul => EnsureSampled() ? _highlightedMul : 1.05f;

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
    /// <paramref name="overrideMaterial"/> (item 5/6): when non-null the face renders
    /// through it instead of the default Sprites/Default sprite material — the tray
    /// passes the bundled <c>GloomhavenVR/Overlay</c> material (which samples the
    /// sprite through <c>_MainTex</c> and exposes <c>_ZTest</c>) so a board-HUD button
    /// cap (gear / follow-toggle) can be forced ZTest-Always by RenderOnTop and draw
    /// over the opaque board; CONFIRM/UNDO pass it too but look identical (default
    /// ZTest LEqual) until they are RenderOnTop'd (they are not).
    /// </summary>
    internal static SpriteRenderer? CreateFace(Transform parent, Vector2 size, float localZ, int sortingOrder,
        Material? overrideMaterial = null)
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
        if (overrideMaterial != null)
            sr.sharedMaterial = overrideMaterial;
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

    /// <summary>Give a world TMP label the game's HUD font (no-op until one is sampled) —
    /// and force the label DEPTH-HONEST (see <see cref="MakeLabelDepthHonest"/>): the game
    /// font's shared material is authored for the 2D screen HUD and renders ON TOP
    /// (renderQueue 4003 + ZTest Always), which in VR bled button text through nearer
    /// geometry and through the held-figure info panel (perspective must hold — only the
    /// skybox is exempt).</summary>
    internal static void ApplyFont(TMP_Text label)
    {
        EnsureSampled();
        if (_font != null && label.font != _font)
            label.font = _font;
        else if (_font == null)
            WorldUIAssets.TryAssignGameFont(label); // ReadyButton-label path (same asset)
        MakeLabelDepthHonest(label);
    }

    /// <summary>
    /// Depth-honest render state for a world-space mod label, via a PER-LABEL font-material
    /// INSTANCE (TMP's <c>fontMaterial</c> — the game's shared font asset is never mutated):
    /// renderQueue Transparent (3000, down from the HUD asset's on-top 4003, and ≤ the
    /// converted panels' UI queue) + ZTest LEqual on every ZTest spelling the TMP/UI shader
    /// family uses (<c>_ZTestMode</c> on the distance-field shader, <c>unity_GUIZTestMode</c>
    /// on the UI variants — the per-material value beats Unity's global; ActorBars precedent,
    /// hardware-verified). The label then depth-tests like everything else: occluded by walls,
    /// hands, held cards and any depth-writing geometry in FRONT of it, while staying readable
    /// on its button because it sits proud of the cap face (the ButtonCluster/PlayTray
    /// DEPTH-CORRECT design). Idempotent — safe to re-apply after any font (re)assignment.
    /// </summary>
    internal static void MakeLabelDepthHonest(TMP_Text label)
    {
        if (label == null || label.font == null)
            return; // no font yet → no material to fix; re-applied when the font lands
        Material mat = label.fontMaterial; // per-label instance (never the shared asset)
        if (mat == null)
            return;
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 3000
        if (mat.HasProperty("_ZTestMode"))
            mat.SetInt("_ZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    /// <summary>
    /// T4 (button restyle): ENGRAVED look for a board-button label — a thin dark-umber
    /// SDF outline on the label's per-instance font material, so the parchment glyphs
    /// read as letters carved into (not stickered onto) the wooden cap. Subtle by
    /// design (width 0.10 of the SDF range); properties are probed so any TMP shader
    /// variant without an outline is a clean no-op. Idempotent — safe after every font
    /// (re)assignment; call AFTER <see cref="ApplyFont"/> (which mints the material
    /// instance this writes to). Deliberately NOT part of ApplyFont itself: captions,
    /// HUD lines and the quest block share that path and must stay un-outlined.
    /// </summary>
    internal static void StyleEngravedLabel(TMP_Text label) =>
        StyleEngravedLabel(label, EngraveOutlineColor, EngraveOutlineWidth,
                           ButtonTuning.LabelOutlineEnabled, ButtonTuning.LabelUnderlayEnabled);

    /// <summary>
    /// The same engraved styling, but with the [ButtonColors] values supplied EXPLICITLY instead of
    /// read from this client's own config.
    ///
    /// <para>WHY THE OVERLOAD EXISTS, AND IT IS NOT A GENERALISATION FOR ITS OWN SAKE. Every caller
    /// on the local board wants the local player's dials and calls the no-arg form. The MIRROR of a
    /// peer's board (<c>Net.RemoteBoardFurniture.InertCap</c>) wants THAT PEER's dials, which now
    /// arrive on extension record 28 — and until this overload existed it had no way to ask for
    /// them, so it called the no-arg form and every remote keycap was lettered in the VIEWER's
    /// colours. That is the 1:1 ruling violated in the one direction nobody looks for: not "the
    /// owner's tuning is missing" but "the viewer's tuning has leaked onto somebody else's board".
    /// A player who set their own keyline to red saw red keylines on all three of their team-mates'
    /// boards, and each of those team-mates saw their own.</para>
    /// </summary>
    internal static void StyleEngravedLabel(TMP_Text label, Color outlineColor, float outlineWidth,
                                            bool outlineOn, bool underlayOn)
    {
        if (label == null || label.font == null)
            return; // no font yet — the caller re-applies fonts late, restyle then
        Material mat = label.fontMaterial; // per-label instance (never the shared asset)
        if (mat == null)
            return;
        // CONTRAST FIX (user #3): a fully-opaque, thicker dark keyline. On a LIGHT brass/
        // parchment cap the old 0.10-wide, 85%-alpha umber outline barely registered, so the
        // bright glyphs and the bright cap merged; a full-opacity 0.20 rim now clearly rings
        // every letter and reads against both light and dark caps (paired with the brightened
        // LabelColor fill). Still a dark umber, not black — the carved-engraving look holds.
        // USER DEBUG OPTION: colour/width from the live [ButtonColors] binds, and the outline is
        // gated on the LabelOutline toggle (OFF → width 0 + keyword off = a flat label). Applied on
        // the keycap rebuild that a ButtonColors edit triggers (ButtonTuning.Version), so toggling
        // it live takes effect on the next re-skin.
        if (mat.HasProperty("_OutlineColor"))
            mat.SetColor("_OutlineColor", outlineColor);
        if (mat.HasProperty("_OutlineWidth"))
        {
            mat.SetFloat("_OutlineWidth", outlineOn ? outlineWidth : 0f);
            if (outlineOn)
                mat.EnableKeyword("OUTLINE_ON"); // mobile TMP variants gate outline on this; no-op elsewhere
            else
                mat.DisableKeyword("OUTLINE_ON");
        }
        // A soft dark drop-shadow underlay sells the carved depth AND adds a second contrast
        // cue on light caps (a shaded halo below/right of the glyphs). Darkened and its alpha
        // raised (0.55 → 0.70) so it holds on a bright brass cap (UNDERLAY_ON gates the pass).
        // User debug option: gated on the LabelUnderlay toggle (OFF → keyword off = no shadow).
        if (mat.HasProperty("_UnderlayColor"))
        {
            if (underlayOn)
            {
                mat.SetColor("_UnderlayColor", new Color(0.05f, 0.03f, 0.02f, 0.70f));
                if (mat.HasProperty("_UnderlaySoftness"))
                    mat.SetFloat("_UnderlaySoftness", 0.35f);
                if (mat.HasProperty("_UnderlayOffsetX"))
                    mat.SetFloat("_UnderlayOffsetX", 0.30f);
                if (mat.HasProperty("_UnderlayOffsetY"))
                    mat.SetFloat("_UnderlayOffsetY", -0.30f);
                mat.EnableKeyword("UNDERLAY_ON");
            }
            else
            {
                mat.DisableKeyword("UNDERLAY_ON");
            }
        }

        if (!_styleLogged)
        {
            _styleLogged = true;
            VRLog.Info("WorldUI", "NativeButtonSkin keycap-label CONTRAST (user #3): fill " +
                                  $"RGB({LabelColor.r:F2},{LabelColor.g:F2},{LabelColor.b:F2}) " +
                                  "bright warm parchment (#FBF3E0), " +
                                  $"outline RGBA({outlineColor.r:F2},{outlineColor.g:F2}," +
                                  $"{outlineColor.b:F2},{outlineColor.a:F2}) dark umber " +
                                  $"width {outlineWidth:F2}, + dark underlay shadow — readable on both " +
                                  "light (brass/parchment) and dark (wood/pewter) caps.");
        }
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
            // MIP FIRST, THEN re-border (ModBuild 199 — see the MipBaked helper below): the
            // game's uGUI art carries no mip chain, and these faces are world-space quads
            // read at arm's length, not screen-space widgets at 1:1.
            // Re-border for world scale (test #26): the raw uGUI sprite's 9-slice border
            // would collapse on a small tray face — rescale each to a ~6 mm world frame.
            _normalSprite = ReborderForWorld(MipBaked(best));
            SpriteState ss = bestOwner.spriteState;
            _pressedSprite = ReborderForWorld(MipBaked(ss.pressedSprite));
            _disabledSprite = ReborderForWorld(MipBaked(ss.disabledSprite));
            _highlightedSprite = ReborderForWorld(MipBaked(ss.highlightedSprite));

            ColorBlock cb = bestOwner.colors;
            float normal = cb.normalColor.grayscale;
            if (normal > 0.01f)
            {
                _pressedMul = Mathf.Clamp(cb.pressedColor.grayscale / normal, 0.4f, 1f);
                _disabledMul = Mathf.Clamp(cb.disabledColor.grayscale / normal, 0.3f, 1f);
                // Clamped ABOVE 1 as well as below it: uGUI's own default highlight is a hair
                // DARKER than normal (0.96), and a mirrored hover that darkens reads as a press.
                // The floor of 1 keeps "hovered" meaning "brighter" whatever the artist authored.
                _highlightedMul = Mathf.Clamp(cb.highlightedColor.grayscale / normal, 1f, 1.6f);
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
                                  $"font='{(_font != null ? _font.name : "none")}'. " +
                                  // ModBuild 199 — the aliasing evidence, on the same line, with the
                                  // comparison count so "never ran" and "ran and found nothing" differ.
                                  $"MIP STATE: {_mipAsked} sampled sprite(s) offered to the bake cache, " +
                                  $"{_mipBaked} now sample a MIPMAPPED trilinear/aniso copy, " +
                                  $"{_mipAsked - _mipBaked} kept " +
                                  "the game's own texture (already mipped, or a rotated/tight atlas " +
                                  "placement the cache refuses by design). FACE TEXTURE NOW: " +
                                  FaceTextureState(_normalSprite) + ". A world-space quad read at arm's " +
                                  "length minifies its source; mips=1 there IS the shimmer.");
        return _sampled;
    }

    /// <summary>Number of sampled sprites handed to the bake cache, and how many came back baked.
    /// Log material only — see the MIP STATE clause on the sample line.</summary>
    private static int _mipAsked;
    private static int _mipBaked;

    /// <summary>mips / filter / aniso of the texture a face actually samples, for the log line.</summary>
    private static string FaceTextureState(Sprite? s)
    {
        Texture2D? t = s != null ? s.texture : null;
        if (t == null)
            return "no sprite sampled yet";
        return $"'{t.name}' {t.width}x{t.height}, mips={t.mipmapCount}, {t.filterMode}, aniso {t.anisoLevel}";
    }

    /// <summary>
    /// The sampled sprite, re-expressed on a MIPMAPPED copy of its texture — or the sprite
    /// itself when no such copy is available.
    ///
    /// <para>WHY THIS IS HERE AND WHY IT IS NOT A COPY OF THE WINDOW FIX. The window fix
    /// (<c>PanelSupersample</c>) renders a canvas into a render target above its authored
    /// resolution so the eye lands on a real mip level. There is no canvas here: a native
    /// face is a world-space <see cref="SpriteRenderer"/> quad textured straight from the
    /// game's uGUI art, and the game's uGUI art is imported as "Sprite (2D and UI)" with mip
    /// generation OFF — every one of the 49 distinct game textures the mip cache has ever
    /// measured on hardware reported <c>mips 1</c>. A mipless texture minified onto a
    /// few-centimetre quad drops source texels every frame no matter what the render target
    /// resolution is, which is why the supersample lever cannot reach it. The fix is at the
    /// DATA: a mip chain, trilinear, and aniso — the proven card-face path.</para>
    ///
    /// <para>ONE-SHOT AND SAFE. <see cref="EnsureSampled"/> runs once per session, so this is
    /// four cache lookups in a lifetime and no per-frame cost at all. The cache refuses a
    /// rotated or tight-packed atlas sprite (a rectangular copy would drag in its
    /// neighbours' pixels — the v3 card-corruption rule) and refuses an already-mipped
    /// source; both refusals return null and are handled by keeping the game's own sprite,
    /// i.e. exactly today's look. The VRAM is charged against the ONE shared
    /// <c>CardFaceMipBake</c> ceiling, so a button face sharing an atlas a card already paid
    /// for costs nothing extra.</para>
    ///
    /// <para>Gated on the same [WorldUI] PanelMipBake dial as every other mip swap in this
    /// module, so OFF is exactly the pre-199 build.</para>
    /// </summary>
    private static Sprite? MipBaked(Sprite? source)
    {
        if (source == null)
            return null;
        if (WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
            return source;
        _mipAsked++;
        try
        {
            Sprite? baked = Cards.CardFaceMipBake.ReplacementFor(source);
            if (baked == null)
                return source;
            _mipBaked++;
            return baked;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("WorldUI", $"NativeButtonSkin could not mip-bake '{source.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the face keeps the game's " +
                                  "own mipless sprite, i.e. the pre-199 look, never worse.");
            return source;
        }
    }

    private static Color Grey(float v, float a) => new(v, v, v, a);

    /// <summary>
    /// A copy of a sampled game button sprite whose 9-slice border is re-expressed at a
    /// pixels-per-unit that renders it ~<see cref="SlicedBorderMeters"/> thick in WORLD
    /// space, so a small (few-cm) tray button shows the intact native frame instead of
    /// collapsing into its four overlapping corner tiles (test #26). Non-sliced sprites
    /// (no border) and null pass straight through unchanged.
    /// </summary>
    private static Sprite? ReborderForWorld(Sprite? src)
    {
        if (src == null)
            return null;
        Vector4 b = src.border;
        float maxB = Mathf.Max(Mathf.Max(b.x, b.y), Mathf.Max(b.z, b.w));
        if (maxB <= 0.5f || src.texture == null)
            return src; // flat sprite (no 9-slice border) — nothing to rescale
        try
        {
            float ppu = maxB / SlicedBorderMeters; // border_px / ppu == SlicedBorderMeters
            Rect r = src.rect;
            var pivot = new Vector2(src.pivot.x / r.width, src.pivot.y / r.height);
            Sprite s = Sprite.Create(src.texture, r, pivot, ppu, 0, SpriteMeshType.FullRect, b);
            s.name = src.name + " (VR-sliced)";
            _created.Add(s);
            return s;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("WorldUI", $"NativeButtonSkin: could not re-border '{src.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — using it as-is.");
            return src;
        }
    }

    // ---- un-renderable glyph strip (tofu-box fix) -------------------------------------------

    /// <summary>One log line per distinct raw string we ever stripped (session-scoped).</summary>
    private static readonly System.Collections.Generic.HashSet<string> _sanitizeLogged = new();

    /// <summary>
    /// Strip characters <paramref name="label"/>'s font chain cannot render, collapsing the
    /// whitespace that stripping leaves behind. The label text is NEVER shortened otherwise —
    /// no ellipsis, no truncation (standing rule) — and a fully renderable string is returned
    /// AS THE SAME INSTANCE, so callers' reference-compare change gates stay allocation-free.
    ///
    /// <para>ROOT CAUSE (user report 2026-08-04: "Wenn der Button den Text 'Mach dich bereit'
    /// zeigt, ist ein kleines Viereck vor dem Text"). The confirmed-state CONFIRM keycap builds
    /// its label as <c>"✓ " + Loc.Game("GUI_READY", …)</c> (PlayTray.TickStatus) — German
    /// GUI_READY is the reported "Mach dich bereit". TMP substitutes every character the
    /// label's font (plus its fallback chain) lacks with the hollow box U+25A1, and the mod's
    /// keycap font is HARVESTED from whatever live label <see cref="EnsureSampled"/> finds
    /// first — scene- and language-dependent, so '✓' (U+2713) renders on one setup and boxes
    /// on another. The same failure shape covers a GAME-provided label embedding a glyph only
    /// the game's own HUD font carries (the keycaps mirror <c>buttonText.text</c> verbatim).
    /// Fixing it at this seam — the moment a game/mod string meets a mod-owned TMP label —
    /// handles both: the glyph renders properly wherever the font has it, and is stripped
    /// cleanly (whitespace-collapsed) wherever it does not, instead of ever showing tofu.</para>
    ///
    /// <para>Surrogate pairs (astral-plane symbols) are stripped with the same rule — the SDF
    /// text fonts in play are BMP-only. A null label font means "not sampled yet": the text
    /// passes through unjudged, and the per-tick SetLabel re-runs the check once the font
    /// lands (the changed result then re-applies through the caller's change gate).</para>
    /// </summary>
    internal static string SanitizeLabel(TMP_Text? label, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        TMP_FontAsset? font = label != null ? label.font : null;
        if (font == null)
            return text!;

        // Fast path: nothing to strip → hand back the same instance.
        bool dirty = false;
        for (int i = 0; i < text!.Length && !dirty; i++)
        {
            char c = text[i];
            if (!char.IsWhiteSpace(c) && !CanRender(font, c))
                dirty = true;
        }
        if (!dirty)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        var stripped = new System.Text.StringBuilder(8);
        bool pendingSpace = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0; // collapse runs; never lead with whitespace
                continue;
            }
            if (!CanRender(font, c))
            {
                stripped.Append(stripped.Length > 0 ? " U+" : "U+").Append(((int)c).ToString("X4"));
                continue; // dropped — the pendingSpace state is untouched, so "✓ Text" → "Text"
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        string result = sb.ToString();
        if (_sanitizeLogged.Add(text))
            VRLog.Info("WorldUI", $"BUTTON LABEL: stripped un-renderable glyph(s) [{stripped}] from " +
                                  $"'{text}' → '{result}' — font '{font.name}' (incl. fallbacks) has no " +
                                  "outline for them and TMP would draw a hollow box (U+25A1) instead.");
        return result;
    }

    /// <summary>Can the font chain (own fallbacks + TMP global fallbacks) draw this UTF-16 unit?
    /// Surrogate halves are never renderable by a BMP SDF font; the try/catch mirrors
    /// TablePanelSurfaces.PickGlyph (font asset mid-teardown must never break a label).</summary>
    private static bool CanRender(TMP_FontAsset font, char c)
    {
        if (char.IsSurrogate(c))
            return false;
        try
        {
            return font.HasCharacter(c, searchFallbacks: true);
        }
        catch (System.Exception)
        {
            return true; // unjudgeable this frame — keep the character, re-checked next SetLabel
        }
    }

    /// <summary>Drop cached references (hot-reload / scene teardown safe).</summary>
    internal static void Reset()
    {
        _sampled = false;
        _styleLogged = false;
        _mipAsked = 0;
        _mipBaked = 0;
        _normalSprite = _pressedSprite = _disabledSprite = _highlightedSprite = null;
        _font = null;
        _pressedMul = 0.7f;
        _disabledMul = 0.5f;
        _highlightedMul = 1.05f;
        for (int i = 0; i < _created.Count; i++)
        {
            if (_created[i] != null)
                Object.Destroy(_created[i]);
        }
        _created.Clear();
    }
}
