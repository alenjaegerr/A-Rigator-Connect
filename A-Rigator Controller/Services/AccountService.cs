
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;

namespace A_Rigator_Controller.Services;

public sealed class AccountService : IAccountService
{
    private const string RedirectUri =
        "com.companyname.arigatorcontroller://oauth2redirect";

    private readonly HttpClient _httpClient = new();
    private readonly ISupabaseConfigService _supabaseConfigService;
    private readonly IBackendConfigService _backendConfigService;
    private readonly ISecureSessionStorage _secureSessionStorage;

    public AccountService(
        ISupabaseConfigService supabaseConfigService,
        IBackendConfigService backendConfigService,
        ISecureSessionStorage secureSessionStorage)
    {
        _supabaseConfigService = supabaseConfigService;
        _backendConfigService = backendConfigService;
        _secureSessionStorage = secureSessionStorage;

        _supabaseConfigService.LoadFromPreferences();
        _backendConfigService.LoadFromPreferences();
    }

    public bool IsAuthenticated { get; private set; }
    public string? GoogleUserId { get; private set; }
    public string? DisplayName { get; private set; }
    public string? RobotName { get; private set; }
    public string? Email { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    public bool IsRemoteModeAllowed =>
        IsAuthenticated &&
        _supabaseConfigService.IsConfigured &&
        _backendConfigService.IsConfigured;

    public async Task SignInWithGoogleAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_supabaseConfigService.IsConfigured)
        {
            throw new InvalidOperationException(
                "Configure the Supabase URL and anon key in Account before signing in.");
        }

        var authUrl = BuildSupabaseGoogleAuthUrl();

        var result = await WebAuthenticator.Default.AuthenticateAsync(
            new WebAuthenticatorOptions
            {
                Url = new Uri(authUrl),
                CallbackUrl = new Uri(RedirectUri),
                PrefersEphemeralWebBrowserSession = false
            });

        cancellationToken.ThrowIfCancellationRequested();

