using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board;

/// <summary>
/// THE ONE implementation of the character-focus outlines, so the three places they appear — the
/// control board (local and every peer's), the initiative-track entry, and the Steam avatar next
/// to a board — can never drift apart in colour, brightness or blink rate. Callers pick a
/// <see cref="FocusTurnMark"/>; every visual decision is made here.
///
/// <para>WHY TWO RENDERERS AND ONE COLOUR SOURCE: the initiative track is uGUI (the game's own
/// canvas, adopted into world space by the initiative surface), while the control boards and the
/// Steam-avatar quads are plain world-space meshes. There is no single Unity primitive that
/// serves both, so there are two thin builders — <see cref="UiRing"/> and
/// <see cref="WorldFrame"/> — but exactly ONE colour/alpha function
/// (<see cref="Tint"/>) feeding both. Change the palette here and all three move together.</para>
///
/// <para>THE BLINK. <see cref="BlinkHz"/> is 0.667 Hz (a 1.5 s period), deliberately the SAME
/// period the mod's existing attention cues already breathe at (<c>SoftFramePulse</c>,
/// <c>InitiativeSelectionGlow</c>, <c>SelectionReadyHighlighter</c>) so a new cue reads as part
/// of the same language rather than as a competing flicker. It is also far below the ~3 Hz
/// photosensitivity guideline — a strobe strapped to the face is not an acceptable way to say
/// "wrong character". Alpha swings between <see cref="MinAlpha"/> and <see cref="MaxAlpha"/>
/// rather than to zero, so the outline never fully disappears mid-glance. The clock is
/// <see cref="Time.unscaledTime"/>: every cue in the scene is driven by the same global clock, so
/// they blink IN PHASE, and they keep blinking while the game pauses simulation time during a
/// camera move or a modal.</para>
///
/// <para>THE COLOURS. Green = "you are looking at the character you have to play"; red = "you own
/// the character at turn but you are looking at somebody else". They are the only two states that
/// ever blink; the plain focus ring (<see cref="SelectionTint"/>, a cool blue-white — the same
/// hue as the mod's existing [VR] badge) is STEADY, so "which character am I looking at" and
/// "something needs your attention" are distinguishable without reading a colour at all. That
/// matters for red/green colour blindness: motion, not hue, carries the urgent bit.</para>
///
/// <para><b>MIXED REALITY — WHY GREEN WAS INVISIBLE, AND WHAT REPLACES IT.</b> User report
/// 2026-08-08: "Mixed-Reality-Mode 'grün' ist nicht sichtbar". It was not dim, it was REPLACED BY
/// THE ROOM. In MR the whole background clears to a flat chroma key that Virtual Desktop swaps for
/// passthrough — READ FROM THE LOG: <c>"[Core] Mixed reality ON — … keyed to Green (RGBA
/// 0,1,0,1)"</c>, and READ FROM SOURCE: <c>Defaults.KeyColor = (0,1,0,1)</c>, the first
/// <c>MixedReality.Presets</c> entry. The board frame sits just OFF the board, i.e. over that key,
/// and it was drawn ALPHA-BLENDED at 0.35–0.95. Blending the green cue over the green key lands the
/// FINAL pixel inside the keyer's tolerance — at the blink trough
/// <c>0.35·(0.30,0.85,0.38) + 0.65·(0,1,0) = (0.11,0.95,0.13)</c>, which the mod's own
/// key-proximity rule (<c>MixedReality.NearKey</c>, 0.25 per channel) calls key. That is exactly
/// the failure <c>MixedReality.cs</c> already documents for translucent surfaces over the key
/// ("they ALTER the green tone and VD can no longer key it properly"); the compositor keys FINAL
/// pixels, so a semi-transparent green cue over a green sky is deleted, not darkened.</para>
///
/// <para>BE HONEST ABOUT WHAT IS MEASURED AND WHAT IS REASONED. The key colour is read (log +
/// <c>Defaults</c>); the blend arithmetic above is arithmetic. What is INFERRED is the keyer's
/// tolerance: at the blink CREST the blended pixel is (0.29,0.86,0.36), which clears the mod's own
/// conservative 0.25 radius — so under a strict RGB-distance keyer the cue should have flickered in
/// and out rather than vanished. Virtual Desktop keys on CHROMA similarity, where a desaturated
/// green still reads as the key hue at any brightness, and its tolerance is a user slider. Both
/// readings point the same way and the fix answers both: the MR cue is neither translucent nor
/// green, so neither the blend nor the hue can put it near the key.</para>
///
/// <para>The MR variant therefore changes three things at once, and NOTHING outside MR:
/// <list type="number">
/// <item><b>It stops being translucent.</b> Alpha is pinned to 1 and the blink modulates
///   BRIGHTNESS instead. A fully opaque pixel cannot be dragged toward the key by the background it
///   covers — this alone removes the mechanism, on all three carriers at once.</item>
/// <item><b>It leaves the key's neighbourhood.</b> Green→<see cref="MrCorrectTint"/> (a cool
///   white), red→<see cref="MrWrongTint"/> (amber). Both are far from every preset key (green,
///   magenta, blue, black), and every colour is then run through <see cref="KeySafe"/> against the
///   LIVE key value, so a custom or cycled key cannot swallow the cue either. The pair also beats
///   green/red for red-green colour blindness: white vs amber differs in luminance AND saturation,
///   not only in hue.</item>
/// <item><b>Motion stays the urgent channel, and carries MORE.</b> Both at-turn states keep the
///   0.667 Hz blink in the same phase as every other cue, but at different AMPLITUDES: "wrong"
///   pumps hard (<see cref="MrUrgentMinLevel"/>→1), "correct" merely breathes
///   (<see cref="MrCalmMinLevel"/>→1), and the steady focus ring does neither. The three states are
///   therefore distinguishable by movement alone, with the colour as reinforcement rather than as
///   the only channel.</item>
/// </list>
/// The board outline additionally gains a wider dark KEYLINE under the coloured rim
/// (<see cref="OutlineKeylineTint"/>): it is the one carrier that sits against the real room, and a
/// white rim on a white wall is as invisible as a green one on green. The other two carriers sit on
/// mod-drawn board geometry and need no backing.</para>
///
/// <para>WHY THE MR SWITCH LIVES HERE AND NOWHERE ELSE: all three carriers — the control boards,
/// the Steam-avatar ring and the initiative-track rings, local and mirrored — read their colour
/// from <see cref="Tint"/>, <see cref="SelectionRingTint"/> and <see cref="AtTurnRingTint"/>. One
/// branch in this file is what makes it IMPOSSIBLE for them to disagree about which mode they are
/// in; there is no second toggle and no per-carrier MR code.</para>
/// </summary>
internal static class FocusCue
{
    /// <summary>Blinks per second of the turn outlines — a 1.5 s period, matching every other
    /// attention cue in the mod (see the class doc) and well under the 3 Hz photosensitivity
    /// guideline.</summary>
    internal const float BlinkHz = 1f / 1.5f;

