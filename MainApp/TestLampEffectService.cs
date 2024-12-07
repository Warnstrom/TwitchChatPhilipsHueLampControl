namespace TwitchChatHueControls;

internal interface ITestLampEffectService
{
    ITestLampEffectService Test(string description, double durationMs = 5000);
    ITestLampEffectService Effect(EffectPalette effectName);
    ITestLampEffectService Duration(double durationMs);
    Task ExecuteAsync();
}

class TestLampEffectService(IHueController hueController, ILampEffectQueueService lampEffectQueueService) : ITestLampEffectService
{
    private string? _currentTestDescription = null;
    private double _currentTestDurationMs;
    private EffectPalette _currentEffectName = EffectPalette.Default;

    // General Test method
    public ITestLampEffectService Test(string description, double durationMs = 5000)
    {
        _currentTestDescription = description;
        _currentTestDurationMs = durationMs;
        Console.WriteLine($"Starting test: {description} for {durationMs}ms");
        return this;
    }

    public ITestLampEffectService Duration(double durationMs)
    {
        _currentTestDurationMs = durationMs;
        return this;
    }

    public ITestLampEffectService Effect(EffectPalette effectName)
    {
        _currentEffectName = effectName;
        return this;
    }

    // Execute the chained test
    public async Task ExecuteAsync()
    {

        var LightList = hueController.CreateCustomEffect(_currentEffectName);

        // Placeholder for interacting with the IHueController
        await lampEffectQueueService.EnqueueEffectAsync(async () =>
            {
                try
                {
                    await hueController.RunEffect(LightList, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing lamp effect: {ex}");
                }
            });
    }
}
