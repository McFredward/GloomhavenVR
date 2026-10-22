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
        internal Vector3 localPosition;
        internal Vector3 lossyScale=new(1);
        internal void SetParent(Transform? target, bool worldPositionStays) => parent=target;
    }
    internal static class Time { internal static float unscaledTime; }
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
        internal enum EItemSlotState { Useable, Selected, Consumed, Spent }
        internal enum EUsageType { None, Consumed, Spent }
        internal sealed class ItemData { internal EUsageType Usage; }
        internal ItemData? YMLData=new();
        internal EItemSlotState SlotState;
        internal bool Candidate=true, Accept=true, Activatable=true, Choice, ChoiceSlot=true;
        internal CActiveBonus? Bonus;
    }
    internal sealed class CActiveBonus { internal bool Locked; }
    internal sealed class CPlayerActor { internal bool IsUnderMyControl=true; }
}
namespace GloomhavenVR.Cards
{
    internal sealed class CardsHandUI { internal CPlayerActor PlayerActor=new(); }
    internal static class FFSNetwork { internal static bool IsOnline; }
    internal static class PileFanShape
    {
        internal static Vector3 BoardAnchorLive(Vector3 offset) => offset;
        internal static void FaceHead(Transform root) { }
    }
    internal sealed class UIUseItemScenario { internal CItem Item=new(); }
    internal sealed class ItemCardPicker { internal HashSet<CItem> Selected=new(); }
    internal sealed class PlayTray
    {
        internal static PlayTray? Current=new();
        internal Transform? Root=new(), ItemUseSlotTransform=new();
        internal bool Confirm, ActiveDismissOwner;
        internal string? AreaLabel;
        internal int ActiveDismissCancels, ActiveDismissCommits;
        internal Action? OnConfirm;
        internal void SetItemUseSlotVisible(bool value) { if(!ActiveDismissOwner)ItemUseSlotTransform!.gameObject.SetActive(value); }
        internal void SetItemUseConfirmVisible(bool value, Action? action, string? label=null) { if(!ActiveDismissOwner){Confirm=value; OnConfirm=action;} }
        internal void SetItemUseAreaLabel(string? label) { if(!ActiveDismissOwner)AreaLabel=label; }
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
        internal static int Deselects, Toggles, Confirms, RewardConfirms;
        internal static bool ConfirmAccepted=true, PickerOpen=true, Refreshing, LoseReward, CanSelect=true;
        internal static CPlayerActor? Actor;
        internal static Action? DuringConfirm;
        internal static bool ConfirmItemPick() { Confirms++;if(ConfirmAccepted)PickerOpen=false;DuringConfirm?.Invoke();return ConfirmAccepted; }
        internal static bool ConfirmLoseRewardPick() { RewardConfirms++;if(ConfirmAccepted)PickerOpen=false;DuringConfirm?.Invoke();return ConfirmAccepted; }
        internal static ItemCardPicker? OpenItemPicker(out CPlayerActor? actor, out bool refreshing) { actor=Actor;refreshing=Refreshing;return PickerOpen&&!LoseReward?Picker:null; }
        internal static ItemCardPicker? OpenLoseRewardPicker(out bool canSelect) { canSelect=CanSelect;return PickerOpen&&LoseReward?Picker:null; }
        internal static bool ItemPickReady(ItemCardPicker picker) => picker.Selected.Count>0;
        internal static int ItemPickWanted(ItemCardPicker picker) => 1;
        internal static string ActorLabel(CPlayerActor actor) => "actor";
        internal static bool IsActionTurn(CardsHandUI hand) => true;
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
        private ItemChip? _pendingUseChip, _demandChip, _tdChip, _keptClip, _finishingUseChip;
        private Transform? _root=new(), _anchor=new();
        private GameObject? _useGhost=new();
        private int _keptClipIndex=-1;
        private bool _boardAnchored=true, _pendingSubChoice, _choiceClickArmed, _choiceCapShown, _choiceCapReady;
        private bool _pendingBonusToggled, _useResolving, _useSlotShownLogged, _demandLoseReward, _demandRefreshing, _demandActive, _tdActive;
        private float _demandCueAt;
        private int _splitPivotIndex=-1;
        private int SplitPivotIndex => -1;
        private Vector3 ItemFanOffset => new();
        private UIUseItemScenario? _pendingChoiceSlot;
        private CActiveBonus? _pendingBonus;
        private CardsHandUI? _hand=new();
        private string _lastCloseReason="never", _signature="original";
        private Action<ItemChip, Vector3, VRHand>? _inspectionRelease;
        private bool _inspectionCensusDirty;
        internal bool IsOpen=true;
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
        private static string Signature(CardsHandUI hand) => "original";
        private void Populate(CardsHandUI hand) => _signature=Signature(hand);
        private void UpdateHandSweep() { }
        private void Relayout() { }
        private void TickUseGhost(bool show, ItemChip? candidate) { }
        private void TickPendingUse(CardsHandUI hand) { }
        private ItemChip? HeldActivatableChip() => _chips.FirstOrDefault(chip=>chip.Holder!=null&&chip.IsActivatable);
        private void ForgetSweepWinner(ItemChip chip) { }
        private Vector3 PileConvergeWorld() => new(5);
        private ItemCardPicker? ActiveDemandPicker() => CardsGameApi.Picker;
        private ItemCardPicker? DemandCandidateFilter() => _demandActive ? CardsGameApi.Picker : null;
        private string DemandConfirmLabel() => "surrender";
        internal static ItemsPile Create(string flow, out ItemChip card, out ItemChip sibling)
        {
            PlayTray.Current=new(); CardsGameApi.Picker=new(); CardsGameApi.Deselects=CardsGameApi.Toggles=CardsGameApi.Confirms=CardsGameApi.RewardConfirms=CardsDriver.Sounds=CardsDriver.BerthClaims=0;
            CardsGameApi.ConfirmAccepted=CardsGameApi.PickerOpen=CardsGameApi.CanSelect=true;
            CardsGameApi.Refreshing=CardsGameApi.LoseReward=false;CardsGameApi.DuringConfirm=null;
            var pile=new ItemsPile();card=new();sibling=new();pile._chips.Add(sibling);pile._chips.Add(card);
            card.transform.parent=pile._root;
            pile._demandActive=flow=="demand";pile._tdActive=flow=="damage";
            CardsGameApi.Actor=pile._hand!.PlayerActor;
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
        internal bool Finishing=>UseAnimationPending;
        internal CardsHandUI? Hand=>_hand;
        internal void ConfirmDemand() => ConfirmDemandPick();
        internal void ConfigureDemand(bool refreshing, bool reward)
        {
            _demandRefreshing=CardsGameApi.Refreshing=refreshing;
            _demandLoseReward=CardsGameApi.LoseReward=reward;
        }
        internal void EndDemandNow() => EndDemand("native callback reentry");
        internal void ReopenForTest(ItemChip chip)
        {
            IsOpen=true;_root!.gameObject.SetActive(true);
            if(!_chips.Contains(chip))_chips.Add(chip);
            _keptClip=null;_keptClipIndex=-1; // Populate reinserts the original and releases its closed-fan address.
        }
        internal void NewDemand() { CardsGameApi.Picker=new();CardsGameApi.PickerOpen=true;TickDemandPick(_hand); }
        internal void CompleteAgain(ItemChip chip) => CompleteUsedChipPresentation(chip);
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
            internal int Collapses, Returns, Clips, UsePresentations;
            internal bool Burn, Spent;
            internal bool HasOfferedBonus=>Item?.Bonus!=null;
            internal Action? Completion;
            internal void ClearHandSuppressed() { }
            internal void CancelReleaseGlide() { }
            internal void ClipIntoSlot(Transform slot) { transform.parent=slot;Clips++; }
            internal void ReturnToFan() => Returns++;
            internal void BeginCollapse(Vector3 point) { Collapses++; IsCollapsing=true; }
            // Native animation timing is an explicit boundary. Production retirement must never
            // start a competing collapse before this completion; that would preempt TickUseFx
            // because ItemChip.Update services _collapsing before _useFxActive.
            internal void PlayUseThenCollapse(bool consumed, bool spent, Vector3 point, Action completed)
            {
                UsePresentations++;Burn=consumed;Spent=spent;Completion=completed;
            }
            internal void FinishPresentation()
            {
                if(IsCollapsing)throw new Exception("A competing collapse preempted native use completion");
                Action? complete=Completion;Completion=null;complete?.Invoke();BeginCollapse(new());
            }
        }
    }
}
