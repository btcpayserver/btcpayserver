# Plugin data and migrations

Own plugin data explicitly. Do not add plugin tables or migrations to BTCPay Server's application model, and do not depend on undocumented core table layouts.

## Database context

Use a plugin-specific EF Core `DbContext` derived from `BasePluginDbContext<TContext>`. The `PluginDatabase` attribute specifies the exact name of the plugin's migration-history table, so keep it stable and unique. The parameterless constructor allows `dotnet ef` to create the context, while the options constructor allows BTCPay Server to configure it at runtime:

```csharp
[PluginDatabase("YourPlugin_Migrations")]
public class PluginDbContext(DbContextOptions<PluginDbContext> options)
    : BasePluginDbContext<PluginDbContext>(options)
{
    public PluginDbContext()
        : this(new DbContextOptions<PluginDbContext>())
    {
    }

    public DbSet<Widget> Widgets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("YourPlugin");
    }
}
```

Register the context from the plugin's `Execute` method:

```csharp
serviceCollection.AddPluginDbContext<PluginDbContext>();
```

`AddPluginDbContext` configures BTCPay Server's PostgreSQL connection and retry behavior, registers the context as scoped, registers `IDbContextFactory<PluginDbContext>`, and runs the context's EF migrations during startup. A context's `HasDefaultSchema` setting does not change the schema of the migration-history table; the table uses the first search path from BTCPay Server's PostgreSQL connection.

Inject `PluginDbContext` into scoped services. In singleton or background services, inject `IDbContextFactory<PluginDbContext>` and create and dispose a context for each unit of work:

```csharp
public class WidgetProcessor(IDbContextFactory<PluginDbContext> contextFactory)
{
    public async Task Process(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Use the context for this unit of work.
    }
}
```

`dotnet ef` requires the Docker Compose test environment to be running. See [local development and testing](../../maintainers/local-development.md#test-environment) for startup instructions.

## Create a migration

Use the .NET target framework declared by [`Build/Common.csproj`](https://github.com/btcpayserver/btcpayserver/blob/master/Build/Common.csproj) and the EF Core and Npgsql package versions declared by [`BTCPayServer.Abstractions.csproj`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/BTCPayServer.Abstractions.csproj) in the BTCPay Server version referenced by your plugin. Use the matching `Microsoft.EntityFrameworkCore.Design` version. From the plugin repository root:

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

Plugin migrations target PostgreSQL. Do not use `migrationBuilder.IsNpgsql()`, and follow PostgreSQL naming conventions. Never edit or remove a migration already shipped to users; add a forward migration instead. Test both installation into an empty database and an upgrade from the previous plugin schema when a change has meaningful data or compatibility risk.

## Run migrations

`AddPluginDbContext` runs generated EF migrations during BTCPay Server's migration startup phase. Do not add a separate hosted migration runner.

For data migrations implemented with `MigrationBase<PluginDbContext>`, register the context before the migrations so that EF creates or updates the schema first:

```csharp
serviceCollection.AddPluginDbContext<PluginDbContext>();
serviceCollection.AddMigration<PluginDbContext, NormalizeWidgetsMigration>();
```

Migration executors run in registration order. Always call `AddPluginDbContext` before any `AddMigration` calls for the same context.

Use repositories or focused data services around the context so controllers and background services do not leak context lifetimes. Do not retain a scoped context in a singleton; create a scope or use `IDbContextFactory<PluginDbContext>` for each unit of work.

Existing plugins can continue using [`BaseDbContextFactory<T>`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/Contracts/BaseDbContextFactory.cs) and a custom migration runner. New plugins should prefer `AddPluginDbContext`. See the core [database migration conventions](https://github.com/btcpayserver/btcpayserver/blob/master/docs/maintainers/README.md#database-migrations).
