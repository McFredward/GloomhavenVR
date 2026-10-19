# Build707 — immediate, non-destructive projection delivery preview

## Actual Build706 failure

The supplied Player.log identifies Build706 and records both per-eye projection
geometries followed by the exact helper-ready message. The maintainer reports
only camera passthrough, with no stripes or other test elements, and then kills
the game. No claim of independent alpha transport follows from that result.

The proven regression is in the diagnostic owner: it replaces the entire real
game source with constant zero solely on the helper's local API-ready token.
Successful PC API calls do not establish delivery to the remote headset. The
earlier software checks validated this substitution but could not establish its
required visual precondition. They therefore did not protect this failure.

The logged geometry also exposes a distinct presentation error. Cropping the
original asymmetric eye frusta to texture UV[0.4,0.6] does not put a common image
on the forward viewing axis. In the left eye, tanY=0 is outside the crop; in the
right eye it is near a transparent edge. The horizontal forward rays likewise
miss the colored interior on opposite sides. Local API success cannot establish
that such per-eye charts form a binocularly useful picture. This is a concrete
geometry defect, not proof that it alone caused the absent remote elements.

Original hardware files, hashes, exact projection messages and16 public
compositor records are retained privately in
`.planning/debug/steamlink-707-review/hardware706/`.

## Corrective scope

The explicit Cheats action starts the owned helper on the click and closes VR
Options automatically after accepting the request. The player no longer has to
close the options to initiate it. Opening options later stops the child. No
timer, automatic later blanking, second confirmation stage or restart is added.

The preview always retains the original game source. The constant-zero render
target, source substitution and associated frame latch are removed from the
runtime test. An absent or failed overlay cannot erase the game. The action is
explicitly a projection-delivery preview, not the old black-alpha witness and
not a claimed repair of the soft MR game image. Normal MR, XR dimensions, FOV,
video settings and the public Frame installation are unaffected.

The native helper now prepares one ordinary2048-square RGBA overlay on a
0.6m-wide physical plane1.2m ahead of the sampled head pose. Both eyes view that
same world plane and image. There are no separate eye charts, SBS aspect rules,
new projection matrices or stereo-transport claims. Its own immutable texture
upload is explicitly flushed before handing it off to OpenVR. Local width,
texel-aspect, texture-size and visibility readbacks are bounded setup evidence,
not remote pixel evidence. A dashboard already being open no longer forbids
setup; the overlay remains configured to be hidden inside that dashboard.

The public corner-transform getter has an upstream report of ignoring texel
aspect/bounds, so it is not promoted into an unverified aspect-correction or
startup gate. The ordinary square texture/common plane avoids that ambiguity.
The visible center is explicitly opaque, so straight gaze does not land in a
transparent pattern lane. That still is not proof of its remote appearance.

## Menu evidence from this same run

Across samples10–15, dashboard-visible toggles while foreground fade remains
zero, background fade remains opaque black, grid alpha remains zero and the
focus/last-renderer PIDs stay16000. Scene-app dimming is unavailable(error5), not
a measured default. The final foreground fade changes during subsequent
presence/session loss. These non-atomic public snapshots do not establish that
a public fade reset can repair the local Frame menu transformation. No such
setting write or menu-fix claim is introduced here.

## CI fixture repair

Build706's source/build check and three runtime shards pass; the fourth fails
because the movie-only UI fixture compiles the full Cheats composition without
a boundary for the new, unrelated projection action. Add that explicit port;
keep the movie assertions and the independent projection lifecycle suite. This
repairs fixture compilation, not a hardware rendering defect. Only the affected
movie-options subset needs rerunning; unchanged shard results are inherited.

## Integrated verification and remaining boundary

The managed subset passes65 owner/protocol/UI and44 actual source-callback
checks, plus nine causal controls. In particular, the readiness mutation that
replaces a source image is rejected, and direct-start and automatic-close
mutations fail their behavioral assertions. Local visibility metadata is logged
at Debug and cannot grant readiness. The actual Unity2021.3.5/net472 typecheck
is inherited across the documented prefix-only change; the final integrated
Release build passes with zero warnings/errors.

The native subset passes156 common-quad/pattern/ownership checks, three causal
core controls and one actual-adapter Flush-source control. Its strict Win64
resource rebuild is byte-identical:210432bytes, SHA256
`0b5d8fcda22da91f1ee43259384ce0e5e119d870ef9b4faeeb200973b1600fd0`.
The actual Mono-loaded Build707 DLL contains those exact bytes and matching
compiled verifier constants, and has no zero-image field/substitution method or
frame-latch type. The accepted scene bank, SDK, connection/quality helpers and
read-only compositor observer remain byte-identical. Independent managed/native
source reviews report no blocker.

The repaired movie-only CI fixture passes915 assertions and25 existing causal
controls. Focused documentation/options coverage checks pass. No full local
gate is repeated or claimed; unchanged wire, shaders, receiver and passing706
CI shard evidence are inherited. Evidence and exact final source pins live in
`.planning/debug/steamlink-707-review/integrated-evidence.json`.

All owned worker worktrees and regenerable test caches are removed after reaping
their processes. Hardware inputs, source/result records and current deliverables
are retained. No Frame access, write, install, restart or new hardware capture is
performed. Actual overlay visibility through Steam Link, an independent-alpha
transport route, game-image sharpness and local-menu composition remain unproved
by these software checks. The current preview may be tested without a Frame
update/restart; it must not be described as a fix for those remaining defects.
