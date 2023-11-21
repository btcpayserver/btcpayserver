#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Util;
using AngleSharp.Text;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NBXplorer;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace BTCPayServer.Hosting
{
    static class TopologySort
    {
        public static IEnumerable<ITable> OrderByTopology(this IEnumerable<ITable> tables)
        {
            var comparer = Comparer<ITable>.Create((a, b) => a.Name.CompareTo(b.Name));
            return OrderByTopology(
                tables,
                t =>
                {
                    if (t.Name == "Invoices")
                        return t.ForeignKeyConstraints.Select(f => f.PrincipalTable.Name).Where(f => f != "Refunds");
                    else
                        return t.ForeignKeyConstraints.Select(f => f.PrincipalTable.Name);
                },
                t => t.Name,
                t => t,
                comparer);
        }
        public static IEnumerable<TValue> OrderByTopology<T, TDepend, TValue>(
            this IEnumerable<T> values,
            Func<T, IEnumerable<TDepend>> dependsOn,
            Func<T, TDepend> getKey,
            Func<T, TValue> getValue,
            IComparer<T>? solveTies = null) where T : notnull
        {
            var v = values.ToList();
            return TopologicalSort(v, dependsOn, getKey, getValue, solveTies);
        }

        static List<TValue> TopologicalSort<T, TDepend, TValue>(this IReadOnlyCollection<T> nodes,
                                      Func<T, IEnumerable<TDepend>> dependsOn,
                                      Func<T, TDepend> getKey,
                                      Func<T, TValue> getValue,
                                      IComparer<T>? solveTies = null) where T : notnull
        {
            if (nodes.Count == 0)
                return new List<TValue>();
            if (getKey == null)
                throw new ArgumentNullException(nameof(getKey));
            if (getValue == null)
                throw new ArgumentNullException(nameof(getValue));
            solveTies = solveTies ?? Comparer<T>.Default;
            List<TValue> result = new List<TValue>(nodes.Count);
            HashSet<TDepend> allKeys = new HashSet<TDepend>(nodes.Count);
            var noDependencies = new SortedDictionary<T, HashSet<TDepend>>(solveTies);

            foreach (var node in nodes)
                allKeys.Add(getKey(node));
            var dependenciesByValues = nodes.ToDictionary(node => node,
                                    node => new HashSet<TDepend>(dependsOn(node).Where(n => allKeys.Contains(n))));
            foreach (var e in dependenciesByValues.Where(x => x.Value.Count == 0))
            {
                noDependencies.Add(e.Key, e.Value);
            }
            if (noDependencies.Count == 0)
            {
                throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
            }
            while (noDependencies.Count > 0)
            {
                var nodep = noDependencies.First();
                noDependencies.Remove(nodep.Key);
                dependenciesByValues.Remove(nodep.Key);

                var elemKey = getKey(nodep.Key);
                result.Add(getValue(nodep.Key));
                foreach (var selem in dependenciesByValues)
                {
                    if (selem.Value.Remove(elemKey) && selem.Value.Count == 0)
                        noDependencies.Add(selem.Key, selem.Value);
                }
            }
            if (dependenciesByValues.Count != 0)
            {
                throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
            }
            return result;
        }
    }
    public class ToPostgresMigrationStartupTask : IStartupTask
    {

        public ToPostgresMigrationStartupTask(
            IConfiguration configuration,
            IOptions<DataDirectories> datadirs,
            ILogger<ToPostgresMigrationStartupTask> logger,
            IWebHostEnvironment environment,
            ApplicationDbContextFactory dbContextFactory)
        {
            Configuration = configuration;
            Datadirs = datadirs;
            Logger = logger;
            Environment = environment;
            DbContextFactory = dbContextFactory;
        }

        public IConfiguration Configuration { get; }
        public IOptions<DataDirectories> Datadirs { get; }
        public ILogger<ToPostgresMigrationStartupTask> Logger { get; }
        public IWebHostEnvironment Environment { get; }
        public ApplicationDbContextFactory DbContextFactory { get; }
        public bool HasError { get; private set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var p = Configuration.GetOrDefault<string?>("POSTGRES", null);
            var sqlite = Configuration.GetOrDefault<string?>("SQLITEFILE", null);
            var mysql = Configuration.GetOrDefault<string?>("MYSQL", null);

            string migratingFrom;
            ApplicationDbContext otherContext;
            if (string.IsNullOrEmpty(p))
            {
                return;
            }
            else if (!string.IsNullOrEmpty(sqlite))
            {
                migratingFrom = "SQLite";
                sqlite = Datadirs.Value.ToDatadirFullPath(sqlite);
                if (!File.Exists(sqlite))
                    return;
                otherContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=" + sqlite, o => o.CommandTimeout(60 * 60 * 10)).Options);
            }
            else if (!string.IsNullOrEmpty(mysql))
            {
                migratingFrom = "MySQL";
                otherContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseMySql(mysql, ServerVersion.AutoDetect(mysql), o => o.CommandTimeout(60 * 60 * 10)).Options);
                try
                {
                    await otherContext.Settings.FirstOrDefaultAsync();
                }
                catch (MySqlException ex) when (ex.SqlState == "42000") // DB doesn't exists
                {
                    return;
                }
            }
            else
            {
                return;
            }
            if (await otherContext.Settings.FirstOrDefaultAsync() == null)
                return;
            {
                var postgres = new NpgsqlConnectionStringBuilder(p);
                using var postgresContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(p, o =>
                {
                    o.CommandTimeout(60 * 60 * 10);
                    o.SetPostgresVersion(12, 0);
                }).Options);
                string? state;
                try
                {
                    state = await GetMigrationState(postgresContext);
                    if (state == "complete")
                        return;
                    if (state == null)
                        throw new ConfigException("This postgres database isn't created during a migration. Please use an empty database for postgres when migrating. If it's not a migration, remove --sqlitefile or --mysql settings.");
                }
                catch (NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName || ex.SqlState == PostgresErrorCodes.UndefinedTable) // DB doesn't exists
                {
                    await postgresContext.Database.MigrateAsync();
                    state = "pending";
                    await SetMigrationState(postgresContext, migratingFrom, "pending");
                }

                Logger.LogInformation($"Migrating from {migratingFrom} to Postgres...");
                if (state == "pending")
                {
                    Logger.LogInformation($"There is a unfinished migration in postgres... dropping all tables");
                    foreach (var t in postgresContext.Model.GetRelationalModel().Tables.OrderByTopology())
                    {
                        await postgresContext.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{t.Name}\" CASCADE");
                    }
                    await postgresContext.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"__EFMigrationsHistory\" CASCADE");
                    await postgresContext.Database.MigrateAsync();
                }
                else
                {
                    throw new ConfigException("This database isn't created during a migration. Please use an empty database for postgres when migrating.");
                }
                await otherContext.Database.MigrateAsync();

                await SetMigrationState(postgresContext, migratingFrom, "pending");

                foreach (var t in postgresContext.Model.GetRelationalModel().Tables.OrderByTopology())
                {
                    var typeMapping = t.EntityTypeMappings.Single();
                    var query = (IQueryable<object>)otherContext.GetType().GetMethod("Set", new Type[0])!.MakeGenericMethod(typeMapping.TypeBase.ClrType).Invoke(otherContext, null)!;
                    if (t.Name == "WebhookDeliveries" ||
                        t.Name == "InvoiceWebhookDeliveries" ||
                        t.Name == "StoreRoles")
                        continue;
                    Logger.LogInformation($"Migrating table: " + t.Name);
                    List<PropertyInfo> datetimeProperties = new List<PropertyInfo>();
                    foreach (var col in t.Columns)
                        if (col.PropertyMappings.Single().Property.ClrType == typeof(DateTime))
                        {
                            datetimeProperties.Add(col.PropertyMappings.Single().Property.PropertyInfo!);
                        }
                    List<PropertyInfo> datetimeoffsetProperties = new List<PropertyInfo>();
                    foreach (var col in t.Columns)
                        if (col.PropertyMappings.Single().Property.ClrType == typeof(DateTimeOffset))
                        {
                            datetimeoffsetProperties.Add(col.PropertyMappings.Single().Property.PropertyInfo!);
                        }
                    var rows = await query.ToListAsync();
                    foreach (var row in rows)
                    {
                        foreach (var prop in datetimeProperties)
                        {
                            var v = (DateTime)prop.GetValue(row)!;
                            if (v.Kind == DateTimeKind.Unspecified)
                            {
                                v = new DateTime(v.Ticks, DateTimeKind.Utc);
                                prop.SetValue(row, v);
                            }
                            else if (v.Kind == DateTimeKind.Local)
                            {
                                prop.SetValue(row, v.ToUniversalTime());
                            }
                        }
                        foreach (var prop in datetimeoffsetProperties)
                        {
                            var v = (DateTimeOffset)prop.GetValue(row)!;
                            if (v.Offset != TimeSpan.Zero)
                            {
                                prop.SetValue(row, v.ToOffset(TimeSpan.Zero));
                            }
                        }
                        postgresContext.Entry(row).State = EntityState.Added;
                    }
                    await postgresContext.SaveChangesAsync();
                    postgresContext.ChangeTracker.Clear();
                }
                await postgresContext.SaveChangesAsync();
                postgresContext.ChangeTracker.Clear();
                await UpdateSequenceInvoiceSearch(postgresContext);
                await SetMigrationState(postgresContext, migratingFrom, "complete");
            }
            otherContext.Dispose();
            SqliteConnection.ClearAllPools();
            MySqlConnection.ClearAllPools();

            Logger.LogInformation($"Migration to postgres from {migratingFrom} successful");
        }

        internal static async Task UpdateSequenceInvoiceSearch(ApplicationDbContext postgresContext)
        {
            await postgresContext.Database.ExecuteSqlRawAsync("SELECT SETVAL('\"InvoiceSearches_Id_seq\"', (SELECT max(\"Id\") FROM \"InvoiceSearches\"));");
        }

        internal static async Task<string?> GetMigrationState(ApplicationDbContext postgresContext)
        {
            var o = (await postgresContext.Settings.FromSqlRaw("SELECT \"Id\", \"Value\" FROM \"Settings\" WHERE \"Id\"='MigrationData'").AsNoTracking().FirstOrDefaultAsync())?.Value;
            if (o is null)
                return null;
            return JObject.Parse(o)["state"]?.Value<string>();
        }
        private static async Task SetMigrationState(ApplicationDbContext postgresContext, string migratingFrom, string state)
        {
            await postgresContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Settings\" VALUES ('MigrationData', @p0::JSONB) ON CONFLICT (\"Id\") DO UPDATE SET \"Value\"=@p0::JSONB",
                new[] { $"{{ \"from\": \"{migratingFrom}\", \"state\": \"{state}\" }}" });
        }
    }
}
