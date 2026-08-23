using System;
using System.Text;

namespace GloomhavenVR.Core;

/// <summary>
/// The batch file that actually replaces the mod's DLLs, built as PURE STRING WORK with no
/// file, process or BepInEx access — the same split, and for the same reason, as
/// <c>GloomhavenVR.Preload/RelaunchCommand.cs</c>: a quoting mistake in a script that runs
/// exactly once, with the game deliberately dead, is invisible until it happens on the user's
/// machine at the worst possible moment.
///
/// <para>WHY A SEPARATE PROCESS AT ALL. A running process cannot overwrite the assemblies it has
/// loaded. <c>GloomhavenVR.dll</c> is held open by BepInEx from <c>BepInEx/plugins/GloomhavenVR/</c>
/// and <c>GloomhavenVR.Preload.dll</c> from <c>BepInEx/patchers/GloomhavenVR/</c> for the whole
/// life of the process, so "download and push into the game files, then restart" cannot happen in
/// that order in-process. The mod downloads, verifies and STAGES; this script — a child of the
/// game that outlives it — waits for the game to be gone, then copies, then starts it again.</para>
///
/// <para>WHY THERE IS NOT ONE ABSOLUTE PATH IN THE GENERATED SCRIPT. cmd.exe reads a .cmd in the
/// OEM console codepage, not UTF-8. A user folder like <c>C:\Users\Jörg\…</c> written out as a
/// literal would arrive at robocopy as mojibake and the copy would fail — on exactly the machines
/// least able to diagnose it. Every path in the script is instead derived from <c>%~dp0</c>, which
/// cmd expands from the file's own location at runtime, so the script body is pure ASCII whatever
/// the install path looks like. The only interpolated values left are digits (a pid, a Steam app
/// id), the executable's FILE NAME and the version string — all ASCII-checked below.</para>
///
/// <para>WHY IT CANNOT LEAVE A BROKEN INSTALL. In order: it refuses to copy at all while the game
/// is still running; it takes a full copy of both mod folders into <c>backup/</c> BEFORE the first
/// overwrite and aborts without touching anything if that backup fails; it uses robocopy WITHOUT
/// <c>/MIR</c>, so it only ever adds and overwrites and can never delete a file the new zip happens
/// not to carry (the 70 MB asset bundle being the one that matters); and if the install copy fails
/// part-way it restores the backup over the top before relaunching. The floor under all of that:
/// the mod is not part of the game's own load path, so even a completely destroyed
/// <c>BepInEx/plugins/GloomhavenVR/</c> means BepInEx logs a failed plugin and Gloomhaven starts
/// flat and vanilla. There is no state this can reach in which the game does not start.</para>
/// </summary>
internal static class SelfUpdateApplyScript
{
    /// <summary>
    /// How long the applier waits for the game to disappear before giving up. Generous, because
    /// the alternative to waiting is copying over files that are still locked; and giving up is
    /// harmless — nothing has been touched at that point.
    /// </summary>
    internal const int MaxWaitSeconds = 180;

    /// <summary>Seconds between the game vanishing and the first copy: handles a shutdown that has
    /// released the process but not yet flushed every handle.</summary>
    internal const int SettleSeconds = 3;

    /// <summary>Characters cmd.exe would act on rather than pass through to the game.</summary>
    private static readonly char[] ShellMetaCharacters = { '"', '&', '|', '<', '>', '^', '%' };

