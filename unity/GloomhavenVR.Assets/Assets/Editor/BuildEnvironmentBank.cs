using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    /// <summary>Independent bank; never repack the established main/town asset banks as a side effect.
    /// Original environment derivatives were prepared offline from immutable publisher sources.
    /// Use the game-exact 2021.3.5f1 editor, including when building in a private temporary project.
    /// </summary>
    public static class EnvironmentBankBuilder
    {
        public static void BuildAll()
        {
            try
            {
                const string root = "Assets/Bundle/EnvironmentMeshes";
                const string shader = "Assets/Bundle/Environments/ScenarioCheapTerrain.shader";
                const string output = "Build/Environment";
                if (Application.unityVersion != "2021.3.5f1")
                    throw new InvalidOperationException("Environment banks require Unity 2021.3.5f1.");
                if (!File.Exists(root + "/index.json") || !File.Exists(shader))
                    throw new FileNotFoundException("Generate environment meshes and terrain shader before packing.");
                string[] assets = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(path => path.Replace('\\', '/'))
                    .Where(path => path.EndsWith(".bytes", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal))
                    .Concat(new[] { shader }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string asset in assets)
                    if (AssetDatabase.AssetPathToGUID(asset) == string.Empty)
                        throw new InvalidOperationException("Unimported environment asset: " + asset);
                Directory.CreateDirectory(output);
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output,
                    new[] { new AssetBundleBuild { assetBundleName = "ghvr-environment.bundle", assetNames = assets } },
                    BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new InvalidOperationException("Environment bank build returned null.");
                long length = new FileInfo(output + "/ghvr-environment.bundle").Length;
                if (length >= 100L * 1024 * 1024) throw new InvalidOperationException("Environment bank exceeds ordinary Git file limit; split it before committing.");
                Debug.Log("[GloomhavenVR] Environment bank OK: " + assets.Length + " assets, " + length + " bytes.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GloomhavenVR] Environment bank FAILED: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
    }
}
