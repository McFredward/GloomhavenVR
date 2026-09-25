# Town resident speech and foley

The 55 English voice cues, quiet coin contact and merchant cabinet mechanism are original mod assets, not
recordings extracted from Gloomhaven. They are generated offline; the shipped
mod makes no paid API request. Source WAVs are mono 24 kHz PCM for deterministic
Rhubarb analysis; Unity stores every imported clip as quality-0.45 mono Vorbis
so the expanded set stays within the release bundle limit. `coin-soft.wav` is
a 0.48-second single subdued coin touch with a -49.5 dB mean and -27.8 dB peak;
the resident foley player applies a further 0.065 gain and the game's master/SFX
sliders. `cabinet-cycle.wav` is a 0.84-second subdued wooden latch, card cassette
and stop performance with a -41.0 dB mean and -18.5 dB peak; its spatial source
applies a further 0.55 gain and the same master/SFX controls.

| Resident | Voice source | Cues |
| --- | --- | --- |
| Merchant | Qwen 3 TTS voice designs + fixed 0.6B cloned speaker embeddings | five each: greet, offer, buy, sell |
| Priestess | Qwen 3 TTS voice design + one fixed 0.6B cloned speaker embedding | five each: greet, prayer, donate |
| Enchantress | ElevenLabs v3 voice ID `pFZP5JQG7iQjIQuC4Bku` | five each: greet, cast, enhance, invite |

The revised merchant design asks for a close-miked, deep, clear and warm
middle-aged baritone speaking calmly to one nearby customer. It explicitly
excludes shouting, announcer projection and distant or processed sound. An
independent audio-model review heard a man in his 40s-50s, low-to-mid pitch,
exceptionally clear, warm, close and conversational, with only slight room
reverb and no shouting. The build-563 priestess design is one close-miked elderly
feminine, breathy, smoke-roughened voice shared by all fifteen of her lines. The
rejected merchant-sell-2 take uses a new close, calm merchant reference. No pitch
or formant post-process is applied.
Local tiny.en ASR recovered the intended sentence structure for every newly
generated line, with occasional expected homophones. One first merchant-buy
take stretched two short sentences to 17.98 seconds; it was rejected and
replaced once with a bounded-token take lasting 3.43 seconds. ASR and model
reviews do not replace human listening in the headset.

The final build-563 audio review heard the replacement merchant sentence in full
and described it as close, clean, natural, middle-aged and calmly conversational,
without glitches. A final priestess performance was described as close, elderly,
feminine, smoky, hoarse, gentle and devotional, with no room echo, processing or
cut words. An analysis attempt on the raw priestess design reference returned
`Invalid audio`; the repeated check used the final shipped greeting instead.

The paired JSON files are baked from these exact WAVs by Rhubarb Lip Sync 1.14.
Their validated cue, resident, duration and phoneme intervals let the elected
multiplayer face author choose one of five event variants, then broadcast that
exact cue/generation/age to all observers. The
voice playback seeks to that shared age; mouth curves use it directly. Neither
the voice API nor Rhubarb runs at game runtime.

The variant plan in `scripts/generate-town-voices.py` records immutable paid
intents and estimated per-call prices. Its 51 planned one-shot calls total about
USD 0.2021 at fal's 2026-09-25 listed rates. The one bounded replacement raises
the displayed-price estimate to about USD 0.2057. A separate coin-review request
returned HTTP 403 and was not retried; actual account billing was not independently
reconciled. Sources: [Qwen voice design](https://fal.ai/models/fal-ai/qwen-3-tts/voice-design/1.7b),
[Qwen TTS](https://fal.ai/models/fal-ai/qwen-3-tts/text-to-speech/0.6b),
[ElevenLabs v3](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3),
[ElevenLabs SFX](https://fal.ai/models/fal-ai/elevenlabs/sound-effects/v2).
Private receipts and source MP3s stay gitignored under
`.planning/debug/town562-speech/`; the API key is never written there.

The cabinet performance was generated through ElevenLabs Sound Effects v2 in
the bounded build-563 revision. The final speech pass ships two accessible voice
references, their fixed cloned embeddings, all fifteen priestess performances and
the replacement merchant-sell-2 performance. One completed priestess reference and
one completed donation result became inaccessible while the original account was
locked; both receipts were retained privately and only those outputs were regenerated
under the replacement key. Provider billing was not independently reconciled. The
complete original revision plan had a USD 0.0704 listed-price estimate. Private
intents, receipts, source MP3s and local ASR output remain gitignored under
`.planning/debug/town563-speech/` and `.planning/debug/town563-whisper/`.
