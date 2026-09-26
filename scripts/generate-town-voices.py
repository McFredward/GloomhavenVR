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
VARIANT_OUT = ROOT / ".planning/debug/town562-speech"
ROUND563_OUT = ROOT / ".planning/debug/town563-speech"
ROUND564_OUT = ROOT / ".planning/debug/town564-speech"
ROUND569_OUT = ROOT / ".planning/debug/town569-speech"
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

# Cue order is a wire contract: TLV80 carries the selected cue number. Every
# event has five complete English performances. Only the elected author makes
# the bounded random choice, after which every peer plays this exact asset.
VOICE_EVENTS = {
    "merchant-greet": (
        ("merchant-greet", "Welcome. Take your time and look around."),
        ("merchant-greet-2", "Good to see you. I may have just what you need."),
        ("merchant-greet-3", "Come closer. The stock is ready for you."),
        ("merchant-greet-4", "Welcome back. Let us find something useful."),
        ("merchant-greet-5", "Have a look. Every piece has earned its place.")),
    "priestess-greet": (
        ("priestess-greet", "Welcome, traveler. The Great Oak watches over you."),
        ("priestess-greet-2", "Come closer, traveler. You are welcome here."),
        ("priestess-greet-3", "Rest a moment. The Great Oak shelters us all."),
        ("priestess-greet-4", "Welcome, child. May this quiet place bring you peace."),
        ("priestess-greet-5", "Traveler, you stand beneath the Great Oak's care.")),
    "enchantress-greet": (
        ("enchantress-greet", "So, you seek a little more power."),
        ("enchantress-greet-2", "There is always more hidden within a card."),
        ("enchantress-greet-3", "Come in. Let us wake the magic you carry."),
        ("enchantress-greet-4", "You have brought me something interesting."),
        ("enchantress-greet-5", "Step closer. The runes are already listening.")),
    "merchant-offer": (
        ("merchant-offer", "Let me take a closer look at that."),
        ("merchant-offer-2", "Set it here. I will give it a fair appraisal."),
        ("merchant-offer-3", "That caught your eye? Hand it over."),
        ("merchant-offer-4", "Let us see what you have brought."),
        ("merchant-offer-5", "Place it in my hand. I will inspect it.")),
    "merchant-buy": (
        ("merchant-buy", "A fine choice. It should serve you well."),
        ("merchant-buy-2", "Excellent. That one belongs in capable hands."),
        ("merchant-buy-3", "A sound purchase. Use it wisely."),
        ("merchant-buy-4", "Good choice. I think you will be pleased."),
        ("merchant-buy-5", "Done. May it see you safely home.")),
    "merchant-sell": (
        ("merchant-sell", "A fair trade. I will take good care of it."),
        ("merchant-sell-2", "Sold. Here is a fair price."),
        ("merchant-sell-3", "A useful piece. We have a deal."),
        ("merchant-sell-4", "Agreed. The coin is yours."),
        ("merchant-sell-5", "That will do nicely. A fair exchange.")),
    "priestess-prayer": (
        ("priestess-prayer", "Great Oak, guide us through the dark."),
        ("priestess-prayer-2", "Great Oak, shelter those who walk beyond these walls."),
        ("priestess-prayer-3", "Let root and branch guard every weary traveler."),
        ("priestess-prayer-4", "May the Great Oak lend us patience and strength."),
        ("priestess-prayer-5", "Keep our companions safe beneath your ancient branches.")),
    "priestess-donate": (
        ("priestess-donate", "May your offering bring you strength."),
        ("priestess-donate-2", "The Great Oak receives your generous offering."),
        ("priestess-donate-3", "Your kindness will not be forgotten."),
        ("priestess-donate-4", "May this gift guard your path ahead."),
        ("priestess-donate-5", "With gratitude, I place your offering before the Great Oak.")),
    "enchantress-cast": (
        ("enchantress-cast-ember", "[whispers] Wake, hidden spark."),
        ("enchantress-cast-echo", "By ember and echo, take shape."),
        ("enchantress-cast-spark", "Threads of light, gather in my hand."),
        ("enchantress-cast-veil", "Let the veil bend, but never break."),
        ("enchantress-cast-rune", "Old rune, answer and awaken.")),
    "enchantress-enhance": (
        ("enchantress-enhance", "The power is yours to command."),
        ("enchantress-enhance-2", "There. The enchantment has taken hold."),
        ("enchantress-enhance-3", "Its magic runs deeper now."),
        ("enchantress-enhance-4", "The rune is bound. Use it well."),
        ("enchantress-enhance-5", "Your card carries a stronger spell.")),
    "enchantress-invite": (
        ("enchantress-invite", "Bring me your card. Let us see what magic it can bear."),
        ("enchantress-invite-2", "Place the card in my hand, and I will read its weave."),
        ("enchantress-invite-3", "Let me see the card. Its hidden paths may surprise you."),
        ("enchantress-invite-4", "Offer me the card, and we shall test its potential."),
        ("enchantress-invite-5", "Give me the card. I can show you what lies within.")),
    # Keep additive cue families at the end: TLV80 carries these numeric cue IDs.
    "priestess-unavailable": (
        ("priestess-unavailable", "The Great Oak has already blessed you. I must close the bowl for now."),
        ("priestess-unavailable-2", "One blessing is granted before each journey. Keep your offering for now."),
        ("priestess-unavailable-3", "Your offering was accepted earlier. I cannot receive another yet."),
        ("priestess-unavailable-4", "The blessing has already been given. Return after your next journey."),
        ("priestess-unavailable-5", "The temple has received your gift. The bowl must remain closed for now.")),
}
VARIANT_CUES = tuple(item for event in VOICE_EVENTS.values() for item in event)


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


