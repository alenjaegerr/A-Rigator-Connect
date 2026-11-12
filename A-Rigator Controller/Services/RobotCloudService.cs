using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Services;

public sealed record RobotCloudRegistration(
    string RobotId,
    string RobotName,
    bool AlreadyRegistered);

public interface IRobotCloudService
{
    Task<RobotCloudRegistration> RegisterRobotAsync(string hardwareId, string robotName, CancellationToken cancellationToken = default);
}

public sealed class RobotCloudService : IRobotCloudService
{
    private readonly HttpClient _http;
    private readonly IAccountService _account;
    private readonly ISupabaseConfigService _supabase;

    public RobotCloudService(HttpClient http, IAccountService account, ISupabaseConfigService supabase)
    {
        _http = http;
        _account = account;
        _supabase = supabase;
    }

    public Task<RobotCloudRegistration> RegisterRobotAsync(string hardwareId, string robotName, CancellationToken cancellationToken = default) =>
        InvokeAsync<RobotCloudRegistration>("register", new Dictionary<string, object?> { ["hardwareId"] = hardwareId, ["robotName"] = robotName }, cancellationToken);

    private async Task<T> InvokeAsync<T>(string action, object payload, CancellationToken cancellationToken)
    {
        if (!_account.IsAuthenticated || string.IsNullOrWhiteSpace(_account.AccessToken))
            throw new InvalidOperationException("Sign in to your A-Rigator account before enabling remote access.");
        if (!_supabase.IsConfigured || string.IsNullOrWhiteSpace(_supabase.Url) || string.IsNullOrWhiteSpace(_supabase.AnonKey))
            throw new InvalidOperationException("Supabase is not configured in Account settings.");

        var url = $"{_supabase.Url.TrimEnd('/')}/functions/v1/robot-mqtt-provisioning";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _account.AccessToken);
        request.Headers.Add("apikey", _supabase.AnonKey);
        var requestBody = new Dictionary<string, object?>(payload as Dictionary<string, object?> ?? new())
        {
            ["action"] = action
        };
        request.Content = JsonContent.Create(requestBody);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string message;
            try
            {
                using var document = JsonDocument.Parse(body);
                var error = document.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                message = error switch
                {
                    "robot_already_registered" => "This robot is already registered to another account. Contact support if ownership needs to be transferred.",
                    "emqx" => "Remote broker provisioning is not configured yet.",
                    _ => error ?? $"Remote setup failed ({(int)response.StatusCode})."
                };
            }
            catch { message = $"Remote setup failed ({(int)response.StatusCode})."; }
            throw new InvalidOperationException(message);
        }

        var result = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? throw new InvalidOperationException("The remote setup service returned an empty response.");
    }
}
