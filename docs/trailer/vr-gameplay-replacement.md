# Genuine VR gameplay replacement

The maintainer accepted the AI cinematics and requested an incremental exchange
of the remaining Flat gameplay in the official Gameplay Overview timeline.
The immutable starting point is the accepted cinematic v9 master, SHA256
`0b332c1e1860a2395154e6d99f938cc8dc1b54177cadd2a2466ed6e585b8ef9b`.
The English narration, existing GloomhavenVR name/logo patches and all eleven
accepted cinematic windows remain inherited. This pass uses no further inference.

## First selection

The inventory identifies 41 full-frame or mixed gameplay regions. Twenty chosen
inserts cover 25 regions with genuine VR images: 3,932 frames, or 65.533 seconds,
on the unchanged 14,810-frame/60fps clock. Sixteen inventory regions remain in
the previous picture pending suitable capture. These counts describe picture
coverage, not literal reproduction of every original character or mechanic.

Two user recordings and all three requested YouTube sources were inspected.
Local source A is `gameplay/2026-10-15 19-17-17.mp4` (720p30); B is
`gameplay/2026-10-15 20-06-57.mp4` (1080p30), relative to the private trailer
folder. The new gameplay inserts use only actual game recordings. Third-party gameplay
sources and actual review evidence remain in `vr-gameplay-v1/youtube-sources/`:

- [Meta Quest 3 gameplay](https://www.youtube.com/watch?v=w0mlw50PqZc)
- [Cards and tabletop gameplay](https://www.youtube.com/watch?v=ucXGnLKwOXs)
- [PCVR Gamer gameplay](https://www.youtube.com/watch?v=tgJAk6Z62b4)

The selected excerpts run at original source time. Ordinary 30fps captures
repeat at 60fps delivery; there is no motion interpolation, speed fitting,
slow motion or held ending. A dedicated nine-feature montage uses spatial
camera zoom only: 25 frames out from a card fan, 120 frames of a 3-by-3 grid,
then 92 frames into a real spell hit. It replaces the original montage block
`[11952,12189)` without claiming that its tiles are independent players.
The cooperative passage `[12522,13032)` uses one real VR viewpoint and its
native remote desktop-player cursor. It does not simulate four VR recordings.

## Matching limits and next captures

The review labels the actual replacement content. Some characters, locations
and mechanics differ while supporting the same narration. Damage choices do
not establish exhaustion; short-rest and damage-avoidance burns are distinct
from played loss actions. Trap/coin inspection does not establish a push,
pull or disarm. The Heater Shield excerpt shows a USE preview, and equipment
or merchant browsing does not establish a completed purchase.

The remaining original regions require push/pull into hazards, the Cataclysm
card-over-film composition, chest loot, enhancement, quest completion,
class/roster selection and the campaign map composition. The clean local
regional map is verified Guildmaster footage; it must not substitute for the
campaign map. The long campaign-table take has an obstructing gold symbol,
and the short unobstructed views cannot supply the whole 20-second sequence.
Additional synchronized VR viewpoints would restore the original cooperative
panel layout. A real completed item use, purchase/equip action, exhaustion and
played loss would also improve the current contextual substitutes.

The German inventory, selected-source links and concrete next capture list live
under `.planning/debug/trailer/remake-v1/vr-gameplay-v1/`. Incoming recordings
and the five requested source videos remain inputs for subsequent passes.
Unused preliminary derivatives may be removed after final review; no incoming
recording or shared game reference is eligible for that cleanup.

## Delivery evidence

The first unpublished export exposed a one-frame desktop leak at frame 4088
between adjacent inserts. The assembler compared floating timestamps with
rational window limits. It now rounds the normalized 60fps clock to its integer
frame before testing the window. A tiny real-FFmpeg regression exercises every
frame of all 20 windows, unchanged gaps and a still-graphic window: the old graph
fails exactly at 4088 and the corrected graph passes all 14,810 frames. This
fixture omits branding and uses 32-by-18 color plates; it establishes placement
and clock behavior rather than production image quality. The relevant expression
functions are documented in the [FFmpeg expression reference](https://ffmpeg.org/ffmpeg-utils.html#Expression-Evaluation).
The rejected export is not a delivery authority.

The corrected master is `full-timeline-v2/reference-remake-vr-v10.mp4`, SHA256
`213abdfaa03e7779870d4c9db3d8a88523d08ce7b8e73cdded79ce6d68ff24f3`,
relative to the private phase folder. It preserves the 246.861-second container
and original audio tail. The assembler passes all 13 technical checks.
Independent technical review decodes all 14,810 frames, checks the continuous
unofficial mark, compares all 10,878 outside-window pictures with v9 and all
3,932 changed pictures with their selected component frames. Maximum 8-bit
320-by-180 mean absolute errors are 0.7021 outside and 1.8869 inside, below the
2.0 limit; this allows ordinary export loss and does not claim identical pixels.
All 11,573 AAC packets, clocks and codec configuration match v9 exactly.

Root personally viewed 186 actual corrected-master Watch cues on 12 contact
sheets plus six full-size cues, supplementing the separate component reviews.
Manual film review is sampled; it is not an every-frame manual viewing of the
entire film. Independent Chromium review passes all 20 chapters and all changed
windows at 1x on desktop and mobile emulation, eight time-preserving comparison
switches and ten late seeks. All 156 HTTP dependencies pass; 28 input hashes
remain unchanged across playback. The browser reviewer personally inspected
56 actual screenshots. Root also viewed both overview layouts.

The private review is [VR gameplay review](http://192.168.0.80:8090/remake-v1/vr-gameplay-v1/REVIEW.html).
The bounded offline assembler regression establishes no new game runtime,
headset, phone hardware or subjective listening result.

The final visual reviewer personally inspected 358 changed/context samples,
156 Watch cues, 55 base/final pairs and four additional full-size witnesses.
The montage author identifies their own contribution; root and a separate
worker provide the independent montage component review. Final receipts are
`editor/root-final-approval.json`, `inventory/final-qc/technical.json`,
`inventory/final-qc/visual-creative-review.json` and
`youtube-sources/final-browser/run-5b7c26af/delivery-verification.json`, relative
to the phase folder.

Cleanup removed three unused preliminary MP4s, including the rejected first
export: 328,151,040 allocated file bytes. All 27 protected media authorities
were hashed before and after deletion and matched. The five incoming/requested
source videos, 20 used components, accepted base, current master, pinned files
and compact review evidence remain available. The deletion receipt is
`editor/unused-media-cleanup.json`. No runtime code or mod build changed;
the bounded offline tool regression and actual media/review-site checks are
the tested scope.

After cleanup, actual desktop/mobile navigation from the homepage reaches the
v2 master and its 20 chapters. All 19 direct homepage dependencies and the
156-item review dependency graph pass again, with six frozen authority hashes
unchanged. This bounded navigation check inherits the completed full playback
review; it does not repeat it. The receipt is
`youtube-sources/final-browser/run-ede63104/navigation-verification.json`.
