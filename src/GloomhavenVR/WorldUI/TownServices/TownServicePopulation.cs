using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One cosmetic NPC per service, independent of the number of browsing players.
/// The canonical parchment frame never uses a visitor's zoom, head pose or environment choice.</summary>
internal static class TownServicePopulation
{
    private sealed class Resident
    {
        internal TownServiceStation Station = null!;
        internal float Visibility;
    }
    private static readonly Dictionary<byte, Resident> Residents = new();
    private static GameObject? _frame;
    internal static Transform? Frame => _frame != null ? _frame.transform : null;

    internal static bool Prepare()
    {
        if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)) return false;
        if (_frame == null) _frame = new GameObject("GloomhavenVR.TownService.SharedFrame");
        _frame.transform.SetPositionAndRotation(center, Quaternion.identity);
        _frame.transform.localScale = Vector3.one * scale;
        TownServiceMirror.SharedFrameForRemote = RemoteFrame;
        return true;
    }

    private static Transform? RemoteFrame(int peer) => MapRoomDriver.Active ? Frame : null;

    internal static TownServiceStation? Acquire(byte service)
    {
        if (!Prepare()) return null;
        if (Residents.TryGetValue(service, out Resident? current)) return current.Station;
        TownServiceStation? station = TownServiceStation.Create(service, _frame!.transform.position, _frame.transform.localScale.x);
        if (station == null) return null;
        station.SetVisibility(0f);
        Residents.Add(service, new Resident { Station = station });
        return station;
    }

    internal static void Tick()
    {
        if (!Prepare()) { Reset(); return; }
        float now = Time.unscaledTime;
        int local = NetPlayerActors.LocalPlayerId();
        for (byte service = 1; service <= 3; service++)
        {
            bool used = TownServicePresentation.Active && TownServicePresentation.Service == service;
            int author = used ? local : int.MaxValue;
            float age = used ? TownServicePresentation.SessionAge : 0f;
            TownServiceSessionInfo? owner = null;
            foreach (TownServiceSessionInfo remote in TownServiceMirror.RemoteSessions.Values)
            {
                if (!remote.Active || remote.Service != service || now - remote.ReceivedTime > 10f) continue;
                used = true;
                if (remote.Peer >= author) continue;
                author = remote.Peer; owner = remote;
                age = remote.SessionAge + Mathf.Max(0f, now - remote.ReceivedTime);
            }
            if (used) Acquire(service);
            if (!Residents.TryGetValue(service, out Resident? resident)) continue;
            // The active author's canonical station pose also reaches clients with another room.
            // The local author keeps its opening pose; none of these objects follows the head.
            if (owner != null)
            {
                Transform root = resident.Station.Root;
                root.position = _frame!.transform.TransformPoint(owner.Position);
                root.rotation = _frame.transform.rotation * owner.Rotation;
                root.localScale = Vector3.Scale(_frame.transform.lossyScale, owner.Scale);
            }
            resident.Visibility = Mathf.MoveTowards(resident.Visibility, used ? 1f : 0f,
                Time.unscaledDeltaTime / (used ? .22f : .18f));
            resident.Station.SetVisibility(resident.Visibility);
            if (used) resident.Station.Sample(age < 1.2f ? "Greeting" : "Idle", age < 1.2f ? age : age - 1.2f);
            if (!used && resident.Visibility <= 0f)
            { resident.Station.Dispose(); Residents.Remove(service); }
        }
    }

    internal static void Reset()
    {
        if (_frame == null && Residents.Count == 0) return;
        TownServiceSync.Shutdown();
        foreach (Resident resident in Residents.Values) resident.Station.Dispose();
        Residents.Clear();
        if (_frame != null) Object.Destroy(_frame);
        _frame = null;
    }
}
