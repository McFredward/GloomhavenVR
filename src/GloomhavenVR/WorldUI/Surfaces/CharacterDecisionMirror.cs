using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Original, inert decision widgets on a board viewing another player's character.</summary>
internal sealed class CharacterDecisionMirror
{
    private Transform? _root, _rowRoot, _promptRoot;
    private RemoteDecisionWidgets? _row;
    private readonly RemoteUseBarWidgets?[] _bars = new RemoteUseBarWidgets?[4];
    private readonly Transform?[] _barRoots = new Transform?[4];
    private RemoteOriginalDecisionPrompt? _prompt;
    private CPlayerActor? _actor;
    internal bool Showing { get; private set; }

    internal void Tick()
    {
        CPlayerActor? actor = Board.CharacterFocus.PresentedActor ?? Board.CharacterFocus.Focused;
        Transform? mount = PlayTray.Current?.DecisionMount;
        if (!WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive || actor == null
            || !Board.CharacterFocus.IsForeign(actor) || mount == null || !mount.gameObject.activeInHierarchy
            || !NetAvatarDriver.TryGetCharacterDecisionOwner(actor, out RemoteAvatar? owner) || owner == null
            || !CharacterDecisionPresentation.TryGet(owner, actor, out CharacterDecisionPresentation? picture) || picture == null)
        { Hide(); return; }
        float ceiling = DecisionDockSurface.AreaCeilingUp(mount, mount.up, mount.lossyScale.x, out _) / mount.lossyScale.x;
        Paint(actor, picture, mount, ceiling, DecisionDockSurface.GapBoardMeters);
    }

    internal void TickAt(CPlayerActor? actor, RemoteAvatar boardOwner, Transform mount, float ceiling, float gap)
    {
        if (actor == null) { Hide(); return; }
        CharacterDecisionPresentation? picture;
        if (!CharacterDecisionPresentation.TryGetLocal(actor, out picture))
        {
            if (!NetAvatarDriver.TryGetCharacterDecisionOwner(actor, out RemoteAvatar? owner)
                || owner == null || owner.PlayerId == boardOwner.PlayerId
                || !CharacterDecisionPresentation.TryGet(owner, actor, out picture)) { Hide(); return; }
        }
        if (picture == null) { Hide(); return; }
        Paint(actor, picture, mount, ceiling, gap);
    }

    private void Paint(CPlayerActor actor, CharacterDecisionPresentation picture, Transform mount, float ceiling, float gap)
    {
        // The only peer lookup is the game's actual character claimant. Merely viewing a character
        // never grants ownership, and nothing created below is registered with an input router.
        if (_root == null) Build(mount);
        if (!ReferenceEquals(_actor, actor)) { ReleaseBars(); _actor = actor; }
        Showing = true;
        _root!.gameObject.SetActive(true);
        _root.SetPositionAndRotation(mount.position, mount.rotation);
        _root.localScale = mount.lossyScale;
        _rowRoot!.localPosition = new Vector3(0f, ceiling, 0f);
        bool rowUp = _row!.Refresh(picture);
        float promptHeight = ShowPrompt(actor, picture, rowUp, ceiling);
        if (promptHeight > 0f)
            _rowRoot.localPosition -= Vector3.up * (promptHeight + gap);
        _row.TickPointer(picture);
        float cursor = rowUp ? _rowRoot.localPosition.y - _row.RowHeight - UseBarsSurface.DecisionClearance : ceiling;
        for (int bar = 0; bar < 4; bar++)
        {
            int count = (picture.UseBarsMask & (1 << bar)) != 0 && picture.UseBarSlotCounts != null
                && bar < picture.UseBarSlotCounts.Length ? picture.UseBarSlotCounts[bar] : 0;
            if (count == 0) { ReleaseBar(bar); continue; }
            if (_bars[bar] == null || _bars[bar]!.Count != count)
            {
                ReleaseBar(bar);
                _barRoots[bar] = new GameObject("CharacterUseBar" + bar).transform;
                _barRoots[bar]!.SetParent(_root, false);
                _bars[bar] = new RemoteUseBarWidgets(_barRoots[bar]!, bar, count);
            }
            RemoteUseBarWidgets widgets = _bars[bar]!;
            _barRoots[bar]!.localPosition = new Vector3(0f, cursor, -UseBarsSurface.ProudStep * (bar + 1));
            widgets.Refresh(actor, picture);
            for (int slot = 0; slot < count; slot++)
            {
                int at = bar * NetProtocol.UseBarsMaxSlots + slot;
                ushort id = picture.UseBarSlotIds != null && at < picture.UseBarSlotIds.Length
                    ? picture.UseBarSlotIds[at] : (ushort)0;
                widgets.SetIcon(slot, UseBarSlotSymbol.ResolveIcon(bar, actor, id, out _));
            }
            widgets.Tick(picture);
            cursor -= widgets.Height + UseBarsSurface.StackGap;
        }
    }

    private void Build(Transform mount)
    {
        _root = new GameObject("CharacterDecisionMirror").transform;
        _root.SetPositionAndRotation(mount.position, mount.rotation);
        _root.localScale = mount.lossyScale;
        _rowRoot = new GameObject("OriginalDecisionRow").transform; _rowRoot.SetParent(_root, false);
        _promptRoot = new GameObject("OriginalDecisionPrompt").transform; _promptRoot.SetParent(_root, false);
        _row = new RemoteDecisionWidgets(_rowRoot, 1f, NativeButtonSkin.LabelColor);
        _prompt = new RemoteOriginalDecisionPrompt(_promptRoot);
    }

    private float ShowPrompt(CPlayerActor actor, CharacterDecisionPresentation picture, bool rowUp, float ceiling)
    {
        string? text = rowUp ? RemoteDecisionPrompt.Compose(picture.DecisionPromptKind,
            picture.DecisionTextVariant, actor, true, picture.DecisionNames) : null;
        _promptRoot!.localPosition = new Vector3(0f, ceiling, 0f);
        _prompt!.Show(text);
        return _prompt.Height;
    }

    internal void Hide() { Showing = false; if (_root != null) _root.gameObject.SetActive(false); }
    private void ReleaseBar(int bar)
    {
        _bars[bar]?.Destroy(); _bars[bar] = null;
        if (_barRoots[bar] != null) Object.Destroy(_barRoots[bar]!.gameObject);
        _barRoots[bar] = null;
    }
    private void ReleaseBars() { for (int i = 0; i < 4; i++) ReleaseBar(i); }
    internal void Destroy()
    {
        ReleaseBars(); _row?.Destroy(); _row = null; _prompt?.Destroy(); _prompt = null;
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = _rowRoot = _promptRoot = null;
    }
}
