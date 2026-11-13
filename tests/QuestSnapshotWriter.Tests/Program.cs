using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GloomhavenVR.Quest;

int assertions = 0;
void Check(bool condition, string why) { assertions++; if (!condition) throw new InvalidOperationException(why); }
string root = args[0]; Directory.CreateDirectory(root);
string path = Path.Combine(root, "state.json");
int caller = Environment.CurrentManagedThreadId;
var calls = new ConcurrentQueue<(string json, int thread)>();
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
using (var writer = new QuestSnapshotWriter(path, (target, json) =>
{
    calls.Enqueue((json, Environment.CurrentManagedThreadId));
    if (json == "first") { entered.Set(); release.Wait(); }
    QuestSnapshotWriter.Publish(target, JsonSerializer.Serialize(new { value = json }));
}))
{
    try
    {
        writer.Enqueue("first"); Check(entered.Wait(TimeSpan.FromSeconds(5)), "worker entered controlled slow IO");
        // This must complete while the actual file publisher is still blocked.
        var producer = Task.Run(() => { for (int i = 0; i < 1000; i++) writer.Enqueue(i.ToString()); });
        Check(producer.Wait(TimeSpan.FromSeconds(5)), "slow file IO must not block the producer");
        Check(calls.Count == 1 && !release.IsSet, "one publisher owns IO; no thread per pending snapshot");
    }
    finally { release.Set(); }
    writer.Flush();
    var rows = calls.ToArray();
    Check(rows.Length == 2 && rows[0].json == "first" && rows[1].json == "999", "coalesce to latest pending snapshot");
    Check(rows.All(row => row.thread != caller), "no file publication on owner thread");
    using var state = JsonDocument.Parse(File.ReadAllText(path));
    Check(state.RootElement.GetProperty("value").GetString() == "999", "durable flush contains latest state");
    writer.TakeCompleted(out double ms, out string error);
    Check(ms > 0 && error == null, "retain actual background write duration");
    writer.TakeCompleted(out ms, out error); Check(ms == 0 && error == null, "consume results once");
}
Check(!File.Exists(path + ".tmp"), "no temporary file after close");

// Actual file errors preserve the last completed state and do not poison retries.
File.WriteAllText(path, "{\"old\":true}");
Directory.CreateDirectory(path + ".tmp");
using (var writer = new QuestSnapshotWriter(path))
{
    writer.Enqueue("{\"new\":1}"); writer.Flush();
    Check(File.ReadAllText(path) == "{\"old\":true}", "failed IO retains previous complete file");
    writer.TakeCompleted(out _, out string error); Check(!string.IsNullOrEmpty(error), "error returned to owner without worker Unity logging");
    Directory.Delete(path + ".tmp");
    writer.Enqueue("{\"new\":2}"); writer.Flush();
    using var repaired = JsonDocument.Parse(File.ReadAllText(path));
    Check(repaired.RootElement.GetProperty("new").GetInt32() == 2, "later snapshots repair transient file failure");
}

// Exercise real publication repeatedly while another thread reads: no partially
// written JSON is exposed, and disposal drains before accepting no more work.
var failures = new ConcurrentQueue<Exception>();
using var stop = new CancellationTokenSource();
var reader = Task.Run(() =>
{
    while (!stop.IsCancellationRequested)
        try { using var value = JsonDocument.Parse(File.ReadAllText(path)); }
        catch (IOException) { } // Windows may briefly deny a reader during replace.
        catch (Exception error) { failures.Enqueue(error); }
});
var atomic = new QuestSnapshotWriter(path);
for (int i = 0; i < 100; i++) { atomic.Enqueue(JsonSerializer.Serialize(new { generation = i, text = new string('漢', 8192) })); atomic.Flush(); }
atomic.Dispose(); stop.Cancel(); reader.GetAwaiter().GetResult();
Check(failures.IsEmpty, "atomic readers never see truncated or mixed JSON");
using (var last = JsonDocument.Parse(File.ReadAllText(path))) Check(last.RootElement.GetProperty("generation").GetInt32() == 99, "dispose drains final publication");
atomic.Enqueue("invalid late write"); atomic.Flush();
using (var last = JsonDocument.Parse(File.ReadAllText(path))) Check(last.RootElement.GetProperty("generation").GetInt32() == 99, "closed writer ignores late lifecycle call");
Console.WriteLine($"PASS snapshot publication: {assertions} assertions; real thread, coalescing, slow IO, atomic readers, retry and shutdown");
