using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;

public static class QuestFigureIdentityProbe
{
    [Serializable] private sealed class Source { public string file = ""; public bool readable = false; }
    [Serializable] private sealed class Sources { public Source[] meshes = Array.Empty<Source>(); }
    [Serializable] private sealed class Row { public string name = "", key = ""; public bool readable; }
    [Serializable] private sealed class Report { public Row[] meshes = Array.Empty<Row>(); }
    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string key) => args[Array.IndexOf(args, key) + 1];
        string input = Arg("-nativeMeshInput"), output = Arg("-nativeMeshOutput");
        QuestFigureMeshGenerator.Build(input, output, Arg("-figureProducerKey"));
        // NativeFormat recovery imports a serialized Mesh and its authored
        // read/write flag. Exercise that same native Unity importer, including
        // false, then the actual Android serializer. No ModelImporter guess.
        EditorSettings.serializationMode = SerializationMode.ForceText;
        Directory.CreateDirectory("Assets/IdentityProbe");
        Sources sources = JsonUtility.FromJson<Sources>(File.ReadAllText(Path.Combine(input, "sources.json")));
        var paths = new List<string>(); var rows = new List<Row>();
        for (int i = 0; i < sources.meshes.Length; i++)
        {
            Source source = sources.meshes[i]; Mesh original = NativeFigureMeshStream.Read(Path.Combine(input, source.file));
            string expected = ScenarioFigureMeshBank.Key(original, source.readable);
            // CreateAsset assigns the filename to the main native object's
            // name. Preserve the authored basename, as the recovery exporter
            // does; a synthetic source0.asset would test a renamed mesh.
            string directory = "Assets/IdentityProbe/source" + i; Directory.CreateDirectory(directory);
            string path = directory + "/" + original.name + ".asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(original, path); AssetDatabase.SaveAssets();
            string yaml = File.ReadAllText(path);
            if (!yaml.Contains("m_IsReadable: 1")) throw new InvalidDataException("Native serialized Mesh readability field missing");
            if (!source.readable) File.WriteAllText(path, yaml.Replace("m_IsReadable: 1", "m_IsReadable: 0"));
            Resources.UnloadAsset(original);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Mesh imported = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (imported.isReadable != source.readable || ScenarioFigureMeshBank.Key(imported) != expected)
                throw new InvalidDataException("NativeFormat figure key changed after native import: " + imported.name
                    + " readable=" + imported.isReadable + "/" + source.readable + " key=" + ScenarioFigureMeshBank.Key(imported) + "/" + expected
                    + " bounds=" + imported.bounds + " vertices=" + imported.vertexCount);
            rows.Add(new Row { name = imported.name, key = expected, readable = imported.isReadable }); paths.Add(path);
        }
        string proof = Path.Combine(Path.GetDirectoryName(output)!, "identity-native"); Directory.CreateDirectory(proof);
        if (BuildPipeline.BuildAssetBundles(proof, new[] { new AssetBundleBuild { assetBundleName = "native-body.bundle", assetNames = paths.ToArray() } },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.Android) == null)
            throw new InvalidDataException("Native identity Android compilation failed");
        File.WriteAllText(Path.Combine(proof, "native-keys.json"), JsonUtility.ToJson(new Report { meshes = rows.ToArray() }));
        EditorApplication.Exit(0);
    }
}
