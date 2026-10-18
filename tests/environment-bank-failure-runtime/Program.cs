using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using GloomhavenVR.Core;

public static class EnvironmentBankFailureProgram
{
    private static int assertions;
    private static void Check(bool yes,string message)
    {assertions++;if(!yes)throw new InvalidOperationException(message);}
    private static void Reset()
    {
        var type=typeof(ScenarioEnvironmentMeshBank);
        foreach(string name in new[]{"Entries","Meshes","NativeEntries","Provenance"})
            ((IDictionary)type.GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).Clear();
        object failed=type.GetField("Failed",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        failed.GetType().GetMethod("Clear")!.Invoke(failed,null);
        type.GetField("_loaded",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,false);
        type.GetField("_failureDetail",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,string.Empty);
        type.GetField("_questSources",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,null);
        ScenarioEnvironmentMeshBank.ConfigureAssetPreparation(()=>true);
        QuestStandalonePlatform.Enabled=false;VRLog.Notes.Clear();
    }
    public static int Run(string desktopBank,string project)
    {
        assertions=0;Reset();
        AssetBundle native=AssetBundle.LoadFromFile(desktopBank);
        Check(native!=null,"retained native desktop environment bank loads");
        const string path="Assets/Bundle/EnvironmentMeshes/index.json";
        Check(native!.Contains(path)&&native.Contains(path.ToLowerInvariant()),"native Contains accepts both exact authored casing and lower-case serialized asset names");
        Check(native.LoadAsset<TextAsset>(path)!=null,"actual mixed-case native TextAsset lookup resolves");
        Check(ScenarioEnvironmentMeshBank.IsReady,"complete production parser accepts the actual retained native index");
        Check(ScenarioEnvironmentMeshBank.Diagnostics.Contains("firstFailure=none"),"successful native parse has no manufactured failure");
        Check(!ScenarioEnvironmentMeshBank.IsUnavailable&&VRLog.Notes.Count==0,"successful parser retains native bank readiness");
        Reset();QuestStandalonePlatform.Enabled=true;
        Check(!ScenarioEnvironmentMeshBank.IsReady&&ScenarioEnvironmentMeshBank.IsUnavailable,"missing required owned source receipt fails open");
        Check(ScenarioEnvironmentMeshBank.Diagnostics.Contains("firstFailure=index:InvalidDataException:missing owned environment source receipt"),"retained state identifies the missing owned-receipt stage and exact cause");
        Check(VRLog.Notes.Count==1,"native index failure reports once");
        string first=ScenarioEnvironmentMeshBank.Diagnostics;
        for(int i=0;i<40;i++)Check(!ScenarioEnvironmentMeshBank.IsReady,"terminal invalid index never retries expensive native reads");
        Check(VRLog.Notes.Count==1&&ScenarioEnvironmentMeshBank.Diagnostics==first,"repeat reads retain the first index reason without log growth");
        native.Unload(true);Reset();

        // A tiny invalid index exercises the actual Unity AssetBundle lookup.
        // It is a controlled fixture, never a repack of original game content.
        string index=Path.Combine(project,path);Directory.CreateDirectory(Path.GetDirectoryName(index)!);
        File.WriteAllText(index,"{\"format\":2,\"entries\":[]}");
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        string output=Path.Combine(project,"FixtureBundles");Directory.CreateDirectory(output);
        var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="invalid-environment.bundle",assetNames=new[]{path}}},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneLinux64);
        Check(manifest!=null,"tiny invalid-index native fixture bank builds");
        native=AssetBundle.LoadFromFile(Path.Combine(output,"invalid-environment.bundle"));
        Check(native!=null&&native.Contains(path),"tiny invalid-index asset resolves through actual native Contains");
        Check(!ScenarioEnvironmentMeshBank.IsReady,"production parser rejects unsupported native index format");
        Check(ScenarioEnvironmentMeshBank.Diagnostics.Contains("firstFailure=index:InvalidDataException:environment index format/bound"),"retained index reason distinguishes format failure from missing provenance");
        native!.Unload(true);Reset();

        ScenarioEnvironmentMeshBank.ConfigureAssetPreparation(()=>throw new InvalidOperationException("loader\nfailed",new IOException("exact inner cause")));
        Check(!ScenarioEnvironmentMeshBank.IsReady&&ScenarioEnvironmentMeshBank.IsUnavailable,"asset preparation exception retains original presentation fallback");
        string retained=ScenarioEnvironmentMeshBank.Diagnostics;
        Check(retained.Contains("firstFailure=asset-loader:InvalidOperationException:loader failed:IOException:exact inner cause"),"retained state includes bounded outer and inner loader causes with no control characters");
        Check(!retained.Contains("\n")&&!retained.Contains("\r"),"retained reason remains a single snapshot line");
        for(int i=0;i<40;i++)Check(!ScenarioEnvironmentMeshBank.IsReady,"repeated loader failure never admits private geometry");
        Check(VRLog.Notes.Count==1&&ScenarioEnvironmentMeshBank.Diagnostics==retained,"recurring loader failure keeps one note and its first precise reason");
        Reset();
        ScenarioEnvironmentMeshBank.ConfigureAssetPreparation(()=>throw new IOException(new string('x',4096)));
        Check(!ScenarioEnvironmentMeshBank.IsReady,"long platform exception stays fail-open");
        string detail=ScenarioEnvironmentMeshBank.Diagnostics.Split(new[]{"firstFailure="},StringSplitOptions.None)[1];
        Check(detail.Length==384,"retained exception obeys its strict 384-character bound");
        Reset();return assertions;
    }
}
