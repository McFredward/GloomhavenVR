using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GloomhavenVR.Quest;

static partial class Program
{
    static QuestGameContentDeliveryResult Install(Fixture fixture, Action<QuestGameContentProgress>? progress = null)
        => QuestGameContent.Install(fixture.Manifest, fixture.Output, fixture.Source, true, fixture.Staged, Archive, progress);

    static void InstallationCache()
    {
        var cold = new Fixture();
        Check(QuestGameContent.GetExisting(cold.Manifest, cold.Output) == null, "installation-absent");
        var progress = new List<QuestGameContentProgress>();
        var result = Install(cold, progress.Add); cold.Match();
        Check(result.InstallationReceiptPublished && result.InstallationState == "installed" && result.ExtractedFiles == 3,
            "installation-cold-receipt");
        Progress(progress, new FileInfo(cold.Staged).Length + cold.Payloads.Sum(p => (long)p.Length));
        Check(progress.All(p => p.Phase is "copying-archive" or "extracting-file" or "content-ready"), "installation-one-workload");
        File.Delete(cold.Source); File.Delete(cold.Staged); progress.Clear();
        // A read-share lock traps any accidental content-byte scan. Cheap metadata
        // remains available, even when the source APK/archive no longer exists.
        using (var locked = new FileStream(cold.Target(0), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                result = Install(cold, progress.Add);
                Check(QuestGameContent.GetExisting(cold.Manifest, cold.Output) != null, "receipt-fast-get-existing");
            }
            catch (IOException) { Check(false, "receipt-warm-no-content-reads"); }
        }
        Check(result.InstallationReceiptReused && result.ReusedContent && result.VerifiedBytes == 0 && result.VerifiedFiles == 0
            && result.MetadataCheckedFiles == 3 && progress.Count == 0, "receipt-warm-zero-byte-work");
        string key = result.ContentKey;
        var otherScope = new QuestGameContentManifest { schema = 1, inputKey = cold.Manifest.inputKey,
            archive = "quest-mod-content.zip", archiveSha256 = cold.Manifest.archiveSha256, files = cold.Manifest.files };
        Check(QuestGameContent.GetExisting(otherScope, cold.Output, "quest-mod-content.zip") == null, "receipt-archive-namespace");
        cold.Manifest.inputKey = "new-source-build-same-owned-content";
        string repacked = Path.Combine(cold.Directory, "repacked.zip");
        using (var zip = ZipFile.Open(repacked, ZipArchiveMode.Create))
            for (int i = 0; i < cold.Manifest.files.Length; ++i)
                using (var file = zip.CreateEntry(cold.Manifest.files[i].path, CompressionLevel.Optimal).Open()) file.Write(cold.Payloads[i]);
        string repackedHash = Sha(File.ReadAllBytes(repacked));
        Check(repackedHash != cold.Manifest.archiveSha256, "receipt-real-zip-repack");
        cold.Manifest.archiveSha256 = repackedHash; // Same required files, different ZIP bytes.
        Array.Reverse(cold.Manifest.files);
        result = Install(cold, progress.Add);
        Check(result.ContentKey == key && result.InstallationReceiptReused && progress.Count == 0, "receipt-source-only-update-reuse");

        var legacy = new Fixture(); legacy.Deliver(); File.Delete(legacy.Source); File.Delete(legacy.Staged);
        progress.Clear(); result = Install(legacy, progress.Add);
        Check(result.InstallationReceiptPublished && result.InstallationState == "adopted" && result.VerifiedFiles == 3
            && result.CopiedBytes == 0 && result.ExtractedBytes == 0 && progress.Count == 0, "receipt-legacy-adopt-once");
        result = Install(legacy); Check(result.InstallationReceiptReused && result.VerifiedBytes == 0, "receipt-legacy-next-fast");

        var repair = new Fixture(); Install(repair);
        DateTime untouched = File.GetLastWriteTimeUtc(repair.Target(0));
        File.Delete(repair.Target(1));
        Check(QuestGameContent.GetExisting(repair.Manifest, repair.Output) == null, "receipt-detect-missing");
        File.Delete(repair.Source); progress.Clear(); result = Install(repair, progress.Add); repair.Match();
        Check(result.MetadataCheckedFiles == 2 && result.ExtractedFiles == 1 && result.VerifiedFiles == 0
            && result.ReusedArchive && result.InstallationState == "repaired", "receipt-only-missing-repaired");
        Check(File.GetLastWriteTimeUtc(repair.Target(0)) == untouched, "receipt-repair-keeps-unaffected");
        Progress(progress, repair.Payloads[1].Length);

        var changed = new Fixture(); Install(changed);
        byte[] altered = (byte[])changed.Payloads[0].Clone(); altered[30] ^= 1;
        File.WriteAllBytes(changed.Target(0), altered);
        Check(QuestGameContent.GetExisting(changed.Manifest, changed.Output) == null, "receipt-detect-changed-metadata");
        result = Install(changed); changed.Match();
        Check(result.MetadataCheckedFiles == 2 && result.ExtractedFiles == 1, "receipt-changed-repaired");
        File.SetLastWriteTimeUtc(changed.Target(0), new DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result = Install(changed);
        Check(result.MetadataCheckedFiles == 2 && result.VerifiedFiles == 1 && result.ExtractedFiles == 0
            && result.InstallationReceiptPublished, "receipt-touched-valid-file-adopted");

        var upgraded = new Fixture(); Install(upgraded);
        string priorKey = QuestGameContent.GetExisting(upgraded.Manifest, upgraded.Output)!.ContentKey;
        upgraded.Payloads[1] = Bytes(upgraded.Payloads[1].Length, 711);
        upgraded.Manifest.files[1].sha256 = Sha(upgraded.Payloads[1]);
        File.WriteAllBytes(upgraded.Target(1), upgraded.Payloads[1]);
        result = Install(upgraded); upgraded.Match();
        Check(result.ContentKey != priorKey && result.MetadataCheckedFiles == 2 && result.VerifiedFiles == 1
            && result.ExtractedFiles == 0, "receipt-actual-bank-change-reuses-subset");

        foreach (string corrupt in new[] { "truncated", "checksum", "oversize", "bad-schema" })
        {
            var fixture = new Fixture(); Install(fixture); string path = QuestGameContent.InstallationReceiptPath(fixture.Output);
            byte[] bytes = File.ReadAllBytes(path);
            if (corrupt == "truncated") File.WriteAllBytes(path, bytes[..25]);
            else if (corrupt == "checksum") { bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(path, bytes); }
            else if (corrupt == "oversize") { using var output = File.OpenWrite(path); output.SetLength(8L * 1048576 + 1); }
            else
            {
                bytes[4] = 2; byte[] checksum = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)); checksum.CopyTo(bytes, bytes.Length - 32);
                File.WriteAllBytes(path, bytes);
            }
            Check(QuestGameContent.GetExisting(fixture.Manifest, fixture.Output) == null, "receipt-invalid-" + corrupt);
            result = Install(fixture); fixture.Match();
            Check(result.VerifiedFiles == 3 && result.InstallationReceiptPublished && !result.InstallationReceiptReused,
                "receipt-invalid-adopt-" + corrupt);
        }

