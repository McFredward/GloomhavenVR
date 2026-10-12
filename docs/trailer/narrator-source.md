# Private original narrator references

The trailer brief authorizes an English narrator clone. The maintainer explicitly
confirmed the speaker's consent and states that the launch-trailer narrator is
the same speaker as the installed game's narration. That voice identification is
user-provided; this extraction does not independently prove actor identity.
No paid inference or voice-cloning request was made by this extraction task.

The installed game exposes 3,157 `AudioClip` objects in `GH_Data/resources.assets`.
The native `Quest_` and `Scenario_` recordings are separate streams, allowing a
voice reference to be decoded without extracting the soundtrack from a mixed
trailer. `resources.resource` holds the encoded media; no Wwise bank is needed.
The campaign narration samples selected for private review are:

| Native AudioClip name | Path ID | Decoded duration |
| --- | ---: | ---: |
| `Quest_Campaign_001_INTRO_TRAVEL_1` | 819 | 12.107458 s |
| `Quest_Campaign_001_INTRO_TRAVEL_2` | 2238 | 12.494958 s |
| `Quest_Campaign_004_INTRO` | 767 | 27.510812 s |

Each is mono PCM16 at 48 kHz. These are original recordings for a private cloning
reference, not new trailer narration or redistributable mod assets. No trimming,
normalization, denoising, mixing or synthesis is applied. An isolated asset and
waveform metrics establish extraction integrity; they do not establish auditory
quality or similarity to the reference trailer.

## Reproduce selectively

Use the existing UnityPy environment or install UnityPy in a dedicated environment.
The script requires Python 3.10+ and uses the game's native Unity 2021.3 I2 asset
layout. Its first mode only lists names, path IDs, lengths and channel counts;
listing never creates output. Supply an original, read-only `GH_Data` root:

```bash
/home/claw/unitypy-venv/bin/python scripts/trailer/extract_narrator.py \
  --data-root /home/claw/gloomhaven_vr/ressources/GH_Data \
  --list-pattern '^Quest_Campaign_001_INTRO_TRAVEL_[12]$'
```

After reviewing the listed names, request exact clips explicitly. Use a fresh
private output directory outside the game references; existing evidence is never
overwritten. Do not point this command at a tracked asset destination:

```bash
/home/claw/unitypy-venv/bin/python scripts/trailer/extract_narrator.py \
  --data-root /home/claw/gloomhaven_vr/ressources/GH_Data \
  --clip Quest_Campaign_001_INTRO_TRAVEL_1 \
  --clip Quest_Campaign_001_INTRO_TRAVEL_2 \
  --clip Quest_Campaign_004_INTRO \
  --output .planning/trailer/voice/new-reference-set
```

The output is restricted to selected WAV files and `manifest.json`. The manifest
records source-file sizes and SHA256 values; AudioClip path IDs; exact stream
offsets, byte lengths and encoded hashes; decoded WAV and PCM hashes; durations,
channel counts, peaks, RMS levels and full-scale sample counts.

## Native transcript evidence

`resources.assets` also contains `I2Languages`, a `LanguageSourceAsset` at path ID
10357. Its five serialized language slots are English (`en-GB`), French (`fr`),
German (`de`), Spanish (`es`) and Polish (`pl`). The script reads its bounded native
serialization, validates the language array, and resolves the exact matching clip
key to its English text. The private manifest retains that subtitle text, its
record byte offset and the localization-object hash.

This is an exact native subtitle, not an independently checked speech transcript:
recorded performance may differ from displayed text. Review the source audio
before using that text as a model's exact reference transcript. Do not copy its
dialogue into the original trailer script.

The extractor refuses output within `GH_Data`, `ressources`, `libs` or
`decompiled`, including resolved symlink destinations. All game inputs are opened
for reading only. Extraction artifacts belong under gitignored `.planning/` and
must not be staged, uploaded as public assets, or bundled with the mod.
