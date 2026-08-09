using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// The THIRD control-board pile (item 4, "Gegenstände"): the acting character's
/// equipped ITEM cards, mounted below the burnt pile and browsed like the discard /
/// burnt stacks (poke — or the board laser — toggles a fixed reading wall above the
/// board; there is NO hand-held variant, see <see cref="TogglePoke"/>). Content is
/// <see cref="CItem"/> read live from
/// <c>PlayerActor.Inventory.AllItems</c> (CInventory.cs:35); the inventory is read,
/// never written (using an item goes through the game's own <c>UseItemService</c>).
///
/// ITEMS REWORK (hardware round 2 — the prior bespoke chips FAILED on hardware):
/// <list type="bullet">
/// <item>REAL card face (was: grey slab; <c>miniIcon</c> is null for items). Each chip
/// hosts a LIVE <c>ItemCardUI</c> obtained from the game's own <c>ObjectPool</c>
/// (<c>SpawnCard(item.ID, ECardType.Item, …)</c>) reparented onto a world-space canvas —
/// the exact card the flat game shows, with its addressable background art
/// (<c>ItemConfigUI.BackgroundImage</c>) loading async onto <c>cardBackground</c>. A
/// legacy colored slab + name is kept only as a fallback when the pool is unavailable.</item>
/// <item>NORMAL-CARD interaction (was: pinch-only). Chips now support the SAME set as
/// ability cards: fingertip hand-sweep POP (readability, single-winner like
/// <see cref="PileBrowser"/>), dominant-hand LASER hover+pluck (a dedicated geometric
/// fan pick, <see cref="TryLaserRaycast"/>, driven by <c>CardsDriver.UpdateItemFanLaser</c>
/// exactly like the pile-browse fan — pops on hover / plucks on trigger), pinch-GRAB,
/// and read-in-hand (snap upright + enlarged).</item>
/// <item>USE = a CLIP-IN slot (was: a floating drop pad computed once). The slot is board
/// furniture built by <see cref="PlayTray"/> UNDER the board next to Confirm/Undo
/// (per-board <c>ItemUseSlotOffset</c>, debug-menu tunable). Its visibility is
/// re-evaluated LIVE every tick: shown ONLY while it is the local character's own turn
/// (<see cref="CardsGameApi.IsActionTurn"/>) AND a held item card's live
/// <c>SlotState</c> is usable. Dropping that card into the slot activates through the
/// game's own items-bar slot click when a live slot exists (the exact 2D/proxy seam),
/// falling back to <c>new UseItemService(hand.PlayerActor).UseItem(cItem)</c> (which owns
/// ALL multiplayer sync + re-validates the item).</item>
/// <item>EVERY item is PLAYED PHYSICALLY (user ruling 2026-08-08: "der Trank soll genauso
/// wie alle anderen Karten wirklich physisch hingelegt werden"). The earlier split kept
/// element-CHOICE items (the mana potions) on their docked <c>UIUseItemsBar</c> symbol —
/// a button, the one item flow that was not a card placement. Now the card is the ONLY
/// entry for those too: placing it clips it in exactly like a plain item and then drives
/// the game's own slot click, which raises the element picker. Because that picker is a
/// serialized child of the slot prefab, it surfaces in the DECISION AREA under the control
/// board (the items bar docks below the decision row, <c>UseBarsSurface</c>) — the same
/// surface every other game decision docks into. No item symbol is ever clickable on its
/// own any more: <see cref="EnforceChoiceSlotSplit"/> keeps every choice slot deactivated
/// until its card is in the slot, and <c>UseBarsSurface.EnforceItemsSplit</c> keeps the
/// plain ones hidden, so the bar docks ONLY to carry the placed card's element choice.
/// The split also covers the TAKE-DAMAGE decision: placing an OnAttacked shield/retaliate
/// card toggles it through the panel's own slot seam (<see cref="TickTakeDamagePick"/>),
/// the panel's confirm commits.</item>
/// <item>…AND SO IS THE WORN ITEM THAT ASKS PER EVENT (user ruling 2026-08-09, the "Brille").
/// A <c>Trigger: PassiveEffect</c> + <c>Usage: Unrestricted</c> item is never activated through
/// <c>UseItemService</c> at all — the game builds it a <c>CActiveBonus</c> off its ITEM CARD and
/// charges it when that bonus is used — so its only 2D affordance was a row on
/// <c>UIActiveBonusBar</c>. That row is gone now (<c>UseBarsSurface.EnforceActiveBonusSplit</c>):
/// the item HIGHLIGHTS in the pile while its bonus is offered, placing its card raises the USE
/// cap, poking USE presses the game's own bonus row (the exact click the button made, MP-synced by
/// the game itself), the card then LIES in the recess as that lit row, and taking it back out is
/// the un-click — unless the game has locked the toggle, which the card refuses to leave. See
/// <see cref="_pendingBonus"/> and <c>CardsGameApi</c>'s "ITEM-BACKED ACTIVE BONUSES" block. What
/// stays in the decision area is only what carries a FURTHER OPTION (initiative ±, forgo,
/// choose-ability, element consume) and every bonus with no card to place at all.</item>
/// </list>
/// State → look: CONSUMED → ashen + the hosted card's OWN consumed FX (the game's separate
/// CardSmoke plume is deliberately NOT spawned here — see the note in ItemChip.Create);
/// SPENT → rolled 90° ("tapped") in the fan + the card's spent FX; otherwise upright. A chip taken INTO the hand snaps upright + enlarged so it always
/// reads. Layout mirrors <see cref="PileBrowser"/>; open/close is driven by
/// <see cref="PileViewer"/>.
///
/// THE FAN ONLY OPENS BY USER ACTION (user report 2026-08-07: "der Gegenstands-Fächer ging beim
/// Schaden von selbst auf und liess sich nie wieder schliessen"). <see cref="Open"/> has exactly
/// ONE caller — <see cref="TogglePoke"/>, i.e. a deliberate poke/laser click on the items stack.
/// The two decision-flow pumps (<see cref="TickDemandPick"/>, <see cref="TickTakeDamagePick"/>)
/// used to raise the fan themselves AND re-raise it every 0.5 s while their flow was live, which
/// is precisely why no close could stick. They now only SERVE an already-open fan and log a
/// throttled cue naming the items stack. Any future "this flow needs the fan up" requirement must
/// raise a CUE on the stack, never call Open.
/// </summary>
internal sealed class ItemsPile
{
    // Arc geometry — same family as PileBrowser (reading, not picking).
    private const float RadiusFactor = 1.7f;
    private const float MaxArcDegrees = 110f;
    private const float MaxStepDegrees = 10f;
    private const float ChipScale = 1.25f;
    private const float ZStagger = 0.004f;

    // Shared board-top anchor (requirement 2): the poke-toggle item fan opens at the SAME
    // spot above the control board as the discard/burnt PileBrowser (mirror of
    // PileBrowser.BoardFloatHeight / BoardFloatProudZ / BoardAnchorBase). The root parents
    // under the board root (PlayTray.Current.Root) so it inherits the board's live pose + scale.
    private const float BoardFloatHeight = 0.26f;
    private const float BoardFloatProudZ = -0.05f;
    private static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    /// <summary>
    /// Requirement #2: the per-board ITEM-fan offset — nudges the item fan root (board-anchored,
    /// held-follow, and above-board reading poses) INDEPENDENTLY of the ability-card fan, since item
    /// cards are a different, near-square shape. Read LIVE every <see cref="Tick"/> (a cheap Vector3
    /// config read) so the debug-menu 'Item card X/Y/Z' steppers move an OPEN fan immediately, exactly
    /// like <see cref="CardsConfig.BrowseFanOffset"/> does for the discard/burnt fans.
    /// </summary>
    private static Vector3 ItemFanOffset => CardsConfig.ItemCardOffset(CardsConfig.CurrentBoard).Value;

    // The hand-sweep reach constants that used to be mirrored here (scale-1 metres) are gone: the
    // item fan now runs the SHARED election (FanSweep.Score) with the reach resolved from each
    // chip's own live world size (FanSweep.ResolveReach). That is the fix for the 2026-08-02
    // report where one player could barely touch a chip and the other could not reproduce it —
    // the chips hang under the CONTROL BOARD and carry its scale, and the two players' boards
    // were 0,32× and 0,80×. Full root cause on FanSweep.ResolveReach.

    /// <summary>Drop-into-use capture radius (world metres at board scale 1; scaled by the slot's live scale).</summary>
    private const float UseSlotRadius = 0.13f;

    /// <summary>CLEAR-AREA factor of the item-use berth: the box a placed card is fitted into,
    /// 1.04× the card metric.
    ///
    /// <para>THE DOC USED TO CALL THIS AN "inner-plate factor" — <c>PlayTray.BuildItemUseSlot</c>'s
    /// FrameInner quad, 1.04× the card box inside a 1.12× gold frame — and that plate no longer
    /// exists: the 2026-08-09 re-art deleted it (a dark plate on a widget that hangs below the board
    /// with the player's room behind it is a hole through to passthrough near the black key preset;
    /// the whole argument is at <c>PlayTray.BuildItemUseSlot</c>). What the number always really
    /// meant survives unchanged: the clear area the card has to sit inside. The berth's two-tone
    /// outline is now drawn just OUTSIDE it (1.08×) and its warm field just inside (1.03×). Mirrored
    /// on the peer's board by <c>Net.RemoteBoardFurniture.UseSlotInnerFactor</c> and linted against
    /// it (scripts/check-mirrors.sh).</para></summary>
    private const float UseSlotInnerFactor = 1.04f;

    /// <summary>How much of that clear area a placed card fills, so the berth's outline stays
    /// visible all the way round instead of being covered by the card's own edge.</summary>
    private const float UseSlotFillFraction = 0.94f;

    /// <summary>
    /// The SLOT-LOCAL scale a placed card comes to rest at (requirement 5a): its own face fitted
    /// into the recess's inner plate, keeping its aspect, with the rim left showing.
    ///
    /// <para>THE SECOND HALF OF "die Karte liegt nicht perfekt auf dem Bereich". The card used to
    /// land at the FAN's chip scale (×1.25), and an item card's face is NEAR-SQUARE
    /// (CardWidth × CardWidth — see ItemChip.TryHostRealCard's fit) while the recess is authored at
    /// the tall ABILITY card box. At the shipped sizes that is a 79 mm-wide card dropped onto a
    /// 71 mm-wide gold frame: even dead-centre it hung over BOTH sides, which is the overhang in
    /// .planning/debug/item_area.png. Deriving the scale from the recess's own geometry instead of
    /// from the fan's makes the fit hold for any card size, any board and any future item aspect —
    /// and because both quantities are in the same board-local metres, this IS the chip's local
    /// scale under the slot; no fan/slot scale-chain conversion is involved any more.</para>
    ///
    /// <para>The drop GHOST is sized from this very number (see <see cref="TickUseGhost"/>), so what
    /// is previewed and what lands are the same rectangle by construction.</para>
    /// </summary>
    internal static float UseSlotFitScale(ItemChip chip)
    {
        float boxW = CardsConfig.CardWidth.Value * UseSlotInnerFactor;
        float boxH = CardsConfig.CardHeight * UseSlotInnerFactor;
        float w = Mathf.Max(chip.FaceWidth, 1e-4f);
        float h = Mathf.Max(chip.FaceHeight, 1e-4f);
        return Mathf.Min(boxW / w, boxH / h) * UseSlotFillFraction;
    }

    private readonly List<ItemChip> _chips = new(12);

    /// <summary>Scratch candidate set for <see cref="TryLaserRaycast"/> — the arc while it is up, the
    /// single card still lying in the recess while it is down. A REUSED list (cleared and refilled,
    /// never re-allocated) because that pick runs per frame per hand and the whole path is written to
    /// be allocation-free.</summary>
    private readonly List<ItemChip> _laserScan = new(12);
    private Transform? _root;
    private TextMeshPro? _title;
    // The ITEMS stack transform (PileViewer.EnsureBuilt passes _items.transform; the shared pile
    // MOUNT is only the null fallback). Used solely as the emerge/collapse converge point — it is
    // not a parent and not a placement scale reference.
    private Transform? _anchor;

    /// <summary>
    /// THE CHARACTER WHOSE ITEMS THESE ARE — the hand the fan was OPENED for, and since the
    /// 2026-08-09 focus regression that is the hand the BOARD PRESENTS
    /// (<c>Board.CharacterFocus.PresentedHand</c>), not the hand the GAME presents.
    ///
    /// <para>USER REPORT (verbatim): "Wenn gerade ein Character am Zug ist, und man wechselt zu einem
    /// anderen Character - dann ändert sich der Item Pile nicht, es bleibt der Item Pile des
    /// Characters der gerade am Zug ist. … Der Item Pile soll von dem Fächer und der Nummer immer
    /// dasjenige anzeigen dessen Character gerade ausgewählt ist - nicht desjenigen das am Zug ist."
    /// This is the ModBuild-89 defect ("Wird der Character gewechselt sollen immer sofort die
    /// jeweiligen richtigen Zahlen des Characters angezeigt werden") surviving in the one stack that
    /// fix did not cover: <c>PileViewer.TickStatus</c> made the discard/burnt NUMBERS follow the
    /// focus and kept feeding the whole ITEM path the game's hand.</para>
    ///
    /// <para>─── WHY THIS FIELD MOVED INSTEAD OF A SECOND "DISPLAY HAND" BEING ADDED BESIDE IT.
    /// The obvious-looking alternative — leave <c>_hand</c> naming the ACTING character and re-resolve
    /// only the display reads (<see cref="Count"/>, <see cref="Populate"/>, <see cref="Signature"/>,
    /// <see cref="UsableCount"/>) to the focused one — was rejected, and it is the more dangerous of
    /// the two structures. It would leave the arc holding character B's CItem instances while
    /// <c>_hand</c> named character A, and every action path here reads BOTH: <see cref="ConfirmPendingUse"/>
    /// builds <c>new UseItemService(_hand.PlayerActor)</c> and hands it <c>chip.Item</c>. That is
    /// literally A's actor being told to spend B's item — the wrong-hand class of bug, manufactured
    /// on purpose. <see cref="CanPlaceChoiceItem"/> and the sub-choice classification would likewise
    /// ask A's hand about B's item's infusions.</para>
    ///
    /// <para>WITH THE FIELD MOVED, EVERY ACTION PATH IS SELF-CONSISTENT — the actor it names IS the
    /// owner of the CItem it passes — and "a merely-watched character may not reach a game seam" is
    /// enforced by a STRONGER mechanism than naming a different hand: the seams are UNREACHABLE.
    /// <c>CardsGameApi.IsActionTurn(_hand)</c> is false by construction whenever <c>_hand</c> is not
    /// the acting character, and it already stands in front of all of them — the use recess only
    /// appears while <c>turn || heldBonus</c> (<see cref="Tick"/>), <see cref="PlayContinued"/>
    /// cancels a placed card the moment it goes false, and <see cref="CanUseNow"/> /
    /// <see cref="UsableCount"/> both require it. So no seam runs for the wrong person; no seam runs
    /// at all.</para>
    ///
    /// <para>THE ONE HOLE THAT LEFT, and it is fixed at its own site: the second, deliberately
    /// turn-INDEPENDENT arm — an item whose ACTIVE BONUS is on offer (the Brille). That predicate
    /// matched the bar's bonuses to an item BY CARD ID, which is not a per-copy identity, so it could
    /// answer YES for a watched character holding another copy of the acting character's item. See
    /// <c>CardsGameApi.PlaceableBonusForItem</c>, which now also requires the bonus's own
    /// <c>Actor</c>. The two FLOW pumps need no such fix: <see cref="TickDemandPick"/> and
    /// <see cref="TickTakeDamagePick"/> are driven from <c>PileViewer.TickItemDemand</c> with the
    /// GAME's hand (they mirror a game decision that belongs to the acting/deciding actor), and their
    /// drop seams key REFERENCE-identical CItem instances out of the game's own dictionaries
    /// (<c>ItemCardPicker.cardSlots</c>, <c>UIUseItemsBar.ItemSlots</c>; CItem overrides neither
    /// Equals nor GetHashCode), so a watched character's card is structurally refused there.</para>
    /// </summary>
    private CardsHandUI? _hand;

    /// <summary>
    /// The actor who OWNS the item cards this fan is showing (<see cref="_hand"/>'s player), or null.
    /// Exists so <see cref="ItemChip.HasOfferedBonus"/> can scope its bonus lookup to its own owner —
    /// see <c>CardsGameApi.PlaceableBonusForItem</c> for why an unscoped by-id match is a cross-actor
    /// leak now that the fan follows the focused character.
    /// </summary>
    internal CPlayerActor? OwnerActor => _hand != null ? _hand.PlayerActor : null;

    private string _signature = string.Empty; // last-built inventory state, for cheap live refresh
    private bool _boardAnchored; // fan parented under the board root (mirrors PileBrowser)

    // Hand-sweep single-winner state, PER HAND (see UpdateHandSweep's "TWO ELECTIONS" note): the
    // chip each physical hand currently lifts, null = that hand lifts nothing. Two fields, not one
    // global winner + "which hand elected it", because that single slot was the drift from the
    // ability cards' arbitration — it could only ever describe ONE hand's candidate, so the other
    // hand swept the item fan without a lift and without a single-winner grab gate.
    private ItemChip? _handWinnerLeft;
    private ItemChip? _handWinnerRight;
    private float _nextHandLogAt;
    private float _nextHandMissLogAt;

    /// <summary>Arc index of the hand-sweep winner, or -1 — drives the fan SPLIT in
    /// <see cref="Relayout"/> (hand-fan parity: the highlighted chip is the pivot, its neighbours
    /// step aside, so the single winner is unmistakable while a hand sweeps through).</summary>
    private int _handWinnerIndex = -1;

    /// <summary>Chips this tick's sweep pop-suppressed, so the set can be cleared from scratch
    /// next tick (stale-flag proof: a chip that left the fan mid-frame cannot stay suppressed).</summary>
    private readonly List<ItemChip> _handSuppressed = new(12);

    // Diagnostics dedup for the live USE-slot gate log ("ITEM USE SLOT: shown/hidden …").
    private bool _useSlotShownLogged;

    // Requirement 6 (clip-in decision): the chip currently CLIPPED into the use slot awaiting a
    // Confirm/cancel decision (null = none). While set, the fan never live-rebuilds (so the decision
    // chip is never yanked), the chip is driven to the slot pose each tick, and a Confirm button shows.
    private ItemChip? _pendingUseChip;

    // ---- THE PLACED CARD OUTLIVES THE **USE**, TOO (user report 2026-08-09) -------------------
    //
    // "Wird ein Gegenstand verbraucht/genutzt geht er sofort mit der Animation in den Pile zurück,
    // BEVOR die Gegenstandsaktion vollständig abgeschlossen ist. Beispiel: Wenn ich einen Heiltrank
    // aktiviere muss ich zuerst noch drücken 'Ziele bestätigen' erst dann ist die Heilung
    // abgeschlossen. Während dessen soll der Gegenstand noch im Overlay liegen bleiben. Er soll in
    // diesem Zustand zwar ganz normal in die Hand genommen werden können aber beim Loslassen geht er
    // wieder zurück an das Overlay."
    //
    // ROOT CAUSE: ConfirmPendingUse called the use seam and then, in the very next statement, ran
    // FinishUsedChip — i.e. it treated "the seam was CALLED" as "the action is DONE". For a great
    // many items that call is the START of an action: UseItemService only enqueues
    // ScenarioRuleClient.ToggleItem, and for an Ability item that pushes a whole CPhaseAction whose
    // targeting and "confirm targets" the player still has to answer.
    //
    // THE STATE. While this is set, _pendingUseChip is STILL the placed card (so nothing about the
    // recess, the fan-close survivor, the grab paths or wire record 26 has to learn a new shape) but
    // the DECISION is over: the USE cap is down and no cancel path applies any more. The card lies
    // in the recess until CardsGameApi.ItemActionResolving says the game is finished with it — a
    // predicate recomputed from the game's own flow objects every frame, with no ledger, no latch
    // and no timer, so every ending converges on its own (see TickUseResolving).
    private bool _useResolving;

    // ---- THE PLACED CARD OUTLIVES THE FAN (user report 2026-08-09) ----------------------------
    //
    // "Wenn ich eine Gegenstandskarte in den Overlay gelegt habe und dann in die Welt klicke um den
    // Fächer zu schließen verschwindet auch die abgelegte Gegenstandskarte — das soll nicht sein.
    // Sie soll liegen bleiben, bis sie entweder wieder aufgehoben wird, oder man mit 'Use'
    // bestätigt."
    //
    // ROOT CAUSE: a card lying in the recess is still a member of _chips, and Close() collapsed
    // EVERY member into the items stack and destroyed it (CollapseChips) while clearing
    // _pendingUseChip and hiding the recess. So a click-away — the ordinary way to put the fan down
    // and get on with the turn — swept the placed card into the fold-in with the rest of the arc.
    // The 2026-08-09 hardware log shows exactly that at 8047: the close, and immediately after it
    // the same chip reported at 0.7 cm wide (down from 4.5) as it shrank into the stack.
    //
    // THE SURVIVOR. The recess is a child of the CONTROL BOARD, not of the fan root, so a clipped
    // chip physically survives the fan root going inactive on its own — all that was missing was for
    // the pile to stop killing it. It is lifted OUT of _chips at the close (so CollapseChips cannot
    // see it), parked here, and served by TickPlacedWhileClosed until one of the three endings the
    // user named happens: it is picked back up, USE confirms it, or play moves on. On a re-open it
    // is put BACK into _chips at its own item's arc position (Populate) instead of a fresh chip
    // being built for that item — one card, one object, no duplicate and no re-clip flicker.
    private ItemChip? _keptClip;

    // The arc POSITION the survivor held when the fan closed. Kept because it is what extension
    // record 26 transmits (ItemsPile.ClippedChipIndex): a peer's copy of the card is the slab at
    // that index, and it has to stay identifiable across the close or the peer's card would fall
    // out of their recess the moment the owner put their fan down. −1 = no survivor.
    private int _keptClipIndex = -1;

    // ---- element-CHOICE placement (items 2026-08-08) ------------------------------------------
    // Set together with _pendingUseChip when the placed card's item needs an element sub-choice
    // (CardsGameApi.ItemNeedsSubChoice). While set, the flow runs the game's OWN two-step:
    //   place → (next tick) slot click → element picker opens in the decision area → pick →
    //   UIUseItemsBar.useItem == item → the USE cap confirms via UIUseItemsBar.UseItem().
    // The bar slot is the game's; the mod only decides WHEN it is visible/clicked.
    private bool _pendingSubChoice;
    private UIUseItemScenario? _pendingChoiceSlot;
    // The slot click is deferred by one tick after the slot is re-activated: SetActive is immediate
    // but the docking surface polls per tick, and firing the click in the same frame the slot
    // appears would open the picker before UseBarsSurface has ever seen the bar populated.
    private bool _choiceClickArmed;
    // Cap-label state, so the cluster is only rebuilt on an actual wording change (a rebuild
    // re-lays out the whole Confirm/Undo/Use stack — see PlayTray.SetItemUseConfirmVisible).
    private bool _choiceCapReady;
    private bool _choiceCapShown;

    // Choice slots this pile deactivated so that NO item symbol is clickable without its card
    // being placed (the pending item's own slot is the single exception). Restored on teardown /
    // when the pending decision ends, but only where the bar still maps the same item to the same
    // slot — never re-activating a slot the game itself has since pooled (mirrors the
    // UseBarsSurface plain-slot split's restore rule).
    private readonly List<KeyValuePair<CItem, UIUseItemScenario>> _choiceHidden = new(4);
    private readonly List<KeyValuePair<CItem, UIUseItemScenario>> _slotScratch = new(8);

    // ---- ACTIVE-BONUS placement (the "Brille", user ruling 2026-08-09) ------------------------
    //
    // "Wenn ich gerade einen Angriff initiiert habe, dann soll die Brille im Gegenstands-Pile
    //  gehighlighted werden und ich kann sie hinlegen und 'usen' — das ist dann äquivalent zu dem
    //  Knopf der gedrückt wird."
    //
    // THE THIRD KIND OF PLACEMENT this recess can carry, alongside the plain item use and the
    // element-choice item. A worn item that asks, per triggering event, whether to spend itself is
    // NOT charged through UseItemService at all (that seam refuses passive items outright); it is
    // charged through a CActiveBonus built off its ITEM CARD, whose only 2D affordance is a row on
    // UIActiveBonusBar. Those rows are gone from the decision area now
    // (UseBarsSurface.EnforceActiveBonusSplit), so this flow is the ONLY way to answer them — which
    // is exactly why every state below is serviced whether the fan is open or closed, and why the
    // one thing that must never happen is a card lying in the recess with no way to resolve it.
    //
    // THE GESTURE, term for term with the button it replaces (all seams in CardsGameApi):
    //   place  → the card clips in and the USE cap comes up. Nothing is toggled yet: this is the
    //            established recess idiom ("hinlegen und usen"), and it means a mis-drop costs the
    //            player nothing at all.
    //   USE    → UIUseSlot.OnPointerDown() on the bonus's own row = THE BUTTON, MP-synced by the
    //            game itself (ActiveBonus.ToggleActiveBonus(…, fromClick: true) → ClickActiveBonusSlot
    //            GameAction → the peer's ProxyUseActiveBonus). The card STAYS LYING in the recess
    //            afterwards, because the bonus is toggled but not yet spent — the lying card IS the
    //            lit row, and it is what a peer sees through record 26.
    //   take it back out → the un-click: OnPointerDown() again → Unselect → UntoggleActiveBonus
    //            (fromClick: true), equally MP-synced. Refused, visibly, while the game has LOCKED
    //            the toggle (CActiveBonus.ToggleLocked): the card springs back into the recess and
    //            says so, rather than silently lying about a state the game will not give back.
    //   the game resolves it → the offer disappears from the bar. Toggled ⇒ the item was spent, so
    //            the card plays its burn/tap flourish and returns to the pile; never toggled ⇒ plain
    //            cancel, the card flies home un-used.
    private CActiveBonus? _pendingBonus;
    private bool _pendingBonusToggled;
    // Throttle for the "the game will not let go of this toggle" refusal line, so a player who keeps
    // tugging at a locked card cannot flood the hardware log.
    private float _bonusLockLogAt;

    // Requirement 8 (drop preview): a translucent GHOST duplicate shown at the use-slot pose while a held
    // usable item card comes NEAR the slot — a preview of where it will land if released. Parented under
    // the board's use slot (so it rides the slot pose + visibility); built lazily, toggled by proximity.
    private GameObject? _useGhost;

    internal bool IsOpen { get; private set; }

    /// <summary>
    /// ALWAYS FALSE since the whole-fan trigger grab was removed (user ruling 2026-08-02 — see
    /// <see cref="TogglePoke"/>): the item fan has exactly one anchoring, board-anchored. Kept as
    /// a property, not deleted, because it is a WIRE seam — <c>NetAvatarDriver</c> fills the
    /// <c>ItemFanHeld</c>/<c>ItemFanLeftHand</c> extras fields from it, and the packet layout must
    /// not shift (no ModBuild bump for a local-only interaction change). Peers therefore always
    /// draw the ghost item fan board-anchored, which is now the only state that exists.
    /// </summary>
    internal bool IsHandHeld => false;

    /// <inheritdoc cref="IsHandHeld"/>
    internal bool IsHeldByLeftHand => false;

    /// <summary>
    /// The open BOARD-ANCHORED fan's board-local anchor position (its root sits under the board
    /// root, so <c>localPosition</c> IS the board frame), or null while closed.
    /// Multiplayer read seam for the fan-anchor wire record: the base spot plus the owner's live
    /// per-board <c>[Cards] BrowseFanOffset + ItemCardOffset</c> tuning — the part a receiver
    /// could never derive, which is why their copy floated at the untuned default.
    /// </summary>
    internal Vector3? BoardLocalAnchor =>
        IsOpen && _boardAnchored && _root != null ? _root.localPosition : null;

    /// <summary>
    /// The currently OPEN item fan (null when closed / destroyed) — the exact counterpart of
    /// <see cref="CardFan.Current"/> for ability cards.
    ///
    /// ROOT CAUSE this exists (user report 5, "Itemkarten auf der Hand sind IMMER noch nicht im
    /// Spiegel zu sehen"): both consumers of "what cards is the local player holding" —
    /// <see cref="WorldUI.AvatarMirror"/> (the local self-preview) and
    /// <see cref="Net.NetAvatarDriver"/> (the multiplayer extras packet) — could only ever find the
    /// ABILITY fan, because <see cref="CardFan"/> published itself through a static Current and the
    /// items fan published NOTHING. The items fan is owned privately by <see cref="PileViewer"/>,
    /// so neither consumer had any way to reach it, and item cards were therefore invisible in the
    /// mirror AND on every peer. Publishing the open fan the same way closes both gaps at once.
    /// </summary>
    internal static ItemsPile? Current { get; private set; }

    /// <summary>The chips the fan currently holds (read-only view — mirrored / counted, never mutated).</summary>
    internal IReadOnlyList<ItemChip> Chips => _chips;

    // ------------------------------------------------------------------ config --

    /// <summary>
    /// The ITEMS stack transform (req #5 converge point), NOT the shared pile mount — the mount
    /// sits up by the DISCARD stack, and "simplifying" this to it makes the fan emerge from the
    /// wrong pile. PileViewer passes the mount only as a null fallback.
    /// </summary>
    internal void SetAnchor(Transform anchor) => _anchor = anchor;

    /// <summary>Item count of the acting character's inventory (drives the board stack look).</summary>
    internal int Count(CardsHandUI? hand)
    {
        List<CItem>? items = ItemsOf(hand);
        return items != null ? items.Count : 0;
    }

    /// <summary>
    /// The acting character's equipped items (null-safe; never mutated). Requirement A —
    /// verified UNFILTERED: <c>CInventory.AllItems</c> (CInventory.cs:35-55) concatenates EVERY
    /// equipped slot (Head, Body, Legs, TwoHand/OneHand, SmallItems, QuestItems) with no
    /// usability/slot-state filter, and neither <see cref="Populate"/> nor <see cref="Signature"/>
    /// drops any entry — passives and timed/triggered items (the initiative boots) are ALWAYS
    /// present; state only changes the LOOK (Spent = tapped, Consumed = ashen, usable = gold
    /// frame). The historical "boots missing" report was the presented-HAND mismatch during
    /// another actor's decision phase — fixed in CardsDriver.CurrentHand (deciding-actor chain).
    /// </summary>
    private static List<CItem>? ItemsOf(CardsHandUI? hand)
    {
        CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
        CInventory? inv = actor != null ? actor.Inventory : null;
        return inv != null ? inv.AllItems : null;
    }

    // ------------------------------------------------------------------ open/close --

    /// <summary>
    /// Toggle: open at a fixed reading wall above the board, or close if already open. THE only
    /// way the item fan opens (poke on the items stack, or the board laser's LaserToggle).
    ///
    /// REMOVED (user ruling 2026-08-02: "Das Greifen des GANZEN Fächers mit dem Trigger war
    /// möglich — das komplett entfernen, das war nie gewollt."): the items stack used to also be
    /// PINCH-GRABBABLE, which opened the fan as one object hanging off the grabbing palm
    /// (<c>OpenHeld</c> / <c>ReleaseHeld</c> / a <c>_followHand</c> the whole fan parented to).
    /// Besides being unwanted it broke both other item interactions for as long as it was held:
    /// the grabbing hand's <c>Grabber.Held</c> was non-null, which is an early-out in
    /// <c>CardsDriver.UpdateItemFanLaser</c> (no laser hover, no laser pluck — the reported
    /// "the laser goes straight through the fan"), and that same hand was excluded from the
    /// hand sweep (no highlights). Individual chip pluck is untouched.
    /// </summary>
    internal void TogglePoke(CardsHandUI hand, VRHand vrHand)
    {
        if (IsOpen)
        {
            Close($"user toggle (stack poke/laser, {vrHand.Side})");
            return;
        }
        Open(hand, vrHand);
    }

    /// <summary>
    /// USER-OPEN ONLY (user report 2026-08-07: "der Gegenstands-Fächer ging bei Schaden von selbst
    /// auf und liess sich nicht mehr schliessen"). This method has exactly ONE caller —
    /// <see cref="TogglePoke"/>, i.e. a deliberate poke/laser click on the items STACK. The two
    /// flow pumps below (<see cref="TickDemandPick"/>, <see cref="TickTakeDamagePick"/>) used to
    /// call it on their own AND re-call it every 0.5 s for as long as the flow was live, which is
    /// exactly the reported defect: the fan opened by itself on damage and every close (click-away,
    /// stack poke, foreign interaction) was undone within half a second, so it could never be
    /// closed. Both auto-open blocks are GONE; the flows now only SERVE an already-open fan and
    /// tell the player, through the board's item-use slot and the demand banner, that the items
    /// stack is where the candidates live. Keep it that way: any new "the flow needs the fan"
    /// requirement must raise a CUE, never call Open.
    /// </summary>
    private void Open(CardsHandUI hand, VRHand? by = null)
    {
        // AN EMPTY FAN MUST NOT OPEN (user ruling 2026-08-03: "Da 0 Gegenstände da waren soll es
        // auch gar nicht möglich sein den Fächer zu öffnen!"). With no items the fan used to open
        // anyway: no chips, no laser targets, and only its own "Gegenstände (0)" caption floating
        // over the board — which is also why it could not be clicked away, since the toggle lives
        // on the items STACK and the empty fan covers nothing to poke. Gated HERE rather than at
        // the toggle so every entry point is covered, including the flow-driven opens (surrender
        // pick, take-damage place), where an empty fan is equally useless.
        int count = ItemsOf(hand)?.Count ?? 0;
        if (count <= 0)
        {
            VRLog.Info("Cards", "Items pile browse REFUSED — the actor carries 0 items, so there is " +
                                "nothing to fan out. An empty fan has no chips and no way to close " +
                                "itself; the stack stays closed instead " +
                                $"(requested by {(by != null ? by.Side.ToString() : "auto/flow")}).");
            return;
        }
        _hand = hand;
        EnsureRoot();
        // Requirement 2: the fan anchors under the board root at the shared board-top spot.
        Transform? boardRoot = PlayTray.Current?.Root;
        _boardAnchored = boardRoot != null;
        Transform parent = boardRoot != null ? boardRoot
                         : (_anchor != null ? _anchor : _root!.parent);
        if (parent != null && _root!.parent != parent)
            _root.SetParent(parent, worldPositionStays: false);
        _root!.gameObject.SetActive(true);
        IsOpen = true;
        Current = this; // publish to the mirror + the net extras sender (see Current's doc comment)
        _signature = string.Empty; // force a build
        Populate(hand);
        if (boardRoot != null)
            PlaceAboveBoard(); // float above the board, inherit its scale (mirror PileBrowser)
        else
            PlaceAtHead(); // no board — head-relative fallback
        EmergeAll(); // req #5: fly the chips OUT of the pile stack (after _root is placed)
        // ANIMATION TELEMETRY, on the open line itself. The 2026-08-09 round of "die Animation ist
        // immer noch zu dezent" had to be diagnosed by reading the hovered chip's face width out of
        // unrelated laser diagnostics, because the log said NOTHING about what this animation was
        // actually configured to do — so the first question ("are the shipped defaults even in
        // effect?") could only be answered from the absence of an override. It is answered here now:
        // the effective dials, and the total wall-clock the deal takes for THIS many chips.
        float openDur = Mathf.Max(0.01f, CardsConfig.ItemFanOpenDuration.Value);
        float openStag = Mathf.Max(0f, CardsConfig.ItemFanOpenStagger.Value);
        float total = openDur + (_chips.Count > 1 ? (_chips.Count - 1) * 0.5f * openStag : 0f);
        VRLog.Info("Cards", $"ITEM FAN OPEN ({(boardRoot != null ? "board-anchored" : "head-relative fallback")}, " +
                            $"{_chips.Count} item(s)) — trigger: {(by != null ? $"USER stack poke/laser ({by.Side})" : "USER (unattributed)")}. " +
                            "There is NO automatic open path any more; if this line ever appears without a " +
                            "user trigger, a flow called Open() again. " +
                            $"DEAL: {openDur:F2}s per card + {openStag:F3}s centre-out stagger per place = " +
                            $"{total:F2}s total, seed {CardsConfig.ItemFanSeedScale.Value:F2}× → 1.00×, " +
                            $"bow {CardsConfig.ItemFanOpenArc.Value:F3} toward the viewer, roll " +
                            $"{CardsConfig.ItemFanOpenSpinDegrees.Value:F0}°, settle overshoot " +
                            $"{CardsConfig.ItemFanSettleOvershoot.Value:F2}. If these are not the shipped " +
                            "defaults, a cfg is overriding the presence pass.");
    }

    /// <summary>
    /// Close the fan. <paramref name="reason"/> is logged so every close (and, by absence, every
    /// failure to close) is traceable in the hardware log — the item-fan counterpart of
    /// <c>CardsDriver.CloseBrowser</c>'s reason string.
    ///
    /// <para><paramref name="keepPlacedCard"/> (default TRUE — user report 2026-08-09, see
    /// <see cref="_keptClip"/>): the card LYING IN the use recess is not part of the arc and does not
    /// fold away with it. It stays in the recess, with the recess and its USE cap, until the player
    /// picks it back up, confirms it, or play moves on. FALSE is for the teardown closes, where there
    /// is no board left to lie on: the pile stacks being hidden, the presented character changing,
    /// and <see cref="Destroy"/> — those retire the card into the items stack instead of stranding
    /// it. The other two clip-in flows (surrender pick, take-damage place) keep the old behaviour on
    /// purpose: their game-side selection survives in the picker/panel and their own pump re-opens
    /// the fan, so their chip is genuinely disposable.</para>
    /// </summary>
    internal void Close(string reason = "unspecified", bool keepPlacedCard = true)
    {
        if (!IsOpen)
            return;
        _lastCloseReason = reason;
        IsOpen = false;
        if (ReferenceEquals(Current, this))
            Current = null; // unpublish (mirror + net extras stop showing the fan this frame)
        _boardAnchored = false;
        ClearHandSweep();

        // THE SURVIVOR, decided BEFORE anything is torn down. A chip the player is HOLDING is not
        // lying in the recess (the grab already dropped PendingUse and backed the decision out), so
        // only a settled, un-held, clipped USE card qualifies; it is lifted out of _chips here so the
        // CollapseChips below — which folds and destroys every member — cannot see it.
        ItemChip? keep = null;
        if (keepPlacedCard && _pendingUseChip != null && _pendingUseChip.Holder == null
            && _pendingUseChip.PendingUse && PlayTray.Current?.ItemUseSlotTransform != null)
        {
            keep = _pendingUseChip;
            _keptClipIndex = _chips.IndexOf(keep);
            _chips.Remove(keep);
            _keptClip = keep;
            keep.ClearHandSuppressed(); // it left the arc's arbitration with the arc
        }

        if (keep == null)
        {
            if (_pendingSubChoice)
                AbandonChoice(_pendingUseChip?.Item); // never leave the game holding a half-answered pick
            // …and never leave a TOGGLED active bonus standing with no card left to express it. The
            // same rule as AbandonChoice above, for the third placement kind: the game's toggle is
            // backed out through the game's own row click, so peers see the untoggle too.
            ReleaseBonusToggle("the fan closed with no card left lying in the recess");
            _pendingSubChoice = false;
            _pendingChoiceSlot = null;
            _choiceClickArmed = false;
            _choiceCapShown = false;
            _choiceCapReady = false;
            _pendingUseChip = null; // #6: drop any pending decision on close
            _useResolving = false;  // …including a placement whose USE was already confirmed
            PlayTray.Current?.SetItemUseConfirmVisible(false, null);
            PlayTray.Current?.SetItemUseSlotVisible(false); // never leave the use slot up once the fan is gone
            _useSlotShownLogged = false;
        }
        // …and when a card DOES stay: the pending decision, the recess and the USE/CHOOSE cap all
        // stay exactly as they were. Nothing is re-shown and nothing is re-armed here — the state
        // simply is not torn down, which is what "sie soll liegen bleiben" means.

        _demandChip = null;     // surrender pick: the chip dies with the fan; the game selection
                                // survives in the picker and the pump re-opens the fan next tick
        _tdChip = null;         // take-damage place: same — the toggle survives in the panel
        if (_useGhost != null) // #8: never leave a drop-preview floating once the fan is gone
            _useGhost.SetActive(false);
        CollapseChips(); // req #5: fly the chips BACK INTO the pile stack, then self-destroy
        _signature = string.Empty;
        if (_root != null)
            _root.gameObject.SetActive(false);
        VRLog.Info("Cards", $"ITEM FAN CLOSE — trigger: {reason}. It stays closed until the player pokes/laser-clicks " +
                            "the items stack again (no flow re-opens it)." +
                            (keep != null
                                ? $" The card '{keep.name}' STAYS LYING in the use recess (it was at arc " +
                                  $"position {_keptClipIndex}) — the fan folding away is not a decision. " +
                                  "It lies there until it is picked back up, confirmed with USE, or play " +
                                  "moves on (which cancels it with an animation)."
                                : string.Empty));
    }

    /// <summary>Last close reason (diagnostics only — surfaced by the demand/take-damage cue lines
    /// so a "why is the fan not up?" question is answerable from the log alone).</summary>
    private string _lastCloseReason = "never closed";

    internal void Destroy()
    {
        ClearHandSweep();
        // The recess survivor is NOT in _chips (see _keptClip) and hangs off the board's use slot,
        // so ClearChips would leave it lying there after this pile is gone. There is no board left
        // to animate onto at teardown, so it goes with the rest.
        if (_keptClip != null)
        {
            Object.DestroyImmediate(_keptClip.gameObject);
            _keptClip = null;
            _keptClipIndex = -1;
        }
        ClearChips();
        if (_pendingSubChoice)
            AbandonChoice(_pendingUseChip?.Item);
        // A toggled active bonus is GAME state, not mod state: teardown must give it back through
        // the game's own row click, or the scenario would keep an armed bonus nobody can see.
        ReleaseBonusToggle("the item pile was torn down");
        _pendingSubChoice = false;
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        // Hand every suppressed item symbol back to the game (the split is a pure, reversible
        // visibility policy — teardown must leave the bar exactly as the game left it).
        RestoreChoiceHidden();
        _pendingUseChip = null; // #6
        _useResolving = false;
        _demandChip = null;     // surrender pick
        _demandActive = false;
        _demandLoseReward = false;
        _tdChip = null;         // take-damage place
        _tdActive = false;
        if (ReferenceEquals(Current, this))
            Current = null;
        IsOpen = false;
        _boardAnchored = false;
        _hand = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        if (_useGhost != null) // #8: the ghost is parented under the (foreign) use slot — destroy it explicitly
        {
            Object.DestroyImmediate(_useGhost);
            _useGhost = null;
        }
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null;
        }
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject("GloomhavenVR.ItemsPile").transform;
        Core.VRLayers.Apply(_root.gameObject);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_root, worldPositionStays: false);
        titleGo.transform.localPosition = new Vector3(0f, 0.16f, -0.004f);
        _title = titleGo.AddComponent<TextMeshPro>();
        _title.text = string.Empty;
        _title.alignment = TextAlignmentOptions.Center;
        _title.color = new Color(1f, 0.9f, 0.6f);
        Core.TmpFit.Fit(_title, 0.30f, 0.032f, maxFontSize: 0.34f, wrap: false);
        WorldUI.MrBacking.Label(_title); // fan title floats over the room in MR
    }

    // ------------------------------------------------------------------ per-frame --

    /// <summary>
    /// Called every frame while the board piles show (from <see cref="PileViewer.TickStatus"/>).
    /// Self-closes on context loss, follows the holding hand when held, runs the physical
    /// hand-sweep pop, gates the live USE slot, and cheaply rebuilds the chips when an item's
    /// state changed — but never while a chip is in hand, so a grab is never yanked away.
    /// </summary>
    internal void Tick(CardsHandUI? hand)
    {
        if (!IsOpen)
        {
            // The arc is down, but a card may still be LYING in the use recess (see _keptClip): it
            // has a live decision on it and therefore still needs servicing every frame.
            TickPlacedWhileClosed(hand);
            return;
        }
        if (hand == null || hand != _hand)
        {
            // The board switched to another character: this card belongs to the old character's
            // inventory, so it may not be left lying on a recess that now describes somebody else.
            Close("the board switched to another character", keepPlacedCard: false);
            return;
        }

        // Live refresh: rebuild only when nothing is held AND no decision is pending (so a chip clipped
        // into the use slot is never yanked by a rebuild) and the inventory state moved. The take-damage
        // clip (_tdChip) must guard too: unlike the surrender pick, its select seam (ToggleShieldItem →
        // Inventory.SelectItem) CHANGES the item's SlotState, which changes the signature — without the
        // guard the very act of placing the shield card would rebuild the fan and destroy the clipped chip.
        if (!AnyHeld() && _pendingUseChip == null && _tdChip == null)
        {
            string sig = Signature(hand);
            if (sig != _signature)
                Populate(hand);
        }

        // Physical fingertip sweep: lift the chip nearest the dominant index tip (single-winner).
        UpdateHandSweep();

        // NOTE (usable-highlight): the per-chip gold rim glow is toggled inside each chip's own face
        // maintenance from CanUseNow — nothing to drive from here. The throttled tally diagnostic lives
        // in PileViewer.TickStatus instead, because it must also report while this fan is CLOSED (the
        // items STACK carries the same highlight then, and there are no chips to count).

        if (_demandActive)
        {
            // ITEM SURRENDER pick: TickDemandPick owns the slot visibility, the clipped chip
            // and the confirm — the action-turn use gate below must not fight it (it would
            // hide the slot every tick: IsActionTurn is false while the Choreographer waits
            // in WaitingForItemRefresh).
            TickUseGhost(false, null);
        }
        else if (_pendingBonus != null && _pendingUseChip != null)
        {
            // ACTIVE-BONUS placement OUTRANKS the take-damage branch below, deliberately. An
            // item-backed bonus can be offered DURING a take-damage decision (an optional
            // prevent-damage bonus off a worn item is exactly that), and the _tdActive branch is a
            // hand-off to TickTakeDamagePick, which owns nothing about this flow. Without this the
            // placed card would be serviced by nobody for as long as the damage panel is up: no
            // cancel, no resolve, no untoggle — a card stranded on the board mid-decision.
            TickPendingUse(hand);
            TickUseGhost(false, null); // clipped in — the ghost preview is not needed
        }
        else if (_tdActive)
        {
            // TAKE-DAMAGE shield place (req C): TickTakeDamagePick owns the slot visibility,
            // the ghost and the clipped chip — the action-turn gate below must not fight it
            // (IsActionTurn is false while the enemy's attack waits on the damage decision).
        }
        else if (_pendingUseChip != null)
        {
            // Requirement 6: a chip is CLIPPED into the use slot awaiting a decision — keep the slot +
            // Confirm button up and glue the chip to the slot pose, or resolve the cancel/invalidation.
            TickPendingUse(hand);
            TickUseGhost(false, null); // clipped in — the ghost preview is not needed
        }
        else
        {
            // Requirement 3 (LIVE gate, re-evaluated every tick): the USE clip-in slot shows ONLY
            // while (a) it is this hand's own action turn AND (b) a HELD item card's live SlotState
            // is usable. Poll the held chip's live SlotState here (never cache it at create time).
            //
            // …OR while the held card's item has a LIVE OFFERED ACTIVE BONUS (the Brille). That case
            // is not turn-gated on purpose: the game itself decides when such a bonus may be
            // answered (it only builds a row while it is answerable — an attack of the owner's, an
            // incoming hit, an end-of-action window), and several of those windows are NOT the
            // owner's action turn (a take-damage decision runs on the ENEMY's turn). Re-applying an
            // action-turn gate on top would hide the recess for exactly the case this pass exists
            // for. The bonus's own presence on UIActiveBonusBar is the gate.
            bool turn = CardsGameApi.IsActionTurn(hand);
            ItemChip? heldUsable = HeldActivatableChip();
            bool heldBonus = heldUsable != null && heldUsable.HasOfferedBonus;
            bool showUseSlot = heldUsable != null && (turn || heldBonus);
            PlayTray.Current?.SetItemUseSlotVisible(showUseSlot);
            // Requirement 8: preview where the held card lands as it nears the slot.
            TickUseGhost(showUseSlot, heldUsable);
            if (showUseSlot != _useSlotShownLogged)
            {
                _useSlotShownLogged = showUseSlot;
                Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
                Vector3 pos = slot != null ? slot.position : Vector3.zero;
                VRLog.Info("Cards", $"ITEM USE SLOT: {(showUseSlot ? "shown" : "hidden")} " +
                                    $"(turn={turn} heldUsable={(heldUsable != null)} heldBonus={heldBonus}) " +
                                    $"at ({pos.x:F2},{pos.y:F2},{pos.z:F2}).");
            }
        }

        // Fan facing/position: BOARD-ANCHORED → re-read the shared board anchor (+ live
        // BrowseFanOffset) and billboard toward the head (ISSUE #7). The former hand-held branch
        // died with the whole-fan trigger grab (see TogglePoke).
        if (_root == null)
            return;
        if (_boardAnchored)
        {
            _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value + ItemFanOffset; // req #2 item nudge
            FaceHead(_root);
        }
    }

    /// <summary>Billboard a transform to face the head (mirror of PileBrowser.Tick's facing math).</summary>
    private static void FaceHead(Transform t)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = t.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // +Z points away from the viewer (uGUI/sprites read from -Z); tilt back a touch.
        t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
                     * Quaternion.Euler(-12f, 0f, 0f);
    }

    private bool AnyHeld()
    {
        for (int i = 0; i < _chips.Count; i++)
            if (_chips[i] != null && _chips[i].Holder != null)
                return true;
        return false;
    }

    // ------------------------------------------------------------------ content --

    private void Populate(CardsHandUI hand)
    {
        if (_root == null)
            return;
        ClearChips();

        // Flow 1 (goal-chest forfeit): the demanded candidates are the picker's REWARD items,
        // never the inventory — the same chips/fan machinery renders them (ItemChip.Create only
        // needs a CItem with an ID for the pooled ItemCardUI face).
        List<CItem>? items = DemandItemsOverride() ?? ItemsOf(hand);
        if (items == null || items.Count == 0)
        {
            _signature = Signature(hand);
            RefreshTitle(0);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            CItem item = items[i];
            if (item == null)
                continue;
            // THE RECESS SURVIVOR REJOINS THE ARC AS ITSELF (see _keptClip). It is still lying in
            // the use recess and still carries the pending decision, so building a SECOND chip for
            // the same item would put the card in two places at once — one in the arc, one in the
            // recess — and the wire index (record 26) would address the wrong one. Re-inserting the
            // existing object at its own item's position instead gives it a live arc slot to glide
            // home to on a cancel, keeps the decision unbroken, and costs no re-clip: Relayout skips
            // it for PendingUse and its parent is still the slot.
            if (_keptClip != null && _keptClip.Item != null && ReferenceEquals(_keptClip.Item, item))
            {
                _chips.Add(_keptClip);
                _keptClip = null;
                _keptClipIndex = -1;
                continue;
            }
            ItemChip chip = ItemChip.Create(this, _root, item);
            _chips.Add(chip);
        }
        // The survivor's item is gone from the list the fan is built from (used elsewhere, dropped,
        // or the fan switched to the forfeit rewards): there is no arc slot for it any more, so it
        // may not go on lying on the board. Back the decision out and fly it home.
        if (_keptClip != null)
            CancelPlacedCard(_keptClip, "its item is no longer in the list this fan shows");
        _signature = Signature(hand);
        RefreshTitle(_chips.Count);
        Relayout();
    }

    private void RefreshTitle(int count)
    {
        if (_title != null)
            _title.text = $"{PileViewer.Caption(PileKind.Items)} ({count})";
    }

    private void ClearChips()
    {
        for (int i = 0; i < _chips.Count; i++)
            if (_chips[i] != null)
                Object.DestroyImmediate(_chips[i].gameObject);
        _chips.Clear();
        ForgetSweepWinners();
    }

    /// <summary>World anchor the fan emerges from / collapses into (req #5): the ITEMS stack
    /// transform handed to <see cref="SetAnchor"/>. Falls back to the fan root.</summary>
    private Vector3 PileConvergeWorld() =>
        _anchor != null ? _anchor.position : (_root != null ? _root.position : Vector3.zero);

    /// <summary>
    /// Requirement 5 (emerge): once the fan root is placed, drop every chip ONTO the pile stack point
    /// (in ROOT-LOCAL space, so it rides the board like the arc homes) and hand each one its own
    /// fly-out — the chips visibly deal OUT of the pile into the arc.
    ///
    /// PRESENCE PASS (user report 2026-08-08: "Ich mag die Animation im Item-Pile sehr aber sie ist
    /// (insbesondere in mixed Reality) etwas zu dezent."). He likes the motion, so nothing here is
    /// replaced — but every chip used to be given the SAME start moment and the SAME exponential
    /// home-lerp, i.e. one blob of twelve cards sliding a straight chord and decelerating to a halt.
    /// Two of the three things that make a move survive passthrough were simply absent from it:
    ///
    ///   • a moving FRONT. The eye tracks an onset that travels; a simultaneous move is one flicker,
    ///     and a room full of real edges and real parallax eats a single flicker. So each chip now
    ///     starts <c>ItemFanOpenStagger</c> later per place of distance from the fan centre — the
    ///     same centre-out ripple the hand fan reveals on (CardFan.OpenProgress), which is also why
    ///     it needs no new idiom to read as "ours".
    ///   • a SIGNED unfold. <paramref name="i"/>'s side of the fan decides which way its chip rolls
    ///     out of the stack (<c>ItemFanOpenSpinDegrees</c>), so the fan opens like a hand of cards
    ///     rather than sliding apart. A roll changes the card's OUTLINE, and an outline change is
    ///     the one signal a cluttered background cannot supply by accident.
    ///
    /// The third — motion in DEPTH — is the chip's own business (<see cref="ItemChip.BeginEmerge"/>
    /// bows the flight toward the viewer and grows the card from a much smaller seed), because both
    /// terms are expressed against the chip's live home pose.
    /// </summary>
    private void EmergeAll()
    {
        if (_root == null)
            return;
        Vector3 localConverge = _root.InverseTransformPoint(PileConvergeWorld());
        int n = _chips.Count;
        float mid = (n - 1) * 0.5f;
        float stagger = Mathf.Max(0f, CardsConfig.ItemFanOpenStagger.Value);
        for (int i = 0; i < n; i++)
        {
            ItemChip c = _chips[i];
            // A CLIPPED chip is not in the arc — it is the recess survivor that just rejoined the
            // list (Populate). Dealing it out of the items stack would rip it off the board and fly
            // it to an arc slot it is deliberately not standing in; it stays exactly where it lies.
            if (c == null || c.Holder != null || c.PendingUse)
                continue;
            // Centre-out: the middle chip leaves first, the outermost pair last. Distance is
            // measured in PLACES (not metres), so the ripple keeps its rhythm on a 2-item and on a
            // 12-item fan instead of stretching with the arc.
            float fromCentre = i - mid;
            c.BeginEmerge(localConverge, Mathf.Abs(fromCentre) * stagger,
                          spinSign: fromCentre >= 0f ? 1f : -1f);
        }
    }

    /// <summary>
    /// Requirement 5 (collapse): on close, hand each chip off to a self-driven glide BACK INTO the pile
    /// stack, then it destroys itself (recycling its ItemCardUI in OnDisable). The chips are re-parented
    /// OUT of the fan root first so they keep updating after the root is deactivated. A held chip (rare
    /// close-mid-grab) is dropped immediately. Clears the live list so a re-open builds fresh chips.
    ///
    /// PRESENCE PASS (same report): the close is the open played BACKWARDS, which is why the delay
    /// here is <c>(far − |i − mid|) × ItemFanCloseStagger</c> — outermost chip first, centre chip
    /// last, the exact reverse of <see cref="EmergeAll"/>'s centre-out ripple. A fold-in that
    /// mirrors the fan-out is read as the same object closing; one that runs in the same order as
    /// the open is read as a second, unrelated animation.
    ///
    /// Nothing may POP (standing user ruling): a chip waiting out its delay is NOT hidden and NOT
    /// snapped anywhere — it holds its exact world pose (the collapse tick's t=0 is the identity)
    /// until its own moment arrives. The chips are already detached from the fan root at that
    /// point, so the whole staggered close plays out even though the root is deactivated on the
    /// same frame.
    /// </summary>
    private void CollapseChips()
    {
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root
                        : (_anchor != null ? _anchor : null);
        int n = _chips.Count;
        float mid = (n - 1) * 0.5f;
        float stagger = Mathf.Max(0f, CardsConfig.ItemFanCloseStagger.Value);
        for (int i = 0; i < n; i++)
        {
            ItemChip c = _chips[i];
            if (c == null)
                continue;
            if (c.Holder != null)
            {
                Object.DestroyImmediate(c.gameObject); // held on close — just drop it
                continue;
            }
            if (keep != null)
                c.transform.SetParent(keep, worldPositionStays: true); // survive the root deactivation
            float fromCentre = i - mid;
            c.BeginCollapse(converge, (mid - Mathf.Abs(fromCentre)) * stagger,
                            spinSign: fromCentre >= 0f ? 1f : -1f);
        }
        _chips.Clear();
        ForgetSweepWinners();
    }

    /// <summary>Cheap change key: item count + each item's slot-state (drives live refresh).
    /// Instance (no longer static): while the goal-chest forfeit demand overrides the content
    /// source the key is prefixed + built from the REWARD items' IDs instead, so the flip
    /// inventory↔rewards (and any reward-list change) rebuilds, and inventory changes during
    /// the forfeit cannot yank the reward fan.</summary>
    private string Signature(CardsHandUI? hand)
    {
        List<CItem>? rewards = DemandItemsOverride();
        if (rewards != null)
        {
            var rb = new StringBuilder(8 + rewards.Count * 6);
            rb.Append("LR:").Append(rewards.Count).Append(':');
            for (int i = 0; i < rewards.Count; i++)
                rb.Append(rewards[i] != null ? rewards[i].ID : -1).Append(',');
            return rb.ToString();
        }
        List<CItem>? items = ItemsOf(hand);
        if (items == null)
            return "0";
        var sb = new StringBuilder(items.Count * 4);
        sb.Append(items.Count).Append(':');
        for (int i = 0; i < items.Count; i++)
        {
            CItem it = items[i];
            sb.Append(it != null ? (int)it.SlotState : -1).Append(',');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ placement --

    /// <summary>
    /// Shared board-top reading pose (requirement 2, mirror of PileBrowser.PlaceAboveBoard):
    /// float the fan at the SAME spot above the control board the discard/burnt fans use.
    /// </summary>
    private void PlaceAboveBoard()
    {
        if (_root == null)
            return;
        _root.localPosition = BoardAnchorBase + CardsConfig.BrowseFanOffset.Value;
        _root.localRotation = Quaternion.identity; // Tick billboards the WORLD rotation each frame
        FaceHead(_root);
    }

    /// <summary>Fixed head-relative reading pose (fallback when no control board exists).</summary>
    private void PlaceAtHead()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        Vector3 pos = headT.position
                      + flatForward * (CardsConfig.TrayForward.Value * 0.9f * scale)
                      + Vector3.up * (-(CardsConfig.TrayDown.Value - 0.22f) * scale);
        _root.position = pos;
        _root.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(-12f, 0f, 0f);
    }

    // ------------------------------------------------------------------ layout --

    private void Relayout()
    {
        if (_root == null)
            return;
        int n = _chips.Count;
        if (n == 0)
            return;

        // PER-PILE SPREAD, read LIVE (user request 2026-08-03: the item fan must be wider so a
        // single chip can be grabbed physically). Shipped default = the old constant.
        float radius = CardsConfig.FanRadius.Value * CardsConfig.FanRadiusFactor(PileKind.Items).Value;
        float step = n > 1
            ? Mathf.Min(CardsConfig.FanStepDegrees(PileKind.Items).Value, MaxArcDegrees / (n - 1))
            : 0f;
        float start = -step * (n - 1) * 0.5f;

        // HAND-FAN PARITY (the collider strip — see FanSweep.StripWidth). Item chips overlap each
        // other by roughly half a card in this arc, and until now each one carried a FULL-size
        // grab box: a fingertip inside the overlap measured 0.0 cm to two or three chips at once,
        // which is exactly what the 2026-08-02 hardware log shows ("contact 0,0 cm; runner-up
        // 0,0 cm"). Ties do not displace the incumbent, so the lift stuck and the sweep skipped
        // chips. Shrinking each chip's box to the chord between neighbouring centres makes the
        // per-chip regions TILE, which also fixes the ProximityGrabber (it picks the nearest
        // collider by the same ClosestPoint metric) — "what pops is what I grab", by geometry.
        // The chord is fan-local; chips are enlarged by ChipScale, hence the divide.
        float stripFanLocal = n > 1 ? FanSweep.ArcChord(radius, step) / ChipScale : float.MaxValue;
        int hovered = _handWinnerIndex >= 0 && _handWinnerIndex < n ? _handWinnerIndex : -1;

        for (int i = 0; i < n; i++)
        {
            ItemChip chip = _chips[i];
            // A HELD chip rides a hand and a CLIPPED chip lies in the board's use recess: neither is
            // at an arc position, so neither may be given one.
            //
            // ROOT CAUSE of "Die Karte liegt nicht perfekt angeordnet auf dem Bereich, sondern
            // schräg" (user report 2026-08-08, .planning/debug/item_area.png). The clipped chip used
            // to be skipped only for `Holder != null`. ItemChip.OnRelease runs, in this order:
            // OnChipReleased (which CLIPS the chip onto the slot at an exact zero local pose) and
            // then RefreshFanLayout — i.e. THIS loop — which called SetHome on the chip that had
            // just been clipped. SetHome writes the transform whenever nothing holds the chip, and
            // by then the chip's parent is the SLOT: the fan-arc pose (a lateral chord offset plus
            // the arc's own −angle·0.85 roll, and for a SPENT item a further 90°) was stamped into
            // SLOT-local space. That is exactly the screenshot — the card sitting a chord's width
            // off centre and rolled a few degrees against the recess frame, overhanging one corner.
            // Update() then returns early for a PendingUse chip, so nothing ever corrected it; every
            // later Relayout (any sweep winner change) re-stamped it.
            //
            // This is the item-fan edition of the load-bearing hand-transfer lesson in CardFan
            // (SetCards stamping AllowsGateHand onto a HELD card): while a card is HELD — or, here,
            // while it is CLIPPED — write nothing its hold depends on. Belt at the far end too:
            // SetHome itself refuses to move a PendingUse chip.
            if (chip == null || chip.Holder != null || chip.PendingUse)
                continue;

            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * radius,
                                  (Mathf.Cos(rad) - 1f) * radius * 0.55f,
                                  -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle * 0.85f);
            // SPENT items lie "tapped": roll the chip 90° in its slot (requirement 3).
            if (chip.State == ItemChip.Visual.Spent)
                rot *= Quaternion.Euler(0f, 0f, 90f);
            // Split the arc around the highlighted chip (hand-fan parity): the pivot holds still,
            // its neighbours slide along their OWN local right so the winner reads unmistakably.
            if (hovered >= 0 && i != hovered)
                pos += rot * new Vector3(FanSweep.SplitOffset(i - hovered) * ChipScale, 0f, 0f);
            chip.SetHome(pos, rot, ChipScale);
            // The last chip in the arc is fully exposed and the split pivot has room on both
            // sides — both keep their full grab box; everything else wears its visible strip.
            // (A chip CLIPPED into the use slot never reaches this line — it left the loop above —
            // and keeps the FULL box its grab gave it, so pulling it back out of the recess never
            // gets harder because the sweep re-laid the arc out around some other chip.)
            chip.SetGrabStrip(i == n - 1 || i == hovered ? float.MaxValue : stripFanLocal);
        }
    }

    // ------------------------------------------------------------------ hand sweep --

    /// <summary>
    /// Physical HAND sweep over the item fan — now literally the SAME election the ability hand
    /// fan runs (<see cref="FanSweep.Score{T}"/>), which is what the user asked for ("gleiche die
    /// Fächer der Piles an das System der Handkarten an"): EITHER free hand's index tip elects a
    /// SINGLE winner among the chips, candidacy by tip OR palm against the reach resolved from the
    /// chip's own live world size (<see cref="FanSweep.ResolveReach"/>), ranked TIP-FIRST with
    /// incumbent hysteresis. The winner POPS and every other chip is suppressed for that hand
    /// (<see cref="ItemChip.SetHandSuppressed"/>) so the ProximityGrabber's own candidate — and
    /// therefore the trigger grab — lands on the same single chip. Read-only: it only lifts for
    /// readability; the pinch/laser own the pull-into-hand.
    ///
    /// ROOT CAUSE this fixes (user report 2026-08-02, MP host: "physically I could only grab
    /// chips with the LEFT hand, but the LEFT hand produced no highlights; the RIGHT hand
    /// highlighted but could not grab anything"). The two halves had DIFFERENT hand policies:
    ///   • HIGHLIGHT was <c>VRHands.Primary</c> only — a hard dominant-hand filter, so the
    ///     off-hand swept through the fan in complete silence no matter how close it got.
    ///   • GRAB is <c>ProximityGrabber</c>, which runs on BOTH hands — but on the DOMINANT hand
    ///     the trigger defers to the laser (<c>ProximityGrabber.Tick</c> yields whenever
    ///     <c>Ray.HasFreshUiHit</c> is set, and the item-fan laser path sets
    ///     <c>Ray.UiHitOverride</c> on every hovered chip), so on the dominant hand the pluck
    ///     belongs to <c>CardsDriver.UpdateItemFanLaser</c> — which that session refused every
    ///     pluck at its blocking-modal commit gate (the MP player picker, see
    ///     <c>ModalFallback.NonBlockingMenus</c>). Net effect, exactly as reported: dominant
    ///     hand = highlight, no grab; off hand = grab, no highlight.
    /// Sweeping BOTH hands makes the highlight policy match the grab policy — every hand that
    /// can take a chip also lights it up first.
    ///
    /// SCORING: ranked TIP-FIRST, like every other fan. This method used to rank by
    /// <c>min(tip, palm)</c> — the one place the three copies of the election had genuinely
    /// drifted apart — which let a chip the PALM brushed outrank the chip the index finger was
    /// pointing at. With the colliders now tiling the arc (see <see cref="Relayout"/>) the tip
    /// distance is a clean partition, so tip-first is both correct and the shared behaviour.
    ///
    /// <para>TWO ELECTIONS, ONE PER HAND (user ruling 2026-08-08: "Auch Item-Karten sollen (wie die
    /// normalen Karten auch) in die linke Hand genommen werden können … Sie sollen also wie normale
    /// Karten reagieren"). THIS is where the item mirror had drifted from the ability cards'
    /// original. Both fans sweep with both hands, but the ability side keeps a SEPARATE winner per
    /// hand (<c>CardsDriver.UpdateHandContactArbitration</c>: <c>_handContactWinner</c> for the
    /// dominant hand, <c>_gateContactWinner</c> for the gate hand, each with its OWN incumbent), and
    /// a card that loses BOTH elections refuses BOTH hands — which is exactly why
    /// <see cref="VRCard"/> carries TWO refused-hand slots. The item fan ran the two hands into ONE
    /// global winner and then asked "which hand elected it": a single slot that can only ever
    /// describe one hand. Consequences, both of them the user's report:
    /// <list type="bullet">
    /// <item>only ONE hand at a time could lift anything. Reach in with the left hand while the
    /// right is anywhere near the arc and the right's better score takes the global winner, so the
    /// left hand sweeps the fan in silence — no pop, no promise, and the player concludes the left
    /// hand cannot take item cards;</item>
    /// <item>the losers refused only the winning hand, so the OTHER hand had no single-winner grab
    /// gate at all — its ProximityGrabber could land on any chip it brushed, i.e. "what lights up is
    /// not what I get" for that hand, the very defect this election exists to prevent.</item>
    /// </list>
    /// Now each hand runs its own election with its own incumbent, each hand's winner POPS, and each
    /// loser refuses every hand that elected somebody else (<see cref="ItemChip.SetHandSuppressed"/>
    /// is accumulative over two hands, mirroring <c>VRCard.SetHandPopSuppressed</c>). A chip that
    /// both hands elect simply pops once.</para>
    /// </summary>
    private void UpdateHandSweep()
    {
        // Re-derive the per-hand suppression from scratch every tick (stale-flag proof).
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].ClearHandSuppressed();
        }
        _handSuppressed.Clear();

        ItemChip? prevLeft = _handWinnerLeft;
        ItemChip? prevRight = _handWinnerRight;

        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        FanSweepPick<ItemChip> leftPick = Elect(left, prevLeft, out float leftScale);
        FanSweepPick<ItemChip> rightPick = Elect(right, prevRight, out float rightScale);
        _handWinnerLeft = leftPick.Winner;
        _handWinnerRight = rightPick.Winner;

        // POP set = the union of the two winners. Drop a former winner only when it is no longer
        // ANY hand's winner (a chip both hands are on must keep its lift while either hand holds
        // it), then raise both current winners — idempotent, so a steady frame writes two bools.
        if (prevLeft != null && !ReferenceEquals(prevLeft, _handWinnerLeft)
            && !ReferenceEquals(prevLeft, _handWinnerRight))
            prevLeft.SetFingertipPop(false);
        if (prevRight != null && !ReferenceEquals(prevRight, _handWinnerRight)
            && !ReferenceEquals(prevRight, _handWinnerLeft))
            prevRight.SetFingertipPop(false);
        _handWinnerLeft?.SetFingertipPop(true);
        _handWinnerRight?.SetFingertipPop(true);

        // ---- THE TICK THAT GOES WITH THE LIFT (user report 2026-08-09, "Controller-Vibrationen") --
        //
        // The ability fan ticks the hand the instant a card starts lifting under it —
        // CardFan.UpdateFingertipHover ends its state change with `dom.SendHaptic(HoverTick)`, and
        // VRCard.OnPokeEnter does the same for a fingertip poke. The item fan raised its chip in
        // complete silence: this election, the ONE event that decides which chip lifts, never spoke
        // to the controller at all. (A HoverTick did reach the hand from ProximityGrabber's own
        // highlight edge, but that is a DIFFERENT edge with a different reach and a different
        // debounce, so the buzz and the lift routinely landed at different moments on different
        // chips — which is exactly why the feedback read as absent rather than as merely weak. The
        // ability card does not have that split: VRCard implements IGrabHighlight, so the grabber's
        // tick and the card's pop are the same event by construction.)
        //
        // SAME PRESET, SAME EDGE, SAME DEBOUNCE — deliberately not a new haptic vocabulary:
        //   * HapticPreset.HoverTick, the mod's single "a hover/highlight started" effect (0.15
        //     amplitude / 12 ms), which is what every other hover in the mod plays, item chips
        //     included (ItemChip.OnPokeEnter already ticks the laser hover with it).
        //   * On the hand whose OWN election changed, so a two-handed sweep ticks the hand that
        //     actually moved — the per-hand elections are separate for exactly this reason.
        //   * On the winner-CHANGE edge only, never per frame. The debounce is the election's own
        //     incumbent hysteresis (FanReach.Sticky, the same margin ProximityGrabber's
        //     SwitchMarginMeters serves): an incumbent keeps its lift until a rival is DECISIVELY
        //     closer, so a hand drawn across an arc of overlapping chips cannot machine-gun the
        //     controller at the strip boundaries. This is CardFan's "debounced: only on card change"
        //     word for word.
        //
        // AND DELIBERATELY NOT DE-DUPLICATED AGAINST ProximityGrabber's OWN TICK, because two
        // proximity tick sources per card IS the ability fan's behaviour and this is a parity task.
        // A card crossed by a sweeping hand can tick twice as well — once when ProximityGrabber's
        // highlight moves to it (palm reach) and once when CardFan.UpdateFingertipHover's pop moves
        // to it (index-tip reach) — because those are two genuinely different reaches answering two
        // genuinely different questions ("which card would my grab take" vs "which card is my finger
        // on"). Suppressing one of the chip's two would make the item fan QUIETER than the ability
        // fan, which is the opposite of the report. Both effects are the same 0.15 amplitude / 12 ms
        // impulse, so a frame in which both fire reads as one slightly firmer tick, not as a rattle.
        //
        // NO TICK FOR A CHIP THAT MAKES NO GRAB PROMISE, structurally rather than by a new guard: a
        // held chip, a chip clipped into the use recess and a chip whose USE is still resolving are
        // all PendingUse or Holder-bound, and IFanSweepTarget.SweepEligible rejects both, so such a
        // chip can never BE a winner here. A buzz over the berth would contradict it, and the berth
        // is the placement flow's to own.
        //
        // MULTIPLAYER: NOTHING TO MIRROR HERE, and that is an answer to the 1:1 ruling rather than an
        // exception to it. The ruling governs what a peer SEES of this player's board ("alle anderen
        // sehen … bei seinem board"), and it is honoured in full: the chip that lifts, how far it
        // lifts and the arc that opens around it all ride extension record 6 + record 28 and are
        // replayed by Net/RemoteItemFan. A haptic is not board state — it is a pulse in the motor of
        // THIS player's controller, produced by THIS player's hand being at THIS chip. There is no
        // hand of theirs at that chip on a peer's machine, and buzzing a peer's controller for
        // somebody else's hover would be a phantom, not a mirror. The ability fan takes the identical
        // position (CardFan/VRCard tick locally and sync only the highlight INDEX), so this is the
        // established reading of the rule and not a new carve-out.
        if (!ReferenceEquals(prevLeft, _handWinnerLeft) && _handWinnerLeft != null)
        {
            left?.SendHaptic(HapticPreset.HoverTick);
            LogHoverFeedbackOnce();
        }
        if (!ReferenceEquals(prevRight, _handWinnerRight) && _handWinnerRight != null)
        {
            right?.SendHaptic(HapticPreset.HoverTick);
            LogHoverFeedbackOnce();
        }

        // ARC SPLIT / MP index: one pivot, because splitting an arc around two pivots at once is
        // not a layout. The DOMINANT hand's winner is the pivot when it has one (it is the hand the
        // laser and every other single-owner rule already defer to), else the other hand's — so a
        // one-handed sweep behaves exactly as before, whichever hand it is.
        ItemChip? pivot = DominantWinner() ?? _handWinnerLeft ?? _handWinnerRight;
        int index = pivot != null ? _chips.IndexOf(pivot) : -1;
        if (index != _handWinnerIndex)
        {
            _handWinnerIndex = index;
            Relayout(); // re-split the arc around the new pivot (hand-fan parity), only on a CHANGE
        }

        float now = Time.unscaledTime;
        bool changed = !ReferenceEquals(prevLeft, _handWinnerLeft)
                       || !ReferenceEquals(prevRight, _handWinnerRight);
        if (changed && now >= _nextHandLogAt)
        {
            if (_handWinnerLeft != null && left != null)
            {
                _nextHandLogAt = now + 0.5f;
                FanSweep.LogWinner("Item-fan", left.Side.ToString(), leftPick,
                    FanSweep.ResolveReach(leftScale, ((IFanSweepTarget)_handWinnerLeft).SweepFaceWidthWorld));
            }
            if (_handWinnerRight != null && right != null)
            {
                _nextHandLogAt = now + 0.5f;
                FanSweep.LogWinner("Item-fan", right.Side.ToString(), rightPick,
                    FanSweep.ResolveReach(rightScale, ((IFanSweepTarget)_handWinnerRight).SweepFaceWidthWorld));
            }
        }

        if (_handWinnerLeft == null && _handWinnerRight == null)
        {
            // NEAR-MISS diagnostic (throttled, only while a hand is genuinely reaching): name the
            // hand, the chip, both probe distances and the EFFECTIVE reaches they failed, all in
            // real centimetres. This is the line that decides "the sweep is broken" versus "the
            // hand was never close enough" on the next hardware log without any arithmetic.
            bool leftCloser = leftPick.Miss != null && leftPick.MissContact <= rightPick.MissContact;
            FanSweepPick<ItemChip> missPick = leftCloser ? leftPick : rightPick;
            VRHand? missHand = leftCloser ? left : right;
            float missScale = leftCloser ? leftScale : rightScale;
            if (missPick.Miss != null && missHand != null && Time.unscaledTime >= _nextHandMissLogAt)
            {
                FanReach missReach = FanSweep.ResolveReach(missScale,
                    ((IFanSweepTarget)missPick.Miss).SweepFaceWidthWorld);
                if (missPick.MissContact <= missReach.Palm * 2f)
                {
                    _nextHandMissLogAt = Time.unscaledTime + 2f;
                    FanSweep.LogNearMiss("Item-fan", missHand.Side.ToString(), missPick, missReach);
                }
            }
            return;
        }

        // SINGLE-WINNER for the GRAB too (user report "what lights up is not what I get"): every
        // other chip refuses the electing hand in ItemChip.AllowsHand, so the ProximityGrabber —
        // which picks the nearest collider by the very same ClosestPoint metric — cannot land on
        // a chip that hand merely brushed while sweeping. Per hand, and ACCUMULATIVE: a chip that
        // lost both elections refuses both hands (the VRCard two-slot rule), where the old single
        // slot let the second write silently re-open the first hand's gate.
        //
        // A chip CLIPPED into the use slot is never suppressed. It is not in the arc — it lies in
        // the board's recess — so no arc election has any business refusing the hand that reaches
        // for it, and refusing it is exactly what stopped a placed card from being picked back up
        // (requirement 5b: the recess is emptied by GRABBING the card, see ItemChip.OnPoke).
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c == null || c.Holder != null || c.PendingUse)
                continue;
            bool touched = false;
            if (_handWinnerLeft != null && !ReferenceEquals(c, _handWinnerLeft))
            {
                c.SetHandSuppressed(left);
                touched = true;
            }
            if (_handWinnerRight != null && !ReferenceEquals(c, _handWinnerRight))
            {
                c.SetHandSuppressed(right);
                touched = true;
            }
            if (touched)
                _handSuppressed.Add(c);
        }
    }

    /// <summary>
    /// One-shot session confirmation that the item fan's PHYSICAL-contact channel now speaks the
    /// ability fan's language — the grep line a hardware log is read for
    /// (<c>"Item-fan hover feedback ACTIVE"</c>), modelled on <c>CardFan</c>'s own
    /// <c>s_loggedFingertipHover</c> line so the two fans are checked the same way. One line per
    /// session, so it can never compete with the per-change <c>FanSweep.LogWinner</c> evidence that
    /// already names the chip, the hand and the reaches.
    /// </summary>
    private static void LogHoverFeedbackOnce()
    {
        if (s_loggedHoverFeedback)
            return;
        s_loggedHoverFeedback = true;
        VRLog.Info("Cards", "Item-fan hover feedback ACTIVE (parity pass 2026-08-09): the elected " +
                            "chip now lifts on VRCard's own pop (up 0.012 m + [Cards] " +
                            "FanSelectedPopForward toward the viewer, ×1.18, ramped at 8/s) and the " +
                            "electing hand gets the standard HapticPreset.HoverTick on the " +
                            "winner-change edge — the same preset, edge and hysteresis the ability " +
                            "hand fan uses. The laser channel already ticked (ItemChip.OnPokeEnter) " +
                            "and now shares the same lift.");
    }

    /// <summary>One-shot guard for <see cref="LogHoverFeedbackOnce"/>.</summary>
    private static bool s_loggedHoverFeedback;

    /// <summary>
    /// One-shot session confirmation that the card LYING IN THE USE RECESS now speaks the same
    /// feedback language as a card lying in one of the board's slot recesses — the grep line the next
    /// hardware log is read for (<c>"Item recess hover feedback ACTIVE"</c>), modelled on
    /// <see cref="LogHoverFeedbackOnce"/> so the arc and the berth are checked the same way. Fired on
    /// the first lift of a session only; the per-hover evidence is already carried by the item-fan
    /// laser line and by the stand-down's own "item use recess" zone.
    /// </summary>
    private static void LogRecessFeedbackOnce()
    {
        if (s_loggedRecessFeedback)
            return;
        s_loggedRecessFeedback = true;
        VRLog.Info("Cards", "Item recess hover feedback ACTIVE (parity pass 2026-08-09): the card lying " +
                            "in the item-USE berth now LIFTS on hover — up 0.012 m + [Cards] " +
                            "FanSelectedPopForward toward the viewer, ×1.18, ramped at 8/s, applied in " +
                            "SLOT-local space (its rotation is identity there, so a spent card's tapped " +
                            "roll cannot send it sideways). Both channels feed it, exactly as they do " +
                            "for a slot-docked ability card: the BEAM through ItemChip.OnPokeEnter " +
                            "(pop + HoverTick) and the HAND through IGrabHighlight, i.e. the very " +
                            "ProximityGrabber highlight edge that already plays HoverTick — so the buzz " +
                            "and the lift are one event. The chip stays OUT of the arc sweep " +
                            "(SweepEligible is unchanged): it is not at an arc position and takes no " +
                            "part in the split, the pivot or the grab gate. A placed card whose active " +
                            "bonus the game has LOCKED still lifts for nobody.");
    }

    /// <summary>One-shot guard for <see cref="LogRecessFeedbackOnce"/>.</summary>
    private static bool s_loggedRecessFeedback;

    /// <summary>
    /// ONE hand's election over the arc — the per-hand half of <see cref="UpdateHandSweep"/>'s two
    /// elections, with that hand's OWN incumbent so the two can never steal each other's
    /// hysteresis (the same separation <c>CardsDriver</c> keeps between its dominant-hand and
    /// gate-hand picks). A hand qualifies while it is tracked and NOT holding anything: the held
    /// chip already rides that hand, and popping a second one under it reads as a phantom.
    /// </summary>
    private FanSweepPick<ItemChip> Elect(VRHand? hand, ItemChip? incumbent, out float worldScale)
    {
        worldScale = 1f;
        FanSweepPick<ItemChip> pick = FanSweepPick<ItemChip>.Empty;
        if (hand == null || !hand.HasPose || hand.Grabber.Held != null)
            return pick;

        Vector3 tip = hand.Rig.IndexTip.position;
        Vector3 palm = hand.Rig.PalmCenter.position;
        worldScale = Mathf.Max(hand.WorldScale, 1e-4f);
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c == null)
                continue;
            // Reach from the chip's OWN live world width — the fix for "same build, different
            // board scale, different behaviour". Held / clipped chips are rejected inside
            // Score via IFanSweepTarget.SweepEligible.
            FanReach reach = FanSweep.ResolveReach(worldScale, ((IFanSweepTarget)c).SweepFaceWidthWorld);
            FanSweep.Score(c, tip, palm, reach, incumbent, tipFirst: true, ref pick);
        }
        return pick;
    }

    /// <summary>The DOMINANT hand's sweep winner (null when that hand lifts nothing) — the pivot
    /// preference for the arc split and for the mirrored highlight index.</summary>
    private ItemChip? DominantWinner()
    {
        VRHand? dom = VRHands.Primary;
        if (dom == null)
            return null;
        return ReferenceEquals(dom, VRHands.Left) ? _handWinnerLeft : _handWinnerRight;
    }

    /// <summary>This hand's own sweep winner (null = it lifts nothing / it is neither hand).</summary>
    private ItemChip? WinnerFor(VRHand? hand)
    {
        if (hand == null)
            return null;
        if (ReferenceEquals(hand, VRHands.Left))
            return _handWinnerLeft;
        return ReferenceEquals(hand, VRHands.Right) ? _handWinnerRight : null;
    }

    /// <summary>Drop BOTH sweep winners without touching the chips — for the teardown paths that
    /// have already destroyed (or handed off) the chip objects, where clearing the pop on them would
    /// dereference a dead MonoBehaviour.</summary>
    private void ForgetSweepWinners()
    {
        _handWinnerLeft = null;
        _handWinnerRight = null;
        _handWinnerIndex = -1;
    }

    /// <summary>Drop ONE chip from whichever hand(s) elected it (it is leaving the arc — used,
    /// surrendered, collapsing). Untouched otherwise; the next sweep re-derives everything.</summary>
    private void ForgetSweepWinner(ItemChip chip)
    {
        if (ReferenceEquals(_handWinnerLeft, chip))
            _handWinnerLeft = null;
        if (ReferenceEquals(_handWinnerRight, chip))
            _handWinnerRight = null;
    }

    private void ClearHandSweep()
    {
        _handWinnerLeft?.SetFingertipPop(false);
        _handWinnerRight?.SetFingertipPop(false);
        _handWinnerLeft = null;
        _handWinnerRight = null;
        _handWinnerIndex = -1; // the split closes with the lift
        for (int i = 0; i < _handSuppressed.Count; i++)
        {
            if (_handSuppressed[i] != null)
                _handSuppressed[i].ClearHandSuppressed();
        }
        _handSuppressed.Clear();
    }

    /// <summary>
    /// The chip THIS hand is physically in contact with, or null — the item counterpart of
    /// <c>CardsDriver.HandOwnedFanCard</c>. It is the hand-sweep winner when this very hand
    /// elected it, else the hand's own proximity-grab candidate (which is what its trigger would
    /// actually take).
    ///
    /// WHY the laser consults this (user report 2026-08-02, "what lights up is not what I get"):
    /// while a hand reaches INTO the fan its laser is usually on the fan too, and the laser path
    /// sets <c>Ray.UiHitOverride</c> — which makes <c>ProximityGrabber</c> defer the trigger to
    /// the laser. The pop the player sees then comes from the hand sweep while the grab comes
    /// from the beam, and on an arc of overlapping chips those are routinely different cards.
    /// <c>CardsDriver.UpdateItemFanLaser</c> therefore yields hover AND trigger to the hand
    /// whenever this is non-null and disagrees with the ray chip — the same single-owner
    /// contract the ability fan enforces in <c>UpdateFanHoverSplit</c>.
    /// </summary>
    /// <summary>
    /// Index of the chip currently singled out in this fan, or -1 — the ITEM-fan counterpart of
    /// <c>PileBrowser.HighlightedIndex</c> and <c>CardFan.HighlightedIndex</c>.
    ///
    /// <para>MULTIPLAYER (user report 2026-08-03: "Die Highlights der Karten vom Fächer werden
    /// nicht synchronisiert bei den Piles z.B. Gegenstände/Item-Fächer"). The wire carries a bare
    /// fan POSITION, never a card identity (extension record 6), and the sender used to read that
    /// position from the pile BROWSER only — so a player sweeping their item fan lifted a chip
    /// that no peer ever saw move. This is the missing source; at most one board fan is open at a
    /// time, so it feeds the very same wire field.</para>
    ///
    /// <para>LASER PARITY (user report 2026-08-07: "Beim Hovern mit dem Laser über eine
    /// Pile/Item-Karte wird das Highlight nicht synchronisiert; mit der Hand schon"). This used to
    /// return <c>_handWinnerIndex</c> and nothing else, i.e. the HAND SWEEP only. The laser hover
    /// path (<c>CardsDriver.UpdateItemFanLaser</c> → <c>ItemChip.OnPokeEnter</c>) sets a SEPARATE
    /// flag that <c>ItemChip.Tick</c> pops on but that nothing here could read — so a beam hover
    /// lifted a chip locally and reached no peer. It now scans for
    /// <see cref="ItemChip.IsHighlighted"/>, which covers BOTH pop flags — the exact shape
    /// <c>PileBrowser.HighlightedIndex</c> / <c>CardFan.HighlightedIndex</c> already use through
    /// <c>VRCard.IsHighlighted</c>, which is why those two synced on the laser from the start.
    /// NO WIRE CHANGE: this is still the same bare fan POSITION on extension record 6, and
    /// <c>NetAvatarDriver</c> still dedups it against the last value it sent.</para>
    ///
    /// <para>DEDUP / SINGLE OWNER: the hand sweep wins whenever it has a winner. That is the same
    /// arbitration <c>CardsDriver.UpdateItemFanLaser</c> already applies locally (it yields hover
    /// AND trigger to the hand whenever <see cref="HandOwnedChip"/> disagrees with the ray chip),
    /// so the transmitted index is by construction the ONE chip the owner sees popped — hand and
    /// laser can never contribute two different indices, and a hover held across a hand→laser
    /// handover emits no packet at all while the chip is unchanged.</para>
    ///
    /// <para>TWO HANDS, ONE WIRE FIELD. Since the sweep runs a per-hand election (see
    /// <see cref="UpdateHandSweep"/>) the owner can lift TWO chips at once, one per hand, while the
    /// wire carries a single fan POSITION and must not grow a second one. The tie-break is the one
    /// the arc split already uses, so it is not a second rule: the DOMINANT hand's winner is what
    /// travels (<see cref="_handWinnerIndex"/> is written from it), falling back to the other hand's
    /// when the dominant hand lifts nothing. Deterministic, so a peer never sees the highlight
    /// flicker between two chips, and a one-handed sweep — either hand — is byte-identical to
    /// before.</para>
    ///
    /// <para>THE CARD LYING IN THE USE RECESS TRAVELS ON THIS FIELD TOO (user report 2026-08-09,
    /// "auch nach oben hinweg gehighlighted"). Its lift is an ANIMATION of a card lying on the
    /// control board, so the standing 1:1 ruling covers it — and the mod has already answered
    /// exactly this question for both fans: a highlighted card's POSITION is mirrored, the haptic is
    /// not (see <see cref="UpdateHandSweep"/>'s haptic note). Following that same line needs NO wire
    /// change and no new dial: the clipped chip has a perfectly good ARC INDEX — it is the very
    /// number record 26 already sends (<see cref="ClippedChipIndex"/>) — so naming it here makes the
    /// existing bare position on record 6 mean "the owner is singling this one out", whether it is
    /// standing in the arc or lying in the recess. <c>Net.RemoteItemFan</c> recognises the case by
    /// comparing it against the clip index it already holds, lifts that slab in ITS recess frame and
    /// splits nothing (the owner's arc does not split around it either — the clipped chip is not a
    /// sweep winner and never becomes the layout pivot).</para>
    ///
    /// <para>LOWEST PRECEDENCE, so nothing that already travels changes: the hand sweep's pivot
    /// first, then an arc chip's own combined pop, and only then the recess card. The three are
    /// mutually exclusive in practice anyway — the sweep cannot elect a clipped chip
    /// (<c>SweepEligible</c>) and a clipped chip answers false to <c>IsHighlighted</c> — so the
    /// ordering only decides the two-handed case where one hand sweeps the arc while the other
    /// reaches into the berth, and there the ARC keeps the wire exactly as it does today.</para>
    /// </summary>
    internal int HighlightedIndex
    {
        get
        {
            if (!IsOpen)
                return -1;
            if (_handWinnerIndex >= 0 && _handWinnerIndex < _chips.Count)
                return _handWinnerIndex;
            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c != null && c.IsHighlighted)
                    return i;
            }
            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c != null && c.RecessHighlighted && c.Holder == null)
                    return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Arc index of the chip currently CLIPPED INTO the board's item-USE recess, or -1 — the
    /// multiplayer read seam for extension record 26
    /// (<see cref="Net.NetProtocol.ExtIdItemUseClip"/>).
    ///
    /// <para>WHY IT EXISTS (2026-08-09). The wire said only HOW MANY item cards are in this fan and
    /// that the recess is VISIBLE, so a card the owner had laid INTO the recess was still drawn out
    /// in the arc on every other machine while their mirrored recess stood empty. The standing
    /// multiplayer ruling of 2026-08-08 covers "alle Interaktionen, Animationen und Anzeigen des
    /// Controllboards", and a card lying in a recess is exactly such an Anzeige. What travels is
    /// this POSITION — never the item; the receiver resolves what to draw from the same replicated
    /// <c>Inventory.AllItems</c> it already draws the arc from.</para>
    ///
    /// <para>ONE SEAM FOR ALL THREE CLIP-IN FLOWS, and deliberately not three: the ordinary USE
    /// placement (<see cref="_pendingUseChip"/>), the item-SURRENDER demand pick
    /// (<c>_demandChip</c>) and the take-damage shield place (<c>_tdChip</c>) all park a chip in the
    /// SAME recess and all set <see cref="ItemChip.PendingUse"/> on it. The rendered fact a peer
    /// needs is "this fan position lies in the recess", which is identical in the three cases, so
    /// the scan reads that flag rather than the three fields — a flow the fields miss can never
    /// produce a chip in the recess that the wire denies.</para>
    ///
    /// <para>A chip that has been GRABBED BACK OUT is excluded: its holder owns the pose for that
    /// frame (the flag is only cleared one tick later, in <c>TickPendingUse</c>), and reporting it
    /// as "in the recess" would pin a peer's copy to a recess the card has physically left. The
    /// index is into <see cref="Chips"/>, i.e. into exactly the ordered fan whose COUNT the extras
    /// packet already carries.</para>
    /// </summary>
    internal int ClippedChipIndex
    {
        get
        {
            // THE CARD OUTLIVES THE FAN (2026-08-09, see _keptClip). A closed arc used to answer −1
            // flatly, so the moment the owner clicked their fan away every peer's copy of the card
            // left their mirrored recess — the receiver-side half of exactly the defect the local
            // survivor fixes. While the arc is down the survivor still reports the arc POSITION it
            // held, which is the slab a peer already has parented to their recess; the index stays
            // the same object on both machines across the close.
            if (!IsOpen)
                return _keptClip != null && _keptClip.Holder == null ? _keptClipIndex : -1;
            for (int i = 0; i < _chips.Count; i++)
            {
                ItemChip c = _chips[i];
                if (c != null && c.PendingUse && c.Holder == null)
                    return i;
            }
            return -1;
        }
    }

    /// <summary>True while a card is LYING IN the board's item-use recess with the arc CLOSED (see
    /// <see cref="_keptClip"/>) — the seam the interaction drivers ask so a hand reaching for that
    /// card still owns its own trigger even though there is no fan open any more.</summary>
    internal bool HasPlacedCardWhileClosed => !IsOpen && _keptClip != null;

    /// <summary>
    /// Give up a card still lying in the recess because the BOARD is going away underneath it — the
    /// pile stacks being hidden, no presented hand at all, the viewer tearing down. It is not a
    /// player decision, so it goes home the same animated way every other cancel does (and the game
    /// side is backed out through its own seam); it simply must not be left lying on furniture that
    /// is about to stop being rendered or ticked. No-op when nothing is placed.
    /// </summary>
    internal void RetirePlacedCardIfAny(string why)
    {
        if (_keptClip != null)
            CancelPlacedCard(_keptClip, why);
    }

    internal ItemChip? HandOwnedChip(VRHand? hand)
    {
        // …or while a card is still lying in the recess with the fan closed: it is grabbable exactly
        // like a chip in the arc (user requirement 2026-08-09), so it must be able to own a hand's
        // trigger and stand that hand's laser down, both of which resolve through this method.
        if (hand == null || (!IsOpen && _keptClip == null))
            return null;
        // THE CARD LYING IN THE USE RECESS OUTRANKS THE ARC ELECTION (user report 2026-08-09: a
        // placed card still could not be taken back into the hand). A clipped chip is deliberately
        // NOT sweep-eligible — it is not at an arc position, so an arc election has no business
        // ranking it (IFanSweepTarget.SweepEligible) — which means it can only ever reach this method
        // through the ProximityGrabber highlight below. But the arc sweep can still have a winner for
        // this hand at the same time (a chip whose 3,5 cm fingertip reach the hand is grazing on its
        // way down to the board), and that winner used to be returned first: the trigger then took a
        // chip out of the FAN while the player was visibly reaching into the recess.
        //
        // The grabber's highlight is the single nearest-by-PALM grabbable this hand has, so when it
        // IS the clipped chip, no arc chip is nearer — the recess card is unambiguously what a grab
        // would take, and therefore what must own the hover, the laser yield and the trigger. Only
        // this one case jumps the queue; everything else keeps the arc election's answer.
        if (hand.Grabber.Highlighted is ItemChip placed && placed != null && placed.PendingUse
            && placed.Holder == null && ReferenceEquals(placed.Owner, this))
            return placed;

        // THIS hand's own election result (see UpdateHandSweep's two-elections note) — not "the
        // global winner if this hand happened to be the one that elected it", which reported
        // nothing at all for whichever hand lost the old single-slot race.
        ItemChip? mine = WinnerFor(hand);
        if (mine != null)
            return mine;
        return hand.Grabber.Highlighted as ItemChip;
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Geometric ray hit-test over the item fan — the item counterpart of
    /// <see cref="PileBrowser.TryRaycast"/>, the same algorithm verbatim (per-chip plane + rect off
    /// the LIVE transform, nearest hit along the ray, sticky-hover hysteresis so overlap never
    /// flips the highlight), with the rect sized to each chip's ACTUAL near-square face
    /// (<see cref="ItemChip.FaceWidth"/>/<see cref="ItemChip.FaceHeight"/> — item cards are not
    /// the tall ability rect). Driven by <c>CardsDriver.UpdateItemFanLaser</c>. No allocations.
    ///
    /// ROOT CAUSE this replaces (user: sweeping the laser RIGHT→LEFT popped only every SECOND
    /// card, while left→right stepped through every card): the chips used to be the ONLY fan
    /// cards whose laser hover came from the generic board-element PHYSICS scan
    /// (PlayTray.LaserTargets → nearest <c>Collider.Raycast</c> over the LIVE BoxColliders,
    /// re-elected from scratch every frame, no hysteresis). But the hover POP is part of that
    /// collider: the popped chip lifts 2 cm toward the viewer and grows ×1.18, so the pick
    /// geometry moved the instant the beam landed — the exact feedback class
    /// <see cref="VRCard.TryGetRestingLaserRect"/> documents for the ability fan ("the raise
    /// moved the plane INTO the beam"). At this fan's spacing (63.5 mm near-square faces at
    /// ×1.25 chip scale on a 10°-step arc ≈ 47.5 mm centers, 86.8 mm collider spans) the
    /// un-popped corridor between a popped chip's ENLARGED collider edge (±51.2 mm) and the
    /// next-but-one chip's collider (51.6 mm out) is under a MILLIMETRE — so any hand-off that
    /// had to clear the popped collider landed two chips over, and the immediate neighbour's
    /// hover lived a frame at best. Why only ONE direction: a lifted collider shadows the beam
    /// only on the side facing AWAY from the beam origin — with the laser in the (dominant,
    /// right) hand the 2 cm lift parallax-extends the popped chip's pick footprint over its
    /// LEFT neighbour's corridor, while hand-offs to the RIGHT happen on the beam-origin side
    /// where there is no shadow; the z-stagger tiebreak (chip i+1 sits 4 mm nearer the viewer
    /// than chip i) then hands the shared exit region to the far chip. Hence: right→left skips
    /// every second card, left→right does not. A per-frame geometric pick with the sticky rule
    /// has none of these behaviours in either direction: the pop can only ever EXTEND the
    /// incumbent's own hover (harmless — the beam is on that card anyway), never occlude a
    /// neighbour, because each rect is tested independently and the hand-off is decided by the
    /// nearest rect actually under the ray.
    /// </summary>
    internal bool TryLaserRaycast(Vector3 origin, Vector3 direction, ItemChip? sticky,
        out ItemChip? chip, out Vector3 point, out float distance,
        bool allowNearMiss = false)
    {
        chip = null;
        point = default;
        distance = float.PositiveInfinity;
        LastLaserPick = FanSweep.FanLaserPick.None;

        // WHAT THE BEAM MAY SEE. With the arc UP that is the arc, unchanged — including the clipped
        // chip, which has always been scanned on purpose (see the loop's own note: the far laser puts
        // a placed card back). With the arc DOWN it is the ONE card still lying in the recess
        // (<see cref="_keptClip"/>), and that case is new.
        //
        // WHY IT HAD TO BE ADDED (user report 2026-08-09, "die Gegenstandskarte die auf dem Overlay
        // liegt … wenn man mit dem Laser drüberfährt"). The card outliving the fan is the state the
        // player spends most of the decision in — they lay it in, click the arc away and go on
        // playing — and in exactly that state this scan answered "no chips" because the ARC was
        // closed. So the beam passed straight through the card: no hover, no lift, and no
        // laser-click either, even though `ItemChip.OnPoke` has carried the "the far laser puts it
        // back" branch for it since ModBuild 92. The card is the same object with the same live
        // transform, the same face rect and the same board-anchored pose in both states; only the arc
        // it is no longer part of had gone away. No new rule is introduced here — the existing one
        // simply reaches the state it could not see.
        _laserScan.Clear();
        if (IsOpen && _root != null)
            _laserScan.AddRange(_chips);
        else if (_keptClip != null)
            _laserScan.Add(_keptClip);
        if (_laserScan.Count == 0)
            return false;

        // ANGULAR RESCUE bookkeeping (the "I cannot reliably laser-hover the chips" report). The
        // exact-rect test below is correct at the default board scale and a coin flip on a small
        // one: at board 0,32× a chip is 2,5 × 3,5 REAL cm, so at a ~50 cm aim distance it subtends
        // 1,4° × 2,0° half-angle — inside a hand-held controller's own aim jitter, while the same
        // fan on the 0,80× default board subtends 5,7° × 8,0° and never misses. Each chip is
        // therefore granted a minimum angular half-size (FanSweep.LaserMinHalfAngleDegrees) as a
        // SECOND pass: while the beam is genuinely on a chip nothing changes, and the fan OCCLUDER
        // (RayInteractor.ComputeFanOccluder) never passes allowNearMiss, so a rescued near-miss can
        // never begin hiding game UI behind the fan.
        ItemChip? rescue = null;
        Vector3 rescuePoint = default;
        float rescueDist = 0f, rescueOvershoot = 0f, rescuePad = 0f, rescueRatio = float.MaxValue;
        float rescueWidth = 0f;
        var miss = FanSweep.FanLaserPick.None;
        float missOvershoot = float.MaxValue;

        for (int i = 0; i < _laserScan.Count; i++)
        {
            ItemChip c = _laserScan[i];
            // Held chips ride the hand (never laser targets). A clipped (PendingUse) chip stays
            // pickable on purpose: the old collider path let the laser pull it back out of the
            // use slot, and that must keep working (its live transform sits at the slot pose).
            if (c == null || c.Holder != null || !c.gameObject.activeInHierarchy)
                continue;

            Transform t = c.transform;
            float denom = Vector3.Dot(direction, t.forward); // chips face the viewer with −Z
            // Numerical stability ONLY (see FanSweep.LaserMinFaceDenominator). The angular cone that
            // used to sit here rejected legitimate hits: a chip billboards to the HEAD while the
            // beam leaves the HAND, so an off-to-the-side controller meets the face steeply even
            // with the reticle dead centre on it. A crossing inside the finite face is a hit at any
            // incidence; the grazing-plane runaway is bounded below, in card widths, where it belongs.
            if (denom < FanSweep.LaserMinFaceDenominator)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (ChipScale + pop grow)
            // Accept margin (FanSweep.LaserAcceptMargin), the same ~10 % the ability fan has always
            // had: the exact rect plus a zero angular pad at reading distance left no tolerance at
            // all for controller jitter or the chip's own hover pop.
            float halfW = c.FaceWidth * 0.5f;
            float halfH = c.FaceHeight * 0.5f;
            float overX = Mathf.Abs(local.x) - halfW * FanSweep.LaserAcceptMargin;
            float overY = Mathf.Abs(local.y) - halfH * FanSweep.LaserAcceptMargin;
            float lossy = Mathf.Max(t.lossyScale.x, 1e-5f);
            if (overX > 0f || overY > 0f)
            {
                if (!allowNearMiss)
                    continue;
                float overshoot = (Mathf.Max(overX, 0f) + Mathf.Max(overY, 0f)) * lossy;
                // A crossing this far outside the face was never aimed at this chip — that is the
                // grazing-plane runaway, bounded where it actually is (FanSweep doc).
                if (overshoot > Mathf.Max(halfW, halfH) * lossy * FanSweep.LaserMaxMissOvershootFactor)
                    continue;
                float pad = FanSweep.LaserPad(dist, Mathf.Max(halfW, halfH) * lossy);
                if (overshoot < missOvershoot)
                {
                    missOvershoot = overshoot;
                    miss = new FanSweep.FanLaserPick
                    {
                        Hit = false, Rescued = false, Name = c.name, Distance = dist,
                        Overshoot = overshoot, Pad = pad, FaceWidthWorld = halfW * 2f * lossy,
                    };
                }
                if (pad <= 0f || overshoot > pad)
                    continue;
                float ratio = overshoot / pad;
                if (ReferenceEquals(c, sticky))
                    ratio *= 0.5f; // the incumbent keeps the beam through a graze (same hysteresis as below)
                if (ratio >= rescueRatio)
                    continue;
                rescue = c;
                rescuePoint = hit;
                rescueDist = dist;
                rescueOvershoot = overshoot;
                rescuePad = pad;
                rescueRatio = ratio;
                rescueWidth = halfW * 2f * lossy;
                continue;
            }

            if (ReferenceEquals(c, sticky))
            {
                // Current hover still under the ray — it wins outright (overlap hysteresis).
                chip = c;
                point = hit;
                distance = dist;
                LastLaserPick = new FanSweep.FanLaserPick
                {
                    Hit = true, Rescued = false, Name = c.name, Distance = dist,
                    Overshoot = 0f, Pad = 0f, FaceWidthWorld = halfW * 2f * lossy,
                };
                return true;
            }

            if (dist >= distance)
                continue;
            chip = c;
            point = hit;
            distance = dist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = false, Name = c.name, Distance = dist,
                Overshoot = 0f, Pad = 0f, FaceWidthWorld = halfW * 2f * lossy,
            };
        }

        if (chip == null && rescue != null)
        {
            chip = rescue;
            point = rescuePoint;
            distance = rescueDist;
            LastLaserPick = new FanSweep.FanLaserPick
            {
                Hit = true, Rescued = true, Name = rescue.name, Distance = rescueDist,
                Overshoot = rescueOvershoot, Pad = rescuePad, FaceWidthWorld = rescueWidth,
            };
        }
        else if (chip == null)
        {
            LastLaserPick = miss;
        }
        return chip != null;
    }

    /// <summary>
    /// What the last <see cref="TryLaserRaycast"/> decided — hit or miss, which chip, dead-on or
    /// rescued by the angular pad, and how far outside the face the ray crossed. Read by
    /// <c>CardsDriver.UpdateItemFanLaser</c> for the throttled laser diagnostic; the pick itself
    /// runs per frame per hand and must stay silent.
    /// </summary>
    internal FanSweep.FanLaserPick LastLaserPick { get; private set; } = FanSweep.FanLaserPick.None;

    // ------------------------------------------------------------------ use slot --

    /// <summary>
    /// The single held chip that can actually be USED right now (requirement 3): non-passive AND
    /// in a Useable/Selected slot state — the EXACT predicate <c>UseItemService.UseItem</c>
    /// enforces (evaluated LIVE via <see cref="ItemChip.IsActivatable"/>), so the slot never
    /// appears for an item the service would reject. Element-CHOICE items are NO LONGER excluded
    /// (user ruling 2026-08-08 — every item is played by placing its card): they are placed like
    /// any other card and answer their element choice afterwards in the decision area. They are
    /// excluded only when the game has built no bar slot for them at all
    /// (<see cref="CanPlaceChoiceItem"/>) — without that slot there is no picker to raise, so
    /// promising the recess would be a dead end.
    ///
    /// <para>ACTIVE-BONUS ITEMS ARE ADMITTED TOO (2026-08-09, the Brille). Such an item is PASSIVE
    /// and would fail <see cref="ItemChip.IsActivatable"/> for ever — that is precisely the fact the
    /// last round mistook for "it must keep its button". It is offered here on the second seam
    /// instead (<see cref="ItemChip.HasOfferedBonus"/>), and the element-choice gate is deliberately
    /// NOT applied to it: the bonus flow never touches <c>UIUseItemsBar</c>, so the absence of an
    /// items-bar slot — which is guaranteed for a passive item — says nothing about it. Its own
    /// "no further option" filter already ran when the bonus was classified as placeable.</para>
    /// </summary>
    private ItemChip? HeldActivatableChip()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c == null || c.Holder == null)
                continue;
            if (c.HasOfferedBonus)
                return c;
            if (c.IsActivatable && CanPlaceChoiceItem(c.Item))
                return c;
        }
        return null;
    }

    /// <summary>
    /// Placement gate for the element-CHOICE half: such an item can only be placed while the game
    /// HAS a bar slot for it (active or mod-suppressed — <see cref="CardsGameApi.ItemsBarSlot"/>
    /// with <c>requireActive:false</c>), because the whole choice UI (the element picker, the
    /// element holders, the pending-confirm state) is serialized ON that slot prefab. Plain items
    /// never need one (they fall back to <c>UseItemService</c> directly), so they always pass.
    /// </summary>
    private bool CanPlaceChoiceItem(CItem? item)
    {
        if (item == null)
            return false;
        if (!CardsGameApi.ItemNeedsSubChoice(item, _hand))
            return true;
        return CardsGameApi.ItemsBarSlot(item, requireActive: false) != null;
    }

    /// <summary>
    /// USABLE-HIGHLIGHT truth source — can this chip's item be USED right now: it is the action turn
    /// OF THE CHARACTER THIS FAN BELONGS TO (<see cref="_hand"/> — the PRESENTED character since the
    /// 2026-08-09 focus fix, see that field) AND the item's live state is activatable (non-passive +
    /// Useable/Selected — the exact gate <c>UseItemService.UseItem</c> enforces). Owner-driven
    /// (turn-aware), so a merely-watched character's cards never light up and the acting character's
    /// playable ones do — the per-card twin of <see cref="UsableCount"/>'s stack cue.
    /// Polled live from each chip's per-frame face maintenance; never cached — usability moves with
    /// turn/phase and with every item the player spends.
    ///
    /// WHY the highlight (not a dim) hangs off THIS predicate: the game logic stays untouched; the
    /// visual is a pure read of it. Flipping the cue from "grey out the unusable" to "light up the
    /// usable" is a change of which side of this bool draws a quad, nothing else.
    ///
    /// <para>SECOND ARM (2026-08-09, the user's first sentence about the Brille: "dann soll die
    /// Brille im Gegenstands-Pile gehighlighted werden"): an item whose ACTIVE BONUS is being
    /// offered right now lights up too. It is turn-INDEPENDENT on purpose — the offer itself is the
    /// game's own statement that the question is live, and several of the windows in which it is
    /// live are not the owner's action turn (a take-damage decision runs on the enemy's turn). It
    /// also does not go through <see cref="ItemChip.IsActivatable"/>, which such an item can never
    /// satisfy: it is passive, and passive is exactly why it has a bonus row instead of a bar slot.</para>
    /// </summary>
    internal bool CanUseNow(ItemChip chip) =>
        chip != null && _hand != null
        && ((CardsGameApi.IsActionTurn(_hand) && chip.IsActivatable) || chip.HasOfferedBonus);

    /// <summary>
    /// The SINGLE live activatability predicate, shared by the chips (<see cref="ItemChip.IsActivatable"/>)
    /// and by the fan-CLOSED stack highlight (<see cref="UsableCount"/>): non-passive AND in a
    /// Useable/Selected slot state — byte-for-byte the gate <c>UseItemService.UseItem</c> enforces, so a
    /// highlight can never promise a use the service would reject. Read-only on game data.
    ///
    /// <para>─── THE ITEM AUDIT (user question, 2026-08-09, verbatim): "Geh alle Karten so durch die
    /// im Spiel existieren — Gibt es überhaupt eine Itemkarte (die nicht nur einen passiven oder
    /// dauerhaften Effekt hat) und nicht durch ein physisches Hinlegen der Karten aktiviert werden
    /// können?" Answered here because THIS predicate is the fork the answer turns on.</para>
    ///
    /// <para>The per-item YML is not shippable with the mod and is not on the build machine — the
    /// item cards live in <c>&lt;GameDir&gt;/Gloomhaven_Data/StreamingAssets/Rulebase/*.ruleset</c>
    /// zip archives (GH.Runtime/YMLLoading.cs:140-172, :512-515), and <c>ressources/</c> holds only
    /// the Managed DLLs. So the audit is done over the game's own CODE, which is strictly stronger
    /// than a card list: it enumerates the KINDS an item can be, and every item that exists is one
    /// of them by construction (ItemCardYMLData.Validate, :99-147, rejects any item with
    /// <c>Trigger: None</c>).</para>
    ///
    /// <para>ALL EIGHT TRIGGERS (<c>CItem.EItemTrigger</c>, ScenarioRuleLibrary/CItem.cs:59-70 —
    /// and although it is <c>[Flags]</c>, the parser takes exactly ONE name per item,
    /// ItemCardYML.cs:191-209, so these are eight disjoint classes):
    /// <c>PassiveEffect</c>, <c>AtStartOfRound</c>, <c>DuringOwnTurn</c>, <c>SingleTarget</c>,
    /// <c>SingleAbility</c>, <c>EntireAction</c>, <c>OnAttacked</c>, <c>AtEndOfTurn</c>.</para>
    ///
    /// <para>THE ANSWER IS NO — with exactly one class of exception, and it is not a counter-example
    /// to the rule but the definition of it. <c>UseItemService.UseItem</c> (GH.Runtime, :29-38) is
    /// the ONE seam through which any item is ever activated, and it admits an item on precisely the
    /// two conditions this predicate reads: not <c>PassiveEffect</c>, and <c>SlotState</c> in
    /// {Useable, Selected}. The board's recess confirm calls that same seam. So for every one of the
    /// SEVEN non-passive triggers, placing the card IS a legal, complete activation — there is no
    /// item the placement flow structurally cannot activate. What some items additionally OWE is a
    /// further answer AFTER the item has been chosen, and there are exactly five such mechanisms:
    /// <list type="bullet">
    /// <item>the ELEMENT choice — <c>Consumes</c> containing <c>Any</c> (CInventory.cs:425 refuses
    /// activation until <c>ChosenElement</c> is filled). Already handled: the card is placed like
    /// any other and the picker opens in the decision area (ModBuild 92).</item>
    /// <item>the INFUSION pick — unselected <c>IsAnyElement</c> infusions
    /// (UIUseItemsBar.cs:85/:160-179). Same flow.</item>
    /// <item>the CHOICE among several effects — <c>Data.Abilities</c> containing a <c>Choose</c>
    /// ability (UIUseItemScenario.cs:161-169).</item>
    /// <item>the TARGET pick — <c>Trigger: SingleTarget</c>, answered into <c>CItem.SingleTarget</c>
    /// (CInventory.cs:487-490).</item>
    /// <item>the CARD pick — <c>RefreshItemCards</c>/<c>ConsumeItemCards</c>, an n-of-m pick over
    /// OTHER item cards (ItemCardRefreshPicker.cs:22-32); the mod already answers this one by
    /// placing cards too (<see cref="TickDemandPick"/>).</item>
    /// </list>
    /// None of those five is a "use this item" button. They are the follow-up question, and the
    /// decision area is exactly where they belong — which is the line the user drew.</para>
    ///
    /// <para>THE SECOND SEAM — and the answer this comment gave in ModBuild 94 was WRONG about it.
    /// An item with <c>Usage: Unrestricted</c>, <c>Trigger: PassiveEffect</c> and
    /// <c>UsedWhenEquipped != true</c> does not go through UseItemService at all. The game builds it
    /// a <c>CActiveBonus</c> off its item card (CActiveBonus.cs:395-400) and charges the item only
    /// when that bonus is USED (<c>CActiveBonus.ActiveBonusUsed</c>, :683-694 — that triple is the
    /// literal predicate there). Its toggle lives on <c>UIActiveBonusBar</c>, not on the items bar.
    /// The old note concluded from "UseItemService refuses passive items" that its BUTTON had to
    /// stay; the user rejected that and was right (2026-08-09: "Ich verstehe deine Begründung nicht
    /// warum der Knopf bleiben muss"). UseItemService refusing is a statement about ONE seam, not
    /// about what is possible: the bonus row's click is itself only
    /// <c>UIUseSlot.OnPointerDown → ToggleActiveBonus(…, fromClick: true)</c>, the bonus is built
    /// off the ITEM CARD, and both directions of the toggle are first-class in the game. So this
    /// class is placed too — see <see cref="ItemChip.HasOfferedBonus"/> and the ACTIVE-BONUS
    /// placement block near <see cref="_pendingBonus"/> — and it is the reason
    /// <see cref="CanUseNow"/> and <see cref="UsableCount"/> have a second arm that this predicate
    /// deliberately does NOT: this one stays the exact <c>UseItemService</c> gate and nothing else,
    /// because that is what the plain and element-choice confirms call.</para>
    ///
    /// <para>CONSEQUENCE for the use bars: NO item button survives in the decision area at all.
    /// PLAIN item slots are suppressed by <c>UseBarsSurface.EnforceItemsSplit</c>, CHOICE slots by
    /// <see cref="EnforceChoiceSlotSplit"/> (which keeps exactly the slot belonging to the card
    /// currently LYING in the recess), and item-backed option-less BONUS rows by
    /// <c>UseBarsSurface.EnforceActiveBonusSplit</c>. What remains in the decision area is what the
    /// user asked to remain: the further OPTIONS (initiative ±, forgo-which-ability,
    /// choose-ability, the element consume) and every bonus that has no card to place at all
    /// (auras, character abilities, summons). The mirror half — a bar left masked into wire record
    /// 25 with zero visible slots, which drew an empty caption plate on the peer's board — is fixed
    /// in <c>UseBarsSurface.SampleWire</c>.</para>
    /// </summary>
    private static bool IsItemActivatable(CItem? item) =>
        item != null && item.YMLData != null
        && item.YMLData.Trigger != CItem.EItemTrigger.PassiveEffect
        && (item.SlotState == CItem.EItemSlotState.Useable
            || item.SlotState == CItem.EItemSlotState.Selected);

    /// <summary>
    /// How many of <paramref name="hand"/>'s equipped items are PLAYABLE RIGHT NOW — the count that
    /// drives the items STACK highlight while the fan is CLOSED (there are no chips then, so it is
    /// read straight from the live inventory). Two disjoint arms, matching <see cref="CanUseNow"/>
    /// exactly so the stack and the fanned-out cards can never disagree:
    /// <list type="bullet">
    /// <item>the ordinary use — this hand's own action turn AND <see cref="IsItemActivatable"/>;</item>
    /// <item>an item whose ACTIVE BONUS is on offer right now (the Brille), which is NOT turn-gated:
    /// the game only builds the row while the question is answerable, and some of those windows are
    /// not the owner's turn at all.</item>
    /// </list>
    /// Cheap: one pass over a handful of items, per frame, allocation-free (the bonus lookup walks
    /// the bar's own small slot dictionary).
    ///
    /// <para>─── THIS IS ALSO, WORD FOR WORD, THE USER'S SECOND RULE (2026-08-09): "Weiterhin möchte
    /// ich das die Pile-Animation das etwas nutzbar ist NUR bei dem Character sichtbar ist der gerade
    /// am Zug ist, denn nur da ist aktuell wirklich gerade etwas nutzbar, bei den anderen ja nicht."
    /// NO SECOND GATE WAS ADDED FOR IT, deliberately — the rule the user gave is a consequence and
    /// its REASON is the rule. Now that <c>PileViewer.TickStatus</c> asks this question about the
    /// character the board is SHOWING, both arms already answer 0 for a character who has nothing to
    /// play: arm 1 needs that character's own action turn, and arm 2 needs the game to be holding a
    /// bonus row of that character's own (scoped by owner since this same pass — see
    /// <c>CardsGameApi.PlaceableBonusForItem</c>; before it, an ID collision with the acting
    /// character's copy of the same item WOULD have leaked the cue across actors, which is precisely
    /// the leak the user's rule is about). A hard "…&amp;&amp; IsActionTurn" wrapped round the whole
    /// thing would instead have deleted the Brille cue the user asked for two rounds earlier ("dann
    /// soll die Brille im Gegenstands-Pile gehighlighted werden"), because the game offers that
    /// question during an ENEMY's turn.</para>
    /// </summary>
    internal int UsableCount(CardsHandUI? hand)
    {
        if (hand == null)
            return 0;
        bool turn = CardsGameApi.IsActionTurn(hand);
        CPlayerActor? owner = hand.PlayerActor;
        List<CItem>? items = ItemsOf(hand);
        if (items == null)
            return 0;
        int n = 0;
        for (int i = 0; i < items.Count; i++)
            if ((turn && IsItemActivatable(items[i]))
                || CardsGameApi.PlaceableBonusForItem(items[i], owner) != null)
                n++;
        return n;
    }

    /// <summary>
    /// Requirement 8 — show a translucent GHOST duplicate at the use-slot pose while a HELD usable item
    /// card comes NEAR the slot, previewing where it will land if released (mirror of the ability cards'
    /// drop telegraph, which glows the destination slot). Built lazily under the slot so it rides the
    /// slot pose + visibility; toggled purely by proximity. No-op / hidden when nothing is held near it.
    /// </summary>
    private void TickUseGhost(bool showUseSlot, ItemChip? heldUsable)
    {
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        bool show = false;
        if (showUseSlot && heldUsable != null && slot != null && slot.gameObject.activeSelf)
        {
            float scale = slot.lossyScale.x;
            float near = UseSlotRadius * 2.2f * (scale > 1e-4f ? scale : 1f); // generous "approaching" band
            if ((heldUsable.transform.position - slot.position).sqrMagnitude <= near * near)
                show = true;
        }
        if (show)
        {
            if (_useGhost == null && slot != null)
                _useGhost = BuildUseGhost(slot);
            if (_useGhost != null && !_useGhost.activeSelf)
                _useGhost.SetActive(true);
            // THE GHOST IS THE PROMISE, SO IT MUST BE THE TRUTH (requirement 5a). It used to be
            // built once at the ABILITY card's tall w×h, while what actually lands is THIS chip's
            // near-square face fitted to the recess — so the preview and the settled card were two
            // different rectangles, and the preview was the honest-looking one. It is now sized from
            // the very call ItemChip.ClipIntoSlot settles to, so "where it will land" is literally
            // where it lands.
            if (_useGhost != null && heldUsable != null)
            {
                float fit = UseSlotFitScale(heldUsable);
                var want = new Vector3(heldUsable.FaceWidth * fit, heldUsable.FaceHeight * fit, 1f);
                if (_useGhost.transform.localScale != want)
                    _useGhost.transform.localScale = want;
            }
        }
        else if (_useGhost != null && _useGhost.activeSelf)
        {
            _useGhost.SetActive(false);
        }
    }

    /// <summary>Requirement 8 — the translucent gold card-shaped ghost, parented at the use-slot
    /// pose. Built at a placeholder size; <see cref="TickUseGhost"/> fits it to the HELD chip's own
    /// face every time it is shown (see the note there).</summary>
    private static GameObject BuildUseGhost(Transform slot)
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "GloomhavenVR.ItemUseGhost";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(slot, worldPositionStays: false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.004f); // viewer side, proud of the slot face
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(w, h, 1f);
        var mr = quad.GetComponent<MeshRenderer>();
        Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (sh != null)
            mr.sharedMaterial = new Material(sh) { color = new Color(0.92f, 0.85f, 0.5f, 0.34f) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Core.VRLayers.Apply(quad); // mod-owned ghost on the mod layer (double-sided Sprites/Default)
        return quad;
    }

    /// <summary>
    /// Requirement 6 (clip-in + decide): a just-released usable chip dropped onto/near the board's
    /// item-use slot does NOT use immediately — it CLIPS INTO the slot and waits. A poke/laser on the
    /// Confirm button uses it (<see cref="ConfirmPendingUse"/>); grabbing the card BACK OUT and
    /// releasing it returns it to the deck, no use. Dropped anywhere else (or a non-activatable chip)
    /// is a no-op — the base glide-home already runs. Inventory is never mutated here.
    /// </summary>
    internal void OnChipReleased(ItemChip chip, Vector3 dropWorldPos, VRHand vrHand)
    {
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (chip == null)
            return;
        if (slot == null || !slot.gameObject.activeSelf)
        {
            AfterRefusedDrop(chip);
            return;
        }

        // ─── THE ACTION IS STILL RESOLVING: A RELEASE IS A RETURN, NEVER A CANCEL ────────────────
        //
        // User requirement 2026-08-09, third sentence: "Er soll in diesem Zustand zwar ganz normal in
        // die Hand genommen werden können aber beim Loslassen geht er wieder zurück an das Overlay."
        //
        // Tested FIRST, before every other drop route, and deliberately without the proximity test
        // that decides the ordinary placement. While the item's action is resolving the recess is
        // simply WHERE THIS CARD LIVES — it is not a candidate for a placement decision any more,
        // because the decision has already been made and pressed. So "where you let go decides", the
        // rule that governs an un-confirmed placement, does not apply here: there is nothing left to
        // cancel and nowhere else for the card to be. It flies back to the recess from wherever the
        // hand let go, on the same animated settle a first placement uses.
        //
        // It cannot collide with the three other placement flows below: a resolving card is by
        // construction the _pendingUseChip and can be neither _demandChip nor _tdChip, and its item
        // is mid-use, so no active bonus is being offered off it either.
        if (_useResolving && ReferenceEquals(chip, _pendingUseChip))
        {
            ReturnResolvingCardToRecess(chip, slot, vrHand);
            return;
        }

        // ITEM SURRENDER pick (event consume/refresh mali): while the game's ItemCardPicker is
        // open, a drop into the slot SELECTS the item through the picker's own slot seam —
        // never UseItemService (nothing is consumed until the demand confirm commits). The
        // IsActivatable gate below is the ACTION-TURN use gate and deliberately does not apply:
        // the picker's own candidate filter (cardSlots) is the authority here.
        if (_demandActive)
        {
            HandleDemandDrop(chip, dropWorldPos, slot, vrHand);
            return;
        }

        // ACTIVE-BONUS place (the Brille, 2026-08-09) — tested BEFORE the take-damage branch and
        // regardless of the action turn. An item-backed bonus is answered on UIActiveBonusBar, not
        // on UIUseItemsBar, so the two flows address disjoint items (a bonus-backed item is
        // PassiveEffect; an OnAttacked shield item is not), and the bonus can legitimately be
        // offered while a take-damage decision is up — that is when a worn prevent-damage item asks
        // its question. Routing it here means the _tdActive branch below never sees a card it has no
        // items-bar slot for and would silently bounce.
        // Owner-scoped (see PlaceableBonusForItem): this is a DROP ROUTER, so an unscoped by-id match
        // would let the watched character's card toggle the ACTING character's bonus in
        // ConfirmPendingBonus — the "an action path reading the focus" direction, and the one real
        // hazard the item pile's move to the presented hand opened.
        CActiveBonus? offered = chip.Item != null
            ? CardsGameApi.PlaceableBonusForItem(chip.Item, OwnerActor) : null;
        if (offered != null)
        {
            HandleBonusDrop(chip, dropWorldPos, slot, vrHand, offered);
            return;
        }

        // TAKE-DAMAGE shield place (req C): while the damage decision presents OnAttacked
        // candidates on the (hidden) items bar, a drop TOGGLES the shield item through the
        // panel's own slot seam — never UseItemService (the panel's confirm commits + syncs).
        if (_tdActive)
        {
            HandleTakeDamageDrop(chip, dropWorldPos, slot, vrHand);
            return;
        }

        if (!chip.IsActivatable)
        {
            AfterRefusedDrop(chip);
            return;
        }
        // Element-CHOICE items are placed like every other card (2026-08-08). The ONE case that
        // still bounces is "the choice UI does not exist": no bar slot ⇒ no picker to raise ⇒ the
        // placement could never be answered. Mirrors HeldActivatableChip's gate, so the recess was
        // not shown for this chip anyway; this is the belt-and-braces on the drop itself.
        if (!CanPlaceChoiceItem(chip.Item))
        {
            VRLog.Info("Cards", $"ITEM place: '{chip.name}' needs an element choice but the game has built " +
                                "no items-bar slot for it (bar not shown / item filtered out) — there is no " +
                                "picker to raise, so the card returns to the fan.");
            AfterRefusedDrop(chip);
            return;
        }

        // Proximity test in world space (parenting-independent): the capture radius scales with
        // the board so the slot stays the same on-screen size at any board scale.
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
        {
            // Dropped AWAY from the recess. With the fan open the base glide-home has already been
            // started by ItemChip.OnRelease and returns it to the arc; with the fan closed this is
            // the user's CANCEL and AfterRefusedDrop flies it into the items stack.
            AfterRefusedDrop(chip);
            return;
        }

        // Only one pending decision at a time (a fresh drop replaces an older pending clip cleanly).
        if (_pendingUseChip != null && !ReferenceEquals(_pendingUseChip, chip))
            _pendingUseChip.ReturnToFan();

        _pendingUseChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide(); // do NOT glide home — we clip into the slot instead
        // Bolt it to the slot (see ClipIntoSlot): rigid by hierarchy, not chased by a lerp — the
        // fan root billboards to the head, the slot does not, and chasing across that boundary is
        // what made the clipped card swim behind head movement.
        chip.ClipIntoSlot(slot);
        PlayTray.Current?.SetItemUseSlotVisible(true);

        // Does the placed card owe an element choice? Classify ONCE, here, from the item data —
        // the live slot's own predicate is only valid after the slot has been built and fed.
        _pendingSubChoice = CardsGameApi.ItemNeedsSubChoice(chip.Item!, _hand);
        _pendingChoiceSlot = _pendingSubChoice
            ? CardsGameApi.ItemsBarSlot(chip.Item!, requireActive: false) : null;
        _choiceClickArmed = false;
        _choiceCapReady = false;
        _choiceCapShown = false;
        if (_pendingSubChoice)
        {
            // The cap says CHOOSE, not USE: nothing is confirmable until the element is picked.
            // Poking it re-raises the picker (the player may close it and come back).
            ShowChoiceCap(ready: false);
            // Re-activate this ONE slot; the next tick fires its click, which opens the element
            // picker. The items bar then docks under the decision row (UseBarsSurface) — that is
            // where the element choice appears, exactly like every other game decision.
            _choiceClickArmed = true;
        }
        else
        {
            PlayTray.Current?.SetItemUseConfirmVisible(true, ConfirmPendingUse);
        }

        vrHand.SendHaptic(HapticPreset.HoverTick);
        // ITEM 4 (card sounds): the place "thunk" the ability cards play when a card drops into a slot.
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM clip-in: '{chip.name}' held in the use slot ({vrHand.Side}) — " +
                            (_pendingSubChoice
                                ? "it owes an ELEMENT choice; raising the game's picker in the decision "
                                  + "area under the board. Pick, then poke USE — or grab it back out to cancel."
                                : "poke USE to confirm, or grab it back out to cancel."));
    }

    /// <summary>
    /// A released chip that did NOT clip into the recess — every "not a placement" exit of
    /// <see cref="OnChipReleased"/> lands here.
    ///
    /// <para>While the fan is OPEN this is a no-op on purpose: <c>ItemChip.OnRelease</c> has already
    /// started the animated glide back to the chip's own arc slot, and the arc IS the item pile
    /// fanned out.</para>
    ///
    /// <para>While the fan is CLOSED there is no arc to glide to, and this is the user's CANCEL
    /// (2026-08-09: "Wenn man eine aufgehobene Gegenstandskarte loslässt soll sie wieder ganz normal
    /// zurück in den item-pile und das overlay verschwinden — das ist sozusagen der Abbruch ein Item
    /// benutzen zu wollen."). The card was taken out of the recess and let go somewhere that is not
    /// the recess, so the pending use is backed out through its own game seam and the card folds
    /// into the items stack with the closing fan's animation. WHERE YOU LET GO STILL DECIDES — a
    /// release back over the recess never reaches this method, it re-clips above.</para>
    /// </summary>
    private void AfterRefusedDrop(ItemChip chip)
    {
        if (chip == null || IsOpen)
            return;
        CancelPlacedCard(chip, "taken out of the recess and released away from it — the CANCEL");
    }

    // ================= the card STAYS while the item's action resolves (2026-08-09) =============

    /// <summary>
    /// The USE has been pressed and accepted — hand the card over to the RESOLVING state instead of
    /// sending it home. Shared by every "the item has now been used" entry: the USE cap's confirm,
    /// the element pick that auto-uses its own card, and the state-change edge that notices the game
    /// charged the item under us.
    ///
    /// <para>WHAT CHANGES AND WHAT DOES NOT. The DECISION ends here — the USE/CHOOSE cap comes down
    /// and the element-choice bookkeeping is dropped, because there is nothing left to confirm or to
    /// back out. The PLACEMENT does not: <see cref="_pendingUseChip"/> stays this chip and
    /// <c>PendingUse</c> stays set, so the card keeps its identity as "the card lying in the recess"
    /// for the fan-close survivor (<see cref="_keptClip"/>), for the grab/laser routing, for
    /// <see cref="RefreshFanLayout"/> (which skips it) and for wire record 26 — a peer's copy stays
    /// in their mirrored recess for exactly the same window, off the index it already receives.</para>
    ///
    /// <para>The predicate is asked IMMEDIATELY, in this same call: an item that resolves inside its
    /// own SRL message (a shield, an unrestricted trinket) is already finished by the time a slower
    /// path would look, and it must keep the same instant burn/tap flourish it has always had.</para>
    /// </summary>
    private void BeginUseResolving(ItemChip chip, CItem item, string why)
    {
        _pendingUseChip = chip;
        _useResolving = true;
        _pendingSubChoice = false;
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        chip.PendingUse = true;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        VRLog.Info("Cards", $"ITEM USED '{chip.name}' ({why}) — the card STAYS LYING in the recess while the " +
                            "game resolves what the use started (target confirmation, a follow-up prompt, an " +
                            "element choice). It can still be taken into either hand; releasing it puts it " +
                            "back in the recess. It travels home the moment the action is finished.");
        TickUseResolving(chip); // may finish this very frame — see the note above
    }

    /// <summary>
    /// Per-tick service of a card whose USE has been confirmed and whose ACTION the game is still
    /// resolving. One question per frame — <see cref="CardsGameApi.ItemActionResolving"/>, which
    /// states its own terms and its own evidence — and no state of our own is consulted, so this
    /// converges on every ending by construction: the action completes, the player undoes it, the
    /// scenario tears the phase down, the turn is taken away. There is no ledger to go stale, no
    /// latch to get stuck and no timer to expire early.
    ///
    /// <para>A HAND THAT HOLDS THE CARD OWNS IT, and this method may not move it — the same rule
    /// <see cref="TickPlacedWhileClosed"/> already applies to a placed card. So a finish that comes
    /// due while the player is holding the card is DEFERRED to the release (which re-clips it into
    /// the recess, <see cref="ReturnResolvingCardToRecess"/>, and the next tick then sends it home
    /// with its flourish). That is not a stall: a card in a hand is not a card stranded on the
    /// board, and the release is one trigger-up away.</para>
    /// </summary>
    private void TickUseResolving(ItemChip chip)
    {
        CItem? item = chip.Item;
        if (item == null)
        {
            // Nothing left to ask about — send it home the ordinary animated way.
            _useResolving = false;
            _pendingUseChip = null;
            ReturnPlacedChipHome(chip, "the used card lost its item under us");
            return;
        }

        if (chip.Holder != null)
        {
            PlayTray.Current?.SetItemUseSlotVisible(true); // it is coming back here — keep the target lit
            return;
        }

        if (!CardsGameApi.ItemActionResolving(item))
        {
            FinishUsedChip(chip, item, "the item's action finished resolving");
            return;
        }

        // Still resolving: hold the recess up and keep the card seated in it. Same re-seat rule as
        // the pending placement — the hierarchy holds the card for free once it has landed, so this
        // only ever fires if a board rebuild swapped the slot transform under it.
        PlayTray.Current?.SetItemUseSlotVisible(true);
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (slot == null)
        {
            // The recess went away with a board rebuild: there is nothing left to lie on, so the card
            // goes home NOW rather than hanging in space. It was genuinely used, so it goes home the
            // used way — with its burn/tap flourish, not as a cancel.
            FinishUsedChip(chip, item, "the board's item-use recess was rebuilt away mid-resolution");
            return;
        }
        if (chip.transform.parent != slot)
            chip.ClipIntoSlot(slot); // re-seat visibly (the settle), never a teleport
    }

    /// <summary>Throttle for the "the beam cannot take a used card off the recess" line.</summary>
    private float _resolveRefuseLogAt;

    /// <summary>
    /// The far laser asked for a card whose USE is already confirmed to be put back on the pile.
    /// There is no such move: the card is lying there because the game is still resolving, and it
    /// leaves on its own. Say so, and spring it visibly back into the recess if a hand had it — the
    /// same shape of answer <see cref="RefuseLockedBonusRemoval"/> gives, for the same reason (a
    /// refusal the player can see beats a gesture that does nothing).
    /// </summary>
    private void RefuseResolvingRemoval(ItemChip chip)
    {
        Transform? useSlot = PlayTray.Current?.ItemUseSlotTransform;
        if (useSlot != null && chip.Holder == null)
        {
            chip.CancelReleaseGlide();
            chip.PendingUse = true;
            chip.ClipIntoSlot(useSlot); // the visible spring-back (the settle animation, not a pop)
        }
        PlayTray.Current?.SetItemUseSlotVisible(true);
        if (Time.unscaledTime < _resolveRefuseLogAt)
            return;
        _resolveRefuseLogAt = Time.unscaledTime + 2f;
        VRLog.Info("Cards", $"ITEM recess: '{chip.name}' cannot be put back on the pile — it has already been " +
                            "USED and the game is still resolving the action it started (target confirmation, " +
                            "a follow-up prompt). It stays lying in the recess until that finishes, then it " +
                            "flies home on its own. Reach for it if you want to hold it; letting go puts it back.");
    }

    /// <summary>
    /// The card was taken into a hand while its action resolved and has now been let go: put it BACK
    /// into the recess, animated, wherever the release happened. The user's rule for this state is
    /// that the recess is where the card belongs, so a release is a return and never a cancel — see
    /// the block in <see cref="OnChipReleased"/> that routes here.
    /// </summary>
    private void ReturnResolvingCardToRecess(ItemChip chip, Transform slot, VRHand vrHand)
    {
        chip.CancelReleaseGlide(); // no glide to the arc may fight the clip
        chip.PendingUse = true;
        chip.ClipIntoSlot(slot);   // the same settle the first placement plays
        PlayTray.Current?.SetItemUseSlotVisible(true);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM recess: '{chip.name}' returns to the recess ({vrHand.Side}) — its use is " +
                            "already confirmed and the game is still resolving the action, so letting go " +
                            "anywhere puts the card back where it belongs. It leaves on its own the moment " +
                            "the action is finished.");
    }

    /// <summary>
    /// Requirement 6 — per-tick pending-decision service: resolve a CANCEL (the card was grabbed back
    /// out of the slot), an invalidation (turn ended / item no longer usable), or else keep the card
    /// glued to the slot pose (root-local so it rides the board billboard) with the Confirm button up.
    /// </summary>
    private void TickPendingUse(CardsHandUI hand)
    {
        ItemChip? chip = _pendingUseChip;
        if (chip == null)
            return;

        // THE USE IS ALREADY CONFIRMED and the game is resolving the action it started: no cancel
        // path below applies any more (not the grab-back, not "play moved on", not the element
        // choice), so this branch comes first and owns the card until it is finished.
        if (_useResolving)
        {
            TickUseResolving(chip);
            return;
        }

        // ACTIVE-BONUS placement runs its own service: its endings come from the BONUS BAR (the
        // offer withdrawn, the toggle locked, the game untoggling under us), not from the
        // action-turn/SlotState pair PlayContinued reads — that pair is false for a passive item
        // by construction and would cancel the card the same frame it was placed.
        if (_pendingBonus != null)
        {
            TickBonusDecision(chip);
            return;
        }

        // Cancel by grabbing it BACK OUT (#6 refinement): once held again, clear the pending state; the
        // chip's own OnRelease then returns it to the fan (or re-clips if dropped back on the slot).
        if (chip.Holder != null)
        {
            CancelPendingUse("grabbed back out of the slot");
            return;
        }
        // The element pick itself can CONSUME the placed card: a consume-"Any" item auto-uses the
        // moment its picker closes (UIUseConsumeInfuseSlot.Select re-enters and falls through to
        // base.Select → the bar's onSelect → UseItemService). Check that BEFORE the usability
        // invalidation below, or a legitimately used card would be filed as "no longer usable" and
        // slink back to the fan without its burn/tap flourish.
        if (_pendingSubChoice && chip.Item != null && WasUsed(chip.Item))
        {
            BeginUseResolving(chip, chip.Item, "auto-used when the element pick completed");
            return;
        }

        // PLAY MOVED ON — the CANCEL predicate. See PlayContinued for why it is exactly this test.
        if (PlayContinued(hand, chip))
        {
            CancelPlacedCard(chip, "play moved on without a USE confirmation");
            return;
        }

        if (_pendingSubChoice)
            TickChoiceDecision(chip);

        PlayTray.Current?.SetItemUseSlotVisible(true);
        // No per-frame pose work: the chip is a CHILD of the slot while clipped (ClipIntoSlot), so
        // the hierarchy holds it exactly, whatever the head and the board do. Re-assert the parent
        // only if something else stole it (a board rebuild re-creating the slot transform).
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (slot != null && chip.transform.parent != slot)
            chip.ClipIntoSlot(slot);
    }

    // ------------------------------------ the placed card's life AFTER the fan folds away --

    /// <summary>
    /// "PLAY CONTINUES" — the predicate that cancels an un-confirmed placed card (user requirement
    /// 2026-08-09: "Wenn weiter mit den Aktionen fortgefahren wird während eine Gegenstandskarte
    /// liegt und nicht mit 'Use' bestätigt wird, soll die Item Karte auch wieder in das item pile
    /// fliegen (mit einer Animation) und das overlay verschwinden").
    ///
    /// <para>WHAT WAS CHOSEN, AND WHY IT IS THIS AND NOT A TIMER. Two terms, both read from the
    /// GAME'S OWN state, and both already the gate that decided the recess could be offered at all
    /// (<see cref="Tick"/>'s use-slot gate, which is where they come from — this is the same
    /// question asked again one tick later, not a new rule):</para>
    /// <list type="bullet">
    /// <item><c>CardsGameApi.IsActionTurn(hand)</c> — the <c>Choreographer.CurrentActor</c> IS this
    /// hand's own player actor and we control it. This goes false the instant the turn passes to
    /// anybody else, and it is also false through every non-action phase (card selection, the
    /// enemies' turns, the round change). It is precisely "the game has moved on".</item>
    /// <item><c>chip.IsActivatable</c> — the item's LIVE <c>SlotState</c> still says it can be used.
    /// This catches the same item being spent through another route (the 2D bar, a peer's replay of
    /// our own use, a scenario effect) while its card lies here waiting.</item>
    /// </list>
    ///
    /// <para>WHY IT CANNOT FIRE WHILE THE PLAYER IS MERELY THINKING — the explicit requirement ("the
    /// card must survive a long pause"). Neither term contains a clock. As long as it is your action
    /// turn and the item is still usable, both stay true forever: you can put the fan down, walk
    /// round the table, read the board, open and close other menus, and the card lies there. What
    /// makes it fire is an ACTION — ending the turn, playing the cards, taking the initiative
    /// elsewhere — i.e. exactly the user's "wenn weiter mit den Aktionen fortgefahren wird".</para>
    ///
    /// <para>REJECTED ALTERNATIVES, for the record: an idle timeout (fires while thinking — the one
    /// thing forbidden); "any board button press" (the USE cap and the item bar are board presses,
    /// so it would cancel the very confirmation it is waiting for); "the fan closed" (that is the
    /// case this whole change exists to STOP being a cancel).</para>
    /// </summary>
    private static bool PlayContinued(CardsHandUI? hand, ItemChip chip) =>
        hand == null || !CardsGameApi.IsActionTurn(hand) || !chip.IsActivatable;

    /// <summary>
    /// Per-frame service of the card LYING IN the use recess while the arc is CLOSED (see
    /// <see cref="_keptClip"/>). The fan is down, so <see cref="Tick"/> is not running its normal
    /// body — but the decision on this card is very much alive, and every ending the user named has
    /// to be reachable from here: pick it back up, confirm it with USE, or play moves on.
    /// </summary>
    private void TickPlacedWhileClosed(CardsHandUI? hand)
    {
        ItemChip? chip = _keptClip;
        if (chip == null)
            return;
        if (chip.gameObject == null) // destroyed under us (scene teardown)
        {
            _keptClip = null;
            _keptClipIndex = -1;
            return;
        }

        // The board is showing somebody else's hand now — the card belongs to the old character.
        if (hand == null || hand != _hand)
        {
            CancelPlacedCard(chip, "the board switched to another character");
            return;
        }

        // PICKED BACK UP. The grab itself already dropped PendingUse and unclipped the chip
        // (ItemChip.OnGrab), so all that is owed here is the recess staying up: the user's rule is
        // that where you LET GO decides, and releasing back over the recess must still re-clip.
        // The pending decision is only backed out when the release lands somewhere else
        // (OnChipReleased → AfterRefusedDrop).
        //
        // …with ONE flow that must answer the grab immediately instead: a card whose ACTIVE BONUS is
        // already toggled ON. Taking it out IS the un-click, and a locked toggle must refuse the
        // removal visibly — both are decisions, not deferred ones, so TickBonusDecision owns this
        // case (see the grab handling there). Everything else keeps the "where you let go decides"
        // rule unchanged.
        if (chip.Holder != null && (_pendingBonus == null || !ReferenceEquals(chip, _pendingUseChip)))
        {
            PlayTray.Current?.SetItemUseSlotVisible(true);
            return;
        }

        // A confirm may have resolved it between ticks (the USE cap runs ConfirmPendingUse
        // directly), or the element pick may have auto-used it.
        if (!ReferenceEquals(chip, _pendingUseChip))
        {
            _keptClip = null;
            _keptClipIndex = -1;
            return;
        }

        // THE USE IS ALREADY CONFIRMED and the game is resolving the action (see TickUseResolving):
        // one service, fan open or closed, and it outranks every cancel below for the same reason it
        // does in TickPendingUse — there is nothing left to cancel.
        if (_useResolving)
        {
            TickUseResolving(chip);
            return;
        }

        // ACTIVE-BONUS placement: one service, fan open or closed (see TickBonusDecision).
        if (_pendingBonus != null)
        {
            TickBonusDecision(chip);
            return;
        }
        if (_pendingSubChoice && chip.Item != null && WasUsed(chip.Item))
        {
            BeginUseResolving(chip, chip.Item, "auto-used when the element pick completed");
            return;
        }

        // PLAY MOVED ON (see PlayContinued): back out and fly home, animated.
        if (PlayContinued(hand, chip))
        {
            CancelPlacedCard(chip, "play moved on without a USE confirmation");
            return;
        }

        if (_pendingSubChoice)
            TickChoiceDecision(chip);

        // Keep the recess and the USE cap up — they belong to the card, not to the fan.
        PlayTray.Current?.SetItemUseSlotVisible(true);
        Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
        if (slot == null)
        {
            // The board rebuilt and took the recess with it: there is nothing left to lie on.
            CancelPlacedCard(chip, "the board's item-use recess was rebuilt away under it");
            return;
        }
        if (chip.transform.parent != slot)
            chip.ClipIntoSlot(slot); // re-seat visibly (the settle), never a teleport
    }

    /// <summary>
    /// CANCEL a placed card: back the owning flow out through its own game seam and send the card
    /// home WITH AN ANIMATION — the shared ending for every "this placement is over and nothing was
    /// used" path (play moved on, the recess/board went away, the card was taken out and released
    /// somewhere else, the presented character changed).
    ///
    /// <para>WHERE "home" IS depends on whether the arc is up, and both answers are the item pile:
    /// with the fan OPEN the card glides back to its own slot in the arc (the pile, fanned out);
    /// with the fan CLOSED there is no arc to glide to, so it folds into the items STACK on the
    /// board exactly the way the closing fan's chips do — <see cref="RetireChipToPile"/>. Either way
    /// it is seen travelling, which is the standing "nothing may pop" ruling.</para>
    /// </summary>
    private void CancelPlacedCard(ItemChip chip, string why)
    {
        if (chip == null)
            return;
        bool wasPending = ReferenceEquals(chip, _pendingUseChip);
        if (wasPending)
            CancelPendingUse(why); // releases the element choice, drops the cap, hides the recess
        ReturnPlacedChipHome(chip, why);
    }

    /// <summary>Send <paramref name="chip"/> back to the item pile with the right animation for the
    /// state the fan is in — the arc glide while it is open, the fold-into-the-stack while it is
    /// not. Never a teleport, and never a chip left parented to a transform that is about to be
    /// deactivated.</summary>
    private void ReturnPlacedChipHome(ItemChip chip, string why)
    {
        if (chip == null)
            return;
        if (IsOpen && _root != null && _root.gameObject.activeInHierarchy && _chips.Contains(chip))
        {
            UnclipChip(chip);   // back into the fan's frame BEFORE the fan-local glide starts
            chip.PendingUse = false;
            chip.ReturnToFan(); // the animated home glide every refused drop uses
            RefreshFanLayout(); // it rejoins the arc: re-derive the tiling collider strips
            return;
        }
        RetireChipToPile(chip, why);
    }

    /// <summary>
    /// The card has nowhere in an arc to go — fly it INTO the items stack on the board with the same
    /// fold-in the closing fan plays, then let it destroy itself (<see cref="ItemChip.BeginCollapse"/>;
    /// its OnDisable recycles the hosted card widget). This is the "wieder in das item pile fliegen
    /// (mit einer Animation)" the user asked for whenever the fan is already down.
    ///
    /// <para>The chip is re-parented to the BOARD root first, keeping its world pose: it is leaving
    /// the use slot (which the cancel is about to hide) and must not be a child of anything that
    /// could be deactivated mid-flight — the collapse drives world space from there.</para>
    /// </summary>
    private void RetireChipToPile(ItemChip chip, string why)
    {
        // RE-ENTRANT BY CONSTRUCTION, so it says so: releasing the hand below runs the whole drop
        // routing again (OnRelease → OnChipReleased → AfterRefusedDrop → back here) with the chip
        // un-held, and that inner call is the one that does the work. The collapse flag is the
        // honest "this card is already on its way home" test for both entries.
        if (chip == null || chip.IsCollapsing)
            return;
        if (chip.Holder != null)
        {
            chip.Holder.Grabber.CancelAll(); // never collapse a card out of a closed fist
            if (chip.IsCollapsing)
                return; // the re-entry already retired it
        }
        if (ReferenceEquals(_keptClip, chip))
        {
            _keptClip = null;
            _keptClipIndex = -1;
        }
        _chips.Remove(chip);
        ForgetSweepWinner(chip);
        chip.PendingUse = false;
        chip.CancelReleaseGlide();
        chip.ClearHandSuppressed();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor;
        if (keep != null && chip.transform.parent != keep)
            chip.transform.SetParent(keep, worldPositionStays: true);
        chip.BeginCollapse(PileConvergeWorld());
        VRLog.Info("Cards", $"ITEM recess: '{chip.name}' flies back into the items stack ({why}) — " +
                            "the placement is cancelled, nothing was used, and the recess overlay goes " +
                            "with it. The fan is not open, so there is no arc slot to glide to; it folds " +
                            "into the stack exactly like a chip in a closing fan.");
    }

    // ----------------------------------------- the element choice, in the decision area --

    /// <summary>
    /// Per-tick service of a PLACED card that owes an element choice. Three jobs, in order:
    /// <list type="number">
    /// <item>RAISE the choice — one tick after the card clipped in, click the item's own bar slot
    /// (<c>UIUseItemScenario.OnPointerDown</c>, the game's 2D/proxy seam). That is what opens the
    /// element picker; because the picker is a serialized child of the slot prefab and
    /// <see cref="EnforceChoiceSlotSplit"/> has just made this the bar's ONLY visible slot, it
    /// surfaces in the decision area under the control board and nowhere else. The one-tick delay
    /// exists because the docking surface polls: firing in the same frame the slot is re-activated
    /// would open the picker before the bar had ever been seen populated.</item>
    /// <item>TRACK readiness — the game sets <c>UIUseItemsBar.useItem</c> exactly when every "Any"
    /// has been picked (<c>onPickedAll</c> → <c>SetUseItem</c>). That, and nothing else, is the
    /// signal that a CONFIRM is now legal (<c>UseItem()</c> guards on it).</item>
    /// <item>LABEL the cap accordingly — "CHOOSE ELEMENT" (poke re-toggles the picker) until then,
    /// the ordinary USE afterwards. Change-gated: a label change rebuilds the whole board button
    /// cluster.</item>
    /// </list>
    /// </summary>
    private void TickChoiceDecision(ItemChip chip)
    {
        CItem? item = chip.Item;
        if (item == null)
            return;

        // The slot may be rebuilt/repooled by the game (AddItem/RefreshItem) — re-resolve it live.
        UIUseItemScenario? slot = CardsGameApi.ItemsBarSlot(item, requireActive: false);
        _pendingChoiceSlot = slot;
        if (slot == null)
        {
            // The choice UI vanished under us; do not strand the card on the board. Routed through
            // CancelPlacedCard so it also works with the arc CLOSED (the placed card outlives the
            // fan now — a fan-local glide would have no arc to aim at there).
            CancelPlacedCard(chip, "the game withdrew this item's bar slot (no picker to answer)");
            return;
        }

        if (_choiceClickArmed)
        {
            if (!slot.gameObject.activeSelf)
                return; // EnforceChoiceSlotSplit re-activates it; click on the NEXT tick
            _choiceClickArmed = false;
            bool clicked = CardsGameApi.ClickItemsBarSlot(slot);
            VRLog.Info("Cards", $"ITEM choice: raised the element picker for '{chip.name}' via the game's own " +
                                $"items-bar slot click (accepted={clicked}, pickerOpen=" +
                                $"{CardsGameApi.ItemsBarSlotPickerOpen(slot)}) — it docks in the decision " +
                                "area under the control board.");
            if (!clicked)
                CancelPlacedCard(chip, "the items-bar slot refused the click (state gate)");
            return;
        }

        // A consume-"Any" item uses ITSELF the moment its picker closes: the pick re-enters
        // UIUseConsumeInfuseSlot.Select, the consume controller now reports "all picked", and the
        // call falls through to base.Select() — which both sets the slot SELECTED and fires the
        // bar's use callback. The infuse path (mana potions) never reaches base.Select(), so a
        // selected slot is an unambiguous "already used" edge, and a faster one than waiting for
        // the SlotState round trip (online it lands a frame or more later).
        if (CardsGameApi.ItemsBarSlotSelected(slot))
        {
            // …and it is a USE like any other, so it hands over to the RESOLVING state rather than
            // ending the card's life here: a consume-"Any" potion still owes whatever its own
            // ability asks for next (see BeginUseResolving).
            BeginUseResolving(chip, item, "auto-used when the element pick completed");
            return;
        }

        bool ready = ReferenceEquals(CardsGameApi.ItemsBarPendingItem(), item);
        ShowChoiceCap(ready);
    }

    /// <summary>Show the item-use cap in its CHOOSE (poke = re-open the picker) or USE (poke =
    /// confirm) wording. Change-gated: <c>SetItemUseConfirmVisible</c> rebuilds the Confirm/Undo/Use
    /// cluster on a label change, which must not happen every frame.</summary>
    private void ShowChoiceCap(bool ready)
    {
        if (_choiceCapShown && _choiceCapReady == ready)
            return;
        _choiceCapShown = true;
        _choiceCapReady = ready;
        PlayTray.Current?.SetItemUseConfirmVisible(
            true,
            ready ? ConfirmPendingUse : ReopenChoicePicker,
            ready ? null : ChooseElementLabel);
        VRLog.Info("Cards", $"ITEM choice cap → {(ready ? "USE (element picked; poke to confirm)" : "CHOOSE ELEMENT (poke re-opens the picker)")}.");
    }

    /// <summary>Cap action while the element is still unpicked: re-toggle the game's own picker.
    /// <c>UIUseSlot.Toggle</c> closes it if it is open and opens it if it is not
    /// (<c>UIUseConsumeInfuseSlot.Select</c> → <c>controller.IsSelecting() ? ClosePicker() :
    /// Pick()</c>), so ONE cap covers "I dismissed it, bring it back" and "hide it a second".</summary>
    private void ReopenChoicePicker()
    {
        UIUseItemScenario? slot = _pendingChoiceSlot;
        if (slot == null || _pendingUseChip == null)
            return;
        bool wasOpen = CardsGameApi.ItemsBarSlotPickerOpen(slot);
        CardsGameApi.ClickItemsBarSlot(slot);
        VRLog.Info("Cards", $"ITEM choice: CHOOSE cap toggled the element picker ({(wasOpen ? "open→close" : "closed→open")}).");
    }

    /// <summary>Has this item already left the usable states — i.e. the game applied it? Used to tell
    /// "the element pick auto-used the card" apart from "the turn moved on". Mirrors the states
    /// <c>CInventory.HandleUsedItem</c> writes (Spent/Consumed) plus the commit-side Locked/Active.</summary>
    private static bool WasUsed(CItem item) =>
        item.SlotState == CItem.EItemSlotState.Spent
        || item.SlotState == CItem.EItemSlotState.Consumed
        || item.SlotState == CItem.EItemSlotState.Locked
        || item.SlotState == CItem.EItemSlotState.Active;

    /// <summary>
    /// The item-use cap wording while the placed card still owes its element choice. Local fallback
    /// until <c>Core/Loc.cs</c> carries the key (that file is owned elsewhere) — <c>Loc.Mod</c>
    /// returns the id itself when a key is missing, which would put "item_choose_element" on a
    /// board cap.
    /// </summary>
    private static string ChooseElementLabel
    {
        get
        {
            const string id = "item_choose_element";
            string s = Core.Loc.Mod(id);
            return string.IsNullOrEmpty(s) || s == id ? "CHOOSE ELEMENT" : s;
        }
    }

    /// <summary>
    /// EVERY item is played by placing its card, so no item symbol on the docked
    /// <c>UIUseItemsBar</c> may be clickable on its own. <c>UseBarsSurface</c> already suppresses
    /// the PLAIN symbols; this suppresses the element-CHOICE ones — all of them except the single
    /// slot whose card is currently in the board's item-use slot, which is deliberately re-activated
    /// so its picker can dock. Net effect: the items bar docks ONLY to carry a placed card's element
    /// choice, and the bar's own <c>ItemsPopulated</c> gate (it counts ACTIVE choice slots) turns the
    /// dock on and off for free.
    ///
    /// Level-triggered every tick, fan open or closed — the game re-activates pooled slots at will
    /// (<c>AddItem</c>/<c>RefreshItem</c>/<c>OnUnreservedElement</c>). Restores only where the bar
    /// still maps the same item to the same slot (never resurrecting a slot the game itself pooled),
    /// the same rule <c>UseBarsSurface.RestorePlainHidden</c> uses.
    /// </summary>
    internal void TickItemSymbolSplit() => EnforceChoiceSlotSplit();

    private void EnforceChoiceSlotSplit()
    {
        // HANDS OFF during the take-damage decision: TakeDamagePanel.Show repopulates the SAME bar
        // with the attacked actor's OnAttacked candidates, and that flow's own place seam resolves
        // slots through LiveItemsBarSlot (active-only). Suppressing anything there would make a
        // shield card undroppable. Same for the item-surrender demand, which is picker-driven.
        if (_tdActive || _demandActive)
        {
            RestoreChoiceHidden();
            return;
        }

        CItem? keep = _pendingSubChoice && _pendingUseChip != null ? _pendingUseChip.Item : null;
        CardsGameApi.ItemsBarSlotsSnapshot(_slotScratch);

        if (_slotScratch.Count == 0)
        {
            RestoreChoiceHidden();
            return;
        }

        for (int i = 0; i < _slotScratch.Count; i++)
        {
            CItem item = _slotScratch[i].Key;
            UIUseItemScenario slot = _slotScratch[i].Value;
            if (slot == null)
                continue;
            bool isKeep = keep != null && ReferenceEquals(item, keep);
            if (isKeep)
            {
                if (!slot.gameObject.activeSelf)
                    slot.gameObject.SetActive(true);
                DropChoiceHidden(slot);
                continue;
            }
            if (!CardsGameApi.SlotNeedsSubChoice(slot) || !slot.gameObject.activeSelf)
                continue; // plain slots belong to UseBarsSurface's half of the split
            slot.gameObject.SetActive(false);
            bool known = false;
            for (int j = 0; j < _choiceHidden.Count; j++)
                if (ReferenceEquals(_choiceHidden[j].Value, slot))
                {
                    known = true;
                    break;
                }
            if (!known)
            {
                _choiceHidden.Add(_slotScratch[i]);
                VRLog.Info("Cards", $"ITEM symbol split: hid the choice symbol for '{item.Name}' — it is used by " +
                                    "PLACING its card in the board's item slot; the element choice then opens " +
                                    "in the decision area.");
            }
        }
        _slotScratch.Clear();
    }

    /// <summary>Forget one slot from the suppression ledger (it is the pending card's own slot now).</summary>
    private void DropChoiceHidden(UIUseItemScenario slot)
    {
        for (int i = _choiceHidden.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_choiceHidden[i].Value, slot))
                _choiceHidden.RemoveAt(i);
    }

    /// <summary>Re-activate every choice symbol this pile suppressed, but only where the bar still
    /// maps the same item to the same slot — otherwise the game has pooled it and re-activating
    /// would corrupt its pooling. Called when the bar empties and on teardown.</summary>
    private void RestoreChoiceHidden()
    {
        if (_choiceHidden.Count == 0)
            return;
        for (int i = 0; i < _choiceHidden.Count; i++)
        {
            UIUseItemScenario slot = _choiceHidden[i].Value;
            CItem item = _choiceHidden[i].Key;
            if (slot == null)
                continue;
            UIUseItemScenario? live = CardsGameApi.ItemsBarSlot(item, requireActive: false);
            if (ReferenceEquals(live, slot) && !slot.gameObject.activeSelf)
                slot.gameObject.SetActive(true);
        }
        _choiceHidden.Clear();
    }

    /// <summary>
    /// Take a clipped chip back out of the use-slot hierarchy and into the fan root, world pose
    /// preserved (see <see cref="ItemChip.ClipIntoSlot"/> for why it was parented to the slot at all).
    /// EVERY exit from the pending state routes through here — cancel, invalidation and the grab that
    /// pulls the card back out — so the chip's fan-local home pose and glide are always evaluated in
    /// the frame they were written for. No-op when the chip is not (or no longer) under the slot.
    /// </summary>
    internal void UnclipChip(ItemChip chip) => chip?.UnclipFromSlot(ClipParkParent);

    /// <summary>
    /// The transform a chip leaving the use slot is handed to. Normally the fan root — that is the
    /// frame its arc home pose and its glide are written in.
    ///
    /// <para>BUT THE FAN ROOT IS DEACTIVATED WHILE THE FAN IS CLOSED, and since a placed card now
    /// outlives the close (see <see cref="_keptClip"/>) that is a reachable state: parenting the
    /// card there the moment the player grabbed it out of the recess would put it under an INACTIVE
    /// object, so the chip would go inactive in the hierarchy — its <c>Update</c> would stop and the
    /// card would simply vanish out of the hand. The board root is the live fallback: it is the same
    /// board the recess hangs off, it is never deactivated while the piles show, and the paths that
    /// use it (<see cref="RetireChipToPile"/>'s fold-in, <see cref="CollapseChips"/>) drive world
    /// space, so no fan-local pose is being relied on there.</para>
    /// </summary>
    private Transform? ClipParkParent =>
        _root != null && _root.gameObject.activeInHierarchy
            ? _root
            : (PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor);

    /// <summary>Re-run the arc layout (poses, split, collider strips) without rebuilding content —
    /// used when a chip rejoins the arc after a grab, so its full-size grab box is stripped back
    /// down and the per-chip regions tile again.</summary>
    internal void RefreshFanLayout()
    {
        if (IsOpen)
            Relayout();
    }

    // ---------------------------------------------------- hand-to-hand chip transfer (req 6) --

    /// <summary>The chip mid-handover — non-null ONLY inside <see cref="TransferHeldChip"/>'s
    /// release+grab call stack, and read by <see cref="ItemChip.OnRelease"/> so that release skips
    /// the whole drop routing (the card never left the hands).</summary>
    private ItemChip? _transferChip;
    private VRHand? _transferTo;

    /// <summary>True while <paramref name="chip"/> is the card being handed from one hand to the
    /// other (see <see cref="TransferHeldChip"/>).</summary>
    internal bool IsTransferring(ItemChip chip) =>
        _transferChip != null && ReferenceEquals(_transferChip, chip);

    /// <summary>
    /// Requirement 6 — HAND-TO-HAND transfer of a held item card (user ruling 2026-08-08: "Auch
    /// Item-Karten sollen (wie die normalen Karten auch) … Hände getauscht werden können. Sie sollen
    /// also wie normale Karten reagieren").
    ///
    /// <para>The ITEM twin of <c>CardsDriver.TransferHeldCard</c>, and deliberately the same
    /// ordering: the release from <paramref name="from"/> and the adoption into
    /// <paramref name="to"/> happen in ONE call stack, so no frame — and no net rig sample — can
    /// ever observe the card un-held (which would flash it back into the fan on peers and in the
    /// mirror), and the chip keeps its world pose through both re-parents, so it eases into the new
    /// hand instead of teleporting. The receiving hold is trigger-held exactly like a pluck.</para>
    ///
    /// <para>The DETECTION (free hand in touch reach of the held card, hover haptic, trigger) lives
    /// where it already lived for ability cards — <c>CardsDriver.UpdateHeldCardTransfer</c> — so the
    /// two card kinds share one gesture and one set of hysteresis constants. This method is only the
    /// commit.</para>
    ///
    /// <para>ABORT RULE, copied from the ability side for the same reason: if the adoption is
    /// refused the chip is re-adopted into the ORIGINAL hand rather than released, because a refusal
    /// that falls through to the drop routing would glide the card back to the fan mid-handover —
    /// the "the card vanished out of my hands" failure. Only if BOTH hands refuse does the normal
    /// release routing run.</para>
    /// </summary>
    internal void TransferHeldChip(ItemChip chip, VRHand from, VRHand to)
    {
        if (chip == null || from == null || to == null)
            return;
        _transferChip = chip;
        _transferTo = to;
        try
        {
            from.Grabber.CancelAll(); // → ItemChip.OnRelease → CompleteChipTransfer (adopt in-stack)
        }
        finally
        {
            bool adopted = ReferenceEquals(to.Grabber.Held, chip);
            bool aborted = !adopted && ReferenceEquals(from.Grabber.Held, chip);
            _transferChip = null;
            _transferTo = null;
            if (adopted)
                VRLog.Info("Cards", $"ITEM hand transfer: '{chip.name}' handed {from.Side} → {to.Side} " +
                                    "(trigger on the held card) — release routing skipped, the hold " +
                                    "continues on the receiving hand's trigger.");
            else if (aborted)
                VRLog.Warn("Cards", $"ITEM hand transfer: adoption of '{chip.name}' into the {to.Side} hand " +
                                    $"was refused — ABORTED, the card stays held in the {from.Side} hand.");
            else
                VRLog.Warn("Cards", $"ITEM hand transfer: adoption of '{chip.name}' into the {to.Side} hand " +
                                    $"AND the re-adoption into the {from.Side} hand were refused — the card " +
                                    "took the normal release routing instead (no limbo).");
        }
    }

    /// <summary>
    /// Requirement 6 — the adoption half of <see cref="TransferHeldChip"/>, called from inside the
    /// releasing <see cref="ItemChip.OnRelease"/> so the card is never observed un-held. Returns
    /// true when SOME hand holds the chip afterwards (adopted, or safely re-adopted into the
    /// releasing hand); false means both refused and the caller must run the normal routing.
    /// </summary>
    internal bool CompleteChipTransfer(ItemChip chip, VRHand from)
    {
        VRHand? to = _transferTo;
        if (to == null)
            return false;
        if (to.Grabber.ForceGrab(chip, releaseOnTriggerUp: true))
            return true;
        // Refused — keep the card in the hand that was holding it, on whichever button is still
        // physically down (a grip hold re-adopted trigger-held would release itself next Tick).
        return from.Grabber.ForceGrab(chip, releaseOnTriggerUp: from.TriggerPressed);
    }

    /// <summary>Requirement 6 — drop the pending state + hide the Confirm button. Clears the chip's own
    /// PendingUse flag so a chip GRABBED back out glides home on release (instead of re-clipping); the
    /// invalidation path already called <see cref="ItemChip.ReturnToFan"/> to start that glide.</summary>
    private void CancelPendingUse(string why)
    {
        ItemChip? chip = _pendingUseChip;
        _pendingUseChip = null;
        // …and with it the RESOLVING state, if the placement had got that far. Reaching a cancel with
        // a confirmed use means a BACKSTOP fired (the board switched character, the pile is being
        // torn down): the card must not be left lying on furniture that is going away, and the game
        // side of the use is the game's own business — it was committed through the game's seam and
        // will resolve, or not, without a card on a recess to represent it.
        _useResolving = false;
        // Back the GAME out of the element choice first (while _pendingChoiceSlot is still valid):
        // an open picker is closed by re-toggling the slot, and a completed pick is released by the
        // bar's own OnItemBackClick — which clears the slot's element holders, clears the pending
        // item, re-arms the native buttons and resets the phase for the two mana potions. Doing this
        // by hand would drop that last part on the floor.
        if (_pendingSubChoice)
            AbandonChoice(chip?.Item);
        // …and the same rule for the THIRD placement kind: an active bonus this flow toggled on must
        // never outlive the card that stands for it. No-op when the caller already released the
        // toggle itself (TickBonusDecision's un-click clears _pendingBonusToggled first).
        ReleaseBonusToggle(why);
        _pendingSubChoice = false;
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        if (chip != null)
        {
            UnclipChip(chip); // no-op when a grab already took it out of the slot hierarchy
            chip.PendingUse = false;
            // The decision is over, so the recess-survivor state goes with it (see _keptClip): the
            // card is no longer "the one lying in the recess", whatever happens to it next. The
            // CALLER owns the animation home — every cancel path routes through CancelPlacedCard,
            // which runs this and then ReturnPlacedChipHome; this method only ends the decision.
            if (ReferenceEquals(_keptClip, chip))
            {
                _keptClip = null;
                _keptClipIndex = -1;
            }
        }
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        // THE RECESS OUTLIVES A CANCEL-BY-TAKING-IT-BACK (same user report as UnclipFromSlot's
        // note — the pick-it-up-again gesture). When the cancel fires because a HAND took the card,
        // that card is still usable and still in that hand, so <see cref="Tick"/>'s ordinary
        // action-turn gate will show the recess again on the very next tick. Hiding it here would
        // therefore (a) flash the recess overlay off and back on for one frame — the "nothing pops"
        // ruling — and, worse, (b) open a real failure window for the user's own release rule:
        // <see cref="OnChipReleased"/> refuses any drop while the slot object is inactive, so a
        // player who grabbed the card and let go again quickly, right over the recess, would have
        // the re-clip REFUSED and watch the card glide back to the fan instead. Every other cancel
        // (play moved on, the board rebuilt, the laser put it back) leaves no card in a hand and
        // still hides the recess here.
        if (chip == null || chip.Holder == null)
        {
            PlayTray.Current?.SetItemUseSlotVisible(false);
            _useSlotShownLogged = false;
        }
        VRLog.Info("Cards", $"ITEM clip-in CANCEL ({why}) — card returns to the deck, NOT used." +
                            (chip != null && chip.Holder != null
                                ? $" It is IN THE {chip.Holder.Side} HAND (taken back out of the recess): the" +
                                  " hand keeps it, the recess stays up, and where it is released decides —" +
                                  " over the recess it clips back in, anywhere else it goes home."
                                : string.Empty));
    }

    /// <summary>
    /// Requirement 5b — the FAR-LASER half of "take the placed card back": put the card lying in
    /// the recess back on the pile in ONE deliberate gesture, animated, without the phantom hop
    /// through the hand. The rule this belongs to (hand = take, laser = put back) is written out in
    /// full at <see cref="ItemChip.OnPoke"/>, which is the fork that chooses between them.
    ///
    /// <para>Every flow that can own the recess is backed out through ITS OWN game seam, because a
    /// placed card is never only a visual: the surrender pick has a SELECTION in the game's
    /// <c>ItemCardPicker</c> and the take-damage place has a TOGGLED shield item on the panel's
    /// items bar. Returning the card without undoing those would leave the game holding a choice the
    /// player can no longer see — the exact "card and state disagree" class the grab-back paths in
    /// <see cref="TickDemandPick"/> / <see cref="TickTakeDamagePick"/> already guard. So this is the
    /// same inverse those two run, reached by the laser instead of by a physical grab.</para>
    /// </summary>
    internal void ReturnPlacedChip(ItemChip chip, VRHand hand)
    {
        if (chip == null)
            return;

        string flow;
        if (ReferenceEquals(chip, _demandChip))
        {
            ItemCardPicker? picker = ActiveDemandPicker();
            if (picker != null && chip.Item != null)
                CardsGameApi.ItemPickDeselect(picker, chip.Item);
            _demandChip = null;
            flow = "item-surrender pick — DESELECTED through the game's own ItemCardPickerSlot seam";
        }
        else if (ReferenceEquals(chip, _tdChip))
        {
            _tdChip = null;
            if (chip.Item != null && chip.Item.SlotState == CItem.EItemSlotState.Selected)
            {
                UIUseItemScenario? barSlot = CardsGameApi.LiveItemsBarSlot(chip.Item);
                if (barSlot != null)
                    CardsGameApi.ClickItemsBarSlot(barSlot);
            }
            flow = "take-damage shield place — UNTOGGLED through the panel's own slot seam";
        }
        else if (ReferenceEquals(chip, _pendingUseChip))
        {
            // THE USE IS ALREADY CONFIRMED: there is no placement left to put back. The beam gesture
            // means "cancel this placement and return the card to the pile", and that sentence has no
            // meaning once the item has been used — the card is lying in the recess because the GAME
            // is still resolving the action, and it leaves when the action does. Refused the same way
            // a locked bonus toggle is refused, and with the same visible spring-back, so the gesture
            // is answered rather than silently swallowed. Taking the card INTO a hand is still free
            // (that is the physical route, and letting go returns it here).
            if (_useResolving)
            {
                RefuseResolvingRemoval(chip);
                hand.SendHaptic(HapticPreset.HoverTick);
                return;
            }
            // A LOCKED active-bonus toggle refuses the laser route exactly as it refuses the
            // physical one (see RefuseLockedBonusRemoval): the rules will not give the choice back,
            // so the card stays where it is and says why. Same refusal, both gestures.
            if (_pendingBonus != null && _pendingBonusToggled
                && CardsGameApi.ActiveBonusToggleLocked(_pendingBonus))
            {
                RefuseLockedBonusRemoval(chip, _pendingBonus);
                hand.SendHaptic(HapticPreset.HoverTick);
                return;
            }
            // Releases the element choice too (AbandonChoice), gives back a toggled active bonus
            // (ReleaseBonusToggle) and drops the Confirm button.
            CancelPendingUse("laser click on the placed card — put back on the pile");
            flow = "pending USE decision — cancelled, nothing was used";
        }
        else
        {
            flow = "no owning flow (stale clip) — returned visually";
        }

        // The SAME animated home every refused drop uses — never a pop. Routed through
        // ReturnPlacedChipHome so it also answers the case the fan is not open (the placed card
        // outlives the close now, see _keptClip): with an arc up it glides back to its own slot,
        // without one it folds into the items stack.
        ReturnPlacedChipHome(chip, "laser click on the placed card — put back on the pile");
        hand.SendHaptic(HapticPreset.HoverTick);
        VRLog.Info("Cards", $"ITEM recess: laser click on the placed card '{chip.name}' ({hand.Side}) — " +
                            $"it glides back to the fan. {flow}. To take it INTO your hand instead, " +
                            "reach for it and grab it (either hand) — that is the physical route, and " +
                            "where you let go decides whether it clips back in or returns to the pile.");
    }

    /// <summary>Release the game's half of an element choice: close an open picker (a second slot
    /// toggle — <c>UIUseConsumeInfuseSlot.Select</c> routes that to <c>ClosePicker</c>, whose
    /// close handler cancels partial selections), then run the bar's own back-out if the pick had
    /// already completed. Every step is a game seam; the mod invents no cancel of its own.</summary>
    private void AbandonChoice(CItem? item)
    {
        UIUseItemScenario? slot = _pendingChoiceSlot;
        try
        {
            if (slot != null && CardsGameApi.ItemsBarSlotPickerOpen(slot))
                CardsGameApi.ClickItemsBarSlot(slot);
            if (CardsGameApi.ItemsBarBackOut(item))
                VRLog.Info("Cards", "ITEM choice: released through the bar's own back-out " +
                                    "(element selections cleared; mana potions re-enter ActionSelection).");
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ITEM choice: back-out threw ({e.Message}); the card still returns to the fan.");
        }
    }

    // ======================= the ACTIVE-BONUS placement (the Brille, 2026-08-09) =================
    //
    // The whole rationale — why this is possible at all, and why the previous round's "the button
    // must stay" was wrong — lives at the seam it drives: CardsGameApi's
    // "ITEM-BACKED ACTIVE BONUSES" block. What follows is only the VR flow around it.

    /// <summary>
    /// Drop routing while the released card's item has a LIVE OFFERED, option-less active bonus.
    /// Clips the card in and raises the ordinary USE cap — and toggles NOTHING yet. That is
    /// deliberate and it is the user's own wording ("ich kann sie hinlegen und 'usen'"): the recess
    /// idiom is place-then-confirm everywhere else on this board, a mis-drop must be free, and the
    /// commit (which is a real, MP-synced game action) belongs on the deliberate second gesture.
    /// A drop away from the recess is not a placement — the base glide-home already runs.
    /// </summary>
    private void HandleBonusDrop(ItemChip chip, Vector3 dropWorldPos, Transform slot, VRHand vrHand,
                                 CActiveBonus bonus)
    {
        // A LOCKED toggle cannot be taken back, so its card cannot leave: whatever route got it into
        // a hand (ItemChip.AllowsHand refuses the ordinary grab, but a forced release — the
        // CancelAll in RefuseLockedBonusRemoval itself — routes through here), the answer is the same
        // and it is idempotent: put it back in the recess. Written FIRST so no drop-position test can
        // turn a refusal into a cancel.
        if (ReferenceEquals(chip, _pendingUseChip) && _pendingBonusToggled
            && CardsGameApi.ActiveBonusToggleLocked(bonus))
        {
            RefuseLockedBonusRemoval(chip, bonus);
            return;
        }

        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
        {
            AfterRefusedDrop(chip);
            return;
        }

        // One pending decision at a time (a fresh drop replaces an older pending clip cleanly) —
        // and the older one is backed out through ITS own seam first, exactly as the plain path does.
        if (_pendingUseChip != null && !ReferenceEquals(_pendingUseChip, chip))
            CancelPlacedCard(_pendingUseChip, "another item card was placed in the recess");

        _pendingUseChip = chip;
        _pendingBonus = bonus;
        _pendingBonusToggled = false;
        _pendingSubChoice = false;   // a placeable bonus has no element consume, by its own predicate
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        chip.PendingUse = true;
        chip.CancelReleaseGlide(); // do NOT glide home — we clip into the slot instead
        chip.ClipIntoSlot(slot);
        PlayTray.Current?.SetItemUseSlotVisible(true);
        PlayTray.Current?.SetItemUseConfirmVisible(true, ConfirmPendingBonus);

        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM BONUS clip-in: '{chip.name}' lies in the use recess for the offered active " +
                            $"bonus '{CardsGameApi.BonusCardName(bonus)}' ({bonus.GetType().Name}). Nothing is " +
                            "toggled yet — poke USE to press the game's own bonus row (that click is what the " +
                            "flat game's button does, and the game syncs it to peers itself), or grab the card " +
                            "back out to cancel for free.");
    }

    /// <summary>
    /// USE cap for a placed ACTIVE-BONUS card: click the bonus's own row through the game's seam.
    /// This IS the button — <c>UIUseSlot.OnPointerDown → Toggle → Select</c> →
    /// <c>ActiveBonus.ToggleActiveBonus(…, fromClick: true)</c>, which is also what sends the
    /// <c>ClickActiveBonusSlot</c> GameAction that peers replay in <c>ProxyUseActiveBonus</c>.
    ///
    /// <para>THE CARD STAYS LYING THERE afterwards, and the cap goes away. A toggled bonus is not a
    /// spent item: the game charges it later, inside <c>CActiveBonus.ActiveBonusUsed</c>, when the
    /// attack (or whatever triggered the offer) actually resolves. Until then the toggle is
    /// reversible in the game's own model, so the card must stay reversible too — it is the lit row,
    /// and taking it back out is the un-click. Removing the card on USE would have thrown that away
    /// AND lied about the item being spent.</para>
    ///
    /// <para>A refused click is reported and changes nothing: the bar disarms its slots for the few
    /// frames a toggle is being processed (<c>SetInteractionAvailableSlots(false)</c>), so "not
    /// interactable" means "not yet", and the cap stays up for another poke.</para>
    /// </summary>
    private void ConfirmPendingBonus()
    {
        ItemChip? chip = _pendingUseChip;
        CActiveBonus? bonus = _pendingBonus;
        if (chip == null || bonus == null)
            return;
        if (_pendingBonusToggled)
            return; // already pressed; the cap should be down already

        UIUseActiveBonus? slot = CardsGameApi.ActiveBonusSlot(bonus);
        if (slot == null)
        {
            CancelPlacedCard(chip, "the game withdrew the bonus offer before the USE confirm");
            return;
        }
        if (!CardsGameApi.ClickActiveBonusSlot(slot))
        {
            VRLog.Info("Cards", $"ITEM BONUS USE refused for '{chip.name}': the game has the bonus bar " +
                                "disarmed this instant (SetInteractionAvailableSlots(false) — it does that " +
                                "while a toggle is being processed by the rules engine). The card stays in " +
                                "the recess and the USE cap stays up; poke it again in a moment.");
            return;
        }
        if (!CardsGameApi.ActiveBonusSlotSelected(slot))
        {
            VRLog.Warn("Cards", $"ITEM BONUS USE: the game's own row click did not leave '{chip.name}' " +
                                "selected — the bonus is not toggled, so the card goes back to the pile " +
                                "rather than lying there claiming a state the game does not have.");
            CancelPlacedCard(chip, "the bonus row refused the click");
            return;
        }

        _pendingBonusToggled = true;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        VRLog.Info("Cards", $"ITEM BONUS USED: '{chip.name}' toggled the active bonus " +
                            $"'{CardsGameApi.BonusCardName(bonus)}' ON through the game's own row click " +
                            $"(model ToggledBonus={bonus.ToggledBonus}). Online this shipped as the game's " +
                            "own ClickActiveBonusSlot GameAction — no mod wire is involved in the rules " +
                            "effect. The card STAYS in the recess: the bonus is armed, not yet spent, so " +
                            "taking the card back out is still the un-click until the game locks or " +
                            "resolves it.");
    }

    /// <summary>
    /// Per-tick service of a placed ACTIVE-BONUS card, fan open or closed. Every ending comes from
    /// the game's own bonus bar, never from a clock and never from the action-turn/SlotState pair
    /// <see cref="PlayContinued"/> reads (a passive item fails that pair by construction):
    /// <list type="bullet">
    /// <item>GRABBED BACK OUT — the un-click. Not yet toggled ⇒ a free cancel. Toggled ⇒ untoggle
    /// through the same row click (<c>Unselect → UntoggleActiveBonus(fromClick: true)</c>, which
    /// syncs itself), unless the game has LOCKED the toggle, in which case the removal is REFUSED:
    /// the hand's grab is cancelled, the card springs back into the recess and the log says why.
    /// Silently letting the card leave would put the board and the rules in disagreement.</item>
    /// <item>THE OFFER WITHDRAWN (<c>GetSlotForActiveBonus</c> → null, i.e. the bar's
    /// <c>Remove</c>/<c>Clear</c> ran) — if the toggle stood, the game has resolved it and charged
    /// the item, so the card plays its burn/tap flourish and returns to the pile; if it never
    /// toggled, this is a plain cancel and the card flies home un-used.</item>
    /// <item>THE GAME UNTOGGLED IT under us (<c>UndoSelection</c>, a lethality recalc, a peer's undo)
    /// — the card goes home, because card and rules state may never disagree.</item>
    /// <item>THE GAME TOGGLED IT for us (<c>TakeDamagePanelSafety</c>'s mandatory auto-click, a
    /// proxy replay) — adopt it silently rather than fight it, and drop the now-meaningless cap.</item>
    /// </list>
    /// </summary>
    private void TickBonusDecision(ItemChip chip)
    {
        CActiveBonus? bonus = _pendingBonus;
        if (bonus == null)
            return;
        UIUseActiveBonus? slot = CardsGameApi.ActiveBonusSlot(bonus);

        // ---- picked back up -------------------------------------------------------------------
        if (chip.Holder != null)
        {
            if (!_pendingBonusToggled)
            {
                // Nothing was ever committed: this is the ordinary "where you let go decides" rule.
                CancelPendingUse("the bonus card was grabbed back out before it was used");
                return;
            }
            if (CardsGameApi.ActiveBonusToggleLocked(bonus))
            {
                RefuseLockedBonusRemoval(chip, bonus);
                return;
            }
            if (slot != null && CardsGameApi.ActiveBonusSlotSelected(slot)
                && !CardsGameApi.ClickActiveBonusSlot(slot))
            {
                // The bar is disarmed for a frame or two while the rules engine answers. Hold the
                // card in the hand and try again next tick — never leave the toggle standing.
                return;
            }
            _pendingBonusToggled = false;
            VRLog.Info("Cards", $"ITEM BONUS untoggled: '{chip.name}' was taken back out of the recess, so the " +
                                $"active bonus '{CardsGameApi.BonusCardName(bonus)}' was un-clicked through the " +
                                "game's own row (UntoggleActiveBonus, fromClick: true — the game syncs the " +
                                "untoggle to peers itself).");
            CancelPendingUse("the bonus card was grabbed back out — the toggle was released");
            return;
        }

        // ---- the offer is gone ------------------------------------------------------------------
        if (slot == null)
        {
            CItem? item = chip.Item;
            // Only a real state change earns the burn/tap flourish. For the Brille class it will NOT
            // come, and that is not a bug: CActiveBonus.ActiveBonusUsed charges such an item through
            // CActor.UsedItem, which (SRL CActor.cs:1982-2000) writes an EVENT-LOG message and the
            // bonus's own tracker — it never touches CItem.SlotState. So the card simply travels
            // home, animated, exactly as it does after any other resolved decision. The WasUsed test
            // stays because an item-backed bonus on a Spent/Consumed item WOULD move the state, and
            // that one deserves its flourish.
            if (_pendingBonusToggled && item != null && WasUsed(item))
            {
                FinishUsedChip(chip, item, "the game resolved the toggled bonus and the item's state moved with it");
                return;
            }
            CancelPlacedCard(chip, _pendingBonusToggled
                ? "the game resolved the toggled bonus (the item is charged through the bonus's own " +
                  "tracker, not through its slot state), so the card goes back to the pile"
                : "the game withdrew the bonus offer without it being used");
            return;
        }

        // ---- the game moved the toggle under us --------------------------------------------------
        bool selected = CardsGameApi.ActiveBonusSlotSelected(slot);
        if (_pendingBonusToggled && !selected)
        {
            CancelPlacedCard(chip, "the game untoggled the bonus (undo / recalculation) — card and rules " +
                                   "state may never disagree");
            return;
        }
        if (!_pendingBonusToggled && selected)
        {
            _pendingBonusToggled = true;
            PlayTray.Current?.SetItemUseConfirmVisible(false, null);
            VRLog.Info("Cards", $"ITEM BONUS: the GAME toggled '{CardsGameApi.BonusCardName(bonus)}' on by " +
                                "itself (a mandatory auto-use or a replayed peer action) while its card lay " +
                                "in the recess — adopted, and the USE cap dropped; the card now stands for " +
                                "that toggle.");
        }

        // ---- steady state -----------------------------------------------------------------------
        PlayTray.Current?.SetItemUseSlotVisible(true);
        Transform? useSlot = PlayTray.Current?.ItemUseSlotTransform;
        if (useSlot == null)
        {
            CancelPlacedCard(chip, "the board's item-use recess was rebuilt away under it");
            return;
        }
        if (chip.transform.parent != useSlot)
            chip.ClipIntoSlot(useSlot); // re-seat visibly (the settle), never a teleport
    }

    /// <summary>
    /// The game has LOCKED this toggle (<c>CActiveBonus.ToggleLocked</c>, set by
    /// <c>UIActiveBonusBar.LockToggledActiveBonuses</c> once the surrounding step commits), so the
    /// card may not leave the recess: the rules will not give the choice back, and a card that
    /// travelled home would be a promise the game cannot keep. The removal FAILS VISIBLY —
    /// the hand's grab is cancelled, so the card springs back into the recess in front of the
    /// player — plus a haptic and a throttled log line naming the reason. It is never silent.
    /// </summary>
    /// <summary>Is <paramref name="chip"/> the card lying in the recess for an active bonus the game
    /// has LOCKED — i.e. the one card in this pile that may not be picked up at all? Asked by
    /// <see cref="ItemChip.AllowsHand"/> every hover, so it is a handful of reference compares plus
    /// one bool read; nothing is allocated and no game call is made beyond
    /// <c>CActiveBonus.ToggleLocked</c>.</summary>
    internal bool PlacedCardIsLocked(ItemChip chip) =>
        chip != null && _pendingBonus != null && _pendingBonusToggled
        && ReferenceEquals(chip, _pendingUseChip)
        && CardsGameApi.ActiveBonusToggleLocked(_pendingBonus);

    private void RefuseLockedBonusRemoval(ItemChip chip, CActiveBonus bonus)
    {
        VRHand? hand = chip.Holder;
        hand?.Grabber.CancelAll();
        hand?.SendHaptic(HapticPreset.HoverTick);
        Transform? useSlot = PlayTray.Current?.ItemUseSlotTransform;
        if (useSlot != null && chip.Holder == null)
        {
            chip.CancelReleaseGlide();
            chip.PendingUse = true;
            chip.ClipIntoSlot(useSlot); // the visible spring-back (the settle animation, not a pop)
        }
        if (Time.unscaledTime < _bonusLockLogAt)
            return;
        _bonusLockLogAt = Time.unscaledTime + 2f;
        VRLog.Info("Cards", $"ITEM BONUS: '{chip.name}' cannot be taken back out — the game has LOCKED the " +
                            $"toggle of '{CardsGameApi.BonusCardName(bonus)}' (CActiveBonus.ToggleLocked; " +
                            "ScenarioRuleClient.LockActiveBonus ran because the surrounding step committed). " +
                            "The flat game refuses the same un-click there (UIUseActiveBonus.ClearSelection " +
                            "does nothing while locked), so the card springs back into the recess instead of " +
                            "pretending the choice is still open. It leaves on its own the moment the game " +
                            "resolves the bonus.");
    }

    /// <summary>
    /// Give the game back a toggle this pile is about to stop being able to express — the
    /// active-bonus twin of <see cref="AbandonChoice"/>. Called from every teardown that drops the
    /// pending decision WITHOUT routing through <see cref="CancelPendingUse"/>'s normal path, so a
    /// toggled bonus can never survive the disappearance of the card that stands for it. A LOCKED
    /// toggle is left alone: the game refuses to release it, and it is about to resolve anyway.
    /// </summary>
    private void ReleaseBonusToggle(string why)
    {
        CActiveBonus? bonus = _pendingBonus;
        _pendingBonus = null;
        bool wasToggled = _pendingBonusToggled;
        _pendingBonusToggled = false;
        if (bonus == null || !wasToggled)
            return;
        try
        {
            if (CardsGameApi.ActiveBonusToggleLocked(bonus))
            {
                VRLog.Info("Cards", $"ITEM BONUS: '{CardsGameApi.BonusCardName(bonus)}' stays toggled ({why}) — " +
                                    "the game has LOCKED it, so there is nothing to give back.");
                return;
            }
            UIUseActiveBonus? slot = CardsGameApi.ActiveBonusSlot(bonus);
            if (slot != null && CardsGameApi.ActiveBonusSlotSelected(slot)
                && CardsGameApi.ClickActiveBonusSlot(slot))
            {
                VRLog.Info("Cards", $"ITEM BONUS released: '{CardsGameApi.BonusCardName(bonus)}' untoggled " +
                                    $"through the game's own row click ({why}) — peers get the untoggle from " +
                                    "the game's own ClickActiveBonusSlot action.");
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ITEM BONUS: releasing the toggle threw ({e.Message}); the card still leaves " +
                                "the recess.");
        }
    }

    /// <summary>
    /// Requirement 6 — CONFIRM: use the pending item through the game's own <c>UseItemService</c>
    /// (which owns ALL multiplayer sync + re-validates), and then hand the card over to the RESOLVING
    /// state (<see cref="BeginUseResolving"/>) — it stays lying in the recess until the ACTION the
    /// use started is finished, and only then plays its burn/tap flourish and collapses back into the
    /// deck. Invoked by the Confirm button's poke/laser callback. Inventory is never mutated here.
    ///
    /// <para>THE LINE THAT MOVED (user report 2026-08-09 — the heal potion that left the recess while
    /// "Ziele bestätigen" was still up): this method used to call <see cref="FinishUsedChip"/> in the
    /// statement after the seam. Calling the seam is not the end of the action — for an Ability item
    /// it is the start of one — so the finish now hangs off the game's own resolution instead
    /// (<see cref="CardsGameApi.ItemActionResolving"/>). Every REFUSAL below still ends the placement
    /// on the spot: nothing was used, so there is nothing to wait for.</para>
    /// </summary>
    private void ConfirmPendingUse()
    {
        // An ACTIVE-BONUS placement has its own confirm (ConfirmPendingBonus is what its cap is
        // wired to — the bonus is toggled through the bonus bar, never through UseItemService, which
        // refuses passive items outright). This guard exists only so a stale cap callback from a
        // previous placement can never route a bonus card into the wrong seam.
        if (_pendingBonus != null)
        {
            ConfirmPendingBonus();
            return;
        }

        ItemChip? chip = _pendingUseChip;
        bool subChoice = _pendingSubChoice;
        // The DECISION is over either way, so the cap and the choice bookkeeping go now. Whether the
        // CARD goes is decided at the bottom: a refused confirm clears _pendingUseChip on its own way
        // out, an accepted one has BeginUseResolving put it straight back (it never stopped being the
        // card lying in the recess, which is what _pendingUseChip means to every other reader).
        _pendingUseChip = null;
        _pendingSubChoice = false;
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        if (chip == null)
            return;

        CPlayerActor? actor = _hand != null ? _hand.PlayerActor : null;
        if (actor == null || chip.Item == null)
        {
            // ReturnPlacedChipHome, not ReturnToFan: the card may be lying in the recess with the
            // fan already CLOSED (the USE cap outlives the arc now — see _keptClip), and
            // ReturnToFan's glide is expressed in FAN-ROOT-local metres while the chip is still a
            // child of the SLOT. With no arc up that glide has no target it could mean.
            ReturnPlacedChipHome(chip, "the confirm found no actor / no item");
            return;
        }

        CItem item = chip.Item;
        string itemName = chip.name;
        try
        {
            string seam;
            if (subChoice)
            {
                // ELEMENT-CHOICE item: the ONLY correct confirm is the bar's own UseItem() — it
                // ships the chosen elements to peers (ClickItemBonusSlot + ItemToken), APPLIES them
                // (ConsumeOrInfuseIfPossible → ElementInfusionBoardManager.Infuse/Consume) and only
                // then calls UseItemService. A bare service call would spend the potion and create
                // nothing (CAbilityInfuse.DoInfuse filters "Any" out for player actors).
                if (!CardsGameApi.ItemsBarConfirmUse())
                {
                    // Nothing pending: the pick was cancelled/incomplete under us. Do NOT fall back
                    // to a bare use — that is the "spends the potion, creates nothing" trap.
                    VRLog.Info("Cards", $"ITEM USE refused for '{itemName}': the element choice is not complete " +
                                        "(the bar holds no pending item) — the card returns to the pile.");
                    ReturnPlacedChipHome(chip, "the element choice was not complete");
                    return;
                }
                seam = "items-bar UseItem() (element choice confirmed; elements shipped + applied)";
            }
            else
            {
                // Prefer the game's OWN items-bar slot click when a live slot exists (ShowUsableItems
                // keeps the hidden 2D bar populated during the turn) — byte-identical to the 2D click
                // AND to the game's own MP replay seam (ProxyUseItemBonus → slot.OnPointerDown,
                // UIUseItemsBar.cs:618). Unlike the direct service call it also auto-resolves
                // FIXED-element consumes (MultiElementPickController.Pick) before the wired
                // UseItemService runs. Only PLAIN slots reach here (a choice item takes the branch
                // above), so the click can never open a picker. Fallback: the direct service call
                // (owns the online GameAction send + local execution + re-validation), as before.
                UIUseItemScenario? slot = CardsGameApi.LiveItemsBarSlot(item);
                bool viaSlot = slot != null && !CardsGameApi.SlotNeedsSubChoice(slot)
                               && CardsGameApi.ClickItemsBarSlot(slot);
                if (!viaSlot)
                    new UseItemService(actor).UseItem(item);
                seam = viaSlot
                    ? "items-bar slot click (game's own 2D/proxy seam)"
                    : "UseItemService direct (no live bar slot)";
            }
            VRLog.Info("Cards", $"ITEM USE seam: {seam} for '{itemName}'.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ITEM USE failed for '{itemName}': {e.Message}");
            ReturnPlacedChipHome(chip, "the use seam threw");
            return;
        }

        BeginUseResolving(chip, item, "CONFIRM");
    }

    /// <summary>
    /// Reflect the RESULT of a use with an FX on the clipped card, then let it collapse back into the
    /// deck. Detaches it from the fan root + drops it from the live list so the flourish/collapse runs
    /// to completion even as the fan live-rebuilds to show the item's new (Spent/Consumed) state.
    /// Shared by the CONFIRM path and by the element pick that auto-uses its own card.
    ///
    /// Requirement 9b: the intended flourish is read from the item's STATIC usage TYPE
    /// (<c>YMLData.Usage</c>), NOT the live <c>SlotState</c> — online, the state change ships as a
    /// GameAction that resolves a frame or more LATER, so <c>SlotState</c> is still Useable/Selected
    /// the instant we return here and the old "spent = SlotState==Spent" read was false ⇒ no tap
    /// animation ever played. The usage type is authored config, stable pre/post use.
    /// </summary>
    private void FinishUsedChip(ItemChip chip, CItem item, string why)
    {
        // A HAND MAY NOT HAVE ITS CARD TAKEN AWAY. Every ordinary route here already declines while
        // the chip is held (TickUseResolving defers to the release, TickBonusDecision answers the
        // grab itself), so this is the belt for the backstops: releasing the grab first means the
        // detach below can never rip the card out of a closed fist — the defect UnclipFromSlot's
        // root-cause note documents at length — and it is the same order RetireChipToPile uses.
        //
        // RE-ENTRANT, and deliberately released BEFORE the fields are cleared: the cancel runs the
        // whole drop routing (OnRelease → OnChipReleased), which with the resolving state still set
        // simply puts the card back in the recess — the one answer that cannot conflict with what
        // this method is about to do. The collapse flag is the honest "it already went home by
        // another road" test, exactly as in RetireChipToPile.
        if (chip.Holder != null)
        {
            chip.Holder.Grabber.CancelAll();
            if (chip.IsCollapsing)
                return;
        }
        _pendingUseChip = null;
        _useResolving = false;
        _pendingSubChoice = false;
        _pendingChoiceSlot = null;
        _choiceClickArmed = false;
        _choiceCapShown = false;
        _choiceCapReady = false;
        // An ACTIVE-BONUS placement ends here too, and the toggle is deliberately NOT given back:
        // reaching this method means the game USED the bonus (it charged the item through
        // CActiveBonus.ActiveBonusUsed), so there is nothing to release — dropping the fields is the
        // whole job. Cleared directly rather than via ReleaseBonusToggle, which would try to
        // un-click a bonus the game has already spent.
        _pendingBonus = null;
        _pendingBonusToggled = false;
        chip.PendingUse = false;
        // The decision resolved, so this card is no longer the recess survivor (see _keptClip) —
        // it is about to detach and play its own flourish/collapse.
        if (ReferenceEquals(_keptClip, chip))
        {
            _keptClip = null;
            _keptClipIndex = -1;
        }

        CItem.EUsageType usage = item.YMLData != null ? item.YMLData.Usage : CItem.EUsageType.None;
        bool consumed = usage == CItem.EUsageType.Consumed
                        || item.SlotState == CItem.EItemSlotState.Consumed;
        bool spent = !consumed
                     && (usage == CItem.EUsageType.Spent
                         || item.SlotState == CItem.EItemSlotState.Spent);
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor;
        if (keep != null)
            chip.transform.SetParent(keep, worldPositionStays: true);
        _chips.Remove(chip);
        ForgetSweepWinner(chip);
        chip.PlayUseThenCollapse(consumed, spent, converge);

        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"ITEM USED {chip.name} ({why}; state now {item.SlotState}) — playing " +
                            $"{(consumed ? "burn" : spent ? "tap" : "use")} FX, then it returns to the deck.");
    }

    // ------------------------------------------------- item-surrender pick (event mali) --

    // The game's open ItemCardPicker being served (event consume/refresh demand, OR the
    // goal-chest "lose 1 item reward" forfeit — flow 1: the SAME window, discriminated by the
    // Choreographer wait state, see CardsGameApi.OpenLoseRewardPicker). The chip clipped into
    // the use slot mirrors the picker's selection; the slot's confirm button ("ITEM ABGEBEN"/
    // "ITEM AUFFRISCHEN"/"BELOHNUNG ABGEBEN", never "USE") commits through the owning picker's
    // own confirm seam. All game selection state lives in the PICKER — the mod only mirrors it.
    private bool _demandActive;
    private bool _demandRefreshing;
    private bool _demandLoseReward;  // flow 1: the fan shows REWARD items, commit = ItemRewardLosePicker
    private ItemChip? _demandChip;   // chip clipped into the slot for the current selection
    private float _demandCueAt;      // throttle for the "fan is closed, open the stack" cue line

    /// <summary>
    /// The picker the ACTIVE demand mirrors — the refresh/consume picker normally, the
    /// goal-chest forfeit picker while flow 1 owns the shared window. Every selection seam
    /// (drop-select, grab-back deselect, ready check) resolves through here so the two flows
    /// can never cross-talk on the one scene-serialized ItemCardPicker.
    /// </summary>
    private ItemCardPicker? ActiveDemandPicker() =>
        _demandLoseReward
            ? CardsGameApi.OpenLoseRewardPicker(out _)
            : CardsGameApi.OpenItemPicker(out _, out _);

    /// <summary>
    /// Flow 1 content override: while the goal-chest forfeit demand is live the fan presents
    /// the picker's REWARD items (never in any inventory — <see cref="CardsGameApi.LoseRewardItems"/>);
    /// null otherwise, and <see cref="Populate"/>/<see cref="Signature"/> fall back to the
    /// inventory as always.
    /// </summary>
    private List<CItem>? DemandItemsOverride() =>
        _demandActive && _demandLoseReward ? CardsGameApi.LoseRewardItems() : null;

    /// <summary>True while an item consume/refresh demand is being served (gates the normal
    /// action-turn use-slot logic in <see cref="Tick"/>).</summary>
    internal bool DemandActive => _demandActive;

    /// <summary>
    /// EVENT ITEM-SURRENDER pump, one call per frame from the driver (independent of the
    /// action-turn item flow): while the game's <c>ItemCardRefreshPicker</c> demands items
    /// from a locally-controlled actor (see <see cref="CardsGameApi.OpenItemPicker"/> — the
    /// flat picker window is invisible on VR's hidden 2D stack, the item twin of the
    /// card-discard deadlock), raise the ITEM FAN board-anchored, keep the board's item-use
    /// slot visible as the drop target, glue the selected chip to the slot, and surface the
    /// demand confirm. Cleans up the moment the picker closes (confirmed / game moved on).
    /// </summary>
    internal void TickDemandPick(CardsHandUI? hand)
    {
        ItemCardPicker? picker = CardsGameApi.OpenItemPicker(out CPlayerActor? actor, out bool refreshing);
        bool loseReward = false;
        if (picker == null)
        {
            // Flow 1 (goal-chest "lose 1 item reward"): the SAME window, owned by
            // ItemRewardLosePicker while the Choreographer waits in
            // WaitingForLoseGoalChestRewardSelection. Only the DECIDING client (host/offline
            // — the game's own CanSelect gate) raises the fan; guests get the wait banner
            // from UpdateItemDemandStatus and the host's commit replays via the game's own
            // ProxyItemRewardLose. No owning actor: any presented local hand anchors the fan.
            picker = CardsGameApi.OpenLoseRewardPicker(out bool canSelect);
            loseReward = picker != null;
            if (!canSelect)
                picker = null;
        }
        bool active = picker != null && hand != null
                      && (loseReward // forfeit: party-level pick, no actor to match
                          || (actor != null && ReferenceEquals(hand.PlayerActor, actor)
                              && (!FFSNetwork.IsOnline || actor.IsUnderMyControl)));
        if (!active)
        {
            if (_demandActive)
                EndDemand("picker closed — selection committed or the game moved on");
            return;
        }

        if (!_demandActive || _demandLoseReward != loseReward)
        {
            if (_demandActive) // owner flipped refresh↔forfeit without a closed frame — restart clean
                EndDemand("demand owner changed (refresh/consume ↔ goal-chest forfeit)");
            _demandActive = true;
            _demandRefreshing = !loseReward && refreshing;
            _demandLoseReward = loseReward;
            _demandCueAt = 0f;
            _signature = string.Empty; // content source may have flipped (inventory ↔ reward items)
            if (loseReward)
                VRLog.Info("Cards", "GOAL-CHEST FORFEIT pick OPEN: the game demands " +
                                    $"{CardsGameApi.ItemPickWanted(picker!)} earned reward item(s) back " +
                                    "(ItemRewardLosePicker → the same flat ItemCardPicker window, dead in VR; " +
                                    "Choreographer in WaitingForLoseGoalChestRewardSelection) — item fan raised " +
                                    "with the REWARD items, drop one into the board's item slot; the slot button " +
                                    "commits through ItemRewardLosePicker.ConfirmSelectedRewardItems.");
            else
                VRLog.Info("Cards", $"ITEM SURRENDER pick OPEN ({(refreshing ? "refresh" : "consume")}): the game demands " +
                                    $"{CardsGameApi.ItemPickWanted(picker!)} item(s) from " +
                                    $"'{(actor != null ? CardsGameApi.ActorLabel(actor) : "?")}' (ItemCardRefreshPicker; flat window is dead in " +
                                    "VR) — item fan raised, drop the demanded item into the board's item slot; the slot " +
                                    "button commits through the game's own picker confirm.");
        }

        // NO AUTO-OPEN (user report 2026-08-07). The demand used to raise the fan itself and
        // re-raise it every 0.5 s while it was live, which made the fan unclosable. The demand is
        // now advertised, not forced: the use slot below is the ask, the demand banner names it,
        // and the items STACK (whose usable-highlight embers are already running) is the one place
        // that opens the fan. Throttled cue line so the next hardware log proves which state the
        // fan was in during a demand.
        if (!IsOpen && Time.unscaledTime >= _demandCueAt)
        {
            _demandCueAt = Time.unscaledTime + 5f;
            VRLog.Info("Cards", "ITEM DEMAND waiting with the item fan CLOSED — poke (or laser-click) the " +
                                $"items stack to fan the candidates out (last close: {_lastCloseReason}). " +
                                "The mod never opens the fan by itself.");
        }

        // The use slot IS the ask — visible for the whole demand (Tick's action-turn gate is
        // bypassed while _demandActive, see there).
        PlayTray.Current?.SetItemUseSlotVisible(true);

        // Clipped-chip service: a grab-back DESELECTS through the picker's own slot seam; the
        // chip's own release then glides it home (or re-clips on a re-drop).
        ItemChip? chip = _demandChip;
        if (chip != null && chip.Item != null && chip.Holder != null)
        {
            CardsGameApi.ItemPickDeselect(picker!, chip.Item);
            UnclipChip(chip);
            chip.PendingUse = false;
            _demandChip = null;
            VRLog.Info("Cards", "ITEM SURRENDER: card grabbed back out of the slot — deselected through the " +
                                "game's ItemCardPickerSlot seam; drop an item again to choose.");
            chip = null;
        }
        if (chip != null)
        {
            Transform? slot = PlayTray.Current?.ItemUseSlotTransform;
            if (slot != null && chip.transform.parent != slot)
                chip.ClipIntoSlot(slot); // re-assert after a board rebuild
        }

        // Demand confirm: shown exactly while the PICKER reports the full selection (survives a
        // lost chip — e.g. the fan was closed and rebuilt — because the game selection is the
        // authority). Label says SURRENDER/FORFEIT, never "USE".
        bool ready = CardsGameApi.ItemPickReady(picker!);
        PlayTray.Current?.SetItemUseConfirmVisible(ready, ready ? ConfirmDemandPick : null,
            DemandConfirmLabel());
    }

    /// <summary>The demand confirm-cap wording per flow: refresh (positive pick), forfeit
    /// (flow 1 — the player GIVES UP an earned reward), or the consume surrender.</summary>
    private string DemandConfirmLabel() =>
        _demandLoseReward ? Core.Loc.Mod("item_lose_reward")
        : _demandRefreshing ? Core.Loc.Mod("item_refresh_confirm")
        : Core.Loc.Mod("item_surrender");

    /// <summary>Drop routing while a demand is active (see <see cref="OnChipReleased"/>).</summary>
    private void HandleDemandDrop(ItemChip chip, Vector3 dropWorldPos, Transform slot, VRHand vrHand)
    {
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — the base glide-home returns it to the fan

        ItemCardPicker? picker = ActiveDemandPicker();
        if (picker == null || chip.Item == null)
            return;
        if (!CardsGameApi.IsItemPickCandidate(picker, chip.Item))
        {
            VRLog.Info("Cards", $"ITEM SURRENDER: '{chip.name}' is NOT among the demanded candidates " +
                                "(the picker's own filter) — it returns to the fan.");
            return; // base glide-home
        }

        // A previous clipped selection returns to the fan; the select seam below swaps the
        // game selection (ItemPickSelect deselects the oldest when full — the picker's own
        // overflow rule).
        if (_demandChip != null && !ReferenceEquals(_demandChip, chip))
        {
            ItemChip old = _demandChip;
            _demandChip = null;
            if (old.Item != null)
                CardsGameApi.ItemPickDeselect(picker, old.Item);
            UnclipChip(old);
            old.PendingUse = false;
            old.ReturnToFan();
        }

        if (!CardsGameApi.ItemPickSelect(picker, chip.Item))
        {
            VRLog.Warn("Cards", $"ITEM SURRENDER: game picker REJECTED selecting '{chip.name}' " +
                                "(slot not selectable) — card returns to the fan.");
            return; // base glide-home
        }

        _demandChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide();
        chip.ClipIntoSlot(slot);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"ITEM SURRENDER clip-in: '{chip.name}' SELECTED through the game's " +
                            "ItemCardPickerSlot seam — press the slot button " +
                            $"('{DemandConfirmLabel()}') " +
                            "to commit, or grab it back to swap.");
    }

    /// <summary>
    /// Demand CONFIRM — commit through the game's own picker confirm
    /// (<see cref="CardsGameApi.ConfirmItemPick"/>: Inventory.UseItem/ReactivateItem +
    /// GameActionType.ConsumeItem/RefreshItem with the game's ItemsToken, picker hidden,
    /// Choreographer released). On success the clipped chip plays the same result FX as a
    /// normal use (burn plume for a consumed item, tap for spent/refreshed) and collapses
    /// back into the deck — the surrender is VISIBLE, not a silent vanish.
    /// </summary>
    private void ConfirmDemandPick()
    {
        ItemChip? chip = _demandChip;
        _demandChip = null;
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        // Flow 1 commits through ITS owning picker (ItemRewardLosePicker.ConfirmSelectedRewardItems
        // — RewardGroup removal + GameActionType.LoseItemReward + StepComplete); the refresh/
        // consume demand through ItemCardRefreshPicker.ConfirmSelectedCards as before.
        bool fired = _demandLoseReward
            ? CardsGameApi.ConfirmLoseRewardPick()
            : CardsGameApi.ConfirmItemPick();
        VRLog.Info("Cards", (_demandLoseReward ? "GOAL-CHEST FORFEIT CONFIRM → " : "ITEM SURRENDER CONFIRM → ") +
                            "game picker confirm " +
                            (fired ? (_demandLoseReward
                                         ? "accepted (Reward removed from every RewardGroup + GameActionType." +
                                           "LoseItemReward IndexToken — outcome networked by the game, Choreographer released)."
                                         : "accepted (Inventory.UseItem/ReactivateItem + GameActionType.ConsumeItem/" +
                                           "RefreshItem via the game's ItemsToken — outcome networked by the game).")
                                   : "rejected (selection incomplete or picker already closed)."));
        if (chip == null)
            return;
        if (!fired || chip.Item == null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            chip.ReturnToFan();
            return;
        }

        // Result FX, mirrored from ConfirmPendingUse: consumed → burn out of the slot;
        // spent/refreshed → tap. Usage type is authored config (stable pre/post the
        // networked state change — the same 9b lesson as the use flow). A FORFEITED reward
        // item always burns: it leaves the party for good, whatever its usage type says.
        CItem item = chip.Item;
        CItem.EUsageType usage = item.YMLData != null ? item.YMLData.Usage : CItem.EUsageType.None;
        bool consumed = _demandLoseReward
                        || (!_demandRefreshing
                            && (usage == CItem.EUsageType.Consumed
                                || item.SlotState == CItem.EItemSlotState.Consumed));
        bool spent = !consumed;
        Vector3 converge = PileConvergeWorld();
        Transform? keep = PlayTray.Current?.Root != null ? PlayTray.Current!.Root : _anchor;
        if (keep != null)
            chip.transform.SetParent(keep, worldPositionStays: true);
        _chips.Remove(chip);
        ForgetSweepWinner(chip);
        chip.PendingUse = false;
        chip.PlayUseThenCollapse(consumed, spent, converge);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
    }

    /// <summary>Demand teardown (picker closed): unclip/return any leftover chip, drop the
    /// confirm + slot, restore the normal action-turn gates. The fan stays as-is — the live
    /// rebuild shows the item's new state; the player closes it like any browse.</summary>
    private void EndDemand(string why)
    {
        _demandActive = false;
        bool wasLoseReward = _demandLoseReward;
        _demandLoseReward = false;
        if (wasLoseReward)
            _signature = string.Empty; // fan content flips back from REWARD items to the inventory
        ItemChip? chip = _demandChip;
        _demandChip = null;
        if (chip != null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            if (chip.Holder == null && chip.gameObject != null && chip.gameObject.activeInHierarchy)
                chip.ReturnToFan();
        }
        PlayTray.Current?.SetItemUseConfirmVisible(false, null);
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"ITEM SURRENDER pick END ({why}).");
    }

    // ------------------------------------------------- take-damage shield place (req C) --

    // TAKE-DAMAGE decision context: while TakeDamagePanel presents the attacked actor's
    // OnAttacked shield/retaliate items on the (hidden) UIUseItemsBar
    // (TakeDamagePanel.Show → ShowItems(actorBeingAttacked, OnAttacked-filter,
    // ToggleShieldItem, clear:true), TakeDamagePanel.cs:249-264), placing the item CARD into
    // the board's use slot toggles the item through the game's own slot seam — the exact 2D
    // click (slot.OnPointerDown → Toggle → onSelect/onUnselect → TakeDamagePanel.
    // ToggleShieldItem → Inventory.SelectItem/DeselectItem). Grabbing the card back OUT
    // toggles it off the same way. There is NO extra board confirm: the panel's own
    // TakeDamage button (docked by DecisionDockSurface) commits — that confirm carries the
    // whole selection online in one GameActionType.TakeDamage ItemsToken
    // (TakeDamagePanel.cs:770-775; per-click sync is deliberately absent in the 2D flow too,
    // the ShowItems wrapper skips the send during TakeDamageConfirmation). Zero wire changes.
    private bool _tdActive;
    private ItemChip? _tdChip;      // chip clipped into the slot = the toggled shield item
    private float _tdCueAt;         // throttle for the "fan is closed, open the stack" cue line

    /// <summary>
    /// TAKE-DAMAGE place pump, one call per frame from the driver (sibling of
    /// <see cref="TickDemandPick"/>): while an open, locally-decided take-damage decision
    /// presents OnAttacked items for the PRESENTED hand's actor
    /// (<see cref="CardsGameApi.TakeDamagePlaceContext"/> — the presented hand itself follows
    /// the attacked actor via CardsGameApi.TakeDamageHand), gate the use slot on a held
    /// candidate and service the clipped chip (grab-back = toggle off through the panel's own
    /// slot seam). Ends the moment the panel closes — the game's confirm already committed (or
    /// discarded) the selection.
    ///
    /// NO AUTO-OPEN (user report 2026-08-07: "als der Charakter Schaden bekam ging der
    /// Gegenstands-Fächer von selbst auf und liess sich nie wieder schliessen"). This pump used
    /// to <c>Open</c> the fan on the first frame of the decision and RE-open it every 0.5 s for
    /// as long as the panel stayed up — so the stack poke, the click-away and every foreign-
    /// interaction close were all undone within half a second, and the hardware log shows the
    /// exact churn (CLOSE → CLOSE → "OPEN … opened by auto/flow" triplets, e.g. LogOutput.log
    /// 15732-15734 / 15906-15908 / 20748-20750, plus ~15 cycles on the peer). Placing a shield
    /// item is OPTIONAL, so nothing is lost by requiring the deliberate stack poke; the item
    /// stack's usable-highlight embers already advertise that there is something to play.
    /// </summary>
    internal void TickTakeDamagePick(CardsHandUI? hand)
    {
        bool active = hand != null && !_demandActive && CardsGameApi.TakeDamagePlaceContext(hand);
        if (!active)
        {
            if (_tdActive)
                EndTakeDamagePick("panel closed / context lost — the panel confirm owns the committed selection");
            return;
        }

        if (!_tdActive)
        {
            _tdActive = true;
            _tdCueAt = 0f;
            VRLog.Info("Cards", "TAKE-DAMAGE item place ARMED: the damage decision presents OnAttacked " +
                                "shield/retaliate items (TakeDamagePanel → hidden UIUseItemsBar; plain " +
                                "symbols are filtered from the docked bar). The item fan is NOT raised " +
                                $"automatically (fan currently {(IsOpen ? "OPEN" : "closed")}) — poke the items " +
                                "stack, then place a shield card into the board's item slot to toggle it; grab " +
                                "it back out to untoggle; the panel's own TakeDamage confirm commits.");
        }

        // NO AUTO-OPEN — see the method doc. One throttled cue instead, so a hardware log still
        // proves whether the fan was available during the decision.
        if (!IsOpen && Time.unscaledTime >= _tdCueAt)
        {
            _tdCueAt = Time.unscaledTime + 5f;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: shield candidates exist but the item fan is CLOSED " +
                                $"(last close: {_lastCloseReason}) — poke the items stack to fan them out. " +
                                "Placing a shield is optional; the mod never opens the fan by itself.");
        }

        // ACTIVE-BONUS placement owns the recess while it is live, and it can legitimately be live
        // right here: a worn item's optional prevent-damage bonus is offered on UIActiveBonusBar
        // during exactly this decision. Its own service runs from Tick/TickPlacedWhileClosed
        // (TickBonusDecision) and writes the recess visibility itself, so this pump must not fight
        // it — two writers of SetItemUseSlotVisible would flicker the overlay every frame.
        if (_pendingBonus != null && _pendingUseChip != null)
            return;

        // Use slot: visible while a candidate chip is HELD or one is clipped (the toggle).
        // A held card whose ACTIVE BONUS is on offer counts as a candidate too — its item has no
        // OnAttacked items-bar slot (it is passive), so HeldTakeDamageCandidate can never see it,
        // and without this the recess would simply not appear for the one card the player is
        // holding out over it.
        ItemChip? held = HeldTakeDamageCandidate() ?? HeldBonusChip();
        bool show = held != null || _tdChip != null;
        PlayTray.Current?.SetItemUseSlotVisible(show);
        TickUseGhost(show && _tdChip == null, held);

        // Clipped-chip service.
        ItemChip? chip = _tdChip;
        if (chip == null)
            return;
        if (chip.Item == null)
        {
            _tdChip = null;
            return;
        }
        if (chip.Holder != null)
        {
            // Grabbed back OUT = toggle the shield item OFF through the same slot seam.
            UIUseItemScenario? slot = CardsGameApi.LiveItemsBarSlot(chip.Item);
            if (slot != null && chip.Item.SlotState == CItem.EItemSlotState.Selected)
                CardsGameApi.ClickItemsBarSlot(slot);
            UnclipChip(chip);
            chip.PendingUse = false;
            _tdChip = null;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: card grabbed back out of the slot — shield item " +
                                "untoggled through the panel's own slot seam.");
            return;
        }
        // The game moved the selection out from under the clip (e.g. DeselectAllShieldItems on
        // a lethal recalc): return the card to the fan so card and state never disagree.
        if (chip.Item.SlotState != CItem.EItemSlotState.Selected)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            chip.ReturnToFan();
            _tdChip = null;
            VRLog.Info("Cards", "TAKE-DAMAGE item place: the game deselected the shield item — card returns to the fan.");
            return;
        }
        // Re-assert the clip after a board rebuild recreated the slot transform.
        Transform? useSlot = PlayTray.Current?.ItemUseSlotTransform;
        if (useSlot != null && _root != null && chip.transform.parent != useSlot)
            chip.ClipIntoSlot(useSlot);
    }

    /// <summary>The single HELD chip that is a live take-damage candidate — its item has a
    /// visible slot on the panel-populated items bar (the game's own OnAttacked filter +
    /// CanConsume gate decided candidacy; the mod adds nothing).</summary>
    private ItemChip? HeldTakeDamageCandidate()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder != null && c.Item != null
                && CardsGameApi.LiveItemsBarSlot(c.Item) != null)
                return c;
        }
        return null;
    }

    /// <summary>The single HELD chip whose item has a LIVE OFFERED, option-less ACTIVE BONUS — the
    /// Brille in the player's hand. Its own placement predicate (CardsGameApi.PlaceableBonusForItem)
    /// is the whole gate; the mod adds nothing to what the game already decided to offer.</summary>
    private ItemChip? HeldBonusChip()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            ItemChip c = _chips[i];
            if (c != null && c.Holder != null && c.HasOfferedBonus)
                return c;
        }
        return null;
    }

    /// <summary>Drop routing while the take-damage context is active (see <see cref="OnChipReleased"/>):
    /// clip + TOGGLE ON through the panel's own slot seam; a second card swaps (the previous one is
    /// untoggled and returns to the fan — one clipped card mirrors one toggled item, so grab-back
    /// stays an exact inverse). A non-candidate drop glides home untouched.</summary>
    private void HandleTakeDamageDrop(ItemChip chip, Vector3 dropWorldPos, Transform slot, VRHand vrHand)
    {
        float scale = slot.lossyScale.x;
        float radius = UseSlotRadius * (scale > 1e-4f ? scale : 1f);
        if ((dropWorldPos - slot.position).sqrMagnitude > radius * radius)
            return; // dropped away from the slot — the base glide-home returns it to the fan

        if (chip.Item == null)
            return;
        UIUseItemScenario? barSlot = CardsGameApi.LiveItemsBarSlot(chip.Item);
        if (barSlot == null)
        {
            VRLog.Info("Cards", $"TAKE-DAMAGE item place: '{chip.name}' is NOT among the OnAttacked candidates " +
                                "(the panel's own filter) — it returns to the fan.");
            return; // base glide-home
        }

        // Swap: untoggle + return a previously clipped selection first (1:1 card ↔ toggle).
        if (_tdChip != null && !ReferenceEquals(_tdChip, chip))
        {
            ItemChip old = _tdChip;
            _tdChip = null;
            if (old.Item != null && old.Item.SlotState == CItem.EItemSlotState.Selected)
            {
                UIUseItemScenario? oldSlot = CardsGameApi.LiveItemsBarSlot(old.Item);
                if (oldSlot != null)
                    CardsGameApi.ClickItemsBarSlot(oldSlot);
            }
            UnclipChip(old);
            old.PendingUse = false;
            old.ReturnToFan();
        }

        // Toggle ON through the game's own click seam (idempotent on a re-drop of an already
        // selected item). Verify against the INVENTORY truth (SlotState), not the widget flag.
        if (chip.Item.SlotState != CItem.EItemSlotState.Selected)
        {
            if (!CardsGameApi.ClickItemsBarSlot(barSlot)
                || chip.Item.SlotState != CItem.EItemSlotState.Selected)
            {
                VRLog.Warn("Cards", $"TAKE-DAMAGE item place: the game rejected toggling '{chip.name}' " +
                                    "(slot state gate) — card returns to the fan.");
                return; // base glide-home
            }
        }

        _tdChip = chip;
        chip.PendingUse = true;
        chip.CancelReleaseGlide();
        chip.ClipIntoSlot(slot);
        vrHand.SendHaptic(HapticPreset.HoverTick);
        CardsDriver.PlayCardSound(CardsConfig.CardPlaceSound.Value, chip.transform);
        VRLog.Info("Cards", $"TAKE-DAMAGE item place: '{chip.name}' TOGGLED through the panel's own slot seam " +
                            "(ToggleShieldItem) — grab it back out to untoggle; the panel's TakeDamage " +
                            "confirm commits (and syncs) the selection.");
    }

    /// <summary>Take-damage context teardown (panel closed / context lost): the game's confirm
    /// already committed or discarded the selection, so the chip is only returned visually —
    /// nothing is toggled here. The fan stays up; its live rebuild shows the items' new state.</summary>
    private void EndTakeDamagePick(string why)
    {
        _tdActive = false;
        ItemChip? chip = _tdChip;
        _tdChip = null;
        if (chip != null)
        {
            UnclipChip(chip);
            chip.PendingUse = false;
            if (chip.Holder == null && chip.gameObject != null && chip.gameObject.activeInHierarchy)
                chip.ReturnToFan();
        }
        PlayTray.Current?.SetItemUseSlotVisible(false);
        _useSlotShownLogged = false;
        VRLog.Info("Cards", $"TAKE-DAMAGE item place END ({why}).");
    }

    // ================================================================== item chip ==

    /// <summary>
    /// One physical item card in the pile. Renders the REAL <c>ItemCardUI</c> face (game art +
    /// name; async background via <c>ImageAddressableLoader</c>) hosted on a world-space canvas,
    /// with a legacy colored slab + name as the fallback. Supports the full ability-card
    /// interaction set: fingertip hand-sweep POP (<see cref="SetFingertipPop"/>), dominant-hand
    /// LASER hover+pluck (the <see cref="IPokeable"/> seam, driven by the driver's geometric
    /// item-fan pick — see <see cref="ItemsPile.TryLaserRaycast"/>),
    /// pinch-GRAB, and read-in-hand (<see cref="GetHeldPose"/>). Spent/consumed chips show the
    /// hosted card's own state FX (UpdateState); consumed chips carry NO separate burn plume.
    /// </summary>
    internal sealed class ItemChip : GrabbableBehaviour, IPokeable, IGrabbableHandFilter,
        IGrabHighlight, IFanSweepTarget
    {
        internal enum Visual { Ready, Spent, Consumed }

        internal Visual State { get; private set; }

        /// <summary>The live inventory item this chip represents (read-only — the ONLY write is the
        /// single <c>UseItemService.UseItem</c> the owner makes on a clip-in-to-use).</summary>
        internal CItem? Item { get; private set; }

        /// <summary>
        /// LIVE (never cached — hardware bug 3): can this item be USED right now? non-passive AND
        /// Useable/Selected — the exact gate <c>UseItemService.UseItem</c> enforces. Read every
        /// tick by the owner's use-slot gate + the clip-in-to-use path.
        /// </summary>
        internal bool IsActivatable => IsItemActivatable(Item);

        /// <summary>
        /// LIVE: is this item's ACTIVE BONUS being offered right now (the Brille asking, per
        /// triggering event, whether to spend itself)? The SECOND way an item can be played, and
        /// disjoint from <see cref="IsActivatable"/> by construction — such an item is
        /// <c>Trigger: PassiveEffect</c>, which that predicate rejects, and its question is asked on
        /// <c>UIActiveBonusBar</c> instead of on the items bar. Never cached: the offer appears and
        /// disappears with the game's own bar population, several times per turn.
        /// </summary>
        /// <para>Scoped to the OWNING actor (<see cref="ItemsPile.OwnerActor"/>): the bar is matched
        /// by item CARD id, which two characters can share, so an unscoped ask would light this chip
        /// up for a bonus that belongs to somebody else — see
        /// <c>CardsGameApi.PlaceableBonusForItem</c>.</para>
        internal bool HasOfferedBonus =>
            CardsGameApi.PlaceableBonusForItem(Item, _owner != null ? _owner.OwnerActor : null) != null;

        // ---- THE LIFT AN ABILITY CARD GIVES, TERM FOR TERM (user report 2026-08-09) ------------
        //
        // "Mir gefällt der neue Gegenstandsoverlay sehr gut, ich möchte das dort das selbe Feedback
        //  implementiert ist wie beim anderen Kartenoverlay auch, also Controller-Vibrationen, aber
        //  auch visuell. So gehen im normalen Overlay die Karten nach oben bzw. highlighten wenn man
        //  physisch dran ist oder mit dem Laser drüber hovered um anzuzeigen dass man sie nehmen
        //  kann. Gleiche die Experience hier an."
        //
        // WHAT WAS ACTUALLY DIFFERENT. The chip already had a pop, a single-winner election and a
        // laser hover — the hooks were all here, which is why this reads as a tuning gap rather than
        // a missing feature. Three numbers, all of them private re-inventions of numbers the ability
        // card already carries, made it read as a different language:
        //   * NO UPWARD COMPONENT AT ALL. VRCard's pop is `(0, 0.012, -FanSelectedPopForward)`; the
        //     chip's was `(0, 0, -0.02)`. The card literally rises out of the arc — which is the half
        //     of the report the user names first ("gehen die Karten nach oben") — and the chip only
        //     ever crept toward the eye, a motion that is nearly invisible head-on because a card
        //     coming straight at you changes no silhouette, only its size.
        //   * A SMALLER FORWARD PUSH from a PARALLEL constant: 0.02 m against the ability fan's
        //     authored [Cards] FanSelectedPopForward (0.035 m). That dial means exactly this — "how
        //     far a lifted card comes toward the viewer" — so the chip now READS it instead of
        //     mirroring it badly. One dial, both fans, one thing to tune, and it is already on the
        //     wire (extension record 28, id 75), so a peer's mirrored fan lifts by the owner's own
        //     number for free. This is the mirrored-constant lesson applied: a second set of numbers
        //     for the same idea is how the two fans drifted apart in the first place.
        //   * TWICE THE RAMP RATE (16/s vs VRCard's 8/s). Same tween, same MoveTowards form — the
        //     chip simply snapped where the card eases, which is what makes an otherwise identical
        //     lift feel like a different mechanism.
        //
        // The +18 % enlargement was already VRCard's number and is unchanged.
        private const float PopScale = 1.18f;

        /// <summary>
        /// The UPWARD component of the lift, in fan-local metres — <c>VRCard</c>'s own 0.012 m
        /// (see its home-pose update; <c>Net.RemoteBrowserFan.PopUp</c> mirrors the same value for
        /// the browse arc). Deliberately a shared authored CONSTANT rather than a new [Cards] dial:
        /// the ability side has never had one either, and minting a second dial for the ability
        /// card's own number is precisely the parallel set this change exists to remove.
        /// </summary>
        private const float PopUp = 0.012f;

        /// <summary>VRCard's pop RAMP rate (units/second, MoveTowards) — see the block above.</summary>
        private const float PopLerpSpeed = 8f;

        /// <summary>Grab-box margin around the rendered face (card-local metres) — a little slack
        /// for easy laser/finger targeting. Named because <see cref="SetGrabStrip"/> has to rebuild
        /// the box from the face size every layout and must not drift from <see cref="Create"/>.</summary>
        private const float ColliderMargin = 0.006f;

        /// <summary>Grab-box depth (card-local metres): the chip is a thin plate.</summary>
        private const float ColliderDepth = 0.02f;

        private ItemsPile? _owner; // for the clip-in-to-use callback on release

        /// <summary>The pile that built this chip. Published so the hand-to-hand transfer detector
        /// (<c>CardsDriver.UpdateHeldCardTransfer</c>) commits through THIS chip's owner rather than
        /// through the static <see cref="Current"/> — the two are the same object today, and a
        /// handover that set the mid-transfer flag on the wrong instance would silently drop the
        /// card instead of handing it over.</summary>
        internal ItemsPile? Owner => _owner;
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private float _homeScale = 1f;
        private BoxCollider? _box;
        private GameObject? _cardGo;    // hosted ItemCardUI GameObject (recycled to the pool on disable)
        private ItemCardUI? _cardUI;
        private SmokeClamp[]? _smokeClamps; // ItemCardEffects emitters bounded card-local; restored before recycle
        private bool _fingerPopped;
        private bool _laserPopped;

        /// <summary>
        /// THE HAND-PROXIMITY POP OF THE CARD LYING IN THE USE RECESS — set by
        /// <see cref="OnGrabHighlight"/>, i.e. by <c>ProximityGrabber</c> electing this chip as the
        /// hand's grab candidate. Read ONLY through <see cref="RecessHighlighted"/>; the chips
        /// standing in the ARC keep their own channel (<see cref="_fingerPopped"/>, written by the
        /// owner's single-winner sweep) and this flag is inert for them.
        ///
        /// <para>WHY A SEPARATE FLAG AND NOT THE SWEEP (user report 2026-08-09: "Ich will das die
        /// Gegenstandskarte die auf dem Overlay liegt auch nach oben hinweg gehighlighted wird wenn
        /// man mit dem Laser drüberfährt oder mit der Hand hinkommt … Soll sich da also gleich
        /// verhalten vom Feedback."). A clipped chip is excluded from the arc sweep BY CONSTRUCTION —
        /// <c>IFanSweepTarget.SweepEligible</c> is <c>Holder == null &amp;&amp; !PendingUse</c> — and that
        /// exclusion is load-bearing, not an oversight: the sweep decides the arc's LAYOUT (which
        /// chip is the split pivot), the mirrored highlight INDEX and the single-winner grab gate,
        /// and a card that is not at an arc position must take part in none of those. So the card in
        /// the recess is given its own contact signal instead of being let back in.</para>
        ///
        /// <para>AND THE SIGNAL IS THE ONE THAT ALREADY EXISTS FOR THIS STATE, not a second one:
        /// <c>ItemsPile.HandOwnedChip</c>'s first branch — "the grabber's highlight IS the clipped
        /// chip" — is precisely "this hand is on the placed card", and it is what already routes the
        /// trigger and the laser stand-down for it. <see cref="IGrabHighlight"/> is that branch AT ITS
        /// SOURCE: the very event <c>ProximityGrabber.SetHighlighted</c> raises, on the very edge it
        /// already plays its <see cref="HapticPreset.HoverTick"/> on. That makes the buzz and the
        /// lift the SAME event by construction — which is, word for word, the property
        /// <c>UpdateHandSweep</c>'s haptic note names as the reason the ability card feels different
        /// ("VRCard implements IGrabHighlight, so the grabber's tick and the card's pop are the same
        /// event by construction"). A docked ability card's hand-proximity lift comes from exactly
        /// this hook and nothing else; the placed item card now uses the identical one.</para>
        /// </summary>
        private bool _recessPopped;
        private float _pop; // smoothed 0..1
        // Actual rendered card size (item aspect) — set by TryHostRealCard, drives the backing + collider.
        private float _faceWidth;
        private float _faceHeight;

        /// <summary>Rendered face width in card-local metres (item aspect — NOT the ability-card
        /// aspect). Falls back to the ability-card width before the real card has been hosted.
        /// Read by <see cref="WorldUI.AvatarMirror"/> so a mirrored item slab keeps the item's own,
        /// near-square shape instead of being stretched to the ability-card ratio.</summary>
        internal float FaceWidth => _faceWidth > 0.001f ? _faceWidth : CardsConfig.CardWidth.Value;

        /// <summary>Rendered face height in card-local metres (see <see cref="FaceWidth"/>).</summary>
        internal float FaceHeight => _faceHeight > 0.001f ? _faceHeight : CardsConfig.CardHeight;

        // ITEM #1 (de-shimmer): the hosted ItemCardUI's cardBackground art loads ASYNC, so — exactly
        // like the ability cards' CardFace — its mipless-atlas sprites must be swapped for mip-baked
        // equivalents on host AND re-scanned on a slow cadence until the art has arrived (CardFace.cs
        // uses MipRescanInterval=1s and re-runs forever, because async arrivals / state changes keep
        // putting mipless originals back). RestoreSprites runs before the widget is recycled to the pool
        // so the pooled card is left clean. The bake caches are static + content-keyed, so the item
        // sprites share the ability cards' baked atlases for free (see CardFaceMipBake).
        private const float MipRescanInterval = 1f;
        private float _nextMipRescan;

        // ITEM #1 (de-shimmer immediately): the 1 s maintenance cadence above lands the FIRST successful
        // bake up to ~1.5 s after the fan opens (the art loads async, the periodic Rescan then catches
        // it), so the card visibly shimmers until then. Close that window: for the first ~2 s after host
        // poll the hosted card's background sprite every frame (a cheap null check) and Rescan the INSTANT
        // the async art appears — no visible shimmer window once the art is present. Then the 1 s cadence
        // takes over for ongoing state-change maintenance.
        private const float TightArtPollSeconds = 2f;
        private float _tightArtPollUntil;
        private bool _artBaked;

        // USABLE HIGHLIGHT (replaces the former de-emphasis dim — see ROOT CAUSE below).
        //
        // WHAT CHANGED AND WHY: the first two attempts at this cue worked the NEGATIVE way round —
        // de-emphasise everything you cannot play. Attempt 1 (a CanvasGroup alpha on the hosted card)
        // was invisible because the game's own ItemCardUI.OnReturnedToPool DISABLES the card's
        // CanvasGroup (component.enabled = false, ItemCardUI.cs:350), so a pooled card handed back to
        // us ignores every alpha we write. Attempt 2 (a mod-owned dark quad over the face) was visible
        // but wrong on its own terms: with most items passive or off-turn, the fan was mostly grey
        // sludge, the art stopped reading, and the player had to infer the playable cards from the
        // ABSENCE of a veil. The cue is now POSITIVE — light up exactly the cards you CAN play and
        // leave every other card at its natural, fully legible look.
        //
        // MECHANISM (round 2 — the gold glow QUAD is gone, see BuildUsableFrame): the cue is now the
        // SAME soft, breathing OUTLINE the initiative order wears for "this hero still has to choose"
        // (WorldUI SoftCueArt.FrameSprite + SoftFramePulse, extracted from InitiativeSelectionGlow) —
        // a hollow 9-sliced frame in the card's margin, with rounded corners so it hugs the card
        // instead of boxing it in. The user rejected the previous flat gold overlay outright and named
        // that initiative frame as the reference, so the two now share one sprite recipe and one breath.
        // Fully mod-owned: a child canvas of OUR chip GameObject, destroyed with the chip; the hosted
        // game ItemCardUI is never touched, so the widget goes back to the ObjectPool untouched.
        // Toggled LIVE (never cached) from the owner's turn-aware CanUseNow. -1 = "not yet applied".
        private GameObject? _usableFrame;  // mod-owned soft gold outline framing USABLE item cards
        private int _usabilityShown = -1;  // last applied state: -1 none, 0 normal, 1 highlighted

        // ITEM #3 (desktop mirror): the hosted card's world-space FaceCanvas. VRCard binds its face
        // canvas' worldCamera to the head camera every frame (VRCard.UpdateCanvasCamera); ItemsPile
        // never did, so a WorldSpace canvas with a null worldCamera culled/sorted differently per
        // camera and the item face was absent from the desktop mirror while ability cards showed.
        // Binding it to VRRigDriver.HeadCamera makes the item face render exactly like the ability
        // face (both faces are already on the same authored UI layer; the mod bodies on the mod layer).
        private Canvas? _faceCanvas;

        // FIX 1 (held pose) — the in-hand pinch target captured at grab time, in GrabAnchor-local
        // space; TickHeldPose lerps toward it each frame while also billboarding the face to the head
        // (mirror of VRCard._heldPos/_heldRot/_heldScale + VRCard.TickHeldPose).
        private Vector3 _heldPos;
        private Quaternion _heldRot = Quaternion.identity;
        private float _heldScale = 1f;

        /// <summary>FIX 1 — fraction of the card height between the bottom edge and the pinch anchor
        /// (mirror of VRCard.PinchGripFraction): the fingers grip ~12 % up from the card bottom.</summary>
        private const float PinchGripFraction = 0.12f;

        /// <summary>FIX 2 — unscaled seconds the post-release home glide runs (mirror of
        /// VRCard.ReleaseGlideSeconds): keeps the exponential home-lerp flying while the game may pause
        /// simulation time. <see cref="_releaseGlide"/> counts it down; a re-grab cancels it.</summary>
        private const float ReleaseGlideSeconds = 0.35f;
        private float _releaseGlide;

        // Requirement 5 (emerge-out-of-pile), PRESENCE PASS 2026-08-08. The fly-out used to BORROW the
        // post-release home glide: BeginEmerge dropped the chip on the stack point at 0.35× and set
        // _releaseGlide, and Update's exponential lerp did the rest. That is why the animation read as
        // "zu dezent" in mixed reality — an exponential is a curve with no end (it only decelerates,
        // asymptotically, so there is no moment the eye can call the arrival), it cannot express an
        // arc because it always takes the straight chord, and every chip ran the same one at the same
        // time. The fly-out is its own PARAMETRIC animation now: a normalized 0..1 clock per chip, an
        // ease-out-BACK (an overshoot, i.e. a reversal of direction — the loudest event motion has,
        // and it costs no extra travel), a mid-flight bow toward the viewer, and a roll it unwinds
        // from. Everything is expressed against the chip's LIVE home pose, read fresh every frame, so
        // a re-layout mid-flight (hand sweep, live refresh) moves the target rather than snapping the
        // chip — the standing "everything that moves must move WITH the animation" ruling.
        private bool _emerging;
        private float _emergeTime;          // unscaled seconds since the fan opened (own clock per chip)
        private float _emergeDelay;         // this chip's place in the centre-out ripple
        private Vector3 _emergeFrom;        // the pile stack point, ROOT-LOCAL (rides the board)
        private Quaternion _emergeSpin = Quaternion.identity; // the roll it unwinds FROM, home-relative

        // Requirement 5 (collapse-into-pile): a closing chip is detached from the fan root by the owner
        // and self-glides (WORLD space) into the pile stack point, then destroys itself — its OnDisable
        // recycles the hosted ItemCardUI back to the pool, so the collapse never leaks a card widget.
        // PRESENCE PASS: same treatment in reverse — an own clock with a per-chip delay, an ease-in-BACK
        // (the chip winds up AWAY from the stack before it is pulled in), and the roll wound back on.
        // The from-pose is captured once at the close so the whole thing survives the fan root going
        // inactive on the very same frame.
        private bool _collapsing;
        private Vector3 _collapseWorld;
        private Vector3 _collapseFrom;      // world pose captured at the close (already re-parented out)
        private Quaternion _collapseFromRot;
        private float _collapseFromScale;
        private float _collapseTime;        // counts UP; the chip dies at _collapseDelay + duration
        private float _collapseDelay;
        private Quaternion _collapseSpin = Quaternion.identity;

        // Requirement 6 (clip-in decision): while a released usable chip is CLIPPED into the use slot
        // awaiting a Confirm/cancel decision, PendingUse is set and the chip is RE-PARENTED onto the
        // slot (ClipIntoSlot) so the hierarchy holds it there rigidly — it used to be chased toward a
        // per-tick target instead, which is what made it swim behind head movement. The chip stays
        // grabbable so the player can grab it BACK OUT to cancel (the #6 refinement); the grab hands it
        // back to the fan root first, so on release it glides to the fan as always.
        internal bool PendingUse { get; set; }
        // (There is no _hasClip flag any more: the clip/unclip rework left behind a field that was
        // written false in three places and never read — CS0414. The hierarchy owns the clipped
        // pose now, so there is nothing for a flag to gate.)

        /// <summary>Requirement 5a — unscaled seconds the clip-in SETTLE runs (same family as
        /// <see cref="ReleaseGlideSeconds"/>): the window in which the card eases from the release
        /// pose into the recess's own frame. Short enough to read as "it snaps into place", long
        /// enough that it is a movement and not a pop.</summary>
        private const float ClipSettleSeconds = 0.28f;
        private float _clipSettle;
        private float _clipScale = 1f;

        // Requirement 6 (use FX): after a CONFIRM the owner detaches the chip and plays a brief flourish
        // reflecting the result — a burn plume (Consumed) or a "tap" roll to 90° with a scale pulse
        // (Spent) — then the chip collapses into the deck. Independent of the collapse/pending states.
        private bool _useFxActive;
        private float _useFxTime;
        private bool _useFxSpent;
        private Vector3 _useFxCollapseWorld;
        private Quaternion _useFxBaseRot = Quaternion.identity;
        private const float UseFxSeconds = 0.55f;

        private static bool s_loggedRealCard;
        private static bool s_loggedBacking; // one-line confirm of the backing source reused (FIX 3)
        private static bool s_loggedMirror;  // one-line confirm of the desktop-mirror canvas-camera bind (#3)

        internal static ItemChip Create(ItemsPile owner, Transform parent, CItem item)
        {
            Visual state = Classify(item);
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject($"ItemChip_{Name(item)}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // #2: the grab collider MUST exist on this GameObject BEFORE the GrabbableBehaviour (ItemChip)
            // is added — GrabbableBehaviour.Awake caches GetComponent<Collider>() and, finding none, warned
            // and never armed grab, so the laser/trigger pluck was dead (only the collider-free finger
            // sweep worked). Add it first with a placeholder size; it's resized to the real card below.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;

            var chip = go.AddComponent<ItemChip>();
            chip.snapToHand = true; // taken INTO the hand to read
            chip.State = state;
            chip.Item = item;
            chip._owner = owner;
            chip._box = box;

            // Requirement 1: host the REAL ItemCardUI face. Fall back to a colored slab + name if the
            // game's item-card pool is unavailable. Hosting measures the item card's real (near-square)
            // size into chip._faceWidth/_faceHeight so the body + collider match it (no black bars).
            bool realCard = chip.TryHostRealCard(item, go.transform, w, h, state);
            float cw = chip._faceWidth > 0.001f ? chip._faceWidth : w;
            float ch = chip._faceHeight > 0.001f ? chip._faceHeight : h;

            // FIX 3 — the card BODY behind the face. For a REAL item card, reuse the SAME backing the
            // ability cards wear (bundle CardBacking.prefab handed out by PlayTray, or the procedural
            // CardMesh fallback) so the item card's back matches the others instead of a plain black
            // slab — cropped to the item card's near-square shape. The legacy colored cube slab is kept
            // ONLY for the fallback (pool-unavailable) face so its icon/name still read on a tinted body.
            GameObject? backing = realCard ? chip.BuildCardBacking(go.transform, cw, ch) : null;
            if (backing == null)
            {
                // Thin cube slab, sized to the ACTUAL card (item aspect): dark for a real card whose
                // shared backing was unavailable, tinted FaceColor for the colored-slab fallback face.
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Backing";
                Object.Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(go.transform, worldPositionStays: false);
                cube.transform.localScale = new Vector3(cw, ch, 0.0022f);
                cube.transform.localPosition = new Vector3(0f, 0f, 0.0012f); // behind the face (+Z away from viewer)
                Shader? backShader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (backShader != null)
                    cube.GetComponent<MeshRenderer>().sharedMaterial = new Material(backShader)
                        { color = realCard ? new Color(0.10f, 0.09f, 0.08f) : FaceColor(state) };
                Core.VRLayers.Apply(cube); // mod-owned backing on the mod layer (recursion-safe: no children)
            }
            if (!realCard)
                BuildFallbackFace(go.transform, item, cw, ch, state);

            // USABLE HIGHLIGHT — the mod-owned soft outline AROUND the card, hidden by default and
            // shown by TickFaceMaintenance for items that CAN be used right now. Built to the ACTUAL
            // card size so the frame traces the real (near-square) item silhouette; mod layer; a child
            // of the chip, so it dies with the chip and never touches the pooled game card.
            chip._usableFrame = chip.BuildUsableFrame(go.transform, cw, ch);

            // Now size the grab collider to the real card (a small margin for easy laser/finger targeting).
            box.size = new Vector3(cw + ColliderMargin, ch + ColliderMargin, ColliderDepth);
            // NOTE: do NOT VRLayers.Apply(go) — it recurses into the hosted ItemCardUI, which is a
            // GAME-owned canvas that must keep its authored UI layer (reversibility rule; it renders
            // via the VR camera's UI-layer bit owned by CanvasConversion). The mod-owned pieces
            // (backing above, fallback face below) are layered individually instead.

            // FULLY CONSUMED → the ashen tint here PLUS the game's own ItemCardEffects burn timeline,
            // which TryHostRealCard leaves live on the hosted card (bounded by ClampCardEffectSmoke).
            // The SEPARATE CardSmoke plume stays removed — BurnCardFx.SpawnConsumedPlume, which spawned
            // it, has itself been deleted for want of callers (its own file keeps the full reasoning).
            // That prefab is authored for the full-size screen card and, spawned onto the item chip, was
            // the second fog source.

            // Laser: NOT registered as a generic PlayTray laser target any more. The chips' laser
            // hover/pluck is the driver's geometric fan pick (CardsDriver.UpdateItemFanLaser →
            // ItemsPile.TryLaserRaycast, same OnPokeEnter/OnPoke seam) — the physics-collider scan
            // elected nearest LIVE colliders with no hysteresis, and since the hover pop moves and
            // enlarges this very collider, that made right→left laser sweeps skip every second
            // chip (full mechanism on TryLaserRaycast). The collider itself stays: it is the
            // pinch-grab volume and the fingertip-sweep distance source.

            if (!s_loggedRealCard)
            {
                s_loggedRealCard = true;
                VRLog.Info("Cards", $"ITEM CARD: art resolved={realCard} via " +
                                    $"{(realCard ? "ObjectPool ItemCardUI (real face + async background art)" : "fallback colored slab + name")} " +
                                    $"for '{go.name}'.");
            }
            return chip;
        }

        /// <summary>
        /// FIX 3 (back face — not black): build the chip's card BODY reusing the SAME backing the
        /// ability cards use so the item card's back matches them. Prefers the bundle
        /// <c>CardBacking.prefab</c> the <see cref="VRCardFactory"/> hands to <see cref="VRCard.Build"/>
        /// (exposed via <see cref="PlayTray.CardBackingPrefab"/>); when the bundle prefab is
        /// unavailable it falls back to the procedural <see cref="CardMesh"/> slab — the exact edge/back
        /// materials VRCard's own procedural backing uses. Either way the body is sized to the item
        /// card's near-square <paramref name="cw"/>×<paramref name="ch"/> shape (the same back, just
        /// cropped). Returns null only if neither path can build (caller draws the legacy cube slab).
        /// </summary>
        private GameObject? BuildCardBacking(Transform parent, float cw, float ch)
        {
            try
            {
                GameObject? prefab = PlayTray.Current?.CardBackingPrefab;
                GameObject backing;
                string source;
                if (prefab != null)
                {
                    backing = Object.Instantiate(prefab, parent, worldPositionStays: false);
                    // The prefab is authored at the ability card w×h — crop it to the item's near-square
                    // shape by scaling its authored (base) scale by cw/w and ch/h (same back, cropped).
                    float w = CardsConfig.CardWidth.Value;
                    float h = CardsConfig.CardHeight;
                    Vector3 baseScale = backing.transform.localScale;
                    if (w > 1e-5f && h > 1e-5f)
                        backing.transform.localScale = new Vector3(
                            baseScale.x * (cw / w), baseScale.y * (ch / h), baseScale.z);
                    source = "bundle CardBacking.prefab (shared with the ability cards, via PlayTray)";
                }
                else
                {
                    // Procedural fallback: the SAME rounded slab + edge/back materials VRCard builds when
                    // the bundle prefab is missing — sized directly to the item's near-square shape.
                    backing = new GameObject("Backing");
                    backing.transform.SetParent(parent, worldPositionStays: false);
                    backing.AddComponent<MeshFilter>().sharedMesh = CardMesh.Get(cw, ch);
                    var mr = backing.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new[] { CardMesh.CreateEdgeMaterial(), CardMesh.CreateBackMaterial() };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    source = "procedural CardMesh backing (same edge/back material as the ability cards)";
                }
                backing.name = "Backing";
                backing.transform.localPosition = Vector3.zero; // face canvas sits a hair in front (-Z)
                Core.VRLayers.Apply(backing); // mod-owned backing on the mod layer (no game children)
                if (!s_loggedBacking)
                {
                    s_loggedBacking = true;
                    VRLog.Info("Cards", $"ITEM CARD backing source: {source} — the item chip's back now " +
                                        "matches the ability cards (no more plain black slab).");
                }
                return backing;
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM CARD backing build failed ({e.Message}) — falling back to the cube slab.");
                return null;
            }
        }

        /// <summary>
        /// Build the mod-owned "you can play this NOW" cue: a soft, breathing gold FRAME around the
        /// card — the very treatment the user pointed at ("ein Rahmen, ähnlich wie du es mal bei der
        /// Initiativreihenfolge gemacht hast"), reusing the initiative ring's own art and motion via
        /// <see cref="WorldUI.SoftCueArt.FrameSprite"/> + <see cref="WorldUI.SoftFramePulse"/>.
        ///
        /// WHY A FRAME AND NOT THE OLD GLOW QUAD: the previous cue was an additive gold quad parked
        /// behind the card so its oversized border survived as a halo. It worked mechanically but the
        /// user rejected the LOOK outright — a hard-edged rectangle of light stuck behind an antique
        /// fantasy card. A hollow 9-sliced outline draws light ONLY in the card's margin band, with a
        /// falloff on both sides and ROUNDED corners, so nothing is ever a filled rectangle; the item
        /// art stays completely untouched and the cue reads as the same "waiting for you" language the
        /// initiative bar already speaks.
        ///
        /// WHY A uGUI CANVAS AND NOT A MESH: the frame's thickness must be CONSTANT (a fixed band in the
        /// card's margin), not proportional to the card — a stretched textured quad would scale its
        /// border with the card and turn a hairline into a slab on a wide card. That is exactly what a
        /// 9-sliced Image gives for free, and it is what makes this literally the same mechanism as the
        /// initiative ring rather than a look-alike. The chip already hosts one mod-owned world-space
        /// canvas for the card face, so this adds a second sibling canvas of the SAME kind — it is
        /// parked at <see cref="FrameZ"/> (viewer side of the face) with an explicit
        /// <c>sortingOrder</c> above the face canvas, so the draw order is decided, not distance-luck.
        ///
        /// Built INACTIVE; <see cref="TickFaceMaintenance"/> toggles it live from the owner's turn-aware
        /// <c>CanUseNow</c>. Never touches the hosted game card — a child of the chip, destroyed with it.
        /// </summary>
        private GameObject BuildUsableFrame(Transform parent, float cw, float ch)
        {
            // The mod's telegraph gold, warmed toward the initiative ring's amber so the two cues are
            // recognisably the same voice. Alpha is the CEILING the breath multiplies (SoftFramePulse).
            var color = new Color(1f, 0.80f, 0.32f, 1f);

            var canvasGo = new GameObject("UsableFrame", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)canvasGo.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1; // above the hosted face canvas (0) — deterministic, not distance-sorted

            // px → metres. Working in a fixed pixel reference (rather than in metres) is what lets the
            // outset/border constants below be stated in the SAME uGUI px units the initiative ring uses,
            // so both frames end up proportionally identical.
            float scale = cw / FrameReferencePixels;
            rt.sizeDelta = new Vector2(FrameReferencePixels + 2f * FrameOutsetPixels,
                                       ch / scale + 2f * FrameOutsetPixels);
            rt.localScale = new Vector3(scale, scale, scale);
            rt.localPosition = new Vector3(0f, 0f, FrameZ);
            rt.localRotation = Quaternion.identity;

            // The outline itself lives on a CHILD stretched to the canvas rect, so SoftFramePulse can
            // breathe its localScale without rescaling the canvas' pixel reference under it.
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.SetParent(rt, worldPositionStays: false);
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            ringRt.localPosition = new Vector3(0f, 0f, 0f);
            ringRt.localRotation = Quaternion.identity;

            var img = ringGo.GetComponent<Image>();
            img.sprite = WorldUI.SoftCueArt.FrameSprite(FrameCornerRadiusPx);
            img.type = Image.Type.Sliced;
            img.fillCenter = false;   // hollow — a frame, never a wash over the art
            img.raycastTarget = false; // the chip is grabbed through its collider; nothing may raycast this
            img.color = color;
            // ONE RHYTHM FOR THE WHOLE ITEM CUE (2026-08-09 presence pass). This frame used to breathe
            // on a private sine at a private period; the closed pile's rings and ember puffs now beat on
            // [Cards] ItemCueBeatSeconds, and a player who has an item fan open is looking at BOTH the
            // fan and the pile it came out of. Two unrelated periods read as two unrelated widgets
            // flickering at each other; one shared heartbeat reads as one cue. The swing is opened up
            // with it — the floor is lifted so the frame never disappears between beats, the ceiling
            // taken to full, and the scale pulse roughly doubled, because a SILHOUETTE change is the
            // half of this cue a bright passthrough room cannot swallow.
            ringGo.AddComponent<WorldUI.SoftFramePulse>().Init(img, color,
                beatSeconds: Mathf.Max(0.2f, CardsConfig.ItemCueBeatSeconds.Value),
                minAlpha: 0.45f, maxAlpha: 1f, scalePulse: 0.07f);

            Core.VRLayers.Apply(canvasGo); // mod-owned overlay on the mod layer (no game children below it)
            // Perspective (user report 2026-08-08, MR: "Die mixed reality hintergründe schieben
            // sich vor den outlines von karten, das darf nicht sein"). This frame deliberately
            // floats just OUTSIDE the card silhouette (FrameOutsetPixels above), which is exactly
            // where the card's depth-writing slab did NOT stamp depth — so the MR backing plate's
            // ZTest, which protects the card BODY per pixel, had nothing to fail against here, and
            // at sortingOrder 1 against a plate on the panel ladder (>= 100) the plate simply
            // painted last. The item fan hangs 26 cm above the board top and 5 cm proud of it,
            // right where the board-docked initiative track's plate lives, so this was constant.
            // Rank against the ladder by the frame's own eye distance instead — full root cause on
            // CardCueOrder, including why giving this hollow soft outline a depth write is not the
            // answer. sortingOrder 1 above stays correct until the first LateUpdate seats it, and
            // every ladder value is far above the hosted face canvas' 0.
            CardGlow.RankWithPanels(canvasGo);
            canvasGo.SetActive(false);     // shown only while the item is usable
            return canvasGo;
        }

        /// <summary>Pixel width the card is mapped to for the frame canvas. Matches the item card's own
        /// native rect (~300 px), so <see cref="FrameOutsetPixels"/> and the sprite's 16 px border land in
        /// the same proportions the initiative ring wears on a portrait.</summary>
        private const float FrameReferencePixels = 300f;

        /// <summary>How far (uGUI px, i.e. ~1/300 of the card width) the frame rect is grown past the card
        /// on every side — the initiative ring's outset. The sprite's bright core sits 2–6 px inside the
        /// frame's outer lip, so the visible gold line floats just OUTSIDE the card silhouette and only its
        /// soft inner falloff touches the art's outermost few percent.</summary>
        private const float FrameOutsetPixels = 8f;

        /// <summary>Corner rounding (px) of the outline. Non-zero on purpose: the user's one hard
        /// constraint on the new cue was "nicht viereckig", and a rounded, softly-falling outline hugging
        /// the card is the opposite of the hard rectangle that was rejected.</summary>
        private const int FrameCornerRadiusPx = 14;

        /// <summary>Local -Z (TOWARD the viewer) the frame sits at: 1 mm proud of the hosted face canvas
        /// (-0.0012) so no card body or face graphic can ever occlude or z-fight the outline, yet close
        /// enough that it never parallax-separates from the card when the fan is viewed at an angle.</summary>
        private const float FrameZ = -0.0022f;

        /// <summary>
        /// Host the game's real <c>ItemCardUI</c> on a world-space canvas (requirement 1): spawn it
        /// from the game's own pool exactly as the flat inventory tooltip does
        /// (<c>ObjectPool.SpawnCard(item.ID, ECardType.Item, …)</c> → set <c>item</c> →
        /// <c>Show(false)</c> → <c>UpdateState(SlotState, force)</c>), then reparent + fit it onto a
        /// world-space canvas. The background art loads ASYNC onto <c>cardBackground</c> once active.
        /// Any GraphicRaycaster on the card is disabled so the game's input module never raycasts our
        /// world canvas (the card is a grab target, not a click surface). Fully guarded — returns
        /// false on any failure so the caller draws the colored-slab fallback.
        /// </summary>
        private bool TryHostRealCard(CItem item, Transform parent, float w, float h, Visual state)
        {
            try
            {
                if (item.ID == 0)
                    return false;

                var canvasGo = new GameObject("FaceCanvas");
                canvasGo.transform.SetParent(parent, worldPositionStays: false);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                _faceCanvas = canvas; // #3: bind worldCamera to the head camera (kept live in TickFaceMaintenance)
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.localPosition = new Vector3(0f, 0f, -0.0012f); // viewer side of the backing
                // Layer the (still EMPTY) mod-owned canvas now — BEFORE the game card is parented
                // under it — so the recursive layer apply never touches the game-owned card (which
                // keeps its authored UI layer; SpawnCard's reparent does not change the card's layer).
                Core.VRLayers.Apply(canvasGo);

                GameObject cardGo = ObjectPool.SpawnCard(item.ID, ObjectPool.ECardType.Item,
                    canvasRect, resetLocalScale: true);
                if (cardGo == null)
                {
                    Object.Destroy(canvasGo);
                    return false;
                }
                var cardUI = cardGo.GetComponent<ItemCardUI>();
                if (cardUI == null)
                {
                    ObjectPool.RecycleCard(item.ID, ObjectPool.ECardType.Item, cardGo);
                    Object.Destroy(canvasGo);
                    return false;
                }
                cardUI.item = item;
                // The game's ItemCardEffects runs ON the card (Show()/UpdateState() →
                // cardEffects.ToggleEffect(Consumed/Spent)) and is KEPT LIVE: the "verbraucht"
                // look the user wants back — the burn tint + dissolve + grey-out material sweep
                // over the card's face images, the greyed card text and the `fgFx` flame/ghost
                // overlay quad — is all uGUI ON the card's own RectTransforms, so it renders at
                // exactly card size on our world canvas. (An earlier round nulled cardEffects
                // wholesale to kill the fog; that threw the on-card FX away with it.)
                cardUI.Show(highlightElement: false); // no UIManager lock; activates + loads art async
                cardUI.UpdateState(item.SlotState, force: true); // plays the state FX on the card

                // Fit the card's native rect onto the physical card size (mirror of CardFace).
                var cardRect = cardGo.transform as RectTransform;
                Vector2 native = cardRect != null ? cardRect.rect.size : Vector2.zero;
                if (native.x < 1f || native.y < 1f)
                    native = new Vector2(300f, 300f); // near-square fallback (item cards are ~square)
                canvasRect.sizeDelta = native;
                float fit = Mathf.Min(w / native.x, h / native.y);
                canvasRect.localScale = new Vector3(fit, fit, fit);
                // #1: item cards are NEAR-SQUARE, not the tall ability rect — fitting them into the w×h
                // ability box left black backing bars top/bottom. Record the ACTUAL rendered card size so
                // Create sizes the backing slab + grab collider to match it exactly (no bars; the dark
                // body is the same as the other cards, just cropped to the item card's shape).
                _faceWidth = native.x * fit;
                _faceHeight = native.y * fit;
                if (cardRect != null)
                {
                    cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition3D = Vector3.zero;
                    cardRect.localRotation = Quaternion.identity;
                    cardRect.localScale = Vector3.one;
                }

                // Bound the ONE part of the state FX that is NOT uGUI (see ClampCardEffectSmoke).
                // Runs AFTER the fit above so the clamp can measure the card's final world scale;
                // still the same frame the effect coroutine was started in, and particles do not
                // simulate until after this Update, so nothing oversized is ever emitted.
                ClampCardEffectSmoke(cardGo);

                // Neutralize the card's own raycasters — we drive interaction through the mod's
                // collider (grab/laser), never the game's uGUI input module (which would raycast
                // this world canvas at the parked mouse pixel).
                foreach (GraphicRaycaster gr in cardGo.GetComponentsInChildren<GraphicRaycaster>(true))
                    gr.enabled = false;

                // ITEM #1 — swap the mipless-atlas sprites for mip-baked equivalents right after host,
                // then (a) tight-poll for the async background art every frame for the first ~2 s and
                // Rescan the INSTANT it appears (no shimmer window), and (b) re-scan on the 1 s cadence
                // afterward for ongoing state-change maintenance.
                CardFaceMipBake.Rescan(cardUI);
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                _tightArtPollUntil = Time.unscaledTime + TightArtPollSeconds;
                _artBaked = false;

                _cardGo = cardGo;
                _cardUI = cardUI;
                return true;
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM CARD host failed ({e.Message}) — falling back to the colored slab.");
                return false;
            }
        }

        /// <summary>One clamped game emitter + the module values it had before we touched it. The start
        /// size/speed are kept as the WHOLE <c>MinMaxCurve</c>, not just their multiplier: in
        /// TwoConstants/TwoCurves mode the multiplier only writes the MAX side, so scaling through it
        /// would leave the MIN side unshrunk (and could invert the range).</summary>
        private struct SmokeClamp
        {
            internal ParticleSystem Ps;
            internal ParticleSystemSimulationSpace Space;
            internal ParticleSystemScalingMode Scaling;
            internal ParticleSystem.MinMaxCurve StartSize;
            internal ParticleSystem.MinMaxCurve StartSpeed;
        }

        /// <summary>
        /// The consumed/spent plume may not read wider than this many CARD WIDTHS in world space —
        /// wisps ON the card, never a cloud AROUND it. The user explicitly wants the burn look back
        /// but not the fog, so this is a hard geometric bound, not a taste multiplier.
        /// </summary>
        private const float SmokeCardSpan = 0.9f;

        private static bool s_loggedSmokeClamp;

        /// <summary>
        /// ROOT CAUSE of the field-sized fog ring around a consumed item card, and the ONE thing that
        /// has to be neutralised (everything else in <c>ItemCardEffects</c> stays live).
        ///
        /// <c>ItemCardEffects.BurnCardTimeline</c> / <c>GhostOutOnTimeline</c> drive two kinds of
        /// visual. Nearly all of it is uGUI ON the card — the <c>_Burn</c>/<c>_Dissolve</c>/
        /// <c>_GreyOut</c>/<c>_Flow</c> material sweep over <c>imgComp</c>, the greyed <c>txtComp</c>
        /// text, and the <c>fgFx</c> flame/ghost overlay Image — all of which lives on the card's own
        /// RectTransforms and therefore renders at exactly card size on our world-space FaceCanvas.
        /// The single exception is the serialized <c>fx_Smoke</c> <see cref="ParticleSystem"/> the
        /// timeline switches on: a particle system renders through its OWN renderer, not the canvas,
        /// and Unity's default <c>ParticleSystemScalingMode.Local</c>/<c>Shape</c> makes it IGNORE the
        /// parent scale chain. Under the flat screen-space canvas that is invisible (scale 1); under
        /// our FaceCanvas — which is downscaled by ~<c>fit</c> (card metres ÷ ~300 canvas px, i.e.
        /// three orders of magnitude) to make the card hand-sized — the emitter keeps emitting at its
        /// authored CANVAS-PIXEL size in METRES. That is the green fog: puffs tens of metres across,
        /// centred on and following the card. Nothing about the effect's colour or timing was wrong,
        /// only its scale reference.
        ///
        /// Fix (mod-side, reversible, no re-layering, no game data touched): leave the effect running
        /// and put every emitter in the hosted card back into the card's frame of reference —
        /// <c>simulationSpace = Local</c> so the puffs ride the card instead of being emitted into
        /// world space at authored size, and <c>scalingMode = Hierarchy</c> so start size/velocity
        /// inherit the FaceCanvas downscale like the rest of the card does. Hierarchy scaling alone
        /// already restores the authored card-relative look (unlike <see cref="BurnCardFx"/>, whose
        /// plume is a separately spawned prefab authored for the full-size screen card); on top of it
        /// we still shrink start size/speed by whatever factor is needed to keep the plume inside
        /// <see cref="SmokeCardSpan"/> card widths, so a stray authored value can never grow into a
        /// cloud again. Only these four module values are written, and every one is recorded per
        /// emitter and restored by <see cref="RestoreCardEffectSmoke"/> before the widget goes back to
        /// <c>ObjectPool.RecycleCard</c>.
        /// </summary>
        private void ClampCardEffectSmoke(GameObject cardGo)
        {
            _smokeClamps = null;
            if (cardGo == null)
                return;
            // includeInactive: the timeline only SetActive(true)s fx_Smoke once it starts, and
            // RestoreCard switches it back off — so at host time it is normally inactive.
            ParticleSystem[] systems = cardGo.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            if (systems.Length == 0)
                return;

            // World size of one authored unit once scalingMode=Hierarchy applies the card's scale chain.
            float lossy = Mathf.Abs(cardGo.transform.lossyScale.x);
            float span = (_faceWidth > 0.001f ? _faceWidth : CardsConfig.CardWidth.Value) * SmokeCardSpan;

            var clamps = new List<SmokeClamp>(systems.Length);
            var report = new StringBuilder();
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null)
                    continue;
                ParticleSystem.MainModule main = ps.main;
                var rec = new SmokeClamp
                {
                    Ps = ps,
                    Space = main.simulationSpace,
                    Scaling = main.scalingMode,
                    StartSize = main.startSize,
                    StartSpeed = main.startSpeed,
                };

                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                // Belt-and-braces bound: with Hierarchy scaling a particle's world size is its start
                // size × the card's lossy scale, and its world drift is start speed × lifetime × the
                // same. Shrink both by the single worst-case factor needed to keep them inside `span`.
                float shrink = 1f;
                if (lossy > 1e-6f && span > 1e-5f)
                {
                    float worldSize = UpperBound(main.startSize) * lossy;
                    float worldDrift = UpperBound(main.startSpeed) * UpperBound(main.startLifetime) * lossy;
                    float worst = Mathf.Max(worldSize, worldDrift);
                    if (worst > span)
                        shrink = span / worst;
                }
                if (shrink < 1f)
                {
                    main.startSize = ScaleCurve(rec.StartSize, shrink);
                    main.startSpeed = ScaleCurve(rec.StartSpeed, shrink);
                }

                clamps.Add(rec);
                if (!s_loggedSmokeClamp)
                    report.Append(report.Length > 0 ? ", " : "")
                          .Append(ps.name).Append(" [").Append(rec.Space).Append('/').Append(rec.Scaling)
                          .Append(" → Local/Hierarchy, size×").Append(shrink.ToString("F3")).Append(']');
            }
            _smokeClamps = clamps.ToArray();

            if (!s_loggedSmokeClamp)
            {
                s_loggedSmokeClamp = true;
                VRLog.Debug("Cards", $"ITEM CARD FX: game ItemCardEffects LIVE (burn/dissolve/greyout/overlay on the " +
                                     $"card); bounded {clamps.Count} emitter(s) to <= {span:F3} m " +
                                     $"(card lossy {lossy:F5}): {(report.Length > 0 ? report.ToString() : "none")}.");
            }
        }

        /// <summary>Worst-case value a start-size/speed/lifetime curve can produce (curve modes fold
        /// their multiplier, which is the curve's peak, so this is an upper bound in every mode).</summary>
        private static float UpperBound(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => curve.constant,
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(curve.constantMin, curve.constantMax),
            _ => Mathf.Abs(curve.curveMultiplier),
        };

        /// <summary>Scale a start-size/speed curve by <paramref name="f"/> in EVERY mode (constants and
        /// curve multipliers alike), so both ends of a random range shrink together.</summary>
        private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float f) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => new ParticleSystem.MinMaxCurve(curve.constant * f),
            ParticleSystemCurveMode.TwoConstants =>
                new ParticleSystem.MinMaxCurve(curve.constantMin * f, curve.constantMax * f),
            ParticleSystemCurveMode.TwoCurves =>
                new ParticleSystem.MinMaxCurve(curve.curveMultiplier * f, curve.curveMin, curve.curveMax),
            _ => new ParticleSystem.MinMaxCurve(curve.curveMultiplier * f, curve.curve),
        };

        /// <summary>
        /// Put every emitter clamped by <see cref="ClampCardEffectSmoke"/> back to its recorded module
        /// values. MUST run before <c>ObjectPool.RecycleCard</c>: the card widget is GAME-owned and
        /// pooled, so the flat UI would inherit our clamp on the next spawn. Writing module values on a
        /// system the pool has already disabled is harmless, so no active check is needed.
        /// </summary>
        private void RestoreCardEffectSmoke()
        {
            SmokeClamp[]? clamps = _smokeClamps;
            _smokeClamps = null;
            if (clamps == null)
                return;
            foreach (SmokeClamp rec in clamps)
            {
                if (rec.Ps == null)
                    continue;
                ParticleSystem.MainModule main = rec.Ps.main;
                main.simulationSpace = rec.Space;
                main.scalingMode = rec.Scaling;
                main.startSize = rec.StartSize;
                main.startSpeed = rec.StartSpeed;
            }
        }

        /// <summary>
        /// Legacy fallback face (pool unavailable): a colored slab tint is set on the backing by the
        /// caller; here we add the synchronous mini-icon (if any) + the localized item name so the
        /// chip still reads. Only reached when <see cref="TryHostRealCard"/> failed.
        /// </summary>
        private static void BuildFallbackFace(Transform parent, CItem item, float w, float h, Visual state)
        {
            Sprite? icon = TryGetIcon(item);
            bool hasIcon = icon != null;
            if (hasIcon)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(parent, worldPositionStays: false);
                iconGo.transform.localPosition = new Vector3(0f, h * 0.14f, -0.0018f);
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = icon;
                if (state == Visual.Consumed)
                    sr.color = new Color(0.62f, 0.60f, 0.58f);
                Vector2 size = icon!.bounds.size;
                if (size.x > 1e-4f && size.y > 1e-4f)
                {
                    float s = Mathf.Min(w * 0.82f / size.x, h * 0.56f / size.y);
                    iconGo.transform.localScale = Vector3.one * s;
                }
                Core.VRLayers.Apply(iconGo); // mod-owned fallback icon on the mod layer
            }

            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(parent, worldPositionStays: false);
            nameGo.transform.localPosition = new Vector3(0f, hasIcon ? -h * 0.36f : 0f, -0.0025f);
            var nameTmp = nameGo.AddComponent<TextMeshPro>();
            nameTmp.text = Name(item);
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color = state == Visual.Consumed ? new Color(0.75f, 0.72f, 0.7f)
                                                     : new Color(0.12f, 0.10f, 0.07f);
            WorldUI.NativeButtonSkin.ApplyFont(nameTmp);
            Core.TmpFit.Fit(nameTmp, w * (hasIcon ? 0.9f : 0.88f), h * (hasIcon ? 0.24f : 0.82f),
                            maxFontSize: 0.16f, wrap: true);
            Core.VRLayers.Apply(nameGo); // mod-owned fallback name label on the mod layer
        }

        /// <summary>
        /// Fallback-only: resolve the item's SYNCHRONOUS mini-icon (<c>ItemConfigUI.miniIcon</c>).
        /// Often null for items (the real face lives in the addressable <c>BackgroundImage</c>, used
        /// by the hosted card above) — fully null-guarded.
        /// </summary>
        private static Sprite? TryGetIcon(CItem item)
        {
            if (item == null || item.YMLData == null)
                return null;
            string? art = item.YMLData.Art;
            if (string.IsNullOrEmpty(art))
                return null;
            if (!Singleton<UIInfoTools>.IsInitialized)
                return null;
            UIInfoTools tools = Singleton<UIInfoTools>.Instance;
            if (tools == null)
                return null;
            try
            {
                return tools.GetItemConfig(art)?.miniIcon;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Requirement 5 (emerge): seat the chip AT the pile converge point (root-local), shrunk to
        /// <c>ItemFanSeedScale</c> and rolled by <c>ItemFanOpenSpinDegrees</c>, then let
        /// <see cref="TickEmerge"/> fly it out to its arc home. Called once at open, after
        /// <see cref="SetHome"/> has recorded the home pose.
        ///
        /// <para><paramref name="delay"/> is this chip's place in the owner's centre-out ripple and
        /// <paramref name="spinSign"/> its side of the fan (see <see cref="ItemsPile.EmergeAll"/>);
        /// both are decided there because only the owner knows the chip's index.</para>
        ///
        /// <para>THE SEED POSE IS WRITTEN HERE, NOT ON THE FIRST TICK, and that is not a detail: this
        /// runs inside <see cref="ItemsPile.Open"/>, before any frame is rendered, so a chip whose
        /// delay has not elapsed sits ON the stack from the very first frame it exists. It never
        /// appears at its arc slot and then jumps back — the pop the standing ruling forbids.</para>
        /// </summary>
        internal void BeginEmerge(Vector3 localConverge, float delay, float spinSign)
        {
            if (Holder != null)
                return;
            _releaseGlide = 0f;   // the fly-out owns the pose now; no second lerp may fight it
            _emerging = true;
            _emergeTime = 0f;
            _emergeDelay = Mathf.Max(0f, delay);
            _emergeFrom = localConverge;
            // Home-RELATIVE, so the roll unwinds into whatever slot the chip ends up in even if the
            // arc is re-laid out mid-flight. Signed outward: the fan unfolds instead of sliding open.
            _emergeSpin = Quaternion.Euler(0f, 0f, spinSign * CardsConfig.ItemFanOpenSpinDegrees.Value);
            transform.localPosition = localConverge;
            transform.localRotation = _homeRot * _emergeSpin;
            transform.localScale = Vector3.one * (_homeScale * SeedScale());
        }

        /// <summary>
        /// Requirement 5 (collapse): begin a self-driven WORLD-space glide into the pile stack point,
        /// then destroy this chip. The owner has already re-parented the chip out of the fan root so it
        /// keeps updating after the root deactivates. Disables the collider so it can't be grabbed mid-collapse.
        ///
        /// <para><paramref name="delay"/> is the REVERSE ripple (outermost chip first — see
        /// <see cref="ItemsPile.CollapseChips"/>). While it runs down the chip holds the exact world
        /// pose captured here, because the collapse curve is the identity at t = 0: a chip waiting its
        /// turn is standing still, never hidden and never moved.</para>
        /// </summary>
        /// <summary>True once this chip has been handed its fold-into-the-items-stack glide (see
        /// <see cref="BeginCollapse"/>) — i.e. it is on its way home and about to destroy itself.
        /// Read by <c>ItemsPile.RetireChipToPile</c>, which is re-entered through the release
        /// routing and must not start a second flight for the same card.</summary>
        internal bool IsCollapsing => _collapsing;

        internal void BeginCollapse(Vector3 worldConverge, float delay = 0f, float spinSign = 1f)
        {
            _emerging = false;
            _collapsing = true;
            _collapseWorld = worldConverge;
            _collapseFrom = transform.position;
            _collapseFromRot = transform.rotation;
            _collapseFromScale = transform.localScale.x;
            _collapseTime = 0f;
            _collapseDelay = Mathf.Max(0f, delay);
            // Wound back ON over the fall — the open's unfold, played backwards.
            _collapseSpin = Quaternion.Euler(0f, 0f, spinSign * CardsConfig.ItemFanOpenSpinDegrees.Value);
            _fingerPopped = false;
            _laserPopped = false;
            _recessPopped = false; // a folding chip is nobody's grab candidate any more
            if (_box != null)
                _box.enabled = false;
        }

        /// <summary>The size a chip starts the fly-out at (and ends the collapse at), as a fraction of
        /// its seated size — <c>[Cards] ItemFanSeedScale</c>, clamped so a nonsense config can never
        /// produce a zero-scale (and therefore invisible, i.e. popping) card.</summary>
        private static float SeedScale() => Mathf.Clamp(CardsConfig.ItemFanSeedScale.Value, 0.02f, 1f);

        /// <summary>
        /// Ease-out BACK: overshoots 1 near the end and settles back onto it. <paramref name="s"/> = 0
        /// degenerates to the plain ease-out cubic the fan used before the presence pass, which is
        /// exactly what <c>[Cards] ItemFanSettleOvershoot</c> = 0 is documented to restore.
        ///
        /// <para>WHY AN OVERSHOOT AND NOT SIMPLY MORE SPEED (the mixed-reality argument): passthrough
        /// gives the eye a background that already moves with the head and is already full of
        /// contrast, so it competes with anything that merely translates faster. It cannot, however,
        /// produce a REVERSAL — a thing that goes one way and then comes back is a discontinuity in
        /// direction, which is the single most salient event a motion can contain, and it happens at
        /// the end of the flight where the eye has already arrived.</para>
        /// </summary>
        private static float EaseOutBack(float t, float s)
        {
            float u = t - 1f;
            return 1f + u * u * ((s + 1f) * u + s);
        }

        /// <summary>Ease-in BACK: dips slightly BELOW 0 first (the chip winds up away from the stack)
        /// and then accelerates in. The mirror of <see cref="EaseOutBack"/>, used by the collapse so
        /// the close is the open reversed rather than a different animation.</summary>
        private static float EaseInBack(float t, float s) => t * t * ((s + 1f) * t - s);

        /// <summary>The authored back-ease strength, clamped. Shared by both directions so one dial
        /// governs the whole "settle" character of the fan.</summary>
        private static float Overshoot() => Mathf.Clamp(CardsConfig.ItemFanSettleOvershoot.Value, 0f, 3f);

        /// <summary>
        /// Advance the fly-out. <paramref name="posTarget"/> / <paramref name="scaleTarget"/> are the
        /// chip's LIVE arc home with the hand-sweep pop already folded in — passed in rather than read
        /// here so the emerge and the settled steady state aim at exactly the same pose and the
        /// hand-off between them cannot produce a step.
        ///
        /// <para>THE THREE AMPLITUDES, and why each one survives a passthrough background:</para>
        /// <list type="bullet">
        /// <item>the BOW (<c>ItemFanOpenArc</c>, along fan-local −Z, i.e. toward the viewer, peaking at
        ///   mid-flight and exactly 0 at both ends): it moves the card in DEPTH, which the two eyes
        ///   resolve as disparity against a room that is metres further away. Passthrough can hide
        ///   contrast; it cannot hide stereo separation.</item>
        /// <item>the GROWTH (from <c>ItemFanSeedScale</c>): the monocular half of the same cue, and the
        ///   reason the seed dropped from 0.35× to 0.12× — a card that grows eightfold is coming
        ///   toward you, one that grows by a third is a picture being nudged.</item>
        /// <item>the OVERSHOOT (<c>ItemFanSettleOvershoot</c>): see <see cref="EaseOutBack"/>.</item>
        /// </list>
        ///
        /// <para>Allocation-free, like everything else on this per-frame path: five config reads,
        /// three struct maths, no closures and no temporaries that escape.</para>
        /// </summary>
        private void TickEmerge(float dt, Vector3 posTarget, float scaleTarget)
        {
            _emergeTime += dt;
            float dur = Mathf.Max(0.01f, CardsConfig.ItemFanOpenDuration.Value);
            float t = Mathf.Clamp01((_emergeTime - _emergeDelay) / dur);
            float e = EaseOutBack(t, Overshoot());

            // LerpUnclamped: the overshoot is the point — a clamp here would quietly delete it.
            Vector3 p = Vector3.LerpUnclamped(_emergeFrom, posTarget, e);
            p.z -= Mathf.Max(0f, CardsConfig.ItemFanOpenArc.Value) * Mathf.Sin(t * Mathf.PI);
            transform.localPosition = p;
            // Rotation eases on the CLAMPED progress: a card that overshoots its ROLL reads as a
            // wobble rather than a settle, and it is the one axis where the reversal does not help.
            transform.localRotation = _homeRot * Quaternion.Slerp(_emergeSpin, Quaternion.identity, e);
            transform.localScale = Vector3.one
                                 * Mathf.LerpUnclamped(_homeScale * SeedScale(), scaleTarget, e);

            if (t >= 1f)
                _emerging = false; // settled — the steady-state branch asserts the home pose from here
        }

        /// <summary>
        /// Requirement 6 — BOLT the chip into the use slot: re-parent it onto the slot transform at an
        /// exact zero local pose, so it is as rigid there as a played ability card is in a board recess.
        ///
        /// ROOT CAUSE of "wenn man mit dem Kopf wackelt, wackelt die Karte auch etwas und zieht nach":
        /// the clipped chip stayed parented to the ITEMS-FAN root, and that root BILLBOARDS to the head
        /// every frame (ItemsPile.Tick rewrites _root.rotation from the head direction). The slot,
        /// meanwhile, is bolted to the board. So the owner re-derived the slot pose in fan-root local
        /// space each tick and the chip chased it with an exponential lerp — a target that jumped with
        /// every head movement, followed by something that only ever converges asymptotically. The card
        /// could not help but swim behind the head.
        ///
        /// Re-parenting removes the chase entirely instead of tuning it: once the chip IS a child of the
        /// slot, Unity's transform hierarchy holds it there for free, at zero cost and with no residual
        /// error, however the head or the board moves.
        ///
        /// The SIZE it lands at is no longer the fan's chip scale carried across the scale chain but the
        /// recess's own fit (<see cref="ItemsPile.UseSlotFitScale"/>) — see requirement 5a there; and the
        /// pose it lands at is reached by a short SETTLE (<see cref="TickClipSettle"/>) rather than by an
        /// instant snap, because everything that moves has to be seen moving.
        /// </summary>
        internal void ClipIntoSlot(Transform slot)
        {
            if (slot == null)
                return;
            _releaseGlide = 0f; // and no glide may fight the parent
            // Re-parent keeping the WORLD pose, then SETTLE into the slot frame over
            // ClipSettleSeconds (TickClipSettle). The target is the slot's own frame exactly —
            // centred, square, unrotated, and scaled to FIT the recess
            // (<see cref="ItemsPile.UseSlotFitScale"/>) — so the card comes to rest aligned to the
            // recess and to the gold drop GHOST that previewed it, which is the whole of
            // requirement 5a. The motion exists because the user's standing rule is that everything
            // that moves moves WITH an animation: this used to be a hard teleport onto the slot
            // (worldPositionStays: false), i.e. the card popped from the fingers into the recess.
            transform.SetParent(slot, worldPositionStays: true);
            _clipScale = UseSlotFitScale(this);
            _clipSettle = ClipSettleSeconds;
            // The card arrives SEATED, never pre-lifted: the settle's target is the slot frame
            // itself, and a ramp carried in from the arc/hand would make the recess lift (TickRecessPop)
            // start halfway up the moment the settle window closes. Every path into the recess goes
            // through here — the ordinary placement, the resolving-card return and the locked-bonus
            // spring-back — so this is the one place that has to say it.
            _pop = 0f;
        }

        /// <summary>
        /// Requirement 5a — advance the clip-in settle: ease the chip's SLOT-LOCAL pose toward the
        /// slot's own frame (zero position, identity rotation, <see cref="_clipScale"/>), then land
        /// on it exactly. Runs only while the settle window is open; once it closes the transform
        /// hierarchy holds the card rigidly at the slot with no per-frame work at all, which is the
        /// property ClipIntoSlot's re-parent was introduced for (see its ROOT CAUSE note).
        /// Unscaled time, like every other card glide, so it plays while the game is paused.
        /// </summary>
        private void TickClipSettle()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _clipSettle -= dt;
            if (_clipSettle <= 0f)
            {
                _clipSettle = 0f;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one * _clipScale;
                return;
            }
            float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * dt);
            transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, t);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.identity, t);
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _clipScale, t);
        }

        /// <summary>
        /// ─── THE LIFT OF THE CARD LYING IN THE USE RECESS (user report 2026-08-09) ──────────────
        ///
        /// "Ich will das die Gegenstandskarte die auf dem Overlay liegt auch nach oben hinweg
        ///  gehighlighted wird wenn man mit dem Laser drüberfährt oder mit der Hand hinkommt, genauso
        ///  wie beim Highlighting der Handkarten die auf dem Controllboard-Overlay liegen. Soll sich
        ///  da also gleich verhalten vom Feedback."
        ///
        /// <para>WHY IT DID NOT HAPPEN. Not a missing hover — a missing RENDERING of one. Both contact
        /// signals were already reaching this chip (the beam through <see cref="OnPokeEnter"/>, which
        /// even ticked the controller; the hand through the grabber highlight that routes its trigger
        /// and stands its laser down), and <see cref="Update"/> simply returned before any pose work
        /// for a <see cref="PendingUse"/> chip. So the card in the berth was the one card on the
        /// board that could be hovered and never showed it.</para>
        ///
        /// <para>THE MOTION IS <c>VRCard</c>'S, TERM FOR TERM — the same three numbers a card docked in
        /// one of the board's slot recesses lifts by, which is the parity the report names: UP out of
        /// the berth by <see cref="PopUp"/>, toward the viewer by <c>[Cards] FanSelectedPopForward</c>,
        /// ×<see cref="PopScale"/>, ramped at <see cref="PopLerpSpeed"/>. No new dial and no second
        /// set of constants: this is the arc chips' own lift block, read from the same fields.</para>
        ///
        /// <para>AND IT IS APPLIED IN SLOT-LOCAL SPACE, which is the recess's edition of the fan-local
        /// argument the arc lift already carries. A clipped chip's local ROTATION is identity by
        /// construction (<see cref="TickClipSettle"/> lands it square in the slot frame), so the extra
        /// 90° roll a SPENT chip wears in the arc is simply not present here and cannot send a tapped
        /// card sideways. The slot frame's own +Y is the board's up and its −Z is out of the board
        /// toward the player (<c>PlayTray.BuildItemUseSlot</c>: "+Z is INTO the board, so all three
        /// [berth layers] sit BEHIND the z=0 plane a clipped-in card is parented at") — so the very
        /// same <c>(0, +PopUp, −popForward)</c> vector means "up and out at the player" here, which is
        /// the report's "nach oben hinweg". Both quantities are board-local metres, the same space the
        /// berth's own geometry is authored in, so the lift reads at the same physical size as the
        /// arc's and scales with the board for free.</para>
        ///
        /// <para>WHAT THE BERTH DOES WHILE THE CARD IS LIFTED: NOTHING, deliberately, and no line of
        /// <c>PlayTray.4.Slots.cs</c> was touched.
        /// <list type="number">
        /// <item>There is nothing to un-fight. All three berth layers are authored at POSITIVE local Z
        ///   (field 0.0035, ping 0.0030, outline 0.0025 — INTO the board) behind the card's z=0 plane.
        ///   The lift travels along −Z, so it can only ever INCREASE that separation; the one way a
        ///   berth could genuinely fight a card — co-planar surfaces deciding their order per frame,
        ///   the defect that drove the "USE" caption out of the recess — is moved further away by this
        ///   change, not closer.</item>
        /// <item>The lift REVEALS the berth rather than hiding it. At the shipped numbers the card is
        ///   fitted to 0.94 × 1.04 = 0,978 of the card box (<see cref="ItemsPile.UseSlotFitScale"/>)
        ///   inside a 1,08 outline: rising 12 mm out of a ~95 mm-tall berth opens the berth's lower
        ///   field under it, and the ×1,18 growth adds ~2 mm of overhang on each vertical band —
        ///   35 mm IN FRONT of it, ordinary parallax, for as long as a hand is there.</item>
        /// <item>Parity, which is the whole point of the round: an ability card docked in a slot
        ///   recess pops over that recess's own ring in exactly this way and the recess does nothing
        ///   about it. A bespoke hover reaction here would be a second set of numbers for the same
        ///   idea — the drift the item-fan parity round existed to remove.</item>
        /// <item>The berth is the PLACEMENT flow's state, not the hover's: it arrives when a card
        ///   becomes placeable, pings inward while one approaches, and departs with the cancel. Making
        ///   it react to a hover would announce a change in the placement that did not happen — and
        ///   this round may not touch the placement flow at all.</item>
        /// <item>Multiplayer settles it: the berth is board FURNITURE, mirrored by
        ///   <c>Net.RemoteBoardFurniture</c> and linted against it, so any reaction of its own would be
        ///   a control-board animation the 1:1 ruling requires on the wire — a new tuning dial. The
        ///   CARD's lift needs none: it rides the fan-highlight record this pile already sends (see
        ///   <see cref="ItemsPile.HighlightedIndex"/>).</item>
        /// </list></para>
        ///
        /// <para>NOTHING IS WRITTEN WHILE THE CARD LIES STILL. The whole reason the clip is a
        /// re-parent instead of a chase (see <see cref="ClipIntoSlot"/>'s root cause: the card used to
        /// swim behind head movement) is that a settled card costs zero per-frame transform work, and
        /// that property survives here: with the ramp at rest AND no hover, this returns before
        /// touching the transform, so the hierarchy goes on holding the card exactly as before. The
        /// frame the ramp reaches 0 still writes, and writes the seated pose exactly.</para>
        /// </summary>
        private void TickRecessPop()
        {
            bool popped = RecessHighlighted;
            if (!popped && _pop <= 0f)
                return; // seated and un-hovered: the hierarchy owns the pose, we touch nothing
            if (popped && _pop <= 0f)
                LogRecessFeedbackOnce();
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // unscaled: the lift plays while paused
            _pop = Mathf.MoveTowards(_pop, popped ? 1f : 0f, PopLerpSpeed * udt);
            float popForward = CardsConfig.FanSelectedPopForward != null
                ? CardsConfig.FanSelectedPopForward.Value : Defaults.FanSelectedPopForward;
            transform.localPosition = new Vector3(0f, PopUp * _pop, -popForward * _pop);
            transform.localScale = Vector3.one * (_clipScale * (1f + (PopScale - 1f) * _pop));
            // localRotation is left alone on purpose: the settle landed it on identity and a lift is
            // not a rotation. (Re-asserting it every frame would also be the per-frame work the
            // paragraph above exists to avoid.)
        }

        /// <summary>
        /// Requirement 6 — take the chip back OUT of the slot hierarchy and hand it to
        /// <paramref name="fanRoot"/> again, KEEPING its current world pose so nothing jumps. Every exit
        /// from the pending state goes through this (cancel, invalidation, confirm), so the chip's
        /// fan-local home pose, glide-home and use-flourish all run in the frame they were written for.
        /// </summary>
        internal void UnclipFromSlot(Transform? fanRoot)
        {
            _clipSettle = 0f; // the settle targets SLOT-local zero — it must not survive the exit

            // ─── A HAND OWNS THE HIERARCHY. THIS METHOD MAY NOT TAKE IT AWAY. ──────────────────
            //
            // USER REPORT (2026-08-09, THIRD round on this defect): "Ich kann immer noch keine
            // Itemkarte greifen wenn sie auf dem Itemoverlay liegt zum 'usen'. Stattdessen bleibt
            // die item karte in der Mitte des Controllboards in der Nähe des Fächers kleben solange
            // ich mit dem Trigger gedrückt halte."
            //
            // ROOT CAUSE — and it is NOT the entry-point split ModBuild 93 made (that part works;
            // the grab genuinely happens). The chip is grabbed, <see cref="OnGrab"/> unclips it and
            // <c>GrabbableBehaviour.AttachToHand</c> re-parents it to the hand's GrabAnchor. ONE
            // TICK LATER the owner's per-tick service notices <c>Holder != null</c> — the
            // "grabbed back out of the slot" cancel, in ALL THREE placement flows
            // (<see cref="ItemsPile.CancelPendingUse"/> from <see cref="ItemsPile.TickPendingUse"/>,
            // <see cref="ItemsPile.TickDemandPick"/>, <see cref="ItemsPile.TickTakeDamagePick"/>) —
            // and every one of them calls <see cref="ItemsPile.UnclipChip"/> to "put the card back
            // in the fan's frame". The comment at CancelPendingUse claimed that was "no-op when a
            // grab already took it out of the slot hierarchy". IT IS NOT: the guard below only
            // compared the parent against the FAN ROOT, and a held chip's parent is the GrabAnchor,
            // which is not the fan root — so the call RE-PARENTED THE CARD OUT OF THE PLAYER'S HAND
            // and into the fan root, one frame after the grab.
            //
            // WHY THE SYMPTOM LOOKS LIKE "STICKS NEAR THE FAN WHILE THE TRIGGER IS HELD": the
            // grabber still holds the chip (Holder stays set, _attached stays true), so
            // <see cref="Update"/> keeps taking the held branch and <see cref="TickHeldPose"/> keeps
            // lerping transform.localPosition toward <c>_heldPos</c> — a pinch point expressed in
            // GRABANCHOR-LOCAL space. Interpreted in FAN-ROOT-local space instead, that is a fixed
            // point a few centimetres off the fan root's origin: the middle of the control board,
            // right next to the fan, held there for exactly as long as the trigger is down. On
            // trigger-up the base detach restores the pre-grab parent and the ordinary release glide
            // takes it back to the arc — "die Karte bleibt kleben … solange ich den Trigger halte",
            // verbatim.
            //
            // THE FIX is the guard, not a caller-by-caller audit: while a hand holds the chip the
            // HAND owns its parent, full stop. <c>DetachFromHand</c> already restores the parent
            // that OnGrab's own unclip installed (ClipParkParent), so the frame the cancel wanted is
            // waiting for the card the moment it is let go — nothing is lost by declining here, and
            // the three flows keep their game-side back-out (which is the part that actually
            // matters). OnGrab's own unclip is unaffected: it runs BEFORE base.OnGrab, when Holder
            // is still null.
            if (Holder != null)
                return;

            if (fanRoot == null || transform.parent == fanRoot)
                return;
            transform.SetParent(fanRoot, worldPositionStays: true);
        }

        /// <summary>Requirement 6 — cancel the post-release glide-home (used when a drop CLIPS into the
        /// use slot instead of returning to the fan).</summary>
        internal void CancelReleaseGlide() => _releaseGlide = 0f;

        /// <summary>Requirement 6 — return the chip to its fan home (the "return to deck" path on cancel):
        /// clear the clip state and start the same glide the post-release home uses.</summary>
        internal void ReturnToFan()
        {
            PendingUse = false;
            _releaseGlide = ReleaseGlideSeconds; // Update's home-glide flies it back to the arc slot
        }

        /// <summary>
        /// Requirement 6 — after a CONFIRM: play the result flourish (burn plume for Consumed, a "tap"
        /// roll to 90° with a scale pulse for Spent), then collapse into the deck. The owner has already
        /// detached the chip from the fan root and removed it from the live list, so this runs to
        /// completion even if the fan closes/rebuilds.
        /// </summary>
        internal void PlayUseThenCollapse(bool consumed, bool spent, Vector3 collapseWorld)
        {
            PendingUse = false;
            _useFxActive = true;
            _useFxTime = UseFxSeconds;
            _useFxSpent = spent && !consumed;
            _useFxCollapseWorld = collapseWorld;
            _useFxBaseRot = transform.localRotation;
            _fingerPopped = false;
            _laserPopped = false;
            _recessPopped = false; // the decision is over: the flourish owns the pose, not a hover
            if (_box != null)
                _box.enabled = false; // no grabbing during the flourish
            // Consumed flourish = a brief hold on the card's OWN state FX (the game's ItemCardEffects
            // burn sweep + its card-bounded smoke, kept live by TryHostRealCard) over the ashen tint,
            // before the card collapses back into the deck. No extra CardSmoke prefab is spawned here —
            // that separate, screen-card-authored plume was the field-covering fog.
        }

        /// <summary>Requirement 6 — advance the post-confirm flourish, then hand off to the collapse.</summary>
        private void TickUseFx()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _useFxTime -= dt;
            float k = 1f - Mathf.Exp(-14f * dt);
            if (_useFxSpent)
            {
                // "Tap": roll the card to 90° (the game's tapped look) with a brief scale pulse accent.
                Quaternion target = _useFxBaseRot * Quaternion.Euler(0f, 0f, 90f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, target, k);
                float prog = 1f - Mathf.Clamp01(_useFxTime / UseFxSeconds);
                float pulse = 1f + 0.14f * Mathf.Sin(prog * Mathf.PI);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * (_homeScale * pulse), k);
            }
            // Consumed: the card's own burn FX plays over the flourish window; no extra motion.
            if (_useFxTime <= 0f)
            {
                _useFxActive = false;
                BeginCollapse(_useFxCollapseWorld);
            }
        }

        /// <summary>Seat the chip in its arc slot (its "home" the base restores it to on release).</summary>
        internal void SetHome(Vector3 pos, Quaternion rot, float scale)
        {
            _homePos = pos;
            _homeRot = rot;
            _homeScale = scale;
            // RECORD the home, but do NOT move a chip whose pose somebody else owns. Four owners,
            // and the two rounds of 2026-08-08/09 found them from opposite ends — the presence pass
            // (fly-out) and the item-area pass (clip/glide) each discovered one half of the same
            // rule, so they are stated together here rather than as two guards that could drift:
            //   • HELD — the base restores this home on release;
            //   • CLIPPED (PendingUse) — the chip is parented to the board's use slot, so writing a
            //     FAN-arc pose here would place it in SLOT-local space: the "die Karte liegt schräg"
            //     defect (full root cause on the skip in Relayout, which this belt backs up);
            //   • GLIDING (_releaseGlide) — the chip is flying home, and Update's lerp is already
            //     aimed at the very fields written above. Teleporting it here CANCELS that
            //     animation: ItemChip.OnRelease starts the glide and then, one line later, asks the
            //     owner to RefreshFanLayout (it must — the chip rejoins the arc and the tiling
            //     collider strips have to be re-derived), which landed straight in this method and
            //     snapped the card home. So the FIX-2 glide-back-don't-snap never actually played,
            //     and neither would the animated return this method's other callers promise;
            //   • EMERGING (_emerging) — the same argument for the OPEN animation, and it is a
            //     reachable case rather than a theoretical one: Relayout runs on every hand-sweep
            //     winner change, and the presence pass made the flight long enough (a 12-item fan
            //     deals for ~0.64 s) that a fingertip easily arrives inside it. TickEmerge re-reads
            //     _homePos/_homeRot/_homeScale every frame precisely so this costs nothing.
            // In every case the animation keeps converging on the NEW home, which is what a
            // relayout mid-flight should mean anyway.
            if (Holder != null || PendingUse || _releaseGlide > 0f || _emerging)
                return;
            transform.localPosition = pos;
            transform.localRotation = rot;
            transform.localScale = Vector3.one * scale;
        }

        // FIX 1 — take-into-hand reading pose: pinched between thumb and index, the card CENTER sitting
        // (0.5 − PinchGripFraction)·cardH above the pinch along the card up-axis, at InspectScale. This
        // is VRCard.GetHeldPose verbatim, except cardH is the ITEM card's own near-square held height
        // (_faceHeight at held scale) so the pinch grips the right spot on a near-square card. The base
        // snap seats this the instant the chip is grabbed; TickHeldPose then billboards the face.
        //
        // THE HEIGHT TERM IS THE ONLY LICENSED DIFFERENCE from the ability-card original, and it is
        // stated here because this copy has now drifted from it once (see the LEFT-HAND MIRROR note
        // below). Everything else — the HeldFaceBias face normal, the thumbSide card-up, the
        // LookRotation, the thumb/index pinch midpoint in GrabAnchor-local space, the HeldOffPalm/
        // HeldForward partial-rig fallback, the HeldPinchOffset fine-tune and the grip lift — must
        // stay byte-identical to <see cref="VRCard.GetHeldPose"/>, because a held item card and a
        // held ability card are the same gesture and the user judges them side by side.
        protected override HeldPose GetHeldPose(VRHand hand)
        {
            float scale = CardsConfig.InspectScale.Value;
            // Item cards are near-square — use the chip's OWN measured held height (not the tall ability
            // CardHeight) so the grip offset lifts the card the right amount out of the pinch.
            float cardH = (_faceHeight > 0.001f ? _faceHeight : CardsConfig.CardHeight) * scale;

            // GrabAnchor frame: +Y out of the palm, +Z along the fingers, ±X thumb side.
            float bias = CardsConfig.HeldFaceBias.Value * Mathf.Deg2Rad;
            var faceNormal = new Vector3(0f, Mathf.Cos(bias), -Mathf.Sin(bias));
            float thumbSide = hand.Side == HandSide.Right ? 1f : -1f;
            // Card +Z (away from the viewer) = −faceNormal; card top (+Y) = thumb side.
            var rot = Quaternion.LookRotation(-faceNormal, new Vector3(thumbSide, 0f, 0f));

            Vector3 pinchLocal;
            FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb);
            FingerJoints index = hand.Rig.GetFinger(Finger.Index);
            if (thumb.IsValid && index.IsValid)
            {
                Vector3 pinchWorld = (thumb.Tip.position + index.Tip.position) * 0.5f;
                pinchLocal = hand.Rig.GrabAnchor.InverseTransformPoint(pinchWorld);
            }
            else
            {
                pinchLocal = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
            }
            // LEFT-HAND MIRROR (user report 2026-08-09: "Die Position der Item-Karte in der linken
            // Hand ist falsch — das selbe Problem hattest du auch schonmal bei der linken Hand mit den
            // anderen Karten und dort behoben, wende bei den Item Karten den selben Fix an").
            //
            // ROOT CAUSE, and it is literally the ability cards' bug a second time: this method was
            // copied from VRCard.GetHeldPose BEFORE ba70e43 fixed it there, and it kept adding the
            // tuned [Cards] HeldPinchOffset RAW on both hands. That offset is authored on the RIGHT
            // hand (default X = −5.5 cm), but the two GrabAnchor frames are ANATOMICAL MIRRORS — +Y
            // out of the palm and +Z along the fingers on BOTH hands — so the lateral ±X axis
            // necessarily points to the THUMB side on the right hand and to the PINKY side on the
            // left (which is exactly why `thumbSide` above already flips sign per hand). Added raw,
            // the same X therefore shifted the card toward the thumb on one hand and toward the
            // pinky on the other: the left-hand item card missed the thumb/index pinch spot by
            // TWICE the tuned lateral offset, i.e. ~11 cm at the shipped value.
            //
            // Flip ONLY the X term for the left hand (Y and Z are anatomically symmetric): one tuned
            // value set, mirrored by construction — the same authored-right-mirrored-left convention
            // as VRCard.GetHeldPose, FigureGrabConfig.HeldFaceYawFor and VRHand's grip roll/yaw.
            Vector3 pinchOffset = CardsConfig.HeldPinchOffset.Value;
            if (hand.Side == HandSide.Left)
                pinchOffset.x = -pinchOffset.x;
            pinchLocal += pinchOffset;

            Vector3 pos = pinchLocal + rot * new Vector3(0f, cardH * (0.5f - PinchGripFraction), 0f);
            return new HeldPose(pos, rot, scale);
        }

        public override void OnGrab(VRHand hand)
        {
            // GRAB-EDGE FORENSICS (see UnclipFromSlot's root-cause note): this defect has now been
            // mis-diagnosed twice from logs that only said a grab was ATTEMPTED. Record what the
            // chip actually was before the grab, so the line below can state what it BECAME.
            bool wasClipped = PendingUse;
            string parentBefore = transform.parent != null ? transform.parent.name : "<none>";

            // Leave the use slot BEFORE the base records the pre-grab parent. A clipped chip is a
            // CHILD of the slot (ClipIntoSlot), and base.OnRelease restores exactly the parent it saw
            // here — so grabbing a clipped card and dropping it elsewhere would have put it back under
            // the SLOT while its glide-home target (_homePos) is fan-root local. Handing it to the fan
            // root first (world pose preserved) keeps the whole grab/release path in one frame of
            // reference, so a cancel really does return the card to the deck.
            _owner?.UnclipChip(this);
            // PendingUse means EXACTLY "this card is lying in the board's recess". A card in a hand
            // is not, so the flag drops at the grab rather than one tick later, when the owner's
            // per-tick service notices Holder != null and backs the decision out through its game
            // seam (which still happens, and is still what un-does the game-side half — all three
            // flows detect the take-back by Holder, never by this flag). Without the clear there is
            // a window in which a grabbed chip is "clipped": Update would take the settle branch and
            // freeze it, and Relayout would skip it, for a card the player is holding.
            PendingUse = false;
            // Drop the pop so the grabbed chip starts from a clean pose — the recess lift included
            // (the card is IN the hand now; the grabber's own un-highlight edge would clear it a
            // frame later anyway, and a lift that outlived the grab by a frame is a flicker).
            _fingerPopped = false;
            _laserPopped = false;
            _recessPopped = false;
            _pop = 0f;
            // A chip in the hand (or clipped into the use slot) is no longer part of the arc: give
            // it its FULL grab box back so pulling it out of the slot again stays easy, and clear
            // any sweep suppression it was carrying. Relayout re-strips it when it rejoins the arc.
            SetGrabStrip(float.MaxValue);
            ClearHandSuppressed();
            // FIX 1 — keep the world pose across the base re-parent so TickHeldPose flies the chip from
            // its fan slot INTO the hand instead of teleporting (mirror of VRCard.OnGrab). The base snap
            // seats GetHeldPose; we capture that local target, then restore the pre-grab world pose and
            // let TickHeldPose ease in.
            Vector3 worldPos = transform.position;
            Quaternion worldRot = transform.rotation;
            Vector3 worldScale = transform.localScale;
            base.OnGrab(hand); // snaps to the reading pose at GetHeldPose
            _heldPos = transform.localPosition;
            _heldRot = transform.localRotation;
            _heldScale = transform.localScale.x;
            transform.position = worldPos;
            transform.rotation = worldRot;
            transform.localScale = worldScale;
            _releaseGlide = 0f; // re-grab mid-glide: the held pose takes over cleanly (FIX 2)
            hand.SendHaptic(HapticPreset.ClickPulse);
            // ITEM 4 (card sounds): the SAME pluck/grab SFX ability cards play on grab (laser-pluck
            // routes through OnPoke → ForceGrab → OnGrab, so it fires there too — no double sound).
            CardsDriver.PlayCardSound(CardsConfig.CardGrabSound.Value, transform);
            // THE GRAB EDGE, STATED IN FULL. Holder / parent / world+local pose / held target, so
            // the next hardware log answers "what did the chip actually become?" without a fourth
            // guess. The parent MUST read as the hand's GrabAnchor from here until the release: any
            // later line showing this chip parented to the fan root or the board while Holder is
            // still set is the ripped-out-of-the-hand defect (UnclipFromSlot) coming back.
            Transform? p = transform.parent;
            Vector3 wp = transform.position;
            Vector3 lp = transform.localPosition;
            VRLog.Info("Cards", $"Item chip '{name}' taken into hand ({hand.Side}) — readable (state {State}). " +
                                $"GRAB EDGE: fromRecess={wasClipped} parent '{parentBefore}' → " +
                                $"'{(p != null ? p.name : "<none>")}' (anchor='{hand.Rig.GrabAnchor.name}', " +
                                $"match={ReferenceEquals(p, hand.Rig.GrabAnchor)}), Holder={Holder?.Side.ToString() ?? "<null>"}, " +
                                $"world=({wp.x:F3},{wp.y:F3},{wp.z:F3}) local=({lp.x:F3},{lp.y:F3},{lp.z:F3}) → " +
                                $"heldTarget=({_heldPos.x:F3},{_heldPos.y:F3},{_heldPos.z:F3}) scale {_heldScale:F3}.");
        }

        /// <summary>
        /// Requirement 3 (clip-in to use): capture the drop location BEFORE the base restores the
        /// chip to its fan home, then offer it to the board's item-use slot. The base restore always
        /// runs, so a chip dropped anywhere else (or a non-activatable chip) simply returns to the fan.
        /// </summary>
        public override void OnRelease(VRHand hand, Vector3 velocity)
        {
            // While held the chip sits at the hand's grab anchor, so its world position IS the drop
            // point. Capture it before base.OnRelease re-parents it back to the fan.
            Vector3 dropWorldPos = transform.position;
            // FIX 2 — glide back, don't snap: base.OnRelease restores the pre-grab LOCAL pose (an
            // instant teleport to the fan slot). Mirror VRCard.OnRelease — keep the world pose across
            // the re-parent so the chip stays at the release point, then Update's home-lerp flies it
            // back to its fan home over ReleaseGlideSeconds (unscaled, so it plays even while paused).
            Quaternion worldRot = transform.rotation;
            Vector3 worldScale = transform.localScale;

            // HAND-TO-HAND TRANSFER (requirement 6): this release is the FIRST half of a handover —
            // the card never left the hands, so NONE of the drop routing below may run (no clip-in
            // offer, no glide home, no arc relayout) and no frame may observe the chip un-held. Keep
            // the world pose across the base re-parent and let the receiving hand adopt it inside
            // this very call stack; its OnGrab then eases the chip from exactly here into the new
            // pinch. Mirrors CardsDriver.OnCardReleased's transfer branch for ability cards.
            if (_owner != null && _owner.IsTransferring(this))
            {
                base.OnRelease(hand, velocity);
                transform.position = dropWorldPos;
                transform.rotation = worldRot;
                transform.localScale = worldScale;
                _releaseGlide = 0f; // the adopting hold owns the pose now — no fan glide may start
                if (_owner.CompleteChipTransfer(this, hand))
                    return;
                // Both hands refused: fall through to the normal routing as the last honest resort.
                _releaseGlide = ReleaseGlideSeconds;
                _owner.RefreshFanLayout();
                return;
            }

            base.OnRelease(hand, velocity); // detach + restore fan home (pre-grab local pose)
            transform.position = dropWorldPos;
            transform.rotation = worldRot;
            transform.localScale = worldScale;
            _releaseGlide = ReleaseGlideSeconds;
            _owner?.OnChipReleased(this, dropWorldPos, hand);
            // Re-derive the arc's collider strips: this chip carried a FULL grab box while it was
            // in the hand (see OnGrab) and is about to glide back into the arc, where a full box
            // would overlap its neighbours again and re-break the sweep for exactly one card.
            _owner?.RefreshFanLayout();
            // Card sounds: a release that CLIPS into the use slot plays the place "thunk" (fired in
            // OnChipReleased, which sets PendingUse). A release that merely glides back to the fan
            // plays NOTHING — deliberately.
            //
            // It used to play the soft take-back click here, on the claim that this matched the
            // ability cards' SFX set. It did not: CardsDriver only plays that click when a card is
            // pulled back off the FIELD during a pick reopen, precisely because the game itself is
            // silent in that one case. An ability card released back into the fan makes no mod sound
            // at all. So the item chip was the noisier of the two, which is exactly what the user
            // objected to ("die Item-Karten sollten die selben und nicht mehr Geräusche machen als
            // die anderen Karten auch"). Grab and place remain, and now match one-for-one.
        }

        // ---- pop (readability) -----------------------------------------------------

        /// <summary>
        /// ITEM #1 + #7 — per-frame face upkeep, run in every state (held, popped, settled, glide):
        /// (1) re-run the mip-bake sprite swap on a slow cadence until the async background art has
        /// baked (mirror of <c>CardFace.Maintain</c>'s <see cref="MipRescanInterval"/> cadence — async
        /// arrivals / state changes keep putting the mipless originals back), and (2) LIVE re-evaluate
        /// the playable gate from <see cref="IsActivatable"/> so exactly the cards that CAN be used
        /// right now wear the soft gold frame and every other card stays untouched. Usability changes with
        /// turn/phase, so it is polled every frame, never cached. Change-gated (both are no-ops unless
        /// due), so the per-frame cost is a clock compare + a bool compare.
        /// </summary>
        private void TickFaceMaintenance()
        {
            // ITEM #5 — keep the WorldSpace face canvas bound to the head camera (mirror of
            // VRCard.UpdateCanvasCamera). A WorldSpace canvas renders on its LAYER regardless of
            // worldCamera, so this alone can't decide mirror visibility — the deep diagnostic below logs
            // exactly what the item face is (layer, canvas modes, which cameras would draw it) so the next
            // hardware log pinpoints why a held item face is/ isn't in the desktop mirror vs an ability face.
            if (_faceCanvas != null)
            {
                Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
                if (head != null && _faceCanvas.worldCamera != head)
                    _faceCanvas.worldCamera = head;
                if (!s_loggedMirror && head != null)
                    LogMirrorDiag(head);
            }

            // ITEM #1 (de-shimmer immediately) — tight per-frame poll for the async background art; the
            // instant the hosted card's cardBackground sprite is present, Rescan so the mip-baked face
            // lands with ZERO shimmer window (instead of up to ~1.5 s later on the slow cadence).
            if (!_artBaked && _cardUI != null && Time.unscaledTime <= _tightArtPollUntil)
            {
                Image? bg = _cardUI.cardBackground;
                if (bg != null && bg.sprite != null)
                {
                    _artBaked = true;
                    CardFaceMipBake.Rescan(_cardUI); // art just arrived — bake it NOW
                    _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                    if (!s_loggedArtBaked)
                    {
                        s_loggedArtBaked = true;
                        VRLog.Info("Cards", "ITEM #1: background art detected on host — mip-baked immediately " +
                                            "(tight per-frame poll), so the item card no longer shimmers for ~1.5 s.");
                    }
                }
            }

            // Ongoing maintenance cadence (async re-arrivals / game sprite reassignments put mipless
            // originals back — swap them for the baked copies again), same 1 s cadence as CardFace.
            if (_cardUI != null && Time.unscaledTime >= _nextMipRescan)
            {
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                CardFaceMipBake.Rescan(_cardUI);
            }

            // USABLE HIGHLIGHT (live) — light the soft gold FRAME when this card CAN be played RIGHT NOW.
            // Turn-aware: the owner gates on IsActionTurn AND IsActivatable, so off-turn nothing glows
            // and on-turn exactly the playable items do. Polled every frame (usability moves with
            // turn/phase and with every item spent) but change-gated, so the steady-state cost is one
            // bool compare. Non-usable cards get NO treatment at all — they simply keep their natural
            // look, which is the whole point of flipping the cue from "dim the rest" to "light these".
            int want = (_owner != null && _owner.CanUseNow(this)) ? 1 : 0;
            if (want != _usabilityShown)
            {
                _usabilityShown = want;
                if (_usableFrame != null && _usableFrame.activeSelf != (want == 1))
                    _usableFrame.SetActive(want == 1); // frame ON only while usable
            }
        }

        private static bool s_loggedArtBaked;

        /// <summary>
        /// ITEM #5 (desktop-mirror investigation) — one-shot deep diagnostic: dump exactly what the hosted
        /// item face is so the hardware log can compare it to a held ABILITY face. Logs the item card's
        /// render layer + our FaceCanvas mode/camera, every nested Canvas on the hosted ItemCardUI (mode /
        /// worldCamera / overrideSorting — a nested Screen-Space canvas would render only through its own
        /// camera and never reach the head mirror), and every enabled Camera whose cullingMask includes the
        /// item's layer (so the log reveals whether the head camera — and any separate avatar/desktop mirror
        /// camera — actually draws it). No behaviour change; pure evidence.
        /// </summary>
        private void LogMirrorDiag(Camera head)
        {
            s_loggedMirror = true;
            try
            {
                int layer = _cardGo != null ? _cardGo.layer : -1;
                var sb = new StringBuilder(256);
                sb.Append("ITEM #5 MIRROR DIAG: item face layer=").Append(layer)
                  .Append(" ('").Append(layer >= 0 ? LayerMask.LayerToName(layer) : "?").Append("'), FaceCanvas ")
                  .Append(_faceCanvas != null ? _faceCanvas.renderMode.ToString() : "null")
                  .Append(" worldCam=").Append(_faceCanvas != null && _faceCanvas.worldCamera != null ? _faceCanvas.worldCamera.name : "null")
                  .Append("; head '").Append(head.name).Append("' mask 0x").Append(head.cullingMask.ToString("X8"))
                  .Append(" rendersItemLayer=").Append(layer >= 0 && (head.cullingMask & (1 << layer)) != 0);

                if (_cardGo != null)
                {
                    Canvas[] nested = _cardGo.GetComponentsInChildren<Canvas>(true);
                    sb.Append("; nested canvases=").Append(nested.Length);
                    for (int i = 0; i < nested.Length && i < 4; i++)
                    {
                        Canvas c = nested[i];
                        if (c == null || ReferenceEquals(c, _faceCanvas))
                            continue;
                        sb.Append(" [").Append(c.name).Append(':').Append(c.renderMode)
                          .Append(c.overrideSorting ? " override" : "")
                          .Append(" cam=").Append(c.worldCamera != null ? c.worldCamera.name : "null").Append(']');
                    }
                }

                int drawers = 0;
                if (layer >= 0)
                {
                    Camera[] cams = Camera.allCameras;
                    for (int i = 0; i < cams.Length; i++)
                        if (cams[i] != null && (cams[i].cullingMask & (1 << layer)) != 0)
                        {
                            drawers++;
                            sb.Append("; draws:'").Append(cams[i].name).Append('\'');
                        }
                }
                sb.Append(" (").Append(drawers).Append(" camera(s) render the item layer). " +
                          "If only the head camera draws it, the desktop mirror IS the head mirror and the item " +
                          "SHOULD be visible; if a separate avatar/desktop-mirror camera exists but is absent here, " +
                          "its cullingMask excludes the item layer — that is the miss.");
                VRLog.Info("Cards", sb.ToString());
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Cards", $"ITEM #5 mirror diag skipped ({e.Message}).");
            }
        }

        /// <summary>Fingertip hand-sweep pop (set by the owner's single-winner sweep).</summary>
        internal void SetFingertipPop(bool on) => _fingerPopped = on;

        /// <summary>
        /// True while this chip is the one singled out in the fan — the ITEM-fan counterpart of
        /// <c>VRCard.IsHighlighted</c>, and the predicate the MULTIPLAYER hover channel reads
        /// through <see cref="ItemsPile.HighlightedIndex"/>.
        ///
        /// <para>MP test 2026-08-07 ("Beim Hovern mit dem Laser über eine Pile/Item-Karte wird das
        /// Highlight nicht synchronisiert; mit der Hand schon"). Both pop paths write the same
        /// visual state — <c>Tick</c> tweens on <c>_fingerPopped || _laserPopped</c> — but only the
        /// FINGERTIP one had a public reader, so extension record 6 carried the hand sweep and
        /// nothing else. Covering both here is exactly what <c>VRCard.IsHighlighted</c> already
        /// does for the pile browser and the hand fan, which is why THOSE synced on the laser.</para>
        ///
        /// <para>A held chip and a chip clipped into the use slot are excluded: neither is at a fan
        /// position any more, so neither has an index a peer could mirror.</para>
        /// </summary>
        internal bool IsHighlighted => Holder == null && !PendingUse && (_fingerPopped || _laserPopped);

        /// <summary>
        /// <see cref="IGrabHighlight"/> — <c>ProximityGrabber</c> made this chip
        /// <paramref name="hand"/>'s grab candidate (or stopped doing so). The flag is written
        /// unconditionally and READ only while the chip lies in the recess
        /// (<see cref="RecessHighlighted"/>), which is the whole of the arbitration: for a chip
        /// standing in the ARC the owner's single-winner sweep owns the lift and a second, grabber-
        /// driven pop would be exactly the "what lights up is not what I get" defect that sweep was
        /// written to end. Writing it always (rather than gating the write on
        /// <see cref="PendingUse"/>) is what keeps it stale-proof across the grab: the clear arrives
        /// on the grabber's own un-highlight edge even though the flag stopped being read the moment
        /// <see cref="OnGrab"/> dropped <see cref="PendingUse"/>.
        ///
        /// <para>The HAPTIC that goes with this lift is not sent here and must not be: the grabber
        /// itself plays <see cref="HapticPreset.HoverTick"/> on this very edge
        /// (<c>ProximityGrabber.UpdateHighlight</c>), debounced by its own
        /// <c>SwitchMarginMeters</c> hysteresis. Same preset, same edge, same debounce as the
        /// ability card and as the item fan's own chips — and sending a second one from here would
        /// make the recess card the loudest object on the board.</para>
        /// </summary>
        public void OnGrabHighlight(VRHand hand, bool highlighted)
        {
            _ = hand;
            _recessPopped = highlighted;
        }

        /// <summary>
        /// TRUE while the card LYING IN THE USE RECESS is being singled out — the recess counterpart
        /// of <see cref="IsHighlighted"/> (which deliberately answers false here, because a clipped
        /// chip has no ARC position for a peer to mirror through the fan-highlight record).
        ///
        /// <para>Both hover channels, exactly as the ability card combines its own: the hand
        /// (<see cref="_recessPopped"/> — the grabber highlight, see that field) and the beam
        /// (<see cref="_laserPopped"/> — <see cref="OnPokeEnter"/>, which is the same
        /// <c>SetLaserHover</c> + <c>HoverTick</c> pair <c>CardsDriver.UpdateBoardLaser</c> gives a
        /// slot-docked card).</para>
        ///
        /// <para>AND THE GRAB-PROMISE GATE, which is not decoration: a lift is the mod's promise that
        /// a grab would be taken (<c>VRCard</c> enforces the same thing through its ROOTED
        /// predicate — "a card that cannot be grabbed in this phase makes no grab promise: zero pop,
        /// zero scale change"). The ONE placed card that refuses every hand is the one whose ACTIVE
        /// BONUS the game has LOCKED (<see cref="ItemsPile.PlacedCardIsLocked"/> —
        /// <c>CActiveBonus.IsToggleLocked</c>): it may not budge, and lifting it would advertise a
        /// take-back the rules will not perform. The hand channel gates itself (the grabber never
        /// highlights a chip whose <see cref="AllowsHand"/> is false), the BEAM does not — hence the
        /// explicit term. Nothing about the refusal itself changes: the deliberate laser click still
        /// reaches <see cref="ItemsPile.RefuseLockedBonusRemoval"/>, which is where the player is
        /// told why.</para>
        /// </summary>
        internal bool RecessHighlighted =>
            PendingUse && Holder == null && CanGrab
            && (_owner == null || !_owner.PlacedCardIsLocked(this))
            && (_recessPopped || _laserPopped);

        /// <summary>True while the dominant index tip / palm is within this chip's collider reach.</summary>
        internal bool TryFingertipDistance(Vector3 worldPoint, out float distance)
        {
            distance = float.MaxValue;
            if (_box == null || !_box.enabled || !_box.gameObject.activeInHierarchy)
                return false;
            distance = Vector3.Distance(worldPoint, _box.ClosestPoint(worldPoint));
            return true;
        }

        // ---- single-winner grab gate (mirrors VRCard._handPopSuppressed / AllowsHand) ----

        /// <summary>The hand this chip refuses because the owner's sweep gave that hand a
        /// DIFFERENT winner (null = refuses nobody). Per-hand, not a static: the item fan sweeps
        /// both hands and each hand must keep its own independent candidate.</summary>
        private VRHand? _suppressedForHand;

        /// <summary>
        /// SECOND refused hand — filled when a second suppressor names a different hand while the
        /// first is already up. This is the slot the mirror was MISSING (user ruling 2026-08-08,
        /// "Item-Karten … sollen wie normale Karten reagieren"): the item fan runs TWO per-hand
        /// elections in the same tick (<see cref="ItemsPile.UpdateHandSweep"/>), so one chip can
        /// lose BOTH and must refuse BOTH hands — with a single slot the second write silently
        /// re-opened the first hand's grab gate on a chip that hand had NOT elected. Byte-for-byte
        /// the shape <see cref="VRCard"/> already carries
        /// (<c>_handPopSuppressedFor</c> / <c>_handPopSuppressedFor2</c>) and for the same reason.
        /// </summary>
        private VRHand? _suppressedForHand2;

        /// <summary>
        /// Set by <see cref="ItemsPile.UpdateHandSweep"/>: this chip lost the election for
        /// <paramref name="hand"/> and must stay out of that hand's proximity grab, so the chip
        /// that POPPED is the chip the trigger takes ("what lights up is what I get").
        /// ACCUMULATIVE, exactly like <c>VRCard.SetHandPopSuppressed</c>: a second call naming a
        /// DIFFERENT hand ADDS it instead of replacing the first. The suppressor re-derives its
        /// whole set from scratch every tick and clears through
        /// <see cref="ClearHandSuppressed"/>, so the pair can never go stale.
        /// </summary>
        internal void SetHandSuppressed(VRHand? hand)
        {
            if (hand == null)
                return;
            if (_suppressedForHand == null || ReferenceEquals(_suppressedForHand, hand))
                _suppressedForHand = hand;
            else if (!ReferenceEquals(_suppressedForHand2, hand))
                _suppressedForHand2 = hand;
        }

        /// <summary>Drop both refused hands (the per-tick re-derive, and every point where this chip
        /// leaves the arc — a grab, an explicit pluck, the fan closing).</summary>
        internal void ClearHandSuppressed()
        {
            _suppressedForHand = null;
            _suppressedForHand2 = null;
        }

        /// <summary>Per-hand grab/hover gate (<see cref="IGrabbableHandFilter"/>): a chip that lost
        /// the hand sweep is invisible to that hand's <c>ProximityGrabber</c> — no highlight, no
        /// grab, no haptic. The laser path is untouched (it arbitrates itself).
        ///
        /// <para>A chip CLIPPED into the use slot (<see cref="PendingUse"/>) allows EVERY hand,
        /// unconditionally. It lies in the board's recess, not in the arc, so an arc election can
        /// never be a statement about it — and refusing it is exactly what made a placed card
        /// un-pickable by hand (requirement 5b). The owner already skips it when it re-derives the
        /// suppression set; this is the belt, because a single stale flag here would silently cost
        /// the player their card back.</para>
        ///
        /// <para>THE ONE CARD THAT REFUSES EVERY HAND: a placed card whose ACTIVE BONUS the game has
        /// LOCKED (<see cref="ItemsPile.PlacedCardIsLocked"/>). The rules will not take that toggle
        /// back — <c>UIUseActiveBonus.ClearSelection</c> does nothing at all while
        /// <c>IsToggleLocked</c> — so a card that could be lifted out would either lie about the
        /// state or have to be snatched back out of a closed fist. Refusing here means it simply does
        /// not budge: no highlight, no haptic, no grab. The deliberate LASER click still reaches
        /// <see cref="ItemsPile.RefuseLockedBonusRemoval"/>, which is where the player is TOLD why.
        /// It stops refusing on its own the moment the game resolves the bonus.</para></summary>
        public bool AllowsHand(VRHand hand) =>
            (_owner == null || !_owner.PlacedCardIsLocked(this))
            && (PendingUse
                || (!ReferenceEquals(hand, _suppressedForHand)
                    && !ReferenceEquals(hand, _suppressedForHand2)));

        /// <summary>
        /// Shrink this chip's grab box to its VISIBLE strip in FAN-local metres (see
        /// <see cref="FanSweep.StripWidth"/>), or <see cref="float.MaxValue"/> for the full face.
        /// The strip is measured ALONG THE ARC, which for a SPENT chip — rolled 90° in its slot so
        /// it reads as "tapped" — is the box's local Y, not X. Getting that axis wrong would leave
        /// a tapped chip with a collider covering both its neighbours, i.e. exactly the overlap
        /// this is here to remove. Idempotent: an unchanged box is not rewritten (a per-frame
        /// physics-shape dirty for every chip is the cost this guard avoids).
        /// </summary>
        internal void SetGrabStrip(float stripFanLocal)
        {
            if (_box == null)
                return;
            float fullW = FaceWidth + ColliderMargin;
            float fullH = FaceHeight + ColliderMargin;
            bool tapped = State == Visual.Spent; // rolled 90°: the arc runs along local Y
            float along = tapped ? fullH : fullW;
            float strip = Mathf.Clamp(stripFanLocal, along * 0.25f, along);
            float offset = -(along - strip) * 0.5f;
            Vector3 size = tapped
                ? new Vector3(fullW, strip, ColliderDepth)
                : new Vector3(strip, fullH, ColliderDepth);
            Vector3 center = tapped ? new Vector3(0f, offset, 0f) : new Vector3(offset, 0f, 0f);
            if (_box.size == size && _box.center == center)
                return;
            _box.size = size;
            _box.center = center;
        }

        // ---- IFanSweepTarget (explicit: no widening of the chip's surface) ----

        /// <summary>A held chip rides a hand and a chip clipped into the use slot is awaiting a
        /// decision (#6) — neither may win the sweep, or a dead chip would suppress the lift of a
        /// live one beside it.</summary>
        bool IFanSweepTarget.SweepEligible => Holder == null && !PendingUse;

        /// <summary>The chip's rendered face width in WORLD units. This is the number that makes
        /// the reach board-scale-invariant: an item fan on a 0,32× board reports ~2,5 real cm here
        /// where the same fan on the 0,80× default reports ~6,3, and the reach follows.</summary>
        float IFanSweepTarget.SweepFaceWidthWorld => FaceWidth * transform.lossyScale.x;

        bool IFanSweepTarget.TrySweepDistance(Vector3 worldPoint, out float distance)
            => TryFingertipDistance(worldPoint, out distance);

        string IFanSweepTarget.SweepName => name;

        // ---- hand-CONTACT geometry (laser stand-down, user report 2026-08-08 round 2) ----

        /// <summary>
        /// The chip's FACE as a world-space rectangle — centre, plane normal, unit right/up axes and
        /// world-metre half-extents — either where the chip VISIBLY IS right now
        /// (<paramref name="resting"/> false: the live transform, hover pop and ×1.18 grow included)
        /// or where it RESTS in the arc (true: the home pose under the current parent, pop excluded).
        /// The item-fan twin of <see cref="VRCard.TryGetLiveLaserRect"/> /
        /// <see cref="VRCard.TryGetRestingLaserRect"/>, sized to the chip's OWN near-square face
        /// (<see cref="FaceWidth"/>/<see cref="FaceHeight"/>) — an item card is not the tall ability
        /// rect, and a rect built from the ability aspect would be a centimetre too tall on every
        /// chip. A rolled ("tapped", spent) chip needs no special case: the roll lives in the
        /// transform/home rotation, and the face still spans local X by <see cref="FaceWidth"/>.
        ///
        /// <para>WHY BOTH POSES (the lesson <c>CardsDriver.TryHitLiftedCard</c> already learned for
        /// the ability fan): the chip pops 2 cm toward the viewer and grows the instant the hand
        /// elects it, which is exactly the moment the contact test below is asked whether the hand is
        /// touching it. Testing only the resting rect asks about a rectangle the chip has already
        /// left; testing only the live rect loses the chip mid-glide (release glide, arc relayout).
        /// Accepting either keeps it contactable throughout.</para>
        ///
        /// <para>The RESTING pose is refused for a held or clipped-in chip: its home is still the
        /// ARC slot while the chip itself rides a hand or sits in the board's use slot, so that rect
        /// is a phantom somewhere else in the world and a hand passing through it must not read as a
        /// touch. The live rect stays valid in both cases.</para>
        /// </summary>
        internal bool TryGetFaceRect(bool resting, out Vector3 center, out Vector3 normal,
            out Vector3 right, out Vector3 up, out float halfWidth, out float halfHeight)
        {
            center = default;
            normal = default;
            right = default;
            up = default;
            halfWidth = 0f;
            halfHeight = 0f;
            Transform t = transform;
            float lossy;
            if (resting)
            {
                Transform? parent = t.parent;
                if (parent == null || Holder != null || PendingUse)
                    return false;
                center = parent.TransformPoint(_homePos);
                Quaternion rot = parent.rotation * _homeRot;
                right = rot * Vector3.right;
                up = rot * Vector3.up;
                normal = rot * Vector3.forward; // chips face the viewer with −Z, like the cards
                lossy = parent.lossyScale.x * _homeScale;
            }
            else
            {
                center = t.position;
                right = t.right;
                up = t.up;
                normal = t.forward;
                lossy = t.lossyScale.x;
            }
            halfWidth = FaceWidth * 0.5f * lossy;
            halfHeight = FaceHeight * 0.5f * lossy;
            return halfWidth > 1e-5f && halfHeight > 1e-5f;
        }

        private void Update()
        {
            if (_collapsing)
            {
                // Req #5 — self-glide into the pile, then destroy (OnDisable recycles the card widget).
                //
                // PRESENCE PASS: the same parametric treatment as the fly-out, played backwards. It
                // used to be the same shapeless exponential-toward-a-point the emerge was: every chip
                // starting at once, no wind-up, and a hard Destroy at a fixed 0.26 s that cut the lerp
                // off wherever it happened to be (the chip was still ~20 % short of the stack when it
                // vanished — a small pop at the end of a "smooth" animation). Now the curve REACHES
                // the stack, and the destroy happens because it arrived, not because a timer expired.
                float cdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                _collapseTime += cdt;
                float cdur = Mathf.Max(0.01f, CardsConfig.ItemFanCloseDuration.Value);
                float ct = Mathf.Clamp01((_collapseTime - _collapseDelay) / cdur);
                float ce = EaseInBack(ct, Overshoot());
                // Unclamped so the wind-up (ce < 0 early on) actually lifts the chip AWAY from the
                // stack for a moment — the anticipation that tells the eye where the card is about to
                // go before it goes there.
                transform.position = Vector3.LerpUnclamped(_collapseFrom, _collapseWorld, ce);
                transform.rotation = _collapseFromRot * Quaternion.Slerp(Quaternion.identity, _collapseSpin, ct);
                transform.localScale = Vector3.one
                                     * Mathf.LerpUnclamped(_collapseFromScale, _collapseFromScale * SeedScale(), ce);
                if (ct >= 1f)
                    Object.Destroy(gameObject);
                return;
            }

            if (_useFxActive) // Req #6 — post-confirm burn/tap flourish, then collapse into the deck
            {
                TickUseFx();
                return;
            }

            TickFaceMaintenance(); // ITEM #1 (de-shimmer) + live usable-highlight frame — held or not

            if (Holder != null)
            {
                // A grab mid-fly-out CANCELS the fly-out (it does not pause it): the held pose owns
                // the chip from here, and on release the ordinary post-release home glide takes over.
                // Without this the emerge would still be live when the chip is dropped and would yank
                // it back to the stack seed — the pop the standing ruling forbids, arriving by the
                // back door of a state nobody cleared.
                _emerging = false;
                TickHeldPose(); // FIX 1 — track the wrist + billboard the face every frame while held
                return;
            }

            // Req #6 — clipped into the use slot, waiting for the decision: nothing to do ONCE the
            // settle has landed. The chip is a child of the slot at an exact zero local pose, so it
            // is held there by the transform hierarchy — rigid, free, and with no residual error.
            // Any per-frame pose work in the settled state would be the swim-behind-the-head bug
            // coming back, which is why the settle is a bounded window (requirement 5a) and not a
            // permanent chase: it converges on the slot frame and then stops writing entirely.
            if (PendingUse)
            {
                if (_clipSettle > 0f)
                {
                    // THE PLACEMENT LANDS FIRST, THEN THE HOVER LIFTS. The settle is a bounded
                    // window that eases the card onto the slot's own frame; letting the hover ramp
                    // run underneath it would give the same transform two disagreeing targets in the
                    // same frame and, worse, would leave the pop already at 1 when the settle's final
                    // exact assignment lands — a visible jump the instant the window closed. Holding
                    // the ramp at zero means the lift always starts from the seated pose and eases up
                    // over its own 1/8 s, so nothing pops.
                    _pop = 0f;
                    TickClipSettle();
                    return;
                }
                TickRecessPop();
                return;
            }

            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // unscaled: pop/glide play while paused
            bool popped = _fingerPopped || _laserPopped;
            _pop = Mathf.MoveTowards(_pop, popped ? 1f : 0f, PopLerpSpeed * udt);
            // THE LIFT, on top of the arc home, exactly as VRCard applies it: UP out of the arc, a
            // push toward the viewer along local -Z, and 18 % bigger. See the PopUp/PopScale block.
            //
            // WHY THE OFFSET IS FAN-LOCAL AND NOT ROTATED BY _homeRot, where VRCard rotates by its
            // own home rotation. Two reasons, both of them properties of THIS fan:
            //   * A SPENT item lies "tapped" — Relayout gives it an extra 90° roll (requirement 3).
            //     Lifting along that chip's OWN up would send a tapped card sideways out of the arc
            //     while its neighbours rise, i.e. the one card the player most needs to read would
            //     move in a direction nothing else in the fan does.
            //   * The item arc's roll is small (-angle·0.85 over a capped sweep) where the palm fan's
            //     is not, so fan-local up and card-local up differ by a few percent for every chip
            //     that is NOT tapped. The fan root billboards the head every frame (Tick), so
            //     fan-local +Y is the viewer's up: the motion the report asks for, "nach oben".
            // The forward term is unaffected by the choice either way — every rotation in this arc is
            // a ROLL about Z, and a roll leaves Z alone. (Net.RemoteItemFan.Layout applies the same
            // two terms the same way, which is what keeps the mirrored fan 1:1.)
            float popForward = CardsConfig.FanSelectedPopForward != null
                ? CardsConfig.FanSelectedPopForward.Value : Defaults.FanSelectedPopForward;
            Vector3 posTarget = _homePos + new Vector3(0f, PopUp * _pop, -popForward * _pop);
            float scaleTarget = _homeScale * (1f + (PopScale - 1f) * _pop);

            if (_emerging)
            {
                // Req #5 (presence pass) — the staggered, arced, over-shooting fly-out of the items
                // stack. Checked BEFORE the release glide because BeginEmerge clears that one: the two
                // must never drive the same transform in the same frame.
                TickEmerge(udt, posTarget, scaleTarget);
            }
            else if (_releaseGlide > 0f)
            {
                // FIX 2 — post-release glide: exponential ease toward the fan home (position + rotation
                // + scale) at CardLerpSpeed on unscaled time, so a released chip flies back to its slot
                // instead of snapping. The window converges ~99 % by ReleaseGlideSeconds; the tiny
                // residual settle below is imperceptible (same easing/feel as VRCard's home-lerp).
                _releaseGlide -= udt;
                float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * udt);
                transform.localPosition = Vector3.Lerp(transform.localPosition, posTarget, t);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _homeRot, t);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scaleTarget, t);
            }
            else
            {
                // Settled: apply the pop directly on the arc home (unchanged steady-state behavior).
                transform.localScale = Vector3.one * scaleTarget;
                transform.localPosition = posTarget;
            }
        }

        /// <summary>
        /// FIX 1 — fly-in / read-in-hand pose: the chip sits at the pinch point (localPosition lerped in
        /// GrabAnchor-local space, so it tracks the wrist 1:1) but per-frame BILLBOARDS its face to the
        /// head, and lerps scale — VRCard.TickHeldPose verbatim, so a held item card is at the same
        /// place/orientation as a held ability card (at the pinch, always facing the player).
        /// </summary>
        private void TickHeldPose()
        {
            float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, _heldPos, t);
            Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (head != null)
            {
                Vector3 away = transform.position - head.transform.position; // card +Z away from viewer
                if (away.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(away.normalized, head.transform.up), t);
            }
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _heldScale, t);
        }

        // ---- IPokeable (laser hover + pluck; the board laser drives these) ----------

        public void OnPokeEnter(VRHand hand)
        {
            if (Holder != null)
                return;
            // THE GRAB-PROMISE GATE, the laser half (VRCard.OnPokeEnter's "rooted gate", user bug B:
            // "zero pop, zero haptic while the card refuses grabs"). It is a single case here and it
            // is the one card on this board that refuses EVERY hand: a placed card whose ACTIVE BONUS
            // the game has LOCKED (ItemsPile.PlacedCardIsLocked / CActiveBonus.IsToggleLocked). It may
            // not be lifted out, so it may not be lifted at all — buzzing and popping it would
            // advertise a take-back the rules will not perform. Only reachable since the recess card
            // renders its hover (TickRecessPop); before that the flag set here was invisible for a
            // clipped chip. Nothing about the refusal changes: a deliberate CLICK still runs
            // ItemsPile.RefuseLockedBonusRemoval, which is where the player is told why.
            //
            // ARC chips are deliberately NOT gated on AllowsHand: the sweep's per-hand suppression is
            // a statement about the HAND's grab, and the laser arbitrates itself (see AllowsHand).
            if (_owner != null && _owner.PlacedCardIsLocked(this))
                return;
            _laserPopped = true;
            hand.SendHaptic(HapticPreset.HoverTick);
        }

        public void OnPokeExit(VRHand hand) => _laserPopped = false;

        public void OnPoke(VRHand hand)
        {
            // Laser trigger (or fingertip poke) on the chip → pluck it into the hand to read,
            // exactly like the ability-card browse laser (ForceGrab, released on trigger-up).
            if (Holder != null || !CanGrab)
                return;
            _laserPopped = false;

            // ─── THE PLACED CARD: THE FAR LASER PUTS IT BACK, THE HAND TAKES IT ───────────────
            //
            // USER REPORT (2026-08-08): "Von dort möchte ich auch in der Lage sein die Karte wieder
            // in die Hand zu nehmen, aktuell geht sie mit trigger direkt zurück zum pile/Fächer."
            //
            // ROOT CAUSE of the "direkt zurück" half: a laser pluck is a press-and-HOLD hold
            // (ForceGrab(releaseOnTriggerUp: true) — the ability fans work the same way). Aimed at a
            // card lying in the recess, the beam click grabbed it, the grab cancelled the pending
            // decision, and the trigger-up of that same click released it IN MID-AIR, metres from
            // the slot — where the release routing can only glide it home to the fan. The card
            // therefore appeared to jump from the recess straight back to the pile without ever
            // being in the hand, and the player had no way to keep it.
            //
            // THE RULE (documented here because this is the fork):
            //   • THE HAND TAKES IT. Reaching to the recess and grabbing — either hand, trigger or
            //     grip, ProximityGrabber — takes the placed card INTO that hand, where it is an
            //     ordinary held item chip: readable, hand-swappable, and re-placeable. WHERE it is
            //     released decides what happens next, exactly as for the first placement: over the
            //     recess it clips back in (ItemsPile.OnChipReleased), anywhere else it glides home
            //     to the fan. A clipped chip is deliberately never sweep-suppressed and always
            //     AllowsHand, so that grab can never be refused.
            //   • THE FAR LASER PUTS IT BACK, in one deliberate gesture, and never takes it into
            //     the hand. The recess must stay clearable from reading distance without walking the
            //     hand down to the board, and a beam click cannot express a hold anyway (see the
            //     root cause above). So instead of the grab/mid-air-release round trip it now runs
            //     the return DIRECTLY — same cancel, same animated glide home, no phantom hop
            //     through the hand, and one honest log line.
            if (PendingUse && _owner != null)
            {
                _owner.ReturnPlacedChip(this, hand);
                return;
            }

            PluckIntoHand(hand);
        }

        /// <summary>
        /// TAKE THIS CHIP INTO <paramref name="hand"/> — the PHYSICAL-CONTACT half of the rule
        /// <see cref="OnPoke"/> documents ("the HAND takes it, the FAR LASER puts it back"), and the
        /// entry every hand-driven pull must use.
        ///
        /// <para>ROOT CAUSE this method exists (user report 2026-08-09: "Ich kann immer noch nicht
        /// eine Gegenstandskarten wieder direkt zurück in die Hand nehmen, das soll möglich sein
        /// (egal ob linke oder rechte Hand)" — ModBuild 92 claimed this and did not deliver it).
        /// ModBuild 92 wrote the two halves of the rule as ONE method: <see cref="OnPoke"/> checks
        /// <see cref="PendingUse"/> and, for a card lying in the recess, runs
        /// <see cref="ItemsPile.ReturnPlacedChip"/> — the deliberate one-gesture "put it back on the
        /// pile". But <see cref="OnPoke"/> is not the far laser: it is the shared
        /// <see cref="IPokeable"/> entry, and <c>CardsDriver.UpdateBoardFanHandTrigger</c> — the path
        /// that exists PRECISELY so a hand physically touching a chip owns the trigger instead of
        /// losing it to the board laser — delivered its pull through the very same call. So reaching
        /// down to the recess and pulling the trigger ran the FAR-LASER branch: the card was put back
        /// on the pile and could never be taken into the hand. The hardware log of 2026-08-09 shows
        /// the two lines back to back at 7668/7672 — "Item fan: HAND owns the trigger … so the pull
        /// takes it" immediately followed by "ITEM recess: laser click on the placed card … it glides
        /// back to the fan" — one gesture, two contradictory statements, and the second one won.</para>
        ///
        /// <para>Splitting the entries is the whole fix: the beam keeps <see cref="OnPoke"/> (with its
        /// recess branch), every physical hand path calls THIS, and for a chip standing in the arc the
        /// two are identical, so nothing about the ordinary pluck changes. Taking a CLIPPED chip needs
        /// no special case here either — <see cref="OnGrab"/> already unclips it, drops
        /// <see cref="PendingUse"/> and gives it its full grab box back, and the owner's per-tick
        /// service then backs the pending decision out through its own game seam.</para>
        /// </summary>
        internal void PluckIntoHand(VRHand hand)
        {
            if (hand == null || Holder != null || !CanGrab)
                return;
            _laserPopped = false;
            // An EXPLICIT pluck overrides the hand sweep's arbitration for this chip. Without this
            // the pull-jerk grace path could hand ForceGrab a chip the sweep had meanwhile
            // suppressed for that very hand (AllowsHand=false → ForceGrab refuses), turning a
            // promised pluck into a silent refusal. The sweep re-derives its set next tick anyway.
            ClearHandSuppressed();
            hand.Grabber.ForceGrab(this, releaseOnTriggerUp: true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // (No laser-target unregister: chips are picked geometrically by the driver's item-fan
            // laser path now, never through PlayTray.LaserTargets — see Create's laser note.)
            // Undo the emitter clamp BEFORE anything else touches the hosted card, so the widget the
            // pool gets back is byte-for-byte the one it handed out even if the recycle below fails.
            RestoreCardEffectSmoke();
            // Return the hosted ItemCardUI to the game's pool BEFORE this chip is destroyed (recycle
            // reparents it under the pool, so it survives the chip teardown and its art is unloaded).
            if (_cardGo != null && _cardUI != null)
            {
                try
                {
                    // ITEM #1 — leave the pooled widget CLEAN: restore the mip-swapped sprites before the
                    // game reuses this card elsewhere. (The usable highlight is a mod-owned frame canvas,
                    // destroyed with the chip below — it never touches the game card, nothing to reset.)
                    CardFaceMipBake.RestoreSprites(_cardUI);
                    ObjectPool.RecycleCard(_cardUI.CardID, ObjectPool.ECardType.Item, _cardGo);
                }
                catch (System.Exception e)
                {
                    VRLog.Warn("Cards", $"ITEM CARD recycle failed ({e.Message}).");
                }
            }
            _cardGo = null;
            _cardUI = null;
            _faceCanvas = null;
            _usableFrame = null; // child of the chip GameObject — destroyed with it
            _usabilityShown = -1;
        }

        // ---- state → look ----------------------------------------------------------

        private static Visual Classify(CItem item)
        {
            switch (item.SlotState)
            {
                case CItem.EItemSlotState.Consumed:
                    return Visual.Consumed;
                case CItem.EItemSlotState.Spent:
                    return Visual.Spent;
                default:
                    return Visual.Ready;
            }
        }

        private static Color FaceColor(Visual state) => state switch
        {
            Visual.Consumed => new Color(0.22f, 0.19f, 0.17f), // ashen (burnt)
            Visual.Spent => new Color(0.34f, 0.40f, 0.52f),    // cool "tapped/spent" blue-grey
            _ => new Color(0.80f, 0.72f, 0.52f),               // parchment (ready)
        };

        private static string Name(CItem item)
        {
            // item.YMLData.Name is a LOCALIZATION KEY — resolve it like the flat game
            // (ItemCardUI.CreateCard: LocalizationManager.GetTranslation) via the mod's Loc helper.
            string? key = item != null && item.YMLData != null ? item.YMLData.Name : null;
            if (string.IsNullOrEmpty(key))
                return "?";
            return Core.Loc.Game(key!, key!);
        }
    }
}
