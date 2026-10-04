using System.Collections.Generic;
using UnityEngine;

public sealed class ActorBehaviour : MonoBehaviour
{
    public GameObject? m_RootGameObject, m_AnimatedGameObject, m_Hilight;
    public ScenarioRuleLibrary.CActor Actor = new TestActor();
}
public sealed class TestActor : ScenarioRuleLibrary.CActor { }
public class TestClass { public string ID = "NativeSpittingDrake", DefaultModel = "NativeSpittingDrake"; }
namespace ScenarioRuleLibrary
{
    public class CActor { public TestClass Class = new(); }
    public sealed class CharacterYMLData { public string CustomCharacterConfig = ""; }
    public sealed class CCharacterClass { public CharacterYMLData CharacterYML = new(); }
    public sealed class CPlayerActor : CActor { public CCharacterClass CharacterClass = new(); }
    public sealed class MonsterYMLData { public string CustomConfig = ""; }
    public sealed class CMonsterClass { public string DefaultModel = ""; public MonsterYMLData MonsterYML = new(); }
    public class CEnemyActor : CActor { public CMonsterClass MonsterClass = new(); }
    public sealed class PropHealthDetails { public string ActorSpriteName = ""; }
    public sealed class AttachedProp { public PropHealthDetails PropHealthDetails = new(); }
    public sealed class CObjectActor : CEnemyActor { public bool IsAttachedToProp; public AttachedProp AttachedProp = new(); }
    public sealed class HeroSummonClass { public MonsterYMLData SummonYML = new(); }
    public sealed class CHeroSummonActor : CActor { public string Prefab = ""; public string GetPrefabName() => Prefab; public HeroSummonClass HeroSummonClass = new(); }
}
public sealed class MonsterConfigYMLData { public string ID = "", Portrait = ""; }
public sealed class TestSRLYML { public List<MonsterConfigYMLData> MonsterConfigs = new(); }
public static class ScenarioRuleClient { public static TestSRLYML SRLYML = new(); }
public sealed class CharacterConfigUI { public Sprite? scenarioPreviewInfoPortrait; }
public sealed class UIInfoTools
{
    public static UIInfoTools? Instance;
    public readonly Dictionary<(string,string?),SpriteMemoryManagement.ReferenceToSprite> Portraits = new();
    public readonly Dictionary<(string,string),CharacterConfigUI> Characters = new();
    public readonly Dictionary<(string,string),Sprite> HeroPortraits = new();
    public readonly List<(string,string?)> Requested = new();
    public SpriteMemoryManagement.ReferenceToSprite GetActorPortraitRef(string actorModel,string? customPortrait=null)
    { Requested.Add((actorModel,customPortrait)); return Portraits[(actorModel,customPortrait)]; }
    public CharacterConfigUI GetCharacterConfigUI(string character,bool useDefault=true,string customConfigID="")
    { if(!useDefault) throw new System.InvalidOperationException("native uses default fallback"); return Characters[(character,customConfigID)]; }
    public Sprite GetCharacterHeroPortrait(string character,string custom="") => HeroPortraits[(character,custom)];
}
namespace SpriteMemoryManagement
{
    public sealed class ReferenceToSprite { public Sprite? Sprite; public bool Pending; }
}
// Exact native field shapes used by the production wall-ownership predicates. This
// overlay fixture has no action pool; wall-read-facts exercises pool lifecycle causally.
public sealed class WaypointHolder : MonoBehaviour
{
    public sealed class WaypointPrefab { public GameObject? Prefab; }
    public List<WaypointPrefab>? m_Prefabs;
}
public sealed class ObjectPool
{
    public static ObjectPool? instance;
    private readonly Dictionary<GameObject,GameObject> spawnedObjects = new();
    private readonly Dictionary<GameObject,List<GameObject>> pooledObjects = new();
    public void Clear() { spawnedObjects.Clear(); pooledObjects.Clear(); }
}
public sealed class GlobalSettings
{
    public static GlobalSettings? Instance;
    public sealed class GlobalParticleEffects
    { public GameObject? DefaultHealEffect, DefaultPositiveCondition, DefaultNegativeCondition, DefaultCharacterReveal, DefaultCharacterSwap; }
    public sealed class MagicEffects { public GameObject? RetaliateHit, RetaliateTarget, WoundDamage; }
    public sealed class ActiveBonusBuffTargetEffects
    {
        public GameObject? AttackBuffTargetEffect, ShieldActiveBonusTargetEffect, RetaliateActiveBonusTargetEffect, GainShield, GainRetaliate, GainDisarm, GainImmobilize, GainPoison, GainStun, GainWound, GainBless, GainCurse, GainSleep, GainStrengthen, GainMuddle, GainInvisibility, GainAddTarget, GainAddHeal, GainAddRange, GainAttackersGainDisadvantage, GainAttackActiveBonus, GainDefault;
    }
    public GlobalParticleEffects? m_GlobalParticles;
    public MagicEffects? m_MagicEffects;
    public ActiveBonusBuffTargetEffects? m_ActiveBonusBuffTargetEffects;
}
public sealed class HexSelect_Control : MonoBehaviour { public MeshRenderer? HexProjector; }
public sealed class HexSelectControlParticles : MonoBehaviour
{ public ParticleSystem[]? ParticleBits, ParticleHover; }
public sealed class NativeCallbackProbe : MonoBehaviour
{
    public static int Awakes, Events;
    private void Awake() => Awakes++;
    public void NativeEvent() => Events++;
}
public sealed class NativeStateProbe : StateMachineBehaviour
{
    public static int Enters;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo state, int layer) => Enters++;
}
namespace GloomhavenVR.Cards
{
    internal static class PlayTray { internal static Shader? Shader; internal static Shader? OverlayShader() => Shader; }
    internal static class CardFaceMipBake
    {
        internal static readonly HashSet<Sprite> Sprites = new();
        internal static readonly HashSet<Texture2D> Textures = new();
        internal static Sprite? ReplacementFor(Sprite sprite) { Sprites.Add(sprite); return sprite; }
        internal static Texture2D? BakedTextureFor(Texture2D texture) { Textures.Add(texture); return texture; }
    }
    internal static class ScenarioCardPreparation
    {
        internal static readonly HashSet<SpriteMemoryManagement.ReferenceToSprite> References = new();
        internal static void IncludeOriginalReference(SpriteMemoryManagement.ReferenceToSprite reference) => References.Add(reference);
    }
    internal static class CardArtPin
    {
        internal static readonly HashSet<SpriteMemoryManagement.ReferenceToSprite> Pins = new();
        internal static void PinReference(SpriteMemoryManagement.ReferenceToSprite reference) => Pins.Add(reference);
        internal static Sprite? PreparedSprite(SpriteMemoryManagement.ReferenceToSprite reference,out bool pending)
        { pending=reference.Pending;return pending?null:reference.Sprite; }
    }
}
namespace GloomhavenVR.Core
{
    // Profiling is an inert external boundary; actual overlay renderer behavior runs below.
    internal static class PerfMonitor
    {
        internal readonly struct Marker : System.IDisposable { public void Dispose() { } }
        internal static Marker Scope(string label) => new();
    }
    internal static class ScenarioEnvironmentBudget { internal static void BeforeNativeRendererWrite(Renderer renderer) { } }
    internal static class VRLayers { internal const int ModLayer = 26; internal const string ModOwnedNamePrefix = "VR", ModOwnedQualifiedPrefix = "GloomhavenVR."; }
    internal static class VRLog { internal static bool WantsDebug => true; internal static void Warn(string area, string text) { } internal static void Note(string area, string text) { } internal static void Debug(string area, string text) { } internal static void Alert(string area, string text) { } }
}
public sealed class ActorStatPanel : MonoBehaviour
{ public static ActorStatPanel? Instance => Singleton<ActorStatPanel>.Instance; public static int Shows; public void Show() => Shows++; }
public sealed class EnemyCurrentTurnStatPanel : MonoBehaviour
{ public static EnemyCurrentTurnStatPanel? Instance => Singleton<EnemyCurrentTurnStatPanel>.Instance; public static int Shows; public void Show() => Shows++; }
internal static class Singleton<T> where T : class
{
    internal static T? Instance;
    internal static bool IsInitialized => Instance != null;
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class Entry<T> { internal Entry(T value) { Value=value; } internal T Value; }
    internal static class WorldUIConfig { internal static Entry<bool> PanelMipBake = new(true); }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera => null; } }
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldFigures { internal static readonly HashSet<ActorBehaviour> Actors = new(); internal static bool Owns(ActorBehaviour actor) => Actors.Contains(actor); }
    internal static class NetHeldFigures { internal static readonly HashSet<ActorBehaviour> Actors = new(); internal static bool Owns(ActorBehaviour actor) => Actors.Contains(actor); }
}

