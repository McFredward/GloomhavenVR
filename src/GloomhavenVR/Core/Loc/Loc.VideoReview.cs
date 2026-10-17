using System;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Core;

/// <summary>Player-facing movie review names and lifecycle feedback, including refusal and retry.</summary>
internal static partial class Loc
{
    private static string VideoText(string english, string german) =>
        CurrentLanguage == "German" ? german : english;

    internal static string VideoReviewHeading => VideoText("Videos", "Videos");
    internal static string VideoReviewHint => VideoText(
        "Play a video in this scenario's movable video window. Close VR Options to watch it unobstructed. The other players can see it too. Nothing is unlocked and your progress stays unchanged.",
        "Spiele ein Video im beweglichen Videofenster dieses Szenarios ab. Schließe VR Optionen für freie Sicht. Auch die Mitspieler sehen es. Es wird nichts freigeschaltet und dein Spielfortschritt bleibt unverändert.");
    internal static string VideoReviewClose => VideoText("Close test video", "Testvideo schließen");
    internal static string VideoReviewReady => VideoText("Choose a video to play.", "Wähle ein Video zum Abspielen.");
    internal static string VideoReviewClosed => VideoText("Video closed. Choose a video to play again.", "Video geschlossen. Wähle ein Video zum erneuten Abspielen.");
    internal static string VideoReviewScenarioRequired => VideoText("Start a scenario before playing a test video.", "Starte ein Szenario, bevor du ein Testvideo abspielst.");
    internal static string VideoReviewNativeBusy => VideoText("Wait for the current game video to finish, then try again.", "Warte, bis das aktuelle Spielvideo endet, und versuche es erneut.");
    internal static string VideoReviewAlreadyPlaying => VideoText("A test video is open. Close it before choosing another.", "Ein Testvideo ist geöffnet. Schließe es, bevor du ein anderes wählst.");
    internal static string VideoReviewUnavailable => VideoText("Video unavailable. Check your mod installation and try again.", "Video nicht verfügbar. Prüfe deine Mod-Installation und versuche es erneut.");
    internal static string VideoReviewFailed => VideoText("The video could not play. Try again.", "Das Video konnte nicht abgespielt werden. Versuche es erneut.");
    internal static string VideoReviewPreparing(string name) => VideoText("Preparing: ", "Wird vorbereitet: ") + name;
    internal static string VideoReviewPlaying(string name) => VideoText("Playing: ", "Wird abgespielt: ") + name;
    internal static string VideoReviewPlay(string key) => VideoText("Play: ", "Abspielen: ") + VideoReviewName(key);

    private static string VideoHeroName(string model, string english, string german)
    {
        string fallback = VideoText(english, german);
        try
        {
            // The normal class-information window uses this same native LocKey.
            // Read it without requiring that the class is unlocked or selected.
            foreach (CCharacterClass hero in CharacterClassManager.Classes)
                if (hero.CharacterModel.ToString() == model && !string.IsNullOrEmpty(hero.LocKey))
                    return Game(hero.LocKey, fallback);
        }
        catch (Exception)
        {
            // The menu can exist before the game's character/localization bank is ready.
        }
        return fallback;
    }

    internal static string VideoReviewName(string key) => key switch
    {
        "CP_Intro/GH_CP_Intro.mov" => VideoText("GloomhavenVR intro", "GloomhavenVR-Intro"),
        "Heroes/BeastTyrant.mov" => VideoHeroName("BeastTyrant", "Beast Tyrant", "Bestientyrann"),
        "Heroes/Berserker.mov" => VideoHeroName("Berserker", "Berserker", "Berserkerin"),
        "Heroes/Doomstalker.mov" => VideoHeroName("Doomstalker", "Doomstalker", "Unheilbringer"),
        "Heroes/Elementalist.mov" => VideoHeroName("Elementalist", "Elementalist", "Elementarist"),
        "Heroes/Nightshroud.mov" => VideoHeroName("Nightshroud", "Nightshroud", "Nachtschleier"),
        "Heroes/Plagueherald.mov" => VideoHeroName("Plagueherald", "Plagueherald", "Seuchenherold"),
        "Heroes/Quartermaster.mov" => VideoHeroName("Quartermaster", "Quartermaster", "Quartiermeister"),
        "Heroes/Sawbones.mov" => VideoHeroName("Sawbones", "Sawbones", "Knochenflicker"),
        "Heroes/Soothsinger.mov" => VideoHeroName("Soothsinger", "Soothsinger", "Wahrsängerin"),
        "Heroes/Summoner.mov" => VideoHeroName("Summoner", "Summoner", "Beschwörerin"),
        "Heroes/Sunkeeper.mov" => VideoHeroName("Sunkeeper", "Sunkeeper", "Sonnenhüterin"),
        "Ambient/Ambient_02_Cave_01.mov" => VideoText("Cave 1 ambience", "Höhle 1 – Umgebung"),
        "Ambient/Ambient_03_Forest_01.mov" => VideoText("Forest ambience", "Wald – Umgebung"),
        "Ambient/Ambient_04_Cave_02.mov" => VideoText("Cave 2 ambience", "Höhle 2 – Umgebung"),
        "Ambient/Ambient_05_Corpsewood_01.mov" => VideoText("Corpsewood ambience", "Leichenwald – Umgebung"),
        "Ambient/Ambient_06_Town_01.mov" => VideoText("Town ambience", "Stadt – Umgebung"),
        "Ambient/Ambient_Crypt_01.mov" => VideoText("Crypt ambience", "Krypta – Umgebung"),
        // The spelling is the native movie's identity; it never appears in the menu.
        "Ambient/Amibient_07_Dungeon_01.mov" => VideoText("Dungeon ambience", "Verlies – Umgebung"),
        "Embedded/TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best.mov" => VideoText("Twin Sails logo", "Twin-Sails-Logo"),
        _ => VideoText("Video", "Video"),
    };
}
