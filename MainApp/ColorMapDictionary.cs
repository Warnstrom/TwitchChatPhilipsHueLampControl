using Microsoft.Extensions.Configuration;
namespace TwitchChatHueControls;
public interface IHexColorMapDictionary
{
    Task<string?> Get(string key);
    Task<Dictionary<string, string>> GetAllColorsAsync();
    Task<string> GetRandomColor();
}


internal class HexColorMapDictionary: IHexColorMapDictionary
{
    // JsonFileController is used to manage reading and writing JSON data from/to a file.
    private readonly JsonFileController jsonController;

    // Task used to ensure that colors are loaded asynchronously during class initialization.
    private readonly Task initializationTask;

    // Dictionary to store the hex color mappings (key: color name, value: hex code).
    Dictionary<string, string> HexColorMap = new Dictionary<string, string>();

    // Static constructor initializes the class by loading colors from the JSON file.
    public HexColorMapDictionary(string filePath, IConfigurationRoot configuration)
    {
        // The colors are loaded asynchronously, and the task is stored for later use.
        jsonController = new(filePath, configuration);
        initializationTask = LoadColors();
    }

    // Loads the color data from the JSON file into the HexColorMap dictionary.
    private async Task LoadColors()
    {
        HexColorMap = await jsonController.ReadAsDictionaryAsync();
    }

    // Retrieves the hex color value for the given key (color name).
    public async Task<string?> Get(string key)
    {
        // Ensure that the colors are loaded by awaiting the initialization task.
        await initializationTask;

        // Try to get the value from the dictionary using the provided key.
        HexColorMap.TryGetValue(key, out string value);

        // Return the found value or null if the key does not exist.
        return value;
    }

    // Returns all colors as a dictionary (color name and corresponding hex code).
    public async Task<Dictionary<string, string>> GetAllColorsAsync()
    {
        // Wait for the colors to be loaded.
        await initializationTask;

        // Return the dictionary containing all color mappings.
        return HexColorMap;
    }

    // Returns a random hex color value from the dictionary.
    public async Task<string> GetRandomColor()
    {
        // Ensure that the colors are loaded by awaiting the initialization task.
        await initializationTask;

        Random random = new(); // Create a new instance of the Random class.
        int index = random.Next(HexColorMap.Count); // Get a random index based on the dictionary size.

        // Return the color value at the randomly selected index.
        return HexColorMap.ElementAt(index).Value;
    }
}
