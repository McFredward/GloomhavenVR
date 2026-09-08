using System;
using BepInEx.Configuration;

namespace GloomhavenVR.Core;

/// <summary>
/// The ONE dial the self-update feature owns, and the decision it feeds.
///
/// <para>THE RULE IT OBEYS: "Settings may only configure optional content or comfort, never
/// something that can break the game." This entry cannot break anything — it only decides whether
/// a DEV build is allowed to ask GitHub a question it would otherwise not ask. A release build
/// checks regardless and ignores it; switching it on cannot install anything by itself (the user
/// still has to press "Updaten"), and switching it off can never leave a half-installed state
/// because it is read before the check, not during an install.</para>
///
/// <para>WHY IT LIVES IN <c>[Dev]</c> ON THE MAIN CONFIG FILE AND NOT IN A FILE OF ITS OWN. The
/// in-VR "Erweitert" browser is a reflection walk over <see cref="ModuleConfig"/>
/// (WorldUI/ConfigCatalog), and <c>ConfigCatalog.TopicOf</c> already maps main-module
/// <c>[Dev]</c> to <c>ConfigTopic.Diagnostics</c> — so the row appears on the Erweitert
/// diagnostics page with ZERO catalog edits, exactly the anti-drift property that walk exists for.
/// A second .cfg file for a single debug toggle would also be a new file in every player's config
/// folder for a switch no player is meant to touch.</para>
/// </summary>
internal static class SelfUpdateConfig
{
    /// <summary>
    /// Shipped default of <c>[Dev] UpdateCheckOnDevBuilds</c>: OFF.
    ///
    /// <para>The VALUE lives where every other shipped default does —
    /// <c>Defaults.Plugin.cs</c>'s <c>UpdateCheckOnDevBuilds</c>, carrying the
    /// <c>// =&gt; [Dev] UpdateCheckOnDevBuilds</c> annotation <c>scripts/rebase-defaults.py</c>
    /// joins against a tester's cfg. This name is kept as the forwarder because it is what
    /// <see cref="Bind"/> and this file's own doc read, and because a default that is only
    /// reachable through the section it belongs to is harder to find than one named after its
    /// feature.</para>
    /// </summary>
    internal const bool UpdateCheckOnDevBuildsDefault = Defaults.UpdateCheckOnDevBuilds;

    /// <summary>Bound into the main plugin config; null until <see cref="Bind"/> has run.</summary>
    internal static ConfigEntry<bool>? UpdateCheckOnDevBuilds { get; private set; }

    /// <summary>
    /// Bind the dial into the MAIN plugin config file (<c>dev.gloomhavenvr.cfg</c>), reached
    /// through the module registry rather than through <c>Plugin</c> — the registry is the only
    /// handle on that file this lane owns, and it is the same handle the config browser reads.
    /// Idempotent: BepInEx returns the existing entry for a re-bind of the same key.
    /// </summary>
    internal static void Bind()
    {
        if (UpdateCheckOnDevBuilds != null)
            return;

        ConfigFile? main = null;
        foreach (System.Collections.Generic.KeyValuePair<string, ConfigFile> pair in ModuleConfig.Snapshot())
        {
            if (string.Equals(pair.Key, ModuleConfig.MainModule, StringComparison.Ordinal))
            {
                main = pair.Value;
                break;
            }
        }

        if (main == null)
        {
            // Plugin.Awake registers the main file before any module Init runs, so this is a
            // "the world changed" case, not an expected one. Not fatal: no entry means the dev
            // gate stays shut, which is the safe side of this switch.
            VRLog.Warn("SelfUpdate", "the main config file is not in the module registry — "
                + "[Dev] UpdateCheckOnDevBuilds not bound; update checks stay OFF on dev builds.");
            return;
        }

        UpdateCheckOnDevBuilds = main.Bind(
            "Dev", "UpdateCheckOnDevBuilds", UpdateCheckOnDevBuildsDefault,
            // 584 characters — ConfigCatalog clips a row description at 620, and this text IS the
            // English hover in the Erweitert browser.
            "DEV BUILDS ONLY, and off by default. A release build always checks GitHub once, when "
            + "the main menu comes up, whether a newer release exists, and shows a notice window "
            + "with \"Ignore\" and \"Update\" if there is one. A dev build never does, because the "
            + "version it reports is not a released one and every start would offer to \"update\" "
            + "it. Switch this on to exercise that path on a dev build; it takes effect "
            + "immediately, without a restart, while you are standing in the main menu. It changes "
            + "nothing on a release build. If GitHub cannot be reached, no window appears and "
            + "nothing waits on it.");
    }

    /// <summary>
    /// <see cref="BuildInfo.IsDevBuild"/> through a property, deliberately.
    ///
    /// <para>It is a <c>const bool</c> on the contract, so <c>if (BuildInfo.IsDevBuild)</c> is
    /// constant-folded at every call site and the branch the compiler decides is dead raises
    /// CS0162 "Unreachable code detected". The build gate allows EXACTLY six warnings; a version
    /// flag that adds one per call site would spend that budget on nothing. Reading it through a
    /// property is not folded, costs nothing at runtime, and keeps both branches real code that
    /// the compiler still checks.</para>
    /// </summary>
    internal static bool IsDevBuild => BuildInfo.IsDevBuild;

    /// <summary>
    /// True when the update check may run, with the reason either way — the reason is what the
    /// <c>UPDATE CHECK:</c> falsifier line prints, so "it did not run" is never a silent outcome.
    /// </summary>
    internal static bool IsCheckEnabled(out string reason)
    {
        // The notice is a world-space window. Without VR there is nowhere to put it, so asking
        // GitHub a question whose answer could not be shown is work for nothing — and this is the
        // term the falsifier prints when a flat-screen session sees no window.
        if (!VRSession.IsRunning)
        {
            reason = "VR is not running";
            return false;
        }

        if (!IsDevBuild)
        {
            reason = "release build";
            return true;
        }

        bool dial = UpdateCheckOnDevBuilds?.Value ?? UpdateCheckOnDevBuildsDefault;
        reason = dial
            ? "dev build, [Dev] UpdateCheckOnDevBuilds=true"
            : "dev build, [Dev] UpdateCheckOnDevBuilds=false";
        return dial;
    }
}
