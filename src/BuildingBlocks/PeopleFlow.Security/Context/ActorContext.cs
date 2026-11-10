namespace PeopleFlow.Security.Context;

/// <summary>
/// Immutable snapshot of the ambient security actor context.
/// Tracks authenticated user, active session, security roles, and distributed correlation identifier.
/// </summary>
public sealed record ActorContext(
    Guid? UserId,
    Guid? SessionId,
    IReadOnlySet<string> Roles,
    Guid CorrelationId,
    bool IsAuthenticated)
{
    public static ActorContext Anonymous(Guid? correlationId = null) =>
        new(null, null, new HashSet<string>(), correlationId ?? Guid.CreateVersion7(), false);
}
