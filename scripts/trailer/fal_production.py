#!/usr/bin/env python3
"""Submit individually priced, resumable trailer jobs without printing credentials.

Only submit creates inference work. Status and fetch resume the saved queue receipt.
An uncertain submission remains reserved and must be investigated, never retried.
"""

import argparse
import fcntl
import hashlib
import json
import os
import re
import sys
import tempfile
import time
from contextlib import contextmanager
from decimal import Decimal
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import Request, urlopen
from urllib.error import HTTPError


REPO = Path(__file__).resolve().parents[2]
DEFAULT_ROOT = REPO / ".planning/debug/trailer/production"


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(dir=path.parent, prefix=".receipt-")
    try:
        with os.fdopen(fd, "w") as stream:
            json.dump(value, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def credentials():
    key = os.environ.get("FAL_KEY") or os.environ.get("FAL_AI_API_KEY")
    if not key and (REPO / ".env").is_file():
        for line in (REPO / ".env").read_text().splitlines():
            match = re.match(r"\s*(?:export\s+)?(?:FAL_KEY|FAL_AI_API_KEY)\s*=\s*(.*)", line)
            if match:
                key = match[1].strip().strip('"').strip("'")
    if not key:
        raise SystemExit("No local fal credential configured")
    return key


def api(url, key, payload=None):
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in {"queue.fal.run", "api.fal.ai"}:
        raise ValueError("Refusing authenticated request to an unexpected host")
    headers = {"Authorization": "Key " + key}
    body = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(payload).encode()
    with urlopen(Request(url, data=body, headers=headers), timeout=60) as response:
        return json.load(response)


@contextmanager
def ledger_lock(root):
    root.mkdir(parents=True, exist_ok=True)
    with (root / ".ledger.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        yield


def reserve(root, job, amount, budget):
    if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,79}", job):
        raise ValueError("Job ID must be a safe lowercase filename")
    amount, budget = Decimal(str(amount)), Decimal(str(budget))
    if not amount.is_finite() or not budget.is_finite() or amount <= 0 or budget <= 0:
        raise ValueError("Budget and reservation must be finite positive USD amounts")
    with ledger_lock(root):
        path = root / "ledger.json"
        ledger = json.loads(path.read_text()) if path.exists() else {
            "budget_usd": str(budget), "currency": "USD", "jobs": {}}
        # A CLI invocation cannot silently raise a previously configured budget.
        ceiling = min(Decimal(ledger["budget_usd"]), budget)
        ledger["budget_usd"] = str(ceiling)
        if job in ledger["jobs"]:
            raise ValueError("Job already reserved; resume status/fetch instead of resubmitting")
        total = sum(Decimal(v["reserved_usd"]) for v in ledger["jobs"].values())
        if total + amount > ceiling:
            raise ValueError("Submission exceeds the recorded production budget")
        ledger["jobs"][job] = {"reserved_usd": str(amount), "state": "reserved",
                               "created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
        write_json(path, ledger)
    return str(total + amount)


def update(root, job, **values):
    with ledger_lock(root):
        path = root / "ledger.json"
        ledger = json.loads(path.read_text())
        ledger["jobs"][job].update(values)
        write_json(path, ledger)


def upload_inputs(value, sources):
    if isinstance(value, dict) and set(value) == {"$file"}:
        import fal_client
        path = Path(value["$file"]).resolve(strict=True)
        sources.append({"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        return fal_client.upload_file(str(path))
    if isinstance(value, dict):
        return {k: upload_inputs(v, sources) for k, v in value.items()}
    if isinstance(value, list):
        return [upload_inputs(v, sources) for v in value]
    return value


def download_outputs(value, output, prefix="output"):
    if isinstance(value, dict) and isinstance(value.get("url"), str):
        url = value["url"]
        if urlparse(url).scheme != "https":
            raise ValueError("Output must use HTTPS")
        suffix = Path(urlparse(url).path).suffix or ".bin"
        path = output / (prefix + suffix)
        if not path.exists():
            with urlopen(url, timeout=90) as response, path.open("wb") as stream:
                while chunk := response.read(1024 * 1024):
                    stream.write(chunk)
        return [{"path": str(path), "bytes": path.stat().st_size,
                 "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}]
    records = []
    if isinstance(value, dict):
        for key, member in value.items():
            safe = re.sub(r"[^a-zA-Z0-9_-]", "_", key)
            records.extend(download_outputs(member, output, prefix + "_" + safe))
    elif isinstance(value, list):
        for index, member in enumerate(value):
            records.extend(download_outputs(member, output, prefix + "_" + str(index)))
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["submit", "status", "fetch", "ledger"])
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--job")
    parser.add_argument("--spec", type=Path)
    parser.add_argument("--reserve-usd", type=Decimal)
    parser.add_argument("--budget-usd", type=Decimal, default=Decimal("30"))
    args = parser.parse_args()
    root = args.root.resolve()
    if args.action == "ledger":
        print((root / "ledger.json").read_text())
        return
    if not args.job or not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,79}", args.job):
        parser.error("A safe --job is required")
    receipt_path = root / "jobs" / (args.job + ".json")
    key = credentials()
    if args.action == "submit":
        if not args.spec or args.reserve_usd is None:
            parser.error("Submit requires --spec and a reviewed --reserve-usd upper estimate")
        spec = json.loads(args.spec.read_text())
        endpoint = spec["endpoint"]
        if not re.fullmatch(r"[a-zA-Z0-9_.-]+(?:/[a-zA-Z0-9_.-]+)+", endpoint):
            raise ValueError("Invalid model endpoint")
        total = reserve(root, args.job, args.reserve_usd, args.budget_usd)
        os.environ["FAL_KEY"] = key
        sources = []
        try:
            arguments = upload_inputs(spec["arguments"], sources)
            receipt = {"job": args.job, "endpoint": endpoint, "arguments": arguments,
                       "sources": sources, "reserve_usd": str(args.reserve_usd),
                       "submission_state": "submitting"}
            write_json(receipt_path, receipt)
            # One POST. Never automatically repeat an inference submission.
            queue = api("https://queue.fal.run/" + endpoint, key, arguments)
            receipt.update(queue)
            receipt["submission_state"] = "queued"
            write_json(receipt_path, receipt)
            update(root, args.job, state="queued", endpoint=endpoint, request_id=queue["request_id"])
            print(json.dumps({"job": args.job, "request_id": queue["request_id"],
                              "reserved_total_usd": total}))
        except Exception as error:
            update(root, args.job, state="submission_uncertain", error_type=type(error).__name__)
            raise SystemExit("Submission did not finish cleanly; reservation retained. Inspect receipt before any new call.") from None
    else:
        receipt = json.loads(receipt_path.read_text())
        if args.action == "status":
            result = api(receipt["status_url"], key)
            write_json(root / "jobs" / (args.job + "-status.json"), result)
            print(json.dumps({"job": args.job, "status": result.get("status")}))
        else:
            status = api(receipt["status_url"], key)
            if status.get("status") != "COMPLETED":
                raise SystemExit("Job is not completed; no new inference submitted")
            try:
                result = api(receipt["response_url"], key)
            except HTTPError as error:
                # COMPLETED also describes a failed provider task. Save its
                # structured validation evidence without resubmitting or
                # releasing the conservative reservation.
                body = error.read().decode("utf-8", errors="replace")
                try:
                    detail = json.loads(body)
                except ValueError:
                    detail = {"response": body[:4000]}
                write_json(root / "jobs" / (args.job + "-error.json"), detail)
                update(root, args.job, state="failed", http_status=error.code)
                raise SystemExit(f"Provider task failed with HTTP {error.code}; error receipt saved; no new inference submitted") from None
            output = root / "outputs" / args.job
            output.mkdir(parents=True, exist_ok=True)
            write_json(output / "result.json", result)
            files = download_outputs(result, output)
            write_json(output / "files.json", files)
            update(root, args.job, state="completed", files=files)
            print(json.dumps({"job": args.job, "files": files}))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
