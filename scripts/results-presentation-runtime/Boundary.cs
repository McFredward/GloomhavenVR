// Ports: original results/bootstrap/vote controllers (shared lifecycle boundary),
// hand/pixel bake adapters, network root ordering types, shared container fields.
// Actual production capture, assets/material identity, codec, clone neutralizer,
// atomic assembly and all Unity rendering execute unchanged.
#pragma warning disable CS0649,CS0414
using System;using System.Collections.Generic;using UnityEngine;using TMPro;
public class UIScenarioBattleGoalResult:MonoBehaviour { }
public class UIScenarioAchievement:MonoBehaviour { }
public partial class UINewAdventureResultsManager { public UIScenarioBattleGoalResult battleGoalResultSlot=null!;public UIScenarioAchievement partyAchievementPrefab=null!; }
namespace GloomhavenVR.Hands.Interact { internal sealed class PokeOnlyTarget:MonoBehaviour { } }
namespace GloomhavenVR.Cards { internal static class CardFaceMipBake {internal static Sprite OriginalFor(Sprite s)=>s;internal static Sprite? PresentationFor(Sprite? s)=>s;} }
namespace GloomhavenVR.WorldUI { internal static class PanelMipBake {internal static Texture OriginalFor(Texture t)=>t;} }
namespace GloomhavenVR.Core {internal static class BundleShaders {internal static Shader? Resolve(string n,string s,string a,string b)=>Shader.Find(n);} }
namespace GloomhavenVR.Net {
 internal sealed class RigPose {internal Vector3 Position;internal Quaternion Rotation=Quaternion.identity;}
 internal enum SharedWindowMovePhase:byte {Initial,Held,Released,Automatic}
 internal struct SharedWindowMoveEntry {internal byte Kind;internal SharedWindowMovePhase Phase;internal uint ContentKey,Opening,Claim,Sequence,ParentClaim;internal int OpeningAuthor,ParentAuthor;}
}
namespace GloomhavenVR.Net.TownServices {
 internal sealed class TownServiceFrame {
  internal const int MaxBytes=60000,MaxNodes=256,MaxProperties=254;internal uint Structure,Session;internal float SampleTime;internal TownServiceNode[] Nodes=Array.Empty<TownServiceNode>();
 }
}

internal static class ResultsClock {internal static float Now=10;internal static int Frame=1;}
