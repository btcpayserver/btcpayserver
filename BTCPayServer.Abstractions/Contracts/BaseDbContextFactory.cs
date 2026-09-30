using System;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Abstractions.Contracts
{
    public abstract class BaseDbContextFactory<T> : IDbContextFactory<T> where T : DbContext
    {
        private readonly IOptions<DatabaseOptions> _options;
        private readonly string _migrationTableName;

        public BaseDbContextFactory(IOptions<DatabaseOptions> options, string migrationTableName)
        {
            _options = options;
            _migrationTableName = migrationTableName;
        }

        public T CreateContext() => CreateContext(null);
        public abstract T CreateContext(Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null);
        public void ConfigureBuilder(DbContextOptionsBuilder builder) => ConfigureBuilder(builder, null);
        public void ConfigureBuilder(DbContextOptionsBuilder builder, Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
        {
            builder.UseBTCPayServerDatabase(
                _options.Value.ConnectionString,
                _migrationTableName,
                npgsqlOptionsAction);
        }

        T IDbContextFactory<T>.CreateDbContext()
            => this.CreateContext();
    }
}
