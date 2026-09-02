using System.Reflection;

namespace HDGraph.App;

/// <summary>Build facts stamped into the assembly; the version comes from the nearest git tag via MinVer.</summary>
internal static class AppInfo
{
    /// <summary>
    /// "0.2.0" for a tagged build, "0.2.1-alpha.0.3" three commits after the tag; the "+sha" build metadata is stripped.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return "dev";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
