using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// A source of pick rays/points for the game's picking pipeline (FROZEN Phase-2 API).
///
/// Phase-3a feeds this into its Harmony patches on
/// <c>MF.FindInteractableAtMousePosition</c> / <c>InputManager.CursorPosition</c>:
/// the patch asks the provider for the current pick and projects
/// <see cref="PickPose.HitPoint"/> through the game camera. The default provider is
/// the primary hand's <see cref="RayInteractor"/> (see <see cref="VRHands.PrimaryPick"/>);
/// Phase-3a may swap in a fingertip-touch provider when the hand is near the board.
/// </summary>
internal interface IPickProvider
{
    /// <summary>Current pick. Returns false when the provider cannot pick right now (hand untracked, disabled…).</summary>
    bool TryGetPick(out PickPose pick);
}

/// <summary>Result of a pick query (struct — no allocation).</summary>
internal struct PickPose
{
    /// <summary>Ray origin (world).</summary>
    public Vector3 Origin;

    /// <summary>Normalized ray direction (world).</summary>
    public Vector3 Direction;

    /// <summary>True when the ray hit a collider within range.</summary>
    public bool HasHit;

    /// <summary>Hit point (world). Only valid when <see cref="HasHit"/>.</summary>
    public Vector3 HitPoint;

    /// <summary>Hit distance (world units). Only valid when <see cref="HasHit"/>.</summary>
    public float HitDistance;

    /// <summary>Hit collider. Only valid when <see cref="HasHit"/>.</summary>
    public Collider? HitCollider;
}
