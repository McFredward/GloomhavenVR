# Town resident speech research — build 543

Research only, 2026-09-21; integration base `739b66e8` (build 542).
Initial research made no generation calls. A later approved six-call experiment is
recorded below; the user subsequently chose original game recordings, so generated
speech and its unfinished runtime must not ship. No game file was changed.

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


## Final user decision and original-dialog audit

The user explicitly prefers existing game recordings. Generated speech may be
reconsidered later, but is not part of this implementation. The six previously
approved experimental requests had already completed before this steering arrived:
280 characters, displayed-rate estimate USD 0.028, no retries and no further calls.
The generated asset commit is removed from the integration tree. Private receipts,
clips and the unshipped development checkpoint remain in
`.planning/debug/town543-speech/`. Audio input is unsupported by this coding model;
no auditory quality approval is claimed. Offline ASR/content, waveform checks and
phonetic analysis are experiments, not a shipping feature.

A deeper read-only audit covered all five shipped `.ruleset` ZIP archives, including
2,847 YAML members. Recursive parsing found **551 NPC dialogue nodes**:
Merchant 412, Enchantress 99, Priestess 40. Every node has text and a character;
53 also have camera placement. **None supplies an audio identifier**, and none of
those text keys matches a case-insensitive AudioClip name in the 3,157-clip inventory.
The 31 YAML members rejected by the general-purpose parser contain no matching named
NPC dialogue; a separate text scan verified that coverage boundary. The raw
Guildmaster directory independently yielded 202 nodes with the same absence.
Temporary evidence: `/tmp/town543-audio/native-dialogs-packed.json` and
`/tmp/town543-audio/native-dialogs.json`.

Concrete examples:

- Guildmaster `Scenario_Relic_DoomedCompass.yml` assigns its third introduction line
  and second completion line to `Enchantress`, but supplies no audio field.
- `ScenarioDialogueLine` defaults `narrativeAudioId` to null. Unlike custom-level
  pages, `DialogLineDTO(ScenarioDialogueLine)` does not substitute the text key when
  that field is empty. A portrait speaking in a text box does not imply a recording.
- `CTempleState` creates the devotion-level message with
  `GUI_TEMPLE_DEVOTION_LEVEL` but omits the optional `storyAudioId`. `MapChoreographer`
  correctly labels its text-box speaker `Priestess`; the audio remains null.
- `CampaignRewardsManager` creates merchant wealth/unlock text without an audio ID.

No native spoken performance for these three residents is established by this audit.
Do not synthesize a voice, speak narrator passages from their mouths, replay reward
sounds as speech, or silently lip-sync a text-only line.

A future *actual native speech* adapter could observe `UICharacterStoryBox.DecorateLine`
and `StopCurrentAudio` without changing callbacks. It must require all three facts:
(1) `DialogLineDTO.character` exactly identifies the relevant resident,
(2) `narrativeAudioId` is nonempty and validated, and
(3) `AudioController.GetPlayingAudioObjects(id)` returns the actual current instance.
Use that instance's audio time and stop state; never start a second copy, advance the
story, or gate continuation. This hook is not implemented now because there is no
verified matching recording. The generic facial speech adapter remains unbound,
allowing real jaw/lip animation later without claiming voices currently exist.


### Original-data provenance

Read-only SHA256 values for the audited installation:

| File under GH_Data | Bytes | SHA256 |
| --- | ---: | --- |
| `resources.assets` | 42169296 | `bdbb12b62374aee0a00a07f07e162c7a558c052996ea7be360a2d59bd0c8c34a` |
| `StreamingAssets/Rulebase/Campaign.ruleset` | 2802882 | `95cf4a88ead02fffecde3b7482f5774605bece0f679ae18d2a10dcb31cce717c` |
| `StreamingAssets/Rulebase/CustomScenarios.ruleset` | 48602 | `992cdf3de42a85fb54427ce7824fc87b7eb0f2767709cacc3a892d584b509b8e` |
| `StreamingAssets/Rulebase/Global.ruleset` | 541151 | `ba7266f2dbc18ccbe983c08b2b03f588e93a09426763cc1c32d2038b7732d133` |
| `StreamingAssets/Rulebase/Guildmaster.ruleset` | 884905 | `be2709b1fbd21818f8211a6c1f097d2188e4f56aa610b54e56f3d17cd8898fb6` |
| `StreamingAssets/Rulebase/Shared.ruleset` | 178507 | `73045b75b64e2123e721c777a684275a20871d9da3d40b3f29140bb944d01672` |

The exact 31 parser-rejected archive members are recorded in the private
`town543-speech/native-audit-evidence.json` alongside these hashes. The separate
character-marker scan finds zero matching NPC actors in every rejected member;
no rejected member contributes to the 551-node result. This is why the parser
failures do not weaken the named-actor coverage claim.

The unfinished synthetic runtime is archived privately as
`town543-speech/abandoned-synthetic-runtime/`. It compiled, but its unfinished
Unity fixture did not pass curve validation; it is not a validated future
implementation and must not be cherry-picked into production. All local ASR
processes completed; no model server or background transcription remains.
