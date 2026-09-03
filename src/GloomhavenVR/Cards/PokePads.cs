using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE POKE PAD (ModBuild 403) — a larger fingertip hitbox under each card half's small
/// default-action button, and nothing else.
///
/// <para>User, 2026-09-03: <i>"Das physische Drücken (Greiftaste gedrückt gehalten) der kleinen
/// Standard-Aktionen (Standard 2 Laufen oder Standard 2 Nahkampf) ist aktuell unmöglich, man
/// trifft es nicht weil es zu klein ist. … mach die Hitbox für das physische Drücken mit dem
/// ausgestreckten Finger für die Areale etwas größer."</i></para>
///
/// <para>HOW A POKE FINDS ITS TARGET, and why a child pad is the whole answer:
/// <c>PokeInteractor</c> projects the fingertip onto the canvas plane and asks the canvas's own
/// <c>GraphicRaycaster</c> for the topmost raycast target at that point; the press is then sent
/// through <c>ExecuteEvents.GetEventHandler&lt;IPointerClickHandler&gt;</c>, which walks UP the
/// hierarchy from the hit object to the first handler. So an invisible <c>Image</c> parented under
/// the button, stretched past the button's own rect by the pad, is hit where the button would have
/// missed and delivers its click to the button — the game's <c>Button</c> never learns it was
/// reached through a child. The drawn button is untouched: the pad has alpha 0 and no sprite.</para>
///
/// <para>POKE ONLY. The pad carries <see cref="PokeOnlyTarget"/>, and <c>UguiPointer</c>'s far-ray
/// (laser) resolution skips any result that carries it — so the beam's target stays the button's
/// own rect, exactly as before. The user asked for the FINGER's hitbox, not the laser's.</para>
///
/// <para>TWO PADS ON ONE CARD MUST NOT MEET. The top and bottom default actions sit close
/// together; the vertical pad is clamped to just under half the gap between the two buttons, so
/// a finger between them still lands on the nearer one and never on both. The horizontal pad is
/// free (nothing lies beside a default action but the card edge).</para>
///
/// <para>Idempotent per face, driven from the same pump <c>CardHalfTone</c> rides
/// (<c>CardFace</c>): a pooled widget re-used for another card finds its pad and re-sizes it; a
/// dial turned to 0 disables the pads in place. Multiplayer: a hitbox is local input geometry;
/// nothing here reaches the wire, and the mirror never shows a pad.</para>
/// </summary>
internal static class PokePads
{
    private const string PadName = "GloomhavenVR.PokePad";

    /// <summary>The two pads of one face may not come closer than this (uGUI px).</summary>
    private const float MinGapPx = 2f;

    private const int LogBudget = 2;
    private static int _logsLeft = LogBudget;
    private static int _padsBuilt;
    private static int _padsResized;

    internal static void Ensure(FullAbilityCard? face)
    {
        if (face == null)
            return;
        float pad = CardsConfig.PokePadPixelsLive();
        Button? top = face.topActionButton != null ? face.topActionButton.defaultActionButton : null;
        Button? bottom = face.bottomActionButton != null ? face.bottomActionButton.defaultActionButton : null;

        // The vertical pad is bounded by the gap between the two default actions of THIS face.
        float padY = pad;
        if (pad > 0f && top != null && bottom != null
            && top.transform is RectTransform tr && bottom.transform is RectTransform br
            && tr.parent != null && br.parent != null)
        {
            // Both rects in the face's own space: the gap is the distance between the nearer edges.
            Transform space = face.transform;
            Bounds tb = RectTransformUtility.CalculateRelativeRectTransformBounds(space, tr);
            Bounds bb = RectTransformUtility.CalculateRelativeRectTransformBounds(space, br);
            float gap = Mathf.Max(tb.min.y - bb.max.y, bb.min.y - tb.max.y);
            if (gap > 0f)
                padY = Mathf.Min(pad, Mathf.Max(0f, gap * 0.5f - MinGapPx));
        }

        EnsureOne(top, pad, padY, "top");
        EnsureOne(bottom, pad, padY, "bottom");
    }

