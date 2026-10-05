using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using GloomhavenVR.Quest.Editor;
public static class RetentionWitness
{
 static int assertions;static void Require(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
 static AddressableAssetSettings settings;static QuestCampaignShaderValidation.Manifest input;static ShaderVariantCollection source;
 static AddressableAssetEntry original;static AddressableAssetGroup owner;
 static void Setup()
 {
  SerializedProperty.Type=SerializedPropertyType.Enum;SerializedProperty.Names=new[]{"StripUnused","StripAll","KeepAll"};SerializedProperty.Value=0;SerializedProperty.Available=SerializedProperty.Writable=true;
  SerializedProperty.FogType=SerializedPropertyType.Enum;SerializedProperty.FogFlagType=SerializedPropertyType.Boolean;SerializedProperty.FogNames=new[]{"Automatic","Custom"};SerializedProperty.FogValue=0;SerializedProperty.FogAvailable=SerializedProperty.FogWritable=SerializedProperty.FogFlagAvailable=true;SerializedProperty.UnwritableFogFlag=null;
  SerializedProperty.FogFlags.Clear();foreach(string n in new[]{"m_FogKeepLinear","m_FogKeepExp","m_FogKeepExp2"})SerializedProperty.FogFlags.Add(n,false);
  Directory.CreateDirectory(Path.GetDirectoryName(QuestCampaignShaderValidation.DefaultManifest));File.WriteAllText(QuestCampaignShaderValidation.DefaultManifest,"fixture");
  AssetDatabase.Objects.Clear();AssetDatabase.Guids.Clear();settings=new AddressableAssetSettings();source=new ShaderVariantCollection();
  input=new QuestCampaignShaderValidation.Manifest{schema=1,scope="campaign-compiler",graphicsApi="Vulkan",requiredShaderCount=3,shaders=Enumerable.Range(1,3).Select(i=>new QuestCampaignShaderValidation.Row{assetPath="Assets/Native/"+i+".shader",guid=new string((char)('0'+i),32),originalName="Original duplicated name",variants=new[]{new QuestCampaignShaderValidation.Variant{passType="ForwardBase",keywords=new[]{"LIGHTPROBE_SH","DIRECTIONAL"}},new QuestCampaignShaderValidation.Variant{passType="ForwardBase",keywords=new[]{"INSTANCING_ON","DIRECTIONAL"}}}}).ToArray()};
  foreach(var row in input.shaders){var shader=new Shader{name=row.originalName};AssetDatabase.Objects.Add(row.assetPath,shader);AssetDatabase.Guids.Add(row.assetPath,row.guid);foreach(var bank in row.variants)source.Variants.Add(new ShaderVariantCollection.ShaderVariant(shader,UnityEngine.Rendering.PassType.ForwardBase,bank.keywords));}
  AssetDatabase.Objects.Add(QuestStartupAddressablesBuild.CampaignShaderCollectionPath,source);AssetDatabase.Guids.Add(QuestStartupAddressablesBuild.CampaignShaderCollectionPath,new string('e',32));JsonUtility.Input=input;
  owner=QuestStartupAddressablesBuild.GetOrCreateOwnedGroup(settings,"Exact native original owner",true);
  original=settings.CreateOrMoveEntry(input.shaders[0].guid,owner,false,false);original.address="unchanged original route";original.labels.Add("always_loaded_base");original.labels.Add("exact original content label");
 }
 static void Apply(){QuestStartupAddressablesBuild.AddCampaignShaderRetention(settings);}
 static void Success()
 {
  var before=source.Variants.ToArray();Apply();var group=settings.FindGroup(QuestStartupAddressablesBuild.CampaignShaderRetentionGroup);
  Require(SerializedProperty.Value==2,"Actual KeepAll enum not requested");Require(group.entries.Count==3,"Only SVC and two missing original Shader roots expected");
  Require(SerializedProperty.FogValue==1&&SerializedProperty.FogFlags.Values.All(v=>v),"Custom retention must preserve every original fog mode");
  Require(original.parentGroup==owner&&owner.entries.Single()==original,"Original public owner moved");Require(original.address=="unchanged original route"&&original.labels.SetEquals(new[]{"always_loaded_base","exact original content label"}),"Original public routes/labels changed");
  foreach(var row in input.shaders.Skip(1)){var root=settings.FindAssetEntry(row.guid);Require(root!=null&&root.parentGroup==group&&root.AssetPath==row.assetPath&&root.address==row.guid&&root.labels.Count==0,"Private Shader root missing or changed");}
  var clone=AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(QuestStartupAddressablesBuild.CampaignAddressableShaderCollectionPath);Require(clone!=null&&!ReferenceEquals(source,clone)&&clone.shaderCount==3&&clone.variantCount==6,"Native SVC was not an independent exact copy");
  Require(source.Variants.SequenceEqual(before)&&source.shaderCount==3&&source.variantCount==6,"Player SVC changed");var schema=group.GetSchema<BundledAssetGroupSchema>();Require(schema.BundleMode==BundledAssetGroupSchema.BundlePackingMode.PackTogether&&schema.IncludeAddressInCatalog&&schema.IncludeGUIDInCatalog&&!schema.IncludeLabelsInCatalog,"Native retention group schema changed");
  Require(settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(QuestStartupAddressablesBuild.CampaignAddressableShaderCollectionPath)).labels.Count==0,"SVC joined runtime preload");
  var roots=group.entries.ToArray();Apply();Require(group.entries.SequenceEqual(roots)&&original.parentGroup==owner,"Warm run moved original/private entries");
 }
 static void Rejected(string name)
 {
  switch(name){
   case "enum-kind":SerializedProperty.Type=SerializedPropertyType.Integer;break;
   case "enum-name":SerializedProperty.Names=new[]{"StripUnused","StripAll","Unknown"};break;
   case "enum-absent":SerializedProperty.Available=false;break;
   case "enum-unwritten":SerializedProperty.Writable=false;break;
   case "fog-enum-kind":SerializedProperty.FogType=SerializedPropertyType.Integer;break;
   case "fog-enum-name":SerializedProperty.FogNames=new[]{"Automatic","Unknown"};break;
   case "fog-enum-absent":SerializedProperty.FogAvailable=false;break;
   case "fog-enum-unwritten":SerializedProperty.FogWritable=false;break;
   case "fog-flag-kind":SerializedProperty.FogFlagType=SerializedPropertyType.Integer;break;
   case "fog-flag-absent":SerializedProperty.FogFlagAvailable=false;break;
   case "fog-linear-unwritten":SerializedProperty.UnwritableFogFlag="m_FogKeepLinear";break;
   case "fog-exp-unwritten":SerializedProperty.UnwritableFogFlag="m_FogKeepExp";break;
   case "fog-exp2-unwritten":SerializedProperty.UnwritableFogFlag="m_FogKeepExp2";break;
   case "scope":input.scope="startup-compiler";break;
   case "api":input.graphicsApi="OpenGLES3";break;
   case "guid":AssetDatabase.Guids[input.shaders[1].assetPath]=new string('b',32);break;
   case "name":AssetDatabase.Objects[input.shaders[1].assetPath].name="changed";break;
   case "missing-alias":source.Variants.RemoveAt(1);break;
   case "extra-alias":source.Variants.Add(new ShaderVariantCollection.ShaderVariant((Shader)AssetDatabase.Objects[input.shaders[0].assetPath],UnityEngine.Rendering.PassType.Normal,new string[0]));break;
   case "pass-type":input.shaders[1].variants[0].passType="Unknown";break;
   case "foreign-group-root":var unknown=settings.CreateOrMoveEntry(new string('a',32),QuestStartupAddressablesBuild.GetOrCreateOwnedGroup(settings,QuestStartupAddressablesBuild.CampaignShaderRetentionGroup,false),false,false);unknown.address="foreign";break;
   case "foreign-clone-owner":Apply();var cg=AssetDatabase.AssetPathToGUID(QuestStartupAddressablesBuild.CampaignAddressableShaderCollectionPath);settings.CreateOrMoveEntry(cg,owner,false,false);break;
   case "private-address":Apply();settings.FindAssetEntry(input.shaders[1].guid).address="drift";break;
   case "private-label":Apply();settings.FindAssetEntry(input.shaders[1].guid).labels.Add("always_loaded_base");break;
   case "unowned-clone":Directory.CreateDirectory(Path.GetDirectoryName(QuestStartupAddressablesBuild.CampaignAddressableShaderCollectionPath));File.WriteAllText(QuestStartupAddressablesBuild.CampaignAddressableShaderCollectionPath,"foreign asset");break;
   default:throw new Exception("Unknown case");
  }
  bool rejected=false;try{Apply();}catch(InvalidDataException){rejected=true;}Require(rejected,"Control was accepted: "+name);
  Require(original.parentGroup==owner&&original.address=="unchanged original route"&&original.labels.SetEquals(new[]{"always_loaded_base","exact original content label"}),"A rejected adaptation changed native public ownership");
 }
 public static int Main(string[] args){Setup();if(args.Length!=1)throw new Exception("Expected case");if(args[0]=="success")Success();else Rejected(args[0]);Console.WriteLine("PASS "+args[0]+" assertions="+assertions);return 0;}
}
