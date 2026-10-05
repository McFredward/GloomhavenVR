// Controlled public API doubles. The unchanged production method bodies are
// compiled into this fixture; this does not replace the native Unity witness.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace UnityEngine
{
 public class Object { public string name; }
 public class Shader : Object { }
 public class ShaderVariantCollection : Object
 {
  public struct ShaderVariant { public Shader shader; public UnityEngine.Rendering.PassType passType; public string[] keywords; public ShaderVariant(Shader s,UnityEngine.Rendering.PassType p,string[] k){shader=s;passType=p;keywords=k;} }
  public readonly List<ShaderVariant> Variants=new List<ShaderVariant>();
  public int shaderCount { get {return Variants.Select(x=>x.shader).Distinct().Count();} }
  public int variantCount { get {return Variants.Select(Key).Distinct().Count();} }
  static string Key(ShaderVariant x){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x.shader)+"/"+x.passType+"/"+string.Join(" ",x.keywords.OrderBy(y=>y,StringComparer.Ordinal));}
  public bool Contains(ShaderVariant x){return Variants.Any(v=>Key(v)==Key(x));}
 }
 public static class JsonUtility { public static object Input; public static T FromJson<T>(string unused){return (T)Input;} }
 public static class Debug { public static void Log(string value){} }
}
namespace UnityEngine.Rendering
{
 public enum PassType { Normal=0,ForwardBase=4,ForwardAdd=5 }
 public static class GraphicsSettings { public static UnityEngine.Object GetGraphicsSettings(){return new UnityEngine.Object();} }
}
namespace UnityEditor
{
 public enum SerializedPropertyType { Integer,Enum,Boolean }
 public class SerializedProperty
 {
  public static SerializedPropertyType Type=SerializedPropertyType.Enum;public static string[] Names=new[]{"StripUnused","StripAll","KeepAll"};public static int Value;public static bool Available=true,Writable=true;
  public static SerializedPropertyType FogType=SerializedPropertyType.Enum,FogFlagType=SerializedPropertyType.Boolean;
  public static string[] FogNames=new[]{"Automatic","Custom"};public static int FogValue;public static bool FogAvailable=true,FogWritable=true,FogFlagAvailable=true;
  public static readonly Dictionary<string,bool> FogFlags=new Dictionary<string,bool>();public static string UnwritableFogFlag;
  readonly string name;public SerializedProperty(string propertyName){name=propertyName;}
  public SerializedPropertyType propertyType {get{return name=="m_InstancingStripping"?Type:name=="m_FogStripping"?FogType:FogFlagType;}}
  public string[] enumNames {get{return name=="m_InstancingStripping"?Names:FogNames;}}
  public int enumValueIndex {get{return intValue;}set{if(name=="m_InstancingStripping"){if(Writable)Value=value;}else if(FogWritable)FogValue=value;}}
  public int intValue {get{return name=="m_InstancingStripping"?Value:FogValue;}}
  public bool boolValue {get{return FogFlags[name];}set{if(name!=UnwritableFogFlag)FogFlags[name]=value;}}
 }
 public class SerializedObject
 {
  public SerializedObject(UnityEngine.Object ignored){}
  public SerializedProperty FindProperty(string name){bool available=name=="m_InstancingStripping"?SerializedProperty.Available:name=="m_FogStripping"?SerializedProperty.FogAvailable:SerializedProperty.FogFlags.ContainsKey(name)&&SerializedProperty.FogFlagAvailable;return available?new SerializedProperty(name):null;}
  public void ApplyModifiedPropertiesWithoutUndo(){}public void Update(){}
 }
 public static class EditorUtility
 {
  public static void CopySerialized(UnityEngine.Object source,UnityEngine.Object target){var a=(UnityEngine.ShaderVariantCollection)source;var b=(UnityEngine.ShaderVariantCollection)target;b.Variants.Clear();b.Variants.AddRange(a.Variants);}
  public static void SetDirty(UnityEngine.Object ignored){}
 }
 public static class AssetDatabase
 {
  public static readonly Dictionary<string,UnityEngine.Object> Objects=new Dictionary<string,UnityEngine.Object>();public static readonly Dictionary<string,string> Guids=new Dictionary<string,string>();
  public static T LoadAssetAtPath<T>(string path) where T:UnityEngine.Object {UnityEngine.Object value;return Objects.TryGetValue(path,out value)?value as T:null;}
  public static string AssetPathToGUID(string path){string guid;return Guids.TryGetValue(path,out guid)?guid:"";}
  public static void CreateAsset(UnityEngine.Object value,string path){Objects.Add(path,value);Guids.Add(path,new string('f',32));File.WriteAllText(path,"native collection fixture");}
 }
}
namespace UnityEditor.AddressableAssets.Settings.GroupSchemas
{
 public class BundledAssetGroupSchema {public enum BundlePackingMode{PackTogether,PackSeparately}public BundlePackingMode BundleMode;public bool IncludeAddressInCatalog,IncludeGUIDInCatalog,IncludeLabelsInCatalog;}
 public class ContentUpdateGroupSchema { }
}
namespace UnityEditor.AddressableAssets.Settings
{
 public class AddressableAssetEntry
 {
  public string guid,address;public AddressableAssetGroup parentGroup;public readonly HashSet<string> labels=new HashSet<string>();
  public string AssetPath {get{return UnityEditor.AssetDatabase.Guids.Single(x=>x.Value==guid).Key;}}
 }
 public class AddressableAssetGroup : UnityEngine.Object
 {
  public readonly List<AddressableAssetEntry> entries=new List<AddressableAssetEntry>();readonly Dictionary<Type,object> schemas=new Dictionary<Type,object>();
  public T GetSchema<T>() where T:class {object value;return schemas.TryGetValue(typeof(T),out value)?(T)value:null;}
  public void AddSchema<T>(bool ignored) where T:new(){schemas.Add(typeof(T),new T());}
 }
 public class AddressableAssetSettings
 {
  public readonly List<AddressableAssetGroup> groups=new List<AddressableAssetGroup>();
  public AddressableAssetGroup FindGroup(string name){return groups.SingleOrDefault(x=>x.name==name);}
  public AddressableAssetGroup CreateGroup(string name,bool a,bool b,bool c,object d,params Type[] types){var g=new AddressableAssetGroup{name=name};groups.Add(g);g.AddSchema<GroupSchemas.BundledAssetGroupSchema>(false);g.AddSchema<GroupSchemas.ContentUpdateGroupSchema>(false);return g;}
  public AddressableAssetEntry FindAssetEntry(string guid){return groups.SelectMany(x=>x.entries).SingleOrDefault(x=>x.guid==guid);}
  public AddressableAssetEntry CreateOrMoveEntry(string guid,AddressableAssetGroup group,bool a,bool b){var value=FindAssetEntry(guid);if(value==null)value=new AddressableAssetEntry{guid=guid};else if(value.parentGroup==group)return value;else value.parentGroup.entries.Remove(value);value.parentGroup=group;group.entries.Add(value);return value;}
 }
}
namespace GloomhavenVR.Quest.Editor
{
 public static class QuestCampaignShaderValidation
 {
  public const string DefaultManifest="Assets/QuestOriginalCampaign/campaign-shaders.json";
  public class Variant {public string passType;public string[] keywords;}
  public class Row {public string assetPath,guid,originalName;public Variant[] variants;}
  public class Manifest {public int schema,requiredShaderCount;public string scope,graphicsApi;public Row[] shaders;}
 }
}
