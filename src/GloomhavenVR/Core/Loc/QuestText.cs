using System;

namespace GloomhavenVR.Core
{

/// <summary>Shared standalone-target text. The local builder imports this source unchanged.</summary>
public static class QuestText
{
    public static string Get(string key, bool german)
    {
        switch (key)
        {
            case "title": return german ? "GloomhavenVR – Quest-Test" : "GloomhavenVR – Quest test";
            case "diagnostic": return german ? "Technischer Hardware-Test; noch kein spielbarer Port" : "Hardware diagnostic; not yet a playable port";
            case "dummy": return german ? "DUMMY-PROFIL · lokaler Entwicklungsbuild" : "DUMMY PROFILE · local development build";
            case "mr": return german ? "A: Mixed Reality umschalten" : "A: Toggle mixed reality";
            case "recenter": return german ? "B: Tisch neu platzieren" : "B: Place the table again";
            case "save": return german ? "X: lokalen Speicher testen" : "X: Test local storage";
            case "language": return german ? "Y: English" : "Y: Deutsch";
            case "guildmaster": return "Guildmaster";
            case "workshop": return "Steam Workshop";
            case "excluded": return german ? "In der Quest-Version nicht verfügbar." : "Unavailable in the Quest version.";
            case "tracking": return german ? "Controller verfolgen und auf den Tisch zeigen" : "Track controllers and point at the table";
            case "mrFailed": return german ? "Passthrough nicht bereit; VR bleibt aktiv" : "Passthrough is not ready; VR remains active";
            case "savePassed": return german ? "Lokaler Speicher erfolgreich gelesen" : "Local storage read succeeded";
            case "saveFailed": return german ? "Lokaler Speichertest fehlgeschlagen" : "Local storage test failed";
            case "saveMissing": return german ? "Noch kein gespeicherter Hardware-Test" : "No saved hardware test yet";
            case "profileMissing": return german ? "Eingebettetes Profil fehlt oder ist ungültig" : "Embedded profile is missing or invalid";
            case "modelMissing": return german ? "Originalmodell noch nicht im Testbuild enthalten" : "Original model is not included in this test build yet";
            case "model": return german ? "Originalmodell · Shaderdarstellung noch zu prüfen" : "Original model · shader appearance requires verification";
            case "storage": return german ? "Speicherprüfung" : "Storage test";
            case "performance": return german ? "Bildrate" : "Frame rate";
            case "steamId": return german ? "Steam-ID" : "Steam ID";
            case "mrActive": return german ? "Mixed Reality aktiv" : "Mixed reality active";
            case "vrActive": return german ? "VR aktiv" : "VR active";
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Quest text key");
        }
    }
}
}
