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
/// <para>THE ZONE RACE (ModBuild 405) — why 403's pad changed nothing on hardware. ModBuild 404,
/// user: <i>"ich kann die kleinen Standard-Aktionen weiterhin nicht physisch drücken — es drückt
/// IMMER die größere Fläche darunter oder darüber, selbst wenn ich mich sehr bemühe, die kleine
/// Fläche exakt zu treffen."</i> The fingertip runs TWO poke paths every tick
/// (<c>PokeInteractor.Tick</c>): the registered-COLLIDER path first, which fires <c>OnPoke</c> the
/// moment the tip is within 8 mm of the nearest collider, and the uGUI path second, whose click
/// fires only once the tip has pushed [WorldUI] PokePressDepthMm (12 mm) THROUGH the canvas. In
/// the half-selection dock every card carries two <c>HalfSelection.HalfZone</c> colliders — the
/// "larger area" — and their <c>OnPoke</c> committed the half with no log line at all. So a finger
/// aimed at the small plate always played the big action 20 mm before the plate's own click
/// could fire; the pad only ever enlarged the LOSING path. The ModBuild 404 log shows it in order:
/// the tooltip names 'Default action button' under the finger (line 4590), the ActionSelection gate
/// flips to Pick1stTarget (4595, the zone), THEN the one and only <c>uGUI click: 'Default action
/// button' (poke-R)</c> of the session (4602), then UNDO. The fix is the carve-out
/// <see cref="LocateDefaultActions"/> serves: a zone poke whose fingertip lies inside a live
/// default action's PADDED rect is refused, and the plate's own uGUI click lands at depth. The
/// HALF ZONE POKE line prints, for every zone poke, where the finger was against both plates in
/// px and mm and which verdict that produced — so "finger inside the small rect but the big area
/// won" is a number, not a report.</para>
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

    /// <summary>HALF ZONE POKE lines left this session (every zone poke is a player action, so
    /// the first dozen print unconditionally; the refusals have their own budget below).</summary>
    private const int ZonePokeLogBudget = 12;
    private static int _zonePokeLogsLeft = ZonePokeLogBudget;
    private static int _zoneCarveLogsLeft = ZonePokeLogBudget;
    private static int _zonePokes;
    private static int _zoneCarveOuts;

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
            + $"and {_padsResized} re-sized this session ({_logsLeft} more of these lines)."
            // ModBuild 405: the real-size terms, so the figure above can be checked. The formula is
            // right; what was wrong in ModBuild 404 was WHEN it was evaluated — a widget adopted
            // before its card is seated under the scaled tray reports its unseated lossyScale here.
            + $" REAL-SIZE TERMS: mm = px x button lossyScale.x {unitsPerPx:E3} world-units/px "
            + $"/ hand WorldScale {unitsPerMetre:F2} world-units/real-metre x 1000, evaluated at "
            + "BUILD time, when a freshly adopted widget may not yet sit under the scaled tray "
            + "(ModBuild 404's '0.8 x 0.2 mm' for a 95x21 px button was the card at scale ~1 "
            + "against a 20x rig, i.e. 20x too small); the HALF ZONE POKE line re-measures the "
            + "same formula at the finger, where the card is live.");
    }

    // ---- ModBuild 405: the carve-out probe --------------------------------------------------

    /// <summary>
    /// Where a world point lies against ONE half's default-action button, in the button's own
    /// uGUI pixels: inside its drawn rect, inside its padded rect (the pad's live extents, 0 when
    /// the dial is off), and how big a pixel is at the finger right now.
    /// </summary>
    internal readonly struct PadProbe
    {
        public readonly string Which;
        public readonly string Name;
        public readonly bool Found;
        public readonly bool Interactable;
        public readonly bool InsideDrawn;
        public readonly bool InsidePadded;
        public readonly Vector2 LocalPx;
        public readonly Vector2 DrawnPx;
        public readonly Vector2 PadPx;
        public readonly float MmPerPx;

        public PadProbe(string which, string name, bool found, bool interactable, bool insideDrawn,
            bool insidePadded, Vector2 localPx, Vector2 drawnPx, Vector2 padPx, float mmPerPx)
        {
            Which = which;
            Name = name;
            Found = found;
            Interactable = interactable;
            InsideDrawn = insideDrawn;
            InsidePadded = insidePadded;
            LocalPx = localPx;
            DrawnPx = drawnPx;
            PadPx = padPx;
            MmPerPx = mmPerPx;
        }

        /// <summary>The verdict the zone acts on: a LIVE plate under the finger takes the poke.</summary>
        public bool CarvesOut => Found && Interactable && InsidePadded;

        public string Describe()
        {
            if (!Found)
                return $"{Which} default action: none on this face";
            string mm = MmPerPx > 0f
                ? $"{MmPerPx:F3} mm/px live, padded target {(DrawnPx.x + 2f * PadPx.x) * MmPerPx:F1} x "
                  + $"{(DrawnPx.y + 2f * PadPx.y) * MmPerPx:F1} mm"
                : "mm/px unknown";
            return $"{Which} default action '{Name}': finger at ({LocalPx.x:+0;-0;0}, {LocalPx.y:+0;-0;0}) px "
                   + $"from its centre, drawn {DrawnPx.x:F0}x{DrawnPx.y:F0} px, pad +{PadPx.x:F0}/+{PadPx.y:F0} px "
                   + $"-> inside drawn {(InsideDrawn ? "YES" : "no")}, inside padded "
                   + $"{(InsidePadded ? "YES" : "no")}, interactable {(Interactable ? "yes" : "NO")}, {mm}";
        }
    }

    /// <summary>
    /// Probe BOTH default actions of a face against a world point (the fingertip). Both, not the
    /// zone's own half: the plates sit near the card's middle band, so the top plate may lie
    /// inside the bottom zone's box and vice versa — a zone must yield to whichever plate is under
    /// the finger. <paramref name="unitsPerMetre"/> is the hand's WorldScale (world units per real
    /// metre); it only feeds the mm readout.
    /// </summary>
    internal static void LocateDefaultActions(FullAbilityCard? face, Vector3 worldPoint, float unitsPerMetre,
        out PadProbe top, out PadProbe bottom)
    {
        Button? tb = face != null && face.topActionButton != null ? face.topActionButton.defaultActionButton : null;
        Button? bb = face != null && face.bottomActionButton != null ? face.bottomActionButton.defaultActionButton : null;
        top = LocateOne("top", tb, worldPoint, unitsPerMetre);
        bottom = LocateOne("bottom", bb, worldPoint, unitsPerMetre);
    }

    private static PadProbe LocateOne(string which, Button? button, Vector3 worldPoint, float unitsPerMetre)
    {
        if (button == null || button.transform is not RectTransform brt)
            return new PadProbe(which, "?", false, false, false, false, Vector2.zero, Vector2.zero, Vector2.zero, 0f);

        // The pad's live extents, from the same offsets Ensure wrote (0 when off or not built).
        Vector2 padPx = Vector2.zero;
        Transform? pad = brt.Find(PadName);
        if (pad is RectTransform prt && pad.gameObject.activeSelf)
            padPx = new Vector2(Mathf.Max(0f, -prt.offsetMin.x), Mathf.Max(0f, -prt.offsetMin.y));

        // Everything in the BUTTON's local px space, relative to its rect centre: the same frame the
        // GraphicRaycaster's rect test uses, so "inside padded" here is "the pad is a hit" there.
        Vector3 local = brt.InverseTransformPoint(worldPoint);
        Rect drawn = brt.rect;
        Vector2 rel = new(local.x - drawn.center.x, local.y - drawn.center.y);
        Vector2 halfDrawn = drawn.size * 0.5f;
        bool insideDrawn = Mathf.Abs(rel.x) <= halfDrawn.x && Mathf.Abs(rel.y) <= halfDrawn.y;
        bool insidePadded = Mathf.Abs(rel.x) <= halfDrawn.x + padPx.x && Mathf.Abs(rel.y) <= halfDrawn.y + padPx.y;
        bool interactable = button.gameObject.activeInHierarchy && button.IsInteractable();
        float mmPerPx = unitsPerMetre > 1e-6f ? brt.lossyScale.x / unitsPerMetre * 1000f : 0f;
        return new PadProbe(which, button.gameObject.name, true, interactable, insideDrawn, insidePadded,
                            rel, drawn.size, padPx, mmPerPx);
    }

    /// <summary>
    /// The HALF ZONE POKE line — one per zone poke (capped), naming the finger's position against
    /// both plates and the verdict. Called by <c>HalfSelection.HalfZone.OnPoke</c> BEFORE it acts,
    /// so a refused poke and a committed one are printed by the same line.
    /// </summary>
    internal static void LogZonePoke(string half, HandSide side, bool playable, in PadProbe top,
        in PadProbe bottom, bool carvedOut)
    {
        _zonePokes++;
        if (carvedOut)
            _zoneCarveOuts++;
        bool print = carvedOut ? _zoneCarveLogsLeft > 0 : _zonePokeLogsLeft > 0;
        if (!print)
            return;
        if (carvedOut)
            _zoneCarveLogsLeft--;
        else
            _zonePokeLogsLeft--;
        string verdict = !playable
            ? "REFUSED — this half is not playable now"
            : carvedOut
                ? "REFUSED — CARVED OUT for the default action under the finger (ModBuild 405): the "
                  + "zone yields and the plate's own uGUI click lands at [WorldUI] PokePressDepthMm; "
                  + "expect a 'uGUI click: 'Default action …' (poke-…)' line right after this one"
                : "COMMITTED the half (the big action)";
        // HW-VERIFY: the number the ModBuild 404 report lacked. Before 405 a zone poke printed
        // nothing; this line says where the finger was against BOTH small plates (px from each
        // plate's centre, inside drawn / inside padded) and what the zone did. 'inside padded YES'
        // together with 'COMMITTED' would be the old defect reappearing; 'inside padded no' on a
        // poke the player aimed at the plate means the pad is too small, not the pick.
        VRLog.Note("Cards",
            $"HALF ZONE POKE ({side}) on the {half} half zone: {verdict}. {top.Describe()}; "
            + $"{bottom.Describe()}. {_zonePokes} zone poke(s) this session, {_zoneCarveOuts} carved out "
            + $"({_zonePokeLogsLeft} commit line(s) and {_zoneCarveLogsLeft} carve-out line(s) left).");
    }

    /// <summary>Scenario / module teardown: pads die with their widgets; only the counters reset.</summary>
    internal static void Reset()
    {
        _logsLeft = LogBudget;
        _padsBuilt = 0;
        _padsResized = 0;
        _zonePokeLogsLeft = ZonePokeLogBudget;
        _zoneCarveLogsLeft = ZonePokeLogBudget;
        _zonePokes = 0;
        _zoneCarveOuts = 0;
    }
}
