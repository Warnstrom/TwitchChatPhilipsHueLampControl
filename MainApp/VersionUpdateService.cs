using Microsoft.Extensions.Configuration;
using Octokit;
using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

public interface IVersionUpdateService
{
    public Task<string?> CheckForUpdates();
    public Task DownloadUpdate(string downloadUrl);
    public void DisplayUpdateDetails();


}
public class VersionUpdateService(IConfiguration configuration, IJsonFileController jsonFileController) : IVersionUpdateService
{
    readonly string OS = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "windows";
    private readonly string _owner = "Warnstrom";
    private readonly string _repo = "TwitchChatPhilipsHueLampControl";
    private readonly GitHubClient client = new GitHubClient(new ProductHeaderValue("TwitchChatHueController"));
    private string latestVersion = "";

    public async Task<string?> CheckForUpdates()
    {
        try
        {
            AnsiConsole.MarkupLine("[bold yellow]Checking for updates...[/]");

            // Fetch all releases from the repository
            var releases = await client.Repository.Release.GetAll(_owner, _repo);
            if (!releases.Any())
            {
                AnsiConsole.MarkupLine("[red]No releases found in the repository.[/]");
                return null;
            }

            // Get the latest release
            Release latestRelease = releases.FirstOrDefault();
            if (latestRelease == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to identify the latest release.[/]");
                return null;
            }

            latestVersion = latestRelease.TagName.TrimStart('v'); // Extract version number

            // Compare the latest version with the currently installed version
            var currentVersion = configuration["ApplicationVersion"];

            if (Version.TryParse(latestVersion, out var parsedLatestVersion) &&
                Version.TryParse(currentVersion, out var parsedCurrentVersion) &&
                parsedLatestVersion > parsedCurrentVersion)
            {
                // Fetch the correct asset for the OS
                IReadOnlyList<ReleaseAsset> assetList = latestRelease.Assets;
                ReleaseAsset asset = assetList.FirstOrDefault(a => a.Name.Contains(OS));
                AnsiConsole.MarkupLine($"[bold green]\nA new update is available![/] [grey](v{latestVersion})[/]\n");
                if (asset != null)
                {
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

    public async void DisplayUpdateDetails()
    {
        var releases = await client.Repository.Release.GetAll(_owner, _repo);
        Release latestRelease = releases.FirstOrDefault();
        latestVersion = latestRelease.TagName.TrimStart('v'); // Extract version number

        // Compare the latest version with the currently installed version
        var currentVersion = configuration["ApplicationVersion"];
        IReadOnlyList<ReleaseAsset> assetList = latestRelease.Assets;

        ReleaseAsset asset = assetList.FirstOrDefault(a => a.Name.Contains(OS));

        var updateTable = new Table()
            .Border(TableBorder.Rounded)
            .HideHeaders()
            .AddColumn(new TableColumn(""))
            .AddColumn(new TableColumn(""))
            .AddRow("[green]Release ID: [/]", $"[bold blue]v{latestRelease.Id}[/]")
            .AddRow("[green]Latest Version[/]", $"[bold blue]v{latestVersion}[/]")
            .AddRow("[green]Current Version[/]", $"[bold blue]v{currentVersion}[/]")
            .AddRow("[green]Download URL[/]", $"[link={asset.BrowserDownloadUrl}]Download Here[/]")
            .AddRow("[green]Release Notes[/]", $"[grey]{latestRelease.Body}[/]");

        AnsiConsole.Write(updateTable);
    }

    public async Task DownloadUpdate(string downloadUrl)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        // Get the total file size
        var totalBytes = response.Content.Headers.ContentLength ?? 0L;

        if (totalBytes == 0)
        {
            Console.WriteLine("Unable to determine file size.");
            return;
        }

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                // Create a progress task for the download
                var task = ctx.AddTask("[green]Downloading new version...[/]");

                // Ensure progress is tied to the file size
                task.MaxValue = totalBytes;

                var buffer = new byte[8192];
                int bytesRead;

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream($"update_{OS}.zip", System.IO.FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

                // Read the response stream in chunks and write to file
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    task.Increment(bytesRead);
                }
            });
        await ExtractAndUpdateFiles($"update_{OS}.zip");
    }

    private async Task ExtractAndUpdateFiles(string zipFilePath)
    {
        // Validate input
        if (string.IsNullOrEmpty(zipFilePath) || !File.Exists(zipFilePath))
        {
            throw new ArgumentException("The provided zip file path is invalid or does not exist.");
        }

        // Determine the folder name by removing the extension from the zip file name
        string directoryPath = Path.Combine(Path.GetDirectoryName(zipFilePath), Path.GetFileNameWithoutExtension(zipFilePath));
        // Ensure the target directory exists
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        try
        {
            // Extract the .zip file to the target directory
            await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, directoryPath));
            File.Delete(zipFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while extracting the zip file: {ex.Message}");
            throw; // Optionally, rethrow the exception if needed
        }
        StartUpdaterAndExit(directoryPath);
    }

    private async Task StartUpdaterAndExit(string directoryPath)
    {
        // The updater executable is included in your application package
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
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = arguments,
            UseShellExecute = true
        };
        Process.Start(startInfo);
        await jsonFileController.UpdateAsync("ApplicationVersion", latestVersion);
        Environment.Exit(0);
    }

}
