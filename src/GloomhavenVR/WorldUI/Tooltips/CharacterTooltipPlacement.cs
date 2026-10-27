using System.Collections;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Build730: original skin/personal-quest hints must clear the live character.
/// They occupy a right-hand annotation lane beside the fixed window; their
/// native content, show/hide authority and controller lifetime remain unchanged.
/// Identity comes from the actual serialized controllers, never object names,
/// translated text or every tooltip in a character-shaped hierarchy.
/// </summary>
internal static class CharacterTooltipPlacement
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SkinTooltip = Field(typeof(UICharacterSkinSelector), "skinTooltip");
    private static readonly FieldInfo? PartyInformation = Field(typeof(UIAdventurePartyAssemblyCharacterDisplay), "information");
    private static readonly FieldInfo? DescriptionTooltip = Field(typeof(UICampaignAssemblyCharacterInformation), "descriptionTooltip");
    private static readonly FieldInfo? QuestConcealTooltip = Field(typeof(UICampaignAssemblyCharacterInformation), "personalQuestTooltip");
    private static readonly FieldInfo? CreatorPerks = Field(typeof(UICharacterCreatorClassRoster), "perksTooltip");
    private static readonly FieldInfo? CreatorInformation = Field(typeof(UICharacterCreatorClassRoster), "information");
    private static readonly FieldInfo? LocalTarget = Field(typeof(UILocalTooltip), "target");
    private static readonly Vector3[] HostCorners = new Vector3[4], TargetCorners = new Vector3[4], HintCorners = new Vector3[4];
    private const float GapPixels = 12f;

    private static FieldInfo? Field(System.Type type, string name)
    {
        FieldInfo? field = type.GetField(name, Fields);
        if (field == null)
            VRLog.Alert("WorldUI", "CHARACTER TOOLTIP FIELD: " + type.Name + "." + name
                + " is unavailable; retaining the ordinary native hint placement.");
        return field;
    }

    private static bool Describes(Component? declared, RectTransform anchor) => declared != null
        && (ReferenceEquals(declared.transform, anchor) || anchor.IsChildOf(declared.transform));

    internal static bool IsSharedHint(RectTransform? anchor)
    {
        if (anchor == null) return false;
        UIAdventurePartyAssemblyCharacterDisplay? party = anchor.GetComponentInParent<UIAdventurePartyAssemblyCharacterDisplay>();
        UICharacterCreatorWindow? creator = anchor.GetComponentInParent<UICharacterCreatorWindow>();
        if (party == null && creator == null) return false;
        UICharacterSkinSelector? skin = anchor.GetComponentInParent<UICharacterSkinSelector>();
        if (skin != null && Describes(SkinTooltip?.GetValue(skin) as Component, anchor)) return true;
        if (party != null && PartyInformation?.GetValue(party) is IList information)
        {
            for (int i = 0; i < information.Count; i++)
            {
                if (information[i] is not UICampaignAssemblyCharacterInformation item) continue;
                if (Describes(DescriptionTooltip?.GetValue(item) as Component, anchor)
                    || Describes(QuestConcealTooltip?.GetValue(item) as Component, anchor)) return true;
            }
        }
        UICharacterCreatorClassRoster? roster = creator != null
            ? anchor.GetComponentInParent<UICharacterCreatorClassRoster>() : null;
        return roster != null && (Describes(CreatorPerks?.GetValue(roster) as Component, anchor)
            || Describes(CreatorInformation?.GetValue(roster) as Component, anchor));
    }

    internal static bool TryLocalHint(Component widget, out RectTransform? anchor)
    {
        anchor = widget is UILocalTooltip ? LocalTarget?.GetValue(widget) as RectTransform : null;
        // Both native campaign variants anchor the original quest explanation to
        // this exact quest-state print. Other local/item/quest-result hints stay
        // on their existing path, including their native position and callbacks.
        return anchor != null && anchor.GetComponent<UIPersonalQuestState>() != null
            && anchor.GetComponent<UIPersonalQuest>() != null
            && anchor.GetComponentInParent<UICampaignAssemblyCharacterInformation>() != null
            && anchor.GetComponentInParent<UIAdventurePartyAssemblyCharacterDisplay>() != null;
    }

    internal static Vector3 RightCenter(RectTransform host, Quaternion rotation, RectTransform? anchor,
        float halfWidth, float halfHeight, float proud)
    {
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
        host.GetWorldCorners(HostCorners);
        Vector3 center = (HostCorners[0] + HostCorners[2]) * .5f;
        float halfHostWidth = Vector3.Distance(HostCorners[0], HostCorners[3]) * .5f;
        float halfHostHeight = Vector3.Distance(HostCorners[0], HostCorners[1]) * .5f;
        float y = halfHostHeight - halfHeight;
        if (anchor != null)
        {
            anchor.GetWorldCorners(TargetCorners);
            Vector3 top = (TargetCorners[1] + TargetCorners[2]) * .5f;
            y = Vector3.Dot(top - center, up) - halfHeight;
        }
        float limit = Mathf.Max(0f, halfHostHeight - halfHeight);
        y = Mathf.Clamp(y, -limit, limit);
        float gap = GapPixels * Mathf.Max(Mathf.Abs(host.lossyScale.x), 1e-6f);
        return center + right * (halfHostWidth + gap + halfWidth) + up * y
            - rotation * Vector3.forward * proud;
    }

    internal static void PlaceLocal(RectTransform hint, RectTransform host, RectTransform? anchor)
    {
        Quaternion rotation = host.rotation;
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
        hint.GetWorldCorners(HintCorners);
        Vector3 center = (HintCorners[0] + HintCorners[2]) * .5f;
        float halfWidth = Mathf.Abs(Vector3.Dot(HintCorners[3] - HintCorners[0], right)) * .5f;
        float halfHeight = Mathf.Abs(Vector3.Dot(HintCorners[1] - HintCorners[0], up)) * .5f;
        Vector3 wanted = RightCenter(host, rotation, anchor, halfWidth, halfHeight, 0f);
        Vector3 delta = wanted - center;
        if (Mathf.Abs(Vector3.Dot(delta, right)) <= 1e-4f && Mathf.Abs(Vector3.Dot(delta, up)) <= 1e-4f) return;
        Vector3 local = hint.parent != null ? hint.parent.InverseTransformVector(delta) : delta;
        Vector3 position = hint.localPosition;
        hint.localPosition = new Vector3(position.x + local.x, position.y + local.y, 0f);
    }
}
