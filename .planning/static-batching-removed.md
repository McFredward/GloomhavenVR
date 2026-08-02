# Static Batching (Bündelung) — ENTFERNT (2026-08-02)

**User-Beschluss:** „Wenn Bündelung diesen Effekt haben kann und wir das nicht verhindern
können, ist es keine valide Option mehr." — Zunächst geparkt (`StaticBatchConfig.CurrentMode`
lieferte ab ModBuild 15 hart `Off`, Commit `8e6f8e8`), dann per Folge-Beschluss **vollständig
entfernt**: dieser Commit löscht den Code, alle Aufruf-/Registrierungsstellen und die gesamte
Config-/Settings-Oberfläche. Das Einstellungsmenü zeigt keine Spur von „Bündelung" mehr.

**Wiederherstellung:** `8e6f8e8` ist der letzte Commit mit der kompletten geparkten
Implementierung (voll funktionsfähig, nur der Mode-Getter gepinnt). Ein zukünftiger Worker
holt sich jede Datei mit `git show 8e6f8e8:<pfad>` zurück — Inventar unten.

## Warum (Wurzelursache der unsichtbaren aufgedeckten Räume, 8 Runden)

1. Der Batcher kombiniert beim Szenario-Laden die Renderer unter dem `Maps`-Root
   (904 Renderer → 32 Sammel-Meshes, Unity-interner Pfad über `SetStaticBatchInfo`).
2. Apparance erzeugt den Inhalt neu aufgedeckter Räume durch **Klonen von In-Szene-Quellen**
   (`ApparanceEntity.CreateInstance` → `Object.Instantiate(template, …)`).
3. Ein Klon einer gebatchten Quelle wird mit **leerem `sharedMaterials`-Array** und OHNE
   Batch-Zustand geboren (Hardware-Forensik ModBuild 14:
   `mat0='<no-slots>' staticBatch=False` auf jedem festhängenden Renderer; ohne Bündelung
   lädt derselbe Raum vollständig). So ein Renderer kann nie zeichnen — und kein
   nachträglicher Heiler kann Material-Slots wiederherstellen, die der Klon nie hatte
   (`MaterialLoader` weist nur in vorhandene Slots zu).
4. Der Batcher kann zukünftige Klone nicht kennen → das Zusammenspiel ist **strukturell**.

## Fehlversuche, die das Bild schärften (Kurzchronik)

- R2/3: Apparance-Synthese-Viewpoint (parked camera) — echter, notwendiger Fix, aber nicht
  hinreichend (`ApparanceDetailFocus`).
- R4–8: `MaterialLoaderHeal`-Wächter (Discovery über Kachel-Scan → szenenweit → Harmony-
  Registry; `done-stuck`-Heilpfad; Foreign-Disable-Diskriminator). Der Wächter blieb
  wirkungslos, weil die Klone gar keine Slots hatten — der Lader war nie das Problem.
  Der Wächter bleibt als Versicherung + Diagnose installiert (`ml=`-Zensus).

## Bekannte Restbaustelle (unabhängig von Bündelung)

13 Münz-Deko-Renderer (`coinsingle`/`coinpile`) haben einen echten Addressables-Ladefehler
(Material-GUID `2c309731defe50f4d84721fd7f50c5c4`, `null-result`, auch ohne Bündelung).
Der Wächter versucht 5×, gibt dann mit ERROR-Zeile auf.

## Wiederbelebungs-Skizze

Batching nur, wenn Apparance-Klon-Inhalte batch-sicher gemacht werden:
- Quellen nach dem Kombinieren ihre Material-Arrays zurückgeben (Kopie halten), ODER
- `CreateInstance`-Postfix: Klonen Batch-Zustand löschen + Material-Array aus dem
  Template-Asset (nicht der In-Szene-Quelle) neu befüllen, ODER
- Batching auf nicht-prozedurale Wurzeln beschränken (praktisch wertlos: 907/1529 Meshes
  liegen unter `Maps`).

## Inventar des Entfernten (alles per `git show 8e6f8e8:<pfad>` wiederholbar)

**Gelöschte Dateien (die komplette Implementierung):**
- `src/GloomhavenVR/Core/StaticBatcher.cs` (~1750 Zeilen — Scan/Probe/Apply/Undo-Ledger,
  Bewegungs-Wächter, Auto-Exclude, Treiber-Komponente)
- `src/GloomhavenVR/Core/StaticBatchInterop.cs` (~280 Zeilen — Reflection auf Unitys
  `SetStaticBatchInfo`/Batch-Zustand inkl. `ClearBatchState(Renderer)`)
- `src/GloomhavenVR/Core/StaticBatchConfig.cs` (~320 Zeilen — `BatchMode`-Enum, `[Batching]`-
  Config-Bindings, der gepinnte `CurrentMode`-Getter)

**Bereinigte Aufrufstellen (Feature-Anteil entfernt, Rest unangetastet):**
- `src/GloomhavenVR/Core/CoreModule.cs` — `StaticBatcher.Install(_hostGo)` in `Initialize`,
  `StaticBatcher.Uninstall()` in `Shutdown` (samt Erklärkommentaren)
- `src/GloomhavenVR/WorldUI/ConfigCatalog.cs` — `Bind("batching", StaticBatchConfig.Bind)`,
  Topic-Zuordnung `"batching" → Visual`, Abschnittsname `"Batching" → Loc.Mod("batching")`
- `src/GloomhavenVR/Core/MaterialLoaderHeal.cs` — Runde-8-Heilpfad: der
  `StaticBatchInterop.ClearBatchState(r)`-Aufruf (die Heilung selbst — re-enable + Log —
  bleibt; nur die Batch-Zustand-Behandlung ist raus)
- `src/GloomhavenVR/Core/PerfSceneProfile.cs` — Verweise auf „[Batching] Mode /
  Core.StaticBatcher" im Batching-Verdict-Logtext (die `isPartOfStaticBatch`-Zählung selbst
  bleibt — Unity-API-Diagnose)
- `src/GloomhavenVR/Core/WallSegmentFade.cs` — `<see cref="StaticBatcher"/>`-Verweis im
  Doc-Kommentar von `IsWallFadeShaderName`

**Entfernte Config-/Loc-Oberfläche (das ganze `[Batching]`-Segment):**
- `src/GloomhavenVR/Defaults/Defaults.Core.cs` — der komplette `[Batching]`-Block
  (Mode, Roots, AutoDetectRoots, MinRenderers, MaxVertices, SettleSeconds, RescanSeconds,
  RescanGrowth, IncludeInactive, Watchdog, WatchdogAutoRevert, WatchdogAutoExclude,
  ExcludeLayers, ExcludeNames, ExcludeComponents, FreeCombinedCpuCopy, VerboseLog)
- `src/GloomhavenVR/Core/Loc.ConfigNames.cs` — alle 17 `Batching/*`-Anzeigenamen
- `src/GloomhavenVR/Core/Loc.ConfigDescriptions.German.cs` — alle 17 `Batching/*`-Hovertexte
- `src/GloomhavenVR/Core/Loc.cs` — die Keys `batching`, `batch_mode_probe`, `batch_mode_on`

Eine Nutzer-cfg, die noch eine `[Batching]`-Sektion enthält, ist harmlos: nichts bindet sie
mehr, BepInEx toleriert verwaiste Sektionen (Orphaned Entries).

Forensik-Runden in der Git-History (ModBuild 9–14); Perf-Kontext in
`.planning/perf/FINDINGS.md` (Graphics Jobs machten den Gewinn der Bündelung ohnehin strittig).
