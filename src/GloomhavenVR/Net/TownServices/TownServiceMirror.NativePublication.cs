namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
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
    private static byte[] WriteNativeTownFrame(TownServiceFrame frame) =>
        TryWriteNativeTemplateState(frame, out byte[] packet) ? packet : TownServiceCodec.Write(frame);
}
