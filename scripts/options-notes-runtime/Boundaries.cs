using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal static partial class VROptionsTab
    {
        private static GameObject? _toggleTemplate;
        internal static TMP_FontAsset Font = null!;
        internal const float AuthoredFontSize = 24f;
        // Explicit native row skeleton/clone and caption-style seams. The production
        // BuildNote is extracted unchanged; this boundary performs no note sizing.
        internal static void Configure(GameObject template) => _toggleTemplate = template;
        internal static void Note(Transform parent, string text, bool multiline) => BuildNote(parent, text, multiline);
        private static GameObject StampRow(GameObject? template, Transform parent,
            out TMP_Text? title, out Transform? option)
        {
            GameObject row = Object.Instantiate(template!, parent);
            row.SetActive(true);
            title = row.transform.Find("Title").GetComponent<TMP_Text>();
            option = row.transform.Find("Option");
            return row;
        }
        private static void ApplyOptionCaption(TMP_Text title)
        {
            title.font = Font;
            title.fontSize = AuthoredFontSize;
            title.fontSizeMin = AuthoredFontSize * .78f;
            title.fontSizeMax = AuthoredFontSize;
            title.enableAutoSizing = true;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Overflow;
            title.alignment = TextAlignmentOptions.TopLeft;
            title.color = Color.white;
        }
    }
}
