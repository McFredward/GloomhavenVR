namespace GloomhavenVR.Preload;

/// <summary>
/// The command line that closes and reopens the game once, built as PURE STRING WORK with no
/// BepInEx, no environment and no process access — which is the whole point of it living here
/// rather than inside <see cref="Patcher"/>.
///
/// <para>WHY IT IS SEPARATE. A quoting mistake in this string is invisible until it happens on a
/// player's machine, at the one moment the mod deliberately kills the game — the worst possible
/// place to find out. Split out like this, the exact command can be asserted by
/// <c>scripts/wire-tests.sh</c>, which compiles THIS FILE into the test assembly (the same
/// source-link trick the wire files use, so the two can never drift).</para>
/// </summary>
internal static class RelaunchCommand
{
    /// <summary>
    /// Seconds the relauncher waits before starting the game. It must outlast this process's own
    /// shutdown: a process cannot restart itself, and for a moment both would exist — a game with a
    /// single-instance check answers that by killing the NEW one, leaving the player with nothing.
    /// </summary>
    internal const int DelaySeconds = 5;

    /// <summary>Characters cmd.exe would act on rather than pass through to the game.</summary>
    private static readonly char[] ShellMetaCharacters = { '"', '&', '|', '<', '>', '^', '%' };

    /// <summary>
    /// Build the argument for <c>cmd.exe /c</c>.
    /// </summary>
    /// <param name="exe">Full path of the running game executable.</param>
    /// <param name="steamGameId">
    /// Value of the <c>SteamGameId</c> environment variable, which the Steam client sets on the
    /// process it launches. When present we go back through Steam, so the overlay, the controller
    /// bindings and the playtime counter attach exactly as they normally would. Absent (null or
    /// empty) on GOG or a direct start, and then the executable is started directly.
    /// </param>
    /// <param name="argv">
    /// The full command line as <c>Environment.GetCommandLineArgs()</c> returns it — index 0 is the
    /// executable and is skipped here.
    /// </param>
    /// <param name="droppedArgument">
    /// The launch option that forced every argument to be dropped, or null when all were carried
    /// over. Reported by the caller so a player who loses one is told, rather than left guessing.
    /// </param>
    internal static string Build(string exe, string? steamGameId, string[] argv, out string? droppedArgument)
    {
        droppedArgument = null;

        // 'ping' and not 'timeout': timeout refuses to run when stdin is redirected, which is
        // exactly the situation a process started by a launcher can be in. -n counts the first
        // ping as immediate, hence the +1.
        string wait = $"ping -n {DelaySeconds + 1} 127.0.0.1 >nul";

        if (!string.IsNullOrEmpty(steamGameId) && IsAllDigits(steamGameId!))
            return $"{wait} & start \"\" \"steam://rungameid/{steamGameId}\"";

        // 'start ""' — the empty title is required, otherwise cmd reads the quoted path AS the
        // window title and starts nothing at all.
        return $"{wait} & start \"\" \"{exe}\"{FormatArguments(argv, out droppedArgument)}";
    }

    /// <summary>
    /// The player's own launch options, carried over (<c>-force-d3d11</c> is a real example from
    /// the install guide). One argument cmd would interpret means NONE are passed: losing an option
    /// is recoverable and visible, running half of one as a shell command is neither.
    /// </summary>
    private static string FormatArguments(string[] argv, out string? droppedArgument)
    {
        droppedArgument = null;
        var args = new System.Text.StringBuilder();

        for (int i = 1; i < argv.Length; i++)
        {
            if (argv[i].IndexOfAny(ShellMetaCharacters) >= 0)
            {
                droppedArgument = argv[i];
                return string.Empty;
            }

            args.Append(' ');
            args.Append(argv[i].IndexOf(' ') >= 0 ? $"\"{argv[i]}\"" : argv[i]);
        }

        return args.ToString();
    }

    /// <summary>
    /// Guards the one value that reaches the command line unquoted. char.IsDigit would also accept
    /// non-ASCII digits, which have no business in a Steam app id.
    /// </summary>
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
}
