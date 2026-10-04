using System;
using GloomhavenVR.Net.TownServices;

// The received-frame predicate, both independent-visitor classifiers, physical
// mesh classifier and cumulative delta expansion compile from production.
// These frames isolate presentation admission; they do not run native gameplay.
internal static class TemplePurseVisitorProof
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        { count++; if (!condition) throw new Exception(message); }
        foreach (int peer in new[] { 2, 7 })
        foreach (bool returning in new[] { false, true })
        {
            TownServiceFrame labelled = Frame("temple.row|", mesh: false);
            Check(BoundTemplePurseVisitor.AdmitsReceived(labelled, peer, returning)
                && BoundTemplePurseVisitor.IndependentVisitorModule(labelled, peer, returning),
                "another visitor retains the original labelled wrist purse root");
            TownServiceFrame held = Frame("ritual.purse.held|", mesh: true);
            Check(BoundTemplePurseVisitor.AdmitsReceived(held, peer, returning)
                && BoundTemplePurseVisitor.IndependentVisitorModule(held, peer, returning),
                "another visitor retains the original held purse body");
            TownServiceFrame baseline = Frame("ritual.purse|", mesh: true);
            Check(BoundTemplePurseVisitor.IndependentVisitorModule(baseline, peer, returning),
                "another visitor retains the original ordinary wrist purse body");
            TownServiceFrame current = TownServiceDelta.Copy(baseline);
            current.Sequence++; current.SampleTime += .1f;
            current.Pose[0] = .12f;
            current.Nodes[0].Values[TownServiceProperty.Group].Numbers[0] = .6f;
            TownServiceFrame delta = TownServiceDelta.Create(baseline, current);
            Check(delta.BaseSequence == baseline.Sequence && delta.Nodes.Length == 1
                && !delta.Nodes[0].Values.ContainsKey(TownServiceProperty.Mesh),
                "production cumulative purse delta omits unchanged original mesh artwork");
            Check(!BoundTemplePurseVisitor.IndependentVisitorModule(delta, peer, returning)
                && BoundTemplePurseVisitor.AdmitsReceived(delta, peer, returning),
                "cumulative purse delta reaches expansion without repeating its native mesh");
            TownServiceFrame? expanded = TownServiceDelta.Expand(baseline, delta);
            Check(expanded != null && expanded.Nodes[0].Values.ContainsKey(TownServiceProperty.Mesh)
                && BoundTemplePurseVisitor.IndependentVisitorModule(expanded, peer, returning)
                && expanded.Pose[0] == .12f
                && expanded.Nodes[0].Values[TownServiceProperty.Group].Numbers[0] == .6f,
                "expanded original purse retains both body admission and latest owner pose and opacity");
            current.ParentModule = 4;
            Check(BoundTemplePurseVisitor.IndependentVisitorModule(current, peer, returning),
                "deposited native purse body remains shared at its authored bowl parent");

            TownServiceFrame image = Frame("ritual.purse|", mesh: false);
            Check(!BoundTemplePurseVisitor.IndependentVisitorModule(image, peer, returning),
                "an image with the purse address cannot become a second shared bowl body");
            TownServiceFrame fadedImage = TownServiceDelta.Copy(image);
            fadedImage.Sequence++;
            fadedImage.Nodes[0].Values[TownServiceProperty.Group].Numbers[0] = .2f;
            TownServiceFrame imageDelta = TownServiceDelta.Create(image, fadedImage);
            Check(BoundTemplePurseVisitor.AdmitsReceived(imageDelta, peer, returning),
                "provisional purse-address admission does not pretend to know sparse delta artwork");
            TownServiceFrame? expandedImage = TownServiceDelta.Expand(image, imageDelta);
            Check(expandedImage != null
                && !BoundTemplePurseVisitor.IndependentVisitorModule(expandedImage, peer, returning),
                "expanded image cannot borrow independent purse body admission");
        }
        return count;
    }

    private static TownServiceFrame Frame(string address, bool mesh)
    {
        var node = new TownServiceNode { Binding = 1 };
        node.Values.Add(TownServiceProperty.Group, new TownServiceValue { Numbers = new[] { 1f } });
        if (mesh) node.Values.Add(TownServiceProperty.Mesh, new TownServiceValue { Text = new[] { "original.purse.mesh" } });
        return new TownServiceFrame { Service = 2, Session = 5, Sequence = 10,
            Module = 6, Template = 1, Structure = 19, TemplateAddress = address,
            Visible = true, Nodes = new[] { node },
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
    }
}
