using System;

namespace GloomhavenVR.Net;

// Explicit native model/transport/render boundary: production source supplies the commitment,
// actual staged-pose capture and one-time renderer handover decisions. These adapters do not
// execute Unity objects, native artwork playback or Photon delivery.
internal readonly struct CardFlightSource
{
    internal readonly int ActorId;
    internal readonly byte Seat, Count;
    internal CardFlightSource(int actor, byte seat, byte count) { ActorId = actor; Seat = seat; Count = count; }
    internal bool Validate() => ActorId != 0 && (Count == 0 ? Seat == 0 : Seat < Count);
}
internal readonly struct RigPose
{
    internal readonly FixtureVector Position;
    internal readonly FixtureRotation Rotation;
    internal RigPose(FixtureVector at, FixtureRotation facing) { Position = at; Rotation = facing; }
}
internal sealed class CardDismissFlightOrigin
{
    internal readonly RigPose BerthPose;
    internal readonly float Width;
    internal readonly int SourceActorId = 101;
    internal readonly ushort PoolSeat = 1, PoolCount = 3;
    internal CardDismissFlightOrigin(RigPose pose, float width) { BerthPose = pose; Width = width; }
}
internal enum CardFxAnchor : byte { Board, HandFan, Slot0, Slot1, Discard, Burnt, Active }
internal static class NetCardFx
{
    internal static CardFxAnchor From(byte endpoints) => (CardFxAnchor)(endpoints & 15);
    internal static CardFxAnchor To(byte endpoints) => (CardFxAnchor)(endpoints >> 4);
    internal static byte Pair(CardFxAnchor from, CardFxAnchor to) => (byte)((byte)from | (byte)to << 4);
}
internal static class RemoteHandFan { internal const float DefaultCardWidth = .0635f; }
internal static class RemoteBoardFocus
{
    internal static readonly System.Collections.Generic.Dictionary<int, CPlayerActor> Actors = new();
    internal static CPlayerActor? ActorById(int actor) => Actors.TryGetValue(actor, out var found) ? found : null;
    internal static CPlayerActor? DisplayedActor(RemoteAvatar owner, out bool ignored)
    { ignored = false; return owner.Actor; }
}
internal sealed class FixtureFan { internal void RefreshFlightSeats() { } }
internal sealed partial class RemoteAvatar
{
    internal CPlayerActor? Actor;
    internal readonly FixtureTransform StableBoard = new();
    internal FixtureTransform? ItemUseRecess;
    internal RemoteActiveCards? ActiveCards;
    internal readonly FixtureFan HandFan = new();
    internal bool OriginalVisible = true;
    internal readonly System.Collections.Generic.HashSet<int> PendingDismiss = new();
    internal int TransferCount;
    internal int TransferredCard = int.MinValue;
    internal float BoardScale = 1;
    internal bool HoldsBurnCardLayout = false;
    internal int PlayerId = 22;
    private long generation;
    internal long NextFlightOwnership() => ++generation;
    internal bool DismissFlightOwnsCard(int card) => PendingDismiss.Contains(card);
    internal bool RetainsDismissOriginal(int card) => PendingDismiss.Contains(card);
    internal bool TryDrawnBoardPose(out FixtureVector at, out FixtureRotation rotation, out float scale)
    { at = default; rotation = FixtureRotation.identity; scale = BoardScale; return true; }
    internal FixtureTuning BoardTuning => new();
    internal bool TryDismissPose(FixtureCard card, out FixtureVector world, out FixtureRotation rotation, out float width)
    {
        world = default; rotation = FixtureRotation.identity; width = 0;
        return ActiveCards != null && ActiveCards.TryDismissPose(card, out world, out rotation, out width);
    }
    internal int TransferCardToFlight(CardFxAnchor from, int card, CPlayerActor? actor, long generation)
    { TransferCount++; TransferredCard = card; OriginalVisible = false; return card; }
    internal int ObserveWithoutTransfer(CardFxAnchor from, int card, CPlayerActor? actor, long generation) => card;
    internal bool OwnsActiveFlight(int card, CPlayerActor? actor, long generation) => true;
    internal void BeginActiveFlight(int card, CPlayerActor? actor, long generation) { }
    internal void BeginFlightArrival(int seat, CPlayerActor? actor, long generation) { }
    internal bool OwnsFlightSlot(int seat, CPlayerActor? actor, long generation) => true;
    internal void SuppressBurnRecess(int seat) { }
}
internal sealed class FixtureTuning { internal float CardWidth => .0635f; }
internal sealed partial class RemoteCardFx
{
    internal sealed class Flight
    {
        internal FixtureObject? Go = new();
        internal bool FromDismissBerth, SourceTransferred, Active = true, Drawable;
        internal FixtureCard? FaceCard;
        internal CPlayerActor? SourceActor;
        internal CardDismissFlightOrigin? DismissOrigin;
        internal FixtureVector From, To;
        internal FixtureRotation Rotation = FixtureRotation.identity;
        internal float FromWidth, Arc;
        internal float ArtworkWait;
        internal int TransferredCardId = int.MinValue, ActiveCardId = int.MinValue;
        internal byte Endpoints;
        internal long Generation = 1;
    }
    private readonly RemoteAvatar _owner;
    private readonly System.Collections.Generic.List<PendingFlight> _pending = new();
    private const float ResolveSeconds = 2;
    private const float CardHeight = .088f, MinArcCardHeights = 1.5f, ArcFraction = .55f;
    private float DrawnBoardScale => _owner.BoardScale;
    internal RemoteCardFx(RemoteAvatar owner) { _owner = owner; }
    private static bool CanDrawFlight(Flight flight) => flight.Active && flight.Drawable;
    internal void Handover(Flight flight) => TransferVisibleFlight(flight);
    internal int PendingCount => _pending.Count;
    internal int Admissions;
    internal void Queue(byte endpoints, CardFlightSource source, CardDismissFlightOrigin origin, FixtureCard card)
    { _pending.Add(new PendingFlight(endpoints, 0, source, 1, origin, card, _owner.PlayerId, ScenarioRuleLibrary.ScenarioManager.Scenario)); }
    private bool TryPlay(byte endpoints, byte flags, CardFlightSource? source, long generation,
        CardDismissFlightOrigin? origin, FixtureCard? original, int player)
    {
        bool committed = source.HasValue && RemoteActiveDepartures.HasCommittedOriginal(
            RemoteBoardFocus.ActorById(source.Value.ActorId), original, NetCardFx.To(endpoints), source);
        if (committed) Admissions++;
        return committed;
    }
    internal static bool ArtworkExpired(Flight flight) => ArtworkWaitExpired(flight);
}
internal static class CardAppearanceProvenance
{
    internal static FixtureCard? Original;
    internal static FixtureCard? Resolve(int actor, ushort seat, ushort count) => Original;
}
internal static class NetAvatarDriver
{
    internal static RemoteAvatar? Controller;
    internal static bool TryGetCharacterDecisionOwner(CPlayerActor actor, out RemoteAvatar? controller)
    { controller = Controller; return controller != null; }
}
