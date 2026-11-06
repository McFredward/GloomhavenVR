# Peer retirement regression proof

Run `scripts/peer-retirement-tests.sh`. The suite compiles the complete current
`NetAvatarDriver.ForgetPeer`, `DestroyAllAvatars`, `OnEnable` and `OnDisable`
bodies, its Results forwarding callback and current production `TickGuard`;
it does not copy their recovery logic into tests.

Unity object cleanup, native presentation/held-object owners, transport teardown,
timing and logging are explicit portable ports. Unrelated sent-state assignment
fields use opaque storage. This proves method ordering, exception isolation,
identity/snapshot handling and report cadence against those boundaries. It does
not execute the native cleanup internals, Bolt, Unity callbacks, input or headset
pixels, and does not demonstrate that an arbitrary failing destructor released
its own resources. Failures stay logged; no native decision or ACK is generated.

The historical file under `scripts/peer-retirement-tests` preserves all three
original method bodies from `cd827d688` (ModBuild 757). Each old body is compiled
separately against the same executable fault scenarios and must fail. Additional
controls remove a specific guard, detach ordering or snapshot protection.

Build761 additionally executes the original `OnEnable` and Results visibility
forwarding callback against an explicit event payload/pose port. The existing
complete `OnDisable` body must detach that exact handler; a separate compiling
causal control leaves it attached and must fail. This proves subscription
retirement, not native Results bootstrap, Photon or headset output.

Per-case source snapshots, source/assembly hashes, console output, validation and
canonical cache-cleanup receipts remain in `.planning/debug/peer-retirement`.
