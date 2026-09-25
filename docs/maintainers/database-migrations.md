# Database Migrations

Entity Framework Core migrations live in `BTCPayServer.Data/Migrations` and target PostgreSQL.

## Create a Migration

1. Generate it with `dotnet ef migrations add <migration-name>`.
2. Copy the class attributes from the generated `.Designer.cs` file to the migration `.cs` file.
3. Remove the generated `.Designer.cs` file.
4. Remove the `Down()` method.
5. Review the model snapshot and generated SQL implications.

Do not use `migrationBuilder.IsNpgsql()`; migrations may assume PostgreSQL. Follow PostgreSQL naming conventions.

If Entity Framework cannot generate the required operation, add a timestamp-prefixed file in `BTCPayServer.Data/Migrations`, such as `20260525115757_passkey.cs`, and use `migrationBuilder.Sql(...)` for the raw SQL.

Test both a fresh database and an upgrade from the previous schema when the change has meaningful data or compatibility risk. The operator procedure for migrating legacy SQLite or MySQL installations is separate: [database backend migration](../operators/database-migration.md).
