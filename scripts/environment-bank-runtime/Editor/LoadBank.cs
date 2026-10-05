using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LoadEnvironmentBank
{
    public static void Run()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string path = args[Array.IndexOf(args, "-environmentBank") + 1];
            AssetBundle bank = AssetBundle.LoadFromFile(path);
            if (bank == null) throw new InvalidOperationException("Actual packaged environment bank did not load.");
            string[] names = bank.GetAllAssetNames();
            if (names.Length != 3172) throw new InvalidOperationException("Actual packaged asset count drift: " + names.Length);
            int count = 0;
            foreach (string name in names.Where(name => name.EndsWith(".bytes", StringComparison.Ordinal)))
            {
                TextAsset asset = bank.LoadAsset<TextAsset>(name);
                if (asset == null || asset.bytes.Length < 5) throw new InvalidOperationException("Missing geometry stream " + name);
                count++;
            }
            TextAsset index = bank.LoadAsset<TextAsset>("Assets/Bundle/EnvironmentMeshes/index.json");
            Shader shader = bank.LoadAsset<Shader>("Assets/Bundle/Environments/ScenarioCheapTerrain.shader");
            if (count != 3170 || index == null || shader == null || shader.name != "GloomhavenVR/ScenarioCheapTerrain")
                throw new InvalidOperationException("Actual packaged environment content unavailable.");
            Debug.Log("PASS packaged environment bank load: " + count + " streams, index and shader; Unity " + Application.unityVersion);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogError("FAIL packaged environment bank load: " + error); EditorApplication.Exit(1); }
    }
}
