using Hangfire;
using Hangfire.PostgreSql;

namespace PeopleFlow.Jobs.Configuration;

/// <summary>
/// Configuration helpers for isolated Hangfire PostgreSQL storage schema.
/// </summary>
public static class HangfirePostgreSqlOptionsExtensions
{
    public const string DefaultHangfireSchema = "hangfire";

    public static PostgreSqlStorageOptions CreateDefaultPostgreSqlStorageOptions(string schemaName = DefaultHangfireSchema)
    {
        return new PostgreSqlStorageOptions
        {
            SchemaName = schemaName,
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(15)
        };
    }

    public static IGlobalConfiguration UsePeopleFlowPostgreSql(
        this IGlobalConfiguration configuration,
        string connectionString,
        Action<PostgreSqlStorageOptions>? configureOptions = null)
    {
        var options = CreateDefaultPostgreSqlStorageOptions();
        configureOptions?.Invoke(options);
        return configuration.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), options);
    }
}
