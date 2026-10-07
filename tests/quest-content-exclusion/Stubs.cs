using System;
using System.IO;
using System.Web.Script.Serialization;
namespace UnityEngine {
 public static class Application { public static string dataPath; }
 public static class Debug { public static void LogError(object message) { Console.Error.WriteLine(message); } }
 public static class JsonUtility {
  public static T FromJson<T>(string value) { return new JavaScriptSerializer().Deserialize<T>(value); }
  public static string ToJson(object value, bool pretty) { return new JavaScriptSerializer().Serialize(value); }
 }
}
namespace UnityEngine.AddressableAssets { public static class Addressables { public static string BuildPath; } }
namespace UnityEditor {
 [Flags] public enum ImportAssetOptions { ForceSynchronousImport=8 }
 public static class AssetDatabase {
  public static Action onRefresh, onImport;
  public static void Refresh(ImportAssetOptions options) { if(onRefresh!=null) onRefresh(); }
  public static void ImportAsset(string path, ImportAssetOptions options) { if(onImport!=null) onImport(); }
 }
}
namespace GloomhavenVR.Quest {
 public sealed class QuestGameContentFile { public string path,sha256; public long size; }
 public sealed class QuestGameContentManifest { public int schema; public string inputKey,archive,archiveSha256; public bool externalDelivery; public QuestGameContentFile[] files; }
}