namespace GloomhavenVR.Core
{
    internal static class PerfConfig { internal static bool SharedWallReadCacheOn => true; }
    internal static partial class WallSegmentFade
    {
        internal readonly struct OwnershipReading
        {
            internal readonly bool Mod, WallFade, Exempt;
            internal readonly int Folded, ExemptRows, RowId, WaterCount, WallCount;
            internal readonly ulong Scene, Narrow, Figures;
            internal OwnershipReading(bool mod, bool wallFade, bool exempt, int folded, int exemptRows,
                int rowId, int waterCount, int wallCount, ulong scene, ulong narrow, ulong figures)
            { Mod=mod; WallFade=wallFade; Exempt=exempt; Folded=folded; ExemptRows=exemptRows;
              RowId=rowId; WaterCount=waterCount; WallCount=wallCount; Scene=scene; Narrow=narrow; Figures=figures; }
        }
        internal sealed class OwnershipProbe
        {
            private readonly FadeDriver driver=new();
            internal OwnershipReading Survey(Renderer? renderer) => driver.Survey(renderer);
            internal static bool LiveOwned(Renderer renderer) => FadeDriver.LiveOwned(renderer);
        }
        private sealed partial class FadeDriver
        {
            private readonly Dictionary<Shader,bool> _shaderVerdict=new(),_shaderFoliageVerdict=new(),_shaderWaterVerdict=new();
            private readonly List<Material> _matScratch=new();
            private readonly Renderer?[] _snapshot=new Renderer?[1];
            private readonly RendererFact[] _facts=new RendererFact[1];
            private readonly List<int> _factWallFade=new(),_factWater=new();
            private bool _classifyCold=true;
            private ulong[] _sigRow=System.Array.Empty<ulong>();
            private int[] _sigRowId=System.Array.Empty<int>();
            private byte[] _sigRowFlags=System.Array.Empty<byte>();
            private int _sceneExemptRows,_sceneFoldedRows;
            private ulong _sceneFactSigSum,_sceneFactSigXor,_narrowSceneSigSum,_narrowSceneSigXor,_figureSetSigSum,_figureSetSigXor;
            private static readonly Dictionary<Transform,bool> FigureAncestryMemo=new(),GameLogicAncestryMemo=new(),WallGeneratorAncestryMemo=new();
            private static readonly Dictionary<Transform,Transform?> FigureRootMemo=new();
            private static bool _figureMemoActive,_figureRootMemoActive;
            private static bool IsMountableRendererType(Renderer renderer) => renderer is MeshRenderer;
            private static bool IsFigureOrActorRenderer(Renderer renderer) => renderer.GetComponentInParent<ActorBehaviour>(true)!=null;
            private static bool IsWallGeneratedDressing(Renderer renderer) => false;
            internal static bool LiveOwned(Renderer renderer) => IsModObject(renderer);
            internal OwnershipReading Survey(Renderer? renderer)
            {
                _snapshot[0]=renderer;_factWallFade.Clear();_factWater.Clear();
                _sceneExemptRows=_sceneFoldedRows=0;
                _sceneFactSigSum=_sceneFactSigXor=_narrowSceneSigSum=_narrowSceneSigXor=_figureSetSigSum=_figureSetSigXor=0;
                BeginFigureMemo();
                try
                {
                    if(!_figureMemoActive) throw new System.InvalidOperationException("Native memo scope must be active");
                    ClassifySlice(0,1);
                }
                finally { EndFigureMemo(); }
                _classifyCold=false;
                return new OwnershipReading(_facts[0].Mod,_facts[0].WallFadeShader,SceneRowWasExemptWhenAlive(0),
                    _sceneFoldedRows,_sceneExemptRows,_sigRowId[0],_factWater.Count,_factWallFade.Count,
                    _sceneFactSigSum,_narrowSceneSigSum,_figureSetSigSum);
            }
        }
    }
}
