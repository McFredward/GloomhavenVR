#!/usr/bin/env python3
"""Prune only named development Actions artifacts; dry-run unless --apply is given.

Uses the authenticated gh CLI. The complete, validated listing is collected before
any deletion, so pagination cannot shift underneath this process's own mutations.
Release assets, workflow runs, logs and caches are never addressed.
"""

import argparse
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import json
import os
import re
import subprocess
import sys


DEV_NAME = re.compile(r"GloomhavenVR-dev-[0-9a-fA-F]{40}(?:-[1-9][0-9]*-[1-9][0-9]*)?", re.ASCII)
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", re.ASCII)
PAGE_SIZE = 100


class CleanupError(Exception):
    """An invalid listing or failed API request; no credentials are included."""


@dataclass(frozen=True)
class Artifact:
    id: int
    name: str
    created_at: datetime
    expired: bool
    size_in_bytes: int


def api(method, endpoint):
    """Return API text, tolerating only a DELETE's already-missing artifact."""
    try:
        result = subprocess.run(
            ["gh", "api", "--method", method, endpoint],
            capture_output=True, text=True, check=False,
        )
    except OSError as exc:
        raise CleanupError("Could not execute gh; install and authenticate GitHub CLI.") from exc
    if result.returncode:
        if method == "DELETE" and re.search(r"\(HTTP 404\)", result.stderr):
            return None
        # CLI stderr can contain account information; never echo it or credentials.
        raise CleanupError(f"GitHub API {method} failed for {endpoint} (exit {result.returncode}).")
    return result.stdout


def integer(value, field, minimum=0):
    if type(value) is not int or value < minimum:
        raise CleanupError(f"Artifact listing has invalid {field}.")
    return value


def timestamp(value):
    if not isinstance(value, str):
        raise CleanupError("Artifact listing has invalid created_at.")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError("Missing timezone")
        return parsed.astimezone(timezone.utc)
    except ValueError as exc:
        raise CleanupError("Artifact listing has invalid created_at.") from exc


def snapshot(repo, transport=api):
    """Read every Actions-artifact page and validate before returning candidates."""
    if not REPOSITORY.fullmatch(repo) or any(part in (".", "..") for part in repo.split("/")):
        raise CleanupError("Repository must be OWNER/REPO.")
    artifacts = []
    seen = set()
    page = 1
    while True:
        raw = transport("GET", f"repos/{repo}/actions/artifacts?per_page={PAGE_SIZE}&page={page}")
        try:
            data = json.loads(raw)
        except (ValueError, TypeError) as exc:
            raise CleanupError("GitHub artifact listing is not valid JSON.") from exc
        if not isinstance(data, dict) or not isinstance(data.get("artifacts"), list):
            raise CleanupError("GitHub artifact listing has no artifacts array.")
        integer(data.get("total_count"), "total_count")
        items = data["artifacts"]
        if len(items) > PAGE_SIZE:
            raise CleanupError("GitHub artifact page exceeds the requested page size.")
        for item in items:
            if not isinstance(item, dict):
                raise CleanupError("GitHub artifact listing contains a non-object.")
            artifact_id = integer(item.get("id"), "id", 1)
            name = item.get("name")
            if not isinstance(name, str) or artifact_id in seen:
                raise CleanupError("GitHub artifact listing has an invalid name or duplicate id; retry cleanup.")
            seen.add(artifact_id)
            if not DEV_NAME.fullmatch(name):
                continue
            if type(item.get("expired")) is not bool:
                raise CleanupError("Artifact listing has invalid expired flag.")
            artifacts.append(Artifact(
                artifact_id, name, timestamp(item.get("created_at")), item["expired"],
                integer(item.get("size_in_bytes"), "size_in_bytes"),
            ))
        if len(items) < PAGE_SIZE:
            return artifacts
        page += 1


def plan(artifacts, keep, max_age_days, now):
    if type(keep) is not int or keep < 0 or type(max_age_days) is not int or max_age_days < 1:
        raise CleanupError("Keep must be nonnegative and max age must be at least one day.")
    try:
        cutoff = now - timedelta(days=max_age_days)
    except OverflowError as exc:
        raise CleanupError("Max age exceeds the supported date range.") from exc
    ordered = sorted(artifacts, key=lambda artifact: (artifact.created_at, artifact.id), reverse=True)
    survivors = 0
    deletions = []
    for artifact in ordered:
        if artifact.expired:
            reason = "expired"
        elif artifact.created_at <= cutoff:
            reason = "age"
        elif survivors >= keep:
            reason = "count"
        else:
            survivors += 1
            continue
        deletions.append((artifact, reason))
    return deletions


def cleanup(repo, keep=3, max_age_days=2, apply=False, transport=api, now=None):
    # Validate limits before making requests, then snapshot before any mutation.
    current_time = now if now is not None else datetime.now(timezone.utc)
    plan([], keep, max_age_days, current_time)
    artifacts = snapshot(repo, transport)
    deletions = plan(artifacts, keep, max_age_days, current_time)
    size = sum(artifact.size_in_bytes for artifact, _ in deletions)
    print(f"{'Apply' if apply else 'Dry-run'}: {len(artifacts)} matching dev artifacts; "
          f"delete {len(deletions)}, keep {len(artifacts) - len(deletions)}; "
          f"{size} bytes selected for removal.")
    for artifact, reason in deletions:
        print(f"{'Delete' if apply else 'Would delete'} {artifact.id} {artifact.name} ({reason})")
        if apply:
            transport("DELETE", f"repos/{repo}/actions/artifacts/{artifact.id}")
    return len(deletions)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY"), help="OWNER/REPO; defaults to GITHUB_REPOSITORY")
    parser.add_argument("--keep", type=int, default=3, help="Maximum surviving dev artifacts (default: 3; use 2 before upload)")
    parser.add_argument("--max-age-days", type=int, default=2, help="Delete artifacts this old or older (default: 2)")
    parser.add_argument("--apply", action="store_true", help="Actually delete the selected artifacts")
    args = parser.parse_args(argv)
    if not args.repo:
        parser.error("--repo or GITHUB_REPOSITORY is required")
    try:
        cleanup(args.repo, args.keep, args.max_age_days, args.apply)
    except CleanupError as exc:
        print(f"Artifact cleanup failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
