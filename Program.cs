using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text;
using TwitchLib.Api.Core.Exceptions;

namespace TwitchChatHueControls
{
    class Program
    {
        private static string SettingsFile = "";

        private static async Task Main(string[] args)
        {
            SettingsFile = args.FirstOrDefault() == "dev" ? "devmodesettings.json" : "appsettings.json";
            try
            {
                // Create a new ServiceCollection (IoC container) for dependency injection
                var serviceCollection = new ServiceCollection();

                // Register and configure the required services
                ConfigureServices(serviceCollection, args);

                // Build the service provider to resolve dependencies
                ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

                // Run the application by resolving the main App class and calling its RunAsync method
                await serviceProvider.GetRequiredService<App>().RunAsync();
            }
            catch (Exception ex)
            {
                // Error handling with Spectre.Console for better visual output
                // This part displays an ASCII art with a message when an exception occurs
                Console.OutputEncoding = System.Text.Encoding.UTF8;
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
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything); // Display the exception with Spectre.Console's enhanced formatting
            }
            finally
            {
                // Prompt user to press Enter to exit the application
                AnsiConsole.Markup("[bold yellow]Press [green]Enter[/] to exit.[/]");
                Console.ReadLine();
            }
        }

        // Method to configure the services needed by the application
        private static void ConfigureServices(IServiceCollection services, string[] args)
        {
            // Create and configure the ConfigurationBuilder to load settings from the specified JSON file
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Set the base path to the current directory
                .AddJsonFile(SettingsFile, optional: true, reloadOnChange: true); // Add the settings file
            // Build the configuration object
            IConfiguration configuration = configurationBuilder.Build();

            // Register the required services and controllers
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(new ArgsService(args));
            services.AddSingleton<IJsonFileController>(sp => new JsonFileController(SettingsFile));
            services.AddSingleton<IHueController, HueController>();
            services.AddSingleton<TwitchLib.Api.TwitchAPI>();
            services.AddSingleton<TwitchEventSubListener>();
            services.AddSingleton<IBridgeValidator, BridgeValidator>();
            services.AddScoped<ITwitchHttpClient, TwitchHttpClient>();
            services.AddTransient<IVersionUpdateService, VersionUpdateService>();
            services.AddSingleton<WebServer>();
            // Register the main application entry point
            services.AddTransient<App>();
        }
    }

    // The main application class, handling the core flow of the program
    public class App(IConfiguration configuration, IJsonFileController jsonController, IHueController hueController,
            TwitchLib.Api.TwitchAPI api, TwitchEventSubListener eventSubListener, WebServer webServer,
            ArgsService argsService, IBridgeValidator bridgeValidator,
            IVersionUpdateService versionUpdateService)
    {
        // The main run method to start the app's functionality
        public async Task RunAsync()
        {
            var DownloadUrl = await versionUpdateService.CheckForUpdates();
            Console.WriteLine(DownloadUrl);
            if (!string.IsNullOrEmpty(DownloadUrl))
            {
                var prompt = new SelectionPrompt<string>()
                    .Title("\nThere's an update available. Do you want to update now or later?")
                    .AddChoices("Update now.", "Later.") // Menu options
                    .HighlightStyle(new Style(foreground: Color.Yellow)); // Highlight style for selection   
                string selectedOption = AnsiConsole.Prompt(prompt);
                if (selectedOption.Equals("Update now."))
                {
                    await versionUpdateService.DownloadUpdate(DownloadUrl);
                }
            }
            await StartMenu();
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
                        await StartApp(); // Start the main application
                        break;
                    default:
                        AnsiConsole.Markup("[red]Invalid choice. Please select again.[/]\n"); // Handle invalid selections
                        break;
                }
            }
        }

        // Method to render the start menu using Spectre.Console
        private async Task<byte> RenderStartMenu()
        {
            bool twitchConfigured = await ValidateTwitchConfiguration(); // Check if Twitch is configured
            await ValidateHueConfiguration(); // Validate Hue bridge configuration

            // Create a table to structure the menu visually
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Teal)
                .AddColumn(new TableColumn("[bold teal]Welcome To Yuki's Disco Lights[/]")); // Add a welcome message

            // Display Twitch configuration status
            string twitchStatus = twitchConfigured ? "Complete" : "Incomplete";
            table.AddRow($"[bold yellow]1.[/] Connect to Twitch ([{(twitchConfigured ? "green" : "yellow")}]{twitchStatus}[/])");
            table.AddRow("[bold yellow]2.[/] Start Bot");

            // Render the table in the console
            AnsiConsole.Write(table);

            // Prompt the user to select an option
            var prompt = new SelectionPrompt<int>()
                .Title("Please choose an option")
                .AddChoices(1, 2) // Menu options
                .HighlightStyle(new Style(foreground: Color.Teal)); // Highlight style for selection

            // Get and return the selected option
            byte selectedOption = (byte)AnsiConsole.Prompt(prompt);
            return selectedOption;
        }

        // Method to validate Hue bridge configuration
        private async Task ValidateHueConfiguration()
        {
            // Retrieve the bridge IP, ID, and app key from the configuration
            string localBridgeIp = configuration["bridgeIp"];
            string localBridgeId = configuration["bridgeId"];
            string localAppKey = configuration["AppKey"];

            // Discover the bridge if the IP or ID is missing
            if (string.IsNullOrEmpty(localBridgeIp) || string.IsNullOrEmpty(localBridgeId))
            {
                await hueController.DiscoverBridgeAsync();
                localBridgeIp = configuration["bridgeIp"];
                localBridgeId = configuration["bridgeId"];
            }

            // Configure the certificate if not already present
            if (!File.Exists("huebridge_cacert.pem"))
            {
                if (!string.IsNullOrEmpty(localBridgeIp))
                {
                    await CertificateService.ConfigureCertificate([localBridgeIp, "443", "huebridge_cacert.pem"]);
                }
                else
                {
                    AnsiConsole.Markup("[red]Bridge IP is missing, cannot configure the certificate.[/]\n");
                }
            }

            // Validate the bridge IP and update if needed
            bool validBridgeIp = await bridgeValidator.ValidateBridgeIpAsync(localBridgeIp, localBridgeId, localAppKey);
            if (!validBridgeIp)
            {
                AnsiConsole.MarkupLine($"[bold yellow]Invalid BridgeIp: {localBridgeIp}[/]");
                AnsiConsole.MarkupLine($"[bold yellow]Discovering new BridgeIp...[/]");
                await hueController.DiscoverBridgeAsync();
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
                    const string ws = "wss://eventsub.wss.twitch.tv/ws"; // Twitch EventSub websocket endpoint
                    const string localws = "ws://127.0.0.1:8080/ws"; // Local websocket for development
                    string wsstring = argsService.Args.FirstOrDefault() == "dev" ? localws : ws; // Choose the appropriate websocket based on the environment
                    await eventSubListener.ValidateAndConnectAsync(new Uri(wsstring)); // Connect to the EventSub websocket
                    await eventSubListener.ListenForEventsAsync(); // Start listening for events
                }
            }
        }

        // Method to configure Twitch OAuth tokens
        private async Task ConfigureTwitchTokens()
        {
            // List of scopes the application will request
            List<string> scopes = new List<string> { "channel:bot", "user:read:chat", "channel:read:redemptions", "user:write:chat" };
            string state = RandomStringGenerator.GenerateRandomString(); // Generate a random state for OAuth security
            api.Settings.ClientId = configuration["ClientId"];
            AnsiConsole.Markup($"Please authorize here:\n[link={getAuthorizationCodeUrl(configuration["ClientId"], configuration["RedirectUri"], scopes, state)}]Authorization Link[/]\n");

            // Listen for the OAuth callback and retrieve the authorization code
            Authorization? auth = await webServer.ListenAsync(state);

            if (auth != null)
            {
                // Exchange the authorization code for access and refresh tokens
                TwitchLib.Api.Auth.AuthCodeResponse? resp = await api.Auth.GetAccessTokenFromCodeAsync(auth.Code, configuration["ClientSecret"], configuration["RedirectUri"]);
                api.Settings.AccessToken = resp.AccessToken;
                await jsonController.UpdateAsync("AccessToken", resp.AccessToken);
                await jsonController.UpdateAsync("RefreshToken", resp.RefreshToken);

                var user = (await api.Helix.Users.GetUsersAsync()).Users[0]; // Get user details from Twitch

                // Display a success message with the user information
                AnsiConsole.Write(
                    new Panel($"[bold green]Authorization success![/]\n\n[bold aqua]User:[/] {user.DisplayName} (id: {user.Id})\n[bold aqua]Scopes:[/] :{string.Join(", ", resp.Scopes)}")
                    .BorderColor(Color.Green)
                );
            }
        }

        // Method to generate the authorization URL for OAuth
        private string getAuthorizationCodeUrl(string clientId, string redirectUri, List<string> scopes, string state)
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

}