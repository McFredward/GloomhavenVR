# Quest Builder: whole shader progress and retained app-storage restoration

Report `quest-build-support-20261013T211355Z-85a32824.zip` identifies Builder
`ae0a6f072`, ModBuild672, session `b0a1cdab3326426a9d8d026cd79d5cf4`. The APK
build has completed: SHA-256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The authorized USB device already reports installed version
`0.1.0.B672.d4e76da00ec2`. This report contains no new compilation failure.
Installation is incomplete because the existing signing-recovery transaction
cannot reopen the replacement app's external storage view to restore data.

Private and device-protected backups have restored. Their 10,240-byte tar
containers contain 2,143 and 106 mutable bytes respectively. The external backup
is not empty: 52 members, 36 files and 246,284,296 mutable bytes. Retain it and its
transaction; never treat the inability to enter its destination as permission
to discard it. The 11-GB original-content bank is not part of this data backup.

The old restoration created the external package directory in the shell view,
then immediately required it to exist in the replacement UID's separate view.
The capture proves the latter test fails, but does not establish an exact errno.
Create a missing exact package root within the actual replacement-app view,
after checking existing ordinary ancestors and links. Prove an app-owned
write/read acknowledgement before extraction. External tar restoration still
uses the app UID, never the shell's UID or a recursive permissions change.

If a separate storage namespace is required, preserve the existing guarded
debugger-wait approach. In the capture, Android acknowledges the intent but no
app PID appears in any of 40 observations. This does not distinguish a sleeping
headset, deferred launch or early crash. Retry only that package once, preserve
the existing transient debugger selection, and record bounded package/power/
launcher observations. Request `KEYCODE_WAKEUP` only when power evidence says
Asleep/Dozing; do not bypass authentication/presence or change global settings.
UID, process starttime, debugger wait and complete thread pause remain mandatory.

If that safe boundary is still unavailable, the existing structured
`restore-app-access` action becomes an actionable pause with English/German
instructions to wear/unlock the Quest and retry installation. Previously the
Wizard ignored that valid action and presented a generic signing failure.
The completed APK, archives and restoration journal remain reusable. Retry
continues installation without another build, uninstall or bank backup; genuinely
lost-device actions retain their own USB/Wi-Fi guidance.

The shader complaint is independently source-proven: the separate compiler card
used one pass's `closed` flag to mark the whole shader section complete. The
retained overview includes pass1557 at6/6 variants and250 observed shader names.
A returned pass is not completion of all other passes and shader serialization.
Use the owning whole phase's existing finite source census and observed native
handoff for the primary bar. Label the census as known shader-name coverage,
not a promised selected variant queue. Keep the current pass's actual variants
in an explicitly secondary bar. Only the observed whole caller/handoff may
publish primary100%; old retained per-pass completion cannot restore false100%.
Reuse the bounded existing asset census, without a shader QA build or game scan.

Focused restore/signing, Wizard recovery, Python/Node progress and actual captured
state replay checks qualify this host-only repair. Compare the complete previous
release's original recovery/preparation and compact-update scopes, and exercise
the extracted game-free Linux release. Inherit unchanged B672 game/native/import
evidence; do not claim a new Windows APK or successful headset restoration.

Replace the Builder files, retain `.ghvrq`, and resume this run's installation.
Keep the Quest awake and unlocked for the next test. Compact evidence lives in
`quest3-local/build/evidence/B672-install-shader-progress-20261013`. Reap exact
fixture processes and invoke central completion retention; remove only this
round's integrated worker copies and duplicate ZIP after publishing the current
handoff. Preserve original inputs, captures, backups and other agents' work.
