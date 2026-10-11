#!/usr/bin/env python3
"""Generate five bounded post-offer enchantress lines with the approved voice.

Run with ``uv run --env-file /path/to/.env python3 scripts/generate-town-enchantress-inspect.py``.
Only the process environment supplies FAL_AI_API_KEY. An ambiguous paid request
leaves an intent file and must be reconciled, never submitted a second time.
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
WORK = ROOT / ".planning/debug/town574-speech"
ASSETS = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio"
BASE = runpy.run_path(str(ROOT / "scripts/generate-town-voices.py"))
ENDPOINT = "fal-ai/minimax/speech-2.8-hd"
LINES = {
    "enchantress-inspect": "Now the runes are clear. Choose the part you wish to change.",
    "enchantress-inspect-2": "The card has a path for magic. Show me where it should take hold.",
    "enchantress-inspect-3": "Its markings have opened to me. Point to the part you want strengthened.",
    "enchantress-inspect-4": "Ah, there it is. Which ability shall I enchant?",
    "enchantress-inspect-5": "I have the card. Choose where the new rune should go.",
}
MAX_USD = .04


def payload(name: str) -> dict:
    return {
        "prompt": LINES[name],
        "voice_setting": {"voice_id": "English_Whispering_girl", "speed": .94,
                          "vol": 1.0, "pitch": 0, "emotion": "neutral",
                          "english_normalization": True},
        "audio_setting": {"sample_rate": 24000, "bitrate": 128000,
                          "format": "mp3", "channel": 1},
        "language_boost": "English", "output_format": "url",
        "normalization_setting": {"enabled": True, "target_loudness": -22,
                                  "target_range": 8, "target_peak": -3},
    }


def estimate() -> float:
    # fal's 2026-09-27 listed price is USD 0.10 / 1,000 characters.
    return sum(len(line) for line in LINES.values()) * .10 / 1000


def plan() -> None:
    if estimate() > MAX_USD:
        raise RuntimeError("Inspection lines exceed the USD 0.04 listed-price ceiling")
    for name, line in LINES.items():
        data = payload(name)
        folder = WORK / name
        folder.mkdir(parents=True, exist_ok=True)
        BASE["save_json"](folder / "plan.json", {
            "endpoint": ENDPOINT,
            "input_sha256": hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest(),
            "estimated_usd": round(len(line) * .10 / 1000, 6),
        })
    print(f"Planned five one-shot calls, listed-price estimate USD {estimate():.4f}.")


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
    BASE["import_audio"](name, mp3, replace=True, target_lufs=-27, highpass_hz=35)
    print(name, "collected")


def curves() -> None:
    rhubarb = Path(os.environ.get("RHUBARB_PATH", "/tmp/town543-rhubarb/Rhubarb-Lip-Sync-1.14.0-Linux/rhubarb"))
    if not rhubarb.is_file():
        raise FileNotFoundError(rhubarb)
    for cue, name in enumerate(LINES, 61):
        wav = ASSETS / (name + ".wav")
        raw = WORK / name / "rhubarb.json"
        subprocess.run([str(rhubarb), "-r", "phonetic", "-f", "json", "-q",
                        "-o", str(raw), str(wav)], check=True, capture_output=True)
        result = json.loads(raw.read_text())
        intervals = result["mouthCues"]
        BASE["save_json"](ASSETS / (name + ".json"), {
            "schema": 1, "cue": cue, "service": 3, "language": "en",
            "duration": float(result["metadata"]["duration"]),
            "packed": ";".join(f"{item['start']:.2f},{item['end']:.2f},{item['value']}" for item in intervals),
            "mouthCues": intervals,
        })
        meta = ASSETS / (name + ".json.meta")
        if not meta.exists():
            meta.write_text("fileFormatVersion: 2\nguid: " + __import__("uuid").uuid4().hex
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
