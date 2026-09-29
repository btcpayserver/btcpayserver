# Local Development and Testing

## Prerequisites

- Install the .NET SDK required by `Build/Common.csproj` (currently .NET 10).
- Install Docker with Compose for the local PostgreSQL, NBXplorer, Bitcoin,
  Lightning, Tor, and Mailpit services.
- Use Visual Studio 2022 or JetBrains Rider for the repository launch profiles
  and debugging.

## Build

Build the solution directly:

```sh
dotnet build btcpayserver.sln
```

Create the release publish output used by the run scripts:

```sh
./build.sh
```

On PowerShell, use `./build.ps1`.

## Run

Start the development dependencies:

```sh
cd BTCPayServer.Tests
docker-compose up -d dev
cd ..
```

Run BTCPay Server with the `Bitcoin` launch profile:

```sh
dotnet run --project BTCPayServer/BTCPayServer.csproj --launch-profile Bitcoin
```

After running the build script, start the published application or inspect its
options:

```sh
./run.sh
./run.sh --help
```

On PowerShell, use `./run.ps1`. IDEs use the launch profiles from
`BTCPayServer/Properties/launchSettings.json`. Use `Bitcoin` for HTTP or
`Bitcoin-HTTPS` for HTTPS. The HTTPS profile requires a trusted development
certificate:

```sh
dotnet dev-certs https --trust
```

If Brave does not recognize the trusted development certificate, export its
public certificate:

```sh
dotnet dev-certs https --export-path ./aspnetcore-localhost.crt --format PEM
```

Open `brave://certificate-manager/`, select **Authorities** (or **Custom** >
**Trusted Certificates** in newer versions), and import
`aspnetcore-localhost.crt`. Enable trust for identifying websites when Brave
asks, restart the browser, and reopen the local HTTPS URL. The exported file
contains only the public certificate and can be deleted after import.

For altcoin development, start the alternate dependency environment and use
the `Altcoins` or `Altcoins-HTTPS` launch profile:

```sh
cd BTCPayServer.Tests
docker-compose -f docker-compose.altcoins.yml up -d dev
```

See [testing](#testing) for focused test commands and regtest tooling.

## Testing

### Test Environment

Start dependencies from `BTCPayServer.Tests` before running integration or
Playwright tests:

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

If the dependency environment becomes stale, run
`docker-compose down --volumes`, then `docker-compose pull` and
`docker-compose up -d dev` from `BTCPayServer.Tests`.

### Test Design

- Prefer extending an existing relevant scenario over adding a separate test.
- Exercise real browser or `BTCPayServerClient` interfaces instead of manually
  constructing controllers, unless controller internals are the subject of the
  test.
- Use Playwright's auto-waiting `Expect` assertions; do not add
  `WaitForLoadStateAsync` before them.
- Prefer `Expect` assertions such as `ToHaveCountAsync`, `ToContainTextAsync`,
  `ToHaveValueAsync`, and `ToHaveURLAsync` over manually fetching state. Add
  `using static Microsoft.Playwright.Assertions;` where needed.
- Keep one-off selectors and helpers in the test. Introduce a Page Model Object
  only for repeated component or page behavior.
- Page Model Objects should expose user-level actions and assertions and hide
  selector details.
- Prefer stable BEM class hooks for reusable frontend components.

[`BTCPayServer.Tests/README.md`](../../BTCPayServer.Tests/README.md) documents
payment simulation, Bitcoin and Lightning helper scripts, Polar, and the
altcoin test environment.
