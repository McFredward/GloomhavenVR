#!/usr/bin/env python3
"""List native narration, then explicitly extract selected clips without editing game data."""

from __future__ import annotations

import argparse
import array
import hashlib
import io
import json
import math
from pathlib import Path
import re
import struct
import sys
import wave


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


class AlignedReader:
    """Bounded reader for the shipped Unity 2021.3 I2 LanguageSourceAsset layout."""

    def __init__(self, data: bytes, position: int = 0):
        self.data = data
        self.position = position

    def uint(self) -> int:
        if self.position + 4 > len(self.data):
            raise ValueError("Truncated I2 localization integer")
        value = struct.unpack_from("<I", self.data, self.position)[0]
        self.position += 4
        return value

    def aligned_bytes(self, length: int) -> bytes:
        end = self.position + length
        if end > len(self.data):
            raise ValueError("Truncated I2 localization data")
        value = self.data[self.position:end]
        self.position = (end + 3) & ~3
        return value

    def string(self) -> str:
        return self.aligned_bytes(self.uint()).decode("utf-8")

    def strings(self) -> list[str]:
        count = self.uint()
        if count > 100:
            raise ValueError("Unexpected I2 language count")
        return [self.string() for _ in range(count)]


def english_terms(environment, wanted: set[str]) -> tuple[dict, dict]:
    # UnityPy's generated custom type tree does not decode this installed I2 asset.
    # Read only its audited native serialized layout, checking counts and language order.
    matches = []
    for obj in environment.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        header = obj.read_typetree(check_read=False)
        if header.get("m_Name") == "I2Languages":
            matches.append(obj)
    if len(matches) != 1:
        raise ValueError("Expected exactly one native I2Languages asset")
    obj = matches[0]
    raw = obj.get_raw_data()
    reader = AlignedReader(raw, 28)  # MonoBehaviour's native base fields precede m_Name.
    if reader.string() != "I2Languages":
        raise ValueError("Unexpected native localization layout")
    source_flags = [reader.uint() for _ in range(3)]
    if any(flag not in (0, 1) for flag in source_flags):
        raise ValueError("Unexpected native localization flags")
    term_count = reader.uint()
    if not 1 <= term_count <= 100000:
        raise ValueError("Unexpected native localization term count")
    terms = {}
    for _ in range(term_count):
        offset = reader.position
        term = reader.string()
        reader.uint()  # TermType.
        translations = reader.strings()
        reader.aligned_bytes(reader.uint())  # Per-language Flags.
        reader.strings()  # Serialized Languages_Touch.
        if term.casefold() in wanted:
            if term.casefold() in terms:
                raise ValueError(f"Duplicate native transcript key: {term}")
            terms[term.casefold()] = {"key": term, "record_byte_offset": offset,
                                      "translations": translations}
    reader.uint()  # CaseInsensitiveTerms.
    reader.uint()  # OnMissingTranslation.
    reader.string()  # mTerm_AppName.
    language_count = reader.uint()
    if not 1 <= language_count <= 100:
        raise ValueError("Unexpected native language array")
    languages = []
    for _ in range(language_count):
        languages.append({"name": reader.string(), "code": reader.string(),
                          "flags": reader.uint()})
    english = [i for i, language in enumerate(languages)
               if language["name"] == "English" and language["code"].startswith("en")]
    if len(english) != 1:
        raise ValueError("Native English language slot is ambiguous")
    for term in terms.values():
        translations = term.pop("translations")
        if len(translations) != language_count:
            raise ValueError("Native translation and language arrays differ")
        term["text"] = translations[english[0]].strip()
        term["status"] = "native English subtitle; not independently checked against spoken audio"
    return terms, {"path_id": obj.path_id, "name": "I2Languages", "term_count": term_count,
                   "object_sha256": hashlib.sha256(raw).hexdigest(), "languages": languages,
                   "english_index": english[0]}


def pcm_metrics(wav_bytes: bytes) -> dict:
    with wave.open(io.BytesIO(wav_bytes), "rb") as stream:
        frames, rate = stream.getnframes(), stream.getframerate()
        channels, width = stream.getnchannels(), stream.getsampwidth()
        pcm = stream.readframes(frames)
    if width != 2 or not frames or len(pcm) != frames * channels * width:
        raise ValueError("Expected a nonempty complete PCM16 WAV")
    samples = array.array("h", pcm)
    if sys.byteorder != "little":
        samples.byteswap()
    peak = max(abs(value) for value in samples)
    rms = math.sqrt(sum(value * value for value in samples) / len(samples))
    first_audible = next((i for i, value in enumerate(samples) if abs(value) > 104), None)
    last_audible = next((len(samples) - 1 - i for i, value in enumerate(reversed(samples))
                         if abs(value) > 104), None)  # Approximately -50 dBFS.
    return {"duration_seconds": frames / rate, "sample_rate_hz": rate, "channels": channels,
            "bits_per_sample": width * 8, "frame_count": frames,
            "pcm_sha256": hashlib.sha256(pcm).hexdigest(),
            "peak_dbfs": 20 * math.log10(peak / 32768) if peak else None,
            "rms_dbfs": 20 * math.log10(rms / 32768) if rms else None,
            "full_scale_sample_count": sum(value in (-32768, 32767) for value in samples),
            "leading_below_minus_50_db_seconds": first_audible / (rate * channels)
                if first_audible is not None else frames / rate,
            "trailing_below_minus_50_db_seconds": (len(samples) - 1 - last_audible) / (rate * channels)
                if last_audible is not None else frames / rate}


