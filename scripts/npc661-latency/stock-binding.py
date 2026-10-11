"""Bind actual offered stock publisher, native sampler and exact backing factory."""
import hashlib


def bind(root, bound, loader):
    card_path = root / 'src/GloomhavenVR/Cards/VRCard.cs'
    card = card_path.read_text()
    sampler = loader.method(card, 'internal bool TryTownReturnMotion(')
    templates=(root/'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs').read_text()
    bound['ActualStockPath661.cs']='using System;using System.Globalization;using UnityEngine; namespace GloomhavenVR.WorldUI;internal static partial class LazyTemplateProbe {\n'+loader.method(templates,'internal static Transform? At(Transform root, string path)')+'''
internal static void FreezeStock661(Transform source,string key) {
    var entry=new Entry{Original=source};Freeze(key,entry);Entries.Add(key,entry);
}
}\n'''
    bound['ActualPreparedCardSampler661.cs'] = '''using System;using UnityEngine;using GloomhavenVR.Hands;
namespace GloomhavenVR.Cards;internal sealed partial class VRCard {
private bool _flying,_flyIntro;
private float _flyElapsed,_flyDuration,_flyArcHeight;
private uint _townReturnRevision;
private Vector3 _flyFromPos,_flyToPos,_flyFromScale,_flyToScale,_flyArcUp;
private Quaternion _flyRot;
''' + sampler + '\n}\n'
    bound['StockTemplateParts661.cs'] = '''using System;using System.Collections.Generic;using UnityEngine;
namespace GloomhavenVR.WorldUI;internal static partial class NativeTemplates {
internal static IReadOnlyList<Part> StockParts661(string key) {
    var result=new List<Part>();foreach(var part in LazyTemplateProbe.Parts(key))result.Add(new Part{Path=part.Path});return result;
}
internal static void StockResolve661(byte service,ushort template,string address) {
    int split=address.IndexOf('|');string key=address.Substring(0,split),path=address.Substring(split+1);
    foreach(var part in LazyTemplateProbe.Parts(key))if(part.Path==path) {
        GloomhavenVR.Net.TownServices.TownServiceMirror.RegisterTemplate(service,template,part.Original,part.Excluded.Contains,address);return;
    }
    throw new InvalidOperationException("actual frozen stock partition absent: "+address);
}
}'''
    return {str(card_path):hashlib.sha256(card.encode()).hexdigest()}


def adapt_fixture(fixture, loader):
    path=fixture/'Transfer.cs';text=path.read_text()
    start=text.index('        internal bool TryTownReturnMotion(Transform source,Transform shared,Hands.VRHand? hand,out uint revision,out float[] values)')
    end=text.index('\n    }',start)
    text=text[:start]+text[end:];path.write_text(text)
    path=fixture/'Publisher.cs';text=path.read_text()
    for before,after in (
        ('internal static IReadOnlyList<Part> Parts(string key) => OnePart;',
         'internal static IReadOnlyList<Part> Parts(string key) => StockParts661(key);'),
        ('internal static Transform At(Transform source,string path) => source;',
         'internal static Transform At(Transform source,string path) => LazyTemplateProbe.At(source,path)!;'),
        ('{ string key=address.Split(\'|\')[0];TownServiceMirror.RegisterTemplate(service,template,Originals[key],IsBoundary,address); }',
         '{ StockResolve661(service,template,address); }')):
        if text.count(before)!=1:raise RuntimeError('Actual stock template port drift: '+before)
        text=text.replace(before,after,1)
    text=text.replace('        private static readonly Part[] OnePart = { new() };\n','',1)
    path.write_text(text)


def case(text):
    before='        TownServiceMirror.BeginSession(service,639,owner,owner);'
    if text.count(before)!=1:raise RuntimeError('Actual stock source setup binding drift')
    text=text.replace(before,'''        // Source is the real offered native print and the real VRCard backing
        // factory. Only game model/container construction is the existing port.
        var card661=Go("Actual offered VRCard661",owner).AddComponent<GloomhavenVR.Cards.VRCard>();
        card661.FixtureBacking(new Vector2(.14f,.1936f));
        physical.SetParent(card661.transform.Find("Visual"),true);
        string bodyKey661=TownServiceAbilityBody.Key(card661);
        LazyTemplateProbe.FreezeStock661(physical,"face.63901");
        TownServicePresentation.Service=3;
        TownServicePresentation.Ritual=new TownServiceRitual{Handoff=new TownServiceEnhancementHandoff {
            Card=card661,Face=physical,OfferedCardId=63901 }};
        TownServiceSync.UseProductionPublish=true;
        typeof(TownServiceSync).GetMethod("TickStock",PrivateStatic)!.Invoke(null,new object[]{owner});
        TownServiceSync.UseProductionPublish=false;
''' + before,1)
    before='        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),'
    if text.count(before)!=1:raise RuntimeError('Actual stock traffic assertion binding drift')
    text=text.replace(before,'''        var stock661=captured.Where(frame=>frame.VisitorStock&&frame.Module<TownServiceFrame.VoiceModule).ToArray();
        Check(stock661.Length>1&&stock661.Any(frame=>frame.TemplateAddress.StartsWith("face.63901|",StringComparison.Ordinal))
            &&stock661.Any(frame=>frame.TemplateAddress==bodyKey661+"|"),"actual TickStock publishes the full offered print and exact physical VRCard body");
        Check(stock661.All(frame=>!frame.Visible&&frame.BaseSequence==0&&frame.NativeTemplateBasisKey==0),
            "actual native stopped sampler prepares complete stock originals without a duplicate visible card or compact/receipt substitution");
        File.WriteAllText(Path.Combine(_output,"stock661.txt"),"Actual Sync.TickStockCore -> PublishNative -> exact native frozen partitions / real VRCard backing factory / actual TryTownReturnMotion -> CaptureCore StockLane -> unchanged shared scheduler.\\n"
            +"members="+stock661.Length+" uncompressedFullBytes="+stock661.Sum(frame=>TownServiceCodec.Write(frame).Length)+" body="+bodyKey661+"\\n");
''' + before,1)
    return text
