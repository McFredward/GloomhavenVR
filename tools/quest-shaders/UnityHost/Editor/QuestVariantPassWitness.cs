#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.Quest.Editor;

public static class QuestVariantPassWitness
{
    [Serializable] public sealed class PassRecord
    {
        public string guid, name, expectedType, lightMode;
        public int subshader, pass, activeSubshader, actualPassCount, aliases;
        public string[] acceptedTypes, importedEntryTypes;
        public bool expectedAccepted;
    }
    [Serializable] public sealed class Output
    {
        public string unityVersion;
        public int shaders, passes, aliases;
        public int collectionShaders, collectionVariants, mismatchNegativeControls;
        public bool completeRetention, unchangedShaderSourcesAndMetas;
        public PassRecord[] records, controls;
    }
    public static void Run()
    {
        if (Application.unityVersion != "2021.3.5f1") throw new InvalidOperationException("Wrong importer version.");
        string manifest = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST");
        var input = JsonUtility.FromJson<QuestCampaignShaderValidation.Manifest>(File.ReadAllText(manifest));
        var rows = new List<PassRecord>();
        foreach (var row in input.shaders)
        {
            if (rows.Count < 2 || row.originalName == "KriptoFX/RFX4/Particle") Debug.Log("SVC witness source " + row.guid + " " + row.originalName);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(row.assetPath);
            if (shader == null || shader.name != row.originalName || AssetDatabase.AssetPathToGUID(row.assetPath) != row.guid)
                throw new InvalidOperationException("Native source identity changed.");
            foreach (var group in row.variants.GroupBy(v => v.subshader + "/" + v.pass + "/" + v.passType))
            {
                var bank = group.First();
                var record = Probe(shader, bank.subshader, bank.pass, bank.passType, bank.keywords);
                record.guid = row.guid; record.aliases = group.Count();
                rows.Add(record);
            }
        }
        var controls = new List<PassRecord>();
        foreach (string state in new[] { "Lighting Off", "Lighting On", "Lighting On Tags { \"LightMode\"=\"Always\" }", "Lighting On Tags { \"LightMode\"=\"ForwardBase\" }", "Lighting Off Tags { \"LightMode\"=\"Vertex\" }" })
        {
            string source = "Shader \"QuestPrivate/PassWitness" + controls.Count + "\" { SubShader { Pass { " + state + " HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\nfloat4 vert(float4 p:POSITION):SV_POSITION {return p;}\nfloat4 frag():SV_Target {return float4(1,0,0,1);}\nENDHLSL\n} } }";
            var shader = ShaderUtil.CreateShaderAsset(source, false);
            var record = Probe(shader,0,0,"Normal",new string[0]); record.guid = state;
            controls.Add(record); UnityEngine.Object.DestroyImmediate(shader);
        }
        string output = Environment.GetEnvironmentVariable("GHVR_QUEST_PASS_WITNESS_OUTPUT");
        File.WriteAllText(output,JsonUtility.ToJson(new Output {unityVersion=Application.unityVersion,shaders=input.shaders.Length,passes=rows.Count,aliases=rows.Sum(r=>r.aliases),records=rows.ToArray(),controls=controls.ToArray()},true)+"\n");
        foreach(var row in rows.Where(r=>!r.expectedAccepted)) Debug.Log("Native SVC mismatch " + row.guid + " " + row.name + " expected=" + row.expectedType + " accepted=" + string.Join(",",row.acceptedTypes)+" LightMode="+row.lightMode);
        Debug.Log("PASS native SVC importer census: shaders="+input.shaders.Length+", passes="+rows.Count+", aliases="+rows.Sum(r=>r.aliases));
    }
    static PassRecord Probe(Shader shader,int subshader,int pass,string expected,string[] keywords)
    {
        var data=ShaderUtil.GetShaderData(shader);
        var actual=data.GetSubshader(subshader);
        var accepted=new List<string>();
        foreach(PassType type in Enum.GetValues(typeof(PassType)))
        {
            try {new ShaderVariantCollection.ShaderVariant(shader,type,keywords);accepted.Add(type.ToString());}
            catch(ArgumentException) {}
        }
        return new PassRecord {name=shader.name,expectedType=expected,subshader=subshader,pass=pass,activeSubshader=data.ActiveSubshaderIndex,actualPassCount=actual.PassCount,
            lightMode=actual.GetPass(pass).FindTagValue(new ShaderTagId("LightMode")).name,
            acceptedTypes=accepted.ToArray(),importedEntryTypes=new string[0],expectedAccepted=accepted.Contains(expected)};
    }

