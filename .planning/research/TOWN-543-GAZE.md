# Build 543 — shared facial motion

Base: `739b66e8` on `dev`. This lane owns runtime gaze/facial binding and additive transport;
asset anatomy and the native voice adapter are separate worktrees. No paid API is used here.

## Authorship and geometry

The existing lowest fresh enabled resident authority chooses attention, preferring a current
service visitor over equally near spectators. Selection is sticky for 1.2 seconds and retains
a distance hysteresis; invalid/departed headsets are removed immediately. Failed searches
retry at 0.2 seconds, rather than repeating peer scans and physics queries at render rate. Candidates must be
within six canonical metres, in the actor's forward hemisphere and unobstructed by native
physics. No native transaction or interaction availability is changed.

The optical frame comes from the authored eye-rest rotation **after** the original body clip
sample. Station +Z is not assumed to face the player, and imported Head bone axes need not match
optical axes. The bounded delta rotates the sampled Head in that optical frame. Head yaw/pitch
are limited to 50/22 degrees; binocular eyes use 25/15. Convergence uses separate eye origins
rotated about the desired head pivot and a minimum 65 cm fixation distance. No observer chooses
its own viewer. Missing incoming poses retain a short hold, then return gently toward neutral.

`Head`, `EyeLeft`, `EyeRight` are the transform contract. Each facial LOD supplies BlinkLeft,
BlinkRight, JawOpen, MouthWide, MouthRound, Smile and BrowRaise. Optional LidUpLeft/LidDownLeft/
LidUpRight/LidDownRight follow vertical eye motion and are masked by full blink closure. The
body animations must contain no facial blendshape curves. Binding happens once; unchanged
weights are not rewritten, but changed weights reach every facial LOD. An incomplete rig emits
one normal-level warning per service for the process and never blocks the native service.

## Wire and lifecycle

Version stays 3. Existing resident record79 is byte-identical. Additive record80 contains an
active flag, nonzero process/map/ownership epoch, sequence, shared expression clock and three facial
samples. Each sample contains seven signed centidegree angles, a ushort exact voice/language
cue, uint utterance generation, voice age and three normalized mouth-weight bytes.

- Active payload: **94 bytes**, TLV **96 bytes**, independent message21 packet **102 bytes**.
- Only the elected author sends the small stream, at most 15 Hz, for all three NPCs together:
  **1530 raw bytes/second**, before ordinary transport/event overhead.
- Presence5Hz also carries a recovery snapshot; it does not run at facial rate.
- Worst complete presence: **7082 bytes**, below the unchanged **7168-byte** reassembly cap.
  Allocation **7339** retains the established 257-byte largest-record spare margin.
- Presence establishes an epoch. Fast packets with a different epoch cannot replace a newly
  connected actor's face or speech. Serial-number arithmetic handles uint wrap; old sequence
  or regressing source clocks are rejected. There are at most eight peer entries, each with four retired-epoch tombstones. Inactive/stale
  entries are evicted before they can block a newly joined participant. A temporary stale-peer
  withdrawal suspends its current epoch without retiring it: newer same-epoch presence can
  recover, fast packets cannot revive it alone, and only acceptance of another epoch retires
  the former ownership. This distinguishes packet loss from a proven process/authority change.
- Intermediate gaze poses interpolate at render rate. Blinks and subtle idle expressions are
  evaluated from the current author's shared clock (never a viewer-local ahead clock), so an entire blink cannot vanish between
  presence samples. This follows existing avatar-style network interpolation, with ordinary
  transmission latency; packet-loss intervals cannot reproduce samples never delivered.
- New authority seeds the visible pose/clock/utterance where a fresh previous-author sample is
  available. Opted-out visitors never become an unpublished facial authority. Map teardown
  resets incoming face state and calls the optional voice cleanup hook.

`TownServiceFaceSpeech` is an injected cosmetic adapter. Without a verified cue, every mouth
stays closed; the runtime never manufactures silent jaw motion. The user's later instruction
prefers original game voices; no synthetic greeting is part of this lane. The owner samples
actual mouth weights and a cue clock. Observers evaluate the same registered curve and exact
cue/language at that clock. Audio ownership and native speech attribution remain the separate
voice adapter's responsibility.

