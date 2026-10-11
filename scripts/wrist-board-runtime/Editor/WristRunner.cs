using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class WristRunner
{
    [InitializeOnLoadMethod] static void WirePlay()
    {
        EditorApplication.playModeStateChanged += s => {
            if(s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("WristProof", false)) {
                SessionState.SetBool("WristProof", false);WristHarness.Exit=EditorApplication.Exit;new GameObject("OriginalBoardWristProof").AddComponent<WristHarness>();
            }
        };
    }
    public static string Argument(string key) { var args=Environment.GetCommandLineArgs();return args[Array.IndexOf(args,key)+1]; }
    public static void Start()
    {
        Application.runInBackground=true;EditorApplication.isPaused=false;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.update += () => { if(EditorApplication.isPlaying) EditorApplication.isPaused=false; };
        string package=Directory.GetDirectories("Library/PackageCache","com.unity.textmeshpro@*")[0];
        if(Shader.Find("TextMeshPro/Distance Field")==null) AssetDatabase.ImportPackage(Path.Combine(package,"Package Resources/TMP Essential Resources.unitypackage"),false);
        EditorApplication.delayCall += Prepare;
    }
    static void Prepare()
    {
        if(Shader.Find("TextMeshPro/Distance Field")==null){EditorApplication.delayCall+=Prepare;return;}
        try {
            string folder=Path.Combine(Argument("-evidenceRoot"),"original-board-bundle");Directory.CreateDirectory(folder);
            var build=new AssetBundleBuild { assetBundleName="gloomhavenvr-wrist-proof",assetNames=AssetDatabase.FindAssets("",new[]{"Assets/Bundle/Table","Assets/Bundle/Hands"}).Select(AssetDatabase.GUIDToAssetPath).Where(File.Exists).Where(p=>!p.EndsWith(".meta")).ToArray() };
            string cache=Path.Combine("Library","WristOriginalBundle");Directory.CreateDirectory(cache);
            string fingerprint=Hash128.Compute("Linux64|"+string.Join("|",build.assetNames.OrderBy(x=>x).Select(x=>x+":"+AssetDatabase.GetAssetDependencyHash(x)))).ToString();
            string marker=Path.Combine(cache,"source.hash");string cached=Path.Combine(cache,build.assetBundleName);
            if(!File.Exists(cached)||!File.Exists(marker)||File.ReadAllText(marker)!=fingerprint) {
                BuildPipeline.BuildAssetBundles(cache,new[]{build},BuildAssetBundleOptions.UncompressedAssetBundle,BuildTarget.StandaloneLinux64);
                File.WriteAllText(marker,fingerprint);
            }
            File.Copy(cached,Path.Combine(folder,build.assetBundleName),true);
            SessionState.SetBool("WristProof",true);EditorApplication.isPlaying=true;
        } catch(Exception error) { File.WriteAllText(Path.Combine(Argument("-evidenceRoot"),"results.json"),JsonUtility.ToJson(new Failure { error=error.ToString() },true));EditorApplication.Exit(1); }
    }
    [Serializable] sealed class Failure { public bool passed=false;public string error; }
}
