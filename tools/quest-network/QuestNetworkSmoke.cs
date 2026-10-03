// Editor-only experiment using original unmodified Photon managed assemblies.
// Does not create or join any game room, initialize EOS, or change original config.
using System;
using System.IO;
using System.Reflection;
using Photon.Realtime;
using UdpKit;
using UdpKit.Platform;
using UdpKit.Platform.Photon;
using UnityEditor;
using UnityEngine;

public static class QuestNetworkSmoke
{
    [Serializable]
    private sealed class Result
    {
        public int schema = 1;
        public string platform = "UnityEditor";
        public string stage = "not-started";
        public string failure;
        public string disconnectReason;
        public bool originalDefaultConfig;
        public bool customAuthenticationSupplied;
        public bool eosInitialized;
        public bool connectedToMaster;
        public bool joinedOriginalDefaultLobby;
        public bool gameRoomJoined;
        public bool androidConnected;
        public double elapsedSeconds;
    }

    private static readonly Result Report = new Result();
    private static LoadBalancingClient Client;
    private static MethodInfo UpdateClient;
    private static double StartTime;
    private static bool Finished;
    private static int Timeout;

    public static void Run()
    {
        StartTime = EditorApplication.timeSinceStartup;
        Timeout = int.Parse(Environment.GetEnvironmentVariable("GHVR_NETWORK_TIMEOUT_SECONDS") ?? "45");
        Report.platform = Application.platform.ToString();
        Report.stage = "original-config";
        try
        {
            var config = new PhotonPlatformConfig();
            if (config.AuthenticationValues != null)
                throw new InvalidOperationException("Original default authentication changed");
            Report.originalDefaultConfig = true;
            // Match the original manager's first host/default client region.
            config.Region = PhotonRegion.GetRegion(PhotonRegion.Regions.US);
            var type = typeof(PhotonPlatformConfig).Assembly.GetType("UdpKit.Platform.Photon.Realtime.PhotonClient", true);
            Client = (LoadBalancingClient)Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { config }, null);
            Client.LoadBalancingPeer.DebugOut = ExitGames.Client.Photon.DebugLevel.OFF;
            UpdateClient = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public);
            var connect = type.GetMethod("Connect", new[] { typeof(PhotonRegion), typeof(Action<bool, UdpConnectionDisconnectReason>) });
            Report.stage = "photon-connect";
            Action<bool, UdpConnectionDisconnectReason> done = (accepted, reason) =>
            {
                Report.disconnectReason = reason.ToString();
                Report.connectedToMaster = accepted || Report.connectedToMaster;
                Report.joinedOriginalDefaultLobby = accepted && Client.InLobby;
                Finish(Report.joinedOriginalDefaultLobby ? null : "Original Photon connect/lobby callback rejected the client");
            };
            EditorApplication.update += Pump;
            if (!(bool)connect.Invoke(Client, new object[] { config.Region, done }))
                Finish("Original Photon connect did not start");
        }
        catch (Exception failure)
        {
            Finish("Local initialization failed: " + failure.GetBaseException().GetType().Name);
        }
    }

    private static void Pump()
    {
        if (Finished) return;
        try
        {
            UpdateClient.Invoke(Client, null);
            Report.connectedToMaster |= Client.State == ClientState.ConnectedToMasterServer || Client.InLobby;
            if (EditorApplication.timeSinceStartup - StartTime > Timeout)
                Finish("Bounded Photon connection timeout");
        }
        catch (Exception failure)
        {
            Finish("Local update failed: " + failure.GetBaseException().GetType().Name);
        }
    }

    private static void Finish(string failure)
    {
        if (Finished) return;
        Finished = true;
        EditorApplication.update -= Pump;
        Report.failure = failure;
        Report.elapsedSeconds = EditorApplication.timeSinceStartup - StartTime;
        Report.stage = failure == null ? "joined-original-default-lobby" : Report.stage;
        try
        {
            // Original Disable stops its fallback thread and disconnects this
            // one client. No global/network/server configuration is changed.
            if (Client != null)
                Client.GetType().GetMethod("Disable").Invoke(Client, new object[] { DisconnectCause.DisconnectByClientLogic });
        }
        catch (Exception cleanup)
        {
            Report.failure = (Report.failure ?? "") + " Cleanup failed: " + cleanup.GetBaseException().GetType().Name;
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("GHVR_NETWORK_REPORT"), JsonUtility.ToJson(Report, true));
        EditorApplication.Exit(Report.failure == null ? 0 : 1);
    }
}
