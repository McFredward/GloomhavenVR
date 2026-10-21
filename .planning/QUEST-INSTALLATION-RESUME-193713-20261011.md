# Quest installation: durable file progress and connection recovery

Date: 2026-10-11. Branch: `feature/quest3-standalone`. Runtime remains671;
this repair changes the host Builder/installer only. No Unity import, game
rebuild, new APK export or unrelated whole-game gate is required.

## Captured workload and actual failure

Capture `quest-build-support-20261011T193713Z-3f4a5739.zip` identifies delivered
source175a84964, the finished B665 APK and six closed Builder stages. The APK
SHA is `0a585c616387ae5b3eca83a490d3a990c39fc7eccc122d7273a9dceeaa1a094f`;
its input key is `9207a0cf49519b717a85c4d3e6f13f9dade1b1cbb2e3bb460e4484365899dbef`.
An authorized USB Quest is used directly. No adbd restart for Wi-Fi, fresh game
build, large original APK backup or new full app-data archive is observed.

Neither content bank initially has a valid complete, previous or pending
installation receipt. This is the first file-backed content delivery after the
previously accepted reinstall. Four mod files total245765978 bytes; the original
game bank has6151 files. The combined payload totals11755745528 bytes. Ordinary
installation must deliver these missing game assets; saves/settings are separate
small mutable data, previously captured as573440 archive bytes.

At interruption, the measured total is7629212241/11755745528 bytes (64.90%). The
last closed checkpoint retains4597 game files and7474441579 combined bytes.
Another65 files were successfully published after that checkpoint; a66th file
has exact size1808844 and matching SHA in verified staging. Its separate `mv`
command fails because the local Windows ADB daemon at127.0.0.1:5037 cannot start
or connect (10060). USB disconnection itself is not established. The following
cleanup command succeeds and deletes that verified staging. The old batch-only
receipt does not claim those65 published files. The exported device/install
logs are clipped; summing their visible upload lines cannot measure the whole
transfer.

## Reduced work and durable boundaries

- On Android's observed `remote fchown failed: Operation not permitted`, the
  shell-v2 path now performs containment checks, binary transfer, exact size/SHA
  qualification, atomic publication and an append-only file claim in one remote
  operation. Its bounded stdout must contain the unique operation token, size
  and SHA, with observed remote Exit0 after exact binary stdin EOF. A zero client
  exit or absent acknowledgement alone can never claim success. Healthy files
  require no additional CLI launch for hash, rename or staging cleanup.
- Each qualified file is recorded immediately, instead of waiting for the next
  256MiB/256-file batch. Claims bind the bank's exact content key/path/size/SHA.
  An interrupted append cannot own completion; leading newline boundaries
  isolate torn entries. The bounded journal is removed only after the complete
  bank receipt is published. Existing batch receipts remain compatible.
- Resume checks metadata only for already claimed files. It does not first stat
  every missing file, hash completed banks, extract already completed mod ZIPs
  or reconstruct a full receipt for every newly accepted file. Explicit content
  repair still checks actual bytes. A completed unchanged bank uses its compact
  exact receipt without scanning its tree.
- The original captured closed batch remains reusable. Because the old Builder
  never wrote individual claims for the subsequent65 files, that small open
  batch can require one last retransmission on migration to this repair. New
  transfers preserve per-file progress. Resending the complete game bank is
  unnecessary while its matching device claims remain present.
- Unchanged local APK/content ZIP SHA results use a small persistent cache in
  the device-settings directory (`qualified-inputs.json`). Exact path, device,
  inode, size, mtime and ctime must still match. Missing, damaged, unwritable or
  concurrently busy caches fall back to ordinary hashing. Expected source SHA
  comparisons and the owned-game qualification remain mandatory.
- Manual APK ZIP CRC qualification is cached separately in
  `qualified-apks.json`, only after actual CRC success. A digest cache can never
  substitute for that first qualification. Completed build/handoff receipts
  already identify an exact verified APK and avoid redundant whole-APK CRC
  decompression. Dry-run never creates cache/config directories.

Metadata witnesses intentionally avoid routine full-file rereads. They do not
promise discovery of same-metadata disk bit rot or protect against deliberate
local cache modification; explicit repair performs byte verification. Neither
cache is a licensing or anti-piracy mechanism.

## Connection prerequisite and exact continuation

The Wizard starts its installer with `--wait-for-connection`. Missing connection,
authorization, local-daemon failure and transport timeout pause that owned child
with measured progress retained. A small `start-server` probe precedes qualified
USB-first selection, with only the remembered Wi-Fi fallback. No global server
kill, network scan or unrelated-headset adoption occurs. The selected physical
serial must still match; transport aliases can change from Wi-Fi to USB while
hardware identity remains fixed.

The UI uses a bilingual connection prerequisite, preserves the byte/whole-stage
bars and cancellation, and disables a duplicate installation attempt. It does
not present this live pause as a terminal error. Reconnecting/unlocking the same
Quest and accepting USB debugging resumes that child automatically.

After lost shell-v2 Exit/publication acknowledgement, reconnect probes only the
uncertain operation: a complete staged file or an exact already-published target
is qualified and published/claimed without another payload transfer. An empty
known incomplete exit76 permits removal/retry of that operation's partial stdin
file. Complete staged checksum errors, foreign links and wrong EOF markers remain
integrity failures; they are not disguised as a disconnected device.

Signing recovery resumes its existing committed transaction through the cause of
a transport exception. It does not replay arbitrary uninstall/restore commands
outside that transaction. A lost normal APK-install acknowledgement qualifies
only the uncertain installed package's exact SHA before deciding whether another
APK transfer is needed. Normal success never hashes the headset APK again.
Read-only package queries and idempotent final launch/cleanup can reconnect;
actual policy compromises still require the existing explicit user choice.

## Validation and practical limits

Fresh focused installer, Wizard progress/device/recovery, support-export and
browser suites cover these changes. An independent reviewer executes generated
shell scripts in actual bounded child processes, including connection loss,
lost publication acknowledgement, partial binary stdin, zero-client-exit refusal,
65-file journal resume, warm receipt reuse and unrelated failure refusal.
Source-bound counts and artifacts are recorded in the current Builder validation
receipt and the compact evidence directory below.

The actual delivered game-free archive is CRC/source-inventory qualified and its
Linux Bash launcher is exercised from a path containing spaces, including its
venv, authenticated UI/API, artwork and retained-session reopening. Old/new
release inventories are compared at the captured completed27-owner preparation
frontier, with changed producer inputs rejected. The game/project/profile/journal
for this selector check are synthetic; the Windows imported workspace is not
available here. These checks establish host boundaries, not headset throughput,
an end-to-end Windows installation or a new successful Quest game run.

Evidence: `/home/claw/quest3-local/build/evidence/B671-install-resume-193713-20261011/`.
The final delivery directory retains only the latest game-free Builder ZIP,
archive audit and validation. Reaped owned worker worktrees and duplicate test
archives are removed; supplied captures, borrowed game inputs, current symbols,
compact evidence and other agents' work are preserved.
