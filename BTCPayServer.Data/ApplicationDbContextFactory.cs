using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Data
{
    public class ApplicationDbContextFactory : BaseDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "")
        {
        }

        public override ApplicationDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.AddInterceptors(Data.InvoiceData.MigrationInterceptor.Instance);
            ConfigureBuilder(builder, npgsqlOptionsAction);
            return new ApplicationDbContext(builder.Options);
        }
    }
}
