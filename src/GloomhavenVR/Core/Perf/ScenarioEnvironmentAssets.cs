using System;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>One shared PC/Frame asset bank, loaded only when a selected presentation needs it.
/// Keep its immutable meshes/shader alive across scene and VR changes, just like the figure bank:
/// disposing a source bank while a prepared proxy still refers to it would invalidate rendering.
/// Missing/incompatible assets fail open to originals and produce one useful normal-level report.
/// </summary>
internal static class ScenarioEnvironmentAssets
{
    internal const string Filename = "ghvr-environment.bundle";
    private static AssetBundle? _bank;
    private static bool _attempted;
    internal static bool IsUnavailable => _attempted && _bank == null;
    internal static bool EnsureLoaded()
    {
        if (_bank != null) return true;
        if (_attempted) return false;
        _attempted = true;
        try
        {
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                if (loaded.name == Filename) { _bank = loaded; return true; }
            string folder = Path.GetDirectoryName(typeof(ScenarioEnvironmentAssets).Assembly.Location) ?? string.Empty;
            string path = Path.Combine(folder, Filename);
            if (!File.Exists(path)) throw new FileNotFoundException("missing environment asset bank", Filename);
            _bank = AssetBundle.LoadFromFile(path);
            if (_bank == null) throw new IOException("incompatible environment asset bank");
            VRLog.Info("Perf", "Scenario environment assets loaded: " + Filename + "; shared PC/Frame presentation bank.");
            return true;
        }
        catch (Exception error)
        {
            VRLog.Note("Perf", "Scenario environment assets unavailable (" + error.GetType().Name
                + "); original terrain remains available. Check " + Filename + " beside the plugin.");
            return false;
        }
    }
}
