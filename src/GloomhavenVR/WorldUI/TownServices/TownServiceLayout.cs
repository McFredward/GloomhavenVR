using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Shared station and visitor poses, measured against both original rooms.
/// The opened filing banks reserve their complete travel; reading-side yaw cannot rotate
/// a validated forest gap into a tree or a cellar counter into its furniture.</summary>
internal static class TownServiceLayout
{
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
            radius = visitor == 1 ? 2.3f : visitor == 2 ? 2.4f : 3.1f;
            bearing = visitor == 1 ? 100f : visitor == 2 ? 220f : 270f;
            yaw = visitor == 1 ? 100f : visitor == 2 ? 220f : 300f;
        }
        else
        {
            if (service < 1 || service > 3) throw new ArgumentOutOfRangeException(nameof(service));
            radius = service == 3 ? 2.4f : 2.3f;
            bearing = service == 1 ? 15f : service == 2 ? 320f : 160f;
            yaw = service == 1 ? 15f : service == 2 ? 290f : 160f;
        }
        position = Quaternion.Euler(0f, bearing, 0f) * new Vector3(0f, 0f, radius);
    }
}
