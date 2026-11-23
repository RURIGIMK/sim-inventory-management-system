using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SimInventory.Api.Data;

/// <summary>
/// Applies the reviewed, versioned SQL scripts in Data/Migrations in file-name order.
/// Each script runs once, inside its own transaction, under a session advisory lock so two
/// processes cannot migrate concurrently. A checksum is stored for every applied script and a
/// later change to an applied script stops the migration instead of silently diverging.
/// </summary>
public static class SchemaMigrator
{
    private const long MigrationLockKey = 73482190650001L;
    private const string ResourcePrefix = "migrations/";

    public sealed record Script(string Id, string Sql, string Checksum);

    public static IReadOnlyList<Script> LoadScripts()
    {
        var assembly = typeof(SchemaMigrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Missing migration resource {name}.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var sql = reader.ReadToEnd().Replace("\r\n", "\n");
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
                return new Script(name[ResourcePrefix.Length..^4], sql, checksum);
            })
            .ToList();
    }

    public static async Task<IReadOnlyList<string>> MigrateAsync(SimInventoryDbContext db, ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_lock({MigrationLockKey})", cancellationToken);
            await ExecuteAsync(connection, null, """
                CREATE TABLE IF NOT EXISTS "__SchemaMigrations" (
                    "Id" varchar(150) PRIMARY KEY,
                    "Checksum" varchar(64) NOT NULL,
                    "AppliedAt" timestamptz NOT NULL DEFAULT now()
                )
                """, cancellationToken);

            var existing = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT \"Id\", \"Checksum\" FROM \"__SchemaMigrations\"";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    existing[reader.GetString(0)] = reader.GetString(1);
            }

            foreach (var script in LoadScripts())
            {
                if (existing.TryGetValue(script.Id, out var checksum))
                {
                    if (!string.Equals(checksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Migration {script.Id} was changed after it was applied. Add a new migration instead of editing an applied one.");
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await ExecuteAsync(connection, transaction, script.Sql, cancellationToken);
                await using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO \"__SchemaMigrations\" (\"Id\", \"Checksum\") VALUES (@id, @checksum)";
                    AddParameter(insert, "id", script.Id);
                    AddParameter(insert, "checksum", script.Checksum);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                applied.Add(script.Id);
                logger?.LogInformation("Applied migration {Migration}", script.Id);
            }
        }
        finally
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None);
            if (openedHere) await connection.CloseAsync();
        }

        return applied;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
