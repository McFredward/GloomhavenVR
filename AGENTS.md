# Working on GloomhavenVR

## Unified development workflow (2026-09-29)

**Quest scope, corrected by the maintainer on2026-10-06:** include Guildmaster
again. Its earlier exclusion depended on removing the procedural engine; the
current bridge instead preserves original dynamic generation. Retain native
Guildmaster menu availability, saves, loading and multiplayer admission. Remove
Quest-specific unavailable hints/guards from the complete game target. Steam
Workshop and store/cloud services remain excluded. Engine execution and its
combined mobile CPU/memory/frame cost remain unverified hardware gates.

**Quest exception, clarified by the maintainer on 2026-10-03:** keep Quest
implementation and hardware installer work on `feature/quest3-standalone`, with
separate worker worktrees from its current checkpoint. The primary agent reviews,
runs the complete checks and publishes this feature branch for Windows hardware
testing. Do not merge or push these Quest changes into `dev`, where other agents
continue Steam Frame work. This explicit instruction supersedes the generic
integration-branch rules below for the Quest task; all other contracts still apply.

**Quest hardware handoff, clarified on 2026-10-04:** after verifying each new
hardware package, keep the main ignored `.planning/debug/quest3/` output directory
limited to the latest APK, Windows archive and current handoff receipt. Remove
superseded APKs/archives and move historical validation outside that handoff
directory. Preserve supplied captures in `quest3_probleme/`. The initial loading
view must use the existing GloomhavenVR logo, a real progress bar and percentage;
keep the presentation reusable for later in-game loading.

**Quest startup, clarified on 2026-10-04 after the longer menu capture:**
prefer the original Intro-to-menu flow without an extra preparation display.
Reuse successfully installed content across launches and source-only mod updates;
do not run full-file integrity scans on every launch. If actual installation or
repair needs preparation, show one overall progress bar before native startup,
without individual verification/file/step bars. Complete it only at observed
native handover. This supersedes the earlier five-step progress presentation.
The original native loaders retain their asset-load ownership.

**Quest adaptation compatibility, clarified by the maintainer on 2026-10-04:**
standalone-only behavior must not change the ordinary PC mod. Gate adaptations
through `QuestStandalonePlatform.Enabled` or a centrally owned capability, use
small seams in the shared current components, and retain desktop behavior tests.
Do not fork menus/keyboards, pin an old mod binary or maintain a Quest option
allowlist. The builder must include selected current source/art automatically;
report genuine original/package ABI drift visibly rather than guessing bindings.
Future settings remain available unless a specific Quest-only exclusion applies.

**Quest cache hygiene, requested by the maintainer on 2026-10-04:** after a
verified hardware handoff, archive compact diagnostic receipts and prune obsolete
Quest worker checkouts, generated test copies and historical Unity/build caches.
Keep Git branch refs, active builds/worktrees, latest matching native symbols,
canonical owned inputs, supplied captures and potentially unique dirty source
snapshots. Check active process references and incoming symlinks before removal;
never follow shared links into other agents' work or the read-only references.
Record measured reclaimed space rather than summing potentially shared extents.

The maintainer requested integration of `feature/immersive-town-services` into
`dev`. All subsequent NPC, Steam Frame, and other development now integrates and
pushes to `dev`; create worker worktrees from the current `dev` commit. The NPC
feature still targets a release of at least 1.1.0, but it no longer has a separate
active integration branch. The former NPC branch remains historical ancestry.

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
- During implementation, workers run the relevant focused suites and source checks
  for their owned changes. For substantial integrations, the primary agent runs
  the complete local gate, including `scripts/wire-tests.sh` and its golden vectors.
  The maintainer's explicit 2026-10-06 clarification permits focused subsets for
  small, bounded changes. After a limited repair, rerun the affected checks and
  reuse already passing evidence for unchanged areas rather than repeating the
  entire suite. Record the tested scope and inherited evidence honestly: a focused
  `--suite` pass is not a new complete-gate pass.
- Speak to the user in German. Write code, comments, and developer documentation
  in English. Product strings remain English and German through `Core/Loc`.
- Hardware evidence lives in the main checkout's gitignored `.planning/debug/`;
  the other player's logs are in `.planning/debug/remote/`. Verify both build
  banners and inspect supplied screenshots before assigning a cause.
- Initialize worker dependencies with `scripts/worktree-setup.sh`. Keep any new
  refactor baseline private to its worktree; never overwrite a shared symlink.
- Never use `git stash`. Preserve unrelated work and the read-only game references.
  Do not print `.env` or copy its contents into tracked files.
- Keep player documentation and release highlights focused on installation from release archives,
  controls and visible changes. Installer scripts, CI/build details and pending hardware tests
  belong in developer documentation or `.planning/`, not in player instructions.
