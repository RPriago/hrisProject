namespace PeopleFlow.Security.Context;

/// <summary>
/// Mutable or pre-initialized actor context implementing <see cref="ICurrentActorContext"/>.
/// Useful for scoped test contexts and ambient actor tracking.
/// </summary>
public class CurrentActorContext : ICurrentActorContext
{
    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? SessionId { get; set; }
    public IReadOnlySet<string> Roles { get; set; }
    public Guid CorrelationId { get; set; }
    public bool IsAuthenticated => UserId.HasValue && UserId.Value != Guid.Empty;

    public ActorContext CurrentActor => new(
        UserId,
        SessionId,
        Roles,
        CorrelationId,
        IsAuthenticated);

    public CurrentActorContext(
        Guid? userId = null,
        Guid? tenantId = null,
        IEnumerable<string>? roles = null,
        Guid? sessionId = null,
        Guid? correlationId = null)
    {
        UserId = userId;
        TenantId = tenantId;
        Roles = roles != null ? new HashSet<string>(roles) : new HashSet<string>();
        SessionId = sessionId;
        CorrelationId = correlationId ?? Guid.CreateVersion7();
    }

    public void SetActorContext(ActorContext actorContext)
    {
        ArgumentNullException.ThrowIfNull(actorContext);
        UserId = actorContext.UserId;
        SessionId = actorContext.SessionId;
        Roles = actorContext.Roles;
        CorrelationId = actorContext.CorrelationId;
    }

    public IDisposable BeginScope(ActorContext actorContext)
    {
        ArgumentNullException.ThrowIfNull(actorContext);
        var prevUser = UserId;
        var prevSession = SessionId;
        var prevRoles = Roles;
        var prevCorr = CorrelationId;

        SetActorContext(actorContext);

        return new Scope(() =>
        {
            UserId = prevUser;
            SessionId = prevSession;
            Roles = prevRoles;
            CorrelationId = prevCorr;
        });
    }

    public void Clear()
    {
        UserId = null;
        SessionId = null;
        Roles = new HashSet<string>();
        CorrelationId = Guid.CreateVersion7();
    }

    private sealed class Scope : IDisposable
    {
        private Action? _onDispose;
        public Scope(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
