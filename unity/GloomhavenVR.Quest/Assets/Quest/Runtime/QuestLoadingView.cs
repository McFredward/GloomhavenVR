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
        [SerializeField] Text stepLabel;
        [SerializeField] Text fileLabel;
        [SerializeField] Text fileNameLabel;
        [SerializeField] RectTransform fill;
        string lastLabel;
        string lastPercentage, lastStep, lastFile, lastFileName;
        int lastPercent = int.MinValue;
        int lastPreparationPercent = -1;
        public bool Available { get { return canvas != null && phaseLabel != null && percentageLabel != null && stepLabel != null && fileLabel != null && fileNameLabel != null && fill != null && gameObject != null && gameObject.activeInHierarchy
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
            // Keep the accepted wordmark, phase and bar geometry. Only the UI
            // backing extends down to distinguish the preparation total from
            // its current file, without a second camera or interactive surface.
            view.stepLabel = AddText(panel.transform, layer, "Preparation step", new Vector2(0, -211), new Vector2(1100, 42), font, 26);
            view.fileLabel = AddText(panel.transform, layer, "Current file progress", new Vector2(0, -253), new Vector2(1100, 42), font, 25);
            view.fileNameLabel = AddText(panel.transform, layer, "Current file name", new Vector2(0, -297), new Vector2(1100, 42), font, 23);
            view.UpdatePhase("", 0, 0);
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

        /// <summary>Present the caller's localized phase and its actual byte counts.</summary>
        public void UpdatePhase(string label, long processed, long total)
        {
            lastPreparationPercent = -1;
            Render(label, Percent(processed, total), "", "", "", "");
        }

        /// <summary>
        /// Overall measured preparation, independent of any startup/content DTO.
        /// Unmeasured work contributes only completed steps. Byte completion does
        /// not assert that its surrounding gate has committed successfully.
        /// </summary>
        public static int PreparationPercent(int completedSteps, int totalSteps, long processed, long total)
        {
            if (totalSteps <= 0) return -1;
            int completed = Math.Max(0, Math.Min(totalSteps, completedSteps));
            if (completed == totalSteps) return 100;
            double fraction = total > 0 && processed > 0 ? Math.Min(1d, (double)processed / total) : 0d;
            int nextBoundary = (int)Math.Ceiling(100d * (completed + 1) / totalSteps) - 1;
            return Math.Min(nextBoundary, (int)Math.Floor(100d * (completed + fraction) / totalSteps));
        }

        /// <summary>Show one stable total plus caller-localized step and file details.</summary>
        public void UpdateProgress(string label, string overallCaption, int completedSteps, int totalSteps,
            long processed, long total, string step, string file, string fileName)
        {
            int percent = PreparationPercent(completedSteps, totalSteps, processed, total);
            // A new phase or a temporarily unavailable measurement never rewinds
            // work already observed in this view. A new operation uses Create or
            // UpdatePhase to reset this preparation history explicitly.
            if (percent >= 0) lastPreparationPercent = Math.Max(lastPreparationPercent, percent);
            Render(label, lastPreparationPercent, overallCaption, step, file, fileName);
        }

        /// <summary>Readable plain basename; never show a storage path or text markup.</summary>
        public static string Basename(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int start = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;
            string name = path.Substring(start);
            const int limit = 56;
            if (name.Length > limit)
            {
                int length = limit - 1;
                if (char.IsHighSurrogate(name[length - 1])) length--;
                name = name.Substring(0, length) + "…";
            }
            char[] cleaned = null;
            for (int i = 0; i < name.Length; i++)
                if (char.IsControl(name[i]))
                {
                    if (cleaned == null) cleaned = name.ToCharArray();
                    cleaned[i] = ' ';
                }
            return cleaned == null ? name : new string(cleaned);
        }

        void Render(string label, int percent, string overallCaption, string step, string file, string fileName)
        {
            if (label != lastLabel) { lastLabel = label; phaseLabel.text = label; }
            string percentage = (string.IsNullOrEmpty(overallCaption) ? "" : overallCaption + ": ")
                + (percent < 0 ? "—" : percent.ToString(CultureInfo.InvariantCulture) + " %");
            if (percentage != lastPercentage) { lastPercentage = percentage; percentageLabel.text = percentage; }
            if (percent != lastPercent)
            {
                lastPercent = percent;
                fill.anchorMax = new Vector2(percent < 0 ? 0 : percent / 100f, 1);
            }
            if (step != lastStep) { lastStep = step; stepLabel.text = step; }
            if (file != lastFile) { lastFile = file; fileLabel.text = file; }
            if (fileName != lastFileName) { lastFileName = fileName; fileNameLabel.text = fileName; }
        }

        public void Retire() { gameObject.SetActive(false); Destroy(gameObject); }
    }
}
#endif
