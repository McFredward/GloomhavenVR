using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core { internal class NamespaceMarker { } }
namespace UnityEngine.UI { internal class NamespaceMarker { } }
namespace UnityEngine
{
    internal static class Mathf
    {
        internal static float Abs(float value) => Math.Abs(value);
        internal static float DeltaAngle(float a, float b)
        {
            float delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
internal enum UIWindowID { QuestPopup, QuestLog, Other }
internal sealed class UIWindow { internal UIWindowID ID; }
namespace GloomhavenVR.WorldUI
{
    internal sealed class TargetStub
    {
        internal UIWindow Window = new();
        internal T? GetComponent<T>() where T : class => Window as T;
    }
    internal sealed class ConvertedPanel
    {
        internal bool IsAlive = true;
        internal TargetStub? Target;
    }
    internal static partial class ModalFallback
    {
        private sealed class Claim
        {
            internal ConvertedPanel? Panel;
            internal float HalfWidthDeg;
        }
        private static readonly Claim[] _arcClaims = { new(), new() };
        private static readonly float[] _arcSeatWorldYaw = new float[2];
        private const float NeighbourGapDegrees = 2f, ChooserSlotExtraGapDegrees = 4f;
        private static readonly List<float> TestedYaws = new();
        private static int _checks;
        private static UIWindow? WindowForPanel(ConvertedPanel panel) => panel.Target?.Window;
        private static bool IsQuestLogWindow(UIWindow? window) => window?.ID == UIWindowID.QuestLog;
        // A deterministic occupancy fixture, including the log's actual angular reservation.
        private static bool ArcSeatIsFree(float yaw, float half)
        {
            TestedYaws.Add(yaw);
            for (int i = 0; i < _arcClaims.Length; i++)
                if (_arcClaims[i].Panel != null && Math.Abs(UnityEngine.Mathf.DeltaAngle(yaw,
                    _arcSeatWorldYaw[i])) < _arcClaims[i].HalfWidthDeg + half + 6f - 0.001f)
                    return false;
            return true;
        }
        private static ConvertedPanel Panel(UIWindowID id) => new() { Target = new() { Window = new() { ID = id } } };
        private static void Check(bool value, string why)
        {
            _checks++;
            if (!value) throw new Exception(why);
        }
        private static void Setup(float gaze = 0f, float logOffset = 33f)
        {
            _arcArrivingPanel = Panel(UIWindowID.QuestPopup);
            _arcClaims[0].Panel = Panel(UIWindowID.QuestLog);
            _arcClaims[0].HalfWidthDeg = 5.5f;
            _arcSeatWorldYaw[0] = gaze + logOffset;
            _arcClaims[1].Panel = null;
            TestedYaws.Clear();
        }
        private static void Main()
        {
            foreach (float gaze in new[] { 0f, 175f, -175f, 350f })
            foreach (float logOffset in new[] { 33f, -33f })
            {
                Setup(gaze, logOffset);
                Check(TryQuestSelectionPreferredSeat(gaze, 28f, 12f, out float offset), "free centre must be preferred");
                Check(offset == 0f, "free centre must not inherit adjacent-log offset");
                Check(TestedYaws.Count == 1 && TestedYaws[0] == gaze, "centre occupancy is tested before adjacent seat");
                Check(!QuestPopupTakesRightCorner(_arcArrivingPanel), "standing log retains its corner");
            }
            Setup();
            _arcClaims[1].Panel = Panel(UIWindowID.Other);
            _arcClaims[1].HalfWidthDeg = 0f;
            _arcSeatWorldYaw[1] = -17f;
            Check(TryQuestSelectionPreferredSeat(0f, 28f, 12f, out float fallback), "occupied centre retains adjacent fallback");
            Check(Math.Abs(fallback - 9.5f) < 0.001f, "fallback respects existing log clearance");
            _arcSeatWorldYaw[1] = 0f;
            Check(!TryQuestSelectionPreferredSeat(0f, 28f, 12f, out _), "blocked fallback returns ordinary search");
            Setup();
            Check(!TryQuestSelectionPreferredSeat(0f, -1f, 12f, out _), "oversized popup cannot claim gaze centre");
            Setup();
            _arcArrivingPanel = Panel(UIWindowID.Other);
            Check(!TryQuestSelectionPreferredSeat(0f, 28f, 12f, out _), "unrelated windows retain ordinary search");
            Check(!QuestPopupTakesRightCorner(_arcArrivingPanel), "unrelated windows cannot steal quest corner");
            Setup();
            _arcClaims[0].Panel = null;
            Check(QuestPopupTakesRightCorner(_arcArrivingPanel), "private selection retains right corner when log hidden");
            Check(!TryQuestSelectionPreferredSeat(0f, 28f, 12f, out _), "hidden log cannot invoke centre preference");
            string note = TakeQuestSelectionNote(_arcArrivingPanel);
            Check(note.Contains("RIGHT corner"), "private selection retains its diagnostic");
            Check(TakeQuestSelectionNote(_arcArrivingPanel) == string.Empty, "diagnostic is consumed once");
            Setup();
            _arcClaims[0].Panel!.IsAlive = false;
            Check(QuestPopupTakesRightCorner(_arcArrivingPanel), "destroyed quest log releases right corner");
            Console.WriteLine($"Quest seat: {_checks} production-linked assertions passed.");
        }
    }
}
