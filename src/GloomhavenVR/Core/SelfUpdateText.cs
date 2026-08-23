using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// German/English strings for the update window.
///
/// <para>INTEGRATOR NOTE: these belong in <c>Core/Loc.cs</c>'s embedded table beside
/// <c>ver_mismatch_title</c> and friends, and are shaped exactly like it (id → language name →
/// text, English fallback, keyed on <see cref="Loc.CurrentLanguage"/>) so they can be moved there
/// verbatim. They are here only because <c>Loc.cs</c> is not this lane's file. Lifting them is a
/// copy of <see cref="Table"/> into <c>Loc.Build()</c> and a replacement of
/// <see cref="SelfUpdateText.T"/> with <c>Loc.Mod</c>.</para>
/// </summary>
internal static class SelfUpdateText
{
    /// <summary>Look up a string in the current game language, English fallback, id as last resort.</summary>
    internal static string T(string id)
    {
        if (Table.TryGetValue(id, out Dictionary<string, string>? byLanguage))
        {
            if (byLanguage.TryGetValue(Loc.CurrentLanguage, out string? text) && text.Length > 0)
                return text;
            if (byLanguage.TryGetValue("English", out string? english) && english.Length > 0)
                return english;
        }
        return id;
    }

    private static Dictionary<string, string> Pair(string en, string de) =>
        new(2) { ["English"] = en, ["German"] = de };

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new(16)
    {
        ["upd_title"] = Pair("Update available", "Update verfügbar"),

        // {0} installed version, {1} available version, {2} download size in MB
        ["upd_body"] = Pair(
            "A newer version of GloomhavenVR has been released.\n\n"
            + "Installed:  {0}\nAvailable:  {1}\nDownload:   {2} MB\n\n"
            + "\"Update\" downloads it, replaces the mod files and restarts the game. "
            + "Your saves, your campaign and your settings are not touched.",
            "Eine neuere Version von GloomhavenVR ist erschienen.\n\n"
            + "Installiert:  {0}\nVerfügbar:    {1}\nDownload:     {2} MB\n\n"
            + "\"Updaten\" lädt sie herunter, ersetzt die Mod-Dateien und startet das Spiel neu. "
            + "Deine Spielstände, deine Kampagne und deine Einstellungen bleiben unberührt."),

        ["upd_ignore"] = Pair("Ignore", "Ignorieren"),
        ["upd_update"] = Pair("Update", "Updaten"),
        ["upd_cancel"] = Pair("Cancel", "Abbrechen"),
        ["upd_close"] = Pair("Close", "Schließen"),

        // {0} target version
        ["upd_working"] = Pair(
            "Installing GloomhavenVR {0}.\n\n"
            + "The game closes and comes back by itself when the files have been replaced. "
            + "Do not close it by hand while the bar is running.",
            "GloomhavenVR {0} wird installiert.\n\n"
            + "Das Spiel schließt sich und kommt von selbst zurück, sobald die Dateien ersetzt "
            + "sind. Bitte schließe es nicht selbst, solange der Balken läuft."),

        // {0} downloaded MB, {1} total MB
        ["upd_downloading"] = Pair("Downloading … {0} of {1} MB", "Lädt herunter … {0} von {1} MB"),
        ["upd_verifying"] = Pair("Checking the archive …", "Archiv wird geprüft …"),
        ["upd_extracting"] = Pair("Unpacking …", "Wird entpackt …"),
        ["upd_restarting"] = Pair("Restarting the game …", "Spiel wird neu gestartet …"),

        // {0} the term that failed
        ["upd_failed"] = Pair(
            "The update was not installed.\n\n{0}\n\n"
            + "Nothing was changed — the version you were running is still installed. "
            + "You can always install the update by hand from the GitHub releases page.",
            "Das Update wurde nicht installiert.\n\n{0}\n\n"
            + "Es wurde nichts verändert — die bisherige Version ist weiterhin installiert. "
            + "Du kannst das Update jederzeit von Hand über die GitHub-Releases-Seite einspielen."),

        ["upd_still_running"] = Pair(
            "Everything is ready. The game did not close by itself — please close it now, and it "
            + "will come back with the new version installed.",
            "Alles ist bereit. Das Spiel hat sich nicht von selbst geschlossen — bitte schließe es "
            + "jetzt; es kommt mit der neuen Version zurück."),
    };
}
