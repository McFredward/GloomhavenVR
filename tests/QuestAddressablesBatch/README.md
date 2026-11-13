# Native Addressables editing-batch witness

This fixture runs the actual production `QuestStartupAddressablesBuild` helper
with Unity 2021.3.5f1, Addressables 1.19.19 and the same Scriptable Build Pipeline
1.20.1 package as the full Quest project. It uses a private project with 24 tiny
authored TextAssets and contains no original game files.

Copy the production Editor file and this fixture into `Assets/Editor`, enable
`GHVR_QUEST_STARTUP` through the fixture's `Assets/csc.rsp`, and invoke these in
separate Editor processes targeting Android:

```text
-executeMethod QuestAddressablesBatchFixture.Measure
-executeMethod QuestAddressablesBatchFixture.Reopen
```

Measure starts from fresh private `Assets/baseline` and `Assets/batch` settings.
Both cases use native package schema creation and the same already-imported
TextAssets. Timing includes final `StopAssetEditing` and `SaveAssets`; it excludes
settings initialization and source TextAsset import in both cases. A native
AssetModificationProcessor counts every SaveAssets callback and a native
AssetPostprocessor counts actual group/schema imports. No callbacks are disabled.

Checks cover independent group/entry ownership, both native schema types, Group
references, PackTogether, address/GUID/label flags, original preload labels and
native Local.BuildPath/Local.LoadPath profile links. An existing partial group
must retain the schema's GUID while adding its missing native schema. Reopen
verifies the persisted objects in a fresh Editor process. A planted missing source
asset invokes production `Build`, must preserve its original validation exception,
and must permit immediate native asset import afterward (editing released).

The observed private measurement is a bounded comparison, not a prediction of
the full 131k-asset project or native content-build performance. In particular,
package AddSchema still invokes SaveAssets for each schema: batching avoids their
individual imports rather than pretending those saves disappeared. The grouping
strategy, bundle names, lifecycle boundaries, aliases and labels remain unchanged.

Private evidence: `/home/claw/quest3-local/full-assets/addressables-batch-v1/`.
The initial fixture-only compile failure (a nonexistent profile getter) remains
in `measure.log`; corrected `measure-v2.log` and `reopen.log` are the actual proof.
