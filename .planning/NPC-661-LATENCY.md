# NPC661 latency investigation

Both supplied hardware log banners identify Build660 `12df1353f`. The host
debug stream observes the other peer's enchantress, whereas the remote log is
Info-only. No owner-visible timestamp or paired video clock is available for
that enchantress. The supplied video was inspected in all 18 balanced frames;
progressive panel population may also be actual owner UI updates and is not
assigned a network cause without corresponding owner evidence.

## Exact original rejection

The host repeatedly refuses a compact original for the `face.333` bottom-button
Row Container: owner basis `798023A253BCF25E`, observer basis
`820A72B8FD734195`, structure `1B23CA78`. One actual transaction reports module
72 missing and finally admits after 1.286 seconds. The available Info owner
stream does not establish which physical output generated module 72.

Source inspection establishes that a compact refusal previously waited for the
complete outbound atomic bundle callback, a further 150 ms receipt grace,
another 15 Hz capture turn, and the queued full original. Record 114 now requests
only the genuinely refused exact private original. It neither credits a receipt
nor changes native template validation. The owner retains the same immutable
full original and sequence, ignores stale/wrong-owner/withdrawn/already-received
identities, and encodes at most two requested originals or 2 ms after its first
encoding per render pass. Both directions retain existing transport budgets.

The first-arrival admission clock now survives required-census changes. A
separate census age identifies subsequent actual owner membership changes; the
bounded significant-anomaly budget is retained across a transaction.

## Honest clock boundary

The new `scripts/npc661-latency/run.py` reuses the maintained actual native
exporter, capture, scheduler, fragment, receive, validation, apply and rendering
bindings. Its primary clock starts before the first owner draw and all actual
selected-original freeze/partition/registration and basis preparation. It never
restarts after preparation and preserves the existing one-second assertion.
The secondary `--prepared-control` labels its excluded preparation explicitly.

Initial cold run `run-dwf3v53g` **fails** the unchanged one-second threshold:
first owner draw 0.113 s, native bank frozen at 0.585 s, subsequent asset/basis
preparation 0.636 s, complete rendered observer 2.780 s. The prior prepared
boundary omitted roughly 1.22 s before delivery in this run. The separate
prepared request run `run-pckyn0ta` also **fails**, at 1.179 s. These are measured
Unity editor/llvmpipe fixture times, not inferred hardware elapsed times.

The native serialized hierarchies and every original material/graphic property
remain real. Native gameplay `MakeAbilityAction`, model/container population and
full HUD Initialize are explicit constructor ports, and the inherited physical
body fixture remains a separate limitation. A passing prepared boundary does
not prove the whole cold hardware lifecycle. Further pipeline work and causal
controls remain required; this checkpoint does not claim the latency target is
met or that a headset outcome has been verified.
