# Guildmaster cold MR furniture and separated map controls — build 534

## Evidence

The fresh local Debug capture identifies build 533 (assembly 1.0.5.0). Remote logs still identify
build 500 and provide no current peer verification. No fresh screenshot was supplied for this
report. The local log establishes that the native Guildmaster table already exists:

- LogOutput 6527: `GH_Map_Table`, 1.67 × 0.851 × 2.03 m, top only 4 mm below the map.
  The optional campaign slab/leg finder rejects it because this mesh includes the whole table.
- LogOutput 6580: MR explicitly disables that same native renderer as sky/background geometry.
- LogOutput 7567/7908: the native table remains resident across the environment/MR switches.

The earlier `GUILDMASTER TABLE unavailable` at 6524 described missing *campaign slab assets*,
not missing Guildmaster furniture. Loading another slab cannot correct MR hiding the native table.
The integrator owns the MR visibility correction and its focused tests.

## Changes in this worker

- Check the existing native full-table support before attempting campaign-slab asset discovery.
  Accept a temporarily disabled native renderer for this read-only geometry decision, avoiding
  both a duplicate tabletop and a misleading warning during MR/material initialization. Native
  geometry, materials, scene assets, controllers and transforms remain untouched.
- Keep Guildmaster actions in a vertical column. Put the two map-surface controls in a separate
  vertical column to the right, centred against the action column. Preserve each group's declared
  native order and provide a larger horizontal group gap.
- Fit both groups, including their outer glow/socket, to the measured support and knife clearance.
  Missing or unacceptably narrow support retains the existing accessible fallback policy with
  the same grouping and knife clearance. Campaign placement, cap sampling and native callbacks
  remain unchanged. No new per-frame scans, logging streams or network messages were added.

## Validation

`bash scripts/guildmaster-room-tests.sh`: 10,338 production layout assertions, 25 bindings and
nine negative controls (including failed-build scan cadence). New cases vary action/map counts,
scale and support geometry; they verify distinct groups, vertical order, equal group centres,
whole-cap support and fallback knife clearance. Deliberately losing the group centre or right-hand
separation fails the corresponding test. Integration build and full gates are recorded by the
primary agent. Headset appearance and cold first-entry behavior still need hardware confirmation.
