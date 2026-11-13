# Campaign Editor scene cleanup

Run only in a new disposable Unity 2021.3.5f1 project, never the full generated
game project. Copy the production `QuestCampaignAssetValidation.cs` and
`QuestSceneRestorationFixture.cs` to `Assets/Editor`; copy
`QuestSceneLifecycleWitness.cs` and `QuestSceneReferenceWitness.cs` to `Assets`. Create the marker
`quest-scene-restoration-fixture.marker` in the project root, then execute:

```text
Unity -batchmode -nographics -buildTarget Android -projectPath <private-project> -executeMethod QuestSceneRestorationFixture.Run -logFile <private-log>
```

The fixture creates thirteen tiny source scenes and exercises production
validation/restoration with empty, one-scene and two-scene prior states. A wrong
original scene collection must retain its `InvalidDataException` and publish no
success receipt. A direct empty `RestoreSceneManagerSetup` call is a planted
control for the observed native build failure. The result also records
actual normal-scene `ExecuteInEditMode` callbacks and the availability of the
pinned Editor's public preview-open API. Unity 2021.3.5f1 does not expose
`OpenPreviewScene`; unsupported/private preview APIs are not used. The production
scene-open path is unchanged by this cleanup correction.

`scene-restoration-result.json` records actual Editor observations. These synthetic
scenes establish cleanup and callback semantics, not full-game asset fidelity or
headset behavior.
