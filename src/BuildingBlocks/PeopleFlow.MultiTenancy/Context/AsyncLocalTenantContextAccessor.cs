using PeopleFlow.MultiTenancy.Abstractions;

namespace PeopleFlow.MultiTenancy.Context;

/// <summary>
/// Ambient async-local implementation of <see cref="ITenantContextAccessor"/> suitable for ASP.NET Core requests and Hangfire background workers.
/// </summary>
public class AsyncLocalTenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> _currentTenant = new();

    public TenantContext CurrentTenant => _currentTenant.Value ?? TenantContext.Empty;

    public Guid? TenantId => _currentTenant.Value?.TenantId;

    public string? SubscriptionStatus => _currentTenant.Value?.SubscriptionStatus;

    public bool IsSupportAccess => _currentTenant.Value?.IsSupportAccess ?? false;

    public void SetTenantContext(TenantContext? context)
    {
        _currentTenant.Value = context;
    }

    public void Clear()
    {
        _currentTenant.Value = null;
    }

    public IDisposable BeginScope(TenantContext context)
    {
        var previous = _currentTenant.Value;
        _currentTenant.Value = context;
        return new Scope(() => _currentTenant.Value = previous);
    }

    public IDisposable BeginScope(Guid tenantId, string? companySlug = null)
    {
        return BeginScope(new TenantContext(tenantId, TenantCode: companySlug));
    }

    private sealed class Scope : IDisposable
    {
        private Action? _onDispose;

        public Scope(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}
