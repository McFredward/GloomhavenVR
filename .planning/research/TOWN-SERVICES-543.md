# Build 543: articulated town faces and shared attention

Status: implementation in progress, based on dev `739b66e8` (build 542).

## Hardware evidence

The maintainer approves the overall new NPC appearance but reports eye texture landmarks
above the corresponding mesh relief. `gesicht1.jpg` shows a pronounced extra physical
crease below the merchant's painted eyes; `gesicht2.jpg` and `gesicht3.jpg` show the same
class of anatomy/texture mismatch. The head images are presentation references, not
proof that the mesh has eyelid or mouth topology. Build 542 explicitly had neither
separate eyes nor a facial rig.

The supplied local `LogOutput.log` and `Player.log` identify build 542. No Error-level
mod entries occur in either supplied file; this does not invalidate the visible asset
defect. The remote log still identifies build 500 and is excluded from current evidence.
The build-542 CI run `35612140232` completed successfully; automated gates did not test
anatomical eye/texture alignment.

Evidence SHA-256:

- LogOutput.log: `4fbbefd52eda908b27dbca3a397f20ca67928fa1f7fb84d24819ac54bf057ac8`
- Player.log: `3a0429c2e4fae90e0f3982e23791d0e795d9a3a20488faa1aadc0d237667413a`
- gesicht1.jpg: `2795f61fb4ed7f6e5b9dadeee8b585508d12856b784f132bd4a3f1511ce3b6e2`
- gesicht2.jpg: `0aac9e41561417f1d288dc4350ee538ee38e5643e9c3d083da468861c01db874`
- gesicht3.jpg: `1de5129d7dc04950e18d14a388ff648d5fd7b6e4170cea390393c7e0f260e9e0`

## Implementation contract

- Fit actual anatomy to reference landmarks, including open eye sockets, separate eyes,
  deformable eyelids and connected lip loops with an oral interior. Preserve the approved
  costumes, body proportions, floor contact and environment lighting.
- Evaluate gaze after existing body animation. Use physiological limits, smooth attention
  changes and stable visitor preference. A shared NPC looks at one actual participant;
  observers must never independently turn the same NPC toward themselves.
- One elected cosmetic author owns gaze and expression time. Shared time drives complete
  blinks and microexpressions; initial/recovery snapshots and bounded small facial updates
  must preserve the existing transport budget. The native transaction flow is untouched.
- Speech uses an explicit cue and clock, with matching mouth motion. No idle jaw flapping,
  repeated greetings on every snapshot, or gameplay progression waiting for audio.
- Inspect neutral, profile, blink-closed and speaking views in actual Unity. A passing
  geometry/runtime test is not a claim of realistic headset facial animation.

Independent workers use current-dev worktrees for facial assets, gaze/network runtime,
and voice-source research. Root integrates and reviews; only root pushes dev.

## Maintainer clarification: original speech only for this build

The maintainer subsequently chose existing game recordings and deferred AI-generated
voices to possible later work. Six test greetings had already been generated before
that answer arrived (listed estimate USD 0.028); this was disclosed immediately.
Their asset commit and bundle-loader addition were reverted. The test audio and
receipts remain private under `.planning/debug/town543-speech/`, outside the shipping
asset tree. No generated greeting, automatic synthetic playback or paid retry belongs
in this build.

The game contains narration, but no service greeting has yet been verified for these
three NPCs. Further research must establish a genuine native speaker/clip association;
playing unrelated narrator recordings from an NPC would not satisfy this preference.
Eye/head motion, expressions and a physically speakable mouth rig remain in scope.
Without a verified applicable speech source, the resting mouth stays closed and the
speech adapter remains inactive. Do not claim audible NPC speech is implemented merely
because the rig can articulate.
