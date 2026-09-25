# Town service hardware follow-up — ModBuild 561

The supplied local `Player.log` identifies ModBuild 560. No remote log was supplied for
this round. The eight screenshots in `.planning/debug/npc_probleme/` show the
priestess holding her arms sideways, the merchant's floating visitor hands,
visible hollow sleeve ends, incomplete parchment on the priestess's book,
an incorrectly rotated returned item card, and a dark upper-arm gap on the
enchantress. The images prove visible defects in that build; the effects of the
changes below still require a headset retest.

The donation failure is more specific than a missed bowl collision. In the log,
multiple purse releases report `bowl accepted` with tracking, pose, trigger,
eligibility, identity and context all true. Each is followed by `native
confirmation could not be submitted` and cancellation before the donation
callback. The native selection disables its originating row while opening the
confirmation. The VR path incorrectly required that row to remain interactable
*after* selection, so it cancelled the prompt and returned the purse. Pending
validation now retains the original native callback while checking that the
session, character, offer and visitor still match. A temporarily unavailable
original confirm button is retried briefly and cancelled cleanly if it never
becomes ready; the presentation code never changes gold or blessings itself.
Read-only inspection of the original `GH.Runtime.dll` confirms that the
service's `IsAvailable`, `CanAfford` and `CanBuy` checks do not depend on the
modal, row or CanvasGroup state; they still catch a real gold, character or
multiplayer-ownership change before the original callback executes.
Temple proximity is also kept separate from temporary ritual input focus so the
ordinary ability-card fan cannot rebuild over the purse during that modal step.
The ghost purse cue is visible before a purse is picked up.

The original temple book's decorative top surface is made from trimmed mesh
islands. The blank parchment overlay had discarded every grid cell with one
unsampled corner, exposing the original printed strips. Missing vertices now
take the nearest actual page height and normal, and the overlay spans both
inset leaves without filling the centre binding. No collider or duplicate
gameplay component is added.

The visitor motion lowers the priestess's hands and moves the merchant's hands
toward his belt. A fitted inner hem and dark shallow fabric cap hide the cut
forearm behind each resident's cuff. The returned original item chip settles
to its exact fan-home rotation after its normal release glide; otherwise its
remaining palm-facing angle could show a blank back on reopening.

The merchant's offering-hand blend is now advanced only by the elected NPC
author. A one-byte value joins additive activity record 81, so observers
interpolate the same authored transition instead of timing it from local
packet receipt and render rate. Presence worst case increases from 7673 to
7674 bytes, within the 7680-byte fragment bound; the local send buffer grows
one byte to retain its 257-byte largest-record margin. The same authored
activity clock already controls idle and visitor gestures. For voice, record
80 carries cue, generation and age; a follower now keeps speech age monotonic
within one utterance so jitter cannot rewind its lips while audio continues.

The previous synthetic coin chirp has been replaced by a short, low-level
physical contact sample, with one contact allowed per shared 2.4-second work
cycle. The merchant now uses one projected, hearty middle-aged speaker across
his lines, with a laugh in the greeting; the priestess uses one breathier,
rougher speaker across hers. The enchantress has a new invitation while her
hand opens. Mouth poses blend at both phoneme boundaries on the authored cue
timeline. The fixed WAV and baked lip curves live in the town bundle; neither
speech synthesis nor lip analysis runs in the game. See the audio provenance
in `unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio/PROVENANCE.md`.
The priestess's precise perceived age and the new volume need human listening.

The local ModBuild 560 log also has a `VoiceChat.BoltVoiceChatService.OnDestroy`
null reference after the OpenXR session has exited, and intermittent Hydra
DNS failures. Neither coincides with a town-service interaction or explains
the failed donation. One native town-art ambiguity warning concerns an inactive
merchant item icon; this round does not establish a visible defect from it.

The additional dark upper-shoulder facets on the enchantress are still open.
An actual-bundle near render and albedo-only render found a continuous mesh,
but the angular facets are already present in the costume surface before
lighting; 1,602 sampled animated frames showed no penetration or open seam.
Isolated Unity normal recalculation at 85 and 180 degrees barely changed the
same shaded view, so those asset edits were rejected. A local fold/light
rework is needed rather than a broad importer setting that could also alter
her face. The supplied still image alone does not establish whether this
shadow is objectionable during headset motion.

Validation for the integrated ModBuild 561: strict Release compiled with zero
warnings and errors; the full guard's 14/14 source and 77/77 local runtime
suites passed; the byte-exact wire harness passed 286,560 assertions. The
surface diff removed no config keys, Harmony patches or log markers. The
Windows town bundle rebuilt with Unity 2021.3.5f1 and passed the UnityFS
format check at 97,250,867 bytes, SHA-256
`90429b74d250cee956a4ff9cc7584428f4bb3e479e426c8df1c5ef846c8e5823`.
The guard's final compiled fingerprint exits 1 because its private baseline
is commit `080c505e`, far behind both `dev` and this NPC branch; it reports
129 changed and 210 added/removed types. This is an expected broad branch
comparison, not a source, wire, bundle or runtime test failure. These checks
do not establish headset appearance, perceived audio volume or a completed
online donation.
