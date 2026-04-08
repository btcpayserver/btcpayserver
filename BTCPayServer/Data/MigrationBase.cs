using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

class RawSqlMigration(string migrationId, string sql) : MigrationBase<ApplicationDbContext>(migrationId)
{
    public override Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    => dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken: cancellationToken);
}

public abstract class MigrationBase<TDbContext>(string migrationId)
    where TDbContext : DbContext
{
    public string MigrationId { get; } = migrationId;

    public abstract Task MigrateAsync(TDbContext dbContext, CancellationToken cancellationToken);
}
