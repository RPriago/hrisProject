using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PeopleFlow.MultiTenancy.Abstractions;

namespace PeopleFlow.MultiTenancy.Interceptors;

/// <summary>
/// EF Core interceptor that enforces PostgreSQL Row Level Security (RLS) fail-closed boundaries
/// by binding the ambient tenant identifier to PostgreSQL session configuration for transactions,
/// synchronizing command-level session variables when tenant scope changes on an open connection,
/// and wiping session configuration when connections are closed or transactions are completed/rolled back.
/// </summary>
public sealed class NpgsqlTenantRlsInterceptor : DbConnectionInterceptor, IDbTransactionInterceptor, IDbCommandInterceptor
{
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ConditionalWeakTable<DbConnection, ConnectionRlsState> _connectionStates = new();
    private readonly AsyncLocal<bool> _isApplyingRls = new();

    private sealed class ConnectionRlsState
    {
        public Guid? LastAppliedTenantId { get; set; }
    }

    public const string SetTenantTransactionConfigSql = "SELECT set_config('app.current_tenant_id', @tenantId, true);";
    public const string SetTenantSessionConfigSql = "SELECT set_config('app.current_tenant_id', @tenantId, false);";
    public const string ResetTenantConfigSql = "SELECT set_config('app.current_tenant_id', '', false);";
    public const string ResetSessionSql = "SELECT set_config('app.current_tenant_id', '', false), set_config('app.outbox_dispatcher_mode', '', false);";

    // Backward-compatibility alias
    public const string SetTenantConfigSql = SetTenantTransactionConfigSql;

    public NpgsqlTenantRlsInterceptor(ITenantContextAccessor tenantContextAccessor)
    {
        _tenantContextAccessor = tenantContextAccessor ?? throw new ArgumentNullException(nameof(tenantContextAccessor));
    }

    #region DbConnectionInterceptor Overrides

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyTenantSession(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSessionAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ResetTenantSession(connection);
        _connectionStates.Remove(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ResetTenantSessionAsync(connection, default).ConfigureAwait(false);
        _connectionStates.Remove(connection);
        return await base.ConnectionClosingAsync(connection, eventData, result).ConfigureAwait(false);
    }

    #endregion

    #region IDbTransactionInterceptor Implementation

    public void TransactionStarted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (transaction?.Connection != null)
        {
            ApplyTenantSession(transaction.Connection, transaction);
        }
    }

