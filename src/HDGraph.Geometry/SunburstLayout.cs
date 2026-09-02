using HDGraph.Core;

namespace HDGraph.Geometry;

/// <summary>One ring sector. Angles are degrees, 0 at 12 o'clock, growing clockwise; radii are in the same
/// unit the layout was built with (device-independent pixels in the app).</summary>
public readonly record struct Arc(
    DirectoryNode Node,
    int Ring,
    double StartAngle,
    double SweepAngle,
    double InnerRadius,
    double OuterRadius)
{
    public double EndAngle => StartAngle + SweepAngle;
    public double MidAngle => StartAngle + SweepAngle / 2;
    public double MidRadius => (InnerRadius + OuterRadius) / 2;

    /// <summary>Length of the arc through the middle of the ring: the room a label has.</summary>
    public double MidArcLength => MidRadius * SweepAngle * Math.PI / 180;
}

public sealed class SunburstLayoutOptions
{
    /// <summary>How many directory levels below the centre node are drawn.</summary>
    public int Rings { get; init; } = 5;

    /// <summary>Sectors narrower than this are not laid out (nor are their subtrees). Below a fraction of a
    /// degree a sector is thinner than a pixel on any screen and only costs draw calls.</summary>
    public double MinSweepDegrees { get; init; } = 0.15;

    /// <summary>Radius of the centre disc as a multiple of the ring thickness.</summary>
    public double CenterRadiusRatio { get; init; } = 1.0;
}

/// <summary>Sector geometry of a sunburst for a given root and radius. Pure arithmetic over the node tree:
/// no colours, no text, no rotation (the renderer rotates, the hit test un-rotates).</summary>
public sealed class SunburstLayout
{
    private readonly Arc[][] _rings;

    private SunburstLayout(DirectoryNode root, double radius, double centerRadius, double ringThickness, Arc[][] rings)
    {
        Root = root;
        Radius = radius;
        CenterRadius = centerRadius;
        RingThickness = ringThickness;
        _rings = rings;
        Arcs = rings.SelectMany(static r => r).ToArray();
    }

    public DirectoryNode Root { get; }
    public double Radius { get; }
    public double CenterRadius { get; }
    public double RingThickness { get; }

    /// <summary>All sectors, inner rings first, clockwise within a ring.</summary>
    public IReadOnlyList<Arc> Arcs { get; }

    public int RingCount => _rings.Length;

    public static SunburstLayout Build(DirectoryNode root, double radius, SunburstLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new SunburstLayoutOptions();
        if (options.Rings < 1) throw new ArgumentOutOfRangeException(nameof(options), "Rings must be at least 1.");
        if (radius <= 0) return new SunburstLayout(root, 0, 0, 0, []);

        var ringThickness = radius / (options.Rings + options.CenterRadiusRatio);
        var centerRadius = ringThickness * options.CenterRadiusRatio;

        var rings = new List<Arc>[options.Rings];
        for (var i = 0; i < rings.Length; i++) rings[i] = [];

        if (root.TotalSize > 0)
            LayoutChildren(root, 0, 360, ring: 1, rings, centerRadius, ringThickness, options);

        return new SunburstLayout(root, radius, centerRadius, ringThickness,
            rings.Select(static r => r.ToArray()).ToArray());
    }

    private static void LayoutChildren(
        DirectoryNode parent, double startAngle, double sweep, int ring,
        List<Arc>[] rings, double centerRadius, double ringThickness, SunburstLayoutOptions options)
    {
        if (ring > rings.Length || parent.TotalSize <= 0) return;

        var inner = centerRadius + (ring - 1) * ringThickness;
        var outer = inner + ringThickness;
        var angle = startAngle;
        foreach (var child in parent.Children)
        {
            var childSweep = sweep * child.TotalSize / parent.TotalSize;
            if (childSweep >= options.MinSweepDegrees)
            {
                rings[ring - 1].Add(new Arc(child, ring, angle, childSweep, inner, outer));
                if (child.Kind == NodeKind.Directory)
                    LayoutChildren(child, angle, childSweep, ring + 1, rings, centerRadius, ringThickness, options);
            }
            angle += childSweep;
        }
    }

    /// <summary>Which node is under a point given relative to the centre, with the picture rotated by
    /// <paramref name="rotationDegrees"/> clockwise. Returns the root for the centre disc, null for empty
    /// space and for anything outside the outer ring.</summary>
    public DirectoryNode? HitTest(double dx, double dy, double rotationDegrees = 0)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= CenterRadius) return Root;
        if (RingThickness <= 0) return null;

        var ringIndex = (int)((distance - CenterRadius) / RingThickness);
        if (ringIndex < 0 || ringIndex >= _rings.Length) return null;

        var angle = ToAngle(dx, dy) - rotationDegrees;
        angle %= 360;
        if (angle < 0) angle += 360;

        var arc = FindArc(_rings[ringIndex], angle);
        return arc?.Node;
    }

    /// <summary>Angle in this layout's convention (0 at 12 o'clock, clockwise) of a vector from the centre.</summary>
    public static double ToAngle(double dx, double dy)
    {
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return angle < 0 ? angle + 360 : angle;
    }

    /// <summary>Point at <paramref name="radius"/> along <paramref name="angleDegrees"/>, relative to the centre.</summary>
    public static (double X, double Y) ToPoint(double angleDegrees, double radius)
    {
        var rad = angleDegrees * Math.PI / 180;
        return (radius * Math.Sin(rad), -radius * Math.Cos(rad));
    }

    private static Arc? FindArc(Arc[] ring, double angle)
    {
        // Arcs in a ring are laid out clockwise, so StartAngle is sorted: binary-search the last start <= angle.
        int lo = 0, hi = ring.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (ring[mid].StartAngle <= angle)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found < 0) return null;
        var candidate = ring[found];
        return angle < candidate.EndAngle ? candidate : null;
    }
}