def variant_curves() -> None:
    """Bake all five performances for each shared event in wire cue order."""
    rhubarb = Path(os.environ.get("RHUBARB_PATH", "/tmp/town543-rhubarb/Rhubarb-Lip-Sync-1.14.0-Linux/rhubarb"))
    if not rhubarb.is_file():
        raise FileNotFoundError("Rhubarb Lip Sync 1.14 executable not found; set RHUBARB_PATH")
    for cue, (name, _) in enumerate(VARIANT_CUES, 1):
        wav = ASSETS / (name + ".wav")
        if not wav.is_file():
            raise FileNotFoundError(wav)
        raw = VARIANT_OUT / "rhubarb" / name / "rhubarb.json"
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
        save_json(ASSETS / (name + ".json"), {
            "schema": 1, "cue": cue, "service": service, "language": "en",
            "duration": duration, "packed": packed, "mouthCues": intervals})
        meta = ASSETS / (name + ".json.meta")
        if not meta.exists():
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nTextScriptImporter:\n"
                            "  externalObjects: {}\n  userData: \n  assetBundleName: \n"
                            "  assetBundleVariant: \n")
        print(f"{cue:2d} {name}: {duration:.2f}s, {len(intervals)} mouth intervals")


VARIANT_MERCHANT_DESCRIPTION = (
    "A close-miked English-speaking man in his late forties with a naturally deep, clear, warm baritone. "
    "He is a well-fed, friendly fantasy merchant speaking calmly to one customer at arm's length. "
    "Full chest resonance, crisp consonants, an occasional restrained chuckle, and relaxed conversational energy. "
    "No shouting, calling across a market, announcer projection, room echo, distant sound, gravelly monster voice, "
    "radio processing, or exaggerated acting.")
VARIANT_COIN_DESCRIPTION = (
    "Exactly one soft dry click from two small old metal coins gently touching in a merchant's fingertips. "
    "Very quiet close foley, natural dull metal contact, under half a second, followed by silence. "
    "No handful, spill, jingle, ringing tail, impact, music, voice, reverb, or electronic tone.")


def variant_required_lines() -> list[tuple[str, str, str]]:
    lines: list[tuple[str, str, str]] = []
    for event in VOICE_EVENTS.values():
        for name, text in event:
            actor = name.split("-")[0]
            # Existing priestess/enchantress first takes stay in the approved
            # voice. The rejected merchant voice is replaced in full.
            existing_first = not name[-1:].isdigit() and name not in (
                "enchantress-cast-spark", "enchantress-cast-veil", "enchantress-cast-rune")
            if actor != "merchant" and existing_first:
                continue
            lines.append((actor, name, text))
    return lines


