# Town resident speech and foley

The twelve English voice cues and quiet coin contact are original mod assets, not
recordings extracted from Gloomhaven. They are generated offline; the shipped
mod makes no paid API request. All WAVs are mono 24 kHz PCM. `coin-soft.wav` is
a 0.44-second, gently faded physical coin contact at roughly -37 LUFS; the
resident foley player applies a further 0.15 gain and the game's master/SFX
sliders.

| Resident | Voice source | Cues |
| --- | --- | --- |
| Merchant | Qwen 3 TTS voice design + one fixed 0.6B cloned speaker embedding | greet, offer, buy, sell |
| Priestess | Qwen 3 TTS voice design candidate v4 + one fixed 0.6B cloned speaker embedding | greet, prayer, donate |
| Enchantress | ElevenLabs v3 voice ID `pFZP5JQG7iQjIQuC4Bku` | greet, two casts, enhance, invite |

The merchant design asked for a projected, hearty middle-aged baritone with a
natural laugh. The priestess v4 design asked for an elderly feminine, breathy,
smoke-roughened voice. Independent audio-model reviews heard a clear laugh in
the merchant greeting and more noticeable rasp/breath in priestess v4 than in
v2. Both reviews still estimated the priestess as roughly 40s-60s, so her exact
perceived age remains a headset/listening validation item. No subtle pitch or
formant pass was applied because an unverified effect could introduce artifacts
or damage consistency. Local tiny.en ASR recovered every new line, including
the enchantress hand invitation, but ASR and audio-model reviews do not replace
human listening.

The paired JSON files are baked from these exact WAVs by Rhubarb Lip Sync 1.14.
Their validated cue, resident, duration and phoneme intervals let the elected
multiplayer face author broadcast one cue/generation/age to all observers. The
voice playback seeks to that shared age; mouth curves use it directly. Neither
the voice API nor Rhubarb runs at game runtime.

The revision plan in `scripts/generate-town-voices.py` records immutable paid
intents and estimated per-call prices. Its 26 listed one-shot calls total about
USD 0.1157 at fal's 2026-09-25 listed rates; three incompatible experimental
requests returned HTTP 422 and were not retried. Actual account billing was not
independently reconciled. Sources: [Qwen voice design](https://fal.ai/models/fal-ai/qwen-3-tts/voice-design/1.7b),
[Qwen TTS](https://fal.ai/models/fal-ai/qwen-3-tts/text-to-speech/0.6b),
[ElevenLabs v3](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3),
[ElevenLabs SFX](https://fal.ai/models/fal-ai/elevenlabs/sound-effects/v2).
Private receipts and source MP3s stay gitignored under
`.planning/debug/town561-speech/`; the API key is never written there.
