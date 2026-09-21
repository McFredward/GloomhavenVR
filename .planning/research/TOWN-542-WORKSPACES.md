# Build 542: compact multiplayer merchant workspaces

The new permanent NPC positions made the old station-local visitor row unsafe.
Its centres were roughly 3.89–4.29 m from the map, inside actual forest trunks/rocks
and cellar walls. Full-size counters now occupy the unused southern ring rather
than extending outward behind the merchant.

The primary visitor stays at the NPC. Additional complete-native-roster ordinals
use map-frame radius 2.35 m and yaws -124°, 180°, +124°. Original card/widget sizes
and native purchasing remain intact. Reserved counter bounds fit within radius
2.915 m and outside 1.928 m; actual production station poses are checked with SAT
against all three permanent NPC envelopes. Only the owner resolves and publishes
poses, including rotation, original widget alpha and furniture material fade.
Original floor mesh sampling supplies each counter's own standing height.

Membership changes wait for held/returning cards. A 220 ms fade-out/pose-change/
fade-in includes one completely invisible frame at the new position, avoiding
visible sweeps through other stands. The original card itself is retained. Native
Selectable availability remains unchanged; only presentation CanvasGroups gate
input during relocation. Stable rosters do not restart fades.

Validation is source-bound Unity runtime geometry/material/transition testing,
plus catalog and presentation interaction checks. A headset run still needs to
confirm comfortable reach, composition and simultaneous four-player browsing.

Targeted evidence (Unity 2021.3.5f1):

- Workspace: 1,580 assertions + 8 compiled negative controls,
  `/tmp/town542-compact-workspace/run-oeh8nhbv`.
- Catalog and original controls: 409 assertions + 19 compiled negative controls,
  `/tmp/town542-compact-catalog/run-a5ihro_7`.
- Presentation/interaction: 901 assertions,
  `/tmp/town542-compact-interaction/run-9bgf69gl`; its full 31 negative controls
  passed immediately before the final first-frame input-gate addition in
  `/tmp/town542-compact-interaction/run-7fpu3gaj`.

`TownServiceGrounding` resolves each cloned counter's existing support bottom from
its own footprint on sloped terrain. Support tops and worktop height remain fixed;
these original child-transform changes use the existing mirrored presentation stream.
The first fully opaque relocation frame already blocks new grabs, closing a race
where a card could otherwise be picked up just as the fade starts.
