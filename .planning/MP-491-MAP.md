# Build 491 map-card regression review

## Evidence and scope

Base: `48330b22` (`dev`, ModBuild 490). Both supplied `LogOutput.log:17`
and `remote/LogOutput.log:17` identify ModBuild 490. The main checkout's
`regression_3d_umgebung.jpg` shows plain brown local card silhouettes and
patterned remote backs. `regression_board.jpg` shows an incomplete scenario
card face; that shared artwork path is covered by the artwork lane.

The map is public, including other players' fans and held cards. Scenario
selection privacy must not be added to map rendering. This review found no
closed map gate in the supplied failure: the peer loadout resolved correctly,
then construction of its front failed.

## Confirmed failure chain

- Main log 12756 starts `RemoteCardArt clone failed (Original card group count
  exceeds appearance bound.)`. There are 19,914 instances in the main log and
  11,242 in the other player's log (first occurrence there: 2282).
- Remote log 2293 reports the gate **OPEN**, the named map character, and all
  ten loadout cards matching the arc plus fist, while the fan displays backs.
  Main log 15246 reports the same result for the other peer. This rules out a
  missing character key, count mismatch, or deliberate concealment as the cause
  of those observed fan failures.
- Local `MapRoomHand.PrintPendingFaces` also uses `RemoteAbilityCardSource` and
  `RemoteCardArt`. A failed clone returns `FacePath.None`; the caller destroys
  the incomplete art and leaves the plain local `VRCard` body. Its retry runs
  at 5 Hz. Neither log contains a successful `MAP-ROOM HAND FACES` line.
- `RemoteHandFan.PrintMapFace` attempts that same pooled artwork construction.
  Failure keeps the remote body material in its patterned-back state and
  leaves the print key unset, so the next draw retries. Held map cards use the
  same pooled source through `RemoteHeldCardFace`.

Build 490 made complete native reset construct `CardAppearanceBindings` as part
of every clone initialization. Its eight-group appearance-transport bound became
an exception in ordinary original-prefab construction, before original artwork
could initialize. Repeating that deterministic failure cannot load a front.
The repair therefore belongs in the shared artwork/binding implementation,
including a complete transport representation for the original group hierarchy;
changing the map secrecy gate or weakening positional source validation would
not repair the failure.

## Map-specific audit result

No independent rendering mutation is required in the map caller files:

- A local failed print remains pending and retries; it never latches success.
- A remote failed print retains an unset card key and retries on the next draw.
- Cached successful fronts continue asynchronous artwork maintenance.
- Map held source resolution uses the original character/class-pool address,
  independent of the currently selected character.
- Fan projection retains actual held holes and removes only the held source
  seats; unrelated held characters do not remove seats from the visible fan.
- A same-size map loadout edit resolves again on the next draw.

These paths depend on the shared artwork fix. The build 490 pure projection and
source-address tests verified card identity and seat order; they did not construct
an original Unity prefab with more than eight CanvasGroups. They therefore could
not detect this runtime construction failure. A passing protocol test is not
proof that a card front exists in the rendered picture.

## Hardware follow-up

Verify readable local and remote map fans, both held slots, a character change
while holding an old character's card, and a loadout edit without a count change.
Map fronts should remain public throughout. Also verify the scenario card shown
in `regression_board.jpg`; its incomplete original artwork is a separate
observable consequence of the shared lifecycle failure.
