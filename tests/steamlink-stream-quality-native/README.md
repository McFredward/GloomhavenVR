# Steam Link stream-quality lease: focused native controls

Run `python3 -B tests/steamlink-stream-quality-native/run.py --output <private-output> --rebuild-native`.
Add `--sanitize` for ASan/UBSan. All children are reaped and the shared
`cleanup_completed_run` API plus exact owned-cache cleanup runs on pass/failure.
Logs, serializer transcripts, results and cleanup receipts remain for review.

The fixture executes the actual production C lifecycle and typed public function
table calls with controlled fake OpenVR APIs: four-key snapshot before writes,
identity admission/recheck, partial apply failures, bounded width readback,
manual-edit conflicts, reverse restoration and actual parent-pipe EOF. A planted
blind-restore source mutation must fail those same controls. It emits
acquired/restored/error transcripts through the same C serializers as the PE.
The shipping PE is rebuilt byte-identically with source/compiler provenance.

These are not Windows/Proton/OpenVR/SteamVR hardware executions. Named Windows
mutex placement and zero-wait/refusal paths receive source checks, not a Windows
kernel or cross-Proton-prefix concurrency proof. Public settings have no atomic
compare-exchange: a simultaneous edit between a read and write, or manual reselection
of the same value, is not distinguishable. An observed later unknown width change
is preserved as a conflict rather than attributed to the driver. Parent/game death
closes stdin and requests restore; killing/crashing this helper itself can prevent
restore and must remain an explicit unknown-outcome warning in the parent.
Restoration preserves the previous effective typed values; public IVRSettings
does not expose whether those values originally came from defaults or explicit
overrides, so absence of a user override is not reconstructed.
