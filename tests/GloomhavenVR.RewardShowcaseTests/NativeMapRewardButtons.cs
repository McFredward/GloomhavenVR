using System;
using System.Collections.Generic;
using UnityEngine.UI;

// Native continuation bodies retained verbatim. Rendering, video transport and window
// transitions are boundaries; this fixture does not establish headset appearance.
public abstract class UIAdventureRewardsManager : Singleton<UIAdventureRewardsManager> { }
public sealed class UIGuildmasterAdventureRewardsManager : UIAdventureRewardsManager
{
    public ExtendedButton closeButton = null!;
    public UIWindow window = null!;
    public UnityEngine.CanvasGroup rewardsPopupCanvasGroup = new() { interactable = true };
    public UIIntroductionRewardsProcess rewardIntroduction = new();
    public readonly List<string> m_CharacterIDsUnlocked = new();
	public void Hide()
	{
		if (m_CharacterIDsUnlocked.Count > 0)
		{
			string text = CharacterClassManager.Find(m_CharacterIDsUnlocked[0]).CharacterModel.ToString();
			m_CharacterIDsUnlocked.RemoveAt(0);
			if (!VideoCamera.s_This.PlayFullscreenVideo("Heroes/" + text, Hide, m_CharacterIDsUnlocked.Count == 0))
			{
				window.Hide();
			}
			else
			{
				InputManager.RequestEnableInput(this, EKeyActionTag.All);
			}
		}
		else
		{
			InputManager.RequestEnableInput(this, EKeyActionTag.All);
			window.Hide();
		}
	}

}
public sealed class UIUnlockLocationFlowManager : Singleton<UIUnlockLocationFlowManager>
{
    public Button continueButton = null!;
    public Action? continueAction;
	public void Continue()
	{
		continueAction?.Invoke();
	}

}
public enum EKeyActionTag { All }
public sealed class CharacterClassManager
{
    public string CharacterModel = string.Empty;
    public static CharacterClassManager Find(string id) => new() { CharacterModel = id };
}
public sealed class VideoCamera
{
    public static VideoCamera s_This = new();
    public readonly List<string> Played = new();
    public Action? Finished;
    public bool Available = true;
    public bool PlayFullscreenVideo(string name, Action onFinish, bool last)
    {
        Played.Add(name);
        if (Available) Finished = onFinish;
        return Available;
    }
    public void Finish()
    {
        var done = Finished;
        Finished = null;
        done?.Invoke();
    }
}
