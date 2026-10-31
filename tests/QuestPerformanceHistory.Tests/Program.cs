using GloomhavenVR.Core;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
var history = new QuestPerformanceHistory();
Check(history.Snapshot().Length == 0, "empty history contains no invented observations");
history.Record(false, 1, 1, "ProcGen", "FRAME hidden");
Check(history.Snapshot().Length == 0, "disabled history retains nothing");
foreach (string value in new[] { "SPIKE frame1", "", "other", "FRAMEWORK false", "COUNTSNOT false" })
    history.Record(true, 1, 1, "ProcGen", value);
Check(history.Snapshot().Length == 0, "only original aggregate report families are retained");
for (int index = 0; index < 35; index++) history.Record(true, index, index + .25, "ProcGen", "FRAME sample=" + index);
string[] current = history.Snapshot();
Check(current.Length == 24, "at most24 completed aggregate report lines survive");
Check(current[0].Contains("endFrame=11 endSeconds=11.25 FRAME sample=11"), "oldest retained observation has original chronological context");
Check(current[^1].EndsWith("FRAME sample=34"), "latest observation survives overflow");
current[0] = "external mutation";
Check(history.Snapshot()[0] != current[0], "support snapshot cannot mutate owned history");
history.Clear(); Check(history.Snapshot().Length == 0, "teardown retires previous session observations");
history.Record(true, 1, 2, "ProcGen", "SPLIT " + new string('x', 20000));
Check(history.Snapshot()[0].Length < 16500 && history.Snapshot()[0].EndsWith("[retained summary truncated]"), "unexpected giant reports stay bounded and are labelled");

QuestPerformanceEvidence.Clear();
VRLog.WantsDebug = false;
QuestPerformanceEvidence.RetainTiming("FRAME ordinary-player");
VRLog.WantsDebug = true; QuestStandalonePlatform.Enabled = false;
QuestPerformanceEvidence.RetainTiming("FRAME desktop");
Check(UnityEngine.SceneManagement.SceneManager.Reads == 0, "ordinary players and desktop perform no context query");
Check(QuestPerformanceEvidence.Snapshot().Length == 9, "disabled retention leaves existing actual-consumer diagnostics intact");
QuestStandalonePlatform.Enabled = true;
foreach (string value in new[] { "FRAME measured42ms", "STEPS actual-work", "COUNTS actual-consumers", "SPLIT Unity FrameTimingManager n/a (player unavailable)" })
    QuestPerformanceEvidence.RetainTiming(value);
string[] snapshot = QuestPerformanceEvidence.Snapshot();
Check(snapshot.Length == 13, "existing support snapshot includes all four exact timing families");
Check(snapshot.Take(9).SequenceEqual(new[] { "figure", "figureBank", "worldMaterial", "environment", "terrain", "environmentBank", "audio", "hex", "fire" }), "timing retention preserves every existing consumer diagnostic and order");
Check(snapshot[^4].EndsWith("FRAME measured42ms") && snapshot[^1].Contains("FrameTimingManager n/a"), "measured values and unavailable GPU status survive without reinterpretation");
Check(UnityEngine.SceneManagement.SceneManager.Reads == 4, "only existing closed summaries read context");
QuestPerformanceEvidence.Clear();
Check(QuestPerformanceEvidence.Snapshot().Length == 9, "teardown also clears delivered support evidence");
Console.WriteLine($"Quest performance history: {checks} assertions passed.");
