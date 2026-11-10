using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Native serialized links and opening states are explicit inputs. No native
// character creation, save, refcount, transition callback or gameplay is run.
public sealed class NewPartyDisplayUI : MonoBehaviour
{
    public static NewPartyDisplayUI? PartyDisplay;
    public Component? AbilityCardsDisplay, EnhancementCardsDisplay, PerkManager,
        CharacterSelector, ItemInventoryDisplay, BattleGoalWindow;
}
public sealed class UICampaignAdventurePartyAssemblyWindow : MonoBehaviour
{
    public AdventureCharacterCreator? CharacterCreator;
}
public sealed class AdventureCharacterCreator : MonoBehaviour
{
    public UICharacterCreatorWindow? CreatorWindow;
}
namespace GloomhavenVR.WorldUI
{
    internal sealed partial class ConvertedPanel
    {
        internal int FitAppliedGeneration;
        internal Vector2 FitContentPadding, NativeDesignFrame;
        internal bool FitFrameDegenerate, FitHeightCapped, FitMeasuredOnce, FitOneShotApplied, FrameDriftLogged;
        internal string FitHeightCapName="creation fixture";
    }
    internal static class SharedWindowSize
    {
        internal static bool IsArmed(ConvertedPanel p)=>false;
        internal static bool Repin(ConvertedPanel p,RectTransform t,out string note){note="";return false;}
    }
    internal static class LoadoutConfirmPark { internal static Transform? HeldControl=>null; }
    internal static class HintOnOwnerComposite { internal static bool IsParkedContent(Transform t)=>false; }
    internal static class TransientFamilies
    {
        internal const int EffectQuadFamily=7;
        internal static readonly string[] Names={"none","card","item","modifier","tooltip","flyout","preview","effect"};
        internal static int OfGraphic(Transform? t,Transform root,Dictionary<Transform,int> memo)=>0;
    }
    internal static class LevelUpComposite
    {
        internal static Component? FittedView=>null;
        internal static Component? InventoryView=>null;
        internal static float ExtraFrameWidth(ConvertedPanel panel)=>0;
    }
    internal static partial class ModalFallback
    {
        internal static int CreationRepositions;
        internal static void OnFixedFitApplied(ConvertedPanel p){CreationRepositions++;}
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapQuestReadyRoster { internal static Transform? HeldRow=>null; }
}