def variant_steps() -> list[tuple[str, str, float]]:
    steps = [
        ("design", "merchant", len(VOICE_EVENTS["merchant-greet"][0][1]) * .09 / 1000),
        ("review", "merchant", .01),
        ("clone", "merchant", .0007 * 4 / 60),
    ]
    for actor, name, text in variant_required_lines():
        if actor == "merchant" and name == "merchant-greet":
            continue  # the voice-design reference is this exact line
        steps.append(("eleven" if actor == "enchantress" else "speak", name,
                      len(text) * (.10 if actor == "enchantress" else .09) / 1000))
    steps += [("effect", "coin-soft", .002), ("review", "coin-soft", .01)]
    return steps


def variant_plan() -> None:
    estimate = sum(cost for _, _, cost in variant_steps())
    if estimate > .22:
        raise RuntimeError("Variant batch exceeds USD 0.22 displayed-price ceiling")
    for stage, name, cost in variant_steps():
        save_json(VARIANT_OUT / stage / name / "plan.json",
                  {"stage": stage, "name": name, "estimated_usd": round(cost, 6)})
    print(f"Planned {len(variant_steps())} one-shot calls; listed-price estimate USD {estimate:.4f}.")


def variant_line(name: str) -> tuple[str, str]:
    for event in VOICE_EVENTS.values():
        for candidate, text in event:
            if candidate == name:
                return candidate.split("-")[0], text
    raise KeyError(name)


def prior_priestess_embedding() -> dict:
    configured = os.environ.get("TOWN_PRIESTESS_EMBEDDING_RESULT")
    path = Path(configured) if configured else Path(
        "/home/claw/worktrees/npc561-voice/.planning/debug/town561-speech/clone-v4/priestess/result.json")
    if not path.is_file():
        raise FileNotFoundError("Set TOWN_PRIESTESS_EMBEDDING_RESULT to the build-561 clone result")
    return json.loads(path.read_text())


def variant_input(stage: str, name: str) -> tuple[str, dict]:
    if stage == "design":
        return "fal-ai/qwen-3-tts/voice-design/1.7b", {
            "text": VOICE_EVENTS["merchant-greet"][0][1],
            "prompt": VARIANT_MERCHANT_DESCRIPTION, "language": "English",
            "temperature": .45, "repetition_penalty": 1.15, "max_new_tokens": 120}
    if stage == "clone":
        result = json.loads((VARIANT_OUT / "design" / "merchant" / "result.json").read_text())
        return "fal-ai/qwen-3-tts/clone-voice/0.6b", {
            "audio_url": result["audio"]["url"],
            "reference_text": VOICE_EVENTS["merchant-greet"][0][1]}
    if stage == "review":
        result = json.loads((VARIANT_OUT / ("effect" if name == "coin-soft" else "design") /
                             name / "result.json").read_text())
        return "fal-ai/audio-understanding", {
            "audio_url": result["audio"]["url"],
            "prompt": ("Describe the sound events and loudness impression. Confirm whether this is one quiet, dry, natural coin touch with no jingle, ringing tail, electronic tone, voice, music, or reverb."
                       if name == "coin-soft" else
                       "Judge the recorded sound, not the words: describe perceived age, pitch, clarity, warmth, distance, room echo, and whether the man sounds conversational or is shouting. Be direct about any mismatch."),
            "detailed_analysis": True}
    if stage == "speak":
        actor, text = variant_line(name)
        result = (json.loads((VARIANT_OUT / "clone" / "merchant" / "result.json").read_text())
                  if actor == "merchant" else prior_priestess_embedding())
        return "fal-ai/qwen-3-tts/text-to-speech/0.6b", {
            "text": text, "language": "English",
            "speaker_voice_embedding_file_url": result["speaker_embedding"]["url"],
            "reference_text": (VOICE_EVENTS["merchant-greet"][0][1] if actor == "merchant"
                               else VOICE_EVENTS["priestess-greet"][0][1]),
            "temperature": .45, "repetition_penalty": 1.15, "max_new_tokens": 120}
    if stage == "eleven":
        _, text = variant_line(name)
        return ENDPOINT, {"text": text, "voice": VOICES["enchantress"], "language_code": "en",
                          "stability": .55, "similarity_boost": .82,
                          "apply_text_normalization": "auto", "timestamps": True}
    if stage == "effect":
        return "fal-ai/elevenlabs/sound-effects/v2", {
            "text": VARIANT_COIN_DESCRIPTION, "duration_seconds": .5,
            "prompt_influence": .8, "loop": False}
    raise ValueError(stage)


