using System;
using GloomhavenVR.Net;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Board
    {
        internal readonly RemoteBoardRefreshGate Gate = new();
        internal object? Subject = new object();
        internal uint Presence;
        internal ulong Objectives = 10, Elements = 20, Track = 30, Model = 40;
        internal RemoteBoardRefreshSections Poll(float now) =>
            Gate.Poll(Presence, Objectives, Elements, Track, Model, Subject, now, .25f);
    }

    private static void Main()
    {
        var boards = new[] { new Board(), new Board(), new Board(), new Board() };
        foreach (Board board in boards)
        {
            Check(board.Poll(0) == RemoteBoardRefreshSections.All, "First render materializes every original section");
            Check(board.Poll(.001f) == RemoteBoardRefreshSections.None, "Settled output needs no content rebuild");
        }
        // Different remote owners must not consume each other's content/activation edges.
        for (int owner = 0; owner < boards.Length; owner++)
        {
            boards[owner].Presence++;
            for (int i = 0; i < boards.Length; i++)
                Check(boards[i].Poll(.01f + owner * .001f) == (i == owner
                    ? RemoteBoardRefreshSections.Actor : RemoteBoardRefreshSections.None),
                    "Owner extras must not refit unrelated native panels or other boards");
        }
        for (int owner = 0; owner < boards.Length; owner++)
        {
            boards[owner].Subject = new object();
            Check(boards[owner].Poll(.02f) == RemoteBoardRefreshSections.Actor,
                "Actor retarget is immediate even when its model revision is unchanged");
            boards[owner].Subject = null;
            Check(boards[owner].Poll(.021f) == RemoteBoardRefreshSections.Actor,
                "Actor loss clears actor content immediately");
            boards[owner].Model++;
            Check(boards[owner].Poll(.022f) == RemoteBoardRefreshSections.Actor,
                "Actorless model changes still refresh wire-fed counts and furniture");
        }
        // Source changes are shared inputs, but every board must observe the edge independently.
        foreach (Board board in boards) board.Objectives++;
        foreach (Board board in boards)
            Check(board.Poll(.03f) == RemoteBoardRefreshSections.Objectives,
                "Objective layout/row changes refresh every observer immediately");
        foreach (Board board in boards) board.Elements++;
        foreach (Board board in boards)
            Check(board.Poll(.04f) == RemoteBoardRefreshSections.Elements,
                "Element activation and fitted geometry do not wait for recovery");
        foreach (Board board in boards) board.Track++;
        foreach (Board board in boards)
            Check(board.Poll(.05f) == RemoteBoardRefreshSections.Track,
                "Initiative identity/art/activity changes do not refit the decision drawer");
        foreach (Board board in boards)
        {
            board.Objectives = board.Elements = board.Track = board.Model = 0;
            Check(board.Poll(.06f) == RemoteBoardRefreshSections.All, "Source/model disappearance is an immediate edge");
            board.Objectives = 10; board.Elements = 20; board.Track = 30; board.Model = 40;
            Check(board.Poll(.061f) == RemoteBoardRefreshSections.All, "Returning sources refresh in the same frame");
            board.Gate.Reset();
            Check(board.Poll(.062f) == RemoteBoardRefreshSections.All,
                "Style/tuning rebuild resets every section even if all inputs are identical");
        }

        foreach (Board board in boards)
        {
            RemoteBoardRefreshSections sections = board.Gate.Poll(board.Presence, board.Objectives,
                board.Elements, board.Track, board.Model, board.Subject, .063f, .25f,
                RemoteBoardRefreshSections.Elements);
            Check(sections == RemoteBoardRefreshSections.Elements,
                "A lost clone or first valid owner frame recovers immediately in its own section");
            Check(board.Poll(.064f) == RemoteBoardRefreshSections.None,
                "Successful clone recovery does not force repeated unchanged refreshes");
        }

        // Native row pooling may retarget an actor/avatar while hierarchy and art stay identical.
        object row = new object(), actor = new object(), avatar = new object();
        ulong TrackIdentity(object? entry, object? subject, object? portrait)
        {
            ulong revision = 123;
            RemoteBoardEntryIdentity.Mix(ref revision, entry, subject, portrait);
            return revision;
        }
        foreach (Board board in boards) { board.Track = TrackIdentity(row, actor, avatar); board.Poll(.07f); }
        actor = new object();
        foreach (Board board in boards)
        {
            board.Track = TrackIdentity(row, actor, avatar);
            Check(board.Poll(.071f) == RemoteBoardRefreshSections.Track,
                "Pooled native row actor retarget invalidates every observer's hover/order cache immediately");
            board.Track = TrackIdentity(row, actor, null);
            Check(board.Poll(.072f) == RemoteBoardRefreshSections.Track,
                "Removed native avatar clears its cached selection and focus bindings");
            board.Track = TrackIdentity(row, actor, new object());
            Check(board.Poll(.073f) == RemoteBoardRefreshSections.Track,
                "Replaced native avatar refreshes bindings even with identical art and geometry");
        }

        var readiness = new RemoteBoardRecoveryEdge[4];
        for (int i = 0; i < readiness.Length; i++)
        {
            Check(!readiness[i].Observe(true), "Initially successful native output needs no extra fit");
            Check(!readiness[i].Observe(false) && readiness[i].Pending,
                "Rejected native output records its hidden playback state");
            Check(!readiness[i].Observe(false), "Repeated absent output does not spin a ready-frame rebuild");
            Check(readiness[i].Observe(true) && !readiness[i].Pending,
                "Locally recovered native output retries validated fit/show without a new packet");
            Check(!readiness[i].Observe(true), "Settled successful playback does not repeat the recovery fit");
            readiness[i].Observe(false);
            Check(readiness[i].Observe(true), "A subsequent Apply recovery starts a new validated fit attempt");
            readiness[i].Observe(false); // TryMirror rejected the fit after a successful Apply.
            Check(readiness[i].Pending && readiness[i].Observe(true),
                "Rejected validated recovery remains pending for the next same-frame-state retry");
            readiness[i].Observe(false);
            readiness[i].Reset();
            Check(!readiness[i].Observe(true), "A successful full refresh consumes the pending recovery edge");
        }

        // Repeated extras/use-bar frames must neither broaden native refresh nor starve recovery.
        int nativeRefreshes = 0;
        foreach (Board board in boards) { board.Gate.Reset(); board.Poll(0); }
        for (int frame = 1; frame <= 270; frame++)
        {
            foreach (Board board in boards)
            {
                board.Presence++;
                RemoteBoardRefreshSections sections = board.Poll(frame / 90f);
                Check((sections & RemoteBoardRefreshSections.Actor) != 0,
                    "Every received actor/decision edge is consumed at native frame rate");
                if ((sections & ~RemoteBoardRefreshSections.Actor) != 0) nativeRefreshes++;
            }
        }
        Check(nativeRefreshes > 0 && nativeRefreshes <= 48,
            "Native recovery remains bounded and progresses despite continuous owner traffic");
        // Per-section clocks: repeated geometry changes in one original must not postpone others.
        var busy = new Board(); busy.Poll(0);
        for (int frame = 1; frame < 23; frame++)
        {
            busy.Objectives++;
            Check(busy.Poll(frame / 90f) == RemoteBoardRefreshSections.Objectives,
                "Every intermediate fitted objective size is consumed immediately");
        }
        busy.Objectives++;
        Check(busy.Poll(23f / 90f) == RemoteBoardRefreshSections.All,
            "Busy objective animation cannot starve other native or actor recovery");
        Console.WriteLine($"Board refresh production harness: {_assertions} assertions passed; " +
            $"four boards / 270 frames retain all 1080 actor updates with {nativeRefreshes} native recovery passes.");
    }
}
