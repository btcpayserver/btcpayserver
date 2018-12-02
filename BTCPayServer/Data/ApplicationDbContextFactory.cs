using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using JetBrains.Annotations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BTCPayServer.Data
{
    public enum DatabaseType
    {
        Sqlite,
        Postgres,
        MySQL,
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


        public DatabaseType Type
        {
            get
            {
                return _Type;
            }
        }

        public ApplicationDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            ConfigureBuilder(builder);
            return new ApplicationDbContext(builder.Options);
        }

        class CustomNpgsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
        {
            public CustomNpgsqlMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies) : base(dependencies)
            {
            }

            protected override void Generate(NpgsqlCreateDatabaseOperation operation, IModel model, MigrationCommandListBuilder builder)
            {
                builder
                     .Append("CREATE DATABASE ")
                     .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

                // POSTGRES gotcha: Indexed Text column (even if PK) are not used if we are not using C locale
                builder
                    .Append(" TEMPLATE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("template0"));

                builder
                    .Append(" LC_CTYPE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("C"));

                builder
                    .Append(" LC_COLLATE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("C"));

                builder
                    .Append(" ENCODING ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("UTF8"));

                if (operation.Tablespace != null)
                {
                    builder
                        .Append(" TABLESPACE ")
                        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Tablespace));
                }

                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

                EndStatement(builder, suppressTransaction: true);
            }
        }

        public void ConfigureBuilder(DbContextOptionsBuilder builder)
        {
            if (_Type == DatabaseType.Sqlite)
                builder.UseSqlite(_ConnectionString);
            else if (_Type == DatabaseType.Postgres)
                builder
                    .UseNpgsql(_ConnectionString)
                    .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();
            else if (_Type == DatabaseType.MySQL)
                builder.UseMySql(_ConnectionString);
        }

        public void ConfigureHangfireBuilder(IGlobalConfiguration builder)
        {
            builder.UseMemoryStorage();
            //We always use memory storage because of incompatibilities with the latest postgres in 2.1
            //if (_Type == DatabaseType.Sqlite)
            //    builder.UseMemoryStorage(); //Sqlite provider does not support multiple workers
            //else if (_Type == DatabaseType.Postgres)
            //    builder.UsePostgreSqlStorage(_ConnectionString);
        }
    }
}
