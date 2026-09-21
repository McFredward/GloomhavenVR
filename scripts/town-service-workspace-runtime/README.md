# Merchant visitor workspace checks

Run `python3 scripts/check-town-service-workspace.py`. This compiles the production
`TownServiceWorkspace` into separately named test assemblies and runs them in real
Unity 2021.3.5 with the shipping town bundle. Only the connected native roster, the
clock and bundle-location boundary are fixtures. `Time.unscaledTime` is bound to an
explicit deterministic test clock; source hashes are retained beside each run.

The primary ordinal keeps the existing NPC/front-counter position. Its extra
furniture instance is inactive and dissolved, so it neither z-fights nor adds draw
calls. Other ordinals have full-size counter extensions at x = -1.8 / 0 / +1.8 m,
z = 2.2 m, in the station's outward frame. The 1.65 m tops have 15 cm gaps. Their near
edge leaves at least 80 cm behind the NPC envelope. All owner positions, material
values and intermediate motion must be published by the integration layer; observers
must not derive their own offsets. Parent the catalogue to `Root`; preserve manual
tray placement when handling later roster movement.

Native `PlayerRegistry.CreatePlayer` takes `BoltConnection.ConnectionId` as PlayerID;
`UdpSocket.AcceptConnection` increments `connectionIdCounter`, so IDs are not bounded
party slots and reconnects can exceed four. `NetworkPlayer.Detached` removes the entry
from `AllPlayers`. The existing `LocalStableIndex` uses *Participants*, which omits
connected users with no active controllable. This helper instead uses current full
`AllPlayers` ordinals via `CollectRoster`, including flat/unassigned users. It does not
cache different rank histories on late join. A membership change moves the owner's
workspace over 0.22 seconds; an impossible fifth ordinal throws before opening so the
presentation's existing native-window fallback can remain usable.

Checks cover four distinct ordinals with sparse connection IDs, original furniture
provenance, no cloned NPC or input-blocking colliders, separate owned materials without
mesh property blocks, original-material preservation, intermediate owner movement,
late joins, reconnects, offline/host placement, fifth-user rejection and immediate
idempotent teardown. Six compiled mutations must fail the corresponding assertions.

The helper check establishes neither network publication nor headset clearance against
custom room walls. Those remain integration/hardware responsibilities. It introduces
no wire allocation protocol and does not serialize concurrent shopping.
