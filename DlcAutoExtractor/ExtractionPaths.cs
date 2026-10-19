using System.Text.RegularExpressions;

namespace DlcAutoExtractor;

/// <summary>Pure output-path policy; contains no CodeWalker dependencies.</summary>
public static class ExtractionPaths
{
    private static readonly Regex InvalidWindowsNameChars = new("[<>:\"|?*\\x00-\\x1F]", RegexOptions.Compiled);
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
        "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6",
        "LPT7", "LPT8", "LPT9"
    };

    public static string MakeOutputRelativePath(string rpfEntryPath)
    {
        if (string.IsNullOrWhiteSpace(rpfEntryPath))
            throw new ArgumentException("The archive entry has no path.");

        if (rpfEntryPath.StartsWith('\\') || rpfEntryPath.StartsWith('/'))
            throw new InvalidDataException("Absolute archive entry path is forbidden.");

        string[] parts = rpfEntryPath.Split(new[] { '\\', '/' }, StringSplitOptions.None);
        if (parts.Length == 0) throw new InvalidDataException("Empty entry path.");

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            ValidateSegment(part);

            if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                part = part[..^4]; // dlc.rpf -> dlc, nested RPF works identically.
                ValidateSegment(part);
            }
            parts[i] = part;
        }

        return Path.Combine(parts);
    }

    public static string ResolveOutputFile(string outputRoot, string entryPath)
    {
        var root = Path.GetFullPath(outputRoot);
        var relative = MakeOutputRelativePath(entryPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive path escapes the output directory.");
        return candidate;
    }

    public static void ValidateDisjointFolders(string inputRoot, string outputRoot)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputRoot));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            IsChild(a, b) || IsChild(b, a))
        {
            throw new ArgumentException("Input and output folders must not overlap. Select a separate output folder.");
        }
    }

    private static bool IsChild(string parent, string child) => child.StartsWith(
        Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static void ValidateSegment(string part)
    {
        if (part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            InvalidWindowsNameChars.IsMatch(part) ||
            DeviceNames.Contains(part.Split('.')[0]))
            throw new InvalidDataException($"Invalid or unsafe archive path segment: {part}");
    }
}
