using System;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Abstractions.Contracts;

/// <summary>
/// Identifies a plugin database context and its migration-history table.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginDatabaseAttribute : Attribute
{
    public PluginDatabaseAttribute(string migrationHistoryTableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationHistoryTableName);
        MigrationHistoryTableName = migrationHistoryTableName;
    }

    public string MigrationHistoryTableName { get; }
}

/// <summary>
/// Provides runtime-safe design-time configuration for a plugin database context.
/// </summary>
public abstract class BasePluginDbContext<TContext> : DbContext
    where TContext : BasePluginDbContext<TContext>
{
    private const string DefaultDesignTimeConnectionString =
        "User ID=postgres;Include Error Detail=true;Host=127.0.0.1;Port=39372;Database=btcpay_plugin_design_time";

    protected BasePluginDbContext()
    {
    }

    protected BasePluginDbContext(DbContextOptions<TContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(ConfigureNpgsql);
            return;
        }

        if (!EF.IsDesignTime)
        {
            throw new InvalidOperationException(
                $"{typeof(TContext).FullName} must be created through dependency injection or IDbContextFactory<{typeof(TContext).Name}>.");
        }

        var database = (PluginDatabaseAttribute)Attribute.GetCustomAttribute(
            typeof(TContext),
            typeof(PluginDatabaseAttribute));
        if (database is null)
        {
            throw new InvalidOperationException(
                $"{typeof(TContext).FullName} must have a {nameof(PluginDatabaseAttribute)}.");
        }
        optionsBuilder.UseBTCPayServerDatabase(
            DefaultDesignTimeConnectionString,
            database.MigrationHistoryTableName,
            ConfigureNpgsql);
    }

    protected virtual void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder optionsBuilder)
    {
    }
}
