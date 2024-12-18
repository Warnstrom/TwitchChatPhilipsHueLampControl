using System.Text;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
namespace TwitchChatHueControls;

internal interface ITwitchHttpClient
{
    Task<HttpResponseMessage> PostAsync(string type, string message);
    Task UpdateOAuthToken(string newToken = "");
    Task<bool> CheckIfStreamIsOnline();
}

internal class TwitchHttpClient : ITwitchHttpClient
{
    private readonly Dictionary<string, string> TwitchTypeToUrlMap = new()
    {
        { "AddSubscription", "https://api.twitch.tv/helix/eventsub/subscriptions" },
        { "ChatMessage", "https://api.twitch.tv/helix/chat/messages" },
        { "SearchChannels", "https://api.twitch.tv/helix/search/channels" },
        { "Streams", "https://api.twitch.tv/helix/streams" },
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private string _oauthToken;
    private readonly IConfiguration _configuration;
    private readonly IJsonFileController _jsonFileController;
    private readonly ArgsService _argsService;
    private readonly TwitchLib.Api.TwitchAPI _api;

    public TwitchHttpClient(IConfiguration configuration, TwitchLib.Api.TwitchAPI api,
    ArgsService argsService, IJsonFileController jsonFileController)
    {
        _configuration = configuration;
        _jsonFileController = jsonFileController;
        _api = api;
        _argsService = argsService;
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
        await _jsonFileController.UpdateAsync("AccessToken", _oauthToken);

    }

    private void LogRequestHeaders()
    {
        Console.WriteLine("Logging HTTP Request Headers:");

        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
    }

    public async Task<HttpResponseMessage> PostAsync(string type, string message)
    {
        try
        {
            if (_argsService.Args.Length != 0 && _argsService.Args[0] == "dev")
            {
                LogRequestHeaders();
                Console.WriteLine(message);
            }

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

    private async Task<string> GetAsync(string type, string query)
    {
        // Check if the provided type exists in the URL map
        if (!TwitchTypeToUrlMap.TryGetValue(type, out string url))
        {
            AnsiConsole.Markup($"[red]Error: The type '{type}' is not valid.[/]\n");
            return string.Empty; // Return an empty string if type is invalid
        }

        try
        {
            // Send the HTTP GET request
            HttpResponseMessage response = await _httpClient.GetAsync($"{url}{query}");

            // Check if the status code is a success (2xx)
            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                return content; // Return the content if the request is successful
            }
            else
            {
                AnsiConsole.Markup($"[red]Error: Failed to get a successful response. Status Code: {response.StatusCode}[/]\n");
                AnsiConsole.Markup($"[yellow]Consider checking the ChannelId value if it's valid[/]\n");
                return string.Empty; // Return an empty string on failure
            }
        }
        catch (HttpRequestException ex)
        {
            // Handle HTTP-related exceptions, e.g., network errors
            AnsiConsole.Markup($"[red]HTTP Error: {ex.Message}[/]\n");
            return string.Empty; // Return an empty string on network errors
        }
        catch (TaskCanceledException)
        {
            // Handle request timeouts or cancellations
            AnsiConsole.Markup("[red]Error: The request was canceled or timed out.[/]\n");
            return string.Empty; // Return an empty string on timeout
        }
        catch (Exception ex)
        {
            // Catch any other general exceptions
            AnsiConsole.Markup($"[red]Error: An unexpected error occurred. {ex.Message}[/]\n");
            return string.Empty; // Return an empty string for other errors
        }
    }


     public async Task<bool> CheckIfStreamIsOnline()
    {
        // Pattern matching with 'is' and string.IsNullOrEmpty
        if (_configuration["ChannelId"] is string channelId && !string.IsNullOrEmpty(channelId))
        {
            var content = await GetAsync("Streams", $"?user_id={channelId}");

            // Pattern matching with JsonObject and is operator
            return JsonSerializer.Deserialize<JsonObject>(content) is JsonObject json
                && json["data"] is JsonNode data
                && data.AsArray() is JsonArray array
                && array.Any();
        }
        return false;
    }

}
