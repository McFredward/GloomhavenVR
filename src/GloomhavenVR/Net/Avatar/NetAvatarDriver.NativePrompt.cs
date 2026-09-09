using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly byte[] _promptBuffer = new byte[NativeDecisionPromptCodec.MaxSize];
    private NativeDecisionPromptSnapshot? _sentPrompt;
    private float _nextPromptRefresh, _promptSourceTime;
    private readonly Dictionary<int, List<NativeDecisionPromptSnapshot>> _pendingPrompts = new();

    private void TickNativePromptSend(float now)
    {
        NativeDecisionPromptSnapshot? frame = NativeDecisionPromptSampler.Sample();
        if (frame == null)
        {
            if (_sentPrompt == null || _sentPrompt.State != null)
                frame = new NativeDecisionPromptSnapshot(now, 0, null);
            else frame = _sentPrompt;
        }
        bool changed = !ReferenceEquals(frame, _sentPrompt);
        if (changed)
        {
            if (_sentPrompt != null && now - _sentPrompt.SampleTime > .25f && _promptSourceTime > _sentPrompt.SampleTime)
                SendNativePrompt(new NativeDecisionPromptSnapshot(_promptSourceTime, _sentPrompt.ActorId, _sentPrompt.State));
            _sentPrompt = frame;
        }
        if (changed || now >= _nextPromptRefresh)
        {
            SendNativePrompt(frame);
            _nextPromptRefresh = now + .5f;
        }
        _promptSourceTime = now;
    }

    private void SendNativePrompt(NativeDecisionPromptSnapshot snapshot)
    {
        int length = NativeDecisionPromptCodec.Write(snapshot, _promptBuffer);
        if (length > 0) _transport.Send(_promptBuffer, length, snapshot);
    }

    private bool QueueNativePrompt(int sender, byte[] buffer, int length)
    {
        if (!NativeDecisionPromptCodec.TryRead(buffer, length, out NativeDecisionPromptSnapshot? frame)) return false;
        if (!_pendingPrompts.TryGetValue(sender, out List<NativeDecisionPromptSnapshot>? samples))
        {
            if (_pendingPrompts.Count >= 8) return true;
            samples = new List<NativeDecisionPromptSnapshot>(4);
            _pendingPrompts.Add(sender, samples);
        }
        if (samples.Count == 0 || frame!.SampleTime > samples[samples.Count - 1].SampleTime)
            PresentationPending.Append(samples, frame!, NativeDecisionPromptSnapshot.SameIdentity);
        return true;
    }

    private void ApplyNativePrompt()
    {
        foreach (var pair in _pendingPrompts)
        {
            if (pair.Value.Count == 0) continue;
            try { NativeDecisionPromptRegistry.Set(pair.Key, pair.Value[0]); }
            catch (Exception e) { LogPhaseError($"Apply native decision prompt from player {pair.Key}", e); }
            pair.Value.RemoveAt(0);
        }
    }

    private void ForgetNativePrompt(int sender)
    {
        _pendingPrompts.Remove(sender);
        NativeDecisionPromptRegistry.Remove(sender);
    }

    private void ResetNativePrompt()
    {
        foreach (int sender in _pendingPrompts.Keys) NativeDecisionPromptRegistry.Remove(sender);
        _pendingPrompts.Clear();
        _sentPrompt = null;
        _nextPromptRefresh = _promptSourceTime = 0;
        NativeDecisionPromptSampler.Reset();
    }
}
