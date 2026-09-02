using HDGraph.Geometry;
using HDGraph.Tests.TestSupport;

namespace HDGraph.Tests;

public sealed class SunburstLayoutTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public async Task RingsAndArcGeometryMatchNodeShares()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var layout = SunburstLayout.Build(root, radius: 300, new SunburstLayoutOptions { Rings = 3 });

        Assert.Equal(75, layout.CenterRadius);
        Assert.Equal(75, layout.RingThickness);

        var ring1 = layout.Arcs.Where(static arc => arc.Ring == 1).OrderBy(static arc => arc.StartAngle).ToArray();
        var ring2 = layout.Arcs.Where(static arc => arc.Ring == 2).OrderBy(static arc => arc.StartAngle).ToArray();

        var a = root.Children.Single(static c => c.Name == "a");
        var b = root.Children.Single(static c => c.Name == "b");
        var c = root.Children.Single(static c => c.Name == "c");
        var sub = a.Children.Single(static c => c.Name == "sub");

        // c has zero size: no arc for it anywhere.
        Assert.DoesNotContain(layout.Arcs, arc => arc.Node == c);

        var expectedRing1Sweep = (double)(a.TotalSize + b.TotalSize) / root.TotalSize * 360;
        AssertClose(expectedRing1Sweep, ring1.Sum(static arc => arc.SweepAngle));

        AssertNoOverlap(ring1);
        AssertNoOverlap(ring2);

        var aArc = Assert.Single(ring1, arc => arc.Node == a);
        AssertClose(0, aArc.StartAngle);
        Assert.Equal(75, aArc.InnerRadius);
        Assert.Equal(150, aArc.OuterRadius);

        var subArc = Assert.Single(ring2, arc => arc.Node == sub);
        AssertClose(aArc.StartAngle, subArc.StartAngle);
        AssertClose(aArc.SweepAngle * sub.TotalSize / a.TotalSize, subArc.SweepAngle);
        Assert.Equal(150, subArc.InnerRadius);
        Assert.Equal(225, subArc.OuterRadius);
    }

    [Fact]
    public async Task MinSweepDegreesFiltersNarrowArcs()
    {
        await using var tree = await ScannedTestTree.CreateAsync();
        var root = tree.Root;
        var options = new SunburstLayoutOptions { Rings = 3, MinSweepDegrees = 200 };
        var layout = SunburstLayout.Build(root, radius: 300, options);

        var a = root.Children.Single(static c => c.Name == "a");
        var sub = a.Children.Single(static c => c.Name == "sub");

        var expectedASweep = (double)a.TotalSize / root.TotalSize * 360;
        var expectedSubSweep = expectedASweep * sub.TotalSize / a.TotalSize;

        Assert.True(expectedASweep >= options.MinSweepDegrees);
        Assert.True(expectedSubSweep >= options.MinSweepDegrees);

        var ring1 = layout.Arcs.Where(static arc => arc.Ring == 1).ToArray();
        var ring2 = layout.Arcs.Where(static arc => arc.Ring == 2).ToArray();

        var aArc = Assert.Single(ring1);
        Assert.Equal(a, aArc.Node);
        AssertClose(expectedASweep, aArc.SweepAngle);

        var subArc = Assert.Single(ring2);
        Assert.Equal(sub, subArc.Node);
        AssertClose(expectedSubSweep, subArc.SweepAngle);
    }

    private static void AssertNoOverlap(Arc[] ring)
    {
        for (var i = 1; i < ring.Length; i++)
        {
            Assert.True(ring[i].StartAngle >= ring[i - 1].StartAngle);
            Assert.True(ring[i].StartAngle >= ring[i - 1].EndAngle - 1e-9);
        }
    }

    private static void AssertClose(double expected, double actual, double tolerance = Tolerance) =>
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} but was {actual} (tolerance {tolerance}).");
}
