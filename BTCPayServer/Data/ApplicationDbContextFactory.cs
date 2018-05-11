using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;

namespace BTCPayServer.Data
{
    public enum DatabaseType
    {
        Sqlite,
        Postgres
    }
    public class ApplicationDbContextFactory
    {
        string _ConnectionString;
        DatabaseType _Type;
        public ApplicationDbContextFactory(DatabaseType type, string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _Type = type;
        }

        public ApplicationDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            ConfigureBuilder(builder);
            return new ApplicationDbContext(builder.Options);
        }

        public void ConfigureBuilder(DbContextOptionsBuilder builder)
        {
            if (_Type == DatabaseType.Sqlite)
                builder.UseSqlite(_ConnectionString);
            else if (_Type == DatabaseType.Postgres)
                builder.UseNpgsql(_ConnectionString);
        }

        public void ConfigureHangfireBuilder(IGlobalConfiguration builder)
        {
            //if (_Type == DatabaseType.Sqlite)
            builder.UseMemoryStorage(); //Sql provider does not support multiple workers
            //else if (_Type == DatabaseType.Postgres)
            //    builder.UsePostgreSqlStorage(_ConnectionString);
        }
    }
}
