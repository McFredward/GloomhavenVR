#!/usr/bin/env python3
"""Submit one bounded merchant cabinet concept to fal and retain its receipt.

The credential comes only from FAL_AI_API_KEY in the process environment. Launch
with ``uv run --env-file /path/to/.env python3 scripts/generate-town-cabinet.py``.
An uncertain POST is never retried; inspect the saved intent/provider history.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / ".planning/research/town571-cabinet/empty-front-concept.png"
REAR = ROOT / ".planning/research/town571-cabinet/empty-rear-concept.png"
OUTPUT = ROOT / ".planning/debug/town571-cabinet/hunyuan"
ENDPOINT = "fal-ai/hunyuan3d-v3/image-to-3d"
# Official fal pricing: Normal $0.375 + PBR $0.15 + back view $0.15.
ESTIMATED_USD = 0.675


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def request(url, payload=None):
    if not url.startswith("https://queue.fal.run/"):
        raise ValueError("Refusing credentials outside fal queue origin")
    headers = {"Content-Type": "application/json", "Authorization": "Key " + os.environ["FAL_AI_API_KEY"]}
    body = None if payload is None else json.dumps(payload).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request(url, data=body, headers=headers), timeout=90) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Provider HTTP {error.code}; no automatic resubmission") from None


def submit():
    if not os.environ.get("FAL_AI_API_KEY"):
        raise RuntimeError("FAL_AI_API_KEY missing from process environment")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    if (OUTPUT / "intent.json").exists() or (OUTPUT / "receipt.json").exists():
        raise RuntimeError("Existing intent or receipt; never submit twice")
    image = SOURCE.read_bytes()
    rear = REAR.read_bytes()
    params = {
        "input_image_url": "data:image/png;base64," + base64.b64encode(image).decode(),
        "back_image_url": "data:image/png;base64," + base64.b64encode(rear).decode(),
        "generate_type": "Normal", "enable_pbr": True,
    }
    intent = {"endpoint": ENDPOINT, "estimated_usd": ESTIMATED_USD,
              "input_sha256": hashlib.sha256(image).hexdigest(),
              "back_sha256": hashlib.sha256(rear).hexdigest(),
              "parameters": {key: value for key, value in params.items() if not key.endswith("_url")},
              "started_utc": datetime.now(timezone.utc).isoformat()}
    with (OUTPUT / "intent.json").open("x") as stream:
        json.dump(intent, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    result = request("https://queue.fal.run/" + ENDPOINT, params)
    save(OUTPUT / "receipt.json", result)
    print("Submitted one cabinet model; estimated USD", ESTIMATED_USD, "request", result["request_id"])


def collect():
    if (OUTPUT / "model.glb").exists():
        print("Already downloaded")
        return
    receipt = json.loads((OUTPUT / "receipt.json").read_text())
    status = request(receipt["status_url"])
    save(OUTPUT / "status.json", status)
    print(status.get("status"))
    if status.get("status") != "COMPLETED":
        return
    result = request(receipt["response_url"])
    save(OUTPUT / "result.json", result)
    url = result["model_glb"]["url"]
    if not url.startswith("https://"):
        raise ValueError("Non-HTTPS provider asset URL")
    temporary = OUTPUT / "model.download"
    with urllib.request.urlopen(url, timeout=180) as response, temporary.open("wb") as stream:
        while chunk := response.read(1024 * 1024):
            stream.write(chunk)
    content = temporary.read_bytes()
    if content[:4] != b"glTF":
        raise ValueError("Provider asset is not GLB")
    temporary.replace(OUTPUT / "model.glb")
    save(OUTPUT / "asset.json", {"sha256": hashlib.sha256(content).hexdigest(),
                                 "bytes": len(content), "request_id": receipt["request_id"]})
    print("Saved provider GLB", len(content), "bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("submit", "collect"))
    argument = parser.parse_args()
    globals()[argument.action]()
