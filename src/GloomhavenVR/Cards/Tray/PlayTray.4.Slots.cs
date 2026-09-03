using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 4 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Regions: slots, pick field (REMOVED — the note is the record, see Batch D), highlight
// incl. the steady "wanted slot" hint, recess seat liner (round 13), item-use slot.

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ slots --

    internal VRCard? Occupant(int slot) => _occupants[slot];

    // SlotHomeOffset IS RETIRED (2026-08-11) — the spread-free convenience accessor that used to
    // stand here, `SlotHomeOffsetFor(0, applySpread: false)`. Its doc claimed it was "shared by
    // every path that parks a card in a slot", and that was the defect: PlaceCard, PlacePickCard
    // and the live-tune re-home in PlayTray.3.Pose all take SlotHomeOffsetFor(slot) WITH the pair
    // spread, and so do both slot glows — only HalfSelection's docked action cards read this one,
    // for BOTH cards, so a docked pair sat 5.5 mm wider than the same pair placed there during
    // selection and each card 2.75 mm off its own overlay (Oak, shipped defaults; the full
    // arithmetic and the user report are on the call site in HalfSelection.SetCards). One accessor
    // per seat, spread included, is the whole point of the debug menu's "Overlays" element; a
    // second, spread-free one is only ever a way to lose it again. No migration marker is needed —
    // this is an internal code seam, not a config key.
    //
    // If a future caller genuinely wants the un-spread centre of a recess, call
    // SlotHomeOffsetFor(slot, applySpread: false) at the site and say there why the pair spread
    // does not apply to it.

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
    /// fill the recess. Multiplies on top of the slot frame's inherited 1.3× SlotScale.
    /// Shared by every slot-home path (<see cref="PlaceCard"/>, <see cref="PlacePickCard"/>,
    /// HalfSelection docked cards) so a slotted card is the same size regardless of how it got there.
    ///
    /// <para>THIS IS THE OVERLAY SIZE (2026-08-11). It used to be its own dial, <c>[Cards]
    /// SlotCardFill</c>, while <see cref="BuildWantedHighlights"/> sized the blinking overlay off a
    /// code literal — 1.36 against the card's 1.45, so the card that landed overhung by 6.6 % the
    /// very rectangle that had just marked the spot, and no dial could close it. User, after being
    /// asked whether the overlay should keep a visible rim: "exakt ausfüllen". So the card and the
    /// wanted-glow now read ONE per-board number, <c>[Cards] SlotOverlayScale_{board}</c>, seeded
    /// with SlotCardFill's 1.45 — the card is unchanged, the overlay grew to meet it, and turning
    /// the dial moves both. Both live in the slot frame and both scale the same authored card
    /// metric (the sweep's face width is <c>CardWidth × lossyScale</c>, the quad is
    /// <c>CardWidth × factor</c>), so the two numbers are directly comparable — that is what makes
    /// "exactly fills" a single shared factor rather than a fitted constant.</para>
    /// </summary>
    internal static float SlotCardScale =>
        Mathf.Max(0.1f, CardsConfig.SlotOverlayScale(CardsConfig.CurrentBoard).Value);

    /// <summary>
    /// The gold SNAP glow's size as a fraction of the teal WANTED glow (= of the card). The two
    /// shipped as 1.24 and 1.36 and can be on together — a held card in snap range over a slot the
    /// game still wants — where the teal is meant to read as a rim AROUND the gold. Keeping their
    /// RATIO rather than a second dial preserves that reading at every setting of
    /// <c>SlotOverlayScale</c>; 1.24/1.36 is where it shipped.
    /// </summary>
    /// <remarks>Internal, not private: <c>Net.RemoteBoardFurniture</c> reproduces the pair on a
    /// peer's board and must hold the same ratio — a second literal over there is exactly how the
    /// two drifted apart before.</remarks>
    internal const float SnapGlowRatio = 1.24f / 1.36f;

    /// <summary>The teal wanted-pulse quad's size factor — the card's own, by construction.</summary>
    private static float WantedGlowScale => SlotCardScale;

    /// <summary>The gold snap-glow quad's size factor: the card's, held at the shipped ratio.</summary>
    private static float SnapGlowScale => SlotCardScale * SnapGlowRatio;

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
    /// One capture probe of the two recesses: what each sample measured, which recess won, and
    /// why. Purely a value carrier for the drop diagnostics — <see cref="SlotNear(Vector3,
    /// Vector3, out SlotProbe)"/> fills it, nothing in here decides anything.
    ///
    /// <para>It exists because the old three <c>out float</c>s could only report ONE number per
    /// slot (the min of the two samples), which is exactly the number that hid the 2026-08-11
    /// defect: a log line reading "slot1 0.31 m, slot2 0.09 m" cannot tell you whether 0.09 came
    /// from the card or from the hand, so the report that the wrong recess lit up could not be
    /// confirmed from a log. Card and hand are separate fields now, and
    /// <see cref="Describe"/> names which of them picked the winner.</para>
    /// </summary>
    internal readonly struct SlotProbe
    {
        /// <summary>Card-centre distance to slot 1 / slot 2 (world metres at board scale).</summary>
        internal readonly float Card0, Card1;

        /// <summary>Holding hand's palm distance to slot 1 / slot 2.</summary>
        internal readonly float Hand0, Hand1;

        /// <summary>The capture radius both samples were tested against (board-scaled).</summary>
        internal readonly float Radius;

        /// <summary>The winning recess (-1 = none captured) — ranked by CARD distance.</summary>
        internal readonly int Slot;

        /// <summary>Which recess the HAND sample alone would have picked (-1 = none). Diagnostic
        /// only; it has no vote. When this differs from <see cref="Slot"/> the log line says so,
        /// which is the one-grep proof that the 2026-08-11 fix is doing its work.</summary>
        internal readonly int HandWouldPick;

        /// <summary>True when the winner became eligible ONLY through the hand sample (the card
        /// centre itself is outside the radius). The pinch-grip reach case — see
        /// <see cref="SlotNear(Vector3, Vector3, out SlotProbe)"/>.</summary>
        internal readonly bool WinnerViaHandOnly;

        internal SlotProbe(float card0, float card1, float hand0, float hand1, float radius,
            int slot, int handWouldPick, bool winnerViaHandOnly)
        {
            Card0 = card0;
            Card1 = card1;
            Hand0 = hand0;
            Hand1 = hand1;
            Radius = radius;
            Slot = slot;
            HandWouldPick = handWouldPick;
            WinnerViaHandOnly = winnerViaHandOnly;
        }

        /// <summary>
        /// The drop log's distance clause (test #14 keeps one concise line per real drop). Reads
        /// e.g. <c>slot1 card 0.31 / hand 0.44 m, slot2 card 0.52 / hand 0.09 m, radius 0.25 m,
        /// ranked by CARD → slot1 (hand-nearest was slot2 — CARD OVERRODE HAND)</c>. Grep
        /// <c>OVERRODE</c> to find every drop where the two samples disagreed, which is the
        /// family the 2026-08-11 report is from. Note it does NOT claim the pre-fix build would
        /// have chosen slot2: that build ranked on <c>min(card, hand)</c>, so it agreed with the
        /// card whenever the card's own distance was the smaller of the four.
        /// </summary>
        internal string Describe()
        {
            string verdict = Slot < 0 ? "none"
                : "slot" + (Slot + 1)
                  + (HandWouldPick >= 0 && HandWouldPick != Slot
                      ? $" (hand-nearest was slot{HandWouldPick + 1} — CARD OVERRODE HAND)"
                      : " (hand agrees)")
                  + (WinnerViaHandOnly ? " [eligible via HAND sample only]" : string.Empty);
            return $"slot1 card {Card0:F2} / hand {Hand0:F2} m, "
                   + $"slot2 card {Card1:F2} / hand {Hand1:F2} m, "
                   + $"radius {Radius:F2} m, ranked by CARD → {verdict}";
        }
    }

    /// <summary>
    /// Which slot would capture a card with the card center at <paramref name="cardPos"/>
    /// and the holding hand at <paramref name="handPos"/>? EITHER sample within the
    /// capture radius makes a recess ELIGIBLE — the pinch-grip held pose (P8) offsets the card
    /// center from the palm, so "hand over the slot" and "card over the slot" must both work
    /// (test #13) — but the CHOICE between the two eligible recesses is the CARD's alone.
    /// Returns -1 when outside both radii of both slots.
    /// </summary>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos) =>
        SlotNear(cardPos, handPos, out _);

    /// <summary>
    /// Same test with the full sample table exposed so the RELEASE path can log one concise line
    /// per real drop (test #14) — no logging in here, see <see cref="SlotProbe.Describe"/>.
    /// </summary>
    /// <remarks>
    /// USER REPORT, 2026-08-11 (hardware, ModBuild 108), verbatim:
    /// "Wenn man eine Karte zum Overlay hält wird aktuell das Overlay gehighlighted das näher an
    /// der Hand ist. Und die Karte wird dann dort hingelegt. Das hat zB zur Folge, dass ich auf den
    /// linken Platz etwas hinlegen wil und mit der rechten hand die Karte zum linken Platz führe -
    /// aber da meine rechte hand näher am Rechten Platz ist geht die Karte zum rechten Platz. Das
    /// soll nicht sein - das Overlay das näher zur Karte (zB dem Kartemittelpunkt) und NICHT näher
    /// zur hand ist soll reagieren."
    ///
    /// <para>ROOT CAUSE: ELIGIBILITY AND RANKING HAD BEEN CONFLATED INTO ONE NUMBER. The loop used
    /// to reduce each recess to <c>Mathf.Min(cardDistance, handDistance)</c> and then take the
    /// smallest of those two minima. The min is the right ELIGIBILITY test and the wrong RANKING
    /// key: a right hand leading a card across to the LEFT recess sits nearer the RIGHT one, its
    /// hand sample undercuts the left recess's card sample, and the right recess wins a comparison
    /// the card was never allowed to enter. Both halves of the bug follow from that single number —
    /// the gold snap glow lights on the hand's recess (CardsDriver.UpdateSlotHighlight ranks
    /// through here) and the card then drops there (OnCardReleased takes the glow first, this test
    /// second), so the telegraph was consistent with the drop and BOTH were wrong.</para>
    ///
    /// <para>WHY THE HAND SAMPLE EXISTS — DO NOT DELETE IT. It is not a tie-breaker and never was:
    /// the pinch grip (P8) parks the card centre a good way off the palm, so requiring the CARD
    /// centre inside the capture radius would shrink the physical reach of every drop by roughly
    /// that offset, and "hand over the recess" is how a lot of players actually aim (test #13).
    /// Deleting it was considered and REJECTED for exactly that: it fixes the ranking by making
    /// the tray harder to hit, which is a second defect, not a fix. So the hand sample keeps its
    /// full ELIGIBILITY vote and loses its RANKING vote — the accept SET is bit-for-bit what it
    /// was (a recess captures iff either sample is within radius), only WHICH of two eligible
    /// recesses wins has changed, and that is now the card's answer.</para>
    ///
    /// <para>REJECTED ALTERNATIVE — "any eligible slot opens the tray, then rank ALL slots by card
    /// distance". It reads closer to the report's wording, and it differs from what is implemented
    /// in exactly one case: the card is outside the radius of the recess it is nearest to while
    /// the OTHER recess is eligible through the hand. That case is a card held well off both
    /// recesses (&gt; 0.25 board units ≈ 1.6 slot spacings) with the palm hovering over one of
    /// them — and letting a card that is near nothing drop into a recess that nothing was near is
    /// worse than letting the hand have the only eligible one. So candidacy stays per-recess. When
    /// only ONE recess is eligible it wins whatever the card distances say; that is not the hand
    /// winning a ranking, it is a ranking with one candidate.</para>
    ///
    /// <para>No wire change: the peer's board mirrors <see cref="HighlightedSlot"/> and
    /// <see cref="OccupiedSlotMask"/>, i.e. the RESULT of this decision, both of which already
    /// ride the board-UI record.</para>
    /// </remarks>
    internal int SlotNear(Vector3 cardPos, Vector3 handPos, out SlotProbe probe)
    {
        probe = default;
        if (_root == null || !IsVisible)
            return -1;
        float scale = _root.lossyScale.x;
        float radius = SlotCaptureRadius * scale;
        float card0 = float.PositiveInfinity, card1 = float.PositiveInfinity;
        float hand0 = float.PositiveInfinity, hand1 = float.PositiveInfinity;
        int best = -1, handBest = -1;
        float bestCard = float.MaxValue, bestHand = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            Transform? slot = _slots[i];
            if (slot == null)
                continue;
            float cardDist = Vector3.Distance(cardPos, slot.position);
            float handDist = Vector3.Distance(handPos, slot.position);
            if (i == 0) { card0 = cardDist; hand0 = handDist; }
            else { card1 = cardDist; hand1 = handDist; }
            // ELIGIBILITY: either sample inside the radius (unchanged — this is the reach).
            if (cardDist > radius && handDist > radius)
                continue;
            // RANKING: the CARD's distance decides which eligible recess wins. The hand's is
            // tracked alongside for the log only (see the remarks) and casts no vote.
            if (cardDist < bestCard)
            {
                bestCard = cardDist;
                best = i;
            }
            if (handDist < bestHand)
            {
                bestHand = handDist;
                handBest = i;
            }
        }
        bool viaHandOnly = best >= 0 && bestCard > radius;
        probe = new SlotProbe(card0, card1, hand0, hand1, radius, best, handBest, viaHandOnly);
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
            float snap = SnapGlowScale; // 2026-08-11: the card's size, at the shipped gold/teal ratio
            _slotHighlights[i] = CardGlow.CreateGlowQuad("SlotHighlight",
                slot!,
                new Vector3(w * snap, h * snap, 1f),
                new Vector3(ov.x + xSpread, ov.y, SlotGlowBaseZ + ov.z),
                new Color(1f, 0.85f, 0.3f, 0.95f));
            // 2026-08-04 (status-placard defect family): additive glow, depth-less — ride the
            // board's furniture order group like the placard/labels.
            AdoptFurniture(_slotHighlights[i]);
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
            // Larger rim than the snap glow so the teal reads AROUND the gold when both show; sits a
            // hair less proud (base z -0.004) than the snap glow (-0.006) so the gold snap draws in
            // front of the teal, preserving the old ordering. 2026-08-11: this is now EXACTLY the
            // size of the card that will land here (SlotCardScale) — that is the whole point of the
            // shared dial, see SlotCardScale's doc — and the gold keeps its ratio to it.
            float wanted = WantedGlowScale;
            quad.transform.localScale = new Vector3(w * wanted, h * wanted, 1f);
            quad.transform.localPosition = new Vector3(ov.x + xSpread, ov.y, WantedGlowBaseZ + ov.z); // PROUD toward the player + per-board offset + pair spacing
            var renderer = quad.GetComponent<MeshRenderer>();
            var baseColor = new Color(0.25f, 0.85f, 0.6f, 0.7f); // teal accent — the "drop here" hint
            // Emissive teal via Overlay (additive). CORE FIX: NEGATIVE local-Z (proud, toward the
            // player) and NO RenderOnTop — depth-correct, no shine-through.
            Material? mat = MakeGlowMaterial(baseColor);
            if (mat != null)
                renderer.sharedMaterial = mat;
            quad.AddComponent<SlotPulse>().Init(renderer, baseColor);
            // 2026-08-04 (status-placard defect family): additive glow, depth-less — ride the
            // board's furniture order group like the placard/labels.
            AdoptFurniture(quad);
            quad.SetActive(false);
            _wantedHighlights[i] = quad;
        }
    }

    // --------------------------------------------------- recess seat liner: RETIRED (137+1) --

    //
    // THE SEAT LINER IS GONE. User report 2026-08-13, verbatim: "Uns ist aufgefallen das unter den
    // Kartenoverlays noch so ein weiteres Brett liegt. Verschiebt man die Kartenoverlays geht
    // dieses Brett mit. Es ist nie aufgefallen weil die overlays im Controllbaordasset so
    // positioniert waren, dass es perfekt drinen lag. Dieses Brett das zu den overlays gehört soll
    // komplett weg, das brauchen wir nicht. Die overlays reichen und können auf dem asset des
    // controllboards ohne etwas zugehöriges positioniert werden."
    //
    // WHAT IT WAS: rounds 13/14 built a rounded <c>CardMesh</c> slab of grain wood
    // (<c>SlotSeatLiner</c>, 1.45x the seated card) on the recess floor behind each tray card,
    // because every pixel the card's punch-out erased rendered as the bundled tray's own near-black
    // AO-baked recess floor and read as "schwarze Ränder". It was a BACKDROP for a card that had
    // holes in it.
    //
    // WHY IT MAY GO: the card-border saga was solved at the source (mesh-contour punch-out) —
    // the seated card no longer erases a frame band it needs something warm behind. What remained
    // was a third member of the "Overlays" element that silently travelled with the two glows
    // (<see cref="SetOverlayOffset"/> re-seated it in the same loop), invisible only as long as the
    // tuned offsets kept it buried inside the control-board asset. The ModBuild-137 rebase moved
    // the overlays and the plank came out from under the board. The user's ruling settles it: the
    // overlays are the whole element, and they seat directly on the board asset.
    //
    // NOTHING WAS ANCHORED TO IT — verified before deletion: no child was ever parented to
    // <c>SlotSeatLiner</c>, no code looked it up by name, nothing read its renderer or bounds, it
    // was never handed to <c>AdoptFurniture</c>/<c>CanvasConversion.RegisterFurniture</c>, and
    // <see cref="SlotHoldsCard"/> counts only children carrying a <c>VRCard</c>, so
    // <c>OccupiedSlotMask</c> cannot move. Its three constants (SlotLinerSeatRatio 1.45,
    // SlotLinerColor (0.46,0.37,0.26), SlotLinerZ) had exactly two readers — this builder and the
    // peer mirror's <c>Net.RemoteBoardFurniture.BuildSlotLiner</c> — and both are deleted in the
    // same change, so a peer's board loses the same plank in the same build. There is no config
    // dial to retire: the liner never had one.
    //
    // REJECTED: hiding it behind a new [Cards] toggle. A dial the user has ruled OFF is a
    // dial that has to be explained in the options tree forever, and the settings audits of
    // ModBuild 136 removed twenty of exactly that kind.
    //
    // (No member here. This block is the record of a deletion — do NOT rebuild the liner without
    // re-reading the ruling above.)

    // ------------------------------------------------------------------ item-use slot --

    // ---- the item-use BERTH's geometry (see BuildItemUseSlot for the design and the report) ----

    /// <summary>The berth OUTLINE's rectangle, as a factor of the card box. Just outside the
    /// 1.04× clear area a placed card is fitted into (<c>ItemsPile.UseSlotInnerFactor</c>) and the
    /// 0.94 of it the card actually fills, so the outline stays visible all the way round a seated
    /// card instead of being covered by its edge — the property the old gold rim had, kept.</summary>
    private const float ItemBerthRectFactor = 1.08f;

    /// <summary>The warm FIELD inside the berth, as a factor of the card box: it fills the clear
    /// area up to the outline's inner shoulder and no further.</summary>
    private const float ItemBerthFieldFactor = 1.03f;

    /// <summary>Corner rounding of the berth outline, as a factor of the card WIDTH. The item cards
    /// themselves are rounded rectangles; a berth with square corners would read as a picture frame
    /// hung around them rather than as the slot they belong in.</summary>
    private const float ItemBerthCornerFactor = 0.10f;

    // Board-local Z of the three berth layers. +Z is INTO the board, so all three sit BEHIND the
    // z=0 plane a clipped-in card is parented at (ItemsPile.ItemChip.ClipIntoSlot) — the card lies
    // ON the berth, and the berth's own layers never fight each other for depth.
    private const float ItemBerthFieldZ = 0.0035f;
    private const float ItemBerthPingZ = 0.0030f;
    private const float ItemBerthOutlineZ = 0.0025f;

    /// <summary>The berth's arrival/departure driver (see <see cref="SetItemUseSlotVisible"/>).
    /// Null before the board is built; the component dies with the slot subtree.</summary>
    private WorldUI.SoftCueReveal? _itemUseReveal;

    /// <summary>The LOGICAL "an item is placeable right now" state — what
    /// <see cref="SetItemUseSlotVisible"/> was last asked for, as opposed to what is on screen while
    /// the departure animation is still running. This is the edge the multiplayer mirror is driven
    /// from (<see cref="ItemUseSlotShown"/>).</summary>
    private bool _itemUseSlotWanted;

    /// <summary>
    /// Build the ITEM-USE clip-in berth (items rework, requirement 3): a card-sized destination
    /// UNDER the board next to the Confirm/Undo buttons. Positioned at
    /// <see cref="ItemUseSlotBase"/> + the per-board <see cref="CardsConfig.ItemUseSlotOffset"/>
    /// (debug-menu tunable, live-applied via <see cref="SetItemUseSlotOffset"/>). Built ONCE and
    /// starts HIDDEN — <see cref="ItemsPile"/> shows it live only while the local player holds a
    /// usable item card on their own turn, then reads <see cref="ItemUseSlotTransform"/> to
    /// detect a drop-in.
    ///
    /// <para>REDESIGNED 2026-08-09 (user report: "Überarbeite das Aussehen des Item-Overlays.
    /// Aktuell ist es einfach so ein schwarzes Rechteck, das am Rand pulsiert. Das sieht nicht sehr
    /// gut aus. Überlege dir eine andere Darstellung die visuell ansprechender ist aber immer noch
    /// das selbe vermittelt."). It WAS a mirror of the two play-slot recesses: an opaque gold 1.12×
    /// frame, an opaque near-black 1.04× inner plate and a 1.28× additive gold quad breathing on
    /// <c>SlotPulse</c>'s sine. Copying the play slots was the mistake, and it is a geometric one
    /// rather than a matter of taste: those slots lie ON the board's opaque slab, which is what
    /// their dark inner plate reads against. This berth hangs BELOW the board's lower edge
    /// (<see cref="ItemUseSlotBase"/> is −BoardH/2 − 0.095) with nothing behind it at all — so in
    /// mixed reality its backdrop is a chroma-key composite of the player's actual room, and a dark
    /// plate over that is exactly what the report calls it. Worse, near the BLACK key preset a dark
    /// plate is not a rectangle but a HOLE punched through to the passthrough camera.</para>
    ///
    /// <para>SO THE PLATE IS GONE AND THE BERTH IS OPEN. Four pieces, each carrying one of the four
    /// things this widget has to keep saying:</para>
    /// <list type="number">
    /// <item>"A CARD GOES HERE" — a card-shaped, constant-thickness, rounded SOFT OUTLINE at the
    ///   size a card actually lands at. It is the same outline language as the item cards' own
    ///   "usable now" frame and the initiative ring (<see cref="WorldUI.SoftCueArt"/>), so the
    ///   destination is drawn in the same hand as the thing that will fill it. And it is TWO-TONE —
    ///   bright core, dark shoulder on both sides — which is what keeps it legible over a white
    ///   wall and over a dark room alike (see SoftCueArt's CONTOUR note; with the BLACK key preset
    ///   the dark shoulder is keyed away and the bright core carries the cue on its own).</item>
    /// <item>"NOW" — an inward <see cref="WorldUI.SoftCuePing"/>: a ring that sweeps IN and closes
    ///   onto the outline, on the shared item heartbeat. It is the exact mirror of the outward ring
    ///   the closed items pile throws ("look here" ↔ "put it in here"), and it replaces the border
    ///   sine, which was the least noticeable rhythm the widget could have had.</item>
    /// <item>The middle is filled with LIGHT, not darkness — a pale warm translucent field, the same
    ///   recipe <c>ItemsPile.BuildUseGhost</c> uses for the ghost card that previews a drop. The
    ///   berth is now that ghost's resting state, so approaching with a card brightens a shape that
    ///   is already there instead of introducing a second one. In mixed reality the room shows
    ///   through it.</item>
    /// <item>IT IS NOT A BUTTON (the constraint that forced the "USE" caption's restyle one round
    ///   earlier, see the caption comment below). Every mod button is a raised, filled KEYCAP with a
    ///   bright face and travel; this is a hollow outline with an open middle, no face, no travel,
    ///   no press state. A hole you put something into and a cap you push are now maximally
    ///   different objects — and the caption keeps the muted engraved pile-caption voice.</item>
    /// </list>
    ///
    /// <para>And it ARRIVES and DEPARTS: the whole berth grows in with a back-ease overshoot and
    /// collapses out (<see cref="WorldUI.SoftCueReveal"/>). It used to blink in and out on a raw
    /// SetActive — the last unanimated transition in the item flow, and a breach of the standing
    /// "nothing pops" rule.</para>
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

        // EVERYTHING THAT ANIMATES HANGS OFF ONE NODE, so the arrival and the departure are a single
        // motion of a single object rather than four widgets each doing their own thing. The root
        // above keeps the plain SetActive semantics the drop/ghost gates in ItemsPile read.
        var berthGo = new GameObject("Berth");
        berthGo.transform.SetParent(go.transform, worldPositionStays: false);
        berthGo.transform.localPosition = Vector3.zero;
        berthGo.transform.localRotation = Quaternion.identity;
        Transform berth = berthGo.transform;
        var reveal = berthGo.AddComponent<WorldUI.SoftCueReveal>();
        reveal.DeactivateTarget = go;
        reveal.Configure(CardsConfig.ItemBerthRevealSeconds.Value);
        _itemUseReveal = reveal;

        float rectW = w * ItemBerthRectFactor;
        float rectH = h * ItemBerthRectFactor;
        float band = Mathf.Max(0.0008f, CardsConfig.ItemBerthRingThickness.Value);
        float corner = w * ItemBerthCornerFactor;
        // The berth's gold, warmed toward the initiative ring's amber like every other cue in this
        // family, run through the chroma-key guard so no key preset can turn the widget into a hole.
        Color berthGold = WorldUI.SoftCueArt.KeySafe(new Color(1f, 0.80f, 0.36f, 0.92f));

        // 1. THE FIELD — light in the berth, never a dark plate. (0 = a completely open berth.)
        float glow = Mathf.Clamp01(CardsConfig.ItemBerthGlow.Value);
        if (glow > 0.002f)
        {
            GameObject field = WorldUI.SoftCueArt.FieldQuad("Field", berth,
                new Vector3(0f, 0f, ItemBerthFieldZ),
                w * ItemBerthFieldFactor, h * ItemBerthFieldFactor,
                new Color(0.92f, 0.85f, 0.5f, glow)); // BuildUseGhost's own wash, at the berth's rest level
            _itemUseSlotGlow = field.GetComponent<MeshRenderer>().sharedMaterial;
            reveal.Track(field);
        }

        // 2. THE OUTLINE — the card-shaped destination itself.
        GameObject outline = WorldUI.SoftCueArt.RectOutlineQuad("Outline", berth,
            new Vector3(0f, 0f, ItemBerthOutlineZ), rectW, rectH, band, corner, berthGold);
        reveal.Track(outline);

        // 3. THE INWARD PING — "put it in HERE", on the shared item heartbeat.
        float pingSeconds = CardsConfig.ItemBerthPingSeconds.Value;
        float pingReach = Mathf.Max(1f, CardsConfig.ItemBerthPingReach.Value);
        if (pingSeconds > 0.01f && pingReach > 1.001f)
        {
            GameObject ping = WorldUI.SoftCueArt.RectOutlineQuad("Ping", berth,
                new Vector3(0f, 0f, ItemBerthPingZ), rectW, rectH, band, corner, berthGold);
            ping.AddComponent<WorldUI.SoftCuePing>().Init(
                ping.GetComponent<MeshRenderer>(), berthGold,
                new Vector3(rectW * pingReach, rectH * pingReach, 1f),
                new Vector3(rectW, rectH, 1f),
                pingSeconds);
        }

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
        //
        // …AND IT IS A LABEL, NOT A KEYCAP (user report 2026-08-08: "Das 'Use' unten drunter
        // erscheint eher wie ein button (was er nicht ist), überarbeite daher nochmal die visuelle
        // Darstellung"). The caption was styled in the mod's BUTTON voice — bright keycap cream
        // (1, 0.92, 0.72), the same tone the Confirm/Undo/Use caps wear, fitted into a 3 cm-tall box
        // so the glyphs came out roughly twice the height of the pile captions right beside it. Next
        // to a REAL "USE" keycap in the Confirm/Undo column carrying the very same word, that read as
        // a second, pressable button — and poking it does nothing, because it is engraving.
        //
        // It now wears the board's CAPTION voice, byte-for-byte the one the three pile stacks use
        // ("ABGEWORFEN" / "VERBRANNT" / "GEGENSTÄNDE" — PileViewer.PileStack.Create): the same muted
        // parchment colour, the same native HUD font, and the SAME fit box and font ceiling, so at
        // the shipped pile scale the glyphs come out the same physical size. One glance now sorts the
        // board into "things you press" (bright caps, raised faces) and "things that name a zone"
        // (muted engraving under the zone) — and this is the latter.
        //
        // …AND IT NOW RIDES THE BERTH'S ARRIVAL. It hangs off the animated node rather than off the
        // slot root, so the caption grows in and collapses out WITH the outline it names instead of
        // blinking beside a widget that is animating. It is moved by the reveal's SCALE only —
        // SoftCueReveal deliberately never writes a TMP's colour, because a TextMeshPro draws
        // through one font-atlas material shared with every label in the game and its MR backing
        // plate through one shared plate material (see SoftCueReveal.Track).
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(berth, worldPositionStays: false);
        labelGo.transform.localPosition = new Vector3(0f, -(h * 0.5f + ItemUseLabelDrop), -0.002f);
        var label = labelGo.AddComponent<TextMeshPro>();
        // MOD string, not Loc.Game("GUI_USE"): that key does not resolve in this build, so the
        // English fallback shipped and a German board read "USE" (see Loc's item_use_area note).
        label.text = ItemUseAreaCaption();
        label.alignment = TextAlignmentOptions.Center;
        label.color = ItemUseLabelColor;
        WorldUI.NativeButtonSkin.ApplyFont(label);
        // …AND THE RIM (user request 5, 2026-09-03: "Gewährleiste, dass der Text lesbar ist auf den
        // Boards"). This caption is parked OUTSIDE the recess, which is the same sentence two lines
        // down that explains why it needs an MR backing — and outside the recess means that in
        // NORMAL VR there is nothing behind it either, only whatever the scenario put there (the
        // battle goal, measured on the same scene, falls to 2.24:1 where it crosses lit stone). The
        // colour is unchanged; it gains the dark keyline the battle goal and the objectives block
        // now carry, so all three read the same way against the same forest.
        WorldUI.NativeButtonSkin.StyleWorldReadableLabel(label);
        // The pile captions' own fit call. Note it is WIDER and SHORTER than the card-width box that
        // used to be here: the width is what stops a long word ("BENUTZEN") from being squeezed, and
        // the 2.4 cm height is what keeps the glyphs at caption size instead of letting TMP grow them
        // to keycap size in the empty strip below the recess.
        Core.TmpFit.Fit(label, ItemUseLabelWidth, ItemUseLabelHeight, maxFontSize: 0.22f, wrap: false);
        // Parked OUTSIDE the recess (below) means nothing is behind it any more — back it in MR.
        WorldUI.MrBacking.Label(label);
        _itemUseCaption = label;

        // Requirement 9a: the item "Use" confirm is no longer a bespoke keycap beside the slot — it is a
        // GENERIC cluster board button in the right-hand Confirm/Undo column (built in BuildButtons,
        // positioned above the pair by SetConfirmUndoOffset), shown only while a card is clipped into the
        // slot awaiting the decision. See BuildButtons / SetItemUseConfirmVisible.

        Core.VRLayers.Apply(go);
        // 2026-08-04 (status-placard defect family): every piece of this berth is a depth-less
        // alpha-blended transparent (there is no opaque quad left in it at all since the plate went)
        // — ride the board's furniture order group so a panel BEHIND the board cannot paint over it.
        AdoptFurniture(go);
        go.SetActive(false); // ItemsPile toggles it live via SetItemUseSlotVisible
        _itemUseSlotWanted = false; // a freshly built berth starts hidden; keep the logical state with it
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

    /// <summary>
    /// THE WHOLE ITEM-USE AREA'S WORDING, in one call — the cap AND the caption engraved under the
    /// recess. Set by <see cref="ItemsPile"/> the moment a demand opens (surrender / refresh /
    /// goal-chest forfeit) and cleared to null when it ends; null means the neutral zone name.
    ///
    /// <para>THE REPORTED DEFECT (user, 2026-09-03): "Das Item-Overlay wird unter dem Controllboard
    /// angezeigt, allerdings steht da noch 'benutzen' — das passt hier nicht, da ja ein Gegenstand
    /// abgeworfen wird der dort hingelegt wird." The KEYCAP has carried the per-flow override since
    /// ModBuild 352 (<see cref="SetItemUseConfirmVisible"/>), and it still does. The CAPTION did not:
    /// <c>BuildItemUseSlot</c> wrote <c>Loc.Mod("item_use_area")</c> into it once, at construction,
    /// and nothing ever re-read it — so the engraving under the recess an item is being SURRENDERED
    /// into read "BENUTZEN" for the whole flow. Two widgets naming one zone, one of them relabelled.</para>
    ///
    /// <para>IT IS ALSO EARLIER THAN THE CAP. The cap only exists once the picker reports the
    /// selection READY, i.e. after the card is already in the recess; the caption is up from the
    /// first frame of the demand, which is exactly when the player needs to be told what the recess
    /// is for. That is why this is its own call and not a parameter of the confirm's.</para>
    /// </summary>
    internal void SetItemUseAreaLabel(string? label)
    {
        if (_itemUseAreaLabel == label)
            return;
        _itemUseAreaLabel = label;
        ApplyItemUseCaption();
    }

    /// <summary>The word the recess engraving currently wants — the live per-flow override, or the
    /// neutral zone name. The SINGLE source both the build path and the live relabel read, so a
    /// caption built during a demand and one relabelled into it cannot come out different.</summary>
    private string ItemUseAreaCaption() =>
        (_itemUseAreaLabel ?? Core.Loc.Mod("item_use_area")).ToUpperInvariant();

    /// <summary>Re-state the caption and RE-FIT it. The re-fit is the point: the neutral word is one
    /// short token and a demand's is two long ones, and a box solved for the first truncates the
    /// second (ModBuild 281's truncation family).</summary>
    private void ApplyItemUseCaption()
    {
        TMPro.TextMeshPro? label = _itemUseCaption;
        if (label == null)
            return;
        string want = ItemUseAreaCaption();
        if (label.text == want)
            return;
        label.text = want;
        Core.TmpFit.Fit(label, ItemUseLabelWidth, ItemUseLabelHeight, maxFontSize: 0.22f, wrap: false);
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

    /// <summary>
    /// Show/hide the item-use berth (idempotent). Driven live by <see cref="ItemsPile"/>'s gate.
    ///
    /// <para>ANIMATED SINCE 2026-08-09 — this was a raw <c>SetActive</c>, i.e. the one transition in
    /// the whole item flow that popped, against the standing "nothing that appears, disappears or
    /// moves does so without an animation" rule. Showing activates the root and plays the berth's
    /// arrival; hiding plays the departure and lets <see cref="WorldUI.SoftCueReveal"/> switch the
    /// root off at the END of it, because a component cannot animate its own disappearance from
    /// inside a GameObject that has already been deactivated.</para>
    ///
    /// <para>SO THE ROOT STAYS ACTIVE FOR THE LENGTH OF THE COLLAPSE (a fraction of a second), and
    /// the two gates in <see cref="ItemsPile"/> that read its <c>activeSelf</c> see it. Both are
    /// coarse pre-filters in front of the real authority and neither can act on its own:
    /// <c>TickUseGhost</c> also requires the pile's own live "show the use slot" answer, which has
    /// just gone false, and <c>OnChipReleased</c> goes on to ask the game's demand / active-bonus /
    /// activatable checks, which are what actually decide whether a drop uses anything. The LOGICAL
    /// state is tracked separately in <see cref="_itemUseSlotWanted"/> and is what the multiplayer
    /// mirror is driven from (<see cref="ItemUseSlotShown"/>), so a peer's copy flips on the same
    /// edge this animation starts on rather than at the end of it.</para>
    /// </summary>
    internal void SetItemUseSlotVisible(bool visible)
    {
        if (_itemUseSlot == null)
            return;
        bool active = _itemUseSlot.gameObject.activeSelf;
        // Re-assert a SHOW whose root went inactive under us (board rebuild / teardown races) —
        // the wanted flag alone would latch the berth away for the rest of the decision.
        if (visible == _itemUseSlotWanted && (!visible || active))
            return;
        _itemUseSlotWanted = visible;
        if (_itemUseReveal == null)
        {
            if (active != visible)
                _itemUseSlot.gameObject.SetActive(visible); // shader-less fallback: no art, no reveal
            return;
        }
        if (visible)
        {
            if (!active)
                _itemUseSlot.gameObject.SetActive(true);
            _itemUseReveal.Show();
        }
        else
        {
            _itemUseReveal.Hide(); // deactivates the root once the collapse has played out
        }
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

    // =============================================================================================
    //  IsRoundSlotCard(VRCard) is REMOVED — the invariant it encoded was RETIRED, not just unused.
    //
    //  It answered "is this card parked in a round-card slot", and its whole reason for existing was
    //  the sentence in its own doc: round-slot cards "are the ONLY local cards whose faces peers ever
    //  see", which made it the load-bearing half of the tooltip identity gate
    //  (WorldUI.WorldTooltips.ContentPublicToPeers) — a card that failed it was "backs-only on every
    //  peer forever", so its tooltip could never ride the wire.
    //
    //  The user ruling of 2026-08-08 makes that sentence FALSE:
    //      "Die Oberseiten der Karten des remote Spielers soll auch überall sichtbar sein, sei es
    //       Karten in der Hand, der Hand-Karten-Pile oder einer der Piles aus dem Board (Items/
    //       Abgeworfen/Verbrannt). … NUR in der Auswahlphase sieht man überall nur die Rückseiten von
    //       remote spielern, in allen anderen Phasen, ist alles sichtbar."
    //  Peers now draw FRONTS for the hand fan (Net.RemoteHandFan), for the discard/burnt/items browse
    //  arcs (Net.RemoteBrowserFan) and for the item fan (Net.RemoteItemFan), all behind the same
    //  Net.RevealGate the two slots already used. Secrecy is a PHASE, not a PLACE — so the tooltip
    //  gate asks Net.RevealGate.PeersSeeOurCardFronts and asks nothing at all about WHERE the card
    //  lies, which left this predicate with no callers and, more importantly, no meaning.
    //
    //  The "physical truth, not a flag" idea it shared with OccupiedSlotMask is NOT retired — that
    //  mask still rides the board-UI record so a peer's recess shows an occupied back. Only the
    //  card-side question, and only its secrecy role, is gone. Beware a bare-name grep: nothing else
    //  in the mod ever called it.
    // =============================================================================================

    /// <summary>True while a CONFIRM control is visible on this board — the mod keycap, or the
    /// docked native Continue widget that replaces it at the same spot (either way the owner
    /// SEES a confirm control there, which is what a peer must reproduce).</summary>
    internal bool ConfirmControlShown =>
        (_confirm != null && _confirm.LogicalVisible)
        || (WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible);

    /// <summary>
    /// The text the SHOWN confirm control currently displays — the multiplayer cap-label read seam
    /// (wire record <c>NetProtocol.ExtIdCapLabels</c> bit 0). Which control answers follows
    /// <see cref="ConfirmControlShown"/>'s own precedence: the docked NATIVE Continue widget when
    /// it is the visible one (its live <c>ReadyButton.buttonText</c> — the 16-state wording the
    /// game rewrites per flow, e.g. "Fortfahren"), else the mod keycap's current label (which
    /// TickStatus already drives through the pick overrides / "✓ READY" / ConfirmLabel logic).
    /// Null while no confirm control is shown, so the wire record is omitted exactly then.
    /// Peers render this string verbatim on their copy's confirm cap; their neutral GUI_CONFIRM
    /// re-localization is only the no-record fallback — the fix for the hardware-test report
    /// "mein Mitspieler las 'Fortfahren', ich sehe 'Bestätigen'" ("my teammate read 'Continue',
    /// I see 'Confirm'").
    /// </summary>
    internal string? ConfirmControlLabel
    {
        get
        {
            if (WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
                && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible)
            {
                ReadyButton? native = Choreographer.s_Choreographer != null
                    ? Choreographer.s_Choreographer.readyButton
                    : null;
                return native != null && native.buttonText != null ? native.buttonText.text : null;
            }
            return _confirm != null && _confirm.LogicalVisible ? _confirm.CurrentLabel : null;
        }
    }

    /// <summary>True while an UNDO control is visible on this board (mod keycap or the docked
    /// native Undo widget) — see <see cref="ConfirmControlShown"/>.</summary>
    internal bool UndoControlShown =>
        (_undo != null && _undo.LogicalVisible)
        || WorldUI.Surfaces.TrayControlDockSurface.UndoDocked;

    /// <summary>
    /// The text the SHOWN undo control currently displays — the multiplayer cap-label read seam
    /// (wire record <c>NetProtocol.ExtIdCapLabels</c> bit 2), with exactly
    /// <see cref="ConfirmControlLabel"/>'s shape and precedence.
    ///
    /// <para>WHY IT NEEDED ITS OWN WIRE SLOT. The UNDO keycap has TWO wordings. Normally it is the
    /// game's live undo string (<c>CardsGameApi.UndoLabel()</c>); during the EVENT-DISCARD pick
    /// flow the driver overrides it with the confirm dialog's own CANCEL option
    /// (<see cref="SetPickStatus"/>'s <c>undoLabel</c>, e.g. "Wähle eine andere Karte") — in that
    /// flow this cap IS the popup's second button. Peers wrote a flat GUI_UNDO, so the owner's
    /// cancel affordance read "Rückgängig" on every other screen: the same defect record 13 was
    /// created for, one cap to the left.</para>
    /// </summary>
    internal string? UndoControlLabel
    {
        get
        {
            if (WorldUI.Surfaces.TrayControlDockSurface.UndoDocked)
            {
                UndoButton? native = Choreographer.s_Choreographer != null
                    ? Choreographer.s_Choreographer.m_UndoButton
                    : null;
                return native != null && native.m_ButtonText != null ? native.m_ButtonText.text : null;
            }
            return _undo != null && _undo.LogicalVisible ? _undo.CurrentLabel : null;
        }
    }

    // ---- the turn-flow SKIP's three wire read seams ------------------------------------------
    //
    // THEY MOVED HERE FROM `WorldUI.ButtonCluster`, WHERE THEY WERE STATICS, and the move is the
    // whole reason the wire needed no new field: the three facts a peer draws its skip cap from —
    // is it shown, is it pressable, what does it say — are the same three facts, sampled by the same
    // NetAvatarDriver lines, into the same bit of the same record. All that changed is which object
    // owns the cap they are read off. `BoardSkipShown` / `BoardSkipEnabled` / `BoardSkipLabel` were
    // statics because the cluster instance was a private of WorldUIModule; the cap is a member of
    // the tray now, and `PlayTray.Current` is how every other board seam is already reached.

    /// <summary>True while the turn-flow SKIP keycap is visible on this board — the multiplayer
    /// board-UI read seam (record bit <c>NetProtocol.BoardUiSkipBit</c>). Reads the LOGICAL state,
    /// so it flips on the edge the owner's own crumble/assemble starts on.</summary>
    internal bool SkipCapShown => _skip != null && _skip.LogicalVisible;

    /// <summary>True while that cap is also PRESSABLE — the cap-STATE byte's
    /// <c>NetProtocol.BoardUiCapSkipEnabledBit</c>. The disabled look is not a palette swap but a
    /// lerp toward dark wood with a faded label, and a peer drawing the enabled look for a dead skip
    /// is the same picture for two different states.</summary>
    internal bool SkipCapEnabled => SkipCapShown && _skip!.StateEnabled;

    /// <summary>
    /// The wording the shown SKIP cap displays — cap-label read seam (wire record
    /// <c>NetProtocol.ExtIdCapLabels</c> bit 1). Null while the cap is down, so the record is
    /// omitted exactly then.
    ///
    /// <para>The game gives this ONE widget six wordings (GUI_SKIP_MOVEMENT / _ATTACK / _ABILITY /
    /// _PUSH / _PULL plus a computed targeting term), which is why it has a wire slot at all: peers
    /// render the owner's string verbatim on their copy — their board, their language, the
    /// pick-banner rule — and the neutral GUI_SKIP_MOVEMENT re-localization is only their no-record
    /// fallback.</para>
    /// </summary>
    internal string? SkipCapLabel => SkipCapShown ? _skip!.CurrentLabel : null;

    /// <summary>
    /// The text the item-USE cap currently displays — the multiplayer cap-label read seam (wire
    /// record <c>NetProtocol.ExtIdCapLabels</c> bit 3). Null while the cap is not up, so the
    /// record's presence tracks <see cref="ItemUseCapShown"/>.
    ///
    /// <para>WHY: an item-SURRENDER demand (event malus) overrides this cap's wording so "the user
    /// must never read a surrender as an ordinary use" (see
    /// <see cref="SetItemUseConfirmVisible"/>). The mirror hardcoded the localized "USE", so a peer
    /// watching a player hand an item over saw them apparently USE it. A widget label, never an
    /// item name.</para>
    /// <para>IT IS THE WHOLE AREA'S WORDING SINCE ModBuild 378, not only the cap's. The engraved
    /// caption under the recess carries the same per-flow word (see
    /// <see cref="SetItemUseAreaLabel"/>) and is up from the FIRST frame of a demand, long before
    /// the cap exists — so the seam reports the area label first and falls back to the live cap
    /// text. That is a widening of an existing field's MEANING, not a new one: record 13 bit 3
    /// still carries one string, the mirror still applies it to its cap, and it now also states
    /// its caption from it. A peer therefore reads "ITEM ABGEBEN" under the mirrored recess at the
    /// same moment the owner does, which is what the 1:1 rule asks for and what no new wire field
    /// was needed to buy.</para>
    /// </summary>
    internal string? ItemUseCapLabel =>
        _itemUseAreaLabel
        ?? (_itemUseActive && _itemUseConfirm != null && _itemUseConfirm.LogicalVisible
            ? _itemUseConfirm.CurrentLabel
            : null);

    /// <summary>
    /// The slot whose GOLD SNAP GLOW is lit right now (-1 = none) — the multiplayer read seam for
    /// the board-UI record's snap field (byte 1 bits 6..7).
    ///
    /// <para>This is a HOVER telegraph, not a drop report: <c>CardsDriver.UpdateSlotHighlight</c>
    /// lights it while a held card is within snap range of a recess, moves it as the hand crosses
    /// between the two, and clears it when the card leaves — "what glows is what drops". A peer's
    /// mirrored board used to light its rim on the OCCUPANCY edge instead, i.e. after the drop and
    /// never at all for a hover that ended without one. Reading the field the local glow itself is
    /// driven from is what makes the two agree by construction.</para>
    /// </summary>
    internal int HighlightedSlot => _highlightedSlot;

    /// <summary>True while the CONFIRM keycap is ACCENTED — multiplayer cap-STATE read seam
    /// (board-UI record byte 2 bit 0), read off the flag the local renderer itself obeys. False
    /// when no mod keycap exists (the native dock's own look is not a mod cap state).</summary>
    internal bool ConfirmCapAccent => _confirm != null && _confirm.StateAccent;

    /// <summary>True while the CONFIRM keycap is in its CONFIRMED (readied, worn-brass) state —
    /// see <see cref="ConfirmCapAccent"/>.</summary>
    internal bool ConfirmCapConfirmed => _confirm != null && _confirm.StateConfirmed;

    /// <summary>True while the item-use clip-in BERTH is shown (ItemsPile toggles it while the owner
    /// is handling a usable item).
    ///
    /// <para>Reads the LOGICAL state, not the root's <c>activeSelf</c>: since the berth animates out
    /// (see <see cref="SetItemUseSlotVisible"/>) the GameObject outlives the decision by the length
    /// of the collapse, and a peer must flip on the edge the owner's own animation starts on — that
    /// is what lets the mirror play its own matching arrival/departure instead of popping a frame
    /// late.</para></summary>
    internal bool ItemUseSlotShown => _itemUseSlot != null && _itemUseSlotWanted;

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
