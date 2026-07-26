using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic ghost of a remote player's ITEM fan (<see cref="ItemsPile"/>) — the counterpart of
/// <see cref="RemoteHandFan"/> for equipped item cards.
///
/// ROOT CAUSE this exists (user report 5, multiplayer half): the extras packet only ever carried
/// the ABILITY hand-card count, so when a peer raised their item fan — a very visible, near-square
/// arc of cards floating over their palm or their board — every other player saw absolutely
/// nothing. The item fan is now broadcast as an additive count + a held/board-anchored flag
/// (<see cref="NetProtocol.FlagItemFan"/>), and this renders it.
///
/// ANTI-CHEAT / bandwidth: BACKS only, exactly like <see cref="RemoteHandFan"/>'s default. No item
/// identity, art or state ever rides the wire; a peer sees how many item cards are up and where the
/// fan is, nothing more. Item cards are near-square rather than 63.5×88, so the slab uses its own
/// dimensions — the real per-card face size is deliberately NOT transmitted (it would be per-card
/// data for a back-only visual).
///
/// PLACEMENT mirrors the local fan's two modes: HAND-HELD → floating a palm standoff above the
/// sender's DOMINANT hand (the hand that pinch-grabbed the item stack); BOARD-ANCHORED → floating
/// above the sender's synced control board at the same shared board-top spot the local fan uses.
/// Faces away from the owner's head so the owner's side reads as the "front" and everyone else sees
/// backs — the same convention as every other remote card visual.
/// </summary>
internal sealed class RemoteItemFan
{
    // ---- geometry (mirror of ItemsPile's arc constants) --------------------------------------
    private const int MaxCards = 12;
    private const float CardW = 0.075f;                  // item cards read near-square…
    private const float CardH = CardW * 1.15f;           // …so this is NOT the 88/63.5 ability ratio
    private const float Radius = 0.1792f * 1.7f;         // CardsConfig.FanEffectiveRadius × RadiusFactor
    private const float MaxArcDegrees = 110f;            // ItemsPile.MaxArcDegrees
    private const float MaxStepDegrees = 10f;            // ItemsPile.MaxStepDegrees
    private const float ZStagger = 0.004f;               // ItemsPile.ZStagger (draw order)
    private const float HandPalmOffset = 0.16f;          // ItemsPile.HandPalmOffset
    private const float BoardFloatHeight = 0.26f;        // ItemsPile.BoardFloatHeight
    private const float BoardFloatProudZ = -0.05f;       // ItemsPile.BoardFloatProudZ
    private const float Smoothing = 14f;

    private readonly RemoteAvatar _owner;
    private GameObject? _root;
    private readonly List<GameObject> _cards = new(MaxCards);
    private Mesh? _mesh;
    private int _builtCount = -1;
    private bool _poseInit;
    private int _loggedCount = -1;
    private bool _loggedHeld;

    // Fan-out reveal, identical to RemoteHandFan's (the local ItemsPile.EmergeAll flies the chips
    // OUT of the stack on open — a peer must see a fan SPREAD, not a fan pop into existence).
    private float _openElapsed = -1f;
    private const float OpenSeconds = 0.22f;

    public RemoteItemFan(RemoteAvatar owner)
    {
        _owner = owner;
    }

    public void Tick(float dt)
    {
        int count = Mathf.Clamp(_owner.ItemCardCount, 0, MaxCards);
        if (count == 0 || !TryResolvePose(out Vector3 target, out Quaternion rot))
        {
            Hide();
            return;
        }

        EnsureRoot();
        if (_root == null)
            return;
        if (count != _builtCount)
            Rebuild(count);
        // Sender scale lives on the root (the fan is NOT parented under a scaled holder), and it
        // changes live with the sender's diorama zoom — re-apply every frame, it is one compare.
        float rigScale = _owner.AppliedScale;
        if (!Mathf.Approximately(_root.transform.localScale.x, rigScale))
            _root.transform.localScale = Vector3.one * rigScale;
        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true;   // snap on the frame we appear, never ease in from a stale pose
            _openElapsed = 0f;  // …but the chips fan OUT of the centre stack
        }

