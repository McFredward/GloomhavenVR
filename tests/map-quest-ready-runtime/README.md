# Map quest readiness dispatcher proof

Run `bash scripts/check-map-quest-ready-runtime.sh`. The runner needs .NET 8.
Its committed `NativeFixture.cs` contains ten verbatim original native method
bodies and permits the same checks on hosted CI. When read-only game source is
available beside the repository's common Git directory, the runner first pins
every body byte-for-byte against that source. `GHVR_NATIVE_SOURCE_ROOT` overrides
that discovered source location. An absent reference tree uses the committed
fixture; an existing tree with changed native methods fails the source pin.

The fixture compiles **the complete production `MapQuestReadyUp` and
`ReadyToggleParkClaim` classes**, including their actual Harmony seam handlers.
It also compiles unchanged native `UIReadyToggle.Initialize`, visibility methods,
`ShouldBeVisible`, `ReadyUp` and its permission guard, the desktop presenter's
installed click wrapper, and both native prompt click method bodies. No copied
test policy replaces the readiness dispatcher or those native method bodies.

The Unity objects, native singleton registry, native quest scene callback,
participant assignment, and gameplay transport are explicit boundaries. The
native callback is represented by preview count plus the real native visibility
methods; the transport records a native ready/unready request without fabricating
host validation. The fixture is a CPU causal proof, not a native scene, Unity
render, Photon session, state ACK, scenario load, or headset test.

The 224 assertions cover 25 two-through-four-player Flat/VR membership layouts,
desktop and console prompts, the sticky requested-visibility failure, capture
before room activation, reset/rebuild, native initialization delay, identical
quest identity rebuild, replacement native controller, cancellation, commitment,
host/offline/other decision refusal, native 2D prompt consumption before room
entry, native hover preview cleanup, callback failure containment, spectator
assignment, native visibility blocks/all-ready continuation, full-roster unready,
and current-singleton-only claims with bounded expiry/fallback and destroyed
Unity target invalidation.

Seven controls inject the original visible-toggle shortcut, prompt erasure on
reset, missing controller validation, omitted desktop cleanup, a stale singleton
claim, omitted native click consumption, and a claim on a destroyed native
target. Each must reach its named causal
assertion; a compiler/setup failure cannot count as a rejected regression.

The same runner also compiles the complete production
`MapQuestDepartureValidation` with 22 unchanged native bodies in
`NativeDepartureFixture.cs`. These include native `OnPlayerLeft`, the complete
controllable-state ACK coroutine, `Proceed`/`Reset`, both validated ready/unready
paths, native input/progress handlers, `ProxySetReadyState` and the native ACK
receiver. Source pins use the same reference discovery and portable fallback.

Its 63 assertions reproduce the false departure option, then exercise both map
phases, missing ACK/timeout, awaited players, unready/spectator host refusals,
VR/offline/nonquest/scenario exclusions and native participant-count barriers.
Client controls require a real enabled native Cancel after an actual departure;
ordinary `ReadyUp(false)` stays blocked. They cover synchronous and asynchronous
input, aborted progress, quest/controller/session/phase/commitment changes,
one-shot permission, and a ready departure revealing Cancel during ACK waiting.
A spectator departure may qualify only when it produces the same measured
blocked local state; the seam does not infer a departed player's previous role.

The Flat-host case passes the VR withdrawal through the **original unmodified
host `ProxySetReadyState` and `UnreadyPlayer`**. A normal withdrawal is refused;
the explicit native validated withdrawal is accepted and returned to the VR
client. A fresh genuine Accept then passes through the original host receiver,
waits for the actual native ACK receiver and emits one native `ReadyProceed`.
The transport/process switch, initial replicated ready snapshots, and MEC clock
are boundaries. No mod-owned quorum or accepted action replaces native policy.

Seven additional production controls disable the native departure option,
broaden withdrawal outside real input, lose delayed input permission, retain
permission after cancelled progress, omit controller/quest identity, or admit
nonmap initialization. Each must reach its causal runtime assertion after a
strict rebuild. The normal portable suite totals 287 assertions and 14 causal
controls; the membership-layout count remains 25.

An optional actual patch-registration proof runs separately:

```bash
python3 tests/map-quest-ready-runtime/run-departure.py --real-harmony
```

It uses HarmonyX 2.7.0 under Mono (`GHVR_UNITY_MONO` overrides executable
discovery), installs **the production `MapQuestDepartureValidation.Install()`**,
checks `Harmony.GetPatchInfo` for all seven exact targets and the two input and
progress prefix/finalizer state pairs, then runs the same native scenarios
without manual seam dispatch. Its 72 assertions include those nine additional
registration assertions. If Mono is absent, it explicitly skips and claims no
actual registration/runtime pass. The portable .NET 8 suite uses explicit seam
delivery; it does not need Mono or Harmony packages. Neither mode proves Unity
rendering, a live network session, loading or headset behavior.
