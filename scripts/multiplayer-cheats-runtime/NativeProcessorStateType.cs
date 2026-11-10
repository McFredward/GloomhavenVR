namespace FFSNet;

internal enum ActionProcessorStateType
{
	NONE,
	Halted,
	ProcessFreely,
	ProcessOneAndHalt,
	ProcessOneAndSwitchBack,
	SwitchBackToSavedState
}
