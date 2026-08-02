# Static Batching (Bündelung) — GEPARKT (2026-08-02)

**User-Beschluss:** „Wenn Bündelung diesen Effekt haben kann und wir das nicht verhindern
können, ist es keine valide Option mehr." — `StaticBatchConfig.CurrentMode` liefert seit
ModBuild 15 hart `Off`; die Config-Oberfläche bleibt lesbar (Legacy), der Code bleibt
vollständig erhalten.

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

Git-Referenzen: Batcher `src/GloomhavenVR/Core/StaticBatcher.cs` + `StaticBatchInterop.cs`
(voll funktionsfähig, nur der Mode-Getter ist gepinnt); Forensik-Runden in der History
(ModBuild 9–14).