        // WebAuthenticatorResult exposes parsed callback parameters directly.
        // Do not call ToString() or try to parse the result as a URI.
        var accessToken = result?.AccessToken;
        var refreshToken = result?.RefreshToken;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "The sign-in callback did not include an access token. Check the Supabase redirect configuration and OAuth response type.");
        }

        // Validate the token and user before committing the new session.
        var user = await GetSupabaseUserAsync(
            accessToken, cancellationToken);

        await _secureSessionStorage.SaveAsync(
            accessToken, refreshToken, cancellationToken);

        AccessToken = accessToken;
        RefreshToken = refreshToken;
        GoogleUserId = user.id;
        DisplayName = user.name;
        Email = user.email;
        AvatarUrl = user.avatarUrl;
        IsAuthenticated = true;
        await LoadCloudAccountSettingsAsync(cancellationToken);
    }

    public async Task InitializeSessionAsync(
        CancellationToken cancellationToken = default)
    {
        await RefreshStateAsync(cancellationToken);
    }

    public async Task RefreshStateAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await _secureSessionStorage.LoadAsync(cancellationToken);

        AccessToken = session.AccessToken;
        RefreshToken = session.RefreshToken;

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            ClearAccountState();
            return;
        }

        try
        {
            var user = await GetSupabaseUserAsync(
                AccessToken, cancellationToken);

            GoogleUserId = user.id;
            DisplayName = user.name;
            Email = user.email;
            AvatarUrl = user.avatarUrl;
            IsAuthenticated = true;
            await LoadCloudAccountSettingsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ClearAccountState();
            await _secureSessionStorage.ClearAsync(cancellationToken);
            throw;
        }
    }

    public async Task SignOutAsync(
        CancellationToken cancellationToken = default)
    {
        ClearAccountState();
        await _secureSessionStorage.ClearAsync(cancellationToken);
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(AccessToken))
            throw new InvalidOperationException("Sign in before deleting your account.");

        var baseUrl = _supabaseConfigService.Url?.TrimEnd('/');
        var anonKey = _supabaseConfigService.AnonKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(anonKey))
            throw new InvalidOperationException("Supabase configuration is missing.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl}/functions/v1/account-management");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Headers.TryAddWithoutValidation("apikey", anonKey);
        request.Content = new StringContent("{\"action\":\"delete_account\"}", Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Account deletion failed ({(int)response.StatusCode}): {body}",
                null, response.StatusCode);

        await SignOutAsync(cancellationToken);
    }

    public async Task<string> UploadProfileAvatarAsync(Stream image, string contentType, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(GoogleUserId))
            throw new InvalidOperationException("Sign in with Google to back up your profile picture.");

        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new InvalidOperationException("Choose a JPG, PNG, or WEBP image.");

        using var memory = new MemoryStream();
        await image.CopyToAsync(memory, cancellationToken);
        if (memory.Length is 0 or > 5 * 1024 * 1024)
            throw new InvalidOperationException("Profile pictures must be smaller than 5 MB.");
        memory.Position = 0;

        var baseUrl = _supabaseConfigService.Url?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(_supabaseConfigService.AnonKey))
            throw new InvalidOperationException("Supabase configuration is missing.");

        var extension = contentType switch { "image/png" => "png", "image/webp" => "webp", _ => "jpg" };
        var objectPath = $"{GoogleUserId}/avatar.{extension}";
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl}/storage/v1/object/profile-avatars/{Uri.EscapeDataString(GoogleUserId)}/avatar.{extension}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Headers.TryAddWithoutValidation("apikey", _supabaseConfigService.AnonKey);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        request.Content = new StreamContent(memory);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Profile picture upload failed ({(int)response.StatusCode}): {body}", null, response.StatusCode);

        AvatarUrl = $"{baseUrl}/storage/v1/object/public/profile-avatars/{GoogleUserId}/avatar.{extension}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return AvatarUrl;
    }

    public async Task SaveAccountSettingsAsync(
        string displayName,
        string robotName,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(AccessToken) ||
            string.IsNullOrWhiteSpace(GoogleUserId) || string.IsNullOrWhiteSpace(Email))
            throw new InvalidOperationException("Sign in before saving account settings.");

        displayName = displayName.Trim();
        robotName = robotName.Trim();
        if (displayName.Length is < 1 or > 48)
            throw new ArgumentException("Display name must contain 1–48 characters.", nameof(displayName));
        if (robotName.Length is < 1 or > 32)
            throw new ArgumentException("Robot name must contain 1–32 characters.", nameof(robotName));

        var baseUrl = _supabaseConfigService.Url?.TrimEnd('/');
        var anonKey = _supabaseConfigService.AnonKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(anonKey))
            throw new InvalidOperationException("Supabase configuration is missing.");

        async Task SendAsync(HttpMethod method, string endpoint, object payload, string? prefer = null)
        {
            using var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            request.Headers.TryAddWithoutValidation("apikey", anonKey);
            request.Headers.Accept.ParseAdd("application/json");
            if (!string.IsNullOrWhiteSpace(prefer))
                request.Headers.TryAddWithoutValidation("Prefer", prefer);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Could not save account settings ({(int)response.StatusCode}): {body}",
                    null, response.StatusCode);
            }
        }

        // The profiles row is created by the existing auth.users trigger.
        await SendAsync(HttpMethod.Post,
            "/rest/v1/profiles?on_conflict=id",
            new[] { new { id = GoogleUserId, email = Email, display_name = displayName } },
            "resolution=merge-duplicates,return=minimal");

        // A user may sign in before registering a robot. In that case, only the
        // personal profile is updated; robot settings are saved after registration.
        using (var check = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl}/rest/v1/robots?select=id&owner_id=eq.{Uri.EscapeDataString(GoogleUserId)}&limit=1"))
        {
            check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            check.Headers.TryAddWithoutValidation("apikey", anonKey);
            using var response = await _httpClient.SendAsync(check, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Could not check robot registration ({(int)response.StatusCode}): {body}", null, response.StatusCode);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
            {
                await SendAsync(HttpMethod.Patch,
                    $"/rest/v1/robots?owner_id=eq.{Uri.EscapeDataString(GoogleUserId)}",
                    new { name = robotName },
                    "return=minimal");
            }
        }

        DisplayName = displayName;
        RobotName = robotName;
    }

    private async Task LoadCloudAccountSettingsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(GoogleUserId) || string.IsNullOrWhiteSpace(AccessToken)) return;
        var baseUrl = _supabaseConfigService.Url?.TrimEnd('/');
        var anonKey = _supabaseConfigService.AnonKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(anonKey)) return;

        async Task<JsonDocument?> GetRowsAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            request.Headers.TryAddWithoutValidation("apikey", anonKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        try
        {
            using var profile = await GetRowsAsync($"/rest/v1/profiles?select=display_name&id=eq.{Uri.EscapeDataString(GoogleUserId)}&limit=1");
            if (profile?.RootElement.ValueKind == JsonValueKind.Array && profile.RootElement.GetArrayLength() > 0 &&
                profile.RootElement[0].TryGetProperty("display_name", out var displayName) &&
                !string.IsNullOrWhiteSpace(displayName.GetString()))
                DisplayName = displayName.GetString();

            using var robot = await GetRowsAsync($"/rest/v1/robots?select=name&owner_id=eq.{Uri.EscapeDataString(GoogleUserId)}&limit=1");
            if (robot?.RootElement.ValueKind == JsonValueKind.Array && robot.RootElement.GetArrayLength() > 0 &&
                robot.RootElement[0].TryGetProperty("name", out var robotName) &&
                !string.IsNullOrWhiteSpace(robotName.GetString()))
                RobotName = robotName.GetString();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* Keep OAuth sign-in available if profile restoration is temporarily unavailable. */ }
    }

    private void ClearAccountState()
    {
        IsAuthenticated = false;
        GoogleUserId = null;
        DisplayName = null;
        RobotName = null;
        Email = null;
        AvatarUrl = null;
        AccessToken = null;
        RefreshToken = null;
    }

    private string BuildSupabaseGoogleAuthUrl()
    {
        var baseUrl = _supabaseConfigService.Url!.TrimEnd('/');
        var redirect = Uri.EscapeDataString(RedirectUri);

        return $"{baseUrl}/auth/v1/authorize?provider=google&redirect_to={redirect}";
    }

    private async Task<SupabaseUserInfo> GetSupabaseUserAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "No Supabase access token was provided.");
        }

        var baseUrl = _supabaseConfigService.Url;

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(_supabaseConfigService.AnonKey))
        {
            throw new InvalidOperationException(
                "Supabase configuration is missing.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}/auth/v1/user");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add("apikey", _supabaseConfigService.AnonKey);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase user lookup failed: {(int)response.StatusCode} {response.ReasonPhrase}. Check the session and Supabase configuration.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // Standard Supabase responses return the user object at the root.
        // Also accept a nested "user" object if one is returned.
        var userJson =
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("user", out var nestedUser) &&
            nestedUser.ValueKind == JsonValueKind.Object
                ? nestedUser
                : root;

        var id = userJson.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;

        var email = userJson.TryGetProperty("email", out var emailElement)
            ? emailElement.GetString()
            : null;

        string? displayName = null;
        string? avatarUrl = null;

        if (userJson.TryGetProperty("user_metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("full_name", out var fullName))
                displayName = fullName.GetString();

            if (string.IsNullOrWhiteSpace(displayName) &&
                metadata.TryGetProperty("name", out var name))
                displayName = name.GetString();

            if (metadata.TryGetProperty("avatar_url", out var avatar)) avatarUrl = avatar.GetString();
            if (string.IsNullOrWhiteSpace(avatarUrl) && metadata.TryGetProperty("picture", out var picture)) avatarUrl = picture.GetString();
        }

        displayName = string.IsNullOrWhiteSpace(displayName)
            ? email ?? "Google User"
            : displayName;

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "Supabase returned incomplete user information.");
        }

        return new SupabaseUserInfo(id, displayName, email, avatarUrl);
    }

    private sealed record SupabaseUserInfo(
        string id,
        string name,
        string email,
        string? avatarUrl);
}
