# Multiplayer hardware round — ModBuild 482 observations, 484 fixes

## Evidence and scope

Both supplied `LogOutput.log` files identify ModBuild **482**, commit **6efa4acfc**.
The implementation base is `dev` **8cc2f3a0**, ModBuild **483**. Therefore these logs
do not test the asset changes in 483. Screenshots are the three `aktive_boni*.jpg`
files in the gitignored `.planning/debug/` directory.

The seven requested areas are active-card pulse liveness, meaningful card flights,
immediate active-card grabbing after landing, actionable-only rest controls,
short-rest burn presentation on both clients, original remote bonus widgets, and
the client's disconnection. Code, comments, and developer documentation remain
English; user reports are German. Implementation workers have separate worktrees;
the integrator reviews their changes and publishes the resulting build to `dev`.

## Disconnection: what the supplied logs actually prove

The remote `Player.log` ends receipt of network actions at line **81529**. It
continues issuing side actions and rendering frames afterwards. At **82322** it
prints `[EndPoint IPv4 0.0.0.1:0] timed out`, followed by
`Disconnected from the session. Reason: Timeout`. The host records player 2
leaving at **94064**, then transfers that player's characters normally.

This is the UDP receive timeout, not the game's action-desynchronization path.
Read-only inspection of the installed `udpkit.dll` identifies the exact log site:
`UdpConnection.ProcessConnectedTimeouts` compares `RecvTime + ConnectionTimeout`
with the socket clock. The game's `NetworkManager.OnEnable` sets that timeout to
**30,000 ms**. There is no corresponding packet-unpacking exception or mod
subscriber exception in the interval.

The client also slows to roughly **200 ms per frame** during the receive gap;
heartbeats 65 and 66 each advance only 49 frames in ten seconds while head and
controllers remain tracked and the head pose continues moving. The host's nearby
30-second frame report averages **13.30 ms**, with a maximum of **30.84 ms**.
Consequently the evidence does not support a host main-thread deadlock.

The increasing `Send Rate` multiplier is not proof of excessive mod traffic.
Installed `BoltConnection.SendRateMultiplier` derives it from the UDP outstanding
packet window's fill ratio: missing acknowledgements also produce this reading.
Likewise, the recurring Hydra DNS errors occur throughout both otherwise-working
sessions; their presence alone does not identify this disconnection's cause.

The logs do not identify why transport packets stopped arriving. A transport
timeout is established; a specific Wi-Fi, relay, runtime, or mod cause is not.
Increasing the timeout, suppressing disconnection, or replaying game actions would
not repair a missing connection and is not a justified fix.

## Integration and validation

Pending worker review and the complete local gate set. No hardware result for
ModBuild 484 is claimed by this document.
