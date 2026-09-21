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
