# Multiplayer hardware round — ModBuild 482 observations, 484 fixes

## Evidence and scope

Both supplied `LogOutput.log` files identify ModBuild **482**, commit **6efa4acfc**.
The implementation base is `dev` **8cc2f3a0**, ModBuild **483**. Therefore these logs
do not test the asset changes in 483. Screenshots are the three `aktive_boni*.jpg`
files in the gitignored `.planning/debug/` directory.

The seven requested areas are active-card pulse liveness, meaningful card flights,
immediate active-card grabbing after landing, actionable-only rest controls,
short-rest burn presentation on both clients, original remote bonus widgets, and
the client's disconnection. Code, comments, and developer documentation remain
English; user reports are German. Implementation workers have separate worktrees;
the integrator reviews their changes and publishes the resulting build to `dev`.

## Disconnection: what the supplied logs actually prove

The remote `Player.log` ends receipt of network actions at line **81529**. It
continues issuing side actions and rendering frames afterwards. At **82322** it
prints `[EndPoint IPv4 0.0.0.1:0] timed out`, followed by
`Disconnected from the session. Reason: Timeout`. The host records player 2
leaving at **94064**, then transfers that player's characters normally.

This is the UDP receive timeout, not the game's action-desynchronization path.
Read-only inspection of the installed `udpkit.dll` identifies the exact log site:
`UdpConnection.ProcessConnectedTimeouts` compares `RecvTime + ConnectionTimeout`
with the socket clock. The game's `NetworkManager.OnEnable` sets that timeout to
**30,000 ms**. There is no corresponding packet-unpacking exception or mod
subscriber exception in the interval.

The client also slows to roughly **200 ms per frame** during the receive gap;
heartbeats 65 and 66 each advance only 49 frames in ten seconds while head and
controllers remain tracked and the head pose continues moving. The host's nearby
30-second frame report averages **13.30 ms**, with a maximum of **30.84 ms**.
Consequently the evidence does not support a host main-thread deadlock. After receipt
stops, remote `Player.log:81683` / `LogOutput.log:14730` records XR refresh falling from
**72 to 18 Hz**. No focus-loss, standby, display/input-stop or explicit frame-throttle
message precedes the timeout in this interval. Pause/background shutdown messages follow
teardown. This corroborates presentation degradation without establishing its cause.

The increasing `Send Rate` multiplier is not proof of excessive mod traffic.
Installed `BoltConnection.SendRateMultiplier` derives it from the UDP outstanding
packet window's fill ratio: missing acknowledgements also produce this reading.
Likewise, the recurring Hydra DNS errors occur throughout both otherwise-working
sessions; their presence alone does not identify this disconnection's cause.

The logs do not identify why transport packets stopped arriving. A transport
timeout is established; a specific Wi-Fi, relay, runtime, or mod cause is not.
Increasing the timeout, suppressing disconnection, or replaying game actions would
not repair a missing connection and is not a justified fix.

## Implemented presentation changes

1. **Active pulse.** Native `CardActionHighlight.OnDisable` cancels `hoverAnim` while
   its child remains `activeSelf`. The old gate treated that stopped tween as live.
   Check tween liveness and reassert active intent only on an adopted original face.
   Remote pulse material ownership now survives temporarily hidden, still-live faces.
2. **Card flights.** For the logged LeapingCleave short rest, the owner runs the burn
   for **1.99 seconds** before releasing the real card. The watcher starts its flight
   after **0.51 seconds**, reporting an unobservable local effect. Remote burn release
   now follows the owner's semantic event with actor/recess matching, a short reorder
   allowance, and bounded loss fallback only after the owner recess empties. Explicit
   focus-flush claims prevent a later release from producing another flight. FX sequence
   comparison rejects duplicate and backward events. The audit found no reason to remove
   the observed normal model-departure flights. This identifies a concrete premature
   flight path; the user's exact end-turn sighting has no unique timestamp in the logs.
3. **Grabbing.** `FlyFromPile` disabled grabbing but the rebuilding pass skipped flying
   cards and never restored it at landing. Completion now restores inspect/grab while
   the card remains active in the same presented hand, independently of a second card.
4. **Rest controls.** Hide after actual confirmed selection, and expose controls only
   while native selection/button conditions and mod ownership allow their action.
   `IsSelectionReady` means two cards laid, so it must not substitute for confirmation.
   `IsInteractable` is a preview latch, so long rest uses native `IsSelectable` instead.
   Queued input uses the same guard. Remote controls follow the owner's visibility.
5. **Short-rest burning.** The local log proves the native timeline runs, not that it
   renders correctly. Adopted ability faces now receive the world-canvas shader footprint
   and flame draw ordering already required by the remote path. Only initialized private
   materials are changed; their original footprint/queue are restored when returned.
   Remotely, a burn front exclusively owns its recess and hides the overlapping covered
   face. Explicit record 46 keeps the short-rest choice secret independently of record 39.
