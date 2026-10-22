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

    // One packet samples one live presentation. Addresses count the complete active ability-card
    // model population, including temporarily missing native UI artwork, exactly like held cards.
    internal static bool ActiveDismissBerthActive => Instance != null && Instance._dismissBerthShown;
    internal static bool ActiveDismissGhostShown => Instance != null
        && Instance._dismissGhost != null && Instance._dismissGhost.activeSelf;
    internal static byte ActiveDismissListCount => Instance != null ? Instance._dismissCount : (byte)0;
    internal static byte ActiveDismissSeat => Instance != null ? Instance._dismissSeat : byte.MaxValue;

    // Called only after an item drop has passed its original live candidacy/range gates, before
    // that flow installs its own cap. A rejected item drop must never displace this reservation.
    internal static void YieldActiveDismissBerthToItem() => Instance?.ResetActiveDismiss();

    private bool ItemOwnsDismissBerth() => ItemsPile.RecessOwner?.OwnsUseBerth == true;

    private void TickActiveDismiss(CardsHandUI? presented)
    {
        if (_fakeActive || NativeSceneLoadInProgress || !_tray.IsVisible || _tray.ItemUseSlotTransform == null
            || presented == null || ItemOwnsDismissBerth())
        {
            ResetActiveDismiss();
            return;
        }

        if (_dismissCommitted && (_dismissCard == null || _dismissHand == null
            || !ReferenceEquals(presented, _dismissHand)
            || !ActiveCardSet.IsActive(_dismissHand.PlayerActor, _dismissCard.GameCard?.AbilityCard)))
            CancelActiveDismiss();
        if (_dismissDecision != null && (_dismissCard == null
            || !ReferenceEquals(presented, _dismissHand) || !_active.Contains(_dismissCard)
            || !_dismissDecision.CanConfirm))
            CancelActiveDismiss();

        VRCard? candidate = _dismissCard ?? HeldDismissCandidate(presented);
        if (candidate == null || !TryAddressDismissCard(presented, candidate,
            out _dismissCount, out byte seat))
        {
            HideActiveDismissBerth();
            return;
        }

        _dismissBerthShown = true;
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

    private void CancelActiveDismiss()
    {
        VRCard? card = _dismissCard;
        CardsHandUI? hand = _dismissHand;
        CardsGameApi.ActiveCardDismissal? decision = _dismissDecision;
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
