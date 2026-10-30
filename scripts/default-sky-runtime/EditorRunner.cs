using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
public static class DefaultSkyRunner
{
    private static IEnumerator routine;private static string output;
    public static void Start()
    {
        string[] args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-skyEvidence")+1];
        string assembly=args[Array.IndexOf(args,"-skyAssembly")+1];
        // A real serialized additive scene exercises SceneManager.sceneLoaded;
        // the native controller is unnecessary for an arrival notification.
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/Initial.unity");
        Scene arrival=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var native=GameObject.CreatePrimitive(PrimitiveType.Sphere);native.name="GH_SkySphere";
        SceneManager.MoveGameObjectToScene(native,arrival);
        var material=new Material(Shader.Find("AMP_SkyShader"));material.renderQueue=2111;
        AssetDatabase.CreateAsset(material,"Assets/NativeArrival.mat");native.GetComponent<Renderer>().sharedMaterial=material;
        EditorSceneManager.SaveScene(arrival,"Assets/SkyArrival.unity");EditorSceneManager.CloseScene(arrival,true);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Initial.unity",true),new EditorBuildSettingsScene("Assets/SkyArrival.unity",true)};
        routine=(IEnumerator)Assembly.Load(assembly).GetType("DefaultSkyFixture").GetMethod("Run").Invoke(null,new object[]{output});
        EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update+=Begin;EditorApplication.EnterPlaymode();
    }
    private static void Begin(){if(!EditorApplication.isPlaying)return;EditorApplication.update-=Begin;new GameObject("default sky fixture").AddComponent<DefaultSkyCoroutine>().StartCoroutine(Step());}
    private static IEnumerator Step()
    {
        while(true)
        {
            bool next=false;try{next=routine.MoveNext();}catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;File.WriteAllText(Path.Combine(output,"failure.txt"),e+"\n");Debug.LogException(e);EditorApplication.Exit(1);yield break;}
            if(!next){EditorApplication.Exit(0);yield break;}yield return routine.Current;
        }
    }
}
public sealed class DefaultSkyCoroutine:MonoBehaviour{}
