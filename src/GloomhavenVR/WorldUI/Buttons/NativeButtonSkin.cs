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
    /// this exact #FBF3E0, so nothing changes until the user tunes it. Every LOCAL consumer (cluster
    /// caps, board keycaps, docked native captions) reads this property, so a stepper edit
    /// re-colours them all (keycaps on the ButtonTuning.Version rebuild, the cluster per-tick).
    /// A MIRRORED board must NOT read it — see <see cref="LabelOwner"/> and
    /// <see cref="LabelColorFor"/>, which is this same value with the question spelled out.</summary>
    internal static Color LabelColor => LabelColorFor(LabelOwner.ThisViewersOwnDial);

    /// <summary>
    /// <b>WHAT A DEAD CONTROL'S CAPTION IS MULTIPLIED BY — R15, one answer where there were three.</b>
    ///
    /// <para>A disabled cap on the map table dimmed its caption by this factor; a disabled CONFIRM
    /// key on the control board did not dim at all — its <c>tmp.color</c> was written once at build
    /// and never again, so a dead key was a full-brightness gold word on a dark-wood plate, which
    /// reads as a live button. (A harvested uGUI button is the third answer and stays the third
    /// answer: it dims through the game's own <c>Selectable.disabledColor</c>, which is the game's
    /// look and not ours to overwrite.)</para>
    ///
    /// <para>The map table's factor is the reference because it is the one that was actually seen
    /// and accepted on hardware. It multiplies ALPHA as well as RGB, deliberately: a caption on a
    /// dead key is meant to recede, and a carved gold word at full opacity does not recede however
    /// dark you make it. Each family still supplies its OWN base colour — the board's is
    /// <see cref="LabelColor"/> or plain white when the skin never sampled a font, the table's is
    /// always <see cref="LabelColor"/> — because a shared DIM is not a shared colour.</para>
    /// </summary>
    internal const float DisabledLabelDim = 0.45f;

    /// <summary>
    /// WHOSE label colour a call site is asking for — the question <see cref="LabelColor"/> on its
    /// own cannot express, and the one the mirrored DECISION row answered wrongly for its whole
    /// shipped life.
    ///
    /// <para>THE DEFECT, STATED SO IT CANNOT COME BACK. <see cref="LabelColor"/> is THIS client's
    /// <c>[ButtonColors] LabelR/G/B</c>. On the owner's own dock
    /// (<c>WorldUI.Surfaces.DecisionDockSurface.AdjustDockedRow</c>) that is correct BY DEFINITION —
    /// this client IS the owner there. On a MIRRORED board it is not, and three sites read it
    /// anyway: <c>Net.RemoteDecisionWidgets.Apply</c> (the cloned game widgets),
    /// <c>Net.RemoteBoardFurniture.SetDecisionLines</c> and its <c>BaseLabelGold</c> (the mod-drawn
    /// plate fallback). A player who set their labels to red therefore saw red lettering on every
    /// team-mate's decision buttons while each of those team-mates saw parchment on their own — the
    /// 1:1 ruling broken in the direction nobody looks for: not "the owner's tuning is missing" but
    /// "the VIEWER's tuning has leaked onto somebody else's board".</para>
    ///
    /// <para>IT IS THE SAME DEFECT THE KEYCAPS ALREADY CLOSED, one screen away — see the second
    /// <see cref="StyleEngravedLabel(TMP_Text, Color, float, bool, bool)"/> overload, whose note
    /// describes this exact leak for the engraved KEYLINE while the FILL on the decision row went
    /// on reading the viewer's dial. Nothing was ever missing but a way for a call site to SAY
    /// which of the two colours it means: the owner's has been on the wire the whole time
    /// (<c>Net.NetProtocol.TuneLabelColor</c>, id 48 → <c>Net.RemoteBoardTuning.LabelColor</c>) and
    /// the mirrored keycaps beside these plates were already lettered out of it.</para>
    ///
    /// <para>An ENUM and not a bool, for the reason <c>Cards.CardDustFx.Permission</c> records: the
    /// two are different QUESTIONS with different owners, and a boolean at a call site reads as
    /// neither of them.</para>
    /// </summary>
    internal enum LabelOwner
    {
        /// <summary>
        /// "What do MY OWN buttons letter in?" — this client's <c>[ButtonColors] LabelR/G/B</c> is
        /// the whole answer, and this class asks it. Every local face means this: the cluster caps,
        /// the tray caps, the map rail, the options rows, and the owner's own docked decision row.
        ///
        /// <para>It is the ZERO member deliberately, so <c>default(LabelOwner)</c> — and any future
        /// call site with no peer colour to hand — resolves to the LOCAL answer. A label can then
        /// only ever come out in the colours of the board it is standing on: an answer that is
        /// right on every local surface and merely stale on a mirror, where the opposite default
        /// would letter a LOCAL button out of a peer's palette, which is right nowhere.</para>
        /// </summary>
        ThisViewersOwnDial = 0,

        /// <summary>
        /// "What does the OWNER of the board this label is drawn on letter in?" — already answered
        /// by the caller, off that owner's record-28 <c>LabelColor</c>, before it got here. This
        /// client's own dial is not consulted and MUST NOT be: it answers a question about this
        /// client's OWN buttons. The only callers are the remote mirrors.
        /// </summary>
        TheBoardOwnersDial,
    }

    /// <summary>
    /// <see cref="LabelColor"/> with the CALL SITE naming whose dial it means — the sibling the
    /// mirror needed (see <see cref="LabelOwner"/>). <paramref name="ownersLabelColor"/> is that
    /// board owner's own <c>[ButtonColors] LabelR/G/B</c> off the wire; it is read ONLY for
    /// <see cref="LabelOwner.TheBoardOwnersDial"/>, and its ALPHA is discarded exactly as
    /// <c>ButtonTuning.LabelColor</c>'s is hardwired opaque — a label's alpha belongs to the label
    /// (the mirrors fade their own), never to the palette. Naming that member and handing it no
    /// colour yields a BLACK label rather than a quietly wrong one: a caller that only half
    /// answered the question failing loudly is the whole point of making it answer.
    ///
    /// <para>THERE IS NO <c>HasFont</c> LADDER HERE, AND THAT IS A SECOND FIX RATHER THAN AN
    /// OMISSION. Each of the three mirror sites fell back to a hardcoded
    /// <c>new Color(0.91f, 0.82f, 0.62f)</c> for as long as <see cref="HasFont"/> was false, while
    /// the owner's own dock has no such gate and letters in <see cref="LabelColor"/> (shipped
    /// 0.984/0.953/0.878) from its first frame. So until this client had harvested a game button,
    /// every mirrored plate stood in a duller gold than the plate it mirrors — transient, once per
    /// session, and wrong for every pair of players who never touched the dial. The colour never
    /// depended on sampling at all: it is a config bind on one side and a wire field on the other,
    /// and both are known before anything is harvested. (The KEYCAPS' own
    /// <c>HasFont ? Fill : white</c> ladder in <c>Net.RemoteBoardFurniture.InertCap.BuildLabel</c>
    /// is a different rule and stays: it reproduces the ladder the LOCAL cap runs, so the two
    /// boards agree in the un-skinned case as well as the skinned one.)</para>
    /// </summary>
    internal static Color LabelColorFor(LabelOwner whose, Color ownersLabelColor = default) =>
        whose == LabelOwner.TheBoardOwnersDial
            ? new Color(ownersLabelColor.r, ownersLabelColor.g, ownersLabelColor.b, 1f)
            : ButtonTuning.LabelColor;

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

    /// <summary>
    /// The game's own PRESS dimming, as a bare factor — the twin of <see cref="HighlightMul"/>.
    ///
    /// <para>ADDED BECAUSE THE OBVIOUS ROUTE HAD A HOLE, and the receiver lane found it rather than
    /// papering over it. A caller with no <c>Selectable</c> of its own could already reach this
    /// number through <c>ColorFor(FaceState.Pressed).grayscale</c> — but that method returns WHITE
    /// when the harvested skin ships a real SpriteSwap pressed sprite, because a native face then
    /// shows its press through the SPRITE and not through a tint. A mirrored plate never swaps
    /// sprites, so on such a skin it would have shown no press at all. This exposes the factor
    /// itself, which is what a tint-only consumer actually needs.</para></summary>
    internal static float PressedMul => EnsureSampled() ? _pressedMul : 0.7f;

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
    /// instance this writes to). Deliberately NOT part of ApplyFont itself, and that has NOT
    /// changed: ApplyFont is shared by every mod label in the game and folding a relief recipe
    /// into it would style things nobody has looked at.
    ///
    /// <para><b>WHAT DID CHANGE (2026-09-03, user request 5).</b> This comment used to name "the
    /// quest block" among the labels that "must stay un-outlined". That was never a rule about the
    /// quest block, it was a description of who happened to call ApplyFont — and it read as a
    /// standing ruling for long enough that the battle-goal text shipped bare over a forest floor,
    /// where the worst ground it crosses drops it to 2.24:1 — under the 3:1 large-text floor. The
    /// rule is still "ApplyFont does not outline"; labels that need relief
    /// now OPT IN by name. For a label that floats over the SCENE rather than over a cap, the
    /// opt-in is <see cref="StyleWorldReadableLabel"/>, not this one.</para>
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
        // CONTRAST FIX (user #3): a fully-opaque, thicker dark keyline. On a LIGHT brass/
        // parchment cap the old 0.10-wide, 85%-alpha umber outline barely registered, so the
        // bright glyphs and the bright cap merged; a full-opacity 0.20 rim now clearly rings
        // every letter and reads against both light and dark caps (paired with the brightened
        // LabelColor fill). Still a dark umber, not black — the carved-engraving look holds.
        // USER DEBUG OPTION: colour/width from the live [ButtonColors] binds, and the outline is
        // gated on the LabelOutline toggle (OFF → width 0 + keyword off = a flat label). Applied on
        // the keycap rebuild that a ButtonColors edit triggers (ButtonTuning.Version), so toggling
        // it live takes effect on the next re-skin.
        //
        // A soft dark drop-shadow underlay sells the carved depth AND adds a second contrast
        // cue on light caps (a shaded halo below/right of the glyphs). Darkened and its alpha
        // raised (0.55 → 0.70) so it holds on a bright brass cap (UNDERLAY_ON gates the pass).
        // User debug option: gated on the LabelUnderlay toggle (OFF → keyword off = no shadow).
        if (!ApplySdfRelief(label, outlineColor, outlineWidth, outlineOn,
                            underlayOn, CapUnderlayColor, CapUnderlaySoftness, CapUnderlayOffset))
            return;

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

    // ------------------------------------------------------------------ world-readable labels --

    /// <summary>The keycap recipe's underlay: a warm near-black shadow, soft and offset down-RIGHT,
    /// because a cap label sits PROUD of its cap and a proud thing casts its shadow away from
    /// BoardLit's baked key. Named rather than inline so the world-label recipe below can differ
    /// from it deliberately instead of by a copy nobody compared.</summary>
    private static readonly Color CapUnderlayColor = new(0.05f, 0.03f, 0.02f, 0.70f);

    private const float CapUnderlaySoftness = 0.35f;

    private static readonly Vector2 CapUnderlayOffset = new(0.30f, -0.30f);

    /// <summary>
    /// The keyline for a label that floats over the SCENE — near-black and fully opaque.
    ///
    /// <para><b>WHY NEAR-BLACK AND NOT THE KEYCAP'S UMBER.</b> A cap label has a known background:
    /// its own cap, whose colour this mod chose. A HUD label hanging beside the board has no
    /// background at all — it has whatever the scenario put behind it, which in the scene this was
    /// measured from is a black forest AND pale lit rock within the same line of text. Against the
    /// rock the umber keyline (0.5, 0.5, 0.5) is worth 1.9:1; near-black is worth 7.3:1. The fill
    /// keeps the light parchment, so on the black half nothing changes at all.</para>
    /// </summary>
    private static readonly Color WorldLabelKeyline = new(0.05f, 0.04f, 0.03f, 1f);

    /// <summary>Keyline width for a world label, in SDF range. Wider than the keycap's 0.20 because
    /// the glyphs it has to ring are a THIN SERIF at HUD size seen through a headset's per-eye
    /// resolution: at 0.20 the rim survives on the cap (a large glyph on a known ground) and gets
    /// eaten by the scene here. 0.26 is the widest the shipped TMP SDF material renders without the
    /// counters of 'e'/'a' filling in at this point size — checked against the quest block's own
    /// solved font size, not chosen for roundness.</summary>
    private const float WorldLabelKeylineWidth = 0.26f;

    /// <summary>A world label's drop shadow: pure black, near-opaque, tight and cast DOWN-RIGHT.
    /// Tighter than the cap's (0.25 vs 0.35 softness, 0.22 vs 0.30 offset) because it is doing a
    /// different job — on a cap the shadow sells depth, here it is a second, lower-frequency dark
    /// ground under the glyph for the case where the keyline alone is not enough (a scene feature
    /// whose own edge runs along a stroke).</summary>
    private static readonly Color WorldLabelUnderlayColor = new(0f, 0f, 0f, 0.85f);

    private const float WorldLabelUnderlaySoftness = 0.25f;

    private static readonly Vector2 WorldLabelUnderlayOffset = new(0.22f, -0.22f);

    private static bool _worldStyleLogged;

    /// <summary>
    /// LEGIBILITY RELIEF FOR A LABEL THAT FLOATS OVER THE SCENE — light parchment fill, an opaque
    /// near-black SDF keyline, and a tight black drop shadow. NO PLATE.
    ///
    /// <para><b>THE REQUEST</b> (user, 2026-09-03): <i>"Gewährleiste, dass der Text lesbar ist auf
    /// den Boards, indem du die Schriftfarbe entsprechend wählst. Es soll immersiv sein weiterhin
    /// und gut aussehen, aber lesbar sein! Siehe text-board.jpg, dort sieht du das der Text im
    /// Hintergrund untergeht."</i></para>
    ///
    /// <para><b>THIS IS THE SECOND HALF OF REQUEST 5, AND THE SMALLER ONE.</b> The user said "auf
    /// den Boards", and the text literally cut INTO the boards was far worse — 1.64–1.96:1, fixed
    /// by deepening the carve in <c>Cards.Caps.BoardEngraving</c>. This is the floating half: the
    /// battle goal, the game's objectives block, the item-use caption and the peer readouts, which
    /// hang beside the board with the scenario behind them.</para>
    ///
    /// <para><b>WHY A COLOUR ALONE CANNOT DO IT, MEASURED OFF HIS OWN SCREENSHOT, AND THE HONEST
    /// SIZE OF IT.</b> Sampling text-board.jpg along those lines WITH THE GLYPHS MASKED OUT (a
    /// threshold plus a 3 px dilation, so no antialiased edge pixel is counted as bright ground —
    /// the first pass of this measurement did count them and overstated the defect by an order of
    /// magnitude): the ground is near-black for the great majority of every line (p50 L=0.0003 to
    /// L=0.0008, fill 17.1–17.3:1), and the WORST ground the strokes cross is L=0.3365 on quest
    /// line 2 and L=0.2586 on line 3. The rendered cream fill measures L=0.8165. So the fill falls
    /// to <b>2.24:1</b> and <b>2.81:1</b> at those points — under the 3:1 floor for large text —
    /// while the same line reads at 17:1 two words away. The defect is LOCAL, and that is precisely
    /// why no fill colour fixes it: darker wins the rock and loses the litter, lighter the reverse.
    /// It is a RELIEF problem, not a palette problem.</para>
    ///
    /// <para><b>WHAT THIS BUYS, IN THE SAME UNITS.</b> The keyline is measured against the
    /// backgrounds, not against the fill: near-black over the L=0.3365 stone is <b>7.26:1</b> where
    /// the fill was 2.24:1, and over the L=0.2586 stone <b>5.80:1</b> where the fill was 2.81:1 —
    /// so every stroke carries at least one edge above the floor everywhere in that screenshot,
    /// and the black-litter majority is untouched at 17:1. The second reason, which a screenshot
    /// cannot measure: at this angular size through a Quest 3 the serif strokes are one to two
    /// pixels PER EYE, and a dark rim is what gives them an edge that survives the panel and the
    /// reprojection. That half is a hardware question, not a claim made here.</para>
    ///
    /// <para><b>AND NOT A PLATE.</b> <i>"Es soll immersiv sein weiterhin und gut aussehen."</i> A
    /// dark quad behind the text is the crude answer and this project has already ruled against it
    /// twice — the round readout's backing plate was deleted on the user's own instruction ("nativ
    /// und immersiv in dem board verarbeitet, nicht einfach als schwebender Text darüber"), and
    /// <c>Cards.Caps.BoardEngraving</c> carries "NO BACKING PLATE, EVER" in its class doc. This
    /// adds no geometry and no second draw: it is three properties on the label's own font material
    /// instance, which is the same mechanism every keycap caption on the board already uses. The
    /// fill colour, the font and the size are untouched, so the look is the shipped look plus the
    /// shadow an inscription would have cast anyway.</para>
    ///
    /// <para><b>THIS IS NOT PART OF <see cref="ApplyFont"/>.</b> Labels opt in by name — see the
    /// note on <see cref="StyleEngravedLabel(TMP_Text)"/>. Idempotent, safe to re-apply after any
    /// font (re)assignment, and a clean no-op on a TMP shader variant with no outline pass.</para>
    /// </summary>
    internal static void StyleWorldReadableLabel(TMP_Text label)
    {
        if (!ApplySdfRelief(label, WorldLabelKeyline, WorldLabelKeylineWidth, outlineOn: true,
                            underlayOn: true, WorldLabelUnderlayColor, WorldLabelUnderlaySoftness,
                            WorldLabelUnderlayOffset))
            return;

        if (_worldStyleLogged)
            return;
        _worldStyleLogged = true;
        // HW-VERIFY
        VRLog.Note("WorldUI", "WORLD-LABEL LEGIBILITY (user request 5, 'der Text im Hintergrund " +
            "untergeht'): the battle goal, the scenario objectives block, the item-use caption and " +
            "the peer-board readouts now carry an opaque near-black SDF keyline RGBA(" +
            $"{WorldLabelKeyline.r:F2},{WorldLabelKeyline.g:F2},{WorldLabelKeyline.b:F2}," +
            $"{WorldLabelKeyline.a:F2}) at width {WorldLabelKeylineWidth:F2} plus a black underlay " +
            $"at alpha {WorldLabelUnderlayColor.a:F2}, softness {WorldLabelUnderlaySoftness:F2}, " +
            $"offset ({WorldLabelUnderlayOffset.x:F2}, {WorldLabelUnderlayOffset.y:F2}). NO PLATE " +
            "and NO fill-colour change. Measured off text-board.jpg: that text crosses ground " +
            "running from L=0.0003 (black litter, fill 17.2:1) to L=0.3365 (lit stone, fill " +
            "2.24:1, under the 3:1 floor) WITHIN ONE LINE, so no single fill colour can serve " +
            "both; the keyline is 7.26:1 against that stone. The BOARD's own engraved captions " +
            "were the larger half of the same request and are fixed separately — see BOARD " +
            "ENGRAVING. If the tester reports this text still sinking, the field that decides it " +
            "is WHICH ground it sinks against: dark means the fill is the term to change, pale " +
            "means the keyline is not thick enough at that angular size.");
    }

    /// <summary>
    /// The SDF relief writes both label recipes share — outline colour/width + keyword, and the
    /// underlay colour/softness/offset + keyword — on the label's own per-instance font material.
    ///
    /// <para>Extracted so the two recipes differ only in their NUMBERS. They were one copied block
    /// for exactly as long as there was one recipe; the moment a second appeared, the shipped
    /// version of "which properties does a TMP relief consist of" would have had two homes and the
    /// keyword gating (<c>OUTLINE_ON</c> / <c>UNDERLAY_ON</c>, which mobile TMP variants need and
    /// desktop ones ignore) would have been the half that got missed in the copy.</para>
    ///
    /// <para>Returns false when there is nothing to write to yet — no font, so no material instance
    /// — which is the caller's cue that its own log line has not earned the right to claim
    /// anything. Every property is probed, so a TMP shader variant without an outline or underlay
    /// pass is a clean no-op rather than an exception.</para>
    /// </summary>
    private static bool ApplySdfRelief(TMP_Text label, Color outlineColor, float outlineWidth,
                                       bool outlineOn, bool underlayOn, Color underlayColor,
                                       float underlaySoftness, Vector2 underlayOffset)
    {
        if (label == null || label.font == null)
            return false; // no font yet — the caller re-applies fonts late, restyle then
        Material mat = label.fontMaterial; // per-label instance (never the shared asset)
        if (mat == null)
            return false;
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
        if (mat.HasProperty("_UnderlayColor"))
        {
            if (underlayOn)
            {
                mat.SetColor("_UnderlayColor", underlayColor);
                if (mat.HasProperty("_UnderlaySoftness"))
                    mat.SetFloat("_UnderlaySoftness", underlaySoftness);
                if (mat.HasProperty("_UnderlayOffsetX"))
                    mat.SetFloat("_UnderlayOffsetX", underlayOffset.x);
                if (mat.HasProperty("_UnderlayOffsetY"))
                    mat.SetFloat("_UnderlayOffsetY", underlayOffset.y);
                mat.EnableKeyword("UNDERLAY_ON");
            }
            else
            {
                mat.DisableKeyword("UNDERLAY_ON");
            }
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="label"/> already wears the world-readable relief, read off the
    /// material rather than off a bookkeeping set.
    ///
    /// <para><b>WHY IT READS THE PICTURE AND NOT A FLAG.</b> The callers that need this are sweeping
    /// a subtree the GAME owns and rebuilds — the objectives panel — so any set of "labels I have
    /// styled" is a set of instance ids that go stale silently on every rebuild and leak across
    /// scene loads. The material's own <c>_OutlineWidth</c> cannot go stale: if it reads back as
    /// this recipe's width, this recipe is what is on screen.</para>
    /// </summary>
    internal static bool HasWorldReadableRelief(TMP_Text? label)
    {
        if (label == null || label.font == null)
            return false;
        Material mat = label.fontMaterial;
        return mat != null
               && mat.HasProperty("_OutlineWidth")
               && Mathf.Abs(mat.GetFloat("_OutlineWidth") - WorldLabelKeylineWidth) < 1e-4f;
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
    internal static string SanitizeLabel(TMP_Text? label, string? text) =>
        SanitizeLabel(label, text, out _);

    /// <summary>
    /// <see cref="SanitizeLabel(TMP_Text?, string?)"/>, also reporting how many TMP rich-text
    /// tags the raw string carried. THE TAG STRIP RUNS FIRST AND UNCONDITIONALLY — before the
    /// font check, because a tag is un-renderable on a keycap whatever the font: the game's
    /// pick-dialog wording <c>&lt;sprite name="LOST"&gt; Verbrennen '…'</c> reached this seam
    /// verbatim and the cap printed the tag as text (user screenshot buttontext.jpg; the
    /// mechanism and why the glyph is stripped rather than rendered are in
    /// <see cref="RichTextTags"/>). Both the owner's <c>BoardButton.SetLabel</c> and the peer
    /// mirror's <c>InertCap.SetLabel</c> come through here, so the two boards agree on the
    /// cleaned wording by construction (MP 1:1 rule).
    /// </summary>
    internal static string SanitizeLabel(TMP_Text? label, string? text, out int tags)
    {
        tags = 0;
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        string raw = text!;
        text = RichTextTags.Strip(raw, out tags);
        if (tags > 0)
            LogStrippedTags(raw, text, tags, LabelOwnerName(label));
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

    /// <summary>Distinct raw strings whose tags were reported this session — the change gate of
    /// the KEYCAP LABEL line, capped at <see cref="TagLogCap"/> so a dialog whose wording
    /// interpolates a card title cannot turn the line into a census.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _tagLogged = new();

    /// <summary>How many KEYCAP LABEL lines one session may print.</summary>
    private const int TagLogCap = 6;

    /// <summary>
    /// KEYCAP LABEL — the one line that says a game string carrying rich-text tags reached a
    /// mod-owned label, what it carried and what the player reads instead. Change-gated on the
    /// raw string and capped per session; a session with no such line had no tagged label.
    /// Shared by the keycap seam (<see cref="SanitizeLabel(TMP_Text?, string?, out int)"/>) and
    /// the board engravings (<c>Cards.BoardEngraving.SetText</c>).
    /// </summary>
    internal static void LogStrippedTags(string raw, string clean, int tags, string where)
    {
        if (_tagLogged.Count >= TagLogCap || !_tagLogged.Add(raw))
            return;
        // HW-VERIFY: the answer to "does the key still print a <sprite> tag?" is read off this
        // line — the raw game string beside the cleaned one — so it must print at the DEFAULT
        // log level (Note). scripts/check-hw-verify.py enforces it.
        VRLog.Note("WorldUI", $"KEYCAP LABEL: game string '{RichTextTags.Escape(raw)}' on '{where}' " +
                              $"carried {tags} rich-text tag(s) — the label reads '{clean}' " +
                              "(strategy STRIPPED: the mod label is a bare TextMeshPro with no sprite " +
                              "asset, so TMP would draw a <sprite> tag as literal text, and a keycap " +
                              "wears one engraved style that a colour/size tag would override). " +
                              $"Line {_tagLogged.Count}/{TagLogCap} of this session.");
    }

    /// <summary>The label's owner for the KEYCAP LABEL line: up to two ancestors and the label,
    /// e.g. <c>BoardButton_BESTÄTIGEN/Cap/Label</c>.</summary>
    private static string LabelOwnerName(TMP_Text? label)
    {
        if (label == null)
            return "<no label>";
        Transform t = label.transform;
        string name = t.name;
        for (int i = 0; i < 2 && t.parent != null; i++)
        {
            t = t.parent;
            name = t.name + "/" + name;
        }
        return name;
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
