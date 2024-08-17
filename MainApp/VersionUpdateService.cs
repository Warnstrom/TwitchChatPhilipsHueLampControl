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

        var releases = await client.Repository.Release.GetAll(_owner, _repo);
        string? DownloadUrl = null;
        if (releases.Any())
        {
            Release latestRelease = releases.FirstOrDefault(); // Get the latest release (first in the list)

            latestVersion = latestRelease.TagName.TrimStart('v'); // Extract version number
            IReadOnlyList<ReleaseAsset> AssetList = latestRelease.Assets;
            ReleaseAsset asset = AssetList.FirstOrDefault(asset => asset.Name.Contains(OS));

            if (Version.Parse(latestVersion) > Version.Parse(configuration["ApplicationVersion"]))
            {
                DownloadUrl = asset.BrowserDownloadUrl;
                Console.WriteLine($"Application version: v{configuration["ApplicationVersion"]}");
                Console.WriteLine($"Latest version available: v{latestVersion}");
                return DownloadUrl;
            }
        }
        Console.WriteLine("Newest version is installed.");
        return null;
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