        var falseStats = new Fixture(); Install(falseStats);
        string receiptPath = QuestGameContent.InstallationReceiptPath(falseStats.Output);
        byte[] falseReceipt = File.ReadAllBytes(receiptPath);
        falseReceipt[falseReceipt.Length - 40] ^= 1; // Last entry's ctime nanoseconds, with a valid envelope checksum.
        SHA256.HashData(falseReceipt.AsSpan(0, falseReceipt.Length - 32)).CopyTo(falseReceipt, falseReceipt.Length - 32);
        File.WriteAllBytes(receiptPath, falseReceipt);
        Check(QuestGameContent.GetExisting(falseStats.Manifest, falseStats.Output) == null, "receipt-false-metadata-declined");
        result = Install(falseStats);
        Check(result.MetadataCheckedFiles == 2 && result.VerifiedFiles == 1 && result.ExtractedFiles == 0
            && result.InstallationReceiptPublished, "receipt-false-metadata-rechecked");

        var cancelled = new Fixture(); bool stopped = false;
        try { Install(cancelled, row => { if (row.Phase == "extracting-file" && row.FileIndex == 2) throw new OperationCanceledException(); }); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && !File.Exists(QuestGameContent.InstallationReceiptPath(cancelled.Output)), "receipt-no-partial-publication");
        result = Install(cancelled); cancelled.Match();
        Check(result.VerifiedFiles == 1 && result.ExtractedFiles == 2 && result.InstallationReceiptPublished, "receipt-interruption-resume");
        Check(!Directory.GetFiles(cancelled.Output, "*.tmp", SearchOption.AllDirectories).Any(), "receipt-no-leftover-temp");

        var wrong = new Fixture(wrongFileHash: true); Reject(() => Install(wrong), "installation-first-file-sha");
        Check(!File.Exists(QuestGameContent.InstallationReceiptPath(wrong.Output)), "receipt-never-publishes-wrong-bytes");
        var partial = new Fixture(); Install(partial);
        File.WriteAllText(QuestGameContent.InstallationReceiptPath(partial.Output) + ".quest-unfinished.tmp", "partial write");
        Check(QuestGameContent.GetExisting(partial.Manifest, partial.Output) != null, "receipt-atomic-prior-retained");
        var reserved = new Fixture(); reserved.Manifest.files[0].path = ".quest-installation.receipt";
        Reject(() => Install(reserved), "installation-receipt-alias");
    }
}
