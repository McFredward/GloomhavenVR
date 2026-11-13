# Installed Quest sources and automatic updates

Date: 2026-10-21. Runtime remains 1.2.0/B717. This is Builder functionality;
the preceding B717 loading/FX/performance hardware candidate is unchanged.

## Maintainer decision and actual dependency

The maintainer explicitly permits an existing Quest installation to suffice
without the formerly mandatory PC game-copy check. This applies to an explicit
installed-game update source. Ordinary new builds and PC-assisted updates retain
their existing owned-PC input validation. It is a convenience/ownership hurdle,
not an open-source DRM or store-ownership proof.

An installed IL2CPP APK does not contain the original Mono method bodies.
The existing update capsule proves serialized script layouts, package ABI and
input identities; hashes and type metadata cannot reconstruct that missing IL.
The current static weaver and native compiler still need the exact original
game assemblies. The current 169 DLL inputs total 47,283,392 bytes; the deterministic
portable seed is 18,389,781 compressed bytes. See
[seed qualification](QUEST-UPDATE-SEED-20261021.md).

New builds and one PC-assisted compact/profile update embed this seed in the
personal APK, bound to its immutable original input inventory. No original DLL,
generated seed, save or key is shipped inside the game-free Builder archive.
Subsequent code updates can recover those references from the installed APK.
Older APKs require that one-time preparation; this is a compact APK update,
not permission to repeat the full original game conversion. A compatible
profile-only edit itself needs no compiler seed or Unity.

## Source selection and minimal work

The Wizard offers the current workspace/local APK or portable ZIP and the
connected authorized Quest. Entering Update checks cached ADB availability and
the exact installed package's metadata without downloading tools, pulling its
APK, traversing game banks or waiting on absent devices. USB is preferred and
existing verified wireless connection behavior is reused. Import is explicit,
asynchronous, cancellable and reports completed byte counts.

Only Package Manager's exact base.apk is read, in bounded resumable chunks.
Hardware/package generations are checked again before publishing an import.
External Campaign/movie banks and saves remain on the headset. The imported
APK, bounded identity and reference seed feed the existing compact update path;
the embedded profile and included DLC roster remain authoritative, without
Steam/Epic/GOG launcher requests. See
[device protocol](QUEST-INSTALLED-SOURCE-20261021.md) and Wizard lane review.

An explicit `--installed-game` update never treats the reference-only seed as a
complete PC installation, creates fake game files, or falls back into full asset
conversion. Exact existing card pixel/outline sidecars are retained. A changed
original-derived art/figure/audio/movie producer or incompatible serialized
layout requests the matching PC source before starting that work. Unknown
compiler changes cannot be hidden by the finite reviewed host-file aliases.

Current code-only updates still compile the matched ARM64 native library and
metadata; the existing serialized-layout/native-pair proof remains mandatory.
This functionality removes persistent PC game/build inputs, not all temporary
compiler, tool or APK output space. Keeping the optional compiler cache speeds
future updates; deleting it causes compilation again, not original asset export.
The ordinary reserve/progress/resume rules still apply.

Keep the small private signing key separately. The installed APK contains its
certificate, not the private key. Android requires matching package/certificate
and a compatible versionCode for an ordinary in-place update. Losing the key
requires the existing explicitly chosen signing-migration/reinstallation path;
neither a seed nor possession of an APK can recreate it. Do not put signing
secrets inside the seed or APK to claim that nothing needs to be kept.

Owned imported APKs are bounded per headset: preserve its latest closed import
and all pending selected sources, then remove older proven imports during the
existing update-history cleanup. Partial transfers, pins, unknown owners and
borrowed files remain protected. The reference caches are classified as reusable
inputs; deleting them warns about the extra preparation next time. Metadata
cleanup never rehashes APKs or traverses the external game bank.

## Existing PC updater

