# Steam Link mixed-reality development

The maintainer requested removal of the synthetic test pattern and its VR
setting on 2026-10-17. The shipped mod no longer binds
`SteamLink/PayloadTestEnabled`, creates the test driver, replaces either eye
with a synthetic canvas, or embeds the pattern textures/manifests. The ordinary
menu and VR-options lifecycle has no diagnostic-output stop hook.

An older `dev.gloomhavenvr.steamlink.cfg` is left untouched on disk and is no
longer opened by the mod. No user configuration file is deleted or migrated.
This explicit removal supersedes the usual rule to retain persisted config keys.

The completed synthetic streaming experiment and its original source history
remain useful development evidence. Historical scripts and controls were removed
with their production source; their prior results remain in Git history and
private captures. Their retirement is not a replacement passing test result.

Further Steam Link work now targets the real game's same-eye contribution and
coverage. The [real-game coverage audit](research/steamlink-game-coverage-audit-20261017.md)
records that scope and its remaining native-material and GPU requirements. The
separate receiver fork retains its offline experiments; this removal installs
nothing on the Frame and does not modify the Steam sender.

Existing standalone Steam Frame passthrough, ordinary mixed-reality options,
menu focus/availability, gameplay and multiplayer protocols are unchanged.
