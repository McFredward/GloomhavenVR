# Automatic original Gloomhaven launch options on Steam Frame

User request: October 19, 2026. Installer-only repair on current `dev` after
Build 714; no game DLL, ModBuild, protocol, config default or native patch changes.

## Report and causal history

The supplied October 19, 21:25:38 Frame setup log selects account 128150452 and
records the existing VR shortcut as current, with the original game's launch
options missing or stale. The older installer writes one file while Steam is
running, reads that file back as current, then requests a restart. Its last Steam
message says the command was forwarded to the already-running client. The user
finds the original Gloomhaven launch-options field empty afterward. This is
hardware evidence of failed persistence, not proof that disk readback established
the live client's configuration. No mod build banner or new headset picture was
supplied with this setup-only report.

Earlier attempts are retained in `STEAM-FRAME-SHORTCUT.md` and `FIX-672-REVIEW.md`:

- A detached shutdown worker could disappear when the entire Frame UI session
  closed, before reaching its configuration writer. A separate process/session
  was insufficient to own continuation.
- Writing before shutdown survived that terminal loss but let Steam's cached
  old `localconfig.vdf` overwrite the externally repaired file. Readback before
  shutdown cannot detect the subsequent overwrite.
- Build 672 avoided the race with a native Steam Properties save and restart.
  That manual completion no longer meets the user's explicit automatic setup
  requirement. It is superseded by this repair.

An existing October 19 hardware capture proves active user `steam.service` and
`steamvr.service`, persistent Steam service drop-ins and actual stop/start/retry
lifecycle. It does not establish all current vendor unit settings. The installer
therefore inspects the actual current unit, main PID, ownership, cgroups, kill
policy and startup budget before selecting the managed path. Missing or unknown
inspection is an error, never an assumption that Steam is stopped.

Valve's shipped Steam UI also exposes a native launch-options setter/getter.
That route requires an established privileged client context; none is proven on
this Frame. The repair does not enable remote debugging or ship an unverified
console/API transport.

## Implemented transaction

Offline setup retains the automatic backed-up atomic writer. Live setup never
writes the original launch-options VDF or shortcuts while their cache owner is
running. It prepares the local art, opt-in marker, launcher, graphics-job keys
and existing-preserving Frame resolution suggestion first.

The installer places an immutable, SHA256-named configuration helper and a
bounded schema 2 job in the user's persistent `GloomhavenVR/SteamSetup/` data
directory. The request pins the generation, selected Steam account/root,
AppID 780290, helper hash, original Steam PID/start ticks/boot identity and input
paths. It contains no stale whole-file VDF image. The original game remains flat;
only the separate `GloomhavenVR` launcher supplies `--gloomhavenvr`.

An owned `90-gloomhavenvr-setup.conf` appends one `ExecStartPre` to the actual
user `steam.service`. It does not replace vendor commands, existing drop-ins or
service lifecycle settings. The command uses systemd argument escaping for
spaces, quotes, backslashes, dollar signs and percent specifiers. Its nonfatal
`-` prefix and independent 15-second timeout with 2-second kill grace keep missing
game/helper/artwork or a failed repair from gating Steam's ordinary library.
The installer admits a startup budget of at least 30 seconds or infinity, without
changing that budget.

After manager reload, setup requests `systemctl --user --no-block restart
steam.service`; it does not send another Steam CLI that might merely forward to
the old cache owner. The service manager stops the old group, runs the persisted
hook against a verified empty Steam process census, then starts the replacement.
The hook re-reads the latest shutdown-flushed VDF, merges only the required
winhttp override and single `%command%`, repairs the owned shortcut/artwork,
checks readback and atomically records the exact applied generation. Custom
options and unrelated app/account/shortcut fields remain intact.

The foreground process distinguishes queued from verified completion. A CLI
exit 0 or a current file alone cannot complete setup. Its verification requires
the generation returned by its own queue command, a matching applied receipt,
current account, valid options and a fresh Steam process lifetime. The generated
VR launcher also rejects pending, failed, corrupt, old-account or stale setup
instead of silently opening flat. Recovery guidance reruns automatic setup.

Request locking prevents an old completion overwriting a newer request. Failed
jobs can retry; completed jobs never force old values over later deliberate user
edits. Running setup again creates a fresh repair generation. The persistent
hook is a no-op when successful offline setup has removed its request. Bounded
regular-file reads reject FIFOs and oversized configuration inputs.

## Validation and limits

The final integration command `bash scripts/test-install-steam-frame.sh` passes
the existing desktop/offline/artwork/preferences checks and all 24 Python tests:
12 inherited cases, 5 managed request cases and 7 independent real-process
cache/service cases. It exercises the actual persisted production hook, latest
custom cache edits at shutdown, terminal loss, failed repair and retry, exact
generation/account checks, literal paths and unsafe service rejection. Existing
service drop-ins remain byte-identical. All 7 manager fixtures bind the exact
final installer/helper/test hashes and use actual PID/UID/lifetime/cgroup reads;
no private cgroup adapter was needed on this host. Root-cgroup CI containers use
an explicitly recorded private read-boundary adapter, while root-group rejection
always executes the unadapted production source.

The archived pre-fix installer fails the same functional assertion because its
new client's cache still lacks the required original AppID 780290 options. A
second control reproduces successful disk readback followed by stale cached
flush, despite a forwarded CLI returning 0. These are causal controls, not log
string comparisons. The shell CI entry now invokes the Python helper suite, so
these transaction cases run through the existing hosted smoke-test seam.

Final shell syntax, bilingual documentation, whitespace and a ZIP containing
the current source helpers/artwork all pass. PowerShell is unavailable locally;
the unchanged PowerShell packager's hosted check is not claimed as a new local
pass. No C# runtime or broad Unity/wire suites were rerun for this installer-only
change. Their unchanged Build 714 evidence remains separate.

Evidence: `.planning/debug/frame-options-auto/integration.json`,
`final-installer.log`, static/archive `run-34rwaq7g/`, and shell fixture
`.planning/debug/frame672-installer/run-69mmhzqv/`. The worker evidence is archived
with hash-verified regular files and preserved FIFO/symlink metadata under
`frame-options-auto/worker-evidence/`. Canonical completion cleanup follows
process reaping; current logs, source manifests, hardware and results remain.

No Frame configuration was changed or service restarted during implementation.
Source and simulation evidence do not establish the next headset's actual Steam
Properties display or VR launch. A hardware run of the updated archive remains
the final device acceptance check.

Diagnostics are the bounded game-side `FrameSetup/steam-frame-setup.log` and its
one previous log, persistent `SteamSetup/managed-setup.json` and
`SteamSetup/verification.log`, and `journalctl --user -u steam.service -b` for a
terminal closed during restart. Keep these with the next report.

The startup ordering, ignored-failure prefix and drop-in semantics follow the
[systemd.service manual](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html),
[systemd.exec argument rules](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html#Command%20lines),
and [systemd.unit drop-in rules](https://www.freedesktop.org/software/systemd/man/latest/systemd.unit.html).
These platform contracts do not replace a Frame hardware result.
