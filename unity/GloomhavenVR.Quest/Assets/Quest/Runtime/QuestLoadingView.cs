#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Quest
{
    /// <summary>Reusable, noninteractive loading artwork under the current camera owner.</summary>
    public sealed class QuestLoadingView : MonoBehaviour
    {
        public const string LogoResource = "quest-loading-logo";
        [SerializeField] Canvas canvas;
        [SerializeField] Text phaseLabel;
        [SerializeField] Text percentageLabel;
        [SerializeField] RectTransform fill;
        string lastLabel;
        string lastPercentage;
        int lastPercent = int.MinValue;
        int overallPercent;
        public bool Available { get { return canvas != null && phaseLabel != null && percentageLabel != null && fill != null && gameObject != null && gameObject.activeInHierarchy
            && canvas.worldCamera != null && canvas.worldCamera.isActiveAndEnabled; } }

        public static QuestLoadingView Create(Camera owner, int layer)
        {
            if (owner == null) throw new InvalidOperationException("Loading artwork requires its current camera owner.");
            Texture2D logo = Resources.Load<Texture2D>(LogoResource);
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (logo == null || font == null) throw new InvalidOperationException("Original loading logo or font is unavailable.");
            GameObject panel = new GameObject("Quest loading artwork", typeof(RectTransform), typeof(Canvas), typeof(QuestLoadingView));
            panel.layer = layer;
            panel.transform.SetParent(owner.transform, false);
            panel.transform.localPosition = new Vector3(0, -.025f, 1.8f);
            panel.transform.localScale = Vector3.one * .001f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1200, 640);
            QuestLoadingView view = panel.GetComponent<QuestLoadingView>();
            view.canvas = panel.GetComponent<Canvas>();
            view.canvas.renderMode = RenderMode.WorldSpace;
            view.canvas.worldCamera = owner;
            // Every piece is ordinary world-space UI on its owner's presentation
            // layer. No camera, input, raycaster or tracking rig is created here.
            AddImage(panel.transform, layer, "Backdrop", new Vector2(0, -45), new Vector2(1200, 600), new Color(.025f, .03f, .045f, .94f));
            RectTransform artwork = Child(panel.transform, layer, "Original GloomhavenVR logo", new Vector2(0, 80),
                new Vector2(1060, 1060f * logo.height / logo.width));
            RawImage image = artwork.gameObject.AddComponent<RawImage>();
            image.texture = logo; image.raycastTarget = false;
            view.phaseLabel = AddText(panel.transform, layer, "Loading phase", new Vector2(0, -53), new Vector2(1100, 65), font, 32);
            RectTransform track = AddImage(panel.transform, layer, "Progress track", new Vector2(0, -119), new Vector2(1060, 14), new Color(.22f, .19f, .14f, 1f));
            view.fill = AddImage(track, layer, "Progress", Vector2.zero, Vector2.zero, new Color(.84f, .67f, .39f, 1f));
            view.fill.anchorMin = Vector2.zero; view.fill.anchorMax = new Vector2(0, 1);
            view.fill.offsetMin = view.fill.offsetMax = Vector2.zero;
            view.percentageLabel = AddText(panel.transform, layer, "Progress percentage", new Vector2(0, -167), new Vector2(1100, 50), font, 30);
            // A single startup bar belongs to this view. File names, integrity
            // phases and internal gate counters remain developer telemetry.
            view.UpdateOverall("", 0);
            return view;
        }

        static RectTransform Child(Transform parent, int layer, string name, Vector2 position, Vector2 size)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.layer = layer; child.transform.SetParent(parent, false);
            RectTransform rectangle = child.GetComponent<RectTransform>();
            rectangle.anchorMin = rectangle.anchorMax = new Vector2(.5f, .5f);
            rectangle.anchoredPosition = position; rectangle.sizeDelta = size;
            return rectangle;
        }
        static RectTransform AddImage(Transform parent, int layer, string name, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rectangle = Child(parent, layer, name, position, size);
            Image image = rectangle.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return rectangle;
        }
        static Text AddText(Transform parent, int layer, string name, Vector2 position, Vector2 size, Font font, int fontSize)
        {
            Text text = Child(parent, layer, name, position, size).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            text.color = new Color(.94f, .91f, .84f, 1f); text.raycastTarget = false;
            return text;
        }

        /// <summary>Phase-local byte percentage; -1 means there is no measured total.</summary>
        public static int Percent(long processed, long total)
        {
            if (total <= 0) return -1;
            if (processed <= 0) return 0;
            if (processed >= total) return 100;
            // Converting Int64 maxima to double can round a partial count to the
            // total. Only an actual completed byte count may display 100 percent.
            return Math.Min(99, (int)Math.Floor(100d * processed / total));
        }

        /// <summary>Move the same artwork to the observed rig without a new camera or canvas.</summary>
        public void Retarget(Camera owner, int layer)
        {
            if (owner == null) throw new InvalidOperationException("Loading artwork requires an observed camera owner.");
            transform.SetParent(owner.transform, false);
            canvas.worldCamera = owner;
            SetLayer(transform, layer);
        }
        static void SetLayer(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            for (int i = 0; i < node.childCount; ++i) SetLayer(node.GetChild(i), layer);
        }

        /// <summary>One monotonic startup percentage; 100 requires the native scene handover.</summary>
        public void UpdateOverall(string label, int percent, bool nativeHandover = false)
        {
            int bounded = nativeHandover ? 100 : Math.Max(0, Math.Min(99, percent));
            overallPercent = Math.Max(overallPercent, bounded);
            if (label != lastLabel) { lastLabel = label; phaseLabel.text = label; }
            string percentage = overallPercent.ToString(CultureInfo.InvariantCulture) + " %";
            if (percentage != lastPercentage) { lastPercentage = percentage; percentageLabel.text = percentage; }
            if (overallPercent != lastPercent)
            {
                lastPercent = overallPercent;
                fill.anchorMax = new Vector2(overallPercent / 100f, 1);
            }
        }

        public void Retire() { gameObject.SetActive(false); Destroy(gameObject); }
    }
}
#endif
