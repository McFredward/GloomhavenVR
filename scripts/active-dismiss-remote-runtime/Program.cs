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
        var owner = new RemoteAvatar();
        owner.Apply(new PresenceState { HasActiveDismissBerth = true, ActiveDismissListCount = 4, ActiveDismissSeat = 2 });
        var berth = new FixtureTransform { localPosition = new(.23f, -.22f, -.02f), localScale = FixtureVector.one * .83f,
            localRotation = FixtureRotation.Yaw(.7f) };
        Check(owner.ResolveDismissSeat(4, berth) == 2, "counted public seat stages the requested card");
        Check(owner.ResolveDismissSeat(3, berth) == -1, "active-list count mismatch cannot stage a different card");
        Check(owner.ResolveDismissSeat(4, null) == -1, "missing berth cannot stage a card");
        owner.Apply(default);
        Check(!owner.ActiveDismissBerthActive && !owner.ActiveDismissGhostShown && owner.ActiveDismissListCount == 0
            && owner.ActiveDismissSeat == -1 && owner.ResolveDismissSeat(4, berth) == -1,
            "absent state clears the previous remote reservation");
        owner.Apply(new PresenceState { HasActiveDismissBerth = true, ActiveDismissGhostShown = true,
            ActiveDismissListCount = 4, ActiveDismissSeat = 255 });
        Check(owner.ActiveDismissGhostShown && owner.ActiveDismissSeat == -1,
            "near preview has no staged public card");

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
