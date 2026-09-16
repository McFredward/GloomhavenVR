using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core
{
    internal static partial class TickGuard
    {
        internal static int Errors;
        internal static Exception? LastError;
        private static string DeriveScope(string name) => name;
        private static void NoteThrow(string scope, string name, Exception ex, string subject, string reason, string advice)
        { Errors++; LastError = ex; }
    }
    internal static class PerfMonitor
    {
        internal static long BeginStep() => 0;
        internal static void EndStep(string name, long timestamp) { }
    }
}
namespace GloomhavenVR.Cards
{
    internal static class CardsDriver { internal static bool NativeSceneLoadInProgress; }
    internal sealed class WidgetMap
    {
        private readonly Dictionary<AbilityCardUI, VRCard> _items = new();
        internal bool TryGetValue(AbilityCardUI widget, out VRCard card)
        { if (_items.TryGetValue(widget, out var value)) { card = value; return true; } card = null!; return false; }
        internal VRCard this[AbilityCardUI widget] { set => _items[widget] = value; }
    }
    internal static class VRLog { internal static void Warn(string category, string text) { } }
    internal static class CardsGameApi { internal static string CardName(AbilityCardUI card) => "fixture"; }
    internal sealed class Node
    {
        internal Node? Parent;
        internal bool Alive = true;
        internal readonly List<Node> Children = new();
        internal void ParentTo(Node parent)
        { Parent?.Children.Remove(this); Parent = parent; parent.Children.Add(this); }
        internal void Destroy() { Alive = false; foreach (var child in Children) child.Destroy(); }
    }
    internal sealed class AbilityCardUI
    {
        internal readonly Node Owner = new(), Face = new(), Mask = new(), Selectable = new();
        internal AbilityCardUI() { Face.ParentTo(Owner); Mask.ParentTo(Face); Selectable.ParentTo(Face); }
    }
    internal partial class VRCard
    {
        internal AbilityCardUI? GameCard;
        internal readonly Node Host = new();
        internal bool HasAdoptedFace;
        internal int Adoptions;
        internal bool AttachmentUnavailable;
        internal sealed class BurnFx { internal void Detach() { } }
        private readonly BurnFx _burnFx = new();
        internal bool AttachGameCard(AbilityCardUI widget)
        {
            if (AttachmentUnavailable) return false;
            GameCard = widget; widget.Face.ParentTo(Host); HasAdoptedFace = true; Adoptions++; return true;
        }
        internal void DetachGameCard()
        {
            if (GameCard != null && HasAdoptedFace) GameCard.Face.ParentTo(GameCard.Owner);
            HasAdoptedFace = false; GameCard = null;
        }
    }
    internal partial class VRCardFactory
    {
        private readonly WidgetMap _byWidget = new();
        private readonly List<VRCard> _all = new();
        internal VRCard CreateBlank() { var card = new VRCard(); _all.Add(card); return card; }
    }
}
