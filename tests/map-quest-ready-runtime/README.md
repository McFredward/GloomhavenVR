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
