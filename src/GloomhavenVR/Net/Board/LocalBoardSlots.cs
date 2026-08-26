using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE OWNER'S OWN ROUND-CARD RECESSES, read PHYSICALLY, for the two facts a receiver has been
/// re-deriving instead of being told (hardware MP session 2026-08-15, items 6 and 8).
///
/// ─── WHY THIS CLASS EXISTS AT ALL ──────────────────────────────────────────────────────────────
/// Both defects have the same shape: something about the owner's board that is DECIDED on the
/// owner's machine was never stated, so every other client answered the question with a rule of
/// its own — and the rules disagree.
///
///   ITEM 8, the LEFT/RIGHT ORDER of the two docked cards ("Die Position der Karten (linke
///   Karte/rechte Karte) war in einem Test verdreht wenn ich einen Character anklicke die einem
///   anderen Spieler gehört. Die Reihenfolge MUSS zwingend identisch sein wie es der jenige
///   Spieler auch sieht."). Three surfaces, three DIFFERENT derivations, none transmitted:
///     • the owner's tray seats <c>InitiativeAbilityCard</c> into recess 0
///       (<c>PlayTray.SyncFromGameState</c>, <c>CardsDriver.ReconcileInitiative</c>);
///     • the owner's ACTION-phase dock and every watcher's focus dock take the iteration order of
///       that client's own <c>CardsHandUI.cardsUI</c> list (<c>CardsDriver.CollectRoundCards</c>
///       → <c>HalfSelection.SetCards</c>, which docks list index i into recess i) — a list sorted
///       by <c>AbilityCardUI.CompareTo</c> whenever THAT client last called <c>SortCards()</c>;
///     • the mirrored peer board uses the initiative rule again
///       (<c>RemoteControlBoard.OrderRoundCards</c>).
///   Session proof, one session on three machines: the owner of 'Hilde Die 2Te' placed
///   UnbridledPower LEFT and FatalFury RIGHT during selection
///   (<c>remote2/LogOutput.log:46564-46565</c>) but his own action dock then showed FatalFury LEFT
///   (<c>remote2:46580-46581</c>); the reporter's focus dock showed FatalFury LEFT too
///   (<c>LogOutput.log:50056-50057</c>) while the mirrored board on the same machine kept
///   UnbridledPower LEFT. The two rules agree only by luck — <c>LogOutput.log:34433/34455</c> is a
///   round where they happened to coincide, which is exactly the "in EINEM Test verdreht" the
///   report describes.
///
///   ITEM 6, the STANDARD-ACTION fields ("Wenn jemand die standart Aktion ausgewählt hat oder
///   drüber hovered wird trotzdem der große untere bzw obere Bereich der Karte bei den remote
///   boards angezeigt/gehighlighted"). See <see cref="NetProtocol.HalfDefaultHoverBit"/> for the
///   mechanism; the sampling half lives here because it needs the same physical recess read.
///
/// ─── WHY THE RECESSES ARE READ FROM THE SCENE ──────────────────────────────────────────────────
/// The identical argument <c>PlayTray.OccupiedSlotMask</c> already makes, and this class
/// deliberately shares its source: <c>PlayTray._occupants</c> is maintained only by
/// <c>PlaceCard</c>, and the ACTION-phase dock does not go through it — <c>HalfSelection.SetCards</c>
/// re-homes the cards onto the same slot transforms directly. A field read would therefore be
/// blind in exactly the phase both defects were reported in. The slot TRANSFORM's own live child
/// is the physical truth in every phase, which is also what makes it the same truth the already
/// synced occupancy nibble states.
///
/// NOTHING HERE PUTS A CARD ON THE WIRE. The sampler resolves identities locally and hands its
/// callers a BIT (which of two orders) and three BITS (which of two regions) — see
/// <see cref="NetProtocol.ExtIdSlotOrder"/> for why an order is not an identity.
/// </summary>
internal static class LocalBoardSlots
{
    /// <summary>The live, un-held VR card physically parented to the tray's round-card recess
    /// <paramref name="slot"/>, or null. Mirrors <c>PlayTray.SlotHoldsCard</c>'s walk exactly (a
    /// HELD card reads as empty there and here — the owner is holding it in their hand, and the
    /// hand is separately synced), so this and the occupancy nibble can never disagree.</summary>
    private static VRCard? CardInSlot(PlayTray? tray, int slot)
    {
        Transform? anchor = tray != null ? tray.SlotTransform(slot) : null;
        if (anchor == null)
            return null;
        for (int c = 0; c < anchor.childCount; c++)
        {
            Transform child = anchor.GetChild(c);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;
            var card = child.GetComponent<VRCard>();
            if (card != null && !card.IsHeld)
                return card;
        }
        return null;
    }

