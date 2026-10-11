# Steam Frame per-eye resolution suggestion

Valve documents an app-provided `vrpreferences.json` in the installed game's
root directory, containing `steam_frame.preferResolution` as the **absolute
width in pixels per eye**. SteamVR reads it at installation or startup, applies
it as the per-application default, and retains any explicit user override until
the user selects Reset to Default. It is not a percentage and does not require
writing the user's global `steamvr.vrsettings`.

The Frame setup creates this file with 3408, matching the tested SteamVR eye
target. If valid JSON already exists but lacks `steam_frame.preferResolution`,
it adds only that field, retains the other JSON values, and keeps an exact
`FrameSetup/vrpreferences.json.gloomhavenvr-backup` of the previous file. An existing resolution value is
never replaced. Invalid JSON or a symlink is left untouched and logged. If
this is the only setup change, the helper still requests the final Steam
restart so SteamVR can read the default. No PC installer or global SteamVR
preference is changed. SteamVR's override remains editable under **Video →
Per-Application Video Settings**.

The current VR shortcut forwards to Gloomhaven's original Steam AppID 780290
for Steamworks identity. Therefore SteamVR may associate this suggestion with
the original game's per-app settings, including a flat launch viewed in Game
Theater. The official file mechanism does not establish separate identities
for a forwarding shortcut. Headset verification must check `vrserver.txt` for
`Loading app-provided preferences from '<game>/vrpreferences.json'`, confirm
3408×3408 in the VR per-app settings and eye-target log, then verify that a
manually changed resolution survives a setup rerun. If a flat Game Theater
launch inherits the 3408 suggestion, the app-identity limitation needs a
separate launch architecture rather than editing global SteamVR settings.

Source: https://partner.steamgames.com/doc/steamhardware/steamframe/vrpreferences
