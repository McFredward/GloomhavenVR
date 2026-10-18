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
Restoration preserves previous effective typed values and explicitly missing
keys as described below. An effective API default cannot be distinguished from
an explicit override; that case restores the value, not unobservable provenance.

Build701 also covers all16 setting-presence combinations, explicit false/zero,
120 partial-apply rollback cases with absent keys, exact RemoveKeyInSection
restoration/readback, user removal while active and all non5 API errors. A second
negative control restores Build700's original missing-setting refusal. Error5
means neither explicit value nor default; it is not permission to fabricate an
old effective value. Receipts carry an absence mask, and remaining snapshot
failures name the key plus public API error number. The old hardware log only
said snapshot, so it cannot identify the original failing key/error retroactively.

Build702 requests64..16384 in64-pixel steps, including actual-carrier examples
3456/4096/4864. These are parser/arithmetic bounds, not a demonstrated encoder or
decoder maximum. An immediately observed smaller readback rolls back with
`width_limited`; the helper cannot prove later asynchronous video negotiation.
The managed parser also rejects a smaller acquired receipt. Higher existing
manual widths remain preserved. The fixture exercises these paths in the actual
C session, with ASan/UBSan and byte-identical shipping-PE reproduction.
