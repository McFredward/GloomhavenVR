using System;
using System.Collections.Generic;
using UnityEngine;

// Only external adapters are fixtures. Capture, assets, materials, codec, deltas, playback,
// clone-neutralization and all Unity UI/rendering operations compile from production sources.
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Info(string channel, string message) { }
        internal static void Note(string channel, string message) => Messages.Add(channel + ": " + message);
        internal static void Warn(string channel, string message) => Messages.Add(channel + ": " + message);
    }
}
namespace GloomhavenVR.Rig
{ internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Cards
{ internal static class CardFaceMipBake { internal static Sprite OriginalFor(Sprite sprite) => sprite; } }
namespace GloomhavenVR.WorldUI
{
    internal static class PanelMipBake { internal static Texture OriginalFor(Texture texture) => texture; }
    internal enum TownVoiceReaction : byte
    { MerchantOffer, MerchantBuy, MerchantSell, PriestessDonate, EnchantressEnhance, PriestessUnavailable,
      EnchantressOffer, EnchantressInspect, MerchantUnaffordable, MerchantSoldOut }
    internal static class TownServiceVoice
    {
        internal static Action<byte, TownVoiceReaction>? RelayRequest;
        internal static readonly List<(byte Service, TownVoiceReaction Reaction, int Peer, uint Session, uint Sequence, float Age)> Accepted = new();
        internal static bool AcceptRelayedReaction(byte service, TownVoiceReaction reaction,
            int peer, uint session, uint sequence, float age)
        {
            if (!TownServicePopulation.IsFaceAuthor || age < 0f || age > 3f) return false;
            Accepted.Add((service, reaction, peer, session, sequence, age)); return true;
        }
    }
    internal static class TownServicePopulation { internal static bool IsFaceAuthor = true; }

    // The converted-window distance ladder has its own production harness. Here
    // only its registration boundary is inert; remote construction remains real.
    internal static class TownServiceDepthOrder
    {
        internal static readonly HashSet<Transform> Bound = new();
        internal static void Bind(Transform root) => Bound.Add(root);
        internal static void Refresh(GameObject root) { }
    }

}
namespace GloomhavenVR.Net
{
    internal static class NetProtocol
    {
        internal const byte ExtIdTownWorkspaceCloth = 90;
        internal const byte ExtIdTownInteraction = 91;
        internal const byte ExtIdTownTransaction = 92;
        internal const byte ExtIdTownCatalogLayout = 94, ExtIdTownVisitorStock = 95, ExtIdTownDonationClock = 93;
        internal const float StaleTimeoutSeconds = 3f;
    }
    internal struct TownClothRunnerState { internal Vector2 Left, Right, LeftVelocity, RightVelocity; }
    internal static class TownResidentsCodec
    {
        internal static void WriteCloth(byte[] buffer, ref int at, in TownClothRunnerState runner)
        {
            static byte Position(float value) => unchecked((byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -.1f, .1f) * 1000f));
            static byte Velocity(float value) => unchecked((byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -.25f, .25f) * 500f));
            buffer[at++] = Position(runner.Left.x); buffer[at++] = Position(runner.Left.y);
            buffer[at++] = Position(runner.Right.x); buffer[at++] = Position(runner.Right.y);
            buffer[at++] = Velocity(runner.LeftVelocity.x); buffer[at++] = Velocity(runner.LeftVelocity.y);
            buffer[at++] = Velocity(runner.RightVelocity.x); buffer[at++] = Velocity(runner.RightVelocity.y);
        }
        internal static TownClothRunnerState ReadCloth(byte[] buffer, ref int at)
        {
            static float Position(byte value) => unchecked((sbyte)value) * .001f;
            static float Velocity(byte value) => unchecked((sbyte)value) * .002f;
            return new TownClothRunnerState {
                Left = new Vector2(Position(buffer[at++]), Position(buffer[at++])),
                Right = new Vector2(Position(buffer[at++]), Position(buffer[at++])),
                LeftVelocity = new Vector2(Velocity(buffer[at++]), Velocity(buffer[at++])),
                RightVelocity = new Vector2(Velocity(buffer[at++]), Velocity(buffer[at++])) };
        }
    }
}
namespace GloomhavenVR.Net.TownServices
{
    // Network grants are exercised by TownGrantCases. This Unity fixture isolates
    // original-widget publication and playback with no Bolt transport attached.
    internal static class TownServiceGrantSync
    {
        internal static int GrantedOwner(byte service) => 0;
        internal static void SetOffer(byte service, uint session, bool active) { }
        internal static bool MayCommit(byte service, uint session) => true;
        internal static bool Unavailable(byte service, uint session) => false;
        internal static bool Denied(byte service, uint session) => false;
        internal static void ForgetPeer(int player) { }
        internal static void Reset() { }
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal static void Apply(GameObject root)
        { foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 9; }
    }
}
