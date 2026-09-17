using System.Collections;
namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch:Attribute {public HarmonyPatch(Type type,string name){}} }
namespace Chronos { public class Clock {public float time,deltaTime;} public class Timekeeper {public static Timekeeper instance=new();public Clock m_GlobalClock=new();} }
namespace UnityEngine {
    public static class Time {public static float unscaledDeltaTime=.05f;}
    public class WaitForEndOfFrame {}
    public class Texture2D {}
    public struct Color {public Color(float a,float b,float c,float d){}}
    public struct Vector2 {public Vector2(float a,float b){}}
    public struct Vector3 {public static Vector3 one=>new();public static Vector3 operator*(Vector3 a,float b)=>a;public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>b;}
    public struct Quaternion {public static Quaternion identity=>new();public static Quaternion Euler(float a,float b,float c)=>new();public static Quaternion operator*(Quaternion a,Quaternion b)=>a;public static Quaternion Slerp(Quaternion a,Quaternion b,float t)=>b;}
    public class GameObject {public void SetActive(bool value){}}
    public class Transform {public Quaternion localRotation;public Vector3 localScale;public void SetParent(Transform parent,bool worldPositionStays){}}
    public class Image {public GameObject gameObject=new();public Material material=new();public Color color;}
    public class Material {readonly Dictionary<object,float> values=new();public float GetFloat(object key)=>values.TryGetValue(key,out var value)?value:0;public void SetFloat(object key,float value)=>values[key]=value;public void SetColor(int key,Color c){}public void SetTexture(int key,Texture2D t){}public void SetTextureScale(int key,Vector2 v){}}
    public static class Mathf {public const float PI=MathF.PI;public static float Min(float a,float b)=>MathF.Min(a,b);public static float Exp(float x)=>MathF.Exp(x);public static float Sin(float x)=>MathF.Sin(x);public static float Clamp01(float x)=>Clamp(x,0,1);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;}
}
namespace GloomhavenVR.Cards {
    using UnityEngine;
    internal sealed class ItemCardUI {
        internal ItemCardEffects cardEffects=new();
        internal ScenarioRuleLibrary.CItem item=new();
        internal int StateReads;
        internal ItemCardUI(){cardEffects.Owner=this;}
        internal void UpdateState()=>StateReads++;
        internal void OnReturnedToPool(){ItemTestHooks.Pool(this);cardEffects.RestoreCard();}
    }
    internal static class ItemTestHooks {
        internal static bool Allow(Type patch,ItemCardEffects effect)=>(bool)patch.GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{effect})!;
        internal static void Pool(ItemCardUI owner)=>typeof(ItemBurnPlayback.ReturnedToPool_Retire).GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{owner});
    }
    internal sealed class Box {internal bool enabled=true;}
    internal sealed class PlayTray {internal static PlayTray? Current=new();internal Transform? Root=new();internal bool Slot;internal void SetItemUseSlotVisible(bool value)=>Slot=value;}
    internal sealed partial class ItemsPile {
        private ItemChip? _finishingUseChip,_keptClip;
        private int _keptClipIndex=-1;
        private readonly List<ItemChip> _chips=new();
        private Transform? _anchor=new();
        private bool _useSlotShownLogged=false;
        internal bool IsOpen=true;
        internal int Count=>_chips.Count;
        internal bool Pending=>_finishingUseChip!=null;
        internal bool ShownLog=>_useSlotShownLogged;
        private void ForgetSweepWinner(ItemChip chip){}
        internal void Add(ItemChip chip)=>_chips.Add(chip);
        internal void Begin(ItemChip chip,bool spent=false)=>BeginUsedChipPresentation(chip,!spent,spent,new());
        internal sealed partial class ItemChip {
            internal ItemCardUI? _cardUI=new();
            internal object? Holder=>null;
            internal bool PendingUse;
            internal Transform transform=new();
            private bool _useFxActive,_useFxConsumed,_useFxSpent,_fingerPopped,_laserPopped,_recessPopped;
            private float _useFxTime,_homeScale=1;
            private Vector3 _useFxCollapseWorld;
            private Quaternion _useFxBaseRot;
            private const float UseFxSeconds=.55f;
            private Action? _useFxCompleted;
            private Box? _box=new();
            internal int Collapses;
            internal bool InputBlocked=>_box!=null&&!_box.enabled;
            internal bool Active=>_useFxActive;
            internal bool Hover=>_fingerPopped||_laserPopped||_recessPopped;
            private void BeginCollapse(Vector3 world)=>Collapses++;
            internal void Tick(){if(_useFxActive)TickUseFx();}
        }
    }
}

namespace GloomhavenVR.Net { internal static class ItemAppearanceSampler { internal static int Completions; internal static bool CapturedBeforeRetirement; internal static void RetainCompletion(Cards.ItemsPile.ItemChip chip) { Completions++; CapturedBeforeRetirement = chip.PendingUse && chip.Collapses == 0; } } }

namespace ScenarioRuleLibrary { internal sealed class CItem {internal enum EItemSlotState {None,Consumed,Spent,Ready} internal EItemSlotState SlotState=EItemSlotState.Consumed;} }
