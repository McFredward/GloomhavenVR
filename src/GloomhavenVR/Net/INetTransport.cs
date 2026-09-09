using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Abstraction over the underlying multiplayer channel used to ship cosmetic rig packets.
/// Payload bytes, sender player id and optional immutable local queue metadata — no Photon-Bolt
/// or FFSNet types leak across this seam, so
/// the avatar/sampler/renderer layers build and unit-reason independently of the transport,
/// and an offline/no-netcode environment gets a null-object implementation
/// (<see cref="NullNetTransport"/>) that keeps everything a strict no-op.
///
/// The production implementation is <see cref="FfsNetTransport"/> (piggybacks
/// <c>FFSNet.Synchronizer.SendSideAction</c> + a Harmony hook on
/// <c>FFSNet.ActionProcessor.ProcessSideAction</c>).
/// </summary>
internal interface INetTransport
{
    /// <summary>True when a live, online session exists and we have a local network identity.</summary>
    bool IsOnline { get; }

    /// <summary>Local player's network id, or 0 when unknown/offline.</summary>
    int LocalPlayerId { get; }

    /// <summary>
    /// Broadcast <paramref name="length"/> bytes of <paramref name="payload"/> to the other
    /// peers (unreliable). Must be a safe no-op when <see cref="IsOnline"/> is false and must
    /// never throw into game code.
    /// </summary>
    /// <param name="presentationIdentity">Optional immutable snapshot used to write these exact
    /// bytes successfully. Local queue metadata only; never transmitted or used on receive.
    /// Supplying it avoids decoding our own native presentation just to preserve identity edges.</param>
    void Send(byte[] payload, int length, object? presentationIdentity = null);

    /// <summary>
    /// Raised on the main thread for each inbound rig packet: (senderPlayerId, buffer, length).
    /// The buffer is transient — copy anything you need to keep beyond the callback.
    /// </summary>
    event Action<int, byte[], int>? PacketReceived;

    /// <summary>Install the receive hook / wire up sending. Idempotent; never throws.</summary>
    void Install();

    /// <summary>Remove hooks and detach. Idempotent; never throws.</summary>
    void Uninstall();
}

/// <summary>No-op transport for offline / netcode-absent / disabled scenarios.</summary>
internal sealed class NullNetTransport : INetTransport
{
    public bool IsOnline => false;
    public int LocalPlayerId => 0;
    public void Send(byte[] payload, int length, object? presentationIdentity = null) { }

    /// <summary>
    /// A REAL event, not <c>{ add { } remove { } }</c>. Empty accessors accept a handler and throw
    /// it away, which is how a subscription made one line too early stayed invisible through an
    /// entire multiplayer session: the driver held a subscription that had never been stored
    /// anywhere, and nothing could observe the difference. Keeping the handler costs nothing —
    /// this transport never raises the event — and leaves the mistake detectable.
    /// </summary>
#pragma warning disable CS0067 // never raised: that IS this transport's job, and the handler is still kept
    public event Action<int, byte[], int>? PacketReceived;
#pragma warning restore CS0067
    public void Install() { }
    public void Uninstall() { }
}
