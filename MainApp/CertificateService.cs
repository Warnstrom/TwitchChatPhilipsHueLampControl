using System.Diagnostics;
class CertificateService
{
    // This method is the main entry point for configuring the certificate based on the provided arguments.
    // It accepts an array of arguments where:
    // args[0] - server address
    // args[1] - port number
    // args[2] - file path to save the certificate
    public static async Task ConfigureCertificate(string[] args)
    {
        // Extract arguments.
        string serverAddress = args[0];
        string port = args[1];
        string outputFilePath = args[2];

        try
        {
            // Fetch the certificate from the specified server and port.
            string certificate = await FetchCertificateAsync(serverAddress, port);

            // If the certificate was found, save it to the specified file path.
            if (!string.IsNullOrEmpty(certificate))
            {
                await SaveCertificateToFileAsync(outputFilePath, certificate);
                Console.WriteLine($"Certificate saved to {outputFilePath}");
            }
            else
            {
                Console.WriteLine("No certificate found in the output.");
            }
        }
        catch (Exception ex)
        {
            // Catch and display any errors that occur during the process.
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    // This method runs the OpenSSL command to retrieve the certificate from the server.
    // It uses the server's address and port number as inputs.
    private static async Task<string?> FetchCertificateAsync(string serverAddress, string port)
    {
        // Create a new process to run the OpenSSL command.
        Process process = new Process();
        process.StartInfo.FileName = "openssl";
        process.StartInfo.Arguments = $"s_client -showcerts -connect {serverAddress}:{port}";
        process.StartInfo.RedirectStandardOutput = true; // Redirect the output to capture the certificate.
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true; // Run the process without showing a command window.

        process.Start();

        // Read the output from the command execution.
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(); // Wait until the process exits.

        // Extract the certificate from the output.
        return ExtractCertificate(output);
    }

    // This method extracts the certificate from the command output.
    // It looks for the "BEGIN CERTIFICATE" and "END CERTIFICATE" markers in the output.
    private static string? ExtractCertificate(string output)
    {
        // Constants that define the certificate markers.
        const string beginCert = "-----BEGIN CERTIFICATE-----";
        const string endCert = "-----END CERTIFICATE-----";

        // Find the positions of the certificate markers in the output.
        int startIndex = output.IndexOf(beginCert);
        int endIndex = output.IndexOf(endCert);

        // If both markers are found, extract the certificate between them.
        if (startIndex != -1 && endIndex != -1)
        {
            endIndex += endCert.Length;
            return output[startIndex..endIndex]; // Extract the certificate block.
        }

        return null; // Return null if no certificate is found.
    }

    // This method saves the extracted certificate to a file.
    // It takes the output file path and the certificate content as inputs.
    private static async Task SaveCertificateToFileAsync(string outputFilePath, string certificate)
    {
        // Write the certificate content to the specified file asynchronously.
        await File.WriteAllTextAsync(outputFilePath, certificate);
    }
}
