using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Room-relative station and visitor poses, measured against the original scenery.
/// The opened filing banks reserve their complete travel; reading-side yaw cannot rotate
/// a validated forest gap into a tree or a cellar counter into its furniture.</summary>
internal static class TownServiceLayout
{
    internal enum Environment { Open, Cellar, Forest }

    internal static Environment ForRoom(Transform? room) => room == null ? Environment.Open
        : room.Find("RoomGeo/Ground") != null ? Environment.Forest : Environment.Cellar;

    internal static Quaternion Frame(Transform? room, float readingYaw) =>
        Quaternion.Euler(0f, room != null ? room.eulerAngles.y : readingYaw, 0f);

    internal static void Resolve(Environment environment, byte service, int visitor,
        out Vector3 position, out float yaw)
    {
        if (visitor < 0 || visitor > 3) throw new ArgumentOutOfRangeException(nameof(visitor));
        bool forest = environment == Environment.Forest;
        float radius;
        if (visitor != 0)
        {
            if (forest)
            {
                radius = visitor == 1 ? 3.1f : visitor == 2 ? 2.5f : 2.7f;
                yaw = visitor == 1 ? 160f : visitor == 2 ? 340f : 270f;
            }
            else
            {
                radius = visitor == 3 ? 2.7f : 2.5f;
                yaw = visitor == 1 ? 0f : visitor == 2 ? 120f : 190f;
            }
        }
        else
        {
            if (service < 1 || service > 3) throw new ArgumentOutOfRangeException(nameof(service));
            if (forest)
            {
                radius = service == 1 ? 2.3f : service == 2 ? 2.9f : 1.9f;
                yaw = service == 1 ? 90f : service == 2 ? 30f : 210f;
            }
            else
            {
                radius = service == 1 ? 2.7f : 2.9f;
                yaw = service == 1 ? 260f : service == 2 ? 70f : 310f;
            }
        }
        position = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, radius);
    }
}
