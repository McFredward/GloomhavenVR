using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHERE A FULLY-DETAILED ITEM-CARD FACE COMES FROM, for an item equipped by SOMEBODY ELSE.
///
/// The item twin of <see cref="RemoteAbilityCardSource"/>, and it exists for the same reason: the
/// user's ruling of 2026-08-08 ("Die Oberseiten der Karten des remote Spielers soll auch überall
/// sichtbar sein … NUR in der Auswahlphase sieht man überall nur die Rückseiten") makes a peer's
/// equipped items a FRONT surface in every phase but the secret one, and a peer's item fan used to be
/// card BACKS unconditionally.
///
/// ─── WHY THERE IS ONLY ONE PATH HERE, AND IT IS THE BORROW ──────────────────────────────────────
/// An ability card has a LIVE widget per card on every client (the game builds a populated
/// <c>CardsHandUI</c> per player actor — see <see cref="RemoteAbilityCardSource"/>), so its preferred
/// path is "clone the peer's own widget". An ITEM card has no such thing: nothing in the game keeps a
/// standing <c>ItemCardUI</c> per equipped item. Every consumer manufactures one on demand from the
/// pool — the flat inventory screens do it, and so does the mod's own <c>Cards.ItemsPile</c>
/// (<c>ObjectPool.SpawnCard(item.ID, ECardType.Item, …)</c> → set <c>item</c> → <c>Show(false)</c>).
/// So the pooled BORROW is not a fallback here, it is the mechanism.
///
/// ─── BORROW-AND-CLONE, NOT HOST ────────────────────────────────────────────────────────────────
/// <c>Cards.ItemsPile</c> HOSTS its pooled <c>ItemCardUI</c> for the life of the chip, because the
/// LOCAL item card has to stay live: its state changes under it (spent / consumed) and the game's own
/// <c>ItemCardEffects</c> must play ON that widget. A peer's mirrored item needs none of that — it is
/// a picture of a card. Holding a game-owned pooled widget for the life of a remote fan would buy
/// nothing and cost ItemsPile's whole hazard list (a mutated widget that must be restored before
/// recycle, a recycle that must survive every teardown path, and a leak that corrupts the flat UI if
/// one of those paths is missed) — multiplied by up to a dozen items times three peers. So the widget
/// is borrowed INACTIVE, cloned, and handed straight back inside this one method, exactly as
/// <see cref="RemoteAbilityCardSource.TryPooledClone"/> does: there is no window in which anything can
/// go wrong.
///
/// ─── AND WHY THE BORROW IS NOT ON THE PER-FRAME PATH ───────────────────────────────────────────
/// <see cref="RemoteCardArt"/> dedups a shown front on a key. For ability cards that key is the live
/// widget's instance id; a borrowed widget has no stable id at all, so this class keys on the ITEM
/// INSTANCE and asks <see cref="RemoteCardArt.ShowsKey"/> BEFORE it spawns anything. A settled fan
/// therefore performs zero pool spawns, zero clones and zero allocations per frame — the borrow runs
/// only when the item in a given slot actually changes.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. The item identities come from the
/// host-replicated <c>CPlayerActor.Inventory.AllItems</c> that every client already holds, never from
/// a packet; the face is manufactured locally from the game's own pool. Item IDENTITY stays
/// DELIBERATELY-NOT on the wire. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteItemCardSource
{
    /// <summary>One-shot latch for the "the item borrow works on this build" line — once per session,
    /// never once per card.</summary>
    private static bool s_logged;

    /// <summary>
    /// Stable dedup key for <paramref name="item"/>, in <see cref="RemoteCardArt"/>'s key space.
    ///
    /// <para>REFERENCE identity, not <c>item.ID</c>: a character can equip two copies of the same
    /// item, and the two must not dedup into one face when the fan re-orders. <c>CItem</c> is a plain
    /// model object whose <c>GetHashCode</c> the game is free to override, so the key is taken through
    /// <c>RuntimeHelpers.GetHashCode</c> — the identity hash, which no override can move.</para>
    ///
    /// <para>…TIMES THE USED-STATE, because this key is also the REBUILD TRIGGER. Spending an item does
    /// not change which item it is, so on identity alone the face already up would keep the FRESH look
    /// for as long as the peer kept the item equipped — the decoration would be right only for cards
    /// that happened to arrive already spent. Folding the state in makes a state flip a key change,
    /// which is exactly the event <see cref="RemoteCardArt.ShowsKey"/> already exists to detect; the
    /// rebuild then lands on <see cref="RemotePileFronts"/>'s normal content cadence. A FRESH card keys
    /// bit-for-bit as it always did, so nothing that was settled before this existed rebuilds now.</para>
    /// </summary>
    internal static int KeyFor(CItem item)
    {
        int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);
        int look = (int)LookOf(item);
        if (look == 0)
            return identity;
        unchecked
        {
            // A fixed odd multiplier, so Spent and Consumed land far apart in the key space and a
            // one-bit state cannot alias two different items onto one face.
            return identity ^ (look * (int)0x9E3779B1u);
        }
    }

    /// <summary>
    /// WHICH "already used" decoration the OWNER's board is drawing over <paramref name="item"/> right
    /// now — the mirror's half of <c>ItemCardUI.UpdateState</c> (ItemCardUI.cs:376-395), which maps the
    /// same two slot states onto the same two <c>ItemCardEffects</c> timelines and treats every other
    /// state as a fresh card.
    ///
    /// <para>ZERO WIRE, and read off the SAME object that manufactures the face. The item instance this
    /// method is handed came out of <c>RemotePileFronts.Resolve</c>'s walk of the host-replicated
    /// <c>CPlayerActor.Inventory.AllItems</c> — the identical list, in the identical order, that
    /// <c>RemotePileFronts.TryResolveItemSpentFlags</c> walks for the fan's tapped ROTATION. So the
    /// ghost and the tap are two readings of one model and cannot disagree about which chip is spent,
    /// and no new wire field is owed for either. Never gated by <see cref="RevealGate"/> on its own: it
    /// only ever runs on a face the caller has ALREADY decided may be shown.</para>
    ///
    /// <para>Guarded, because it is called from <see cref="KeyFor"/>, which sits OUTSIDE the try in
    /// <see cref="ShowFace"/> — a throwing model read must cost the decoration, never the face.</para>
    /// </summary>
    private static RemoteCardArt.SpentLook LookOf(CItem? item)
    {
        try
        {
            if (item == null)
                return RemoteCardArt.SpentLook.None;
            return item.SlotState switch
            {
                CItem.EItemSlotState.Spent => RemoteCardArt.SpentLook.Spent,
                CItem.EItemSlotState.Consumed => RemoteCardArt.SpentLook.Consumed,
                _ => RemoteCardArt.SpentLook.None,
            };
        }
        catch (System.Exception)
        {
            return RemoteCardArt.SpentLook.None;
        }
    }

    /// <summary>
    /// Show <paramref name="item"/>'s REAL card face on <paramref name="art"/>. Returns true iff a
    /// face is up afterwards; on false the caller keeps showing the card BACK.
    ///
    /// CALLER CONTRACT (anti-cheat): only ever call this when <see cref="RevealGate"/> is open for the
    /// item's owner. Nothing here re-checks it — deliberately, and for the same reason
    /// <see cref="RemoteAbilityCardSource.ShowFullFace"/> does not: one gate, in one place, evaluated
    /// by the caller BEFORE any face object exists, is auditable in a way a gate re-derived in four
    /// files is not.
    /// </summary>
    internal static bool ShowFace(RemoteCardArt art, CItem? item)
    {
        if (art == null || item == null)
            return false;

        int key = KeyFor(item);
        if (art.ShowsKey(key))
        {
            art.MaintainMipBake(); // the async background art arrives after activation, like the ability clone
            return true;
        }

        try
        {
            return TryPooledClone(art, item, key);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote ITEM face: pooled borrow failed ({e.Message}) — the chip keeps " +
                              "its card BACK.");
            art.HideFront();
            return false;
        }
    }

    /// <summary>
    /// Borrow an <c>ItemCardUI</c> from the game's pool, clone its face, and give the widget straight
    /// back in a <c>finally</c>.
    ///
    /// The recipe is the flat game's own (and <c>Cards.ItemsPile.TryHostRealCard</c>'s): spawn by item
    /// id, assign <c>item</c>, and let the widget paint itself. Two deliberate differences from the
    /// local pile:
    ///   * the widget is spawned <c>activate: false</c> under an INACTIVE mod-owned holder, so it can
    ///     never render for a frame — the same "no face can leak ahead of the gate" construction the
    ///     ability borrow uses;
    ///   * <c>Show()</c>/<c>UpdateState()</c> are NOT called on the borrowed widget. Both exist to play
    ///     the state FX through <c>ItemCardEffects</c>, and on THIS widget they cannot: it has never
    ///     been active, so that component's <c>Initialize</c> never minted its per-image materials and
    ///     its writes would land on the game's SHARED authored material — a pool widget we are about to
    ///     hand back. <c>Show()</c> would also activate a widget we require to stay dark. The "already
    ///     used" look those calls exist for is NOT lost: it is rebuilt on the CLONE, from the game's own
    ///     settled end-state, by <c>RemoteCardArt.ApplySpentLook</c> — read that method's doc before
    ///     reconsidering this line, it records why every route through the borrowed widget is dead. The
    ///     clone runs its OWN <c>OnEnable → LoadBackground</c> once it activates, which is where the
    ///     background art actually arrives.
    /// The <c>item</c> field is re-planted ON THE CLONE while it is still inactive
    /// (<c>beforeActivate</c>): it is a plain managed reference that <c>Object.Instantiate</c> does not
    /// carry across, and <c>ItemCardUI.OnEnable → LoadBackground</c> dereferences <c>item.ID</c> the
    /// instant the clone comes up (ItemCardUI.cs:446-448).
    /// </summary>
    private static bool TryPooledClone(RemoteCardArt art, CItem item, int key)
    {
        if (ObjectPool.instance == null || item.ID == 0)
            return false;

        // The borrowed widget is transient, so any key currently latched on this overlay carries no
        // meaning for it — drop the front first, exactly as the ability borrow does.
        art.HideFront();

        GameObject? holder = null;
        GameObject? cardGo = null;
        ItemCardUI? ui = null;
        try
        {
            holder = new GameObject("GloomhavenVR.ItemCardBorrow") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(ObjectPool.instance.transform, worldPositionStays: false);
            holder.SetActive(false);

            cardGo = ObjectPool.SpawnCard(item.ID, ObjectPool.ECardType.Item, holder.transform,
                resetLocalScale: true, resetToMiddle: true, resetLocalRotation: false, activate: false);
            if (cardGo == null)
                return false;

            ui = cardGo.GetComponent<ItemCardUI>();
            if (ui == null)
                return false;

            // The SOURCE gets the model reference too: the widget must be a complete card at the
            // moment it is copied (title/symbol/condition are painted from it), and RecycleCard →
            // OnReturnedToPool resets the widget for the pool either way.
            ui.item = item;

            // Re-plant the same reference ON THE CLONE while it is still inactive — Instantiate does
            // not carry a plain managed field across, and the clone's OnEnable dereferences item.ID.
            // The closure is the one allocation of this path and it happens ONLY on a real rebuild
            // (the ShowsKey dedup in ShowFace is what keeps a settled fan off it entirely).
            CItem captured = item;
            bool shown = art.ShowFront(cardGo, key, skinSource: null, beforeActivate: clone =>
            {
                var cloneUi = clone.GetComponent<ItemCardUI>();
                if (cloneUi != null)
                    cloneUi.item = captured;
            }, spentLook: LookOf(item));
            if (shown)
                ReportOnce(item);
            return shown;
        }
        finally
        {
            if (cardGo != null && ui != null)
            {
                try { ObjectPool.RecycleCard(ui.CardID, ObjectPool.ECardType.Item, cardGo); }
                catch (System.Exception e) { VRLog.Warn("Net", $"Item-card borrow recycle failed: {e.Message}"); }
            }
            else if (cardGo != null)
            {
                Object.Destroy(cardGo);
            }
            if (holder != null)
                Object.Destroy(holder);
        }
    }

    /// <summary>One Info line the first time an item face is manufactured in a session.</summary>
    private static void ReportOnce(CItem item)
    {
        if (s_logged)
            return;
        s_logged = true;
        VRLog.Info("Net", $"Remote ITEM card FACE path = PooledBorrow (first use; e.g. item id {item.ID}) — " +
                          "a peer's equipped item is now the REAL game item card (art, title, symbol, " +
                          "condition), cloned locally from a widget borrowed from and returned to the " +
                          "game's own pool. Zero wire traffic, zero item identity on the wire.");
    }
}
