namespace PeopleFlow.Kernel.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayUtc { get; }
    TimeProvider TimeProvider { get; }
}
