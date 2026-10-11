"""Exact driver original-send and FFS admission seams; only SDK lifecycle is a port."""
import hashlib


def bind(root, bound, loader, expired=False, old_budget=False):
    driver_path = root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.TownServices.cs'
    driver = driver_path.read_text()
    body = loader.method(driver, 'private void SendTownServices()')
    # Keep the entire actual original metadata pass and actual 15Hz capture gate.
    # Gameplay grants, independent resident faces and VR mode are outside this
    # original source proof. Their omission is retained in the compiled source.
    begin = body.index('        TownServiceGrantSync.Tick(')
    gate = body.index('        float now = UnityEngine.Time.unscaledTime;', begin)
    end = body.index('        // Native widget capture is independent', gate)
    body = body[:begin] + body[gate:end] + '    }'
    fields = driver[driver.index('    private float _nextFaceSend'):driver.index('    private bool SendMerchantControl')]
    fields = fields.replace('private float _nextFaceSend, _nextTownCapture;', 'private float _nextTownCapture;')
    fields = fields.replace('    private Func<byte[], int, bool, bool>? _merchantControlSender;\n', '')
    fields = fields.replace('        VersionGuard.CollectContinuationPeers(peers, _transport.LocalPlayerId);',
                            '        VersionGuard661.Collect(peers, _transport.LocalPlayerId);')
    methods = [loader.method(driver, signature) for signature in (
        'private void QueueTownOriginalRequest(byte[] bytes, int length)',
        'private void QueueTownPresentation(byte[] bytes, int length, object? identity)')]
    bound['ActualOriginalDriver661.cs'] = '''using System;using System.Collections.Generic;
using UnityEngine;using GloomhavenVR.WorldUI;using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;namespace GloomhavenVR.Net;
internal sealed partial class NetAvatarDriver {
''' + fields + '\n' + '\n'.join(methods) + '\n' + body + '''
    private readonly RequestTransport661 _transport=new FfsNetTransport();
    internal FfsNetTransport FixtureTransport661=>(FfsNetTransport)_transport;
    private void LogPhaseError(string phase,Exception error)=>FixtureTransport661.Errors.Add(phase+": "+error.Message);
    internal void FixtureSend661() { _nextTownCapture=Time.unscaledTime+100;SendTownServices(); }
}
'''
    ffs_path = root / 'src/GloomhavenVR/Net/FfsNetTransport.cs'
    ffs = ffs_path.read_text()
    admission = [loader.method(ffs, signature) for signature in (
        'internal bool TrySendTownPresentation(byte[] payload, int length, TownServices.TownServiceFrame identity)',
        'internal bool TrySendTownOriginalReceipt(byte[] payload, int length)',
        'internal bool TrySendTownOriginalRequest(byte[] payload, int length)')]
    bound['ActualOriginalFfs661.cs'] = '''using System;namespace GloomhavenVR.Net;
internal sealed partial class FfsNetTransport:RequestTransport661 {
''' + '\n'.join(admission) + '\n}\n'
    bound['OriginalSendPorts661.cs'] = '''using System;using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;namespace GloomhavenVR.Net {
internal static class VersionGuard661 {
    internal static void Collect(List<int> peers,int local) { peers.Clear();peers.Add(local==2?10:2); }
}
internal class RequestTransport661 {
    internal bool IsOnline=true;
    internal int LocalPlayerId=>NetPlayerActors.Peer;
    internal virtual void Send(byte[] bytes,int length,object? identity=null)=>throw new InvalidOperationException("FFS admission path was bypassed");
}
internal sealed partial class FfsNetTransport {
    private bool _degraded;
    private bool _installed=true;
    private readonly object _sendSideAction=new(),_customDataCtor=new();
    private readonly ExtrasSendScheduler _extrasQueue=new(0,3,4);
    internal bool Installed { get=>_installed;set=>_installed=value; }
    internal readonly List<string> Errors=new();
    internal Action<byte[],int,object?>? Published;
    internal Action<TownServiceFrame>? AdmissionFailure661;
    internal byte[]? NextOriginalBatch661(double now)=>_extrasQueue.NextBatch(now);
    internal int RequestedMarkerCount661 {
        get {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            object town=typeof(ExtrasSendScheduler).GetField("_town",flags)!.GetValue(_extrasQueue)!;
            object lane=typeof(TownServiceSendQueue).GetField("_private",flags)!.GetValue(town)!;
            return ((System.Collections.IDictionary)typeof(TownServiceLaneSendQueue).GetField("_requestedOriginals",flags)!.GetValue(lane)!).Count;
        }
    }
    internal byte[] TakeMetadata661(bool required=true) {
        byte[]? packet=_extrasQueue.NextBatch(UnityEngine.Time.unscaledTime);
        if(packet==null) { if(required)throw new InvalidOperationException("actual bounded original metadata missing");return Array.Empty<byte>(); }
        foreach(byte[] page in PresentationBatch.TryRead(packet,packet.Length,out var pages)?pages!:new[]{packet})
            if(TownServiceOriginalRequestCodec.TryRead(page,page.Length,out _))return page;
        if(required)throw new InvalidOperationException("actual bounded batch contains no original request");return Array.Empty<byte>();
    }
}
}
namespace GloomhavenVR.Core {
internal static class PerfMonitor { internal static IDisposable Scope(string name)=>new Scope661();private sealed class Scope661:IDisposable {public void Dispose(){}} }
}
'''
    # A readonly publication observer retains exact object identity; source FFS
    # enqueue and rejection behavior run first, without a substituted send path.
    name = 'ActualOriginalFfs661.cs'
    before = 'try { _extrasQueue.Enqueue(payload, length, identity: identity); return true; }'
    if bound[name].count(before) != 1:
        raise RuntimeError('Actual FFS publication observer anchor drift')
    bound[name] = bound[name].replace(before, 'try { AdmissionFailure661?.Invoke(identity); _extrasQueue.Enqueue(payload, length, identity: identity); Published?.Invoke(payload,length,identity); return true; }', 1)
    native = 'TownServiceMirror.NativePublication.cs'
    if expired:
        before = 'long started = System.Diagnostics.Stopwatch.GetTimestamp();'
        if bound[native].count(before) != 1:
            raise RuntimeError('Requested original CPU clock port drift')
        bound[native] = bound[native].replace(before, before.replace('GetTimestamp();', 'GetTimestamp() - System.Diagnostics.Stopwatch.Frequency;'), 1)
    if old_budget:
        before = 'count >= 2 || count > 0 &&'
        if bound[native].count(before) != 1:
            raise RuntimeError('Old expired-budget guard control drift')
        bound[native] = bound[native].replace(before, 'count >= 2 ||', 1)
    return {str(driver_path):hashlib.sha256(driver.encode()).hexdigest(),
            str(ffs_path):hashlib.sha256(ffs.encode()).hexdigest()}
