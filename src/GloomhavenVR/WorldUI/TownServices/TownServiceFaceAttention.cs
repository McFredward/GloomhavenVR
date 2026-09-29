using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One resident author's stable attention election. A follower never calls this.
/// Existing valid head poses are reused; no scene-wide search, allocations or gameplay writes.</summary>
internal sealed class TownServiceFaceAttention
{
    private readonly List<int> _peers = new(8);
    private int _target = int.MinValue;
    private float _nextChoice;
    internal bool Visitor { get; private set; }
    private static bool Visiting(int player, byte service, int local)
    {
        if (player == local) return TownServicePresentation.Active && TownServicePresentation.Service == service;
        return TownServiceMirror.RemoteSessions.TryGetValue(player, out TownServiceSessionInfo? info)
            && info.Active && info.Service == service && Time.unscaledTime - info.LastSeenTime <= NetProtocol.StaleTimeoutSeconds;
    }
    private static bool Head(int player, int local, out Vector3 point)
    {
        if (player != local) return NetAvatarDriver.TryGetTownFaceHead(player, out point);
        Camera? camera = VRRigDriver.HeadCamera;
        point = camera != null ? camera.transform.position : Vector3.zero;
        return camera != null && camera.gameObject.activeInHierarchy;
    }
    private static bool Visible(Transform root, Quaternion optical, Vector3 eye, Vector3 point)
    {
        Vector3 delta = (Quaternion.Inverse(optical) * (point - eye)) / Mathf.Max(.01f, root.lossyScale.x);
        return delta.sqrMagnitude >= .01f && delta.sqrMagnitude <= 36f && delta.z > -.1f;
    }
    private static bool Unobstructed(Transform root, Vector3 eye, Vector3 point)
    {
        Vector3 delta = point - eye;
        float reach = Mathf.Max(0f, delta.magnitude - .08f * root.lossyScale.x);
        return !Physics.Raycast(eye, delta.normalized, reach,
            Physics.DefaultRaycastLayers & ~(1 << GloomhavenVR.Core.VRLayers.ModLayer), QueryTriggerInteraction.Ignore);
    }
    internal Vector3? Select(byte service, Transform root, Quaternion optical, Vector3 eye)
    {
        int local = Mathf.Max(1, NetPlayerActors.LocalPlayerId());
        int interactionOwner = TownServiceMirror.InteractionOwner(service);
        if (interactionOwner != 0)
        {
            _target = interactionOwner;
            _nextChoice = Time.unscaledTime + .2f;
            Visitor = true;
            if (Head(interactionOwner, local, out Vector3 ownerHead)
                && Visible(root, optical, eye, ownerHead) && Unobstructed(root, eye, ownerHead))
                return ownerHead;
            return null;
        }
        bool valid = _target != int.MinValue && Head(_target, local, out Vector3 current) && Visible(root, optical, eye, current);
        if (valid && Time.unscaledTime < _nextChoice && Head(_target, local, out current))
        { Visitor = Visiting(_target, service, local); return current; }
        if (!valid)
        {
            if (_target != int.MinValue) { _target = int.MinValue; _nextChoice = Time.unscaledTime; }
            Visitor = false;
            if (Time.unscaledTime < _nextChoice) return null;
        }
        _peers.Clear(); NetAvatarDriver.CollectTownFacePeers(_peers); _peers.Add(local);
        int best = int.MinValue;
        float score = float.PositiveInfinity;
        Vector3 chosen = Vector3.zero;
        foreach (int candidate in _peers)
        {
            if (!Head(candidate, local, out Vector3 point) || !Visible(root, optical, eye, point) || !Unobstructed(root, eye, point)) continue;
            float next = (point - eye).sqrMagnitude / (root.lossyScale.x * root.lossyScale.x);
            if (Visiting(candidate, service, local)) next -= 100f;
            if (candidate == _target) next -= 1.25f; // stable engagement beats minor distance noise
            if (next < score || (next == score && candidate < best))
            { best = candidate; score = next; chosen = point; }
        }
        _target = best;
        Visitor = best != int.MinValue && Visiting(best, service, local);
        _nextChoice = Time.unscaledTime + (best == int.MinValue ? .2f : 1.2f);
        return best == int.MinValue ? null : chosen;
    }
}
