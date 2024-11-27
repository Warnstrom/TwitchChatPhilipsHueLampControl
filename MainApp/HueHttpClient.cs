using System.Net.Http.Json;
using System.Text.Json;
using HueApi.BridgeLocator;
using HueApi;
using HueApi.Models.Requests;
using HueApi.ColorConverters;
using Spectre.Console;
using Microsoft.Extensions.Configuration;
using HueApi.ColorConverters.Original.Extensions;
using HueApi.Models;

namespace TwitchChatHueControls;
internal enum ColorPalette
{
    Default,
    Subscription,
    Raid,
    Follow,
    Bits,
}

// Predefined XY color positions for common colors
internal static class Xy
{
    // Primary Colors
    internal static readonly XyPosition Red = new() { X = 0.6484, Y = 0.3309 };
    internal static readonly XyPosition Green = new() { X = 0.2857, Y = 0.6429 };
    internal static readonly XyPosition Blue = new() { X = 0.1546, Y = 0.0566 };
    internal static readonly XyPosition Yellow = new() { X = 0.4951, Y = 0.5048 };
    internal static readonly XyPosition Magenta = new() { X = 0.4324, Y = 0.2159 };
    internal static readonly XyPosition Cyan = new() { X = 0.1546, Y = 0.1547 };
    internal static readonly XyPosition Orange = new() { X = 0.5562, Y = 0.4084 };
    internal static readonly XyPosition Peach = new() { X = 0.4800, Y = 0.3700 };
    internal static readonly XyPosition Purple = new() { X = 0.3457, Y = 0.1700 };
    internal static readonly XyPosition Teal = new() { X = 0.2000, Y = 0.3300 };
    internal static readonly XyPosition SkyBlue = new() { X = 0.2000, Y = 0.2200 };
    internal static readonly XyPosition HotPink = new() { X = 0.4800, Y = 0.2600 };
    internal static readonly XyPosition LimeGreen = new() { X = 0.3000, Y = 0.6000 };
}
internal interface IHueController : IDisposable
{
    Task DiscoverBridgeAsync(); // Discovers available Hue bridges
    Task<bool> TryRegisterApplicationAsync(string appName, string deviceName); // Registers the application with the Hue bridge
    Task<bool> StartPollingForLinkButtonAsync(string appName, string deviceName, string bridgeIp, string appKey); // Starts polling for the link button press on the Hue bridge
    Task GetLightsAsync(); // Retrieves the available lights from the Hue bridge
    Task SetLampColorAsync(string lamp, RGBColor color); // Sets the color of a specific lamp
    void SetLampEffect(string lampIdentifier, string lampEffects, string alternatingEffectType);
    List<Effect> GetAllAvailableEffects();
}

// Implementation of the IHueController interface
internal class HueController(IJsonFileController jsonController, IConfiguration configuration) : IHueController
{
    // Service for discovering Hue bridges over the network
    private readonly IBridgeLocator _bridgeLocator = new HttpBridgeLocator();
    private readonly HttpClient _httpClient = new(); // HTTP client for making API requests
    private LocalHueApi _hueClient; // Client for interacting with the Hue bridge API
    private readonly Dictionary<string, Guid> _lightMap = new(); // Maps lamp names to their unique IDs
    private string _appKey; // Stores the app key used to authenticate with the Hue bridge
    private Timer _pollingTimer; // Timer for polling the link button status
    private TaskCompletionSource<bool> _pollingTaskCompletionSource; // Task completion source for waiting on the polling process

    public static readonly Dictionary<ColorPalette, List<XyPosition>> ColorPalettes = new Dictionary<ColorPalette, List<XyPosition>>
    {
        { ColorPalette.Default, new List<XyPosition> { Xy.Blue, Xy.Green, Xy.Red } },
        { ColorPalette.Subscription, new List<XyPosition> { Xy.Blue, Xy.Teal } },
        { ColorPalette.Bits, new List<XyPosition> { Xy.Green, Xy.Yellow } },
        { ColorPalette.Follow, new List<XyPosition> { Xy.Red, Xy.Blue } },
        { ColorPalette.Raid, new List<XyPosition> { Xy.Orange, Xy.Purple}}
    };

