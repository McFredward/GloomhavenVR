using System;
using System.Collections.Generic;
using UnityEngine;

// Only native controller inputs and independent services are fixtures. Pose math, preparation,
// track selection, per-frame resampling, mesh ownership reads and mesh records are production.
public class ActorBehaviour : MonoBehaviour
{
    public static ActorBehaviour? GetActorBehaviour(GameObject root) => root.GetComponent<ActorBehaviour>();
}
public class UnknownPoseWriter : MonoBehaviour { }
// The mathematical regression rig stays stripped. The separate native actor-audit suite
// references the publisher's real types and original serialized prefab components instead.
public class IdleSMB : StateMachineBehaviour { }
public class AnimationOffsetSMB : StateMachineBehaviour { }
public class ToggleAlternativeIdleFxSMB : StateMachineBehaviour { }
public class ProgressChoreographerSMB : StateMachineBehaviour { }
public class CharacterManager : MonoBehaviour { }
public class ActorEvents : MonoBehaviour { }
public class UnityGameEditorObject : MonoBehaviour { }
public class VFXLookup : MonoBehaviour { }
public class FootstepSound : MonoBehaviour { }
public class AnimFXTrigger : MonoBehaviour { }
public interface IDetailDisablerProvider { }
public class CharacterShadowsDisabler : MonoBehaviour, IDetailDisablerProvider { }
public class EnemyShadowsDisabler : CharacterShadowsDisabler { }
public class DetailsDisabler : MonoBehaviour { }
public class DeathDissolve : MonoBehaviour
{
    public bool addVertexAnim;
    public static readonly List<DeathDissolve> s_DeathDissolvesInProgress = new();
}
public class AutomaticLOD : MonoBehaviour
{
    public enum SwitchMode { SwitchMesh, SwitchGameObject, UnityLODGroup }
    public SwitchMode LODSwitchMode => SwitchMode.UnityLODGroup;
}
namespace EPOOutline
{
    public class Outlinable : MonoBehaviour { }
    public class TargetStateListener : MonoBehaviour { }
}
public class WorldspaceDisplayPanelBase : MonoBehaviour
{
    public enum PoinToTrack { Base, HeadBone, HeadBoneStatic }
}
public class WorldspacePanelUIController : WorldspaceDisplayPanelBase
{
    public GameObject m_ObjectToTrack = null!;
    public Transform m_HeadBonePoint = null!, m_BasePoint = null!;
    public PoinToTrack m_PointToTrackOnActor = PoinToTrack.HeadBone;
    public Vector3 m_HeadBaseOffset;
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal const string ModOwnedNamePrefix = "VR", ModOwnedQualifiedPrefix = "GloomhavenVR.";
    }
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static int Notes;
        internal static void Note(string area, string text) { Notes++; }
        internal static void Debug(string area, string text) { }
        internal static void Info(string area, string text) { }
    }
    internal static class PerfMonitor
    {
        internal readonly struct Sample : IDisposable { public void Dispose() { } }
        internal static int Preparations;
        internal static Sample Scope(string label) { Preparations++; return new Sample(); }
    }
    internal static class PerfConfig
    {
        internal static float ActorBarPoseCheckInterval = 0;
    }
    internal static partial class ScenarioFigureDetailBudget
    {
        internal static readonly Driver Instance = new();
        private static readonly Driver _driver = Instance;
        internal sealed class ActorRecord
        {
            internal readonly List<ScenarioFigureMeshBank.Record> MeshDetails = new();
        }
        internal sealed partial class Driver
        {
            private readonly List<ActorRecord> _actors = new();
            internal void Own(ScenarioFigureMeshBank.Record record)
            {
                var actor = new ActorRecord(); actor.MeshDetails.Add(record); _actors.Add(actor);
            }
            internal void Reset() => _actors.Clear();
        }
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class FigureBusy
    {
        internal static bool IsIdleClip(string name) => name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    internal static class ActorPropBody
    {
        internal static ActorBehaviour? Attached;
        internal static GameObject? Body = null;
        internal static GameObject? PropFor(ActorBehaviour? actor) => actor != null && actor == Attached ? actor.gameObject : null;
        internal static GameObject? BodyFor(ActorBehaviour? actor) => actor != null && actor == Attached ? Body : null;
    }
    internal static class FigureOverlay
    {
        internal static void CopyBlendShapeWeights(SkinnedMeshRenderer source, SkinnedMeshRenderer copy)
        {
            for (int i = 0; i < source.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
        }
        internal static void MatchCloneWorldScale(Transform target, Transform parent, Transform source) => target.localScale = source.localScale;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static partial class ActorBars
    {
        internal sealed class Adopted
        {
            internal ActorBarPose? Pose;
            internal float AnchorOffsetWU, PoseAnchorY, NextAnchorSample, ScanPhase = 0;
            internal int AnchorSamplesLeft;
            internal bool AttachedPropActor;
            internal ActorBehaviour? Actor;
        }
        private const int AnchorSampleBudget = 8;
        private const float AnchorSampleIntervalSeconds = 0.5f, AnchorResampleTolerance = 0.02f, DepthScanIntervalSeconds = 2f;
        internal static float BarHeightOffsetWU;
        internal static int LegacyMeasures;
        private static float MeasureAnchorOffsetWU(WorldspacePanelUIController controller, bool wantReport, out string report, out bool ignored)
        { LegacyMeasures++; report = "legacy native policy"; ignored = false; return 2.1f; }
        private static string LabelOf(WorldspacePanelUIController controller, ActorBehaviour? actor) => "fixture";
        private static void LogAnchor(string what, string label, float offset, string report) { }
        internal static Adopted Start(WorldspacePanelUIController controller)
        {
            var pose = CapturePose(controller);
            bool known = TryPoseOffset(pose, controller, out float height);
            return new Adopted { Pose = known ? pose : null, AnchorOffsetWU = known ? height : 2.1f,
                PoseAnchorY = TrackY(controller) + (known ? height : 2.1f),
                AnchorSamplesLeft = known ? 0 : 8, NextAnchorSample = 0.25f,
                Actor = ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack),
                AttachedPropActor = GloomhavenVR.Board.FigureGrab.ActorPropBody.PropFor(ActorBehaviour.GetActorBehaviour(controller.m_ObjectToTrack)) != null };
        }
        internal static void Tick(Adopted bar, WorldspacePanelUIController controller, float now) => ResampleAnchor(bar, controller, now);
        internal static Vector3 Position(Adopted bar, WorldspacePanelUIController controller)
        { TryGetTrackPoint(controller, out Vector3 track); return track + Vector3.up * bar.AnchorOffsetWU; }
    }
}
