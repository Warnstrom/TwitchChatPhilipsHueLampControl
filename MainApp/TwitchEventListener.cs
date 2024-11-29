using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;
using HueApi.ColorConverters;
using Microsoft.Extensions.Configuration;
using TwitchChatHueControls.Models;

namespace TwitchChatHueControls;

// Classes representing the event payload structure for subscribing to events.

// Interface for the Twitch EventSub listener.
internal interface ITwitchEventSubListener
{
    Task ValidateAndConnectAsync(Uri websocketUrl);        // Connect to the websocket server.
    Task ListenForEventsAsync();                           // Listen for incoming events.
}

// Implementation of the Twitch EventSub listener.
internal class TwitchEventSubListener(IConfiguration configuration, TwitchLib.Api.TwitchAPI api,
IJsonFileController jsonFileController, ArgsService argsService, ITwitchHttpClient twitchHttpClient,
IHexColorMapDictionary hexColorMapDictionary, IHueController hueController) : ITwitchEventSubListener
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly Regex ValidHexCodePattern = new Regex("([0-9a-fA-F]{6})$"); // Regex pattern to validate hex color codes.
    private ClientWebSocket? _webSocket;                                         // Web socket for connecting to Twitch EventSub.
                                                                                 // Method to connect to the Twitch EventSub websocket.
    public async Task ValidateAndConnectAsync(Uri websocketUrl)
    {
        // Step 1: Ensure the OAuth token is valid.
        await EnsureValidAccessTokenAsync();

        // Step 2: Initialize and connect the WebSocket.
        await InitializeAndConnectWebSocketAsync(websocketUrl);

        AnsiConsole.MarkupLine("[bold green]Successfully connected to Twitch Redemption Service[/]");
    }
    // Validates or refreshes the OAuth token if needed.
    private async Task EnsureValidAccessTokenAsync()
    {
        var tokenValidationResult = await api.Auth.ValidateAccessTokenAsync();
        if (tokenValidationResult == null)
        {
            string refreshToken = configuration["RefreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new InvalidOperationException("OAuth token is invalid, and no refresh token is available.");
            }
            AnsiConsole.Markup("[yellow]AccessToken is invalid, refreshing for a new token...[/]\n");
            var refresh = await api.Auth.RefreshAuthTokenAsync(refreshToken, configuration["ClientSecret"], configuration["ClientId"]);
            if (refresh == null || string.IsNullOrEmpty(refresh.AccessToken))
            {
                throw new InvalidOperationException("Failed to refresh the OAuth token.");
            }
            // Update the configuration with the new token.
            api.Settings.AccessToken = refresh.AccessToken;
            await jsonFileController.UpdateAsync("AccessToken", refresh.AccessToken);
            await twitchHttpClient.UpdateOAuthToken(refresh.AccessToken);
        }
    }

    // Initializes and connects the WebSocket.
    private async Task InitializeAndConnectWebSocketAsync(Uri websocketUrl)
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Client-Id", configuration["ClientId"]);
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {api.Settings.AccessToken}");
        _webSocket.Options.SetRequestHeader("Content-Type", "application/json");

        await _webSocket.ConnectAsync(websocketUrl, CancellationToken.None);
    }

    // Subscribe to channel point reward redemptions.
    private async Task SubscribeToChannelPointRewardsAsync(string sessionId)
    {
        Condition condition = new(configuration["ChannelId"], null);
        Transport transport = new("websocket", sessionId);
        SubscribeEventPayload eventPayload = new("channel.channel_points_custom_reward_redemption.add", "1", condition, transport);
        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to stream online notifications.
    private async Task SubscribeToStreamOnlineNotificationsAsync(string sessionId)
    {
        Condition condition = new(configuration["ChannelId"], null);
        Transport transport = new("websocket", sessionId);
        SubscribeEventPayload eventPayload = new("stream.online", "1", condition, transport);
        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to stream offline notifications.
    private async Task SubscribeToStreamOfflineNotificationsAsync(string sessionId)
    {
        Condition condition = new(configuration["ChannelId"], null);
        Transport transport = new("websocket", sessionId);
        SubscribeEventPayload eventPayload = new("stream.offline", "1", condition, transport);
        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to chat messages for local testing (not used in production).
    private async Task SubscribeToChannelChatMessageAsync(string sessionId)
    {
        Condition condition = new(configuration["ChannelId"], configuration["ChannelId"]);
        Transport transport = new("websocket", sessionId);
        SubscribeEventPayload eventPayload = new("channel.chat.message", "1", condition, transport);
        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Method to send a subscription request to the Twitch API.
    private async Task SendMessageAsync(SubscribeEventPayload eventPayload)
    {
        string payload = JsonSerializer.Serialize(eventPayload, _jsonSerializerOptions);
        try
        {
            HttpResponseMessage response = await twitchHttpClient.PostAsync("AddSubscription", payload);
            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold green]Successfully subscribed to Twitch Redemption Service Event:[/] [bold yellow]{eventPayload.type}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]Failed to subscribe to Twitch Redemption Service Event:[/] [bold yellow]{eventPayload.type}[/]");
                AnsiConsole.MarkupLine($"[bold teal]Reason:[/] [bold white]{response.StatusCode}[/]");
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("HTTP request exception: " + e.Message);
        }
    }

    // Method to listen for incoming events from the WebSocket connection.
    // Method to listen for incoming events from the WebSocket connection.
    public async Task ListenForEventsAsync()
    {
        const int maxBufferSize = 1024; // Buffer size for incoming WebSocket messages.
        var buffer = new byte[maxBufferSize]; // Buffer for temporarily holding received data.
        var messageBuffer = new MemoryStream(); // MemoryStream to store the full message across multiple fragments.

        try
        {
            while (_webSocket.State == WebSocketState.Open) // Continue reading messages while the WebSocket is open.
            {
                // Receive message from the WebSocket.
                WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                // Check if the received message is a close request.
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Display the close status and description.
                    Console.WriteLine(result.CloseStatus);
                    Console.WriteLine(result.CloseStatusDescription);
                    await AttemptReconnectAsync(); // Attempt to reconnect after closing.
                }

                // Handle text messages received from the WebSocket.
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await messageBuffer.WriteAsync(buffer.AsMemory(0, result.Count), CancellationToken.None);
                    // If this is the last fragment, process the full message.
                    if (result.EndOfMessage)
                    {
                        messageBuffer.Seek(0, SeekOrigin.Begin); // Reset the stream position.
                        string payloadJson = await ReadMessageAsync(messageBuffer); // Read the full message as a JSON string.
                        await HandleEventNotificationAsync(payloadJson); // Handle the parsed message.
                        messageBuffer.SetLength(0); // Clear the message buffer for the next message.
                    }
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Handle cases where the WebSocket connection closes unexpectedly.
            AnsiConsole.MarkupLine($"[bold red]Twitch Redemption Service connection closed prematurely.[/]");
            AnsiConsole.MarkupLine($"[bold yellow]Reason: {ex.Message}[/]");

            await AttemptReconnectAsync(); // Attempt to reconnect after the error.
        }
        catch (Exception ex)
        {
            // Log any other exceptions that occur.
            Console.WriteLine($"An error occurred while listening for events: {ex.Message}");
            Console.WriteLine($"{ex}");
        }
        finally
        {
            messageBuffer.Dispose(); // Dispose of the message buffer when finished.
        }
    }

    // Helper method to read the full message from the message buffer.
    private static async Task<string> ReadMessageAsync(Stream messageBuffer)
    {
        using var reader = new StreamReader(messageBuffer, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // Method to attempt reconnection to the WebSocket if the connection is lost.
    private async Task AttemptReconnectAsync()
    {
        const int maxAttempts = 5; // Maximum number of reconnection attempts.
        const int delayBetweenAttemptsMs = 2500; // Delay between each reconnection attempt (in milliseconds).

        for (int attempt = 0; attempt <= maxAttempts; attempt++)
        {
            try
            {
                AnsiConsole.MarkupLine($"[bold yellow]Attempting to reconnect... (Attempt {attempt}/{maxAttempts})[/]");

                // Determine which WebSocket URL to connect to (development or production).
                const string ws = "wss://eventsub.wss.twitch.tv/ws";
                const string localws = "ws://127.0.0.1:8080/ws";
                string wsstring = argsService.Args.FirstOrDefault() == "dev" ? localws : ws;

                await ValidateAndConnectAsync(new Uri(wsstring)); // Attempt connection.

                if (_webSocket.State == WebSocketState.Open)
                {
                    AnsiConsole.MarkupLine("[bold green]Reconnected successfully![/]");
                    await ListenForEventsAsync(); // Start listening for events again.
                    return;
                }
            }
            catch (Exception ex)
            {
                // Log any errors during the reconnection attempt.
                AnsiConsole.MarkupLine($"[bold red]Reconnection attempt failed: {ex.Message}[/]");
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayBetweenAttemptsMs); // Wait before attempting the next reconnection.
            }
        }

        // If all reconnection attempts fail, log an error message.
        AnsiConsole.MarkupLine("[bold red]Max reconnection attempts reached. Could not reconnect to WebSocket.[/]");
    }

    // Method to handle event notifications received from the WebSocket.
    private async Task HandleEventNotificationAsync(string payloadJson)
    {
        if (argsService.Args.FirstOrDefault() == "dev")
        {
            Console.WriteLine(payloadJson);
        }

        var message = JsonDocument.Parse(payloadJson);
        var metadata = message.RootElement.GetProperty("metadata");
        var messageType = metadata.GetProperty("message_type").GetString();
        // Dictionary mapping message types to their respective handler methods.
        switch (messageType)
        {
            case "session_welcome":
                await HandleSessionWelcomeAsync(message);
                break;
            case "session_keepalive":
                await HandleKeepAliveAsync(message);
                break;
            case "session_reconnect":
                await HandleReconnectAsync(message);
                break;
            case "notification":
                await HandleNotificationAsync(message);
                break;
        }
    }

    // Method to handle the "session_reconnect" message type.
    private async Task HandleReconnectAsync(JsonDocument payload)
    {
        var data = JsonSerializer.Deserialize<EventSubWebsocketSessionInfoMessage>(payload, _jsonSerializerOptions);

        try
        {
            // If the WebSocket is open, close it gracefully before reconnecting.
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
                AnsiConsole.MarkupLine("[bold yellow]Disconnecting from Twitch Redemption Service[/]");
                DisposeWebSocket(); // Dispose of the existing WebSocket.
            }
            // Extract the reconnect URL from the payload.
            string reconnectUrl = data.Payload.Session.ReconnectUrl;

            // Validate and connect using the new reconnect URL.
            if (Uri.TryCreate(reconnectUrl, UriKind.Absolute, out Uri? uri))
            {
                AnsiConsole.MarkupLine("[bold yellow]Reconnecting to Twitch Redemption Service[/]");
                await ValidateAndConnectAsync(uri);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error during reconnect: " + ex.Message); // Log errors during reconnection.
        }
    }

    // Method to handle the "session_welcome" message type.
    private async Task HandleSessionWelcomeAsync(JsonDocument payload)
    {
        var data = JsonSerializer.Deserialize<EventSubWebsocketSessionInfoMessage>(payload, _jsonSerializerOptions);
        string sessionId = data.Payload.Session.Id;

        if (argsService.Args.FirstOrDefault() == "dev")
        {
            Console.WriteLine(sessionId); // Log the session ID in development mode.
        }

        // Subscribe to various Twitch events using the session ID.
        await SubscribeToChannelPointRewardsAsync(sessionId);
        await SubscribeToStreamOnlineNotificationsAsync(sessionId);
        await SubscribeToStreamOfflineNotificationsAsync(sessionId);
        await SubscribeToChannelChatMessageAsync(sessionId);
    }
    private static string ExtractEventType(JsonDocument payload)
    {
        try
        {
            return payload.RootElement
                .GetProperty("payload")
                .GetProperty("subscription")
                .GetProperty("type").ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    // Method to handle notifications received from Twitch.
    private async Task HandleNotificationAsync(JsonDocument payload)
    {
        string eventType = ExtractEventType(payload);

        try
        {
            switch (eventType)
            {
                case "channel.channel_points_custom_reward_redemption.add":
                    Notification<ChannelPointsCustomRewardRedemptionAdd>? CustomRewardRedemptionAddPayload = JsonSerializer.Deserialize<Notification<ChannelPointsCustomRewardRedemptionAdd>>(payload, _jsonSerializerOptions);
                    await HandleCustomRewardRedemptionAsync(CustomRewardRedemptionAddPayload);
                    break;
                case "channel.chat.message":
                    var data = JsonSerializer.Deserialize<Notification<TwitchChatMessage>>(payload, _jsonSerializerOptions);
                    if (argsService.Args.FirstOrDefault() == "dev")
                    {
                        string ChatterUsername = data.Payload.Event.chatter_user_name;
                        string ChatterInput = data.Payload.Event.message.text;

                        if (ChatterInput.Length < 30)
                        {
                            var SplittedInput = ChatterInput.Split(' ');

                            if (SplittedInput[0] == "color")
                            {
                                await HandleColorCommandAsync("left", CleanUserInput(SplittedInput[1]), ChatterUsername);
                            }
                            else if (SplittedInput[0] == "effect")
                            {
                                HandleLampEffectsCommand("left", SplittedInput[1], ChatterUsername);
                            }
                        }
                    }
                    break;
                case "stream.online":
                    //await HandleStreamOnlineNotificationAsync(payload);
                    break;
                case "stream.offline":
                    //await HandleStreamOfflineNotificationAsync(payload);
                    break;
                default:
                    Console.WriteLine("Unhandled event type: " + eventType); // Log unhandled event types.
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error handling notification: " + ex.Message); // Log errors while handling notifications.
        }
    }

    // Method to handle the "stream.online" event type.
    private async Task HandleStreamOnlineNotificationAsync(Notification<StreamOnline>  payload)
    {
        string StreamerUsername = payload.Payload.Event.broadcaster_user_name;
        Console.WriteLine($"{StreamerUsername} went live!"); // Log the stream online event.
    }

    // Method to handle the "stream.offline" event type.
    private async Task HandleStreamOfflineNotificationAsync(Notification<StreamOffline>  payload)
    {
        string StreamerUsername = payload.Payload.Event.broadcaster_user_name;
        Console.WriteLine($"{StreamerUsername} went offline!"); // Log the stream offline event.
    }

    // Method to handle custom reward redemptions from Twitch.
    private async Task HandleCustomRewardRedemptionAsync(Notification<ChannelPointsCustomRewardRedemptionAdd> payload)
    {
        string RewardTitle = payload.Payload.Event.reward.title;
        string UserInput = payload.Payload.Event.user_input;
        string RedeemUsername = payload.Payload.Event.user_name;

        // Handle specific reward titles.
        switch (RewardTitle)
        {
            case "Change left lamp color":
                await HandleColorCommandAsync("left", CleanUserInput(UserInput), RedeemUsername);
                break;
            case "Change right lamp color":
                await HandleColorCommandAsync("right", CleanUserInput(UserInput), RedeemUsername);
                break;
            default:
                Console.WriteLine("Unknown command: " + RewardTitle); // Log unknown commands.
                break;
        }
    }

    // Method to clean up user input (e.g., remove special characters).
    private static string CleanUserInput(string userInput)
    {
        userInput = userInput.Trim().ToLower(); // Normalize input.

        if (userInput.Contains('#'))
        {
            userInput = userInput.Replace("#", ""); // Remove the hash symbol if present.
        }
        return userInput;
    }

    // Method to handle color commands (e.g., changing lamp colors).
    private async Task HandleColorCommandAsync(string lamp, string color, string RedeemUsername)
    {
        RGBColor finalColor = await GetColorAsync(color, RedeemUsername); // Resolve the color input.
        await hueController.SetLampColorAsync(lamp, finalColor); // Set the lamp color using the resolved RGB value.
    }

    private async void HandleLampEffectsCommand(string lamp, string effect, string RedeemUsername)
    {
        // Get all available effects
        var availableEffects = hueController.GetAllAvailableEffects()
                                             .Select(e => e.ToString().ToLowerInvariant())
                                             .ToList();
        // Check if the provided effect exists (case-insensitive)
        if (availableEffects.Contains(effect.ToLowerInvariant()) || effect.ToLowerInvariant() == "alternate")
        {
            hueController.SetLampEffect(lamp, effect.ToLowerInvariant(), "subscription");
        }
        else
        {
            await SendInvalidEffectMessageAsync(RedeemUsername, effect);
            Console.WriteLine($"Effect '{effect}' is not a valid option. Available effects are: {string.Join(", ", availableEffects)}.");
        }
    }


    // Method to resolve the color input into an RGB color.
    private async Task<RGBColor> GetColorAsync(string color, string RedeemUsername)
    {
        if (argsService.Args.FirstOrDefault() == "dev")
        {
            Console.WriteLine(color); // Log the requested color in development mode.
        }


        if (color.Equals("random"))
        {
            return RGBColor.Random(); // Return a random color if requested.
        }

        string? baseColor = await hexColorMapDictionary.Get(color); // Attempt to map the color name to a hex code.

        if (baseColor != null)
        {
            return new RGBColor(baseColor); // Return the mapped RGB color.
        }

        // Check if the input is a valid hex code.
        if (ValidHexCodePattern.IsMatch(color))
        {
            return new RGBColor(color); // Return the RGB color for the hex code.
        }

        await SendInvalidColorMessageAsync(RedeemUsername, color); // Notify the user in Twitch chat if the color is invalid.
        return RGBColor.Random(); // Default to a random color if the input is invalid.
    }

    // Method to send a message notifying the user of an invalid color input.
    private async Task SendInvalidColorMessageAsync(string RedeemUsername, string invalidColor)
    {
        var errorMessage = new
        {
            broadcaster_id = configuration["ChannelId"],
            sender_id = configuration["ChannelId"],
            message = $"@{RedeemUsername} Unfortunately it appears that '{invalidColor}' is not currently supported, or an invalid hex code was provided. Yuki chose a color for you instead. asecre3RacDerp",
        };

        string errorMessageJson = JsonSerializer.Serialize(errorMessage, _jsonSerializerOptions);
        var response = await twitchHttpClient.PostAsync("ChatMessage", errorMessageJson);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to send message. Status code: {response.StatusCode}"); // Log failure to send message.
        }
    }

    private async Task SendInvalidEffectMessageAsync(string RedeemUsername, string invalidEffect)
    {
        var errorMessage = new
        {
            broadcaster_id = configuration["ChannelId"],
            sender_id = configuration["ChannelId"],
            message = $"@{RedeemUsername} Unfortunately it appears that '{invalidEffect}' is not currently supported. Yuki chose a color for you instead. asecre3RacDerp",
        };

        string errorMessageJson = JsonSerializer.Serialize(errorMessage, _jsonSerializerOptions);
        var response = await twitchHttpClient.PostAsync("ChatMessage", errorMessageJson);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to send message. Status code: {response.StatusCode}"); // Log failure to send message.
        }
    }


    // Handle the "session_keepalive" event type (currently does nothing).
    private Task HandleKeepAliveAsync(JsonDocument payload)
    {
        // var data = JsonSerializer.Deserialize<EventSubWebsocketSessionKeepAlive>(payload, _jsonSerializerOptions);

        return Task.CompletedTask;
    }

    // Dispose of the WebSocket when finished.
    private void DisposeWebSocket()
    {
        _webSocket?.Dispose();
        _webSocket = null;
    }
}