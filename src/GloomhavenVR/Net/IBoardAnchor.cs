using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Converts between world space and the SHARED cross-client reference frame in which avatar
/// poses travel. Default (<see cref="WorldAnchor"/>) is the identity: game world space, which
/// is already shared across all clients in Gloomhaven's server-authoritative model
/// (see <see cref="AvatarSerializer"/>). This seam exists so a future build can bind a
/// deterministic board-origin transform WITHOUT touching the wire format or the sampler.
///
/// NOTE: the epic brief pointed at <c>PlayTray._root</c> as the shared origin — that turned
/// out to be WRONG: PlayTray._root is the per-player cards tray, parented under each player's
/// own rig and re-homed to their head (TrayFollow / PlaceAtHead), so it is NOT identical
/// across clients. The correct shared frame is world space (or a real board transform).
/// </summary>
internal interface IBoardAnchor
{
    /// <summary>World pose → shared-frame pose.</summary>
    void ToAnchor(Vector3 worldPos, Quaternion worldRot, out Vector3 pos, out Quaternion rot);

    /// <summary>Shared-frame pose → world pose.</summary>
    void ToWorld(Vector3 pos, Quaternion rot, out Vector3 worldPos, out Quaternion worldRot);
}

/// <summary>Identity anchor: poses travel in game world space. The default and, for the
/// current title build, the correct choice.</summary>
internal sealed class WorldAnchor : IBoardAnchor
{
    public static readonly WorldAnchor Instance = new();

    public void ToAnchor(Vector3 worldPos, Quaternion worldRot, out Vector3 pos, out Quaternion rot)
    {
        pos = worldPos;
        rot = worldRot;
    }

    public void ToWorld(Vector3 pos, Quaternion rot, out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = pos;
        worldRot = rot;
    }
}