    // Discovers Hue bridges on the local network
    public async Task DiscoverBridgeAsync()
    {
        // Discover bridges within a 10-second window
        //IEnumerable<LocatedBridge>? bridges = await _bridgeLocator.LocateBridgesAsync(TimeSpan.FromSeconds(10));

        IEnumerable<LocatedBridge>? bridges = null;

        await AnsiConsole.Status()
            .StartAsync("Searching for Hue bridges...", async ctx =>
            {
                // Adjust these time spans as needed.
                var searchTimeout = TimeSpan.FromSeconds(10);
                var discoveryTimeout = TimeSpan.FromSeconds(60);

                // This is where the discovery happens with the spinner active.
                bridges = await HueBridgeDiscovery.CompleteDiscoveryAsync(searchTimeout, discoveryTimeout);
            });

        // Once the discovery is done, you can process the bridges.
        if (bridges != null)
        {
            foreach (var bridge in bridges)
            {
                AnsiConsole.MarkupLine($"[green]Discovered Bridge:[/] {bridge.BridgeId} - {bridge.IpAddress}");
                await jsonController.UpdateAsync("bridgeId", bridge.BridgeId);
                await jsonController.UpdateAsync("bridgeIp", bridge.IpAddress);
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[red]No bridges found.[/]");
        }
    }

    // Tries to register the application with the Hue bridge
    public async Task<bool> TryRegisterApplicationAsync(string appName, string deviceName)
    {
        // Ensure that the bridge IP is set before attempting registration
        if (string.IsNullOrEmpty(configuration["bridgeIp"]))
        {
            await DiscoverBridgeAsync();
        }

        // Prepare the payload for the registration request
        var payload = new { devicetype = $"{appName}#{deviceName}", generateclientkey = true };

        // Send a POST request to the Hue bridge to register the application
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync($"http://{configuration["bridgeIp"]}/api", payload);
        JsonElement responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(); // Parse the JSON response
        // If the registration is successful, extract and save the app key
        if (responseJson.ValueKind == JsonValueKind.Array && responseJson[0].TryGetProperty("success", out var success))
        {
            _appKey = success.GetProperty("username").GetString();
            var StreamingClientKey = success.GetProperty("clientkey").GetString();
            await jsonController.UpdateAsync("HueStreamingClientKey", StreamingClientKey);
            await jsonController.UpdateAsync("AppKey", _appKey);
            return true;
        }

        // If an error occurs, display the error message
        if (responseJson[0].TryGetProperty("error", out var error))
        {
            AnsiConsole.MarkupLine($"[bold red]{error.GetProperty("description").GetString()}[/]");
        }

        return false; // Registration failed
    }

    // Starts polling for the Hue bridge link button press
    public async Task<bool> StartPollingForLinkButtonAsync(string appName, string deviceName, string bridgeIp, string appKey)
    {
        _pollingTaskCompletionSource = new TaskCompletionSource<bool>(); // Used to signal when polling is complete

        // If no app key is provided, start polling for the link button press
        if (string.IsNullOrEmpty(appKey))
        {
            _pollingTimer = new Timer(async _ =>
            {
                // Try to register the application
                bool registered = await TryRegisterApplicationAsync(appName, deviceName);

                // If registration is successful, stop polling and initialize the Hue client
                if (registered)
                {
                    StopPolling(); // Stop the polling timer
                    _hueClient = new LocalHueApi(configuration["bridgeIp"], _appKey); // Initialize the Hue API client
                    AnsiConsole.MarkupLine($"[bold green]Successfully registered with the Hue Bridge[/] [bold yellow]({configuration["bridgeIp"]})[/]\n");
                    await GetLightsAsync(); // Fetch the lights available on the bridge
                    _pollingTaskCompletionSource.SetResult(true); // Signal that polling is complete
                }
                else
                {
                    AnsiConsole.MarkupLine("[bold yellow]Waiting for the link button to be pressed...[/]");
                }
            }, null, 0, 5000); // Poll every 5 seconds
        }
        else
        {
            // If the app key is already available, connect immediately
            _hueClient = new LocalHueApi(bridgeIp, appKey);
            //HueApi.Models.Clip.RegisterEntertainmentResult? t = await LocalHueApi.RegisterAsync(appName, deviceName, bridgeIp);
            AnsiConsole.MarkupLine($"[bold green]Successfully connected with Hue Bridge using predefined ip[/] [bold yellow]({bridgeIp})[/]\n");
            await GetLightsAsync();
            _pollingTaskCompletionSource.SetResult(true);
        }

        return await _pollingTaskCompletionSource.Task; // Wait for polling to complBaseEffect.ete
    }

    // Retrieves the available lights from the Hue bridge and displays them in a table
    public async Task GetLightsAsync()
    {
        //await SetupHueStreaming();
        _lightMap.Clear(); // Clear the existing light map
        var lights = await _hueClient.GetLightsAsync(); // Fetch lights from the bridge
        if (lights.Data.Any())
        {
            // Create a table to display the lights
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Spectre.Console.Color.Teal);

            table.AddColumn("Type");
            table.AddColumn(new TableColumn("Name").Centered());

            // Add each light to the table and update the light map
            foreach (var light in lights.Data)
            {
                table.AddRow(light.Type, light.Metadata.Name);
                _lightMap[light.Metadata.Name] = light.Id;
            }

            //AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.MarkupLine("[bold red]No lamps found.[/]");
        }
    }

