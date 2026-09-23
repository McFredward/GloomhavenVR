using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Shared station and visitor poses, measured against both original rooms.
/// Positions use the common room frame, so observers with different environments agree.</summary>
internal static class TownServiceLayout
{
    internal const float ResidentRadius = 2.3f;
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
            radius = visitor == 3 ? 2.7f : 2.55f;
            bearing = visitor == 1 ? -140f : visitor == 2 ? 150f : -40f;
            // The outer reservations turn slightly into the central aisle to clear
            // original cellar props and forest trunks without changing either room.
            yaw = visitor == 1 ? -140f : visitor == 2 ? 180f : 5f;
        }
        else
        {
            if (service < 1 || service > 3) throw new ArgumentOutOfRangeException(nameof(service));
            radius = service == 2 ? 2.2f : ResidentRadius;
            bearing = service == 1 ? 15f : service == 2 ? -85f : 90f;
            yaw = bearing;
        }
        position = Quaternion.Euler(0f, bearing, 0f) * new Vector3(0f, 0f, radius);
    }
}
