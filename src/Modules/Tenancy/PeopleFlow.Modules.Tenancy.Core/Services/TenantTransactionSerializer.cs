using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

internal static class TenantTransactionSerializer
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> NonNpgsqlTenantLocks = new();

    public static async Task<Result<T>> ExecuteAsync<T>(
        TenancyDbContext db,
        Guid tenantId,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            return await ExecuteCoreAsync(db, tenantId, lockRow: true, action, cancellationToken);
        }

        var gate = NonNpgsqlTenantLocks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteCoreAsync(db, tenantId, lockRow: false, action, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<Result<T>> ExecuteCoreAsync<T>(
        TenancyDbContext db,
        Guid tenantId,
        bool lockRow,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        var ownTx = db.Database.CurrentTransaction is null;
        var tx = ownTx ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (lockRow)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM tenancy_tenants WHERE id = {tenantId} FOR UPDATE",
                    cancellationToken);
            }

            var result = await action();
            if (ownTx && tx is not null)
            {
                if (result.IsSuccess)
                {
                    await tx.CommitAsync(cancellationToken);
                }
                else
                {
                    await tx.RollbackAsync(cancellationToken);
                }
            }

            return result;
        }
        catch
        {
            if (ownTx && tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }
}
