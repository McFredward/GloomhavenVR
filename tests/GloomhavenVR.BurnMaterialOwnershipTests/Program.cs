using GloomhavenVR.Cards;
using UnityEngine;
using UnityEngine.UI;
static class Program
{
 static int assertions;
 static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
 static FullAbilityCard Face() => new() { Graphics = new[] { new Graphic { material = new Material { Paint=.4f, Burn=.691f } } } };
 static void Main()
 {
  for (int source=0; source<5; source++)
  {
   var face=Face();
   if(source==0) face.cardEffects=new(); // Original borrowed by native dialog, no UI parent or adoption.
   if(source==1) face.Component=new(); // Serialized field not yet initialized, component already present.
   if(source==2) face.Adopted=true;
   if(source==3) face.Owner=new();
   if(source==4) face.Parent=new();
   var material=face.Graphics[0].material;
   for(int frame=0;frame<90;frame++)
   {
    material.Paint=.4f+frame/180f;
    CardHalfTone.Observe(face);
    Check(ReferenceEquals(material,face.Graphics[0].material) && material.Burn==.691f,
      $"Original source {source} must retain its native animated material across scans");
   }
  }
  var clone=Face(); var old=clone.Graphics[0].material; CardHalfTone.Observe(clone);
  Check(!ReferenceEquals(old,clone.Graphics[0].material) && clone.Graphics[0].material.Paint==0,
    "Unowned stripped clones must still receive their initial clean material");
  Check(old.Paint==.4f && old.Burn==.691f,"Clone normalization cannot mutate its original source material");
  var remote=Face(); remote.Held=true;var output=remote.Graphics[0].material;
  for(int frame=0;frame<90;frame++){output.Paint=frame/90f;CardHalfTone.Observe(remote);Check(ReferenceEquals(output,remote.Graphics[0].material),"Owner-driven remote burn output must retain its material");}
  remote.Held=false;CardHalfTone.Observe(remote);Check(remote.Graphics[0].material.Paint==0,"Recovered stripped clones may be normalized again");
  Console.WriteLine($"Burn material ownership: {assertions} runtime assertions passed.");
 }
}
namespace UnityEngine { class Material { internal float Paint, Burn; } }
namespace UnityEngine.UI { class Graphic { internal Material material=new(); internal string name="plate"; } }
class CardEffects { }
class AbilityCardUI { }
class FullAbilityCard {
 internal CardEffects? cardEffects=null,Component=null;
 internal AbilityCardUI? Parent=null,Owner=null;
 internal bool Adopted=false,Held=false;
 internal Graphic[] Graphics=Array.Empty<Graphic>();
 internal T? GetComponent<T>() where T:class => Component as T;
 internal T? GetComponentInParent<T>(bool includeInactive) where T:class => Parent as T;
 internal T[] GetComponentsInChildren<T>(bool includeInactive) where T:class => Graphics.OfType<T>().ToArray();
}
namespace GloomhavenVR.Core { static class VRLog {internal static void Info(string scope,string message){} } }
namespace GloomhavenVR.Cards {
 static class CardArtGuard {internal static bool IsAdopted(FullAbilityCard face)=>face.Adopted;}
 static class CardFace {internal static AbilityCardUI? OwnerOf(FullAbilityCard face)=>face.Owner;}
 partial class CardHalfTone {
 const string Scope="Cards"; const float RestEpsilon=.001f;
 static int s_fxCorrected;static float s_fxWorstBefore;static bool s_fxLogged;
 static bool HasMirroredDim(FullAbilityCard face)=>false;
 static bool NeedsCorrection(FullAbilityCard face)=>false;
 static void Normalize(FullAbilityCard face)=>throw new Exception("No dim normalization in this fixture");
 static bool HasCardFxHold(FullAbilityCard face)=>face.Held;
 static void MaybeCensus(){}
 static void LogErrorOnce(string source,Exception ex)=>throw ex;
 static bool IsCardFxMaterial(Material source)=>true;
 static float RestDeviation(Material source)=>Math.Max(source.Paint,source.Burn);
 static Material RestCopyOf(Material source)=>new();
 static string WorstTermName(Material source)=>"_GreyOut";
 }
}