def variant_submit(stage: str, name: str) -> None:
    folder = VARIANT_OUT / stage / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(stage, name, "already submitted"); return
    if (folder / "intent.json").exists():
        raise RuntimeError(f"Ambiguous paid intent for {stage}/{name}; reconcile provider history")
    endpoint, payload = variant_input(stage, name)
    if sum(cost for _, _, cost in variant_steps()) > .22:
        raise RuntimeError("Variant batch exceeds USD 0.22 displayed-price ceiling")
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest(),
                   "estimated_usd": planned["estimated_usd"], "endpoint": endpoint}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + endpoint, payload)
    save_json(folder / "receipt.json", receipt)
    print(stage, name, "submitted", receipt.get("request_id", "unknown"))


def variant_collect(stage: str, name: str) -> None:
    folder = VARIANT_OUT / stage / name
    result_path = folder / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
        print(stage, name, "already collected")
    else:
        receipt = json.loads((folder / "receipt.json").read_text())
        status = request(receipt["status_url"])
        save_json(folder / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(stage, name, status.get("status")); return
        result = request(receipt["response_url"])
        save_json(result_path, result)
        print(stage, name, "collected")
    if stage in ("clone", "review"): return
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https": raise ValueError("Provider audio URL must be HTTPS")
    with urllib.request.urlopen(url, timeout=120) as response:
        data = response.read(2_000_001)
    if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
        raise ValueError("Unexpected audio response")
    mp3 = folder / "audio.mp3"
    if not mp3.exists(): mp3.write_bytes(data)
    target = "merchant-greet" if stage == "design" else name
    import_audio(target, mp3, replace=True,
                 target_lufs=-42 if stage == "effect" else -27)


def compress_imports() -> None:
    """Use mono Vorbis in the bundle; source WAVs remain lossless for Rhubarb."""
    names = [name for name, _ in VARIANT_CUES] + ["coin-soft", "cabinet-cycle"]
    for name in names:
        wav = ASSETS / (name + ".wav")
        if not wav.is_file(): raise FileNotFoundError(wav)
        meta = wav.with_suffix(".wav.meta")
        if not meta.exists():
            guid = uuid.uuid4().hex
        else:
            guid = next(line.split(":", 1)[1].strip() for line in meta.read_text().splitlines()
                        if line.startswith("guid:"))
        meta.write_text("fileFormatVersion: 2\nguid: " + guid + "\nAudioImporter:\n"
                        "  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n"
                        "    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n"
                        "    sampleRateOverride: 24000\n    compressionFormat: 1\n    quality: 0.45\n"
                        "    conversionMode: 0\n  platformSettingOverrides: {}\n  forceToMono: 1\n"
                        "  normalize: 0\n  preloadAudioData: 1\n  loadInBackground: 0\n"
                        "  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n"
                        "  assetBundleVariant: \n")
    print(f"Configured {len(names)} resident clips for mono Vorbis bundle storage.")


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


# Build 563 listening revision. The user rejected the priestess voice as distant
# and identified merchant-sell-2 ("Here is a fair price") as a broken take. One
# designed reference per actor is cloned once; every priestess performance uses
# the same immutable embedding. The cabinet gets one short physical mechanism
# recording rather than three unrelated UI clicks.
ROUND563_REFERENCES = {
    "merchant": ("Good day, friend. Here is a fair price.",
        "A close-miked English-speaking man in his late forties with a naturally deep, clear, warm baritone. "
        "He is a well-fed, friendly fantasy merchant speaking calmly to one customer at arm's length. "
        "Full chest resonance and crisp consonants, relaxed and conversational. No shouting, announcer projection, "
        "distance, room echo, radio processing, monster voice or exaggerated acting."),
    "priestess": (VOICE_EVENTS["priestess-greet"][0][1],
        "A close-miked elderly English-speaking woman around eighty-five. Her voice is unmistakably feminine, "
        "low and smoky with a weathered hoarse rasp, soft breath and subtle age tremor. She is kind, intimate, "
        "quietly devotional and clearly beside the listener. Dry studio sound with no room echo or distant quality. "
        "Never masculine, young, theatrical, booming, witch-like, monstrous or processed."),
}
ROUND563_PRIESTESS = tuple(item for key in ("priestess-greet", "priestess-prayer", "priestess-donate")
                           for item in VOICE_EVENTS[key])
ROUND563_CABINET_DESCRIPTION = (
    "One compact hand-operated wooden fantasy merchant card cabinet mechanism over 0.85 seconds: "
    "a soft wooden latch and short shutter slide, a muted leather-and-card cassette roll, then one gentle wooden stop. "
    "Close dry physical foley, restrained and quiet. No voice, music, electronic UI tone, bell, coin, loud impact, "
    "long reverb or background ambience.")


def round563_steps() -> list[tuple[str, str, float]]:
    steps = []
    for actor in ("merchant", "priestess"):
        steps.append(("design", actor, len(ROUND563_REFERENCES[actor][0]) * .09 / 1000))
        steps.append(("clone", actor, .0007 * 5 / 60))
    for name, text in ROUND563_PRIESTESS:
        if name != "priestess-greet":
            steps.append(("speak", name, len(text) * .09 / 1000))
    steps.append(("speak", "merchant-sell-2", len(VOICE_EVENTS["merchant-sell"][1][1]) * .09 / 1000))
    steps.append(("effect", "cabinet-cycle", .002))
    return steps


def round563_plan() -> None:
    steps = round563_steps()
    estimate = sum(cost for _, _, cost in steps)
    if estimate > .10:
        raise RuntimeError("Build 563 audio batch exceeds USD 0.10 displayed-price ceiling")
    for stage, name, cost in steps:
        save_json(ROUND563_OUT / stage / name / "plan.json",
                  {"stage": stage, "name": name, "estimated_usd": round(cost, 6)})
    print(f"Planned {len(steps)} one-shot calls; listed-price estimate USD {estimate:.4f}.")


def round563_input(stage: str, name: str) -> tuple[str, dict]:
    if stage == "design":
        text, prompt = ROUND563_REFERENCES[name]
        return "fal-ai/qwen-3-tts/voice-design/1.7b", {
            "text": text, "prompt": prompt, "language": "English",
            "temperature": .45, "max_new_tokens": 240}
    if stage == "clone":
        result = json.loads((ROUND563_OUT / "design" / name / "result.json").read_text())
        return "fal-ai/qwen-3-tts/clone-voice/0.6b", {
            "audio_url": result["audio"]["url"], "reference_text": ROUND563_REFERENCES[name][0]}
    if stage == "speak":
        actor = name.split("-")[0]
        lines = dict(ROUND563_PRIESTESS)
        text = lines[name] if actor == "priestess" else VOICE_EVENTS["merchant-sell"][1][1]
        result = json.loads((ROUND563_OUT / "clone" / actor / "result.json").read_text())
        return "fal-ai/qwen-3-tts/text-to-speech/0.6b", {
            "text": text, "language": "English",
            "speaker_voice_embedding_file_url": result["speaker_embedding"]["url"],
            "reference_text": ROUND563_REFERENCES[actor][0],
            "temperature": .38, "repetition_penalty": 1.2, "max_new_tokens": 120}
    if stage == "effect":
        return "fal-ai/elevenlabs/sound-effects/v2", {
            "text": ROUND563_CABINET_DESCRIPTION, "duration_seconds": .85,
            "prompt_influence": .78, "loop": False}
    raise ValueError(stage)


def round563_submit(stage: str, name: str) -> None:
    folder = ROUND563_OUT / stage / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(stage, name, "already submitted"); return
    if (folder / "intent.json").exists():
        raise RuntimeError(f"Ambiguous paid intent for {stage}/{name}; reconcile provider history")
    endpoint, payload = round563_input(stage, name)
    if sum(cost for _, _, cost in round563_steps()) > .10:
        raise RuntimeError("Build 563 audio batch exceeds USD 0.10 displayed-price ceiling")
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest(),
                   "estimated_usd": planned["estimated_usd"], "endpoint": endpoint}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + endpoint, payload)
    save_json(folder / "receipt.json", receipt)
    print(stage, name, "submitted", receipt.get("request_id", "unknown"))


