using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
namespace TwitchChatHueControls;

public interface IBridgeValidator
{
    public Task<bool> ValidateBridgeIpAsync(string bridgeIp, string bridgeId, string appKey);
}

internal class BridgeValidator : IBridgeValidator, IDisposable
{
    private readonly HttpClient _httpClient;

    public BridgeValidator()
    {
        // Set up the HttpClient with custom DNS resolution and certificate handling
        var handler = new HttpClientHandler();

        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
        {
            // Allow connections if there are no SSL policy errors
            if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                return true;

            // Load the custom CA certificate
            var customCaCert = new X509Certificate2("huebridge_cacert.pem");

            // Check if the certificate chain is signed by the custom CA certificate
            bool isCustomCaSigned = chain.ChainElements
                .Cast<X509ChainElement>()
                .Any(x => x.Certificate.Equals(customCaCert));

            // Check if the certificate issuer is the expected one
            bool isExpectedIssuer = cert.Issuer == "CN=root-bridge";

            // Allow if either custom CA signed or expected issuer
            return isCustomCaSigned || isExpectedIssuer;
        };

        _httpClient = new HttpClient(handler);

        // You can still set the host header if required
        _httpClient.DefaultRequestHeaders.Host = "bridgeId";
    }

    public async Task<bool> ValidateBridgeIpAsync(string localBridgeIp, string bridgeId, string appKey)
    {

        if (string.IsNullOrEmpty(localBridgeIp) || string.IsNullOrEmpty(appKey))
        {
            return false;
        }

        string url = $"https://{localBridgeIp}/api/{appKey}/config";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var validatedUri) || (validatedUri.Scheme != Uri.UriSchemeHttps))
        {
            Console.WriteLine("Invalid URI: The hostname could not be parsed.");
            return false;
        }

        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync();
            JsonNode? json = JsonNode.Parse(content);

            if (json is JsonObject jsonObject)
            {
                string? ipAddress = null;
                string? bridgeIdFromResponse = null;

                if (jsonObject.TryGetPropertyValue("ipaddress", out JsonNode? ipaddressValue))
                {
                    ipAddress = ipaddressValue?.AsValue().ToString();
                }

                if (jsonObject.TryGetPropertyValue("bridgeid", out JsonNode? bridgeIdValue))
                {
                    bridgeIdFromResponse = bridgeIdValue?.AsValue().ToString();
                }
                return ipAddress == localBridgeIp && bridgeIdFromResponse == bridgeId.ToUpper();
            }
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"Request error: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

}