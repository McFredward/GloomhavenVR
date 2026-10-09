namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private static TownServiceNode[] NativePublicationNodes(TownServiceNode[] nodes)
    {
        if (_service != 3 || nodes.Length == 0
            || !nodes[0].Values.TryGetValue(TownServiceProperty.Transform, out TownServiceValue? pose)
            || pose.Numbers.Length < 10) return nodes;
        // Binding.Apply owns only the root rect fields; its absolute TRS comes
        // from the frame header. Motion capture uses this same canonical layout
        // prefix. Canonicalize the full source too, before baseline/delta capture,
        // so the lossless value pool can share repeated native row/root geometry.
        // Child TRS and every root rect value remain owner-authored. Binding.Read
        // retains its cache: never mutate a cached value or an emitted original.
        float[] numbers = (float[])pose.Numbers.Clone();
        System.Array.Clear(numbers, 0, 10);
        numbers[6] = numbers[7] = numbers[8] = numbers[9] = 1f;
        var root = new TownServiceNode { Binding = nodes[0].Binding };
        foreach (var property in nodes[0].Values) root.Values.Add(property.Key, property.Value);
        root.Values[TownServiceProperty.Transform] = new TownServiceValue { Numbers = numbers, Text = pose.Text };
        TownServiceNode[] result = (TownServiceNode[])nodes.Clone(); result[0] = root; return result;
    }

    private static bool UsesNativeTemplateState(TownServiceFrame frame) =>
        (frame.Service == 3 || frame.Service == 1 && (frame.PublicCatalog
            || frame.TemplateAddress.StartsWith("item.confirm.part.", System.StringComparison.Ordinal))) && !frame.VisitorStock
        && frame.Module < TownServiceFrame.VoiceModule && frame.CatalogBank == null && frame.Rack == null;


    private sealed class PublicationTrace
    {
        public PublicationTrace() { }
        internal uint Session;
        internal int Reports;
        internal readonly System.Collections.Generic.Dictionary<ushort, int> Modules = new();
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalLane, PublicationTrace> PublicationTraces = new();
    private static void TraceNativePublication(LocalModule module, TownServiceFrame frame, int bytes)
    {
        if (!GloomhavenVR.Core.VRLog.WantsDebug || !frame.HighPriority || frame.PublicCatalog) return;
        PublicationTrace trace = PublicationTraces.GetOrCreateValue(_local);
        if (trace.Session != frame.Session) { trace.Session = frame.Session; trace.Reports = 0; trace.Modules.Clear(); }
        if (trace.Reports >= 64) return;
        trace.Modules.TryGetValue(module.Id, out int count);
        if (count >= 2) return;
        trace.Modules[module.Id] = count + 1; trace.Reports++;
        GloomhavenVR.Core.VRLog.Info("TownServices", "Native visible original queued: service=" + frame.Service
            + " session=" + frame.Session + " module=" + frame.Module + " address=" + frame.TemplateAddress
            + " bytes=" + bytes + " nodes=" + frame.Nodes.Length + " baseline=" + frame.BaseSequence
            + " sequence=" + frame.Sequence + " sample=" + frame.SampleTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            + " parent=" + frame.ParentModule + " required=" + (_local.RequiredVisibleModules?.Length ?? -1)
            + " prepared=" + _local.Modules.Count + " kind=" + (frame.BaseSequence == 0 ? "cold-original" : "owner-delta") + ".");
    }
    private static byte[] WriteNativeTownFrame(TownServiceFrame frame)
    {
        if (!TryWriteNativeTemplateState(frame, out byte[] packet)) return TownServiceCodec.Write(frame);
        if (!frame.PublicCatalog && !frame.VisitorStock && frame.BaseSequence == 0
            && Local.TryGetValue(frame.Module, out LocalModule? source))
        {
            NativeTemplateRepair repair = source.NativeRepair ??= new NativeTemplateRepair();
            repair.Original = frame;
            repair.Requested = false;
            // Sending a partial fragment is not receipt. The complete actual
            // transport callback starts the short acknowledgment grace below.
            repair.After = float.PositiveInfinity;
        }
        return packet;
    }

    /// <summary>A known original rejection needs no speculative receipt grace or
    /// another 15Hz artwork sampling turn. Encode only the retained immutable
    /// requested source, using the same bounded transport admission as capture.</summary>
    internal static void CaptureRequestedOriginalRepairs(System.Action<byte[], int, object?> send)
    {
        if (!RequestedOriginalRepairPending) return;
        RequestedOriginalRepairPending = false;
        using var lane = new LaneScope(PrivateLane);
        int count = 0;
        float now = UnityEngine.Time.unscaledTime;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (LocalModule source in Local.Values)
        {
            NativeTemplateRepair? repair = source.NativeRepair;
            if (repair?.Requested != true) continue;
            if (repair.Original == null) { repair.Requested = false; continue; }
            if (count >= 2 || count > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                / (double)System.Diagnostics.Stopwatch.Frequency >= .002)
            { RequestedOriginalRepairPending = true; continue; }
            count++;
            try { CaptureNativeOriginalRepair(source, send, now); }
            catch (System.Exception error) { Report("requested native original repair " + source.Id, error); }
            if (repair.Original == null) repair.Requested = false;
            else RequestedOriginalRepairPending = true;
        }
    }

    private static void CaptureNativeOriginalRepair(LocalModule module,
        System.Action<byte[], int, object?> send, float now)
    {
        NativeTemplateRepair? repair = module.NativeRepair;
        if (repair?.Original == null) return;
        TownServiceFrame original = repair.Original;
        if (!ReferenceEquals(module.Baseline, original) || original.Session != _session
            || original.Service != _service || HasReceivedOriginal(module))
        { repair.Original = null; return; }
        if (now < repair.After) return;
        // Retain the original if encoding/enqueueing fails, but do not retry that
        // expensive operation on every render frame after a bounded flow report.
        repair.After = now + .25f;
        // Keep every byte of the exact owner original, including its sequence.
        // This full fallback can satisfy a waiting cumulative delta and replaces
        // no newer source identity. It uses the existing bounded town arbitration.
        byte[] packet = TownServiceCodec.Write(original);
        send(packet, packet.Length, original);
        repair.Original = null;
        TraceNativePublication(module, original, packet.Length);
    }
}