    public static void Retain()
    {
        string manifestPath=Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST");
        var input=JsonUtility.FromJson<QuestCampaignShaderValidation.Manifest>(File.ReadAllText(manifestPath));
        if(input.shaders.Length!=688 || input.shaders.Sum(row=>row.variants.Length)!=51564)
            throw new InvalidOperationException("Incomplete original retention census.");
        var sourceHashes=JsonUtility.FromJson<SourceHashes>(File.ReadAllText(Environment.GetEnvironmentVariable("GHVR_QUEST_PASS_SOURCE_HASHES")));
        foreach(var source in sourceHashes.sources)
            if(Hash(File.ReadAllBytes(source.path))!=source.sha256) throw new InvalidOperationException("Actual source snapshot differs from the retained target.");
        int negatives=0;
        foreach(var row in input.shaders.Where(value=>value.originalName=="KriptoFX/RFX4/Particle"))
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(row.assetPath);
            foreach(var value in new[] {"Normal","ForwardBase","Invented"})
            {
                try {QuestCampaignShaderValidation.ImportedCollectionPassType(shader,new QuestCampaignShaderValidation.Variant {subshader=0,pass=0,passType=value});}
                catch(InvalidOperationException) {++negatives;continue;}
                throw new InvalidOperationException("Incorrect native pass classification was accepted.");
            }
            try {QuestCampaignShaderValidation.ImportedCollectionPassType(shader,new QuestCampaignShaderValidation.Variant {subshader=0,pass=1,passType="Vertex"});}
            catch(InvalidOperationException) {++negatives;continue;}
            throw new InvalidOperationException("Missing original pass ordinal was accepted.");
        }
        if(negatives!=8) throw new InvalidOperationException("Negative controls are incomplete.");
        QuestCampaignShaderValidation.PrepareVariantCollection(manifestPath);
        var collection=AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>("Assets/Resources/QuestCampaignShaderVariants.shadervariants");
        foreach(var row in input.shaders)
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(row.assetPath);
            foreach(var bank in row.variants)
                if(!collection.Contains(new ShaderVariantCollection.ShaderVariant(shader,(PassType)Enum.Parse(typeof(PassType),bank.passType),bank.keywords)))
                    throw new InvalidOperationException("Serialized collection lost an original native alias.");
        }
        if(sourceHashes.sources.Any(source=>Hash(File.ReadAllBytes(source.path))!=source.sha256))
            throw new InvalidOperationException("Retention changed native shader source/meta bytes.");
        File.WriteAllText(Environment.GetEnvironmentVariable("GHVR_QUEST_PASS_WITNESS_OUTPUT"),JsonUtility.ToJson(new Output {
            unityVersion=Application.unityVersion,shaders=688,aliases=51564,collectionShaders=collection.shaderCount,collectionVariants=collection.variantCount,
            mismatchNegativeControls=negatives,completeRetention=true,unchangedShaderSourcesAndMetas=true},true)+"\n");
        Debug.Log("PASS all original native SVC aliases retained and source bytes unchanged.");
    }
    [Serializable] public sealed class Source { public string path,sha256; }
    [Serializable] public sealed class SourceHashes { public Source[] sources; }
    static string Hash(byte[] source) {using(var hash=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(source)).Replace("-","").ToLowerInvariant();}
}
#endif