## Evidence

Final source-bound real Unity run: **2076 assertions and sixteen compiled negative controls**,
`/tmp/town543-face-runtime/run-_4_h1sw7`. Literal face golden vectors and all previous wire tests
passed **258091 assertions** (257025 at the base, +1066). Resident lifecycle passes **143
assertions and eight negative controls**, including a deliberately ahead viewer clock (500)
following an author at 20. Existing station geometry/lifecycle/grounding passes **1581/59/243
assertions and fourteen negative controls**; the three new station assertions and negative
control prove a facial failure cannot block native service interaction or retry indefinitely.
Strict Release build has zero warnings/errors. Wire coverage and EN/DE docs checks pass.

The Unity runner destroys objects left behind by intentionally failing variants so an occluding
fixture cannot poison another case's physics scene. All these tests are source-bound. Final
integrated checks and actual fitted asset validation are still required; no headset appearance
result or final-mesh beauty is claimed. The real Unity fixture requires the Editor and is a
local gate; it must not be added unchanged to hosted CI without an Editor.

## Final imported asset binding (2026-09-21)

Focused real-Unity run `/tmp/town543-final-prefabs/run-6cjzla3h` binds the unchanged production
rig/motion to the final Linux validation bundle, rather than synthetic transform fixtures.
Artifact: `/home/claw/gvr-town543-face/.planning/debug/town543-unity/final-evidence/town-review.bundle`,
**99,233,188 bytes**, SHA-256
`be0f46bbe0241b3c2478b7872d5e2308cb701a0e57330eb34d6c08c3f1906b43`.
The checker hashes it before and after execution and rejects concurrent changes.

The gate passes **9862 assertions**, **three separately compiled negative controls**, and
**nine in-memory jaw-weight corruption controls**. Every resident has three facial LODs with
all eleven runtime shape bindings (99 bindings overall), inward optical forward aligned with
station -Z, actual binocular convergence, and head reset without accumulated rotation.
The four imported body clips retain runtime eye rotations and cached facial weights at their
sampled times. The three compiled controls break the optical frame, head reset and blink
binding respectively; each fails at its expected assertion.

| Resident | Eye separation | Maximum eye-target error | Maximum skull/jaw rigidity residual |
| --- | ---: | ---: | ---: |
| Merchant | 75.956 mm | 0.02798° | 0.001223 mm |
| Priestess | 64.880 mm | 0.02798° | 0.001607 mm |
| Enchantress | 52.286 mm | 0.02798° | 0.001864 mm |

Each resident contributes 144 independent skull and 144 lower-jaw probes across its three LODs.
The masks originate in the anatomical template and survive subdivision/import through a second
UV channel; they are not selected by inspecting the final Head weights. The gate checks at least
0.999 Head weight and less than 0.5 mm deviation from rigid head motion at all combinations of
±50° yaw/±22° pitch, with both closed and 65%-open mouth expressions. Deliberately replacing a
jaw probe with 50/50 Head/Neck weights must violate that same geometry limit in every LOD.

Eye geometry totals per resident: four renderers, 2566 vertices, 4880 triangles and 144,536 bytes
reported by Unity's runtime mesh memory API in this Editor run. This metric is mesh memory only;
it is not a GPU frame-time, total-resident memory, or headset performance measurement. Actor
CPU-skinned envelopes remain finite and approximately 1.75 metres tall through sampled body
motion and facial poses.

The first diagnostic run exposed a fixture issue: Unity JsonUtility omitted nested DTO arrays
from the dynamically loaded test assembly. Explicit DataContract JSON handling now preserves
both the bundled probe metadata and resident evidence; the final asset itself was unchanged.
Static eye meshes intentionally lack CPU-readable index buffers, so triangle metrics use
GetIndexCount instead of requesting a triangles array.

This establishes binding and the sampled anatomical invariants for the stated artifact hash.
It does not certify facial beauty, continuous neck-seam appearance, eye reflections, headset
comfort, multiplayer transport on hardware, or a separately built Windows bundle's byte identity.
The parent independently reviewed final v8 rendered views; headset results remain unverified.
