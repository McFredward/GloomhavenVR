using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE TWO DIRECTIONS OF WIRE RECORD 45 — the SENDER reading a slot's identity off the widget its
/// owner is actually looking at, and the RECEIVER resolving that id against its own replicated
/// model and asking the GAME for the sprite. The record's shape, its constants and its fold live in
/// <see cref="UseBarSlotIdentity"/>; read that class first, it carries the why.
///
/// ─── BOTH DIRECTIONS IN ONE FILE, ON PURPOSE ───────────────────────────────────────────────────
/// A wire field whose producer and consumer sit in different files drifts apart one careful edit at
/// a time — this project has paid for that more than once. Here the two call the same
/// <c>ActiveBonusId</c> / <c>ItemId</c> methods and the same <see cref="TakesPlainIcon"/>
/// predicate, so "the sender and the receiver agree about what an id means" is a property of the
/// code rather than a promise two files keep.
///
/// ─── WHY THESE IDS ARE STABLE ACROSS MACHINES ──────────────────────────────────────────────────
/// Each is folded from the game's OWN cross-machine identity for the thing, not from anything
/// local:
///
/// <list type="bullet">
///   <item><b>An active bonus</b> — <c>(Ability.Name, BaseCard.ID)</c>. That pair is not a guess:
///     it is literally what the GAME matches on when it resolves an active bonus that arrived over
///     its own network. <c>TakeDamagePanel.ProxyTakeDamage</c> does
///     <c>preventDamageActiveBonuses.FirstOrDefault(x =&gt; x.Ability.Name == data.AbilityName
///     &amp;&amp; x.BaseCard.ID == data.BaseCardID)</c> against an <c>ActiveBonusesToken</c> off the
///     wire. If that pair were not stable across machines the base game's own damage mitigation
///     would mis-resolve.</item>
///   <item><b>An item</b> — <c>CItem.NetworkID</c>. The game's own item network identity: the same
///     field <c>ProxyTakeDamage</c> matches an <c>ItemsToken</c> on, and one that is persisted
///     through save state rather than assigned per session.</item>
/// </list>
///
/// <para>NEVER <c>GetInstanceID()</c> and never a slot ordinal on the owner's bar alone: the first
/// is per-process, and the second is exactly the "somebody else's decision" failure this whole area
/// exists to prevent.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR IDENTITY — 16-bit id, sparse, default-off. The ART stays
/// DELIBERATELY-NOT on the wire. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class UseBarSlotSymbol
{
    /// <summary>Scratch for the receiver's unique-match walk, so a 4 Hz resolve allocates nothing.
    /// Single-threaded, one board and one slot at a time.</summary>
    private static readonly List<CActiveBonus> BonusScratch = new(16);

    // ---- the two identities, off the game's own replicated fields ---------------------------

    /// <summary>The id of an active bonus: the game's own <c>(Ability.Name, BaseCard.ID)</c> pair.
    /// <c>NoIdentity</c> when either half is missing, which refuses rather than folding a partial
    /// identity that could collide with a different bonus's partial identity.</summary>
    internal static ushort ActiveBonusId(CActiveBonus? bonus)
    {
        if (bonus == null)
            return UseBarSlotIdentity.NoIdentity;
        CAbility? ability = bonus.Ability;
        CBaseCard? card = bonus.BaseCard;
        if (ability == null || card == null || string.IsNullOrEmpty(ability.Name))
            return UseBarSlotIdentity.NoIdentity;
        uint h = UseBarSlotIdentity.MixString(UseBarSlotIdentity.FoldStart, ability.Name);
        return UseBarSlotIdentity.Fold(UseBarSlotIdentity.MixInt(h, card.ID));
    }

    /// <summary>The id of an inventory item: the game's own <c>CItem.NetworkID</c>. <c>1</c> is
    /// <c>CItem</c>'s own "not networked yet" initialiser, so it is refused rather than folded —
    /// every un-networked item on a machine would otherwise share one id and every one of them
    /// would resolve ambiguously anyway.</summary>
    internal static ushort ItemId(CItem? item)
    {
        if (item == null || item.NetworkID <= 1u)
            return UseBarSlotIdentity.NoIdentity;
        return UseBarSlotIdentity.Fold(
            UseBarSlotIdentity.MixUInt(UseBarSlotIdentity.FoldStart, item.NetworkID));
    }

    /// <summary>
    /// Does this bonus take the PLAIN <c>ActiveBonus.GetIcon()</c> path — i.e. can a receiver that
    /// holds only the model reproduce the very sprite the owner's widget is wearing?
    ///
    /// <para>Read out of the game, not assumed. <c>UIUseActiveBonus.SetActiveBonus</c> wraps the
    /// model bonus in one of four <c>IActiveBonus</c> implementations before decorating, and three
    /// of them share ONE inherited <c>GetIcon()</c>: <c>ActiveBonus</c> itself,
    /// <c>AdjustInitativeActiveBonus : ActiveBonus</c> and <c>ChooseAbilityActiveBonus :
    /// ActiveBonus</c>. Only <c>ForgoActiveBonus</c> derives from <c>BaseActiveBonus&lt;T&gt;</c>
    /// directly and OVERRIDES <c>GetIcon()</c> with a different lookup
    /// (<c>GetCharacterActiveAbilityIcon(actor.GetPrefabName(), …)</c> against the plain path's
    /// <c>Class.ID</c>), and its wrapper cannot be rebuilt off the model alone because it is
    /// constructed from the widget's own option UI.</para>
    ///
    /// <para>So this excludes exactly one model type. It is applied on BOTH sides — the sender
    /// withholds the id, and the receiver refuses one it is nevertheless handed — because a future
    /// or hostile sender must not be able to talk this receiver into a sprite it cannot verify.
    /// </para>
    /// </summary>
    internal static bool TakesPlainIcon(CActiveBonus bonus) =>
        bonus is not CForgoActionsForCompanionActiveBonus;

    // ---- SENDER: the id of the slot widget the owner is looking at --------------------------

    /// <summary>
    /// The id of the slot <paramref name="child"/> in bar <paramref name="barIndex"/>, read off the
    /// owner's OWN live bar. <c>NoIdentity</c> whenever it cannot be established, which is the
    /// normal answer for the two bars this record does not carry.
    ///
    /// <para>The model behind a widget is found by inverting the game's own
    /// <c>UIActiveBonusBar.activeBonusSlots</c> / <c>UIUseItemsBar.ItemSlots</c> map rather than by
    /// reading <c>UIUseSlot&lt;T&gt;.element</c>: for bar 0 that field holds the <c>IActiveBonus</c>
    /// WRAPPER, whose underlying <c>CActiveBonus</c> sits behind a generic protected field, while
    /// the map is keyed by the model object itself. The maps hold at most a handful of entries and
    /// this runs on the 4 Hz content cadence.</para>
    ///
    /// <para>BARS 1 AND 2 CARRY NOTHING, deliberately. The abilities bar is raised on every client
    /// by the replicated path <see cref="RemoteUseBarSymbols"/> already leans on, so its symbols
    /// resolve locally and need no bytes; the augmentation slot widget carries no icon field at all
    /// (<c>UIUseAugmentation</c> has no <c>Image</c>), so there is no sprite for an id to name. The
    /// record's addressing has room for both, the day either changes.</para>
    /// </summary>
    internal static ushort SlotId(int barIndex, Transform? child)
    {
        if (child == null)
            return UseBarSlotIdentity.NoIdentity;
        try
        {
            switch (barIndex)
            {
                case 0:
                {
                    if (!Singleton<UIActiveBonusBar>.IsInitialized)
                        return UseBarSlotIdentity.NoIdentity;
                    UIActiveBonusBar bar = Singleton<UIActiveBonusBar>.Instance;
                    Dictionary<CActiveBonus, UIUseActiveBonus>? slots =
                        bar != null ? bar.activeBonusSlots : null;
                    if (slots == null)
                        return UseBarSlotIdentity.NoIdentity;
                    foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> kv in slots)
                    {
                        if (kv.Value == null || !ReferenceEquals(kv.Value.transform, child))
                            continue;
                        return kv.Key != null && TakesPlainIcon(kv.Key)
                            ? ActiveBonusId(kv.Key)
                            : UseBarSlotIdentity.NoIdentity;
                    }
                    return UseBarSlotIdentity.NoIdentity;
                }
                case 3:
                {
                    if (!Singleton<UIUseItemsBar>.IsInitialized)
                        return UseBarSlotIdentity.NoIdentity;
                    UIUseItemsBar bar = Singleton<UIUseItemsBar>.Instance;
                    Dictionary<CItem, UIUseItemScenario>? slots = bar != null ? bar.ItemSlots : null;
                    if (slots == null)
                        return UseBarSlotIdentity.NoIdentity;
                    foreach (KeyValuePair<CItem, UIUseItemScenario> kv in slots)
                    {
                        if (kv.Value != null && ReferenceEquals(kv.Value.transform, child))
                            return ItemId(kv.Key);
                    }
                    return UseBarSlotIdentity.NoIdentity;
                }
                default:
                    return UseBarSlotIdentity.NoIdentity;
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"USE BAR IDENTITY: reading the id of a bar-{barIndex} slot threw " +
                              $"({e.Message}) — that slot rides anonymously, as it did before " +
                              "record 45 existed.");
            return UseBarSlotIdentity.NoIdentity;
        }
    }

    // ---- RECEIVER: the sprite that id names, in THIS client's own model ---------------------

    /// <summary>Why a wire id did or did not become a sprite. Distinct from
    /// <see cref="RemoteUseBarSymbols.RefusalReason"/>, which is about the LOCAL resolve; the caller
    /// prints both, because "the owner named nothing" and "the owner named something I could not
    /// find" are different findings and only one of them is a defect.</summary>
    internal enum ResolveOutcome
    {
        /// <summary>The id named exactly one model object and the game gave up its sprite.</summary>
        Resolved = 0,

        /// <summary>No id was carried for this slot — the ordinary answer for a bar this record
        /// does not carry, and for every peer that predates it.</summary>
        NoIdentity,

        /// <summary>This receiver has no rule for that bar index (bars 1 and 2 today).</summary>
        BarNotCarried,

        /// <summary>The id matched NOTHING in this client's own model for that character: a model a
        /// beat behind the packet, or a bonus that belongs to the owner's summon rather than to the
        /// character this board draws. Refuses.</summary>
        NoModelMatch,

        /// <summary>The id matched MORE THAN ONE candidate — a 16-bit fold collision inside one
        /// character's own set. Refuses, because there is no way to tell which one the owner meant.
        /// </summary>
        Ambiguous,

        /// <summary>The id resolved to a bonus whose icon this receiver cannot reproduce off the
        /// model (see <see cref="TakesPlainIcon"/>). Refuses rather than showing a near-miss.
        /// </summary>
        NotReproducible,

        /// <summary>The lookup threw; the warning beside it carries the message.</summary>
        Threw,
    }

    /// <summary>Human-readable form of <see cref="ResolveOutcome"/> for the caller's log line.
    /// </summary>
    internal static string Describe(ResolveOutcome why, int barIndex, ushort id) => why switch
    {
        ResolveOutcome.Resolved => "resolved",
        ResolveOutcome.NoIdentity =>
            $"record 45 carried no id for that bar-{barIndex} slot",
        ResolveOutcome.BarNotCarried =>
            $"bar {barIndex} carries no identity on this wire (only the active-bonus and item bars "
            + "do — the abilities bar resolves locally and an augment slot has no icon at all)",
        ResolveOutcome.NoModelMatch =>
            $"id 0x{id:X4} matched nothing in this client's own model for that character — a model "
            + "a beat behind the packet, or a bonus/item belonging to the owner's summon rather "
            + "than to the character this board draws. The tile stays anonymous",
        ResolveOutcome.Ambiguous =>
            $"id 0x{id:X4} matched MORE THAN ONE candidate in this client's own model — a 16-bit "
            + "fold collision inside one character's set. Refused: there is no way to tell which "
            + "one the owner meant, and a symbol from the wrong decision is worse than none",
        ResolveOutcome.NotReproducible =>
            $"id 0x{id:X4} names a bonus whose icon this client cannot reproduce off the model "
            + "alone (ForgoActiveBonus overrides GetIcon and its wrapper is built from the owner's "
            + "own option UI). Refused rather than shown a near-miss",
        ResolveOutcome.Threw =>
            $"the bar-{barIndex} model lookup threw (see the warning beside this line)",
        _ => "unknown",
    };

    /// <summary>
    /// The sprite the owner's slot is wearing, found by resolving <paramref name="id"/> against
    /// THIS client's own replicated model for <paramref name="owner"/> and then asking the GAME for
    /// the art. Null on every refusal, and <paramref name="why"/> says which one.
    ///
    /// <para>THE UNIQUE-MATCH RULE IS THE SAFETY PROPERTY. Zero matches and two matches both return
    /// null. Nothing here picks a "best" candidate and nothing falls back to a neighbour — the
    /// caller keeps the anonymous plate every build before this one drew.</para>
    /// </summary>
    internal static Sprite? ResolveIcon(int barIndex, CPlayerActor? owner, ushort id,
                                        out ResolveOutcome why)
    {
        if (id == UseBarSlotIdentity.NoIdentity || owner == null)
        {
            why = ResolveOutcome.NoIdentity;
            return null;
        }
        try
        {
            switch (barIndex)
            {
                case 0:
                    return ResolveBonusIcon(owner, id, out why);
                case 3:
                    return ResolveItemIcon(owner, id, out why);
                default:
                    why = ResolveOutcome.BarNotCarried;
                    return null;
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"USE BAR IDENTITY: resolving id 0x{id:X4} for bar {barIndex} threw " +
                              $"({e.Message}) — the mirrored tile stays anonymous this cadence.");
            why = ResolveOutcome.Threw;
            return null;
        }
    }

    private static Sprite? ResolveBonusIcon(CPlayerActor owner, ushort id, out ResolveOutcome why)
    {
        // THE CANDIDATE SET IS MODEL-ONLY, which is the entire point: the local BAR is empty on a
        // watcher (TakeDamagePanel.ShowOtherPlayer never raises it), while the actor's active
        // bonuses are ordinary replicated simulation state every client already holds.
        List<CActiveBonus>? all = CharacterClassManager.FindAllActiveBonuses(owner);
        BonusScratch.Clear();
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                CActiveBonus b = all[i];
                if (b != null && ActiveBonusId(b) == id)
                    BonusScratch.Add(b);
            }
        }
        if (BonusScratch.Count == 0)
        {
            why = ResolveOutcome.NoModelMatch;
            return null;
        }
        if (BonusScratch.Count > 1)
        {
            why = ResolveOutcome.Ambiguous;
            return null;
        }
        CActiveBonus match = BonusScratch[0];
        if (!TakesPlainIcon(match))
        {
            why = ResolveOutcome.NotReproducible;
            return null;
        }
        // THE GAME'S OWN CALL, on the game's own wrapper — not a re-implementation of its art
        // lookup. `ActiveBonus` is a pure read: its constructor assigns two fields and GetIcon only
        // queries UIInfoTools and the model, so nothing here writes game state.
        why = ResolveOutcome.Resolved;
        return new ActiveBonus(match, owner).GetIcon();
    }

    private static Sprite? ResolveItemIcon(CPlayerActor owner, ushort id, out ResolveOutcome why)
    {
        CInventory? inventory = owner.Inventory;
        List<CItem>? items = inventory != null ? inventory.AllItems : null;
        CItem? found = null;
        int hits = 0;
        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                CItem it = items[i];
                if (it == null || ItemId(it) != id)
                    continue;
                found = it;
                hits++;
                if (hits > 1)
                    break;
            }
        }
        if (hits == 0)
        {
            why = ResolveOutcome.NoModelMatch;
            return null;
        }
        if (hits > 1)
        {
            why = ResolveOutcome.Ambiguous;
            return null;
        }
        // The same lookup UIUseItemScenario.Decorate performs — GetItemConfig(art).miniIcon — so the
        // peer wears the sprite the owner's own slot was decorated with, not a lookalike.
        ItemConfigUI? config = UIInfoTools.Instance != null
            ? UIInfoTools.Instance.GetItemConfig(found!.YMLData.Art) : null;
        why = ResolveOutcome.Resolved;
        return config != null ? config.miniIcon : null;
    }
}
