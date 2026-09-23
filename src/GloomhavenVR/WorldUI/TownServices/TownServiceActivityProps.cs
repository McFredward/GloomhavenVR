using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Original game coins follow actual fingertip contact, including interruptions.</summary>
internal sealed class TownServiceActivityProps : IDisposable
{
    private readonly Transform _root;
    private readonly byte _service;
    private readonly Transform?[] _coins = new Transform?[3];
    private Transform? _leftGrip;
    private Vector3 _coinOffset, _coinRest;
    private bool _gripsBound, _suspended;

    internal TownServiceActivityProps(Transform root, byte service, Shader? shader)
    { _root = root; _service = service; }

    internal void BindCoin(Transform coin, Vector3 offset)
    {
        ClearCopies();
        _coinOffset = offset; _coinRest = coin.localPosition; _coins[0] = coin;
        for (int i = 1; i < _coins.Length; i++)
        {
            // Decoration already stripped gameplay components from this native asset.
            // Sharing its station-owned materials also preserves fade/lighting parity.
            _coins[i] = UnityEngine.Object.Instantiate(coin.gameObject, coin.parent, false).transform;
            _coins[i]!.name = "Town.CountingCoin" + i;
            _coins[i]!.gameObject.SetActive(true);
        }
    }

    internal void Sample(in TownActivityPose pose)
    { var visual = TownServiceActivityMotion.Visual(_service, in pose); Sample(in visual); }
    internal void Sample() { var pose = default(TownActivityPose); Sample(in pose); }
    internal void Sample(in TownActivityVisual visual)
    {
        if (_service != 1) return;
        _suspended = false;
        if (!_gripsBound)
        {
            _gripsBound = true;
            _leftGrip = _root.Find("ActivityGripLeft");
        }
        if (_leftGrip == null) return;
        Vector3 pinch = _root.InverseTransformPoint(_leftGrip.position);
        for (int i = 0; i < _coins.Length; i++)
        {
            Transform? coin = _coins[i];
            if (coin == null) continue;
            Vector3 seat = i == 0 ? visual.Coin0 : i == 1 ? visual.Coin1 : visual.Coin2;
            float grip = i == 0 ? visual.CoinGrip.x : i == 1 ? visual.CoinGrip.y : visual.CoinGrip.z;
            // Grasp/release weights change only during the authored contact dwell.
            // During transport the coin remains rigidly attached to the real pinch.
            coin.localPosition = _coinOffset + Vector3.Lerp(seat, pinch, grip);
        }
    }

    internal void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        if (_coins[0] != null) _coins[0]!.localPosition = _coinRest;
        for (int i = 1; i < _coins.Length; i++)
            if (_coins[i] != null) _coins[i]!.localPosition = _coinOffset + TownServiceActivityMotion.CoinSeat(i, false);
    }

    // Coin copies share decoration materials; the decoration owner writes visibility.
    internal void SetVisibility(float value) { }
    private void ClearCopies()
    {
        for (int i = 1; i < _coins.Length; i++)
            if (_coins[i] != null) { UnityEngine.Object.Destroy(_coins[i]!.gameObject); _coins[i] = null; }
    }
    public void Dispose() => ClearCopies();
}
