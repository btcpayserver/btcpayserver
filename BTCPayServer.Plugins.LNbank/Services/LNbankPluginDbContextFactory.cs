using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LNbankPluginDbContext>
    {
        public LNbankPluginDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<LNbankPluginDbContext>();
            
            // FIXME: Somehow the DateTimeOffset column types get messed up when not using Postgres
            // https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers?tabs=dotnet-core-cli
            // builder.UseSqlite("Data Source=temp.db");
            builder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39372;Database=designtimebtcpay" );

            return new LNbankPluginDbContext(builder.Options, true);
        }
    }

    public class LNbankPluginDbContextFactory : BaseDbContextFactory<LNbankPluginDbContext>
    {
        public LNbankPluginDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "BTCPayServer.Plugins.LNbank")
        {
        }

        public override LNbankPluginDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<LNbankPluginDbContext>();
            ConfigureBuilder(builder);
            return new LNbankPluginDbContext(builder.Options);
        }
    }
}
