using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GloomhavenVR.Core;

/// <summary>
/// Loads the mod-shipped managed XR assemblies (<c>Unity.XR.Management</c>,
/// <c>Unity.XR.CoreUtils</c>, <c>Unity.XR.OpenXR</c>, …) from
/// <c>BepInEx/plugins/GloomhavenVR/RuntimeDeps/</c> via <see cref="Assembly.LoadFile"/> —
/// the LCVR <c>PreloadRuntimeDependencies</c> pattern. Must run before any method
/// that references XR types is JIT-compiled (Mono binds assembly references against
/// already-loaded assemblies by name).
/// </summary>
internal static class RuntimeDepsLoader
{
    private static bool _loaded;

    /// <summary>Directory the plugin assembly lives in (BepInEx/plugins/GloomhavenVR/).</summary>
    private static string PluginDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    internal static string RuntimeDepsDir => Path.Combine(PluginDir, "RuntimeDeps");

    /// <summary>
    /// Load every DLL in RuntimeDeps/. Returns false (with an actionable log) when the
    /// directory is missing/empty or an assembly fails to load — callers must then leave
    /// VR off; the game keeps running flat.
    /// </summary>
    internal static bool LoadAll()
    {
        if (_loaded)
            return true;

        string dir = RuntimeDepsDir;
        if (!Directory.Exists(dir))
        {
            VRLog.Error("Core", $"RuntimeDeps directory not found: {dir} — VR unavailable. " +
                                "Re-install the mod (developers: scripts/build-runtimedeps.sh + scripts/deploy.ps1).");
            return false;
        }

        string[] files = Directory.GetFiles(dir, "*.dll");
        if (files.Length == 0)
        {
            VRLog.Error("Core", $"RuntimeDeps directory is empty: {dir} — VR unavailable.");
            return false;
        }

        // Safety net: resolve by simple name from the RuntimeDeps dir in case anything
        // requests one of our assemblies through a path Mono doesn't map to LoadFile'd ones.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromRuntimeDeps;

        var loadedNames = new List<string>();
        foreach (string file in files.OrderBy(f => f))
        {
            try
            {
                Assembly asm = Assembly.LoadFile(file);
                loadedNames.Add(asm.GetName().Name);
            }
            catch (Exception e)
            {
                VRLog.Error("Core", $"Failed to load runtime dependency '{Path.GetFileName(file)}': {e}");
                return false;
            }
        }

        VRLog.Info("Core", $"Loaded {loadedNames.Count} runtime dependencies: {string.Join(", ", loadedNames)}");
        _loaded = true;
        return true;
    }

    private static Assembly? ResolveFromRuntimeDeps(object sender, ResolveEventArgs args)
    {
        try
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string candidate = Path.Combine(RuntimeDepsDir, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFile(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
}
