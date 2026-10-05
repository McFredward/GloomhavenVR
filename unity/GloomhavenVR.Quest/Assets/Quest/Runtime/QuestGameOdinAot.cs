#nullable disable
#if GHVR_QUEST_STARTUP && GHVR_QUEST_GAME
using System.Collections.Generic;
using OdinSerializer;
using UnityEngine.Scripting;

namespace GloomhavenVR.Quest
{
    /// <summary>Compile the original save serializer's observed closed generic methods.</summary>
    [Preserve]
    static class QuestGameOdinAot
    {
        // This compiler root is never invoked. It neither initializes a save
        // nor substitutes a value. link.xml alone preserves type metadata but
        // cannot generate every value-type generic method under IL2CPP.
        [Preserve]
        static void PreserveSaveFormatters()
        {
            PreserveList<CombatLogFilter>();
            PreserveList<string>();
            PreserveList<GlobalData.KeyBinding>();
            PreserveList<PartyAdventureData>();
            PreserveList<GHRuleset>();
            var dictionary = new DictionaryFormatter<string, int>();
            dictionary.Serialize(null, null);
            dictionary.Deserialize(null);
            var filter = new EnumSerializer<CombatLogFilter>();
            filter.WriteValue(null, default(CombatLogFilter), null);
            filter.ReadValue(null);
            PreserveComplex<GlobalData>();
            PreserveComplex<GlobalData.KeyBinding>();
            PreserveComplex<PartyAdventureData>();
            PreserveComplex<GHRuleset>();
            PreserveComplex<StatsDataStorage>();
            PreserveComplex<IEqualityComparer<string>>();
            PreserveComplex<IEqualityComparer<int>>();
            var keyBinding = new ReflectionFormatter<GlobalData.KeyBinding>();
            keyBinding.Serialize(null, null);
            keyBinding.Deserialize(null);
            PreserveSerializable<GlobalData>();
            PreserveSerializable<StatsDataStorage>();
            PreserveSerializable<PartyAdventureData>();
            PreserveSerializable<GHRuleset>();
        }

        [Preserve]
        static void PreserveList<T>()
        {
            var formatter = new ListFormatter<T>();
            formatter.Serialize(null, null);
            formatter.Deserialize(null);
        }

        [Preserve]
        static void PreserveComplex<T>()
        {
            var serializer = new ComplexTypeSerializer<T>();
            serializer.WriteValue(null, default(T), null);
            serializer.ReadValue(null);
        }

        [Preserve]
        static void PreserveSerializable<T>()
        {
            var formatter = new SerializableFormatter<T>();
            formatter.Serialize(default(T), null);
            formatter.Deserialize(null);
        }
    }
}
#endif
