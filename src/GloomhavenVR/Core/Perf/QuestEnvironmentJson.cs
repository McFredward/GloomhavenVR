using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GloomhavenVR.Core;

/// <summary>Explicit, reflection-free readers for the existing owned environment receipts.
/// B692 reaches XmlObjectSerializerReadContext.EnsureArraySize&lt;uint&gt; on Android without
/// an IL2CPP specialization. Desktop retains its original serializer. Quest uses the
/// existing bounded managed JSON tokenizer and explicit field/array conversion instead;
/// no dynamically discovered generic serializer or new asset-bank format is required.</summary>
internal static class QuestEnvironmentJson
{
    internal static Dictionary<string, object?> Read(byte[] bytes)
    {
        if (bytes.Length > 8 * 1024 * 1024) throw Invalid();
        return Object(SelfUpdateJson.Parse(new UTF8Encoding(false, true).GetString(bytes), 8 * 1024 * 1024));
    }

    internal static Dictionary<string, object?> Object(object? value) =>
        value as Dictionary<string, object?> ?? throw Invalid();

    internal static object? Required(Dictionary<string, object?> value, string key) =>
        value.TryGetValue(key, out object? result) ? result : throw Invalid();

    internal static string String(Dictionary<string, object?> value, string key) =>
        Required(value, key) as string ?? throw Invalid();

    internal static bool Boolean(Dictionary<string, object?> value, string key) =>
        Required(value, key) is bool result ? result : throw Invalid();

    internal static List<object?> Array(Dictionary<string, object?> value, string key, int min, int max)
    {
        var result = Required(value, key) as List<object?>;
        return result != null && result.Count >= min && result.Count <= max ? result : throw Invalid();
    }

    internal static int Integer(Dictionary<string, object?> value, string key) =>
        (int)Integral(Required(value, key), int.MinValue, int.MaxValue);

    internal static uint Unsigned(object? value) => (uint)Integral(value, 0, uint.MaxValue);

    private static double Integral(object? value, double min, double max) =>
        value is double number && number >= min && number <= max && Math.Floor(number) == number
            ? number : throw Invalid();

    internal static float Single(object? value)
    {
        if (value is not double number || double.IsNaN(number) || double.IsInfinity(number)
            || number < -float.MaxValue || number > float.MaxValue) throw Invalid();
        return (float)number;
    }

    private static InvalidDataException Invalid() => new("owned environment JSON shape/bound");
}
