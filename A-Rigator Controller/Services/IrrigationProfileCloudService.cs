using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class IrrigationProfileCloudService : IIrrigationProfileCloudService
{
    private readonly HttpClient _http;
    private readonly ISupabaseConfigService _config;
    private readonly IAccountService _account;

    public IrrigationProfileCloudService(HttpClient http, ISupabaseConfigService config, IAccountService account)
    {
        _http = http;
        _config = config;
        _account = account;
    }

    public async Task<IReadOnlyList<IrrigationProfile>> DownloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var url = _config.Url!.TrimEnd('/') +
            "/rest/v1/irrigation_profiles?select=id,name,direction_mode,duration_seconds,left_duration_seconds,back_duration_seconds,right_duration_seconds,repeat_count&order=updated_at.desc";

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Profile download failed ({(int)response.StatusCode}): {body}", null, response.StatusCode);

        var rows = JsonSerializer.Deserialize<List<CloudProfileRow>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        return rows.Where(x => Guid.TryParse(x.Id, out _) && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new IrrigationProfile
            {
                Id = Guid.Parse(x.Id).ToString("N"),
                Name = x.Name.Trim(),
                DirectionMode = x.DirectionMode,
                DurationSeconds = Math.Clamp(x.DurationSeconds, 1, 60),
                LeftDurationSeconds = Math.Clamp(x.LeftDurationSeconds, 0, 60),
                BackDurationSeconds = Math.Clamp(x.BackDurationSeconds, 0, 60),
                RightDurationSeconds = Math.Clamp(x.RightDurationSeconds, 0, 60),
                RepeatCount = Math.Clamp(x.RepeatCount, 1, 20)
            }).ToList();
    }

    public async Task SyncProfilesAsync(IEnumerable<IrrigationProfile> profiles, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var userId = _account.GoogleUserId;
        if (!Guid.TryParse(userId, out var ownerId))
            throw new InvalidOperationException("The signed-in account did not return a valid user ID.");

        var rows = profiles.Select(profile =>
        {
            profile.ApplyDirectionMode();
            return new CloudProfileWriteRow
            {
                Id = Guid.Parse(profile.Id).ToString(),
                OwnerId = ownerId.ToString(),
                Name = profile.Name.Trim(),
                DirectionMode = profile.DirectionMode,
                DurationSeconds = profile.DurationSeconds,
                LeftDurationSeconds = profile.LeftDurationSeconds,
                BackDurationSeconds = profile.BackDurationSeconds,
                RightDurationSeconds = profile.RightDurationSeconds,
                RepeatCount = profile.RepeatCount,
                IsPublic = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }).ToList();

        if (rows.Count == 0) return;

        var url = _config.Url!.TrimEnd('/') + "/rest/v1/irrigation_profiles?on_conflict=id";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(rows), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Profile sync failed ({(int)response.StatusCode}): {body}", null, response.StatusCode);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("apikey", _config.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _account.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureAuthenticated()
    {
        if (!_account.IsAuthenticated || string.IsNullOrWhiteSpace(_account.AccessToken))
            throw new InvalidOperationException("Sign in to your account to synchronize irrigation profiles.");
    }

    private sealed class CloudProfileRow
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("direction_mode")] public string DirectionMode { get; set; } = "FullSweep";
        [JsonPropertyName("duration_seconds")] public int DurationSeconds { get; set; } = 3;
        [JsonPropertyName("left_duration_seconds")] public int LeftDurationSeconds { get; set; } = 3;
        [JsonPropertyName("back_duration_seconds")] public int BackDurationSeconds { get; set; } = 3;
        [JsonPropertyName("right_duration_seconds")] public int RightDurationSeconds { get; set; } = 3;
        [JsonPropertyName("repeat_count")] public int RepeatCount { get; set; } = 1;
    }

    private sealed class CloudProfileWriteRow
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("owner_id")] public string OwnerId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("direction_mode")] public string DirectionMode { get; set; } = "";
        [JsonPropertyName("duration_seconds")] public int DurationSeconds { get; set; }
        [JsonPropertyName("left_duration_seconds")] public int LeftDurationSeconds { get; set; }
        [JsonPropertyName("back_duration_seconds")] public int BackDurationSeconds { get; set; }
        [JsonPropertyName("right_duration_seconds")] public int RightDurationSeconds { get; set; }
        [JsonPropertyName("repeat_count")] public int RepeatCount { get; set; }
        [JsonPropertyName("is_public")] public bool IsPublic { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    }
}
