# NPC 661: remaining Build 660 hardware defects

## Evidence boundary

The maintainer reports that Build 660 is substantially better, with four remaining
defects: enchantress body/front separation during hover, separation while the card
owner turns, up to three seconds before the complete remote card/UI, and a gray
merchant purchased-item return. The previously confirmed stable enhancement
overlay remains a required regression boundary.

Both players' Player.log and LogOutput.log identify ModBuild 660, source
12df1353f on dev, assembly 1.1.0.0, built 2026-10-09 21:01:35 UTC. The inputs were
copied before analysis to the main checkout's gitignored
`.planning/debug/npc661/inputs/`; its manifest records sizes and SHA-256 hashes.
The host has Debug town-motion traces; the other player's normal log level does
not provide the same detailed clock/capture stream.

Both new recordings are from the enchantress. There is no merchant purchase
recording in this evidence set. The merchant defect is the user's hardware
observation and must be reproduced through the actual item purchase lifecycle.

- `VirtualDesktop.Android-20261009-232038-0.mp4`: 8.266633 s, 1280x720, 30 fps.
  All five extracted keyframes and all 17 uniformly sampled frames were inspected.
  The card is already offered at the beginning. Rotation exposes the back and
  front simultaneously as displaced surfaces around three seconds; a broad
  plain physical surface overlaps only part of the printed front around 6.5 s.
  The recording establishes visible separation, not its exact transform error.
- `VirtualDesktop.Android-20261009-232128-0.mp4`: 8.811089 s, 1280x720, 30 fps.
  All five keyframes and all 18 uniformly sampled frames were inspected. The
  selected card approaches the offered slot around 1.5 s. The offered card/ring
  and option panel appear around 3.5 s, then the visible rows change/fill.
  Those subsequent changes require comparison with the owner-native state:
  original UI transitions or scrolling must not be counted as transfer delay.
  System overlays obscure the beginning/end. File names alone do not establish
  exact synchronization with game clocks.

## Why the previous proof did not finish this task

Build 660 made ability bodies from exact owner factory dimensions and retained
original silhouette updates. That proves construction and identity, but it does
not prove coherent motion of separately published body and front modules. The
offered-root interpolation path recognizes fronts and native holders; the body
is a separate source/module. Identical initial geometry cannot guarantee matching
per-render transforms when independent samples and clocks subsequently move it.

Build 660's prepared-native latency tests measure their declared preparation
boundary. They do not establish the time from actual first owner-visible content
through cold native admission, publication, transport, construction and final
observer rendering. The new test must measure that entire lifecycle and retain
the original owner's intermediate states.

Build 660's return-completion tests cover real native samplers, root rect
canonicalization and hand attachment, but explicitly use simplified print model
ports. A merchant purchase can change the original ItemCardUI construction and
ownership. Correct flight endpoints cannot prove that the purchased item's real
front remains drawable throughout that handoff.

## Work ownership

Independent geometry, merchant purchase rendering and first-picture delivery
workers start from current dev 12df1353f in separate initialized worktrees. The
integrator owns shared-file coordination, test registration, build metadata,
review, complete local checks and direct dev delivery. Source-proven changes,
runtime regression evidence and unverified headset outcomes remain distinct.
