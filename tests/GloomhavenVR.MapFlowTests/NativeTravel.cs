#nullable disable
using System;
// Original native entry and completion methods from AdventureMapUIManager.cs. The
// surrounding APIs below the fixture supply audiovisual effects only, never admission.
internal sealed partial class AdventureMapUIManager
{
	public void OnSelectedMapLocation(MapLocation mapLocation, Action<MapLocation> onConfirmTravelCallback)
	{
		this.onConfirmTravelCallback = onConfirmTravelCallback;
		if (mapLocation == locationToTravel)
		{
			if (!FFSNetwork.IsOnline)
			{
				ConfirmTravel();
			}
			return;
		}
		DeselectCurrentMapLocation();
		this.onConfirmTravelCallback = onConfirmTravelCallback;
		SetLocationToTravel(mapLocation);
		Singleton<MapMarkersManager>.Instance.FadeMarkers();
		HideTravelWarning();
		travelButton.TextLanguageKey = (locationToTravel.IsCompleted() ? "GUI_REPLAY_LOCATION" : "GUI_TRAVEL");
		Singleton<UIGuildmasterHUD>.Instance.EnableHeadquartersOptions(this, enableOptions: false);
		Singleton<UIMapMultiplayerController>.Instance.OnSelectedLocation();
		UIWindowManager.RegisterEscapable(this);
		SetFocused(focused: true);
	}

	public void ConfirmTravel()
	{
		if (!(locationToTravel == null))
		{
			if (CheckTravel())
			{
				AudioControllerUtils.PlaySound(confirmTravelAudiItem);
				onConfirmTravelCallback?.Invoke(locationToTravel);
				HideTravelOption();
			}
			else
			{
				AudioControllerUtils.PlaySound(UIInfoTools.Instance.InvalidOptionAudioItem);
			}
		}
	}
}
