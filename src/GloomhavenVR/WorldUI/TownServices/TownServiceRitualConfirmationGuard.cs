using System;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Retain the native cancellation callback across its delayed confirm fade.
/// Scope exists only while a physical offering invokes its original selection handler;
/// ordinary flat prompts and unrelated confirmation boxes are never intercepted.</summary>
internal sealed class TownServiceRitualConfirmationGuard : IDisposable
{
    private static TownServiceRitualConfirmationGuard? _active;
    private readonly TownServiceRitualConfirmationGuard? _previous;
    private readonly UIEnhancementConfirmationBox _box;
    private readonly Func<bool> _valid;
    private bool _captured, _completed, _disposed;

    private TownServiceRitualConfirmationGuard(UIEnhancementConfirmationBox box, Func<bool> valid)
    {
        _box = box; _valid = valid; _previous = _active; _active = this;
    }

    internal static TownServiceRitualConfirmationGuard Begin(UIEnhancementConfirmationBox box, Func<bool> valid) => new(box, valid);

    internal static void Capture(UIEnhancementConfirmationBox box, ref Action onActionConfirmed, ref Action? onCancelled)
    {
        TownServiceRitualConfirmationGuard? scope = _active;
        if (scope == null || scope._disposed || scope._captured || !ReferenceEquals(scope._box, box)) return;
        scope._captured = true;
        Action confirm = onActionConfirmed;
        Action? cancel = onCancelled;
        onActionConfirmed = () => scope.Complete(true, confirm, cancel);
        onCancelled = () => scope.Complete(false, confirm, cancel);
    }

    private void Complete(bool requested, Action confirm, Action? cancel)
    {
        if (_completed) return;
        _completed = true;
        // A destroyed/recycled native source must fail closed while still running the
        // original cancellation cleanup. Validation itself never changes gameplay state.
        bool valid = false;
        if (requested)
        {
            try { valid = _box != null && _valid(); }
            catch (MissingReferenceException) { }
            catch (NullReferenceException) { }
        }
        if (valid) confirm(); else cancel?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(_active, this)) _active = _previous;
        // The captured delegates intentionally outlive this synchronous selection scope.
    }
}

[HarmonyPatch(typeof(UIEnhancementConfirmationBox), nameof(UIEnhancementConfirmationBox.ShowConfirmation),
    new[] { typeof(string), typeof(string), typeof(Sprite), typeof(string), typeof(Action), typeof(string), typeof(string), typeof(Action) })]
internal static class TownServiceRitualConfirmationCapture
{
    private static void Prefix(UIEnhancementConfirmationBox __instance, ref Action onActionConfirmed, ref Action? onCancelled) =>
        TownServiceRitualConfirmationGuard.Capture(__instance, ref onActionConfirmed, ref onCancelled);
}