def round563_collect(stage: str, name: str) -> None:
    folder = ROUND563_OUT / stage / name
    result_path = folder / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
        print(stage, name, "already collected")
    else:
        receipt = json.loads((folder / "receipt.json").read_text())
        status = request(receipt["status_url"])
        save_json(folder / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(stage, name, status.get("status")); return
        result = request(receipt["response_url"])
        save_json(result_path, result)
        print(stage, name, "collected")
    if stage == "clone": return
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https": raise ValueError("Provider audio URL must be HTTPS")
    mp3 = folder / "audio.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected audio response")
        mp3.write_bytes(data)
    if stage == "design" and name == "merchant":
        return  # reference-only; keep the already accepted merchant greeting asset
    target = (name + "-greet") if stage == "design" else name
    import_audio(target, mp3, replace=True, target_lufs=-34 if stage == "effect" else -27)


# Build-564 listening revision. Hardware listening rejected every Qwen-designed
# priestess take as too young despite text/audio classifiers describing them as
# elderly. MiniMax's named Wise_Woman performance is therefore used directly;
# this avoids cloning an identifiable person from an unverified web recording
# and gives every line one stable, commercially licensed preset voice.
ROUND564_ENDPOINT = "fal-ai/minimax/speech-2.8-hd"
ROUND564_CANDIDATES = {
    "natural": {"speed": .88, "pitch": 0, "modify": None},
    "weathered": {"speed": .86, "pitch": -1,
                  "modify": {"pitch": -4, "intensity": -10, "timbre": -12}},
    "frail": {"speed": .82, "pitch": 0,
               "modify": {"pitch": -2, "intensity": -18, "timbre": -18}},
}
# Keep the named performance unprocessed. The lower-timbre previews sounded
# shorter/louder in provider output and risk replacing "young" with an equally
# artificial effect; Wise_Woman itself is the age-specific source.
ROUND564_SELECTED = "natural"


