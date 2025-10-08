using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Data
{
    public class ApplicationDbContextFactory : BaseDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContextFactory(IOptions<DatabaseOptions> options, ILoggerFactory loggerFactory) : base(options, "")
        {
            LoggerFactory = loggerFactory;
        }

        public ILoggerFactory LoggerFactory { get; }

        public override ApplicationDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseLoggerFactory(LoggerFactory);
            builder.AddInterceptors(MigrationInterceptor.Instance);
            builder.ConfigureWarnings(w =>
            {
                w.Log((CoreEventId.OptimisticConcurrencyException, LogLevel.Debug));
                w.Log((CoreEventId.SaveChangesFailed, LogLevel.Debug));
            });
            ConfigureBuilder(builder, npgsqlOptionsAction);
            return new ApplicationDbContext(builder.Options);
        }
    }
}
