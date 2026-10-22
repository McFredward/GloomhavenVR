using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One spawn author for the room. Net supplies current-opening provenance and elects
/// the VR placement surrogate when the native host has no VR presentation. Native continuation
/// never consults this bridge; it only prevents a private provisional pose becoming visible.</summary>
internal static class SharedWindowSpawnBridge
{
    internal delegate bool InitialPoseReader(SharedWindowKind kind, out Vector3 position,
                                            out Quaternion rotation, out float factor);

    internal static Func<bool>? InitialAuthor { get; set; }
    internal static InitialPoseReader? TryInitialPose { get; set; }

    internal static bool Handles(ConvertedPanel? panel) =>
        SharedWindowSize.UsesSharedWorldFrame(panel)
        && panel!.SharedDesignKind is SharedWindowKind.ScenarioStory or SharedWindowKind.MapStory
            or SharedWindowKind.QuestConfirm or SharedWindowKind.Encounter;

    /// <summary>An absent transport never grants a second local placement author.</summary>
    internal static bool IsInitialAuthor => InitialAuthor?.Invoke() == true;

    internal static bool TryReveal(ConvertedPanel panel)
    {
        if (!Handles(panel)) return true;
        if (panel.SharedSpawnPoseReady) return true;
        if (IsInitialAuthor)
        {
            panel.SharedSpawnPoseReady = true;
            return true;
        }
        if (TryInitialPose == null
            || !TryInitialPose(panel.SharedDesignKind, out Vector3 position,
                               out Quaternion rotation, out float factor)) return false;
        return ModalFallback.ApplySharedInitialPose(panel, position, rotation, factor);
    }
}