    /// <summary>Trough of the blink. Non-zero on purpose: the outline stays legible for a glance
    /// that lands in the dark half of the cycle.</summary>
    internal const float MinAlpha = 0.35f;

    /// <summary>Crest of the blink.</summary>
    internal const float MaxAlpha = 0.95f;

    /// <summary>Steady alpha of the (non-blinking) plain focus ring.</summary>
    internal const float SelectionAlpha = 0.85f;

    /// <summary>Peak of the breathing SCALE pulse that rides the blink (1 = authored size). The
    /// selection-phase ring's value, so a blinking outline and the mod's existing cues swell by
    /// the same amount.</summary>
    internal const float ScalePulseAmount = 0.05f;

    /// <summary>"You are on the right character" — a clearly saturated green that still reads on
    /// the warm parchment/oak the boards are made of.</summary>
    internal static readonly Color CorrectTint = new(0.30f, 0.85f, 0.38f);

    /// <summary>"Wrong character while it is your turn" — a warm red, not the orange the mod
    /// already spends on ordinary highlights.</summary>
    internal static readonly Color WrongTint = new(0.92f, 0.26f, 0.24f);

    /// <summary>The plain "this is the character I am looking at" ring: cool blue-white, the same
    /// hue as the [VR] peer badge, so it never collides with the warm amber the selection-phase
    /// cue already owns.</summary>
    internal static readonly Color SelectionTint = new(0.56f, 0.85f, 1f);

