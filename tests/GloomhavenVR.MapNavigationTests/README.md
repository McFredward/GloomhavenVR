# Native map navigation regression

Run `bash scripts/map-navigation-tests.sh`. The fixture extracts and executes the
complete current production `MapLocationInteractor.SetHover` and `Release`
bodies, both navigation entry helpers, and their current-state predicate. The
existing production `MapInputGate` is linked directly.

The tracked native `MapLocationSelector.Update` is copied verbatim from the game.
Whenever the read-only decompiled reference is available, the script verifies
that complete method before running it against the same concrete state inputs.
Hosted CI uses the tracked fixture. The test does not replace native eligibility
with a second implementation in its boundary.

Checks include ordinary world-map/loadout/hover transitions and exact location
payload, protected story/private-quest/service/travel/reward states, unknown or
null current state, absent navigation, pointer callbacks taking ownership during
dispatch, native map locking, and room teardown with pointer cleanup, original
ray-mask restoration, and adapter disposal. Two causal negative controls remove
the entry and exit gates separately; each must fail its precise ownership
assertion rather than merely fail to compile.

Unity graphics, hardware pointer input, native state implementation internals,
Harmony ordering, and actual native multiplayer transport are not simulated. The
boundary records state exit and dispatch counts to prove whether the production
adapter invokes a transition; it does not claim a successful headset picture or
packet delivery. There are no wire messages or role-specific continuation calls
in this repair.
