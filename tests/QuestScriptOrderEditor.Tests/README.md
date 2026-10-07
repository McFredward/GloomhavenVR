# Original execution order Editor checks

These fixtures execute the production `QuestOriginalScriptOrders` helper against
Unity 2021.3.5's actual Android-target Editor and imported MonoScripts. They must
run in disposable projects, never the recovered game or a shared prepared project.

Compile `OriginalOrderFixture.cs` to `Assets/Plugins/OriginalOrderFixture.dll`
against that Editor's engine/reference assemblies. Copy the production helper and
`QuestScriptOrderFixture.cs` into `Assets/Editor`, include UGUI 1.0.0, and create an
empty `quest-script-orders-fixture.marker` in the project root.

Run `QuestScriptOrderFixture.RunReadOnlyPackageControl` with registry UGUI. It
verifies that a genuinely ignored package setter fails and rolls back an earlier
DLL mutation without publishing a success receipt. Run `QuestScriptOrderFixture.Run`
in another private project with UGUI embedded under `Packages/com.unity.ugui`
(omit the package's loose `Tests` sources, as the production builder does).

Invoke either method with the actual Editor's `-batchmode -nographics -buildTarget
Android -projectPath <private-project> -executeMethod <method> -logFile <private-log>`.
The methods exit with an explicit success/failure code and write JSON check receipts.
The positive suite covers exact early/late/package orders, explicit zero replacing
a nonzero value, the original order 32001, provenance, and invalid mappings
that must leave both existing orders and the prior receipt intact. Missing
unreferenced default-order types are recorded explicitly rather than synthesized.
Static utility exclusions require both original metadata and exact imported-type
evidence; malformed flags, base references, actual types and referenced exclusions
must fail before changes.

`QuestScriptOrderInventory.Run` performs a read-only census of every expected
type against the actual imported script set and loaded assembly metadata. Copy it
into `Assets/Editor` in a private clone of a prepared project's DLLs, Packages and
ProjectSettings, retaining the input manifest and private marker. Its JSON report
separates missing referenced/nonzero targets from non-Unity types that cannot
participate in Unity lifecycle ordering. It does not instantiate game types or
invoke static methods.

These checks establish importer semantics and fail-closed mapping behavior; the
actual prepared-game receipt and headset startup remain separate evidence.

`dotnet run --project tests/QuestScriptOrderEditor.Tests/QuestScriptOrderConsumer.Tests.csproj`
compiles the production consumer with explicit Unity API stubs. It checks the
source-backed Campaign TestRunner exclusions, unchanged Startup exclusions,
retained authored order, and malformed source/type/hash/reference controls before
any importer mutation. This is a portable contract check, not native Unity proof.
An optional prepared-project path after `--` performs a read-only preflight of the
actual 113 TestRunner exclusions by copying only its three manifest files to a
temporary fixture; it never runs Unity or modifies the supplied project.
