# Build734 Quest menu AOT and startup consumers

The actual optimized Release audit exposed13 integration gates: the new
`HarmonyTargetMethods` metadata, an unresolved native menu lifecycle selector,
two field-reader factories containing desktop Reflection.Emit paths and the
consequent uncompiled patch registration. The initial Debug audit has different
optimized-IL contracts and is not product failure evidence. The adapter consumes
only Cecil metadata; no production mod initializer, selector or Unity host runs
during this qualification.

`tools/QuestWeaver/MenuDepthCompatibility.cs` closes the exact current contracts
before ordinary discovery:

- Pin the complete lifecycle selector, its generated iterators and original
  owner/card descriptor tables. Resolve their exact native assemblies, inherited
  zero-argument methods, assembly filters and deduplication. The independent
  current original-target roster contains25 methods. Retain the original
  selector iterator and canonicalize only its private method name to
  `TargetMethods`, which the existing AOT runtime registry already executes and
  fences against the compiled target set. Ambiguous or changed selectors fail.
- Qualify both complete readonly field factories and their generated closures,
  including native field identity/type, field lookup flags, Android comparison,
  null/type guards and catch behavior. Prove the Android branch, then remove
  only statically unreachable desktop instructions. The actual `FieldInfo`
  closures retain identical qualified IL; no field reader stub, actor write or
  generic dynamic-code whitelist is introduced. Original PC source is retained.
- Qualify the exact upstream DLL-relative story getter. Only the privately
  converted Quest mod reads `EnvironmentBundleDirectory/MenuDepth/Story`.
  Desktop source and existing menu fixture dependencies remain unchanged.

The Harmony facade adds the inert `HarmonyTargetMethods` attribute metadata.
Runtime registry code needs no change. The final actual mod audits with226 patch
classes,340 attributed hook methods,588 concrete hooks and zero unresolved gates.
Its actual weave writes369 targets, verifies seven protected original types and
retains five explicitly qualified substitutions. Original game DLL inputs remain
immutable; writes go exclusively to private converted outputs.

The player has a separate downstream content gate. The original
`QuestGameBootstrap.ValidateModBanks` rejected every manifest beyond its four
native banks, even after the signed PC installer admitted the story suffix.
Commit `d2de643af` preserves the exact four-bank prefix and diagnostic startup's
single bank, while Full Campaign may append one bounded story manifest followed
by1–4096 strictly sorted, unique lowercase32-hex `.gmd` paths in the exact story
namespace. Existing general content validation still supplies hash, duplicate
and safe-path checks. No renderer, game state or bank loading order changes.

The progress UI has a separate consumer correction in `8f52f6bf8`: both Startup
and current Game can report five preparation contracts, with different actual
children. The denominator therefore cannot prove `startup-compute` belongs to
Game. The UI recognizes `menu-story-content` and displays only checkpoints
observed in the current attempt for that ambiguous scope. Actual source ordinals,
completed/remaining counts and the monotonic stage percentage remain intact;
the unchanged four-checkpoint legacy plan remains explicitly known. Builder
production and its frozen orchestration AST are untouched.

## Focused evidence

This is a bounded worker scope within the integrator's class-C rebase. The
integrator owns the complete final union gate; these are focused results:

- Final strict Weaver Release build:0 warnings/errors.
- Actual menu compatibility fixture:30 assertions, including ten causal
  guard/closure/selector/descriptor/native-field/story-path controls. It performs
  a real weave and verifies unchanged original input bytes. Unknown target or
  reader variants close the build gate.
- The existing current-mod consumer separately passes840 assertions before the
  final additional selector-ambiguity guard. Final focused qualification covers
  that guard and the complete actual positive weave; the earlier result is not
  relabeled as a new final-input complete gate.
- Final managed Android mod SHA256
  `df940c4e4ec97a3034438e6195780f801a67db39ae5be7434c194545fcc386a3` is audited
  and actually woven again with zero gates. Cecil reads its two actual embedded
  menu resources and proves byte equality against producer `ba75e6e9...`.
- The complete affected `quest-startup-loading` suite passes25/25 variants
  in73.7seconds. Production and inert-comment variants each pass450 assertions;
  all23 existing causal mutations are rejected. The real compiled Bootstrap
  admission method now exercises both legacy banks and valid numeric story
  content, plus17 new positive/negative suffix assertions.
- All94 tests in the affected graphics, whole-stage and bounded-event progress
  consumer files pass in5.17seconds. Gameplay/Startup five-step plans, late
  observations and retry ownership are exercised. A causal control restores
  the exact old denominator-only rule and the actual Store/UI fixture rejects
  its invented work. Its common completion cleanup records no remaining cache.

Initial development failures (a compiler local-name collision and a type-token
inspection null) remain in the evidence; both are repaired before the final
source freeze. Resource closure's earlier failed test removed an optional tail
row instead of a mandatory producer; its corrected mandatory-Editor control
passes, with no product tolerance change.

All executed source, input bindings, DLLs, reports and failed/passing logs live
under ignored
`/home/claw/quest3-local/build/evidence/quest-dev734-rebase-20261028/resources/`.
The `weaver/run-i79pt40n` archive contains final metadata/resource proofs and both
actual weave outputs. `weaver/startup-loading` and `startup-suite-report` retain
the complete runtime consumer evidence. Processes have exited before cleanup.
Weaver project caches are removed through `cleanup_completed_run`. The central
startup cleanup's512-directory discovery bound initially prevents its sweep;
the same API then cleans each of the25 stopped fixture projects independently,
removing all50 generated `bin`/`obj` trees while retaining sources, assemblies
and IO/results evidence. No shared input or another worktree is swept.

These checks prove static AOT integration, byte-bound resource inclusion and
compiled managed startup admission. They do not establish Android GPU effect
pixels, XR startup on a device or headset motion quality. No full APK, local
release ZIP or game/campaign shader conversion is produced for this scope.
