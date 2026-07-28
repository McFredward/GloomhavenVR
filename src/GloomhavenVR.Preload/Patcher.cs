using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace GloomhavenVR.Preload;

/// <summary>
/// BepInEx 5 preloader patcher entry point (lives in <c>BepInEx/patchers/GloomhavenVR/</c>).
///
/// We do not rewrite any game assembly — this patcher exists purely because it runs
/// <b>before Unity engine init</b>, which is the only moment at which OpenXR native
/// plugins and the <c>UnitySubsystems</c> manifest can be installed so the engine
/// picks them up on the same boot (no restart required). See
/// <c>.planning/ARCHITECTURE.md</c> §2 and <c>.planning/research/TOOLCHAIN.md</c> §5.2
/// (RepoXR/LCVR-1.3.2 pattern).
///
/// Shipping layout (see README "Install"):
/// <code>
/// BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll   (this assembly)
/// BepInEx/patchers/GloomhavenVR/Natives/UnityOpenXR.dll
/// BepInEx/patchers/GloomhavenVR/Natives/openxr_loader.dll
/// </code>
/// Installed at boot (idempotent, hash-compared):
/// <code>
/// GH_Data/Plugins/x86_64/UnityOpenXR.dll
/// GH_Data/Plugins/x86_64/openxr_loader.dll
/// GH_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json
/// </code>
///
/// HARD RULE: <see cref="Initialize"/> must never throw. Any failure here must degrade
/// to "VR unavailable" (the plugin's pre-flight check reports it) — never break the
/// vanilla game boot.
/// </summary>
public static class Patcher
{
    internal static readonly ManualLogSource Log = Logger.CreateLogSource("GloomhavenVR.Preload");

    /// <summary>
    /// OpenXR package version of the shipped set. Must match libs/Natives (fetch pin)
    /// and libs/RuntimeDeps (managed Unity.XR.OpenXR.dll) — TOOLCHAIN.md risk R5.
    /// </summary>
    private const string OpenXRPackageVersion = "1.10.0";

