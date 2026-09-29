# Maintainer Handbook

These pages document the repository-specific practices shared by maintainers and contributors. Public user and deployment documentation remains at [docs.btcpayserver.org](https://docs.btcpayserver.org/).

Changes to the documentation structure in this repository must also be reflected in the [btcpayserver-doc repository](https://github.com/btcpayserver/btcpayserver-doc) before they appear on the public documentation site.

For vulnerability reports, follow the canonical root [security policy](../../SECURITY.md).

## Guides

- [Local development and testing](local-development.md) covers prerequisites,
  builds, launch profiles, development dependencies, and test practices.
- [Coding conventions](coding-conventions.md) covers repository style, pull
  requests, frontend selectors, Razor localization, and changelog entries.
- [Greenfield API maintenance](greenfield-api.md) covers routes, authorization,
  public models, errors, compatibility, OpenAPI, client methods, and tests.
- [Configuration option maintenance](#configuration-option-maintenance) covers
  the supported configuration sources, consumers, defaults, and documentation.
- [Database migrations](#database-migrations) covers the repository-specific
  Entity Framework and PostgreSQL migration workflow.
- [Release cycles and checklist](#release-cycles) covers release types,
  responsibilities, preparation, and publication.

## Architecture

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

## Configuration Option Maintenance

Treat each supported configuration source and each consumer as part of a public startup option.

1. Choose one canonical lowercase key consistent with existing settings.
2. Register it in `DefaultConfiguration.CreateCommandLineApplicationCore()` with the correct `CommandOptionType`, an accurate description, and its default.
3. Add a commented example to `DefaultConfiguration.GetDefaultConfigurationFileTemplate()` when it helps operators.
4. Read it through `IConfiguration`, normally with `GetOrDefault<T>(key, defaultValue)`, and make the behavioral default explicit.
5. Use the existing providers rather than reading environment variables directly. The `BTCPAY_` prefix maps `exampleenabled` to `BTCPAY_EXAMPLEENABLED`.
6. Check every consumer. Disabled features must not leave background work running or UI that exposes unavailable behavior.
7. Set the option explicitly in fixtures that depend on non-default behavior; do not weaken the production default for tests.
8. Document the configuration-file key, environment variable, and command-line form. Change deployment manifests only when that deployment should opt in.

Build the affected project, run focused parsing and behavior tests, and verify the option and default in `./run.sh --help`.

## Database Migrations

Entity Framework Core migrations live in `BTCPayServer.Data/Migrations` and target PostgreSQL.

### Create a Migration

1. Generate it with `dotnet ef migrations add <migration-name>`.
2. Copy the class attributes from the generated `.Designer.cs` file to the migration `.cs` file.
3. Remove the generated `.Designer.cs` file.
4. Remove the `Down()` method.
5. Review the model snapshot and generated SQL implications.

Do not use `migrationBuilder.IsNpgsql()`; migrations may assume PostgreSQL. Follow PostgreSQL naming conventions.

If Entity Framework cannot generate the required operation, add a timestamp-prefixed file in `BTCPayServer.Data/Migrations`, such as `20260525115757_passkey.cs`, and use `migrationBuilder.Sql(...)` for the raw SQL.

Test both a fresh database and an upgrade from the previous schema when the change has meaningful data or compatibility risk.

## Release Cycles

BTCPay Server uses three release types.

### Critical Releases

Critical releases address major bugs or security vulnerabilities that require immediate attention, including newly introduced workflow blockers without an easy workaround, migration failures, and defects that make a server unusable. They are expedited and may be published immediately.

- Nicolas Dorier oversees critical releases.
- Kukks is the secondary lead.
- Pavlenex publishes announcements across communication channels.

### Minor Releases

Minor releases collect small fixes and improvements merged since the previous release. The team reaches consensus before publishing them. They are planned every two to three weeks.

- Pavlenex structures the release and assigns issues to team members.
- Nicolas Dorier and Kukks publish the GitHub release.

### Major Releases

Major releases contain significant features and enhancements and are scheduled every two to three months. They receive broader community testing, a formal announcement, and a detailed blog post.

Feature freeze starts one week before a major release. During the freeze, maintainers stop adding features and focus on testing and bug fixes. Release candidates are then published for contributor and community testing. After reported release-candidate issues are resolved, the final release is tagged and published.

Use the [release checklist](#release-checklist) when preparing any release.

## Release Checklist

### Pre-release Check

Before publishing a release candidate, run the checks tagged `PreReleaseCheck`
from the repository root:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj --filter "PreReleaseCheck=PreReleaseCheck"
```

The documentation check validates internal Markdown links and compares the
generated operator configuration reference with the application's current
command-line options. If the reference is stale, the test updates
`docs/operators/configuration-reference.md` and fails intentionally. Review and
commit the generated change, fix any reported broken links, and rerun the check
until it passes.

When creating a release:

1. Run `dotnet format` on the solution.
2. Run the `PullTransifexTranslations` test.
3. Write the release notes in `Changelog.md`.
4. Bump the version in `Build/Version.csproj`.
5. Confirm the worktree is clean and verify the intended branch, remote, HEAD,
   version, and absence of the release tag locally and remotely.
6. Ensure the release commit is GPG-signed; do not merge it through the GitHub UI.
7. Review `publish-docker.ps1` before running it. The script switches to
   `master`, tags the checked-out commit, and pushes the tag with force. Stop if
   any preflight value is unexpected or the tag already exists.
8. After CI builds the Docker images, copy the new version's changelog section into the GitHub release.

Before publishing, confirm that the release type and timing follow the [release cycle policy](#release-cycles).
