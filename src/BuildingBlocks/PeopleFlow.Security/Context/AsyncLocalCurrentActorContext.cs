namespace PeopleFlow.Security.Context;

/// <summary>
/// Ambient actor context backed by AsyncLocal storage for async call flows.
/// </summary>
public sealed class AsyncLocalCurrentActorContext : ICurrentActorContext
{
    private readonly AsyncLocal<ActorContext?> _currentActor = new();

    public ActorContext CurrentActor => _currentActor.Value ?? ActorContext.Anonymous();

    public Guid? UserId => CurrentActor.UserId;
    public Guid? SessionId => CurrentActor.SessionId;
    public IReadOnlySet<string> Roles => CurrentActor.Roles;
    public Guid CorrelationId => CurrentActor.CorrelationId;
    public bool IsAuthenticated => CurrentActor.IsAuthenticated;

    public void SetActorContext(ActorContext actorContext)
    {
        _currentActor.Value = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
    }

    public IDisposable BeginScope(ActorContext actorContext)
    {
        ArgumentNullException.ThrowIfNull(actorContext);
        var previous = _currentActor.Value;
        _currentActor.Value = actorContext;
        return new ActorScope(this, previous);
    }

    public void Clear()
    {
        _currentActor.Value = null;
    }

    private sealed class ActorScope : IDisposable
    {
        private readonly AsyncLocalCurrentActorContext _parent;
        private readonly ActorContext? _previous;
        private bool _disposed;

        public ActorScope(AsyncLocalCurrentActorContext parent, ActorContext? previous)
        {
            _parent = parent;
            _previous = previous;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _parent._currentActor.Value = _previous;
                _disposed = true;
            }
        }
    }
}
