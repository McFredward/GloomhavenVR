# Build648: terrain regression diagnosis and rollback boundary

The maintainer reported worse fully loaded Steam Frame performance after647.
The new capture is frozen in the main checkout at
`.planning/debug/frame648/hardware647-inputs.tar.gz`. Root integration selected
a restoration of646 after the maintainer explicitly requested rollback unless
a net improvement over the previous hardware run could be demonstrated.
An offline geometry certificate or Editor render cannot establish that net gain.
This note records a bounded647 production reproduction, not a shipped648 repair.

## Source-proven unnecessary work

`Surface.StepGeometry` correctly retains exact original geometry when the
requested coarse bank endpoint has no triangle saving. However,
`Surface.WantsSubstitute` independently accepts every enabled room floor when
`CheapWallShadingOn` is true. Active world simplification does not remove this
shader-only request, although its owner already provides native-slot simplified
shading. The source then traverses full current scope/material/property/proxy
admission and can acquire an original-geometry private lease.

This is separate from the useful coarse derivatives and settled floor groups.
Skipping useless individual proxies could preserve both, but the worker's
proposed change was not integrated or validated after the rollback steering.
The existing live property-effect refusal also runs after private pose,
material-array and complete renderer-state copy work. Moving that refusal was
only a private experiment; no resulting runtime or headset gain is claimed.

## Bounded actual Unity reproduction

The isolated worker starts from647 commit
`7288d59ab3a360730d9085bd759bfa55c23ee17f`. Its production terrain files remain
byte-identical to that commit. A private fixture adds320 current native floor
renderers:256 repeat an independently exact fallback endpoint, and64 repeat a
genuinely reduced endpoint. Native mesh identities, role/configuration and the
world factory are explicit fixture boundaries. Actual Unity meshes,
`Camera.Render`, production ownership/morph/lease methods and native primitive
read wrappers execute.

Observed old647 work in one fully settled camera invocation:

| Observation | Count |
| --- | --- |
| Prepared floor renderers |320 |
| Useful reduced endpoints |64 |
| Camera floor candidates |320 |
| Surviving private floor leases |320 |
| Native material-list reads |320 |
| Native property-block presence guards |320 |

The256 unreduced endpoints account for80% of these private admissions and add
no geometry reduction. The fixture fails the expected native-retention assertion
as intended. It does not establish what fraction of the real Frame sources have
this exact cause, GPU busy time, total headset FPS, or the result of a repair.
The hardware counters alone do not distinguish exact-endpoint requests from
current material/property/scope refusals.

Worker evidence, retained for primary-agent curation:

- `.planning/debug/frame648-terrain-reproduce/run-8ofz5lmp`: initial old647
  production reproduction and the failed native-retention assertion.
- `.planning/debug/frame648-terrain-old647-counts/run-9hf9_yz8`: repeated old647
  production reproduction with the explicit operation receipt at `unity.log`
  line642. Source stability is `unchanged=true`.
- `.planning/debug/frame648-private-experiment/`: unshipped experimental runtime
  diff and complete experimental source snapshots. No positive fixture, focused
  complete suite, strict build or hardware pass is attributed to that experiment.

Both failed attempts retain source copies, input hashes, compiled assembly/PDB,
actual Unity logs and failure receipts. The bank/native inputs are referenced by
their immutable source hashes rather than copied into this small handoff.

The primary agent owns648's restored bank/runtime, final validation, push,
handoff and cleanup. This worker diagnosis introduces no option, default,
gameplay/wire change or claim that647's asset triangle savings improve Frame FPS.
