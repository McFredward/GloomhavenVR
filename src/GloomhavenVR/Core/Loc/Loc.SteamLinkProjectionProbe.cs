namespace GloomhavenVR.Core;

internal static partial class Loc
{
    private static string ProjectionProbeText(string english, string german) => CurrentLanguage == "German" ? german : english;
    internal static string SteamLinkProjectionProbeHeading => ProjectionProbeText("Steam Link projection test", "Steam-Link-Projektionstest");
    internal static string SteamLinkProjectionProbeHint => ProjectionProbeText(
        "Temporary transport test, not a quality fix. Close VR Options to begin and keep your head still. Open VR Options again to restore the game. Requires Debug logging, Steam Frame over Steam Link and native Mixed Reality.",
        "Vorübergehender Übertragungstest, keine Qualitätskorrektur. Schließe VR Optionen zum Start und halte den Kopf ruhig. Öffne VR Optionen erneut, um das Spielbild wiederherzustellen. Benötigt Debug-Logging, Steam Frame über Steam Link und natives Mixed Reality.");
    internal static string SteamLinkProjectionProbeStart => ProjectionProbeText("Prepare projection test", "Projektionstest vorbereiten");
    internal static string SteamLinkProjectionProbeArmed => ProjectionProbeText("Ready: close VR Options to start", "Bereit: VR Optionen zum Start schließen");
    internal static string SteamLinkProjectionProbeBusy => ProjectionProbeText("Projection test is finishing", "Projektionstest wird beendet");
    internal static string SteamLinkProjectionProbeFailed => ProjectionProbeText("Projection test unavailable; see Debug log", "Projektionstest nicht verfügbar; siehe Debug-Log");
    internal static string SteamLinkProjectionProbeUnavailable => ProjectionProbeText("Requires Debug and native Steam Link Mixed Reality", "Benötigt Debug und natives Steam-Link-Mixed-Reality");
}
