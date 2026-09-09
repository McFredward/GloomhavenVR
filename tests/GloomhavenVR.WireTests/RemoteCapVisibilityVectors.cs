using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class RemoteCapVisibilityVectors
{
    internal static void Run(Harness t)
    {
        t.Case("remote cap: other button edges cannot restart an in-flight hide");
        var cap = new RemoteCapVisibility(true);
        t.True(cap.Change(false), "first hidden request starts dissolve");
        for (int edge = 0; edge < 32; edge++)
            t.True(!cap.Change(false), "repeated hide is not a new transition even while object remains active");
        t.True(cap.Change(true), "reopen reverses the logical hide before its object deactivates");
        t.True(!cap.Change(true), "repeated visible request cannot restart appearance");
        t.True(cap.Change(false), "subsequent real hide remains observable");
        t.True(cap.Change(true), "hide and reopen can occur in the same rendered frame");

        t.Case("remote cap: independent roles retain independent visibility");
        var confirm = new RemoteCapVisibility(true);
        var skip = new RemoteCapVisibility(false);
        t.True(!confirm.Change(true), "unchanged confirm stays settled");
        t.True(skip.Change(true), "skip appears independently");
        t.True(skip.Change(false), "skip starts disappearing");
        t.True(confirm.Change(false), "confirm edge while skip dissolves is independent");
        t.True(!skip.Change(false), "confirm edge does not resample skip's shrinking geometry");
    }
}
