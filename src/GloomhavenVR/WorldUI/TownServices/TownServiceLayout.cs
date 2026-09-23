using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Shared station and visitor poses, measured against both original rooms.
/// Positions use the common room frame, so observers with different environments agree.</summary>
internal static class TownServiceLayout
{
    internal const float ResidentRadius = 4.8f;
    internal enum Environment { Open, Cellar, Forest }

    internal static Environment ForRoom(Transform? room) => room == null ? Environment.Open
        : room.Find("RoomGeo/Ground") != null ? Environment.Forest : Environment.Cellar;

    internal static Quaternion Frame(Transform? room, Transform? parchment) => room != null
        ? Quaternion.Euler(0f, room.eulerAngles.y, 0f)
        : parchment != null ? GloomhavenVR.Rig.VRRigDriver.YawOnly(parchment.rotation) : Quaternion.identity;

    internal static void Resolve(Environment environment, byte service, int visitor,
        out Vector3 position, out float yaw)
    {
        if (visitor < 0 || visitor > 3) throw new ArgumentOutOfRangeException(nameof(visitor));
        float radius, bearing;
        // Identical across cellar, forest and default/MR: peers can choose different rooms,
        // but published residents and independently resolved visitor counters share one town.
        if (visitor != 0)
        {
            radius = 5.8f;
            bearing = visitor == 1 ? 105f : visitor == 2 ? 255f : 180f;
            yaw = bearing;
        }
        else
        {
            if (service < 1 || service > 3) throw new ArgumentOutOfRangeException(nameof(service));
            radius = ResidentRadius;
            bearing = service == 1 ? 0f : service == 2 ? -60f : 60f;
            yaw = bearing;
        }
        position = Quaternion.Euler(0f, bearing, 0f) * new Vector3(0f, 0f, radius);
    }
}
