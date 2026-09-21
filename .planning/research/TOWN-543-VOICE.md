# Town resident speech research — build 543

Research only, 2026-09-21; integration base `739b66e8` (build 542).
No generation API was called, no voice was cloned and no game file was changed.

## Native asset and interface evidence

Read-only UnityPy 1.25.2 enumeration of every top-level `GH_Data/*.assets`
found 3,157 AudioClip objects, all in `resources.assets`. The temporary complete
name/length/path-ID inventory is `/tmp/town543-audio/clips.json`.
Service-specific matches are effects, not identified spoken lines:

| Clip name | Path ID | Seconds |
| --- | ---: | ---: |
| SFX_UIMerchantOpen | 2963 | 1.720 |
| SFX_UIMerchantBuy | 1965 | 2.302 |
| SFX_UIMerchantSell | 2816 | 2.034 |
| SFX_UIOakTempleOpen | 2982 | 3.095 |
| SFX_UIOakTempleBless | 2647 | 4.141 |
| SFX_UIEnchantressOpen | 781 | 2.588 |
| SFX_UIEnchantressEnchant | 701 | 2.884 |

803 clips have the `Scenario_` prefix, 671 `Quest_`, and 113 `Town_`.
These include story narration, not a verified set of in-character greetings.
The Addressables catalog has no service-specific voice references. No separate
German/English service voice variants were identified. This is a bounded absence
finding, not proof that no suitable spoken fragment exists anywhere in narration.
Do not relabel unrelated narrator passages as NPC speech.

Native source evidence under the main checkout's read-only `decompiled/GH.Runtime`:

- `UIShopItemWindow.EnterShop` displays `GLOO.Introduction.UIIntroduce` help on the
  first visit. `UINewEnhancementWindow.ShowTooltip` and `UITempleWindow.ShowIntroduction`
  do likewise. These are UI tutorials, not speech actors.
- `UITempleWindow` plays the ordinary `PlaySound_UIReceivedItem` confirmation.
- `UICharacterStoryBox` plays the current `DialogLineDTO.narrativeAudioId` through
  `AudioControllerUtils.PlaySound(..., optional: true)` and stops the previous line.
- `AudioControllerUtils.AdjustStoryVolume` changes all `VONarration*` categories.
  `AudioController.GetPlayingAudioObjectsInCategory` can detect active narration;
  cache category names, and poll at bounded cadence rather than enumerate every frame.
- `AudioObject` exposes `audioTime`, `clipLength`, `category` and underlying audio
  sources; native source playback is observable without driving a dialog callback.

## Existing listener ownership: avoid historical-comment traps

`Core/Sound/GameAudio.cs` retains an old explanation that the listener stays on the
flat camera. Current `Core/HeadEar.cs` is the actual shared listener implementation:
`Claim(string)`, `Release(string)`, `Current`, and `Owned`. Environment ambience and
peer voice already share it. `WorldUI/Grab/UiSoundEar.cs` keeps ClockStone's cached
listener coherent. Do not add another listener or move the flat camera.

Preferred speech implementation: acquire a stable `TownResidents` HeadEar claim
while immersive residents/audio exist; release it on disable/teardown. Place a mod-owned
spatial AudioSource at the mouth. If that claim cannot be acquired (head absent),
skip/defer playback without gating the native shop. No listener-relative proxy is
needed when the shared ear is acquired. If deliberately avoiding ownership, a proxy
would have to map head-local mouth displacement into the active native listener's
frame every update; that is a fallback, not the existing preferred architecture.

## Available paid generation routes

Official provider documentation checked 2026-09-21. Prices are listed estimates,
not independently confirmed account billing. Parent integrator owns the decision.

**Eleven v3 on fal:** `fal-ai/elevenlabs/tts/eleven-v3`, listed **$0.10/1,000
input characters**, supports English and German. The actual endpoint input supports
`text`, `voice`, `stability`, `timestamps`, `language_code` and
`apply_text_normalization`. Proposed values: three distinct stock voice IDs,
`stability: 0.5`, `timestamps: true`, `language_code: en/de`, normalization `auto`.
Audio result is a downloadable file (MP3 in the documented example). Word timestamps
are optional; the endpoint schema does **not** guarantee phonemes or visemes.
Do not mistake other models' `seed`/`output_format` fields in the large shared API
page for supported v3 input fields. Select actual stock IDs from the provider's
voice list before submission; no real-person clone is required.
[Model and price](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3),
[endpoint schema](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3/api).

**Qwen3 Voice Design:** `fal-ai/qwen-3-tts/voice-design/1.7b`, listed
**$0.09/1,000 characters**. Required `text` and descriptive `prompt`, explicit
`language: English/German`; documented defaults `top_k: 50`, `top_p: 1`,
`temperature: 0.9`, `repetition_penalty: 1.05`, sub-talker sampling enabled,
`max_new_tokens: 200`. Output example: mono MP3, 24 kHz, duration metadata.
No timestamp/phoneme output is specified. Each design request creates speech from
a description; consistent identity across independently designed languages is not
established. The provider recommends retaining a design through its separate
clone/embedding workflow, which would need separate cost/quality review. Avoid
six independent designs if stable NPC identity is the priority.
[Model and price](https://fal.ai/models/fal-ai/qwen-3-tts/voice-design/1.7b),
[endpoint schema](https://fal.ai/models/fal-ai/qwen-3-tts/voice-design/1.7b/api).

Recommended first bounded experiment: six short greetings, one per NPC per language,
using three stock Eleven v3 voices and no retries. Under 600 total text characters
means approximately $0.06 at the displayed rate, before any account-specific billing
minimum. Listen to every file before committing. Do not accept an estimated cost as
permission for unbounded resampling or unnecessary transcription jobs.

Proposed original text (not game quotations; subject to integration review):

| Resident | English | German |
| --- | --- | --- |
| Merchant | Welcome. Take a look at my wares. | Willkommen. Seht Euch meine Waren an. |
| Priestess | Welcome. May the Great Oak watch over you. | Willkommen. Möge die Große Eiche über Euch wachen. |
| Enchantress | Welcome. Let us see what your abilities can become. | Willkommen. Sehen wir, was sich aus Euren Fähigkeiten machen lässt. |

## Proposed playback and animation contract

Author each greeting offline into the town bundle, with text/localization key,
cue ID, exact decoded duration, PCM analysis, and lip curves. Runtime must never
contact a paid API. Words alone are insufficient for realistic lip sync: build and
review phoneme-to-viseme timing offline (including bilabial closure), then use the
same immutable cue curves everywhere. A sampled amplitude envelope is only an
auxiliary jaw-open signal, not a realistic viseme replacement.

Resident authority emits monotonically identified cue, language, start time/age,
plus actor/service identity. Every observer plays the same cue once, seeks to the
current age on late arrival and never restarts on each presence update. Authority
handover must preserve current cue. Use one world voice at a time, visit-driven
cooldown and no endless ambient chatter. Own audio objects only; do not use global
StopAll or change narrative/gameplay callbacks.

Narration wins: defer greeting while native VONarration is playing, and stop/duck
only NPC-owned sound if narration begins. Respect master/story mute/volume and
cleanup on map exit, immersive opt-out and disposed residents. Do not stall native
service interaction waiting for audio, facial rig readiness or speech completion.

One shared NPC cannot have two different mouth poses for two different language
clips under the standing multiplayer 1:1 rule. Encode the authority's speech language
and exact cue so every peer hears/animates that shared performance; localized
subtitles may differ as product text. A viewer-local dub requires an explicit user
exception or a carefully approved different presentation contract.
