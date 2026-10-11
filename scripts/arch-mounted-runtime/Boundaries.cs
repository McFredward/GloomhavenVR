using System.Collections.Generic;
using UnityEngine;

// Real Unity transforms, renderer bounds, mesh identity and particle modules are
// executed. Native gameplay marker components are explicit inert query boundaries.
internal sealed class ActorBehaviour : MonoBehaviour { }
internal sealed class CInteractableActor : MonoBehaviour { }
internal sealed class ProceduralWall : MonoBehaviour { }
internal sealed class ProceduralMapTile : MonoBehaviour { }

namespace GloomhavenVR.Core
{
    internal static partial class WallSegmentFade
    {
        private sealed partial class FadeDriver
        {
            private sealed class Live
            {
                internal readonly List<ArchRect> ArchRects = new List<ArchRect>();
            }
            private readonly Live _live = new Live();
            private readonly HashSet<Renderer> _mountedOwned = new HashSet<Renderer>();
            private sealed class MountedProp { internal Renderer Renderer = null!; internal bool Restored; }
            private static void RestoreProp(MountedProp prop) { prop.Restored = true; }
            internal void AddArch(float minX, float maxX, float minZ, float maxZ, float topY)
                => _live.ArchRects.Add(new ArchRect { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, TopY = topY, LastSeen = 0 });
            internal void ClearArches() => _live.ArchRects.Clear();
            internal bool Protect(Renderer renderer) => IsArchMountedEffect(renderer);
            internal bool Legacy(Renderer renderer) => IsArchProtected(new Bounds(renderer.transform.position, Vector3.zero), renderer.name);
        }
        internal static int Replay() => FadeDriver.Replay();
    }
}
