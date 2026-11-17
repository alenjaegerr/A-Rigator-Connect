using System.Net.Http.Headers;
using System.Text.Json;

namespace A_Rigator_Controller.Services;

public sealed class FirmwareSourceUpdateResult
{
    public bool IsFirstCheck { get; init; }
    public bool HasChanged { get; init; }
    public string CommitSha { get; init; } = string.Empty;
    public string ShortSha => CommitSha.Length > 7 ? CommitSha[..7] : CommitSha;
    public string CommitMessage { get; init; } = string.Empty;
    public DateTimeOffset? CommitDate { get; init; }
    public string CommitUrl { get; init; } = string.Empty;
}

public sealed class FirmwareSourceUpdateService
{
    private const string RepositoryOwner = "alenjaegerr";
    private const string RepositoryName = "A-Rigator-Connect";
    private const string Branch = "feature/arigator-unified";
    private const string FirmwarePath = "Firmware/A-RigatorBot/A-RigatorBot.ino";
    private const string StoredShaKey = "FirmwareSourceLastSeenSha";

    private readonly HttpClient _httpClient;

    public FirmwareSourceUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FirmwareSourceUpdateResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var endpoint =
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/commits" +
            $"?path={Uri.EscapeDataString(FirmwarePath)}" +
            $"&sha={Uri.EscapeDataString(Branch)}&per_page=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("ARigatorConnect/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("GitHub did not return a firmware source commit.");
        }

        var latest = document.RootElement[0];
        var sha = latest.GetProperty("sha").GetString();

        if (string.IsNullOrWhiteSpace(sha))
            throw new InvalidOperationException("GitHub returned an invalid firmware source revision.");

        var commit = latest.GetProperty("commit");
        var message = commit.GetProperty("message").GetString() ?? "Firmware source updated";
        var dateText = commit.GetProperty("author").GetProperty("date").GetString();
        DateTimeOffset? date = DateTimeOffset.TryParse(dateText, out var parsedDate)
            ? parsedDate
            : null;
        var url = latest.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? string.Empty
            : string.Empty;

        var previousSha = Preferences.Default.Get(StoredShaKey, string.Empty);
        var isFirstCheck = string.IsNullOrWhiteSpace(previousSha);
        var hasChanged = !isFirstCheck && !string.Equals(previousSha, sha, StringComparison.Ordinal);

        // Record only after a successful GitHub response.
        Preferences.Default.Set(StoredShaKey, sha);

        return new FirmwareSourceUpdateResult
        {
            IsFirstCheck = isFirstCheck,
            HasChanged = hasChanged,
            CommitSha = sha,
            CommitMessage = message.Split('\n', 2)[0],
            CommitDate = date,
            CommitUrl = url
        };
    }
}
