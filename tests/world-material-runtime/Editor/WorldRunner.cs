using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldRunner
{
    [Serializable]private class Case {public string name,dll,expected;}
    [Serializable]private class Manifest {public string result;public Case[] cases;}
    private static Manifest manifest;private static bool ran;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args,"-worldManifest")+1]));
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/WorldMaterialBase.unity");
        var unrelated=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(unrelated,"Assets/WorldMaterialUnrelated.unity");
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(unrelated,true);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/WorldMaterialUnrelated.unity",true)};
        EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update+=Run;EditorApplication.EnterPlaymode();
    }
    private static void Run()
    {
        if(!EditorApplication.isPlaying||ran)return;ran=true;
        new GameObject("World material coroutine runner").AddComponent<WorldMaterialCaseRunner>().StartCoroutine(Execute());
    }
    private static IEnumerator Execute()
    {
        bool passed=true;
        using(var report=new StreamWriter(manifest.result))
        {
            Environment.SetEnvironmentVariable("GHVR_WORLD_EVIDENCE", Path.GetDirectoryName(manifest.result));
            report.WriteLine("Unity "+Application.unityVersion+"; GPU "+SystemInfo.graphicsDeviceName);
            foreach(var entry in manifest.cases)
            {
                var previous=new HashSet<int>();foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))previous.Add(go.GetInstanceID());
                var previousScenes=new HashSet<int>();for(int i=0;i<SceneManager.sceneCount;i++)previousScenes.Add(SceneManager.GetSceneAt(i).handle);
                Assembly assembly=null;Exception failure=null;int assertions=0;IEnumerator asynchronous=null;
                try
                {
                    assembly=Assembly.LoadFile(entry.dll);
                    bool useAsync=entry.name=="production"||entry.name.StartsWith("off-")||entry.name=="retained-scene-not-reseeded";
                    if(useAsync)asynchronous=(IEnumerator)assembly.GetType("WorldMaterialProgram").GetMethod("RunAsync").Invoke(null,null);
                    else assertions=(int)assembly.GetType("WorldMaterialProgram").GetMethod("Run").Invoke(null,null);
                }
                catch(Exception error){failure=error;}
                while(asynchronous!=null&&failure==null)
                {
                    bool advanced=false;
                    try{advanced=asynchronous.MoveNext();}catch(Exception error){failure=error;}
                    if(!advanced)break;
                    yield return asynchronous.Current;
                }
                if(asynchronous!=null&&failure==null)assertions=(int)assembly.GetType("WorldMaterialProgram").GetProperty("Assertions").GetValue(null);
                try
                {
                    if(failure!=null)throw failure;
                    if(!String.IsNullOrEmpty(entry.expected))throw new Exception("negative control escaped");
                    report.WriteLine("PASS "+entry.name+": "+assertions+" runtime assertions");
                }
                catch(Exception error)
                {
                    while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                    if(!String.IsNullOrEmpty(entry.expected)&&error.Message.Contains(entry.expected))report.WriteLine("PASS negative "+entry.name+": "+error.Message);
                    else{passed=false;report.WriteLine("FAIL "+entry.name+": "+error);}
                }
                finally
                {
                    if(assembly!=null)
                    {
                        var budget=assembly.GetType("GloomhavenVR.Core.WorldMaterialBudget");budget.GetMethod("Shutdown",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
                        var session=assembly.GetType("GloomhavenVR.Core.VRSession");object harmony=session.GetField("Harmony",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                        harmony.GetType().GetMethod("UnpatchSelf").Invoke(harmony,null);
                    }
                    if(asynchronous is IDisposable disposable)disposable.Dispose();
                    foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))if(go!=null&&!previous.Contains(go.GetInstanceID()))UnityEngine.Object.DestroyImmediate(go);
                    Physics.SyncTransforms();
                }
                // A failing control may leave the coroutine before its final
                // scene-unload yield. Complete real engine cleanup before reentry.
                var addedScenes=new List<Scene>();
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    Scene scene=SceneManager.GetSceneAt(i);
                    if(scene.isLoaded&&!previousScenes.Contains(scene.handle))addedScenes.Add(scene);
                }
                foreach(Scene scene in addedScenes)yield return SceneManager.UnloadSceneAsync(scene);
                report.Flush();
            }
        }
        EditorApplication.Exit(passed?0:1);
    }
}

public sealed class WorldMaterialCaseRunner:MonoBehaviour{ }
