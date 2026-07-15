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
/// </summary>
internal static class ModuleConfig
{
    /// <summary>Create (or reopen) the module's own config file. <paramref name="module"/> is lowercased.</summary>
    public static ConfigFile Create(string module) =>
        new(Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.{module.ToLowerInvariant()}.cfg"),
            saveOnInit: true);
}
