using UnityEngine;

namespace GloomhavenVR.WorldUI
{
    // Only Station.RefreshEnvironment/SetGrounding are selected from production.
    // Actual native bundle geometry, placement and grounding execute. Decoration,
    // skinning and the independent light owner are observed lifecycle boundaries;
    // original actor/transaction controllers are never run by this fixture.
    internal sealed class StationAuxiliary746 { internal void Tick() { } }
    internal sealed class StationLight746
    {
        internal int Refreshes;
        internal bool LocalEnvironment;
        internal void Refresh(Transform root, bool localEnvironment = true)
        { Refreshes++; LocalEnvironment = localEnvironment; }
    }
    internal sealed partial class TownServiceStation
    {
#pragma warning disable CS0649
        private readonly GameObject _root;
        private readonly byte _service;
        private readonly TownServiceGrounding _grounding;
        internal readonly StationLight746 _lighting = new();
        private readonly StationAuxiliary746 _decor = new(), _skinning = new();
        private Transform? _room;
        private Vector3 _center;
        private float _scale, _layoutYaw, _lightScale;
        private bool _placed, _authorPose;
#pragma warning restore CS0649
        internal Transform Root => _root.transform;
        internal float ActorFloorOffset { get; private set; }
        internal float FurnitureBottom { get; private set; }
        internal TownServiceStation(GameObject root, byte service, Vector3 center, float scale)
        { _root=root;_service=service;_center=center;_scale=scale;_grounding=new TownServiceGrounding(root.transform); }
        internal void Dispose() { _grounding.Dispose();Object.DestroyImmediate(_root); }
    }
}
