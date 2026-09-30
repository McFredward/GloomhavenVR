# Steam Frame launch presentation — 2026-09-30 hardware review

## Observed state

The owner confirmed that the generated `GloomhavenVR` shortcut and its five
Library images now appear on Steam Frame. The next Frame capture,
`debug/kein_logo_beim_spiel.jpg`, shows the SteamVR dashboard still open over
the rendered scenario. The active-game panel says **Gloomhaven** and displays a
gray placeholder where its cover should be. The owner must press **Resume
Game** at each startup.

This is not evidence that OpenXR failed: the game is rendered behind the
dashboard. The captured SteamVR/OpenXR diagnostic identifies a successful
runtime. The screenshot and the launched process identity are consistent with
the current launcher:

```text
GloomhavenVR shortcut (own non-Steam AppID, own Library art)
  -> launch-steam-frame.sh
  -> steam -applaunch 780290 --gloomhavenvr
  -> Steam game AppID 780290 (Gloomhaven, flat Steamworks launch type)
  -> mod starts OpenXR after BepInEx loads
```

The five custom grid images currently live under the shortcut's own AppID in
`userdata/<account>/config/grid/`. They are correctly installed for the new
Library entry. The active SteamVR panel is evidently associated with the
original game launch, so installing further shortcut-ID grid images cannot
repair that gray panel. Steam's original game also has separate Library art;
replacing `780290` grid overrides would conflate the flat and VR entries.

## Platform boundary

Steamworks controls whether an AppID has a VR launch option. Valve's current
[VR application-settings documentation](https://partner.steamgames.com/doc/features/steamvr/settings)
states that a VR launch option must be defined in the publisher's Steamworks
settings to enable native Steam VR launch handling. Gloomhaven's shipped
Steamworks configuration has flat launch options. A local non-Steam shortcut
marked `OpenVR=1` puts it in the VR Library but cannot redefine AppID 780290's
publisher-controlled launch type. The current shortcut forwards to 780290 to
preserve Steamworks identity, achievements, Cloud saves and multiplayer.

The current SteamVR dashboard can auto-open a desktop-game/theater view for
flat-classified launches even when an OpenXR scene later starts. A report of
the same behavior for a [non-Steam OpenXR shortcut](https://steamcommunity.com/app/250820/discussions/3/798965318967038820/)
found that `dashboard.autoShowGameTheater=false` suppressed the automatic
opening on one system. That is useful as a **manual A/B test**, not proof for
Steam Frame. It is a global SteamVR preference for all desktop games, and a
Valve developer notes that the modern theater is normally meant to disappear
once a game submits VR frames
([developer response](https://steamcommunity.com/app/250820/discussions/0/596272860832518646/)).
The former per-game desktop-theater toggle was removed with SteamVR 2.1
([Valve response](https://steamcommunity.com/app/250820/discussions/0/4035852333636940598/)).
The installer must not silently disable theater globally or leave a temporary
override behind if the headset shell restarts.

## Paths worth evaluating

1. **Publisher VR launch option** is the complete solution to native SteamVR
   identity and startup classification, but requires the Gloomhaven publisher's
   Steamworks access. The mod cannot implement this locally.
2. **Frame-only OpenVR application manifest and process identification** may
   give the running VR scene its own application key and image without changing
   the original Library art. OpenVR provides an application manifest
   `image_path` and `IVRApplications::IdentifyApplication`, but the mod uses
   Unity OpenXR, and the current running process is launched and owned by Steam
   as `780290`. An OpenVR manifest plus process re-identification is an
   experimental integration, not an established shortcut setting. It requires
   a small native/managed bridge and an on-device test of dashboard title,
   image, VR input and Steamworks identity before release.
3. **Launch the executable under the non-Steam shortcut AppID directly** would
   likely let SteamVR use the new entry's identity and art. It would no longer
   be the original Steam launch, though; forwarding `SteamAppId=780290` and
   reusing `STEAM_COMPAT_DATA_PATH` are not demonstrated substitutes for its
   Steamworks session. This could affect achievements, Cloud saves, friends,
   multiplayer and updates. Do not switch to this path merely for the image.
4. **Original-AppID grid override** could fill the active gray cover if the
   dashboard reads Steam's custom grid there. It also changes the flat entry's
   image and does not fix the recurring dashboard focus, so it is excluded.

## Next Frame diagnostic

Keep the current launcher and record, immediately after a `GloomhavenVR`
start, the SteamVR dashboard setting `dashboard.autoShowGameTheater` from
`~/.local/share/Steam/config/steamvr.vrsettings` and the launch/overlay lines
from SteamVR's `vrserver.txt`, `vrcompositor.txt` and dashboard log. The
`steamapps.vrmanifest` entry for `780290`, if present, would reveal the
runtime's registered name, image and launch type. The decisive A/B is one
launch with **Present Non-VR Applications on Theater Screen Upon Launch**
disabled in SteamVR settings (then restore the user's preference), using the
same shortcut and build. If that removes Resume Game while the game enters VR,
the cause is theater auto-show; it still leaves the active-app artwork/identity
question separate. The image fix needs either a safe OpenVR identity experiment
or publisher VR metadata.

This review does not claim a headset-confirmed fix. No SteamVR configuration or
original AppID artwork is modified by it.
