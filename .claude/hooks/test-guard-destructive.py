#!/usr/bin/env python3
"""
Tests for .claude/hooks/guard-destructive.py.

Run:  python3 .claude/hooks/test-guard-destructive.py

A guard nobody tests is a guard nobody can trust. Every case below is a command that
could plausibly be typed in this repository. MUST_BLOCK is the damage list; MUST_ALLOW
is the daily work that has to keep running, because a guard that blocks real work gets
switched off and then protects nothing.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

GUARD = Path(__file__).resolve().parent / "guard-destructive.py"

MUST_BLOCK = [
    # the headline risk
    "gh repo delete McFredward/GloomhavenVR",
    "gh repo delete --yes McFredward/GloomhavenVR",
    "/usr/bin/gh repo delete McFredward/GloomhavenVR",
    "sudo gh repo delete x",
    "GH_TOKEN=abc gh repo delete x",
    # hidden behind a separator, a pipe, a subshell, a newline
    "ls && gh repo delete x",
    "echo hi; gh repo delete x",
    "gh repo list | head -1 && gh repo delete x",
    "echo $(gh repo delete x)",
    "ls\ngh repo delete x",
    # everything else that destroys published state
    "gh release delete v0.1.0",
    "gh release delete-asset v0.1.0 GloomhavenVR-0.1.0.zip",
    "gh repo archive McFredward/GloomhavenVR",
    "gh repo rename something-else",
    "gh repo edit --visibility private",
    "gh secret delete GH_TOKEN",
    "gh variable delete FOO",
    "gh cache delete --all",
    "gh run delete 123",
    "gh label delete bug",
    "gh issue delete 4",
    "gh gist delete abc",
    "gh ssh-key delete 1",
    "gh auth logout",
    "gh extension remove foo",
    # the api escape hatch
    "gh api -X DELETE /repos/McFredward/GloomhavenVR",
    "gh api --method DELETE /repos/x/y/git/refs/tags/v0.1.0",
    "gh api --method=delete /repos/x/y",
    "gh api graphql -f query='mutation { deleteRepository }'",
    # remote-destructive git
    "git push --force origin main",
    "git push -f origin main",
    "git push --force-with-lease origin dev",
    "git push origin --delete dev",
    "git push origin :main",
    "git push origin +dev:main",
    "git push --mirror origin",
    # a here-document fed to a SHELL is a script, and must still be judged
    "bash <<'EOF'\ngh repo delete x\nEOF",
    "bash <<EOF\ngit push --force origin main\nEOF",
    "python3 <<'PY'\nimport os\nPY\ngh repo delete x",
]

MUST_ALLOW = [
    # the release procedure itself
    "git push origin dev:main",
    "git push -q origin dev",
    "git push -u origin dev",
    "git push --atomic origin HEAD:main refs/tags/v0.1.0",
    "git push origin refs/tags/v0.1.0",
    # ordinary local work, including local history surgery
    "git status --short",
    "git branch -D worktree-agent-abc",
    "git worktree remove --force .claude/worktrees/agent-abc",
    "git reset --hard origin/dev",
    "git stash list",
    "git commit -q -m 'delete the dead code path'",
    # additive and read-only gh
    "gh auth status",
    "gh repo view McFredward/GloomhavenVR",
    "gh release create v0.1.0 dist/GloomhavenVR-0.1.0.zip",
    "gh pr create --fill",
    "gh run list --limit 5",
    "gh api /repos/McFredward/GloomhavenVR/branches/main/protection",
    "gh api -X PUT /repos/McFredward/GloomhavenVR/branches/main/protection --input -",
    "gh api -X POST /repos/McFredward/GloomhavenVR/rulesets --input rules.json",
    "gh api -X PATCH /repos/McFredward/GloomhavenVR",
    # unrelated commands that merely contain the letters
    "./scripts/build.sh",
    "grep -rn 'delete' src/ | head",
    "rm -rf /home/claw/.claude/jobs/2eeab786/tmp/scratch",
    # a here-document fed to something that CONSUMES it is data, and its prose — which
    # may quite reasonably describe the very commands this guard refuses, and may
    # contain an apostrophe shlex would read as an unterminated quote — is not judged.
    # The guard blocked its own commit over this on its first live run.
    "git commit -q -F - <<'EOF'\nchore: refuse `gh repo delete`; it doesn't belong here\nEOF",
    "cat > /tmp/notes.md <<'EOF'\nDon't run git push --force on main\nEOF",
    # ATTRIBUTION: the body belongs to `cat`, even though an interpreter appears later
    # in the same command. Judging every body whenever any interpreter was present
    # refused exactly this, on the second live run.
    "cat >> docs/CI-CD.md <<'EOF'\nIt refuses `gh repo delete`; don't work around it\nEOF\npython3 tests.py",
    # ATTRIBUTION through a brace group: `{` was left as the first word, cleaned to the
    # empty string, read as "owner unknown" and failed closed — refusing a commit whose
    # MESSAGE contained the word "remove". Third false refusal, same root cause.
    "git commit -F x || { cat > msg.txt <<'EOF'\nlocal git worktree remove is fine\nEOF\ngit commit -F msg.txt; }",
]

# Commands that must still be REFUSED even though a here-document is involved, so the
# attribution fixes above cannot be mistaken for a way through.
MUST_BLOCK += [
    "true || { bash <<'EOF'\ngh repo delete x\nEOF\n}",
]


def run(command: str) -> tuple[int, str]:
    payload = json.dumps({"tool_name": "Bash", "tool_input": {"command": command}})
    proc = subprocess.run(
        [sys.executable, str(GUARD)], input=payload, capture_output=True, text=True
    )
    return proc.returncode, proc.stderr


def main() -> int:
    failures: list[str] = []

    for cmd in MUST_BLOCK:
        code, _ = run(cmd)
        if code != 2:
            failures.append(f"NOT BLOCKED (exit {code}): {cmd!r}")

    for cmd in MUST_ALLOW:
        code, err = run(cmd)
        if code != 0:
            failures.append(f"WRONGLY BLOCKED (exit {code}): {cmd!r}\n    {err.strip()}")

    # An unparsable command that mentions gh must fail CLOSED.
    code, _ = run("gh repo delete 'unterminated")
    if code != 2:
        failures.append("unparsable gh command was not refused — the guard failed OPEN")

    total = len(MUST_BLOCK) + len(MUST_ALLOW) + 1
    if failures:
        print(f"guard: {len(failures)} of {total} cases FAILED\n")
        for f in failures:
            print("  " + f)
        return 1

    print(
        f"guard: {total} cases pass — {len(MUST_BLOCK)} destructive commands refused, "
        f"{len(MUST_ALLOW)} ordinary commands allowed, and an unparsable one fails closed."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
