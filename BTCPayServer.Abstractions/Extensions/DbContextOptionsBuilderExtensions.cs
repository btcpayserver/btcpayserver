#nullable enable
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Operations;

namespace BTCPayServer.Abstractions.Extensions;

public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures an EF Core context with BTCPay Server's PostgreSQL conventions.
    /// </summary>
    public static DbContextOptionsBuilder UseBTCPayServerDatabase(
        this DbContextOptionsBuilder builder,
        string connectionString,
        string? migrationHistoryTableName = null,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return builder
            .UseNpgsql(connectionString, options =>
            {
                options.EnableRetryOnFailure(10);
                options.SetPostgresVersion(14, 0);
                npgsqlOptionsAction?.Invoke(options);
                var historyTableName = string.IsNullOrEmpty(migrationHistoryTableName)
                    ? "__EFMigrationsHistory"
                    : migrationHistoryTableName;
                options.MigrationsHistoryTable(historyTableName, GetSearchPath(connectionString));
            })
            .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();
    }

    private static string? GetSearchPath(string connectionString)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var searchPaths = connectionStringBuilder.SearchPath?.Split(',');
        return searchPaths is not { Length: > 0 } ? null : searchPaths[0];
    }

    private sealed class CustomNpgsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
    {
#pragma warning disable EF1001 // Internal EF Core API usage.
        public CustomNpgsqlMigrationsSqlGenerator(
            MigrationsSqlGeneratorDependencies dependencies,
            Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.INpgsqlSingletonOptions options)
            : base(dependencies, options)
#pragma warning restore EF1001 // Internal EF Core API usage.
        {
        }

        protected override void Generate(
            NpgsqlCreateDatabaseOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            builder
                .Append("CREATE DATABASE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            // Indexed text columns are not used if PostgreSQL is not using the C locale.
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
}
