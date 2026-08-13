using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// GERMAN text for the bound config entries' descriptions — the paragraph in the middle of
/// every hover bubble in Debug ▸ Alle Einstellungen. Keyed <c>"Section/Key"</c>; a <c>*</c> in
/// the key marks a per-hand-style / per-control-board FAMILY whose members share one text (see
/// <c>Loc.ConfigDescriptions.cs</c> for the resolution and for why the English at the bind site
/// stays the source of truth).
///
/// <para>ADDING A SETTING. Nothing here is required: an entry with no line in this table shows
/// its English description, complete and readable, and the tooltip stays in ONE language
/// either way. Translating it later is one line — no UI change, no registration.</para>
///
/// <para>The tooltip clips a description at 620 characters after collapsing whitespace, so these
/// are written tight rather than expansive; where the English already overran, so does this.</para>
/// </summary>
internal static partial class Loc
{
    private static Dictionary<string, string> BuildGerman() =>
        new(784, StringComparer.Ordinal)
        {
            // ---- [Perf] ----
            ["Perf/Enabled"] =
                "Hauptschalter für die gesamte Leistungsmessung. AN protokolliert regelmäßig eine [Perf] "
                + "FRAME/STEPS-Zusammenfassung plus eine [Perf] SPIKE-Zeile für jedes Bild, das das "
                + "Bildzeitbudget der Anzeige sprengt — die Zahlen, die aus \"es ruckelt, wenn ich den Kopf "
                + "schnell drehe\" ein zuordenbares Subsystem machen. AUS macht die ganze Schicht zum No-op "
                + "(ein bool-Test pro Frame, keine Stopwatch-Lesungen, keine Dictionary-Lookups, keine "
                + "Logzeilen).",
            ["Perf/SummaryIntervalSeconds"] =
                "Sekunden zwischen den periodischen Zusammenfassungszeilen [Perf] FRAME und [Perf] STEPS. "
                + "Niedriger = feinere zeitliche Auflösung im Log, dafür mehr Zeilen; die Abtastkosten pro "
                + "Frame ändern sich durch diesen Wert nicht.",
            ["Perf/Attribution"] =
                "Misst jeden BENANNTEN Mod-Schritt (jeden TickGuard.Run-Schritt plus die explizit "
                + "umschlossenen Treiber-Rümpfe) mit einer Stopwatch, damit die [Perf] STEPS-Zeile die "
                + "Subsysteme des Mods nach Kosten sortieren kann und eine SPIKE-Zeile benennen kann, wem der "
                + "Spike gehörte. Das ist das wertvollste Einzelstück der Instrumentierung: es beantwortet "
                + "\"ist der Ruckler UNSERER?\". Kostet zwei QueryPerformanceCounter-Lesungen und einen "
                + "Dictionary-Lookup pro Schritt und Frame (insgesamt wenige Mikrosekunden). AUS misst "
                + "weiterhin den Bildtakt, nur ohne Zuordnung.",
            ["Perf/TopSteps"] =
                "Wie viele Mod-Schritte die periodische [Perf] STEPS-Zeile auflistet (sortiert nach der "
                + "Gesamtzeit im Fenster). Wird ignoriert, solange Attribution aus ist.",
            ["Perf/SpikeLines"] =
                "Gibt eine [Perf] SPIKE-Zeile aus, sobald ein einzelnes Bild länger braucht als "
                + "SpikeBudgetFactor x das Bildzeitbudget der Anzeige, und benennt die schlimmsten Mod-Schritte "
                + "IN DIESEM BILD. Das ist die Zeile, die das gemeldete \"die Welt ruckelt bei schneller "
                + "Kopfdrehung\" einfängt — ein Ruckler ist eine Handvoll zu später Bilder, und die kann ein "
                + "Mittelwert niemals zeigen.",
            ["Perf/SpikeBudgetFactor"] =
                "Ein Bild gilt als SPIKE, wenn es länger braucht als dieses Vielfache des Bildzeitbudgets der "
                + "Anzeige (Budget = 1 / tatsächliche Bildwiederholrate, vom XR-Display gelesen — kein fest "
                + "verdrahtetes 72/90 Hz). 2.0 = \"mindestens ein ganzes angezeigtes Bild wurde verpasst\". "
                + "Niedriger fängt mehr ein, um den Preis von mehr SPIKE-Zeilen.",
            ["Perf/SpikeMaxPerSecond"] =
                "Harte Obergrenze für SPIKE-Zeilen pro Sekunde. Eine wirklich schlechte Phase würde sonst "
                + "eine Zeile pro Frame ausgeben und das Protokollieren selbst würde zum Leistungsproblem; "
                + "unterdrückte Spikes werden trotzdem GEZÄHLT und in der nächsten FRAME-Zusammenfassung "
                + "gemeldet, es geht also nichts verloren. Eine Einstellung \"unbegrenzt\" gibt es bewusst "
                + "nicht.",
            ["Perf/Allocations"] =
                "Erfasst einmal pro Frame die Zahl der GC-Durchläufe und das Wachstum des Managed Heap und "
                + "meldet sie pro Fenster. Ein gen0-Durchlauf mitten in einer Kopfdrehung ist genau das "
                + "gemeldete Symptom, deshalb ist Allokationsdruck hier ein Beweismittel erster Klasse. Kosten: "
                + "eine billige Heap-Größen-Lesung pro Frame.",
            ["Perf/XrStats"] =
                "Fragt das XR-Display-Subsystem nach seiner EIGENEN Wahrheit — verworfene Bilder, "
                + "dargestellte Bilder, GPU-Zeit, Motion-to-Photon-Latenz — und nur so wird Reprojektion "
                + "sichtbar (der Compositor deckt uns ab) statt nur unserer CPU-Zeiten. Zähler, die die Runtime "
                + "nicht offenlegt, erscheinen im Log als \"n/a\", nie als vorgetäuschte Null.",
            ["Perf/FrameSplit"] =
                "Zerlegt jedes Bild in MAIN-THREAD-LOGIK (Update->LateUpdate), RENDER LOOP (Culling + "
                + "Draw-Call-Abgabe, zusätzlich pro Kamera und pro Pass aufgeschlüsselt) und BLOCKIERT (Warten "
                + "auf GPU / XR-Compositor) — die [Perf] SPLIT-Zeile. Das ist die Messung, die sagt, welcher "
                + "SCHICHT das Bild gehört, und weder das Bildintervall noch der GPU-Zähler der Runtime können "
                + "das beantworten: auf der Hardware von 2026-07 las dieser Zähler in jedem Fenster das "
                + "Bildintervall ab und bewegte sich nicht, als das Pixel-Sample-Budget um den Faktor 11 "
                + "gesenkt wurde, weil die Runtime die App durchgehend auf 45 Hz festgenagelt hatte. Die "
                + "Spannen hier entstehen aus den eigenen Uhrenlesungen des Mods an bekannten Punkten in Unitys "
                + "Frame und greifen deshalb auf jeder Plattform, ohne eine Spielereinstellung vorauszusetzen. "
                + "Kosten: zwei Timer-Lesungen pro Frame plus zwei pro Kamera-Rendering, ohne Allokation.",
            ["Perf/SceneCensus"] =
                "Hängt eine Renderer-Zählung (gesamt / aktiviert / für mindestens eine Kamera sichtbar) an "
                + "die [Perf] SPLIT-Zeile an. Die Batch- und Draw-Call-Zähler von UnityStats gibt es nur im "
                + "Editor, also ist dies der nächstbeste Laufzeit-Ersatz für \"wie viel ist da abzugeben\", und "
                + "zusammen mit der Zahl der Kamera-Pässe beziffert sie ein zusätzliches vollständiges "
                + "Szenen-Rendering. Bewusst nur EINMAL PRO FENSTER abgetastet: der dahinterliegende "
                + "Objektdurchlauf allokiert und wäre bei Bildrate selbst ein Ruckler. AUS für eine "
                + "Aufzeichnung, bei der schon ein Hänger pro Fenster stört.",
            ["Perf/SceneProfile"] =
                "Standardmäßig AUS. Hängt jeder Zusammenfassung zwei weitere Zeilen an: [Perf] SCENE — die "
                + "Renderer-Population aufgeschlüsselt nach Szenen-Root/Kindgruppe, Ebene (mit Namen), "
                + "Renderer-Typ, Schattenwurf-Modus, Anzahl der MaterialPropertyBlocks und ANZAHL VERSCHIEDENER "
                + "MATERIALIEN — und [Perf] GFX — der Renderzustand, der das Abgabevolumen vervielfacht "
                + "(Qualitätsstufe, Schatteneinstellungen, Pixel-Lichter, die Lichterzählung sowie "
                + "Pfad/Tiefentextur/Culling-Maske der Kopfkamera). Die nackte Zählung auf der SPLIT-Zeile sagt "
                + "1683 Renderer; sie kann nicht sagen, WAS sie sind, und eine Zahl kann keinen Hebel wählen. "
                + "Entscheidend ist die Zahl der verschiedenen Materialien: die Built-in-Pipeline kann nur "
                + "Renderer zusammenfassen, die sich eine Material-Instanz teilen — ein Material pro Renderer "
                + "heißt also, dass es gar keinen Batch gibt, den irgendwer, Mod oder Spiel, zerbrechen könnte. "
                + "Wie die Zählung nur EINMAL PRO FENSTER abgetastet: der Durchlauf allokiert und wäre bei "
                + "Bildrate selbst ein Ruckler. Läuft nie in den Szenen vor dem Menü (Bootstrap/Intro): dort "
                + "gibt es fünf Renderer, und ein Fehler in einem Durchlauf, der läuft, bevor die "
                + "Einstellungstafel existiert, lässt sich aus dem Headset heraus nicht abschalten.",
            ["Perf/CullSubmitSplit"] =
                "Standardmäßig AUS. Teilt den Wert jeder Kamera auf der [Perf] SPLIT-Zeile auf in CULL "
                + "(onPreCull→onPreRender: Unitys Sichtbarkeitsbestimmung, die mit der Zahl der vorhandenen und "
                + "von der Culling-Maske durchgelassenen Renderer skaliert) und SUBMIT "
                + "(onPreRender→onPostRender: die Draw Calls, einschließlich des Tiefenpasses, mit dem eine "
                + "Forward-Kamera ihre Tiefentextur baut, und jeder Shadow Map). \"Der Render Loop besitzt das "
                + "Bild\" sagt nicht, welche Hälfte, und die beiden haben verschiedene Hebel — also entscheidet "
                + "dieser Eintrag es, statt darüber zu streiten. Kostet eine zusätzliche Stopwatch-Lesung und "
                + "ein Camera.onPreRender-Abo pro Kamera-Rendering; AUS meldet den Callback vollständig ab, "
                + "statt innen auf null zu prüfen, und die SPLIT-Zeile druckt dann wie bisher den kombinierten "
                + "Wert pro Kamera.",
            // ---- [Optimize] ----
            ["Optimize/CacheTickDelegates"] =
                "Hält die an TickGuard.Run übergebenen Action-Delegaten im Cache, statt sie jeden Frame neu "
                + "aus einer Instanz-Methodengruppe zu erzeugen. Reine Arbeitsersparnis — identisches "
                + "Verhalten, nur ohne ~7 Delegat-Allokationen pro Frame, die den gen0-Collector füttern, der "
                + "die Ruckler bei Kopfdrehungen verursacht. AUS stellt die alte Allokation pro Frame wieder "
                + "her (nur für A/B).",
            ["Optimize/MapIconCache"] =
                "Cacht den Icon-Scan der Kampagnenkarte. Das Icon-Zeichnen läuft aus Camera.onPreCull — "
                + "einmal PRO RENDERNDER KAMERA PRO FRAME, die Stereo-Leinwand hat zwei oder drei — und "
                + "wiederholte bisher jedes Mal ein szenenweites FindObjectOfType, zwei allokierende "
                + "Komponentendurchläufe sowie je Decal ein GetComponent, einen frischen MaterialPropertyBlock "
                + "und einen langen Diagnosetext. Die MENGE der Decals ändert sich nur mit dem Kartenzustand, "
                + "wird also nur im Intervall gescannt; jede Icon-Pose wird weiter live pro Frame gelesen — "
                + "Schieben und Zoomen sind pixelidentisch. AUS stellt den Scan pro Frame wieder her (A/B).",
            ["Optimize/FigureScanCache"] =
                "Überspringt Komponentendurchläufe pro Frame, die der Figuren-Greiftreiber nicht braucht: "
                + "eine bereits übernommene Figur wird nicht mehr in jedem Frame per GetComponentInChildren neu "
                + "aufgelöst, und der Ring-Unterdrücker nimmt seinen \"nichts wird gehalten\"-Ausstieg, BEVOR "
                + "er die Enumeratoren allokiert, die er durchlaufen hätte. Verhaltensgleiche Arbeitsersparnis; "
                + "AUS stellt die bedingungslosen Durchläufe wieder her.",
            ["Optimize/LeanLogStrings"] =
                "BAUT keine Diagnosetexte, die das Log anschließend wegwirft. Mehrere Diagnosen werden erst "
                + "im Aufgerufenen gedrosselt oder auf Änderung gefiltert, sodass die interpolierte Meldung "
                + "(plus der Zugriff auf UnityEngine.Object.name, der bei jedem Lesen einen frischen String "
                + "allokiert) in jedem Frame bezahlt wurde, während nur eine von hunderten Zeilen ausgegeben "
                + "wurde. Die Filter sitzen jetzt vor der String-Arbeit statt dahinter. Die Log-Ausgabe ist so "
                + "oder so identisch.",
            ["Optimize/TooltipScanGate"] =
                "Koppelt die Arbeit des Welt-Tooltip-Subsystems pro Frame daran, ob überhaupt ein Tooltip "
                + "angezeigt wird: bisher lief der vollständige Durchlauf des Canvas-Teilbaums vor dieser "
                + "Prüfung, und die Ersatzsuche nach dem CanvasManager des Spiels wiederholte in jedem Frame "
                + "ein szenenweites FindObjectOfType, solange sie erfolglos blieb. Identische Tooltips, weit "
                + "weniger Scans.",
            ["Optimize/FanRelayoutMinInterval"] =
                "Mindestabstand in Sekunden zwischen zwei BLICKGESTEUERTEN Neuaufbauten des offenen "
                + "Kartenfächers. 0 = Neuaufbau in jedem Frame, in dem der Blick-Filter auslöst — heutiges "
                + "Verhalten und genau das, was eine schnelle Kopfdrehung in jedem einzelnen Frame auslöst. Ein "
                + "kleiner Wert (0.02 = 50 Hz) entfernt die überflüssigen Neuaufbauten, die eine Anzeige über "
                + "72 Hz anfordert, ohne sichtbare Änderung, weil das Home-Lerp jeder Karte glättet, was der "
                + "Filter durchlässt. Betrifft nur den BLICK-Filter — Änderungen am Kartensatz, Hovern, "
                + "Herausziehen und das Aufklappen bauen immer sofort neu auf.",
            ["Optimize/WallFadeEvalInterval"] =
                "Mindestabstand in Sekunden zwischen zwei SICHTBARKEITS-Auswertungen der Wand-Durchsicht (der "
                + "Abtastdurchlauf pro Segment). 0 = jeden Frame, heutiges Verhalten. Die Auswertung speist "
                + "ohnehin einen Schmitt-Trigger mit Verweilhysterese im Sekundenbereich, deshalb kann eine "
                + "Abtastung mit z. B. 0.05 (20 Hz) nicht ändern, welche Wände ausblenden — sie hört nur auf, "
                + "eine bewusst träge Entscheidung ständig neu zu treffen. Ohne Wirkung, solange [Compat] "
                + "WallFade nicht an ist.",
            ["Optimize/InitiativeDepthEvalInterval"] =
                "Mindestabstand in Sekunden zwischen zwei TIEFEN-NORMALISIERUNGEN der angedockten "
                + "Initiativreihe — dem Durchlauf, der den Unterbaum jedes aktiven Porträts abläuft und die "
                + "vom Spiel angelegte Tiefenstaffelung der Reihe auf [WorldUI] InitiativeDepthMaxSpreadPx "
                + "begrenzt. 0 = jeden Frame, heutiges Verhalten; die gemessenen Kosten dieses Durchlaufs "
                + "steigen mit der Zahl der Figuren in der Runde. Der Durchlauf ist IDEMPOTENT und leitet "
                + "jedes Ziel erneut aus dem gespeicherten Original-z ab, deshalb kann ein Takt von z. B. "
                + "0.05 (20 Hz) nicht ändern, wo ein Porträt landet — er verzögert höchstens um dieses "
                + "Intervall, wann ein NEU eingereihtes Porträt zum ersten Mal flachgelegt wird.",
            ["Optimize/QuietDiagnostics"] =
                "Unterdrückt die hochfrequenten DIAGNOSE-Logzeilen einzelner Subsysteme (den "
                + "\"diag:\"-Durchlauf der Wandüberblendung, den \"Fan depth-curve:\"-Rekorder des "
                + "Kartenfächers, die uGUI-Spur pro Klick). Standardmäßig AUS, weil diese Zeilen die "
                + "Beweisgrundlage mehrerer offener Untersuchungen sind und auf Hardware deutlich unter einer "
                + "Zeile pro Sekunde gemessen wurden — schalte sie AN für eine saubere Leistungsaufzeichnung, "
                + "in der nur die [Perf]-Zeilen zählen.",
            ["Optimize/RemoteContentInterval"] =
                "Überschreibt das Auffrischungsintervall (Sekunden) des Inhalts-Scans der Boards der "
                + "MITSPIELER — der 4-Hz-Durchlauf, der im Mehrspieler die Board-Inhalte der anderen Spieler "
                + "neu aufbaut. 0 = den eigenen Takt des Subsystems von 0.25 s belassen. Ein höherer Wert (z. "
                + "B. 0.5) halbiert die Kosten dieses Durchlaufs; er verzögert nur, wie schnell die "
                + "Board-Inhalte eines Mitspielers nachziehen, und tut im Einzelspieler überhaupt nichts.",
            ["Optimize/HeadDepthPrepass"] =
                "Behält DepthTextureMode.Depth der Kopfkamera. AN ist das heutige Verhalten und es ist NICHT "
                + "umsonst: auf dem Built-in-FORWARD-Pfad (den die Kopfkamera des Mods nutzt) baut Unity "
                + "_CameraDepthTexture, indem es die gesamte opake Szene ein ZWEITES Mal durch den "
                + "Shadow-Caster-Pass jedes Shaders rendert — eine volle zusätzliche Szenenabgabe PRO "
                + "AUGEN-PASS, also vier volle Abgaben pro Frame unter MultiPass statt zwei. Das ist das größte "
                + "einzelne Stück Abgabevolumen, das der MOD selbst hinzufügt, und die Messung von 2026-07 "
                + "sagt, dass das Abgabevolumen die Wand ist. AUS halbiert es. Was AUS kostet: die VFX-Shader "
                + "des Spiels (Fackelflammen, Glow-Billboards, DFade-Wolken) blenden weich gegen diese "
                + "Tiefentextur aus, und ohne sie schlägt die Überblendung OFFEN fehl — Glow rendert wieder "
                + "glatt durch dünne Wände, also genau der Fehler, für den dieser Modus eingeführt wurde. Das "
                + "ist somit ein echter Kompromiss und keine kostenlose Arbeitsersparnis, und es steht "
                + "standardmäßig auf dem heutigen Verhalten. Schalte es aus der Debug-Einstellungstafel um, "
                + "damit das [Perf]-Messfenster genau auf der Grenze schließt und die beiden SPLIT-Zeilen "
                + "vergleichbar sind.",
            ["Optimize/HeadCullingMaskDrop"] =
                "Ebenen, die die Kopfkamera NICHT rendern darf, als kommagetrennte Liste von Ebenen-NAMEN "
                + "oder -Indizes (z. B. 'Water, 14'). Leer = nichts weglassen, das ist das heutige Verhalten "
                + "und die normale Maskenpolitik (Maske der Ankerkamera | die Mod-Ebene). Jede hier entfernte "
                + "Ebene ist ein Stück Szene, das nicht mehr gecullt UND nicht mehr abgegeben wird, einmal pro "
                + "Augen-Pass — aber welche Ebenen gefahrlos wegfallen können, ist eine MESSUNG und nichts, was "
                + "man nachschlagen kann, denn es hängt vom Szenario ab und davon, was die Ankerkamera des "
                + "Spiels gerade mitführte. Die [Perf] SCENE-Zeile druckt jede Ebene mit Namen, ihrer "
                + "Renderer-Zahl und der Angabe, ob die Kopfkamera sie derzeit rendert: lies die Namen aus dem "
                + "Log ab, lass eine weg, vergleiche den Render-Loop-Split. Unbekannte Namen werden gemeldet "
                + "und ignoriert, nie stillschweigend angewendet — ein Tippfehler kann die Sicht also nicht "
                + "leeren. Wird in jedem Frame neu gesetzt, deshalb stellt das Leeren des Eintrags die normale "
                + "Maske sofort wieder her.",
            ["Optimize/HeadMaskFromScenarioCamera"] =
                "Speist im SZENARIO die Culling-Maske der Kopfkamera aus der spieleigenen ScenarioCamera "
                + "statt aus der Ankerkamera. Warum es das gibt: der Szenario-Anker löst sich zu 'Main Camera' "
                + "auf, deren Maske 0xFFFFFFFF ist — ALLE 32 Ebenen —, während die Kamera, mit der das flache "
                + "Spiel den Dungeon tatsächlich rendert, 0x700FFF17 trägt und dreizehn davon absichtlich "
                + "ausschließt. Die Kopfkamera cullt und übergibt also einen Überschuss, den das Spiel nie "
                + "zeichnet, zweimal pro Frame unter MultiPass. AUS (Standard) behält die Pauschalmaske, die "
                + "aus gutem Grund die sichere ursprüngliche Wahl war: die Kopfkamera rendert zu Recht Dinge, "
                + "die die ScenarioCamera nie gerendert hat — die Hände des Mods, Karten, das Kontrollbrett, "
                + "Remote-Avatare und die umgewandelte Welt-UI. Die Mod-Ebene und die UI-Ebene werden deshalb "
                + "bedingungslos WIEDER HINZUGEFÜGT und können von diesem Schalter nie weggelassen werden; "
                + "alles andere, was sich als nötig herausstellt, wird schlicht unsichtbar — darum steht dies "
                + "standardmäßig auf aus und darum benennt das Log jede Ebene, die es weglässt, mit ihrer "
                + "Renderer-Zahl, in dem Moment, in dem es sie weglässt. Lies zuerst die Aufschlüsselung pro "
                + "Ebene in der [Perf] SCENE-Zeile: eine Ebene ohne Renderer kostet nichts, wenn man sie "
                + "behält, und bringt nichts, wenn man sie weglässt. Wirkt live; Zurückschalten stellt die "
                + "Pauschalmaske sofort wieder her.",
            // ---- [MixedReality] ----
            ["MixedReality/Enabled"] =
                "Mixed-Reality-Modus (Chroma-Key-Passthrough). AN färbt sich Himmel/Hintergrund des ganzen "
                + "Spiels flächig in KeyColor und jede Skybox wird abgeschaltet, damit Virtual Desktop (oder "
                + "ein beliebiger Compositor) diese Farbe per Chroma-Key ausstanzen und das Diorama bzw. den "
                + "Tisch schwebend über deinem echten Zimmer zeigen kann. Die 3D-Geometrie wird weiter "
                + "gerendert — nur der Himmel wird zur flächigen Key-Farbe. Schwebende Mod-Texte und "
                + "-Fenster bekommen bei aktivem MR zusätzlich undurchsichtige Rückplatten, damit sie "
                + "über dem echten Zimmer lesbar bleiben. Beim Ausschalten wird alles (inklusive der "
                + "Platten) vollständig wiederhergestellt.",
            ["MixedReality/KeyColor"] =
                "Die flächige Chroma-Key-Farbe, auf die Himmel/Hintergrund im Mixed-Reality-Modus geleert "
                + "werden (Standard reines Grün, RGBA 0,1,0,1). Die VR-Einstellungstafel schaltet der Reihe "
                + "nach durch die Vorgaben Grün / Magenta / Blau; hier ist jedes RGBA erlaubt.",
            ["MixedReality/HideSkyMeshes"] =
                "Schaltet bei aktivem MR zusätzlich die Himmel-/Hintergrund-GEOMETRIE ab. Der "
                + "Szenario-Hintergrund ist ein undurchsichtiges Mesh (keine Skybox); nur das Kamera-Clear "
                + "einzufärben lässt ihn weiter über der Key-Farbe stehen. MR durchsucht daher die Renderer und "
                + "schaltet die ab, die den umgebenden Himmel zeichnen (himmelartiger Name/Material oder "
                + "Bounds, die den Kopf auf allen Achsen umschließen — nie die Dioramaplatten/Requisiten), und "
                + "stellt sie beim Ausschalten von MR wieder her. Nur AUS, wenn ein Durchlauf zeigt, dass "
                + "gewollte Geometrie versteckt wird — das Log nennt jeden abgeschalteten Renderer.",
            ["MixedReality/OpaquePreviewTiles"] =
                "TEIL VON MIXED REALITY, keine Wahl daneben (wie HideSkyMeshes; nicht im VR-Menü). SÄMTLICHE "
                + "durchscheinende 'Unseen'-Geometrie des Spiels — die verdeckten Kachel-STAPEL noch nicht "
                + "entdeckter Räume UND die Hexfelder, die das unentdeckte Gebiet hinter Türen markieren — "
                + "mischt sich mit dem, was hinter ihr liegt; über der dunklen Leere des Spiels fällt das "
                + "nicht auf, aber in MR scheint die Key-Farbe / das echte Zimmer hindurch und alles wirkt "
                + "wie grünes Glas. Solange MR an ist, findet der Durchlauf diese Renderer (die "
                + "'Unseen'-Shader-Familie plus alles Durchscheinende unter dem aktiven 'Preview'-Teilbaum "
                + "einer Map-Kachel) und schiebt UNTER jeden eine OPAKE dunkle Rückplatte — das gestaltete "
                + "durchscheinende Material rendert exakt weiter wie entworfen, Look und Animation bleiben "
                + "unangetastet, es mischt sich nur gegen Dunkel statt gegen dein Zimmer. Die Rückplatten "
                + "werden beim Ausschalten von MR zerstört — der normale Modus bleibt unberührt. Nur AUS, "
                + "wenn ein Durchlauf zeigt, dass gewollte Geometrie verdunkelt wird — das Log nennt, was "
                + "hinterlegt wurde.",
            ["MixedReality/UnseenSkirtScale"] =
                "Verbreiterung der RILLEN-FÜLLUNG jedes 'Unseen'-Stücks relativ zu seiner Geometrie (1 = "
                + "exakte Silhouette). Jedes Fog-of-War-Stück bekommt in MR ZWEI dunkle Rückplatten: eine "
                + "exakte Kopie direkt hinter seinen Flächen und eine abgesenkte Füll-Kopie, die die "
                + "abgeschrägten Rillen ZWISCHEN benachbarten Hexfeldern verschließt — dieser Faktor "
                + "verbreitert nur diese Füllung, damit sich die Füllungen der Nachbarn unter der "
                + "Rillenlinie überlappen. Wirkt bei aktivem MR, live (Rückplatten werden bei Änderung "
                + "neu gebaut). Erhöhen, wenn Rillen zwischen den Hexfeldern weiter leuchten; "
                + "verringern, wenn Dunkel über die äußersten Hexkanten hinausragt. Begrenzt auf 1..2.",
            ["MixedReality/UnseenWaferDrop"] =
                "Wie weit (Welteinheiten) die flache Fugen-RÜCKPLATTE jedes 'Unseen'-Stücks in MR "
                + "unter dessen OBERKANTE sitzt. Die Rückplatte ist eine plattgedrückte, leicht "
                + "verbreiterte dunkle Kopie des Stücks, die die Fugen ZWISCHEN benachbarten "
                + "Hexfeldern direkt unter deren Oberkante ausfüllt — der Blick in eine Fuge trifft "
                + "auf Dunkel statt auf das durchscheinende Zimmer, während die animierte Kante "
                + "darüber weiterspielt. (Nachfolger des ausgemusterten Schlüssels UnseenFillDrop, "
                + "dessen gespeicherter Tiefen-Füllwert nicht mehr zu dieser Bedeutung passte.) "
                + "Wirkt bei aktivem MR, live (Rückplatten werden bei Änderung neu gebaut). Erhöhen, "
                + "wenn die Rückplatte mit den Hex-Oberseiten flimmert; Richtung 0.01 senken, wenn "
                + "bei flachen Winkeln weiter grüne Fugen zu sehen sind. Begrenzt auf 0..2.",
            ["MixedReality/UnseenRimInset"] =
                "Wie weit (Welteinheiten) der dunkle RAND-VORHANG in MR INNERHALB der senkrechten "
                + "Seitenflächen jedes 'Unseen'-Stücks sitzt. Der Vorhang ist ein vom Mod GEBAUTES "
                + "dunkles Prisma, das der Hex-Kontur des Stücks folgt und direkt hinter dessen "
                + "Seitenflächen steht (seiner HÖHE — der äußeren 'Kante' des Fog-of-War-Gebiets), "
                + "damit der Blick auf den Gebietsrand auf Dunkel trifft statt auf das durchscheinende "
                + "Zimmer. Gebaut statt kopiert, weil die Kachel-Meshes des Spiels nicht CPU-lesbar "
                + "sind: eine Kopie würde das gestaltete Vertex-Alpha dieser Seitenflächen erben — "
                + "genau der Grund, warum zwölf Runden gleich-Mesh-Rückplatten sie nie abgedeckt haben. "
                + "Wirkt bei aktivem MR, live (Rückplatten werden bei Änderung neu gebaut). Erhöhen, "
                + "wenn an einer beschädigten/eingekerbten Kante Dunkel hervorsteht; Richtung 0.01 "
                + "senken, wenn die Außenränder weiter leuchten. Begrenzt auf 0.005..0.2.",
            ["MixedReality/UnseenRimTopClearance"] =
                "Wie weit (Welteinheiten) die Oberkante des RAND-VORHANGS in MR UNTER der Oberkante "
                + "jedes 'Unseen'-Stücks bleibt. Das ist die Garantie, dass der Vorhang niemals eine "
                + "gestaltete Hex-Oberseite oder deren Animation übermalt: er wird immer unter der "
                + "(breiteren) Fugen-Rückplatte gehalten und ist von oben vollständig hinter einer "
                + "bereits dunklen Fläche verborgen. Erhöhen, falls jemals Dunkel auf einer "
                + "Hex-Oberseite erscheint; Richtung Rückplatten-Abstand senken, wenn der oberste "
                + "Saum des Außenrands noch leuchtet. Wird auf mindestens UnseenWaferDrop + 0.005 "
                + "erzwungen. Wirkt bei aktivem MR, live (Rückplatten werden bei Änderung neu "
                + "gebaut). Begrenzt auf 0.005..0.5.",
            ["MixedReality/UnseenRegionMembership"] =
                "TEIL VON MIXED REALITY, keine Wahl daneben (wie HideSkyMeshes; nicht im VR-Menü). "
                + "Gibt auch jedem Teil eine dunkle Rückplatte, das einfach IM Fog-of-War-Gebiet "
                + "STEHT — unterhalb der Oberkante der unentdeckten Kacheln — selbst wenn sein "
                + "Material für den Mod nicht durchscheinend aussieht. Die Kacheln des unentdeckten "
                + "Gebiets bestehen aus mehreren Meshes pro Hexfeld, und das höchste davon (der "
                + "Block, der die äußere KANTE des Gebiets bildet) verrät nichts, was der Mod "
                + "erkennen könnte: kein 'Unseen' im Namen, keine sichtbare Transparenz-Mischung, "
                + "keine Transparenz-Renderreihenfolge. Genau dessen senkrechte Flächen ließen "
                + "weiterhin das Zimmer durchscheinen. Statt aus Materialien zu raten, fragt diese "
                + "Option, WO ein Teil steht. Figuren werden nie angefasst, nichts was AUF den "
                + "Kacheln steht wird angefasst, und alles, was deutlich größer als ein Hexfeld "
                + "ist, wird abgelehnt. Nur AUS, wenn ein Durchlauf zeigt, dass gewollte Geometrie "
                + "verdunkelt wird — das Log nennt alles, was hinterlegt wurde.",
            ["MixedReality/UnseenBackingDebugColors"] =
                "DIAGNOSE, standardmäßig aus — nur einschalten, wenn nach einem Screenshot gefragt "
                + "wird. In MR bekommt jedes Fog-of-War-Stück drei vom Mod gebaute dunkle "
                + "Rückplatten (eine Kopie direkt hinter seinen Flächen, eine flache Scheibe knapp "
                + "unter seiner Oberkante und ein Prisma hinter seinen äußeren Seitenflächen). Mit "
                + "dieser Option AN werden sie statt dunkel in flachen Signalfarben gezeichnet — "
                + "Kopie BLAU, Scheibe MAGENTA, Seitenprisma ROT — bei sonst völlig unveränderter "
                + "Form, Position und Zeichenreihenfolge. Ein Foto zeigt dann, welche der "
                + "Mod-Flächen tatsächlich bei dir ankommen und wo genau sie sitzen — das Einzige, "
                + "was eine dunkle Rückplatte niemals zeigen kann. Währenddessen die GRÜNE "
                + "Key-Farbe benutzen, damit keine Signalfarbe weggekeyt wird. Wirkt bei aktivem "
                + "MR, live (Rückplatten werden bei Änderung neu gebaut); beim Ausschalten kehrt "
                + "sofort der normale dunkle Look zurück.",
            // ---- [Sky] ----
            ["Sky/Style"] =
                "In welcher Umgebung du spielst (Nutzer-Entscheide 2026-08-12/13: die "
                + "Umgebung rendert NUR im Szenario, so wie es die originale "
                + "Standard-Umgebung auch macht — niemals im Menü; sie besteht aus EIGENEN "
                + "Inhalten des Mods im malerischen Stil des Spiels). Default = der eigene "
                + "animierte Himmel des Spiels, exakt wie bisher. Cellar = ein "
                + "kerzenbeleuchteter Steinkeller; SwampNight = eine mondbeschienene "
                + "Sumpflichtung unter einer Sternenkuppel mit Sternschnuppen, Bodennebel "
                + "und Glühwürmchen. Eine andere Wahl als Default blendet im Szenario die "
                + "Himmelskugel des Spiels aus und baut die Umgebung als FESTEN ORT UM DAS "
                + "SPIELBRETT, wobei das Brett ein KLEINES SPIELFELD IST, DAS IN DER MITTE "
                + "EINES VIEL GRÖSSEREN ORTES SCHWEBT — wie ein Tisch mit Spielfiguren in "
                + "einem Raum, niemals ansatzweise so groß wie die Umgebung. Die offene "
                + "Fläche, in der du stehst — die Waldlichtung, der Kellerinnenraum —, ist "
                + "ein Mehrfaches der Brettbreite, damit alles, woraus die Umgebung "
                + "besteht, Bäume und Wände eingeschlossen, deutlich außerhalb des "
                + "Spielfelds bleibt; und das Brett schwebt um eine brettproportionale Höhe "
                + "über dem Boden, statt in ihm zu stecken. DAS BRETT "
                + "BEWEGT SICH NIEMALS IM RAUM. Zoomen skaliert die ganze Szene — Brett "
                + "und Raum gemeinsam, im gleichen Verhältnis: weit herausgezoomt wirkt "
                + "der Ort wie ein Modell vor dir, hereingezoomt stehst du darin, und das "
                + "Brett schwebt bei jedem Zoom exakt gleich. Das "
                + "Brett kann nicht mehr unter dem Boden landen. Stick-Flug, Drehen, das "
                + "Welt-Greifen und körperliches Gehen bewegen dich alle durch den Ort "
                + "hindurch; keines davon setzt ihn je neu. Nur der Himmel selbst wird neu "
                + "um dich herum gesetzt, und das ausschließlich durch die Recenter-Geste "
                + "(B+Y) oder einen Rig-Neuaufbau — ein erneutes Auswählen des Stils baut "
                + "alles neu. Sie kann den Laser niemals abfangen (keine Collider, nur "
                + "Mod-Layer). Wirkt sofort aus dem VR-Menü, greift sobald ein Szenario "
                + "läuft. MIXED REALITY GEWINNT IMMER: solange MR an ist, ist jeder Himmel "
                + "und jede Umgebung aus, damit der Chroma-Key dein Zimmer zeigen kann; "
                + "die Wahl greift wieder, sobald MR ausgeht. Werte aus den alten "
                + "Panorama-Builds (Night/Sunset) gibt es nicht mehr, sie fallen auf "
                + "Default zurück. Rein lokale Darstellung, wird nie mit Mitspielern "
                + "synchronisiert.",
            // ---- [Stereo] ----
            ["Stereo/RenderMode"] =
                "OpenXR-Stereo-Rendermodus, wird beim Erzeugen der XR-Session angewandt (erfordert einen "
                + "Spielneustart). MultiPass rendert die Szene einmal pro Auge und ist der EINZIGE Modus, der "
                + "in diesem Spiel korrekt rendert. SinglePassInstanced würde die Kosten des Szenendurchlaufs "
                + "etwa halbieren, aber die ausgelieferten Shader dieses Spiels enthalten keine "
                + "Stereo-Varianten (per Disassembly aus resources.assets belegt — siehe tools/ShaderDisasm/), "
                + "die Bundle-Shader des Mods ebenfalls nicht, und der Stereo-Flatscreen des Mods hängt am "
                + "Vertrag von zwei Durchläufen pro Bild — das Ergebnis ist ein schwarzes oder doppeltes "
                + "rechtes Auge plus Mono-Menüs. Angeboten zum Testen eines künftigen stereo-fähigen "
                + "Shader-Bundles, nicht als Leistungseinstellung.",
            // ---- [WallFade] ----
            ["WallFade/OnFraction"] =
                "Eine Wand wird ausgeblendet, sobald sie mindestens diesen (EMA-geglätteten) Anteil der im "
                + "Sichtkegel liegenden BODEN-Stichproben (Hexfeld-Ebene) eines Raums verdeckt — 0.25 = die "
                + "Wand verdeckt 25% des Bodens, den du gerade ansiehst (obere Schwelle des Schmitt-Triggers). "
                + "Live änderbar; begrenzt auf 0.05-0.95.",
            ["WallFade/OffFraction"] =
                "Ist die Wand einmal ausgeblendet, bleibt sie es, solange der geglättete Anteil der "
                + "Bodenabdeckung auf oder über diesem Wert liegt (untere Schwelle des Schmitt-Triggers). Live "
                + "änderbar; begrenzt auf 0.01-0.95 und nie über OnFraction.",
            ["WallFade/ExitDwellMovedSeconds"] =
                "Sekunden, die der Anteil unter OffFraction bleiben muss, bevor die Wand wieder erscheint — "
                + "und zwar dann, wenn sich die PERSPEKTIVE kürzlich geändert hat (echte Kopfbewegung / Welt "
                + "greifen / neu zentrieren). Live änderbar.",
            ["WallFade/ExitDwellStationarySeconds"] =
                "Wartezeit bis zum Wiedererscheinen, solange sich der Kopf zuletzt nur GEDREHT hat — eine "
                + "Drehung allein soll eine Wand fast nie zurückbringen. Live änderbar; nie unter "
                + "ExitDwellMovedSeconds.",
            ["WallFade/StackedShellFade"] =
                "Blendet Festungs-/Burg-Aufbauten mit ihrer Mauer aus: Meshes OHNE Ausblende-Shader, die "
                + "direkt auf einem erkannten Mauerzug aufsitzen (Zinnen, Obergeschosse), zählen zur "
                + "Verdeckungs-Box dieser Mauer und verschwinden bzw. erscheinen mit deren Ausblendung — "
                + "ohne diese Option bleibt eine mehrstöckige Festung komplett massiv, weil nur ihr unterstes "
                + "Geschoss echte Mauer-Geometrie ist. AUS = Originalanblick solcher Aufbauten. Live (greift "
                + "beim nächsten 2-Sekunden-Rescan).",
            ["WallFade/SyncPeerFades"] =
                "Mehrspieler: Wände, die bei einem MITSPIELER ausgeblendet sind, verschwinden auch bei dir "
                + "(und kehren zurück, wenn sie es bei ihm tun) — mit derselben Animation wie deine eigenen "
                + "Wand-Ausblendungen. Empfänger-Einstellung: Die eigenen Ausblendungen werden immer "
                + "gesendet; jeder Spieler entscheidet mit diesem Schalter nur, was ER sieht — Umschalten "
                + "mitten in der Sitzung braucht keine Neuverhandlung. Live änderbar.",
            // ---- [Comfort] ----
            ["Comfort/WorldGrabEnabled"] =
                "Tisch-Manipulation über den Griff: ein Griff (abseits greifbarer Objekte) zieht den Tisch, "
                + "zwei Griffe drehen und skalieren ihn per Pinch-Griff. Bewegt nur das VR-Rig, nie die "
                + "Spielwelt.",
            ["Comfort/FreeMovement"] =
                "Völlig freie Diorama-Bewegung: das Ziehen mit einem Griff bewegt den Tisch in JEDE Richtung "
                + "(auch senkrecht hoch/runter, ohne Kopf-über-Tisch-Begrenzung, ganz ohne Positionsgrenzen), "
                + "und der Pinch-Skalierbereich beträgt mindestens 0.1x-12x der Grundskalierung. Ausschalten "
                + "stellt die alten Komfort-Begrenzungen wieder her (nur waagerechtes Ziehen, sofern nicht "
                + "VerticalDrag, Kopf bleibt über dem Tisch, eingestelltes ScaleMin/ScaleMax). Neu zentrieren "
                + "(B+Y halten) bringt dich von überall an die Tischkante zurück.",
            ["Comfort/VerticalDrag"] =
                "Erlaubt dem Ziehen mit einem Griff, den Tisch auch senkrecht zu bewegen. Aus = nur "
                + "waagerechte Ebene. Wird ignoriert (immer an), solange FreeMovement aktiv ist.",
            ["Comfort/RotateEnabled"] =
                "Die Zwei-Griff-Geste dreht den Tisch um den Punkt zwischen deinen Händen (nur Gieren).",
            ["Comfort/ScaleEnabled"] =
                "Der Zwei-Griff-Pinch skaliert den Tisch (Hände auseinander = Board wird größer).",
            ["Comfort/ScaleMin"] =
                "Untere Grenze der Pinch-Skalierung als Vielfaches der Grund-WorldScale. Solange FreeMovement "
                + "an ist, liegt die wirksame Untergrenze unabhängig von diesem Wert bei höchstens 0.1x.",
            ["Comfort/ScaleMax"] =
                "Obere Grenze der Pinch-Skalierung als Vielfaches der Grund-WorldScale. Solange FreeMovement "
                + "an ist, liegt die wirksame Obergrenze unabhängig von diesem Wert bei mindestens 12x.",
            ["Comfort/TurnMode"] =
                "Drehen per Thumbstick: Snap = stufenweise Schritte, Smooth = stufenlos, Off = aus. Im "
                + "Ziel-Modus auf dem Spielbrett nie aktiv (dort dreht der Stick die AoE-Muster).",
            ["Comfort/SnapTurnDegrees"] =
                "Gierwinkel in Grad pro Schritt beim stufenweisen Drehen (typisch 30 oder 45).",
            ["Comfort/SmoothTurnSpeed"] = "Geschwindigkeit des stufenlosen Drehens in Grad pro Sekunde.",
            ["Comfort/TurnHand"] = "Welcher Thumbstick dreht. Dominant folgt [Hands] PrimaryHand.",
            // Comfort/TableHeightOffset ist ENTFALLEN (Nutzer-Entscheid 2026-08: durch das freie
            // Bewegen — Stick-Flug und Welt-Greifen — wird die Tischhöhe nicht mehr gebraucht).
            // Kein Eintrag mehr nötig: der Schlüssel wird nirgends mehr gebunden.
            ["Comfort/RecenterHoldSeconds"] =
                "Die obere Taste (B + Y) an BEIDEN Controllern so viele Sekunden halten, um am Tisch neu zu "
                + "zentrieren. 0 schaltet die Tastenkombination ab.",
            ["Comfort/SavedScaleMultiplier"] =
                "Letzter Pinch-Skalierungsfaktor relativ zur Grund-WorldScale (die \"Tischgröße\" in der "
                + "VR-Einstellungstafel). Wird nach jeder Zwei-Griff-Skaliergeste automatisch geschrieben und "
                + "beim Neuaufbau des Rigs wieder angewandt. Standard 2.5 — die automatische Grundskalierung "
                + "wirkt wie ein riesiges Diorama; 2.5x schrumpft sie beim ersten Erscheinen auf eine angenehme "
                + "Tischgröße (Nutzerwunsch: Standard-Tischgröße ~2.5).",
            ["Comfort/KeepPlaceOnReorigin"] =
                "Bleib dort, wo du warst, wenn die VR-Laufzeit ihren Tracking-Ursprung unter dem "
                + "Spiel verschiebt — genau das passiert meist, wenn du die Brille absetzt und wieder "
                + "aufsetzt, und deshalb stehst du danach woanders. Der Mod erkennt die Verschiebung "
                + "(der Kopf springt in EINEM Frame weiter, als ein Hals sich bewegen kann), wartet "
                + "ein paar Frames zur Sicherheit ab, ob es nur ein Tracking-Aussetzer war, und "
                + "verschiebt das Rig um denselben Betrag zurück — dein Kopf steht danach wieder "
                + "exakt an der Stelle und in der Blickrichtung von vorher. Weltverankerte Dinge "
                + "werden dabei NICHT mitbewegt: du warst ja nie woanders. Aus = der Ursprung der "
                + "Laufzeit gewinnt (bisheriges Verhalten).",
            ["Comfort/DebugGizmos"] =
                "Zeigt das Komfort-Debug-Overlay (Zustand von Welt greifen, Skalierungsfaktor, Status der "
                + "Begrenzungen).",
            ["Comfort/TableScaleDefault25Applied"] =
                "Interne einmalige Migrationsmarke: Die 2.5x-Standard-Tischgröße wurde dieser "
                + "Konfigurationsdatei angeboten. Nicht bearbeiten.",
            // ---- [RenderQuality] ----
            ["RenderQuality/MsaaLevel"] =
                "Hardware-MSAA-Sampleanzahl für das VR-Augenrendering (0 = aus, 2/4/8). Die spieleigene "
                + "Kantenglättung steckt im PostProcessLayer, den der Mod abschaltet, und die Qualitätsstufe "
                + "beim Start setzt antiAliasing 0 — ohne dies hat das Headset also GAR KEINE Kantenglättung "
                + "(flimmernde Kartenlinien / Brettkanten). Wird gegen die Qualitätsstufen-Wechsel des Spiels "
                + "jedes Bild neu durchgesetzt; wirkt sofort. Erfordert den Forward-Renderpfad ([Rig] "
                + "ForwardRendering, standardmäßig an) — der Deferred-Pfad ignoriert MSAA. KOSTET GPU-ZEIT: Es "
                + "vervielfacht Render-Target- und Resolve-Bandbreite eines Ziels, das die Runtime uns bereits "
                + "supersampled liefert, und das unter MultiPass zweimal pro Bild. Wäge es gegen "
                + "EyeResolutionScale weiter unten ab — beide bekämpfen Geometrie-Aliasing und ihre "
                + "Sampleanzahlen multiplizieren sich; 8x auf einem stark supersampelten Ziel ist die teure "
                + "Hälfte einer schon größtenteils erledigten Arbeit.",
            ["RenderQuality/ForceAnisotropic"] =
                "Erzwingt anisotrope Texturfilterung für ALLE Texturen (plus eine globale Aniso-Untergrenze). "
                + "Verringert das Flimmern in der Ferne auf flach zum Blick liegenden Texturen — "
                + "Kartenvorderseiten, Initiativ-Porträts, Brettgrafik. Reine Anhebung der Sampling-Qualität; "
                + "zum Wiederherstellen der Spieleinstellung ausschalten.",
            ["RenderQuality/EyeResolutionScale"] =
                "Renderauflösung pro Auge, relativ zu dem, was die OpenXR-Runtime anfordert (1 = wie "
                + "angefordert). DER wichtigste GPU-Regler: praktisch die gesamte Arbeit pro Pixel — Shading, "
                + "Rasterisierung, die MSAA-Flächen und deren Resolve — skaliert im QUADRAT dieses Werts. 0.7 = "
                + "etwa die halbe Pixelarbeit, 1.4 = etwa die doppelte. Beachte: die Anforderung der Runtime "
                + "ist selbst meist schon supersampelt (die Auflösungsregler von Virtual Desktop / SteamVR "
                + "sitzen darüber), unter 1 liegst du also oft noch über der Panelauflösung — die tatsächliche "
                + "Pixelzahl steht in der Zeile [Rig] EYE-TARGET DIAG. Über 1 ist der einzige Regler gegen "
                + "SHADER-/TEXTUR-Flimmern (Glanzfunkeln, Subpixel-Details), an das MSAA als "
                + "Geometriekanten-Glättung nicht herankommt; unter 1 werden Texturdetails weicher, bevor "
                + "Kanten weicher werden. Wirkt sofort.",
            ["RenderQuality/ViewportScaleFallback"] =
                "Wenn EyeResolutionScale die Größe der Augentextur nicht verändert (manche OpenXR-Anbieter "
                + "handeln die Swapchain einmal beim Sessionstart aus und ignorieren sie danach), wird auf "
                + "XRSettings.renderViewportScale ausgewichen: das rendert in einen Teilbereich der bestehenden "
                + "Swapchain und wird überall beachtet. Entschieden wird das durch ZURÜCKLESEN DER BELEGUNG, "
                + "nicht durch Raten, und die Zeile [Rig] EYE-TARGET DIAG nennt, welcher Hebel gegriffen hat. "
                + "Aus = es wird nur eyeTextureResolutionScale genutzt (bei so einem Anbieter tut der "
                + "Auflösungsregler dann stillschweigend nichts — nur für A/B).",
            ["RenderQuality/RebuildRigOnMsaaChange"] =
                "Baut das VR-Rig bei jeder Änderung der MSAA-Stufe ab und neu auf. Notausgang für "
                + "OpenXR-Anbieter, die Sampleanzahlen nur beim Sessionstart neu aushandeln. Die Frage, für die "
                + "dies ursprünglich geschrieben wurde — ob MSAA überhaupt greift —, ist beantwortet (es "
                + "greift; im Headset sichtbar wirksam), also ist dies kein Diagnosewerkzeug mehr. Verursacht "
                + "bei jeder MSAA-Änderung einen kurzen Reset der Ansicht; im normalen Spiel aus lassen.",
            // ---- [General] ----
            ["General/Enabled"] =
                "Hauptschalter. Auf false gesetzt läuft das Spiel völlig unverändert (der Mod tut nichts).",
            ["General/RuntimeOverride"] =
                "Optionaler Pfad zu einer OpenXR-Runtime-JSON-Datei (z. B. steamxr_win64.json von SteamVR). "
                + "Setzt XR_RUNTIME_JSON vor der XR-Initialisierung und wird zuerst probiert. Leer lassen für "
                + "automatische Erkennung (aktive Runtime aus der Registry, dann alle verfügbaren Runtimes, "
                + "dann bekannte Pfade).",
            // ---- [Core] ----
            ["Core/RuntimePriority"] =
                "Reihenfolge, in der OpenXR-Runtimes probiert werden. 'auto' = zuerst die "
                + "System-Standard-Runtime (worauf Betriebssystem/Registry zeigen), dann VDXR, wenn der Virtual "
                + "Desktop Streamer läuft, dann die übrigen installierten Runtimes, SteamVR zuletzt (ein "
                + "SteamVR-Versuch startet dessen Compositor). Oder eine kommagetrennte Liste aus: default, "
                + "vdxr, steamvr, oculus oder vollständigen Pfaden zu Runtime-JSON-Dateien — genau in dieser "
                + "Reihenfolge probiert.",
            ["Core/SkipRuntimeCandidates"] =
                "Notausstieg: nur ein einziger Init-Versuch auf der System-Standard-OpenXR-Runtime, und "
                + "XR_RUNTIME_JSON wird nie gesetzt (kein Failover über Kandidaten). Nutze das, wenn das "
                + "Failover selbst Ärger macht (z. B. wenn es ständig Runtimes startet, die du gar nicht "
                + "verwendest).",
            ["Core/EnableGraphicsJobs"] =
                "Lässt den Mod Unitys THREADED RENDER SUBMISSION für dich einschalten, indem er "
                + "gfx-enable-gfx-jobs und gfx-enable-native-gfx-jobs in GH_Data/boot.config "
                + "schreibt. Das ist der mit Abstand größte Leistungsfund des ganzen Projekts: Unity "
                + "setzt normalerweise jeden Zeichenaufruf auf EINEM Thread ab — demselben, der fertig "
                + "sein muss, bevor ein Bild angezeigt werden kann — und im Szenario war genau dieser "
                + "Thread der gesamte Engpass. Gemessen am 28.07.2026, gleiche Szene, gleicher Build, "
                + "nur diese eine Änderung: Renderloop auf dem Hauptthread 14,9 ms → 1,8 ms, "
                + "Kopfkamera 13,4 ms → 1,45 ms, Bildzeit 17,5 ms → 11,14 ms, und das Headset ging von "
                + "fest 45 Hz auf saubere 90 Hz. Das gemeldete Ghosting bei Kopfbewegung verschwand "
                + "vollständig. WIRKT ERST BEIM NÄCHSTEN SPIELSTART: die Engine liest boot.config, "
                + "bevor überhaupt Mod-Code existiert — deshalb kann der Mod das auch nicht zur "
                + "Laufzeit setzen. Die ursprüngliche boot.config wird vor der ersten Änderung nach "
                + "boot.config.gloomhavenvr-backup kopiert; auf false gesetzt schreibt der Mod die "
                + "Schlüssel beim nächsten Start wieder auf 0. Gibst du -force-gfx-jobs selbst in den "
                + "Startoptionen an, gewinnt das und die Datei bleibt unangetastet. Sollte das Spiel "
                + "je nicht mehr starten: die Sicherungskopie von Hand zurückkopieren — der Mod kann "
                + "dort nicht helfen, weil er gar nicht erst läuft.",
            ["Core/AutoRestartForGraphicsJobs"] =
                "Beim EINEN Start, an dem die Einstellung darüber neu geschrieben wird, schließt der "
                + "Mod das Spiel und startet es selbst neu — damit du die Leistung sofort hast, statt "
                + "aufgefordert zu werden, selbst neu zu starten. Das passiert einmal nach dem "
                + "Installieren oder Aktualisieren des Mods, dauert ein paar Sekunden und kann sich "
                + "nicht wiederholen: der neu gestartete Prozess ist als solcher markiert und startet "
                + "nicht noch einmal neu, und ein Zähler in BepInEx/patchers/GloomhavenVR/ begrenzt "
                + "das auf zwei Versuche, solange die Einstellung nicht greift. Es steht nichts auf "
                + "dem Spiel: das läuft während des Engine-Starts, bevor überhaupt ein Spielstand "
                + "oder eine Kampagne geladen ist. Auf false setzen, wenn du lieber selbst beendest "
                + "und neu startest; das Protokoll sagt dir dann Bescheid. Ohne jede Wirkung, wenn "
                + "EnableGraphicsJobs false ist, wenn du -force-gfx-jobs selbst angibst, oder sobald "
                + "boot.config die Einstellung schon hat — also bei jedem Start nach dem ersten.",
            ["Core/InitDelayFrames"] =
                "Notausstieg: verzögert die Mod-Initialisierung (samt OpenXR-Init) um so viele gerenderte "
                + "Frames. Manche Runtime/GPU-Kombinationen brauchen ein vollständig hochgefahrenes "
                + "Grafikgerät, bevor xrCreateSession funktioniert. 0 (Standard) = sofortige Initialisierung im "
                + "Awake des Plugins.",
            // ---- [Rig] ----
            ["Rig/SpawnInCircle"] =
                "Mehrspieler: Beim Beitreten zu einer Sitzung bzw. beim Betreten eines Szenarios wirst du "
                + "GEGENÜBER den bereits anwesenden Spielern abgesetzt — bei einem Mitspieler genau "
                + "gegenüber, mit direktem Blick auf seine Maske; bei mehreren in der breitesten freien "
                + "Lücke. So spawnt niemand mehr hinter oder in der Maske eines Mitspielers. Du stehst dabei "
                + "AM TISCHRAND dieser Seite (die tatsächliche Brettkante in dieser Richtung plus etwas "
                + "Standabstand), also weder über dem Spielfeld noch weit davor — bei jeder Brettgröße und "
                + "jedem Zoom. Die Platzierung passiert EINMAL bei der Ankunft — plus höchstens eine Korrektur "
                + "in den ersten Sekunden, falls direkt nach dir noch jemand auftaucht — und endet endgültig, "
                + "sobald du dich selbst bewegst. Rein lokal: es wird nichts zusätzlich über das Netzwerk "
                + "gesendet. Ohne Wirkung im Einzelspieler/Offline (der Solo-Platz bleibt unverändert). "
                + "Aus = alle Spieler behalten wie bisher denselben gemeinsamen Platz. Jeder Ausgang wird "
                + "protokolliert (platziert / wartet auf Mitspieler / noch kein Brett / aus).",
            ["Rig/Experimental3DMap"] =
                "RESERVIERT — DERZEIT NICHT UMGESETZTER Platzhalter für eine künftige Funktion: die "
                + "Kampagnen-/Weltkarte als kopfgetracktes 3D-Diorama erkunden statt auf der flachen "
                + "2D-Leinwand. Heute hat dieser Schalter KEINE Wirkung: alles vor einem echten Kampfszenario "
                + "(Kampagnenkarte, Guildmaster, Händler, Stufenaufstieg) bleibt bewusst in Menu2D auf dem "
                + "schwebenden Bildschirm, weil die Kartenszene nie für eine freie VR-Kamera gebaut wurde (Test "
                + "#8: riesige Karte unter dem Spieler, schwarzes flaches Fenster). Der Wunsch ist hier "
                + "festgehalten, damit er in eine spätere Phase überlebt.",
            ["Rig/WorldTiltDegrees"] =
                "ALT — ohne Wirkung (Funktion 2026-08 auf Nutzerwunsch GEPARKT: die Weltneigung machte zu "
                + "viele Probleme und ist vorerst abgeschaltet; sie kommt eventuell später wieder). Das war "
                + "die Weltneigung im Demeo-Stil in Grad (0-60): der gesamte Spielbereich erschien zu dir "
                + "hin geneigt, rig-seitig umgesetzt und mehrspielersicher. Der Wert bleibt erhalten, damit "
                + "ein eingestellter Winkel in dieser Datei überlebt, aber die Laufzeit erzwingt Neigung 0 "
                + "(VRRigDriver.WorldTilt.cs, TargetTiltDegrees). Wiederbelebung = diese eine Klammer "
                + "entfernen und die Menüzeile wiederherstellen.",
            ["Rig/MaskedReaimHeadRate"] =
                "ALT — ohne Wirkung, solange die Weltneigung geparkt ist (siehe [Rig] WorldTiltDegrees). "
                + "Nur bei Weltneigung. Wenn du dich körperlich mit Körper/Kopf drehst, wird die Richtung, in "
                + "die die Neigung kippt, unbemerkt auf deinen Blick nachgeführt — aber NUR, solange sich dein "
                + "Kopf schneller als diese Schwelle dreht (Grad pro Sekunde), sodass die Korrektur von deiner "
                + "eigenen Bewegung wahrnehmungsmäßig verdeckt wird (Redirected-Rotation-Technik). Unterhalb "
                + "der Schwelle bleibt die Welt exakt eingefroren. Standard 30.",
            ["Rig/MaskedReaimGain"] =
                "ALT — ohne Wirkung, solange die Weltneigung geparkt ist (siehe [Rig] WorldTiltDegrees). "
                + "Nur bei Weltneigung. Geschwindigkeit der verdeckten Neigungs-Nachführung als Bruchteil der "
                + "Drehgeschwindigkeit deines Kopfes (0-0.5). 0.15 = die Achse führt mit 15% der jeweiligen "
                + "Kopfdrehgeschwindigkeit nach — weit unter der Wahrnehmungsschwelle für Rotationsverstärkung "
                + "von ~20%, sodass sich die Welt nie sichtbar bewegt. Höher konvergiert schneller, fällt aber "
                + "eher auf.",
            ["Rig/MaskedReaimDeadband"] =
                "ALT — ohne Wirkung, solange die Weltneigung geparkt ist (siehe [Rig] WorldTiltDegrees). "
                + "Nur bei Weltneigung. Richtungsabweichungen zwischen Blick und Neigung, die kleiner sind als "
                + "dieser Wert (Grad), werden vollständig ignoriert — normales Umsehen löst nie eine Korrektur "
                + "aus, und eine derart kleine Restabweichung ist visuell nicht von einer perfekten Ausrichtung "
                + "zu unterscheiden.",
            // ---- [Compat] ----
            ["Compat/DisablePostProcessing"] =
                "Schaltet PostProcessing v2 (PostProcessLayer/PostProcessVolume) ab, solange VR aktiv ist. "
                + "Standard in Phase 1: true (PPv2 ist beim Stereo-Rendering nicht verifiziert).",
            ["Compat/DisableVolumetricFog"] =
                "Schaltet den Bildeffekt VolumetricFogAndMist.VolumetricFog ab, solange VR aktiv ist.",
            ["Compat/DisableComponents"] =
                "Zusätzliche, kommagetrennte vollständige Komponenten-Typnamen (optional 'FullName, "
                + "Assembly'), die abgeschaltet werden, solange VR aktiv ist, z. B. 'BeautifyEffect.Beautify'.",
            ["Compat/WallFade"] =
                "Wände durchsichtig in VR. AN blendet eine Wand, die zwischen deinem Kopf und dem "
                + "betrachteten Teil des Spielbereichs steht, ALS GANZES aus (weiches Auflösen, ~0.35s) bis auf "
                + "ihre Fundamentreihe, und wieder ein, sobald sie die Sicht nicht mehr blockiert. Die "
                + "Entscheidung ist zeitlich geglättet (muss ~0.4s anhalten), sodass schnelle Kopfbewegungen "
                + "Wände nie flackern lassen. AUS (Standard) hält jede Wand solide — das bisherige "
                + "VR-Verhalten. Rein visuell und lokal (Material Property Blocks pro Renderer): Mitspieler im "
                + "Mehrspieler sind nicht betroffen. Live umschaltbar in der VR-Einstellungstafel.",
            // ---- [Hands] ----
            ["Hands/PrimaryHand"] =
                "Dominante Hand (Right/Left). Ihr Zeigefinger-Strahl ist die Standardquelle für die Auswahl "
                + "beim Zielen auf dem Brett.",
            ["Hands/HandStyle"] =
                "Welches Handmodell getragen wird: Glove (Lederhandschuh, Standard), Plate "
                + "(Plattenpanzer-Stulpe) oder Arcane (Magierhandschuh mit Arkanrunen). Wirkt live (die Hände "
                + "werden bei Änderung neu aufgebaut) und wird im Mehrspieler synchronisiert, sodass andere "
                + "VR-Spieler deine gewählten Hände an deinem Avatar sehen. Fällt auf Glove zurück, wenn das "
                + "Prefab des Stils in einem älteren Asset-Bundle fehlt, und ohne Bundle auf die prozedurale "
                + "Hand.",
            ["General/LogLevel"] =
                "Wie viel der Mod in die LogOutput.log schreibt. Aus = still. Errors = nur was "
                + "fehlgeschlagen ist. Warnings = zusätzlich, was still degradiert ist (ein fehlendes "
                + "Asset, ein greifender Notbehelf) — die Untergrenze, ab der ein Fehlerbericht noch "
                + "brauchbar ist. Normal = zusätzlich die wenigen Zeilen, die sagen, welcher Build "
                + "läuft und ob VR hochgekommen ist. Verbose = zusätzlich der laufende Kommentar "
                + "jedes Subsystems, mehrere hundert Zeilen pro Sitzung. Trace = alles, samt "
                + "Debug-Geplauder. Trace ist VORERST die Voreinstellung, das Log ist damit exakt "
                + "das gewohnte; stell auf Warnings oder Normal, sobald die aktuelle Fehlersuche "
                + "durch ist. Wirkt ab der nächsten Zeile — kein Neustart nötig.",
            ["Hands/ScrollWithStickOnly"] =
                "Listen werden NUR mit dem Stick gescrollt. Ein Laser steht nie ganz still, und da die "
                + "Zieh-Schwelle abgeschaltet ist, verschiebt jeder Druck auch die Liste darunter — genau "
                + "das macht das Treffen von Optionen schwer. Mit dieser Option bleibt ein Druck, dessen "
                + "einziges Ziehziel die Liste selbst ist, ein sauberer Klick. Schieberegler, Scrollbalken "
                + "und Aufklappmenüs sind nicht betroffen: sie lösen auf sich selbst auf, nicht auf die Liste.",
            ["Hands/LaserFingerOrigin"] =
                "Lässt den SICHTBAREN Laserstrahl an der Zeigefingerspitze des Hand-Rigs beginnen "
                + "(zusammenlaufend auf den Endpunkt des Strahls der Aim-Pose), sodass er wirkt, als ginge er "
                + "vom zeigenden Finger aus. Der Auswahlstrahl selbst nutzt immer die OpenXR-Aim-Pose. Aus = "
                + "der Strahl beginnt am Ursprung der Aim-Pose (Controller).",
            ["Hands/LaserFingerOffsetMeters"] =
                "Feinjustierung für LaserFingerOrigin: wie weit (Meter, entlang des Strahls) vor der "
                + "Zeigefingerspitze der sichtbare Strahl beginnt.",
            ["Hands/HandColor"] =
                "Grundfarbe der prozeduralen Hände als RRGGBB-Hex (ohne '#'). Muss hell genug bleiben, um "
                + "sich gegen die schwarze Leere abzuheben — der Hand-Shader ist unbeleuchtet (in der Leere "
                + "gibt es keine Lichter; beleuchtete Shader werden dort schwarz gerendert). Die linke Hand "
                + "bekommt automatisch einen leicht kühlen Farbstich, damit die Seiten unterscheidbar bleiben.",
            // ---- [Rig] ----
            ["Rig/VoidColor"] =
                "Clear Color der Kopfkamera des Mods — die Leere rund um den schwebenden Menü-Bildschirm und "
                + "außerhalb des Dioramas. Standard ist reines Schwarz. Zum DEBUGGEN ein dunkles Grau setzen "
                + "(z. B. 1F2126FF): Grau unterscheidet \"Kamera rendert, aber Inhalt fehlt\" von \"Kamera tot "
                + "/ rendert nicht\" (tiefschwarz), was in Berichten aus dem Headset unbezahlbar ist.",
            ["Rig/ForwardRendering"] =
                "Rendert die Kopfkamera des Mods in FORWARD statt im DeferredShading des Spiels. Das ist die "
                + "Lösung dafür, dass transparente Effekte (Feuer-/Fackelschein, Hex-Auswahlring, Lebensbalken) "
                + "in VR DURCH Wände gerendert werden: der Renderer für den Himmels-Tiefen-Reset (Queue 1999, "
                + "ZTest Always) hat keinen Deferred-Pass und läuft auf einer Deferred-Kamera daher im "
                + "Forward-Opaque-Fallback NACH den Wänden, löscht deren Tiefe und lässt den Transparenten "
                + "nichts, wogegen sie testen könnten. Forward-Rendering stellt die strikte Queue-Reihenfolge "
                + "wieder her (Reset 1999 läuft VOR den Wänden 2000, die Wände überschreiben ihn), sodass der "
                + "Tiefenpuffer die Wände behält und Transparente korrekt verdeckt werden. NUR abschalten, wenn "
                + "die Forward-Beleuchtung falsch aussieht (Deferred verarbeitet viele dynamische Lichter pro "
                + "Pixel; Forward hat ein Lichter-Limit pro Objekt).",
            // ---- [Dev] ----
            ["Dev/Enabled"] =
                "Entwicklermodus: verbindet den VR-Eventbus und die Handsimulation auch ohne Headset und "
                + "installiert die Dev-Konsole (F8 simulierte Hände, F9 Ready anstoßen, F10 Overlay).",
            ["Dev/Overlay"] =
                "Zeigt das Dev-Overlay beim Start, wenn der Entwicklermodus aktiv ist (F10 schaltet es zur "
                + "Laufzeit um).",
            ["Dev/SimulateHands"] =
                "Animiert unechte Hand-Transforms auf dem Desktop (kein Headset nötig). T halten = Trigger, G "
                + "= Griff. Zur Laufzeit mit F8 umschalten. Wird ignoriert, solange echtes VR läuft.",
            ["Dev/InputDeviceDumpInterval"] =
                "Protokolliert alle UnityEngine.XR.InputDevices alle N Sekunden (0 = aus).",
            // ---- [Hands] ----
            ["Hands/*Scale"] =
                "Gleichmäßige visuelle Größe dieses Handstils (1 = Größe wie modelliert). Die Meshes der "
                + "Stile haben alle dieselbe Handlänge von 0.19 m, unterscheiden sich aber stark in der "
                + "Massigkeit — die gepanzerten Stile liegen standardmäßig unter 1, damit ihre Knöchelbreite "
                + "der realen Handgröße des Lederhandschuhs entspricht. Wirkt live (kein Neuaufbau); gegriffene "
                + "Objekte, der Kartenfächer und das Handgelenk-HUD behalten ihre eigene Größe (die "
                + "Rig-Sockets, an denen sie hängen, sind größenkompensiert).",
            // ---- [Cards] ----
            ["Cards/DevFakeHand"] =
                "Erzeugt so viele Dummy-VR-Karten (prozedurale Platzhalter-Kartenbilder), dass sich Fächer, "
                + "Kontrollbrett und Greifen ohne Szenario ausprobieren lassen. Erfordert [Dev] Enabled (+ "
                + "SimulateHands oder ein echtes HMD). 0 = aus.",
            ["Cards/RevealMode"] =
                "Wie sich der Handfächer zeigt. \"tilt\" = Demeo-artige SUPINATION des Handgelenks der "
                + "nicht-dominanten Hand: die Handfläche nach oben / zu dir drehen, allein auf der ROLL-Achse "
                + "gemessen, bei JEDER Armneigung — selbst bei senkrecht nach oben zeigenden Fingern öffnet "
                + "eine Handgelenksdrehung den Fächer (Roll-Gate v4, Hardware-Test #10 + Runde 4). \"always\" = "
                + "der Fächer ist draußen, sobald eine Kartenphase Karten hat, ganz ohne Geste.",
            ["Cards/RevealEnterDegrees"] =
                "RevealMode=tilt: Hand-ROLLWINKEL in GRAD, ab dem sich der Fächer ÖFFNET. Das Roll-Gate v4 "
                + "misst das ECHTE Handgelenks-Rollen über eine parallel transportierte Referenz: die "
                + "vorzeichenbehaftete Verdrehung des Handrückens um die Fingerachse, gegen Drift an Welt-Oben "
                + "verankert, sobald die Finger nicht senkrecht stehen. Den Arm zu neigen oder auszurichten — "
                + "auch gerade nach OBEN — kann den Messwert nicht bewegen; nur das Rollen des Handgelenks tut "
                + "das. Gemessen an der SICHTBAREN Hand (nach den Hand-Sitz-Versätzen/Handstil-Feinkorrekturen "
                + "aus dem Debug-Menü). Skala: 0 = flache Hand mit Knöcheln oben, 90 = Handfläche voll zum "
                + "Gesicht gerollt (negativ = andersherum gerollt, das öffnet den Fächer nie). Standard 60 = "
                + "eine bequeme Supination deutlich über die Senkrechte hinaus (Demeos eigene Schwelle liegt "
                + "bei ~37°). HINWEIS: die Skala hat sich gegenüber dem alten v2-Messverfahren GEÄNDERT (dessen "
                + "Standard 95 auf einer 0-180-Skala war) — alte Werte außerhalb des Bereichs werden einmalig "
                + "automatisch zurückgesetzt. Live änderbar im VR-Debug-Menü (Kategorie Kartenfächer).",
            ["Cards/RevealExitDegrees"] =
                "RevealMode=tilt: Hand-Rollwinkel in GRAD, unter dem sich der Fächer SCHLIESST (gleiche "
                + "Rollskala wie RevealEnterDegrees: 0 = flach, 90 = Handfläche voll zum Gesicht). Das "
                + "voreingestellte Totband von 15° unter dem Öffnen-Wert von 60° verhindert, dass das Gate an "
                + "der Grenze flattert; das Gate hält diesen Wert immer unter RevealEnterDegrees. Live änderbar "
                + "im VR-Debug-Menü (Kategorie Kartenfächer).",
            ["Cards/FanRadius"] =
                "Bogenradius des Handflächen-Fächers in echten Metern (die Dioramen-Skalierung wird "
                + "automatisch angewendet).",
            ["Cards/FanArcDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch FanArcSweepDegrees. Nichts liest diesen Wert. Er war "
                + "der maximale Gesamtbogen des Fächers in Grad; FanArcSweepDegrees hat ihn abgelöst und wurde "
                + "mit diesem Standardwert × 1.3 (= 91°) vorbelegt. Bleibt gebunden, damit vorhandene "
                + "cfg-Dateien unverändert laden — ein nicht gebundener Schlüssel wird beim nächsten Speichern "
                + "stillschweigend aus deiner Datei entfernt.",
            ["Cards/FanPalmOffset"] =
                "Höhe des Fächer-Drehpunkts über der Handflächenmitte, in echten Metern.",
            ["Cards/CardWidth"] =
                "Physische Kartenbreite in Metern (echte Pokerkarte = 0.0635). Die Höhe behält das "
                + "Seitenverhältnis 63.5:88.",
            ["Cards/InspectScale"] =
                "Größenfaktor, der auf eine Karte angewendet wird, solange du sie hältst (Betrachten in "
                + "natürlicher Größe).",
            ["Cards/HeldTiltDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch HeldFaceBias. Nichts liest diesen Wert (Hardware-Test "
                + "#13). Die alte, an der Handfläche ausgerichtete Haltepose erzwang eine starke Supination, um "
                + "die Karte zu lesen; die Pose steuert jetzt stattdessen HeldFaceBias. Bleibt nur gebunden, "
                + "damit vorhandene Konfigurationsdateien sauber laden. HINWEIS: [FigureGrab] HeldTiltDegrees "
                + "ist ein ANDERER, aktiver Eintrag.",
            ["Cards/HeldFaceBias"] =
                "Gehaltene Karte lesbar (Test #13): Neigung der KARTENSEITE in Grad aus \"flach auf der "
                + "Handfläche\" (0 = alte Pose, Seite entlang der Handflächen-Normale — nur per "
                + "Handgelenksdrehung lesbar) zurück zu Handgelenk/Unterarm. Im lockeren Controller-Griff "
                + "(geneigte Griffpose, siehe die Handsitz-Neigung pro Stil unter [Hands]) zeigen die Finger vorwärts und "
                + "leicht abwärts, bei ~65 also die Seite hoch/zurück zu deinen Augen — wie bei einer echten "
                + "Spielkarte. Die Oberkante zeigt zur Daumenseite (im lockeren Griff Welt-Oben; links "
                + "automatisch gespiegelt). Folgt weiter 1:1 dem Handgelenk — fester Versatz, KEINE Ausrichtung "
                + "pro Frame.",
            ["Cards/HeldForward"] =
                "RÜCKFALL für gehaltene Karten (nur genutzt, wenn das Hand-Rig keine Fingergelenke hat): "
                + "Versatz des Pinch-Punkts vom Greifanker aus entlang der Finger, in Metern.",
            ["Cards/HeldOffPalm"] =
                "RÜCKFALL für gehaltene Karten (nur genutzt, wenn das Hand-Rig keine Fingergelenke hat): "
                + "Versatz des Pinch-Punkts von der Handflächen-Oberfläche weg, in Metern.",
            ["Cards/HeldPinchOffset"] =
                "Feinjustierung der gehaltenen Karte: Versatz (Meter), der auf den berechneten Pinch-Punkt — "
                + "die Mitte zwischen Daumen- und Zeigefingerspitze im Moment des Greifens — ADDIERT wird, in "
                + "GrabAnchor-lokalen Achsen: +Y aus der Handfläche heraus, +Z entlang der Finger, +X seitlich. "
                + "Für die RECHTE Hand eingestellt; das X-Vorzeichen wird für die linke Hand automatisch "
                + "gespiegelt, sodass die Karte in beiden Händen an derselben anatomischen Stelle sitzt. "
                + "Beispiel {x:0, y:0.01, z:0.02} hebt die Karte 1 "
                + "cm von der Handfläche ab und schiebt sie 2 cm zu den Fingerspitzen.",
            ["Cards/TrayForward"] =
                "Platzierung des Kontrollbretts: Abstand nach vorn vom Kopf im Moment der Platzierung, in "
                + "Metern.",
            ["Cards/TrayDown"] =
                "Platzierung des Kontrollbretts: Absenkung unter Augenhöhe, in Metern (0.35 ~ Brusthöhe).",
            ["Cards/TrayRight"] = "Platzierung des Kontrollbretts: seitlicher Versatz (+rechts), in Metern.",
            ["Cards/TrayTilt"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise BoardTilt_<board>. Nichts liest diesen "
                + "Wert. Er war die Neigung des Kontrollbretts in Grad AUS DER WAAGERECHTEN zum Spieler hin (0 "
                + "= flach wie ein Tisch, 90 = aufrechte Tafel); BoardTilt_<board> hat ihn in der "
                + "Posenberechnung abgelöst und wurde mit 30 vorbelegt, damit Oak unverändert bleibt. Justiere "
                + "stattdessen BoardTilt_<board>. Bleibt gebunden, damit vorhandene cfg-Dateien unverändert "
                + "laden.",
            ["Cards/TrayYaw"] =
                "Drehung des Kontrollbretts relativ zur waagerechten Blickrichtung des Kopfes im Moment der "
                + "Platzierung, in Grad. Wird automatisch geschrieben, wenn du das Brett mit dem Griff an "
                + "seiner Haltestange bewegst; von Hand nur zum Zurücksetzen ändern.",
            ["Cards/TrayScale"] =
                "Größenfaktor des Kontrollbretts (0.5–2). Wird automatisch vom beidhändigen Brett-Griff "
                + "geschrieben (Haltestange mit beiden Händen greifen und auseinanderziehen/zusammenschieben); "
                + "von Hand nur zum Zurücksetzen ändern.",
            ["Cards/TrayFollow"] =
                "Verankerung des Kontrollbretts (Test #15, umgeschaltet mit dem Pin-Knopf am Brettrahmen). "
                + "true = das Brett hängt am Rig: es bewegt sich mit dir (Welt greifen, stufenweises Drehen, "
                + "neu zentrieren) und platziert sich beim Moduseintritt neu an den Versätzen "
                + "TrayForward/Down/Right. false = das Brett ist FIXIERT, wo du es gelassen hast, in der Welt "
                + "verankert — es bleibt liegen, während du dich bewegst, und platziert sich nie neu. Zurück "
                + "auf FOLGEN verankert es wieder an den eingestellten Versätzen.",
            ["Cards/BoardMoveMode"] =
                "Punkt 12: was der Griff an der Haltestange mit dem Kontrollbrett tun darf. Limited "
                + "(Standard, \"Begrenzt\") = das bisherige Verhalten: nur Position + Drehung, das Brett "
                + "bleibt für DICH waagerecht (unter der Weltneigung heißt waagerecht: waagerecht in deiner "
                + "Sicht, nicht in der Welt). LimitedPitch (\"Begrenzt mit Neigung\") = wie Begrenzt, "
                + "zusätzlich darf der Griff das Brett zu dir hin/von dir weg NEIGEN, begrenzt auf das "
                + "brettweise Fenster BoardPitchMin_<board>..BoardPitchMax_<board>. Free (\"Frei\") = das Brett folgt der "
                + "greifenden Hand 1:1 in ALLEN Achsen — keine Waagerecht-Haltung, keine Begrenzung (es KANN "
                + "kopfüber enden; zurück auf einen Begrenzt-Modus richtet es wieder aus). Wählbar in den "
                + "VR-Einstellungen (Tafeln → Karten & Brett); rein lokal — Mitspieler sehen wie bisher nur "
                + "die resultierende Brettpose.",
            ["Cards/TrayPitch"] =
                "Punkt 12: die vom Griff eingestellte Brettneigung, in Grad ADDIERT auf das brettweise "
                + "BoardTilt_<board> (positiv = aufrechter zu dir hin). Wird beim Loslassen der Haltestange "
                + "im Modus LimitedPitch oder Free automatisch geschrieben, damit die Neigung "
                + "Neuplatzierungen und Sitzungen übersteht; im Modus Limited ohne Wirkung (der nutzt immer "
                + "BoardTilt allein). Von Hand nur zum Zurücksetzen ändern. Beim Anwenden in LimitedPitch auf "
                + "das brettweise Fenster BoardPitchMin_<board>..BoardPitchMax_<board> begrenzt.",
            ["Cards/BoardPitchMinDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise BoardPitchMin_<board>. Nichts liest "
                + "diesen Wert. Er war die GLOBALE untere Kante des Neigungsfensters im Modus LimitedPitch, "
                + "in Grad relativ zu BoardTilt; BoardPitchMin_<board> hat ihn in der Begrenzung abgelöst "
                + "und wurde mit −45 vorbelegt, damit sich nichts ändert. Justiere stattdessen "
                + "BoardPitchMin_<board>. Bleibt gebunden, damit vorhandene cfg-Dateien unverändert laden.",
            ["Cards/BoardPitchMaxDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise BoardPitchMax_<board>. Nichts liest "
                + "diesen Wert. Er war die GLOBALE obere Kante des Neigungsfensters im Modus LimitedPitch, "
                + "in Grad relativ zu BoardTilt; BoardPitchMax_<board> hat ihn in der Begrenzung abgelöst "
                + "und wurde mit 45 vorbelegt, damit sich nichts ändert. Justiere stattdessen "
                + "BoardPitchMax_<board>. Bleibt gebunden, damit vorhandene cfg-Dateien unverändert laden.",
            ["Cards/CardLerpSpeed"] =
                "Geschwindigkeit der Kartenflug-Animation (Konstante der exponentiellen Glättung, 1/s).",
            ["Cards/SlotCardInset"] =
                "Wie weit eine Karte AUS der physischen Slot-Vertiefung heraus zum Betrachter gehoben wird, "
                + "in echten Metern (die -Z-Seite des Bretts). BuildBoard projiziert die Slot-Anker jetzt auf "
                + "den BODEN der Vertiefung, deshalb sitzt eine Karte bei 0 tief darin und wirkt, als stecke "
                + "sie hindurch — von oben kaum sichtbar. Dies hebt sie (ungefähr) bis zum Rand der Vertiefung, "
                + "sodass sie sichtbar AUF der Oberseite des Bretts liegt und zum Spieler zeigt. Erhöhe den "
                + "Wert, wenn Karten noch versunken wirken, senke ihn, wenn sie schweben. Gilt für gespielte "
                + "Karten, angedockte Aktionskarten und die Layouts für Einzelkartenwahl/kurze Rast "
                + "gleichermaßen. Ändert NICHT Breite/Höhe der Karte (ein separater Schritt richtet die "
                + "Vertiefung an der Karte aus).",
            // Cards/SlotCardFill ist WEG (stillgelegt 2026-08-11) — Nachfolger ist das brettweise
            // Cards/SlotOverlayScale_*, das Überlagerung und liegende Karte gemeinsam bemisst.
            ["Cards/RoundButtonDiameter"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise RestButtonDiameter_<board>. Nichts "
                + "liest diesen Wert: den brettweisen Eintrag, den der alte Text hier als \"zukünftig\" "
                + "bezeichnete, gibt es längst, und er gewinnt (RestControls.EnsureBuilt liest "
                + "RestButtonDiameter_<board>). Er war der Durchmesser (echte Meter) der RUNDEN Scheiben für "
                + "kurze/lange Rast, die in den beiden runden Rast-Aussparungen des Bretts sitzen. Justiere "
                + "stattdessen RestButtonDiameter_<board> — gleiche Bedeutung, gleicher Standard 0.105. Bleibt "
                + "gebunden, damit vorhandene cfg-Dateien unverändert laden.",
            ["Cards/RoundButtonThickness"] =
                "VERALTET — ohne Wirkung, ersetzt durch [RestButtons] Depth. Nichts liest diesen Wert; aktiv "
                + "ist ButtonTuning.RestCapDepth mit genau diesem Standard 0.012, damit das Aussehen "
                + "unverändert bleibt. Er war die Dicke (echte Meter) des runden Rast-Knopfpucks entlang der "
                + "Druckachse. Justiere stattdessen [RestButtons] Depth. Bleibt gebunden, damit vorhandene "
                + "cfg-Dateien unverändert laden.",
            ["Cards/RestButtonInsetX"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise RestButtonOffset_<board>. Nichts liest "
                + "diesen Wert: RestControls.EnsureBuilt liest RestButtonOffset_<board>, dessen X denselben "
                + "Versatz trägt (und dessen Z die herausstehende Tiefe ergänzt, die der alte Raycast-Sitz "
                + "früher schätzte). An diesem Schlüssel zu drehen ändert NICHTS, was auch immer diese "
                + "Beschreibung früher versprochen hat. Er war der seitliche Versatz der runden Scheiben für "
                + "kurze/lange Rast entlang des LOKALEN X des Rast-Ankers, in echten Metern: POSITIV = zur "
                + "Brett-MITTE, NEGATIV = zum Brett-RAND, und das richtige Vorzeichen hängt vom Bezugssystem "
                + "des Bretts ab. Diese Richtungskonvention gilt weiterhin — für RestButtonOffset_<board>.X, "
                + "das aus diesem Oak-Wert 0.024 vorbelegt ist. Bleibt gebunden, damit vorhandene cfg-Dateien "
                + "unverändert laden.",
            ["Cards/ConfirmUndoInsetX"] =
                "VERALTET — ohne Wirkung, ersetzt durch das brettweise ConfirmUndoOffset_<board>. Nichts "
                + "liest diesen Wert; trotz des \"PER-BOARD\", mit dem diese Beschreibung früher endete, ist er "
                + "ein einzelner globaler Wert. ConfirmUndoOffset_<board> ist der brettweise Eintrag, den "
                + "PlayTray.BuildButtons tatsächlich liest, aus diesem Oak-Wert als X = −0.014 vorbelegt. Er "
                + "war der Versatz nach innen in lokalem X (echte Meter, zur Brettmitte), der auf "
                + "Fortfahren/Rückgängig angewendet wurde, damit sie mittig auf den Oak-Metallplatten sitzen. "
                + "Bleibt gebunden, damit vorhandene cfg-Dateien unverändert laden.",
            ["Cards/WantedSlotHint"] =
                "Ruhiges, sanft pulsierendes Akzent-Leuchten auf den Slots, deren Befüllung das Spiel gerade "
                + "erwartet (Test #28) — zu unterscheiden vom kurzzeitigen goldenen Einrast-Leuchten, das vorab "
                + "zeigt, wo eine GEHALTENE Karte landet. Bei der normalen Kartenauswahl markiert es die noch "
                + "leeren Spiel-Slots, in denen die Runde eine Karte erwartet; bei Einzelkartenwahlen (lange "
                + "Rast: Karte verlieren, Schaden vermeiden, wiederherstellen/ablegen) markiert es den linken "
                + "Slot. Erlischt, sobald die Anforderung erfüllt ist oder der Ablauf endet. false = kein "
                + "Hinweis auf erwartete Slots.",
            ["Cards/FaceMipBake"] =
                "Aliasing-Runde 3 (T3): das Spiel liefert seine Kartenbild-Sprite-Atlanten OHNE Mipmaps (FACE "
                + "TEXTURE DIAG: mips=1), deshalb flimmern übernommene Kartenbilder bei Verkleinerung, egal "
                + "welche MSAA-/Supersampling-Stufe läuft. Bei true wird jede eindeutige Kartenbild-Textur zur "
                + "Laufzeit EINMAL in eine mipmapped trilinear/aniso-8-Kopie gebacken (GPU-Blit -> Readback -> "
                + "Mip-Kette) und die Sprites der Bilder auf gleichwertige Sprites dieser Kopie umgestellt "
                + "(rect/pivot/border/PPU bleiben erhalten; Originale kehren zurück, sobald ein Bild ans Spiel "
                + "zurückgeht). false = die miplosen Atlanten bleiben unangetastet.",
            ["Cards/Board"] =
                "Welches Kontrollbrett-Modell (PlayTray) aus dem Asset-Bundle geladen wird — live umschaltbar "
                + "in der VR-Einstellungstafel. Oak = das ursprünglich mitgelieferte Brett (Standard); Steel "
                + "und Bronze sind die beiden neuen Bretter. Die Zuordnung Enum→Bundle-Pfad steht in "
                + "VRCardFactory.GetTrayPrefab. Ist das gewählte Prefab noch nicht im Bundle, fällt das Brett "
                + "auf Oak zurück, und fehlt auch Oak, wird das prozedurale Brett benutzt — jede Auswahl ist "
                + "also gefahrlos. Eine Änderung baut das Brett live ab und neu auf (CardsDriver) und setzt "
                + "dabei die Karten auf dem neu geladenen Brett neu ein.",
            ["Cards/RestButtonOffset_*"] =
                "Versatz der RUNDEN Rast-Scheiben vom Rast-Anker, board-lokale Meter. X/Y liegen in der "
                + "Board-Ebene (+X zur Board-Mitte), Z = Herausstehen zum Spieler hin (NEGATIV = steht weiter "
                + "heraus). Ersetzt den Raycast-Sitz — stelle Z ein, bis die Scheiben sauber in den "
                + "Aussparungen sitzen. Von Oak übernommen (RestButtonInsetX 0.024, −5 mm heraus).",
            ["Cards/RestButtonDiameter_*"] =
                "Durchmesser (Meter) der runden Scheiben für kurze/lange Rast. Von Oak übernommen (0.105).",
            ["Cards/ConfirmUndoOffset_*"] =
                "Versatz der ECKIGEN Fortfahren/Rückgängig-Knöpfe von ihren Knopf-Ankern, board-lokale Meter. "
                + "X/Y in der Ebene (−X von der rechten Spalte zur Board-Mitte), Z = Herausstehen zum Spieler "
                + "hin (NEGATIV = steht weiter heraus). Von Oak übernommen (ConfirmUndoInsetX −0.014, −5 mm "
                + "heraus).",
            // Cards/ConfirmUndoSize_* ist WEG — 2026-08 mit dem Eintrag selbst stillgelegt
            // (Nutzerbericht: der Regler hatte keinen Effekt; die Größe der Bestätigen/Zurück-
            // Tasten kommt jetzt für beide Formen aus [BoardButtons] Breite/Höhe).
            ["Cards/ItemUseSlotOffset_*"] =
                "Versatz, der zur lokalen Position des Einsteck-Slots GEGENSTAND BENUTZEN ADDIERT wird "
                + "(zusätzlich zu seiner festen Basis UNTER dem Board neben den Fortfahren/Rückgängig-Knöpfen), "
                + "board-lokale Meter. X/Y in der Ebene, Z = Herausstehen zum Spieler hin (NEGATIV = steht "
                + "weiter heraus). Lege eine gehaltene benutzbare Gegenstandskarte in diesen Slot, um sie zu "
                + "BENUTZEN. Startwert 0 (Oak).",
            ["Cards/ItemCardOffset_*"] =
                "Versatz, der zum Fächer des GEGENSTANDS-Stapels und zur Haltepose der Gegenstandskarte "
                + "ADDIERT wird, board-lokale Meter — bewegt die Gegenstandskarten UNABHÄNGIG vom Fächer der "
                + "Fähigkeitskarten (sie haben eine andere, fast quadratische Form). X/Y in der Ebene, Z = "
                + "Herausstehen zum Spieler hin (NEGATIV = steht weiter heraus). Startwert 0 (Oak).",
            ["Cards/SlotOverlayOffset_*"] =
                "Versatz, der zur lokalen Position des Einrast-Leuchtens / Wunsch-Leuchtens am Slot ADDIERT "
                + "wird, board-lokale Meter. X/Y in der Ebene, Z = Herausstehen zum Spieler hin (NEGATIV = "
                + "steht weiter heraus). Startwert 0 (Oak).",
            ["Cards/SlotOverlaySpacing_*"] =
                "ZUSÄTZLICHER Abstand (board-lokale Meter), der zwischen den ZWEI Slot-Overlays entlang der "
                + "langen (Slot-zu-Slot-)Achse des Boards ADDIERT wird — Slot 0 (links) wandert −½, Slot 1 "
                + "(rechts) +½. Startwert 0 (die Slots verteilen die Overlays bereits; positiv zieht sie "
                + "auseinander). Item 1.",
            ["Cards/SlotOverlayScale_*"] =
                "GRÖSSE der beiden blinkenden Slot-Overlays UND der Karte, die darin zu liegen kommt — ein "
                + "Regler für beides, damit die Fläche, die den Platz markiert, genau die Fläche ist, die die "
                + "Karte danach bedeckt. Multipliziert die eigene Breite/Höhe der Karte (zusätzlich zum 1.3x "
                + "SlotScale des Rahmens), 1.0 = Karte in ihrer angelegten Größe. Das türkise Wunsch-Leuchten "
                + "nimmt genau diesen Wert; das goldene Einrast-Leuchten behält sein Verhältnis von 0,912 dazu "
                + "und liegt weiter INNERHALB des türkisen, wenn beide zu sehen sind. So weit erhöhen, bis die "
                + "Karte die physische Vertiefung fast füllt, ohne den Rand zu überragen. Ersetzt das "
                + "stillgelegte globale SlotCardFill und übernimmt dessen 1.45 unverändert. Ändert NICHT die "
                + "Sitztiefe (das ist SlotCardInset).",
            ["Cards/DecisionGap_*"] =
                "Senkrechter Abstand zwischen dem ENTSCHEIDUNGSTEXT (den das Spiel an der Unterkante "
                + "des Bretts zeichnet) und der OBERKANTE der Entscheidungsknöpfe, in board-lokalen "
                + "Metern. Das ist die EINZIGE Stellschraube für diesen Abstand: Sie ist bewusst "
                + "unabhängig von Größe und Versatz des Entscheidungsdocks — Vergrößern oder "
                + "Verschieben ändert also nie, wie weit die Knöpfe vom Text entfernt sitzen.",
            ["Cards/FanStepDegrees_*"] =
                "Winkel zwischen zwei benachbarten Karten dieses Stapel-Fächers, in Grad — die "
                + "SPREIZUNG. Größer = die Karten liegen weiter auseinander, und genau das macht "
                + "das physische Greifen einer einzelnen Karte leicht. Der Gesamtbogen des Fächers "
                + "wird trotzdem nie überschritten: Bei einem sehr vollen Stapel wird die Spreizung "
                + "gedeckelt, damit jede Karte erreichbar bleibt.",
            ["Cards/FanRadiusFactor_*"] =
                "Radius dieses Stapel-Fächers als Vielfaches des Handkarten-Radius ([Cards] "
                + "Fächer: Radius). Größer = ein weiterer, flacherer Bogen, was die Karten "
                + "ebenfalls auseinanderzieht.",
            ["Cards/BoardMinWidthMeters"] =
                "Wie klein das Controllboard höchstens werden darf, gemessen an seiner TATSÄCHLICH "
                + "SICHTBAREN BREITE in echten Metern — also daran, wie breit es für dich aussieht, "
                + "nicht an einem internen Faktor. Genau das verhindert das Schrumpfen auf "
                + "Streichholzgröße: Im Folgen-Modus hängt das Brett am Rig, also verkleinert es "
                + "sich mit, wenn du DICH SELBST per Weltgriff kleiner machst — und abwechselnd "
                + "fixieren, vergrößern, folgen, verkleinern multipliziert beide Verkleinerungen "
                + "miteinander. Die Grenze wirkt jeden Frame auf die Endgröße, keine Abfolge von "
                + "Gesten kommt daran vorbei.",
            ["Cards/BoardMaxWidthMeters"] =
                "Wie groß das Controllboard höchstens werden darf, ebenfalls als sichtbare BREITE "
                + "in echten Metern (siehe Mindestbreite). Wird immer etwas über der Mindestbreite "
                + "gehalten, was auch immer die beiden Werte sagen.",
            ["Cards/SpawnLeftOfHead"] =
                "Das Controllboard beim ERSTEN Platzieren in einem Szenario immer LINKS NEBEN DEM "
                + "KOPF absetzen, statt dort, wo du es zuletzt hingezogen hattest. Deine gespeicherte "
                + "Anordnung gilt weiterhin für den Rest der Sitzung (Verschieben wird wie bisher "
                + "gemerkt) — nur der STARTPLATZ ist dadurch jedes Mal derselbe, du weißt also immer, "
                + "wohin du greifen musst. Die drei Werte darunter legen diesen Platz fest.",
            ["Cards/SpawnSideMeters"] =
                "Startplatz: wie weit LINKS von dir das Brett steht, in echten Metern.",
            ["Cards/SpawnForwardMeters"] =
                "Startplatz: wie weit VOR dir das Brett steht, in echten Metern. Bewusst klein — "
                + "\"neben dir\", nicht \"vor dir\".",
            ["Cards/SpawnDownMeters"] =
                "Startplatz: wie weit UNTER Augenhöhe das Brett steht, in echten Metern.",
            ["Cards/GameCardParticles"] =
                "Den EIGENEN Partikeleffekt des Spiels für Karten (die Funken-/Rauchwolke "
                + "\"CardSmoke\") zulassen. Standardmäßig AUS: Er ist für die bildschirmgroße 2D-Karte "
                + "gemacht und sprüht auf dem tischgroßen Brett Funken über das GANZE Spielfeld — am "
                + "auffälligsten, wenn am Zugende die gespielten Karten in ihre Stapel geräumt werden. "
                + "Unterdrückt wird er über den spieleigenen Sparschalter für schwache Hardware, es "
                + "entsteht also gar kein Partikel, das falsch skaliert werden könnte; beim "
                + "Wiedereinschalten wird der Originalwert sofort zurückgesetzt. Das Abbrand-/"
                + "Auflösebild der Karte selbst bleibt unberührt.",
            ["Cards/CardDust"] =
                "Staub-/Funkenwolke, wenn eine Karte erscheint oder zerfällt. Standardmäßig AUS: Auf "
                + "der Hardware wirkte die Wolke beim Abwerfen einer Karte wie eine riesige "
                + "Funken-Animation über das GANZE Spielfeld (die Partikel werden in Welteinheiten "
                + "ausgestoßen, das Brett ist aber ein vergrößertes Diorama) statt wie ein kleines "
                + "Wölkchen an der Karte. Die Karte blendet ohnehin aus und schrumpft leicht — der "
                + "Staub war nur schmückendes Beiwerk.",
            ["Cards/InitiativeOffset_*"] =
                "Lokale Position der Aufhängung der Initiativleiste (ersetzt die feste Mount-Position), "
                + "board-lokale Meter. Von Oak übernommen (0, 0.10, −0.004).",
            ["Cards/PickBannerOffset_*"] =
                "Verschiebung der STATUS-TAFEL — der schwebenden Zeile über dem Board, die z. B. "
                + "\"Barbar: Wähle 1 Karte(n) zum Verlieren\" anzeigt — in board-lokalen Metern. X/Y in "
                + "der Ebene, Z = Tiefe zum Spieler hin (NEGATIV = weiter vorne). Startwert 0 = die "
                + "bisherige Stelle direkt über der Oberkante des Boards, also unmittelbar unter der "
                + "Initiativleiste. Im MEHRSPIELER-Modus setzt die Spiegelung die Tafel eines Mitspielers "
                + "an dieselbe Stelle auf dessen Remote-Board.",
            ["Cards/HoverHintOffset_*"] =
                "Verschiebung des TOOLTIP-BEREICHS — der EINEN festen Stelle an der OBEREN LINKEN "
                + "Ecke des Boards, an der ALLE zum Board gehörenden Tooltips erscheinen (Hover-"
                + "Hinweise, der angedockte Schadens-Hinweis) — in board-lokalen Metern. X/Y in der "
                + "Ebene, Z = Tiefe zum Spieler hin (NEGATIV = weiter vorne). Startwert 0 = die "
                + "berechnete Stelle oben links: die untere linke Ecke des Kastens sitzt knapp über "
                + "der GEMESSENEN oberen linken Board-Ecke, folgt also dem echten Board in jeder "
                + "Größe. Ein Tooltip, der zu einem schwebenden FENSTER oder MENÜ gehört, wird "
                + "stattdessen auf diesem Fenster angezeigt und ignoriert diesen Wert. Wird jeden "
                + "Frame gelesen — eine Änderung verschiebt auch einen bereits offenen Tooltip. "
                + "(Der Schlüsselname bleibt aus Kompatibilität zur alten Konfiguration erhalten.)",
            ["Cards/BoardTilt_*"] =
                "Neigung des Boards aus der Waagerechten zum Spieler hin, Grad (0 = flach wie ein Tisch, 90 = "
                + "aufrecht). Ersetzt TrayTilt in der Posenberechnung für dieses Board. Von Oak übernommen "
                + "(30).",
            ["Cards/BoardPitchMin_*"] =
                "Punkt 12, nur BoardMoveMode=LimitedPitch: wie weit der Griff DIESES Brett gegenüber seinem "
                + "eingestellten BoardTilt nach UNTEN/von dir weg neigen darf, in Grad (untere Kante des "
                + "Neigungsfensters; 0 = gar nicht nach unten). Live im Debug-Menü einstellbar; beim Lesen "
                + "immer ≤ BoardPitchMax gehalten. Ersetzt das globale BoardPitchMinDegrees, von dessen "
                + "Standard übernommen (−45).",
            ["Cards/BoardPitchMax_*"] =
                "Punkt 12, nur BoardMoveMode=LimitedPitch: wie weit der Griff DIESES Brett gegenüber seinem "
                + "eingestellten BoardTilt nach OBEN/zu dir hin neigen darf, in Grad (obere Kante des "
                + "Neigungsfensters; 0 = gar nicht nach oben). Live im Debug-Menü einstellbar; beim Lesen "
                + "immer ≥ BoardPitchMin gehalten. Ersetzt das globale BoardPitchMaxDegrees, von dessen "
                + "Standard übernommen (45).",
            ["Cards/BoardYaw_*"] =
                "Zusätzliche Board-Drehung (Gieren), die auf den durch Greifen geschriebenen TrayYaw ADDIERT "
                + "wird, Grad. Startwert 0 (Oak).",
            ["Cards/BoardScale_*"] =
                "Größen-MULTIPLIKATOR des Boards, angewendet zusätzlich zum durch Greifen geschriebenen "
                + "TrayScale. Standard 0.5: das Board hängt am Rig, seine scheinbare Größe schrumpft also nicht "
                + "mit dem Tisch — der Standard von ~0.4 (Tischverhältnis) wirkte beim ersten Erscheinen etwas "
                + "klein, deshalb öffnet das Board leicht größer (0.5). Die Größe lässt sich jederzeit mit der "
                + "beidhändigen Greifgeste ändern (schreibt TrayScale); dies ist der Startwert pro Board "
                + "obendrauf.",
            ["Cards/BoardPosOffset_*"] =
                "Positions-Versatz des Boards, der auf den kopfrelativen Versatz des Trays ADDIERT wird, "
                + "echte Meter im Kopf-Bezugssystem (X = rechts, Y = hoch, Z = vorne). Startwert 0 (Oak).",
            ["Cards/RestButtonSpacing_*"] =
                "ZUSÄTZLICHER Abstand (board-lokale Meter), der zwischen den RAST-Knöpfen für kurze/lange "
                + "Rast entlang der kurzen Board-Achse ADDIERT wird — die Scheibe für die kurze Rast (oben) "
                + "wandert +½, die für die lange (unten) −½. Startwert 0 (die Anker im Bundle verteilen sie "
                + "bereits; positiv zieht sie auseinander).",
            ["Cards/GenericButtonSpacing_*"] =
                "ZUSÄTZLICHER Abstand (board-lokale Meter), der zwischen den GENERISCHEN "
                + "Fortfahren/Rückgängig-Knöpfen entlang der kurzen Board-Achse ADDIERT wird — Fortfahren "
                + "(oben) +½, Rückgängig (unten) −½. Startwert 0.",
            ["Cards/RestButtonShape_*"] =
                "FORM der Kappen der RAST-Knopfgruppe (kurze/lange Rast). Round = Scheiben in den "
                + "Aussparungen (heutiges Aussehen); Square = eckige Tastenkappen. Startwert Round.",
            ["Cards/GenericButtonShape_*"] =
                "FORM der Kappen der GENERISCHEN Knopfgruppe (Fortfahren/Rückgängig usw.). Square = eckige "
                + "Tastenkappen (heutiges Aussehen); Round = Scheiben in den Aussparungen. Startwert Square.",
            ["Cards/ActiveOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung der AKTIVEN Karten ADDIERT wird (zusätzlich "
                + "zur festen Basis direkt hinter den Kartenstapeln), board-lokale Meter. Startwert 0 (Oak).",
            ["Cards/ActiveCardScale_*"] =
                "Größe der Spalte der AKTIVEN Karten (× Kartengröße). Startwert 0.82 — etwas kleiner als der "
                + "Hand-/Blätterfächer.",
            ["Cards/ActiveGridSpacing_*"] =
                "Raster-Schritt-FAKTOREN der AKTIVEN Karten: X = Spaltenschritt (× skalierte Kartenbreite), Y "
                + "= Zeilenschritt (× skalierte Kartenhöhe). Startwert (1.06, 0.70).",
            ["Cards/PileOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung der Ablage-/Verbrennstapel ADDIERT wird "
                + "(zusätzlich zur festen Basis an der rechten Kante), board-lokale Meter. Startwert 0 (Oak).",
            ["Cards/PileScale_*"] =
                "Größen-MULTIPLIKATOR der beiden Ablage-/Verbrennstapel. Startwert 1 (Oak).",
            ["Cards/PileSpacing_*"] =
                "Senkrechter Abstand (board-lokale Meter) zwischen den Mittelpunkten des Ablagestapels (oben) "
                + "und des Verbrennstapels (unten). Startwert 0.116 (Oak).",
            ["Cards/ObjectivesOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung des AUFGABEN-Docks ('Aufgaben') ADDIERT "
                + "wird (zusätzlich zur festen Basis in der linken Spalte), board-lokale Meter. Startwert 0 "
                + "(Oak).",
            ["Cards/ObjectivesScale_*"] =
                "GRÖSSEN-Multiplikator des AUFGABEN-Docks ('Aufgaben') — ein echter ZOOM: Text, "
                + "Fortschrittsbalken, Symbole und die Tafel selbst skalieren gemeinsam, denn dies ist die "
                + "localScale des Aufgaben-MOUNTS und die angedockte Tafel folgt mount.lossyScale. Dies ist der "
                + "EINZIGE Regler, der die Schriftgröße des Aufgabentextes ändert; ObjectivesWidth ändert die "
                + "Form des Blocks, nie die Schriftgröße. Startwert 1 (Oak).",
            ["Cards/ObjectivesWidth_*"] =
                "BREITEN-Multiplikator des AUFGABEN-Docks ('Aufgaben') — NUR die FORM, NICHT die Größe. Die "
                + "Umbruchspalte ist PlayTray.ObjectivesMountWidth (0.26 m) x diesem Faktor, z. B. 1.6 = 416 "
                + "mm, dem Aufgaben-Container des Spiels als Pixelbreite AUFGEZWUNGEN (Spalte x Tafeldichte, "
                + "2400 px/m x 0.6) — der Aufgaben-TEXT BRICHT also auf diesem Maß neu um und der "
                + "fillAmount-FORTSCHRITTSBALKEN jeder Zeile ist wirklich so lang. Die gerenderte SCHRIFTGRÖSSE "
                + "bleibt unberührt: die Dock-Einpassung sieht für diese Tafel die Breitenachse nicht mehr an "
                + "(sie kann nicht — der Inhalt IST das Budget), also ergibt sich Meter-pro-Pixel allein aus "
                + "der Tafeldichte und nur ObjectivesScale ('Größe') verändert sie. Höher = derselbe Text auf "
                + "weniger, längeren Zeilen, wobei die Tafel von der Board-Kante weiter nach LINKS in den "
                + "freien Raum reicht (keine Überlappung mit dem Board); niedriger = mehr, kürzere Zeilen in "
                + "einem schmaleren Block, gleiche Buchstabenhöhe. Unter ~0.5 brechen die Zeilen enger um, als "
                + "das Spiel sie angelegt hat. Standard 1.6. Live angewendet: ObjectivesSurface erzwingt die "
                + "Spalte in jedem Tick neu; jede Rect-Änderung wird zurückgenommen, wenn die Tafel losgelassen "
                + "wird.",
            ["Cards/ElementsOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung des Docks für ELEMENT-Infusionen "
                + "('Elemente') ADDIERT wird (zusätzlich zur festen Basis in der linken Spalte unterhalb der "
                + "Aufgaben), board-lokale Meter. Startwert 0 (Oak).",
            ["Cards/ElementsScale_*"] =
                "Größen-MULTIPLIKATOR des Docks für ELEMENT-Infusionen ('Elemente'). Startwert 1 (Oak).",
            ["Cards/PinOffset_*"] =
                "Versatz, der zur lokalen Position des FOLGEN/FIXIERT-Schalters ADDIERT wird (zusätzlich zu "
                + "seiner festen Basis unten rechts), board-lokale Meter (Z = Herausstehen zum Spieler hin). "
                + "Startwert 0 (Oak).",
            ["Cards/ReadoutOffset_*"] =
                "Versatz, der zur lokalen Position der RUNDEN-Anzeige ('Runde N') ADDIERT wird (zusätzlich zu "
                + "ihrer festen Basis oben rechts), board-lokale Meter (Z = Herausstehen zum Spieler hin). "
                + "Startwert 0 (Oak).",
            ["Cards/ClusterOffset_*"] =
                "Versatz, der zur lokalen Position der KNOPFGRUPPE für den Zugablauf ADDIERT wird — am Board "
                + "ist das die ÜBERSPRINGEN-Taste ('Bewegung/Angriff überspringen') — zusätzlich zu ihrer "
                + "festen Basis, board-lokale Meter (Z = Herausstehen zum Spieler hin). Addiert sich im "
                + "selben Bezugssystem und derselben Einheit zu [RoundButtons] OffsetX/Y/Z. Live. "
                + "Startwert 0 (Oak).",
            ["Cards/ClusterScale_*"] =
                "Größen-MULTIPLIKATOR der KNOPFGRUPPE für den Zugablauf (zusätzlich zu ihrer festen "
                + "Dock-Skalierung von 0.7x). Startwert 1 (Oak).",
            ["Cards/DecisionOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung des gemeinsamen ENTSCHEIDUNGS-DOCKS ADDIERT "
                + "wird, board-lokale Meter — er verschiebt den GESAMTEN Entscheidungsbereich als einen Block: "
                + "die Knöpfe, den Text darüber und die Benutzungs-Leisten darunter, alle um denselben Betrag. "
                + "Y = hoch/runter (NEGATIV = weiter unter dem Board), X = seitlich, Z = nach vorne zum Spieler. "
                + "Den Abstand INNERHALB des Bereichs ändert er nie — das macht allein DecisionGap. Startwert 0 "
                + "auf jedem Board (die 157 mm, die hier früher standen, stecken jetzt in der festen Basis der "
                + "Aufhängung, seit das Y wirklich wirkt).",
            ["Cards/DecisionScale_*"] =
                "Größen-MULTIPLIKATOR des gemeinsamen ENTSCHEIDUNGS-DOCKS (seine angedockte Abfragezeile "
                + "folgt in der Pose der lossyScale des Mounts). Startwert 1 (Oak).",
            ["Cards/DecisionOffsetYRebased"] =
                "Interne Einmal-Migrationsmarke: das Y von DecisionOffset_* wurde in dieser "
                + "Konfigurationsdatei auf den Stand gebracht, ab dem dieser Regler den ganzen "
                + "Entscheidungsbereich verschiebt (die früher hier stehenden 157 mm sind in die feste "
                + "Basis der Aufhängung gewandert). Nicht bearbeiten.",
            ["Cards/BoardScaleDefault04Applied"] =
                "Interne Einmal-Migrationsmarke: der Standardwert 0.4x für BoardScale (im Gespann mit dem "
                + "Standard-Tischmaßstab 2.5x) wurde dieser Konfigurationsdatei angeboten. Nicht bearbeiten.",
            ["Cards/FanCurveByFill"] =
                "Demeo-Parität (G1): die senkrechte Wölbung des Fächers und die Neigung pro Karte danach "
                + "skalieren, wie voll die Hand ist — mit wenigen Karten fast flach, bei voller Hand "
                + "gewölbt/geneigt (Demeo CardHandView). false = die alte konstante Krümmung bei jeder "
                + "Handgröße.",
            ["Cards/FanMaxHandForCurve"] =
                "Demeo-Parität (G1): Handgröße, bei der der Fächer die volle Krümmung erreicht. fill = "
                + "cardCount / dieser Wert (begrenzt auf 0..1) skaliert Wölbung + Neigung.",
            ["Cards/FanFlatCurvatureFactor"] =
                "Demeo-Parität (G1): der Faktor der senkrechten Wölbung bei VOLLER Hand (fill = 1). Dies ist "
                + "der konstante Wert von vor Demeo; mit FanCurveByFill ist er nun der Zielwert bei fill=1, und "
                + "die Wölbung geht Richtung flach zurück, wenn die Hand kleiner wird.",
            ["Cards/FanTiltFactor"] =
                "Demeo-Parität (G1): der Faktor der Z-Neigung pro Karte bei VOLLER Hand (fill = 1), skaliert "
                + "mit fill.",
            ["Cards/FanSplitMultiplier"] =
                "Demeo-Parität (G2): wie weit (echte Meter) die Karten des Fächers zur Seite rutschen, um "
                + "eine Lücke um die überfahrene Karte zu öffnen (Demeo spreizt den ganzen Fächer auf, nicht "
                + "nur die überfahrene Karte). 0 = keine Spreizung.",
            ["Cards/FanSplitFalloff"] =
                "Demeo-Parität (G2): wie schnell die Spreizung der Nachbarn mit dem Abstand (in "
                + "Kartenplätzen) zur überfahrenen Karte abklingt. Höher = nur die nächsten Nachbarn bewegen "
                + "sich; niedriger = der ganze Fächer spreizt sich auf. Im Code fest verdrahteter Ersatz für "
                + "Demeos serialisierte Abklingkurve.",
            ["Cards/FanSelectedPopForward"] =
                "Demeo-Parität (G2): wie weit (echte Meter) die überfahrene/ausgewählte Karte zum Betrachter "
                + "hin herausspringt (entlang der negativen Flächennormale). Demeo nutzt ~0.25 Szeneneinheiten; "
                + "0.035 m passt zu unserem Maßstab.",
            ["Cards/FanFollowSmoothing"] =
                "Demeo-Parität (G4): Rate der geglätteten Fächer-Nachführung (exponentielle Glättung, 1/s). "
                + "Der Fächer folgt der Handfläche weich nachgezogen, statt starr mit ihr verschweißt zu sein "
                + "(Demeo ViewHelper). 0 = starr angehängt (das Verhalten von vor Demeo). Höher = schnappiger.",
            ["Cards/FanFollowDeadzone"] =
                "Demeo-Parität (G4): Totzone der Fächer-Nachführung (echte Meter). Der Fächer bleibt stehen, "
                + "bis die Handfläche weiter als dieser Wert abdriftet, und zieht dann weich nach — beseitigt "
                + "Mikro-Zittern (Demeo minDistanceToMove). Wird nur genutzt, wenn FanFollowSmoothing > 0.",
            ["Cards/RevealIgnoreWhenGrabbing"] =
                "Demeo-Parität (G5): den Fächer nicht auf der Hand öffnen, die gerade etwas greift (Demeo "
                + "unterdrückt das Aufdecken auf der beschäftigten Hand). false = das alte Verhalten.",
            ["Cards/FanOpenDuration"] =
                "Aufblätter-Animation (Demeo CardHandView fan-in): Sekunden, die jede Karte vom "
                + "zusammengeklappten Mittelstapel zu ihrem Fächerplatz braucht (Ease-out, UNSKALIERTE Zeit — "
                + "läuft auch, während das Spiel die Simulationszeit pausiert). 0 = sofort.",
            ["Cards/FanOpenStagger"] =
                "Aufblättern: zusätzliche Startverzögerung in Sekunden PRO PLATZ Abstand von der Fächermitte "
                + "— der Fächer wellt sich nach außen, statt dass alle Karten gleichzeitig losfahren. Demeo "
                + "bewegt alle Karten gleichzeitig (0); ein winziger Versatz wirkt lebendiger. 0 = keine.",
            ["Cards/FanCloseDuration"] =
                "Fächer verbergen: Sekunden, die die Karten brauchen, um wieder in den Mittelstapel "
                + "zusammenzuklappen, bevor der Fächer verschwindet (unskalierte Zeit). 0 = sofort verschwinden "
                + "(Verhalten vor der Animation).",
            ["Cards/FanSwapDuration"] =
                "Charakterwechsel: Sekunden, die EINE Karte braucht, um aus dem Fächer heraus (oder in ihn "
                + "hinein) zu fliegen, wenn du bei offenem Fächer umschaltest, wessen Hand du ansiehst "
                + "(unskalierte Zeit — läuft auch, während das Spiel pausiert). Der ganze Tausch braucht das "
                + "plus die Überlappung plus den Verzug der letzten Karte. Länger = der Tausch ist lesbar "
                + "statt ein Zucken; zu lang und das Umschalten wirkt zäh.",
            ["Cards/FanSwapStagger"] =
                "Charakterwechsel: zusätzliche Startverzögerung in Sekunden PRO KARTE entlang des Bogens, "
                + "sodass der Tausch über die Hand wischt, statt dass alle Karten gleichzeitig losfahren. "
                + "BEIDE Hälften benutzen ihn und beide laufen in dieselbe Richtung — genau das lässt die "
                + "gehende und die kommende Hand als EIN Wischen lesen statt als zwei Animationen. Es ist "
                + "auch der Grund, warum die Bewegung vor einem Mixed-Reality-Hintergrund ankommt: das Auge "
                + "verfolgt eine wandernde Front, ein gleichzeitiger Klumpen ist nur ein Flackern, das ein "
                + "unruhiger Raum schluckt. 0 = alle Karten starten zusammen.",
            ["Cards/FanSwapOverlap"] =
                "Charakterwechsel: wie stark die ANKUNFT eines Platzes seinen ABGANG überlappt. 1 = die neue "
                + "Karte startet im selben Moment wie die alte (die beiden Hände kreuzen sich in der Luft); "
                + "0 = die neue Karte startet erst, wenn die alte ganz weg ist. Das ist der eine Regler, der "
                + "entscheidet, ob der Wechsel als TAUSCH gelesen wird oder als eine leere Hand, die wieder "
                + "gefüllt wird — deshalb ist der ausgelieferte Wert hoch.",
            ["Cards/FanSwapTravel"] =
                "Charakterwechsel: wie weit JENSEITS des Bogenendes der Sammel-/Austeilpunkt liegt (echte "
                + "Meter). Die gehende Hand läuft auf diesen Punkt an einem Ende zusammen (wie ein "
                + "aufgenommenes Deck), die kommende fächert aus dem spiegelbildlichen Punkt am anderen Ende "
                + "auf — die Karten gehen und kommen also sichtbar, statt an Ort und Stelle zu verblassen. "
                + "Größer = sie fahren weiter von der Hand weg.",
            ["Cards/FanSwapArc"] =
                "Charakterwechsel: wie weit sich die Karten in der Mitte ihres Flugs in der TIEFE bewegen "
                + "(echte Meter). Die gehende Karte taucht um diesen Betrag VON DIR WEG, die kommende wölbt "
                + "sich um denselben Betrag ZU DIR — die neue Hand zieht also vor der alten vorbei, und die "
                + "beiden können nie durcheinander hindurchzugleiten scheinen. Tiefe ist außerdem der Reiz, "
                + "den Passthrough nicht überdecken kann: beide Augen sehen die Trennung. 0 = beide Hälften "
                + "bleiben flach in der Fächerebene.",
            ["Cards/FanSwapSpinDegrees"] =
                "Charakterwechsel: die Drehung (Grad), durch die sich die beiden Hälften bewegen — die "
                + "gehende Hand wickelt sich beim Zusammenlaufen in die eine Richtung, die kommende beim "
                + "Austeilen aus der anderen heraus. Eine Drehung verändert die Silhouette einer Karte, und "
                + "ein unruhiger Hintergrund erzeugt niemals zufällig eine zusammenhängende Silhouettendrehung; "
                + "die entgegengesetzten Vorzeichen sind es, die die beiden Hälften als Tausch lesen lassen "
                + "und nicht als Schub. 0 = keine Drehung.",
            ["Cards/FanSwapSeedScale"] =
                "Charakterwechsel: die Größe, die eine Karte am Sammel-/Austeilpunkt hat, als Anteil ihrer "
                + "Größe im Fächer. Kleiner = mehr Schrumpfen nach draußen und mehr Wachsen nach drinnen — "
                + "die einäugige Hälfte desselben Tiefenreizes, den die Wölbung stereoskopisch liefert.",
            ["Cards/FanSwapSettleOvershoot"] =
                "Charakterwechsel: die Stärke des Überschwingens am ENDE des Flugs einer ankommenden Karte "
                + "(sie schießt über ihren Platz hinaus und schwingt in ihn zurück) und des passenden "
                + "Ausholens, das eine gehende Karte vor dem Abflug macht. Ein RICHTUNGSWECHSEL ist das "
                + "auffälligste Ereignis, das Bewegung hat, und er kostet keinen zusätzlichen Weg. Etwa 1,5 "
                + "sind ~6 % Überschwingen; 0 = ein reines Ausrollen ohne Umkehr.",
            ["Cards/ItemFanOpenDuration"] =
                "Gegenstands-Fächer öffnen: Sekunden, die EINE Gegenstandskarte braucht, um aus dem "
                + "Gegenstands-Stapel an ihren Platz im Bogen zu fliegen (unskalierte Zeit — läuft auch, "
                + "während das Spiel pausiert). Der ganze Fächer braucht das PLUS den Verzug der letzten "
                + "Karte. Länger = der Flug ist lesbar statt ein Zucken; zu lang und das Öffnen wirkt zäh.",
            ["Cards/ItemFanOpenStagger"] =
                "Gegenstands-Fächer öffnen: zusätzliche Startverzögerung in Sekunden PRO PLATZ Abstand von "
                + "der Fächermitte — die Karten werden nacheinander nach außen ausgeteilt, statt dass alle "
                + "gleichzeitig den Stapel verlassen. Das ist der wichtigste Grund, warum die Animation vor "
                + "einem Mixed-Reality-Hintergrund überhaupt ankommt: eine wandernde FRONT verfolgt das "
                + "Auge, ein gleichzeitiger Klumpen ist nur ein Flackern, das ein unruhiger Raum schluckt. "
                + "0 = alle Karten starten zusammen (das alte Verhalten).",
            ["Cards/ItemFanOpenArc"] =
                "Gegenstands-Fächer öffnen: wie weit sich die fliegende Karte in der Mitte ihres Flugs ZU "
                + "DIR hin wölbt (echte Meter), bevor sie sich in die Fächerebene zurücklegt. Aus dem "
                + "geraden Schieben wird ein geworfener Bogen — und, der Mixed-Reality-Teil, die Karte "
                + "bewegt sich in der TIEFE, sodass beide Augen sie vom Raum dahinter getrennt sehen. "
                + "Stereo-Trennung ist ein Reiz, den Passthrough nicht überdecken kann. 0 = die alte Gerade.",
            ["Cards/ItemFanOpenSpinDegrees"] =
                "Gegenstands-Fächer öffnen: die Drehung (Grad), aus der sich eine Karte im Flug "
                + "herauswickelt — nach außen vorzeichenbehaftet, sodass sich der Fächer sichtbar "
                + "AUFKLAPPT statt aufzuschieben. Eine Drehung verändert die Silhouette der Karte, und "
                + "eine Silhouettenänderung ist auch vor einem Hintergrund lesbar, der selbst Bewegung "
                + "und Kontrast mitbringt. 0 = keine Drehung (das alte Verhalten).",
            ["Cards/ItemFanSeedScale"] =
                "Gegenstands-Fächer: die Größe, mit der eine Karte auf dem Stapel startet (und auf die sie "
                + "beim Schließen schrumpft), als Anteil ihrer Größe im Bogen. Kleiner = mehr Wachstum über "
                + "den Flug, der zweite Tiefenreiz nach der Wölbung — eine Karte, die ihre Größe "
                + "verdoppelt, kommt auf dich zu und schiebt sich nicht über ein Bild.",
            ["Cards/ItemFanSettleOvershoot"] =
                "Gegenstands-Fächer: die Stärke des Überschwingens am ENDE des Ausflugs (die Karte schießt "
                + "über ihren Platz hinaus und schwingt in ihn zurück) und des passenden Ausholens vor dem "
                + "Einklappen. Ein RICHTUNGSWECHSEL ist das auffälligste Ereignis, das Bewegung hat, und er "
                + "kostet anders als Tempo keinen zusätzlichen Weg — deshalb steht er hier statt einer "
                + "einfach schnelleren Animation. Etwa 1,4 sind ~5 % Überschwingen; 0 = ein reines "
                + "Ausrollen ohne Umkehr (das alte Verhalten).",
            ["Cards/ItemFanCloseDuration"] =
                "Gegenstands-Fächer schließen: Sekunden, die EINE Gegenstandskarte braucht, um zurück in "
                + "den Gegenstands-Stapel zu fallen, bevor sie verschwindet (unskalierte Zeit). Die Karten "
                + "werden vorher vom Fächer gelöst, also läuft das vollständig ab, egal wodurch der Fächer "
                + "geschlossen wurde.",
            ["Cards/ItemFanCloseStagger"] =
                "Gegenstands-Fächer schließen: Verzug pro Platz, ÄUSSERSTE KARTE ZUERST, sodass das "
                + "Einklappen das Öffnen exakt rückwärts ist. 0 = alle Karten lösen sich gleichzeitig.",
            ["Cards/ItemCueBeatSeconds"] =
                "Gegenstands-Hinweis: Sekunden pro HERZSCHLAG — die eine Uhr, nach der der ganze Hinweis "
                + "läuft (die Ringe, die der geschlossene Gegenstands-Stapel abwirft, die Funken-Stöße, "
                + "die Rahmen um nutzbare Karten im offenen Fächer und der Ping in der Ablage). Bewusst "
                + "ein Schlag mit einer PAUSE darin statt eines gleichmäßigen Atmens: der Augenwinkel "
                + "meldet plötzliche Änderungen und überliest langsame Verläufe — die Pause ist das, was "
                + "den nächsten Schlag sichtbar macht. Kürzer = drängender; viel länger und die Pause "
                + "wirkt wie 'aus'.",
            ["Cards/ItemCueRingReach"] =
                "Gegenstands-Hinweis: wie weit jeder Lichtring vom geschlossenen Gegenstands-Stapel nach "
                + "außen wandert, als Vielfaches der Stapelgröße. Ein Ring, der WÄCHST, ist eine "
                + "Formänderung — und eine Formänderung ist das Einzige, was ein heller, unruhiger "
                + "Mixed-Reality-Raum nicht schlucken kann; bei Helligkeit gewinnt der Raum. 1 = der Ring "
                + "verlässt den Stapel nie.",
            ["Cards/ItemCueRingAlpha"] =
                "Gegenstands-Hinweis: Spitzen-Deckkraft dieser Ringe. Sie werden als heller Kern mit "
                + "einem DUNKLEN Saum auf beiden Seiten gezeichnet, damit sie vor einer weißen Wand "
                + "genauso einen sichtbaren Umriss behalten wie vor einem dunklen Raum. 0 = gar keine "
                + "Ringe (nur die treibenden Funken, der alte Hinweis).",
            ["Cards/ItemCueEmberRate"] =
                "Gegenstands-Hinweis: weiche Goldfunken pro Sekunde, die vom geschlossenen "
                + "Gegenstands-Stapel aufsteigen. Sie kommen jetzt als STOSS auf jedem Herzschlag statt "
                + "als gleichmäßiges Rieseln. 0 = keine Funken.",
            ["Cards/ItemCueEmberSize"] =
                "Gegenstands-Hinweis: wie viel größer ein Funke ist als in der ursprünglichen, "
                + "'bewusst dezenten' Größe. Wenige Millimeter sind auf Lesedistanz unter einem "
                + "Winkelgrad — dort hört ein Funke auf, ein Objekt zu sein, und wird zu Flimmern.",
            ["Cards/ItemBerthRingThickness"] =
                "Ablage (Gegenstand benutzen): wie dick der kartenförmige Umriss der Ablage gezeichnet "
                + "wird, in echten Metern. Die Ablage ist jetzt ein hohler Umriss mit OFFENER Mitte, "
                + "also ist diese Linie die ganze Form — dick genug, um vom anderen Tischende zu lesen, "
                + "dünn genug, dass daraus nie wieder eine Platte wird.",
            ["Cards/ItemBerthGlow"] =
                "Ablage: Spitzenhelligkeit des warmen Lichts, das die Ablage FÜLLT. Es ist "
                + "hinzugefügtes Licht, keine dunkle Platte — auf dunkler Szene leuchtet die Ablage, "
                + "und in Mixed Reality scheint dein eigener Raum hindurch statt eines schwarzen "
                + "Rechtecks. 0 = eine völlig offene Ablage (nur Umriss und Ping).",
            ["Cards/ItemBerthPingSeconds"] =
                "Ablage: Sekunden pro EINWÄRTS-Ping — ein Ring, der sich auf den Kartenumriss "
                + "zusammenzieht, das Spiegelbild der Ringe, die der Gegenstands-Stapel nach außen "
                + "wirft. Nach außen heißt 'schau hierher', nach innen heißt 'leg es hier hinein'. "
                + "0 = kein Ping.",
            ["Cards/ItemBerthPingReach"] =
                "Ablage: wie weit außerhalb des Kartenumrisses dieser Einwärts-Ping beginnt, als "
                + "Vielfaches der Karte. Größer = er fegt aus größerer Entfernung herein und ist im "
                + "Augenwinkel leichter zu erwischen. 1 = kein Weg.",
            ["Cards/ItemBerthRevealSeconds"] =
                "Ablage: Sekunden, die die Ablage zum HINEINWACHSEN braucht, wenn ein Gegenstand "
                + "ablegbar wird, und zum Zusammenfallen, wenn er es nicht mehr ist. Früher blinkte sie "
                + "übergangslos ein und aus — der einzige Übergang im Gegenstands-Ablauf, der nie "
                + "animiert war.",
            ["Cards/CardSoundsEnabled"] =
                "Hauptschalter für die karteneigenen Klänge des Mods (Fächer auf/zu, Karte greifen, "
                + "ablegen, zurücknehmen). Aus = der Mod spielt keinen davon; die Klänge des Spiels "
                + "selbst bleiben unberührt. Die fünf *Sound-Einträge darunter behalten ihre "
                + "Audio-Item-Namen in jedem Fall — dieser Schalter ist ein UND darüber, kein "
                + "Umschreiben.",
            ["Cards/FanRevealSound"] =
                "Spiel-Audio-Item, das einmal abgespielt wird, wenn sich der Handflächen-Fächer aufdeckt "
                + "(Demeo spielt MotherbrainAudio.OnCardHandShow, CardHandView.cs:682). PlaySound_EnemyCardDraw "
                + "ist das Kartenzieh-Rauschen des Spiels (InitiativeTrack.cs:59); Alternativen aus GH.Runtime: "
                + "PlaySound_CardUI_SelectCard, PlaySound_UICardTabSelect, PlaySound_CardUI_DiscardedCard, "
                + "PlaySound_CardUI_BurnedCard. Leer = stumm.",
            ["Cards/FanHideSound"] =
                "Spiel-Audio-Item, das einmal abgespielt wird, wenn sich der Handflächen-Fächer verbirgt "
                + "(Demeo spielt MotherbrainAudio.OnCardHandHide, CardHandView.cs:691). "
                + "PlaySound_UICardTabSelect ist das weiche Karten-Tab-Ticken. Leer = stumm.",
            ["Cards/CardGrabSound"] =
                "Spiel-Audio-Item, das einmal abgespielt wird, wenn eine Karte gegriffen wird (aus dem "
                + "Fächer, einem Board-Slot, dem Auswahlfeld oder einem Stapel bzw. der Aktiv-Spalte gezupft — "
                + "Nähe-Griff wie Laser-Zupfen gleichermaßen). Das Spiel selbst spielt bei einem körperlichen "
                + "Griff nichts. PlaySound_UICardTabSelect ist das weiche Karten-Tab-Ticken des Spiels — ein "
                + "leises Aufnehmen. Alternativen: PlaySound_UIButtonSelect, PlaySound_CardUI_SelectCard. Leer "
                + "= stumm.",
            ["Cards/CardPlaceSound"] =
                "Spiel-Audio-Item, das einmal abgespielt wird, wenn eine gehaltene Karte dort landet, wo das "
                + "SPIEL keinen eigenen Ton spielt: beim Umsortieren Tray→Tray oder beim erneuten Ablegen einer "
                + "Auswahlkarte. Ablagen, die eine Karte auswählen (Fächer→Slot, Tausch, Auswahl bestätigen), "
                + "spielen absichtlich KEINEN Mod-Ton — dort feuert der spieleigene AbilityCardUI-Profilklick "
                + "(mouseDownAudioItem), sobald das eingereihte SelectCard aufgelöst wird, und ihn zu "
                + "verdoppeln war der Fehler mit zwei Tönen pro Ablage. Alternativen: "
                + "PlaySound_ScenarioUI_TileConfirm, PlaySound_UIButtonSelect. Leer = stumm.",
            ["Cards/CardTakeBackSound"] =
                "Spiel-Audio-Item, das einmal abgespielt wird, wenn eine Karte zurückgenommen wird, während "
                + "das Spiel stumm bleibt (erneutes Öffnen der Auswahl — die Karte wurde vom spieleigenen Pfad "
                + "\"andere Karte wählen\" bereits abgewählt). Normale Rücknahmen spielen über das eingereihte "
                + "UnselectCard den spieleigenen Profilklick und fügen keinen Mod-Ton hinzu. "
                + "PlaySound_UIUndoHex ist der weiche Feld-Abbruchklick des Spiels — klingt nach Rückgängig. "
                + "Alternativen: PlaySound_ScenarioUIUndo, PlaySound_UICardTabSelect. Leer = stumm.",
            ["Cards/FanPerCardStepDegrees"] =
                "Handfächer (global, In-VR-Debug-Kategorie 'Kartenfächer'): Obergrenze des Winkelschritts pro "
                + "Karte in Grad. Wichtigster Regler für kleine/mittlere Hände — jede zusätzliche Karte fächert "
                + "um diesen Betrag auf, bis der Gesamtbogen FanArcSweepDegrees erreicht. Größer = benachbarte "
                + "Karten liegen weiter auseinander (leichter einzeln anzuvisieren). Startwert 14° (die alte "
                + "lokale Konstante aus Item 8).",
            ["Cards/FanArcSweepDegrees"] =
                "Handfächer (global): gesamter Bogenwinkel des Fächers in Grad — bestimmt GROSSE Hände "
                + "(sobald genug Karten da sind, um die Schritt-Obergrenze zu erreichen, legt dies fest, wie "
                + "weit sich die volle Hand herumzieht: höher = ein runderer, kreisförmigerer Fächer). "
                + "Startwert 91° (das alte FanArcDegrees 70 × den Bogenfaktor 1.3).",
            ["Cards/FanEffectiveRadius"] =
                "Handfächer (global): effektiver Bogenradius in echten Metern. Schafft echten Abstand "
                + "zwischen den Kartenmittelpunkten (Sehne ∝ Radius·sin(Schritt/2)) und vergrößert den "
                + "freiliegenden Greifstreifen entsprechend mit. Startwert 0.1792 m (das alte FanRadius 0.16 × "
                + "den Radiusfaktor 1.12). Ersetzt FanRadius nur für den Handfächer (der Blätterfächer der "
                + "Stapel behält seinen eigenen).",
            ["Cards/FanHoverSplitScale"] =
                "Handfächer (global): Faktor der Hover-Spreizung — multipliziert FanSplitMultiplier, damit "
                + "die Lücke, die der Fächer um eine überfahrene Karte öffnet, im Verhältnis zum (größeren) "
                + "Kartenabstand bleibt. Startwert 1.45 (die alte lokale Konstante aus Item 8).",
            ["Cards/BrowseFanOffset"] =
                "Ankerversatz des BLÄTTERFÄCHERS der Stapel, board-lokale Meter, ADDIERT zur festen Basispose "
                + "über dem Board (x 0, y Board-Oberkante + 0.26, z -0.05). X = entlang der langen Board-Achse "
                + "(+rechts), Y = nach oben über die Board-Fläche, Z = aus der Board-Fläche heraus (NEGATIV = "
                + "zum Spieler hin). GLOBAL (nicht pro Board): der Anker ist board-LOKAL und folgt damit "
                + "ohnehin Pose und Größe jedes Boards. Live: PileBrowser liest ihn bei offenem Blätterfächer "
                + "jeden Frame neu, sodass die Stepper 'Browse X/Y/Z' im Element Stapel des VR-Debug-Menüs den "
                + "offenen Fächer sofort bewegen. Startwert 0 (heutige Platzierung).",
            ["Cards/FanSideDepthCurve"] =
                "Handfächer (global): TIEFENKRÜMMUNG — vorzeichenbehaftete Wölbung (echte Meter) der "
                + "ÄUSSERSTEN Karte entlang der Fächer-Vorwärtsachse, sodass eine volle Hand sich wie ein "
                + "echter Fächer in die Tiefe wölbt. POSITIV = Randkarten weichen VOM Betrachter ZURÜCK (Mitte "
                + "am nächsten); NEGATIV = sie wölben sich ANDERSHERUM, ZUM Betrachter hin (Mitte am weitesten "
                + "weg). Die Wölbung ist quadratisch (FanCurvePower) im Abstand zur Mitte und wächst mit der "
                + "Handgröße ein (flach unter FanCurveMinCards, voll bei FanMaxHandForCurve). "
                + "Greif-/Hover-Raycasting folgt bei beiden Vorzeichen. 0 = flach (das alte Billboard-Blatt).",
            ["Cards/FanCurvePower"] =
                "Handfächer (global): Exponent der Tiefenkrümmung, angewendet auf den Mittenabstands-Anteil "
                + "jeder Karte (0 bei der mittleren Karte, 1 bei der äußersten). 2 = quadratisch (sanft nahe "
                + "der Mitte, zu den Rändern hin steiler — wirkt wie ein echter Fächer); 1 = ein gerader Keil; "
                + "höher = flachere Mitte mit schärferem Zurückweichen der Ränder.",
            ["Cards/FanCurveMinCards"] =
                "Handfächer (global): Kartenzahl, bei/unter der der Fächer FLACH bleibt (keine "
                + "Tiefenwölbung). Die Krümmung wächst von hier linear bis FanMaxHandForCurve ein, sodass 1-3 "
                + "Karten flach wirken und eine volle Hand sich deutlich krümmt.",
            ["Cards/FanGazeBias"] =
                "Handfächer (global): die blickabhängige Ausrichtungs-DREHUNG einschalten (optional, Standard "
                + "AUS). AUS = der Fächer richtet sich als Billboard gleichmäßig zum Kopf aus, und die "
                + "TIEFENkrümmung (FanSideDepthCurve) ist die einzige Formantwort — das ruhige, vorhersehbare "
                + "Nachführen. AN fügt wieder eine geglättete Zusatzdrehung hinzu, die den Fächer ein Stück in "
                + "die Blickrichtung des Kopfes dreht, sodass sich die angeschaute Kante nach vorne neigt; eine "
                + "BREITE Totzone plus Seiten-Hysterese sorgt dafür, dass ein Kopfschütteln nach links/rechts "
                + "nahe der Fächermitte nicht mehr zwischen den Neigerichtungen flattert (er hält die Mitte, "
                + "bis der Blick sich klar auf eine Seite festlegt). ABGELÖST durch FanFaceViewer + "
                + "FanGazeApexFollow — AUS lassen, außer du willst die zusätzliche Neigung.",
            ["Cards/FanFaceViewer"] =
                "Handfächer (global): EINWÄRTSDREHUNG jeder Karte zum Kopf. Der Fächer billboardet als Ganzes "
                + "zum Kopf, aber eine Karte 13 cm weiter außen auf dem Bogen wird noch ~15-20° neben ihrer "
                + "eigenen Normalen gesehen — sie ist genau dann weggedreht, wenn du zum Lesen hinschaust. Dies "
                + "richtet stattdessen JEDE Karte zum Kopf aus (wie ein in der Hand gewölbtes echtes Blatt). 0 "
                + "= aus (flaches Billboard-Blatt, das alte Aussehen), 1 = exakte Ausrichtung pro Karte. Nur "
                + "die Ausrichtung: Positionen, Zeichenreihenfolge, Hover-Spreizung und Laser-Trefferflächen "
                + "bleiben gleich (die Auswahl liest die gezeichnete Ruhe-Rotation).",
            ["Cards/FanGazeApexFollow"] =
                "Handfächer (global): wie stark dein BLICK die Tiefenwölbung AUFHEBT. Die Wölbung "
                + "(FanSideDepthCurve) lässt Karten mit wachsendem Abstand zur Handmitte vom Betrachter "
                + "zurückweichen, sodass die äußerste Karte — die, zu der du den Kopf drehst, um sie zu lesen — "
                + "am WEITESTEN weg ist; eine Karte anzuschauen machte sie also schwerer erkennbar. Bei 1 wird "
                + "die Karte unter deinem Blick vollständig aus diesem Zurückweichen herausgehoben (ihre "
                + "Nachbarn teilweise, auslaufend), bei 0.5 zur Hälfte, bei 0 gar nicht (= die schlichte "
                + "symmetrische Wölbung, ein bit-genaues Zurücksetzen). Die Aufhebung ENTFERNT immer nur "
                + "Zurückweichen: keine Karte kann bei irgendeinem Blickwinkel weiter hinten landen als bei 0. "
                + "Die Reaktion ist eine glatte Funktion des Blickwinkels, ohne Schwelle, ohne Einrasten und "
                + "ohne Quantisierung pro Karte — den Kopf weiter zu einer Karte zu drehen kann diese Karte "
                + "immer nur weiter nach vorne bringen — und wird mit FanGazeSmoothing geglättet. "
                + "Silhouetten-sicher: die Wölbung verläuft entlang der BLICKACHSE, das Aufheben ändert also, "
                + "was am nächsten ist, ohne den Fächer sichtbar zu bewegen.",
            ["Cards/FanGazeSmoothing"] =
                "Handfächer (global): exponentielle Glättungsrate (1/s, unskalierte Zeit), mit der der "
                + "Blick-Scheitelpunkt der Karte folgt, die du gerade ansiehst. Niedriger = träger/gemächlicher "
                + "und völlig unempfindlich gegen Kopfzittern; höher = der Fächer präsentiert die angeschaute "
                + "Karte schneller. 8 erreicht ~90 % einer Kopfdrehung in ~0.3 s.",
            // ---- [WorldUI] ----
            ["WorldUI/ButtonCluster"] = "Physische Knöpfe für Bereit/Rückgängig/Überspringen am Tischrand.",
            ["WorldUI/CombatLog"] = "Kampflog als Welt-Tafel an der gegenüberliegenden Tischseite.",
            ["WorldUI/Dialogs"] = "Bestätigungsdialoge als Welt-Modale vor dem HMD (Ja/Nein antippen).",
            ["WorldUI/DecisionDock"] =
                "Entscheidungs-/Bestätigungsabfragen im Szenario (die Verbrennen-Wahl beim Schadennehmen, der "
                + "Verbrennen-Bestätigungsdialog \"diese verbrennen / andere Karte wählen\" und jede weitere "
                + "Abfrage in der DecisionDock-Registry) zeigen ihre ECHTEN Spiel-Widgets — die tatsächlichen "
                + "Knöpfe/Schalter, damit Beschriftung, Lokalisierung und Aktiv-Zustände nativ sind — angedockt "
                + "in einer reservierten Zone UNTER den beiden Karten auf dem Kontrollbrett, solange die "
                + "Abfrage offen ist, statt als schwebendes flaches Fenster (Test #22, verallgemeinert das "
                + "Schadennehmen-Dock aus Test #21). Der Kartenfächer bleibt für Folgeauswahlen verfügbar (kein "
                + "ModalUI). Aus = der generische Modal-Rückfall lässt wie bisher das ganze Fenster schweben. "
                + "(Umbenannt von \"TakeDamageBoard\".)",
            ["WorldUI/TrayNativeControls"] =
                "Dockt die ECHTEN Widgets des Spiels für Weiter/Bestätigen (ReadyButton), Rückgängig "
                + "(UndoButton) und kurze Rast (ShortRest) auf dem Kontrollbrett an — die tatsächlichen "
                + "Spielknöpfe mit nativem Sprite, lokalisierter Beschriftung und Aktiv-/Inaktiv-Zuständen, "
                + "über denselben Andock-Mechanismus wie DecisionDock — anstelle der vom Mod gezeichneten "
                + "BESTÄTIGEN-/RÜCKGÄNGIG-Knöpfe und des Kurzrast-Tokens (Test #23, Punkt 4). Die lange Rast "
                + "hat kein eigenes uGUI-Widget (sie wird über den Kartenfächer + Weiter gewählt) und bleibt "
                + "daher ein Mod-Token. STANDARDMÄSSIG AUS (Test #27, Punkt 3): die echten Widgets nur "
                + "anzudocken, SOLANGE sie spielseitig aktiv sind, und beim Ausblenden zum Mod-Knopf "
                + "zurückzufallen, ließ die Board-Knöpfe sichtbar zwischen dem flachen Spiel-Widget und dem "
                + "3D-Mod-Knopf FLACKERN. Die vom Mod gezeichneten Board-Knöpfe tragen inzwischen selbst die "
                + "abgetastete native Spiel-Optik (NativeButtonSkin, Test #26); AUS gelassen bekommt daher "
                + "jeder Board-Knopf EIN dauerhaftes, einheitliches Aussehen im Spielstil, ganz ohne Wechsel. "
                + "An = das Andocken der echten Widgets wieder aktivieren (nimmt das Flackern in Kauf).",
            ["WorldUI/BarFixedSize"] =
                "LP-/Effekt-Balken der Figuren ignorieren den KOPFABSTAND — ein Balken ist gleich groß, ob du "
                + "dich vorbeugst oder zurücktrittst (Test #14: die alte Abstandskompensation ließ die Balken "
                + "beim Zurücktreten auf bis zu 2.5x anwachsen, was als \"Wachsen\" der Balken wahrgenommen "
                + "wurde). Aus = altes Verhalten: die Balken wachsen sanft mit dem Kopfabstand, um lesbar zu "
                + "bleiben — begrenzt durch dieselbe Mindest-/Maximalgröße wie der Tischzoom. Über den ZOOM "
                + "sagt dieser Schalter nichts; dafür sind BarSizeScale und seine Grenzen da.",
            ["WorldUI/BarSizeScale"] =
                "GRÖSSE der LP-/Effekt-Balken über den Miniaturen, als Faktor der Größe, die sie seit jeher "
                + "haben. Die Einheit hinter dem Faktor sind ECHTE MILLIMETER AM AUGE: 1.0 = CanvasScaleMm x "
                + "0.35 mm pro uGUI-Pixel vor deinem Gesicht, 2.0 ist also ein doppelt so hoher und doppelt so "
                + "breiter Balken — bei jedem Tischzoom. Das ist die einzige Größenangabe, die einen "
                + "Pinch-Zoom überlebt, denn der Zoom des Mods ist eine Skalierung des RIGS: eine Größe in "
                + "Welteinheiten sähe auf jeder Zoomstufe anders groß aus. Standard 1.0 = genau die Größe von "
                + "vor diesem Regler (beim ausgelieferten Tischzoom), es ändert sich also nichts, bis du ihn "
                + "verstellst. Live: schon das nächste Bild wird in der neuen Größe gezeichnet. Bereich "
                + "0.25-3.",
            ["WorldUI/BarZoomMinScale"] =
                "MINDESTGRÖSSE der Figurenbalken, als Faktor von BarSizeScale. Die Balken folgen dem "
                + "TISCHZOOM — ziehst du den Tisch größer, wächst ein Balken mit seiner Miniatur mit, schiebst "
                + "du ihn weg, schrumpft er mit ihr — und dies ist der Boden dieses Mitwachsens: wie weit du "
                + "auch herauszoomst, ein Balken wird nie kleiner als BarSizeScale x dieser Wert. 0.7 = "
                + "höchstens 30 % kleiner als die von dir eingestellte Größe. Setzt du ihn gleich "
                + "BarZoomMaxScale, ist das Mitwachsen ganz aus und der Balken hat auf jeder Zoomstufe genau "
                + "eine echte Größe. Bereich 0.1-1.",
            ["WorldUI/BarZoomMaxScale"] =
                "MAXIMALGRÖSSE der Figurenbalken, als Faktor von BarSizeScale — die Decke des bei "
                + "BarZoomMinScale beschriebenen Mitwachsens mit dem Tischzoom. Wie weit du auch hineinzoomst, "
                + "ein Balken wird nie größer als BarSizeScale x dieser Wert; ein herangezoomter Tisch kann die "
                + "Balken also nie das Brett verschlucken lassen. 1.5 = höchstens 50 % größer als die von dir "
                + "eingestellte Größe. Bereich 1-3.",
            ["WorldUI/WristHud"] =
                "Kompakter Charakterstatus (LP/EP/Zustände/Gold) am nicht-dominanten Handgelenk, per Hinsehen "
                + "aktiviert.",
            ["WorldUI/PanelMipBake"] =
                "Aliasing-Nachzügler zu [Cards] FaceMipBake: auch die Texturen, die die INITIATIVLEISTE "
                + "(RawImage-Porträts + Rahmen-/Linien-Sprites) und die Mouseover-HINWEISBOX abtasten, liefert "
                + "das Spiel OHNE Mipmaps — beide flimmern deshalb auf ihren Welt-Tafeln bei Verkleinerung, "
                + "egal welche MSAA-Stufe läuft. Bei true wird jede eindeutige miplose Textur EINMAL in eine "
                + "mipmapped trilinear/aniso-Kopie gebacken (gemeinsamer Cache mit den Kartenbildern — ein von "
                + "beiden genutzter Atlas wird nur einmal gebacken) und die Grafiken auf die Kopien umgestellt "
                + "(Originale kehren zurück, sobald eine Fläche freigegeben wird). false = Initiativleiste und "
                + "Hinweisbox tasten weiter die miplosen Originale ab.",
            ["WorldUI/CanvasScaleMm"] =
                "Welt-Canvas-Skalierung: Millimeter pro uGUI-Pixel bei Diorama-Größe 1 (Standard 1 px = 1 "
                + "mm).",
            ["WorldUI/InitiativeDepthMaxSpreadPx"] =
                "3D-Tiefeneffekt der Initiativleiste: die MAXIMALE Gesamt-z-Spanne von vorn nach hinten "
                + "(uGUI-Pixel) zwischen dem flachsten und dem tiefsten Initiativporträt. Die vorgegebene "
                + "Zeilentiefe wird proportional gestaucht, bis sie diese Obergrenze einhält (nie verstärkt). "
                + "Höher = stärkere Staffelung; 0 = flach. Live änderbar im Debug-Menü (Tafeln -> Initiative). "
                + "Bereich 0..40.",
            ["WorldUI/HoverInfoScale"] =
                "GRÖSSEN-Faktor der Infotafeln beim Mouseover — der kleinen Karten, die das Spiel einblendet, "
                + "während Zeiger/Fingerspitze über einem Brettfeld schweben (\"2 Gold\", \"Geschlossene Tür\", "
                + "Truhe, Hindernis, Druckplatte, Falle, Questgegenstand …), plus der Elementhinweis zur "
                + "Kartenaktion. Der Faktor multipliziert die Welt-Skalierung der Tafel, skaliert also die "
                + "GANZE Tafel (Rahmen + Text) gleichmäßig zusätzlich zum Diorama-/Brett-Maßstab; der Hinweis "
                + "behält so seine Proportionen und bleibt bei jeder Brettgröße und Betrachtungsentfernung "
                + "lesbar — es ist ein Zoom, kein Neu-Layout, und die Platzierung der Tafel (die aus ihren "
                + "eigenen gemessenen Ausmaßen abgeleitet wird) folgt automatisch. Standard 0.6 = die Größe von "
                + "vor diesem Regler, es ändert sich also nichts, bis du ihn verstellst; erhöhe ihn, wenn die "
                + "Mouseover-Karten im HMD zu klein wirken. Live änderbar im Debug-Menü (Anzeige -> "
                + "Infotafel-Größe): der Wert wird bei jedem Platzierungstick gelesen, eine offene Tafel ändert "
                + "ihre Größe also sofort und das nächste Mouseover erscheint gleich in der neuen Größe — kein "
                + "Neustart. Bereich 0.2-2.",
            ["WorldUI/EnemyRevealBoardClearance"] =
                "Wie weit die GEGNER-RUNDENENTHÜLLUNG (die Monster-Fähigkeitskarten, die gezeigt werden, "
                + "nachdem alle ihre Wahl bestätigt haben) die Oberkante des KONTROLLBRETTS freihalten muss, in "
                + "echten Metern, gemessen AM BRETT. Beim Erscheinen der Enthüllung schaut der Spieler "
                + "normalerweise nach UNTEN auf das Kontrollbrett; eine rein blickverankerte Platzierung landet "
                + "daher direkt hinter/im Brett und ist unlesbar. Die Enthüllung wird deshalb gerade so weit "
                + "angehoben, dass die Sichtlinie des Spielers zu ihrer UNTERKANTE um diesen Abstand über der "
                + "echten (gerenderten) Oberkante des Bretts vorbeiläuft. Da der Abstand am Brett gemessen "
                + "wird, die Enthüllung aber weiter entfernt schwebt, ist die Lücke im Bild proportional "
                + "größer. 0 = die Oberkante streifen; erhöhe den Wert, wenn die Enthüllung noch zu nah am "
                + "Brett wirkt. Live änderbar im Debug-Menü (Tafeln -> Initiative); gilt ab der nächsten "
                + "Enthüllung bzw. dem nächsten trägen Nachführschritt. Bereich 0-0.5.",
            ["WorldUI/DesktopMirrorLeftEye"] =
                "Der flache Monitor spiegelt NUR das LINKE Auge des HMD: setzt XRSettings.gameViewRenderMode "
                + "fest auf LeftEye und überspringt den Composite-Blit des 2D-Menüs auf dem Desktop, sodass der "
                + "Desktop in jedem Zustand ein sauberes Einzelaugen-Spiegelbild zeigt. Aus = altes Verhalten "
                + "(2D-Menü-Composite in Menüs, sonst unkontrollierter XR-Standardspiegel).",
            // [WorldUI] WristHud{Pitch..OffsetZ} had translations here until 2026-08-09. They are
            // retired ("LEGACY — no effect", WorldUIConfig.Bind) and a retired entry never reaches
            // the UI, so a translation for one is a promise the menu cannot keep. The live pose
            // rows are [WristHud] *Palm*, further down this table.
            ["WorldUI/ShowIntro"] =
                "Zeigt das Intro des Spiels (Logos/Video, Szenen vor dem Menü) auch in VR auf dem schwebenden "
                + "Bildschirm. Aus = altes Verhalten: das Intro läuft nur auf dem Desktop und im HMD steht eine "
                + "\"startet…\"-Anzeige in der Leere.",
            ["WorldUI/ScreenWidth"] =
                "Breite der schwebenden 2D-Leinwand in echten Metern (16:9, die Höhe folgt daraus). Ersetzt "
                + "den Schlüssel \"FlatScreenWidth\" von vor Test #6 (1.4 m wirkte auf 1.6 m Entfernung zu "
                + "klein).",
            ["WorldUI/ScreenDistance"] = "Abstand vom Kopf zur schwebenden 2D-Leinwand in echten Metern.",
            ["WorldUI/SuppressPhysicalMouse"] =
                "Deaktiviert während des VR-Betriebs die physische Desktop-Maus im InputSystem, damit ihre "
                + "(veraltete) Desktop-Position keine Karten-/Menüelemente mehr hinter deinem Rücken überfahren "
                + "oder auswählen kann — nur der VR-Laser steuert den Zeiger. Beim Beenden von VR wird die Maus "
                + "wieder aktiviert.",
            ["WorldUI/MapWindOpacity"] =
                "Deckkraft der treibenden Wind-/Wolken-Ambiente-Partikel auf der Kampagnenkarte (0..1). Die "
                + "flache Kartenkamera des Spiels bearbeitet/maskiert sie per Post-Processing, sodass sie "
                + "dezent wirken; die Forward-Erfassung in VR tut das nicht, deshalb erscheinen sie bei voller "
                + "Stärke als dicke, halbtransparente Schlieren, die über die Ortssymbole schmieren. 0.3 lässt "
                + "ein dezentes Treiben übrig; 1 = ursprüngliche Stärke; 0 = ganz ausgeblendet.",
            ["WorldUI/DragUnlockDegrees"] =
                "Wie weit (Grad) sich der Strahl von seiner Druckrichtung entfernen muss, damit die "
                + "Klick-Sperre zu einem Ziehen aufgeht.",
            ["WorldUI/DragUnlockSeconds"] =
                "Wie lange (Sekunden) der Strahl über DragUnlockDegrees hinaus bleiben muss, bevor die Sperre "
                + "aufgeht (filtert Zitter-Ausreißer einzelner Frames).",
            ["WorldUI/PokeClick"] =
                "Ein Antippen der schwebenden 2D-Leinwand mit der Zeigefingerspitze klickt an der berührten "
                + "Stelle (Druck bei Kontakt mit der Ebene, Loslassen beim Zurückziehen; Sperr-Regeln wie "
                + "oben). Auf Standardabstand liegt der Bildschirm womöglich außerhalb der Armreichweite — "
                + "beuge dich vor bzw. tritt näher, oder verringere [WorldUI] ScreenDistance.",
            ["WorldUI/PokePressDepthMm"] =
                "Eindrücktiefe in MILLIMETERN, die eine Fingerspitze DURCH die Canvas-Ebene eines flachen "
                + "(konvertierten uGUI-) Knopfes muss, bevor der Klick auslöst. Kontakt mit der Ebene SCHARFT "
                + "nur: der Knopf zeigt seine Gedrückt-Darstellung (pointerDown) mit leichtem Haptik-Tick; "
                + "tiefer löst der Klick mit stärkerem Impuls aus; Zurückziehen davor bricht lautlos ab — kein "
                + "Klick, keine Strafe, nach Verlassen der Ebene wieder scharf. Schützt vor versehentlichem "
                + "Drücken beim Streifen einer Tafel, ohne bewusstes Drücken mühsam zu machen. 0 = altes "
                + "Verhalten: Klick sofort bei Kontakt. Laser-Klicks bleiben unberührt. Bereich 0-30.",
            ["WorldUI/DecisionPokeDeliberate"] =
                "ENTSCHEIDUNGS-Knöpfe (die angedockte Verbrennen-Wahl beim Schadennehmen, der "
                + "Verbrennen-Bestätigungsdialog, das Ja/Nein der kurzen Rast) verlangen einen BEWUSSTEN "
                + "körperlichen Druck: die Berührung des Knopfes SCHARFT ihn nur (Gedrückt-Darstellung + "
                + "leichter haptischer Tick); der Klick löst aus, wenn die Fingerspitze bewusst aus der Ebene "
                + "ZURÜCKGEZOGEN wird und dabei die Auslösetiefe überschreitet; ein Durchwischen mit der Hand "
                + "oder ein seitliches Verlassen bricht lautlos ab — kein Klick, keine Strafe. Schützt die "
                + "teuren, unumkehrbaren Entscheidungsabfragen vor versehentlichem Sofortauslösen. Gilt NUR für "
                + "das körperliche Antippen von Entscheidungs-Dock-Knöpfen; jede andere konvertierte Fläche "
                + "behält den Eindrück-Druck über PokePressDepthMm, und Laser-Klicks bleiben unberührt. Aus = "
                + "Entscheidungsknöpfe drücken sich wie alles andere.",
            ["WorldUI/CombatLogFollowSeat"] =
                "Ankermodus der Kampflog-Tafel (der FOLGEN/FIXIERT-Pin schaltet um). False (FIXIERT, "
                + "Standard): die Tafel steht STATISCH IN DER WELT — bei Szenariobeginn einmal aus den "
                + "gespeicherten Versätzen platziert, dann eingefroren, bis sie gegriffen wird. True (FOLGEN): "
                + "sie leitet ihren Platz neu aus Tischanker + Sitzdrehung ab (bewegt sich mit Neuzentrieren "
                + "und Diorama wie andere Welt-Tafeln; die Ausrichtung wird weiter nur beim Neuzentrieren "
                + "abgeleitet, nie pro Frame). Ersetzt CombatLogFollow aus Test #19: dessen Folgen-Standard "
                + "plus Gier-Billboard pro Tick wirkte, als liefe die Tafel dem Kopf nach (Test #20).",
            ["WorldUI/CombatLogForward"] =
                "Versatz der Kampflog-Tafel vom Tischanker in Sitz-Vorwärtsrichtung, echte Meter (Standard = "
                + "der alte Bogenplatz: Azimut 56° bei 1.10 m). Wird automatisch gespeichert, sobald die "
                + "Greifleiste der Tafel losgelassen wird.",
            ["WorldUI/CombatLogRight"] =
                "Versatz der Kampflog-Tafel nach rechts vom Sitz aus, echte Meter (wird beim Loslassen des "
                + "Griffs gespeichert).",
            ["WorldUI/CombatLogUp"] =
                "Höhe der Kampflog-Tafel über der Tischebene, echte Meter (wird beim Loslassen des Griffs "
                + "gespeichert).",
            ["WorldUI/CombatLogScale"] =
                "Größenfaktor der Kampflog-Tafel (Skalieren mit zwei Händen; begrenzt auf 0.5-2).",
            ["WorldUI/CombatLogUserClosed"] =
                "Der Spieler hat das Kampflog über den X-Knopf oben rechts (oder den Schalter \"Kampflog "
                + "anzeigen\" in den VR-Einstellungen) ausgeblendet. Solange true, kehrt die Tafel an ihren "
                + "2D-Platz zurück und erscheint im Szenario NICHT von selbst wieder; der Einstellungs-Schalter "
                + "löscht den Wert und zeigt die Tafel erneut. Bewusst getrennt von [WorldUI] CombatLog (dem "
                + "Hauptschalter der Funktion), damit das Wiederanzeigen weder den Funktionsschalter noch das "
                + "gespeicherte Layout stört.",
            ["WorldUI/PanelsFollowView"] =
                "VERALTETES Verhalten (vor Test #8): die Welt-Tafeln (Initiativleiste, Elementtafel, "
                + "Kampflog, Aufgaben, Zugleiste) leiten ihre Platzierung in jedem Frame neu aus der aktuellen "
                + "Rig-Drehung ab und schwenken deshalb bei jedem stufenweisen Drehen / Welt-Greifen um den "
                + "Tisch — was als schwebendes HUD wahrgenommen wird. Standard false: die Tafeln sind AM TISCH "
                + "IN DER WELT FIXIERT und verankern sich nur beim Neuaufbau des Rigs oder beim Neuzentrieren "
                + "neu.",
            ["WorldUI/HexHintFollowView"] =
                "Solange ein Mouseover-Hinweis zu einem Brettfeld (die TextInfoPanel/PropInfoPanel-Popups, z. "
                + "B. \"Geschlossene Tür\") gezeigt wird, wandert er mit TRÄGER (kritisch gedämpfter) Bewegung, "
                + "die dem Kopf folgt und zur Ruhe kommt, an eine bequem lesbare Stelle nahe der MITTE des "
                + "Blickfelds, statt am festen Tisch-Andockplatz von PropInfoSurface zu bleiben. So oder so "
                + "bleibt er aufrecht und richtet sich zum Kopf aus. Aus = Andockposition beibehalten und ihn "
                + "nur zum Kopf ausrichten.",
            ["WorldUI/HexHintDistance"] =
                "Wie weit VOR dem Kopf ein Mouseover-Hinweis zu einem Brettfeld steht, solange er "
                + "gezeigt wird, in echten Metern (skaliert mit dem Diorama). Größer = weiter weg und "
                + "kleiner wirkend. Live wirksam; nur sinnvoll, wenn \"Feld-Hinweis folgt Blick\" an ist.",
            ["WorldUI/HexHintDrop"] =
                "Wie weit UNTERHALB der Blickmitte ein Mouseover-Hinweis zu einem Brettfeld steht, in "
                + "echten Metern (skaliert mit dem Diorama). Positiv = tiefer, negativ = oberhalb der "
                + "Blickmitte. Live wirksam; nur sinnvoll, wenn \"Feld-Hinweis folgt Blick\" an ist.",
            ["WorldUI/HexHintSide"] =
                "Seitliche Verschiebung eines Mouseover-Hinweises gegenüber der Blickmitte, in echten "
                + "Metern (skaliert mit dem Diorama). Positiv = nach rechts, negativ = nach links. 0 = "
                + "mittig (bisheriges Verhalten). Live wirksam; nur sinnvoll, wenn \"Feld-Hinweis folgt "
                + "Blick\" an ist.",
            ["WorldUI/ModalStyle"] =
                "Wie 2D-Rückfallfenster im Szenario (Story-Boxen, Ereignisse, Tutorials, ESC-Menü, "
                + "Belohnungen, Auswahldialoge …) in VR nutzbar werden (P8, Test #12). \"window\" (Standard): "
                + "nur GENAU DIESES Fenster wird zur Welt-Tafel vor dem HMD, per Antippen UND Laser bedienbar, "
                + "und kehrt beim Schließen an seinen 2D-Platz zurück; die volle Leinwand erscheint nur, wenn "
                + "ein Fenster sich nicht umwandeln lässt (Grund wird protokolliert). \"screen\": Verhalten vor "
                + "P8 — für jedes Rückfallfenster erscheint das komplette 2D-Composite. Die manuelle "
                + "A/X-Kombination holt davon unabhängig immer die volle Leinwand.",
            ["WorldUI/ManualScreenChordSeconds"] =
                "Haltedauer (Sekunden) von A/X der nicht-dominanten Hand für das manuelle Umschalten der "
                + "Leinwand.",
            ["WorldUI/ScreenLayerSplit"] =
                "Rendert die schwebende 2D-Leinwand als ZWEI Ebenen (Hardware-Test #18): die UI-Kameras des "
                + "Spiels — deren Screen-Space-Camera-Canvases immer nur über ihre zugewiesene Kamera rendern, "
                + "weshalb Stereo-Spiegelkameras sie nie reproduzieren können und das Menü einäugig wurde — "
                + "zeichnen auf ein transparentes \"Glas\"-Quad, das beiden Augen identisch in der "
                + "Leinwandebene gezeigt wird, während 3D-Szenenkameras und Videos einige cm dahinter eine "
                + "Hintergrundebene mit augenweiser Stereotiefe rendern. Aus (oder bei jedem Fehler): Rückfall "
                + "auf eine einzelne RT — eine flache Mono-Leinwand in beiden Augen, nie einäugig.",
            // ---- [SettingsPanel] ----
            ["Keyboard/AutoCapitalise"] =
                "Schreibt den ersten Buchstaben jedes Wortes groß und den Rest klein. Die Tastatur "
                + "des Spiels sendet Tasten-CODES und bildet Buchstaben auf ihren Großbuchstaben-"
                + "Namen ab; ohne diese Korrektur entsteht \"MEINE TAPFERE GRUPPE\". Aus = jeder "
                + "Buchstabe kommt genau so an, wie die Spiel-Tastatur ihn liefert.",
            ["WorldUI/DevShowAllPanels"] =
                "DEV: erzeugt das Welt-Tafel-Layout mit Platzhalter-Inhalt auf dem Desktop (kein HMD nötig).",
            ["WorldUI/DevForceConvert"] =
                "DEV: wendet die echten Canvas-Umwandlungen im Dev-Modus ohne HMD an (das verschiebt die "
                + "2D-Tafeln des Spiels in den 3D-Raum — die Desktop-Ansicht ändert sich entsprechend).",
            // ---- [Hands] ----
            // The per-style ROLL, one entry per style — Loc.ConfigDescription matches section+key
            // exactly, and these keys are built by interpolation, so there is no single row to write.
            ["Hands/GloveGripYawDegrees"] =
                "Gierwinkel (Grad) der sichtbaren Hand um ihre Hochachse, während der Handstil Glove "
                + "(Lederhandschuh) getragen wird — wohin die Finger zeigen. WIRD GESPIEGELT wie der "
                + "Rollwinkel: die linke Hand erhält den negierten Wert, eine positive Zahl dreht "
                + "also beide Hände gleich herum bezogen auf die jeweilige Körperseite. Mitspieler "
                + "sehen es, und gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/GloveSpreadOffset"] =
                "Wie weit die beiden Hände AUSEINANDER sitzen (Meter), während der Handstil Glove "
                + "(Lederhandschuh) getragen wird: POSITIV schiebt die linke Hand nach links und die rechte "
                + "nach rechts, negativ führt sie zusammen. GESPIEGELT — genau das unterscheidet es "
                + "von LateralOffset, das beide Hände gleichsinnig verschiebt (das Paar wandert "
                + "gemeinsam) und mit keinem Wert den Abstand ändern kann. Mitspieler sehen es, und "
                + "gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/PlateGripYawDegrees"] =
                "Gierwinkel (Grad) der sichtbaren Hand um ihre Hochachse, während der Handstil Plate "
                + "(Panzerhandschuh) getragen wird — wohin die Finger zeigen. WIRD GESPIEGELT wie der "
                + "Rollwinkel: die linke Hand erhält den negierten Wert, eine positive Zahl dreht "
                + "also beide Hände gleich herum bezogen auf die jeweilige Körperseite. Mitspieler "
                + "sehen es, und gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/PlateSpreadOffset"] =
                "Wie weit die beiden Hände AUSEINANDER sitzen (Meter), während der Handstil Plate "
                + "(Panzerhandschuh) getragen wird: POSITIV schiebt die linke Hand nach links und die rechte "
                + "nach rechts, negativ führt sie zusammen. GESPIEGELT — genau das unterscheidet es "
                + "von LateralOffset, das beide Hände gleichsinnig verschiebt (das Paar wandert "
                + "gemeinsam) und mit keinem Wert den Abstand ändern kann. Mitspieler sehen es, und "
                + "gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/ArcaneGripYawDegrees"] =
                "Gierwinkel (Grad) der sichtbaren Hand um ihre Hochachse, während der Handstil Arcane "
                + "(Magierhandschuh) getragen wird — wohin die Finger zeigen. WIRD GESPIEGELT wie der "
                + "Rollwinkel: die linke Hand erhält den negierten Wert, eine positive Zahl dreht "
                + "also beide Hände gleich herum bezogen auf die jeweilige Körperseite. Mitspieler "
                + "sehen es, und gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/ArcaneSpreadOffset"] =
                "Wie weit die beiden Hände AUSEINANDER sitzen (Meter), während der Handstil Arcane "
                + "(Magierhandschuh) getragen wird: POSITIV schiebt die linke Hand nach links und die rechte "
                + "nach rechts, negativ führt sie zusammen. GESPIEGELT — genau das unterscheidet es "
                + "von LateralOffset, das beide Hände gleichsinnig verschiebt (das Paar wandert "
                + "gemeinsam) und mit keinem Wert den Abstand ändern kann. Mitspieler sehen es, und "
                + "gehaltene Figuren und Karten folgen mit. Live änderbar.",
            ["Hands/GloveGripRollDegrees"] =
                "Rollwinkel (Grad) der sichtbaren Hand um die Vorwärtsachse des Controllers, während der "
                + "Handstil Glove (Lederhandschuh) getragen wird — die Verdrehung, die eine Hand auf dem Controller "
                + "schief wirken lässt. WIRD ZWISCHEN DEN HÄNDEN GESPIEGELT: beide Controller melden lokale "
                + "Achsen gleicher Händigkeit, ein unverändert auf beide angewandter Wert würde sie also in "
                + "Weltkoordinaten gleichsinnig verdrehen und das Paar unsymmetrisch machen. Die linke Hand "
                + "erhält daher den negierten Wert, und eine positive Zahl dreht beide Handflächen gleich "
                + "herum bezogen auf die jeweilige Körperseite. MITSPIELER SEHEN ES: auf der Leitung liegt die "
                + "SICHTBARE Hand, und in der Hand gehaltene Figuren und Karten hängen an derselben "
                + "Wurzel und folgen mit. Live änderbar.",
            ["Hands/PlateGripRollDegrees"] =
                "Rollwinkel (Grad) der sichtbaren Hand um die Vorwärtsachse des Controllers, während der "
                + "Handstil Plate (Panzerhandschuh) getragen wird — die Verdrehung, die eine Hand auf dem Controller "
                + "schief wirken lässt. WIRD ZWISCHEN DEN HÄNDEN GESPIEGELT: beide Controller melden lokale "
                + "Achsen gleicher Händigkeit, ein unverändert auf beide angewandter Wert würde sie also in "
                + "Weltkoordinaten gleichsinnig verdrehen und das Paar unsymmetrisch machen. Die linke Hand "
                + "erhält daher den negierten Wert, und eine positive Zahl dreht beide Handflächen gleich "
                + "herum bezogen auf die jeweilige Körperseite. MITSPIELER SEHEN ES: auf der Leitung liegt die "
                + "SICHTBARE Hand, und in der Hand gehaltene Figuren und Karten hängen an derselben "
                + "Wurzel und folgen mit. Live änderbar.",
            ["Hands/ArcaneGripRollDegrees"] =
                "Rollwinkel (Grad) der sichtbaren Hand um die Vorwärtsachse des Controllers, während der "
                + "Handstil Arcane (Magierhandschuh) getragen wird — die Verdrehung, die eine Hand auf dem Controller "
                + "schief wirken lässt. WIRD ZWISCHEN DEN HÄNDEN GESPIEGELT: beide Controller melden lokale "
                + "Achsen gleicher Händigkeit, ein unverändert auf beide angewandter Wert würde sie also in "
                + "Weltkoordinaten gleichsinnig verdrehen und das Paar unsymmetrisch machen. Die linke Hand "
                + "erhält daher den negierten Wert, und eine positive Zahl dreht beide Handflächen gleich "
                + "herum bezogen auf die jeweilige Körperseite. MITSPIELER SEHEN ES: auf der Leitung liegt die "
                + "SICHTBARE Hand, und in der Hand gehaltene Figuren und Karten hängen an derselben "
                + "Wurzel und folgen mit. Live änderbar.",
            ["Hands/GlovePinkyCounterAbduction"] =
                "Nur Handstil GLOVE: Grad Gegen-Abspreizung (Drehung um das lokale Z der Kleinfinger-Wurzel = "
                + "Handflächen-Normale), angewandt bei voller Krümmung und mit dem Krümmungswert skaliert. Der "
                + "MESH-Schlauch des Glove-Kleinfingers neigt sich ~18° nach außen, während seine Knochenkette "
                + "gerade ist — eine reine Faust um lokal X lässt den gekrümmten Kleinfinger sichtbar "
                + "abgespreizt stehen; dies zieht ihn beim Krümmen zum Ringfinger zurück (das Vorzeichen kippt "
                + "für die rechte Hand automatisch). 0 schaltet ab. Live änderbar.",
            ["Cards/AssetOffset_*"] =
                "Positionsversatz NUR des Brett-Meshes, in brettlokalen Metern. Die sechs Anker "
                + "(Kartenslots, Rast-Marken, Bestätigen/Rückgängig) und alles, was an ihnen hängt, "
                + "BLEIBEN STEHEN — BoardPosOffset verschiebt das ganze Brett samt Elementen, diese "
                + "Option schiebt nur das Asset darunter weg. PRO BRETT. Ausgeliefert mit 0.",
            ["Cards/AssetRotation_*"] =
                "VERALTET — ohne Wirkung, ersetzt durch AssetPitch/Yaw/RollDegrees_<Brett>. Ein "
                + "Vector3, dessen Schlüssel kein Einheitenwort trug — das Menü stellte ihn in "
                + "Hundertstel Grad.",
            ["Cards/AssetPitchDegrees_*"] =
                "NEIGUNG nur des Brett-Meshes, in Grad — kippt das Asset zur Spielerin hin oder weg, "
                + "um die Brettwurzel. Anker und angedockte Elemente bleiben stehen (BoardTilt neigt "
                + "das GANZE Brett; das hier nur das Asset). PRO BRETT. Ausgeliefert mit 0.",
            ["Cards/AssetYawDegrees_*"] =
                "GIERUNG nur des Brett-Meshes, in Grad — dreht das Asset flach um die Brettwurzel. "
                + "Anker und angedockte Elemente bleiben stehen. PRO BRETT. Ausgeliefert mit 0.",
            ["Cards/AssetRollDegrees_*"] =
                "ROLLEN nur des Brett-Meshes, in Grad — rollt das Asset um die Brettwurzel. Anker und "
                + "angedockte Elemente bleiben stehen. Der Mesh-Collider wandert mit, der Laser trifft "
                + "also weiterhin, was du siehst. PRO BRETT. Ausgeliefert mit 0.",
            ["Hands/GhostHandOnHeldCard"] =
                "Macht eine Hand AUCH dann halbtransparent, wenn sie eine KARTE hält — die Finger "
                + "umschließen genau die Kartenkunst, für die man die Karte hochgenommen hat. "
                + "Unabhängig von GhostHandOnFan: jede der beiden Optionen kann allein an sein, und "
                + "beide Hände können gleichzeitig geistern (je eine Karte pro Hand). Nutzt dieselbe "
                + "Stärke (GhostHandStrength), ist live einstellbar, vollständig umkehrbar und "
                + "synchronisiert — Mitspieler sehen deine Hände exakt wie du.",
            ["Hands/GhostHandOnFan"] =
                "Macht die Hand, die gerade den GEÖFFNETEN Kartenfächer hält, halbtransparent "
                + "(\"Geisterhand\"), damit das Hand-Mesh keine Kartendetails mehr verdeckt. Die Hand bleibt "
                + "sichtbar — nur ihre Deckkraft sinkt (Stärke: GhostHandStrength). Standardmäßig AUS; an den "
                + "Händen ändert sich nichts, bis du es einschaltest. Live änderbar, vollständig umkehrbar (die "
                + "Überblendung läuft auf privaten Materialkopien pro Renderer, nie auf den gemeinsamen "
                + "Handmaterialien) und wird auf den Avatar-Spiegel und auf die Sicht der Mitspieler auf dich "
                + "übertragen.",
            ["Hands/GhostHandStrength"] =
                "Transparenz-STÄRKE der Geisterhand bei geöffnetem Kartenfächer: 0 = völlig undurchsichtig, 1 "
                + "= völlig unsichtbar (Material-Alpha = 1 - Stärke). Begrenzt, damit die Hand nie ganz "
                + "verschwindet — du musst weiterhin sehen, wo deine Finger sind, um eine Karte zu greifen. "
                + "Live änderbar; dieselbe Stärke wird an die Mitspieler gesendet, damit deine Geisterhand auf "
                + "deren Bildschirmen identisch aussieht.",
            ["Hands/TestFist"] =
                "DEBUG: erzwingt eine VOLLE Faust (Krümmung 1.0 an allen fünf Fingern beider Hände) "
                + "unabhängig vom Controller-Eingang. Schalte dies ein, um die maximale Faust zu sehen, die das "
                + "aktuelle Rig erzeugen kann — sieht diese Faust richtig aus, schließt aber das Zudrücken des "
                + "Controllers die Hand nicht, liegt der Verlust am EINGANG (der Griff-Wert erreicht nie den "
                + "vollen Bereich); bleibt selbst diese Faust offen, liegt er am Rig bzw. an den Winkeln (dann "
                + "CurlProximal/CurlMiddle/CurlTip erhöhen).",
            ["Hands/CurlProximal"] =
                "Drehung bei voller Krümmung (Grad, lokales X) des PROXIMALEN Gelenks (Wurzel/Knöchel) jedes "
                + "Fingers bei Krümmung 1.0. Der proximale Daumenwinkel skaliert proportional mit (Standard: "
                + "Daumen 25 bei Finger 75). Live änderbar.",
            ["Hands/CurlMiddle"] =
                "Drehung bei voller Krümmung (Grad, lokales X) des MITTLEREN Gelenks jedes Fingers bei "
                + "Krümmung 1.0. Der mittlere Daumenwinkel skaliert proportional mit (Standard: Daumen 45 bei "
                + "Finger 95). Live änderbar.",
            ["Hands/CurlTip"] =
                "Drehung bei voller Krümmung (Grad, lokales X) des SPITZEN-Gelenks (distal) jedes Fingers bei "
                + "Krümmung 1.0. Der Daumen-Spitzenwinkel skaliert proportional mit (Standard: Daumen 60 bei "
                + "Finger 65). Live änderbar.",
            ["Hands/CurlInputFullAt"] =
                "Roher analoger Griff-/Trigger-Wert (0.3-1.0), der bereits als VOLLE Krümmung zählt: Krümmung "
                + "= Rohwert / dieser Wert, auf 1 begrenzt. Quest-3-Controller über Virtual Desktop bleiben "
                + "beim bequemen vollen Zudrücken oft unter 1.0 stehen — senke diesen Wert, wenn die Logzeile "
                + "'[Hands] squeeze released: peak grip=...' zeigt, dass dein Spitzenwert nie 1.0 erreicht. 1.0 "
                + "= roher, nicht umgerechneter Eingang.",
            ["Hands/*GripPitchDegrees"] =
                "Neigung (Grad) zwischen der getrackten OpenXR-Griffpose und der sichtbaren Hand, während "
                + "dieser Handstil getragen wird — NEGATIV kippt die Fingerspitzen nach UNTEN. PRO STIL "
                + "absoluter Wert (ersetzt das alte gemeinsame GripPitchOffsetDegrees + Trimmung in der "
                + "Haupt-cfg; beim ersten Start daraus übernommen). Live änderbar — die Hände sitzen im "
                + "nächsten Frame neu.",
            ["Hands/*LateralOffset"] =
                "Seitlicher Versatz (Meter, Geräteachse X; POSITIV = zur Daumenseite) der sichtbaren Hand "
                + "gegenüber der Griffpose, während dieser Handstil getragen wird. PRO STIL absoluter Wert "
                + "(ersetzt das alte gemeinsame HandLateralOffset + Trimmung; beim ersten Start daraus "
                + "übernommen). Live änderbar.",
            ["Hands/*VerticalOffset"] =
                "Vertikaler Versatz (Meter, Geräteachse Y; POSITIV = nach oben) der sichtbaren Hand gegenüber "
                + "der Griffpose, während dieser Handstil getragen wird. PRO STIL absoluter Wert (ersetzt das "
                + "alte gemeinsame HandVerticalOffset + Trimmung; beim ersten Start daraus übernommen). Live "
                + "änderbar.",
            ["Hands/*ForwardOffset"] =
                "Vorwärts-/Tiefenversatz (Meter, Geräteachse Z; POSITIV = zu den Fingerspitzen) der "
                + "sichtbaren Hand gegenüber der Griffpose, während dieser Handstil getragen wird. PRO STIL "
                + "absoluter Wert (ersetzt das alte gemeinsame HandForwardOffset + Trimmung; beim ersten Start "
                + "daraus übernommen). Live änderbar.",
            // ---- [WristHud] ----
            ["WristHud/*PalmPitch"] =
                "Neigung (Pitch, Grad) der Arm-Anzeige zusätzlich zur Grundausrichtung — 0 ist die "
                + "ausgelieferte Lage: flach auf der Handfläche, lesbar wenn du die Handfläche zu dir "
                + "drehst. PRO STIL absoluter Wert, während dieser Handstil getragen wird. Live änderbar — "
                + "WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*PalmYaw"] =
                "Drehung (Gieren, Grad) der Arm-Anzeige zusätzlich zur Grundausrichtung; 0 ist die "
                + "ausgelieferte Lage. PRO STIL absoluter Wert, während dieser Handstil getragen wird. "
                + "Live änderbar — WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*PalmRoll"] =
                "Rollen (Grad) der Arm-Anzeige zusätzlich zur Grundausrichtung; 0 ist die ausgelieferte "
                + "Lage. PRO STIL absoluter Wert, während dieser Handstil getragen wird. Live änderbar — "
                + "WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*PalmSideOffset"] =
                "Versatz der Arm-Anzeige QUER über die Hand (echte Meter). PRO STIL absoluter Wert, "
                + "während dieser Handstil getragen wird. Live änderbar — WristHud wendet die Pose bei "
                + "jedem Tick neu an.",
            ["WristHud/*PalmFingerOffset"] =
                "Versatz der Arm-Anzeige IN RICHTUNG DER FINGER (echte Meter; negativ schiebt sie zurück "
                + "auf den Unterarm). PRO STIL absoluter Wert, während dieser Handstil getragen wird. "
                + "Live änderbar — WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*PalmLiftOffset"] =
                "Abstand der Arm-Anzeige VON DER HANDFLÄCHE WEG (echte Meter) — wie weit die Tafel vor "
                + "der Hand schwebt. PRO STIL absoluter Wert, während dieser Handstil getragen wird. "
                + "Live änderbar — WristHud wendet die Pose bei jedem Tick neu an.",
            // ---- [RoundButtons] ----
            ["RoundButtons/OffsetX"] =
                "Seitlicher Versatz (Meter im WURZEL-Frame des Boards, +X = zur rechten Board-Kante / zu den "
                + "Rückgängig-Zahnrad-Feldern) der angedockten ÜBERSPRINGEN-Taste ('Bewegung überspringen' / "
                + "'Angriff überspringen') gegenüber ihrem Standard-Anker — sie ist das einzige sichtbare "
                + "Mitglied der Kurzzeit-Knopfgruppe der Rundenphase. Live änderbar; begrenzt auf -0.30..0.30.",
            ["RoundButtons/OffsetY"] =
                "Versatz das Board hinauf (Meter im WURZEL-Frame des Boards, +Y = zu den Kartenplätzen / zur "
                + "fernen Kante, -Y = zur unteren Kante und zum Haltegriff) der angedockten ÜBERSPRINGEN-Taste "
                + "gegenüber ihrem Standard-Anker. Live änderbar; begrenzt auf -0.30..0.30.",
            ["RoundButtons/OffsetZ"] =
                "Versatz aus der Ebene heraus (Meter im WURZEL-Frame des Boards, +Z = AUS dem Board heraus "
                + "zum Spieler, -Z = versenkt in bzw. hinter die Board-Fläche) der angedockten "
                + "ÜBERSPRINGEN-Taste gegenüber ihrem standardmäßig erhabenen Sitz. Live änderbar; begrenzt "
                + "auf -0.30..0.30.",
            ["RoundButtons/Shape"] =
                "Kappenform der angedockten ÜBERSPRINGEN-Taste: Round = abgeflachter Puck, Square = kantige "
                + "Tastenkappe (dann gelten Width/Height/Depth dieses Abschnitts; Square ist der "
                + "Auslieferungsstandard). Live änderbar.",
            ["RoundButtons/CapSize"] =
                "Kappenradius (Meter im Cluster-Frame) der ÜBERSPRINGEN-Taste. Die automatische Spaltenanpassung "
                + "VERKLEINERT nur unter diesen Wert, wenn sich mehrere Knöpfe die Spalte teilen müssen; ein "
                + "einzelner Knopf nutzt genau diese Größe — am Steuerbrett immer der Fall. Live änderbar; "
                + "begrenzt auf 0.015..0.09.",
            ["RoundButtons/Width"] =
                "Kappenbreite (Meter) der ÜBERSPRINGEN-Taste bei Shape=Square. Gilt NUR für diese Gruppe. Live "
                + "änderbar; begrenzt auf 0.02..0.20.",
            ["RoundButtons/Height"] =
                "Kappenhöhe (Meter) der ÜBERSPRINGEN-Taste bei Shape=Square. Gilt NUR für diese Gruppe. Live "
                + "änderbar; begrenzt auf 0.015..0.20.",
            ["RoundButtons/Depth"] =
                "Kappentiefe/-extrusion (Meter zum Spieler hin) der ÜBERSPRINGEN-Taste bei Shape=Square. Gilt "
                + "NUR für diese Gruppe. Live änderbar; begrenzt auf 0.006..0.08.",
            ["RoundButtons/Travel"] =
                "Druckweg (Meter) der ÜBERSPRINGEN-Taste — wie weit die Kappe unter der Fingerspitze einsinkt, "
                + "bevor der tiefenbasierte Druck auslöst (löst bei 90% des Wegs aus). Gilt NUR für diese "
                + "Gruppe. Live änderbar; begrenzt auf 0.002..0.02.",
            // ---- [BoardButtons] ----
            ["BoardButtons/Width"] =
                "Kappenbreite (Meter, entlang der X-Achse des Boards) der Fortfahren/Rückgängig-Tastenkappen "
                + "auf dem Kontrollbrett. Gilt NUR für Fortfahren/Rückgängig (eckige Form). Live änderbar; "
                + "begrenzt auf 0.02..0.20.",
            ["BoardButtons/Height"] =
                "Kappenhöhe (Meter, entlang der Y-Achse des Boards) der Fortfahren/Rückgängig-Tastenkappen. "
                + "Gilt NUR für Fortfahren/Rückgängig (eckige Form). Live änderbar; begrenzt auf 0.015..0.20.",
            ["BoardButtons/Depth"] =
                "Kappentiefe/-extrusion (Meter zum Spieler hin) der Fortfahren/Rückgängig-Tastenkappen. Gilt "
                + "NUR für Fortfahren/Rückgängig. Live änderbar; begrenzt auf 0.006..0.08.",
            ["BoardButtons/Travel"] =
                "Druckweg (Meter) der Fortfahren/Rückgängig-Tastenkappen — Einsinktiefe der Kappe und "
                + "Druckstrecke der Tiefenauslösung. Gilt NUR für Fortfahren/Rückgängig. Live änderbar; "
                + "begrenzt auf 0.002..0.02.",
            // ---- [BoardDashboard] ----
            ["BoardDashboard/PinWidth"] =
                "Kappenbreite (Meter) der Platte des Folgen/Fixieren-Schalters ('Fixiert') auf dem "
                + "Kontrollbrett. Gilt NUR für diesen Schalter. Live änderbar; begrenzt auf 0.02..0.20.",
            ["BoardDashboard/Height"] =
                "Kappenhöhe (Meter) der Zahnrad- und der Folgen/Fixieren-Platte. Gilt NUR für diese beiden. "
                + "Live änderbar; begrenzt auf 0.015..0.20.",
            ["BoardDashboard/Depth"] =
                "Kappentiefe/-extrusion (Meter zum Spieler hin) der Zahnrad- und der Folgen/Fixieren-Platte. "
                + "Gilt NUR für diese beiden. Live änderbar; begrenzt auf 0.006..0.08.",
            ["BoardDashboard/Travel"] =
                "Druckweg (Meter) der Zahnrad- und der Folgen/Fixieren-Platte. Gilt NUR für diese beiden. "
                + "Live änderbar; begrenzt auf 0.002..0.02.",
            // ---- [RestButtons] ----
            ["RestButtons/Width"] =
                "Kappenbreite (Meter) der Tastenkappen für kurze/lange RAST in der Rastzone des Boards, "
                + "solange ihre Form pro Board Square ist. Runde Rast-Scheiben behalten den pro Board "
                + "vorgegebenen Durchmesser. Gilt NUR für die Rast-Knöpfe. Live änderbar; begrenzt auf "
                + "0.02..0.20.",
            ["RestButtons/Height"] =
                "Kappenhöhe (Meter) der Tastenkappen für kurze/lange RAST, solange ihre Form pro Board Square "
                + "ist. Gilt NUR für die Rast-Knöpfe. Live änderbar; begrenzt auf 0.015..0.20.",
            ["RestButtons/Depth"] =
                "Kappentiefe/-extrusion (Meter zum Spieler hin) der Tastenkappen für kurze/lange RAST — für "
                + "BEIDE Formen, Round-Scheiben wie Square-Kappen. Gilt NUR für die Rast-Knöpfe. Live änderbar; "
                + "begrenzt auf 0.006..0.08.",
            ["RestButtons/Travel"] =
                "Druckweg (Meter) der Tastenkappen für kurze/lange RAST — Einsinktiefe der Kappe und "
                + "Druckstrecke der Tiefenauslösung. Gilt NUR für die Rast-Knöpfe. Live änderbar; begrenzt auf "
                + "0.002..0.02.",
            // ---- [ButtonColors] ----
            ["ButtonColors/LabelR"] =
                "Farbe der gravierten Tastenkappen-BESCHRIFTUNG — ROT-Kanal (0..1). Färbt den Text auf JEDER "
                + "3D-Tastenkappe (Fortfahren/Rückgängig, Zahnrad/Fixiert, kurze/lange Rast, "
                + "Rundenphasen-Cluster) UND die angedockten nativen Knopfbeschriftungen. Standard 0.984 = das "
                + "helle warme Pergament (#FBF3E0). Live änderbar.",
            ["ButtonColors/LabelG"] =
                "Farbe der gravierten Tastenkappen-BESCHRIFTUNG — GRÜN-Kanal (0..1). Standard 0.953 "
                + "(#FBF3E0). Live änderbar.",
            ["ButtonColors/LabelB"] =
                "Farbe der gravierten Tastenkappen-BESCHRIFTUNG — BLAU-Kanal (0..1). Standard 0.878 "
                + "(#FBF3E0). Live änderbar.",
            ["ButtonColors/LabelOutline"] =
                "Zeichnet die dunkle KONTUR um die Tastenkappen-Beschriftung (der Gravurrand, der helle "
                + "Zeichen von einer hellen Messingkappe trennt). AUS für eine flache Beschriftung. Standard "
                + "true. Live änderbar.",
            ["ButtonColors/LabelOutlineR"] =
                "KONTURFARBE der Tastenkappen-Beschriftung — ROT-Kanal (0..1). Standard 0.09 = dunkles Umbra. "
                + "Live änderbar.",
            ["ButtonColors/LabelOutlineG"] =
                "KONTURFARBE der Tastenkappen-Beschriftung — GRÜN-Kanal (0..1). Standard 0.06 = dunkles "
                + "Umbra. Live änderbar.",
            ["ButtonColors/LabelOutlineB"] =
                "KONTURFARBE der Tastenkappen-Beschriftung — BLAU-Kanal (0..1). Standard 0.03 = dunkles "
                + "Umbra. Live änderbar.",
            ["ButtonColors/LabelOutlineWidth"] =
                "KONTURBREITE der Tastenkappen-Beschriftung, Anteil der SDF-Streuung (dicker = schwererer "
                + "dunkler Rand). Standard 0.20. Live änderbar; begrenzt auf 0..1.",
            ["ButtonColors/LabelUnderlay"] =
                "Zeichnet den weichen dunklen Schlagschatten (UNTERLEGUNG) unter der "
                + "Tastenkappen-Beschriftung (ein zweiter Kontrasthinweis auf hellen Kappen). AUS lässt den "
                + "Schatten weg. Standard true. Live änderbar.",
            ["ButtonColors/BoardCapTintR"] =
                "FARBTON der Kappenfläche der Fortfahren/Rückgängig-Tastenkappen — ROT-Kanal (0..1), wird "
                + "MULTIPLIKATIV auf die Kappenfläche gelegt (natives Sprite UND prozedural). 1 = unverändert; "
                + "niedriger = dunkler/weniger rot, damit weißer Text lesbar bleibt. Live änderbar.",
            ["ButtonColors/BoardCapTintG"] =
                "Farbton der Kappenfläche der Fortfahren/Rückgängig-Tastenkappen — GRÜN-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/BoardCapTintB"] =
                "Farbton der Kappenfläche der Fortfahren/Rückgängig-Tastenkappen — BLAU-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/DashCapTintR"] =
                "Farbton der Fläche der Dashboard-Platten Zahnrad + Fixiert (Folgen/Fixieren) — ROT-Kanal "
                + "(0..1). 1 = unverändert. Live änderbar.",
            ["ButtonColors/DashCapTintG"] =
                "Farbton der Fläche der Dashboard-Platten Zahnrad + Fixiert — GRÜN-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/DashCapTintB"] =
                "Farbton der Fläche der Dashboard-Platten Zahnrad + Fixiert — BLAU-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/ClusterCapTintR"] =
                "Farbton der Kappenfläche im Rundenphasen-Cluster (Bereit/Rückgängig/Überspringen) — "
                + "ROT-Kanal (0..1). 1 = unverändert. Live änderbar.",
            ["ButtonColors/ClusterCapTintG"] =
                "Farbton der Kappenfläche im Rundenphasen-Cluster — GRÜN-Kanal (0..1). 1 = unverändert. Live "
                + "änderbar.",
            ["ButtonColors/ClusterCapTintB"] =
                "Farbton der Kappenfläche im Rundenphasen-Cluster — BLAU-Kanal (0..1). 1 = unverändert. Live "
                + "änderbar.",
            ["ButtonColors/RestCapTintR"] =
                "Farbton der Kappenfläche der Tastenkappen für kurze/lange RAST — ROT-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/RestCapTintG"] =
                "Farbton der Kappenfläche der Tastenkappen für kurze/lange RAST — GRÜN-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            ["ButtonColors/RestCapTintB"] =
                "Farbton der Kappenfläche der Tastenkappen für kurze/lange RAST — BLAU-Kanal (0..1). 1 = "
                + "unverändert. Live änderbar.",
            // ---- [ButtonAnim] ----
            ["ButtonAnim/Enable"] =
                "Spielt die Erscheinen-/Verschwinden-Animation der 3D-Tastenkappen (Fortfahren/Rückgängig, "
                + "Zahnrad/Fixiert, kurze/lange Rast und die Kurzzeit-Knöpfe des Rundenphasen-Clusters). AN: "
                + "ein passendes Paar — ein verschwindender Knopf ZERFÄLLT ZU STAUB (schrumpft weg, Staubstoß "
                + "in seiner Flächenfarbe), ein erscheinender MATERIALISIERT SICH AUS STAUB (zusammenlaufende "
                + "Staubkörnchen legen sich auf die Kappe, während ihre Oberfläche auf volle Farbe hochblendet "
                + "— kein Größensprung). AUS: Knöpfe erscheinen/verschwinden sofort (kein Staub, keine Blende). "
                + "Die Eingabe ist so oder so sofort aktiv. Standard true. Live änderbar.",
            ["ButtonAnim/AppearParticles"] =
                "Erzeugt die zusammenlaufende \"sich zusammensetzende\" Staubwolke, wenn eine Tastenkappe "
                + "ERSCHEINT (der Zerfalls-Staubstoß des Verschwindens rückwärts — dasselbe gepoolte System, "
                + "gleiche Dichte). AUS = nur die Einblendung der Oberfläche. Standard true. Live änderbar.",
            ["ButtonAnim/DisappearSeconds"] =
                "Sekunden, die die Kappe wegschrumpft, während der Staubstoß läuft, wenn eine Tastenkappe "
                + "VERSCHWINDET (das logische Ausblenden — Eingabe aus, Neuumbruch des Layouts — erfolgt "
                + "ohnehin sofort). Standard 0.16. Live änderbar; begrenzt auf 0.05..1.0.",
            ["ButtonAnim/AppearSeconds"] =
                "Sekunden des ERSCHEINENS aus Staub (zusammenlaufende Staubkörnchen legen sich auf die Kappe, "
                + "während ihre Oberfläche an Ort und Stelle vom Staub auf volle Farbe hochblendet — kein "
                + "Größensprung). Eingabe/Collider sind ab dem ersten Frame aktiv — die Animation ist rein "
                + "visuell. Standard 0.15. Live änderbar; begrenzt auf 0.05..1.0.",
            // ---- [TransientButtons] ----
            ["TransientButtons/OffsetX"] = "veraltet",
            ["TransientButtons/OffsetY"] = "veraltet",
            ["TransientButtons/Shape"] = "veraltet",
            ["TransientButtons/CapSize"] = "veraltet",
            // ---- [SquareCaps] ----
            ["SquareCaps/Width"] = "veraltet",
            ["SquareCaps/Height"] = "veraltet",
            ["SquareCaps/Depth"] = "veraltet",
            ["SquareCaps/Travel"] = "veraltet",
            // ---- [Board] ----
            ["Board/TouchTilesWithFingertip"] =
                "Tippe ein hervorgehobenes Feld direkt mit der Zeigefingerspitze an — das löst genau die "
                + "Aktion aus, die auch ein Klick mit dem Laser auf dieses Feld auslöst. Es passiert nur, "
                + "solange der GRIFF gedrückt ist (Faust mit ausgestrecktem Zeigefinger), damit ein "
                + "versehentliches Streifen über das Brett niemals etwas auslöst. Ein Auslöser pro Feld: "
                + "verlasse das Feld, hebe den Finger ab oder lass den Griff los, um den nächsten scharf "
                + "zu machen.",
            ["Board/TouchRange"] =
                "Wie nah (in echten Metern, mit dem Diorama skaliert) die Zeigefingerspitze über dem "
                + "Spielbrett sein muss, bevor das Zeigen aus der Nähe den Fernstrahl ablöst.",
            ["Board/SnapToHexCenter"] =
                "Rastet den projizierten Zielpunkt (den virtuellen Spiel-Cursor) auf die Mitte des "
                + "überfahrenen Hex-Felds ein — stabilisiert die Verankerung von Hover und Tooltip auf kleinen "
                + "Feldern.",
            ["Board/HoverHaptics"] =
                "Haptischer Impuls in der zeigenden Hand, sobald der Zielpunkt auf ein neues gültiges Ziel "
                + "auf dem Spielbrett wandert.",
            ["Board/AoeFlickThreshold"] =
                "Waagerechte Auslenkung des Thumbsticks (0.2-0.95), die ein aktives AoE-Muster um einen "
                + "Schritt von 60 Grad dreht (links = gegen den Uhrzeigersinn, rechts = im Uhrzeigersinn).",
            ["Board/AoeRepeatInterval"] =
                "Sekunden zwischen den AoE-Drehschritten, solange der Stick ausgelenkt bleibt. Werte unter "
                + "0.3 arbeiten gegen die spieleigene Richtungssperre in RotateAOEClockwise (sie ignoriert "
                + "Richtungswechsel innerhalb von 0.3 s).",
            // ---- [FigureGrab] ----
            ["FigureGrab/GrabFigures"] =
                "Greife eine Figur vom Spielbrett (Held ODER Monster) mit dem TRIGGER in die Hand, um sie aus "
                + "der Nähe zu betrachten — reine Immersion, keine Auswirkung auf das Spiel. Loslassen setzt "
                + "sie zurück auf ihr Feld auf dem Spielbrett.",
            ["FigureGrab/PickRadiusMillimeters"] =
                "Wie nah dein GREIFPUNKT (die Stelle zwischen Daumen und Zeigefinger, an der eine gehaltene "
                + "Figur sitzt) an eine Figur heran muss, damit sie als die zu greifende aufleuchtet — in "
                + "echten MILLIMETERN AN DEINER HAND, also die Strecke, die deine eigene Hand zurücklegt, "
                + "und keine Strecke auf dem Spielbrett. Der Bereich wächst NICHT mit, wenn du den Tisch "
                + "kleiner zoomst: dieselben 40 mm Reichweite decken dann einfach weniger Felder ab. "
                + "Gemessen wird bis zur Oberfläche der Figur, große Figuren bleiben also leicht zu "
                + "erwischen. Kleiner stellen, falls du weiterhin versehentlich Figuren aufnimmst; 130 ist "
                + "die alte handbreite Reichweite, bei der Schweben irgendwo über einer Figur genügte.",
            ["FigureGrab/StretchScaleMin"] =
                "Kleinste GESAMTGRÖSSE, die eine Figur in deiner Hand haben darf, als Faktor der Größe, die "
                + "sie beim STANDARD-Tischzoom zeigt (0,5 = halb so groß). Die Grenze greift, egal wie die "
                + "Größe zustande kam: eine Figur, die du weit herausgezoomt greifst, kommt genau in dieser "
                + "Größe in die Hand statt noch winziger, und auch die Zwei-Hand-Ziehgeste kann sie nicht "
                + "darunter schrumpfen. Die Geste ist ein Verhältnis — ziehst du zurück nach außen, läuft die "
                + "Figur durch jede Größe zurück — das hier ist also eine Klemme, keine Stufe. Ohne Wirkung, "
                + "solange StretchLimits aus ist.",
            ["FigureGrab/StretchScaleMax"] =
                "Größte GESAMTGRÖSSE, die eine Figur in deiner Hand haben darf, als Faktor der Größe, die "
                + "sie beim STANDARD-Tischzoom zeigt (3 = dreifach). Die Grenze greift, egal wie die Größe "
                + "zustande kam: eine Figur, die du so tief hereingezoomt greifst, dass sie größer wäre, "
                + "kommt genau in dieser Größe in die Hand, und auch die Zwei-Hand-Ziehgeste kann sie nicht "
                + "darüber hinaus vergrößern. Gilt nur für das Halten: Loslassen gleitet die Figur immer auf "
                + "ihre echte Brettgröße zurück. Ohne Wirkung, solange StretchLimits aus ist.",
            ["FigureGrab/StretchLimits"] =
                "Ober- und Untergrenze der Figurengröße in der Hand (StretchScaleMin/Max) überhaupt "
                + "durchsetzen. Aus = eine Figur in der Hand darf jede Größe annehmen, die Greif-Zoom und "
                + "Ziehgeste ergeben; nur eine winzige technische Untergrenze hält die Größe positiv. Live: "
                + "der nächste Griff und der nächste Gesten-Frame folgen der neuen Einstellung; eine bereits "
                + "gehaltene Figur behält ihre Größe, bis du etwas tust (sie an Ort und Stelle umzuklemmen "
                + "wäre ein sichtbarer Sprung).",
            ["FigureGrab/HeldFigureInfo"] =
                "Zeigt beim Aufnehmen einer Figur die neben ihr angedockte Info-Tafel (dieselbe "
                + "Werte-Karte, die das Spiel beim Daraufzeigen zeigt). Aus = beim Aufnehmen erscheint "
                + "keine Tafel. Live: Ausschalten schließt eine offene Tafel sofort; Einschalten wirkt ab "
                + "dem nächsten Aufnehmen.",
            ["FigureGrab/HeldOffsetForward"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldOffsetForward. "
                + "Dieser Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim "
                + "ersten Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: Versatz der Halteposition in Richtung Fingerspitzen "
                + "(lokales Z des Greif-Ankers) — schiebt die Figur hinaus zum Pinch-Griff zwischen Daumen und "
                + "Zeigefinger.",
            ["FigureGrab/HeldOffsetUp"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldOffsetUp. Dieser "
                + "Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim ersten "
                + "Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: Versatz der Halteposition aus der Handfläche heraus "
                + "(lokales Y des Greif-Ankers).",
            ["FigureGrab/HeldOffsetSide"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldOffsetSide. Dieser "
                + "Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim ersten "
                + "Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: seitlicher Versatz der Halteposition (lokales X des "
                + "Greif-Ankers) hin zum Pinch-Griff zwischen Daumen und Zeigefinger.",
            ["FigureGrab/HeldUpright"] =
                "Hält die Figur AUFRECHT (stehend, nach oben zeigend), im Pinch-Griff zwischen Daumen und "
                + "Zeigefinger und dir zugewandt — so, wie man eine Schachfigur betrachtet. False = alte, flach "
                + "auf der Handfläche liegende Haltung. LIVE änderbar (anders als die übrigen Held*-Einträge in "
                + "diesem Abschnitt): das hier ist ein Modus, keine Geometrie, deshalb blieb er global statt "
                + "pro Handstil zu gelten.",
            ["FigureGrab/HeldTiltDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldTiltDegrees. "
                + "Dieser Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim "
                + "ersten Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: Neigung beim Halten (Grad) — kippt die Figur zum "
                + "Betrachten zu deinem Gesicht hin.",
            ["FigureGrab/HeldFaceYawDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldFaceYawDegrees. "
                + "Dieser Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim "
                + "ersten Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: nur im aufrechten Modus: zusätzliche Drehung (Grad), "
                + "die die Vorderseite der Figur zu dir dreht. Setze 180, wenn sie dir den Rücken zuwendet.",
            ["FigureGrab/HeldUprightAtGrab"] =
                "Stellt die Figur im Moment des Greifens KOPFOBEN IN DER WELT auf, egal aus welchem "
                + "Winkel du zugegriffen hast — Handfläche nach unten, von der Seite, kopfüber. Das "
                + "wird EINMALIG beim Greifen festgelegt: danach hängt die Figur wie gewohnt an der "
                + "Hand, du kannst sie also mit dem Handgelenk weiterhin in jede Lage drehen. Es ist "
                + "keine Zwangsführung, die die Figur ständig wieder aufrichtet, während du ihre "
                + "Unterseite ansehen willst. Die Winkel unten bleiben Offsets — mit dieser Option "
                + "sind sie Offsets gegenüber „aufrecht“ statt gegenüber der Hand, du wirst sie also "
                + "einmal neu einstellen wollen.",
            ["FigureGrab/*HeldRotPitch"] =
                "NEIGUNG (Grad): kippt die Figur vor und zurück, um die Achse quer durch deine Handfläche. "
                + "Das ist das alte HeldTiltDegrees unter einem Namen, der die Achse benennt. PRO HANDSTIL.",
            ["FigureGrab/*HeldRotYaw"] =
                "GIERUNG (Grad): dreht die Figur um IHRE EIGENE Hochachse — eine Drehung, nie ein Kippen, "
                + "bringt also die lesbare Vorderseite zu dir. Wird vor Neigung und Rollung auf die Figur "
                + "angewandt; genau das hält sie zu einer reinen Drehung, egal wie die anderen beiden "
                + "stehen. Zwischen den Händen GESPIEGELT — du stellst die rechte ein, die linke folgt. "
                + "PRO HANDSTIL.",
            ["FigureGrab/*HeldRotRoll"] =
                "ROLLUNG (Grad): dreht die Figur um IHRE EIGENE Vorwärtsachse. Zwischen den Händen "
                + "GESPIEGELT wie die Gierung. PRO HANDSTIL.",
            ["FigureGrab/*HeldRollDegrees"] =
                "Gehaltene ROLLUNG (Grad) um die Achse, die aus deinen Fingerspitzen zeigt — die dritte "
                + "Drehachse, damit eine Figur in der Hand dieselbe Freiheit hat wie die Hand selbst: drei "
                + "Verschiebungen und drei Winkel. Zwischen den Händen GESPIEGELT wie die Gierung, du "
                + "stellst also nur die rechte Hand ein. PRO HANDSTIL.",
            ["FigureGrab/*HeldOffsetSide"] =
                "Seitlicher Versatz der Halteposition (lokales X des Greif-Ankers) hin zum Pinch-Griff "
                + "zwischen Daumen und Zeigefinger. PRO STIL geltender absoluter Wert, solange dieser Handstil "
                + "getragen wird (ersetzt den gemeinsamen alten Eintrag, aus dem er beim ersten Lauf befüllt "
                + "wurde). Live änderbar — eine gehaltene Figur wird sofort neu ausgerichtet.",
            ["FigureGrab/*HeldOffsetUp"] =
                "Versatz der Halteposition aus der Handfläche heraus (lokales Y des Greif-Ankers). PRO STIL "
                + "geltender absoluter Wert, solange dieser Handstil getragen wird (ersetzt den gemeinsamen "
                + "alten Eintrag, aus dem er beim ersten Lauf befüllt wurde). Live änderbar — eine gehaltene "
                + "Figur wird sofort neu ausgerichtet.",
            ["FigureGrab/*HeldOffsetForward"] =
                "Versatz der Halteposition in Richtung Fingerspitzen (lokales Z des Greif-Ankers). PRO STIL "
                + "geltender absoluter Wert, solange dieser Handstil getragen wird (ersetzt den gemeinsamen "
                + "alten Eintrag, aus dem er beim ersten Lauf befüllt wurde). Live änderbar — eine gehaltene "
                + "Figur wird sofort neu ausgerichtet.",
            ["FigureGrab/*HeldTiltDegrees"] =
                "Neigung beim Halten (Grad) — kippt die Figur zum Betrachten zu deinem Gesicht hin. PRO STIL "
                + "geltender absoluter Wert, solange dieser Handstil getragen wird (ersetzt den gemeinsamen "
                + "alten Eintrag, aus dem er beim ersten Lauf befüllt wurde). Live änderbar — eine gehaltene "
                + "Figur wird sofort neu ausgerichtet.",
            ["FigureGrab/*HeldFaceYawDegrees"] =
                "Nur im aufrechten Modus: zusätzliche Drehung (Grad), die die Vorderseite der Figur zu dir "
                + "dreht. PRO STIL geltender absoluter Wert, solange dieser Handstil getragen wird (ersetzt den "
                + "gemeinsamen alten Eintrag, aus dem er beim ersten Lauf befüllt wurde). Live änderbar — eine "
                + "gehaltene Figur wird sofort neu ausgerichtet.",
            // ---- [HexHighlight] ----
            ["HexHighlight/SwapStableShader"] =
                "Ersetzt den OmniDecal_Shd der Feld-Hervorhebung durch den stereostabilen "
                + "GloomhavenVR/HexDecalStable des Mods (gleiche Optik, keine Tiefenrekonstruktion im "
                + "Bildschirmraum — beseitigt die \"Spiegelung\" pro Auge, die bei Kopfbewegungen mitschwimmt). "
                + "Fehlt der Shader in einem älteren Bundle, greifen stattdessen die Kill*-Regler darunter.",
            ["HexHighlight/StableZTest"] =
                "ZTest (UnityEngine.Rendering.CompareFunction) für das stabile Hex-Decal. 4 = LEqual "
                + "(Standard): der Shader gibt pro Pixel die echte Tiefe des Bodenpunkts aus (SV_Depth), sodass "
                + "Figuren auf dem Feld und Wände davor die Hervorhebung wie normale Geometrie verdecken. 8 = "
                + "Always: Originalverhalten — die Hervorhebung zeichnet durch alles hindurch (Rückfalloption "
                + "auf dem Gerät, falls die Tiefenausgabe Ärger macht). Wird bei jedem Zustandswechsel der "
                + "Hervorhebung neu gesetzt, Änderungen greifen also sofort.",
            ["HexHighlight/StableDepthBias"] =
                "Zur Kamera hin wirkender Versatz im Tiefenpuffer-Raum, der auf die ausgegebene Tiefe des "
                + "stabilen Hex-Decals addiert wird. Verhindert Z-Fighting-Flimmern gegen die Bodenplatte, auf "
                + "der die Hervorhebung liegt. Leicht erhöhen, wenn die Hervorhebung flimmert oder ausfällt; "
                + "Richtung 0 senken, wenn sie sichtbar über den untersten Rand der Figurensockel läuft.",
            ["HexHighlight/KillBorderFlame"] =
                "RÜCKFALL (nur genutzt, wenn der Tausch auf den stabilen Shader aus oder nicht verfügbar "
                + "ist): setzt _BorderFlameIntensity auf den Materialien der Feld-Hervorhebung (OmniDecal_Shd) "
                + "auf null. Entfernt die animierte Randflammen-Schicht — jene im Bildschirmraum "
                + "tiefenprojizierte Schicht, die in VR bei Kopfbewegungen mitschwimmt. Weiße Füllung und "
                + "Randlinie bleiben erhalten.",
            ["HexHighlight/KillCrosshair"] =
                "RÜCKFALL (nur genutzt, wenn der Tausch auf den stabilen Shader aus oder nicht verfügbar "
                + "ist): setzt _CrossHair auf null. Entfernt die pulsierende Zielrahmen-/Fadenkreuz-Grafik, die "
                + "während der Zielauswahl INNERHALB des Felds projiziert wird — dieselbe mitschwimmende "
                + "Projektion.",
            ["HexHighlight/LogMaterialDump"] =
                "Protokolliert Shader-Namen und die vollständige Eigenschaftsliste des Materials der "
                + "Feld-Hervorhebung für die ersten paar gesehenen Materialien (Belege zum Feinjustieren der "
                + "Korrektur).",
            // ---- [SelectionReady] ----
            ["SelectionReady/Enabled"] =
                "Lässt während der Kartenauswahl-Phase den Eintrag auf der INITIATIVLEISTE jedes Charakters, "
                + "den DU steuerst und der noch nicht zwei Karten oder eine lange Rast gewählt hat, sanft "
                + "pulsieren — so ist auf der Initiativleiste klar, für welche Charaktere noch gewählt werden "
                + "muss. Erlischt in dem Moment, in dem sich eine Figur festlegt, und wenn die Phase endet.",
            // ---- [Net] ----
            ["Net/Enabled"] =
                "Mehrspieler-Synchronisierung der VR-Verkörperung: sendet deinen Kopf + deine Hände über den "
                + "spieleigenen Netcode, damit andere VR-Spieler dich sehen (wie in Demeo), und stellt "
                + "entfernte VR-Spieler dar. Rein kosmetisch, ändert nie den Spielzustand; im Einzelspieler "
                + "wirkungslos und mit flachen/nicht modifizierten Spielern unproblematisch. AUS entfernt den "
                + "Netzwerk-Hook vollständig.",
            ["Net/MaskId"] =
                "Welche Kopfmaske der lokale Spieler trägt (0..2). Auswahl in der VR-Einstellungstafel; wird "
                + "synchronisiert, damit andere VR-Spieler die richtige Maske an dir sehen.",
            ["Net/MaskSize"] =
                "Gleichmäßige Größe deiner Kopfmaske (1 = ursprünglich gestaltete Größe). Live einstellbar in "
                + "der VR-Einstellungstafel (Avatar > Maskengröße); wird synchronisiert, sodass andere "
                + "VR-Spieler deine Maske genau in der von dir gewählten Größe sehen. Rein kosmetisch.",
            ["Net/MirrorEnabled"] =
                "Zeigt dich selbst in einem Spiegel, der vor deinem Kopf schwebt, damit du deine gewählte "
                + "Maske + Hände sehen kannst. Nur lokale kosmetische Vorschau — unabhängig vom Netzwerk, "
                + "funktioniert auch im Einzelspieler. Umschaltbar in der VR-Einstellungstafel.",
            ["Net/VersionGuard"] =
                "Mod-Versionsabgleich im Mehrspieler: Spielt ein anderer MODIFIZIERTER Spieler einen anderen "
                + "Mod-Build, erscheint ein Dialog mit der Wahl, als Flat-Spieler beizutreten (VR bleibt lokal "
                + "an, Mod-Netzwerk für die Sitzung aus) oder die Sitzung zu verlassen. Flache Spieler ohne "
                + "Mod lösen ihn nie aus. AUS überspringt den Dialog; unterschiedliche Builds sprechen dann "
                + "ihr bestmöglich kompatibles Format.",
            ["Net/RemoteBoards"] =
                "Wie viel von den Kontrollbrettern der Mitspieler du siehst: Off (nie), ActionPhaseOnly (nur "
                + "in der Aktionsphase — während der geheimen Kartenauswahl ausgeblendet), Always (immer). Rein "
                + "lokale Darstellung; ändert nie den Spielzustand. Der Anti-Cheat-Schutz greift zusätzlich "
                + "immer: während der Auswahl zeigen fremde Rundenkarten stets nur die RÜCKSEITE, erst nach dem "
                + "Aufdecken die echten Karten.",
            // ---- [WorldUI] ----
            ["WorldUI/BarsOccluded"] =
                "Die LP-/Effektleisten der Figuren werden gegen die Welt tiefengetestet: Wände verdecken sie "
                + "wie jedes andere Weltobjekt, statt dass die Leiste hindurchscheint. Optikerhaltend — die "
                + "Leisten bleiben aktiv und zur Kamera ausgerichtet, sie werden lediglich Pixel für Pixel dort "
                + "ausgeblendet, wo eine Wand davor steht. Ausschalten für die originalen, stets obenauf "
                + "gezeichneten Leisten.",
            // [WorldUI] BarsDepthStamp is gone with its binding (see ActorBars.BindConfig): the
            // bars take part in the panel-vs-panel compose again, unconditionally, per the user
            // ruling that perspective must be respected everywhere.
            ["WorldUI/StereoScreen"] =
                "Stellt die schwebende 2D-Leinwand MIT Stereo-Tiefe dar (3D-Film-/Fenstereffekt): der "
                + "erfasste 3D-Menüinhalt (Kampagnenkarte, Stadt, Slideshow-Szene) wird über modeigene "
                + "Spiegelkameras einmal pro Auge gerendert, während flache UI exakt auf der Leinwandebene "
                + "bleibt. Bedienung (Laser, Antippen, virtuelle Maus) und das Desktop-Spiegelbild bleiben "
                + "unberührt. Kostet ein zusätzliches Rendern der Menüszene pro Frame, solange die Leinwand "
                + "sichtbar ist. Aus = eine einzelne Mono-RenderTexture, exakt das Verhalten vor Stereo.",
            ["WorldUI/ScreenDepthStrength"] =
                "Stärke der Stereo-Tiefe der flachen Leinwand (skaliert den Augenversatz linear). 1 = "
                + "geometrisch aus dem IPD deines Headsets abgeleitet (fenstergenau, bewusst leicht "
                + "zurückhaltend); kleiner = flacher/angenehmer; 0 = mono (wie StereoScreen=false).",
            ["WorldUI/VideoDepthLayer"] =
                "Behält die Stereo-Tiefe bei, während ein bildschirmfüllendes 2D-Video auf der Leinwand läuft "
                + "(Ambiente-Film im Hauptmenü, Story-Videos): der VideoPlayer bleibt unangetastet in seinem "
                + "originalen Kameraebenen-Modus, und BEIDE Augen zeigen leicht verschobene Kopien des "
                + "erfassten Hintergrunds, sodass der gesamte Hintergrund (dann im Grunde nur noch das Video) "
                + "VideoDepth Meter HINTER der Glas-UI erscheint — der Hintergrund tritt zurück, das Menü "
                + "schwebt davor, ohne künstliche Geometrie. Aus = Stereo wird vollständig ausgesetzt (mono), "
                + "solange irgendein Kameraebenen-Video läuft.",
            ["WorldUI/VideoDepth"] =
                "Wie weit HINTER der Leinwandebene der Hintergrund erscheint, während ein bildschirmfüllendes "
                + "2D-Video läuft, in echten Metern (VideoDepthLayer). Disparität p = IPD*V/(D+V) mit D = "
                + "ScreenDistance: bei der Standard-Leinwand in 1.6 m und 2.2 m Tiefe erscheint das Video bei "
                + "3.8 m (~2.4x Leinwandabstand, ~36 mm Disparität — unter der Divergenzgrenze von ~63 mm; "
                + "ohnehin auf 55 mm begrenzt). Nach Test #19 von 0.8 angehoben (das Zurücktreten wirkte zu "
                + "subtil). 0 = Video auf der Leinwandebene (keine Video-Tiefe).",
            ["WorldUI/ScreenParallaxScale"] =
                "Verstärkt die szenenINTERNE Tiefe der Stereo-Leinwand (Test #16: entfernte Menükulissen "
                + "wirkten bei geometrischen Werten flach). Augenversatz UND Konvergenz werden mit demselben "
                + "Faktor multipliziert, sodass die Disparität im Unendlichen (ihr Verhältnis) konstant und "
                + "angenehm bleibt, während Tiefenunterschiede innerhalb der erfassten Szene um diesen Faktor "
                + "stärker werden — Diorama hinter Glas statt flaches Foto. 1 = strenge Fenstergeometrie; auf "
                + "1-60 begrenzt.",
        };
}
