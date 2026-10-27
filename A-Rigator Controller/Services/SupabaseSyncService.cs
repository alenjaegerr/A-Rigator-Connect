
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace A_Rigator_Controller.Services;

public sealed class SupabaseSyncService : ISupabaseSyncService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAccountService _accountService;
    private readonly ISupabaseConfigService _supabaseConfigService;
    private readonly HttpClient _httpClient = new();

    public SupabaseSyncService(
        IAccountService accountService,
        ISupabaseConfigService supabaseConfigService)
    {
        _accountService = accountService;
        _supabaseConfigService = supabaseConfigService;
    }

    public bool IsCloudSyncEnabled =>
        _accountService.IsAuthenticated &&
        _supabaseConfigService.IsConfigured;

    public bool IsRemoteModeAvailable =>
        _accountService.IsRemoteModeAllowed &&
        _supabaseConfigService.IsConfigured;

    public async Task<bool> SyncUserProfileAsync(
        string userId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSession(userId, out var baseUrl, out var anonKey,
                out var accessToken, out var authenticatedUserId))
        {
            return false;
        }

        try
        {
            var profile = new
            {
                id = authenticatedUserId,
                email = _accountService.Email,
                display_name = _accountService.DisplayName ?? string.Empty,
                updated_at = DateTimeOffset.UtcNow
            };

            using var request = CreateRequest(
                HttpMethod.Post,
                $"{baseUrl}/rest/v1/profiles?on_conflict=id",
                anonKey,
                accessToken);

            request.Headers.TryAddWithoutValidation(
                "Prefer", "resolution=merge-duplicates,return=representation");

            request.Content = JsonContent.Create(profile);

            using var response = await _httpClient.SendAsync(
                request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            // Verify the row exists and belongs to this authenticated user.
            using var verifyRequest = CreateRequest(
                HttpMethod.Get,
                $"{baseUrl}/rest/v1/profiles?id=eq.{Uri.EscapeDataString(authenticatedUserId)}&select=id,email,display_name",
                anonKey,
                accessToken);

            using var verifyResponse = await _httpClient.SendAsync(
                verifyRequest, cancellationToken);

            if (!verifyResponse.IsSuccessStatusCode)
                return false;

            var rows = await verifyResponse.Content
                .ReadFromJsonAsync<List<ProfileRow>>(
                    JsonOptions, cancellationToken);

            return rows?.Any(row =>
                row.Id == authenticatedUserId &&
                row.Email == _accountService.Email) == true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public async Task<bool> SyncRobotProfileAsync(
        string robotId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSession(
                _accountService.GoogleUserId ?? string.Empty,
                out var baseUrl,
                out var anonKey,
                out var accessToken,
                out var authenticatedUserId) ||
            string.IsNullOrWhiteSpace(robotId))
        {
            return false;
        }

        string? robotName;

        try
        {
            using var document = JsonDocument.Parse(payload);

            robotName =
                document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(robotName))
            return false;

        try
        {
            // robotId must be a stable robot_code, not a made-up UUID.
            var robot = new
            {
                owner_id = authenticatedUserId,
                name = robotName.Trim(),
                robot_code = robotId.Trim(),
                connection_mode = "local",
                updated_at = DateTimeOffset.UtcNow
            };

            using var request = CreateRequest(
                HttpMethod.Post,
                $"{baseUrl}/rest/v1/robots?on_conflict=robot_code",
                anonKey,
                accessToken);

            request.Headers.TryAddWithoutValidation(
                "Prefer", "resolution=merge-duplicates,return=representation");

            request.Content = JsonContent.Create(robot);

            using var response = await _httpClient.SendAsync(
                request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            using var verifyRequest = CreateRequest(
                HttpMethod.Get,
                $"{baseUrl}/rest/v1/robots?robot_code=eq.{Uri.EscapeDataString(robotId.Trim())}&owner_id=eq.{Uri.EscapeDataString(authenticatedUserId)}&select=id,owner_id,robot_code,name",
                anonKey,
                accessToken);

            using var verifyResponse = await _httpClient.SendAsync(
                verifyRequest, cancellationToken);

            if (!verifyResponse.IsSuccessStatusCode)
                return false;

            var rows = await verifyResponse.Content
                .ReadFromJsonAsync<List<RobotRow>>(
                    JsonOptions, cancellationToken);

            return rows?.Any(row =>
                row.OwnerId == authenticatedUserId &&
                row.RobotCode == robotId.Trim()) == true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private bool TryGetSession(
        string requestedUserId,
        out string baseUrl,
        out string anonKey,
        out string accessToken,
        out string authenticatedUserId)
    {
        _supabaseConfigService.LoadFromPreferences();

        baseUrl = (_supabaseConfigService.Url ?? string.Empty).TrimEnd('/');
        anonKey = _supabaseConfigService.AnonKey ?? string.Empty;
        accessToken = _accountService.AccessToken ?? string.Empty;
        authenticatedUserId = _accountService.GoogleUserId ?? string.Empty;

        if (!_accountService.IsAuthenticated ||
            !_supabaseConfigService.IsConfigured ||
            string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(authenticatedUserId) ||
            !string.Equals(
                requestedUserId,
                authenticatedUserId,
                StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Host != "localhost" &&
             uri.Host != "10.0.2.2"))
        {
            return false;
        }

        return true;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string anonKey,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, url);

        request.Headers.TryAddWithoutValidation("apikey", anonKey);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    private sealed class ProfileRow
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class RobotRow
    {
        public string? OwnerId { get; set; }
        public string? RobotCode { get; set; }
    }
}
