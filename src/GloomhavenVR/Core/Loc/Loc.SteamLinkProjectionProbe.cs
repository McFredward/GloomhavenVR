namespace GloomhavenVR.Core;

internal static partial class Loc
{
    private static string ProjectionProbeText(string english, string german) => CurrentLanguage == "German" ? german : english;
    internal static string SteamLinkProjectionProbeHeading => ProjectionProbeText("Steam Link projection preview", "Steam-Link-Projektionsvorschau");
    internal static string SteamLinkProjectionProbeHint => ProjectionProbeText(
        "Starts immediately and closes VR Options. Keep your head still. The game stays visible while testing whether Steam Link delivers a fixed overlay. Reopen VR Options or press Stop to end. Requires Debug logging, Steam Frame over Steam Link and native Mixed Reality.",
        "Startet sofort und schließt VR Optionen. Halte den Kopf ruhig. Das Spiel bleibt sichtbar, während geprüft wird, ob Steam Link eine feste Projektion überträgt. Öffne VR Optionen erneut oder drücke Stopp zum Beenden. Benötigt Debug-Logging, Steam Frame über Steam Link und natives Mixed Reality.");
    internal static string SteamLinkProjectionProbeStart => ProjectionProbeText("Start projection preview", "Projektionsvorschau starten");
    internal static string SteamLinkProjectionProbeStop => ProjectionProbeText("Stop projection preview", "Projektionsvorschau stoppen");
    internal static string SteamLinkProjectionProbeBusy => ProjectionProbeText("Projection preview is running or finishing", "Projektionsvorschau läuft oder wird beendet");
    internal static string SteamLinkProjectionProbeFailed => ProjectionProbeText("Projection preview unavailable; see Debug log", "Projektionsvorschau nicht verfügbar; siehe Debug-Log");
    internal static string SteamLinkProjectionProbeUnavailable => ProjectionProbeText("Requires Debug and native Steam Link Mixed Reality", "Benötigt Debug und natives Steam-Link-Mixed-Reality");
}
