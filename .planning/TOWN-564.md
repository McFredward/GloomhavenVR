# Synchronized resident interaction follow-up — ModBuild 564

The supplied local `LogOutput.log` identifies ModBuild 563. The four screenshots under
`.planning/debug/npc_probleme/` show the merchant and priestess keeping their hands forward at
table height, a green opaque donation guide and purse whose orientation follows the controller,
and the enchantress workbench practical above its intended support. These are hardware facts
about build 563. The presentation described below is source- and runtime-tested; its final image,
sound and physical feel still require a headset retest.

The priestess's fifteen English cues now use MiniMax Speech 2.8 HD's age-specific
`Wise_Woman` voice. Three bounded previews compared the unmodified preset with two altered
pitch/timbre variants. The direct preset was selected so the named older voice remained natural.
A random recording of a real speaker was not cloned because consent and reuse rights could not
be established. One ambiguous `boughs` performance was replaced with the clearer authored phrase
`ancient branches`. Rhubarb regenerated every mouth curve from the final WAVs, and local tiny.en
ASR recovered every sentence. Nineteen planned one-shot calls had a displayed total estimate of
USD 0.0900. The first preview submissions failed schema validation before generation and were
resubmitted only after that outcome was explicit; private intents and receipts remain gitignored.

The temple now derives its physical affordance from the original donation availability. An
eligible ritual shows a blue translucent ghost purse, keeps the held purse world-up and facing
the player independently of controller roll, and snaps the accepted real purse to that exact bowl
seat before native continuation. A successful availability edge advances one shared revision and
plays one bounded blue/gold blessing around the permanent shrine. Late join establishes a silent
baseline and cannot replay an old blessing. If the original service says the character cannot
donate, the guide and its collider are absent and the priestess closes both hands over the bowl.
The station owns the effect, avoiding a second local copy during transaction teardown.

Merchant and priestess attentive targets are now inside the measured reach of their imported
arms. Their wrists settle beside their own hips with coordinated outward elbows instead of
clamping at full extension onto the worktop. The merchant and enchantress offering slots accept a
second valid physical card as an atomic swap: the old original returns through its canonical fan
path before the replacement occupies the same palm. Rejected replacements preserve the current
offer. The enchantress practical samples the actual workbench support rather than a nominal table
height, so its base follows the authored furniture.

Each resident now has one independent sticky interaction lease. A 120 ms acquisition window
chooses simultaneous visitors deterministically by session age and player ID; after acquisition a
later visitor cannot preempt the active one. Explicit close, walking away, cancellation,
disconnect, scene reset and the existing three-second stale timeout release it. The elected player
alone can drive colliders, native transaction input, attention, activity, offering state and voice
requests. Every observer still receives the same original station modules, pose, speech timeline,
card offer and native confirmation presentation. Losing visitors see the shared result but their
local affordances stay inert until the resident is free.

Temple eligibility and blessing revision travel in additive inner town-service record 91. The
payload contains only known/available state and a monotonic visual revision; it contains no
character, card, price or transaction identity. The pre-existing original native transaction
remains authoritative. The wire registry therefore reserves 92 as the next free ID. Shared voice
and merchant offering playback are gated by the same lease, preventing split face/body/audio
authors. Flat map mode remains unchanged.

The remaining flat service sound came from a native enhancement display show edge rather than NPC
foley. Immersive presentation now suppresses that exact `Display.audioItemShow` access only while
the original display method runs. The ordinary flat window, manual fan sounds, resident speech,
coin contact and cabinet mechanism keep their existing paths. Entry/exit fan suppression remains
one-shot rather than a per-frame audio blanket.

The supplied Debug log also shows the temple presentation rebuild taking about 239–240 ms on each
open. The synchronous workspace and ritual reconstruction is the repeated cost. This round does
not cache native slot objects across closes because their lifetime is owned by the original window
and scene; retaining them without a proven lifecycle would risk stale callbacks and deadlocks.
That measured performance item remains separate from the correctness fixes.

Focused integration validation passed 2,699 voice assertions plus twelve negative controls, 198
ritual assertions plus eighteen controls, 1,296 merchant assertions plus seventeen controls, 908
enchantment assertions plus twenty-one controls, 261 ritual-layout assertions plus thirteen
controls, and 92 decoration assertions plus thirteen controls. The full mirror production suite
and the actual-bundle activity run passed after their fixtures were updated for exclusive lease
acquisition; the latter executed 624,208 runtime assertions against the Unity 2021.3.5 town
bundle. The complete 78-suite gate then passed 286,569 assertions, including all compiled negative
controls. The fourteen source guards, removal-only config/patch/log surface comparison, EN/DE
documentation check, strict Release build (zero warnings and errors), and both UnityFS bundle
checks passed. The historical compiled-form baseline predates the NPC feature and therefore
reports its intended production surface as changed; it is not a same-feature no-op baseline.

Automated checks cannot judge the perceived age and character of the new voice, the final hand
silhouette from both headset eyes, purse readability and snap feel, the blessing's visual weight,
or contention among four real players. The next hardware test must cover eligible and unavailable
donations, purse rotation and snap, both card-swap directions at merchant and enchantress, repeated
entry without the flat sound, the grounded enchantress lamp, and two players approaching each of
the three residents at the same time.