def guarded_output(output: Path, data_root: Path) -> Path:
    output = output.resolve()
    repo = Path(__file__).resolve().parents[2]
    protected = [data_root]
    protected.extend((repo / name).resolve() for name in ("ressources", "libs", "decompiled"))
    if data_root.parent.name == "ressources":
        protected.append(data_root.parent)
    if any(output == root or output.is_relative_to(root) for root in protected):
        raise ValueError("Output must be outside read-only game references")
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True, help="Read-only original GH_Data directory")
    parser.add_argument("--list-pattern", default=r"^(Quest|Scenario)_Campaign_", help="Regex used only in listing mode")
    parser.add_argument("--min-seconds", type=float, default=10)
    parser.add_argument("--max-seconds", type=float, default=30)
    parser.add_argument("--clip", action="append", default=[], help="Exact AudioClip name; repeat for each explicit selection")
    parser.add_argument("--output", type=Path, help="Private output directory; required for explicit extraction")
    args = parser.parse_args()
    try:
        import UnityPy

        data_root = args.data_root.resolve(strict=True)
        source = data_root / "resources.assets"
        if not source.is_file():
            raise ValueError("The original resources.assets file is missing")
        if bool(args.clip) != bool(args.output):
            raise ValueError("Extraction requires both --clip and --output")
        if len(args.clip) != len(set(args.clip)):
            raise ValueError("Duplicate explicit clip selection")
        output = guarded_output(args.output, data_root) if args.output else None
        if output and output.exists() and any(output.iterdir()):
            raise ValueError("Use an empty output directory; existing evidence is never overwritten")
        environment = UnityPy.load(str(source))
        clips = []
        for obj in environment.objects:
            if obj.type.name != "AudioClip":
                continue
            data = obj.read()
            clips.append((obj, data))
        if not args.clip:
            pattern = re.compile(args.list_pattern)
            listed = [{"name": data.m_Name, "path_id": obj.path_id, "seconds": data.m_Length,
                       "sample_rate_hz": data.m_Frequency, "channels": data.m_Channels}
                      for obj, data in clips if pattern.search(data.m_Name)
                      and args.min_seconds <= data.m_Length <= args.max_seconds]
            print(json.dumps({"audio_clip_count": len(clips), "candidate_count": len(listed),
                              "candidates": sorted(listed, key=lambda clip: clip["name"])}, indent=2))
            return 0
        selected = []
        for name in args.clip:
            matching = [(obj, data) for obj, data in clips if data.m_Name == name]
            if len(matching) != 1:
                raise ValueError(f"Expected one exact AudioClip match for {name!r}; found {len(matching)}")
            selected.append(matching[0])
        terms, localization = english_terms(environment, {name.casefold() for name in args.clip})
        sources = {"resources.assets": {"bytes": source.stat().st_size, "sha256": sha256(source)}}
        records, wav_outputs = [], []
        for obj, data in selected:
            samples = data.samples
            if len(samples) != 1:
                raise ValueError(f"Expected one decoded stream for {data.m_Name}")
            filename, wav_bytes = next(iter(samples.items()))
            if Path(filename).name != filename or not filename.endswith(".wav"):
                raise ValueError("Decoded filename must be a single WAV basename")
            metrics = pcm_metrics(wav_bytes)
            if abs(metrics["duration_seconds"] - data.m_Length) > 0.002:
                raise ValueError("Native clip duration differs from decoded audio")
            stream = data.m_Resource
            stream_source = (data_root / stream.m_Source).resolve(strict=True)
            if not stream_source.is_relative_to(data_root):
                raise ValueError("Unexpected external native audio stream")
            source_key = str(stream_source.relative_to(data_root))
            if source_key not in sources:
                sources[source_key] = {"bytes": stream_source.stat().st_size, "sha256": sha256(stream_source)}
            with stream_source.open("rb") as original:
                original.seek(stream.m_Offset)
                encoded = original.read(stream.m_Size)
            if len(encoded) != stream.m_Size:
                raise ValueError("Native encoded stream is truncated")
            records.append({"name": data.m_Name, "path_id": obj.path_id,
                            "asset_file": "resources.assets", "output_file": filename,
                            "wav_sha256": hashlib.sha256(wav_bytes).hexdigest(), **metrics,
                            "native_stream": {"source": source_key, "byte_offset": stream.m_Offset,
                                              "byte_length": stream.m_Size,
                                              "sha256": hashlib.sha256(encoded).hexdigest()},
                            "transcript": terms.get(data.m_Name.casefold()),
                            "processing": "UnityPy decode only; no trimming, normalization, denoising or synthesis"})
            wav_outputs.append((filename, wav_bytes))
        manifest = {"schema_version": 1, "data_root": str(data_root), "unitypy_version": UnityPy.__version__,
                    "source_files": sources, "localization": localization, "clips": records,
                    "voice_identity": "Original game campaign narration; reference-trailer speaker equivalence is user-confirmed, not independently audio-verified",
                    "purpose": "Private narrator cloning reference; do not distribute original extracted audio"}
        output.mkdir(parents=True, exist_ok=True)
        for filename, wav_bytes in wav_outputs:
            with (output / filename).open("xb") as destination:
                destination.write(wav_bytes)
        with (output / "manifest.json").open("x", encoding="utf-8") as destination:
            json.dump(manifest, destination, indent=2, ensure_ascii=False)
            destination.write("\n")
        print(json.dumps({"manifest": str(output / "manifest.json"), "clips": [
            {"file": record["output_file"], "seconds": record["duration_seconds"],
             "native_transcript_available": record["transcript"] is not None} for record in records]}, indent=2))
        return 0
    except (OSError, ValueError, re.error) as error:
        parser.exit(2, f"error: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
