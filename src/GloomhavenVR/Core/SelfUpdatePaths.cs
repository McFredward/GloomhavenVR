using System;
using System.IO;
using System.Threading;
using BepInEx;

namespace GloomhavenVR.Core;

/// <summary>
/// Where a staged update lives, and who takes it away again.
///
/// <para>WHY <c>BepInEx/GloomhavenVR-update/</c>. It has to be on the same VOLUME as the install
/// (a cross-volume move is a copy, and the applier's job is to be as close to atomic as a batch
/// file gets), it has to be somewhere the user can find and delete by hand, and it must NOT be
/// somewhere BepInEx scans. BepInEx 5 walks <c>BepInEx/plugins</c> and <c>BepInEx/patchers</c>
/// recursively and nothing else, so a sibling folder under <c>BepInEx/</c> is inert: the staged
/// copy of GloomhavenVR.dll sitting there is never loaded, never locked, and disappears with
/// BepInEx if the user removes the mod the manual way.</para>
///
/// <para>WHO CLEANS UP. Three layers, in order of how normal they are:
/// <list type="number">
/// <item>the applier deletes <c>staged/</c> and the downloaded zip itself, right after a
/// successful copy — that is the bulk of the ~70 MB;</item>
/// <item>the NEXT start of the game reads <see cref="PendingMarkerFile"/>, and if the version
/// written there is the version now running, the update took: the whole folder goes, backup
/// included. This is the confirmation gate — the backup outlives the install until the install has
/// PROVEN itself by booting;</item>
/// <item>anything left over for more than <see cref="StaleDays"/> days is removed regardless, so a
/// machine that lost power mid-update does not carry a 140 MB folder forever.</item>
/// </list>
/// A failed update keeps the folder on purpose: <c>update.log</c> in it is the only account of what
/// the copy did, and <c>backup/</c> is the user's own way back.</para>
/// </summary>
internal static class SelfUpdatePaths
{
    /// <summary>Folder name under BepInEx/. Also the string a user is told to delete.</summary>
    internal const string StagingFolderName = "GloomhavenVR-update";

    /// <summary>Marker naming the version the staged install was supposed to produce.</summary>
    internal const string PendingMarkerFile = "pending.txt";

    /// <summary>Leftovers older than this are swept even without a confirmation.</summary>
    internal const int StaleDays = 14;

    /// <summary>The Gloomhaven install folder — the one holding GH.exe and BepInEx/.</summary>
    internal static string GameRoot => Paths.GameRootPath;

    /// <summary>Root of everything this feature ever writes.</summary>
    internal static string StagingRoot => Path.Combine(Paths.BepInExRootPath, StagingFolderName);

    /// <summary>Where the release zip is unpacked. Mirrors the zip: BepInEx/... plus INSTALL.txt.</summary>
    internal static string StagedTree => Path.Combine(StagingRoot, "staged");

    /// <summary>Copy of the CURRENT install taken before anything is overwritten.</summary>
    internal static string BackupRoot => Path.Combine(StagingRoot, "backup");

    /// <summary>The verified download.</summary>
    internal static string DownloadZip => Path.Combine(StagingRoot, "download.zip");

    /// <summary>The download while it is still arriving. Never verified, never installed.</summary>
    internal static string PartialZip => Path.Combine(StagingRoot, "download.zip.part");

    /// <summary>The batch file that does the actual replacement, after the game is gone.</summary>
    internal static string ApplyScript => Path.Combine(StagingRoot, "apply-update.cmd");

    /// <summary>What the applier writes. The only account of a copy that happened with no game running.</summary>
    internal static string ApplyLog => Path.Combine(StagingRoot, "update.log");

    /// <summary>Marker file path — see <see cref="PendingMarkerFile"/>.</summary>
    internal static string PendingMarker => Path.Combine(StagingRoot, PendingMarkerFile);

    /// <summary>The two folders whose contents the mod owns and the applier backs up.</summary>
    internal static string InstalledPluginDir =>
        Path.Combine(Paths.BepInExRootPath, "plugins", "GloomhavenVR");

    /// <summary>See <see cref="InstalledPluginDir"/>.</summary>
    internal static string InstalledPatcherDir =>
        Path.Combine(Paths.BepInExRootPath, "patchers", "GloomhavenVR");

    /// <summary>
    /// Throw away any previous staging and recreate an empty one. Returns false with a reason when
    /// the folder cannot be prepared — a reason the caller shows, because at this point the user has
    /// pressed "Updaten" and deserves to know why nothing is happening.
    /// </summary>
    internal static bool ResetStaging(out string error)
    {
        error = string.Empty;
        try
        {
            if (Directory.Exists(StagingRoot))
                Directory.Delete(StagingRoot, recursive: true);
            Directory.CreateDirectory(StagingRoot);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Look at what a previous update attempt left behind and dispose of it — on a BACKGROUND
    /// THREAD, because a confirmed update leaves roughly 140 MB to delete and the main thread is
    /// the one drawing frames. Nothing in here touches a Unity API, so the thread is safe; it is
    /// fire-and-forget and every path is inside one catch, so a locked file cannot reach a caller.
    /// </summary>
    internal static void SweepLeftoversAsync()
    {
        try
        {
            ThreadPool.QueueUserWorkItem(static _ => SweepLeftovers());
        }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"leftover sweep could not be queued: {e.Message}");
        }
    }

    private static void SweepLeftovers()
    {
        try
        {
            if (!Directory.Exists(StagingRoot))
                return;

            string staged = ReadPendingVersion();
            bool confirmed = staged.Length > 0
                && string.Equals(staged, BuildInfo.Version, StringComparison.Ordinal);
            bool stale = DateTime.UtcNow - Directory.GetLastWriteTimeUtc(StagingRoot)
                > TimeSpan.FromDays(StaleDays);

            if (confirmed || stale)
            {
                Directory.Delete(StagingRoot, recursive: true);
                VRLog.Info("SelfUpdate", confirmed
                    ? $"UPDATE INSTALL: confirmed — running {BuildInfo.Version}, the version the "
                      + $"staged install promised; removed {StagingFolderName} with its backup."
                    : $"UPDATE INSTALL: removed a {StaleDays}-day-old {StagingFolderName} folder "
                      + "that never confirmed. Nothing was installed from it.");
                return;
            }

            // Kept on purpose. Named at Warnings level exactly once, because THIS one is a real
            // fault: an update was staged, the game came back, and it is not the promised version.
            VRLog.Warn("SelfUpdate",
                $"UPDATE INSTALL: staged version '{staged}' but this build is {BuildInfo.Version} — "
                + $"the update did not take. Kept {StagingRoot} for inspection: update.log says what "
                + "the copy did, and backup/ holds the install exactly as it was before it ran.");
        }
        catch (Exception e)
        {
            // Housekeeping. Nothing downstream depends on it, so it never escalates.
            VRLog.Info("SelfUpdate", $"leftover sweep skipped: {e.Message}");
        }
    }

    /// <summary>The version a previous attempt staged, or "" when there is no readable marker.</summary>
    private static string ReadPendingVersion()
    {
        try
        {
            return File.Exists(PendingMarker) ? File.ReadAllText(PendingMarker).Trim() : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
