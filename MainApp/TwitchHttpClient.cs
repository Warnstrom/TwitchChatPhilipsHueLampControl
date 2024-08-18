using System.Text;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

public interface ITwitchHttpClient
{
    Task<HttpResponseMessage> PostAsync(string type, string message);
    Task UpdateOAuthToken(string newToken = "");
}

public class TwitchHttpClient : ITwitchHttpClient
{
    private readonly Dictionary<string, string> TwitchTypeToUrlMap = new()
    {
        { "AddSubscription", "https://api.twitch.tv/helix/eventsub/subscriptions" },
        { "ChatMessage", "https://api.twitch.tv/helix/chat/messages" },
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private string _oauthToken;
     private readonly IConfiguration _configuration;
    private readonly TwitchLib.Api.TwitchAPI _api;

    public TwitchHttpClient(IConfiguration configuration, TwitchLib.Api.TwitchAPI api)
    {
        _configuration = configuration;
        _api = api;
        _clientId = configuration["ClientId"];
        _oauthToken = configuration["AccessToken"];
        _httpClient = new();
        _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _oauthToken);
    }

    public async Task UpdateOAuthToken(string newToken = "")
    {
        var response = await _api.Auth.RefreshAuthTokenAsync(_configuration["RefreshToken"], _configuration["ClientSecret"]);
        _oauthToken = string.IsNullOrEmpty(newToken) ? response.AccessToken : newToken;
        // Update the Authorization header dynamically when the token changes
        if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
        }
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _oauthToken);
    }

   public async Task<HttpResponseMessage> PostAsync(string type, string message)
    {
        try
        {
            if (TwitchTypeToUrlMap.TryGetValue(type, out string url))
            {
                HttpResponseMessage response = await _httpClient.PostAsync(url, new StringContent(message, Encoding.UTF8, "application/json"));

                // Handle token expiration scenario
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    AnsiConsole.MarkupLine("[bold yellow]OAuth token is invalid or expired. Attempting to refresh...[/]");

                    // Refresh the token
                    await UpdateOAuthToken();

                    // Retry the request with the new token
                    response = await _httpClient.PostAsync(url, new StringContent(message, Encoding.UTF8, "application/json"));
                }

                return response;
            }
            else
            {
                throw new ArgumentException($"The type '{type}' is not valid.");
            }
        }
        catch (HttpRequestException e)
        {
            AnsiConsole.MarkupLine($"{e}");
            AnsiConsole.MarkupLine($"HTTP request exception: {e.Message}");
            throw;
        }
    }
}