def round564_steps() -> list[tuple[str, str, float]]:
    preview = VOICE_EVENTS["priestess-greet"][0][1]
    steps = [("preview", name, len(preview) * .10 / 1000)
             for name in ROUND564_CANDIDATES]
    steps.extend(("speak", name, len(text) * .10 / 1000)
                 for name, text in ROUND563_PRIESTESS)
    # The first take made "boughs" sound like another English word in local
    # transcription. Keep that paid result immutable and create one explicit
    # corrected take with the clearer authored wording above.
    steps.append(("respeak", "priestess-prayer-5",
                  len(dict(ROUND563_PRIESTESS)["priestess-prayer-5"]) * .10 / 1000))
    return steps


def round564_plan() -> None:
    steps = round564_steps()
    estimate = sum(cost for _, _, cost in steps)
    if estimate > .16:
        raise RuntimeError("Build 564 priestess batch exceeds USD 0.16 displayed-price ceiling")
    for stage, name, cost in steps:
        save_json(ROUND564_OUT / stage / name / "plan.json",
                  {"stage": stage, "name": name, "estimated_usd": round(cost, 6)})
    print(f"Planned {len(steps)} one-shot calls; listed-price estimate USD {estimate:.4f}.")


def round564_payload(stage: str, name: str) -> dict:
    if stage == "preview":
        text = VOICE_EVENTS["priestess-greet"][0][1]
        settings = ROUND564_CANDIDATES[name]
    elif stage in ("speak", "respeak"):
        text = dict(ROUND563_PRIESTESS)[name]
        settings = ROUND564_CANDIDATES[ROUND564_SELECTED]
    else:
        raise ValueError(stage)
    payload = {
        "prompt": text,
        "voice_setting": {
            "voice_id": "Wise_Woman", "speed": settings["speed"],
            "vol": 1.0, "pitch": settings["pitch"], "emotion": "neutral",
            "english_normalization": True,
        },
        "audio_setting": {"sample_rate": 24000, "bitrate": 128000,
                          "format": "mp3", "channel": 1},
        "language_boost": "English", "output_format": "url",
        "normalization_setting": {"enabled": True, "target_loudness": -22,
                                  "target_range": 8, "target_peak": -3},
    }
    if settings["modify"]:
        payload["voice_modify"] = settings["modify"]
    return payload


