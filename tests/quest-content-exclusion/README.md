# Campaign content exclusion fixture

Addressables1.19.19's `AddressablesPlayerBuildProcessor.GetStreamingAssetPaths`
adds any existing `Addressables.BuildPath` as `aa`, even with
`DoNotBuildWithPlayer`. Campaign builds therefore temporarily exclude both the
owned ZIP and the exact native Android directory. Their generated linker XML
remains in `Assets/Quest/CampaignLink/link.xml`; original collisions are rejected.

The schema1 journal at `QuestCampaignEvidence/excluded-payload/journal.json`
is flushed before moves. It binds the input key, exact source/temporary pairs,
file sizes/SHA256s and current/previous owned linker hashes. Planned/excluded
recovery preflights all pairs before moving any; both-present/both-missing and
unknown paths fail closed. Restored journals own no excluded payload and allow
legitimate subsequent content regeneration, including a missing native directory
after an unsuccessful later content build. Constructors still require a complete
current native directory/link. Completed journals retain linker ownership.

The executable fixture compiles the actual production C# source with small Unity
API boundaries and performs real filesystem operations, including an independent
child process that exits without Dispose.99 assertions cover scope lifetime,
recovery, conflict/escape/symlink negatives, exact constructor rollback and
original exception identity, and changed-input/linker updates before/after a
process exit or failure. It establishes no native Player or headset outcome.

Example developer invocation (Linux, separate private output):

```sh
"$quest_unity/Editor/Data/MonoBleedingEdge/bin/mcs" -langversion:latest \
  -define:GHVR_QUEST_GAME,UNITY_EDITOR -r:System.Web.Extensions \
  -out:"$quest_fixture/witness.exe" \
  unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignContentBuild.cs \
  tests/quest-content-exclusion/Stubs.cs tests/quest-content-exclusion/ContentBuildWitness.cs
"$quest_unity/Editor/Data/MonoBleedingEdge/bin/mono" "$quest_fixture/witness.exe" \
  "$quest_unity/Editor/Data/MonoBleedingEdge/bin/mono" "$quest_fixture/fresh-cases"
```

Private evidence: `/home/claw/quest3-local/full-shader-validation/aa-exclusion-v1/`.
A separate metadata compilation against the actual pinned Unity2021.3.5 Editor,
Core/JSON modules and native Addressables DLL succeeds without Unity launch.
`metadata-compile.json` pins source/assembly/reference hashes and the proof limits.
Central Python recovery consumes the same confined journal before outer builder
rollback; it must not delete an excluded native directory during cache cleanup.
