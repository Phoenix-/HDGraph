using HDGraph.Geometry;

namespace HDGraph.Tests;

public sealed class SunburstAngleTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(359)]
    public void ToAngleAndToPointRoundTrip(double angle)
    {
        var (x, y) = SunburstLayout.ToPoint(angle, 1);
        var roundTripped = SunburstLayout.ToAngle(x, y);

        Assert.True(
            Math.Abs(roundTripped - angle) <= Tolerance,
            $"Expected {angle} but round-tripped to {roundTripped}.");
    }

    [Fact]
    public void ToAngleMatchesClockFaceConvention()
    {
        // 0 degrees is 12 o'clock (straight up, negative Y); angle grows clockwise.
        AssertClose(0, SunburstLayout.ToAngle(0, -1));
        AssertClose(90, SunburstLayout.ToAngle(1, 0));
        AssertClose(180, SunburstLayout.ToAngle(0, 1));
        AssertClose(270, SunburstLayout.ToAngle(-1, 0));
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance,
            $"Expected {expected} but was {actual}.");
}