    /// <summary>"This character is at turn" when the local player has no stake in it (a teammate's
    /// or a monster's turn): warm gold, STEADY. It states a fact; only the two states that need
    /// the LOCAL player to do something ever blink.</summary>
    internal static readonly Color AtTurnTint = new(1f, 0.82f, 0.35f);

    /// <summary>Steady alpha of the neutral at-turn ring.</summary>
    internal const float AtTurnAlpha = 0.9f;

    /// <summary>The blink's 0..1 phase, shared by every cue so they pulse together.</summary>
    internal static float Phase =>
        0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI * BlinkHz));

    // ------------------------------------------------------------------------- mixed reality --

    /// <summary>
    /// True while the game is composited against the chroma key (see the class doc). Read from
    /// <c>MixedReality.BackingsWanted</c>, which IS the mode's own want condition — "the SAME want
    /// condition Tick keys the whole mode off" — so the cue can never be in a different mode than
    /// the sky it is drawn against, and there is no second toggle to fall out of sync. It also
    /// answers false before the config is bound, which is what makes reading
    /// <c>MixedReality.KeyColor.Value</c> below safe.
    /// </summary>
    internal static bool MrActive => MixedReality.BackingsWanted;

    /// <summary>MR replacement for <see cref="CorrectTint"/>: a cool white. Far from every chroma
    /// preset (green / magenta / blue / black) on at least one channel by a wide margin, and the
    /// brightest thing available — the "you are on the right character" state should read as calm
    /// and clean, not as a second warning.</summary>
    internal static readonly Color MrCorrectTint = new(0.88f, 0.97f, 1f);

    /// <summary>MR replacement for <see cref="WrongTint"/>: amber. Red is a poor MR colour twice
    /// over — it is the room's most common accent, and red-vs-green is the one pair colour blindness
    /// destroys. Amber against a near-white partner separates by LUMINANCE and saturation as well
    /// as hue, and no chroma preset is anywhere near it.</summary>
    internal static readonly Color MrWrongTint = new(1f, 0.58f, 0.06f);

    /// <summary>MR replacement for <see cref="SelectionTint"/> — the same cool blue-white identity,
    /// pushed a little more saturated so it still separates from <see cref="MrCorrectTint"/> once
    /// both are opaque.</summary>
    internal static readonly Color MrSelectionTint = new(0.32f, 0.78f, 1f);

    /// <summary>MR replacement for <see cref="AtTurnTint"/> (a teammate's / a monster's turn). Kept
    /// gold: it is already key-safe, and this state must NOT compete with the two that ask the local
    /// player for something.</summary>
    internal static readonly Color MrAtTurnTint = new(1f, 0.80f, 0.30f);

    /// <summary>The dark keyline drawn UNDER the board outline's coloured rim in MR. Nearly black
    /// but not black, so it survives <see cref="KeySafe"/> under the Black preset too.</summary>
    internal static readonly Color MrKeylineTint = new(0.05f, 0.04f, 0.07f);

    /// <summary>Blink floor of the CALM MR state ("at turn, right character"): a shallow breath.</summary>
    internal const float MrCalmMinLevel = 0.70f;

    /// <summary>Blink floor of the URGENT MR state ("at turn, wrong character"): a hard pump, so the
    /// two states differ in MOVEMENT and not only in colour. Still far from zero — the rim never
    /// disappears mid-glance, the same rule <see cref="MinAlpha"/> follows.</summary>
    internal const float MrUrgentMinLevel = 0.35f;

    /// <summary>Per-channel distance a cue colour must keep from the live chroma key. Deliberately
    /// wider than <c>MixedReality</c>'s own 0.25 <c>NearKey</c> radius: that radius is the mod's
    /// estimate of what a compositor MIGHT swallow, and a cue that only just clears it would be at
    /// the mercy of the user's tolerance slider in Virtual Desktop.</summary>
    private const float MrKeyMargin = 0.30f;

    /// <summary>
    /// Push <paramref name="c"/> out of the live chroma key's neighbourhood, if it is in it.
    /// A no-op outside MR and a no-op for a colour that is already clear on any one channel (the
    /// keyer's own test is a per-channel AND). Otherwise the channel with the most headroom is
    /// driven past <see cref="MrKeyMargin"/>, which is always possible because the presets sit at
    /// the cube's corners. Alpha is never touched.
    /// </summary>
    internal static Color KeySafe(Color c)
    {
        if (!MrActive)
            return c;
        Color key = MixedReality.KeyColor.Value;
        if (Mathf.Abs(key.r - c.r) >= MrKeyMargin || Mathf.Abs(key.g - c.g) >= MrKeyMargin
            || Mathf.Abs(key.b - c.b) >= MrKeyMargin)
            return c;

        // Headroom of each channel toward the far end of ITS key value; take the best one.
        float tr = key.r < 0.5f ? 1f : 0f, tg = key.g < 0.5f ? 1f : 0f, tb = key.b < 0.5f ? 1f : 0f;
        float hr = Mathf.Abs(tr - c.r), hg = Mathf.Abs(tg - c.g), hb = Mathf.Abs(tb - c.b);
        if (hr >= hg && hr >= hb)
            c.r = Escape(c.r, key.r);
        else if (hg >= hb)
            c.g = Escape(c.g, key.g);
        else
            c.b = Escape(c.b, key.b);
        return c;
    }

    private static float Escape(float v, float k) =>
        Mathf.Clamp01(k < 0.5f ? Mathf.Max(v, k + MrKeyMargin * 1.2f)
                               : Mathf.Min(v, k - MrKeyMargin * 1.2f));

    // ------------------------------------------------------------------ the board outline's rim --

    /// <summary>Rim width of the board outline in board-LOCAL metres — how far the inverted hull is
    /// pushed out along the board's own normals, i.e. how proud of the silhouette the outline
    /// stands. 6 mm on a 0.64 × 0.32 m board: it reads across the table without looking like
    /// furniture, the brief the old 12 mm bars were written to.</summary>
    private const float RimLocal = 0.006f;

    /// <summary>The MR rim is wider. It competes with a real room instead of with a dark void, and
    /// it is the only mode where the cue may have to be seen out of the corner of an eye.</summary>
    private const float MrRimLocal = 0.010f;

    /// <summary>The MR keyline sits outside the rim by another notch, so a dark border of ~6 mm
    /// remains visible around the coloured band whatever the room behind it is doing.</summary>
    private const float MrKeylineLocal = 0.016f;

    /// <summary>Live rim width of the board outline (board-local metres).</summary>
    internal static float OutlineRimExtrudeLocal => MrActive ? MrRimLocal : RimLocal;

    /// <summary>Live keyline width of the board outline (board-local metres). Only read while
    /// <see cref="OutlineKeylineTint"/> is non-null.</summary>
    internal static float OutlineKeylineExtrudeLocal => MrKeylineLocal;

    /// <summary>The dark contrast backing under the board outline's coloured rim, or null outside
    /// MR (where the cue is drawn against the game's own dark scene and needs none). STEADY on
    /// purpose — it is a border, and a border that blinks with the rim would just re-encode the
    /// same bit twice.</summary>
    internal static Color? OutlineKeylineTint()
    {
        if (!MrActive)
            return null;
        Color c = KeySafe(MrKeylineTint);
        c.a = 1f;
        return c;
    }

    // ------------------------------------------------------------------------------ the palette --

    /// <summary>
    /// The live colour (RGB + alpha) for a mark, or null when nothing should be drawn. This is the
    /// single decision point every outline in the feature routes through — including the MR/non-MR
    /// fork, which is why the three carriers can never disagree about the mode.
    /// </summary>
    internal static Color? Tint(FocusTurnMark mark)
    {
        switch (mark)
        {
            case FocusTurnMark.AtTurnCorrect:
                return MrActive ? MrPulse(MrCorrectTint, MrCalmMinLevel) : WithBlink(CorrectTint);
            case FocusTurnMark.AtTurnWrong:
                return MrActive ? MrPulse(MrWrongTint, MrUrgentMinLevel) : WithBlink(WrongTint);
            default:
                return null;
        }
    }

    /// <summary>The steady focus-ring colour (no blink) — "this is the character being looked
    /// at", independent of whose turn it is.</summary>
    internal static Color SelectionRingTint()
    {
        if (MrActive)
        {
            Color mr = KeySafe(MrSelectionTint);
            mr.a = 1f;
            return mr;
        }
        Color c = SelectionTint;
        c.a = SelectionAlpha;
        return c;
    }

    /// <summary>The steady neutral at-turn colour, for a turn nobody local has to react to.</summary>
    internal static Color AtTurnRingTint()
    {
        if (MrActive)
        {
            Color mr = KeySafe(MrAtTurnTint);
            mr.a = 1f;
            return mr;
        }
        Color c = AtTurnTint;
        c.a = AtTurnAlpha;
        return c;
    }

    private static Color WithBlink(Color baseColor)
    {
        Color c = baseColor;
        c.a = Mathf.Lerp(MinAlpha, MaxAlpha, Phase);
        return c;
    }

    /// <summary>The MR blink: OPAQUE, with the brightness swinging instead of the alpha (see the
    /// class doc — a translucent cue over the chroma key is what got keyed away). Key-safety is
    /// applied AFTER the dim, because dimming moves a colour toward black and black is itself a
    /// selectable key.</summary>
    private static Color MrPulse(Color baseColor, float minLevel)
    {
        float level = Mathf.Lerp(minLevel, 1f, Phase);
        return KeySafe(new Color(baseColor.r * level, baseColor.g * level, baseColor.b * level, 1f));
    }
}

