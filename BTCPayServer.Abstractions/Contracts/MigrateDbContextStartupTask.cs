using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Abstractions.Contracts;

public interface IDbContextMigrator
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

public class MigrateDbContextStartupTask<TDbContext>(IDbContextFactory<TDbContext> dbContextFactory) : IDbContextMigrator
    where TDbContext : DbContext
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Database.SetCommandTimeout(TimeSpan.FromDays(1.0));
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
