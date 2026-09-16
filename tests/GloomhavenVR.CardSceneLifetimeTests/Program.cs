using System;
using System.Collections;
using GloomhavenVR.Cards;

internal static class Program
{
    private static int checks;
    private static void Require(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    private sealed class Native : IEnumerator, IDisposable
    {
        internal Action Step = () => { };
        internal int Steps, Disposals, Resets;
        internal bool Running = true;
        internal readonly object Yield = new();
        public object Current => Yield;
        public bool MoveNext() { Steps++; Step(); return Running; }
        public void Dispose() => Disposals++;
        public void Reset() => Resets++;
    }

    private static void Main()
    {
        int releases = 0;
        var native = new Native();
        bool restoring = false;
        var wrapped = new NativeCardSceneLifetime(native, () => !restoring, () => releases++);
        Require(releases == 0 && native.Steps == 0, "Iterator construction must not release or advance native work");
        restoring = true;
        Require(wrapped.MoveNext(), "Data restore must retain the original native result");
        Require(releases == 0 && native.Steps == 1, "DataRestoring must be sampled on first execution");
        restoring = false;
        wrapped.MoveNext();
        Require(releases == 0, "A skipped no-op load must not enter on a later step");
        wrapped.Dispose();
        Require(native.Disposals == 1, "Native disposal must be forwarded");

        native = new Native();
        wrapped = new NativeCardSceneLifetime(native, () => true, () => releases++);
        wrapped.Dispose();
        Require(releases == 0 && native.Steps == 0, "Abandoned unstarted iterator must not release");

        // The native operation commits its destruction set when unloading begins. A face still
        // beneath the outgoing VR host at that instant is lost even if an OnDestroy callback
        // reparents it afterwards. This fixture tests the production wrapper's scheduling seam.
        bool borrowed = true, faceAlive = true;
        native = new Native { Step = () => { if (borrowed) faceAlive = false; } };
        wrapped = new NativeCardSceneLifetime(native, () => true, () => { borrowed = false; releases++; });
        Require(wrapped.MoveNext(), "Native yield must survive interception");
        Require(faceAlive && !borrowed, "Borrowed face must return before native destruction begins");
        Require(ReferenceEquals(wrapped.Current, native.Yield), "Native yielded object must remain identical");
        wrapped.MoveNext();
        Require(releases == 1 && native.Steps == 2, "Repeated native steps must not release twice or advance extra steps");
        native.Running = false;
        Require(!wrapped.MoveNext(), "Native completion must remain unchanged");
        wrapped.Reset();
        Require(native.Resets == 1, "Native reset contract must be forwarded");

        var error = new InvalidOperationException("native load failure");
        native = new Native { Step = () => throw error };
        wrapped = new NativeCardSceneLifetime(native, () => true, () => releases++);
        try { wrapped.MoveNext(); throw new Exception("Native failure was swallowed"); }
        catch (InvalidOperationException ex) { Require(ReferenceEquals(ex, error), "Original native exception must propagate unchanged"); }
        wrapped.Dispose();
        Require(native.Disposals == 1, "Failed load must still allow native cleanup");

        var modError = new InvalidOperationException("mod cleanup failure");
        native = new Native();
        wrapped = new NativeCardSceneLifetime(native, () => true,
            () => GloomhavenVR.Core.TickGuard.Run("Cards.SceneRelease", () => throw modError, "Cards"));
        Require(wrapped.MoveNext() && native.Steps == 1, "Guarded mod release failure must not cancel native loading");
        Require(ReferenceEquals(GloomhavenVR.Core.TickGuard.LastError, modError), "Guarded release error must be reported unchanged");
        wrapped.MoveNext();
        Require(GloomhavenVR.Core.TickGuard.Errors == 1 && native.Steps == 2, "Guarded release failure must not repeat for each native step");

        // Separate native operations have no shared latch. A canceled iterator that Unity does
        // not dispose cannot suppress another transition's release or poison later admission.
        var abandoned = new NativeCardSceneLifetime(new Native(), () => true, () => releases++);
        abandoned.MoveNext();
        int before = releases;
        native = new Native();
        wrapped = new NativeCardSceneLifetime(native, () => true, () => releases++);
        wrapped.MoveNext();
        Require(releases == before + 1, "Undisposed load must not block a later native operation");
        var nested = new NativeCardSceneLifetime(new Native(), () => true, () => releases++);
        before = releases;
        nested.MoveNext();
        wrapped.MoveNext();
        Require(releases == before + 1, "Nested operations must enter independently and outer release remains once");
        // Production factory methods: all native descendants return intact; wrapper identity
        // remains in selected-field arrays throughout an aborted same-scene transition.
        var factory = new VRCardFactory();
        var first = new AbilityCardUI();
        var second = new AbilityCardUI();
        var selected = new[] { factory.GetOrCreate(first), factory.GetOrCreate(second) };
        factory.ReturnBorrowedFacesBeforeSceneLoad();
        Require(ReferenceEquals(selected[0].GameCard, first) && ReferenceEquals(selected[1].GameCard, second),
            "Scene release must retain selected wrapper game identity");
        Require(ReferenceEquals(first.Face.Parent, first.Owner) && ReferenceEquals(second.Face.Parent, second.Owner),
            "Every borrowed native hierarchy must return to its owner");
        Require(!selected[0].HasAdoptedFace && !selected[1].HasAdoptedFace, "Scene release must retire both adoptions");
        Require(selected[0].Adoptions == 1 && selected[1].Adoptions == 1, "Release must not reborrow during loading");
        CardsDriver.NativeSceneLoadInProgress = true;
        factory.GetOrCreate(first);
        Require(selected[0].Adoptions == 1, "Factory lookup during load must not re-adopt a returned face");
        var lateWidget = new AbilityCardUI();
        var lateWrapper = factory.GetOrCreate(lateWidget);
        Require(lateWrapper.Adoptions == 0, "New factory widget during load must not acquire a native face");
        factory.ReattachBorrowedFacesAfterSceneLoad();
        Require(selected[0].Adoptions == 1 && selected[1].Adoptions == 1, "Recovery must not reborrow while native loading is active");
        CardsDriver.NativeSceneLoadInProgress = false;
        selected[1].AttachmentUnavailable = true;
        Require(!factory.ReattachBorrowedFacesAfterSceneLoad(), "Unavailable retained face must leave recovery pending");
        Require(ReferenceEquals(selected[1].GameCard, second), "Unavailable face must retain its selected identity for retry");
        selected[1].AttachmentUnavailable = false;
        Require(factory.ReattachBorrowedFacesAfterSceneLoad(), "Ready retained face must complete pending recovery");
        Require(selected[1].HasAdoptedFace && selected[1].Adoptions == 2,
            "Recovery must reattach confirmed selected slots even without a GetOrCreate lookup");
        factory.ReattachBorrowedFacesAfterSceneLoad();
        Require(selected[1].Adoptions == 2, "Repeated recovery must not duplicate native adoption");
        Require(ReferenceEquals(factory.GetOrCreate(first), selected[0]), "Aborted load must reuse selected wrapper identity");
        Require(selected[0].HasAdoptedFace && selected[0].Adoptions == 2, "Aborted load rebuild must reattach exactly once");
        factory.GetOrCreate(first);
        Require(selected[0].Adoptions == 2, "Subsequent lookups must not duplicate adoption");
        factory.ReturnBorrowedFacesBeforeSceneLoad();
        factory.ReturnBorrowedFacesBeforeSceneLoad();
        selected[0].Host.Destroy(); selected[1].Host.Destroy();
        Require(first.Face.Alive && first.Mask.Alive && first.Selectable.Alive
            && second.Face.Alive && second.Mask.Alive && second.Selectable.Alive,
            "Native FullAbilityCard and mask/button descendants must survive outgoing VR hosts");
        Console.WriteLine($"Card scene lifetime: {checks} assertions passed.");
    }
}
