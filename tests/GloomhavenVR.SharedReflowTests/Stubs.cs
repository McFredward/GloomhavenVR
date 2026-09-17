using GloomhavenVR.WorldUI;
using UnityEngine;
namespace UnityEngine {
    internal struct Vector3 { internal float x; internal static Vector3 zero => default; }
    internal struct Quaternion { internal static Quaternion identity => default; }
    internal static class Time { internal static float unscaledTime; internal static int frameCount; }
}
namespace UnityEngine.UI { internal sealed class UIWindow { } }
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver { internal static bool Active; } }
namespace GloomhavenVR.WorldUI {
    internal static class SharedWindowReflowBridge {
        internal static Func<bool>? CanArrange; internal static Func<SharedWindowKind,bool>? Begin;
        internal static Func<SharedWindowKind,int>? Revision;internal static Action<SharedWindowKind>? End;
    }
    internal enum SharedWindowKind { None, MapStory, QuestConfirm, Encounter }
    internal sealed class GrabbableModal { internal bool IsGrabbed; internal bool Visible=true; internal Vector3 Position; }
    internal static class SharedWindows {
        internal static readonly Dictionary<SharedWindowKind,GrabbableModal> Grabs=new();
        internal static bool TryGetGrab(SharedWindowKind kind, out GrabbableModal? grab) => Grabs.TryGetValue(kind,out grab);
    }
    internal static class ModalFallback { internal static void NoteSharedAnchorSpent(SharedWindowKind kind,string why) {} }
}
namespace GloomhavenVR.Net {
    internal static class FFSNetwork { internal static bool IsOnline,IsHost; }
    internal static class NetPlayerActors { internal static int Id; internal static int LocalPlayerId()=>Id; }
    internal static class NetProtocol {
        internal const byte MapRoomInRoomBit=1, MapRoomHostBit=2, SharedPoseBit=2, SharedFinishedBit=4, SharedWindowKindMapStory=1;
        internal const byte SharedWindowMotionMapStoryBit=1,SharedWindowMotionQuestConfirmBit=2,SharedWindowMotionEncounterBit=4;
        internal static byte EncodeStoryPage(int value)=>(byte)value;
    }
    internal struct PresenceState { internal bool HasSharedWindowMotion,HasMapRoom; internal byte SharedWindowHeldMask,SharedWindowReflowMask,MapRoomFlags; }
    internal struct SharedWindowEntry { internal byte Flags,PoseStamp,Kind,Page,PageCount; internal uint ContentKey; }
    internal sealed class SharedWindowPoseTrack { internal void Reset(){} }
    internal static class SharedWindowFrame {
        internal static bool TryRead(GrabbableModal grab,out Vector3 pos,out Quaternion rot,out float size) {pos=grab.Position;rot=default;size=1;return grab.Visible;}
    }
    internal static partial class RemoteMapStory {
        private const float PeerStaleSeconds=5,MoveSettleSeconds=0.15f;
        private const int IdentitySettleFrames=2;
        private static readonly Local StoryLocal=new(),QuestLocal=new(),EncounterLocal=new();
        private static readonly SharedWindowEntry[] SendBuffer=new SharedWindowEntry[3];
        private static bool SyncIdentity(SharedWindowKind kind,Local local,GrabbableModal grab,Vector3 pos,Quaternion rot,float size) {
            if(ReferenceEquals(local.Grab,grab))return false;
            local.ForgetPose();local.Grab=grab;local.FramePos=pos;local.FrameRot=rot;local.FrameSize=size;local.HaveBaseline=true;local.SwapFrame=Time.frameCount;return true;
        }
        private static void TrackFrame(SharedWindowKind kind,Local local,bool reset) {if(reset){local.HaveBaseline=false;local.Moving=false;local.Grab=null;}}
        private static void WritePose(SharedWindowKind kind,Local local,ref SharedWindowEntry entry) {if(local.PoseOwned||local.Moving)entry.Flags|=NetProtocol.SharedPoseBit;}
    }
}
