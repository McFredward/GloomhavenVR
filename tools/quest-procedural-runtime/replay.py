"""Replay genuine native requests and compare original per-entity task buffers.

This is host protocol evidence. It does not establish Android performance or playability.
"""
from __future__ import annotations
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import struct
import time
from protocol import Client, string, windows_path
from tasks import canonical_task


def checked_buffer(root: Path, row: dict) -> bytes:
    path = (root / row["buffer"]).resolve()
    if path.parent != root.resolve():
        raise ValueError("Journal buffer must be an immediate sibling")
    data = path.read_bytes()
    if len(data) != row["size"] or hashlib.sha256(data).hexdigest() != row["sha256"]:
        raise ValueError(f"Journal buffer integrity failed: {path.name}")
    return data


def asset_payload(context: int, descriptor: str, asset_id: int, row: dict) -> bytes:
    frame = row["frame"]
    if len(frame) not in (0, 15):
        raise ValueError("Native resource frame must contain exactly 15 floats")
    return struct.pack("<i", context) + string(descriptor) + struct.pack("<iiiI", asset_id, row["boundsAvailable"], row["variantCount"], len(frame) * 4) + struct.pack("<" + "f" * len(frame), *frame)


def replay(client: Client, journal: Path, library: Path, procedures: Path) -> dict:
    rows = [json.loads(line) for line in journal.read_text().splitlines()]
    for row in rows:
        if row.get("schema") != 1:
            raise ValueError("Unsupported original native journal")
        if row["buffer"]:
            checked_buffer(journal.parent, row)
    replies = {row["descriptor"]: row for row in rows if row["operation"] == "asset-response" and row["descriptor"]}
    expected = {row["result"]: checked_buffer(journal.parent, row) for row in rows if row["operation"] == "entity-task"}
    descriptor_ids = {name: index + 1 for index, name in enumerate(sorted(replies))}
    expected_resources = {row["assetId"]: descriptor_ids[row["descriptor"]] for row in rows if row["operation"] == "asset-response" and row["descriptor"]}
    actual_resources = {}
    raw_exact = 0
    initial = string(windows_path(library)) + string(windows_path(procedures)) + struct.pack("<iii", 4, 35, 0)
    if client.request(1, initial) != struct.pack("<i", 1):
        raise RuntimeError("Original procedural engine failed to initialize")
    mapping = {}
    collected = {}
    asset_requests = []
    started = time.monotonic()
    try:
        # Original entity creation/build order and all exact effective parameters.
        # Poll scheduling is independent of the original editor's frame rate.
        for row in rows:
            op = row["operation"]
            if op == "create":
                old = mapping.get(row["entity"], row["entity"])
                mapping[row["result"]] = struct.unpack("<i", client.request(6, struct.pack("<i", old)))[0]
            elif op == "build":
                data = checked_buffer(journal.parent, row)
                client.request(8, struct.pack("<iIiI", mapping[row["entity"]], row["procedure"], int(row["dynamicDetail"]), len(data)) + data)
            elif op == "asset-response" and not row["descriptor"]:
                client.request(11, asset_payload(row["entity"], "", row["assetId"], row))
        inverse = {value: key for key, value in mapping.items()}
        deadline = time.monotonic() + 180
        while len(collected) != len(expected):
            if time.monotonic() >= deadline:
                raise TimeoutError(f"Only {len(collected)}/{len(expected)} native task buffers arrived; original logs: {client.logs[-8:]}")
            client.request(3, struct.pack("<ffff", .1, 0, 0, 0))
            for unused in range(256):
                response = client.request(12)
                context, asset_id, length = struct.unpack_from("<iii", response)
                if length == -1:
                    break
                if length < 0 or len(response) != length + 12:
                    raise RuntimeError("Invalid copied native resource descriptor")
                descriptor = response[12:].decode("utf-8")
                if descriptor not in replies:
                    raise RuntimeError(f"No exact original resource reply for {descriptor!r}")
                # Original manager returns global context zero for these resources.
                fixture = replies[descriptor]
                reply_context = mapping.get(fixture["entity"], fixture["entity"])
                client.request(11, asset_payload(reply_context, descriptor, asset_id, fixture))
                actual_resources[asset_id] = descriptor_ids[descriptor]
                asset_requests.append(descriptor)
            for unused in range(256):
                response = client.request(9)
                handle, length = struct.unpack_from("<ii", response)
                if not handle:
                    break
                if len(response) != length + 8 or handle not in inverse:
                    raise RuntimeError("Native task handle or copied byte count differs")
                original = inverse[handle]
                if original in collected:
                    raise RuntimeError(f"Unexpected second native task for entity {original}")
                data = response[8:]
                collected[original] = data
                raw_exact += data == expected[original]
                if canonical_task(data, actual_resources) != canonical_task(expected[original], expected_resources):
                    output = journal.parent.parent / f"worker-task-{original}.bin"
                    output.write_bytes(data)
                    raise AssertionError(f"Original task bytes differ for entity {original}: expected {len(expected[original])}/{hashlib.sha256(expected[original]).hexdigest()}, actual {len(data)}/{hashlib.sha256(data).hexdigest()}; {output}")
            time.sleep(.005)
        if Counter(asset_requests) != Counter(row["descriptor"] for row in rows if row["operation"] == "asset-request"):
            raise AssertionError("Original asset descriptor request multiset differs")
        for row in rows:
            if row["operation"] == "destroy":
                client.request(7, struct.pack("<i", mapping[row["entity"]]))
        return {"schema": 1, "originalLibrarySha256": hashlib.sha256(library.read_bytes()).hexdigest(),
                "journalSha256": hashlib.sha256(journal.read_bytes()).hexdigest(), "exactEntities": len(collected),
                "exactTaskBytes": sum(map(len, collected.values())), "rawExactTasks": raw_exact,
                "canonicalization": "Only documented object-set keys and resource IDs resolved by exact descriptor; no geometry/parameter byte changes", "assetRequests": len(asset_requests),
                "buildRequests": sum(row["operation"] == "build" for row in rows),
                "elapsedSeconds": time.monotonic() - started, "androidExecutionVerified": False,
                "originalCallbacks": [dict(level=level, text=text.decode("utf-8", errors="replace")) for level, text in client.logs]}
    finally:
        client.request(5)


def main():
    parser = argparse.ArgumentParser()
    for name in ("worker", "library", "procedures", "journal", "prefix", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--wine", type=Path, default=Path("/usr/lib/wine/wine64"))
    args = parser.parse_args()
    args.prefix.mkdir(parents=True, exist_ok=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, WINEPREFIX=str(args.prefix.resolve()), WINEDEBUG="-all", WINEDLLOVERRIDES="mscoree,mshtml=")
    with args.output.with_suffix(".stderr.log").open("wb") as stderr:
        client = Client([str(args.wine), str(args.worker)], environment, stderr)
        try:
            receipt = replay(client, args.journal, args.library, args.procedures)
            args.output.write_text(json.dumps(receipt, indent=2) + "\n")
            print(json.dumps({key: value for key, value in receipt.items() if key != "originalCallbacks"}))
        finally:
            client.close()


if __name__ == "__main__":
    main()
