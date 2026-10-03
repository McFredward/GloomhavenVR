using System;
using UnityEngine;
/// <summary>Typed import of native engine fields; JSON cannot overwrite engine transforms.
/// Every value comes directly from recovered serialized original RectTransform data.</summary>
public static class NativeFixture
{
    [Serializable] private sealed class RectData
    {
        public Vector2 m_AnchorMin,m_AnchorMax,m_AnchoredPosition,m_SizeDelta,m_Pivot;
        public Vector3 m_LocalPosition,m_LocalScale;
        public Quaternion m_LocalRotation;
    }
    public static void ApplyRect(RectTransform target,string json)
    {
        RectData d=JsonUtility.FromJson<RectData>(json);
        target.anchorMin=d.m_AnchorMin;target.anchorMax=d.m_AnchorMax;target.pivot=d.m_Pivot;
        target.sizeDelta=d.m_SizeDelta;target.anchoredPosition=d.m_AnchoredPosition;
        target.localScale=d.m_LocalScale;target.localRotation=d.m_LocalRotation;
    }
}