/// <summary>
/// A hollow uGUI outline for a canvas rect — the initiative-track entry's portrait. Built from the
/// SAME <c>SoftCueArt.FrameSprite</c> recipe the selection-phase ring uses, so the two cues are
/// literally one sprite family; the only difference is the colour this class is told to wear.
///
/// <para>Every property that made the selection ring safe is preserved deliberately:
/// <c>raycastTarget = false</c> (it must never eat the portrait's own click — that click is how
/// the player CHANGES focus), <c>fillCenter = false</c> (a frame in the margin, never a wash over
/// the face), local z 0 (invisible to the initiative surface's per-portrait depth normalisation),
/// and the parent's layer copied every apply so it rides the surface's mod-layer re-layering.</para>
/// </summary>
internal sealed class UiRing
{
    /// <summary>Outset (uGUI px) past the framed rect, so the ring sits in the margin band.
    /// Slightly wider than the selection-phase ring's 8 px: this ring may sit OUTSIDE that one on
    /// the same portrait, and two concentric rings have to be distinguishable.</summary>
    private const float OutsetPixels = 13f;

    private readonly Image _image;

    private UiRing(Image image) => _image = image;

    /// <summary>True while the underlying GameObject is still alive.</summary>
    internal bool Alive => _image != null && _image.transform.parent != null;