    /// <summary>The game widget of the card in recess <paramref name="slot"/>, or null.</summary>
    private static FullAbilityCard? WidgetInSlot(PlayTray? tray, int slot)
    {
        VRCard? card = CardInSlot(tray, slot);
        return card != null ? card.FullCard : null;
    }

    /// <summary>
    /// Which of the two round cards lies in the LEFT recess, as the one bit record 18 carries:
    /// false = recess 0 holds <c>CCharacterClass.InitiativeAbilityCard</c> (the legacy assumption
    /// every receiver makes today), true = recess 0 holds the OTHER round card.
    ///
    /// <para>Returns false when the answer is not knowable this frame — no tray, no local actor,
    /// fewer than two resolvable cards, or a pair in which neither card is the initiative card.
    /// The caller then omits the record entirely, and a receiver keeps its existing derivation:
    /// this fact may only ever REPLACE a guess with the owner's truth, never overwrite it with a
    /// second guess.</para>
    ///
    /// <para>WHY THE INITIATIVE CARD IS THE REFERENCE and not, say, an index into
    /// <c>RoundAbilityCards</c>: <c>InitiativeAbilityCard</c> is a single replicated REFERENCE
    /// every client resolves to the same card, whereas the LIST's order is a per-client property
    /// of the same kind that caused this defect in the first place. One bit against a
    /// client-independent reference cannot inherit the bug it is fixing.</para>
    /// </summary>
    internal static bool TrySampleSlotOrder(PlayTray? tray, CardsHandUI? hand, out bool swapped)
    {
        swapped = false;
        try
        {
            CAbilityCard? initiative = hand?.PlayerActor?.CharacterClass?.InitiativeAbilityCard;
            if (initiative == null)
                return false;
            CAbilityCard? left = WidgetInSlot(tray, 0)?.abilityCard;
            CAbilityCard? right = WidgetInSlot(tray, 1)?.abilityCard;
            if (left == null || right == null)
                return false; // one recess (or none) — there is no ORDER to state
            if (ReferenceEquals(left, initiative))
                return true;  // recess 0 = the initiative card: the legacy order, stated as fact
            if (ReferenceEquals(right, initiative))
            {
                swapped = true;
                return true;
            }
            return false;     // neither is the initiative card: say nothing rather than guess
        }
        catch (System.Exception)
        {
            return false;     // a card mid-teardown must never take down the extras sender
        }
    }

