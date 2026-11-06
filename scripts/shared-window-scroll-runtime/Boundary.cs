using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Explicit native session/window registry ports. Actual Unity ScrollRect,
// Scrollbar, graphics, masks, event camera, raycasters and input callbacks run.
public class UIWindow : MonoBehaviour { public bool IsOpen = true, IsVisible = true; }
public static class FFSNetwork { public static bool IsOnline = true; }
public static class Singleton<T> where T : class
{ public static T Instance = null!; public static bool IsInitialized => Instance != null; }
public class UICharacterStoryBox : MonoBehaviour { public List<string>? dialogs; public int currentDialogIndex; }
public sealed class StoryController { public UICharacterStoryBox? DialogBox; }
public sealed class MapStoryController { public UICharacterStoryBox? dialogBox; public UIWindow? window; }
public sealed class UIEventPanel { public EventData? eventData; public Screen? currentScreen; public class EventData { public string ID = "event"; } public class Screen { public string Name = "page"; } }
public class UIQuestPopup : MonoBehaviour { public Quest? quest; public class Quest { public string LocalisedNameKey = "quest"; } }
namespace FFSNet { internal static class PlayerRegistry { internal static int HostPlayerID = 1; } }
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string scope, string text) { } internal static void Info(string scope, string text) { } } }
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapCityEventReadyUp { internal static bool Active; internal static uint ContentKey; } }
namespace GloomhavenVR.WorldUI
{
    internal enum SharedWindowKind : byte { None, ScenarioStory, MapStory, QuestConfirm, Encounter, Video, RewardShowcase, Results }
    internal static class SharedWindows
    {
        internal static UIWindow?[] Native = new UIWindow?[8];
        internal static bool MapActive = true;
        internal static UIWindow? WindowOf(SharedWindowKind kind) => Native[(byte)kind];
        internal static SharedWindowKind KindOf(UIWindow window)
        { for (byte i = 1; i <= 7; i++) if (Native[i] == window) return (SharedWindowKind)i; return 0; }
        internal static bool ParticipatesHere(SharedWindowKind kind) => FFSNetwork.IsOnline && kind != 0
            && ((kind != SharedWindowKind.MapStory && kind != SharedWindowKind.QuestConfirm && kind != SharedWindowKind.Encounter) || MapActive);
        internal static bool MapActiveFor(int id) => Program.Peers[id].Map;
    }
}
namespace GloomhavenVR.Net
{
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static class SharedWindowSpawnAuthority { internal static int AuthorId = 1; }
    internal static class VersionGuard
    {
        internal static ushort PeerBuild(int id) => Program.Unmodded.Contains(id) ? (ushort)0 : NetProtocol.ModBuild;
        internal static void CollectContinuationPeers(List<int> peers, int local)
        { peers.Clear(); foreach (int id in Program.Peers.Keys) if (id != local && !Program.Unmodded.Contains(id)) peers.Add(id); }
    }
    internal static class RemoteStorySync
    {
        internal static uint HashDialog(List<string>? pages) => pages == null ? 0 : NetProtocol.HashMapKey(string.Join("/", pages));
    }
    internal interface INetTransport { int LocalPlayerId { get; } bool IsOnline { get; } }
}
