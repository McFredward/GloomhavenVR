# Clean installation after package removal — capture194342

`quest-build-support-20261014T194342Z-b3aec28d.zip` identifies Builder7dab24c13,
run b0a1cdab3326426a9d8d026cd79d5cf4 and the unchanged B672 APK, SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The user explicitly confirmed deleting app data and reinstalling. Authorized USB
works. Android returns exit0 / Success for uninstall, then exit1 with both streams
empty for `pm path --user 0 dev.gloomhavenvr.quest`. No replacement install command
was issued. This is expected package absence, not another observed certificate
conflict. The source durably committed `reset-installing` before the rejected
query; the report's older summary whitelist incorrectly displayed `unknown`.

The optional recovery package probe now admits exactly the empty absent result,
with an exact physical-headset guard. Other exit codes, diagnostic output,
malformed or split package paths and transport failures remain failures. The
three required installed-APK queries keep their stricter identity contract.
Seven optional callers cover clean reset, lost removal/install acknowledgements,
legacy preparation and retained debugger cleanup. The fixture now returns actual
Android absence semantics through the production Adb class, rather than exit0.

An unchanged `.ghvrq` resumes the approved `reset-installing` frontier. It installs
the completed local APK without repeating removal, rebuilding Unity or replaying
old save archives. Normal missing game-content deployment after the approved
deletion is still required; the APK's11GB sidecar is not a new backup operation.

Clean-install errors now use a separate installer prefix and Wizard prerequisite,
with accurate retry text rather than signing-restoration advice. Support summaries
retain the exact five reset states and bounded consent/reset flags; no app-data
payload or private member list is exported.

Focused worker156 cases include19 clean-reset cases, real production Adb parsing,
durable interrupted states and negative error/device controls. Root collector,
Wizard/API and actual browser checks cover the changed presentation/diagnostics.
Whole prior-release inventory comparison retains completed preparation/recovery
and compact-update producer scopes. Source hashes qualify inherited evidence for
unchanged areas; this is not a new runtime/wire gate, APK build or headset pass.

Evidence and completion-cleanup receipts are retained outside worker worktrees at
`/home/claw/quest3-local/build/evidence/B672-reset-install-194342-20261014/`.