def round564_submit(stage: str, name: str) -> None:
    folder = ROUND564_OUT / stage / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(stage, name, "already submitted"); return
    if (folder / "intent.json").exists():
        raise RuntimeError(f"Ambiguous paid intent for {stage}/{name}; reconcile provider history")
    payload = round564_payload(stage, name)
    if sum(cost for _, _, cost in round564_steps()) > .16:
        raise RuntimeError("Build 564 priestess batch exceeds USD 0.16 displayed-price ceiling")
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest(),
                   "estimated_usd": planned["estimated_usd"], "endpoint": ROUND564_ENDPOINT}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + ROUND564_ENDPOINT, payload)
    save_json(folder / "receipt.json", receipt)
    print(stage, name, "submitted", receipt.get("request_id", "unknown"))


def round564_collect(stage: str, name: str) -> None:
    folder = ROUND564_OUT / stage / name
    result_path = folder / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
        print(stage, name, "already collected")
    else:
        receipt = json.loads((folder / "receipt.json").read_text())
        status = request(receipt["status_url"])
        save_json(folder / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(stage, name, status.get("status")); return
        result = request(receipt["response_url"])
        save_json(result_path, result)
        print(stage, name, "collected")
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https": raise ValueError("Provider audio URL must be HTTPS")
    mp3 = folder / "audio.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected audio response")
        mp3.write_bytes(data)
    if stage in ("speak", "respeak"):
        import_audio(name, mp3, replace=True, target_lufs=-27)


# Build 569 adds one contextual family to the already approved Wise_Woman
# performance. Five direct takes are cheaper and more consistent than designing
# or cloning another voice, and their names append to the stable cue table.
ROUND569_PRIESTESS = VOICE_EVENTS["priestess-unavailable"]


def round569_steps() -> list[tuple[str, str, float]]:
    return [("speak", name, len(text) * .10 / 1000)
            for name, text in ROUND569_PRIESTESS]


def round569_plan() -> None:
    steps = round569_steps()
    estimate = sum(cost for _, _, cost in steps)
    if estimate > .04:
        raise RuntimeError("Build 569 contextual priestess batch exceeds USD 0.04 displayed-price ceiling")
    for stage, name, cost in steps:
        save_json(ROUND569_OUT / stage / name / "plan.json",
                  {"stage": stage, "name": name, "estimated_usd": round(cost, 6)})
    print(f"Planned {len(steps)} one-shot calls; listed-price estimate USD {estimate:.4f}.")


def round569_payload(name: str) -> dict:
    text = dict(ROUND569_PRIESTESS)[name]
    settings = ROUND564_CANDIDATES[ROUND564_SELECTED]
    return {
        "prompt": text,
        "voice_setting": {
            "voice_id": "Wise_Woman", "speed": settings["speed"],
            "vol": 1.0, "pitch": settings["pitch"], "emotion": "neutral",
            "english_normalization": True,
        },
        "audio_setting": {"sample_rate": 24000, "bitrate": 128000,
                          "format": "mp3", "channel": 1},
        "language_boost": "English", "output_format": "url",
        "normalization_setting": {"enabled": True, "target_loudness": -22,
                                  "target_range": 8, "target_peak": -3},
    }


