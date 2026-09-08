# NEEDED-OUTSIDE — lane worldui-front (refactor 2026-09)

Changes this lane needs in files it does not own. Each is an exact diff with its reason; the
owning lane (or the integrator) applies it.

---

## 1. lane **core** — `src/GloomhavenVR/Core/Loc/Loc.ConfigDescriptions.German.cs` (F1)

**Why.** `WorldUI/WorldUIConfig.cs` corrected the English `MapRoomWindowBarHeightMeters`
description in this lane's F1 commit: since ModBuild 480 (`b27bbb9e`) a shared (blue-barred)
map-room window hangs at the shipped `Defaults.MapRoomWindowBarHeightMeters` on every client
(`ArcSeats.ResolveMapRoomBarHeightMeters(…, sharedWindow: true)`), and the dial moves only the
player's own local windows. The German text still tells the player the opposite — *"MEHRSPIELER:
Dieser Wert gehört zur gemeinsamen Platzierung — alle Spieler einer Sitzung sollten dieselbe Zahl
stehen lassen. Ein abweichender Wert hängt die eigene Kopie eines gemeinsamen Fensters auf eine
andere Höhe"* — which is the advice R2 F8 retired. (There is no German entry for
`SharedWindowArcRadiusMeters`; it falls back to the corrected English bind text, so nothing is
needed for it.)

```diff
--- a/src/GloomhavenVR/Core/Loc/Loc.ConfigDescriptions.German.cs
+++ b/src/GloomhavenVR/Core/Loc/Loc.ConfigDescriptions.German.cs
@@ -2557,8 +2557,9 @@
                 + "\"ideale Position\"-Screenshot, gemessen aus dem Platzierungs-Log. NUR DIE ANFANGS-"
                 + "HÖHE: jedes Fenster bleibt frei verschiebbar, und ein bereits stehendes Fenster bewegt "
-                + "sich nicht, wenn du das hier änderst. MEHRSPIELER: Dieser Wert gehört zur gemeinsamen "
-                + "Platzierung — alle Spieler einer Sitzung sollten dieselbe Zahl stehen lassen. Ein "
-                + "abweichender Wert hängt die eigene Kopie eines gemeinsamen Fensters auf eine andere "
-                + "Höhe, bis es jemand verschiebt. Bereich 0.05-1.2.",
+                + "sich nicht, wenn du das hier änderst. MEHRSPIELER: Seit ModBuild 480 bewegt dieser "
+                + "Regler NUR DEINE EIGENEN Kartenraum-Fenster. Ein gemeinsames (blau beleistetes) "
+                + "Fenster hängt auf jedem Client auf den ausgelieferten 0,60 m, egal was hier steht — "
+                + "die Pose eines gemeinsamen Fensters darf von nichts Lokalem abhängen — und die "
+                + "MAP ROOM WINDOW BAR HEIGHT-Zeile im Log nennt diesen Regler und den ignorierten "
+                + "Wert, sobald beide abweichen. Bereich 0.05-1.2.",
```

---

## 2. lane **Board** (Board/FigureGrab) — an API this lane already exposes and nobody calls (D5)

`WorldUI/Surfaces/PropInfoSurface.ShowHeldProp(Transform? anchor, HandSide)` /
`ClearHeldProp(HandSide)` have had no caller since ModBuild 360 (`19a13ca5`). Their doc says the
dock resolves the anchor from the shared `HeldProps` registry on its own and that this call is a
fidelity upgrade the grab side (`Board/FigureGrab/GrabbableProp.cs`) could make; the patch text
is already in `.planning/LANE-PROPINFO-357-NEEDED-OUTSIDE.md`. Re-raised here so the pair is
either called or retired in one place — this lane kept it (BRIEF §5: an API a documented
cross-lane request names is not dead).
