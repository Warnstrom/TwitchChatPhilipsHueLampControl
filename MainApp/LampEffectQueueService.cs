using System.Threading.Channels;

namespace TwitchChatHueControls
{

    internal interface ILampEffectQueueService
    {
        Task EnqueueEffectAsync(Func<Task> effectAction);
        Task StopAsync();
    }

    public class LampEffectQueueService : ILampEffectQueueService
    {
        private readonly Channel<Func<Task>> _effectQueue;
        private Task _queueProcessorTask;
        private readonly CancellationTokenSource _cts = new();

        public LampEffectQueueService(int capacity = 100)
        {
            _effectQueue = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
            StartQueueProcessor();
        }

        /// <summary>
        /// Enqueue a new effect action.
        /// </summary>
        public async Task EnqueueEffectAsync(Func<Task> effectAction)
        {
            ArgumentNullException.ThrowIfNull(effectAction);
            await _effectQueue.Writer.WriteAsync(effectAction);
        }

        /// <summary>
        /// Start processing the queue.
        /// </summary>
        private void StartQueueProcessor()
        {
            _queueProcessorTask = Task.Run(async () =>
            {
                await foreach (var effectAction in _effectQueue.Reader.ReadAllAsync(_cts.Token))
                {
                    try
                    {
                        await effectAction();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing effect: {ex}");
                    }
                }
            });
        }

        /// <summary>
        /// Stop the queue processor.
        /// </summary>
        public async Task StopAsync()
        {
            _effectQueue.Writer.Complete();
            _cts.Cancel();
            await _queueProcessorTask;
        }
    }
}
