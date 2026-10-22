using UnityEngine;
using ScenarioRuleLibrary;
using GloomhavenVR.Hands;

namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float value) { x=value; y=z=0; }
        internal static Vector3 zero => new();
        internal float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x);
    }
    internal sealed class GameObject
    {
        internal bool activeSelf=true;
        internal bool activeInHierarchy => activeSelf;
        internal void SetActive(bool value) => activeSelf=value;
    }
    internal sealed class Transform
    {
        internal GameObject gameObject=new();
        internal Transform? parent;
        internal Vector3 position;
        internal Vector3 lossyScale=new(1);
        internal void SetParent(Transform? target, bool worldPositionStays) => parent=target;
    }
}
namespace GloomhavenVR.Hands
{
    internal enum HapticPreset { HoverTick }
    internal sealed class VRHand
    {
        internal string Side="right";
        internal int Pulses;
        internal Grabber Grabber=new();
        internal void SendHaptic(HapticPreset preset) => Pulses++;
    }
    internal sealed class Grabber { internal void CancelAll() { } }
}
namespace ScenarioRuleLibrary
{
    internal sealed class CItem
    {
        internal enum EItemSlotState { Useable, Selected, Consumed }
        internal EItemSlotState SlotState;
        internal bool Candidate=true, Accept=true, Activatable=true, Choice, ChoiceSlot=true;
        internal CActiveBonus? Bonus;
    }
    internal sealed class CActiveBonus { internal bool Locked; }
}
namespace GloomhavenVR.Cards
{
    internal sealed class CardsHandUI { }
    internal sealed class UIUseItemScenario { internal CItem Item=new(); }
    internal sealed class ItemCardPicker { internal HashSet<CItem> Selected=new(); }
    internal sealed class PlayTray
    {
        internal static PlayTray? Current=new();
        internal Transform? Root=new(), ItemUseSlotTransform=new();
        internal bool Confirm, ActiveDismissOwner;
        internal int ActiveDismissCancels, ActiveDismissCommits;
        internal Action? OnConfirm;
        internal void SetItemUseSlotVisible(bool value) { if(!ActiveDismissOwner)ItemUseSlotTransform!.gameObject.SetActive(value); }
        internal void SetItemUseConfirmVisible(bool value, Action? action) { if(!ActiveDismissOwner){Confirm=value; OnConfirm=action;} }
        internal void SetItemUseAreaLabel(string? label) { }
        internal void ReserveActiveDismiss() { ActiveDismissOwner=true;Confirm=true;OnConfirm=()=>ActiveDismissCommits++; }
        internal void ReleaseActiveDismiss()
        {
            if(!ActiveDismissOwner)return;
            ActiveDismissCancels++;
            ActiveDismissOwner=false;
            Confirm=false;OnConfirm=null;
            ItemUseSlotTransform!.gameObject.SetActive(false);
        }
    }
    internal static class CardsDriver
    {
        internal static int Sounds, BerthClaims;
        internal static void PlayCardSound(string sound, Transform target) => Sounds++;
        internal static void YieldActiveDismissBerthToItem() { BerthClaims++;PlayTray.Current!.ReleaseActiveDismiss(); }
    }
    internal static class CardsConfig
    {
        internal static Dial CardPlaceSound=new();
        internal sealed class Dial { internal string Value="place"; }
    }
    internal static class CardsGameApi
    {
        internal static ItemCardPicker Picker=new();
        internal static int Deselects, Toggles;
        internal static CActiveBonus? PlaceableBonusForItem(CItem item, object? actor) => item.Bonus;
        internal static bool ItemNeedsSubChoice(CItem item, CardsHandUI? hand) => item.Choice;
        internal static UIUseItemScenario? ItemsBarSlot(CItem item, bool requireActive) => item.ChoiceSlot ? new(){Item=item} : null;
        internal static UIUseItemScenario? LiveItemsBarSlot(CItem item) => item.Candidate ? new(){Item=item} : null;
        internal static bool IsItemPickCandidate(ItemCardPicker picker, CItem item) => item.Candidate;
        internal static bool ItemPickSelect(ItemCardPicker picker, CItem item) { if(!item.Accept) return false; picker.Selected.Add(item); return true; }
        internal static void ItemPickDeselect(ItemCardPicker picker, CItem item) { picker.Selected.Remove(item); Deselects++; }
        internal static bool ClickItemsBarSlot(UIUseItemScenario slot) { if(!slot.Item.Accept) return false; Toggles++; slot.Item.SlotState=slot.Item.SlotState==CItem.EItemSlotState.Selected?CItem.EItemSlotState.Useable:CItem.EItemSlotState.Selected; return true; }
        internal static bool ActiveBonusToggleLocked(CActiveBonus bonus) => bonus.Locked;
        internal static string BonusCardName(CActiveBonus bonus) => "bonus";
    }
    internal static class VRLog { internal static void Info(string area, string message) { } internal static void Warn(string area, string message) { } }
    internal sealed partial class ItemsPile
    {
        internal static ItemsPile? Current;
        private const float UseSlotRadius=.13f;
        private readonly List<ItemChip> _chips=new();
        private ItemChip? _pendingUseChip, _demandChip, _tdChip, _keptClip;
        private Transform? _root=new(), _anchor=new();
        private GameObject? _useGhost=new();
        private int _keptClipIndex=-1;
        private bool _boardAnchored=true, _pendingSubChoice, _choiceClickArmed, _choiceCapShown, _choiceCapReady;
        private bool _pendingBonusToggled, _useResolving, _useSlotShownLogged, _demandLoseReward, _demandActive, _tdActive;
        private UIUseItemScenario? _pendingChoiceSlot;
        private CActiveBonus? _pendingBonus;
        private CardsHandUI? _hand=new();
        private string _lastCloseReason="never", _signature="original";
        private Action<ItemChip, Vector3, VRHand>? _inspectionRelease;
        private bool _inspectionCensusDirty;
        internal bool IsOpen=true, UseAnimationPending;
        internal int OrdinaryCancels, ChoiceTicks, BonusTicks, Clears, Decisions;
        internal object OwnerActor=new();
        private void AbandonChoice(CItem? item) => OrdinaryCancels++;
        private void ReleaseBonusToggle(string reason) { _pendingBonus=null; _pendingBonusToggled=false; }
        private void ClearHandSweep() => Clears++;
        private void CollapseChips() { foreach(var chip in _chips) chip.BeginCollapse(new()); _chips.Clear(); }
        private void ConfirmPendingUse() => Decisions++;
        private void ConfirmPendingBonus() => Decisions++;
        private void ShowChoiceCap(bool ready) => PlayTray.Current!.SetItemUseConfirmVisible(true, ConfirmPendingUse);
        private bool CanPlaceChoiceItem(CItem? item) => item!=null && (!item.Choice || item.ChoiceSlot);
        private void ReturnResolvingCardToRecess(ItemChip chip, Transform slot, VRHand hand) => chip.ClipIntoSlot(slot);
        private void RefuseLockedBonusRemoval(ItemChip chip, CActiveBonus bonus) { }
        private void TickUseResolving(ItemChip chip) => PlayTray.Current!.SetItemUseSlotVisible(true);
        private void TickBonusDecision(ItemChip chip) { BonusTicks++; PlayTray.Current!.SetItemUseSlotVisible(true); }
        private static bool WasUsed(CItem item) => item.SlotState==CItem.EItemSlotState.Consumed;
        private void BeginUseResolving(ItemChip chip, CItem item, string reason) => _useResolving=true;
        private static bool PlayContinued(CardsHandUI? hand, ItemChip chip) => !chip.IsActivatable;
        private void TickChoiceDecision(ItemChip chip) { ChoiceTicks++; _choiceClickArmed=false; }
        private void CancelPendingUse(string reason) { _pendingUseChip=null; _pendingSubChoice=false; if(_keptClip!=null) _keptClip.PendingUse=false; PlayTray.Current!.SetItemUseConfirmVisible(false,null); }
        private void UnclipChip(ItemChip chip) => chip.transform.SetParent(_root, true);
        private void RefreshFanLayout() { }
        private void ForgetSweepWinner(ItemChip chip) { }
        private Vector3 PileConvergeWorld() => new(5);
        private ItemCardPicker? ActiveDemandPicker() => CardsGameApi.Picker;
        private ItemCardPicker? DemandCandidateFilter() => _demandActive ? CardsGameApi.Picker : null;
        private string DemandConfirmLabel() => "surrender";
        internal static ItemsPile Create(string flow, out ItemChip card, out ItemChip sibling)
        {
            PlayTray.Current=new(); CardsGameApi.Picker=new(); CardsGameApi.Deselects=CardsGameApi.Toggles=CardsDriver.Sounds=CardsDriver.BerthClaims=0;
            var pile=new ItemsPile();card=new();sibling=new();pile._chips.Add(sibling);pile._chips.Add(card);
            card.transform.parent=pile._root;
            pile._demandActive=flow=="demand";pile._tdActive=flow=="damage";
            if(flow=="bonus") card.Item!.Bonus=new();
            if(flow=="choice") card.Item!.Choice=true;
            return pile;
        }
        internal bool Owns(ItemChip chip) => ReferenceEquals(_pendingUseChip,chip)||ReferenceEquals(_demandChip,chip)||ReferenceEquals(_tdChip,chip);
        internal bool Kept(ItemChip chip) => ReferenceEquals(_keptClip,chip);
        internal bool ChoiceArmed=>_pendingSubChoice&&_choiceClickArmed;
        internal bool RootVisible=>_root!.gameObject.activeSelf;
        internal bool OrdinaryPending=>_pendingUseChip!=null;
        internal bool DemandPending=>_demandChip!=null;
        internal bool DamagePending=>_tdChip!=null;
        internal void TickClosed() => TickPlacedWhileClosed(_hand);
        internal void End(string flow) {if(flow=="demand")EndDemand("test decision ended");else EndTakeDamagePick("test decision ended");}
        internal void ReturnTaken(ItemChip chip) { chip.Holder=null;chip.PendingUse=false;AfterRefusedDrop(chip); }
        internal void ClearPicked(string flow) { if(flow=="demand")_demandChip=null;else _tdChip=null; }
        internal bool HeldCandidate(string flow, ItemChip chip) => ReferenceEquals(flow=="demand"?HeldDemandCandidate():HeldTakeDamageCandidate(), chip);
        internal sealed class ItemChip
        {
            internal CItem? Item=new();
            internal VRHand? Holder;
            internal bool PendingUse, TownOffering, IsCollapsing;
            internal string name="item";
            internal Transform transform=new();
            internal GameObject gameObject=new();
            internal bool IsActivatable=>Item?.Activatable==true;
            internal int Collapses, Returns, Clips;
            internal void ClearHandSuppressed() { }
            internal void CancelReleaseGlide() { }
            internal void ClipIntoSlot(Transform slot) { transform.parent=slot;Clips++; }
            internal void ReturnToFan() => Returns++;
            internal void BeginCollapse(Vector3 point) { Collapses++; IsCollapsing=true; }
        }
    }
}
