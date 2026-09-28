# Plugin data and migrations

Own plugin data explicitly. Do not add plugin tables or migrations to BTCPay Server's application model, and do not depend on undocumented core table layouts.

## Database context

Use a plugin-specific EF Core `DbContext` and factory. Do not construct the runtime connection string yourself.

The [Payroll plugin](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/tree/master/Plugins/BTCPayServer.RockstarDev.Plugins.Payroll) provides a complete example. Its context accepts `DbContextOptions` so the factory and dependency injection can configure it:

```csharp
public class PluginDbContext(DbContextOptions<PluginDbContext> options)
    : DbContext(options)
{
    public DbSet<Widget> Widgets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("YourPlugin");
    }
}
```

Create a factory derived from `BaseDbContextFactory<T>`. It supplies BTCPay Server's PostgreSQL connection, retry behavior, and migration-history configuration. The name passed to the base constructor identifies your plugin's migration history table, so keep it stable and unique:

```csharp
public class PluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<PluginDbContext>(options, "YourPlugin")
{
    public override PluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new PluginDbContext(builder.Options);
    }
}
```

Register both the factory and the context from the plugin's `Execute` method:

```csharp
serviceCollection.AddSingleton<PluginDbContextFactory>();
serviceCollection.AddDbContext<PluginDbContext>((provider, builder) =>
{
    var factory = provider.GetRequiredService<PluginDbContextFactory>();
    factory.ConfigureBuilder(builder);
});
serviceCollection.AddHostedService<PluginMigrationRunner>();
```

Inject `PluginDbContext` into scoped services. In singleton or background services, inject `PluginDbContextFactory` and call `CreateContext()` for each unit of work. A startup migration runner can do the same and call `context.Database.MigrateAsync(cancellationToken)`. See Payroll's [`PluginDbContextFactory`](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/Data/PluginDbContextFactory.cs), [service registration](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/Program.cs), and [`PluginMigrationRunner`](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/Data/PluginMigrationRunner.cs).

For `dotnet ef`, add an `IDesignTimeDbContextFactory<PluginDbContext>` that builds the context with a development PostgreSQL connection. EF uses this factory only while generating migrations; the runtime factory still supplies the server's configured connection. See Payroll's [`DesignTimeDbContextFactory`](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/Data/DesignTimeDbContextFactory.cs).

## Create a migration

Use the .NET target framework and EF Core package versions declared by the [`BTCPayServer.Data.csproj`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Data/BTCPayServer.Data.csproj) project referenced by your plugin. From the plugin repository root:

1. Update the plugin model.
2. Generate the migration, specifying the plugin project, context, and output directory:

   ```sh
   dotnet ef migrations add <migration-name> \
       --project <plugin-project> \
       --context PluginDbContext \
       --output-dir Data/Migrations
   ```

3. Copy the class attributes from the generated `.Designer.cs` file to the migration `.cs` file.
4. Remove the generated `.Designer.cs` file.
5. Remove the `Down()` method.
6. Review the migration, model snapshot, and generated SQL implications, then commit the migration and snapshot with the model change.

The Payroll plugin keeps its project under `Plugins/BTCPayServer.RockstarDev.Plugins.Payroll`, so its equivalent generation command is:

```sh
dotnet ef migrations add <migration-name> \
    --project Plugins/BTCPayServer.RockstarDev.Plugins.Payroll \
    --context PluginDbContext \
    --output-dir Data/Migrations
```

Plugin migrations target PostgreSQL. Do not use `migrationBuilder.IsNpgsql()`, and follow PostgreSQL naming conventions. Never edit or remove a migration already shipped to users; add a forward migration instead. Test both installation into an empty database and an upgrade from the previous plugin schema when a change has meaningful data or compatibility risk.

## Run migrations

Run generated EF migrations at startup through the registered `PluginMigrationRunner`, which creates a context from `PluginDbContextFactory` and calls `context.Database.MigrateAsync(cancellationToken)`.

For startup data migrations outside the EF schema history, use the migration registration contracts exposed by BTCPay Server, such as `AddMigration<TDbContext, TMigration>`. The generic context must have a registered `IDbContextFactory<TDbContext>`. Keep migration identifiers unique within that context and ordered; date-prefixed identifiers are recommended for raw SQL migrations.

If you use those contracts with the plugin context, register the same factory through the interface as well:

```csharp
serviceCollection.AddSingleton<IDbContextFactory<PluginDbContext>>(provider =>
    provider.GetRequiredService<PluginDbContextFactory>());
```

Use repositories or focused data services around the context so controllers and background services do not leak context lifetimes. Do not retain a scoped context in a singleton; create a scope or use the registered factory for each unit of work.

See [`BaseDbContextFactory<T>`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/Contracts/BaseDbContextFactory.cs) and the core [database migration conventions](https://github.com/btcpayserver/btcpayserver/blob/master/docs/maintainers/README.md#database-migrations).
