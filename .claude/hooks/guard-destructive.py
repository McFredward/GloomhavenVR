#!/usr/bin/env python3
"""
PreToolUse guard: refuse destructive GitHub operations before they run.

WHY THIS EXISTS
---------------
The repository owner is about to hand an agent an authenticated `gh`. The risk is not
malice, it is a slip: one `gh repo delete` typed into a Bash tool call, or a
`git push --force` that rewrites a release branch. This hook reads the command Claude
Code is about to run and refuses the ones that destroy something on GitHub.

WHAT IT IS AND IS NOT
---------------------
It is an ACCIDENT GUARD, not a sandbox. It inspects the command string the Bash tool
was given. A command that hides a `gh` call inside a script file (`bash deploy.sh`)
is not visible to it, by construction. The airtight control is the TOKEN SCOPE: a
GitHub token issued without `delete_repo` cannot delete a repository no matter what
runs. Use both; this one catches the realistic mistakes, the token scope closes the
worst case.

DESIGN RULES
------------
1. FAIL CLOSED. If the command mentions gh or git and cannot be parsed, refuse it.
   A guard that gives up on a command it cannot read is not a guard.
2. Scan the WHOLE command, not the first word. `a && gh repo delete x` is one string.
   `$(...)`, backticks, pipes, newlines and semicolons all carry a second command.
3. Deny by DEFAULT on unknown destructive-sounding verbs. `delete`, `remove`,
   `archive`, `transfer`, `rename` are refused wherever they appear as a bare gh
   argument, so a subcommand added by a future gh release is refused rather than
   waved through. A false refusal costs one rephrase; a false pass costs a repo.
4. Only REMOTE-destructive git operations are refused. Local history surgery
   (`git reset --hard`, `git branch -D`, `git worktree remove`) is ordinary work here
   and stays allowed — it cannot damage anything on GitHub.

Exit 0 = allow. Exit 2 = block, with the reason on stderr (Claude Code feeds that
back to the model).
"""

from __future__ import annotations

import json
import os
import re
import shlex
import sys

# --------------------------------------------------------------------------------------
# what counts as a command separator once shlex has tokenised the string
# --------------------------------------------------------------------------------------
SEPARATORS = {"&&", "||", "|", ";", "&", "\n", "|&", ";;"}

# --------------------------------------------------------------------------------------
# gh: bare arguments that are refused wherever they appear
#
# Deliberately verbs and not "subcommand paths": `gh repo delete`, `gh release delete`,
# `gh secret delete`, `gh cache delete` and every one gh grows next year all read the
# same way, and matching the verb covers them without this file needing to know the
# whole command tree.
# --------------------------------------------------------------------------------------
GH_DESTRUCTIVE_WORDS = {
    "delete",
    "delete-asset",
    "delete-assets",
    "remove",
    "archive",
    "unarchive",
    "transfer",
    "rename",
    "purge",
    "logout",  # gh auth logout — throws away the credential the owner just granted
}

# gh api: HTTP methods that change or destroy server state.
# PUT and PATCH are ALLOWED on purpose: branch protection is created with
# `PUT /repos/{owner}/{repo}/branches/{branch}/protection`, which is the very thing
# this guard exists to let happen safely. DELETE never is.
GH_API_FORBIDDEN_METHODS = {"DELETE"}

# --------------------------------------------------------------------------------------
# git: only the operations that can destroy something on the REMOTE
# --------------------------------------------------------------------------------------
GIT_PUSH_FORBIDDEN_FLAGS = {
    "-f",
    "--force",
    "--force-with-lease",
    "--force-if-includes",
    "-d",
    "--delete",
    "--mirror",
    "--prune",
}


def clean(token: str) -> str:
    """Strip shell decoration shlex leaves attached to a command word."""
    return token.lstrip("$(`{").rstrip(")`}")


def basename(token: str) -> str:
    return os.path.basename(clean(token))


# Programs that would EXECUTE a here-document instead of consuming it as data.
# `git commit -F -` and `cat > file` receive prose; `bash <<EOF` receives commands.
INTERPRETERS = {
    "bash", "sh", "zsh", "dash", "ksh", "fish",
    "python", "python3", "perl", "ruby", "node", "eval", "xargs", "source",
}

HEREDOC = re.compile(r"<<-?\s*(['\"]?)([A-Za-z_][A-Za-z0-9_]*)\1")


