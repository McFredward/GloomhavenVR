using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string scope, string value) { }
        internal static void Note(string scope, string value) { }
        internal static void Error(string scope, string value) { }
    }
}
namespace GloomhavenVR.Hands
{
    internal sealed class VRHand { internal string Side => "fixture"; }
}
namespace GloomhavenVR.Board
{
    // Hardware/controller input and UI admission are explicit fixture boundaries. Tile picking
    // below uses the same native Physics ray and the extracted production tile resolution.
    internal static class PointerBoundary { internal static bool IsPointerOverUI; }
    internal static partial class BoardPick
    {
        internal enum PickSource { None, Far, Near }
        private static PickSource _source = PickSource.Far;
        private static bool _hasHit;
        private static Collider? _hitCollider;
        private static Vector3 _origin, _direction;
        internal static int ActiveMask = 1 << 9;
        internal static bool InScenario = true;
        internal static PickSource Source => _source;
        internal static GloomhavenVR.Hands.VRHand? SourceHand => null;
        internal static Collider? HitCollider => _hitCollider;
        internal static void Ray(Vector3 origin, Vector3 direction, PickSource source = PickSource.Far)
        {
            _origin = origin; _direction = direction.normalized; _source = source;
            _hasHit = Physics.Raycast(_origin, _direction, out RaycastHit hit, 1000f, ActiveMask);
            _hitCollider = _hasHit ? hit.collider : null;
        }
        internal static bool TryGetGameRay(out Vector3 origin, out Vector3 direction, out float distance)
        { origin = _origin; direction = _direction; distance = 1000f; return _source != PickSource.None; }
        private static void EnsureFresh() { }
    }
}