    /// <summary>
    /// Build a ring as a child of <paramref name="rect"/> (typically the entry's avatar
    /// <c>RawImage</c>). Returns null when the rect is not laid out yet — callers retry next tick.
    /// </summary>
    internal static UiRing? Build(RectTransform? rect, string name)
    {
        if (rect == null)
            return null;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(rect, worldPositionStays: false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-OutsetPixels, -OutsetPixels);
        rt.offsetMax = new Vector2(OutsetPixels, OutsetPixels);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        // z == 0 ⇒ ignored by the initiative surface's depth passes (see the selection ring).
        rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        rt.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.sprite = WorldUI.SoftCueArt.FrameSprite(0); // hard corners — it frames a hard rect
        img.type = Image.Type.Sliced;
        img.fillCenter = false;
        img.raycastTarget = false; // MUST NOT eat the portrait click that CHANGES the focus
        go.layer = rect.gameObject.layer;
        go.SetActive(false);
        return new UiRing(img);
    }

    /// <summary>Show the ring in <paramref name="tint"/>, or hide it when null. Copies the
    /// parent's live layer so the ring follows the surface across convert/release cycles.</summary>
    internal void Apply(Color? tint, bool breathe)
    {
        if (_image == null)
            return;
        GameObject go = _image.gameObject;
        if (tint == null)
        {
            if (go.activeSelf)
                go.SetActive(false);
            return;
        }
        Transform? parent = _image.transform.parent;
        int layer = parent != null ? parent.gameObject.layer : go.layer;
        if (go.layer != layer)
            go.layer = layer;
        _image.color = tint.Value;
        float s = breathe ? 1f + FocusCue.ScalePulseAmount * FocusCue.Phase : 1f;
        _image.transform.localScale = new Vector3(s, s, 1f);
        if (!go.activeSelf)
            go.SetActive(true);
    }

