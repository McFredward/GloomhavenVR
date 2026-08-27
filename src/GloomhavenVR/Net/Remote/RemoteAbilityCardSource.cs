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

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  THE SKIN FIXUP — the one thing Object.Instantiate cannot carry onto a cloned card face
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // USER REPORT (MP hardware round, ModBuild 137, verbatim): "Die Vorderseite der Handkarten von
    // den Mitspielern sehen kaput aus. Falsche Farbe und weiße vierecke mit manchen symbolen drin -
    // das symbol hatte ich schon einmal gemeldet." (screenshot remote_handkarten.jpg: the peer's
    // three hand cards show a correct name banner, initiative band and level number inside a
    // magenta/gold frame, while BOTH action halves are blank WHITE boxes carrying only a couple of
    // stray icons.)
    //
    // "das symbol hatte ich schon einmal gemeldet" is exact: this is pixel-for-pixel the LOCAL
    // decision-phase failure of 2026-08-07 that <see cref="Cards.CardArtGuard"/> was written for —
    // white action halves, missing action content, everything OUTSIDE the halves fine. Same pixels,
    // DIFFERENT cause, which is why the guard never healed it: the guard only knows faces the mod
    // ADOPTED (CardFace.Adopt → CardArtGuard.NoteAdopted), and a peer's face is never adopted, it is
    // a throwaway CLONE.
    //
    // ─── ROOT CAUSE, read from the game's own decompiled source ───────────────────────────────────
    //
    //  1. The action half's background is `FullAbilityCardAction.actionButton.image`. Its sprite is
    //     NOT authored on the prefab — it is streamed by
    //     `FullAbilityCardAction.Show() → ApplyImage() → ButtonSpritesAddressableLoader
    //     .AddReferenceToSprites(actionButton, _referenceForImageActionButton, …)`
    //     (FullAbilityCardAction.cs:529-539). `ApplyImage` returns immediately when
    //     `_referenceForImageActionButton` is null.
    //
    //  2. That field — and its two siblings `_stateReferencesForActionButton` and `skin` — are
    //     PLAIN PRIVATE RUNTIME FIELDS (FullAbilityCardAction.cs:69-73: no [SerializeField], unlike
    //     every field above them in that same class). They are populated exactly once, by
    //     `FullAbilityCardAction.SetSkin(skin, topAction, longRest)` (FullAbilityCardAction.cs:511),
    //     whose only caller is `FullAbilityCard.SetSkin(skin)` (FullAbilityCard.cs:239-251), whose
    //     only caller is `AbilityCardUI.Init` → `SetSkin(ClassModel, ClassCharacterConfig)`
    //     (AbilityCardUI.cs:549-559).
    //
    //  3. `Object.Instantiate` copies SERIALIZED state only. So a clone of a `FullAbilityCard` is
    //     born with `skin`/`_referenceForImageActionButton`/`_stateReferencesForActionButton` = null
    //     on BOTH halves, and its own `OnEnable → ShowCard() → topActionButton.Show()` therefore
    //     loads nothing at all. `RemoteCardArt.TryReapplySkin` re-hands ONLY the ROOT's private
    //     `FullAbilityCard._skin` — which is why the header/title art (the one thing `ShowCard`
    //     loads off `_skin` directly, FullAbilityCard.cs:442) comes out RIGHT while the halves stay
    //     empty. Half the fix has been in place since the class was written; this is the other half.
    //
    //  4. …and the sprite the clone was copied WITH is null: a peer's hand widget lives in a hidden
    //     2D `CardsHandUI` whose `fullAbilityCard` GameObject the game keeps DEACTIVATED
    //     (`AbilityCardUI.ToggleFullCard(false)`), so its own `OnEnable/ShowCard` never ran and its
    //     action backgrounds were never streamed either. A uGUI `Image` with a null sprite draws
    //     Unity's built-in WHITE texture — the reported white boxes. The action CONTENT vanishes
    //     with it because `ImageAddressableLoader` alpha-0s its `_objectsToHideWhileLoad` groups
    //     around a load and only restores them when the load lands (ImageAddressableLoader.cs:60-71)
    //     — the identical two-symptom signature CardArtGuard documents.
    //
    //  5. "Falsche Farbe": `FullAbilityCard.SetSkin` is ALSO the only writer of
    //     `buttonsHolderImage.sprite = skin.buttonsHolderSprite` (the card FRAME) and of
    //     `initiativeText.color = skin.initiativeColor`. Never called on a clone ⇒ the peer's card
    //     wears whatever the PREFAB shipped instead of that character class's frame and initiative
    //     colour. Nothing here is Unity's missing-shader magenta: every material on the clone is the
    //     game's own, and no shader is ever looked up on this path.
    //
    // ─── THE FIX ──────────────────────────────────────────────────────────────────────────────────
    // Replay `FullAbilityCard.SetSkin` on the CLONE while it is still INACTIVE, from
    // <see cref="RemoteCardArt"/>'s `beforeActivate` seam — the same seam an ITEM card already uses
    // to re-plant its model reference. Because it runs before activation, the clone's own
    // `OnEnable → ShowCard → Show → ApplyImage` then streams the halves exactly as the owner's card
    // does, and the frame/initiative colour are correct on the FIRST drawn frame: there is no window
    // in which a peer's card is white, and none in which it wears the wrong frame.
    //
    // WHY THIS IS NOT A CALL TO `FullAbilityCard.SetSkin(skin)` ITSELF. That method opens with
    // `cardEffects.HasEffect(FXTask.BurnCard)` (FullAbilityCard.cs:242) and
    // <see cref="RemoteCardArt"/> has already `DestroyImmediate`d the clone's `CardEffects` by the
    // time `beforeActivate` runs (it must — the screen-space `_PosAndBounds` material is the known
    // "card renders DEEP BLACK" hazard on a detached world-space clone). Calling it would NRE on
    // every single face. The body below is that method verbatim MINUS that one deref, whose only
    // purpose is "do not repaint the initiative of a card that is mid-burn" — a state a stripped,
    // effect-less clone can never be in.
    //
    // REJECTED — "load the sprites on the SOURCE widget instead, so Instantiate copies them": it
    // mutates a game-owned widget in a peer's hidden hand, and the load is ASYNC, so the clone (made
    // in the same call, and dedup-latched by instance id afterwards) would copy the still-null
    // sprite and stay white for the card's whole life. REJECTED — "heal it afterwards from the
    // maintenance cadence, like CardArtGuard": that is a repair after the player has already seen
    // the white frame, and the standing rule is that a peer's card must never look worse than the
    // owner's for even one frame.
    //
    // ZERO WIRE, unchanged: the skin comes off the SOURCE widget's own runtime `_skin`, which the
    // game set from the host-replicated `CAbilityCard.ClassModel`. No identity, no art reference and
    // no packet is involved — see the CLASSIFICATION remark on this class.

    /// <summary>One-shot latch for the <c>REMOTE FRONT</c> diagnostic (per session, not per card).</summary>
    private static bool s_loggedSkinFixup;

    /// <summary>
    /// One cached hook per SKIN. <see cref="RemoteCardArt.ShowFront(FullAbilityCard)"/> is called
    /// EVERY FRAME by <c>RemoteHandFan</c> while a front is up (its dedup lives one call deeper), so
    /// building the closure on each call would be a steady-state allocation per card per peer per
    /// frame — precisely the cost that file is written to avoid. Bounded by construction: one entry
    /// per character-class skin (~a dozen), and the skins are long-lived game data.
    /// </summary>
    private static readonly Dictionary<AbilityCardUISkin, System.Action<GameObject>> s_skinHooks = new(8);

    /// <summary>
    /// The <c>beforeActivate</c> hook that gives a cloned ability-card face the class SKIN its two
    /// action halves need — see the block comment above for the whole derivation. Returns null when
    /// there is no skin to hand over (then the clone behaves exactly as it did before this existed:
    /// copied visuals, no streamed halves — degrade to today, never to something worse).
    /// </summary>
    internal static System.Action<GameObject>? SkinFixup(FullAbilityCard? source)
    {
        AbilityCardUISkin? skin;
        try
        {
            skin = source != null ? source._skin : null;   // publicized runtime field
        }
        catch (System.Exception)
        {
            return null;
        }
        if (skin == null)
            return null;
        if (s_skinHooks.TryGetValue(skin, out System.Action<GameObject> hook))
            return hook;
        hook = clone => ApplySkin(clone, skin);
        s_skinHooks[skin] = hook;
        return hook;
    }

    /// <summary>
    /// <c>FullAbilityCard.SetSkin(skin)</c> replayed on a mod-owned clone (see the block comment):
    /// the frame sprite, the initiative colour and — the part that fixes the white halves — the two
    /// <c>FullAbilityCardAction.SetSkin</c> calls that repopulate the non-serialized sprite
    /// references `ApplyImage()` needs. Every deref is guarded and any surprise leaves the clone
    /// exactly as it would have been without this method.
    /// </summary>
    private static void ApplySkin(GameObject clone, AbilityCardUISkin skin)
    {
        if (clone == null)
            return;
        try
        {
            var full = clone.GetComponent<FullAbilityCard>();
            if (full == null)
                return;

            // Provenance for the diagnostic: was this really the null-reference state the report
            // describes? Captured BEFORE we write, so the log states the defect, not our fix.
            bool topWasBlind = full.topActionButton == null
                               || full.topActionButton._referenceForImageActionButton == null;
            bool bottomWasBlind = full.bottomActionButton == null
                                  || full.bottomActionButton._referenceForImageActionButton == null;

            full._skin = skin;
            bool longRest = full.isLongRestCard;   // [SerializeField] ⇒ carried by Instantiate
            if (!longRest)
            {
                if (full.buttonsHolderImage != null)
                    full.buttonsHolderImage.sprite = skin.buttonsHolderSprite;
                if (full.initiativeText != null)
                    full.initiativeText.color = skin.initiativeColor;
            }
            ApplyHalfSkin(full.topActionButton, skin, topAction: true, longRest);
            ApplyHalfSkin(full.bottomActionButton, skin, topAction: false, longRest);

            if (s_loggedSkinFixup)
                return;
            s_loggedSkinFixup = true;
            VRLog.Info("Net", "REMOTE FRONT: applied the class skin " +
                              $"'{(string.IsNullOrEmpty(skin.ID) ? "(unnamed)" : skin.ID)}' to a peer's " +
                              "CLONED ability-card face, resolved from the SOURCE widget's own runtime " +
                              "FullAbilityCard._skin (host-replicated CAbilityCard.ClassModel — zero wire, " +
                              "zero card identity). On arrival the clone's halves were " +
                              $"{(topWasBlind ? "BLIND" : "already set")}/" +
                              $"{(bottomWasBlind ? "BLIND" : "already set")} (top/bottom): " +
                              "FullAbilityCardAction.skin/_referenceForImageActionButton/" +
                              "_stateReferencesForActionButton carry NO [SerializeField], so " +
                              "Object.Instantiate cannot copy them and ApplyImage() streamed nothing — " +
                              "the peer's action halves stayed on a NULL sprite, which uGUI draws as its " +
                              "built-in WHITE texture (report 2026-08-13 'weiße vierecke', the same pixels " +
                              "as the local decision-phase bug CardArtGuard fixed). The frame sprite and " +
                              "the initiative colour come from the same SetSkin call, which is the " +
                              "'falsche Farbe' half of the report.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"REMOTE FRONT: skin fixup skipped ({ex.GetType().Name}: {ex.Message}) — " +
                              "the peer's card keeps the clone's copied visuals (the pre-2026-08-13 look).");
        }
    }

    /// <summary>
    /// One action half. <c>SetSkin</c> is the game's own; the extra step after it covers the LONG
    /// REST card, whose reference is built with <c>new ReferenceToSprite(sprite)</c> — an
    /// "InitializedWithSpecialSprite" reference that <c>ButtonLoadingContext</c> deliberately does
    /// NOT stream (ButtonLoadingContext.cs:52), so on that one card the background would stay null
    /// (i.e. white) even with the references restored. Assigning the sprite it already holds costs
    /// nothing on the normal path (there `GetSprite()` is null) and closes that case.
    /// </summary>
    private static void ApplyHalfSkin(FullAbilityCardAction? half, AbilityCardUISkin skin,
                                      bool topAction, bool longRest)
    {
        if (half == null)
            return;
        half.SetSkin(skin, topAction, longRest);
        Sprite? ready = half._referenceForImageActionButton != null
            ? half._referenceForImageActionButton.GetSprite()
            : null;
        UnityEngine.UI.Button? button = half.actionButton;
        UnityEngine.UI.Image? image = button != null ? button.image : null;
        if (ready != null && image != null && image.sprite == null)
            image.sprite = ready;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  THE PLATE-STATE FIXUP — the ACTUAL cause of "the selectable areas are greyer"
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // USER REPORT, three hardware rounds running (2026-08-22, ModBuild 197, verbatim): "Das
    // Farbproblem ist unverändert wie im alten Screenshot." Photograph Farbunterschied.jpg, both
    // cards at the same on-screen size (~320 px), sampled over 20 000+ px boxes:
    //
    //     feature                overlay (uGUI)     mod-built fan card
    //     title bar              (167.2,40.1,37.1)  (165.5,54.3,50.6)   the same
    //     card frame rail        ( 55,  6,  2)      ( 50,  3,  0)       the same
    //     TOP action plate fill  (132.0,48.8,56.1)  ( 74.9,61.3,63.2)   COLLAPSED
    //     BOTTOM action plate    (128.5,47.8,55.7)  ( 92.8,79.8,81.3)   COLLAPSED
    //
    // The plate fill fits lerp(overlay, Rec.709 luminance, ≈0.85) to within two counts per channel,
    // and it is confined to the two action plates. THREE ROUNDS OF MEASUREMENT ELIMINATED EVERY
    // MULTIPLICATIVE EXPLANATION, and correctly: a multiply cannot raise a green channel from 48.8 to
    // 61.3. The ModBuild-196 round proved the halves' CanvasGroup alpha is 1.00 on both surfaces; the
    // ModBuild-197 round proved the plates' material `_GreyOut` (the game's spent-card wash) reads
    // 0.00 on BOTH the CLONE and the GAME-POOL population it is copied from, over 20 and 286 plate
    // images — its census line and the "worst term 1.000 before the write" it printed came from a
    // DIFFERENT image on the face, never from a plate. Neither round moved a pixel, because both were
    // measuring numbers that were already correct.
    //
    // ─── IT IS NOT A COLOUR AT ALL. IT IS A DIFFERENT SPRITE. ─────────────────────────────────────
    //
    // An action plate's background is a `Selectable` target graphic, and the game skins it through
    // Unity's SPRITE SWAP transition, not through a tint:
    //
    //   * `AbilityCardUISkin` ships FOUR plate sprites per half — `TopActionRegularSprite`,
    //     `…HighlightSprite`, `…SelectedSprite` and `TopActionDisabledSprite`
    //     (AbilityCardUISkin.cs:16-30). `GetActionSpriteState` packs the last three into a
    //     `ReferenceToSpriteState` (AbilityCardUISkin.cs:105-113) …
    //   * … which `FullAbilityCardAction.ApplyImage()` hands to
    //     `ButtonSpritesAddressableLoader.AddReferenceToSprites`, whose load completion assigns
    //     `button.spriteState = …` (ButtonLoadingContext.cs:88). `Selectable.spriteState`'s setter
    //     calls `OnSetProperty()` → `DoStateTransition(currentSelectionState, false)`, which for
    //     `Transition.SpriteSwap` runs `DoSpriteSwap(…)` → `image.overrideSprite = <state sprite>`.
    //   * `currentSelectionState` returns `SelectionState.Disabled` whenever `IsInteractable()` is
    //     false, and `Selectable.IsInteractable()` is `m_GroupsAllowInteraction && m_Interactable` —
    //     a CONJUNCTION, of which every census so far had only ever read the second term.
    //
    // ─── AND THE MOD ITSELF FALSIFIES THE FIRST TERM ──────────────────────────────────────────────
    //
    // `RemoteCardArt.Neutralize` writes `CanvasGroup.interactable = false` on the clone root, to make
    // a peer's card impossible to poke. That single line makes `ParentGroupAllowsInteraction()` false
    // for EVERY Selectable under the face, so the plates' sprite swap resolves to the skin's DISABLED
    // plate — an artwork the artist authored as a ≈0.85-desaturated version of the regular plate.
    // That is the photograph, exactly: the plates carry different PIXELS, while the CanvasGroup alpha
    // stays 1.00, the CanvasRenderer tint stays white (a sprite swap never touches it — which is
    // precisely why the census's `renderedTint 1.00..1.00` was true AND useless), `_GreyOut` stays 0
    // and `button.interactable` stays true. Every number three rounds measured is correct; none of
    // them is the one that decides which sprite is drawn.
    //
    // IT ALSO PREDICTS THE POPULATION BOUNDARY, which is what makes it more than a plausible story.
    // The defect must appear on every surface that MANUFACTURES a front (map-room hand, peers' hands,
    // every pooled-borrow clone) because `Neutralize` runs on all of them, and must be ABSENT from
    // the adopted scenario fan, whose face is the game's own live widget re-hosted by
    // `Cards.CardFace.Adopt` — a path that adds a CanvasGroup for FADE only (`VRCard`:1611-1613) and
    // never writes `interactable`. That is the split the user reports ("generell, nicht nur die
    // Weltkarte") and the split the 2026-08-15 scenario photograph measured.
    //
    // ─── THE FIX: REMOVE THE TRANSITION, NOT THE SAFETY ───────────────────────────────────────────
    //
    // The `CanvasGroup.interactable = false` STAYS: it is anti-cheat, and conceding it to win a
    // colour would be trading a guarantee for a look. What goes instead is the state machine that
    // translates it into pixels — `Selectable.transition = Transition.None` on the four plate buttons
    // of a mod-built face, which is the GAME'S OWN call for exactly this situation
    // (`FullAbilityCardAction.DisableHoverHighlight(true)`, FullAbilityCardAction.cs:589-592, what
    // the game runs when a card must look like a card without behaving like one). With the transition
    // gone, `DoStateTransition` is inert for every future state change too — so the async plate load
    // that lands seconds later cannot re-grey the face, and there is no write war to lose. The
    // already-set `overrideSprite` is cleared in the same pass so the regular plate is what draws.
    //
    // WHY THIS IS NOT "SET IT INTERACTABLE INSTEAD". Making `IsInteractable()` true would mean either
    // conceding the CanvasGroup (anti-cheat) or re-writing `m_GroupsAllowInteraction`, which Unity
    // recomputes on every `OnCanvasGroupChanged` — a value owned by the engine, i.e. the shape of
    // write war this project has already paid for. Removing the transition asks the engine for
    // nothing and is written once per clone.
    //
    // ZERO WIRE, unchanged: nothing here reads or sends card identity; it is four property writes on
    // an object the mod built for itself.

    /// <summary>One-shot latch for the <c>REMOTE PLATE</c> diagnostic (per session, not per card).</summary>
    private static bool s_loggedPlateFixup;

    /// <summary>
    /// Take a mod-built ability face out of Unity's DISABLED sprite-swap state — see the block
    /// comment above for the whole derivation. Runs on every clone, unconditionally and before the
    /// clone is ever activated: it is NOT gated behind any measurement of its own, so it cannot
    /// repeat ModBuild 196's failure of only correcting the faces its own defect counter had found.
    /// A clone with no <c>FullAbilityCard</c> (an item card) is left alone.
    /// </summary>
    internal static void NeutralizePlateLook(GameObject clone)
    {
        if (clone == null)
            return;
        try
        {
            var full = clone.GetComponent<FullAbilityCard>();
            if (full == null)
                return;

            // READ IT BEFORE WRITING IT: once the four writes land, every later reading reports the
            // CORRECTED state, so the numbers that PROVE the defect only exist in this instant.
            var before = new System.Text.StringBuilder(160);
            NeutralizeHalfPlates(full.topActionButton, "top", before);
            NeutralizeHalfPlates(full.bottomActionButton, "bottom", before);

            if (s_loggedPlateFixup || before.Length == 0)
                return;
            s_loggedPlateFixup = true;
            VRLog.Info("Net", "REMOTE PLATE: a mod-built ability face arrived in Unity's DISABLED " +
                              "sprite-swap state — MEASURED BEFORE THE WRITE: " + before +
                              ". Selectable.IsInteractable() is m_GroupsAllowInteraction && " +
                              "m_Interactable, and RemoteCardArt.Neutralize writes " +
                              "CanvasGroup.interactable = false on the clone root (anti-cheat, and it " +
                              "STAYS), which falsifies the FIRST term while every census so far read " +
                              "only the second. A false IsInteractable() makes currentSelectionState " +
                              "Disabled, and the action plate is a SPRITE-SWAP Selectable, so " +
                              "DoSpriteSwap writes image.overrideSprite = " +
                              "AbilityCardUISkin.Top/BottomActionDisabledSprite — the artist's " +
                              "≈0.85-desaturated plate. THAT is the photographed grey, not _GreyOut " +
                              "(0.00 on clone AND pool), not the CanvasGroup alpha (1.00 on both) and " +
                              "not a tint (a sprite swap never touches the CanvasRenderer colour, " +
                              "which is why 'renderedTint 1.00' was true and useless). Corrected by " +
                              "the game's own DisableHoverHighlight route: transition = None on the " +
                              "four plate buttons + overrideSprite cleared, so the regular plate draws " +
                              "and no later state change — including the async plate load — can swap " +
                              "it back. Read CARD HALF TONE CENSUS for the drawn SPRITE NAMES per " +
                              "population: CLONE and GAME-LIVE printing the same plate sprite name is " +
                              "what proves this took effect.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"REMOTE PLATE: plate-state fixup skipped ({ex.GetType().Name}: " +
                              $"{ex.Message}) — the face keeps the disabled plate sprite.");
        }
    }

    /// <summary>Both plate buttons of one half. <c>actionButton</c> is the big action plate the report
    /// is about; <c>defaultActionButton</c> is the default-attack/move plate that sits in the same
    /// rect and carries its own disabled sprite (<c>AbilityCardUISkin.defaultTopActionDisabledSprite</c>),
    /// so leaving it out would fix one plate and keep the other.</summary>
    private static void NeutralizeHalfPlates(FullAbilityCardAction? half, string which,
                                             System.Text.StringBuilder before)
    {
        if (half == null)
            return;
        NeutralizePlateButton(half.actionButton, which + " action", before);
        NeutralizePlateButton(half.defaultActionButton, which + " default", before);
    }

    private static void NeutralizePlateButton(UnityEngine.UI.Selectable? plate, string what,
                                              System.Text.StringBuilder before)
    {
        if (plate == null)
            return;

        UnityEngine.UI.Selectable.Transition transition = plate.transition;
        UnityEngine.UI.Graphic? target = plate.targetGraphic;
        var image = target as UnityEngine.UI.Image;
        // `Image.overrideSprite`'s GETTER returns activeSprite — the override when one is set, the
        // plain sprite otherwise — so it IS the drawn sprite, and "a swap is live" is the REFERENCE
        // inequality against `.sprite`, never a null test. (Reading it as a null test is how a swap
        // hides from a probe.)
        Sprite? drawn = image != null ? image.overrideSprite : null;
        Sprite? plain = image != null ? image.sprite : null;
        bool swapped = image != null && !ReferenceEquals(drawn, plain);
        // THE DEFECT SIGNATURE IS "A SWAP TRANSITION IS ARMED WHILE A GROUP ABOVE IT IS NOT
        // INTERACTABLE", not "a disabled sprite is already showing". This method runs on a clone that
        // is still INACTIVE, so `IsInteractable()` here still reports the field default true — Unity
        // has not delivered OnCanvasGroupChanged yet, and the swap it will then perform lands after
        // activation and again when the async plate load assigns `button.spriteState`. Reading the
        // ARMED condition rather than its consequence is the only way this line can state the defect
        // at the one moment the correction is applied. (The consequence is measured on the live face
        // instead — Cards.CardHalfTone's census prints the DRAWN plate sprite name per population.)
        // includeInactive: the clone is still INACTIVE here, and the group this is about was added to
        // its root three lines ago — the default overload would return null on every single face.
        CanvasGroup? gate = plate.GetComponentInParent<CanvasGroup>(includeInactive: true);
        bool armed = transition != UnityEngine.UI.Selectable.Transition.None
                     && (swapped || (gate != null && !gate.interactable));
        if (armed)
        {
            if (before.Length > 0)
                before.Append("; ");
            before.Append(what).Append(": transition=").Append(transition)
                  .Append(", interactable flag=").Append(plate.interactable)
                  .Append(", nearest CanvasGroup.interactable=")
                  .Append(gate != null ? gate.interactable.ToString() : "(none)")
                  .Append(", spriteState.disabledSprite='")
                  .Append(plate.spriteState.disabledSprite != null
                              ? plate.spriteState.disabledSprite.name : "(none)")
                  .Append("', plate sprite now '")
                  .Append(drawn != null ? drawn.name : "(none)")
                  .Append(swapped ? "' via overrideSprite (a SWAP is ALREADY live)" : "'");
        }

        plate.transition = UnityEngine.UI.Selectable.Transition.None;
        if (image != null)
            image.overrideSprite = null;
        // A swap never wrote this, but a ColorTint plate on some future skin would have; white = no
        // tint, i.e. the state Selectable.InstantClearState leaves behind.
        if (target != null && target.canvasRenderer != null)
            target.canvasRenderer.SetColor(Color.white);
    }

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
    // INTERNAL SINCE ModBuild 310, and the reason is worth a line: RemoteBoardCard needs the same
    // widget this resolves — the live one behind a seated round card — to read whether the game is
    // running a burn/lost/discard effect on it (the mirrored card plume, wire id 236). Left private,
    // that file would have had to resolve the question a SECOND time, and two independent resolves
    // that merely agree is the failure mode this project names outright. One answer, two consumers.
    internal static FullAbilityCard? TryLiveWidget(CPlayerActor? actor, CAbilityCard card)
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