    private static void EnsureOne(Button? button, float padX, float padY, string which)
    {
        if (button == null)
            return;
        Transform bt = button.transform;
        Transform? existing = bt.Find(PadName);
        if (padX <= 0f)
        {
            if (existing != null && existing.gameObject.activeSelf)
                existing.gameObject.SetActive(false);
            return;
        }

        RectTransform rt;
        bool built = false;
        if (existing == null)
        {
            var go = new GameObject(PadName, typeof(RectTransform), typeof(CanvasRenderer),
                                    typeof(Image), typeof(PokeOnlyTarget));
            go.layer = button.gameObject.layer;
            rt = (RectTransform)go.transform;
            rt.SetParent(bt, worldPositionStays: false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
            img.maskable = true;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Behind the button's own graphics in sibling order: invisible anyway, but a pad must
            // never sit ABOVE the button's image in the raycast when the two rects coincide, so
            // the button itself is what a centred finger reports.
            rt.SetAsFirstSibling();
            built = true;
            _padsBuilt++;
        }
        else
        {
            rt = (RectTransform)existing;
            if (!existing.gameObject.activeSelf)
                existing.gameObject.SetActive(true);
        }

        Vector2 wantMin = new(-padX, -padY);
        Vector2 wantMax = new(padX, padY);
        if (rt.offsetMin != wantMin || rt.offsetMax != wantMax)
        {
            rt.offsetMin = wantMin;
            rt.offsetMax = wantMax;
            if (!built)
                _padsResized++;
        }
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition3D = Vector3.zero;

        if (!built || _logsLeft <= 0)
            return;
        _logsLeft--;
        // Real size at the finger: uGUI px → world (the button's lossyScale) → real metres (the
        // hand's world scale is world units per real metre) → mm.
        var brt = (RectTransform)bt;
        float unitsPerPx = brt.lossyScale.x;
        VRHand? hand = VRHands.Right ?? VRHands.Left;
        float unitsPerMetre = hand != null ? Mathf.Max(hand.WorldScale, 1e-4f) : 0f;
        float mm = unitsPerMetre > 0f ? unitsPerPx / unitsPerMetre * 1000f : 0f;
        Vector2 size = brt.rect.size;
        string real = mm > 0f
            ? $"{size.x * mm:F1} x {size.y * mm:F1} mm drawn, {(size.x + 2f * padX) * mm:F1} x "
              + $"{(size.y + 2f * padY) * mm:F1} mm pokeable at the finger"
            : "real size unknown (no hand scale yet)";
        // HW-VERIFY: the line that says how big the finger's target really is. A pad of 0 mm beside
        // a drawn size means the dial is off; a pokeable size no larger than the drawn one means the
        // pad was clamped to nothing by the gap between the two actions.
        VRLog.Note("Cards",
            $"POKE PAD built under the {which} default action '{button.gameObject.name}': button "
            + $"{size.x:F0}x{size.y:F0} px, pad +{padX:F0} px sideways / +{padY:F0} px vertically "
            + $"([Cards] PokePadPixels {CardsConfig.PokePadPixelsLive():F0}, vertical clamped to half "
            + $"the gap to the other action) = {real}. The pad is an alpha-0 raycast target that "
            + "carries PokeOnlyTarget, so the LASER skips it and only the fingertip is enlarged; "
            + $"the click reaches the button through the hierarchy walk. {_padsBuilt} pad(s) built "
            + $"and {_padsResized} re-sized this session ({_logsLeft} more of these lines).");
    }

    /// <summary>Scenario / module teardown: pads die with their widgets; only the counters reset.</summary>
    internal static void Reset()
    {
        _logsLeft = LogBudget;
        _padsBuilt = 0;
        _padsResized = 0;
    }
}