- The maintainer always tests at Debug. Ordinary players use the normal log level, which
  must remain useful for bug reports: retain build/version, important lifecycle and flow
  context, failures, and bounded reports of significant anomalies. Deduplicate or rate-limit
  recurring reports; normal logging must be informative without growing with every frame
  or routine action. Detailed measurements and frequent state traces belong at Debug, with
  bounded repetition and guards before expensive sampling or string formatting. Do not
  promote diagnostic streams merely to simplify the maintainer's hardware tests, or hide all
  useful failure context at Debug (user clarification, 2026-09-18).
- Commit useful checkpoints. Attribute commits truthfully: the Claude-specific
  coauthor and session-link template in `CLAUDE.md` does not apply to Codex.
- Distinguish source-proven fixes, log evidence, and unverified hardware outcomes.
  A green automated check does not establish that a headset picture is correct.

## Multiplayer visual parity

The user's 2026-10-03 NPC review explicitly makes town **card pre-drop guides**
visitor-local: do not synchronize the merchant/enchantress "place card here"
guide. The user's 2026-10-04 paired-log review extends this exception to the
temple purse pre-drop ghost: display each visitor's guide locally, never a second
observer copy. Actual wrist-preview/held/donated purses, held/offered cards,
resident offered-hand poses, options, confirmations, return flights and audio
remain shared; scenario board guides keep their existing synchronization.

The user's 2026-09-09 review instruction covers everything the owner sees: original widgets,
content, appearance, order, geometry, state, effects and intermediate animation. Only an
explicitly user-confirmed exception permits a divergence. Historical deferrals, performance
arguments and implementation comments are not approvals. Record the source of each actual
exception; fix newly discovered divergences within the authorized review. Native prefab clones
must retain original presentation without running gameplay controllers or callbacks.

The latest multiplayer test ruling (2026-09-09, ModBuild 486 evidence) supersedes older
pile/active-card face exceptions: during the action phase cards are face-up; during
ability selection, remote fans, held cards and placed cards are face-down. Remote short-rest
burn flights must remain face-down. Damage-sacrifice choices during the action phase
remain face-up. Card-face visibility and permission to name private cards remain separate.

The user clarified during the build 490 review (2026-09-09): concealment is exclusively a
remote presentation rule. Local cards of controlled characters are never concealed, including
short-rest burn flights and fallback artwork. During selection, local character switching is
already limited to controlled characters; do not add remote secrecy gates to local rendering.

The build 490 hardware report (2026-09-09) explicitly confirms that the entire 3D map
environment is public: neither local nor remote fans, held cards or other map card surfaces
may be concealed. Artwork construction failures are rendering defects, not privacy decisions.

The build 500 hardware ruling (2026-09-14) requires immediate return of a held figure to
its board position before any non-idle action starts, locally and for remote observers.
This supersedes the older forced-release glide ruling; ordinary idle manual releases keep
their usual glide. Native movement must never derive its start from a hand-held transform.

The user's 2026-09-17 window-placement request permits necessary, animated opening-time
room making when a new window would overlap an existing visible window. Keep affected
windows in view and use one author for shared movement. This narrowly supersedes the
historical fixed-window rule; it does not authorize periodic head-following or lost-window
recall. Manual grabs take precedence, and layout must never gate native continuation.
The build-526 hardware clarification fixes the newly opened window at its spawn pose: only
older, visibly overlapping windows may move. Transparent host/hit areas are not visual overlap;
a free quest-popup gaze centre takes precedence over the legacy beside-quest-log preference.

The user's 2026-09-17 tutorial clarification restores the original task-specific hands/controller
choice and animated transitions. Card handling and physical fingertip tasks show hands; key
lessons show controllers. Both tracked sides must remain represented by either a hand or a
controller. Build 518's interpretation that controller models must stay visible for the whole
lesson was incorrect and is superseded.

The user's build-529 Guildmaster clarification (2026-09-18) keeps the original quest list
visible during map browsing, including the mode's persistent ordinary dialog. Only actual
quest commitment and its subsequent story/loadout may hide it. A dialog asking to hide
other native UI is not, by itself, proof of Guildmaster quest acceptance.

The user's build-531 hardware clarification (2026-09-18, `wasser_hintergrund.jpg`)
restricts mixed-reality backings to UI elements. Never add MR background geometry inside
the play area, including water, fog/unseen terrain and preview meshes. Earlier scenery
backing implementations and comments do not authorize exceptions to this rule.

The user's build-536 options-menu clarification (2026-09-20) requires VR Options to
leave other menu entries' focus and availability untouched. A grey native button must
represent genuine native disablement, never opening the independent VR window. The VR
entry is a toggle synchronized with window closure, including X; the next press must
immediately reopen it, without an extra deselect click or a grace period.
