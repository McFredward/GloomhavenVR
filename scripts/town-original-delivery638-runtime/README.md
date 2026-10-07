# Original-artwork delivery proof

Run `python3 scripts/check-town-original-delivery638.py --controls` with .NET 8.
It requires neither Unity nor the game and runs in both local and hosted CI gates.

The runner binds the current production `SendToken` reflection invocation and
`NextBatch` method, original codecs, fragment assembly, delta dependencies and
allocation-free delivery selector. Protocol constants are extracted verbatim;
unused Unity pose/math helpers are outside this proof, without replacement shims.
Source hashes and generated compilations remain with each result.

Native reflection/channel and queue selection are explicit fixture boundaries.
The controls must fail runtime assertions for unreliable originals, missing
argument restoration, omitted compressed/batched classification and event-budget
regressions. This proves the native reliability request and exact bounded data
path, not Bolt retransmission timing, queue fairness or headset appearance.