The current `Plugin` explicitly omits `SelfUpdateModule` when
`QuestStandalonePlatform.Enabled`. This is appropriate: `SelfUpdateInstaller`
downloads the PC mod archive, writes the PC apply script, replaces the BepInEx
files and restarts the desktop game. Copying that DLL/archive into an IL2CPP
Android installation neither changes the native executable nor updates the
matching metadata. It must remain disabled while this architecture is used.

Unity's IL2CPP description explicitly identifies ahead-of-time conversion of
managed IL to native C++/binary:
https://docs.unity3d.com/2021.3/Documentation/Manual/IL2CPP.html

## Realistic automatic-update options

| Capability | Required work | Assessment |
| --- | --- | --- |
| Notify in the Quest menu | A small Quest-specific release manifest/channel, current ModBuild/package ABI comparison, bounded HTTPS request/cache and a normal user-dismissable notice. Keep PC/Quest artifacts separate. | Straightforward future feature; no compiler or Horizon app registration needed merely to check a public manifest. Not implemented here. |
| Guided automatic update with the PC Builder | A published game-free Builder update channel, changed-source acquisition, an optional PC companion waiting for the user's action, selected headset/source identity, reference-seed recovery, current compact compiler, local signer and ADB install. Preserve banks and saves; resume interrupted compilation/transfer. | Recommended path. This round supplies source recovery; notification, release distribution and companion automation remain separate work. A PC is still needed during compilation. |
| Entire update on the Quest | Either an Android-supported compiler/toolchain for the current woven game architecture, or a deliberately different native extension/patch architecture and game-free update artifact format. Also local signing-key handling, transactional APK construction, disk/RAM budgeting, Android installer UX and rollback. | Substantial redesign, not a switch in the PC updater. The desktop Unity/IL2CPP Builder cannot simply execute as a normal Quest app. No promise of practical device-only compilation or arbitrary DLL hot swapping. |
| Prebuilt native patch downloaded by the Quest | Stable exact base/ABI compatibility, a reproducible artifact excluding distributable game bodies, verified patch application to locally held bytes, matching code/metadata, local signing and user-facing install recovery. | Needs a separate feasibility and distribution-rights review. Current woven native Player contains original game code; it is not automatically a publishable mod-only package. |

Android's ordinary installer APIs can request installation, but an application
must handle `STATUS_PENDING_USER_ACTION` and may need the user's installation
permission/confirmation. Unattended updates have specific installer, target-SDK
and permission conditions; do not promise them for this sideloaded Quest game.
Actual Quest OS prompt/display behavior would need a headset test. No root,
system-installer privilege or registered Horizon service is assumed.

Primary Android references:

- Package/version/certificate conditions:
  https://developer.android.com/google/play/app-updates#android
- Signing keys:
  https://developer.android.com/studio/publish/app-signing
- Install sessions and pending user actions:
  https://developer.android.com/reference/android/content/pm/PackageInstaller.Session
- Conditional unattended-update rules:
  https://developer.android.com/reference/android/content/pm/PackageInstaller.SessionParams#setRequireUserAction(int)

## Evidence boundaries

Focused production host/ZIP/source tests cover seed migration/reuse, original
input hashes, installed-only orchestration, preserved identity/DLC/sidecars,
unsupported asset changes, finite source compatibility and source-import
protocols. The real extracted 169-DLL seed also compiled the unchanged B717 mod with
strict Release settings (zero warnings/errors, all 15 explicit original
references, no RefAsm fallback). Actual QuestWeaver audit/weave using that seed
closed 490 hooks, 303 targets and seven protected types without issues. This
proves the managed compiler/weaver reference closure, not Android Player
execution. Unity/IL2CPP, APK signing and device execution remain external test
boundaries; unchanged B717 runtime/rendering qualification is inherited. Actual browser/API source selection and
the delivered archive are qualified separately. No new headset installation,
Windows end-to-end run, remote release feed, autonomous updater or game FPS
acceptance is established by this Builder change.

Detailed receipts, failed intermediate checks, source pins and cleanup records:
`/home/claw/quest3-local/build/evidence/quest-device-update-20261021/`.
