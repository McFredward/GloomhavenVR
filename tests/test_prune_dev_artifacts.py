"""Deletion-scope and API-boundary tests for the production artifact cleanup."""

from contextlib import redirect_stdout, redirect_stderr
from datetime import datetime, timedelta, timezone
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location(
    "prune_dev_artifacts", Path(__file__).resolve().parents[1] / "scripts" / "prune-dev-artifacts.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)
NOW = datetime(2026, 9, 13, 12, tzinfo=timezone.utc)
NAME = "GloomhavenVR-dev-" + "a" * 40


def artifact(artifact_id, *, name=NAME, days=0, expired=False):
    return {
        "id": artifact_id, "name": name, "created_at": (NOW - timedelta(days=days)).isoformat(),
        "expired": expired, "size_in_bytes": 9000000,
    }


class Transport:
    def __init__(self, pages):
        self.pages = list(pages)
        self.calls = []

    def __call__(self, method, endpoint):
        self.calls.append((method, endpoint))
        if method == "GET":
            value = self.pages.pop(0)
            if isinstance(value, Exception):
                raise value
            return json.dumps({"total_count": 1000, "artifacts": value})
        return ""


class CleanupTests(unittest.TestCase):
    def run_cleanup(self, transport, **kwargs):
        with redirect_stdout(io.StringIO()):
            return MODULE.cleanup("owner/repo", transport=transport, now=NOW, **kwargs)

    def deleted_ids(self, transport):
        return [int(endpoint.rsplit("/", 1)[1]) for method, endpoint in transport.calls if method == "DELETE"]

    def test_default_is_read_only(self):
        transport = Transport([[artifact(1, days=10)]])
        self.assertEqual(self.run_cleanup(transport), 1)
        self.assertEqual(self.deleted_ids(transport), [])

    def test_scope_excludes_release_like_names_logs_caches_and_near_matches(self):
        names = [
            "GloomhavenVR-v1.0.0", "GloomhavenVR-dev", "GloomhavenVR-dev-" + "a" * 39,
            NAME + "-backup", NAME + "\n", "prefix-" + NAME, NAME + "-123", NAME + "-0-1",
            NAME + "-123-0", NAME + "-123-1-extra", "logs", "cache", NAME + "/../../releases",
        ]
        values = [artifact(i + 1, name=name, days=10) for i, name in enumerate(names)]
        values += [artifact(101, days=10), artifact(102, name=NAME + "-123-2", days=10)]
        transport = Transport([values])
        self.run_cleanup(transport, apply=True)
        self.assertEqual(self.deleted_ids(transport), [102, 101])
        self.assertTrue(all("/actions/artifacts" in endpoint for _, endpoint in transport.calls))

    def test_snapshot_all_pages_before_delete(self):
        transport = Transport([
            [artifact(i, days=10) for i in range(1, 101)],
            [artifact(101, name="unrelated", days=10), artifact(102, days=10)],
        ])
        self.run_cleanup(transport, apply=True)
        self.assertEqual(transport.calls[:2], [
            ("GET", "repos/owner/repo/actions/artifacts?per_page=100&page=1"),
            ("GET", "repos/owner/repo/actions/artifacts?per_page=100&page=2"),
        ])
        self.assertEqual(len(self.deleted_ids(transport)), 101)
        self.assertNotIn(101, self.deleted_ids(transport))

    def test_full_final_page_requires_empty_page(self):
        transport = Transport([[artifact(i, days=10) for i in range(1, 101)], []])
        self.run_cleanup(transport, apply=True)
        self.assertEqual(transport.calls[1][0], "GET")
        self.assertEqual(len(self.deleted_ids(transport)), 100)

    def test_expired_age_and_count_combined_with_id_tie_break(self):
        transport = Transport([[
            artifact(5), artifact(6), artifact(2), artifact(3), artifact(1, days=2),
            artifact(9, expired=True), artifact(7, days=10),
        ]])
        self.run_cleanup(transport, apply=True)
        self.assertEqual(self.deleted_ids(transport), [9, 2, 1, 7])

    def test_keep_two_reserves_upload_slot(self):
        transport = Transport([[artifact(1), artifact(2), artifact(3)]])
        self.run_cleanup(transport, apply=True, keep=2)
        self.assertEqual(self.deleted_ids(transport), [1])

    def test_keep_zero_explicitly_deletes_all_matching(self):
        transport = Transport([[artifact(1), artifact(2)]])
        self.run_cleanup(transport, apply=True, keep=0)
        self.assertEqual(self.deleted_ids(transport), [2, 1])

    def test_empty_listing(self):
        transport = Transport([[]])
        self.assertEqual(self.run_cleanup(transport, apply=True), 0)

    def test_invalid_limits_make_no_requests(self):
        for options in ({"keep": -1}, {"max_age_days": 0}, {"max_age_days": 10**20}):
            with self.subTest(options=options):
                transport = Transport([])
                with self.assertRaises(MODULE.CleanupError):
                    self.run_cleanup(transport, apply=True, **options)
                self.assertEqual(transport.calls, [])

    def test_invalid_repo_makes_no_requests(self):
        for repo in ("/", "a/b/c", "a/..", "../b", "a/b?c", "a/b\n", "a/b;echo"):
            transport = Transport([])
            with self.subTest(repo=repo), self.assertRaises(MODULE.CleanupError):
                MODULE.snapshot(repo, transport)
            self.assertEqual(transport.calls, [])

    def test_corrupt_candidate_prevents_all_deletion(self):
        for field, value in (
            ("id", True), ("id", "1"), ("id", -1), ("expired", "false"),
            ("created_at", "not a date"), ("created_at", "2026-09-13"),
            ("size_in_bytes", -1), ("size_in_bytes", False),
        ):
            item = artifact(2, days=10)
            item[field] = value
            transport = Transport([[artifact(1, days=10), item]])
            with self.subTest(field=field, value=value), self.assertRaises(MODULE.CleanupError):
                self.run_cleanup(transport, apply=True)
            self.assertEqual(self.deleted_ids(transport), [])

    def test_second_page_failure_prevents_all_deletion(self):
        transport = Transport([
            [artifact(i, days=10) for i in range(1, 101)], MODULE.CleanupError("Unavailable"),
        ])
        with self.assertRaises(MODULE.CleanupError):
            self.run_cleanup(transport, apply=True)
        self.assertEqual(self.deleted_ids(transport), [])

    def test_duplicate_pagination_id_fails_without_deleting(self):
        transport = Transport([[artifact(i, days=10) for i in range(1, 101)], [artifact(100, days=10)]])
        with self.assertRaises(MODULE.CleanupError):
            self.run_cleanup(transport, apply=True)
        self.assertEqual(self.deleted_ids(transport), [])

    def test_malformed_page_fails(self):
        for body in ("broken", "[]", "{}", '{"total_count": 1, "artifacts": null}'):
            with self.subTest(body=body), self.assertRaises(MODULE.CleanupError):
                MODULE.snapshot("owner/repo", lambda *args: body)

    def test_delete_failure_stops_cleanup(self):
        transport = Transport([[artifact(1, days=10), artifact(2, days=10)]])

        def fail_delete(method, endpoint):
            if method == "DELETE":
                raise MODULE.CleanupError("Denied")
            return transport(method, endpoint)

        with self.assertRaises(MODULE.CleanupError):
            self.run_cleanup(fail_delete, apply=True)


class ApiTests(unittest.TestCase):
    def test_gh_invocation_and_json_return(self):
        with patch.object(MODULE.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "{}", "")) as run:
            self.assertEqual(MODULE.api("GET", "repos/owner/repo/actions/artifacts"), "{}")
            self.assertEqual(run.call_args.args[0], ["gh", "api", "--method", "GET", "repos/owner/repo/actions/artifacts"])

    def test_delete_404_already_gone_is_tolerated_only_for_delete(self):
        result = subprocess.CompletedProcess([], 1, "", "gh: Not Found (HTTP 404)")
        with patch.object(MODULE.subprocess, "run", return_value=result):
            self.assertIsNone(MODULE.api("DELETE", "repos/owner/repo/actions/artifacts/1"))
            with self.assertRaises(MODULE.CleanupError):
                MODULE.api("GET", "repos/owner/repo/actions/artifacts")

    def test_auth_and_server_failures_are_not_swallowed_or_echoed(self):
        for status in (401, 403, 429, 500):
            result = subprocess.CompletedProcess([], 1, "sensitive", f"secret-token (HTTP {status})")
            with patch.object(MODULE.subprocess, "run", return_value=result), self.assertRaises(MODULE.CleanupError) as caught:
                MODULE.api("DELETE", "repos/owner/repo/actions/artifacts/1")
            self.assertNotIn("secret", str(caught.exception))
            self.assertNotIn("sensitive", str(caught.exception))

    def test_missing_cli_fails_cleanly(self):
        with patch.object(MODULE.subprocess, "run", side_effect=FileNotFoundError()), self.assertRaises(MODULE.CleanupError):
            MODULE.api("GET", "repos/owner/repo/actions/artifacts")

    def test_cli_failure_returns_nonzero(self):
        with patch.object(MODULE, "cleanup", side_effect=MODULE.CleanupError("Denied")), redirect_stderr(io.StringIO()):
            self.assertEqual(MODULE.main(["--repo", "owner/repo", "--apply"]), 1)


if __name__ == "__main__":
    unittest.main()
