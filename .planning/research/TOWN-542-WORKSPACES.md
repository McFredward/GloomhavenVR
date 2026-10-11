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
visible sweeps through other stands. A new wire presentation generation at that boundary
prevents remote interpolation across the clearing even when every invisible packet is
coalesced. The native session and original card are retained. Native
Selectable availability remains unchanged; only presentation CanvasGroups gate
input during relocation. Stable rosters do not restart fades.

Validation is source-bound Unity runtime geometry/material/transition testing,
plus catalog and presentation interaction checks. A headset run still needs to
confirm comfortable reach, composition and simultaneous four-player browsing.

Targeted evidence (Unity 2021.3.5f1):

- Workspace: 1,587 assertions + 9 compiled negative controls,
  `/tmp/town542-generation-workspace/run-koxhkjvc`.
- Relocation publication: 61 production-path assertions + 4 compiled negative controls,
  `/tmp/town542-generation-mirror/run-oriul7_r`; intentionally drops every zero-alpha
  packet. See `scripts/town-service-mirror-runtime/RELOCATION.md` for transport limits.
- Catalog and original controls: 409 assertions + 19 compiled negative controls,
  `/tmp/town542-compact-catalog/run-a5ihro_7`.
- Presentation/interaction: 901 assertions and all 31 negative controls against
  integrated `bbfb53ff`, including the final first-frame input gate:
  `/tmp/town542-final-interaction/run-81gzet7z`.

`TownServiceGrounding` resolves each cloned counter's existing support bottom from
its own footprint on sloped terrain. Support tops and worktop height remain fixed;
these original child-transform changes use the existing mirrored presentation stream.
The first fully opaque relocation frame already blocks new grabs, closing a race
where a card could otherwise be picked up just as the fade starts.
