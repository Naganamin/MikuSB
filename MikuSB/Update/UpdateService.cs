using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.MikuSB.Update;

/// <summary>Warns when upstream has commits that the locally built components don't include. Never downloads.</summary>
public static class UpdateService
{
    private static readonly Logger Logger = new("Updater");
    private const string UpstreamBranch = "main";
    private const int RequestTimeoutSeconds = 10;

    private sealed record Component(string Name, string Repository, Func<string?> ReadBuiltCommit, string CommitSource);

    public static async Task CheckUpstreamAsync()
    {
        using var client = CreateHttpClient();
        foreach (var component in GetComponents())
        {
            try
            {
                await CheckComponentAsync(client, component);
            }
            catch (Exception ex)
            {
                Logger.Warn($"{component.Name}: upstream check failed.", ex);
            }
        }
    }

    private static IEnumerable<Component> GetComponents()
    {
        var loader = ConfigManager.Config.Loader;
        var patchPath = (loader.PatchPaths ?? [])
            .FirstOrDefault(x => Path.GetFileName(x).Equals("MikuSB-Patch.dll", StringComparison.OrdinalIgnoreCase));
        var resourcePath = Path.GetFullPath(ConfigManager.Config.Path.ResourcePath);

        yield return new("MikuSB", "MikuLeaks/MikuSB", () => BuildVersion.SourceCommit, "the build metadata");
        if (patchPath is not null)
        {
            var commitFile = Path.ChangeExtension(ResolveFromBase(patchPath), ".commit");
            yield return new("MikuSB-Patch", "Kei-Luna/MikuSB-Patch", () => ReadCommitFile(commitFile), commitFile);
        }
        if (loader.EnableInGameConsole)
        {
            var commitFile = Path.Combine(Path.GetDirectoryName(ResolveFromBase(loader.InGameConsoleLoaderPath))!,
                "MikuSB-InGame-GUI-Console.commit");
            yield return new("MikuSB-inGame-GUI-Console", "Kei-Luna/MikuSB-inGame-GUI-Console",
                () => ReadCommitFile(commitFile), commitFile);
        }
        yield return new("MikuSB-Resource", "Kei-Luna/MikuSB-Resource", () => ReadGitHead(resourcePath),
            Path.Combine(resourcePath, ".git"));
    }

    private static async Task CheckComponentAsync(HttpClient client, Component component)
    {
        var builtCommit = component.ReadBuiltCommit();
        if (builtCommit is null)
        {
            Logger.Warn($"{component.Name}: build commit unknown (checked {component.CommitSource}); skipping upstream check.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
        var url = $"https://api.github.com/repos/{component.Repository}/compare/{builtCommit}...{UpstreamBranch}";
        using var response = await client.GetAsync(url, cts.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Logger.Warn($"{component.Name}: commit {builtCommit} not found in {component.Repository}; cannot compare with upstream.");
            return;
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var compare = await JsonSerializer.DeserializeAsync<CompareResponse>(stream, cancellationToken: cts.Token)
            ?? throw new InvalidDataException("GitHub returned an empty compare response.");
        // ahead_by = upstream commits missing from the build; behind_by = local-only commits, ignored
        if (compare.AheadBy > 0)
            Logger.Warn($"{component.Name}: upstream {UpstreamBranch} is {compare.AheadBy} commit(s) ahead of your build: {compare.HtmlUrl}");
        else
            Logger.Info($"{component.Name}: up to date with upstream ({builtCommit[..7]}).");
    }

    private static string ResolveFromBase(string path) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    private static string? ReadCommitFile(string path) =>
        File.Exists(path) ? AsSha(File.ReadAllText(path)) : null;

    private static string? ReadGitHead(string repositoryPath)
    {
        var gitDirectory = Path.Combine(repositoryPath, ".git");
        var headPath = Path.Combine(gitDirectory, "HEAD");
        if (!File.Exists(headPath))
            return null;

        var head = File.ReadAllText(headPath).Trim();
        if (!head.StartsWith("ref:", StringComparison.Ordinal))
            return AsSha(head);

        var refName = head["ref:".Length..].Trim();
        var refPath = Path.Combine(gitDirectory, refName);
        if (File.Exists(refPath))
            return AsSha(File.ReadAllText(refPath));

        var packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
        if (!File.Exists(packedRefsPath))
            return null;

        return File.ReadLines(packedRefsPath)
            .Select(line => line.Split(' ', 2))
            .Where(parts => parts.Length == 2 && parts[1].Trim() == refName)
            .Select(parts => AsSha(parts[0]))
            .FirstOrDefault();
    }

    private static string? AsSha(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 40 && trimmed.All(Uri.IsHexDigit) ? trimmed.ToLowerInvariant() : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MikuSB-UpstreamCheck", BuildVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class CompareResponse
    {
        [JsonPropertyName("ahead_by")]
        public int AheadBy { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }
}
