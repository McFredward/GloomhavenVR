"""Bind the executed native-item and transport harnesses to production routing."""
from pathlib import Path
import sys

root = Path(sys.argv[1])
net = root / 'src/GloomhavenVR/Net'
transport = (net / 'FfsNetTransport.cs').read_text()

def send_binding(source):
    start = source.index('    public void Send(')
    queued = source.index('_extrasQueue.Enqueue(payload, length, identity: presentationIdentity);', start)
    guard = source[source.index('if (type == NetProtocol.MsgExtras', start):queued]
    assert 'type == NetProtocol.MsgItemAppearance' in guard, 'Native item packets must use the bounded presentation scheduler'
    assert queued < source.index('SendToken(bytes);', start), 'Item snapshots must not bypass fragmentation'

send_binding(transport)
start = transport.index('    public void Send(')
end = transport.index('            if (type == NetProtocol.MsgNativeUseBar)', start)
mutation = transport[:start] + transport[start:end].replace(' || type == NetProtocol.MsgItemAppearance', '') + transport[end:]
try:
    send_binding(mutation)
except AssertionError as error:
    assert str(error) == 'Native item packets must use the bounded presentation scheduler'
else:
    raise AssertionError('Missing item send registration survived the integration negative control')
assert 'NetProtocol.MsgItemAppearance => NetProtocol.MsgItemAppearanceFragments' in transport
assert 'routedType == NetProtocol.MsgItemAppearanceFragments ? _itemAppearanceFragments' in transport
assert '_itemAppearanceFragments.Forget(senderId);' in transport and '_itemAppearanceFragments.Clear();' in transport
assert 'type == NetProtocol.MsgItemAppearance' in (net / 'ExtrasFragments.cs').read_text()
assert 'type == NetProtocol.MsgItemAppearanceFragments' in (net / 'PresentationBatch.cs').read_text()
stream = (net / 'Avatar/NetAvatarDriver.ItemAppearance.cs').read_text()
for binding in ['ItemAppearanceSampler.Sample()', '_transport.Send(_itemAppearanceBuffer, length, snapshot)',
                'ItemAppearanceCodec.TryRead(', 'ItemAppearanceSnapshot.SameIdentity',
                'ItemAppearanceMirror.Set(', 'ItemAppearanceMirror.Remove(sender)', 'ItemAppearanceSampler.Reset()']:
    assert binding in stream, f'Missing item stream lifecycle: {binding}'
dispatch = (net / 'Avatar/NetAvatarDriver.NativePresentation.cs').read_text()
for binding in ['TickItemAppearanceSend(now)', 'QueueItemAppearance(sender, buffer, length)',
                'ApplyItemAppearance();', 'ForgetItemAppearance(sender);', 'ResetItemAppearance();']:
    assert binding in dispatch, f'Unregistered item stream lifecycle: {binding}'
assert 'case NetProtocol.MsgItemAppearance:' in (net / 'Avatar/NetAvatarDriver.cs').read_text()
furniture = (net / 'Remote/RemoteBoardFurniture.cs').read_text()
start = furniture.index('public void TickWire(')
close = furniture.index('// ---- SYNCED CAP STATES', start)
block = furniture[start:close]
assert block.index('ItemAppearanceMirror.HoldsAnyClip(') > block.index('SetShown(_decision, true);')
assert 'SetItemUseShown(!synced || (buttons & NetProtocol.BoardUiItemRecessBit) != 0 || retainItemClip, _settled);' in block
print('Native item transport binding: bounded send, fragment routing, stream lifecycle and clip parent verified; missing-send negative control rejected.')
