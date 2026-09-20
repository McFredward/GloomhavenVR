#!/usr/bin/env python3
"""Prepare and execute a bounded, resumable fal NPC comparison.

Credentials are read only from FAL_AI_API_KEY in the process environment.
This script never opens .env. Launch with uv --env-file when needed.
Submission is never retried automatically: an uncertain POST requires manual
account reconciliation. Raw provider assets and local request receipts persist.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".planning/debug/npc-meshes"
NPCS = ("merchant", "priestess", "enchantress")
ENDPOINTS = {"trellis": "fal-ai/trellis-2", "hunyuan": "fal-ai/hunyuan3d-v3/image-to-3d"}


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def prepare():
    from PIL import Image
    for npc in NPCS:
        source = ROOT / f".planning/debug/npc-modeling/{npc}-reference-sheet.png"
        with Image.open(source) as sheet:
            assert sheet.size == (3840, 2160)
            records = []
            for i, view in enumerate(("front", "left", "back")):
                # Exclude panel dividers and labels; preserve full silhouettes.
                box = (1280 * i + 10, 10, 1280 * (i + 1) - 10, 1015)
                crop = sheet.crop(box).convert("RGB")
                path = OUT / npc / "inputs" / f"{view}.png"
                path.parent.mkdir(parents=True, exist_ok=True)
                crop.save(path)
                records.append({"view": view, "crop": box, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
            save(OUT / npc / "inputs/manifest.json", {"source": str(source.relative_to(ROOT)), "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(), "views": records})
        for candidate in ENDPOINTS:
            params = ({"resolution": "1536", "texture_size": "4096", "seed": 20260920,
                       "decimation_target": 500000, "remesh": True,
                       "ss_sampling_steps": 12, "shape_slat_sampling_steps": 12,
                       "tex_slat_sampling_steps": 12}
                      if candidate == "trellis" else {"generate_type": "Normal", "enable_pbr": True})
            inputs = ({"image_url": "front.png"} if candidate == "trellis" else
                      {"input_image_url": "front.png", "left_image_url": "left.png", "back_image_url": "back.png"})
            path = OUT / npc / candidate / "plan.json"
            if not path.exists():
                save(path, {"endpoint": ENDPOINTS[candidate], "parameters": params,
                            "inputs": inputs, "estimated_usd": .35 if candidate == "trellis" else .675,
                            "rationale": "Highest Trellis geometry and 4K textures, documented default sampling; compare Hunyuan multiview/PBR without paid custom face count. No automatic variants."})
    print("Prepared 9 input views and 6 plans; estimated generation total USD 3.075.")


def request(url, payload=None, auth=True):
    headers = {"Content-Type": "application/json"}
    if auth:
        if not url.startswith("https://queue.fal.run/"):
            raise ValueError("Refusing to send credential outside fal queue origin")
        headers["Authorization"] = "Key " + os.environ["FAL_AI_API_KEY"]
    body = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(url, data=body, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=90) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # Do not print headers, request bodies or credentials.
        raise RuntimeError(f"Provider HTTP {error.code}; no automatic resubmission") from None


def submit(npc, candidate):
    folder = OUT / npc / candidate
    plan = json.loads((folder / "plan.json").read_text())
    receipt = folder / "receipt.json"
    intent = folder / "submission-intent.json"
    if receipt.exists():
        print(npc, candidate, "already submitted; use collect")
        return
    if intent.exists():
        raise RuntimeError("Submission intent exists without receipt. Reconcile provider history before another paid call.")
    args = dict(plan["parameters"])
    for field, filename in plan["inputs"].items():
        data = (OUT / npc / "inputs" / filename).read_bytes()
        args[field] = "data:image/png;base64," + base64.b64encode(data).decode()
    if not os.environ.get("FAL_AI_API_KEY"):
        raise RuntimeError("FAL_AI_API_KEY missing from process environment")
    save(intent, {"endpoint": plan["endpoint"], "estimated_usd": plan["estimated_usd"], "plan_sha256": hashlib.sha256((folder / "plan.json").read_bytes()).hexdigest()})
    result = request("https://queue.fal.run/" + plan["endpoint"], args)
    save(receipt, result)
    print(npc, candidate, "submitted", result["request_id"], "estimated USD", plan["estimated_usd"])


def collect(npc, candidate):
    folder = OUT / npc / candidate
    if (folder / "model.glb").exists():
        print(npc, candidate, "downloaded")
        return
    receipt = json.loads((folder / "receipt.json").read_text())
    status = request(receipt["status_url"])
    save(folder / "status.json", status)
    print(npc, candidate, status.get("status"))
    if status.get("status") != "COMPLETED":
        return
    result = request(receipt["response_url"])
    save(folder / "result.json", result)
    url = result["model_glb"]["url"]
    if not url.startswith("https://"):
        raise ValueError("Non-HTTPS asset URL")
    target = folder / "model.glb"
    temporary = target.with_suffix(".download")
    with urllib.request.urlopen(url, timeout=180) as response, temporary.open("wb") as stream:
        while chunk := response.read(1024 * 1024):
            stream.write(chunk)
    data = temporary.read_bytes()
    if data[:4] != b"glTF":
        raise ValueError("Downloaded asset is not GLB")
    temporary.replace(target)
    save(folder / "asset.json", {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data), "request_id": receipt["request_id"]})
    print(npc, candidate, "saved GLB", len(data), "bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "submit", "collect"))
    parser.add_argument("--npc", choices=NPCS)
    parser.add_argument("--candidate", choices=tuple(ENDPOINTS))
    options = parser.parse_args()
    if options.action == "prepare":
        prepare()
    else:
        if not options.npc or not options.candidate:
            parser.error("--npc and --candidate are required")
        globals()[options.action](options.npc, options.candidate)
