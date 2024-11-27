using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Spectre.Console;
using HueApi.ColorConverters;
using Microsoft.Extensions.Configuration;
namespace TwitchChatHueControls;

// Classes representing the event payload structure for subscribing to events.
public class SubscribeEventPayload
{
    public string? type { get; set; }         // Type of event to subscribe to.
    public string? version { get; set; }      // Version of the event subscription.
    public Condition? condition { get; set; } // Conditions required for the event.
    public Transport? transport { get; set; } // Transport details for event subscription.
}
public record Condition(string? broadcaster_user_id, string? user_id);
public record Transport(string? method, string? session_id);
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
        var eventPayload = new SubscribeEventPayload
        {
            type = "channel.channel_points_custom_reward_redemption.add", // Event type for channel points redemption.
            version = "1",
            condition = new Condition
            (
                configuration["ChannelId"], null
            ),
            transport = new Transport
            (
                "websocket",
                sessionId
            )
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to stream online notifications.
    private async Task SubscribeToStreamOnlineNotificationsAsync(string sessionId)
    {
        var eventPayload = new SubscribeEventPayload
        {
            type = "stream.online", // Event type for stream online notification.
            version = "1",
            condition = new Condition(configuration["ChannelId"], null),
            transport = new Transport("websocket", sessionId)
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to stream offline notifications.
    private async Task SubscribeToStreamOfflineNotificationsAsync(string sessionId)
    {
        var eventPayload = new SubscribeEventPayload
        {
            type = "stream.offline", // Event type for stream offline notification.
            version = "1",
            condition = new Condition(configuration["ChannelId"], null),
            transport = new Transport("websocket", sessionId)
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to chat messages for local testing (not used in production).
    private async Task SubscribeToChannelChatMessageAsync(string sessionId)
    {
        var eventPayload = new SubscribeEventPayload
        {
            type = "channel.chat.message", // Event type for chat messages.
            version = "1",
            condition = new Condition(configuration["ChannelId"], configuration["ChannelId"]),
            transport = new Transport("websocket", sessionId)
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Method to send a subscription request to the Twitch API.
    private async Task SendMessageAsync(SubscribeEventPayload eventPayload)
    {
        string payload = JsonConvert.SerializeObject(eventPayload);
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
                    await messageBuffer.WriteAsync(buffer, 0, result.Count); // Write the received data to the buffer.
                                                                             // If this is the last fragment, process the full message.
                    if (result.EndOfMessage)
                    {
                        messageBuffer.Seek(0, SeekOrigin.Begin); // Reset the stream position.
                        var payloadJson = await ReadMessageAsync(messageBuffer); // Read the full message as a JSON string.
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

    // Helper method to read the full message from the message buffer.
    private static async Task<string> ReadMessageAsync(Stream messageBuffer)
    {
        using var reader = new StreamReader(messageBuffer, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // Method to handle event notifications received from the WebSocket.
    private async Task HandleEventNotificationAsync(string payloadJson)
    {
        var payload = JObject.Parse(payloadJson); // Parse the JSON payload.
        string MessageType = (string)payload["metadata"]["message_type"]; // Extract the message type.

        // Dictionary mapping message types to their respective handler methods.
        var handlers = new Dictionary<string, Func<JObject, Task>>
        {
            { "session_welcome", HandleSessionWelcomeAsync },
            { "session_keepalive", HandleKeepAliveAsync },
            { "session_reconnect", HandleReconnectAsync },
            { "notification", HandleNotificationAsync },
        };

        // Check if a handler exists for the received message type.
        if (handlers.TryGetValue(MessageType, out var handler))
        {
            await handler(payload); // Invoke the appropriate handler.
        }
        else
        {
            Console.WriteLine("Unhandled message type: " + MessageType); // Log unhandled message types.
        }
    }

    // Method to handle the "session_reconnect" message type.
    private async Task HandleReconnectAsync(JObject payload)
    {
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
            string reconnectUrl = (string)payload["payload"]["session"]["reconnect_url"];

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
    private async Task HandleSessionWelcomeAsync(JObject payload)
    {
        string sessionId = (string)payload["payload"]["session"]["id"]; // Extract the session ID.

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

    // Method to handle notifications received from Twitch.
    private async Task HandleNotificationAsync(JObject payload)
    {
        try
        {
            // Extract the event type from the payload.
            string eventType = payload["payload"]["subscription"]["type"].ToString();

            // Determine how to handle the event based on its type.
            switch (eventType)
            {
                case "channel.channel_points_custom_reward_redemption.add":
                    await HandleCustomRewardRedemptionAsync(payload);
                    break;
                case "channel.chat.message":
                    if (argsService.Args.FirstOrDefault() == "dev")
                    {
                        string ChatterUserName = payload["payload"]["event"]["chatter_user_name"].ToString();
                        string ChatterInput = payload["payload"]["event"]["message"]["text"].ToString();
                        if (ChatterInput.Length < 10)
                        {
                            await HandleColorCommandAsync("left", CleanUserInput(ChatterInput), ChatterUserName);
                        }
                    }
                    break;
                case "stream.online":
                    await HandleStreamOnlineNotificationAsync(payload);
                    break;
                case "stream.offline":
                    await HandleStreamOfflineNotificationAsync(payload);
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
    private async Task HandleStreamOnlineNotificationAsync(JObject payload)
    {
        string StreamerUsername = payload["payload"]["event"]["broadcaster_user_name"].ToString(); // Get the broadcaster's username.
        Console.WriteLine($"{StreamerUsername} went live!"); // Log the stream online event.
    }

    // Method to handle the "stream.offline" event type.
    private async Task HandleStreamOfflineNotificationAsync(JObject payload)
    {
        string StreamerUsername = payload["payload"]["event"]["broadcaster_user_name"].ToString(); // Get the broadcaster's username.
        Console.WriteLine($"{StreamerUsername} went offline!"); // Log the stream offline event.
    }

    // Method to handle custom reward redemptions from Twitch.
    private async Task HandleCustomRewardRedemptionAsync(JObject payload)
    {
        string RewardTitle = payload["payload"]["event"]["reward"]["title"].ToString();
        string UserInput = payload["payload"]["event"]["user_input"].ToString();
        string RedeemUsername = payload["payload"]["event"]["user_name"].ToString();

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

        string errorMessageJson = JsonConvert.SerializeObject(errorMessage);
        var response = await twitchHttpClient.PostAsync("ChatMessage", errorMessageJson);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to send message. Status code: {response.StatusCode}"); // Log failure to send message.
        }
    }

    // Handle the "session_keepalive" event type (currently does nothing).
    private Task HandleKeepAliveAsync(JObject payload)
    {
        return Task.CompletedTask;
    }

    // Dispose of the WebSocket when finished.
    private void DisposeWebSocket()
    {
        _webSocket?.Dispose();
        _webSocket = null;
    }
}