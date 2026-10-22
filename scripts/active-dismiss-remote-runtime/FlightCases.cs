using System;
using GloomhavenVR.Net;

internal static class FlightCases
{
    private static bool Near(FixtureVector a, FixtureVector b) => (a - b).sqrMagnitude < 1e-7f;
    internal static void Run(Action<bool, string> check)
    {
        var actor = new CPlayerActor { StableId = 101 };
        var foreign = new CPlayerActor { StableId = 202 };
        RemoteBoardFocus.Actors[101] = actor;
        RemoteBoardFocus.Actors[202] = foreign;
        var original = new FixtureCard { CardInstanceID = 51 };
        var replacement = new FixtureCard { CardInstanceID = 52 };
        var source = new CardFlightSource(101, 2, 4);
        actor.CharacterClass.ActivatedCards.Add(original);
        check(!RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Discard, source),
            "discard flight waits for its own native destination");
        actor.CharacterClass.DiscardedAbilityCards.Add(original);
        check(!RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Discard, source),
            "active membership must end before a committed flight");
        actor.CharacterClass.ActivatedCards.Clear();
        check(RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Discard, source),
            "immutable original remains resolvable after old active count and seat disappear");
        check(!RemoteActiveDepartures.HasCommittedOriginal(actor, replacement, CardFxAnchor.Discard, source),
            "native destination membership cannot name a different original");
        foreign.CharacterClass.DiscardedAbilityCards.Add(original);
        check(!RemoteActiveDepartures.HasCommittedOriginal(foreign, original, CardFxAnchor.Discard, source),
            "foreign actor cannot authorize a committed dismissal");
        check(!RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Discard, new(101, 4, 4))
            && !RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Discard, new(101, 0, 0)),
            "invalid old seat and missing count never authorize an original");
        check(!RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Burnt, source),
            "discard membership does not authorize a burn destination");
        actor.CharacterClass.PermanentlyLostAbilityCards.Add(original);
        check(RemoteActiveDepartures.HasCommittedOriginal(actor, original, CardFxAnchor.Burnt, source),
            "permanently lost native destination supports the same original dismissal");

        var berth = new FixtureTransform { localPosition = new(.7f, .3f, -.1f), localScale = FixtureVector.one * 1.7f,
            localRotation = FixtureRotation.Yaw(.7f) };
        var owner = new RemoteAvatar { Actor = actor, ItemUseRecess = berth, BoardScale = 1.7f };
        var capture = new RemoteActiveCards(owner);
        owner.ActiveCards = capture;
        var stage = new RemoteBoardCard(berth, new(.023f, -.019f, .011f));
        stage.PresentationTransform.localRotation = FixtureRotation.Yaw(.2f);
        stage.PresentationTransform.localScale = FixtureVector.one * .79f;
        capture.Stage(stage, original);
        var origin = new CardDismissFlightOrigin(new(new(.017f, -.012f, .009f), FixtureRotation.Yaw(-.12f)), .051f);
        check(RemoteActiveDepartures.TryDismissPose(owner, original, origin, out var at, out var facing, out var width)
            && Near(at, stage.PresentationTransform.position)
            && FixtureRotation.Angle(facing, stage.PresentationTransform.rotation) < .1f
            && Math.Abs(width - .0635f * .79f * 1.7f) < 1e-6f,
            "live staged original retains actual intermediate position orientation and width");
        check(!capture.TryDismissPose(replacement, out _, out _, out _),
            "staged capture cannot borrow another original card");
        capture.SetPopulation(replacement, original);
        owner.PendingDismiss.Add(51);
        owner.Apply(default);
        check(capture.PendingSeat(actor, berth) == 1,
            "accepted pending flight retains its original staged card after118 clears");
        stage.Move(FixtureVector.zero, false, 8, .9776f);
        FixtureVector frozen = stage.PresentationTransform.position;
        capture.Retain();
        berth.Active = false;
        check(stage.PresentationTransform.ActiveInHierarchy && Near(stage.PresentationTransform.position, frozen),
            "accepted original survives hidden berth without changing its frozen world pose");
        check(capture.RefreshRetained(original, stage, actor)
            && stage.PresentationTransform.ActiveInHierarchy && ReferenceEquals(stage.PresentationTransform.parent, owner.StableBoard),
            "native active refresh preserves the frozen original before ordinary parent and glide writes");
        for (int i = 0; i < 80; i++) stage.TickGlide();
        check(Near(stage.PresentationTransform.position, frozen),
            "frozen original cannot resume its obsolete parent home glide");
        berth.Active = true;
        capture.Stage(null, null);
        capture.SetPopulation();
        check(capture.Retains(original) && capture.TryDismissPose(original, out var retainedAt, out _, out _)
            && Near(retainedAt, stage.PresentationTransform.position) && !stage.BlankCalled,
            "native retirement retains the actual staged renderer while flight artwork loads");
        owner.PendingDismiss.Clear();
        capture.Retain();
        check(!capture.Retains(original) && stage.BlankCalled,
            "completed or expired handover releases its retained original panel");
        capture.Stage(stage, original);
        stage.PresentationTransform.Active = true;
        check(capture.PendingSeat(actor, berth) == -1,
            "ordinary cancellation does not retain a former berth reservation");
        var flight = new RemoteCardFx.Flight { FaceCard = original, SourceActor = actor, FromDismissBerth = true,
            DismissOrigin = origin, Endpoints = NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Discard), To = new(1, 2, 3) };
        var fx = new RemoteCardFx(owner);
        fx.Handover(flight);
        check(!flight.SourceTransferred && owner.OriginalVisible && owner.TransferCount == 0,
            "artwork loading keeps original stage visible before flight handover");
        stage.PresentationTransform.localPosition = new(.012f, -.008f, .004f);
        at = stage.PresentationTransform.position;
        flight.Drawable = true;
        fx.Handover(flight);
        check(flight.SourceTransferred && !owner.OriginalVisible && owner.TransferredCard == 51 && owner.TransferCount == 1,
            "first drawable flight transfers the exact original once");
        check(Near(flight.From, at) && Near(flight.Go!.transform.position, at),
            "first drawable flight starts at last visible original pose after artwork wait");
        stage.PresentationTransform.localPosition = new(.2f, -.3f, .1f);
        fx.Handover(flight);
        check(Near(flight.From, at), "handover never recaptures after the original renderer was transferred");
        var secondBoard = new RemoteAvatar { Actor = foreign, ItemUseRecess = berth };
        var secondFx = new RemoteCardFx(secondBoard);
        flight.SourceTransferred = false;
        secondFx.Handover(flight);
        check(!flight.SourceTransferred && secondBoard.TransferCount == 0,
            "foreign focus cannot transfer a same-index original on another board");

        capture.Stage(null, null);
        check(RemoteActiveDepartures.TryDismissPose(owner, original, origin, out at, out facing, out width)
            && Near(at, berth.TransformPoint(origin.BerthPose.Position)),
            "missing stage retains exact owner intermediate position");
        check(Math.Abs(width - .051f * 1.7f) < 1e-6f,
            "missing stage retains exact owner intermediate width");
        check(FixtureRotation.Angle(facing, berth.rotation * origin.BerthPose.Rotation) < .1f,
            "missing stage retains exact owner intermediate orientation");
        check(!RemoteActiveDepartures.TryDismissPose(owner, original, null, out _, out _, out _),
            "missing explicit origin cannot fall back to invented berth geometry");

        CardAppearanceProvenance.Original = original;
        NetAvatarDriver.Controller = owner;
        actor.CharacterClass.DiscardedAbilityCards.Clear();
        actor.CharacterClass.PermanentlyLostAbilityCards.Clear();
        actor.CharacterClass.ActivatedCards.Add(original);
        var delayed = new RemoteCardFx(owner);
        delayed.Queue(NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Discard), source, origin, original);
        FixtureClock.unscaledTime = 5;
        delayed.Tick(.1f);
        check(delayed.PendingCount == 1 && delayed.Admissions == 0,
            "accepted dismissal survives native model delay longer than two seconds");
        actor.CharacterClass.ActivatedCards.Clear();
        actor.CharacterClass.DiscardedAbilityCards.Add(original);
        delayed.Tick(.1f);
        check(delayed.PendingCount == 0 && delayed.Admissions == 1,
            "delayed native commit admits the accepted original flight once");
        delayed.Tick(.1f);
        check(delayed.Admissions == 1, "repeated native and origin snapshots cannot re-admit a consumed flight");
        delayed.Queue(NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Discard), source, origin, original);
        actor.CharacterClass.HandAbilityCards.Add(original);
        delayed.Tick(.1f);
        check(delayed.PendingCount == 0 && delayed.Admissions == 1,
            "real recovery retires the pending original");
        actor.CharacterClass.HandAbilityCards.Clear();
        actor.CharacterClass.DiscardedAbilityCards.Clear();
        actor.CharacterClass.ActivatedCards.Add(original);
        delayed.Queue(NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Discard), source, origin, original);
        ScenarioRuleLibrary.ScenarioManager.Scenario = new();
        delayed.Tick(.1f);
        check(delayed.PendingCount == 0, "scenario replacement retires the pending original");
        delayed.Queue(NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Discard), source, origin, original);
        NetAvatarDriver.Controller = new RemoteAvatar { PlayerId = 77 };
        delayed.Tick(.1f);
        check(delayed.PendingCount == 0, "actual character controller change retires the pending original");
        NetAvatarDriver.Controller = owner;
        var artWait = new RemoteCardFx.Flight { FromDismissBerth = true, ArtworkWait = 5 };
        check(!RemoteCardFx.ArtworkExpired(artWait), "accepted dismissal keeps waiting for its exact public artwork");
        artWait.FromDismissBerth = false;
        check(RemoteCardFx.ArtworkExpired(artWait), "unmarked legacy artwork waits retain their original bounded expiry");

        actor.CharacterClass.DiscardedAbilityCards.Clear();
        actor.CharacterClass.ActivatedCards.Clear();
        actor.CharacterClass.PermanentlyLostAbilityCards.Add(original);

        var burn = new RemoteBurnFx(owner);
        var held = new RemoteBurnFx.Burn { OriginalCard = original, ActorId = 101, Elapsed = 6, ClaimId = 91 };
        burn.Add(held);
        byte burnEndpoints = NetCardFx.Pair(CardFxAnchor.Active, CardFxAnchor.Burnt);
        burn.PrepareDismissRelease(burnEndpoints, source, original, origin);
        check(held.FromDismissBerth && held.FromActive && ReferenceEquals(held.DismissOrigin, origin),
            "burn release upgrades the same original hold with its exact origin");
        burn.Capture(held);
        check(Near(held.From, berth.TransformPoint(origin.BerthPose.Position)) && Math.Abs(held.FromWidth - .051f) < 1e-6f,
            "missed stage burn uses exact original pose and width at native hold");
        CardAppearanceMirror.Presented = true;
        check(!burn.TryHandover(held), "native artwork alone cannot authorize a remote burn flight");
        held.OwnerReleased = true;
        CardAppearanceMirror.Presented = false;
        check(!burn.TryHandover(held), "owner release waits for the exact native completion artwork");
        CardAppearanceMirror.Presented = true;
        check(burn.TryHandover(held) && held.HandoverLogged && held.Elapsed == 2 && burn.ReleaseCalls == 1,
            "native completion and owner release start one original burn flight with a fresh arc clock");
        long generation = held.FlightGeneration;
        burn.TryHandover(held);
        check(burn.ReleaseCalls == 1 && held.FlightGeneration == generation,
            "repeated release cannot restart the same burn flight");
        actor.CharacterClass.PermanentlyLostAbilityCards.Clear();
        actor.CharacterClass.ActivatedCards.Add(original);
        burn.Prune();
        check(!burn.HasOrigin(original), "recovery retires dismissal origin before a later ordinary burn");
    }
}
