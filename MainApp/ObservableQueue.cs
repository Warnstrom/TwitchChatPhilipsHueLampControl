using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchChatHueControls.Models;

namespace TwitchChatHueControls;

internal interface IObservableQueue
{
    event EventHandler<LightRequest> ItemEnqueued;
    event EventHandler<LightRequest> ItemDequeued;
    event EventHandler QueueCleared;
    void Enqueue(LightRequest item);
    LightRequest Dequeue();
    LightRequest Peek();
    void Clear();
    int Count();
    bool IsEmpty();
}

internal class ObservableQueue : IObservableQueue
{
    private readonly Queue<LightRequest> _queue = new Queue<LightRequest>();
    private bool _isProcessing = false;

    // Events
    public event EventHandler<LightRequest> ItemEnqueued;
    public event EventHandler<LightRequest> ItemDequeued;
    public event EventHandler QueueCleared;

    // Properties
    public int Count() => _queue.Count;
    public bool IsEmpty() => _queue.Count == 0;

    // Enqueue method
    public void Enqueue(LightRequest item)
    {
        _queue.Enqueue(item);

        // Raise events
        ItemEnqueued?.Invoke(this, item);
    }

    // Dequeue method
    public LightRequest Dequeue()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Queue is empty");

        LightRequest item = _queue.Dequeue();

        // Raise events
        ItemDequeued?.Invoke(this, item);

        return item;
    }

    public LightRequest Peek()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("Queue is empty");

        return _queue.Peek();
    }

    public void Clear()
    {
        _queue.Clear();

        // Raise event
        QueueCleared?.Invoke(this, EventArgs.Empty);
    }

}
