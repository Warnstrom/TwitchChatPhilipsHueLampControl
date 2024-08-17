class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Updater started...");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Updater.exe <target directory> <main application executable>");
            return;
        }

        string targetDirectory = args[0];
        string mainExecutable = args[1];
        string updateDirectory = args[2];

        Console.WriteLine($"Target Directory: {targetDirectory}");
        Console.WriteLine($"Main Executable: {mainExecutable}");
        Console.WriteLine($"Update Directory: {updateDirectory}");

        // Wait for the main application to exit
        Console.WriteLine("Waiting for the main application to exit...");
        Thread.Sleep(2500); // Adjust the wait time as needed

        try
        {
            // Start copying files from the update directory
            Console.WriteLine("Starting to copy new files...");

            // Copy new files from the update directory to the target directory
            foreach (var file in Directory.GetFiles(updateDirectory))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDirectory, fileName);

                Console.WriteLine($"Copying {fileName} to {destFile}...");

                // Delete the existing file before copying the new one to avoid corruption
                if (File.Exists(destFile))
                {
                    Console.WriteLine($"Deleting existing file: {destFile}");
                    File.Delete(destFile);
                }

                // Copy the new file
                File.Copy(file, destFile);

                Console.WriteLine($"{fileName} copied successfully.");
            }
            // Optionally delete the update files after copying
            Directory.Delete(updateDirectory, true);
            Console.WriteLine("Update completed.");
            Console.WriteLine("You may close this window and restart the application.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update failed: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }

    }
}
