using System;
using System.Collections.Generic;
using Vector3 = FixtureVector;
using Quaternion = FixtureRotation;

namespace GloomhavenVR.Net
{
    internal static class CardBurnCompletionHistory { internal const int CountMax = 128; }
    internal static class CardAppearanceMirror
    {
        internal static bool Presented;
        internal static bool HasPresentedThrough(int player, CPlayerActor? actor, FixtureCard? card, float clock) => Presented;
    }
    internal sealed partial class RemoteBurnFx
    {
        internal sealed class Burn
        {
            internal bool Active = true, HandoverLogged, FromDismissBerth, FromActive, OwnerReleased;
            internal FixtureCard? OriginalCard;
            internal int ActorId, Recess = -1, ClaimId, PresentationPlayer = 22;
            internal CardDismissFlightOrigin? DismissOrigin;
            internal Quaternion Rotation = Quaternion.identity;
            internal Vector3 From, To, ActiveLocal = default;
            internal float FromWidth, Arc, Elapsed, CompletionTime = 10;
            internal readonly FixtureObject Go = new();
            internal long FlightGeneration;
            internal string Name = "original";
        }
        private readonly RemoteAvatar _owner;
        private readonly Dictionary<FixtureCard, (CardFlightSource Source, CardDismissFlightOrigin Origin, bool Committed)> _dismissOrigins = new();
        private readonly List<FixtureCard> _pruneScratch = new();
        private readonly List<Burn> _burns = new();
        private const float HoldSeconds = 2;
        private float DrawnBoardScale => _owner.BoardScale;
        private Quaternion DrawnBoardRotation => Quaternion.identity;
        internal int ReleaseCalls;
        internal RemoteBurnFx(RemoteAvatar owner) { _owner = owner; }
        private bool OwnsDisplayedBoard(Burn burn) => _owner.Actor?.StableId == burn.ActorId;
        private bool TryAnchor(CardFxAnchor anchor, out Vector3 world)
        { world = anchor == CardFxAnchor.Burnt ? new(.5f, .3f, .1f) : new(-.7f, .3f, .1f); return true; }
        private void ReleaseClaim(int token) { ReleaseCalls++; }
        private static void LogHandover(Burn burn, float elapsed, string why) { }
        internal void Add(Burn burn) => _burns.Add(burn);
        internal bool HasOrigin(FixtureCard card) => _dismissOrigins.ContainsKey(card);
        internal void Prune() => PruneDismissOrigins();
        internal void Capture(Burn burn) => CaptureFlightPose(burn);
        internal bool TryHandover(Burn burn)
        {
            if (!MayReleaseOwnerBurn(burn, _owner.Actor, burn.OriginalCard)) return false;
            Handover(burn, "fixture native completion"); return true;
        }
    }
}
internal static class VRCard { internal const float FlyArcHeightFraction = .55f; }
namespace GloomhavenVR.Cards
{
    internal static class CardFlightLedger { internal static void Note(string owner, string target, string source, string name) { } }
}
