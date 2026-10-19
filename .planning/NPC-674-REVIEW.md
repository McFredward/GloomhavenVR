# Build674: recover merchant preparation after extended scenario sessions

Integration base: `f36c1ff3a1bec4112394b06dec66282bb5b4b130` (`dev`).
Semantic version remains the existing development version1.1.1. This repair does
not publish another release or modify the published1.1.0 archive.

## Water status

The complete water repair is already present on `origin/dev`: Build673 includes
`ea09d7ce0`, `e6ef65027`, `6a9c7774c` and `9a112b0fc`, with final evidence recorded
by `0738dc920`. It covers late native material publication, all45 audited terrain,
pond and DLC films, and alpha-compatible private film queues. Build674 leaves
that production code, original water materials and shader assets unchanged.

The supplied October15 local logs identify Build671/assembly1.1.0.0, so they are
not hardware verification of673. The remote vanilla log is from October11 and
is not a paired participant in the new long-session report. See
[WATER-673-REVIEW.md](WATER-673-REVIEW.md) for the actual native coverage and its
separate Addressables/scenario/headset boundary.

## Observed merchant failure and limits

Frozen local LogOutput.log contains seven `Original widget publisher unavailable:
prepare:` reports with empty messages (lines12502,13496,24523,25303,28933,29500,
29980). They start after the first scenario; later resident loading times out.
The publisher's preparation step also repeatedly consumes about80–115ms on
retries. No corresponding persistent-merchant-stock failure is recorded. An
unready NativeTemplates bank prevents public catalog construction, leaving the
visible resident/cabinet without its cards. The old message-only catch loses the
exception type and stack, so it cannot identify the exact offending object.

Read-only native review rules out a generic stale-HUD explanation as sufficient:
ordinary map teardown already retires the complete town lifetime. Instead, the
required bank calls a global Resources asset census. One unrelated native metadata
getter can throw through that optional census and abort the whole bank.

Two source-proven failure classes are exercised. A generated mip sprite can
outlive its original, and OriginalFor returns that destroyed original. Key used
to read native properties or return its old cached identity after unwrapping.
Separately, the shipped TMP1.3 legacy spriteCharacterTable getter can throw on
an unfinished asset through UpgradeSpriteAsset. The independent native probe
shows that TMP exception has a nonempty message and can be a one-shot failure,
so it does not establish that this specific TMP object caused the persistent
empty-message failure in the user's session.

## Repair contract

Reject lost native provenance after normalization, before cached-ID lookup;
never substitute the generated mip sprite as canonical artwork. Isolate only
recoverable native metadata failures in optional per-object discovery. Required
Key/RegisterOriginal/template validation and identity conflicts remain strict.
A malformed unrelated asset must not prevent later valid assets from being
registered or an otherwise valid merchant bank from becoming available.

Keep diagnostics useful and bounded: discovery reports are limited per registry
lifetime, and publisher failures retain exception type/reason even for an empty
message. Full stacks are guarded Debug output within the existing report cadence.
No gameplay inventory/gold/model writes, native callbacks, visual/layout defaults,
multiplayer authority, wire records, assets or water behavior are changed.

## Correct water test registration

The previous dev CI was already red. The native water fixture had been placed
in both local and CI groups, although it needs the game-exact Unity editor,
actual Unity runtime modules and the original native asset census. Hosted
metadata references cannot execute that proof. Its inventory lock was also
missing the new suite. The repair keeps all217 local suites, including the full
native water test, and restores the intended98 hosted suites plus16 source checks.
No workflow, trust policy, local water assertion or negative control is removed.

The registry/topology repair passes18 runner tests (16 passing and two unchanged
optional compiler skips), five workflow tests, complete disjoint shards25/25/24/24
and both causal registration controls. The existing water production488,
queue-pixel17, eight lifetime controls, wall19,511/all29 and native figure731
coverage remains inherited for byte-unchanged inputs.

## Final integration evidence

The complete focused native-bank lifecycle suite passes245 production assertions
and all25 causal controls (26 variants), including four new failure/diagnostic
controls and all21 existing controls. Its exact seven source/fixture inputs are
hash-identical after integration. Independent shipped-TMP execution passes nine
assertions and final source review approves the checkpoint. Earlier attempts
exposing a self-mutating legacy test input and earlier control-message ordering
remain retained; they are not counted as product passes.

Root integration passes all16 source checks, strict Debug/Release0 errors/0
warnings, direct300518 real-Unity wire goldens, bilingual docs and all bank checks.
Public surfaces remain666 config keys/236 patches/4795 log tokens; wire Version3
and next-free118 stay unchanged. Compiled comparison is1257 ->1257 C# types with
no additions/removals: two implementation types and eight inlined674 consumers
change. The generated project differs only in decompiler-relative reference hint
paths, becoming byte-identical after one output-directory-prefix normalization.
The copied673 compiled baseline is valid because src/ is unchanged between its
bound9a112b0fc source and the integration base; the old shared guard is untouched.

Source-bound mip lookup replaced a fixture-only identity adapter. Integration
therefore repairs the separate native-peer wrapper to seed the actual mapping,
and gives the existing card-mip/flame external logging boundaries the newly used
passive logger members. Full focused native-peer owner/observer execution passes
45775/45862 assertions and all seven controls in separate Unity processes. The
complete card-mip six variants and flame1156 assertions/all six clock controls
plus original billboard control pass. Their original production rendering and
causal assertions are preserved; compilation failure is never a passing control.

Results, exact source/assembly bindings, compiled/surface comparison and the final
hosted CI outcome are retained under `.planning/debug/npc674/`. Frozen hardware
inputs, failed fixture attempts, original native DLL hashes and independent
review evidence are retained. Focused results are not a new complete217-suite
local gate. Actual long-session recovery and the HMD picture still need hardware
verification on the updated Debug build.

The final source-changing push runs normal full hosted CI. Its outcome is retained
as an ignored receipt after completion, without another tracked documentation-only
push that would unnecessarily start all hosted tests again.

Every owned run reaps its processes before completion-cache cleanup. Logs,
sources, manifests, capture/native inputs, .keep pins and current Debug binaries
are preserved. Unrelated trailer edits are outside this repair and remain intact.
