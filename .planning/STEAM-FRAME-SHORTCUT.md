# Steam Frame: separate local VR entry (Build 588 candidate)

The Frame installation needs two launch identities over one installed Gloomhaven
copy. The original Steam entry should remain flat; a local library shortcut named
`GloomhavenVR` should opt into VR and display the mod's existing logo. Windows PC
installation and launch behavior must remain unchanged.

Valve's Steamworks VR launch option belongs to the original game's publisher; a
mod cannot change its server-side app configuration. Steam can add a local
non-Steam shortcut, but that does not rewrite the original app's VR metadata:
https://partner.steamgames.com/doc/features/steamvr/settings and
https://help.steampowered.com/en/faqs/view/4B8B-9697-2338-40EC.

## Chosen launch path

`install-steam-frame.sh` runs from the extracted release in the game directory.
It checks for `GH.exe`, the preloader and the two existing mod artwork files;
copies the art and a small launcher into the user's data directory; and creates
`BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker`. The player adds the
launcher to Steam through its normal UI, names it `GloomhavenVR`, includes it in
the VR library and selects the copied logo/icon. The helper never edits Steam's
VDF files or the original game's Steamworks entry.

The launcher invokes `steam -applaunch 780290 --gloomhavenvr`. Forwarding to the
original app ID is deliberate: launching `GH.exe` directly as a new non-Steam
Proton game could create a separate prefix and jeopardize saves, Cloud state or
Steamworks multiplayer. The original app's Steam launch option retains
`WINEDLLOVERRIDES="winhttp=n,b" %command%` so BepInEx loads under Proton.

`FrameLaunchOptIn` is one source file compiled into both the preloader and
plugin. With no marker it returns true, preserving historical PC behavior.
With the marker it accepts only an exact `--gloomhavenvr` argv token. The
preloader checks before XR native installation or graphics-job changes; the
plugin checks before config binding, OpenXR startup or Harmony registration.
A marked flat launch may still load BepInEx because the original launch option
remains, and XR native files installed by an earlier VR run remain in the game
directory, but the mod does not start an XR session or present VR content.
The preloader suppresses its usual Steam URI auto-restart on a marked Frame
install: that URI would reopen the original flat entry without the opt-in flag.
The player instead launches the VR shortcut again after the one-time graphics
change. The release archive must never contain the marker; only the Frame
helper creates it. The Windows packager deletes a marker accidentally staged
from a local game tree.

## Hardware gates

These are unverified on the actual Frame and must not be presented as shipped
guarantees:

1. Steam Frame Game Mode must pass the additional `--gloomhavenvr` argument from
   the local shell shortcut through `steam -applaunch` to `GH.exe`. If it does
   not, the opt-in rejects VR and the shortcut opens flat. Do not replace this
   with a persistent or timed on-disk launch token: an original-entry launch
   could consume that token and accidentally enter VR.
2. The local shortcut should appear under the player's VR library with the
   selected art. Whether SteamVR exposes *original AppID 780290* per-app
   resolution controls before that forwarded process runs is separate; the
   local alias cannot change Steamworks metadata. If those settings remain
   absent, they can still be set while the original app is running in VR.
3. Launch original `Gloomhaven` after the marker is created and confirm flat
   presentation, then launch `GloomhavenVR` and confirm the ModBuild banner and
   VR table. Repeat the VR launch once if the first graphics-job boot closes.
   Check that both entries share campaign saves and multiplayer identity.

Source-level validation: `scripts/test-install-steam-frame.sh` covers helper
dry run, idempotence, paths with spaces, missing preloader, artwork and exact
launcher argv. Wire vectors pin the shared gate's marker and argument. A strict
Release build, release-package layout, 14 source checks, all 80 runtime suites,
286609 wire assertions and EN/DE documentation check passed on the integrated
tree. The compiled-form difference against Build 587 consists of the opt-in
gate and expected build constants. None of these checks proves Steam Game Mode
argument forwarding or headset library artwork.

## Setup archive follow-up (2026-09-29)

The maintainer's first direct `./install-steam-frame.sh` attempt on Frame failed
before any installer code ran: `/usr/bin/env: 'bash\r': No such file or directory`.
The tracked Linux source had LF, so a Windows checkout or packaging path had
changed the shebang to CRLF. `.gitattributes` now fixes shell/desktop files to
LF. Both archive builders normalize these two launcher files explicitly. The
Linux packager and Windows packager also encode Unix 0755 ZIP permissions;
`scripts/check-frame-launchers.py` rejects an archive that loses either the
line endings or the executable attributes. `GloomhavenVR-Setup.desktop` runs
the existing helper from its own directory through Dolphin and pauses its
terminal on exit. The shell fallback remains `bash ./install-steam-frame.sh`.
The desktop entry's relative path was exercised with spaces in the folder name;
Frame's own Dolphin trust prompt and Steam shortcut behavior still need a
hardware check.

The Build 591 singleplayer trace did not establish a new functional failure:
all 85 selected-character censuses were valid and the +100 gold cheat applied
twice. The enchantress Visit hitch remains measurable (up to 152 ms in this
run); no claim of smoothness follows from the absence of a noticed glitch.

The first Windows packaging attempt after this change failed in
`Set-ZipUnixLaunchers`: Windows PowerShell 5.1 evaluated `33261 -shl 16` as a
signed Int32 before the `UInt32` cast and rejected its negative result. The
archive helper now writes the already-reviewed unsigned value `2179792896`
(`0x81ED0000`), and a PowerShell-authored synthetic ZIP passes the same LF and
Unix-mode checker as the Linux release ZIP in CI. The first CI desktop smoke
also exposed a host-dependent `%k`/shell parameter-expansion path; the launcher
now derives its directory with `dirname` and the smoke covers a space-containing
path. Neither fix changes a game DLL or the ModBuild handshake.
