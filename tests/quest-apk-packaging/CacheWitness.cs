using System;
using System.IO;
using GloomhavenVR.Quest.Editor;

public static class CacheWitness
{
    static void Require(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }
    static void Sparse(string path, long size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var stream = File.Create(path)) stream.SetLength(size);
    }
    public static int Main(string[] args)
    {
        string root = args[0];
        string module = Path.Combine(root, "Gradle/unityLibrary");
        Directory.CreateDirectory(module);
        string output = Path.Combine(root, "Gradle/launcher/build/outputs/apk/debug");
        Require(QuestAndroidManifest.RemoveLargeCachedApks(module) == 0, "cold cache");
        string small = Path.Combine(output, "small.apk");
        string large = Path.Combine(output, "launcher-debug.apk");
        string nonApk = Path.Combine(output, "diagnostic.bin");
        string original = Path.Combine(root, "original/Game.apk");
        string native = Path.Combine(root, "Gradle/unityLibrary/il2cpp/libil2cpp.so");
        Sparse(small, 2147483647L);
        Sparse(large, 2147483648L);
        Sparse(nonApk, 2147483648L);
        Sparse(original, 2147483648L);
        Sparse(native, 128L);
        Require(QuestAndroidManifest.RemoveLargeCachedApks(module) == 1, "one large generated APK");
        Require(!File.Exists(large), "large cached APK removed");
        Require(File.Exists(small) && File.Exists(nonApk), "small APK/non-APK retained");
        Require(File.Exists(original) && File.Exists(native), "original/native input retained");
        Require(QuestAndroidManifest.RemoveLargeCachedApks(module) == 0, "warm idempotence");
        bool rejected = false;
        try { QuestAndroidManifest.RemoveLargeCachedApks(Path.Combine(root, "original")); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && File.Exists(original), "unrecognized module rejected");
        Console.WriteLine("PASS actual generated-cache boundary, sparse large APK, cold/warm reuse, retained native/original inputs");
        return 0;
    }
}
