using System.Collections.Concurrent;
using PeopleFlow.Kernel.Time;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class ApiClientRateLimiter
{
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _windows = new();

    public ApiClientRateLimiter(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool TryConsume(Guid clientRecordId, int limitPerMinute)
    {
        var max = Math.Max(1, limitPerMinute);
        var now = _clock.UtcNow;
        var cutoff = now.AddMinutes(-1);
        var queue = _windows.GetOrAdd(clientRecordId, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= max)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
