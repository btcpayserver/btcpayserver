using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BTCPayServer.Tests;

public class MigrationRegistrationTests
{
    [Fact]
    public void RegistersOneMigrationExecutorPerDbContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMigrationExecutor, MigrationExecutor<ApplicationDbContext>>();

        services.AddMigration<PluginDbContext, FirstPluginMigration>();
        services.AddMigration<PluginDbContext, SecondPluginMigration>();

        var executors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IMigrationExecutor))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        Assert.Equal(2, executors.Length);
        Assert.Contains(typeof(MigrationExecutor<ApplicationDbContext>), executors);
        Assert.Contains(typeof(MigrationExecutor<PluginDbContext>), executors);
    }

    private sealed class PluginDbContext(DbContextOptions<PluginDbContext> options) : DbContext(options);

    private sealed class FirstPluginMigration() : MigrationBase<PluginDbContext>("first")
    {
        public override Task MigrateAsync(PluginDbContext dbContext, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SecondPluginMigration() : MigrationBase<PluginDbContext>("second")
    {
        public override Task MigrateAsync(PluginDbContext dbContext, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