    /// <summary>
    /// The three STANDARD-ACTION qualifier bits of record 14 byte 2 (see
    /// <see cref="NetProtocol.HalfDefaultHoverBit"/>): for the hover and for each slot's
    /// selection that the caller has already sampled, is the region the half's small default
    /// "Attack 2" / "Move 2" chip rather than the big action half?
    ///
    /// <para>SELECTION is read off the game's own second latch — <c>isSelectedDefaultAction</c>,
    /// the twin of the <c>isSelected</c> that <c>HalfSelection.SelectedHalfOf</c> already folds
    /// into the transmitted half. Reading the pair here SPLITS what that method merged, which is
    /// the whole defect.</para>
    ///
    /// <para>AGREEMENT GUARD: a slot's bit is set only when this class's own read of that slot's
    /// selected half AGREES with the half the caller is about to transmit. The two reads walk
    /// different lists (this one the tray's recesses, the caller's <c>HalfSelection</c>'s docked
    /// list), and until item 8's order fact is honoured everywhere those lists can disagree — in
    /// which case the qualifier would name the wrong card's chip. Disagreement therefore degrades
    /// to "the big half", i.e. exactly the picture every build before this one drew, and never to
    /// a confidently wrong one.</para>
    ///
    /// <para>HOVER is read off the mod's own uGUI pointer rather than a game latch, because the
    /// game has no latch for it: <c>FullAbilityCardAction</c> stores no hover state at all (its
    /// chip hover is a uGUI sprite swap driven by <c>defaultActionButton.OnPointerEnter</c>). The
    /// laser is also the ONLY input that can reach a chip — the mod's poke volumes are the two
    /// half zones and commit <c>TopAction</c>/<c>BottomAction</c> only
    /// (<c>HalfSelection.HalfZone</c>) — so "the beam's hovered uGUI object is the chip, or inside
    /// it" is not an approximation of the reachable cases, it is all of them. The owner's log
    /// shows the beam doing exactly this: <c>remote2/LogOutput.log:47826</c> "uGUI hover ENTER:
    /// 'Default action button' (laser-R)" through <c>:48266</c>'s EXIT, while the sender kept
    /// emitting "Half hover SENT: slot 1, BOTTOM half" (<c>:48159</c>, <c>:48216</c>).</para>
    /// </summary>
    internal static void SampleDefaults(PlayTray? tray,
                                        bool hover, int hoverSlot, bool hoverTop,
                                        int sel0, int sel1,
                                        out bool hoverDefault,
                                        out bool sel0Default, out bool sel1Default)
    {
        hoverDefault = false;
        sel0Default = false;
        sel1Default = false;
        if (tray == null)
            return;
        try
        {
            sel0Default = SelectionIsDefault(tray, 0, sel0);
            sel1Default = SelectionIsDefault(tray, 1, sel1);
            if (hover && hoverSlot >= 0)
                hoverDefault = HoverIsDefault(tray, hoverSlot, hoverTop);
        }
        catch (System.Exception)
        {
            hoverDefault = false;
            sel0Default = false;
            sel1Default = false;
        }
    }

    /// <summary>Is <paramref name="slot"/>'s committed selection its STANDARD action? Guarded by
    /// the agreement test described on <see cref="SampleDefaults"/>: the half this class reads
    /// must be the half the caller is transmitting.</summary>
    private static bool SelectionIsDefault(PlayTray tray, int slot, int transmittedHalf)
    {
        if (transmittedHalf != NetProtocol.HalfSelectTop
            && transmittedHalf != NetProtocol.HalfSelectBottom)
            return false; // nothing selected here — nothing to qualify
        FullAbilityCard? full = WidgetInSlot(tray, slot);
        if (full == null)
            return false;
        FullAbilityCardAction? action = transmittedHalf == NetProtocol.HalfSelectTop
            ? full.topActionButton
            : full.bottomActionButton;
        if (action == null)
            return false;
        // AGREEMENT: this half really is the one the game has latched. Either latch satisfies it;
        // which of the two is set is the answer itself.
        if (!action.isSelected && !action.isSelectedDefaultAction)
            return false;
        return action.isSelectedDefaultAction && !action.isSelected;
    }

    /// <summary>Is the beam sitting on the hovered half's standard-action chip? Walks the hovered
    /// uGUI object's ancestor chain up to the chip's own button, so a hit on the chip's Move/Attack
    /// glyph child counts as a hit on the chip.</summary>
    private static bool HoverIsDefault(PlayTray tray, int slot, bool top)
    {
        FullAbilityCard? full = WidgetInSlot(tray, slot);
        FullAbilityCardAction? action = full == null
            ? null
            : top ? full.topActionButton : full.bottomActionButton;
        UnityEngine.UI.Button? chip = action != null ? action.defaultActionButton : null;
        if (chip == null)
            return false;
        return IsUnder(VRHands.Left, chip.transform) || IsUnder(VRHands.Right, chip.transform);

        static bool IsUnder(VRHand? hand, Transform chipRoot)
        {
            GameObject? hovered = hand != null && hand.RayUgui != null ? hand.RayUgui.Hovered : null;
            if (hovered == null)
                return false;
            Transform? t = hovered.transform;
            while (t != null)
            {
                if (ReferenceEquals(t, chipRoot))
                    return true;
                t = t.parent;
            }
            return false;
        }
    }
}
