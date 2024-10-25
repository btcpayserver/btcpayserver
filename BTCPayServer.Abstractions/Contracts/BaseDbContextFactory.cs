using System;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Operations;

namespace BTCPayServer.Abstractions.Contracts
{
    public abstract class BaseDbContextFactory<T> where T : DbContext
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
        class CustomNpgsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
        {
#pragma warning disable EF1001 // Internal EF Core API usage.
            public CustomNpgsqlMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies, Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.INpgsqlSingletonOptions opts) : base(dependencies, opts)
#pragma warning restore EF1001 // Internal EF Core API usage.
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

        public void ConfigureBuilder(DbContextOptionsBuilder builder) => ConfigureBuilder(builder, null);
        public void ConfigureBuilder(DbContextOptionsBuilder builder, Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
        {
            builder
            .UseNpgsql(_options.Value.ConnectionString, o =>
            {
                o.EnableRetryOnFailure(10);
                o.SetPostgresVersion(12, 0);
                npgsqlOptionsAction?.Invoke(o);
                var mainSearchPath = GetSearchPath(_options.Value.ConnectionString);
                var schemaPrefix = string.IsNullOrEmpty(_migrationTableName) ? "__EFMigrationsHistory" : _migrationTableName;
                o.MigrationsHistoryTable(schemaPrefix, mainSearchPath);
            })
            .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();
        }

        private string GetSearchPath(string connectionString)
        {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
            var searchPaths = connectionStringBuilder.SearchPath?.Split(',');
            return searchPaths is not { Length: > 0 } ? null : searchPaths[0];
        }
    }
}
