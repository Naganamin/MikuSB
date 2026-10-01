namespace MikuSB.Util;

public static class RequiredFileCheck
{
    public const string ResourceRepository = "https://github.com/Kei-Luna/MikuSB-Resource";
    public const string PatchRepository = "https://github.com/Kei-Luna/MikuSB-Patch";
    public const string InGameConsoleRepository = "https://github.com/Kei-Luna/MikuSB-inGame-GUI-Console";

    private static readonly string[] RequiredResourceFiles =
    [
        "item/templates/card.json",
        "item/templates/weapon.json"
    ];

    public static void EnsureResourcesPresent()
    {
        var resourcePath = ConfigManager.Config.Path.ResourcePath;
        var missing = RequiredResourceFiles
            .Select(fileName => Path.GetFullPath(Path.Combine(resourcePath, fileName)))
            .Where(path => !File.Exists(path))
            .ToList();
        if (missing.Count == 0)
            return;

        throw new FileNotFoundException(
            $"Game resources not found: {string.Join(", ", missing)}. " +
            $"Clone {ResourceRepository} and set Path.ResourcePath in Config.json to its absolute path.",
            missing[0]);
    }

    public static void EnsurePresent(IEnumerable<string> paths, string description, string sourceRepository)
    {
        var missing = paths.Where(path => !File.Exists(path)).ToList();
        if (missing.Count == 0)
            return;

        throw new FileNotFoundException(
            $"{description} not found: {string.Join(", ", missing)}. " +
            $"Build it from your fork of {sourceRepository} and copy it there.",
            missing[0]);
    }
}
