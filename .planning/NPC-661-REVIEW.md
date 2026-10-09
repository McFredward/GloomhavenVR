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

## Causal reproductions and integration review

The new geometry fixture captures the real procedural backing and printed
ability original through production publication, codec, receive, motion and
camera readback. Unchanged660 separates their world vertices by1.59154mm at the
first interpolated turn: the body rotates0.22 degrees while the print rotates
0.09 degrees. The new physical affinity uses the actual handoff's source pair
and the measured backing-to-print transform. It temporarily mounts the body on
the final rendered print, then restores its ordinary parent before the next
header/motion pass. The source matrix must be a finite factorable TRS; this does
not pretend that quaternion/lossy-scale division represents arbitrary shear.
Return, regrab, withdrawal and reoffer must supersede obsolete offered affinity.

The merchant proof instantiates the serialized native ItemCard hierarchy and
the real Leather_Armor sprite, then executes the actual ItemChip purchase
preparation and return sampler. Preparation deliberately sets its enclosing
canvas scale to zero. With unchanged660, the first returning print retains that
zero scale while the physical body moves. Moving the parent's validity guard
after canvas restoration is necessary but insufficient: the complete canvas
recipe was suppressed by the existing live-return budget. The fix freezes each
member's existing Kind1 original root/canvas recipe alongside its Kind10 subset
and stages them until all originals are ready. Record113 is not reinterpreted.
Integrator review also identified the out-of-order subset case: root admission
must use its own member's packet sequence, not the cohort's maximum sequence.
Frozen-source acknowledgements must preserve a newer terminal root.

The strengthened cold latency fixture starts before the first owner render,
native-bank freeze/partition and asset preparation. Its initial result is2.780s,
with about1.22s spent in that fixture's preparation before delivery. Further
review finds that it freezes repeated UI rows under separate addresses, whereas
actual Sync publishes them against one shared original key/address. That result
reveals previously excluded fixture work; it does not measure the actual HUD
Initialize cost or establish the hardware delay's cause. Final comparison must
bind actual template sharing and concurrent stock traffic, with the same clock
boundary for production and old-source controls. Earlier0.83s prepared
measurements retain only their declared boundary and are not cold hardware proof.

An additive record114 uses existing message28 to request the exact rejected
private compact original. It is separate from112 retained-original receipts:
it grants no interaction and acknowledges no original. The owner verifies the
compatible requester, current private session, module, exact immutable baseline
and absence of that requester's valid receipt. Bounded repairs reuse the ordinary
transport/event budget. Driver coalescing retains a same-sequence complete
repair instead of discarding it behind the rejected compact packet. Immediate
repair runs independently of the15Hz source sampling gate; a late compact-send
callback cannot postpone an already requested repair.

These are source/runtime findings. The clips do not time-align their frames with
the logs, and no merchant clip was supplied. Neither a green native runtime
fixture nor its desktop latency establishes the final headset picture.

## Paired log timing and unresolved attribution

The host records private enchantress incoming originals of37 members/29835
bytes in0.757s and15 members/26861 bytes in0.983s. The independent stock-bit63
stream ranges from0.1s to5.397s; it must not be labeled the offered private card
without source/lane identity. A repeatedly refused compact native original at
`face.333|Bottom button:0/Content:0/Layout Parent:0/Row Container:4` has structure
1B23CA78, owner basis798023A253BCF25E and observer basis820A72B8FD734195. The
logs do not identify the differing property. A transaction reports43 missing of71
required originals with42 already received,1.014s waiting and1.286s admission;
the module number alone does not prove which physical surface was absent.

Current private model geometry is now owner-authored even when its original
prefab numbers differ. The compact policy still requires exact equality for
omitted content/material/sprite fields; a genuine content mismatch receives the
complete immutable original through114. Sending geometry corrects a reproduced
native-model assumption, rather than establishing the particular logged basis
difference's sole cause.

The corrected latency fixture shares the same original row bank as actual Sync
and binds actual stock publication, exact backing construction and the genuinely
stopped native sampler. The stricter one-second cold diagnostic still fails:
one actual-stock numeric run takes1.817s, including about0.596s in real asset
Scan,0.116s in selected-original Freeze/Partition/Register and0.009s in native
basis work. Full HUD Initialize and model population remain constructor ports.
Production Initialize already prepares assets during loading/reconnect, so this
deliberately cold scan is not evidence that every physical offer incurs that
cost. It is also not a green cold-latency or hardware acceptance result.

A prepared actual-stock numeric run completes in0.994s. A genuine omitted
material refusal takes1.075s with exact-request priority, compared with1.226s
without that priority in the matched source control. Both material runs fail the
unchanged one-second diagnostic. The reverse request and complete source need
additional existing send turns; no bandwidth/event limits are increased.
Correctness, exact reconstruction, admission retry and closed-session retirement
are gated separately. All original performance assertions and failed receipts
remain available; the complete local gate must not be described as certifying
universal subsecond first-picture performance.
