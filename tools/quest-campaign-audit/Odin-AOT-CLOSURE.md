# Original Odin Campaign metadata AOT closure

This audit uses the supplied original DLLs and their actual formatter selections,
not inferred generic types. All named game classes below are public types from
`GH.Runtime`; nested `GlobalData.KeyBinding`,
`ClientIndependantValues.CIVKeyValuePair` and `SaveOwner.SerializedAvatar` are public.
All formatter/serializer APIs come from the original `OdinSerializer` assembly.

| Original selected formatter | Concrete arguments proven by native metadata |
|---|---|
| `SerializableFormatter<T>` | `GlobalData`, `StatsDataStorage`, `PartyAdventureData`, `GHRuleset`, `ClientIndependantValues`, `SaveOwner`, `SaveOwner.SerializedAvatar` |
| `ReflectionFormatter<T>` | `GlobalData.KeyBinding`, `ClientIndependantValues.CIVKeyValuePair`, `Tuple<string,int>`, `Tuple<string,int,string>` |
| `ListFormatter<T>` and its `ComplexTypeSerializer<T>` element serializer | Key binding, independent flag and both tuple types; original Party/ruleset elements. `string` and `CombatLogFilter` use their primitive/enum serializers. |
| `ComplexTypeSerializer<T>` | `IEqualityComparer<string>` and `IEqualityComparer<int>` initialized by original dictionary formatter static constructors |
| `PrimitiveArrayFormatter<byte>` | Original `SaveOwner.SerializedAvatar.Bytes`; the only array element type demonstrated by the original Global/Party/ruleset serialization members. |

The existing list/dictionary roots for the global settings remain necessary. The
table gives additional formatter selections missing from the earlier
`FormatterInstances`-only closure. The original reflection and ISerializable
locators return instances directly into `StrongTypeFormatterMap` and
`WeakTypeFormatterMap`; both are now captured by the actual host oracle.

`SerializableFormatter<T>` has constraint `T : ISerializable` and uses original
`GetObjectData`/deserialization constructors. `ReflectionFormatter<T>` has a public
parameterless constructor and a public `ISerializationPolicy` constructor. Both
inherit public `void Serialize(T, IDataWriter)` and `T Deserialize(IDataReader)`
from `BaseFormatter<T>`. Merely rooting `ComplexTypeSerializer<T>` does not
explicitly instantiate the formatter chosen by
`Activator.CreateInstance(MakeGenericType)`. Its `GetBaseFormatter` calls the
**strong** `FormatterLocator.GetFormatter<T>`, which disallows weak formatters.
Therefore actual reflection list element formatters and serializable roots for
nonempty Party/ruleset lists must be compiled explicitly.

`PrimitiveArrayFormatter<byte>` has a public parameterless constructor and inherits
public `Serialize(byte[], IDataWriter)` / `byte[] Deserialize(IDataReader)` from
`MinimalBaseFormatter<byte[]>`. Its generic virtual calls need actual byte roots:

```csharp
// Never invoked: preservation roots, not a replacement save fixture.
new BinaryDataWriter().WritePrimitiveArray<byte>(null);
byte[] bytes;
new BinaryDataReader().ReadPrimitiveArray<byte>(out bytes);
```

Both original reader/writer classes have public parameterless constructors and
public `(Stream, DeserializationContext)` / `(Stream, SerializationContext)`
constructors respectively. The public methods are
`void WritePrimitiveArray<T>(T[])` and `bool ReadPrimitiveArray<T>(out T[])`, with
the original `T : struct` constraint. The native avatar byte array already passes
the original binary writer/reader host oracle.

`GenericEqualityComparer<string>` is internal to the original BCL and appears only
through a **weak** formatter request. Original `FormatterLocator` retains its
non-generic `WeakReflectionFormatter(Type)` fallback when generic instantiation
throws the expected AOT `ExecutionEngineException`. It needs linker preservation,
not an invented inaccessible C# closed generic type. Original SDK weak array and
serializable fallbacks similarly cannot replace strong list element formatters.
No multidimensional array or custom generic formatter was demonstrated on this
native metadata path.

The only external Odin serialization API callers in the 71 original custom
assemblies are `SaveData.SaveGlobalDataExecutor` and
`SaveData.<LoadGlobalData>d__95.MoveNext`, both typed as `GlobalData`. Campaign map
snapshots retain the separate original `BinaryFormatter` path. These observations
bound the Odin roots; they do not prove every binary map/save graph on a headset.

The supplemental host fixture contains nonempty original Campaign slot metadata:
one owner with a byte-array avatar, independent item flag, character ID/level and
display-name tuples, timestamp and run identity. A second fixture contains native
ruleset metadata. Original constructor, formatter and checkpoint-refresh bodies
run unchanged against a disposable root. PC/reflection/poisoned-all-345-emit-site
streams must match byte for byte, including write/read/write. No gameplay map or
real account/backend token is created. Original DLL hashes are checked afterward.
