
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

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
    public string? Email { get; private set; }
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
        IsAuthenticated = true;
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
            IsAuthenticated = true;
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

    private void ClearAccountState()
    {
        IsAuthenticated = false;
        GoogleUserId = null;
        DisplayName = null;
        Email = null;
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

        if (userJson.TryGetProperty("user_metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("full_name", out var fullName))
                displayName = fullName.GetString();

            if (string.IsNullOrWhiteSpace(displayName) &&
                metadata.TryGetProperty("name", out var name))
                displayName = name.GetString();
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

        return new SupabaseUserInfo(id, displayName, email);
    }

    private sealed record SupabaseUserInfo(
        string id,
        string name,
        string email);
}
