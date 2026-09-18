using System;
using UnityEngine;

namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    internal struct Bounds
    {
        internal Vector3 center, size;
        internal bool Contains(Vector3 point) => Math.Abs(point.x - center.x) <= size.x / 2
            && Math.Abs(point.y - center.y) <= size.y / 2 && Math.Abs(point.z - center.z) <= size.z / 2;
    }
    internal sealed class GameObject { internal string name = ""; }
    internal sealed class Shader { internal string name = "Standard"; }
    internal sealed class Material { internal string name = "Wood"; internal Shader? shader = new(); }
    internal sealed class Renderer
    {
        internal GameObject gameObject = new();
        internal Material? sharedMaterial = new();
        internal Bounds bounds;
    }
}

namespace GloomhavenVR.Core
{
    internal static partial class MixedReality
    {
        internal static bool ClassifySky(Renderer renderer, Vector3 head, float sizeFloor) =>
            IsSkyRenderer(renderer, head, sizeFloor);
    }

    internal static class SkyCases
    {
        internal static void Run(Action<bool, string> check)
        {
            // Exact build-533 table bounds; no map driver or ready camera seat is required.
            var renderer = new Renderer { bounds = new Bounds {
                center = new Vector3(-69.16f, -93.42f, -.23f), size = new Vector3(366.91f, 186.72f, 444.78f) } };
            Vector3 coldHead = new(-69f, -20f, 0f);
            foreach (string name in new[] { "GH_Map_Table", "GH_Map_Table(Clone)", "GH_Map_TableTop_Lg",
                         "GH_Map_TableTop_Lg (2)", "GH_Map_Bench", "GH_Map_Barrel", "gh_map_table" })
            {
                renderer.gameObject.name = name;
                check(!MixedReality.ClassifySky(renderer, coldHead, 100f), "cold map furniture must never be sky");
                check(!MixedReality.ClassifySky(renderer, new Vector3(0, 50, 0), 100f), "seated map furniture stays visible");
            }
            foreach (string name in new[] { "GH_SkySphere", "Clouds (6)", "Map_Background" })
            {
                renderer.gameObject.name = name;
                check(MixedReality.ClassifySky(renderer, coldHead, 100f), "actual map sky still hides");
                check(MixedReality.ClassifySky(renderer, new Vector3(0, 50, 0), 100f), "named sky remains independent of head enclosure");
            }
            renderer.gameObject.name = "Unnamed";
            check(MixedReality.ClassifySky(renderer, coldHead, 100f), "anonymous enclosing sky still hides");
            check(!MixedReality.ClassifySky(renderer, coldHead, 200f), "small enclosing geometry remains visible");
            check(!MixedReality.ClassifySky(renderer, new Vector3(0, 50, 0), 100f), "non-enclosing geometry remains visible");
            renderer.bounds.size.y = 1f;
            check(!MixedReality.ClassifySky(renderer, renderer.bounds.center, 100f), "thin map surfaces are not domes");
            renderer.sharedMaterial!.shader!.name = "VFX/MapCloudShd";
            check(MixedReality.ClassifySky(renderer, coldHead, 100f), "native cloud shader still hides");
        }
    }
}
