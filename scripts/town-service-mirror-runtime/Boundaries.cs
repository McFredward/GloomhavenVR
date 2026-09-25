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
    { MerchantOffer, MerchantBuy, MerchantSell, PriestessDonate, EnchantressEnhance }
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
    // The cloth mesh/contact solver has its own furniture fixture. This mirror
    // fixture observes only the network lane and the inert clone's lifecycle.
    internal sealed class TownServiceCloth : IDisposable
    {
        internal static int Created, Ticks, Disposed;
        internal static TownServiceCloth? Last;
        internal static GloomhavenVR.Net.TownClothRunnerState LastFirst;
        internal bool Visible;
        internal TownServiceCloth(Transform station, byte service) { Created++; Last = this; }
        internal void SetVisible(bool visible) { Visible = visible; }
        internal void TickObserver(float age, float elapsed, in GloomhavenVR.Net.TownClothRunnerState first,
            in GloomhavenVR.Net.TownClothRunnerState second, bool visible)
        { Ticks++; LastFirst = first; }
        public void Dispose() { Disposed++; }
    }
}
namespace GloomhavenVR.Net
{
    internal static class NetProtocol { internal const byte ExtIdTownWorkspaceCloth = 90; }
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
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal static void Apply(GameObject root)
        { foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 9; }
    }
}
