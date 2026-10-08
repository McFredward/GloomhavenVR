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
            int argument = Array.IndexOf(args, "-environmentBank");
            if (argument < 0 || argument + 1 >= args.Length) throw new ArgumentException("Missing actual environment bank path.");
            string path = args[argument + 1];
            int expectedArgument = Array.IndexOf(args, "-environmentExpectedStreams");
            int expected;
            if (expectedArgument < 0 || expectedArgument + 1 >= args.Length
                || !int.TryParse(args[expectedArgument + 1], out expected) || expected < 1 || expected > 24576)
                throw new ArgumentException("Missing independently verified packaged stream count.");
            if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Use the game-exact Unity2021.3.5f1 loader.");
            AssetBundle bank = AssetBundle.LoadFromFile(path);
            if (bank == null) throw new InvalidOperationException("Actual packaged environment bank did not load.");
            string[] names = bank.GetAllAssetNames();
            // The Python verifier independently compares each packed stream to
            // immutable sources, index hashes and the separately checked receipt.
            // Do not pin an old bank generation's count in this actual decoder.
            if (names.Length != expected + 3) throw new InvalidOperationException("Actual packaged asset count drift: " + names.Length);
            int count = 0; long vertices = 0, triangles = 0;
            foreach (string name in names.Where(name => name.EndsWith(".bytes", StringComparison.Ordinal)))
            {
                TextAsset asset = bank.LoadAsset<TextAsset>(name);
                if (asset == null || asset.bytes.Length < 5) throw new InvalidOperationException("Missing geometry stream " + name);
                // Bind the real production decoder in this tiny editor project.
                // Assets are TextAssets loaded on demand; this never modifies a
                // native game mesh or mutates the read-only source bundles.
                Mesh mesh = GloomhavenVR.Core.ScenarioEnvironmentMeshStream.Read(asset.bytes);
                try
                {
                    if (!mesh.isReadable || mesh.vertexCount < 3 || mesh.subMeshCount < 1)
                        throw new InvalidOperationException("Invalid decoded private geometry " + name);
                    vertices += mesh.vertexCount;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) triangles += mesh.GetIndexCount(submesh) / 3;
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                count++;
            }
            TextAsset index = bank.LoadAsset<TextAsset>("Assets/Bundle/EnvironmentMeshes/index.json");
            Shader shader = bank.LoadAsset<Shader>("Assets/Bundle/Environments/ScenarioCheapTerrain.shader");
            Shader world = bank.LoadAsset<Shader>("Assets/Bundle/Environments/WorldSimpleMaterial.shader");
            if (world == null || world.name != "GloomhavenVR/WorldSimpleMaterial")
                throw new InvalidOperationException("Actual packaged world material shader unavailable.");
            if (count != expected || index == null || shader == null || shader.name != "GloomhavenVR/ScenarioCheapTerrain")
                throw new InvalidOperationException("Actual packaged environment content unavailable.");
            Debug.Log("PASS packaged environment bank load: " + count + " production-decoded streams, " + vertices
                + " vertex slots, " + triangles + " triangles; index and both shaders; Unity " + Application.unityVersion);
            Debug.Log("Shader availability only: " + shader.name + " and " + world.name + "; graphics " + SystemInfo.graphicsDeviceType
                + "; supported " + shader.isSupported + "/" + world.isSupported + ". Original Windows shader/HMD/Frame appearance is not established by this loader.");
            bank.Unload(true);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogError("FAIL packaged environment bank load: " + error); EditorApplication.Exit(1); }
    }
}
