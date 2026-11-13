using System;
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Remove the pinned OpenXR package's unconditional eye-tracking requirement.</summary>
    public sealed class QuestAndroidManifest : IPostGenerateGradleAndroidProject
    {
        const string Android = "http://schemas.android.com/apk/res/android";
        public int callbackOrder { get { return int.MaxValue; } }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string mainManifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(mainManifest)) throw new InvalidOperationException("Generated Unity manifest is missing");
            int removed = 0;
            foreach (string manifest in Directory.GetFiles(path, "AndroidManifest.xml", SearchOption.AllDirectories))
            {
                // XR Management writes requirements into a separate .androidlib manifest.
                // Gradle merges that file after these callbacks, so clean both source roots.
                string parent = Path.GetFileName(Path.GetDirectoryName(manifest));
                if (manifest != mainManifest && !parent.EndsWith(".androidlib", StringComparison.Ordinal)) continue;
                removed += RemoveUnusedRequirements(manifest);
            }
            Debug.Log("[GloomhavenVR Quest] manifest removed unused eye-tracking declarations=" + removed);
            RemoveLargeCachedApks(path);
        }

        public static int RemoveLargeCachedApks(string unityLibrary)
        {
            // AGP 4's incremental ZIP reader fails on signed offsets in an old
            // >2 GiB APK. The actual 096d warm build failed at offset 2161209521;
            // rebuilding only the generated APK from unchanged native inputs
            // succeeds. Preserve every import, C++ object and other Gradle task.
            string module = Path.GetFullPath(unityLibrary);
            if (Path.GetFileName(module) != "unityLibrary")
                throw new InvalidOperationException("Generated Unity Gradle module path is unrecognized.");
            string root = Path.GetDirectoryName(module);
            string output = root;
            foreach (string component in new[] { "launcher", "build", "outputs", "apk" })
            {
                output = Path.Combine(output, component);
                if (!Directory.Exists(output)) return 0;
                if ((File.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Generated APK cache contains a linked directory.");
            }
            int removed = 0;
            var directories = new System.Collections.Generic.Stack<string>();
            directories.Push(output);
            while (directories.Count != 0)
            {
                string directory = directories.Pop();
                foreach (string child in Directory.GetDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Generated APK cache contains a linked directory.");
                    directories.Push(child);
                }
                foreach (string apk in Directory.GetFiles(directory, "*.apk"))
                {
                    if ((File.GetAttributes(apk) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Generated APK cache contains a linked file.");
                    if (new FileInfo(apk).Length < 2147483648L) continue;
                    File.Delete(apk);
                    removed++;
                }
            }
            if (removed != 0)
                Debug.Log("[Quest Campaign] Fresh packaging for large cached APKs=" + removed + "; native build cache retained.");
            return removed;
        }
        static int RemoveUnusedRequirements(string manifest)
        {
            var document = new XmlDocument();
            document.Load(manifest);
            var root = document.DocumentElement;
            if (root == null || root.Name != "manifest") throw new InvalidOperationException("Android manifest root is invalid");
            int removed = 0;
            foreach (XmlNode child in root.SelectNodes("uses-feature | uses-permission"))
            {
                var element = child as XmlElement;
                if (element == null) continue;
                string name = element.GetAttribute("name", Android);
                if (name != "oculus.software.eye_tracking" && name != "com.oculus.permission.EYE_TRACKING") continue;
                root.RemoveChild(element);
                ++removed;
            }
            document.Save(manifest);
            return removed;
        }
    }
}
