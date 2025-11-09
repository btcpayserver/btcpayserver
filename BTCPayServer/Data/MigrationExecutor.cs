using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Data;

public interface IMigrationExecutor
{
    Task Execute(CancellationToken cancellationToken);
}

public class MigrationExecutor<TDbContext>(
    ILoggerFactory loggerFactory,
    IDbContextFactory<TDbContext> dbContextFactory,
    IEnumerable<MigrationBase<TDbContext>> migrations) : IMigrationExecutor
    where TDbContext : DbContext
{
    ILogger logger = loggerFactory.CreateLogger($"BTCPayServer.Migrations.{typeof(TDbContext).Name}");
    public async Task Execute(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var history = dbContext.Database.GetService<IHistoryRepository>();
        var appliedMigrations = (await history.GetAppliedMigrationsAsync(cancellationToken)).Select(m => m.MigrationId).ToHashSet();
        var insertedRows = new List<HistoryRow>();
        foreach (var migration in migrations
                     .Where(m => !appliedMigrations.Contains(m.MigrationId))
                     .OrderBy(m => m.MigrationId))
        {
            logger.LogInformation("Applying migration '{MigrationId}'", migration.MigrationId);
            await migration.MigrateAsync(dbContext, cancellationToken);
            insertedRows.Add(new HistoryRow(migration.MigrationId, ProductInfo.GetVersion()));
        }
        if (insertedRows.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            var insertMigrations =
                string.Concat(insertedRows
                    .Select(r => history.GetInsertScript(r))
                    .ToArray());
            await dbContext.Database.ExecuteSqlRawAsync(insertMigrations, cancellationToken);
        }
    }
}
