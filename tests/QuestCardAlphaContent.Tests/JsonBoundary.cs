using System.Reflection;
using System.Text.Json;

// Portable boundary only: execute the unchanged production file/ZIP/hash logic.
// Unity's private Serializable manifest is populated through the same public
// fields. This does not qualify Unity JsonUtility's Android implementation.
namespace UnityEngine;

internal static class JsonUtility
{
    internal static T? FromJson<T>(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Null) return default;
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("JSON object expected.");
            T instance = (T)Activator.CreateInstance(typeof(T), true)!;
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!document.RootElement.TryGetProperty(field.Name, out JsonElement value)) continue;
                if (field.FieldType == typeof(int)) field.SetValue(instance, value.GetInt32());
                else if (field.FieldType == typeof(string)) field.SetValue(instance, value.GetString());
                else throw new ArgumentException("Unexpected fixture JSON field.");
            }
            return instance;
        }
        catch (JsonException error) { throw new ArgumentException("Malformed JSON.", error); }
    }
}
