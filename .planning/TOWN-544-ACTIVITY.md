# Town activity runtime (build 544)

## Implementation contract

A sole resident author selects the stable shared attention target. Active visitors take
priority; other tracked players interrupt work within 2.4 canonical metres and leave at
2.9 metres. Missing flat-player/headset poses are not invented. Activity interruption is
an analytic 0.65-second transition whose starting blend is preserved on reversal. Its
integrated work clock pauses smoothly and resumes the same phase. Original Idle body
sampling continues; deterministic two-bone arm contact correction runs afterwards and
before facial motion. No gameplay controller or action is invoked.

The merchant keeps a native coin in the left grip and an authored small reed pen in the
right grip while alternating counting and writing on the original open-book mesh. The
enchantress alternates studying and a restrained spell gesture. The priestess holds a
prayer pose. Two-bone correction was chosen over additional disconnected animation clips
because the tabletop and tool contact positions must remain stable after terrain grounding.
All fingers/axes/grip searches are cached. Cosmetic failure cannot gate a native visit.

## Network

Version 3 and records 79/80 remain unchanged. Additive81 has 52 payload bytes, 54 including
its TLV header: active flag, epoch, sequence, shared clock and three analytic occupation
states (work clock, transition age, starting blend, engaged flag). Recovery presence has
worst-case 7,136 bytes, below the unchanged 7,168 assembly limit by 32 bytes. The 7,393
allocation still reserves the previous 257-byte largest-record margin.

Message22 atomically carries unchanged record80 followed by81: 156 bytes, 15 Hz, 2,340
raw bytes/second for all three residents from one authority. This replaces the new
publisher's standalone102-byte message21 (increment810 bytes/second), whose reader and
literal compatibility vector remain. Both records must validate and share epoch, sequence
and sample clock before either is committed. Established paired peers reject standalone
message21. Presence distinguishes a malformed81 from absent old-peer81 so a bad body
record cannot advance the face half through legacy fallback.

Each peer interpolates from its actually displayed pose at packet arrival, using the
same reconciliation interval for head and hands. Render time continues between packets.
Sequence wrap, retired epochs and temporary suspension use the existing face protocol
rules: fast packets cannot resurrect a suspended peer; a newer same-epoch presence can.

## Focused validation and remaining limits

- `check-town-activity.py --portable`: real production analytic phase and remote playback
  bound to managed Unity math, 24,022 assertions and three deliberately compiled defects.
  Registered for hosted CI; this mode makes no Unity scene/rig claim.
- Real Unity optional `--bundle`: imported three-NPC arm chains, continuous target contact
  and body reset/no accumulation. Latest positive run:39,026 assertions against the
  immutable build543 Linux bundle SHA256
  `be0f46bbe0241b3c2478b7872d5e2308cb701a0e57330eb34d6c08c3f1906b43`.
- Direct wire executable:260,256 assertions passed before the subsequent malformed81
  marker regression was added. Release compile:zero warnings/errors.
- Resident lifecycle:143 assertions/eight negative controls; settings, station and
  grounding:1,581/59/243 assertions and14 compiled negative controls.
- Contact images are diagnostic, not headset evidence. The fixture now CPU-skins the
  current pose into an explicit static snapshot because multiple `Camera.Render` calls
  during one Editor tick can reuse an earlier GPU skinning upload. The native open-book
  geometry has a neutral diagnostic material; its diagnostic coin is labelled non-native.
  Runtime still uses the original game props/materials.

Visual tool contact is still being calibrated. Final544 assets, actual terrain-offset
contact, and headset motion are not yet verified. The checkpoint does not claim final
pose quality, multiplayer hardware parity or measured headset performance.
