// Host oracle for the original game and original Odin binary writer/reader.
// Compile with Unity's bundled mcs; run each selector in a fresh Mono process.
// Fixtures contain no real player account or campaign payload.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;

internal static class OdinGlobalDataProof
{
    static Assembly game, odin;
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidDataException(message);
    }
    static object Allocate(Type type) { return FormatterServices.GetUninitializedObject(type); }
    static Type Type(string name) { return game.GetType(name, true); }
    static void Field(object value, string name, object data)
    {
        value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, data);
    }
    static object Field(object value, string name)
    {
        FieldInfo field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(value) : value.GetType().GetProperty(name).GetValue(value, null);
    }
    static byte[] Serialize(object value)
    {
        Type utility = odin.GetType("OdinSerializer.SerializationUtility", true);
        Type formatType = odin.GetType("OdinSerializer.DataFormat", true);
        MethodInfo method = utility.GetMethods().Single(methodInfo => methodInfo.Name == "SerializeValue"
            && methodInfo.IsGenericMethodDefinition && methodInfo.GetParameters().Length == 3
            && methodInfo.GetParameters()[1].ParameterType == formatType);
        return (byte[])method.MakeGenericMethod(value.GetType()).Invoke(null,
            new object[] { value, Enum.Parse(formatType, "Binary"), null });
    }
    static object Deserialize(byte[] bytes)
    {
        Type utility = odin.GetType("OdinSerializer.SerializationUtility", true);
        Type formatType = odin.GetType("OdinSerializer.DataFormat", true);
        MethodInfo method = utility.GetMethods().Single(methodInfo => methodInfo.Name == "DeserializeValue"
            && methodInfo.IsGenericMethodDefinition && methodInfo.GetParameters().Length == 3
            && methodInfo.GetParameters()[0].ParameterType == typeof(byte[]));
        return method.MakeGenericMethod(Type("GlobalData")).Invoke(null,
            new object[] { bytes, Enum.Parse(formatType, "Binary"), null });
    }
    static Dictionary<string, object> Entries(object value)
    {
        var info = new SerializationInfo(value.GetType(), new FormatterConverter());
        ((ISerializable)value).GetObjectData(info, new StreamingContext());
        var result = new Dictionary<string, object>();
        foreach (SerializationEntry entry in info) result.Add(entry.Name, entry.Value);
        return result;
    }
    static void Verify(object value, bool nonempty)
    {
        Check(value != null && value.GetType() == Type("GlobalData"), "Original GlobalData deserializer returned no correct instance.");
        var entries = Entries(value);
        Check(entries.Count == 50, "Original GlobalData serialization member count changed: " + entries.Count);
        Check((int)entries["MasterVolume"] == (nonempty ? 37 : 80), "Original master volume changed.");
        Check((int)entries["MusicVolume"] == (nonempty ? 64 : 80), "Original music volume changed.");
        Check((float)entries["StickSensitivity"] == (nonempty ? 1.375f : 1.0f), "Original float setting changed.");
        Check((bool)entries["EpicLogin"] == nonempty, "Imported EpicLogin save state changed.");
        Check(((IList)entries["UsersAcceptedEULA"]).Count == (nonempty ? 2 : 0), "Original user string list changed.");
        Check(((IList)entries["CompletedTutorialIDs"]).Count == (nonempty ? 2 : 0), "Original tutorial string list changed.");
        Check(((IList)entries["KeyBindings"]).Count == (nonempty ? 2 : 0), "Original typed key-binding list changed.");
        Check(((IList)entries["DisabledCombatLogFilters"]).Count == (nonempty ? 1 : 0), "Original value-type enum list changed.");
        Check(entries["StatsDataStorage"] != null && entries["StatsDataStorage"].GetType() == Type("StatsDataStorage"), "Original nested stats graph disappeared.");
        Check(((IList)entries["AllCampaigns"]).Count == 0 && ((IList)entries["AllAdventures"]).Count == 0,
            "Fixture must not imply full campaign/party save validation.");
    }
    static int Main(string[] args)
    {
        if (args.Length != 4) { Console.Error.WriteLine("Usage: OdinGlobalDataProof HOST_MANAGED PC_OR_QUEST OUTPUT_DIRECTORY write|read-other"); return 2; }
        string managed = Path.GetFullPath(args[0]), variant = args[1], output = Path.GetFullPath(args[2]);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, requested) => {
            string path = Path.Combine(managed, new AssemblyName(requested.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        odin = Assembly.LoadFrom(Path.Combine(managed, "OdinSerializer." + variant + ".dll"));
        game = Assembly.LoadFrom(Path.Combine(managed, "GH.Runtime.dll"));
        bool canEmit = (bool)odin.GetType("OdinSerializer.Utilities.EmitUtilities").GetProperty("CanEmit").GetValue(null, null);
        Check(canEmit == variant.StartsWith("pc", StringComparison.Ordinal), "Wrong original/AOT capability selector loaded.");
        Directory.CreateDirectory(output);
        object save = Allocate(Type("SaveData"));
        Field(save, "<PersistentDataPath>k__BackingField", Path.Combine(output, "disposable-save-root"));
        Field(save, "<RootData>k__BackingField", Activator.CreateInstance(Type("RootSaveData")));
        Type("SaveData").GetField("Instance").SetValue(null, save);
        object layer = Allocate(Type("PlatformLayer"));
        object filesystem = Allocate(Type("PlatformFileSystem"));
        Field(filesystem, "_persistentDataPath", Field(save, "<PersistentDataPath>k__BackingField"));
        Field(layer, "m_FileSystem", filesystem);
        Field(layer, "m_Modding", Allocate(Type("PlatformModding")));
        Type("PlatformLayer").GetField("Instance").SetValue(null, layer);
        foreach (bool nonempty in new[] { false, true })
        {
            string fixture = nonempty ? "nonempty" : "default";
            object original = Activator.CreateInstance(Type("GlobalData"));
            // Normalize the native migration field in both oracles so byte equality
            // measures formatter selection, rather than intentional old-save migration.
            Field(original, "SteamDataSuiteAttributionDone", true);
            if (nonempty)
            {
                Field(original, "MasterVolume", 37); Field(original, "MusicVolume", 64);
                Field(original, "StickSensitivity", 1.375f); Field(original, "EpicLogin", true);
                ((IList)Field(original, "UsersAcceptedEULA")).Add("Quest original serializer fixture (DUMMY)");
                ((IList)Field(original, "UsersAcceptedEULA")).Add("second original fixture (DUMMY)");
                ((IList)Field(original, "CompletedTutorialIDs")).Add("FixtureTutorial01");
                ((IList)Field(original, "CompletedTutorialIDs")).Add("FixtureTutorial02");
                Type keyType = Type("GlobalData+KeyBinding");
                ConstructorInfo ctor = keyType.GetConstructors().Single(candidate => candidate.GetParameters().Length == 2
                    && candidate.GetParameters()[0].ParameterType.IsEnum);
                object[] actions = Enum.GetValues(ctor.GetParameters()[0].ParameterType).Cast<object>().Take(2).ToArray();
                object[] keys = Enum.GetValues(ctor.GetParameters()[1].ParameterType).Cast<object>().Take(2).ToArray();
                for (int index = 0; index < 2; index++) ((IList)Field(original, "KeyBindings")).Add(ctor.Invoke(new[] { actions[index], keys[index] }));
                IList filters = (IList)Field(original, "DisabledCombatLogFilters");
                filters.Add(Enum.GetValues(filters.GetType().GetGenericArguments()[0]).GetValue(0));
                ((IDictionary)Field(Field(original, "m_StatsDataStorage"), "m_DefeatedAdventureBosses"))["OriginalStatFixture (DUMMY)"] = 3;
            }
            Verify(original, nonempty);
            byte[] bytes = Serialize(original);
            Check(bytes.Length > 256, "Original Odin binary stream unexpectedly empty.");
            File.WriteAllBytes(Path.Combine(output, variant + "-" + fixture + ".dat"), bytes);
            object copy = Deserialize(bytes); Verify(copy, nonempty);
            Check(bytes.SequenceEqual(Serialize(copy)), "Native original write/read/write changed bytes: " + variant + "/" + fixture);
            if (args[3] == "read-other")
            {
                string other = variant == "pc" ? "quest" : "pc";
                byte[] otherBytes = File.ReadAllBytes(Path.Combine(output, other + "-" + fixture + ".dat"));
                Verify(Deserialize(otherBytes), nonempty);
                Check(bytes.SequenceEqual(otherBytes), "Native original PC/Quest formatter bytes changed: " + fixture);
            }
            Console.WriteLine(variant + "/" + fixture + " " + bytes.Length + " bytes SHA256=" + BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
        }
        Type serializerType = odin.GetType("OdinSerializer.Serializer", true);
        var closure = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string field in new[] { "Weak_ReaderWriterCache", "Strong_ReaderWriterCache" })
            foreach (DictionaryEntry entry in (IDictionary)serializerType.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                closure.Add("serializer " + ((Type)entry.Key).AssemblyQualifiedName + " => " + entry.Value.GetType().AssemblyQualifiedName);
        Type locatorType = odin.GetType("OdinSerializer.FormatterLocator", true);
        foreach (DictionaryEntry entry in (IDictionary)locatorType.GetField("FormatterInstances", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            closure.Add("formatter " + ((Type)entry.Key).AssemblyQualifiedName);
        File.WriteAllLines(Path.Combine(output, variant + "-original-type-closure.txt"), closure.ToArray());
        Console.WriteLine("Actual original GlobalData + original Odin binary writer/reader: " + assertions + " assertions passed; host-only Unity native substitutes; Android/full campaign graph not verified.");
        return 0;
    }
}
