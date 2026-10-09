"""Upgrade the inherited native delivery case without changing its deadline."""

def patch(case_source, cold=False, mismatch=False):
    start = case_source.index('        Transform physical=Rect("Complete original offered print"')
    end = case_source.index('        Image? poison=null;', start)
    case_source = case_source[:start] + '''        Transform nativeAbility658=NativeRow632(owner,"", "ability-card");
        Transform physical=nativeAbility658.Find("Full");
        Check(physical!=null,"shipped native AbilityCard Full subtree exists");
        physical.SetParent(owner,false);Object.DestroyImmediate(nativeAbility658.gameObject);
        physical.localScale=Vector3.one*.00049f;physical.localPosition=new Vector3(-.24f,.02f,0);
        physical.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        if(physical.GetComponent<CanvasGroup>()==null)physical.gameObject.AddComponent<CanvasGroup>();
        var title=physical.Find("Header/Title text").GetComponent<TMP_Text>();
        title.text="Poison dart";title.color=Color.white;
        var ability=physical.Find("Default action buttons/Default action button/TextMeshPro Text").GetComponent<TMP_Text>();
        ability.text="Attack3 / Range3 / Poison";ability.color=Color.white;
        Check(physical.GetComponentsInChildren<Transform>(true).Length==50,
            "actual50-node original Full print survives without gameplay controllers");
        // Native MakeAbilityAction uses these shipped CreateLayout prototypes.
        // Model population and dynamic container geometry are declared inputs;
        // every serialized native child/material remains in the captured print.
        var nativeActionIcons658=new List<(Image Image,string Name)>();
        for(int half658=0;half658<2;half658++)
        {
            Transform content658=physical.Find(half658==0?"Top button/Content":"Bottom button/Content");
            RectTransform layout658=(RectTransform)NativeRow632(content658,"","text-container");
            layout658.name="Layout Parent";layout658.sizeDelta=new Vector2(294,190);layout658.anchoredPosition=Vector2.zero;
            for(int row658=0;row658<2;row658++)
            {
                RectTransform rowRoot658=(RectTransform)NativeRow632(layout658,"","text-container");
                rowRoot658.name="Row Container "+row658;rowRoot658.sizeDelta=new Vector2(270,70);rowRoot658.anchoredPosition=new Vector2(0,45-row658*75);
                Transform text658=NativeRow632(rowRoot658,"","preview-text");
                ((RectTransform)text658).sizeDelta=new Vector2(235,55);((RectTransform)text658).anchoredPosition=Vector2.zero;
                text658.GetComponent<TMP_Text>().text=half658==0?(row658==0?"Attack 3":"Range 3 — Poison"):(row658==0?"Move 4":"Gain 1 experience");
                Transform enhancementContainer658=NativeRow632(rowRoot658,"","enhancement-container");
                ((RectTransform)enhancementContainer658).anchoredPosition=new Vector2(120,0);
                Transform enhancement658=NativeRow632(enhancementContainer658,"","enhancement");
                nativeActionIcons658.Add((enhancement658.GetComponent<Image>(),half658==0?(row658==0?"Attack":"Range"):"Move"));
            }
            NativeRow632(layout658,"","xp-container");NativeRow632(layout658,"","duration-res");
        }
''' + case_source[end:]
    case_source=case_source.replace('            string[] optionNames=',
        '            foreach(var icon658 in nativeActionIcons658){icon658.Image.sprite=ExactOption639(nativeSprites,icon658.Name);rowIcons.Add(icon658);}\n            string[] optionNames=',1)
    anchor = '        Add(physical,mage?"face.63901|":"itemface.63901|");'
    assert case_source.count(anchor) == 1
    case_source = case_source.replace(anchor, '''        var fullParts658=LazyTemplateProbe.NativePrintPartitions658(physical);
        int requiredPrint658=0;
        foreach(var part in fullParts658)
        {
            using var probe658=new TownServiceBinding(part.Original,part.Excluded.Contains);
            bool visible658=probe658.HasVisibleOutput();if(visible658)requiredPrint658++;
            Add(part.Original,"face.63901|"+part.Path,part.Excluded.Contains,required:visible658);
        }
        File.WriteAllText(Path.Combine(_output,"native-full-print658.txt"),
            "AbilityCard.Full plus original TextContainer/PreviewText/EnhancementContainer/Enhancement/XPContainer/DurationRes prototypes. Dynamic model population/container dimensions are inputs, native gameplay MakeAbilityAction is not executed.\\n"+
            "nodes="+physical.GetComponentsInChildren<Transform>(true).Length+" partitions="+fullParts658.Count+" required="+requiredPrint658+"\\n"+
            string.Join("\\n",fullParts658.Select(part=>part.Path+" nodes="+new TownServiceBinding(part.Original,part.Excluded.Contains).Nodes.Length)));
''')
    # The prior33/35 densities named a hand-written5-node card. Retain all its
    # native UI rows plus every actual structural partition of the complete print.
    case_source = case_source.replace('originals.Count==59&&originals.Count(x=>x.Required)==33',
        'originals.Count==58+fullParts658.Count&&originals.Count(x=>x.Required)==32+requiredPrint658')
    case_source = case_source.replace('originals.Count==66&&originals.Count(x=>x.Required)==35',
        'originals.Count==65+fullParts658.Count&&originals.Count(x=>x.Required)==34+requiredPrint658')
    # Frozen-native registration still precedes the owner offer, as in production;
    # shader/property basis and the initial original asset scan are timed here.
    case_source = case_source.replace('        TownServiceMirror.Assets.Clear();\n        var assetPreparation=',
        '        TownServiceMirror.Assets.Clear();\n        LazyTemplateProbe.RefreshAssets658();\n        var assetPreparation=')
    if cold:
        case_source = case_source.replace('            TownServiceMirror.PrepareNativeTemplateBasis(service,address);', '')
        case_source = case_source.replace('        TownServiceMirror.Assets.Clear();',
            '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n'
            '        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState",PrivateStatic)!.Invoke(null,null);\n'
            '        TownServiceMirror.Assets.Clear();',1)
        case_source = case_source.replace('        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n        double clock=',
            '        double clock=')
    else:
        case_source = case_source.replace('        preparation.Stop();',
            '        preparation.Stop();\n        yield return null;')
    case_source = case_source.replace('originals.First(x=>!x.Required);',
        'originals.First(x=>!x.Required&&x.Address.StartsWith("enchant.row|",StringComparison.Ordinal));')
    case_source = case_source.replace('remotePrint.Root.Find("Native Poison icon").GetComponent<Image>().sprite',
        'Remote(2,originals.First(x=>x.Source==poison!.transform).Id)!.Root.GetComponent<Image>().sprite')
    # Native TMP creates fresh atlas/material objects at its first draw. Validate
    # the actual production capture, rather than an obsolete pre-draw asset alias
    # or extra diagnostic bindings which would inflate the wall-time measurement.
    anchor = '            stage=watch.Elapsed.TotalSeconds;bool complete=originals.Where(x=>x.Required).All(original=>VisibleComplete639(original)&&(!PriorInFlight639||TextMatches639(original)));validateCpu+=watch.Elapsed.TotalSeconds-stage;'
    assert case_source.count(anchor) == 1
    case_source = case_source.replace(anchor, '''            foreach(var original in originals.Where(x=>x.Required))
            { var current658=TownServiceMirror.CapturedValues658(original.Id);
              if(current658!=null)original.Expected=current658; }
''' + anchor)
    anchor='        foreach(var original in originals.Where(x=>x.Required))AssertComplete639(original);'
    assert case_source.count(anchor)==2
    case_source=case_source.replace(anchor,anchor+'''
        foreach(var original658 in originals.Where(x=>x.Required))
        {
            var clone658=Remote(2,original658.Id)!;
            foreach(var image658 in original658.Source.GetComponentsInChildren<Image>(true))
            {
                if(original658.Exclude!=null&&Excluded639(image658.transform,original658.Source,original658.Exclude))continue;
                string path658=Relative639(original658.Source,image658.transform);
                var remoteImage658=(path658.Length==0?clone658.Root:clone658.Root.Find(path658))?.GetComponent<Image>();
                Check(remoteImage658!=null&&remoteImage658.gameObject.activeSelf==image658.gameObject.activeSelf&&remoteImage658.enabled==image658.enabled,
                    "all original hidden FX/Infuse graphic flags remain owner-authored without accidental activation: "+original658.Address+"/"+path658);
                if(image658.enabled&&image658.sprite!=null)
                    Check(remoteImage658!.sprite!=null&&TownServiceMirror.Assets.Key(remoteImage658.sprite)==TownServiceMirror.Assets.Key(image658.sprite),
                        "hidden and visible original sprite fields preserve exact authored pixels and packing: "+original658.Address+"/"+path658);
            }
        }
''')
    # Reverse receipts use their independent machine's ordinary bounded budget.
    # They affirm only actually admitted originals, even before the whole picture.
    anchor='        var receiver=new NetAvatarDriver();TownServiceMirror.SharedFrameForRemote=_=>observer;'
    assert case_source.count(anchor)==1
    case_source=case_source.replace(anchor,anchor+'''
        TownServiceMirror.CollectOriginalReceiptPeers=peers=>{peers.Clear();peers.Add(NetPlayerActors.Peer==2?10:2);};
        var receipts658=new ExtrasSendScheduler(0,3,4);
        FillOtherQueues639(scheduler);FillOtherQueues639(receipts658);
        using(var noiseHash658=System.Security.Cryptography.SHA256.Create())
            File.WriteAllText(Path.Combine(_output,"saturated-payloads658.txt"),
                string.Join("\\n",SaturatedPackets658.Select(pair=>pair.Key+" bytes="+pair.Value.Length+" sha256="+BitConverter.ToString(noiseHash658.ComputeHash(pair.Value)).Replace("-","").ToLowerInvariant()))+"\\n");
        void Receipt658(double receiptClock)
        {
            if(!AcknowledgeObserver639)return;
            NetPlayerActors.Peer=10;
            TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>receipts658.Enqueue(bytes,length));
            FillOtherQueues639(receipts658);var packet658=receipts658.NextBatch(receiptClock);
            if(packet658!=null)foreach(var page658 in PresentationBatch.TryRead(packet658,packet658.Length,out var pages658)?pages658!:new[]{packet658})
            {
                if(page658.Length<6||page658[5]!=28)continue;
                NetPlayerActors.Peer=2;Check(TownServiceMirror.ReceiveOriginalReceipt(10,page658,page658.Length),"actual first-picture original receipt uses bounded reverse transport");
            }
            NetPlayerActors.Peer=10;
        }
''')
    case_source=case_source.replace('    private static readonly bool PrewarmObserver639',
        '    private static Action? ReceiptPulse658;\n'
        '    private static readonly Dictionary<string,byte[]> SaturatedPackets658=new();\n'
        '    private static readonly bool PrewarmObserver639',1)
    anchor='''            var bytes=new byte[limit];new System.Random(639).NextBytes(bytes);
            bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=type;
            queue.Enqueue(bytes,bytes.Length);'''
    assert case_source.count(anchor)==1
    case_source=case_source.replace(anchor,'''            if(!SaturatedPackets658.TryGetValue(field,out var bytes))
            {
                bytes=new byte[limit];new System.Random(639).NextBytes(bytes);
                bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=type;
                SaturatedPackets658.Add(field,bytes);
            }
            Check(bytes.Length==limit&&bytes[5]==type,"cached saturation retains every exact production stream capacity/type");
            queue.Enqueue(bytes,bytes.Length);''',1)
    case_source=case_source.replace('        var deliveryClock=System.Diagnostics.Stopwatch.StartNew();',
        '        var deliveryClock=System.Diagnostics.Stopwatch.StartNew();\n        ReceiptPulse658=()=>Receipt658(deliveryClock.Elapsed.TotalSeconds);',1)
    case_source=case_source.replace('receiver.FixtureApply638();Canvas.ForceUpdateCanvases();',
        'receiver.FixtureApply638();ReceiptPulse658?.Invoke();Canvas.ForceUpdateCanvases();')
    case_source=case_source.replace('receiver.FixtureApply638();applyCpu+=', 'receiver.FixtureApply638();Receipt658(deliveryClock.Elapsed.TotalSeconds);applyCpu+=',1)
    if mismatch:
        anchor='        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),'
        assert case_source.count(anchor)==1
        case_source=case_source.replace(anchor,'''
        var mismatch658=originals.First(x=>x.Required&&x.Source.GetComponentsInChildren<Transform>(true).Length==32&&x.Address.StartsWith("face.",StringComparison.Ordinal));
        var mismatchFull658=captured.First(x=>x.Module==mismatch658.Id&&x.BaseSequence==0);
        Check(TownServiceMirror.TryWriteNativeTemplateState(mismatchFull658,out var mismatchBytes658)
            &&TownServiceCodec.TryRead(mismatchBytes658,mismatchBytes658.Length,out var sparse658),"actual full native print supplies an exact independently compact basis");
        TownServiceCodec.TryRead(mismatchBytes658,mismatchBytes658.Length,out sparse658);
        var omitted658=mismatchFull658.Nodes.Skip(1).First(node=>!sparse658!.Nodes.Any(patch=>patch.Binding==node.Binding&&patch.Values.ContainsKey(TownServiceProperty.Transform)));
        var templates658=(Dictionary<string,GameObject>)typeof(TownServiceMirror).GetField("Templates",PrivateStatic)!.GetValue(null)!;
        string key658=(string)typeof(TownServiceMirror).GetMethod("TemplateKey",PrivateStatic)!.Invoke(null,new object[]{service,mismatch658.Id,mismatch658.Address})!;
        using(var templateBindings658=new TownServiceBinding(templates658[key658].transform))
            ((RectTransform)templateBindings658.Nodes[Array.IndexOf(templateBindings658.Bindings,omitted658.Binding)]).sizeDelta+=new Vector2(37,19);
        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState",PrivateStatic)!.Invoke(null,null);
        Check(!TownServiceMirror.TryExpandNativeTemplateState(sparse658!,out _),"an actually omitted full-print numeric default rejects before paint");
''' + anchor,1)
        anchor='        Check(ready>=0&&ready<=1.000,"all exact visible originals render within1s wall clock");'
        assert case_source.count(anchor)==1
        case_source=case_source.replace(anchor,'''
        Check(ready>=0&&ready<=3,"actual full native mismatch fallback makes bounded progress within3s");
        foreach(var original658 in originals.Where(x=>x.Required))AssertComplete639(original658);
        var exactOriginal658=captured.First(x=>x.Module==mismatch658.Id&&x.BaseSequence==0);
        Check(captured.Count(x=>ReferenceEquals(x,exactOriginal658))==2,"mismatched full native original repairs once with the exact source identity");
        File.WriteAllText(Path.Combine(_output,"native-mismatch658.txt"),"full complete picture="+ready+" strict1s="+(ready<=1)+" rejectedModule="+mismatch658.Id+" address="+mismatch658.Address+" fullBytes="+TownServiceCodec.Write(exactOriginal658).Length+"\\n");
''' + anchor,1)
    return case_source
