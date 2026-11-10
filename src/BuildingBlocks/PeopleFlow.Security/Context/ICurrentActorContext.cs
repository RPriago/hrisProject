namespace PeopleFlow.Security.Context;

/// <summary>
/// Exposes ambient security actor state (user, roles, correlation ID) across execution scopes.
/// </summary>
public interface ICurrentActorContext
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
    IReadOnlySet<string> Roles { get; }
    Guid CorrelationId { get; }
    bool IsAuthenticated { get; }
    ActorContext CurrentActor { get; }

    void SetActorContext(ActorContext actorContext);
    IDisposable BeginScope(ActorContext actorContext);
    void Clear();
}
