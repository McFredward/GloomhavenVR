# Quest menu sound, video, presentation and local DLCs — ModBuild 619

Work stays on `feature/quest3-standalone`. Steam Frame development on `dev`
remains independent. This is the next original-startup diagnostic with the real
VR mod; playable campaign export and authenticated Android crossplay are gated.

## Verified B618 evidence

`quest-capture-20261004T152959Z-3189d714.zip` records B618, input
`97e403505791…`, installed APK SHA256 `ee288e822a1b…`. Its local historical
B616 installation receipt is explicitly reported as a mismatch, rather than
substituted for the installed APK. The three supplied screenshots were inspected.
The maintainer confirms that the native menu loads and is usable, including MR.
Audio, Intro, spinner layers, EULA backing, DLC artwork and text sampling remain
reported defects. A paused/exited process at export end is not crash evidence.

The actual native menu player prepares, starts and decodes frames. However,
`BackgroundView.Update` changes its URL to an Ambient movie after the adapter
binds the trailer; the old diagnostic label therefore does not prove that the
trailer was displayed. Intro still reports `Cannot read file` for its `file://`
URL. Native menu movies do not require a loaded scenario.

## Conversion and presentation changes

- Use absolute native Android movie paths while retaining every existing media
  payload, original timer, callback and playback owner. Bounded Debug evidence
  records the actual player URL, decoder state, hierarchy and output target.
- Restore 793 quad PCM samples from the owned original bank. The recovered mono
  exports contain only a quarter of the interleaved source data. Fully decode
  two original quad IMA tracks; independent native FMOD comparisons match both
  complete PCM payloads. Repair the two zero WAV length words of eight stereo
  music exports while retaining every decoded sample. Preserve GUIDs, importers,
  channel layouts, sample rates, timelines, load types and native playback.
  A build-time import gate verifies the actual Unity AudioClip metadata. Bounded
  read-only Debug probes inspect the device, listeners and native sources.
- Restore the original loading sprites' complete drawing rectangles and crop
  padding. Match native Image drawing bounds and rotation pivots. Quest's two
  authored spinner layers occupy one stereo plane; their animation is retained.
  Retire the old cached geometry without removing either original layer.
- Add automatically refreshed mip sampling to existing Quest menu color captures.
  Keep eye/capture resolution, MSAA, physical geometry and desktop defaults.
  Local Unity rendering tests establish reduced minification noise, current
  animated mip contents and retained alpha; headset readability remains open.

The EULA's Splash Screen Shader was exported as constant white. The promotion's
UI/Dissolve mask also lost its original dissolve/alpha/stencil operations. Restore
their original compiled programs, properties and pass states, preserving materials,
textures and scene bindings. A GLES3 preflight checks all three keyword banks and
both stages before IL2CPP. Do not treat a source/import/pixel proof as confirmation
of headset appearance.

## Local DLC contract

On Windows, the builder reads the active Steam client's local DLC subscription
and installation through the PC game's original library in an isolated process.
It checks that the account matches the selected baked profile. This is PC-side
capture: the APK contains no live Steam account, store or cloud integration.
Purchased but unavailable PC content stops the build with an installation hint.
File presence alone never grants ownership, because the base installation also
ships unavailable DLC data.

The selected availability is included in the input key and baked into the
original `PlatformDLC.UserInstalledDLC` seam. Original CanPlayDLC, party/save
validation, promotion selection and other unrelated behavior remain native.
Unavailable DLC rule directories are omitted from the generated startup content;
advertisements and shared UI resources remain. This bounded export does not
establish complete playable scenario-DLC asset coverage.

The original purchase buttons are grey with a native hover tooltip saying to
buy on PC and rebuild the Quest APK. Missing-DLC save rows and purchase-mode DLC
selectors receive the same hint; native loading/owned selection returns when a
pooled row is rebound. Global store invocation is inert, including hotkeys.

The maintainer confirms Jaws of the Lion and Solo Scenarios ownership, but is
unsure whether the provided PC copy predates those purchases. This private
diagnostic uses those two declared DLCs, checks their actual source rule content,
and retains the visibly labelled dummy profile. Additional JoTL skins remain
unowned. Portable hosts can use a small account-bound ownership declaration;
the dummy diagnostic flags are not a live license/DRM claim.

## Hardware procedure

1. Extract the complete B619 Windows archive and run `Install-Quest.cmd`.
   Confirm selected and installed B619; retain application data and saves.
2. Check first and second launch, Intro frames/audio, transition to menu and its
   animated background. The second launch should reuse installed content.
3. Compare SFX pitch/distortion and music; check system-menu pause/resume.
4. Inspect spinner alignment with each eye, EULA backing where available, DLC
   artwork and purchase hover text. Check owned DLC selection without claiming
   that a campaign is playable in this diagnostic.
5. Compare stationary and moving menu text/window edges at ordinary distance.
   Test MR and normal view, and confirm native menu selection still works.
6. Run `Collect-Quest-Logs.cmd` after each run and supply both captures and images.

The owned original PCM boundary increases exported WAV storage substantially;
the signed APK size and device memory remain explicit validation observations,
without silently downmixing or altering audio semantics. Only the final verified
APK, Windows archive and current handoff belong in the latest download directory.

Final build identity, focused test receipts, native/shader/import proofs and
cleanup measurements are retained in the private B619 validation directory.
