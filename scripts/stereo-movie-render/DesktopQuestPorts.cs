using System;
using UnityEngine;

// Android rendering is outside this desktop movie fixture. Its bridge's actual
// desktop guards and material policies are extracted separately from production.
// Never accept an Android-only asset or draw-witness request through these ports.
namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Debug }
}
namespace GloomhavenVR.Cards
{
    internal static class PlayTray
    {
        internal static Shader OverlayShader() => throw new InvalidOperationException("Unexpected Android overlay in desktop movie fixture");
    }
}

// Startup capture diagnostics do not author movie pixels or camera ownership.
namespace GloomhavenVR.WorldUI
{
    internal sealed class QuestMenuDrawWitness : MonoBehaviour
    {
        internal FlatScreen Owner = null!;
        private void OnEnable() => throw new InvalidOperationException("Unexpected Android witness in desktop movie fixture");
    }
    internal sealed partial class FlatScreen
    {
        private static void DesktopOnly()
        {
            if (Core.QuestStandalonePlatform.Enabled)
                throw new InvalidOperationException("Android diagnostics are outside desktop movie fixture");
        }
        private void ObserveQuestMenuProducer(Camera camera) => DesktopOnly();
        private void ReleaseQuestMenuReceipt() => DesktopOnly();
        private void TickQuestPresentationEvidence(bool visible) => DesktopOnly();
        private void ObserveQuestMenuFrame() => DesktopOnly();
    }
}
