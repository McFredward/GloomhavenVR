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
from urllib.parse import urlparse
import uuid


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".planning/debug/town560-speech"
REVISION_OUT = ROOT / ".planning/debug/town561-speech"
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
    "priestess-prayer": "Great Oak, guide us through the dark.",
    "priestess-donate": "May your offering bring you strength.",
    "enchantress-cast-ember": "[whispers] Wake, hidden spark.",
    "enchantress-cast-echo": "By ember and echo, take shape.",
    "enchantress-enhance": "The power is yours to command.",
    "enchantress-invite": "Bring me your card. Let us see what magic it can bear.",
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
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.netloc != "queue.fal.run" or not parsed.path.startswith(("/fal-ai/", "/openrouter/")):
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


def import_audio(name: str, mp3: Path, *, replace: bool = False, target_lufs: int = -23) -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    wav = ASSETS / (name + ".wav")
    if wav.exists() and not replace:
        print(name, "asset already exists")
        return
    temp = wav.with_suffix(".wav.tmp")
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(mp3),
                    "-ac", "1", "-ar", "24000", "-af", f"loudnorm=I={target_lufs}:TP=-6:LRA=9",
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
        raw = REVISION_OUT / name / "rhubarb.json"
        raw.parent.mkdir(parents=True, exist_ok=True)
        wav_hash = hashlib.sha256(wav.read_bytes()).hexdigest()
        hash_path = raw.with_suffix(".wav.sha256")
        if not raw.exists() or not hash_path.exists() or hash_path.read_text() != wav_hash:
            subprocess.run([str(rhubarb), "-r", "phonetic", "-f", "json", "-q",
                            "-o", str(raw), str(wav)], check=True, capture_output=True)
            hash_path.write_text(wav_hash)
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


# Build-561 voice revision. Two inexpensive voice-designed greetings become the
# exact references for fixed Qwen speaker embeddings. All later merchant and
# priestess lines reuse those embeddings, rather than designing each utterance
# independently. The enchantress keeps her existing Eleven v3 voice ID.
REVISION_GREETINGS = {
    "merchant-greet": "Ah, a customer! Come closer, friend. Ha!",
    "priestess-greet": "Welcome, traveler. The Great Oak watches over you.",
}
REVISION_DESCRIPTIONS = {
    "merchant": "A hearty, jovial middle-aged English-speaking male merchant. Full, chesty, resonant baritone with a little roughness; strong projected speech and an easy natural laugh. Friendly and animated, never announcer-like.",
    "priestess": "An elderly English-speaking woman in her seventies. Low smoky contralto, distinctly raspy and breathy with an aged rough throat. Gentle, prayerful, deliberate and clear, never a theatrical witch caricature.",
    "priestess-v2": "Clearly and unmistakably an elderly FEMALE grandmother speaking English, approximately eighty years old. A woman's medium pitch and fragile breathy timbre, with a weathered dry rasp and occasional age-worn vocal crack. Tender, humble, prayerful, intimate conversational delivery. Never masculine, baritone, commanding, epic or theatrical.",
    "priestess-v3": "An unmistakably elderly English-speaking WOMAN in her late eighties: a weathered, husky, smoke-roughened grandmother's voice with audible age-worn creak and grain. Medium feminine pitch, frail breath, intimate and prayerful rather than deep or declamatory. Natural human speech, not a young actress, male priest, witch, narrator or monster.",
    "priestess-v4": "A real elderly woman, around eighty-five, speaking close to the listener. Her voice is recognizably feminine, low and smoky, with a persistent hoarse scratch from a worn throat, breath between phrases, and subtle age-related vocal tremor. She sounds kind but frail, quiet and devotional. Avoid clean young-sounding resonance, theatrical projection, male pitch, monster effects and exaggerated witch acting.",
}
REVISION_LINES = {name: LINES[name] for name in (
    "merchant-offer", "merchant-buy", "merchant-sell", "priestess-prayer", "priestess-donate",
    "enchantress-invite")}
REVISION_SFX = ("A single small handful of old metal coins gently set down on a wooden shop counter."
                " Soft realistic close foley, a quiet natural metal-on-metal clink, no jingle,"
                " no music, no voices, no reverb, no dramatic impact.")


