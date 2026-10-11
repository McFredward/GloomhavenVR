using UnityEngine;
namespace GloomhavenVR.Hands { internal static class HandStyles { public const int Count = 3; } }
namespace GloomhavenVR.Core { internal static class VRLog { public static void Info(string category, string text) {} } }
namespace GloomhavenVR.Net
{
    internal static class HeadMaskLibrary { public const int MaskCount = 3; }
    internal static class RemoteHandFan { public const float DefaultCardWidth = .0635f; }
    internal struct PresenceState
    {
        public bool HasSecondHeldCard, HasHeldCardGrip, HasHeldCardFace, HasHeldMapCard;
        public RigPose SecondHeldCardPose;
        public byte HeldCardGripMask, SecondHeldFaceCode, SecondHeldFaceCount, HeldMapArcSeat;
        public int SecondHeldFaceActorId;
        public uint HeldMapKey;
        public ushort HeldMapPoolSeat, HeldMapPoolCount;
        public TownItemHeldSource? HeldTownItem;
    }
    // Geometry/artwork construction is outside this motion receipt. Production pose,
    // authority, scale and billboard methods operate on actual Unity transforms.
    internal sealed partial class RemoteAvatar
    {
        private bool _hasAtomicSecondHeldCardState, _hasSecondHeldCard, _secondHeldFaceAddressReady;
        private bool _atomicPrimaryHeld, _atomicPrimaryLeft;
        private bool _loggedHeldCardRigid, _heldCardBillboardLogged, _hasTarget;
        private byte _heldCardGripMask, _secondHeldMapArcSeat, _secondHeldFaceCode, _secondHeldFaceCount;
        private int _secondHeldFaceActorId;
        private uint _secondHeldMapKey;
        private ushort _secondHeldMapPoolSeat, _secondHeldMapPoolCount;
        private RigPose _secondHeldCardPose;
        private TownItemHeldSource? _secondHeldTownItem;
        private float _heldCardWidth = .0635f, _heldInspectScale = 1.6f, _loggedHeldSlabScale = -1f;
        private AvatarState _target;
        private Transform? _heldCardHolder, _secondCardHolder;
        private readonly Transform _headHolder = new GameObject("Motion head").transform;
        public float AppliedScale = 2f;
        public int PlayerId => 2;
        internal enum HeldCardSizing { NominalCardOnly, OwnerHeldCard }
        private Transform BuildCardSlab(string name, HeldCardSizing sizing)
        {
            Transform value = new GameObject(name).transform;
            value.gameObject.SetActive(false); return value;
        }
        public void Rig(in AvatarState state)
        {
            _target = state; _hasTarget = true;
            AcceptSecondHeldCardRig(in state);
        }
        public void Legacy(in PresenceState state) => AcceptSecondHeldCardExtras(in state);
        public void Frame(float dt)
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);
            UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
            UpdateCardSlab(ref _heldCardHolder, "Primary", _target.HasHeldCard, in _target.HeldCardPose, k,
                HeldCardSizing.OwnerHeldCard, (_heldCardGripMask & NetProtocol.HeldCardGripFirstBit) != 0);
            UpdateCardSlab(ref _secondCardHolder, "Secondary", _hasSecondHeldCard, in _secondHeldCardPose, k,
                HeldCardSizing.OwnerHeldCard, (_heldCardGripMask & NetProtocol.HeldCardGripSecondBit) != 0);
        }
        public Transform? Primary => _heldCardHolder;
        public Transform? Secondary => _secondCardHolder;
        public bool Source(TownItemHeldSource source) => _secondHeldFaceAddressReady
            && _secondHeldTownItem.HasValue && _secondHeldTownItem.Value.Same(source);
        public void Cleanup()
        {
            if (_heldCardHolder != null) Object.DestroyImmediate(_heldCardHolder.gameObject);
            if (_secondCardHolder != null) Object.DestroyImmediate(_secondCardHolder.gameObject);
            Object.DestroyImmediate(_headHolder.gameObject);
        }
    }
}