def round569_submit(stage: str, name: str) -> None:
    folder = ROUND569_OUT / stage / name
    planned = json.loads((folder / "plan.json").read_text())
    if (folder / "receipt.json").exists():
        print(stage, name, "already submitted"); return
    if (folder / "intent.json").exists():
        raise RuntimeError(f"Ambiguous paid intent for {stage}/{name}; reconcile provider history")
    payload = round569_payload(name)
    if sum(cost for _, _, cost in round569_steps()) > .04:
        raise RuntimeError("Build 569 contextual priestess batch exceeds USD 0.04 displayed-price ceiling")
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / "intent.json").open("x") as stream:
        json.dump({"input_sha256": hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest(),
                   "estimated_usd": planned["estimated_usd"], "endpoint": ROUND564_ENDPOINT}, stream)
        stream.flush(); os.fsync(stream.fileno())
    receipt = request("https://queue.fal.run/" + ROUND564_ENDPOINT, payload)
    save_json(folder / "receipt.json", receipt)
    print(stage, name, "submitted", receipt.get("request_id", "unknown"))


def round569_collect(stage: str, name: str) -> None:
    folder = ROUND569_OUT / stage / name
    result_path = folder / "result.json"
    if result_path.exists():
        result = json.loads(result_path.read_text())
        print(stage, name, "already collected")
    else:
        receipt = json.loads((folder / "receipt.json").read_text())
        status = request(receipt["status_url"])
        save_json(folder / "status.json", status)
        if status.get("status") != "COMPLETED":
            print(stage, name, status.get("status")); return
        result = request(receipt["response_url"])
        save_json(result_path, result)
        print(stage, name, "collected")
    url = result["audio"]["url"]
    if urlparse(url).scheme != "https": raise ValueError("Provider audio URL must be HTTPS")
    mp3 = folder / "audio.mp3"
    if not mp3.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read(2_000_001)
        if len(data) > 2_000_000 or not (data.startswith(b"ID3") or data[:2] in (b"\xff\xfb", b"\xff\xf3")):
            raise ValueError("Unexpected audio response")
        mp3.write_bytes(data)
    import_audio(name, mp3, replace=True, target_lufs=-27)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "submit", "collect", "import-greetings", "curves",
                                           "revision-plan", "revision-submit", "revision-collect",
                                           "variant-plan", "variant-submit", "variant-collect",
                                           "variant-curves", "compress-imports",
                                           "round563-plan", "round563-submit", "round563-collect",
                                           "round564-plan", "round564-submit", "round564-collect",
                                           "round569-plan", "round569-submit", "round569-collect"))
    parser.add_argument("names", nargs="*")
    args = parser.parse_args()
    if args.action.startswith("round569-"):
        if args.action == "round569-plan": round569_plan()
        else:
            if len(args.names) != 2: parser.error("round569 calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in round569_steps()}:
                parser.error("Unknown round569 stage/name")
            (round569_submit if args.action == "round569-submit" else round569_collect)(stage, name)
        raise SystemExit(0)
    if args.action.startswith("round564-"):
        if args.action == "round564-plan": round564_plan()
        else:
            if len(args.names) != 2: parser.error("round564 calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in round564_steps()}:
                parser.error("Unknown round564 stage/name")
            (round564_submit if args.action == "round564-submit" else round564_collect)(stage, name)
        raise SystemExit(0)
    if args.action.startswith("round563-"):
        if args.action == "round563-plan": round563_plan()
        else:
            if len(args.names) != 2: parser.error("round563 calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in round563_steps()}:
                parser.error("Unknown round563 stage/name")
            (round563_submit if args.action == "round563-submit" else round563_collect)(stage, name)
        raise SystemExit(0)
    if args.action.startswith("revision-"):
        if args.action == "revision-plan": revision_plan()
        else:
            if len(args.names) != 2: parser.error("revision calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in revision_steps()}:
                parser.error("Unknown revision stage/name")
            (revision_submit if args.action == "revision-submit" else revision_collect)(stage, name)
        raise SystemExit(0)
    if args.action.startswith("variant-") or args.action == "compress-imports":
        if args.action == "variant-plan": variant_plan()
        elif args.action == "variant-curves": variant_curves()
        elif args.action == "compress-imports": compress_imports()
        else:
            if len(args.names) != 2: parser.error("variant calls need STAGE NAME")
            stage, name = args.names
            if (stage, name) not in {(s, n) for s, n, _ in variant_steps()}:
                parser.error("Unknown variant stage/name")
            (variant_submit if args.action == "variant-submit" else variant_collect)(stage, name)
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
