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

    /// <summary>
    /// The live colour (RGB + alpha) for a mark, or null when nothing should be drawn. This is the
    /// single decision point every outline in the feature routes through.
    /// </summary>
    internal static Color? Tint(FocusTurnMark mark)
    {
        switch (mark)
        {
            case FocusTurnMark.AtTurnCorrect:
                return WithBlink(CorrectTint);
            case FocusTurnMark.AtTurnWrong:
                return WithBlink(WrongTint);
            default:
                return null;
        }
    }

    /// <summary>The steady focus-ring colour (no blink) — "this is the character being looked
    /// at", independent of whose turn it is.</summary>
    internal static Color SelectionRingTint()
    {
        Color c = SelectionTint;
        c.a = SelectionAlpha;
        return c;
    }

    /// <summary>The steady neutral at-turn colour, for a turn nobody local has to react to.</summary>
    internal static Color AtTurnRingTint()
    {
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
