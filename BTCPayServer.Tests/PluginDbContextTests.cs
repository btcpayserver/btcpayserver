using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Integration", "Integration")]
public class PluginDbContextTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    public void CanCreatePluginDbContextAtDesignTimeOnly()
    {
        using (var context = new TestPluginDbContext())
        {
            var exception = Assert.Throws<InvalidOperationException>(() => context.Database.GetDbConnection());
            Assert.Contains("must be created through dependency injection", exception.Message);
        }

        var wasDesignTime = EF.IsDesignTime;
        try
        {
            EF.IsDesignTime = true;
            using var context = new TestPluginDbContext();
            var connectionString = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
            Assert.Equal("127.0.0.1", connectionString.Host);
            Assert.Equal(39372, connectionString.Port);
            Assert.Equal("btcpay_plugin_design_time", connectionString.Database);
            Assert.Equal(42, context.Database.GetCommandTimeout());
            var history = context.Database.GetService<IHistoryRepository>();
            Assert.Contains("TestPluginMigrations", history.GetCreateScript());
        }
        finally
        {
            EF.IsDesignTime = wasDesignTime;
        }
    }

    [Fact]
    public async Task CanRegisterAndMigratePluginDbContext()
    {
        var database = CreateDBTester();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(LoggerFactory);
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions
        {
            ConnectionString = database.ConnectionString
        }));
        services.AddPluginDbContext<TestPluginDbContext>();
        services.AddMigration<TestPluginDbContext, SeedWidgetsMigration>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<TestPluginDbContext>>();
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<TestPluginDbContext>(scope.ServiceProvider.GetRequiredService<TestPluginDbContext>());
        Assert.Single(provider.GetServices<IMigrationExecutor>());

        var executor = provider.GetRequiredService<IMigrationExecutor>();
        await executor.Execute(CancellationToken.None);
        await executor.Execute(CancellationToken.None);

        await using var context = await factory.CreateDbContextAsync();
        Assert.Equal(42, context.Database.GetCommandTimeout());
        var history = context.Database.GetService<IHistoryRepository>();
        var appliedMigrations = await history.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, migration => migration.MigrationId == CreatePluginWidgetsMigration.Id);
        Assert.Contains(appliedMigrations, migration => migration.MigrationId == SeedWidgetsMigration.Id);
        Assert.Equal(1, await context.Database.ExecuteSqlRawAsync("DELETE FROM \"PluginWidgets\""));
    }

    [PluginDatabase("TestPluginMigrations")]
    public class TestPluginDbContext(DbContextOptions<TestPluginDbContext> options)
        : BasePluginDbContext<TestPluginDbContext>(options)
    {
        public TestPluginDbContext()
            : this(new DbContextOptions<TestPluginDbContext>())
        {
        }

        protected override void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.CommandTimeout(42);
        }
    }

    [DbContext(typeof(TestPluginDbContext))]
    [Migration(Id)]
    public class CreatePluginWidgetsMigration : Migration
    {
        public const string Id = "20260929000000_CreatePluginWidgets";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                "PluginWidgets",
                table => new
                {
                    Id = table.Column<string>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PluginWidgets", row => row.Id));
        }
    }

    public class SeedWidgetsMigration() : MigrationBase<TestPluginDbContext>(Id)
    {
        public const string Id = "20260929000001_SeedWidgets";

        public override Task MigrateAsync(TestPluginDbContext dbContext, CancellationToken cancellationToken)
        {
            return dbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"PluginWidgets\" (\"Id\") VALUES ('widget')",
                cancellationToken);
        }
    }
}
