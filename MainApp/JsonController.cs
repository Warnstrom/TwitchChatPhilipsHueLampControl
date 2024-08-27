using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

public interface IJsonFileController
{
    Task<T?> GetValueByKeyAsync<T>(string key);
    Task UpdateAsync(string key, string value);
}

public class JsonFileController : IJsonFileController
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private JsonObject _cachedJsonData;
    private readonly SemaphoreSlim _fileLock = new(1, 1); // Ensures thread-safe access to the file

    public JsonFileController(string filePath)
    {
        _filePath = filePath;
        _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        
        if (!File.Exists(_filePath))
        {
            InitializeDefaultJsonFile();
        }
    }

    // Initialize the JSON file with default content if it does not exist
    private void InitializeDefaultJsonFile()
    {
        var defaultJson = new JsonObject
        {
            ["bridgeIp"] = "",
            ["bridgeId"] = "",
            ["AppKey"] = "",
            ["HueStreamingClientKey"] = "",
            ["AccessToken"] = "",
            ["RefreshToken"] = "",
            ["ClientSecret"] = "",
            ["ClientId"] = "",
            ["RedirectUri"] = "http://localhost:8004/callback/",
            ["ApplicationVersion"] = "0.0.1"
        };

        File.WriteAllText(_filePath, defaultJson.ToJsonString(_jsonOptions));
    }

    // Reads and caches the JSON data from the file, with lazy initialization and caching
    private async Task<JsonObject> LoadJsonDataAsync()
    {
        if (_cachedJsonData != null) return _cachedJsonData;

        await _fileLock.WaitAsync(); // Ensure only one thread can access the file at a time
        try
        {
            if (_cachedJsonData == null)
            {
                using FileStream fs = File.OpenRead(_filePath);
                var jsonNode = await JsonSerializer.DeserializeAsync<JsonNode>(fs, _jsonOptions);
                _cachedJsonData = jsonNode as JsonObject ?? new JsonObject();
            }
        }
        catch (Exception ex)
        {
            // Handle I/O or JSON parsing errors
            Console.WriteLine($"Error reading JSON file: {ex.Message}");
            _cachedJsonData = new JsonObject(); // Return a fallback empty JSON object
        }
        finally
        {
            _fileLock.Release();
        }

        return _cachedJsonData;
    }

    // Writes the JSON data back to the file and refreshes the cache
    private async Task SaveJsonDataAsync(JsonObject data)
    {
        await _fileLock.WaitAsync();
        try
        {
            using FileStream fs = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(fs, data, _jsonOptions);
            _cachedJsonData = data; // Update the cache with the latest data
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing JSON file: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpdateAsync(string key, string value)
    {
        var jsonData = await LoadJsonDataAsync();
        jsonData[key] = value;
        await SaveJsonDataAsync(jsonData);
    }

    public async Task<T?> GetValueByKeyAsync<T>(string key)
    {
        var jsonData = await LoadJsonDataAsync();

        if (jsonData.TryGetPropertyValue(key, out JsonNode? value))
        {
            try
            {
                return value.Deserialize<T>();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error deserializing key '{key}': {ex.Message}");
            }
        }

        return default;
    }

    public async Task<Dictionary<string, string>> ReadAsDictionaryAsync()
    {
        var jsonData = await LoadJsonDataAsync();
        return jsonData.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? string.Empty
        );
    }
}
