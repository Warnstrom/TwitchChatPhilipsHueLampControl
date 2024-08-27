using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    static void Main(string[] args)
    {

        if (args.Length < 3)
        {
            AnsiConsole.MarkupLine("[red]Usage: Updater.exe [green]<target directory>[/] [green]<main application executable>[/] [green]<update directory>[/][/]");
            return;
        }
        AnsiConsole.MarkupLine("[bold yellow]Updater started...[/]");
        string targetDirectory = args[0];
        string mainExecutable = args[1];
        string updateDirectory = args[2];

        AnsiConsole.MarkupLine($"[bold]Target Directory:[/] [blue]{targetDirectory}[/]");
        AnsiConsole.MarkupLine($"[bold]Main Executable:[/] [blue]{mainExecutable}[/]");
        AnsiConsole.MarkupLine($"[bold]Update Directory:[/] [blue]{updateDirectory}[/]");

        // Wait for the main application to exit
        AnsiConsole.MarkupLine("[yellow]Waiting for the main application to exit...[/]");
        Thread.Sleep(2500); // Adjust the wait time as needed

        try
        {
            AnsiConsole.MarkupLine("[bold green]Starting to copy new files...[/]");

            // Copy new files from the update directory to the target directory
            foreach (var file in Directory.GetFiles(updateDirectory))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDirectory, fileName);

                try
                {
                    //AnsiConsole.MarkupLine($"[cyan]Copying [bold]{fileName}[/] to [bold]{destFile}[/]...[/]");

                    // Delete the existing file before copying the new one to avoid corruption
                    if (File.Exists(destFile))
                    {
                        //AnsiConsole.MarkupLine($"[yellow]Deleting existing file: {destFile}[/]");
                        File.Delete(destFile);
                    }

                    // Copy the new file
                    File.Copy(file, destFile, overwrite: true);

                    AnsiConsole.MarkupLine($"[green]{fileName} copied successfully.[/]");
                }
                catch (IOException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to copy {fileName}: {ex.Message}[/]");
                    // Consider adding retry logic or rollback mechanism here
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Unexpected error while copying {fileName}: {ex.Message}[/]");
                }
            }


            // Optionally delete the update files after copying
            Directory.Delete(updateDirectory, true);
            AnsiConsole.MarkupLine("\n[bold green]Update completed successfully![/]");

        }
        catch (Exception ex)
        {

            AnsiConsole.MarkupLine("[bold red]Update failed. Details below:[/]");
            Console.WriteLine($"Update failed: {ex.Message}");
            Console.WriteLine($"{ex.StackTrace}");
        }
        finally
        {
            AnsiConsole.MarkupLine("[bold yellow]Press any key to close this window and restart the application.[/]");
            /*{
                Process.Start(new ProcessStartInfo
                {
                    FileName = "TwitchChatHueControls.exe",
                    UseShellExecute = true
                });

            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {

                Process.Start(new ProcessStartInfo
                {
                    FileName = "TwitchChatHueControls",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }*/
        }
    }
}
