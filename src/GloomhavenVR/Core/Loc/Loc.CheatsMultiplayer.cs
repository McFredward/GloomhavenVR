namespace GloomhavenVR.Core;

internal static partial class Loc
{
    internal static string CheatsMultiplayerHelp => CurrentLanguage == "German"
        ? "Im Mehrspieler kann der Host Cheats für die ganze Gruppe auslösen. Für Gold, Freischalten, Räume und Voraussetzungen brauchen alle den aktuellen Mod. Ein normaler Szenario-Sieg funktioniert auch mit unmodifizierten Mitspielern."
        : "In multiplayer, the host can apply cheats to the whole party. Gold, unlocks, rooms and requirements need everyone on the current mod. A normal scenario win also works with unmodded players.";
    internal static string CheatQueued => CurrentLanguage == "German"
        ? "Für die Gruppe angefordert — wird mit der nächsten Spielaktion ausgeführt"
        : "Requested for the party — executes through the next game action";
}
