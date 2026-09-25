#!/usr/bin/env python3
"""Generate a bounded set of original English resident lines through fal.

This script reads only FAL_AI_API_KEY from its process environment. Launch with
``uv run --env-file /path/to/.env python3 scripts/generate-town-voices.py``.
It never opens, prints or persists the key. A submission intent is durable before
each paid POST; an ambiguous request is reconciled manually, never retried here.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import urllib.error
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".planning/debug/town560-speech"
ASSETS = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio"
ENDPOINT = "fal-ai/elevenlabs/tts/eleven-v3"
PRICE_PER_1000 = 0.10  # fal listing checked 2026-09-25; estimate, not receipt.
VOICES = {
    "merchant": "JBFqnCBsd6RMkjVDRZzb",
    "priestess": "Xb7hH8MSUJpSbSDYk0k2",
    "enchantress": "pFZP5JQG7iQjIQuC4Bku",
}
LINES = {
    "merchant-offer": "Let's see what you've brought.",
    "merchant-buy": "A fine choice. Shall we trade?",
    "merchant-sell": "Fair price for that.",
    "priestess-prayer": "[whispers] Great Oak, guide us through the dark.",
    "priestess-donate": "May your offering bring you strength.",
    "enchantress-cast-ember": "[whispers] Wake, hidden spark.",
    "enchantress-cast-echo": "By ember and echo, take shape.",
    "enchantress-enhance": "The power is yours to command.",
}
GREETING = {
    "merchant-greet": "merchant-en",
    "priestess-greet": "priestess-en",
    "enchantress-greet": "enchantress-en",
}
CUES = tuple(GREETING) + tuple(LINES)


def save_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, indent=2) + "\n")
    temp.replace(path)


def request(url: str, payload: dict | None = None) -> dict:
    if not (url.startswith("https://queue.fal.run/fal-ai/elevenlabs/tts/eleven-v3")
            or url.startswith("https://queue.fal.run/fal-ai/elevenlabs/requests/")):
        raise ValueError("Refusing unexpected fal endpoint")
    key = os.environ.get("FAL_AI_API_KEY")
    if not key:
        raise RuntimeError("FAL_AI_API_KEY missing from process environment")
    headers = {"Authorization": "Key " + key, "Content-Type": "application/json"}
    body = None if payload is None else json.dumps(payload).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request(url, data=body, headers=headers), timeout=90) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Provider HTTP {error.code}; never auto-retry a paid request") from None


def plan(name: str) -> dict:
    actor = name.split("-")[0]
    return {"endpoint": ENDPOINT,
            "input": {"text": LINES[name], "voice": VOICES[actor], "language_code": "en",
                      "stability": 0.5, "similarity_boost": 0.8,
                      "apply_text_normalization": "auto", "timestamps": True},
            "estimated_usd": round(len(LINES[name]) * PRICE_PER_1000 / 1000, 6)}


def prepare() -> None:
    total = 0.0
    for name in LINES:
        path = OUT / name / "plan.json"
        desired = plan(name)
        if path.exists() and json.loads(path.read_text()) != desired:
            raise RuntimeError(f"Existing plan differs: {name}")
        if not path.exists():
            save_json(path, desired)
        total += desired["estimated_usd"]
    print(f"Prepared {len(LINES)} one-shot English lines; displayed-price estimate USD {total:.4f}.")


def submit(name: str) -> None:
    folder = OUT / name
    descriptor = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(name, "already submitted")
        return
    if (folder / "intent.json").exists():
        raise RuntimeError(name + ": ambiguous existing intent; reconcile provider history")
    # Hard ceiling covers this complete batch even when the caller selects a subset.
    estimated = sum(plan(item)["estimated_usd"] for item in LINES)
    if estimated > .10:
        raise RuntimeError("Voice batch exceeds USD 0.10 displayed-price ceiling")
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / "intent.json").open("x") as stream:
        json.dump({"sha256": hashlib.sha256((folder / "plan.json").read_bytes()).hexdigest(),
                   "estimated_usd": descriptor["estimated_usd"]}, stream)
        stream.flush()
        os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + ENDPOINT, descriptor["input"])
    save_json(folder / "receipt.json", receipt)
    print(name, "submitted", receipt.get("request_id", "unknown"),
          "estimated USD", descriptor["estimated_usd"])


def import_audio(name: str, mp3: Path) -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    wav = ASSETS / (name + ".wav")
    if wav.exists():
        print(name, "asset already exists")
        return
    temp = wav.with_suffix(".wav.tmp")
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(mp3),
                    "-ac", "1", "-ar", "24000", "-af", "loudnorm=I=-23:TP=-3:LRA=9",
                    "-c:a", "pcm_s16le", "-f", "wav", str(temp)], check=True)
    probe = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                            "-of", "default=noprint_wrappers=1:nokey=1", str(temp)],
                           check=True, capture_output=True, text=True)
    seconds = float(probe.stdout.strip())
    if not .3 <= seconds <= 12:
        temp.unlink()
        raise ValueError(f"Implausible speech duration for {name}: {seconds}")
    temp.replace(wav)
    meta = wav.with_suffix(".wav.meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nAudioImporter:\n"
                        "  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n"
                        "    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n"
                        "    sampleRateOverride: 24000\n    compressionFormat: 0\n    quality: 1\n"
                        "    conversionMode: 0\n  platformSettingOverrides: {}\n  forceToMono: 1\n"
                        "  normalize: 0\n  preloadAudioData: 1\n  loadInBackground: 0\n"
                        "  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n"
                        "  assetBundleVariant: \n")
    print(name, f"imported {seconds:.2f}s")


def collect(name: str) -> None:
    folder = OUT / name
    if (ASSETS / (name + ".wav")).exists():
        print(name, "already imported")
        return
    receipt = json.loads((folder / "receipt.json").read_text())
    status = request(receipt["status_url"])
    save_json(folder / "status.json", status)
    if status.get("status") != "COMPLETED":
        print(name, status.get("status"))
        return
    result = request(receipt["response_url"])
    save_json(folder / "result.json", result)
    url = result["audio"]["url"]
    if not url.startswith("https://"):
        raise ValueError("Speech asset URL is not HTTPS")
    mp3 = folder / "speech.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected audio response")
        mp3.write_bytes(data)
    import_audio(name, mp3)


def import_greetings() -> None:
    previous = Path("/home/claw/gloomhaven_vr/.planning/debug/town543-speech")
    for name, old in GREETING.items():
        src = previous / "not-shipping-audio" / (old + ".wav")
        if not src.exists():
            raise FileNotFoundError("Approved earlier voice sample missing: " + str(src))
        dest = ASSETS / (name + ".wav")
        ASSETS.mkdir(parents=True, exist_ok=True)
        if not dest.exists():
            dest.write_bytes(src.read_bytes())
            meta = ASSETS / (name + ".wav.meta")
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nAudioImporter:\n"
                            "  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n"
                            "    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n"
                            "    sampleRateOverride: 24000\n    compressionFormat: 0\n    quality: 1\n"
                            "    conversionMode: 0\n  platformSettingOverrides: {}\n  forceToMono: 1\n"
                            "  normalize: 0\n  preloadAudioData: 1\n  loadInBackground: 0\n"
                            "  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n"
                            "  assetBundleVariant: \n")
        print(name, "imported from the earlier same-voice English sample")


def curves() -> None:
    """Bake audio-derived mouth intervals; no model or API calls at runtime."""
    rhubarb = Path(os.environ.get("RHUBARB_PATH", "/tmp/town543-rhubarb/Rhubarb-Lip-Sync-1.14.0-Linux/rhubarb"))
    if not rhubarb.is_file():
        raise FileNotFoundError("Rhubarb Lip Sync 1.14 executable not found; set RHUBARB_PATH")
    for cue, name in enumerate(CUES, 1):
        wav = ASSETS / (name + ".wav")
        if not wav.is_file():
            raise FileNotFoundError(wav)
        raw = OUT / name / "rhubarb.json"
        raw.parent.mkdir(parents=True, exist_ok=True)
        if not raw.exists():
            subprocess.run([str(rhubarb), "-r", "phonetic", "-f", "json", "-q",
                            "-o", str(raw), str(wav)], check=True, capture_output=True)
        result = json.loads(raw.read_text())
        duration = float(result["metadata"]["duration"])
        intervals = result["mouthCues"]
        service = 1 if name.startswith("merchant-") else 2 if name.startswith("priestess-") else 3
        packed = ";".join(f"{item['start']:.2f},{item['end']:.2f},{item['value']}" for item in intervals)
        data = {"schema": 1, "cue": cue, "service": service, "language": "en",
                "duration": duration, "packed": packed, "mouthCues": intervals}
        target = ASSETS / (name + ".json")
        save_json(target, data)
        meta = target.with_suffix(".json.meta")
        if not meta.exists():
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nTextScriptImporter:\n"
                            "  externalObjects: {}\n  userData: \n  assetBundleName: \n"
                            "  assetBundleVariant: \n")
        print(f"{cue:2d} {name}: {duration:.2f}s, {len(intervals)} mouth intervals")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "submit", "collect", "import-greetings", "curves"))
    parser.add_argument("names", nargs="*")
    args = parser.parse_args()
    if any(name not in LINES for name in args.names):
        parser.error("Unknown line name; choose from: " + ", ".join(LINES))
    if args.action == "prepare":
        prepare()
    elif args.action == "import-greetings":
        import_greetings()
    elif args.action == "curves":
        curves()
    else:
        for item in (args.names or list(LINES)):
            (submit if args.action == "submit" else collect)(item)
