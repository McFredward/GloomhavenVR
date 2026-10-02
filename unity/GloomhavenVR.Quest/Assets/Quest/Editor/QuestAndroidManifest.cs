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
            string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
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
            Debug.Log("[GloomhavenVR Quest] manifest removed unused eye-tracking declarations=" + removed);
        }
    }
}
