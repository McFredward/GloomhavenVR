// Observable engine/game boundary for the actual production physical-flow partial.
// No renderer, headset, native rules simulation or networking is claimed by this fixture.
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public static void Destroy(object value) { } }
    public class MonoBehaviour : Object { }
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => new(0,0,0);
        public static Vector3 one => new(1,1,1);
        public static Vector3 up => new(0,1,0);
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public record struct Quaternion { public static Quaternion identity => new(); public static Quaternion Inverse(Quaternion value)=>value; public static Quaternion operator *(Quaternion a,Quaternion b)=>a; }
    public record struct Color(float r,float g,float b,float a);
    public static class Mathf { public static float Abs(float value)=>Math.Abs(value); public static float Max(float a,float b)=>Math.Max(a,b); }
    public enum PrimitiveType { Quad }
    public class Transform
    {
        public readonly GameObject gameObject=new(false);
        public Vector3 position,localPosition,localScale=Vector3.one,lossyScale=Vector3.one;
        public Quaternion localRotation,rotation;
        public Vector3 InverseTransformPoint(Vector3 world)=>world-position;
        public Transform? parent;
        public void SetParent(Transform value,bool worldPositionStays)=>parent=value;
    }
    public class GameObject
    {
        public bool activeSelf=true;
        public bool activeInHierarchy=>activeSelf&&(transform?.parent?.gameObject.activeInHierarchy??true);
        public string name="";
        public readonly Transform transform;
        public GameObject(bool hasTransform=true) { transform=hasTransform?new():null!; }
        private readonly MeshRenderer renderer=new();
        public static GameObject CreatePrimitive(PrimitiveType type)=>new();
        public void SetActive(bool value)=>activeSelf=value;
        public T GetComponent<T>() where T:class,new()=>renderer is T t?t:new();
    }
    public class Collider { }
    public class MeshRenderer { public Material? sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class Material { public Color color; public Material(Shader shader) { } }
    public class Shader { public static Shader Find(string name)=>new(); }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace ScenarioRuleLibrary
{
    public class CBaseCard { }
    public class CAbilityCard:CBaseCard { public readonly List<CActiveBonus> ActiveBonuses=new(); }
    public class CActiveBonus { public enum EActiveBonusDurationType { Summon,Persistent } public EActiveBonusDurationType Duration; }
    public class CCharacterClass { public readonly List<CBaseCard> ActivatedCards=new(); public readonly List<CAbilityCard> RoundAbilityCards=new(),ExtraTurnCards=new(),DiscardedAbilityCards=new(),LostAbilityCards=new(),PermanentlyLostAbilityCards=new(),HandAbilityCards=new(); }
    public class CPlayerActor { public int Id=1; public readonly CCharacterClass CharacterClass=new(); public List<CBaseCard> Cards=>CharacterClass.ActivatedCards; }
}
public class AbilityCardUI { public CAbilityCard AbilityCard=new(); public CPlayerActor? PlayerActor; }
public class CardsHandUI { public CPlayerActor PlayerActor=new(); }
namespace GloomhavenVR.Hands
{
    public enum HapticPreset { HoverTick }
    public class Grabber { public object? Held; }
    public class VRHand
    {
        public bool HasPose=true,TriggerUp=true,GripUp;
        public readonly Grabber Grabber=new();
        public int Haptics;
        public void SendHaptic(HapticPreset preset)=>Haptics++;
    }
    public static class VRHands { public static VRHand? Left,Right; }
}
namespace GloomhavenVR.Core
{
    public static class Loc { public static string Mod(string key)=>key; }
    public static class VRLog { public static void Info(string scope,string text) { } public static void Warn(string scope,string text) { } public static void Note(string scope,string text) { } }
    public static class VRLayers { public static void Apply(GameObject value) { } }
}
namespace GloomhavenVR.WorldUI
{
    internal class SoftCueReveal { internal void Show() { } internal void Hide() { } }
}
namespace GloomhavenVR.Net
{
    internal static class NetFigures { internal static int StableActorId(CPlayerActor? actor)=>actor?.Id??0; }
}
namespace GloomhavenVR.Cards
{
    internal static class CardsConfig
    {
        internal sealed class Entry<T>(T value) { internal T Value=value; }
        internal static readonly Entry<float> CardWidth=new(.0635f);
        internal static float CardHeight=>CardWidth.Value*(88f/63.5f);
        internal static readonly Entry<string> CardPlaceSound=new("place");
    }
    internal static class ActiveCardSet
    {
        internal static List<CBaseCard> ActivatedCards(CPlayerActor actor)=>actor.Cards;
        internal static bool IsActive(CPlayerActor actor,CAbilityCard? card)=>card!=null&&actor.Cards.Contains(card);
    }
    internal sealed class VRCard
    {
        internal AbilityCardUI GameCard=new();
        internal readonly GameObject gameObject=new();
        internal Transform transform=>gameObject.transform;
        internal VRHand? Holder;
        internal bool IsHeld=>Holder!=null;
        internal int Homes;
        internal float LastScale;
        internal void SetLaserHover(bool value) { }
        internal void ResetColliderRegion() { }
        internal void SetHome(Transform parent,Vector3 position,Quaternion rotation,float scale,bool instant=false)
        { transform.parent=parent;transform.localPosition=position;LastScale=scale;Homes++; }
        internal bool Grabbable=true,IsFlying,IsVanishing=false;
        internal string name=>"card";
        internal object? FullCard=>null;
        internal const float FlyArcHeightFraction=.2f;
        internal int Flights;
        internal Vector3 FlightFrom,FlightTo;
        internal Action? Land;
        internal void FlyToPile(Vector3 to,float width,float seconds,Vector3 up,Action done,float minArc) {IsFlying=true;Flights++;FlightFrom=transform.position;FlightTo=to;Land=done;}
    }
    internal sealed class ActivePileViewer
    {
        internal readonly List<VRCard> Cards=new();
        internal VRCard? StagedCard;
        internal int Returns;
        internal bool Contains(VRCard card)=>Cards.Contains(card);
        internal void Add(VRCard card) { Returns++; if(!card.IsHeld)card.SetHome(Grid,Vector3.zero,Quaternion.identity,.82f); }
        internal readonly Transform Grid=new();
    }
    internal sealed partial class PlayTray
    {
        internal bool IsVisible=true;
        private readonly Transform? _itemUseSlot=new();
        internal readonly Transform Root=new();
        internal int RecessSeatOfCard(VRCard card)=>-1;
        internal int SlotOf(VRCard card)=>-1;
        internal Transform? ItemUseSlotTransform=>_itemUseSlot;
        private bool _activeDismissOwnsBerth;
        private bool _itemUseSlotWanted,_itemUseActive;
        private string? _itemUseAreaLabel,_itemUseConfirmLabel;
        private Action? _itemUseConfirmAction;
        private GloomhavenVR.WorldUI.SoftCueReveal? _itemUseReveal=null;
        private readonly Cap _itemUseConfirm=new();
        internal bool Shown=>_itemUseSlotWanted;
        internal bool ConfirmShown=>_itemUseConfirm.Visible;
        internal string? Caption;
        internal string? ConfirmLabel=>_itemUseConfirmLabel;
        internal Action? ConfirmAction=>_itemUseConfirmAction;
        private void ApplyItemUseCaption()=>Caption=_itemUseAreaLabel;
        internal Action? CapClick;
        private void RebuildAttachedControls() { CapClick=BindItemUseConfirmation(_itemUseConfirmAction); }
        private class Cap {internal bool Visible; internal void SetVisible(bool visible)=>Visible=visible;}
        internal static void AdoptFurniture(GameObject value) { }
    }
    internal class ItemsPile
    {
        internal static ItemsPile? RecessOwner;
        internal bool OwnsUseBerth;
        internal class ItemChip { }
    }
    internal static class CardsGameApi
    {
        internal static bool Allowed=true;
        internal static int Requests,Accepted,Cancelled;
        internal static bool Deferred,Reject;
        internal static int Destination;
        internal static bool ControlsActor(CPlayerActor? actor)=>actor!=null;
        internal static string ActorLabel(CPlayerActor actor)=>"actor";
        internal static string CardName(AbilityCardUI card)=>"card";
        internal static void Commit(CPlayerActor actor,CAbilityCard card) {actor.Cards.Remove(card); if(Destination==0)actor.CharacterClass.DiscardedAbilityCards.Add(card); else if(Destination==1)actor.CharacterClass.LostAbilityCards.Add(card); else actor.CharacterClass.PermanentlyLostAbilityCards.Add(card);}
        internal static bool CanDismissActiveCard(CardsHandUI hand,AbilityCardUI? card)=>Allowed&&card!=null&&hand.PlayerActor.Cards.Contains(card.AbilityCard);
        internal static ActiveCardDismissal? RequestDismissActiveCard(CardsHandUI hand,AbilityCardUI? card)
        { Requests++;return CanDismissActiveCard(hand,card)?new(hand,card!):null; }
        internal sealed class ActiveCardDismissal(CardsHandUI hand,AbilityCardUI card)
        {
            internal bool Pending=true;
            internal bool DismissesSummon=>card.AbilityCard.ActiveBonuses.Any(x=>x.Duration==CActiveBonus.EActiveBonusDurationType.Summon);
            internal bool CanConfirm=>Pending&&CanDismissActiveCard(hand,card);
            internal bool Confirm() { if(!CanConfirm)return false;if(Reject)return false;Pending=false;Accepted++;if(!Deferred)Commit(hand.PlayerActor,card.AbilityCard);return true; }
            internal void Cancel() { if(Pending){Pending=false;Cancelled++;} }
        }
    }
    internal sealed partial class CardsDriver
    {
        private static CardsDriver? Instance;
        private readonly PlayTray _tray=new();
        private readonly ActivePileViewer _active=new();
        private bool _fakeActive,_modalInputBlocked;
        internal CardsHandUI? Presented=new();
        internal int Rebuilds;
        internal static int Sounds;
        internal static bool NativeSceneLoadInProgress;
        private CardsHandUI? CurrentHand()=>Presented;
        private CardsHandUI? PresentedHandForCardLayout(CardsHandUI? hand)=>Presented;
        private void RequestRebuild()=>Rebuilds++;
        private static void PlayCardSound(string sound,Transform transform)=>Sounds++;
        internal CardsDriver() { Net.NetCardFx.Events.Clear();Instance=this;ItemsPile.RecessOwner=new();VRHands.Left=VRHands.Right=null; }
        internal PlayTray Tray=>_tray;
        internal ActivePileViewer Active=>_active;
        internal GameObject? Ghost=>_dismissGhost;
        internal void Tick()=>TickActiveDismiss(Presented);
        internal bool Drop(VRCard card,VRHand hand)=>TryStageActiveDismissCard(card,hand);
        internal void Grab(VRCard card,VRHand hand) {card.Holder=hand;TakeBackActiveDismissCard(card);}
        internal void Reset()=>ResetActiveDismiss();
        internal void Block(bool value)=>_modalInputBlocked=value;
        internal void Fake(bool value)=>_fakeActive=value;
        internal VRCard Card(bool summon=false)
        {
            var card=new VRCard();
            if(summon)card.GameCard.AbilityCard.ActiveBonuses.Add(new(){Duration=CActiveBonus.EActiveBonusDurationType.Summon});
            card.GameCard.PlayerActor=Presented!.PlayerActor;Presented.PlayerActor.Cards.Add(card.GameCard.AbilityCard);_active.Cards.Add(card);_factory.All.Add(card);return card;
        }
    }
}
