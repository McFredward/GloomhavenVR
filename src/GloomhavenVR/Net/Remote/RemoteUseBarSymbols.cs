using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// THE SYMBOL ON A PEER'S USE-BAR SLOT — the game's own icon, resolved on THIS machine against
/// THIS client's own copy of the bar. Zero wire bytes.
///
/// ─── THE DEFECT THIS RETIRES ───────────────────────────────────────────────────────────────────
/// User report 2026-08-13, verbatim: "Die Entscheidungsleiste wird immer noch nicht richtig
/// synchronisiert. Zwar sieht man die Buttons bei Schaden (aber auch nicht richtig …), alle anderen
/// Dinge wie entscheidungen wegen Gegenständen etc. sieht man nur eine box. Ich will 1:1 genau das
/// sehen wie der Mitspieler - das gleiche Symbol vom Spiel, in genau der gleichen Größe und
/// Position."
///
/// The "Box" is identified, not guessed. The ModBuild-137 logs say it outright — on the frame of the
/// screenshot the receiving client wrote
/// <c>"Remote use bars: 1 mirrored bar row(s), 1 slot tile(s) (mask 0x01)"</c>, and the sending
/// client wrote <c>"USE BARS: bonus-bar split KEPT the decision-area row for
/// 'ITEM_NAME_BootsofSpeed'"</c>. So the thing on screen was NOT the decision dock's button row at
/// all (record 12 published nothing that session outside the take-damage prompt): it was ONE
/// mirrored USE-BAR SLOT, and a mirrored use-bar slot was a flat coloured quad by design.
///
/// ─── WHY IT WAS ANONYMOUS, AND WHY THAT ARGUMENT DOES NOT HOLD ─────────────────────────────────
/// <c>RemoteBoardFurniture.SetUseBars</c> stated the reason in its own doc: the game's slot art
/// comes from <c>UIInfoTools.GetItemConfig(item.YMLData.Art).miniIcon</c> and "that art IS card
/// identity, and card identity never rides this wire in any form". Both halves are true and the
/// conclusion still does not follow, because NOTHING HAS TO RIDE THE WIRE. The identity is already
/// on the receiver's machine:
///
///   • The four bars are per-client <c>Singleton</c>s and the game raises them from REPLICATED
///     messages. <c>Choreographer.CheckForInitiativeAdjustments</c> calls
///     <c>UIActiveBonusBar.ShowActiveBonus(msg.m_ActorSpawningMessage, …)</c> with no
///     <c>IsUnderMyControl</c> test at all (only the ready button is gated), so a peer's own bonus
///     bar is populated with the same rows for the same actor. That is the same mechanism
///     <see cref="RemoteDecisionWidgets"/> already leans on for <c>TakeDamagePanel</c>.
///   • Every slot knows its own actor (<c>UIUseSlot.actor</c>) and every bar knows its owner set
///     (<c>UIActiveBonusBar.actors</c>, <c>UIUseItemsBar.actor</c>, …), so "is this MY copy of THAT
///     player's bar?" is a reference comparison, not an inference.
///
/// This is exactly the rule <see cref="RemoteItemCardSource"/> ships for a peer's item faces —
/// structure travels, identity is resolved locally — and it is the direction the standing ruling
/// asks for: make the mirror read the SAME source the owner reads instead of inventing a field.
///
/// ─── THE THREE-WAY GATE (why a wrong symbol is impossible, not merely unlikely) ────────────────
/// A wrong icon on somebody else's decision would be worse than no icon, so the resolve is refused
/// unless ALL of the following hold:
///   1. the local bar exists and its container is alive;
///   2. its owner set CONTAINS the actor whose remote board is being drawn (reference identity);
///   3. its visible slot count equals the count record 25 carried for that bar, walked by the same
///      rule the sender walked (container children, hierarchy order, inactive skipped, non-slot
///      children skipped, and — since ModBuild 480 — a render-hidden plain ITEM slot skipped, which
///      is the sender's <c>IsPlainRenderHidden</c> term read off the hierarchy instead of off its
///      ledger; <see cref="RenderHiddenPlainItem"/> says exactly where the two can still differ, and
///      <c>UseBarsSurface.BarDock.SampleWireSlots</c> is the walk it is matched against).
/// Any failure returns 0 and the caller keeps the anonymous tile it drew before. The peer therefore
/// shows the real symbol or an honest blank — never a symbol from a different decision.
/// <see cref="RefusalReason"/> says WHICH of the three refused, and the caller prints it: a gate that
/// lists three reasons and names none is a gate whose verdict cannot be acted on.
///
/// ─── ModBuild 479: THE PREMISE ABOVE IS FALSE FOR THE PREVENT-DAMAGE PROMPT ────────────────────
/// User report 2026-09-07 item 8, verbatim: <i>"Die entsprechenden Symbole sehe ich auch nicht."</i>
/// His log answers it: <c>DOCK MIRROR: no mirrored use-bar symbol resolved</c>, on the very tick a
/// co-player's <c>CShieldActiveBonus</c> (<c>ABILITY_CARD_WardingStrength</c>) was pending. The
/// counts agreed on both machines — record 25 published <c>mask 0x01 [activeBonus: 1 slot(s)]</c>
/// and the mirror drew one tile — so gate 3 is not what refused. Gate 2 is, and it CANNOT PASS:
///
///   <c>UIScenarioMultiplayerController.RefreshDamagePhase</c> (decompiled, the take-damage entry
///   point, <c>:212-249</c>) branches on the CARD OWNER — <c>m_ActorToShowCardsFor ??
///   m_ActorBeingAttacked</c> at <c>:216-218</c> — NOT on the attacked actor, and the test depends
///   on that actor's type: <c>CPlayerActor.IsUnderMyControl</c> (<c>:238</c>),
///   <c>CHeroSummonActor.Summoner.IsUnderMyControl</c> (<c>:233</c>), <c>FFSNetwork.IsHost</c> for a
///   <c>CEnemyActor</c> (<c>:229</c>). ModBuild 480 corrected that wording here and in three other
///   places; the conclusion is untouched, since exactly one client takes <c>Show</c> and every
///   other takes <c>ShowOtherPlayer</c> (<c>:242</c>, its only call site in the tree).
///
///   And <c>ShowOtherPlayer</c> does more than fail to raise the bars: it HIDES them. Its body
///   (<c>TakeDamagePanel.cs:1102-1134</c>) calls <c>ResetToggles()</c> at <c>:1122</c>, which
///   contains <c>UIUseItemsBar.Hide()</c> (<c>:434</c>) and <c>UIActiveBonusBar.Hide()</c>
///   (<c>:435</c>), and it then ends on <c>myWindow.Hide(instant: true)</c> at <c>:1133</c>. Only
///   the controlling client's <c>TakeDamagePanel.Show</c> reaches <c>ShowItems</c> (<c>:249</c>)
///   and <c>ShowReduceDamageActiveBonuses</c> (<c>:265</c>, <c>:269</c>).
///
/// So for a PEER'S prevent-damage decision this client's <c>UIActiveBonusBar</c> is never
/// populated for that actor and <see cref="BarBelongsTo"/> is false by construction. The class
/// doc's evidence — <c>Choreographer.CheckForInitiativeAdjustments</c> calling
/// <c>ShowActiveBonus</c> with no <c>IsUnderMyControl</c> test — is about the INITIATIVE-ADJUSTMENT
/// bonus, a different prompt with a different entry point, and it was generalised to "the game
/// raises these bars from replicated messages on every client" without being checked against this
/// one. That generalisation is the defect; the code below is doing exactly what it says.
///
/// WHAT WOULD ACTUALLY FIX IT is therefore NOT a better local resolve. The identity is on this
/// machine (<c>UIActiveBonusBar.GetPreventDamageActiveBonuses(actor, abilityType, isLethal)</c> is
/// public and reads only the local model, and <c>IActiveBonus.GetIcon()</c> is the sprite), but
/// picking WHICH bonus fills slot #0 needs the owner's <c>abilityType</c> and lethal flag, and the
/// game's own filter closes over <c>activeBonusSlots</c> — the bar's LIVE slot map, which is empty
/// here. A re-derivation could therefore pick a different bonus than the owner's bar did, which is
/// the one outcome this class exists to prevent.
///
/// ─── ModBuild 479: THAT FIX SHIPPED, AND THIS CLASS IS STILL THE OTHER HALF ────────────────────
/// The honest fix was one slot-identity field on the wire, and it is now
/// <see cref="UseBarSlotIdentity"/> — extension record 45, a 16-bit fold of the game's own
/// cross-machine identity, sampled off the owner's own widget and resolved by the RECEIVER against
/// its own model. <c>RemoteBoardFurniture.ApplyUseBarSymbols</c> prefers it wherever the owner
/// named a slot, because an id read off the widget the owner is looking at outranks any local
/// inference.
///
/// <para>THIS CLASS IS NOT SUPERSEDED. It remains the ONLY source for every bar the record does not
/// carry, for every peer below <c>NetProtocol.UseBarSlotIdentityMinPeerBuild</c>, and for every FLAT
/// or unmodded player.</para>
///
/// <para>THE ABILITIES BAR IS GENUINELY RAISED ON EVERY CLIENT, and ModBuild 480 fixed the citation
/// that said so. The line here used to offer <c>Choreographer.CheckForInitiativeAdjustments</c> as
/// its evidence; that path raises <c>UIActiveBonusBar</c> (bar 0), not <c>UIUseAbilitiesBar</c>
/// (<c>decompiled/GH.Runtime/Choreographer.cs:11678</c>). The claim is true on different lines —
/// <c>Choreographer.cs:8331</c> (<c>ShowInfuseAbilities</c>), <c>:8386</c>
/// (<c>ShowChooseAbility</c>), <c>:8503</c> (<c>ShowGenericInfusion</c>), none of them gated on
/// <c>IsUnderMyControl</c>; the gates at <c>:8342-8349</c> and <c>:8395</c> only pick the
/// <c>ActionProcessor</c> state. A confident comment protecting a claim never checked against the
/// file it names is the same shape as the ModBuild-479 generalisation two paragraphs up.</para>
///
/// <para>THE TWO SOURCES MEET PER SLOT, NOT PER BAR (ModBuild 480). ModBuild 479 wrote "either the
/// owner named that bar's slots or nobody did" and enforced it with a whole-bar switch in
/// <c>RemoteBoardFurniture.ApplyUseBarSymbols</c>. That is false of the record and false of the
/// game: the sender withholds an id by design for <c>CForgoActionsForCompanionActiveBonus</c>
/// while <c>UseBarsSurface.EnforceActiveBonusSplit</c> keeps that row, so a MIXED bar is the normal
/// output of the mod's own split, and the switch deleted this class's answer for every other slot
/// on it. A slot the owner NAMED is still the owner's answer or blank; a slot the owner said
/// NOTHING about is this class's, exactly as it was before record 45 existed.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. The icons come from this client's own
/// game UI, raised by the host-replicated message every client already receives. Slot art stays
/// DELIBERATELY-NOT on the wire. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteUseBarSymbols
{
    /// <summary>Owner scratch for the bar-owner test (single-threaded, one board at a time, on the
    /// 4 Hz content cadence).</summary>
    private static readonly List<CActor> OwnerScratch = new(4);

    /// <summary>One-shot "the local resolve works on this build" line.</summary>
    private static bool s_logged;

    /// <summary>
    /// WHICH ARM OF THE THREE-WAY GATE REFUSED — the field ModBuild 479 added, because the old
    /// refusal line listed all three reasons and named none, and a verdict nobody can act on costs
    /// a hardware round to re-ask. Written by <see cref="Resolve"/> on every call (including the
    /// successful ones, which set <see cref="RefusalReason.None"/>); read by the caller's log line.
    /// </summary>
    internal enum RefusalReason
    {
        /// <summary>No refusal — the resolve succeeded.</summary>
        None = 0,

        /// <summary>Record 25 published no slots for this bar (or the caller passed no board actor
        /// / no scratch), so there was nothing to resolve. Not a defect.</summary>
        NoWireSlots,

        /// <summary>GATE 1: this client has no live <c>Singleton</c> for that bar, or its slot
        /// container is gone. The bar has never been raised in this session.</summary>
        NoLocalBar,

        /// <summary>GATE 2: the local bar exists but its owner set does not contain the character
        /// this board draws. For a PEER'S PREVENT-DAMAGE prompt this is structural and permanent —
        /// see the ModBuild 479 block in the class doc.</summary>
        NotThisBoardsCharacter,

        /// <summary>GATE 3: the local bar is the right character's but shows a different number of
        /// visible slots than record 25 reported — a genuinely stale or mid-rebuild bar.</summary>
        SlotCountMismatch,

        /// <summary>The walk threw; the warning beside it carries the message.</summary>
        Threw,

        /// <summary>NOT A REFUSAL AND NOT A DEFECT: the local arm was never asked, because the
        /// OWNER named every visible slot of that bar through record 45 and an owner-named slot is
        /// the owner's answer or nothing. Added in ModBuild 480 with the per-slot gate: before it,
        /// this state was reported as <see cref="NoWireSlots"/>, whose text says "record 25 named
        /// no slot for bar N" — the opposite of what had happened.</summary>
        NotAsked,
    }

    /// <summary>Human-readable form of <see cref="RefusalReason"/> for the caller's log line, with
    /// the counts filled in. <paramref name="localSlots"/> is meaningful only for
    /// <see cref="RefusalReason.SlotCountMismatch"/>.</summary>
    internal static string Describe(RefusalReason why, int barIndex, int wireCount, int localSlots) => why switch
    {
        RefusalReason.None => "resolved",
        RefusalReason.NoWireSlots =>
            $"record 25 named no slot for bar {barIndex}, so there was nothing to resolve",
        RefusalReason.NoLocalBar =>
            $"GATE 1 (local bar absent): this client has no live bar-{barIndex} singleton or its "
            + "slot container is gone — the game has never raised that bar here",
        RefusalReason.NotThisBoardsCharacter =>
            $"GATE 2 (wrong character): this client's own bar {barIndex} exists but its owner set "
            + "does not contain the character this board draws. FOR A PEER'S PREVENT-DAMAGE PROMPT "
            + "THIS IS STRUCTURAL, NOT A GLITCH: the game's own "
            + "UIScenarioMultiplayerController sends a non-controlling client to "
            + "TakeDamagePanel.ShowOtherPlayer, which raises neither UIActiveBonusBar nor "
            + "UIUseItemsBar — so the local copy CANNOT be populated for that actor and no local "
            + "resolve can ever succeed here. THIS IS NO LONGER THE END OF THE ROAD: extension "
            + "record 45 (UseBarSlotIdentity) carries the slot's identity from the owner, and the "
            + "line beside this one says whether it answered. Seeing this arm alone means the peer "
            + "named nothing — check its ModBuild in the same line",
        RefusalReason.SlotCountMismatch =>
            $"GATE 3 (slot count): this client's own bar {barIndex} is the right character's but "
            + $"shows {localSlots} visible slot(s) against record 25's {wireCount}. THIS LINE NAMES "
            + "NO CAUSE, and that is deliberate (ModBuild 480): until this build it asserted 'a "
            + "stale or mid-rebuild local bar', which the count alone cannot establish, and a "
            + "refusal reason naming the wrong cause has cost this tree whole rounds. Two "
            + "populations produce this number and only one of them clears itself. (a) A STALE OR "
            + "MID-REBUILD local bar — transient, gone by the next cadence tick. (b) THE TWO SPLITS "
            + "DIVERGED — the sender skips a plain item slot its own UseBarsSurface RENDER-hid, and "
            + "this walk skips a slot that draws nothing HERE, so a bar docked on one machine and "
            + "not on the other counts differently and the mismatch PERSISTS while that lasts. "
            + "WHICH ONE: grep the other machine for 'items-bar SPLIT' on the same tick and compare "
            + "its render-hidden count against this difference; and grep THIS machine for the same "
            + "line, because a bar this client has not docked hides nothing at all",
        RefusalReason.Threw =>
            $"the walk over bar {barIndex} threw (see the warning beside this line)",
        RefusalReason.NotAsked =>
            $"the local resolve was NOT ASKED about bar {barIndex}: the owner named every visible "
            + "slot of it through record 45, and an owner-named slot is the owner's answer or "
            + "blank. Read the wire-identity arm beside this one for why it is blank",
        _ => "unknown",
    };

    /// <summary>How many visible slots the LOCAL bar showed on the last <see cref="Resolve"/> call
    /// — the number <see cref="RefusalReason.SlotCountMismatch"/> is about. Meaningless for the
    /// other arms.</summary>
    internal static int LastLocalSlots { get; private set; }

    /// <summary>
    /// Fill <paramref name="into"/> with the sprite each visible slot of bar
    /// <paramref name="barIndex"/> is wearing on THIS client, for the bar belonging to
    /// <paramref name="boardActor"/>. Returns the number of slots written (0 = refuse, see the
    /// three-way gate in the class doc); entries past the return value are left untouched and
    /// entries inside it may still be null for a slot type that carries no icon.
    /// </summary>
    internal static int Resolve(int barIndex, CPlayerActor? boardActor, int wireCount, Sprite?[] into,
                                out RefusalReason why)
    {
        LastLocalSlots = 0;
        if (boardActor == null || wireCount <= 0 || into == null || into.Length == 0)
        {
            why = RefusalReason.NoWireSlots;
            return 0;
        }
        try
        {
            RectTransform? container = ContainerOf(barIndex);
            if (container == null)
            {
                why = RefusalReason.NoLocalBar;
                return 0;
            }
            if (!BarBelongsTo(barIndex, boardActor))
            {
                why = RefusalReason.NotThisBoardsCharacter;
                return 0;
            }

            int count = 0;
            for (int i = 0; i < container.childCount && count < into.Length; i++)
            {
                Transform child = container.GetChild(i);
                // THE SENDER'S WALK HAS THREE TERMS, AND THIS HAD TWO (ModBuild 480). The comment
                // that stood here said "THE SENDER'S OWN WALK, verbatim: active children only,
                // non-slot children skipped" — and the sender
                // (UseBarsSurface.BarDock.SampleWireSlots) also skips
                // `_owner.IsPlainRenderHidden(child)`. A slot the plain-item split RENDER-hid is
                // still activeSelf — IsPlainRenderHidden's own doc says exactly that — so this walk
                // counted a slot record 25 had dropped, gate 3 refused on the count, and the tiles
                // went anonymous under a refusal string that blamed "a stale local bar". Read
                // RenderHiddenPlainItem before touching it: it is the ledger's observable effect,
                // not the ledger, and it says where the two can still differ.
                if (!child.gameObject.activeSelf || !IsSlot(child))
                    continue;
                if (RenderHiddenPlainItem(child))
                    continue;
                into[count++] = IconOf(child);
            }
            LastLocalSlots = count;
            if (count != wireCount)
            {
                why = RefusalReason.SlotCountMismatch;
                return 0; // the local bar is not showing the owner's row — refuse, keep the tile
            }
            ReportOnce(barIndex, count);
            why = RefusalReason.None;
            return count;
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote use-bar symbols: resolve failed for bar {barIndex} " +
                              $"({e.Message}) — the mirrored tiles stay anonymous this cadence.");
            why = RefusalReason.Threw;
            return 0;
        }
    }

    /// <summary>The bar's slot container on THIS client, by the record-25 bit order (0 = active
    /// bonus, 1 = abilities, 2 = augments, 3 = items — <c>NetProtocol.UseBarActiveBonusBit</c> …).</summary>
    internal static RectTransform? ContainerOf(int barIndex) => barIndex switch
    {
        0 => Singleton<UIActiveBonusBar>.IsInitialized
            ? Singleton<UIActiveBonusBar>.Instance.container : null,
        1 => Singleton<UIUseAbilitiesBar>.IsInitialized
            ? Singleton<UIUseAbilitiesBar>.Instance.container : null,
        2 => Singleton<UIUseAugmentationsBar>.IsInitialized
            ? Singleton<UIUseAugmentationsBar>.Instance.container : null,
        3 => Singleton<UIUseItemsBar>.IsInitialized
            ? Singleton<UIUseItemsBar>.Instance.container : null,
        _ => null,
    };

    /// <summary>
    /// Gate 2: does the local bar belong to the character whose remote board this is? A summon maps
    /// to its <c>Summoner</c>, which is the same mapping <c>UseBarsSurface.AddOwner</c> uses, so the
    /// two sides answer the ownership question identically.
    /// </summary>
    internal static bool BarBelongsTo(int barIndex, CPlayerActor boardActor)
    {
        OwnerScratch.Clear();
        switch (barIndex)
        {
            case 0:
            {
                UIActiveBonusBar bar = Singleton<UIActiveBonusBar>.Instance;
                List<CActor>? actors = bar != null ? bar.actors : null;
                if (actors != null)
                {
                    for (int i = 0; i < actors.Count; i++)
                        OwnerScratch.Add(actors[i]);
                }
                break;
            }
            case 1:
            {
                UIUseAbilitiesBar bar = Singleton<UIUseAbilitiesBar>.Instance;
                if (bar != null && bar.actor != null)
                    OwnerScratch.Add(bar.actor);
                break;
            }
            case 2:
            {
                UIUseAugmentationsBar bar = Singleton<UIUseAugmentationsBar>.Instance;
                if (bar != null && bar.actor != null)
                    OwnerScratch.Add(bar.actor);
                break;
            }
            case 3:
            {
                UIUseItemsBar bar = Singleton<UIUseItemsBar>.Instance;
                if (bar != null && bar.actor != null)
                    OwnerScratch.Add(bar.actor);
                break;
            }
        }
        for (int i = 0; i < OwnerScratch.Count; i++)
        {
            CActor? a = OwnerScratch[i];
            CPlayerActor? owner = a switch
            {
                CPlayerActor player => player,
                CHeroSummonActor summon => summon.Summoner,
                _ => null,
            };
            if (owner != null && ReferenceEquals(owner, boardActor))
                return true;
        }
        return false;
    }

    /// <summary>Graphic scratch for <see cref="RenderHiddenPlainItem"/>, so the 4 Hz walk allocates
    /// nothing. Single-threaded, one child at a time.</summary>
    private static readonly List<Graphic> GraphicScratch = new(16);

    /// <summary>
    /// THE THIRD TERM OF THE SENDER'S WALK: is this a plain ITEM slot that the items-bar split
    /// render-hid, i.e. one that is still <c>activeSelf</c> and draws nothing?
    ///
    /// <para>MEASURED RATHER THAN LOOKED UP, and this doc says which, because the comment that
    /// stood in the walk claimed the sender's rule "verbatim" while missing this term entirely. The
    /// sender consults a LEDGER — <c>UseBarsSurface.IsPlainRenderHidden(child)</c>, the list of
    /// plain item slots its own split disabled (<c>UseBarsSurface.cs:2125-2127</c>) — and that
    /// ledger is an instance field of a surface object with no static reach from this class. What
    /// this observes instead is the ledger's WHOLE OBSERVABLE EFFECT:
    /// <c>UseBarsSurface.HidePlainSlotGraphics</c> disables every enabled <see cref="Graphic"/>
    /// under the slot and touches nothing else, so a render-hidden slot always answers true here.
    /// It is also the sender's stated INTENT read directly — "a tile the owner is not drawing must
    /// never appear on a peer's board".</para>
    ///
    /// <para>SCOPED TO <c>UIUseItemScenario</c> ON PURPOSE, and that is not a shortcut: the
    /// sender's ledger is typed <c>List&lt;KeyValuePair&lt;CItem, UIUseItemScenario&gt;&gt;</c>, so
    /// <c>IsPlainRenderHidden</c> can never return true for a bonus, ability or augment slot. A
    /// wider test here would be a rule the sender does not have, and would refuse bars 0/1/2 on
    /// this side for a reason no sender could produce. The bonus-bar split is not this term either
    /// — <c>EnforceActiveBonusSplit</c> uses <c>SetActive(false)</c>, which the activeSelf test
    /// above already covers on both sides.</para>
    ///
    /// <para>WHERE THE TWO CAN STILL DIVERGE, stated rather than papered over: this also answers
    /// true for an item slot the GAME left with every graphic off, which the sender's ledger would
    /// have counted. Both walks then disagree by one, gate 3 refuses, and the tiles go blank —
    /// fail-closed. <see cref="RefusalReason.SlotCountMismatch"/>'s text names this as one of the
    /// two populations it cannot tell apart rather than asserting the other one.</para>
    /// </summary>
    internal static bool RenderHiddenPlainItem(Transform child)
    {
        if (child.GetComponent<UIUseItemScenario>() == null)
            return false;
        GraphicScratch.Clear();
        child.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        bool draws = false;
        for (int i = 0; i < GraphicScratch.Count && !draws; i++)
        {
            Graphic g = GraphicScratch[i];
            draws = g != null && g.enabled;
        }
        GraphicScratch.Clear();
        return !draws;
    }

    /// <summary>True for a real slot widget (the four concrete <c>UIUseSlot&lt;T&gt;</c> types) —
    /// pooled decoration under the same container is never a mirrored tile, which is precisely what
    /// the sender's <c>_chosen(child) == null</c> test excludes.</summary>
    internal static bool IsSlot(Transform child) =>
        child.GetComponent<UIUseActiveBonus>() != null
        || child.GetComponent<UIUseAbility>() != null
        || child.GetComponent<UIUseAugmentation>() != null
        || child.GetComponent<UIUseItemScenario>() != null;

    /// <summary>
    /// The sprite a slot is showing, off the game's OWN serialized field — never a name lookup and
    /// never "the first Image in the subtree", which would pick up the button background, the
    /// selected mask or a highlight instead.
    ///
    /// <para><c>UIUseActiveBonus.icon</c> ← <c>bonus.GetIcon()</c>, <c>UIUseAbility.icon</c>,
    /// <c>UIUseItemScenario.imageItem</c> ← <c>UIInfoTools.GetItemConfig(art).miniIcon</c>.
    /// <c>UIUseAugmentation</c> carries no icon field at all, so an augment tile stays blank rather
    /// than borrowing a neighbour's picture — an honest gap beats a plausible lie.</para>
    /// </summary>
    private static Sprite? IconOf(Transform child)
    {
        var bonus = child.GetComponent<UIUseActiveBonus>();
        if (bonus != null)
            return SpriteOf(bonus.icon);
        var ability = child.GetComponent<UIUseAbility>();
        if (ability != null)
            return SpriteOf(ability.icon);
        var item = child.GetComponent<UIUseItemScenario>();
        if (item != null)
            return SpriteOf(item.imageItem);
        return null;
    }

    private static Sprite? SpriteOf(Image? image) =>
        image != null && image.sprite != null ? image.sprite : null;

    private static void ReportOnce(int barIndex, int count)
    {
        if (s_logged)
            return;
        s_logged = true;
        VRLog.Info("Net", $"DOCK MIRROR: use-bar SYMBOLS resolved locally for the first time (bar " +
                          $"{barIndex}, {count} slot(s)) — a peer's mirrored decision tiles now wear " +
                          "THE GAME'S OWN icon, taken off this client's own copy of that bar (the " +
                          "game raises these bars from replicated messages on every client), gated " +
                          "on the bar's owner BEING the character this board draws and on its " +
                          "visible slot count matching record 25's. Zero wire bytes, zero art " +
                          "identity on the wire.");
    }
}
