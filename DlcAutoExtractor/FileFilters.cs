namespace DlcAutoExtractor;

public static class FileFilters
{
    // Focused on map editing; users can select ALL to preserve any extension.
    private static readonly HashSet<string> MapResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ymap", ".ytyp", ".ydr", ".ydd", ".ytd", ".ybn", ".yft", ".ymf",
        ".ymt", ".ynd", ".ynv", ".ycd", ".ypt", ".ywr", ".yvr", ".yld",
        ".yed", ".yfd", ".yhd", ".meta", ".xml", ".dat"
    };

    public static bool ShouldExtract(string name, bool allFiles) =>
        !name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
        (allFiles || MapResourceExtensions.Contains(Path.GetExtension(name)));
}
