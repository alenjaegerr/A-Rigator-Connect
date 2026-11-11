using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class IrrigationProfileCatalogService : IIrrigationProfileCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ISupabaseConfigService _configService;

    public IrrigationProfileCatalogService(HttpClient httpClient, ISupabaseConfigService configService)
    {
        _httpClient = httpClient;
        _configService = configService;
    }

    public async Task<IReadOnlyList<IrrigationProfile>> GetPublicProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_configService.IsConfigured ||
            string.IsNullOrWhiteSpace(_configService.Url) ||
            string.IsNullOrWhiteSpace(_configService.AnonKey))
        {
            throw new InvalidOperationException("Configure the Supabase project URL and public anon key first.");
        }

        var query = "rest/v1/irrigation_profiles" +
                    "?select=id,name,direction_mode,duration_seconds,left_duration_seconds,back_duration_seconds,right_duration_seconds,repeat_count" +
                    "&is_public=eq.true&order=created_at.desc";
        var requestUri = $"{_configService.Url.TrimEnd('/')}/{query}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", _configService.AnonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = string.IsNullOrWhiteSpace(responseBody)
                ? response.ReasonPhrase
                : responseBody;
            throw new HttpRequestException(
                $"Supabase returned {(int)response.StatusCode}: {detail}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PublicProfileRow>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? new List<PublicProfileRow>();

        return rows
            .Where(row => Guid.TryParse(row.Id, out _) &&
                          !string.IsNullOrWhiteSpace(row.Name) &&
                          IsSupportedDirection(row.DirectionMode))
            .Select(row => new IrrigationProfile
            {
                Id = row.Id,
                Name = row.Name.Trim(),
                DirectionMode = row.DirectionMode,
                DurationSeconds = Math.Clamp(row.DurationSeconds, 1, 60),
                LeftDurationSeconds = Math.Clamp(row.LeftDurationSeconds, 0, 60),
                BackDurationSeconds = Math.Clamp(row.BackDurationSeconds, 0, 60),
                RightDurationSeconds = Math.Clamp(row.RightDurationSeconds, 0, 60),
                RepeatCount = Math.Clamp(row.RepeatCount, 1, 20)
            })
            .ToList();
    }

    private static bool IsSupportedDirection(string? directionMode) =>
        directionMode is "LeftSweep" or "RightSweep" or "BackSweep" or "BothSides" or "FullSweep";

    private sealed class PublicProfileRow
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("direction_mode")]
        public string DirectionMode { get; set; } = string.Empty;

        [JsonPropertyName("duration_seconds")]
        public int DurationSeconds { get; set; }

        [JsonPropertyName("left_duration_seconds")]
        public int LeftDurationSeconds { get; set; }

        [JsonPropertyName("back_duration_seconds")]
        public int BackDurationSeconds { get; set; }

        [JsonPropertyName("right_duration_seconds")]
        public int RightDurationSeconds { get; set; }

        [JsonPropertyName("repeat_count")]
        public int RepeatCount { get; set; }
    }
}
