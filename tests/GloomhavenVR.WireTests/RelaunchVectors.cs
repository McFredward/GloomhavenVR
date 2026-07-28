using GloomhavenVR.Preload;

namespace GloomhavenVR.WireTests;

/// <summary>
/// The exact command line the preloader hands to <c>cmd.exe</c> when it closes and reopens the game
/// once, after writing the graphics-jobs keys into boot.config.
///
/// <para>WHY THIS IS TESTED AT ALL, when it is "just string concatenation". It runs at the one
/// moment the mod deliberately kills the game. A missing quote does not produce a wrong result — it
/// produces a game that never comes back, on a player's machine, with no way to tell them what went
/// wrong because the process that would have logged it is already gone. There is no safe place to
/// discover a defect in this string, so it is pinned here.</para>
///
/// <para>The Steam app id is the one value that reaches the command line unquoted, so the vectors
/// below include what happens when it is not a number.</para>
/// </summary>
internal static class RelaunchVectors
{
    private const string Exe = @"C:\Games\Gloomhaven\GH.exe";

    /// <summary>The wait, spelled exactly as <see cref="RelaunchCommand.DelaySeconds"/> implies.</summary>
    private const string Wait = "ping -n 6 127.0.0.1 >nul";

    internal static void Run(Harness t)
    {
        // The delay is a real constant with a real reason (outlast our own shutdown), so the
        // literal above is pinned to it rather than to a number someone can quietly change.
        t.Case("relaunch/delay");
        t.Equal(5, RelaunchCommand.DelaySeconds, "delay seconds");
        t.True(Wait.Contains($"-n {RelaunchCommand.DelaySeconds + 1} "),
               "ping count must be DelaySeconds+1 (ping's first packet is immediate)");

        // ---- Steam: go back through Steam so overlay/playtime/controller bindings attach --------
        t.Case("relaunch/steam");
        t.Equal($"{Wait} & start \"\" \"steam://rungameid/780290\"",
                RelaunchCommand.Build(Exe, "780290", new[] { Exe }, out string? dropped),
                "steam launch uses the rungameid url, not the exe");
        t.True(dropped == null, "nothing dropped on the steam path");

        // A Steam id is the ONLY thing interpolated without quotes. If it is ever not a number we
        // must fall back to the executable rather than paste it into a shell command.
        t.Case("relaunch/steam-id-not-a-number");
        t.Equal($"{Wait} & start \"\" \"{Exe}\"",
                RelaunchCommand.Build(Exe, "780290 & del /q *", new[] { Exe }, out dropped),
                "a non-numeric SteamGameId is refused, not pasted into the command");
        t.Equal($"{Wait} & start \"\" \"{Exe}\"",
                RelaunchCommand.Build(Exe, "", new[] { Exe }, out dropped),
                "empty SteamGameId falls back to the executable");
        t.Equal($"{Wait} & start \"\" \"{Exe}\"",
                RelaunchCommand.Build(Exe, null, new[] { Exe }, out dropped),
                "absent SteamGameId falls back to the executable");
        t.True(!RelaunchCommand.IsAllDigits(""), "empty string is not a number");
        t.True(!RelaunchCommand.IsAllDigits("12a"), "trailing letter is not a number");
        t.True(RelaunchCommand.IsAllDigits("780290"), "a real app id is a number");

        // ---- direct start: the player's own launch options must survive -------------------------
        t.Case("relaunch/launch-options");
        t.Equal($"{Wait} & start \"\" \"{Exe}\" -force-d3d11",
                RelaunchCommand.Build(Exe, null, new[] { Exe, "-force-d3d11" }, out dropped),
                "a launch option is carried over (INSTALL.txt tells players to use this one)");
        t.True(dropped == null, "nothing dropped for a plain option");

        t.Equal($"{Wait} & start \"\" \"{Exe}\" -logfile \"C:\\My Logs\\gh.txt\"",
                RelaunchCommand.Build(Exe, null, new[] { Exe, "-logfile", @"C:\My Logs\gh.txt" }, out dropped),
                "an argument containing a space is quoted, the one without is not");

        // ---- the executable path itself ---------------------------------------------------------
        t.Case("relaunch/exe-path-with-spaces");
        const string spaced = @"D:\Program Files (x86)\Steam\steamapps\common\Gloomhaven\GH.exe";
        t.Equal($"{Wait} & start \"\" \"{spaced}\"",
                RelaunchCommand.Build(spaced, null, new[] { spaced }, out dropped),
                "the default Steam install path stays a single quoted token");

        // ---- anything cmd would act on means NO arguments, never a mangled command line ---------
        t.Case("relaunch/shell-metacharacter");
        t.Equal($"{Wait} & start \"\" \"{Exe}\"",
                RelaunchCommand.Build(Exe, null, new[] { Exe, "-nice", "-evil&del" }, out dropped),
                "one dangerous argument drops ALL arguments rather than half a shell command");
        t.Equal("-evil&del", dropped, "the offending argument is reported so the player can be told");

        foreach (string meta in new[] { "a\"b", "a|b", "a<b", "a>b", "a^b", "a%b" })
        {
            RelaunchCommand.Build(Exe, null, new[] { Exe, meta }, out dropped);
            t.Equal(meta, dropped, $"'{meta}' is refused");
        }

        // ---- argv[0] is the executable and is never repeated as an argument ---------------------
        t.Case("relaunch/argv0");
        t.Equal($"{Wait} & start \"\" \"{Exe}\"",
                RelaunchCommand.Build(Exe, null, new[] { Exe }, out dropped),
                "argv[0] is skipped, so the exe never appears twice");
    }
}