def revision_steps() -> list[tuple[str, str, float]]:
    steps: list[tuple[str, str, float]] = []
    for actor, greet in (("merchant", "merchant-greet"), ("priestess", "priestess-greet")):
        steps.append(("design", actor, len(REVISION_GREETINGS[greet]) * .09 / 1000))
        steps.append(("review", actor, .01))
        steps.append(("clone", actor, .0007 * 6 / 60))
    # Independent candidates have immutable paid intents. V4 has substantially
    # more audible breath and rasp than the previous female candidate.
    steps += [("design", "priestess-v2", len(REVISION_GREETINGS["priestess-greet"]) * .09 / 1000),
              ("review", "priestess-v2", .01),
              ("design", "priestess-v3", len(REVISION_GREETINGS["priestess-greet"]) * .09 / 1000),
              ("review", "priestess-v3", .01),
              ("review2", "priestess-v3", .002),
              ("review3", "priestess-v3", .002),
              ("design", "priestess-v4", len(REVISION_GREETINGS["priestess-greet"]) * .09 / 1000),
              ("review3", "priestess-v4", .002),
              ("review", "priestess-v4", .01),
              ("clone-v4", "priestess", .0007 * 6 / 60)]
    for name, line in REVISION_LINES.items():
        steps.append(("eleven" if name == "enchantress-invite" else
                      "speak06-clean" if name == "priestess-prayer" else "speak06", name,
                      len(line) * (.10 if name == "enchantress-invite" else .09) / 1000))
    for name in ("priestess-prayer", "priestess-donate"):
        steps.append(("speak-v4", name, len(REVISION_LINES[name]) * .09 / 1000))
    steps.append(("effect", "coin-soft", .002))
    steps.append(("review", "coin-soft", .01))
    return steps


def revision_plan() -> None:
    steps = revision_steps()
    estimate = sum(cost for _, _, cost in steps)
    if estimate > .13:
        raise RuntimeError("Voice revision exceeds USD 0.13 estimated ceiling")
    for stage, name, cost in steps:
        save_json(REVISION_OUT / stage / name / "plan.json",
                  {"stage": stage, "name": name, "estimated_usd": round(cost, 6)})
    print(f"Planned {len(steps)} one-shot calls; listed-price estimate USD {estimate:.4f}.")


def revision_input(stage: str, name: str) -> tuple[str, dict]:
    if stage == "design":
        greeting = REVISION_GREETINGS[("priestess" if name.startswith("priestess") else name) + "-greet"]
        return "fal-ai/qwen-3-tts/voice-design/1.7b", {
            "text": greeting, "prompt": REVISION_DESCRIPTIONS[name], "language": "English",
            "temperature": .65, "max_new_tokens": 350}
    if stage == "clone":
        chosen = "priestess-v2" if name == "priestess" else name
        result = json.loads((REVISION_OUT / "design" / chosen / "result.json").read_text())
        return "fal-ai/qwen-3-tts/clone-voice/0.6b", {
            "audio_url": result["audio"]["url"],
            "reference_text": REVISION_GREETINGS[name + "-greet"]}
    if stage == "clone-v4":
        result = json.loads((REVISION_OUT / "design" / "priestess-v4" / "result.json").read_text())
        return "fal-ai/qwen-3-tts/clone-voice/0.6b", {
            "audio_url": result["audio"]["url"],
            "reference_text": REVISION_GREETINGS["priestess-greet"]}
    if stage == "review":
        result = json.loads((REVISION_OUT / ("effect" if name == "coin-soft" else "design") /
                             name / "result.json").read_text())
        return "fal-ai/audio-understanding", {
            "audio_url": result["audio"]["url"],
            "prompt": ("Describe the audible sound events, including whether this resembles quiet real coins making contact on wood, and whether there is any music, voice, electronic chime or exaggerated jingle."
                       if name == "coin-soft" else
                       "Describe only the speaker's perceived gender, approximate age range, pitch, rasp, chest resonance, expressiveness, and whether any natural laugh is audible. Do not use the spoken words as evidence. If a quality is uncertain, say so."),
            "detailed_analysis": True}
    if stage == "review2" or stage == "review3":
        result = json.loads((REVISION_OUT / "design" / name / "result.json").read_text())
        return "openrouter/router/audio", {
            "audio_url": result["audio"]["url"], "model": "google/gemini-3.8-flash",
            "prompt": "Listen to the audio itself. Describe the speaker's perceived gender, age range, vocal roughness/rasp, and speaking style. Ignore the text content. State uncertainty clearly.",
            "max_tokens": 220, "temperature": 0,
            "reasoning": True if stage == "review3" else False}
    if stage in ("speak06", "speak06-clean", "speak-v4"):
        actor = name.split("-")[0]
        clone_stage = "clone-v4" if stage == "speak-v4" else "clone"
        result = json.loads((REVISION_OUT / clone_stage / actor / "result.json").read_text())
        return "fal-ai/qwen-3-tts/text-to-speech/0.6b", {
            "text": REVISION_LINES[name], "language": "English",
            "speaker_voice_embedding_file_url": result["speaker_embedding"]["url"],
            "reference_text": REVISION_GREETINGS[actor + "-greet"],
            "temperature": .65, "max_new_tokens": 350}
    if stage == "eleven":
        return ENDPOINT, {"text": REVISION_LINES[name], "voice": VOICES["enchantress"],
                          "language_code": "en", "stability": .5, "similarity_boost": .8,
                          "apply_text_normalization": "auto", "timestamps": True}
    if stage == "effect":
        return "fal-ai/elevenlabs/sound-effects/v2", {
            "text": REVISION_SFX, "duration_seconds": 1.0,
            "prompt_influence": .55, "loop": False}
    raise ValueError(stage)


