using System;
using GloomhavenVR.Net;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(FixtureVector a, FixtureVector b) => (a - b).sqrMagnitude < 1e-7f;
    private static int Main()
    {
        var owner = new RemoteAvatar { ItemUseCapLabel = "Dismiss summon" };
        var actor = new CPlayerActor { StableId = 101 };
        var otherActor = new CPlayerActor { StableId = 202 };
        owner.Apply(new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 101, ActiveDismissListCount = 4, ActiveDismissSeat = 2 });
        var berth = new FixtureTransform { localPosition = new(.23f, -.22f, -.02f), localScale = FixtureVector.one * .83f,
            localRotation = FixtureRotation.Yaw(.7f) };
        Check(owner.ResolveDismissSeat(actor, 4, berth) == 2, "counted public seat stages the requested card");
        Check(owner.ResolveDismissSeat(actor, 3, berth) == -1, "active-list count mismatch cannot stage a different card");
        Check(owner.ResolveDismissSeat(actor, 4, null) == -1, "missing berth cannot stage a card");
        Check(owner.ResolveDismissSeat(otherActor, 4, berth) == -1,
            "same-count different actor cannot stage a card");
        Check(owner.ResolveDismissSeat(null, 4, berth) == -1,
            "missing displayed actor cannot inherit a dismissal");
        var matchingPicture = RemoteFurniture.Picture(owner, actor, true, 12);
        Check(matchingPicture.Berth && matchingPicture.Armed && matchingPicture.Caption == "Dismiss summon",
            "matching exact actor retains the original dismissal presentation");
        var wrongPicture = RemoteFurniture.Picture(owner, otherActor, true, 12);
        Check(!wrongPicture.Berth && !wrongPicture.Ghost && !wrongPicture.Armed && wrongPicture.Caption == null,
            "wrong actor hides its dismissal berth, preview, cap and caption");
        owner.Apply(default);
        Check(!owner.ActiveDismissBerthActive && !owner.ActiveDismissGhostShown && owner.ActiveDismissActorId == 0
            && owner.ActiveDismissListCount == 0
            && owner.ActiveDismissSeat == -1 && owner.ResolveDismissSeat(actor, 4, berth) == -1,
            "absent state clears the previous remote reservation");
        owner.ItemCardCount = 2;
        owner.ItemUseCapLabel = "Use";
        var ordinary = RemoteFurniture.Picture(owner, null, false, 0);
        Check(ordinary.Berth && ordinary.Armed && ordinary.Caption == "Use" && !ordinary.Ghost,
            "ordinary item fallback remains unchanged without a dismissal record");
        owner.Apply(new PresenceState { HasActiveDismissBerth = true, ActiveDismissGhostShown = true,
            ActiveDismissActorId = 101, ActiveDismissListCount = 4, ActiveDismissSeat = 255 });
        Check(owner.ActiveDismissGhostShown && owner.ActiveDismissSeat == -1,
            "near preview has no staged public card");
        Check(RemoteFurniture.Picture(owner, actor, true, 12).Ghost,
            "matching actor shows its original held-card preview");
        Check(!RemoteFurniture.Picture(owner, otherActor, true, 12).Ghost,
            "wrong actor cannot show the held-card landing preview");
        owner.Apply(new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 0,
            ActiveDismissListCount = 4, ActiveDismissSeat = 2 });
        Check(owner.ResolveDismissSeat(new CPlayerActor(), 4, berth) == -1,
            "zero provenance never matches an unresolved actor");

        // A card can already be at the destination POSITION while still needing rotation/scale
        // correction. The parent's changed frame, rather than a nonzero translation, owns that glide.
        var coincidentGrid = new FixtureTransform { localScale = FixtureVector.one * 1.4f };
        var coincidentBerth = new FixtureTransform { localScale = FixtureVector.one * .83f };
        var coincidentPanel = new RemoteBoardCard(coincidentGrid, FixtureVector.zero);
        coincidentPanel.SetPresentationParent(coincidentBerth);
        coincidentPanel.Move(FixtureVector.zero, false, 8, 1.04f * .94f);
        for (int i = 0; i < 500; i++) coincidentPanel.TickGlide();
        Check(Math.Abs(coincidentPanel.PresentationTransform.localScale.x - 1.04f * .94f) < 1e-5f,
            "zero-distance reparent still settles its changed frame scale");

        var grid = new FixtureTransform { localPosition = new(1, 2, 3), localScale = FixtureVector.one * 1.4f,
            localRotation = FixtureRotation.Yaw(-.4f) };
        var panel = new RemoteBoardCard(grid, new(.1f, .05f, -.001f));
        FixtureTransform slab = panel.PresentationTransform;
        FixtureVector world = slab.position; FixtureRotation facing = slab.rotation; float width = slab.Scale;
        panel.SetPresentationParent(berth);
        Check(ReferenceEquals(slab, panel.PresentationTransform) && ReferenceEquals(slab.parent, berth),
            "the original slab moves instead of leaving an active-grid copy");
        Check(Near(slab.position, world) && FixtureRotation.Angle(slab.rotation, facing) < .1f && Math.Abs(slab.Scale - width) < 1e-5f,
            "reparent preserves the complete current world pose before settling");
        const float fit = 1.04f * .94f;
        panel.Move(FixtureVector.zero, false, 8, fit);
        panel.TickGlide();
        Check((slab.localPosition - FixtureVector.zero).sqrMagnitude > 1e-6f
            && (slab.position - world).sqrMagnitude > 1e-6f,
            "first frame advances the original slab without snapping to the berth");
        for (int i = 0; i < 500; i++) panel.TickGlide();
        Check(Near(slab.localPosition, FixtureVector.zero) && Math.Abs(slab.localScale.x - fit) < 1e-5f,
            "ability slab settles to the exact owner-authored berth fit");
        Check(FixtureRotation.Angle(slab.localRotation, FixtureRotation.identity) < .1f,
            "staged card lies in the berth plane");
        world = slab.position;
        panel.SetPresentationParent(grid);
        Check(Near(slab.position, world), "taking a staged card back preserves its departure pose");
        FixtureVector cell = new(.1f, .05f, -.001f);
        panel.Move(cell, false, 8);
        for (int i = 0; i < 500; i++) panel.TickGlide();
        Check(ReferenceEquals(slab, panel.PresentationTransform) && Near(slab.localPosition, cell)
            && Near(slab.localScale, FixtureVector.one), "cancellation returns the same slab at original active-grid scale");
        panel.SetPresentationParent(berth);
        panel.Move(FixtureVector.zero, true, 8, fit);
        Check(Near(slab.localScale, FixtureVector.one * fit), "freshly seeded stage uses the same fit as animated arrival");
        Console.WriteLine("PASS: " + assertions + " production remote-dismissal assertions; explicit portable transform boundary.");
        return 0;
    }
}
