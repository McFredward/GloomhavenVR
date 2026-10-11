# Town presentation election runtime proof

`check-town-presentation-election.py` extracts unchanged production election,
identity, affordance and reset methods from `TownServiceMirror.cs`, and compiles
them with the complete production grant sync, codec and ledger. The test ports
are the Unity clock, local/native-host identity, connectivity, visitor/session
arrival and reliable byte delivery. No fixture replaces the election or grant
algorithm. Source hashes are retained with each compact run.

The proof checks independent clients with delayed/reversed visitor observations,
receiver-local age skew, member departure/replacement/staleness, independent
residents, non-exclusive browsing and temple purses, actual reliable host grant
takeover/release and cosmetic transaction fallback. Negative controls restore
the original sticky browsing cache and original receiver-age ranking separately,
and inject stale transaction caching, missing grant dominance and temple
occupation. Each must fail its designated production assertion.

This is a network lifetime/selection proof. It does not render the original UI,
exercise sockets, simulate the full peer-disconnect cleanup, or establish headset
picture/audio correctness. The original-widget rendering suite remains separate.
