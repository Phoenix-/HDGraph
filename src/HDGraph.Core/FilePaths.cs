namespace HDGraph.Core;

/// <summary>Path arithmetic the scanner and the UI agree on: one normal form, one comparison rule.</summary>
public static class FilePaths
{
    /// <summary>How paths are compared: case-insensitively on Windows, exactly elsewhere.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Absolute path without a trailing separator, except a root ("C:\", "/"), which keeps it.
    /// Throws like <see cref="Path.GetFullPath(string)"/> for malformed input.</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return IsRoot(full) ? full : Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>True for a drive or filesystem root such as "C:\" or "/".</summary>
    public static bool IsRoot(string fullPath) =>
        string.Equals(Path.GetPathRoot(fullPath), fullPath, Comparison);

    /// <summary>What a node for this path is called: the last segment; for a drive root the drive without its
    /// separator ("C:", as Explorer shows it); for a root that is nothing but a separator ("/") the root itself.</summary>
    public static string DisplayName(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        if (!string.IsNullOrEmpty(name)) return name;
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? fullPath : trimmed;
    }

    public static bool PathsEqual(string a, string b) => string.Equals(a, b, Comparison);

    /// <summary>Parent of a normalized path, or null at a root.</summary>
    public static string? GetParent(string fullPath) => Path.GetDirectoryName(fullPath);

    /// <summary>Whether <paramref name="path"/> lies strictly below <paramref name="ancestor"/>, both normalized.
    /// A bare prefix is not enough: "C:\Program" is not an ancestor of "C:\Program Files".</summary>
    public static bool IsStrictAncestor(string ancestor, string path)
    {
        if (path.Length <= ancestor.Length || !path.StartsWith(ancestor, Comparison)) return false;
        // A root already ends with its separator; any other ancestor must be followed by one.
        return IsSeparator(ancestor[^1]) || IsSeparator(path[ancestor.Length]);
    }

    public static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
