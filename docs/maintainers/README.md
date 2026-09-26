# Maintainer Handbook

These pages document the repository-specific practices shared by maintainers and contributors. Public user and deployment documentation remains at [docs.btcpayserver.org](https://docs.btcpayserver.org/).

For vulnerability reports, follow the canonical root [security policy](../../SECURITY.md).

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

## Local Development

### Prerequisites

- Install the .NET SDK required by `Build/Common.csproj` (currently .NET 10).
- Install Docker with Compose for the local PostgreSQL, NBXplorer, Bitcoin, Lightning, Tor, and Mailpit services.
- Use Visual Studio 2022 or JetBrains Rider for the repository launch profiles and debugging.

The broader platform setup guide is in the [public local development documentation](https://docs.btcpayserver.org/Development/LocalDevelopment/).

### Build

Build the solution directly:

```sh
dotnet build btcpayserver.sln
```

Create the release publish output used by the run scripts:

```sh
./build.sh
```

On PowerShell, use `./build.ps1`.

### Run

Start the development dependencies from `BTCPayServer.Tests`:

```sh
docker-compose up -d dev
```

After running the build script, start the published application or inspect its options:

```sh
./run.sh
./run.sh --help
```

On PowerShell, use `./run.ps1`. For debugger-driven development, use the `Docker-Regtest` launch profile. The `Docker-Regtest-https` profile also requires a trusted development certificate:

```sh
dotnet dev-certs https --trust
```

See [testing](#testing) for focused test commands and regtest tooling.

## Testing

### Test Environment

Start dependencies from `BTCPayServer.Tests` before running integration or Playwright tests:

```sh
docker-compose up -d dev
```

Run tests on the host. Run the full project from the repository root with:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj
```

Run one test with its fully qualified method name:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj --filter-method BTCPayServer.Tests.BitpayTests.CanUsePairing
```

If the dependency environment becomes stale, run `docker-compose down --volumes`, then `docker-compose pull` and `docker-compose up -d dev` from `BTCPayServer.Tests`.

### Test Design

- Prefer extending an existing relevant scenario over adding a separate test.
- Exercise real browser or `BTCPayServerClient` interfaces instead of manually constructing controllers, unless controller internals are the subject of the test.
- Use Playwright's auto-waiting `Expect` assertions; do not add `WaitForLoadStateAsync` before them.
- Prefer `Expect` assertions such as `ToHaveCountAsync`, `ToContainTextAsync`,
  `ToHaveValueAsync`, and `ToHaveURLAsync` over manually fetching state. Add
  `using static Microsoft.Playwright.Assertions;` where needed.
- Keep one-off selectors and helpers in the test. Introduce a Page Model Object only for repeated component or page behavior.
- Page Model Objects should expose user-level actions and assertions and hide selector details.
- Prefer stable BEM class hooks for reusable frontend components.

[`BTCPayServer.Tests/README.md`](../../BTCPayServer.Tests/README.md) documents payment simulation, Bitcoin and Lightning helper scripts, Polar, and the altcoin test environment.

## Coding Conventions

### General

- Follow the repository `.editorconfig`; it is the source of truth for formatting and C# style.
- Prefer `Newtonsoft.Json` over `System.Text.Json` when adding or changing JSON serialization.

### Pull Requests

Write descriptions for users, merchants, operators, support contributors, translators, and reviewers who need to understand the outcome rather than the implementation.

- Explain user-visible behavior, workflows, settings, permissions, API behavior, and operational impact in plain language.
- State why the change matters and describe the practical before-and-after effect when useful.
- Mention limitations, compatibility concerns, and follow-up work that affects users or operators.
- Do not repeat the diff or include routine verification commands.
- Keep technical implementation details only when they are necessary for review or explain public behavior.
- Add screenshots for visual changes and a short video or GIF for multi-step UI flows when practical. Briefly explain when useful visual evidence cannot be included.

### Frontend Selectors

Use BEM-style classes for reusable styling, JavaScript, and Playwright hooks:
`.block`, `.block__element`, `.block--modifier`, and
`.block__element--modifier`. Use the component name as the block. Scope DOM
queries to the nearest component or form when possible.

Keep ids required for labels, ARIA and Bootstrap wiring, browser behavior, model binding, or compatibility. Even when an id remains, use a BEM class for new component selectors.

### Razor Localization

- Use `StringLocalizer` for plain text; Razor encodes the localized result.
- Use `ViewLocalizer` only when the resource intentionally contains HTML.
- Pass dynamic `ViewLocalizer` parameters through `Html.Encode(...)`.
- Do not encode intentional HTML returned by helpers such as `Html.ActionLink(...)`.

### Changelog

Record user-visible features, fixes, regressions, deprecations, removals, security-relevant behavior, and compatibility changes in `Changelog.md`. Skip internal refactors, test-only changes, tooling changes unless users or release operators are affected, and entries already covered by an earlier patch release. Put removals and deprecations under **Miscellaneous** unless another existing section is a better fit.

Use concise imperative bullets under the existing sections, preserve product terminology, wrap identifiers in backticks, and include PR numbers and contributor handles when known. When an entry begins with a titled prefix, bold only that title: `* **Title**: Description`.

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

## API Changes

The Greenfield API contract includes controller behavior, models, permissions, serialization, and the hand-maintained OpenAPI templates in `BTCPayServer/wwwroot/swagger/v1/`.

### New Endpoints

- Document every endpoint and schema in the matching `swagger.template.*.json` file.
- Assign the correct permission; introduce a permission only when no existing one fits.
- Use REST methods where practical: `POST` for creation or actions, `PUT` for full replacement, `PATCH` for partial updates, and `DELETE` for deletion or archival.
- Return validation failures as HTTP 422 with `path` and `message` entries. Return business request failures as HTTP 400 with a stable `code` and human-readable `message`.
- Register JSON converters on the model with attributes. Serialize precision-sensitive or overflow-prone values such as `decimal` and `long` as strings while accepting compatible input forms where required.

### Compatibility

Changing a property type or removing a property is breaking; version the endpoint unless compatibility can be preserved completely. Adding a required property or one without a safe default can also break clients. For additions, detect omission and retain the existing value on updates or apply a documented default on creation.

Update the matching OpenAPI template in the same pull request whenever request fields, response fields, validation, models, or behavior change. Cover compatibility and permissions with Greenfield API tests.

See [Greenfield API development](../greenfield-development.md) for detailed model-evolution examples and [authorization](../greenfield-authorization.md) for authentication flows.

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
