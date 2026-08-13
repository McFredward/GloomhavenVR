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
///   3. its visible slot count equals the count record 25 carried for that bar, walked by the very
///      same rule the sender walked (container children, hierarchy order, inactive skipped,
///      non-slot children skipped — <c>UseBarsSurface.BarDock.SampleWireSlots</c>).
/// Any failure returns 0 and the caller keeps the anonymous tile it drew before. The peer therefore
/// shows the real symbol or an honest blank — never a symbol from a different decision.
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
    /// Fill <paramref name="into"/> with the sprite each visible slot of bar
    /// <paramref name="barIndex"/> is wearing on THIS client, for the bar belonging to
    /// <paramref name="boardActor"/>. Returns the number of slots written (0 = refuse, see the
    /// three-way gate in the class doc); entries past the return value are left untouched and
    /// entries inside it may still be null for a slot type that carries no icon.
    /// </summary>
    internal static int Resolve(int barIndex, CPlayerActor? boardActor, int wireCount, Sprite?[] into)
    {
        if (boardActor == null || wireCount <= 0 || into == null || into.Length == 0)
            return 0;
        try
        {
            RectTransform? container = ContainerOf(barIndex);
            if (container == null)
                return 0;
            if (!BarBelongsTo(barIndex, boardActor))
                return 0;

            int count = 0;
            for (int i = 0; i < container.childCount && count < into.Length; i++)
            {
                Transform child = container.GetChild(i);
                // THE SENDER'S OWN WALK, verbatim: active children only, non-slot children skipped.
                // Index alignment with record 25's state bytes is structural because the two walks
                // are the same walk — there is no second rule that could disagree about "slot 2".
                if (!child.gameObject.activeSelf || !IsSlot(child))
                    continue;
                into[count++] = IconOf(child);
            }
            if (count != wireCount)
                return 0; // the local bar is not showing the owner's row — refuse, keep the tile
            ReportOnce(barIndex, count);
            return count;
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote use-bar symbols: resolve failed for bar {barIndex} " +
                              $"({e.Message}) — the mirrored tiles stay anonymous this cadence.");
            return 0;
        }
    }

    /// <summary>The bar's slot container on THIS client, by the record-25 bit order (0 = active
    /// bonus, 1 = abilities, 2 = augments, 3 = items — <c>NetProtocol.UseBarActiveBonusBit</c> …).</summary>
    private static RectTransform? ContainerOf(int barIndex) => barIndex switch
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
    private static bool BarBelongsTo(int barIndex, CPlayerActor boardActor)
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

    /// <summary>True for a real slot widget (the four concrete <c>UIUseSlot&lt;T&gt;</c> types) —
    /// pooled decoration under the same container is never a mirrored tile, which is precisely what
    /// the sender's <c>_chosen(child) == null</c> test excludes.</summary>
    private static bool IsSlot(Transform child) =>
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