def revision_submit(stage: str, name: str) -> None:
    folder = REVISION_OUT / stage / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(stage, name, "already submitted")
        return
    if (folder / "intent.json").exists():
        raise RuntimeError(f"Ambiguous paid intent for {stage}/{name}; reconcile provider history")
    endpoint, payload = revision_input(stage, name)
    if sum(cost for _, _, cost in revision_steps()) > .13:
        raise RuntimeError("Voice revision exceeds estimated USD 0.13 ceiling")
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest(),
                   "estimated_usd": planned["estimated_usd"], "endpoint": endpoint}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + endpoint, payload)
    save_json(folder / "receipt.json", receipt)
    print(stage, name, "submitted", receipt.get("request_id", "unknown"))


def revision_collect(stage: str, name: str) -> None:
    folder = REVISION_OUT / stage / name
    if (folder / "result.json").exists():
        print(stage, name, "already collected")
        return
    receipt = json.loads((folder / "receipt.json").read_text())
    status = request(receipt["status_url"])
    save_json(folder / "status.json", status)
    if status.get("status") != "COMPLETED":
        print(stage, name, status.get("status")); return
    result = request(receipt["response_url"])
    save_json(folder / "result.json", result)
    print(stage, name, "collected")
    if stage in ("clone", "clone-v4", "review", "review2", "review3"): return
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https": raise ValueError("Provider audio URL must be HTTPS")
    with urllib.request.urlopen(url, timeout=120) as response:
        data = response.read(2_000_001)
    if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
        raise ValueError("Unexpected audio response")
    mp3 = folder / "audio.mp3"; mp3.write_bytes(data)
    if stage == "design":
        import_audio(("priestess" if name.startswith("priestess") else name) + "-greet", mp3, replace=True)
    elif stage in ("speak06", "speak06-clean", "speak-v4", "eleven"):
        import_audio(name, mp3, replace=True)
    else:
        import_audio(name, mp3, replace=True, target_lufs=-34)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "submit", "collect", "import-greetings", "curves",
                                           "revision-plan", "revision-submit", "revision-collect"))
    parser.add_argument("names", nargs="*")
    args = parser.parse_args()
    if args.action.startswith("revision-"):
        if args.action == "revision-plan": revision_plan()
        else:
            if len(args.names) != 2: parser.error("revision calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in revision_steps()}:
                parser.error("Unknown revision stage/name")
            (revision_submit if args.action == "revision-submit" else revision_collect)(stage, name)
        raise SystemExit(0)
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
