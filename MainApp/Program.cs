using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text;
using TwitchLib.Api.Core.Exceptions;
namespace TwitchChatHueControls;
public class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            await RunApplicationAsync(args);
        }
        catch (Exception ex)
        {
            DisplayErrorMessage(ex);
        }
        finally
        {
            PromptExit();
        }
    }

    private static async Task RunApplicationAsync(string[] args)
    {
        var (settingsFile, configFile) = ParseCommandLineArguments(args);
        await using var serviceProvider = CreateServiceProvider(args, settingsFile, configFile);
        await serviceProvider.GetRequiredService<App>().RunAsync();
    }
    private static (string SettingsFile, string ConfigFile) ParseCommandLineArguments(string[] args)
    {
        string settingsFile = args.FirstOrDefault() == "dev"
            ? "devmodesettings.json"
            : "appsettings.json";
        string configFile = args.Length == 2 && !args.Contains("local")
            ? args[1]
            : string.Empty;
        return (settingsFile, configFile);
    }
    private static ServiceProvider CreateServiceProvider(string[] args, string settingsFile, string configFile)
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, args, settingsFile, configFile);
        return serviceCollection.BuildServiceProvider();
    }
    private static void ConfigureServices(
        IServiceCollection services,
        string[] args,
        string settingsFile,
        string configFile)
    {
        var configurationRoot = BuildConfiguration(settingsFile, configFile);
        services.AddSingleton<IConfiguration>(configurationRoot);
        services.AddSingleton<IConfigurationRoot>(configurationRoot);
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton(new ArgsService(args));
        services.AddSingleton<IJsonFileController>(sp => new JsonFileController(string.IsNullOrEmpty(configFile) ? settingsFile : configFile, configurationRoot));
        services.AddSingleton<IHexColorMapDictionary>(new HexColorMapDictionary("colors.json", configurationRoot));
        services.AddSingleton<IHueController, HueController>();
        services.AddSingleton<ILampEffectQueueService, LampEffectQueueService>();
        services.AddSingleton<ITestLampEffectService, TestLampEffectService>();
        services.AddSingleton<TwitchLib.Api.TwitchAPI>();
        services.AddSingleton<IBridgeValidator, BridgeValidator>();
        services.AddScoped<ITwitchHttpClient, TwitchHttpClient>();
        services.AddTransient<IVersionUpdateService, VersionUpdateService>();
        services.AddSingleton<WebServer>();
        services.AddSingleton<TwitchEventSubListener>();
        services.AddTransient<App>();
    }
    private static IConfigurationRoot BuildConfiguration(string settingsFile, string configFile)
    {
        // TODO: Removed "ReloadOnChange" for the JsonFile
        // Find a better solution for this, apparently this exceeds the max filewatchers allowed
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(string.IsNullOrEmpty(configFile) ? settingsFile : configFile,
                optional: true)
            .Build();
    }
    private static void DisplayErrorMessage(Exception ex)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("                    ____________________________");
        Console.WriteLine("                   / Oops, something went wrong. \\");
        Console.WriteLine("                   \\     Please try again :3     /");
        Console.WriteLine("                  / ‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾");
        Console.WriteLine("　　　　　   __  /");
        Console.WriteLine("　　　　 ／フ   フ");
        Console.WriteLine("　　　　|  .   .|");
        Console.WriteLine("　 　　／`ミ__xノ");
        Console.WriteLine("　 　 /　　 　 |");
        Console.WriteLine("　　 /　 ヽ　　ﾉ");
        Console.WriteLine(" 　 │　　 | | |");
        Console.WriteLine("／￣|　　 | | |");
        Console.WriteLine("| (￣ヽ_ヽ)_)__)");
        Console.WriteLine("＼二つ");
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }
    private static void PromptExit()
    {
        AnsiConsole.Markup("[bold yellow]Press [green]Enter[/] to exit.[/]");
        Console.ReadLine();
    }
}
// The main application class, handling the core flow of the program
internal class App(IConfiguration configuration, IJsonFileController jsonController, IHueController hueController,
        TwitchLib.Api.TwitchAPI api, TwitchEventSubListener eventSubListener, WebServer webServer,
        ArgsService argsService, IBridgeValidator bridgeValidator,
        IVersionUpdateService versionUpdateService, ITwitchHttpClient twitchHttpClient, IConfigurationService configurationEditor, ITestLampEffectService testLampEffectService)
{
    // The main run method to start the app's functionality
    public async Task RunAsync()
    {
        if (argsService.Args.Length != 0 && argsService.Args[0] == "auto")
        {
            await StartApp();
        }
        else
        {
            var downloadUrl = await versionUpdateService.CheckForUpdates();
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                bool continuePrompting = true;

                while (continuePrompting)
                {
                    var prompt = new SelectionPrompt<string>()
                        .Title("[white]Would you like to update now or later?[/]")
                        .AddChoices(new[] { "Update now", "Later", "Read More" }) // Menu options
                        .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for the selected option
                        .Mode(SelectionMode.Leaf) // Focuses on the current selection, giving a modern feel
                        .WrapAround(false) // Prevents wrap-around behavior for a more streamlined UX
                        .UseConverter(text => $"[dim white]»[/] [white]{text}[/]"); // Custom converter for a minimal selection icon

                    string selectedOption = AnsiConsole.Prompt(prompt);
                    switch (selectedOption)
                    {
                        case "Update now":
                            await versionUpdateService.DownloadUpdate(downloadUrl);
                            continuePrompting = false; // Exit the loop after downloading
                            break;

                        case "Read More":
                            versionUpdateService.DisplayUpdateDetails(); // Show update details
                            break;

                        case "Later":
                            continuePrompting = false; // Exit the loop if the user chooses "Later"
                            break;
                    }
                }
            }

            await StartMenu();
        }
    }

    // Method to handle the main menu flow
    private async Task StartMenu()
    {
        while (true)
        {
            // Render the start menu and get the user's choice
            byte choice = await RenderStartMenu();

            switch (choice)
            {
                case 1:
                    await ConfigureTwitchTokens(); // Handle Twitch token configuration
                    break;
                case 2:
                    using (var success = hueController.StartPollingForLinkButtonAsync("YukiDanceParty", "MyDevice", configuration["bridgeIp"], configuration["AppKey"]))
                    {
                        AnsiConsole.Markup("[yellow]Opening Lamp Effect Testing[/]\n");
                        var keys = new Dictionary<int, string>
                        {
                            { 0, "Exit" },
                            { 1, "Subscription" },
                            { 2, "GiftedSubscription" },
                            { 3, "Follow" },
                        };
                        // Create a prompt for the user to select a configuration key to edit
                        var prompt = new SelectionPrompt<string>()
                            .Title("[grey]Select an effect you want to play:[/]")
                            .AddChoices(keys.Values.ToArray()) // Add the keys as choices
                            .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
                            .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
                            .WrapAround(true)
                            .UseConverter(text => $"[dim white]»[/] [white]{text}[/]");

                        // Get the selected value from the prompt
                        string selectedKey = AnsiConsole.Prompt(prompt);

                        // Check if the selected key is "Exit"}
                        if (selectedKey == "Exit") break;
                        switch (selectedKey)
                        {
                            case "Subscription":
                                await testLampEffectService
                                        .Test("Testing Subscription Effect")
                                        .Effect(EffectPalette.Subscription)
                                        .ExecuteAsync();
                                break;
                            case "GiftedSubscription":
                                await testLampEffectService
                                        .Test("Testing Gifted Subscription Effect")
                                        .Effect(EffectPalette.GiftedSubscription)
                                        .ExecuteAsync();
                                break;
                            case "Follow":
                                await testLampEffectService
                                        .Test("Testing Follow Effect")
                                        .Effect(EffectPalette.Follow)
                                        .ExecuteAsync();
                                break;
                        }
                    }
                    break;
                case 3:
                    AnsiConsole.Markup("[yellow]Opening app configuration for editing...[/]\n");
                    await configurationEditor.EditConfigurationAsync(); // Start the main application
                    break;
                case 4:
                    AnsiConsole.Markup("[green]Starting the application...[/]\n");
                    await StartApp(); // Start the main application
                    break;
                case 5:
                    AnsiConsole.Markup("[red]Exiting application...[/]\n");
                    Environment.Exit(0);
                    break;
            }
        }
    }

    // Method to render the start menu using Spectre.Console
    private async Task<byte> RenderStartMenu()
    {
        await ValidateHueConfiguration();
        bool twitchConfigured = await ValidateTwitchConfiguration(); // Check if Twitch is configured
                                                                     //await ValidateHueConfiguration(); // Validate Hue bridge configuration

        // Create a table to structure the menu visually
        var borderStyle = new Style(foreground: Color.White, decoration: Decoration.Bold);

        // Create a visually appealing table for the start menu
        var table = new Table()
            .Title("[underline bold yellow]Welcome To Yuki's Disco Light Party[/]")  // Title of the application
            .Border(TableBorder.Rounded)                                              // Rounded borders for a friendly look
            .BorderColor(Color.DeepSkyBlue4)                                           // Border color
            .BorderStyle(borderStyle)                                                  // Border style with bold text
            .AddColumn(new TableColumn("[bold gold3_1]Main Menu[/]").LeftAligned());   // Left aligned for clearer UX

        // Welcome message with additional tips
        table.AddRow("[bold cyan]This application helps you manage Twitch and Philips Hue configurations.[/]");
        table.AddRow("[bold cyan]Select an option below to proceed.[/]");
        table.AddEmptyRow();  // Add a blank row for spacing

        // Display Twitch configuration status
        string twitchStatus = twitchConfigured ? "[green]Complete[/]" : "[red]Incomplete[/]";
        table.AddRow($"[bold gold3_1]1.[/] [white]Connect to Twitch[/] ({twitchStatus})");
        table.AddRow("[bold gold3_1]3.[/] [white]Test Lamp Effects[/]");
        table.AddRow("[bold gold3_1]3.[/] [white]Edit App Configuration[/]");
        table.AddRow("[bold gold3_1]4.[/] [white]Start App[/]");
        table.AddRow("[bold gold3_1]5.[/] [white]Quit Application[/]");

        table.AddEmptyRow();  // Add a blank row for spacing

        // Instructions to guide the user
        table.AddRow("[bold aqua]Instructions:[/]");
        table.AddRow("[white]Use [bold yellow]arrow keys[/] to navigate and [bold yellow]Enter[/] to select.[/]");

        // Render the table in the console
        AnsiConsole.Write(table);

        // Prompt the user to select an option
        var prompt = new SelectionPrompt<int>()
            .Title("[grey]Select an option:[/]")
            .AddChoices(new[] {
            twitchConfigured ? -1 : 1, // Disable Twitch connect if already configured
            2, 3, 4, 5 // App Configuration, Start, and Quit options are always enabled
            })
            .UseConverter(option => option switch
            {
                1 => "[dim white]»[/] [white]Connect to Twitch[/]",
                -1 => "[dim grey]»[/] [grey]Connect to Twitch (Configured)[/]",  // Disabled menu for Twitch
                2 => "[dim white]»[/] [white]Test Lamp Effects[/]",
                3 => "[dim white]»[/] [white]Edit App Configuration[/]",
                4 => "[dim white]»[/] [white]Start App[/]",
                5 => "[dim white]»[/] [white]Exit[/]",
                _ => "[dim white]»[/] [white]Unknown Option[/]"
            })
            .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
            .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
            .WrapAround(false)        // Disable wrap-around behavior
            .PageSize(5);             // Fit all options on one page


        // Get and return the selected option
        byte selectedOption = (byte)AnsiConsole.Prompt(prompt);
        return selectedOption;
    }

    // Method to validate Hue bridge configurationprivate async Task<bool> ValidateTwitchConfiguration()
    private async Task<bool> ValidateHueConfiguration()
    {
        try
        {
            string bridgeIp = await GetOrDiscoverBridgeIpAsync();
            string bridgeId = await GetOrDiscoverBridgeIdAsync();
            string appKey = configuration["AppKey"];

            if (string.IsNullOrEmpty(bridgeIp))
            {
                AnsiConsole.Markup("[red]Bridge IP is missing. Unable to proceed with configuration.[/]\n");
                return false;
            }

            //await EnsureCertificateExistsAsync(bridgeIp);

            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error during Hue configuration validation: {ex.Message}[/]");
            return false;
        }
    }

    private async Task<string> GetOrDiscoverBridgeIpAsync()
    {
        string bridgeIp = configuration["bridgeIp"];
        if (string.IsNullOrEmpty(bridgeIp))
        {
            AnsiConsole.MarkupLine("[yellow]Bridge IP missing. Discovering...[/]");
            await hueController.DiscoverBridgeAsync();
            bridgeIp = await jsonController.GetValueByKeyAsync<string>("bridgeIp");
        }
        return bridgeIp;
    }

    private async Task<string> GetOrDiscoverBridgeIdAsync()
    {
        string bridgeId = configuration["bridgeId"];
        if (string.IsNullOrEmpty(bridgeId))
        {
            AnsiConsole.MarkupLine("[yellow]Bridge ID missing. Discovering...[/]");
            await hueController.DiscoverBridgeAsync();
            bridgeId = await jsonController.GetValueByKeyAsync<string>("bridgeId");
        }
        return bridgeId;
    }

    private async Task EnsureCertificateExistsAsync(string bridgeIp)
    {
        const string certFileName = "huebridge_cacert.pem";
        if (!File.Exists(certFileName))
        {
            AnsiConsole.MarkupLine("[yellow]Certificate not found. Configuring...[/]");
            await ConfigureCertificate(bridgeIp);
        }
    }

    private async Task<bool> ValidateBridgeConnectionAsync(string bridgeIp, string bridgeId, string appKey)
    {
        bool isValid = await bridgeValidator.ValidateBridgeIpAsync(bridgeIp, bridgeId, appKey);
        if (!isValid && !string.IsNullOrEmpty(appKey))
        {
            AnsiConsole.MarkupLine($"[bold yellow]Invalid Bridge IP: {bridgeIp}[/]");
            AnsiConsole.MarkupLine("[bold yellow]Attempting to rediscover bridge...[/]");
        }
        return isValid;
    }


    private static async Task ConfigureCertificate(string bridgeIp)
    {
        try
        {
            await CertificateService.ConfigureCertificate([bridgeIp, "443", "huebridge_cacert.pem"]);
        }
        catch (Exception ex)
        {
            AnsiConsole.Markup($"[red]Error configuring certificate: {ex.Message}[/]\n");
        }
    }

    // Method to validate Twitch configuration
    private async Task<bool> ValidateTwitchConfiguration()
    {
        string RefreshToken = configuration["RefreshToken"]; // Get the refresh token from the configuration

        if (string.IsNullOrEmpty(RefreshToken))
        {
            return false; // Return false if no refresh token is found
        }

        // Set the access token for the API and validate it
        api.Settings.AccessToken = configuration["AccessToken"];
        if (await api.Auth.ValidateAccessTokenAsync() != null)
        {
            return true;
        }
        else
        {
            try
            {
                // Refresh the access token because it's invalid
                api.Settings.ClientId = configuration["ClientId"];
                AnsiConsole.Markup("[yellow]AccessToken is invalid, refreshing for a new token...[/]\n");
                TwitchLib.Api.Auth.RefreshResponse refresh = await api.Auth.RefreshAuthTokenAsync(RefreshToken, configuration["ClientSecret"], configuration["ClientId"]);
                api.Settings.AccessToken = refresh.AccessToken;
                // Update the access token in the configuration file
                await jsonController.UpdateAsync("AccessToken", refresh.AccessToken);
                await twitchHttpClient.UpdateOAuthToken(refresh.AccessToken);
                return true;
            }
            catch (BadRequestException ex)
            {
                Console.WriteLine(ex.Message); // Log any exceptions during the refresh process
                return false;
            }
        }
    }

    // Method to start the main application
    private async Task StartApp()
    {
        // Validate the Twitch configuration before proceeding
        bool twitchConfigured = await ValidateTwitchConfiguration();

        if (!twitchConfigured)
        {
            AnsiConsole.Markup("[bold red]\nError: Twitch Configuration is incomplete.\n[/]");
            return;
        }
        else
        {
            // Start polling the Hue bridge for the link button press and connect to the Twitch EventSub
            bool result = await hueController.StartPollingForLinkButtonAsync("YukiDanceParty", "MyDevice", configuration["bridgeIp"], configuration["AppKey"]);
            if (result)
            {
                string wsstring = ""; // Choose the appropriate websocket based on the environment
                const string ws = "wss://eventsub.wss.twitch.tv/ws"; // Twitch EventSub websocket endpoint
                const string localws = "ws://127.0.0.1:8080/ws"; // Local websocket for development
                if (argsService.Args.Contains("local"))
                {
                    wsstring = localws;
                }
                else
                {
                    wsstring = ws;

                }

                await eventSubListener.ValidateAndConnectAsync(new Uri(wsstring)); // Connect to the EventSub websocket
                await eventSubListener.ListenForEventsAsync(); // Start listening for events
            }
        }
    }
    // Method to configure Twitch OAuth tokens
    private async Task<bool> ConfigureTwitchTokens()
    {
        string clientId = configuration["ClientId"];
        string clientSecret = configuration["ClientSecret"];

        // Check if ClientId or ClientSecret is missing
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            AnsiConsole.Markup("[bold red]ClientId or ClientSecret not found in configuration file![/]\n");

            // Prompt user for missing ClientId
            if (string.IsNullOrEmpty(clientId))
            {
                clientId = AnsiConsole.Ask<string>("[yellow]Please enter the ClientId:[/]");
                await jsonController.UpdateAsync("ClientId", clientId); // Save to appsettings.json
            }

            // Prompt user for missing ClientSecret
            if (string.IsNullOrEmpty(clientSecret))
            {
                clientSecret = AnsiConsole.Ask<string>("[yellow]Please enter the ClientSecret:[/]");
                await jsonController.UpdateAsync("ClientSecret", clientSecret); // Save to appsettings.json
            }

            AnsiConsole.Markup("[green]ClientId and ClientSecret have been updated in appsettings.json.[/]\n");
        }
        // List of scopes the application will request
        List<string> scopes = ["channel:bot", "user:read:chat", "channel:read:redemptions", "user:write:chat", "channel:read:subscriptions", "bits:read"];
        string state = RandomStringGenerator.GenerateRandomString(); // Generate a random state for OAuth security
        api.Settings.ClientId = configuration["ClientId"];

        AnsiConsole.Markup($"Please authorize here:\n[link={GetAuthorizationCodeUrl(configuration["ClientId"], configuration["RedirectUri"], scopes, state)}]Authorization Link[/]\n");
        var linkAccessibility = AnsiConsole.Confirm("[yellow]If you are unable to click the link, would you like to see the raw URL?[/]");

        if (linkAccessibility)
        {
            // Provide the raw URL as fallback
            string rawLink = GetAuthorizationCodeUrl(configuration["ClientId"], configuration["RedirectUri"], scopes, state);
            AnsiConsole.Markup($"[bold green]Raw URL:[/] {rawLink}\n");
        }
        // Listen for the OAuth callback and retrieve the authorization code
        AuthorizationResult? auth = await webServer.WaitForAuthorizationAsync(state);

        if (auth.IsSuccess)
        {
            // Exchange the authorization code for access and refresh tokens
            TwitchLib.Api.Auth.AuthCodeResponse? resp = await api.Auth.GetAccessTokenFromCodeAsync(auth.Code, configuration["ClientSecret"], configuration["RedirectUri"]);
            api.Settings.AccessToken = resp.AccessToken;
            await jsonController.UpdateAsync("AccessToken", resp.AccessToken);
            await jsonController.UpdateAsync("RefreshToken", resp.RefreshToken);

            var user = (await api.Helix.Users.GetUsersAsync()).Users[0]; // Get user details from Twitch
            await jsonController.UpdateAsync("ChannelId", user.Id);

            // Display a success message with the user information
            AnsiConsole.Write(
                new Panel($"[bold green]Authorization success![/]\n\n[bold aqua]User:[/] {user.DisplayName} (id: {user.Id})\n[bold aqua]Scopes:[/] :{string.Join(", ", resp.Scopes)}")
                .BorderColor(Color.Green)
            );
            return true;
        }
        return false;
    }

    // Method to generate the authorization URL for OAuth
    private static string GetAuthorizationCodeUrl(string clientId, string redirectUri, List<string> scopes, string state)
    {
        var scopesStr = string.Join('+', scopes); // Join the requested scopes
        var encodedRedirectUri = System.Web.HttpUtility.UrlEncode(redirectUri); // URL-encode the redirect URI
        return "https://id.twitch.tv/oauth2/authorize?" +
            $"client_id={clientId}&" +
            $"force_verify=true&" +
            $"redirect_uri={encodedRedirectUri}&" +
            "response_type=code&" +
            $"scope={scopesStr}&" +
            $"state={state}";
    }
}

// A simple implementation of the XORShift32 PRNG
public static class XorShift32
{
    public static uint Next(uint seed)
    {
        uint x = seed;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        seed = x;
        return x;
    }
}

// A utility class to generate random
public static class RandomStringGenerator
{
    public static string GenerateRandomString(int length = 32)
    {
        const string chars = "7jXb2NEFp9M3hCRKZwBvLziPDSUq5Ixl4y1GtQJcr0HmkOnW6gsToA8fYdeVua";
        var stringBuilder = new StringBuilder(length);

        // Initialize XORShift with a seed (you can use any uint seed)

        for (int i = 0; i < length; i++)
        {
            // Generate a random number and map it to a character in the chars array
            uint randomValue = XorShift32.Next((uint)DateTime.Now.Ticks);
            stringBuilder.Append(chars[(int)(randomValue % chars.Length)]);
        }

        return stringBuilder.ToString();
    }
}