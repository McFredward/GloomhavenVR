using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    // User, 2026-10-22: put an active rectangular card into the SAME berth as an item, then
    // explicitly confirm dismissal. Holding the card must never require pressing a second-hand
    // menu. This is a physical reservation of the original card, not a second card or a game-state
    // change. The game's own captured confirmation callback alone commits and synchronizes it.
    internal const float ActiveDismissFitScale = 1.04f * 0.94f;
    private const float ActiveDismissDropRadius = 0.13f; // same capture as ItemsPile.UseSlotRadius
    private VRCard? _dismissCard;
    private CardsHandUI? _dismissHand;
    private CardsGameApi.ActiveCardDismissal? _dismissDecision;
    private System.Action? _dismissConfirmAction;
    private bool _dismissCommitted;
    private GameObject? _dismissGhost;
    private Material? _dismissGhostMaterial;
    private bool _dismissBerthShown;
    private byte _dismissCount;
    private byte _dismissSeat = byte.MaxValue;
    private int _dismissActorId;
    private readonly System.Collections.Generic.Dictionary<AbilityCardUI, DismissFlightOriginal>
        _dismissFlightOriginals = new();
    private readonly System.Collections.Generic.List<AbilityCardUI> _dismissFlightPrune = new();
    private sealed class DismissFlightOriginal
    {
        internal readonly Net.CardAppearanceState Address;
        internal readonly CAbilityCard Card;
        internal readonly CPlayerActor Actor;
        internal DismissFlightOriginal(Net.CardAppearanceState address, CAbilityCard card, CPlayerActor actor)
        { Address = address; Card = card; Actor = actor; }
    }

    // One packet samples one live presentation. Addresses count the complete active ability-card
    // model population, including temporarily missing native UI artwork, exactly like held cards.
    internal static bool ActiveDismissBerthActive => Instance != null && Instance._dismissBerthShown;
    internal static bool ActiveDismissGhostShown => Instance != null
        && Instance._dismissGhost != null && Instance._dismissGhost.activeSelf;
    internal static byte ActiveDismissListCount => Instance != null ? Instance._dismissCount : (byte)0;
    internal static byte ActiveDismissSeat => Instance != null ? Instance._dismissSeat : byte.MaxValue;
    internal static int ActiveDismissActorId => Instance != null ? Instance._dismissActorId : 0;

    // Called only after an item drop has passed its original live candidacy/range gates, before
    // that flow installs its own cap. A rejected item drop must never displace this reservation.
    internal static void YieldActiveDismissBerthToItem() => Instance?.ResetActiveDismiss();

    private bool ItemOwnsDismissBerth() => ItemsPile.RecessOwner?.OwnsUseBerth == true;

    private void TickActiveDismiss(CardsHandUI? presented)
    {
        PruneActiveDismissFlights();
        if (_fakeActive || NativeSceneLoadInProgress || !_tray.IsVisible || _tray.ItemUseSlotTransform == null
            || presented == null || ItemOwnsDismissBerth())
        {
            ResetActiveDismiss();
            return;
        }

        // A native dismissal can retire between PollActive's dirty edge and the next rebuild.
        // Its berth is an animated visibility parent, not a flight owner: hiding it used to
        // shrink/hide the SAME card before the pile sweep could offer a flight (and also hid a
        // flight that had already started). Transfer the original to the stable board root FIRST.
        AdvanceCommittedActiveDismiss(presented);

        if (_dismissCommitted && (_dismissCard == null || _dismissHand == null
            || !ReferenceEquals(presented, _dismissHand)
            || !_dismissFlightOriginals.ContainsKey(_dismissCard.GameCard!)))
            CancelActiveDismiss();
        if (_dismissDecision != null && (_dismissCard == null
            || !ReferenceEquals(presented, _dismissHand) || !_active.Contains(_dismissCard)
            || !_dismissDecision.CanConfirm))
            CancelActiveDismiss();

        VRCard? candidate = _dismissCard ?? HeldDismissCandidate(presented);
        byte seat = _dismissSeat;
        if (candidate == null || (!_dismissCommitted && !TryAddressDismissCard(presented, candidate,
            out _dismissCount, out seat)))
        {
            HideActiveDismissBerth();
            return;
        }

        _dismissBerthShown = true;
        _dismissActorId = Net.NetFigures.StableActorId(presented.PlayerActor);
        _dismissSeat = _dismissCard != null ? seat : byte.MaxValue;
        bool summon = _dismissDecision != null ? _dismissDecision.DismissesSummon
            : DismissesSummon(candidate);
        // Original record13 carries one wording for both the engraved destination and its cap.
        // Keep that contract so an observer reads exactly the owner's visible action label.
        string label = Loc.Mod(summon ? "summon_dismiss_confirm" : "active_dismiss_confirm");
        _tray.SetActiveDismissPresentation(true, label, label,
            _dismissDecision != null ? _dismissConfirmAction : null);
        TickActiveDismissGhost(_dismissCard == null ? candidate : null);
    }

    private VRCard? HeldDismissCandidate(CardsHandUI hand)
    {
        if (_modalInputBlocked)
            return null;
        // An item in either hand keeps its own existing flow. Never offer two different actions
        // on the same destination while the player is holding both families of card.
        if (VRHands.Left?.Grabber.Held is ItemsPile.ItemChip
            || VRHands.Right?.Grabber.Held is ItemsPile.ItemChip)
            return null;
        foreach (VRCard card in _active.Cards)
        {
            if (card == null || !card.IsHeld || card.Holder == null || !card.Holder.HasPose
                || !CardsGameApi.CanDismissActiveCard(hand, card.GameCard))
                continue;
            return card;
        }
        return null;
    }

    private static bool DismissesSummon(VRCard card)
    {
        if (card.GameCard?.AbilityCard?.ActiveBonuses == null)
            return false;
        foreach (CActiveBonus bonus in card.GameCard.AbilityCard.ActiveBonuses)
            if (bonus != null && bonus.Duration == CActiveBonus.EActiveBonusDurationType.Summon)
                return true;
        return false;
    }

    private static bool TryAddressDismissCard(CardsHandUI hand, VRCard card,
        out byte count, out byte seat)
    {
        int total = 0, found = -1;
        var models = ActiveCardSet.ActivatedCards(hand.PlayerActor);
        if (models != null)
            foreach (CBaseCard model in models)
                if (model is CAbilityCard)
                {
                    if (ReferenceEquals(model, card.GameCard?.AbilityCard))
                        found = total;
                    total++;
                }
        count = total > 0 && total <= byte.MaxValue ? (byte)total : (byte)0;
        seat = found >= 0 && found < count ? (byte)found : byte.MaxValue;
        return count > 0 && seat != byte.MaxValue;
    }

    private bool TryStageActiveDismissCard(VRCard card, VRHand hand)
    {
        Transform? berth = _tray.ItemUseSlotTransform;
        if (_dismissCommitted && ReferenceEquals(card, _dismissCard) && berth != null)
        {
            card.SetHome(berth, Vector3.zero, Quaternion.identity, ActiveDismissFitScale);
            return true; // an accepted effect cannot be cancelled by reading its card again
        }
        CardsHandUI? presented = PresentedHandForCardLayout(CurrentHand());
        if (!hand.HasPose || !(hand.TriggerUp || hand.GripUp) || _modalInputBlocked
            || berth == null || !_tray.IsVisible || presented == null || ItemOwnsDismissBerth()
            || !CardsGameApi.CanDismissActiveCard(presented, card.GameCard))
            return false;
        float radius = ActiveDismissDropRadius * Mathf.Abs(berth.lossyScale.x);
        if ((card.transform.position - berth.position).sqrMagnitude > radius * radius
            || !TryAddressDismissCard(presented, card, out _, out _))
            return false;

        CancelActiveDismiss(); // replacing a pending card is a cancellation, never a commit
        CardsGameApi.ActiveCardDismissal? decision =
            CardsGameApi.RequestDismissActiveCard(presented, card.GameCard);
        if (decision == null)
            return false;
        _dismissCard = card;
        _dismissHand = presented;
        _dismissDecision = decision;
        _dismissConfirmAction = () => ConfirmActiveDismiss(decision);
        _active.StagedCard = card;
        card.SetLaserHover(false);
        card.ResetColliderRegion();
        card.SetHome(berth, Vector3.zero, Quaternion.identity, ActiveDismissFitScale);
        hand.SendHaptic(HapticPreset.HoverTick);
        PlayCardSound(CardsConfig.CardPlaceSound.Value, card.transform);
        TickActiveDismiss(presented);
        VRLog.Info("Cards", "ACTIVE DISMISS: card placed; awaiting explicit confirmation.");
        return true;
    }

    private void TakeBackActiveDismissCard(VRCard card)
    {
        if (!_dismissCommitted && ReferenceEquals(card, _dismissCard))
            CancelActiveDismiss();
    }

    private void ConfirmActiveDismiss(CardsGameApi.ActiveCardDismissal expected)
    {
        CardsGameApi.ActiveCardDismissal? decision = _dismissDecision;
        if (!ReferenceEquals(decision, expected))
            return; // a dismantled/replaced cap never confirms a later card's decision
        VRCard? card = _dismissCard;
        if (decision == null || card == null || card.IsHeld || !card.gameObject.activeInHierarchy
            || NativeSceneLoadInProgress || !_tray.IsVisible || _modalInputBlocked
            || !ReferenceEquals(PresentedHandForCardLayout(CurrentHand()), _dismissHand)
            || ItemOwnsDismissBerth() || !decision.CanConfirm)
        {
            CancelActiveDismiss();
            return;
        }
        // Retain the physical reservation until the original active model actually retires it.
        // Native callbacks may refresh the UI synchronously; remote clients may see that update
        // later. Neither may briefly return this same card to the active grid after acceptance.
        _dismissDecision = null;
        _dismissConfirmAction = null;
        _dismissCommitted = true;
        // Native UI refresh may be synchronous. Preserve both the complete active-list address
        // and the immutable native pool address before invoking the only gameplay callback.
        AbilityCardUI widget = card.GameCard!;
        var original = new Net.CardAppearanceState { ActorId = _dismissActorId };
        if (!Net.CardAppearanceProvenance.Capture(original, _dismissHand!.PlayerActor, widget.AbilityCard))
            VRLog.Warn("Cards", "ACTIVE DISMISS: original pool provenance unavailable; local flight keeps its actual card, no guessed remote berth identity is sent.");
        _dismissFlightOriginals[widget] = new DismissFlightOriginal(original, widget.AbilityCard, _dismissHand.PlayerActor);
        _activeExitOrigins[widget] = new Net.CardFlightSource(_dismissActorId, _dismissSeat, _dismissCount);
        bool accepted = false;
        try
        {
            accepted = decision.Confirm();
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Cards", $"ACTIVE DISMISS: original confirmation failed ({e.Message}).");
        }
        if (!accepted)
        {
            decision.Cancel();
            CancelActiveDismiss();
        }
        else
        {
            VRLog.Info("Cards", "ACTIVE DISMISS: explicit confirmation forwarded to the original game.");
            RequestRebuild();
        }
    }

    /// <summary>Only authoritative pile membership releases the staged original. An accepted
    /// callback, an active-list gap or a hidden native widget does not invent a destination.</summary>
    private void AdvanceCommittedActiveDismiss(CardsHandUI? presented)
    {
        VRCard? card = _dismissCard;
        CardsHandUI? hand = _dismissHand;
        if (!_dismissCommitted || card == null || hand == null || card.GameCard == null
            || !ReferenceEquals(presented, hand) || card.IsHeld || NativeSceneLoadInProgress)
            return;
        RoundCardExit exit = RoundCardExitOf(hand, card, out _);
        if (exit is not (RoundCardExit.Discarded or RoundCardExit.Lost or RoundCardExit.PermanentlyLost))
            return;
        Transform? root = _tray.Root;
        PileKind pile = exit == RoundCardExit.Discarded ? PileKind.Discard : PileKind.Burnt;
        if (root == null || !_piles.TryGetPileWorld(pile, out _, out _))
            return;

        // SetParent(true) retains the current drop/settle pose, including a fast mid-glide
        // confirmation. The new home is that same pose; later active-grid or berth visibility
        // changes cannot pull the card away while the existing native burn gate owns it.
        PreserveActiveDismissFlightPose(card);
        _active.StagedCard = null;
        _dismissCard = null;
        _dismissHand = null;
        _dismissCommitted = false;
        HideActiveDismissBerth();
        TryStartFlyToPile(hand, card); // burn fate keeps its original artwork/completion hold
    }

    private void PreserveActiveDismissFlightPose(VRCard card)
    {
        Transform? root = _tray.Root;
        if (card.GameCard == null || !_dismissFlightOriginals.ContainsKey(card.GameCard)
            || root == null || ReferenceEquals(card.transform.parent, root))
            return;
        card.transform.SetParent(root, worldPositionStays: true);
        card.SetHome(root, card.transform.localPosition, card.transform.localRotation,
            card.transform.localScale.x, instant: true);
        card.Grabbable = false;
    }

    private Net.CardDismissFlightOrigin? TakeActiveDismissFlightOrigin(AbilityCardUI? widget, VRCard? card)
    {
        if (widget == null || !_dismissFlightOriginals.TryGetValue(widget, out DismissFlightOriginal? captured)
            || captured == null)
            return null;
        _dismissFlightOriginals.Remove(widget);
        Net.CardAppearanceState original = captured.Address;
        Transform? berth = _tray.ItemUseSlotTransform;
        if (berth == null || original.SourceActorId == 0 || original.PoolCount == 0)
            return null;
        if (card == null && (!_lastCardWorldPos.ContainsKey(widget)
            || !_lastCardWorldRot.ContainsKey(widget) || !_lastCardWorldWidth.ContainsKey(widget)))
            return null; // no measured origin: never fabricate a berth-centre flight
        Vector3 position = card != null ? card.transform.position : _lastCardWorldPos[widget];
        Quaternion rotation = card != null ? card.transform.rotation : _lastCardWorldRot[widget];
        float width = card != null ? card.transform.lossyScale.x * CardsConfig.CardWidth.Value : _lastCardWorldWidth[widget];
        var pose = new Net.RigPose { Position = berth.InverseTransformPoint(position),
            Rotation = Quaternion.Inverse(berth.rotation) * rotation };
        return new Net.CardDismissFlightOrigin(original.SourceActorId, original.PoolSeat,
            original.PoolCount, pose, width / Mathf.Max(0.00001f, Mathf.Abs(berth.lossyScale.x)));
    }

    private void ForgetActiveDismissFlight(AbilityCardUI widget)
    {
        if (_dismissFlightOriginals.Remove(widget)) _activeExitOrigins.Remove(widget);
    }

    private void PruneActiveDismissFlights()
    {
        _dismissFlightPrune.Clear();
        foreach (var entry in _dismissFlightOriginals)
        {
            AbilityCardUI widget = entry.Key;
            DismissFlightOriginal source = entry.Value;
            CCharacterClass? character = source.Actor.CharacterClass;
            if (character == null || widget == null || !ReferenceEquals(widget.AbilityCard, source.Card)
                || !ReferenceEquals(widget.PlayerActor, source.Actor)
                || character.HandAbilityCards.Contains(source.Card)
                || character.RoundAbilityCards.Contains(source.Card) || character.ExtraTurnCards.Contains(source.Card)
                || (!ReferenceEquals(_dismissCard?.GameCard, widget) && character.ActivatedCards.Contains(source.Card)))
                _dismissFlightPrune.Add(widget!);
        }
        foreach (AbilityCardUI widget in _dismissFlightPrune) ForgetActiveDismissFlight(widget);
        _dismissFlightPrune.Clear();
    }

    private void CancelActiveDismiss()
    {
        VRCard? card = _dismissCard;
        CardsHandUI? hand = _dismissHand;
        CardsGameApi.ActiveCardDismissal? decision = _dismissDecision;
        if (card?.GameCard != null)
        {
            _dismissFlightOriginals.Remove(card.GameCard);
            // Cancelling an offer is not an active-card departure. A committed departure that
            // has transferred to the flight owner has already cleared _dismissCard above.
            _activeExitOrigins.Remove(card.GameCard);
        }
        _dismissCard = null;
        _dismissHand = null;
        _dismissDecision = null;
        _dismissConfirmAction = null;
        _dismissCommitted = false;
        _active.StagedCard = null;
        decision?.Cancel();
        if (card != null && hand != null && _active.Contains(card) && card.gameObject.activeInHierarchy
            && ActiveCardSet.IsActive(hand.PlayerActor, card.GameCard?.AbilityCard))
            _active.Add(card); // held cards keep their pose; releasing returns them to the same grid
    }

    private void HideActiveDismissBerth()
    {
        _dismissBerthShown = false;
        _dismissCount = 0;
        _dismissSeat = byte.MaxValue;
        _dismissActorId = 0;
        if (_dismissGhost != null)
            _dismissGhost.SetActive(false);
        _tray.SetActiveDismissPresentation(false);
    }

    private void ResetActiveDismiss()
    {
        CancelActiveDismiss();
        HideActiveDismissBerth();
    }

    private void DestroyActiveDismiss()
    {
        ResetActiveDismiss();
        if (_dismissGhostMaterial != null)
            UnityEngine.Object.Destroy(_dismissGhostMaterial);
        _dismissGhostMaterial = null;
    }

    private void TickActiveDismissGhost(VRCard? held)
    {
        Transform? slot = _tray.ItemUseSlotTransform;
        float near = slot != null ? ActiveDismissDropRadius * 2.2f * Mathf.Abs(slot.lossyScale.x) : 0f;
        bool show = held != null && slot != null
            && (held.transform.position - slot.position).sqrMagnitude <= near * near;
        if (show && _dismissGhost == null && slot != null)
        {
            var ghost = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ghost.name = "GloomhavenVR.ActiveDismissGhost";
            UnityEngine.Object.Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(slot, worldPositionStays: false);
            ghost.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            ghost.transform.localRotation = Quaternion.identity;
            ghost.transform.localScale = new Vector3(CardsConfig.CardWidth.Value * ActiveDismissFitScale,
                CardsConfig.CardHeight * ActiveDismissFitScale, 1f);
            MeshRenderer renderer = ghost.GetComponent<MeshRenderer>();
            Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null)
            {
                if (_dismissGhostMaterial != null)
                    UnityEngine.Object.Destroy(_dismissGhostMaterial);
                _dismissGhostMaterial = new Material(shader) { color = new Color(0.92f, 0.85f, 0.5f, 0.34f) };
                renderer.sharedMaterial = _dismissGhostMaterial;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            VRLayers.Apply(ghost);
            PlayTray.AdoptFurniture(ghost);
            _dismissGhost = ghost;
        }
        if (_dismissGhost != null && _dismissGhost.activeSelf != show)
            _dismissGhost.SetActive(show);
        if (show && _dismissGhost != null)
            _dismissGhost.transform.localScale = new Vector3(CardsConfig.CardWidth.Value * ActiveDismissFitScale,
                CardsConfig.CardHeight * ActiveDismissFitScale, 1f);
    }
}