    /// <summary>
    /// Build the applier. Returns false with a reason when a value cannot be embedded safely — and
    /// a refusal here is a refusal to install: the caller shows the reason and leaves the install
    /// exactly as it is, which is always better than writing a script whose quoting is a guess.
    /// </summary>
    /// <param name="pid">Process id of the running game; the script waits for it to be gone.</param>
    /// <param name="executableName">FILE NAME of the game executable (not its path) — used to start
    /// the game again when it was not launched through Steam.</param>
    /// <param name="steamGameId">The <c>SteamGameId</c> environment variable the Steam client sets
    /// on the process it launches. Present means going back through Steam, so overlay, controller
    /// bindings and playtime attach exactly as they normally would.</param>
    /// <param name="argv">The command line as <c>Environment.GetCommandLineArgs()</c> returns it;
    /// index 0 is the executable and is skipped.</param>
    /// <param name="version">Version being installed — echoed into update.log, nothing else.</param>
    /// <param name="script">The finished script text.</param>
    /// <param name="refusal">Why nothing was built, when the result is false.</param>
    /// <param name="droppedArgument">A launch option that forced ALL of them to be dropped, or null.</param>
    internal static bool TryBuild(int pid, string executableName, string? steamGameId, string[] argv,
        string version, out string script, out string refusal, out string? droppedArgument)
    {
        script = string.Empty;
        refusal = string.Empty;
        droppedArgument = null;

        if (pid <= 0)
        {
            refusal = "the game's own process id could not be read";
            return false;
        }
        if (!IsPlainAscii(executableName) || executableName.Length == 0
            || executableName.IndexOfAny(ShellMetaCharacters) >= 0
            || executableName.IndexOf('\\') >= 0 || executableName.IndexOf('/') >= 0)
        {
            refusal = $"the executable name '{executableName}' cannot be written into a batch file safely";
            return false;
        }
        if (!IsPlainAscii(version))
        {
            refusal = "the release version string is not plain ASCII";
            return false;
        }

        string relaunch = BuildRelaunchLine(executableName, steamGameId, argv, out droppedArgument);
        int waitTicks = MaxWaitSeconds; // one 'ping -n 2' tick is ~1 second

        var sb = new StringBuilder(4096);
        sb.Append("@echo off\r\n");
        sb.Append("rem GloomhavenVR self-update applier. Generated by the mod, runs once.\r\n");
        sb.Append("rem It waits for the game to exit, backs the current mod up, copies the staged\r\n");
        sb.Append("rem release over it, restores the backup if that fails, and starts the game again.\r\n");
        sb.Append("rem Safe to delete together with this whole folder.\r\n");
        sb.Append("setlocal enableextensions\r\n");
        sb.Append($"set \"PID={pid.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"\r\n");
        // %~dp0 is this file's own folder, WITH a trailing backslash: <game>\BepInEx\<staging>\
        sb.Append("set \"HERE=%~dp0\"\r\n");
        sb.Append("set \"ROOT=%HERE%..\\..\"\r\n");
        sb.Append("set \"LOG=%HERE%update.log\"\r\n");
        sb.Append("set \"PLUGDST=%ROOT%\\BepInEx\\plugins\\GloomhavenVR\"\r\n");
        sb.Append("set \"PATCHDST=%ROOT%\\BepInEx\\patchers\\GloomhavenVR\"\r\n");
        sb.Append("set \"RC=/E /R:5 /W:2 /NFL /NDL /NJH /NP\"\r\n");
        sb.Append("\r\n");
        sb.Append($"echo ==== GloomhavenVR update to {version} ==== >>\"%LOG%\"\r\n");
        sb.Append("echo started %DATE% %TIME%, waiting for pid %PID% >>\"%LOG%\"\r\n");
        sb.Append("\r\n");
        sb.Append("set /a TRIES=0\r\n");
        sb.Append(":wait\r\n");
        // 'ping' and not 'timeout': timeout refuses to run when stdin is redirected, which is
        // exactly the situation a process started by a launcher can be in.
        sb.Append("tasklist /FI \"PID eq %PID%\" /NH 2>nul | find \"%PID%\" >nul\r\n");
        sb.Append("if errorlevel 1 goto gone\r\n");
        sb.Append("set /a TRIES=TRIES+1\r\n");
        sb.Append($"if %TRIES% GEQ {waitTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)} goto stillrunning\r\n");
        sb.Append("ping -n 2 127.0.0.1 >nul\r\n");
        sb.Append("goto wait\r\n");
        sb.Append("\r\n");
        sb.Append(":stillrunning\r\n");
        sb.Append($"echo ABORT: the game was still running after {MaxWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds. NOTHING was copied. >>\"%LOG%\"\r\n");
        sb.Append("exit /b 10\r\n");
        sb.Append("\r\n");
        sb.Append(":gone\r\n");
        sb.Append("echo the game has exited >>\"%LOG%\"\r\n");
        sb.Append($"ping -n {(SettleSeconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} 127.0.0.1 >nul\r\n");
        sb.Append("\r\n");
        sb.Append("echo ---- backing up the current install ---- >>\"%LOG%\"\r\n");
        sb.Append("robocopy \"%PLUGDST%\" \"%HERE%backup\\plugins\\GloomhavenVR\" %RC% >>\"%LOG%\" 2>&1\r\n");
        sb.Append("if errorlevel 8 goto backupfailed\r\n");
        sb.Append("robocopy \"%PATCHDST%\" \"%HERE%backup\\patchers\\GloomhavenVR\" %RC% >>\"%LOG%\" 2>&1\r\n");
        sb.Append("if errorlevel 8 goto backupfailed\r\n");
        sb.Append("\r\n");
        sb.Append("echo ---- installing ---- >>\"%LOG%\"\r\n");
        // No /MIR anywhere: this only adds and overwrites. A file the new zip does not carry --
        // the 70 MB asset bundle above all -- is left exactly where it is.
        sb.Append("robocopy \"%HERE%staged\\BepInEx\" \"%ROOT%\\BepInEx\" %RC% >>\"%LOG%\" 2>&1\r\n");
        sb.Append("if errorlevel 8 goto installfailed\r\n");
        sb.Append("if exist \"%HERE%staged\\INSTALL.txt\" copy /Y \"%HERE%staged\\INSTALL.txt\" \"%ROOT%\\INSTALL.txt\" >>\"%LOG%\" 2>&1\r\n");
        sb.Append($"echo OK: installed {version} >>\"%LOG%\"\r\n");
        sb.Append("goto relaunch\r\n");
        sb.Append("\r\n");
        sb.Append(":backupfailed\r\n");
        sb.Append("echo ABORT: the current install could not be backed up, so nothing was overwritten. >>\"%LOG%\"\r\n");
        sb.Append("echo The mod is untouched and still the version you were running. >>\"%LOG%\"\r\n");
        sb.Append("goto relaunch\r\n");
        sb.Append("\r\n");
        sb.Append(":installfailed\r\n");
        sb.Append("echo FAILED part-way through the copy - restoring the backup >>\"%LOG%\"\r\n");
        sb.Append("robocopy \"%HERE%backup\\plugins\\GloomhavenVR\" \"%PLUGDST%\" %RC% >>\"%LOG%\" 2>&1\r\n");
        sb.Append("robocopy \"%HERE%backup\\patchers\\GloomhavenVR\" \"%PATCHDST%\" %RC% >>\"%LOG%\" 2>&1\r\n");
        sb.Append("echo ROLLBACK done: every file of the previous install has been written back. >>\"%LOG%\"\r\n");
        sb.Append("goto relaunch\r\n");
        sb.Append("\r\n");
        sb.Append(":relaunch\r\n");
        sb.Append("echo ---- starting the game ---- >>\"%LOG%\"\r\n");
        sb.Append("ping -n 3 127.0.0.1 >nul\r\n");
        sb.Append(relaunch);
        sb.Append("\r\n");
        // staged/ and the zip are the bulk of the ~70 MB and are of no further use. backup/,
        // update.log and pending.txt stay: the mod's next start reads pending.txt, and only
        // deletes the backup once the version it promised is the version actually running.
        sb.Append("rd /s /q \"%HERE%staged\" 2>nul\r\n");
        sb.Append("del /q \"%HERE%download.zip\" 2>nul\r\n");
        sb.Append("echo finished %DATE% %TIME% >>\"%LOG%\"\r\n");
        sb.Append("exit /b 0\r\n");

        script = sb.ToString();
        return true;
    }

    /// <summary>
    /// The one line that starts the game again. Steam app id wins when the game was launched
    /// through Steam, so the overlay and playtime attach the way they normally would.
    /// </summary>
    private static string BuildRelaunchLine(string executableName, string? steamGameId, string[] argv,
        out string? droppedArgument)
    {
        droppedArgument = null;
        if (!string.IsNullOrEmpty(steamGameId) && IsAllDigits(steamGameId!))
            return $"start \"\" \"steam://rungameid/{steamGameId}\"";

        // 'start ""' -- the empty title is required, otherwise cmd reads the quoted path AS the
        // window title and starts nothing at all. '/D' sets the new process's working directory to
        // the install folder: this script runs from the staging folder, and a game started with
        // THAT as its cwd would look for relative paths one level too deep.
        return $"start \"\" /D \"%ROOT%\" \"%ROOT%\\{executableName}\""
            + FormatArguments(argv, out droppedArgument);
    }

    /// <summary>
    /// The user's own launch options, carried over (<c>-force-d3d11</c> is a real example from the
    /// install guide). One argument cmd would interpret means NONE are passed: losing an option is
    /// recoverable and visible, running half of one as a shell command is neither.
    /// </summary>
    private static string FormatArguments(string[] argv, out string? droppedArgument)
    {
        droppedArgument = null;
        var args = new StringBuilder();

        for (int i = 1; i < argv.Length; i++)
        {
            if (argv[i].IndexOfAny(ShellMetaCharacters) >= 0 || !IsPlainAscii(argv[i]))
            {
                droppedArgument = argv[i];
                return string.Empty;
            }
            args.Append(' ');
            args.Append(argv[i].IndexOf(' ') >= 0 ? $"\"{argv[i]}\"" : argv[i]);
        }

        return args.ToString();
    }

    /// <summary>Guards every value that reaches the command line unquoted. char.IsDigit would also
    /// accept non-ASCII digits, which have no business in a Steam app id.</summary>
    internal static bool IsAllDigits(string value)
    {
        if (value.Length == 0)
            return false;
        foreach (char c in value)
        {
            if (c < '0' || c > '9')
                return false;
        }
        return true;
    }

    /// <summary>Printable 7-bit ASCII only — see the codepage note in the class comment.</summary>
    internal static bool IsPlainAscii(string value)
    {
        foreach (char c in value)
        {
            if (c < ' ' || c > '~')
                return false;
        }
        return true;
    }
}
