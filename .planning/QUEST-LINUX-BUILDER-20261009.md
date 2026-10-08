# Quest Builder Windows and Linux host support

The maintainer requested a Linux shell launcher alongside the existing Windows
CMD and a functional shared Builder on both operating systems. This is a
Builder-only delivery with ModBuild647. The confirmed Quest feature branch is
the delivery branch; worker worktrees started at current devf006ecf88 and then
received a private exact Quest085e037ce source snapshot. Those private baseline
commits are not integrated or pushed.

## Host implementation

- `Quest-Builder.sh` and `scripts/quest-builder-wizard.sh` launch the same browser
  server as Windows. Linux x86_64, paths with spaces, `--state-root`, `--port` and
  `--no-browser` are supported. Venv stays script-local. System Python requires
  Python3.11+, venv, ensurepip and TLS; a missing capability selects checksum-pinned
  Astral CPython3.13.16/20261003 instead. Fallback runtime uses
  `scripts/.quest-python-linux`; the durable workspace defaults to `~/.ghvrq`.
- Tools select native .NET8.0.425/10.0.401, Temurin17.0.16+8 and Android35 build
  tools. Linux qualifies an existing Git>=2.25 without claiming it is pinned.
  Safe owned TAR/ZIP extraction preserves required executable modes and internal
  links. A missing Git, desktop or native system dependency receives a concrete
  diagnostic; no automatic privileged system-package installation is performed.
- Unity Hub3.22.2 is a pinned official AppImage using its FUSE-free extraction
  mode. Linux uses native Hub CLI arguments and installs Editor/modules beneath
  `workspace/tools/unity-editors`. Both hosts retain repeatable sign-in/probe
  actions. Hub stderr is retained in session logs and included in bounded local
  log viewing/support exports; OAuth callback queries are redacted in exports.
  A missing Unity Hub URI handler is registered only in the user's XDG profile
  for the verified owned AppImage. Existing handlers and foreign desktop files
  are preserved; registration logs are exposed alongside Hub diagnostics.
- Discovery finds Linux Steam/Proton files, Hub editor registries, conventional
  Editor paths, configured `UNITY_EDITOR_PATH` and the owned Editor/Hub paths.
  Linux desktop browsing uses Zenity/KDialog, with manual fields when unavailable.
  UI path placeholders match the host. Steam DLCs require the existing explicit
  purchased-DLC declaration on Linux, before SDK setup or Unity import; base-game
  only is an explicit empty declaration. Profile-only updates do not require it.
- The real OpenGLCore compiler host uses a desktop X display, or Xvfb+xauth on a
  headless host. The installer automatically provisions official Linux ADB37.0.1
  and repairs lost executable permissions. Android native toolchains, managed
  codecs, recovery and Bee host-launcher already had Linux implementations.
- One source ZIP now requires both launcher families and carries Unix execute
  metadata. Existing Windows release manifests remain valid. Local Python/ADB
  output directories and lock files never count as shipped source modifications.

## Resume and validation

Added Linux pins are excluded from historical Windows stage keys. An exact
085e037ce golden tool-stage key still matches; moving a workspace to Linux
requires host-specific prerequisite receipts. Root launchers and launch
instructions are excluded only from the original preparation prefix: they are
delivery files, never asset producers. Full conversion helpers and original
game/profile/template identities remain qualified.

Evidence is private under
`/home/claw/quest3-local/build/evidence/linux-builder-20261009/`. Worker evidence
includes15 Linux bootstrap cases with actual pinned fallback extraction/reuse,
16 Linux tool and7 Unity setup cases, and153 focused pipeline/installer/media/
update/preparation cases. Actual .NET8/10, Java, keytool, aapt, apksigner, zipalign
and ADB binaries were executed. Actual ARM64 passthrough compilation and the
Linux Bee apphost/backend help succeeded. Root adds host/discovery/picker, DLC,
historical Windows identity, archive permission/privacy, local HTTP and browser
checks. Focused checks are not a new whole-mod/full-wire gate.
Actual isolated XDG registration, readback and repeat-call preservation succeeded;
GLib forwarded a dummy callback exactly through paths containing spaces, percent
signs, quotes and shell metacharacters. This is not an account sign-in claim.

The actual pinned Hub launched and unpacked without FUSE, but its bounded CLI
help attempt encountered external Unity feature-flag TLS failures and stopped
after60 seconds. That does not establish GUI sign-in or a fresh Editor install.
The inherited unmodified Box64 native-host fixture remains stale against the
current Proton default; its failure is recorded, not attributed to Linux edits.
No full APK/import, broad shader audit, Windows end-to-end build or new headset
outcome is claimed. Source delivery keeps those acceptance boundaries explicit.
