# Resident greeting assets

Original synthetic speech generated offline on 2026-09-21 through
`fal-ai/elevenlabs/tts/eleven-v3` using provider stock voices (no user voice clone).
Six requests, 280 characters, displayed-price estimate USD 0.028. Private request
receipts remain under `.planning/debug/town543-speech/`.

Mono 24 kHz PCM, normalized to -20 LUFS / -2 dB true-peak target with ffmpeg.
Rhubarb Lip Sync 1.14.0, phonetic recognizer, produces sound-derived mouth intervals.
The JSON curves are derived data; Rhubarb binaries/models are not shipped.
Runtime maps those classes to the three authored mouth blendshapes with short
coarticulation. This is automatic approximate articulation, not captured facial acting.

All files passed clipping/duration checks. Offline Whisper-small matched five
greetings; merchant English was confirmed with unbiased Whisper-medium after
small-model wares/words ambiguity. Audio input was unavailable to the coding model;
auditory/headset approval remains outstanding.

| Clip | Text | Voice ID | Duration | WAV SHA256 |
| --- | --- | --- | ---: | --- |
| merchant-en | Welcome. Take a look at my wares. | JBFqnCBsd6RMkjVDRZzb | 2.2727 | 008a8a245dda4ebf8045f7b08338fe6fca23bbfff5f34c4200437f2f6a30555e |
| merchant-de | Willkommen. Seht Euch meine Waren an. | JBFqnCBsd6RMkjVDRZzb | 2.351 | ed4f5b494c0e730b6b6e3aab69e36df88118cf65134ee633b9f067b5543600cc |
| priestess-en | Welcome. May the Great Oak watch over you. | Xb7hH8MSUJpSbSDYk0k2 | 3.3959 | 9965cb366a5bb195dba48528a41dc4527c48066e892d3a11f649b5f4c8db043f |
| priestess-de | Willkommen. Möge die Große Eiche über Euch wachen. | Xb7hH8MSUJpSbSDYk0k2 | 3.8661 | 6cc06ed7c03c172eda9b8ef3fda84855bf107fdbbb4fba6f39dc7aeabd87665f |
| enchantress-en | Welcome. Let us see what your abilities can become. | pFZP5JQG7iQjIQuC4Bku | 3.5527 | 7ca72185a553e47adda5c18b800ce69114670dc91e9321b53fab5847b16f1998 |
| enchantress-de | Willkommen. Sehen wir, was sich aus Euren Fähigkeiten machen lässt. | pFZP5JQG7iQjIQuC4Bku | 4.3625 | 7fa610af61bdec98d16af85734cc4b77a2b586d37ea705deaae84ebfc68da14a |
