using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 4 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Regions: slots, pick field (REMOVED — the note is the record, see Batch D), highlight
// incl. the steady "wanted slot" hint, item-use slot.

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ slots --

    internal VRCard? Occupant(int slot) => _occupants[slot];

    /// <summary>
    /// Home offset that seats a card ON the physical recess surface instead of at the
    /// bundle anchor's mid-plane centre (test #28): a small push toward the viewer
    /// (the board's -Z face), tuned by [Cards] SlotCardInset. Shared by every path
    /// that parks a card in a slot — <see cref="PlaceCard"/>, <see cref="PlacePickCard"/>
    /// and HalfSelection's docked action cards — so the seating is consistent and
    /// tunable in one place. The slot itself carries the tray tilt/scale; the card
    /// inherits both.
    /// </summary>
    internal static Vector3 SlotHomeOffset => SlotHomeOffsetFor(0, applySpread: false);

    /// <summary>
    /// Item B (couple the resting card to the Overlays element): the slot-local home offset a card
    /// takes, now including the per-board <see cref="CardsConfig.SlotOverlayOffset"/> (X/Y in plane,
    /// Z proud) and — when <paramref name="applySpread"/> — the <see cref="CardsConfig.SlotOverlaySpacing"/>
    /// pair spread (slot 0 −½, slot 1 +½). Tuning the debug-menu "Overlays" element therefore moves
    /// the actual SLOT where a placed card physically rests together with its snap/wanted glows (which
    /// take the identical offset in <see cref="BuildSlotHighlights"/>/<see cref="BuildWantedHighlights"/>).
    /// The base inset (−SlotCardInset toward the viewer) is unchanged.
    /// </summary>
    internal static Vector3 SlotHomeOffsetFor(int slot, bool applySpread = true)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        Vector3 ov = CardsConfig.SlotOverlayOffset(b).Value;
        float xSpread = applySpread
            ? (slot == 0 ? -0.5f : 0.5f) * CardsConfig.SlotOverlaySpacing(b).Value
            : 0f;
        return new Vector3(ov.x + xSpread, ov.y, -CardsConfig.SlotCardInset.Value + ov.z);
    }

    /// <summary>
    /// ITEM 3: the home scale a card takes when it seats in a slot — it grows to (nearly)
    /// fill the recess. Multiplies on top of the slot frame's inherited 1.3× SlotScale;
    /// tuned per board via <c>[Cards] SlotCardFill</c>. Shared by every slot-home path
    /// (<see cref="PlaceCard"/>, <see cref="PlacePickCard"/>, HalfSelection docked cards)
    /// so a slotted card is the same size regardless of how it got there.
    /// </summary>
    internal static float SlotCardScale => Mathf.Max(0.1f, CardsConfig.SlotCardFill.Value);

    /// <summary>
    /// Slot anchor transform (test #19: HalfSelection docks the round cards into
    /// the SAME slots during action selection). Null until built or out of range —
    /// callers treat null as "no dock" instead of crashing the driver.
    /// </summary>
    internal Transform? SlotTransform(int slot) =>
        slot >= 0 && slot < _slots.Length ? _slots[slot] : null;

    internal int SlotOf(VRCard card)
    {
        if (_occupants[0] == card) return 0;
        if (_occupants[1] == card) return 1;
        return -1;
    }

    internal bool ContainsCard(VRCard card) => SlotOf(card) >= 0;

    /// <summary>
    /// Which slot would capture a card with the card center at <paramref name="cardPos"/>
    /// and the holding hand at <paramref name="handPos"/>? EITHER sample within the
    /// capture radius accepts — the pinch-grip held pose (P8) offsets the card center
    /// from the palm, so "hand over the slot" and "card over the slot" must both work
    /// (test #13). Returns -1 when outside both radii. With <paramref name="log"/> the
    /// full distance table and the verdict go to the log (drop-time diagnostics).
    /// </summary>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos) =>
        SlotNear(cardPos, handPos, out _, out _, out _);

    /// <summary>
    /// Same test with the sampled distances exposed so the RELEASE path can log one
    /// concise line per real drop (test #14) — no logging in here.
    /// </summary>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos, out float d0, out float d1, out float radius)
    {
        d0 = d1 = float.PositiveInfinity;
        radius = 0f;
        if (_root == null || !IsVisible)
            return -1;
        float scale = _root.lossyScale.x;
        radius = SlotCaptureRadius * scale;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null)
                continue;
            float dist = Mathf.Min(
                Vector3.Distance(cardPos, slot.position),
                Vector3.Distance(handPos, slot.position));
            if (i == 0) d0 = dist; else d1 = dist;
            if (dist <= radius && dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ pick field (REMOVED) --

    // The modal-pick DROP FIELD (test #21 B) is GONE — removed as dead code, not as a design
    // decision. It was a slot-style frame in the CENTER of the slot zone that a candidate card
    // could be laid onto. BuildPickField ran on EVERY board build and allocated five GameObjects,
    // a glow material and a TMP caption, then ended with SetActive(false) — and nothing ever
    // turned it on: SetPickFieldVisible had zero external callers, so _pickFieldVisible could
    // never be true, which in turn made SetPickFieldHighlight and PickFieldNear unreachable and
    // PickFieldAnchor/PickFieldVisible unread. Test #28 superseded it: pick candidates home into
    // the slot recesses (see the class doc), and this class doc already recorded that.
    //
    // Two things a re-adder must know, which is why this note stays:
    //  - While a pick field shows, BOTH play-slot roots must hide. Two empty slot frames flanking
    //    a third read as three competing targets, and the slots are guaranteed empty in pick modes
    //    (Rebuild calls ClearSlots outside CardsSelection). That rule is in INVARIANTS-Cards.md §8.
    //  - Net/RemoteBoardFurniture has its OWN BuildPickField and DOES show it via SetPickField(bool).
    //    So peers may render a pick field the local player never sees. That inconsistency predates
    //    this removal and is an open question for the user, not something this commit changed.

    // ------------------------------------------------------------------ highlight --

    private readonly GameObject?[] _slotHighlights = new GameObject?[2];
    private int _highlightedSlot = -1;

    // ---- steady "wanted slot" hint (test #28) ------------------------------------------
    // A softly PULSING accent behind a slot marks where the game is currently waiting
    // for a card — the still-empty play slot(s) during selection, or the LEFT slot
    // during a single-card pick flow. Deliberately distinct from the transient gold
    // snap glow above (_slotHighlights): a different hue (teal), a larger rim, and it
    // pulses so a steady "drop here" hint never reads as the "card will land here on
    // release" preview. The driver toggles it via SetWantedSlots; PlayTray owns only
    // the visuals (pulse is self-animated by SlotPulse, no PlayTray Update needed).
    private readonly GameObject?[] _wantedHighlights = new GameObject?[2];
    private int _wantedMask = -1;

    /// <summary>True while a modal single-card pick flow is live (drives the CONFIRM accent, test #28).</summary>
    private bool _pickActive;

    /// <summary>The driver marks pick flows so CONFIRM accents as the mode's mirrored confirm affordance.</summary>
    internal void SetPickActive(bool active) => _pickActive = active;

    /// <summary>
    /// Glow frame behind each slot — shown while a HELD card is within snap range
    /// (test #13: telegraph exactly where the card will zap on release). Unlit
    /// bright gold so it reads emissive in the unlit void scenes.
    /// </summary>
    private void BuildSlotHighlights()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        Vector3 ov = CardsConfig.SlotOverlayOffset(CardsConfig.CurrentBoard).Value; // PART B: per-board overlay offset
        float ovSpacing = CardsConfig.SlotOverlaySpacing(CardsConfig.CurrentBoard).Value; // item 1: pair spacing
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null || _slotHighlights[i] != null)
                continue;
            float xSpread = (i == 0 ? -0.5f : 0.5f) * ovSpacing; // spread the overlay pair apart along the slot axis
            // Emissive gold via the shared glow-quad helper (Overlay additive): reads as light
            // ADDED over the board, NEGATIVE local-Z (proud toward the player), depth-correct (no
            // RenderOnTop, occludes naturally). The hand-fan insertion overlay uses this SAME
            // helper so the two telegraphs look identical.
            _slotHighlights[i] = CardGlow.CreateGlowQuad("SlotHighlight",
                slot!,
                new Vector3(w * 1.24f, h * 1.24f, 1f),
                new Vector3(ov.x + xSpread, ov.y, SlotGlowBaseZ + ov.z),
                new Color(1f, 0.85f, 0.3f, 0.95f));
        }
    }

    /// <summary>Show the snap-preview glow on one slot (-1 = none). No-ops unless it changes.</summary>
    internal void SetHighlightedSlot(int slot)
    {
        if (slot == _highlightedSlot)
            return;
        _highlightedSlot = slot;
        for (int i = 0; i < 2; i++)
        {
            GameObject? go = _slotHighlights[i];
            if (go != null && go.activeSelf != (i == slot))
                go.SetActive(i == slot);
        }
    }

    /// <summary>
    /// Steady "wanted slot" hint (test #28): a larger teal rim behind the slot that
    /// PULSES via <see cref="SlotPulse"/> — distinct from the gold snap glow. Built as
    /// a slot child so it inherits the slot's SlotScale and pose, hidden by default.
    /// </summary>
    private void BuildWantedHighlights()
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        Vector3 ov = CardsConfig.SlotOverlayOffset(CardsConfig.CurrentBoard).Value; // PART B: per-board overlay offset
        float ovSpacing = CardsConfig.SlotOverlaySpacing(CardsConfig.CurrentBoard).Value; // item 1: pair spacing
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null || _wantedHighlights[i] != null)
                continue;
            float xSpread = (i == 0 ? -0.5f : 0.5f) * ovSpacing; // spread the overlay pair apart along the slot axis
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "WantedHighlight";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(slot, worldPositionStays: false);
            // Larger rim than the snap glow (1.24×) so the teal reads AROUND the gold when both
            // show; sits a hair less proud (base z -0.004) than the snap glow (-0.006) so the gold
            // snap draws in front of the teal, preserving the old ordering.
            quad.transform.localScale = new Vector3(w * 1.36f, h * 1.36f, 1f);
            quad.transform.localPosition = new Vector3(ov.x + xSpread, ov.y, WantedGlowBaseZ + ov.z); // PROUD toward the player + per-board offset + pair spacing
            var renderer = quad.GetComponent<MeshRenderer>();
            var baseColor = new Color(0.25f, 0.85f, 0.6f, 0.7f); // teal accent — the "drop here" hint
            // Emissive teal via Overlay (additive). CORE FIX: NEGATIVE local-Z (proud, toward the
            // player) and NO RenderOnTop — depth-correct, no shine-through.
            Material? mat = MakeGlowMaterial(baseColor);
            if (mat != null)
                renderer.sharedMaterial = mat;
            quad.AddComponent<SlotPulse>().Init(renderer, baseColor);
            quad.SetActive(false);
            _wantedHighlights[i] = quad;
        }
    }

    // ------------------------------------------------------------------ item-use slot --

    /// <summary>
    /// Build the ITEM-USE clip-in slot (items rework, requirement 3): a card-sized recess
    /// UNDER the board next to the Confirm/Undo buttons — a gold Frame + darker inner + a
    /// pulsing "drop here" glow + a localized "USE" caption. Positioned at
    /// <see cref="ItemUseSlotBase"/> + the per-board <see cref="CardsConfig.ItemUseSlotOffset"/>
    /// (debug-menu tunable, live-applied via <see cref="SetItemUseSlotOffset"/>). Built ONCE and
    /// starts HIDDEN — <see cref="ItemsPile"/> shows it live only while the local player holds a
    /// usable item card on their own turn, then reads <see cref="ItemUseSlotTransform"/> to
    /// detect a drop-in. Mirrors the procedural slot recess so it reads as a real card slot.
    /// </summary>
    private void BuildItemUseSlot()
    {
        if (_root == null)
            return;
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var go = new GameObject("GloomhavenVR.ItemUseSlot");
        go.transform.SetParent(_root, worldPositionStays: false);
        go.transform.localPosition = ItemUseSlotBase + CardsConfig.ItemUseSlotOffset(CardsConfig.CurrentBoard).Value;
        go.transform.localRotation = _boardFaceFrame; // face the player like the slots / decision buttons

        // Gold frame + darker inner (mirror of the procedural Slot1/Slot2 recess look).
        var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        frame.name = "Frame";
        Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(go.transform, worldPositionStays: false);
        frame.transform.localScale = new Vector3(w * 1.12f, h * 1.12f, 1f);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        Tint(frame, new Color(0.55f, 0.45f, 0.22f));

        var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
        inner.name = "FrameInner";
        Object.Destroy(inner.GetComponent<Collider>());
        inner.transform.SetParent(go.transform, worldPositionStays: false);
        inner.transform.localScale = new Vector3(w * 1.04f, h * 1.04f, 1f);
        inner.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        Tint(inner, new Color(0.12f, 0.10f, 0.08f));

        // Pulsing warm "drop here to use" glow rim (self-animated, no PlayTray Update).
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.name = "Glow";
        Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(go.transform, worldPositionStays: false);
        glow.transform.localScale = new Vector3(w * 1.28f, h * 1.28f, 1f);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.0035f); // between frame and inner, proud
        var glowRenderer = glow.GetComponent<MeshRenderer>();
        var glowColor = new Color(1f, 0.82f, 0.35f, 0.8f); // warm gold — the inviting "use" highlight
        _itemUseSlotGlow = MakeGlowMaterial(glowColor);
        if (_itemUseSlotGlow != null)
            glowRenderer.sharedMaterial = _itemUseSlotGlow;
        glow.AddComponent<SlotPulse>().Init(glowRenderer, glowColor);

        // "USE" caption, BELOW the recess — never across it.
        //
        // ROOT CAUSE of "der 'Use'-Text glitcht immer mal wieder vor die Karte und darunter": the
        // caption used to sit at the slot's CENTRE (0, 0, −0.002), i.e. exactly where the clipped-in
        // card lands, and a couple of millimetres proud of it. Two co-planar surfaces two millimetres
        // apart, one of them a card whose face art and backing are pushed into a high render queue
        // (CardMesh.HeldCardRenderQueue / VRCard's render-on-top), decide their order per FRAME and
        // per VIEW ANGLE — so the text flickered in front of the card art and then behind it as the
        // head moved. Nudging the z would only move the flicker.
        //
        // The fix is geometric, not a depth tweak: the caption is parked entirely OUTSIDE the card
        // footprint, one half-card below the recess plus a margin, so there is no overlap left to
        // fight over at any viewing angle. It is a label for the slot, and a slot's label belongs
        // under it — which is also what the user asked for ("er soll fix unter der Karte bleiben").
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, worldPositionStays: false);
        labelGo.transform.localPosition = new Vector3(0f, -(h * 0.5f + ItemUseLabelDrop), -0.002f);
        var label = labelGo.AddComponent<TextMeshPro>();
        // "USE" — localized from the game's own use-item bar key, safe English fallback, uppercased.
        label.text = Core.Loc.Game("GUI_USE", "USE").ToUpperInvariant();
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.92f, 0.72f);
        WorldUI.NativeButtonSkin.ApplyFont(label);
        // Fit into the strip BELOW the card, not into the card's own height — a box as tall as the
        // card would let TMP grow the glyphs back up across the recess and undo the separation.
        Core.TmpFit.Fit(label, w * 1.1f, ItemUseLabelHeight, maxFontSize: 0.16f, wrap: false);
        // Parked OUTSIDE the recess (above) means nothing is behind it any more — back it in MR.
        WorldUI.MrBacking.Label(label);

        // Requirement 9a: the item "Use" confirm is no longer a bespoke keycap beside the slot — it is a
        // GENERIC cluster board button in the right-hand Confirm/Undo column (built in BuildButtons,
        // positioned above the pair by SetConfirmUndoOffset), shown only while a card is clipped into the
        // slot awaiting the decision. See BuildButtons / SetItemUseConfirmVisible.

        Core.VRLayers.Apply(go);
        go.SetActive(false); // ItemsPile toggles it live via SetItemUseSlotVisible
        _itemUseSlot = go.transform;
    }

    /// <summary>
    /// Requirement 6 — show/hide the item-use CONFIRM button and set its click action. Called by
    /// <see cref="ItemsPile"/> when a card clips into the slot (visible, with the use callback) and
    /// when the decision resolves (hidden, null). The CANCEL is grabbing the card out, so no button.
    /// </summary>
    internal void SetItemUseConfirmVisible(bool visible, System.Action? onConfirm, string? label = null)
    {
        _itemUseConfirmAction = visible ? onConfirm : null;
        // Item-surrender pick (event mali): the button may carry a demand-specific label
        // ("ITEM ABGEBEN") instead of the default "USE" — the user must never read a
        // surrender as an ordinary use. Null = default. A label change while visible
        // rebuilds the cluster so the cap re-fits the new text.
        string? wantLabel = visible ? label : null;
        bool labelChanged = visible && _itemUseActive && wantLabel != _itemUseConfirmLabel;
        _itemUseConfirmLabel = wantLabel;
        // Requirement 9a: the "Use" confirm is a dynamic member of the generic cluster. Toggling it
        // changes the member COUNT (2 ↔ 3), so the whole Confirm/Undo/Use stack must re-lay-out (and the
        // caps re-size to fit) — rebuild the cluster on an actual change, then show/hide the fresh button.
        if (visible != _itemUseActive || labelChanged)
        {
            _itemUseActive = visible;
            RebuildAttachedControls(); // rebuilds Confirm/Undo (+ Use when active) at the new count
        }
        _itemUseConfirm?.SetVisible(visible);
    }

    /// <summary>Requirement 6 / 4 — is <paramref name="target"/> the item-use CONFIRM button? The board
    /// laser dispatch uses this to EXEMPT a confirm click from the foreign-interaction fan-close (it is
    /// part of the item interaction, not a foreign one).</summary>
    internal bool IsItemUseConfirm(object? target) =>
        _itemUseConfirm != null && ReferenceEquals(_itemUseConfirm, target);

    /// <summary>
    /// The ITEM-USE clip-in slot transform (world pose read by <see cref="ItemsPile"/> for the
    /// drop-in proximity test). Null before the board is built / after teardown.
    /// </summary>
    internal Transform? ItemUseSlotTransform => _itemUseSlot;

    /// <summary>Show/hide the item-use slot (idempotent). Driven live by <see cref="ItemsPile"/>'s gate.</summary>
    internal void SetItemUseSlotVisible(bool visible)
    {
        if (_itemUseSlot != null && _itemUseSlot.gameObject.activeSelf != visible)
            _itemUseSlot.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Live-apply (debug menu / cfg edit): move the item-use slot to <see cref="ItemUseSlotBase"/>
    /// + the new per-board offset (instant). Mirrors <see cref="SetConfirmUndoOffset"/>.
    /// </summary>
    internal void SetItemUseSlotOffset(Vector3 offset)
    {
        if (_itemUseSlot != null)
            _itemUseSlot.localPosition = ItemUseSlotBase + offset;
    }

    /// <summary>
    /// Set which slots the game currently WANTS filled (bit 0 = Slot1, bit 1 = Slot2);
    /// 0 = none. The driver computes the mask from game state each frame; PlayTray
    /// dedupes the visual toggle so a rebuild never restarts the pulse.
    /// </summary>
    internal void SetWantedSlots(int mask)
    {
        if (mask == _wantedMask)
            return;
        _wantedMask = mask;
        for (int i = 0; i < 2; i++)
        {
            GameObject? go = _wantedHighlights[i];
            bool on = (mask & (1 << i)) != 0;
            if (go != null && go.activeSelf != on)
                go.SetActive(on);
        }
    }

    // ---- multiplayer board-UI read seam (Net.NetAvatarDriver.TickExtrasSend) -----------------
    // Pure reads of state this file already owns, published so the extras sender can put the
    // owner's LIVE board-UI onto the wire (extension record 4) without reaching into privates.

    /// <summary>The wanted-slot glow mask exactly as currently shown (bit0 = left slot, bit1 =
    /// right slot; 0 before the driver ever wrote one). Wire input of the board-UI record.</summary>
    internal int WantedSlotMask => _wantedMask > 0 ? _wantedMask & 0x3 : 0;

    /// <summary>
    /// Which of the two card recesses PHYSICALLY holds a card right now (bit0 = Slot1, bit1 =
    /// Slot2). Wire input of the board-UI record's occupancy nibble
    /// (<c>NetProtocol.BoardUiSlotMask</c>, applied by <c>Net.NetAvatarDriver</c> — the Cards layer
    /// deliberately does not reference Net, so the shift and the clamp live on the Net side) — the
    /// user's requirement that a peer sees a card BACK lying exactly where this board has one, and
    /// an empty recess where it has none. One bit per entry of <see cref="_slots"/>, so the mask
    /// widens with the board rather than with a second hard-coded count.
    ///
    /// ROOT CAUSE OF READING THE SCENE INSTEAD OF <see cref="_occupants"/>. <see cref="_occupants"/>
    /// is NOT "what lies in the recesses" — it is the CardsSelection round-card bookkeeping, and it
    /// is only one of FOUR paths that park a card on a slot anchor:
    ///   • <see cref="PlaceCard"/>            — the round cards during CardsSelection (tracked here),
    ///   • <see cref="PlacePickCard"/>        — burn / discard / recover candidates, which
    ///                                          DELIBERATELY do not touch <see cref="_occupants"/>,
    ///   • <c>HalfSelection</c>'s docked action cards — parented straight onto
    ///     <see cref="SlotTransform"/> for the owner's own turn, while
    ///     <c>CardsDriver.Rebuild</c> has just called <see cref="ClearSlots"/> because the mode is
    ///     no longer CardsSelection,
    ///   • the short-rest sacrifice display, which lays its card in the LEFT recess.
    /// A mask built from <see cref="_occupants"/> would therefore report an EMPTY board through the
    /// entire action turn, which is the opposite of what the owner is looking at.
    ///
    /// So this asks the only question that is true for all four paths and cannot go stale: does the
    /// slot anchor currently PARENT a live, un-held card? Every path above goes through
    /// <c>VRCard.SetHome(slot, ...)</c> (or <see cref="PlacePickCard"/>, which is the same call),
    /// and <c>SetHome</c> re-parents. There is no flag to latch, so there is no flag to get stuck:
    /// the card leaving the recess (grabbed, parked, destroyed, the whole board rebuilt) removes it
    /// from the answer in the same frame, which is what the "no desyncs, both players see exactly
    /// the same" requirement needs. A HELD card reads as EMPTY on purpose — the owner is holding it
    /// in their hand, and their hand is separately synced.
    ///
    /// Cheap by construction: two transforms, a handful of children each, once per extras packet
    /// (5–15 Hz), no allocation.
    /// </summary>
    internal int OccupiedSlotMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (SlotHoldsCard(_slots[i]))
                    mask |= 1 << i;
            return mask;
        }
    }

    /// <summary>Does <paramref name="slot"/> currently parent a live, un-held VR card? See
    /// <see cref="OccupiedSlotMask"/> for why this is the physical truth rather than a flag. The
    /// slot's own children are the two glow quads plus at most a card, so this loop is tiny.</summary>
    private static bool SlotHoldsCard(Transform? slot)
    {
        if (slot == null)
            return false;
        for (int c = 0; c < slot.childCount; c++)
        {
            Transform child = slot.GetChild(c);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;
            var card = child.GetComponent<VRCard>();
            if (card != null && !card.IsHeld)
                return true;
        }
        return false;
    }

    /// <summary>True while a CONFIRM control is visible on this board — the mod keycap, or the
    /// docked native Continue widget that replaces it at the same spot (either way the owner
    /// SEES a confirm control there, which is what a peer must reproduce).</summary>
    internal bool ConfirmControlShown =>
        (_confirm != null && _confirm.LogicalVisible)
        || (WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible);

    /// <summary>True while an UNDO control is visible on this board (mod keycap or the docked
    /// native Undo widget) — see <see cref="ConfirmControlShown"/>.</summary>
    internal bool UndoControlShown =>
        (_undo != null && _undo.LogicalVisible)
        || WorldUI.Surfaces.TrayControlDockSurface.UndoDocked;

    /// <summary>True while the item-use clip-in RECESS is shown (ItemsPile toggles it while the
    /// owner is handling a usable item).</summary>
    internal bool ItemUseSlotShown => _itemUseSlot != null && _itemUseSlot.gameObject.activeSelf;

    /// <summary>True while the item-use USE cap is up (a card is clipped in / a demand pick is
    /// ready — the dynamic third member of the confirm cluster).</summary>
    internal bool ItemUseCapShown => _itemUseActive;

    /// <summary>
    /// Visually park a card in a slot (game-state sync happens separately).
    /// <paramref name="announce"/> is true only on the REAL drop path — the
    /// game-state sync re-runs on every rebuild and must stay silent (test #14: the
    /// unconditional log here produced the "card placed" spam without user drops).
    /// A HELD card is never re-homed: SetHome re-parents, which used to yank the
    /// card out of the hand mid-grab and pull it onto the slot (the source of the
    /// phantom ACCEPTs — the card then sat within capture radius at the next
    /// unrelated grip release). Occupancy still updates; the release path homes it.
    /// </summary>
    internal void PlaceCard(VRCard card, int slot, bool instant = false, bool announce = false)
    {
        if (_slots[slot] == null)
            return;
        // A card can only occupy one slot.
        if (_occupants[0] == card) _occupants[0] = null;
        if (_occupants[1] == card) _occupants[1] = null;
        _occupants[slot] = card;
        if (!card.IsHeld)
        {
            card.gameObject.SetActive(true);
            card.SetHome(_slots[slot]!, SlotHomeOffsetFor(slot), Quaternion.identity, SlotCardScale, instant); // ITEM 3: fill the recess; Item B: track the overlay offset
            card.SetDockGrabPad(true); // task #2 follow-up: under/around-grab apron while slot-docked
        }
        if (announce)
            VRLog.Info("Cards", $"Board: card placed in slot {slot + 1}.");
    }

    /// <summary>
    /// Home a single-card PICK candidate into a slot recess (test #28): pick 0 → the
    /// LEFT slot (Slot1), pick 1 → the RIGHT slot (Slot2, the burn-two-discarded
    /// flows). Deliberately does NOT touch <see cref="_occupants"/> — pick cards are
    /// tracked by the driver's <c>_fieldCards</c>, not the played-card slot occupancy
    /// (the slots stay logically empty so <see cref="SyncFromGameState"/> /
    /// <see cref="SlotOf"/> keep their CardsSelection meaning). A HELD card is never
    /// re-homed (the phantom-ACCEPT lesson, see <see cref="PlaceCard"/>). Returns the
    /// slot index used, or -1 when it fell back BESIDE Slot2 (index ≥ 2 — rare; the
    /// caller logs the fallback).
    /// </summary>
    internal int PlacePickCard(VRCard card, int index)
    {
        int slotIndex = index < 2 ? index : 1;
        Transform? slot = _slots[slotIndex];
        if (slot == null || card.IsHeld)
            return slotIndex < index ? -1 : slotIndex; // held: skip; caller re-runs on release
        card.gameObject.SetActive(true);
        if (index < 2)
        {
            card.SetHome(slot, SlotHomeOffsetFor(slotIndex), Quaternion.identity, SlotCardScale); // ITEM 3: fill the recess; Item B: track overlay
            card.SetDockGrabPad(true); // task #2 follow-up: pick cards docked in a recess get the same apron
            return slotIndex;
        }
        // Graceful fallback for a 3rd+ pick card (no silent cap): lay it beside Slot2.
        float w = CardsConfig.CardWidth.Value;
        Vector3 off = SlotHomeOffsetFor(1) + new Vector3((index - 1) * w * 1.15f, 0f, 0f);
        card.SetHome(slot, off, Quaternion.identity, SlotCardScale); // ITEM 3: fill the recess
        card.SetDockGrabPad(true);
        return -1;
    }

    internal void RemoveCard(VRCard card)
    {
        int slot = SlotOf(card);
        if (slot >= 0)
        {
            _occupants[slot] = null;
            card.SetDockGrabPad(false); // apron off with the dock (grab already clears it too)
            VRLog.Info("Cards", $"Board: card taken back from slot {slot + 1}.");
        }
    }

    internal void ClearSlots()
    {
        _occupants[0]?.SetDockGrabPad(false);
        _occupants[1]?.SetDockGrabPad(false);
        _occupants[0] = null;
        _occupants[1] = null;
    }

    /// <summary>
    /// Mirror the authoritative round pile into the slots while PRESERVING the player's
    /// FREE physical placement (item A). The game only tracks the SET of two
    /// <c>RoundAbilityCards</c> plus which one leads initiative
    /// (<c>CCharacterClass.InitiativeAbilityCard</c>, CCharacterClass.cs:220) — it does
    /// NOT care which VR slot a card sits in (<c>SwapInitiative</c> merely toggles the
    /// leader + reverses the pair, AbilityCardUI.cs:809). So this no longer FORCES the
    /// initiative card into slot 0; instead it keeps every already-seated round card in
    /// the exact slot the player dropped it, only (a) evicting occupants that left the
    /// round and (b) dropping a NEWLY-selected round card into an empty slot (default
    /// initiative→0 / other→1 purely as the seed when neither is placed yet, e.g. a
    /// mode re-entry). <see cref="CardsDriver.ReconcileInitiative"/> then drives the
    /// game's initiative to follow whatever card the player put in slot 0 — the inverse
    /// of the old game→slot mapping that fought the player's placement. Returns true
    /// when anything changed. Re-places ONLY what changed (test #14): this runs on every
    /// rebuild — unconditional re-placing re-parents held cards and spams the log.
    /// </summary>
    internal bool SyncFromGameState(CardsHandUI hand, VRCardFactory factory)
    {
        if (hand.PlayerActor == null)
            return false;
        var round = hand.PlayerActor.CharacterClass.RoundAbilityCards;
        ScenarioRuleLibrary.CAbilityCard? initiative = hand.PlayerActor.CharacterClass.InitiativeAbilityCard;

        // Resolve the round cards to their VRCards, tagging the initiative (leading) one
        // so a brand-new pair gets a sensible default seat (initiative → slot 0).
        VRCard? roundInit = null, roundOther = null;
        for (int i = 0; i < round.Count && i < 2; i++)
        {
            AbilityCardUI? widget = FindWidget(hand, round[i]);
            if (widget == null)
                continue;
            VRCard card = factory.GetOrCreate(widget);
            bool isInitiative = initiative != null ? round[i] == initiative : i == 0;
            if (isInitiative && roundInit == null)
                roundInit = card;
            else if (roundOther == null)
                roundOther = card;
            else
                roundInit ??= card;
        }

        bool changed = false;
        // (a) Evict any occupant that is no longer one of the two round cards (unselected
        // / swapped out) — its slot frees up for the surviving/new card.
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _occupants[s];
            if (occ != null && occ != roundInit && occ != roundOther)
            {
                occ.SetDockGrabPad(false); // evicted from the slot → apron off
                _occupants[s] = null;
                changed = true;
            }
        }
        // (b) Seat any round card that is not already in a slot into an empty slot,
        // preferring its default seat (initiative → 0, other → 1) but taking whichever
        // slot is free — the player's own drops already placed most cards, so this only
        // fires for cards the GAME selected without a VR drop (mode re-entry / undo).
        changed |= PlaceRoundCardIfMissing(roundInit, preferSlot: 0);
        changed |= PlaceRoundCardIfMissing(roundOther, preferSlot: 1);
        return changed;
    }

    /// <summary>
    /// Item A helper: seat <paramref name="card"/> into a slot ONLY if it is not already
    /// an occupant (preserving the player's free placement). Prefers
    /// <paramref name="preferSlot"/>, falls back to the other empty slot. No-op when the
    /// card is null or already seated.
    /// </summary>
    private bool PlaceRoundCardIfMissing(VRCard? card, int preferSlot)
    {
        if (card == null || _occupants[0] == card || _occupants[1] == card)
            return false;
        int slot = _occupants[preferSlot] == null ? preferSlot
            : _occupants[1 - preferSlot] == null ? 1 - preferSlot : -1;
        if (slot < 0)
            return false;
        PlaceCard(card, slot);
        return true;
    }

    private static AbilityCardUI? FindWidget(CardsHandUI hand, ScenarioRuleLibrary.CAbilityCard card)
    {
        var cards = hand.cardsUI; // publicized private list
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == card)
                return cards[i];
        }
        return null;
    }

    /// <summary>
    /// Laser pluck for slotted cards (P7): geometric rect test against the two
    /// occupants — same math as CardFan.TryRaycast. No allocations.
    /// </summary>
    internal bool TryRaycastCards(Vector3 origin, Vector3 direction, out VRCard? card,
        out Vector3 point, out float distance)
    {
        card = null;
        point = default;
        distance = float.PositiveInfinity;
        if (!IsVisible)
            return false;

        float halfW = CardsConfig.CardWidth.Value * 0.5f;
        float halfH = CardsConfig.CardHeight * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            VRCard? c = _occupants[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;
            Transform t = c.transform;
            float denom = Vector3.Dot(direction, t.forward);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f || dist >= distance)
                continue;
            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit);
            if (Mathf.Abs(local.x) > halfW || Mathf.Abs(local.y) > halfH)
                continue;
            card = c;
            point = hit;
            distance = dist;
        }
        return card != null;
    }
}