def heredoc_owner(prefix: str) -> str:
    """
    The program a here-document on this line is being fed to.

    Attribution matters and the coarse version was wrong: judging every body whenever
    ANY interpreter appeared somewhere in the command refused
    `cat >> doc.md <<EOF … EOF && python3 tests.py`, because `python3` was present and
    the body was prose. The owner is the first word of the LAST command segment before
    the `<<`, which is the segment the redirection attaches to.
    """
    last = re.split(r"(?:\|\||&&|[;|&\n])", prefix)[-1]
    try:
        words = strip_prefix_words(tokenise(last))
    except ValueError:
        return ""  # unreadable prefix — caller treats an unknown owner as a shell
    return basename(words[0]) if words else ""


def strip_prefix_words(words: list[str]) -> list[str]:
    """
    Drop shell grouping, `sudo`/`env`/`time` and VAR=value so the real program is first.

    The grouping tokens are not decoration: `… || { cat > f <<EOF` left `{` as the first
    word, which cleaned to the empty string, which read as "owner unknown", which fails
    closed — and the guard refused a commit whose MESSAGE happened to contain the word
    "remove". Third false refusal, same root: the attribution has to survive real shell.
    """
    i = 0
    while i < len(words):
        w = clean(words[i])
        if w in {"", "{", "(", "!", "then", "do", "else", "elif"}:
            i += 1
            continue
        if basename(w) in {"env", "command", "sudo", "nohup", "time", "builtin"}:
            i += 1
            continue
        if "=" in w and not w.startswith("-") and w.split("=", 1)[0].isidentifier():
            i += 1
            continue
        break
    return words[i:]


def strip_heredocs(command: str) -> tuple[str, list[tuple[str, str]]]:
    """
    Pull here-document BODIES out of the command before it is tokenised.

    A body is data, not syntax: a commit message contains apostrophes, and `shlex`
    reads one as an unterminated quote. The first live run of this guard refused its
    own commit for exactly that reason — correct fail-closed behaviour, wrong answer.
    Each body is returned WITH the program it is fed to, so `bash <<EOF` can still be
    judged as the script it is while `git commit -F -` is left alone.
    """
    lines = command.split("\n")
    kept: list[str] = []
    bodies: list[tuple[str, str]] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        kept.append(line)
        match = HEREDOC.search(line)
        i += 1
        if not match:
            continue
        owner = heredoc_owner(line[: match.start()])
        delimiter = match.group(2)
        body: list[str] = []
        while i < len(lines) and lines[i].strip() != delimiter:
            body.append(lines[i])
            i += 1
        i += 1  # step over the closing delimiter
        bodies.append((owner, "\n".join(body)))
    return "\n".join(kept), bodies


def tokenise(command: str) -> list[str]:
    """
    Tokenise with the shell's punctuation kept as its own tokens.

    `punctuation_chars=True` is what makes `echo hi; gh repo delete x` and
    `echo $(gh repo delete x)` come apart — plain `shlex.split` glues the `;` onto the
    preceding word and leaves `$(gh` as one token, and the first draft of this guard
    let both of those through. That was found by the test file next to this one, which
    is the only reason it is not still true.
    """
    lexer = shlex.shlex(command, posix=True, punctuation_chars=True)
    lexer.whitespace_split = True
    return list(lexer)


def invocations(tokens: list[str]) -> list[tuple[str, list[str]]]:
    """
    Every gh or git call in the stream, wherever it sits.

    Scanning EVERY position rather than only the start of a segment is deliberate: a
    newline is swallowed as whitespace, so `ls\\ngh repo delete x` has no separator
    token at all, and a command substitution puts the interesting word three tokens in.
    Arguments run to the next separator, or to the end — over-collecting arguments can
    only make this guard refuse more, which is the safe direction.
    """
    found: list[tuple[str, list[str]]] = []
    for i, tok in enumerate(tokens):
        prog = basename(tok)
        if prog not in {"gh", "git"}:
            continue
        args: list[str] = []
        for nxt in tokens[i + 1 :]:
            if nxt in SEPARATORS:
                break
            args.append(clean(nxt))
        found.append((prog, args))
    return found