    // Sets the color of a specific lamp
    public async Task SetLampColorAsync(string lampIdentifier, RGBColor color)
    {
        // Check if the lamp exists in the light map
        if (_lightMap.TryGetValue(GetLampName(lampIdentifier), out Guid lightId))
        {
            // Create the update command to set the color
            var command = new UpdateLight().SetColor(color);
            await _hueClient.UpdateLightAsync(lightId, command); // Send the command to the Hue bridge
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]Lamp '{lampIdentifier}' not found in the light map.[/]");
        }
    }
    private static UpdateLight CreateAlternatingEffect(
          ColorPalette palette = ColorPalette.Default,
          int durationMs = 5000)
    {
        // Use custom colors if provided, otherwise use the selected palette
        var colors = ColorPalettes[palette];

        // Ensure we have at least two colors
        if (colors == null || colors.Count < 2)
        {
            throw new ArgumentException("At least two colors are required for alternating effect");
        }

        return new UpdateLight
        {
            Signaling = new SignalingUpdate
            {
                Signal = Signal.alternating,
                Duration = durationMs,
                Colors = colors.Select(xyPos => new HueApi.Models.Color { Xy = xyPos }).ToList()
            }
        };
    }

    private async Task AlternatingEffect(Guid lightId, string alternatingEffectType)
    {
        ColorPalette palette = alternatingEffectType.ToLowerInvariant() switch
        {
            "default" => ColorPalette.Default,
            "subscription" => ColorPalette.Subscription,
            "raid" => ColorPalette.Raid,
            "bits" => ColorPalette.Bits,
            "follow" => ColorPalette.Follow,
            _ => ColorPalette.Default // Fallback to default if no match
        };

        var customUpdate = CreateAlternatingEffect(
            palette,
            durationMs: 6000
        );

        await SendLightUpdateAsync(lightId, customUpdate);
    }
    public async void SetLampEffect(string lampIdentifier, string lampEffects, string alternatingEffectType = null)
    {
        // Check if the lamp exists in the light map
        if (_lightMap.TryGetValue(GetLampName(lampIdentifier), out Guid lightId))
        {
            try
            {
                switch (lampEffects)
                {
                    case "prism":
                        PrismEffect(lightId);
                        break;
                    case "fire":
                        FireEffect(lightId);
                        break;
                    case "candle":
                        CandleEffect(lightId);
                        break;
                    case "alternate":
                        AlternatingEffect(lightId, alternatingEffectType);
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error updating lamp '{lampIdentifier}': {ex.Message}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]Lamp '{lampIdentifier}' not found in the light map.[/]");
        }
    }

    private void PrismEffect(Guid lightId)
    {
        UpdateLight prismEffectUpdate = CreateEffectUpdate(Effect.prism);
        SendLightUpdateAsync(lightId, prismEffectUpdate);
    }

    private void FireEffect(Guid lightId)
    {
        UpdateLight fireEffectUpdate = CreateEffectUpdate(Effect.fire);
        SendLightUpdateAsync(lightId, fireEffectUpdate);
    }

    private void CandleEffect(Guid lightId)
    {
        UpdateLight candleEffectUpdate = CreateEffectUpdate(Effect.candle);
        SendLightUpdateAsync(lightId, candleEffectUpdate);
    }

    private async Task<HuePutResponse> SendLightUpdateAsync(Guid lightId, UpdateLight update)
    {
        return await _hueClient.UpdateLightAsync(lightId, update);
    }

    public List<Effect> GetAllAvailableEffects()
    {
        return Enum.GetValues(typeof(Effect)).Cast<Effect>().ToList();
    }

    private UpdateLight CreateEffectUpdate(Effect effect)
    {
        return new UpdateLight
        {
            Effects = new Effects
            {
                Effect = effect
            }
        };
    }


    // Maps input identifiers (e.g., "left", "right") to specific lamp names
    private string GetLampName(string lamp) => lamp switch
    {
        "left" => "room_streaming_left_lamp",
        "right" => "room_streaming_right_lamp",
        _ => throw new ArgumentException("Invalid lamp identifier", nameof(lamp)),
    };

    // Stops the polling timer
    private void StopPolling()
    {
        _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer
        _pollingTimer?.Dispose(); // Dispose of the timer
        _pollingTimer = null;
    }

    // Disposes of resources used by the controller
    public void Dispose()
    {
        _httpClient.Dispose(); // Dispose of the HTTP client
        StopPolling(); // Stop and dispose of the polling timer if active
    }
}
