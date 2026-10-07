# NPC639 local visit and map-seat repair

Base: `9870bd802` (published build638). This worker changes only the temple wrist
focus and the map rig's one-shot entry/recenter placement. It does not change NPC
attention, donation eligibility, remote purse transport, furniture, scenario rigs,
or any network identity.

## Evidence and requested behavior

Both supplied host/remote logs in `.planning/debug/npc639/inputs` report build638.
The local wrist predicate currently enters at 2.4 m and exits at 2.6 m in stable station
space. This is the source-proven reason it elects the purse too far away; the
maintainer explicitly requests a smaller area after the build 638 hardware run.

The host's initial map recenter reports **tracked Y 2.21 m**, world scale 198.12,
parchment top Y -0.02 and eyes 1.43 m above the parchment (`host-LogOutput.log:1198`).
That line does **not** prove a low initial spawn. The remote's Info report retains
yaw/seat coordinates but omits the measured head Y line. A source review shows the
map recenter retained any measured tracked Y directly: a seated/eye-origin pose
at or below 0.78 m consequently puts the eyes at or below the tabletop. This policy
predates build 638; no claim is made that build 638 changed it or that an XR-origin switch was
observed in the supplied logs. The repair prevents that valid low-origin outcome
and records the actual adjustment in the existing bounded recenter lifecycle line.

## Repair

* Wrist entry/exit become **1.45m/1.65m** in original station space. Deliberately
  placing the free palm at the bowl still selects the purse. An already held or
  deposited purse retains its original context. Wider 2.6 m NPC attention/committed
  donation presence and original native ownership/payment checks are unchanged.
  Another visitor's animated eye still cannot elect or hide this local wrist.
* Map entry/recenter guarantees at least **0.70m above the native parchment**.
  A taller standing pose keeps its original height. Only the rig gets one upward
  correction; original headset/hand local transforms and table/floor geometry are
  unchanged. Horizontal tracking offsets and yaw are still absorbed at the seat.
  Ordinary head movement, flight and world grabs are never clamped afterward.
* After a world-grab zoom, the cached seat's native top/floor difference is measured
  in the **current** rig scale. Assuming the original 0.78 m height at the changed
  scale could reintroduce a low recenter. Repeated recenter does not accumulate lift.

## Focused proof

`python3 scripts/check-npc639-purse-runtime.py --source-root <checkout> --output-dir <evidence>`
reuses the original real Unity ritual fixture. It passes **402 runtime assertions**
and **four causal controls**: restoring the broad radius, restoring shared-eye
wrist election, restoring shared-eye rendered gating, and forcing retiring ritual
cleanup to publish normal cards. The original 2.1 m valid-visit fixture was explicitly
superseded by the maintainer's smaller-radius request; its native-mode regression
now uses a valid 1.3 m visit, with 15 new actual transformed entry/hysteresis checks
at 0.7/1/198.12 scales. Native payment and shared-purse positive cases still run.
Unchanged complete 48-control ritual evidence is inherited, not rerun/claimed here.

`python3 scripts/check-npc639-visit-runtime.py --source-root <checkout> --output-dir <evidence>`
copies actual `RecenterMap`, actual `YawOnly` and complete `MapRoomSeat` into real
Unity 2021.3.5. It passes **2403 runtime assertions** and **three causal controls**
(raw low origin retained, excessive double lift, horizontal tracking offset left
unabsorbed). Cases cover tracked Y -1/0/.12/.75/1.2/1.48/1.65/2.21m, scales 1/198.12/500,
zoom 0.5/1/2 and independent multiplayer local yaw -135/0/37 degrees; native world,
city and Guildmaster parchment seat shapes are retained. It verifies coherent
head/hand transforms, table-relative clearance, repeated recenter, report geometry
and free ordinary motion. Source call-site checks retain existing pending-entry and
map-branch recenter wiring. Scene discovery/log endpoints are explicit boundaries;
it does not pretend to run a full game load or change XR runtime tracking modes.

Evidence: `.planning/debug/npc639/visit/purse/run-o0a9fhdh` and
`.planning/debug/npc639/visit/map/run-86t0m65e`. Strict Release build logs are beside
them. No headset comfort/visual acceptance or new full-suite pass is claimed.
