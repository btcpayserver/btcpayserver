using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public abstract class MigrationBase<TDbContext>(string migrationId)
    where TDbContext : DbContext
{
    public string MigrationId { get; } = migrationId;

    public abstract Task MigrateAsync(TDbContext dbContext, CancellationToken cancellationToken);
}
