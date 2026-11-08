# Build765 — shared Results continuation lane

Scope: **B, bounded behavior**, based on the complete Build764 checkpoint
`128b6b6ca`. The parent integrates the final union, strict build, source gates and
compiled review. This lane ran only Results ownership/continuation and the
changed public presentation/input controls. No full gate, wire inventory,
release, ZIP, device writes or worker push.

Runtime/fixture commit: `bffb579af`. The worker also carries an equivalent
cherry-pick of the parent's Build765 reservation; integrate only the runtime
commit, not that duplicate stamp.

## Cause and evidence

Both supplied hardware banners are Build764 / `128b6b6ca`; the host uses
SteamVR/OpenXR and the visitor VirtualDesktopXR. Parent-preserved inputs are in
`.planning/validation/build765/hardware764` with `hardware-inputs.json` hashes.
No new screenshot directory was supplied in this round.

Host `Player.log` assigns both Brute and Mindthief to player1 at lines11660–11708,
with no later control reassignment. At line22126 guest2 automatically readies:
`Readied Kronosbasher. Players Ready Count: 1`. At27248 the result animation
finally makes the native toggle interactable; there is no VISIBLE transition.
The paired roster and assignment history imply one participant but two players;
these internal counts were not directly sampled by those logs.

The exact original `ShowReadyUpToggles` uses **Player** readiness, with clients
explicitly auto-readying and the host retaining a manual final vote. Native
`ReadyUpPlayer` correctly waits for **AllPlayers** and its state ACKs. However,
`UIReadyToggle.OnReadiedPlayersChanged` sets its private, visibility-only
`_allPlayersReady` from **Participants.Count**, irrespective of ready mode. The
unassigned guest's acknowledgement therefore hides the still-unvoted host's
only Continue button. The shared picture faithfully copies that absence.

The new fixture executes the exact native ShowReadyUpToggles,
OnReadiedPlayersChanged, ShouldBeVisible and UpdateVisiblity bodies. With two
players, one participant and one guest acknowledgement it reproduces the hidden
host button; removing the scoped repair fails at the expected visibility check.
The earlier fixture simplified readiness visibility, so it could not expose this
actual native roster mismatch.

The observer picture also lacked a continuation-input adapter: all cloned game
controllers/callbacks are intentionally removed, and only scrolling had metadata
input. Simply making the footer visible would still leave it inert remotely.

## Bounded implementation

- Only during an open original Results / ScenarioEnded / NotSet / Player waiter,
  clear the erroneous all-ready **presentation flag** when the actual all-player
  quorum remains outstanding. Re-evaluate the native visibility request,
  interactability and blockers. No vote, state ACK, callback or phase is invented.
- A host without a character is also awaited by Player readiness. Offer its
  original manual ready control once the original animation/reveal has made it
  interactable; no automatic vote is added.
- Keep original-control parking, layer restoration, owner retirement and native
  client auto-ready behavior. The speculative disabled-Canvas hypothesis was
  rejected after the original asset audit: the Results root has no Canvas.
- Add three explicit operation identities: ready, return to map, retry. Inert
  pictures contain transparent metadata-only hit surfaces on their exact matching
  original widget geometry. They carry the displayed opening key, retain copied
  opacity and use registered host/nested canvases while the candidate is inactive.
  Cache the surfaces per picture; no repeated full hierarchy walk is added.
- The transport lane validates native sender identity, version, host and current
  opening. The endpoint then validates live native Results, phase, original
  ready role/visibility/interactability or whitelisted option, original native
  click interception and active state. It executes the **original pointer handler**.
  Native progress confirmation, individual vote, exit/retry callback and final
  all-player ACK remain native. Clients never execute a local host callback.
- Repeated accepted ready clicks cannot unready: the native toggled-on latch is
  checked. Original return/retry closes its original option before another click.
  Refused native input stays retryable.

No modification to scroll ownership, codecs, snapshots, native gameplay queue,
actor permissions or ModBuild is part of this lane.

## Final affected verification

One Unity2021.3.5f1 / llvmpipe GL editor at a time,
`ADB_SERVER_SOCKET=tcp:localhost:5082`, `ADB_MDNS_AUTO_CONNECT=0`.

