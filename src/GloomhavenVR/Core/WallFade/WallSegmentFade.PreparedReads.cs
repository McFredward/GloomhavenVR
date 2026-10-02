using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // Preparation already takes each wall's live child list. Only reads taken on the
        // SAME frame as publication are reusable: earlier slices may have seen a regenerated,
        // reparented or activated subtree. No persistent hierarchy validity is assumed.
        // Preparation is read-only and the preceding commit phases restore renderer/material
        // state without editing these native subtrees, so no native Update can intervene.
        private readonly Dictionary<Component, List<MeshRenderer>> _preparedWallChildren = new(64);
        private readonly List<List<MeshRenderer>> _preparedWallChildPool = new(64);
        private int _preparedWallChildFrame = -1;
        private int _preparedWallChildListsUsed;
        private int _cyclePreparedChildReads;
        private int _cyclePreparedChildHits;

        private readonly Dictionary<Transform, StandingLabel> _standingLabels = new(128);
        private int _standingLabelsBuilt;

        private readonly struct StandingLabel
        {
            internal readonly string RootName;
            internal readonly string Why;
            internal readonly bool Figure;
            internal readonly string Text;
            internal StandingLabel(string rootName, bool figure, string why)
            {
                RootName = rootName;
                Figure = figure;
                Why = why;
                Text = "'" + rootName + "' " + why;
            }
        }

        // Why is already a per-unit immutable measured sentence. Cache the enclosing name
        // too, instead of formatting/allocating it for every child of that same unit. Check
        // the live name, arm and sentence so changing a diagnostic operand never uses stale text.
        private string StandingNamedWhy(Transform root, bool figure, string why)
        {
            string rootName = root.name;
            if (_standingLabels.TryGetValue(root, out StandingLabel cached)
                && cached.RootName == rootName && cached.Figure == figure && cached.Why == why)
                return cached.Text;
            var label = new StandingLabel(rootName, figure, why);
            _standingLabels[root] = label;
            _standingLabelsBuilt++;
            return label.Text;
        }

        private void ClearPreparedStandingLabels()
        {
            _standingLabels.Clear();
            _standingLabelsBuilt = 0;
        }

        private void NotePreparedWallChildren(Component anchor, List<MeshRenderer> renderers)
        {
            if (_preparedWallChildFrame != Time.frameCount)
                ClearPreparedWallChildren();
            _preparedWallChildFrame = Time.frameCount;
            if (_preparedWallChildren.TryGetValue(anchor, out List<MeshRenderer>? previous))
            {
                previous.Clear();
                previous.AddRange(renderers);
                return;
            }
            if (_preparedWallChildListsUsed == _preparedWallChildPool.Count)
                _preparedWallChildPool.Add(new List<MeshRenderer>(renderers.Count));
            List<MeshRenderer> saved = _preparedWallChildPool[_preparedWallChildListsUsed++];
            saved.AddRange(renderers);
            _preparedWallChildren.Add(anchor, saved);
            _cyclePreparedChildReads++;
        }

        private void ReadWallCacheChildren(Component anchor, List<MeshRenderer> destination)
        {
            destination.Clear();
            if (!TryPreparedWallChildren(anchor, destination))
                anchor.GetComponentsInChildren(includeInactive: false, destination);
        }

        private bool TryPreparedWallChildren(Component anchor, List<MeshRenderer> destination)
        {
            if (_preparedWallChildFrame != Time.frameCount
                || !_preparedWallChildren.TryGetValue(anchor, out List<MeshRenderer>? saved))
                return false;
            destination.AddRange(saved);
            _cyclePreparedChildHits++;
            return true;
        }

        private void ClearPreparedWallChildren()
        {
            _preparedWallChildren.Clear();
            for (int i = 0; i < _preparedWallChildListsUsed; i++)
                _preparedWallChildPool[i].Clear();
            _preparedWallChildListsUsed = 0;
            _preparedWallChildFrame = -1;
        }
    }
}
