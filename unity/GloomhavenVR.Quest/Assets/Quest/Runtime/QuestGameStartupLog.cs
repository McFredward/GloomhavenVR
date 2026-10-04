#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GloomhavenVR.Quest
{
    /// <summary>Thread-safe bounded first-cause evidence, retaining a bounded previous run.</summary>
    internal sealed class QuestGameStartupLog
    {
        internal const int MaxBytes = 256 * 1024, MaxRecords = 512;
        internal const int MaxOriginalErrors = 64;
        const int StartupBytes = 32 * 1024, StartupRecords = 96;
        const int LifecycleBytes = MaxBytes / 2 - StartupBytes, ErrorBytes = MaxBytes / 2;
        const int LifecycleRecords = MaxRecords - MaxOriginalErrors - StartupRecords - 3;
        // Retain the existing grep token; it now caps only lifecycle records.
        const string LifecycleLimit = "[Quest startup] diagnostic log limit reached; further records suppressed.\n";
        const string ErrorLimit = "[Quest startup] original error log limit reached; further distinct causes suppressed.\n";
        const string StartupLimit = "[Quest startup] startup lifecycle log limit reached; further records suppressed.\n";
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        readonly string path;
        readonly object sync = new object();
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> errorsSeen = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> startupSeen = new HashSet<string>(StringComparer.Ordinal);
        int startupBytes, startupRecords;
        bool startupCapped;
        int bytes, records;
        int errorBytes, originalErrors;
        bool capped, errorsCapped;
        internal int OriginalErrors { get { return Volatile.Read(ref originalErrors); } }
        internal QuestGameStartupLog(string path, string stamp)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                // Older builds did not bound this file. Never move that potentially
                // unbounded file into a supposedly bounded previous-run artifact.
                using (var input = File.OpenRead(path))
                using (var previous = File.Create(Path.ChangeExtension(path, ".previous.log")))
                {
                    byte[] buffer = new byte[8192]; int remaining = MaxBytes;
                    while (remaining > 0)
                    {
                        int read = input.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                        if (read == 0) break;
                        previous.Write(buffer, 0, read); remaining -= read;
                    }
                }
            }
            File.WriteAllText(path, "", Utf8);
            Append("run " + stamp);
        }
        internal void Append(string message, string stack = null)
        {
            lock (sync)
            {
                // B614's real mod initialization filled the ordinary log budget
                // before native cameras/video/scene edges appeared. Reserve a
                // small bounded lifecycle lane independently of mod Debug traces
                // and the existing first-error reserve. It never stores stacks.
                if (message != null && message.StartsWith("[Quest startup]", StringComparison.Ordinal))
                {
                    if (startupCapped) return;
                    string startupKey = Clip(message, 4096);
                    if (startupSeen.Contains(startupKey)) return;
                    string startupLine = Line(startupKey, null);
                    int startupLength = Utf8.GetByteCount(startupLine);
                    if (startupRecords >= StartupRecords || startupBytes + startupLength + Utf8.GetByteCount(StartupLimit) > StartupBytes)
                    {
                        File.AppendAllText(path, StartupLimit, Utf8); startupBytes += Utf8.GetByteCount(StartupLimit); startupCapped = true; return;
                    }
                    File.AppendAllText(path, startupLine, Utf8); startupBytes += startupLength; startupRecords++; startupSeen.Add(startupKey);
                    return;
                }
                if (capped) return;
                string key = Clip(message ?? "", 4096);
                if (seen.Contains(key)) return;
                string line = Line(key, stack);
                int length = Utf8.GetByteCount(line);
                if (records >= LifecycleRecords || bytes + length + Utf8.GetByteCount(LifecycleLimit) > LifecycleBytes)
                {
                    File.AppendAllText(path, LifecycleLimit, Utf8); bytes += Utf8.GetByteCount(LifecycleLimit); capped = true; return;
                }
                File.AppendAllText(path, line, Utf8); bytes += length; records++; seen.Add(key);
            }
        }

        internal void AppendOriginalError(string message, string stack)
        {
            // B612 recorded one repeated InputSystem exception yet exhausted the
            // outer 24-error counter. Later loader failures disappeared entirely.
            // Deduplicate BEFORE spending this reserved error budget. Include the
            // stack in identity so equal messages from distinct causes survive.
            // A synchronous, closed-file append under this lock also works during
            // a blocked main-thread loader; no Unity API or Update flush is needed.
            lock (sync)
            {
                if (errorsCapped) return;
                string clippedMessage = Clip(message ?? "", 4096), clippedStack = Clip(stack ?? "", 8192);
                string key = clippedMessage + "\n" + clippedStack;
                if (errorsSeen.Contains(key)) return;
                string line = Line(clippedMessage, clippedStack.Length == 0 ? "[stack not supplied by Unity]" : clippedStack);
                int length = Utf8.GetByteCount(line);
                if (originalErrors >= MaxOriginalErrors || errorBytes + length + Utf8.GetByteCount(ErrorLimit) > ErrorBytes)
                {
                    File.AppendAllText(path, ErrorLimit, Utf8); errorBytes += Utf8.GetByteCount(ErrorLimit); errorsCapped = true; return;
                }
                // Publish only successfully persisted causes. If IO fails, leave
                // the key unspent so a later occurrence can still preserve it.
                File.AppendAllText(path, line, Utf8); errorBytes += length; errorsSeen.Add(key);
                Volatile.Write(ref originalErrors, originalErrors + 1);
            }
        }
        static string Line(string message, string stack)
        {
            string line = DateTime.UtcNow.ToString("O") + " " + message + "\n";
            if (!string.IsNullOrEmpty(stack)) line += Clip(stack, 8192) + "\n";
            return line;
        }
        static string Clip(string value, int length) { return value.Length > length ? value.Substring(0, length) + " [truncated]" : value; }
    }
}
#endif
