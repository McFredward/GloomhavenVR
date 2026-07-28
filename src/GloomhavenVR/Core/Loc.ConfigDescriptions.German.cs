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
            // ---- [Batching] — the experimental runtime static-batching pass (2026-07) ----
            ["Batching/Mode"] =
                "EXPERIMENTELL UND INZWISCHEN VERMUTLICH ÜBERFLÜSSIG — lies den letzten Absatz, bevor "
                + "du es einschaltest. ÜBERHOLT am 28.07.2026: die Bündelung funktioniert (1666 "
                + "Renderer zu 47 Meshes zusammengefasst, abgesetzte Materialslots 1208 → ~132, rund "
                + "8 ms wert), aber die eigentliche Lösung war die PARALLELE BILDABGABE "
                + "([Core] EnableGraphicsJobs, standardmäßig an): sie brachte den Renderloop auf dem "
                + "Hauptthread von 14,9 ms auf 1,8 ms und das Headset von 45 Hz auf 90 Hz. Damit "
                + "bleibt auf dem Hauptthread kaum noch etwas übrig, was die Bündelung gewinnen "
                + "könnte — ob sie überhaupt noch etwas bringt, ist UNGEMESSEN. Sie kostet ~100 MB "
                + "und eine ~60-ms-Pause pro Szenarioladen; lass sie aus, außer du testest schwache "
                + "Hardware. Siehe .planning/perf/FINDINGS.md. — "
                + "Was der Bündelungs-Durchlauf tun darf. AUS (Auslieferungszustand) tut "
                + "nichts und gibt bereits Zusammengefasstes wieder frei. MESSEN untersucht die Szene und "
                + "schreibt eine [Batch] PROBE-Zeile, wie viele Renderer zusammengefasst werden KÖNNTEN, "
                + "über wie viele Materialien und zu welchen Speicherkosten — es wird NICHTS verändert. AN "
                + "fasst zusammen. Hintergrund: sechs Hardware-Sitzungen haben die Wand des VR-Bildes im "
                + "Absetzen der ZEICHENAUFRUFE auf dem Hauptthread verortet (Culling 0,08 ms gegen 21,5 ms "
                + "Submit); ein Szenario setzt ~1481 Renderer über nur ~103 verschiedene Materialien ab. "
                + "Bündelung ist der einzige Hebel dagegen, der keine Shader-Varianten braucht — anders als "
                + "Single-Pass-Stereo und anders als GPU-Instancing, das hier nichts bringt, weil die "
                + "Dungeon-Geometrie prozedural erzeugt wird und fast jeder Renderer ein eigenes Mesh hat. "
                + "Die Entwickler des Spiels haben denselben Aufruf auf einer Debug-Taste ausgeliefert.",
            ["Batching/Roots"] =
                "Kommagetrennte Namen der SZENEN-WURZELOBJEKTE, die der Durchlauf durchlaufen darf. Alles "
                + "außerhalb bleibt unangetastet. Die Vorgabe ist genau die Wurzel, die auch die "
                + "Bündelungs-Taste des Spiels benutzt und unter der laut [Perf] SCENE praktisch der ganze "
                + "Dungeon hängt (1440 der 1481 abgesetzten Renderer). Die Namen für DEIN Szenario stehen in "
                + "der SCENE-Zeile unter \"by scene-root/child group\". Ein Name, der auf nichts passt, wird "
                + "gemeldet und ignoriert — nie geraten.",
            ["Batching/AutoDetectRoots"] =
                "Existiert KEINER der in Roots genannten Namen in den geladenen Szenen, ersatzweise die "
                + "Szenen-Wurzeln mit den meisten Mesh-Objekten suchen und diese verwenden. WARUM "
                + "STANDARDMÄSSIG AN: Roots ist Freitext, und Freitext ist das Einzige, was der "
                + "VR-Konfigurationsbrowser nur ANZEIGEN kann — wer ein Szenario spielt, dessen "
                + "Geometrie-Wurzel anders als \"Maps\" heißt, könnte das aus dem Headset heraus nicht "
                + "korrigieren, und die Funktion liefe still ins Leere. Dies ist der Ausweg, und er ist "
                + "kein Blindflug: die eigenen Wurzeln des Mods werden übersprungen, ein Kandidat muss "
                + "weiterhin MinRenderers erreichen, höchstens vier werden genommen, und die [Batch] "
                + "PROBE-Zeile NENNT jede gewählte Wurzel und jede geprüfte samt Mesh-Anzahl — ein "
                + "automatisch gewählter Durchlauf ist also nie ein Rätsel, und der richtige Name für "
                + "Roots lässt sich direkt aus dem Log ablesen. Aus = nur die konfigurierten Namen "
                + "verwenden und melden, dass keiner davon existiert.",
            ["Batching/MinRenderers"] =
                "Ein Wurzelobjekt mit weniger geeigneten Renderern als hier wird übersprungen. Eine Handvoll "
                + "Objekte zusammenzufassen kostet Speicher und bringt nichts Messbares, und jedes "
                + "zusammengefasste Objekt ist ein Eintrag mehr, der beim Rückgängigmachen zurückgegeben "
                + "werden muss.",
            ["Batching/MaxVertices"] =
                "Obergrenze der Vertices, die ein Durchlauf in zusammengefasste Meshes kopieren darf. DAS "
                + "IST DIE SPEICHERBREMSE und der echte Preis der Bündelung: das zusammengefasste Mesh ist "
                + "eine KOPIE jedes enthaltenen Vertex — im Arbeits- und im Grafikspeicher — zusätzlich zu "
                + "den Originalen, die erhalten bleiben, damit sich alles rückgängig machen lässt. Bei "
                + "typischen ~44 Byte pro Vertex sind 4 Millionen Vertices rund 170 MB je Kopie. Kandidaten "
                + "werden in Scan-Reihenfolge aufgenommen, bis das Budget aufgebraucht ist; die [Batch] "
                + "APPLY-Zeile nennt, wie viele ausgelassen wurden — ein abgeschnittener Durchlauf kann nie "
                + "wie ein vollständiger aussehen.",
            ["Batching/SettleSeconds"] =
                "Sekunden Wartezeit nach einem Szenenwechsel vor dem Zusammenfassen. Die Dungeon-Geometrie "
                + "wird über mehrere Bilder hinweg prozedural erzeugt, und ein noch nicht existierendes "
                + "Objekt kann nicht zusammengefasst werden — der Durchlauf wartet also, statt dem Generator "
                + "davonzulaufen. Gilt auch nach einem manuellen \"Jetzt anwenden\".",
            ["Batching/RescanSeconds"] =
                "Sekunden zwischen den Prüfungen, ob neue Geometrie aufgetaucht ist (0 = nie prüfen). Räume "
                + "werden beim Erkunden aufgedeckt, ein einzelner Durchlauf zu Beginn kann eine wachsende "
                + "Karte also nicht abdecken. Die Prüfung ist günstig — sie durchläuft nur die "
                + "konfigurierten WURZELN, nicht die ganze Szene — und fasst erst wieder zusammen, wenn "
                + "mindestens RescanGrowth neue geeignete Renderer dazugekommen sind. Was dazwischen "
                + "entsteht, wird schlicht unzusammengefasst gezeichnet: korrekt, nur nicht schneller.",
            ["Batching/RescanGrowth"] =
                "Wie viele NEUE geeignete Renderer eine Prüfung finden muss, bevor erneut zusammengefasst "
                + "wird. Niedrige Werte jagen jedem einzelnen erschienenen Objekt hinterher und zahlen dafür "
                + "je einen Durchlauf; hohe Werte lassen einen frisch geöffneten Raum bis zur nächsten "
                + "größeren Änderung unzusammengefasst.",
            ["Batching/IncludeInactive"] =
                "Auch Renderer auf INAKTIVEN Objekten zusammenfassen — die noch nicht geöffneten Räume. AN "
                + "erfasst ein Durchlauf zu Szenariobeginn die ganze Karte, und das Aufdecken eines Raums "
                + "nimmt ihn nicht wieder aus der Bündelung heraus. Das ist eine echte Fähigkeit, kein "
                + "bloßes Flag: Unitys eigenes Combine(root) überspringt inaktive Objekte, weil es sie mit "
                + "FindObjectsOfType einsammelt — die hier benutzte Array-Variante tut das nicht, und Unitys "
                + "Eignungsregel lautet renderer.enabled, nicht activeInHierarchy. AUS beschränkt den "
                + "Durchlauf auf das gerade Sichtbare.",
            ["Batching/Watchdog"] =
                "Überwacht eine rotierende Stichprobe zusammengefasster Objekte auf BEWEGUNG. Das ist der "
                + "eine Weg, auf dem Bündelung sichtbar schiefgeht: die Vertices eines zusammengefassten "
                + "Objekts sind in den Raum der Bündelungswurzel eingebacken, sein eigener Transform "
                + "bestimmt also nicht mehr, wo es gezeichnet wird — bewegt das Spiel es doch, erscheint es "
                + "dort, wo es zusammengefasst wurde. Der Wächter vergleicht die Pose RELATIV ZUR WURZEL "
                + "(das Verschieben der ganzen Wurzel und der Welt-Griff zählen also korrekt nicht als "
                + "Bewegung), nennt den Verursacher im Log und gibt mit WatchdogAutoRevert alles sofort "
                + "zurück. Kosten: ein paar Dutzend Matrixvergleiche pro Sekunde.",
            ["Batching/WatchdogAutoRevert"] =
                "Wenn der Wächter ein zusammengefasstes Objekt in Bewegung sieht, den ganzen Durchlauf "
                + "sofort rückgängig machen statt es nur zu protokollieren. AN ist die sichere Vorgabe — die "
                + "Alternative ist eine korrekte Logzeile neben einem falschen Bild. AUS behält die "
                + "Bündelung, damit das verrutschte Objekt fotografiert und identifiziert werden kann: eine "
                + "Diagnose-Einstellung, keine Vorliebe.",
            ["Batching/WatchdogAutoExclude"] =
                "Erwischt der Wächter ein zusammengefasstes Objekt in Bewegung, dessen NAMEN zu "
                + "ExcludeNames hinzufügen und den Durchlauf erneut versuchen, statt ihn nur "
                + "rückgängig zu machen. DAS MACHT DIE FUNKTION SELBSTKORRIGIEREND, und der "
                + "Hardware-Lauf von 2026-07 ist der Grund dafür: der erste Durchlauf fasste 1666 "
                + "Renderer zusammen und senkte die abgesetzten Materialslots von 1225 auf ~130 — "
                + "eine Sekunde später bewegte sich ein Objekt namens \"Glow\" und alles wurde "
                + "korrekterweise zurückgegeben, womit eine funktionierende Optimierung an einer "
                + "Handvoll Fackelflammen scheiterte. Der Ausschluss über den NAMEN statt über das "
                + "Objekt ist Absicht: solche Dinge treten in Familien auf, ein Name deckt alle ab. "
                + "Der Name wird in die cfg geschrieben, das nächste Szenario startet also bereits "
                + "mit diesem Wissen, und das Log nennt jede Ergänzung — ein zu weit gefasster "
                + "Ausschluss lässt sich einfach löschen. Begrenzt durch denselben Aufgabe-Zähler wie "
                + "zuvor, eine Szene voller Beweger endet also in einem klaren Urteil statt in einer "
                + "Endlosschleife. AUS = zurücknehmen und aufhören, wie bisher.",
            ["Batching/ExcludeLayers"] =
                "Ebenen, die nie zusammengefasst werden, kommagetrennt als Name oder Index (z. B. "
                + "\"Hero, Monster\"). Leer = nichts zusätzlich ausschließen. Die EIGENE Ebene des Mods ist "
                + "immer ausgeschlossen und lässt sich hier nicht wieder hinzufügen — Hände, Karten oder "
                + "Kontrollbrett zusammenzufassen würde sie an Ort und Stelle einfrieren, und der Mod darf "
                + "sich nicht durch einen Konfigurationstippfehler selbst zerstören. Die [Perf] SCENE-Zeile "
                + "listet jede Ebene mit Namen und Rendereranzahl. Unbekannte Namen werden gemeldet und "
                + "ignoriert.",
            ["Batching/ExcludeNames"] =
                "Namensbestandteile von Objekten, die nie zusammengefasst werden, kommagetrennt, "
                + "Groß-/Kleinschreibung egal (z. B. \"Door, Chest\"). Leer = nichts nach Namen "
                + "ausschließen. Das ist die Notausstiegsluke für genau den Fall, den der Wächter finden "
                + "soll: nennt das Log ein Objekt, das sich bewegt hat, kommt ein Stück seines Namens "
                + "hierher — und der Rest der Karte wird weiterhin gebündelt.",
            ["Batching/ExcludeComponents"] =
                "ZUSÄTZLICHE Komponenten-Typnamen, die ein Objekt und alles darunter disqualifizieren, "
                + "kommagetrennt. Animator, Animation und Rigidbody sind bedingungslos ausgeschlossen und "
                + "müssen hier nicht stehen — sie sind die drei Komponenten, die \"dieser Transform wird "
                + "gesteuert\" bedeuten, und genau das überlebt Bündelung nicht. Dieser Eintrag ist für "
                + "später gefundene spielspezifische Beweger; verglichen wird der kurze Typname.",
            ["Batching/FreeCombinedCpuCopy"] =
                "Nach dem Hochladen eines zusammengefassten Meshes auf die Grafikkarte dessen Kopie im "
                + "Arbeitsspeicher freigeben (Mesh.UploadMeshData(true)). Das halbiert den Speicherbedarf "
                + "des Durchlaufs und kann das Bild nicht verändern — gezeichnet wird der GPU-Puffer, und "
                + "auch Unitys eigene Bündelung zur Bauzeit erzeugt nicht lesbare Meshes. Es beeinträchtigt "
                + "auch das Rückgängigmachen nicht: dieses stellt das ORIGINAL-Mesh wieder her und zerstört "
                + "das zusammengefasste, ohne es je zu lesen. AUS behält die Kopie, was nur zum Untersuchen "
                + "nützlich ist.",
            ["Batching/VerboseLog"] =
                "Jeden ABGELEHNTEN Renderer samt Grund benennen — eine Logzeile pro Objekt, ein Szenario mit "
                + "1700 Renderern schreibt also 1700 Zeilen. Ausgeschaltet meldet die [Batch] PROBE-Zeile "
                + "jeden Ablehnungsgrund weiterhin als ANZAHL, was für eine Entscheidung reicht; dies ist "
                + "für den Fall, dass ein bestimmtes Objekt namentlich gefunden werden muss.",

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
                + "gerendert — nur der Himmel wird zur flächigen Key-Farbe. Beim Ausschalten wird alles "
                + "vollständig wiederhergestellt.",
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
            ["Comfort/TableHeightOffset"] =
                "Zusätzliche Augenhöhe über dem Tisch beim Neuzentrieren, in echten Metern (positiv = Tisch "
                + "sitzt tiefer). Eine Änderung führt das Neuzentrieren sofort erneut aus.",
            ["Comfort/RecenterHoldSeconds"] =
                "Die obere Taste (B + Y) an BEIDEN Controllern so viele Sekunden halten, um am Tisch neu zu "
                + "zentrieren. 0 schaltet die Tastenkombination ab.",
            ["Comfort/SavedScaleMultiplier"] =
                "Letzter Pinch-Skalierungsfaktor relativ zur Grund-WorldScale (die \"Tischgröße\" in der "
                + "VR-Einstellungstafel). Wird nach jeder Zwei-Griff-Skaliergeste automatisch geschrieben und "
                + "beim Neuaufbau des Rigs wieder angewandt. Standard 2.5 — die automatische Grundskalierung "
                + "wirkt wie ein riesiges Diorama; 2.5x schrumpft sie beim ersten Erscheinen auf eine angenehme "
                + "Tischgröße (Nutzerwunsch: Standard-Tischgröße ~2.5).",
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
                + "gfx-enable-gfx-jobs und gfx-enable-native-gfx-jobs in Gloomhaven_Data/boot.config "
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
            ["Core/InitDelayFrames"] =
                "Notausstieg: verzögert die Mod-Initialisierung (samt OpenXR-Init) um so viele gerenderte "
                + "Frames. Manche Runtime/GPU-Kombinationen brauchen ein vollständig hochgefahrenes "
                + "Grafikgerät, bevor xrCreateSession funktioniert. 0 (Standard) = sofortige Initialisierung im "
                + "Awake des Plugins.",
            // ---- [Rig] ----
            ["Rig/WorldScale"] =
                "Dioramen-Maßstab: Spielwelt-Einheiten pro realem Meter (das Rig wird damit skaliert, sodass "
                + "das Spielbrett wie ein Tisch wirkt). 0 = automatisch aus der Hexfeld-Größe (typisch ~10-20).",
            ["Rig/MenuRig"] =
                "Koppelt die Menükamera des Spiels an die Kopfbewegung, solange kein Szenario läuft "
                + "(Hauptmenü, Guildmaster-Karte), damit die schwebende 2D-Leinwand und die Hände auch "
                + "außerhalb von Szenarien funktionieren. Aus = das Menü wird aus einem festen Blickpunkt "
                + "gerendert.",
            ["Rig/SpawnInCircle"] =
                "Mehrspieler: setzt jeden VR-Spieler auf einen eigenen Azimut, gleichmäßig um das Spielbrett "
                + "verteilt (Abstand 360 / Spieleranzahl), jeweils mit Blick zur Brettmitte, damit Avatare "
                + "nicht mehr ineinander gestapelt erscheinen. Ohne Wirkung im Einzelspieler/Offline (der "
                + "Solo-Platz bleibt unverändert). Aus = alle Spieler behalten wie bisher denselben gemeinsamen "
                + "Platz.",
            ["Rig/Experimental3DMap"] =
                "RESERVIERT — DERZEIT NICHT UMGESETZTER Platzhalter für eine künftige Funktion: die "
                + "Kampagnen-/Weltkarte als kopfgetracktes 3D-Diorama erkunden statt auf der flachen "
                + "2D-Leinwand. Heute hat dieser Schalter KEINE Wirkung: alles vor einem echten Kampfszenario "
                + "(Kampagnenkarte, Guildmaster, Händler, Stufenaufstieg) bleibt bewusst in Menu2D auf dem "
                + "schwebenden Bildschirm, weil die Kartenszene nie für eine freie VR-Kamera gebaut wurde (Test "
                + "#8: riesige Karte unter dem Spieler, schwarzes flaches Fenster). Der Wunsch ist hier "
                + "festgehalten, damit er in eine spätere Phase überlebt.",
            ["Rig/WorldTiltDegrees"] =
                "Weltneigung im Demeo-Stil in Grad (0-60, 0 = aus/Standard). Der GESAMTE Spielbereich (Brett, "
                + "Figuren, alles) erscheint zu dir hin geneigt — ideal beim Spielen in zurückgelehnter oder "
                + "liegender Haltung. Rig-seitig umgesetzt: der VR-Trackingraum wird um die Brettmitte "
                + "gegengedreht, sodass dein Blickpunkt hinauf und über das Brett kreist, während die "
                + "Weltkoordinaten unangetastet bleiben (mehrspielersicher: Bretter und Figuren bewegen sich "
                + "für niemanden; andere Spieler sehen lediglich deinen Avatar kreisen, was das physikalisch "
                + "ehrliche Bild ist). KOMFORT-WARNUNG: das Neigen richtet die Schwerkraft relativ zu deinem "
                + "Kopf neu aus — der Horizont passt nicht mehr zu deinem Gleichgewichtssinn. Erhöhe in kleinen "
                + "Schritten (die Einstellungstafel schaltet in 5-Grad-Schritten) und bleibe bei maßvollen "
                + "Winkeln. Live: Änderungen wirken sofort und bleiben erhalten.",
            ["Rig/MaskedReaimHeadRate"] =
                "Nur bei Weltneigung. Wenn du dich körperlich mit Körper/Kopf drehst, wird die Richtung, in "
                + "die die Neigung kippt, unbemerkt auf deinen Blick nachgeführt — aber NUR, solange sich dein "
                + "Kopf schneller als diese Schwelle dreht (Grad pro Sekunde), sodass die Korrektur von deiner "
                + "eigenen Bewegung wahrnehmungsmäßig verdeckt wird (Redirected-Rotation-Technik). Unterhalb "
                + "der Schwelle bleibt die Welt exakt eingefroren. Standard 30.",
            ["Rig/MaskedReaimGain"] =
                "Nur bei Weltneigung. Geschwindigkeit der verdeckten Neigungs-Nachführung als Bruchteil der "
                + "Drehgeschwindigkeit deines Kopfes (0-0.5). 0.15 = die Achse führt mit 15% der jeweiligen "
                + "Kopfdrehgeschwindigkeit nach — weit unter der Wahrnehmungsschwelle für Rotationsverstärkung "
                + "von ~20%, sodass sich die Welt nie sichtbar bewegt. Höher konvergiert schneller, fällt aber "
                + "eher auf.",
            ["Rig/MaskedReaimDeadband"] =
                "Nur bei Weltneigung. Richtungsabweichungen zwischen Blick und Neigung, die kleiner sind als "
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
            ["Hands/GripPitchOffsetDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch [Hands] Glove/Plate/ArcaneGripPitchDegrees in "
                + "dev.gloomhavenvr.hands.cfg. Änderungen hier bewirken nichts; der Wert wird genau einmal "
                + "gelesen, als Startwert für jene Schlüssel pro Stil bei deren erster Erzeugung, danach nie "
                + "wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien weiter laden. Historische "
                + "Bedeutung: Neigungsversatz (Grad) zwischen der getrackten OpenXR-Grip-Pose und dem "
                + "sichtbaren Handmodell, um die X-Achse des Controllers. NEGATIV kippt die Fingerspitzen NACH "
                + "UNTEN gegenüber der Vorwärtsrichtung der Grip-Pose. Die OpenXR-Grip-Pose zeigt am "
                + "Controller-Griff entlang nach oben, nicht dorthin, wohin eine entspannte Hand zeigt. "
                + "Zusammen mit HandLateralOffset / HandVerticalOffset / HandForwardOffset setzt dies die "
                + "sichtbare Hand AUF den physischen Controller; Position 0/0/0 mit Neigung 0 legt die Hand "
                + "GENAU auf die getrackte Grip-Pose. Standard -30 hält die Handfläche um einen GEHALTENEN "
                + "Controller gelegt (eine stärkere Abwärtsneigung wirkt wie eine entspannte Hand und hebt die "
                + "Handfläche vom Gerät ab — Hardware-Test #27). Hot-Reload-fähig: im laufenden Spiel "
                + "bearbeiten, die Hände posieren sich im nächsten Frame neu. Justierhilfe: docs/TESTING-P2.md.",
            ["Hands/HandLateralOffset"] =
                "VERALTET — ohne Wirkung, ersetzt durch [Hands] Glove/Plate/ArcaneLateralOffset in "
                + "dev.gloomhavenvr.hands.cfg. Änderungen hier bewirken nichts; der Wert wird genau einmal "
                + "gelesen, als Startwert für jene Schlüssel pro Stil bei deren erster Erzeugung, danach nie "
                + "wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien weiter laden. Historische "
                + "Bedeutung: seitlicher Versatz (Meter) des sichtbaren Handmodells gegenüber der getrackten "
                + "OpenXR-Grip-Pose, entlang der lokalen X-Achse des Controllers. POSITIV verschiebt die Hand "
                + "zur Daumenseite (Geräteraum; das Vorzeichen wird pro Hand durch die Rig-Geometrie "
                + "gespiegelt). Eine der vier [Hands]-Sitz-Einstellungen (HandLateralOffset / "
                + "HandVerticalOffset / HandForwardOffset / GripPitchOffsetDegrees) — zusammen setzen sie die "
                + "sichtbare Hand AUF den physischen Controller, und Position 0/0/0 mit Neigung 0 legt die Hand "
                + "GENAU auf die getrackte Grip-Pose. Standard 0 hält die Hand mittig auf dem Controller-Griff. "
                + "Hot-Reload-fähig: im laufenden Spiel bearbeiten, die Hände setzen sich im nächsten Frame "
                + "neu.",
            ["Hands/HandVerticalOffset"] =
                "VERALTET — ohne Wirkung, ersetzt durch [Hands] Glove/Plate/ArcaneVerticalOffset in "
                + "dev.gloomhavenvr.hands.cfg. Änderungen hier bewirken nichts; der Wert wird genau einmal "
                + "gelesen, als Startwert für jene Schlüssel pro Stil bei deren erster Erzeugung, danach nie "
                + "wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien weiter laden. Historische "
                + "Bedeutung: senkrechter Versatz (Meter) des sichtbaren Handmodells gegenüber der getrackten "
                + "OpenXR-Grip-Pose, entlang der lokalen Hoch-Achse (Y) des Controllers. POSITIV hebt die Hand "
                + "an. Eine der vier [Hands]-Sitz-Einstellungen (HandLateralOffset / HandVerticalOffset / "
                + "HandForwardOffset / GripPitchOffsetDegrees) — zusammen setzen sie die sichtbare Hand AUF den "
                + "physischen Controller, und Position 0/0/0 mit Neigung 0 legt die Hand GENAU auf die "
                + "getrackte Grip-Pose. Standard 0 setzt die Handfläche auf die Grip-Pose: die maßvolle Neigung "
                + "von -30 lässt die Handfläche nicht mehr so absacken wie die alten -60, deshalb ist zu Beginn "
                + "kein Anheben nötig — erhöhe den Wert, wenn die Handfläche auf deinem Controller noch zu tief "
                + "wirkt (Hardware-Tests #24/#27). Hot-Reload-fähig: im laufenden Spiel bearbeiten, die Hände "
                + "setzen sich im nächsten Frame neu.",
            ["Hands/HandForwardOffset"] =
                "VERALTET — ohne Wirkung, ersetzt durch [Hands] Glove/Plate/ArcaneForwardOffset in "
                + "dev.gloomhavenvr.hands.cfg. Änderungen hier bewirken nichts; der Wert wird genau einmal "
                + "gelesen, als Startwert für jene Schlüssel pro Stil bei deren erster Erzeugung, danach nie "
                + "wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien weiter laden. Historische "
                + "Bedeutung: Vorwärts-/Tiefenversatz (Meter) des sichtbaren Handmodells gegenüber der "
                + "getrackten OpenXR-Grip-Pose, entlang der lokalen Vorwärts-Achse (Z) des Controllers. POSITIV "
                + "schiebt die Hand zu den Fingerspitzen hin; NEGATIV setzt das Handgelenk hinter den "
                + "Grip-Ursprung. Eine der vier [Hands]-Sitz-Einstellungen (HandLateralOffset / "
                + "HandVerticalOffset / HandForwardOffset / GripPitchOffsetDegrees) — zusammen setzen sie die "
                + "sichtbare Hand AUF den physischen Controller, und Position 0/0/0 mit Neigung 0 legt die Hand "
                + "GENAU auf die getrackte Grip-Pose. Standard -0.06 setzt das Handgelenk knapp hinter den "
                + "Grip-Ursprung, sodass die Handfläche den Controller-Griff umschließt. Hot-Reload-fähig: im "
                + "laufenden Spiel bearbeiten, die Hände setzen sich im nächsten Frame neu.",
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
            // ---- [Hands] ----
            ["Hands/RayAlwaysOn"] =
                "Hält den Laser-/Strahl-Interactor in jedem VR-Modus aktiv statt nur in Kontexten mit "
                + "Fern-Interaktion.",
            ["Hands/ModalRayConeDegrees"] =
                "Solange ein modaler Dialog offen ist (Modus ModalUI), bleibt der Strahl nutzbar, sein Laser "
                + "wird aber nur angezeigt, wenn er innerhalb dieser Gradzahl auf eine UI-Fläche zeigt "
                + "(Weltdialog, flacher Bildschirm). 0 = den Laser in ModalUI immer anzeigen.",
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
            ["Hands/*PitchTrimDegrees"] =
                "VERALTET — ohne Wirkung, ersetzt durch den Schlüssel GripPitchDegrees dieses Handstils unter "
                + "[Hands] in dev.gloomhavenvr.hands.cfg, der ein ABSOLUTER Wert pro Stil ist und kein "
                + "Trimmwert. Dieser Eintrag wird genau einmal gelesen, als Teil des Startwerts für jenen "
                + "Schlüssel bei dessen erster Erzeugung, danach nie wieder. Bleibt gebunden, damit bestehende "
                + "Konfigurationsdateien weiter laden. Historische Bedeutung: zusätzliche Neigung (Grad), die "
                + "zu GripPitchOffsetDegrees ADDIERT wird, solange dieser Handstil getragen wird.",
            ["Hands/*LateralTrim"] =
                "VERALTET — ohne Wirkung, ersetzt durch den Schlüssel LateralOffset dieses Handstils unter "
                + "[Hands] in dev.gloomhavenvr.hands.cfg, der ein ABSOLUTER Wert pro Stil ist und kein "
                + "Trimmwert. Dieser Eintrag wird genau einmal gelesen, als Teil des Startwerts für jenen "
                + "Schlüssel bei dessen erster Erzeugung, danach nie wieder. Bleibt gebunden, damit bestehende "
                + "Konfigurationsdateien weiter laden. Historische Bedeutung: zusätzlicher seitlicher Versatz "
                + "(X) in Metern, der zu HandLateralOffset ADDIERT wird, solange dieser Handstil getragen wird.",
            ["Hands/*VerticalTrim"] =
                "VERALTET — ohne Wirkung, ersetzt durch den Schlüssel VerticalOffset dieses Handstils unter "
                + "[Hands] in dev.gloomhavenvr.hands.cfg, der ein ABSOLUTER Wert pro Stil ist und kein "
                + "Trimmwert. Dieser Eintrag wird genau einmal gelesen, als Teil des Startwerts für jenen "
                + "Schlüssel bei dessen erster Erzeugung, danach nie wieder. Bleibt gebunden, damit bestehende "
                + "Konfigurationsdateien weiter laden. Historische Bedeutung: zusätzlicher senkrechter Versatz "
                + "(Y) in Metern, der zu HandVerticalOffset ADDIERT wird, solange dieser Handstil getragen "
                + "wird.",
            ["Hands/*ForwardTrim"] =
                "VERALTET — ohne Wirkung, ersetzt durch den Schlüssel ForwardOffset dieses Handstils unter "
                + "[Hands] in dev.gloomhavenvr.hands.cfg, der ein ABSOLUTER Wert pro Stil ist und kein "
                + "Trimmwert. Dieser Eintrag wird genau einmal gelesen, als Teil des Startwerts für jenen "
                + "Schlüssel bei dessen erster Erzeugung, danach nie wieder. Bleibt gebunden, damit bestehende "
                + "Konfigurationsdateien weiter laden. Historische Bedeutung: zusätzlicher Vorwärts-Versatz (Z) "
                + "in Metern, der zu HandForwardOffset ADDIERT wird, solange dieser Handstil getragen wird.",
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
                + "(geneigte Griffpose, siehe [Hands] GripPitchOffsetDegrees) zeigen die Finger vorwärts und "
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
                + "GrabAnchor-lokalen Achsen: +Y aus der Handfläche heraus, +Z entlang der Finger, +X seitlich "
                + "(zwischen den Händen anatomisch gespiegelt). Beispiel {x:0, y:0.01, z:0.02} hebt die Karte 1 "
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
            ["Cards/SlotCardFill"] =
                "Punkt 3: wie weit eine in einen Brett-Slot gelegte Karte HOCHSKALIERT wird, um die physische "
                + "Vertiefung zu füllen. Multipliziert ihre Slot-Größe (zusätzlich zum 1.3x SlotScale des "
                + "Rahmens). 1.0 = Größe vor der Korrektur, sichtbar kleiner als die Vertiefung. PRO BRETT: "
                + "Richtung Vertiefungs-/Kartenverhältnis des AKTIVEN Bretts erhöhen, bis die Karte sie fast "
                + "füllt, ohne den Rand zu überragen; der Standard passt zum mitgelieferten PlayTray. Gilt für "
                + "gespielte Karten, Einzelkartenwahl-Kandidaten und angedockte Aktionskarten. Ändert NICHT die "
                + "Vertiefung oder die Sitztiefe (das ist SlotCardInset).",
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
            ["Cards/PileViewer"] =
                "Ablage-/Verbrennstapel am rechten Rand des Kontrollbretts (Wunsch aus Hardware-Test #21): "
                + "jeder Stapel erscheint als kleiner physischer Kartenstapel mit Zähler; Antippen oder Greifen "
                + "per Pinch-Griff hebt einen lesbaren Blätterfächer der Karten dieses Stapels hoch (rein "
                + "informativ — loslassen/erneut antippen schließt ihn). false = überhaupt keine Stapel auf dem "
                + "Brett.",
            ["Cards/ActivePile"] =
                "Bereich AKTIVE KARTEN (Funktion 6): die aktuell aktiven Fähigkeitskarten des Charakters "
                + "(rundenlang oder dauerhaft), DAUERHAFT als schmale Spalte direkt RECHTS neben den "
                + "Ablage-/Verbrennstapeln gezeigt. Die Karten sind etwas kleiner als im Handfächer und bleiben "
                + "einzeln greifbar, sodass du eine herausziehen und lesen kannst (beim Loslassen kehrt sie in "
                + "die Spalte zurück); die aktive HÄLFTE jeder Karte ist hervorgehoben. Rein informativ — eine "
                + "aktive Karte zu greifen wählt oder bestätigt sie nie. Leer, wenn keine Karte aktiv ist. "
                + "false = überhaupt kein Bereich für aktive Karten.",
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
            ["Cards/ConfirmUndoSize_*"] =
                "Kantenlänge (Meter) der eckigen Fortfahren/Rückgängig-Knöpfe. Von Oak übernommen (0.073).",
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
            ["Cards/InitiativeOffset_*"] =
                "Lokale Position der Aufhängung der Initiativleiste (ersetzt die feste Mount-Position), "
                + "board-lokale Meter. Von Oak übernommen (0, 0.10, −0.004).",
            ["Cards/BoardTilt_*"] =
                "Neigung des Boards aus der Waagerechten zum Spieler hin, Grad (0 = flach wie ein Tisch, 90 = "
                + "aufrecht). Ersetzt TrayTilt in der Posenberechnung für dieses Board. Von Oak übernommen "
                + "(30).",
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
            ["Cards/VRSettingsOffset_*"] =
                "Versatz, der zur lokalen Position des ZAHNRAD-Knopfes für die VR-Einstellungen ADDIERT wird "
                + "(zusätzlich zu seiner festen Basis in der rechten Spalte), board-lokale Meter (Z = "
                + "Herausstehen zum Spieler hin). Startwert 0 (Oak).",
            ["Cards/PinOffset_*"] =
                "Versatz, der zur lokalen Position des FOLGEN/FIXIERT-Schalters ADDIERT wird (zusätzlich zu "
                + "seiner festen Basis unten rechts), board-lokale Meter (Z = Herausstehen zum Spieler hin). "
                + "Startwert 0 (Oak).",
            ["Cards/ReadoutOffset_*"] =
                "Versatz, der zur lokalen Position der RUNDEN-Anzeige ('Runde N') ADDIERT wird (zusätzlich zu "
                + "ihrer festen Basis oben rechts), board-lokale Meter (Z = Herausstehen zum Spieler hin). "
                + "Startwert 0 (Oak).",
            ["Cards/ClusterOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung der KNOPFGRUPPE für den Zugablauf "
                + "(Rückgängig|Bereit|Überspringen) ADDIERT wird (zusätzlich zu ihrer festen Basis unter den "
                + "Slots), board-lokale Meter. Startwert 0 (Oak).",
            ["Cards/ClusterScale_*"] =
                "Größen-MULTIPLIKATOR der KNOPFGRUPPE für den Zugablauf (zusätzlich zu ihrer festen "
                + "Dock-Skalierung von 0.7x). Startwert 1 (Oak).",
            ["Cards/DecisionOffset_*"] =
                "Versatz, der zur lokalen Position der Aufhängung des gemeinsamen ENTSCHEIDUNGS-DOCKS ADDIERT "
                + "wird (die Zeile mit Entscheidungs-/Bestätigungsabfragen, die unter dem Board hängt), "
                + "board-lokale Meter. Startwert 0 (Oak).",
            ["Cards/DecisionScale_*"] =
                "Größen-MULTIPLIKATOR des gemeinsamen ENTSCHEIDUNGS-DOCKS (seine angedockte Abfragezeile "
                + "folgt in der Pose der lossyScale des Mounts). Startwert 1 (Oak).",
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
            ["Cards/GrabButton"] =
                "Demeo-Parität (G3): welcher Controller-Knopf eine Karte per Nähe greift. Trigger = Demeo "
                + "(Zeigefinger-Pinch, passt zu unserem Laser-Zupfen, sodass eine auf beide Arten gegriffene "
                + "Karte beim Loslassen des Triggers freigegeben wird). Grip = das Verhalten von vor Demeo.",
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
            ["WorldUI/Master"] =
                "Hauptschalter für die gesamte physische Oberfläche (alle Flächen darunter UND die schwebende "
                + "2D-Leinwand). Aus = die spieleigene 2D-Bildschirmoberfläche bleibt unangetastet.",
            ["WorldUI/ButtonCluster"] = "Physische Knöpfe für Bereit/Rückgängig/Überspringen am Tischrand.",
            ["WorldUI/InitiativeTrack"] = "Initiativleiste als Welt-Tafel über dem Tisch.",
            ["WorldUI/ElementBoard"] =
                "Element-Infusionstafel, angedockt an der linken Spalte des Kontrollbretts, unter der "
                + "Aufgaben-Tafel (schwebende Welt-Tafel nur als Rückfall ohne Board).",
            ["WorldUI/CombatLog"] = "Kampflog als Welt-Tafel an der gegenüberliegenden Tischseite.",
            ["WorldUI/Objectives"] = "Szenario-Aufgaben als Welt-Tafel an der gegenüberliegenden Tischseite.",
            ["WorldUI/Dialogs"] = "Bestätigungsdialoge als Welt-Modale vor dem HMD (Ja/Nein antippen).",
            ["WorldUI/StatPanels"] =
                "Werte-Tafeln von Figuren/Monstern als Welt-Tafeln nahe dem Tisch (geöffnet vom Spiel / per "
                + "Phase-3a-Antippen).",
            ["WorldUI/PropInfoCards"] =
                "Mouseover-Objektinfokarten (geschlossene Türen/Truhen, Fallen, Gelände, Questgegenstände — "
                + "die TextInfoPanel/PropInfoPanel-Popups des Spiels) als kleine passive Welt-Tafel unten im "
                + "Blickfeld.",
            ["WorldUI/EnemyReveal"] =
                "Gegner-Rundenenthüllung (die Monster-Fähigkeitskarten, die gezeigt werden, nachdem alle ihre "
                + "Kartenwahl bestätigt haben) als reine Anzeige-Tafel, die über dem Spielbrett schwebt, "
                + "solange das Spiel sie zeigt — statt versteckt auf dem Kontrollbrett.",
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
            ["WorldUI/ActorBars"] =
                "Echte LP-/Effekt-Balken im 3D-Raum über den Miniaturen (ersetzt die auf den Bildschirm "
                + "projizierten Balken).",
            ["WorldUI/BarFixedSize"] =
                "LP-/Effekt-Balken der Figuren behalten eine FESTE Größe im Brett-Maßstab — sie skalieren nur "
                + "mit dem Diorama, genau wie die Miniaturen selbst (Test #14: die alte Abstandskompensation "
                + "ließ die Balken beim Zurücktreten auf bis zu 2.5x anwachsen, was als \"Wachsen\" der Balken "
                + "wahrgenommen wurde). Aus = altes Verhalten: die Balken wachsen sanft mit dem Kopfabstand, um "
                + "lesbar zu bleiben.",
            ["WorldUI/WristHud"] =
                "Kompakter Charakterstatus (LP/EP/Zustände/Gold) am nicht-dominanten Handgelenk, per Hinsehen "
                + "aktiviert.",
            ["WorldUI/FlatScreen"] =
                "Schwebende 2D-Leinwand, die die UICamera für Menüs/Händler/Stufenaufstieg spiegelt, samt "
                + "Strahl-Zeiger.",
            ["WorldUI/Tooltips"] =
                "Verankert das Tooltip-Canvas des Spiels neu im 3D-Raum, nahe der antippenden Fingerspitze.",
            ["WorldUI/ActionElementHints"] =
                "Zeigt den Erklärungshinweis des Spiels zu Element/Fähigkeit in der Aktionsphase (den "
                + "Kartenaktions-Tooltip) als Welt-Tafel, die während eines Szenarios an der Ecke OBEN LINKS "
                + "des Kontrollbretts angeheftet ist. Eine kurze Nachlaufzeit beim Mouseover verhindert, dass "
                + "er bei winzigen Bewegungen vom Element weg wegflackert. Aus = der Hinweis wird nie in den "
                + "3D-Raum überführt und in VR nie gezeigt (der normale 2D-Menü-Tooltip bleibt unberührt). Mit "
                + "der VR-Einstellungstafel verdrahtet und live gelesen — das Umschalten wirkt ohne Neustart.",
            ["WorldUI/ForceMouseMode"] =
                "Hält den InputManager während des VR-Betriebs im Maus-Modus, damit die \"Game\"- (nicht "
                + "\"Game_gamepad\"-) Szenenvarianten laden und Knöpfe ohne Gamepad-Langdruck-Abläufe auslösen.",
            ["WorldUI/CanvasScaleMm"] =
                "Welt-Canvas-Skalierung: Millimeter pro uGUI-Pixel bei Diorama-Größe 1 (Standard 1 px = 1 "
                + "mm).",
            ["WorldUI/InitiativeDepthMaxSpreadPx"] =
                "3D-Tiefeneffekt der Initiativleiste: die MAXIMALE Gesamt-z-Spanne von vorn nach hinten "
                + "(uGUI-Pixel) zwischen dem flachsten und dem tiefsten Initiativporträt. Die vorgegebene "
                + "Zeilentiefe wird proportional gestaucht, bis sie diese Obergrenze einhält (nie verstärkt). "
                + "Höher = stärkere Staffelung; 0 = flach. Live änderbar im Debug-Menü (Tafeln -> Initiative). "
                + "Bereich 0..40.",
            ["WorldUI/DecisionRowGapPx"] =
                "Senkrechter Abstand (uGUI-Pixel, im Maßstab der angedockten Zeile) zwischen der Unterkante "
                + "des Kontrollbretts — dort zeichnet das Spiel die Entscheidungszeile, z. B. \"Schadensphase: "
                + "Erleide entweder Schaden …\" — und der OBERKANTE des angedockten interaktiven Widget-Blocks "
                + "(die Knöpfe \"Schaden erhalten\" / Verbrennen, die Verbrennen-Bestätigungsoptionen, das "
                + "Ja/Nein der kurzen Rast). Der Mod bestimmt, wo dieser Block andockt, deshalb steuert der "
                + "Wert direkt seine PLATZIERUNG: KLEINER zieht die Knöpfe nach OBEN zur Abfrage (schließt die "
                + "Lücke), GRÖSSER lässt sie absinken. Gilt für jede angedockte Entscheidungszeile und wird bei "
                + "einer Änderung sofort auf ein offenes Dock angewendet. Die Greifleiste wird nie überlappt "
                + "(der Block sitzt immer mindestens seinen Sicherheitsabstand darunter). Bereich 0-120.",
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
            ["WorldUI/FlatScreenAutoShow"] =
                "Blendet die schwebende 2D-Leinwand automatisch ein, solange kein Szenario läuft (Hauptmenü, "
                + "Karte), und in Szenario-Modi wieder aus.",
            ["WorldUI/DesktopMirrorLeftEye"] =
                "Der flache Monitor spiegelt NUR das LINKE Auge des HMD: setzt XRSettings.gameViewRenderMode "
                + "fest auf LeftEye und überspringt den Composite-Blit des 2D-Menüs auf dem Desktop, sodass der "
                + "Desktop in jedem Zustand ein sauberes Einzelaugen-Spiegelbild zeigt. Aus = altes Verhalten "
                + "(2D-Menü-Composite in Menüs, sonst unkontrollierter XR-Standardspiegel).",
            ["WorldUI/WristHudPitch"] =
                "Neigung des Handgelenk-Übersichts-HUD (Pitch, Grad) zusätzlich zur flach auf der Hand "
                + "liegenden Grundausrichtung.",
            ["WorldUI/WristHudYaw"] = "Drehung des Handgelenk-Übersichts-HUD (Gieren, Grad).",
            ["WorldUI/WristHudRoll"] = "Rollen des Handgelenk-Übersichts-HUD (Grad).",
            ["WorldUI/WristHudOffsetX"] =
                "Versatz des Handgelenk-Übersichts-HUD entlang der Handgelenk-X-Achse, echte Meter.",
            ["WorldUI/WristHudOffsetY"] =
                "Versatz des Handgelenk-Übersichts-HUD aus dem Handrücken heraus (Handgelenk +Y), echte "
                + "Meter.",
            ["WorldUI/WristHudOffsetZ"] =
                "Versatz des Handgelenk-Übersichts-HUD in Richtung der Finger (Handgelenk +Z), echte Meter.",
            ["WorldUI/ShowIntro"] =
                "Zeigt das Intro des Spiels (Logos/Video, Szenen vor dem Menü) auch in VR auf dem schwebenden "
                + "Bildschirm. Aus = altes Verhalten: das Intro läuft nur auf dem Desktop und im HMD steht eine "
                + "\"startet…\"-Anzeige in der Leere.",
            ["WorldUI/ScreenWidth"] =
                "Breite der schwebenden 2D-Leinwand in echten Metern (16:9, die Höhe folgt daraus). Ersetzt "
                + "den Schlüssel \"FlatScreenWidth\" von vor Test #6 (1.4 m wirkte auf 1.6 m Entfernung zu "
                + "klein).",
            ["WorldUI/ScreenDistance"] = "Abstand vom Kopf zur schwebenden 2D-Leinwand in echten Metern.",
            ["WorldUI/ClickLatch"] =
                "Friert die Position der virtuellen Maus vom Trigger-Druck (oder der Berührung mit der "
                + "Fingerspitze) bis zum Loslassen ein, damit Druck und Loslassen auf DEMSELBEN Pixel landen "
                + "und uGUI einen Klick registriert — sonst verschiebt schon ein Handzittern von unter einem "
                + "Grad den projizierten Punkt um Dutzende px und macht aus jedem Klick ein wirkungsloses "
                + "Ziehen. Bewusste Bewegung über DragUnlockDegrees hinaus für DragUnlockSeconds löst die "
                + "Sperre zu einem echten Ziehen (Scroll-Listen funktionieren weiter).",
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
            ["WorldUI/ClickMode"] =
                "Wie ein gesperrter Klick auf dem schwebenden Bildschirm zugestellt wird. \"execute\" "
                + "(Standard): direkt über uGUI ExecuteEvents auf dem Raycast-Ziel — derselbe Mechanismus, den "
                + "auch BaseButtons.clickButton des Spiels nutzt; unempfindlich gegen Eigenheiten der "
                + "Flankensichtbarkeit im Input-Modul (Hardware-Test #7: die Tastenflanken der virtuellen Maus "
                + "erzeugten keine Klicks). \"virtualmouse\": Druck/Loslassen nur über das virtuelle Mausgerät. "
                + "\"both\": beide Wege (kann doppelt auslösen — nur zur Diagnose). Bewusstes Ziehen läuft "
                + "unabhängig vom Modus immer über die virtuelle Maus.",
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
            ["WorldUI/ModalStyle"] =
                "Wie 2D-Rückfallfenster im Szenario (Story-Boxen, Ereignisse, Tutorials, ESC-Menü, "
                + "Belohnungen, Auswahldialoge …) in VR nutzbar werden (P8, Test #12). \"window\" (Standard): "
                + "nur GENAU DIESES Fenster wird zur Welt-Tafel vor dem HMD, per Antippen UND Laser bedienbar, "
                + "und kehrt beim Schließen an seinen 2D-Platz zurück; die volle Leinwand erscheint nur, wenn "
                + "ein Fenster sich nicht umwandeln lässt (Grund wird protokolliert). \"screen\": Verhalten vor "
                + "P8 — für jedes Rückfallfenster erscheint das komplette 2D-Composite. Die manuelle "
                + "A/X-Kombination holt davon unabhängig immer die volle Leinwand.",
            ["WorldUI/ManualScreenChord"] =
                "Selbstrettungs-Tastenkombination: HALTE im Szenario die untere Fronttaste der "
                + "NICHT-dominanten Hand (A oder X) für ManualScreenChordSeconds, um die schwebende 2D-Leinwand "
                + "(volle Desktop-Oberfläche + Zeiger) umzuschalten — immer verfügbar, wenn ein 2D-Fenster "
                + "offen ist, das VR nicht zeigt. Kurzes Halten schaltet weiterhin die Einstellungstafel um; "
                + "diese Kombination löst beim LOSLASSEN aus (vor der Bildschirm-Schwelle), damit sich die "
                + "beiden nie in die Quere kommen.",
            ["WorldUI/ManualScreenChordSeconds"] =
                "Haltedauer (Sekunden) von A/X der nicht-dominanten Hand für das manuelle Umschalten der "
                + "Leinwand. Muss länger sein als [SettingsPanel] ChordHoldSeconds.",
            ["WorldUI/DemoteOverlaySolidClears"] =
                "Solange die schwebende 2D-Leinwand die Kameras des Spiels in ihre RenderTexture aufnimmt, "
                + "werden VOLLBILD-SolidColor-Clears erfasster NICHT-Basis-Kameras (z. B. die \"Video Camera\" "
                + "der Kampagnenkarte auf Tiefe 5, deren Clear nur der schwarze Hintergrund hinter "
                + "Vollbildvideos ist — GH VideoCamera.PlayFullscreenVideo) auf Depth-only herabgestuft, damit "
                + "sie die zusammengesetzte Karte/UI nie schwarz überschreiben können. Kameras mit "
                + "eingeschränktem Viewport (Teilrechteck) behalten ihren Clear. Abschalten für vanilla-genaue "
                + "Clears (schwarzer Letterbox-Hintergrund während Videos).",
            ["WorldUI/ScreenLayerSplit"] =
                "Rendert die schwebende 2D-Leinwand als ZWEI Ebenen (Hardware-Test #18): die UI-Kameras des "
                + "Spiels — deren Screen-Space-Camera-Canvases immer nur über ihre zugewiesene Kamera rendern, "
                + "weshalb Stereo-Spiegelkameras sie nie reproduzieren können und das Menü einäugig wurde — "
                + "zeichnen auf ein transparentes \"Glas\"-Quad, das beiden Augen identisch in der "
                + "Leinwandebene gezeigt wird, während 3D-Szenenkameras und Videos einige cm dahinter eine "
                + "Hintergrundebene mit augenweiser Stereotiefe rendern. Aus (oder bei jedem Fehler): Rückfall "
                + "auf eine einzelne RT — eine flache Mono-Leinwand in beiden Augen, nie einäugig.",
            // ---- [SettingsPanel] ----
            ["SettingsPanel/ChordHoldSeconds"] =
                "So viele Sekunden die untere Fronttaste der NICHT-dominanten Hand (A oder X) halten, um die "
                + "VR-Einstellungstafel umzuschalten — funktioniert auch im Menü. 0 schaltet die "
                + "Tastenkombination ab. (Neu zentrieren bleibt auf B+Y, gehalten auf BEIDEN Händen.)",
            ["SettingsPanel/FollowSeat"] =
                "Ankermodus der Einstellungstafel (der FOLGEN/FIXIERT-Pin-Schalter der Tafel schaltet ihn "
                + "um). False (FIXIERT, Standard): die Tafel wird beim Öffnen einmal ins Blickfeld gesetzt und "
                + "dann in der Welt eingefroren, bis sie gegriffen wird. True (FOLGEN): sie leitet ihren Platz "
                + "in jedem Tick neu aus Tischanker + Sitzdrehung ab (bewegt sich mit dem Neuzentrieren und dem "
                + "Diorama). In beiden Fällen wird die Pose bei jedem Öffnen ins vordere Blickfeld begrenzt, "
                + "damit sie nie außerhalb des Blicks auftauchen kann.",
            ["SettingsPanel/Forward"] =
                "Versatz der Einstellungstafel vom Tischanker in Sitz-Vorwärtsrichtung, echte Meter (wird "
                + "beim Loslassen des Griffs gespeichert). Nur ein Startpunkt — die Tafel wird bei jedem Öffnen "
                + "erneut vor den Kopf begrenzt.",
            ["SettingsPanel/Right"] =
                "Versatz der Einstellungstafel nach rechts vom Sitz aus, echte Meter (wird beim Loslassen des "
                + "Griffs gespeichert).",
            ["SettingsPanel/Up"] =
                "Höhe der Einstellungstafel über der Tischebene, echte Meter (wird beim Loslassen des Griffs "
                + "gespeichert).",
            ["SettingsPanel/Scale"] =
                "Größenfaktor der Einstellungstafel (Skalieren mit zwei Händen; begrenzt auf 0.5-2).",
            // ---- [WorldUI] ----
            ["WorldUI/DevShowAllPanels"] =
                "DEV: erzeugt das Welt-Tafel-Layout mit Platzhalter-Inhalt auf dem Desktop (kein HMD nötig).",
            ["WorldUI/DevForceConvert"] =
                "DEV: wendet die echten Canvas-Umwandlungen im Dev-Modus ohne HMD an (das verschiebt die "
                + "2D-Tafeln des Spiels in den 3D-Raum — die Desktop-Ansicht ändert sich entsprechend).",
            // ---- [Hands] ----
            ["Hands/GlovePinkyCounterAbduction"] =
                "Nur Handstil GLOVE: Grad Gegen-Abspreizung (Drehung um das lokale Z der Kleinfinger-Wurzel = "
                + "Handflächen-Normale), angewandt bei voller Krümmung und mit dem Krümmungswert skaliert. Der "
                + "MESH-Schlauch des Glove-Kleinfingers neigt sich ~18° nach außen, während seine Knochenkette "
                + "gerade ist — eine reine Faust um lokal X lässt den gekrümmten Kleinfinger sichtbar "
                + "abgespreizt stehen; dies zieht ihn beim Krümmen zum Ringfinger zurück (das Vorzeichen kippt "
                + "für die rechte Hand automatisch). 0 schaltet ab. Live änderbar.",
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
            ["WristHud/*Pitch"] =
                "Neigung (Pitch, Grad) des Handgelenk-Übersichts-HUD zusätzlich zur flach auf der Hand "
                + "liegenden Grundlage. PRO STIL absoluter Wert, während dieser Handstil getragen wird (ersetzt "
                + "den gemeinsamen Eintrag [WorldUI] WristHud*, aus dem er beim ersten Start übernommen wurde). "
                + "Live änderbar — WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*Yaw"] =
                "Drehung (Gieren, Grad) des Handgelenk-Übersichts-HUD. PRO STIL absoluter Wert, während "
                + "dieser Handstil getragen wird (ersetzt den gemeinsamen Eintrag [WorldUI] WristHud*, aus dem "
                + "er beim ersten Start übernommen wurde). Live änderbar — WristHud wendet die Pose bei jedem "
                + "Tick neu an.",
            ["WristHud/*Roll"] =
                "Rollen (Grad) des Handgelenk-Übersichts-HUD. PRO STIL absoluter Wert, während dieser "
                + "Handstil getragen wird (ersetzt den gemeinsamen Eintrag [WorldUI] WristHud*, aus dem er beim "
                + "ersten Start übernommen wurde). Live änderbar — WristHud wendet die Pose bei jedem Tick neu "
                + "an.",
            ["WristHud/*OffsetX"] =
                "Versatz des Handgelenk-Übersichts-HUD entlang der Handgelenk-Achse X, echte Meter. PRO STIL "
                + "absoluter Wert, während dieser Handstil getragen wird (ersetzt den gemeinsamen Eintrag "
                + "[WorldUI] WristHud*, aus dem er beim ersten Start übernommen wurde). Live änderbar — "
                + "WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*OffsetY"] =
                "Versatz des Handgelenk-Übersichts-HUD aus dem Handrücken heraus (Handgelenk +Y), echte "
                + "Meter. PRO STIL absoluter Wert, während dieser Handstil getragen wird (ersetzt den "
                + "gemeinsamen Eintrag [WorldUI] WristHud*, aus dem er beim ersten Start übernommen wurde). "
                + "Live änderbar — WristHud wendet die Pose bei jedem Tick neu an.",
            ["WristHud/*OffsetZ"] =
                "Versatz des Handgelenk-Übersichts-HUD zu den Fingern hin (Handgelenk +Z), echte Meter. PRO "
                + "STIL absoluter Wert, während dieser Handstil getragen wird (ersetzt den gemeinsamen Eintrag "
                + "[WorldUI] WristHud*, aus dem er beim ersten Start übernommen wurde). Live änderbar — "
                + "WristHud wendet die Pose bei jedem Tick neu an.",
            // ---- [RoundButtons] ----
            ["RoundButtons/OffsetX"] =
                "Seitlicher Versatz (Meter im WURZEL-Frame des Boards, +X = zur rechten Board-Kante / zu den "
                + "Rückgängig-Zahnrad-Feldern) der Kurzzeit-Knopfgruppe der Rundenphase (Schritt überspringen "
                + "usw.) gegenüber ihrem Standard-Anker. Live änderbar; begrenzt auf -0.30..0.30.",
            ["RoundButtons/OffsetY"] =
                "Versatz das Board hinauf (Meter im WURZEL-Frame des Boards, +Y = zu den Kartenplätzen / zur "
                + "fernen Kante, -Y = zur unteren Kante und zum Haltegriff) der Kurzzeit-Knopfgruppe gegenüber "
                + "ihrem Standard-Anker. Live änderbar; begrenzt auf -0.30..0.30.",
            ["RoundButtons/OffsetZ"] =
                "Versatz aus der Ebene heraus (Meter im WURZEL-Frame des Boards, +Z = AUS dem Board heraus "
                + "zum Spieler, -Z = versenkt in bzw. hinter die Board-Fläche) der Kurzzeit-Knopfgruppe "
                + "gegenüber ihrem standardmäßig erhabenen Sitz. Live änderbar; begrenzt auf -0.30..0.30.",
            ["RoundButtons/Shape"] =
                "Kappenform der Kurzzeit-Knöpfe der Rundenphase: Round = abgeflachter Puck (Standard), Square "
                + "= kantige Tastenkappe (dann gelten Width/Height/Depth dieses Abschnitts). Live änderbar.",
            ["RoundButtons/CapSize"] =
                "Kappenradius (Meter im Cluster-Frame) der Kurzzeit-Knöpfe. Die automatische Spaltenanpassung "
                + "VERKLEINERT nur unter diesen Wert, wenn sich mehrere Knöpfe die Spalte teilen müssen; ein "
                + "einzelner Knopf nutzt genau diese Größe. Live änderbar; begrenzt auf 0.015..0.09.",
            ["RoundButtons/Width"] =
                "Kappenbreite (Meter) der Kurzzeit-Knöpfe bei Shape=Square. Gilt NUR für diese Gruppe. Live "
                + "änderbar; begrenzt auf 0.02..0.20.",
            ["RoundButtons/Height"] =
                "Kappenhöhe (Meter) der Kurzzeit-Knöpfe bei Shape=Square. Gilt NUR für diese Gruppe. Live "
                + "änderbar; begrenzt auf 0.015..0.20.",
            ["RoundButtons/Depth"] =
                "Kappentiefe/-extrusion (Meter zum Spieler hin) der Kurzzeit-Knöpfe bei Shape=Square. Gilt "
                + "NUR für diese Gruppe. Live änderbar; begrenzt auf 0.006..0.08.",
            ["RoundButtons/Travel"] =
                "Druckweg (Meter) der Kurzzeit-Knöpfe — wie weit eine Kappe unter der Fingerspitze einsinkt, "
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
            ["BoardDashboard/GearWidth"] =
                "Kappenbreite (Meter) der Einstellungs-Zahnrad-Platte auf dem Kontrollbrett. Gilt NUR für das "
                + "Zahnrad. Live änderbar; begrenzt auf 0.02..0.20.",
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
            ["Board/ForceFarMode"] =
                "Deaktiviert das Zeigen aus der Nähe mit der Fingerspitze und benutzt immer den Fernstrahl. "
                + "Nützlich für Desktop-/Entwicklertests ([Dev] SimulateHands), bei denen die Fake-Hände das "
                + "Spielbrett nie erreichen.",
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
            ["FigureGrab/HeldScale"] =
                "VERALTET — ohne Wirkung, ersetzt durch [FigureGrab] Glove/Plate/ArcaneHeldScale. Dieser "
                + "Eintrag wird genau einmal gelesen, als Startwert für jene Pro-Stil-Schlüssel beim ersten "
                + "Anlegen, danach nie wieder. Bleibt gebunden, damit bestehende Konfigurationsdateien "
                + "weiterhin laden. Historische Bedeutung: Betrachtungs-Zoom zusätzlich zur Weltgröße der Figur "
                + "auf dem Spielbrett, während sie gehalten wird (1 = Brettgröße in deiner Hand; höher "
                + "vergrößert sie).",
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
            ["FigureGrab/*HeldScale"] =
                "Betrachtungs-Zoom zusätzlich zur Weltgröße der Figur auf dem Spielbrett, während sie "
                + "gehalten wird. PRO STIL geltender absoluter Wert, solange dieser Handstil getragen wird "
                + "(ersetzt den gemeinsamen alten Eintrag, aus dem er beim ersten Lauf befüllt wurde). Live "
                + "änderbar — eine gehaltene Figur wird sofort neu ausgerichtet.",
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
            ["HexHighlight/KillBorderLine"] =
                "RÜCKFALL-Eingrenzungsregler: setzt zusätzlich _BorderLineIntensity (den scharfen Randring) "
                + "auf null. Einschalten, wenn das Mitschwimm-Artefakt trotz entfernter Flamme/Fadenkreuz "
                + "bestehen bleibt. Ändert die Optik (das Feld verliert seine scharfe Kontur).",
            ["HexHighlight/KillFill"] =
                "RÜCKFALL-Eingrenzungsregler: setzt zusätzlich _HexIntensity (die weiche weiße Füllung) auf "
                + "null. Nur zur Diagnose — das entfernt den größten Teil der Hervorhebung.",
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
            ["WorldUI/ScreenLeftMirrorFallback"] =
                "VERALTET — ohne Wirkung, und hatte auch nie eine: dieser Schlüssel wird nirgends im Mod "
                + "gelesen. Er gab sich als Hauptschalter für die Rettung der schwarzen Karte aus; der "
                + "tatsächliche Schalter ist MapAlbedoRender. Bleibt gebunden, damit bestehende .cfg-Dateien "
                + "unverändert laden.",
            ["WorldUI/MapAlbedoRender"] =
                "DIE KARTEN-KORREKTUR (Standard AN): rendert das Pergament der Kampagnenkarte UNBELEUCHTET "
                + "über eine modeigene FORWARD-Kamera in eine PRIVATE RenderTexture, die das Leinwand-Quad dann "
                + "anzeigt. Die Karte ist gewöhnliche Mesh-Geometrie (MapChoreographer.worldMap / cityMap, "
                + "GH_WorldMap-Materialien, deren Albedo in _Alb / _MainTex liegt), aber ihr "
                + "Deferred-Amplify-Shader wird nie in eine RenderTexture beleuchtet, die uns gehört, und ihr "
                + "Backbuffer ist unter XR nicht lesbar — statt das Rendering des Spiels abzugreifen, rendert "
                + "der Mod das Mesh deshalb neu, mit einem GloomhavenVR/MapUnlit-Material pro Submesh (Albedo "
                + "-> _MainTex, UV auf der GPU aus dem mesheigenen TexCoord0 gesampelt), nur für unser "
                + "Rendering eingesetzt und im selben Frame wiederhergestellt (rein darstellend, "
                + "mehrspielersicher). Eine von oben gemalte Karte wirkt unbeleuchtet korrekt. Aus = die "
                + "schwarze Karte wird erkannt, aber die Basis-RT bleibt, wie sie ist (schwarz).",
            ["WorldUI/MapAlbedoOriginalMaterial"] =
                "VERALTET — ohne Wirkung. Der Schlüssel wählte zwischen dem Rendern der Karte mit dem "
                + "spieleigenen Amplify-Material und einem Override-Material; die Karte wird jetzt immer mit "
                + "GloomhavenVR/MapUnlit gezeichnet, und beide von ihm benannten Alternativen gibt es nicht "
                + "mehr. Bleibt gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapAlbedoAmbient"] =
                "VERALTET — ohne Wirkung. Der Schlüssel erzwang ein helles Umgebungslicht während des "
                + "Forward-Renderings der Karte, damals, als dieses Rendering noch BELEUCHTET war. MapUnlit ist "
                + "unbeleuchtet, also kann kein Umgebungslicht-Wert die Karte verändern; der Code, der diesen "
                + "Schlüssel las, wurde entfernt. (Solange er lief, wurde gemessen: ein Umgebungslicht von 4 "
                + "machte aus einem flachen dunklen Pergament nur ein flaches helleres.) Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapAlbedoLight"] =
                "VERALTET — ohne Wirkung. Der Schlüssel fügte während des Forward-Renderings der Karte ein "
                + "Richtungslicht des Mods hinzu, für den Fall, dass die Kartendetails ein per Normal Map "
                + "erzeugtes Relief wären. MapUnlit ist unbeleuchtet, also kann ein Licht sie nicht "
                + "beeinflussen; der Code, der diesen Schlüssel las, wurde entfernt. Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapCaptureMode"] =
                "VERALTET — ohne Wirkung. Die Kampagnenkarte wird immer von der Forward-Albedo-Kamera des "
                + "Mods in eine private RenderTexture gerendert. Die hiermit gewählte Strategie "
                + "\"passive-deferred\" (1) wurde widerlegt — die Deferred-Kartenkamera des Spiels rendert in "
                + "jede RenderTexture, die uns gehört, nur Schwarz, woran kein Entfernen von Image Effects "
                + "etwas ändern kann — und die Strategie \"texture blit\" (2) wurde nie umgesetzt. Beide "
                + "Codepfade wurden entfernt. Bleibt gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapStripBeautify"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 1, entfernt). Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapStripVolumetricFog"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 1, entfernt). Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapStripSSAO"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 1, entfernt). Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapStripPostProcess"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 1, entfernt). Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapTexFlipX"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 2, das nie umgesetzt wurde). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapTexFlipY"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 2, das nie umgesetzt wurde). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapTexSwapDiag"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 2, das nie umgesetzt wurde). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapStripAllImageEffects"] =
                "VERALTET — ohne Wirkung (gehörte zu MapCaptureMode 1, entfernt). Bleibt gebunden, damit "
                + "bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvSource"] =
                "VERALTET — ohne Wirkung. Die UV der Karte wird von GloomhavenVR/MapUnlit auf der GPU aus dem "
                + "mesheigenen TexCoord0 gesampelt; der hiermit konfigurierte CPU-Pfad zum Neuaufbau von uv0 "
                + "wurde entfernt. (Das Mesh wurde offline extrahiert und mit seiner eigenen UV0 rasterisiert: "
                + "es ergibt die vollständige, korrekte Karte, es gibt also nichts zu korrigieren.) Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvSwapUV"] =
                "VERALTET — ohne Wirkung (gehörte zum entfernten CPU-Pfad für den uv0-Neuaufbau). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvFlipU"] =
                "VERALTET — ohne Wirkung (gehörte zum entfernten CPU-Pfad für den uv0-Neuaufbau). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvFlipV"] =
                "VERALTET — ohne Wirkung (gehörte zum entfernten CPU-Pfad für den uv0-Neuaufbau). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvChannel"] =
                "VERALTET — ohne Wirkung und bewusst nicht gelesen: der UV-Kanal des Shaders ist fest auf 0 "
                + "verdrahtet, damit ein veralteter gespeicherter Wert die Karte nicht zerstören kann. Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
            ["WorldUI/MapUvComponent"] =
                "VERALTET — ohne Wirkung (gehörte zum entfernten CPU-Pfad für den uv0-Neuaufbau). Bleibt "
                + "gebunden, damit bestehende .cfg-Dateien unverändert laden.",
        };
}
