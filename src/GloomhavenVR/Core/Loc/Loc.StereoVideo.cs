namespace GloomhavenVR.Core;

internal static partial class Loc
{
    internal static string StereoVideoCalibrationStart => CurrentLanguage == "German"
        ? "Stereo-Videotest starten" : "Start stereo video test";
    internal static string StereoVideoCalibrationMenuHint => CurrentLanguage == "German"
        ? "Nur im Offline-Hauptmenü. Zeigt ein stummes Testbild für beide Augen. Schließe die VR-Optionen für freie Sicht auf das Testbild. Zum Beenden auf das Bild klicken; danach hier erneut starten."
        : "Offline main menu only. Shows a silent test picture for both eyes. Close VR Options to view the test picture. Click the picture to end the test, then start it again here.";
    internal static string StereoVideoCalibrationReady => CurrentLanguage == "German"
        ? "Bereit." : "Ready.";
    internal static string StereoVideoCalibrationPreparing => CurrentLanguage == "German"
        ? "Videotest wird vorbereitet …" : "Preparing video test …";
    internal static string StereoVideoCalibrationPlaying => CurrentLanguage == "German"
        ? "Stereo-Videotest läuft. Zum Schließen auf das Testbild klicken."
        : "Stereo video test is running. Click the test picture to close.";
    internal static string StereoVideoCalibrationMono => CurrentLanguage == "German"
        ? "Mono-Testbild läuft; Stereo ist mit dem aktuellen Renderer nicht verfügbar."
        : "Mono test picture is running; stereo is unavailable with the current renderer.";
    internal static string StereoVideoCalibrationClosed => CurrentLanguage == "German"
        ? "Videotest geschlossen. Du kannst ihn erneut starten." : "Video test closed. You can start it again.";
    internal static string StereoVideoCalibrationUnavailable => CurrentLanguage == "German"
        ? "Der Videotest ist nur im Offline-Hauptmenü in VR verfügbar."
        : "The video test is available only in the offline main menu in VR.";
    internal static string StereoVideoCalibrationMissingMovie => CurrentLanguage == "German"
        ? "Die Testfilme fehlen. Bitte die Modinstallation aktualisieren."
        : "The test movies are missing. Please update the mod installation.";
    internal static string StereoVideoCalibrationFailed => CurrentLanguage == "German"
        ? "Videotest fehlgeschlagen. Details stehen im Log; du kannst es erneut versuchen."
        : "Video test failed. Details are in the log; you can try again.";
    internal static string StereoVideoCalibrationClose => CurrentLanguage == "German"
        ? "Stereo-Videotest · Zum Schließen auf das Bild klicken"
        : "Stereo video test · Click the picture to close";
}
