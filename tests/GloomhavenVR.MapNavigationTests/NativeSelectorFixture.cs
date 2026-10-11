#nullable disable
using Code.State;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using Script.GUI.SMNavigation.States.PopupStates;
using UnityEngine;

// Update is copied verbatim from the read-only native MapLocationSelector.
// Camera/physics inputs below only expose hit/no-hit; this fixture contains
// the original eligibility decision rather than restating it as a test policy.
internal sealed class NativeSelectorFixture
{
    private MapLocation _mapLocation;
    private readonly RaycastHit[] _results = new RaycastHit[1];
    private readonly Camera _camera = new();
    private readonly object _screenCenterPosition = new();
    private readonly float _maxDistance = 1000f;
    private readonly int _layerMask = 32768;
    private readonly MapLocation _location;
    internal NativeSelectorFixture(MapLocation location) => _location = location;
    internal void RunUpdate(bool hit)
    {
        Physics.Hit = hit;
        _results[0].collider = new Collider { Location = _location };
        Update();
    }

	private void Update()
	{
		if (Singleton<MapFTUEManager>.Instance != null && Singleton<MapFTUEManager>.Instance.HasToShowFTUEOnNoneOrInitialStep)
		{
			return;
		}
		IState currentState = Singleton<UINavigation>.Instance.StateMachine.CurrentState;
		if (!(currentState is LoadoutState) && !(currentState is LocationHoverState) && !(currentState is WorldMapState))
		{
			if (currentState is PersonalQuestCompletedState || currentState is TravelMapState)
			{
				_mapLocation?.OnPointerExit(null);
				_mapLocation = null;
			}
		}
		else if (Physics.RaycastNonAlloc(_camera.ScreenPointToRay(_screenCenterPosition), _results, _maxDistance, _layerMask) > 0)
		{
			if (!(_mapLocation != null) && _results[0].collider.TryGetComponent<MapLocation>(out var component))
			{
				_mapLocation = component;
				_mapLocation.OnPointerEnter(null);
				Singleton<UINavigation>.Instance.StateMachine.Enter(CampaignMapStateTag.LocationHover, new MapLocationStateData(_mapLocation));
			}
		}
		else if (_mapLocation != null)
		{
			_mapLocation.OnPointerExit(null);
			_mapLocation = null;
			Singleton<UINavigation>.Instance.StateMachine.Enter(CampaignMapStateTag.WorldMap);
		}
	}
}
internal sealed class MapFTUEManager
{
    internal bool HasToShowFTUEOnNoneOrInitialStep => false;
}
namespace UnityEngine
{
    internal struct RaycastHit { internal Collider collider; }
    internal sealed class Collider
    {
        internal MapLocation Location;
        internal bool TryGetComponent<T>(out T component) where T : class
        {
            component = Location as T;
            return component != null;
        }
    }
    internal sealed class Camera { internal object ScreenPointToRay(object point) => point; }
    internal static class Physics
    {
        internal static bool Hit;
        internal static int RaycastNonAlloc(object ray, RaycastHit[] results, float distance, int layers) => Hit ? 1 : 0;
    }
}
