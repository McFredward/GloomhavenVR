# Working on GloomhavenVR

Read `CLAUDE.md`, `.planning/STATE.md`, and the newest build notes beside
`NetProtocol.ModBuild` before implementation. `CLAUDE.md` retains the project's
technical contracts and historical reasoning; the rules below adapt its workflow
to Codex and record the user's instructions of 2026-09-08.

- The primary agent is the user's contact and integrator. Delegate independent
  implementation tasks to workers with separate Git worktrees and explicit,
  disjoint file ownership. Create each worktree from the current `dev` integration
  commit, never from the older release branch. Respect the available concurrency
  limit; do not assume a tool creates an isolated worktree automatically.
- Review worker changes, integrate them into `dev`, run the complete required
  checks, and push directly to `origin/dev`. This is authorized by the user;
  another confirmation is unnecessary. Never push worker branches or force-push.
- Speak to the user in German. Write code, comments, and developer documentation
  in English. Product strings remain English and German through `Core/Loc`.
- Hardware evidence lives in the main checkout's gitignored `.planning/debug/`;
  the other player's logs are in `.planning/debug/remote/`. Verify both build
  banners and inspect supplied screenshots before assigning a cause.
- Initialize worker dependencies with `scripts/worktree-setup.sh`. Keep any new
  refactor baseline private to its worktree; never overwrite a shared symlink.
- Never use `git stash`. Preserve unrelated work and the read-only game references.
  Do not print `.env` or copy its contents into tracked files.
- Commit useful checkpoints. Attribute commits truthfully: the Claude-specific
  coauthor and session-link template in `CLAUDE.md` does not apply to Codex.
- Distinguish source-proven fixes, log evidence, and unverified hardware outcomes.
  A green automated check does not establish that a headset picture is correct.
