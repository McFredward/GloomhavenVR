using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    // Cheap live permission for the preview's cold Tick. Binding belongs to the
    // options/catalogue path, never to rendering or the helper worker.
    internal static bool ProjectionProbeCheatsEnabled => _cheatsEnabled != null && _cheatsEnabled.Value;

    private static int BuildSteamLinkProjectionProbe()
    {
        if (ContentRoot == null || !CheatsAvailable) return 0;
        BuildHeader(ContentRoot, Loc.SteamLinkProjectionProbeHeading, sub: true);
        BuildNote(ContentRoot, Loc.SteamLinkProjectionProbeHint, multiline: true);
        RegisterCheatRow(BuildLinkRow(ContentRoot, ProjectionProbeCaption(), () =>
        {
            // Recheck stale actions. Only an accepted direct start may close the
            // window; duplicate/refused starts never hide the user's controls.
            if (!CheatsAvailable) { SteamLinkProjectionProbe.Stop(); return; }
            if (!SteamLinkProjectionProbe.Start()) return;
            try { Close(); }
            catch (Exception ex)
            {
                if (VRLog.WantsDebug) VRLog.Debug("Core", "Steam Link projection preview options close unavailable: " + ex.GetType().Name + "; Stop remains available.");
            }
            // Observe a successful programmatic close immediately, including if
            // the user reopens options before the next normal runtime Tick.
            SteamLinkProjectionProbe.Tick();
        }, asAction: true), ProjectionProbeCaption);
        BuildLinkRow(ContentRoot, Loc.SteamLinkProjectionProbeStop, SteamLinkProjectionProbe.Stop, asAction: true);
        return 2;
    }

    private static string ProjectionProbeCaption() => !SteamLinkProjectionProbe.Eligible
        ? Loc.SteamLinkProjectionProbeUnavailable : SteamLinkProjectionProbe.Status switch
        {
            SteamLinkProjectionProbe.Phase.Starting or SteamLinkProjectionProbe.Phase.Active or SteamLinkProjectionProbe.Phase.Stopping => Loc.SteamLinkProjectionProbeBusy,
            SteamLinkProjectionProbe.Phase.Failed or SteamLinkProjectionProbe.Phase.Unsafe => Loc.SteamLinkProjectionProbeFailed,
            _ => Loc.SteamLinkProjectionProbeStart
        };
}
