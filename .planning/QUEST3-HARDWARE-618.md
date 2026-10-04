# Quest native Intro/menu recovery — ModBuild 618

Work stays on `feature/quest3-standalone`. The concurrent Steam Frame `dev`
checkout is independent. This candidate continues the original startup with the
real VR mod; campaign play and authenticated Android crossplay remain gated.

## Longer hardware evidence

The two captures `quest-capture-20261004T125121Z-530a1f5f.zip` and
`quest-capture-20261004T125610Z-cc148743.zip` independently record installed APK
SHA256 `14d421d118150969649a80ab47a3a0c902b4ddef412136a4dc1fb1bec687f9e7`,
the B616 startup banner and input `70d3fc52…`. The maintainer reports selecting
B617 and merging new archives into an existing Windows directory. Installer
source precedence can retain an older remembered APK; filenames alone do not
establish installed bytes. The second export is a fresh device query, not a
cached collector report. Both reports are still useful evidence for defects in
the native pipeline that B617 retained. No new screenshot accompanies them.

This longer run advances beyond the earlier preparation-only capture:

- Eleven real mod modules and its rig are ready after 27.048 seconds.
- Re-reading already installed original files takes 182.383 seconds.
- Catalog/alias setup and the diagnostic label preload take about 8.5 seconds.
- Original Bootstrap, Intro, Gloomhaven_unified and MainMenu are actually loaded.
- Intro's native VideoPlayer reports `Cannot read file`. Its trailer subsequently
  prepares and decodes frames from the same file-backed delivery directory.
- MainMenu uses an active UICamera redirected to the mod's scrub target, while
  its MainCamera object is inactive. The previous resolver excludes that camera;
  the mod subsequently observes `rig=False` while the Unity XR session is running.
- Three recovered Bloom shaders have only a dummy pass. The original component
  invokes missing pass indices, producing native rendering errors.
- XR device removal calls `UserManagementGeneric.RemovePlatformUser(null)`,
  producing an InputSystem exception.

MainMenu loads about 64 seconds after Gloomhaven_unified in this diagnostic.
Its native loading flags later become false; this does not justify bypassing
native loading/gameplay gates. The retained prior B615 abort is historical.
Paused/focus-lost state at collection end is not evidence of a new crash.
The retained Android logcat no longer covers this earlier 64-second interval.
Read-only source inspection finds checksum/YML worker/rule loading and persistence
gates, without a proven sixty-second service timer. B618 retains a bounded set of
the original startup milestone messages durably until MainMenu, so the next
capture can distinguish those phases even after ordinary logcat is overwritten.

## Implemented boundaries

Installed content now has an atomic, bounded local receipt keyed by required
file paths, sizes and hashes. Warm startup checks file identity metadata without
opening content bytes or the APK. Source-only mod changes, reordered manifests
and archive repacking preserve that cache. Missing/changed files are repaired;
older installations without a receipt receive one native byte check for adoption.
This private application cache is not an anti-piracy mechanism.

Preparation artwork appears for actual content installation/repair, using one
overall bar. A cached start has no additional preparation canvas. The original
logo and camera/input ownership are retained; completion follows observed native
Bootstrap handover. Individual file/verification/step rows are removed.

The native catalog and original key aliases are initialized without loading all
required assets a second time. The original AssetBundleManager owns that work.
The Quest camera resolver recognizes object-identical mod capture/scrub targets
and can use the native UICamera as its last reference anchor. Private preview
render textures and the owned XR head remain excluded; desktop behavior is kept.

The three Bloom placeholders are restored from the pinned official Unity
Standard Assets 5.3.5f1 package, preserving the owned game's GUIDs and metadata.
Their 11/2/5 pass counts, original material properties and all 18 pass states match
the recovered original recipes. The Python builder acquires/extracts the package
into a private cache without another executable/package dependency. Downloaded
shader sources are not committed to the public repository. Android compilation
is validated separately from that source-restoration proof.

The generic platform-user removal bridge returns only for a missing user. Its
complete non-null original method body, events and default-user behavior remain
identical. The adaptation checks the original method/list/InputUser interface
before emitting it; an incompatible future game input stops the build.

The Intro's extra QuickTime timecode track is removed by a deterministic,
lossless MP4 container adaptation. All 240 video and 375 audio packet payloads,
codec descriptions, timestamps and durations remain identical. Native callbacks,
the original eight-second sequence, audio and looping flags are retained. This
is a compatibility hypothesis; the recorded error does not prove timecode was
the sole decoder cause. The delivered SHA is recorded separately from the source.

Windows automatic selection compares build stamps inside local APKs, including
APKs left by merged archives. Explicit command-line source selection stays
available. Android versionCode and versionName contain build/input identity;
the installer confirms them from the device before launching or recording
success. The collector separately records package identity, actual APK hash,
local receipts and historical log banners.

## Next hardware procedure

1. Extract the complete new Windows archive into the existing test folder.
2. Run `Install-Quest.cmd`; confirm its selected and installed build are B618.
3. Keep existing application data. Observe preparation, native Intro/video,
   logo transitions, menu appearance, both controllers and menu selection.
4. Exit normally and launch again without reinstalling or clearing data. Verify
   no additional content-preparation bar appears. Record both launch times.
5. Run `Collect-Quest-Logs.cmd` after each run and supply the ZIPs plus screenshots
   of any remaining grey surface, double image or unresponsive native menu.

Managed/source checks and native build validation do not establish headset image
correctness. The full recovered player still uses the diagnostic native compiler
configuration; release startup performance remains a separate acceptance gate.

Validation receipts and final signed artifact identity are retained beside this
handoff outside the latest-only download directory.
