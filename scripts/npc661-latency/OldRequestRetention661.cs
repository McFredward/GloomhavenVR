// Actual production methods from delivery checkpoint 7aa766d85, before source/census dominance guards.
// Negative source-control input only; never compiled as a standalone fixture.
    private static bool RetainUnpreparedNativeTemplate(int peer, TownServiceFrame frame)
    {
        if (peer <= 0 || frame.NativeTemplateBasisKey == 0) return false;
        RecordOriginalRequest(peer, frame);
        var key = new UnpreparedKey(peer, frame);
        if (UnpreparedNativeTemplates.TryGetValue(key, out UnpreparedNativeTemplate? previous))
        {
            if (previous.Frame.Session == frame.Session && previous.Frame.Service == frame.Service
                && previous.Frame.Sequence >= frame.Sequence) return true;
            previous.Frame = frame; previous.Received = Time.unscaledTime; previous.RetryAt = 0f; return true;
        }
        if (UnpreparedNativeTemplates.Count >= TownServiceFrame.MaxModules) return false;
        UnpreparedNativeTemplates.Add(key, new UnpreparedNativeTemplate { Frame = frame, Received = Time.unscaledTime });
        UnpreparedNativeOrder.Enqueue(key); return true;
    }

    private static void RecordOriginalRequest(int peer, TownServiceFrame frame)
    {
        // A refused compact original is not a retained-original receipt. Request
        // only that exact private metadata identity; an unrelated cold catalog or
        // invisible visitor-stock preparation keeps its independent repair path.
        if (peer <= 0 || peer == LocalPeer || frame.PublicCatalog || frame.VisitorStock
            || frame.NativeTemplateBasisKey == 0 || frame.BaseSequence != 0
            || frame.Sequence == 0 || frame.Session == 0 || frame.Service is not (1 or 3)
            || frame.Module >= TownServiceFrame.VoiceModule) return;
        if (!OriginalRequests.TryGetValue(peer, out OriginalRequestOwner? owner))
        {
            if (OriginalRequests.Count >= 24) return;
            owner = new OriginalRequestOwner(); OriginalRequests.Add(peer, owner);
        }
        if (owner.Service != frame.Service || owner.Session != frame.Session)
        { owner.Modules.Clear(); owner.Service = frame.Service; owner.Session = frame.Session; }
        if (owner.Modules.TryGetValue(frame.Module, out OriginalRequest? previous)
            && previous.Frame.Sequence >= frame.Sequence) return;
        if (owner.Modules.Count >= TownServiceFrame.MaxModules && !owner.Modules.ContainsKey(frame.Module)) return;
        owner.Modules[frame.Module] = new OriginalRequest { Frame = frame, Created = UnityEngine.Time.unscaledTime };
    }