    /// <summary>Destroy the ring GameObject (module shutdown / track rebuilt).</summary>
    internal void Destroy()
    {
        if (_image != null)
            Object.Destroy(_image.gameObject);
    }
}

/// <summary>
/// A world-space rectangular outline: four thin unlit bars laid out around a rect in its parent's
/// local XY plane. Used for the control boards and for the Steam-avatar quad next to them.
///
/// <para>WHY BARS AND NOT A 9-SLICED SPRITE QUAD: a sprite frame stretched onto a single quad
/// scales its border with the quad, so the same recipe would draw a hairline around a 6 cm avatar
/// and a hand-wide band around a 60 cm board. Four bars of a CONSTANT metric thickness read
/// identically at both sizes, which is the whole point of a shared implementation.</para>
///
/// <para>Everything is UNLIT (the scenario lighting is not ours — the head/hand rule), collider
/// free, and shadow free. The bars are children of the caller's root, so they inherit its pose and
/// die with it.</para>
/// </summary>
internal sealed class WorldFrame
{
    private readonly GameObject _root;
    private readonly Material _material;
    private bool _shown;

    private WorldFrame(GameObject root, Material material)
    {
        _root = root;
        _material = material;
    }

    /// <summary>
    /// Build a frame of <paramref name="size"/> local metres around the origin of
    /// <paramref name="parent"/>, <paramref name="thickness"/> metres thick, at local z
    /// <paramref name="z"/> (negative = towards the viewer under the module's +Z-away convention).
    /// </summary>
    internal static WorldFrame Build(Transform parent, string name, Vector2 size,
                                     float thickness, float z)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, worldPositionStays: false);
        root.transform.localPosition = new Vector3(0f, 0f, z);

        // ONE material shared by the four bars: one colour write per apply, not four.
        Material mat = BoardVisual.Unlit(Color.white);
        float hw = size.x * 0.5f;
        float hh = size.y * 0.5f;
        // Top / bottom run the full width (corners included); left / right fill the gap between.
        Bar(root.transform, "Top", new Vector2(size.x + thickness, thickness), new Vector3(0f, hh, 0f), mat);
        Bar(root.transform, "Bottom", new Vector2(size.x + thickness, thickness), new Vector3(0f, -hh, 0f), mat);
        Bar(root.transform, "Left", new Vector2(thickness, size.y - thickness), new Vector3(-hw, 0f, 0f), mat);
        Bar(root.transform, "Right", new Vector2(thickness, size.y - thickness), new Vector3(hw, 0f, 0f), mat);

        VRLayers.Apply(root);
        root.SetActive(false);
        return new WorldFrame(root, mat);
    }

    private static void Bar(Transform parent, string name, Vector2 size, Vector3 pos, Material mat)
    {
        MeshRenderer mr = BoardVisual.Quad(parent, name, size, mat);
        mr.transform.localPosition = pos;
    }

    /// <summary>Show the frame in <paramref name="tint"/>, or hide it when null.</summary>
    internal void Apply(Color? tint)
    {
        if (_root == null)
            return;
        if (tint == null)
        {
            if (_shown)
            {
                _shown = false;
                _root.SetActive(false);
            }
            return;
        }
        if (_material != null)
            _material.color = tint.Value;
        if (!_shown)
        {
            _shown = true;
            _root.SetActive(true);
        }
    }

    /// <summary>Re-seat the frame inside its parent (the avatar ring follows the avatar quad's own
    /// seat in the owner-tag row). Cheap enough to call every tick — it is one transform write.</summary>
    internal void SetLocalSeat(Vector3 localPosition)
    {
        if (_root != null)
            _root.transform.localPosition = localPosition;
    }

    /// <summary>Every renderer of the frame — for the panel-compositing ladder, where a caller
    /// already ranks its own renderers.</summary>
    internal Renderer[] Renderers =>
        _root != null ? _root.GetComponentsInChildren<Renderer>(includeInactive: true)
                      : System.Array.Empty<Renderer>();

    internal void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root);
    }
}
