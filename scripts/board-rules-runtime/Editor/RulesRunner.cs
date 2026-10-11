using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RulesRunner
{
    static RulesRunner() { EditorApplication.playModeStateChanged+= state=> { if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("RulesProof",false)) { SessionState.SetBool("RulesProof",false); new GameObject("NativeRulesProof").AddComponent<RulesHarness>(); } }; }
    [Serializable] private sealed class Scalar { public string name; public float value; }
    [Serializable] private sealed class Tint { public string name; public Color value; }
    [Serializable] private sealed class Properties { public Scalar[] floats; public Tint[] colors; public string[] keywords; }
    public static void Start()
    {
        string package=Directory.GetDirectories("Library/PackageCache","com.unity.textmeshpro@*")[0];
        if(Shader.Find("TextMeshPro/Distance Field")==null) AssetDatabase.ImportPackage(Path.Combine(package,"Package Resources/TMP Essential Resources.unitypackage"),false);
        EditorApplication.delayCall+=LoadNative;
    }
    private static void LoadNative()
    {
        if(Shader.Find("TextMeshPro/Distance Field")==null) { EditorApplication.delayCall+=LoadNative; return; }
        string root = Argument("-evidenceRoot");
        try
        {
            AssetDatabase.Refresh();
            foreach(string asset in new[]{"Assets/NativeAtlas.asset","Assets/NativeRulesMaterial.mat","Assets/Resources/NativeRulesFont.asset","Assets/Resources/NativeRule.prefab","Assets/Resources/NativeButtonTexture.asset","Assets/Resources/NativeButton.asset"}) AssetDatabase.DeleteAsset(asset);
            string data = "Assets/NativeSource/";
            // Native font tables and face metrics are serialized output, not a regenerated font.
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(data+"font.json"),font);
            File.WriteAllText(Path.Combine(root,"font-import.json"),JsonUtility.ToJson(font));
            var atlas = new Texture2D(2,2,TextureFormat.RGBA32,false,true);atlas.LoadImage(File.ReadAllBytes(data+"atlas.png"));atlas.name="NativeRulesAtlas";
            AssetDatabase.CreateAsset(atlas,"Assets/NativeAtlas.asset");
            font.atlasTextures=new[]{atlas};
            Material mat=new Material(Shader.Find("TextMeshPro/Distance Field"));mat.name="NativeRulesMaterial";
            mat.mainTexture=atlas;
            Properties properties=JsonUtility.FromJson<Properties>(File.ReadAllText(data+"material-properties.json"));
            foreach(var value in properties.floats) mat.SetFloat(value.name,value.value);
            foreach(var value in properties.colors) mat.SetColor(value.name,value.value);
            mat.shaderKeywords=properties.keywords;
            font.material=mat;font.ReadFontAssetDefinition();
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(mat,"Assets/NativeRulesMaterial.mat");AssetDatabase.CreateAsset(font,"Assets/Resources/NativeRulesFont.asset");
            var settings=Resources.Load<TMP_Settings>("TMP Settings");
            if(settings==null)settings=ScriptableObject.CreateInstance<TMP_Settings>();
            typeof(TMP_Settings).GetField("m_defaultFontAsset",BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(settings,font);
            if(!AssetDatabase.Contains(settings))AssetDatabase.CreateAsset(settings,"Assets/Resources/TMP Settings.asset");
            GameObject row=new GameObject("Scenario modifier",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
            NativeFixture.ApplyRect((RectTransform)row.transform,File.ReadAllText(data+"row-rect.json"));
            var text=row.GetComponent<TextMeshProUGUI>();JsonUtility.FromJsonOverwrite(File.ReadAllText(data+"text.json"),text);
            text.font=font;text.fontSharedMaterial=mat;
            Type widget=Assembly.Load("GH.Runtime").GetType("ScenarioModifierUI",true);Component native=row.AddComponent(widget);
            widget.GetField("scenarioModifierText",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(native,text);
            PrefabUtility.SaveAsPrefabAsset(row,"Assets/Resources/NativeRule.prefab");UnityEngine.Object.DestroyImmediate(row);
            var buttonTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);buttonTexture.LoadImage(File.ReadAllBytes(data+"button.png"));
            AssetDatabase.CreateAsset(buttonTexture,"Assets/Resources/NativeButtonTexture.asset");
            var metrics=JsonUtility.FromJson<ButtonMetrics>(File.ReadAllText(data+"button-metrics.json"));
            Sprite button=Sprite.Create(buttonTexture,new Rect(0,0,buttonTexture.width,buttonTexture.height),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,metrics.border);
            AssetDatabase.CreateAsset(button,"Assets/Resources/NativeButton.asset");
            AssetDatabase.SaveAssets();
            SessionState.SetBool("RulesProof",true);
            EditorApplication.isPlaying=true;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(root,"results.json"),"{\"passed\":false,\"error\":\""+e.ToString().Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","")+"\"}");EditorApplication.Exit(1);}
    }
    [Serializable] private sealed class ButtonMetrics { public Vector4 border; }
    internal static string Argument(string key){string[] a=Environment.GetCommandLineArgs();for(int i=0;i<a.Length-1;i++)if(a[i]==key)return a[i+1];throw new Exception("Missing argument "+key);}
}
