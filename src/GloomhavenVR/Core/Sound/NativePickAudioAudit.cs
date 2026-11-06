using System;
using ClockStone;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Core;

/// <summary>Read-only Debug correlation for the unproven doubled forced-discard cue.
/// No request is suppressed, delayed, replayed or stopped. Only the synchronous original
/// OnLoseCardClick scope is measured; later asynchronous audio has no invented attribution.</summary>
internal static class NativePickAudioAudit
{
    internal sealed class Scope
    {
        internal readonly Scope? Previous;
        internal readonly int Id;
        internal readonly string Producer;
        internal int Requests;
        internal Scope(Scope? previous, int id, string producer) { Previous = previous; Id = id; Producer = producer; }
    }
    private static Scope? _current;
    private static int _transactions, _failures;
    internal static Scope? Begin(CardsHandUI hand)
    {
        Scope? previous = _current; _current = null;
        if (!VRSession.IsRunning || !VRLog.WantsDebug || _transactions >= 12) return previous;
        try
        {
            if (hand == null || hand.currentMode != CardHandMode.DiscardCard) return previous;
            _current = new Scope(previous, ++_transactions, hand.name);
            VRLog.Debug("Cards", $"PICK AUDIO transaction={_current.Id} begin original OnLoseCardClick: hand={hand.name}, actor={hand.PlayerActor?.ID ?? 0}, phase={PhaseManager.PhaseType}, selected={hand.SelectedCards?.Count ?? 0}.");
        }
        catch (Exception ex) { Failure(ex); }
        return previous;
    }
    internal static void End(Scope? previous, Exception? error)
    {
        Scope? completed = _current; _current = previous;
        if (completed == null || !VRLog.WantsDebug) return;
        try { VRLog.Debug("Cards", $"PICK AUDIO transaction={completed.Id} end original OnLoseCardClick: native-item-requests={completed.Requests}, exception={error?.GetType().Name ?? "none"}. Later asynchronous audio is not attributed to this scope."); }
        catch (Exception ex) { Failure(ex); }
    }
    internal static void Observe(AudioItem? item, AudioObject? source)
    {
        Scope? scope = _current;
        if (scope == null || !VRLog.WantsDebug) return;
        int ordinal = ++scope.Requests;
        if (ordinal > 8) return;
        try
        {
            var audio = source != null ? source.primaryAudioSource : null;
            VRLog.Debug("Cards", $"PICK AUDIO transaction={scope.Id} request={ordinal} producer={scope.Producer}/original OnLoseCardClick, item='{item?.Name}', native-object={(source != null ? source.GetInstanceID() : 0)}, loop={audio?.loop}, spatial={audio?.spatialBlend}, pan={audio?.panStereo}. Playback unchanged.");
        }
        catch (Exception ex) { Failure(ex); }
    }
    private static void Failure(Exception ex)
    {
        if (!VRLog.WantsDebug || _failures++ >= 3) return;
        try { VRLog.Debug("Cards", $"PICK AUDIO diagnostic unavailable: {ex.GetType().Name}. Original callbacks and playback continue unchanged."); }
        catch { /* Diagnostic I/O failure cannot alter the original callback or exception. */ }
    }
}
