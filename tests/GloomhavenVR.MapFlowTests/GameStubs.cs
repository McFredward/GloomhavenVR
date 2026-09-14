using System;
using System.Collections.Generic;

// This harness provides lifecycle and hierarchy inputs only. The linked production
// helpers/policies make the decisions; no stub implements their policy.
namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal string name = "native-window";
        public static bool operator ==(Object? a, Object? b) =>
            (ReferenceEquals(a, null) || a.Destroyed)
                ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    internal sealed class Transform : Object
    {
        internal Transform? parent;
        internal UnityEngine.UI.UIWindow? window;
        internal T? GetComponent<T>() where T : Object => window as T;
        internal bool IsChildOf(Transform other)
        {
            for (Transform? at = this; at != null; at = at.parent)
                if (ReferenceEquals(at, other)) return true;
            return false;
        }
    }
    internal sealed class GameObject : Object
    {
        internal bool activeInHierarchy = true;
    }
}
namespace UnityEngine.UI
{
    internal enum UIWindowID { None, PartyPanel }
    internal sealed class UIWindow : UnityEngine.Object
    {
        internal bool IsOpen;
        internal UIWindowID ID;
        internal readonly UnityEngine.Transform transform;
        internal UIWindow() { transform = new UnityEngine.Transform { window = this }; }
    }
}
internal static class Singleton<T> where T : class
{
    internal static bool IsInitialized;
    internal static T Instance = null!;
}
internal sealed class UILoadoutManager : UnityEngine.Object
{
    internal bool IsOpen;
}
internal sealed class NewPartyDisplayUI : UnityEngine.Object
{
    internal static NewPartyDisplayUI? PartyDisplay;
    internal UnityEngine.UI.UIWindow? window;
    internal readonly HashSet<UnityEngine.Object> hideRequests = new();
}
internal sealed class AdventureMapUIManager : UnityEngine.Object
{
    // Matches the native manager's request-set contract, independent of visual alpha.
    internal readonly HashSet<object> lockRequests = new();
    internal bool IsLocked => lockRequests.Count > 0;
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
}
namespace GloomhavenVR.WorldUI
{
    internal static partial class StoryComposite
    {
        private const string Scope = "StoryComposite";
        private static bool _loadoutCurtainOwnerNoted;
        internal static int Withheld => CurtainWithheldNow();
        internal static bool _curtainStanding;
        internal static bool _curtainLifted;
        internal static readonly List<UnityEngine.UI.UIWindow> CurtainMembers = new();
    }
    internal static partial class ModalFallback
    {
        private static readonly HashSet<string> IntervalAncestorWarned = new();
        internal static UnityEngine.UI.UIWindow? RefusedAncestor(UnityEngine.UI.UIWindow window) => RefusedForTheMomentAbove(window);
    }
    // Isolate the story-curtain row of the native refusal table. Other refusal
    // classes are outside this fixture; the ancestor traversal is production code.
    internal enum FloatRefusalClass { PastThePointOfNoReturn }
    internal static class FloatRefusalTable
    {
        internal static bool RefusesForTheMoment(UnityEngine.UI.UIWindow window) => StoryComposite.CurtainRefuses(window);
        internal static string? Describe(UnityEngine.UI.UIWindow window) => null;
    }
    internal sealed partial class MapTravelConfirm
    {
        internal bool _parkedIsReadyToggle;
        internal bool ReadyVisible;
        private bool ReadyToggleVisible() => ReadyVisible;
    }
}

internal static class VRLog
{
    internal static void Note(string scope, string message) { }
    internal static void Info(string scope, string message) { }
}
