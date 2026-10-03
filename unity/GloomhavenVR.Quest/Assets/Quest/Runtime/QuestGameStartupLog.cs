#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GloomhavenVR.Quest
{
    /// <summary>Bounded per-run diagnostic evidence, retaining a bounded previous run.</summary>
    internal sealed class QuestGameStartupLog
    {
        internal const int MaxBytes = 256 * 1024, MaxRecords = 512;
        readonly string path;
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int bytes, records;
        bool capped;
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
            File.WriteAllText(path, "", new UTF8Encoding(false));
            Append("run " + stamp);
        }
        internal void Append(string message, string stack = null)
        {
            if (capped) return;
            string key = Clip(message ?? "", 4096);
            if (!seen.Add(key)) return;
            string line = DateTime.UtcNow.ToString("O") + " " + key + "\n";
            if (!string.IsNullOrEmpty(stack)) line += Clip(stack, 8192) + "\n";
            int length = Encoding.UTF8.GetByteCount(line);
            const string marker = "[Quest startup] diagnostic log limit reached; further records suppressed.\n";
            if (records >= MaxRecords - 1 || bytes + length + marker.Length > MaxBytes)
            {
                File.AppendAllText(path, marker, new UTF8Encoding(false)); bytes += marker.Length; capped = true; return;
            }
            File.AppendAllText(path, line, new UTF8Encoding(false)); bytes += length; records++;
        }
        static string Clip(string value, int length) { return value.Length > length ? value.Substring(0, length) + " [truncated]" : value; }
    }
}
#endif
