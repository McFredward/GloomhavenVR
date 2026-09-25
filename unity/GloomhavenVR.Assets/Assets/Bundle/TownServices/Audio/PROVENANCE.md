# Town resident English speech

The 11 WAVs are original mod lines, not recordings extracted from Gloomhaven.
They were generated offline through `fal-ai/elevenlabs/tts/eleven-v3` using three
stock voice IDs. The same ID is used for every line of a resident:

| Resident | Stock voice ID | Cues |
| --- | --- | --- |
| Merchant | `JBFqnCBsd6RMkjVDRZzb` | greet, offer, buy, sell |
| Priestess | `Xb7hH8MSUJpSbSDYk0k2` | greet, prayer, donate |
| Enchantress | `pFZP5JQG7iQjIQuC4Bku` | greet, two casts, enhance |

The three greetings reuse the English samples from the earlier six-call
development experiment. Eight additional lines were submitted once on
2026-09-25 (255 input characters in total). fal's listed price on that date
was USD 0.10 per 1,000 characters, giving an estimated cost of USD 0.0255 for
the eight new calls; account billing was not independently reconciled.

The WAVs are mono 24 kHz PCM, normalized offline to a -23 LUFS / -3 dBTP
target. Their paired JSON files contain Rhubarb Lip Sync 1.14 phonetic mouth
intervals and a validated compact copy for Unity 2021's runtime parser. The
mouth shapes are sound-derived approximations, not motion-captured acting.
Neither a paid API nor Rhubarb runs in the shipped mod.

Generation script: `scripts/generate-town-voices.py`. Private request receipts
and provider MP3s are gitignored in the worker's `.planning/debug/town560-speech/`.
The tiny.en local ASR check recovered all eight new English sentences, but it
is not a substitute for listening and headset loudness review.
