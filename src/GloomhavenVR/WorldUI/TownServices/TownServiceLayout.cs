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
            radius = visitor == 1 ? 2.4f : visitor == 3 ? 2.65f : 2.5f;
            bearing = visitor == 1 ? -140f : visitor == 2 ? -90f : -45f;
            // The outer reservations turn slightly into the central aisle to clear
            // original cellar props and forest trunks without changing either room.
            yaw = visitor == 1 ? -155f : visitor == 2 ? -90f : -75f;
        }
        else
        {
            if (service < 1 || service > 3) throw new ArgumentOutOfRangeException(nameof(service));
            radius = ResidentRadius;
            // The authored map reading side is -X (campaign hardware seat yaw90).
            // Positive-X semicircle puts residents left/front/right instead of placing
            // the priestess behind the player. This never depends on a viewer's head.
            bearing = service == 1 ? 15f : service == 2 ? 160f : 90f;
            yaw = service == 3 ? 95f : bearing;
        }
        position = Quaternion.Euler(0f, bearing, 0f) * new Vector3(0f, 0f, radius);
    }
}
