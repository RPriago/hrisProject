namespace PeopleFlow.Kernel.Time;

public sealed class SystemClock : IClock
{
    public SystemClock(TimeProvider? timeProvider = null)
    {
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeProvider TimeProvider { get; }

    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
