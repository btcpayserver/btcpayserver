# Plugin data and migrations

Own plugin data explicitly. Do not add plugin tables or migrations to BTCPay Server's application model, and do not depend on undocumented core table layouts.

## Database context

Use a plugin-specific EF Core `DbContext` and factory. `BaseDbContextFactory<T>` applies BTCPay Server's PostgreSQL connection, retry, and migration-history conventions; pass a stable, plugin-specific migration history name. Register the factory and context through dependency injection rather than constructing connection strings yourself.

Generate migrations in the plugin repository with the .NET and EF versions selected by the [plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template). Commit migration source alongside the model. Never edit or remove a migration already shipped to users; add a forward migration.

For startup migrations, use the migration registration contracts exposed by BTCPay Server, such as `AddMigration<TDbContext, TMigration>`. The generic context must have a registered `IDbContextFactory<TDbContext>`. Keep migration identifiers unique within that context and ordered; date-prefixed identifiers are recommended for raw SQL migrations.

## Safe migration design

- Test installation into an empty database and upgrade from every supported released schema.
- Make data conversion explicit and bounded. Large rewrites should avoid holding startup locks for an unbounded period.
- Preserve rollback safety where practical, but assume a newer plugin may have changed data before an operator restores an older binary.
- Do not query core tables by assumed columns from plugin migrations unless the supported BTCPay version contract requires and tests that dependency.
- Back up production data before upgrades that transform or delete records.

Use repositories or focused data services around the context so controllers and background services do not leak context lifetimes. Do not retain a scoped context in a singleton; create a scope or use the registered factory for each unit of work.

See [`BaseDbContextFactory<T>`](https://github.com/btcpayserver/btcpayserver/blob/master/BTCPayServer.Abstractions/Contracts/BaseDbContextFactory.cs) and the core [database migration conventions](https://github.com/btcpayserver/btcpayserver/blob/master/docs/maintainers/database-migrations.md).