def judge_gh(args: list[str]) -> str | None:
    """Return a refusal reason for a gh invocation, or None to allow it."""
    bare = [a for a in args if not a.startswith("-")]

    # gh api — the universal escape hatch. Judge the method, and refuse graphql
    # outright: a mutation is a free-form string this guard cannot read.
    if bare and bare[0] == "api":
        for i, a in enumerate(args):
            if a in {"-X", "--method"} and i + 1 < len(args):
                if args[i + 1].upper() in GH_API_FORBIDDEN_METHODS:
                    return f"`gh api` with method {args[i + 1].upper()}"
            if a.startswith("--method="):
                if a.split("=", 1)[1].upper() in GH_API_FORBIDDEN_METHODS:
                    return f"`gh api` with method {a.split('=', 1)[1].upper()}"
        if "graphql" in bare:
            return "`gh api graphql` (a mutation is free-form text this guard cannot read)"
        return None

    for a in bare:
        if a.lower() in GH_DESTRUCTIVE_WORDS:
            return f"`gh … {a} …`"

    # `gh repo edit --visibility private` hides a public repository from everyone who
    # has it. Not a deletion, indistinguishable from one for anybody depending on it.
    if bare[:2] == ["repo", "edit"]:
        for a in args:
            if a == "--visibility" or a.startswith("--visibility="):
                return "`gh repo edit --visibility`"

    return None


def judge_git(args: list[str]) -> str | None:
    """Return a refusal reason for a git invocation, or None to allow it."""
    bare = [a for a in args if not a.startswith("-")]
    if not bare or bare[0] != "push":
        return None  # local git is ordinary work in this repository

    for a in args:
        if a in GIT_PUSH_FORBIDDEN_FLAGS:
            return f"`git push {a}`"
        if a.startswith("--force"):
            return f"`git push {a}`"

    # `git push origin :branch` deletes the remote branch; a leading `+` on a refspec
    # is a force push in disguise.
    for a in bare[1:]:
        if a.startswith(":"):
            return f"`git push … {a}` (a colon refspec deletes the remote ref)"
        if a.startswith("+"):
            return f"`git push … {a}` (a leading + is a force push)"

    return None


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return 0  # not our event shape; do not interfere with unrelated tools

    if payload.get("tool_name") != "Bash":
        return 0

    command = (payload.get("tool_input") or {}).get("command") or ""
    if not command:
        return 0

    # Fast path: nothing to judge.
    if "gh" not in command and "git" not in command:
        return 0

    stripped, bodies = strip_heredocs(command)

    try:
        tokens = tokenise(stripped)
    except ValueError as exc:
        sys.stderr.write(
            "BLOCKED by .claude/hooks/guard-destructive.py: this command mentions gh or "
            f"git and could not be parsed ({exc}), so it is refused rather than guessed "
            "at. Split it into simpler calls and run them separately.\n"
        )
        return 2

    # A here-document handed to an INTERPRETER is a script, so judge it too. Handed to
    # anything else it is data — a commit message, a JSON payload — and the words in it
    # mean nothing. Getting this distinction wrong in either direction is a real cost:
    # judge everything and the guard refuses commit messages that merely DESCRIBE it;
    # judge nothing and `bash <<EOF ... gh repo delete ... EOF` walks straight through.
    # An owner this file could not read is treated as a shell — fail closed.
    for owner, body in bodies:
        if owner and owner not in INTERPRETERS:
            continue
        try:
            tokens.extend(tokenise(body))
        except ValueError as exc:
            sys.stderr.write(
                "BLOCKED by .claude/hooks/guard-destructive.py: this command feeds a "
                f"here-document to {owner or 'a shell'} and that script could not be "
                f"parsed ({exc}). Write it to a reviewed file instead of piping it in.\n"
            )
            return 2

    for prog, args in invocations(tokens):
        reason = judge_gh(args) if prog == "gh" else judge_git(args)
        if reason:
            sys.stderr.write(
                f"BLOCKED by .claude/hooks/guard-destructive.py: {reason} destroys "
                "something on GitHub and this project refuses it.\n"
                "\n"
                "This guard is deliberate and is not to be worked around — do not "
                "re-run the command through a script file, an alias or a different "
                "shell to get past it. If the repository owner genuinely wants this "
                "action, he performs it himself; say what you wanted to do and why, "
                "and let him decide.\n"
                "\n"
                "Read-only and additive GitHub work is unaffected: gh api with GET, "
                "POST, PUT or PATCH, gh release create, gh pr/issue create, and an "
                "ordinary git push all run normally.\n"
            )
            return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())
