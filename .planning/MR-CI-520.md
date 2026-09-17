# Build 520 — initiative MR bounds and CI evidence reuse

## Request and evidence

The maintainer approved reusing successful dev validation instead of repeating the
full suite on unchanged PR/main trees. Releases must still originate from main.
The same hardware report identifies excessive vertical MR initiative backgrounds
in `.planning/debug/mixed_reality_background.jpg`.

The local `LogOutput.log` identifies ModBuild 519 / assembly 1.0.4.0. Its 302 lines
contain no Error/Fatal entries or exception messages. The remote drop still names
build 500 and is not matching evidence for this session. The screenshot shows the
initiative portrait above the board and long opaque strips down the board centre.
Their precise native graphic origin is not logged; the source mismatch below is
established independently of that attribution.

## MR bounds

The initiative surface already fits only `initiativeTrackHolder`; the element
surface similarly declares `elementsHolder`. Build 516's backing measurement walked
the entire conversion target instead, admitting full-screen sibling graphics outside
those rows. The remote clone used the same overbroad walk. A whole-host artwork floor
could additionally retain excessive height after narrowing the measurement.

Scoped row measurements must follow the declared native root, preserve ancestor
clipping and actual visible row artwork, and exclude sibling geometry. The inert
remote clone uses the corresponding source-to-clone root. Normal floating windows
retain full artwork/overflow handling. Existing independent sampling, visibility
gates and smooth 150 ms resizing remain in place. No gameplay callback or original
UI layout is changed.

## CI behavior

Dev pushes and manual dev validation run the full suite. An internal PR can reuse
successful trusted dev evidence for its exact merged Git tree; otherwise it runs
the full checks. Fork PRs always receive full checks with read-only permissions.
The required `Build and gates` check keeps its name.

Release checks trusted full-test run/job metadata before building the actual main
commit. Reused PR checks cannot recursively become full-test proof. Missing,
superseded, failed or stale evidence blocks publication. Release-specific build,
provenance, bundle, version and archive verification remain. No proof artifacts
consume GitHub storage. See `docs/CI-CD.md` for the implementation and recovery path.

## Validation

- CI evidence: 23 fixture/local-Git test cases pass, including merge conflicts,
  different trees, forks, wrong workflow identity, failed/cancelled superseding runs,
  latest attempts, missing proof, stale proof and API failure.
- Release topology: 36 assertions pass; optional artifact cleanup: 20 tests pass.
  Actionlint 1.7.7 accepts both workflows. All 37 existing CI step names and all
  33 production harness commands remain; the PR surface comparison also runs on reuse.
- A live API check rejects the first still-running dev job as non-reusable. The first
  hosted run also exposed unnecessary full-history checkout in dev planning; only PR
  planning now fetches history. Dev reads its tree from a shallow checkout, covered by
  the workflow binding test. Runtime validation is unchanged by this CI-only follow-up.
- MR background: 250 runtime assertions and 41 source bindings; 13 runtime and
  three binding negative controls reject the broken alternatives.
- Native ink: 259 assertions, ten runtime and one binding negative control.
  These run the actual production measurement against oversized parents/siblings,
  ancestor masks, real portrait artwork, foreign/hidden roots and inert clone trees.
- Source surfaces: 625 config keys, 172 patch signatures and 4,731 log tokens.
  The sole new marker is `MR BACKING SCOPE`, emitted at Info for the scoped rows.

- Complete integration guard passes all 17 source checkers, all production harnesses
  and 254,565 wire assertions. Its exit 1 is the expected compiled-difference verdict,
  not a failed checker. The bundle remains 74,942,975 bytes / Unity 2021.3.5f1.
- Strict Release build: zero warnings and errors. Bilingual docs, shell syntax and
  whitespace checks pass. Patch inventory remains 130 classes / 197 methods.
- Compared with the private build-519 compiled snapshot: 13 changed types, one added,
  zero removed. Seven changes only propagate ModBuild 520; the six behavioral changes
  are the MR measurement/layout and its two remote call sites. `MrBackingScope` is the
  single added type. The retained build-502 comparison is 86 changed / 54 added / zero
  removed. No unrelated compiled behavior was found in the incremental comparison.

Hardware confirmation remains open: check MR
initiative backgrounds with one and several portraits, changing initiative order,
element rows, and matching remote boards. Also retain the prior normal-window
growth/shrink check to catch unwanted cropping of legitimate artwork.
