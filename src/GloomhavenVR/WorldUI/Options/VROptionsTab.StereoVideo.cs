using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    /// <summary>
    /// The maintainer requested the stereo pilot directly under Advanced after Build683's
    /// launch-argument experiment. This is a deliberate action, with visible runtime feedback;
    /// it never changes a saved setting, native tab focus or the lifetime of this menu.
    /// </summary>
    private static int BuildStereoVideoCalibrationRow()
    {
        if (ContentRoot == null) return 0;
        StereoVideoMenuStatus? feedback = null;
        GameObject action = BuildLinkRow(ContentRoot, Loc.StereoVideoCalibrationStart, () =>
        {
            TickGuard.Run("VROptionsTab.StereoVideo", StereoVideoCalibration.RequestStart, "WorldUI");
            feedback?.Refresh();
        }, asAction: true);

        int before = Rows.Count;
        BuildNote(ContentRoot, Loc.StereoVideoCalibrationMenuHint, multiline: true);
        GameObject? hint = Rows.Count > before ? Rows[Rows.Count - 1] : null;
        before = Rows.Count;
        BuildNote(ContentRoot, StereoVideoCalibration.Status, multiline: true);
        GameObject? status = Rows.Count > before ? Rows[Rows.Count - 1] : null;
        feedback = action.AddComponent<StereoVideoMenuStatus>();
        feedback.Bind(action, hint, status);
        return 3;
    }

    /// <summary>
    /// Attached only to the live Advanced action row. Decoder preparation/error/close and
    /// language changes repaint these labels in place; rebuilding would destroy the button
    /// under the pointer. Destroying the menu rows also ends this updater without static roots.
    /// </summary>
    private sealed class StereoVideoMenuStatus : MonoBehaviour
    {
        private TMP_Text? _action, _hint, _status;
        private GameObject? _hintRow, _statusRow;
        private float _hintMinimum, _statusMinimum;

        internal void Bind(GameObject action, GameObject? hint, GameObject? status)
        {
            _action = action.GetComponentInChildren<TMP_Text>(true);
            _hintRow = hint;
            _statusRow = status;
            _hint = hint?.GetComponentInChildren<TMP_Text>(true);
            _status = status?.GetComponentInChildren<TMP_Text>(true);
            _hintMinimum = hint?.GetComponent<LayoutElement>()?.minHeight ?? 0f;
            _statusMinimum = status?.GetComponent<LayoutElement>()?.minHeight ?? 0f;
            Refresh();
        }

        private void Update() => Refresh();

        internal void Refresh()
        {
            if (_action != null && _action.text != Loc.StereoVideoCalibrationStart)
                _action.text = Loc.StereoVideoCalibrationStart;
            PaintNote(_hintRow, _hint, Loc.StereoVideoCalibrationMenuHint, _hintMinimum);
            PaintNote(_statusRow, _status, StereoVideoCalibration.Status, _statusMinimum);
        }

        private static void PaintNote(GameObject? row, TMP_Text? label, string text, float minimum)
        {
            if (row == null || label == null || label.text == text) return;
            label.text = text;
            LayoutElement? layout = row.GetComponent<LayoutElement>();
            if (layout == null) return;
            float width = label.rectTransform.rect.width;
            if (width <= 1f) width = ((RectTransform)row.transform).rect.width - 24f;
            float height = Mathf.Max(minimum, label.GetPreferredValues(text, Mathf.Max(1f, width), float.PositiveInfinity).y + 16f);
            layout.minHeight = layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }
    }
}
