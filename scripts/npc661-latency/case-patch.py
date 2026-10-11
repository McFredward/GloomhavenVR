"""Actual native source clock and template-policy challenges; no deadline changes."""

def replace_once(text, before, after):
    if text.count(before) != 1:
        raise RuntimeError('NPC661 source binding anchor drift: ' + before[:90])
    return text.replace(before, after, 1)


def shared_original_bank(text):
    text = replace_once(text, '        void Add(Transform source,string address,Func<Transform,bool>? exclude=null,bool required=true)',
                        '        var frozenAddresses661=new HashSet<string>(StringComparer.Ordinal);\n'
                        '        void Add(Transform source,string address,Func<Transform,bool>? exclude=null,bool required=true)')
    text = replace_once(text, '            ushort id=(ushort)(originals.Count+1);',
                        '''            // Actual Sync.Publish uses key + "|" + native part.Path;
            // repeated native option rows share enchant.row's one canonical part.
            if(address.StartsWith("enchant.row|",StringComparison.Ordinal))address="enchant.row|";
            ushort id=(ushort)(originals.Count+1);''')
    text = replace_once(text, '            Transform nativeObserver=Object.Instantiate(source.gameObject,observer,false).transform;',
                        '            if(!frozenAddresses661.Add(address))return;\n'
                        '            Transform nativeObserver=Object.Instantiate(source.gameObject,observer,false).transform;')
    return text


def native_change_setup():
    return '''        var mismatch658=originals.First(x=>x.Required&&x.Source.GetComponentsInChildren<Transform>(true).Length==32&&x.Address.StartsWith("face.",StringComparison.Ordinal));
        var mismatchFull658=captured.First(x=>x.Module==mismatch658.Id&&x.BaseSequence==0);
        Check(TownServiceMirror.TryWriteNativeTemplateState(mismatchFull658,out var mismatchBytes658)
            &&TownServiceCodec.TryRead(mismatchBytes658,mismatchBytes658.Length,out var sparse658),"actual full native print supplies an exact compact source");
        TownServiceCodec.TryRead(mismatchBytes658,mismatchBytes658.Length,out sparse658);
        var templates658=(Dictionary<string,GameObject>)typeof(TownServiceMirror).GetField("Templates",PrivateStatic)!.GetValue(null)!;
        string key658=(string)typeof(TownServiceMirror).GetMethod("TemplateKey",PrivateStatic)!.Invoke(null,new object[]{service,mismatch658.Id,mismatch658.Address})!;
'''


def numeric_original(text):
    setup = native_change_setup() + '''        Check(sparse658!.Nodes.All(node=>node.Values.ContainsKey(TownServiceProperty.Transform)),
            "every actual native model partition child carries owner-authored numeric geometry");
        using(var templateBindings658=new TownServiceBinding(templates658[key658].transform))
            foreach(var child661 in templateBindings658.Nodes.Skip(1).OfType<RectTransform>())
            { child661.sizeDelta+=new Vector2(37,19);child661.localRotation=Quaternion.Euler(9,17,23);child661.localScale*=1.13f; }
        TownServiceMirror.InvalidateOnlyBasis661(key658);
        Check(TownServiceMirror.TryExpandNativeTemplateState(sparse658!,out var exact661),
            "different observer native Row Container geometry reconstructs exact owner output immediately");
        AssertNativeEqual623(mismatchFull658,exact661);
        File.WriteAllText(Path.Combine(_output,"numeric-native661.txt"),"actual32-node native model partition + differing observer child RectTransform/TRS; full original bytes="
            +TownServiceCodec.Write(mismatchFull658).Length+" compact bytes="+mismatchBytes658.Length+"\\n");
'''
    return replace_once(text, '        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),',
                        setup + '        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),')


