using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHERE A FULLY-DETAILED ABILITY-CARD FACE COMES FROM, for a card that is NOT in the local hand.
///
/// THE CLAIM THIS CLASS RETIRES. <see cref="RemoteControlBoard"/> used to state that "the full
/// painted card art is only ever instantiated by the game as an <c>AbilityCardUI</c> widget for the
/// LOCAL player's own hand", and therefore drew a peer's played round cards as a mod-made panel
/// with the card NAME + INITIATIVE. That claim was wrong on BOTH counts, and the evidence is in the
/// game's own code:
///
///   1. THE WIDGETS EXIST FOR EVERY ACTOR, NOT JUST THE LOCAL ONE.
///      <c>Choreographer</c> calls <c>CardsHandManager.AddPlayer/AddPlayerCoroutine((CPlayerActor)
///      actor)</c> for EVERY player actor it spawns (Choreographer.cs:925/1112), and
///      <c>AddPlayer</c> instantiates a whole <c>CardsHandUI</c> per actor and appends it to
///      <c>cardHandsUI</c> (CardsHandManager.cs:481-505). Each of those hands spawns a real,
///      populated <c>AbilityCardUI</c> per deck card into its own <c>cardsUI</c> list
///      (CardsHandUI.SpawnCards / SpawnGivenCardUI, CardsHandUI.cs:1684). So a REMOTE actor's
///      round cards already have live, fully painted widgets on OUR client — they are merely
///      parked in a hidden 2D hand. <see cref="RemoteHandFan"/> has been relying on exactly this
///      for the ghost hand fan all along.
///
///   2. A WIDGET CAN ALSO BE MANUFACTURED FROM THE CARD MODEL ALONE.
///      <c>ObjectPool.SpawnCard(cardID, ECardType.Ability, parent, …)</c> auto-creates the pool on
///      a miss via <c>CreateAbilityCardInternal</c>, which looks the card up in
///      <c>CharacterClassManager.AllAbilityCards</c> and builds it from its YML
///      (ObjectPool.cs:378-384/415-438) — no hand, no actor, no ownership required. The flat game
///      does precisely this for cards nobody holds: <c>UILevelUpCardHolder.PlaceNewCard</c> spawns
///      by id, calls <c>Init(card, disableEventDetection: true)</c> and <c>Show()</c>
///      (UILevelUpCardHolder.cs:38-41), and <c>UICharacterCreatorCardsViewer</c> does
///      <c>Init(card)</c> + <c>ToggleFullCard(true)</c> (UICharacterCreatorCardsViewer.cs:51-53).
///      <c>AbilityCardUI.Init(CAbilityCard, bool)</c> takes the SKIN off the card model itself
///      (<c>abilityCard.ClassModel</c> / <c>ClassCharacterConfig</c>, AbilityCardUI.cs:550-559) and
///      puts the widget into <c>CardHandMode.Preview</c>, which is the mode whose only job is
///      "show this card, full size, don't interact".
///
/// So a peer's played card CAN be rendered at FULL, real detail on their remote control board, with
/// ZERO new wire traffic and ZERO card identity on the wire: the identity is already in the
/// host-replicated <c>CPlayerActor.CharacterClass</c> that the board reads today — this class only
/// changes how that same, already-available information is DRAWN.
///
/// ─── THE TWO PATHS, IN PREFERENCE ORDER ────────────────────────────────────────────────────────
///
/// A. LIVE (preferred) — clone the peer's OWN widget. Find the actor's <c>AbilityCardUI</c> for this
///    exact card in <c>CardsHandManager.GetHand(actor).cardsUI</c> and hand its
///    <c>fullAbilityCard</c> to <see cref="RemoteCardArt"/>, which <c>Instantiate</c>s a throwaway
///    copy onto a world-space canvas. Highest fidelity (it carries the ENHANCEMENT stickers that
///    player actually bought) and it mutates NOTHING: we never adopt or reparent the live widget,
///    we copy it.
///
/// B. POOLED BORROW (fallback) — manufacture a source. When the hand UI cannot be resolved (an
///    actor whose hand is mid-(re)build, a card that is not in <c>cardsUI</c>), spawn a widget from
///    the pool by card ID, clone its face, and hand the widget STRAIGHT BACK with
///    <c>ObjectPool.RecycleCard</c> in the same call — see <see cref="TryPooledClone"/> for why
///    borrow-and-return is the whole point.
///
/// Both paths end in the same place: a mod-owned CLONE that we destroy ourselves. Nothing
/// game-owned is left mutated, nothing game-owned is re-layered, no game data is written.
///
/// ANTI-CHEAT: this class holds NO gate of its own. It renders a face only when a caller asks it
/// to, and every caller asks only under <see cref="RevealGate.ShowRoundCardFronts"/>. Every game
/// deref is null-guarded and every failure returns <see cref="FacePath.None"/> so the caller keeps
/// showing a card BACK — fail-safe is "no face", never "a face we could not verify".
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. It resolves a peer's round-card face from
/// the host-replicated model (their own live <c>AbilityCardUI</c>, or a widget borrowed from the
/// game's pool by card id), never from a packet. This class is the reason card IDENTITY can stay
/// DELIBERATELY-NOT on the wire while a peer's card is still fully readable. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteAbilityCardSource
{
    /// <summary>Which mechanism produced the face a caller is currently showing — reported in the
    /// diagnostics so a hardware log states the fidelity path, not just "a card is drawn".</summary>
    internal enum FacePath
    {
        /// <summary>No real face — the caller falls back to the mod-drawn name+initiative panel.</summary>
        None,

        /// <summary>Path A: cloned from the peer's own live <c>AbilityCardUI.fullAbilityCard</c>.</summary>
        LiveWidget,

        /// <summary>Path B: cloned from a widget borrowed from (and returned to) the game's pool.</summary>
        PooledBorrow,
    }

    /// <summary>Per-PATH one-shot latch so the "which mechanism works on this build" line lands in the
    /// log at most once per path per session — never once per card, and never flip-flopping when the
    /// two paths alternate across cards.</summary>
    private static readonly bool[] s_loggedPath = new bool[3];

    /// <summary>
    /// Show <paramref name="card"/>'s REAL, fully detailed face on <paramref name="art"/>, trying the
    /// live-widget path first and the pooled borrow second. Returns which path succeeded (or
    /// <see cref="FacePath.None"/>, in which case <paramref name="art"/> is left showing nothing and
    /// the caller must draw its own fallback).
    ///
    /// CALLER CONTRACT (anti-cheat): only ever call this when the reveal gate for
    /// <paramref name="actor"/> is OPEN. Nothing here re-checks it, deliberately — one gate, in one
    /// place, evaluated by the caller BEFORE any face object is created, is easier to audit than a
    /// gate re-derived in three files.
    /// </summary>
    internal static FacePath ShowFullFace(RemoteCardArt art, CPlayerActor? actor, CAbilityCard card)
    {
        if (art == null || card == null)
            return FacePath.None;

        try
        {
            FullAbilityCard? live = TryLiveWidget(actor, card);
            if (live != null && art.ShowFront(live))
                return Report(FacePath.LiveWidget, card);
        }
        catch (System.Exception e)
        {
            VRLog.Debug("Net", $"Remote card face: live-widget path unavailable ({e.Message}) — trying the pool.");
        }

        try
        {
            if (TryPooledClone(art, card))
                return Report(FacePath.PooledBorrow, card);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote card face: pooled borrow failed ({e.Message}) — the slot keeps " +
                              "the mod-drawn name+initiative panel.");
        }

        art.HideFront();
        return FacePath.None;
    }

    /// <summary>One Info line the first time each path is exercised in a session — enough for a
    /// hardware log to state WHICH mechanism is carrying the fidelity, without a line per card.</summary>
    private static FacePath Report(FacePath path, CAbilityCard card)
    {
        int i = (int)path;
        if (i < 0 || i >= s_loggedPath.Length || s_loggedPath[i])
            return path;
        s_loggedPath[i] = true;
        VRLog.Info("Net", $"Remote card FACE path = {path} (first use; e.g. card id {card.ID}) — " +
                          "a peer's played card is now the REAL game card widget (full art, actions, " +
                          "icons, initiative), cloned locally from the host-replicated model. " +
                          "Zero wire traffic, zero card identity on the wire.");
        return path;
    }

    // ---------------------------------------------------------------------- path A: live widget --

    /// <summary>
    /// Path A — the peer's OWN live widget for exactly this card.
    ///
    /// <c>CardsHandManager.GetHand(actor)</c> resolves the per-actor <c>CardsHandUI</c> that
    /// <c>AddPlayer</c> created for EVERY player actor (see the class note), and <c>cardsUI</c> is
    /// that hand's full list of spawned <c>AbilityCardUI</c>s across every pile. We match on the
    /// model reference first (<c>AbilityCardUI.AbilityCard</c> is the very <c>CAbilityCard</c> the
    /// widget was <c>Init</c>ed with) and fall back to the id pair the game itself matches on
    /// (<c>CardID</c> + <c>CardInstanceID</c>, cf. <c>AbilityCardUI.IsMatchingCard</c>).
    ///
    /// Read-only: we return the widget's <c>fullAbilityCard</c> for CLONING only. Nothing is
    /// reparented, no flag is flipped, the peer's hidden 2D hand is left exactly as it was.
    /// </summary>
    private static FullAbilityCard? TryLiveWidget(CPlayerActor? actor, CAbilityCard card)
    {
        if (actor == null)
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;
        CardsHandUI hand = manager.GetHand(actor);
        if (hand == null)
            return null;
        List<AbilityCardUI> cards = hand.cardsUI; // publicized private field (as RemoteHandFan uses)
        if (cards == null)
            return null;

        FullAbilityCard? byIds = null;
        for (int i = 0; i < cards.Count; i++)
        {
            AbilityCardUI c = cards[i];
            if (c == null || c.fullAbilityCard == null)
                continue;
            if (ReferenceEquals(c.AbilityCard, card))
                return c.fullAbilityCard;            // exact model match — always preferred
            if (byIds == null && c.CardID == card.ID && c.CardInstanceID == card.CardInstanceID)
                byIds = c.fullAbilityCard;           // id match — kept as the runner-up
        }
        return byIds;
    }

    // -------------------------------------------------------------------- path B: pooled borrow --

    /// <summary>
    /// Path B — BORROW a widget from the game's own object pool, clone its face, and give the widget
    /// straight back, all inside this method.
    ///
    /// WHY BORROW-AND-RETURN RATHER THAN HOST THE POOLED WIDGET (which is what
    /// <see cref="Cards.ItemsPile"/> does for item cards). ItemsPile keeps its <c>ItemCardUI</c>
    /// alive because it must stay LIVE: the item's state changes under it (spent / consumed) and the
    /// game's own <c>ItemCardEffects</c> has to play ON that widget. A peer's played round card
    /// needs none of that — it is a static picture of a card. Holding a game-owned pooled widget for
    /// the whole life of a remote board would buy nothing and cost the entire ItemsPile hazard list:
    /// a mutated widget that must be restored before recycle, a recycle that must survive every
    /// teardown path (phase change, board destroy, peer leave, scene unload), and a leak that
    /// corrupts the flat UI if any of those paths is missed. Borrowing for the duration of ONE method
    /// call removes that class of bug entirely: the widget is spawned INACTIVE
    /// (<c>activate: false</c>, so it can never render for even a frame), configured, cloned, and
    /// recycled in a <c>finally</c> — there is no window in which anything can go wrong.
    ///
    /// The recipe itself is the flat game's own, verbatim:
    ///   <c>SpawnCard(card.ID, ECardType.Ability, holder)</c> → <c>Init(card, disableEventDetection:
    ///   true)</c> → <c>Show()</c>, exactly as <c>UILevelUpCardHolder.PlaceNewCard</c> does
    ///   (UILevelUpCardHolder.cs:38-41). <c>Init</c> puts the widget in <c>CardHandMode.Preview</c>,
    ///   which activates <c>fullAbilityCard</c> and sets the class SKIN from the card model;
    ///   <c>Show()</c> kicks the skin's async header-art load so the CLONE (which
    ///   <see cref="RemoteCardArt"/> re-hands the same skin to) can reload it for itself.
    ///
    /// Everything the borrow touches is undone by the game's own <c>OnReturnedToPool</c> contract,
    /// which <c>RecycleCard</c> invokes: it runs <c>DeInit</c>, drops the interactability controls
    /// <c>Init</c> added, clears the mini card, resets validity/highlight/consumes and re-enables
    /// event detection (AbilityCardUI.cs:1312-1335). We add no mutation of our own beyond that call
    /// pair, so the widget the pool gets back is the widget the pool handed out.
    /// </summary>
    private static bool TryPooledClone(RemoteCardArt art, CAbilityCard card)
    {
        if (ObjectPool.instance == null || card.ID == 0)
            return false;

        // Reset the art's source dedup first: the borrowed widget is transient, so its instance id
        // carries no meaning across calls and must never be allowed to dedup a genuinely new card.
        art.HideFront();

        GameObject? holder = null;
        GameObject? cardGo = null;
        AbilityCardUI? ui = null;
        try
        {
            // An INACTIVE, mod-owned holder parked under the pool root. The widget is spawned with
            // activate:false and lives under a deactivated parent, so it is unrenderable from the
            // instant it exists — there is no frame in which a face could leak ahead of the gate.
            holder = new GameObject("GloomhavenVR.AbilityCardBorrow") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(ObjectPool.instance.transform, worldPositionStays: false);
            holder.SetActive(false);

            cardGo = ObjectPool.SpawnCard(card.ID, ObjectPool.ECardType.Ability, holder.transform,
                resetLocalScale: true, resetToMiddle: true, resetLocalRotation: false, activate: false);
            if (cardGo == null)
                return false;

            ui = cardGo.GetComponent<AbilityCardUI>();
            if (ui == null || ui.fullAbilityCard == null)
                return false;

            // Preview mode: activates fullAbilityCard, takes the class SKIN off the card model.
            // Everything the CLONE needs to draw is already baked on the widget by the pool's own
            // MakeFullCard (name, initiative, level, both action layouts — FullAbilityCard.cs:456+);
            // Init adds the skin, which RemoteCardArt re-hands to the clone.
            ui.Init(card, disableEventDetection: true);

            // BEST-EFFORT ONLY. Show() → FullAbilityCard.ShowCard() kicks the skin's async header-art
            // load through an ImageAddressableLoader keyed on the widget — but our borrowed widget is
            // parented under an INACTIVE holder, so an async loader that wants to run a coroutine on
            // it can legitimately refuse. That costs us nothing: the clone carries the same _skin and
            // runs its OWN OnEnable → ShowCard once it activates (FullAbilityCard.cs:430), which is
            // where the header art actually arrives. So a failure here must never sink the path.
            try { ui.Show(); }
            catch (System.Exception e)
            {
                VRLog.Debug("Net", $"Borrowed ability card Show() skipped ({e.Message}) — the clone " +
                                   "reloads its own header art on activation.");
            }

            return art.ShowFront(ui.fullAbilityCard);
        }
        finally
        {
            // Hand the widget back NO MATTER WHAT — including when ShowFront threw. The clone (if one
            // was made) is a separate object parented under the caller's own host, so recycling the
            // source here cannot disturb it.
            if (cardGo != null && ui != null)
            {
                try { ObjectPool.RecycleCard(ui.CardID, ObjectPool.ECardType.Ability, cardGo); }
                catch (System.Exception e) { VRLog.Warn("Net", $"Ability-card borrow recycle failed: {e.Message}"); }
            }
            else if (cardGo != null)
            {
                Object.Destroy(cardGo); // spawned but unusable — never leave it parented under our holder
            }
            if (holder != null)
                Object.Destroy(holder);
        }
    }
}
