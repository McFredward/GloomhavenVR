internal enum CardHandMode { CardsSelection, DiscardCard, LoseCard, RecoverDiscardedCard }
internal sealed class CardsHandUI { public CardHandMode Mode = CardHandMode.DiscardCard; public bool PickLive = true; public bool Popup; public bool SelectionReady; public bool LongRest; public bool ShortRest; public bool ShortChoosing; public bool SelectionPhase = true; public bool Placement = true; public int Wanted = 2; }
internal sealed class Widget { public bool IsSelected = true; }
internal sealed class VRCard { public Widget? GameCard = new(); public bool IsHeld; public bool Parked; }
internal static class CardsConfig { internal sealed class Setting { public bool Value = true; } public static Setting WantedSlotHint = new(); }
internal static class Mathf { public static int Clamp(int n, int lo, int hi) => Math.Clamp(n,lo,hi); public static int Min(int a,int b) => Math.Min(a,b); }
internal static class CardsGameApi {
 public static int Total = 3;
 public static CardHandMode Mode(CardsHandUI h) => h.Mode;
 public static bool IsShortRestChoosing(CardsHandUI h) => h.ShortChoosing;
 public static bool IsSelectionPhase(CardsHandUI h) => h.SelectionPhase;
 public static bool IsLongRestSelected(CardsHandUI h) => h.LongRest;
 public static bool IsShortRestSelected(CardsHandUI h) => h.ShortRest;
 public static bool IsSelectionReady(CardsHandUI h) => h.SelectionReady;
 public static int SelectionCardsStillWanted(CardsHandUI h) => h.Wanted;
 public static int PickCardsWanted() => Total;
 public static bool IsPickConfirmDialogOpen(CardsHandUI h) => h.Popup;
}
internal sealed class Tray { public bool IsVisible = true; public int Mask; public VRCard?[] Occupants = new VRCard?[2]; public VRCard? Occupant(int i) => Occupants[i]; public void SetWantedSlots(int m) => Mask = m; }
internal sealed partial class CardsDriver {
 public readonly Tray _tray = new(); public bool _overlayGateBlocked; public bool Exiting;
 public float _overlayExitHeldSince; public object? _loggedOverlayExit;
 public readonly List<VRCard> _fieldCards = new(); public readonly HashSet<VRCard> _pickExitFlown = new(); public readonly HashSet<VRCard> _pickReturnFlight = new();
 public int _pickLockedCount; public bool _pickReopenBusy; public int _loggedFieldOverflow;
 private static bool PlacementIsOffered(CardsHandUI h) => h.Placement;
 private static bool PickFlowLive(CardsHandUI h) => h.PickLive;
 private bool BoardStillOwnsACardsExit(out int holds,out int flights) { holds=Exiting?1:0; flights=0; return Exiting; }
 private static void LogOverlayHeldByExit(int holds,int flights) { }
 private static bool IsParked(VRCard card) => card.Parked;
 public void Tick(CardsHandUI? h) => UpdateWantedSlots(h);
 public void Prune() => PrunePickField(); public int Restart() => ArmPickRestart(); public int Target() => PickTargetSlot();
}
