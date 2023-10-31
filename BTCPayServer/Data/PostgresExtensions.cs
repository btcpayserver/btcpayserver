using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;

namespace BTCPayServer.Data;

public static class PostgresExtensions
{
    /// <summary>
    /// Asynchronously bulk inserts or upserts (inserts or updates) a collection of entities into the PostgreSQL database associated with the given dbContext. 
    /// This method leverages temporary tables and PostgreSQL's binary import functionality for efficient bulk operations, 
    /// especially beneficial when dealing with a large volume of data.
    /// Depending on the value of the 'upsert' parameter, the method either performs a simple insert ignoring conflicts or updates the existing records.
    /// </summary>
    /// <param name="dbContext">The application's database context.</param>
    /// <param name="entities">A collection of entities to be added or updated in the database.</param>
    /// <param name="connection">An optional PostgreSQL connection. If not provided, a connection is established using the provided dbContext.</param>
    /// <param name="upsert">A flag to determine if the operation should be an upsert (true) or a simple insert (false).</param>
    /// <typeparam name="TEntity">The type of the entity to be added or updated.</typeparam>
    /// <exception cref="NotSupportedException">Thrown when the database is not PostgreSQL.</exception>
    /// <exception cref="Exception">Thrown for PostgreSQL-specific errors or general exceptions, notably when there's an invalid .NET to NpgsqlDbType mapping.</exception>
    public static async Task BulkAddAsync<TEntity>
    (
        this ApplicationDbContext dbContext,
        IEnumerable<TEntity> entities,
        NpgsqlConnection connection = null,
        bool upsert = false
    )
        where TEntity : class
    {
        if (!dbContext.Database.IsNpgsql())
        {
            throw new NotSupportedException("BulkAddAsync is only supported for Postgres");
        }

        bool ownConnection = false;
        if (connection is null)
        {
            ownConnection = true;
            connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            await connection.OpenAsync();
        }

        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        var tableName = entityType.GetTableName();
        var tableSchema = entityType.GetSchema();

        var tableIdentifer = $"{(tableSchema is null ? string.Empty : $"\"{tableSchema}\".")}\"{tableName}\"";
        var storeObjectIdentifier = StoreObjectIdentifier.Table(tableName, tableSchema);
        var columns = entityType.GetProperties();
        var colKeys = string.Join(',', columns.Select(key => $"\"{key.GetColumnName(storeObjectIdentifier)}\""));

        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(
            $"CREATE TEMP TABLE tmp_table (LIKE {tableIdentifer} INCLUDING DEFAULTS) ON COMMIT DROP;");
        var command =
            $"copy tmp_table ({colKeys}) from stdin binary";

        await using var writer = await connection.BeginBinaryImportAsync(command);

        foreach (var entity in entities)
        {
            await writer.StartRowAsync();
            foreach (var column in columns)
            {
                var value = column.GetGetter().GetClrValue(entity);
                if (value == null)
                {
                    await writer.WriteNullAsync();
                }
                else if (column.GetTypeMapping() is NpgsqlTypeMapping npgsqlTypeMapping)
                {
                    var dbType = npgsqlTypeMapping.NpgsqlDbType;
                    await writer.WriteAsync(value, dbType);
                }
                else
                {
                    var dbType = column.GetColumnType(storeObjectIdentifier);
                    await writer.WriteAsync(value, dbType);
                }
            }
        }

        try
        {
            await writer.CompleteAsync();
            await writer.CloseAsync();
            await connection.ExecuteAsync(
                $"INSERT INTO {tableIdentifer} SELECT * FROM tmp_table ON CONFLICT DO {(upsert?"UPDATE": "NOTHING")};");
            await transaction.CommitAsync();
        }

        catch (PostgresException ex) when (ex.Message == "08P01: insufficient data left in message")
        {
            await transaction.RollbackAsync();
            throw new Exception(
                $"Postgres error most likely caused by invalid .NET to NpgsqlDbType mapping. Postgres Exception: {ex.Message}",
                ex);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (ownConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
