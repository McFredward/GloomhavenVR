using System;
using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core
{
    // Presentation diagnostics have no state or native interaction side effects.
    internal static class VRLog { internal static void Info(string area, string text) {} internal static void Note(string area, string text) {} }
}
namespace GloomhavenVR.Hands
{
    internal enum HapticPreset { ClickPulse }
    internal sealed class VRHand
    {
        internal string Side = "fixture";
        internal sealed class HandRig { internal Transform PalmCenter = new GameObject("palm").transform; }
        internal HandRig Rig = new();
        internal void SendHaptic(HapticPreset preset) {}
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomHand
    {
        // Map loadouts are outside the scenario regression. Native scenario widgets
        // must resolve their own key without using this unrelated fallback.
        internal static bool TryFanOrderKey(GloomhavenVR.Cards.VRCard card, out string key, out int id)
        { key = string.Empty; id = int.MinValue; return false; }
    }
}
namespace GloomhavenVR.Cards
{
    internal sealed class VRCard
    {
        internal AbilityCardUI? GameCard;
        internal bool InspectOnly;
        internal Transform transform = new GameObject("physical-card").transform;
    }
    internal sealed class CardFan
    {
        internal readonly List<VRCard> Entries = new();
        internal IReadOnlyList<VRCard> Cards => Entries;
        internal void Add(VRCard card) { if (!Entries.Contains(card)) Entries.Add(card); }
        internal void SetCards(List<VRCard> cards) { Entries.Clear(); Entries.AddRange(cards); }
        internal void SetInsertionGap(int gap) {}
    }
    internal sealed class PlayTray
    {
        private readonly VRCard?[] occupants = new VRCard?[2];
        internal int Target = -1;
        internal CardsHandUI? LastSyncedHand;
        internal readonly struct SlotProbe { internal string Describe() => "explicit release target"; }
        internal int SlotNear(Vector3 card, Vector3 palm, out SlotProbe probe) { probe = new SlotProbe(); return Target; }
        internal VRCard? Occupant(int slot) => occupants[slot];
        internal bool ContainsCard(VRCard card) => Array.IndexOf(occupants, card) >= 0;
        internal int SlotOf(VRCard card) => Array.IndexOf(occupants, card);
        internal void PlaceCard(VRCard card, int slot)
        {
            RemoveCard(card); occupants[slot] = card;
        }
        internal void RemoveCard(VRCard card) { for (int i = 0; i < 2; i++) if (ReferenceEquals(occupants[i], card)) occupants[i] = null; }
        internal void NoteSlotActivity() {}
        internal void SyncFromGameState(CardsHandUI hand, VRCardFactory factory) => LastSyncedHand = hand;
        // Inter-frame pending projection executes in the separate exact-PlayTray
        // fixture. This suite observes the original native selection operations.
        internal void SetSelectionPending(VRCard card, bool pending) {}
    }
    internal sealed class VRCardFactory {}
    internal static class CardsConfig
    {
        internal sealed class Entry { internal string Value = ""; }
        internal static Entry CardPlaceSound = new();
    }
    internal static class CardActionQueue
    {
        private static readonly Queue<Action> actions = new();
        internal static int Count => actions.Count;
        internal static void Enqueue(Action call, Action completion) => actions.Enqueue(() => { call(); completion(); });
        internal static void Drain() { while (actions.Count > 0) actions.Dequeue()(); }
    }
    internal static partial class CardsGameApi
    {
        // Production driver selection operations reach these exact original widget
        // entry points. Tutorial isolation is tested by its dedicated existing suite.
        internal static void SelectCard(CardsHandUI hand, CAbilityCard ability) => hand.SelectCard(ability);
        internal static void UnselectCard(CardsHandUI hand, CAbilityCard ability) => hand.UnselectCard(ability);
        internal static bool IsInRound(CardsHandUI hand, CAbilityCard ability) => hand.PlayerActor.CharacterClass.RoundAbilityCards.Contains(ability);
        internal static bool MustRestInsteadOfPlay(CardsHandUI hand) => hand.PlayerActor.CharacterClass.HandAbilityCards.Count + hand.PlayerActor.CharacterClass.RoundAbilityCards.Count < 2;
        internal static int PlayableCardCount(CardsHandUI hand) => hand.PlayerActor.CharacterClass.HandAbilityCards.Count + hand.PlayerActor.CharacterClass.RoundAbilityCards.Count;
        internal static CAbilityCard? InitiativeCard(CardsHandUI hand) => hand.PlayerActor.CharacterClass.InitiativeAbilityCard;
    }
    internal sealed partial class CardsDriver
    {
        internal static readonly FanOrderMemory _fanOrders = new();
        internal static readonly FanOrderMemory _unnamedFanOrders = new();
        internal List<int> _fanOrder = new();
        internal readonly CardFan _fan = new();
        internal readonly PlayTray _tray = new();
        internal readonly VRCardFactory _factory = new();
        internal readonly List<VRCard> _fanBuffer = new();
        internal readonly List<VRCard> _fanReorderScratch = new();
        internal VRCard? _snapHighlightCard;
        internal int _snapHighlightSlot = -1;
        internal VRCard? _insertHighlightCard;
        internal int _insertGap = -1;
        internal bool _dirty;
        internal CardsHandUI? _boundHand;
        internal void Release(VRCard card, CardsHandUI hand) { _fan.Entries.Remove(card); ReleaseSelection(card, new Hands.VRHand(), hand, false); }
        internal void Gap(VRCard card, int gap) { _insertHighlightCard = card; _insertGap = gap; }
        internal void ApplyOrder() { _fanBuffer.Clear(); _fanBuffer.AddRange(_fan.Cards); ReorderFanBuffer(); _fan.SetCards(_fanBuffer); }
        internal void Insert(VRCard card, int gap) => CommitFanInsertion(card, gap);
        internal static bool Key(VRCard card, out string key, out int id) => TryFanKey(card, out key, out id);
        internal void Reconcile(CardsHandUI hand) => ReconcileInitiative(hand);
        internal void Swap() => OnSwapRequested();
        private CardsHandUI? CurrentHand() => CardsHandManager.Instance.CurrentHand;
        private void ForeignInteraction(string context) {}
        private void PlayCardSound(string sound, Transform root) {}
    }
}
