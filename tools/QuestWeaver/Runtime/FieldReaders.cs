using System;
using System.Reflection;

namespace QuestWeaver.Runtime
{
    /// <summary>AOT-compiled readonly accessor for private collection revision metadata.
    /// The original mod's add/remove probe still qualifies the live framework field.
    /// No reference escapes, runtime code is generated, or collection is mutated here.</summary>
    public static class FieldReaders
    {
        public static Func<T, int> Version<T>(string fieldName)
        {
            if (fieldName != "_version" && fieldName != "m_version" && fieldName != "version")
                throw new NotSupportedException("Unknown collection revision field: " + fieldName);
            FieldInfo? field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int) || field.IsStatic)
                throw new MissingFieldException(typeof(T).FullName, fieldName);
            return instance => (int)(field.GetValue(instance) ?? throw new InvalidOperationException("Collection revision field is null."));
        }
    }
}
