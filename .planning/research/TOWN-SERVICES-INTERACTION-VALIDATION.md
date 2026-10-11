# Town service interaction validation

The offline regression harness runs production interaction code in **Unity
2021.3.5f1 play mode**, with real transforms, colliders, CanvasGroup, EventSystem,
Selectable and Button. It does not launch Gloomhaven or require a headset.

Build 539 validation: **617 production assertions and 22 compiled negative controls**.
All three services are exercised with the option initially off and through repeated live
off/on cycles. Checks cover exact descendant parents, original portrait enablement, one
ordinary conversion owner, native selection preservation and ordinary fitting/placement
flags. A held sample cannot select after disabling, even before the next presentation tick;
still-held cancellation and re-enabling cannot revive a stale gesture. A fresh gesture still
selects correctly. Three option-specific negative controls prove opening, release fencing
and restoration of the ordinary lifecycle are tested. Retained evidence is under
`.planning/debug/town-build-539/interaction/`; the complete worker run is
`/home/claw/gvr-town-toggle-tests/.planning/debug/town-service-interaction/run-hh_ex53d`.

## Run

```sh
python3 scripts/check-town-service-interaction.py
```

The default editor is `/home/claw/unity-2021.3.5/Editor/Unity`; override with
`--unity` or `UNITY_PATH`. The harness requires the .NET SDK and a real
`UnityEngine.UI.dll`, found under the checkout's built Unity asset project or
`ressources/GH_Data/Managed`. Pass `--unity-ui` for another location. Metadata-only
`libs/RefAsm` assemblies are unsuitable for this runtime test.

`--source-root /path/to/checkout` binds another checkout read-only, useful while
an integration agent updates production code. `--output-dir` changes the evidence
directory. Every invocation creates a fresh temporary project beneath the
gitignored `.planning/debug/town-service-interaction/`; it does not reuse or alter
the asset-build Unity project. Generated source copies, build logs, source SHA256
hashes, Unity log and `results.txt` remain there for inspection.

`--no-negative-controls` runs only the positive production fixture and is a quick
iteration aid, **not** a complete validation run. The normal command also compiles
every deliberately broken variant. A compiler failure is a test infrastructure
failure, never evidence that a negative control was detected. Each negative control
must reach its specific runtime assertion; an unrelated exception also fails.

## Production binding and boundaries

The complete `TownServiceToken`, `TownServicePresentation` and town-service modal
handoff partial compile directly from the selected production checkout. The
`ReleaseForComposite`, `BeginGrab`, `HealDeadHeld`, `CancelAll` methods, the held
release branch of `ProximityGrabber.Tick`, and the relevant grab interfaces are
extracted verbatim. The release-branch wrapper supplies only an entry point and a
null-held guard; its trigger/grip condition and native release callback are the
production statements. Source binding fails explicitly if expected extraction or
mutation anchors drift.

`Boundaries.cs` declares small substitutes for game controllers, tracking,
configuration, the visual mirror, station animation, native conversion internals
and section construction. This avoids running game singleton/controller startup.
The conversion substitute uses real Unity parenting and can deliberately defer
restoration or retain an owner. The real handoff code must detect these failures.
The fake native window emits the real UnityEvent synchronously before marking its
continuation. The hover adapter reproduces native enhancement preview changes to
`SowingCard` and `cardHolder.Card`, leaving committed `selectedCard` intact.

These boundaries mean the harness proves the production **decision and orchestration
logic**, not complete CanvasConversion/adopted-camera rollback, game affordability,
payment or multiplayer authorization, visual mirror fidelity, tracking or rendering.
Those remain covered by their own checks and runtime review. In particular, one
real Unity Button invocation is not evidence that an entire in-game purchase was
completed correctly. No headset outcome is claimed.

## Covered behavior

- Merchant, temple and enchantress owner changes invalidate a held sample; buy/sell
  mode and committed enhancement-card changes do too. A reused `AbilityCardUI`
  wrapper cannot retain the identity of its previous underlying card.
- Both context and pooled identity are rechecked on release before the next frame
  update. Captured session mismatches cancel independently of normal disposal.
  A native close/reopen in one frame retires the old session immediately.
- Actual `BeginGrab` clears prior highlight, grabs and re-enters hover without
  changing the committed selection context. Native hover previews stay independent.
- A real drop over the work mat dispatches one real Unity Button click. Repeated
  release, tracking loss, disabled controls and outside-mat drops cannot select.
- Both `CancelAll` and hand healing take explicit cancellation routes, including
  frames with `TriggerUp=true`. Existing grabbables without the new optional
  cancellation interface retain their release callback. A token which internally
  cancelled its hold causes the production hand filter/healer to clear ownership.
- UI handoff refuses a target still in its old host, a wrong original parent (also
  when the original was a scene root with no parent) and a
  lingering active conversion owner. Re-enrollment preserves pose and opening state.
- Native hide retires parent context before reverse-order section teardown. Native
  continuation completes synchronously without station animation sampling. Failure
  after two section conversions rolls them back and re-enrolls exactly one native
  context with the original parent.

## Recorded verification

The 2026-09-21 focused run against the integration checkout passed **77 runtime
assertions and 19 independently compiled negative controls** in Unity 2021.3.5f1.
Evidence: `.planning/debug/town-service-interaction/run-7k9dkji3/` in the
`town-service-tests` worktree. This run includes the explicit cancellation interface
added after base commit `b9e70c28`; it does not claim that the earlier base contained
that fix. It also includes the null-inclusive original-parent guard, held-content
accessor and bounded held-mirror refresh. The evidence's `source-hashes.json`
identifies the tested production bytes.

The negative controls remove owner, mode, committed-card, pool-card, release-time
context, release-time identity, session, cancellation-origin, healing-origin,
hand-filter, tracking-pose, single-click, original-parent, null-parent, active-owner, LIFO,
parent-first and native-hidden guards, plus replace the committed card with the
hover preview. Each fails the corresponding named behavior assertion.

Only focused harness compilation/runtime and Python syntax/whitespace checks are
part of this worker lane; repository-wide integration checks belong to the primary
agent.
