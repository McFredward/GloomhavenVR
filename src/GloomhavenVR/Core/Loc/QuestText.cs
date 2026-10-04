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
            case "dlcPurchaseOnPc": return german ? "Kaufe diesen DLC auf deinem PC und baue anschließend die Quest-APK neu." : "Buy this DLC on your PC, then rebuild the Quest APK.";
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
            case "navigation": return german ? "Linker Stick: bewegen · Rechter Stick: drehen / Höhe · Trigger: auswählen" : "Left stick: move · Right stick: turn / height · Trigger: select";
            case "startupTitle": return german ? "Startdiagnose des Originalspiels" : "Original game startup diagnostic";
            case "startupPending": return german ? "Spielstart wird vorbereitet" : "Preparing game startup";
            case "loadingPreparing": return german ? "Spielstart vorbereiten" : "Preparing game startup";
            case "loadingReading": return german ? "Inhalte laden" : "Loading content";
            case "loadingUnpackingFile": return german ? "Aktuelle Datei bereitstellen" : "Preparing current file";
            case "loadingVerifyingFile": return german ? "Aktuelle Datei prüfen" : "Checking current file";
            case "loadingVerifying": return german ? "Inhalte prüfen" : "Checking content";
            case "loadingStarting": return german ? "Spiel starten" : "Starting the game";
            case "loadingOverall": return german ? "Vorbereitung" : "Preparation";
            case "loadingStep": return german ? "Schritt {0} von {1}" : "Step {0} of {1}";
            case "loadingFile": return german ? "Datei {0} von {1}" : "File {0} of {1}";
            case "startupCheckingMod": return german ? "VR-Dateien werden geprüft" : "Checking VR resources";
            case "startupCopyingMod": return german ? "VR-Dateien werden aus der APK gelesen" : "Reading VR resources from the APK";
            case "startupExtractingMod": return german ? "VR-Dateien werden bereitgestellt und geprüft" : "Preparing and verifying VR resources";
            case "startupStartingMod": return german ? "VR-Mod wird gestartet" : "Starting the VR mod";
            case "startupCheckingContent": return german ? "Spieldateien werden geprüft" : "Checking game files";
            case "startupExtractingContent": return german ? "Spieldateien werden entpackt" : "Extracting game files";
            case "startupAddressables": return german ? "Originale Startinhalte werden geladen" : "Loading original startup assets";
            case "startupLoadingOriginal": return german ? "Originalspiel wird gestartet" : "Starting the original game";
            case "startupOriginalLoaded": return german ? "Originalstart geladen" : "Original startup loaded";
            case "startupFailed": return german ? "Spielstart angehalten" : "Game startup stopped";
            case "startupDiagnosticScope": return german ? "Originalmenüs; Kampagne, Mod und Crossplay bleiben unbestätigt." : "Original menus; campaign, mod and crossplay remain unverified.";
            case "startupVoiceUnavailable": return german ? "Sprachchat ist in dieser Startdiagnose nicht verfügbar." : "Voice chat is unavailable in this startup diagnostic.";
            case "probeAim": return german ? "Zielpose links / rechts" : "Left / right aim pose";
            case "probeBattery": return german ? "Hardware-Prüfungen" : "Hardware checks";
            case "probeMaterials": return german ? "Materialien und Atlas" : "Materials and atlas";
            case "probeAnimation": return german ? "Animation und Farbreferenz" : "Animation and colour reference";
            case "probeTiming": return german ? "Zeitmessung und App-Zustand" : "Timing and app lifecycle";
            case "probeNextPage": return german ? "Nächste Prüfseite" : "Next check page";
            case "probeNextMaterial": return german ? "Nächstes Material" : "Next material";
            case "probeToggleRender": return german ? "Beleuchtung wechseln" : "Switch lighting";
            case "probeToggleAnimation": return german ? "Animation pausieren/fortsetzen" : "Pause/resume animation";
            case "probeToggleScale": return german ? "Modellgröße wechseln" : "Change model size";
            case "probeCapture": return german ? "Messstand speichern" : "Save snapshot";
            case "probeLit": return german ? "Beleuchtete Annäherung" : "Lit approximation";
            case "probeAlbedo": return german ? "Nur Grundfarbe" : "Albedo only";
            case "probePaused": return german ? "Pausiert" : "Paused";
            case "probeRunning": return german ? "Läuft" : "Running";
            case "probeNormalSize": return german ? "Tischgröße" : "Tabletop size";
            case "probeInspectionSize": return german ? "Prüfgröße" : "Inspection size";
            case "probeSourceShader": return german ? "Original-Shader" : "Source shader";
            case "probeRuntimeShader": return german ? "Aktueller Shader" : "Current shader";
            case "probeTexture": return german ? "Grundfarbtextur" : "Albedo texture";
            case "probeTint": return german ? "Farbton" : "Tint";
            case "probeUv": return german ? "UV-Skalierung / Versatz" : "UV scale / offset";
            case "probeParityUnknown": return german ? "Original-Shaderdarstellung ist unbestätigt" : "Original shader appearance is unverified";
            case "probeNoTexture": return german ? "Keine Grundfarbtextur" : "No albedo texture";
            case "probeUnavailable": return german ? "Nicht verfügbar" : "Unavailable";
            case "probeFrameTiming": return german ? "Bildzeit ms: Min / Mittel / p95 / Max" : "Frame ms: min / avg / p95 / max";
            case "probeLongFrames": return german ? "Lange Bilder (>1,5 Bildperioden)" : "Long frames (>1.5 refresh periods)";
            case "probeRefresh": return german ? "XR-Bildfrequenz" : "XR refresh";
            case "probeManagedMemory": return german ? "Verwalteter Speicher" : "Managed memory";
            case "probeTracking": return german ? "Kopf / links / rechts erfasst" : "Head / left / right tracking";
            case "probeLifecycle": return german ? "Fokus- / Pausenereignisse" : "Focus / pause events";
            case "probeStateSaved": return german ? "Messstand gespeichert" : "Snapshot saved";
            case "probeStateFailed": return german ? "Messstand nicht gespeichert" : "Snapshot save failed";
            case "probeDiagnosticScope": return german ? "Diese kleine Diagnose misst keine Spielleistung." : "This small diagnostic does not measure full-game performance.";
            case "probeCheckAtlas": return german ? "Atlas und Modell vergleichen; Beleuchtung und jedes Material prüfen." : "Compare the atlas and model; switch lighting and inspect each material.";
            case "probeCheckStereo": return german ? "Farben mit jedem Auge vergleichen; Modell pausiert prüfen." : "Compare these colours with each eye; inspect the model while paused.";
            case "probeCheckResume": return german ? "Systemmenü öffnen und schließen, schlafen und wecken; Tracking und Bildzeiten vergleichen." : "Open and close the system menu, sleep and wake, then compare tracking and timing.";
            case "probeYes": return german ? "ja" : "yes";
            case "probeNo": return german ? "nein" : "no";
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Quest text key");
        }
    }
}
}
