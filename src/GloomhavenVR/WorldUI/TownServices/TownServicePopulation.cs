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
        internal float Visibility, Age;
        internal byte Clip;
        internal TownServiceVisitTarget Visit = null!;
    }
    private static readonly Dictionary<byte, Resident> Residents = new();
    private static GameObject? _frame;
    internal static Transform? Frame => _frame != null ? _frame.transform : null;
    internal static TownResidentsState Published { get; private set; }
    internal static bool Available(byte service) => Residents.TryGetValue(service, out Resident? resident)
        && resident.Station.IsReady;
    private static float _started, _retryAt;

    internal static bool HasRemoteVisitors
    {
        get
        {
            float now = Time.unscaledTime;
            foreach (TownServiceSessionInfo remote in TownServiceMirror.RemoteSessions.Values)
                if (remote.Active && now - remote.ReceivedTime <= NetProtocol.StaleTimeoutSeconds) return true;
            return false;
        }
    }

    internal static bool Prepare()
    {
        if (!MapRoomDriver.Active || !MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)) return false;
        if (_frame == null)
        { _frame = new GameObject("GloomhavenVR.TownService.SharedFrame"); _started = Time.unscaledTime; }
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
        Residents.Add(service, new Resident { Station = station, Visit = new TownServiceVisitTarget(service, station.Root) });
        return station;
    }

    internal static void Tick()
    {
        bool enabled = WorldUIConfig.ImmersiveTownServices.Value;
        if (!MapRoomDriver.Active) { Reset(); return; }
        if (_frame == null && !enabled && !HasRemoteVisitors) return;
        if (!Prepare()) { Reset(); return; }
        float now = Time.unscaledTime;
        bool follows = RemoteTownResidents.TryAuthor(out TownResidentsState authored, out float elapsed);
        var published = new TownResidentsState { Active = enabled };
        bool retry = now >= _retryAt;
        bool missing = false;
        for (byte service = 1; service <= 3; service++)
        {
            bool visiting = TownServicePresentation.Active && TownServicePresentation.Service == service;
            float visitAge = visiting ? TownServicePresentation.SessionAge : float.PositiveInfinity;
            foreach (TownServiceSessionInfo remote in TownServiceMirror.RemoteSessions.Values)
            {
                if (!remote.Active || remote.Service != service || now - remote.ReceivedTime > NetProtocol.StaleTimeoutSeconds) continue;
                visiting = true;
                visitAge = Mathf.Min(visitAge, remote.SessionAge + Mathf.Max(0f, now - remote.ReceivedTime));
            }
            bool used = enabled || visiting;
            if (used && retry) Acquire(service);
            if (!Residents.TryGetValue(service, out Resident? resident))
            { if (used) missing = true; published.Active = false; continue; }
            resident.Station.RefreshEnvironment(!follows);
            bool ready = resident.Station.IsReady;
            if (!ready) published.Active = false;
            float greeting = resident.Station.GreetingDuration;
            if (used && ready && follows)
            {
                TownResidentPose pose = authored.At(service - 1);
                Transform root = resident.Station.Root;
                root.SetPositionAndRotation(_frame!.transform.TransformPoint(pose.Pose.Position),
                    _frame.transform.rotation * pose.Pose.Rotation);
                root.localScale = Vector3.one * (_frame.transform.lossyScale.x * pose.Scale);
                resident.Visibility = pose.Visibility / 255f;
                resident.Age = pose.Age + elapsed;
                resident.Clip = pose.Clip;
                if (resident.Clip == 1 && resident.Age >= greeting)
                { resident.Clip = 0; resident.Age -= greeting; }
            }
            else
            {
                resident.Visibility = Mathf.MoveTowards(resident.Visibility, used && ready ? 1f : 0f,
                    Time.unscaledDeltaTime / (used ? .22f : .18f));
                resident.Clip = visiting && visitAge < greeting ? (byte)1 : (byte)0;
                resident.Age = resident.Clip == 1 ? visitAge : visiting
                    ? Mathf.Max(0f, visitAge - greeting) : Mathf.Max(0f, now - _started);
            }
            resident.Station.SetVisibility(resident.Visibility);
            resident.Station.Sample(resident.Clip == 1 ? "Greeting" : "Idle", resident.Age);
            resident.Visit.Tick(enabled && used && ready && resident.Visibility >= .99f);
            Transform station = resident.Station.Root;
            published.Set(service - 1, new TownResidentPose {
                Pose = new RigPose { Position = _frame!.transform.InverseTransformPoint(station.position),
                    Rotation = Quaternion.Inverse(_frame.transform.rotation) * station.rotation },
                Scale = station.lossyScale.x / _frame.transform.lossyScale.x,
                Age = resident.Age, Clip = resident.Clip,
                Visibility = (byte)Mathf.RoundToInt(resident.Visibility * 255f) });
            if (!used && resident.Visibility <= 0f)
            { resident.Visit.Dispose(); resident.Station.Dispose(); Residents.Remove(service); }
        }
        if (missing && retry) _retryAt = now + 2f;
        Published = published;
        TownServiceVisitTarget.TickLaser();
    }

    internal static void Reset()
    {
        Published = default;
        if (_frame == null && Residents.Count == 0) return;
        TownServiceSync.Shutdown();
        foreach (Resident resident in Residents.Values)
        { resident.Visit.Dispose(); resident.Station.Dispose(); }
        Residents.Clear();
        if (_frame != null) Object.Destroy(_frame);
        _frame = null; _retryAt = 0f;
    }
}
