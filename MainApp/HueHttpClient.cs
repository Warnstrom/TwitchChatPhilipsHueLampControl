using System.Net.Http.Json;
using System.Text.Json;
using HueApi.BridgeLocator;
using HueApi;
using HueApi.Models.Requests;
using HueApi.ColorConverters;
using Spectre.Console;
using Microsoft.Extensions.Configuration;
using HueApi.Entertainment;
using HueApi.Entertainment.Models;
using HueApi.ColorConverters.Original.Extensions;
using HueApi.Models;

namespace TwitchChatHueControls;

public interface IHueController : IDisposable
{
    Task DiscoverBridgeAsync(); // Discovers available Hue bridges
    Task<bool> TryRegisterApplicationAsync(string appName, string deviceName); // Registers the application with the Hue bridge
    Task<bool> StartPollingForLinkButtonAsync(string appName, string deviceName, string bridgeIp, string appKey); // Starts polling for the link button press on the Hue bridge
    Task GetLightsAsync(); // Retrieves the available lights from the Hue bridge
    Task SetLampColorAsync(string lamp, RGBColor color); // Sets the color of a specific lamp
    Task AlternatingLampColors(string lampIdentifier, RGBColor color);
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

        return await _pollingTaskCompletionSource.Task; // Wait for polling to complete
    }

    public async Task SetupHueStreaming()
    {
        try
        {
            // Retrieve the StreamingHueClientKey from your configuration
            string streamingHueClientKey = await jsonController.GetValueByKeyAsync<string>("HueStreamingClientKey");
            if (string.IsNullOrEmpty(streamingHueClientKey))
            {
                Console.WriteLine("[Error] HueStreamingClientKey is missing or invalid.");
                return;
            }

            // Initialize the StreamingHueClient with the necessary configuration
            var client = new StreamingHueClient(configuration["bridgeIp"], configuration["AppKey"], streamingHueClientKey);

            // Get the entertainment groups
            var entertainmentGroups = await client.LocalHueApi.GetEntertainmentGroups();
            var group = entertainmentGroups?.Data.FirstOrDefault();

            // Handle the case where no entertainment group is found
            if (group == null)
            {
                Console.WriteLine("[Error] No entertainment group found.");
                return;
            }

            // Create the streaming group with the fetched group channels
            var entGroup = new StreamingGroup(group.Channels);

            // Attempt to connect to the streaming group
            client.ConnectAsync(group.Id);

            Console.WriteLine("[Info] Connected successfully. Starting auto-update...");

            // Start auto-updating the entertainment group
            _ = client.AutoUpdateAsync(entGroup, CancellationToken.None);

            // Optionally, log that the auto-update has started
            Console.WriteLine("[Info] Auto-update started for the entertainment group.");
        }
        catch (Exception ex)
        {
            // Log any unexpected exceptions
            Console.WriteLine($"[Error] An exception occurred: {ex.Message}");
        }
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
        if (_lightMap.TryGetValue(GetLampName(lampIdentifier), out var lightId))
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


    public async Task AlternatingLampColors(string lampIdentifier, RGBColor color)
    {
        // Check if the lamp exists in the light map
        if (_lightMap.TryGetValue(GetLampName(lampIdentifier), out var lightId))
        {
            // Create the update command to set the color
            UpdateLight req = new()
            {
                Signaling = new SignalingUpdate
                {
                    Signal = Signal.alternating,
                    Duration = 60000,
                    Colors = new List<HueApi.Models.Color>
                    {
                        new HueApi.Models.Color { Xy = new XyPosition { X = 0.456, Y = 0.123, }},
                        new HueApi.Models.Color { Xy = new XyPosition { X = 0.333, Y = 0.712, }}
                    }
                }
            };
            UpdateLight req1 = new()
            {
                Effects = new Effects
                {
                    Effect = Effect.prism,
                }
            };
            HuePutResponse res = await _hueClient.UpdateLightAsync(lightId, req1); // Send the command to the Hue bridge
            Console.WriteLine(res.Data.First().ToString());
            Console.WriteLine(res.Errors.First().Description);
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]Lamp '{lampIdentifier}' not found in the light map.[/]");
        }
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