    public async Task TransactionStartedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (transaction?.Connection != null)
        {
            await ApplyTenantSessionAsync(transaction.Connection, cancellationToken, transaction).ConfigureAwait(false);
        }
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (transaction?.Connection != null)
        {
            RestoreOrResetSessionAfterTransaction(transaction.Connection);
        }
    }

    public async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (transaction?.Connection != null)
        {
            await RestoreOrResetSessionAfterTransactionAsync(transaction.Connection, cancellationToken).ConfigureAwait(false);
        }
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (transaction?.Connection != null)
        {
            RestoreOrResetSessionAfterTransaction(transaction.Connection);
        }
    }

    public async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (transaction?.Connection != null)
        {
            await RestoreOrResetSessionAfterTransactionAsync(transaction.Connection, cancellationToken).ConfigureAwait(false);
        }
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (transaction?.Connection != null)
        {
            RestoreOrResetSessionAfterTransaction(transaction.Connection);
        }
    }

    public async Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (transaction?.Connection != null)
        {
            await RestoreOrResetSessionAfterTransactionAsync(transaction.Connection, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region IDbCommandInterceptor Implementation

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        SynchronizeTenantSession(command);
        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SynchronizeTenantSessionAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SynchronizeTenantSession(command);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SynchronizeTenantSessionAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        SynchronizeTenantSession(command);
        return result;
    }

    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SynchronizeTenantSessionAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }

    #endregion

    #region Private Command Execution Helpers

    private static bool IsPostgreSqlConnection(DbConnection connection)
    {
        if (connection == null) return false;
        var typeName = connection.GetType().Name;
        return typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Spy", StringComparison.OrdinalIgnoreCase);
    }

    private void SynchronizeTenantSession(DbCommand command)
    {
        if (_isApplyingRls.Value || command?.Connection == null || command.Connection.State != ConnectionState.Open || !IsPostgreSqlConnection(command.Connection))
        {
            return;
        }

        if (command.CommandText != null && command.CommandText.Contains("set_config('app.current_tenant_id'", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = _connectionStates.GetOrCreateValue(command.Connection);
        var currentTenantId = _tenantContextAccessor.TenantId;

        if (state.LastAppliedTenantId != currentTenantId)
        {
            var previous = _isApplyingRls.Value;
            _isApplyingRls.Value = true;
            try
            {
                if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty)
                {
                    ExecuteSetConfig(command.Connection, currentTenantId.Value, command.Transaction);
                }
                else
                {
                    ExecuteResetConfig(command.Connection, command.Transaction);
                }
                state.LastAppliedTenantId = currentTenantId;
            }
            finally
            {
                _isApplyingRls.Value = previous;
            }
        }
    }

    private async Task SynchronizeTenantSessionAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (_isApplyingRls.Value || command?.Connection == null || command.Connection.State != ConnectionState.Open || !IsPostgreSqlConnection(command.Connection))
        {
            return;
        }

        if (command.CommandText != null && command.CommandText.Contains("set_config('app.current_tenant_id'", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = _connectionStates.GetOrCreateValue(command.Connection);
        var currentTenantId = _tenantContextAccessor.TenantId;

        if (state.LastAppliedTenantId != currentTenantId)
        {
            var previous = _isApplyingRls.Value;
            _isApplyingRls.Value = true;
            try
            {
                if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty)
                {
                    await ExecuteSetConfigAsync(command.Connection, currentTenantId.Value, cancellationToken, command.Transaction).ConfigureAwait(false);
                }
                else
                {
                    await ExecuteResetConfigAsync(command.Connection, cancellationToken, command.Transaction).ConfigureAwait(false);
                }
                state.LastAppliedTenantId = currentTenantId;
            }
            finally
            {
                _isApplyingRls.Value = previous;
            }
        }
    }

    private void ApplyTenantSession(DbConnection connection, DbTransaction? transaction = null)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            var tenantId = _tenantContextAccessor.TenantId;
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                ExecuteSetConfig(connection, tenantId.Value, transaction);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = tenantId.Value;
            }
            else
            {
                ExecuteResetConfig(connection, transaction);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
            }
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private async Task ApplyTenantSessionAsync(
        DbConnection connection,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            var tenantId = _tenantContextAccessor.TenantId;
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                await ExecuteSetConfigAsync(connection, tenantId.Value, cancellationToken, transaction).ConfigureAwait(false);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = tenantId.Value;
            }
            else
            {
                await ExecuteResetConfigAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
            }
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private void ResetTenantSession(DbConnection connection, DbTransaction? transaction = null)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            ExecuteResetSession(connection, transaction);
            _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
        }
        catch
        {
            // Suppress exception on cleanup during connection closing or failed rollback
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private async Task ResetTenantSessionAsync(
        DbConnection connection,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            await ExecuteResetSessionAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
            _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
        }
        catch
        {
            // Suppress exception on cleanup during connection closing or failed rollback
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private void RestoreOrResetSessionAfterTransaction(DbConnection connection)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            var tenantId = _tenantContextAccessor.TenantId;
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                ExecuteSetConfig(connection, tenantId.Value, transaction: null);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = tenantId.Value;
            }
            else
            {
                ExecuteResetConfig(connection, transaction: null);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
            }
        }
        catch
        {
            // Suppress exception on cleanup during failed transaction or broken connection
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private async Task RestoreOrResetSessionAfterTransactionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        if (_isApplyingRls.Value || connection.State != ConnectionState.Open || !IsPostgreSqlConnection(connection))
        {
            return;
        }

        var previous = _isApplyingRls.Value;
        _isApplyingRls.Value = true;
        try
        {
            var tenantId = _tenantContextAccessor.TenantId;
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                await ExecuteSetConfigAsync(connection, tenantId.Value, cancellationToken, transaction: null).ConfigureAwait(false);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = tenantId.Value;
            }
            else
            {
                await ExecuteResetConfigAsync(connection, cancellationToken, transaction: null).ConfigureAwait(false);
                _connectionStates.GetOrCreateValue(connection).LastAppliedTenantId = null;
            }
        }
        catch
        {
            // Suppress exception on cleanup during failed transaction or broken connection
        }
        finally
        {
            _isApplyingRls.Value = previous;
        }
    }

    private static void ExecuteSetConfig(DbConnection connection, Guid tenantId, DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
            command.CommandText = SetTenantTransactionConfigSql;
        }
        else
        {
            command.CommandText = SetTenantSessionConfigSql;
        }

        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId.ToString();
        command.Parameters.Add(param);
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteSetConfigAsync(
        DbConnection connection,
        Guid tenantId,
        CancellationToken cancellationToken,
        DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
            command.CommandText = SetTenantTransactionConfigSql;
        }
        else
        {
            command.CommandText = SetTenantSessionConfigSql;
        }

        var param = command.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = tenantId.ToString();
        command.Parameters.Add(param);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ExecuteResetConfig(DbConnection connection, DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = ResetTenantConfigSql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteResetConfigAsync(
        DbConnection connection,
        CancellationToken cancellationToken,
        DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = ResetTenantConfigSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ExecuteResetSession(DbConnection connection, DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = ResetSessionSql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteResetSessionAsync(
        DbConnection connection,
        CancellationToken cancellationToken,
        DbTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = ResetSessionSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
