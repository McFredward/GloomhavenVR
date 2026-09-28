# NPC hardware follow-up — ModBuild 579

## Evidence from the ModBuild 578 run

The supplied `Player.log` identifies ModBuild 578 at Debug level. The local
`npc_probleme` evidence includes the enchantress with an extended hand but no
offer overlay (`VirtualDesktop.Android-20260928-093752.jpg`), the merchant's
resting hands visibly above the belly (`...093723.jpg`), and videos of weak
cloth contact (`...093808-0.mp4`) and the priestess's synchronized,
mechanical prayer-to-neutral arm descent (`...093842-0.mp4`). No remote log
was supplied for this test.

At both recorded native enchantress openings, the native window and palm
exist while the handoff's input gate is briefly false (`Player.log` lines
6667 and 7637); it becomes true and the cue appears shortly afterwards
(lines 6705 and 7674). Those short opening delays do not explain a prolonged
blank hand by themselves. The earlier hand proximity in the same log precedes
the first native enchantress opening, so the approach path needs separate
review.

The approach state machine records entry into the enchantress's area before
checking whether another native town destination owns the map UI. Its old
destination gate then discarded the entry while retaining the “inside” latch.
Once that other NPC closed, there was no new entry edge to retry the
enchantress window. This is a source-level match for the prolonged blank hand;
the brief native input delay is a distinct opening-state transition.

## Corrections integrated so far

- A blocked enchantress entry stays pending while the merchant or temple owns
  the native town destination. The original shop opens after that destination
  closes, without forcing the visitor to leave and re-enter. A successful
  native press clears the pending entry, preserving explicit close behavior.
- Town cloth now collides against the hand's real wrist-to-index-tip span,
  including the glove back. The contact gate measures the whole tapered
  capsule against vertices and nearby triangle interiors, rather than only
  the palm-to-fingertip segment and individual vertices. Remote hands use the
  wrist and index-tip anchors of their already synchronized hand rig, which
  preserves each glove style's geometry without a new wire record. The former
  assumed wrist offset varied from the actual anchor by several centimeters.
- The priestess no longer lowers both arms as one rigid pair when a visitor
  interrupts prayer. Each wrist, elbow pole and hand rotation follows its own
  slightly staggered phase, and the shared mid-transition elbow dip is reduced.
  The ordinary prayer-to-idle visit is now part of the 90 Hz motion regression
  path, rather than only the covered-bowl transition.

The cloth geometry control ran nine executable checks with 34 active source
negative controls; the far/parked probes visited no cloth triangles, and a
near contact visited 33 of 288 cells. A local Release compile without NuGet
restore passed with zero warnings and errors. The isolated worker also rendered the imported
red drape with the old and new contact capsules; that render is a geometry
check, not a substitute for the Unity Cloth solver or a headset view.
The portable priestess production-motion check passed 179,919 assertions and
its active mutation cases, including a newly covered mid-transition bowl path;
the changed arm silhouette remains pending a fresh Unity render and headset
inspection because the editor cannot start in this sandbox.

## Merchant contact remains open

The build-578 accepted skinned-mesh export (`/tmp/npc578-merchant-fit2`) had
zero arm/torso triangle intersections across 91 sampled frames, but the new
headset photograph and an offline render show both hands floating. Individual
nearest points at roughly 1–1.5 cm concealed a much wider 6–8 cm gap across
the visible hand area. The similarly named `/tmp/npc578-merchant-final` is an
earlier rejected export with intersections and must not be used as the build-578
baseline.

Simple inward hand translation and even a locally tapered 0.8–1.5 cm coat
bulge caused real arm/coat intersections in transition frames. Those attempts
were rejected and no merchant pose change was committed. A viable fix needs
asymmetric palm orientation, finger curl and elbow motion, followed by a fresh
90 Hz skinned-mesh contact/intersection scan and a headset-near render. The
current Unity start failure blocks that validation; a coordinate-only target
test would repeat the build-578 false positive.

## Validation boundary

The current restricted Linux sandbox does not allow Unity Editor to start its
device monitor: it fails with `cannot bind socket` before project import. An
offline rig render and source-level tests can check deterministic geometry and
state transitions but cannot establish headset appearance or interaction.
