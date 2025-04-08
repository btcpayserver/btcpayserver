using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests.Logging;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Tests
{
    public class DatabaseTester
    {
        private readonly ILoggerFactory _loggerFactory;
        public readonly string dbname;
        private string[] notAppliedMigrations;

        public DatabaseTester(ILog log, ILoggerFactory loggerFactory)
        {
            var connStr = Environment.GetEnvironmentVariable("TESTS_POSTGRES");
            if (string.IsNullOrEmpty(connStr))
                connStr = ServerTester.DefaultConnectionString;
            var r = RandomUtils.GetUInt32();
            dbname = $"btcpayserver{r}";
            connStr = connStr.Replace("btcpayserver", dbname);
            log.LogInformation("DB: " + dbname);
            ConnectionString = connStr;
            _loggerFactory = loggerFactory;
        }

        public ApplicationDbContextFactory CreateContextFactory()
        {
            return new ApplicationDbContextFactory(new OptionsWrapper<DatabaseOptions>(new DatabaseOptions()
            {
                ConnectionString = ConnectionString
            }), _loggerFactory);
        }

        public InvoiceRepository GetInvoiceRepository()
        {
            var logs = new BTCPayServer.Logging.Logs();
            logs.Configure(_loggerFactory);
            return new InvoiceRepository(CreateContextFactory(), new EventAggregator(logs));
        }

        public ApplicationDbContext CreateContext() => CreateContextFactory().CreateContext();

        public async Task MigrateAsync()
        {
            using var ctx = CreateContext();
            await EnsureCreatedAsync();
            await ctx.Database.MigrateAsync();
        }

        private async Task EnsureCreatedAsync()
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString);
            builder.Database = null;
            NpgsqlConnection conn = new NpgsqlConnection(builder.ToString());
            await conn.ExecuteAsync($"CREATE DATABASE \"{dbname}\";");
        }

        public async Task MigrateUntil(string migration = null)
        {
            using var ctx = CreateContext();
            var db = ctx.Database.GetDbConnection();
            await EnsureCreatedAsync();
            var migrations = ctx.Database.GetMigrations().ToArray();
            if (migration is not null)
            {
                var untilMigrationIdx = Array.IndexOf(migrations, migration);
                if (untilMigrationIdx == -1)
                    throw new InvalidOperationException($"Migration {migration} not found");
                notAppliedMigrations = migrations[untilMigrationIdx..];
                await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT, \"ProductVersion\" TEXT)");
                await db.ExecuteAsync("INSERT INTO \"__EFMigrationsHistory\" VALUES (@migration, '8.0.0')", notAppliedMigrations.Select(m => new { migration = m }).ToArray());
            }
            await ctx.Database.MigrateAsync();
        }

        public async Task ContinueMigration()
        {
            if (notAppliedMigrations is null)
                throw new InvalidOperationException("Call MigrateUpTo first");
            using var ctx = CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = ANY (@migrations)", new { migrations = notAppliedMigrations });
            await ctx.Database.MigrateAsync();
            notAppliedMigrations = null;
        }

        public string ConnectionString { get; }
    }
}
