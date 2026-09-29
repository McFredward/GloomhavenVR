#!/usr/bin/env python3
"""Generate ten bounded merchant stock replies in the established resident voice.

Run with ``uv run --env-file /path/to/.env python3 scripts/generate-town-merchant-stock-replies.py``.
Only the process environment supplies FAL_AI_API_KEY. Each paid request has a
durable intent and must never be retried if its outcome is ambiguous.
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
import uuid

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / ".planning/debug/town575-speech"
ASSETS = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio"
BASE = runpy.run_path(str(ROOT / "scripts/generate-town-voices.py"))
ENDPOINT = "fal-ai/minimax/speech-2.8-hd"
VOICE = "English_Deep-VoicedGentleman"
LINES = {
    "merchant-unaffordable": "A fine piece, friend, but you need more coin for this one.",
    "merchant-unaffordable-2": "I'm afraid your purse won't cover the price today.",
    "merchant-unaffordable-3": "That one costs more than you have, I'm sorry to say.",
    "merchant-unaffordable-4": "A little more coin, and it could be yours.",
    "merchant-unaffordable-5": "Keep it in mind. Come back when you have enough.",
    "merchant-sold-out": "I'm afraid that one's sold out for now.",
    "merchant-sold-out-2": "No more of those left, friend. Check back another time.",
    "merchant-sold-out-3": "The last one went earlier. I have none in stock.",
    "merchant-sold-out-4": "You've found an empty place. That item is sold out.",
    "merchant-sold-out-5": "No stock left for that one. Perhaps something else?",
}
MAX_USD = .06


def payload(name: str) -> dict:
    return {
        "prompt": LINES[name],
        "voice_setting": {"voice_id": VOICE, "speed": .94, "vol": 1.0,
                          "pitch": 0, "emotion": "neutral", "english_normalization": True},
        "audio_setting": {"sample_rate": 24000, "bitrate": 128000,
                          "format": "mp3", "channel": 1},
        "language_boost": "English", "output_format": "url",
        "normalization_setting": {"enabled": True, "target_loudness": -22,
                                  "target_range": 8, "target_peak": -3},
    }


def estimate() -> float:
    # Checked on the provider's model page on 2026-09-27: $0.10 / 1,000 chars.
    return sum(map(len, LINES.values())) * .10 / 1000


def plan() -> None:
    if estimate() > MAX_USD:
        raise RuntimeError("Stock reply batch exceeds the USD 0.06 listed-price ceiling")
    for name, line in LINES.items():
        data = payload(name)
        folder = WORK / name
        folder.mkdir(parents=True, exist_ok=True)
        BASE["save_json"](folder / "plan.json", {
            "endpoint": ENDPOINT,
            "input_sha256": hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest(),
            "estimated_usd": round(len(line) * .10 / 1000, 6),
        })
    print(f"Planned {len(LINES)} one-shot calls, listed-price estimate USD {estimate():.4f}.")


def submit(name: str) -> None:
    folder = WORK / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(name, "already submitted"); return
    if (folder / "intent.json").exists():
        raise RuntimeError("Ambiguous paid intent for " + name + "; reconcile provider history")
    data = payload(name)
    digest = hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()
    if planned["endpoint"] != ENDPOINT or planned["input_sha256"] != digest or estimate() > MAX_USD:
        raise RuntimeError("Paid request differs from its immutable plan")
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": digest, "estimated_usd": planned["estimated_usd"]}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = BASE["request"]("https://queue.fal.run/" + ENDPOINT, data)
    BASE["save_json"](folder / "receipt.json", receipt)
    print(name, "submitted", receipt.get("request_id", "unknown"))


def collect(name: str) -> None:
    folder = WORK / name
    result_path = folder / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
    else:
        receipt = json.loads((folder / "receipt.json").read_text())
        status = BASE["request"](receipt["status_url"])
        BASE["save_json"](folder / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(name, status.get("status")); return
        result = BASE["request"](receipt["response_url"])
        BASE["save_json"](result_path, result)
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https":
        raise ValueError("Provider audio URL must be HTTPS")
    mp3 = folder / "audio.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected provider audio")
        mp3.write_bytes(data)
    BASE["import_audio"](name, mp3, replace=True, target_lufs=-27)
    print(name, "collected")


def curves() -> None:
    rhubarb = Path(os.environ.get("RHUBARB_PATH", "/tmp/town543-rhubarb/Rhubarb-Lip-Sync-1.14.0-Linux/rhubarb"))
    if not rhubarb.is_file():
        raise FileNotFoundError(rhubarb)
    for cue, name in enumerate(LINES, 66):
        wav = ASSETS / (name + ".wav")
        raw = WORK / name / "rhubarb.json"
        subprocess.run([str(rhubarb), "-r", "phonetic", "-f", "json", "-q",
                        "-o", str(raw), str(wav)], check=True, capture_output=True)
        result = json.loads(raw.read_text())
        intervals = result["mouthCues"]
        BASE["save_json"](ASSETS / (name + ".json"), {
            "schema": 1, "cue": cue, "service": 1, "language": "en",
            "duration": float(result["metadata"]["duration"]),
            "packed": ";".join(f"{item['start']:.2f},{item['end']:.2f},{item['value']}" for item in intervals),
            "mouthCues": intervals,
        })
        meta = ASSETS / (name + ".json.meta")
        if not meta.exists():
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex
                            + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n"
                            + "  assetBundleName: \n  assetBundleVariant: \n")
        print(cue, name, "mouth intervals", len(intervals))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("plan", "submit", "collect", "curves"))
    parser.add_argument("name", nargs="?")
    args = parser.parse_args()
    if args.action in ("submit", "collect") and args.name not in LINES:
        parser.error("submit/collect need one known line name")
    if args.action == "plan": plan()
    elif args.action == "submit": submit(args.name)
    elif args.action == "collect": collect(args.name)
    else: curves()
