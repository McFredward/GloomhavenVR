import pathlib, sys
root = pathlib.Path(sys.argv[1]); out = pathlib.Path(sys.argv[2])
cards = root / 'src/GloomhavenVR/Cards'
flows = (cards/'Driver/CardsDriver.6.Flows.cs').read_text()
interactions = (cards/'Driver/CardsDriver.5.Interactions.cs').read_text()
rebuild = (cards/'Driver/CardsDriver.4.Rebuild.cs').read_text()
update = (cards/'Driver/CardsDriver.2.Update.cs').read_text()

def method(text, signature):
    start = text.index(signature)
    start = text.rfind('\n', 0, start) + 1
    brace = text.index('{', start); depth = 1; end = brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}'); end += 1
    return text[start:end]

# Bind extracted production behavior to its real call site and the unchanged owner-to-peer path.
assert '                PrunePickField();' in rebuild
assert rebuild.index('PrunePickField();') < rebuild.index('bool fieldAffordance =')
relayout = method(interactions, 'private void RelayoutField()')
assert 'if (_pickExitFlown.Contains(card))\n                continue;' in relayout
sender = (root/'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs').read_text()
receiver = (root/'src/GloomhavenVR/Net/Remote/RemoteAvatar.cs').read_text()
mirror = (root/'src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs').read_text()
assert 'int overlays = trayNow.WantedSlotMask & NetProtocol.BoardUiWantedMask;' in sender
assert 'WantedGlowMask = p.HasBoardUi ? p.BoardOverlayMask & NetProtocol.BoardUiWantedMask : 0;' in receiver
assert 'wantedMask = owner.WantedGlowMask;' in mirror and 'SetWanted(wantedMask);' in mirror
recycle = method(update, 'private void OnCardRecycling(')
assert 'RetirePickFieldCard(card);' in recycle
assert recycle.index('RetirePickFieldCard(card);') < recycle.index('_factory.ReleaseWidget(widget);')
print('Pick tray source bindings: 8 assertions passed.')
parts = [method(flows, 'private void UpdateWantedSlots(')]
for signature in ['private void PrunePickField()', 'private int PickTargetSlot()', 'private int ArmPickRestart()', 'private int PickSeatOfIndex(']:
    parts.append(method(interactions, signature))
parts.append(method(update, 'private void RetirePickFieldCard('))
out.write_text('internal sealed partial class CardsDriver {\n' + '\n'.join(parts) + '\n}')