6. **Original active bonuses.** The oversized census identifies exactly
   `Furniture/UseBarsDrawer/UseBar0/Caption`, **19.87 metres** tall. Remove that custom
   caption, plate and tile/frame system. Use the original serialized game slot and picker
   prefabs, or an owner-matched live source, stripping gameplay behaviours before activation.
   Owner state drives native masks, buttons, inline values, consume icons and pickers through
   records 25/45/47. Art and wording resolve against the already public replicated model.
   A missing native source is withheld and logged; there is no replica fallback. Native
   `UIUsePreview` decoration also runs on the inactive, clone-owned presentation. Slot
   alpha is transmitted; steady-state descriptor sampling/painting is change-gated.

   **Follow-up:** build 486 implements the intermediate native animation stream requested after
   this report; see [MP-ANIMATION-486.md](MP-ANIMATION-486.md). The following limit describes 484.

   **Animation verification limit:** the serialized native show-animation recipes are not
   available in this checkout. The mirror applies their native final move/scale/fade pose
   and the owner's current slot alpha. Intermediate show move/scale values are not sent.
   Therefore full opening-animation parity must not be claimed from the automated results;
   it remains a specific headset check, alongside the ordinary visual checks.

## Transport delivery discovered during integration

Read-only inspection of the installed Bolt code establishes a **1200-byte packet**.
The side-action event overhead is 213 bits (26.625 bytes) before shared packet/channel
headers; an 862-byte envelope plus a 132-byte rig event occupy about 1048 bytes. The
separate 944-byte protocol-token scratch limit is not used by this direct event path.
`EventChannel.Pack` does
not fragment oversized events: an unreliable event that fails packing twice is discarded.
The existing documented extras worst case was already 1801 bytes; complete bonus
subwidgets raise the documented snapshot bound to **3449 bytes**, buffer **3710**.
Simply increasing that buffer cannot make an oversized Bolt event deliverable.

The original v3 presence snapshot stays byte-for-byte intact. Message type **2** carries
additive TLV **48** fragments with a 64-bit sequence, total length and aligned offset.
Events are at most **864 bytes**, paced at one per **50 ms**, without catch-up bursts.
The sender finishes the current snapshot, then sends only the latest waiting snapshot.
Reception is bounded to eight peers and 4096 bytes per assembly, refuses malformed or
conflicting fragments, expires incomplete assemblies after five seconds, and applies a
complete snapshot exactly once. A newer generation supersedes an incomplete older one.
Peer departure/session teardown clear state.

A tiny legacy-readable version-only announcement preserves mismatch detection on older
builds. New builds recognize only its exact byte shape before board dispatch, so the
announcement cannot clear a same-build board. A real reassembled minimal snapshot is still
applied normally. Incompatible old builds can briefly lose their mirror while their
existing mismatch dialog appears. Flat/unmodded players still ignore the sentinel action.

`EXTRAS TRANSPORT` reports event counts, payload bytes and completed snapshots every ten
seconds. This fixes a source-proven delivery limitation and improves future evidence;
**it does not establish or fix the underlying cause of the recorded 30-second timeout**.

## Integration and validation

Changes are integrated on `dev` from isolated worker branches and reviewed by the master.
The main checkout's older `boards-texture-rework` branch and its unique README commit are
preserved. No game data, native networking implementation, assets or shipped config values
were changed. ModBuild 484 is a DLL update relative to the full 483 installation; the players
who supplied 482 logs need the **74,943,763-byte bundle** as well.

Final validation:

- All 17 guard checkers pass; wire suite **213,704 assertions** (210,164 at the baseline).
- Compiled comparison against `8cc2f3a0`: **26 changed types, 11 new types**. Changes are
  confined to the reviewed implementation and inlined ModBuild consumers (`Plugin`,
  `RemoteHandFan`, `VersionGuard`, `DesyncWatch`). Guard exit 1 denotes these expected changes,
  not a failed checker. No unrelated compiled behaviour changed.
- Patch inventory remains **107 classes / 165 methods**; config keys remain **625**.
  Log markers **4698 -> 4704**, six additions and none removed.
- Release build **0 errors / 0 warnings**. Documentation i18n passes for all eight files.
  All sixteen reference assemblies remain metadata-only. Asset bundle validation passes
  at Unity 2021.3.5f1, format 7, **74,943,763 bytes**.
- Defaults comparison against the supplied local cfg reports existing Cheats/LogLevel
  differences and three nonliteral card-grip values. This is not a request to change
  shipped defaults; none are rebased.

No headset result is claimed by these automated checks.

## Next headset verification

- Hide/re-show active cards and change character focus while watching both pulses.
- Land the first active card and immediately grab it before playing the second.
- Confirm/undo selection and check short/long-rest visibility on both boards.
- Accept a short-rest sacrifice: see the local native two-second burn, exactly one remote
  front with no back flicker, then one owner-timed flight; repeat around character changes.
- Exercise active bonuses including mandatory/optional states, consume elements and open
  option/element pickers. Compare original content, order, size and visual state on both boards.
- If a disconnect recurs, retain both complete logs and the new transport summaries. The
  underlying cause remains open; automated tests cannot promise that a relay connection never fails.
