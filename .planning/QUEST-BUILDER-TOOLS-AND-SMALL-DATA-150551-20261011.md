# Quest Builder device tools and small-data installation recovery

## Evidence and scope

The maintainer aborted capture150551 while signing recovery copied the installed
game bank on the headset. The delivered Builder is source939bb5fc2, Runtime671;
the completed Player remains665. All six preceding build stages are closed.
The installation log measures11,169,001,665 bytes in the old bank. This is
regenerable game content, not savegame/app-data backup. The recovery journal also
retains a2,871,119,500-byte original APK for the earlier all-data rollback promise.

The previous parking probe compared inode numbers across Android FUSE views.
A different inode selected the copy fallback even though those numbers do not
establish whether a directory rename copied its contents. The fallback then
copied and hashed all bank members. This round changes the save-priority path;
it performs no Unity import, Shader matrix, game-code change or new APK export.

## Installation contract

- Default recovery retains savegames, their checkpoint/history/import roots,
  settings and readable mutable app data. Known external StreamingAssets and
  private Wine/Proton prefixes/caches are pruned before enumeration. Native save
  roots are protected by location, irrespective of filenames or extensions.
- The small-data path does not copy or hash the old installed APK. Bounded package
  metadata witnesses guard the old app and the replacement at mutation boundaries.
  Verified binary archives retain the preceding Windows transport repair.
- A176-byte owned static ARM64 helper calls only renameat and exit. It cannot copy,
  enumerate, hash or remove game assets. A transaction marker proves a two-way
  move before banks are parked. FUSE inode comparisons and toybox mv copy fallback
  are not used on this path.
- If Android refuses the primitive or a bank move, earlier moved banks are
  returned. The installed app remains present and the UI asks explicitly before
  uninstalling and normally transferring required content again. Ordinary Retry
  never consents. Approval binds session, build/input/APK identities, attempt,
  transaction and hardware serial; it reopens installation only.
- Capture150551's pre-removal legacy copy can upgrade after a bounded exact old
  APK witness. Healthy small archives remain reusable; only transaction-owned
  partial generated copies are removed. An already destructive legacy frontier
  retains its earlier restoration promise. Existing original-APK backups are
  not silently deleted.
- If replacement installation fails after uninstall, small archives and parked
  banks remain durable for an installation retry. The small-data policy does not
  create a multi-GB original APK rollback package. A changed identity pauses
  rather than restoring savegames into an unexpected app.
- Source selection still qualifies the local APK/content archive. A bounded,
  per-invocation digest cache uses pre/post file-stat witnesses and avoids the
  identical second qualification read. It does not persist a trust exemption.

## Independent homepage tools

Launch opens a homepage with new-build, existing-APK mod update, name/profile edit
and explicit interrupted-run resume actions. Opening Resume reads the saved run;
it does not start or replan it. A name-only edit preserves the APK's existing
provider/account identity and DLC record. A legal locally installed game remains
required; mod updates use the bundled release source rather than silently taking
a newer network branch or an arbitrary game-folder mod.

The Quest connection/log section reuses the existing installer and collector:
USB authorization guidance, wireless setup/reconnection, hardware selection,
cancel/retry and a downloadable runtime-capture ZIP. It works without a build
session. Captures live below the owned workspace's device/captures directory.
The default workspace is %USERPROFILE%/.ghvrq on Windows and ~/.ghvrq on Linux;
the launcher-local Python environment remains beside its scripts.

Storage inventory performs one metadata-only walk, without asset hashes or
following links/reparse points. It reports logical category sizes and separate
old-APK/app-data backup sizes; results stay cached until explicit refresh.
Deletion still requires a scoped preview and confirmation, preserving completed
APK, its required adjacent content bank, signing keys and active save recovery.
Build-cache deletion removes reusable conversion/Unity results, making a later
full rebuild slower; it does not remove the finished distributable pair.

Device mutation and cleanup share the owned kernel run lock with builds. The
loopback token/Origin boundary is retained. A capture download accepts a bounded
opaque ID, never a user-supplied filesystem path or shell command.

## Validation and limitations

Fresh root checks exercise affected Wizard HTTP/device/storage/profile/consent,
release/support boundaries and the composed UI. Installer validation covers the
complete affected host suite with explicitly skipped optional Windows cases.
Detailed counts, source hashes, logs and archive qualification are retained in
the delivered validation JSON and private evidence:
/home/claw/quest3-local/build/evidence/B671-builder-tools-150551-20261011/.

Independent controls compare336 old/new ordinary stage keys across all seven
stages, providers and host families;48 name-edit controls establish the scoped
new identity. Exact ARM64 instructions are cross-assembled against NDK tools and
executed through QEMU with actual Linux kernel operations, including explicit
EXDEV/EACCES refusal. An11-GB sparse-file move demonstrates directory rename
without payload copies; it is not a measurement of full Quest content throughput.
Old/new delivered-source selectors qualify retained complete preparation owners
without re-reading the Windows asset workspace. Actual extracted Linux launch
checks bootstrap, authenticated UI/API, inventory and retained session reopening.

Quest SELinux/FUSE acceptance, save restoration and completed hardware installation
remain unverified. The official Windows ADB end-to-end path is not established by
Linux/QEMU or mock HTTP tests. Unchanged game-code compilation evidence is inherited
from the preceding strict Release check; this round is not a fresh complete game
gate or an assurance that the headset picture is correct.
