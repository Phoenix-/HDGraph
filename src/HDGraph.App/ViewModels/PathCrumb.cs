namespace HDGraph.App.ViewModels;

public enum CrumbState
{
    /// <summary>Part of the scanned tree: clicking centres the chart on it at once.</summary>
    Scanned,

    /// <summary>Being read by the scan that is running.</summary>
    Scanning,

    /// <summary>Above the scanned tree: clicking extends the scan up to it.</summary>
    Unscanned,
}

/// <summary>One segment of the path bar, from the drive root down to the folder in the centre of the chart.
/// The boundary between scanned and unscanned segments is where the scanned tree starts.</summary>
public sealed record PathCrumb(string Path, string Label, CrumbState State)
{
    public double Opacity => State == CrumbState.Scanned ? 1.0 : 0.55;

    /// <summary>What the breadcrumb bar shows: its items render their content as text.</summary>
    public override string ToString() => Label;

    public string Tooltip => State switch
    {
        CrumbState.Scanned => Path,
        CrumbState.Scanning => $"{Path}\nBeing scanned…",
        _ => $"{Path}\nNot scanned yet: click to extend the scan up to here",
    };
}
