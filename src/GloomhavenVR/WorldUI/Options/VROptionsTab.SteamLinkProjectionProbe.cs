using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    // Cheap live permission for the cold Tick and render-frame decision. Binding
    // belongs to the options/catalogue path, never to the image callback.
    internal static bool ProjectionProbeCheatsEnabled => _cheatsEnabled != null && _cheatsEnabled.Value;

    private static int BuildSteamLinkProjectionProbe()
    {
        if (ContentRoot == null || !CheatsAvailable) return 0;
        BuildHeader(ContentRoot, Loc.SteamLinkProjectionProbeHeading, sub: true);
        BuildNote(ContentRoot, Loc.SteamLinkProjectionProbeHint, multiline: true);
        RegisterCheatRow(BuildLinkRow(ContentRoot, ProjectionProbeCaption(), () =>
        {
            // Recheck permission when a stale action is invoked after cfg edits.
            if (CheatsAvailable) SteamLinkProjectionProbe.Arm();
            else SteamLinkProjectionProbe.Stop();
        }, asAction: true), ProjectionProbeCaption);
        return 1;
    }

    private static string ProjectionProbeCaption() => !SteamLinkProjectionProbe.Eligible
        ? Loc.SteamLinkProjectionProbeUnavailable : SteamLinkProjectionProbe.Status switch
        {
            SteamLinkProjectionProbe.Phase.PendingClose => Loc.SteamLinkProjectionProbeArmed,
            SteamLinkProjectionProbe.Phase.Starting or SteamLinkProjectionProbe.Phase.Active or SteamLinkProjectionProbe.Phase.Stopping => Loc.SteamLinkProjectionProbeBusy,
            SteamLinkProjectionProbe.Phase.Failed or SteamLinkProjectionProbe.Phase.Unsafe => Loc.SteamLinkProjectionProbeFailed,
            _ => Loc.SteamLinkProjectionProbeStart
        };
}
