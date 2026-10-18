using System;
using System.Threading.Tasks;
namespace GloomhavenVR.Core.SteamLink;

// Test-only scheduler boundary inserted into a private copy of RunAsync AFTER
// its terminal phase publication, without changing any production instruction.
// Holding the actual worker here makes the legal CI preemption window exact.
internal static class ProjectionProbeFixtureCompletion
{
    private static object? _owner;
    private static TaskCompletionSource<bool> _published=NewSignal(),_resume=NewSignal();
    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static Task Published => _published.Task;
    internal static void Hold(object owner) { _owner=owner;_published=NewSignal();_resume=NewSignal(); }
    internal static Task PauseAfterPhaseAsync(object owner)
    {
        if(!ReferenceEquals(_owner,owner))return Task.CompletedTask;
        _published.TrySetResult(true);return _resume.Task;
    }
    internal static void Resume() { _owner=null;_resume.TrySetResult(true); }
}
