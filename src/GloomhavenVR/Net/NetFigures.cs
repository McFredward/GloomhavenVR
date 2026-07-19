// FILLED BY WORKER C — do not add logic here.
// Foundation stub: exact signatures only, all bodies are strict no-ops so the driver +
// LocalRigSampler compile and run as no-ops until figure-grab sync lands.

using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Figure-pickup sync (cosmetic): send side samples the figure the local player physically
/// holds; receive side drives a remotely-held figure's transform toward the synced pose and
/// restores authoritative control on release. STUB — worker C fills the bodies.
/// </summary>
internal static class NetFigures
{
    /// <summary>Send side: the figure the local player is holding this frame (id + world pose).
    /// Stub returns false (nothing held).</summary>
    public static bool TrySampleHeld(out int actorId, out Vector3 pos, out Quaternion rot)
    {
        actorId = 0;
        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    /// <summary>Receive side: a remote player is holding figure <paramref name="actorId"/> at the
    /// given world pose. Stub no-op.</summary>
    public static void ApplyRemoteHeld(int playerId, int actorId, Vector3 pos, Quaternion rot) { }

    /// <summary>Receive side: the remote player released whatever they held. Stub no-op.</summary>
    public static void ReleaseRemote(int playerId) { }

    /// <summary>Per-frame drive of remotely-held figures. Stub no-op.</summary>
    public static void Tick() { }
}
