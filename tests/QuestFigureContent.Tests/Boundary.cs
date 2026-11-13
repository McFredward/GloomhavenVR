using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    internal static class Debug { internal static void Log(object text) { } internal static void LogWarning(object text) { } }
    internal static class JsonUtility
    {
        internal static T FromJson<T>(string json)
        {
            using var document = JsonDocument.Parse(json);
            return (T)Read(document.RootElement, typeof(T));
        }
        static object Read(JsonElement element, Type type)
        {
            if (type == typeof(string)) return element.GetString()!;
            if (type == typeof(int)) return element.GetInt32();
            if (type == typeof(long)) return element.GetInt64();
            if (type.IsArray)
            {
                var values = Array.CreateInstance(type.GetElementType()!, element.GetArrayLength()); int index = 0;
                foreach (var item in element.EnumerateArray()) values.SetValue(Read(item, type.GetElementType()!), index++);
                return values;
            }
            object result = Activator.CreateInstance(type, true)!;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (element.TryGetProperty(field.Name, out var item)) field.SetValue(result, Read(item, field.FieldType));
            return result;
        }
    }
}
namespace GloomhavenVR.Quest
{
    public sealed class QuestGameContentProgress
    {
        public readonly string File; public readonly long OverallProcessedBytes, OverallTotalBytes;
        public QuestGameContentProgress(string phase, string file, long done, long total, long overallDone = 0,
            long overallTotal = -1, int index = 0, int count = 0)
        { File = file; OverallProcessedBytes = overallDone; OverallTotalBytes = overallTotal; }
    }
}
