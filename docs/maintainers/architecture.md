# Architecture

BTCPay Server is an ASP.NET Core application targeting the .NET version defined in `Build/Common.csproj`. The solution is split into these main projects:

- `BTCPayServer`: Web application, controllers, views, services, configuration, built-in plugins, and static assets.
- `BTCPayServer.Data`: Entity Framework Core entities, PostgreSQL migrations, and database scripts.
- `BTCPayServer.Client`: Greenfield API client and public API models.
- `BTCPayServer.Abstractions`: Shared contracts used by the application and extensions.
- `BTCPayServer.Common`: Common infrastructure shared across projects.
- `BTCPayServer.Rating`: Exchange-rate functionality.
- `BTCPayServer.Tests`: Unit, integration, API, and Playwright tests plus the regtest dependency environment.
- `BTCPayServer.PluginPacker`: Plugin packaging tool.

The application uses PostgreSQL for persistence and NBXplorer to track blockchain activity. Bitcoin and Lightning implementations run as external services. The development test environment in `BTCPayServer.Tests/docker-compose.yml` supplies PostgreSQL, NBXplorer, Bitcoin regtest, Lightning nodes, Tor, and Mailpit.

Built-in features are organized under `BTCPayServer/Plugins`. Keep reusable contracts in the abstractions or client projects only when they are genuinely shared; otherwise, keep behavior close to its feature in the main application.