        Transform t = _root.transform;
        if (_poseInit)
        {
            _poseInit = false;
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rot, k));
        }

        Layout(count, dt);

        if (count != _loggedCount || _owner.ItemFanHeld != _loggedHeld)
        {
            _loggedCount = count;
            _loggedHeld = _owner.ItemFanHeld;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: {count} item card(s), " +
                              $"{(_owner.ItemFanHeld ? $"hand-held above their {(_owner.ItemFanLeftHand ? "LEFT" : "RIGHT")} palm" : "anchored above their board")} " +
                              "— backs only (no item identity on the wire).");
        }
    }

    /// <summary>Where the fan sits this frame: above the sender's dominant palm when they hold it,
    /// otherwise above their synced control board. False when neither reference exists yet.</summary>
    private bool TryResolvePose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        float scale = _owner.AppliedScale;

        if (_owner.ItemFanHeld)
        {
            // The grabbing hand rides the wire as a flag (FlagItemFanLeft) — the owner may raise the
            // item fan with either hand, and guessing the dominant one put it on the wrong arm.
            Transform? holder = _owner.ItemFanLeftHand ? _owner.LeftHandHolder : _owner.RightHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            pos = holder.position + holder.up * (HandPalmOffset * scale);
        }
        else
        {
            if (!_owner.HasBoard)
                return false;
            float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
            var local = new Vector3(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);
            pos = _owner.BoardPosition + _owner.BoardRotation * (local * bs);
        }

        // Fronts (−Z) toward the owner, backs toward everyone else — the shared card convention.
        Transform? head = _owner.HeadHolder;
        if (head != null)
        {
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f)
                rot = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        return true;
    }

    /// <summary>Arc the slabs in fan-local space — the same reading arc <see cref="ItemsPile"/>
    /// lays its chips out on (capped sweep, capped per-card step, z-staggered for draw order).</summary>
    private void Layout(int n, float dt)
    {
        float step = n > 1 ? Mathf.Min(MaxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        float blend = 1f;
        if (_openElapsed >= 0f)
        {
            _openElapsed += Mathf.Max(dt, 0f);
            float u = OpenSeconds > 0f ? Mathf.Clamp01(_openElapsed / OpenSeconds) : 1f;
            blend = u * u * (3f - 2f * u);
            if (u >= 1f)
                _openElapsed = -1f;
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * Radius, (Mathf.Cos(rad) - 1f) * Radius, -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle);
            if (blend < 1f)
            {
                pos = Vector3.Lerp(new Vector3(0f, 0f, -ZStagger * i), pos, blend);
                rot = Quaternion.Slerp(Quaternion.identity, rot, blend);
            }
            Transform t = _cards[i].transform;
            t.localPosition = pos;
            t.localRotation = rot;
        }
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteItemFan[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        // Sized by the sender's rig scale so the fan reads the same physical size as their hands.
        _root.transform.localScale = Vector3.one * _owner.AppliedScale;
        _root.SetActive(false);
        _mesh = RemoteHandFan.BuildBackSlab(CardW, CardH);
        VRLayers.Apply(_root);
    }

    private void Rebuild(int count)
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();

        Material back = CardMesh.CreateBackMaterial(); // SHARED cache — never ours to destroy
        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Item{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }
        _builtCount = count;
        VRLayers.Apply(_root!);
    }

    private void Hide()
    {
        if (_loggedCount > 0)
        {
            _loggedCount = 0;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closed.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _openElapsed = -1f; // next appearance fans out again
    }

    public void Destroy()
    {
        _cards.Clear();
        _builtCount = -1;
        if (_mesh != null)
            Object.Destroy(_mesh); // asset — not freed with the GameObject tree
        _mesh = null;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}
