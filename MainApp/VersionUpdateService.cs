using Microsoft.Extensions.Configuration;
using Octokit;
using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace TwitchChatHueControls;

// Interface defining the contract for version update operations
internal interface IVersionUpdateService
{
    public Task<string?> CheckForUpdates();
    public Task DownloadUpdate(string downloadUrl);
    public void DisplayUpdateDetails();
}

// Service that handles checking for, downloading, and installing application updates from GitHub
internal class VersionUpdateService(IConfiguration configuration, IJsonFileController jsonFileController) : IVersionUpdateService
{
    // Determine the operating system for platform-specific operations
    readonly string OS = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "windows";

    // GitHub repository information
    private readonly string _owner = "Warnstrom";
    private readonly string _repo = "TwitchChatPhilipsHueLampControl";
    // Initialize GitHub API client
    private readonly GitHubClient client = new GitHubClient(new ProductHeaderValue("TwitchChatHueController"));
    private string latestVersion = "";

    public async Task<string?> CheckForUpdates()
    {
        try
        {
            AnsiConsole.MarkupLine("[bold yellow]Checking for updates...[/]");

            // Fetch all releases from the GitHub repository
            var releases = await client.Repository.Release.GetAll(_owner, _repo);
            if (!releases.Any())
            {
                AnsiConsole.MarkupLine("[red]No releases found in the repository.[/]");
                return null;
            }

            // Get the most recent release
            List<Release> MainReleases = releases.Where(release => release.TagName.Contains("Release_")).ToList();

            // Get version number from tag name Release_20241214-v1.0.0 -> 1.0.0
            // Remove 'v' prefix from version number (e.g., 'v1.0.0' -> '1.0.0')
            Release LatestRelease = MainReleases.FirstOrDefault();
            latestVersion = LatestRelease.TagName.Split("-")[1].TrimStart('v');
            if (MainReleases == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to identify the latest release.[/]");
                return null;
            }

            // Get current version from application configuration
            var currentVersion = configuration["ApplicationVersion"];
            // Compare versions to determine if an update is needed
            if (Version.TryParse(latestVersion, out var parsedLatestVersion) &&
                Version.TryParse(currentVersion, out var parsedCurrentVersion) &&
                parsedLatestVersion > parsedCurrentVersion)
            {
                // Find the appropriate release asset for the current operating system
                IReadOnlyList<ReleaseAsset> assetList = LatestRelease.Assets;
                ReleaseAsset asset = assetList.FirstOrDefault(a => a.Name.Contains(OS));
                if (asset != null)
                {
                    AnsiConsole.MarkupLine($"[bold green]\nA new update is available![/] [grey](v{latestVersion})[/]\n");
                    return asset.BrowserDownloadUrl;
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]No suitable asset found for the current OS.[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[bold green]You are already running the latest version.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error while checking for updates:[/] [red]{ex.Message}[/]");
        }

        return null;
    }



    // Get version number from tag name Release_20241214-v1.0.0 -> 1.0.0
    // Remove 'v' prefix from version number (e.g., 'v1.0.0' -> '1.0.0')

    // Display detailed information about the latest release
    public async void DisplayUpdateDetails()
    {
        var releases = await client.Repository.Release.GetAll(_owner, _repo);
        List<Release> MainReleases = releases.Where(release => release.TagName.Contains("Release_")).ToList();

        Release LatestRelease = MainReleases.FirstOrDefault();
        latestVersion = LatestRelease.TagName.TrimStart('v');

        var currentVersion = configuration["ApplicationVersion"];
        IReadOnlyList<ReleaseAsset> assetList = LatestRelease.Assets;
        ReleaseAsset asset = assetList.FirstOrDefault(a => a.Name.Contains(OS));

        // Create a formatted table with update information using Spectre.Console
        var updateTable = new Table()
            .Border(TableBorder.Rounded)
            .HideHeaders()
            .AddColumn(new TableColumn(""))
            .AddColumn(new TableColumn(""))
            .AddRow("[green]Release ID: [/]", $"[bold blue]v{LatestRelease.Id}[/]")
            .AddRow("[green]Latest Version[/]", $"[bold blue]v{latestVersion}[/]")
            .AddRow("[green]Current Version[/]", $"[bold blue]v{currentVersion}[/]")
            .AddRow("[green]Download URL[/]", $"[link={asset.BrowserDownloadUrl}]Download Here[/]")
            .AddRow("[green]Release Notes[/]", $"[grey]{LatestRelease.Body}[/]");

        AnsiConsole.Write(updateTable);
    }

    // Download the update package with progress indication
    public async Task DownloadUpdate(string downloadUrl)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0L;

        if (totalBytes == 0)
        {
            Console.WriteLine("Unable to determine file size.");
            return;
        }

        // Show download progress using Spectre.Console
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Downloading new version...[/]");
                task.MaxValue = totalBytes;

                var buffer = new byte[8192];
                int bytesRead;

                // Download the file in chunks while updating the progress bar
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream($"update_{OS}.zip", System.IO.FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    task.Increment(bytesRead);
                }
            });

        // Extract the downloaded update package
        await ExtractAndUpdateFiles($"update_{OS}.zip");
    }

    // Extract the downloaded update package and prepare for installation
    private async Task ExtractAndUpdateFiles(string zipFilePath)
    {
        if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
        {
            throw new ArgumentException("The provided zip file path is invalid or does not exist.");
        }

        // Create extraction directory
        string directoryPath = Path.Combine(Path.GetDirectoryName(zipFilePath), Path.GetFileNameWithoutExtension(zipFilePath));
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        try
        {
            // Extract files and clean up the zip file
            await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, directoryPath));
            File.Delete(zipFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while extracting the zip file: {ex.Message}");
            throw;
        }

        // Start the updater process and exit the current application
        StartUpdaterAndExit(directoryPath);
    }

    // Launch the updater application and exit the current instance
    private async Task StartUpdaterAndExit(string directoryPath)
    {
        // Configure the updater process based on the operating system
        string updaterPath = "TwitchChatHueUpdater";
        string arguments = "";
        switch (OS)
        {
            case "windows":
                updaterPath += ".exe";
                arguments = $". TwitchChatHueControls.exe {directoryPath}";
                break;
            case "linux":
                arguments = $"{AppContext.BaseDirectory} TwitchChatHueControls {directoryPath}";
                break;
        }

        // Start the updater process
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = arguments,
            UseShellExecute = true
        };
        Process.Start(startInfo);

        // Update the application version in configuration and exit
        await jsonFileController.UpdateAsync("ApplicationVersion", latestVersion);
        Environment.Exit(0);
    }
}