    /// <summary>Exact manifest content from TOOLCHAIN.md §5.2 (RepoXR ships the same).</summary>
    private const string SubsystemsManifestJson = @"{
  ""name"": ""OpenXR XR Plugin"",
  ""version"": """ + OpenXRPackageVersion + @""",
  ""libraryName"": ""UnityOpenXR"",
  ""displays"": [
    {
      ""id"": ""OpenXR Display""
    }
  ],
  ""inputs"": [
    {
      ""id"": ""OpenXR Input""
    }
  ]
}";

    private static readonly string[] NativeFiles = { "UnityOpenXR.dll", "openxr_loader.dll" };

    /// <summary>No assemblies are patched; empty list keeps the patcher contract valid.</summary>
    public static IEnumerable<string> TargetDLLs => [];

    /// <summary>Required by the BepInEx patcher contract; intentionally a no-op.</summary>
    public static void Patch(AssemblyDefinition _)
    {
        // Intentionally empty — we never modify game assemblies (see class docs).
    }

    /// <summary>Runs before any game assembly is loaded — earliest hook we get.</summary>
    public static void Initialize()
    {
        try
        {
            if (!IsVREnabledInConfig())
            {
                Log.LogInfo("[General] Enabled = false in dev.gloomhavenvr.cfg — skipping OpenXR runtime asset install (game stays vanilla).");
                return;
            }

            // Independent of the OpenXR asset install and deliberately BEFORE it: this one is
            // worth doing even if VR then fails to come up, and it must not be skipped because a
            // native copy errored.
            bool relaunch = EnsureGraphicsJobs();

            bool nativesOk = InstallNatives();
            bool manifestOk = InstallSubsystemsManifest();

            if (nativesOk && manifestOk)
            {
                WriteInstallMarker();
                Log.LogInfo($"OpenXR runtime assets ready (package {OpenXRPackageVersion}).");
            }
            else
            {
                Log.LogError("OpenXR runtime asset install incomplete — VR will be unavailable this session (game itself is unaffected).");
            }

            // LAST, deliberately: this ends the process. Doing it here rather than straight after
            // the boot.config write means the natives and the manifest are already installed, so
            // the relaunched process boots into a fully prepared install and its log is a clean
            // first run rather than a half-done one.
            if (relaunch)
                RelaunchOnce();
        }
        catch (Exception e)
        {
            // Never propagate: a broken preloader must not take the game down.
            Log.LogError($"Unexpected preloader failure — VR will be unavailable this session. {e}");
        }
    }

    /// <summary>
    /// <c>GH_Data/</c> — derived from BepInEx's ManagedPath (…/GH_Data/Managed)
    /// rather than hardcoding the product name.
    /// </summary>
    private static string DataPath => Path.GetDirectoryName(Paths.ManagedPath)!;

    /// <summary>Directory this patcher assembly was loaded from (BepInEx/patchers/GloomhavenVR/).</summary>
    private static string PatcherDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    /// <summary>
    /// Copy <c>UnityOpenXR.dll</c> + <c>openxr_loader.dll</c> (shipped next to this patcher
    /// under <c>Natives/</c>) into <c>GH_Data/Plugins/x86_64/</c>.
    /// Idempotent: SHA256-compared, skipped when identical, overwritten when different.
    /// </summary>
    private static bool InstallNatives()
    {
        string sourceDir = Path.Combine(PatcherDir, "Natives");
        string destDir = Path.Combine(DataPath, "Plugins", "x86_64");

        bool allOk = true;
        foreach (string name in NativeFiles)
        {
            try
            {
                string source = Path.Combine(sourceDir, name);
                string dest = Path.Combine(destDir, name);

                if (!File.Exists(source))
                {
                    Log.LogError($"Missing shipped native '{source}' — the mod package is incomplete " +
                                 "(re-install; developers: run scripts/fetch-natives.sh and scripts/deploy.ps1).");
                    allOk = false;
                    continue;
                }

                if (File.Exists(dest) && FileHashesEqual(source, dest))
                {
                    Log.LogDebug($"Native up to date: {dest}");
                    continue;
                }

                Directory.CreateDirectory(destDir);
                File.Copy(source, dest, overwrite: true);
                Log.LogInfo($"Installed native: {dest}");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to install native '{name}': {e}");
                allOk = false;
            }
        }

        return allOk;
    }

    /// <summary>
    /// Write <c>GH_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json</c>
    /// (version-matched to the shipped OpenXR package) so the engine registers the
    /// "OpenXR Display"/"OpenXR Input" subsystem descriptors at boot.
    /// Idempotent: content-compared, only rewritten when different.
    /// </summary>
    private static bool InstallSubsystemsManifest()
    {
        try
        {
            string dir = Path.Combine(DataPath, "UnitySubsystems", "UnityOpenXR");
            string manifest = Path.Combine(dir, "UnitySubsystemsManifest.json");

            if (File.Exists(manifest) && File.ReadAllText(manifest) == SubsystemsManifestJson)
            {
                Log.LogDebug($"Subsystems manifest up to date: {manifest}");
                return true;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(manifest, SubsystemsManifestJson);
            Log.LogInfo($"Installed subsystems manifest: {manifest}");
            return true;
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to install UnitySubsystemsManifest.json: {e}");
            return false;
        }
    }

    /// <summary>
    /// Record what was installed (version + file hashes + when) next to this patcher —
    /// diagnostic breadcrumb for the failure-triage table in docs/TESTING-P1.md.
    /// </summary>
    private static void WriteInstallMarker()
    {
        try
        {
            string destDir = Path.Combine(DataPath, "Plugins", "x86_64");
            var lines = new List<string>
            {
                "{",
                $"  \"openxrPackageVersion\": \"{OpenXRPackageVersion}\",",
                $"  \"installedUtc\": \"{DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}\",",
                "  \"files\": {"
            };
            for (int i = 0; i < NativeFiles.Length; i++)
            {
                string dest = Path.Combine(destDir, NativeFiles[i]);
                string comma = i < NativeFiles.Length - 1 ? "," : "";
                lines.Add($"    \"{NativeFiles[i]}\": \"{(File.Exists(dest) ? Sha256Of(dest) : "missing")}\"{comma}");
            }
            lines.AddRange(["  }", "}"]);

            File.WriteAllLines(Path.Combine(PatcherDir, "install-state.json"), lines);
        }
        catch (Exception e)
        {
            // Marker is purely diagnostic — never fail the install over it.
            Log.LogWarning($"Could not write install-state.json: {e.Message}");
        }
    }

    /// <summary>
    /// Read the plugin's own config file to honor the master switch. The BepInEx config
    /// API isn't reliably usable in patcher context (config binding belongs to the plugin),
    /// so this is a minimal INI scan of <c>BepInEx/config/dev.gloomhavenvr.cfg</c>.
    /// Missing file / unparsable content ⇒ enabled (first run creates the file later).
    /// </summary>
    private static bool IsVREnabledInConfig() => ReadConfigFlag("General", "Enabled", defaultValue: true);

    /// <summary>
    /// Read one boolean from <c>BepInEx/config/dev.gloomhavenvr.cfg</c> by section and key.
    /// A missing file, a missing key or unparsable content all yield
    /// <paramref name="defaultValue"/> — first run creates the file later, and a config the
    /// preloader cannot read must never change what the preloader does.
    /// </summary>
    private static bool ReadConfigFlag(string section, string key, bool defaultValue)
    {
        try
        {
            string cfg = Path.Combine(Paths.ConfigPath, "dev.gloomhavenvr.cfg");
            if (!File.Exists(cfg))
                return defaultValue;

            string? current = null;
            foreach (string raw in File.ReadAllLines(cfg))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0 || !string.Equals(current, section, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(eq + 1).Trim();
                return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            }

            return defaultValue;
        }
        catch (Exception e)
        {
            Log.LogWarning($"Could not read dev.gloomhavenvr.cfg ({e.Message}) — assuming "
                           + $"[{section}] {key} = {defaultValue}.");
            return defaultValue;
        }
    }

    // ==========================================================================================
    //  Graphics jobs — the single largest performance finding of the whole project
    // ==========================================================================================

    /// <summary>The two <c>boot.config</c> keys that together are what <c>-force-gfx-jobs native</c> does.</summary>
    private static readonly string[] GraphicsJobKeys = { "gfx-enable-gfx-jobs", "gfx-enable-native-gfx-jobs" };

    /// <summary>Copy of the ORIGINAL boot.config, written once. The user's manual escape hatch.</summary>
    private const string BootConfigBackupSuffix = ".gloomhavenvr-backup";

    /// <summary>
    /// Process environment variable carrying whether THIS SESSION is actually running with graphics
    /// jobs — "1"/"0", set from the boot.config value read BEFORE this patcher edits it.
    ///
    /// <para>It exists because the obvious alternative is wrong: the plugin cannot read boot.config
    /// and conclude anything about the running session, since by then this patcher may already have
    /// changed it. Process-scoped rather than a file, so it can never go stale and leaves nothing
    /// behind. Absent = this patcher did not run, and the plugin says so rather than guessing.</para>
    /// </summary>
    internal const string SessionStateVariable = "GLOOMHAVENVR_GFXJOBS_SESSION";

    /// <summary>
    /// TURN ON UNITY'S THREADED RENDER SUBMISSION, so the player does not have to know about a
    /// command-line flag.
    ///
    /// <para>WHY THIS IS HERE AND NOT IN THE PLUGIN. Six hardware sessions blamed the VR judder on
    /// the amount of work — resolution, MSAA, shadows, the depth prepass, the culling mask, per-pixel
    /// lights, draw-call count — and every one of those levers moved the frame by under 10 %. The
    /// actual wall was that Unity submits every draw call on ONE thread, the same thread that has to
    /// finish before a frame can be presented. Measured 2026-07-28 on the same scene, the same
    /// build, changing nothing but this: main-thread render loop <b>14.9 ms → 1.8 ms</b>, head
    /// camera <b>13.4 ms → 1.45 ms</b>, frame p50 <b>17.5 ms → 11.14 ms</b>, and the runtime went
    /// from locked at 45 Hz to a clean <b>90 Hz</b> with 4 spikes in 2668 frames. The user's report
    /// was "das Ghosting ist komplett verschwunden".</para>
    ///
    /// <para>It cannot be done from the plugin: the engine has chosen its job mode long before any
    /// managed mod code runs. Even here, in the preloader, we are too late for THIS boot — Unity
    /// reads <c>boot.config</c> before the mono runtime exists. So this writes the setting and it
    /// takes effect at the NEXT game start, which the log says plainly rather than leaving someone
    /// to wonder why the first run after installing looks unchanged.</para>
    ///
    /// <para>THIS TOUCHES A GAME FILE, which is the one thing this mod otherwise never does. What
    /// makes it defensible: it is two <c>key=value</c> lines in a plain-text engine config, it is
    /// idempotent, the original file is copied to <c>boot.config.gloomhavenvr-backup</c> before the
    /// first edit, an unknown key is ignored by Unity rather than fatal, and setting
    /// <c>[Core] EnableGraphicsJobs = false</c> writes the keys back to 0 on the next start. No game
    /// CONTENT, save, campaign or asset is read or written.</para>
    ///
    /// <para>IF THE GAME EVER FAILS TO BOOT because of this, the plugin cannot fix it — it never
    /// runs. That is why the backup exists and why its full path is in the log: restore
    /// <c>boot.config.gloomhavenvr-backup</c> over <c>boot.config</c> and the game is exactly as it
    /// shipped.</para>
    /// </summary>
    /// <returns>
    /// True when the file was just changed to ENABLE graphics jobs, i.e. this session is running
    /// without them but the next one would have them — the only case in which relaunching buys the
    /// player anything. <see cref="RelaunchOnce"/> decides separately whether it is allowed to.
    /// </returns>
    private static bool EnsureGraphicsJobs()
    {
        try
        {
            bool wanted = ReadConfigFlag("Core", "EnableGraphicsJobs", defaultValue: true);

            // An explicit -force-gfx-jobs on the command line is the player's own decision and
            // outranks the file either way, so editing the file would only be confusing noise.
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-force-gfx-jobs", StringComparison.OrdinalIgnoreCase))
                    continue;
                Log.LogInfo("Graphics jobs: '-force-gfx-jobs' is on the command line — leaving "
                            + "boot.config alone, the launch option wins.");
                return false;
            }

            string bootConfig = Path.Combine(DataPath, "boot.config");
            if (!File.Exists(bootConfig))
            {
                Log.LogWarning($"Graphics jobs: no boot.config at '{bootConfig}' — cannot enable "
                               + "threaded render submission automatically. Add '-force-gfx-jobs native' "
                               + "to the game's launch options instead; it is worth roughly 12 ms per "
                               + "frame in a scenario.");
                return false;
            }

            string[] lines = File.ReadAllLines(bootConfig);

            // WHAT THIS SESSION IS ACTUALLY RUNNING WITH — read BEFORE the edit, because the engine
            // read the file before this patcher existed. Published to the process so the plugin can
            // report the truth instead of reading the file back and seeing our own write.
            // (2026-07-28: the plugin did exactly that and logged "Graphics jobs: ON" for a session
            // that was running without them. A diagnostic that reports the wrong state is worse than
            // no diagnostic — this is the handshake that makes it impossible.)
            bool runningWithJobs = ReadKey(lines, "gfx-enable-native-gfx-jobs") == "1";
            Environment.SetEnvironmentVariable(SessionStateVariable, runningWithJobs ? "1" : "0");

            // Running with them IS the goal state, so this is the moment the relaunch budget is
            // handed back. Without this a single pathological launch would spend the budget
            // permanently and no later install could ever auto-restart again.
            if (runningWithJobs)
                ClearRelaunchAttempts();

            string desired = wanted ? "1" : "0";
            if (!RewriteKeys(lines, desired, out string[] updated))
            {
                Log.LogDebug($"Graphics jobs already {(wanted ? "enabled" : "disabled")} in boot.config.");
                return false;
            }

            // Back up the file as it was BEFORE this mod ever touched it, once. Never overwritten:
            // a second backup taken after our own edit would record our edit as the original.
            string backup = bootConfig + BootConfigBackupSuffix;
            if (!File.Exists(backup))
                File.Copy(bootConfig, backup);

            // Write via a temp file and replace, so an interrupted write cannot leave the engine
            // with a half-written boot.config — the one failure here that would stop the game
            // starting at all.
            string temp = bootConfig + ".gloomhavenvr-tmp";
            File.WriteAllLines(temp, updated);
            File.Copy(temp, bootConfig, overwrite: true);
            File.Delete(temp);

            if (wanted)
            {
                Log.LogInfo("Graphics jobs ENABLED in boot.config (gfx-enable-gfx-jobs + "
                            + "gfx-enable-native-gfx-jobs = 1). This moves Unity's draw-call "
                            + "submission off the main thread and was measured at main-thread render "
                            + "14.9 ms → 1.8 ms, 45 Hz → 90 Hz on 2026-07-28 hardware. IT TAKES EFFECT "
                            + $"AT THE NEXT GAME START, not this one. Original saved as '{backup}' — "
                            + "restore that over boot.config to undo by hand, or set "
                            + "[Core] EnableGraphicsJobs = false to have this undo it for you.");
                return true;
            }

            Log.LogInfo("Graphics jobs DISABLED in boot.config ([Core] EnableGraphicsJobs = false). "
                        + "Takes effect at the next game start.");
            return false;
        }
        catch (Exception e)
        {
            // Same hard rule as the rest of this patcher: never take the game down. A failure here
            // costs performance, nothing else.
            Log.LogWarning($"Graphics jobs: could not update boot.config ({e.Message}). Add "
                           + "'-force-gfx-jobs native' to the game's launch options instead.");
            return false;
        }
    }

    // ==========================================================================================
    //  The one automatic restart
    // ==========================================================================================

    /// <summary>
    /// Marks a process that IS the automatic restart, so it can never start another one. Set on the
    /// relauncher's environment and inherited by the game it starts. Deliberately separate from the
    /// persistent counter: this one cannot go stale, and it holds even if the disk write failed.
    /// </summary>
    private const string RelaunchedVariable = "GLOOMHAVENVR_RELAUNCHED";

    /// <summary>How many automatic restarts may be spent before the setting has ever taken hold.</summary>
    private const int MaxAutoRelaunches = 2;

    /// <summary>Persistent counter, in the patcher's own folder. Cleared the moment we boot WITH jobs.</summary>
    private const string RelaunchMarkerFile = "gfxjobs-relaunch.txt";

    /// <summary>Time given to BepInEx's disk log listener to flush before the process is killed.</summary>
    private const int LogFlushGraceMs = 1200;

    /// <summary>
    /// RESTART THE GAME EXACTLY ONCE so the player never has to.
    ///
    /// <para>WHY THIS IS THE ONLY WAY. Unity reads <c>boot.config</c> before the mono runtime
    /// exists, so no mod code — this patcher included — can affect the boot it is running in. The
    /// setting is written for the NEXT start, and until now that meant telling the player to quit
    /// and start again. This does it for them.</para>
    ///
    /// <para>WHY IT SPAWNS A DETACHED RELAUNCHER INSTEAD OF STARTING THE GAME DIRECTLY. A process
    /// cannot restart itself: whatever it starts overlaps with its own shutdown, and a game with a
    /// single-instance check answers that overlap by killing the NEW process — leaving the player
    /// with no game at all, which is far worse than the restart being avoided. So a short-lived
    /// <c>cmd.exe</c> waits for this process to be gone and only then starts the game.</para>
    ///
    /// <para>WHY IT CANNOT LOOP. Four independent brakes, in order: the player can turn it off
    /// (<c>[Core] AutoRestartForGraphicsJobs</c>); the relaunched process carries
    /// <see cref="RelaunchedVariable"/> and refuses to relaunch again; a persistent counter caps it
    /// at <see cref="MaxAutoRelaunches"/> across launches; and the counter is cleared only by the
    /// success condition itself — actually booting with graphics jobs on. In the normal case the
    /// counter reaches 1, the next boot clears it, and it is never touched again.</para>
    ///
    /// <para>Nothing of the player's is at risk at this point: this runs during engine init, before
    /// any game assembly is loaded, so no save, campaign or scenario exists to lose.</para>
    /// </summary>
    private static void RelaunchOnce()
    {
        const string manual = "RESTART THE GAME ONCE to actually get them: they have just been "
            + "written into boot.config, and the engine read that file before this mod existed, so "
            + "THIS session is still running without them. Nothing is wrong; the next start picks "
            + "them up.";

        try
        {
            if (!ReadConfigFlag("Core", "AutoRestartForGraphicsJobs", defaultValue: true))
            {
                Log.LogWarning($"Graphics jobs: {manual} (Restarting for you is switched off in "
                               + "[Core] AutoRestartForGraphicsJobs.)");
                return;
            }

            if (Environment.GetEnvironmentVariable(RelaunchedVariable) == "1")
            {
                Log.LogWarning("Graphics jobs: this process already IS the automatic restart, and "
                               + "boot.config STILL had to be written — so the write is not "
                               + $"sticking. Not restarting again. {manual}");
                return;
            }

            string marker = Path.Combine(PatcherDir, RelaunchMarkerFile);
            int attempts = ReadRelaunchAttempts(marker);
            if (attempts >= MaxAutoRelaunches)
            {
                Log.LogWarning($"Graphics jobs: restarted automatically {attempts} times already "
                               + "without the setting ever taking hold — something outside this mod "
                               + "is reverting boot.config. Giving up rather than looping forever. "
                               + $"Delete '{marker}' to let it try once more. {manual}");
                return;
            }

            // Everything that can fail is done BEFORE the counter is spent, so a failure here does
            // not quietly eat one of the two attempts.
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string command = RelaunchCommand.Build(
                exe,
                Environment.GetEnvironmentVariable("SteamGameId"),
                Environment.GetCommandLineArgs(),
                out string? droppedArgument);

            if (droppedArgument != null)
            {
                Log.LogWarning("Graphics jobs: a launch option contains a character the relauncher "
                               + $"cannot pass on safely ('{droppedArgument}') — restarting WITHOUT "
                               + "any launch options. Add them again by hand if the game needs them.");
            }

            var starter = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            starter.EnvironmentVariables[RelaunchedVariable] = "1";

            Log.LogWarning("Graphics jobs: RESTARTING THE GAME NOW, once. They were just written "
                           + "into boot.config and the engine had already read that file, so this "
                           + "session cannot have them — rather than asking you to quit and start "
                           + "again, the game closes and comes back by itself in about "
                           + $"{RelaunchCommand.DelaySeconds} seconds. This happens once, after installing "
                           + "or updating the mod. To stop it happening at all, set "
                           + "[Core] AutoRestartForGraphicsJobs = false.");
            Log.LogInfo($"Relaunch command: cmd /c {command}");

            // THE COUNTER BEFORE THE START, AND THE START LAST. Both orderings are load-bearing.
            // Counter first: if starting the relauncher fails we would rather have spent an attempt
            // than have no record at all. Start last: from the moment it returns, a relaunch IS
            // coming in a few seconds, so no statement may follow that could throw and send us down
            // the catch — which returns WITHOUT killing this process, and the player would end up
            // with two games running. Logging is therefore already done above.
            File.WriteAllText(marker, (attempts + 1).ToString(CultureInfo.InvariantCulture));
            Process.Start(starter);
        }
        catch (Exception e)
        {
            Log.LogWarning($"Graphics jobs: the automatic restart could not be arranged ({e.Message}). "
                           + $"Nothing is broken and the game carries on. {manual}");
            return;
        }

        // Past the point of no return: the relauncher is already counting down, so this process
        // MUST end. Give the log a moment to reach disk first — the line explaining what just
        // happened is the only thing the player has to go on.
        try
        {
            Thread.Sleep(LogFlushGraceMs);
            Process.GetCurrentProcess().Kill();
        }
        catch (Exception e)
        {
            Log.LogWarning($"Graphics jobs: could not close this process ({e.Message}) — exiting instead.");
            Environment.Exit(0);
        }
    }

    private static int ReadRelaunchAttempts(string marker)
    {
        try
        {
            return File.Exists(marker)
                   && int.TryParse(File.ReadAllText(marker).Trim(), NumberStyles.Integer,
                                   CultureInfo.InvariantCulture, out int n)
                ? n
                : 0;
        }
        catch
        {
            // Unreadable counter must not be read as "plenty of attempts left" NOR block a genuine
            // first restart; the env-var brake still makes a loop impossible within one chain.
            return 0;
        }
    }

    private static void ClearRelaunchAttempts()
    {
        try
        {
            string marker = Path.Combine(PatcherDir, RelaunchMarkerFile);
            if (File.Exists(marker))
                File.Delete(marker);
        }
        catch
        {
            // Cosmetic bookkeeping only — never worth a log line at boot, never worth failing over.
        }
    }

    /// <summary>Value of one boot.config key as the file currently stands, or null when absent.</summary>
    private static string? ReadKey(string[] lines, string key)
    {
        foreach (string line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line.Substring(eq + 1).Trim();
        }
        return null;
    }

    /// <summary>
    /// Set every key in <see cref="GraphicsJobKeys"/> to <paramref name="value"/>, appending any
    /// that are absent. Returns false when the file already says exactly that, so an unchanged file
    /// is never rewritten (and no backup is taken for a no-op). Every other line is preserved
    /// byte-for-byte — this must never reformat a file the engine parses.
    /// </summary>
    private static bool RewriteKeys(string[] lines, string value, out string[] updated)
    {
        var result = new List<string>(lines.Length + GraphicsJobKeys.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (string line in lines)
        {
            int eq = line.IndexOf('=');
            string key = eq > 0 ? line.Substring(0, eq).Trim() : string.Empty;

            bool ours = false;
            for (int i = 0; i < GraphicsJobKeys.Length; i++)
                ours |= string.Equals(key, GraphicsJobKeys[i], StringComparison.OrdinalIgnoreCase);

            if (!ours)
            {
                result.Add(line);
                continue;
            }

            seen.Add(key);
            string rewritten = key + "=" + value;
            changed |= !string.Equals(line, rewritten, StringComparison.Ordinal);
            result.Add(rewritten);
        }

        foreach (string key in GraphicsJobKeys)
        {
            if (seen.Contains(key))
                continue;
            result.Add(key + "=" + value);
            changed = true;
        }

        updated = result.ToArray();
        return changed;
    }

    private static bool FileHashesEqual(string a, string b) => Sha256Of(a) == Sha256Of(b);

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