| Final containing scope | Result | Elapsed |
| --- | --- | --- |
| `results-window-runtime/run-gtnfxkcj`, complete seven-case containing scope | **58 assertions +6 causal controls PASS** |23.80s |
| `results-presentation-runtime/run-000w9f44`, production + directly affected inert-controller/atomic-preparation/public-input controls | **394 assertions +3 causal controls PASS** |33.63s |

Final owned coverage is **452 assertions +9 controls**,57.43s. Native/window tests
cover the reported spectator ACK, real final footer pixels and GraphicRaycaster,
original ExtendedToggle/ExtendedButton pointer methods and native option
Register/InvokeAction, disabled/intercepted/blocked/closing/client/no-character
permissions, explicit host-without-character continuation, duplicate input,
owner handovers, pooled reuse, hide/deactivation/destruction and no fake votes.
The presentation test imports the complete original widget graph, prepares the
whole candidate inactive, routes a real registered GraphicRaycaster hit through
ExecuteHierarchy to its own metadata proxy, checks no private client vote and
compares independent original/observer pixels including the footer. Controller
retention and incomplete-candidate mutations fail causally.

Ten unchanged presentation controls are inherited from the input-bound
Build764 checkpoint, not relabeled as newly run: native image import and3DNoise
identity, obsolete-author/early-close leases, original row lookup, immutable
capture, root pose, future opening, animation blending and atomic failure
retention. `inherited-projections.json` verifies their unchanged production
methods/files and underlying binding/material/asset/neutralizer sources against
`128b6b6ca`. Production executes these ordinary paths too; only unrelated
mutation cases were not repeated. Root owns final union validation.

### All earlier attempts retained

| Run | Actual outcome / reason |Elapsed |
| --- | --- | --- |
| window `lr1xqq34` |FAIL: a new mutation used an unconditional return and did not compile; replaced by removal of only the flag assignment. No compilation failure counts as a control. |3.89s |
| window `cjndsm2g` |FAIL: the upgraded native guest ACK legitimately hides once; old fixture expected zero Hide calls. Record native baseline after repair. |14.75s |
| window `whqbdzz1` |FAIL: the actual native all-ready callback hides its control; old fixture tried a subsequent owner handover without a new decision. Start a fresh native decision. |15.29s |
| window `wvb3w8t9` |PASS43+1, intermediate readiness-only scope. |14.81s |
| window `24_87ix9` |FAIL compilation: newly added metadata fields had no writers in this narrow fixture before the input exercise existed. Add the owned input exercise; no warning suppression. |2.40s |
| window `gwtj28i4` |Production PASS57; new mutation marker FAIL because ExecuteEvents catches callback exceptions before the intended outer assertion. Record metadata inside the callback and assert outside it. |16.93s |
| window `45171nh0` |PASS57+2, intermediate expanded input scope. |16.68s |

Including failures/intermediate attempts, measured validation elapsed is142.18s.
There was no repair to production caused by those fixture-only failures and no
repeat of an unrelated successful suite.

## Proof, limits and cleanup

Verified evidence is preserved under
`.planning/validation/build765/results-lane`: all actual run records, retained
sources/pins/manifests/logs, original-asset audit, captures, control results,
unchanged-source projections and a hash-verified archive receipt. The parent
owns the immutable original hardware logs. All runs reaped processes before
`cleanup_completed_run`; cleanup receipts include failed attempts. Own generated
Unity Library and compile bin/obj caches were removed after every run. No other
agent's worktree/cache or shared read-only references were cleaned.

The imported original-material screenshots retain the fixture's declared
bootstrap/layout/font/GL material ports and placeholder/native backing outputs.
The simple window capture visibly contains the repaired yellow footer below its
red body; full snapshot captures compare exact matching original/observer pixels
and registered input geometry. They do **not** establish full game-authored
labels, HMD appearance, Windows D3D11, native scene/bootstrap timing, or an actual
Photon round trip. Network identity/opening routing is separately tested by the
controls lane. No hardware acceptance is claimed.
