using System.Text.Json;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public class ScriptableObject { }
    public static class Debug { public static void Log(string message) { } }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
        public static string ToJson(object value, bool pretty = false) => JsonSerializer.Serialize(value, value.GetType(), Options);
    }
}

namespace UnityEditor
{
    public sealed class MonoScript
    {
        public Type type;
        public string path, guid;
        public long fileId;
        public int order;
        public Type GetClass() => type;
    }
    public static class MonoImporter
    {
        public static MonoScript[] scripts = Array.Empty<MonoScript>();
        public static int writes;
        public static MonoScript[] GetAllRuntimeMonoScripts() => scripts;
        public static int GetExecutionOrder(MonoScript script) => script.order;
        public static void SetExecutionOrder(MonoScript script, int value) { writes++; script.order = value; }
    }
    public static class AssetDatabase
    {
        public static string GetAssetPath(MonoScript script) => script.path;
        public static bool TryGetGUIDAndLocalFileIdentifier(MonoScript script, out string guid, out long fileId)
        {
            guid = script.guid; fileId = script.fileId; return true;
        }
    }
}
