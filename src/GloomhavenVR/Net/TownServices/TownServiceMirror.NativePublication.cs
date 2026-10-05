namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private static bool UsesNativeTemplateState(TownServiceFrame frame) =>
        (frame.Service == 3 || frame.Service == 1 && (frame.PublicCatalog
            || frame.TemplateAddress.StartsWith("item.confirm.part.", System.StringComparison.Ordinal))) && !frame.VisitorStock
        && frame.Module < TownServiceFrame.VoiceModule && frame.CatalogBank == null && frame.Rack == null;

    private static byte[] WriteNativeTownFrame(TownServiceFrame frame) =>
        TryWriteNativeTemplateState(frame, out byte[] packet) ? packet : TownServiceCodec.Write(frame);
}
