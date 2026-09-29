#!/usr/bin/env python3
"""Render the enchantress's spoken and whispered families with one Eleven v3 voice.

Use `uv run --env-file /path/to/.env python3 scripts/generate-town-enchantress-576.py`.
Only the named FAL_AI_API_KEY environment variable reaches the provider helper.
Every paid request has a durable plan and a write-ahead intent; an ambiguous
submission is never retried automatically. Cue names/order remain unchanged.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import urllib.request
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / ".planning/debug/town576-speech"
ASSETS = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio"
BASE = runpy.run_path(str(ROOT / "scripts/generate-town-voices.py"))
INSPECT = runpy.run_path(str(ROOT / "scripts/generate-town-enchantress-inspect.py"))
ENDPOINT = "fal-ai/elevenlabs/tts/eleven-v3"
VOICE = BASE["VOICES"]["enchantress"]
LINES = dict(item for family in ("enchantress-greet", "enchantress-cast",
    "enchantress-enhance", "enchantress-invite") for item in BASE["ROUND570_EVENTS"][family])
LINES.update(INSPECT["LINES"])
PREVIEWS = ("enchantress-greet", "enchantress-cast-ember")
MAX_USD = .20
USD_PER_1000 = .10  # fal's listed Eleven v3 rate, checked 2026-09-27.


def payload(name: str) -> dict:
    line = LINES[name]
    if name.startswith("enchantress-cast-"):
        line = "[whispers] " + line
    return {"text": line, "voice": VOICE, "language_code": "en",
            "stability": .55, "apply_text_normalization": "auto"}


def estimate() -> float:
    return sum(len(payload(name)["text"]) for name in LINES) * USD_PER_1000 / 1000


def folder(name: str) -> Path:
    if name not in LINES:
        raise ValueError("Unknown enchantress cue: " + name)
    return WORK / name


def plan() -> None:
    if estimate() > MAX_USD:
        raise RuntimeError("Voice batch exceeds the USD 0.20 listed-price ceiling")
    for name in LINES:
        data = payload(name)
        BASE["save_json"](folder(name) / "plan.json", {
            "endpoint": ENDPOINT,
            "input_sha256": hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest(),
            "estimated_usd": round(len(data["text"]) * USD_PER_1000 / 1000, 6),
        })
    print(f"Planned {len(LINES)} one-shot calls, including {len(PREVIEWS)} audition calls; "
          f"listed-price estimate USD {estimate():.4f}.")


def submit(name: str) -> None:
    target = folder(name)
    descriptor = json.loads((target / "plan.json").read_text())
    if (target / "receipt.json").exists():
        print(name, "already submitted"); return
    if (target / "intent.json").exists():
        raise RuntimeError("Ambiguous paid request for " + name + "; reconcile provider history")
    data = payload(name)
    digest = hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()
    if descriptor["endpoint"] != ENDPOINT or descriptor["input_sha256"] != digest or estimate() > MAX_USD:
        raise RuntimeError("Paid payload differs from immutable plan")
    with (target / "intent.json").open("x") as stream:
        json.dump({"input_sha256": digest, "estimated_usd": descriptor["estimated_usd"]}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = BASE["request"]("https://queue.fal.run/" + ENDPOINT, data)
    BASE["save_json"](target / "receipt.json", receipt)
    print(name, "submitted", receipt.get("request_id", "unknown"))


def collect(name: str) -> None:
    target = folder(name)
    result_path = target / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
    else:
        receipt = json.loads((target / "receipt.json").read_text())
        status = BASE["request"](receipt["status_url"])
        BASE["save_json"](target / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(name, status.get("status")); return
        result = BASE["request"](receipt["response_url"])
        BASE["save_json"](result_path, result)
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https":
        raise ValueError("Provider audio URL must be HTTPS")
    mp3 = target / "audio.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected provider audio")
        mp3.write_bytes(data)
    probe = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                            "-of", "default=noprint_wrappers=1:nokey=1", str(mp3)],
                           check=True, capture_output=True, text=True)
    print(name, "collected", round(float(probe.stdout.strip()), 2), "seconds")


def import_assets() -> None:
    if any(not (folder(name) / "audio.mp3").exists() for name in LINES):
        raise RuntimeError("Collect and review every cue before replacing shipped assets")
    for name in LINES:
        BASE["import_audio"](name, folder(name) / "audio.mp3", replace=True,
                             target_lufs=-31 if name.startswith("enchantress-cast-") else -27,
                             highpass_hz=35)
    rhubarb = Path(os.environ.get("RHUBARB_PATH", "/tmp/town543-rhubarb/Rhubarb-Lip-Sync-1.14.0-Linux/rhubarb"))
    if not rhubarb.is_file():
        raise FileNotFoundError(rhubarb)
    names = BASE["ROUND570_EVENTS"]
    ordered = [name for family in ("enchantress-greet", "enchantress-cast", "enchantress-enhance",
        "enchantress-invite") for name, _ in names[family]] + list(INSPECT["LINES"])
    for name in ordered:
        # Preserve the already-published TLV80 cue ID verbatim. Re-enumerating a
        # local subset would make other peers play an unrelated line.
        curve_path = ASSETS / (name + ".json")
        cue = json.loads(curve_path.read_text())["cue"]
        raw = folder(name) / "rhubarb.json"
        subprocess.run([str(rhubarb), "-r", "phonetic", "-f", "json", "-q",
                        "-o", str(raw), str(ASSETS / (name + ".wav"))],
                       check=True, capture_output=True)
        result = json.loads(raw.read_text())
        intervals = result["mouthCues"]
        BASE["save_json"](curve_path, {
            "schema": 1, "cue": cue, "service": 3, "language": "en",
            "duration": float(result["metadata"]["duration"]),
            "packed": ";".join(f"{item['start']:.2f},{item['end']:.2f},{item['value']}" for item in intervals),
            "mouthCues": intervals,
        })
    print("Updated", len(ordered), "shared-voice clips and lip curves")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("plan", "submit", "collect", "import-assets"))
    parser.add_argument("name", nargs="?")
    args = parser.parse_args()
    if args.action in ("submit", "collect") and args.name not in LINES:
        parser.error("submit/collect require one known enchantress cue")
    if args.action == "plan": plan()
    elif args.action == "submit": submit(args.name)
    elif args.action == "collect": collect(args.name)
    else: import_assets()
