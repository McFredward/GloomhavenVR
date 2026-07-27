using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace GloomhavenVR.Core;

/// <summary>
/// Canonical module-config factory (Phase 5, MISSION A.9). Every feature module that
/// needs its own settings binds them against a module-owned <see cref="ConfigFile"/>
/// created here — NOT against the main plugin config (<c>Plugin.Config</c>, reserved
/// for the cross-cutting [General]/[Rig]/[Hands]/[Compat]/[Dev] sections) and NOT via
/// <c>FindObjectOfType&lt;Plugin&gt;().Config</c> (the pre-P5 Board pattern, retired).
///
/// File naming: <c>BepInEx/config/dev.gloomhavenvr.&lt;module&gt;.cfg</c> — the plugin
/// GUID plus a lowercase module suffix, matching the files Cards and Comfort already
/// shipped (<c>dev.gloomhavenvr.cards.cfg</c>, <c>dev.gloomhavenvr.comfort.cfg</c>).
///
/// Usage pattern (see BoardConfig/CardsConfig/WorldUIConfig):
/// <code>
///   private static ConfigFile? _file;
///   internal static void Bind()
///   {
///       if (_file != null) return;           // bind-once guard (same-instance re-Init)
///       _file = ModuleConfig.Create("board");
///       MyEntry = _file.Bind("Board", "Key", default, "description");
///   }
/// </code>
/// BepInEx auto-saves on every entry write, so panel/runtime changes persist for free.
///
/// <para>REGISTRY (2026-07, user: "Alle config einstellungen sollen im VR Menu anpassbar sein!").
/// Every file handed out here is also REMEMBERED, so the in-VR config browser
/// (<c>WorldUI/ConfigCatalog</c> → Debug ▸ Alle Einstellungen) can enumerate every bound
/// <see cref="ConfigEntryBase"/> without anyone maintaining a list of them. That is the whole
/// anti-drift property: a module that binds a new entry tomorrow — or a whole new module file —
/// shows up in the VR menu the moment it binds, because the browser reads THIS registry rather
/// than a hand-written table. The main plugin config is registered by <c>Plugin.Awake</c> under
/// the module name <see cref="MainModule"/>; it is not created here (BaseUnityPlugin owns it).</para>
/// </summary>
internal static class ModuleConfig
{
    /// <summary>Registry module name of the MAIN plugin config (<c>dev.gloomhavenvr.cfg</c>).</summary>
    internal const string MainModule = "main";

    /// <summary>
    /// Every config file the mod has opened, in creation order. Small and bounded (one entry per
    /// module — under two dozen), never removed from, so a consumer may hold the list across
    /// frames. Guarded by <see cref="Gate"/> for the (unlikely) case of two threads binding.
    /// </summary>
    private static readonly List<KeyValuePair<string, ConfigFile>> Files = new(24);

    private static readonly object Gate = new();

    /// <summary>Create (or reopen) the module's own config file. <paramref name="module"/> is lowercased.</summary>
    public static ConfigFile Create(string module)
    {
        var file = new ConfigFile(
            Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.{module.ToLowerInvariant()}.cfg"),
            saveOnInit: true);
        Register(module, file);
        return file;
    }

    /// <summary>
    /// Remember a config file the mod did not create through <see cref="Create"/> — today only the
    /// main plugin config, which BaseUnityPlugin owns. Idempotent per module name: a re-registration
    /// REPLACES the stored instance, so a module that reopened its file is never browsed through a
    /// stale handle.
    /// </summary>
    internal static void Register(string module, ConfigFile? file)
    {
        if (file == null || string.IsNullOrEmpty(module))
            return;
        string name = module.ToLowerInvariant();
        lock (Gate)
        {
            for (int i = 0; i < Files.Count; i++)
            {
                if (string.Equals(Files[i].Key, name, StringComparison.Ordinal))
                {
                    Files[i] = new KeyValuePair<string, ConfigFile>(name, file);
                    return;
                }
            }
            Files.Add(new KeyValuePair<string, ConfigFile>(name, file));
        }
    }

    /// <summary>
    /// Snapshot of the registry (module name → file). A COPY, so the caller can walk it while a
    /// module binds on another code path without an "collection modified" throw mid-walk.
    /// </summary>
    internal static KeyValuePair<string, ConfigFile>[] Snapshot()
    {
        lock (Gate)
            return Files.ToArray();
    }
}
