// Observable game seams. DTO/codec, sampler, mirror, bindings and writes below are production files.
using UnityEngine;
using UnityEngine.UI;
using ScenarioRuleLibrary;
internal sealed class ItemCardEffects
{
    internal readonly Transform transform;
    private readonly Image fgFx;
    internal readonly Texture overlayFrameBurn=new(), overlayFrameGhost=new();
    internal bool Running=false;
    internal ItemCardEffects(Transform root,Image overlay){transform=root;fgFx=overlay;}
    internal Image Overlay=>fgFx;
}
internal sealed class ItemCardUI { internal ItemCardEffects cardEffects; internal ItemCardUI(ItemCardEffects fx)=>cardEffects=fx; }
namespace ScenarioRuleLibrary
{
    internal sealed class CItem {internal enum EItemSlotState{Ready,Spent,Consumed} internal EItemSlotState SlotState=EItemSlotState.Ready;}
    internal sealed class Inventory {internal List<CItem> AllItems=new();}
    internal sealed class CPlayerActor {internal int Id;internal Inventory? Inventory=new();}
}
namespace GloomhavenVR.Core
{
    internal static class VRLog {internal static void Warn(string topic,string text)=>throw new InvalidOperationException(text);}
    internal static class PerfMonitor { internal static IDisposable Scope(string text)=>new Empty(); private sealed class Empty:IDisposable{public void Dispose(){}} }
}
namespace GloomhavenVR.Cards
{
    internal static class ItemBurnPlayback {internal static bool Playing(ItemCardEffects? effect)=>effect?.Running??false;}
    internal static class CardsGameApi {internal static List<CItem>? Rewards;internal static List<CItem>? LoseRewardItems()=>Rewards;}
    internal sealed class ItemsPile
    {
        internal CPlayerActor? OwnerActor;
        internal bool ItemSourceAddress(ItemChip chip,out byte seat,out byte count,out byte population){seat=chip.Seat;count=chip.Count;population=chip.Population;return true;}
        internal sealed class ItemChip
        {
            internal static readonly List<ItemChip> Registered=new();
            internal static void CopySmokeChips(List<ItemChip> target){target.Clear();target.AddRange(Registered);}
            internal ItemsPile? Owner;internal CItem? Item;internal ItemCardUI? NativeItemCard;
            internal bool BurnPresentationPending,PendingUse;
            internal byte Seat=0,Count=1,Population=0;
        }
    }
}
namespace GloomhavenVR.Net
{
    internal static class NetProtocol {internal const uint Magic=0x31525647; internal const byte Version=3,MsgItemAppearance=17,MsgItemAppearanceFragments=18,ExtIdItemAppearance=76;}
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] b,ref int p,uint n){BitConverter.TryWriteBytes(b.AsSpan(p,4),n);p+=4;}
        internal static uint ReadU32(byte[] b,ref int p){var n=BitConverter.ToUInt32(b,p);p+=4;return n;}
        internal static void WriteI32(byte[] b,ref int p,int n)=>WriteU32(b,ref p,(uint)n);
        internal static int ReadI32(byte[] b,ref int p)=>(int)ReadU32(b,ref p);
        internal static void WriteF32(byte[] b,ref int p,float n)=>WriteU32(b,ref p,BitConverter.SingleToUInt32Bits(n));
        internal static float ReadF32(byte[] b,ref int p)=>BitConverter.UInt32BitsToSingle(ReadU32(b,ref p));
    }
    internal static class NetFigures {internal static int StableActorId(CPlayerActor? actor)=>actor?.Id??0;}
    internal static class RemoteBoardFocus {internal static readonly Dictionary<int,CPlayerActor> Actors=new();internal static CPlayerActor? ActorById(int id)=>Actors.GetValueOrDefault(id);}
}
