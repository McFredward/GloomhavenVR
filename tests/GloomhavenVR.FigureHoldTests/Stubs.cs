using System;
using UnityEngine;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object o)
        {
        }
        public static T[] FindObjectsOfType<T>() => Array.Empty<T>();
    }
    public sealed class GameObject : Object
    {
        public bool activeSelf = true;
        public void SetActive(bool v)
        {
            activeSelf = v;
        }
        public Transform transform;
        public ActorBehaviour? Actor;
        public GameObject()
        {
            transform = new Transform(this);
        }
    }
    public sealed class Transform
    {
        public GameObject gameObject;
        public Transform? parent;
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public bool IsChildOf(Transform root)
        {
            for (Transform? t = this; t != null; t = t.parent)
                if (t == root)
                    return true;
            return false;
        }
        public Transform(GameObject owner)
        {
            gameObject = owner;
        }
        public Vector3 position
        {
            get => localPosition + (parent?.position ?? Vector3.zero);
            set => localPosition = value - (parent?.position ?? Vector3.zero);
        }
        public Quaternion rotation
        {
            get => localRotation;
            set => localRotation = value;
        }
    }
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float a, float b, float c)
        {
            x = a;
            y = b;
            z = c;
        }
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 zero => default;
        public static Vector3 operator *(Vector3 v, float f) => new(v.x * f, v.y * f, v.z * f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float k) => a + (b - a) * k;
        public bool Equals(Vector3 v) => x == v.x && y == v.y && z == v.z;
        public override bool Equals(object? o) => o is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);
    }
    public struct Quaternion
    {
        public float w;
        public static Quaternion identity => new() { w = 1 };
        public static Quaternion Slerp(Quaternion a, Quaternion b, float k) => b;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Exp(float a) => MathF.Exp(a);
        public static float Lerp(float a, float b, float k) => a + (b - a) * k;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.00001f;
    }
    public sealed class Animator
    {
        public Transform transform;
        public object? runtimeAnimatorController = new();
        public bool ValidState = true;
        public Animator(Transform t)
        {
            transform = t;
        }
        public bool HasState(int l, int h) => ValidState;
        public static int StringToHash(string s) => s.GetHashCode();
    }
    public static class Time
    {
        public static float unscaledTime, unscaledDeltaTime = 1;
    }
}
namespace ScenarioRuleLibrary
{
    public sealed class CActor
    {
        public int ID;
        public string ActorGuid = "";
    }
}
public sealed class ActorBehaviour
{
    public UnityEngine.GameObject m_RootGameObject = new(), m_AnimatedGameObject = new();
    public ScenarioRuleLibrary.CActor Actor = new();
    public GameObject m_Hilight = new();
    public bool Busy, IsMoving, m_NewLocoTarget, m_Jump, m_IsPushPullInProgress, m_Teleport;
    public WorldspacePanelUIController? m_WorldspacePanelUI;
    public static ActorBehaviour GetActorBehaviour(UnityEngine.GameObject obj) => obj.Actor!;
    public void SetLocoTarget()
    {
    }
    public void PushPullToLocation()
    {
    }
    public void TeleportToLocation()
    {
    }
    public void TeleportToCurrentLocoTarget()
    {
    }
    public void ForceSetLocoIntermediateTarget()
    {
    }
    public static void SetHilighted(UnityEngine.GameObject obj, bool value)
    {
    }
}
public sealed class WorldspacePanelUIController
{
    public bool FlowControlActive() => false;
    public UnityEngine.GameObject m_ObjectToTrack = new();
}
public sealed class WorldspaceUITools
{
    public static WorldspaceUITools Instance = new();
    public List<WorldspacePanelUIController> _panelUIControllers = new();
}
namespace GloomhavenVR.Hands
{
    public enum HandSide
    {
        Left, Right
    }
    public sealed class VRHand
    {
        public HandSide Side;
    }
}
namespace GloomhavenVR.Core
{
    public static class PerfConfig
    {
        public static bool FigureScanCacheOn = true;
    }
    public static class VRLog
    {
        public static void Error(string a, string b) { }
        public static void Info(string a, string b)
        {
        }
        public static void Note(string a, string b)
        {
        }
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t)
        {
        }
        public HarmonyPatch(Type t, string s)
        {
        }
        public HarmonyPatch(string s)
        {
        }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute
    {
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    public static class HeldFigures
    {
        public static readonly HashSet<ActorBehaviour> Actors = new();
        public static int Count => Actors.Count;
        public static IEnumerable<ActorBehaviour> All => Actors;
        public static bool Owns(ActorBehaviour a) => Actors.Contains(a);
        public static bool TryGetSlot(int s, out ActorBehaviour a, out GloomhavenVR.Hands.HandSide h)
        {
            a = null!;
            h = default;
            return false;
        }
    }
    public static partial class FigureBusy
    {
        private static bool _animationWait = false, _ruleClientBusy = false;
        private static ActorBehaviour? _waitActor = null;
        private static ScenarioRuleLibrary.CActor? _currentActor = null;
        private static void Sample()
        {
        }
        private static bool OwnAnimationPlaying(ActorBehaviour actor) => actor.Busy;
        public static bool HoldMustEnd(ActorBehaviour actor) => HoldMustEnd(actor, out _);
    }

    public static partial class FigureGhosts
    {
        private sealed class Ghost
        {
            public GameObject Go = new();
        }
        private static readonly Dictionary<ActorBehaviour, Ghost> _ghosts = new();
        public static GameObject? GhostFor(ActorBehaviour a) => _ghosts.TryGetValue(a, out var g) ? g.Go : null;
        public static UnityEngine.GameObject GhostSource(ActorBehaviour a) => a.m_AnimatedGameObject;
        public static void NotifyHeld(ActorBehaviour a, UnityEngine.Vector3 p, UnityEngine.Quaternion r)
        {
            _ghosts.TryAdd(a, new());
        }
    }

    public sealed partial class FigureGrabbable
    {
        public static float HeldSizeFactorOf(ActorBehaviour a) => 1;
        public static FigureGrabbable? AttachedTo(ActorBehaviour a)
        {
            foreach (var g in Live)
                if (g._actor == a)
                    return g;
            return null;
        }
        public float CaptureBodyRadiusRealMeters => 0;
        public float CaptureCeilingRealMeters => 0;
        public float LatchTotalRatio => 1;
        public float Stretch => 1;
        public string Label => "mini";
    }
    public static class FigureGrabConfig
    {
        public static float StretchReachRealMeters => 1;
    }
    public static class FigureStretchMath
    {
        public static float InteractionRadiusRealMeters(float a, float b) => a + b;
    }
    public static class FigureCloth
    {
        public static void Note(UnityEngine.GameObject g, float s)
        {
        }
    }

}
namespace GloomhavenVR.Net
{
    public static class NetAvatarDriver
    {
        public static bool TryGetPeerRigScale(int p, out float s)
        {
            s = 1;
            return true;
        }
    }
    public static class NetProtocol
    {
        public const float InterpolationSharpness = 50;
        public static ushort EncodeHeldStretch(float f) => (ushort)(f * 100);
        public static float DecodeHeldStretch(ushort f) => f / 100f;
    }
}

public sealed class Choreographer
{
    public UnityEngine.GameObject FindClientActorGameObject(ScenarioRuleLibrary.CActor a)
    {
        foreach (var p in WorldspaceUITools.Instance._panelUIControllers)
            if (ReferenceEquals(p.m_ObjectToTrack.Actor?.Actor, a))
                return p.m_ObjectToTrack;
        return null!;
    }
}
public static class MF
{
    public static bool AnimatorPlay(UnityEngine.Animator a, string s) => true;
}
public sealed class SceneController
{
    public static SceneController Instance = new();
    public ErrorState GlobalErrorMessage = new();
    public sealed class ErrorState
    {
        public bool ShowingMessage;
    }
}
public static class PhaseManager
{
    public static object? CurrentPhase = new();
}
namespace ScenarioRuleLibrary
{
    public static class ScenarioRuleClient
    {
        public static System.Threading.Thread s_MainThread = System.Threading.Thread.CurrentThread;
    }
    public class CMessageData
    {
        public enum MessageType
        {
            ActorIsKilling, ActorIsHealing, ActorIsSelectingItemCards, ActorHasDamaged, PlacingTrap, DisarmTrap, ActivateOrDeactivateSpawner, DestroyObstacle, ActorIsApplyingConditionActiveBonus, RecoverLostCards, RecoverDiscardedCards, SelectRecoverCards, SelectLoseCards, SelectIncreasedCardLimit, SelectExtraTurnCards, ActorIsPulling, ActorIsPushing, PlayerSelectingToAvoidDamageOrNot, ActionSelection
        }
        public MessageType m_Type;
        public CActor? m_ActorSpawningMessage;
    }
    public sealed class CActorHasMoved_MessageData : CMessageData
    {
        public CActor? m_MovingActor;
        public List<CActor>? m_ActorsToCarry;
    }
    public sealed class CActorHasTeleported_MessageData : CMessageData
    {
        public CActor? m_ActorTeleported;
    }
    public sealed class CActorsAreSwapping_MessageData : CMessageData
    {
        public CActor? m_FirstTarget;
        public CActor? m_SecondTarget;
    }
    public sealed class CActorIsAttacking_MessageData : CMessageData
    {
        public CActor? m_AttackingActor;
        public List<CActor>? m_ActorsAttacking;
    }
    public sealed class CActorBeenAttacked_MessageData : CMessageData
    {
        public CActor? m_ActorBeingAttacked;
    }
    public sealed class CActorBeenDamaged_MessageData : CMessageData
    {
        public CActor? m_ActorBeingDamaged;
    }
    public sealed class CSummon_MessageData : CMessageData
    {
        public CActor? m_ActorSummoning;
    }
    public sealed class CRevive_MessageData : CMessageData
    {
        public CActor? m_ActorReviving;
    }
    public sealed class CActorDead_MessageData : CMessageData
    {
        public CActor? m_Actor;
    }
}

namespace GloomhavenVR.Board.FigureGrab
{
    public sealed partial class FigureGrabbable
    {
        private static readonly List<FigureGrabbable> Live = new(), Gliding = new();
        private ActorBehaviour _actor = null!;
        private GloomhavenVR.Hands.VRHand? _holder;
        private bool _glideActive;
        private Vector3 _glideFromPos, _glideFromScale, _origLocalPos, _origLocalScale;
        private Quaternion _glideFromRot, _origLocalRot;
        private float _glideStartTime;
        public int RestoreCount;
        public bool GlidingNow => _glideActive;
        private GameObject Root => _actor.m_RootGameObject;
        public static FigureGrabbable Create(ActorBehaviour actor, GloomhavenVR.Hands.VRHand hand)
        {
            var g = new FigureGrabbable { _actor = actor, _holder = hand };
            Live.Add(g);
            HeldFigures.Actors.Add(actor);
            return g;
        }
        private string Describe() => "mini";
        private bool TryBeginGlide()
        {
            if (_holder == null)
                return false;
            Live.Remove(this);
            Gliding.Add(this);
            _holder = null;
            _glideActive = true;
            _glideFromPos = Vector3.one;
            _glideFromScale = Vector3.one;
            _origLocalPos = Vector3.zero;
            _origLocalScale = Vector3.one;
            _glideFromRot = Quaternion.identity;
            _origLocalRot = Quaternion.identity;
            _glideStartTime = 0;
            return true;
        }
        private void FinishGlide(Transform? t)
        {
            _glideActive = false;
            Gliding.Remove(this);
            HeldFigures.Actors.Remove(_actor);
        }
        private void NoteClothScale(Transform t)
        {
        }
        public void Restore()
        {
            RestoreCount++;
            Live.Remove(this);
            Gliding.Remove(this);
            _glideActive = false;
            _holder = null;
            HeldFigures.Actors.Remove(_actor);
        }
        public void TickLocalGlide() => TickGlide();
    }
    public static class HeldGlideMath
    {
        public const float DurationSeconds = .28f;
        public static bool Sample(float a, float b, out float e)
        {
            e = 0;
            return true;
        }
        public static void WriteEased(Transform t, Vector3 a, Quaternion b, Vector3 c, Vector3 d, Quaternion e, Vector3 f, float g)
        {
        }
    }
}