def material_refusal(text):
    start = text.index('        var mismatch658=originals.First(')
    end = text.index('        File.WriteAllText(Path.Combine(_output,"initial-capture.txt"),', start)
    replacement = native_change_setup() + '''        var omitted661=mismatchFull658.Nodes.Skip(1).First(node=>node.Values.ContainsKey(TownServiceProperty.Material)
            &&!sparse658!.Nodes.Any(patch=>patch.Binding==node.Binding&&patch.Values.ContainsKey(TownServiceProperty.Material)));
        using(var templateBindings658=new TownServiceBinding(templates658[key658].transform))
        {
            var graphic661=templateBindings658.Nodes[Array.IndexOf(templateBindings658.Bindings,omitted661.Binding)].GetComponent<Graphic>();
            var different661=new Material(graphic661.material){color=Color.magenta};Assets.Add(different661);graphic661.material=different661;
        }
        TownServiceMirror.InvalidateOnlyBasis661(key658);
        Check(!TownServiceMirror.TryExpandNativeTemplateState(sparse658!,out _),"different omitted native material still refuses before paint and requires the exact full source");
'''
    return text[:start] + replacement + text[end:]


def timed_cold_bank(text):
    beginning = text.index('            Transform nativeObserver=Object.Instantiate(source.gameObject,observer,false).transform;')
    end_marker = '            Object.DestroyImmediate(nativeObserver.gameObject);'
    ending = text.index(end_marker, beginning) + len(end_marker)
    freeze = text[beginning:ending]
    text = text[:beginning] + text[ending:]
    loop = '''        // The exact owner originals are now active. No observer partition has
        // been frozen, registered or based before this visible-source clock.
        var watch=System.Diagnostics.Stopwatch.StartNew();
        float began=Time.unscaledTime,nextCapture=began;
        Canvas.ForceUpdateCanvases();Render639(owner);
        double ownerFirstDraw661=watch.Elapsed.TotalSeconds;
        frozenAddresses661.Clear();
        foreach(var original661 in originals)
        {
            if(!frozenAddresses661.Add(original661.Address))continue;
            Transform source=original661.Source;
            string address=original661.Address;
            Func<Transform,bool>? exclude=original661.Exclude;
            ushort id=original661.Id;
''' + freeze + '''
        }
        double bankFrozen661=watch.Elapsed.TotalSeconds;
'''
    text = replace_once(text, '        TownServiceMirror.BeginSession(service,639,owner,owner);',
                        loop + '        TownServiceMirror.BeginSession(service,639,owner,owner);')
    text = replace_once(text,
                        '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n        double clock=',
                        '        // Continue the same clock through every preparation and delivery stage.\n        double clock=')
    text = replace_once(text,
                        '        var preparation=System.Diagnostics.Stopwatch.StartNew();\n        Canvas.ForceUpdateCanvases();Render639(owner);',
                        '        var preparation=System.Diagnostics.Stopwatch.StartNew();\n        Canvas.ForceUpdateCanvases();')
    text = replace_once(text, '        preparation.Stop();\n        yield return null;',
                        '        preparation.Stop();')
    return text


def request_transport(text):
    # The observer and owner run in separate declared role scopes, like the
    # inherited receipt proof. Both directions use the real saturated scheduler.
    text = replace_once(text,
                        '            TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>receipts658.Enqueue(bytes,length));',
                        '            TownServiceMirror.CaptureOriginalRequests((bytes,length)=>receipts658.Enqueue(bytes,length));\n'
                        '            TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>receipts658.Enqueue(bytes,length));')
    text = replace_once(text,
                        '                NetPlayerActors.Peer=2;Check(TownServiceMirror.ReceiveOriginalReceipt(10,page658,page658.Length),"actual first-picture original receipt uses bounded reverse transport");',
                        '''                NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
                try
                {
                    bool request661=TownServiceOriginalRequestCodec.TryRead(page658,page658.Length,out _);
                    Check(request661?TownServiceMirror.ReceiveOriginalRequest(10,page658,page658.Length)
                        :TownServiceMirror.ReceiveOriginalReceipt(10,page658,page658.Length),
                        "actual first-picture receipt/request uses unchanged bounded reverse transport");
                    TownServiceMirror.CaptureRequestedOriginalRepairs(publish);
                }
                finally { SetNativeSenderActive629(false); }
''')
    return text
