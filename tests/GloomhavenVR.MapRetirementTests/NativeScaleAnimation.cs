#nullable disable
using System;
using UnityEngine;

[Serializable]
public class LeanTweenGuiAnimationSettingScale : LeanTweenGuiAnimationSetting<Vector3, RectTransform>
{
	protected override LTDescr BuildTweenAction()
	{
		return LeanTween.scale(Target, ToValue, Duration);
	}

	protected override void SetValue(Vector3 value)
	{
		Target.localScale = value;
	}

	public LeanTweenGuiAnimationSettingScale(RectTransform target, float duration, float delay = 0f, LeanTweenType easing = LeanTweenType.notUsed)
		: base(target, duration, delay, easing)
	{
	}
}

public abstract class LeanTweenGuiAnimationSetting<TValue, TTarget>
{
    protected readonly TTarget Target;
    protected TValue ToValue;
    protected float Duration;
    protected LeanTweenGuiAnimationSetting(TTarget target, float duration, float delay, LeanTweenType easing)
    { Target = target; Duration = duration; }
    protected abstract LTDescr BuildTweenAction();
    protected abstract void SetValue(TValue value);
}
public sealed class LTDescr { }
public enum LeanTweenType { notUsed }
public static class LeanTween
{
    public static LTDescr scale(UnityEngine.RectTransform target, UnityEngine.Vector3 value, float duration) => new();
}
public sealed class NativeScaleFrames : LeanTweenGuiAnimationSettingScale
{
    public NativeScaleFrames(UnityEngine.RectTransform target) : base(target, .25f) { }
    public void Frame(UnityEngine.Vector3 value) => SetValue(value);
}
