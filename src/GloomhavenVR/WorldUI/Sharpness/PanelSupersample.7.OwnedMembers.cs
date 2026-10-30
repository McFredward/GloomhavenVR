using System.Collections.Generic;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // A confirmation is part of its explicit native producer, but its original
    // DontDestroyOnLoad home cannot be parented into that producer's scene. Give
    // only this owned member the producer's existing capture, never another
    // independent mono allocation or another window's private layer.
    private sealed class OwnedMember : IMenuDepthOwnedWindowSource
    {
        internal readonly ConvertedPanel Owner, Member;
        internal readonly RectTransform OwnerRoot, MemberRoot, OwnerTarget, MemberTarget;
        internal readonly List<LayerRecord> Layers = new(32);
        internal readonly HashSet<Transform> Recorded = new(TransformIdentityComparer.Instance);
        internal readonly List<Transform> Pending = new(32);
        internal readonly List<Graphic> Graphics = new(16);
        internal Entry? Entry;
        internal Graphic? Anchor;
        internal bool Registered;

        internal OwnedMember(ConvertedPanel owner, ConvertedPanel member)
        {
            Owner = owner; Member = member; OwnerRoot = owner.HostRect; MemberRoot = member.HostRect;
            OwnerTarget = owner.Target; MemberTarget = member.Target;
        }
        public Graphic Graphic => Anchor!;
        public Transform CaptureOwner => OwnerRoot;
        public RectTransform AnnotationRect => MemberRoot;
        public bool IsReady => Registered && Entry != null && Usable(this, Entry)
            && Anchor != null && Anchor.transform.IsChildOf(Member.Target)
            && OwnedTree(this, Anchor.transform) && MenuDepthSources.IsVisible(Anchor);
        public void BeforeCapture(MenuDepthEye eye) { }
        public void AfterCapture() { }
    }

    private static readonly List<OwnedMember> OwnedMembers = new(4);

    private static bool LiveConversion(ConvertedPanel panel)
    {
        if (!panel.IsAlive || panel.Target == null || !panel.Target.gameObject.activeInHierarchy
            || panel.HostGo == null || !panel.HostGo.activeInHierarchy) return false;
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        for (int i = 0; i < live.Count; i++) if (ReferenceEquals(live[i], panel)) return true;
        return false;
    }

    private static bool Usable(OwnedMember member, Entry entry) => Entries.Contains(entry)
        && entry.Layer >= 0 && ReferenceEquals(entry.Panel, member.Owner)
        && SameBinding(member)
        && LiveConversion(member.Owner) && LiveConversion(member.Member)
        && !member.Owner.RenderHidden && !member.Owner.OwnerRenderHidden
        && !member.Member.RenderHidden && !member.Member.OwnerRenderHidden
        && member.Owner.ModLayerEnabled && member.Member.ModLayerEnabled;

    private static bool SameBinding(OwnedMember member)
        => ReferenceEquals(member.Owner.HostRect, member.OwnerRoot) && ReferenceEquals(member.Owner.Target, member.OwnerTarget)
            && ReferenceEquals(member.Member.HostRect, member.MemberRoot) && ReferenceEquals(member.Member.Target, member.MemberTarget);

    private static bool OwnedTree(OwnedMember member, Transform node)
    {
        if (!ReferenceEquals(member.Member.HostRect, member.MemberRoot) || !ReferenceEquals(member.Member.Target, member.MemberTarget)
            || member.Member.HostGo == null || !node.IsChildOf(member.Member.HostGo.transform)) return false;
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        for (int i = 0; i < live.Count; i++)
        {
            ConvertedPanel other = live[i];
            if (ReferenceEquals(other, member.Member) || ReferenceEquals(other, member.Owner)) continue;
            if (other.IsAlive && other.HostGo != null && node.IsChildOf(other.HostGo.transform)) return false;
        }
        return true;
    }

    internal static void BindConfirmationMember(ConvertedPanel owner, ConvertedPanel member)
    {
        if (ReferenceEquals(owner, member) || owner.HostRect == null || member.HostRect == null
            || owner.HostRect.IsChildOf(member.HostRect)) return;
        for (int i = 0; i < OwnedMembers.Count; i++)
        {
            OwnedMember current = OwnedMembers[i];
            if (ReferenceEquals(current.Member, member) && ReferenceEquals(current.Owner, owner) && SameBinding(current)) return;
            // A finite single native producer owns this confirmation. Never
            // construct a capture cycle or make one member own another group.
            if (ReferenceEquals(current.Member, owner) || ReferenceEquals(current.Owner, member)) return;
        }
        ForgetOwnedPanel(member);
        for (int i = Entries.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Entries[i].Panel, member)) StandDown(Entries[i], i, "the native confirmation joined its producer");
        var owned = new OwnedMember(owner, member);
        OwnedMembers.Add(owned);
        for (int i = 0; i < Entries.Count; i++)
            if (ReferenceEquals(Entries[i].Panel, owner)) SyncOwnedMembers(Entries[i]);
    }

    private static bool CapturedOwnedMember(ConvertedPanel panel)
    {
        for (int i = 0; i < OwnedMembers.Count; i++)
        {
            OwnedMember member = OwnedMembers[i];
            if (ReferenceEquals(member.Member, panel) && member.Entry != null && Usable(member, member.Entry)) return true;
        }
        return false;
    }

    private static void SyncOwnedMembers(Entry entry)
    {
        for (int i = OwnedMembers.Count - 1; i >= 0; i--)
        {
            OwnedMember member = OwnedMembers[i];
            if (!ReferenceEquals(member.Owner, entry.Panel)) continue;
            if (!SameBinding(member) || !LiveConversion(member.Owner) || !LiveConversion(member.Member))
            { ReleaseOwnedCapture(member); OwnedMembers.RemoveAt(i); continue; }
            if (!Usable(member, entry)) { ReleaseOwnedCapture(member); continue; }
            if (!ReferenceEquals(member.Entry, entry)) { ReleaseOwnedCapture(member); member.Entry = entry; }
            member.Member.Target.GetComponentsInChildren(true, member.Graphics);
            Graphic? anchor = null;
            for (int g = 0; g < member.Graphics.Count; g++)
            {
                Graphic graphic = member.Graphics[g];
                if (MenuDepthSources.IsVisible(graphic) && OwnedTree(member, graphic.transform)) { anchor = graphic; break; }
            }
            if (!ReferenceEquals(anchor, member.Anchor))
            {
                entry.MenuDepthFrame = -1;
                if (member.Registered) { member.Registered = false; MenuDepthSources.Unregister(member); }
                member.Anchor = anchor;
            }
            if (member.Anchor == null && member.Registered)
            { member.Registered = false; MenuDepthSources.Unregister(member); }
            member.Pending.Clear(); member.Pending.Add(member.Member.HostGo.transform);
            while (member.Pending.Count != 0)
            {
                int last = member.Pending.Count - 1;
                Transform node = member.Pending[last]; member.Pending.RemoveAt(last);
                if (node == null || !OwnedTree(member, node)
                    || node.GetComponent<Renderer>() != null || node.GetComponent<Camera>() != null) continue;
                int observed = node.gameObject.layer;
                // An independently admitted neighbour remains authoritative.
                // Do not accept its layer merely because a pooled node arrived.
                if (IsForeignPoolLayer(entry, observed)) continue;
                if (observed != entry.Layer)
                {
                    entry.MenuDepthFrame = -1;
                    if (member.Recorded.Add(node)) member.Layers.Add(new LayerRecord { Transform = node, OriginalLayer = observed });
                    node.gameObject.layer = entry.Layer;
                }
                for (int child = node.childCount - 1; child >= 0; child--) member.Pending.Add(node.GetChild(child));
            }
            if (!member.Registered && member.Anchor != null)
            {
                member.Registered = true;
                MenuDepthSources.Register(member);
            }
        }
    }

    private static void ReleaseOwnedCapture(OwnedMember member)
    {
        if (member.Entry != null) member.Entry.MenuDepthFrame = -1;
        if (member.Registered) { member.Registered = false; MenuDepthSources.Unregister(member); }
        int layer = member.Entry != null ? member.Entry.Layer : -1;
        for (int i = 0; i < member.Layers.Count; i++)
        {
            LayerRecord record = member.Layers[i];
            // A pooled widget may already belong to another native window,
            // possibly using the same recycled layer number. Its current
            // root, rather than that integer alone, decides restoration.
            if (record.Transform != null && OwnedTree(member, record.Transform)
                && record.Transform.gameObject.layer == layer)
                record.Transform.gameObject.layer = record.OriginalLayer;
        }
        member.Layers.Clear(); member.Recorded.Clear(); member.Graphics.Clear(); member.Pending.Clear();
        member.Anchor = null; member.Entry = null;
    }

    private static void ReleaseOwnedMembers(Entry entry)
    {
        for (int i = 0; i < OwnedMembers.Count; i++)
            if (ReferenceEquals(OwnedMembers[i].Entry, entry)) ReleaseOwnedCapture(OwnedMembers[i]);
    }

    private static void ForgetOwnedPanel(ConvertedPanel panel)
    {
        for (int i = OwnedMembers.Count - 1; i >= 0; i--)
            if (ReferenceEquals(OwnedMembers[i].Member, panel) || ReferenceEquals(OwnedMembers[i].Owner, panel))
            { ReleaseOwnedCapture(OwnedMembers[i]); OwnedMembers.RemoveAt(i); }
    }

    private static void ResetOwnedMembers()
    {
        for (int i = 0; i < OwnedMembers.Count; i++) ReleaseOwnedCapture(OwnedMembers[i]);
        OwnedMembers.Clear();
    }
}
