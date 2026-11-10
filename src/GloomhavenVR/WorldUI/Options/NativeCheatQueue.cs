using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FFSNet;
using GloomhavenVR.Net;

namespace GloomhavenVR.WorldUI;

/// <summary>Exact native queue/roster reflection; keeps Bolt's entity types out of mod references.</summary>
internal static class NativeCheatQueue
{
    private static readonly Type Registry = typeof(PlayerRegistry);
    private static readonly FieldInfo? Players = Registry.GetField("AllPlayers", BindingFlags.Static | BindingFlags.Public);
    private static readonly PropertyInfo? Local = Registry.GetProperty("MyPlayer", BindingFlags.Static | BindingFlags.Public);
    private static readonly PropertyInfo? Host = Registry.GetProperty("HostPlayer", BindingFlags.Static | BindingFlags.Public);
    private static readonly PropertyInfo? Joining = Registry.GetProperty("JoiningPlayers", BindingFlags.Static | BindingFlags.Public);
    private static readonly PropertyInfo? Connecting = Registry.GetProperty("ConnectingUsers", BindingFlags.Static | BindingFlags.Public);
    private static readonly MethodInfo? Queue = typeof(Synchronizer).GetMethod("AutoExecuteServerAuthGameAction", BindingFlags.Static | BindingFlags.Public);

    private static readonly Dictionary<int, WeakReference<object>> Receipts = new();
    private static int _receiptEpoch = -1;
    internal sealed class Cohort
    {
        internal readonly Dictionary<int, WeakReference<object>> Members = new();
    }
    private static void ReceiptEpoch()
    {
        if (_receiptEpoch == VersionGuard.SessionEpoch) return;
        Receipts.Clear(); _receiptEpoch = VersionGuard.SessionEpoch;
    }
    private static object? CurrentPlayer(int wanted)
    {
        try
        {
            if (Players?.GetValue(null) is not IEnumerable players) return null;
            foreach (object? player in players)
                if (player?.GetType().GetProperty("PlayerID")?.GetValue(player) is int id && id == wanted) return player;
        }
        catch { /* A retiring native entity is not a capability receipt. */ }
        return null;
    }
    internal static void NoteVersionReceipt(int sender, ushort build)
    {
        ReceiptEpoch();
        if (build != NetProtocol.ModBuild) { Receipts.Remove(sender); return; }
        object? player = CurrentPlayer(sender);
        if (player == null) { Receipts.Remove(sender); return; }
        if (Receipts.TryGetValue(sender, out var receipt) && receipt.TryGetTarget(out object? previous)
            && ReferenceEquals(previous, player)) return;
        Receipts[sender] = new WeakReference<object>(player);
    }
    internal static bool HasCurrentVersion(int sender)
    {
        ReceiptEpoch();
        return Receipts.TryGetValue(sender, out var receipt) && receipt.TryGetTarget(out object? seen)
            && ReferenceEquals(seen, CurrentPlayer(sender));
    }
    internal static Cohort? CaptureCohort(bool requireStable = true)
    {
        if (!ReadRoster(out int[] ids, out _, requireStable)) return null;
        var cohort = new Cohort();
        foreach (int id in ids)
        {
            object? player = CurrentPlayer(id); if (player == null) return null;
            cohort.Members[id] = new WeakReference<object>(player);
        }
        return cohort;
    }
    internal static bool CohortUnchanged(Cohort? cohort, bool requireStable = true)
    {
        if (cohort == null || !ReadRoster(out int[] ids, out _, requireStable) || cohort.Members.Count != ids.Length) return false;
        foreach (int id in ids)
            if (!cohort.Members.TryGetValue(id, out var reference) || !reference.TryGetTarget(out object? before)
                || !ReferenceEquals(before, CurrentPlayer(id))) return false;
        return true;
    }

    internal static bool ReadRoster(out int[] ids, out int local, bool requireStable = true)
    {
        ids = Array.Empty<int>(); local = 0;
        try
        {
            if (Players == null || Local == null || Host == null || Joining == null || Connecting == null
                || requireStable && (PlayerRegistry.OtherClientsAreJoining || PlayerRegistry.LoadingInFromJoiningClient
                    || PlayerRegistry.IsSwitchingCharacter
                    || Joining.GetValue(null) is ICollection joins && joins.Count > 0
                    || Connecting.GetValue(null) is ICollection connects && connects.Count > 0)) return false;
            // Null is a native empty client-side list. Any other unexpected type
            // fails closed instead of being misread as an empty join queue.
            object? joining = Joining.GetValue(null), connecting = Connecting.GetValue(null);
            if (joining != null && joining is not ICollection || connecting != null && connecting is not ICollection) return false;
            object? me = Local.GetValue(null), host = Host.GetValue(null);
            if (me == null || host == null || Players.GetValue(null) is not IEnumerable players) return false;
            PropertyInfo? id = me.GetType().GetProperty("PlayerID", BindingFlags.Public | BindingFlags.Instance);
            if (id == null || id.GetValue(host) is not int hostId || hostId != PlayerRegistry.HostPlayerID
                || id.GetValue(me) is not int localId || localId <= 0) return false;
            var result = new List<int>(4); var unique = new HashSet<int>();
            foreach (object? player in players)
            {
                if (player == null || id.GetValue(player) is not int value || value <= 0 || !unique.Add(value)) return false;
                result.Add(value);
            }
            if (!unique.Contains(hostId) || !unique.Contains(localId)) return false;
            result.Sort(); ids = result.ToArray(); local = localId; return true;
        }
        catch (Exception) { return false; } // Unknown topology never admits a state mutation.
    }

    internal static void Enqueue(byte[] payload)
    {
        if (Queue == null) throw new MissingMethodException("Original host action queue is unavailable.");
        ParameterInfo[] parameters = Queue.GetParameters(); var args = new object?[parameters.Length];
        bool kind = false, phase = false, data = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo p = parameters[i];
            switch (p.Name)
            {
                case "actionType": args[i] = (GameActionType)MultiplayerCheats.NativeAction; kind = true; break;
                case "targetPhaseType": args[i] = ActionPhaseType.NONE; phase = true; break;
                case "customBinaryData": args[i] = payload; data = true; break;
                default:
                    if (!p.HasDefaultValue) throw new MissingMethodException("Original queue signature changed.");
                    args[i] = p.DefaultValue; break;
            }
        }
        if (!kind || !phase || !data) throw new MissingMethodException("Original queue payload parameters changed.");
        try { Queue.Invoke(null, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}
