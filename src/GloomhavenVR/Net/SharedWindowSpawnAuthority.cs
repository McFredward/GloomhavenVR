using System;
using FFSNet;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Placement authority only. Native input, story completion and gameplay authority
/// are independent. A flat host has no VR head, so one compatible VR presenter supplies its
/// shared physical placement; disabling transport always retains usable local native UI.</summary>
internal static class SharedWindowSpawnAuthority
{
    private static readonly List<int> Peers = new(4);
    private static Func<bool>? _transportOnline;

    internal static void Bind(Func<bool> transportOnline)
    {
        _transportOnline = transportOnline;
        SharedWindowSpawnBridge.InitialAuthor = IsInitialAuthor;
        SharedWindowSpawnBridge.TryInitialPose = TryInitialPose;
    }

    internal static int AuthorId
    {
        get
        {
            int local = NetPlayerActors.LocalPlayerId();
            if (!FFSNetwork.IsOnline || FFSNetwork.IsHost) return local;
            if (VersionGuard.PeerBuild(PlayerRegistry.HostPlayerID) == NetProtocol.ModBuild)
                return PlayerRegistry.HostPlayerID;
            // No compatible host presentation: use one VR surrogate, never each viewer's head.
            VersionGuard.CollectContinuationPeers(Peers, local);
            int author = local;
            foreach (int peer in Peers)
                if (author <= 0 || peer < author) author = peer;
            return author;
        }
    }

    internal static bool IsInitialAuthor() => !FFSNetwork.IsOnline || NetSession.FlatNetMode
        || _transportOnline?.Invoke() != true
        || (NetPlayerActors.LocalPlayerId() > 0 && AuthorId == NetPlayerActors.LocalPlayerId());

    internal static bool AcceptInitial(int sender, SharedWindowPoseContext? context, int kind) =>
        context.HasValue && context.Value.Valid && context.Value.IsInitial(kind)
        && sender == AuthorId && context.Value.Host == (sender == PlayerRegistry.HostPlayerID);

    internal static bool MayPublishInitial(SharedWindowKind kind) => IsInitialAuthor()
        && (kind == SharedWindowKind.Video ? NativeVideoWindow.SharedInitialPoseReady
            : ModalFallback.SharedInitialPoseReady(kind));

    private static bool TryInitialPose(SharedWindowKind kind, out Vector3 position,
        out Quaternion rotation, out float factor) => kind == SharedWindowKind.ScenarioStory
        ? RemoteStorySync.TryInitialPose(out position, out rotation, out factor)
        : kind == SharedWindowKind.Video
            ? RemoteMapStory.TryVideoSharedInitialPose(out position, out rotation, out factor)
            : RemoteMapStory.TryInitialPose(kind, out position, out rotation, out factor);

    internal static void SampleContext(ref PresenceState presence)
    {
        bool any = presence.HasStorySync && (presence.StoryFlags & NetProtocol.StoryPoseBit) != 0;
        byte initial = presence.StoryInitialPose ? SharedWindowPoseContext.KindBit(1) : (byte)0;
        if (presence.HasSharedWindow && presence.SharedWindowEntries != null)
            for (int i = 0; i < presence.SharedWindowCount && i < presence.SharedWindowEntries.Length; i++)
            {
                SharedWindowEntry entry = presence.SharedWindowEntries[i];
                if ((entry.Flags & NetProtocol.SharedPoseBit) == 0) continue;
                any = true;
                if (entry.InitialPose) initial |= SharedWindowPoseContext.KindBit(entry.Kind + 1);
            }
        if (presence.HasVideoWindow && (presence.VideoWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0)
        {
            any = true;
            if (presence.VideoWindow.Window.InitialPose) initial |= SharedWindowPoseContext.KindBit(5);
        }
        if (presence.HasRewardWindow && (presence.RewardWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0)
        {
            any = true;
            if (presence.RewardWindow.Window.InitialPose) initial |= SharedWindowPoseContext.KindBit(6);
        }
        if (!any || !PanelLayout.TryGetAnchor(out Vector3 origin, out Quaternion yaw)) return;
        var context = new SharedWindowPoseContext { Host = FFSNetwork.IsHost, Initial = initial,
            Origin = origin, Yaw = yaw.eulerAngles.y, Scale = Mathf.Max(PanelLayout.WorldScale, 0.01f) };
        if (context.Valid) presence.WindowPoseContext = context;
    }
}
