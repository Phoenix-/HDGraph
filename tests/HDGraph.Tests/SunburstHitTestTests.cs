using HDGraph.Geometry;
using HDGraph.Tests.TestSupport;

namespace HDGraph.Tests;

public sealed class SunburstHitTestTests
{
    [Fact]
    public async Task HitTestResolvesCentreArcsAndMisses()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var layout = SunburstLayout.Build(root, radius: 300, new SunburstLayoutOptions { Rings = 3 });

        // Inside the centre disc: always the root, regardless of angle.
        Assert.Same(root, layout.HitTest(10, 0));

        var a = root.Children.Single(static c => c.Name == "a");
        var b = root.Children.Single(static c => c.Name == "b");
        var aArc = layout.Arcs.Single(arc => arc.Ring == 1 && arc.Node == a);
        var bArc = layout.Arcs.Single(arc => arc.Ring == 1 && arc.Node == b);

        // Middle of a's ring-1 arc.
        var (ax, ay) = SunburstLayout.ToPoint(aArc.MidAngle, 110);
        Assert.Same(a, layout.HitTest(ax, ay));

        // Same physical point, but the picture is rotated 90 degrees clockwise: querying the point that is
        // now where a's midpoint used to be (i.e. rotated the other way) must still resolve to a.
        var (rx, ry) = SunburstLayout.ToPoint(aArc.MidAngle + 90, 110);
        Assert.Same(a, layout.HitTest(rx, ry, rotationDegrees: 90));

        // Beyond the outer edge of the outermost ring.
        var (fx, fy) = SunburstLayout.ToPoint(0, 400);
        Assert.Null(layout.HitTest(fx, fy));

        // In the gap left, within ring 1, by the root's own file (which has no arc of its own): any angle
        // strictly between the end of the last arc and 360 degrees.
        var gapAngle = (aArc.SweepAngle + bArc.SweepAngle + 360) / 2;
        var (gx, gy) = SunburstLayout.ToPoint(gapAngle, 100);
        Assert.Null(layout.HitTest(gx, gy));
    }
}
